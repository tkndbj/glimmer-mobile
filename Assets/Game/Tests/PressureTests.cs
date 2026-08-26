using GlimmerGrove.Content;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The move budget and fragile conduits exist for one reason: a player with
    /// unlimited, freely reversible turns has nothing at stake, so no hazard can bite.
    ///
    /// These two put a price on turns — the budget on how many, fragility on which.
    /// What follows pins the rules that keep that pressure fair: a budget that never
    /// ends a run the player was still winning, and a fragile board that can always,
    /// provably, still be solved.
    /// </summary>
    public sealed class PressureTests
    {
        static Cell[] Parse(int w, int h, string[] rows)
        {
            var result = LevelGridParser.Parse(new LevelLayout(w, h, rows));
            Assert.IsTrue(result.Ok, string.Join("; ", result.Errors));
            return result.Cells;
        }

        static Puzzle Board(int w, int h, string[] rows, LevelTuning tuning = null)
            => new Puzzle(LevelId.Parse("t_level"), w, h,
                          tuning ?? LevelTuning.Default(3), Parse(w, h, rows));

        static LevelDefinition Level(int w, int h, string[] rows)
            => new LevelDefinition(
                LevelId.Parse("t_level"), ChapterId.Parse("t_chapter"),
                new LevelLayout(w, h, rows), LevelTuning.Default(3),
                new LevelPresentation(new Vector2(.5f, .5f), null, null, null));

        static bool Has(LevelValidationReport report, LevelIssueSeverity severity, string fragment)
        {
            foreach (var issue in report.Issues)
                if (issue.Severity == severity && issue.Message.Contains(fragment)) return true;
            return false;
        }

        // ------------------------------------------------------------- move budget
        /// <summary>
        /// The three lines a run is measured against, and the property that has to hold
        /// between them at every par: <c>gold &lt; silver &lt; budget</c>.
        ///
        /// <para>
        /// <b>This replaces two earlier rules, and the history is the point.</b> It first said
        /// the budget must sit above the two-star line — right while the clock was the fail
        /// state and the budget was a backstop under somebody drumming, because ending a run
        /// that was still earning stars read as the game cheating. The clock went, the budget
        /// became the only way to lose, and that floor put the fail line past the point where
        /// the player had already stopped earning anything, so it was removed and the default
        /// dropped to 1.60. The rule was then rewritten to say the budget sits *inside* the
        /// two-star band — and that version was wrong in a way nothing caught: it made one star
        /// arithmetically unscorable, because a run still alive has spent fewer turns than the
        /// budget.
        /// </para>
        /// <para>
        /// So the property is not where the budget sits relative to any one line. It is that
        /// all three bands can be landed in: a run can be graded three, two or one, and beyond
        /// that it ends. The star lines are now even thirds of the slack the budget creates
        /// (<see cref="LevelTuning.DefaultGoldFactor"/>), which is what makes that true by
        /// construction rather than by luck.
        /// </para>
        /// <para>
        /// Tiny pars are in the list deliberately: at par 1 and 2 the ceilings collapse the
        /// bands onto each other, so the ordering is asserted with room for that rather than
        /// as a strict inequality that only holds on the pars somebody happened to think of.
        /// Real content starts at par 10 and is checked strictly below.
        /// </para>
        /// </summary>
        [Test]
        public void EveryStarBandCanBeLandedInAtEveryRealPar()
        {
            foreach (int par in new[] { 1, 2, 5, 20, 38, 46, 200 })
            {
                var tuning = LevelTuning.Default(par);

                Assert.LessOrEqual(tuning.GoldThreshold, tuning.SilverThreshold,
                    $"par {par}: three stars must not ask for more turns than two");
                Assert.LessOrEqual(tuning.SilverThreshold, tuning.MoveBudget,
                    $"par {par}: the run must not end before one star can be scored");

                // A perfect run always survives its own board, whatever the rounding does.
                Assert.GreaterOrEqual(tuning.MoveBudget, par,
                    $"par {par}: a par run must survive");
            }

            // At any par the shipped content actually uses, the three bands are strictly
            // separated and every one of them is reachable.
            foreach (int par in new[] { 10, 18, 24, 36, 50, 63, 70 })
            {
                var t = LevelTuning.Default(par);

                Assert.Less(t.GoldThreshold, t.SilverThreshold, $"par {par}");
                Assert.Less(t.SilverThreshold, t.MoveBudget, $"par {par}");

                Assert.AreEqual(3, t.StarsFor(t.GoldThreshold), $"par {par}: three is landable");
                Assert.AreEqual(2, t.StarsFor(t.SilverThreshold), $"par {par}: two is landable");
                Assert.AreEqual(1, t.StarsFor(t.MoveBudget - 1),
                    $"par {par}: one is landable — the slowest surviving run scores it");
            }
        }

        /// <summary>
        /// An authored factor is honoured exactly — there is no floor under it any more.
        /// If the clamp <c>MoveBudget</c> used to apply ever comes back, this says so.
        /// </summary>
        [Test]
        public void AnAuthoredBudgetIsNotClampedUpToTheTwoStarLine()
        {
            // par 30 at the shipped lines: three stars at 36, two at 42.
            var tuning = new LevelTuning(30, LevelTuning.DefaultGoldFactor,
                                         LevelTuning.DefaultSilverFactor, 1.10f);

            Assert.AreEqual(33, tuning.MoveBudget, "ceil(30 x 1.10), not raised to 43");
            Assert.Less(tuning.MoveBudget, tuning.SilverThreshold);
        }

        /// <summary>
        /// A threshold is integer arithmetic, and this is the case that proves it.
        ///
        /// <para>
        /// <c>1.20f</c> is 1.20000004768…, so <c>Mathf.CeilToInt(45 * 1.20f)</c> is 55 where
        /// <c>par × 1.20</c> is exactly 54 — and 61 against 60 at par 50. Both shipped: four
        /// glades granted a turn more for three stars than the design says, with the offline
        /// mirror printing the design's number and nothing comparing the two. Every par whose
        /// product lands exactly on an integer is a candidate, so the fix is hundredths in
        /// integer arithmetic rather than a nudge, and these are the pars that would catch it
        /// coming back.
        /// </para>
        /// <para>
        /// Same family as <c>WeaveGenerator</c>'s <c>1.3f</c> (see *Hard-won facts*) and worse
        /// in one way: that one differed between .NET and Mono, so a diff could find it, while
        /// this is wrong identically everywhere and only disagrees with arithmetic.
        /// </para>
        /// </summary>
        [Test]
        public void AThresholdIsExactWhereTheProductLandsOnAnInteger()
        {
            // par x 1.20 = 54.0 exactly; the float product is 54.000003814697266
            var at45 = LevelTuning.Default(45);
            Assert.AreEqual(54, at45.GoldThreshold, "par 45 x 1.20 is 54, not 55");
            Assert.AreEqual(63, at45.SilverThreshold, "par 45 x 1.40 is 63");

            var at50 = LevelTuning.Default(50);
            Assert.AreEqual(60, at50.GoldThreshold, "par 50 x 1.20 is 60, not 61");
            Assert.AreEqual(70, at50.SilverThreshold, "par 50 x 1.40 is 70");

            // A product that genuinely needs rounding up still does.
            var at49 = LevelTuning.Default(49);
            Assert.AreEqual(59, at49.GoldThreshold, "ceil(58.8)");

            // And the budget takes the same path, exact factor and all.
            var budget = new LevelTuning(45, LevelTuning.DefaultGoldFactor,
                                         LevelTuning.DefaultSilverFactor, 1.60f);
            Assert.AreEqual(72, budget.MoveBudget, "par 45 x 1.60 is 72");
        }

        /// <summary>
        /// The validator's own check on the three lines actually fires — on the exact
        /// configuration that shipped, and on the two either side of it.
        ///
        /// <para>
        /// <b>This is the test the change needed and did not have.</b> The bands were left
        /// stranded by a budget retune, every number involved stayed individually plausible,
        /// the boards validated green, and nothing said that one star had become unscorable.
        /// A rule with no failing case is not a rule — the same lesson <c>names.py</c> learned
        /// when it ran the fold on a runtime that could not disagree with it — so each branch
        /// of <see cref="LevelValidator"/>'s star-band check is driven here with a tuning that
        /// must trip it.
        /// </para>
        /// </summary>
        [Test]
        public void TheStarBandCheckCatchesAStrandedBand()
        {
            // A solvable three-tile board; the tuning is what is under test, not the grid.
            var rows = new[] { "*E#R/0 -EW/1 @W#R/0" };

            LevelValidationReport Graded(float gold, float silver, float budget)
                => LevelValidator.Validate(new LevelDefinition(
                    LevelId.Parse("t_level"), ChapterId.Parse("t_chapter"),
                    new LevelLayout(3, 1, rows), new LevelTuning(20, gold, silver, budget),
                    new LevelPresentation(new Vector2(.5f, .5f), null, null, null)));

            // What actually shipped: the budget cut to 1.60 while two stars was still 2.00, so
            // a surviving run was always inside the two-star band and one star was unscorable.
            Assert.IsTrue(Has(Graded(1.35f, 2.00f, 1.60f), LevelIssueSeverity.Warning,
                              "one star can never be scored"),
                          "the stranded one-star band must be reported");

            // Tighter still: nothing can be graded at all, which is a broken level.
            Assert.IsTrue(Has(Graded(1.35f, 2.00f, 1.30f), LevelIssueSeverity.Error,
                              "no run can be graded"));

            // Two stars asking for fewer turns than three: the middle band is empty.
            Assert.IsTrue(Has(Graded(1.60f, 1.40f, 2.00f), LevelIssueSeverity.Error,
                              "two-star band is empty"));

            // And the shipped tuning is clean, or the check is just noise.
            var ok = Graded(LevelTuning.DefaultGoldFactor, LevelTuning.DefaultSilverFactor,
                            LevelTuning.DefaultBudgetFactor);
            Assert.IsFalse(Has(ok, LevelIssueSeverity.Warning, "one star can never be scored"));
            Assert.IsFalse(Has(ok, LevelIssueSeverity.Error, "no run can be graded"));
            Assert.IsFalse(Has(ok, LevelIssueSeverity.Error, "two-star band is empty"));
        }

        /// <summary>
        /// A glade with no budget cannot strand a band, so it is never reported for one.
        /// The first glade in the game is the only one, and it would otherwise be warned
        /// about for ever.
        /// </summary>
        [Test]
        public void AnUnbudgetedGladeIsNeverReportedForAStrandedBand()
        {
            var report = LevelValidator.Validate(new LevelDefinition(
                LevelId.Parse("t_level"), ChapterId.Parse("t_chapter"),
                new LevelLayout(3, 1, new[] { "*E#R/0 -EW/1 @W#R/0" }),
                new LevelTuning(20, 1.20f, 1.40f, LevelTuning.Unlimited),
                new LevelPresentation(new Vector2(.5f, .5f), null, null, null)));

            Assert.IsFalse(Has(report, LevelIssueSeverity.Warning, "one star can never be scored"));
            Assert.IsFalse(Has(report, LevelIssueSeverity.Error, "no run can be graded"));
        }

        [Test]
        public void ANegativeFactorRemovesTheBudgetEntirely()
        {
            var tuning = new LevelTuning(10, 0f, 0f, LevelTuning.Unlimited);

            Assert.IsFalse(tuning.HasBudget);
            Assert.AreEqual(int.MaxValue, tuning.MoveBudget);
        }

        /// <summary>
        /// Zero means "not authored" everywhere else in the DTOs, and it has to mean it
        /// here too — otherwise every existing level silently loses its fail state the
        /// moment the field is added.
        /// </summary>
        [Test]
        public void AnUnwrittenFactorTakesTheDefaultRatherThanRemovingTheBudget()
        {
            var tuning = new LevelTuning(10, 0f, 0f, 0f);

            Assert.IsTrue(tuning.HasBudget);
            Assert.AreEqual(LevelTuning.DefaultBudgetFactor, tuning.BudgetFactor);
        }

        [Test]
        public void RunningOutOfTurnsEndsTheRun()
        {
            // par 1, budget forced small: 2 turns allowed
            var tuning = new LevelTuning(1, 1f, 1f, 2f);
            var board = Board(2, 1, new[] { "*E#R/1 @W#R/0" }, tuning);

            Assert.IsFalse(board.OutOfMoves);

            board.Moves = tuning.MoveBudget;
            board.Evaluate();

            Assert.IsTrue(board.OutOfMoves);
            Assert.AreEqual(0, board.MovesLeft);
        }

        /// <summary>Solving on the very last turn is a win, not a loss.</summary>
        [Test]
        public void SpendingTheLastTurnOnTheSolutionStillWins()
        {
            var tuning = new LevelTuning(1, 1f, 1f, 2f);
            var board = Board(2, 1, new[] { "*E#R/0 @W#R/0" }, tuning);

            board.Moves = tuning.MoveBudget;
            board.Evaluate();

            Assert.IsTrue(board.Won);
            Assert.IsFalse(board.OutOfMoves, "a solved board is never also out of turns");
        }


        [Test]
        public void AnUnbudgetedBoardCanNeverRunOut()
        {
            var tuning = new LevelTuning(1, 1f, 1f, LevelTuning.Unlimited);
            var board = Board(2, 1, new[] { "*E#R/1 @W#R/0" }, tuning);

            board.Moves = 100_000;
            board.Evaluate();

            Assert.IsFalse(board.OutOfMoves);
        }

        // ---------------------------------------------------------------- fragility
        [Test]
        public void AFragileConduitParsesItsTurnCount()
        {
            var cells = Parse(3, 1, new[] { "*E#R/0 -EW/0~3 @W#R/0" });

            Assert.AreEqual(3, cells[1].fragile);
            Assert.AreEqual(0, cells[0].fragile, "a plain tile is not fragile");
        }

        [Test]
        public void OnlyAConduitMayBeFragile()
        {
            var result = LevelGridParser.Parse(new LevelLayout(2, 1, new[] { "*E#R/0~2 @W#R/0" }));

            Assert.IsFalse(result.Ok);
            Assert.IsTrue(string.Join("; ", result.Errors).Contains("only a conduit"),
                          string.Join("; ", result.Errors));
        }

        /// <summary>
        /// The count is how many turns the conduit <em>survives</em>, so "~2" takes two
        /// turns safely and gives way on the third. That distinction is load-bearing:
        /// a crumble now loses the glade, and validation lets a conduit be owed exactly
        /// its whole allowance — with the count meaning anything else, the last turn of
        /// a legitimate solution would kill the run.
        /// </summary>
        [Test]
        public void AFragileConduitSurvivesItsCountAndBreaksOnTheNextTurn()
        {
            var board = Board(3, 1, new[] { "*E#R/0 -EW/0~2 @W#R/0" });

            Assert.AreEqual(2, board.FragileLeft(1));
            Assert.IsTrue(board.Used(1));

            board.Turn(1);
            Assert.AreEqual(1, board.FragileLeft(1));
            Assert.IsFalse(board.Shattered(1), "one of its two turns");

            board.Turn(1);
            Assert.AreEqual(0, board.FragileLeft(1));
            Assert.IsFalse(board.Shattered(1), "both turns spent, but it holds");

            board.Turn(1);
            Assert.IsTrue(board.Shattered(1), "the turn past its allowance breaks it");
            Assert.AreEqual(1, board.ShatteredAt);
        }

        /// <summary>
        /// The property the validator relies on: a conduit owed exactly its allowance
        /// can always reach its solution without breaking.
        /// </summary>
        [Test]
        public void AConduitOwedItsWholeAllowanceStillSurvivesSolvingIt()
        {
            // "-EW" is symmetric every half turn, so /1 owes one turn; ~1 grants one.
            var board = Board(3, 1, new[] { "*E#R/0 -EW/1~1 @W#R/0" });

            board.Turn(1);

            Assert.IsFalse(board.Shattered(1), "solving it must never be what breaks it");
            Assert.IsTrue(board.Used(1));
        }

        /// <summary>
        /// A crumbled conduit leaves a hole. If it still conducted, shattering would be
        /// cosmetic and the whole mechanic would be a lie.
        /// </summary>
        [Test]
        public void AShatteredConduitLeavesTheBoard()
        {
            var board = Board(3, 1, new[] { "*E#R/0 -EW/0~1 @W#R/0" });
            Assert.AreNotEqual(0, board.Energy(2), "the critter starts lit through the conduit");

            board.Turn(1);          // its one safe turn
            board.Turn(1);          // and the one that breaks it
            board.Evaluate();

            Assert.IsFalse(board.Used(1));
            Assert.AreEqual(0, board.Energy(2), "the light no longer reaches past the gap");
            Assert.IsFalse(board.Won);
        }

        /// <summary>
        /// Undo rewinds the rotation but never mends the conduit. If it did, exploring
        /// would be free again and fragility would stop meaning anything.
        /// </summary>
        [Test]
        public void UndoDoesNotGiveFragilityBack()
        {
            var board = Board(3, 1, new[] { "*E#R/0 -EW/0~3 @W#R/0" });

            board.Turn(1);
            Assert.AreEqual(2, board.FragileLeft(1));

            board.Turn(1, -1, wear: false);
            Assert.AreEqual(2, board.FragileLeft(1), "the turn was undone; the wear was not");
        }

        [Test]
        public void RestartingMendsEveryConduit()
        {
            var board = Board(3, 1, new[] { "*E#R/0 -EW/0~1 @W#R/0" });
            var start = board.Snapshot();

            board.Turn(1);
            board.Turn(1);
            Assert.IsTrue(board.Shattered(1));

            board.Reset(start);

            Assert.IsFalse(board.Shattered(1));
            Assert.AreEqual(1, board.FragileLeft(1));
            Assert.AreEqual(-1, board.ShatteredAt);
            Assert.IsTrue(board.Used(1));
        }

        // --------------------------------------------------------------- validation
        /// <summary>
        /// The check that keeps the mechanic honest: a conduit needing more turns than
        /// it can survive is an unwinnable level that looks entirely correct.
        /// </summary>
        [Test]
        public void AFragileConduitThatCannotReachItsSolutionIsAnError()
        {
            // The elbow starts a quarter turn past its solution, so it owes three turns
            // clockwise — and it can only survive one.
            var doomed = LevelValidator.Validate(Level(2, 2, new[]
            {
                "*ES#R/0 -SW/1~1",
                "@N#R/0  -N/0",
            }));

            Assert.IsTrue(Has(doomed, LevelIssueSeverity.Error, "would crumble on the way"), doomed.Describe());
        }

        [Test]
        public void AFragileConduitWithJustEnoughTurnsIsAccepted()
        {
            // the same board, given exactly the three turns it needs
            var fine = LevelValidator.Validate(Level(2, 2, new[]
            {
                "*ES#R/0 -SW/1~3",
                "@N#R/0  -N/0",
            }));

            Assert.IsFalse(Has(fine, LevelIssueSeverity.Error, "would crumble on the way"), fine.Describe());
        }

        [Test]
        public void AFragileConduitThatIsAlsoRootedIsAWarning()
        {
            var report = LevelValidator.Validate(Level(3, 1, new[] { "*E#R/0 -EW/0!~2 @W#R/0" }));

            Assert.IsTrue(Has(report, LevelIssueSeverity.Warning, "also rooted"), report.Describe());
        }

        [Test]
        public void EveryShippedLevelKeepsItsFragileConduitsSolvable()
        {
            var levels = SaveMigrationTests.LoadBundledLevels();
            if (levels.Count == 0) Assert.Ignore("no bundled content available in this run");

            foreach (var report in LevelValidator.ValidateAll(levels))
                Assert.IsFalse(Has(report, LevelIssueSeverity.Error, "would crumble on the way"), report.Describe());
        }
    }
}
