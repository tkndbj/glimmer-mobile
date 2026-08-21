using System;
using System.Threading;
using System.Threading.Tasks;

namespace GlimmerGrove.Privacy
{
    /// <summary>
    /// Asks the iOS tracking question. Implemented natively; absent everywhere else.
    ///
    /// A seam of its own rather than a branch inside the CMP, because the two are unrelated
    /// obligations that happen to be adjacent: Apple governs the device advertising id and
    /// wants a system prompt, and the GDPR governs personal data and wants a consent record.
    /// Building them as one thing means every future change to either has to reason about both.
    /// </summary>
    public interface ITrackingPrompt
    {
        /// <summary>The answer without asking. Cheap, and safe to call every launch.</summary>
        TrackingStatus Status { get; }

        /// <summary>
        /// Shows Apple's prompt if it has never been answered, and returns the result.
        ///
        /// Idempotent by the platform's own rules: iOS shows the dialog once per install and
        /// afterwards this simply reports what was decided. Must never throw.
        /// </summary>
        Task<TrackingStatus> RequestAsync(CancellationToken cancellation = default);
    }

    /// <summary>Reports "not an iOS build", which is the truth on Android and in the Editor.</summary>
    public sealed class NullTrackingPrompt : ITrackingPrompt
    {
        public TrackingStatus Status => TrackingStatus.NotSupported;

        public Task<TrackingStatus> RequestAsync(CancellationToken cancellation = default)
            => Task.FromResult(TrackingStatus.NotSupported);
    }

    /// <summary>
    /// What this player has agreed to, and the order in which they were asked.
    ///
    /// <para>
    /// <b>The ordering is the whole feature.</b> Everything else here is plumbing; the one
    /// thing that cannot be got wrong is that the mediation SDK does not start until this has
    /// finished. An SDK initialised before consent has already chosen what it may collect and
    /// has already run an auction on it — telling it the answer afterwards changes the
    /// <em>next</em> request and cannot undo the first. That is why <c>Boot</c> awaits
    /// <see cref="ResolveAsync"/> and only then constructs the provider, and why this type
    /// exists at all rather than each caller asking the CMP directly.
    /// </para>
    /// <para>
    /// <b>Nothing here is stored by us, and that is deliberate.</b> The CMP writes its own
    /// record — the IAB TCF string — into the platform preference store where the adapters
    /// read it, and iOS keeps the tracking answer itself. A copy in our save file would be a
    /// second source of truth that a merge would then have to arbitrate, for a value that is
    /// per-device, revocable and therefore not monotonic: exactly the shape invariant 11b
    /// forbids. The save file gains no field for any of this.
    /// </para>
    /// <para>
    /// Until <see cref="ResolveAsync"/> completes, <see cref="Signals"/> is
    /// <see cref="AdPrivacySignals.Restricted"/>. Nothing has to null-check, and the state
    /// before an answer is the conservative one rather than a hopeful one.
    /// </para>
    /// </summary>
    public static class AdPrivacy
    {
        static IConsentGateway _consent = new NullConsentGateway();
        static ITrackingPrompt _tracking = new NullTrackingPrompt();

        /// <summary>
        /// Whether this app is directed at children under COPPA.
        ///
        /// <para>
        /// A compile-time constant because it is a fact about the product rather than a
        /// tuning knob — it follows from the store listing's age rating and from how the game
        /// is marketed, and a published file that could flip it would let a content push
        /// change the app's legal posture. Glimmer Grove is a general-audience puzzle game and
        /// is not child-directed; if that ever changes, this constant and the store listings
        /// move together, and mediation is told through <see cref="AdPrivacySignals"/> with
        /// nothing else to edit.
        /// </para>
        /// </summary>
        public const bool ChildDirected = false;

        /// <summary>What the player has agreed to. Restrictive until resolved.</summary>
        public static AdPrivacySignals Signals { get; private set; } = AdPrivacySignals.Restricted;

        /// <summary>True once <see cref="ResolveAsync"/> has completed at least once.</summary>
        public static bool IsResolved { get; private set; }

        /// <summary>
        /// Raised whenever the signals change — on first resolve, and again if the player
        /// revisits the form. Anything holding a copy has to repaint; the settings row and
        /// the ad provider both listen rather than polling.
        /// </summary>
        public static event Action<AdPrivacySignals> Changed;

        public static void Install(IConsentGateway gateway) => _consent = gateway ?? new NullConsentGateway();

        public static void Install(ITrackingPrompt prompt) => _tracking = prompt ?? new NullTrackingPrompt();

        /// <summary>Whether the player is entitled to a privacy control in Settings.</summary>
        public static bool CanRevisit => _consent.CanRevisit;

        /// <summary>
        /// Resolves consent, prompting where one is owed, and returns the result.
        ///
        /// <para>
        /// <b>Consent form first, then Apple's prompt.</b> Both orders are legal and the
        /// choice is about opt-in rates rather than compliance: Apple only requires its prompt
        /// to precede any use of the advertising id, and nothing touches the id until the
        /// mediation SDK starts, which is after both. Showing the system dialog cold — with no
        /// explanation, seconds after a first launch — is the reliable way to have it refused,
        /// where the CMP's form has already established what the question is for. Swapping the
        /// two is moving one line, and the reason to do it would be evidence rather than taste.
        /// </para>
        /// <para>
        /// Never throws. A CMP that cannot reach its servers, a cancelled boot, an SDK that
        /// misbehaves — all of them leave the restrictive default in place, which is a game
        /// that runs and shows unpersonalised ads rather than a splash screen that never ends.
        /// </para>
        /// </summary>
        public static async Task<AdPrivacySignals> ResolveAsync(CancellationToken cancellation = default)
        {
            AdPrivacySignals resolved;

            try
            {
                resolved = await _consent.ResolveAsync(cancellation).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Deliberately swallowed rather than logged-and-rethrown. This runs on the boot
                // path before anything is on screen, so an exception here is a game that never
                // starts — and the safe answer is already known.
                resolved = AdPrivacySignals.Restricted;
            }

            TrackingStatus tracking;

            try
            {
                tracking = await _tracking.RequestAsync(cancellation).ConfigureAwait(false);
            }
            catch (Exception)
            {
                tracking = TrackingStatus.NotDetermined;
            }

            Commit(With(resolved, tracking));
            IsResolved = true;

            return Signals;
        }

        /// <summary>
        /// Reopens the consent form and adopts whatever the player decides.
        ///
        /// The tracking answer is <em>read</em> rather than re-requested: iOS shows its dialog
        /// once per install and asking again is a no-op, so a player who wants to change that
        /// half is sent to the system settings by the panel rather than being handed a prompt
        /// that will not appear.
        /// </summary>
        public static async Task<AdPrivacySignals> RevisitAsync(CancellationToken cancellation = default)
        {
            if (!_consent.CanRevisit) return Signals;

            try
            {
                var resolved = await _consent.RevisitAsync(cancellation).ConfigureAwait(false);
                Commit(With(resolved, _tracking.Status));
            }
            catch (Exception)
            {
                // Leaves the last known answer standing. A form that failed to open has not
                // changed anybody's mind, so nothing should move.
            }

            return Signals;
        }

        /// <summary>
        /// Folds in the two answers this type owns rather than the CMP: the tracking status,
        /// and the app's own COPPA classification. Kept in one place so a gateway cannot
        /// accidentally claim a child-directed install is not one.
        /// </summary>
        static AdPrivacySignals With(AdPrivacySignals resolved, TrackingStatus tracking)
            => new AdPrivacySignals(resolved.GdprApplies, resolved.Gdpr, resolved.DoNotSell,
                                    ChildDirected, tracking);

        static void Commit(AdPrivacySignals next)
        {
            if (next == Signals && IsResolved) return;

            Signals = next;
            Changed?.Invoke(next);
        }

        /// <summary>Puts everything back, for a test that installs its own gateway.</summary>
        internal static void Reset()
        {
            _consent = new NullConsentGateway();
            _tracking = new NullTrackingPrompt();
            Signals = AdPrivacySignals.Restricted;
            IsResolved = false;
            Changed = null;
        }
    }
}
