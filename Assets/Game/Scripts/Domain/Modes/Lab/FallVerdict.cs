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

        /// <summary>The supply ran out with motes still standing.</summary>
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
    /// cannot be proved. <c>RippleVerdict</c> is the same class for the same reason, and every
    /// branch here is arithmetic over a board and two integers.
    /// </para>
    /// <para>
    /// <b>The two fail states are read in the order a player would want them.</b> An empty well
    /// wins even if the last mote came to rest on the brim before bursting — there is nothing
    /// left to flood — and a run that has just flooded is not also reported as starved.
    /// </para>
    /// <para>
    /// <b>A run ends when the supply runs out, and not one drop before.</b> There used to be a
    /// second clause here and it was <em>sound</em>: every channel a mote ever gains comes from
    /// a drop, so a mote missing a colour the remaining supply cannot deliver can never be
    /// finished, and the well is provably unemptiable. It ended the run there rather than making
    /// somebody spend the rest of their motes to find out.
    /// </para>
    /// <para>
    /// <b>It was removed anyway, and being right is not the same as being wanted.</b> Reported
    /// from play as a run that ended while the tray still had motes in it — which reads as the
    /// game deciding on the player's behalf, and is indistinguishable from a bug unless you
    /// already know the rule it is enforcing. A player who wants to spend their last three motes
    /// on a board that cannot be won is entitled to; it costs them nothing they had not already
    /// lost, and the alternative is being told "no" by something they cannot see. The proof
    /// survives as <see cref="Deficit"/>, where it is still exactly the right question — how
    /// much would have to be bought before a continue was usable room.
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
        /// at all. A glade is lost when its counter reaches the budget and any turn makes it
        /// playable again. A well runs dry at a moment that has nothing to do with what is
        /// standing in it — so the motes that come next may be the wrong colours entirely, and
        /// handing over the authored allowance alone would put the player back on a board that
        /// still cannot be finished and end the run again a few drops later, having taken their
        /// gems. So the deficit is however many drops it takes for every channel still wanted to
        /// come round, and the authored figure is working room on top of that.
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

            // There used to be a clause here refusing a continue on a well with no mote left to
            // cook — glass with nothing to light it, which was then genuinely unfinishable. It
            // is gone because the board changed underneath it: a drop now feeds glass it lands
            // on, so a lone lens is one drop from firing and more motes are exactly what such a
            // run needs. Left in, it would refuse the offer on the one board where it is most
            // obviously worth taking. See `FallBoard.Charges`.

            // There is deliberately no clause here for a whorl either. A drop opens one whatever
            // colour it is, and one with nothing beside it closes rather than waiting - so a
            // whorl can always be got rid of and more motes really can finish a well full of
            // them, which is the property the lens had to have a valve added to acquire and the
            // reason this mechanic was built with it from the start.
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
