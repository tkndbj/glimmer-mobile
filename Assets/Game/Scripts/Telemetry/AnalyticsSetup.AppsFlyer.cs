#if GLIMMER_APPSFLYER
using System;
using System.Collections.Generic;
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
    /// <b>Initialised at boot and started only once consent is known</b>, which is the
    /// vendor's own required order and not a house preference: consent data set after the
    /// start is consent that arrived too late for the install it was about. The two steps are
    /// separate calls for exactly that reason, so the gap between them is where the answer is
    /// waited for - by <see cref="MeasurementGate"/>, which opens Firebase on the same clock
    /// (invariant 55b). It starts on the committed answer; or anonymised, as a GDPR player, when
    /// UMP has said nothing at all for three seconds; and a new EEA player whose form is owed
    /// waits for the form, because an install reported anonymised can never be attributed.
    /// Events raised meanwhile are held by <see cref="AttributionStart"/> and sent after the
    /// start, under the consent the start applied.
    /// </para>
    /// <para>
    /// <b>Nothing is ever blocked on the network.</b> The SDK is told to start as soon as the
    /// gateway answers - on the splash wherever no consent form is owed, otherwise once the
    /// form has been answered on the hub (invariant 55) - not when a sign-in completes, and not
    /// when a save syncs. On iOS the install is reported without waiting for Apple's prompt,
    /// which now comes on the hub after the tutorial: holding the install for it would lose
    /// every player who left during the tutorial, and the SDK reads the advertising id on
    /// later events once the player has allowed it. An install reported late is an install attributed to nobody, and a
    /// sign-in is the one step here that can fail on a device with no Play Services.
    /// </para>
    /// </summary>
    public static partial class AnalyticsSetup
    {
        static AppsFlyerSink _attribution;
        static AttributionStart _starter;
        static bool _started;
        static bool _startFailed;
        static bool _identified;

        static partial void InstallAttribution()
        {
            // Two gates, exactly as Boot applies to the ad provider: the SDK has to be
            // compiled in *and* a real dev key has to exist. Without the second, the SDK
            // starts happily and reports every install into nobody's account - a campaign
            // that spends against a dashboard which stays empty, with nothing anywhere
            // saying why.
            if (!AttributionConfig.IsConfigured)
            {
                Debug.Log("[Attribution] no dev key in this build; attribution is dark");
                return;
            }

            _starter = new AttributionStart(new AppsFlyerSdk());
            _attribution = new AppsFlyerSink(_starter);
            Telemetry.AddSink(_attribution);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AppsFlyer.setIsDebug(true);
#endif

            // Null rather than a MonoBehaviour, because that argument exists to receive
            // conversion-data and deep-link callbacks and this build asks for neither. Passing
            // an object to be called back on would mean keeping one alive for the life of the
            // app to service messages nothing reads.
            AppsFlyer.initSDK(AttributionConfig.Key, AttributionConfig.AppId, null);

            // Started by AnalyticsSetup's one privacy subscription (AttributionPrivacyChanged) or,
            // without an answer, by the gate's time-out (TickAttribution).

            // The join between an advert and an account. Set after the start rather than
            // before it, because AppsFlyer's wait-for-id mode holds the whole install back
            // until an id exists - which puts attribution behind an anonymous sign-in that
            // can fail, to buy a link that is just as correct arriving a few seconds later.
            CloudSaveService.Synced += Identify;
        }

        /// <summary>
        /// Every committed answer: starts the SDK on the first, and re-applies consent on any later
        /// change (the hub's form after a time-out start, a privacy-options revisit), so what the
        /// vendor is told follows the player's answer in both directions.
        ///
        /// <para>
        /// <b>The signals are taken from the event rather than read back off
        /// <c>AdPrivacy</c>.</b> A commit raises <c>Changed</c> and sets
        /// <c>IsResolved</c> on the line <em>after</em> it, so a handler that asks the flag is
        /// told the answer has not arrived - during the one call that carries it. Both halves
        /// of this file did exactly that and both failed the same silent way: attribution was
        /// initialised and never started, and analytics collection stayed off for the life of
        /// the session, with the SDK logs showing a healthy startup either way. What made it
        /// invisible is that the miss leaves no trace at all - there is no error, no retry and
        /// no second event, because consent is answered once.
        /// </para>
        /// </summary>
        static partial void AttributionPrivacyChanged(AdPrivacySignals signals)
        {
            if (_starter == null) return;
            StartAttribution(AttributionConsent.For(signals));
        }

        /// <summary>The gate's time-out: UMP has said nothing, so start anonymised rather than never.</summary>
        static partial void TickAttribution()
        {
            if (_starter == null || _started || _startFailed) return;
            if (_gate.AttributionMayStart(out var consent)) StartAttribution(consent);
        }

        static void StartAttribution(AttributionConsent consent)
        {
            if (_startFailed) return;

            bool first = !_started;
            var before = _starter.Applied;
            try
            {
                _starter.Start(consent);
                _started = true;

                if (first)
                    Debug.Log($"[Attribution] started ({(consent.Anonymised ? "anonymised" : "identified")}" +
                              $"{(_gate.IsAnswered ? "" : ", before the consent answer")}); dropped before start={_starter.Dropped}");
                else if (!before.Equals(_starter.Applied))
                    Debug.Log($"[Attribution] consent updated ({(consent.Anonymised ? "anonymised" : "identified")})");
            }
            catch (Exception e)
            {
                // Never fatal, for the analytics half's reason: a lost measurement is worth
                // strictly less than a session. Not retried every frame either.
                if (first) _startFailed = true;
                Debug.LogWarning("[Attribution] failed to start: " + e.Message);
            }
        }

        /// <summary>
        /// The vendor's calls, in the order <see cref="AttributionStart"/> makes them: consent data
        /// and anonymisation first, then the start, then whatever was held.
        /// </summary>
        sealed class AppsFlyerSdk : IAttributionSdk
        {
            readonly Dictionary<string, string> _values = new Dictionary<string, string>(4);

            public void ApplyConsent(AttributionConsent consent)
            {
                AppsFlyer.setConsentData(consent.GdprApplies
                    ? AppsFlyerConsent.ForGDPRUser(consent.Allowed, consent.Allowed)
                    : AppsFlyerConsent.ForNonGDPRUser());

                // Belt as well as braces. Consent data tells the vendor what may be shared
                // onward; anonymising tells it not to build a device identity in the first
                // place, which is the half that matters if the first call is ever mishandled.
                // Set both ways, because a later answer may lift it as well as impose it.
                AppsFlyer.anonymizeUser(consent.Anonymised);
            }

            public void Start() => AppsFlyer.startSDK();

            public void Send(string name, IReadOnlyDictionary<string, string> values)
            {
                _values.Clear();
                if (values != null) foreach (var pair in values) _values[pair.Key] = pair.Value;
                AppsFlyer.sendEvent(name, _values);
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
