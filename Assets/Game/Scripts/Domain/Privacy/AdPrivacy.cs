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
    /// thing that cannot be got wrong is that the mediation SDK does not start until the
    /// answer is known. An SDK initialised before consent has already chosen what it may
    /// collect and has already run an auction on it - telling it the answer afterwards changes
    /// the <em>next</em> request and cannot undo the first. That is why
    /// <c>RewardedAds.StartAsync</c> waits on <see cref="WhenResolvedAsync"/> and only then
    /// starts the provider, and why this type exists at all rather than each caller asking the
    /// CMP directly.
    /// </para>
    /// <para>
    /// <b>Two moments, and nothing is drawn at the first</b> (invariant 55). A new player meets
    /// the game before any question: the splash calls <see cref="PrepareAsync"/>, which only
    /// refreshes what the CMP knows and reads Apple's stored answer. Where that settles
    /// everything - outside the EEA and the UK, and for every player who has answered before -
    /// the answer is committed there and then, and the SDKs start on the splash exactly as they
    /// always did. What is still owed - the consent form, Apple's prompt - is asked by
    /// <see cref="AskAsync"/> when the player is standing on an idle hub, which for a new
    /// player is the moment the tutorial hands them over. A player who has answered both is
    /// never shown anything, because there is nothing left to ask: the CMP and iOS each keep
    /// their own record, and this type only reads them.
    /// </para>
    /// <para>
    /// <b>Nothing here is stored by us, and that is deliberate.</b> The CMP writes its own
    /// record - the IAB TCF string - into the platform preference store where the adapters
    /// read it, and iOS keeps the tracking answer itself. A copy in our save file would be a
    /// second source of truth that a merge would then have to arbitrate, for a value that is
    /// per-device, revocable and therefore not monotonic: exactly the shape invariant 11b
    /// forbids. The save file gains no field for any of this, and neither does the device:
    /// "what is still owed" is re-derived from the two records on every launch.
    /// </para>
    /// <para>
    /// Until the first answer is committed, <see cref="Signals"/> is
    /// <see cref="AdPrivacySignals.Restricted"/>. Nothing has to null-check, and the state
    /// before an answer is the conservative one rather than a hopeful one.
    /// </para>
    /// </summary>
    public static class AdPrivacy
    {
        static IConsentGateway _consent = new NullConsentGateway();
        static ITrackingPrompt _tracking = new NullTrackingPrompt();

        /// <summary>The splash's step, shared by every caller; null until somebody asks.</summary>
        static Task<AdPrivacySignals> _preparing;

        /// <summary>Completes on the first commit. Replaced only by <see cref="Reset"/>.</summary>
        static TaskCompletionSource<bool> _resolved = NewResolved();

        /// <summary>The splash's step has finished, so what is owed is known.</summary>
        static bool _prepared;

        /// <summary>The hub's step is running. Nothing may start a second one.</summary>
        static bool _asking;

        /// <summary>The consent form is still owed on this launch.</summary>
        static bool _formOwed;

        /// <summary>Apple's prompt is still owed on this launch.</summary>
        static bool _trackingOwed;

        /// <summary>
        /// Whether this app is directed at children under COPPA.
        ///
        /// <para>
        /// A compile-time constant because it is a fact about the product rather than a
        /// tuning knob - it follows from the store listing's age rating and from how the game
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

        /// <summary>True once an answer has been committed at least once.</summary>
        public static bool IsResolved { get; private set; }

        /// <summary>
        /// Whether the hub has a question to put to the player: the splash's step has run, and
        /// the consent form or Apple's prompt is still owed on this launch. False while the
        /// hub's step is already running, so a poll can never start it twice.
        ///
        /// <para>
        /// False for ever on a build with no ad provider, because nothing calls
        /// <see cref="PrepareAsync"/> there - a question nobody will use the answer to is
        /// never asked. See <c>RewardedAds.StartAsync</c>.
        /// </para>
        /// </summary>
        public static bool Owed => _prepared && !_asking && (_formOwed || _trackingOwed);

        /// <summary>
        /// Raised whenever the signals change - on first resolve, when the hub's question is
        /// answered, and again if the player revisits the form. Anything holding a copy has to
        /// repaint; the settings row and the ad provider both listen rather than polling.
        /// </summary>
        public static event Action<AdPrivacySignals> Changed;

        public static void Install(IConsentGateway gateway) => _consent = gateway ?? new NullConsentGateway();

        public static void Install(ITrackingPrompt prompt) => _tracking = prompt ?? new NullTrackingPrompt();

        /// <summary>Whether the player is entitled to a privacy control in Settings.</summary>
        public static bool CanRevisit => _consent.CanRevisit;

        /// <summary>
        /// The splash's step: learns what is owed, and commits the answer at once where nothing
        /// is. <b>Never draws anything.</b> Idempotent - every caller shares the one run.
        ///
        /// <para>
        /// Apple's status is <em>read</em> here, never requested. A settled consent question is
        /// committed with whatever iOS already holds, so a player outside the EEA has ads,
        /// analytics and attribution running before the tutorial's first frame, and Apple's
        /// prompt follows on the hub as one more change of signals.
        /// </para>
        /// <para>
        /// Never throws. A CMP that cannot be reached leaves the question open, and the hub asks
        /// again - one more refresh, then the form - before giving up for the launch.
        /// </para>
        /// </summary>
        /// <remarks>
        /// Deliberately no <c>ConfigureAwait(false)</c> on any await in this type. Everything
        /// downstream of it eventually calls into a native SDK - the CMP, Apple's prompt, then
        /// mediation - and those must be reached from Unity's main thread. A plain await resumes
        /// there because the main thread carries a SynchronizationContext; ConfigureAwait(false)
        /// resumes on the thread pool, where the first JNI call throws into a task nobody is
        /// watching. See <see cref="Ads.RewardedAds.BeginStart"/>.
        /// </remarks>
        public static Task<AdPrivacySignals> PrepareAsync(CancellationToken cancellation = default)
            => _preparing ?? (_preparing = Prepare(cancellation));

        static async Task<AdPrivacySignals> Prepare(CancellationToken cancellation)
        {
            AdPrivacySignals refreshed;

            try
            {
                refreshed = await _consent.RefreshAsync(cancellation);
            }
            catch (Exception)
            {
                // Swallowed rather than rethrown: this runs from the splash, and the safe answer
                // - an open question, asked again on the hub - is already known.
                refreshed = AdPrivacySignals.Restricted;
            }

            var tracking = StoredTracking();

            _formOwed = !refreshed.ConsentSettled;
            _trackingOwed = tracking == TrackingStatus.NotDetermined;

            // Committed only when the consent question is closed. An open one starts nothing:
            // in the EEA the SDKs wait for the form, which is what the law asks and what the
            // splash always did - the only difference is that the form now waits for the hub.
            if (!_formOwed) Commit(With(refreshed, tracking));

            _prepared = true;
            return Signals;
        }

        /// <summary>
        /// The hub's step: shows what is still owed, in the one legal order - the consent form,
        /// then Apple's prompt - and commits the answer.
        ///
        /// <para>
        /// <paramref name="mayPresent"/> is the caller's "the player is on an idle hub", asked
        /// again immediately before each dialog, because the form takes a network round trip to
        /// load and the player may have walked into a run meanwhile. A dialog that would land
        /// on anything but the hub is not shown; it stays owed and the hub asks again when the
        /// player is back.
        /// </para>
        /// <para>
        /// <b>Consent form first, then Apple's prompt.</b> Both orders are legal and the
        /// choice is about opt-in rates rather than compliance: Apple only requires its prompt
        /// to precede any use of the advertising id. Showing the system dialog cold is the
        /// reliable way to have it refused, where the CMP's form has already established what
        /// the question is for.
        /// </para>
        /// <para>
        /// <b>And the order is a rule Apple enforces, not only a preference.</b> A consent form
        /// that appears <em>after</em> "Ask App Not to Track" reads to App Review as asking
        /// permission to track twice (Guideline 5.1.1(iv)), so Apple is asked only once the
        /// consent question is closed (<see cref="AdPrivacySignals.ConsentSettled"/>): on a
        /// launch where the form has been shown and answered, or was never owed. A launch on
        /// which the CMP failed asks Apple nothing. Pinned by <c>PrivacyTests</c>.
        /// </para>
        /// <para>
        /// Never throws, and returns at once when nothing is owed or a run is already going.
        /// A failure commits the restrictive answer, which is a game that runs and shows
        /// unpersonalised ads rather than one that waits for ever.
        /// </para>
        /// </summary>
        public static async Task<AdPrivacySignals> AskAsync(Func<bool> mayPresent,
                                                            CancellationToken cancellation = default)
        {
            if (!Owed) return Signals;

            mayPresent = mayPresent ?? (() => false);
            _asking = true;

            try
            {
                // Where the form is not owed the splash committed the consent half already, and
                // that is the answer Apple's half joins.
                var consent = Signals;

                if (_formOwed)
                {
                    ConsentAsk asked;

                    try
                    {
                        asked = await _consent.AskAsync(mayPresent, cancellation);
                    }
                    catch (Exception)
                    {
                        asked = ConsentAsk.Answered(AdPrivacySignals.Restricted);
                    }

                    // Dropped unseen: the player left the hub while it loaded. Nothing was asked
                    // and nothing is committed; the question is put again on their return.
                    if (asked.Deferred) return Signals;

                    consent = asked.Signals;
                    _formOwed = false;
                }

                // Apple's prompt is asked only once the consent question is closed, and read
                // without asking otherwise. A sequence of awaits only orders what each await
                // *covers*: a gateway that gave up on its own form and returned with it still on
                // screen put Apple's dialog on top of it, and Apple rejected 1.0.2 for exactly
                // that (5.1.1(iv), 2026-09-22). So an open question - a form that failed to load
                // or show - leaves Apple for a later launch on which the form can come first.
                var tracking = StoredTracking();

                if (_trackingOwed)
                {
                    if (!consent.ConsentSettled)
                    {
                        _trackingOwed = false;
                    }
                    else if (mayPresent())
                    {
                        try
                        {
                            tracking = await _tracking.RequestAsync(cancellation);
                        }
                        catch (Exception)
                        {
                            tracking = TrackingStatus.NotDetermined;
                        }

                        // Asked once a launch. An unanswered dialog (a call came in, the app was
                        // backgrounded) leaves iOS at NotDetermined, and the next launch asks
                        // again rather than this one asking in a loop.
                        _trackingOwed = false;
                    }

                    // Otherwise the player has moved on since the form: Apple stays owed and is
                    // asked on their return, and the consent answer is committed now regardless.
                }

                Commit(With(consent, tracking));
                return Signals;
            }
            finally
            {
                _asking = false;
            }
        }

        /// <summary>
        /// Completes once an answer has been committed - on the splash where nothing was owed,
        /// otherwise when the hub's question has been answered or has failed.
        /// </summary>
        public static async Task WhenResolvedAsync(CancellationToken cancellation = default)
        {
            if (IsResolved) return;

            if (!cancellation.CanBeCanceled)
            {
                await _resolved.Task;
                return;
            }

            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (cancellation.Register(() => cancelled.TrySetCanceled()))
            {
                await await Task.WhenAny(_resolved.Task, cancelled.Task);
            }
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
                var resolved = await _consent.RevisitAsync(cancellation);
                Commit(With(resolved, StoredTracking()));
            }
            catch (Exception)
            {
                // Leaves the last known answer standing. A form that failed to open has not
                // changed anybody's mind, so nothing should move.
            }

            return Signals;
        }

        /// <summary>What iOS already holds, read without asking. Never throws.</summary>
        static TrackingStatus StoredTracking()
        {
            try
            {
                return _tracking.Status;
            }
            catch (Exception)
            {
                return TrackingStatus.NotDetermined;
            }
        }

        /// <summary>
        /// Folds in the two answers this type owns rather than the CMP: the tracking status,
        /// and the app's own COPPA classification. Kept in one place so a gateway cannot
        /// accidentally claim a child-directed install is not one.
        /// </summary>
        static AdPrivacySignals With(AdPrivacySignals resolved, TrackingStatus tracking)
            => new AdPrivacySignals(resolved.GdprApplies, resolved.Gdpr, resolved.DoNotSell,
                                    ChildDirected, tracking);

        /// <summary>
        /// Adopts an answer. <c>IsResolved</c> is set <em>after</em> <c>Changed</c> is raised,
        /// and that order is pinned - see <c>PrivacyTests.AChangedHandlerIsGivenTheAnswerTheFlagDoesNotYetCarry</c>.
        /// </summary>
        static void Commit(AdPrivacySignals next)
        {
            if (next == Signals && IsResolved) return;

            Signals = next;
            Changed?.Invoke(next);

            IsResolved = true;
            _resolved.TrySetResult(true);
        }

        static TaskCompletionSource<bool> NewResolved()
            => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Puts everything back, for a test that installs its own gateway.</summary>
        internal static void Reset()
        {
            _consent = new NullConsentGateway();
            _tracking = new NullTrackingPrompt();
            _preparing = null;
            _resolved = NewResolved();
            _prepared = false;
            _asking = false;
            _formOwed = false;
            _trackingOwed = false;
            Signals = AdPrivacySignals.Restricted;
            IsResolved = false;
            Changed = null;
        }
    }
}
