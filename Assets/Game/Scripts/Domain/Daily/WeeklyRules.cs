namespace GlimmerGrove.Daily
{
    /// <summary>
    /// What a week is, for everything that resets weekly.
    ///
    /// <para>
    /// The same discipline as <see cref="DailyRules"/>, one unit up: a week is an integer
    /// count since the epoch, so "is this a new week" is an <c>int</c> compare, the reset
    /// is lazy (the next read notices), and the cross-device merge is a <c>max</c>. It is
    /// derived from the <em>day</em> key rather than from the clock a second time, so the
    /// two calendars cannot disagree about where a day falls — a week turns over at the
    /// same UTC midnight a day does, never an hour either side of it.
    /// </para>
    /// <para>
    /// <b>Weeks begin on Monday, UTC.</b> The epoch fell on a Thursday, so a naive
    /// <c>day / 7</c> would reset every Thursday morning, which nobody on Earth reads as
    /// the start of a week. The offset is three days, which puts week 1 at Monday the 5th
    /// of January 1970; week 0 is the four-day stub before it, which no live player has a
    /// key in and which is therefore what zero can mean — the same trick
    /// <c>DailyStateDto.dayKey</c> plays with 1970.
    /// </para>
    /// <para>
    /// <b>The offset is contract.</b> The server derives the same key from the same day
    /// (<c>weekKey</c> in <c>functions/src/tasks.ts</c>) to bound a weekly claim, and a
    /// client and a server that disagreed about which week a Sunday belongs to would pay a
    /// task twice on one side of midnight and refuse it on the other.
    /// </para>
    /// </summary>
    public static class WeeklyRules
    {
        public const int DaysPerWeek = 7;

        public const long SecondsPerWeek = DaysPerWeek * DailyRules.SecondsPerDay;

        /// <summary>Days between the epoch's Thursday and the first Monday after it.</summary>
        public const int MondayOffset = 3;

        /// <summary>Whole weeks since the epoch, Monday-aligned, UTC. Never negative.</summary>
        public static int WeekKeyFor(long unix) => WeekOfDay(DailyRules.DayKeyFor(unix));

        /// <summary>The week a day key falls in.</summary>
        public static int WeekOfDay(int dayKey) => dayKey <= 0 ? 0 : (dayKey + MondayOffset) / DaysPerWeek;

        /// <summary>The Monday a week begins on, as a day key.</summary>
        public static int WeekStartDay(int weekKey)
            => weekKey <= 0 ? 0 : weekKey * DaysPerWeek - MondayOffset;

        /// <summary>The instant a week begins, so a countdown has something to count to.</summary>
        public static long WeekStartUnix(int weekKey) => DailyRules.DayStartUnix(WeekStartDay(weekKey));

        /// <summary>
        /// Seconds until the next Monday midnight, UTC. Never zero: at the boundary the
        /// answer is a whole week, because that instant already belongs to the new one.
        /// </summary>
        public static long SecondsUntilReset(long now)
        {
            if (now <= 0L) return SecondsPerWeek;

            long next = WeekStartUnix(WeekKeyFor(now) + 1);
            long left = next - now;
            return left <= 0L ? SecondsPerWeek : left;
        }
    }
}
