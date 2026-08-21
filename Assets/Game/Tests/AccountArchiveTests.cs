using System.IO;
using GlimmerGrove.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The groves this device is not playing right now.
    ///
    /// <para>
    /// Against a real temporary directory, for <c>SaveStoreTests</c>' reason: what this type
    /// does is decide where files go and which of them to throw away, and a fake filesystem
    /// would prove neither. The rules above it — which grove is on the device after a swap,
    /// and which account owns it — are in-memory facts and are proved offline in
    /// <c>AccountSwitchTests</c> against an archive that keeps nothing on disk at all.
    /// </para>
    /// </summary>
    public sealed class AccountArchiveTests
    {
        const string Mine = "uid-mine";
        const string Theirs = "uid-theirs";

        string _dir;

        // Made on demand rather than in a SetUp, so that the one case here needing no
        // filesystem — that the folder name a grove is filed under never moves — is not
        // dragged into the Editor by Application.temporaryCachePath, which is native.
        [TearDown]
        public void RemoveDir()
        {
            if (_dir != null && Directory.Exists(_dir)) Directory.Delete(_dir, true);
            _dir = null;
        }

        AccountArchiveStore Archive()
        {
            _dir ??= Path.Combine(Application.temporaryCachePath, "archivetests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
            return new AccountArchiveStore(_dir);
        }

        static SaveFileDto GroveOf(string userId, string glade) => new SaveFileDto
        {
            schemaVersion = SaveSchema.Version,
            settings = new SettingsDto(),
            wallet = WalletDto.Unwritten(),
            legacyImportDone = true,
            levels = new[] { new LevelRecordDto { levelId = glade, stars = 3, bestMoves = 34, clears = 1 } },
            cloud = new CloudStateDto { userId = userId },
        };

        [Test]
        public void RoundTripsAGrove()
        {
            var archive = Archive();

            Assert.IsFalse(archive.Has(Mine));
            Assert.IsTrue(archive.Stash(Mine, GroveOf(Mine, "c01_first_light")));
            Assert.IsTrue(archive.Has(Mine));

            var back = archive.Read(Mine);

            Assert.IsNotNull(back);
            Assert.AreEqual("c01_first_light", back.levels[0].levelId);
            Assert.AreEqual(Mine, back.cloud.userId);
        }

        /// <summary>
        /// Reading does not remove. The caller drops a slot only once the grove in it has been
        /// adopted, so a process death between the two costs a duplicate copy rather than a
        /// grove — which is the one failure this whole subsystem exists to make unreachable.
        /// </summary>
        [Test]
        public void ReadingDoesNotRemove()
        {
            var archive = Archive();
            archive.Stash(Mine, GroveOf(Mine, "c01_first_light"));

            Assert.IsNotNull(archive.Read(Mine));
            Assert.IsNotNull(archive.Read(Mine));
            Assert.IsTrue(archive.Has(Mine));

            archive.Forget(Mine);

            Assert.IsFalse(archive.Has(Mine));
            Assert.IsNull(archive.Read(Mine));
        }

        [Test]
        public void TwoAccountsDoNotShareASlot()
        {
            var archive = Archive();
            archive.Stash(Mine, GroveOf(Mine, "c01_first_light"));
            archive.Stash(Theirs, GroveOf(Theirs, "c01_lantern_ring"));

            Assert.AreEqual("c01_first_light", archive.Read(Mine).levels[0].levelId);
            Assert.AreEqual("c01_lantern_ring", archive.Read(Theirs).levels[0].levelId);
        }

        /// <summary>
        /// The folder is a hash of the account id, so this is what makes the hash safe: the
        /// identity travels inside the file, and a slot that does not name the account being
        /// asked for is not that player's grove however it came to be there. Adopting it would
        /// be the one mistake the archive must never make.
        /// </summary>
        [Test]
        public void ASlotThatNamesAnotherAccountIsIgnored()
        {
            var archive = Archive();

            // Filed under one account, holding another's file. Only reachable through a hash
            // collision or a tampered device, and the answer to both is the same.
            archive.Stash(Mine, GroveOf(Mine, "c01_first_light"));

            string folder = Directory.GetDirectories(_dir)[0];
            new SaveStore(folder).Save(GroveOf(Theirs, "c01_lantern_ring"));

            Assert.IsNull(archive.Read(Mine), "a slot must never hand back somebody else's grove");
        }

        /// <summary>
        /// Stashing the same account twice replaces rather than accumulates, which is what
        /// makes switching back and forth cost a bounded amount of disk however often it is
        /// done.
        /// </summary>
        [Test]
        public void StashingTwiceReplaces()
        {
            var archive = Archive();
            archive.Stash(Mine, GroveOf(Mine, "c01_first_light"));
            archive.Stash(Mine, GroveOf(Mine, "c01_lantern_ring"));

            Assert.AreEqual(1, Directory.GetDirectories(_dir).Length);
            Assert.AreEqual("c01_lantern_ring", archive.Read(Mine).levels[0].levelId);
        }

        /// <summary>
        /// It is a cache, and a bounded one. Evicting loses a copy and never a grove: the
        /// switch that filled a slot pushed it to the server first, so the only cost of being
        /// evicted is that coming back to that account downloads it again.
        /// </summary>
        [Test]
        public void OnlyTheNewestSlotsAreKept()
        {
            var archive = Archive();

            for (int i = 0; i < AccountArchiveStore.MaxArchived + 3; i++)
            {
                string uid = "uid-" + i;
                archive.Stash(uid, GroveOf(uid, "c01_first_light"));

                // The eviction orders by write time and a loop finishes inside one tick of the
                // filesystem's clock, so without this every folder is the same age and which
                // three go is arbitrary. A player's switches are minutes apart.
                //
                // Aged into the *past*, and that is not cosmetic: the eviction runs inside
                // Stash, before this line, so stamping a folder into the future would make the
                // next one created — which is the newest — look like the oldest thing there and
                // throw it away immediately. That is what this test caught on its first run in
                // the Editor.
                string folder = Path.Combine(_dir, Key(uid));
                if (Directory.Exists(folder))
                    Directory.SetLastWriteTimeUtc(folder, System.DateTime.UtcNow.AddMinutes(i - 1000));
            }

            Assert.AreEqual(AccountArchiveStore.MaxArchived, Directory.GetDirectories(_dir).Length);
            Assert.IsTrue(archive.Has("uid-" + (AccountArchiveStore.MaxArchived + 2)), "the newest is kept");
            Assert.IsFalse(archive.Has("uid-0"), "and the oldest is not");
        }

        /// <summary>
        /// The folder name is derived from the id and must never move. A hash that changed with
        /// the runtime would orphan every archive on the device the day the engine was
        /// upgraded — the copies would still be there and nothing would ever look for them.
        /// </summary>
        [Test]
        public void TheFolderNameIsStable()
        {
            Assert.AreEqual(Key(Mine), Key(Mine));
            Assert.AreNotEqual(Key(Mine), Key(Theirs));
            Assert.AreEqual(16, Key(Mine).Length);
        }

        static string Key(string userId) => AccountArchiveStore.Key(userId);
    }
}
