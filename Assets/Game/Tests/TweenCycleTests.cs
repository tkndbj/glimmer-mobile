using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The animation clock, run offline a thousand frames at a time.
    ///
    /// <para>
    /// This suite exists because of what the bug it was written for looked like: for over a
    /// year every <c>Loop(-1, true)</c> in the game snapped back at the end of its period
    /// instead of easing back, and the reverse branch that was supposed to do the easing was
    /// unreachable code. It compiled, it validated, it shipped, and it was reported from play
    /// as "the background seems to flicker a little" — because a wrong number here is not a
    /// wrong pixel, it is a wrong <em>motion</em>, and nothing but running it catches that.
    /// </para>
    /// <para>
    /// Every case drives <see cref="TweenCycle"/> the way <c>Tween.Update</c> drives it — a
    /// step at a time, feeding the previous frame's state back in — so what is proved here is
    /// the sequence the player would have seen, not a single call.
    /// </para>
    /// </summary>
    public sealed class TweenCycleTests
    {
        const float Frame = 1f / 60f;

        /// <summary>
        /// Runs a tween for <paramref name="frames"/> frames and hands back every phase it
        /// passed through, exactly as <c>Tween.Update</c> would have applied them.
        /// </summary>
        static float[] Play(float duration, int loops, bool pingPong, int frames,
                            float step = Frame, float firstStep = -1f)
        {
            var phases = new float[frames];
            float elapsed = 0f;

            for (int i = 0; i < frames; i++)
            {
                float raw = i == 0 && firstStep >= 0f ? firstStep : step;
                var f = TweenCycle.Advance(elapsed, TweenCycle.Step(raw), duration, loops, pingPong);
                elapsed = f.Elapsed;
                loops = f.Loops;
                phases[i] = f.Phase;
            }
            return phases;
        }

        static float LargestJump(float[] phases, int from = 0)
        {
            float worst = 0f;
            for (int i = from + 1; i < phases.Length; i++)
            {
                float d = phases[i] - phases[i - 1];
                if (d < 0f) d = -d;
                if (d > worst) worst = d;
            }
            return worst;
        }

        // ------------------------------------------------------------ the ping-pong

        /// <summary>
        /// The one that was broken. A ping-pong has to come back down, and the only proof
        /// that survives a refactor is that it visits the far end and returns without ever
        /// moving more in one frame than one frame's worth.
        /// </summary>
        [Test]
        public void APingPongReturnsRatherThanSnappingBack()
        {
            // 3.4s is the hub's backdrop light, which is where this was seen.
            var phases = Play(3.4f, -1, true, 900);

            float top = 0f, bottom = 1f;
            foreach (float p in phases)
            {
                if (p > top) top = p;
                if (p < bottom) bottom = p;
            }

            Assert.Greater(top, .99f, "never reached the far end");
            Assert.Less(bottom, .01f, "never came back");

            // A frame may only ever move the phase by a frame's worth of the traverse. The
            // snap this replaces was a jump of a whole 1.0 once per period.
            Assert.Less(LargestJump(phases), Frame / 3.4f * 1.5f,
                        "the phase jumped further in one frame than one frame of travel");
        }

        /// <summary>
        /// The rise is not slowed by the fix, which is the part that decides whether any
        /// of the shipped durations need retuning. A traverse still takes exactly
        /// <c>duration</c>; what changed is that the return takes one too, instead of
        /// happening between two frames.
        /// </summary>
        [Test]
        public void OneTraverseStillTakesExactlyTheDuration()
        {
            var phases = Play(1f, -1, true, 240);

            int peak = 0;
            for (int i = 1; i < phases.Length; i++)
                if (phases[i] > phases[peak]) peak = i;

            // 60 frames of 1/60s to climb, give or take the frame it lands on.
            Assert.That(peak, Is.InRange(59, 61), "the climb changed length");
            Assert.Less(phases[120], .02f, "should be back at the bottom after two traverses");
        }

        [Test]
        public void APingPongIsAlwaysAValidPhase()
        {
            foreach (float p in Play(.4f, -1, true, 2000))
                Assert.That(p, Is.InRange(0f, 1f));
        }

        // ------------------------------------------------------------- the big step

        /// <summary>
        /// The resume. <c>Time.unscaledDeltaTime</c> is not capped the way
        /// <c>Time.deltaTime</c> is, so the first frame back carries however long the app was
        /// suspended. Two minutes is an ordinary lunch break.
        /// </summary>
        [Test]
        public void ResumingAfterTwoMinutesDoesNotSwingThePhase()
        {
            var phases = Play(3.4f, -1, true, 400, firstStep: 120f);

            // From the second frame on — the first is allowed to land wherever a clamped
            // step puts it — nothing may move faster than it does in steady state.
            Assert.Less(LargestJump(phases, 1), Frame / 3.4f * 1.5f,
                        "a resume left surplus time to burn off over the frames after it");
        }

        /// <summary>
        /// The clamp alone is not enough and the wrap alone is not enough, so this drives a
        /// loop shorter than <see cref="TweenCycle.MaxStep"/> with an enormous step — the
        /// case where a clamped step still covers whole cycles.
        /// </summary>
        [Test]
        public void ALoopShorterThanTheClampSurvivesAnEnormousStep()
        {
            var phases = Play(.1f, -1, true, 60, firstStep: 3600f);

            foreach (float p in phases) Assert.That(p, Is.InRange(0f, 1f));

            // Whatever the surplus was, it is gone by the next frame rather than draining
            // one cycle at a time — so the tween is running normally immediately.
            var after = TweenCycle.Advance(0f, TweenCycle.Step(3600f), .1f, -1, true);
            Assert.Less(after.Elapsed, .2f, "surplus was left in elapsed to burn off later");
        }

        [Test]
        public void ABrokenFrameCostsATweenNothing()
        {
            Assert.AreEqual(0f, TweenCycle.Step(float.NaN));
            Assert.AreEqual(0f, TweenCycle.Step(float.PositiveInfinity));
            Assert.AreEqual(0f, TweenCycle.Step(-4f));
            Assert.AreEqual(TweenCycle.MaxStep, TweenCycle.Step(90f));
            Assert.AreEqual(Frame, TweenCycle.Step(Frame), 1e-6f);
        }

        // ----------------------------------------------------------- the other modes

        /// <summary>
        /// A plain loop is meant to saw — <c>HomeScreen</c>'s beacon ring is written that way
        /// on purpose, so that it rests between steps. The fix must not have turned every
        /// loop into a ping-pong.
        /// </summary>
        [Test]
        public void APlainLoopStillRestartsRatherThanReturning()
        {
            var phases = Play(2.4f, -1, false, 600);

            bool sawTheSnap = false;
            for (int i = 1; i < phases.Length; i++)
                if (phases[i] < phases[i - 1] - .5f) sawTheSnap = true;

            Assert.IsTrue(sawTheSnap, "a non-ping-pong loop should restart at the bottom");
            foreach (float p in phases) Assert.That(p, Is.InRange(0f, 1f));
        }

        [Test]
        public void AOneShotEndsOnceAtTheFarEnd()
        {
            float elapsed = 0f;
            int finishes = 0;
            float last = 0f;

            for (int i = 0; i < 120; i++)
            {
                var f = TweenCycle.Advance(elapsed, TweenCycle.Step(Frame), 1f, 0, false);
                elapsed = f.Elapsed;
                last = f.Phase;
                if (f.Finished) { finishes++; break; }
            }

            Assert.AreEqual(1, finishes, "a one-shot must finish exactly once");
            Assert.AreEqual(1f, last, 1e-4f, "and must be applied at its end before it dies");
        }

        /// <summary>
        /// Nothing in the game asks for a finite loop count today, which is exactly why it is
        /// pinned: the next thing that does will be trusting arithmetic no screen exercises.
        /// </summary>
        [Test]
        public void AFiniteLoopRunsEveryCycleItWasAskedFor()
        {
            float elapsed = 0f;
            int loops = 3, frames = 0;

            while (frames < 6000)
            {
                var f = TweenCycle.Advance(elapsed, TweenCycle.Step(Frame), 1f, loops, false);
                elapsed = f.Elapsed;
                loops = f.Loops;
                frames++;
                if (f.Finished) break;
            }

            Assert.AreEqual(0, loops);

            // Three wraps and the final traverse: four seconds, not three and not seven.
            Assert.That(frames / 60f, Is.EqualTo(4f).Within(.05f));
        }

        /// <summary>
        /// A step large enough to cover more cycles than are owed must not overshoot into
        /// a phase the tween was never meant to rest at.
        /// </summary>
        [Test]
        public void AFiniteLoopIsNotCutShortByOneHugeStep()
        {
            var f = TweenCycle.Advance(0f, TweenCycle.Step(3600f), .05f, 2, false);

            Assert.AreEqual(0, f.Loops);
            Assert.That(f.Phase, Is.InRange(0f, 1f));
            Assert.IsTrue(f.Finished, "the loops were spent, so it should be reporting done");
        }
    }
}
