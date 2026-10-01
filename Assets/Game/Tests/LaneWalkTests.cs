using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The walk a challenge raider makes down its lane (<see cref="LaneWalk"/>), driven a frame
    /// at a time.
    ///
    /// <para>
    /// <b>This is the fixture for a fault no gate could see.</b> A raider stepping forward
    /// and back on a device (2026-09-30) was two writers of one position disagreeing across
    /// frames - a superseded tween and a repaint - and a screenshot of any one frame was
    /// correct. What is held here is the shape of the motion over frames: it never crosses
    /// its goal, it never moves away from it, a second step in mid-stride carries the first's
    /// momentum, a long frame lands short and never past, and a step is over by the time the
    /// hill's replay moves on. Every clause was proved by mutation before it was trusted.
    /// </para>
    /// </summary>
    public sealed class LaneWalkTests
    {
        const float Sixty = 1f / 60f, Thirty = 1f / 30f;

        /// <summary>
        /// Drive the walk for <paramref name="seconds"/> at a fixed frame length, asserting on
        /// every frame that the position stayed between where it was and where it is going.
        /// Returns the seconds elapsed when the walk first reported arrived, or -1.
        /// </summary>
        static float Drive(ref float at, ref float speed, float goal, float dt, float seconds)
        {
            float arrivedAt = -1f, clock = 0f;
            while (clock < seconds)
            {
                float before = at;
                bool arrived = LaneWalk.Advance(ref at, ref speed, goal, dt);
                clock += dt;

                Between(before, goal, at);
                Assert.IsFalse(float.IsNaN(at) || float.IsNaN(speed), "the walk went non-finite");

                if (arrived && arrivedAt < 0f) arrivedAt = clock;
                if (arrived) Assert.IsTrue(LaneWalk.Arrived(at, speed, goal), "arrived, but not standing on the goal");
            }
            return arrivedAt;
        }

        static void Between(float from, float goal, float now)
        {
            float lo = from < goal ? from : goal, hi = from < goal ? goal : from;
            Assert.IsTrue(now >= lo - 1e-6f && now <= hi + 1e-6f,
                          $"the walk left [{lo:0.####}, {hi:0.####}] for {now:0.####}: it moved away from or past its goal");
        }

        [Test]
        public void AStepFromRestIsBehindItByTheNextBeat()
        {
            float at = 10f, speed = 0f;
            float arrived = Drive(ref at, ref speed, 9f, Sixty, ChallengeHillView.StepFor);

            Assert.LessOrEqual(at - 9f, .03f,
                               $"a step from rest still had {(at - 9f) * 100f:0.0}% of its stride to go when the replay's next beat began");

            // And it is over, not merely close, within a hair of that.
            float more = Drive(ref at, ref speed, 9f, Sixty, .12f);
            Assert.IsTrue(arrived >= 0f || more >= 0f, "a step from rest never reported arrived");
            Assert.IsTrue(LaneWalk.Arrived(at, speed, 9f), "arrived without standing exactly on the seat");
        }

        [Test]
        public void AWalkNeverCrossesOrLeavesItsGoalAtAnyFrameRate()
        {
            foreach (float dt in new[] { Sixty, Thirty, 1f / 24f, .1f })
            {
                float at = 12f, speed = 0f;
                float arrived = Drive(ref at, ref speed, 7f, dt, 3f);
                Assert.GreaterOrEqual(arrived, 0f, $"a five-step walk at {1f / dt:0}fps never arrived");
            }
        }

        [Test]
        public void ASecondStepMidStrideKeepsItsMomentum()
        {
            float at = 10f, speed = 0f;

            // The hurried replay's gap between beats: the first step is still in flight.
            Drive(ref at, ref speed, 9f, Sixty, ChallengeHillView.StepFor / ChallengeHillView.Hurry);
            Assert.Less(at, 10f, "the first step had not begun");
            Assert.Greater(at, 9f, "the first step was already over, so this proves nothing about mid-stride");
            float before = speed;
            Assert.Less(before, 0f, "not walking down the hill");

            // The second step lands: the next frame is at least as fast, and the same way.
            LaneWalk.Advance(ref at, ref speed, 8f, Sixty);
            Assert.LessOrEqual(speed, before,
                               $"a second step restarted the walk from rest ({before:0.###} -> {speed:0.###} steps/s)");

            float arrived = Drive(ref at, ref speed, 8f, Sixty, 1f);
            Assert.GreaterOrEqual(arrived, 0f, "the retargeted walk never arrived");
        }

        [Test]
        public void ALongFrameLandsShortOfTheGoalNeverPast()
        {
            // A hitch: the tween clock's cap is what the walk sees, and it lands short.
            float at = 10f, speed = 0f;
            LaneWalk.Advance(ref at, ref speed, 9f, 5f);
            Between(10f, 9f, at);
            Assert.Greater(at, 9f, "one capped frame finished a whole step, which is a snap");

            // Momentum into a near goal: this is where an integrated spring overshoots, and
            // where the clamp has to hold - the last frames of a fast stride.
            at = 9.05f; speed = -20f;
            LaneWalk.Advance(ref at, ref speed, 9f, Sixty);
            Assert.GreaterOrEqual(at, 9f, $"the walk crossed its goal to {at:0.####}");
            Assert.IsTrue(LaneWalk.Arrived(at, speed, 9f) || speed <= 0f, "past the goal the walk was still moving");
        }

        [Test]
        public void AGoalRaisedAgainstTheWalkStandsRatherThanRetreats()
        {
            float at = 10f, speed = 0f;
            Drive(ref at, ref speed, 9f, Sixty, .1f);
            Assert.Less(speed, 0f, "not in mid-stride");

            // The hill never does this (ChallengeHillView.Aim only lowers a goal); the rule
            // holds anyway, because a rule that holds only while its callers behave has a hole.
            // The goal is raised to just behind the walk, where its momentum outweighs the
            // spring's pull and an unguarded step carries it the wrong way.
            float before = at;
            float raised = at + .1f;
            LaneWalk.Advance(ref at, ref speed, raised, Sixty);
            Assert.GreaterOrEqual(at, before,
                                  $"the walk carried on away from a goal that had moved behind it ({before:0.####} -> {at:0.####})");

            float arrived = Drive(ref at, ref speed, raised, Sixty, 1f);
            Assert.GreaterOrEqual(arrived, 0f, "the walk never turned and arrived at the raised goal");
        }

        [Test]
        public void ABurstOfStepsIsOneStride()
        {
            float at = 10f, speed = 0f, clock = 0f;
            int goal = 10;

            // Six steps fifty milliseconds apart, faster than any could finish alone.
            while (goal > 4)
            {
                goal--;
                float lastAt = at;
                for (float t = 0f; t < .05f; t += Sixty)
                {
                    LaneWalk.Advance(ref at, ref speed, goal, Sixty);
                    Between(lastAt, goal, at);
                    lastAt = at;
                    clock += Sixty;
                }
            }

            float arrived = Drive(ref at, ref speed, 4f, Sixty, 1f);
            Assert.GreaterOrEqual(arrived, 0f, "the burst never arrived");
            Assert.LessOrEqual(arrived, ChallengeHillView.StepFor + .3f,
                               $"a burst took {arrived:0.00}s after its last step to settle, which is a raider still creeping under the next turn");
        }

        [Test]
        public void StandingOnTheGoalCostsNothingAndABrokenFrameMovesNothing()
        {
            float at = 6f, speed = 0f;
            Assert.IsTrue(LaneWalk.Advance(ref at, ref speed, 6f, Sixty));
            Assert.AreEqual(6f, at);
            Assert.AreEqual(0f, speed);

            at = 6f; speed = 0f;
            foreach (float dt in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                bool arrived = LaneWalk.Advance(ref at, ref speed, 5f, dt);
                Assert.IsFalse(arrived, $"a frame of {dt} arrived");
                Assert.AreEqual(6f, at, $"a frame of {dt} moved the walk");
                Assert.AreEqual(0f, speed, $"a frame of {dt} gave the walk speed");
            }
        }
    }
}
