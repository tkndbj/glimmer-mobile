using GlimmerGrove.Content;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The countdown: the limit a glade allows, the run it ends, and the half of the star
    /// rule it owns.
    ///
    /// <para>
    /// <b>What makes this worth pinning is what did <em>not</em> change.</b> The clock counts
    /// down, but what is measured and stored is still elapsed play time — so
    /// <c>LevelRecord.BestMillis</c>, the map badge and <c>publishGroveStats</c> all kept
    /// working without a migration, a schema bump or a server deploy. Remaining time is
    /// derived for the HUD and reaches nothing else. If a change ever makes the save hold
    /// time <i>left</i> instead, every one of those breaks at once and silently, because both
    /// are milliseconds and both look plausible in a save file.
    /// </para>
    /// </summary>
    public sealed class CountdownTests
    {
        /// <summary>Par 30 at the shipped 2s/turn: a 60 second glade, gold at 30, silver at 45.</summary>
        static LevelTuning Timed(int par = 30, float timeFactor = 0f)
            => new LevelTuning(par, LevelTuning.DefaultGoldFactor, LevelTuning.DefaultSilverFactor,
                               LevelTuning.DefaultHintAllowance, 0f, timeFactor);

        static LevelTuning Untimed(int par = 30)
            => new LevelTuning(par, LevelTuning.DefaultGoldFactor, LevelTuning.DefaultSilverFactor,
                               LevelTuning.DefaultHintAllowance, 0f, LevelTuning.Unlimited);

        // ------------------------------------------------------------ the limit
        /// <summary>
        /// Derived from par, for the reason the move budget is: a flat limit is a different
        /// difficulty on every board, and nothing about authoring one per level makes that
        /// visible to whoever authored it.
        /// </summary>
        [Test]
        public void TheLimitScalesWithTheBoardRatherThanBeingFlat()
        {
            Assert.AreEqual(60_000, Timed(30).TimeLimitMillis);
            Assert.AreEqual(68_000, Timed(34).TimeLimitMillis, "c01_first_light");
            Assert.AreEqual(98_000, Timed(49).TimeLimitMillis, "c01_twin_streams");

            Assert.Greater(Timed(49).TimeLimitMillis, Timed(34).TimeLimitMillis,
                           "the harder board gets the longer clock, which a flat 60s could not do");
        }

        [Test]
        public void AnOmittedFactorTakesTheDefaultAndOnlyANegativeOneRemovesTheClock()
        {
            Assert.IsTrue(Timed(30, 0f).HasTimeLimit, "0 means 'not authored', never 'untimed'");
            Assert.AreEqual(LevelTuning.DefaultTimeFactor, Timed(30, 0f).TimeFactor);

            Assert.IsFalse(Untimed().HasTimeLimit);
            Assert.AreEqual(int.MaxValue, Untimed().TimeLimitMillis,
                            "so callers can compare without special-casing, as MoveBudget does");
        }

        /// <summary>
        /// The two star thresholds are fractions of the limit, so retuning
        /// <see cref="LevelTuning.TimeFactor"/> moves all three together and they cannot
        /// drift apart.
        /// </summary>
        [Test]
        public void TheClockThresholdsFollowTheLimit()
        {
            var tight = Timed(30, 1f);      // 30s
            var loose = Timed(30, 4f);      // 120s

            Assert.AreEqual(15_000, tight.TimeGoldMillis);
            Assert.AreEqual(22_500, tight.TimeSilverMillis);

            Assert.AreEqual(60_000, loose.TimeGoldMillis);
            Assert.AreEqual(90_000, loose.TimeSilverMillis);
        }

        // ------------------------------------------------------------- the stars
        [Test]
        public void TheClockAloneGradesOnItsOwnThresholds()
        {
            var t = Timed();                // 60s, gold 30s, silver 45s

            Assert.AreEqual(3, t.StarsForTime(29_999));
            Assert.AreEqual(3, t.StarsForTime(30_000), "the boundary is inclusive, as the move one is");
            Assert.AreEqual(2, t.StarsForTime(30_001));
            Assert.AreEqual(2, t.StarsForTime(45_000));
            Assert.AreEqual(1, t.StarsForTime(45_001));
            Assert.AreEqual(1, t.StarsForTime(60_000));
        }

        /// <summary>
        /// Zero is "never timed" and costs nothing, which is the convention the save file,
        /// <see cref="RunClock.Millis"/> and <c>BestMillis</c> all already use. Grading a run
        /// nobody measured as though it took forever would punish a player for something they
        /// did not do.
        /// </summary>
        [Test]
        public void AnUnmeasuredRunIsNeverPenalisedByTheClock()
        {
            Assert.AreEqual(3, Timed().StarsForTime(0));
            Assert.AreEqual(3, Timed().StarsForTime(-1));
            Assert.AreEqual(3, Untimed().StarsForTime(999_999), "no clock, no opinion");
        }

        /// <summary>
        /// The whole rule: the worse of the two readings. Three stars therefore means
        /// efficient <em>and</em> quick, and a player who lost one can see which half cost it
        /// because both numbers are on the panel.
        /// </summary>
        [Test]
        public void StarsAreTheWorseOfTheTwoReadings()
        {
            var t = Timed();                // par 30 -> gold 41 moves, silver 60; 30s / 45s

            Assert.AreEqual(3, t.StarsFor(40, 29_000), "quick and efficient");
            Assert.AreEqual(1, t.StarsFor(40, 50_000), "efficient, but the clock says one");
            Assert.AreEqual(1, t.StarsFor(70, 29_000), "quick, but the turns say one");
            Assert.AreEqual(2, t.StarsFor(55, 29_000), "the turns are the worse half");
            Assert.AreEqual(2, t.StarsFor(40, 44_000), "the clock is the worse half");
        }

        [Test]
        public void TheMoveThresholdsKeepExactlyTheMeaningTheyHad()
        {
            var t = Timed();

            for (int moves = 1; moves < 100; moves++)
                Assert.AreEqual(t.StarsForMoves(moves), t.StarsFor(moves, 0),
                                $"an untimed run of {moves} turns scores as it always did");
        }

        // ------------------------------------------------------------- the clock
        [Test]
        public void ResetArmsTheClockForTheGladeItIsAbout()
        {
            var clock = new RunClock();
            Assert.IsFalse(clock.HasLimit, "a clock built with the screen knows no glade yet");

            clock.Reset(60_000);
            Assert.IsTrue(clock.HasLimit);
            Assert.AreEqual(60_000, clock.LimitMillis);
            Assert.AreEqual(60_000, clock.RemainingMillis, "nothing spent before the first turn");

            clock.Reset(0);
            Assert.IsFalse(clock.HasLimit, "and the next glade cannot inherit the last one's clock");
        }

        /// <summary>
        /// An untouched board is not on a countdown. Without the started guard,
        /// <c>Millis &gt;= LimitMillis</c> holds at zero and the run would be lost on the
        /// frame it appeared.
        /// </summary>
        [Test]
        public void AnUntouchedBoardHasNotRunOutOfTime()
        {
            var clock = new RunClock();
            clock.Reset(60_000);

            Assert.IsFalse(clock.Expired);

            clock.Advance(1f);
            Assert.IsFalse(clock.Expired, "still not started, so that frame was not counted");
            Assert.AreEqual(60_000, clock.RemainingMillis);
        }

        [Test]
        public void TheClockExpiresExactlyWhenItIsSpent()
        {
            var clock = new RunClock();
            clock.Reset(1_000);
            clock.Start();

            for (int i = 0; i < 3; i++) clock.Advance(.25f);
            Assert.IsFalse(clock.Expired, "0.75s of a 1s glade");
            Assert.AreEqual(250, clock.RemainingMillis);

            clock.Advance(.25f);
            Assert.IsTrue(clock.Expired);
            Assert.AreEqual(0, clock.RemainingMillis);
        }

        /// <summary>
        /// Held at the limit rather than allowed past it, so a spent clock reads 0:00 and the
        /// elapsed time it reports is a number the glade could actually produce — which
        /// matters because that number is what a win writes to the record.
        /// </summary>
        [Test]
        public void ASpentClockNeverOverruns()
        {
            var clock = new RunClock();
            clock.Reset(1_000);
            clock.Start();

            for (int i = 0; i < 40; i++) clock.Advance(.25f);

            Assert.AreEqual(1_000, clock.Millis);
            Assert.AreEqual(0, clock.RemainingMillis);
        }

        [Test]
        public void AnUntimedClockBehavesExactlyAsItDidBefore()
        {
            var clock = new RunClock();
            clock.Reset();
            clock.Start();

            for (int i = 0; i < 40; i++) clock.Advance(.25f);

            Assert.IsFalse(clock.HasLimit);
            Assert.IsFalse(clock.Expired, "no limit, no expiry, however long the run");
            Assert.AreEqual(10_000, clock.Millis);
            Assert.AreEqual(0, clock.RemainingMillis, "0 means 'no clock to report', not 'spent'");
        }

        // ------------------------------------------------------------ the outcome
        static Puzzle Board(int moves, LevelTuning tuning)
        {
            var cells = new Cell[1];
            cells[0] = new Cell { kind = Kind.Lamp, solved = Puzzle.N, rot = 0, colour = 0 };

            var board = new Puzzle(LevelId.Parse("plain_one"), 1, 1, tuning, cells);
            board.Moves = moves;
            return board;
        }

        [Test]
        public void ARunCarriesItsOwnLimitSoNothingHasToAskTheBoardLater()
        {
            var run = RunOutcome.Loss(Board(20, Timed()), DefeatReason.OutOfTime, previousBest: 0,
                                      attempt: 1, hintsUsed: 0, seconds: 60f, millis: 60_000);

            Assert.IsTrue(run.TimedOut);
            Assert.IsTrue(run.HasTimeLimit);
            Assert.AreEqual(60_000, run.TimeLimit);
            Assert.AreEqual(0, run.TimeLeft);

            var untimed = RunOutcome.Loss(Board(20, Untimed()), DefeatReason.OutOfMoves, previousBest: 0,
                                          attempt: 1, hintsUsed: 0, seconds: 60f, millis: 60_000);

            Assert.IsFalse(untimed.HasTimeLimit);
            Assert.IsFalse(untimed.TimedOut);
            Assert.AreEqual(0, untimed.TimeLeft);
        }

        [Test]
        public void TimeLeftIsWhatTheRunHadInHand()
        {
            var run = RunOutcome.Win(Board(20, Timed()), stars: 3, previousBest: 0, firstClear: true,
                                     attempt: 1, hintsUsed: 0, seconds: 22f, millis: 22_000, route: 20);

            Assert.AreEqual(38_000, run.TimeLeft);
            Assert.AreEqual(22_000, run.Millis, "what is stored is still time taken, never time left");
        }

        /// <summary>
        /// A timeout leaves the board intact, so the distance to a solution is exactly as
        /// sound as it is on the move budget — and it is the ending where "one turn from it"
        /// drives a retry hardest. Only a crumbled conduit refuses the count, because it takes
        /// its own owed turns off the board with it.
        /// </summary>
        /// <summary>A crystal, a conduit a quarter turn out, and a critter. One turn from won.</summary>
        static Puzzle OneTurnOut(LevelTuning tuning)
        {
            var parsed = LevelGridParser.Parse(new LevelLayout(3, 1, new[] { "*E#R -EW/1 @W" }));
            Assert.IsTrue(parsed.Ok, string.Join("; ", parsed.Errors));

            return new Puzzle(LevelId.Parse("t_clock"), 3, 1, tuning, parsed.Cells);
        }

        [Test]
        public void ATimeoutStillKnowsHowCloseThePlayerWas()
        {
            var board = OneTurnOut(Timed());
            Assert.AreEqual(1, board.TurnsToSolution, "the fixture itself has to be one turn out");

            var timedOut = RunOutcome.Loss(board, DefeatReason.OutOfTime, previousBest: 0,
                                           attempt: 1, hintsUsed: 0, seconds: 60f, millis: 60_000);

            Assert.AreEqual(1, timedOut.TurnsShort);
            Assert.IsTrue(timedOut.NearMiss);

            var crumbled = RunOutcome.Loss(board, DefeatReason.ConduitLost, previousBest: 0,
                                           attempt: 1, hintsUsed: 0, seconds: 60f, millis: 60_000);

            Assert.AreEqual(-1, crumbled.TurnsShort, "the count over the survivors would flatter");
        }
    }
}
