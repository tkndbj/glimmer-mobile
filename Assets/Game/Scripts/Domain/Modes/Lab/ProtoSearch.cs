using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// One arrangement of a board, and the moves available from it. The single thing a prototype
    /// mode hands to <see cref="ProtoSearch"/> so that none of them writes a solver.
    ///
    /// <para>
    /// <b>Every mode here is the same search wearing different rules, and that is a fact about
    /// the modes rather than a convenience.</b> The entry test was the one property this project
    /// makes a mode prove before a level is authored (invariant 20j): every legal input strictly
    /// moves something that only goes one way. A stone leaves the cairn and is never put back. So
    /// the state graph is a DAG with bounded depth, a breadth-first walk of it terminates by
    /// construction, and the first layer holding a finished board is par. Five prototypes were
    /// admitted on that test and four were later withdrawn for how they played; not one of them
    /// failed it, which is what a good entry test looks like.
    /// </para>
    /// <para>
    /// <b>Immutable, and <see cref="Play"/> returns a new position.</b> The search keeps whole
    /// layers alive at once, so a position that mutated in place would be a position the frontier
    /// behind it no longer describes. It is also what makes <see cref="Ways"/> possible at all:
    /// counting shortest solutions means visiting one state from several parents.
    /// </para>
    /// </summary>
    public abstract class ProtoPosition
    {
        /// <summary>Whether this arrangement has met the board's goal.</summary>
        public abstract bool Won { get; }

        /// <summary>
        /// How many moves are indexable from this position. Indices are stable for one position
        /// and mean nothing across two — a mode is free to number its moves however it likes, so
        /// long as <see cref="Play"/> agrees with this about how many there are.
        /// </summary>
        public abstract int MoveCount { get; }

        /// <summary>
        /// The position after playing move <paramref name="move"/>, or null when that move is not
        /// legal here.
        ///
        /// <para>
        /// <b>A move that changes nothing must answer null</b>, not a copy. That is the whole of
        /// what keeps the search finite: a no-op move is a self-edge, and a self-edge in a
        /// breadth-first walk is a level the frontier never leaves. It is also the rule the modes
        /// themselves are built on — a tap that would achieve nothing is refused out loud rather
        /// than swallowed, so the player is never charged for it either.
        /// </para>
        /// </summary>
        public abstract ProtoPosition Play(int move);

        /// <summary>
        /// Writes a canonical form of this arrangement, for the visited set.
        ///
        /// <para>
        /// <b>It must cover everything a rule reads and nothing else.</b> Too little and the
        /// search calls two different boards the same board, which under-reports par — the
        /// direction that hands out stars nobody earned. Too much (a move counter, an animation
        /// hint) and states that really are identical never merge, which is a search that runs
        /// out of budget on a board a player finishes in four taps.
        /// </para>
        /// </summary>
        public abstract void Write(List<byte> key);

        /// <summary>
        /// What a player who is not thinking would notice this move is worth, right now.
        ///
        /// <para>
        /// Used only by <see cref="ProtoSearch.Careless"/>, which is the reading that asks
        /// whether a board can be finished by taking the biggest thing on offer every time. Zero
        /// for every move — the default — makes that reading meaningless rather than wrong, which
        /// is the honest answer for a mode where nothing on the board looks bigger than anything
        /// else.
        /// </para>
        /// </summary>
        public virtual int Gain(int move) => 0;
    }

    /// <summary>What a search found: the shortest answer, how many there are, and what it cost.</summary>
    public readonly struct ProtoAnswer
    {
        /// <summary>Moves in the shortest solution, or nought when none was proved.</summary>
        public readonly int Par;

        /// <summary>
        /// How many distinct shortest solutions the board has, counted as move <em>sequences</em>.
        ///
        /// <para>
        /// The difficulty reading every mode in this game is authored against, and it reads the
        /// same way here as it does everywhere else: one means the board has exactly one answer
        /// and has to be solved rather than played, and a large number means the arms decide it
        /// rather than the player (invariant 5d). Which end warns is the mode's business — a mode
        /// commissioned to be easy warns at the bottom (invariant 20k).
        /// </para>
        /// <para>
        /// Capped at <see cref="MaxWays"/> rather than counted exactly. The number is read as a
        /// band, the count can run away on a wide board, and an author who needs to know whether
        /// it is 900 or 9,000 is asking the wrong question.
        /// </para>
        /// </summary>
        public readonly int Ways;

        /// <summary>Positions expanded. Against <see cref="ProtoSearch.NodeBudget"/>.</summary>
        public readonly int Nodes;

        /// <summary>
        /// Whether the search finished rather than running out of budget. A false answer with a
        /// par of nought is "cannot be solved"; a false answer is never "solvable in this many".
        /// </summary>
        public readonly bool Proved;

        public ProtoAnswer(int par, int ways, int nodes, bool proved)
        {
            Par = par;
            Ways = ways;
            Nodes = nodes;
            Proved = proved;
        }

        public bool Solvable => Par > 0;
    }

    /// <summary>
    /// The one solver every prototype mode shares: breadth-first over arrangements, counting
    /// shortest answers as it goes.
    ///
    /// <para>
    /// <b>Breadth-first rather than iterative deepening, and only because these modes are
    /// monotone.</b> Groovekeeper deepened iteratively because its board <em>grew</em> — two
    /// orderings of the same tiles were two boards, so the frontier grew like permutations and
    /// keeping a layer alive was not affordable. Nothing here adds anything to a board, so two
    /// orderings that remove the same things reach the same state and merge, and a layer stays
    /// small. That merging is also what makes <see cref="ProtoAnswer.Ways"/> cheap: a state's
    /// path count is the sum of its parents'.
    /// </para>
    /// <para>
    /// <b>The node budget is a promise to the phone, not to the build machine.</b> A level's par
    /// is resolved lazily on the device that opens it (invariant 26d), so this is a quarter of a
    /// second of nothing happening on the way into a level. The validators refuse a board that
    /// costs more than a fraction of it, so the budget is a floor under a mistake rather than the
    /// bar content is authored against.
    /// </para>
    /// </summary>
    public static class ProtoSearch
    {
        /// <summary>
        /// Positions this will expand before giving up. Matches <c>KeeperSolver</c>'s, because it
        /// is the same promise about the same moment — the beat between tapping a node and the
        /// board arriving.
        /// </summary>
        public const int NodeBudget = 90_000;

        /// <summary>
        /// The deepest answer worth looking for. A board needing more moves than this is not a
        /// board somebody authored, it is a board somebody mistyped: every mode here deals a
        /// budget of par plus a handful, so par thirty would ask for forty moves of a mode whose
        /// levels are three to six.
        /// </summary>
        public const int MaxDepth = 24;

        /// <summary>The most shortest-answers reported. See <see cref="ProtoAnswer.Ways"/>.</summary>
        public const int MaxWays = 9_999;

        /// <summary>
        /// The fewest moves that finish this board, how many ways there are of doing it, and what
        /// the walk cost.
        ///
        /// <para>
        /// A board that is already finished answers par nought and <see cref="ProtoAnswer.Proved"/>
        /// true — which every validator here reads as a refusal, because a level nobody has to
        /// play is a node on a map that opens onto a celebration.
        /// </para>
        /// </summary>
        public static ProtoAnswer Solve(ProtoPosition start)
        {
            if (start == null) return new ProtoAnswer(0, 0, 0, true);
            if (start.Won) return new ProtoAnswer(0, 0, 0, true);

            var seen = new Dictionary<ProtoKey, int>(4096);
            var frontier = new List<ProtoPosition>(256) { start };
            var counts = new List<int>(256) { 1 };

            seen[ProtoKey.Of(start)] = 0;

            int nodes = 0;
            var scratch = new List<byte>(96);

            for (int depth = 1; depth <= MaxDepth; depth++)
            {
                var nextPositions = new List<ProtoPosition>(frontier.Count * 2);
                var nextCounts = new List<int>(frontier.Count * 2);
                var index = new Dictionary<ProtoKey, int>(frontier.Count * 2);

                long won = 0;

                for (int i = 0; i < frontier.Count; i++)
                {
                    var from = frontier[i];
                    int ways = counts[i];

                    if (++nodes > NodeBudget) return new ProtoAnswer(0, 0, nodes, false);

                    int moves = from.MoveCount;
                    for (int m = 0; m < moves; m++)
                    {
                        var to = from.Play(m);
                        if (to == null) continue;

                        // A state reached at an earlier depth is not a shortest route to it, and
                        // one reached earlier *in this layer* is the same layer - so the second
                        // arrival adds its parent's ways rather than starting a new entry.
                        var key = ProtoKey.Of(to, scratch);

                        if (to.Won)
                        {
                            won = Cap(won + ways);
                            continue;
                        }

                        if (seen.ContainsKey(key)) continue;

                        if (index.TryGetValue(key, out int at))
                        {
                            nextCounts[at] = (int)Cap(nextCounts[at] + ways);
                            continue;
                        }

                        index[key] = nextPositions.Count;
                        nextPositions.Add(to);
                        nextCounts.Add(ways);
                    }
                }

                if (won > 0) return new ProtoAnswer(depth, (int)Cap(won), nodes, true);

                if (nextPositions.Count == 0) return new ProtoAnswer(0, 0, nodes, true);

                foreach (var pair in index) seen[pair.Key] = depth;

                frontier = nextPositions;
                counts = nextCounts;
            }

            return new ProtoAnswer(0, 0, nodes, false);
        }

        static long Cap(long n) => n > MaxWays ? MaxWays : n;

        /// <summary>
        /// How a board goes for somebody taking the biggest thing on offer every time, and never
        /// looking further than the move in front of them.
        ///
        /// <para>
        /// <b>Which end of this is good news depends on what the mode was commissioned to be</b>
        /// (invariant 20k). A mode meant to be a puzzle wants careless play to run out; a mode
        /// meant to be chill wants it to finish, because a board a relaxed player cannot finish is
        /// asking for more than the mode promised. Either way it is a number rather than an
        /// argument, which is the point.
        /// </para>
        /// <para>
        /// Ties go to the lowest move index, which makes the reading deterministic without
        /// claiming the tie-break is what a person would do. A reading that varied run to run
        /// could not be pinned by a test, and an unpinnable reading is one nobody trusts enough
        /// to author against.
        /// </para>
        /// </summary>
        /// <returns>Moves spent, or nought when it never finished inside the budget.</returns>
        public static int Careless(ProtoPosition start, int budget)
        {
            if (start == null || budget <= 0) return 0;

            var at = start;
            for (int spent = 0; spent < budget; spent++)
            {
                if (at.Won) return spent;

                int best = -1, bestGain = int.MinValue;
                ProtoPosition bestTo = null;

                int moves = at.MoveCount;
                for (int m = 0; m < moves; m++)
                {
                    var to = at.Play(m);
                    if (to == null) continue;

                    int gain = at.Gain(m);
                    if (gain <= bestGain) continue;

                    best = m;
                    bestGain = gain;
                    bestTo = to;
                }

                if (best < 0) return 0;
                at = bestTo;
            }

            return at.Won ? budget : 0;
        }
    }

    /// <summary>
    /// A board's canonical bytes, hashed once.
    ///
    /// <para>
    /// A struct over a byte array rather than a string, because the alternative was measured and
    /// it is not close: a forty-cell board reached as a string allocates a forty-character string
    /// per expansion, and the search expands tens of thousands. The hash is computed at
    /// construction, so the dictionary never walks the array twice for the same key.
    /// </para>
    /// </summary>
    public readonly struct ProtoKey : System.IEquatable<ProtoKey>
    {
        readonly byte[] _bytes;
        readonly int _hash;

        ProtoKey(byte[] bytes, int hash)
        {
            _bytes = bytes;
            _hash = hash;
        }

        public static ProtoKey Of(ProtoPosition position) => Of(position, new List<byte>(96));

        public static ProtoKey Of(ProtoPosition position, List<byte> scratch)
        {
            scratch.Clear();
            position.Write(scratch);

            var bytes = scratch.ToArray();

            // FNV-1a, 32-bit, for the reason the chest generator uses it: it is cheap, it spreads
            // well over short runs of small integers, and it is written down in two other places
            // in this project already.
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= 16777619;
                }
                return new ProtoKey(bytes, (int)hash);
            }
        }

        public bool Equals(ProtoKey other)
        {
            if (_hash != other._hash) return false;

            var a = _bytes;
            var b = other._bytes;
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;

            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public override bool Equals(object obj) => obj is ProtoKey other && Equals(other);
        public override int GetHashCode() => _hash;
    }
}
