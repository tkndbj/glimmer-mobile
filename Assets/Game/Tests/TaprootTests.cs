using GlimmerGrove.Content;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Conduits that share a taproot turn as one, which is the first rule in this game
    /// that makes a tap stop being a local act.
    ///
    /// <para>
    /// Two properties carry the whole mechanic and both are easy to break by accident.
    /// A root is <b>charged once</b> — par, the move budget and the clock are all derived
    /// from it, so counting a root per member would quietly make every bound glade twice
    /// as generous as it looks. And a root must be able to <b>reach its own solution</b>:
    /// one number of turns has to solve every conduit on it, or the level is unwinnable
    /// while looking perfectly authored, exactly like a brittle conduit owed more turns
    /// than it survives.
    /// </para>
    /// </summary>
    public sealed class TaprootTests
    {
        static Puzzle Board(string[] rows, LevelTuning tuning = null)
        {
            int width = rows[0].Split(' ').Length;
            var layout = new LevelLayout(width, rows.Length, rows);
            var parsed = LevelGridParser.Parse(layout);
            Assert.IsTrue(parsed.Ok, string.Join("; ", parsed.Errors));

            int par = Mathf.Max(1, PuzzleFactory.MinimumMoves(parsed.Cells));
            return new Puzzle(LevelId.Parse("t_level"), width, rows.Length,
                              tuning ?? LevelTuning.Default(par), parsed.Cells);
        }

        static LevelDefinition Level(string[] rows)
        {
            int width = rows[0].Split(' ').Length;
            var layout = new LevelLayout(width, rows.Length, rows);
            var parsed = LevelGridParser.Parse(layout);
            int par = parsed.Ok ? Mathf.Max(1, PuzzleFactory.MinimumMoves(parsed.Cells)) : 1;

            return new LevelDefinition(
                LevelId.Parse("t_level"), ChapterId.Parse("t_chapter"),
                layout, LevelTuning.Default(par),
                new LevelPresentation(new Vector2(.5f, .5f), null, null, null));
        }

        static bool HasError(LevelValidationReport report, string fragment)
        {
            foreach (var issue in report.Issues)
                if (issue.Severity == LevelIssueSeverity.Error && issue.Message.Contains(fragment))
                    return true;
            return false;
        }

        /// <summary>
        /// A crystal, two elbows on one root, and a critter. Both elbows start a quarter
        /// turn out, so one tap on either finishes the glade.
        ///
        ///   *E  -EW&A  -EW&A  @W
        /// </summary>
        static readonly string[] OneTapRows = { "*E#R/0 -EW/1&A -EW/1&A @W#R/0" };

        // ------------------------------------------------------------- turning
        [Test]
        public void TurningOneConduitTurnsTheWholeRoot()
        {
            var board = Board(OneTapRows);

            byte before = board.C[2].rot;
            board.Turn(1);

            Assert.AreNotEqual(before, board.C[2].rot,
                               "the partner did not move, so the root is not bound");
            Assert.AreEqual(board.C[1].rot, board.C[2].rot,
                            "both conduits started level and must stay level");
        }

        [Test]
        public void TurningTheOtherEndTurnsItToo()
        {
            var board = Board(OneTapRows);
            board.Turn(2);
            board.Evaluate();

            Assert.IsTrue(board.Won, "either end of a root drives the whole root");
        }

        [Test]
        public void UndoRewindsTheWholeRoot()
        {
            var board = Board(OneTapRows);
            byte a = board.C[1].rot, b = board.C[2].rot;

            board.Turn(1);
            board.Turn(1, -1, wear: false);

            Assert.AreEqual(a, board.C[1].rot);
            Assert.AreEqual(b, board.C[2].rot, "an undo has to unwind every conduit the turn moved");
        }

        // ---------------------------------------------------------------- par
        [Test]
        public void ARootIsChargedOnceRatherThanPerConduit()
        {
            var bound = Board(OneTapRows);
            var loose = Board(new[] { "*E#R/0 -EW/1 -EW/1 @W#R/0" });

            Assert.AreEqual(1, bound.Par, "one tap solves both, so par is one");
            Assert.AreEqual(2, loose.Par, "the same board unbound costs a tap each");
        }

        [Test]
        public void ParAgreesWithWhatItActuallyTakesToSolve()
        {
            var board = Board(OneTapRows);
            int par = board.Par;

            for (int k = 0; k < par; k++) board.Turn(1);
            board.Evaluate();

            Assert.IsTrue(board.Won, $"par said {par} taps and the board is not solved");
        }

        /// <summary>
        /// A straight conduit reads the same every half turn, so it is solved at two of
        /// the four offsets and simply goes along with whatever the elbows on its root
        /// demand. That is what makes the root's count something other than the largest of
        /// its members' — and it is the case a naive implementation gets wrong.
        /// </summary>
        [Test]
        public void AStraightConduitOnARootFollowsTheElbow()
        {
            // The junction carrying the light needs three turns; the straight below it is
            // solved at two of the four offsets and goes along with whatever the junction
            // asks for. Taking the largest of the two counts, or the first, gets this wrong.
            var board = Board(new[]
            {
                "*S#R/0 . .",
                "-NES/1&A -EW/0 @W#R/0",
                "-NS/1&A . .",
                "-N/0 . .",
            });

            Assert.AreEqual(3, board.TurnsOwed(3), "the junction decides the root's count");
            Assert.AreEqual(3, board.TurnsOwed(6), "and the straight is quoted the same number");
            Assert.AreEqual(1, board.TurnsOwedAlone(6), "on its own it would only owe one");

            for (int k = 0; k < 3; k++) board.Turn(3);
            board.Evaluate();
            Assert.IsTrue(board.Won);
        }

        // ------------------------------------------------------- the near miss
        [Test]
        public void TheNearMissCountsARootOnceAndStaysAnUpperBound()
        {
            var board = Board(OneTapRows);

            Assert.AreEqual(1, board.TurnsToSolution,
                            "two conduits, one tap: quoting two would overstate the distance");

            board.Turn(1);
            board.Evaluate();
            Assert.IsTrue(board.Won, "so one turn really does finish it");
        }

        // ------------------------------------------------------------ refusals
        [Test]
        public void ARootThatCanNeverAgreeIsAnError()
        {
            // one elbow owed one turn, the other owed two: no single count solves both
            var report = LevelValidator.Validate(Level(new[]
            {
                "*E#R/0 -EW/1&A -EW/2&A @W#R/0",
            }));

            Assert.IsTrue(HasError(report, "can never all be right at once"), report.Describe());
        }

        [Test]
        public void ARootOfOneIsAnError()
        {
            var report = LevelValidator.Validate(Level(new[] { "*E#R/0 -EW/1&A @W#R/0" }));

            Assert.IsTrue(HasError(report, "binds nothing"), report.Describe());
        }

        /// <summary>
        /// The pip limit is an authoring limit, so it has to be *said*. The mark clamps at
        /// <see cref="Puzzle.MaxReadableRunes"/>, which means a seventh root would be drawn
        /// wearing the sixth's identity — a binding the player can read that is not there.
        /// A silent clamp is the failure this project has already been bitten by twice.
        /// </summary>
        [Test]
        public void MoreRootsThanAMarkCanTellApartIsWarnedAbout()
        {
            // One long run: crystal, then a pair of conduits per root, then the critter.
            var row = new System.Text.StringBuilder("*E#R/0");
            for (int r = 0; r <= Puzzle.MaxReadableRunes; r++)      // one root too many
            {
                char rune = (char)('A' + r);
                row.Append($" -EW/1&{rune} -EW/1&{rune}");
            }
            row.Append(" @W#R/0");

            var report = LevelValidator.Validate(Level(new[] { row.ToString() }));

            Assert.IsFalse(report.HasErrors, "the board itself is sound; only the marks are"
                                             + "\n" + report.Describe());

            bool warned = false;
            foreach (var issue in report.Issues)
                if (issue.Severity == LevelIssueSeverity.Warning &&
                    issue.Message.Contains("taproots but a mark")) warned = true;

            Assert.IsTrue(warned, "a seventh root has to be reported, not quietly redrawn as the sixth\n"
                                  + report.Describe());
        }

        [Test]
        public void AWellFormedRootPasses()
        {
            var report = LevelValidator.Validate(Level(OneTapRows));
            Assert.IsFalse(report.HasErrors, report.Describe());
        }

        [Test]
        public void ARootedConduitCannotAlsoBeBound()
        {
            var parsed = LevelGridParser.Parse(new LevelLayout(4, 1,
                new[] { "*E#R/0 -EW/1!&A -EW/1&A @W#R/0" }));

            Assert.IsFalse(parsed.Ok, "one says never turn it, the other says its partner does");
        }

        [Test]
        public void ABrittleConduitCannotAlsoBeBound()
        {
            var parsed = LevelGridParser.Parse(new LevelLayout(4, 1,
                new[] { "*E#R/0 -EW/1~2&A -EW/1&A @W#R/0" }));

            Assert.IsFalse(parsed.Ok, "one tap would crumble several and only one can be reported");
        }

        [Test]
        public void OnlyAConduitCanCarryARune()
        {
            var parsed = LevelGridParser.Parse(new LevelLayout(3, 1,
                new[] { "*E#R/0 -EW/1&A @W#R/0&A" }));

            Assert.IsFalse(parsed.Ok, "a critter turning by remote control shows nothing on the board");
        }

        // ------------------------------------------------------------- tapping
        /// <summary>
        /// A crossroads reads the same at every angle, so on its own it refuses a tap. On a
        /// root worth turning it must not: the player would be poking a tile that visibly
        /// moves its partners for everybody else.
        /// </summary>
        [Test]
        public void ACrossroadsOnALiveRootIsStillWorthTapping()
        {
            var board = Board(new[]
            {
                ". -S/0 . .",
                "*E#R/0 -NESW/0&A -EW/1&A @W#R/0",
                ". -N/0 . .",
            });

            Assert.IsTrue(board.InertAlone(5), "the crossroads alone is the same at every angle");
            Assert.IsFalse(board.Inert(5), "but its root is not, so the tap has to be accepted");
            Assert.IsTrue(board.CanTurn(5));
        }
    }
}
