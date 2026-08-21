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
        /// The budget must sit above the one-star line, or a run would end while it was
        /// still earning stars — which reads to a player as the game cheating.
        /// </summary>
        [Test]
        public void TheBudgetIsAlwaysLooserThanTheOneStarThreshold()
        {
            foreach (int par in new[] { 1, 2, 5, 20, 38, 46, 200 })
            {
                var tuning = LevelTuning.Default(par);
                Assert.Greater(tuning.MoveBudget, tuning.SilverThreshold,
                               $"par {par}: a budget at or below silver ends winnable runs");
            }
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
