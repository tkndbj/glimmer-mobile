using System;
using System.Collections.Generic;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// One thing standing in the grove: its anchor tile, what it is, which way it faces, and
    /// the footprint it therefore covers.
    ///
    /// The footprint is already facing the right way — see <see cref="GroveFootprint.Facing"/>
    /// — so a reader never has to remember to mirror it. Everything a screen draws or a picker
    /// tests is derived from the four stored facts here and nothing else.
    /// </summary>
    public readonly struct GroveStand
    {
        public readonly int AnchorCol, AnchorRow;
        public readonly string PieceId;
        public readonly bool Flipped;
        public readonly GroveFootprint Footprint;

        /// <summary>True for the hall: drawn from the best home owned, never placed, never picked up.</summary>
        public readonly bool IsHall;

        public GroveStand(int anchorCol, int anchorRow, string pieceId, bool flipped,
                          GroveFootprint footprint, bool isHall = false)
        {
            AnchorCol = anchorCol;
            AnchorRow = anchorRow;
            PieceId = pieceId ?? string.Empty;
            Flipped = flipped;
            Footprint = footprint;
            IsHall = isHall;
        }

        /// <summary>False for a <c>default</c> stand, whose id is null — the constructor is bypassed.</summary>
        public bool IsValid => !string.IsNullOrEmpty(PieceId);

        public string AnchorId => GroveFloor.TileId(AnchorCol, AnchorRow);

        public long AnchorKey => GroveOccupancy.Key(AnchorCol, AnchorRow);

        public bool Holds(int col, int row) => Footprint.Holds(AnchorCol, AnchorRow, col, row);

        public int Depth => Footprint.Depth(AnchorCol, AnchorRow);

        public float CentreCol => Footprint.CentreCol(AnchorCol);

        public float CentreRow => Footprint.CentreRow(AnchorRow);

        public override string ToString() => $"{PieceId}@{AnchorId} {Footprint}{(Flipped ? " flipped" : "")}";
    }

    /// <summary>Why a placement did or did not happen. See <see cref="HomesteadLayout.TryPlace"/>.</summary>
    public enum GrovePlaceResult
    {
        /// <summary>Written, saved and raised.</summary>
        Placed,

        /// <summary>What was asked for is already the case. Nothing written.</summary>
        Unchanged,

        /// <summary>The footprint does not fit anywhere around the tile the player chose.</summary>
        NoRoom,

        /// <summary>A rule refuses it outright — the hall, a tile off the floor, an empty request.</summary>
        Refused,
    }

    /// <summary>
    /// Which tile is covered by what: the derived index every placement rule and every drawing
    /// path reads.
    ///
    /// <para>
    /// <b>Derived, never stored.</b> The save holds anchors (<see cref="HomesteadLayout"/>);
    /// this is the anchors expanded through the catalog's footprints, rebuilt whenever a row
    /// changes. That is what makes a footprint retunable in a content drop with no migration,
    /// and it is why a merge can never corrupt it — there is nothing here to merge.
    /// </para>
    /// <para>
    /// <b>Two stands may overlap, and the index says so rather than hiding one.</b> Two devices
    /// can each place something on ground the other's placement covers, and the join keeps
    /// both (invariant 11 refuses to drop either). A covered tile answers with the stand
    /// nearest the viewer, which is also the one drawn on top, so what a finger picks is what
    /// the eye sees; an anchor tile always answers with the stand anchored there. Nothing new
    /// can be placed into an overlap, and moving either piece away resolves it.
    /// </para>
    /// </summary>
    public sealed class GroveOccupancy
    {
        public static readonly GroveOccupancy Empty = new GroveOccupancy(Array.Empty<GroveStand>());

        readonly Dictionary<long, GroveStand> _byAnchor = new Dictionary<long, GroveStand>();
        readonly Dictionary<long, long> _anchorOf = new Dictionary<long, long>();
        readonly List<GroveStand> _stands = new List<GroveStand>();

        public GroveOccupancy(IEnumerable<GroveStand> stands)
        {
            if (stands == null) return;

            foreach (var stand in stands)
                if (stand.IsValid) _stands.Add(stand);

            // Back to front, so that where two stands cover one tile the nearer — the one
            // painted on top — is the one the tile answers with.
            _stands.Sort((a, b) => a.Depth.CompareTo(b.Depth));

            foreach (var stand in _stands)
                for (int c = 0; c < stand.Footprint.Cols; c++)
                    for (int r = 0; r < stand.Footprint.Rows; r++)
                        _anchorOf[Key(stand.AnchorCol + c, stand.AnchorRow + r)] = stand.AnchorKey;

            // Anchors last, so a stand always owns its own anchor tile whatever covers it.
            foreach (var stand in _stands)
            {
                _byAnchor[stand.AnchorKey] = stand;
                _anchorOf[stand.AnchorKey] = stand.AnchorKey;
            }
        }

        /// <summary>Every stand, back to front.</summary>
        public IReadOnlyList<GroveStand> Stands => _stands;

        public int Count => _stands.Count;

        /// <summary>The stand anchored exactly on this tile, if any.</summary>
        public bool TryAnchored(int col, int row, out GroveStand stand)
            => _byAnchor.TryGetValue(Key(col, row), out stand);

        /// <summary>The stand covering this tile — anchored on it or reaching over it — if any.</summary>
        public bool TryStandAt(int col, int row, out GroveStand stand)
        {
            stand = default;
            return _anchorOf.TryGetValue(Key(col, row), out long anchor)
                && _byAnchor.TryGetValue(anchor, out stand);
        }

        public bool IsCovered(int col, int row) => _anchorOf.ContainsKey(Key(col, row));

        /// <summary>
        /// How many tiles of a region hold something, counting every tile a footprint covers.
        ///
        /// The hall's tiles are excluded, because the hall is drawn from what the player owns
        /// rather than placed — counting them would make a region that reads as started before
        /// anybody touched it.
        /// </summary>
        public int CoveredCount(GroveRegion region)
        {
            if (region == null || !region.IsValid) return 0;

            int count = 0;
            for (int c = region.Col; c < region.Col + region.Cols; c++)
                for (int r = region.Row; r < region.Row + region.Rows; r++)
                    if (TryStandAt(c, r, out var stand) && !stand.IsHall) count++;

            return count;
        }

        // ---------------------------------------------------------------- fitting
        /// <summary>
        /// Whether a footprint anchored here stands entirely on ground the player may build on
        /// and covers nothing else.
        ///
        /// <para>
        /// Up to two anchors may be ignored — the stand being moved, and the one it would swap
        /// with — because a move is a question about the board <em>without</em> them on it.
        /// Passing them in rather than building a second index is what keeps a drag's preview
        /// free of allocation on every frame the finger moves.
        /// </para>
        /// </summary>
        public bool Fits(GroveFloor floor, GroveFootprint footprint, int anchorCol, int anchorRow,
                         Func<int, int, bool> buildable, long ignoreA = NoKey, long ignoreB = NoKey)
        {
            if (floor == null || floor.IsEmpty) return false;

            for (int c = 0; c < footprint.Cols; c++)
                for (int r = 0; r < footprint.Rows; r++)
                {
                    int col = anchorCol + c, row = anchorRow + r;

                    if (!floor.Contains(col, row)) return false;
                    if (floor.IsHall(col, row)) return false;
                    if (buildable != null && !buildable(col, row)) return false;

                    if (_anchorOf.TryGetValue(Key(col, row), out long anchor)
                        && anchor != ignoreA && anchor != ignoreB)
                        return false;
                }

            return true;
        }

        /// <summary>
        /// How many distinct stands a footprint anchored here would land on, other than the
        /// ignored ones — and which, when it is exactly one. For deciding whether a move is a
        /// swap, which is the only question that needs the answer, and one that needs no list.
        /// </summary>
        public int Overlapping(GroveFootprint footprint, int anchorCol, int anchorRow,
                               out GroveStand only, long ignoreA = NoKey, long ignoreB = NoKey)
        {
            only = default;
            long first = NoKey, second = NoKey;
            int count = 0;

            for (int c = 0; c < footprint.Cols; c++)
                for (int r = 0; r < footprint.Rows; r++)
                {
                    if (!_anchorOf.TryGetValue(Key(anchorCol + c, anchorRow + r), out long anchor)) continue;
                    if (anchor == ignoreA || anchor == ignoreB) continue;
                    if (anchor == first || anchor == second) continue;

                    // A footprint is at most four by four, so it can land on more than two
                    // stands; only the first two are told apart, because the answer that matters
                    // is "one, and which" against "more than one".
                    if (first == NoKey) first = anchor;
                    else if (second == NoKey) second = anchor;
                    count++;
                }

            if (count == 1) _byAnchor.TryGetValue(first, out only);
            return count;
        }

        /// <summary>
        /// Finds an anchor for a footprint that includes the tile the player touched, if any.
        ///
        /// <para>
        /// The touched tile is tried as the anchor first — the piece then extends toward the
        /// viewer from where the finger was — and then every other anchor that still covers
        /// the touched tile, nearest first. So a player tapping the last free tile against a
        /// wall still gets their two-wide bench, laid the only way it fits, rather than a
        /// refusal they cannot see the reason for.
        /// </para>
        /// </summary>
        public bool TryFit(GroveFloor floor, GroveFootprint footprint, int touchCol, int touchRow,
                           Func<int, int, bool> buildable, out int anchorCol, out int anchorRow,
                           long ignoreA = NoKey, long ignoreB = NoKey)
        {
            for (int reach = 0; reach <= footprint.Cols + footprint.Rows - 2; reach++)
                for (int i = 0; i < footprint.Cols; i++)
                {
                    int j = reach - i;
                    if (j < 0 || j >= footprint.Rows) continue;

                    anchorCol = touchCol - i;
                    anchorRow = touchRow - j;

                    if (Fits(floor, footprint, anchorCol, anchorRow, buildable, ignoreA, ignoreB))
                        return true;
                }

            anchorCol = touchCol;
            anchorRow = touchRow;
            return false;
        }

        // ------------------------------------------------------------------ keys
        public const long NoKey = -1L;

        public static long Key(int col, int row) => ((long)col << 32) | (uint)row;

        public static int ColOf(long key) => (int)(key >> 32);

        public static int RowOf(long key) => (int)(key & 0xFFFFFFFFL);

        /// <summary>
        /// A stand for a placement row, resolved against the catalog.
        ///
        /// A piece this build does not know still stands on its one tile: the row is somebody's
        /// arrangement from a newer drop, and drawing an empty tile that can be built over would
        /// let this build silently bury it.
        /// </summary>
        public static GroveStand Of(HomesteadCatalog catalog, string tileId, string pieceId,
                                    bool flipped, bool isHall = false)
        {
            if (string.IsNullOrEmpty(pieceId)) return default;
            if (!GroveFloor.TryParse(tileId, out int col, out int row)) return default;

            var piece = catalog != null ? catalog.Find(pieceId) : default;
            var footprint = isHall && catalog != null
                ? catalog.Floor.HallFootprint
                : (piece.IsValid ? piece.Footprint : GroveFootprint.Single).Facing(flipped);

            return new GroveStand(col, row, pieceId, flipped, footprint, isHall);
        }
    }
}
