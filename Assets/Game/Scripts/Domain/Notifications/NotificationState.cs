using GlimmerGrove.Daily;

namespace GlimmerGrove.Notifications
{
    /// <summary>
    /// Everything the planner is allowed to know, captured the instant the app is backgrounded.
    ///
    /// <para>
    /// <b>This struct is the whole reason the scheme is free.</b> A notification can only be
    /// scheduled *ahead* — nothing runs on the device once the app is gone — so the planner
    /// must answer, now, whether a sentence will be true at 19:30 four days from now. That is
    /// only possible for facts that are a pure function of (what the save says now, the wall
    /// clock), and every field below is one: a refill is a due time, a task slate is a
    /// rotation on the day number (invariant 45b), a streak is a pair of day keys, a season
    /// is a calendar window. Nothing here needs a server to know it, which is why no server
    /// is involved.
    /// </para>
    /// <para>
    /// <b>The rule that keeps it honest is the one about arguments.</b> A notification's text
    /// is baked when it is scheduled and read up to a week later, so any number inside it
    /// must be a function of the *fire* instant and never of the *schedule* instant — "play
    /// today to keep night 12" is correct when written and a lie four days on. Every string
    /// this game ships is therefore argument-free, and this note is what the next person adds
    /// one against.
    /// </para>
    /// <para>
    /// A plain snapshot rather than a set of live accessors, for <c>SiegeAttention</c>'s
    /// reason: the planner is then a pure function of two values and the whole of it runs in
    /// the offline test suite with no save, no clock and no phone.
    /// </para>
    /// </summary>
    public readonly struct NotificationState
    {
        /// <summary>UTC seconds at the moment the plan is built.</summary>
        public readonly long NowUnix;

        /// <summary>
        /// The device's offset from UTC, in seconds, as it is right now.
        ///
        /// <para>
        /// Slots are hours of the player's own day and everything else in this game is UTC
        /// (<c>DailyRules</c>), so the two have to be bridged somewhere and this is the only
        /// number that does it. <b>A daylight-saving change inside the horizon shifts the
        /// remaining slots by an hour</b>, because the offset is captured once — which is
        /// accepted rather than solved: the plan is rebuilt every time the app is
        /// backgrounded, so the error lasts until the player next opens the game, and an
        /// hour's drift on a reminder is not worth carrying a timezone database for.
        /// </para>
        /// </summary>
        public readonly int UtcOffsetSeconds;

        /// <summary>
        /// When the heart bar reaches its refill cap. Nought when it is already there.
        ///
        /// Nought is a real answer and not an absent one, which is why the predicate treats
        /// it separately rather than comparing against it: hearts that were already full when
        /// the player put the game down are not news that evening — they were holding them.
        /// </summary>
        public readonly long HeartsFullUnix;

        /// <summary>Whether a streak night is sitting uncollected. It never expires, so it stays true.</summary>
        public readonly bool StreakChestWaiting;

        /// <summary>The UTC day key the player last played on.</summary>
        public readonly int StreakLastPlayedDay;

        /// <summary>How many nights the streak is worth right now. Under two, nothing nags about it.</summary>
        public readonly int StreakDays;

        /// <summary>The first day a shield covers, and how many days it covers. Nought for none.</summary>
        public readonly int ShieldFromDay;
        public readonly int ShieldDays;

        /// <summary>Whether a season is running, and the last UTC day it can be claimed on.</summary>
        public readonly bool SeasonOpen;
        public readonly int SeasonEndDay;

        /// <summary>Whether any season rung is standing open right now.</summary>
        public readonly bool SeasonRungsReady;

        /// <summary>Whether this keeper's grove is published, so a board is a thing they are on.</summary>
        public readonly bool OnBoards;

        /// <summary>Whether the Infinite lane's keeper wall has been cleared (invariant 43d).</summary>
        public readonly bool EndlessOpen;

        public NotificationState(long nowUnix, int utcOffsetSeconds, long heartsFullUnix,
                                 bool streakChestWaiting, int streakLastPlayedDay, int streakDays,
                                 int shieldFromDay, int shieldDays,
                                 bool seasonOpen, int seasonEndDay, bool seasonRungsReady,
                                 bool onBoards, bool endlessOpen)
        {
            NowUnix = nowUnix;
            UtcOffsetSeconds = utcOffsetSeconds;
            HeartsFullUnix = heartsFullUnix;
            StreakChestWaiting = streakChestWaiting;
            StreakLastPlayedDay = streakLastPlayedDay;
            StreakDays = streakDays;
            ShieldFromDay = shieldFromDay;
            ShieldDays = shieldDays;
            SeasonOpen = seasonOpen;
            SeasonEndDay = seasonEndDay;
            SeasonRungsReady = seasonRungsReady;
            OnBoards = onBoards;
            EndlessOpen = endlessOpen;
        }

        /// <summary>The UTC day this plan was built on.</summary>
        public int Today => DailyRules.DayKeyFor(NowUnix);

        /// <summary>
        /// Whether <paramref name="kind"/>'s sentence would be true at <paramref name="fireUnix"/>.
        ///
        /// <para>
        /// One method rather than a predicate per kind on ten classes, because these have to
        /// be read <em>together</em> to be reasoned about: the question this design keeps
        /// asking is "does any of these reject anything" (invariant 5d), and ten files is how
        /// a kind comes to be one that is always true without anybody noticing.
        /// </para>
        /// </summary>
        public bool IsTrueAt(NotificationKind kind, long fireUnix)
        {
            int day = DailyRules.DayKeyFor(fireUnix);

            switch (kind)
            {
                // The day the refill lands, and only that day.
                //
                // <b>This is news, not a standing fact, and the difference is the whole
                // quality of the feature.</b> Written as "the bar is full at this instant" it
                // is true of every day after the refill completes, so a player who stays away
                // for a week is told their hearts are full on seven consecutive afternoons in
                // identical words — which is how an app gets muted. Bounded to one day it says
                // the thing on the day it happened and then stops, and the plan naturally
                // thins out the longer somebody is away. A game that nags harder the longer
                // you ignore it is a game you uninstall.
                //
                // If the bar was already full when they put the phone down, the "event" is the
                // next day rather than an instant that has passed — they were holding a full
                // bar an hour ago, so it is not news that evening.
                case NotificationKind.HeartsFull:
                    if (HeartsFullUnix > 0L)
                        return fireUnix >= HeartsFullUnix
                            && day <= DailyRules.DayKeyFor(HeartsFullUnix) + 1;

                    return day == Today + 1;

                // A slate is dealt by the day number alone, so "there are new tasks" is true
                // of every day after this one, with nothing stored and nothing to check.
                case NotificationKind.TasksDaily:
                    return day > Today;

                case NotificationKind.TasksWeekly:
                    return WeeklyRules.WeekOfDay(day) > WeeklyRules.WeekOfDay(Today);

                // Exactly the day after the last one played, and only while a shield is not
                // already covering it. A day later than that and the streak has broken, so
                // the sentence would be a lie; with a shield up it is safe, so the sentence
                // would be a nag for something the player has already paid to prevent.
                case NotificationKind.StreakRisk:
                    return StreakDays >= 2
                        && day == StreakLastPlayedDay + 1
                        && !DailyStreak.ShieldCovers(ShieldFromDay, day, ShieldDays);

                // A waiting night does not expire, so this stays true until they come and
                // take it — which is the point.
                case NotificationKind.ChestWaiting:
                    return StreakChestWaiting;

                case NotificationKind.SeasonRungs:
                    return SeasonOpen && SeasonRungsReady && day <= SeasonEndDay;

                case NotificationKind.SeasonEnding:
                    return SeasonOpen && day >= SeasonEndDay - 2 && day <= SeasonEndDay;

                case NotificationKind.BoardClimb:
                    return OnBoards;

                case NotificationKind.EndlessCall:
                    return EndlessOpen;

                // The evergreen one, and the only one here that rejects nothing. It is
                // allowed to exist because something has to fill a quiet day, and it is
                // bounded by being last in the ladder and by its own cooldown rather than by
                // a predicate (invariant 5d, answered with a ranking instead of a rule).
                case NotificationKind.GroveIdle:
                    return true;

                default:
                    return false;
            }
        }
    }
}
