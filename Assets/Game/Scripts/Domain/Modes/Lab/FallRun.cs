using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// One Lightfall run: a well, the supply it is dealt, and the ten lines that let the two
    /// move together.
    ///
    /// <para>
    /// <b>It is small on purpose.</b> All of this lived inside <c>FallBoard</c> once, and one
    /// change turned that class into a puzzle model with an economy and a fail state in it —
    /// after which none of the three could be tested without building a well. <c>WeaveRun</c>
    /// is the same split for the same reason, and it is what makes "the supply is taken exactly
    /// once per landed drop" and "a lost run is only ever ended once" arithmetic over integers
    /// rather than a claim about a screen.
    /// </para>
    /// </summary>
    public sealed class FallRun
    {
        public readonly FallLayout Layout;
        public readonly FallBoard Board;
        public readonly FallSupply Supply;

        /// <summary>How many motes were standing when the run began, for the readout.</summary>
        public readonly int Started;

        readonly List<FallStep> _steps = new List<FallStep>();

        public FallRun(FallLayout layout, int budget)
        {
            Layout = layout;
            Board = new FallBoard(layout);
            Supply = new FallSupply(budget);
            Started = Board.Motes;
        }

        public FallDeal Deal => Layout.Deal;

        /// <summary>The mote about to fall.</summary>
        public int Next => Supply.Any ? Deal.At(Supply.Spent) : Energy.None;

        /// <summary>What is queued behind it, for the tray. <paramref name="n"/> counts from nought.</summary>
        public int Ahead(int n)
            => n >= 0 && (!Supply.Bounded || n < Supply.Left) ? Deal.At(Supply.Spent + n)
                                                              : Energy.None;

        /// <summary>Motes dropped, which is the grade and the record.</summary>
        public int Drops => Supply.Spent;

        /// <summary>Motes still standing in the well.</summary>
        public int Left => Board.Motes;

        /// <summary>The longest chain this run has set off — the number worth shouting about.</summary>
        public int Best { get; private set; }

        /// <summary>How the run stands. Recomputed rather than cached: it is one walk of a small board.</summary>
        public FallVerdict Verdict => FallVerdict.Read(Board, Supply, Deal);

        /// <summary>Whether a mote may be dropped into this column right now.</summary>
        public bool CanDrop(int column)
            => Supply.Any && !Verdict.IsOver && Board.CanDrop(Next, column);

        /// <summary>
        /// Drops the next mote and resolves everything that follows, or null if it could not be
        /// dropped there.
        ///
        /// <para>
        /// <b>The supply is taken here and nowhere else</b>, and only once the drop is known to
        /// have landed. A run charged for a tap that could not be honoured is a run that quietly
        /// costs a player a mote for touching a full column.
        /// </para>
        /// </summary>
        public FallResolution Drop(int column)
        {
            if (!CanDrop(column)) return null;

            _steps.Clear();
            var result = Board.Drop(Next, column, _steps);
            if (result == null) return null;

            Supply.Take();

            if (result.Waves > Best) Best = result.Waves;

            return result;
        }

        /// <summary>
        /// Deals more motes, because a continue was paid for.
        ///
        /// Nothing else moves: the well stands exactly as it stood, and <see cref="Drops"/> —
        /// the grade — is untouched, so a bought run scores what it spent. Since a run only
        /// reaches the offer after spending past the two-star line, it can score one star at
        /// most (invariant 23).
        /// </summary>
        public void Grant(int motes) => Supply.Grant(motes);
    }
}
