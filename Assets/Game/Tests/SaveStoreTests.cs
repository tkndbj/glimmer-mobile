using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Progress has to survive a process being killed mid-write. These tests exercise
    /// the rotation and the integrity check against a real temporary directory,
    /// because the failure being guarded against is a filesystem one.
    /// </summary>
    public sealed class SaveStoreTests
    {
        string _dir;

        [SetUp]
        public void MakeDir()
        {
            _dir = Path.Combine(Application.temporaryCachePath, "savetests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void RemoveDir()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        SaveStore Store() => new SaveStore(_dir);

        static SaveFileDto FileWith(string levelId, int stars, int moves) => new SaveFileDto
        {
            schemaVersion = SaveSchema.Version,
            settings = new SettingsDto(),
            wallet = WalletDto.Unwritten(),
            levels = new[] { new LevelRecordDto { levelId = levelId, stars = stars, bestMoves = moves } },
            legacyImportDone = true,
        };

        [Test]
        public void RoundTripsARecord()
        {
            var store = Store();
            Assert.IsTrue(store.Save(FileWith("c01_first_light", 3, 34)));

            var loaded = store.Load();

            Assert.AreEqual(1, loaded.levels.Length);
            Assert.AreEqual("c01_first_light", loaded.levels[0].levelId);
            Assert.AreEqual(3, loaded.levels[0].stars);
            Assert.AreEqual(34, loaded.levels[0].bestMoves);
        }

        [Test]
        public void MissingFileYieldsUsableDefaults()
        {
            var loaded = Store().Load();

            Assert.IsNotNull(loaded.settings);
            Assert.IsNotNull(loaded.levels);
            Assert.AreEqual(0, loaded.levels.Length);
        }

        [Test]
        public void ASecondWriteRotatesThePreviousCopyToBackup()
        {
            var store = Store();
            store.Save(FileWith("a", 1, 10));
            store.Save(FileWith("b", 2, 20));

            Assert.IsTrue(File.Exists(Path.Combine(_dir, SaveSchema.FileName)));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, SaveSchema.BackupFileName)),
                          "the previous good file must be kept");
        }

        [Test]
        public void CorruptMainFileFallsBackToTheBackup()
        {
            var store = Store();
            store.Save(FileWith("older", 1, 10));    // becomes the backup
            store.Save(FileWith("newer", 2, 20));    // becomes the main file

            File.WriteAllText(Path.Combine(_dir, SaveSchema.FileName), "{ this is not json");

            var loaded = store.Load();

            Assert.AreEqual(1, loaded.levels.Length);
            Assert.AreEqual("older", loaded.levels[0].levelId,
                            "an unreadable main file must not lose the previous save");
        }

        [Test]
        public void TruncatedButParseableFileIsCaughtByTheChecksum()
        {
            var store = Store();
            store.Save(FileWith("older", 1, 10));
            store.Save(FileWith("newer", 2, 20));

            // still valid JSON, but a record has been dropped: exactly the shape of
            // damage a checksum exists to notice
            string path = Path.Combine(_dir, SaveSchema.FileName);
            var damaged = JsonUtility.FromJson<SaveFileDto>(File.ReadAllText(path));
            damaged.levels = new LevelRecordDto[0];
            File.WriteAllText(path, JsonUtility.ToJson(damaged, true));

            var loaded = store.Load();

            Assert.AreEqual("older", loaded.levels[0].levelId,
                            "silently loading half a save is worse than falling back");
        }

        [Test]
        public void FilesWrittenBeforeChecksumsExistedStillLoad()
        {
            var dto = FileWith("legacy", 3, 12);
            dto.checksum = string.Empty;
            File.WriteAllText(Path.Combine(_dir, SaveSchema.FileName), JsonUtility.ToJson(dto, true));

            var loaded = Store().Load();

            Assert.AreEqual("legacy", loaded.levels[0].levelId,
                            "adding integrity checks must not invalidate saves already on devices");
        }

        [Test]
        public void ChecksumIsStableAndIndependentOfItsOwnField()
        {
            var a = FileWith("x", 1, 1);
            var b = FileWith("x", 1, 1);

            b.checksum = "whatever was there before";

            Assert.AreEqual(SaveChecksum.Compute(a), SaveChecksum.Compute(b));
        }

        // ------------------------------------------------------------ records
        [Test]
        public void ARunKeepsTheBestOfEachMeasureIndependently()
        {
            var record = LevelRecord.Empty(LevelId.Parse("lvl"))
                                    .WithRun(stars: 3, moves: 40, nowUnix: 100);

            // fewer moves but a worse star rating: both bests must survive
            var after = record.WithRun(stars: 1, moves: 20, nowUnix: 200);

            Assert.AreEqual(3, after.Stars);
            Assert.AreEqual(20, after.BestMoves);
            Assert.AreEqual(2, after.Clears);
            Assert.AreEqual(100, after.FirstClearedUnix, "first clear is recorded once");
        }

        [Test]
        public void AWorseRunDoesNotCountAsAnImprovement()
        {
            var record = LevelRecord.Empty(LevelId.Parse("lvl")).WithRun(3, 20, 100);

            Assert.IsFalse(record.Improves(2, 30));
            Assert.IsTrue(record.Improves(3, 19));
        }
    }
}
