using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// What is left of the countdown, which is a stored number and no rule at all.
    ///
    /// <para>
    /// A glade used to be played against a clock, and stars were the worse of what the turns
    /// allowed and what the clock allowed. Both are gone: a run is graded on turns alone
    /// (<see cref="LevelTuning.StarsFor"/>), nothing measures play time, and no level authors
    /// a <c>timeFactor</c>. This fixture replaces <c>CountdownTests</c>, <c>ContinueTests</c>
    /// and <c>RunClockTests</c>, and it pins the two things that outlived the mechanic.
    /// </para>
    /// <para>
    /// <b>The star rule reads turns and only turns.</b> That is the rule the whole change
    /// rests on, and it is worth a test rather than a reading of the source, because the
    /// failure it guards against is silent: an overload taking a second number would compile
    /// at every call site and quietly re-grade the game.
    /// </para>
    /// <para>
    /// <b>A time already earned still merges.</b> <see cref="LevelRecord.BestMillis"/> is
    /// retired rather than deleted — it is on the wire in both directions and removing a save
    /// field is how a rollback loses data (invariant 12a) — so the join has to keep working
    /// for the accounts that hold one. Smaller wins, zero is absent, and the join stays
    /// idempotent and order-independent like every other rule in <see cref="SaveMerge"/>
    /// (invariant 11).
    /// </para>
    /// </summary>
    public sealed class RetiredRunTimeTests
    {
        static readonly LevelId Glade = LevelId.Parse("plain_one");

        /// <summary>Par 30, so three stars at 36 turns, two at 42, and the run ends at 48.</summary>
        static LevelTuning Tuning()
            => new LevelTuning(30, LevelTuning.DefaultGoldFactor, LevelTuning.DefaultSilverFactor);

        // --------------------------------------------------------------- the star rule
        [Test]
        public void StarsComeFromTheTurnCountAlone()
        {
            var tuning = Tuning();

            Assert.AreEqual(36, tuning.GoldThreshold);
            Assert.AreEqual(42, tuning.SilverThreshold);

            Assert.AreEqual(3, tuning.StarsFor(1), "a board finished in one turn");
            Assert.AreEqual(3, tuning.StarsFor(36), "exactly on the three-star line");
            Assert.AreEqual(2, tuning.StarsFor(37));
            Assert.AreEqual(2, tuning.StarsFor(42), "exactly on the two-star line");
            Assert.AreEqual(1, tuning.StarsFor(43));
            Assert.AreEqual(1, tuning.StarsFor(10_000), "a clear is never worth nothing");
        }

        /// <summary>
        /// A level that authors no budget still gets one, and one that authors a negative
        /// removes it. The convention has to survive the clock's removal untouched: it is the
        /// only remaining way to author a glade that cannot be lost, and the first glade in
        /// the game depends on it.
        /// </summary>
        [Test]
        public void TheBudgetConventionIsUnchanged()
        {
            Assert.AreEqual(LevelTuning.DefaultBudgetFactor,
                            new LevelTuning(30, 0f, 0f).BudgetFactor);
            Assert.AreEqual(1.60f, LevelTuning.DefaultBudgetFactor, 1e-5f,
                            "the shipped losing line; the cases above are written against it");

            var none = new LevelTuning(30, 0f, 0f, LevelTuning.Unlimited);
            Assert.IsFalse(none.HasBudget);
            Assert.AreEqual(int.MaxValue, none.MoveBudget);
        }

        // ------------------------------------------------------------------ the record
        [Test]
        public void ANegativeStoredTimeReadsAsUntimed()
        {
            var broken = new LevelRecordDto
            {
                levelId = Glade.Value, stars = 3, bestMoves = 40, clears = 1, bestMillis = -9,
            };

            Assert.IsTrue(LevelRecord.TryFromDto(broken, out var record));
            Assert.AreEqual(0, record.BestMillis);
        }

        /// <summary>
        /// Recording a run leaves any stored time exactly where it was. Nothing measures play
        /// time now, so the only way a time can change is by being lost — which is the one
        /// outcome keeping the field was meant to prevent.
        /// </summary>
        [Test]
        public void FoldingARunInDoesNotDisturbAStoredTime()
        {
            var held = new LevelRecord(Glade, 2, 50, 1, 100, 100, 0, 62_000);

            var after = held.WithRun(3, 38, 200, Social.LevelStats.None);

            Assert.AreEqual(3, after.Stars);
            Assert.AreEqual(38, after.BestMoves);
            Assert.AreEqual(62_000, after.BestMillis);
            Assert.AreEqual(62_000, held.WithRank(Social.LevelStats.None).BestMillis);
        }

        // ------------------------------------------------------------------- the merge
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
        /// A device on a build that never measured one writes zero, and zero is "never timed"
        /// rather than "instant". Every device writes zero now, so this is the case that
        /// decides whether the times already on accounts survive the drop at all.
        /// </summary>
        [Test]
        public void ADeviceWithNoTimeCannotEraseARecordedOne()
        {
            var blank = File(40, 0, 900);
            var mine = File(40, 41_000, 100);

            Assert.AreEqual(41_000, Only(SaveMerge.Join(mine, blank)).bestMillis);
            Assert.AreEqual(41_000, Only(SaveMerge.Join(blank, mine)).bestMillis);
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
    }
}
