namespace GlimmerGrove.Modes
{
    /// <summary>
    /// How close a well is to running dry, as a reading a screen can colour by.
    ///
    /// <para>
    /// Fractions of the well's own supply rather than fixed counts, for <c>InkPressure</c>'s
    /// reason: three motes left is comfortable on a board dealt forty and desperate on one
    /// dealt six, so a threshold written as a count would mean a different thing on every
    /// level. Here rather than beside the paint, so the thresholds are pinned by a test
    /// instead of living in a <c>MonoBehaviour</c> where nothing can prove them.
    /// </para>
    /// </summary>
    public enum FallPressure
    {
        /// <summary>Motes to spare.</summary>
        Easy = 0,

        /// <summary>Under a third of the supply: worth counting.</summary>
        Low = 1,

        /// <summary>Under a sixth, which on any well that ships is a drop or two.</summary>
        Critical = 2,
    }

    /// <summary>
    /// A well's supply: how many motes this run may drop, and how many of them are gone.
    ///
    /// <para>
    /// <b>It is the mode's budget, in the unit the mode is graded in</b> (invariant 22b). A
    /// Lightfall run is measured in drops — a drop is this mode's turn — so the same
    /// <c>par × budgetFactor</c> every other mode is dealt is dealt here as motes, the tray
    /// counts them down, and the run ends when the last one has fallen with the well not empty.
    /// Nothing about the grading is special: the same three factors over the same derived par,
    /// which is what stops a second mode quietly retuning the economy.
    /// </para>
    /// <para>
    /// <b>Spending is permanent and there is no undo.</b> That is the whole difference from a
    /// glade's move budget, and it is what a player has to be told (see <c>Mechanic</c>): a
    /// glade hands a turn back for every undo without limit, so exploring costs nothing there,
    /// and somebody who learned that rule and was never taught this one would tap about and
    /// lose. What keeps it fair is that a wrong drop is cheap to <em>discover</em> — the ghost
    /// under the thumb says where the mote lands, whether it enriches and whether it bursts,
    /// before anything is committed — and only expensive to <em>make</em>.
    /// </para>
    /// <para>
    /// Pure integers and no policy at all, for <c>RippleSatchel</c>'s reason: where the budget comes
    /// from is <c>FallMode.Tune</c>'s business, when a run is lost is <see cref="FallVerdict"/>'s,
    /// and what the player sees is the screen's. This is only the arithmetic, so every rule over
    /// it is proved offline against plain numbers.
    /// </para>
    /// </summary>
    public sealed class FallSupply
    {
        /// <summary>
        /// A well with no supply budget, which therefore cannot run dry.
        ///
        /// <see cref="int.MaxValue"/> rather than a flag, so <see cref="Left"/> compares without
        /// special-casing at every call site — the bargain <c>LevelTuning.MoveBudget</c> already
        /// makes for a glade authored with no budget.
        /// </summary>
        public const int Unlimited = int.MaxValue;

        /// <summary>
        /// Motes this run may drop.
        ///
        /// The pot rather than a constant, because it can be topped up: a continue that has
        /// been paid for raises this and nothing else. Note what it deliberately does not touch
        /// — <see cref="Spent"/>, which is the grade, so a well finished with bought motes
        /// scores exactly what it spent (invariant 23).
        /// </summary>
        public int Dealt { get; private set; }

        /// <summary>Motes dropped so far. This is the grade and the record.</summary>
        public int Spent { get; private set; }

        public FallSupply(int dealt)
            => Dealt = dealt < 0 ? Unlimited : dealt;

        public bool Bounded => Dealt != Unlimited;

        /// <summary>Motes still to come. <see cref="Unlimited"/> on a well with no budget.</summary>
        public int Left
        {
            get
            {
                if (!Bounded) return Unlimited;
                int left = Dealt - Spent;
                return left < 0 ? 0 : left;
            }
        }

        /// <summary>Whether there is a mote left to drop at all.</summary>
        public bool Any => Left > 0;

        /// <summary>Takes one. Called once per landed drop and never anywhere else.</summary>
        public void Take() => Spent++;

        /// <summary>
        /// Deals more, because a continue was paid for. Guarded against overflow rather than
        /// trusted: what is handed over is content and a content push is what changes it.
        /// </summary>
        public void Grant(int motes)
        {
            if (motes <= 0 || !Bounded) return;
            Dealt = motes > Unlimited - Dealt ? Unlimited : Dealt + motes;
        }

        /// <summary>
        /// How close this run is to running dry. Unbounded supplies are always
        /// <see cref="FallPressure.Easy"/>, because there is nothing to be close to.
        /// </summary>
        public FallPressure Pressure
        {
            get
            {
                if (!Bounded || Dealt <= 0) return FallPressure.Easy;

                // Integer arithmetic throughout, for LevelTuning's reason: a threshold decided
                // by a float is a threshold three code generators round three ways.
                int left = Left;
                if (left * 6 <= Dealt) return FallPressure.Critical;
                if (left * 3 <= Dealt) return FallPressure.Low;
                return FallPressure.Easy;
            }
        }
    }
}
