using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>Why a companion cannot be bought right now, or that it can.</summary>
    public enum CompanionPurchaseState
    {
        /// <summary>Affordable, unheld and for sale. The only state a buy button is live in.</summary>
        Ready,

        /// <summary>Already held — reached by level, or bought earlier.</summary>
        AlreadyHeld,

        /// <summary>The roster does not price this one: it is earned by playing or not at all.</summary>
        NotForSale,

        /// <summary>For sale, unheld, and the player is short. Carries the shortfall.</summary>
        TooExpensive,
    }

    /// <summary>What a companion costs this player right now, and whether they can pay it.</summary>
    public readonly struct CompanionOffer
    {
        public readonly CompanionPurchaseState State;

        /// <summary>The price, or 0 when there is none.</summary>
        public readonly long Cost;

        /// <summary>Credits the player is holding, for a panel that shows the gap.</summary>
        public readonly long Balance;

        public CompanionOffer(CompanionPurchaseState state, long cost, long balance)
        {
            State = state;
            Cost = cost;
            Balance = balance;
        }

        public bool CanBuy => State == CompanionPurchaseState.Ready;

        /// <summary>How many credits short, or 0 when the player can pay.</summary>
        public long Shortfall => Cost > Balance ? Cost - Balance : 0L;
    }

    /// <summary>
    /// Which companions this player holds, and the act of buying one.
    ///
    /// <para>
    /// <b>The rule is level OR purchase, and it lives here alone.</b> Every screen asks
    /// <see cref="IsHeld(AvatarDefinition, int)"/>; nothing else composes the two halves.
    /// <see cref="AvatarCatalog.ReachedBy"/> answers only the level half and is named so
    /// that reading it as the whole rule is obviously wrong — it used to be called
    /// <c>IsUnlocked</c>, and a call site checking half the rule under a name that promises
    /// all of it is precisely how a companion somebody paid for stays behind a padlock.
    /// </para>
    /// <para>
    /// <b>Why the purchased half is stored when nothing else here is.</b> This file's
    /// neighbours go to some length to derive rather than store — XP, credits, the heart
    /// count, an event's payout — and every one of them can, because each is a function of
    /// facts already in the save. A purchase is not. Nothing observable implies "this player
    /// paid 8,000 credits for Coral": the spend that bought it is in the currency ledger,
    /// but a ledger records amounts and reasons, not entitlements, and mining a purchase back
    /// out of a debit's <c>reason</c> string would make a support tool's free-text field
    /// load-bearing. So it is stored, and stored in the one shape invariant 11b allows.
    /// </para>
    /// <para>
    /// <b>A set of permanent ids, joined by union.</b> Buying is irreversible, so between two
    /// devices the player owns whatever either of them bought — the join is idempotent,
    /// commutative and associative without trying, exactly like <see cref="TipLedger"/>. Note
    /// what this is <em>not</em>: a count of companions owned would be hearts' old mistake
    /// (two devices at 4 and 2 are equally consistent with "one is behind" and "one bought
    /// two more"), and a per-companion flag with a false state would be unable to tell "not
    /// bought" from "written by a build that did not know this companion existed". A set has
    /// neither problem, because absence and "not bought" are the same fact.
    /// </para>
    /// <para>
    /// <b>Unknown ids are kept.</b> A companion bought on a newer build must not be
    /// confiscated by a trip through an older one, and an id this build does not recognise
    /// costs one short string. That is <see cref="TipLedger"/>'s rule for the same reason.
    /// </para>
    /// <para>
    /// <b>The forging bound.</b> The set is client-written, so a player who edits their save
    /// can award themselves a companion. It buys a cosmetic and nothing else: no currency, no
    /// progression, no advantage on a board. The money half is the part that is defended, and
    /// it is defended where money always is — <see cref="PlayerProgression.TrySpend"/> books
    /// a debit with an idempotency key, <c>submitSpends</c> refuses one the balance cannot
    /// cover, and the balance is server-derived. So the worst outcome is a forged save wearing
    /// a portrait it did not pay for, which is the same class of loss as the forgeable run
    /// counter and priced accordingly. Nothing here needs the server to adjudicate it, which
    /// is why nothing does.
    /// </para>
    /// </summary>
    public static class CompanionLedger
    {
        static readonly HashSet<string> _bought = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Prefix on a purchase's spend reason. Read by support, never by code.</summary>
        public const string SpendReason = "companion:";

        /// <summary>Raised when the held set changed, so an open screen can redraw.</summary>
        public static event Action Changed;

        /// <summary>Raised on a completed purchase, for the panel doing the ceremony.</summary>
        public static event Action<AvatarDefinition> Bought;

        // ------------------------------------------------------------- reading
        /// <summary>
        /// Whether the player holds this companion: reached by keeper level, or bought.
        ///
        /// <b>The whole unlock rule.</b> Nothing else composes it.
        /// </summary>
        public static bool IsHeld(AvatarDefinition avatar, int keeperLevel)
            => avatar.IsValid
            && (AvatarCatalog.ReachedBy(avatar, keeperLevel) || _bought.Contains(avatar.Id));

        public static bool IsHeld(string id, int keeperLevel) => IsHeld(AvatarCatalog.Find(id), keeperLevel);

        /// <summary>
        /// Whether this companion was paid for.
        ///
        /// Only for a panel that wants to say so. Never a substitute for
        /// <see cref="IsHeld(AvatarDefinition, int)"/>: a companion reached by level is held
        /// and was never bought, so gating anything on this locks most of the roster.
        /// </summary>
        public static bool WasBought(AvatarDefinition avatar)
            => avatar.IsValid && _bought.Contains(avatar.Id);

        /// <summary>How many the player holds, for the "12 of 31 awake" caption.</summary>
        public static int HeldCount(int keeperLevel)
        {
            int count = 0;

            foreach (var avatar in AvatarCatalog.All)
                if (IsHeld(avatar, keeperLevel)) count++;

            return count;
        }

        /// <summary>
        /// The next companion the keeper level will reach that the player does not already
        /// hold, or an invalid one when there is nothing ahead.
        ///
        /// <para>
        /// Skipping the ones already bought is the whole reason this is not
        /// <c>AvatarCatalog.NextLocked</c> any more. <see cref="UnlockGoal"/> points the hub's
        /// progress bar at whatever comes back, and aiming it at a companion the player is
        /// already wearing is worse than showing no goal at all — it tells somebody who just
        /// spent 9,000 credits that they have four ranks to climb for what they are looking at.
        /// </para>
        /// </summary>
        public static AvatarDefinition NextUnheld(int keeperLevel)
        {
            var best = default(AvatarDefinition);

            foreach (var avatar in AvatarCatalog.All)
            {
                if (avatar.UnlockLevel <= keeperLevel) continue;   // already reached
                if (_bought.Contains(avatar.Id)) continue;         // already paid for
                if (!best.IsValid || avatar.UnlockLevel < best.UnlockLevel) best = avatar;
            }

            return best;
        }

        /// <summary>The cheapest companion still for sale that the player does not hold.</summary>
        public static AvatarDefinition CheapestForSale(int keeperLevel)
            => AvatarCatalog.CheapestUnheld(avatar => IsHeld(avatar, keeperLevel));

        /// <summary>
        /// What this companion would cost the player right now, and why it might not be
        /// buyable.
        ///
        /// <para>
        /// Every refusal is a distinct member because each one renders a different sentence,
        /// and the panel is built from them — the same bargain <c>AdOfferState</c> makes. A
        /// single "unavailable" would mean a player short of 300 credits and a player looking
        /// at a companion that is not for sale read the same greyed button, and one of those
        /// resolves by playing for an hour while the other never resolves at all.
        /// </para>
        /// </summary>
        public static CompanionOffer OfferFor(AvatarDefinition avatar, int keeperLevel)
        {
            long balance = PlayerProgression.Credits;

            if (!avatar.IsValid)
                return new CompanionOffer(CompanionPurchaseState.NotForSale, 0L, balance);

            if (IsHeld(avatar, keeperLevel))
                return new CompanionOffer(CompanionPurchaseState.AlreadyHeld, avatar.UnlockCost, balance);

            if (!avatar.IsForSale)
                return new CompanionOffer(CompanionPurchaseState.NotForSale, 0L, balance);

            var state = balance >= avatar.UnlockCost
                ? CompanionPurchaseState.Ready
                : CompanionPurchaseState.TooExpensive;

            return new CompanionOffer(state, avatar.UnlockCost, balance);
        }

        // ------------------------------------------------------------- writing
        /// <summary>
        /// Buys a companion, debiting credits and recording it as held.
        ///
        /// <para>
        /// The order matters and is the opposite of the tempting one: the debit goes first,
        /// and the id is only added if it succeeded. A process killed between the two leaves a
        /// player who paid and did not receive, which a support tool can see in the spend log
        /// and put right; the other order leaves a companion nobody paid for, which is
        /// indistinguishable from the forgery this deliberately tolerates and therefore
        /// invisible. Given a choice about which way an interrupted purchase fails, it fails
        /// toward the recoverable one.
        /// </para>
        /// <para>
        /// Re-entrancy is handled by the held check rather than by a flag: a double tap finds
        /// the companion already held on the second pass and returns false without charging.
        /// </para>
        /// </summary>
        public static bool TryBuy(AvatarDefinition avatar, int keeperLevel)
        {
            var offer = OfferFor(avatar, keeperLevel);
            if (!offer.CanBuy) return false;

            if (!PlayerProgression.TrySpend(Persistence.Currency.Credits, offer.Cost,
                                            SpendReason + avatar.Id))
                return false;

            _bought.Add(avatar.Id);

            Telemetry.Track("companion_bought", "companion", avatar.Id, "cost", offer.Cost,
                            "level", keeperLevel);

            // TrySpend already wrote the debit. This write carries the id that the debit
            // paid for, and losing it would be the failure described above.
            SaveService.Save();
            Raise();

            try { Bought?.Invoke(avatar); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }

            return true;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            _bought.Clear();

            var ids = dto?.companionsOwned;
            if (ids != null)
                foreach (var id in ids)
                    if (!string.IsNullOrEmpty(id)) _bought.Add(id);

            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.companionsOwned = Sorted(_bought);
        }

        /// <summary>
        /// The union of two devices' purchases. Buying cannot be undone, so between them the
        /// player owns whatever either bought.
        ///
        /// <para>
        /// No early return for an empty side, deliberately — the same trap
        /// <c>EventCollection.Join</c> documents. Handing one array straight back would skip
        /// the sort, so an unsorted file joined against nothing would come out still unsorted,
        /// and <see cref="SaveDelta"/> walks these in order: every launch would then read as
        /// changed and push a write for nothing, forever.
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

        static void Absorb(SortedSet<string> into, string[] ids)
        {
            if (ids == null) return;

            foreach (var id in ids)
                if (!string.IsNullOrEmpty(id)) into.Add(id);
        }

        /// <summary>
        /// The held ids, sorted.
        ///
        /// Not tidiness. <see cref="SaveChecksum"/> hashes the serialised file and
        /// <see cref="SaveDelta"/> decides whether to sync by walking these in order, so ids
        /// in hash-set order would make an unchanged save look changed on every launch.
        /// </summary>
        static string[] Sorted(HashSet<string> ids)
        {
            if (ids.Count == 0) return Array.Empty<string>();

            var list = new List<string>(ids);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        /// <summary>Test seam: forgets every purchase, as a fresh install would.</summary>
        internal static void ResetForTests() => _bought.Clear();
    }
}
