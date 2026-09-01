using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>What a search found out about a well.</summary>
    public readonly struct FallSurvey
    {
        /// <summary>Fewest drops that empty the well without flooding it, or -1 if none was proved.</summary>
        public readonly int Par;

        /// <summary>
        /// How many distinct drop sequences of exactly <see cref="Par"/> length empty it,
        /// counted up to <see cref="FallSolver.MostWays"/>.
        ///
        /// <para>
        /// <b>Invariant 5d's instrument for this mode.</b> A well with hundreds of par-length
        /// solutions is one where almost anything works, so the colours and the ordering are
        /// deciding nothing and the level is decoration however pretty it looks. A well with
        /// one is a puzzle. It falls down a chapter exactly as Lightweave's <c>ways</c> does,
        /// and for the same reason it is a reading rather than a gate.
        /// </para>
        /// </summary>
        public readonly int Ways;

        /// <summary>
        /// Drops a player who never looks ahead would take, or -1 when that player loses.
        ///
        /// <para>
        /// The greedy policy: take the drop that bursts the most motes this turn, preferring one
        /// that enriches, then the leftmost. It is the honest proxy for "does this board ask
        /// anything" — if thoughtlessness clears it, the answer is no. Reported rather than
        /// gated, because on a chapter's opening levels thoughtlessness is <em>supposed</em> to
        /// work: that is what teaching the verb looks like.
        /// </para>
        /// </summary>
        public readonly int Greedy;

        /// <summary>
        /// How many of a lens's two sideways shots land on something, at its best on this board.
        ///
        /// <para>
        /// <b>Invariant 5d's instrument for the lens, and it needs one for the same reason
        /// <see cref="Ways"/> does.</b> Glass whose every beam leaves the well the moment it sets
        /// off has been charged over three drops to do nothing at all. That board validates, the
        /// glass fills, it fires, and it would play exactly the same with the lens taken out of it.
        /// </para>
        /// <para>
        /// <b>Out of two rather than out of four, because a lens filled the ordinary way fires
        /// sideways.</b> Counting the vertical pair flattered every board in the chapter: a well
        /// has gravity, so a lens rests on something and its downward beam always lands, on the
        /// cell it is standing on, having crossed nothing. That is a shot scoring a mark for
        /// travelling one cell into the thing already holding it up. All four are counted only
        /// for a lens another lens strikes, and where a lens has fallen to by then is not a
        /// position any cheap check can enumerate.
        /// </para>
        /// <para>
        /// Geometry of the authored position rather than a proof, and it warns rather than
        /// refuses: a well collapses under a chain, so a lens fires from wherever it has fallen
        /// to, which is not a position any cheap check can enumerate. Nought on a board with no
        /// glass on it, which is every board of the first chapter.
        /// </para>
        /// </summary>
        public readonly int Aim;

        /// <summary>The longest single shot, in cells, that lands. See <see cref="Aim"/>.</summary>
        public readonly int Reach;

        /// <summary>Positions the search had to look at. Watched against <see cref="FallSolver.NodeBudget"/>.</summary>
        public readonly int Nodes;

        /// <summary>Whether the search finished rather than running out of budget.</summary>
        public readonly bool Proved;

        public FallSurvey(int par, int ways, int greedy, int aim, int reach, int nodes,
                          bool proved)
        {
            Par = par;
            Ways = ways;
            Greedy = greedy;
            Aim = aim;
            Reach = reach;
            Nodes = nodes;
            Proved = proved;
        }

        public bool IsSolvable => Par > 0;

        public static readonly FallSurvey Unproved = new FallSurvey(-1, 0, -1, 0, 0, 0, false);
    }

    /// <summary>
    /// Proves a well can be emptied, and in how few drops.
    ///
    /// <para>
    /// <b>Par is searched, never authored</b> (invariant 5). A typed par is the failure that has
    /// no symptom: one too high hands three stars to a careless run for ever, one too low makes
    /// them unreachable, and neither is visible in the file that caused it. Everything a
    /// Lightfall level is graded by falls out of this one number — the two star lines and the
    /// supply it is dealt are all multiples of it — so a level authors a well, a fill and a
    /// procession and no difficulty number at all.
    /// </para>
    /// <para>
    /// <b>It plays the game rather than modelling it.</b> Every position is reached by calling
    /// <c>FallBoard.Drop</c>, the same method the screen calls, with the step list left null so
    /// nothing is allocated to describe waves nobody will watch. A second implementation of the
    /// burst-and-wash rule is the thing invariant 9a exists to refuse — and this rule is subtle
    /// enough (the wash is read before the fall, from the positions the bursting motes stood in)
    /// that a copy would be wrong within a drop.
    /// </para>
    /// <para>
    /// <b>Breadth-first, and that is the shape the rule earns.</b> The procession is fixed, so
    /// the colour of drop <c>d</c> is known before the search starts and depth <em>is</em> the
    /// drop count — there is no iterative deepening to do. A burst chain destroys a large part
    /// of a well in one drop, so par stays small (single figures on everything that ships) and
    /// positions converge hard: a hundred orders of the same handful of useful drops arrive at
    /// the same board and are counted once.
    /// </para>
    /// <para>
    /// <b>It runs on the player's phone, at level load, once.</b> <see cref="NodeBudget"/> is
    /// what makes that safe, and <c>FallValidator</c> refuses to ship a well that comes near it
    /// — so a board that cannot be proved is a build failure rather than a hitch on somebody's
    /// device. See <c>FallSetup</c> for the cache, and for what happens in the case the build
    /// gate is supposed to have made impossible.
    /// </para>
    /// </summary>
    public static class FallSolver
    {
        /// <summary>
        /// Positions one search may look at before it gives up.
        ///
        /// <para>
        /// Chosen against what it costs on the device rather than against what a desktop can
        /// afford: the frontier holds forked boards and the visited set holds eight bytes each,
        /// so a quarter of a million is a few megabytes for a few tens of milliseconds, once,
        /// while a chapter body is being read. Every shipped well is proved in a small fraction
        /// of it and the validator prints the figure, so a board drifting towards the ceiling is
        /// visible long before it reaches one.
        /// </para>
        /// </summary>
        public const int NodeBudget = 250_000;

        /// <summary>
        /// Deepest a search will look. A well needing more drops than this is not a hard level,
        /// it is a level whose author has lost control of it.
        /// </summary>
        public const int MaxDrops = 28;

        /// <summary>Where <see cref="FallSurvey.Ways"/> stops counting. A reading, not a total.</summary>
        public const int MostWays = 100_000;

        /// <summary>
        /// Fewest drops that empty this well, or -1 if none was proved inside the budget.
        ///
        /// What the phone calls. <see cref="Survey"/> is the same search with the authoring
        /// readings kept.
        /// </summary>
        public static int Par(FallLayout layout) => Search(layout, false).Par;

        /// <summary>The same search, with everything an author needs to judge the board by.</summary>
        public static FallSurvey Survey(FallLayout layout)
        {
            var found = Search(layout, true);
            Blast(layout, out int aim, out int reach);

            return new FallSurvey(found.Par, found.Ways, Greedy(layout), aim, reach,
                                  found.Nodes, found.Proved);
        }

        readonly struct Found
        {
            public readonly int Par, Ways, Nodes;
            public readonly bool Proved;

            public Found(int par, int ways, int nodes, bool proved)
            {
                Par = par;
                Ways = ways;
                Nodes = nodes;
                Proved = proved;
            }
        }

        static Found Search(FallLayout layout, bool countWays)
        {
            if (layout == null) return new Found(-1, 0, 0, false);

            var start = new FallBoard(layout);
            if (start.IsEmpty) return new Found(0, 1, 0, true);

            var deal = layout.Deal;

            // The frontier is the positions reachable in exactly `drops` drops, deduplicated by
            // what is standing in the well. `paths` counts how many orders arrive at each, which
            // is what makes Ways exact rather than a sample.
            var frontier = new List<FallBoard> { start };
            var paths = countWays ? new List<int> { 1 } : null;

            var seen = new HashSet<ulong> { start.Signature() };

            var next = new List<FallBoard>();
            var nextPaths = countWays ? new List<int>() : null;
            var index = new Dictionary<ulong, int>();

            int nodes = 0;

            for (int drops = 1; drops <= MaxDrops; drops++)
            {
                int colour = deal.At(drops - 1);

                next.Clear();
                nextPaths?.Clear();
                index.Clear();

                int ways = 0;
                bool emptied = false;

                for (int f = 0; f < frontier.Count; f++)
                {
                    var board = frontier[f];
                    int arrivals = countWays ? paths[f] : 1;

                    for (int x = 0; x < layout.Width; x++)
                    {
                        if (!board.CanDrop(colour, x)) continue;

                        if (++nodes > NodeBudget) return new Found(-1, 0, nodes, false);

                        var child = board.Fork();
                        child.Drop(colour, x);

                        // A flooded well is a dead position rather than a losing one: par is the
                        // fewest drops that empty it *without* ever breaching the brim, because
                        // a run that breaches it is over and never reaches an empty board. This
                        // one line is what keeps the two fail states from disagreeing about
                        // which boards are winnable.
                        if (child.Flooded) continue;

                        if (child.IsEmpty)
                        {
                            emptied = true;
                            if (!countWays) return new Found(drops, 1, nodes, true);

                            ways = ways > MostWays - arrivals ? MostWays : ways + arrivals;
                            continue;
                        }

                        // Nothing past the answer is worth expanding.
                        if (emptied) continue;

                        ulong key = child.Signature();
                        if (!seen.Add(key))
                        {
                            // Already reachable in this many drops or fewer. Counting the extra
                            // orders that arrive here would be counting routes to a position,
                            // not routes to an answer, so only the first depth to find it holds
                            // a path count — which is the depth par is read at.
                            if (countWays && index.TryGetValue(key, out int at))
                                nextPaths[at] = nextPaths[at] > MostWays - arrivals
                                              ? MostWays : nextPaths[at] + arrivals;
                            continue;
                        }

                        next.Add(child);
                        if (!countWays) continue;

                        index[key] = nextPaths.Count;
                        nextPaths.Add(arrivals);
                    }
                }

                if (emptied) return new Found(drops, ways < 1 ? 1 : ways, nodes, true);
                if (next.Count == 0) return new Found(-1, 0, nodes, true);   // proved unsolvable

                var swap = frontier; frontier = next; next = swap;

                if (!countWays) continue;

                var swapPaths = paths; paths = nextPaths; nextPaths = swapPaths;
            }

            // Deeper than MaxDrops. Reported as unproved rather than unsolvable: the difference
            // matters to whoever has to fix it, and only one of the two is a bug in the search.
            return new Found(-1, 0, nodes, false);
        }

        /// <summary>
        /// What the glass on this board is pointing at, as geometry rather than as play: the
        /// best lens's landing count out of two, and the longest single shot that lands.
        ///
        /// <para>
        /// Read off the authored position with every lens treated as though it were full, which
        /// is the only cheap way to ask the question at all — actually filling one takes three
        /// drops of three colours, so no single trial drop can ever set one off and the old
        /// reading (drop everything once, watch the beams) would have answered nought on every
        /// board in the chapter.
        /// </para>
        /// <para>
        /// <b>A beam that lands on nothing does not count, and leaving it in makes the number
        /// mean nothing.</b> A lens with open sky above it throws one straight out of the well
        /// every time, as far as the ceiling, which measured as reach would score full marks for
        /// light that got nowhere. That was written the wrong way round first and every fill of
        /// a whole shape scored six.
        /// </para>
        /// </summary>
        static void Blast(FallLayout layout, out int aim, out int reach)
        {
            aim = 0;
            reach = 0;

            for (int at = 0; at < layout.Count; at++)
            {
                if (!FallCell.IsLens(layout.At(at))) continue;

                int lands = 0;

                for (int n = 0; n < Across.Length; n++)
                {
                    int x = at % layout.Width, y = at / layout.Width;
                    int travelled = 0;

                    while (true)
                    {
                        x += Across[n].dx;
                        y += Across[n].dy;
                        travelled++;

                        if (x < 0 || y < 0 || x >= layout.Width || y >= layout.Height) break;
                        if (layout.At(y * layout.Width + x) == FallCell.Empty) continue;

                        lands++;
                        if (travelled > reach) reach = travelled;
                        break;
                    }
                }

                if (lands > aim) aim = lands;
            }
        }

        static readonly (int dx, int dy)[] Across = { (1, 0), (-1, 0) };

        /// <summary>
        /// How a player who never looks ahead would get on: take the drop that bursts the most
        /// motes now, preferring one that enriches, then the leftmost.
        ///
        /// <para>
        /// Bounded by <see cref="MaxDrops"/> rather than by the level's own budget, because the
        /// budget is derived from par and par is what this is being reported alongside. What it
        /// answers is "does thoughtlessness clear this board", and how many drops thoughtlessness
        /// needs is the interesting half of that.
        /// </para>
        /// </summary>
        static int Greedy(FallLayout layout)
        {
            var board = new FallBoard(layout);
            var deal = layout.Deal;

            for (int drops = 0; drops < MaxDrops; drops++)
            {
                if (board.IsEmpty) return drops;

                int colour = deal.At(drops);
                int bestColumn = -1, bestBurst = -1;
                bool bestEnriches = false;

                for (int x = 0; x < layout.Width; x++)
                {
                    if (!board.CanDrop(colour, x)) continue;

                    var trial = board.Fork();
                    var result = trial.Drop(colour, x);
                    if (result == null || trial.Flooded) continue;

                    int burst = result.Burst;
                    bool enriches = board.Enriches(colour, x);

                    bool better = burst > bestBurst ||
                                  (burst == bestBurst && enriches && !bestEnriches);

                    if (!better) continue;

                    bestColumn = x;
                    bestBurst = burst;
                    bestEnriches = enriches;
                }

                if (bestColumn < 0) return -1;                  // every drop floods it
                board.Drop(colour, bestColumn);
            }

            return board.IsEmpty ? MaxDrops : -1;
        }
    }
}
