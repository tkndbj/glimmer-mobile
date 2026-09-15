using System;
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
            foreach (var shelf in Shelves)
            {
                string placement = ShopAdShelf.For(shelf);
                if (placement == null) continue;

                Assert.IsTrue(AdPlacement.IsKnown(placement),
                              $"the {shelf} shelf offers '{placement}', which this build has " +
                              "retired - the card is not drawn and nothing says so");
            }
        }

        [Test]
        public void TheTwoShelvesThatOfferAVideoAreCoinsAndHearts()
        {
            // Stated as the pairs rather than as a count, because which shelf gets which
            // placement is the whole decision: a coin shelf offering the heart placement would
            // pay hearts under a card headlined in coins, and every gate here would be green.
            Assert.AreEqual(AdPlacement.CoinBonus, ShopAdShelf.For(StoreShelf.Coins));
            Assert.AreEqual(AdPlacement.HeartRefill, ShopAdShelf.For(StoreShelf.Supplies));

            foreach (var shelf in Shelves)
                if (shelf != StoreShelf.Coins && shelf != StoreShelf.Supplies)
                    Assert.IsNull(ShopAdShelf.For(shelf),
                                  $"the {shelf} shelf offers a video, which is a merchandising " +
                                  "decision nobody has written down");
        }

        [Test]
        public void TheBuiltInTablePaysEveryShelfThatOffersOne()
        {
            foreach (var shelf in Shelves)
            {
                string placement = ShopAdShelf.For(shelf);
                if (placement == null) continue;

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
