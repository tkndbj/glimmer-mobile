using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// A flower worth tapping with the colour in hand: where it is, what it would become, and
    /// whether it is known to keep the grove winnable.
    /// </summary>
    public readonly struct BudSpot
    {
        /// <summary>The cell to point at, or -1 when there is nothing to point at.</summary>
        public readonly int Cell;

        /// <summary>What the colour in hand would turn that flower into.</summary>
        public readonly int Colour;

        /// <summary>
        /// Whether this tap is known to lie on a play that frees everybody inside the taps that
        /// are left.
        ///
        /// <b>False is not "bad", it is "unproved".</b> The search is node-bounded, so a grove
        /// that is expensive early answers with the biggest chain going instead — which is a
        /// perfectly good tap on a mode built to be generous, and is what somebody asking for a
        /// hint here is actually asking for. What it must never do is claim a proof it does not
        /// have: the mark is drawn the same either way, and only this flag could tell them apart.
        /// </summary>
        public readonly bool Proved;

        /// <summary>What tapping it comes to, so the mark can promise the right size of thing.</summary>
        public readonly int Waves, Burst, Freed;

        public BudSpot(int cell, int colour, bool proved, int waves, int burst, int freed)
        {
            Cell = cell;
            Colour = colour;
            Proved = proved;
            Waves = waves;
            Burst = burst;
            Freed = freed;
        }

        public static readonly BudSpot None = new BudSpot(-1, Energy.None, false, 0, 0, 0);

        public bool Any => Cell >= 0;
    }

    /// <summary>
    /// Where to tap next, for the hint key.
    ///
    /// <para>
    /// <b>It answers a different question from <see cref="BudSolver"/>, which is why it is a
    /// different class.</b> The solver asks how few taps finish a grove — content being graded,
    /// mirrored in Python and pinned by vectors (invariant 9a). This asks which of the taps
    /// available right now is worth spending a hint on: a client convenience with no authored
    /// number behind it, nothing stored, nothing adjudicated and no second copy anywhere.
    /// <c>Puzzle.NextHint</c> is the same shape one mode over, and costs the save file, the wire
    /// and the server exactly what that one does, which is nothing.
    /// </para>
    /// <para>
    /// <b>Correct first, spectacular second, and both halves matter.</b> The search finds every
    /// opening tap that still leads to a finish inside the taps that are left; among those it
    /// takes the one that goes off hardest, by the same ranking <see cref="BudSolver.Careless"/>
    /// uses. So a hint here can never quietly cost somebody the level, and it never points at the
    /// dull half of two equally good answers — which on a mode whose whole product is the cascade
    /// would be a resource spent to be handed the boring version.
    /// </para>
    /// <para>
    /// <b>It is bounded and degrades rather than stalling.</b> Cost goes as the flower count to
    /// the power of the taps left, exactly as par does, so a hint asked for on the first tap of a
    /// big grove can be dearer than proving the whole board. Past <see cref="NodeBudget"/> it
    /// stops proving and answers with the biggest chain going, flagged
    /// <see cref="BudSpot.Proved"/> false. A hint that took a second to arrive would be worse
    /// than a slightly worse hint.
    /// </para>
    /// </summary>
    public static class BudHint
    {
        /// <summary>
        /// What proving a hint may cost, in positions.
        ///
        /// Half of <see cref="BudSolver.NodeBudget"/>, deliberately: par is searched once while a
        /// level is opening and this is searched on a tap, with a thumb on the button and a grove
        /// already on screen. It is the cheaper question in practice too — every tap already
        /// spent has taken a level off the tree.
        /// </summary>
        public const int NodeBudget = 60_000;

        /// <summary>The best tap available to this run, or <see cref="BudSpot.None"/>.</summary>
        public static BudSpot Best(BudRun run)
        {
            if (run == null || run.Verdict.IsOver) return BudSpot.None;

            int left = run.Satchel.Bounded ? run.Satchel.Left : BudSolver.MaxTaps;
            return Best(run.Board, run.Deal, run.Spent, left);
        }

        /// <summary>
        /// The same question asked of a board directly, so it can be proved offline against a
        /// position rather than against a whole run.
        /// </summary>
        public static BudSpot Best(BudBoard board, BudDeal deal, int spent, int tapsLeft)
        {
            if (board == null || deal == null || tapsLeft <= 0) return BudSpot.None;
            if (board.IsFinished) return BudSpot.None;

            int hand = deal.At(spent);
            if (hand == Energy.None) return BudSpot.None;

            // The fallback is worked out first and unconditionally, because it is also what
            // decides whether there is anything to point at at all — and that answer must not
            // depend on how much of the search budget the position happened to eat.
            var greedy = BudSpot.None;
            var greedyChain = BudChainResult.Nothing;

            for (int i = 0; i < board.Count; i++)
            {
                if (!board.CanTap(i, hand)) continue;

                var chain = board.Preview(i, hand);
                if (greedy.Any && !Better(chain, greedyChain)) continue;

                greedyChain = chain;
                greedy = new BudSpot(i, board.Mixed(i, hand), false,
                                     chain.Waves, chain.Burst, chain.Freed);
            }

            if (!greedy.Any) return BudSpot.None;

            int ceiling = tapsLeft < BudSolver.MaxTaps ? tapsLeft : BudSolver.MaxTaps;
            var proved = new Search(board, deal).Run(spent, ceiling);

            return proved.Any ? proved : greedy;
        }

        /// <summary>
        /// Which of two chains is the one worth pointing at.
        ///
        /// The same order <see cref="BudSolver.Careless"/> ranks by, and the same order for a
        /// reason: "the biggest thing available" is one idea, and a second copy of it would let a
        /// hint recommend a tap the mode's own bar would not have taken.
        /// </summary>
        static bool Better(BudChainResult a, BudChainResult b)
        {
            if (a.Freed != b.Freed) return a.Freed > b.Freed;
            if (a.Cracked != b.Cracked) return a.Cracked > b.Cracked;
            if (a.Waves != b.Waves) return a.Waves > b.Waves;
            return a.Burst > b.Burst;
        }

        /// <summary>
        /// Iterative deepening over the taps that are left, one first move at a time.
        ///
        /// <para>
        /// <b>Every first move gets its own search rather than sharing one tree, and the reason
        /// is the dedup.</b> A single walk keys its visited set on the position, so a grove
        /// reachable by two different opening taps is explored for whichever came first and the
        /// other is pruned — which silently hides half the good answers, and often the one that
        /// goes off hardest. Per-move searches cost about the same in total (a tree of b^L is b
        /// subtrees of b^(L-1)) and cannot lose a move.
        /// </para>
        /// </summary>
        sealed class Search
        {
            readonly BudBoard _board;
            readonly BudDeal _deal;

            readonly BudGround[][] _groundAt;
            readonly int[][] _valueAt;

            readonly HashSet<string> _seen = new HashSet<string>();
            readonly char[] _key;

            int _nodes;
            bool _exhausted;

            public Search(BudBoard board, BudDeal deal)
            {
                _board = new BudBoard(board);
                _deal = deal;

                int depth = BudSolver.MaxTaps + 2;
                _groundAt = new BudGround[depth][];
                _valueAt = new int[depth][];

                for (int i = 0; i < depth; i++)
                {
                    _groundAt[i] = new BudGround[board.Count];
                    _valueAt[i] = new int[board.Count];
                }

                _key = new char[board.Count + 3];
            }

            public BudSpot Run(int spent, int ceiling)
            {
                var best = BudSpot.None;

                for (int limit = 1; limit <= ceiling; limit++)
                {
                    var chosen = BudChainResult.Nothing;
                    int hand = _deal.At(spent);

                    _board.Save(_groundAt[0], _valueAt[0]);

                    for (int i = 0; i < _board.Count; i++)
                    {
                        if (!_board.CanTap(i, hand)) continue;

                        int made = _board.Mixed(i, hand);
                        var chain = _board.Tap(i, hand, null);

                        _seen.Clear();
                        bool wins = Reaches(spent + 1, limit - 1, 1);
                        _board.Restore(_groundAt[0], _valueAt[0]);

                        if (wins && (!best.Any || Better(chain, chosen)))
                        {
                            chosen = chain;
                            best = new BudSpot(i, made, true,
                                               chain.Waves, chain.Burst, chain.Freed);
                        }

                        if (_exhausted) return best;
                    }

                    // The shortest limit that works is the one to answer from: a longer one
                    // would let a tap that wastes a whole turn tie with one that does not.
                    if (best.Any) return best;
                }

                return best;
            }

            /// <summary>Whether the grove as it stands finishes inside <paramref name="left"/> taps.</summary>
            bool Reaches(int spent, int left, int depth)
            {
                if (_exhausted) return false;
                if (++_nodes > NodeBudget) { _exhausted = true; return false; }

                if (_board.IsFinished) return true;
                if (left <= 0 || depth >= _groundAt.Length) return false;
                if (!Fresh(spent, left)) return false;

                _board.Save(_groundAt[depth], _valueAt[depth]);
                int hand = _deal.At(spent);

                for (int i = 0; i < _board.Count; i++)
                {
                    if (!_board.CanTap(i, hand)) continue;

                    _board.Tap(i, hand, null);
                    bool won = Reaches(spent + 1, left - 1, depth + 1);
                    _board.Restore(_groundAt[depth], _valueAt[depth]);

                    if (won) return true;
                    if (_exhausted) return false;
                }

                return false;
            }

            bool Fresh(int spent, int left)
            {
                _board.KeyInto(_key, out int length);

                // How much rope is left is part of the position, and so is the colour in hand:
                // the same grove with two taps to go is a different problem from the same grove
                // with five, so a position visited under a longer allowance must never prune the
                // shorter one.
                _key[length] = (char)('0' + (left & 15));
                _key[length + 1] = Energy.Letter(_deal.At(spent));

                return _seen.Add(new string(_key, 0, length + 2));
            }
        }
    }
}
