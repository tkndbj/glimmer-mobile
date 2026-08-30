using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// A <b>living</b> grove: one that falls, grows, carries bombs and ripens between taps.
    ///
    /// <para>
    /// <b>Four rules that arrived together, and they are gated together.</b> A grove with a
    /// strip has all four; one without has none, and behaves exactly as this mode shipped. That
    /// is not politeness to old content — it is what lets eight vector cases go on pinning the
    /// base rule (mix, burst, wash) in isolation from everything built on top of it, and it is
    /// what <see cref="AStillGroveDoesNoneOfIt"/> holds.
    /// </para>
    /// <para>
    /// <c>BudVectorTests</c> pins these against the Python mirror. These are the claims that
    /// would still be worth making if there were only one copy — and two of them are safety
    /// properties rather than behaviour: a cascade still <b>terminates</b>, and the grove at
    /// rest is still <b>settled</b>.
    /// </para>
    /// </summary>
    public sealed class BudLivingTests
    {
        static BudLayout Grove(string[] rows, string colours, string strip)
        {
            Assert.IsTrue(BudDeal.TryParse(colours, out var deal, out string dealError),
                          dealError);

            BudDeal regrow = null;
            if (!string.IsNullOrEmpty(strip))
                Assert.IsTrue(BudDeal.TryParse(strip, out regrow, out string growError,
                                               pure: false),
                              growError);

            int width = rows[0].Length;
            Assert.IsTrue(BudLayout.TryReadRows(rows, width, rows.Length,
                                                out var ground, out var value, out string error),
                          error);

            return new BudLayout(width, rows.Length, ground, value, deal, regrow);
        }

        /// <summary>Three yellows in the bottom row with a full board standing over them.</summary>
        static readonly string[] Stack =
        {
            "BGBG",
            "CMCM",
            "YYRB",
            "GBOM",
        };

        // ------------------------------------------------------------------ it falls
        [Test]
        public void WhatBurstsLeavesAHoleAndTheGroveFallsIntoIt()
        {
            var board = new BudBoard(Grove(Stack, "G", "YBGCM"));
            var drops = new List<BudDrop>();

            board.Tap(board.Index(2, 2), Energy.G, null, null, drops);

            Assert.Greater(drops.Count, 0, "nothing moved at all");

            bool fell = false;
            foreach (var drop in drops)
            {
                if (drop.Grew) continue;

                fell = true;
                Assert.Greater(drop.Cell, drop.From,
                               "a drop reported travelling upward — the grove falls, and a cell " +
                               "index rises down the board");
                Assert.AreEqual(drop.From % board.Width, drop.Cell % board.Width,
                                "and it stays in its own column");
            }

            Assert.IsTrue(fell, "the flowers above the bunch did not come down");
        }

        // ------------------------------------------------------------------ it grows
        [Test]
        public void AndTheHolesFillOnceTheChainHasStopped()
        {
            var layout = Grove(Stack, "G", "YBGCM");
            var board = new BudBoard(layout);

            board.Tap(board.Index(2, 2), Energy.G, null);

            int standing = 0;
            for (int i = 0; i < board.Count; i++)
                if (board.IsFlower(i) || board.IsCocoon(i)) standing++;

            Assert.AreEqual(board.Count, standing,
                            "the grove is not full again: a living grove refills so the fortieth " +
                            "tap is dealt as good a board as the first");
        }

        /// <summary>
        /// And it is <b>settled</b>, which is the rule every level is authored to — now true
        /// after every tap as well as before the first.
        ///
        /// <b>The strip here is deliberately hostile</b>: four magentas in a row, so a grow that
        /// took the strip at face value would drop three alike into the same hole and set off a
        /// chain the player did not cause.
        /// </summary>
        [Test]
        public void AndWhatGrewNeverMadeABunch()
        {
            var board = new BudBoard(Grove(Stack, "G", "MMMMBC"));

            board.Tap(board.Index(2, 2), Energy.G, null);

            Assert.IsFalse(board.AnyBunch(),
                           "the grove grew itself a bunch, so the next tap sets off a cascade " +
                           "nobody caused — which is exactly what BudValidator.Settled refuses " +
                           "of an authored board");
        }

        // ------------------------------------------------------------------ it cannot run away
        /// <summary>
        /// <b>A cascade still terminates, and this is the property regrowth nearly cost.</b>
        ///
        /// Before it, a chain was bounded by a plain fact: every wave took at least three flowers
        /// off a board that never gained any. Growing <em>inside</em> the loop destroys that — new
        /// flowers arrive from a repeating strip, so a grove and a strip that resonate go off,
        /// refill into another bunch, and do it again. Measured on the first cut, two thirds of
        /// opening taps ran straight into the wave ceiling and par collapsed to one. Growing
        /// after the chain restores the proof.
        /// </summary>
        [Test]
        public void ACascadeStillEndsOnItsOwnRatherThanOnTheCeiling()
        {
            // The worst case there is: one colour on the board and one colour in the strip.
            var rows = new[] { "RRBR", "BRRB", "RBRR", "RRoB" };
            var board = new BudBoard(Grove(rows, "R", "RRRRRR"));

            var chain = board.Tap(board.Index(2, 0), Energy.B, null);

            Assert.Less(chain.Waves, BudLayout.MostWaves,
                        "the chain ran to the ceiling, which means it did not stop on its own — " +
                        "the ceiling is a backstop, not a bound anybody should reach");
        }

        // ------------------------------------------------------------------ the bomb
        [Test]
        public void AWhiteFlowerIsABombRatherThanADeadCell()
        {
            var rows = new[] { "BGBG", "CWCM", "YGRB", "GBoM" };
            var layout = Grove(rows, "R", "YBGCM");
            var board = new BudBoard(layout);
            int white = board.Index(1, 1);

            Assert.IsTrue(board.IsBomb(white));
            Assert.IsTrue(board.CanTap(white, Energy.R),
                          "white holds every channel, so mixing does nothing to it — it is " +
                          "tappable because it goes off, not because it takes the colour");

            var chain = board.Tap(white, Energy.R, null);

            Assert.AreEqual(9, chain.Biggest,
                            "the bomb clears the square around it, which on a full board is nine");
            Assert.GreaterOrEqual(chain.Burst, 9);
        }

        [Test]
        public void AndABombOnTheEdgeClearsOnlyWhatIsThere()
        {
            var rows = new[] { "WGBG", "CBCM", "YGRB", "GBoM" };
            var board = new BudBoard(Grove(rows, "R", "YBGCM"));

            var chain = board.Tap(board.Index(0, 0), Energy.R, null);

            Assert.AreEqual(4, chain.Biggest,
                            "a corner has four cells in its square, not nine");
        }

        // ------------------------------------------------------------------ the creep
        /// <summary>
        /// One flower ripens between taps, and always beside somebody still shut in — so the
        /// grove leans toward the player rather than drifting away from them.
        /// </summary>
        [Test]
        public void OneFlowerRipensBesideACocoonBetweenTaps()
        {
            // The cocoon sits well away from the bunch, so it is still shut when the creep looks
            // — a cocoon beside the burst is cracked open by it and there is then nobody for the
            // grove to lean toward.
            var rows = new[] { "BGBG", "CMCM", "YYRB", "GBCM", "OBGY" };
            var layout = Grove(rows, "G", "YBGCM");
            var board = new BudBoard(layout);

            int cocoon = board.Index(0, 4);
            Assert.IsTrue(board.IsCocoon(cocoon), "the fixture wants a cocoon down here");

            board.Tap(board.Index(2, 2), Energy.G, null);

            Assert.IsTrue(board.IsCocoon(cocoon), "and one that survives the chain");

            // Something standing beside it now holds the colour that was just spent. Which cell
            // is the palest neighbour, which is a fact about the position rather than about this
            // test — what is asserted is that the grove leaned toward the cocoon at all.
            var beside = new List<int>();
            layout.Beside(cocoon, beside);

            bool ripened = false;
            foreach (int at in beside)
                if (board.IsFlower(at) && (board.ValueAt(at) & Energy.G) != 0) ripened = true;

            Assert.IsTrue(ripened,
                          "nothing beside the cocoon took the colour that was just spent");
        }

        // ------------------------------------------------------------------ and none of it
        [Test]
        public void AStillGroveDoesNoneOfIt()
        {
            var rows = new[] { "BGBG", "CWCM", "YGRB", "GBoM" };
            var board = new BudBoard(Grove(rows, "R", null));
            int white = board.Index(1, 1);

            Assert.IsFalse(board.IsBomb(white), "a still grove has no bomb");
            Assert.IsFalse(board.CanTap(white, Energy.R),
                           "and white is what it always was there: a flower nothing can be " +
                           "mixed into");

            var drops = new List<BudDrop>();
            board.Tap(board.Index(2, 2), Energy.R, null, null, drops);

            Assert.AreEqual(0, drops.Count, "nothing falls and nothing grows on a still grove");
        }
    }
}
