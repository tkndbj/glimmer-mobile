using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Buying one more go: the price, what it hands over, and the one property that makes it
    /// an offer rather than a charge.
    ///
    /// <para>
    /// <b>The case this suite exists for is
    /// <see cref="ContinuingAThicketHandsOverTheAuthoredTapsAndNothingElse"/>.</b> A glade is lost when
    /// its counter reaches the budget and any turn at all makes it playable again, so selling
    /// fifteen of them cannot go wrong. A weave is lost when the light left cannot cover the
    /// cheapest possible finish — which usually leaves cells in the pot that cannot be spent —
    /// so selling the authored twenty alone would put the player back on a board that is still
    /// provably unwinnable and end the run again in the same frame, <em>having taken their
    /// gems</em>. Nothing in a compile, a validator or a screenshot could see that: the price
    /// is right, the grant lands, the meter goes up, and the run dies anyway.
    /// </para>
    /// <para>
    /// Everything here runs offline. <c>RunContinue.Offer</c> is pure — what the player holds,
    /// what it costs and whether there is a shop are all passed in — precisely because it is
    /// the function that decides whether somebody is asked for money.
    /// </para>
    /// </summary>
    public sealed class RunContinueTests
    {
        [TearDown]
        public void Restore() => ProgressionRules.Reset();

        /// <summary>Installs a table so the live facade reads the authored numbers.</summary>
        static void Publish(ContinueDto carryOn)
        {
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 10,
                continueRun = carryOn,
            };

            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, new List<string>()));
            ProgressionRules.Publish(table);
        }

        static ContinueTable Read(ContinueDto dto, List<string> problems = null)
            => ContinueTable.Resolve(dto, problems ?? new List<string>());

        /// <summary>An unwritten block: every field at its tri-state "inherit".</summary>
        static ContinueDto Unwritten() => new ContinueDto();

        // ================================================================ the content block
        [Test]
        public void AnAbsentBlockKeepsTheBuiltInPrice()
        {
            var table = Read(null);

            Assert.IsTrue(table.Enabled);
            Assert.AreEqual(ContinueLimits.DefaultGems, table.Gems);
            Assert.AreEqual(ContinueLimits.DefaultTurns, table.Turns);
            Assert.AreEqual(ContinueLimits.DefaultInk, table.Ink);
        }

        /// <summary>
        /// The reason <c>enabled</c> is an integer and not a <c>bool</c>.
        ///
        /// <c>JsonUtility</c> instantiates a <c>[Serializable]</c> class field even when the
        /// JSON carries no such key, so a file written before this block existed arrives here
        /// as an object with every field at its default. A <c>bool</c> would default to
        /// <c>false</c> and withdraw the offer from every client that had not taken a content
        /// push — silently, on the one field where silence costs the most.
        /// </summary>
        [Test]
        public void ABlockPresentButUnwrittenStillLeavesTheOfferStanding()
        {
            var table = Read(Unwritten());

            Assert.IsTrue(table.Enabled, "an unwritten block must not read as 'switched off'");
            Assert.AreEqual(ContinueLimits.DefaultGems, table.Gems);
        }

        [Test]
        public void ZeroWithdrawsTheOfferEntirely()
        {
            var table = Read(new ContinueDto { enabled = 0 });

            Assert.IsFalse(table.Enabled);
        }

        /// <summary>
        /// A free continue is not a cheap continue: it is a move budget that no longer ends a
        /// run, which is invariant 5d's complaint about a rule that rejects nothing applied to
        /// a fail state. Refused and named rather than clamped silently.
        /// </summary>
        [Test]
        public void AFreeContinueIsRefusedAndSaysSo()
        {
            var problems = new List<string>();
            var table = Read(new ContinueDto { gems = 0 }, problems);

            Assert.AreEqual(ContinueLimits.DefaultGems, table.Gems);
            Assert.AreEqual(1, problems.Count, string.Join("; ", problems));
            StringAssert.Contains("enabled", problems[0],
                                  "the message has to name the way to actually withdraw it");
        }

        /// <summary>
        /// The mirror of the above: a continue that hands over nothing would charge for a run
        /// that is still lost, which is the exact failure this whole suite is about.
        /// </summary>
        [Test]
        public void AContinueThatHandsOverNothingIsRefusedInBothUnits()
        {
            var problems = new List<string>();
            var table = Read(new ContinueDto { turns = 0, ink = 0 }, problems);

            Assert.AreEqual(ContinueLimits.DefaultTurns, table.Turns);
            Assert.AreEqual(ContinueLimits.DefaultInk, table.Ink);
            Assert.AreEqual(2, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void APriceAboveTheCeilingIsClampedAndNamed()
        {
            var problems = new List<string>();
            var table = Read(new ContinueDto { gems = ContinueLimits.MaxGems + 1 }, problems);

            Assert.AreEqual(ContinueLimits.MaxGems, table.Gems);
            Assert.AreEqual(1, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void TheBlockIsWiredIntoTheTableTheGameReads()
        {
            // A block that resolves perfectly and is never wired into the table is a retune
            // that silently does nothing, which is how a published lever fails.
            Publish(new ContinueDto { gems = 33, turns = 7, ink = 9 });

            Assert.AreEqual(33L, ContinueRules.Table.Gems);
            Assert.AreEqual(7, ContinueRules.Table.AmountFor(ContinueUnit.Turns));
            Assert.AreEqual(9, ContinueRules.Table.AmountFor(ContinueUnit.Ink));
        }

        // ================================================================ the price
        [Test]
        public void TheShippedPriceIsFlatHoweverManyHaveBeenBought()
        {
            var table = Read(null);

            Assert.AreEqual(table.Gems, table.PriceFor(0));
            Assert.AreEqual(table.Gems, table.PriceFor(1));
            Assert.AreEqual(table.Gems, table.PriceFor(50));
        }

        [Test]
        public void AStepMakesEachFurtherContinueDearer()
        {
            var table = Read(new ContinueDto { gems = 20, gemsStep = 10 });

            Assert.AreEqual(20L, table.PriceFor(0));
            Assert.AreEqual(30L, table.PriceFor(1));
            Assert.AreEqual(40L, table.PriceFor(2));
        }

        /// <summary>
        /// Nothing bounds how many continues one run may have, so the one piece of arithmetic
        /// here that could run away is bounded by the same ceiling a published price is. A
        /// wrapped price would be a <em>cheap</em> continue, which is the direction that costs
        /// money.
        /// </summary>
        [Test]
        public void AClimbingPriceSaturatesRatherThanWrapping()
        {
            var table = Read(new ContinueDto { gems = 20, gemsStep = ContinueLimits.MaxGemsStep });

            Assert.AreEqual(ContinueLimits.MaxGems, table.PriceFor(int.MaxValue));
            Assert.AreEqual(ContinueLimits.MaxGems, table.PriceFor(1_000_000));
        }

        // ================================================================ what may be offered
        [Test]
        public void GemsInHandMeanTheOfferIsSimplyTaken()
            => Assert.AreEqual(GemChoice.Spend,
                               GemPrice.ChoiceFor(gemsHeld: 20, price: 20, gemsForSale: false));

        [Test]
        public void ShortOfGemsWithAShopBehindItOffersToSellSome()
            => Assert.AreEqual(GemChoice.BuyGems,
                               GemPrice.ChoiceFor(gemsHeld: 19, price: 20, gemsForSale: true));

        /// <summary>
        /// The branch this project's house rule is about: a control that can never work is
        /// worse than no control. Short of gems in a build with no store leaves nothing to
        /// press, so the offer is withdrawn rather than drawn as a dead end over a frozen
        /// board.
        /// </summary>
        [Test]
        public void ShortOfGemsWithNoShopIsNoOfferAtAll()
            => Assert.AreEqual(GemChoice.Unavailable,
                               GemPrice.ChoiceFor(gemsHeld: 19, price: 20, gemsForSale: false));

        [Test]
        public void AWithdrawnRuleOffersNothingHoweverRichThePlayerIs()
        {
            Publish(new ContinueDto { enabled = 0 });

            var offer = RunContinue.Offer(ContinueUnit.Turns, deficit: 0, taken: 0,
                                          gemsHeld: 1_000_000, gemsForSale: true);

            Assert.IsFalse(offer.Exists);
        }

        /// <summary>
        /// A mode saying "no amount of allowance would help" — a weave with every pair walled
        /// in — must never be sold one. Charging for that would be charging for nothing.
        /// </summary>
        [Test]
        public void ARunThatCannotBeRescuedIsNeverSoldAContinue()
        {
            var offer = RunContinue.Offer(ContinueUnit.Ink, RunContinue.NoContinue, taken: 0,
                                          gemsHeld: 1_000_000, gemsForSale: true);

            Assert.IsFalse(offer.Exists);
        }

        [Test]
        public void AGladesOfferIsExactlyWhatTheTableAuthored()
        {
            Publish(new ContinueDto { gems = 20, turns = 15 });

            var offer = RunContinue.Offer(ContinueUnit.Turns, deficit: 0, taken: 0,
                                          gemsHeld: 20, gemsForSale: false);

            Assert.IsTrue(offer.Exists);
            Assert.IsTrue(offer.Affordable);
            Assert.AreEqual(15, offer.Amount);
            Assert.AreEqual(20L, offer.Gems);
        }

        /// <summary>
        /// The rule that makes this an offer rather than a charge, stated on the arithmetic:
        /// the shortfall is cleared <em>first</em> and the authored figure is working room on
        /// top of it.
        /// </summary>
        [Test]
        public void AShortfallIsClearedBeforeTheAuthoredAllowanceIsCounted()
        {
            Publish(new ContinueDto { gems = 20, ink = 20 });

            var offer = RunContinue.Offer(ContinueUnit.Ink, deficit: 9, taken: 0,
                                          gemsHeld: 20, gemsForSale: false);

            Assert.AreEqual(29, offer.Amount,
                            "nine to un-lose the grove, then the twenty that was sold");
        }

        [Test]
        public void TheOfferQuotesThePriceForTheContinueBeingBought()
        {
            Publish(new ContinueDto { gems = 20, gemsStep = 10, turns = 15 });

            var third = RunContinue.Offer(ContinueUnit.Turns, deficit: 0, taken: 2,
                                          gemsHeld: 1_000, gemsForSale: false);

            Assert.AreEqual(40L, third.Gems);
            Assert.AreEqual(2, third.Taken, "the panel, the debit and the event quote one number");
        }

        // ================================================================ a glade's allowance
        static Puzzle Board(int w, int h, string[] rows, LevelTuning tuning)
        {
            var parsed = LevelGridParser.Parse(new LevelLayout(w, h, rows));
            Assert.IsTrue(parsed.Ok, string.Join("; ", parsed.Errors));
            return new Puzzle(LevelId.Parse("t_level"), w, h, tuning, parsed.Cells);
        }

        [Test]
        public void GrantingTurnsRaisesTheBudgetAndNothingElse()
        {
            // par 1, budget forced to two turns.
            var tuning = new LevelTuning(1, 1f, 1f, 2f);
            var board = Board(2, 1, new[] { "*E#R/1 @W#R/0" }, tuning);

            board.Moves = tuning.MoveBudget;
            board.Evaluate();
            Assert.IsTrue(board.OutOfMoves);

            board.Grant(15);

            Assert.IsFalse(board.OutOfMoves, "a bought turn is a turn");
            Assert.AreEqual(15, board.MovesLeft);
            Assert.AreEqual(tuning.MoveBudget + 15, board.MoveBudget);

            // The half that must not move. Stars are held against par, never against the
            // budget (invariant 22), so a continued run is still graded on what it spent —
            // which is why it can only ever score one.
            Assert.AreEqual(tuning.GoldThreshold, board.Gold);
            Assert.AreEqual(tuning.SilverThreshold, board.Silver);
            Assert.AreEqual(1, board.StarsFor(board.Moves));
        }

        /// <summary>
        /// A restart abandons the run and begins another, at the price of a heart — so a
        /// continue buys <em>this</em> run and not this glade. The alternative would make a
        /// bought budget cheaper to keep than to use.
        /// </summary>
        [Test]
        public void ARestartTakesBoughtTurnsWithIt()
        {
            var tuning = new LevelTuning(1, 1f, 1f, 2f);
            var board = Board(2, 1, new[] { "*E#R/1 @W#R/0" }, tuning);

            board.Grant(15);
            Assert.AreEqual(15, board.Granted);

            board.Reset(board.Snapshot());

            Assert.AreEqual(0, board.Granted);
            Assert.AreEqual(tuning.MoveBudget, board.MoveBudget);
        }

        /// <summary>
        /// Nothing on an unbudgeted board can run out, so a continue could never have been
        /// offered for one — and quietly accepting the grant would leave a player's gem
        /// balance as the only witness to that bug.
        /// </summary>
        [Test]
        public void AnUnbudgetedBoardRefusesAGrantRatherThanBankingIt()
        {
            var tuning = new LevelTuning(1, 1f, 1f, LevelTuning.Unlimited);
            var board = Board(2, 1, new[] { "*E#R/1 @W#R/0" }, tuning);

            board.Grant(15);

            Assert.AreEqual(0, board.Granted);
            Assert.AreEqual(int.MaxValue, board.MoveBudget, "still unbounded, not wrapped");
        }

        // ================================================================ a thicket's allowance
        [Test]
        public void GrantingTapsRaisesTheSatchelAndNeverTheSpend()
        {
            var satchel = new BudSatchel(10);
            for (int i = 0; i < 8; i++) satchel.Take();

            satchel.Grant(20);

            Assert.AreEqual(30, satchel.Dealt);
            Assert.AreEqual(22, satchel.Left);
            Assert.AreEqual(8, satchel.Spent,
                            "the grade is what was spent, and buying does not undo it");
        }

        [Test]
        public void AnUnboundedSatchelRefusesAGrantRatherThanOverflowing()
        {
            var satchel = new BudSatchel(BudSatchel.Unlimited);

            satchel.Grant(20);

            Assert.IsFalse(satchel.Bounded,
                           "a grant must never turn an unbounded thicket into a bounded one");
            Assert.AreEqual(BudSatchel.Unlimited, satchel.Dealt);
        }

        [Test]
        public void ATopUpCanNeverTurnASatchelIntoAnUnboundedOne()
        {
            // The fail state the whole mode rests on would retire itself silently.
            var satchel = new BudSatchel(BudSatchel.Unlimited - 4);

            satchel.Grant(1_000);

            Assert.IsTrue(satchel.Bounded);
        }

        // ================================================================ the case this exists for
        /// <summary>
        /// A thicket that has run out of taps, bought a continue, and can genuinely carry on.
        ///
        /// <para>
        /// A thicket is lost the tap its satchel empties, and one tap is a legal move again — so
        /// the shortfall is nought and the offer is exactly what the table authored. That is the
        /// claim worth pinning: a mode whose deficit was wrong would either sell nothing on a
        /// board that needed a top-up or ask for one on a board that did not.
        /// </para>
        /// </summary>
        [Test]
        public void ContinuingAThicketHandsOverTheAuthoredTapsAndNothingElse()
        {
            var run = new BudRun(TwoCritters(), 1);

            // One tap into the corner, which frees nobody, and the satchel is empty.
            run.Tap(Cell(0, 0), null);

            var lost = run.Verdict;
            Assert.AreEqual(BudEnding.Spent, lost.Ending);
            Assert.AreEqual(0, lost.Deficit, "a tap is a legal move again the moment there is one");

            Publish(new ContinueDto { gems = 20, taps = 4 });

            var offer = RunContinue.Offer(ContinueUnit.Taps, lost.Deficit, taken: 0,
                                          gemsHeld: 20, gemsForSale: false);

            Assert.AreEqual(4, offer.Amount, "the authored figure, with nothing owed on top");

            run.Grant(offer.Amount);

            Assert.AreEqual(BudEnding.Live, run.Verdict.Ending,
                            "a continue that does not continue is a charge");
        }

        [Test]
        public void AThicketStillGoingOwesNothing()
        {
            var run = new BudRun(TwoCritters(), 6);

            Assert.AreEqual(BudEnding.Live, run.Verdict.Ending);
            Assert.AreEqual(0, run.Verdict.Deficit);
        }

        /// <summary>
        /// A grove with nothing left to tap is never sold anything, which is invariant 28f's
        /// rule: the only thing that answer decides is whether it would be honest to take
        /// somebody's gems. Nothing here ever grows a flower back, so no number of taps helps.
        /// </summary>
        [Test]
        public void AThicketWithNothingLeftToTapIsNeverSoldTaps()
        {
            var run = new BudRun(NothingLeftToTap(), 6);

            Assert.AreEqual(BudEnding.Barren, run.Verdict.Ending);
            Assert.AreEqual(RunContinueDeficit.None, run.Verdict.Deficit);
        }

        // ------------------------------------------------------------------ the boards
        const int ThicketWidth = 4;

        static int Cell(int x, int y) => y * ThicketWidth + x;

        /// <summary>A ripe corner and two cocoons the corner's chain cannot reach.</summary>
        static BudLayout TwoCritters()
            => Grove(new[] { "R...", "....", "..Ro", "...o" });

        /// <summary>Nothing tappable at all: white takes no colour, so no tap changes anything.</summary>
        static BudLayout NothingLeftToTap()
            => Grove(new[] { "W...", "....", "...o", "...o" });

        static BudLayout Grove(string[] rows)
        {
            int width = rows[0].Length;

            Assert.IsTrue(BudDeal.TryParse("G", out var deal, out string dealError), dealError);
            Assert.IsTrue(BudLayout.TryReadRows(rows, width, rows.Length,
                                                out var ground, out var value, out string error),
                          error);

            return new BudLayout(width, rows.Length, ground, value, deal);
        }
    }
}
