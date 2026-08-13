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
                new LevelPresentation(mapPos, null, null, null),
                null, null, null);
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
            // one tile is a quarter turn out, the other is already correct
            var report = LevelValidator.Validate(Level(SolvableRows));
            Assert.AreEqual(1, report.ComputedPar, report.Describe());
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

        [Test]
        public void EveryShippedLevelIsValid()
        {
            var catalog = SaveMigrationTests.LoadBundledCatalog();
            if (catalog.IsEmpty) Assert.Ignore("no bundled content available in this run");

            foreach (var report in LevelValidator.ValidateAll(catalog))
                Assert.IsFalse(report.HasErrors, report.Describe());
        }
    }
}
