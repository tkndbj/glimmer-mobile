using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Budburst's rules, one claim at a time.
    ///
    /// <para>
    /// <c>BudVectorTests</c> holds the C# copy to the same file the Python mirror is held to,
    /// which is what stops the two drifting; these are the claims that would still be worth
    /// making if there were only one copy — and two of them are the properties that made this
    /// mode shippable where the two before it were not: <b>it cannot stall</b> and <b>the chain
    /// cannot run away</b>.
    /// </para>
    /// </summary>
    public sealed class BudBoardTests
    {
        static BudLayout Grove(string[] rows, string colours = "G")
        {
            int width = rows[0].Length;

            Assert.IsTrue(BudDeal.TryParse(colours, out var deal, out string dealError),
                          dealError);

            Assert.IsTrue(BudLayout.TryReadRows(rows, width, rows.Length,
                                                out var ground, out var value, out string error),
                          error);

            return new BudLayout(width, rows.Length, ground, value, deal);
        }

        static int At(BudLayout layout, int x, int y) => layout.Index(x, y);

        // ------------------------------------------------------------------ the one rule
        [Test]
        public void TheColourInHandMixesIntoTheFlowerRatherThanReplacingIt()
        {
            // The whole verb. Red with green in hand is *yellow* — a flower the player made, out
            // of the same arithmetic four chapters of glades already taught them.
            var layout = Grove(new[] { "....", ".R..", "....", "...o" });
            var board = new BudBoard(layout);

            board.Tap(At(layout, 1, 1), Energy.G, null);

            Assert.AreEqual(Energy.R | Energy.G, board.ValueAt(At(layout, 1, 1)));
        }

        [Test]
        public void ThreeAlikeTouchingBurstAndTwoDoNot()
        {
            // Three is the bar, and it is what makes a grove a place to build rather than a row
            // of buttons: two alike touching sit there until somebody makes a third.
            var two = new BudBoard(Grove(new[] { "RG..", "G...", "..o.", "...." }));
            var twoLayout = two.Layout;

            var quiet = two.Tap(At(twoLayout, 0, 0), Energy.G, null);
            Assert.AreEqual(0, quiet.Burst, "two alike is not a bunch");

            var three = new BudBoard(Grove(new[] { ".Y..", "YR..", "..o.", "...." }));
            var threeLayout = three.Layout;

            var went = three.Tap(At(threeLayout, 1, 1), Energy.G, null);
            Assert.AreEqual(3, went.Burst, "and the third one sets all three off");
        }

        [Test]
        public void ABurstWashesItsColourIntoEveryFlowerItTouches()
        {
            // The chain, and the clause a loose transcription drops. Without the wash a tap is
            // one pop and everything around it is untouched.
            var layout = Grove(new[] { "YYR.", "..R.", "..o.", "...." });
            var board = new BudBoard(layout);

            board.Tap(At(layout, 2, 0), Energy.G, null);

            int washed = At(layout, 2, 1);
            Assert.AreEqual(Energy.R | Energy.G, board.ValueAt(washed),
                            "the red below took the bunch's yellow and is now yellow itself");
        }

        [Test]
        public void AWashedFlowerThatCompletesAThirdGoesOffInTheNextWave()
        {
            // Wash, then three, then wash again — which is the only thing in this mode that can
            // make one tap worth more than one pop.
            var layout = Grove(new[] { "YYR.", ".RY.", ".YR.", ".Y.o" });
            var board = new BudBoard(layout);

            var chain = board.Tap(At(layout, 2, 0), Energy.G, null);

            Assert.AreEqual(2, chain.Waves, "the reds it washed became yellow and went in turn");
            Assert.AreEqual(8, chain.Burst, "eight of the nine, from one tap");
        }

        [Test]
        public void OldWoodCarriesNothing()
        {
            var layout = Grove(new[] { "YY#Y", "...Y", "..o.", "...." });
            var board = new BudBoard(layout);

            var chain = board.Tap(At(layout, 1, 0), Energy.R, null);

            Assert.AreEqual(0, chain.Burst, "two yellows and a wall is not a bunch");
            Assert.AreEqual(Energy.R | Energy.G, board.ValueAt(At(layout, 3, 0)),
                            "and what is past the wood is untouched");
        }

        [Test]
        public void ATapThatWouldChangeNothingIsRefused()
        {
            // White holds every channel, so mixing into it is a colour spent and a grove exactly
            // as it was — the one move a player makes by accident and never means to.
            var layout = Grove(new[] { "W...", "....", "..o.", "...R" });
            var board = new BudBoard(layout);

            Assert.IsFalse(board.CanTap(At(layout, 0, 0), Energy.G));
            Assert.IsTrue(board.CanTap(At(layout, 3, 3), Energy.G));
        }

        // ------------------------------------------------------------------ the critters
        [Test]
        public void ACocoonIsCrackedByABurstBesideItAndNeverByBeingTapped()
        {
            var layout = Grove(new[] { ".Y..", "YRo.", "....", "...." });
            var board = new BudBoard(layout);

            Assert.IsFalse(board.CanTap(At(layout, 2, 1), Energy.G), "a cocoon is not a flower");

            var chain = board.Tap(At(layout, 1, 1), Energy.G, null);

            Assert.AreEqual(1, chain.Freed);
            Assert.IsTrue(board.IsFinished);
        }

        [Test]
        public void ATougherCocoonTakesOneCrackPerWaveAndNoMore()
        {
            // A cocoon taking a crack from every flower of a bunch that touched it would make
            // the shape of the grove pay for itself.
            // Two of the four that burst are touching the cocoon, and it takes one crack, not
            // two: a cocoon taking a crack per *member* would make the shape of the grove pay
            // for itself.
            var layout = Grove(new[] { ".YO.", "YRY.", "....", "...." });
            var board = new BudBoard(layout);

            var chain = board.Tap(At(layout, 1, 1), Energy.G, null);

            Assert.AreEqual(4, chain.Burst, "the red became yellow and joined the three");
            Assert.AreEqual(1, chain.Cracked, "one crack, from the one wave that touched it");
            Assert.AreEqual(0, chain.Freed);
            Assert.AreEqual(1, board.ValueAt(At(layout, 2, 0)), "one crack left in it");
        }

        // ------------------------------------------------------------------ why it ships
        [Test]
        public void EveryTapMovesTheGroveSoItCanNeverStall()
        {
            // The property the tilt design could not have: there, a board froze into a position
            // where no input changed anything and the player was stuck flipping back and forth.
            // Here a tap that changes nothing is refused outright, so every tap that is *allowed*
            // adds a channel — and channels only ever go up, so the grove always moves.
            var layout = Grove(new[] { "RRRR", "RRRR", "RRRR", "RRRo" }, "GB");
            var board = new BudBoard(layout);

            int taps = 0;
            int carried = Carried(board);

            while (taps < 200)
            {
                int colour = layout.Deal.At(taps);
                int at = -1;

                for (int i = 0; i < layout.Count; i++)
                    if (board.CanTap(i, colour)) { at = i; break; }

                if (at < 0) break;

                board.Tap(at, colour, null);
                taps++;

                int now = Carried(board);
                Assert.AreNotEqual(carried, now, "a tap that moved nothing is a grove that stalls");
                carried = now;
            }

            Assert.Greater(taps, 0);
        }

        /// <summary>Channels standing on the grove, which every allowed tap has to move.</summary>
        static int Carried(BudBoard board)
        {
            int sum = 0;
            for (int i = 0; i < board.Count; i++)
            {
                if (!board.IsFlower(i)) continue;

                int mask = board.ValueAt(i);
                if ((mask & Energy.R) != 0) sum++;
                if ((mask & Energy.G) != 0) sum++;
                if ((mask & Energy.B) != 0) sum++;
            }

            return sum;
        }

        [Test]
        public void AChainIsBoundedByTheFlowersOnTheBoard()
        {
            // The other half: a wave always removes at least three flowers and nothing is ever
            // added, so the longest chain there could be is every flower in one go and the search
            // can never run for ever.
            var layout = Grove(new[] { "YYYY", "YYYY", "YYYo", "YYYY" });
            var board = new BudBoard(layout);

            int flowers = layout.Flowers;
            var chain = board.Tap(At(layout, 0, 0), Energy.B, null);

            Assert.LessOrEqual(chain.Burst, flowers);
            Assert.LessOrEqual(chain.Waves, flowers);
            Assert.Greater(chain.Freed, 0, "and a grove of one colour goes off all at once");
        }

        [Test]
        public void AnAuthoredGroveMustNotAlreadyHoldABunch()
        {
            // A board holding three alike before a tap is spent bursts in the first frame — the
            // player is shown a chain they did not cause, and par is measured against a position
            // they never met. `BudValidator.Settled` refuses one.
            Assert.IsTrue(new BudBoard(Grove(new[] { "YYY.", "....", "..o.", "...." })).AnyBunch());
            Assert.IsFalse(new BudBoard(Grove(new[] { "YYR.", "....", "..o.", "...." })).AnyBunch());
        }

        [Test]
        public void APreviewChangesNothing()
        {
            var layout = Grove(new[] { ".Y..", "YRo.", "....", "...." });
            var board = new BudBoard(layout);

            var seen = board.Preview(At(layout, 1, 1), Energy.G);

            Assert.Greater(seen.Burst, 0);
            Assert.AreEqual(layout.Flowers, board.Flowers, "the grove is untouched");
            Assert.AreEqual(1, board.Shut);
        }

        // ------------------------------------------------------------------ reading a grove
        [Test]
        public void AGroveWrittenOutReadsBackAsItself()
        {
            var rows = new[] { "RG#Y", ".o.O", "B..C", "..M." };
            var layout = Grove(rows);

            CollectionAssert.AreEqual(rows, layout.Written());
            Assert.AreEqual(2, layout.Cocoons);
            Assert.AreEqual(1, layout.ToughCocoons);
            Assert.AreEqual(1, layout.Stones);
            Assert.AreEqual(3, layout.Blends, "Y, C and M");
        }

        [Test]
        public void AnythingThatIsNotPartOfAGroveIsRefused()
        {
            Assert.IsFalse(BudLayout.TryReadRows(new[] { "0...", "....", "....", "...." },
                                                 4, 4, out _, out _, out string zero));
            StringAssert.Contains("not part of a grove", zero);

            Assert.IsFalse(BudLayout.TryReadRows(new[] { "x...", "....", "....", "...." },
                                                 4, 4, out _, out _, out _));
            Assert.IsFalse(BudLayout.TryReadRows(new[] { "..." }, 4, 4, out _, out _, out _));
        }

        [Test]
        public void ABasketIsDealtPureColourAndRepeats()
        {
            // A blend is something the player *makes*. Handing one over would give away the one
            // decision the mode has in it.
            Assert.IsFalse(BudDeal.TryParse("GY", out _, out string blend));
            StringAssert.Contains("pure", blend);

            Assert.IsTrue(BudDeal.TryParse("GRB", out var deal, out _));
            Assert.AreEqual(Energy.G, deal.At(0));
            Assert.AreEqual(Energy.B, deal.At(2));
            Assert.AreEqual(Energy.G, deal.At(3), "and it comes round again");
            Assert.AreEqual("GRB", deal.Written());
        }

        // ------------------------------------------------------------------ the run
        [Test]
        public void EveryTapSpendsOneAndARefusedOneSpendsNothing()
        {
            // Held in a local rather than read through `.Layout.`, which is the shape compile.py
            // refuses: `LevelDefinition.Layout` is null on anything that is not a glade, and the
            // check is textual on purpose.
            var layout = Grove(new[] { ".Y..", "YRo.", "....", "...." });
            var run = new BudRun(layout, 4);

            run.Tap(At(layout, 2, 1), null);            // a cocoon
            Assert.AreEqual(0, run.Spent);

            run.Tap(At(layout, 1, 1), null);
            Assert.AreEqual(1, run.Spent);
        }

        [Test]
        public void AGroveIsWonTheTapTheLastCritterIsOut()
        {
            var layout = Grove(new[] { ".Y..", "YRo.", "....", "...." });
            var run = new BudRun(layout, 4);

            run.Tap(At(layout, 1, 1), null);

            Assert.IsTrue(run.Verdict.IsWon);
            Assert.AreEqual(1, run.Spent, "graded on what it spent, not on what it had");
        }

        [Test]
        public void AGroveWithNothingLeftToTapIsOverAndCannotBeSoldTaps()
        {
            // Nothing here ever grows a flower back and a colour already carried cannot be mixed
            // in twice, so no number of taps would help — and selling one for a board that cannot
            // be finished is the one thing the offer must never do.
            //
            // Note what "nothing left to tap" is *not*: this grove has a flower on it. White
            // holds every channel, so it can never be mixed into. Reading `AnyFlower` here left a
            // board that could be neither won nor ended (invariant 20g), which is why the rule
            // asks `AnyMove` instead.
            var layout = Grove(new[] { "W...", "....", "...o", "...o" });
            var run = new BudRun(layout, 8);

            Assert.AreEqual(BudEnding.Barren, run.Verdict.Ending);
            Assert.AreEqual(RunContinueDeficit.None, run.Verdict.Deficit);
            Assert.IsTrue(run.Verdict.EndsTheRun(live: true, committed: true));
        }

        [Test]
        public void ARunNobodyHasCommittedIsNeverEnded()
        {
            var run = new BudRun(Grove(new[] { ".Y..", "YRo.", "....", "...." }), 0);

            Assert.AreEqual(BudEnding.Spent, run.Verdict.Ending);
            Assert.IsFalse(run.Verdict.EndsTheRun(live: true, committed: false));
        }

        [Test]
        public void TheBestChainIsTheLongestSingleTapAndNotTheTotal()
        {
            var layout = Grove(new[] { "YYR.", ".RY.", ".YR.", ".Y.o" });
            var run = new BudRun(layout, 6);

            int standing = layout.Flowers;
            run.Tap(At(layout, 2, 0), null);

            Assert.AreEqual(2, run.Best, "the longest single chain, which is this one");
            Assert.AreEqual(standing - run.Board.Flowers, run.Burst);
        }

        [Test]
        public void TheColourInHandIsTheOneTheBasketIsUpTo()
        {
            var layout = Grove(new[] { "RRR.", "....", "..o.", "...." }, "GB");
            var run = new BudRun(layout, 6);

            Assert.AreEqual(Energy.G, run.Next);
            Assert.AreEqual(Energy.B, run.Ahead(1));

            run.Tap(At(layout, 0, 0), null);

            Assert.AreEqual(Energy.B, run.Next, "and it moves on exactly when a tap lands");
        }
    }
}
