namespace GlimmerGrove.Notifications
{
    /// <summary>
    /// One row of the slate: a kind, where it wants to sit, how it ranks, and how often it may
    /// come round again.
    ///
    /// <para>
    /// <b>There is no text on it.</b> The copy is derived from the kind's permanent id
    /// (<see cref="NotificationKinds.TitleKey"/>), so a row can never disagree with the string
    /// table and a content push can never ship an untranslated sentence — see the note on
    /// <see cref="NotificationKinds"/> for why that line is where it is.
    /// </para>
    /// <para>
    /// <b><see cref="MinDaysBetween"/> is the only dial that stops this becoming spam, and it
    /// is the reason the planner walks days in order rather than scoring them independently.</b>
    /// Three slots a day over a seven-day horizon is twenty-one picks; without a per-kind
    /// cooldown the same three sentences fire at the same three times every day for a week,
    /// which is exactly how a player learns to swipe the whole app away. With it, a slot that
    /// finds no eligible candidate is simply left <em>empty</em> — which is where "two to
    /// three a day" comes from: it is an outcome of the cooldowns rather than a quota
    /// somebody has to keep the table consistent with.
    /// </para>
    /// </summary>
    public sealed class NotificationEntry
    {
        public NotificationEntry(NotificationKind kind, NotificationSlot slot,
                                 int priority, int minDaysBetween, bool enabled)
        {
            Kind = kind;
            Slot = slot;
            Priority = priority;
            MinDaysBetween = minDaysBetween < 1 ? 1 : minDaysBetween;
            Enabled = enabled;
        }

        public NotificationKind Kind { get; }

        /// <summary>Which time of day this wants. <see cref="NotificationSlot.Any"/> takes leftovers.</summary>
        public NotificationSlot Slot { get; }

        /// <summary>
        /// Higher wins a contested slot. Ties break on the order the table lists them, which
        /// is stated rather than emergent for <c>SiegeBoard</c>'s reason: a dictionary walk is
        /// not promised to enumerate the same way on two runtimes, and a plan that differed
        /// between a test and a phone would be a plan no fixture could pin.
        /// </summary>
        public int Priority { get; }

        /// <summary>Whole days that must pass before this kind may be sent again.</summary>
        public int MinDaysBetween { get; }

        /// <summary>
        /// Whether the slate sends this at all.
        ///
        /// The one thing here that genuinely needs to be content: a notification that turns
        /// out to annoy people has to be switchable off without two store reviews, and a
        /// kind removed from the table entirely would take its cooldown history with it.
        /// </summary>
        public bool Enabled { get; }
    }
}
