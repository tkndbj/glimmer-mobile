using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The percentile the victory panel quotes.
    ///
    /// <para>
    /// This is the only number in the game that is about other people, which makes it the
    /// only one a player cannot check. Everything else — a star, a reward, a streak — they
    /// can verify by playing again; "faster than 70% of keepers" they have to take on
    /// trust. So the arithmetic has to be defensible and the refusals have to be real: a
    /// percentile over a dozen players is noise wearing a fact's clothes, and the players
    /// who would see it are the first to reach a new chapter, which is to say the most
    /// engaged players in the game.
    /// </para>
    /// </summary>
    public sealed class GroveStatsTests
    {
        /// <summary>Ten through ninety, so a decile boundary is its own percentile.</summary>
        static LevelStats Table(int samples = 1000)
            => new LevelStats(samples, new[] { 10, 20, 30, 40, 50, 60, 70, 80, 90 });

        [TearDown]
        public void Forget() => GroveStats.Clear();

        // -------------------------------------------------------- the percentile
        [Test]
        public void FasterThanTheWholeTableIsCappedRatherThanPerfect()
        {
            // Never 100: a line saying a player beat everybody is a line somebody will
            // find a counterexample to on the same board an hour later.
            Assert.AreEqual(95, Table().PercentSlower(10));
            Assert.AreEqual(95, Table().PercentSlower(1));
        }

        [Test]
        public void SlowerThanTheWholeTableIsCappedRatherThanZero()
        {
            Assert.AreEqual(5, Table().PercentSlower(90));
            Assert.AreEqual(5, Table().PercentSlower(500));
        }

        [Test]
        public void AMoveCountOnADecileBoundaryReadsAsThatDecile()
        {
            // 50 is p50, so half the population took more.
            Assert.AreEqual(50, Table().PercentSlower(50));

            // 20 is p20, so four fifths took more.
            Assert.AreEqual(80, Table().PercentSlower(20));
        }

        [Test]
        public void AMoveCountBetweenTwoDecilesIsInterpolated()
        {
            // Halfway between p50 and p60 is p55, so 45% took more.
            Assert.AreEqual(45, Table().PercentSlower(55));
        }

        [Test]
        public void FewerMovesNeverReadsAsWorse()
        {
            var table = Table();
            int previous = -1;

            // Walked from slow to fast, so the answer must only ever rise.
            for (int moves = 100; moves >= 1; moves--)
            {
                int slower = table.PercentSlower(moves);
                Assert.GreaterOrEqual(slower, previous,
                                      $"{moves} moves reads worse than {moves + 1} did");
                previous = slower;
            }
        }

        // ---------------------------------------------------------- the refusals
        /// <summary>
        /// Silence is the correct output of a sample too small to speak from. The players
        /// who reach a brand new chapter first are exactly the ones a noisy percentile
        /// would be shown to.
        /// </summary>
        [Test]
        public void ASampleTooSmallSaysNothing()
        {
            var thin = Table(LevelStats.MinimumSamples - 1);

            Assert.IsFalse(thin.IsUsable);
            Assert.AreEqual(-1, thin.PercentSlower(10));
            Assert.IsFalse(thin.IsWorthSaying(10));
        }

        [Test]
        public void AMalformedTableSaysNothing()
        {
            var short_ = new LevelStats(1000, new[] { 10, 20, 30 });

            Assert.IsFalse(short_.IsUsable);
            Assert.AreEqual(-1, short_.PercentSlower(15));
        }

        [Test]
        public void NoTableAtAllSaysNothing()
        {
            Assert.IsFalse(LevelStats.None.IsUsable);
            Assert.AreEqual(-1, LevelStats.None.PercentSlower(10));
        }

        /// <summary>
        /// Drawn upward only. Told they are ahead, a player plays more; told they are
        /// behind, a good share stop — and the ones who stop are disproportionately the
        /// ones who were already struggling. Both readings are true; this is a decision
        /// about which true things belong on a victory screen.
        /// </summary>
        [Test]
        public void TheLineIsOnlyWorthSayingWhenItIsGoodNews()
        {
            var table = Table();

            Assert.IsTrue(table.IsWorthSaying(20), "faster than four fifths");
            Assert.IsTrue(table.IsWorthSaying(50), "exactly average is the boundary");
            Assert.IsFalse(table.IsWorthSaying(70), "slower than most: say nothing");
            Assert.IsFalse(table.IsWorthSaying(89));
        }

        [Test]
        public void AnUnplayedGladeSaysNothing()
        {
            Assert.AreEqual(-1, Table().PercentSlower(0));
            Assert.AreEqual(-1, Table().PercentSlower(-4));
        }

        // ------------------------------------------------------------- the table
        [Test]
        public void PublishingKeepsOnlyWhatIsUsable()
        {
            var usable = LevelId.Parse("plain_one");
            var thin = LevelId.Parse("plain_two");

            GroveStats.Publish(new Dictionary<LevelId, LevelStats>
            {
                [usable] = Table(),
                [thin] = Table(LevelStats.MinimumSamples - 1),
            });

            Assert.IsTrue(GroveStats.For(usable).IsUsable);
            Assert.IsFalse(GroveStats.For(thin).IsUsable);
            Assert.AreEqual(1, GroveStats.LevelCount);
        }

        /// <summary>
        /// Replaced wholesale rather than merged. A glade that has dropped out of a later
        /// publish has dropped out for a reason — retired, or a job that read fewer saves —
        /// and leaving the old figure behind would have it claiming to be current.
        /// </summary>
        [Test]
        public void PublishingReplacesRatherThanMerges()
        {
            var first = LevelId.Parse("plain_one");
            var second = LevelId.Parse("plain_two");

            GroveStats.Publish(new Dictionary<LevelId, LevelStats> { [first] = Table() });
            GroveStats.Publish(new Dictionary<LevelId, LevelStats> { [second] = Table() });

            Assert.IsFalse(GroveStats.For(first).IsUsable, "the first table must not survive");
            Assert.IsTrue(GroveStats.For(second).IsUsable);
        }

        [Test]
        public void AGladeNobodyHasPlayedIsSimplyAbsent()
        {
            GroveStats.Publish(new Dictionary<LevelId, LevelStats>());

            Assert.IsFalse(GroveStats.For(LevelId.Parse("plain_one")).IsUsable);
        }
    }
}
