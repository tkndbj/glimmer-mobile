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

        static partial void InstallFirebase()
        {
            if (_firebase != null) return;
            _firebase = new FirebaseAnalyticsSink();
            Telemetry.AddSink(_firebase);
            _adRevenue = new AdRevenueTracker(_firebase, AdRevenueSources.ForCurrentPlatform(), DiagnoseAdRevenue);
            AdRevenue.Install(_adRevenue);

            AdPrivacy.Changed += ApplyCollectionRule;
            if (AdPrivacy.IsResolved) ApplyCollectionRule(AdPrivacy.Signals);
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
                _firebase.MarkReady();
            }
            catch (Exception)
            {
                // No exception message: native failures can include configuration or device data.
                Debug.LogWarning("[Analytics] Firebase initialization failed; delivery remains closed");
            }
        }

        static void ApplyCollectionRule(AdPrivacySignals signals)
        {
            // Existing product eligibility is intentionally retained pending approval of an
            // analytics-specific consent integration. This advertising flag is NOT evidence of
            // analytics_storage permission. Delivery must never infer consent from it itself.
            // Read the event's value: AdPrivacy sets IsResolved after raising Changed.
            bool allowed = signals.AllowsPersonalisation;
            _adRevenue.SetCollectionAllowed(allowed);
            _firebase.SetCollectionAllowed(allowed);
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
