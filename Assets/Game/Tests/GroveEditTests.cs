using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Editing a grove that has already been built: moving a piece, and turning it round.
    ///
    /// <para>
    /// Placing was always easy and editing was not — a piece could only be moved by clearing
    /// one tile and finding the same thing again in the shop's grid, which is two operations
    /// and a search for what the player experiences as one gesture. <c>Move</c> and
    /// <c>Flip</c> are that gesture, and both write to the one part of the save file that can
    /// lose something (invariant 11c), which is what this suite is for.
    /// </para>
    /// <para>
    /// <b>A flip and not a rotation.</b> Every piece in the catalog is a single drawing from
    /// one fixed isometric angle — the packs the art was cut from ship no directional variants
    /// — so there is no second sprite to rotate to, and rotating the transform would turn the
    /// painting rather than the object. A mirror is the only transform that leaves an
    /// isometric drawing standing on its own tile. See <see cref="Placement.Flipped"/>.
    /// </para>
    /// </summary>
    public sealed class GroveEditTests
    {
        sealed class FakeProgress : IHomesteadProgress
        {
            public bool IsCleared(LevelId level) => false;
            public bool IsChapterFinished(ChapterId chapter) => false;
        }

        /// <summary>
        /// The layout is a process-wide static, so a test that arranged one would leave it
        /// arranged for whatever runs next. The guard <c>HomesteadTests</c> uses, for its reason.
        /// </summary>
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
        static HomesteadPiece Piece(string id)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Decor,
                                  0, LevelId.None, ChapterId.None, 1f, .5f);

        /// <summary>A 6×6 floor: the hall on t_000_000 and the starter friend on t_001_000.</summary>
        static HomesteadCatalog Grove()
            => new HomesteadCatalog(
                new GroveFloor(6, 6, string.Empty, GroveFloor.TileId(0, 0), GroveFloor.TileId(1, 0),
                               new[] { new GroveRegion("all", 0, 0, 6, 6, 0) }),
                new[] { Piece("fence"), Piece("oak"), Piece("well") });

        static string T(int col, int row) => GroveFloor.TileId(col, row);

        static Dictionary<string, HomesteadPlacementDto> ById(HomesteadPlacementDto[] rows)
        {
            var map = new Dictionary<string, HomesteadPlacementDto>(StringComparer.Ordinal);
            foreach (var row in rows) map[row.slot] = row;
            return map;
        }

        // ============================================================== flipping
        [Test]
        public void AFlipTogglesAndIsRemembered()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(2, 2), "fence");

            Assert.IsFalse(HomesteadLayout.FlippedAt(T(2, 2)), "a piece faces the way it was drawn");

            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.Flip(grove, T(2, 2)));
            Assert.IsTrue(HomesteadLayout.FlippedAt(T(2, 2)));

            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.Flip(grove, T(2, 2)));
            Assert.IsFalse(HomesteadLayout.FlippedAt(T(2, 2)), "and back again — it is a toggle");
        }

        [Test]
        public void FlippingNothingDoesNothing()
        {
            var grove = Grove();

            Assert.AreNotEqual(GrovePlaceResult.Placed, HomesteadLayout.Flip(grove, T(3, 3)), "bare ground has no facing");
            Assert.AreNotEqual(GrovePlaceResult.Placed, HomesteadLayout.Flip(grove, null));
        }

        /// <summary>
        /// The starter companion is <em>shown</em> while its tile has no row and never stored,
        /// because writing it at first launch would stamp it with now and put it back on a
        /// device where the player had moved it (invariant 16f). So flipping it has to write
        /// the friend down as well as the facing — a row saying only "mirrored" would be a row
        /// saying the tile is empty, and the friend would vanish on the first flip.
        /// </summary>
        [Test]
        public void FlippingTheStarterWritesTheCompanionDownAsWellAsTheFacing()
        {
            var grove = Grove();
            string starter = grove.Floor.StarterPiece;

            Assume.That(starter, Is.Not.Empty, "the roster must have a companion nothing gates");
            Assert.AreEqual(starter, HomesteadLayout.Shown(grove, T(1, 0)));
            Assert.AreEqual(string.Empty, HomesteadLayout.At(T(1, 0)), "shown, not stored");

            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.Flip(grove, T(1, 0)));

            Assert.AreEqual(starter, HomesteadLayout.At(T(1, 0)), "the friend is still there");
            Assert.IsTrue(HomesteadLayout.FlippedAt(T(1, 0)));
        }

        /// <summary>
        /// A facing describes the thing standing in the slot, so it cannot outlive it. Carrying
        /// it over would make a slot remember a decision about something that has been taken away.
        /// </summary>
        [Test]
        public void PuttingSomethingElseDownFacesItTheWayItWasDrawn()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(2, 2), "fence");
            HomesteadLayout.Flip(grove, T(2, 2));

            HomesteadLayout.Place(T(2, 2), "oak");

            Assert.IsFalse(HomesteadLayout.FlippedAt(T(2, 2)));
        }

        [Test]
        public void AClearedSlotIsNeverFlipped()
        {
            // Not tidiness: an emptied slot keeps its row (that is how "taken away" survives a
            // merge), and a facing on nothing is a bit the join would have to break a tie on
            // that no player could ever see.
            Assert.IsFalse(new Placement(string.Empty, 100, true).Flipped);

            var grove = Grove();
            HomesteadLayout.Place(T(2, 2), "fence");
            HomesteadLayout.Flip(grove, T(2, 2));
            HomesteadLayout.Clear(T(2, 2));

            Assert.IsFalse(HomesteadLayout.FlippedAt(T(2, 2)));
        }

        // ================================================================ moving
        [Test]
        public void MovingToBareGroundLeavesTheTileBehindEmptied()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(2, 2), "oak");

            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.Move(grove, T(2, 2), T(4, 3)));

            Assert.AreEqual("oak", HomesteadLayout.At(T(4, 3)));
            Assert.AreEqual(string.Empty, HomesteadLayout.At(T(2, 2)));
            Assert.AreEqual(1, HomesteadLayout.OccupiedCount(grove), "moved, not copied");
        }

        /// <summary>
        /// A move and a swap are the same operation on purpose: one path means there is no
        /// state in which a drag is refused for a reason the player has to discover, and every
        /// move is undone by making it again.
        /// </summary>
        [Test]
        public void MovingOntoSomethingSwapsThem()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(2, 2), "oak");
            HomesteadLayout.Place(T(4, 3), "well");

            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.Move(grove, T(2, 2), T(4, 3)));

            Assert.AreEqual("oak", HomesteadLayout.At(T(4, 3)));
            Assert.AreEqual("well", HomesteadLayout.At(T(2, 2)));
            Assert.AreEqual(2, HomesteadLayout.OccupiedCount(grove), "both survive a swap");
        }

        /// <summary>
        /// The facing is a fact about the piece rather than about the ground under it. Leaving
        /// it behind would mean re-flipping after every move, so the two controls would undo
        /// each other.
        /// </summary>
        [Test]
        public void TheFacingTravelsWithThePieceAndSoDoesTheOneItSwappedWith()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(2, 2), "oak");
            HomesteadLayout.Flip(grove, T(2, 2));
            HomesteadLayout.Place(T(4, 3), "well");

            HomesteadLayout.Move(grove, T(2, 2), T(4, 3));

            Assert.IsTrue(HomesteadLayout.FlippedAt(T(4, 3)), "the mirrored oak is still mirrored");
            Assert.IsFalse(HomesteadLayout.FlippedAt(T(2, 2)), "and the well it displaced is not");
        }

        /// <summary>
        /// The two rows a move writes are one decision, so they carry one stamp. A merge takes
        /// both or neither from whichever side is newer; stamped separately, a device could
        /// take the arrival without the departure and draw the piece twice.
        /// </summary>
        [Test]
        public void BothRowsOfAMoveShareOneInstant()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(2, 2), "oak");
            HomesteadLayout.Move(grove, T(2, 2), T(4, 3));

            var save = new SaveFileDto();
            HomesteadLayout.WriteInto(save);
            var rows = ById(save.homesteadPlaced);

            Assert.AreEqual(rows[T(2, 2)].setUnix, rows[T(4, 3)].setUnix);
            Assert.Greater(rows[T(4, 3)].setUnix, 0L);
        }

        [Test]
        public void MovingTheStarterTakesTheFriendWithIt()
        {
            var grove = Grove();
            string starter = grove.Floor.StarterPiece;

            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.Move(grove, T(1, 0), T(3, 4)));

            Assert.AreEqual(starter, HomesteadLayout.At(T(3, 4)));
            Assert.AreEqual(string.Empty, HomesteadLayout.At(T(1, 0)));
            Assert.AreEqual(string.Empty, HomesteadLayout.Shown(grove, T(1, 0)),
                            "the tile now has a row, so it no longer draws the starter");
        }

        [Test]
        public void TheHallIsRefusedAtBothEnds()
        {
            // It is drawn from the best home the player owns rather than placed (invariant 16),
            // so it has nothing to give and no room to take.
            var grove = Grove();
            HomesteadLayout.Place(T(2, 2), "oak");

            Assert.AreNotEqual(GrovePlaceResult.Placed, HomesteadLayout.Move(grove, T(2, 2), T(0, 0)), "nothing may stand on the hall");
            Assert.AreNotEqual(GrovePlaceResult.Placed, HomesteadLayout.Move(grove, T(0, 0), T(2, 3)), "and the hall cannot be picked up");

            Assert.AreEqual("oak", HomesteadLayout.At(T(2, 2)), "a refused move changes nothing");
        }

        [Test]
        public void AMoveThatGoesNowhereIsRefused()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(2, 2), "oak");

            Assert.AreNotEqual(GrovePlaceResult.Placed, HomesteadLayout.Move(grove, T(2, 2), T(2, 2)));
            Assert.AreNotEqual(GrovePlaceResult.Placed, HomesteadLayout.Move(grove, T(3, 3), T(4, 4)), "bare ground holds nothing");
            Assert.AreNotEqual(GrovePlaceResult.Placed, HomesteadLayout.Move(grove, T(2, 2), T(9, 9)), "off the floor");
            Assert.AreNotEqual(GrovePlaceResult.Placed, HomesteadLayout.Move(null, T(2, 2), T(3, 3)));
        }

        // ========================================================= the save file
        [Test]
        public void AFacingSurvivesTheRoundTripThroughTheSaveFile()
        {
            var grove = Grove();
            HomesteadLayout.Place(T(2, 2), "fence");
            HomesteadLayout.Flip(grove, T(2, 2));

            var save = new SaveFileDto();
            HomesteadLayout.WriteInto(save);

            Assert.IsTrue(ById(save.homesteadPlaced)[T(2, 2)].flipped);

            HomesteadLayout.ResetForTests();
            HomesteadLayout.LoadFrom(save);

            Assert.IsTrue(HomesteadLayout.FlippedAt(T(2, 2)));
        }

        /// <summary>
        /// A v17 row has no facing at all, and <c>JsonUtility</c> writes false into a field an
        /// older file never had. False is exactly what every v17 row meant — nothing could be
        /// mirrored before this existed — so there is no migration and no ambiguity.
        /// </summary>
        [Test]
        public void AFileWrittenBeforeFacingsReadsAsUnflipped()
        {
            HomesteadLayout.LoadFrom(new SaveFileDto
            {
                homesteadPlaced = new[]
                {
                    new HomesteadPlacementDto { slot = T(2, 2), piece = "fence", setUnix = 900 },
                },
            });

            Assert.AreEqual("fence", HomesteadLayout.At(T(2, 2)));
            Assert.IsFalse(HomesteadLayout.FlippedAt(T(2, 2)));
        }

        /// <summary>
        /// The trap the facing introduced into the merge, and the reason it is worth a test of
        /// its own. Two rows can now agree on the stamp <em>and</em> the piece and still differ,
        /// and the old tie-break fell through to "return the first argument" — which is not a
        /// tie-break at all, it is argument order. Left alone, two devices would each keep their
        /// own facing and push it at the other for ever, which is exactly the property invariant
        /// 11 promises this join does not have.
        /// </summary>
        [Test]
        public void AJoinThatDiffersOnlyInFacingDoesNotDependOnWhoAsked()
        {
            var mine = new[]
            {
                new HomesteadPlacementDto { slot = T(2, 2), piece = "fence", setUnix = 500, flipped = true },
            };
            var theirs = new[]
            {
                new HomesteadPlacementDto { slot = T(2, 2), piece = "fence", setUnix = 500, flipped = false },
            };

            var one = ById(HomesteadLayout.Join(mine, theirs))[T(2, 2)];
            var other = ById(HomesteadLayout.Join(theirs, mine))[T(2, 2)];

            Assert.AreEqual(one.flipped, other.flipped, "commutative");

            // And idempotent: joining a result with either side again must not move it.
            var again = ById(HomesteadLayout.Join(HomesteadLayout.Join(mine, theirs), mine))[T(2, 2)];
            Assert.AreEqual(one.flipped, again.flipped);
        }

        [Test]
        public void TheLaterFacingStillWinsWhenTheStampsDiffer()
        {
            var older = new[]
            {
                new HomesteadPlacementDto { slot = T(2, 2), piece = "fence", setUnix = 100, flipped = true },
            };
            var newer = new[]
            {
                new HomesteadPlacementDto { slot = T(2, 2), piece = "fence", setUnix = 900, flipped = false },
            };

            Assert.IsFalse(ById(HomesteadLayout.Join(older, newer))[T(2, 2)].flipped);
            Assert.IsFalse(ById(HomesteadLayout.Join(newer, older))[T(2, 2)].flipped);
        }

        // ============================================================== touching
        //
        // Reported from play: "I placed a lantern, and pressing its body does nothing — I have
        // to click the tile it stands on." Taps resolved by inverting the isometric transform,
        // which answers a question about the *ground*, and a lantern's post and flame are drawn
        // several tile-heights above the diamond they belong to. See GrovePick.

        static GroveHit Box(int col, int row, float centreX, float centreY, float w, float h)
            => new GroveHit(col, row, centreX, centreY, w * .5f, h * .5f);

        [Test]
        public void PressingThePartOfAPieceThatIsDrawnHighAboveItsTileStillFindsIt()
        {
            // A lantern: a narrow box rising 400 units out of a tile whose own point is at the
            // bottom of it. The flame is the top, and the top is what a finger goes for.
            var lantern = Box(5, 5, 100f, 300f, 120f, 400f);

            Assert.IsTrue(GrovePick.Topmost(new[] { lantern }, 100f, 470f, out int col, out int row),
                          "the flame, near the top of the art");
            Assert.AreEqual(5, col);
            Assert.AreEqual(5, row);

            Assert.IsTrue(GrovePick.Topmost(new[] { lantern }, 100f, 120f, out _, out _),
                          "and the base, near the tile itself");
        }

        [Test]
        public void APointNothingIsDrawnOverLetsTheGroundAnswer()
        {
            var lantern = Box(5, 5, 100f, 300f, 120f, 400f);

            Assert.IsFalse(GrovePick.Topmost(new[] { lantern }, 100f, 40f, out _, out _),
                           "below the art");
            Assert.IsFalse(GrovePick.Topmost(new[] { lantern }, 400f, 300f, out _, out _),
                           "beside it");
            Assert.IsFalse(GrovePick.Topmost(new GroveHit[0], 0f, 0f, out _, out _));
            Assert.IsFalse(GrovePick.Topmost(null, 0f, 0f, out _, out _));
        }

        [Test]
        public void ATileWithNothingStandingOnItIsNeverPicked()
        {
            // An empty tile reports a zero box rather than being left out, so the caller does
            // not have to hold two collections that can disagree about which tiles exist.
            Assert.IsFalse(GrovePick.Topmost(new[] { Box(5, 5, 100f, 300f, 0f, 0f) },
                                             100f, 300f, out _, out _));
        }

        /// <summary>
        /// The rule that makes the answer agree with the picture: frontmost by the same order
        /// the field paints in. A piece's art rises up the screen, which in an isometric
        /// projection is <em>backwards</em>, so a tall piece covers the tiles behind it — and
        /// the tap belongs to the thing the player can actually see.
        /// </summary>
        [Test]
        public void WhereTwoPiecesOverlapTheOneInFrontTakesTheTouch()
        {
            var behind = Box(4, 4, 100f, 320f, 300f, 400f);
            var infront = Box(5, 5, 100f, 300f, 300f, 400f);

            Assume.That(GroveFloor.DrawOrder(5, 5), Is.GreaterThan(GroveFloor.DrawOrder(4, 4)),
                        "nearer the viewer draws later");

            Assert.IsTrue(GrovePick.Topmost(new[] { behind, infront }, 100f, 300f, out int col, out int row));
            Assert.AreEqual(5, col);
            Assert.AreEqual(5, row);

            // And the other way round, because a caller iterating a dictionary of live tiles
            // has no order worth relying on.
            Assert.IsTrue(GrovePick.Topmost(new[] { infront, behind }, 100f, 300f, out col, out row));
            Assert.AreEqual(5, col);
            Assert.AreEqual(5, row);
        }
    }
}
