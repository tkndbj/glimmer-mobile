using System;
using System.Globalization;
using UnityEngine;

namespace GlimmerGrove.Notifications
{
    /// <summary>Where a "remind me" answered yes has to send the player.</summary>
    public enum AskRoute
    {
        /// <summary>Nothing to ask: granted, switched off by the player, mid-dialog, or no notifications here.</summary>
        None = 0,

        /// <summary>The OS will still show its own dialog: never asked, iOS provisional, or Android's second chance.</summary>
        System,

        /// <summary>The OS will never show its dialog again; only its settings page can change the answer.</summary>
        Settings,
    }

    /// <summary>
    /// How many times this install has offered our own "remind me" panel, and when it last did.
    ///
    /// <para>
    /// <b>Device-local, for <see cref="NotificationOptIn"/>'s reason</b>: what the OS has been asked
    /// is a fact about this handset's install, not about the player, so it never reaches the save.
    /// </para>
    /// </summary>
    public readonly struct AskLog
    {
        /// <summary>Panels shown so far, capped at <see cref="NotificationAsk.MaxShown"/>.</summary>
        public readonly int Shown;

        /// <summary>When the last one was shown, UTC seconds; nought when none has been.</summary>
        public readonly long LastUnix;

        public AskLog(int shown, long lastUnix)
        {
            Shown = Math.Max(0, Math.Min(shown, NotificationAsk.MaxShown));
            LastUnix = Math.Max(0L, lastUnix);
        }

        /// <summary>The log after one more panel, shown at <paramref name="nowUnix"/>.</summary>
        public AskLog After(long nowUnix) => new AskLog(Shown + 1, nowUnix);

        /// <summary>"shown:unix". Invariant culture, so a Turkish or Arabic device writes the same digits.</summary>
        public string Format()
            => Shown.ToString(CultureInfo.InvariantCulture) + ":" + LastUnix.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Reads <see cref="Format"/>'s output. Anything else - absent, truncated, hand-edited - is
        /// the empty log, which costs at most one panel more than was owed and never one fewer
        /// than the cap allows.
        /// </summary>
        public static AskLog Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return default;

            int colon = text.IndexOf(':');
            if (colon <= 0 || colon == text.Length - 1) return default;

            if (!int.TryParse(text.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out int shown))
                return default;
            if (!long.TryParse(text.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out long last))
                return default;

            return new AskLog(shown, last);
        }
    }

    /// <summary>
    /// When our own "remind me" panel may be put in front of the player, and what its yes does
    /// (invariant 50p).
    ///
    /// <para>
    /// <b>Why our panel and never the OS's straight away.</b> iOS shows its notification dialog once
    /// per install and Android 13+ twice; after that a request answers "denied" without drawing
    /// anything. A "no" to the OS's dialog is therefore spent for good, where a "not now" to ours
    /// costs nothing and can be asked again. So the OS's dialog is only ever raised by a player who
    /// has just said yes to ours, which is the primer every large mobile game puts in front of it.
    /// </para>
    /// <para>
    /// <b>Asked again, spaced and capped.</b> The first panel is due the first time it can be shown;
    /// each later one waits <see cref="GapDays"/> after the last, and after <see cref="MaxShown"/>
    /// the game stops asking for the life of the install. A player who keeps saying no is told
    /// four times in about a month and never again, which is persuasion and not nagging.
    /// </para>
    /// <para>
    /// <b>A player who turned reminders off in our own Settings row is never asked</b>: that switch
    /// is an answer, and a panel arguing with it is the nagging this rule exists to avoid.
    /// </para>
    /// <para>
    /// Pure, and Domain, so every branch runs in the offline suite (<c>NotificationTests</c>). The
    /// device store is the two members at the foot.
    /// </para>
    /// </summary>
    public static class NotificationAsk
    {
        /// <summary>Panels this install may ever show. The first plus one per <see cref="GapDays"/> entry.</summary>
        public const int MaxShown = 4;

        /// <summary>
        /// Days to wait after the n-th panel before the next. Growing, because a player who has said
        /// "not now" twice is told less often rather than more.
        /// </summary>
        static readonly int[] GapDays = { 3, 7, 14 };

        const long Day = 86400L;

        /// <summary>
        /// The shortest streak that earns the panel when today's run extends it. A first day is
        /// not yet something a player would mind losing; a second is a streak they have kept.
        /// </summary>
        public const int StreakWorthAsking = 2;

        /// <summary>Whether a streak just extended to <paramref name="length"/> days is a moment to ask.</summary>
        public static bool StreakMoment(int length) => length >= StreakWorthAsking;

        /// <summary>The wait after <paramref name="shown"/> panels, in days. Public for the fixture.</summary>
        public static int GapAfter(int shown)
            => shown <= 0 ? 0 : GapDays[Math.Min(shown, GapDays.Length) - 1];

        /// <summary>
        /// Whether the OS would draw its dialog if asked now. The one definition both
        /// <see cref="NotificationOptIn.CanAsk"/> and <see cref="Route"/> read.
        /// </summary>
        /// <param name="osCanPromptAgain">Android's "show a rationale" answer: true after exactly one refusal.</param>
        public static bool OsWillPrompt(NotificationPermission permission, bool osCanPromptAgain)
            => permission == NotificationPermission.Unasked
            || permission == NotificationPermission.Provisional
            || (permission == NotificationPermission.Denied && osCanPromptAgain);

        /// <summary>What a yes would have to do, ignoring the cadence.</summary>
        public static AskRoute Route(NotificationPermission permission, bool osCanPromptAgain, bool wanted)
        {
            if (!wanted) return AskRoute.None;
            if (OsWillPrompt(permission, osCanPromptAgain)) return AskRoute.System;
            return permission == NotificationPermission.Denied ? AskRoute.Settings : AskRoute.None;
        }

        /// <summary>
        /// Whether the cadence allows a panel at <paramref name="nowUnix"/>.
        ///
        /// A stamp more than a day in the future came from a clock that has since been corrected;
        /// it is treated as old rather than trusted, or one wrong clock would silence the feature
        /// until the calendar caught up with it.
        /// </summary>
        public static bool Due(AskLog log, long nowUnix)
        {
            if (log.Shown >= MaxShown) return false;
            if (log.Shown == 0) return true;
            if (log.LastUnix > nowUnix + Day) return true;
            return nowUnix - log.LastUnix >= GapAfter(log.Shown) * Day;
        }

        /// <summary>The whole rule: the route, when the cadence allows one; otherwise none.</summary>
        public static AskRoute Decide(NotificationPermission permission, bool osCanPromptAgain, bool wanted,
                                      AskLog log, long nowUnix)
        {
            var route = Route(permission, osCanPromptAgain, wanted);
            return route != AskRoute.None && Due(log, nowUnix) ? route : AskRoute.None;
        }

        /// <summary>Analytics spelling. Permanent once a dashboard reads it.</summary>
        public static string Id(AskRoute route)
        {
            switch (route)
            {
                case AskRoute.System: return "system";
                case AskRoute.Settings: return "settings";
                case AskRoute.None: return "none";
                default: throw new ArgumentOutOfRangeException(nameof(route), route, null);
            }
        }

        // ------------------------------------------------------------- device store
        /// <summary>
        /// Where the log is written. Renaming it re-offers every panel to every install, so the
        /// rename has to be a decision - <see cref="NotificationOptIn"/>'s note on its own key.
        /// </summary>
        const string Key = "glimmer.notify.asks";

        /// <summary>What this install has shown so far.</summary>
        public static AskLog Log => AskLog.Parse(PlayerPrefs.GetString(Key, ""));

        /// <summary>
        /// Records a panel. Called the moment it opens, not when it is answered: a process killed
        /// with the panel up must not show it again tomorrow, and the promise is a count of
        /// panels seen.
        /// </summary>
        public static void MarkShown(long nowUnix)
        {
            PlayerPrefs.SetString(Key, Log.After(nowUnix).Format());
            PlayerPrefs.Save();
        }
    }
}
