using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>What a search of a grove came to. Par, and the readings an author needs.</summary>
    public readonly struct BudSurvey
    {
        public readonly bool Proved;

        /// <summary>The fewest moves that free every critter. 0 when nothing does.</summary>
        public readonly int Par;

        /// <summary>
        /// How many different shortest plays there are, capped at <see cref="BudSolver.MaxWays"/>.
        ///
        /// <b>Read the other way round in this mode.</b> Everywhere else a high count is a warning
        /// — a board almost anything finishes is deciding nothing (invariant 5d). Here the brief
        /// is a board almost anything finishes, so the reading that matters is the *low* end: one
        /// single shortest play means the grove is a puzzle, and a puzzle is what this mode is
        /// deliberately not.
        /// </summary>
        public readonly int Ways;

        /// <summary>How many positions it cost. What the player's device will pay (invariant 26d).</summary>
        public readonly int Nodes;

        /// <summary>
        /// Of the shortest plays, how many forged a special, and how many fired one.
        ///
        /// <b>Whorlwater's <c>kindled</c>, for the special.</b> Measured over every shortest
        /// solution rather than the first one the search reaches, because <c>Ways</c> is rarely
        /// one and an author tuning against the first line is tuning against a coin toss. A
        /// grove where no shortest play fires a special is a grove the specials are decoration
        /// on.
        /// </summary>
        public readonly int Forged, Fired;

        public BudSurvey(bool proved, int par, int ways, int nodes, int forged = 0, int fired = 0)
        {
            Proved = proved;
            Par = par;
            Ways = ways;
            Nodes = nodes;
            Forged = forged;
            Fired = fired;
        }

        public bool IsSolvable => Proved && Par > 0;
    }

    /// <summary>
    /// The fewest moves that free every critter, found by search.
    ///
    /// <para>
    /// <b>The goal is the cocoons and not the buds, and that choice is what makes this
    /// affordable.</b> "Clear every bud" was tried first: branching is the flower count, so a
    /// six-by-six grove cost ninety-five thousand positions and often could not be proved at
    /// all. Freeing the critters is a far smaller target reached by the same chains — measured on
    /// the same boards it costs a few hundred positions, and it is also the more forgiving goal,
    /// which is the point of the mode.
    /// </para>
    /// <para>
    /// Cost still goes as the move count to the power of par, so the cheap fix for an expensive
    /// grove is a shorter answer — a cocoon moved nearer the powder, never a bigger board. A
    /// graft adds every productive pair to the branching, which is why the second chapter stays
    /// at par 3 with everything else.
    /// </para>
    /// </summary>
    public static class BudSolver
    {
        public const int NodeBudget = 120_000;

        /// <summary>No shipped grove may need more moves than this to finish.</summary>
        public const int MaxTaps = 8;

        /// <summary>Counting shortest plays stops here; past it the reading says the same thing.</summary>
        public const int MaxWays = 2000;

        public static int Par(BudLayout layout) => Survey(layout).Par;

        public static BudSurvey Survey(BudLayout layout)
        {
            if (layout == null) return new BudSurvey(true, 0, 0, 0);
            return new Search(layout).Run();
        }

        /// <summary>
        /// How a player who never looks past this move gets on: always the one that frees the
        /// most, then runs the furthest.
        ///
        /// <b>Read the opposite way round here too.</b> On every other mode a careless player
        /// finishing is a warning that the board decides nothing. On this one it is the bar: the
        /// brief is chill, so a grove a careless player cannot finish is a grove that is
        /// asking too much.
        /// </summary>
        public static int Careless(BudLayout layout, int budget)
        {
            if (layout == null) return -1;

            var run = new BudRun(layout, budget);
            int ceiling = budget > 0 && budget < MaxTaps * 4 ? budget : MaxTaps * 4;
            var moves = new List<BudMove>(64);

            for (int tap = 0; tap < ceiling; tap++)
            {
                if (run.Board.IsFinished) return run.Spent;
                if (!run.Satchel.Any) return -1;

                BudRun.Moves(run.Board, run.Next, moves);

                var best = BudMove.None;
                var bestChain = BudChainResult.Nothing;

                for (int i = 0; i < moves.Count; i++)
                {
                    var chain = run.Preview(moves[i]);
                    if (best.Any && !Better(chain, bestChain)) continue;

                    best = moves[i];
                    bestChain = chain;
                }

                if (!best.Any) return -1;
                run.Play(best, null);
            }

            return run.Board.IsFinished ? run.Spent : -1;
        }

        /// <summary>
        /// The best opening move the dealt grove has in it, by chain length and then by size —
        /// the reading every fixture pins a board's opening on. The move order is
        /// <see cref="BudRun.Moves"/>' and the tie goes to the earlier move, which is part of the
        /// contract with the Python mirror.
        /// </summary>
        public static BudMove Opening(BudLayout layout, out BudChainResult best)
        {
            best = BudChainResult.Nothing;
            var chosen = BudMove.None;
            if (layout == null) return chosen;

            var board = new BudBoard(layout);
            var moves = new List<BudMove>(64);
            BudRun.Moves(board, layout.Deal.At(0), moves);

            for (int i = 0; i < moves.Count; i++)
            {
                var probe = new BudBoard(board);
                BudRun.Apply(probe, moves[i], layout.Deal.At(0), null, out var chain);

                if (chain.Waves > best.Waves
                    || (chain.Waves == best.Waves && chain.Burst > best.Burst))
                {
                    best = chain;
                    chosen = moves[i];
                }
            }

            return chosen;
        }

        static bool Better(BudChainResult a, BudChainResult b)
        {
            if (a.Freed != b.Freed) return a.Freed > b.Freed;
            if (a.Cracked != b.Cracked) return a.Cracked > b.Cracked;
            if (a.Waves != b.Waves) return a.Waves > b.Waves;
            return a.Burst > b.Burst;
        }

        sealed class Search
        {
            readonly BudLayout _layout;
            readonly BudBoard _board;

            readonly BudGround[][] _groundAt;
            readonly int[][] _valueAt;
            readonly BudSpecial[][] _specialAt;
            readonly int[] _packedAt;
            readonly List<BudMove>[] _movesAt;

            readonly HashSet<string> _seen = new HashSet<string>();
            readonly char[] _key;

            int _nodes, _limit, _ways, _forged, _fired;
            bool _budgetSpent;

            public Search(BudLayout layout)
            {
                _layout = layout;
                _board = new BudBoard(layout);

                int depth = MaxTaps + 2;
                _groundAt = new BudGround[depth][];
                _valueAt = new int[depth][];
                _specialAt = new BudSpecial[depth][];
                _movesAt = new List<BudMove>[depth];

                for (int i = 0; i < depth; i++)
                {
                    _groundAt[i] = new BudGround[layout.Count];
                    _valueAt[i] = new int[layout.Count];
                    _specialAt[i] = new BudSpecial[layout.Count];
                    _movesAt[i] = new List<BudMove>(64);
                }

                _packedAt = new int[depth];
                _key = new char[layout.Count + 6];
            }

            public BudSurvey Run()
            {
                if (_layout.Cocoons == 0) return new BudSurvey(true, 0, 0, 0);
                if (_board.IsFinished) return new BudSurvey(true, 0, 1, 0);

                // Certainly lost before a tap is spent, which is a *proof* rather than a search
                // that ran out — and the two have to be told apart, because one says the grove
                // is unwinnable and the other says nobody knows.
                if (!_board.AnyMove()) return new BudSurvey(true, 0, 0, 0);

                for (int limit = 1; limit <= MaxTaps; limit++)
                {
                    _limit = limit;
                    _ways = 0;
                    _forged = 0;
                    _fired = 0;
                    _seen.Clear();

                    Walk(0, 0);

                    if (_budgetSpent) return new BudSurvey(false, 0, 0, _nodes);
                    if (_ways > 0) return new BudSurvey(true, limit, _ways, _nodes, _forged, _fired);
                }

                return new BudSurvey(false, 0, 0, _nodes);
            }

            void Walk(int spent, int dealt)
            {
                if (_budgetSpent) return;
                if (++_nodes > NodeBudget) { _budgetSpent = true; return; }
                if (!Fresh(spent, dealt)) return;

                if (_board.IsFinished)
                {
                    if (spent == _limit && _ways < MaxWays)
                    {
                        _ways++;
                        if (_board.Forged > 0) _forged++;
                        if (_board.Fired > 0) _fired++;
                    }
                    return;
                }

                if (spent >= _limit) return;

                // Every remaining cocoon needs at least one burst beside it, and one tap's chain
                // can reach several — so the only floor that is always true is "at least one more
                // tap", which is what the loop above already charges.
                _board.Save(_groundAt[spent], _valueAt[spent], _specialAt[spent], out _packedAt[spent]);

                int hand = _layout.Deal.At(dealt);
                var moves = _movesAt[spent];
                BudRun.Moves(_board, hand, moves);

                for (int i = 0; i < moves.Count; i++)
                {
                    int took = BudRun.Apply(_board, moves[i], hand, null, out _);
                    Walk(spent + 1, dealt + took);
                    _board.Restore(_groundAt[spent], _valueAt[spent], _specialAt[spent], _packedAt[spent]);

                    if (_budgetSpent) return;
                }
            }

            bool Fresh(int spent, int dealt)
            {
                _board.KeyInto(_key, out int length);

                // The colour in hand is part of the position, and so is where the basket is up
                // to: the same grove with red up next is a different problem from the same grove
                // with blue up next, and "RGRB" with red in hand is two different futures.
                _key[length] = (char)('0' + spent);
                _key[length + 1] = (char)('a' + dealt % _layout.Deal.Count);

                return _seen.Add(new string(_key, 0, length + 2));
            }
        }
    }
}
