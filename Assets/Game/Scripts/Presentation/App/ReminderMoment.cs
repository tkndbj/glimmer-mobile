using GlimmerGrove.Analytics;
using GlimmerGrove.Async;
using GlimmerGrove.Daily;
using GlimmerGrove.Notifications;
using GlimmerGrove.Privacy;
using GlimmerGrove.Release;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>What earned the "remind me" panel. It picks the sentence and the analytics tag.</summary>
    public enum ReminderTrigger
    {
        /// <summary>A chest was just collected: there is demonstrably another tomorrow.</summary>
        Chest,

        /// <summary>Today's run extended a streak worth keeping: tomorrow is what it needs.</summary>
        Streak,
    }

    /// <summary>
    /// The one place our "remind me" panel is put in front of a player (invariant 50p). Nothing
    /// else may raise <see cref="ReminderAskOverlay"/>, and nothing but a yes on it - or the
    /// Settings row - may ask the OS.
    ///
    /// <para>
    /// <b>Two moments, both a promise about tomorrow.</b> A chest just collected (noted by
    /// <see cref="ChestOverlay"/>), and a streak just extended to
    /// <see cref="NotificationAsk.StreakWorthAsking"/> days or more (heard from
    /// <see cref="DailyStreak.Advanced"/>). The panel follows once the screen underneath stands
    /// calm, so it never interrupts the payoff it is built on. Both draw on one cadence
    /// (<see cref="NotificationAsk"/>): a second moment is a better chance at the same four
    /// panels, never a fifth.
    /// </para>
    /// <para>
    /// <b>A chest goes stale; a streak waits for the menus.</b> A chest collected and then a
    /// minute spent elsewhere is not the moment any more, so its note lapses after
    /// <see cref="ChestFreshSeconds"/>. A streak is extended at the end of a run, and the player
    /// may go straight into another; its note waits for the first calm screen outside a run this
    /// session, which is where the sentence about tomorrow still reads true. When both are held
    /// the streak is asked, its sentence being the more specific one. Both lapse when the app is
    /// backgrounded.
    /// </para>
    /// <para>
    /// <b>A poll, for <see cref="DealMoment"/>'s reason</b>, driven from <c>Boot.Pump</c> after
    /// it. Calm is DealMoment's test - nothing over the screen, no update wall, focused, the
    /// launch settled, the consent questions behind the player - plus not in a run, a challenge
    /// or the tutorial. A frame that raised a deals popup is covered, so the two never stack.
    /// </para>
    /// </summary>
    public static class ReminderMoment
    {
        /// <summary>How long the screen must stand calm before the panel is laid over it, in seconds.</summary>
        const float Beat = .8f;

        /// <summary>How long a collected chest stays the moment, in seconds of real time.</summary>
        const float ChestFreshSeconds = 25f;

        static bool _hooked, _streakPending;
        static float _chestAt = -1f;
        static float _calmSince = -1f;

        /// <summary>
        /// Listens for the streak. Called by <c>Boot</c> once; idempotent. <see cref="DailyStreak.Advanced"/>
        /// is raised by a run that extended the streak, whichever mode or screen ended it.
        /// </summary>
        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;

            DailyStreak.Advanced += (length, _) =>
            {
                if (Notify.Supported && NotificationAsk.StreakMoment(length)) _streakPending = true;
            };
        }

        /// <summary>A chest has just been collected. Cheap and idempotent; asks nothing by itself.</summary>
        public static void NoteChest()
        {
            if (!Notify.Supported) return;
            _chestAt = Time.realtimeSinceStartup;
        }

        /// <summary>One frame.</summary>
        public static void Tick()
        {
            if (_chestAt >= 0f && Time.realtimeSinceStartup - _chestAt > ChestFreshSeconds)
                _chestAt = -1f;

            if (!_streakPending && _chestAt < 0f)
            {
                _calmSince = -1f;
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

            var trigger = _streakPending ? ReminderTrigger.Streak : ReminderTrigger.Chest;

            long unix = GameClock.NowUnix();
            var log = NotificationAsk.Log;
            var route = NotificationAsk.Decide(NotificationOptIn.Permission, NotificationOptIn.CanPromptAgain,
                                               NotificationOptIn.Wanted, log, unix);

            // Asked once per moment: one that finds nothing to ask - granted, switched off, not
            // due - is spent, and the next chest or streak day asks afresh.
            Forget();
            if (route == AskRoute.None) return;

            Show(trigger, route, log, unix);
        }

        /// <summary>The app went to the background: whatever was noted is no longer the moment.</summary>
        public static void Paused() => Forget();

        static void Forget()
        {
            _chestAt = -1f;
            _streakPending = false;
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

        /// <summary>
        /// Analytics spelling. Permanent once a dashboard reads it. No default answer (44e): a
        /// trigger added without a spelling throws in the first play-through, not in a chart.
        /// </summary>
        public static string Id(ReminderTrigger trigger)
        {
            switch (trigger)
            {
                case ReminderTrigger.Chest: return "chest";
                case ReminderTrigger.Streak: return "streak";
                default: throw new System.ArgumentOutOfRangeException(nameof(trigger), trigger, null);
            }
        }

        static void Show(ReminderTrigger trigger, AskRoute route, AskLog log, long unix)
        {
            // Marked before anything is raised, so a panel that fails to build - or a process
            // killed with it up - still counts against the cap rather than coming back tomorrow.
            NotificationAsk.MarkShown(unix);

            Telemetry.Track("notification_ask_shown", "route", NotificationAsk.Id(route),
                            "trigger", Id(trigger),
                            "ordinal", log.Shown + 1,
                            "permission", NotificationOptIn.Id(NotificationOptIn.Permission));

            Flow.Modal<ReminderAskOverlay>(v =>
            {
                v.Route = route;
                v.Trigger = trigger;
            });
        }
    }
}
