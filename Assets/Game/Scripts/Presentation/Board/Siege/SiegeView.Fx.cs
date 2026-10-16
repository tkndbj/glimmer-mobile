using System.Collections.Generic;
using GlimmerGrove.Shuffle;
using UnityEngine;

namespace GlimmerGrove
{
    public sealed partial class SiegeView
    {
        ShuffleFx _batchFx;
        List<Vector2> _arcTrunk, _arcBranch;
        List<FxParticle> _arcQuads;

        void BuildBatches()
        {
            if (_batchFx != null) _batchFx.ClearParticles();
            _batchFx = null;
            // A presentation boundary, not an upgrade test: the first unupgraded Shuffle
            // wave uses the same renderer as its last. Chapters and Infinite keep their path.
            if (!(_layout.Ramp is ShuffleRamp)) return;
            PrepareArcBuffers();
            _batchFx = ShuffleFx.Attach(Field);
            _batchFx.Prepare(Art.Spark(64));
            _batchFx.Prepare(Art.Glow(96));
            _batchFx.Prepare(Art.Glow(128, 2.4f));
            _batchFx.Prepare(Art.Ring(128, 8f));
            _batchFx.Prepare(Art.SoftCapsule(40, 120));
            _batchFx.Prepare(Art.Capsule(24, 96));
            _batchFx.Prepare(Art.Round(4));
        }

        void FxPop(Vector2 at, Color tint, float size = 1.8f, float seconds = .34f)
        {
            if (_batchFx == null) { Pop(at, tint, size, seconds); return; }
            _batchFx.Emit(Field, Art.Glow(96), null, new FxParticle
            {
                Motion = FxMotion.Pop, At = at, Size = Vector2.one * Cell,
                Tint = Pal.A(tint, .9f), Scale = size, Duration = seconds,
            });
        }

        void FxWave(Vector2 at, Color tint, float to, float seconds)
        {
            if (_batchFx == null) { Shockwave(at, tint, to, seconds); return; }
            _batchFx.Emit(Field, Art.Ring(128, 8f), null, new FxParticle
            {
                Motion = FxMotion.Wave, At = at, Size = Vector2.one * Cell,
                Tint = Pal.A(tint, .8f), Scale = to, Duration = seconds,
            });
        }

        void FxSparks(Transform parent, Vector2 at, Color colour, int count = 14,
                      float power = 190f, float size = 26f, float life = .62f)
        {
            if (_batchFx == null || !(parent is RectTransform layer))
            {
                Burst.Sparks(parent, at, colour, count, power, size, life);
                return;
            }
            var sprite = Art.Spark(64);
            for (int i = 0; i < count; i++)
            {
                // Keep the old random draw order as well as its distributions.
                float angle = i / (float)count * Mathf.PI * 2f + Random.Range(-.22f, .22f);
                float speed = power * Random.Range(.55f, 1.3f);
                float wide = size * Random.Range(.55f, 1.35f);
                float spin = Random.Range(-260f, 260f);
                float duration = life * Random.Range(.75f, 1.25f);
                _batchFx.Emit(layer, sprite, null, new FxParticle
                {
                    Motion = FxMotion.Spark, At = at, Size = Vector2.one * wide,
                    Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed,
                    Tint = Pal.A(Pal.Lift(colour, .35f), 1f), Spin = spin, Duration = duration,
                });
            }
            _batchFx.Emit(layer, Art.Glow(128, 2.4f), null, new FxParticle
            {
                Motion = FxMotion.Core, At = at, Size = Vector2.one * size * 5f,
                Tint = Pal.A(Pal.Lift(colour, .5f), .9f), Duration = .42f,
            });
        }

        void BatchCinder(Vector2 at, Color tint, float size, float life, Vector2 drift)
        {
            _batchFx.Emit(_fx, Art.Spark(64), null, new FxParticle
            {
                Motion = FxMotion.Cinder, At = at, Size = Vector2.one * size,
                Tint = Pal.A(Pal.Lift(tint, .4f), 1f), Velocity = drift, Duration = life,
            });
        }

        void BatchArc(Vector2 a, Vector2 b, Color tint, float wide, float life, int forks, float jag)
        {
            PrepareArcBuffers();
            _arcQuads.Clear();
            ArcJoints(_arcTrunk, a, b, jag);
            ArcTrace(_arcTrunk, tint, wide);
            for (int f = 0; f < forks; f++)
            {
                int at = Random.Range(1, Mathf.Max(2, _arcTrunk.Count - 1));
                var root = _arcTrunk[at];
                var away = (b - a).normalized;
                float turn = Random.Range(.5f, 1.3f) * (Random.value < .5f ? -1f : 1f);
                var side = new Vector2(-away.y, away.x);
                var tip = root + (away + side * turn).normalized * (b - a).magnitude * Random.Range(.22f, .42f);
                ArcJoints(_arcBranch, root, tip, jag * 1.4f);
                ArcTrace(_arcBranch, tint, wide * .58f);
            }
            float phase = Random.Range(0f, 40f);
            var halo = Art.SoftCapsule(40, 120);
            var capsule = Art.Capsule(24, 96);
            for (int i = 0; i < _arcQuads.Count; i++)
            {
                var particle = _arcQuads[i];
                particle.Duration = life;
                particle.Phase = phase;
                // The halo, sheath and core remain in segment order, with ordinary alpha.
                _batchFx.Emit(_fx, i % 3 == 0 ? halo : capsule, null, particle);
            }
        }

        void ArcJoints(List<Vector2> into, Vector2 a, Vector2 b, float jag)
        {
            into.Clear();
            var dir = b - a;
            float length = dir.magnitude;
            int steps = Mathf.Clamp(Mathf.RoundToInt(length / (Cell * .7f)), 3, ArcSegments);
            var side = length > .001f ? new Vector2(-dir.y, dir.x) / length : Vector2.right;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float push = Random.Range(-1f, 1f) * jag * Cell * Mathf.Sin(t * Mathf.PI);
                into.Add(Vector2.Lerp(a, b, t) + side * push);
            }
        }

        void ArcTrace(List<Vector2> joints, Color tint, float wide)
        {
            for (int i = 0; i + 1 < joints.Count; i++)
            {
                var dir = joints[i + 1] - joints[i];
                float length = dir.magnitude;
                if (length < .5f) continue;
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
                var at = (joints[i] + joints[i + 1]) * .5f;
                ArcQuad(at, new Vector2(wide * 4.2f, length + wide * 3.2f), Pal.A(tint, .55f), angle);
                ArcQuad(at, new Vector2(wide * 2f, length + wide * 1.2f), Pal.A(tint, .95f), angle);
                ArcQuad(at, new Vector2(wide, length + wide * .6f), Pal.A(Color.Lerp(tint, Color.white, .62f), 1f), angle);
            }
        }

        void ArcQuad(Vector2 at, Vector2 size, Color tint, float angle)
            => _arcQuads.Add(new FxParticle { Motion = FxMotion.Arc, At = at, Size = size, Tint = tint, Angle = angle });

        void PrepareArcBuffers()
        {
            if (_arcTrunk != null) return;
            _arcTrunk = new List<Vector2>(11);
            _arcBranch = new List<Vector2>(11);
            _arcQuads = new List<FxParticle>(96);
        }

        void BatchFlash(Vector2 at, Color tint, float cells)
        {
            var flare = Kit(AssetPipeline.StrikeFx.Flare);
            if (flare != null)
                _batchFx.Emit(_fx, flare, Additive.Material, new FxParticle
                {
                    Motion = FxMotion.Flare, At = at, Size = Vector2.one * Cell * cells,
                    Tint = Pal.A(tint, 1f), Angle = Random.Range(-14f, 14f), Duration = .42f,
                });
            var streak = Kit(AssetPipeline.StrikeFx.Streak);
            if (streak != null)
            {
                float wide = Cell * cells * 1.6f;
                _batchFx.Emit(_fx, streak, Additive.Material, new FxParticle
                {
                    Motion = FxMotion.Streak, At = at,
                    Size = new Vector2(wide, wide * streak.rect.height / streak.rect.width),
                    Tint = Pal.A(Color.Lerp(tint, Color.white, .5f), 1f), Duration = .24f,
                });
            }
            var glint = Kit(AssetPipeline.StrikeFx.Glint);
            if (glint != null)
                _batchFx.Emit(_fx, glint, Additive.Material, new FxParticle
                {
                    Motion = FxMotion.Glint, At = at, Size = Vector2.one * Cell * cells * .55f,
                    Tint = Pal.A(Color.white, .95f), Spin = Random.Range(-1f, 1f) < 0f ? -50f : 50f,
                    Duration = .32f,
                });
        }

        void BatchEmbers(Vector2 at, Color tint, int count, float reach, float size)
        {
            var sprite = Kit(AssetPipeline.StrikeFx.Star) ?? Art.Spark(64);
            for (int i = 0; i < count; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float speed = reach * Random.Range(1.4f, 3.2f);
                var velocity = new Vector2(Mathf.Cos(angle) * speed * .8f, Mathf.Sin(angle) * speed * .5f + reach * 2.2f);
                float life = Random.Range(.42f, .78f);
                float wide = size * Random.Range(.6f, 1.4f);
                _batchFx.Emit(_fx, sprite, Additive.Material, new FxParticle
                {
                    Motion = FxMotion.Ember, At = at, Size = Vector2.one * wide, Tint = tint,
                    Velocity = velocity, Gravity = Cell * 9f, Scale = Cell * 6f, Duration = life,
                });
            }
        }

        void BatchTwinkles(Vector2 at, Color tint, int count, float spread)
        {
            var sprite = Kit(AssetPipeline.StrikeFx.Glint);
            for (int i = 0; i < count; i++)
            {
                var where = at + new Vector2(Random.Range(-spread, spread), Random.Range(-spread, spread) * GroundSquash);
                float size = Cell * Random.Range(.35f, .8f);
                float delay = Random.Range(0f, .28f);
                float spin = Random.Range(-40f, 40f);
                if (sprite == null) break;
                _batchFx.Emit(_fx, sprite, Additive.Material, new FxParticle
                {
                    Motion = FxMotion.Twinkle, At = where, Size = Vector2.one * size,
                    Tint = Color.Lerp(tint, Color.white, .6f), Delay = delay, Spin = spin, Duration = .24f,
                });
            }
        }
    }
}
