using System.Text;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// A rectangle of authored characters, read once and asked about afterwards.
    ///
    /// <para>
    /// <b>The one place every prototype mode agrees about what a board file looks like.</b> Each
    /// of them authors a grid of letters and a short deal string, and each of the five built at
    /// once was about to write its own reader — which is five copies of "spaces are ignored",
    /// five copies of the row-length message and five chances for one of them to accept a board
    /// the others would refuse. What differs between the modes is which letters mean something,
    /// and that is one parameter.
    /// </para>
    /// <para>
    /// <b>Every refusal names the row and the column.</b> A content error is read by somebody
    /// looking at a text file with forty characters in it, so "row 3 column 5 is 'q'" is the
    /// difference between a fix and a hunt. It is also why an unknown letter is refused rather
    /// than treated as bare ground: a mistyped board that quietly loses a critter validates,
    /// derives a plausible par and ships (invariant 5f's rule, applied to a parser).
    /// </para>
    /// </summary>
    public sealed class ProtoGrid
    {
        /// <summary>
        /// The narrowest board worth authoring. Below this there is nothing to arrange.
        /// </summary>
        public const int MinSide = 3;

        /// <summary>
        /// The widest. Not a drawing limit — it is the search: every one of these modes costs
        /// roughly its board's size to the power of its par, so a board twice as wide is not twice
        /// as expensive (invariant 26d). Ten a side is comfortably inside
        /// <see cref="ProtoSearch.NodeBudget"/> at the pars these modes are authored to.
        /// </summary>
        public const int MaxSide = 10;

        public readonly int Width, Height;

        readonly char[] _cells;

        ProtoGrid(int width, int height, char[] cells)
        {
            Width = width;
            Height = height;
            _cells = cells;
        }

        public int Count => _cells.Length;

        public char At(int index) => index >= 0 && index < _cells.Length ? _cells[index] : '\0';

        public char At(int x, int y)
            => x < 0 || y < 0 || x >= Width || y >= Height ? '\0' : _cells[y * Width + x];

        public int Index(int x, int y) => y * Width + x;

        public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public int XOf(int index) => index % Width;
        public int YOf(int index) => index / Width;

        /// <summary>How many cells carry this character.</summary>
        public int CountOf(char c)
        {
            int n = 0;
            for (int i = 0; i < _cells.Length; i++) if (_cells[i] == c) n++;
            return n;
        }

        /// <summary>The first cell carrying this character, or -1.</summary>
        public int FirstOf(char c)
        {
            for (int i = 0; i < _cells.Length; i++) if (_cells[i] == c) return i;
            return -1;
        }

        /// <summary>A copy of the cells, for a board that means to mutate them.</summary>
        public char[] Copy() => (char[])_cells.Clone();

        /// <summary>
        /// Reads an authored grid, or says exactly what is wrong with it.
        /// </summary>
        /// <param name="rows">One string per row, top first. Spaces are ignored throughout.</param>
        /// <param name="width">Columns the level declares.</param>
        /// <param name="height">Rows the level declares.</param>
        /// <param name="legal">Every character this mode understands.</param>
        public static bool TryRead(string[] rows, int width, int height, string legal,
                                   out ProtoGrid grid, out string error)
        {
            grid = null;
            error = null;

            if (width < MinSide || width > MaxSide)
            {
                error = $"a board is {MinSide}..{MaxSide} wide; this one says {width}";
                return false;
            }

            if (height < MinSide || height > MaxSide)
            {
                error = $"a board is {MinSide}..{MaxSide} tall; this one says {height}";
                return false;
            }

            if (rows == null || rows.Length != height)
            {
                error = $"declares {height} rows and carries {(rows == null ? 0 : rows.Length)}";
                return false;
            }

            var cells = new char[width * height];

            for (int y = 0; y < height; y++)
            {
                string raw = rows[y] ?? string.Empty;

                var packed = new StringBuilder(width);
                for (int i = 0; i < raw.Length; i++)
                    if (raw[i] != ' ') packed.Append(raw[i]);

                if (packed.Length != width)
                {
                    error = $"row {y} is {packed.Length} cells wide and the board says {width}";
                    return false;
                }

                for (int x = 0; x < width; x++)
                {
                    char c = packed[x];
                    if (legal.IndexOf(c) < 0)
                    {
                        error = $"row {y} column {x} is '{c}', which is not something this " +
                                $"board understands (it reads {legal})";
                        return false;
                    }

                    cells[y * width + x] = c;
                }
            }

            grid = new ProtoGrid(width, height, cells);
            return true;
        }
    }
}
