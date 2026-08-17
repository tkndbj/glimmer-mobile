using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The run stopwatch, and the record it writes.
    ///
    /// <para>
    /// A best time is permanent and only ever falls, which makes every way of over-counting
    /// unrecoverable: one bad reading becomes a record no honest run can ever beat, on that
    /// glade, for the life of the account. There is no "recalculate" to reach for afterwards,
    /// because nothing else in the save implies how long a past clear took. So the arithmetic
    /// is defensive by design and this is where that is pinned.
    /// </para>
    /// </summary>
    public sealed class RunClockTests
    {
        static readonly LevelId Glade = LevelId.Parse("plain_one");

        static RunClock Started()
        {
            var clock = new RunClock();
            clock.Start();
            return clock;
        }

        // ------------------------------------------------------------- the clock
        [Test]
        public void NothingIsCountedBeforeTheFirstTurn()
        {
            var clock = new RunClock();

            clock.Advance(5f);
            clock.Advance(5f);

            Assert.IsFalse(clock.HasStarted);
            Assert.AreEqual(0, clock.Millis, "time before the first turn is thinking, not playing");
        }

        [Test]
        public void TimeAccumulatesOnceStarted()
        {
            var clock = Started();

            for (int i = 0; i < 60; i++) clock.Advance(1f / 60f);

            Assert.AreEqual(1000, clock.Millis, 20);
        }

        /// <summary>
        /// The failure this type exists to prevent. A phone call, a notification, a locked
        /// screen or a rewarded video all arrive as one enormous delta, and a stopwatch that
        /// believed it would write a record of forty minutes for a two minute glade.
        /// </summary>
        [Test]
        public void OneEnormousFrameCannotPoisonARecord()
        {
            var clock = Started();

            clock.Advance(1f);          // clamped to MaxTick
            clock.Advance(45f * 60f);   // a suspended app, clamped too

            Assert.LessOrEqual(clock.Millis, (int)(RunClock.MaxTick * 2000f) + 1);
        }

        [Test]
        public void ABrokenFrameContributesNothing()
        {
            var clock = Started();

            clock.Advance(float.NaN);
            clock.Advance(float.PositiveInfinity);
            clock.Advance(-30f);
            clock.Advance(0f);

            Assert.AreEqual(1, clock.Millis, "started, so floored at 1 — but nothing accrued");
        }

        /// <summary>
        /// Zero has to mean "never timed" everywhere this is stored, so a real run can never
        /// produce it. A one-turn tutorial board genuinely resolves inside a second, which is
        /// exactly why the unit is milliseconds and not seconds.
        /// </summary>
        [Test]
        public void AStartedClockNeverReadsZero()
        {
            var clock = Started();

            Assert.AreEqual(1, clock.Millis);

            clock.Advance(.0001f);
            Assert.GreaterOrEqual(clock.Millis, 1);
        }

        [Test]
        public void StartIsIdempotentSoThePollerCannotDoubleFire()
        {
            var clock = new RunClock();

            clock.Start();
            clock.Advance(.2f);
            clock.Start();
            clock.Start();

            Assert.AreEqual(200, clock.Millis, 20, "a second Start must not rewind the reading");
        }

        /// <summary>
        /// A celebration runs for seconds after a win, and the value is about to be written
        /// to a permanent record. Stopping has to be final.
        /// </summary>
        [Test]
        public void AStoppedClockAcceptsNoMoreTime()
        {
            var clock = Started();
            clock.Advance(.2f);
            clock.Stop();

            clock.Advance(.2f);
            clock.Advance(.2f);

            Assert.AreEqual(200, clock.Millis, 20);
        }

        /// <summary>
        /// The leak that would matter. A clock surviving a restart hands the next run the
        /// previous one's time — and because a best only ever falls, whichever reading was
        /// lower sticks for ever.
        /// </summary>
        [Test]
        public void ResetReturnsItToNeverStarted()
        {
            var clock = Started();
            clock.Advance(.2f);
            clock.Stop();

            clock.Reset();

            Assert.IsFalse(clock.HasStarted);
            Assert.IsFalse(clock.IsStopped, "a reset clock has to be able to run again");
            Assert.AreEqual(0, clock.Millis);

            clock.Advance(.2f);
            Assert.AreEqual(0, clock.Millis, "and it stays at nothing until the first turn");
        }

        // ------------------------------------------------------------ formatting
        [Test]
        public void TimesReadAsAStopwatch()
        {
            Assert.AreEqual("0:00", RunClock.Format(0));
            Assert.AreEqual("0:00", RunClock.Format(-5));
            Assert.AreEqual("0:01", RunClock.Format(1000));
            Assert.AreEqual("0:09", RunClock.Format(9999));
            Assert.AreEqual("1:00", RunClock.Format(60_000));
            Assert.AreEqual("2:14", RunClock.Format(134_000));
            Assert.AreEqual("59:59", RunClock.Format(3_599_000));
        }

        /// <summary>
        /// Past an hour it grows a field rather than wrapping. A run that reads shorter than
        /// it was is worse than one that reads long.
        /// </summary>
        [Test]
        public void AnHourLongRunGrowsAFieldRatherThanWrapping()
        {
            Assert.AreEqual("1:00:00", RunClock.Format(3_600_000));
            Assert.AreEqual("1:04:12", RunClock.Format(3_852_000));
        }

        // ---------------------------------------------------------- the better of two
        [Test]
        public void FasterWinsAndZeroMeansNeverTimed()
        {
            Assert.AreEqual(1200, RunClock.Better(1200, 3000));
            Assert.AreEqual(1200, RunClock.Better(3000, 1200));

            Assert.AreEqual(1200, RunClock.Better(1200, 0), "an untimed run cannot beat a timed one");
            Assert.AreEqual(1200, RunClock.Better(0, 1200));
            Assert.AreEqual(0, RunClock.Better(0, 0));
            Assert.AreEqual(0, RunClock.Better(0, -9), "and nonsense reads as untimed");
        }

        // --------------------------------------------------------------- the record
        static LevelRecord Cleared(int moves, int millis = 0)
            => new LevelRecord(Glade, 3, moves, 1, 100, 100, 0, millis);

        [Test]
        public void AFasterClearReplacesTheRecordedTime()
        {
            var held = Cleared(moves: 40, millis: 90_000);

            Assert.AreEqual(62_000, held.WithTime(62_000).BestMillis);
        }

        [Test]
        public void ASlowerClearLeavesTheRecordedTimeAlone()
        {
            var held = Cleared(moves: 40, millis: 62_000);
            var after = held.WithTime(90_000);

            Assert.AreSame(held, after, "nothing improved, so nothing is rewritten");
            Assert.AreEqual(62_000, after.BestMillis);
        }

        [Test]
        public void AnUntimedRunLeavesTheRecordedTimeAlone()
        {
            var held = Cleared(moves: 40, millis: 62_000);

            Assert.AreSame(held, held.WithTime(0));
            Assert.AreEqual(62_000, held.WithTime(0).BestMillis);
        }

        /// <summary>
        /// Moves and time are independent bests, the way stars and moves already are: a
        /// player can beat their move count on one run and their time on another, and both
        /// results are real.
        /// </summary>
        [Test]
        public void MovesAndTimeImproveIndependently()
        {
            var held = Cleared(moves: 40, millis: 62_000);

            // A slower run that used fewer turns.
            var after = held.WithRun(3, 31, 200).WithTime(90_000);

            Assert.AreEqual(31, after.BestMoves);
            Assert.AreEqual(62_000, after.BestMillis);

            // And the reverse.
            var faster = after.WithRun(3, 55, 300).WithTime(41_000);

            Assert.AreEqual(31, faster.BestMoves);
            Assert.AreEqual(41_000, faster.BestMillis);
        }

        [Test]
        public void FoldingARunCarriesTheTimeThroughUntouched()
        {
            var held = Cleared(moves: 40, millis: 62_000);

            // WithRun knows nothing about time; it must not drop what is already held.
            Assert.AreEqual(62_000, held.WithRun(3, 38, 200).BestMillis);
            Assert.AreEqual(62_000, held.WithRank(Social.LevelStats.None).BestMillis);
        }

        [Test]
        public void TheTimeSurvivesADtoRoundTrip()
        {
            var held = Cleared(moves: 40, millis: 62_000);

            Assert.IsTrue(LevelRecord.TryFromDto(held.ToDto(), out var back));
            Assert.AreEqual(62_000, back.BestMillis);
            Assert.AreEqual(40, back.BestMoves);
        }

        [Test]
        public void ANegativeTimeReadsAsUntimed()
        {
            var broken = new LevelRecordDto
            {
                levelId = Glade.Value, stars = 3, bestMoves = 40, clears = 1, bestMillis = -9,
            };

            Assert.IsTrue(LevelRecord.TryFromDto(broken, out var record));
            Assert.AreEqual(0, record.BestMillis);
        }

        // ------------------------------------------------------------------ the merge
        static SaveFileDto File(int moves, int millis, long updatedUnix)
            => new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                updatedUnix = updatedUnix,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                levels = new[]
                {
                    new LevelRecordDto
                    {
                        levelId = Glade.Value, stars = 3, bestMoves = moves, clears = 1,
                        firstClearedUnix = updatedUnix, lastPlayedUnix = updatedUnix,
                        bestMillis = millis,
                    },
                },
                progression = ProgressionStateDto.Unwritten(),
            };

        static LevelRecordDto Only(SaveFileDto dto) => dto.levels[0];

        [Test]
        public void TheMergeKeepsTheFasterTimeRegardlessOfWhichSideIsNewer()
        {
            Assert.AreEqual(41_000, Only(SaveMerge.Join(File(40, 41_000, 100), File(40, 90_000, 900))).bestMillis);
            Assert.AreEqual(41_000, Only(SaveMerge.Join(File(40, 90_000, 900), File(40, 41_000, 100))).bestMillis);
        }

        /// <summary>
        /// A device on an older build, or one that cleared the glade before the clock
        /// existed, writes zero. Zero is "never timed" and must not win a comparison against
        /// a real time — which is the whole reason the unit is milliseconds.
        /// </summary>
        [Test]
        public void AnUntimedDeviceCannotEraseARecordedTime()
        {
            var older = File(40, 0, 900);
            var mine = File(40, 41_000, 100);

            Assert.AreEqual(41_000, Only(SaveMerge.Join(mine, older)).bestMillis);
            Assert.AreEqual(41_000, Only(SaveMerge.Join(older, mine)).bestMillis);
        }

        [Test]
        public void TheTimeMergeIsAJoin()
        {
            var a = File(40, 41_000, 100);
            var b = File(31, 90_000, 900);

            var ab = SaveMerge.Join(a, b);
            var ba = SaveMerge.Join(b, a);

            Assert.AreEqual(Only(ab).bestMillis, Only(ba).bestMillis, "order independent");
            Assert.AreEqual(Only(ab).bestMillis, Only(SaveMerge.Join(ab, ab)).bestMillis, "idempotent");
            Assert.AreEqual(41_000, Only(ab).bestMillis);
            Assert.AreEqual(31, Only(ab).bestMoves, "and the move count joins separately");
        }

        [Test]
        public void ANewTimeIsWorthSyncing()
        {
            var remote = File(40, 0, 100);
            var local = File(40, 41_000, 100);

            Assert.IsFalse(SaveDelta.Between(remote, local).IsEmpty);
            Assert.IsTrue(SaveDelta.Between(local, local).IsEmpty);
        }
    }
}
