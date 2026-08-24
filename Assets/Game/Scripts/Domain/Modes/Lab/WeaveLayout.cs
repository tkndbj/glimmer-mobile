using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>One crystal and the critter that wants its colour.</summary>
    public readonly struct WeavePair
    {
        /// <summary>The crystal's cell. Where a channel must start.</summary>
        public readonly int Heart;

        /// <summary>The critter's cell. Where it must end.</summary>
        public readonly int Critter;

        public readonly int Colour;

        public WeavePair(int heart, int critter, int colour)
        {
            Heart = heart;
            Critter = critter;
            Colour = colour;
        }
    }

    /// <summary>
    /// A Lightweave puzzle: a grove, some pairs to join, and the arrangement that proves it can
    /// be done.
    ///
    /// <para>
    /// <b>The solution is generated first and the puzzle is what is left of it.</b> Scattering
    /// endpoints at random and hoping produces an unsolvable board most of the time — the paths
    /// have to fit past each other without crossing, and whether four of them can is not
    /// something you can tell by looking at where they start. Carving four disjoint paths and
    /// then hiding everything but their ends makes solvability a property of how the board was
    /// built rather than something to be checked afterwards and prayed over.
    /// </para>
    /// <para>
    /// <see cref="Solution"/> is kept, and not only as proof. Its total length is what the clock
    /// and the star thresholds are derived from, so a knottier board gets more time without
    /// anybody authoring a number — which is invariant 5 for a mode whose difficulty is
    /// generated rather than typed.
    /// </para>
    /// </summary>
    public sealed class WeaveLayout
    {
        public readonly int Width, Height;

        readonly WeavePair[] _pairs;
        readonly int[][] _solution;

        public WeaveLayout(int width, int height, WeavePair[] pairs, int[][] solution)
        {
            Width = width;
            Height = height;
            _pairs = pairs;
            _solution = solution;
        }

        public int Count => Width * Height;
        public int Index(int x, int y) => y * Width + x;
        public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public IReadOnlyList<WeavePair> Pairs => _pairs;

        /// <summary>The route the generator carved for a pair. The board's own proof it can be done.</summary>
        public IReadOnlyList<int> Solution(int pair) => _solution[pair];

        /// <summary>
        /// Every cell the solution uses. What the clock is scaled by: a board whose paths wind
        /// through fifty cells is a longer job than one whose paths take twenty, whatever their
        /// endpoints look like.
        /// </summary>
        public int SolutionLength
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _solution.Length; i++) total += _solution[i].Length;
                return total;
            }
        }

        /// <summary>How much of the grove the solution occupies, 0..1. The congestion knob.</summary>
        public float Coverage => Count == 0 ? 0f : SolutionLength / (float)Count;

        /// <summary>
        /// Whether every cell of the grove belongs to a channel.
        ///
        /// The generator's acceptance bar, and an exact integer question rather than a comparison
        /// against a float: a grove is either full or it is not, and a board one cell short is a
        /// board with a spare route through it.
        /// </summary>
        public bool IsComplete => Count > 0 && SolutionLength == Count;

        /// <summary>Whether a cell is one of the endpoints, of any pair.</summary>
        public int EndpointAt(int cell)
        {
            for (int i = 0; i < _pairs.Length; i++)
                if (_pairs[i].Heart == cell || _pairs[i].Critter == cell) return i;
            return -1;
        }

        public bool IsHeart(int cell)
        {
            for (int i = 0; i < _pairs.Length; i++) if (_pairs[i].Heart == cell) return true;
            return false;
        }

        public bool IsCritter(int cell)
        {
            for (int i = 0; i < _pairs.Length; i++) if (_pairs[i].Critter == cell) return true;
            return false;
        }

        public static readonly (int dx, int dy)[] Steps = { (0, -1), (1, 0), (0, 1), (-1, 0) };

        /// <summary>Whether two cells are orthogonally adjacent — the only step a channel may take.</summary>
        public bool Adjacent(int a, int b)
        {
            int ax = a % Width, ay = a / Width, bx = b % Width, by = b / Width;
            int dx = ax > bx ? ax - bx : bx - ax;
            int dy = ay > by ? ay - by : by - ay;
            return dx + dy == 1;
        }
    }
}
