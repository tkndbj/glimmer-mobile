using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// Builds a Lightweave puzzle that is guaranteed to be solvable, by building its solution.
    ///
    /// <para>
    /// <b>Carve, then hide.</b> Self-avoiding walks are grown through the grove, each one only
    /// ever entering cells no earlier walk took. What is handed to the player is the two ends of
    /// each walk, a few beads dropped along them, and nothing else — so the board they are given
    /// demonstrably has at least one arrangement, because the generator just drew one. The
    /// alternative, scattering endpoints and checking afterwards, is both slower and worse: most
    /// scatterings have no solution at all, and a search that says so cannot tell you what to do
    /// about it.
    /// </para>
    /// <para>
    /// <b>Difficulty is contention, and the bar is that nobody can have their way.</b> This used
    /// to be congestion — a board was accepted only when its channels covered every cell, because
    /// covering every cell was also what the player had to do. That rule is gone: a channel now
    /// runs wherever the player likes. So the carve still fills the grove, but for a different
    /// reason and to a different end — filling is what pushes the endpoints out to the edges and
    /// interleaves the routes between them, and what a board is now <em>accepted</em> on is
    /// <see cref="WeaveSolver.AnyTautSolution"/>: there must be no arrangement in which every
    /// single pair is drawn as directly as it could possibly be drawn. One pair always can; the
    /// board is refused when all of them can at once, because that board is six drags and a
    /// celebration.
    /// </para>
    /// <para>
    /// <b>A walk is given a budget, and that is what made any of this work.</b> Warnsdorff hugs
    /// the edge of the free space, so an unbounded first walk is very nearly Hamiltonian: it eats
    /// the grove and leaves scraps that cannot make a second pair, let alone a sixth. Measured,
    /// that rejected 3,993 attempts in every 4,000 and drove the generator onto its straight-rows
    /// fallback for more than half of all seeds — a mode-wide difficulty failure that no test of
    /// a single board would ever have shown. Capping each walk near its fair share of what is
    /// left turns the acceptance rate from 0.2% into roughly one attempt in two.
    /// </para>
    /// <para>
    /// Deterministic from its seed, so a level deals the same puzzle to everybody for ever, a
    /// retry meets the board the player just failed, and a report about a bad board is
    /// reproducible from the level id alone.
    /// </para>
    /// </summary>
    public static class WeaveGenerator
    {
        /// <summary>
        /// The least of the grove a carve may occupy.
        ///
        /// Not the acceptance bar — <see cref="Build"/> holds out for a carve that reaches every
        /// cell — but the floor below which a board is reported as slack, by
        /// <c>WeaveMode.Validate</c> and by the suite. A carve that leaves islands behind tends
        /// to deal a board where each pair has its own quiet corner and never meets anybody.
        /// </summary>
        public const float MinCoverage = .92f;

        /// <summary>The fewest cells a path may run through, endpoints included.</summary>
        public const int MinPathLength = 5;

        /// <summary>
        /// The least a pair's two ends may be apart, as a straight-line cell count.
        ///
        /// <para>
        /// <b>This is the "place the critters cleverly" bar, and it is a rule about where things
        /// stand rather than about which way anybody has to go.</b> A crystal three cells from
        /// its critter is joined by a reflex — the player does not decide anything, they flick at
        /// it — and a board with two of those on it hands away a third of itself before the
        /// thinking starts. Half the grove's own span, so it scales with the board instead of
        /// being a number that stops meaning anything at 7x9.
        /// </para>
        /// <para>
        /// <b>Why this is allowed to exist now when the old per-pair rule had to go.</b> The rule
        /// this replaces refused a pair whose <em>carved route</em> was the straight line between
        /// its ends, and the argument for it was that a close pair whose route has to run right
        /// round the grove is the mode's finest trick. That argument depended entirely on the
        /// fill rule: something had to make the long way round compulsory, and covering every
        /// cell was what did. With routing free there is no trick left in a close pair — the
        /// short way works, so the short way is what happens, and the only honest way to make a
        /// drag a decision is to put the two ends far enough apart that there is more than one
        /// sensible way to get between them. So the bar moved from the route to the placement,
        /// which is also the one of the two the player can see before they commit to anything.
        /// </para>
        /// <para>
        /// Measured against the raw distance between the ends and deliberately not against
        /// <see cref="WeaveLayout.Straight"/>: a bead lifts a pair's floor, so a gated pair could
        /// clear a bar on its floor while its crystal sat next to its critter — and then the bead
        /// would be carrying a pair that should not have been dealt that way in the first place.
        /// </para>
        /// </summary>
        /// <remarks>
        /// A third of the grove's span plus two, and never more than six. Both halves are
        /// measured rather than chosen: below five, boards come out full of two-cell flicks; at
        /// seven, six-pair groves stop being dealable at all — a filled carve simply does not
        /// have that many long walks in it, and the generator falls back to a board that clears
        /// no bar whatsoever, which is worse than the close pair it was trying to avoid. Six is
        /// the largest value every shape this mode ships can actually meet.
        /// </remarks>
        public static int MinReach(int width, int height)
        {
            int scaled = (width + height) / 3 + 2;
            return scaled > 6 ? 6 : scaled;
        }

        /// <summary>
        /// The least total detour a shipped grove may force — <c>WeaveSolver.Tally.Slack</c>.
        ///
        /// <para>
        /// <b>This is the acceptance bar, and it is the mode's whole difficulty argument.</b>
        /// Slack is how many cells of light, over and above every pair's own floor, the board
        /// makes somebody spend. Zero means there is an arrangement in which every pair goes as
        /// directly as it possibly could, so the board is joined by drawing the obvious line six
        /// times — which is what the chapter did before anything was measuring it, and it came
        /// back from play as "each critter is literally next to their matching light".
        /// </para>
        /// <para>
        /// <b>Why this is not the old rule wearing a new name.</b> What used to be enforced was a
        /// <em>per-pair</em> detour: no channel could be the straight line between its own ends,
        /// so every pair was sent the long way round and the player was walking a route the board
        /// had already picked. This is a bar on the pairs <em>together</em>. Any one route may
        /// still be perfectly direct — most are — and what the board denies is all of them being
        /// direct at the same time. So the question a grove asks is "who yields", the player
        /// chooses, and nothing is forced on anybody in particular.
        /// </para>
        /// <para>
        /// <b>Two, because a detour cannot be odd.</b> A route and the floor it is measured
        /// against always share a parity, so two is the smallest slack there is and the bar reads
        /// as "not everybody at once" rather than as a number somebody picked.
        /// </para>
        /// </summary>
        public const int MinSlack = 2;

        /// <summary>
        /// How hard the acceptance bar may search before it gives up on a candidate.
        ///
        /// <para>
        /// This runs on the player's phone, once per level opened, so it is bounded rather than
        /// exact — and an undecided candidate is <em>refused</em>. That errs towards throwing a
        /// good board away rather than shipping an easy one, which is the right direction for a
        /// bar whose failure is silent. In practice it is never close: an excess budget of zero
        /// prunes every step that does not shorten the walk ahead of it, so a candidate is
        /// usually decided inside a few thousand positions.
        /// </para>
        /// </summary>
        public const int BarBudget = 120_000;

        /// <summary>How many boards to try before settling for the best one seen.</summary>
        public const int Attempts = 240;

        /// <summary>
        /// How many times one walk may be re-grown looking for ends far enough apart.
        ///
        /// Small on purpose: a walk that has failed this eight times is being grown through
        /// ground that has no long route left in it, and the next attempt's different carve is a
        /// better use of the time than a ninth try at this one.
        /// </summary>
        public const int WalkTries = 8;

        /// <summary>
        /// How many times one walk may be re-grown on a grove that has hedges on it.
        ///
        /// <para>
        /// <b>A fenced walk has two bars to clear where an open one has a single bar</b> — its
        /// ends must be far enough apart <em>and</em>, for the first <see cref="MinBitten"/> of
        /// them, on opposite sides of the fence. <see cref="WalkTries"/> was measured against one
        /// of those, and against both it is the difference between a shape dealing roughly one
        /// usable board in eight hundred and one in a hundred: the odds of the pair of bars are
        /// multiplied, so the retry that beats them has to be a good deal longer.
        /// </para>
        /// <para>
        /// <b>A second number rather than a bigger one, and that is not tidiness.</b> A walk that
        /// clears its bar on the ninth try is a walk the eight-try rule threw away, so raising
        /// <see cref="WalkTries"/> itself would re-deal every grove in the Weftwood and the
        /// Nightloom — two chapters pinned board-for-board by <c>WeaveLadderTests</c> and by
        /// <c>Tools/verify/weave.py</c>, neither of which has hedges anywhere in it. Reading a
        /// different number when there is a fence leaves both of them dealt from exactly the
        /// sequence they were authored from, for the reason <c>Attempt</c> rolls no hedge at all
        /// when a level asks for none.
        /// </para>
        /// </summary>
        public const int FencedWalkTries = 40;

        /// <summary>
        /// How far past its fair share of the free ground one walk may run, as an exact
        /// fraction — thirteen tenths.
        ///
        /// <para>
        /// At exactly its share every channel comes out the same length, which fills the grove and
        /// reads as four identical problems; the slack is what lets one channel be a long snake
        /// and another a short hop. Measured, 1.3 fills the grove on 88% of attempts against 75%
        /// at 1.0, so the variety is free.
        /// </para>
        /// <para>
        /// <b>A fraction rather than a <c>float</c>, and that is a correctness fix rather than a
        /// tidy-up.</b> This was <c>(int)(free / (float)walksLeft * 1.3f)</c>, and 1.3 is not
        /// representable in binary: the nearest float is 1.2999999523162842, so a walk with
        /// thirty cells free and three walks to go computes 12.99999952… — which truncates to
        /// <b>13</b> if the multiply is kept in single precision and to <b>12</b> if it is
        /// promoted to double. Both are legal for a C# compiler, and the runtimes disagree:
        /// Unity's Mono answers 13 and .NET 8 answers 12, on the same source and the same seed.
        /// </para>
        /// <para>
        /// <b>A one-cell budget difference is not a rounding error here — it is a different
        /// board.</b> The budget decides where the first walk stops, so every later walk starts
        /// from different ground and the whole grove is re-dealt. That is the one thing this
        /// generator must never do: a level's board is proved solvable and measured for
        /// difficulty at authoring time, on a desktop, and then generated again from the same
        /// seed on the player's phone under a third code generator. A grove that is not the same
        /// board everywhere is a grove whose proof does not apply to the copy anybody plays.
        /// Found by <c>WeaveLadderTests</c> failing offline and passing in the Editor, which is
        /// exactly the divergence the shared name-fold vectors were written to catch in
        /// invariant 19e — the same lesson, in arithmetic rather than in Unicode.
        /// </para>
        /// <para>
        /// The rule this leaves: <b>nothing that decides a cell may be a float.</b> Integers all
        /// the way down are identical on every runtime this game will ever run on.
        /// </para>
        /// </summary>
        public const int SlackNumerator = 13, SlackDenominator = 10;

        /// <summary>
        /// The colours pairs are dealt, in order.
        ///
        /// <para>
        /// <b>Every mix the board's own light makes, except white.</b> Not invented hues — the
        /// three channels and their three two-channel blends are what <see cref="Energy"/>
        /// already means, so a Lightweave grove is lit by exactly the same palette a glade is
        /// and nothing here is a colour the player has not already been taught.
        /// </para>
        /// <para>
        /// <b>White is deliberately left out, and it is the one exclusion that matters.</b>
        /// <c>Energy.All</c> is <see cref="Pal.Radiance"/>, a near-white cream — and a woken
        /// critter is tinted white, because that is how this mode says "awake". A seventh pair
        /// wearing it would be a critter whose sleeping colour is the colour of being awake, so
        /// the one thing the board most has to say at a glance would be the one thing it could
        /// not say. Six is therefore the ceiling, and it is a fact about the colour language
        /// rather than a number somebody picked.
        /// </para>
        /// <para>
        /// Four was the old ceiling and it capped the mode's difficulty rather than its palette:
        /// pairs are the only thing that makes channels contend for ground, so a ladder of ten
        /// groves had nothing but size to climb, and size on a phone is finger accuracy rather
        /// than thinking.
        /// </para>
        /// </summary>
        public static readonly int[] Palette =
        {
            Energy.R, Energy.G, Energy.B,
            Energy.R | Energy.G, Energy.G | Energy.B, Energy.R | Energy.B
        };

        /// <summary>
        /// The most beads a grove may be given, however many a level asks for.
        ///
        /// One per pair. A pair with two beads is a tour rather than a route, which is a
        /// different and much fussier kind of thinking — and on a phone it is mostly an exercise
        /// in remembering which circle you have already been through. The board says everything
        /// it has to say with one apiece.
        /// </summary>
        public static int MostBeads(int pairs) => pairs;

        /// <summary>
        /// The most hedges a grove may be grown, however many a level asks for.
        ///
        /// <para>
        /// A sixth of the grove's span, so it scales with the board instead of being a number that
        /// stops meaning anything at 8x10 - the same shape as <see cref="MinReach"/> and for the
        /// same reason. Every hedge takes a way out of the grove without taking any ground, so
        /// enough of them turn a field into a corridor with no decisions left in it: the routes
        /// stop being chosen and start being the only ones there are, which is the opposite of
        /// what this mechanic is for.
        /// </para>
        /// <para>
        /// Never more than three. Past that the acceptance bars stop being satisfiable in any
        /// sensible number of attempts - a carve has to fill a grove it can no longer cross freely
        /// - and the generator falls back to a board that meets no bar at all, which is worse than
        /// the plainer grove it was reaching past.
        /// </para>
        /// </summary>
        public static int MostHedges(int width, int height)
        {
            int scaled = (width + height) / 6;
            return scaled > 3 ? 3 : scaled;
        }

        /// <summary>
        /// The fewest cells one hedge may run for.
        ///
        /// <para>
        /// Two, because a single closed edge is stepped round for two cells and almost never
        /// changes a floor - <see cref="WeaveLayout.HedgesBite"/> would simply refuse the board,
        /// so a one-edge hedge is wasted work rather than a bad board. The most is one short of
        /// the whole span it grows along: a hedge that reached the far side would cut the grove in
        /// two, and leaving exactly one way past is the doorway this mechanic exists to make.
        /// </para>
        /// </summary>
        public const int MinHedge = 2;

        /// <summary>How many placements one hedge may be offered before the attempt is abandoned.</summary>
        public const int HedgeTries = 40;

        /// <summary>
        /// How many pairs the fence has to send a longer way, for the hedges to be doing the work
        /// this mechanic exists for — <see cref="WeaveLayout.PairsBitten"/>.
        ///
        /// <para>
        /// <b>The bar this joins was satisfiable by one pair, and that is what shipped.</b>
        /// <see cref="WeaveLayout.HedgesBite"/> asks whether the barriers lengthen anybody's
        /// floor, which is a sum over the grove, so one channel detouring two cells clears it for
        /// a board of six. Measured on the Wildhedge as authored: eight of its ten groves reached
        /// exactly one pair, three barriers apiece, five channels out of six drawn as though the
        /// fence were not there. Every check in the mode passed and the chapter came back from
        /// play as "it is like they are not there", which is precisely what it was.
        /// </para>
        /// <para>
        /// <b>One more pair per barrier, because a barrier is worth what its doorway is worth.</b>
        /// A hedge does not remove ground, it removes a way — so what it offers the board is a
        /// gap, and a gap is only a decision when more than one channel wants it. One pair sent
        /// round is a longer line and nothing else; two pairs at one gap is the mode's only
        /// question, asked by something the player can see before committing to anything
        /// (<see cref="MinReach"/>'s argument, for the ground rather than for the endpoints).
        /// Growing a second barrier that reaches nobody new buys the board nothing, so each one
        /// is made to pay for itself.
        /// </para>
        /// <para>
        /// Never more than half the pairs. Past that the carve has to thread most of the grove
        /// through the same gaps, the acceptance rate collapses, and what the generator falls back
        /// on is a board meeting no bar at all — <see cref="MostHedges"/>'s failure exactly, and
        /// worse than the plainer grove it was reaching past.
        /// </para>
        /// </summary>
        public static int MinBitten(int pairs, int hedges)
        {
            if (hedges < 1) return 0;
            int wanted = hedges + 1;
            int half = pairs / 2;
            return wanted > half ? half : wanted;
        }

        public static WeaveLayout Build(int width, int height, int pairs, uint seed, int beads = 0,
                                        int hedges = 0)
        {
            if (pairs < 1) pairs = 1;
            if (pairs > Palette.Length) pairs = Palette.Length;

            if (beads < 0) beads = 0;
            if (beads > MostBeads(pairs)) beads = MostBeads(pairs);

            if (hedges < 0) hedges = 0;
            if (hedges > MostHedges(width, height)) hedges = MostHedges(width, height);

            var rng = new Roller(seed);
            WeaveLayout best = null;

            int bestRank = int.MinValue;

            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                var candidate = Attempt(width, height, pairs, beads, hedges, rng);
                if (candidate == null) continue;

                // The cheap half first. A carve that did not reach every cell is refused before
                // the search runs, because the search is the expensive half and a slack carve is
                // the common way an attempt is poor.
                //
                // A grove asked for hedges must also have grown ones that *do* something, and do
                // it to more than one channel -- see MinBitten, which is the half that was missing
                // and is why a chapter of fenced groves played like open ones. Both halves are
                // cheap integer comparisons and both come before the search, for the same reason:
                // a board carrying decoration is refused without paying to find out how contested
                // it is. See WeaveLayout.HedgesBite.
                bool full = candidate.IsComplete;
                bool fenced = hedges == 0
                           || (candidate.HedgesBite
                               && candidate.PairsBitten >= MinBitten(pairs, hedges));
                bool contested = false;

                if (full && fenced)
                {
                    bool taut = WeaveSolver.AnyTautSolution(candidate, out bool decided, BarBudget);

                    // Undecided is refused rather than believed either way — see BarBudget.
                    contested = decided && !taut;
                    if (contested) return candidate;
                }

                // Ranked as integers rather than compared as coverage fractions. Every candidate
                // of one Build is the same grove size, so cell counts order identically to the
                // fractions — and a float comparison deciding which board ships is exactly the
                // hazard SlackNumerator documents, one line further on.
                int rank = (full ? 1 << 20 : 0) + (fenced ? 1 << 19 : 0)
                         + (contested ? 1 << 18 : 0) + candidate.SolutionLength;
                if (best == null || rank > bestRank) { best = candidate; bestRank = rank; }
            }

            // Nothing met both bars, which the shipped shapes do not do. The best board seen still
            // has a solution — it was carved the same way — so it is a slacker puzzle rather than
            // a broken one, and that is the right way to fail: a level that is a little easy beats
            // a level that cannot be finished. Validate Content says so out loud either way.
            return best ?? Fallback(width, height, pairs);
        }

        static WeaveLayout Attempt(int width, int height, int pairs, int beads, int hedges,
                                   Roller rng)
        {
            int count = width * height;
            var taken = new bool[count];
            var walks = new int[pairs][];
            var made = new WeavePair[pairs];

            var grown = new List<List<int>>();

            // The hedges go up *before* anything is carved, and that ordering is the whole reason
            // this mechanic cost the mode no new proof. Every walk below is grown over the ways
            // that are still open, so the arrangement the generator draws respects every barrier
            // by construction — exactly as it respects the no-crossing rule — and a hedged board
            // is solvable for the same reason an open one is rather than for a new one. Placing
            // them afterwards would mean carving a solution and then walling parts of it off, and
            // the board's own proof would have to be re-checked and could fail.
            //
            // Nothing is rolled when a level asks for none, so every grove authored before hedges
            // existed is dealt from exactly the sequence it was dealt from then. That is not
            // tidiness: this generator is pinned board-for-board by WeaveLadderTests and by
            // Tools/verify/weave.py, and consuming one roll here would have re-dealt two shipped
            // chapters.
            var fence = hedges > 0
                      ? Fence(width, height, hedges, rng)
                      : System.Array.Empty<WeaveHedge>();

            if (fence == null) return null;

            var walls = new WeaveHedges(width, height, fence);

            for (int p = 0; p < pairs; p++)
            {
                // The last walk is given the run of whatever is left, because there is nobody
                // after it to starve and any cell it declines is a hole in the grove.
                int left = pairs - p;
                int budget = left > 1 ? Share(taken, left) : count;

                // Re-walked rather than discarded, and that is what makes the reach bar
                // affordable at all. A walk whose two ends came out close together is one walk's
                // bad luck; throwing the attempt away for it discards every other pair's work
                // too, so the chance of dealing a board becomes the bar's odds raised to the
                // number of pairs — measured, a reach of five went from ordinary to one seed in
                // five at six pairs, purely from that. Retrying the one walk multiplies the odds
                // instead of compounding them.
                //
                // The same retry is what puts pairs *across* the fence, and it has to be asked
                // for rather than hoped for. Where a walk's two ends land is the only thing that
                // decides whether the hedges lengthen that pair's floor, and left to chance they
                // essentially never lengthen more than one: swept over twelve thousand seeds of
                // the shipped shape, every single grove carrying one hedge bit exactly one pair
                // and not one bit two. So the first MinBitten walks are re-grown until their ends
                // are separated by the fence, which is the same multiply-the-odds trick the reach
                // bar already relies on — and it is what turns a barrier from a longer line for
                // somebody into a gap several channels want.
                bool wantBitten = p < MinBitten(pairs, hedges);
                int allowed = hedges > 0 ? FencedWalkTries : WalkTries;

                List<int> walk = null;
                for (int tries = 0; tries < allowed; tries++)
                {
                    walk = Walk(width, height, taken, walls, rng, budget);
                    if (walk == null || walk.Count < MinPathLength) continue;

                    int from = walk[0], to = walk[walk.Count - 1];
                    if (!Reaches(from, to, width, height)) continue;

                    // Gap is the open-grid distance and WeaveHedges.Span is the one over the ways
                    // that are actually left, so the two differ exactly when this pair cannot go
                    // the way it would have gone on bare ground. That is WeaveLayout.PairsBitten's
                    // own test, asked one pair at a time before the pair exists.
                    if (wantBitten && walls.Span(from, to) <= Gap(from, to, width)) continue;

                    break;
                }

                if (walk == null || walk.Count < MinPathLength) return null;

                foreach (int cell in walk) taken[cell] = true;
                grown.Add(walk);
            }

            // A second pass over the leftovers, and it is what turns a nearly-full grove into a
            // full one. Budgeted walks still strand small islands of free ground that no walk
            // happened to enter, and every island is another way past somebody — so the paths are
            // grown out through them until nothing is left. Without it a complete grove is a
            // coincidence; with it, it is the ordinary outcome.
            bool moved = true;
            while (moved)
            {
                moved = false;
                foreach (var walk in grown)
                    if (Extend(walk, width, height, taken, walls, rng)) moved = true;
            }

            for (int p = 0; p < pairs; p++)
            {
                var walk = grown[p];
                int head = walk[0], tail = walk[walk.Count - 1];

                // Ends too close together make a pair joinable by a reflex, whatever route the
                // generator took to get between them - a free pair on a board that is meant to
                // be a set of decisions. See MinReach.
                if (Gap(head, tail, width) + 1 < MinReach(width, height)) return null;

                walks[p] = walk.ToArray();
                made[p] = new WeavePair(head, tail, Palette[p]);
            }

            return new WeaveLayout(width, height, made, walks,
                                   Thread(walls, made, walks, beads, rng), fence);
        }

        /// <summary>
        /// Grows the grove's hedges: runs of closed edges, each anchored at one side of the grove
        /// and reaching inward.
        ///
        /// <para>
        /// <b>Anchored rather than free-floating, and that is what makes a hedge do any work.</b>
        /// On an open grid there are a great many shortest routes between two cells, so a barrier
        /// dropped in the middle is walked round for nothing — the player takes one of the other
        /// routes and never notices. A run that starts at the edge of the grove shuts every one of
        /// them that crosses it above the run's tip, which is what turns a field into two rooms
        /// with a doorway between them. A doorway is the sharpest form of the only question this
        /// mode asks — who yields — and unlike everything else that forces the issue, the player
        /// can see it before they commit to anything.
        /// </para>
        /// <para>
        /// <b>Nothing may be sealed off, ever</b>, and it is checked as each run goes up rather
        /// than at the end. A pocket cut out of the grove is not a harder board: the carve cannot
        /// fill it, and a pair dealt inside one could not be joined at any price — a run that
        /// cannot be finished and will not end, which is the one state invariant 20g says this
        /// game must never produce. Two runs growing in from opposite sides of one boundary is the
        /// way it happens, and neither of them is wrong on its own.
        /// </para>
        /// <para>
        /// Null when the grove could not carry the hedges it was asked for, which abandons the
        /// attempt. Silently growing fewer would be a rung whose difficulty is one hedge lighter
        /// than the ladder says, with nothing anywhere to mention it — the same refusal
        /// <c>WeaveMode.TryRead</c> makes of a bead count it cannot honour.
        /// </para>
        /// </summary>
        static WeaveHedge[] Fence(int width, int height, int count, Roller rng)
        {
            var grown = new List<WeaveHedge>();

            for (int i = 0; i < count; i++)
            {
                bool placed = false;

                for (int tries = 0; tries < HedgeTries && !placed; tries++)
                {
                    var run = Hedgerow(width, height, rng);

                    var candidate = new List<WeaveHedge>(grown) { run };
                    var walls = new WeaveHedges(width, height, candidate.ToArray());

                    // Two runs closing the same way is not an error, but it is a hedge that is
                    // half a hedge — the fence would be shorter than its length says and the
                    // rung a notch easier than the ladder claims. Cheaper to notice by counting
                    // than by comparing runs pairwise, and it catches a crossing as well as an
                    // overlap.
                    if (walls.Edges != Edges(candidate)) continue;

                    // Tested on the whole fence rather than on the run, because disconnection is
                    // a property of the set: the run that seals a pocket is perfectly harmless
                    // until the one growing to meet it goes up.
                    if (!walls.AllReachable) continue;

                    grown.Add(run);
                    placed = true;
                }

                if (!placed) return null;
            }

            return grown.ToArray();
        }

        /// <summary>How many ways a fence would close if none of its runs met.</summary>
        static int Edges(List<WeaveHedge> fence)
        {
            int n = 0;
            for (int i = 0; i < fence.Count; i++) n += fence[i].Length;
            return n;
        }

        /// <summary>
        /// One run of closed edges: a side of the grove, a boundary to grow along, and how far in
        /// to reach.
        ///
        /// <para>
        /// The two orientations are the same idea turned ninety degrees. An <em>upright</em> hedge
        /// stands between two columns and is climbed down from the top of the grove or up from the
        /// bottom; a <em>flat</em> one lies between two rows and is walked in from the left or the
        /// right. Its length is at least <see cref="MinHedge"/> and at most one short of the span
        /// it grows along, so there is always a way past its inner tip.
        /// </para>
        /// </summary>
        static WeaveHedge Hedgerow(int width, int height, Roller rng)
        {
            bool upright = rng.Next(2) == 0;
            bool fromStart = rng.Next(2) == 0;

            int along = upright ? height : width;
            int across = upright ? width : height;

            int boundary = rng.Next(across - 1);
            int longest = along - 1;
            int length = MinHedge + rng.Next(longest - MinHedge + 1);

            // Anchored at one end of the grove or the other, so the run always reaches a border.
            // A run stored canonically starts at its low-index end whichever side it grew from,
            // which is why the far anchor is expressed as a start rather than as a direction --
            // one spelling per hedge, for the reason WeaveHedge documents.
            int start = fromStart ? 0 : along - length;

            return upright
                 ? new WeaveHedge(start * width + boundary, true, length)
                 : new WeaveHedge(boundary * width + start, false, length);
        }

        /// <summary>
        /// Drops beads along the carved routes, one pair at a time.
        ///
        /// <para>
        /// <b>On the carved route, so the board's own proof still holds.</b> A bead is a cell one
        /// channel must be threaded through; putting it anywhere else would mean the arrangement
        /// the generator just drew no longer satisfies the board it just built, and solvability
        /// here is a property of construction rather than something checked afterwards.
        /// </para>
        /// <para>
        /// <b>Off the direct corridor, or it is decoration.</b> A cell <c>c</c> lies on some
        /// shortest route between a pair's ends exactly when <c>d(heart,c) + d(c,critter)</c>
        /// equals <c>d(heart,critter)</c> — so a bead placed on one of those asks for nothing at
        /// all: the player draws the line they were going to draw and threads it on the way past.
        /// Only cells strictly outside that corridor are candidates, which means every bead
        /// placed here provably lifts its own pair's floor. That is invariant 5d's rule for this
        /// mode: a mechanic that rejects no arrangement is decoration, and this one is countable
        /// before it is placed.
        /// </para>
        /// <para>
        /// <b>The corridor is measured over the ways that are open, not in straight lines</b>, or
        /// the test stops meaning what it says the moment a hedge goes up. A cell on the far side
        /// of a barrier is a long way off a pair's real shortest route while sitting squarely on
        /// its Manhattan one, and a bead placed there by the old reading would be a second detour
        /// nobody counted — the reading and the floor it is checked against have to be the same
        /// distance. On an open grove the two are the same integer, so nothing already shipped
        /// moved.
        /// </para>
        /// <para>
        /// Spread across different pairs before any pair gets a second, because two beads on one
        /// channel and none on four others is a board with one interesting corner. Beyond that the
        /// choice is the roller's, so a level's beads are as much a property of its seed as its
        /// endpoints are.
        /// </para>
        /// </summary>
        static WeaveBead[] Thread(WeaveHedges walls, WeavePair[] pairs, int[][] walks, int beads,
                                  Roller rng)
        {
            if (beads <= 0) return System.Array.Empty<WeaveBead>();

            var placed = new List<WeaveBead>(beads);
            var used = new HashSet<int>();
            var room = new List<int>();

            for (int i = 0; i < beads; i++)
            {
                int pair = i % pairs.Length;
                var walk = walks[pair];
                if (walk.Length < 3) continue;

                int heart = pairs[pair].Heart, critter = pairs[pair].Critter;
                int direct = walls.Span(heart, critter);

                room.Clear();
                for (int step = 1; step < walk.Length - 1; step++)
                {
                    int cell = walk[step];
                    if (used.Contains(cell)) continue;

                    if (walls.Span(heart, cell) + walls.Span(cell, critter) > direct)
                        room.Add(cell);
                }

                if (room.Count == 0) continue;

                int chosen = room[rng.Next(room.Count)];
                used.Add(chosen);
                placed.Add(new WeaveBead(chosen, pair));
            }

            return placed.ToArray();
        }

        static int Gap(int a, int b, int width)
        {
            int ax = a % width, ay = a / width, bx = b % width, by = b / width;
            int dx = ax > bx ? ax - bx : bx - ax;
            int dy = ay > by ? ay - by : by - ay;
            return dx + dy;
        }

        /// <summary>
        /// How long a walk may run when this many are still to be grown.
        ///
        /// Exact integer arithmetic, deliberately — see <see cref="SlackNumerator"/> for the
        /// board this used to deal differently on different runtimes.
        /// </summary>
        static int Share(bool[] taken, int walksLeft)
        {
            int free = 0;
            for (int i = 0; i < taken.Length; i++) if (!taken[i]) free++;

            int budget = free * SlackNumerator / (walksLeft * SlackDenominator);
            return budget < MinPathLength ? MinPathLength : budget;
        }

        /// <summary>
        /// One self-avoiding walk through whatever is still free, up to <paramref name="budget"/>
        /// cells.
        ///
        /// <para>
        /// At each step it prefers the neighbour with the <em>fewest</em> free neighbours of its
        /// own. That is Warnsdorff's rule, and it is what makes the walks long and winding rather
        /// than short and straight: hugging the edges of the free space leaves the open middle
        /// available instead of cutting it in half and stranding one side. A plain random walk on
        /// this grove averages about six cells; this one averages nearer fifteen, which is the
        /// difference between a board with no congestion and a board with a puzzle in it.
        /// </para>
        /// </summary>
        static List<int> Walk(int width, int height, bool[] taken, WeaveHedges walls, Roller rng,
                              int budget)
        {
            var free = new List<int>();
            for (int i = 0; i < taken.Length; i++) if (!taken[i]) free.Add(i);
            if (free.Count == 0) return null;

            int at = free[rng.Next(free.Count)];

            var walk = new List<int> { at };
            var used = new bool[taken.Length];
            used[at] = true;

            while (walk.Count < budget)
            {
                int best = -1, bestOnward = int.MaxValue;
                int ties = 0;

                foreach (int next in Neighbours(at, width, height, walls))
                {
                    if (taken[next] || used[next]) continue;

                    int onward = 0;
                    foreach (int beyond in Neighbours(next, width, height, walls))
                        if (!taken[beyond] && !used[beyond]) onward++;

                    if (onward < bestOnward)
                    {
                        best = next;
                        bestOnward = onward;
                        ties = 1;
                    }
                    else if (onward == bestOnward && rng.Next(++ties) == 0)
                    {
                        best = next;
                    }
                }

                if (best < 0) break;

                used[best] = true;
                walk.Add(best);
                at = best;
            }

            return walk;
        }

        /// <summary>
        /// Whether a pair standing on these two cells is far enough apart to be a decision.
        ///
        /// Measured as a straight line even on a hedged grove, deliberately. A barrier can only
        /// ever push two cells further apart, so a pair clearing this bar in straight lines clears
        /// it over the open ways as well — the straight-line reading is the stricter of the two,
        /// and it is also the one the player takes with their eyes when they look at the board.
        /// </summary>
        static bool Reaches(int a, int b, int width, int height)
            => Gap(a, b, width) + 1 >= MinReach(width, height);

        /// <summary>
        /// Grows a walk outward from either end through ground nobody took, one cell at a time.
        ///
        /// Both ends, because a walk that ran into a corner has nowhere to go forward and plenty
        /// of room behind it. Returns whether anything was taken, so the caller can keep going
        /// until the leftovers are gone.
        /// </summary>
        static bool Extend(List<int> walk, int width, int height, bool[] taken, WeaveHedges walls,
                           Roller rng)
        {
            bool grew = false;

            for (int end = 0; end < 2; end++)
            {
                int from = end == 0 ? walk[0] : walk[walk.Count - 1];
                int other = end == 0 ? walk[walk.Count - 1] : walk[0];

                int best = -1, bestOnward = int.MaxValue, ties = 0;
                foreach (int next in Neighbours(from, width, height, walls))
                {
                    if (taken[next]) continue;

                    // Refused here rather than by throwing the board away afterwards. Warnsdorff
                    // hugs the edge of the free space, so a walk being grown from both ends
                    // routinely curls its head round beside its own tail — and a rejection at the
                    // end of the attempt discards every other pair's work with it, which is what
                    // sent this generator to its straight-rows fallback on more than half of all
                    // seeds. Declining the one step costs the board nothing.
                    //
                    // The step is only refused for closing the gap, never for the gap being
                    // small: a walk already inside the bar has to be able to climb back out of
                    // it, and a guard that declined every step would instead leave the leftover
                    // ground it was in the middle of eating.
                    if (!Reaches(next, other, width, height)
                        && Gap(next, other, width) <= Gap(from, other, width)) continue;

                    int onward = 0;
                    foreach (int beyond in Neighbours(next, width, height, walls))
                        if (!taken[beyond]) onward++;

                    if (onward < bestOnward) { best = next; bestOnward = onward; ties = 1; }
                    else if (onward == bestOnward && rng.Next(++ties) == 0) best = next;
                }

                if (best < 0) continue;

                taken[best] = true;
                if (end == 0) walk.Insert(0, best); else walk.Add(best);
                grew = true;
            }

            return grew;
        }

        /// <summary>
        /// The cells a walk may step to from here: inside the grove, with nothing grown between.
        ///
        /// <b>Every carve reads this and only this</b>, which is what makes the arrangement the
        /// generator draws respect the hedges without a single check anywhere else. Warnsdorff
        /// counts onward neighbours through it too, so a walk approaching a barrier sees the dead
        /// end coming and hugs it instead of running into it — which is why a hedged grove still
        /// fills.
        /// </summary>
        static IEnumerable<int> Neighbours(int cell, int width, int height, WeaveHedges walls)
        {
            int x = cell % width, y = cell / width;

            for (int i = 0; i < WeaveLayout.Steps.Length; i++)
            {
                int nx = x + WeaveLayout.Steps[i].dx, ny = y + WeaveLayout.Steps[i].dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                int next = ny * width + nx;
                if (walls.Open(cell, next)) yield return next;
            }
        }

        /// <summary>
        /// A board of straight rows, used only if hundreds of attempts somehow produced nothing.
        ///
        /// It is dull and it is solvable, which is the correct order of those two properties for
        /// a last resort — a player meeting an easy grove has a worse minute, and a player
        /// meeting an impossible one stops trusting the game.
        /// </summary>
        static WeaveLayout Fallback(int width, int height, int pairs)
        {
            var made = new WeavePair[pairs];
            var walks = new int[pairs][];

            for (int p = 0; p < pairs; p++)
            {
                // A grove with fewer rows than pairs has no row left to give, and leaving the
                // entry null is a NullReferenceException in whichever tool reads the solution
                // first — which on this path is Validate Content, in the Editor, with no board
                // on screen to explain it. An empty route is a board the validator reports as
                // unsolvable, out loud, which is the failure this should have.
                if (p >= height)
                {
                    walks[p] = new int[0];
                    made[p] = new WeavePair(0, 0, Palette[p]);
                    continue;
                }

                var row = new int[width];
                for (int x = 0; x < width; x++) row[x] = p * width + x;

                walks[p] = row;
                made[p] = new WeavePair(row[0], row[width - 1], Palette[p]);
            }

            // Deliberately beadless. This board is already the failure case, and a bead on it
            // would be one more thing that has to be satisfiable on a board nothing has checked.
            return new WeaveLayout(width, height, made, walks);
        }

        /// <summary>xorshift32, all 32-bit, so a seed produces the same grove anywhere.</summary>
        sealed class Roller
        {
            uint _state;

            public Roller(uint seed) => _state = seed == 0 ? 2463534242u : seed;

            public int Next(int bound)
            {
                if (bound <= 0) return 0;

                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return (int)(_state % (uint)bound);
            }
        }
    }
}
