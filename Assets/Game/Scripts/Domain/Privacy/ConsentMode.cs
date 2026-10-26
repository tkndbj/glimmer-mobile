using System;

namespace GlimmerGrove.Privacy
{
    /// <summary>
    /// One purpose's answer in Google's consent mode, as the UMP SDK writes it: one digit of
    /// <c>UMP_consentModeValues</c>. The numbers are Google's and are contract.
    /// </summary>
    public enum ConsentPurpose
    {
        /// <summary><c>0</c>: no value yet - UMP has not finished, or never ran.</summary>
        NotReady = 0,

        /// <summary><c>1</c>: the player granted this purpose.</summary>
        Granted = 1,

        /// <summary><c>2</c>: the player denied this purpose.</summary>
        Denied = 2,

        /// <summary><c>3</c>: consent mode does not apply to this player (outside the regulated regions).</summary>
        NotApplicable = 3,

        /// <summary><c>4</c>: consent mode is not configured for this purpose in the AdMob console.</summary>
        Unconfigured = 4,
    }

    /// <summary>
    /// The four purposes UMP reports for Google's consent mode, read from the
    /// <c>UMP_consentModeValues</c> local storage key: a four-digit string, in the order
    /// <c>ad_storage</c>, <c>ad_user_data</c>, <c>ad_personalization</c>, <c>analytics_storage</c>
    /// (Google, "Google consent management solution SDKs").
    ///
    /// <para>
    /// <b>Parsed strictly, and anything else is "not ready".</b> A missing key, a wrong length or
    /// a digit outside 0-4 is not an answer, so it reads as <see cref="ConsentPurpose.NotReady"/>
    /// on all four - which <see cref="AnalyticsConsent"/> treats as not granted. A malformed
    /// value can never manufacture a grant.
    /// </para>
    /// </summary>
    public readonly struct ConsentModeValues : IEquatable<ConsentModeValues>
    {
        public readonly ConsentPurpose AdStorage, AdUserData, AdPersonalization, AnalyticsStorage;

        public ConsentModeValues(ConsentPurpose adStorage, ConsentPurpose adUserData,
                                 ConsentPurpose adPersonalization, ConsentPurpose analyticsStorage)
        {
            AdStorage = adStorage;
            AdUserData = adUserData;
            AdPersonalization = adPersonalization;
            AnalyticsStorage = analyticsStorage;
        }

        /// <summary>Nothing known: what a build without UMP, or a launch before it answers, holds.</summary>
        public static ConsentModeValues NotReady => default;

        public static ConsentModeValues Parse(string raw)
        {
            if (raw == null || raw.Length != 4) return NotReady;

            var digits = new ConsentPurpose[4];
            for (int i = 0; i < 4; i++)
            {
                int d = raw[i] - '0';
                if (d < 0 || d > 4) return NotReady;
                digits[i] = (ConsentPurpose)d;
            }

            return new ConsentModeValues(digits[0], digits[1], digits[2], digits[3]);
        }

        public bool Equals(ConsentModeValues other)
            => AdStorage == other.AdStorage && AdUserData == other.AdUserData
            && AdPersonalization == other.AdPersonalization && AnalyticsStorage == other.AnalyticsStorage;

        public override bool Equals(object obj) => obj is ConsentModeValues other && Equals(other);

        public override int GetHashCode() => (AdStorage, AdUserData, AdPersonalization, AnalyticsStorage).GetHashCode();

        public override string ToString()
            => $"{(int)AdStorage}{(int)AdUserData}{(int)AdPersonalization}{(int)AnalyticsStorage}";
    }

    /// <summary>
    /// What Google Analytics is told: whether to collect at all, and each consent-mode purpose
    /// (invariant 55a's analytics half).
    ///
    /// <para>
    /// <b>Consent mode, advanced: collection stays on and the purposes decide what it may
    /// carry.</b> With <c>analytics_storage</c> denied, Analytics on a mobile app sends its
    /// events with no device or user identifier and uses them for modelling (Google, "Consent
    /// mode on websites and mobile apps"); with it granted, the events are the full events. So
    /// a player who has not answered yet - every EEA player before the form, wherever the form
    /// is put - is measured without being identified, and nothing is dropped. The form can stay
    /// on the hub (invariant 55) without the funnel before it going dark.
    /// </para>
    /// <para>
    /// <b>Each purpose is granted only by an answer that grants it</b>: the player's own
    /// <see cref="ConsentPurpose.Granted"/>, or Google's <see cref="ConsentPurpose.NotApplicable"/>
    /// (consent mode does not apply to this player). Not ready and unconfigured are not grants,
    /// with one exception that is an answer too: a player UMP has resolved as outside every
    /// regulated region (<c>GdprApplies</c> false), who needs no consent for anything. The ad purposes
    /// that carry a person to an advertiser (<c>ad_user_data</c>, <c>ad_personalization</c>) are
    /// also denied for a do-not-sell or child-directed answer, whatever UMP says.
    /// </para>
    /// <para>
    /// <b>A child-directed install collects nothing</b>, as it never has.
    /// </para>
    /// </summary>
    public readonly struct AnalyticsConsent : IEquatable<AnalyticsConsent>
    {
        public readonly bool Collect;
        public readonly bool AnalyticsStorage, AdStorage, AdUserData, AdPersonalization;

        public AnalyticsConsent(bool collect, bool analyticsStorage, bool adStorage, bool adUserData,
                                bool adPersonalization)
        {
            Collect = collect;
            AnalyticsStorage = analyticsStorage;
            AdStorage = adStorage;
            AdUserData = adUserData;
            AdPersonalization = adPersonalization;
        }

        public static AnalyticsConsent For(AdPrivacySignals signals)
        {
            var mode = signals.ConsentMode;
            bool outside = !signals.GdprApplies && signals.ConsentSettled;
            bool sharesPerson = !signals.DoNotSell && !signals.ChildDirected;

            return new AnalyticsConsent(
                collect: !signals.ChildDirected,
                analyticsStorage: Grants(mode.AnalyticsStorage, outside),
                adStorage: Grants(mode.AdStorage, outside),
                adUserData: sharesPerson && Grants(mode.AdUserData, outside),
                adPersonalization: sharesPerson && Grants(mode.AdPersonalization, outside));
        }

        static bool Grants(ConsentPurpose value, bool outside)
        {
            switch (value)
            {
                case ConsentPurpose.Granted:
                case ConsentPurpose.NotApplicable:
                    return true;

                case ConsentPurpose.Denied:
                    return false;

                // Not an answer about this purpose. Outside every regulated region there is no
                // consent to wait for, so the player keeps the measurement they have today;
                // inside one, it stays denied - never manufactured from an advertising answer.
                case ConsentPurpose.NotReady:
                case ConsentPurpose.Unconfigured:
                    return outside;

                default:
                    return false;
            }
        }

        public bool Equals(AnalyticsConsent other)
            => Collect == other.Collect && AnalyticsStorage == other.AnalyticsStorage
            && AdStorage == other.AdStorage && AdUserData == other.AdUserData
            && AdPersonalization == other.AdPersonalization;

        public override bool Equals(object obj) => obj is AnalyticsConsent other && Equals(other);

        public override int GetHashCode()
            => (Collect, AnalyticsStorage, AdStorage, AdUserData, AdPersonalization).GetHashCode();

        public override string ToString()
            => $"collect {Collect}, analytics_storage {AnalyticsStorage}, ad_storage {AdStorage}, " +
               $"ad_user_data {AdUserData}, ad_personalization {AdPersonalization}";
    }
}
