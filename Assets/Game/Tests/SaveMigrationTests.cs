using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The most dangerous code in the project.
    ///
    /// This import runs exactly once per player, on the launch after they update, and
    /// it is the only thing standing between the old index-keyed PlayerPrefs and the
    /// new id-keyed save file. If it maps a star onto the wrong level, nobody finds
    /// out until the reviews arrive — there is no second chance to get it right.
    /// </summary>
    public sealed class SaveMigrationTests
    {
        const string KeyStars = "gg.stars.";
        const string KeyBest = "gg.best.";

        [SetUp]
        public void ClearPrefs() => PlayerPrefs.DeleteAll();

        [TearDown]
        public void CleanUp() => PlayerPrefs.DeleteAll();

        static SaveFileDto FreshFile() => new SaveFileDto
        {
            schemaVersion = SaveSchema.Version,
            settings = new SettingsDto(),
            wallet = WalletDto.Unwritten(),
            levels = new LevelRecordDto[0],
        };

        static LevelRecordDto Find(SaveFileDto dto, string id)
            => System.Array.Find(dto.levels, r => r.levelId == id);

        [Test]
        public void LegacyStarsLandOnTheLevelThatEarnedThem()
        {
            // the pre-1.0 build wrote progress against array positions
            PlayerPrefs.SetInt(KeyStars + 0, 3);
            PlayerPrefs.SetInt(KeyBest + 0, 34);
            PlayerPrefs.SetInt(KeyStars + 1, 2);
            PlayerPrefs.SetInt(KeyBest + 1, 61);

            var dto = FreshFile();
            Assert.IsTrue(LegacyPlayerPrefsImport.Apply(dto));

            var first = Find(dto, "c01_first_light");
            Assert.IsNotNull(first, "index 0 must map to c01_first_light");
            Assert.AreEqual(3, first.stars);
            Assert.AreEqual(34, first.bestMoves);

            var second = Find(dto, "c01_twin_streams");
            Assert.IsNotNull(second, "index 1 must map to c01_twin_streams");
            Assert.AreEqual(2, second.stars);
            Assert.AreEqual(61, second.bestMoves);

            Assert.IsNull(Find(dto, "c01_prism_heart"), "an unplayed level gains no record");
        }

        [Test]
        public void ImportRunsOnlyOnce()
        {
            PlayerPrefs.SetInt(KeyStars + 0, 1);

            var dto = FreshFile();
            LegacyPlayerPrefsImport.Apply(dto);
            Assert.IsTrue(dto.legacyImportDone);

            // a second pass must not resurrect or double-count anything
            Assert.IsFalse(LegacyPlayerPrefsImport.Apply(dto));
        }

        [Test]
        public void ExistingProgressIsNeverDowngradedByOlderLegacyData()
        {
            PlayerPrefs.SetInt(KeyStars + 0, 1);
            PlayerPrefs.SetInt(KeyBest + 0, 90);

            var dto = FreshFile();
            dto.levels = new[]
            {
                new LevelRecordDto { levelId = "c01_first_light", stars = 3, bestMoves = 34, clears = 5 },
            };

            LegacyPlayerPrefsImport.Apply(dto);

            var record = Find(dto, "c01_first_light");
            Assert.AreEqual(3, record.stars, "a worse legacy star count must not win");
            Assert.AreEqual(34, record.bestMoves, "a worse legacy move count must not win");
        }

        [Test]
        public void NoLegacyDataStillMarksTheImportDone()
        {
            var dto = FreshFile();

            Assert.IsTrue(LegacyPlayerPrefsImport.Apply(dto));
            Assert.IsTrue(dto.legacyImportDone);
            Assert.AreEqual(0, dto.levels.Length);
        }

        [Test]
        public void SettingsCarryOverButOnlyWhenTheyWereWritten()
        {
            PlayerPrefs.SetInt("gg.music", 0);

            var dto = FreshFile();
            LegacyPlayerPrefsImport.Apply(dto);

            Assert.IsFalse(dto.settings.music.Resolve(true), "music was explicitly off");
            Assert.IsTrue(dto.settings.sfx.Resolve(true), "sfx was never written, so it defaults on");
        }

        [Test]
        public void EveryLegacyIdStillExistsInTheShippedCatalog()
        {
            // If this fails, a level that shipped in the original build has been
            // removed or renamed, and updating players' stars would land nowhere.
            var catalog = LoadBundledCatalog();
            if (catalog.IsEmpty) Assert.Ignore("no bundled content available in this run");

            CollectionAssert.IsEmpty(
                LegacyPlayerPrefsImport.MissingFromCatalog(catalog),
                "LegacyIndexOrder is frozen; a level it names must never leave the catalog");
        }

        internal static LevelCatalog LoadBundledCatalog()
        {
            var source = new Content.Sources.BundledContentSource();
            var result = new LevelRepository(source).LoadAsync().GetAwaiter().GetResult();
            return result.Catalog;
        }
    }
}
