using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Placing something, as a decision the player has not finished making.
    ///
    /// <para>
    /// <b>What these are really about is that three answers agree.</b> Where the ghost is, what
    /// it would occupy, and what gets written have to be one answer on every frame of a drag —
    /// and before <see cref="GroveDraft"/> they were three, kept in step by hand across a
    /// screen. The report that started the rework was a wall that painted four tiles and held
    /// one, which is exactly what happens when the picture and the rule are asked separately.
    /// </para>
    /// </summary>
    public sealed class GroveDraftTests
    {
        sealed class FakeProgress : IHomesteadProgress
        {
            public bool IsCleared(LevelId level) => false;
            public bool IsChapterFinished(ChapterId chapter) => false;
        }

        /// <summary>
        /// The layout is a process-wide static, so a test that arranged one would leave it
        /// arranged for whatever runs next.
        /// </summary>
        [SetUp]
        public void Reset()
        {
            HomesteadProgress.Set(new FakeProgress());
            HomesteadLayout.ResetForTests();

            // A draft from stock asks whether the player holds a copy, because a picker is not
            // where a rule lives — so the fixture has to hold some. Three of each is more than
            // any case here places.
            foreach (string id in new[] { "fence", "wall", "hut", "cart" })
                HomesteadLedger.GrantForTests(id, 3);
        }

        [TearDown]
        public void Restore()
        {
            HomesteadProgress.Set(null);
            HomesteadLayout.ResetForTests();
        }

        static HomesteadPiece Piece(string id, int cols = 1, int rows = 1, int facings = 1)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Decor, 100,
                                  LevelId.None, ChapterId.None, 1f, .4f,
                                  HomesteadSlotKind.Ground, bundle: 99,
                                  footprint: new GroveFootprint(cols, rows), facings: facings);

        /// <summary>A 14x14 floor with room to drag a four-tile wall about: the hall on
        /// t_000_000 and the starter friend beside it, as every other grove fixture here.</summary>
        static HomesteadCatalog Grove()
            => new HomesteadCatalog(
                new GroveFloor(14, 14, string.Empty, GroveFloor.TileId(0, 0), GroveFloor.TileId(1, 0),
                               new[] { new GroveRegion("all", 0, 0, 14, 14, 0) }),
                new[] { Piece("fence"), Piece("wall", 4, 2), Piece("hut", 2, 2),
                        Piece("cart", 2, 2, GroveFootprint.Facings) });

        static string T(int col, int row) => GroveFloor.TileId(col, row);

        // =============================================================== lifting
        [Test]
        public void ADraftFromStockStartsWhereItWasAskedForFacingTheWayItWasDrawn()
        {
            var grove = Grove();
            var draft = GroveDraft.FromStock(grove, "wall", 4, 4);

            Assert.IsNotNull(draft);
            Assert.AreEqual(GroveDraftSource.Stock, draft.Source);
            Assert.AreEqual(0, draft.Facing);
            Assert.AreEqual(new GroveFootprint(4, 2), draft.Footprint);
        }

        [Test]
        public void ADraftLiftedOffTheFloorKeepsWhereItStoodAndWhichWayItFaced()
        {
            var grove = Grove();
            HomesteadLayout.TryPlace(grove, 4, 4, "wall", out int col, out int row);
            HomesteadLayout.Turn(grove, T(col, row));

            var draft = GroveDraft.FromFloor(grove, col, row);

            Assert.IsNotNull(draft);
            Assert.AreEqual(GroveDraftSource.Floor, draft.Source);
            Assert.AreEqual(1, draft.Facing);
            Assert.IsTrue(draft.Unmoved);
        }

        [Test]
        public void WhereAPieceCameFromIsTheFootprintItWasStandingAtNotTheOneItWasDrawn()
        {
            // The mark that says "this is what you are leaving" was lit from the catalogue's own
            // footprint, which is the piece as drawn — so a wall that had been turned lit its
            // origin across the grain, on tiles it had never stood on. An odd quarter swaps the
            // axes, and the answer has to come from the draft rather than from the piece.
            var grove = Grove();
            HomesteadLayout.TryPlace(grove, 4, 4, "wall", out int col, out int row);
            HomesteadLayout.Turn(grove, T(col, row));

            var draft = GroveDraft.FromFloor(grove, col, row);
            var drawn = grove.Find("wall").Footprint;

            Assert.AreEqual(1, draft.Facing, "it was standing turned");
            Assert.AreEqual(new GroveFootprint(drawn.Rows, drawn.Cols), draft.FromFootprint,
                            "so the tiles it was covering are the drawn footprint's axes swapped");

            // And it stays put while the piece is turned in the air: the origin is a fact about
            // where it came from, not about how it is being held.
            draft.Turn();
            Assert.AreEqual(new GroveFootprint(drawn.Rows, drawn.Cols), draft.FromFootprint);
            Assert.AreEqual(drawn, draft.Footprint, "while the ghost's own footprint did turn");
        }

        [Test]
        public void LiftingATileAPieceMerelyReachesOverLiftsThePiece()
        {
            // Touching the far end of a wall picks the wall up, not the ground under it.
            var grove = Grove();
            HomesteadLayout.TryPlace(grove, 4, 4, "wall", out int col, out int row);

            var draft = GroveDraft.FromFloor(grove, col + 3, row + 1);

            Assert.IsNotNull(draft);
            Assert.AreEqual("wall", draft.PieceId);
            Assert.AreEqual(col, draft.Col, "it is the stand's anchor, not the tile touched");
        }

        [Test]
        public void TheStarterCompanionCanBeMovedEvenThoughNobodyEverPlacedIt()
        {
            // It is *shown* rather than placed (16f): the tile draws it while it has no row of
            // its own, so a move had to ask what is shown on the slot rather than what is
            // placed there. Asking the narrower question got nothing back and refused, which is
            // the whole of why a player could not move their companion.
            var grove = Grove();
            GroveFloor.TryParse(grove.Floor.StarterTile, out int col, out int row);

            var draft = GroveDraft.FromFloor(grove, col, row);
            Assert.IsNotNull(draft, "the companion is a stand like any other");

            draft.MoveTo(col + 2, row + 2);
            Assert.AreEqual(GrovePlaceResult.Placed, draft.Commit(), "and it may be put down");

            Assert.IsTrue(HomesteadLayout.TryStandAt(grove, draft.Col, draft.Row, out var moved));
            Assert.AreEqual(draft.PieceId, moved.PieceId, "it is standing where it was dropped");

            // And it is not *also* still standing where it started, which is what would happen
            // if the vacating row were not written: the floor draws the starter on that tile
            // for exactly as long as the tile is untouched.
            Assert.IsFalse(HomesteadLayout.TryStandAt(grove, col, row, out _),
                           "the tile it left is empty rather than drawing a second companion");
        }

        [Test]
        public void NothingCanBeLiftedOffBareGroundOrOffTheHall()
        {
            var grove = Grove();
            Assert.IsNull(GroveDraft.FromFloor(grove, 3, 3), "bare ground holds nothing");

            var hall = grove.Floor;
            GroveFloor.TryParse(hall.HallTile, out int hc, out int hr);
            Assert.IsNull(GroveDraft.FromFloor(grove, hc, hr),
                          "the hall is drawn from the best home owned, never placed");
        }

        [Test]
        public void ADwellingCannotBeDraftedFromStockEither()
        {
            // The picker never offers one; the rule is here as well because a picker is not
            // where a rule lives.
            var grove = Grove();
            Assert.IsNull(GroveDraft.FromStock(grove, grove.Floor.StarterPiece, 4, 4));
        }

        // ================================================================ moving
        [Test]
        public void TheGhostIsCentredOnTheTileUnderTheFinger()
        {
            // The anchor is the back corner, so hanging a four-tile wall off the touched tile
            // would draw all of it to one side of the finger.
            var grove = Grove();
            var draft = GroveDraft.FromStock(grove, "wall", 0, 0);

            draft.MoveTo(6, 6);

            Assert.AreEqual(6 - (4 - 1) / 2, draft.Col);
            Assert.AreEqual(6 - (2 - 1) / 2, draft.Row);
        }

        [Test]
        public void ItNeverShiftsItselfSidewaysToMakeSomethingFit()
        {
            // The blind placement path searches outward for a free anchor, which is right when
            // nothing is drawn. A ghost that jumped aside as the finger crossed an occupied
            // tile would be the control arguing with the player.
            var grove = Grove();
            HomesteadLayout.TryPlace(grove, 5, 5, "hut", out _, out _);

            var draft = GroveDraft.FromStock(grove, "fence", 0, 0);
            draft.MoveTo(5, 5);

            Assert.AreEqual((5, 5), (draft.Col, draft.Row), "it stayed where it was put");
            Assert.IsFalse(draft.Fits, "and says so, rather than moving");
        }

        [Test]
        public void ItIsKeptOnTheFloorBecauseATileOffTheWorldIsNotATile()
        {
            var grove = Grove();
            var draft = GroveDraft.FromStock(grove, "wall", 0, 0);

            draft.MoveTo(grove.Floor.Cols + 5, grove.Floor.Rows + 5);

            Assert.LessOrEqual(draft.Col + draft.Footprint.Cols, grove.Floor.Cols);
            Assert.LessOrEqual(draft.Row + draft.Footprint.Rows, grove.Floor.Rows);
        }

        [Test]
        public void AFingerDraggedPastTheEdgeRestsAgainstItRatherThanStickingWhereItWas()
        {
            // The drag hands over whatever tile the finger is geometrically over, which off the
            // near edge is a negative one — a value the old drag could never produce, because it
            // asked a question that refused any point off the floor and the ghost simply froze.
            var grove = Grove();
            var draft = GroveDraft.FromStock(grove, "wall", 6, 6);

            Assert.IsTrue(draft.MoveTo(-40, -40), "it kept moving rather than ignoring the finger");
            Assert.AreEqual((0, 0), (draft.Col, draft.Row), "and came to rest against the corner");
        }

        // ================================================================ turning
        /// <summary>
        /// A barrel, a boulder and a companion have nothing to turn, and the draft is where
        /// that is known — the bar draws its TURN key off this, so what was a live control
        /// doing nothing at all when pressed is now simply not there.
        /// </summary>
        [Test]
        public void APieceWithOnePictureOnASquareFootprintCannotBeTurnedAtAll()
        {
            var grove = Grove();
            var draft = GroveDraft.FromStock(grove, "fence", 4, 4);

            Assert.IsFalse(draft.CanTurn, "one picture, one tile: a quarter is invisible");

            draft.Turn();
            Assert.AreEqual(0, draft.Facing, "so the facing it would write never moves either");
        }

        /// <summary>
        /// The two clauses are separate facts and either alone is enough: a cart is square, so
        /// nothing about the tiles it covers changes, and it still turns because four facings
        /// is four pictures. The wall opposite is the other half — one picture, but an odd
        /// quarter swaps the tiles it stands on.
        /// </summary>
        [Test]
        public void FourPicturesEarnATurnEvenOnASquareFootprintAndASquashedFootprintEarnsOneWithout()
        {
            var grove = Grove();

            var cart = GroveDraft.FromStock(grove, "cart", 4, 4);
            Assert.IsTrue(cart.CanTurn, "four pictures");
            cart.Turn();
            Assert.AreEqual(1, cart.Facing);
            Assert.AreEqual(new GroveFootprint(2, 2), cart.Footprint, "and it covers the same tiles");

            var wall = GroveDraft.FromStock(grove, "wall", 4, 4);
            Assert.IsTrue(wall.CanTurn, "one picture, but the tiles change");
        }

        [Test]
        public void TurningExchangesTheFootprintsAxesAndFourTurnsComeBack()
        {
            var grove = Grove();
            var draft = GroveDraft.FromStock(grove, "wall", 4, 4);

            Assert.AreEqual(new GroveFootprint(4, 2), draft.Footprint);
            draft.Turn();
            Assert.AreEqual(new GroveFootprint(2, 4), draft.Footprint);
            draft.Turn();
            Assert.AreEqual(new GroveFootprint(4, 2), draft.Footprint);

            draft.Turn();
            draft.Turn();
            Assert.AreEqual(0, draft.Facing);
        }

        [Test]
        public void TurningIntoSomethingIsAllowedAndSimplyDoesNotFit()
        {
            // A draft is not committed, so an illegal facing costs nothing and says something.
            // Refusing the turn would make the button dead for a reason nobody can see.
            // Centred on (5,6) the 4x2 wall holds cols 4..7 of rows 6..7; turned it holds
            // cols 4..5 of rows 6..9. A hut on cols 4..5 of rows 8..9 is clear of the first
            // and under the second.
            var grove = Grove();
            HomesteadLayout.TryPlace(grove, 4, 8, "hut", out _, out _);

            var draft = GroveDraft.FromStock(grove, "wall", 0, 0);
            draft.MoveTo(5, 6);
            Assume.That(draft.Fits, Is.True, "lying flat it clears the hut");

            draft.Turn();

            Assert.AreEqual(1, draft.Facing, "it turned");
            Assert.IsFalse(draft.Fits, "and the view paints that red");
        }

        // ================================================================ fitting
        [Test]
        public void APieceLiftedOffTheFloorIgnoresItselfSoItCanGoBackWhereItWas()
        {
            var grove = Grove();
            HomesteadLayout.TryPlace(grove, 4, 4, "wall", out int col, out int row);

            var draft = GroveDraft.FromFloor(grove, col, row);

            Assert.IsTrue(draft.Fits, "its own tiles are not in its way");
            Assert.AreEqual(GrovePlaceResult.Unchanged, draft.Commit(), "and putting it back writes nothing");
        }

        [Test]
        public void APieceCanBeTurnedWhereItStands()
        {
            var grove = Grove();
            HomesteadLayout.TryPlace(grove, 4, 4, "wall", out int col, out int row);

            var draft = GroveDraft.FromFloor(grove, col, row);
            draft.Turn();

            Assume.That(draft.Fits, Is.True);
            Assert.AreEqual(GrovePlaceResult.Placed, draft.Commit());
            Assert.AreEqual(1, HomesteadLayout.FacingAt(T(col, row)));
        }

        // ============================================================ committing
        [Test]
        public void WhatWasShownIsWhatIsTaken()
        {
            // The whole point of the type. The footprint the ghost lit is the footprint the
            // save holds — asked once, from one place.
            var grove = Grove();
            var draft = GroveDraft.FromStock(grove, "wall", 0, 0);
            draft.MoveTo(6, 6);
            draft.Turn();

            var shown = draft.Footprint;
            int col = draft.Col, row = draft.Row;

            Assume.That(draft.Fits, Is.True);
            Assert.AreEqual(GrovePlaceResult.Placed, draft.Commit());

            Assert.IsTrue(HomesteadLayout.TryStandAt(grove, col, row, out var stand));
            Assert.AreEqual(shown, stand.Footprint);
            Assert.AreEqual((col, row), (stand.AnchorCol, stand.AnchorRow));
        }

        [Test]
        public void ADropThatDoesNotFitIsRefusedRatherThanRelocated()
        {
            var grove = Grove();
            HomesteadLayout.TryPlace(grove, 5, 5, "hut", out _, out _);

            var draft = GroveDraft.FromStock(grove, "fence", 0, 0);
            draft.MoveTo(5, 5);

            Assert.AreEqual(GrovePlaceResult.NoRoom, draft.Commit());
            Assert.AreEqual("hut", HomesteadLayout.At(T(5, 5)), "and nothing was written over");
        }

        [Test]
        public void NothingIsWrittenUntilItIsCommitted()
        {
            // A draft abandoned by backing out of the screen, a crash, or a sync landing
            // mid-drag has to leave the grove exactly as it was.
            var grove = Grove();
            HomesteadLayout.TryPlace(grove, 4, 4, "wall", out int col, out int row);

            var draft = GroveDraft.FromFloor(grove, col, row);
            draft.MoveTo(8, 8);
            draft.Turn();

            Assert.AreEqual("wall", HomesteadLayout.At(T(col, row)), "still standing where it was");

            draft.Cancel();
            Assert.AreEqual((col, row), (draft.Col, draft.Row));
            Assert.AreEqual("wall", HomesteadLayout.At(T(col, row)));
        }

        [Test]
        public void AMoveLeavesNothingBehindOnTheTileItCameFrom()
        {
            var grove = Grove();
            HomesteadLayout.TryPlace(grove, 4, 4, "wall", out int col, out int row);

            var draft = GroveDraft.FromFloor(grove, col, row);
            draft.MoveTo(8, 8);
            Assume.That(draft.Fits, Is.True);

            Assert.AreEqual(GrovePlaceResult.Placed, draft.Commit());
            Assert.AreEqual(string.Empty, HomesteadLayout.At(T(col, row)));
            Assert.IsTrue(HomesteadLayout.TryStandAt(grove, draft.Col, draft.Row, out var moved));
            Assert.AreEqual("wall", moved.PieceId);
        }

        [Test]
        public void TakingItAwayEmptiesTheTileAndOnlyWorksOnSomethingStanding()
        {
            var grove = Grove();
            HomesteadLayout.TryPlace(grove, 4, 4, "wall", out int col, out int row);

            var lifted = GroveDraft.FromFloor(grove, col, row);
            Assert.AreEqual(GrovePlaceResult.Placed, lifted.Remove());
            Assert.AreEqual(string.Empty, HomesteadLayout.At(T(col, row)));

            var fresh = GroveDraft.FromStock(grove, "fence", 4, 4);
            Assert.AreEqual(GrovePlaceResult.Refused, fresh.Remove(),
                            "a piece from the inventory was never put down");
        }
    }
}
