using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Hearts gate play, so the arithmetic here is the difference between a fair wait
    /// and a player locked out of a game they paid attention to.
    ///
    /// Two properties matter more than the rest and are pinned hardest: refills must
    /// not <em>drift</em> (closing the app mid-timer neither loses nor gains the
    /// remainder), and the merge must never <em>mint</em> (two devices cannot refill
    /// each other). Everything else is a consequence of those two.
    /// </summary>
    public sealed class HeartsTests
    {
        const long P = HeartRules.RefillSeconds;
        const long T0 = 1_700_000_000;      // an arbitrary fixed "now"

        [Test]
        public void AFullSetRunsNoTimer()
        {
            var hearts = Hearts.Full;

            Assert.AreEqual(HeartRules.Max, hearts.Count);
            Assert.AreEqual(0, hearts.NextRefillUnix, "a full player has no deadline to show");
            Assert.AreEqual(0, hearts.SecondsToNext(T0));
        }

        [Test]
        public void SpendingFromFullStartsTheClock()
        {
            var hearts = Hearts.Full.Spend(1, T0);

            Assert.AreEqual(HeartRules.Max - 1, hearts.Count);
            Assert.AreEqual(T0 + P, hearts.NextRefillUnix);
            Assert.AreEqual(P, hearts.SecondsToNext(T0));
        }

        /// <summary>
        /// Losing again must not push the pending heart away. If it did, a bad run of
        /// defeats would silently reset a timer the player had already half-waited.
        /// </summary>
        [Test]
        public void SpendingAgainDoesNotPushTheRefillBack()
        {
            var first = Hearts.Full.Spend(1, T0);
            var second = first.Spend(1, T0 + 60);

            Assert.AreEqual(HeartRules.Max - 2, second.Count);
            Assert.AreEqual(first.NextRefillUnix, second.NextRefillUnix,
                            "the pending heart keeps its original deadline");
        }

        [Test]
        public void OnePeriodReturnsExactlyOneHeart()
        {
            var spent = Hearts.Full.Spend(2, T0);      // 3/5, next at T0+P

            var justBefore = spent.At(T0 + P - 1);
            Assert.AreEqual(HeartRules.Max - 2, justBefore.Count, "not yet");

            var onTime = spent.At(T0 + P);
            Assert.AreEqual(HeartRules.Max - 1, onTime.Count);
            Assert.AreEqual(T0 + 2 * P, onTime.NextRefillUnix);
        }

        [Test]
        public void SeveralElapsedPeriodsReturnSeveralHearts()
        {
            var spent = Hearts.Full.Spend(4, T0);      // 1/5, next at T0+P

            // refills land at T0+P and T0+2P; the third is not due until T0+3P
            var later = spent.At(T0 + 2 * P);
            Assert.AreEqual(3, later.Count);
            Assert.AreEqual(T0 + 3 * P, later.NextRefillUnix);
        }

        [Test]
        public void ReachingTheCapClearsTheDeadline()
        {
            var spent = Hearts.Full.Spend(1, T0);
            var back = spent.At(T0 + 10 * P);

            Assert.AreEqual(HeartRules.Max, back.Count);
            Assert.AreEqual(0, back.NextRefillUnix, "no timer once full, however long we waited");
        }

        /// <summary>
        /// The anti-drift property. Reading the state repeatedly must be identical to
        /// reading it once at the end — otherwise every HUD tick would shave the timer.
        /// </summary>
        [Test]
        public void ReadingRepeatedlyDoesNotDrift()
        {
            var spent = Hearts.Full.Spend(3, T0);

            var stepped = spent;
            for (long t = T0; t < T0 + 2 * P; t += 37) stepped = stepped.At(t);

            // land the last read on the same instant as the direct one, or the two are
            // being asked different questions rather than the same one twice
            stepped = stepped.At(T0 + 2 * P);

            var direct = spent.At(T0 + 2 * P);

            Assert.AreEqual(direct.Count, stepped.Count);
            Assert.AreEqual(direct.NextRefillUnix, stepped.NextRefillUnix,
                            "polling the timer must not move it");
        }

        [Test]
        public void SpendingAtZeroChangesNothingAndBlocksPlay()
        {
            var empty = Hearts.Full.Spend(HeartRules.Max, T0);
            Assert.IsTrue(empty.IsEmpty);
            Assert.IsFalse(empty.CanPlay, "zero hearts is the gate");

            var again = empty.Spend(1, T0 + 5);
            Assert.AreEqual(0, again.Count);
            Assert.AreEqual(empty.NextRefillUnix, again.NextRefillUnix);
        }

        [Test]
        public void GrantingBeyondTheCapClamps()
        {
            var granted = Hearts.Full.Spend(2, T0).Grant(99, T0);

            Assert.AreEqual(HeartRules.Max, granted.Count);
            Assert.AreEqual(0, granted.NextRefillUnix);
        }

        /// <summary>
        /// A v3 save carries a count and no deadline. It must start waiting from now,
        /// not be paid for all the time that passed before hearts regenerated at all.
        /// </summary>
        [Test]
        public void AStateWithNoDeadlineStartsTheClockRatherThanBackPaying()
        {
            var legacy = new Hearts(2, 0);
            var caught = legacy.At(T0);

            Assert.AreEqual(2, caught.Count, "no free hearts for time nobody waited");
            Assert.AreEqual(T0 + P, caught.NextRefillUnix);
        }

        // -------------------------------------------------------------- the merge
        [Test]
        public void JoinTakesTheSmallerCountAndTheLaterDeadline()
        {
            var a = new Hearts(4, T0 + 100);
            var b = new Hearts(2, T0 + 900);

            var joined = Hearts.Join(a, b);

            Assert.AreEqual(2, joined.Count, "hearts are consumable; the spender wins");
            Assert.AreEqual(T0 + 900, joined.NextRefillUnix, "the deadline that has granted least");
        }

        /// <summary>
        /// A full device carries deadline 0, meaning "no timer" rather than "epoch". If
        /// that won a max() it would wipe the other device's countdown and hand over a
        /// free heart on the next read.
        /// </summary>
        [Test]
        public void AFullDeviceDoesNotEraseTheOthersCountdown()
        {
            var full = Hearts.Full;                       // 5, deadline 0
            var spent = new Hearts(1, T0 + 500);

            var joined = Hearts.Join(full, spent);

            Assert.AreEqual(1, joined.Count);
            Assert.AreEqual(T0 + 500, joined.NextRefillUnix);
        }

        [Test]
        public void JoinIsIdempotentAndOrderIndependent()
        {
            var a = new Hearts(4, T0 + 100);
            var b = new Hearts(2, T0 + 900);
            var c = new Hearts(3, 0);

            Assert.AreEqual(Hearts.Join(a, b).Count, Hearts.Join(b, a).Count);
            Assert.AreEqual(Hearts.Join(a, b).NextRefillUnix, Hearts.Join(b, a).NextRefillUnix);

            var once = Hearts.Join(a, b);
            var twice = Hearts.Join(once, Hearts.Join(a, b));
            Assert.AreEqual(once.Count, twice.Count);
            Assert.AreEqual(once.NextRefillUnix, twice.NextRefillUnix);

            // associative, so three devices converge regardless of sync order
            var left = Hearts.Join(Hearts.Join(a, b), c);
            var right = Hearts.Join(a, Hearts.Join(b, c));
            Assert.AreEqual(left.Count, right.Count);
            Assert.AreEqual(left.NextRefillUnix, right.NextRefillUnix);
        }

        [Test]
        public void TwoDevicesCannotRefillEachOther()
        {
            // both start full, each loses two, then they sync
            var deviceA = Hearts.Full.Spend(2, T0);
            var deviceB = Hearts.Full.Spend(2, T0);

            var joined = Hearts.Join(deviceA, deviceB);

            Assert.AreEqual(HeartRules.Max - 2, joined.Count,
                            "a merge must never hand back a heart somebody spent");
        }
    }

    /// <summary>
    /// The clock guard. A backwards jump is ordinary on a real device — an NTP
    /// correction, a timezone fix — and without the guard it would strand a refill
    /// deadline in the future and show a countdown that grows.
    /// </summary>
    public sealed class GameClockTests
    {
        sealed class FixedClock : IGameClock
        {
            public long Now;
            public long UtcNowUnix => Now;
            public bool IsTrusted => false;
        }

        [TearDown]
        public void Restore() => GameClock.Set(new DeviceClock());

        [Test]
        public void TimeNeverGoesBackwards()
        {
            var clock = new FixedClock { Now = 1_000 };
            GameClock.Set(clock);

            Assert.AreEqual(1_000, GameClock.NowUnix());

            clock.Now = 400;        // the player winds the device back
            Assert.AreEqual(1_000, GameClock.NowUnix(), "the guard holds the high-water mark");

            clock.Now = 1_500;      // and forwards again
            Assert.AreEqual(1_500, GameClock.NowUnix());
        }

        [Test]
        public void TheDeviceClockIsNeverTrusted()
        {
            GameClock.Set(new DeviceClock());
            Assert.IsFalse(GameClock.IsTrusted,
                           "a user-settable clock must never be evidence for a purchase");
        }

        [Test]
        public void AServerAnchoredClockReportsServerTimeAndIsTrusted()
        {
            var device = new FixedClock { Now = 1_000 };
            var server = new ServerAnchoredClock(device);

            server.Anchor(5_000);                       // server says it is really 5000
            Assert.AreEqual(5_000, server.UtcNowUnix);
            Assert.IsTrue(server.IsTrusted);

            device.Now = 1_060;                         // a minute of device time passes
            Assert.AreEqual(5_060, server.UtcNowUnix, "the offset rides along");
        }

        /// <summary>
        /// Moving the device clock moves the reading and the offset together, so the
        /// corrected time does not budge. That is the whole point of anchoring.
        /// </summary>
        [Test]
        public void MovingTheDeviceClockDoesNotMoveAnchoredTime()
        {
            var device = new FixedClock { Now = 1_000 };
            var server = new ServerAnchoredClock(device);
            server.Anchor(5_000);

            device.Now = 999_000;                       // a big jump forward
            long farFuture = server.UtcNowUnix;

            device.Now = 1_000;                         // and back
            server.Anchor(5_000);                       // next sync re-anchors

            Assert.AreEqual(5_000, server.UtcNowUnix);
            Assert.AreNotEqual(farFuture, server.UtcNowUnix,
                               "an un-resynced jump is exactly what the server anchor exists to correct");
        }
    }
}
