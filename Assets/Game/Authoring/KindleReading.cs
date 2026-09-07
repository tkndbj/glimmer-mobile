using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// What a Kindlewake hollow is really worth: what is scattered over it, how many strands it
    /// can ever hold, and — the half that matters — what the <em>shortest answers</em> actually do
    /// with them.
    ///
    /// <para>
    /// <b>It is a type of its own because it is asked twice.</b> <c>KindleValidator</c> asks it to
    /// decide whether a hollow may ship and <c>ProtoLadderTests</c> asks it of every hollow that
    /// already has, so a second copy of the arithmetic would be a second thing to keep in step with
    /// <c>Tools/verify/kindle.py</c>, which is the third copy and the one that can call neither
    /// (invariant 9a). <c>EmberReading</c>, <c>MarchReading</c> and <c>BudObjectReading</c> are the
    /// same shape for the same reason.
    /// </para>
    /// <para>
    /// <b>Why the interesting readings are taken over shortest solutions and not over the opening
    /// move.</b> An opening-move reading of "does this hollow cross" collapses exactly when a board
    /// is good: a hollow whose very first pair of strands already crosses on a critter is a hollow
    /// that is <em>over</em> in two moves, so tuning against it selects for short boards and
    /// quietly rejects the long ones. What an author wants to know is whether the mode's payoff is
    /// on the road to the answer. That is Budburst's <c>fired</c>, the whorl's <c>kindled</c> and
    /// Hollowmarch's <c>chained</c> (invariants 20m, 26h, 33f), measured over <b>every</b> shortest
    /// solution rather than over the first one the search reaches, because <c>ways</c> is rarely
    /// one and the first winning line is arbitrary among several.
    /// </para>
    /// </summary>
    public readonly struct KindleReading
    {
        /// <summary>Embers scattered over the hollow as authored. The whole of its material.</summary>
        public readonly int Embers;

        /// <summary>Sleeping critters: everything the level is asking for.</summary>
        public readonly int Critters;

        /// <summary>
        /// Critters wanting a blend, which are the only ones a single strand can never wake.
        ///
        /// The number this mode is really authored against: a hollow of nothing but pure critters
        /// is a covering exercise where a hollow with blends is a mode.
        /// </summary>
        public readonly int Blends;

        /// <summary>Standing stone: permanent, and the only thing that stops a strand.</summary>
        public readonly int Stone;

        /// <summary>Distinct ember channels actually present.</summary>
        public readonly int Channels;

        /// <summary>
        /// Channels with fewer than <see cref="KindleLayout.JoinAt"/> embers on the hollow.
        ///
        /// Each one is material that can never be joined into anything at all — it is not merely
        /// weak, it is inert, and a critter wanting that channel is a critter nothing can ever
        /// reach.
        /// </summary>
        public readonly int Lonely;

        /// <summary>
        /// Embers standing where no partner of their own colour shares a clear row or column.
        ///
        /// <para>
        /// A softer fault than <see cref="Lonely"/> and a commoner one: the colour has enough
        /// embers, and this particular one is walled off from all of them. One or two is texture —
        /// something for the eye that the hand cannot use — and a hollow full of them is a hollow
        /// that looks far richer than it plays (invariant 5d, asked of the material rather than of
        /// the answer).
        /// </para>
        /// </summary>
        public readonly int Idle;

        /// <summary>
        /// How deep any legal play can take this hollow, up to the allowance and no further:
        /// the deepest layer that still had a strand to draw.
        ///
        /// <para>
        /// <b>Emberforge's <c>Life</c>, and this mode needs it for the same reason and measures it
        /// a different way.</b> Everywhere else a run ends when the allowance runs out; here the
        /// material is finite, so a hollow can be dead while the meter still says three moves left
        /// — which reads as the game deciding on the player's behalf.
        /// </para>
        /// <para>
        /// <b>Why it is not the greedy walk Emberforge uses.</b> That walk stops when there is
        /// nothing left to play, and on a hollow there is nothing left to play the instant the
        /// last critter wakes — a strand is refused unless it helps somebody
        /// (<c>KindleBoard.Helps</c>), so a finished board has no moves by construction. A greedy
        /// reading would therefore report "how long until this is over" and would equal par on
        /// every board that greedy wins, which is a number that says nothing. What is wanted is
        /// the <em>longest</em> play, and it is exactly computable: every join spends two embers,
        /// so every route to a given arrangement is the same length, the state graph is layered by
        /// construction, and the deepest layer a breadth-first walk reaches <b>is</b> the longest
        /// play.
        /// </para>
        /// <para>
        /// <b>And it stops at the allowance, because that is the only question it is asked.</b>
        /// The reading is read against the budget — "can the meter ever be reached" — so walking
        /// past it buys nothing and costs everything: the whole graph of a hollow with twenty
        /// embers is ten layers deep and the walk is the dearest thing in this file by a wide
        /// margin. Capped, it visits about as much as the par search does. A walk that runs out
        /// of nodes under-reports too, which is the safe direction for a warning — it can cost a
        /// false alarm an author will look at and can never hide a real one.
        /// </para>
        /// </summary>
        public readonly int Life;

        /// <summary>
        /// The most crossings any <em>shortest</em> answer makes: cells where a strand met light
        /// already standing there in another channel.
        ///
        /// The mode's own arithmetic, measured. Nought means every strand on the road to the
        /// answer was drawn over dark ground, which is the mode with its subject taken out.
        /// </summary>
        public readonly int Crossed;

        /// <summary>
        /// The most critters any shortest answer wakes <em>with a blend</em> — a colour that took
        /// two strands to build.
        ///
        /// <para>
        /// <b>Stricter than <see cref="Crossed"/> and the one that condemns a board.</b> A crossing
        /// over bare ground is a tidier picture and decides nothing; a crossing the player had to
        /// arrange <em>on a particular cell</em>, because the critter standing there wants a colour
        /// no ember carries, is the thing they made (invariant 20m). It is the whorl's
        /// <c>kindled</c> asked here, and for the same reason: <c>fused</c> counted whorls that
        /// drew in a pair, and only <c>kindled</c> counted the ones whose pair was worth drawing.
        /// </para>
        /// </summary>
        public readonly int Blended;

        /// <summary>The most critters any single strand of a shortest answer wakes at once.</summary>
        public readonly int Best;

        public KindleReading(int embers, int critters, int blends, int stone, int channels,
                             int lonely, int idle, int life, int crossed, int blended, int best)
        {
            Embers = embers;
            Critters = critters;
            Blends = blends;
            Stone = stone;
            Channels = channels;
            Lonely = lonely;
            Idle = idle;
            Life = life;
            Crossed = crossed;
            Blended = blended;
            Best = best;
        }

        /// <summary>The most strands this hollow could ever hold, whatever the geometry allows.</summary>
        public int Strands => Embers / KindleLayout.JoinAt;

        public static KindleReading Of(KindleLayout layout) => Of(layout, 0);

        /// <summary>
        /// Everything above, for one hollow.
        /// </summary>
        /// <param name="budget">
        /// The allowance the run is dealt, so <see cref="Life"/> is walked exactly as far as it
        /// needs to be and no further. Nought asks for a default depth.
        /// </param>
        public static KindleReading Of(KindleLayout layout, int budget)
        {
            var grid = layout.Grid;

            int embers = 0, critters = 0, blends = 0, stone = 0;
            var perChannel = new int[KindleLayout.Embers.Length];

            for (int i = 0; i < grid.Count; i++)
            {
                char c = grid.At(i);

                int at = KindleLayout.Embers.IndexOf(c);
                if (at >= 0) { embers++; perChannel[at]++; continue; }

                if (KindleLayout.IsSleeper(c))
                {
                    critters++;
                    if (KindleLayout.Channels(KindleLayout.WantOf(c)) > 1) blends++;
                    continue;
                }

                if (c == KindleLayout.Stone) stone++;
            }

            int channels = 0, lonely = 0;
            for (int i = 0; i < perChannel.Length; i++)
            {
                if (perChannel[i] == 0) continue;
                channels++;
                if (perChannel[i] < KindleLayout.JoinAt) lonely++;
            }

            // One past the allowance, so the reading can say the hollow outlasts the meter
            // rather than only that it matches it. A hollow with no allowance is walked to a
            // modest default: there is nothing to compare against, so the number is a report.
            var deep = Walk(layout, budget > 0 ? budget + 1 : FreeDepth);

            return new KindleReading(embers, critters, blends, stone, channels, lonely,
                                     Idlers(layout), deep.Life,
                                     deep.Crossed, deep.Blended, deep.Best);
        }

        /// <summary>
        /// Embers that appear in no candidate pair at all, so no strand could ever start or end
        /// on them.
        ///
        /// Read off <see cref="KindleLayout.Pairs"/> rather than walked again, because that array
        /// <em>is</em> the answer to "which embers can see each other" and a second walk is a
        /// second chance to disagree with the board about what a clear line means.
        /// </summary>
        static int Idlers(KindleLayout layout)
        {
            var grid = layout.Grid;
            var used = new bool[grid.Count];

            var pairs = layout.Pairs;
            for (int i = 0; i < pairs.Length; i++) used[pairs[i]] = true;

            int idle = 0;
            for (int i = 0; i < grid.Count; i++)
                if (KindleLayout.IsEmber(grid.At(i)) && !used[i]) idle++;

            return idle;
        }


        /// <summary>What one walk of a hollow's whole state graph reports.</summary>
        readonly struct Trace
        {
            /// <summary>Crossings, over every shortest answer.</summary>
            public readonly int Crossed;

            /// <summary>Blend-woken critters, over every shortest answer.</summary>
            public readonly int Blended;

            /// <summary>The most critters any single strand of a shortest answer wakes.</summary>
            public readonly int Best;

            /// <summary>The deepest layer any legal play reaches, which is the longest play.</summary>
            public readonly int Life;

            public Trace(int crossed, int blended, int best, int life)
            {
                Crossed = crossed;
                Blended = blended;
                Best = best;
                Life = life;
            }
        }

        /// <summary>
        /// Positions this walk expands before giving up.
        ///
        /// Lower than <see cref="ProtoSearch.NodeBudget"/> on purpose, and the difference is the
        /// point: the search stops at the first winning layer, and this one carries on past it to
        /// find the longest play, so it visits strictly more of the graph. It is an authoring
        /// reading that never runs on a phone, and a truncated answer under-reports
        /// <see cref="Life"/> — a false warning rather than a missed one.
        /// </summary>
        const int WalkBudget = 60_000;

        /// <summary>
        /// How deep a hollow with no allowance is walked. The opening levels of every mode are
        /// authored unlosable (invariant 24), so there is no meter for <see cref="Life"/> to be
        /// read against and the number is a report rather than a check.
        /// </summary>
        const int FreeDepth = 12;

        /// <summary>
        /// Walks every arrangement this hollow can reach and reports what the shortest answers do
        /// with it, and how deep any play can go.
        ///
        /// <para>
        /// <b>One walk rather than two.</b> The shortest-answer readings are carried along the
        /// frontier — a state reached at one depth by two routes keeps the better of the two, so
        /// what comes back at the winning layer is a fact about the <em>set</em> of shortest
        /// solutions and costs one triple of integers per state. Reading the longest play off the
        /// same walk is then free: it is the deepest layer that still had a legal move in it.
        /// </para>
        /// <para>
        /// <b>The layers are exact, and that is a property of the mode rather than of this
        /// method.</b> Every join spends exactly two embers, so every route to a given arrangement
        /// has the same length and no state can appear at two depths. That is what makes
        /// breadth-first depth equal to path length here, what makes <c>ProtoAnswer.Ways</c>
        /// meaningful, and what lets one walk answer both questions without keeping two visited
        /// sets.
        /// </para>
        /// </summary>
        static Trace Walk(KindleLayout layout, int cap)
        {
            var start = KindleBoard.Build(layout);
            if (start.IsFinished) return new Trace(0, 0, 0, 0);

            var seen = new HashSet<ProtoKey> { ProtoKey.Of(new KindleFuture(start)) };
            var frontier = new List<KindleBoard> { start };
            var carried = new List<System.Tuple<int, int, int>> { System.Tuple.Create(0, 0, 0) };

            var moves = new List<KindleMove>(24);
            int nodes = 0, life = 0, wonAt = 0;

            System.Tuple<int, int, int> won = null;

            int deepest = cap < ProtoSearch.MaxDepth ? cap : ProtoSearch.MaxDepth;

            for (int depth = 1; depth <= deepest && frontier.Count > 0; depth++)
            {
                var next = new List<KindleBoard>(frontier.Count * 2);
                var marks = new List<System.Tuple<int, int, int>>(frontier.Count * 2);
                var index = new Dictionary<ProtoKey, int>(frontier.Count * 2);

                for (int i = 0; i < frontier.Count; i++)
                {
                    var from = frontier[i];
                    var mark = carried[i];

                    if (++nodes > WalkBudget)
                        return new Trace(Item1(won), Item2(won), Item3(won), life);

                    from.Joins(moves);
                    var taken = moves.ToArray();

                    for (int m = 0; m < taken.Length; m++)
                    {
                        var forked = from.Fork();
                        var log = forked.Draw(taken[m]);
                        if (log == null) continue;

                        // This layer held a legal move, so some play reaches this depth - whether
                        // or not the arrangement it reaches is a finished one.
                        if (depth > life) life = depth;

                        var here = System.Tuple.Create(
                            mark.Item1 + log.Crossings,
                            mark.Item2 + log.Blended,
                            log.Woke > mark.Item3 ? log.Woke : mark.Item3);

                        if (forked.IsFinished)
                        {
                            // Only the *shortest* answers are read. A state never appears at two
                            // depths, so the first layer that wins is the shortest one and every
                            // later win is a longer answer that must not touch these numbers.
                            if (wonAt == 0) { wonAt = depth; won = here; }
                            else if (wonAt == depth) won = Better(won, here);
                            continue;
                        }

                        var key = ProtoKey.Of(new KindleFuture(forked));
                        if (seen.Contains(key)) continue;

                        if (index.TryGetValue(key, out int at))
                        {
                            marks[at] = Better(marks[at], here);
                            continue;
                        }

                        index[key] = next.Count;
                        next.Add(forked);
                        marks.Add(here);
                    }
                }

                foreach (var pair in index) seen.Add(pair.Key);

                frontier = next;
                carried = marks;
            }

            return new Trace(Item1(won), Item2(won), Item3(won), life);
        }

        static int Item1(System.Tuple<int, int, int> t) => t == null ? 0 : t.Item1;
        static int Item2(System.Tuple<int, int, int> t) => t == null ? 0 : t.Item2;
        static int Item3(System.Tuple<int, int, int> t) => t == null ? 0 : t.Item3;

        static System.Tuple<int, int, int> Better(System.Tuple<int, int, int> a,
                                                  System.Tuple<int, int, int> b)
            => System.Tuple.Create(a.Item1 > b.Item1 ? a.Item1 : b.Item1,
                                   a.Item2 > b.Item2 ? a.Item2 : b.Item2,
                                   a.Item3 > b.Item3 ? a.Item3 : b.Item3);
    }
}
