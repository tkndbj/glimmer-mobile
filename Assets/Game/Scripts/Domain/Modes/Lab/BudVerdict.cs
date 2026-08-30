namespace GlimmerGrove.Modes
{
    /// <summary>How a grove stands. Read in one place so a run can only be decided once.</summary>
    public enum BudEnding
    {
        Live = 0,

        /// <summary>Every critter is out.</summary>
        Woken = 1,

        /// <summary>The taps ran out with somebody still shut in.</summary>
        Spent = 2,

        /// <summary>
        /// No legal tap left, and a critter still shut in. No number of taps helps.
        ///
        /// Not the same as "no flower left": a white flower holds every channel, so it can never
        /// be mixed into — see <c>BudBoard.AnyMove</c>.
        /// </summary>
        Barren = 3,
    }

    /// <summary>
    /// The reading of a grove against its satchel, in one predicate.
    ///
    /// <para>
    /// <see cref="FallVerdict"/>, <see cref="KeeperVerdict"/> and this are the same class for the
    /// same reason: three booleans in an <c>if</c> on a screen is three edges where the run is
    /// decided and the screen has not caught up.
    /// </para>
    /// <para>
    /// <b>Two endings and only one of them may be sold a continue</b> (invariants 23, 26b, 28c).
    /// Running out of taps is a shortage and more taps fix it. A grove with no legal tap left in
    /// it is not: nothing here ever grows a flower back and a colour already carried cannot be
    /// mixed in twice, so no number of taps could reach the cocoons that are left, and selling one
    /// would take somebody's gems for a board that cannot be finished.
    /// </para>
    /// </summary>
    public readonly struct BudVerdict
    {
        public readonly BudEnding Ending;

        /// <summary>
        /// What has to be handed over before a bought run could carry on, above whatever the
        /// continue table authors. Nought here — a grove is lost the tap its satchel empties,
        /// and one tap is a legal move again — or <c>RunContinueDeficit.None</c> when nothing
        /// would help.
        /// </summary>
        public readonly int Deficit;

        BudVerdict(BudEnding ending, int deficit)
        {
            Ending = ending;
            Deficit = deficit;
        }

        public bool IsOver => Ending != BudEnding.Live;
        public bool IsWon => Ending == BudEnding.Woken;

        /// <summary>
        /// Whether this reading should end a live, committed run. The third clause belongs here
        /// rather than in an <c>if</c> on the screen: a run decided twice charges two hearts for
        /// one loss, and one decided before the first tap charges a heart for a board nobody
        /// touched.
        /// </summary>
        public bool EndsTheRun(bool live, bool committed)
            => live && committed
            && (Ending == BudEnding.Spent || Ending == BudEnding.Barren);

        public static BudVerdict Read(BudBoard board, BudSatchel satchel)
        {
            if (board == null || satchel == null) return new BudVerdict(BudEnding.Live, 0);

            if (board.IsFinished) return new BudVerdict(BudEnding.Woken, 0);

            if (!board.AnyMove())
                return new BudVerdict(BudEnding.Barren, RunContinueDeficit.None);

            if (!satchel.Any) return new BudVerdict(BudEnding.Spent, 0);

            return new BudVerdict(BudEnding.Live, 0);
        }
    }
}
