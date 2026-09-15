namespace GlimmerGrove.Notifications
{
    /// <summary>
    /// The bounds everything about the schedule is held inside: how many slots a day has, how
    /// early and late one may be, how many may be pending at once, and how soon the first may
    /// land.
    ///
    /// <para>
    /// Separate from <see cref="NotificationTable"/> for <c>DailyRules</c>' reason: the table
    /// is content and may be retuned by a push, and these are the ceilings that bound what a
    /// bad or hostile content file can ask a phone to do. A slate that asked for eight a day
    /// at four in the morning has to be refused by something that a push cannot move.
    /// </para>
    /// </summary>
    public static class NotificationWindow
    {
        /// <summary>How many times of day exist. One per <see cref="NotificationSlot"/> that is not <c>Any</c>.</summary>
        public const int Slots = 3;

        /// <summary>The earliest a notification may be scheduled, in minutes past local midnight.</summary>
        public const int EarliestMinute = 8 * 60;

        /// <summary>The latest. Past this is somebody's evening rather than their day.</summary>
        public const int LatestMinute = 21 * 60 + 30;

        /// <summary>
        /// The most that may ever be pending.
        ///
        /// <para>
        /// <b>iOS's own limit is 64 and it is enforced by silence.</b> <c>UNUserNotificationCenter</c>
        /// keeps the 64 soonest pending requests and drops the rest without an error, a log
        /// line or a callback — so a horizon that overflowed it would be a schedule that
        /// worked in every test, on every Android device, and quietly stopped a week out on
        /// iPhone. Sixty leaves room for the platform's own and makes the overflow an
        /// arithmetic fault a content gate can refuse rather than a thing somebody discovers.
        /// </para>
        /// </summary>
        public const int MaxPending = 60;

        /// <summary>
        /// The least time between arming the schedule and the first thing in it.
        ///
        /// <para>
        /// A player who has just closed the game does not need telling about it twenty
        /// minutes later — they were *here*. It also covers the ordinary case of somebody
        /// backgrounding the app to answer a message and coming straight back, which would
        /// otherwise fire a reminder over their own session.
        /// </para>
        /// </summary>
        public const long QuietSeconds = 3L * 60L * 60L;

        /// <summary>
        /// Minutes past local midnight for <paramref name="slot"/>, given the table's hours.
        /// <see cref="NotificationSlot.Any"/> has no hour of its own and answers -1.
        /// </summary>
        public static int MinuteOf(NotificationSlot slot, int[] hours)
        {
            if (slot == NotificationSlot.Any || hours == null) return -1;

            int index = (int)slot - 1;
            return index >= 0 && index < hours.Length ? hours[index] : -1;
        }
    }
}
