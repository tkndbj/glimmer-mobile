using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// What a grove is worth, and the stars that earns.
    ///
    /// <para>
    /// The score is derived — no field, no merge rule, no floor — so what has to be pinned is
    /// not persistence but the two properties that make deriving it safe: it counts what is
    /// <em>held</em> rather than what is placed, so rearranging a grove cannot change it and
    /// standing one expensive thing on two hundred tiles cannot inflate it; and every input is
    /// irreversible, so it can only ever rise. Both are easy to break with a change that looks
    /// like a tidy-up.
    /// </para>
    /// <para>
    /// The ladder is content, which brings its own failure: a rung that does not rise awards
    /// two stars at one score, and a rung above the value of the whole catalog is a star
    /// nobody can win. The mapper drops the first and the build gate reports both; the cases
    /// below pin the reader's half.
    /// </para>
    /// </summary>
    public sealed class GroveScoreTests
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
            HomesteadLedger.ResetForTests();
            HomesteadLayout.ResetForTests();
            GroveLand.ResetForTests();
        }

        [TearDown]
        public void Restore()
        {
            HomesteadProgress.Set(null);
            HomesteadLedger.ResetForTests();
            HomesteadLayout.ResetForTests();
            GroveLand.ResetForTests();
        }

        // ------------------------------------------------------------- fixtures
        static HomesteadPiece Sold(string id, int cost)
            => new HomesteadPiece(id, id, false, HomesteadPieceKind.Decor, cost,
                                  LevelId.None, ChapterId.None, 1f, .5f);

        static HomesteadPiece Free(string id)
            => new HomesteadPiece(id, id, false, HomesteadPieceKind.Decor, 0,
                                  LevelId.None, ChapterId.None, 1f, .5f);

        /// <summary>A 6x6 field: a free middle region and one for sale beside it.</summary>
        static GroveFloor Field()
            => new GroveFloor(6, 6, string.Empty, GroveFloor.TileId(0, 0), string.Empty,
                              new[]
                              {
                                  new GroveRegion("home", 0, 0, 3, 6, 0),
                                  new GroveRegion("east", 3, 0, 3, 6, 4000),
                              });

        static HomesteadCatalog Grove(GroveScoreTable scores = null)
            => new HomesteadCatalog(Field(),
                                    new[] { Sold("bench", 500), Sold("oak", 2500), Free("pebble") },
                                    scores);

        static void Own(params string[] ids)
            => HomesteadLedger.LoadFrom(new SaveFileDto { homesteadOwned = ids });

        static void OwnLand(params string[] ids)
            => GroveLand.LoadFrom(new SaveFileDto { groveLandOwned = ids });

        static GroveScoreTable Ladder(params long[] at) => new GroveScoreTable(at);

        // ----------------------------------------------------------- what counts
        [Test]
        public void AnUntouchedGroveIsWorthNothing()
        {
            Assert.AreEqual(0L, GroveScore.Value(Grove()));
        }

        [Test]
        public void EveryPieceHeldAddsItsPrice()
        {
            Own("bench", "oak");
            Assert.AreEqual(3000L, GroveScore.Value(Grove()));
        }

        /// <summary>
        /// Starter land has no price and is never written down (invariant 16e), so it adds
        /// nothing without needing a rule to exclude it — and bought land adds what it cost.
        /// </summary>
        [Test]
        public void LandCountsAndStarterLandIsFree()
        {
            Assert.AreEqual(0L, GroveScore.Value(Grove()), "the free region is worth nothing");

            OwnLand("east");
            Assert.AreEqual(4000L, GroveScore.Value(Grove()));
        }

        /// <summary>
        /// A piece earned by playing cost nothing, so it is worth nothing. The score measures
        /// what a player has put into the place, and the bench everybody starts with is not
        /// that — see <c>GroveScore</c>.
        /// </summary>
        [Test]
        public void AFreePieceAddsNothingEvenThoughItIsHeld()
        {
            Assert.IsTrue(HomesteadLedger.IsHeld(Free("pebble")), "a starter piece is held");
            Assert.AreEqual(0L, GroveScore.Value(Grove()));
        }

        /// <summary>
        /// <b>The property the whole design rests on.</b> Holding a piece is permission to draw
        /// it in as many tiles as the player likes (invariant 16), so a score over placements
        /// would be won by standing one expensive thing everywhere — rewarding exactly the
        /// monotony the grove exists to avoid. Placing changes nothing; buying does.
        /// </summary>
        [Test]
        public void PlacingAndRearrangingCannotChangeTheScore()
        {
            Own("oak");
            var catalog = Grove();

            long bare = GroveScore.Value(catalog);

            for (int col = 0; col < 3; col++)
                HomesteadLayout.Place(GroveFloor.TileId(col, 1), "oak");

            Assert.AreEqual(bare, GroveScore.Value(catalog), "twenty oaks are one purchase");

            HomesteadLayout.Clear(GroveFloor.TileId(0, 1));
            Assert.AreEqual(bare, GroveScore.Value(catalog), "emptying a tile is not a refund");
        }

        /// <summary>
        /// Every input is an entitlement and every entitlement here is irreversible, which is
        /// why there is no high-water floor beside the score. If this ever fails, the feature
        /// needs one — and a floor is a stored number, which is where this file's neighbours
        /// have historically gone wrong.
        /// </summary>
        [Test]
        public void TheScoreOnlyEverRises()
        {
            var catalog = Grove();
            long before = GroveScore.Value(catalog);

            Own("bench");
            long mid = GroveScore.Value(catalog);

            OwnLand("east");
            long after = GroveScore.Value(catalog);

            Assert.Less(before, mid);
            Assert.Less(mid, after);
        }

        [Test]
        public void AnUnknownPieceInTheOwnedSetIsWorthNothing()
        {
            // A save written by a newer build can name a piece this catalog has never heard
            // of. It must not be an error and it cannot have a price.
            Own("bench", "gazebo_from_a_later_drop");
            Assert.AreEqual(500L, GroveScore.Value(Grove()));
        }

        // ------------------------------------------------------------ the ladder
        [Test]
        public void StarsAreAwardedAtEachRung()
        {
            var ladder = Ladder(10_000, 20_000, 50_000);

            Assert.AreEqual(0, ladder.StarsFor(0));
            Assert.AreEqual(0, ladder.StarsFor(9_999));
            Assert.AreEqual(1, ladder.StarsFor(10_000), "the rung itself earns the star");
            Assert.AreEqual(1, ladder.StarsFor(19_999));
            Assert.AreEqual(2, ladder.StarsFor(20_000));
            Assert.AreEqual(3, ladder.StarsFor(50_000));
            Assert.AreEqual(3, ladder.StarsFor(long.MaxValue), "the ladder ends");
        }

        [Test]
        public void TheShippedLadderIsTheOneThatWasAskedFor()
        {
            var ladder = GroveScoreTable.Default;

            Assert.AreEqual(5, ladder.StarCount);
            Assert.AreEqual(10_000L, ladder.At(1));
            Assert.AreEqual(20_000L, ladder.At(2));
            Assert.AreEqual(50_000L, ladder.At(3));
            Assert.AreEqual(100_000L, ladder.At(4));
            Assert.AreEqual(200_000L, ladder.At(5));
        }

        [Test]
        public void ARungOutOfOrderIsSortedRatherThanBelieved()
        {
            var ladder = Ladder(50_000, 10_000, 20_000);

            Assert.AreEqual(10_000L, ladder.At(1));
            Assert.AreEqual(50_000L, ladder.At(3));
        }

        [Test]
        public void RungsThatCannotMeanAnythingAreDropped()
        {
            // Zero would award a star to an empty grove; a repeat would land two at once.
            var ladder = Ladder(0, -5, 10_000, 10_000, 20_000);

            Assert.AreEqual(2, ladder.StarCount);
            Assert.AreEqual(10_000L, ladder.At(1));
            Assert.AreEqual(20_000L, ladder.At(2));
        }

        [Test]
        public void ALadderLongerThanTheReadoutCanDrawIsCut()
        {
            var many = new long[GroveScoreTable.MaxStars + 4];
            for (int i = 0; i < many.Length; i++) many[i] = (i + 1) * 1_000;

            Assert.AreEqual(GroveScoreTable.MaxStars, new GroveScoreTable(many).StarCount);
        }

        [Test]
        public void AnEmptyLadderAwardsNoStarsRatherThanThrowing()
        {
            var ladder = Ladder();

            Assert.AreEqual(0, ladder.StarCount);
            Assert.AreEqual(0, ladder.StarsFor(1_000_000));
            Assert.AreEqual(0L, ladder.At(1));
            Assert.AreEqual(0L, ladder.Top);
        }

        // ---------------------------------------------------------- the standing
        [Test]
        public void TheStandingNamesTheNextRungAndHowFarItIs()
        {
            var s = GroveScore.Standing(12_000, Ladder(10_000, 20_000, 50_000));

            Assert.AreEqual(1, s.Stars);
            Assert.AreEqual(3, s.StarCount);
            Assert.AreEqual(10_000L, s.HeldAt);
            Assert.AreEqual(20_000L, s.NextAt);
            Assert.AreEqual(8_000L, s.ToNext);
            Assert.IsFalse(s.IsTopped);
            Assert.AreEqual(.2f, s.Progress, .0001f);
        }

        [Test]
        public void ProgressBelowTheFirstRungIsMeasuredFromZero()
        {
            var s = GroveScore.Standing(2_500, Ladder(10_000, 20_000));

            Assert.AreEqual(0, s.Stars);
            Assert.AreEqual(0L, s.HeldAt);
            Assert.AreEqual(.25f, s.Progress, .0001f);
        }

        [Test]
        public void TheTopOfTheLadderIsFullAndAsksForNothingMore()
        {
            var s = GroveScore.Standing(999_999, Ladder(10_000, 20_000));

            Assert.IsTrue(s.IsTopped);
            Assert.AreEqual(0L, s.NextAt);
            Assert.AreEqual(0L, s.ToNext);
            Assert.AreEqual(1f, s.Progress, .0001f);
        }

        [Test]
        public void ACatalogWithNoLadderFallsBackToTheBuiltInOne()
        {
            // A grove body a version behind must still be able to draw its own standing.
            Assert.AreEqual(GroveScoreTable.Default.StarCount, Grove().Scores.StarCount);
        }

        [Test]
        public void TheCatalogsOwnLadderIsTheOneRead()
        {
            Own("oak");

            var standing = GroveScore.Of(Grove(Ladder(1_000, 2_000)));

            Assert.AreEqual(2_500L, standing.Score);
            Assert.AreEqual(2, standing.Stars);
            Assert.AreEqual(2, standing.StarCount);
        }

        [Test]
        public void EverythingACatalogHoldsIsWhatACompleteGroveIsWorth()
        {
            // 500 + 2500 for the pieces, 4000 for the region that is for sale.
            Assert.AreEqual(7_000L, GroveScore.MaximumValue(Grove()));
        }

        [Test]
        public void NoCatalogAtAllIsZeroRatherThanAThrow()
        {
            Assert.AreEqual(0L, GroveScore.Value(null));
            Assert.AreEqual(0L, GroveScore.MaximumValue(null));
            Assert.AreEqual(0, GroveScore.Of(null).Stars);
        }
    }
}
