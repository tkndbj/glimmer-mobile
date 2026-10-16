using System;
using System.Collections.Generic;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Shuffle;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    public sealed class ShuffleRewardTests
    {
        static readonly LevelId Watch = LevelId.Parse("s13_shuffle");

        [SetUp]
        public void Reset()
        {
            EndlessCoins.UseStore(new EndlessCoins.MemoryStore());
            ShuffleLedger.LoadFrom(new SaveFileDto());
        }

        [TearDown]
        public void Restore()
        {
            ShuffleLedger.LoadFrom(new SaveFileDto());
            EndlessCoins.UseStore(null);
        }

        static SaveFileDto Snapshot()
        {
            var save = new SaveFileDto();
            ShuffleLedger.WriteInto(save);
            return save;
        }

        static ShuffleWavesDto Counter(string device, long waves)
            => new ShuffleWavesDto { device = device, waves = waves };

        static SaveFileDto Save(params ShuffleWavesDto[] counters)
            => new SaveFileDto
            {
                shuffleBest = new[] { new ShuffleBestDto { level = Watch.Value, rewards = counters } },
            };

        [Test]
        public void EveryRunPaysEvenWhenItDoesNotBeatTheBest()
        {
            ShuffleLedger.Record(Watch, 100);
            Assert.AreEqual(0L, ShuffleLedger.LifetimeWaves, "old bests are not new paid waves");
            foreach (int waves in new[] { 12, 12, 3 }) Assert.AreEqual(waves, ShuffleLedger.Bank(Watch, waves));
            Assert.AreEqual(100, ShuffleLedger.BestFor(Watch));
            Assert.AreEqual(27L, ShuffleLedger.LifetimeWaves);
            Assert.AreEqual(270L, ShuffleRewards.XpFor(ShuffleLedger.LifetimeWaves));
            Assert.AreEqual(270L, ShuffleRewards.CreditsFor(ShuffleLedger.LifetimeWaves));
        }

        [Test]
        public void NoClearedWavesAndAnInvalidLevelPayNothing()
        {
            Assert.AreEqual(0L, ShuffleLedger.Bank(Watch, 0));
            Assert.AreEqual(0L, ShuffleLedger.Bank(Watch, -1));
            Assert.AreEqual(0L, ShuffleLedger.Bank(LevelId.None, 8));
            Assert.IsEmpty(Snapshot().shuffleBest);
        }

        [Test]
        public void OldSavesKeepTheirBestAndStartRewardsAtZero()
        {
            var old = new SaveFileDto
            {
                schemaVersion = 43,
                shuffleBest = new[] { new ShuffleBestDto { level = Watch.Value, wave = 50 } },
            };
            ShuffleLedger.LoadFrom(old);
            Assert.AreEqual(0L, ShuffleRewards.WavesIn(old));
            Assert.AreEqual(50, ShuffleLedger.BestFor(Watch));
            ShuffleLedger.Bank(Watch, 6);
            ShuffleLedger.LoadFrom(Snapshot());
            ShuffleLedger.Bank(Watch, 4);
            Assert.AreEqual(10L, ShuffleLedger.LifetimeWaves);
            Assert.AreEqual(50, ShuffleLedger.BestFor(Watch));
        }

        [Test]
        public void ConcurrentOfflineRunsSurviveAndRetryingTheMergePaysNothingExtra()
        {
            ShuffleLedger.Bank(Watch, 5);
            var shared = Snapshot();
            ShuffleLedger.Bank(Watch, 7);
            var phoneA = Snapshot();

            EndlessCoins.UseStore(new EndlessCoins.MemoryStore());
            ShuffleLedger.LoadFrom(shared);
            ShuffleLedger.Bank(Watch, 3);
            var phoneB = Snapshot();

            var merged = SaveMerge.Join(phoneA, phoneB);
            Assert.AreEqual(15L, ShuffleRewards.WavesIn(merged));
            Assert.AreEqual(15L, ShuffleRewards.WavesIn(SaveMerge.Join(phoneB, phoneA)));
            Assert.AreEqual(15L, ShuffleRewards.WavesIn(SaveMerge.Join(merged, phoneA)));
            Assert.AreEqual(15L, ShuffleRewards.WavesIn(SaveMerge.Join(merged, merged)));
            Assert.AreEqual(12L, ShuffleRewards.WavesIn(phoneA), "joining must not mutate an input");
            Assert.AreEqual(8L, ShuffleRewards.WavesIn(phoneB));
        }

        [Test]
        public void RewardOnlyRowsSurviveTheWireAndTriggerASaveDelta()
        {
            ShuffleLedger.Bank(Watch, 7);
            var save = Snapshot();
            var back = FirestoreSaveMapper.FromDocument(FirestoreSaveMapper.ToDocument(save));
            Assert.AreEqual(0, back.shuffleBest[0].wave, "forfeits need no new best");
            Assert.AreEqual(7L, ShuffleRewards.WavesIn(back));
            Assert.IsTrue(SaveDelta.Between(save, back).IsEmpty);
            var changed = Save(Counter(save.shuffleBest[0].rewards[0].device, 8));
            Assert.IsFalse(SaveDelta.Between(save, changed).IsEmpty);
        }

        [Test]
        public void RewardsAreIndependentOfTheInfiniteLaneAndOfTheOtherAccount()
        {
            long endless = EndlessLedger.LifetimeWaves;
            int paidToday = EndlessCoins.PaidToday;
            ShuffleLedger.Bank(Watch, 9);
            Assert.AreEqual(endless, EndlessLedger.LifetimeWaves);
            Assert.AreEqual(paidToday, EndlessCoins.PaidToday);
            var first = Snapshot();
            ShuffleLedger.LoadFrom(new SaveFileDto());
            ShuffleLedger.Bank(Watch, 2);
            Assert.AreEqual(2L, ShuffleLedger.LifetimeWaves);
            ShuffleLedger.LoadFrom(first);
            Assert.AreEqual(9L, ShuffleLedger.LifetimeWaves);
        }

        [Test]
        public void TheProgressionSumIncludesShuffleBeforeClampingTheBoost()
        {
            var xp = ProvableXp.Of(0L, 0L, 0L, 1000L, ProgressionTable.Default, null, 7L);
            Assert.AreEqual(70L, xp.Shuffle);
            Assert.AreEqual(0L, xp.Endless);
            Assert.AreEqual(0L, xp.Stars);
            Assert.AreEqual(XpBoost.BonusFrom(70L, 1000L, ProgressionTable.Default.XpBoost.MaxPercent), xp.Boost);
            var save = Save(Counter("a", 7));
            var offFile = ProvableXp.In(save, null, ProgressionTable.Default, null);
            Assert.AreEqual(70L, offFile.Total);
        }

        [Test]
        public void BankingSaturatesWithoutOverflowOrChangingThePublishedBest()
        {
            ShuffleLedger.LoadFrom(Save(Counter(ShuffleRewards.DeviceId, ShuffleRewards.MaxWavesPerDevice - 2)));
            Assert.AreEqual(2L, ShuffleLedger.Bank(Watch, int.MaxValue));
            Assert.AreEqual(0L, ShuffleLedger.Bank(Watch, 5));
            Assert.AreEqual(ShuffleRewards.MaxWavesPerDevice, ShuffleLedger.LifetimeWaves);
            Assert.AreEqual(0, ShuffleLedger.Best);
            Assert.AreEqual(40_960_000_000_000L, ShuffleRewards.CreditsFor(long.MaxValue));
        }

        [Test]
        public void AnInventedLevelCannotMintCoins()
        {
            var map = new FixedChapterMap();
            map.Add(Watch.Value, "s13_shufflewatch");
            var save = Save(Counter("a", 7));
            Assert.AreEqual(70L, ShuffleRewards.CreditsIn(save, map));
            save.shuffleBest[0].level = "invented";
            Assert.AreEqual(0L, ShuffleRewards.CreditsIn(save, map));
            Assert.AreEqual(0L, ShuffleRewards.CreditsIn(save, null));
        }

        [Test]
        public void MalformedWireCountsCannotMintCurrencyOrOverflow()
        {
            foreach (object raw in new object[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity,
                                                 "10", new object(), null, -1L })
                Assert.AreEqual(0L, ShuffleRewards.ReadWaves(raw));
            Assert.AreEqual(ShuffleRewards.MaxWavesPerDevice, ShuffleRewards.ReadWaves(double.MaxValue));
            Assert.AreEqual(ShuffleRewards.MaxWavesPerDevice, ShuffleRewards.ReadWaves(long.MaxValue));
            Assert.AreEqual(3L, ShuffleRewards.ReadWaves(3.9));
        }

        [Test]
        public void BankingRaisesAnIntentAndLoadingDoesNot()
        {
            int paid = 0;
            Action onPaid = () => paid++;
            ShuffleLedger.Paid += onPaid;
            try
            {
                ShuffleLedger.Bank(Watch, 1);
                ShuffleLedger.Bank(Watch, 0);
                ShuffleLedger.LoadFrom(Snapshot());
                Assert.AreEqual(1, paid);
            }
            finally { ShuffleLedger.Paid -= onPaid; }
        }

        [Test]
        public void EverySharedVectorAgreesWithTheServer()
        {
            var file = TestJson.ReadShared("shuffle-reward-vectors.json");
            var constants = TestJson.Child(file, "constants");
            Assert.AreEqual(ShuffleRewards.XpPerWave, TestJson.Int(constants, "xpPerWave"));
            Assert.AreEqual(ShuffleRewards.CreditsPerWave, TestJson.Int(constants, "creditsPerWave"));
            Assert.AreEqual(ShuffleRewards.MaxDevices, TestJson.Int(constants, "maxDevices"));
            Assert.AreEqual(ShuffleLedger.MaxRows, TestJson.Int(constants, "maxRows"));
            Assert.AreEqual(ShuffleRewards.MaxDeviceIdLength, TestJson.Int(constants, "maxDeviceIdLength"));
            Assert.AreEqual(ShuffleRewards.MaxWavesPerDevice, TestJson.Long(constants, "maxWavesPerDevice"));

            foreach (var raw in TestJson.Children(file, "cases"))
            {
                var test = TestJson.Object(raw);
                var rows = new List<ShuffleBestDto>();
                foreach (var item in TestJson.Children(test, "rows"))
                {
                    if (item == null) { rows.Add(null); continue; }
                    var row = TestJson.Object(item);
                    var counters = new List<ShuffleWavesDto>();
                    var entries = row.TryGetValue("rewards", out object rewards)
                        ? TestJson.Array(rewards) : new List<object>();
                    foreach (var entry in entries)
                    {
                        if (entry == null) { counters.Add(null); continue; }
                        var count = TestJson.Object(entry);
                        counters.Add(Counter(TestJson.Str(count, "device"), TestJson.Long(count, "waves")));
                    }
                    rows.Add(new ShuffleBestDto
                    {
                        level = TestJson.Str(row, "level"), wave = TestJson.Int(row, "wave"),
                        rewards = counters.ToArray(),
                    });
                }
                var save = new SaveFileDto { shuffleBest = rows.ToArray() };
                long waves = ShuffleRewards.WavesIn(save);
                string name = TestJson.Str(test, "name");
                Assert.AreEqual(TestJson.Long(test, "waves"), waves, name);
                Assert.AreEqual(TestJson.Long(test, "xp"), ShuffleRewards.XpFor(waves), name);
                Assert.AreEqual(TestJson.Long(test, "credits"), ShuffleRewards.CreditsFor(waves), name);
            }
        }
    }
}
