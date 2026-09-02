using System;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// How many tiles a piece stands on: a rectangle of columns and rows, anchored at one tile.
    ///
    /// <para>
    /// <b>Why a piece has one at all.</b> Every tile was a slot holding one thing, and a
    /// cottage is two tiles wide however it is drawn — so it stood on one tile and painted over
    /// the three beside it, which could still be tapped, still be built on, and still be walked
    /// over by a fence somebody put down without seeing the tile under the eaves. Reported from
    /// play as "big objects visually cover more than one tile". The footprint is the fact that
    /// was missing: what a piece <em>occupies</em>, as distinct from what it paints.
    /// </para>
    /// <para>
    /// <b>The anchor is the back corner and the save file stores only the anchor.</b> A
    /// placement row still names one tile, the smallest column and row the piece covers, and
    /// the rest of the footprint is derived from the catalog at read time. So this cost the save
    /// nothing (invariant 20a's shape), a retune of a piece's footprint moves what it occupies
    /// and never what is stored, and a merge that lands two footprints on top of each other is
    /// drawn as two things overlapping rather than losing either — invariant 11 refuses the
    /// alternative.
    /// </para>
    /// <para>
    /// <b>A mirrored piece swaps its columns for its rows.</b> Screen x is
    /// <c>(col - row) · w/2</c>, so reflecting the drawing is exactly exchanging the two axes:
    /// a ramp two tiles long along one diagonal, flipped, runs two tiles along the other. The
    /// anchor stays where it is, which is what makes a flip a decision about one tile rather
    /// than a move in disguise.
    /// </para>
    /// </summary>
    public readonly struct GroveFootprint : IEquatable<GroveFootprint>
    {
        /// <summary>The most a piece may cover on a side. Content larger than this is clamped and reported.</summary>
        public const int MaxSide = 4;

        readonly int _cols, _rows;

        /// <summary>
        /// Never below one, whatever was stored: a <c>default</c> struct bypasses the
        /// constructor, and a piece whose footprint was never set stands on one tile rather
        /// than on nothing. Clamped at read rather than at write for exactly that reason.
        /// </summary>
        public int Cols => _cols < 1 ? 1 : _cols;

        public int Rows => _rows < 1 ? 1 : _rows;

        public GroveFootprint(int cols, int rows)
        {
            _cols = cols < 1 ? 1 : cols > MaxSide ? MaxSide : cols;
            _rows = rows < 1 ? 1 : rows > MaxSide ? MaxSide : rows;
        }

        /// <summary>One tile — what every piece was before footprints, and what most still are.</summary>
        public static readonly GroveFootprint Single = new GroveFootprint(1, 1);

        public bool IsSingle => Cols == 1 && Rows == 1;

        public int TileCount => Cols * Rows;

        /// <summary>The same footprint mirrored: columns and rows exchanged. See the type's remarks.</summary>
        public GroveFootprint Mirrored => new GroveFootprint(Rows, Cols);

        /// <summary>This footprint as it stands when the piece faces one way or the other.</summary>
        public GroveFootprint Facing(bool flipped) => flipped ? Mirrored : this;

        /// <summary>Whether a tile lies inside this footprint when it is anchored at a tile.</summary>
        public bool Holds(int anchorCol, int anchorRow, int col, int row)
            => col >= anchorCol && col < anchorCol + Cols
            && row >= anchorRow && row < anchorRow + Rows;

        /// <summary>
        /// The tile nearest the viewer — the one this piece is depth-sorted by.
        ///
        /// A piece is drawn from its anchor's cell but stands in front of everything up to its
        /// front tile, so sorting it by the anchor would put a two-deep house behind a flower
        /// planted beside its door. <c>GroveFloor.DrawOrder</c> of the front tile is the same
        /// number a single tile there would get.
        /// </summary>
        public int FrontCol(int anchorCol) => anchorCol + Cols - 1;

        public int FrontRow(int anchorRow) => anchorRow + Rows - 1;

        /// <summary>The centre of the footprint in tile coordinates — a half-tile for an even side.</summary>
        public float CentreCol(int anchorCol) => anchorCol + (Cols - 1) * .5f;

        public float CentreRow(int anchorRow) => anchorRow + (Rows - 1) * .5f;

        /// <summary>
        /// Draw order for a piece standing here — later is nearer the viewer.
        ///
        /// <para>
        /// The front tile's own order, doubled, with a single-tile piece one step later: a
        /// fence planted on the front tile of a house (which a merge can produce, invariant 11)
        /// stands on that tile and must draw over the house rather than under it. Unique per
        /// (front tile, size class), which is all a stable sort needs.
        /// </para>
        /// </summary>
        public int Depth(int anchorCol, int anchorRow)
            => GroveFloor.DrawOrder(FrontCol(anchorCol), FrontRow(anchorRow)) * 2 + (IsSingle ? 1 : 0);

        public bool Equals(GroveFootprint other) => Cols == other.Cols && Rows == other.Rows;

        public override bool Equals(object obj) => obj is GroveFootprint other && Equals(other);

        public override int GetHashCode() => Cols * 31 + Rows;

        public static bool operator ==(GroveFootprint a, GroveFootprint b) => a.Equals(b);

        public static bool operator !=(GroveFootprint a, GroveFootprint b) => !a.Equals(b);

        public override string ToString() => $"{Cols}x{Rows}";
    }
}
