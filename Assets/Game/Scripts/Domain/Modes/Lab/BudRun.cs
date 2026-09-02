using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>What kind of move a run was asked for. The unit every reading here is counted in.</summary>
    public enum BudMoveKind
    {
        /// <summary>The colour in hand onto a flower — or a special or a bomb going off.</summary>
        Tap = 0,

        /// <summary>Two neighbouring flowers trading places.</summary>
        Graft = 1,
    }

    /// <summary>
    /// One move, so a search, a hint and a view can all name the same thing.
    ///
    /// <c>Cell</c> is the flower for a tap and the first flower for a graft; <c>Other</c> is
    /// the second flower of a graft and -1 otherwise.
    /// </summary>
    public readonly struct BudMove
    {
        public readonly BudMoveKind Kind;
        public readonly int Cell, Other;

        public BudMove(BudMoveKind kind, int cell, int other = -1)
        {
            Kind = kind;
            Cell = cell;
            Other = other;
        }

        public static readonly BudMove None = new BudMove(BudMoveKind.Tap, -1);

        public bool Any => Cell >= 0;

        public static BudMove Tap(int cell) => new BudMove(BudMoveKind.Tap, cell);
        public static BudMove Graft(int a, int b) => new BudMove(BudMoveKind.Graft, a, b);

        public override string ToString() => Kind + "(" + Cell + (Other >= 0 ? "," + Other : "") + ")";
    }

    /// <summary>
    /// One go at a grove: the board, the satchel of taps, and the few lines that let the two
    /// move together.
    ///
    /// <para>
    /// <c>FallRun</c> and <c>KeeperRun</c> are the same split for the same reason. The board is
    /// the puzzle, the satchel is the meter, the verdict is the reading of one against the other,
    /// and this is the only thing that knows a tap leaves the satchel when a bud goes off.
    /// </para>
    /// <para>
    /// <b>Two counters, and the difference between them is the graft.</b> <see cref="Spent"/> is
    /// taps out of the satchel, which every move costs. <see cref="Dealt"/> is colours off the
    /// basket, which only a flower tap costs — a graft spends a tap and keeps the colour in
    /// hand, so the player who trades two flowers to make a bunch still has the red they were
    /// saving. A single counter would make every graft also a colour thrown away, which reads
    /// as being punished for using the thing.
    /// </para>
    /// </summary>
    public sealed class BudRun
    {
        public readonly BudLayout Layout;
        public readonly BudBoard Board;
        public readonly BudSatchel Satchel;

        /// <summary>How many were shut in when the grove was dealt. The denominator of everything.</summary>
        public readonly int Critters;

        readonly List<BudPulse> _pulses = new List<BudPulse>(48);

        public BudRun(BudLayout layout, int budget)
        {
            Layout = layout;
            Board = new BudBoard(layout);
            Satchel = new BudSatchel(budget);
            Critters = Board.Shut;
        }

        public BudDeal Deal => Layout.Deal;

        /// <summary>Colours taken off the basket so far. What the hand is read from.</summary>
        public int Dealt { get; private set; }

        /// <summary>The colour in hand, or nothing when the basket is empty.</summary>
        public int Next => Satchel.Any ? Deal.At(Dealt) : Energy.None;

        /// <summary>The colour <paramref name="n"/> behind the one in hand.</summary>
        public int Ahead(int n)
            => n >= 0 && (!Satchel.Bounded || n < Satchel.Left) ? Deal.At(Dealt + n)
                                                                : Energy.None;

        public int Spent => Satchel.Spent;

        /// <summary>How many are still shut in. What the run is for.</summary>
        public int Left => Board.Shut;

        /// <summary>The longest chain this run has set off, in waves. Its own high score.</summary>
        public int Best { get; private set; }

        /// <summary>Every flower this run has burst. What the grove has been through.</summary>
        public int Burst { get; private set; }

        /// <summary>The biggest single bunch it has set off.</summary>
        public int Bunch { get; private set; }

        public BudVerdict Verdict => BudVerdict.Read(Board, Satchel);

        bool Live => Satchel.Any && !Verdict.IsOver;

        public bool CanTap(int cell) => Live && Board.CanTap(cell, Next);

        public bool CanGraft(int a, int b) => Live && Board.CanGraft(a, b);

        public bool Can(BudMove move)
        {
            switch (move.Kind)
            {
                case BudMoveKind.Tap: return CanTap(move.Cell);
                case BudMoveKind.Graft: return CanGraft(move.Cell, move.Other);
                default: return false;
            }
        }

        /// <summary>What the colour in hand would turn this flower into.</summary>
        public int Mixed(int cell) => Board.Mixed(cell, Next);

        public BudChainResult Preview(int cell) => Board.Preview(cell, Next, null, null);

        public BudChainResult Preview(int cell, List<BudPulse> pulses,
                                      List<BudWash> washes = null, List<BudDrop> drops = null)
            => Board.Preview(cell, Next, pulses, washes, drops);

        public BudChainResult Preview(BudMove move, List<BudPulse> pulses = null,
                                      List<BudWash> washes = null, List<BudDrop> drops = null)
        {
            if (!Can(move))
            {
                pulses?.Clear();
                washes?.Clear();
                drops?.Clear();
                return BudChainResult.Nothing;
            }

            return move.Kind == BudMoveKind.Graft
                 ? Board.PreviewGraft(move.Cell, move.Other, pulses, washes, drops)
                 : Board.Preview(move.Cell, Next, pulses, washes, drops);
        }

        public BudChainResult Tap(int cell, List<BudPulse> pulses,
                                  List<BudWash> washes = null, List<BudDrop> drops = null)
            => Play(BudMove.Tap(cell), pulses, washes, drops);

        /// <summary>The one place a move is made, so the counting can only be done one way.</summary>
        public BudChainResult Play(BudMove move, List<BudPulse> pulses,
                                   List<BudWash> washes = null, List<BudDrop> drops = null)
        {
            if (!Can(move))
            {
                pulses?.Clear();
                washes?.Clear();
                drops?.Clear();
                return BudChainResult.Nothing;
            }

            BudChainResult chain;

            if (move.Kind == BudMoveKind.Graft)
            {
                chain = Board.Graft(move.Cell, move.Other, pulses ?? _pulses, washes, drops);
            }
            else
            {
                chain = Board.Tap(move.Cell, Next, pulses ?? _pulses, washes, drops);
                Dealt++;
            }

            Satchel.Take();

            Burst += chain.Burst;
            if (chain.Waves > Best) Best = chain.Waves;
            if (chain.Biggest > Bunch) Bunch = chain.Biggest;

            return chain;
        }

        /// <summary>More taps, because a continue was paid for. The grove stands as it stood.</summary>
        public void Grant(int taps) => Satchel.Grant(taps);

        /// <summary>
        /// Every move this position offers, in the one order every search here walks them.
        ///
        /// <b>The order is part of the contract with the Python mirror.</b> Ties in the careless
        /// player's ranking and in the best-opening-move reading are broken by whichever came
        /// first, so two copies that enumerate differently disagree about a board while both
        /// being right about every move on it. Taps by cell, then grafts by cell — rightward
        /// before downward.
        /// </summary>
        public static void Moves(BudBoard board, int hand, List<BudMove> into)
        {
            into.Clear();
            var layout = board.Layout;

            for (int i = 0; i < board.Count; i++)
                if (board.CanTap(i, hand)) into.Add(BudMove.Tap(i));

            if (layout.Grafts)
                for (int i = 0; i < board.Count; i++)
                {
                    if (i % layout.Width < layout.Width - 1 && board.CanGraft(i, i + 1))
                        into.Add(BudMove.Graft(i, i + 1));
                    if (i / layout.Width < layout.Height - 1 && board.CanGraft(i, i + layout.Width))
                        into.Add(BudMove.Graft(i, i + layout.Width));
                }
        }

        /// <summary>
        /// Plays one move on a bare board, which is what a search does without a satchel.
        /// Returns how far the hand moves on: one for a flower tap, nought for a graft.
        /// </summary>
        public static int Apply(BudBoard board, BudMove move, int hand, List<BudPulse> pulses,
                                out BudChainResult chain, List<BudWash> washes = null,
                                List<BudDrop> drops = null)
        {
            if (move.Kind == BudMoveKind.Graft)
            {
                chain = board.Graft(move.Cell, move.Other, pulses, washes, drops);
                return 0;
            }

            chain = board.Tap(move.Cell, hand, pulses, washes, drops);
            return 1;
        }
    }
}
