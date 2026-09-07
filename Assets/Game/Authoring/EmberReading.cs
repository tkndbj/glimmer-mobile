using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// What an Emberforge wall is really worth: what is packed into it, how long it lasts, and
    /// — the half that matters — what the <em>shortest answers</em> actually do.
    ///
    /// <para>
    /// <b>It is a type of its own because it is asked twice.</b> <c>EmberValidator</c> asks it to
    /// decide whether a wall may ship and <c>ProtoLadderTests</c> asks it of every wall that
    /// already has, so a second copy of the arithmetic would be a second thing to keep in step
    /// with <c>Tools/verify/ember.py</c>, which is the third copy and the one that can call
    /// neither (invariant 9a). <c>MarchReading</c> and <c>BudObjectReading</c> are the same shape
    /// for the same reason.
    /// </para>
    /// <para>
    /// <b>Why the interesting readings are taken over shortest solutions and not over the opening
    /// move.</b> An opening-move reading of "does this wall chain" collapses exactly when a board
    /// is good: a wall whose very first swap sets off a four-beat cascade is a wall that is
    /// <em>over</em> in three moves, so tuning against it selects for short boards and quietly
    /// rejects the long ones. What an author wants to know is whether the mode's payoff is on the
    /// road to the answer. That is Budburst's <c>fired</c> and the whorl's <c>kindled</c>
    /// (invariants 20m, 26h), measured over <b>every</b> shortest solution rather than over the
    /// first one the search reaches, because <c>ways</c> is rarely one and the first winning line
    /// is arbitrary among several.
    /// </para>
    /// </summary>
    public readonly struct EmberReading
    {
        /// <summary>Plain shards packed into the wall as authored. The whole of its material.</summary>
        public readonly int Shards;

        /// <summary>Embers the level <em>deals</em>, which is the one number 20m is about.</summary>
        public readonly int Dealt;

        /// <summary>Caged critters.</summary>
        public readonly int Cages;

        /// <summary>Wardens: two plates each, and each of them stops a beam dead.</summary>
        public readonly int Wardens;

        /// <summary>Rivetted stone: permanent, and it stops a beam.</summary>
        public readonly int Stone;

        /// <summary>Frost: it stops nothing and a beam melts it, dropping the wall above it.</summary>
        public readonly int Frost;

        /// <summary>Distinct shard colours actually present.</summary>
        public readonly int Colours;

        /// <summary>
        /// Colours with fewer than <see cref="EmberLayout.FuseAt"/> shards on the wall.
        ///
        /// Each one is material that can never be fused into anything, so it is dead weight that
        /// still breaks up other people's runs — one or two is texture, four is a wall of confetti.
        /// </summary>
        public readonly int Lonely;

        /// <summary>The most beats any single opening move sets off. What a first touch can do.</summary>
        public readonly int Chain;

        /// <summary>The most pieces any single opening move takes off the wall.</summary>
        public readonly int Biggest;

        /// <summary>
        /// Moves a player who always takes the biggest thing going can make before the wall has
        /// nothing left to give — whether or not they win on the way.
        ///
        /// <para>
        /// <b>The reading this mode needed and no other one did.</b> Everywhere else a run ends
        /// when the allowance runs out; here the wall itself is finite, so a board can be dead
        /// while the meter still says four moves left — which reads as the game deciding on the
        /// player's behalf. Read against the allowance: a wall that a spendthrift cannot keep
        /// alive that long is a wall whose real fail state is not the one the readout is showing.
        /// </para>
        /// </summary>
        public readonly int Life;

        /// <summary>The most beats any <em>shortest</em> answer sets off. The mode's payoff, measured.</summary>
        public readonly int Chained;

        /// <summary>Embers made along a shortest answer. What the player builds rather than is given.</summary>
        public readonly int Forged;

        /// <summary>Stars set off along a shortest answer.</summary>
        public readonly int Starred;

        public EmberReading(int shards, int dealt, int cages, int wardens, int stone, int frost,
                            int colours, int lonely, int chain, int biggest, int life,
                            int chained, int forged, int starred)
        {
            Shards = shards;
            Dealt = dealt;
            Cages = cages;
            Wardens = wardens;
            Stone = stone;
            Frost = frost;
            Colours = colours;
            Lonely = lonely;
            Chain = chain;
            Biggest = biggest;
            Life = life;
            Chained = chained;
            Forged = forged;
            Starred = starred;
        }

        /// <summary>Goals the wall opens with: every cage, and every warden.</summary>
        public int Goals => Cages + Wardens;

        public static EmberReading Of(EmberLayout layout) => Of(layout, 0);

        /// <summary>
        /// Everything above, for one wall.
        /// </summary>
        /// <param name="budget">
        /// The allowance the run is dealt, so <see cref="Life"/> can be walked exactly as far as
        /// it needs to be and no further. Nought asks for a default depth.
        /// </param>
        public static EmberReading Of(EmberLayout layout, int budget)
        {
            var grid = layout.Grid;

            int shards = 0, dealt = 0, cages = 0, wardens = 0, stone = 0, frost = 0;
            var perHue = new int[EmberLayout.Shards.Length];

            for (int i = 0; i < grid.Count; i++)
            {
                char c = grid.At(i);

                int hue = EmberLayout.Shards.IndexOf(c);
                if (hue >= 0) { shards++; perHue[hue]++; continue; }

                if (EmberLayout.IsEmber(c)) dealt++;
                else if (c == EmberLayout.Cage) cages++;
                else if (c == EmberLayout.Warden) wardens++;
                else if (c == EmberLayout.Stone) stone++;
                else if (c == EmberLayout.Frost) frost++;
            }

            int colours = 0, lonely = 0;
            for (int i = 0; i < perHue.Length; i++)
            {
                if (perHue[i] == 0) continue;
                colours++;
                if (perHue[i] < EmberLayout.FuseAt) lonely++;
            }

            var board = EmberBoard.Build(layout);

            int chain = 0, biggest = 0;
            var moves = new List<EmberMove>(32);
            board.Moves(moves);

            for (int i = 0; i < moves.Count; i++)
            {
                var log = board.Fork().Fire(moves[i]);
                if (log == null) continue;

                if (log.Beats > chain) chain = log.Beats;
                if (log.Took > biggest) biggest = log.Took;
            }

            var deep = Deepen(layout);

            return new EmberReading(shards, dealt, cages, wardens, stone, frost, colours, lonely,
                                    chain, biggest, Alive(layout, budget),
                                    deep.Item1, deep.Item2, deep.Item3);
        }

        /// <summary>How many moves the most extravagant possible player gets out of this wall.</summary>
        const int MostMoves = 40;

        /// <summary>
        /// Plays the biggest thing going, every time, until the wall has nothing left — and
        /// counts the moves.
        ///
        /// <para>
        /// The same greedy walk <see cref="ProtoSearch.Careless"/> makes and a different question:
        /// that one asks whether thoughtlessness <em>wins</em>, this one asks how long the wall
        /// survives it. Greedy is the right player to ask, because in this mode the biggest move
        /// is always the one that spends the most material — so this is the shortest life the
        /// wall has under any play that is not deliberately self-destructive.
        /// </para>
        /// </summary>
        static int Alive(EmberLayout layout, int budget)
        {
            // Above the allowance rather than at it, or the reading could never say the
            // wall outlasts the meter - which is the one thing it exists to answer.
            int most = budget > 0 && budget + 4 < MostMoves ? budget + 4 : MostMoves;

            var at = EmberBoard.Build(layout);
            var moves = new List<EmberMove>(32);

            for (int spent = 0; spent < most; spent++)
            {
                at.Moves(moves);
                if (moves.Count == 0) return spent;

                int bestGain = -1;
                EmberBoard bestTo = null;

                for (int m = 0; m < moves.Count; m++)
                {
                    var forked = at.Fork();
                    var log = forked.Fire(moves[m]);
                    if (log == null) continue;

                    int gain = log.Goals * 100 + log.Took;
                    if (gain <= bestGain) continue;

                    bestGain = gain;
                    bestTo = forked;
                }

                if (bestTo == null) return spent;
                at = bestTo;
            }

            return most;
        }

        /// <summary>
        /// Walks every shortest solution and reports the most beats any of them sets off, how
        /// many embers any of them makes, and how many stars any of them spends.
        ///
        /// <para>
        /// Carried along the frontier rather than searched again: a state reached at one depth by
        /// two routes keeps the better of the two, so what comes back at the winning layer is a
        /// fact about the <em>set</em> of shortest solutions and costs one triple of integers per
        /// state. It is the same breadth-first walk <see cref="ProtoSearch.Solve"/> already makes,
        /// so it can never be dearer than the search that has already run.
        /// </para>
        /// </summary>
        static System.Tuple<int, int, int> Deepen(EmberLayout layout)
        {
            var start = EmberBoard.Build(layout);
            var none = System.Tuple.Create(0, 0, 0);

            if (start.IsFinished) return none;

            var seen = new HashSet<ProtoKey> { ProtoKey.Of(new EmberFuture(start)) };
            var frontier = new List<EmberBoard> { start };
            var carried = new List<System.Tuple<int, int, int>> { none };

            var moves = new List<EmberMove>(32);
            int nodes = 0;

            for (int depth = 1; depth <= ProtoSearch.MaxDepth; depth++)
            {
                var next = new List<EmberBoard>(frontier.Count * 2);
                var marks = new List<System.Tuple<int, int, int>>(frontier.Count * 2);
                var index = new Dictionary<ProtoKey, int>(frontier.Count * 2);

                System.Tuple<int, int, int> won = null;

                for (int i = 0; i < frontier.Count; i++)
                {
                    var from = frontier[i];
                    var mark = carried[i];

                    if (++nodes > ProtoSearch.NodeBudget) return none;

                    from.Moves(moves);
                    var taken = moves.ToArray();

                    for (int m = 0; m < taken.Length; m++)
                    {
                        var forked = from.Fork();
                        var log = forked.Fire(taken[m]);
                        if (log == null) continue;

                        var here = System.Tuple.Create(
                            log.Beats > mark.Item1 ? log.Beats : mark.Item1,
                            mark.Item2 + log.Forged,
                            mark.Item3 + log.Stars);

                        if (forked.IsFinished)
                        {
                            won = won == null ? here : Better(won, here);
                            continue;
                        }

                        var key = ProtoKey.Of(new EmberFuture(forked));
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

                if (won != null) return won;
                if (next.Count == 0) return none;

                foreach (var pair in index) seen.Add(pair.Key);

                frontier = next;
                carried = marks;
            }

            return none;
        }

        static System.Tuple<int, int, int> Better(System.Tuple<int, int, int> a,
                                                  System.Tuple<int, int, int> b)
            => System.Tuple.Create(a.Item1 > b.Item1 ? a.Item1 : b.Item1,
                                   a.Item2 > b.Item2 ? a.Item2 : b.Item2,
                                   a.Item3 > b.Item3 ? a.Item3 : b.Item3);
    }
}
