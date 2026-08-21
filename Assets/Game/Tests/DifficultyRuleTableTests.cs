using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Difficulty as content.
    ///
    /// <para>
    /// This is the block whose failure is not a mistuning but an unplayable game. Every other
    /// number in <c>progression.json</c> has a worst case that is merely a worse deal; a clock
    /// scale pushed too low makes every glade in the world impossible to finish, on every
    /// device at once, with no app update to roll back and no way for a player to tell it from
    /// the game being broken. So the cases below are weighted toward what a <em>hostile or
    /// mistaken file</em> can do rather than toward what a correct one does — the balance
    /// <c>HeartRuleTableTests</c> strikes, for a sharper reason.
    /// </para>
    /// </summary>
    public sealed class DifficultyRuleTableTests
    {
        [TearDown]
        public void Restore() => ProgressionRules.Reset();

        static DifficultyRuleTable Read(DifficultyDto dto, List<string> problems = null)
            => DifficultyRuleTable.Resolve(dto, problems ?? new List<string>());

        /// <summary>Installs a table so the live facade reads the authored number.</summary>
        static void Publish(float clockScale)
        {
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 10,
                difficulty = new DifficultyDto { clockScale = clockScale },
            };

            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, new List<string>()));
            ProgressionRules.Publish(table);
        }

        static LevelTuning Glade(int par = 30, float timeFactor = 2f)
            => new LevelTuning(par, LevelTuning.DefaultGoldFactor, LevelTuning.DefaultSilverFactor, 0f, timeFactor);

        // ------------------------------------------------------------- reading
        [Test]
        public void AnAbsentBlockPlaysTheContentExactlyAsAuthored()
        {
            Assert.AreEqual(DifficultyLimits.DefaultClockScale, Read(null).ClockScale);

            var problems = new List<string>();
            Read(null, problems);
            Assert.IsEmpty(problems, "an absent optional block is not a content error");
        }

        /// <summary>
        /// Zero is what <c>JsonUtility</c> writes into a field a file predating this never
        /// had, so it has to keep meaning "not set" rather than "a clock of zero seconds".
        /// </summary>
        [Test]
        public void AnUnwrittenScaleIsNotAClockOfZero()
        {
            Assert.AreEqual(1f, Read(new DifficultyDto()).ClockScale, "the DTO's own -1 default");
            Assert.AreEqual(1f, Read(new DifficultyDto { clockScale = 0f }).ClockScale);
            Assert.AreEqual(1f, Read(new DifficultyDto { clockScale = -0.5f }).ClockScale);
        }

        [Test]
        public void AnAuthoredScaleIsRead()
        {
            Assert.AreEqual(0.8f, Read(new DifficultyDto { clockScale = 0.8f }).ClockScale, 1e-5f);
            Assert.AreEqual(1.4f, Read(new DifficultyDto { clockScale = 1.4f }).ClockScale, 1e-5f);
        }

        /// <summary>
        /// The one bound that has to be a compile-time constant: it is what a published file
        /// is checked <em>against</em>, and a limit that could itself be published would not
        /// be a limit.
        /// </summary>
        [Test]
        public void AScaleOutsideTheBandIsClampedAndSaysSo()
        {
            var problems = new List<string>();
            Assert.AreEqual(DifficultyLimits.MinClockScale,
                            Read(new DifficultyDto { clockScale = 0.05f }, problems).ClockScale);
            Assert.IsNotEmpty(problems, "a clamped push has to be visible in the build");

            problems.Clear();
            Assert.AreEqual(DifficultyLimits.MaxClockScale,
                            Read(new DifficultyDto { clockScale = 99f }, problems).ClockScale);
            Assert.IsNotEmpty(problems);
        }

        // ------------------------------------------------------------- applying
        /// <summary>
        /// <b>The scale moves the limit and nothing else.</b> That is the whole design: the
        /// star thresholds are held against par, so a retune changes where a run is
        /// <em>lost</em> without changing what a clear is <em>worth</em> — and earned credits
        /// are derived from the star ledger, so the alternative is a difficulty push that
        /// silently retunes the economy with it.
        /// </summary>
        [Test]
        public void ThePublishedScaleMovesTheLimitAndLeavesTheStarsAlone()
        {
            Assert.AreEqual(60_000, Glade().TimeLimitMillis);
            Assert.AreEqual(30_000, Glade().TimeGoldMillis);
            Assert.AreEqual(45_000, Glade().TimeSilverMillis);

            Publish(0.75f);

            Assert.AreEqual(45_000, Glade().TimeLimitMillis, "the losing edge moved in");
            Assert.AreEqual(30_000, Glade().TimeGoldMillis, "three stars is the same run it was");
            Assert.AreEqual(45_000, Glade().TimeSilverMillis);

            Assert.AreEqual(3, Glade().StarsForTime(30_000));
            Assert.AreEqual(2, Glade().StarsForTime(30_001));
        }

        /// <summary>
        /// A scale tight enough to cut under the three-star line does not make three stars
        /// impossible — it makes finishing at all worth them. That is the honest reading of a
        /// threshold beyond the point the run ends, and it is the only one that cannot punish
        /// somebody for something they did not do.
        /// </summary>
        [Test]
        public void AScaleTighterThanTheStarLineGradesFinishingAsTheBestItCanBe()
        {
            Publish(DifficultyLimits.MinClockScale);        // 0.6 -> 36s on a 60s glade

            var glade = Glade();
            Assert.AreEqual(36_000, glade.TimeLimitMillis);
            Assert.AreEqual(30_000, glade.TimeGoldMillis, "still under the limit, so still real");

            Publish(0.4f);                                  // clamped to the floor, not honoured
            Assert.AreEqual(36_000, Glade().TimeLimitMillis);
        }

        /// <summary>
        /// <see cref="int.MaxValue"/> is the sentinel an untimed glade uses so callers can
        /// compare without special-casing. Scaling it would overflow it into a limit, which is
        /// the one thing it must never become.
        /// </summary>
        [Test]
        public void AnUntimedGladeIsNeverScaledIntoATimedOne()
        {
            Publish(0.6f);

            var untimed = new LevelTuning(30, LevelTuning.DefaultGoldFactor,
                                          LevelTuning.DefaultSilverFactor, 0f, LevelTuning.Unlimited);

            Assert.IsFalse(untimed.HasTimeLimit);
            Assert.AreEqual(int.MaxValue, untimed.TimeLimitMillis);
            Assert.AreEqual(int.MaxValue, untimed.TimeGoldMillis);
            Assert.AreEqual(3, untimed.StarsForTime(999_999));
        }

        /// <summary>
        /// A limit rounded to nothing is not a hard glade, it is a glade lost on the frame it
        /// appears. The floor is well inside the published band, so this only ever guards a
        /// board with a par of one.
        /// </summary>
        [Test]
        public void ALimitNeverRoundsAwayToNothing()
        {
            Publish(DifficultyLimits.MinClockScale);

            var tiny = new LevelTuning(1, LevelTuning.DefaultGoldFactor,
                                       LevelTuning.DefaultSilverFactor, 0f, 0.5f);

            Assert.GreaterOrEqual(tiny.TimeLimitMillis, 1000);
        }

        // ------------------------------------------------------------ the reach
        /// <summary>
        /// <b>The scale reaches the limit and nothing that is stored.</b> What a run records is
        /// elapsed play time, never time left, so a retune leaves <c>bestMillis</c>, the map
        /// badge and the published move deciles all meaning exactly what they meant. If a
        /// change ever makes the save hold time left instead, this is where it shows up — both
        /// are milliseconds and both look plausible in a save file.
        /// </summary>
        [Test]
        public void ARetuneNeverReachesWhatARunRecorded()
        {
            var clock = new RunClock();
            clock.Reset(Glade().TimeLimitMillis);
            clock.Start();
            clock.Advance(0.25f);
            clock.Advance(0.25f);

            int taken = clock.Millis;
            Publish(0.6f);

            Assert.AreEqual(taken, clock.Millis, "a running clock's elapsed time is not retunable");
            Assert.AreEqual(500, taken, "and it is time taken, which is what the save stores");
        }
    }
}
