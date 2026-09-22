using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// A player's own act asks for a sync; a save being read never does.
    ///
    /// <para>
    /// The second half is the one with teeth. Every ledger raises <c>Changed</c> when a save
    /// is loaded, and a sync adopts a merge by loading one — so a sync asked for on
    /// <c>Changed</c> is a sync every three seconds for the life of the process, invisible
    /// on any screen and paid for in battery and document writes by every player. The
    /// triggers therefore hang on <see cref="Wallet.ProfileChanged"/>,
    /// <see cref="CompanionLedger.Bought"/> and <see cref="EndlessLedger.Beaten"/>, which only
    /// the player raises.
    /// </para>
    /// <para>
    /// <b>This fixture used to be about the grove</b> — a placement, a purchase and a region
    /// were the three acts it proved, and all three went with the Grovement on 2026-09-21. The
    /// rule did not, so it is asked here of the triggers that are left rather than deleted with
    /// the ones that are not: the trap is a property of <c>Changed</c>, not of the grove.
    /// </para>
    /// </summary>
    public sealed class SyncTriggersTests
    {
        [SetUp]
        public void Reset()
        {
            CompanionLedger.ResetForTests();
            PlayerProgress.LoadFrom(new SaveFileDto());
            CloudSaveService.ForgetSyncRequestForTests();
            SyncTriggers.Attach();
        }

        [TearDown]
        public void Restore()
        {
            CompanionLedger.ResetForTests();
            PlayerProgress.LoadFrom(new SaveFileDto());
            CloudSaveService.ForgetSyncRequestForTests();
        }

        /// <summary>
        /// The one that was missing on 2026-09-22. A glade cleared on one phone rode the
        /// background sync, which on Android is a push started as the process is frozen, and
        /// the other phone drew the glade as unplayed. A run is the player's act and it asks
        /// for a sync while the phone is still awake.
        /// </summary>
        [Test]
        public void AFinishedRunAsksForASync()
        {
            Assert.IsFalse(CloudSaveService.IsSyncPending);

            PlayerProgress.RecordRun(LevelId.Parse("c01_first_light"), 1, 12);

            Assert.IsTrue(CloudSaveService.IsSyncPending);
        }

        /// <summary>
        /// A lost run is recorded too — the attempt count climbs — and it goes up for the
        /// same reason: the next device should agree about how many times this glade was
        /// tried, and a defeat is the last thing a player does before putting the phone down.
        /// </summary>
        [Test]
        public void ALostRunAsksForASyncToo()
        {
            PlayerProgress.RecordRun(LevelId.Parse("c01_first_light"), 0, 40);

            Assert.IsTrue(CloudSaveService.IsSyncPending);
        }

        [Test]
        public void ANewEndlessBestAsksForASync()
        {
            Assert.IsFalse(CloudSaveService.IsSyncPending);

            EndlessLedger.Record(LevelId.Parse("s02_endlesswatch"), 12);

            Assert.IsTrue(CloudSaveService.IsSyncPending);
        }

        [Test]
        public void ARunThatBeatNothingAsksForNothing()
        {
            EndlessLedger.Record(LevelId.Parse("s02_endlesswatch"), 12);
            CloudSaveService.ForgetSyncRequestForTests();

            // Under the best already held, so nothing a stranger can see has moved.
            EndlessLedger.Record(LevelId.Parse("s02_endlesswatch"), 5);

            Assert.IsFalse(CloudSaveService.IsSyncPending);
        }

        [Test]
        public void ARenameAsksForASync()
        {
            Assert.IsFalse(CloudSaveService.IsSyncPending);

            Wallet.SetDisplayName("Fern", 1_000L);

            Assert.IsTrue(CloudSaveService.IsSyncPending);
        }

        [Test]
        public void ASaveBeingReadNeverAsksForASync()
        {
            var save = new SaveFileDto
            {
                companionsOwned = new[] { "monarch" },
                endlessBest = new[]
                {
                    new EndlessBestDto { level = "s02_endlesswatch", wave = 40, waves = 200 },
                },
                levels = new[]
                {
                    new LevelRecordDto { levelId = "c01_first_light", stars = 3, bestMoves = 11, clears = 1 },
                },
            };

            // The doors a merge comes through. Each raises Changed, and none may raise a
            // request — or every sync would schedule the next. The star ledger is the one
            // with teeth now that a run asks: it raises `Reloaded` here, never `RecordChanged`.
            CompanionLedger.LoadFrom(save);
            EndlessLedger.LoadFrom(save);
            PlayerProgress.LoadFrom(save);

            Assert.IsFalse(CloudSaveService.IsSyncPending);
        }
    }
}
