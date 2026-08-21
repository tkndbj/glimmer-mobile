using GlimmerGrove.Content;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The validator is what allows content to ship every fortnight without anyone
    /// hand-playing every level. These tests check that it actually catches the
    /// failures it claims to — a validator that passes everything is worse than none,
    /// because it is trusted.
    /// </summary>
    public sealed class LevelValidatorTests
    {
        static LevelDefinition Level(string[] rows, int par = 0, Vector2 mapPos = default)
        {
            int width = rows[0].Split(' ').Length;
            var layout = new LevelLayout(width, rows.Length, rows);

            if (par <= 0)
            {
                var parsed = LevelGridParser.Parse(layout);
                par = parsed.Ok ? Mathf.Max(1, PuzzleFactory.MinimumMoves(parsed.Cells)) : 1;
            }

            return new LevelDefinition(
                LevelId.Parse("t_level"), ChapterId.Parse("t_chapter"),
                layout, LevelTuning.Default(par),
                new LevelPresentation(mapPos, null, null, null));
        }

        static bool HasError(LevelValidationReport report, string fragment)
        {
            foreach (var issue in report.Issues)
                if (issue.Severity == LevelIssueSeverity.Error && issue.Message.Contains(fragment))
                    return true;
            return false;
        }

        static bool HasWarning(LevelValidationReport report, string fragment)
        {
            foreach (var issue in report.Issues)
                if (issue.Severity == LevelIssueSeverity.Warning && issue.Message.Contains(fragment))
                    return true;
            return false;
        }

        /// <summary>A heart and a critter facing each other, one turn from solved.</summary>
        static readonly string[] SolvableRows = { "*E#R/1 @W#R/0" };

        [Test]
        public void AWellFormedLevelPasses()
        {
            var report = LevelValidator.Validate(Level(SolvableRows, mapPos: new Vector2(.5f, .5f)));
            Assert.IsFalse(report.HasErrors, report.Describe());
        }

        [Test]
        public void DanglingArmIsAnError()
        {
            // the heart reaches east, but its neighbour does not reach back
            var report = LevelValidator.Validate(Level(new[] { "*E#R/0 @N#R/0" }));
            Assert.IsTrue(HasError(report, "not mated"), report.Describe());
        }

        [Test]
        public void ArmPointingOffTheBoardIsAnError()
        {
            var report = LevelValidator.Validate(Level(new[] { "*W#R/0 @W#R/0" }));
            Assert.IsTrue(HasError(report, "off the board"), report.Describe());
        }

        [Test]
        public void ALevelWithNoHeartIsAnError()
        {
            var report = LevelValidator.Validate(Level(new[] { "-E/0 @W#R/0" }));
            Assert.IsTrue(HasError(report, "no heart-crystal"), report.Describe());
        }

        [Test]
        public void ALevelWithNoCrittersIsAnError()
        {
            var report = LevelValidator.Validate(Level(new[] { "*E#R/0 -W/0" }));
            Assert.IsTrue(HasError(report, "no sleeping critters"), report.Describe());
        }

        [Test]
        public void ACritterWantingTheWrongColourIsUnsolvable()
        {
            // red light reaching a critter that dreams of blue can never wake it
            var report = LevelValidator.Validate(Level(new[] { "*E#R/1 @W#B/0" }));
            Assert.IsTrue(HasError(report, "authored solution"), report.Describe());
        }

        [Test]
        public void AuthoredParThatDisagreesWithTheBoardWarns()
        {
            var report = LevelValidator.Validate(Level(SolvableRows, par: 99));
            Assert.IsTrue(HasWarning(report, "par is 99"), report.Describe());
            Assert.IsFalse(report.HasErrors, "a wrong par is a warning, not a broken level");
        }

        [Test]
        public void MapPositionOutsideTheStripWarns()
        {
            var report = LevelValidator.Validate(Level(SolvableRows, mapPos: new Vector2(4f, 9f)));
            Assert.IsTrue(HasWarning(report, "map position"), report.Describe());
        }

        [Test]
        public void ComputedParCountsOnlyTheTurnsActuallyOwed()
        {
            // "*E#R/1 @W#R/0": the critter is already correct and costs nothing. The
            // heart sits one quarter turn *clockwise* of its solution, and a tap only
            // ever turns clockwise — BoardView.ApplyTurn(i, 1); dir -1 exists solely for
            // Undo, which decrements the move count rather than spending one. So getting
            // it home costs three taps, not one.
            var report = LevelValidator.Validate(Level(SolvableRows));
            Assert.AreEqual(3, report.ComputedPar, report.Describe());
        }

        /// <summary>
        /// Par is taps, not angular distance, and the two differ because the board turns
        /// one way. Pinned as its own test because it is the assumption every shipped
        /// par rests on: if a counter-clockwise tap is ever added, every par in the game
        /// drops and the gold and silver thresholds move with it.
        /// </summary>
        [Test]
        public void ParIsMeasuredInClockwiseTapsNotShortestRotation()
        {
            var parsed = LevelGridParser.Parse(new LevelLayout(2, 1, new[] { "*E#R/1 @W#R/0" }));
            Assert.IsTrue(parsed.Ok, string.Join("; ", parsed.Errors));

            Assert.AreEqual(3, PuzzleFactory.MinimumMoves(parsed.Cells),
                            "one turn clockwise from solved is three taps back, not one");

            // and the far side: three turns out is one tap home
            var other = LevelGridParser.Parse(new LevelLayout(2, 1, new[] { "*E#R/3 @W#R/0" }));
            Assert.IsTrue(other.Ok, string.Join("; ", other.Errors));
            Assert.AreEqual(1, PuzzleFactory.MinimumMoves(other.Cells));
        }

        [Test]
        public void LockedAndSymmetricTilesCostNothing()
        {
            var layout = new LevelLayout(2, 1, new[] { "*E#R/1! @W#R/0" });
            var parsed = LevelGridParser.Parse(layout);

            Assert.IsTrue(parsed.Ok, string.Join("; ", parsed.Errors));
            Assert.AreEqual(0, PuzzleFactory.MinimumMoves(parsed.Cells),
                            "a rooted tile can never be turned, so it cannot cost a move");
        }

        // A board the validator now refuses on purpose - see the four cases below. It is
        // still the right fixture here, because what is under test is that MinimumMoves
        // skips a rooted tile at all, and a rooted tile at /0 owes nothing anyway.

        [Test]
        public void ARootedTileAwayFromItsSolutionIsAnError()
        {
            var report = LevelValidator.Validate(Level(new[] { "*E#R/1 -EW/1! @W#R/0" },
                                                       mapPos: new Vector2(.5f, .5f)));

            Assert.IsTrue(HasError(report, "can never be turned"), report.Describe());
        }

        [Test]
        public void ARootedTileOnItsSolutionIsFine()
        {
            var report = LevelValidator.Validate(Level(new[] { "*E#R/1 -EW/0! @W#R/0" },
                                                       mapPos: new Vector2(.5f, .5f)));

            Assert.IsFalse(report.HasErrors, report.Describe());
        }

        [Test]
        public void ARootedStraightHalfATurnRoundReadsAsSolved()
        {
            // The check asks Puzzle.Alike rather than rot == 0. A straight conduit is the
            // same conduit half a turn round, so refusing this would refuse a tile that is
            // already correct - and every rooted straight in the Mill Vale is one.
            var report = LevelValidator.Validate(Level(new[] { "*E#R/1 -EW/2! @W#R/0" },
                                                       mapPos: new Vector2(.5f, .5f)));

            Assert.IsFalse(report.HasErrors, report.Describe());
        }

        [Test]
        public void ARootedStraightCrossingIsSolvedAtEveryAngle()
        {
            // Stonebridge's shape. Turning a straight crossing swaps which strand is called
            // which and nothing on the board can tell, so no rotation of one is ever wrong.
            var report = LevelValidator.Validate(Level(new[]
            {
                ". *S#G/0 .",
                "*E#R/1 =NS+EW/1! @W#R/0",
                ". @N#G/0 .",
            }, mapPos: new Vector2(.5f, .5f)));

            Assert.IsFalse(report.HasErrors, report.Describe());
        }

        [Test]
        public void EveryShippedLevelIsValid()
        {
            var levels = SaveMigrationTests.LoadBundledLevels();
            if (levels.Count == 0) Assert.Ignore("no bundled content available in this run");

            foreach (var report in LevelValidator.ValidateAll(levels))
                Assert.IsFalse(report.HasErrors, report.Describe());
        }
    }
}
