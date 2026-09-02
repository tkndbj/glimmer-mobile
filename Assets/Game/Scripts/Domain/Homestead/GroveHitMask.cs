using System;
using System.Text;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// Where a piece's art actually is, inside its own rectangle: a grid of bits over the
    /// picture, one for every cell the paint covers, at a fixed cell size in the art's own
    /// pixels.
    ///
    /// <para>
    /// <b>Why a mask and not the sprite's alpha.</b> Testing the texture itself would be exact
    /// and costs a readable copy of every texture in the grove — a CPU-side duplicate of a
    /// hundred and sixty props, permanently resident, to sharpen the edges of a tap. And
    /// testing the sprite's <em>box</em>, which is what shipped, is wrong in the other
    /// direction: an oak is 370 by 570 floor pixels of box around a trunk that is forty wide,
    /// so the box claimed every tile behind the tree and the ground beside the trunk could not
    /// be tapped at all. Reported from play as "it is hard to tap on some tiles once things are
    /// placed". A mask is a few dozen bytes a piece, generated offline from the shipped PNG
    /// (<c>Tools/grove_art_facts.py</c>), and answers the question the box was asked to answer.
    /// </para>
    /// <para>
    /// <b>The cell is a fixed size in art pixels, so the grid is the shape of the picture.</b>
    /// A fixed sixteen-by-sixteen grid was the first cut, and on a ladder 88 wide and 408 tall
    /// its cells were five pixels across and twenty-five down — the tolerance a tap got
    /// depended on which way the picture was long. At <see cref="CellPx"/> a cell is the same
    /// square everywhere, the grid's size follows from the art's size (which is why a mask is
    /// only readable beside <c>w</c> and <c>h</c>), and a tolerance is a distance rather than a
    /// count of cells — see <see cref="Hits"/>.
    /// </para>
    /// <para>
    /// <b>It is content, and it is checked.</b> A mask describes a picture, so it is only true
    /// until somebody re-cuts the picture — which is <c>GroveFloor.TileFaceRatio</c>'s lesson.
    /// The generator's <c>--check</c> proves every shipped mask is what it would write today,
    /// <c>Tools/verify/content.py</c> runs that check, and both refuse a mask whose length is
    /// not the one its art implies.
    /// </para>
    /// <para>
    /// <b>The encoding is a contract between three runtimes</b> — the Python that writes it,
    /// this reader, and the Python that checks it — so it is spelled out: the grid is
    /// <c>ceil(w / CellPx)</c> columns by <c>ceil(h / CellPx)</c> rows; the bits run row-major
    /// from the <em>top</em> row of the image, each row left to right; they are written as
    /// hexadecimal, the first bit of a character being its most significant, the last
    /// character padded with zero bits.
    /// </para>
    /// </summary>
    public readonly struct GroveHitMask
    {
        /// <summary>The side of one cell, in the art's own pixels.</summary>
        public const int CellPx = 16;

        /// <summary>
        /// The most cells a side may have. Grove art is imported at 512 at most, which is
        /// 32 cells; the cap keeps a corrupt size from asking for a mask the size of a texture.
        /// </summary>
        public const int MaxSide = 64;

        readonly ulong[] _bits;

        public readonly int Cols, Rows;

        GroveHitMask(int cols, int rows, ulong[] bits)
        {
            Cols = cols;
            Rows = rows;
            _bits = bits;
        }

        /// <summary>A mask with no art in it: what a piece without one carries.</summary>
        public static readonly GroveHitMask None = default;

        /// <summary>True when any cell is set — that is, when there is a mask at all.</summary>
        public bool IsSet
        {
            get
            {
                if (_bits == null) return false;
                foreach (ulong word in _bits)
                    if (word != 0UL) return true;
                return false;
            }
        }

        /// <summary>How many columns and rows a picture of this size has. See the type's remarks.</summary>
        public static int SideFor(int pixels) => pixels <= 0 ? 0 : (pixels + CellPx - 1) / CellPx;

        /// <summary>The length of a serialised mask for a picture of this size, in hexadecimal characters.</summary>
        public static int HexLengthFor(int width, int height)
        {
            int cells = SideFor(width) * SideFor(height);
            return (cells + 3) / 4;
        }

        /// <summary>
        /// Whether the picture covers one cell. <paramref name="cx"/> runs left to right and
        /// <paramref name="cy"/> top to bottom; anything outside the grid is not covered.
        /// </summary>
        public bool Covers(int cx, int cy)
        {
            if (_bits == null || cx < 0 || cy < 0 || cx >= Cols || cy >= Rows) return false;

            int bit = cy * Cols + cx;
            return (_bits[bit >> 6] & (1UL << (63 - (bit & 63)))) != 0UL;
        }

        /// <summary>
        /// Whether a point inside the sprite's rectangle lands on the picture, or within a
        /// tolerance of it.
        ///
        /// <para>
        /// <paramref name="u"/> runs 0 to 1 left to right and <paramref name="v"/> 0 to 1
        /// <em>top to bottom</em>, matching the encoding. A mirrored piece is asked with
        /// <paramref name="flipped"/> rather than with a second mask, because a flip is the
        /// one transform this grove offers and it is exactly a reflection of <c>u</c>.
        /// </para>
        /// <para>
        /// The tolerance is a distance in the same fractions — the caller converts a finger's
        /// slop from floor pixels through the box it is testing — so a tap a few pixels off a
        /// lantern post still lands on the lantern by the same margin on every piece, whatever
        /// its size or shape. A mask that is not set at all answers true everywhere, so a piece
        /// with no mask falls back to being its box.
        /// </para>
        /// </summary>
        public bool Hits(float u, float v, bool flipped, float toleranceU = 0f, float toleranceV = 0f)
        {
            if (!IsSet) return true;
            if (flipped) u = 1f - u;

            int x0 = (int)Math.Floor((u - toleranceU) * Cols), x1 = (int)Math.Floor((u + toleranceU) * Cols);
            int y0 = (int)Math.Floor((v - toleranceV) * Rows), y1 = (int)Math.Floor((v + toleranceV) * Rows);

            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 >= Cols) x1 = Cols - 1;
            if (y1 >= Rows) y1 = Rows - 1;

            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                    if (Covers(cx, cy)) return true;

            return false;
        }

        /// <summary>How many cells are set. For a validator judging whether a mask is plausible.</summary>
        public int CellCount
        {
            get
            {
                if (_bits == null) return 0;
                int n = 0;
                foreach (ulong word in _bits)
                {
                    ulong x = word;
                    while (x != 0UL) { x &= x - 1UL; n++; }
                }
                return n;
            }
        }

        // ------------------------------------------------------------- encoding
        /// <summary>
        /// Reads the serialised form for a picture of a given size. False for anything that is
        /// not exactly the length that size implies — a truncated or corrupted mask is refused
        /// rather than read as a different picture, and the caller falls back to the box.
        /// </summary>
        public static bool TryParse(string hex, int width, int height, out GroveHitMask mask)
        {
            mask = None;

            int cols = SideFor(width), rows = SideFor(height);
            if (cols <= 0 || rows <= 0 || cols > MaxSide || rows > MaxSide) return false;
            if (string.IsNullOrEmpty(hex) || hex.Length != HexLengthFor(width, height)) return false;

            int cells = cols * rows;
            var bits = new ulong[(cells + 63) >> 6];

            for (int k = 0; k < hex.Length; k++)
            {
                int nibble = Nibble(hex[k]);
                if (nibble < 0) return false;

                for (int j = 0; j < 4; j++)
                {
                    if ((nibble & (8 >> j)) == 0) continue;

                    int bit = k * 4 + j;
                    if (bit >= cells) return false;      // a set bit in the padding is not a mask

                    bits[bit >> 6] |= 1UL << (63 - (bit & 63));
                }
            }

            mask = new GroveHitMask(cols, rows, bits);
            return true;
        }

        /// <summary>A mask from explicit cells over a grid, for tests and tools. Out-of-range cells are ignored.</summary>
        public static GroveHitMask FromCells(int cols, int rows, params (int cx, int cy)[] cells)
        {
            if (cols < 1 || rows < 1 || cols > MaxSide || rows > MaxSide) return None;

            var bits = new ulong[(cols * rows + 63) >> 6];

            if (cells != null)
                foreach (var (cx, cy) in cells)
                {
                    if (cx < 0 || cy < 0 || cx >= cols || cy >= rows) continue;

                    int bit = cy * cols + cx;
                    bits[bit >> 6] |= 1UL << (63 - (bit & 63));
                }

            return new GroveHitMask(cols, rows, bits);
        }

        /// <summary>The serialised form. The inverse of <see cref="TryParse"/>, exactly.</summary>
        public string ToHex()
        {
            if (_bits == null) return string.Empty;

            int cells = Cols * Rows;
            int length = (cells + 3) / 4;
            var sb = new StringBuilder(length);

            for (int k = 0; k < length; k++)
            {
                int nibble = 0;
                for (int j = 0; j < 4; j++)
                {
                    int bit = k * 4 + j;
                    if (bit < cells && (_bits[bit >> 6] & (1UL << (63 - (bit & 63)))) != 0UL)
                        nibble |= 8 >> j;
                }
                sb.Append("0123456789abcdef"[nibble]);
            }

            return sb.ToString();
        }

        static int Nibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        public override string ToString() => IsSet ? $"{Cols}x{Rows}:{ToHex()}" : "(none)";
    }
}
