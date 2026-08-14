namespace GlimmerGrove.Daily
{
    /// <summary>
    /// Where a day begins and ends, and the ceilings that bound everything built on it.
    ///
    /// <para>
    /// The boundary is <b>UTC midnight</b>, not the device's midnight. Local midnight is
    /// friendlier — a player in Auckland gets their reset over breakfast rather than at
    /// lunchtime — and it is unusable here for two reasons that outweigh that. It is
    /// trivially farmed by moving the timezone forward, which mints a whole extra day of
    /// chests per tap; and it cannot be validated by the server, which has no way to know
    /// which of the thirty-eight offsets a player is entitled to claim under. A day the
    /// server can adjudicate has to be a day both sides can compute from one number.
    /// </para>
    /// <para>
    /// The day is expressed as an integer count of whole days since the Unix epoch. That
    /// makes "is this a new day" an integer comparison rather than a date calculation,
    /// makes the reset lazy — nothing has to fire at midnight, the next read simply
    /// notices — and makes the cross-device merge a <c>max</c>. Storing a timestamp and
    /// deriving the day at every read would work too, and would put the same calculation
    /// in every caller instead of in one place.
    /// </para>
    /// </summary>
    public static class DailyRules
    {
        public const long SecondsPerDay = 24L * 60L * 60L;

        /// <summary>
        /// Hard ceiling on how many chests a day may hold.
        ///
        /// The count itself is content — see <c>DailyChestTable</c> — because tuning the
        /// daily loop must not need a store review. This bounds what a bad or hostile
        /// content file can ask the home screen to draw, in the same spirit as
        /// <c>ProgressionTable.MaxSupportedLevel</c>.
        /// </summary>
        public const int MaxChests = 6;

        /// <summary>Whole days since the Unix epoch, UTC. Never negative.</summary>
        public static int DayKeyFor(long unix) => unix <= 0L ? 0 : (int)(unix / SecondsPerDay);

        /// <summary>The instant a day begins, so a countdown has something to count to.</summary>
        public static long DayStartUnix(int dayKey) => dayKey <= 0 ? 0L : dayKey * SecondsPerDay;

        /// <summary>
        /// Seconds until the current day rolls over. Drives the countdown on the panel,
        /// which is the only thing that tells a player their unopened chests are on a
        /// clock — an expiry nobody can see is an expiry that reads as a bug.
        /// </summary>
        public static long SecondsUntilReset(long now)
        {
            if (now <= 0L) return SecondsPerDay;

            long into = now % SecondsPerDay;
            return SecondsPerDay - into;
        }
    }
}
