using System;
using System.Collections.Generic;
using GlimmerGrove.Store;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// The heart containers this account has bought, and the refill cap they add up to.
    ///
    /// <para>
    /// A container is the one thing in this game bought with real money that is not currency.
    /// The rule that permits it is written out in <see cref="StoreProduct.HeartCapacity"/> and
    /// is worth restating from this side, because it is what decides the shape of everything
    /// here: <b>a capacity is an idempotent permanent entitlement rather than an amount</b>.
    /// It arrives as the union of one permanent id, so applying it twice is applying it once —
    /// which means no record of "did I already apply this transaction" has to exist, and that
    /// record is the whole of what invariant 18 was protecting against. The same property is
    /// what makes <c>CompanionLedger</c> safe, and this is that shape for the fifth time in
    /// the save file.
    /// </para>
    /// <para>
    /// <b>What is held is a set of product ids and what is derived is the cap.</b> Storing the
    /// number instead would be a second answer to a question the catalog already answers, and
    /// it would freeze a container's worth at whatever it was on the day it was bought — a
    /// rung retuned upward could then never reach the people who had already paid for it. The
    /// cap is the <em>largest</em> container held rather than the sum, so buying the rungs out
    /// of order, buying one twice through a restore, or restoring on a device that already
    /// holds a better one all resolve to the same number without a special case.
    /// </para>
    /// <para>
    /// <b>Refunds are the one thing a client-held entitlement cannot see, so they arrive
    /// separately.</b> Invariant 18c: money leaving has to be watched, and buy-refund-repeat is
    /// the commonest way a mobile economy leaks. The server revokes a refunded receipt already;
    /// what is new is that it tells the client, as a list of <em>explicitly revoked</em>
    /// product ids on every wallet reply. Note carefully what it is not: it is not "the ids the
    /// server thinks you own", and the difference is the safety property. An answer read as a
    /// whitelist would confiscate a container on any reply that was short, truncated or from an
    /// account the server had not caught up with; an explicit revocation can only ever be
    /// produced by a refund that actually happened. Both sets are monotonic and joined by
    /// union, so two devices converge on the same answer whatever order they sync in
    /// (invariant 11b), and a re-purchase after a refund lifts the revocation because
    /// <c>redeemPurchase</c> clears it in the transaction that grants.
    /// </para>
    /// <para>
    /// <b>Buying one usually hands over hearts at once, and that falls out rather than being
    /// arranged.</b> <see cref="RegenLedger.DueUnix"/> idles in the past while a player sits
    /// at their cap, so raising the cap lets the catch-up walk pay for the time they really
    /// did wait — somebody who has been full for a week arrives at their new cap immediately,
    /// and somebody who has been playing hard gets a higher ceiling and no windfall. Both are
    /// right, both are bounded by the cap that was paid for, and neither can be repeated
    /// without buying again. Do not "fix" it by restarting the clock: that would take the
    /// gift away from exactly the players who had earned it by waiting.
    /// </para>
    /// <para>
    /// It lives beside <see cref="Hearts"/> rather than with the other entitlements because
    /// <see cref="Hearts"/> is what reads it, on the HUD's own tick. The common case — an
    /// account holding no container at all — costs one integer comparison and no lookup.
    /// </para>
    /// </summary>
    public static class HeartContainerLedger
    {
        static readonly HashSet<string> _held = new HashSet<string>(StringComparer.Ordinal);
        static readonly HashSet<string> _revoked = new HashSet<string>(StringComparer.Ordinal);

        // The last answer, and enough of what it was computed from to know when it went
        // stale. See RefillCap for why this is a cache rather than a plain walk.
        static StoreCatalog _cachedAgainst;
        static int _cachedPublished, _cachedCeiling, _cachedCap;

        /// <summary>Raised when the held set changes, so a HUD can redraw its bar.</summary>
        public static event Action Changed;

        // ------------------------------------------------------------------ reading
        /// <summary>
        /// Where the refill timer stops for <em>this player</em>: the published cap, or the
        /// largest container they hold, whichever is higher.
        ///
        /// <para>
        /// Held to the published <see cref="HeartRules.Ceiling"/> as well, because a timer
        /// carrying somebody past the most they are allowed to hold would leave every grant
        /// refused while the clock kept paying — the same contradiction
        /// <c>HeartRuleTable.Resolve</c> refuses to publish. It is a bound and not a clamp on
        /// anything stored: nothing here can reach into a ledger and take a heart out of it,
        /// so lowering the ceiling from a content push still costs nobody anything.
        /// </para>
        /// <para>
        /// Never below the published cap, which is what keeps a container from becoming a
        /// downgrade if the free tuning is ever raised past one.
        /// </para>
        /// </summary>
        public static int RefillCap
        {
            get
            {
                int published = HeartRules.RefillCap;

                // The overwhelmingly common case — an account that has bought nothing — and
                // this property is read on every HUD tick through Hearts.Bounds, so it costs
                // one count and nothing else.
                if (_held.Count == 0) return published;

                // Everyone else gets a cached answer, and the cache is worth its fifteen
                // lines for one reason: the walk below is a dictionary lookup per container
                // per frame, and it would fall entirely on the players who paid. The
                // surrounding code takes a local copy of HeartRules.Table for less than
                // that.
                //
                // What it is keyed on is the point. A content push swaps in a whole new
                // immutable catalog, so a reference comparison catches a retune with no
                // event to subscribe to and no install step to forget — the same argument
                // that made the addressable registration an importer hook rather than a menu
                // item. The two published numbers are compared because a push can move them
                // without moving anything else.
                var catalog = StoreRules.Catalog;
                int ceiling = HeartRules.Ceiling;

                if (ReferenceEquals(catalog, _cachedAgainst) &&
                    published == _cachedPublished && ceiling == _cachedCeiling)
                    return _cachedCap;

                int best = published;

                foreach (var id in _held)
                {
                    if (_revoked.Contains(id)) continue;

                    var product = catalog.Find(id);

                    // An id this build's catalog does not carry is kept and ignored rather than
                    // dropped — a container bought on a newer build must survive a trip through
                    // an older one, exactly as tipsSeen and companionsOwned do.
                    if (product == null || !product.IsContainer) continue;

                    if (product.HeartCapacity > best) best = product.HeartCapacity;
                }

                if (best > ceiling) best = ceiling;
                if (best > HeartLimits.MaxRefillCap) best = HeartLimits.MaxRefillCap;
                if (best < published) best = published;

                _cachedAgainst = catalog;
                _cachedPublished = published;
                _cachedCeiling = ceiling;
                _cachedCap = best;

                return best;
            }
        }

        /// <summary>
        /// Throws the cached cap away. Called from every writer, because the cache cannot see
        /// the sets change — only the catalog and the published numbers it was keyed on.
        /// </summary>
        static void Invalidate() => _cachedAgainst = null;

        /// <summary>True when this account holds a container and it has not been refunded.</summary>
        public static bool IsHeld(string productId)
            => !string.IsNullOrEmpty(productId)
               && _held.Contains(productId) && !_revoked.Contains(productId);

        /// <summary>True when a refund has taken this container back. Drawn as "refunded", never as "buy".</summary>
        public static bool WasRevoked(string productId)
            => !string.IsNullOrEmpty(productId) && _revoked.Contains(productId);

        // ------------------------------------------------------------------ writing
        /// <summary>
        /// Records a container against a redeemed receipt, and says whether anything moved.
        ///
        /// <para>
        /// <b>Called on every successful redemption, including the ones the server reports as
        /// already granted</b>, and that is the property the whole feature rests on rather than
        /// an optimisation. A capacity is idempotent, so re-applying it is free; and both stores
        /// re-deliver a non-consumable for ever, so a player who reinstalls, switches phone or
        /// loses their save gets it back by tapping Restore — with no state of ours involved and
        /// nothing for a support case to repair. Granting only on the first delivery would make
        /// that recovery impossible for the one purchase in the shop that can never be bought
        /// again.
        /// </para>
        /// <para>
        /// A re-purchase after a refund lifts the revocation here as well as on the server, so
        /// the two answers cannot come apart while the reply that carries them is in flight.
        /// </para>
        /// </summary>
        public static bool Grant(StoreProduct product)
        {
            if (product == null || !product.IsContainer) return false;

            bool changed = _held.Add(product.Id);
            changed |= _revoked.Remove(product.Id);

            if (changed) { Invalidate(); Announce(); }
            return changed;
        }

        /// <summary>
        /// Adopts the server's list of revoked containers — receipts it granted and has since
        /// reversed, because the store refunded or charged back the payment.
        ///
        /// <para>
        /// <b>A revocation list and not an ownership list.</b> The distinction is the safety of
        /// the whole design: an absent id here means nothing at all, so a short reply, an
        /// account the server has not caught up with, or a build that predates the field can
        /// never confiscate something somebody paid for. Only a refund that actually happened
        /// produces an entry, and an entry only ever removes.
        /// </para>
        /// <para>
        /// Monotonic, so it is joined by union like every other set in this file and two devices
        /// converge whatever order they merge in. The reverse — buying a refunded container
        /// again — is not an un-revocation arriving from here; it is
        /// <see cref="Grant(StoreProduct)"/>, driven by a real receipt.
        /// </para>
        /// </summary>
        public static bool ApplyServerRevocations(IReadOnlyList<string> revokedIds)
        {
            if (revokedIds == null || revokedIds.Count == 0) return false;

            bool changed = false;
            for (int i = 0; i < revokedIds.Count; i++)
            {
                string id = revokedIds[i];
                if (string.IsNullOrEmpty(id)) continue;
                changed |= _revoked.Add(id);
            }

            if (changed) { Invalidate(); Announce(); }
            return changed;
        }

        /// <summary>
        /// The cap moved while the game is running, so everything drawing a heart bar has to
        /// hear about it.
        ///
        /// <para>
        /// Deliberately not called from <see cref="LoadFrom"/>: the load path runs this
        /// before <c>Wallet.LoadFrom</c> (see <c>SaveService</c>), so announcing there would
        /// hand every listener the outgoing account's ledger with the incoming account's cap
        /// on it — and the wallet raises its own event a few lines later anyway.
        /// </para>
        /// </summary>
        static void Announce()
        {
            Raise();

            // Through the heart event rather than a second one of our own. Every HUD in the
            // game already redraws "3 / 5" on it, so a container bought on the shop screen
            // reaches the hub, the map and the board with nothing new to subscribe to.
            Wallet.AnnounceCapacity();
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            _held.Clear();
            _revoked.Clear();
            Invalidate();

            Absorb(_held, dto?.heartContainersOwned);
            Absorb(_revoked, dto?.heartContainersRevoked);

            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.heartContainersOwned = Sorted(_held);
            dto.heartContainersRevoked = Sorted(_revoked);
        }

        /// <summary>
        /// The union of two devices' containers. Buying cannot be undone by anything a device
        /// knows about, so between them the player owns whatever either bought — and the
        /// revoked set is joined the same way, because a refund cannot be undone either.
        ///
        /// <para>
        /// No early return for an empty side, deliberately, for <c>CompanionLedger.Join</c>'s
        /// reason: handing one array straight back would skip the sort, and
        /// <see cref="SaveDelta"/> walks these in order, so an unsorted file joined against
        /// nothing would make every launch read as changed and push a write for ever.
        /// </para>
        /// </summary>
        public static string[] Join(string[] mine, string[] other)
        {
            var union = new SortedSet<string>(StringComparer.Ordinal);

            Absorb(union, mine);
            Absorb(union, other);

            var result = new string[union.Count];
            union.CopyTo(result);
            return result;
        }

        static void Absorb(ISet<string> into, string[] ids)
        {
            if (ids == null) return;

            foreach (var id in ids)
                if (!string.IsNullOrEmpty(id)) into.Add(id);
        }

        /// <summary>The held ids, sorted, for <c>CompanionLedger.Sorted</c>'s reason.</summary>
        static string[] Sorted(HashSet<string> ids)
        {
            if (ids.Count == 0) return Array.Empty<string>();

            var list = new List<string>(ids);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        /// <summary>Puts the ledger back to a fresh account's state. Tests use this.</summary>
        internal static void Reset()
        {
            _held.Clear();
            _revoked.Clear();
            Invalidate();
        }
    }
}
