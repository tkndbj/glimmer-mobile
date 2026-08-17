using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The heart gate as content.
    ///
    /// <para>
    /// The gate decides how many sessions a player gets, so it is the number in the economy
    /// most likely to be wrong on launch day and the most expensive to leave wrong. Making
    /// it publishable is worth very little if a bad push can break a save, so the cases
    /// below are weighted toward what a <em>hostile or mistaken file</em> can do rather than
    /// toward what a correct one does.
    /// </para>
    /// </summary>
    public sealed class HeartRuleTableTests
    {
        const long T0 = 1_700_000_000;

        [TearDown]
        public void Restore() => ProgressionRules.Reset();

        static HeartRuleTable Read(HeartsDto dto, List<string> problems = null)
            => HeartRuleTable.Resolve(dto, problems ?? new List<string>());

        /// <summary>Installs a table so the live facade reads the authored numbers.</summary>
        static void Publish(HeartsDto hearts)
        {
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 10,
                hearts = hearts,
            };

            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, new List<string>()));
            ProgressionRules.Publish(table);
        }

        static HeartsDto Unwritten() => new HeartsDto();

        // ------------------------------------------------------------- reading
        [Test]
        public void AnAbsentBlockKeepsTheBuiltInNumbers()
        {
            var table = Read(null);

            Assert.AreEqual(HeartLimits.DefaultRefillCap, table.RefillCap);
            Assert.AreEqual(HeartLimits.DefaultCeiling, table.Ceiling);
            Assert.AreEqual(HeartLimits.DefaultRefillSeconds, table.RefillSeconds);
            Assert.AreEqual(HeartLimits.DefaultDefeatCost, table.DefeatCost);
        }

        /// <summary>
        /// Every field is independently optional, which is what lets a live push change one
        /// number without restating the other five — and restating five numbers to change
        /// one is how the other five drift away from what anybody intended.
        /// </summary>
        [Test]
        public void AnUnwrittenFieldInheritsRatherThanReadingZero()
        {
            var table = Read(new HeartsDto { refillSeconds = 3600 });

            Assert.AreEqual(3600, table.RefillSeconds, "the one that was written");
            Assert.AreEqual(HeartLimits.DefaultRefillCap, table.RefillCap, "and the rest inherit");
            Assert.AreEqual(HeartLimits.DefaultCeiling, table.Ceiling);
            Assert.AreEqual(HeartLimits.DefaultDefeatCost, table.DefeatCost);
        }

        [Test]
        public void EveryFieldIsClampedIntoItsSupportedRange()
        {
            var problems = new List<string>();

            var table = Read(new HeartsDto
            {
                refillCap = 9999,
                ceiling = 9999,
                refillSeconds = 1,
                boostedRefillSeconds = 1,
                maxBoostHours = 9999,
                defeatCost = 9999,
            }, problems);

            Assert.AreEqual(HeartLimits.MaxRefillCap, table.RefillCap);
            Assert.AreEqual(HeartLimits.HardCeiling, table.Ceiling);
            Assert.AreEqual(HeartLimits.MinRefillSeconds, table.RefillSeconds);
            Assert.AreEqual(HeartLimits.MaxBoostHoursLimit, table.MaxBoostHours);
            Assert.AreEqual(HeartLimits.MaxDefeatCost, table.DefeatCost);

            Assert.IsNotEmpty(problems, "a clamped file is still an authoring mistake worth reporting");
        }

        /// <summary>
        /// A "boost" slower than the ordinary rate is the feature working backwards: the
        /// player is told hearts return faster and they return more slowly. Nothing else in
        /// the build would notice, because both numbers are individually legal.
        /// </summary>
        [Test]
        public void ABoostSlowerThanTheOrdinaryRateIsHeldAtIt()
        {
            var problems = new List<string>();
            var table = Read(new HeartsDto { refillSeconds = 3600, boostedRefillSeconds = 7200 }, problems);

            Assert.AreEqual(3600, table.BoostedRefillSeconds, "never slower than not being boosted");
            Assert.IsNotEmpty(problems);
        }

        /// <summary>
        /// A ceiling under the refill cap is a contradiction rather than a small ceiling —
        /// the clock would carry a player past what they are allowed to hold, so the timer
        /// would keep paying while every grant was refused.
        /// </summary>
        [Test]
        public void ACeilingBelowTheRefillCapIsRaisedToIt()
        {
            var problems = new List<string>();
            var table = Read(new HeartsDto { refillCap = 10, ceiling = 3 }, problems);

            Assert.AreEqual(10, table.Ceiling);
            Assert.IsNotEmpty(problems);
        }

        // ------------------------------------------------------- the live facade
        [Test]
        public void ThePublishedTableIsWhatTheGameRunsOn()
        {
            Publish(new HeartsDto { refillCap = 3, ceiling = 12, refillSeconds = 1800, defeatCost = 2 });

            Assert.AreEqual(3, HeartRules.RefillCap);
            Assert.AreEqual(12, HeartRules.Ceiling);
            Assert.AreEqual(1800, HeartRules.RefillSeconds);
            Assert.AreEqual(2, HeartRules.DefeatCost);

            // and the ledger follows it without being told. Spent down rather than built at
            // zero, because a ledger with no deadline has a timer that has never started and
            // deliberately refuses to back-pay for the wait nobody did — see
            // AStateWithNoDeadlineStartsTheClockRatherThanBackPaying.
            var empty = Hearts.Full.Spend(3, T0);
            Assert.AreEqual(0, empty.Count);

            Assert.AreEqual(3, empty.At(T0 + 100 * 1800).Count, "the clock stops at the published cap");
        }

        [Test]
        public void APublishedRefillPeriodChangesWhenTheNextHeartLands()
        {
            Publish(new HeartsDto { refillSeconds = 600 });

            var spent = Hearts.Full.Spend(1, T0);

            Assert.AreEqual(T0 + 600, spent.NextRefillUnix);
            Assert.AreEqual(HeartRules.RefillCap, spent.At(T0 + 600).Count);
        }

        // ----------------------------------------------- what a push must not do
        /// <summary>
        /// The case this whole design is arranged around, and the reason the ledger clamps
        /// to <see cref="HeartLimits.HardCeiling"/> rather than to the published ceiling.
        ///
        /// <para>
        /// A player collects thirty hearts. A config push then lowers the ceiling to ten.
        /// If the ledger's own clamp used the published number, <c>produced</c> would be cut
        /// downward on the next read — and <c>produced</c> is the counter the entire merge
        /// proof rests on only ever rising. The player would lose twenty hearts they had
        /// earned, and worse, a second device that had not fetched the new table would keep
        /// restoring them, so the two would never converge.
        /// </para>
        /// </summary>
        [Test]
        public void LoweringThePublishedCeilingNeverConfiscatesHeartsAlreadyHeld()
        {
            Publish(new HeartsDto { ceiling = 40 });
            var rich = Hearts.Full.Grant(25, T0);
            Assert.AreEqual(30, rich.Count);

            // the economy is retuned overnight
            Publish(new HeartsDto { ceiling = 10 });

            Assert.AreEqual(30, rich.Count, "a tuning change must never reach into a save");
            Assert.AreEqual(30, rich.At(T0 + 90000).Count, "and reading it back must not either");
            Assert.AreEqual(30, Hearts.Join(rich, rich).Count, "nor may a merge quietly apply it");

            // What it does do is refuse to add more, which is the whole of what a lower
            // ceiling should mean.
            Assert.IsTrue(rich.IsAtCeiling);
            Assert.AreEqual(30, rich.Grant(5, T0).Count, "no new grants while over the new ceiling");

            // and once they spend back under it, grants resume against the new number
            var spent = rich.Spend(25, T0);
            Assert.AreEqual(5, spent.Count);
            Assert.AreEqual(10, spent.Grant(99, T0).Count, "topped up to the published ceiling, not the old one");
        }

        /// <summary>
        /// The same property for the refill cap: lowering it stops the clock earlier and
        /// leaves anybody above it holding exactly what they had.
        /// </summary>
        [Test]
        public void LoweringThePublishedRefillCapStopsTheClockRatherThanDraining()
        {
            Publish(new HeartsDto { refillCap = 5 });
            var full = Hearts.Full;
            Assert.AreEqual(5, full.Count);

            Publish(new HeartsDto { refillCap = 2 });

            Assert.AreEqual(5, full.At(T0 + 30 * 86400).Count, "the clock is a floor, never a drain");
            Assert.IsTrue(full.IsRefilled);
            Assert.AreEqual(0, full.NextRefillUnix);
        }

        /// <summary>
        /// The structural bound is a constant, so no file — however hostile — can widen the
        /// range the ledger has to represent. A published ceiling is clamped to it on the
        /// way in, which is what keeps the merge's upper invariant a fact about the code
        /// rather than a fact about whatever was last downloaded.
        /// </summary>
        [Test]
        public void NoPublishedCeilingCanExceedTheStructuralBound()
        {
            Publish(new HeartsDto { ceiling = int.MaxValue });

            Assert.AreEqual(HeartLimits.HardCeiling, HeartRules.Ceiling);
            Assert.AreEqual(HeartLimits.HardCeiling, Hearts.Full.Grant(int.MaxValue, T0).Count);
        }

        /// <summary>
        /// A file that cannot be read at all costs the live tuning and nothing else. The
        /// curve, the chests, the ads and the streak ladder all have to survive it — an
        /// unreadable gate must never be able to take the rest of the economy down with it.
        /// </summary>
        [Test]
        public void AnUnreadableHeartsBlockDoesNotDiscardTheRestOfTheTable()
        {
            var problems = new List<string>();
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 10,
                hearts = new HeartsDto { refillCap = -50, refillSeconds = 3 },
            };

            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, problems),
                          "a bad gate is not a reason to lose the curve");

            Assert.AreEqual(100, table.XpToNext(1), "the curve is intact");
            Assert.AreEqual(HeartLimits.MinRefillSeconds, table.Hearts.RefillSeconds, "and the gate is safe");
        }
    }
}
