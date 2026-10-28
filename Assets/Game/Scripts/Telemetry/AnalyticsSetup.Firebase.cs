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
    ///
    /// <para>
    /// <b>The order is UMP's answer, then <c>setConsent</c>, then collection on</b> (invariant 55b,
    /// <see cref="MeasurementGate"/>). The manifest and the plist start collection off with every
    /// purpose denied; Firebase logs <c>first_open</c> and <c>session_start</c> the moment collection
    /// turns on, so turning it on before the answer sent them as cookieless pings GA never reports
    /// (Android 1.1.4: no first opens at all). Events tracked meanwhile wait in
    /// <see cref="AnalyticsDelivery"/>'s bounded queue, as they always did for the dependency check.
    /// </para>
    /// </summary>
    public static partial class AnalyticsSetup
    {
        static FirebaseAnalyticsSink _firebase;
        static AdRevenueTracker _adRevenue;

        /// <summary>Consent before collection: what the SDK is told, and when delivery opens.</summary>
        static AnalyticsOpening _opening;

        static partial void InstallFirebase()
        {
            if (_firebase != null) return;
            _firebase = new FirebaseAnalyticsSink();
            Telemetry.AddSink(_firebase);
            _adRevenue = new AdRevenueTracker(_firebase, AdRevenueSources.ForCurrentPlatform(), DiagnoseAdRevenue);
            AdRevenue.Install(_adRevenue);

            // The restrictive answer until a real one: eligibility (not child-directed) lets events
            // queue now, and it is what the SDK is told if the gate times out before UMP speaks.
            var restricted = AnalyticsConsent.For(AdPrivacySignals.Restricted);
            _opening = new AnalyticsOpening(restricted, ApplyConsent, OpenDelivery);
            ApplyEligibility(restricted);
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
                // Consent may be pushed from here on; delivery opens only when the gate allows.
                _opening.SdkReady();
                TryOpen();
            }
            catch (Exception)
            {
                // No exception message: native failures can include configuration or device data.
                Debug.LogWarning("[Analytics] Firebase initialization failed; delivery remains closed");
            }
        }

        /// <summary>
        /// Google's consent mode: collection stays on and each purpose says what it may carry
        /// (<see cref="AnalyticsConsent.For"/>). Before the player has answered, or where they denied
        /// it, <c>analytics_storage</c> is denied and Analytics sends events without identifiers for
        /// modelling; a granted answer makes them full events. Nothing is inferred from the
        /// advertising answer - the purposes come from UMP's own <c>UMP_consentModeValues</c>.
        /// Every later answer (the hub's form, a privacy-options revisit) is pushed the same way.
        /// </summary>
        static partial void FirebasePrivacyChanged(AdPrivacySignals signals)
        {
            if (_opening == null) return;
            var consent = AnalyticsConsent.For(signals);
            ApplyEligibility(consent);
            _opening.Consent(consent);
            TryOpen();
        }

        static void ApplyEligibility(AnalyticsConsent consent)
        {
            _adRevenue.SetCollectionAllowed(consent.Collect);
            _firebase.SetCollectionAllowed(consent.Collect);
        }

        static void TryOpen()
        {
            if (!_opening.TryOpen(_gate.AnalyticsMayOpen)) return;
            Debug.Log(_gate.IsAnswered
                ? "[Analytics] opened on the consent answer"
                : "[Analytics] opened before the consent answer; every purpose denied until it arrives");
        }

        /// <summary>Crash reporting's half of the push (<c>AnalyticsSetup.Crashlytics</c>); absent without the package.</summary>
        static partial void CrashesConsent(AnalyticsConsent consent);

        static bool ApplyConsent(AnalyticsConsent consent)
        {
            // Crash reporting follows the same answer whether or not Analytics took it.
            CrashesConsent(consent);

            if (_firebase.ApplyConsent(consent))
            {
                Debug.Log($"[Analytics] consent mode applied: {consent}");
                return true;
            }
            Debug.LogWarning("[Analytics] consent mode could not be applied; the denied defaults stand");
            return false;
        }

        static void OpenDelivery() => _firebase.MarkReady();

        static partial void TickFirebase()
        {
            if (_opening != null && !_opening.IsOpen) TryOpen();
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
