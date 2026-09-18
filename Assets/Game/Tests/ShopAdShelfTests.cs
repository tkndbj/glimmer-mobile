using System;
using System.Collections.Generic;
using GlimmerGrove.Ads;
using GlimmerGrove.Daily;
using GlimmerGrove.Store;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The shop's free spot: that it names placements this build still has, and that the table
    /// shipping inside the build still pays them.
    ///
    /// <para>
    /// <b>The failure these exist for is a silent one.</b> A shelf that names a retired
    /// placement, or a built-in table that stops carrying one, does not throw, log or draw
    /// anything wrong — <c>AdRewardTable.Offer</c> answers <see cref="AdOffer.None"/>, the row
    /// count drops by one and the shelf simply has one fewer card on it. Every other gate in
    /// this project stays green, because the shop is individually correct on every card it does
    /// draw. That is invariant 40a's fault with a shelf in place of a wave: a thing nobody
    /// authored into the table does not exist, and nothing says so.
    /// </para>
    /// <para>
    /// Offline, all of them: nothing here reads <c>progression.json</c>, which needs
    /// <c>JsonUtility</c> and therefore the Editor (invariant 29e). What they hold is the
    /// built-in table, which is what a client runs before any content push has landed and what
    /// it falls back to when one is refused — so it is the table that must be right, not merely
    /// the published one.
    /// </para>
    /// </summary>
    public sealed class ShopAdShelfTests
    {
        static readonly StoreShelf[] Shelves =
            (StoreShelf[])Enum.GetValues(typeof(StoreShelf));

        [Test]
        public void EveryShelfNamesAPlacementThisBuildStillHas()
        {
            // **`All` rather than `For`, and that is not a tidy-up.** A shelf may stand more than
            // one video now, and `For` answers only the first — so a second placement retired
            // under it would go unchecked by exactly the gate written to catch that.
            foreach (var shelf in Shelves)
                foreach (string placement in ShopAdShelf.All(shelf))
                    Assert.IsTrue(AdPlacement.IsKnown(placement),
                                  $"the {shelf} shelf offers '{placement}', which this build has " +
                                  "retired - the card is not drawn and nothing says so");
        }

        /// <summary>
        /// One shelf may not stand the same placement twice, and no two shelves may share one.
        ///
        /// <b>Both would draw two identical free cards</b> — on one shelf side by side, across two
        /// shelves as the same offer in two places — and a player who took it once would find the
        /// other refused by the cooldown with nothing to explain it. Only reachable since a shelf
        /// became a list, which is why it is checked since a shelf became a list.
        /// </summary>
        [Test]
        public void NoPlacementStandsOnTwoShelvesOrTwiceOnOne()
        {
            var seen = new Dictionary<string, StoreShelf>(StringComparer.Ordinal);

            foreach (var shelf in Shelves)
                foreach (string placement in ShopAdShelf.All(shelf))
                {
                    Assert.IsFalse(seen.TryGetValue(placement, out var already),
                                   $"'{placement}' stands on both the {already} shelf and the " +
                                   $"{shelf} shelf, so one offer is drawn as two cards");

                    seen[placement] = shelf;
                }
        }

        [Test]
        public void TheThreeShelvesThatOfferAVideoAreCoinsHeartsAndUtilities()
        {
            // Stated as the pairs rather than as a count, because which shelf gets which
            // placement is the whole decision: a coin shelf offering the heart placement would
            // pay hearts under a card headlined in coins, and every gate here would be green.
            //
            // **The utilities shelf joined them when the XP boost moved onto it**, and the rule
            // the pair follows is `StoreGoodKinds.ShelfFor`: a video stands on the shelf that
            // sells the thing it pays for. A free XP boost on the hearts tab beside a paid one on
            // the utilities tab would be one offer in two places.
            Assert.AreEqual(AdPlacement.CoinBonus, ShopAdShelf.For(StoreShelf.Coins));
            Assert.AreEqual(AdPlacement.HeartRefill, ShopAdShelf.For(StoreShelf.Supplies));
            Assert.AreEqual(AdPlacement.XpBoost, ShopAdShelf.For(StoreShelf.Utilities));

            foreach (var shelf in Shelves)
                if (shelf != StoreShelf.Coins && shelf != StoreShelf.Supplies
                    && shelf != StoreShelf.Utilities)
                    Assert.IsEmpty(ShopAdShelf.All(shelf),
                                   $"the {shelf} shelf offers a video, which is a merchandising " +
                                   "decision nobody has written down");
        }

        /// <summary>
        /// A video stands where the thing it pays for is sold.
        ///
        /// Derived from <c>StoreGoodKinds.ShelfFor</c> rather than restated, so moving a good
        /// between shelves moves this with it — the fault it guards against is the two drifting
        /// apart and nobody noticing until a player sees the same offer on two tabs.
        /// </summary>
        [Test]
        public void TheXpVideoStandsWhereTheXpBoostIsSold()
        {
            Assert.AreEqual(StoreGoodKinds.ShelfFor(StoreGoodKind.XpBoost),
                            StoreShelf.Utilities);

            CollectionAssert.Contains(ShopAdShelf.All(StoreGoodKinds.ShelfFor(StoreGoodKind.XpBoost)),
                                      AdPlacement.XpBoost);

            // And hearts stay where they were, which is the other half of the same rule.
            Assert.AreEqual(StoreGoodKinds.ShelfFor(StoreGoodKind.Hearts), StoreShelf.Supplies);
            CollectionAssert.Contains(ShopAdShelf.All(StoreShelf.Supplies), AdPlacement.HeartRefill);
        }

        [Test]
        public void TheBuiltInTablePaysEveryShelfThatOffersOne()
        {
            foreach (var shelf in Shelves)
                foreach (string placement in ShopAdShelf.All(shelf))
                {
                    var offer = AdRewardTable.Default.Offer(placement);

                    Assert.IsTrue(offer.IsValid,
                                  $"the {shelf} shelf offers '{placement}' and the built-in table " +
                                  "pays it nothing, so the shelf loses a card on a fresh install");
                }
        }

        [Test]
        public void AShelfPaysTheCurrencyItsCardsAreHeadlinedIn()
        {
            // What the card draws is `offer.Kind` (`ProductCard.Draw(AdOffer, StoreShelf)`), so
            // a placement paying the wrong thing is not a wrong number on the card - it is a
            // heart drawn on the coin shelf, correctly, under a green WATCH.
            Assert.AreEqual(ChestDropKind.Credits,
                            AdRewardTable.Default.Offer(ShopAdShelf.For(StoreShelf.Coins)).Kind);
            Assert.AreEqual(ChestDropKind.Hearts,
                            AdRewardTable.Default.Offer(ShopAdShelf.For(StoreShelf.Supplies)).Kind);
        }

        [Test]
        public void OffersAgreesWithFor()
        {
            // The two answers are read in different places - one decides whether a row exists,
            // the other is the row's own offer - so a shelf they disagree about is a grid sized
            // for a card nothing fills.
            foreach (var shelf in Shelves)
                Assert.AreEqual(ShopAdShelf.For(shelf) != null, ShopAdShelf.Offers(shelf));
        }
    }
}
