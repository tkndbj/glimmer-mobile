using GlimmerGrove.Ads;
using GlimmerGrove.Daily;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// That the advert panel says what the advert pays.
    ///
    /// <para>
    /// <b>Written because it did not.</b> <c>TitleKey</c> ended in a bare
    /// <c>: "ui.ads.hearts_title"</c> — a default that is a real answer — so the day a fifth
    /// placement shipped, tapping the free XP boost in the shop raised a panel headed "Out of
    /// hearts" over a reward card correctly showing an XP boost. Reported from play, in exactly
    /// those words: <i>"when I tap on XP ad, it pops refill hearts modal"</i>.
    /// </para>
    /// <para>
    /// <b>The grant was never wrong</b>, which is the only reason this was a copy bug and not a
    /// payment one: the watch button carries the placement id and the table resolves the reward
    /// from it, so the panel lied about something the machinery underneath had right. That is
    /// the shape invariant 44e describes — a fall-through is wrong in a way nothing throws over.
    /// </para>
    /// </summary>
    public sealed class AdOfferCopyTests
    {
        /// <summary>
        /// <b>No placement may borrow another's headline.</b> The fault was one placement
        /// silently taking the heart panel's title, so the case is stated as "every placement
        /// gets its own", which is the property that was missing rather than a list of the five
        /// that happen to ship.
        /// </summary>
        [Test]
        public void EveryPlacementHasATitleOfItsOwn()
        {
            var seen = new System.Collections.Generic.Dictionary<string, string>();

            foreach (string placement in AdPlacement.All)
            {
                var offer = AdRewardTable.Default.Offer(placement);
                string key = AdOfferOverlay.TitleKey(placement, offer);

                Assert.IsNotEmpty(key, $"'{placement}' has no title at all");

                Assert.IsFalse(seen.TryGetValue(key, out string already),
                               $"'{placement}' and '{already}' share the title '{key}', so one " +
                               "of them is described as the other");

                seen[key] = placement;
            }
        }

        /// <summary>
        /// And the resource the panel falls back to is the one the placement really pays.
        ///
        /// Checked against the built-in table rather than a list written here, so a retune that
        /// changes what a placement pays moves both halves together or fails.
        /// </summary>
        [Test]
        public void EveryPlacementNamesTheResourceItPays()
        {
            foreach (string placement in AdPlacement.All)
            {
                var offer = AdRewardTable.Default.Offer(placement);
                if (!offer.IsValid) continue;

                Assert.AreEqual(offer.Kind, AdOfferOverlay.ResourceOf(placement),
                                $"'{placement}' pays {offer.Kind} and the panel falls back to " +
                                $"{AdOfferOverlay.ResourceOf(placement)}");
            }
        }

        /// <summary>The XP boost's own case, named, because it is the one that shipped wrong.</summary>
        [Test]
        public void TheXpBoostIsNotDescribedAsHearts()
        {
            var offer = AdRewardTable.Default.Offer(AdPlacement.XpBoost);

            Assert.AreEqual(ChestDropKind.XpBoost, offer.Kind);
            Assert.AreEqual("ui.ads.xp_boost_title",
                            AdOfferOverlay.TitleKey(AdPlacement.XpBoost, offer));
            Assert.AreEqual(ChestDropKind.XpBoost, AdOfferOverlay.ResourceOf(AdPlacement.XpBoost));
        }

        /// <summary>
        /// A placement the table does not carry still takes its title from what it is known to
        /// pay rather than from hearts — the fall-through that caused this, made harmless.
        /// </summary>
        [Test]
        public void AnUnpublishedPlacementFallsBackToItsKindRatherThanToHearts()
        {
            Assert.AreEqual("ui.ads.xp_boost_title",
                            AdOfferOverlay.TitleKey(AdPlacement.XpBoost, AdOffer.None));

            Assert.AreEqual("ui.ads.coins_title",
                            AdOfferOverlay.TitleKey(AdPlacement.CoinBonus, AdOffer.None));
        }
    }
}
