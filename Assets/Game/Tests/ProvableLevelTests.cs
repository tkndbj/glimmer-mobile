using GlimmerGrove.Challenges;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The keeper level a card is predicted with: what the server will publish for a save, read
    /// off that save, with no high-water floor.
    ///
    /// <para>
    /// <b>The fault this pins (2026-10-14).</b> A live account stood at level 14 on its own device
    /// - a floor written while stars in since-retired chapters still counted - and at 9 on its
    /// public profile, with no badge. The server was right to say 9 at the moment it built the
    /// card; it was never asked again, because the publish fingerprint carried no level and the
    /// device's badge reading came from the floored level, so ten level clears and a boost moved
    /// nothing the fingerprint covered. The server's own code on the same save answered 11 and
    /// <c>cinderling</c>.
    /// </para>
    /// <para>
    /// Every expected figure is written out by hand against <see cref="ProgressionTable.Default"/>,
    /// which is the shipped curve: 100, 150, 200, 260, 330, 410, 500 to the next level, so level 3
    /// opens at 250, 4 at 450, 6 at 1,040 and 7 at 1,450. A clear is worth 60 plus 30 a star. Offline
    /// throughout - the catalog is built from DTOs, so nothing needs the Editor (29e).
    /// </para>
    /// </summary>
    public sealed class ProvableLevelTests
    {
        static CatalogIndex Catalog()
        {
            var builder = new CatalogIndexBuilder();

            builder.Add(new ManifestChapterDto
            {
                id = "s01_one", order = 10, version = 1, mode = "siege",
                levels = new[] { "one_a", "one_b", "one_c" },
            }, 1);

            builder.Add(new ManifestChapterDto
            {
                id = "s02_endless", order = 20, version = 1, mode = "siege", track = "infinite",
                levels = new[] { "endless_a" },
            }, 1);

            return builder.Build();
        }

        /// <summary>Two glades at three stars: 2 x (60 + 90) = 300 XP, level 3.</summary>
        static SaveFileDto Save()
            => new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                wallet = new WalletDto { displayName = "Fern" },
                levels = new[]
                {
                    new LevelRecordDto { levelId = "one_a", stars = 3, bestMoves = 10, clears = 1 },
                    new LevelRecordDto { levelId = "one_b", stars = 3, bestMoves = 10, clears = 1 },
                },
            };

        static ProvableLevel Read(SaveFileDto save)
            => ProvableLevel.In(save, Catalog(), ProgressionTable.Default, ChallengeTable.Default.Rewards);

        // ================================================================= tests
        [Test]
        public void TheStarLedgerIsReadOffTheFile()
        {
            var xp = ProvableXp.In(Save(), Catalog(), ProgressionTable.Default, ChallengeTable.Default.Rewards);

            Assert.AreEqual(300L, xp.Stars);
            Assert.AreEqual(300L, xp.Total);
            Assert.AreEqual(3, Read(Save()).Earned.Level);
            Assert.AreEqual(3, Read(Save()).Level.Level);
        }

        [Test]
        public void TheHighWaterFloorIsNeverRead()
        {
            // The device's floors are client-written, and `publishGrove` never reads them. The
            // live account's own figures: a floor at 14 over a ledger that proves far less.
            var floored = Save();
            floored.progression = new ProgressionStateDto { xpHighWater = 7840L, levelHighWater = 14 };

            Assert.AreEqual(3, Read(floored).Level.Level, "a floor is the device's, never the card's");
        }

        [Test]
        public void ARecordTheCatalogDoesNotListEarnsNothing()
        {
            // A retired chapter's record (the spent-id table) or a hidden chapter's: neither is in
            // the index, and `levelChapters` on the server is built from the same manifest.
            var retired = Save();
            retired.levels = new[]
            {
                retired.levels[0], retired.levels[1],
                new LevelRecordDto { levelId = "b01_everbloom", stars = 3, bestMoves = 10, clears = 1 },
            };

            Assert.AreEqual(300L,
                ProvableXp.In(retired, Catalog(), ProgressionTable.Default, ChallengeTable.Default.Rewards).Stars);
        }

        [Test]
        public void TheSumIsTheServersSum()
        {
            // 300 from stars, 25 waves at 15 = 375, and a banked boost of 900 under its ceiling of
            // (300 + 375) x 150% = 1,012. 1,575 in all: level 7 (1,450 to 1,950).
            var save = Save();
            save.endlessBest = new[] { new EndlessBestDto { level = "endless_a", wave = 25, waves = 25 } };
            save.wallet.xpBoostEarned = 900L;

            var xp = ProvableXp.In(save, Catalog(), ProgressionTable.Default, ChallengeTable.Default.Rewards);

            Assert.AreEqual(300L, xp.Stars);
            Assert.AreEqual(375L, xp.Endless);
            Assert.AreEqual(900L, xp.Boost);
            Assert.AreEqual(1575L, xp.Total);
            Assert.AreEqual(7, Read(save).Level.Level);

            // And a forged bonus is clamped to the same ceiling `xpBoostXp` applies.
            save.wallet.xpBoostEarned = 5000L;
            Assert.AreEqual(1012L,
                ProvableXp.In(save, Catalog(), ProgressionTable.Default, ChallengeTable.Default.Rewards).Boost);
        }

        [Test]
        public void TheRuleIsTheLiveRule()
        {
            // `PlayerProgression.EnsureFresh` reads its addends through `Of` too, so this is the
            // statement both readings share: each source through its own table, the boost
            // clamped against the three of them.
            var rewards = ChallengeTable.Default.Rewards;
            var xp = ProvableXp.Of(300L, 25L, 4L, 0L, ProgressionTable.Default, rewards);

            Assert.AreEqual(ProgressionTable.Default.Endless.XpFor(25L), xp.Endless);
            Assert.AreEqual(rewards.XpFor(4L), xp.Challenges);
            Assert.AreEqual(0L, xp.Boost);
            Assert.AreEqual(300L + xp.Endless + xp.Challenges, xp.Total);

            Assert.AreEqual(XpBoost.BonusFrom(xp.Total, 2000L, ProgressionTable.Default.XpBoost.MaxPercent),
                ProvableXp.Of(300L, 25L, 4L, 2000L, ProgressionTable.Default, rewards).Boost);
        }

        [Test]
        public void ABoughtLevelMovesTheCardAndNotTheBadge()
        {
            // Invariant 57: the card carries earned plus bought, the rank is asked about earned.
            var bought = Save();
            bought.wallet.keeperLevelsBought = 2;

            Assert.AreEqual(3, Read(bought).Earned.Level);
            Assert.AreEqual(5, Read(bought).Level.Level);
        }

        [Test]
        public void ALevelGainedOwesAPublish()
        {
            // The other half of the fault: the level is drawn on a public profile, so it is part
            // of what the fingerprint follows.
            string before = GroveCard.OfSave(Save(), "uid", Read(Save()).Level.Level, 1_000L).Fingerprint();

            var further = Save();
            further.levels = new[]
            {
                further.levels[0], further.levels[1],
                new LevelRecordDto { levelId = "one_c", stars = 3, bestMoves = 10, clears = 1 },
            };

            // 450 XP: level 4.
            Assert.AreEqual(4, Read(further).Level.Level);
            Assert.AreNotEqual(before,
                GroveCard.OfSave(further, "uid", Read(further).Level.Level, 1_000L).Fingerprint());
        }

        [Test]
        public void NothingAtAllIsLevelOne()
        {
            Assert.AreEqual(0L, ProvableXp.In(null, null, null, null).Total);
            Assert.AreEqual(1, ProvableLevel.In(null, null, null, null).Level.Level);
            Assert.AreEqual(1, ProvableLevel.In(new SaveFileDto(), Catalog(), null, null).Level.Level);
        }
    }
}
