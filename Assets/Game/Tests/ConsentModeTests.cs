using GlimmerGrove.Privacy;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Analytics in Google's consent mode (invariant 55b): UMP's <c>UMP_consentModeValues</c>
    /// read strictly, and the rule that turns a privacy answer into what Analytics is told.
    /// The native halves - the store read and <c>setConsent</c> - are checked on a device
    /// ("[Analytics] consent mode applied" in the log); this is the decision they carry.
    /// </summary>
    public sealed class ConsentModeTests
    {
        // ------------------------------------------------------------------ the string
        [Test]
        public void TheFourDigitsAreReadInGooglesOrder()
        {
            var v = ConsentModeValues.Parse("1234");

            Assert.AreEqual(ConsentPurpose.Granted, v.AdStorage);
            Assert.AreEqual(ConsentPurpose.Denied, v.AdUserData);
            Assert.AreEqual(ConsentPurpose.NotApplicable, v.AdPersonalization);
            Assert.AreEqual(ConsentPurpose.Unconfigured, v.AnalyticsStorage);
            Assert.AreEqual("1234", v.ToString());
        }

        [Test]
        public void AnythingButFourDigitsFromNoughtToFourIsNotAnAnswer()
        {
            foreach (var raw in new[] { null, "", "111", "11111", "1115", "11a1", " 111", "-111" })
                Assert.AreEqual(ConsentModeValues.NotReady, ConsentModeValues.Parse(raw), $"'{raw}'");
        }

        // ------------------------------------------------------------------ the rule
        static AdPrivacySignals Eea(ConsentStatus answer, string mode)
            => new AdPrivacySignals(true, answer, false, false, TrackingStatus.NotSupported,
                                    ConsentModeValues.Parse(mode));

        static AdPrivacySignals Outside(string mode)
            => new AdPrivacySignals(false, ConsentStatus.Granted, false, false, TrackingStatus.NotSupported,
                                    ConsentModeValues.Parse(mode));

        [Test]
        public void BeforeAnyAnswerItCollectsWithEveryPurposeDenied()
        {
            var c = AnalyticsConsent.For(AdPrivacySignals.Restricted);

            Assert.IsTrue(c.Collect, "consent mode measures the unanswered launch, without identifiers");
            Assert.IsFalse(c.AnalyticsStorage);
            Assert.IsFalse(c.AdStorage);
            Assert.IsFalse(c.AdUserData);
            Assert.IsFalse(c.AdPersonalization);
        }

        [Test]
        public void AnEeaPlayerWhoHasNotAnsweredIsMeasuredButNotIdentified()
        {
            var c = AnalyticsConsent.For(Eea(ConsentStatus.Unknown, "0000"));

            Assert.IsTrue(c.Collect);
            Assert.IsFalse(c.AnalyticsStorage);
            Assert.IsFalse(c.AdStorage);
        }

        [Test]
        public void EachPurposeFollowsThePlayersOwnAnswer()
        {
            var c = AnalyticsConsent.For(Eea(ConsentStatus.Granted, "1212"));

            Assert.IsTrue(c.AdStorage);
            Assert.IsFalse(c.AdUserData);
            Assert.IsTrue(c.AdPersonalization);
            Assert.IsFalse(c.AnalyticsStorage);
        }

        [Test]
        public void AnAdvertisingAnswerNeverManufacturesAnAnalyticsGrant()
        {
            // The console has no analytics purpose configured: the ads were agreed to, and that
            // says nothing about analytics storage.
            var c = AnalyticsConsent.For(Eea(ConsentStatus.Granted, "1114"));

            Assert.IsTrue(c.Collect);
            Assert.IsFalse(c.AnalyticsStorage, "unconfigured in a regulated region stays denied");
            Assert.IsTrue(c.AdStorage);
        }

        [Test]
        public void OutsideTheRegulatedRegionsEverythingIsGranted()
        {
            foreach (var mode in new[] { "3333", "0000", "4444", null })
            {
                var c = AnalyticsConsent.For(Outside(mode));

                Assert.IsTrue(c.Collect, mode);
                Assert.IsTrue(c.AnalyticsStorage, $"{mode}: no consent is owed, so none is waited for");
                Assert.IsTrue(c.AdStorage, mode);
                Assert.IsTrue(c.AdUserData, mode);
                Assert.IsTrue(c.AdPersonalization, mode);
            }
        }

        [Test]
        public void AnExplicitDenialOutsideTheRegionsIsStillADenial()
        {
            var c = AnalyticsConsent.For(Outside("2222"));

            Assert.IsFalse(c.AnalyticsStorage);
            Assert.IsFalse(c.AdStorage);
        }

        [Test]
        public void DoNotSellWithholdsThePurposesThatCarryAPersonToAnAdvertiser()
        {
            var signals = new AdPrivacySignals(false, ConsentStatus.Granted, doNotSell: true,
                                               childDirected: false, tracking: TrackingStatus.NotSupported,
                                               consentMode: ConsentModeValues.Parse("1111"));
            var c = AnalyticsConsent.For(signals);

            Assert.IsTrue(c.AnalyticsStorage);
            Assert.IsTrue(c.AdStorage);
            Assert.IsFalse(c.AdUserData);
            Assert.IsFalse(c.AdPersonalization);
        }

        [Test]
        public void AChildDirectedInstallCollectsNothing()
        {
            var signals = new AdPrivacySignals(false, ConsentStatus.Granted, doNotSell: false,
                                               childDirected: true, tracking: TrackingStatus.NotSupported,
                                               consentMode: ConsentModeValues.Parse("1111"));
            var c = AnalyticsConsent.For(signals);

            Assert.IsFalse(c.Collect);
            Assert.IsFalse(c.AdUserData);
            Assert.IsFalse(c.AdPersonalization);
        }

        [Test]
        public void TheConsentModeIsPartOfTheAnswer()
        {
            // A refresh that only changes the purposes must still reach Analytics: it is a change
            // of answer, so `AdPrivacy.Commit` must not swallow it as equal.
            Assert.AreNotEqual(Eea(ConsentStatus.Granted, "1111"), Eea(ConsentStatus.Granted, "1112"));
            Assert.AreEqual(Eea(ConsentStatus.Granted, "1111"), Eea(ConsentStatus.Granted, "1111"));
        }
    }
}
