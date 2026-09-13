#if GLIMMER_APPSFLYER
using System;
using AppsFlyerSDK;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using GlimmerGrove.Privacy;
using UnityEngine;

namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// The attribution half of <see cref="AnalyticsSetup"/>.
    ///
    /// <para>
    /// <b>Initialised at boot and started only once consent has resolved</b>, which is the
    /// vendor's own required order and not a house preference: consent data set after the
    /// start is consent that arrived too late for the install it was about. The two steps are
    /// separate calls for exactly that reason, so the gap between them is where the answer is
    /// waited for.
    /// </para>
    /// <para>
    /// <b>Nothing is ever blocked on the network.</b> The SDK is told to start as soon as the
    /// gateway answers, which happens on the splash — not when a sign-in completes, and not
    /// when a save syncs. An install reported late is an install attributed to nobody, and a
    /// sign-in is the one step here that can fail on a device with no Play Services.
    /// </para>
    /// </summary>
    public static partial class AnalyticsSetup
    {
        static AppsFlyerSink _attribution;
        static bool _started;
        static bool _identified;

        static partial void InstallAttribution()
        {
            // Two gates, exactly as Boot applies to the ad provider: the SDK has to be
            // compiled in *and* a real dev key has to exist. Without the second, the SDK
            // starts happily and reports every install into nobody's account — a campaign
            // that spends against a dashboard which stays empty, with nothing anywhere
            // saying why.
            if (!AttributionConfig.IsConfigured)
            {
                Debug.Log("[Attribution] no dev key in this build; attribution is dark");
                return;
            }

            _attribution = new AppsFlyerSink();
            Telemetry.AddSink(_attribution);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AppsFlyer.setIsDebug(true);
#endif

            // Null rather than a MonoBehaviour, because that argument exists to receive
            // conversion-data and deep-link callbacks and this build asks for neither. Passing
            // an object to be called back on would mean keeping one alive for the life of the
            // app to service messages nothing reads.
            AppsFlyer.initSDK(AttributionConfig.Key, AttributionConfig.AppId, null);

            AdPrivacy.Changed += StartAttribution;
            if (AdPrivacy.IsResolved) StartAttribution(AdPrivacy.Signals);

            // The join between an advert and an account. Set after the start rather than
            // before it, because AppsFlyer's wait-for-id mode holds the whole install back
            // until an id exists — which puts attribution behind an anonymous sign-in that
            // can fail, to buy a link that is just as correct arriving a few seconds later.
            CloudSaveService.Synced += Identify;
        }

        /// <summary>
        /// Starts the SDK once, as soon as consent is known.
        ///
        /// <para>
        /// Idempotent because it is called from two places that race by design: the gateway
        /// may have resolved before this assembly was installed, or seconds afterwards.
        /// </para>
        /// <para>
        /// <b>The signals are taken from the event rather than read back off
        /// <c>AdPrivacy</c>.</b> <c>ResolveAsync</c> raises <c>Changed</c> and sets
        /// <c>IsResolved</c> on the line <em>after</em> it, so a handler that asks the flag is
        /// told the answer has not arrived — during the one call that carries it. Both halves
        /// of this file did exactly that and both failed the same silent way: attribution was
        /// initialised and never started, and analytics collection stayed off for the life of
        /// the session, with the SDK logs showing a healthy startup either way. What made it
        /// invisible is that the miss leaves no trace at all — there is no error, no retry and
        /// no second event, because consent is answered once.
        /// </para>
        /// </summary>
        static void StartAttribution(AdPrivacySignals signals)
        {
            if (_started) return;
            _started = true;

            try
            {
                bool allowed = signals.AllowsPersonalisation;

                // The same reading the analytics half takes, for the same reason: this project
                // has one consent answer, so the conservative mapping is the only honest one.
                AppsFlyer.setConsentData(signals.GdprApplies
                    ? AppsFlyerConsent.ForGDPRUser(allowed, allowed)
                    : AppsFlyerConsent.ForNonGDPRUser());

                // Belt as well as braces. Consent data tells the vendor what may be shared
                // onward; anonymising tells it not to build a device identity in the first
                // place, which is the half that matters if the first call is ever mishandled.
                if (!allowed) AppsFlyer.anonymizeUser(true);

                AppsFlyer.startSDK();
                _attribution.Started = true;

                Debug.Log($"[Attribution] started ({(allowed ? "identified" : "anonymised")})");
            }
            catch (Exception e)
            {
                // Never fatal, for the analytics half's reason: a lost measurement is worth
                // strictly less than a session.
                Debug.LogWarning("[Attribution] failed to start: " + e.Message);
            }
        }

        /// <summary>
        /// Tells the vendor which account this install turned into, once and once only.
        ///
        /// <para>
        /// <c>Synced</c> rather than <c>Settled</c>: the narrower event is about a save having
        /// reached the server (invariant 19j), and what is wanted here is the earliest moment
        /// an id exists at all. It fires on a switch and a link as well as a sync, which is
        /// why the latch is on the id having been sent rather than on the event.
        /// </para>
        /// <para>
        /// Not re-sent when the account changes, deliberately. An install belongs to the
        /// account it produced; re-pointing it at whoever signed in later would move a
        /// campaign's credit onto a different player every time somebody switched.
        /// </para>
        /// </summary>
        static void Identify()
        {
            if (_identified || !_started) return;

            string uid = CloudState.UserId;
            if (string.IsNullOrEmpty(uid)) return;

            _identified = true;

            try
            {
                AppsFlyer.setCustomerUserId(uid);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Attribution] could not set the account id: " + e.Message);
            }
        }
    }
}
#endif
