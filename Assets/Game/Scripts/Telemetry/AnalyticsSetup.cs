using UnityEngine;

namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// Chooses the measurement platforms for this build and holds them to the player's consent.
    ///
    /// <para>
    /// One call for <c>Boot</c>, for the reason <c>PrivacySetup.Install</c> is one call: the
    /// alternative is a <c>#if</c> ladder in the boot path, which is where a per-platform
    /// mistake is least visible and most expensive. Everything conditional lives here.
    /// </para>
    /// <para>
    /// <b>Two platforms, one assembly, because they are one concern.</b> Every other vendor
    /// here has an assembly to itself — <c>Ads</c>, <c>Cloud</c>, <c>Iap</c>, <c>Privacy</c> —
    /// and that reads like a rule about vendors when it is really a rule about concerns: each
    /// of those is the only supplier of the thing it supplies. Measurement is one concern with
    /// two suppliers answering different halves of it, and splitting them would give the boot
    /// path two calls that must always be made together.
    /// </para>
    /// <para>
    /// <b>Each half is a partial method, absent rather than empty when its package is not
    /// installed.</b> A call to an unimplemented partial is removed by the compiler, so the
    /// alternative — a body wrapped in <c>#if</c> — differs only in leaving a method that does
    /// nothing and a define that has to be spelled correctly in two files to say so.
    /// </para>
    /// </summary>
    public static partial class AnalyticsSetup
    {
        /// <summary>
        /// Attaches every sink this build has. Returns immediately: both SDKs resolve in the
        /// background, and neither is allowed to collect anything until the consent gateway
        /// has answered.
        /// </summary>
        public static void Install()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Telemetry.AddSink(new DebugAnalyticsSink());
#endif

            InstallFirebase();
            InstallAttribution();

#if !GLIMMER_ANALYTICS && !GLIMMER_APPSFLYER
            Debug.Log("[Analytics] no measurement platform is installed; events go nowhere");
#endif
        }

        /// <summary>Analytics: what people did once they were here.</summary>
        static partial void InstallFirebase();

        /// <summary>Attribution: which advert they came from.</summary>
        static partial void InstallAttribution();
    }
}
