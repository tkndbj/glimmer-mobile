using System;

namespace GlimmerGrove.Frames
{
    /// <summary>
    /// The life in a name frame: when the head nods, when the eyes catch the light, when a
    /// sheen runs the body. Pure arithmetic over a clock, so it can be proved offline and so
    /// two frames on one screen never move in step.
    ///
    /// <para>
    /// <b>Three motions, one rule each.</b> A <em>nod</em> is a lift, a drop and a settle — the
    /// anticipation is what makes it read as a creature deciding to move rather than a hinge
    /// being pulled; the settle is a damped spring, because a head that stops dead is a
    /// mechanism. Between nods the head <em>sways</em> by less than half a degree, which is the
    /// difference between a picture and a thing that breathes. The <em>eyes</em> flare on the
    /// drop and fade over most of a second, over a faint slow shimmer that never quite goes
    /// out. The <em>sheen</em> is a band of light that crosses the painting left to right every
    /// few seconds, on its own clock so it does not always land with a nod.
    /// </para>
    /// <para>
    /// <b>Sprite space is y up, so a nod is a negative turn</b> — the head bone points from the
    /// neck toward the snout, and turning it clockwise puts the snout down.
    /// </para>
    /// <para>
    /// Every interval is drawn from a stream seeded per instance (xorshift32, the chest
    /// generator's shape), so the trace is deterministic for a seed and different across seeds.
    /// </para>
    /// </summary>
    public sealed class FrameIdle
    {
        // Deepened from 1.6 / -5.5 on the first play-mode look (2026-09-21): at the sizes a frame
        // is drawn the head is under two hundred units across, and a five-degree nod moved the
        // snout six units, which read as a breath. Nine reads as a nod; the painting takes it
        // without tearing (`make_frame_rig.py --preview --degrees 9`).
        public const float LiftDegrees = 2.2f;
        public const float NodDegrees = -9f;
        public const float SwayDegrees = .45f;
        public const float SwayHz = .21f;

        public const float LiftSeconds = .26f;
        public const float DropSeconds = .17f;
        public const float SettleSeconds = .95f;
        public const float NodSeconds = LiftSeconds + DropSeconds + SettleSeconds;

        public const float NodEveryMin = 4.5f, NodEveryMax = 7.5f;
        public const float FirstNodMin = 1.2f, FirstNodMax = 2.4f;

        public const float ShineEveryMin = 3.2f, ShineEveryMax = 6.0f;
        public const float FirstShineMin = .6f, FirstShineMax = 1.8f;
        public const float ShineSeconds = 1.05f;

        /// <summary>The sheen enters and leaves fully: it runs from this to one minus this.</summary>
        public const float ShineOverrun = .2f;

        public const float SparkFade = .6f;
        public const float ShimmerFloor = .12f, ShimmerDepth = .08f, ShimmerHz = .35f;

        /// <summary>The head bone's turn from its bind pose, degrees, positive counter-clockwise.</summary>
        public float HeadDegrees { get; private set; }

        /// <summary>How brightly the eyes shine, nought to one.</summary>
        public float EyeGlow { get; private set; }

        /// <summary>
        /// Where the sheen is across the painting, from <c>-ShineOverrun</c> to
        /// <c>1 + ShineOverrun</c> while it runs, and <c>-1</c> when there is none.
        /// </summary>
        public float ShinePosition { get; private set; } = -1f;

        public float Elapsed { get; private set; }

        /// <summary>How many nods have begun. A gate counts these.</summary>
        public int Nods { get; private set; }

        /// <summary>How many sheens have begun.</summary>
        public int Shines { get; private set; }

        uint _state;
        float _nodAt, _nodStart = -1f;
        float _shineAt, _shineStart = -1f;
        float _spark;

        public FrameIdle(uint seed)
        {
            // Scrambled and warmed before the first draw: xorshift's first outputs from two
            // small seeds are two small numbers, and two frames seeded 1 and 2 would take
            // their first nod on the same frame.
            _state = seed ^ 0x9E3779B9u;
            if (_state == 0) _state = 0x9E3779B9u;
            for (int i = 0; i < 4; i++) Range(0f, 1f);

            _nodAt = Range(FirstNodMin, FirstNodMax);
            _shineAt = Range(FirstShineMin, FirstShineMax);
        }

        /// <summary>Move the clock on. Negative or absurd steps are refused rather than applied.</summary>
        public void Advance(float dt)
        {
            if (!(dt > 0f)) return;
            if (dt > .25f) dt = .25f;   // a hitch is not a quarter-second of animation
            Elapsed += dt;

            // -- the nod
            if (_nodStart < 0f && Elapsed >= _nodAt)
            {
                _nodStart = Elapsed;
                Nods++;
            }

            float nod = 0f;
            if (_nodStart >= 0f)
            {
                float u = Elapsed - _nodStart;
                if (u >= NodSeconds)
                {
                    _nodStart = -1f;
                    _nodAt = Elapsed + Range(NodEveryMin, NodEveryMax);
                }
                else
                {
                    nod = NodCurve(u);
                    // The eyes flare as the head comes down: the spark is lit through the drop.
                    if (u >= LiftSeconds && u < LiftSeconds + DropSeconds)
                        _spark = 1f;
                }
            }

            float sway = SwayDegrees * (float)Math.Sin(2.0 * Math.PI * SwayHz * Elapsed);
            HeadDegrees = nod + sway;

            // -- the eyes
            _spark *= (float)Math.Exp(-dt / SparkFade);
            float shimmer = ShimmerFloor + ShimmerDepth * (float)Math.Sin(2.0 * Math.PI * ShimmerHz * Elapsed);
            EyeGlow = Clamp01(shimmer + _spark);

            // -- the sheen
            if (_shineStart < 0f && Elapsed >= _shineAt)
            {
                _shineStart = Elapsed;
                Shines++;
            }

            if (_shineStart >= 0f)
            {
                float u = (Elapsed - _shineStart) / ShineSeconds;
                if (u >= 1f)
                {
                    _shineStart = -1f;
                    _shineAt = Elapsed + Range(ShineEveryMin, ShineEveryMax);
                    ShinePosition = -1f;
                }
                else
                {
                    float eased = u * u * (3f - 2f * u);
                    ShinePosition = -ShineOverrun + eased * (1f + 2f * ShineOverrun);
                }
            }
            else
            {
                ShinePosition = -1f;
            }
        }

        /// <summary>The head's turn through one nod, degrees, at <paramref name="u"/> seconds in.</summary>
        public static float NodCurve(float u)
        {
            if (u <= 0f) return 0f;

            if (u < LiftSeconds)
            {
                float t = u / LiftSeconds;
                return LiftDegrees * (1f - (1f - t) * (1f - t));          // ease out
            }

            u -= LiftSeconds;
            if (u < DropSeconds)
            {
                float t = u / DropSeconds;
                return LiftDegrees + (NodDegrees - LiftDegrees) * t * t * t; // ease in
            }

            u -= DropSeconds;
            if (u >= SettleSeconds) return 0f;

            // A damped spring back to rest: one small overshoot, then still.
            float s = u / SettleSeconds;
            return NodDegrees * (float)(Math.Exp(-4.5 * s) * Math.Cos(2.0 * Math.PI * 0.9 * s));
        }

        float Range(float min, float max)
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return min + (max - min) * (float)(_state / 4294967296.0);
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
