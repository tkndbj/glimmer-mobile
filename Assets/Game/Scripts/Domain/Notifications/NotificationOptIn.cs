using System;
using UnityEngine;

namespace GlimmerGrove.Notifications
{
    /// <summary>What the operating system has been asked, and what it said.</summary>
    public enum NotificationPermission
    {
        /// <summary>Not asked yet. The only state in which asking is allowed.</summary>
        Unasked = 0,

        /// <summary>Asked, and the OS is still showing the dialog.</summary>
        Pending,

        /// <summary>Granted. Notifications will be delivered.</summary>
        Granted,

        /// <summary>Refused, or switched off in the OS settings afterwards.</summary>
        Denied,

        /// <summary>This platform has no notifications at all - the Editor, a desktop build.</summary>
        Unsupported,

        /// <summary>
        /// iOS only: delivered quietly to Notification Center without ever having shown a dialog,
        /// each one carrying the OS's own Keep / Turn Off buttons. Counts as allowed, and the OS
        /// will still show its full dialog when asked, so our panel may still offer the upgrade.
        /// Appended rather than slotted in, so no ordinal above moves.
        /// </summary>
        Provisional,
    }

    /// <summary>
    /// Whether this player wants reminders, and whether the OS is letting us send them.
    ///
    /// <para>
    /// <b>Device-local, and it must never be in the save.</b> It is a fact about this install
    /// on this handset, not about the player: merged across devices it would follow somebody
    /// from a phone they silenced onto a tablet they did not, and the save merges
    /// monotonically (invariant 11b), which cannot express a switch that goes both ways
    /// without a stamp per field (11c). It is <c>ReleaseGate</c>'s argument exactly, and the
    /// bill it dodges is the same one: no schema version, no <c>SaveDelta</c> field, no
    /// Firestore mapper on both sides and no <c>hasOnly</c> entry, which is the four places
    /// invariant 12a says a save field really costs.
    /// </para>
    /// <para>
    /// <b>The OS answer and the player's switch are two different facts and both are needed.</b>
    /// A player who refused the system dialog can only rarely be asked again by us - iOS shows
    /// it once per install, Android 13+ twice - so the in-game switch has to be able to say "yes
    /// please" while the OS still says no, and the settings panel is then obliged to send them
    /// to the OS rather than lying about what its own toggle did. Whether the OS would still
    /// draw its dialog is <see cref="CanAsk"/>, defined once in <see cref="NotificationAsk"/>.
    /// </para>
    /// </summary>
    public static class NotificationOptIn
    {
        /// <summary>
        /// Where the switch is written.
        ///
        /// Renaming it silently opts every existing player back in, which on a feature whose
        /// whole failure mode is annoying people is worse than it sounds. Written down so the
        /// rename is a deliberate act - <c>ReleaseGate.Key</c>'s note, for the same reason.
        /// </summary>
        const string Key = "glimmer.notify.on";

        /// <summary>Whether the player has left reminders switched on. Default yes.</summary>
        public static bool Wanted
        {
            get => PlayerPrefs.GetInt(Key, 1) != 0;
        }

        /// <summary>What the OS last told us. Set by the platform binding; never persisted.</summary>
        public static NotificationPermission Permission { get; private set; }
            = NotificationPermission.Unsupported;

        /// <summary>Raised when either half changes, so a panel can redraw and the plan can be re-armed.</summary>
        public static event Action Changed;

        /// <summary>
        /// Android's answer to "would a second request still draw the dialog": true after exactly
        /// one refusal on Android 13+, false everywhere else. Set by the binding with
        /// <see cref="Permission"/>; never persisted.
        /// </summary>
        public static bool CanPromptAgain { get; private set; }

        /// <summary>Both halves agree: this device may be spoken to. Provisional is spoken to quietly.</summary>
        public static bool Allowed => Wanted && (Permission == NotificationPermission.Granted
                                              || Permission == NotificationPermission.Provisional);

        /// <summary>
        /// Whether asking the OS would draw its dialog: never asked, iOS provisional, or Android's
        /// second chance. False once it will only answer "denied" without drawing anything, which
        /// is how a "grant notifications" button comes to do nothing with no explanation.
        /// </summary>
        public static bool CanAsk => NotificationAsk.OsWillPrompt(Permission, CanPromptAgain);

        /// <summary>
        /// Writes the switch and flushes.
        ///
        /// Not through <c>DevicePrefs</c>, which only wraps strings, and not needing
        /// to be: the rule that class owns is <em>do not flush what has not changed</em>, and
        /// the guard above is that rule - this is a writer that only ever writes a real
        /// change, which is the exemption its own note grants <c>RunGuard</c>.
        /// </summary>
        public static void SetWanted(bool on)
        {
            if (Wanted == on) return;

            PlayerPrefs.SetInt(Key, on ? 1 : 0);
            PlayerPrefs.Save();
            Raise();
        }

        /// <summary>Records what the platform answered. Called by the binding and by nothing else.</summary>
        public static void SetPermission(NotificationPermission permission, bool canPromptAgain = false)
        {
            canPromptAgain &= permission == NotificationPermission.Denied;
            if (Permission == permission && CanPromptAgain == canPromptAgain) return;

            Permission = permission;
            CanPromptAgain = canPromptAgain;
            Raise();
        }

        /// <summary>Analytics spelling of a permission state. Permanent once a dashboard reads it.</summary>
        public static string Id(NotificationPermission permission)
        {
            switch (permission)
            {
                case NotificationPermission.Unasked: return "unasked";
                case NotificationPermission.Pending: return "pending";
                case NotificationPermission.Granted: return "granted";
                case NotificationPermission.Denied: return "denied";
                case NotificationPermission.Unsupported: return "unsupported";
                case NotificationPermission.Provisional: return "provisional";
                default: throw new ArgumentOutOfRangeException(nameof(permission), permission, null);
            }
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
