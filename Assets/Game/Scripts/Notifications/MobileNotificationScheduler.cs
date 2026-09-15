using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if GLIMMER_NOTIFICATIONS && UNITY_ANDROID
using Unity.Notifications.Android;
#endif
#if GLIMMER_NOTIFICATIONS && UNITY_IOS
using Unity.Notifications.iOS;
#endif

namespace GlimmerGrove.Notifications
{
    /// <summary>
    /// The half of the notification system that knows a phone exists.
    ///
    /// <para>
    /// Everything that can be reasoned about — which reminders, in what order, at what hours,
    /// how often, and whether a sentence will still be true when it is read — is in Domain and
    /// runs in the offline suite. What is here is the handful of calls that cannot be: ask the
    /// OS, write a schedule, clear the shade, find out whether this launch came from a tap.
    /// </para>
    /// <para>
    /// Kept in an assembly of its own behind a <c>versionDefines</c> flag, which is
    /// <c>GlimmerGrove.Privacy</c>'s shape and for a reason this project has already paid for:
    /// a <c>versionDefines</c> flag is per <em>package</em> and is therefore correct on every
    /// build target, where a Player Settings define is per target and would compile an Android
    /// build with the whole feature silently absent.
    /// </para>
    /// <para>
    /// <b>The two platform APIs are used directly rather than the unified <c>NotificationCenter</c>,
    /// and the reason is the icon.</b> The unified API is one code path for channels,
    /// authorisation and both schedulers, and it was written against first — but in 2.4.3 its
    /// <c>Notification</c> struct carries no icon field and <c>NotificationCenterArgs</c> has no
    /// <c>AndroidSmallIcon</c> (that exists only on the package's <c>master</c>, in no release).
    /// So the one thing this binding exists to get right cannot be expressed through it at all.
    /// Going direct also buys the accent colour, the channel's importance and a real auto-cancel,
    /// none of which the unified path exposes. <b>Pin the package version before trusting a
    /// docs page:</b> the published documentation describes master.
    /// </para>
    /// </summary>
    public sealed class MobileNotificationScheduler : INotificationScheduler
    {
        /// <summary>
        /// The drawable the status bar draws, by name.
        ///
        /// <para>
        /// <b>A contract with <c>Tools/make_notification_icons.py</c>, and the one string here
        /// whose failure is silent.</b> The package resolves it through
        /// <c>Resources.getIdentifier(name, "drawable", packageName)</c> and, finding nothing,
        /// falls back to <c>getApplicationInfo().icon</c> — the launcher icon, whose alpha
        /// channel is a solid square, which Android 5.0 and up then renders as a white blob.
        /// So a typo here is not an error, a warning or a missing icon: it is a working
        /// notification with a featureless white circle on it, on every Android device, and
        /// nothing anywhere says so.
        /// </para>
        /// </summary>
        public const string SmallIcon = "ic_stat_glimmer";

        /// <summary>
        /// The Android channel every reminder goes down.
        ///
        /// <para>
        /// One channel, not one per kind. Channels are the player's own controls — they can
        /// silence one and keep another — and a channel per kind would put ten switches in the
        /// OS settings for a game with one thing to say. The id is permanent: Android
        /// remembers a channel's settings for the life of the install and ignores a
        /// re-registration that tries to raise its importance, so renaming it makes a
        /// <em>second</em> channel and leaves the player's old choice governing nothing.
        /// </para>
        /// </summary>
        public const string Channel = "glimmer_reminders";

        /// <summary>
        /// The tint Android draws behind the white silhouette, and the app's own blue.
        ///
        /// Written out rather than read from <c>Pal</c>, which is a Presentation type this
        /// assembly cannot see and should not: the status bar is not one of this game's
        /// screens, and a notification has to look like the app icon rather than like whatever
        /// the current UI kit is tinted with.
        /// </summary>
        static readonly Color Accent = new Color32(0x1E, 0x88, 0xE5, 0xFF);

        readonly string _channelName;
        readonly string _channelBlurb;

        Runner _runner;
        bool _ready;

        // Raised only by the device branch below, so every other compilation of this file —
        // the Editor's, a desktop build's, a clone with no package — warns that nothing raises
        // it. That is the stub doing its job rather than a fault, and the warning is noisy on
        // the one console this project reads for real ones.
#pragma warning disable 67
        public event Action<NotificationKind> Opened;
#pragma warning restore 67

        /// <param name="channelName">What the OS settings call this channel. Player-facing, so localised.</param>
        /// <param name="channelBlurb">The line under it. Also player-facing.</param>
        public MobileNotificationScheduler(string channelName, string channelBlurb)
        {
            _channelName = channelName;
            _channelBlurb = channelBlurb;
        }

#if GLIMMER_NOTIFICATIONS && (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        public bool Supported => true;

        /// <summary>
        /// Registers what the platform needs registering, reads what it already allows, and
        /// starts listening for a tap.
        ///
        /// Idempotent, and separate from the constructor because it touches the platform and
        /// because the coroutines it starts need a scene object, which cannot exist before the
        /// boot path has one.
        /// </summary>
        public void Initialise()
        {
            if (_ready) return;
            _ready = true;

            _runner = Runner.Spawn();

#if UNITY_ANDROID
            AndroidNotificationCenter.Initialize();

            // Default rather than High: these are invitations, not alarms. High gives a
            // heads-up banner over whatever the player is doing, which for "your hearts are
            // full" is the behaviour that gets a game muted. Registering again with a
            // different importance would change nothing anyway — Android freezes a channel's
            // settings at creation for the life of the install.
            AndroidNotificationCenter.RegisterNotificationChannel(
                new AndroidNotificationChannel(Channel, _channelName, _channelBlurb,
                                               Importance.Default));
#endif

            _runner.Run(QueryOpened());
            Refresh();
        }

        /// <summary>
        /// Asks the OS, and reports the answer through <c>NotificationOptIn</c> when it arrives.
        ///
        /// The request is polled on frames rather than against a wall clock because that is
        /// what both platforms expose; what it must never be is a fixed frame budget, since a
        /// system dialog's lifetime has nothing to do with this game's frame rate — the trap a
        /// <c>[UnityTest]</c> in this project has already fallen into from the other side.
        /// </summary>
        public void RequestPermission()
        {
            if (_runner == null) return;
            _runner.Run(Ask());
        }

        IEnumerator Ask()
        {
            NotificationOptIn.SetPermission(NotificationPermission.Pending);

#if UNITY_ANDROID
            // Before Android 13 there is no runtime permission and this finishes immediately
            // as Allowed. It also respects a player who has said "do not ask again", which is
            // why Refresh below is the thing that reads state and this is only ever the ask.
            var request = new PermissionRequest();
            while (request.Status == PermissionStatus.RequestPending) yield return null;
#elif UNITY_IOS
            // Alert, badge and sound — the three this game actually uses. Not asking for
            // sound would deliver every reminder silently, which reads as the feature not
            // working; `registerForRemoteNotifications` is false because nothing here is a
            // push and asking for one would put this app on APNs for no reason.
            using (var request = new AuthorizationRequest(
                       AuthorizationOption.Alert | AuthorizationOption.Badge | AuthorizationOption.Sound,
                       false))
            {
                while (!request.IsFinished) yield return null;
            }
#else
            yield break;
#endif

            Refresh();
        }

        /// <summary>
        /// Re-reads what the OS allows, <b>without prompting</b>.
        ///
        /// <para>
        /// Worth doing on every arm: switching notifications off in the system settings is the
        /// commonest way a granted permission becomes a denied one, and it happens while this
        /// app is not running, so nothing tells us but asking.
        /// </para>
        /// <para>
        /// Deliberately not <c>RequestPermission</c> with the answer thrown away. That answers
        /// immediately once the OS has decided — but on a fresh install it has <em>not</em>
        /// decided, so it prompts, which would turn a background re-arm into a permission
        /// dialog on the splash screen: the exact moment <c>Notify.Ask</c> exists to avoid.
        /// </para>
        /// </summary>
        public void Refresh()
        {
#if UNITY_ANDROID
            switch (AndroidNotificationCenter.UserPermissionToPost)
            {
                case PermissionStatus.Allowed:
                    NotificationOptIn.SetPermission(NotificationPermission.Granted); break;
                case PermissionStatus.NotRequested:
                    NotificationOptIn.SetPermission(NotificationPermission.Unasked); break;
                case PermissionStatus.RequestPending:
                    NotificationOptIn.SetPermission(NotificationPermission.Pending); break;
                default:
                    // Denied, DeniedDontAskAgain and NotificationsBlockedForApp are one state
                    // as far as this game is concerned: nothing will be delivered, and the
                    // only thing that changes it is the player in the OS settings.
                    NotificationOptIn.SetPermission(NotificationPermission.Denied); break;
            }
#elif UNITY_IOS
            switch (iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus)
            {
                case AuthorizationStatus.Authorized:
                case AuthorizationStatus.Provisional:
                case AuthorizationStatus.Ephemeral:
                    NotificationOptIn.SetPermission(NotificationPermission.Granted); break;
                case AuthorizationStatus.NotDetermined:
                    NotificationOptIn.SetPermission(NotificationPermission.Unasked); break;
                default:
                    NotificationOptIn.SetPermission(NotificationPermission.Denied); break;
            }
#endif
        }

        /// <summary>
        /// Cancels everything pending, then writes the plan. See the seam for why that pairing
        /// is one call rather than two.
        /// </summary>
        public void Arm(IReadOnlyList<PlannedNotification> plan)
        {
#if UNITY_ANDROID
            AndroidNotificationCenter.CancelAllScheduledNotifications();
#elif UNITY_IOS
            iOSNotificationCenter.RemoveAllScheduledNotifications();
#endif

            if (plan == null || plan.Count == 0) return;

            foreach (var planned in plan)
            {
                var (title, body) = Notify.Wording(planned);
                if (string.IsNullOrEmpty(title)) continue;

                // Local time, because the plan's instants are UTC seconds and both platforms
                // want a wall-clock DateTime. The conversion is here rather than in the planner
                // so Domain stays integer arithmetic and answers identically on Mono, .NET and
                // IL2CPP.
                var when = DateTimeOffset.FromUnixTimeSeconds(planned.FireUnix).ToLocalTime().DateTime;

#if UNITY_ANDROID
                var notification = new AndroidNotification
                {
                    Title = title,
                    Text = body,
                    FireTime = when,
                    SmallIcon = SmallIcon,

                    // The kind travels with the notification, so a tap can be attributed
                    // without the app keeping any record of what it armed — which is the whole
                    // of why this feature stores nothing at all.
                    IntentData = NotificationKinds.Id(planned.Kind),

                    Color = Accent,

                    // Tapped means dealt with. Without this the row stays in the shade after
                    // the player has opened the game, which is the same complaint ClearDelivered
                    // exists to answer, one notification at a time.
                    ShouldAutoCancel = true,

                    // One thread, so a week of reminders stacks into one row rather than
                    // burying everything else the player has.
                    Group = Channel,
                    GroupAlertBehaviour = GroupAlertBehaviours.GroupAlertChildren,
                };

                AndroidNotificationCenter.SendNotificationWithExplicitID(
                    notification, Channel, planned.Id);
#elif UNITY_IOS
                var notification = new iOSNotification
                {
                    Identifier = planned.Id.ToString(),
                    Title = title,
                    Body = body,
                    Data = NotificationKinds.Id(planned.Kind),

                    // Silent if it arrives while the player is in the game. A reminder to come
                    // and play, delivered over the top of somebody playing, is a bug that looks
                    // like a feature.
                    ShowInForeground = false,

                    ThreadIdentifier = Channel,

                    // A calendar trigger rather than a time interval, so the notification is
                    // pinned to a wall-clock moment the way Android's FireTime is. The two
                    // platforms then mean the same thing by one plan.
                    Trigger = new iOSNotificationCalendarTrigger
                    {
                        Year = when.Year, Month = when.Month, Day = when.Day,
                        Hour = when.Hour, Minute = when.Minute, Second = when.Second,
                        Repeats = false,
                    },
                };

                iOSNotificationCenter.ScheduleNotification(notification);
#endif
            }
        }

        public void ClearDelivered()
        {
#if UNITY_ANDROID
            AndroidNotificationCenter.CancelAllDisplayedNotifications();
#elif UNITY_IOS
            iOSNotificationCenter.RemoveAllDeliveredNotifications();
            iOSNotificationCenter.ApplicationBadge = 0;
#endif
        }

        /// <summary>
        /// Hands the player to the OS's own settings for this app.
        ///
        /// On Android the app's page rather than the channel's: a player whose whole app is
        /// blocked would otherwise be sent to a screen with no master switch on it, which
        /// cannot fix what they came to fix.
        /// </summary>
        public void OpenSettings()
        {
#if UNITY_ANDROID
            AndroidNotificationCenter.OpenNotificationSettings();
#elif UNITY_IOS
            iOSNotificationCenter.OpenNotificationSettings();
#endif
        }

        /// <summary>
        /// Says, once, whether this launch came from a tap.
        ///
        /// <para>
        /// A coroutine rather than a property for the reason the seam gives: Android reads it
        /// off the launching intent, which is not available on the frame the game starts.
        /// Android's answer <em>is</em> synchronous once the intent has arrived, so it is
        /// polled for a bounded number of frames; iOS ships an operation for exactly this and
        /// is yielded on.
        /// </para>
        /// </summary>
        IEnumerator QueryOpened()
        {
            string data = null;

#if UNITY_ANDROID
            // Bounded, because on an ordinary launch there is no intent and never will be —
            // an unbounded poll would be a coroutine running for the life of the session to
            // answer a question that was settled in the first frame.
            for (int frame = 0; frame < 30 && data == null; frame++)
            {
                var intent = AndroidNotificationCenter.GetLastNotificationIntent();
                if (intent != null) data = intent.Notification.IntentData;
                else yield return null;
            }
#elif UNITY_IOS
            var query = iOSNotificationCenter.QueryLastRespondedNotification();
            yield return query;

            if (query.State == QueryLastRespondedNotificationState.HaveRespondedNotification)
                data = query.Notification.Data;
#else
            yield break;
#endif

            var kind = NotificationKinds.Parse(data);
            if (kind == NotificationKind.None) yield break;

            try { Opened?.Invoke(kind); }
            catch (Exception e) { Debug.LogException(e); }
        }
#else
        /// <summary>
        /// The Editor, a desktop build, or a project whose packages have not resolved.
        ///
        /// <para>
        /// Honest rather than convenient: answering <c>false</c> is what makes
        /// <c>NotificationOptIn.Permission</c> read <c>Unsupported</c> and the settings row
        /// draw itself as unavailable, instead of putting a working-looking switch in front of
        /// an Editor that can never send anything. The Editor's half of this feature is the
        /// fixtures, which cover the part that can actually be wrong.
        /// </para>
        /// </summary>
        public bool Supported => false;

        public void Initialise() { }
        public void RequestPermission() { }
        public void Refresh() { }
        public void Arm(IReadOnlyList<PlannedNotification> plan) { }
        public void ClearDelivered() { }
        public void OpenSettings() { }
#endif

        /// <summary>
        /// A scene object to hang coroutines on.
        ///
        /// <para>
        /// The binding owns one rather than borrowing <c>Boot.Pump</c>, because this assembly
        /// must not reference Presentation and because a permission dialog outlives any screen
        /// — a player can sit on it, switch apps and come back, and a coroutine on a screen's
        /// object would have been destroyed under them.
        /// </para>
        /// <para>
        /// <c>HideAndDontSave</c> as well as <c>DontDestroyOnLoad</c>: the object is not the
        /// player's business and must not be saved into a scene by anything in the Editor.
        /// </para>
        /// </summary>
        sealed class Runner : MonoBehaviour
        {
            public static Runner Spawn()
            {
                var go = new GameObject("[Notifications]") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                return go.AddComponent<Runner>();
            }

            public void Run(IEnumerator routine)
            {
                if (routine != null && isActiveAndEnabled) StartCoroutine(routine);
            }
        }
    }
}
