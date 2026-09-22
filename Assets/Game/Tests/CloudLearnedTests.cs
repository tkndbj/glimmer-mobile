using System.Collections.Generic;
using System.Threading.Tasks;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// A sync says what it brought the device, and says nothing when it brought nothing.
    ///
    /// <para>
    /// <see cref="CloudSaveService.Learned"/> is what lets a screen drawn from the save repaint
    /// when another device has moved it — and the second half is the half with teeth, for
    /// invariant 44m's reason: a sync runs on every foreground and the overwhelmingly common one
    /// brings back exactly what the device already holds. An event raised then would redraw
    /// every listening screen every time the app came back, which is the fault
    /// <c>SyncTriggersTests</c> holds the request side to.
    /// </para>
    /// <para>
    /// Runs offline against <c>AccountSwitchTests</c>' in-memory store and scripted backend, for
    /// the same reason that fixture does: what is asserted is what the device now holds and what
    /// it was told, neither of which needs a disk.
    /// </para>
    /// </summary>
    public sealed class CloudLearnedTests
    {
        const string Mine = "uid-mine";
        const string First = "c01_first_light";
        const string Second = "c01_twin_streams";

        AccountSwitchTests.Backend _backend;
        readonly List<SaveDelta> _learned = new List<SaveDelta>();

        [SetUp]
        public void Open()
        {
            SaveService.Unload();
            GlimmerGrove.Progression.EndlessCoins.UseStore(new GlimmerGrove.Progression.EndlessCoins.MemoryStore());
            SaveService.LoadWith(new AccountSwitchTests.MemoryStore(), new AccountSwitchTests.MemoryArchive());

            _backend = new AccountSwitchTests.Backend { Session = Mine };
            CloudSaveService.UseBackend(_backend);

            _learned.Clear();
            CloudSaveService.Learned += Note;
        }

        [TearDown]
        public void Close()
        {
            CloudSaveService.Learned -= Note;
            CloudSaveService.UseBackend(null);
            SaveService.Unload();
            GlimmerGrove.Progression.EndlessCoins.UseStore(null);
        }

        void Note(SaveDelta delta) => _learned.Add(delta);

        // ------------------------------------------------------------------ the cases
        /// <summary>The owner's case: level 65 on the Samsung, the iPhone drawing it unplayed.</summary>
        [Test]
        public void AGladeClearedOnAnotherPhoneIsAnnouncedByName()
        {
            OnDevice(First);
            _backend.Remote[Mine] = SaveWith(First, Second);

            var result = Wait(CloudSaveService.SyncAsync());

            Assert.IsTrue(result.Ok);
            Assert.AreEqual(1, _learned.Count, "one sync, one announcement");
            CollectionAssert.AreEqual(new[] { Second }, _learned[0].ChangedLevelIds);
            Assert.AreEqual(3, PlayerProgress.Stars(LevelId.Parse(Second)),
                            "and the announcement comes after the glade is on the device");
        }

        [Test]
        public void ASyncThatBringsBackWhatTheDeviceHoldsAnnouncesNothing()
        {
            OnDevice(First);
            _backend.Remote[Mine] = SaveWith(First);

            Wait(CloudSaveService.SyncAsync());
            Wait(CloudSaveService.SyncAsync());

            Assert.AreEqual(0, _learned.Count, "two foregrounds, nothing new, no repaint");
        }

        /// <summary>
        /// The other direction. This device is ahead of the server, so the sync pushes and the
        /// device learned nothing — the merge is what the device already had.
        /// </summary>
        [Test]
        public void ASyncThatOnlyPushesAnnouncesNothing()
        {
            OnDevice(First, Second);
            _backend.Remote[Mine] = SaveWith(First);

            Wait(CloudSaveService.SyncAsync());

            Assert.AreEqual(1, _backend.Pushes);
            Assert.AreEqual(0, _learned.Count);
        }

        [Test]
        public void AMissingDocumentAnnouncesNothing()
        {
            OnDevice(First);

            Wait(CloudSaveService.SyncAsync());

            Assert.AreEqual(1, _backend.Pushes, "the first sync writes the whole file");
            Assert.AreEqual(0, _learned.Count, "and there was nothing to learn from nothing");
        }

        /// <summary>
        /// Raised before the push on purpose: the glade is on the device whether or not the
        /// server hears back, and a player on a train should see it.
        /// </summary>
        [Test]
        public void WhatWasLearnedIsAnnouncedEvenWhenThePushThenFails()
        {
            OnDevice(First);
            _backend.Remote[Mine] = SaveWith(First, Second);
            _backend.PushFails = true;

            var result = Wait(CloudSaveService.SyncAsync());

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(1, _learned.Count);
            Assert.AreEqual(3, PlayerProgress.Stars(LevelId.Parse(Second)));
        }

        /// <summary>
        /// A record that moved without a new glade — a better run of one already cleared — is
        /// still news, and it is the same id that names it.
        /// </summary>
        [Test]
        public void ABetterRunOfAClearedGladeIsNewsToo()
        {
            OnDevice(First);

            var better = SaveWith(First);
            better.levels[0].bestMoves = 7;
            _backend.Remote[Mine] = better;

            Wait(CloudSaveService.SyncAsync());

            Assert.AreEqual(1, _learned.Count);
            CollectionAssert.AreEqual(new[] { First }, _learned[0].ChangedLevelIds);
            Assert.AreEqual(7, PlayerProgress.BestMoves(LevelId.Parse(First)));
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>
        /// Puts this device on the account with the named glades three-starred, through the
        /// one door a save arrives by, and with a sync history so the gate sees a settled file.
        /// </summary>
        void OnDevice(params string[] glades)
        {
            SaveService.Adopt(SaveWith(glades));
            CloudState.MarkSynced(1_700_000_000);
            SaveService.Flush();
        }

        static SaveFileDto SaveWith(params string[] glades)
        {
            var levels = new LevelRecordDto[glades.Length];
            for (int i = 0; i < glades.Length; i++)
                levels[i] = new LevelRecordDto { levelId = glades[i], stars = 3, bestMoves = 11, clears = 1 };

            return new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                legacyImportDone = true,
                levels = levels,
                cloud = new CloudStateDto { userId = Mine, revision = 4 },
            };
        }

        static T Wait<T>(Task<T> task)
        {
            Assert.IsTrue(task.Wait(10000), "the sync did not finish");
            return task.Result;
        }
    }
}
