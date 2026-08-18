using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Homestead
{
    /// <summary>Why a piece cannot be bought right now, or that it can.</summary>
    public enum HomesteadPurchaseState
    {
        /// <summary>Affordable, unheld and for sale. The only state a buy button is live in.</summary>
        Ready,

        /// <summary>Already held — earned by playing, bought earlier, or free from the start.</summary>
        AlreadyHeld,

        /// <summary>The catalog does not price this one. Every resident is here, always.</summary>
        NotForSale,

        /// <summary>For sale, unheld, and the player is short. Carries the shortfall.</summary>
        TooExpensive,
    }

    /// <summary>What a piece costs this player right now, and whether they can pay it.</summary>
    public readonly struct HomesteadOffer
    {
        public readonly HomesteadPurchaseState State;

        /// <summary>The price, or 0 when there is none.</summary>
        public readonly long Cost;

        /// <summary>Credits the player is holding, for a panel that shows the gap.</summary>
        public readonly long Balance;

        public HomesteadOffer(HomesteadPurchaseState state, long cost, long balance)
        {
            State = state;
            Cost = cost;
            Balance = balance;
        }

        public bool CanBuy => State == HomesteadPurchaseState.Ready;

        /// <summary>How many credits short, or 0 when the player can pay.</summary>
        public long Shortfall => Cost > Balance ? Cost - Balance : 0L;
    }

    /// <summary>
    /// Which pieces and plots this player holds, and the act of buying one.
    ///
    /// <para>
    /// <b>The rule is requirement OR purchase, and it lives here alone.</b> Every screen
    /// asks <see cref="IsHeld(HomesteadPiece)"/>; nothing else composes the two halves.
    /// That is invariant 15a, learned on companions: <c>AvatarCatalog.ReachedBy</c> had to
    /// be renamed away from <c>IsUnlocked</c> because a call site checking half a rule under
    /// a name promising all of it is precisely how something somebody paid for stays behind
    /// a padlock. Nothing here is called <c>IsUnlocked</c> for the same reason.
    /// </para>
    /// <para>
    /// <b>The earned half is derived; the bought half is stored as a union-joined set.</b>
    /// A requirement is a question about the star ledger, so it recomputes on every device,
    /// survives every merge, cannot be lost, and can be retuned for players who already
    /// have it — invariant 14, and the reason plots and residents cost the save file
    /// nothing at all. A purchase cannot be derived from anything observable, so it is
    /// stored, in the one shape invariant 11b permits and <c>CompanionLedger</c> already
    /// proved: a set of permanent ids that only ever grows.
    /// </para>
    /// <para>
    /// <b>What a purchase buys is permission, not a copy.</b> Holding <c>fence_low</c> means
    /// every slot in the grove may draw a low fence — one of them, or all of them. See
    /// <see cref="HomesteadPiece"/> for why a count of copies is not merely undesirable but
    /// unrepresentable in a file that has to merge across devices, and why the shop is
    /// better for it.
    /// </para>
    /// <para>
    /// <b>The forging bound is the same one companions accept.</b> The set is client-written,
    /// so an edited save can award itself a bench. It buys a picture on a screen nobody else
    /// sees: no currency, no progression, no advantage on a board. The money half is defended
    /// where money always is — <see cref="PlayerProgression.TrySpend"/> books an idempotent
    /// debit and <c>submitSpends</c> refuses one the server-derived balance cannot cover.
    /// Nothing here needs adjudicating, which is why nothing adjudicates it.
    /// </para>
    /// </summary>
    public static class HomesteadLedger
    {
        static readonly HashSet<string> _bought = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Prefix on a purchase's spend reason. Read by support, never by code.</summary>
        public const string SpendReason = "grove:";

        /// <summary>Raised when the held set changed, so an open screen can redraw.</summary>
        public static event Action Changed;

        /// <summary>Raised on a completed purchase, for the panel doing the ceremony.</summary>
        public static event Action<HomesteadPiece> Bought;

        // ------------------------------------------------------------- reading
        /// <summary>
        /// Whether the player holds this piece: its requirement met, or bought, or free.
        ///
        /// <b>The whole unlock rule.</b> Nothing else composes it.
        /// </summary>
        public static bool IsHeld(HomesteadPiece piece)
        {
            if (!piece.IsValid) return false;
            if (_bought.Contains(piece.Id)) return true;
            return IsEarned(piece);
        }

        public static bool IsHeld(string id) => IsHeld(HomesteadCatalog.Current.Find(id));

        /// <summary>
        /// Whether play alone has earned this piece.
        ///
        /// <b>Half of the rule.</b> Named for its narrowness on purpose — see the type's
        /// remarks, and invariant 15a for the bug this naming exists to prevent. A panel
        /// that wants to say "you unlocked this by finishing Thorn Hollow" reads this; a
        /// panel deciding whether to draw a padlock reads <see cref="IsHeld(HomesteadPiece)"/>.
        /// </summary>
        public static bool IsEarned(HomesteadPiece piece)
        {
            if (!piece.IsValid) return false;

            // Nothing asked of it and no price: the starter furniture a new grove opens with.
            if (!piece.HasRequirement) return !piece.IsForSale;

            if (piece.RequiresLevel.IsValid && HomesteadProgress.IsCleared(piece.RequiresLevel))
                return true;

            return piece.RequiresChapter.IsValid
                && HomesteadProgress.IsChapterFinished(piece.RequiresChapter);
        }

        /// <summary>
        /// Whether this piece was paid for.
        ///
        /// Only for a panel that wants to say so. Never a substitute for
        /// <see cref="IsHeld(HomesteadPiece)"/>: most of the roster is earned and was never
        /// bought, so gating anything on this locks the grove.
        /// </summary>
        public static bool WasBought(HomesteadPiece piece)
            => piece.IsValid && _bought.Contains(piece.Id);

        /// <summary>Whether this plot's land has been earned. Derived; there is nothing stored.</summary>
        public static bool IsPlotHeld(HomesteadPlot plot)
            => plot != null && plot.IsValid
            && (plot.IsStarter || HomesteadProgress.IsChapterFinished(plot.RequiresChapter));

        /// <summary>How many pieces the player holds, for the "18 of 46" caption.</summary>
        public static int HeldCount(HomesteadCatalog catalog)
        {
            if (catalog == null) return 0;

            int count = 0;
            foreach (var piece in catalog.Pieces)
                if (IsHeld(piece)) count++;

            return count;
        }

        /// <summary>How many plots are open, for the same reason.</summary>
        public static int HeldPlotCount(HomesteadCatalog catalog)
        {
            if (catalog == null) return 0;

            int count = 0;
            foreach (var plot in catalog.Plots)
                if (IsPlotHeld(plot)) count++;

            return count;
        }

        // ------------------------------------------------------------ the home
        /// <summary>
        /// The best home this player owns, or an invalid piece when the catalog has none.
        ///
        /// <para>
        /// <b>The whole dwelling rule.</b> The hearth draws this; nothing is ever placed on
        /// it. Because the ladder is a set of ids in the same union-joined set as every other
        /// purchase, "best owned" is a maximum over what is held — which makes it idempotent,
        /// order-independent and impossible to lose in a merge, exactly like every other
        /// derived answer in this feature. See <see cref="HomesteadPieceKind.Dwelling"/> for
        /// why it is not a stored level.
        /// </para>
        /// <para>
        /// Ties on tier are broken by catalog order, which is arbitrary and <em>stable</em> —
        /// the property that matters, since two devices must draw the same home.
        /// </para>
        /// </summary>
        public static HomesteadPiece BestDwelling(HomesteadCatalog catalog)
        {
            var best = default(HomesteadPiece);
            if (catalog == null) return best;

            foreach (var piece in catalog.Pieces)
            {
                if (!piece.IsDwelling || !IsHeld(piece)) continue;
                if (!best.IsValid || piece.Tier > best.Tier) best = piece;
            }

            return best;
        }

        /// <summary>
        /// The next rung of the home ladder: the lowest tier above the best one held.
        ///
        /// Invalid when the player is at the top, which is what the panel renders as "this is
        /// the finest home in the grove" rather than as a dead button.
        /// </summary>
        public static HomesteadPiece NextDwelling(HomesteadCatalog catalog)
        {
            if (catalog == null) return default;

            int held = BestDwelling(catalog).Tier;
            var next = default(HomesteadPiece);

            foreach (var piece in catalog.Pieces)
            {
                if (!piece.IsDwelling || piece.Tier <= held || IsHeld(piece)) continue;
                if (!next.IsValid || piece.Tier < next.Tier) next = piece;
            }

            return next;
        }

        /// <summary>How many rungs the home ladder has, for the pips on the home panel.</summary>
        public static int DwellingCount(HomesteadCatalog catalog)
        {
            if (catalog == null) return 0;

            int count = 0;
            foreach (var piece in catalog.Pieces)
                if (piece.IsDwelling) count++;

            return count;
        }

        /// <summary>
        /// The cheapest piece still for sale that the player does not hold, or an invalid one
        /// when there is nothing left to sell. Drives the shop's "next" prompt.
        /// </summary>
        public static HomesteadPiece CheapestUnheld(HomesteadCatalog catalog)
        {
            var best = default(HomesteadPiece);
            if (catalog == null) return best;

            foreach (var piece in catalog.Pieces)
            {
                if (!piece.IsForSale) continue;
                if (IsHeld(piece)) continue;
                if (!best.IsValid || piece.Cost < best.Cost) best = piece;
            }

            return best;
        }

        /// <summary>
        /// What this piece would cost the player right now, and why it might not be buyable.
        ///
        /// Every refusal is a distinct member because each one renders a different sentence —
        /// the bargain <c>AdOfferState</c> and <c>CompanionOffer</c> both make. A single
        /// "unavailable" would draw the same greyed button for a player 300 credits short and
        /// for a resident that is not for sale at any price, and one of those resolves by
        /// playing for an hour while the other never resolves at all.
        /// </summary>
        public static HomesteadOffer OfferFor(HomesteadPiece piece)
        {
            long balance = PlayerProgression.Credits;

            if (!piece.IsValid)
                return new HomesteadOffer(HomesteadPurchaseState.NotForSale, 0L, balance);

            if (IsHeld(piece))
                return new HomesteadOffer(HomesteadPurchaseState.AlreadyHeld, piece.Cost, balance);

            if (!piece.IsForSale)
                return new HomesteadOffer(HomesteadPurchaseState.NotForSale, 0L, balance);

            var state = balance >= piece.Cost
                ? HomesteadPurchaseState.Ready
                : HomesteadPurchaseState.TooExpensive;

            return new HomesteadOffer(state, piece.Cost, balance);
        }

        // ------------------------------------------------------------- writing
        /// <summary>
        /// Buys a piece, debiting credits and recording it as held.
        ///
        /// <para>
        /// The debit goes first and the id is only added if it succeeded, which is the
        /// opposite of the tempting order and deliberate — <c>CompanionLedger.TryBuy</c> makes
        /// the argument. A process killed between the two leaves a player who paid and did not
        /// receive, which support can see in the spend log and put right; the other order
        /// leaves a piece nobody paid for, indistinguishable from the forgery this tolerates
        /// and therefore invisible.
        /// </para>
        /// <para>
        /// Re-entrancy is handled by the held check rather than by a flag: a double tap finds
        /// the piece already held on the second pass and returns false without charging.
        /// </para>
        /// </summary>
        public static bool TryBuy(HomesteadPiece piece)
        {
            var offer = OfferFor(piece);
            if (!offer.CanBuy) return false;

            if (!PlayerProgression.TrySpend(Currency.Credits, offer.Cost, SpendReason + piece.Id))
                return false;

            _bought.Add(piece.Id);

            Telemetry.Track("grove_piece_bought", "piece", piece.Id, "cost", offer.Cost,
                            "kind", piece.Kind.ToString());

            // TrySpend already wrote the debit. This write carries the id that debit paid for,
            // and losing it is the failure described above.
            SaveService.Save();
            Raise();

            try { Bought?.Invoke(piece); }
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

            var ids = dto?.homesteadOwned;
            if (ids != null)
                foreach (var id in ids)
                    if (!string.IsNullOrEmpty(id)) _bought.Add(id);

            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.homesteadOwned = Sorted(_bought);
        }

        /// <summary>
        /// The union of two devices' purchases. Buying cannot be undone, so between them the
        /// player owns whatever either bought.
        ///
        /// <para>
        /// No early return for an empty side, deliberately — the trap <c>EventCollection.Join</c>
        /// and <c>CompanionLedger.Join</c> both document. Handing one array straight back would
        /// skip the sort, so an unsorted file joined against nothing would come out still
        /// unsorted, and <see cref="SaveDelta"/> walks these in order: every launch would then
        /// read as changed and push a write for nothing, forever.
        /// </para>
        /// <para>
        /// Unknown ids are kept, exactly as <c>tipsSeen</c> and <c>companionsOwned</c> keep
        /// theirs: a piece bought on a newer build must not be confiscated by a trip through an
        /// older one, and an id this build does not recognise costs one short string.
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
        /// The held ids, sorted. Not tidiness — <see cref="SaveChecksum"/> hashes the
        /// serialised file and <see cref="SaveDelta"/> walks these in order, so hash-set order
        /// would make an unchanged save look changed on every launch.
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
