using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// What a Hollowmarch road is really worth: what is standing on it, how near the gate it is,
    /// and — the half that matters — what the <em>shortest answers</em> actually do.
    ///
    /// <para>
    /// <b>It is a type of its own because it is asked twice.</b> <c>MarchValidator</c> asks it to
    /// decide whether a road may ship and <c>ProtoLadderTests</c> asks it of every road that
    /// already has, so a second copy of the arithmetic would be a second thing to keep in step
    /// with <c>Tools/verify/march.py</c>, which is the third copy and the one that can call
    /// neither (invariant 9a). The fixture pins these numbers for the shipped chapter, so this
    /// class <em>is</em> the C# side of that contract. <c>BudObjectReading</c> is the same shape
    /// for the same reason.
    /// </para>
    /// <para>
    /// <b>Why the interesting readings are taken over shortest solutions and not over the
    /// opening move.</b> An opening-move reading of "does this board chain" collapses exactly
    /// when a board is good: a line whose very first shot sets off a four-wave cascade is a line
    /// that is <em>over</em> in three shots, so tuning against it selects for short boards and
    /// quietly rejects the long ones. What an author wants to know is whether the mode's payoff
    /// is on the road to the answer. That is Budburst's <c>fired</c> and the whorl's
    /// <c>kindled</c> (invariants 20m, 26h), and it is measured over <b>every</b> shortest
    /// solution rather than over the first one the search reaches, because <c>ways</c> is rarely
    /// one and the first winning line is arbitrary among several.
    /// </para>
    /// </summary>
    public readonly struct MarchReading
    {
        /// <summary>Pods on the road as authored.</summary>
        public readonly int Pods;

        /// <summary>Caged critters, which is most of what the board is counting down.</summary>
        public readonly int Cages;

        /// <summary>Haulers: one plate, and a divider the colours cannot run through.</summary>
        public readonly int Haulers;

        /// <summary>Wardens: two plates, so two blasts or one lance.</summary>
        public readonly int Wardens;

        /// <summary>Distinct colours in the magazine.</summary>
        public readonly int Colours;

        /// <summary>How many slots the road has altogether.</summary>
        public readonly int Track;

        /// <summary>Shots before the front of the line reaches the gate and pods start going through.</summary>
        public readonly int Runway;

        /// <summary>
        /// Shots before a <em>goal</em> reaches the gate and the line jams.
        ///
        /// The number the pressure is tuned with, and <see cref="Runway"/> is not: under it
        /// nothing is lost at all and the march is only a picture, over it the front of the line
        /// starts going through the gate and every pod that does is match material the player no
        /// longer has. Neither is a fail state — see <c>MarchBoard.Stranded</c>.
        /// </summary>
        public readonly int Menace;

        /// <summary>The most waves any single <em>opening</em> shot sets off. Reported, never gated.</summary>
        public readonly int Chain;

        /// <summary>The most pods any single opening shot destroys.</summary>
        public readonly int Biggest;

        /// <summary>The most waves any shot on any <em>shortest solution</em> sets off.</summary>
        public readonly int Chained;

        /// <summary>Sparks forged along a shortest solution.</summary>
        public readonly int Forged;

        /// <summary>Sparks spent along a shortest solution.</summary>
        public readonly int Lanced;

        MarchReading(int pods, int cages, int haulers, int wardens, int colours, int track,
                     int runway, int menace, int chain, int biggest, int chained, int forged,
                     int lanced)
        {
            Pods = pods;
            Cages = cages;
            Haulers = haulers;
            Wardens = wardens;
            Colours = colours;
            Track = track;
            Runway = runway;
            Menace = menace;
            Chain = chain;
            Biggest = biggest;
            Chained = chained;
            Forged = forged;
            Lanced = lanced;
        }

        public static MarchReading Of(MarchLayout layout)
        {
            var line = layout.Line;

            int cages = 0, haulers = 0, wardens = 0;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (MarchLayout.IsCaged(c)) cages++;
                else if (c == MarchLayout.Hauler) haulers++;
                else if (c == MarchLayout.Warden) wardens++;
            }

            int colours = 0;
            for (int i = 0; i < MarchLayout.Colours.Length; i++)
                if (layout.Cores.IndexOf(MarchLayout.Colours[i]) >= 0) colours++;

            var board = MarchBoard.Build(layout);

            int menace = board.Head + line.Length;
            for (int at = 0; at < line.Length; at++)
            {
                if (!MarchLayout.IsGoal(line[at])) continue;
                menace = board.Head + at;
                break;
            }

            int chain = 0, biggest = 0;
            var moves = new List<MarchMove>(8);
            board.Moves(moves);

            for (int i = 0; i < moves.Count; i++)
            {
                var log = board.Fork().Fire(moves[i]);
                if (log == null) continue;

                if (log.Waves > chain) chain = log.Waves;
                if (log.Took > biggest) biggest = log.Took;
            }

            var deep = Deepen(layout);

            return new MarchReading(line.Length, cages, haulers, wardens, colours, layout.Track,
                                    board.Head, menace, chain, biggest,
                                    deep.Item1, deep.Item2, deep.Item3);
        }

        /// <summary>
        /// Walks every shortest solution and reports the most waves any of them sets off,
        /// whether any of them forges a Spark, and whether any of them spends one.
        ///
        /// <para>
        /// Carried along the frontier rather than searched again: a state reached at one depth by
        /// two routes keeps the better of the two, so what comes back at the winning layer is a
        /// fact about the <em>set</em> of shortest solutions and costs one triple of integers per
        /// state. It is the same breadth-first walk <see cref="ProtoSearch.Solve"/> already makes,
        /// so it can never be dearer than the search that has already run.
        /// </para>
        /// </summary>
        static System.Tuple<int, int, int> Deepen(MarchLayout layout)
        {
            var start = MarchBoard.Build(layout);
            var none = System.Tuple.Create(0, 0, 0);

            if (start.IsFinished) return none;

            var seen = new HashSet<ProtoKey> { ProtoKey.Of(new MarchFuture(start)) };
            var frontier = new List<MarchBoard> { start };
            var carried = new List<System.Tuple<int, int, int>> { none };

            var moves = new List<MarchMove>(8);
            int nodes = 0;

            for (int depth = 1; depth <= ProtoSearch.MaxDepth; depth++)
            {
                var next = new List<MarchBoard>(frontier.Count * 2);
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
                            log.Waves > mark.Item1 ? log.Waves : mark.Item1,
                            mark.Item2 + (log.Forged ? 1 : 0),
                            mark.Item3 + (log.Lanced ? 1 : 0));

                        if (forked.IsFinished)
                        {
                            won = won == null ? here : Better(won, here);
                            continue;
                        }

                        var key = ProtoKey.Of(new MarchFuture(forked));
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
