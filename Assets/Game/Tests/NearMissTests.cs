using GlimmerGrove.Content;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The near-miss claim, and the reason it is allowed to be made.
    ///
    /// <para>
    /// A defeat screen that says "one turn from it" is the strongest single sentence in
    /// the game for getting a player to try again, and it works on a mechanism that is
    /// easy to abuse: a loss that registers as nearly a win is retried far more often than
    /// a plain one. The line is therefore only worth having while it is <em>true</em> —
    /// the moment a player restarts, counts, and finds they were four turns away, the
    /// sentence stops being information and becomes a tell.
    /// </para>
    /// <para>
    /// So what is pinned here is not the wording but the arithmetic behind it:
    /// <see cref="Puzzle.TurnsToSolution"/> is an upper bound (if it says one, one turn
    /// really does finish the glade), it ignores conduits the solution never needed, and
    /// it refuses to answer at all once the board it measures against has been broken.
    /// </para>
    /// </summary>
    public sealed class NearMissTests
    {
        static Cell[] Parse(int w, int h, string[] rows)
        {
            var result = LevelGridParser.Parse(new LevelLayout(w, h, rows));
            Assert.IsTrue(result.Ok, string.Join("; ", result.Errors));
            return result.Cells;
        }

        static Puzzle Board(int w, int h, params string[] rows)
            => new Puzzle(LevelId.Parse("t_near"), w, h, LevelTuning.Default(3), Parse(w, h, rows));

        /// <summary>A crystal, a conduit and a critter in a line. Solved as authored.</summary>
        static Puzzle Solved() => Board(3, 1, "*E#R -EW @W");

        /// <summary>The same line with the middle conduit a quarter turn out.</summary>
        static Puzzle OneTurnOut() => Board(3, 1, "*E#R -EW/1 @W");

        // ------------------------------------------------------------- the bound
        [Test]
        public void ASolvedBoardIsNoTurnsFromSolved()
        {
            var board = Solved();

            Assert.IsTrue(board.Won, "the authored solution should light the critter");
            Assert.AreEqual(0, board.TurnsToSolution);
        }

        /// <summary>
        /// The load-bearing one. Every other test here is about not overstating; this is
        /// the promise the number actually makes to the player — that the turns it counts
        /// are turns that would have won.
        /// </summary>
        [Test]
        public void OneTurnFromSolvedMeansOneTurnWins()
        {
            var board = OneTurnOut();
            Assert.AreEqual(1, board.TurnsToSolution);

            board.Turn(board.Idx(1, 0));
            board.Evaluate();

            Assert.IsTrue(board.Won, "a board reported one turn short must be won by one turn");
            Assert.AreEqual(0, board.TurnsToSolution);
        }

        /// <summary>
        /// A conduit strung off the lit network can point anywhere in a perfectly winnable
        /// board. Charging the player turns for straightening it would inflate every
        /// reading and quietly retire the near-miss line, because nothing would ever be
        /// within two.
        /// </summary>
        [Test]
        public void DecorationOffTheSolutionsNetworkIsNotCounted()
        {
            var board = Board(3, 2,
                              "*E#R -EW @W",
                              "-EW/1 . .");

            Assert.IsTrue(board.Won, "the stray conduit should not stop the critter waking");
            Assert.AreEqual(0, board.TurnsToSolution,
                            "a conduit the solution never reaches is not owed anything");
        }

        // ------------------------------------------------------------- refusals
        /// <summary>
        /// A crumbled conduit takes its own owed turns out of the board with it — see
        /// <see cref="Puzzle.Used"/> — so any count over what survives reads *lower* than
        /// the truth. That is the one direction the bound may not fail in, so it declines
        /// to answer instead.
        /// </summary>
        [Test]
        public void ABrokenSolutionCannotBeMeasured()
        {
            var board = Board(3, 1, "*E#R -EW/1~1 @W");
            int middle = board.Idx(1, 0);

            board.Turn(middle);
            Assert.IsFalse(board.Shattered(middle), "a conduit survives exactly its allowance");
            Assert.AreEqual(0, board.TurnsToSolution);

            board.Turn(middle);
            Assert.IsTrue(board.Shattered(middle));
            Assert.AreEqual(-1, board.TurnsToSolution);
        }

        // -------------------------------------------------------- the run policy
        [Test]
        public void RunningOutOfTurnsWithinTwoIsANearMiss()
        {
            var run = RunOutcome.Loss(OneTurnOut(), DefeatReason.OutOfMoves,
                                      previousBest: 0, attempt: 1, hintsUsed: 0, seconds: 30f, millis: 0, route: 0);

            Assert.AreEqual(1, run.TurnsShort);
            Assert.IsTrue(run.NearMiss);
        }

        /// <summary>
        /// Not squeamishness about the other ending — the number is simply not sound
        /// there, for the reason <see cref="ABrokenSolutionCannotBeMeasured"/> pins. This
        /// board's own count happens to be intact because the conduit that broke was
        /// decoration, and the answer is still no: the panel cannot tell the difference,
        /// so the policy does not depend on it being able to.
        /// </summary>
        [Test]
        public void ACrumbleIsNeverReportedAsANearMiss()
        {
            var board = Board(3, 2,
                              "*E#R -EW/1 @W",
                              "-EW/1~1 . .");

            int stray = board.Idx(0, 1);
            board.Turn(stray);
            board.Turn(stray);
            Assert.IsTrue(board.Shattered(stray));
            Assert.AreEqual(1, board.TurnsToSolution, "the solution's own path is untouched");

            var run = RunOutcome.Loss(board, DefeatReason.ConduitLost,
                                      previousBest: 0, attempt: 2, hintsUsed: 0, seconds: 12f, millis: 0, route: 0);

            Assert.AreEqual(-1, run.TurnsShort);
            Assert.IsFalse(run.NearMiss);
        }

        [Test]
        public void AWonRunIsNeverANearMiss()
        {
            var board = Solved();
            var run = RunOutcome.Win(board, stars: 3, previousBest: 0, firstClear: true,
                                     attempt: 1, hintsUsed: 0, seconds: 20f, millis: 0, route: 0);

            Assert.AreEqual(-1, run.TurnsShort);
            Assert.IsFalse(run.NearMiss);
            Assert.AreEqual(0, run.TurnsToSolution);
        }

        // --------------------------------------------------------- the value type
        /// <summary>
        /// A loss is not a worse clear. Nothing downstream — a reward, a streak, an event
        /// track — may read a star or a record off one, so the fields that describe a
        /// clear stay at their "never" values rather than at anything mistakable.
        /// </summary>
        [Test]
        public void ALossCarriesNoResult()
        {
            var run = RunOutcome.Loss(OneTurnOut(), DefeatReason.OutOfMoves,
                                      previousBest: 14, attempt: 3, hintsUsed: 1, seconds: 9f, millis: 0);

            Assert.IsFalse(run.Won);
            Assert.AreEqual(0, run.Stars);
            Assert.IsFalse(run.FirstClear);
            Assert.IsFalse(run.NewBest);
            Assert.IsFalse(run.Flawless);
            Assert.AreEqual(14, run.PreviousBest, "the record it failed to beat still stands");
        }

        /// <summary>
        /// The first clear of a glade has no record to beat, and zero means "never
        /// cleared" rather than "cleared perfectly" — the same convention the save file
        /// uses, so the two cannot disagree about what a fresh player's best is.
        /// </summary>
        [Test]
        public void AFirstClearIsAlwaysANewBest()
        {
            var board = Solved();
            board.Moves = 40;

            var run = RunOutcome.Win(board, stars: 1, previousBest: 0, firstClear: true,
                                     attempt: 1, hintsUsed: 0, seconds: 60f, millis: 0, route: 0);

            Assert.IsTrue(run.NewBest);
        }

        [Test]
        public void AWorseReplayIsNotANewBest()
        {
            var board = Solved();
            board.Moves = 40;

            var run = RunOutcome.Win(board, stars: 1, previousBest: 12, firstClear: false,
                                     attempt: 5, hintsUsed: 0, seconds: 60f, millis: 0, route: 0);

            Assert.IsFalse(run.NewBest);
        }

        /// <summary>
        /// Guards against the field's own history: the win panel has always shown the
        /// three-star threshold, which is <see cref="Puzzle.Gold"/> and not
        /// <see cref="Puzzle.Par"/>. They are different numbers, and a rename that
        /// "corrected" this would silently change what every player is told to aim at.
        /// </summary>
        [Test]
        public void TheTargetIsTheThreeStarThresholdNotPar()
        {
            var board = Solved();
            var run = RunOutcome.Win(board, stars: 3, previousBest: 0, firstClear: true,
                                     attempt: 1, hintsUsed: 0, seconds: 20f, millis: 0, route: 0);

            Assert.AreEqual(board.Gold, run.Target);
            Assert.AreNotEqual(board.Par, board.Gold,
                               "if these ever become the same number this test proves nothing");
        }
    }
}
