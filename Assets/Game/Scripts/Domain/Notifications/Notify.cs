using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Events;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Social;
using UnityEngine;

namespace GlimmerGrove.Notifications
{
    /// <summary>
    /// The one thing the rest of the game talks to: take a snapshot, build a plan, hand it to
    /// the platform.
    ///
    /// <para>
    /// <b>Armed when the app is backgrounded and never on a timer</b>, because being
    /// backgrounded is the last moment a mobile app is reliably told anything — Android may
    /// kill the process afterwards without another callback, which is why <c>Boot.Pump</c>
    /// already flushes the save there. It is the correct moment for a second reason: the plan
    /// has to be built from the state the player is *leaving behind*, and there is no later
    /// instant at which that is known.
    /// </para>
    /// <para>
    /// <b>Nothing about the schedule is stored, and that is what makes it safe to rebuild.</b>
    /// There is no record of what was armed last time, no cursor, no "already sent" set — the
    /// plan is a pure function of the save and the clock, so re-arming is idempotent by
    /// construction and a device that is restored from a backup, cloned to a new phone or
    /// rolled back a version simply computes the right answer. That is invariant 14's argument
    /// arriving somewhere nobody expected it: being derived is what keeps this out of the save
    /// file, out of the merge and out of <c>firestore.rules</c> entirely.
    /// </para>
    /// <para>
    /// <b>And there is no server anywhere in it.</b> At ten million players a nightly push
    /// fan-out is tens of millions of document reads a day, for ever, growing with the player
    /// count; this is nought, for ever, because every sentence it sends is derivable on the
    /// handset that sends it. Invariant 14 chose derived rewards for the same reason and got
    /// the same three things free: no schema version, no merge rule, no claim.
    /// </para>
    /// </summary>
    public static class Notify
    {
        static INotificationScheduler _scheduler = new NullNotificationScheduler();
        static bool _armed;
        static bool _rearming;

        /// <summary>
        /// Raised, once, when the game was opened by tapping a notification.
        ///
        /// <para>
        /// A seam with no subscriber today, which is deliberate rather than unfinished:
        /// routing a tap into a screen has to happen after the boot path has built one, and
        /// the flow that owns screens is Presentation. What it costs to leave open is one
        /// event; what it would cost to add later is threading the tapped kind through a boot
        /// sequence that had already thrown it away.
        /// </para>
        /// </summary>
        public static event Action<NotificationKind> Opened;

        /// <summary>Whether the platform can deliver anything. False in the Editor.</summary>
        public static bool Supported => _scheduler.Supported;

        /// <summary>
        /// Installs the platform binding. Called by the boot path once; nothing else needs it
        /// outside tests.
        /// </summary>
        public static void Bind(INotificationScheduler scheduler)
        {
            _scheduler = scheduler ?? new NullNotificationScheduler();

            if (!_scheduler.Supported)
            {
                NotificationOptIn.SetPermission(NotificationPermission.Unsupported);
                return;
            }

            // The switch and the OS answer are two facts (see NotificationOptIn), and the
            // player may have changed either while the game was closed — turning notifications
            // off in the OS settings is the commonest way a granted permission becomes a
            // denied one. Re-arming on the next background is what makes that stick.
            NotificationOptIn.Changed -= Rearm;
            NotificationOptIn.Changed += Rearm;

            _scheduler.Opened -= Tapped;
            _scheduler.Opened += Tapped;

            // Initialising reads the OS's current answer, which moves `Permission` off
            // `Unsupported` and therefore raises `Changed` — so binding on a device that is
            // already granted writes a schedule then and there, as well as on the way out.
            // That is wanted rather than incidental: a process the OS kills without ever
            // sending a pause callback would otherwise be left holding last session's plan.
            _scheduler.Initialise();
            _scheduler.ClearDelivered();
        }

        /// <summary>
        /// Asks the OS, at the moment the player has just been shown why it is worth saying yes.
        ///
        /// <para>
        /// <b>Deliberately not called on the boot path.</b> A permission dialog on the splash
        /// screen is the highest-refusal moment there is — the player has not seen the game,
        /// has nothing to be reminded about, and cannot be asked again by us afterwards,
        /// because both platforms show the system dialog once per install. Asked instead the
        /// first time the player is holding something that will be waiting for them, which is
        /// the only honest version of the question.
        /// </para>
        /// </summary>
        public static void Ask()
        {
            if (!_scheduler.Supported || !NotificationOptIn.CanAsk) return;

            Telemetry.Track("notification_permission_asked");
            _scheduler.RequestPermission();
        }

        /// <summary>
        /// This launch came from a tap. Recorded, then passed on.
        ///
        /// The analytics ordinal is the kind's permanent string rather than the enum's number,
        /// so a kind appended to the enum cannot renumber a series somebody is reading.
        /// </summary>
        static void Tapped(NotificationKind kind)
        {
            if (kind == NotificationKind.None) return;

            Telemetry.Track("notification_opened", "kind", NotificationKinds.Id(kind));
            try { Opened?.Invoke(kind); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// Hands the player to the OS's own settings, which is the only control that can undo
        /// a refusal. See the seam for why there is no second in-game "allow".
        /// </summary>
        public static void OpenSettings() => _scheduler.OpenSettings();

        /// <summary>The player has opened the game; the shade and the badge are stale.</summary>
        public static void Resumed()
        {
            if (!_scheduler.Supported) return;
            _scheduler.ClearDelivered();
        }

        /// <summary>
        /// Builds the plan from what the save says right now and hands it to the platform.
        ///
        /// <para>
        /// Wrapped whole, because this runs on the way out of the app and behind
        /// <c>SaveService.Flush</c>: a reminder nobody will read is never worth an exception
        /// that stops the save from reaching disk. The log is loud, because the alternative to
        /// noticing here is noticing when nobody comes back.
        /// </para>
        /// </summary>
        public static void Rearm()
        {
            if (!_scheduler.Supported || _rearming) return;

            // Re-entrancy is real rather than theoretical: `Refresh` below can discover that
            // the OS answer has changed, which raises `NotificationOptIn.Changed`, which this
            // class subscribes `Rearm` to. Without the guard, a player who revoked permission
            // in the system settings would re-arm twice on their next background — harmless
            // today, and the kind of loop that stops being harmless the moment somebody adds a
            // second listener.
            _rearming = true;

            try
            {
                _scheduler.Refresh();

                if (!NotificationOptIn.Allowed)
                {
                    // Not merely "send nothing next time" — the schedule already on the device
                    // has to go, or a player who switches reminders off keeps getting the week
                    // that was armed before they did, which reads as the switch being broken.
                    _scheduler.Arm(Array.Empty<PlannedNotification>());
                    _armed = false;
                    return;
                }

                var plan = NotificationPlan.Build(Snapshot(), NotificationTable.Active);
                _scheduler.Arm(plan);

                // Once per session rather than per arm: backgrounding happens many times a
                // sitting and this is a fact about the schedule, not an event in it.
                if (!_armed)
                {
                    Telemetry.Track("notifications_armed", "count", plan.Count);
                    _armed = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                _rearming = false;
            }
        }

        /// <summary>
        /// What one planned notification actually says, resolved through the string table.
        ///
        /// <para>
        /// Resolved here, at schedule time, because the OS draws the text with the app not
        /// running and cannot call back into a string table. That is what freezes the copy —
        /// and what makes the rule on <see cref="NotificationState"/> load-bearing: any number
        /// inside one of these strings must be a function of the fire instant rather than of
        /// this moment, so every string this game ships is argument-free.
        /// </para>
        /// <para>
        /// It also means a player who changes language keeps the old language's reminders
        /// until the next time they background the game, which re-arms. Accepted: the
        /// alternative is storing the plan and rebuilding it on a language event, which is
        /// state this feature deliberately does not have.
        /// </para>
        /// </summary>
        public static (string title, string body) Wording(PlannedNotification planned)
            => (Loc.Get(planned.TitleKey), Loc.Get(planned.BodyKey));

        // ------------------------------------------------------------- snapshot
        /// <summary>
        /// Everything the planner is allowed to know, read once.
        ///
        /// Read in one place rather than by the planner itself, which is what keeps
        /// <see cref="NotificationPlan"/> a pure function of two values and therefore runnable
        /// in the offline suite with no save loaded and no clock installed.
        /// </summary>
        public static NotificationState Snapshot()
        {
            long now = GameClock.NowUnix();
            var hearts = Wallet.Hearts;
            var season = GroveEvents.Live;

            return new NotificationState(
                nowUnix: now,
                utcOffsetSeconds: (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalSeconds,
                // Nought when the bar is already at the cap, which the predicate treats as a
                // different fact rather than as "full a long time ago" — see NotificationState.
                heartsFullUnix: hearts.IsRefilled ? 0L : hearts.NextRefillUnix,
                streakChestWaiting: DailyStreak.AnyPending,
                streakLastPlayedDay: DailyStreak.LastPlayedDay,
                streakDays: DailyStreak.Days,
                shieldFromDay: DailyStreak.ShieldFromDay,
                shieldDays: DailyStreak.ShieldDays,
                seasonOpen: season != null && season.IsLiveAt(now),
                seasonEndDay: season == null ? 0 : DailyRules.DayKeyFor(season.EndUnix - 1),
                seasonRungsReady: GroveEvents.Waiting > 0,
                // The published half rather than the switch alone: a keeper who has opted in
                // but whose card has never been built is not on a board, so telling them to
                // climb one is an invitation to a screen with nothing of theirs on it.
                onBoards: GameSettings.BoardOptIn && GroveBoard.Mine.IsValid,
                endlessOpen: EndlessIsOpen());
        }

        /// <summary>
        /// Whether any lane on the Infinite track is open to this player.
        ///
        /// Asked of the catalog rather than of a chapter id, because a chapter id written into
        /// Domain is a content fact in code (invariant 4) — and the one lane that exists today
        /// is exactly the kind of thing a drop adds a second of.
        /// </summary>
        static bool EndlessIsOpen()
        {
            var index = GameContent.Index;
            if (index == null) return false;

            foreach (var mode in index.Modes)
            {
                foreach (var chapter in index.ChaptersIn(mode, GameTrack.Infinite))
                    if (LevelUnlock.GateFor(index, chapter.Id).IsOpen) return true;
            }

            return false;
        }
    }
}
