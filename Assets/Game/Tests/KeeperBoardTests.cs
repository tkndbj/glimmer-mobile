using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Groovekeeper's rules, whose whole idea is an inversion: a seam between two <em>different</em>
    /// colours is worth something and a seam between two of the same is worth nothing.
    ///
    /// <para>
    /// The load-bearing case is <see cref="ThePreviewNeverLiesAboutWhatAPlantingOpens"/>. The
    /// preview is what the player reads before committing, and a planting is permanent — so if the
    /// two ever disagree, every decision in the mode is taken on a false number.
    /// </para>
    /// <para>
    /// The one after it is <see cref="ATileAlreadyBloomingIsNotCountedAgain"/>, which is the bug
    /// the obvious implementation writes: asking whether a neighbour has all three "except this
    /// colour" reads correctly and is wrong, because it takes away a channel the neighbour may
    /// have had from somewhere else. It draws as a flower opening on a tile that opened three
    /// turns ago, and it inflates the flourish count that decides the celebration.
    /// </para>
    /// </summary>
    public sealed class KeeperBoardTests
    {
        static KeeperLayout Grove(string[] rows, string deal)
        {
            Assert.IsTrue(KeeperDeal.TryParse(deal, out var parsed, out string dealError),
                          dealError);
            Assert.IsTrue(KeeperLayout.TryReadRows(rows, rows[0].Length, rows.Length,
                                                   out var ground, out var wants, out var sprigs,
                                                   out string error), error);

            return new KeeperLayout(rows[0].Length, rows.Length, ground, wants, sprigs, parsed);
        }

        static readonly string[] Plain =
        {
            "......",
            "..R...",
            "..*...",
            "......",
        };

        [Test]
        public void AGroveOpensWithItsSprigsStandingAndItsBedsBare()
        {
            var board = new KeeperBoard(Grove(Plain, "GB"));

            Assert.AreEqual(1, board.Planted, "the sprig, and nothing else");
            Assert.AreEqual(Energy.R, board.At(2, 1));
            Assert.AreEqual(1, board.BedsLeft);
            Assert.IsFalse(board.IsFinished);
        }

        [Test]
        public void ATileMayOnlyGoOnBareGroundBesideSomethingStanding()
        {
            var board = new KeeperBoard(Grove(Plain, "GB"));
            int sprig = board.Index(2, 1);

            Assert.IsFalse(board.CanPlant(Energy.G, sprig), "already occupied");
            Assert.IsTrue(board.CanPlant(Energy.G, board.Index(2, 2)), "beside the sprig");
            Assert.IsFalse(board.CanPlant(Energy.G, board.Index(5, 3)),
                           "a grove grows outward from what is standing, it is not sprinkled");
        }

        [Test]
        public void StoneTakesNoTileAndPassesNoLight()
        {
            var board = new KeeperBoard(Grove(new[]
            {
                "..#...",
                "..R...",
                "..*...",
                "......",
            }, "GB"));

            Assert.IsFalse(board.CanPlant(Energy.G, board.Index(2, 0)));

            // And it is not a neighbour either: the tile beside it gathers nothing from it.
            Assert.AreEqual(Energy.R, board.Gathered(board.Index(2, 1)));
        }

        [Test]
        public void AHeartbedRefusesEveryColourButItsOwn()
        {
            var board = new KeeperBoard(Grove(new[]
            {
                "......",
                "..R...",
                "..g...",
                "......",
            }, "GB"));

            int bed = board.Index(2, 2);

            Assert.IsFalse(board.CanPlant(Energy.B, bed), "the wrong colour is refused outright");
            Assert.IsTrue(board.CanPlant(Energy.G, bed));

            // A prism carries every channel, so it satisfies any heartbed.
            Assert.IsTrue(board.CanPlant(Energy.All, bed));
        }

        [Test]
        public void ATileBloomsWhenItselfAndItsNeighboursCarryAllThree()
        {
            var board = new KeeperBoard(Grove(new[]
            {
                "..G...",
                ".R*B..",
                "......",
                "......",
            }, "G"));

            int bed = board.Index(2, 1);
            var gain = board.Preview(Energy.G, bed);

            Assert.AreEqual(1, gain.Blooms);
            Assert.AreEqual(1, gain.Beds);
            Assert.AreEqual(2, gain.Seams, "red and blue are unlike, the green above is not");
        }

        [Test]
        public void ThePreviewNeverLiesAboutWhatAPlantingOpens()
        {
            var rows = new[]
            {
                "..G...",
                ".R*B..",
                "..*...",
                "..R...",
            };

            var board = new KeeperBoard(Grove(rows, "G"));
            var bloomed = new List<int>();

            var openings = new List<int>();
            board.Openings(Energy.G, openings);
            Assert.IsNotEmpty(openings);

            foreach (int at in openings)
            {
                var preview = new KeeperBoard(Grove(rows, "G")).Preview(Energy.G, at);
                var actual = new KeeperBoard(Grove(rows, "G")).Plant(Energy.G, at, bloomed);

                Assert.AreEqual(preview.Blooms, actual.Blooms, "blooms at " + at);
                Assert.AreEqual(preview.Beds, actual.Beds, "beds at " + at);
                Assert.AreEqual(preview.Seams, actual.Seams, "seams at " + at);
                Assert.AreEqual(actual.Blooms, bloomed.Count, "the list matches the count");
            }
        }

        [Test]
        public void OneTileCanOpenTheFourBedsAroundIt()
        {
            // The mode's best moment, and the thing par rewards: a cross of beds that are each one
            // channel short of the same colour, opened together.
            // Four tiles around one bare bed, each one channel short of blue and none of them
            // blooming yet. The blue that lands in the middle finishes all four and itself.
            var board = new KeeperBoard(Grove(new[]
            {
                ".RRG..",
                ".G*R..",
                "..R...",
                "..G...",
            }, "B"));

            foreach (int at in new[] { board.Index(2, 0), board.Index(3, 1),
                                       board.Index(2, 2), board.Index(1, 1) })
                Assert.IsFalse(board.Bloomed(at), "nothing is blooming before the tile lands");

            var gain = board.Preview(Energy.B, board.Index(2, 1));

            Assert.AreEqual(KeeperFlourish.Most, gain.Blooms,
                            "the cell it lands on and the four beside it is the ceiling");
            Assert.AreEqual(1, gain.Beds, "only the bed among them counts towards the goal");
        }

        [Test]
        public void ATileAlreadyBloomingIsNotCountedAgain()
        {
            // The bug the obvious implementation writes. The tile at (1,1) blooms the moment the
            // board is built; laying another red beside it must report nothing new, and the naive
            // "all three except this colour" test reports a second flower.
            var board = new KeeperBoard(Grove(new[]
            {
                ".G....",
                "RBR...",
                "..*...",
                "......",
            }, "R"));

            int already = board.Index(1, 1);
            Assert.IsTrue(board.Bloomed(already), "it is blooming before anybody plays");

            var bloomed = new List<int>();
            var gain = board.Plant(Energy.R, board.Index(1, 2), bloomed);

            Assert.IsFalse(bloomed.Contains(already),
                           "a tile that was already blooming is not news");
        }

        [Test]
        public void AGroveIsFinishedWhenEveryBedIsOpen()
        {
            var board = new KeeperBoard(Grove(new[]
            {
                "..G...",
                ".R*B..",
                "......",
                "......",
            }, "G"));

            Assert.IsFalse(board.IsFinished);
            board.Plant(Energy.G, board.Index(2, 1), null);
            Assert.IsTrue(board.IsFinished);
            Assert.AreEqual(0, board.BedsLeft);
        }

        [Test]
        public void ABedWalledOffFromTheGroveIsReportedLostAndNeverEndsARun()
        {
            // The proof only ever decides whether it would be honest to sell a continue. Ending a
            // run on it is the mistake Lightfall shipped and took back.
            var board = new KeeperBoard(Grove(new[]
            {
                "R.####",
                "..####",
                "####*#",
                "######",
            }, "GB"));

            Assert.IsTrue(board.AnyBedLost());
            Assert.IsFalse(board.IsFinished);
        }

        [Test]
        public void AGroveWithRoomLeftIsNotOvergrown()
        {
            var board = new KeeperBoard(Grove(Plain, "GB"));
            Assert.IsTrue(board.AnyRoom);

            var walled = new KeeperBoard(Grove(new[]
            {
                "#####R",
                "#####*",
                "######",
                "######",
            }, "GB"));

            // The bed beside the sprig is the only cell left, so there is room until it is used.
            Assert.IsTrue(walled.AnyRoom);
            walled.Plant(Energy.G, walled.Index(5, 1), null);
            Assert.IsFalse(walled.AnyRoom);
        }

        [Test]
        public void TheAuthoredRowsSurviveARoundTrip()
        {
            var rows = new[]
            {
                ".#R...",
                "..*g..",
                "...b..",
                "..*...",
            };

            CollectionAssert.AreEqual(rows, Grove(rows, "RGBP").Written());
        }

        [Test]
        public void ADealIsWrittenInPureLightAndPrisms()
        {
            Assert.IsTrue(KeeperDeal.TryParse("RGB P", out var deal, out _));
            Assert.AreEqual(4, deal.Count);
            Assert.AreEqual(Energy.All, deal.At(3));
            Assert.AreEqual(1, deal.Prisms);
            Assert.AreEqual("RGBP", deal.Written());

            // It cycles, because a continue deals more tiles than the author wrote.
            Assert.AreEqual(Energy.R, deal.At(4));

            Assert.IsFalse(KeeperDeal.TryParse("RGY", out _, out string error));
            StringAssert.Contains("blend", error);
        }
    }
}
