namespace GlimmerGrove.Modes
{
    /// <summary>
    /// A hedge: a barrier grown along the edges <em>between</em> cells, which no channel may
    /// cross.
    ///
    /// <para>
    /// <b>It is an edge and not a cell, and that is the whole of why it is a new thing rather
    /// than a bead nobody owns.</b> Every other object in this mode stands <em>on</em> ground —
    /// a crystal, a critter, a bead — so every one of them costs the grove a cell and can be
    /// walked around by one step. A hedge takes no ground at all: it removes a <em>way</em>
    /// between two cells that both stay perfectly usable. Grown in a run from the edge of the
    /// grove it makes rooms with doorways in them, and a doorway is the sharpest form of the one
    /// question this mode asks — who yields — because it is a thing the player can see before
    /// they commit to anything (<see cref="WeaveGenerator.MinReach"/>'s argument, for the ground
    /// rather than for the endpoints).
    /// </para>
    /// <para>
    /// <b>One hedge is the whole run, not one closed edge, and getting that wrong was worth
    /// catching.</b> A run is what the player sees — a single bar drawn down the grove — and it
    /// is one thing to weigh up when working out who goes where. Counting edges instead makes
    /// every reading of the mechanic wrong in the same direction: a level asking for two hedges
    /// would be told it had grown fourteen, and <see cref="WeaveLayout.Par"/>'s cell of looking
    /// per decision would hand a two-hedge grove fourteen cells of allowance. It was measured
    /// doing exactly that before this became a run.
    /// </para>
    /// <para>
    /// <b>It is stored canonically, so a hedge has exactly one spelling.</b> <see cref="Cell"/>
    /// is the topmost — or leftmost — cell on the low-index side of the run. Two spellings of one
    /// barrier would be two things a diff, a survey and a save-independent regeneration could
    /// disagree about, and this mode's entire correctness rests on two runtimes dealing the same
    /// board (see <see cref="WeaveGenerator.SlackNumerator"/>).
    /// </para>
    /// </summary>
    public readonly struct WeaveHedge
    {
        /// <summary>
        /// Where the run starts: the topmost cell of an upright hedge's left-hand column, or the
        /// leftmost cell of a flat hedge's upper row.
        /// </summary>
        public readonly int Cell;

        /// <summary>
        /// True when the hedge stands between two columns — so it is climbed down the grove and
        /// it is east–west movement it refuses. False when it lies between two rows.
        /// </summary>
        public readonly bool Upright;

        /// <summary>How many ways it closes: its length along the grove, in cells.</summary>
        public readonly int Length;

        public WeaveHedge(int cell, bool upright, int length)
        {
            Cell = cell;
            Upright = upright;
            Length = length;
        }

        public override string ToString()
            => Cell + (Upright ? "|" : "_") + Length;
    }

    /// <summary>
    /// Every hedge on one grove, and the two questions a hedge changes the answer to: whether two
    /// cells are still neighbours, and how far apart two cells now are.
    ///
    /// <para>
    /// <b>Both live here rather than in <see cref="WeaveLayout"/> because both are asked by
    /// something that has no layout yet.</b> <see cref="WeaveGenerator"/> grows its hedges
    /// <em>before</em> it carves, so the carve can only ever draw routes that respect them and a
    /// hedged board stays solvable by construction rather than by check. A second copy of "may a
    /// channel step from here to there" living in the generator is the shape invariant 9a exists
    /// to refuse — and it is the copy that would decide whether the shipped board is the board
    /// that was proved.
    /// </para>
    /// <para>
    /// <b><see cref="Span"/> is what keeps par honest, and it is the reason this mechanic could
    /// be added at all.</b> Everything a weave is graded on derives from
    /// <c>WeaveLayout.Straight</c> — the fewest cells any route of a pair's could use — and that
    /// was a Manhattan distance, which walks straight through a hedge. Left alone, a hedged grove
    /// would have been graded against a floor no arrangement of it could reach: the three-star
    /// line would sit below the best possible play and a whole band of the ladder would quietly
    /// stop existing, which is invariant 22's stranded band exactly. So a distance here is a
    /// breadth-first walk over the ways that are actually open, and every threshold in the mode
    /// rises with the hedges by itself, with nothing authored anywhere.
    /// </para>
    /// <para>
    /// <b>On a grove with no hedges it <em>is</em> the Manhattan distance</b>, and not
    /// approximately: a breadth-first walk over an open rectangular grid returns exactly
    /// <c>|dx| + |dy|</c>. That is what made this change free for the two chapters already
    /// shipped — every par, every star line and every seed they were authored against is
    /// unmoved, and <c>Tools/verify/weave.py</c> proves it board for board on both runtimes.
    /// </para>
    /// <para>
    /// <b>Parity survives too, which is what keeps a detour even.</b> Any walk between two cells
    /// of a grid has the parity of their Manhattan distance, hedges or no hedges, so slack stays
    /// an even number and <see cref="WeaveSolver.Latitude"/> keeps meaning "one lazy corner off
    /// the best" rather than a figure somebody picked.
    /// </para>
    /// </summary>
    public sealed class WeaveHedges
    {
        readonly int _width, _height;
        readonly WeaveHedge[] _all;

        /// <summary>Per cell, a bit per <see cref="WeaveLayout.Steps"/> index that is closed.</summary>
        readonly byte[] _closed;

        /// <summary>
        /// All-pairs shortest walks, worked out once on first use and then kept.
        ///
        /// Null while there are no hedges, which is the ordinary case and costs nothing: with
        /// nothing closed the answer is arithmetic. A grove is at most 9x12, so the matrix is a
        /// hundred-odd squared and is built by one breadth-first walk per cell — microseconds,
        /// once, against being asked inside a search that runs to millions of positions.
        /// </summary>
        int[] _span;

        public WeaveHedges(int width, int height, WeaveHedge[] hedges)
        {
            _width = width;
            _height = height;
            _all = hedges ?? System.Array.Empty<WeaveHedge>();

            _closed = new byte[width * height];

            for (int i = 0; i < _all.Length; i++) Close(_all[i]);
        }

        /// <summary>How many hedges are grown here. A run is one hedge, however long it is.</summary>
        public int Count => _all.Length;

        public bool Any => _all.Length > 0;

        public System.Collections.Generic.IReadOnlyList<WeaveHedge> All => _all;

        /// <summary>
        /// How many <em>distinct</em> ways this fence closes.
        ///
        /// <para>
        /// Counted off the closed set rather than summed from the runs' lengths, and that is the
        /// difference between a reading and a tautology. Two runs can be grown on the same
        /// boundary or across each other, and a sum of lengths cannot tell: it says three hedges
        /// of four closed twelve ways whether they closed twelve or eight.
        /// <c>WeaveGenerator.Fence</c> refuses a run that overlaps one already grown by comparing
        /// exactly these two numbers, so a sum here made that guard always agree with itself —
        /// which it did, and a grove asked for three barriers could quietly be given two.
        /// </para>
        /// <para>
        /// Deliberately not what <see cref="Count"/> answers: a run is one hedge however long it
        /// is, because a run is what the player sees and what a lesson points at. See
        /// <see cref="WeaveHedge"/>.
        /// </para>
        /// </summary>
        public int Edges
        {
            get
            {
                int bits = 0;
                for (int i = 0; i < _closed.Length; i++)
                {
                    int mask = _closed[i];
                    while (mask != 0) { bits += mask & 1; mask >>= 1; }
                }

                // Every closed way is written on both of the cells it stands between.
                return bits / 2;
            }
        }

        /// <summary>
        /// Whether a channel may step between two orthogonally adjacent cells.
        ///
        /// Asked of cells rather than of a direction because every caller has two cells and none
        /// of them has a direction — and deriving one in four places is four places to get the
        /// sign wrong.
        /// </summary>
        public bool Open(int a, int b)
        {
            int dir = Toward(a, b);
            return dir >= 0 && (_closed[a] & (1 << dir)) == 0;
        }

        /// <summary>
        /// The fewest steps between two cells over the ways that are open, or
        /// <see cref="Unreachable"/> when there is no way at all.
        ///
        /// <para>
        /// A floor and never a route: it takes no notice of what any channel has drawn, because
        /// the player may always take somebody else's channel up. That is the same direction of
        /// error every bound in this mode is held to — never optimistic about what a finish
        /// costs, so no run is ever ended that could still have been won.
        /// </para>
        /// </summary>
        public int Span(int a, int b)
        {
            if (!Any) return Manhattan(a, b);
            if (a == b) return 0;

            var span = _span ?? (_span = BuildSpans());
            return span[a * _closed.Length + b];
        }

        /// <summary>What <see cref="Span"/> answers when no way through exists at all.</summary>
        public const int Unreachable = int.MaxValue / 4;

        /// <summary>
        /// Whether every cell of the grove can still be reached from every other.
        ///
        /// <para>
        /// The one thing a hedge must never do. A sealed-off pocket is not a harder board — it is
        /// a board the carve cannot fill and, if one ever escaped that, a board where a pair
        /// dealt on the wrong side of the hedge could not be joined at any price. Checked while
        /// hedges are being grown rather than afterwards, so an attempt that would produce one is
        /// abandoned a step earlier and costs nothing.
        /// </para>
        /// </summary>
        public bool AllReachable
        {
            get
            {
                int cells = _closed.Length;
                if (cells == 0) return true;

                var seen = new bool[cells];
                var queue = new int[cells];

                int head = 0, tail = 0, found = 1;
                queue[tail++] = 0;
                seen[0] = true;

                while (head < tail)
                {
                    int at = queue[head++];
                    int x = at % _width, y = at / _width;

                    for (int d = 0; d < WeaveLayout.Steps.Length; d++)
                    {
                        if ((_closed[at] & (1 << d)) != 0) continue;

                        int nx = x + WeaveLayout.Steps[d].dx, ny = y + WeaveLayout.Steps[d].dy;
                        if (nx < 0 || ny < 0 || nx >= _width || ny >= _height) continue;

                        int next = ny * _width + nx;
                        if (seen[next]) continue;

                        seen[next] = true;
                        found++;
                        queue[tail++] = next;
                    }
                }

                return found == cells;
            }
        }

        // ------------------------------------------------------------------ the runs
        /// <summary>
        /// The two cells the <paramref name="step"/>th edge of a hedge stands between, or false
        /// when the run has walked off the grove.
        ///
        /// <para>
        /// The bounds check is not defensive tidiness. A flat hedge is a run of cells along a
        /// row, so a run that overshoots does not fall off the end of the array — it wraps onto
        /// the next row and closes a way on the far side of the grove, which is a barrier nobody
        /// asked for standing somewhere nothing is drawn. That is the class of fault
        /// <c>WeaveHedges.Toward</c> guards on the other side.
        /// </para>
        /// </summary>
        public bool Edge(WeaveHedge hedge, int step, out int a, out int b)
        {
            a = b = -1;
            if (step < 0 || step >= hedge.Length) return false;

            int x = hedge.Cell % _width, y = hedge.Cell / _width;

            if (hedge.Upright)
            {
                y += step;
                if (x + 1 >= _width || y >= _height) return false;

                a = y * _width + x;
                b = a + 1;
                return true;
            }

            x += step;
            if (x >= _width || y + 1 >= _height) return false;

            a = y * _width + x;
            b = a + _width;
            return true;
        }

        void Close(WeaveHedge hedge)
        {
            for (int step = 0; step < hedge.Length; step++)
            {
                if (!Edge(hedge, step, out int a, out int b)) continue;

                int dir = Toward(a, b);
                if (dir < 0) continue;

                _closed[a] |= (byte)(1 << dir);
                _closed[b] |= (byte)(1 << Opposite(dir));
            }
        }

        // ------------------------------------------------------------------ the arithmetic
        int[] BuildSpans()
        {
            int cells = _closed.Length;
            var span = new int[cells * cells];
            var queue = new int[cells];

            for (int from = 0; from < cells; from++)
            {
                int row = from * cells;
                for (int i = 0; i < cells; i++) span[row + i] = Unreachable;

                int head = 0, tail = 0;
                span[row + from] = 0;
                queue[tail++] = from;

                while (head < tail)
                {
                    int at = queue[head++];
                    int x = at % _width, y = at / _width;
                    int step = span[row + at] + 1;

                    for (int d = 0; d < WeaveLayout.Steps.Length; d++)
                    {
                        if ((_closed[at] & (1 << d)) != 0) continue;

                        int nx = x + WeaveLayout.Steps[d].dx, ny = y + WeaveLayout.Steps[d].dy;
                        if (nx < 0 || ny < 0 || nx >= _width || ny >= _height) continue;

                        int next = ny * _width + nx;
                        if (span[row + next] != Unreachable) continue;

                        span[row + next] = step;
                        queue[tail++] = next;
                    }
                }
            }

            return span;
        }

        int Manhattan(int a, int b)
        {
            int ax = a % _width, ay = a / _width, bx = b % _width, by = b / _width;
            int dx = ax > bx ? ax - bx : bx - ax;
            int dy = ay > by ? ay - by : by - ay;
            return dx + dy;
        }

        /// <summary>
        /// Which of <see cref="WeaveLayout.Steps"/> leads from <paramref name="a"/> to
        /// <paramref name="b"/>, or -1 when they are not orthogonal neighbours.
        ///
        /// The row wrap is why this is not simply a subtraction: cell 6 of a seven-wide grove and
        /// cell 7 differ by one and are on different rows with the whole grove between them.
        /// </summary>
        int Toward(int a, int b)
        {
            if (a < 0 || b < 0 || a >= _closed.Length || b >= _closed.Length) return -1;

            int ax = a % _width, ay = a / _width, bx = b % _width, by = b / _width;

            if (ax == bx && by == ay - 1) return 0;      // north
            if (ay == by && bx == ax + 1) return 1;      // east
            if (ax == bx && by == ay + 1) return 2;      // south
            if (ay == by && bx == ax - 1) return 3;      // west

            return -1;
        }

        static int Opposite(int dir) => (dir + 2) & 3;
    }
}
