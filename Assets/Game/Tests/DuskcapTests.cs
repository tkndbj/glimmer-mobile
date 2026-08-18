using GlimmerGrove.Content;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// A duskcap is the first thing on a board the player is trying <em>not</em> to reach.
    ///
    /// <para>
    /// That makes it one term in <see cref="Puzzle.Won"/> and nothing else — no second
    /// graph, no second traversal, no new field in the save file. These tests pin the two
    /// halves that are easy to get backwards: waking one has to <b>stop</b> a glade that is
    /// otherwise finished, and it has to stop being a problem the moment the light is taken
    /// back, because a mistake nobody can undo would make the mechanic a trap rather than a
    /// puzzle.
    /// </para>
    /// <para>
    /// Note what the arms-mate rule already implies, because it shapes every board here:
    /// in the authored solution every arm points at a neighbour pointing back, so a lit
    /// cell's neighbours are lit too. A duskcap's conduits therefore have to be their own
    /// little island of dark, and the only way to wake one is to rotate something out of
    /// its solved orientation — which is exactly what a player does while solving.
    /// </para>
    /// </summary>
    public sealed class DuskcapTests
    {
        static Puzzle Board(string[] rows)
        {
            int width = rows[0].Split(' ').Length;
            var layout = new LevelLayout(width, rows.Length, rows);
            var parsed = LevelGridParser.Parse(layout);
            Assert.IsTrue(parsed.Ok, string.Join("; ", parsed.Errors));

            int par = Mathf.Max(1, PuzzleFactory.MinimumMoves(parsed.Cells));
            return new Puzzle(LevelId.Parse("t_level"), width, rows.Length,
                              LevelTuning.Default(par), parsed.Cells);
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
        /// A crystal feeding a critter through a loop, and a duskcap asleep on its own dark
        /// island below. The loop is the point: it means one conduit can be swung down into
        /// the dark without the critter going out, which is the only interesting case.
        ///
        /// <code>
        ///   *ES  -ESW  @W        indices 0 1 2
        ///   -NE  -NW    .              3 4 5
        ///   xE   -W     .              6 7 8
        /// </code>
        /// </summary>
        static readonly string[] Rows =
        {
            "*ES#R/0 -ESW/0 @W#R/0",
            "-NE/0 -NW/0 .",
            "xE/0 -W/0 .",
        };

        /// <summary>Swings the duskcap's arm up and the conduit above it down to meet it.</summary>
        static void SpillTheLight(Puzzle board)
        {
            for (int k = 0; k < 3; k++) board.Turn(6);   // the duskcap's own arm: E -> N
            board.Turn(3);                               // the loop's left side: NE -> ES
            board.Evaluate();
        }

        [Test]
        public void TheAuthoredSolutionLeavesTheDuskcapAsleep()
        {
            var board = Board(Rows);

            Assert.AreEqual(1, board.DuskcapCount);
            Assert.AreEqual(0, board.DuskcapsWoken);
            Assert.IsTrue(board.Won, "every critter is lit and nothing was disturbed");
        }

        [Test]
        public void AWokenDuskcapStopsAGladeThatIsOtherwiseFinished()
        {
            var board = Board(Rows);
            SpillTheLight(board);

            Assert.AreEqual(board.LampCount, board.LampsLit, "the critter is still awake");
            Assert.AreEqual(1, board.DuskcapsWoken);
            Assert.IsFalse(board.Won,
                           "light spilled where it was not wanted, so the glade is not settled");
        }

        [Test]
        public void TakingTheLightBackSettlesItAgain()
        {
            var board = Board(Rows);
            SpillTheLight(board);
            Assert.IsFalse(board.Won);

            for (int k = 0; k < 3; k++) board.Turn(3);   // put the loop back
            board.Evaluate();

            Assert.AreEqual(0, board.DuskcapsWoken);
            Assert.IsTrue(board.Won,
                          "waking one is a mistake to undo, never a run that cannot be saved");
        }

        [Test]
        public void WokenReportsTheDuskcapAndNothingElse()
        {
            var board = Board(Rows);
            SpillTheLight(board);

            Assert.IsTrue(board.Woken(6));
            Assert.IsFalse(board.Woken(2), "a lit critter is not a woken duskcap");
            Assert.IsFalse(board.Woken(0));
        }

        // ------------------------------------------------------------ refusals
        [Test]
        public void ASolutionThatWakesADuskcapIsAnError()
        {
            // The duskcap hangs straight off the lit junction, so it is awake at rot 0 —
            // the whole level is unwinnable and every other check passes it.
            var report = LevelValidator.Validate(Level(new[]
            {
                "*E#R/0 -ESW/0 @W#R/0",
                ". xN/0 .",
            }));

            Assert.IsTrue(HasError(report, "duskcap"), report.Describe());
        }

        [Test]
        public void ADuskcapTakesNoColour()
        {
            var parsed = LevelGridParser.Parse(new LevelLayout(3, 1,
                new[] { "*E#R/0 xEW#R/0 @W#R/0" }));

            Assert.IsFalse(parsed.Ok, "any light at all wakes one, so a colour would mean nothing");
        }

        [Test]
        public void ADuskcapDoesNotCountAsACritter()
        {
            // Nothing here can ever be won: there is no critter to wake at all.
            var report = LevelValidator.Validate(Level(new[] { "*E#R/0 xW/0" }));

            Assert.IsTrue(HasError(report, "no sleeping critters"), report.Describe());
        }
    }
}
