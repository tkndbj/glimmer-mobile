using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>What a search found out about one grove. See <see cref="KeeperSolver.Survey"/>.</summary>
    public readonly struct KeeperSurvey
    {
        /// <summary>Whether the search finished inside its budget. False means "we do not know".</summary>
        public readonly bool Proved;

        /// <summary>Fewest tiles that open every bed, or nought when nothing does.</summary>
        public readonly int Par;

        /// <summary>
        /// How many different groves of exactly <see cref="Par"/> tiles finish it. Invariant 5d,
        /// counted: one is a board with a single answer, and a great many is a board where the
        /// ground and the procession decide nothing.
        /// </summary>
        public readonly int Ways;

        /// <summary>
        /// What a player who never looks past this turn spends, or -1 if they never finish.
        ///
        /// The mode's own thoughtlessness reading: always take the planting that opens the most
        /// beds, then the most flowers, then the most seams. On a chapter's opening levels this
        /// is supposed to work — that is what teaching the verb looks like — and a chapter's
        /// ladder is where it stops being true.
        /// </summary>
        public readonly int Greedy;

        /// <summary>Positions the search looked at, which is what a build gate is really buying.</summary>
        public readonly int Nodes;

        public KeeperSurvey(bool proved, int par, int ways, int greedy, int nodes)
        {
            Proved = proved;
            Par = par;
            Ways = ways;
            Greedy = greedy;
            Nodes = nodes;
        }

        public bool IsSolvable => Proved && Par > 0;
    }

    /// <summary>
    /// The fewest tiles that open every bed of a grove, found by search.
    ///
    /// <para>
    /// <b>Searched rather than authored, for invariant 5's reason.</b> Par decides both star lines
    /// and the basket a run is dealt, so a typed one that has drifted from its board is the
    /// failure with no symptom: one too high hands three stars to a careless run for ever, one too
    /// low makes them unreachable, and neither is visible in the file that caused it.
    /// </para>
    /// <para>
    /// <b>Iterative deepening rather than breadth-first, and the difference is the whole file.</b>
    /// A grove's state is its grid, and the colour of every tile is decided by <em>when</em> it
    /// was laid — so two orderings of the same cells are two different grids, and the frontier of
    /// a breadth-first search grows like permutations rather than like combinations. Deepening on
    /// a total instead lets the two prunes below cut whole subtrees before they are ever built,
    /// and costs only the shallow layers being walked again.
    /// </para>
    /// <para>
    /// <b>Two prunes, and both are exact rather than heuristic.</b> A search that can miss the
    /// shortest answer is worse than no search, because what it returns is a par that looks
    /// perfectly plausible.
    /// </para>
    /// <list type="number">
    /// <item>
    /// <b>The floor.</b> Every empty bed needs a tile of its own, and a bed <paramref name="k"/>
    /// steps from anything standing needs <paramref name="k"/> tiles to reach — a tile may only
    /// be laid beside the grove, so distance is paid a cell at a time. The larger of those two is
    /// a lower bound on what is left to spend, and a position whose spend-so-far plus that bound
    /// is past the limit cannot be part of an answer at this limit.
    /// </item>
    /// <item>
    /// <b>The reach.</b> A tile can only ever matter to a bed by standing <em>on</em> it or
    /// <em>beside</em> it — nothing further away is in any bed's gather — so a tile laid where no
    /// bed can be reached from it inside what is left is a tile the answer would be shorter
    /// without. Dropping it from any solution gives a shorter solution, so no shortest solution
    /// contains one, and cutting it cannot cost the minimum.
    /// </item>
    /// </list>
    /// </summary>
    public static class KeeperSolver
    {
        /// <summary>
        /// Positions a search may look at before it gives up.
        ///
        /// <para>
        /// Large because it has to make a genuinely hard grove <em>provable</em>: a grove the
        /// search cannot prove is a grove with no par, and everything a player is graded against
        /// derives from par. What that costs on a phone is the separate question
        /// <c>KeeperValidator</c> asks, with two far smaller numbers (invariant 26d).
        /// </para>
        /// </summary>
        public const int NodeBudget = 400_000;

        /// <summary>The deepest answer a search will look for. Past this a grove is refused.</summary>
        public const int MaxTiles = 20;

        /// <summary>Most winning groves <see cref="KeeperSurvey.Ways"/> will count before it stops.</summary>
        public const int MaxWays = 4000;

        /// <summary>The fewest tiles that open every bed, or nought when the search could not.</summary>
        public static int Par(KeeperLayout layout) => Survey(layout).Par;

        /// <summary>Everything a search can say about a grove, in one walk per question.</summary>
        public static KeeperSurvey Survey(KeeperLayout layout)
        {
            if (layout == null) return new KeeperSurvey(true, 0, 0, -1, 0);

            var search = new Search(layout);
            return search.Run();
        }

        /// <summary>
        /// What a player who never looks past this turn spends, or -1 if they never finish.
        ///
        /// Its own walk rather than a by-product of the search, because it is a different
        /// question: not "how few could this be done in" but "does thoughtlessness work here".
        /// </summary>
        public static int Greedy(KeeperLayout layout, int budget)
        {
            if (layout == null) return -1;

            var run = new KeeperRun(layout, budget);
            var openings = new List<int>(layout.Count);
            var bloomed = new List<int>(KeeperFlourish.Most);

            // Bounded by the basket rather than trusted to end: a greedy player who can neither
            // plant nor usefully compost would otherwise walk for ever on an unbounded grove.
            int ceiling = budget > 0 && budget < MaxTiles * 4 ? budget : MaxTiles * 4;

            for (int spent = 0; spent < ceiling; spent++)
            {
                if (run.Board.IsFinished) return run.Spent;

                run.Openings(openings);
                if (openings.Count == 0)
                {
                    if (!run.Compost()) return -1;
                    continue;
                }

                int best = -1;
                var bestGain = KeeperGain.Nothing;

                for (int i = 0; i < openings.Count; i++)
                {
                    var gain = run.Preview(openings[i]);
                    if (best >= 0 && !Better(gain, bestGain)) continue;

                    best = openings[i];
                    bestGain = gain;
                }

                run.Plant(best, bloomed);
            }

            return run.Board.IsFinished ? run.Spent : -1;
        }

        /// <summary>Beds first, then flowers, then seams — the order a player reads them in.</summary>
        static bool Better(KeeperGain a, KeeperGain b)
        {
            if (a.Beds != b.Beds) return a.Beds > b.Beds;
            if (a.Blooms != b.Blooms) return a.Blooms > b.Blooms;
            return a.Seams > b.Seams;
        }

        /// <summary>
        /// One iterative-deepening walk of a grove, with the board mutated in place and taken
        /// back again.
        ///
        /// <para>
        /// A class rather than a pile of static methods with eleven parameters: the search carries
        /// a board, a distance table, a transposition set and three counters, and threading those
        /// through a recursion by hand is where an off-by-one in a prune hides.
        /// </para>
        /// <para>
        /// <b>Every buffer is a field and nothing allocates inside the walk.</b> This runs on a
        /// player's phone the first time somebody opens a level (invariant 26d), a few hundred
        /// thousand positions deep, so a list built per node is a few hundred thousand lists.
        /// </para>
        /// </summary>
        sealed class Search
        {
            readonly KeeperLayout _layout;
            readonly KeeperBoard _board;
            readonly int[] _cells;

            /// <summary>Steps from each cell to the nearest bed, over ground a tile could stand on.</summary>
            readonly int[] _toBed;

            /// <summary>Where each bed is, so the floor does not walk the whole grid per node.</summary>
            readonly int[] _beds;

            /// <summary>One fan of openings per depth, so a walk never allocates one.</summary>
            readonly int[][] _fan;

            readonly List<int> _openings = new List<int>();
            readonly HashSet<string> _seen = new HashSet<string>();
            readonly char[] _key;

            /// <summary>Channels one tile can carry: three where the deal holds a prism, one otherwise.</summary>
            readonly int _perTile;

            /// <summary>The unopened beds, what each still costs, and which cluster it is in.</summary>
            readonly int[] _live, _need, _group;
            readonly bool[] _bare;

            int _nodes, _limit, _ways;
            bool _budgetSpent;

            public Search(KeeperLayout layout)
            {
                _layout = layout;
                _board = new KeeperBoard(layout);
                _cells = _board.Cells;
                _toBed = DistanceToBeds(layout);
                _key = new char[layout.Count + 2];

                var beds = new List<int>();
                for (int i = 0; i < layout.Count; i++) if (layout.IsBed(i)) beds.Add(i);
                _beds = beds.ToArray();

                _perTile = layout.Deal.Prisms > 0 ? 3 : 1;

                _live = new int[_beds.Length];
                _need = new int[_beds.Length];
                _group = new int[_beds.Length];
                _bare = new bool[_beds.Length];

                _fan = new int[MaxTiles + 2][];
                for (int i = 0; i < _fan.Length; i++) _fan[i] = new int[layout.Count];
            }

            public KeeperSurvey Run()
            {
                if (_beds.Length == 0) return new KeeperSurvey(true, 0, 0, -1, 0);
                if (_board.IsFinished) return new KeeperSurvey(true, 0, 1, -1, 0);

                // A bed nothing can ever be planted on makes the answer "no" outright, and saying
                // so here is worth twenty iterations of a search that was always going to fail.
                if (_board.AnyBedLost()) return new KeeperSurvey(true, 0, 0, -1, 0);

                for (int limit = Start(); limit <= MaxTiles; limit++)
                {
                    _limit = limit;
                    _ways = 0;
                    _seen.Clear();

                    Walk(0, 0);

                    if (_budgetSpent) return new KeeperSurvey(false, 0, 0, -1, _nodes);
                    if (_ways > 0) return new KeeperSurvey(true, limit, _ways, -1, _nodes);
                }

                return new KeeperSurvey(false, 0, 0, -1, _nodes);
            }

            int Start()
            {
                int floor = Floor();
                return floor < 1 ? 1 : floor;
            }

            /// <summary>
            /// Walks every move from this position, taking each one back afterwards.
            ///
            /// <paramref name="spent"/> is both the depth and the index into the procession, which
            /// is the whole reason a grove's state is its grid and nothing else: what the next
            /// tile will be is decided by how many have gone.
            /// </summary>
            void Walk(int spent, int composted)
            {
                if (_budgetSpent) return;
                if (++_nodes > NodeBudget) { _budgetSpent = true; return; }

                // Deduped before the win is read, so what Ways counts is distinct groves rather
                // than distinct orderings of the same one, which is the number invariant 5d asks
                // for.
                if (!Fresh(spent)) return;

                if (_board.IsFinished)
                {
                    if (spent == _limit && _ways < MaxWays) _ways++;
                    return;
                }

                if (spent >= _limit) return;
                if (spent + Floor() > _limit) return;

                int colour = _layout.Deal.At(spent);
                int room = _limit - spent;

                _board.Openings(colour, _openings);

                // Into this depth's own fan, because the walk below plants into the board and the
                // next Openings call would rewrite the very list this loop is reading.
                var fan = _fan[spent];
                int count = _openings.Count;
                for (int i = 0; i < count; i++) fan[i] = _openings[i];

                for (int i = 0; i < count; i++)
                {
                    int at = fan[i];

                    // The reach prune. A tile that cannot get within reach of a bed in what is
                    // left is a tile the answer would be shorter without.
                    if (_toBed[at] > room) continue;

                    _cells[at] = colour;
                    Walk(spent + 1, 0);
                    _cells[at] = Energy.None;

                    if (_budgetSpent) return;
                }

                // Composting: the grove stands still and the procession moves on. A lap of the
                // deal is the most that can ever be learnt by doing it, so anything past that
                // repeats a position that has already been walked.
                if (composted < _layout.Deal.Count - 1) Walk(spent + 1, composted + 1);
            }

            /// <summary>
            /// A lower bound on what is still to spend: one tile for every bed still bare, and at
            /// least as many as the furthest bare bed stands away from anything already planted.
            ///
            /// <para>
            /// The distance is measured straight rather than round the stone, which underestimates
            /// and is exactly what a bound has to do: a floor that could ever be too high would
            /// prune the shortest answer and hand back a par nothing can reach.
            /// </para>
            /// </summary>
            int Floor()
            {
                int live = 0, furthest = 0;

                for (int i = 0; i < _beds.Length; i++)
                {
                    int bed = _beds[i];
                    if (_board.IsOpen(bed)) continue;

                    _live[live] = bed;
                    _group[live] = live;

                    if (_cells[bed] != Energy.None)
                    {
                        // Every channel it still wants has to arrive on a bare cell beside it,
                        // and one placement brings one cell.
                        _need[live] = Channels(Energy.All & ~_board.Gathered(bed));
                        _bare[live] = false;
                    }
                    else
                    {
                        // Its own tile, plus a placement for every channel that tile cannot
                        // carry. A prism carries all three, so a deal holding one is given the
                        // benefit of the doubt: a bound that could ever be too high would prune
                        // the shortest answer and hand back a par nothing can reach.
                        _need[live] = 1 + Room(Channels(Energy.All & ~Around(bed)) - _perTile);
                        _bare[live] = true;

                        int steps = StepsTo(bed);
                        if (steps > furthest) furthest = steps;
                    }

                    live++;
                }

                if (live == 0) return 0;

                Cluster(live);

                int total = 0;
                for (int i = 0; i < live; i++)
                {
                    if (_group[i] != i) continue;          // one reading per cluster

                    int cost = 0, bare = 0;
                    for (int j = 0; j < live; j++)
                    {
                        if (_group[j] != i) continue;
                        if (_need[j] > cost) cost = _need[j];
                        if (_bare[j]) bare++;
                    }

                    total += bare > cost ? bare : cost;
                }

                return total > furthest ? total : furthest;
            }

            /// <summary>
            /// Groups the beds whose neighbourhoods touch, so their costs may be <em>added</em>
            /// rather than merely maximised.
            ///
            /// <para>
            /// <b>This is what makes a grove with several beds provable at all.</b> A bed's whole
            /// cost — its own cell and the feeders that bring it the channels it lacks — is spent
            /// inside its closed neighbourhood, so two beds more than two steps apart cannot share
            /// a single tile of it. Summing across those is therefore still a floor, and it is a
            /// far higher one: taking the maximum instead left a two-bed grove with a bound of
            /// three against a real answer of six, and the search walked a quarter of a million
            /// positions it could have cut.
            /// </para>
            /// <para>
            /// The distance term stays a <em>maximum</em> and is compared rather than added,
            /// because a path to one bed may well be a path to another. That is the one part of
            /// this that could double-count, so it is the one part that is not summed.
            /// </para>
            /// </summary>
            void Cluster(int live)
            {
                bool moved = true;
                while (moved)
                {
                    moved = false;

                    for (int i = 0; i < live; i++)
                        for (int j = i + 1; j < live; j++)
                        {
                            if (_group[i] == _group[j] || !Near(_live[i], _live[j])) continue;

                            int lo = _group[i] < _group[j] ? _group[i] : _group[j];
                            int hi = _group[i] < _group[j] ? _group[j] : _group[i];

                            for (int k = 0; k < live; k++)
                                if (_group[k] == hi) _group[k] = lo;

                            moved = true;
                        }
                }
            }

            /// <summary>
            /// Whether two beds are close enough to share a cell. Their closed neighbourhoods
            /// meet exactly when they are two steps apart or fewer.
            /// </summary>
            bool Near(int a, int b)
            {
                int width = _layout.Width;
                int dx = a % width - b % width, dy = a / width - b / width;
                if (dx < 0) dx = -dx;
                if (dy < 0) dy = -dy;

                return dx + dy <= 2;
            }

            static int Room(int n) => n < 0 ? 0 : n;

            /// <summary>Every channel standing beside this cell, whatever is on the cell itself.</summary>
            int Around(int index)
            {
                int width = _layout.Width;
                int x = index % width, y = index / width;
                int mask = Energy.None;

                if (y > 0) mask |= _cells[index - width];
                if (x < width - 1) mask |= _cells[index + 1];
                if (y < _layout.Height - 1) mask |= _cells[index + width];
                if (x > 0) mask |= _cells[index - 1];

                return mask;
            }

            static int Channels(int mask)
            {
                int n = 0;
                if ((mask & Energy.R) != 0) n++;
                if ((mask & Energy.G) != 0) n++;
                if ((mask & Energy.B) != 0) n++;
                return n;
            }

            /// <summary>
            /// How few plantings could put a tile on this cell: the straight-line distance to the
            /// nearest tile already standing, which is one planting per step.
            /// </summary>
            int StepsTo(int cell)
            {
                int width = _layout.Width;
                int cx = cell % width, cy = cell / width;
                int best = int.MaxValue;

                for (int i = 0; i < _cells.Length; i++)
                {
                    if (_cells[i] == Energy.None) continue;

                    int dx = i % width - cx, dy = i / width - cy;
                    if (dx < 0) dx = -dx;
                    if (dy < 0) dy = -dy;

                    int steps = dx + dy;
                    if (steps < best) best = steps;
                }

                return best == int.MaxValue ? 1 : best;
            }

            /// <summary>
            /// Whether this exact grove has been walked at this exact depth before.
            ///
            /// Both halves are needed: the same grid reached in fewer tiles is a different
            /// position, because the tile in hand is decided by how many have gone.
            /// </summary>
            bool Fresh(int spent)
            {
                _key[0] = (char)('0' + spent);
                for (int i = 0; i < _cells.Length; i++) _key[i + 1] = (char)('0' + _cells[i]);

                return _seen.Add(new string(_key, 0, _cells.Length + 1));
            }

            /// <summary>
            /// Steps from every cell to the nearest bed, over ground a tile could ever stand on
            /// and ignoring what happens to be standing.
            ///
            /// A fact about the <em>layout</em>, so it is worked out once rather than per
            /// position, which is what makes the reach prune free.
            /// </summary>
            static int[] DistanceToBeds(KeeperLayout layout)
            {
                var steps = new int[layout.Count];
                for (int i = 0; i < steps.Length; i++) steps[i] = int.MaxValue;

                var queue = new List<int>(layout.Count);
                for (int i = 0; i < layout.Count; i++)
                {
                    if (!layout.IsBed(i)) continue;
                    steps[i] = 0;
                    queue.Add(i);
                }

                for (int head = 0; head < queue.Count; head++)
                {
                    int at = queue[head];
                    int x = at % layout.Width, y = at / layout.Width;

                    if (y > 0) Nearer(layout, at - layout.Width, steps[at], steps, queue);
                    if (x < layout.Width - 1) Nearer(layout, at + 1, steps[at], steps, queue);
                    if (y < layout.Height - 1)
                        Nearer(layout, at + layout.Width, steps[at], steps, queue);
                    if (x > 0) Nearer(layout, at - 1, steps[at], steps, queue);
                }

                return steps;
            }

            static void Nearer(KeeperLayout layout, int at, int from, int[] steps, List<int> queue)
            {
                if (steps[at] != int.MaxValue || !layout.IsPlantable(at)) return;

                steps[at] = from + 1;
                queue.Add(at);
            }
        }
    }
}
