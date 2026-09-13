using System.Collections.Generic;
using System.Threading;
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
            //
            // Asked of the manifest rather than the index, because a chapter hidden behind
            // `disabled` is not a chapter deleted: its levels are still listed and still on
            // disk, and flipping the boolean back makes those stars land again. The index
            // drops a disabled chapter whole, so reading it turned invariant 38 into a
            // violation of invariant 2 the day the glade was hidden.
            var shipped = EveryManifestLevelId();
            if (shipped.Count == 0) Assert.Ignore("no bundled content available in this run");

            CollectionAssert.IsEmpty(
                LegacyPlayerPrefsImport.MissingFrom(shipped),
                "LegacyIndexOrder is frozen; a level it names must never leave the catalog");
        }

        /// <summary>
        /// The manifest exactly as it ships, disabled chapters and all.
        ///
        /// <para>
        /// Deliberately not <see cref="LoadBundledIndex"/>, which drops a disabled chapter
        /// whole. Anything asking "does this still exist" rather than "can this be played
        /// today" has to read the manifest, or hiding a mode behind one boolean reads as
        /// content having been deleted.
        /// </para>
        /// </summary>
        internal static ManifestDto LoadBundledManifest()
        {
            var source = new Content.Sources.BundledContentSource();
            var fetch = source.FetchAsync(ContentPaths.Manifest, CancellationToken.None)
                              .GetAwaiter().GetResult();
            if (!fetch.Success) return null;

            return ContentMapper.ReadManifest(fetch.Text, out _);
        }

        /// <summary>Every level id the manifest names, in every chapter, hidden or not.</summary>
        internal static HashSet<string> EveryManifestLevelId()
        {
            var ids = new HashSet<string>(System.StringComparer.Ordinal);

            var manifest = LoadBundledManifest();
            if (manifest?.chapters == null) return ids;

            foreach (var chapter in manifest.chapters)
            {
                if (chapter?.levels == null) continue;
                foreach (var id in chapter.levels)
                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }

            return ids;
        }

        /// <summary>
        /// The manifest index, which is all the boot path ever reads. Cheap enough that
        /// tests about identity and ordering never touch a chapter body.
        /// </summary>
        internal static CatalogIndex LoadBundledIndex()
        {
            var source = new Content.Sources.BundledContentSource();
            var result = new LevelRepository(source).LoadAsync().GetAwaiter().GetResult();
            return result.Catalog.Index;
        }

        /// <summary>
        /// Every shipped level, bodies and all. Only the tests that genuinely inspect
        /// grids use this — the same split the game itself makes.
        /// </summary>
        internal static System.Collections.Generic.List<LevelDefinition> LoadBundledLevels()
        {
            var source = new Content.Sources.BundledContentSource();
            var result = new LevelRepository(source).LoadEverythingAsync().GetAwaiter().GetResult();

            var levels = new System.Collections.Generic.List<LevelDefinition>();
            foreach (var body in result.Catalog.LoadedBodies()) levels.AddRange(body.Levels);
            return levels;
        }
    }
}
