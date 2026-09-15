using System;

namespace GlimmerGrove.Notifications
{
    /// <summary>
    /// The things this game is ever allowed to interrupt somebody's day about.
    ///
    /// <para>
    /// <b>Every one of these is a statement about state the device already holds, and that is
    /// the whole architecture.</b> Hearts refill on a clock, the task slate rotates on a pure
    /// function of the day (invariant 45b), a streak is a pair of day numbers, a season is a
    /// calendar window. So every reminder this game wants to send can be *derived on the
    /// phone*, which means there is no token to store, no fan-out job to run and no
    /// per-player-per-day server bill — the same bargain invariant 14 strikes for rewards,
    /// collected again. At ten million players a daily fan-out is tens of millions of
    /// document reads a day for ever; this is nought, for ever.
    /// </para>
    /// <para>
    /// <b>What that costs is the one thing local notifications cannot do:</b> a genuine
    /// broadcast — "a new chapter is live", "double rewards this weekend" — is not derivable
    /// from anything on the device, so it is not here. That is a real gap and it has a real
    /// answer that is also free (FCM *topic* messages, which Google fans out with no token
    /// storage and no per-device work); it is deliberately not built, because an unused push
    /// dependency is placeholder architecture and the day it is wanted it is one package and
    /// one implementation of <c>INotificationScheduler</c>'s sibling.
    /// </para>
    /// <para>
    /// <b>The ordinals reach analytics on every notification ever opened, so this enum is
    /// append-only</b> and a retired kind is kept rather than deleted — the same rule
    /// <c>DefeatReason</c> and <c>ChestDropKind</c> live under. The *strings* in
    /// <see cref="NotificationKinds"/> are what name loc keys and what a content file
    /// authors, so they are permanent for invariant 1's reason as well.
    /// </para>
    /// </summary>
    public enum NotificationKind
    {
        None = 0,

        /// <summary>The refill has reached the cap. Derived from the heart ledger's due time.</summary>
        HeartsFull,

        /// <summary>A new day's task slate has been dealt.</summary>
        TasksDaily,

        /// <summary>A new week's slate has been dealt.</summary>
        TasksWeekly,

        /// <summary>A streak that will break unless the player opens the game today.</summary>
        StreakRisk,

        /// <summary>Something claimable was left sitting there when the player closed the game.</summary>
        ChestWaiting,

        /// <summary>Season rungs are standing open.</summary>
        SeasonRungs,

        /// <summary>The season's window is nearly shut.</summary>
        SeasonEnding,

        /// <summary>The boards are rebuilt nightly and this keeper is on them.</summary>
        BoardClimb,

        /// <summary>The Infinite lane, for somebody who has it open.</summary>
        EndlessCall,

        /// <summary>The evergreen one. True for everybody, which is why it is last.</summary>
        GroveIdle,
    }

    /// <summary>
    /// The permanent string id of each kind, and the loc keys derived from it.
    ///
    /// <para>
    /// <b>A kind's copy is derived from its id and cannot be overridden</b>, which is
    /// invariant 5a applied to a sentence: anything holding a <see cref="NotificationKind"/>
    /// can say what it would print without reading the content file. That is what lets the
    /// built-in slate work on a first launch that has not fetched anything.
    /// </para>
    /// <para>
    /// <b>And the copy is a loc key rather than a string in content, which puts a hard line
    /// under what a content push can do.</b> It can switch a kind off, reorder the ladder,
    /// move the hours and change how often one repeats; it cannot write a new sentence,
    /// because a sentence has to be translated and translations ship in the build. That is
    /// exactly invariant 39c's split — "an icon is not content, so adding a utility is a
    /// build" — said about words: <b>a sentence is not content, so adding a notification is
    /// a build; which ones are sent, and when, is content.</b>
    /// </para>
    /// </summary>
    public static class NotificationKinds
    {
        /// <summary>Every kind that can be drawn, in declaration order. Excludes <c>None</c>.</summary>
        public static readonly NotificationKind[] All =
        {
            NotificationKind.HeartsFull,
            NotificationKind.TasksDaily,
            NotificationKind.TasksWeekly,
            NotificationKind.StreakRisk,
            NotificationKind.ChestWaiting,
            NotificationKind.SeasonRungs,
            NotificationKind.SeasonEnding,
            NotificationKind.BoardClimb,
            NotificationKind.EndlessCall,
            NotificationKind.GroveIdle,
        };

        /// <summary>
        /// The id a content file authors and analytics records. Permanent.
        ///
        /// Written out rather than derived from the enum member's name, because
        /// <c>ToString</c> on an enum is a reflection call whose answer would change the day
        /// somebody renamed a member — and a renamed member would silently re-point every
        /// loc key and orphan the analytics series (invariant 1).
        /// </summary>
        public static string Id(NotificationKind kind)
        {
            switch (kind)
            {
                case NotificationKind.HeartsFull:   return "hearts_full";
                case NotificationKind.TasksDaily:   return "tasks_daily";
                case NotificationKind.TasksWeekly:  return "tasks_weekly";
                case NotificationKind.StreakRisk:   return "streak_risk";
                case NotificationKind.ChestWaiting: return "chest_waiting";
                case NotificationKind.SeasonRungs:  return "season_rungs";
                case NotificationKind.SeasonEnding: return "season_ending";
                case NotificationKind.BoardClimb:   return "board_climb";
                case NotificationKind.EndlessCall:  return "endless_call";
                case NotificationKind.GroveIdle:    return "grove_idle";
                default:                            return string.Empty;
            }
        }

        /// <summary>
        /// The kind an id names, or <see cref="NotificationKind.None"/> for one this build has
        /// never heard of.
        ///
        /// Answering <c>None</c> rather than throwing is what lets a newer content pack reach
        /// an older build: a row naming a kind it does not know is dropped by name, exactly as
        /// <c>TaskTable</c> drops a task naming an unknown goal. The opposite — refusing the
        /// whole table — would mean one new notification takes every notification down.
        /// </summary>
        public static NotificationKind Parse(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotificationKind.None;
            foreach (var kind in All)
                if (string.Equals(Id(kind), id, StringComparison.Ordinal)) return kind;
            return NotificationKind.None;
        }

        /// <summary>The loc key for a kind's title line. Derived; never authored.</summary>
        public static string TitleKey(NotificationKind kind) => "notify." + Id(kind) + ".title";

        /// <summary>The loc key for a kind's body line.</summary>
        public static string BodyKey(NotificationKind kind) => "notify." + Id(kind) + ".body";
    }
}
