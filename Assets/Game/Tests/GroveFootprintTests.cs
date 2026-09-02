using System;
using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// What a piece occupies, as distinct from what it paints — the rules a two-wide house
    /// brought to a floor where every tile used to be one slot.
    ///
    /// <para>
    /// Three reports from play, one cause: "big objects visually cover more than one tile",
    /// "some objects get buried in the tiles", and "it is hard to tap on some tiles once
    /// things are placed". Each is a piece whose picture and whose ground disagreed. The
    /// footprint is the fact that was missing, the occupancy index is what every rule reads
    /// it through, and the hit mask is what a tap reads instead of a box.
    /// </para>
    /// </summary>
    public sealed class GroveFootprintTests
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
        }

        [TearDown]
        public void Restore()
        {
            HomesteadProgress.Set(null);
            HomesteadLayout.ResetForTests();
        }

        // ------------------------------------------------------------- fixtures
        static HomesteadPiece Piece(string id, int cols = 1, int rows = 1)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Decor,
                                  0, LevelId.None, ChapterId.None, 1f, .5f,
                                  footprint: new GroveFootprint(cols, rows));

        static HomesteadPiece Home(string id)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Dwelling,
                                  0, LevelId.None, ChapterId.None, 1f, .45f,
                                  HomesteadSlotKind.Hearth, 1, footprint: new GroveFootprint(2, 2));

        /// <summary>
        /// An 8x8 floor owned outright: a 2x2 hall anchored on t_001_001 and the starter friend
        /// on t_004_001.
        /// </summary>
        static HomesteadCatalog Grove()
            => new HomesteadCatalog(
                new GroveFloor(8, 8, string.Empty, T(1, 1), T(4, 1),
                               new[] { new GroveRegion("all", 0, 0, 8, 8, 0) },
                               new GroveFootprint(2, 2)),
                new[] { Home("cottage"), Piece("fence"), Piece("oak"), Piece("house", 2, 2), Piece("ramp", 1, 2) });

        static string T(int col, int row) => GroveFloor.TileId(col, row);

        // ============================================================ footprints
        [Test]
        public void AMirroredFootprintSwapsItsColumnsForItsRows()
        {
            // Screen x is (col - row) * w/2, so reflecting the drawing is exactly exchanging
            // the two axes: a ramp two tiles long along one diagonal runs along the other.
            var ramp = new GroveFootprint(1, 2);

            Assert.AreEqual(new GroveFootprint(2, 1), ramp.Mirrored);
            Assert.AreEqual(ramp, ramp.Mirrored.Mirrored);
            Assert.AreEqual(ramp, ramp.Facing(false));
            Assert.AreEqual(ramp.Mirrored, ramp.Facing(true));
            Assert.AreEqual(GroveFootprint.Single, GroveFootprint.Single.Mirrored, "a square is its own mirror");
        }

        [Test]
        public void AFootprintIsSortedByItsFrontTileAndDrawnFromItsCentre()
        {
            var house = new GroveFootprint(2, 2);

            Assert.AreEqual(4, house.FrontCol(3));
            Assert.AreEqual(6, house.FrontRow(5));
            Assert.AreEqual(3.5f, house.CentreCol(3), 1e-5f);
            Assert.AreEqual(5.5f, house.CentreRow(5), 1e-5f);

            // Deeper than anything on its anchor, as deep as the front tile, and behind a
            // single tile standing on that same front tile — a merge can produce one.
            Assert.Greater(house.Depth(3, 5), GroveFootprint.Single.Depth(3, 5));
            Assert.Less(house.Depth(3, 5), GroveFootprint.Single.Depth(4, 6));
            Assert.Greater(house.Depth(3, 5), GroveFootprint.Single.Depth(4, 5));
        }

        [Test]
        public void AFootprintIsClampedToTheLargestAllowed()
        {
            Assert.AreEqual(GroveFootprint.Single, new GroveFootprint(0, -3));
            Assert.AreEqual(new GroveFootprint(GroveFootprint.MaxSide, 1), new GroveFootprint(99, 1));
            Assert.AreEqual(GroveFootprint.Single, default(HomesteadPiece).Footprint,
                            "a piece with nothing authored stands on one tile");
        }

        // ============================================================= occupancy
        [Test]
        public void ATwoByTwoCoversFourTilesAndAnswersFromAnyOfThem()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(4, 4), "house");

            var index = HomesteadLayout.Occupancy(grove);

            for (int c = 4; c <= 5; c++)
                for (int r = 4; r <= 5; r++)
                {
                    Assert.IsTrue(index.TryStandAt(c, r, out var stand), $"{c},{r}");
                    Assert.AreEqual("house", stand.PieceId);
                    Assert.AreEqual(4, stand.AnchorCol);
                    Assert.AreEqual(4, stand.AnchorRow);
                }

            Assert.IsTrue(index.TryAnchored(4, 4, out _));
            Assert.IsFalse(index.TryAnchored(5, 5, out _), "covered, not anchored");
            Assert.IsFalse(index.IsCovered(6, 4));
            Assert.IsFalse(index.IsCovered(4, 6));
        }

        [Test]
        public void TheHallCoversItsFootprintAndIsNeverPlaceable()
        {
            var grove = Grove();
            var floor = grove.Floor;

            Assert.IsTrue(floor.IsHall(1, 1));
            Assert.IsTrue(floor.IsHall(2, 2), "the front tile of a 2x2 hall");
            Assert.IsFalse(floor.IsHall(3, 3));
            Assert.IsTrue(floor.IsHallAnchor(1, 1));
            Assert.IsFalse(floor.IsHallAnchor(2, 2));

            var index = HomesteadLayout.Occupancy(grove);
            Assert.IsTrue(index.TryStandAt(2, 1, out var hall));
            Assert.IsTrue(hall.IsHall);
            Assert.AreEqual("cottage", hall.PieceId);

            Assert.AreEqual(GrovePlaceResult.Refused, HomesteadLayout.TryPlace(grove, 2, 2, "fence", out _, out _));
            Assert.AreEqual(GrovePlaceResult.Refused, HomesteadLayout.Move(grove, T(2, 2), T(5, 5)));
        }

        [Test]
        public void TheStarterFriendOccupiesItsTileWhileItIsUntouched()
        {
            var grove = Grove();
            var index = HomesteadLayout.Occupancy(grove);

            Assert.IsTrue(index.TryAnchored(4, 1, out var friend));
            Assert.AreEqual(grove.Floor.StarterPiece, friend.PieceId);

            // So a two-wide piece touching the tile beside the friend, boxed in by the hall on
            // the other side, has nowhere to go — the friend is ground already taken.
            Assert.AreEqual(GrovePlaceResult.NoRoom,
                            HomesteadLayout.TryPlace(grove, 4, 0, "house", out _, out _));

            // While touching the friend's own tile replaces the friend, as any stand is replaced.
            Assert.AreEqual(GrovePlaceResult.Placed,
                            HomesteadLayout.TryPlace(grove, 4, 1, "oak", out int col, out int row));
            Assert.AreEqual((4, 1), (col, row));
            Assert.AreEqual("oak", HomesteadLayout.Shown(grove, T(4, 1)));
        }

        [Test]
        public void FillCountsEveryTileAFootprintCovers()
        {
            var grove = Grove();
            var region = grove.Floor.Region("all");

            // 64 tiles, four of them the hall, and the starter friend already on one.
            Assert.AreEqual(1, HomesteadLayout.CoveredCount(grove, region), "the friend counts");
            Assert.AreEqual(1f / 60f, HomesteadLayout.FillOf(grove, region), 1e-5f);

            HomesteadLayout.Place(T(4, 4), "house");
            Assert.AreEqual(5, HomesteadLayout.CoveredCount(grove, region));
            Assert.AreEqual(5f / 60f, HomesteadLayout.FillOf(grove, region), 1e-5f);

            HomesteadLayout.Place(T(6, 6), "fence");
            Assert.AreEqual(6, HomesteadLayout.CoveredCount(grove, region));
        }

        // ============================================================== placing
        [Test]
        public void PlacingFitsTheFootprintAroundTheTouchedTile()
        {
            var grove = Grove();

            // Room at the touch: the touched tile is the anchor and the piece extends toward
            // the viewer from it.
            Assert.AreEqual(GrovePlaceResult.Placed,
                            HomesteadLayout.TryPlace(grove, 4, 4, "house", out int col, out int row));
            Assert.AreEqual((4, 4), (col, row));
            Assert.AreEqual("house", HomesteadLayout.At(T(4, 4)));

            // Against the far edge: the anchor is pulled back so the piece still includes the
            // touched tile rather than being refused.
            Assert.AreEqual(GrovePlaceResult.Placed,
                            HomesteadLayout.TryPlace(grove, 7, 7, "house", out col, out row));
            Assert.AreEqual((6, 6), (col, row));
        }

        [Test]
        public void PlacingWhereNothingFitsIsSaidRatherThanSwallowed()
        {
            var grove = Grove();

            // A ring of fences leaves a single free tile with no room for a 2x2 around it.
            for (int c = 4; c <= 6; c++)
                for (int r = 4; r <= 6; r++)
                    if (c != 5 || r != 5) HomesteadLayout.Place(T(c, r), "fence");

            Assert.AreEqual(GrovePlaceResult.NoRoom,
                            HomesteadLayout.TryPlace(grove, 5, 5, "house", out _, out _));
            Assert.AreEqual(string.Empty, HomesteadLayout.At(T(5, 5)), "a refusal writes nothing");

            // A single tile still goes there.
            Assert.AreEqual(GrovePlaceResult.Placed,
                            HomesteadLayout.TryPlace(grove, 5, 5, "oak", out _, out _));
        }

        [Test]
        public void PlacingOntoACoveredTileReplacesTheWholeStand()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(4, 4), "house");

            // Touching the house's front tile and choosing a fence: the house goes, the fence
            // lands on the touched tile, and both rows carry one stamp.
            Assert.AreEqual(GrovePlaceResult.Placed,
                            HomesteadLayout.TryPlace(grove, 5, 5, "fence", out int col, out int row));

            Assert.AreEqual((5, 5), (col, row));
            Assert.AreEqual("fence", HomesteadLayout.At(T(5, 5)));
            Assert.AreEqual(string.Empty, HomesteadLayout.At(T(4, 4)), "the house's anchor was cleared");
            Assert.IsFalse(HomesteadLayout.Occupancy(grove).IsCovered(4, 5));

            // And clearing through a covered tile clears the anchor.
            HomesteadLayout.Place(T(4, 4), "house");
            Assert.AreEqual(GrovePlaceResult.Placed,
                            HomesteadLayout.TryPlace(grove, 5, 4, string.Empty, out col, out row));
            Assert.AreEqual((4, 4), (col, row));
            Assert.AreEqual(string.Empty, HomesteadLayout.At(T(4, 4)));
        }

        [Test]
        public void ReChoosingWhatStandsThereChangesNothing()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(4, 4), "house");

            Assert.AreEqual(GrovePlaceResult.Unchanged,
                            HomesteadLayout.TryPlace(grove, 5, 5, "house", out _, out _));
            Assert.AreEqual(GrovePlaceResult.Unchanged,
                            HomesteadLayout.TryPlace(grove, 7, 0, string.Empty, out _, out _),
                            "clearing bare ground");
        }

        // =============================================================== moving
        [Test]
        public void AMovedFootprintFitsAroundTheDropTile()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(4, 4), "house");

            var plan = HomesteadLayout.PlanMove(grove, T(4, 4), 7, 7);
            Assert.IsTrue(plan.Ok);
            Assert.AreEqual((6, 6), (plan.AnchorCol, plan.AnchorRow), "pulled back from the edge");
            Assert.AreEqual(new GroveFootprint(2, 2), plan.Footprint);
            Assert.IsFalse(plan.IsSwap);

            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.Move(grove, T(4, 4), T(7, 7)));
            Assert.AreEqual("house", HomesteadLayout.At(T(6, 6)));
            Assert.AreEqual(string.Empty, HomesteadLayout.At(T(4, 4)));
        }

        [Test]
        public void NudgingAPieceWithinItsOwnFootprintIsAMove()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(4, 4), "house");

            // The house's own tiles do not block it: dropping on its front tile anchors there.
            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.Move(grove, T(4, 4), T(5, 5)));
            Assert.AreEqual("house", HomesteadLayout.At(T(5, 5)));
            Assert.AreEqual(string.Empty, HomesteadLayout.At(T(4, 4)));

            Assert.AreEqual(GrovePlaceResult.Unchanged, HomesteadLayout.Move(grove, T(5, 5), T(5, 5)));
        }

        [Test]
        public void ASwapNeedsRoomForBothPieces()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(4, 4), "house");
            HomesteadLayout.Place(T(0, 6), "oak");

            // The oak fits where the house was and the house fits at the oak: swap.
            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.Move(grove, T(4, 4), T(0, 6)));
            Assert.AreEqual("house", HomesteadLayout.At(T(0, 6)));
            Assert.AreEqual("oak", HomesteadLayout.At(T(4, 4)));
            Assert.IsTrue(HomesteadLayout.Occupancy(grove).IsCovered(1, 7), "the house covers its new footprint");
        }

        [Test]
        public void AMoveThatWouldLandOnTwoDifferentPiecesIsRefused()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(4, 4), "house");
            HomesteadLayout.Place(T(0, 6), "oak");
            HomesteadLayout.Place(T(1, 7), "fence");

            var plan = HomesteadLayout.PlanMove(grove, T(4, 4), 0, 6);
            Assert.AreEqual(GrovePlaceResult.NoRoom, plan.Result);

            Assert.AreEqual(GrovePlaceResult.NoRoom, HomesteadLayout.Move(grove, T(4, 4), T(0, 6)));
            Assert.AreEqual("house", HomesteadLayout.At(T(4, 4)), "a refused move changes nothing");
            Assert.AreEqual("oak", HomesteadLayout.At(T(0, 6)));
        }

        [Test]
        public void ASwapIsRefusedWhenTheDisplacedPieceWouldNotFitBehind()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(4, 4), "oak");
            HomesteadLayout.Place(T(6, 6), "house");
            HomesteadLayout.Place(T(5, 5), "fence");

            // The oak fits on the house's anchor, but the house does not fit where the oak
            // was — the fence stands on what would be its front tile.
            Assert.AreEqual(GrovePlaceResult.NoRoom, HomesteadLayout.PlanMove(grove, T(4, 4), 6, 6).Result);
            Assert.AreEqual(GrovePlaceResult.NoRoom, HomesteadLayout.Move(grove, T(4, 4), T(6, 6)));
            Assert.AreEqual("oak", HomesteadLayout.At(T(4, 4)));
            Assert.AreEqual("house", HomesteadLayout.At(T(6, 6)));

            // Take the fence away and the same drop is a swap.
            HomesteadLayout.Clear(T(5, 5));
            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.Move(grove, T(4, 4), T(6, 6)));
            Assert.AreEqual("house", HomesteadLayout.At(T(4, 4)));
            Assert.AreEqual("oak", HomesteadLayout.At(T(6, 6)));
        }

        // ============================================================== flipping
        [Test]
        public void FlippingALongPieceTurnsItsFootprintAndNeedsTheRoomToDoIt()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(4, 4), "ramp");         // 1 col x 2 rows: covers 4,4 and 4,5

            Assert.IsTrue(HomesteadLayout.Occupancy(grove).IsCovered(4, 5));
            Assert.IsFalse(HomesteadLayout.Occupancy(grove).IsCovered(5, 4));

            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.Flip(grove, T(4, 4)));

            Assert.IsTrue(HomesteadLayout.Occupancy(grove).IsCovered(5, 4), "now 2 cols x 1 row");
            Assert.IsFalse(HomesteadLayout.Occupancy(grove).IsCovered(4, 5));

            // Something standing where the turn would land refuses it, and nothing is written.
            HomesteadLayout.Place(T(4, 5), "fence");
            Assert.AreEqual(GrovePlaceResult.NoRoom, HomesteadLayout.Flip(grove, T(4, 4)));
            Assert.IsTrue(HomesteadLayout.FlippedAt(T(4, 4)), "still facing the way it was");
        }

        // ============================================================ hit masks
        //
        // The grid is the picture's: one cell every GroveHitMask.CellPx art pixels, so a mask
        // is only readable beside the size it was generated for, and a tap's tolerance is a
        // distance on the floor rather than a count of cells.

        [Test]
        public void TheGridIsTheSizeThePictureImplies()
        {
            Assert.AreEqual(0, GroveHitMask.SideFor(0));
            Assert.AreEqual(1, GroveHitMask.SideFor(1));
            Assert.AreEqual(1, GroveHitMask.SideFor(GroveHitMask.CellPx));
            Assert.AreEqual(2, GroveHitMask.SideFor(GroveHitMask.CellPx + 1));
            Assert.AreEqual(6, GroveHitMask.SideFor(88), "a ladder 88 wide");
            Assert.AreEqual(26, GroveHitMask.SideFor(408), "and 408 tall");

            // 6 x 26 = 156 cells, 39 characters; 25 x 23 = 575 cells, 144 characters.
            Assert.AreEqual(39, GroveHitMask.HexLengthFor(88, 408));
            Assert.AreEqual(144, GroveHitMask.HexLengthFor(400, 368));
        }

        [Test]
        public void AMaskRoundTripsThroughItsHexForm()
        {
            // A 6 x 5 grid: 30 cells, so eight characters with two padding bits.
            var mask = GroveHitMask.FromCells(6, 5, (0, 0), (5, 0), (2, 2), (0, 4), (5, 4));
            string hex = mask.ToHex();

            Assert.AreEqual(8, hex.Length);
            Assert.AreEqual("84", hex.Substring(0, 2), "top row: first and last cell, MSB first");

            Assert.IsTrue(GroveHitMask.TryParse(hex, 6 * GroveHitMask.CellPx, 5 * GroveHitMask.CellPx, out var back));
            Assert.AreEqual(hex, back.ToHex());
            Assert.AreEqual(5, back.CellCount);
            Assert.AreEqual((6, 5), (back.Cols, back.Rows));
            Assert.IsTrue(back.Covers(2, 2));
            Assert.IsFalse(back.Covers(3, 2));
        }

        [Test]
        public void AMaskIsRefusedUnlessItIsExactlyItsPicturesLength()
        {
            int w = 88, h = 408;
            int length = GroveHitMask.HexLengthFor(w, h);

            Assert.IsFalse(GroveHitMask.TryParse("", w, h, out _));
            Assert.IsFalse(GroveHitMask.TryParse(new string('0', length - 1), w, h, out _), "short");
            Assert.IsFalse(GroveHitMask.TryParse(new string('0', length + 1), w, h, out _), "long");
            Assert.IsFalse(GroveHitMask.TryParse(new string('g', length), w, h, out _), "not hexadecimal");
            Assert.IsFalse(GroveHitMask.TryParse(new string('0', length), 0, 0, out _), "no picture to read against");
            Assert.IsTrue(GroveHitMask.TryParse(new string('0', length), w, h, out var empty));
            Assert.IsFalse(empty.IsSet, "all zero parses, and reads as no mask");

            // A set bit in the padding is not a mask for this picture.
            Assert.IsFalse(GroveHitMask.TryParse(new string('0', length - 1) + "1", 20, 20, out _));
        }

        [Test]
        public void AHitReadsThePictureRatherThanTheBoxWithinAFingersSlop()
        {
            // A post: one column of cells down the middle of a 10 x 10 picture.
            var cells = new (int, int)[10];
            for (int y = 0; y < 10; y++) cells[y] = (5, y);
            var post = GroveHitMask.FromCells(10, 10, cells);

            // Drawn 320 wide, so a cell is 32 floor pixels and the slop is under one cell.
            var hit = new GroveHit(3, 3, 0f, 0f, 160f, 160f, 99, post, false);

            Assert.IsTrue(hit.Contains(16f, 100f), "on the post");
            Assert.IsTrue(hit.Contains(-10f, -100f), "within the slop of it");
            Assert.IsFalse(hit.Contains(-120f, 0f), "inside the box, well off the post");
            Assert.IsFalse(hit.Contains(0f, 300f), "outside the box");

            // Mirrored, a post on the left is on the right.
            var offset = GroveHitMask.FromCells(10, 10, (1, 5), (1, 6));
            var flipped = new GroveHit(3, 3, 0f, 0f, 160f, 160f, 99, offset, true);
            Assert.IsTrue(flipped.Contains(112f, -20f));
            Assert.IsFalse(flipped.Contains(-112f, -20f));
        }

        [Test]
        public void TheSlopIsADistanceSoASmallPieceIsAsForgivingAsALargeOne()
        {
            // The same one-cell post in a picture drawn 64 wide: a cell is 6.4 floor pixels
            // and the slop spans several of them, so a tap 20 pixels off still lands.
            var cells = new (int, int)[10];
            for (int y = 0; y < 10; y++) cells[y] = (5, y);
            var small = new GroveHit(3, 3, 0f, 0f, 32f, 32f, 99, GroveHitMask.FromCells(10, 10, cells), false);

            Assert.IsTrue(small.Contains(20f, 0f));
            Assert.IsFalse(small.Contains(-31f, 0f), "but not from the far edge of the box");
        }

        [Test]
        public void TheFrontmostPictureTakesTheTouchAndAirDoesNot()
        {
            // A tree behind and a fence in front, boxes overlapping. The tree's mask is only
            // its right half; the fence has no mask.
            var half = new (int, int)[50];
            int n = 0;
            for (int y = 0; y < 10; y++)
                for (int x = 5; x < 10; x++) half[n++] = (x, y);

            var tree = new GroveHit(2, 2, 0f, 200f, 200f, 300f, GroveFootprint.Single.Depth(2, 2),
                                    GroveHitMask.FromCells(10, 10, half), false);
            var fence = new GroveHit(3, 3, 0f, 0f, 100f, 60f, GroveFootprint.Single.Depth(3, 3),
                                     GroveHitMask.None, false);

            Assert.IsTrue(GrovePick.Topmost(new[] { tree, fence }, 0f, 0f, out int col, out int row));
            Assert.AreEqual((3, 3), (col, row), "the fence is in front where both are drawn");

            Assert.IsTrue(GrovePick.Topmost(new[] { tree, fence }, 100f, 400f, out col, out row));
            Assert.AreEqual((2, 2), (col, row), "the tree's painted half");

            Assert.IsFalse(GrovePick.Topmost(new[] { tree, fence }, -150f, 400f, out _, out _),
                           "the tree's empty half lets the ground answer");
        }

        [Test]
        public void ATwoWidePieceIsPickedAsItsAnchorFromItsFrontTilesArt()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(4, 4), "house");
            var index = HomesteadLayout.Occupancy(grove);

            Assert.IsTrue(index.TryAnchored(4, 4, out var stand));
            Assert.AreEqual(4.5f, stand.CentreCol, 1e-5f);
            Assert.AreEqual(stand.Footprint.Depth(4, 4), stand.Depth);
            Assert.AreEqual(GroveFloor.DrawOrder(5, 5) * 2, stand.Depth);
        }
    }
}
