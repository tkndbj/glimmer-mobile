namespace GlimmerGrove.Notifications
{
    /// <summary>
    /// The three times of day this game is allowed to speak, and which one a kind belongs in.
    ///
    /// <para>
    /// <b>They are hours of the player's <em>local</em> day, and that is not a nicety.</b>
    /// Every other clock in this project is UTC on purpose — a day the server can adjudicate
    /// has to be a day both sides compute from one number (<c>DailyRules</c>) — and a
    /// notification is the one thing here that must not be, because UTC 09:00 is three in the
    /// morning for a third of the world and a game that wakes somebody at three is a game
    /// they uninstall. Nothing is adjudicated here, nothing is paid, so there is nothing to
    /// farm by moving a timezone: the worst a player can do by lying about the hour is get
    /// their own reminder at the wrong time.
    /// </para>
    /// <para>
    /// <b>A slot is a preference rather than a rule, and the order slots are filled in is the
    /// design.</b> The evening is the best window and is filled first, so the most urgent
    /// thing the planner has goes where it will be seen — which is the opposite of what
    /// ranking the whole day at once would do, since that hands the evening whatever is left
    /// over. A kind that fits nowhere in particular is <see cref="Any"/> and fills whatever
    /// the named ones did not want.
    /// </para>
    /// </summary>
    public enum NotificationSlot
    {
        /// <summary>Fills whichever slot is left. The evergreen kinds.</summary>
        Any = 0,

        /// <summary>Something new has been dealt overnight and is waiting.</summary>
        Morning,

        /// <summary>Something has finished refilling since they put the game down.</summary>
        Afternoon,

        /// <summary>Something runs out at midnight, or is worth a longer sitting.</summary>
        Evening,
    }
}
