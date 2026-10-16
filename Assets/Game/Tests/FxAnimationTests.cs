using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    public sealed class FxAnimationTests
    {
        [TestCase(.25f)]
        [TestCase(.5f)]
        [TestCase(.9f)]
        public void EmberKeepsTheOriginalIndependentWidthAndHeightCurves(float age)
        {
            var particle = new FxParticle
            {
                Motion = FxMotion.Ember, Size = new Vector2(20f, 20f), Duration = 1f,
                Velocity = new Vector2(30f, 70f), Gravity = 40f, Scale = 60f, Tint = Pal.Ember,
            };
            var pose = FxAnimation.Evaluate(particle, age);
            float speed = new Vector2(30f, 70f - 40f * age).magnitude;
            Assert.AreEqual(20f * Mathf.Lerp(1f, .3f, age), pose.Size.x, 1e-5f);
            Assert.AreEqual(20f * (1f + speed / 60f) * Mathf.Lerp(1f, .5f, age), pose.Size.y, 1e-5f);
        }

        [Test]
        public void CinderUsesEasedTimeForBothTravelAndTheSquaredFade()
        {
            var p = new FxParticle
            {
                Motion = FxMotion.Cinder, At = new Vector2(10f, 20f), Size = Vector2.one * 40f,
                Velocity = new Vector2(80f, 120f), Duration = 1f, Tint = Color.white,
            };
            var pose = FxAnimation.Evaluate(p, .25f);
            Assert.AreEqual(45f, pose.At.x, 1e-5f);
            Assert.AreEqual(72.5f, pose.At.y, 1e-5f);
            Assert.AreEqual(40f * (1f - .85f * .4375f * .4375f), pose.Size.x, 1e-5f);
            Assert.AreEqual(1f - .4375f * .4375f, pose.Tint.a, 1e-5f);
        }

        [Test]
        public void PopKeepsItsSeparateScaleAndFadeCurves()
        {
            var pose = FxAnimation.Evaluate(new FxParticle
            {
                Motion = FxMotion.Pop, Size = Vector2.one * 100f, Scale = 2f,
                Tint = new Color(1f, 1f, 1f, .9f), Duration = 1f,
            }, .5f);
            Assert.AreEqual(180f, pose.Size.x, 1e-4f);
            Assert.AreEqual(.225f, pose.Tint.a, 1e-5f);
        }

        [Test]
        public void CreationFrameAndDelayDoNotAdvanceTheAnimation()
        {
            var p = new FxParticle { Motion = FxMotion.Core, Size = Vector2.one * 50f, Tint = Color.white, Duration = 1f };
            Assert.AreEqual(50f, FxAnimation.Evaluate(p, 0f).Size.x);
            p.Motion = FxMotion.Cinder;
            Assert.AreEqual(0f, FxAnimation.Evaluate(p, 0f).Tint.a);
            p.Motion = FxMotion.Twinkle;
            p.Delay = .2f;
            Assert.AreEqual(Vector2.zero, FxAnimation.Evaluate(p, .1f).Size);
            Assert.AreEqual(0f, FxAnimation.Evaluate(p, .2f).Tint.a);
            Assert.Greater(FxAnimation.Evaluate(p, .3f).Tint.a, 0f);
        }

        [Test]
        public void EveryMotionFinishesTransparentAndHasFiniteGeometry()
        {
            foreach (FxMotion motion in System.Enum.GetValues(typeof(FxMotion)))
            {
                var p = new FxParticle
                {
                    Motion = motion, Size = new Vector2(40f, 80f), Tint = Color.white,
                    Duration = 1f, Scale = 100f, Velocity = new Vector2(30f, 60f), Gravity = 40f,
                };
                for (int frame = 0; frame <= 60; frame++)
                {
                    var pose = FxAnimation.Evaluate(p, frame / 60f);
                    Assert.IsFalse(float.IsNaN(pose.At.x) || float.IsInfinity(pose.At.x));
                    Assert.IsFalse(float.IsNaN(pose.Size.y) || float.IsInfinity(pose.Size.y));
                    if (frame == 60) Assert.LessOrEqual(pose.Tint.a, 1e-5f, motion.ToString());
                }
            }
        }
    }
}
