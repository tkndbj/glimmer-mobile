namespace GlimmerGrove.Modes
{
    /// <summary>How a Lightfall run stands, once and in one word.</summary>
    public enum FallEnding
    {
        /// <summary>Still being played.</summary>
        Live = 0,

        /// <summary>The well is empty. Won.</summary>
        Emptied = 1,

        /// <summary>A mote came to rest above the brim.</summary>
        Flooded = 2,

        /// <summary>The supply cannot finish what is left standing.</summary>
        Starved = 3,
    }

    /// <summary>
    /// The reading of a well against its supply: whether the run is over, how, and what it
    /// would take to carry it on.
    ///
    /// <para>
    /// <b>One predicate rather than three booleans in an <c>if</c> on a screen.</b> That is the
    /// shape this project keeps paying for: every one of those booleans is an edge where the
    /// run is decided and the screen has not caught up, and a condition spread across them
    /// cannot be proved. <c>WeaveVerdict</c> is the same class for the same reason, and every
    /// branch here is arithmetic over a board and two integers.
    /// </para>
    /// <para>
    /// <b>The two fail states are read in the order a player would want them.</b> An empty well
    /// wins even if the last mote came to rest on the brim before bursting — there is nothing
    /// left to flood — and a run that has just flooded is not also reported as starved.
    /// </para>
    /// <para>
    /// <b><see cref="FallEnding.Starved"/> is a lower bound and it has to be.</b> Ending a run
    /// the player could still have won is the worst thing this mode could do, so the test is
    /// not "does it look hard from here" but a fact about the rules: every channel any mote
    /// ever gains comes from a drop, either by landing on it or by the wash a burst carries.
    /// So a mote missing a channel that does not appear anywhere in the supply that is left can
    /// never be finished, whatever the player does, and the well can never be emptied. That is
    /// sound, it costs one walk of the board, and it is why a run that has become hopeless ends
    /// there rather than making somebody spend six more motes to find out.
    /// </para>
    /// </summary>
    public readonly struct FallVerdict
    {
        public readonly FallEnding Ending;

        /// <summary>
        /// Motes that would have to be restored before a bought one is a usable one, or
        /// <see cref="RunContinueDeficit.None"/> when nothing would help.
        ///
        /// <para>
        /// <b>Not always nought, unlike a glade's</b>, and this is the reason a deficit exists
        /// at all. A well is not only lost when the tray empties: it is also lost when what is
        /// still to come cannot supply a channel some mote is missing. Handing over the
        /// authored allowance alone would then put the player back on a board that is still
        /// provably unfinishable and end the run again in the same frame, having taken their
        /// gems. So the deficit is however many drops it takes for every channel still wanted
        /// to come round again, and the authored figure is working room on top of that.
        /// </para>
        /// </summary>
        public readonly int Deficit;

        FallVerdict(FallEnding ending, int deficit)
        {
            Ending = ending;
            Deficit = deficit;
        }

        public bool IsOver => Ending != FallEnding.Live;
        public bool IsWon => Ending == FallEnding.Emptied;

        /// <summary>
        /// Whether this reading should end the run now.
        ///
        /// <para>
        /// Three clauses, and all three belong here rather than in an <c>if</c> on the screen. A
        /// run decided twice charges two hearts for one loss; one decided before the first mote
        /// has fallen charges a heart for a board nobody touched; and one decided after the well
        /// has already been won puts a defeat panel over a victory.
        /// </para>
        /// </summary>
        public bool EndsTheRun(bool live, bool committed)
            => live && committed && (Ending == FallEnding.Flooded || Ending == FallEnding.Starved);

        /// <summary>
        /// Reads a well and its supply. Pure — every input is passed in — so every branch is
        /// proved offline against a board and two integers.
        /// </summary>
        public static FallVerdict Read(FallBoard board, FallSupply supply, FallDeal deal)
        {
            if (board == null || supply == null) return new FallVerdict(FallEnding.Live, 0);

            if (board.IsEmpty) return new FallVerdict(FallEnding.Emptied, 0);
            if (board.Flooded) return new FallVerdict(FallEnding.Flooded, RunContinueDeficit.None);

            if (!supply.Any)
                return new FallVerdict(FallEnding.Starved, ShortfallFor(board, supply, deal, 1));

            int missing = board.Wanted & ~Coming(supply, deal);
            if (missing != Energy.None)
                return new FallVerdict(FallEnding.Starved, ShortfallFor(board, supply, deal, 1));

            return new FallVerdict(FallEnding.Live, 0);
        }

        /// <summary>Every channel the supply that is left can still deliver.</summary>
        static int Coming(FallSupply supply, FallDeal deal)
        {
            if (deal == null) return Energy.None;
            if (!supply.Bounded) return deal.Channels;

            int mask = Energy.None;
            int left = supply.Left;

            // A lap of the deal is the most that can ever be learnt: past that it repeats.
            int look = left < deal.Count ? left : deal.Count;
            for (int i = 0; i < look; i++) mask |= deal.At(supply.Spent + i);

            return mask;
        }

        /// <summary>
        /// The fewest extra motes that would bring every channel still wanted back round, at
        /// least <paramref name="floor"/>.
        ///
        /// A bounded scan rather than arithmetic on the deal's shape: the deal repeats, so one
        /// lap past whatever is already coming is always enough, and walking it is exact where
        /// a formula would have to reason about where in the lap the run stopped.
        /// </summary>
        static int ShortfallFor(FallBoard board, FallSupply supply, FallDeal deal, int floor)
        {
            if (deal == null || deal.Count == 0) return RunContinueDeficit.None;

            int wanted = board.Wanted;
            if (wanted == Energy.None) return floor;

            // A deal that cannot supply a wanted channel at all is a board no purchase can
            // rescue. The validator refuses to ship one, so this is the guard rather than the
            // ordinary path — and answering "no offer" is the safe direction for the one seam
            // here that charges money.
            if ((wanted & ~deal.Channels) != Energy.None) return RunContinueDeficit.None;

            int have = Coming(supply, deal);
            int extra = 0;

            // Bounded by one full lap beyond what is already coming, which is the most the
            // procession can hide a channel for.
            int ceiling = deal.Count + floor;
            while ((wanted & ~have) != Energy.None && extra < ceiling)
            {
                have |= deal.At(supply.Spent + supply.Left + extra);
                extra++;
            }

            return extra < floor ? floor : extra;
        }
    }

    /// <summary>
    /// The one value a deficit can take that is not a number of anything.
    ///
    /// It mirrors <c>RunContinue.NoContinue</c> so that Domain's board classes can answer the
    /// continue's question without reaching across into the progression layer for a constant —
    /// <c>FallScreen</c> hands this straight through, and <c>FallVerdictTests</c> pins that the
    /// two agree.
    /// </summary>
    public static class RunContinueDeficit
    {
        /// <summary>No amount of allowance would help. See <c>RunContinue.NoContinue</c>.</summary>
        public const int None = -1;
    }
}
