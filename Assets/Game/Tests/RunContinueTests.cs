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
            Publish(new ContinueDto { gems = 33, turns = 7, ink = 9, wards = 3 });

            Assert.AreEqual(33L, ContinueRules.Table.Gems);
            Assert.AreEqual(7, ContinueRules.Table.AmountFor(ContinueUnit.Turns));
            Assert.AreEqual(9, ContinueRules.Table.AmountFor(ContinueUnit.Ink));
            Assert.AreEqual(3, ContinueRules.Table.AmountFor(ContinueUnit.Wards));
        }

        /// <summary>
        /// A unit with no case in <c>AmountFor</c> falls through to the glade's figure, which is
        /// the one way this table can go wrong silently: the offer exists, the price is right, and
        /// a siege is sold fifteen wards.
        /// </summary>
        [Test]
        public void EveryLiveUnitHasAnAmountOfItsOwn()
        {
            var table = Read(new ContinueDto
            {
                turns = 11, motes = 12, taps = 13, moves = 14, wards = 3,
            });

            Assert.AreEqual(11, table.AmountFor(ContinueUnit.Turns));
            Assert.AreEqual(12, table.AmountFor(ContinueUnit.Motes));
            Assert.AreEqual(13, table.AmountFor(ContinueUnit.Taps));
            Assert.AreEqual(14, table.AmountFor(ContinueUnit.Moves));
            Assert.AreEqual(3, table.AmountFor(ContinueUnit.Wards));
        }

        /// <summary>
        /// The wards figure is bounded like every other, and nought is refused for the reason all
        /// of them are: a continue that raises no ward charges for a run that is still lost.
        /// </summary>
        [Test]
        public void AContinueThatRaisesNoWardIsRefused()
        {
            var problems = new List<string>();
            var table = Read(new ContinueDto { wards = 0 }, problems);

            Assert.AreEqual(ContinueLimits.DefaultWards, table.Wards);
            Assert.AreEqual(1, problems.Count, string.Join("; ", problems));
        }

        // ================================================================ the price
        /// <summary>
        /// The ladder a player is actually quoted on one run, pinned as the sequence rather
        /// than as the factor that produces it. A recurrence nobody can read off two integers
        /// is exactly the thing worth writing down once.
        /// </summary>
        [Test]
        public void TheShippedPriceDoublesWithEveryContinueBoughtOnOneRun()
        {
            var table = Read(null);

            Assert.AreEqual(20L, table.PriceFor(0));
            Assert.AreEqual(40L, table.PriceFor(1));
            Assert.AreEqual(80L, table.PriceFor(2));
            Assert.AreEqual(160L, table.PriceFor(3));
            Assert.AreEqual(320L, table.PriceFor(4));
        }

        /// <summary>
        /// Where the shipped ladder stops climbing, said out loud. It is the one reading that
        /// shows <see cref="ContinueLimits.MaxGems"/> binding - and it binds only after 5,100
        /// gems have been spent inside one lost run, which is more than the largest pack in
        /// the shop holds.
        /// </summary>
        [Test]
        public void TheShippedLadderTopsOutAtTheCeilingOnTheNinth()
        {
            var table = Read(null);

            Assert.AreEqual(2_560L, table.PriceFor(7), "the eighth is the last real rung");
            Assert.AreEqual(ContinueLimits.MaxGems, table.PriceFor(8));
            Assert.AreEqual(ContinueLimits.MaxGems, table.PriceFor(9));
        }

        /// <summary>
        /// A hundred hundredths holds the price still, which with a step is exactly the linear
        /// ladder this block shipped with. The two are one recurrence rather than two dials,
        /// and this is the case that proves the old behaviour is still reachable.
        /// </summary>
        [Test]
        public void AFactorOfAHundredWithAStepIsTheOldLinearLadder()
        {
            var table = Read(new ContinueDto { gems = 20, gemsStep = 10, gemsFactor = 100 });

            Assert.AreEqual(20L, table.PriceFor(0));
            Assert.AreEqual(30L, table.PriceFor(1));
            Assert.AreEqual(40L, table.PriceFor(2));
        }

        [Test]
        public void AFactorOfAHundredWithNoStepIsFlatHoweverManyHaveBeenBought()
        {
            var table = Read(new ContinueDto { gems = 20, gemsStep = 0, gemsFactor = 100 });

            Assert.AreEqual(20L, table.PriceFor(0));
            Assert.AreEqual(20L, table.PriceFor(1));
            Assert.AreEqual(20L, table.PriceFor(50));
        }

        /// <summary>
        /// The multiply lands before the divide, which is the same rule invariant 37bh is:
        /// taken the other way round, a tenth of 8 truncates back to 8 and a retune buys
        /// nothing. 150 hundredths of 20 is 30, and the truncation happens once per rung
        /// rather than once at the end.
        /// </summary>
        [Test]
        public void AFractionalFactorIsExactBecauseTheDivideIsLast()
        {
            var table = Read(new ContinueDto { gems = 20, gemsFactor = 150 });

            Assert.AreEqual(30L, table.PriceFor(1));
            Assert.AreEqual(45L, table.PriceFor(2));
            Assert.AreEqual(67L, table.PriceFor(3), "67.5 truncates, and does so once");
        }

        /// <summary>
        /// A price that <em>falls</em> as more are bought is the one setting that would make
        /// the fail state stop binding altogether - invariant 5d's complaint about a rule that
        /// rejects nothing, said about a price. Named and clamped to flat rather than honoured.
        /// </summary>
        [Test]
        public void AFallingPriceIsRefusedAndSaysSo()
        {
            var problems = new List<string>();
            var table = Read(new ContinueDto { gems = 20, gemsFactor = 50 }, problems);

            Assert.AreEqual(ContinueLimits.MinGemsFactor, table.GemsFactor);
            Assert.AreEqual(20L, table.PriceFor(5), "clamped to flat, never cheaper");
            Assert.AreEqual(1, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void ARunawayFactorIsClampedAndSaysSo()
        {
            var problems = new List<string>();
            var table = Read(new ContinueDto { gems = 20, gemsFactor = 100_000 }, problems);

            Assert.AreEqual(ContinueLimits.MaxGemsFactor, table.GemsFactor);
            Assert.AreEqual(1, problems.Count, string.Join("; ", problems));
        }

        /// <summary>
        /// A factor that truncates back to where it started climbs no further however many are
        /// bought - 101 hundredths of 20 is 20 in integer arithmetic. It must answer at once
        /// rather than iterate a count nothing bounds, because that count is reached over a
        /// frozen board with a defeat panel waiting on it.
        /// </summary>
        [Test]
        public void AFactorThatCannotClimbAnswersAtOnceRatherThanSpinning()
        {
            var table = Read(new ContinueDto { gems = 20, gemsFactor = 101 });

            Assert.AreEqual(20L, table.PriceFor(int.MaxValue));
        }

        [Test]
        public void AStepMakesEachFurtherContinueDearer()
        {
            var table = Read(new ContinueDto { gems = 20, gemsStep = 10, gemsFactor = 100 });

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
            var table = Read(new ContinueDto
            {
                gems = 20, gemsStep = ContinueLimits.MaxGemsStep, gemsFactor = 100
            });

            Assert.AreEqual(ContinueLimits.MaxGems, table.PriceFor(int.MaxValue));
            Assert.AreEqual(ContinueLimits.MaxGems, table.PriceFor(1_000_000));
        }

        /// <summary>
        /// The same, on the shape that ships. A geometric price reaches the ceiling far
        /// sooner, and what matters is that it stops there rather than wrapping - and that it
        /// says so without walking two billion rungs to find out.
        /// </summary>
        [Test]
        public void ADoublingPriceSaturatesRatherThanWrapping()
        {
            var table = Read(new ContinueDto
            {
                gems = 20, gemsFactor = ContinueLimits.MaxGemsFactor
            });

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
            Publish(new ContinueDto { gems = 20, turns = 15 });

            var third = RunContinue.Offer(ContinueUnit.Turns, deficit: 0, taken: 2,
                                          gemsHeld: 1_000, gemsForSale: false);

            Assert.AreEqual(80L, third.Gems, "twenty doubled twice");
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
    }
}
