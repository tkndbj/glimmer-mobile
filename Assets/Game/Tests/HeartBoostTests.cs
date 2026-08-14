using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Faster heart regeneration, which a daily chest can award for a day.
    ///
    /// The interesting cases are all about the <em>edges</em> of the window. A boost that
    /// expires in the middle of a catch-up has to pay some refills fast and the rest
    /// slow; a boost granted while a timer is already running has to shorten it without
    /// ever lengthening it; and reading the state repeatedly must not move it, because
    /// the HUD reads hearts every frame.
    /// </summary>
    public sealed class HeartBoostTests
    {
        const long Slow = HeartRules.RefillSeconds;
        const long Fast = HeartRules.BoostedRefillSeconds;
        const long T0 = 1_700_000_000;

        [Test]
        public void TheBoostedPeriodIsShorterThanTheNormalOne()
        {
            Assert.Less(Fast, Slow, "a boost nobody feels is not a boost");
            Assert.AreEqual(Slow, HeartRules.PeriodAt(T0, 0));
            Assert.AreEqual(Slow, HeartRules.PeriodAt(T0, T0), "expiring exactly now is expired");
            Assert.AreEqual(Fast, HeartRules.PeriodAt(T0, T0 + 1));
        }

        [Test]
        public void SpendingUnderABoostStartsTheShorterClock()
        {
            long until = T0 + 24 * 3600;
            var hearts = Hearts.Full.Spend(1, T0, until);

            Assert.AreEqual(HeartRules.Max - 1, hearts.Count);
            Assert.AreEqual(T0 + Fast, hearts.NextRefillUnix);
        }

        [Test]
        public void ARunningTimerIsPulledForwardWhenABoostArrives()
        {
            var hearts = Hearts.Full.Spend(1, T0);          // next at T0 + 8h, unboosted
            Assert.AreEqual(T0 + Slow, hearts.NextRefillUnix);

            // The boost lands an hour later. Seven hours of waiting would otherwise be
            // left, which reads to the player as the boost simply not working.
            var boosted = hearts.At(T0 + 3600, T0 + 3600 + 24 * 3600);

            Assert.AreEqual(T0 + 3600 + Fast, boosted.NextRefillUnix);
        }

        [Test]
        public void ABoostNeverPushesADeadlineFurtherAway()
        {
            var hearts = Hearts.Full.Spend(1, T0);
            var almost = hearts.At(T0 + Slow - 60);          // one minute to go

            var boosted = almost.At(T0 + Slow - 60, T0 + Slow + 10 * 3600);

            Assert.AreEqual(almost.NextRefillUnix, boosted.NextRefillUnix,
                            "a boost must not lengthen a wait that was nearly over");
        }

        [Test]
        public void ReadingRepeatedlyUnderABoostDoesNotMoveTheDeadline()
        {
            long until = T0 + 24 * 3600;
            var hearts = Hearts.Full.Spend(1, T0, until);
            long deadline = hearts.NextRefillUnix;

            for (long t = T0; t < T0 + Fast; t += 600)
                hearts = hearts.At(t, until);

            Assert.AreEqual(deadline, hearts.NextRefillUnix,
                            "the HUD reads this every frame; a read that drifts is a timer that never lands");
        }

        [Test]
        public void ABoostedPeriodReturnsExactlyOneHeart()
        {
            long until = T0 + 24 * 3600;
            var spent = Hearts.Full.Spend(2, T0, until);     // 3/5, next at T0 + 4h

            Assert.AreEqual(HeartRules.Max - 2, spent.At(T0 + Fast - 1, until).Count);
            Assert.AreEqual(HeartRules.Max - 1, spent.At(T0 + Fast, until).Count);
        }

        /// <summary>
        /// The case a naive implementation gets wrong by rounding to one rate or the
        /// other: a player who closes the app with the boost running and opens it long
        /// after it expired has earned some hearts fast and the rest slowly.
        /// </summary>
        [Test]
        public void ACatchUpSpanningTheExpiryPaysBothRates()
        {
            long until = T0 + Fast + 1;                      // covers exactly the first refill
            var spent = Hearts.Full.Spend(3, T0, until);     // 2/5, next at T0 + 4h

            // First heart at T0+4h (boosted). The second wait starts there, by which time
            // the boost has gone, so it costs the full eight hours.
            var after = spent.At(T0 + Fast + Slow, until);

            Assert.AreEqual(HeartRules.Max - 1, after.Count,
                            "two refills would mean the expiry was ignored; none would mean the boost was");
        }

        [Test]
        public void AnExpiredBoostBehavesExactlyLikeNoBoost()
        {
            long expired = T0 - 1;

            var withExpired = Hearts.Full.Spend(1, T0, expired);
            var without = Hearts.Full.Spend(1, T0);

            Assert.AreEqual(without.Count, withExpired.Count);
            Assert.AreEqual(without.NextRefillUnix, withExpired.NextRefillUnix);
        }

        // ----------------------------------------------------------- the merge
        [Test]
        public void TheLaterBoostDeadlineSurvivesAMerge()
        {
            Assert.AreEqual(T0 + 500, Hearts.JoinBoost(T0 + 500, T0 + 100));
            Assert.AreEqual(T0 + 500, Hearts.JoinBoost(T0 + 100, T0 + 500));
        }

        [Test]
        public void ADeviceWithNoBoostDoesNotCutShortOneThatHasIt()
        {
            Assert.AreEqual(T0 + 500, Hearts.JoinBoost(T0 + 500, 0),
                            "the award behind a boost is deduplicated by its own id, so the " +
                            "conservative rule would only ever take something away");
            Assert.AreEqual(T0 + 500, Hearts.JoinBoost(0, T0 + 500));
            Assert.AreEqual(0, Hearts.JoinBoost(0, 0));
        }

        [Test]
        public void TheBoostMergeIsAJoin()
        {
            long a = T0 + 900, b = T0 + 300;

            Assert.AreEqual(Hearts.JoinBoost(a, b), Hearts.JoinBoost(b, a));
            Assert.AreEqual(Hearts.JoinBoost(a, b), Hearts.JoinBoost(Hearts.JoinBoost(a, b), b));
        }

        /// <summary>
        /// A boost must not resurrect hearts the merge is supposed to keep conservative.
        /// The count still takes the smaller side; only the rate is generous.
        /// </summary>
        [Test]
        public void TheBoostDoesNotChangeHowTheHeartCountMerges()
        {
            var phone = new Hearts(4, T0 + Fast);
            var tablet = new Hearts(2, T0 + Slow);

            var merged = Hearts.Join(phone, tablet);

            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual(T0 + Slow, merged.NextRefillUnix, "the later deadline has granted the least");
        }
    }
}
