namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// The attribution account's own identifiers: the dev key, and the App Store id iOS
    /// needs to recognise this app.
    ///
    /// <para>
    /// <c>AdConfig</c>'s shape and its reasoning, one concern along. These are not tuning —
    /// they are the address of the account a campaign's numbers arrive in, and putting them
    /// in <c>progression.json</c> would let a content push redirect somebody else's ad spend
    /// into this project's dashboard, or this project's into theirs.
    /// </para>
    /// <para>
    /// <b>One dev key for both platforms, unlike a mediation app key.</b> LevelPlay registers
    /// an Android app and an iOS app separately and issues a key for each, which is why
    /// <c>AdConfig</c> holds two; AppsFlyer issues one key per <em>account</em> and tells the
    /// platforms apart by the app id. Copying <c>AdConfig</c>'s two-key shape here would
    /// invent a distinction the vendor does not make, and the second key would be either a
    /// duplicate or wrong.
    /// </para>
    /// <para>
    /// <b>Why an attribution SDK at all, when there is already an analytics one.</b> They
    /// answer different questions and neither can answer the other's. Firebase says what
    /// people did once they were here; attribution says which advert they came from, which is
    /// the only thing that makes a cost-per-install comparable to a lifetime value. An ad
    /// network cannot optimise against a number it is never told, so a campaign run without
    /// this is buying clicks rather than players.
    /// </para>
    /// <para>
    /// <b>And why a measurement partner rather than each network's own SDK.</b> A network SDK
    /// attributes only its own installs, so a second network means a second SDK and two
    /// dashboards each claiming the same install. It is also the shape this project can
    /// actually carry: the partner ships as a UPM package, so it is gated by
    /// <c>versionDefines</c> like every other vendor here, where a <c>.unitypackage</c>
    /// unpacks as loose files under <c>Assets/</c>, carries no version, and would compile to
    /// nothing in silence — the exact failure <c>GooglePackages/fetch.ps1</c> documents for
    /// the consent SDK.
    /// </para>
    /// </summary>
    public static class AttributionConfig
    {
        /// <summary>What an unfilled identifier reads as. Never a valid key.</summary>
        public const string Unset = "UNSET";

        /// <summary>
        /// The AppsFlyer dev key, from Dashboard ▸ App Settings.
        ///
        /// <para>
        /// This is live, and <see cref="IsConfigured"/> still reads the placeholder state, so a
        /// fork of this project with the identifier stripped ships dark rather than shipping an
        /// SDK that cannot work. That matters more here than it does for an ad unit: an SDK
        /// started with a placeholder key does not error — it reports every install into
        /// nobody's account, and the only symptom is a dashboard that stays empty while the
        /// campaign spends.
        /// </para>
        /// <para>
        /// <b>It is a client-side key and it ships inside the binary</b>, which is why it lives
        /// in source beside <c>AdConfig</c>'s app keys rather than in a secret store — anybody
        /// can read it out of an APK, so hiding it here would buy nothing. The account's
        /// <em>API token</em> is the opposite and must never appear in this project: it can
        /// read the account's data and post server-to-server events, and the two are issued a
        /// screen apart in the same dashboard.
        /// </para>
        /// </summary>
        public const string DevKey = "hELUpUkCaA8t6MdZGcRcMU";

        /// <summary>
        /// The numeric App Store id, iOS only — the digits in the store URL, with no prefix.
        ///
        /// <para>
        /// Android does not have one and must be given an empty string rather than the
        /// package name, which the SDK would accept and then fail to match.
        /// </para>
        /// </summary>
        public const string AppleAppId = Unset;

        /// <summary>The dev key, or empty when it is still a placeholder.</summary>
        public static string Key => Real(DevKey);

        /// <summary>The App Store id on iOS, and empty everywhere else.</summary>
        public static string AppId
        {
            get
            {
#if UNITY_IOS
                return Real(AppleAppId);
#else
                return string.Empty;
#endif
            }
        }

        /// <summary>
        /// Whether there is a real account behind this build.
        ///
        /// <para>
        /// The dev key alone, because it is the only half that exists on both platforms: an
        /// Android build has no App Store id and must not be held to one, and an iOS build
        /// missing it is a misconfiguration the SDK reports for itself.
        /// </para>
        /// </summary>
        public static bool IsConfigured => !string.IsNullOrEmpty(Key);

        static string Real(string value)
            => string.IsNullOrEmpty(value) || value == Unset ? string.Empty : value;
    }
}
