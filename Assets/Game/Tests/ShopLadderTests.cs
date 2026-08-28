using System.Collections.Generic;
using GlimmerGrove.Store;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Which rung of its shelf a shop card is drawn as.
    ///
    /// <para>
    /// This is arithmetic nobody can see. A card carries a painted picture of what arrives and,
    /// behind it, a fan of light in that rung's colour — and both are the same answer to the
    /// same question, so the whole risk here is the two coming to disagree while every check
    /// this project has stays green. What that looks like on a phone is the fifth picture under
    /// the sixth colour, which reads as a card that is simply slightly wrong.
    /// </para>
    /// <para>
    /// The shipped shelves are driven at the bottom, because the numbers that matter are the
    /// ones a paying player sees: four coin products spread over a six-rung ladder, six gem
    /// products landing one per rung, and three bundles filling three.
    /// </para>
    /// </summary>
    public sealed class ShopLadderTests
    {
        static StoreProduct Ranked(int tier, int shelfSize, bool oneTime = false)
        {
            var product = new StoreProduct(
                "p" + tier,
                oneTime ? StoreProductKind.NonConsumable : StoreProductKind.Consumable,
                StoreShelf.Gems, 0, 100 * tier, 99 * tier, StoreBadge.None);

            product.Tier = tier;
            product.ShelfSize = shelfSize;
            return product;
        }

        // ------------------------------------------------------------------ the ends
        [Test]
        public void TheSmallestProductIsTheBottomRungAndTheLargestIsTheTop()
        {
            Assert.AreEqual(0, ShopLadder.Rung(Ranked(1, 6), 6));
            Assert.AreEqual(5, ShopLadder.Rung(Ranked(6, 6), 6));
        }

        [Test]
        public void AShelfOfOneIsTheTopRungRatherThanTheBottom()
        {
            // A product with nothing to be compared against is the best thing on its shelf.
            // The other reading — no comparison, so the lowest rung — draws the only product
            // on a shelf as the meanest one there is.
            Assert.AreEqual(5, ShopLadder.Rung(Ranked(1, 1), 6));
        }

        [Test]
        public void AShelfNobodyHasRankedYetIsTheTopRung()
        {
            // Tier is zero until StoreCatalog ranks a shelf, and a card drawn in that window
            // must not advertise a fifty-dollar pack as the cheapest thing in the shop.
            var unranked = new StoreProduct("u", StoreProductKind.Consumable, StoreShelf.Gems,
                                            0, 8500, 4999, StoreBadge.None);

            Assert.AreEqual(5, ShopLadder.Rung(unranked, 6));
        }

        [Test]
        public void AOneTimeOfferTakesTheTopRungHoweverCheapItIs()
        {
            // The starter bundle really is the cheapest product in the shop. Drawing it as the
            // cheapest product in the shop tells the truth about the price and lies about the
            // offer, and it is the one card a player is shown once in their life.
            Assert.AreEqual(2, ShopLadder.Rung(Ranked(1, 3, oneTime: true), 3));
        }

        // ------------------------------------------------------------------ the shelves
        [Test]
        public void EveryShippedShelfClimbsWithoutRepeatingARung()
        {
            // Six rungs of art and four coin products: the ladder has to spread them, not
            // bunch them at the bottom, or the shelf reads as three sizes and a gap.
            CollectionAssert.AreEqual(new[] { 0, 2, 3, 5 }, Rungs(shelf: 4, rungs: 6));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, Rungs(shelf: 6, rungs: 6));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, Rungs(shelf: 3, rungs: 3));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, Rungs(shelf: 3, rungs: 3));
        }

        [Test]
        public void ARungNeverGoesDownAsAProductGetsDearer()
        {
            // The property the shelves above are instances of, over every shape a shelf could
            // take. A ladder that stepped back anywhere would draw a dearer product as a
            // smaller pile, which is the one thing a storefront picture must never do.
            for (int shelf = 1; shelf <= 12; shelf++)
                for (int rungs = 1; rungs <= 8; rungs++)
                {
                    int last = -1;
                    foreach (int rung in Rungs(shelf, rungs))
                    {
                        Assert.GreaterOrEqual(rung, last, $"shelf {shelf}, {rungs} rungs");
                        Assert.Less(rung, rungs, $"shelf {shelf}, {rungs} rungs");
                        last = rung;
                    }
                }
        }

        [Test]
        public void AShelfNoLongerThanItsLadderUsesEveryRung()
        {
            // Six products over six rungs is one each, and that is what makes the top of a
            // shelf legible as the top. It stops holding the moment a shelf is longer than the
            // ladder, which is correct — two products then share a picture — so the claim is
            // only made where it can be true.
            for (int rungs = 2; rungs <= 8; rungs++)
                CollectionAssert.AreEquivalent(Sequence(rungs), Rungs(rungs, rungs),
                                               $"{rungs} products over {rungs} rungs");
        }

        static IEnumerable<int> Sequence(int n)
        {
            for (int i = 0; i < n; i++) yield return i;
        }

        static List<int> Rungs(int shelf, int rungs)
        {
            var found = new List<int>();
            for (int tier = 1; tier <= shelf; tier++)
                found.Add(ShopLadder.Rung(Ranked(tier, shelf), rungs));
            return found;
        }
    }
}
