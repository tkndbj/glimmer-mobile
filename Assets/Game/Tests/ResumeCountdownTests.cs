using System.Collections.Generic;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The count a continued run is handed back on (2026-10-11): a lead, three, two, one, GO - and
    /// the run moves on GO. Every frame of it, at every frame rate a player could meet.
    /// </summary>
    public sealed class ResumeCountdownTests
    {
        const float Step = SiegeScreen.ResumeBeatSeconds;

        /// <summary>Runs a count at a fixed frame length; answers the beats drawn and the seconds taken.</summary>
        static (List<int> beats, float seconds) Play(float frame, int most = 100000)
        {
            var count = new ResumeCountdown();
            count.Start(Step);

            var beats = new List<int>();
            float seconds = 0f;

            for (int i = 0; i < most && count.Active; i++)
            {
                int beat = count.Advance(frame);
                seconds += frame > ResumeCountdown.MostPerFrame ? ResumeCountdown.MostPerFrame : frame;
                if (beat != ResumeCountdown.Nothing) beats.Add(beat);
            }

            Assert.IsFalse(count.Active, "the count never finished, so the run would never move");
            return (beats, seconds);
        }

        /// <summary>Advances a count by some seconds in 60 fps frames; answers the last beat drawn.</summary>
        static int By(ResumeCountdown count, float seconds)
        {
            int last = ResumeCountdown.Nothing;
            for (float t = 0f; t < seconds - 1e-5f; t += 1f / 60f)
            {
                int beat = count.Advance(System.Math.Min(1f / 60f, seconds - t));
                if (beat != ResumeCountdown.Nothing) last = beat;
            }
            return last;
        }

        [Test]
        public void ItCountsThreeTwoOneGoOnceEachInOrder()
        {
            var (beats, _) = Play(1f / 60f);

            CollectionAssert.AreEqual(new[] { 3, 2, 1, ResumeCountdown.Go }, beats);
        }

        [Test]
        public void TheRunMovesOnTheFrameThatSaysGo()
        {
            var count = new ResumeCountdown();
            count.Start(Step);

            int beat = ResumeCountdown.Nothing;
            while (count.Active) beat = count.Advance(1f / 60f);

            Assert.AreEqual(ResumeCountdown.Go, beat, "the frame the count ends is the frame that draws GO");
        }

        [Test]
        public void ItLastsTheLeadAndThreeBeats()
        {
            var (_, seconds) = Play(1f / 60f);

            Assert.AreEqual(ResumeCountdown.Lead + 3 * Step, seconds, 1f / 30f);
            Assert.Less(seconds, 3f, "short: a countdown, not a second wait for the player");
        }

        [Test]
        public void NothingIsDrawnWhileTheLineIsStillStandingUp()
        {
            var count = new ResumeCountdown();
            count.Start(Step);

            Assert.AreEqual(ResumeCountdown.Nothing, By(count, ResumeCountdown.Lead * .9f),
                            "the first number waits for the rally to land");
            Assert.AreEqual(3, By(count, ResumeCountdown.Lead * .2f));
        }

        /// <summary>
        /// A choppy device misses beats rather than stacking them, and still always ends on GO,
        /// because GO is the beat that tells the player the hill is moving.
        /// </summary>
        [Test]
        public void ASlowFrameRateSkipsANumberButNeverGo()
        {
            foreach (float frame in new[] { 1f / 30f, 1f / 12f, .2f, .25f })
            {
                var (beats, _) = Play(frame);

                Assert.AreEqual(ResumeCountdown.Go, beats[beats.Count - 1], $"at {frame:0.000}s a frame");
                for (int i = 1; i < beats.Count; i++)
                    Assert.Less(beats[i], beats[i - 1], $"at {frame:0.000}s a frame the count went backwards");
            }
        }

        /// <summary>A resumed app cannot finish the count in one frame: each frame is clamped.</summary>
        [Test]
        public void AHugeFrameAdvancesOnlyAQuarterSecond()
        {
            var count = new ResumeCountdown();
            count.Start(Step);

            count.Advance(30f);

            Assert.IsTrue(count.Active, "thirty seconds away from the app ended the count in one frame");
        }

        [Test]
        public void AFrameWithNoTimeDrawsNothingAndKeepsCounting()
        {
            var count = new ResumeCountdown();
            count.Start(Step);

            Assert.AreEqual(ResumeCountdown.Nothing, count.Advance(0f));
            Assert.AreEqual(ResumeCountdown.Nothing, count.Advance(float.NaN));
            Assert.AreEqual(ResumeCountdown.Nothing, count.Advance(float.PositiveInfinity));
            Assert.AreEqual(ResumeCountdown.Nothing, count.Advance(-1f));
            Assert.IsTrue(count.Active);
        }

        /// <summary>A mode with no count is handed back at once: starting with no step starts nothing.</summary>
        [Test]
        public void NoStepIsNoCount()
        {
            var count = new ResumeCountdown();
            count.Start(0f);

            Assert.IsFalse(count.Active);
            Assert.AreEqual(ResumeCountdown.Nothing, count.Advance(1f / 60f));
        }

        [Test]
        public void ACancelledCountDrawsNothingMore()
        {
            var count = new ResumeCountdown();
            count.Start(Step);
            By(count, ResumeCountdown.Lead + .02f);

            count.Cancel();

            Assert.IsFalse(count.Active);
            Assert.AreEqual(ResumeCountdown.Nothing, count.Advance(Step));
        }

        /// <summary>A second continue on the same run starts its count from the top.</summary>
        [Test]
        public void StartingAgainCountsFromTheTop()
        {
            var count = new ResumeCountdown();
            count.Start(Step);
            while (count.Active) count.Advance(1f / 60f);

            count.Start(Step);

            Assert.IsTrue(count.Active);
            Assert.AreEqual(3, By(count, ResumeCountdown.Lead + .02f), "the new count opens on 3 again");
            Assert.AreEqual(ResumeCountdown.Nothing, count.Advance(.01f), "and draws it once");
        }
    }
}
