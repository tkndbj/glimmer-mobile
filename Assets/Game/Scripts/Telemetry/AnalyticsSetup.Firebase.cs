#if GLIMMER_ANALYTICS
using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Analytics;
using GlimmerGrove.Privacy;
using UnityEngine;

namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// The Google Analytics for Firebase half of <see cref="AnalyticsSetup"/>.
    ///
    /// <para>
    /// Collection starts <em>off</em> and is turned on only once the consent gateway has
    /// answered and the answer allows it. That is the restrictive reading on purpose and it
    /// matches the gateway's own default (<c>AdPrivacySignals.Restricted</c> assumes the GDPR
    /// applies): a build that guessed permissively would measure people who were never asked,
    /// which is the one failure here that cannot be repaired by deleting rows afterwards.
    /// </para>
    /// <para>
    /// <b>The window this does not close.</b> Firebase enables collection itself the moment
    /// the native SDK initialises, which can be marginally before this runs. Closing that
    /// properly needs <c>firebase_analytics_collection_enabled=false</c> in the Android
    /// manifest and <c>FIREBASE_ANALYTICS_COLLECTION_ENABLED</c> in the iOS plist, so the SDK
    /// starts disabled and this only ever turns it on. Until those are set, treat EEA
    /// measurement as unproven rather than as consented.
    /// </para>
    /// </summary>
    public static partial class AnalyticsSetup
    {
        static FirebaseAnalyticsSink _firebase;
        static bool _collecting;

        static partial void InstallFirebase()
        {
            _firebase = new FirebaseAnalyticsSink();
            Telemetry.AddSink(_firebase);

            // Consent can be answered before or after the SDK resolves, so both paths lead
            // to the same place rather than one assuming it runs second.
            AdPrivacy.Changed += ApplyConsent;
            if (AdPrivacy.IsResolved) ApplyConsent(AdPrivacy.Signals);

            _ = StartFirebaseAsync();
        }

        static async Task StartFirebaseAsync()
        {
            try
            {
                // The same call the cloud backend makes. It is safe to make twice — it
                // resolves once and every later caller is handed the settled answer — and
                // making it here rather than waiting on the cloud is deliberate: analytics
                // must not stop working on a device that has no Play Services for Firestore,
                // and must not be ordered behind a sign-in that can fail.
                var status = await FirebaseApp.CheckAndFixDependenciesAsync();
                if (status != DependencyStatus.Available)
                {
                    Debug.LogWarning($"[Analytics] Firebase unavailable on this device ({status}); events go nowhere");
                    return;
                }

                if (AdPrivacy.IsResolved) ApplyConsent(AdPrivacy.Signals);
                _firebase.MarkReady();
            }
            catch (Exception e)
            {
                // Never fatal. A missing measurement is worth strictly less than a session.
                Debug.LogWarning("[Analytics] Firebase failed to initialise: " + e.Message);
            }
        }

        /// <summary>
        /// Turns collection on or off to match the current signals.
        ///
        /// <para>
        /// Gated on personalisation rather than on a signal of its own, because this project
        /// has one consent answer and no analytics-specific question in it. That is the
        /// conservative mapping — it will refuse measurement in the EEA for anyone who
        /// declines ads — and the right fix when EEA numbers are wanted is a second signal
        /// from the CMP, never a looser reading of this one.
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
        static void ApplyConsent(AdPrivacySignals signals)
        {
            bool allowed = signals.AllowsPersonalisation;
            if (allowed == _collecting) return;

            try
            {
                FirebaseAnalytics.SetAnalyticsCollectionEnabled(allowed);
                _collecting = allowed;
                Debug.Log($"[Analytics] collection {(allowed ? "enabled" : "disabled")}");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Analytics] could not set collection state: " + e.Message);
            }
        }
    }
}
#endif
