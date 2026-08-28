using GlimmerGrove.Layout;
using GlimmerGrove.Privacy;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The three public links Settings carries, and the room they take.
    ///
    /// <para>
    /// <b>Every failure here is silent on a device.</b> A malformed URL makes
    /// <c>Application.OpenURL</c> do nothing at all — no exception, no log, a button that simply
    /// does not work — and a panel that has grown past the canvas draws its title off the top of
    /// a 4:3 tablet and of nothing else. Neither shows up in a compile, a validator, or a
    /// screenshot taken on the phone the developer happens to hold.
    /// </para>
    /// <para>
    /// It matters more than a cosmetic check because the privacy link is a review requirement:
    /// App Store Review 5.1.1(i) wants it reachable inside the app, so a dead one is a rejection
    /// rather than a blemish.
    /// </para>
    /// </summary>
    public sealed class LegalLinkTests
    {
        [Test]
        public void EveryLinkIsOneThePlatformWillActuallyOpen()
        {
            Assert.IsTrue(LegalLinks.Usable(LegalLinks.Privacy), LegalLinks.Privacy);
            Assert.IsTrue(LegalLinks.Usable(LegalLinks.Terms), LegalLinks.Terms);
            Assert.IsTrue(LegalLinks.Usable(LegalLinks.Support), LegalLinks.Support);
        }

        /// <summary>
        /// The guard has to reject something, or it is decoration — invariant 5d's complaint
        /// applied to a predicate rather than to a mechanic.
        /// </summary>
        [Test]
        public void AndTheGuardRefusesTheWaysAUrlGoesWrong()
        {
            Assert.IsFalse(LegalLinks.Usable(null), "null");
            Assert.IsFalse(LegalLinks.Usable(""), "empty");
            Assert.IsFalse(LegalLinks.Usable("https://"), "a scheme and nothing else");
            Assert.IsFalse(LegalLinks.Usable("glimmergroove.app/privacy"), "no scheme");

            // Plain http is refused rather than upgraded. iOS blocks it under App Transport
            // Security anyway, so allowing it here would be a link that works in the Editor and
            // silently does nothing on the device that matters.
            Assert.IsFalse(LegalLinks.Usable("http://www.glimmergroove.app/privacy"), "not https");

            Assert.IsFalse(LegalLinks.Usable("https://www.glimmergroove.app/a b"), "unescaped space");
        }

        /// <summary>
        /// All three on one host, and the host is the <c>www</c> one the site actually serves.
        /// The apex 308-redirects to it, so a link to the apex works and spends a redirect —
        /// and the same spelling belongs in the Developer website field of both store listings,
        /// because that is the domain ad crawlers fetch <c>app-ads.txt</c> from.
        /// </summary>
        [Test]
        public void TheyAllPointAtTheSiteTheStoreListingsName()
        {
            Assert.AreEqual("https://www.glimmergroove.app", LegalLinks.Site);

            foreach (var url in new[] { LegalLinks.Privacy, LegalLinks.Terms, LegalLinks.Support })
                Assert.IsTrue(url.StartsWith(LegalLinks.Site + "/"), url);
        }

        /// <summary>Three different pages, not one page linked three times.</summary>
        [Test]
        public void AndTheyAreThreeDifferentPages()
        {
            Assert.AreNotEqual(LegalLinks.Privacy, LegalLinks.Terms);
            Assert.AreNotEqual(LegalLinks.Terms, LegalLinks.Support);
            Assert.AreNotEqual(LegalLinks.Privacy, LegalLinks.Support);
        }

        /// <summary>
        /// The settings panel in its tallest arrangement — the consent row *and* the legal row —
        /// still fits the shortest canvas this game is drawn on.
        ///
        /// <para>
        /// Read from the panel's own constants rather than restated, because a test that checks
        /// arithmetic the panel does not use is the failure <c>WheelPanelTests</c> had: it passed
        /// throughout while the live panel drew a row through its neighbour.
        /// </para>
        /// </summary>
        [Test]
        public void TheSettingsPanelStillFitsTheShortestCanvas()
        {
            float tallest = SettingsOverlay.BaseHeight
                          + SettingsOverlay.ConsentRow
                          + SettingsOverlay.LegalRow;

            Assert.LessOrEqual(tallest, PanelStack.TallestPanel,
                               $"the settings panel is {tallest} against a ceiling of {PanelStack.TallestPanel}");
        }
    }
}
