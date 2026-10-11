using System.Collections.Generic;
using GlimmerGrove.Ads;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The video continue (2026-10-11): watch, and a lost run carries on beside the gem price.
    /// The content field it is authored as, the per-run rule, and the offer that stands on a video
    /// for a player who cannot pay gems.
    /// </summary>
    public sealed class ContinueVideoTests
    {
        [TearDown]
        public void Restore() => ProgressionRules.Reset();

        /// <summary>Installs a continue table so the live facade reads the authored numbers.</summary>
        static void Publish(ContinueDto carryOn)
        {
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 10,
                continueRun = carryOn,
            };

            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, new List<string>()));
            ProgressionRules.Publish(table);
        }

        static AdsDto Block(int perRun) => new AdsDto
        {
            continueVideosPerRun = perRun,
            placements = new[]
            {
                new AdPlacementDto { id = AdPlacement.CoinBonus, kind = "credits", amount = 300, dailyCap = 6 },
            },
        };

        // ---------------------------------------------------------------------- content
        [Test]
        public void AnUnwrittenCountInheritsOneARun()
        {
            var problems = new List<string>();

            Assert.AreEqual(1, AdRewardTable.Resolve(Block(-1), problems).ContinueVideosPerRun);
            Assert.AreEqual(1, AdRewardTable.Default.ContinueVideosPerRun);
            Assert.IsEmpty(problems);
        }

        [Test]
        public void NoughtSwitchesTheOfferOff()
        {
            var problems = new List<string>();

            Assert.AreEqual(0, AdRewardTable.Resolve(Block(0), problems).ContinueVideosPerRun);
            Assert.IsFalse(ContinueVideo.Allows(0, 0), "a run is never offered a switched-off video");
            Assert.IsEmpty(problems);
        }

        [Test]
        public void ACountAboveTheCeilingIsClampedAndNamed()
        {
            var problems = new List<string>();

            Assert.AreEqual(AdRules.MaxContinueVideosPerRun,
                            AdRewardTable.Resolve(Block(50), problems).ContinueVideosPerRun);
            Assert.IsNotEmpty(problems);
        }

        /// <summary>
        /// A row for the continue would be read as a reward and paid by the generic path on top
        /// of carrying the run on, so it is refused by name.
        /// </summary>
        [Test]
        public void ARewardRowForTheContinueIsRefused()
        {
            var problems = new List<string>();

            var table = AdRewardTable.Resolve(new AdsDto
            {
                placements = new[]
                {
                    new AdPlacementDto { id = AdPlacement.CoinBonus, kind = "credits", amount = 300, dailyCap = 6 },
                    new AdPlacementDto { id = AdPlacement.ContinueVideo, kind = "credits", amount = 300, dailyCap = 6 },
                },
            }, problems);

            Assert.IsFalse(table.Has(AdPlacement.ContinueVideo));
            Assert.IsTrue(problems.Exists(p => p.Contains(AdPlacement.ContinueVideo)));
        }

        [Test]
        public void TheShippedFileOffersOneVideoARun()
        {
            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(
                TestJson.RepoRoot(), "Assets", "StreamingAssets", "Content", "progression.json"));
            var ads = TestJson.Object(TestJson.Object(TestJson.Parse(text))["ads"]);

            Assert.IsTrue(ads.ContainsKey("continueVideosPerRun"),
                          "progression.json's ads block does not author continueVideosPerRun");
            Assert.AreEqual(1, System.Convert.ToInt32(ads["continueVideosPerRun"]));
        }

        /// <summary>Not <c>run_continue</c>, which is spent: an id reaches a dashboard and history.</summary>
        [Test]
        public void ThePlacementIsNotTheRetiredOne()
        {
            Assert.AreNotEqual("run_continue", AdPlacement.ContinueVideo);
            Assert.IsTrue(AdPlacement.IsKnown(AdPlacement.ContinueVideo));
            Assert.IsFalse(AdPlacement.IsKnown("run_continue"));
        }

        // ---------------------------------------------------------------------- per run
        [Test]
        public void OneARunMeansTheSecondContinueIsTheGemPrice()
        {
            Assert.IsTrue(ContinueVideo.Allows(1, 0));
            Assert.IsFalse(ContinueVideo.Allows(1, 1));

            Assert.IsTrue(ContinueVideo.Allows(3, 2));
            Assert.IsFalse(ContinueVideo.Allows(3, 3));
        }

        // ---------------------------------------------------------------------- the offer
        /// <summary>
        /// The player the video is for: short of gems with no shop to buy them in. The gem offer
        /// alone withdraws (no control that cannot work); with a video it stands on the video.
        /// </summary>
        [Test]
        public void AVideoKeepsTheOfferStandingForAPlayerWhoCannotPayGems()
        {
            Publish(new ContinueDto { gems = 20, turns = 15 });

            var gemsOnly = RunContinue.Offer(ContinueUnit.Turns, deficit: 0, taken: 0,
                                             gemsHeld: 0, gemsForSale: false);
            Assert.IsFalse(gemsOnly.Exists, "without a video nothing can be pressed");

            var withVideo = RunContinue.Offer(ContinueUnit.Turns, deficit: 0, taken: 0,
                                              gemsHeld: 0, gemsForSale: false, video: true);

            Assert.IsTrue(withVideo.Exists);
            Assert.IsTrue(withVideo.Video);
            Assert.IsFalse(withVideo.ForGems, "and the gem key is not drawn");
            Assert.AreEqual(15, withVideo.Amount, "a video hands over what the gems would have");
        }

        [Test]
        public void AVideoStandsBesideTheGemPriceRatherThanReplacingIt()
        {
            Publish(new ContinueDto { gems = 20, turns = 15 });

            var offer = RunContinue.Offer(ContinueUnit.Turns, deficit: 0, taken: 0,
                                          gemsHeld: 100, gemsForSale: true, video: true);

            Assert.IsTrue(offer.ForGems);
            Assert.IsTrue(offer.Affordable);
            Assert.IsTrue(offer.Video);
            Assert.AreEqual(20L, offer.Gems);
        }

        /// <summary>A video never opens what the rules closed: no continue, no video either.</summary>
        [Test]
        public void AVideoNeverRescuesWhatTheRulesWithdrew()
        {
            Publish(new ContinueDto { enabled = 0 });
            Assert.IsFalse(RunContinue.Offer(ContinueUnit.Turns, 0, 0, 0, false, video: true).Exists);

            ProgressionRules.Reset();
            Assert.IsFalse(RunContinue.Offer(ContinueUnit.Ink, RunContinue.NoContinue, 0, 0, false,
                                             video: true).Exists,
                           "a board no allowance can save is not sold one for a video either");
        }

        /// <summary>
        /// The debit asks for the gem half, never merely an offer: a video-only offer carries a
        /// price the player was never shown.
        /// </summary>
        [Test]
        public void AVideoOnlyOfferCanNeverBeChargedGems()
        {
            Publish(new ContinueDto { gems = 20, turns = 15 });

            var offer = RunContinue.Offer(ContinueUnit.Turns, deficit: 0, taken: 0,
                                          gemsHeld: 0, gemsForSale: false, video: true);

            Assert.IsFalse(RunContinue.TryBuy(offer, LevelId.Parse("t_level")));
        }
    }
}
