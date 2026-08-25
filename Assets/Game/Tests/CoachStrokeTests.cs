using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The coaching hand, run offline a frame at a time.
    ///
    /// <para>
    /// This suite exists for the reason <c>TweenCycleTests</c> does, and the failures it is
    /// written against are the same shape: a wrong number here is not a wrong pixel, it is a
    /// wrong <em>motion</em>. A hand that arrives before it has faded in, ink still standing
    /// from the previous repeat, a fingertip that jumps because a route's two legs were split
    /// evenly rather than by length — every one of those compiles, validates and reads
    /// perfectly in the source, and the Editor is usually not running.
    /// </para>
    /// <para>
    /// The gesture is only ever shown once in a player's life, on the two lessons a Lightweave
    /// board cannot demonstrate for itself. There is no second showing to catch it at.
    /// </para>
    /// </summary>
    public sealed class CoachStrokeTests
    {
        const float Frame = 1f / 60f;

        // ------------------------------------------------------------------ the pace
        [Test]
        public void AShortStrokeIsNeverFasterThanTheFloor()
        {
            // Two cells at the raw rate is a fifth of a second, which is not a hand travelling.
            Assert.AreEqual(CoachStroke.MinDraw, CoachStroke.DrawSeconds(2), 1e-4f);
            Assert.AreEqual(CoachStroke.MinDraw, CoachStroke.DrawSeconds(1), 1e-4f);
        }

        [Test]
        public void ALongStrokeIsNeverSlowerThanTheCeiling()
        {
            // GroveGrowth's rule: a wider grove must not buy a longer wait before the sentence
            // can be read a second time. The rate is what gives way.
            Assert.AreEqual(CoachStroke.MaxDraw, CoachStroke.DrawSeconds(40), 1e-4f);
            Assert.AreEqual(CoachStroke.MaxDraw, CoachStroke.DrawSeconds(400), 1e-4f);
        }

        [Test]
        public void ADegenerateCellCountStillProducesAStroke()
        {
            // A pair standing on adjacent cells, or a caller that has nothing to say about
            // distance. Neither may divide by zero or produce an instantaneous gesture.
            Assert.Greater(CoachStroke.DrawSeconds(0), 0f);
            Assert.Greater(CoachStroke.DrawSeconds(-3), 0f);
        }

        [Test]
        public void ThePaceRisesWithDistanceBetweenTheFloorAndTheCeiling()
        {
            float shorter = CoachStroke.DrawSeconds(7);
            float longer = CoachStroke.DrawSeconds(11);

            Assert.Greater(longer, shorter);
            Assert.LessOrEqual(longer, CoachStroke.MaxDraw);
        }

        // ------------------------------------------------------------------ the gesture
        [Test]
        public void TheHandFadesInBeforeItPresses()
        {
            float draw = CoachStroke.DrawSeconds(6);

            var opening = CoachStroke.At(0f, draw);
            Assert.AreEqual(0f, opening.Alpha, 1e-4f, "arrives already visible");
            Assert.AreEqual(0f, opening.Press, 1e-4f, "presses before it has landed");
            Assert.AreEqual(1f, opening.Lift, 1e-4f, "starts on the board rather than above it");

            var landed = CoachStroke.At(CoachStroke.ReachSeconds, draw);
            Assert.AreEqual(1f, landed.Alpha, 1e-3f);
            Assert.AreEqual(0f, landed.Lift, 1e-3f);
        }

        [Test]
        public void NothingIsDrawnUntilTheFingerHasPressed()
        {
            float draw = CoachStroke.DrawSeconds(6);
            float untilDraw = CoachStroke.ReachSeconds + CoachStroke.PressSeconds;

            for (float t = 0f; t < untilDraw - 1e-3f; t += Frame)
            {
                var beat = CoachStroke.At(t, draw);
                Assert.AreEqual(0f, beat.Along, 1e-4f, "moved at " + t);
                Assert.AreEqual(0f, beat.Trail, 1e-4f, "inked at " + t);
            }

            Assert.AreEqual(1f, CoachStroke.At(untilDraw, draw).Press, 1e-3f);
        }

        [Test]
        public void TheFingertipOnlyEverGoesForwards()
        {
            float draw = CoachStroke.DrawSeconds(9);
            float cycle = CoachStroke.Cycle(draw);
            float last = -1f;

            for (float t = 0f; t <= cycle; t += Frame)
            {
                var beat = CoachStroke.At(t, draw);

                Assert.GreaterOrEqual(beat.Along + 1e-4f, last, "went backwards at " + t);
                Assert.GreaterOrEqual(beat.Along, 0f);
                Assert.LessOrEqual(beat.Along, 1f);
                last = beat.Along;
            }

            Assert.AreEqual(1f, last, 1e-3f, "never arrived");
        }

        [Test]
        public void TheInkFollowsTheFingertipAndNeverLeadsIt()
        {
            float draw = CoachStroke.DrawSeconds(9);

            for (float t = 0f; t <= CoachStroke.Cycle(draw); t += Frame)
            {
                var beat = CoachStroke.At(t, draw);
                Assert.LessOrEqual(beat.Trail, beat.Along + 1e-4f, "ink ran ahead at " + t);
            }
        }

        [Test]
        public void TheHandIsGoneAndTheInkIsClearedBeforeItHappensAgain()
        {
            // The one that matters most: a repeat starting on top of the last stroke's ink
            // would say the route may be drawn twice, and a hand still visible at the end of
            // the cycle would jump across the board on the loop.
            float draw = CoachStroke.DrawSeconds(5);
            var last = CoachStroke.At(CoachStroke.Cycle(draw), draw);

            Assert.AreEqual(0f, last.Alpha, 1e-3f, "still holding the hand");
            Assert.AreEqual(0f, last.TrailAlpha, 1e-3f, "still holding the ink");

            var first = CoachStroke.At(0f, draw);
            Assert.AreEqual(first.Alpha, last.Alpha, 1e-3f);
        }

        [Test]
        public void TheInkStandsForABeatBeforeItClears()
        {
            // Cleared the instant the hand lifts, the route could never be read as a whole.
            float draw = CoachStroke.DrawSeconds(5);
            float lifted = CoachStroke.ReachSeconds + CoachStroke.PressSeconds + draw
                         + CoachStroke.LiftSeconds;

            Assert.AreEqual(1f, CoachStroke.At(lifted + 1e-3f, draw).TrailAlpha, 1e-3f);
            Assert.Greater(CoachStroke.RestSeconds, CoachStroke.TrailFadeSeconds);
        }

        [Test]
        public void EveryValueStaysInsideItsOwnRange()
        {
            float draw = CoachStroke.DrawSeconds(12);

            for (float t = -1f; t <= CoachStroke.Cycle(draw) + 1f; t += Frame)
            {
                var beat = CoachStroke.At(t, draw);

                Assert.That(beat.Alpha, Is.InRange(0f, 1f));
                Assert.That(beat.Press, Is.InRange(0f, 1f));
                Assert.That(beat.Lift, Is.InRange(0f, 1f));
                Assert.That(beat.Trail, Is.InRange(0f, 1f));
                Assert.That(beat.TrailAlpha, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void ADrawTimeOutsideTheBoundsIsClampedRatherThanTrusted()
        {
            // Cycle and At have to agree about how long the stroke is, or the loop reads the
            // rest phase off the end of its own period.
            Assert.AreEqual(CoachStroke.Cycle(CoachStroke.MinDraw), CoachStroke.Cycle(0f), 1e-4f);
            Assert.AreEqual(1f, CoachStroke.At(CoachStroke.Cycle(0f), 0f).Along, 1e-3f);
        }

        // ------------------------------------------------------------------ the route
        [Test]
        public void ARouteIsWalkedByLengthAndNotBySegment()
        {
            // The bead lesson's two legs are deliberately different lengths. Split evenly, the
            // fingertip crawls across the short one and races across the long one.
            var lengths = new[] { 30f, 90f };

            Assert.IsTrue(CoachStroke.Walk(lengths, .25f, out int seg, out float f));
            Assert.AreEqual(0, seg);
            Assert.AreEqual(1f, f, 1e-4f, "quarter of the way is the end of the first leg");

            Assert.IsTrue(CoachStroke.Walk(lengths, .5f, out seg, out f));
            Assert.AreEqual(1, seg);
            Assert.AreEqual(1f / 3f, f, 1e-4f);
        }

        [Test]
        public void TheEndsOfARouteAreExact()
        {
            var lengths = new[] { 40f, 60f, 20f };

            Assert.IsTrue(CoachStroke.Walk(lengths, 0f, out int seg, out float f));
            Assert.AreEqual(0, seg);
            Assert.AreEqual(0f, f, 1e-4f);

            Assert.IsTrue(CoachStroke.Walk(lengths, 1f, out seg, out f));
            Assert.AreEqual(2, seg);
            Assert.AreEqual(1f, f, 1e-4f);

            // Past either end, so floating-point slop in a driving tween cannot index off it.
            Assert.IsTrue(CoachStroke.Walk(lengths, 1.4f, out seg, out f));
            Assert.AreEqual(2, seg);
            Assert.IsTrue(CoachStroke.Walk(lengths, -.2f, out seg, out f));
            Assert.AreEqual(0, seg);
        }

        [Test]
        public void ARouteWithNoLengthIsRefusedRatherThanDividedBy()
        {
            Assert.IsFalse(CoachStroke.Walk(null, .5f, out _, out _));
            Assert.IsFalse(CoachStroke.Walk(new float[0], .5f, out _, out _));
            Assert.IsFalse(CoachStroke.Walk(new[] { 0f, 0f }, .5f, out _, out _));
        }

        [Test]
        public void AZeroLengthLegIsSteppedOverRatherThanLandedOn()
        {
            // Two board things on the same cell — a bead whose neighbour is the cell it stands
            // on would produce one, and the fingertip must not stall there for a third of the
            // stroke.
            var lengths = new[] { 50f, 0f, 50f };

            // Landing on the junction is right; landing *inside* the empty leg is what would
            // stall the fingertip, and there is no fraction of a zero-length leg to land at.
            Assert.IsTrue(CoachStroke.Walk(lengths, .5f, out int seg, out float f));
            Assert.AreEqual(0, seg);
            Assert.AreEqual(1f, f, 1e-4f);

            Assert.IsTrue(CoachStroke.Walk(lengths, .75f, out seg, out f));
            Assert.AreEqual(2, seg);
            Assert.AreEqual(.5f, f, 1e-4f);
        }
    }
}
