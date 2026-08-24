using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Grovekeeper's rules, whose whole idea is an inversion: a seam between two <em>different</em>
    /// colours is worth something and a seam between two of the same is worth nothing.
    ///
    /// <para>
    /// The load-bearing case is <see cref="ThePreviewNeverLiesAboutWhatAPlacementIsWorth"/>. The
    /// preview is what the player reads before committing, so if it disagrees with what placing
    /// actually scores, every decision in the game is made on a false number.
    /// </para>
    /// </summary>
    public sealed class KeeperBoardTests
    {
        static KeeperBoard Board(int tiles = 30, uint seed = 7)
            => new KeeperBoard(9, 9, tiles, seed);

        static readonly (int dx, int dy)[] Around = { (0, -1), (1, 0), (0, 1), (-1, 0) };

        /// <summary>What a placement ought to be worth, worked out from the board by hand.</summary>
        static (int seams, bool bloom) Expected(KeeperBoard board, int index, int colour)
        {
            int gathered = colour, seams = 0;
            int x = index % board.Width, y = index / board.Width;

            foreach (var (dx, dy) in Around)
            {
                int nx = x + dx, ny = y + dy;
                if (!board.Inside(nx, ny)) continue;

                int mate = board.At(nx, ny);
                if (mate == Energy.None) continue;

                gathered |= mate;
                if (mate != colour) seams++;
            }

            return (seams, gathered == Energy.All);
        }

        [Test]
        public void TheGroveOpensWithOneTileAlreadyDown()
        {
            // A builder that opens on empty ground has to explain "tap anywhere" before it can
            // explain the rule that matters, which is "place it next to something".
            var board = Board();

            Assert.AreEqual(1, board.Placed);
            Assert.AreNotEqual(Energy.None, board.At(board.Width / 2, board.Height / 2));
            Assert.IsNotEmpty(board.Openings());
        }

        [Test]
        public void ATileMayOnlyGoOnEmptyGroundTouchingSomethingPlaced()
        {
            var board = Board();
            int middle = board.Index(board.Width / 2, board.Height / 2);

            Assert.IsFalse(board.CanPlace(middle), "that cell is already taken");
            Assert.IsFalse(board.CanPlace(0), "the far corner touches nothing");
            Assert.IsTrue(board.CanPlace(middle - board.Width), "directly above is legal");
        }

        [Test]
        public void EveryOpeningIsPlaceableAndEveryPlaceableCellIsAnOpening()
        {
            var board = Board();
            var openings = new HashSet<int>(board.Openings());

            for (int i = 0; i < board.Width * board.Height; i++)
                Assert.AreEqual(board.CanPlace(i), openings.Contains(i),
                                $"cell {i} disagrees between CanPlace and Openings");
        }

        [Test]
        public void ThePreviewNeverLiesAboutWhatAPlacementIsWorth()
        {
            var board = Board(60, 23);

            while (!board.IsDone)
            {
                var openings = board.Openings();
                if (openings.Count == 0) break;

                int at = openings[board.Placed % openings.Count];
                int colour = board.Next;

                var predicted = board.Preview(at);
                var expected = Expected(board, at, colour);

                Assert.AreEqual(expected.seams, predicted.Seams, "preview miscounted the seams");
                Assert.AreEqual(expected.bloom, predicted.Bloom, "preview misjudged the bloom");

                int scoreBefore = board.Score;
                var actual = board.Place(at);

                Assert.AreEqual(predicted.Seams, actual.Seams, "the placement differed from its preview");
                Assert.AreEqual(predicted.Bloom, actual.Bloom);
                Assert.AreEqual(predicted.Score, actual.Score);
                Assert.AreEqual(scoreBefore + predicted.Score, board.Score,
                                "the score moved by something other than what was previewed");
            }
        }

        [Test]
        public void ASeamBetweenTwoOfTheSameColourIsWorthNothing()
        {
            // The inversion the whole mode rests on. If matching ever scored, this would be
            // every other edge-matching game and the twist would be gone.
            var board = Board(80, 5);

            for (int guard = 0; guard < 200 && !board.IsDone; guard++)
            {
                foreach (int at in board.Openings())
                {
                    var expected = Expected(board, at, board.Next);
                    if (expected.seams != 0 || expected.bloom) continue;

                    var gain = board.Place(at);
                    Assert.AreEqual(0, gain.Score,
                                    "a placement touching only its own colour scored");
                    Assert.AreEqual(0, gain.Seams);
                    return;
                }

                var openings = board.Openings();
                if (openings.Count == 0) break;
                board.Place(openings[0]);
            }

            Assert.Pass("the deal never offered a like-on-like placement in this run");
        }

        [Test]
        public void GatheringAllThreeColoursAroundOneTileBlooms()
        {
            var board = Board(200, 3);

            for (int guard = 0; guard < 400 && !board.IsDone; guard++)
            {
                foreach (int at in board.Openings())
                {
                    if (!board.Preview(at).Bloom) continue;

                    int blooms = board.Blooms;
                    var gain = board.Place(at);

                    Assert.IsTrue(gain.Bloom);
                    Assert.AreEqual(blooms + 1, board.Blooms);
                    Assert.GreaterOrEqual(gain.Score, KeeperBoard.BloomScore);
                    Assert.IsTrue(board.IsBloomed(at), "the tile should wear its bloom");
                    return;
                }

                var openings = board.Openings();
                if (openings.Count == 0) break;
                board.Place(openings[openings.Count / 2]);
            }

            Assert.Fail("four hundred placements without a single bloom - the rule is unreachable");
        }

        [Test]
        public void ABloomNeedsAllThreeChannelsAndNotMerelyThreeNeighbours()
        {
            var board = Board(200, 41);

            for (int guard = 0; guard < 400 && !board.IsDone; guard++)
            {
                foreach (int at in board.Openings())
                {
                    var gain = board.Preview(at);
                    var expected = Expected(board, at, board.Next);
                    Assert.AreEqual(expected.bloom, gain.Bloom,
                                    "bloom must mean red, green and blue are all present");
                }

                var openings = board.Openings();
                if (openings.Count == 0) break;
                board.Place(openings[0]);
            }
        }

        [Test]
        public void TheRunEndsWhenTheTilesRunOut()
        {
            var board = Board(12);

            while (!board.IsDone)
            {
                var openings = board.Openings();
                Assert.IsNotEmpty(openings, "ran out of ground before running out of tiles");
                board.Place(openings[0]);
            }

            Assert.AreEqual(12, board.Placed - 1, "the opening tile is not one of the run's");
            Assert.IsFalse(board.CanPlace(board.Openings().Count > 0 ? board.Openings()[0] : 0));
        }

        [Test]
        public void TilesLeftCountsDownToNothing()
        {
            var board = Board(10);
            int last = board.Left;

            while (!board.IsDone)
            {
                board.Place(board.Openings()[0]);
                Assert.Less(board.Left, last, "the counter has to move on every placement");
                last = board.Left;
            }

            Assert.AreEqual(0, board.Left);
        }

        [Test]
        public void OnlyPureColoursAreDealt()
        {
            var board = Board(120, 61);

            while (!board.IsDone)
            {
                int colour = board.Next;
                Assert.IsTrue(colour == Energy.R || colour == Energy.G || colour == Energy.B,
                              $"the deal produced {Energy.Letter(colour)}, which is a seam "
                              + "already made");

                var openings = board.Openings();
                if (openings.Count == 0) break;
                board.Place(openings[0]);
            }
        }

        [Test]
        public void TheSameSeedLaysOutTheSameGrove()
        {
            var a = Board(40, 909);
            var b = Board(40, 909);

            while (!a.IsDone)
            {
                Assert.AreEqual(a.Next, b.Next);
                a.Place(a.Openings()[0]);
                b.Place(b.Openings()[0]);
            }

            Assert.AreEqual(a.Score, b.Score);
            Assert.AreEqual(a.Blooms, b.Blooms);
        }

        [Test]
        public void APlacementNowhereLegalChangesNothing()
        {
            var board = Board();
            int score = board.Score, placed = board.Placed;

            board.Place(0);                       // the far corner, touching nothing

            Assert.AreEqual(score, board.Score);
            Assert.AreEqual(placed, board.Placed, "an illegal placement must not spend a tile");
        }
    }
}
