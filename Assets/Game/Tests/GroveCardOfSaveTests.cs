using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The card a save file describes, which is what decides whether a publish is owed.
    ///
    /// <para>
    /// Two things are under contract. The fingerprint must follow exactly what a visitor can
    /// see and nothing else — a star or a heart moving must not cost a publish, and a piece
    /// moving must — and a save and the ledgers it loads into must describe <em>the same</em>
    /// card, because the request is judged from the file and the player's own screen is drawn
    /// from the ledgers, and a disagreement between them is a card that publishes on every
    /// sync or never.
    /// </para>
    /// </summary>
    public sealed class GroveCardOfSaveTests
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
        }

        [TearDown]
        public void Restore()
        {
            HomesteadProgress.Set(null);
            HomesteadLayout.ResetForTests();
            HomesteadLedger.ResetForTests();
            GroveLand.ResetForTests();
        }

        // ------------------------------------------------------------- fixtures
        static HomesteadPiece Decor(string id, int cost)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Decor,
                                  cost, LevelId.None, ChapterId.None, 1f, .5f);

        static HomesteadPiece Home(string id, int cost, int tier)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Dwelling,
                                  cost, LevelId.None, ChapterId.None, 1f, .5f, tier: tier);

        /// <summary>An 8x6 floor: a free 6x6 starter region and a priced strip to its right.</summary>
        static HomesteadCatalog Grove()
            => new HomesteadCatalog(
                new GroveFloor(8, 6, string.Empty, GroveFloor.TileId(0, 0), GroveFloor.TileId(1, 0),
                               new[]
                               {
                                   new GroveRegion("starter", 0, 0, 6, 6, 0),
                                   new GroveRegion("east", 6, 0, 2, 6, 500),
                               }),
                new[] { Decor("fence", 100), Decor("oak", 0), Home("hut", 0, 1), Home("manor", 1000, 2) });

        static string T(int col, int row) => GroveFloor.TileId(col, row);

        static SaveFileDto Save()
            => new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                wallet = new WalletDto { displayName = "Fern", avatarId = "monarch" },
                homesteadStock = new[] { new HomesteadStockDto { id = "fence", copies = 2 } },
                groveLandOwned = new[] { "east" },
                homesteadPlaced = new[]
                {
                    new HomesteadPlacementDto { slot = T(2, 2), piece = "fence", setUnix = 10L },
                    new HomesteadPlacementDto { slot = T(3, 3), piece = "oak", setUnix = 11L, flipped = true },
                },
                levels = new LevelRecordDto[0],
            };

        static string Print(SaveFileDto save)
            => GroveCard.OfSave(Grove(), save, "uid", 3, 1_000L).Fingerprint();

        // ================================================================= tests
        [Test]
        public void TheFingerprintFollowsWhatAVisitorCanSee()
        {
            string baseline = Print(Save());

            var moved = Save();
            moved.homesteadPlaced[0].slot = T(2, 3);
            Assert.AreNotEqual(baseline, Print(moved), "a piece moved");

            var flipped = Save();
            flipped.homesteadPlaced[0].flipped = true;
            Assert.AreNotEqual(baseline, Print(flipped), "a piece turned round");

            var cleared = Save();
            cleared.homesteadPlaced = new[] { cleared.homesteadPlaced[0] };
            Assert.AreNotEqual(baseline, Print(cleared), "a piece taken down");

            var poorer = Save();
            poorer.groveLandOwned = new string[0];
            Assert.AreNotEqual(baseline, Print(poorer), "land sold back");

            var richer = Save();
            richer.homesteadStock = new[] { new HomesteadStockDto { id = "fence", copies = 3 } };
            Assert.AreNotEqual(baseline, Print(richer), "another copy bought");

            var housed = Save();
            housed.homesteadStock = new[]
            {
                new HomesteadStockDto { id = "fence", copies = 2 },
                new HomesteadStockDto { id = "manor", copies = 1 },
            };
            Assert.AreNotEqual(baseline, Print(housed), "a better home held");

            var renamed = Save();
            renamed.wallet.displayName = "Moss";
            Assert.AreNotEqual(baseline, Print(renamed), "a new name");

            var reworn = Save();
            reworn.wallet.avatarId = "coral";
            Assert.AreNotEqual(baseline, Print(reworn), "a different companion worn");
        }

        [Test]
        public void TheFingerprintIgnoresWhatAVisitorCannotSee()
        {
            string baseline = Print(Save());

            var played = Save();
            played.levels = new[] { new LevelRecordDto { levelId = "c01_first_light", stars = 3 } };
            played.wallet.heartsProduced = 40L;
            played.wallet.heartsSpent = 12L;
            played.updatedUnix = 9_999_999L;
            played.cloud = new CloudStateDto { revision = 77L, userId = "uid" };
            played.homesteadPlaced[0].setUnix = 500L;     // when it was placed, not where

            Assert.AreEqual(baseline, Print(played));

            // Nor the keeper level: it is drawn on the card, and it moves with every star,
            // so fingerprinting it would be a publish per session for a number the ranking
            // job does not read. It reaches the board with the next real change.
            Assert.AreEqual(baseline, GroveCard.OfSave(Grove(), Save(), "uid", 9, 1_000L).Fingerprint());
        }

        [Test]
        public void ASaveAndTheLedgersItLoadsIntoDescribeTheSameCard()
        {
            var catalog = Grove();
            var save = Save();

            HomesteadLayout.LoadFrom(save);
            HomesteadLedger.LoadFrom(save);
            GroveLand.LoadFrom(save);

            var live = GroveCard.OfPlayer(catalog, "uid", "Fern", "monarch", 3, 1_000L);
            var file = GroveCard.OfSave(catalog, save, "uid", 3, 1_000L);

            Assert.AreEqual(live.Fingerprint(), file.Fingerprint());
            Assert.AreEqual(live.Score, file.Score);
            Assert.AreEqual(live.DwellingId, file.DwellingId);
            Assert.Greater(file.Score, 0L, "the fixture must be worth publishing");
            Assert.AreEqual("hut", file.DwellingId, "the free rung is held by everybody");
        }

        [Test]
        public void AnUnnamedKeeperReadsTheSameOnBothSides()
        {
            var catalog = Grove();
            var save = Save();
            save.wallet.displayName = string.Empty;

            HomesteadLayout.LoadFrom(save);
            HomesteadLedger.LoadFrom(save);
            GroveLand.LoadFrom(save);

            // The wallet shows the default and never stores it (invariant 11c); the file
            // reading has to show the same thing, or an unnamed keeper publishes twice.
            var live = GroveCard.OfPlayer(catalog, "uid", Wallet.DefaultName, "monarch", 3, 1_000L);
            var file = GroveCard.OfSave(catalog, save, "uid", 3, 1_000L);

            Assert.AreEqual(live.Fingerprint(), file.Fingerprint());
        }

        [Test]
        public void AnEmptiedSlotShowsNothingAndTheLaterRowWins()
        {
            var save = Save();
            save.homesteadPlaced = new[]
            {
                new HomesteadPlacementDto { slot = T(2, 2), piece = "fence" },
                new HomesteadPlacementDto { slot = T(2, 2), piece = "oak" },        // later row wins
                new HomesteadPlacementDto { slot = T(3, 3), piece = "fence" },
                new HomesteadPlacementDto { slot = T(3, 3), piece = string.Empty }, // emptied on purpose
            };

            var card = GroveCard.OfSave(Grove(), save, "uid", 3, 1_000L);

            Assert.AreEqual(1, card.OccupiedCount);
            Assert.AreEqual("oak", card.Placements[T(2, 2)].PieceId);
        }

        [Test]
        public void AnEmptySaveIsWorthNothingAndBreaksNothing()
        {
            var card = GroveCard.OfSave(Grove(), new SaveFileDto(), "uid", 1, 1_000L);

            Assert.AreEqual(0L, card.Score);
            Assert.AreEqual(0, card.OccupiedCount);
            Assert.AreEqual("hut", card.DwellingId);

            Assert.DoesNotThrow(() => GroveCard.OfSave(Grove(), null, "uid", 1, 1_000L));
            Assert.DoesNotThrow(() => GroveCard.OfSave(null, Save(), "uid", 1, 1_000L));
        }
    }
}
