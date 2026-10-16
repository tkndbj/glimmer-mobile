using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>The existing siege particle curves, evaluated without a transform or a tween.</summary>
    public enum FxMotion { Pop, Wave, Core, Spark, Cinder, Ember, Arc, Ripple, Twinkle, Flare, Streak, Glint, Light }

    public struct FxParticle
    {
        public FxMotion Motion;
        public Vector2 At, Size, Velocity;
        public Color Tint;
        public float Duration, Delay, Angle, Spin, Scale, Gravity, Phase;
    }

    public struct FxPose
    {
        public Vector2 At, Size;
        public Color Tint;
        public float Angle;
    }

    public static class FxAnimation
    {
        /// <summary>
        /// Seconds are read from Tween.Clock, including the delay. The creation frame retains
        /// the old Image's initial state; a newly emitted particle must not run a frame early.
        /// </summary>
        public static FxPose Evaluate(FxParticle p, float seconds)
        {
            var pose = new FxPose { At = p.At, Size = p.Size, Tint = p.Tint, Angle = p.Angle };
            float age = seconds - p.Delay;
            if (age <= 0f)
            {
                switch (p.Motion)
                {
                    case FxMotion.Pop: pose.Size *= .4f; break;
                    case FxMotion.Cinder:
                    case FxMotion.Arc:
                    case FxMotion.Ripple: pose.Tint.a = 0f; break;
                    case FxMotion.Twinkle: pose.Size = Vector2.zero; pose.Tint.a = 0f; break;
                    case FxMotion.Ember: pose.Tint = Pal.A(Color.Lerp(p.Tint, Color.white, .45f), 1f); break;
                }
                return pose;
            }

            float u = Mathf.Clamp01(age / Mathf.Max(.0001f, p.Duration));
            float t;
            switch (p.Motion)
            {
                case FxMotion.Pop:
                case FxMotion.Wave:
                    pose.Size *= Mathf.Lerp(p.Motion == FxMotion.Pop ? .4f : 1f, p.Scale, Ease.OutCubic(u));
                    pose.Tint.a *= 1f - Ease.OutQuad(u);
                    break;
                case FxMotion.Core:
                    t = Ease.OutQuint(u);
                    pose.Size *= Mathf.Lerp(.35f, 1.6f, t);
                    pose.Tint.a *= 1f - t;
                    break;
                case FxMotion.Spark:
                    t = Ease.OutCubic(u);
                    pose.At += p.Velocity * t;
                    pose.Size *= Mathf.Lerp(1.15f, .05f, t * t);
                    pose.Angle = p.Spin * t;
                    pose.Tint.a *= 1f - t * t;
                    break;
                case FxMotion.Cinder:
                    t = Ease.OutQuad(u);
                    pose.At += p.Velocity * t;
                    pose.Size *= Mathf.Lerp(1f, .15f, t * t);
                    pose.Tint.a *= 1f - t * t;
                    break;
                case FxMotion.Ember:
                    var velocity = new Vector2(p.Velocity.x, p.Velocity.y - p.Gravity * age);
                    pose.At += new Vector2(p.Velocity.x * age, p.Velocity.y * age - .5f * p.Gravity * age * age);
                    pose.Angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg - 90f;
                    float narrow = Mathf.Lerp(1f, .3f, u);
                    pose.Size = Vector2.Scale(p.Size, new Vector2(narrow, Mathf.Lerp(1f, .5f, u) * (1f + velocity.magnitude / p.Scale)));
                    pose.Tint = Pal.A(Color.Lerp(Color.white, p.Tint, u), 1f - u * u);
                    break;
                case FxMotion.Arc:
                    float envelope = u < .2f ? 1f : 1f - (u - .2f) / .8f;
                    pose.Tint.a *= envelope * (.58f + .42f * Mathf.Abs(Mathf.Sin(u * 34f + p.Phase)));
                    break;
                case FxMotion.Ripple:
                    t = Ease.OutQuint(u);
                    pose.Size *= Mathf.Lerp(.12f, 1f, t);
                    float alpha = t < .12f ? t / .12f : 1f - (t - .12f) / .88f;
                    pose.Tint = Pal.A(Color.Lerp(Color.white, p.Tint, Mathf.Clamp01(t * 3f)), alpha * alpha);
                    break;
                case FxMotion.Twinkle:
                    t = Mathf.Sin(u * Mathf.PI);
                    pose.Size *= t;
                    pose.Angle = p.Spin * u;
                    pose.Tint.a = t;
                    break;
                case FxMotion.Flare:
                    pose.Size *= Mathf.Lerp(.3f, 1f, Ease.OutQuint(Mathf.Clamp01(u / .3f)));
                    pose.Tint = Pal.A(Color.Lerp(Color.Lerp(p.Tint, Color.white, .6f), p.Tint, Mathf.Clamp01(u * 2f)), 1f - u * u);
                    break;
                case FxMotion.Streak:
                    pose.Size = Vector2.Scale(p.Size, new Vector2(Mathf.Lerp(.45f, 1.1f, Ease.OutQuint(u)), 1f - u * .5f));
                    pose.Tint.a *= 1f - u;
                    break;
                case FxMotion.Glint:
                    pose.Size *= Mathf.Lerp(.5f, 1.25f, Ease.OutQuad(u));
                    pose.Angle = 25f + p.Spin * u;
                    pose.Tint.a *= 1f - u;
                    break;
                case FxMotion.Light:
                    pose.Tint.a *= 1f - Ease.OutQuad(u);
                    break;
            }
            return pose;
        }
    }
}
