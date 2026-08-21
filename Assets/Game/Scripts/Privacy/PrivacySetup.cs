using UnityEngine;

namespace GlimmerGrove.Privacy
{
    /// <summary>
    /// Chooses the consent gateway and the tracking prompt for this build.
    ///
    /// <para>
    /// One call for <c>Boot</c>, for the reason <c>AdConfig.AdUnits</c> is one call: the
    /// alternative is a <c>#if</c> ladder in the boot path, which is where a per-platform
    /// mistake is least visible and most expensive. Everything conditional lives here.
    /// </para>
    /// <para>
    /// Note that this installs but does not <em>resolve</em>. Asking the player anything is a
    /// network round trip and possibly a native dialog, so it happens from the splash — see
    /// <c>RewardedAds.StartAsync</c>, which owns the ordering between consent and mediation.
    /// </para>
    /// </summary>
    public static class PrivacySetup
    {
        public static void Install()
        {
            AdPrivacy.Install(Gateway());
            AdPrivacy.Install(new AppTrackingPrompt());
        }

        /// <summary>
        /// The CMP, when one is compiled in.
        ///
        /// Falls back to <see cref="NullConsentGateway"/>, which answers "no consent, and
        /// assume the GDPR applies". That is deliberately the restrictive reading: a build
        /// without a CMP shows unpersonalised ads and earns less, where the permissive guess
        /// would serve personalised ads to people who were never asked.
        /// </summary>
        static IConsentGateway Gateway()
        {
#if GLIMMER_UMP
            return new UmpConsentGateway();
#else
            Debug.Log("[Privacy] no consent platform is installed; ads run unpersonalised");
            return new NullConsentGateway();
#endif
        }
    }
}
