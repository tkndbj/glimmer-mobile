using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
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
    /// triggers therefore hang on <see cref="HomesteadLayout.Edited"/> and the
    /// <c>Bought</c> events, which only the player raises.
    /// </para>
    /// </summary>
    public sealed class SyncTriggersTests
    {
        sealed class NoProgress : IHomesteadProgress
        {
            public bool IsCleared(LevelId level) => false;
            public bool IsChapterFinished(ChapterId chapter) => false;
        }

        [SetUp]
        public void Reset()
        {
            HomesteadProgress.Set(new NoProgress());
            HomesteadLayout.ResetForTests();
            HomesteadLedger.ResetForTests();
            GroveLand.ResetForTests();
            CloudSaveService.ForgetSyncRequestForTests();
            SyncTriggers.Attach();
        }

        [TearDown]
        public void Restore()
        {
            HomesteadProgress.Set(null);
            HomesteadLayout.ResetForTests();
            HomesteadLedger.ResetForTests();
            GroveLand.ResetForTests();
            CloudSaveService.ForgetSyncRequestForTests();
        }

        static string T(int col, int row) => GroveFloor.TileId(col, row);

        [Test]
        public void APlacementAsksForASync()
        {
            Assert.IsFalse(CloudSaveService.IsSyncPending);

            HomesteadLayout.Place(T(2, 2), "fence");

            Assert.IsTrue(CloudSaveService.IsSyncPending);
        }

        [Test]
        public void ClearingAPlacementAsksForASync()
        {
            HomesteadLayout.Place(T(2, 2), "fence");
            CloudSaveService.ForgetSyncRequestForTests();

            HomesteadLayout.Clear(T(2, 2));

            Assert.IsTrue(CloudSaveService.IsSyncPending);
        }

        [Test]
        public void APlacementThatChangesNothingAsksForNothing()
        {
            HomesteadLayout.Place(T(2, 2), "fence");
            CloudSaveService.ForgetSyncRequestForTests();

            HomesteadLayout.Place(T(2, 2), "fence");

            Assert.IsFalse(CloudSaveService.IsSyncPending);
        }

        [Test]
        public void ASaveBeingReadNeverAsksForASync()
        {
            var save = new SaveFileDto
            {
                homesteadPlaced = new[] { new HomesteadPlacementDto { slot = T(2, 2), piece = "fence" } },
                homesteadStock = new[] { new HomesteadStockDto { id = "fence", copies = 1 } },
                groveLandOwned = new[] { "east" },
            };

            // The three doors a merge comes through. Each raises Changed, and none may raise
            // a request — or every sync would schedule the next.
            HomesteadLayout.LoadFrom(save);
            HomesteadLedger.LoadFrom(save);
            GroveLand.LoadFrom(save);

            Assert.IsFalse(CloudSaveService.IsSyncPending);
        }
    }
}
