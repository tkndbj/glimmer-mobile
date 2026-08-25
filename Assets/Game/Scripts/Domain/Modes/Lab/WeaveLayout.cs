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
    /// A bead: a cell one channel — and only that channel — must be threaded through.
    ///
    /// <para>
    /// <b>It is the mode's one visible constraint, and that is the whole reason it exists.</b>
    /// A weave used to be made hard by a rule the board could not show: every cell had to be
    /// covered, so the sensible route was almost never the right one and the player was sent the
    /// long way round by something they could not see. A bead asks for the same detour and
    /// <em>points at where</em>: it is drawn on the ground, in the colour of the channel that
    /// owes it, and the route it demands is one the player chooses rather than one the rules
    /// impose.
    /// </para>
    /// <para>
    /// It constrains twice over, which is what makes one bead worth more than its own detour.
    /// The channel that owns it must come through, and <em>no other channel may</em> — so a bead
    /// is a wall to five colours and a waypoint to one, and both halves push the other channels
    /// into each other. That is the congestion the fill rule used to manufacture, bought with a
    /// thing the player can see and reason about.
    /// </para>
    /// </summary>
    public readonly struct WeaveBead
    {
        /// <summary>The cell it stands on.</summary>
        public readonly int Cell;

        /// <summary>Which pair owes it. Its colour is that pair's colour, never a seventh one.</summary>
        public readonly int Pair;

        public WeaveBead(int cell, int pair)
        {
            Cell = cell;
            Pair = pair;
        }
    }

    /// <summary>
    /// A Lightweave puzzle: a grove, some pairs to join, the beads they must be threaded
    /// through, and the arrangement that proves it can be done.
    ///
    /// <para>
    /// <b>The solution is generated first and the puzzle is what is left of it.</b> Scattering
    /// endpoints at random and hoping produces an unsolvable board most of the time — the paths
    /// have to fit past each other without crossing, and whether four of them can is not
    /// something you can tell by looking at where they start. Carving disjoint paths and then
    /// hiding everything but their ends makes solvability a property of how the board was built
    /// rather than something to be checked afterwards and prayed over.
    /// </para>
    /// <para>
    /// <b><see cref="Solution"/> is kept as proof and no longer as par.</b> It is the route the
    /// generator carved, which fills the grove; the player is not asked to reproduce it and on
    /// most boards will not. What the clock and the star lines derive from is <see cref="Par"/>,
    /// which is built on <see cref="StraightTotal"/> — the fewest cells of light that could
    /// possibly finish the grove — so a board with more pairs or more beads on it allows more
    /// time without anybody authoring a number, which is invariant 5 for a mode whose difficulty
    /// is generated rather than typed.
    /// </para>
    /// </summary>
    public sealed class WeaveLayout
    {
        public readonly int Width, Height;

        readonly WeavePair[] _pairs;
        readonly int[][] _solution;
        readonly WeaveBead[] _beads;

        /// <summary>Which pair owes the bead on each cell, -1 where there is none.</summary>
        readonly int[] _beadOwner;

        /// <summary>Each pair's beads, gathered once. Read by the floor and by every drawer.</summary>
        readonly int[][] _beadsOf;

        /// <summary>The gated floor per pair, worked out lazily and then kept.</summary>
        readonly int[] _straight;

        public WeaveLayout(int width, int height, WeavePair[] pairs, int[][] solution,
                           WeaveBead[] beads = null)
        {
            Width = width;
            Height = height;
            _pairs = pairs;
            _solution = solution;
            _beads = beads ?? System.Array.Empty<WeaveBead>();

            _beadOwner = new int[width * height];
            for (int i = 0; i < _beadOwner.Length; i++) _beadOwner[i] = -1;

            var perPair = new List<int>[pairs.Length];
            for (int p = 0; p < pairs.Length; p++) perPair[p] = new List<int>();

            for (int b = 0; b < _beads.Length; b++)
            {
                var bead = _beads[b];
                if (bead.Cell < 0 || bead.Cell >= _beadOwner.Length) continue;
                if (bead.Pair < 0 || bead.Pair >= pairs.Length) continue;

                _beadOwner[bead.Cell] = bead.Pair;
                perPair[bead.Pair].Add(bead.Cell);
            }

            _beadsOf = new int[pairs.Length][];
            for (int p = 0; p < pairs.Length; p++) _beadsOf[p] = perPair[p].ToArray();

            _straight = new int[pairs.Length];
            for (int p = 0; p < pairs.Length; p++) _straight[p] = -1;
        }

        public int Count => Width * Height;
        public int Index(int x, int y) => y * Width + x;
        public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public IReadOnlyList<WeavePair> Pairs => _pairs;

        public IReadOnlyList<WeaveBead> Beads => _beads;

        /// <summary>Which pair owes the bead standing on this cell, or -1 for plain ground.</summary>
        public int BeadOwner(int cell)
            => cell < 0 || cell >= _beadOwner.Length ? -1 : _beadOwner[cell];

        /// <summary>The cells this pair must be threaded through, in no particular order.</summary>
        public IReadOnlyList<int> BeadsOf(int pair) => _beadsOf[pair];

        /// <summary>Whether any pair on this grove owes a bead. What decides the lesson and the readout.</summary>
        public bool HasBeads => _beads.Length > 0;

        /// <summary>The route the generator carved for a pair. The board's own proof it can be done.</summary>
        public IReadOnlyList<int> Solution(int pair) => _solution[pair];

        /// <summary>
        /// The pair whose carved route covers the least ground.
        ///
        /// <para>
        /// In Domain for <see cref="StrokeThrough"/>'s reason, and it answers the question a
        /// demonstration actually has: not which two ends sit closest, but which channel is
        /// shortest to draw. Those stopped being the same question at invariant 20f — no pair
        /// may be joinable by a straight drag, so the nearest ends are quite often the ones
        /// whose route takes the longest way round.
        /// </para>
        /// </summary>
        public int ShortestSolution()
        {
            int chosen = 0, fewest = int.MaxValue;

            for (int p = 0; p < _solution.Length; p++)
            {
                int length = _solution[p] == null ? 0 : _solution[p].Length;
                if (length < 2 || length >= fewest) continue;

                fewest = length;
                chosen = p;
            }

            return chosen;
        }

        /// <summary>
        /// Every cell the generator's own arrangement uses.
        ///
        /// Kept for the generator's acceptance bar and for the validator, and deliberately no
        /// longer par: the player fills nothing, so grading them against the length of a route
        /// nobody is asked to draw would be grading them against a fiction. See
        /// <see cref="StraightTotal"/>.
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

        /// <summary>How much of the grove the carved arrangement occupies, 0..1.</summary>
        public float Coverage => Count == 0 ? 0f : SolutionLength / (float)Count;

        /// <summary>
        /// Whether the carved arrangement reaches every cell of the grove.
        ///
        /// <para>
        /// <b>Still the generator's bar, and no longer the player's.</b> Filling the grove was
        /// once the win condition and is now purely a property of how a board is built: walks
        /// grown until nothing is left put the endpoints out at the edges of the grove and
        /// interleave the routes between them, which is what makes the pairs contend for ground.
        /// A carve that leaves islands behind tends to deal a board where every pair has its own
        /// quiet corner.
        /// </para>
        /// </summary>
        public bool IsComplete => Count > 0 && SolutionLength == Count;

        static int Manhattan(int a, int b, int width)
        {
            int ax = a % width, ay = a / width, bx = b % width, by = b / width;
            int dx = ax > bx ? ax - bx : bx - ax;
            int dy = ay > by ? ay - by : by - ay;
            return dx + dy;
        }

        /// <summary>Cells apart, ignoring everything in between. The floor every length here rests on.</summary>
        public int Distance(int a, int b) => Manhattan(a, b, Width);

        /// <summary>
        /// The fewest cells any legal route for this pair could possibly use: the shortest walk
        /// from its crystal to its critter that passes through every bead it owes.
        ///
        /// <para>
        /// <b>It is a floor rather than a route.</b> Distances here are straight-line and ignore
        /// everything standing in the way, so no route is ever shorter than this and many boards
        /// admit none this short. That is exactly the property wanted: everything else in this
        /// mode is measured as how far past it a board makes somebody go, and a floor that could
        /// be beaten would make those numbers negative and meaningless.
        /// </para>
        /// <para>
        /// <b>With beads it is a tour, so it is solved as one.</b> The bead order is not
        /// authored — a pair with three beads may thread them in any order, and picking the
        /// wrong one is part of the puzzle — so this is a shortest Hamiltonian path from crystal
        /// to critter over that pair's beads, done exactly by dynamic programming over subsets.
        /// Beads per pair are small by construction, so the exact answer costs nothing and there
        /// is no heuristic here to be subtly wrong.
        /// </para>
        /// <para>
        /// <b>Every order has the same parity, which is what keeps a detour even.</b> The
        /// distance between two cells has the parity of the sum of their coordinates, and the
        /// sum over any chain of beads telescopes to the parity of crystal-to-critter — so the
        /// floor, and therefore every excess measured against it, cannot be odd. That is why the
        /// bars in this mode read as "not straight" rather than as a number somebody picked.
        /// </para>
        /// </summary>
        public int Straight(int pair)
        {
            if (_straight[pair] >= 0) return _straight[pair];

            var ends = _pairs[pair];
            var beads = _beadsOf[pair];

            if (beads.Length == 0)
                return _straight[pair] = Manhattan(ends.Heart, ends.Critter, Width) + 1;

            // Held-Karp over this pair's beads. best[mask, last] is the shortest walk that has
            // left the crystal, threaded exactly the beads in mask, and is standing on beads[last].
            int n = beads.Length, full = 1 << n;
            var best = new int[full * n];
            for (int i = 0; i < best.Length; i++) best[i] = int.MaxValue;

            for (int i = 0; i < n; i++)
                best[(1 << i) * n + i] = Manhattan(ends.Heart, beads[i], Width);

            for (int mask = 1; mask < full; mask++)
                for (int last = 0; last < n; last++)
                {
                    int here = best[mask * n + last];
                    if (here == int.MaxValue || (mask & (1 << last)) == 0) continue;

                    for (int next = 0; next < n; next++)
                    {
                        if ((mask & (1 << next)) != 0) continue;

                        int step = here + Manhattan(beads[last], beads[next], Width);
                        int at = (mask | (1 << next)) * n + next;
                        if (step < best[at]) best[at] = step;
                    }
                }

            int shortest = int.MaxValue;
            for (int last = 0; last < n; last++)
            {
                int here = best[(full - 1) * n + last];
                if (here == int.MaxValue) continue;

                int whole = here + Manhattan(beads[last], ends.Critter, Width);
                if (whole < shortest) shortest = whole;
            }

            return _straight[pair] = shortest + 1;
        }

        /// <summary>
        /// The fewest cells of light that could finish this grove: every pair's floor, summed.
        ///
        /// <para>
        /// <b>This is par</b>, and it is what the clock and the star lines derive from. It is the
        /// honest reading of the work now that the player fills nothing: a grove asks for as much
        /// light as its pairs are far apart and its beads are out of the way, so putting a bead on
        /// a board raises its par, its gold line and its limit together, with no number authored
        /// anywhere. A board is never finishable in fewer cells than this, so three stars stays a
        /// thing a player can hold in their head — draw it about as directly as it can be drawn.
        /// </para>
        /// </summary>
        public int StraightTotal
        {
            get
            {
                int total = 0;
                for (int p = 0; p < _pairs.Length; p++) total += Straight(p);
                return total;
            }
        }

        /// <summary>
        /// What this grove is graded against: the light it must carry, plus a cell of looking
        /// for each decision it asks.
        ///
        /// <para>
        /// <b>Par is the clock and the star lines, so what it counts has to be the work.</b>
        /// <see cref="StraightTotal"/> is the ink — the fewest cells any set of routes could use
        /// — and it is most of the answer. What it misses is that a weave is not drawn at a
        /// constant rate: before the first channel goes down the player has to look at the board
        /// and work out who is in whose way, and that costs time in proportion to how many
        /// things there are to weigh up rather than to how far the light travels. Each pair and
        /// each bead is one such thing, so each is worth a cell.
        /// </para>
        /// <para>
        /// <b>Why not the ink alone.</b> It grades a mode on dragging speed, which is finger
        /// accuracy rather than thinking — and measured against the shipped chapter it cuts every
        /// three-star line by about a third. Three stars pays credits (invariant 9), so a drop
        /// meant to change the puzzle would have quietly retuned the economy with it, in a
        /// direction nobody chose and nothing would have reported. The allowance lands the star
        /// lines within a cell or two of where this mode already graded, so this change moves the
        /// puzzle and leaves the payout alone.
        /// </para>
        /// <para>
        /// <b>Why not the carved solution's length, which is what it used to be.</b> That route
        /// fills the grove, and while filling the grove was the win condition it was exactly the
        /// work. It no longer is: the player draws whatever route they like and on most boards
        /// that is a good deal less, so grading them against it is grading them against a route
        /// nobody is asked to draw. It also made par a function of the grove's size alone, so
        /// two boards of wildly different difficulty at the same size were given the same clock.
        /// </para>
        /// </summary>
        public int Par => StraightTotal + _pairs.Length + _beads.Length;

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

        /// <summary>
        /// Which pair, if any, is the only one allowed on this cell before anything is drawn —
        /// its endpoints and its beads.
        ///
        /// One question rather than three, because the run, the solver and the view all have to
        /// agree about it and three copies of "who is standing here" is three chances to answer
        /// differently. A bead is as much its pair's ground as its crystal is.
        /// </summary>
        public int Reserved(int cell)
        {
            int end = EndpointAt(cell);
            return end >= 0 ? end : BeadOwner(cell);
        }

        /// <summary>
        /// Two cells a bead could be threaded between — one going in, one coming out — for the
        /// lesson that shows what a ring is for.
        ///
        /// <para>
        /// <b>Not read off the solution, deliberately.</b> The carved route through this bead is
        /// the answer to part of the grove, and a demonstration is not the place to hand it over.
        /// What has to be shown is only that a channel goes <em>through</em> a ring rather than
        /// stopping at it, and any straight pass across the cell says that. So the pair is chosen
        /// by geometry: opposite neighbours, both inside the grove, neither one reserved to
        /// anybody.
        /// </para>
        /// <para>
        /// Across before down for the same reason a bubble prefers to sit below a ring — a
        /// horizontal stroke is the one that cannot be mistaken for the hand simply arriving.
        /// Reserved ground is refused rather than merely avoided: a crystal or a critter under
        /// the fingertip would read as the demonstration starting or ending there, which is the
        /// one thing this lesson is not about.
        /// </para>
        /// <para>
        /// In Domain rather than beside the screen, for <c>ChapterMap</c>'s reason: it is a fact
        /// about a board, so it can be proved offline against the shipped groves instead of being
        /// looked at once in the Editor.
        /// </para>
        /// </summary>
        /// <returns>False when the bead is boxed in, in which case there is nothing to trace.</returns>
        public bool StrokeThrough(int bead, out int from, out int to)
        {
            from = to = -1;
            if (bead < 0 || bead >= _beads.Length) return false;

            int cell = _beads[bead].Cell;
            int x = cell % Width, y = cell / Width;

            if (Plain(x - 1, y) && Plain(x + 1, y))
            {
                from = Index(x - 1, y);
                to = Index(x + 1, y);
                return true;
            }

            if (Plain(x, y - 1) && Plain(x, y + 1))
            {
                from = Index(x, y - 1);
                to = Index(x, y + 1);
                return true;
            }

            return false;
        }

        /// <summary>Ground inside the grove that belongs to nobody before anything is drawn.</summary>
        bool Plain(int x, int y) => Inside(x, y) && Reserved(Index(x, y)) < 0;

        public static readonly (int dx, int dy)[] Steps = { (0, -1), (1, 0), (0, 1), (-1, 0) };

        /// <summary>Whether two cells are orthogonally adjacent — the only step a channel may take.</summary>
        public bool Adjacent(int a, int b) => Manhattan(a, b, Width) == 1;
    }
}
