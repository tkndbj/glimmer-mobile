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
    /// </summary>
    public sealed class UmpConsentGateway : IConsentGateway
    {
        /// <summary>
        /// How long to wait for Google's servers before giving up and running unpersonalised.
        ///
        /// A boot path may not wait indefinitely on a network call — the failure a player sees
        /// would be a splash screen that never ends, which is worse than any amount of lost ad
        /// revenue. Fifteen seconds is far beyond a healthy round trip and far short of a
        /// player deciding the game is broken.
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

            if (!await Update(request, cancellation)) return AdPrivacySignals.Restricted;

            // Loads and shows the form only where one is owed — outside the EEA and the UK
            // this returns immediately having drawn nothing, which is why no geography check
            // of ours appears anywhere in this file. A failure here is not fatal: the player
            // simply has not consented, which is the state they were already in.
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

            var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            ConsentForm.ShowPrivacyOptionsForm(error =>
            {
                if (error != null) Debug.LogWarning($"[Privacy] the privacy options form failed: {error.Message}");
                opened.TrySetResult(error == null);
            });

            await Wait(opened.Task, cancellation);

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

            return await Wait(updated.Task, cancellation);
        }

        static async Task ShowIfRequired(CancellationToken cancellation)
        {
            var shown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            ConsentForm.LoadAndShowConsentFormIfRequired(error =>
            {
                if (error != null) Debug.LogWarning($"[Privacy] the consent form failed: {error.Message}");
                shown.TrySetResult(error == null);
            });

            await Wait(shown.Task, cancellation);
        }

        /// <summary>
        /// Awaits an SDK callback, or gives up. Returns false on a timeout or a cancellation.
        ///
        /// A callback that never fires is the failure mode worth defending against here: it is
        /// indistinguishable from a slow one, and the difference between the two is a game
        /// that starts and a game that does not.
        /// </summary>
        static async Task<bool> Wait(Task<bool> work, CancellationToken cancellation)
        {
            var timeout = Task.Delay(TimeoutMilliseconds, cancellation);
            var finished = await Task.WhenAny(work, timeout);

            if (finished != work)
            {
                Debug.LogWarning("[Privacy] the consent SDK did not answer in time; running unpersonalised");
                return false;
            }

            return await work;
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
