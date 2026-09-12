using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Where the hall stands, now that it is the player's decision rather than the floor's.
    ///
    /// <para>
    /// <b>The whole of what changed is one fact moving from content into the save.</b> A hall's
    /// tile was <c>GroveFloor.HallTile</c> — one tile for every grove in the world — so nothing
    /// could pick a home up and nothing needed to ask where one was. It is <c>groveHall</c> now
    /// (save v26), which makes it an <em>instruction</em>: merged by recency against its own
    /// stamp, with a default that is never written down (invariant 11c), and one of exactly two
    /// things in this grove a merge is allowed to lose.
    /// </para>
    /// <para>
    /// What these cases really guard is that every question about the hall is asked in the one
    /// new place. A reader left pointing at the floor is not a compile error and not a crash: it
    /// is a home drawn on one tile and hit-tested on another, or a stretch of ground the hall
    /// has left that can never be built on again.
    /// </para>
    /// </summary>
    public sealed class GroveHallSeatTests
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
            HomesteadLedger.GrantForTests("fence", 3);
        }

        [TearDown]
        public void Restore()
        {
            HomesteadLayout.ResetForTests();
            HomesteadLedger.ResetForTests();
            HomesteadProgress.Set(null);
        }

        // A free cottage, so BestDwelling always answers and the hall always stands.
        static HomesteadPiece Cottage()
            => new HomesteadPiece("home_cottage", "Homestead/home_cottage", false,
                                  HomesteadPieceKind.Dwelling, 0, LevelId.None, ChapterId.None,
                                  1f, .4f, tier: 1,
                                  footprint: new GroveFootprint(2, 2),
                                  facings: GroveFootprint.Facings);

        static HomesteadPiece Fence()
            => new HomesteadPiece("fence", "Homestead/fence", false, HomesteadPieceKind.Decor,
                                  100, LevelId.None, ChapterId.None, 1f, .4f, bundle: 99);

        /// <summary>A 12x12 floor with a 2x2 hall authored at the back corner.</summary>
        static HomesteadCatalog Grove()
            => new HomesteadCatalog(
                new GroveFloor(12, 12, string.Empty, GroveFloor.TileId(1, 1), GroveFloor.TileId(5, 5),
                               new[] { new GroveRegion("all", 0, 0, 12, 12, 0) },
                               new GroveFootprint(2, 2)),
                new[] { Cottage(), Fence() });

        // ============================================================ the seat itself
        [Test]
        public void AGroveNobodyHasRearrangedStandsItsHallWhereTheFloorSays()
        {
            var grove = Grove();

            Assert.IsTrue(HomesteadLayout.HallSeat(grove.Floor, out int col, out int row));
            Assert.AreEqual(1, col);
            Assert.AreEqual(1, row);
            Assert.AreEqual(0, HomesteadLayout.HallFacing);

            // And nothing is written down for it, which is 11c's other half: a grove that has
            // never been rearranged has no opinion, so it can never outrank a device that has.
            var dto = new SaveFileDto();
            HomesteadLayout.WriteInto(dto);
            Assert.IsEmpty(dto.groveHall ?? string.Empty);
            Assert.AreEqual(0L, dto.groveHallSetUnix);
        }

        [Test]
        public void MovingTheHallMovesEveryQuestionAboutWhereItIs()
        {
            var grove = Grove();

            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.MoveHall(grove, 7, 8, 1));

            Assert.IsTrue(HomesteadLayout.HallSeat(grove.Floor, out int col, out int row));
            Assert.AreEqual(7, col);
            Assert.AreEqual(8, row);
            Assert.AreEqual(1, HomesteadLayout.HallFacing);

            // What it covers, and what it has stopped covering. The second half is the one a
            // reader left pointing at the floor would get wrong: ground the hall has left is
            // ordinary ground, and ground it has arrived on is not.
            Assert.IsTrue(HomesteadLayout.IsHall(grove.Floor, 7, 8));
            Assert.IsTrue(HomesteadLayout.IsHall(grove.Floor, 8, 9), "the far corner of a 2x2 hall");
            Assert.IsFalse(HomesteadLayout.IsHall(grove.Floor, 1, 1));

            Assert.IsTrue(GroveLand.IsBuildable(grove.Floor, 1, 1), "the old seat is ordinary ground");
            Assert.IsFalse(GroveLand.IsBuildable(grove.Floor, 7, 8), "and the new one is not");
        }

        [Test]
        public void TheHallMayBeTurnedWhereItStandsRatherThanOnlyMoved()
        {
            var grove = Grove();

            // It overlaps itself completely, which is the case GroveLand.IsBuildable would have
            // refused: buildable ground is owned ground the hall is *not* on, so asking it here
            // would make turning a house on the spot impossible.
            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.MoveHall(grove, 1, 1, 2));
            Assert.AreEqual(2, HomesteadLayout.HallFacing);

            Assert.IsTrue(HomesteadLayout.HallSeat(grove.Floor, out int col, out int row));
            Assert.AreEqual(1, col);
            Assert.AreEqual(1, row);
        }

        [Test]
        public void AHallCannotBeStoodOnTopOfSomethingElse()
        {
            var grove = Grove();
            Assert.AreEqual(GrovePlaceResult.Placed,
                            HomesteadLayout.TryPlace(grove, 7, 8, "fence", out _, out _));

            Assert.AreEqual(GrovePlaceResult.NoRoom, HomesteadLayout.MoveHall(grove, 7, 8, 0));
            Assert.IsTrue(HomesteadLayout.HallSeat(grove.Floor, out int col, out _));
            Assert.AreEqual(1, col, "and it is still where it was");
        }

        [Test]
        public void NothingCanBeStoodWhereTheHallIsNowStanding()
        {
            var grove = Grove();
            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.MoveHall(grove, 7, 8, 0));

            Assert.AreNotEqual(GrovePlaceResult.Placed,
                               HomesteadLayout.TryPlace(grove, 8, 9, "fence", out _, out _),
                               "the far corner of the hall's footprint is not ground");

            // The tile it left is, which is the same fact read the other way — and the one the
            // occupancy index used to refuse for ever, from the floor's own hall tile.
            Assert.AreEqual(GrovePlaceResult.Placed,
                            HomesteadLayout.TryPlace(grove, 1, 1, "fence", out _, out _));
        }

        [Test]
        public void ASeatNamingATileThisFloorHasNotGotFallsBackRatherThanLosingTheHouse()
        {
            var grove = Grove();

            // A drop that shrank the floor under a saved seat, or a save from a build whose
            // grove was bigger. Losing the home over it would be far worse than drawing it
            // where the floor says, which is what every grove started as anyway.
            HomesteadLayout.LoadFrom(new SaveFileDto
            {
                groveHall = GroveFloor.TileId(40, 40),
                groveHallFacing = 2,
                groveHallSetUnix = 1700000000L,
            });

            Assert.IsTrue(HomesteadLayout.HallSeat(grove.Floor, out int col, out int row));
            Assert.AreEqual(1, col);
            Assert.AreEqual(1, row);
        }

        [Test]
        public void ASeatWhoseFootprintWouldHangOffTheEdgeFallsBackToo()
        {
            var grove = Grove();

            // The anchor is on the floor and the 2x2 it needs is not, which is the half a bare
            // Contains on the anchor would wave through.
            HomesteadLayout.LoadFrom(new SaveFileDto
            {
                groveHall = GroveFloor.TileId(11, 11),
                groveHallSetUnix = 1700000000L,
            });

            Assert.IsTrue(HomesteadLayout.HallSeat(grove.Floor, out int col, out int row));
            Assert.AreEqual(1, col);
            Assert.AreEqual(1, row);
        }

        // ============================================================ the round trip
        [Test]
        public void ASeatSurvivesBeingWrittenDownAndReadBack()
        {
            var grove = Grove();
            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.MoveHall(grove, 4, 6, 3));

            var dto = new SaveFileDto();
            HomesteadLayout.WriteInto(dto);

            Assert.AreEqual(GroveFloor.TileId(4, 6), dto.groveHall);
            Assert.AreEqual(3, dto.groveHallFacing);
            Assert.Greater(dto.groveHallSetUnix, 0L, "an instruction carries its own date (11c)");

            HomesteadLayout.ResetForTests();
            HomesteadLayout.LoadFrom(dto);

            Assert.IsTrue(HomesteadLayout.HallSeat(grove.Floor, out int col, out int row));
            Assert.AreEqual(4, col);
            Assert.AreEqual(6, row);
            Assert.AreEqual(3, HomesteadLayout.HallFacing);
        }

        // ============================================================ the merge
        [Test]
        public void ADeviceThatHasMovedItsHallBeatsOneThatNeverTouchedIt()
        {
            // Whichever way round they are joined, and whatever the file dates say: a side with
            // no opinion has nothing to win with. This is the half invariant 11c says a stored
            // default would destroy.
            var mine = HomesteadLayout.JoinHall(string.Empty, 0, 0L, "t_004_006", 2, 500L);
            Assert.AreEqual("t_004_006", mine.Slot);
            Assert.AreEqual(2, mine.Facing);

            var other = HomesteadLayout.JoinHall("t_004_006", 2, 500L, string.Empty, 0, 0L);
            Assert.AreEqual("t_004_006", other.Slot);
            Assert.AreEqual(2, other.Facing);
        }

        [Test]
        public void BetweenTwoDecisionsTheLaterOneWins()
        {
            var a = HomesteadLayout.JoinHall("t_001_001", 0, 400L, "t_009_009", 3, 900L);
            var b = HomesteadLayout.JoinHall("t_009_009", 3, 900L, "t_001_001", 0, 400L);

            Assert.AreEqual("t_009_009", a.Slot);
            Assert.AreEqual(3, a.Facing);
            Assert.AreEqual(900L, a.At);

            Assert.AreEqual(a.Slot, b.Slot, "order-independent, like every other join here");
            Assert.AreEqual(a.Facing, b.Facing);
            Assert.AreEqual(a.At, b.At);
        }

        [Test]
        public void TwoGrovesThatHaveNeverBeenRearrangedJoinToNothingAtAll()
        {
            var join = HomesteadLayout.JoinHall(string.Empty, 0, 0L, string.Empty, 0, 0L);

            Assert.IsEmpty(join.Slot);
            Assert.AreEqual(0L, join.At);
        }

        [Test]
        public void ASeatWithNoStampIsNotADecisionAndCannotWin()
        {
            // groveHallSetUnix is what a merge reads, so a row carrying a slot and no date is
            // either a hand-edited file or a bug — and reading it as a decision would let it
            // outrank a real one for ever, since nothing can be later than a value nothing set.
            var join = HomesteadLayout.JoinHall("t_004_006", 1, 0L, "t_009_009", 2, 100L);

            Assert.AreEqual("t_009_009", join.Slot);
        }

        // ============================================================ the draft
        [Test]
        public void TheHallIsLiftedIntoADraftLikeAnythingElseAndCanBePutDown()
        {
            var grove = Grove();

            var draft = GroveDraft.FromFloor(grove, 1, 1);
            Assert.IsNotNull(draft, "a home is an ordinary thing to pick up now");
            Assert.AreEqual(GroveDraftSource.Hall, draft.Source);
            Assert.AreEqual("home_cottage", draft.PieceId);

            draft.MoveTo(6, 6);
            Assert.IsTrue(draft.Fits);
            Assert.AreEqual(GrovePlaceResult.Placed, draft.Commit());

            Assert.IsTrue(HomesteadLayout.HallSeat(grove.Floor, out int col, out int row));
            Assert.AreEqual(draft.Col, col);
            Assert.AreEqual(draft.Row, row);

            // And it went through MoveHall rather than becoming a placement row: two records of
            // which house the player has are two things a merge can disagree about.
            Assert.IsEmpty(HomesteadLayout.At(GroveFloor.TileId(draft.Col, draft.Row)));
        }

        [Test]
        public void AHallDraftPutBackExactlyWhereItWasWritesNothing()
        {
            var grove = Grove();
            var draft = GroveDraft.FromFloor(grove, 1, 1);

            Assert.AreEqual(GrovePlaceResult.Unchanged, draft.Commit());

            var dto = new SaveFileDto();
            HomesteadLayout.WriteInto(dto);
            Assert.IsEmpty(dto.groveHall ?? string.Empty,
                           "an untouched grove still has no opinion about where its hall is");
        }

        [Test]
        public void AHallDraftFitsOverItsOwnGroundAndNotOverAnybodyElses()
        {
            var grove = Grove();
            Assert.AreEqual(GrovePlaceResult.Placed,
                            HomesteadLayout.TryPlace(grove, 5, 1, "fence", out _, out _));

            var draft = GroveDraft.FromFloor(grove, 1, 1);

            Assert.IsTrue(draft.Fits, "where it already is");
            draft.MoveTo(2, 2);
            Assert.IsTrue(draft.Fits, "and overlapping where it already is");

            draft.MoveTo(5, 1);
            Assert.IsFalse(draft.Fits, "but not on top of the fence");
        }

        [Test]
        public void AHomeCanNeverBeTakenAway()
        {
            var grove = Grove();
            var draft = GroveDraft.FromFloor(grove, 1, 1);

            Assert.AreEqual(GrovePlaceResult.Refused, draft.Remove());
            Assert.IsTrue(HomesteadLayout.HallSeat(grove.Floor, out _, out _));
        }

        [Test]
        public void ATurnedHallIsDrawnAndHitTestedAtTheFacingItWasTurnedTo()
        {
            var grove = Grove();
            Assert.AreEqual(GrovePlaceResult.Placed, HomesteadLayout.MoveHall(grove, 6, 6, 3));

            Assert.IsTrue(HomesteadLayout.TryStandAt(grove, 6, 6, out var stand));
            Assert.IsTrue(stand.IsHall);
            Assert.AreEqual(3, stand.Facing,
                            "forcing nought here would paint one picture and hit-test another");
        }
    }
}
