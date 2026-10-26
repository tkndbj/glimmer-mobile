#if GLIMMER_ANALYTICS && GLIMMER_FIREBASE
using System;
using System.Threading.Tasks;
using Firebase;
using GlimmerGrove.Cloud;
using GlimmerGrove.Privacy;
using UnityEngine;

namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// Installs Firebase independently of mediation. All native calls wait for the shared dependency
    /// check; delivery then applies native collection state explicitly, including the initial false.
    /// </summary>
    public static partial class AnalyticsSetup
    {
        static FirebaseAnalyticsSink _firebase;
        static AdRevenueTracker _adRevenue;

        /// <summary>What Analytics is told now, derived from the latest privacy answer.</summary>
        static AnalyticsConsent _consent;

        /// <summary>Whether the native SDK has passed its dependency check and may be called.</summary>
        static bool _sdkReady;

        /// <summary>The last consent handed to the SDK, so an unchanged answer is not re-sent.</summary>
        static AnalyticsConsent? _applied;

        static partial void InstallFirebase()
        {
            if (_firebase != null) return;
            _firebase = new FirebaseAnalyticsSink();
            Telemetry.AddSink(_firebase);
            _adRevenue = new AdRevenueTracker(_firebase, AdRevenueSources.ForCurrentPlatform(), DiagnoseAdRevenue);
            AdRevenue.Install(_adRevenue);

            // Applied at once, from the restrictive answer if nothing is resolved yet: collection
            // on, every purpose denied, so the launch is measured without identifying anybody.
            AdPrivacy.Changed += ApplyConsentRule;
            ApplyConsentRule(AdPrivacy.IsResolved ? AdPrivacy.Signals : AdPrivacySignals.Restricted);
            _ = StartFirebaseAsync();
        }

        static async Task StartFirebaseAsync()
        {
            try
            {
                var status = await FirebaseReady.EnsureAsync();
                if (status != DependencyStatus.Available)
                {
                    Debug.LogWarning("[Analytics] Firebase dependencies unavailable; delivery remains closed");
                    return;
                }
                // Consent before the first event: the SDK is told what it may carry, and only
                // then does delivery open.
                _sdkReady = true;
                PushConsent();
                _firebase.MarkReady();
            }
            catch (Exception)
            {
                // No exception message: native failures can include configuration or device data.
                Debug.LogWarning("[Analytics] Firebase initialization failed; delivery remains closed");
            }
        }

        /// <summary>
        /// Google's consent mode (invariant 55a): collection stays on and each purpose says what
        /// it may carry (<see cref="AnalyticsConsent.For"/>). Before the player has answered, or
        /// where they denied it, <c>analytics_storage</c> is denied and Analytics sends events
        /// without identifiers for modelling; a granted answer makes them full events. Nothing is
        /// inferred from the advertising answer - the purposes come from UMP's own
        /// <c>UMP_consentModeValues</c>, so the console's consent mode setting decides them.
        /// </summary>
        static void ApplyConsentRule(AdPrivacySignals signals)
        {
            _consent = AnalyticsConsent.For(signals);

            _adRevenue.SetCollectionAllowed(_consent.Collect);
            _firebase.SetCollectionAllowed(_consent.Collect);

            PushConsent();
        }

        static void PushConsent()
        {
            if (!_sdkReady) return;
            if (_applied.HasValue && _applied.Value.Equals(_consent)) return;

            if (_firebase.ApplyConsent(_consent))
            {
                _applied = _consent;
                Debug.Log($"[Analytics] consent mode applied: {_consent}");
            }
            else
            {
                Debug.LogWarning("[Analytics] consent mode could not be applied; the denied defaults stand");
            }
        }

        static partial void TickFirebase()
        {
            _adRevenue?.Pump();
            _firebase?.Tick();
        }

        static void DiagnoseAdRevenue(AdRevenueIssue issue, long count)
        {
            string message = $"[Analytics] ad revenue {issue}; count={count}";
            if (issue == AdRevenueIssue.UnverifiedSource)
            {
                // These are bounded dashboard configuration fields, not personal identifiers.
                // They let an unknown SDK label be reconciled without logging raw ILR JSON.
                var source = _adRevenue.LastUnverifiedSource;
                message += $"; network={DiagnosticLabel(source.Network)}; instance={DiagnosticLabel(source.InstanceName)}"
                    + $"; instanceId={DiagnosticLabel(source.InstanceId)}; unit={DiagnosticLabel(source.AdUnitId)}";
            }
            Debug.Log(message);
        }

        static string DiagnosticLabel(string value) => value?.Replace('\r', ' ').Replace('\n', ' ').Replace(';', ' ');
    }
}
#endif
