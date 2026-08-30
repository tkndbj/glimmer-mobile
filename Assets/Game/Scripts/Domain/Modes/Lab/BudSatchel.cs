namespace GlimmerGrove.Modes
{
    /// <summary>How close a grove is to running out, in three words rather than a number.</summary>
    public enum BudPressure
    {
        Easy = 0,
        Low = 1,
        Critical = 2,
    }

    /// <summary>
    /// The taps a grove is dealt, and what has become of them.
    ///
    /// <para>
    /// Pure integers and no policy at all, for <c>KeeperBasket</c>'s reason: where the budget
    /// comes from is <c>LevelTuning</c>'s business and what a continue costs is
    /// <c>ContinueTable</c>'s. This only counts, which is what lets "three taps spent of eight
    /// leaves five" be proved offline against plain arithmetic rather than by building a grove.
    /// </para>
    /// </summary>
    public sealed class BudSatchel
    {
        /// <summary>What a grove with no fail line is dealt.</summary>
        public const int Unlimited = int.MaxValue;

        public int Dealt { get; private set; }
        public int Spent { get; private set; }

        public BudSatchel(int dealt) => Dealt = dealt < 0 ? Unlimited : dealt;

        public bool Bounded => Dealt != Unlimited;

        public int Left
        {
            get
            {
                if (!Bounded) return Unlimited;

                int left = Dealt - Spent;
                return left < 0 ? 0 : left;
            }
        }

        public bool Any => Left > 0;

        public void Take() => Spent++;

        /// <summary>More taps, because a continue was paid for. Never a fresh grove.</summary>
        public void Grant(int taps)
        {
            if (taps <= 0 || !Bounded) return;

            // Clamped one below the sentinel rather than at it: a top-up that overflowed into
            // "unbounded" would retire the fail state the mode rests on, silently, on the one
            // path where somebody has just paid for it.
            Dealt = taps >= Unlimited - Dealt ? Unlimited - 1 : Dealt + taps;
        }

        public BudPressure Pressure
        {
            get
            {
                if (!Bounded || Dealt <= 0) return BudPressure.Easy;

                int left = Left;
                if (left * 6 <= Dealt) return BudPressure.Critical;
                if (left * 3 <= Dealt) return BudPressure.Low;
                return BudPressure.Easy;
            }
        }
    }
}
