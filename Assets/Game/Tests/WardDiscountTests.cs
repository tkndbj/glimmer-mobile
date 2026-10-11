using System.Collections.Generic;
using GlimmerGrove.Ads;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The turret discount (2026-10-10): watch a video, pay part of a turret's or a star's price.
    /// The arithmetic, the content field it is authored as, and a discounted purchase end to end.
    /// </summary>
    public sealed class WardDiscountTests
    {
        [SetUp]
        public void Open()
        {
            SaveService.Unload();
            EndlessCoins.UseStore(new EndlessCoins.MemoryStore());
            SaveService.LoadWith(new MemoryStore());
        }

        [TearDown]
        public void Restore()
        {
            SaveService.Unload();
            EndlessCoins.UseStore(null);
            ProgressionRules.Reset();
        }

        // ------------------------------------------------------------------- arithmetic
        [Test]
        public void HalfOffHalvesAndTheOddCoinStaysWithTheShop()
        {
            Assert.AreEqual(600L, WardDiscount.PriceOf(1200L, 50));
            Assert.AreEqual(601L, WardDiscount.PriceOf(1201L, 50));

            // Never free: a price of one stays one.
            Assert.AreEqual(1L, WardDiscount.PriceOf(1L, 50));
        }

        [Test]
        public void NoughtPercentOrNoPriceChangesNothing()
        {
            Assert.AreEqual(500L, WardDiscount.PriceOf(500L, 0));
            Assert.AreEqual(0L, WardDiscount.PriceOf(0L, 50));
        }

        [Test]
        public void ThePercentIsClampedSoATypoCannotGiveTheShelfAway()
        {
            Assert.AreEqual(10L, WardDiscount.PriceOf(100L, 100));
            Assert.AreEqual(100L - 100L * AdRules.MaxTurretDiscountPercent / 100,
                            WardDiscount.PriceOf(100L, 250));
        }

        // ---------------------------------------------------------------------- content
        static AdsDto Block(int percent) => new AdsDto
        {
            turretDiscountPercent = percent,
            placements = new[]
            {
                new AdPlacementDto { id = AdPlacement.CoinBonus, kind = "credits", amount = 300, dailyCap = 6 },
            },
        };

        [Test]
        public void AnUnwrittenPercentInheritsTheOwnersFifty()
        {
            var problems = new List<string>();

            Assert.AreEqual(50, AdRewardTable.Resolve(Block(-1), problems).TurretDiscountPercent);
            Assert.AreEqual(50, AdRewardTable.Default.TurretDiscountPercent);
            Assert.IsEmpty(problems);
        }

        [Test]
        public void NoughtSwitchesTheOfferOff()
        {
            var problems = new List<string>();

            Assert.AreEqual(0, AdRewardTable.Resolve(Block(0), problems).TurretDiscountPercent);
            Assert.IsEmpty(problems);
        }

        [Test]
        public void APercentAboveTheCeilingIsClampedAndNamed()
        {
            var problems = new List<string>();

            Assert.AreEqual(AdRules.MaxTurretDiscountPercent,
                            AdRewardTable.Resolve(Block(100), problems).TurretDiscountPercent);
            Assert.IsNotEmpty(problems);
        }

        /// <summary>
        /// A row for the discount would be read as a reward and paid by the generic path on top
        /// of the discount, so it is refused by name.
        /// </summary>
        [Test]
        public void ARewardRowForTheDiscountIsRefused()
        {
            var problems = new List<string>();

            var table = AdRewardTable.Resolve(new AdsDto
            {
                placements = new[]
                {
                    new AdPlacementDto { id = AdPlacement.CoinBonus, kind = "credits", amount = 300, dailyCap = 6 },
                    new AdPlacementDto { id = AdPlacement.TurretDiscount, kind = "credits", amount = 300, dailyCap = 6 },
                },
            }, problems);

            Assert.IsFalse(table.Has(AdPlacement.TurretDiscount));
            Assert.IsTrue(problems.Exists(p => p.Contains(AdPlacement.TurretDiscount)));
        }

        [Test]
        public void TheShippedFileOffersFiftyPercent()
        {
            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(
                TestJson.RepoRoot(), "Assets", "StreamingAssets", "Content", "progression.json"));
            var ads = TestJson.Object(TestJson.Object(TestJson.Parse(text))["ads"]);

            Assert.IsTrue(ads.ContainsKey("turretDiscountPercent"),
                          "progression.json's ads block does not author turretDiscountPercent");
            Assert.AreEqual(50, System.Convert.ToInt32(ads["turretDiscountPercent"]));
        }

        // ------------------------------------------------------------------------ offers
        [Test]
        public void ADiscountTurnsAShortPurseIntoAPurchase()
        {
            var offer = new WardOffer(WardPurchaseState.TooExpensive, 1200L, Currency.Credits, 700L, 3)
                        .Discounted();

            Assert.AreEqual(WardPurchaseState.Ready, offer.State);
            Assert.AreEqual(600L, offer.Cost);
            Assert.AreEqual(3, offer.RequiredLevel);
        }

        /// <summary>The gate is tested before the price (15a): a discount cannot open a wall.</summary>
        [Test]
        public void ADiscountNeverOpensAWall()
        {
            var walled = new WardOffer(WardPurchaseState.LevelLocked, 1200L, Currency.Credits, 5000L, 9);
            var held = new WardOffer(WardPurchaseState.AlreadyHeld, 1200L, Currency.Credits, 5000L);

            Assert.AreEqual(WardPurchaseState.LevelLocked, walled.Discounted().State);
            Assert.AreEqual(1200L, walled.Discounted().Cost);
            Assert.AreEqual(WardPurchaseState.AlreadyHeld, held.Discounted().State);
        }

        [Test]
        public void AStarIsDiscountedTheSameWay()
        {
            var star = new WardUpgradeOffer(WardUpgradeState.Short, 3, 900L, 500L).Discounted();

            Assert.AreEqual(WardUpgradeState.Ready, star.State);
            Assert.AreEqual(450L, star.Cost);
            Assert.AreEqual(3, star.Star);

            var topped = new WardUpgradeOffer(WardUpgradeState.Topped, 0, 0L, 500L).Discounted();
            Assert.AreEqual(WardUpgradeState.Topped, topped.State);
        }

        // -------------------------------------------------------------------- purchases
        static WardModel APricedTurret()
        {
            foreach (var model in WardLedger.Catalog.Models)
                if (!model.IsStarter && model.CoinPrice > 0) return model;

            Assert.Fail("the roster sells no turret for coins");
            return null;
        }

        [Test]
        public void ADiscountedPurchaseChargesTheDiscountedPrice()
        {
            var model = APricedTurret();
            long price = WardDiscount.PriceOf(model.CoinPrice);

            // Exactly the discounted price and not the full one: the case the offer is for. A
            // fresh save carries the account seed, so the purse is brought to the figure either way.
            long have = PlayerProgression.Credits;
            if (have < price) Wallet.Ledger(Currency.Credits).GrantLocally(price - have);
            else if (have > price) Assert.IsTrue(PlayerProgression.TrySpend(Currency.Credits, have - price, "test"));

            Assert.AreEqual(price, PlayerProgression.Credits);
            long before = PlayerProgression.Credits;

            Assert.IsFalse(WardLedger.TryBuy(model, 'r', 999), "bought at full price with half the coins");
            Assert.IsTrue(WardLedger.TryBuy(model, 'r', 999, discounted: true));

            Assert.AreEqual(before - price, PlayerProgression.Credits);
            Assert.IsTrue(WardLedger.IsHeld(model, 'r'));
            Assert.IsFalse(WardLedger.IsHeld(model, 'g'), "a discounted seat stood on another colour");
        }

        [Test]
        public void ADiscountedStarChargesTheDiscountedPrice()
        {
            var model = APricedTurret();
            Wallet.Ledger(Currency.Credits).GrantLocally(model.CoinPrice);
            Assert.IsTrue(WardLedger.TryBuy(model, 'b', 999));

            var star = WardUpgrade.OfferFor(model, 'b');
            if (star.State == WardUpgradeState.Topped) Assert.Ignore("the turret sells no star");

            long price = WardDiscount.PriceOf(star.Cost);
            long top = price - PlayerProgression.Credits;
            if (top > 0) Wallet.Ledger(Currency.Credits).GrantLocally(top);

            long before = PlayerProgression.Credits;
            int stars = WardStarLedger.StarsOf(model, 'b');

            Assert.IsTrue(WardUpgrade.TryBuy(model, 'b', discounted: true));

            Assert.AreEqual(before - price, PlayerProgression.Credits);
            Assert.AreEqual(stars + 1, WardStarLedger.StarsOf(model, 'b'));
        }

        // ------------------------------------------------------------------ plumbing
        /// <summary>A save that never reaches a disk. <c>HeartRescueTests</c>' store.</summary>
        sealed class MemoryStore : ISaveStore
        {
            SaveFileDto _file;

            public SaveFileDto Load() => _file ?? new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                levels = new LevelRecordDto[0],
                progression = ProgressionStateDto.Unwritten(),
                cloud = new CloudStateDto(),

                // Otherwise the load reaches LegacyPlayerPrefsImport, which is PlayerPrefs,
                // which is the Editor.
                legacyImportDone = true,
            };

            public bool Save(SaveFileDto dto)
            {
                _file = dto;
                return true;
            }

            public void Delete() => _file = null;
        }
    }
}
