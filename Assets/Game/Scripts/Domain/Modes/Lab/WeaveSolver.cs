using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// How much a Lightweave grove actually asks of a player, counted rather than argued about.
    ///
    /// <para>
    /// <b>Why this exists.</b> <see cref="WeaveGenerator"/> proves a board is <em>solvable</em> by
    /// carving its solution first, and that is the only thing anything here could otherwise say
    /// about a grove. Solvable is not a difficulty: a board where six straight drags all land is
    /// a puzzle in name only, and no test of one board can tell you that a whole ladder is in
    /// that state. Which is what happened — see <see cref="Tally.Slack"/>.
    /// </para>
    /// <para>
    /// <b>What it counts, and why it is not what it used to count.</b> This used to enumerate
    /// <em>fillings</em>: arrangements covering every cell of the grove, because covering every
    /// cell was the win condition. That rule is gone — a channel may now run wherever the player
    /// likes as long as it crosses nothing and threads its own beads — and with it went the
    /// meaning of that number. Without a fill rule the count of legal arrangements is
    /// astronomical and almost all of it is wiggle: the same six routes with a needless kink in
    /// one of them. Counting that would report a board as a thousand times more forgiving than
    /// the one it is, purely because there is empty ground to waste.
    /// </para>
    /// <para>
    /// <b>So the measure is <em>tautness</em>, not quantity.</b> Every pair has a floor — the
    /// fewest cells any route of its could use, <see cref="WeaveLayout.Straight"/> — and a
    /// solution's <em>excess</em> is how far past the sum of those floors it runs.
    /// <see cref="Tally.Slack"/> is the least excess any solution has, so zero means every pair
    /// can be drawn as directly as it could possibly be drawn, all at once, and the board asked
    /// nothing of anybody. <see cref="Tally.Ways"/> then counts only the arrangements a player
    /// plausibly reaches — those within <see cref="Tally.Latitude"/> cells of the best one —
    /// which is a small number about tidy play rather than a large one about wandering.
    /// </para>
    /// <para>
    /// <b>It is an authoring instrument, not a build gate</b>, and the distinction is deliberate.
    /// The search is exponential in the worst case, so the honest answer is sometimes "I ran out
    /// of budget"; a gate that reports that on a slow machine and not on a fast one is a gate
    /// that fails a build for reasons nobody can reproduce. <c>Survey Lightweave</c> uses it to
    /// pick a level's seed, the suite uses it to prove the ladder it picked still holds, and
    /// <c>WeaveMode.Validate</c> does not use it at all — it proves a grove solvable by
    /// <em>playing</em> the arrangement the generator carved, which is exact and costs nothing.
    /// </para>
    /// <para>
    /// <b>One exception, and it is the same code rather than a copy.</b>
    /// <see cref="AnyTautSolution"/> is this search run with an excess budget of zero, which is
    /// small enough to run on the player's phone — and it is exactly the generator's acceptance
    /// bar. So the number a level is authored against and the rule the generator holds out for
    /// are one predicate, not two that can drift apart. Invariant 5b's argument, in the mode
    /// that had two measures and needed one.
    /// </para>
    /// </summary>
    public static class WeaveSolver
    {
        /// <summary>
        /// How many positions may be examined before the search gives up and says so.
        ///
        /// Far more generous than the old filling counter needed, and it costs nothing: bounding
        /// the excess is a much stronger prune than bounding the shape of a filling ever was, so
        /// a board that used to run past 600,000 positions settles in a few thousand. The budget
        /// is here for the pathological board rather than the ordinary one.
        /// </summary>
        public const int NodeBudget = 2_000_000;

        /// <summary>
        /// Where counting stops. A grove with more near-best arrangements than this is not
        /// meaningfully different from one with twice as many.
        /// </summary>
        public const int DefaultCap = 500;

        /// <summary>
        /// How far past the best arrangement still counts as one a player might actually draw.
        ///
        /// <para>
        /// Two, because an excess is always even (see <see cref="WeaveLayout.Straight"/>), so
        /// this is the narrowest band wider than "the best ones only" — it means "the best
        /// arrangements, and the ones a single lazy corner off the best". Wider bands measure
        /// wandering, which no player does on purpose and which every board with spare ground
        /// admits by the thousand.
        /// </para>
        /// </summary>
        public const int Latitude = 2;

        /// <summary>
        /// The most total detour a board may need before this stops trying to measure it.
        ///
        /// A grove that cannot be finished without twenty-odd cells of detour is not a rung any
        /// ladder here wants, so past this the answer is "undecided" rather than a number bought
        /// with an unbounded search.
        /// </summary>
        public const int MaxSlack = 24;

        /// <summary>What a search found, and whether it got to the end.</summary>
        public readonly struct Tally
        {
            /// <summary>
            /// The least total detour any solution of this grove has: how many cells of light,
            /// over and above every pair's own floor, the board makes somebody spend.
            ///
            /// <para>
            /// <b>This is the mode's difficulty, in one integer.</b> Zero means there is an
            /// arrangement in which every single pair is drawn as directly as it could possibly
            /// be drawn — nobody is in anybody's way, and the board is six drags and a
            /// celebration. That is not a hypothetical: it is what the whole chapter measured
            /// before there was anything here to measure it with, and it came back from play as
            /// "each critter is literally next to their matching light".
            /// </para>
            /// <para>
            /// <b>Note what it is not.</b> It is not "the player is forced to go the long way" —
            /// that was the old fill rule, and it is gone. Slack says the pairs cannot
            /// <em>all</em> have their way at once, so somebody has to yield and the player picks
            /// who. Every individual route stays free; what is not free is the whole set of them
            /// at their shortest.
            /// </para>
            /// <para>
            /// An excess is always even, so 2 is the smallest slack there is and the bar reads as
            /// "not everybody can go straight" rather than as a number somebody picked. Zero when
            /// nothing was found, which <see cref="Impossible"/> and <see cref="Exhausted"/> are
            /// how you tell apart.
            /// </para>
            /// </summary>
            public readonly int Slack;

            /// <summary>
            /// How many arrangements land within <see cref="Latitude"/> cells of the best one —
            /// how much of what a tidy player tries will actually work.
            ///
            /// <para>
            /// Meant to <em>fall</em> down a chapter while the groves themselves grow: an opening
            /// grove where most sensible routings work is forgiving, and a closing one with two
            /// is a grove you have to find. Never more than the cap it was given.
            /// </para>
            /// </summary>
            public readonly int Ways;

            /// <summary>
            /// Whether the search finished. False means it hit the cap or the node budget, so
            /// <see cref="Ways"/> is a floor and <see cref="Slack"/> may be an over-estimate.
            /// </summary>
            public readonly bool Exhausted;

            /// <summary>Positions examined. What the budget is spent on, reported for tuning it.</summary>
            public readonly int Nodes;

            /// <summary>Whether anything at all was found.</summary>
            public readonly bool Solved;

            public Tally(int slack, int ways, bool solved, bool exhausted, int nodes)
            {
                Slack = slack;
                Ways = ways;
                Solved = solved;
                Exhausted = exhausted;
                Nodes = nodes;
            }

            /// <summary>
            /// The board cannot be solved at all — proved, not merely not-found.
            ///
            /// <para>
            /// The distinction matters and it has bitten here before. A search that ran out of
            /// budget has seen some arrangements and not others, so "found nothing" from a capped
            /// search is not evidence of anything; a validator wired to it would fail a build
            /// over a board that is perfectly fine. This is only ever true when the search
            /// actually finished.
            /// </para>
            /// </summary>
            public bool Impossible => Exhausted && !Solved;

            /// <summary>
            /// Every pair can be drawn as directly as it could possibly be drawn, all at once.
            /// The one shape of easy that survives any amount of size, colour and congestion.
            /// </summary>
            public bool Taut => Solved && Slack == 0;

            /// <summary>Exactly one near-best arrangement, and the search saw the whole space.</summary>
            public bool Unique => Exhausted && Ways == 1;

            public override string ToString()
                => !Solved ? (Exhausted ? "impossible" : "undecided (" + Nodes + " nodes)")
                 : "slack " + Slack + ", " + Ways + (Exhausted ? "" : "+") + " ways ("
                   + Nodes + " nodes)";
        }

        /// <summary>
        /// Measures a grove: how much detour it forces, and how many near-best ways in it has.
        /// </summary>
        public static Tally Measure(WeaveLayout grove, int cap = DefaultCap,
                                    int budget = NodeBudget)
        {
            if (grove == null || grove.Pairs.Count == 0)
                return new Tally(0, 0, false, true, 0);

            var search = new Search(grove, budget < 1 ? 1 : budget);

            // Deepened by two rather than by one, because an excess cannot be odd. Stopping at
            // the first budget that admits anything is what makes the answer exact: every
            // arrangement with less detour was already looked for and was not there.
            for (int allowance = 0; allowance <= MaxSlack; allowance += 2)
            {
                int found = search.Run(allowance, 1);
                if (search.OutOfBudget) return new Tally(0, 0, false, false, search.Nodes);
                if (found == 0) continue;

                int ways = search.Run(allowance + Latitude, cap < 1 ? 1 : cap);
                bool whole = !search.OutOfBudget && ways < (cap < 1 ? 1 : cap);
                return new Tally(allowance, ways, true, whole, search.Nodes);
            }

            // Nothing inside MaxSlack. Either the board genuinely cannot be solved, or it needs
            // more detour than this is willing to look for — and those are different claims, so
            // it only says "impossible" when the search actually reached the end of every
            // allowance it tried.
            return new Tally(0, 0, false, !search.OutOfBudget, search.Nodes);
        }

        /// <summary>
        /// Whether every pair can be drawn as directly as it could possibly be drawn, all at
        /// once — the generator's acceptance bar, and the one reading cheap enough to take on
        /// the player's phone.
        ///
        /// <para>
        /// <b>It is <see cref="Measure"/> with an excess budget of zero, deliberately.</b> At
        /// zero every step of every channel has to shorten the walk still ahead of it, so the
        /// search is over straight-ish routes only and collapses to a few hundred positions on a
        /// board where the full measurement runs to thousands. That is what lets the generator
        /// hold out for a board with no taut solution while dealing one on a phone, and it means
        /// the bar the generator enforces is literally the number the ladder is authored against
        /// rather than a cheap proxy for it that can drift.
        /// </para>
        /// <para>
        /// <paramref name="decided"/> is false when the budget ran out, and a caller must treat
        /// that as "could not prove this board is hard" rather than as either answer. The
        /// generator does: an undecided candidate is refused, which errs towards throwing away a
        /// good board rather than shipping an easy one.
        /// </para>
        /// </summary>
        public static bool AnyTautSolution(WeaveLayout grove, out bool decided,
                                           int budget = 200_000)
        {
            decided = false;
            if (grove == null || grove.Pairs.Count == 0) return false;

            var search = new Search(grove, budget < 1 ? 1 : budget);
            int found = search.Run(0, 1);

            decided = !search.OutOfBudget;
            return found > 0;
        }

        /// <summary>
        /// One measurement in progress.
        ///
        /// A class rather than a pile of locals because the search is recursive and every array
        /// here is scratch space reused at every depth — allocating them per position is what
        /// makes a solver that works on paper too slow to answer.
        /// </summary>
        sealed class Search
        {
            readonly WeaveLayout _grove;
            readonly int _cells, _pairs, _budget;

            readonly int[] _heart, _critter, _floor;
            readonly int[][] _beads;          // each pair's bead cells
            readonly int[] _beadIndex;        // cell -> its index within its owner's list, else -1
            readonly int[] _reserved;         // cell -> the only pair allowed to stand here, else -1

            /// <summary>
            /// Per pair, the shortest walk that starts on one of its beads, threads a given set
            /// of the others and ends at its critter. What every lower bound is read out of.
            /// </summary>
            readonly int[][] _rest;

            readonly int[] _neighbours;
            readonly int[] _neighbourCount;

            readonly bool[] _onPath;
            readonly bool[] _seen;
            readonly int[] _stack;

            int _cap, _allowance, _found;

            public int Nodes { get; private set; }
            public bool OutOfBudget { get; private set; }

            public Search(WeaveLayout grove, int budget)
            {
                _grove = grove;
                _budget = budget;
                _cells = grove.Count;
                _pairs = grove.Pairs.Count;

                _heart = new int[_pairs];
                _critter = new int[_pairs];
                _floor = new int[_pairs];
                _beads = new int[_pairs][];

                _reserved = new int[_cells];
                _beadIndex = new int[_cells];
                for (int i = 0; i < _cells; i++) { _reserved[i] = -1; _beadIndex[i] = -1; }

                for (int p = 0; p < _pairs; p++)
                {
                    _heart[p] = grove.Pairs[p].Heart;
                    _critter[p] = grove.Pairs[p].Critter;
                    _floor[p] = grove.Straight(p);

                    _reserved[_heart[p]] = p;
                    _reserved[_critter[p]] = p;

                    var owned = grove.BeadsOf(p);
                    _beads[p] = new int[owned.Count];
                    for (int b = 0; b < owned.Count; b++)
                    {
                        _beads[p][b] = owned[b];
                        _reserved[owned[b]] = p;
                        _beadIndex[owned[b]] = b;
                    }
                }

                _rest = new int[_pairs][];
                for (int p = 0; p < _pairs; p++) BuildTours(p);

                _neighbours = new int[_cells * 4];
                _neighbourCount = new int[_cells];
                for (int c = 0; c < _cells; c++)
                {
                    int x = c % grove.Width, y = c / grove.Width, n = 0;
                    for (int s = 0; s < WeaveLayout.Steps.Length; s++)
                    {
                        int nx = x + WeaveLayout.Steps[s].dx, ny = y + WeaveLayout.Steps[s].dy;
                        if (!grove.Inside(nx, ny)) continue;

                        // A hedge is simply not a neighbour, which is the cheapest possible way
                        // for the search to respect one: every walk, every flood fill and every
                        // prune below reads this table, so there is no branch anywhere that could
                        // be written without the barrier in mind.
                        int next = grove.Index(nx, ny);
                        if (!grove.Open(c, next)) continue;

                        _neighbours[c * 4 + n++] = next;
                    }
                    _neighbourCount[c] = n;
                }

                _onPath = new bool[_cells];
                _seen = new bool[_cells];
                _stack = new int[_cells + 8];
            }

            /// <summary>
            /// Works out, for one pair, the shortest tour from each of its beads through each
            /// subset of the rest to its critter. Exact, by dynamic programming over subsets.
            /// </summary>
            void BuildTours(int pair)
            {
                var beads = _beads[pair];
                int n = beads.Length, full = 1 << n;

                var rest = new int[full * (n < 1 ? 1 : n)];
                _rest[pair] = rest;
                if (n == 0) return;

                for (int i = 0; i < n; i++)
                    rest[0 * n + i] = _grove.Span(beads[i], _critter[pair]);

                for (int mask = 1; mask < full; mask++)
                    for (int i = 0; i < n; i++)
                    {
                        if ((mask & (1 << i)) != 0) { rest[mask * n + i] = int.MaxValue; continue; }

                        int best = int.MaxValue;
                        for (int j = 0; j < n; j++)
                        {
                            if ((mask & (1 << j)) == 0) continue;

                            int onward = rest[(mask & ~(1 << j)) * n + j];
                            if (onward == int.MaxValue) continue;

                            int whole = _grove.Span(beads[i], beads[j]) + onward;
                            if (whole < best) best = whole;
                        }
                        rest[mask * n + i] = best;
                    }
            }

            /// <summary>
            /// The fewest further cells this pair could possibly need, standing on
            /// <paramref name="at"/> with <paramref name="left"/> of its beads still to thread.
            ///
            /// Shortest-walk distances throughout — <see cref="WeaveLayout.Span"/>, which is the
            /// straight line on an open grove and goes round a hedge on a walled one. It can
            /// never over-state what is left, which is the whole requirement of a bound used to
            /// prune: one that guessed high would discard arrangements that exist. A Manhattan
            /// distance would still be admissible here and would be a strictly weaker prune,
            /// which on a hedged grove is the difference between a measurement and a budget
            /// running out.
            /// </summary>
            int Ahead(int pair, int at, int left)
            {
                if (left == 0) return _grove.Span(at, _critter[pair]);

                var beads = _beads[pair];
                var rest = _rest[pair];
                int n = beads.Length, best = int.MaxValue;

                for (int i = 0; i < n; i++)
                {
                    if ((left & (1 << i)) == 0) continue;

                    int onward = rest[(left & ~(1 << i)) * n + i];
                    if (onward == int.MaxValue) continue;

                    int whole = _grove.Span(at, beads[i]) + onward;
                    if (whole < best) best = whole;
                }
                return best == int.MaxValue ? int.MaxValue : best;
            }

            /// <summary>
            /// Runs the search with a total-excess allowance, counting up to
            /// <paramref name="cap"/> arrangements. Returns how many it found.
            /// </summary>
            public int Run(int allowance, int cap)
            {
                _allowance = allowance;
                _cap = cap;
                _found = 0;

                for (int i = 0; i < _cells; i++) _onPath[i] = false;

                Begin(0, 0);
                return _found;
            }

            /// <summary>Starts a pair's channel, if there is any point in starting it.</summary>
            void Begin(int pair, int spent)
            {
                if (pair >= _pairs) return;
                if (!Feasible(pair, _heart[pair], 1, Full(pair), spent)) return;

                _onPath[_heart[pair]] = true;
                Extend(pair, _heart[pair], 1, Full(pair), spent);
                _onPath[_heart[pair]] = false;
            }

            int Full(int pair) => (1 << _beads[pair].Length) - 1;

            /// <summary>
            /// Grows one pair's channel a cell at a time, and hands over to the next pair when
            /// it lands on its critter.
            /// </summary>
            void Extend(int pair, int at, int length, int left, int spent)
            {
                if (++Nodes > _budget) { OutOfBudget = true; return; }
                if (_found >= _cap) return;

                int count = _neighbourCount[at];
                for (int k = 0; k < count; k++)
                {
                    int next = _neighbours[at * 4 + k];

                    if (next == _critter[pair])
                    {
                        // Every bead first. A channel that reaches its critter with one still
                        // unthreaded has not finished, and it cannot pass through and come back
                        // — the critter is where a channel ends.
                        if (left != 0) continue;

                        int excess = length + 1 - _floor[pair];
                        if (spent + excess > _allowance) continue;

                        if (pair == _pairs - 1) _found++;
                        else Begin(pair + 1, spent + excess);

                        if (_found >= _cap || OutOfBudget) return;
                        continue;
                    }

                    if (_onPath[next]) continue;
                    if (_reserved[next] >= 0 && _reserved[next] != pair) continue;

                    int now = left;
                    int bead = _beadIndex[next];
                    if (bead >= 0 && _reserved[next] == pair) now = left & ~(1 << bead);

                    if (!Feasible(pair, next, length + 1, now, spent)) continue;

                    _onPath[next] = true;
                    Extend(pair, next, length + 1, now, spent);
                    _onPath[next] = false;

                    if (_found >= _cap || OutOfBudget) return;
                }
            }

            /// <summary>
            /// Whether this position can still become an arrangement inside the allowance.
            ///
            /// <para>
            /// <b>Two tests, and the search does not work without either.</b> The first is the
            /// budget: the pair being drawn cannot finish in fewer cells than the tour still
            /// ahead of it, so a route that has already wandered past what the allowance can pay
            /// for is dead however far away the failure would otherwise show up. That is what
            /// makes bounding the excess a strong prune rather than a filter applied at the end.
            /// </para>
            /// <para>
            /// The second is reachability, and it is what stops the first pair spending the whole
            /// budget on routes that wall off a pair nobody has started yet. Every pair still to
            /// be drawn must be able to reach its own critter and its own beads over ground
            /// nobody has taken — checked for all of them at every position rather than when
            /// their turn comes, because discovering it at their turn means having explored
            /// everything in between.
            /// </para>
            /// </summary>
            bool Feasible(int pair, int at, int length, int left, int spent)
            {
                int ahead = Ahead(pair, at, left);
                if (ahead == int.MaxValue) return false;
                if (spent + length + ahead - _floor[pair] > _allowance) return false;

                if (!CanStillReach(pair, at, left)) return false;

                for (int q = pair + 1; q < _pairs; q++)
                    if (!CanStillReach(q, _heart[q], Full(q))) return false;

                return true;
            }

            /// <summary>
            /// Whether a pair standing on <paramref name="at"/> can still get to everything it
            /// owes over ground nobody has claimed.
            ///
            /// One flood fill: its critter and every bead it has left must come out inside it.
            /// This does not prove a single route reaches all of them in one walk — that is the
            /// search's job — only that none of them has been sealed off, which is the failure
            /// worth spending a flood on.
            /// </summary>
            bool CanStillReach(int pair, int at, int left)
            {
                for (int i = 0; i < _cells; i++) _seen[i] = false;

                int top = 0;
                _stack[top++] = at;
                _seen[at] = true;

                int wanted = 1;                      // the critter
                var beads = _beads[pair];
                for (int i = 0; i < beads.Length; i++) if ((left & (1 << i)) != 0) wanted++;

                int reached = 0;
                while (top > 0)
                {
                    int cell = _stack[--top];

                    if (cell == _critter[pair]) reached++;
                    int bead = _beadIndex[cell];
                    if (bead >= 0 && _reserved[cell] == pair && (left & (1 << bead)) != 0)
                        reached++;

                    if (reached >= wanted) return true;

                    int count = _neighbourCount[cell];
                    for (int k = 0; k < count; k++)
                    {
                        int next = _neighbours[cell * 4 + k];
                        if (_seen[next] || _onPath[next]) continue;
                        if (_reserved[next] >= 0 && _reserved[next] != pair) continue;

                        _seen[next] = true;
                        _stack[top++] = next;
                    }
                }

                return reached >= wanted;
            }
        }
    }
}
