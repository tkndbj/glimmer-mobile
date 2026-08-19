using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Store;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The shop's rules, which is the one table in this project where being wrong costs
    /// money rather than tuning.
    ///
    /// <para>
    /// Three properties are pinned here and none of them can be seen by reading the file.
    /// The <b>catalog reader drops rather than clamps</b>, because a clamped grant is a
    /// player charged one amount and given another. The <b>ladder gets better as it gets
    /// bigger</b>, which is invisible in the file and obvious to the first player with a
    /// calculator. And the <b>bonus badge is derived from the ladder</b>, so a card cannot
    /// print a claim the prices contradict.
    /// </para>
    /// <para>
    /// The last fixture reads the shipped <c>progression.json</c> and is therefore
    /// Editor-only — it reaches <c>Application.dataPath</c>, which the offline runner
    /// cannot. That is the same bargain <c>HomesteadTests</c> makes, and it is worth the
    /// split: what it proves is that the built-in fallback catalog and the shipped content
    /// have not drifted apart, and a drifted fallback is a shop that promises one amount
    /// offline and another online.
    /// </para>
    /// </summary>
    public sealed class StoreTests
    {
        [TearDown]
        public void Restore() => ProgressionRules.Reset();

        // ------------------------------------------------------------------ reading
        static StoreProductDto Product(string id, string shelf, long credits, long gems,
                                       int cents, string kind = "consumable", string badge = null)
            => new StoreProductDto
            {
                id = id, shelf = shelf, kind = kind, credits = credits, gems = gems,
                referenceUsdCents = cents, badge = badge,
            };

        static StoreGoodDto Good(string id, string kind, int amount, long gems)
            => new StoreGoodDto { id = id, kind = kind, amount = amount, gems = gems };

        [Test]
        public void AnAbsentBlockKeepsTheBuiltInLadder()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(null, problems);

            Assert.AreSame(StoreCatalog.Default, catalog);
            CollectionAssert.IsEmpty(problems, "an absent store block is not an error");
        }

        /// <summary>
        /// The trap that made two unrelated vector fixtures red.
        ///
        /// <c>JsonUtility</c> instantiates a nested serialisable field whether or not the
        /// file carried one, so every progression file written before the shop existed
        /// arrives here as a non-null <see cref="StoreDto"/> with nothing in it. If that
        /// counted as an authoring mistake, every such file would report a problem — and
        /// <c>ContentValidation</c> turns a problem into a build error.
        /// </summary>
        [Test]
        public void AnEmptyBlockIsTheSameAsNoBlockAtAll()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto(), problems);

            Assert.AreSame(StoreCatalog.Default, catalog);
            CollectionAssert.IsEmpty(problems,
                "a file written before the shop existed must not report a content error");
        }

        [Test]
        public void AuthoringOnlyUnreadableEntriesIsStillAnError()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[] { Product("nope", "gems", 0, 0, 99) },
            }, problems);

            // The other half of the distinction above: somebody did write products down and
            // none of them survived, which would open a live build on an empty shop.
            Assert.AreSame(StoreCatalog.Default, catalog);
            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void AProductThatGrantsNothingIsDroppedRatherThanClamped()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[]
                {
                    Product("good_one", "gems", 0, 100, 99),
                    Product("empty_one", "gems", 0, 0, 299),
                },
            }, problems);

            Assert.IsNotNull(catalog.Find("good_one"));
            Assert.IsNull(catalog.Find("empty_one"), "a product granting nothing must not be sold");
            Assert.IsTrue(problems.Exists(p => p.Contains("empty_one")));
        }

        [Test]
        public void AGrantAboveTheCeilingIsRefusedRatherThanCapped()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[] { Product("huge", "gems", 0, StoreLimits.MaxGrant + 1, 99) },
            }, problems);

            // Refusing is the whole point: the server refuses the same figure, so a clamped
            // card would promise an amount no receipt would ever be honoured for.
            Assert.IsNull(catalog.Find("huge"));
            Assert.AreSame(StoreCatalog.Default, catalog,
                           "a block with nothing usable in it falls back rather than closing the shop");
        }

        [Test]
        public void ACurrencyGoodIsRefused()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[] { Product("gems_a", "gems", 0, 100, 99) },
                goods = new[] { Good("coins_for_gems", "credits", 1000, 50) },
            }, problems);

            // Currency may only ever be granted by the server against a receipt. A good is
            // applied by the phone, so a good that paid credits would be the client minting.
            Assert.IsNull(catalog.FindGood("coins_for_gems"));
            Assert.IsTrue(problems.Exists(p => p.Contains("coins_for_gems")));
        }

        [Test]
        public void ADuplicateIdIsDroppedOnce()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[]
                {
                    Product("twice", "gems", 0, 100, 99),
                    Product("twice", "gems", 0, 9000, 99),
                },
            }, problems);

            Assert.AreEqual(100L, catalog.Find("twice").Gems, "the first entry wins");
            Assert.AreEqual(1, catalog.Products.Count);
        }

        [Test]
        public void OneBadgeOfEachKindPerShelf()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[]
                {
                    Product("a", "gems", 0, 100, 99, badge: "popular"),
                    Product("b", "gems", 0, 340, 299, badge: "popular"),
                },
            }, problems);

            Assert.AreEqual(StoreBadge.Popular, catalog.Find("a").Badge);
            Assert.AreEqual(StoreBadge.None, catalog.Find("b").Badge,
                            "a second badge of the same kind reads as a shop that cannot choose");
            Assert.IsNotNull(catalog.Find("b"), "the product survives; only the badge is dropped");
        }

        [Test]
        public void AStarterBadgeOnAConsumableIsDropped()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[] { Product("s", "bundles", 5000, 500, 299, badge: "starter") },
            }, problems);

            // A one-time offer that can be bought twice is not one, and the store — not this
            // code — is what makes it one-time. So the badge follows the product kind.
            Assert.AreEqual(StoreBadge.None, catalog.Find("s").Badge);
        }

        // ------------------------------------------------------------------ ranking
        [Test]
        public void TierFollowsPriceRatherThanTheAuthoredOrder()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[]
                {
                    Product("dear", "gems", 0, 3900, 2499),
                    Product("cheap", "gems", 0, 100, 99),
                    Product("middle", "gems", 0, 750, 599),
                },
            }, problems);

            // The picture on a card is drawn from its tier, so an authored order that
            // disagreed with the prices would put a gold chest above a pouch.
            Assert.AreEqual(1, catalog.Find("cheap").Tier);
            Assert.AreEqual(2, catalog.Find("middle").Tier);
            Assert.AreEqual(3, catalog.Find("dear").Tier);
            Assert.AreEqual(3, catalog.Find("cheap").ShelfSize);
        }

        [Test]
        public void TheBonusIsMeasuredAgainstTheCheapestRepeatableRung()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[]
                {
                    Product("base", "gems", 0, 100, 100),
                    Product("double", "gems", 0, 400, 200),
                },
            }, problems);

            Assert.AreEqual(0, catalog.Find("base").BonusPercent);

            // 400 gems per 200c is twice 100 gems per 100c.
            Assert.AreEqual(100, catalog.Find("double").BonusPercent);
        }

        [Test]
        public void AOneTimeOfferNeverBecomesTheBaseline()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[]
                {
                    // Deliberately the cheapest and by far the best value, which is what a
                    // starter offer is. Ranking the ladder against it would report every
                    // ordinary rung as no bonus at all.
                    Product("starter", "gems", 0, 900, 99, kind: "nonconsumable", badge: "starter"),
                    Product("base", "gems", 0, 100, 100),
                    Product("double", "gems", 0, 400, 200),
                },
            }, problems);

            Assert.AreEqual(0, catalog.Find("base").BonusPercent);
            Assert.AreEqual(100, catalog.Find("double").BonusPercent);
            Assert.Greater(catalog.Find("starter").BonusPercent, 500,
                           "the starter is still measured, and it is the number worth printing on it");
        }

        [Test]
        public void CreditsPerGemComesFromTheTwoEntryRungs()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[]
                {
                    Product("g", "gems", 0, 100, 100),
                    Product("c", "coins", 1000, 0, 100),
                },
            }, problems);

            // 1000 credits per 100c against 100 gems per 100c: one gem is ten credits.
            Assert.AreEqual(10L, catalog.CreditsPerGem);
        }

        // ------------------------------------------------------------------ goods
        [Test]
        public void HeartsThatWouldOverflowTheCeilingAreRefusedRatherThanClamped()
        {
            Publish(new StoreDto
            {
                products = new[] { Product("g", "gems", 0, 100, 99) },
                goods = new[] { Good("many", "hearts", 40, 10) },
            });

            var good = StoreRules.FindGood("many");
            Assert.IsNotNull(good);

            Wallet.LoadFrom(FreshSave());
            Wallet.GrantHearts(HeartRules.Ceiling - 5);

            // Taking gems for hearts that evaporate on arrival is a different thing from a
            // chest losing its surplus: somebody paid for these.
            Assert.AreEqual(GoodOfferState.HeartsNearlyFull, StoreService.OfferForGood(good));
        }

        [Test]
        public void ABoostPastTheCapIsRefused()
        {
            Publish(new StoreDto
            {
                products = new[] { Product("g", "gems", 0, 100, 99) },
                goods = new[] { Good("long", "heart_boost", 24, 10) },
            });

            Wallet.LoadFrom(FreshSave());
            Wallet.GrantHeartBoost(HeartRules.MaxBoostHours);

            var good = StoreRules.FindGood("long");
            Assert.AreEqual(GoodOfferState.BoostNearlyFull, StoreService.OfferForGood(good));
        }

        [Test]
        public void AGoodNamesItselfFromItsIdAndNothingElse()
        {
            var good = new StoreGood("hearts_five", StoreGoodKind.Hearts, 5, 50);

            // Invariant 5a, one shelf over: anything holding the id can name the thing
            // without reading the catalog, which is what lets a receipt say what was bought.
            Assert.AreEqual("store.good.hearts_five", good.NameKey);
            Assert.AreEqual("store_good:hearts_five", good.SpendReason);
        }

        // ---------------------------------------------------------------- the ladder
        [Test]
        public void TheBuiltInLadderGetsBetterAsItGetsBigger()
        {
            AssertLadderRises(StoreCatalog.Default);
        }

        static void AssertLadderRises(StoreCatalog catalog)
        {
            foreach (StoreShelf shelf in System.Enum.GetValues(typeof(StoreShelf)))
            {
                var rungs = new List<StoreProduct>();
                foreach (var product in catalog.Shelf(shelf))
                    if (!product.IsOneTime) rungs.Add(product);

                rungs.Sort((a, b) => a.ReferenceUsdCents.CompareTo(b.ReferenceUsdCents));

                for (int i = 1; i < rungs.Count; i++)
                {
                    long before = rungs[i - 1].ValuePerCent(catalog.CreditsPerGem);
                    long after = rungs[i].ValuePerCent(catalog.CreditsPerGem);

                    Assert.GreaterOrEqual(after, before,
                        $"{shelf}: '{rungs[i].Id}' costs more than '{rungs[i - 1].Id}' and gives less");
                }
            }
        }

        [Test]
        public void EveryBuiltInProductGrantsCurrencyAndOnlyCurrency()
        {
            foreach (var product in StoreCatalog.Default.Products)
            {
                Assert.IsTrue(product.Credits > 0 || product.Gems > 0, product.Id);

                // The property the whole feature rests on. A product that granted hearts
                // would need the client to apply half a purchase, which means a record in
                // the save of what has already been applied — see StoreProduct.
                Assert.AreEqual("store.product." + product.Id, product.NameKey);
            }
        }

        // ------------------------------------------------------- the shipped catalog
        /// <summary>
        /// The built-in ladder and the shipped content agree.
        ///
        /// <para>
        /// Editor-only, because it reads <c>Application.dataPath</c>. It earns the split:
        /// <c>StoreCatalog.Default</c> exists so that a failed content read costs live
        /// tuning rather than the shop, and a fallback that has drifted from the file is a
        /// shop promising one amount before the content loads and another after — against a
        /// server that only ever honours the published one.
        /// </para>
        /// </summary>
        [Test]
        public void TheBuiltInLadderMatchesTheShippedContent()
        {
            string path = Path.Combine(Application.dataPath, "StreamingAssets", "Content",
                                       "progression.json");

            if (!File.Exists(path)) Assert.Inconclusive("progression.json is not on disk");

            var problems = new List<string>();
            Assert.IsTrue(ProgressionTable.TryRead(File.ReadAllText(path), out var table, problems),
                          string.Join("; ", problems));

            var shipped = table.Store;
            var built = StoreCatalog.Default;

            Assert.AreEqual(built.Products.Count, shipped.Products.Count, "product count");
            Assert.AreEqual(built.Goods.Count, shipped.Goods.Count, "good count");

            foreach (var product in built.Products)
            {
                var live = shipped.Find(product.Id);
                Assert.IsNotNull(live, $"progression.json is missing '{product.Id}'");

                Assert.AreEqual(product.Credits, live.Credits, product.Id + " credits");
                Assert.AreEqual(product.Gems, live.Gems, product.Id + " gems");
                Assert.AreEqual(product.Kind, live.Kind, product.Id + " kind");
                Assert.AreEqual(product.Shelf, live.Shelf, product.Id + " shelf");
                Assert.AreEqual(product.ReferenceUsdCents, live.ReferenceUsdCents, product.Id + " price");
                Assert.AreEqual(product.Badge, live.Badge, product.Id + " badge");
            }

            foreach (var good in built.Goods)
            {
                var live = shipped.FindGood(good.Id);
                Assert.IsNotNull(live, $"progression.json is missing '{good.Id}'");

                Assert.AreEqual(good.Kind, live.Kind, good.Id + " kind");
                Assert.AreEqual(good.Amount, live.Amount, good.Id + " amount");
                Assert.AreEqual(good.Gems, live.Gems, good.Id + " price");
            }

            AssertLadderRises(shipped);
        }

        // ------------------------------------------------------------------ helpers
        static void Publish(StoreDto store)
        {
            var problems = new List<string>();
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 0,
                maxLevel = 10,
                store = store,
            };

            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, problems),
                          string.Join("; ", problems));

            ProgressionRules.Publish(table);
        }

        static SaveFileDto FreshSave() => new SaveFileDto { schemaVersion = SaveSchema.Version };
    }
}
