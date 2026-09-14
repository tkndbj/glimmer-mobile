using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Nothing stands on the hall's plot, and what was standing there goes back to the
    /// inventory rather than being drawn through the house.
    ///
    /// <para>
    /// The fault this pins was invisible to every gate: a row standing on the hall parses,
    /// validates, publishes and scores like any other. It arrived twice — when the plot grew
    /// from two tiles to four (invariant 16p) over pieces placed beside the smaller hall, and
    /// as the shape a two-device merge can always produce — and was reported as a visited
    /// grove whose town hall was buried under a tavern. See <see cref="GroveOccupancy"/> and
    /// <see cref="HomesteadLayout.Settle"/>.
    /// </para>
    /// </summary>
    public sealed class GroveHallPlotTests
    {
        sealed class FakeProgress : IHomesteadProgress
        {
            public bool IsCleared(LevelId level) => false;
            public bool IsChapterFinished(ChapterId chapter) => false;
        }

        [SetUp]
        public void Reset()
        {
            HomesteadProgress.Set(new FakeProgress());
            HomesteadLayout.ResetForTests();
            HomesteadLedger.ResetForTests();
            HomesteadLedger.GrantForTests("fence", 1);
            HomesteadLedger.GrantForTests("tavern", 1);
        }

        [TearDown]
        public void Restore()
        {
            HomesteadLayout.ResetForTests();
            HomesteadLedger.ResetForTests();
            HomesteadProgress.Set(null);
        }

        // ------------------------------------------------------------- fixtures
        static HomesteadPiece Cottage(int plot)
            => new HomesteadPiece("home_cottage", "Homestead/home_cottage", false,
                                  HomesteadPieceKind.Dwelling, 0, LevelId.None, ChapterId.None,
                                  1f, .4f, tier: 1,
                                  footprint: new GroveFootprint(plot, plot),
                                  facings: GroveFootprint.Facings);

        static HomesteadPiece Fence()
            => new HomesteadPiece("fence", "Homestead/fence", false, HomesteadPieceKind.Decor,
                                  100, LevelId.None, ChapterId.None, 1f, .4f, bundle: 99);

        static HomesteadPiece Tavern()
            => new HomesteadPiece("tavern", "Homestead/tavern", false, HomesteadPieceKind.Decor,
                                  100, LevelId.None, ChapterId.None, 1f, .4f, bundle: 99,
                                  footprint: new GroveFootprint(3, 3),
                                  facings: GroveFootprint.Facings);

        /// <summary>A 12x12 floor whose hall is anchored at (4,4) and takes <paramref name="plot"/> tiles a side.</summary>
        static HomesteadCatalog Grove(int plot)
            => new HomesteadCatalog(
                new GroveFloor(12, 12, string.Empty, GroveFloor.TileId(4, 4), GroveFloor.TileId(10, 10),
                               new[] { new GroveRegion("all", 0, 0, 12, 12, 0) },
                               new GroveFootprint(plot, plot)),
                new[] { Cottage(plot), Fence(), Tavern() });

        static string T(int col, int row) => GroveFloor.TileId(col, row);

        // ================================================================ the index
        [Test]
        public void TwoOrdinaryStandsMayOverlapButNothingOverlapsTheHall()
        {
            var floor = Grove(4).Floor;

            var hall = floor.HallStand("home_cottage", 4, 4, 0);
            var inside = new GroveStand(6, 6, "fence", 0, GroveFootprint.Single);
            var edge = new GroveStand(7, 7, "tavern", 0, new GroveFootprint(3, 3));      // touches (7,7)
            var clear = new GroveStand(8, 8, "tavern", 0, new GroveFootprint(3, 3));     // starts past it
            var also = new GroveStand(9, 9, "fence", 0, GroveFootprint.Single);          // inside `clear`

            var index = new GroveOccupancy(new[] { hall, inside, edge, clear, also });

            // The hall stands, whole, on every one of its tiles.
            Assert.IsTrue(index.TryAnchored(4, 4, out var stood) && stood.IsHall);
            Assert.IsTrue(index.TryStandAt(7, 7, out stood) && stood.IsHall, "the plot's far corner is the hall's");

            // The two that touch the plot are not in it at all — neither covering nor anchored.
            Assert.IsFalse(index.TryAnchored(6, 6, out _));
            Assert.IsFalse(index.TryAnchored(7, 7, out _));
            Assert.IsFalse(index.IsCovered(8, 7), "the tavern's own tiles off the plot are clear too");

            // The ordinary overlap policy is untouched: two pieces may share ground (invariant 11).
            Assert.IsTrue(index.TryAnchored(8, 8, out stood) && stood.PieceId == "tavern");
            Assert.IsTrue(index.TryAnchored(9, 9, out stood) && stood.PieceId == "fence");

            Assert.AreEqual(2, index.Displaced.Count);
            CollectionAssert.AreEquivalent(new[] { T(6, 6), T(7, 7) },
                                           new[] { index.Displaced[0].AnchorId, index.Displaced[1].AnchorId });
        }

        [Test]
        public void OverlapIsSymmetricAndTouchingCornersCount()
        {
            var a = new GroveStand(4, 4, "home_cottage", 0, new GroveFootprint(4, 4), true);

            Assert.IsTrue(GroveOccupancy.Overlaps(a, new GroveStand(7, 7, "x", 0, new GroveFootprint(3, 3))));
            Assert.IsTrue(GroveOccupancy.Overlaps(new GroveStand(7, 7, "x", 0, new GroveFootprint(3, 3)), a));
            Assert.IsTrue(GroveOccupancy.Overlaps(a, new GroveStand(2, 2, "x", 0, new GroveFootprint(3, 3))), "reaching in from behind");
            Assert.IsFalse(GroveOccupancy.Overlaps(a, new GroveStand(8, 4, "x", 0, GroveFootprint.Single)), "one past the edge");
            Assert.IsFalse(GroveOccupancy.Overlaps(a, new GroveStand(4, 8, "x", 0, GroveFootprint.Single)));
            Assert.IsFalse(GroveOccupancy.Overlaps(a, new GroveStand(0, 0, "x", 0, new GroveFootprint(4, 4))));
        }

        // ============================================================== the plot grew
        [Test]
        public void APieceThePlotGrewOverIsHandedBackAndTheHallIsDrawnWhole()
        {
            // Placed beside a two-tile hall, exactly as every grove was before 16p.
            var small = Grove(2);
            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.TryPlace(small, 6, 6, "fence", out _, out _));
            Assert.AreEqual(0, HomesteadLedger.Available(Fence()), "the one copy is standing");

            // The same rows read against the four-tile plot: the fence is inside the house.
            var big = Grove(4);
            var index = HomesteadLayout.Occupancy(big);
            Assert.IsTrue(index.TryStandAt(6, 6, out var stood) && stood.IsHall, "the hall owns the tile");
            Assert.IsFalse(index.TryAnchored(6, 6, out _), "and the fence is not drawn through it");

            // Settling writes the row empty — a real instruction with a stamp, so it merges —
            // and the copy is back in the inventory.
            Assert.IsTrue(HomesteadLayout.Settle(big));
            Assert.IsFalse(HomesteadLayout.IsOccupied(T(6, 6)));
            Assert.AreEqual(1, HomesteadLedger.Available(Fence()));

            var dto = new SaveFileDto();
            HomesteadLayout.WriteInto(dto);
            var row = System.Array.Find(dto.homesteadPlaced, r => r.slot == T(6, 6));
            Assert.IsNotNull(row, "the emptied slot keeps a row (invariant 16)");
            Assert.IsEmpty(row.piece);
            Assert.Greater(row.setUnix, 0L);

            // Nothing left to settle, and it says so without writing.
            Assert.IsFalse(HomesteadLayout.Settle(big));
            Assert.AreEqual(0, HomesteadLayout.Occupancy(big).Displaced.Count);
        }

        [Test]
        public void SettlingAGroveWithNothingOnThePlotWritesNothing()
        {
            var grove = Grove(4);
            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.TryPlace(grove, 9, 9, "fence", out _, out _));

            Assert.IsFalse(HomesteadLayout.Settle(grove));
            Assert.IsTrue(HomesteadLayout.IsOccupied(T(9, 9)));
            Assert.IsFalse(HomesteadLayout.Settle(HomesteadCatalog.Empty), "no catalog, no opinion");
        }

        // ================================================================ a merge
        [Test]
        public void AHallAMergeStoodOnSomethingDisplacesItOnLoad()
        {
            // What a join can produce and no device can: the hall moved to (7,8) on one
            // device, a tavern built at (8,9) on the other, both the later fact about their
            // own slot. Loaded while a catalog is current, the rows are settled as they land.
            HomesteadCatalog.Publish(Grove(4));
            try
            {
                var dto = new SaveFileDto
                {
                    groveHall = T(7, 8),
                    groveHallSetUnix = 50L,
                    homesteadPlaced = new[]
                    {
                        new HomesteadPlacementDto { slot = T(8, 9), piece = "tavern", setUnix = 60L },
                        new HomesteadPlacementDto { slot = T(1, 1), piece = "fence", setUnix = 60L },
                    },
                };

                HomesteadLayout.LoadFrom(dto);

                var catalog = HomesteadCatalog.Current;
                Assert.IsTrue(HomesteadLayout.IsHall(catalog.Floor, 8, 9), "the hall kept its seat");
                Assert.IsFalse(HomesteadLayout.IsOccupied(T(8, 9)), "the tavern was handed back");
                Assert.IsTrue(HomesteadLayout.IsOccupied(T(1, 1)), "and the fence, clear of it, was not");
                Assert.AreEqual(1, HomesteadLedger.Available(Tavern()));

                var written = new SaveFileDto();
                HomesteadLayout.WriteInto(written);
                var row = System.Array.Find(written.homesteadPlaced, r => r.slot == T(8, 9));
                Assert.IsNotNull(row);
                Assert.IsEmpty(row.piece);
                Assert.Greater(row.setUnix, 60L, "the emptying outranks the placement it undid");
            }
            finally
            {
                HomesteadCatalog.Publish(null);
            }
        }

        // ================================================================= a card
        [Test]
        public void AVisitedCardDrawsTheHallAndNotWhatWasLeftStandingInIt()
        {
            // A card published before its owner's client settled: the row is still on it.
            var catalog = Grove(4);
            var placed = new System.Collections.Generic.Dictionary<string, Placement>
            {
                [T(6, 6)] = new Placement("fence", 0L, 0),
                [T(7, 7)] = new Placement("tavern", 0L, 1),
                [T(9, 9)] = new Placement("fence", 0L, 0),
            };

            var card = new GroveCard("owner", "Fern", "monarch", 3, 0L, 0, null, 1L,
                                     "home_cottage", null, placed);
            var index = card.Occupancy(catalog);

            Assert.IsTrue(index.TryAnchored(4, 4, out var stood) && stood.IsHall);
            Assert.IsTrue(index.TryStandAt(6, 6, out stood) && stood.IsHall);
            Assert.IsTrue(index.TryStandAt(7, 7, out stood) && stood.IsHall);
            Assert.IsFalse(index.TryAnchored(6, 6, out _));
            Assert.IsFalse(index.TryAnchored(7, 7, out _));
            Assert.IsTrue(index.TryAnchored(9, 9, out stood) && stood.PieceId == "fence", "clear of the plot, still shown");
            Assert.AreEqual(2, index.Displaced.Count);
        }

        [Test]
        public void ACardOpensOnTheSeatItsOwnerMovedTheHallTo()
        {
            var floor = Grove(4).Floor;

            var moved = new GroveCard("owner", "Fern", "monarch", 3, 0L, 0, null, 1L,
                                      "home_cottage", null, null, T(7, 8), 1);
            Assert.IsTrue(moved.HallSeat(floor, out int col, out int row));
            Assert.AreEqual(7, col);
            Assert.AreEqual(8, row);

            var never = new GroveCard("owner", "Fern", "monarch", 3, 0L, 0, null, 1L,
                                      "home_cottage", null, null);
            Assert.IsTrue(never.HallSeat(floor, out col, out row));
            Assert.AreEqual(4, col, "absent means where the floor says");
            Assert.AreEqual(4, row);

            // A seat the plot cannot fit on this floor is a lie a stale card can tell, and it
            // falls back rather than opening the visitor on ground off the edge of the world.
            var off = new GroveCard("owner", "Fern", "monarch", 3, 0L, 0, null, 1L,
                                    "home_cottage", null, null, T(10, 10), 0);
            Assert.IsTrue(off.HallSeat(floor, out col, out row));
            Assert.AreEqual(4, col);
            Assert.AreEqual(4, row);
        }
    }
}
