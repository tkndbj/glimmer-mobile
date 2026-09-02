using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// Which parts of the floor the player owns, and the act of buying another.
    ///
    /// <para>
    /// <b>Land used to be derived and is now an entitlement, and that is the one real cost of
    /// the change.</b> An island was held when its chapter was finished — a question about the
    /// star ledger, so it recomputed on every device, survived every merge, could be retuned
    /// for players who already had it, and left nothing on disk (invariant 14). Land bought
    /// with credits cannot be any of that: nothing observable implies "this player paid 4,000
    /// for the east meadow". So it is stored, in the one shape invariant 11b permits and
    /// <c>CompanionLedger</c> and <see cref="HomesteadLedger"/> have both already proved: a set
    /// of permanent ids that only ever grows, joined by union.
    /// </para>
    /// <para>
    /// <b>Per region, never per tile.</b> Both are legal shapes; only one is a sensible size.
    /// A filled floor is several hundred tiles, and a set that large is uploaded, merged and
    /// checksummed on every sync for ever. A dozen region ids says the same thing. See
    /// <see cref="GroveRegion"/>.
    /// </para>
    /// <para>
    /// <b>The forging bound is the one every entitlement here accepts.</b> The set is
    /// client-written, so an edited save can award itself the whole floor. It buys somewhere to
    /// stand a bench on a screen nobody else sees — no currency, no progression, no advantage
    /// on a board. The money half is defended where money always is:
    /// <see cref="PlayerProgression.TrySpend"/> books an idempotent debit and <c>submitSpends</c>
    /// refuses one the server-derived balance cannot cover.
    /// </para>
    /// </summary>
    public static class GroveLand
    {
        static readonly HashSet<string> _bought = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Prefix on a purchase's spend reason. Read by support, never by code.</summary>
        public const string SpendReason = "land:";

        /// <summary>Raised when the owned set changed, so an open screen can redraw.</summary>
        public static event Action Changed;

        /// <summary>Raised on a completed purchase, for the panel doing the ceremony.</summary>
        public static event Action<GroveRegion> Bought;

        // ------------------------------------------------------------- reading
        /// <summary>
        /// Whether the player owns this region: free from the start, or bought.
        ///
        /// <b>The whole rule.</b> Nothing else composes it — invariant 15a's discipline, applied
        /// to land.
        /// </summary>
        public static bool IsOwned(GroveRegion region)
            => region != null && region.IsValid
            && (region.IsStarter || _bought.Contains(region.Id));

        /// <summary>Whether the tile at these coordinates is on land the player owns.</summary>
        public static bool IsOwned(GroveFloor floor, int col, int row)
            => floor != null && floor.Contains(col, row) && IsOwned(floor.RegionOf(col, row));

        /// <summary>
        /// Whether a tile can be built on. Owned land, and not the hall's own tile.
        ///
        /// The hall is drawn from what the player owns rather than placed, so its tile is the
        /// one square on the floor that is not a slot — the hearth's rule, carried over.
        /// </summary>
        public static bool IsBuildable(GroveFloor floor, int col, int row)
            => IsOwned(floor, col, row) && !floor.IsHall(GroveFloor.TileId(col, row));

        /// <summary>How many regions were paid for. See <c>CompanionLedger.BoughtCount</c>.</summary>
        public static int BoughtCount => _bought.Count;

        /// <summary>Whether this region was paid for, for a panel that wants to say so.</summary>
        public static bool WasBought(GroveRegion region)
            => region != null && region.IsValid && _bought.Contains(region.Id);

        /// <summary>How many regions are open, for the "3 of 12" caption.</summary>
        public static int OwnedCount(GroveFloor floor)
        {
            if (floor == null) return 0;

            int count = 0;
            foreach (var region in floor.Regions)
                if (IsOwned(region)) count++;

            return count;
        }

        /// <summary>How many tiles the player can actually build on right now.</summary>
        public static int OwnedTileCount(GroveFloor floor)
        {
            if (floor == null) return 0;

            int count = 0;
            foreach (var region in floor.Regions)
                if (IsOwned(region)) count += region.TileCount;

            return count;
        }

        /// <summary>
        /// The box of tiles the player owns, in tile coordinates. False when they own none.
        ///
        /// <para>
        /// Walked over <em>regions</em> rather than over tiles, which is the whole point of it.
        /// The field is allowed to be 200x200, so measuring the owned area by asking every tile
        /// whether it is owned is forty thousand predicate calls every time somebody buys
        /// something — cheap at the shipped 14x14 and quietly quadratic in the size of the
        /// floor. There are nine regions.
        /// </para>
        /// </summary>
        public static bool OwnedBounds(GroveFloor floor,
                                       out int minCol, out int minRow, out int maxCol, out int maxRow)
        {
            minCol = minRow = int.MaxValue;
            maxCol = maxRow = int.MinValue;

            if (floor == null) return false;

            foreach (var region in floor.Regions)
            {
                if (!IsOwned(region)) continue;

                if (region.Col < minCol) minCol = region.Col;
                if (region.Row < minRow) minRow = region.Row;
                if (region.Col + region.Cols - 1 > maxCol) maxCol = region.Col + region.Cols - 1;
                if (region.Row + region.Rows - 1 > maxRow) maxRow = region.Row + region.Rows - 1;
            }

            return minCol <= maxCol;
        }

        /// <summary>
        /// The one region the player may buy next, or null when the whole floor is owned.
        ///
        /// <para>
        /// <b>The floor is a ladder, not a shelf.</b> The lowest unowned rung is the only thing
        /// for sale; everything above it waits. That is a design decision — a stretch of ground
        /// is the most expensive thing in the shop and the least legible, so a wall of nine
        /// prices asks a new keeper to compare rectangles they cannot yet picture, where one
        /// offer at a time is a next step. It is also what makes two currencies coherent: the
        /// rung is authored (<see cref="GroveRegion.Order"/>), so the credit stretches come
        /// first and the gem ones after, and no comparison between the two ever has to be made.
        /// </para>
        /// <para>
        /// <b>Ranked on the rung and never on the price</b>, which is the half that would rot.
        /// This used to answer the cheapest unowned region, correct while one currency priced
        /// the whole floor and quietly wrong the moment 600 gems and 5,000 credits were both
        /// "cost" — it would have sold the gem land first and for nothing, since a gem region's
        /// <c>Cost</c> is zero. A ladder read off a price also reorders itself on a retune,
        /// under players part-way up it.
        /// </para>
        /// </summary>
        public static GroveRegion NextForSale(GroveFloor floor)
        {
            GroveRegion best = null;
            if (floor == null) return null;

            foreach (var region in floor.Regions)
            {
                if (!region.IsValid || region.IsStarter || IsOwned(region)) continue;
                if (best == null || Rung(region) < Rung(best)) best = region;
            }

            return best;
        }

        /// <summary>
        /// A region's place in the queue. An unauthored rung sorts last rather than first, so a
        /// content mistake leaves a stretch unreachable — which somebody notices — instead of
        /// jumping it to the front of the ladder, which nobody would.
        /// </summary>
        public static int Rung(GroveRegion region)
            => region == null || region.Order <= 0 ? int.MaxValue : region.Order;

        /// <summary>
        /// Whether this region is the rung on offer: unowned, and with nothing cheaper still
        /// unbought.
        ///
        /// Asked of the floor rather than of the region alone, because "next" is a fact about
        /// what else the player holds.
        /// </summary>
        public static bool IsNext(GroveFloor floor, GroveRegion region)
            => region != null && region.IsValid && !IsOwned(region)
            && ReferenceEquals(NextForSale(floor), region);

        /// <summary>
        /// What this region costs the player right now, and why they might not be able to pay.
        ///
        /// <para>
        /// Reuses <see cref="HomesteadOffer"/> rather than inventing a near-identical enum: the
        /// states a purchase can be in are the same states whatever is being sold, and a screen
        /// showing land beside decor should render one refusal, not two.
        /// </para>
        /// <para>
        /// <b>The balance quoted is the one this region is actually bought with.</b> A gem
        /// stretch measured against the credit wallet is a button that says a player can afford
        /// something they cannot, and — because credits outnumber gems by roughly a hundred to
        /// one here — it would have said so for nearly everybody.
        /// </para>
        /// <para>
        /// <b>The ladder is asked before the price</b>, which is invariant 15a's ordering and
        /// for its reason: a keeper who is both several rungs down and short of gems should be
        /// told about the wall money cannot climb, not quoted a price that would not have
        /// helped. It needs the floor, so a caller with only a region gets the rest of the
        /// answer and no ladder check — every screen here has the catalog open.
        /// </para>
        /// </summary>
        public static HomesteadOffer OfferFor(GroveRegion region, GroveFloor floor = null)
        {
            if (region == null || !region.IsValid)
                return new HomesteadOffer(HomesteadPurchaseState.NotForSale, 0L,
                                          PlayerProgression.Credits);

            long balance = PlayerProgression.Balance(region.PaidIn);

            if (IsOwned(region))
                return new HomesteadOffer(HomesteadPurchaseState.AlreadyHeld, region.Price, balance);

            if (floor != null && !IsNext(floor, region))
                return new HomesteadOffer(HomesteadPurchaseState.EarlierFirst, region.Price, balance);

            var state = balance >= region.Price
                ? HomesteadPurchaseState.Ready
                : HomesteadPurchaseState.TooExpensive;

            return new HomesteadOffer(state, region.Price, balance);
        }

        // ------------------------------------------------------------- writing
        /// <summary>
        /// Buys a region, debiting credits and recording it as owned.
        ///
        /// The debit goes first and the id is only added if it succeeded, which is
        /// <c>HomesteadLedger.TryBuy</c>'s order and for its reason: a process killed between
        /// the two leaves a player who paid and did not receive, which support can see in the
        /// spend log and put right, where the other order leaves land nobody paid for and
        /// nothing to distinguish it from a forgery.
        /// </summary>
        public static bool TryBuy(GroveRegion region, GroveFloor floor = null)
        {
            var offer = OfferFor(region, floor);
            if (!offer.CanBuy) return false;

            // Whichever wallet the region is priced in. Never Currency.Credits by name: the
            // debit and the button's caption have to come from one reading of the region, or
            // the shop quotes gems and the ledger takes coins.
            if (!PlayerProgression.TrySpend(region.PaidIn, offer.Cost, SpendReason + region.Id))
                return false;

            _bought.Add(region.Id);

            Telemetry.Track("grove_land_bought", "region", region.Id,
                            "cost", offer.Cost, "currency", region.PaidIn);

            SaveService.Save();
            Raise();

            try { Bought?.Invoke(region); }
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

            var ids = dto?.groveLandOwned;
            if (ids != null)
                foreach (var id in ids)
                    if (!string.IsNullOrEmpty(id)) _bought.Add(id);

            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.groveLandOwned = Sorted(_bought);
        }

        /// <summary>
        /// The union of two devices' land. Buying cannot be undone, so between them the player
        /// owns whatever either bought.
        ///
        /// No early return for an empty side, deliberately — the trap
        /// <c>HomesteadLedger.Join</c> documents. Handing one array straight back would skip the
        /// sort, so an unsorted file joined against nothing would stay unsorted, and
        /// <see cref="SaveDelta"/> walks these in order: every launch would then read as changed
        /// and push a write for nothing, for ever.
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
