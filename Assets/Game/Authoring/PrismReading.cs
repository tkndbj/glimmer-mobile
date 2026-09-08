using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// What a Prismvale board is really worth: what is scattered over it, how much of it is
    /// already lit before anybody has touched it, and — the half that matters — what the
    /// <em>shortest answers</em> actually do with the lanterns.
    ///
    /// <para>
    /// <b>It is a type of its own because it is asked twice.</b> <c>PrismValidator</c> asks it to
    /// decide whether a board may ship and <c>ProtoLadderTests</c> asks it of every board that
    /// already has, so a second copy of the arithmetic would be a second thing to keep in step
    /// with <c>Tools/verify/prism.py</c>, which is the third copy and the one that can call
    /// neither (invariant 9a). <c>EmberReading</c>, <c>MarchReading</c> and
    /// <c>BudObjectReading</c> are the same shape for the same reason.
    /// </para>
    /// <para>
    /// <b>Why the interesting readings are taken over shortest solutions and not over the opening
    /// move.</b> An opening-move reading of "does this board use both lanterns" collapses exactly
    /// when a board is good: one whose very first swap wakes two critters is a board that is
    /// <em>over</em> in two moves, so tuning against it selects for short boards and quietly
    /// rejects the long ones. That is Budburst's <c>fired</c>, the whorl's <c>kindled</c> and
    /// Hollowmarch's <c>chained</c> (invariants 20m, 26h, 33f), measured over <b>every</b>
    /// shortest solution rather than over the first one the search reaches, because <c>ways</c>
    /// is rarely one and the first winning line is arbitrary among several.
    /// </para>
    /// <para>
    /// <b>What is deliberately absent is a <c>life</c>.</b> Every other mode built on this shape
    /// has one, because its material runs out and the meter can therefore be counting down to an
    /// ending that will not be the one that happens. Nothing here is ever consumed: a gem is
    /// moved and never spent, so a board always has a legal move and the allowance is the only
    /// way to lose. A reading that could only ever answer "for ever" is a reading nobody should
    /// author against.
    /// </para>
    /// </summary>
    public readonly struct PrismReading
    {
        /// <summary>Gems on the board. Its whole material, and none of it is ever spent.</summary>
        public readonly int Gems;

        /// <summary>Lanterns: everywhere light can come from.</summary>
        public readonly int Lamps;

        /// <summary>Sleeping critters: everything the level is asking for.</summary>
        public readonly int Critters;

        /// <summary>Bare ground: the only thing that shapes a vein.</summary>
        public readonly int Bare;

        /// <summary>Distinct lantern colours actually standing on the board.</summary>
        public readonly int Hues;

        /// <summary>
        /// Gems already lit as the board is dealt.
        ///
        /// <b>Invariant 5g, counted.</b> A board dealt with most of its veins already running is
        /// a board that starts half done — it passes every other gate, because "how much of this
        /// is already finished" is a question nothing else asks. A little is good and is how the
        /// mode teaches itself without a word; a lot is a level somebody else played.
        /// </summary>
        public readonly int Dealt;

        /// <summary>
        /// How many distinct lantern colours wake a critter along some shortest solution.
        ///
        /// <b>Invariant 5d, asked of the thing this mode's colour rule is made of.</b> A board
        /// standing three lanterns whose answer only ever uses one is a board with two decorative
        /// lanterns on it: the colour decided nothing, and the player was routing rather than
        /// choosing.
        /// </summary>
        public readonly int Used;

        /// <summary>
        /// The most critters one swap wakes along a shortest solution.
        ///
        /// The payoff measured: two at once is a vein the player <em>arranged</em> to serve both,
        /// which is the only thing on this board that is better than the obvious move.
        /// </summary>
        public readonly int Paired;

        /// <summary>
        /// Lanterns with no gem at all beside them.
        ///
        /// A lantern is the brightest thing on the board, so one that could never start a vein
        /// reads as a route that is not there. Geometry rather than a proof: whether a gem of the
        /// right colour can be <em>brought</em> to it is the search's question, and the answer to
        /// that changes every move.
        /// </summary>
        public readonly int Idle;

        PrismReading(int gems, int lamps, int critters, int bare, int hues, int dealt,
                     int used, int paired, int idle)
        {
            Gems = gems;
            Lamps = lamps;
            Critters = critters;
            Bare = bare;
            Hues = hues;
            Dealt = dealt;
            Used = used;
            Paired = paired;
            Idle = idle;
        }

        /// <summary>How deep a board with no allowance is walked.</summary>
        public const int FreeDepth = 6;

        /// <summary>Positions the reading walk will expand before giving up.</summary>
        public const int DeepBudget = 60_000;

        /// <summary>The deepest the reading walk ever goes, whatever the allowance says.</summary>
        public const int DeepDepth = 10;

        public static PrismReading Of(PrismLayout layout, int budget = 0)
        {
            var grid = layout.Grid;

            int gems = 0, lamps = 0, critters = 0, bare = 0;
            int hues = 0;

            for (int i = 0; i < grid.Count; i++)
            {
                char c = grid.At(i);

                if (PrismLayout.IsGem(c)) gems++;
                else if (PrismLayout.IsLamp(c))
                {
                    lamps++;
                    hues |= 1 << PrismLayout.HueOf(c);
                }
                else if (c == PrismLayout.Asleep) critters++;
                else if (c == PrismLayout.Bare) bare++;
            }

            var board = PrismBoard.Build(layout);

            Walk(layout, budget > 0 ? budget + 1 : FreeDepth, out int used, out int paired);

            return new PrismReading(gems, lamps, critters, bare, Bits(hues), board.DealtLit,
                                    used, paired, Idlers(layout));
        }

        static int Bits(int mask)
        {
            int n = 0;
            while (mask != 0) { n += mask & 1; mask >>= 1; }
            return n;
        }

        /// <summary>Lanterns with no gem at all beside them. See <see cref="Idle"/>.</summary>
        static int Idlers(PrismLayout layout)
        {
            var around = new List<int>(4);
            int idle = 0;

            for (int cell = 0; cell < layout.Count; cell++)
            {
                if (!PrismLayout.IsLamp(layout.At(cell))) continue;

                bool any = false;
                layout.Around(cell, around);
                for (int i = 0; i < around.Count; i++)
                    if (PrismLayout.IsGem(layout.At(around[i]))) { any = true; break; }

                if (!any) idle++;
            }

            return idle;
        }

        /// <summary>
        /// What the <em>shortest</em> answers do: how many lantern colours they use, and the best
        /// single move in one.
        ///
        /// <para>
        /// One breadth-first walk carrying two marks along the frontier, read off the first layer
        /// that wins — which is the shortest one, because a state is entered at its shortest
        /// depth and never again. Mirrors <c>Tools/verify/prism.py</c>'s <c>walk</c> exactly, and
        /// it is deliberately a second walk rather than a hook inside <see cref="ProtoSearch"/>:
        /// the shared search is what every mode's par comes from, and threading a per-mode
        /// accumulator through it would put a mode's arithmetic inside the one thing none of them
        /// is allowed to change.
        /// </para>
        /// </summary>
        static void Walk(PrismLayout layout, int cap, out int used, out int paired)
        {
            used = 0;
            paired = 0;

            var start = PrismBoard.Build(layout);
            if (start.IsFinished) return;

            var seen = new HashSet<string> { start.Key() };
            var frontier = new List<PrismBoard> { start };

            // Two marks per frontier entry: which lantern colours have woken something on the way
            // here, and the most a single swap has woken.
            var hues = new List<int> { 0 };
            var best = new List<int> { 0 };

            var moves = new List<PrismMove>(64);

            int nodes = 0;
            int deepest = cap < DeepDepth ? cap : DeepDepth;

            int wonHues = 0, wonBest = 0;
            bool won = false;

            for (int depth = 1; depth <= deepest && frontier.Count > 0; depth++)
            {
                var next = new List<PrismBoard>(frontier.Count * 2);
                var nextHues = new List<int>(frontier.Count * 2);
                var nextBest = new List<int>(frontier.Count * 2);
                var index = new Dictionary<string, int>(frontier.Count * 2);

                for (int i = 0; i < frontier.Count; i++)
                {
                    if (++nodes > DeepBudget)
                    {
                        Report(won, wonHues, wonBest, out used, out paired);
                        return;
                    }

                    var at = frontier[i];
                    at.Moves(moves);

                    for (int m = 0; m < moves.Count; m++)
                    {
                        var forked = at.Fork();
                        var log = forked.Play(moves[m]);
                        if (log == null) continue;

                        int hereHues = hues[i];
                        for (int d = 0; d < log.Deeds.Count; d++)
                        {
                            var deed = log.Deeds[d];
                            if (deed.Deed == PrismDeed.Wake && deed.Hue >= 0)
                                hereHues |= 1 << deed.Hue;
                        }

                        int hereBest = log.Woke > best[i] ? log.Woke : best[i];

                        if (forked.IsFinished)
                        {
                            if (!won) { won = true; wonHues = hereHues; wonBest = hereBest; }
                            else
                            {
                                wonHues |= hereHues;
                                if (hereBest > wonBest) wonBest = hereBest;
                            }
                            continue;
                        }

                        // Once this layer has produced a win, nothing deeper can be a shortest
                        // answer, so the rest of the layer is read and nothing beyond it is kept.
                        if (won) continue;

                        string key = forked.Key();
                        if (seen.Contains(key)) continue;

                        if (index.TryGetValue(key, out int slot))
                        {
                            nextHues[slot] |= hereHues;
                            if (hereBest > nextBest[slot]) nextBest[slot] = hereBest;
                            continue;
                        }

                        index[key] = next.Count;
                        next.Add(forked);
                        nextHues.Add(hereHues);
                        nextBest.Add(hereBest);
                    }
                }

                if (won) break;

                foreach (var pair in index) seen.Add(pair.Key);

                frontier = next;
                hues = nextHues;
                best = nextBest;
            }

            Report(won, wonHues, wonBest, out used, out paired);
        }

        static void Report(bool won, int wonHues, int wonBest, out int used, out int paired)
        {
            used = won ? Bits(wonHues) : 0;
            paired = won ? wonBest : 0;
        }
    }
}
