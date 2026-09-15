using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
#if GLIMMER_NOTIFICATIONS
using Unity.Notifications;
#endif

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Holds the notification package's own project settings to what this game needs, and
    /// proves it on the way into a build.
    ///
    /// <para>
    /// <b>This exists because of one default that is wrong for us and silent about it:
    /// Android discards every scheduled notification when the device restarts.</b>
    /// <c>RescheduleOnDeviceRestart</c> is off out of the box, and with it off a phone reboot
    /// wipes the whole seven-day schedule — which is not rebuilt until the player next opens
    /// the game, and the player whose schedule matters is precisely the one who is not opening
    /// it. Nothing reports this: the build succeeds, the notifications work in a test that
    /// never reboots, and the loss shows up as retention that is quietly worse than it should
    /// be. Turning it on makes the package inject <c>RECEIVE_BOOT_COMPLETED</c> and a
    /// <c>BOOT_COMPLETED</c> receiver that re-registers what has not expired.
    /// </para>
    /// <para>
    /// <b>Exact alarms stay off, deliberately.</b> Android 12 gates <c>SCHEDULE_EXACT_ALARM</c>
    /// behind a user grant and Android 13's <c>USE_EXACT_ALARM</c> is reserved for alarm clocks
    /// and calendars — Google rejects apps that claim it for anything else, and a reminder is
    /// exactly the "anything else" it is written to exclude. Inexact means the OS may hold a
    /// notification back to batch it with others, which for "your hearts are full" is a
    /// difference nobody can perceive and a store-review risk nobody needs.
    /// </para>
    /// <para>
    /// <b>It is applied by a build hook rather than a menu item</b>, which is invariant 7a: a
    /// step somebody has to remember on shipping week will be forgotten, and this one is
    /// invisible when it is. The setting lives in <c>ProjectSettings/</c> rather than in a file
    /// this repository writes, so it is asserted every build instead of being checked in and
    /// hoped for — a fresh clone, a second machine and a reset of Project Settings all land in
    /// the same place.
    /// </para>
    /// </summary>
    public sealed class NotificationSetup : IPreprocessBuildWithReport
    {
        /// <summary>
        /// After <c>ContentBuildGate</c>, which is where a build is stopped. This one changes a
        /// setting rather than failing, so it has nothing to say on a build that is about to be
        /// refused anyway.
        /// </summary>
        public int callbackOrder => 10;

        public void OnPreprocessBuild(BuildReport report) => Apply(report.summary.platform);

        [MenuItem("Glimmer Grove/Apply Notification Settings", false, 24)]
        public static void ApplyMenu() => Apply(EditorUserBuildSettings.activeBuildTarget);

        static void Apply(BuildTarget target)
        {
#if !GLIMMER_NOTIFICATIONS
            if (target == BuildTarget.Android || target == BuildTarget.iOS)
                Debug.LogWarning("[Glimmer] com.unity.mobile.notifications is not installed, so " +
                                 "this build sends no reminders at all. That is a working game " +
                                 "and a silent one — see invariant 50.");
#else
            if (target != BuildTarget.Android) return;

            // Compared before writing, for DevicePrefs' reason: this dirties a project settings
            // asset, and a build that changes a checked-in file every single time is a build
            // that produces a diff nobody can read.
            if (!NotificationSettings.AndroidSettings.RescheduleOnDeviceRestart)
            {
                NotificationSettings.AndroidSettings.RescheduleOnDeviceRestart = true;
                Debug.Log("[Glimmer] turned on Android reschedule-on-restart; without it a " +
                          "reboot silently wipes every scheduled reminder");
            }

            // Asserted rather than assumed. If a future package version changes what this
            // property means, the build says so rather than shipping a schedule a reboot eats.
            if (!NotificationSettings.AndroidSettings.RescheduleOnDeviceRestart)
                Debug.LogError("[Glimmer] Android reschedule-on-restart would not stay on. " +
                               "Every scheduled reminder will be lost on the next device reboot.");
#endif
        }
    }
}
