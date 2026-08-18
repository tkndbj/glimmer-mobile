using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    public delegate float Easing(float t);

    public static class Ease
    {
        public static readonly Easing Linear = t => t;
        public static readonly Easing InQuad = t => t * t;
        public static readonly Easing OutQuad = t => 1f - (1f - t) * (1f - t);
        public static readonly Easing InOutQuad = t => t < .5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * .5f;
        public static readonly Easing InCubic = t => t * t * t;
        public static readonly Easing OutCubic = t => 1f - Mathf.Pow(1f - t, 3f);
        public static readonly Easing InOutCubic = t => t < .5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * .5f;
        public static readonly Easing OutQuint = t => 1f - Mathf.Pow(1f - t, 5f);
        public static readonly Easing InOutSine = t => -(Mathf.Cos(Mathf.PI * t) - 1f) * .5f;
        public static readonly Easing OutSine = t => Mathf.Sin(t * Mathf.PI * .5f);

        public static readonly Easing OutBack = t =>
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        };

        public static readonly Easing OutBackSoft = t =>
        {
            const float c1 = 1.02f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        };

        public static readonly Easing InBack = t =>
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return c3 * t * t * t - c1 * t * t;
        };

        public static readonly Easing OutElastic = t =>
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            const float p = 2f * Mathf.PI / 3f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - .75f) * p) + 1f;
        };

        public static readonly Easing OutBounce = t =>
        {
            const float n1 = 7.5625f, d1 = 2.75f;
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + .75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + .9375f; }
            t -= 2.625f / d1; return n1 * t * t + .984375f;
        };
    }

    /// <summary>A single running interpolation. Dies with its owner.</summary>
    public sealed class Tw
    {
        internal float elapsed, delay, duration;
        internal Easing ease;
        internal Action<float> apply;
        internal Action done;
        internal UnityEngine.Object owner;
        internal string channel;
        internal bool unscaled = true;
        internal bool alive = true;
        internal int loops;          // 0 = once, -1 = forever
        internal bool pingPong;

        public Tw OnDone(Action a) { done = a; return this; }
        public Tw Delay(float d) { delay = d; return this; }
        public Tw Scaled() { unscaled = false; return this; }
        public Tw Loop(int count = -1, bool pingpong = true) { loops = count; pingPong = pingpong; return this; }
        public void Kill(bool complete = false)
        {
            if (!alive) return;
            alive = false;
            if (complete) { apply?.Invoke(ease(1f)); done?.Invoke(); }
        }
    }

    /// <summary>Tiny tween engine: no allocations per frame, auto-cleans dead owners.</summary>
    public sealed class Tween : MonoBehaviour
    {
        static Tween _inst;
        readonly List<Tw> _live = new List<Tw>(256);
        readonly List<Tw> _add = new List<Tw>(32);
        bool _iterating;

        public static Tween Inst
        {
            get
            {
                if (_inst == null)
                {
                    var go = new GameObject("~Tween");
                    DontDestroyOnLoad(go);
                    _inst = go.AddComponent<Tween>();
                }
                return _inst;
            }
        }

        // ------------------------------------------------------------------ core
        public static Tw Run(float duration, Easing ease, Action<float> apply,
                             UnityEngine.Object owner = null, string channel = null)
        {
            var t = new Tw
            {
                duration = Mathf.Max(0.0001f, duration),
                ease = ease ?? Ease.Linear,
                apply = apply,
                owner = owner,
                channel = channel
            };
            Inst.Add(t);
            return t;
        }

        public static Tw After(float delay, Action action, UnityEngine.Object owner = null)
            => Run(0.0001f, Ease.Linear, null, owner).Delay(delay).OnDone(action);

        void Add(Tw t)
        {
            if (t.channel != null) KillChannel(t.owner, t.channel);
            if (_iterating) _add.Add(t); else _live.Add(t);
        }

        public static void KillChannel(UnityEngine.Object owner, string channel)
        {
            if (_inst == null || owner == null) return;
            var list = _inst._live;
            for (int i = 0; i < list.Count; i++)
                if (list[i].alive && list[i].channel == channel && ReferenceEquals(list[i].owner, owner))
                    list[i].alive = false;
        }

        public static void KillAll(UnityEngine.Object owner)
        {
            if (_inst == null || owner == null) return;
            var list = _inst._live;
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i].owner, owner)) list[i].alive = false;
        }

        void Update()
        {
            // Clamped before anything reads them, because the phase arithmetic below is
            // only as sound as the step handed to it — see TweenCycle.MaxStep for why the
            // frame after a resume is not a frame's worth of time.
            float dt = TweenCycle.Step(Time.unscaledDeltaTime);
            float sdt = TweenCycle.Step(Time.deltaTime);
            _iterating = true;
            for (int i = 0; i < _live.Count; i++)
            {
                var t = _live[i];
                if (!t.alive) continue;
                if (t.owner != null && t.owner.Equals(null)) { t.alive = false; continue; }

                float step = t.unscaled ? dt : sdt;
                if (t.delay > 0f) { t.delay -= step; if (t.delay > 0f) continue; step = -t.delay; t.delay = 0f; }

                // The wrap, the loop count and the phase all live in TweenCycle, which is
                // plain arithmetic over no Unity types and is therefore the one part of the
                // animation system the test suite can run a thousand frames of offline. It
                // is the code that ships, not a description of it — this went wrong once
                // already and nothing but motion could have caught it.
                var frame = TweenCycle.Advance(t.elapsed, step, t.duration, t.loops, t.pingPong);

                t.elapsed = frame.Elapsed;
                t.loops = frame.Loops;

                t.apply?.Invoke(t.ease(frame.Phase));

                if (frame.Finished)
                {
                    t.alive = false;
                    t.done?.Invoke();
                }
            }
            _iterating = false;

            if (_add.Count > 0) { _live.AddRange(_add); _add.Clear(); }

            for (int i = _live.Count - 1; i >= 0; i--)
                if (!_live[i].alive) _live.RemoveAt(i);
        }

        // ------------------------------------------------------------- shorthands
        //
        // Every shorthand answers a null target with a finished tween rather than a throw, and
        // that is a rule rather than defensiveness. These are called from click handlers, so an
        // exception here does not merely skip an animation — it abandons whatever the handler
        // was doing halfway. ModalView.Close() is the case that proved it: it fades the content
        // out, scales the panel, and dismisses the view in the scale's OnDone, so a throw on the
        // middle line left an invisible view still eating every touch on the screen. The ones
        // that could throw were the ones reading their target's current value to interpolate
        // from — Scale, Move, Rotate and RotateBy; the rest already guarded. The null tween
        // still completes, so an OnDone chained onto one of them runs and the sequence finishes.
        public static Tw Scale(Transform tr, Vector3 to, float dur, Easing ease = null)
        {
            if (tr == null) return Run(0.001f, Ease.Linear, null);
            var from = tr.localScale;
            return Run(dur, ease ?? Ease.OutCubic, t => { if (tr) tr.localScale = Vector3.LerpUnclamped(from, to, t); }, tr, "scale");
        }

        public static Tw Scale(Transform tr, float to, float dur, Easing ease = null)
            => Scale(tr, Vector3.one * to, dur, ease);

        public static Tw Move(RectTransform rt, Vector2 to, float dur, Easing ease = null)
        {
            if (rt == null) return Run(0.001f, Ease.Linear, null);
            var from = rt.anchoredPosition;
            return Run(dur, ease ?? Ease.OutCubic, t => { if (rt) rt.anchoredPosition = Vector2.LerpUnclamped(from, to, t); }, rt, "move");
        }

        public static Tw Rotate(RectTransform rt, float toZ, float dur, Easing ease = null)
        {
            if (rt == null) return Run(0.001f, Ease.Linear, null);
            float from = rt.localEulerAngles.z;
            // shortest signed path
            float delta = Mathf.DeltaAngle(from, toZ);
            return Run(dur, ease ?? Ease.OutBack, t => { if (rt) rt.localRotation = Quaternion.Euler(0, 0, from + delta * t); }, rt, "rot");
        }

        public static Tw RotateBy(RectTransform rt, float degrees, float dur, Easing ease = null)
        {
            if (rt == null) return Run(0.001f, Ease.Linear, null);
            float from = rt.localEulerAngles.z;
            return Run(dur, ease ?? Ease.OutBack, t => { if (rt) rt.localRotation = Quaternion.Euler(0, 0, from + degrees * t); }, rt, "rot");
        }

        public static Tw Fade(Graphic g, float to, float dur, Easing ease = null)
        {
            if (g == null) return Run(0.001f, Ease.Linear, null);
            float from = g.color.a;
            return Run(dur, ease ?? Ease.OutQuad, t =>
            {
                if (!g) return;
                var c = g.color; c.a = Mathf.LerpUnclamped(from, to, t); g.color = c;
            }, g, "fade");
        }

        public static Tw Fade(CanvasGroup cg, float to, float dur, Easing ease = null)
        {
            if (cg == null) return Run(0.001f, Ease.Linear, null);
            float from = cg.alpha;
            return Run(dur, ease ?? Ease.OutQuad, t => { if (cg) cg.alpha = Mathf.LerpUnclamped(from, to, t); }, cg, "fade");
        }

        public static Tw Tint(Graphic g, Color to, float dur, Easing ease = null)
        {
            if (g == null) return Run(0.001f, Ease.Linear, null);
            var from = g.color;
            return Run(dur, ease ?? Ease.OutQuad, t => { if (g) g.color = Color.LerpUnclamped(from, to, t); }, g, "tint");
        }

        public static Tw Value(float a, float b, float dur, Action<float> set, Easing ease = null, UnityEngine.Object owner = null)
            => Run(dur, ease ?? Ease.OutCubic, t => set(Mathf.LerpUnclamped(a, b, t)), owner);

        /// <summary>A quick squash-and-stretch pop.</summary>
        public static Tw Punch(Transform tr, float strength = .18f, float dur = .34f)
        {
            if (tr == null) return Run(0.001f, Ease.Linear, null);
            var baseScale = tr.localScale;
            return Run(dur, Ease.Linear, t =>
            {
                if (!tr) return;
                float damp = 1f - t;
                float w = Mathf.Sin(t * Mathf.PI * 3f) * strength * damp * damp;
                tr.localScale = new Vector3(baseScale.x * (1f + w), baseScale.y * (1f - w * .7f), baseScale.z);
            }, tr, "punch").OnDone(() => { if (tr) tr.localScale = baseScale; });
        }

        public static Tw Pop(Transform tr, float from = 0f, float dur = .42f, float delay = 0f)
        {
            if (tr == null) return Run(0.001f, Ease.Linear, null);
            var to = tr.localScale;
            // callers often hide the object first; treat that as "pop up to full size"
            if (to.sqrMagnitude < 1e-6f) to = Vector3.one;
            tr.localScale = to * from;
            return Run(dur, Ease.OutBack, t => { if (tr) tr.localScale = Vector3.LerpUnclamped(to * from, to, t); }, tr, "scale").Delay(delay);
        }

        public static Tw Shake(RectTransform rt, float amount = 18f, float dur = .4f)
        {
            if (rt == null) return Run(0.001f, Ease.Linear, null);
            var home = rt.anchoredPosition;
            float seed = UnityEngine.Random.value * 100f;
            return Run(dur, Ease.Linear, t =>
            {
                if (!rt) return;
                float damp = (1f - t) * (1f - t);
                rt.anchoredPosition = home + new Vector2(
                    (Mathf.PerlinNoise(seed, t * 22f) - .5f) * 2f,
                    (Mathf.PerlinNoise(seed + 31f, t * 22f) - .5f) * 2f) * amount * damp;
            }, rt, "shake").OnDone(() => { if (rt) rt.anchoredPosition = home; });
        }

        /// <summary>Endless gentle bob, used for idle life on menus.</summary>
        public static Tw Bob(RectTransform rt, float amplitude = 12f, float period = 2.4f, float phase = 0f)
        {
            if (rt == null) return Run(0.001f, Ease.Linear, null);
            var home = rt.anchoredPosition;
            float t0 = Time.unscaledTime;
            return Run(3600f, Ease.Linear, _ =>
            {
                if (!rt) return;
                float w = (Time.unscaledTime - t0) / period * Mathf.PI * 2f + phase;
                rt.anchoredPosition = home + new Vector2(0f, Mathf.Sin(w) * amplitude);
            }, rt, "bob");
        }

        public static Tw Breathe(Transform tr, float amplitude = .04f, float period = 2.2f, float phase = 0f)
        {
            if (tr == null) return Run(0.001f, Ease.Linear, null);
            var home = tr.localScale;
            float t0 = Time.unscaledTime;
            return Run(3600f, Ease.Linear, _ =>
            {
                if (!tr) return;
                float w = (Time.unscaledTime - t0) / period * Mathf.PI * 2f + phase;
                tr.localScale = home * (1f + Mathf.Sin(w) * amplitude);
            }, tr, "breathe");
        }
    }
}
