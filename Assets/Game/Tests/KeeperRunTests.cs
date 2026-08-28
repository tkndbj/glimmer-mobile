using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// A Groovekeeper run: the basket, the verdict and the ten lines that let the two move
    /// together.
    ///
    /// <para>
    /// These are the claims that used to be assertions about a screen. A basket taken twice for
    /// one planting, a run ended twice, a continue that hands over tiles onto a board that is
    /// still lost — every one of them is arithmetic over integers here and was a paragraph of
    /// hope before <c>KeeperRun</c> was split out of the board.
    /// </para>
    /// </summary>
    public sealed class KeeperRunTests
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

        static readonly string[] Simple =
        {
            "......",
            "..R...",
            ".G*B..",
            "......",
        };

        static KeeperRun Run(int budget = 8) => new KeeperRun(Grove(Simple, "GRB"), budget);

        [Test]
        public void TheBasketIsTakenOncePerLandedPlanting()
        {
            var run = Run();
            int bed = run.Board.Index(2, 2);

            Assert.AreEqual(0, run.Spent);

            // A tap that cannot be honoured charges nothing. A run that paid for touching stone
            // would quietly cost a player a tile for a move it refused to make.
            run.Plant(run.Board.Index(0, 0), null);
            Assert.AreEqual(0, run.Spent, "a refused planting is not a spend");

            run.Plant(bed, null);
            Assert.AreEqual(1, run.Spent);
            Assert.AreEqual(1, run.Basket.Planted);
            Assert.AreEqual(0, run.Basket.Composted);
        }

        [Test]
        public void CompostingSpendsATileAndMovesTheProcessionOn()
        {
            var run = Run();

            int first = run.Next;
            int standing = run.Board.Planted;
            Assert.IsTrue(run.Compost());

            Assert.AreEqual(1, run.Spent, "both ways of spending cost the same tile");
            Assert.AreEqual(1, run.Basket.Composted);
            Assert.AreNotEqual(first, run.Next, "the procession moved on");
            Assert.AreEqual(standing, run.Board.Planted, "and nothing was planted");
        }

        [Test]
        public void CompostingIsAllowedOnTheLastTile()
        {
            // Withholding it there reads as protective and is the one setting that can produce a
            // grove which will not end: a last tile no cell will take would be unplayable and
            // unspendable at once, which is invariant 20g's state exactly.
            var run = new KeeperRun(Grove(Simple, "GRB"), 1);

            Assert.IsTrue(run.CanCompost);
            Assert.IsTrue(run.Compost());
            Assert.AreEqual(KeeperEnding.Starved, run.Verdict.Ending);
        }

        [Test]
        public void AGroveThatRunsOutOfTilesIsStarvedAndMayBeSoldMore()
        {
            var run = new KeeperRun(Grove(Simple, "RRR"), 2);

            run.Compost();
            Assert.IsFalse(run.Verdict.IsOver);

            run.Compost();
            var verdict = run.Verdict;

            Assert.AreEqual(KeeperEnding.Starved, verdict.Ending);
            Assert.AreEqual(0, verdict.Deficit,
                            "a grove that ran dry always has somewhere to plant, so any tile is "
                            + "a usable tile");
            Assert.IsTrue(verdict.EndsTheRun(live: true, committed: true));
        }

        [Test]
        public void AGroveWithNowhereLeftToGrowIsNeverSoldAContinue()
        {
            // One bare cell beside the sprig and a bed that can never be reached past it. Filling
            // that cell leaves the grove with no opening at all, which no number of tiles fixes.
            var run = new KeeperRun(Grove(new[]
            {
                "#####R",
                "#####.",
                "######",
                "####*#",
            }, "GRB"), 6);

            run.Plant(run.Board.Index(5, 1), null);

            var verdict = run.Verdict;
            Assert.AreEqual(KeeperEnding.Overgrown, verdict.Ending);
            Assert.AreEqual(RunContinueDeficit.None, verdict.Deficit);
        }

        [Test]
        public void AFinishedGroveWinsEvenWithNothingLeft()
        {
            // The order the endings are read in. A grove finished by its last tile has nothing
            // left to want, so it wins rather than being reported as starved.
            var run = new KeeperRun(Grove(Simple, "B"), 1);

            run.Plant(run.Board.Index(2, 2), null);

            Assert.IsTrue(run.Board.IsFinished);
            Assert.AreEqual(KeeperEnding.Grown, run.Verdict.Ending);
            Assert.IsTrue(run.Verdict.IsWon);
            Assert.IsFalse(run.Verdict.EndsTheRun(live: true, committed: true),
                           "a win is not a defeat, whatever the basket says");
        }

        [Test]
        public void ARunIsOnlyEverEndedOnce()
        {
            var run = new KeeperRun(Grove(Simple, "RRR"), 1);
            run.Compost();

            var verdict = run.Verdict;

            Assert.IsTrue(verdict.EndsTheRun(live: true, committed: true));
            Assert.IsFalse(verdict.EndsTheRun(live: false, committed: true),
                           "a run decided twice charges two hearts for one loss");
            Assert.IsFalse(verdict.EndsTheRun(live: true, committed: false),
                           "a run decided before it was committed charges for a board nobody "
                           + "touched");
        }

        [Test]
        public void AGrantMovesTheBasketAndNeverTheGrade()
        {
            var run = new KeeperRun(Grove(Simple, "RRR"), 2);
            run.Compost();
            run.Compost();

            Assert.AreEqual(KeeperEnding.Starved, run.Verdict.Ending);

            int spent = run.Spent;
            run.Grant(6);

            Assert.AreEqual(spent, run.Spent, "a bought run scores exactly what it spent");
            Assert.AreEqual(6, run.Basket.Left);
            Assert.IsFalse(run.Verdict.IsOver);
        }

        [Test]
        public void AnUnboundedBasketNeverRunsOut()
        {
            var run = new KeeperRun(Grove(Simple, "GRB"), KeeperBasket.Unlimited);

            Assert.IsFalse(run.Basket.Bounded);
            Assert.AreEqual(KeeperPressure.Easy, run.Basket.Pressure);

            for (int i = 0; i < 20; i++) run.Compost();

            Assert.IsTrue(run.Basket.Any);
            Assert.IsFalse(run.Verdict.IsOver, "the first groove in the chapter cannot be lost");
        }

        [Test]
        public void ThePressureReadingIsAFractionOfThisGrovesOwnBasket()
        {
            // Fixed counts would mean a different thing on every level: three tiles left is
            // comfortable on a board dealt thirty and desperate on one dealt eight.
            var basket = new KeeperBasket(12);

            Assert.AreEqual(KeeperPressure.Easy, basket.Pressure);

            for (int i = 0; i < 8; i++) basket.Plant();
            Assert.AreEqual(KeeperPressure.Low, basket.Pressure, "four of twelve is under a third");

            for (int i = 0; i < 2; i++) basket.Plant();
            Assert.AreEqual(KeeperPressure.Critical, basket.Pressure, "two of twelve is under a sixth");
        }

        [Test]
        public void TheDeficitConstantAgreesWithTheOneTheContinueAsks()
        {
            // Domain answers the continue's question without reaching across into the
            // progression layer for a constant, so the two have to be pinned together.
            Assert.AreEqual(RunContinue.NoContinue, RunContinueDeficit.None);
        }

        [Test]
        public void TheBestFlourishIsTheLargestOnePlantingOpened()
        {
            var run = new KeeperRun(Grove(new[]
            {
                ".RRG..",
                ".G*R..",
                "..R...",
                "..G...",
            }, "B"), 4);

            var bloomed = new List<int>();
            run.Plant(run.Board.Index(2, 1), bloomed);

            Assert.AreEqual(KeeperFlourish.Most, run.Best);
            Assert.AreEqual(KeeperFlourish.Most, run.Blooms);
            Assert.AreEqual(KeeperFlourish.Most, bloomed.Count);
        }
    }
}
