using GlimmerGrove.Analytics;
using GlimmerGrove.Async;
using GlimmerGrove.Notifications;
using GlimmerGrove.Privacy;
using GlimmerGrove.Release;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The one place our "remind me" panel is put in front of a player (invariant 50p). Nothing
    /// else may raise <see cref="ReminderAskOverlay"/>, and nothing but a yes on it - or the
    /// Settings row - may ask the OS.
    ///
    /// <para>
    /// <b>The moment is a chest just collected.</b> The player has opened one and there is
    /// demonstrably another tomorrow, which is the whole of what a reminder promises.
    /// <see cref="ChestOverlay"/> notes it; the panel follows once the reward has flown and the
    /// screen underneath stands calm, so it never interrupts the payoff it is built on.
    /// </para>
    /// <para>
    /// <b>A poll, for <see cref="DealMoment"/>'s reason</b>, driven from <c>Boot.Pump</c> after
    /// it: every way back to a calm screen is caught without anybody calling in. Calm is
    /// DealMoment's test - nothing over the screen, no update wall, focused, the launch settled,
    /// the consent questions behind the player - plus not in a run, a challenge or the tutorial.
    /// A frame that raised a deals popup is covered, so the two never stack.
    /// </para>
    /// <para>
    /// <b>The note goes stale.</b> A chest collected and then a minute spent elsewhere is not the
    /// moment any more, so the note lapses after <see cref="FreshSeconds"/> and the next chest
    /// tries again. <b>Whether to ask at all is <see cref="NotificationAsk"/>'s</b> - the route
    /// and the spaced, capped cadence - and this class only says where and when.
    /// </para>
    /// </summary>
    public static class ReminderMoment
    {
        /// <summary>How long the screen must stand calm before the panel is laid over it, in seconds.</summary>
        const float Beat = .8f;

        /// <summary>How long a collected chest stays the moment, in seconds of real time.</summary>
        const float FreshSeconds = 25f;

        static float _notedAt = -1f;
        static float _calmSince = -1f;

        /// <summary>
        /// A chest has just been collected. Cheap and idempotent; asks nothing by itself.
        /// </summary>
        public static void NoteChest()
        {
            if (!Notify.Supported) return;
            _notedAt = Time.realtimeSinceStartup;
        }

        /// <summary>One frame.</summary>
        public static void Tick()
        {
            if (_notedAt < 0f) return;

            if (Time.realtimeSinceStartup - _notedAt > FreshSeconds)
            {
                Forget();
                return;
            }

            if (!Calm())
            {
                _calmSince = -1f;
                return;
            }

            float now = Time.unscaledTime;
            if (_calmSince < 0f) _calmSince = now;
            if (now - _calmSince < Beat) return;

            long unix = GameClock.NowUnix();
            var log = NotificationAsk.Log;
            var route = NotificationAsk.Decide(NotificationOptIn.Permission, NotificationOptIn.CanPromptAgain,
                                               NotificationOptIn.Wanted, log, unix);

            // Asked once per note: a chest that finds nothing to ask - granted, switched off,
            // not due - is spent, and the next chest asks afresh.
            Forget();
            if (route == AskRoute.None) return;

            Show(route, log, unix);
        }

        /// <summary>The app went to the background: whatever was noted is no longer the moment.</summary>
        public static void Paused() => Forget();

        static void Forget()
        {
            _notedAt = -1f;
            _calmSince = -1f;
        }

        /// <summary>DealMoment's calm, and somewhere a panel about tomorrow belongs.</summary>
        static bool Calm()
            => Flow.Current != null
            && !(Flow.Current is RunScreen)
            && !(Flow.Current is ChallengeScreen)
            && !(Flow.Current is TutorialScreen)
            && !(Flow.Current is SplashScreen)
            && !Flow.Busy
            && !Flow.Covered
            && !ReleaseGate.IsShut
            && Application.isFocused
            && LaunchCalm.IsSettled
            && !AdPrivacy.Owed
            && !AdPrivacy.IsAsking;

        static void Show(AskRoute route, AskLog log, long unix)
        {
            // Marked before anything is raised, so a panel that fails to build - or a process
            // killed with it up - still counts against the cap rather than coming back tomorrow.
            NotificationAsk.MarkShown(unix);

            Telemetry.Track("notification_ask_shown", "route", NotificationAsk.Id(route),
                            "ordinal", log.Shown + 1,
                            "permission", NotificationOptIn.Id(NotificationOptIn.Permission));

            Flow.Modal<ReminderAskOverlay>(v => v.Route = route);
        }
    }
}
