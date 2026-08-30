using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The hint key's one decision: which flower to point at.
    ///
    /// <para>
    /// <b>The claim worth testing is not "it returns something".</b> A hint here costs a resource
    /// on an eight-hour clock, so the two ways it can be wrong are both expensive and neither is
    /// visible in play until it has already happened: it can point at a tap that quietly loses
    /// the grove, and it can point at the dull half of two equally good answers on a mode whose
    /// entire product is the cascade. Both are asserted below against boards small enough to
    /// check by hand.
    /// </para>
    /// </summary>
    public sealed class BudHintTests
    {
        static BudLayout Grove(string[] rows, string colours)
        {
            int width = rows[0].Length;

            Assert.IsTrue(BudDeal.TryParse(colours, out var deal, out string dealError),
                          dealError);
            Assert.IsTrue(BudLayout.TryReadRows(rows, width, rows.Length,
                                                out var ground, out var value, out string error),
                          error);

            return new BudLayout(width, rows.Length, ground, value, deal);
        }

        // ------------------------------------------------------------------ nothing to say
        [Test]
        public void AGroveWithNoLegalTapIsPointedAtNowhere()
        {
            // Every flower already holds the colour in hand, so mixing it in would change
            // nothing and every tap is refused (BudBoard.CanTap). There is no advice to give,
            // and answering with a cell anyway is how a player spends a hint on nothing.
            var layout = Grove(new[] { "GG..", "GG..", "..o.", "...." }, "G");
            var spot = BudHint.Best(new BudBoard(layout), layout.Deal, 0, 5);

            Assert.IsFalse(spot.Any, "there is no tap here, so there is nothing to point at");
        }

        [Test]
        public void AFinishedGroveIsPointedAtNowhere()
        {
            var layout = Grove(new[] { "RR..", "RR..", "....", "...." }, "G");
            var spot = BudHint.Best(new BudBoard(layout), layout.Deal, 0, 5);

            Assert.IsFalse(spot.Any, "nobody is shut in, so there is nothing left to advise");
        }

        // ------------------------------------------------------------------ it points at a tap
        [Test]
        public void ItOnlyEverPointsAtAFlowerTheColourInHandWouldChange()
        {
            var layout = Grove(new[] { "RGRB", "GRoR", "BRGB", "RGoR" }, "GBR");
            var board = new BudBoard(layout);

            var spot = BudHint.Best(board, layout.Deal, 0, 8);

            Assert.IsTrue(spot.Any);
            Assert.IsTrue(board.IsFlower(spot.Cell), "a cocoon cannot be tapped open");
            Assert.IsTrue(board.CanTap(spot.Cell, layout.Deal.At(0)),
                          "and a tap that mixes nothing in is refused rather than swallowed");
            Assert.AreEqual(board.Mixed(spot.Cell, layout.Deal.At(0)), spot.Colour,
                            "the mark draws what the flower would become, so it has to be told");
        }

        // ------------------------------------------------------------------ and a winning one
        /// <summary>
        /// The claim that matters. A hint that points at a tap the grove cannot be finished from
        /// has spent somebody's hint to cost them the level, and nothing in play would say so
        /// until the satchel ran out.
        /// </summary>
        [Test]
        public void TheTapItPointsAtStillFinishesTheGrove()
        {
            var layout = Grove(new[] { "RGRB", "GRoR", "BRGB", "RGoR" }, "GBR");

            int par = BudSolver.Par(layout);
            Assert.Greater(par, 0, "the fixture has to be solvable for the claim to mean anything");

            var board = new BudBoard(layout);
            var spot = BudHint.Best(board, layout.Deal, 0, par);

            Assert.IsTrue(spot.Any);
            Assert.IsTrue(spot.Proved,
                          "a grove this small is nowhere near the node budget, so the answer " +
                          "should be a proved one rather than the greedy fallback");

            // Take it, and the rest has to fit in what is left.
            board.Tap(spot.Cell, layout.Deal.At(0), null);
            Assert.IsTrue(Finishes(board, layout.Deal, 1, par - 1),
                          "the grove has to still be winnable after the tap the hint sold");
        }

        /// <summary>
        /// And it is the shortest one. A tap that keeps the grove winnable but wastes a turn is
        /// still a tap somebody paid a hint for.
        /// </summary>
        [Test]
        public void ItNeverSpendsATurnTheGroveDidNotNeed()
        {
            var layout = Grove(new[] { "RGRB", "GRoR", "BRGB", "RGoR" }, "GBR");

            int par = BudSolver.Par(layout);
            var board = new BudBoard(layout);
            var spot = BudHint.Best(board, layout.Deal, 0, par + 3);

            board.Tap(spot.Cell, layout.Deal.At(0), null);
            Assert.IsTrue(Finishes(board, layout.Deal, 1, par - 1),
                          "offered more rope than the grove needs, the hint still answers from " +
                          "the shortest play - otherwise a wasted turn ties with a useful one");
        }

        // ------------------------------------------------------------------ and the loud one
        /// <summary>
        /// Among taps that are equally correct, the one that goes off hardest.
        ///
        /// <b>On this mode that is not a nicety.</b> Every board here has many shortest plays by
        /// design (invariant 5d, read backwards - see <c>BudSurvey.Ways</c>), so "a correct tap"
        /// is a weak constraint and the hint would otherwise pick whichever cell came first in
        /// index order. What somebody spends a hint on here is the big version of a move they
        /// could have found anyway.
        /// </summary>
        [Test]
        public void AmongEquallyGoodTapsItTakesTheOneThatGoesOffHardest()
        {
            var layout = Grove(new[] { "RGRB", "GRoR", "BRGB", "RGoR" }, "GBR");
            var board = new BudBoard(layout);

            int par = BudSolver.Par(layout);
            var spot = BudHint.Best(board, layout.Deal, 0, par);
            var chosen = board.Preview(spot.Cell, layout.Deal.At(0));

            foreach (int cell in Winning(board, layout.Deal, par))
            {
                var other = board.Preview(cell, layout.Deal.At(0));

                Assert.IsFalse(Louder(other, chosen),
                               $"tapping {cell} frees {other.Freed}, cracks {other.Cracked} and " +
                               $"runs {other.Waves} waves against the marked cell's " +
                               $"{chosen.Freed}/{chosen.Cracked}/{chosen.Waves}");
            }
        }

        static bool Louder(BudChainResult a, BudChainResult b)
        {
            if (a.Freed != b.Freed) return a.Freed > b.Freed;
            if (a.Cracked != b.Cracked) return a.Cracked > b.Cracked;
            if (a.Waves != b.Waves) return a.Waves > b.Waves;
            return a.Burst > b.Burst;
        }

        // ------------------------------------------------------------------ the plain search
        /// <summary>
        /// Every opening tap this grove can be finished from in <paramref name="par"/>. Written
        /// out here rather than borrowed from <see cref="BudHint"/> on purpose: a test that asks
        /// the thing under test what the right answer is proves nothing.
        /// </summary>
        static List<int> Winning(BudBoard board, BudDeal deal, int par)
        {
            var winners = new List<int>();
            int hand = deal.At(0);

            for (int i = 0; i < board.Count; i++)
            {
                if (!board.CanTap(i, hand)) continue;

                var after = new BudBoard(board);
                after.Tap(i, hand, null);

                if (Finishes(after, deal, 1, par - 1)) winners.Add(i);
            }

            return winners;
        }

        static bool Finishes(BudBoard board, BudDeal deal, int spent, int left)
        {
            if (board.IsFinished) return true;
            if (left <= 0) return false;

            int hand = deal.At(spent);

            for (int i = 0; i < board.Count; i++)
            {
                if (!board.CanTap(i, hand)) continue;

                var after = new BudBoard(board);
                after.Tap(i, hand, null);

                if (Finishes(after, deal, spent + 1, left - 1)) return true;
            }

            return false;
        }
    }
}
