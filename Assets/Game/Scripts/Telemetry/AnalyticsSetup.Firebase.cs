#if GLIMMER_ANALYTICS && GLIMMER_FIREBASE
using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Analytics;
using GlimmerGrove.Cloud;
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
    /// <b>Nothing here may touch the SDK until the dependency check has settled</b>, and that is
    /// not a tidiness rule. Firebase refuses <em>any</em> call made while
    /// <c>CheckAndFixDependenciesAsync</c> is in flight, and the refusal lands on whoever else
    /// was starting up — this file once broke sign-in and leaderboards outright, and the error
    /// it produced named the cloud backend. So the check is shared through
    /// <see cref="FirebaseReady"/>, and the consent answer is <em>held</em> until it returns
    /// rather than applied when it arrives.
    /// </para>
    /// <para>
    /// <b>The window this does not close.</b> Firebase enables collection itself the moment
    /// the native SDK initialises, which is before any of this runs. Closing that properly needs
    /// <c>firebase_analytics_collection_enabled=false</c> in the Android manifest and
    /// <c>FIREBASE_ANALYTICS_COLLECTION_ENABLED</c> in the iOS plist, so the SDK starts disabled
    /// and this only ever turns it on. Until those are set, treat EEA measurement as unproven
    /// rather than as consented.
    /// </para>
    /// </summary>
    public static partial class AnalyticsSetup
    {
        static FirebaseAnalyticsSink _firebase;

        /// <summary>What the SDK has been told, and what it should be told.</summary>
        static bool _collecting;
        static bool _wanted;
        static bool _answered;
        static bool _ready;

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
                // Shared with the cloud backend rather than made again here. See FirebaseReady:
                // two concurrent checks is not a wasted call, it is a failed initialisation for
                // whichever subsystem asked second.
                var status = await FirebaseReady.EnsureAsync();
                if (status != DependencyStatus.Available)
                {
                    Debug.LogWarning($"[Analytics] Firebase unavailable on this device ({status}); events go nowhere");
                    return;
                }

                _ready = true;

                // Whatever the player answered while the check was running, applied now.
                Push();
                _firebase.MarkReady();
            }
            catch (Exception e)
            {
                // Never fatal. A missing measurement is worth strictly less than a session.
                Debug.LogWarning("[Analytics] Firebase failed to initialise: " + e.Message);
            }
        }

        /// <summary>
        /// Records what the player has agreed to, and applies it if the SDK is ready.
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
            _wanted = signals.AllowsPersonalisation;
            _answered = true;

            Push();
        }

        /// <summary>
        /// Tells the SDK, once there is something to say and something to say it to.
        ///
        /// <para>
        /// The guard on <see cref="_ready"/> is the load-bearing one: consent resolves on the
        /// splash, which is squarely inside the dependency check, so this is called during it
        /// on an ordinary launch rather than as a rare race.
        /// </para>
        /// </summary>
        static void Push()
        {
            if (!_ready || !_answered || _wanted == _collecting) return;

            try
            {
                FirebaseAnalytics.SetAnalyticsCollectionEnabled(_wanted);
                _collecting = _wanted;
                Debug.Log($"[Analytics] collection {(_wanted ? "enabled" : "disabled")}");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Analytics] could not set collection state: " + e.Message);
            }
        }
    }
}
#endif
