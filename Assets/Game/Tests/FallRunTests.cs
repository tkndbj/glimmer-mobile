using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Everything about a Lightfall run that is arithmetic: the supply it is dealt, how it is
    /// read against the well, how few drops could have emptied it, and how long the animation
    /// of all that is allowed to take.
    ///
    /// <para>
    /// Split from the board's own rules for <c>WeaveRun</c>'s reason. All of this lived inside
    /// <c>FallBoard</c> once, and one change turned that class into a puzzle model with an
    /// economy and a fail state in it — after which none of the three could be proved without
    /// building a well. Every case here is integers.
    /// </para>
    /// </summary>
    public sealed class FallRunTests
    {
        static FallLayout Layout(string deal, params string[] rows)
        {
            Assert.IsTrue(FallDeal.TryParse(deal, out var procession, out string dealError),
                          dealError);

            int width = rows[0].Replace(" ", "").Length;
            Assert.IsTrue(FallLayout.TryReadRows(rows, width, rows.Length, out var fill,
                                                 out string fillError), fillError);

            return new FallLayout(width, rows.Length, fill, procession);
        }

        // ------------------------------------------------------------------ the procession
        [Test]
        public void ADealRepeats()
        {
            Assert.IsTrue(FallDeal.TryParse("RGB", out var deal, out _));

            Assert.AreEqual(Energy.R, deal.At(0));
            Assert.AreEqual(Energy.B, deal.At(2));
            Assert.AreEqual(Energy.R, deal.At(3), "it comes round again");
            Assert.AreEqual(Energy.G, deal.At(31));
        }

        [Test]
        public void ADealRefusesABlend()
        {
            Assert.IsFalse(FallDeal.TryParse("RGY", out _, out string why),
                           "dealing yellow would hand over a step of the cooking for free");
            StringAssert.Contains("blend", why);
        }

        [Test]
        public void AFillMayNotStartWhite()
        {
            Assert.IsFalse(FallLayout.TryReadRows(new[] { "....", "W..." }, 4, 2, out _,
                                                  out string why),
                           "a well that bursts before anybody touches it is one whose author " +
                           "meant something else");
            StringAssert.Contains("white", why);
        }

        [Test]
        public void HeadroomIsCountedFromTheBrimRatherThanFromTheTop()
        {
            var layout = Layout("RGB",
                                ".....",
                                ".....",
                                ".....",
                                "..R..",
                                "..R..",
                                "R.R.R");

            Assert.AreEqual(2, layout.Headroom,
                            "row nought is the brim, so a tallest column reaching row three " +
                            "leaves exactly two rows that may be spent");
        }

        // ------------------------------------------------------------------ the supply
        [Test]
        public void TheSupplyIsTakenOnceForEachLandedDrop()
        {
            var run = new FallRun(Layout("RGB", ".....", ".....", ".....", ".....", ".....", "..Y.."), 6);

            Assert.AreEqual(6, run.Supply.Left);

            run.Drop(2);

            Assert.AreEqual(1, run.Supply.Spent);
            Assert.AreEqual(5, run.Supply.Left);
        }

        [Test]
        public void ADropThatCouldNotBeMadeCostsNothing()
        {
            // Red into a column of red: it adds nothing and there is no room above it, so the
            // column refuses. A run charged for a tap it could not honour quietly costs a mote
            // for touching a full column.
            var run = new FallRun(Layout("RGB", "R...", "R...", "R...", "R...", "R...", "R..."), 6);

            Assert.IsNull(run.Drop(0));
            Assert.AreEqual(0, run.Supply.Spent);
        }

        [Test]
        public void APressureReadingIsAFractionOfThisWellsOwnSupply()
        {
            var supply = new FallSupply(12);

            Assert.AreEqual(FallPressure.Easy, supply.Pressure);

            for (int i = 0; i < 8; i++) supply.Take();
            Assert.AreEqual(FallPressure.Low, supply.Pressure, "a third left");

            for (int i = 0; i < 2; i++) supply.Take();
            Assert.AreEqual(FallPressure.Critical, supply.Pressure, "a sixth left");
        }

        [Test]
        public void AnUnboundedSupplyIsNeverUnderPressureAndNeverRunsOut()
        {
            var supply = new FallSupply(FallSupply.Unlimited);

            for (int i = 0; i < 500; i++) supply.Take();

            Assert.IsTrue(supply.Any);
            Assert.AreEqual(FallPressure.Easy, supply.Pressure);
        }

        [Test]
        public void AGrantRaisesWhatWasDealtAndNeverWhatWasSpent()
        {
            var supply = new FallSupply(4);
            supply.Take();
            supply.Take();

            supply.Grant(6);

            Assert.AreEqual(10, supply.Dealt);
            Assert.AreEqual(2, supply.Spent,
                            "spent motes are the grade, so a bought run scores what it spent");
            Assert.AreEqual(8, supply.Left);
        }

        // ------------------------------------------------------------------ the verdict
        /// <summary>
        /// A full column has one way out and it is never the brim: a colour its top mote lacks
        /// is taken by that mote rather than resting above it, so the escape from a well that is
        /// one drop from flooding is to finish what is standing there.
        /// </summary>
        [Test]
        public void AnEmptiedWellIsWonHoweverCloseToTheBrimItGot()
        {
            var run = new FallRun(Layout("B", "....", "Y...", "Y...", "Y...", "Y...", "Y..."), 4);

            Assert.AreEqual(0, run.Board.Headroom, "there is no room left at all");

            run.Drop(0);

            Assert.IsTrue(run.Board.IsEmpty, "the chain ran the whole column");
            Assert.AreEqual(FallEnding.Emptied, run.Verdict.Ending);
        }

        [Test]
        public void AFloodedWellIsNeverSoldAContinue()
        {
            // Magenta wants green, and green is coming - so this well is playable, and the run
            // ends because the player spent the red on the one column that had no room for it.
            var run = new FallRun(Layout("RG", "....", "M...", "M...", "M...", "M...", "M..."), 6);

            Assert.IsTrue(run.Board.AtBrim(Energy.R, 0), "the ghost warns first");

            run.Drop(0);

            Assert.AreEqual(FallEnding.Flooded, run.Verdict.Ending);
            Assert.AreEqual(RunContinueDeficit.None, run.Verdict.Deficit,
                            "more motes do not empty a well that has already reached its brim");
        }

        [Test]
        public void TheTwoDeficitConstantsAgree()
        {
            Assert.AreEqual(RunContinue.NoContinue, RunContinueDeficit.None,
                            "the board layer answers the continue's question, so the two words " +
                            "for 'nothing would help' have to be one number");
        }

        [Test]
        public void AWellWhoseSupplyHasRunOutIsStarved()
        {
            var run = new FallRun(Layout("RGB", ".....", ".....", ".....", ".....", ".....", "..Y.."), 1);

            run.Drop(0);                                    // red onto bare ground: a wasted mote

            Assert.AreEqual(FallEnding.Starved, run.Verdict.Ending);
            Assert.GreaterOrEqual(run.Verdict.Deficit, 1, "and a mote would help");
        }

        /// <summary>
        /// The sound half of the fail state, and the reason it is a lower bound rather than a
        /// guess: every channel any mote ever gains comes from a drop, so one missing a colour
        /// the remaining supply cannot deliver can never be finished by anybody.
        /// </summary>
        [Test]
        public void AWellIsStarvedWhileMotesRemainIfTheColourTheyWantHasGoneOutOfTheProcession()
        {
            // A yellow wants blue. Two motes are dealt and both of them are the wrong colour.
            var run = new FallRun(Layout("RGB", ".....", ".....", ".....", ".....", ".....", "Y...."), 2);

            Assert.AreEqual(2, run.Supply.Left, "there are motes still to come");
            Assert.AreEqual(FallEnding.Starved, run.Verdict.Ending,
                            "and they are red and green, which yellow already holds");

            Assert.AreEqual(1, run.Verdict.Deficit,
                            "one more drop brings the procession round to blue");
        }

        [Test]
        public void ARunIsOnlyEverEndedOnce()
        {
            var still = new FallRun(Layout("RG", "....", "M...", "M...", "M...", "M...", "M..."), 6);

            Assert.IsFalse(still.Verdict.EndsTheRun(live: true, committed: true),
                           "this well has not flooded yet");

            var run = new FallRun(Layout("RG", "....", "M...", "M...", "M...", "M...", "M..."), 6);
            run.Drop(0);

            Assert.AreEqual(FallEnding.Flooded, run.Verdict.Ending);
            Assert.IsTrue(run.Verdict.EndsTheRun(live: true, committed: true));
            Assert.IsFalse(run.Verdict.EndsTheRun(live: false, committed: true),
                           "a run already decided is not decided again - that is two hearts " +
                           "for one loss");
            Assert.IsFalse(run.Verdict.EndsTheRun(live: true, committed: false),
                           "and a board nobody has touched is not charged for");
        }

        // ------------------------------------------------------------------ the search
        [Test]
        public void ParIsTheFewestDropsThatEmptyTheWell()
        {
            var layout = Layout("B", ".....", ".....", ".....", ".....", ".....", "YYYYY");
            var survey = FallSolver.Survey(layout);

            Assert.IsTrue(survey.Proved);
            Assert.AreEqual(1, survey.Par, "one blue anywhere in the row chains through all five");
            Assert.AreEqual(5, survey.Ways, "and any of the five columns does it");
        }

        /// <summary>
        /// Par counts drops rather than bursts, and a chain only reaches what it touches. Two
        /// blobs the wash cannot cross between are two drops however big either one is.
        /// </summary>
        [Test]
        public void ADropClearsWhatItsChainReachesAndNothingElse()
        {
            var layout = Layout("B", "......", "......", "......", "......", "......", "Y....Y");
            var survey = FallSolver.Survey(layout);

            Assert.IsTrue(survey.Proved);
            Assert.AreEqual(2, survey.Par, "one blue each, and neither reaches the other");
            Assert.AreEqual(2, survey.Ways,
                            "either order, and every other column would leave a mote behind - " +
                            "there is no such thing as a harmless drop here, because every one " +
                            "of them either puts something in the well or changes something " +
                            "already in it");
        }

        [Test]
        public void AWellNobodyCanEmptyIsProvedRatherThanTimedOut()
        {
            // Nothing here wants red, and red is all there is.
            var layout = Layout("R", "....", "....", "....", "....", "....", "M..C");
            var survey = FallSolver.Survey(layout);

            Assert.IsTrue(survey.Proved, "the search finished");
            Assert.IsFalse(survey.IsSolvable, "and there is no answer");
        }

        [Test]
        public void EveryShippedWellHasToBeProvableInsideWhatAPhoneWillSpend()
        {
            Assert.Greater(FallSolver.NodeBudget, 0);
            Assert.LessOrEqual(FallSolver.MaxDrops, 32,
                               "a well needing more drops than this is not a hard level, it is " +
                               "a level whose author has lost control of it");
        }

        // ------------------------------------------------------------------ the two copies
        /// <summary>
        /// The shapes <c>fall-vectors.json</c> is about, written out here as well.
        ///
        /// <para>
        /// <b>Two copies of the numbers on purpose, and it is <c>BriarTests</c>' bargain.</b>
        /// The vector file is the contract between the shipping rule and
        /// <c>Tools/verify/fall.py</c>, and <c>FallVectorTests</c> is what proves the C# side of
        /// it — but that needs the Editor, because <c>JsonUtility</c> is a native call. These run
        /// on every offline compile, so the rule is checked without anybody opening Unity, and a
        /// green run there means all of it agrees.
        /// </para>
        /// <para>
        /// Every case is one of the places a loose transcription reads plausibly and answers
        /// differently: the chain, the wall a chain cannot cross, the wash reaching under a
        /// stack, a pure mote finished by two bursts, a board proved unsolvable rather than
        /// timed out, and the brim excluding a route.
        /// </para>
        /// </summary>
        [Test]
        public void TheSearchAgreesWithTheOfflineMirrorOnEveryShapeItIsAbout()
        {
            Measured("one blob, one drop", "B", 1, 5, 1,
                     "......", "......", "......", "......", "......", "YYYYY.");

            Measured("a wall of the wrong colour", "BR", 3, 8, 3,
                     "......", "......", "......", "......", "......", "YYCYY.");

            Measured("two blobs the wash cannot cross between", "B", 2, 2, 2,
                     "......", "......", "......", "......", "......", "Y....Y");

            Measured("a burst under a stack", "BG", 2, 1, 2,
                     "......", "......", "..M...", "..M...", "..M...", ".YY...");

            Measured("a pure mote finished by two bursts", "BG", 2, 3, 2,
                     "......", "......", "......", "......", ".YRM..", ".YYMM.");

            Measured("a column with no room and a procession that opens badly", "RGB", 3, 2, 3,
                     "....", "Y...", "Y...", "Y...", "Y...", "Y...");

            Measured("a well emptied from the top down", "BR", 2, 3, 2,
                     ".....", ".....", ".....", "..Y..", ".YYY.", "YYCYY");

            Measured("a shipped shape", "RBGRGBB", 5, 2, 6,
                     ".....", ".....", ".....", ".....", "M.C.G", "MCCBY", "MMBYY");
        }

        [Test]
        public void ABoardNothingWantsIsProvedUnsolvableRatherThanTimedOut()
        {
            var survey = FallSolver.Survey(
                Layout("R", "....", "....", "....", "....", "....", "M..C"));

            Assert.IsTrue(survey.Proved);
            Assert.AreEqual(-1, survey.Par);
        }

        static void Measured(string name, string deal, int par, int ways, int greedy,
                             params string[] rows)
        {
            var survey = FallSolver.Survey(Layout(deal, rows));

            Assert.IsTrue(survey.Proved, name + ": not proved");
            Assert.AreEqual(par, survey.Par, name + ": par");
            Assert.AreEqual(ways, survey.Ways, name + ": ways");
            Assert.AreEqual(greedy, survey.Greedy, name + ": greedy");
        }

        // ------------------------------------------------------------------ the room to err
        /// <summary>
        /// A well's budget is par plus a count of wasted drops, not a multiple of par — and the
        /// difference is a bug that reached a player.
        ///
        /// <para>
        /// A wrong drop here is permanent <em>and</em> makes the board worse: the wasted mote
        /// lands in the well and has to be cooked to white like everything else, so one mistake
        /// costs about two drops rather than one. Against <c>par x 1.60</c> that left the second
        /// level of the chapter with two drops of room — exactly one mistake — and it was
        /// reported from play as "one wrong fall and it says out of turns".
        /// </para>
        /// </summary>
        [Test]
        public void AWellsRoomIsCountedInDropsRatherThanScaledWithPar()
        {
            var small = new LevelTuning(() => 2, 0f, 0f, 0f, FallRules.DefaultSpare);
            var large = new LevelTuning(() => 6, 0f, 0f, 0f, FallRules.DefaultSpare);

            Assert.AreEqual(2 + FallRules.DefaultSpare, small.MoveBudget);
            Assert.AreEqual(6 + FallRules.DefaultSpare, large.MoveBudget);

            Assert.AreEqual(small.MoveBudget - small.Par, large.MoveBudget - large.Par,
                            "a mistake costs the same on a short well as on a long one, so the " +
                            "room has to be the same too");
        }

        [Test]
        public void EveryOtherModeStillTakesItsBudgetFromPar()
        {
            var glade = new LevelTuning(30, 0f, 0f, 0f);

            Assert.AreEqual(0, glade.Slack, "no slack means the factor decides, as it always did");
            Assert.AreEqual(48, glade.MoveBudget, "par 30 at the default 1.60");
        }

        /// <summary>
        /// Two ways to say one thing is how they come to disagree, so the one that does nothing
        /// has to be findable. See <c>LevelTuning.BudgetFactorIsIgnored</c>.
        /// </summary>
        [Test]
        public void AFactorWrittenBesideASlackIsReportedRatherThanQuietlyOverruled()
        {
            var honest = new LevelTuning(() => 4, 0f, 0f, 0f, FallRules.DefaultSpare);
            Assert.IsFalse(honest.BudgetFactorIsIgnored, "nothing was authored, so nothing is lost");

            var muddled = new LevelTuning(() => 4, 0f, 0f, 2.5f, FallRules.DefaultSpare);
            Assert.AreEqual(4 + FallRules.DefaultSpare, muddled.MoveBudget, "the slack wins");
            Assert.IsTrue(muddled.BudgetFactorIsIgnored,
                          "and the factor that lost is a number somebody would believe");

            var unlosable = new LevelTuning(() => 4, 0f, 0f, LevelTuning.Unlimited,
                                            FallRules.DefaultSpare);
            Assert.IsFalse(unlosable.BudgetFactorIsIgnored,
                           "a negative factor is not an override - it turns the budget off, " +
                           "which no amount of slack can express");
        }

        [Test]
        public void AWellWithNoBudgetIsStillUnlosableHoweverMuchRoomItWasGiven()
        {
            var free = new LevelTuning(() => 4, 0f, 0f, LevelTuning.Unlimited,
                                       FallRules.DefaultSpare);

            Assert.IsFalse(free.HasBudget);
            Assert.AreEqual(int.MaxValue, free.MoveBudget,
                            "the first well in the game cannot be lost, and room to err must " +
                            "not quietly give it a fail state");
        }

        // ------------------------------------------------------------------ the tempo
        /// <summary>
        /// The board is latched for exactly as long as a cascade takes, so an unbounded cascade
        /// is an unbounded freeze. The rate gives way instead.
        /// </summary>
        [Test]
        public void ACascadeIsBoundedHoweverFarTheChainRuns()
        {
            for (int waves = 1; waves <= 120; waves++)
            {
                Assert.LessOrEqual(FallTempo.Cascade(waves), FallTempo.Ceiling + .0001f,
                                   waves + " waves outstayed the ceiling");
                Assert.Greater(FallTempo.Wave(waves), 0f);
            }
        }

        [Test]
        public void ALongerChainPlaysFasterRatherThanLonger()
        {
            float six = FallTempo.Wave(6);
            float twenty = FallTempo.Wave(20);

            Assert.Less(twenty, six);
            Assert.AreEqual(FallTempo.Cascade(20), FallTempo.Wave(20) * 20, .0005f);
        }

        [Test]
        public void TheThreeBeatsOfAWaveAddUpToIt()
        {
            Assert.AreEqual(1f, FallTempo.FlashShare + FallTempo.BurstShare + FallTempo.SettleShare,
                            .0001f);
        }

        [Test]
        public void AFallIsClampedAtBothEndsHoweverDeepTheWell()
        {
            Assert.AreEqual(FallTempo.MinFall, FallTempo.Fall(0), .0001f);
            Assert.AreEqual(FallTempo.MaxFall, FallTempo.Fall(200), .0001f);
            Assert.LessOrEqual(FallTempo.Fall(14), FallTempo.MaxFall);
        }

        [Test]
        public void TheEntranceFillsFromTheBottomAndFinishesInsideItself()
        {
            Assert.Greater(FallTempo.EntranceDelay(0, 10), FallTempo.EntranceDelay(9, 10),
                           "the bottom row arrives first");
            Assert.AreEqual(0f, FallTempo.EntranceDelay(9, 10), .0001f);
            Assert.Less(FallTempo.EntranceDelay(0, 10), FallTempo.Entrance);
        }

        // ------------------------------------------------------------------ the run
        [Test]
        public void TheLongestChainIsRememberedForTheReadout()
        {
            var run = new FallRun(Layout("B", ".....", ".....", ".....", ".....", ".....", "YYYYY"), 4);

            run.Drop(2);

            Assert.AreEqual(3, run.Best);
        }

        [Test]
        public void AGrantLeavesTheGradeExactlyWhereItWas()
        {
            var run = new FallRun(Layout("RGB", ".....", ".....", ".....", ".....", ".....", "..Y.."), 2);

            run.Drop(0);
            int drops = run.Drops;

            run.Grant(6);

            Assert.AreEqual(drops, run.Drops,
                            "a bought run scores what it spent, so it can only ever be worth " +
                            "one star");
        }
    }
}
