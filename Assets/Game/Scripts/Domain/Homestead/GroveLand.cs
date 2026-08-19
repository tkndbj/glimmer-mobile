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
        /// The cheapest region still for sale, or null when the whole floor is owned. Drives
        /// the screen's "expand" prompt.
        /// </summary>
        public static GroveRegion NextForSale(GroveFloor floor)
        {
            GroveRegion best = null;
            if (floor == null) return null;

            foreach (var region in floor.Regions)
            {
                if (!region.IsValid || IsOwned(region)) continue;
                if (best == null || region.Cost < best.Cost) best = region;
            }

            return best;
        }

        /// <summary>
        /// What this region costs the player right now, and why they might not be able to pay.
        ///
        /// Reuses <see cref="HomesteadOffer"/> rather than inventing a fourth near-identical
        /// enum: the four states a purchase can be in are the same four whatever is being sold,
        /// and a screen showing land beside decor should render one refusal, not two.
        /// </summary>
        public static HomesteadOffer OfferFor(GroveRegion region)
        {
            long balance = PlayerProgression.Credits;

            if (region == null || !region.IsValid)
                return new HomesteadOffer(HomesteadPurchaseState.NotForSale, 0L, balance);

            if (IsOwned(region))
                return new HomesteadOffer(HomesteadPurchaseState.AlreadyHeld, region.Cost, balance);

            var state = balance >= region.Cost
                ? HomesteadPurchaseState.Ready
                : HomesteadPurchaseState.TooExpensive;

            return new HomesteadOffer(state, region.Cost, balance);
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
        public static bool TryBuy(GroveRegion region)
        {
            var offer = OfferFor(region);
            if (!offer.CanBuy) return false;

            if (!PlayerProgression.TrySpend(Currency.Credits, offer.Cost, SpendReason + region.Id))
                return false;

            _bought.Add(region.Id);

            Telemetry.Track("grove_land_bought", "region", region.Id, "cost", offer.Cost);

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
