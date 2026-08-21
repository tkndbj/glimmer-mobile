using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace GlimmerGrove.Privacy
{
    /// <summary>
    /// Apple's App Tracking Transparency prompt, bound to the native framework.
    ///
    /// <para>
    /// <b>Why this is hand-written rather than a package.</b> The whole binding is one
    /// framework call and one callback, and the two obvious dependencies both cost more than
    /// they save: Unity's iOS-support package is another package to resolve for forty lines,
    /// and the mediation SDKs that ship their own ATT helper each want to own the timing —
    /// which is the one thing that must stay ours, because the prompt has to come before any
    /// SDK touches the advertising id. See <see cref="AdPrivacy.ResolveAsync"/>.
    /// </para>
    /// <para>
    /// <b>The prompt is shown once per install and the OS enforces that.</b>
    /// <c>requestTrackingAuthorization</c> presents the dialog only while the status is
    /// <c>notDetermined</c>; afterwards it returns the stored answer without showing anything.
    /// So this is safe to call on every launch, and a player who wants to change their mind
    /// does it in iOS Settings — which is why the settings panel sends them there rather than
    /// offering a button that would do nothing.
    /// </para>
    /// <para>
    /// Everything is guarded to iOS device builds. In the Editor and on Android the type still
    /// exists and answers <see cref="TrackingStatus.NotSupported"/>, so <c>Boot</c> needs no
    /// platform branch and the flow can be exercised on a desktop.
    /// </para>
    /// </summary>
    public sealed class AppTrackingPrompt : ITrackingPrompt
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern int GlimmerTrackingStatus();

        [DllImport("__Internal")]
        static extern void GlimmerRequestTracking();

        /// <summary>
        /// Whether iOS is old enough to have no prompt at all.
        ///
        /// The framework arrived in iOS 14. Below it the advertising id is governed by the
        /// device's own "Limit Ad Tracking" switch and there is nothing to ask, which is what
        /// <see cref="TrackingStatus.NotSupported"/> means — not "we could not find out".
        /// </summary>
        static bool Supported
        {
            get
            {
                var version = UnityEngine.iOS.Device.systemVersion;
                int dot = version.IndexOf('.');
                string major = dot > 0 ? version.Substring(0, dot) : version;
                return int.TryParse(major, out int number) && number >= 14;
            }
        }

        public TrackingStatus Status
            => Supported ? (TrackingStatus)GlimmerTrackingStatus() : TrackingStatus.NotSupported;

        /// <summary>
        /// Asks, then polls until the answer stops being <c>notDetermined</c>.
        ///
        /// <para>
        /// A poll rather than a native callback, and that is a deliberate trade. Bridging
        /// Apple's completion handler back into managed code needs a static function pointer
        /// and a <c>MonoPInvokeCallback</c>, which is a well-known way to crash on IL2CPP if
        /// the delegate is ever collected — for a value that is already readable from the
        /// framework the moment the player answers. Polling four times a second for at most
        /// thirty seconds costs nothing measurable and cannot be got wrong.
        /// </para>
        /// <para>
        /// The timeout exists because a dialog can be interrupted — a call, a backgrounded
        /// app, a screen recording prompt that lands on top of it — and a boot path that waits
        /// forever on a dialog nobody is looking at is a game that never starts. Timing out
        /// leaves the status <c>notDetermined</c>, which is the honest answer and is treated
        /// as "no permission" everywhere downstream.
        /// </para>
        /// </summary>
        public async Task<TrackingStatus> RequestAsync(CancellationToken cancellation = default)
        {
            if (!Supported) return TrackingStatus.NotSupported;

            var current = Status;
            if (current != TrackingStatus.NotDetermined) return current;

            GlimmerRequestTracking();

            for (int i = 0; i < 120 && !cancellation.IsCancellationRequested; i++)
            {
                await Task.Delay(250, cancellation).ConfigureAwait(false);

                current = Status;
                if (current != TrackingStatus.NotDetermined) return current;
            }

            Debug.LogWarning("[Privacy] the tracking prompt was not answered; treating as undetermined");
            return TrackingStatus.NotDetermined;
        }
#else
        public TrackingStatus Status => TrackingStatus.NotSupported;

        public Task<TrackingStatus> RequestAsync(CancellationToken cancellation = default)
            => Task.FromResult(TrackingStatus.NotSupported);
#endif
    }
}
