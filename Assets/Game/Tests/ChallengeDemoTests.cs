using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The arithmetic under the challenge previews (<see cref="ChallengeDemos"/>): the hand's
    /// beats and the loop's seam. Animation arithmetic is the one kind of fault a screenshot
    /// cannot show and the Editor is usually not running, so the clock is walked here
    /// (<c>CoachStrokeTests</c>' reason).
    /// </summary>
    public sealed class ChallengeDemoTests
    {
        static readonly Vector2 From = new Vector2(-100f, 0f), To = new Vector2(200f, 0f);

        [Test]
        public void TheHandIsInvisibleBeforeItsBeatAndAfterIt()
        {
            const float start = 1f, draw = .5f;

            Assert.AreEqual(0f, ChallengeDemos.Trace(.5f, start, From, To, draw).Alpha);
            Assert.AreEqual(0f, ChallengeDemos.Trace(ChallengeDemos.Gone(start, draw) + .01f, start, From, To, draw).Alpha);

            // And wholly present while it is pressing, so the board's reaction is never drawn
            // under a hand that is not there.
            Assert.AreEqual(1f, ChallengeDemos.Trace(ChallengeDemos.Pressed(start) + .01f, start, From, To, draw).Alpha);
        }

        [Test]
        public void ADragArrivesExactlyWhenItLetsGo()
        {
            const float start = .3f, draw = .55f;

            var pressed = ChallengeDemos.Trace(ChallengeDemos.Pressed(start), start, From, To, draw);
            Assert.AreEqual(0f, pressed.Along, 1e-4f, "the drag starts where the finger landed");
            Assert.AreEqual(From, pressed.At);

            var released = ChallengeDemos.Trace(ChallengeDemos.Released(start, draw) - 1e-4f, start, From, To, draw);
            Assert.AreEqual(1f, released.Along, 1e-2f, "the drag has reached its end when the finger lifts");

            // Monotone: a finger never goes back on itself mid-drag.
            float last = 0f;
            for (float t = ChallengeDemos.Pressed(start); t < ChallengeDemos.Released(start, draw); t += 1f / 60f)
            {
                float along = ChallengeDemos.Trace(t, start, From, To, draw).Along;
                Assert.GreaterOrEqual(along, last - 1e-6f);
                last = along;
            }
        }

        [Test]
        public void ATapHoldsWhereItLandedAndNeverTravels()
        {
            const float start = .3f;

            for (float t = ChallengeDemos.Pressed(start); t < ChallengeDemos.Gone(start, 0f); t += 1f / 60f)
            {
                var g = ChallengeDemos.Trace(t, start, From, From, 0f);
                Assert.AreEqual(From, g.At);
            }

            Assert.AreEqual(ChallengeDemos.Pressed(start) + ChallengeDemos.TapHold + ChallengeDemos.LiftFor,
                            ChallengeDemos.Gone(start, 0f), 1e-5f);
        }

        [Test]
        public void TheLoopFadesAtBothEndsAndIsWholeBetween()
        {
            const float cycle = 4f;

            Assert.AreEqual(0f, ChallengeDemos.SeamAlpha(0f, cycle), 1e-5f);
            Assert.AreEqual(0f, ChallengeDemos.SeamAlpha(cycle, cycle), 1e-5f);
            Assert.AreEqual(1f, ChallengeDemos.SeamAlpha(cycle * .5f, cycle));
            Assert.AreEqual(1f, ChallengeDemos.SeamAlpha(.5f, cycle));
        }

        [Test]
        public void EveryGenreHasADemonstration()
        {
            // The registry is a switch whose default refuses (44e): a genre added without a
            // demonstration must fail here rather than open a blank stage on a device. Building
            // one needs a canvas, so what is held offline is the map from genre to verb the
            // panel titles itself with; the stage itself is the Editor's.
            foreach (GlimmerGrove.Challenges.ChallengeGenre genre
                     in System.Enum.GetValues(typeof(GlimmerGrove.Challenges.ChallengeGenre)))
                Assert.IsTrue(GlimmerGrove.Progression.Mechanic.ChallengeVerb(genre).IsValid, genre.ToString());
        }
    }
}
