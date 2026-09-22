#if GLIMMER_UMP
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

namespace GlimmerGrove.Privacy
{
    /// <summary>
    /// Google's User Messaging Platform, behind <see cref="IConsentGateway"/>.
    ///
    /// <para>
    /// Compiled only when the Google Mobile Ads package is installed — <c>GLIMMER_UMP</c>
    /// comes from this assembly's <c>versionDefines</c>, never from Player Settings, for the
    /// reason <c>GLIMMER_ADDRESSABLES</c> already documents: a Player Settings define is per
    /// build target, so one added on Standalone is silently absent on Android and iOS. For a
    /// consent SDK that would mean a mobile build which compiles, ships, and asks nobody
    /// anything.
    /// </para>
    /// <para>
    /// <b>Why a certified CMP rather than a dialog of our own.</b> Three things a hand-rolled
    /// prompt cannot do, and each is disqualifying on its own. It cannot tell whether this
    /// player is in the EEA or the UK, so it must either interrupt everybody on earth or
    /// nobody. It cannot write the IAB TCF consent string, which is the thing every mediation
    /// adapter actually reads — so the networks would go on treating a consenting player as
    /// non-consented and the revenue the exercise exists to protect would not arrive. And
    /// Google's EU User Consent Policy requires a certified CMP for publishers serving Google
    /// demand, which this game will be as soon as AdMob is in the waterfall.
    /// </para>
    /// <para>
    /// <b>What this class does not do is as important as what it does.</b> It never returns a
    /// consent string and never parses one. UMP writes the TCF string into the platform's own
    /// preference store, where the adapters read it directly; a copy carried through our code
    /// would be a second source of truth for a value we neither own nor validate, and it would
    /// be the copy that went stale. What crosses this boundary is only the coarse booleans a
    /// mediation SDK has to be told in an API call.
    /// </para>
    /// <para>
    /// <b>A network call is bounded and a person is not, and the two are never inside one
    /// wait.</b> The first version wrapped "load the form and show it and wait for the answer"
    /// in a single fifteen-second timeout — so a reviewer who read the form for sixteen
    /// seconds, or whose form loaded slowly, found the gateway giving up underneath them:
    /// <see cref="AdPrivacy.ResolveAsync"/> moved on to Apple's tracking prompt, which landed
    /// on top of the consent form still on screen, and after "Ask App Not to Track" the form
    /// was still there asking about personalised ads. Apple rejected 1.0.2 for exactly that
    /// sequence (Guideline 5.1.1(iv), 2026-09-22). So now <see cref="LoadForm"/> is bounded,
    /// because Google's servers can hang, and <see cref="Show"/> is not, because a form on
    /// screen is somebody deciding and there is no honest answer to give in their place. A
    /// form that arrives after the load timeout is dropped rather than shown late, for the
    /// same reason: late is after Apple's prompt.
    /// </para>
    /// </summary>
    public sealed class UmpConsentGateway : IConsentGateway
    {
        /// <summary>
        /// How long to wait for Google's servers before giving up and running unpersonalised.
        ///
        /// A boot path may not wait indefinitely on a network call — the failure a player sees
        /// would be a splash screen that never ends, which is worse than any amount of lost ad
        /// revenue. Fifteen seconds is far beyond a healthy round trip and far short of a
        /// player deciding the game is broken. It bounds the two network calls here — the
        /// consent-info refresh and the form load — and nothing else.
        /// </summary>
        const int TimeoutMilliseconds = 15_000;

        public bool CanRevisit
            => ConsentInformation.PrivacyOptionsRequirementStatus
               == PrivacyOptionsRequirementStatus.Required;

        public async Task<AdPrivacySignals> ResolveAsync(CancellationToken cancellation = default)
        {
            // TagForUnderAgeOfConsent follows the app's own COPPA classification rather than a
            // separate switch, so the two can never disagree. See AdPrivacy.ChildDirected.
            var request = new ConsentRequestParameters
            {
                TagForUnderAgeOfConsent = AdPrivacy.ChildDirected,
            };

            // Only ever set in a development build, and only when a test device is listed —
            // ConsentDebug is compiled out entirely otherwise, so a store build has no debug
            // settings to attach. Without this the form cannot be seen from outside the EEA,
            // which means it cannot be tested at all from here. See ConsentDebug.
            if (ConsentDebug.IsActive)
            {
                var devices = new List<string>(ConsentDebug.Devices);

                request.ConsentDebugSettings = new ConsentDebugSettings
                {
                    DebugGeography = DebugGeography.EEA,
                    TestDeviceHashedIds = devices,
                };

                Debug.LogWarning($"[Privacy] consent debug is ON — forcing EEA for " +
                                 $"{devices.Count} test device(s). This cannot ship: the whole " +
                                 "block is compiled out of a release build.");

                // Cached state beats a forced geography, every time. UMP stores its decision on
                // the device, so the first launch on a Turkish network writes NotRequired and
                // every later run is answered from that — the override is applied, ignored, and
                // nothing in any log says why. Clearing it is what makes the debug geography
                // mean anything. Debug-only: resetting a real player would re-prompt somebody
                // who had already answered.
                if (ConsentDebug.ResetEachRun)
                {
                    ConsentInformation.Reset();
                    Debug.LogWarning("[Privacy] UMP consent state reset for testing");
                }
            }

            // A refresh that fails leaves the question open — Read() would say Unknown, and
            // Restricted says the same thing without pretending the SDK was consulted. Open is
            // what AdPrivacy needs to hear: it means Apple's prompt waits for a launch on which
            // the form can actually be shown first.
            if (!await Update(request, cancellation)) return AdPrivacySignals.Restricted;

            // Logged raw, before Read() folds them together. The two states that matter here
            // are indistinguishable afterwards: NotRequired means UMP placed this player
            // outside the EEA, while Required-but-no-form means it placed them inside and had
            // nothing published to show them. One is a geography problem and the other is a
            // console problem, and guessing which cost an evening once.
            Debug.Log($"[Privacy] UMP says status={ConsentInformation.ConsentStatus}, " +
                      $"canRequestAds={ConsentInformation.CanRequestAds()}, " +
                      $"privacyOptions={ConsentInformation.PrivacyOptionsRequirementStatus}");

            await ShowIfRequired(cancellation);

            Debug.Log($"[Privacy] after the form: status={ConsentInformation.ConsentStatus}, " +
                      $"canRequestAds={ConsentInformation.CanRequestAds()}, " +
                      $"privacyOptions={ConsentInformation.PrivacyOptionsRequirementStatus}");

            return Read();
        }

        public async Task<AdPrivacySignals> RevisitAsync(CancellationToken cancellation = default)
        {
            if (!CanRevisit) return Read();

            // The options form is opened by the player from Settings, so unlike the boot-path
            // form it is never in a race with Apple's prompt — the tracking answer was read
            // long ago. It still waits for the person rather than a clock, because a form that
            // is dismissed and then read back as "no change" has thrown their decision away.
            var dismissed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            ConsentForm.ShowPrivacyOptionsForm(error =>
            {
                if (error != null) Debug.LogWarning($"[Privacy] the privacy options form failed: {error.Message}");
                dismissed.TrySetResult(error == null);
            });

            await Dismissal(dismissed.Task, cancellation);

            return Read();
        }

        // ------------------------------------------------------------- the SDK
        static async Task<bool> Update(ConsentRequestParameters request, CancellationToken cancellation)
        {
            var updated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            ConsentInformation.Update(request, error =>
            {
                // Ordinary on a train, and not worth an error: the player keeps whatever UMP
                // last stored, which on a first launch is nothing and therefore no consent.
                if (error != null) Debug.Log($"[Privacy] consent info could not be refreshed: {error.Message}");
                updated.TrySetResult(error == null);
            });

            return await Bounded(updated.Task, "consent info", cancellation);
        }

        /// <summary>
        /// Shows the form only where one is owed. Outside the EEA and the UK UMP answers
        /// <c>NotRequired</c> and this returns having drawn nothing, which is why no geography
        /// check of ours appears anywhere in this file.
        ///
        /// <para>
        /// Deliberately not <c>LoadAndShowConsentFormIfRequired</c>, which the SDK offers and
        /// the first version used: its one callback fires when the form is <em>dismissed</em>,
        /// so there is no moment at which a caller can tell "still fetching" from "on screen,
        /// being read" — and those two want opposite treatment. Loading and showing as two
        /// calls is what makes the load bounded and the show not.
        /// </para>
        /// </summary>
        static async Task ShowIfRequired(CancellationToken cancellation)
        {
            if (ConsentInformation.ConsentStatus != GoogleMobileAds.Ump.Api.ConsentStatus.Required) return;

            if (!ConsentInformation.IsConsentFormAvailable())
            {
                // The console problem named above: UMP has placed this player inside the EEA
                // and has nothing published to show them. Nothing to wait for; the question
                // stays open and Apple's prompt stays unasked, which is the right pairing —
                // with no GDPR consent there is nothing lawful to do with a device id anyway.
                Debug.LogWarning("[Privacy] UMP requires a consent form and has none to show; " +
                                 "nothing is published for this app in the AdMob console. " +
                                 "Running unpersonalised.");
                return;
            }

            var form = await LoadForm(cancellation);
            if (form == null) return;

            await Show(form, cancellation);
        }

        /// <summary>
        /// Fetches the form, bounded by <see cref="TimeoutMilliseconds"/>. Returns null on a
        /// failure, a timeout or a cancellation — and a form that arrives after the timeout is
        /// left in the completed task and never shown, because by then the caller has moved
        /// on and "shown late" is the exact sequence Apple refused.
        /// </summary>
        static async Task<ConsentForm> LoadForm(CancellationToken cancellation)
        {
            var loaded = new TaskCompletionSource<ConsentForm>(TaskCreationOptions.RunContinuationsAsynchronously);

            ConsentForm.Load((form, error) =>
            {
                if (error != null) Debug.LogWarning($"[Privacy] the consent form failed to load: {error.Message}");
                loaded.TrySetResult(error == null ? form : null);
            });

            var timeout = Task.Delay(TimeoutMilliseconds, cancellation);
            var finished = await Task.WhenAny(loaded.Task, timeout);

            if (finished != loaded.Task)
            {
                Debug.LogWarning("[Privacy] the consent form did not load in time; running " +
                                 "unpersonalised. A form arriving later is dropped, not shown.");
                return null;
            }

            return await loaded.Task;
        }

        /// <summary>
        /// Puts a loaded form on screen and waits for the person to answer it. No timeout,
        /// deliberately: the only thing that can end this is their tap, or the process being
        /// cancelled out from under it. Nothing else in the game waits on this — mediation and
        /// measurement start when it returns, which is the order they are required to start
        /// in, and the splash never did wait.
        /// </summary>
        static async Task Show(ConsentForm form, CancellationToken cancellation)
        {
            var dismissed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            form.Show(error =>
            {
                // A show that fails (no window to present into, a form already consumed) calls
                // back at once with the error, so this cannot hang on a form nobody can see.
                if (error != null) Debug.LogWarning($"[Privacy] the consent form failed to show: {error.Message}");
                dismissed.TrySetResult(error == null);
            });

            await Dismissal(dismissed.Task, cancellation);
        }

        /// <summary>
        /// Awaits a network callback, or gives up. Returns false on a timeout or a cancellation.
        ///
        /// A callback that never fires is the failure mode worth defending against here: it is
        /// indistinguishable from a slow one, and the difference between the two is a game
        /// that starts and a game that does not. Only ever wrapped round a call whose other
        /// end is a server — never round a form, see <see cref="Show"/>.
        /// </summary>
        static async Task<bool> Bounded(Task<bool> work, string what, CancellationToken cancellation)
        {
            var timeout = Task.Delay(TimeoutMilliseconds, cancellation);
            var finished = await Task.WhenAny(work, timeout);

            if (finished != work)
            {
                Debug.LogWarning($"[Privacy] the {what} call did not answer in time; running unpersonalised");
                return false;
            }

            return await work;
        }

        /// <summary>
        /// Awaits a form's dismissal with no clock on it, honouring only cancellation. The
        /// registration completes the wait rather than throwing, because a cancelled boot is
        /// not an error in the consent flow — it is the process going away.
        /// </summary>
        static async Task Dismissal(Task<bool> dismissed, CancellationToken cancellation)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (cancellation.Register(() => cancelled.TrySetResult(false)))
            {
                await Task.WhenAny(dismissed, cancelled.Task);
            }
        }

        /// <summary>
        /// Reads UMP's state back as the coarse signals mediation consumes.
        ///
        /// <para>
        /// <b>Two questions, and they are not the same one.</b> Whether the law applies is
        /// <see cref="PrivacyOptionsRequirementStatus"/> — UMP only requires an ongoing
        /// privacy control where a jurisdiction demands one, which is precisely the EEA and
        /// the UK, so it is a better answer than any geography we could look up ourselves.
        /// Whether the player agreed is <see cref="ConsentInformation.CanRequestAds"/> against
        /// an <see cref="ConsentStatus.Obtained"/> status.
        /// </para>
        /// <para>
        /// <b>The honest limit of this reading.</b> "Can request ads" is coarser than "agreed
        /// to personalisation": a player who consents to storage but refuses profiling
        /// satisfies it. The exact per-purpose truth lives in the TCF string, and the networks
        /// that care read the string rather than this boolean — so being coarse here costs
        /// nothing that matters and buys not shipping a TCF parser in a game client. Where it
        /// errs it errs towards <em>less</em> personalisation than the player allowed, never
        /// more.
        /// </para>
        /// <para>
        /// <b>An <see cref="ConsentStatus.Unknown"/> here means the question is still open</b>
        /// — a form owed and not yet answered — and <see cref="AdPrivacy.ResolveAsync"/> reads
        /// it as "do not ask Apple yet". <c>Required</c> after this method is exactly that
        /// state: the form failed to load, failed to show, or was never published.
        /// </para>
        /// </summary>
        static AdPrivacySignals Read()
        {
            bool applies = ConsentInformation.PrivacyOptionsRequirementStatus
                           == PrivacyOptionsRequirementStatus.Required;

            var status = ConsentInformation.ConsentStatus;

            ConsentStatus consent =
                status == GoogleMobileAds.Ump.Api.ConsentStatus.Obtained
                    ? (ConsentInformation.CanRequestAds() ? ConsentStatus.Granted : ConsentStatus.Denied)
                : status == GoogleMobileAds.Ump.Api.ConsentStatus.NotRequired
                    ? ConsentStatus.Granted
                    : ConsentStatus.Unknown;

            // UMP is a GDPR instrument and holds no opinion about a US "do not sell" right.
            // Reported false rather than guessed: the CCPA signal is a separate obligation and
            // claiming an opt-out nobody made would suppress ads for every American player.
            // When a US privacy flow is added it belongs beside this, not inside it.
            return new AdPrivacySignals(applies, consent, doNotSell: false,
                                        childDirected: AdPrivacy.ChildDirected,
                                        tracking: TrackingStatus.NotDetermined);
        }
    }
}
#endif
