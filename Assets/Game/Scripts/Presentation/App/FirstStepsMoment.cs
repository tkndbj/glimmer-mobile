using System;
using GlimmerGrove.Analytics;
using GlimmerGrove.Async;
using GlimmerGrove.Notifications;
using GlimmerGrove.Privacy;
using GlimmerGrove.Progression;
using GlimmerGrove.Release;
using GlimmerGrove.Tasks;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The one place a first step's ceremony is put in front of a player (<see cref="FirstStepTable"/>),
    /// and the one place that keeps the hub quiet after it.
    ///
    /// <para>
    /// <b>Two doors.</b> The victory panel's exits ask <see cref="AfterVictory"/> before they go
    /// anywhere, so a step is celebrated on the run that earned it, between the verdict and the
    /// next thing the player asked for - and the panel hands over the tap it was given, which the
    /// ceremony resumes when it ends however it ends (<c>AccountOverlay.Then</c>'s contract). And a
    /// calm hub asks <see cref="Tick"/> for anything still owed: a turret whose ceremony a killed
    /// process never showed, a chest step that had no account to claim under at the time. Neither
    /// door grants anything - a turret is handed over at the clear and a chest when it is opened
    /// (<see cref="FirstStepsLedger"/>) - so a ceremony lost to a crash is a report missed, never
    /// a reward.
    /// </para>
    /// <para>
    /// <b>One celebration, then quiet.</b> The owner's rule for a new player: progress felt, never a
    /// flood of panels. So a step's ceremony <see cref="Hush">hushes</see> the moments that would
    /// otherwise be waiting on the hub behind it - the deals popup, a personal offer, the reminder
    /// ask, a gift - until the player starts another run or a new session begins. The victory
    /// panel hushes the same way when it raises the account panel at a chapter's end. The consent
    /// questions are deliberately <em>not</em> hushed: they are owed before the ads that pay for
    /// the game may run, and a new player has met them on the hub before their first level.
    /// </para>
    /// </summary>
    public static class FirstStepsMoment
    {
        /// <summary>How long a calm hub must stand before an owed ceremony is laid over it, in seconds.</summary>
        const float Beat = .9f;

        /// <summary>Back after this long in the background is a new session, as the deals popup counts one.</summary>
        const float NewSessionSeconds = 30f * 60f;

        static bool _hushed;
        static View _hushedOver;
        static float _calmSince = -1f;
        static float _pausedAt = -1f;

        /// <summary>
        /// Whether a ceremony or the chapter's account ask has just had the player's attention.
        /// Asked by every moment that would lay a popup over the hub (<c>GiftMoment</c>,
        /// <c>DealMoment</c>, <c>OfferMoment</c>, <c>ReminderMoment</c>).
        /// The quiet is lifted by <see cref="Tick"/>, never by this read: every one of those moments
        /// stops at "not on the hub" before asking, so a read could never see the run that ends it.
        /// </summary>
        public static bool Hushed => _hushed;

        /// <summary>
        /// A new run is the player moving on, and the end of the quiet: the screen that was current
        /// when the hush began was the run that earned it, so a different run screen is a run begun
        /// since. Watched every frame, because no popup asks while a run is up.
        /// </summary>
        static void LiftOnNewRun(View current)
        {
            if (_hushed && current is RunScreen && !ReferenceEquals(current, _hushedOver)) Lift();
        }

        /// <summary>Keeps the hub quiet until the next run or the next session. Idempotent.</summary>
        public static void Hush()
        {
            _hushed = true;
            _hushedOver = Flow.Current;
        }

        static void Lift()
        {
            _hushed = false;
            _hushedOver = null;
        }

        public static void Paused() => _pausedAt = Time.realtimeSinceStartup;

        public static void Resumed()
        {
            if (_pausedAt >= 0f && Time.realtimeSinceStartup - _pausedAt >= NewSessionSeconds) Lift();
            _pausedAt = -1f;
        }

        // ------------------------------------------------------------- the victory door
        /// <summary>
        /// Raises the ceremony this run's level earned, if one is owed, and hands it
        /// <paramref name="onward"/> - what the player tapped on the victory panel - to resume
        /// when it ends. Answers whether it did: false means nothing was raised and the caller
        /// carries on itself, which is the answer for every level but the handful a step names.
        /// </summary>
        public static bool AfterVictory(RunOutcome run, Action onward)
        {
            if (!run.Level.IsValid) return false;
            if (ReleaseGate.IsShut) return false;

            var step = FirstStepsLedger.Table.At(run.Level);
            if (step == null) return false;

            switch (step.Kind)
            {
                case FirstStepKind.Turret:
                    return FirstStepsLedger.TurretWaiting(step) && ShowTurret(step, onward);

                case FirstStepKind.Welcome:
                    // A moment, so only on the run that first cleared the level: a replay is not
                    // news, and the panel stores nothing that could say it was shown.
                    return run.FirstClear && ShowWelcome(step, onward);

                case FirstStepKind.Chests:
                    return FirstStepsLedger.ChestsWaiting(step) > 0 && FirstStepsLedger.CanClaimChests
                        && ShowChests(step, onward);

                default:
                    throw new ArgumentOutOfRangeException(nameof(step.Kind), step.Kind, null);
            }
        }

        // ------------------------------------------------------------- the hub door
        /// <summary>
        /// One frame. Driven from <c>Boot.Pump</c> after the consent step and before the gift, so a
        /// frame that raised a ceremony is covered and nothing else is offered on it.
        /// </summary>
        public static void Tick()
        {
            LiftOnNewRun(Flow.Current);

            if (!(Flow.Current is HomeScreen) || !Calm())
            {
                _calmSince = -1f;
                return;
            }

            float now = Time.unscaledTime;
            if (_calmSince < 0f) _calmSince = now;
            if (now - _calmSince < Beat) return;
            _calmSince = -1f;

            var step = FirstStepsLedger.Owed;
            if (step == null) return;

            if (FirstStepsLedger.TurretWaiting(step)) ShowTurret(step, null);
            else if (FirstStepsLedger.ChestsWaiting(step) > 0 && FirstStepsLedger.CanClaimChests) ShowChests(step, null);
        }

        /// <summary>Nothing over the hub, nothing about to be, and nothing else asking.</summary>
        static bool Calm()
            => !Flow.Busy
            && !Flow.Covered
            && !ReleaseGate.IsShut
            && Application.isFocused
            && LaunchCalm.IsSettled
            && !AdPrivacy.Owed
            && !AdPrivacy.IsAsking;

        // ------------------------------------------------------------- raising
        static bool ShowTurret(FirstStep step, Action onward)
        {
            if (step.Ward == null) return false;

            // Marked before anything is raised, so a panel that fails to build - or a process
            // killed with it up - cannot raise it a second time. The turret was handed over at the
            // clear; this is its report.
            FirstStepsLedger.MarkShown(step);
            Hush();

            Telemetry.Track("first_step_shown", "level", step.Level.Value, "kind", FirstStepKinds.Id(step.Kind));

            var view = Flow.Modal<WardGiftOverlay>(v =>
            {
                v.Model = step.Ward;
                v.Colour = step.Colour;
                v.Then = onward;
            });
            return view != null;
        }

        static bool ShowChests(FirstStep step, Action onward)
        {
            Hush();
            Telemetry.Track("first_step_shown", "level", step.Level.Value, "kind", FirstStepKinds.Id(step.Kind),
                            "chests", FirstStepsLedger.ChestsWaiting(step));

            var view = Flow.Modal<ChestGiftOverlay>(v =>
            {
                v.Step = step;
                v.Then = onward;
            });
            return view != null;
        }

        /// <summary>
        /// The welcome bonus's progress: the first quest still open that today's play has moved,
        /// with its newest day stamped. Nothing when the bonus is off, finished, or nothing has
        /// been counted - a panel showing an empty row is not progress.
        /// </summary>
        static bool ShowWelcome(FirstStep step, Action onward)
        {
            if (!WelcomeLedger.Offered) return false;

            var quest = WelcomeStep.QuestToShow();
            if (quest == null) return false;

            // The reminder ask rides on this panel (invariant 50p): one of the four an install may
            // see, marked when it opens, so the cadence spaces the next three, seven and fourteen
            // days away - no second ask today, however many chests follow.
            var route = AskRoute.None;
            if (Notify.Supported)
            {
                long unix = GameClock.NowUnix();
                var log = NotificationAsk.Log;
                route = NotificationAsk.Decide(NotificationOptIn.Permission, NotificationOptIn.CanPromptAgain,
                                               NotificationOptIn.Wanted, log, unix);
                if (route != AskRoute.None)
                {
                    NotificationAsk.MarkShown(unix);
                    Telemetry.Track("notification_ask_shown", "route", NotificationAsk.Id(route),
                                    "trigger", WelcomeStep.AskTrigger,
                                    "ordinal", log.Shown + 1,
                                    "permission", NotificationOptIn.Id(NotificationOptIn.Permission));
                }
            }

            Hush();
            Telemetry.Track("first_step_shown", "level", step.Level.Value, "kind", FirstStepKinds.Id(step.Kind),
                            "quest", quest.Id, "days", WelcomeLedger.DaysDone(quest), "ask", NotificationAsk.Id(route));

            var view = Flow.Modal<WelcomeStepOverlay>(v =>
            {
                v.Quest = quest;
                v.Route = route;
                v.Then = onward;
            });
            return view != null;
        }
    }

    /// <summary>The welcome step's two rules, apart from the panel that draws them.</summary>
    public static class WelcomeStep
    {
        /// <summary>The reminder ask's analytics trigger for this panel. Permanent once a dashboard reads it.</summary>
        public const string AskTrigger = "first_steps";

        /// <summary>
        /// The quest the panel shows: the first in the table not yet taken whose verb has been
        /// counted on at least one day. Null when there is none.
        /// </summary>
        public static WelcomeQuest QuestToShow()
        {
            var quests = ProgressionRules.Table.Welcome.Quests;
            for (int i = 0; i < quests.Count; i++)
            {
                var quest = quests[i];
                if (WelcomeLedger.IsClaimed(quest)) continue;
                if (WelcomeLedger.DaysDone(quest) >= 1) return quest;
            }
            return null;
        }
    }
}
