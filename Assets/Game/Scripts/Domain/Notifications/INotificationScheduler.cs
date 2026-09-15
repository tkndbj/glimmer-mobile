using System.Collections.Generic;

namespace GlimmerGrove.Notifications
{
    /// <summary>
    /// The seam between the plan and whichever notification plugin is installed.
    ///
    /// <para>
    /// Lives in Domain and names no SDK type, which is <c>IConsentGateway</c>'s and
    /// <c>ICloudSaveBackend</c>'s bargain and pays the same way: the slate, the pacing, the
    /// quiet hours and every fixture that pins them are written and provable with no
    /// notification package in the project at all, and on a machine with no phone.
    /// </para>
    /// <para>
    /// <b>The interface is four calls, and the one that is not obvious is
    /// <see cref="Arm"/>'s contract: it replaces everything.</b> A scheduler that appended
    /// would accumulate a week of stale reminders every time the app was backgrounded, and
    /// the failure would be invisible in every test — the duplicates only exist on the
    /// device, they only appear while nobody is looking, and the first person to find out is
    /// a player getting nine notifications on a Tuesday. So cancel-then-write is the whole
    /// operation rather than two the caller has to remember to pair.
    /// </para>
    /// <para>
    /// There is deliberately nothing here about tokens, topics or a server. Everything this
    /// game sends is derived on the device (see <see cref="NotificationKind"/>), so a second
    /// implementation that talked to a push service would be answering a question nothing
    /// asks. The day a genuine broadcast is wanted it is a sibling seam, not a widening of
    /// this one — a broadcast has no fire time and no local predicate, which is to say it has
    /// nothing in common with this interface but the word.
    /// </para>
    /// </summary>
    public interface INotificationScheduler
    {
        /// <summary>Whether this platform can deliver anything at all.</summary>
        bool Supported { get; }

        /// <summary>
        /// Registers whatever the platform needs registering and reads its current answer.
        /// Idempotent; called once on the boot path.
        /// </summary>
        void Initialise();

        /// <summary>
        /// Re-reads what the OS allows <b>without prompting</b>, reporting through
        /// <c>NotificationOptIn.SetPermission</c>.
        ///
        /// Separate from <see cref="RequestPermission"/> because the two are genuinely
        /// different acts: a player who revoked permission in the system settings did it while
        /// this app was not running, so the only way to learn about it is to look — and
        /// looking must never turn into a dialog.
        /// </summary>
        void Refresh();

        /// <summary>
        /// Asks the OS for permission, if it has never been asked.
        ///
        /// Must not throw and must not block: both platforms answer asynchronously and the
        /// result arrives through <c>NotificationOptIn.SetPermission</c>. Calling it when the
        /// OS has already answered is a no-op rather than a second dialog, because neither
        /// platform will show one.
        /// </summary>
        void RequestPermission();

        /// <summary>
        /// Replaces the device's whole schedule with <paramref name="plan"/>.
        ///
        /// Every pending notification is cancelled first, so this is idempotent and calling it
        /// twice leaves one schedule. An empty plan is a legal argument and means "say
        /// nothing", which is what a player who has switched reminders off is owed.
        /// </summary>
        void Arm(IReadOnlyList<PlannedNotification> plan);

        /// <summary>
        /// Opens the OS's own notification settings for this app.
        ///
        /// The only honest control to offer a player whose device is blocking us: neither
        /// platform will show its permission dialog twice, so a second in-game "allow" button
        /// would do nothing at all — which is the broken button invariant 16o refuses.
        /// </summary>
        void OpenSettings();

        /// <summary>
        /// Clears notifications already sitting in the shade, and the launcher badge with them.
        ///
        /// Called when the player opens the game, because every reminder this game sends says
        /// "come and look" and they are looking. Leaving them there is how a shade fills with
        /// a week of invitations to something the player is already doing.
        /// </summary>
        void ClearDelivered();

        /// <summary>
        /// Raised once per launch, when the platform works out that this launch came from a tap.
        ///
        /// <para>
        /// An event rather than a property, because neither platform can answer synchronously:
        /// Android has to read the launching intent and iOS the delegate callback, both of
        /// which arrive after the boot path has started. A property would therefore answer
        /// "no" on every launch and be right by accident in the Editor — the shape of bug this
        /// project has already paid for in probes that write off a real failure as expected.
        /// </para>
        /// </summary>
        event System.Action<NotificationKind> Opened;
    }

    /// <summary>
    /// The scheduler used when no notification package is installed — the Editor, a desktop
    /// build, a machine that has never resolved packages.
    ///
    /// <para>
    /// It reports <see cref="Supported"/> false rather than pretending, which is what makes
    /// <c>NotificationOptIn.Permission</c> answer <c>Unsupported</c> and the settings row draw
    /// itself accordingly. A null object that claimed success would put a working-looking
    /// toggle in front of an Editor that can never send anything.
    /// </para>
    /// </summary>
    public sealed class NullNotificationScheduler : INotificationScheduler
    {
        public bool Supported => false;
        public void Initialise() { }
        public void Refresh() { }
        public void RequestPermission() { }
        public void OpenSettings() { }
        public void Arm(IReadOnlyList<PlannedNotification> plan) { }
        public void ClearDelivered() { }

#pragma warning disable 67 // never raised here, which is the whole point of a null object
        public event System.Action<NotificationKind> Opened;
#pragma warning restore 67
    }
}
