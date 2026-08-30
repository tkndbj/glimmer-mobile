using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// One go at a grove: the board, the satchel of taps, and the few lines that let the two
    /// move together.
    ///
    /// <para>
    /// <c>FallRun</c> and <c>KeeperRun</c> are the same split for the same reason. The board is
    /// the puzzle, the satchel is the meter, the verdict is the reading of one against the other,
    /// and this is the only thing that knows a tap leaves the satchel when a bud goes off.
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

        /// <summary>The colour in hand, or nothing when the basket is empty.</summary>
        public int Next => Satchel.Any ? Deal.At(Satchel.Spent) : Energy.None;

        /// <summary>The colour <paramref name="n"/> behind the one in hand.</summary>
        public int Ahead(int n)
            => n >= 0 && (!Satchel.Bounded || n < Satchel.Left) ? Deal.At(Satchel.Spent + n)
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

        public bool CanTap(int cell)
            => Satchel.Any && !Verdict.IsOver && Board.CanTap(cell, Next);

        /// <summary>What the colour in hand would turn this flower into.</summary>
        public int Mixed(int cell) => Board.Mixed(cell, Next);

        /// <summary>
        /// Whether tapping this cell would set anything off at all.
        ///
        /// <b>This is what the board draws a halo on, and it is the single change that took the
        /// arithmetic out of the mode.</b> Every other game of this shape shows the player the
        /// matches and asks them to pick one; Budburst made them work out, in their head, which
        /// cell the colour in hand would turn into a third of something. Now the grove says which
        /// taps pop and the player picks — the choice is still theirs, and it is a choice between
        /// visible things.
        /// </summary>
        public bool Pops(int cell) => CanTap(cell) && Board.Preview(cell, Next).Any;

        public BudChainResult Preview(int cell) => Board.Preview(cell, Next, null, null);

        public BudChainResult Preview(int cell, List<BudPulse> pulses,
                                      List<BudWash> washes = null, List<BudDrop> drops = null)
            => Board.Preview(cell, Next, pulses, washes, drops);

        public BudChainResult Tap(int cell, List<BudPulse> pulses,
                                  List<BudWash> washes = null, List<BudDrop> drops = null)
        {
            if (!CanTap(cell))
            {
                pulses?.Clear();
                washes?.Clear();
                drops?.Clear();
                return BudChainResult.Nothing;
            }

            var chain = Board.Tap(cell, Next, pulses ?? _pulses, washes, drops);
            Satchel.Take();

            Burst += chain.Burst;
            if (chain.Waves > Best) Best = chain.Waves;
            if (chain.Biggest > Bunch) Bunch = chain.Biggest;

            return chain;
        }

        /// <summary>More taps, because a continue was paid for. The grove stands as it stood.</summary>
        public void Grant(int taps) => Satchel.Grant(taps);
    }
}
