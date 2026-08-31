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
        internal Action revert;
        internal UnityEngine.Object owner;
        internal string channel;
        internal bool unscaled = true;
        internal bool alive = true;
        internal int loops;          // 0 = once, -1 = forever
        internal bool pingPong;

        public Tw OnDone(Action a) { done = a; return this; }

        /// <summary>
        /// Where to leave the target if this tween ends before it reaches its end - superseded
        /// on its channel, or killed outright. Without one, an interrupted tween abandons
        /// whatever it was animating at whatever value the interruption caught it holding,
        /// which is a state no caller asked for and nothing puts right.
        ///
        /// <para>
        /// Two kinds of tween need one and they resolve in opposite directions. One that
        /// <em>borrows</em> a value - a punch, a shake, an idle bob - has to hand it back.
        /// One that <em>arrives</em> at a resting state it read off the target first - a pop -
        /// has to land. Everything else is going somewhere new on purpose, declares nothing,
        /// and is left exactly where it got to.
        /// </para>
        /// </summary>
        public Tw OnAbandon(Action a) { revert = a; return this; }
        public Tw Delay(float d) { delay = d; return this; }
        public Tw Scaled() { unscaled = false; return this; }
        public Tw Loop(int count = -1, bool pingpong = true) { loops = count; pingPong = pingpong; return this; }
        public void Kill(bool complete = false)
        {
            if (!alive) return;
            alive = false;
            if (complete) { apply?.Invoke(ease(1f)); done?.Invoke(); }
            else revert?.Invoke();
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

                    // DontDestroyOnLoad throws outside play mode, which is what kept this
                    // engine untestable: a driver nothing can instantiate is a driver nothing
                    // can drive a frame of. Outside play there are no scene loads to survive,
                    // so the call is meaningless there anyway - and the object is marked never
                    // to be saved, so an EditMode test cannot leave one behind in whatever
                    // scene happens to be open.
                    if (Application.isPlaying) DontDestroyOnLoad(go);
                    else go.hideFlags = HideFlags.HideAndDontSave;

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

        /// <summary>
        /// Ends whatever is running on one target's channel, and puts back anything that
        /// channel had borrowed - see <see cref="Tw.OnAbandon"/>.
        ///
        /// <para>
        /// The restore is why this is <c>Kill</c> rather than a flag flipped in place. A
        /// superseded tween used to be dropped exactly where it stood, so a caller that kills
        /// a breathe before punching found the transform mid-breath, and a punch superseding
        /// a punch found it mid-squash - which is the fault the home screen's companion wore.
        /// A tween that declares no revert is untouched by this, so every channel that moves a
        /// value somewhere new rather than borrowing it behaves exactly as it did.
        /// </para>
        /// </summary>
        public static void KillChannel(UnityEngine.Object owner, string channel)
        {
            if (_inst == null || owner == null) return;
            var list = _inst._live;
            for (int i = 0; i < list.Count; i++)
                if (list[i].alive && list[i].channel == channel && ReferenceEquals(list[i].owner, owner))
                    list[i].Kill();
        }

        /// <summary>
        /// Whether a tween was given an owner and that owner has since been destroyed.
        ///
        /// <para>
        /// Two null checks that mean different things, and the order is the whole of it.
        /// <c>UnityEngine.Object</c> overloads <c>==</c> to answer null for an object that
        /// has been <em>destroyed</em> as well as for one that was never set — so
        /// <c>owner != null</c> is false in exactly the case this exists to catch, and the
        /// guard it used to be written as could never fire once. The managed
        /// <see cref="object.ReferenceEquals"/> is the only way to ask "was an owner
        /// supplied at all", and it has to be asked first.
        /// </para>
        /// <para>
        /// What that cost: every tween whose owner had been destroyed went on running to
        /// completion and calling its <c>OnDone</c>, for the life of the game, against the
        /// class's own promise to die with its owner. The <c>apply</c> bodies here all guard
        /// their target, so the motion was harmless and invisible — the callbacks do not,
        /// and a payout chip destroyed mid-flight still landed seven tokens on a glyph that
        /// no longer existed. Passing an owner is opt-in, so a caller that passes one is
        /// asking for this and had not been getting it.
        /// </para>
        /// </summary>
        /// <remarks>
        /// Fixing it changed behaviour at every call site that passes an owner, so the
        /// <c>OnDone</c> chains were walked once and the answer is worth keeping: <b>none of
        /// them needs to outlive its owner</b>. Almost all are cosmetic and already guard
        /// their target — destroy a spent ring, start a breathe, rehome a button. Three carry
        /// state, and each is safe for its own reason rather than by luck:
        /// <list type="bullet">
        /// <item><description>
        /// <c>Overlays.Close</c> hangs <c>Flow.Dismiss</c> and the caller's continuation off a
        /// scale owned by <c>Panel</c>. The only thing that destroys Panel mid-close is
        /// <c>Flow.Go</c>'s swap — which clears <c>_modals</c> itself, so the missed Dismiss is
        /// a no-op, and which sets <c>Busy</c> first, so a missed <c>Flow.Go</c> continuation
        /// would have been refused anyway. Skipping it is if anything more correct than
        /// firing a second navigation into the first.
        /// </description></item>
        /// <item><description>
        /// <c>Flow</c>'s iris is owned by <c>_iris</c>, which is built once on the persistent
        /// effects layer and is never destroyed while the game runs.
        /// </description></item>
        /// <item><description>
        /// The dismissals that must not be missed do not rely on a tween at all:
        /// <c>AdOfferOverlay</c> raises <c>Dismissed</c> from <c>OnDestroy</c> behind a latch,
        /// which is the whole reason that panel was built that way.
        /// </description></item>
        /// </list>
        /// The general rule the audit leaves behind: an <c>OnDone</c> that must happen whatever
        /// becomes of the thing being animated does not belong on an owned tween.
        /// </remarks>
        public static bool Orphaned(UnityEngine.Object owner)
            => !ReferenceEquals(owner, null) && owner == null;

        public static void KillAll(UnityEngine.Object owner)
        {
            if (_inst == null || owner == null) return;
            var list = _inst._live;
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i].owner, owner)) list[i].Kill();
        }

        // Clamped before anything reads them, because the phase arithmetic in Tick is only
        // as sound as the step handed to it — see TweenCycle.MaxStep for why the frame after
        // a resume is not a frame's worth of time.
        void Update() => Tick(TweenCycle.Step(Time.unscaledDeltaTime),
                              TweenCycle.Step(Time.deltaTime));

        /// <summary>
        /// One frame of every live tween, handed the elapsed time rather than reading a clock.
        ///
        /// <para>
        /// Split from <see cref="Update"/> for <c>RunScreen.Tick</c>'s reason, and it is not
        /// a tidying. <see cref="TweenCycle"/> made the phase arithmetic provable offline, and
        /// that left exactly one rule in this file untestable — the one that decides whether a
        /// tween <em>runs at all</em>. It was wrong for the life of the game (see
        /// <see cref="Orphaned"/>), and a test of the predicate alone would not have caught it,
        /// because the predicate was never the part that was broken: the wiring was. A test
        /// has to be able to say "this owner is gone, therefore this OnDone did not fire",
        /// and that needs a frame it can drive.
        /// </para>
        /// <para>
        /// The clamp deliberately stays in <see cref="Update"/>. What a caller hands in is
        /// the step it wants applied; what the engine does with a real frame's
        /// <c>deltaTime</c> is the engine's business, and a test asking for half a second
        /// should get half a second.
        /// </para>
        /// </summary>
        public void Tick(float unscaledStep, float scaledStep)
        {
            float dt = unscaledStep;
            float sdt = scaledStep;
            _iterating = true;
            for (int i = 0; i < _live.Count; i++)
            {
                var t = _live[i];
                if (!t.alive) continue;
                // Dropped rather than reverted: the owner is gone, so there is nothing
                // left to put a borrowed value back onto.
                if (Orphaned(t.owner)) { t.alive = false; continue; }

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

        /// <summary>
        /// A quick squash-and-stretch pop.
        ///
        /// <para>
        /// The rest scale is read <em>after</em> any punch still in flight has been put back,
        /// and that ordering is the whole of it. A punch borrows the transform's scale and
        /// hands it back at the end; a second punch landing inside the first superseded it on
        /// the channel - which dropped it mid-squash, without its restore - and then took the
        /// squashed scale as its own rest. So each punch multiplied into the next, and the
        /// only way back was rebuilding the screen. Spam-tapping the home screen's companion
        /// squeezed it to a sliver, but the shape is everywhere a control can be re-punched
        /// before it settles: a tile tapped twice, a move counter ticking down, a chest
        /// thumping.
        /// </para>
        /// </summary>
        public static Tw Punch(Transform tr, float strength = .18f, float dur = .34f)
        {
            if (tr == null) return Run(0.001f, Ease.Linear, null);

            KillChannel(tr, "punch");            // put back what a punch in flight borrowed
            var baseScale = tr.localScale;
            Action rest = () => { if (tr) tr.localScale = baseScale; };

            return Run(dur, Ease.Linear, t =>
            {
                if (!tr) return;
                float damp = 1f - t;
                float w = Mathf.Sin(t * Mathf.PI * 3f) * strength * damp * damp;
                tr.localScale = new Vector3(baseScale.x * (1f + w), baseScale.y * (1f - w * .7f), baseScale.z);
            }, tr, "punch").OnDone(rest).OnAbandon(rest);
        }

        /// <summary>
        /// A spring up to the scale the transform is already sitting at.
        ///
        /// <para>
        /// <see cref="Punch"/>'s fault in the other direction, and it had a workaround rather
        /// than a fix. A pop reads its destination off the target, so an interrupted one used
        /// to leave a control at whatever fraction it had sprung to - or at nothing at all, if
        /// it was still inside its delay - and a second pop then took <em>that</em> as the size
        /// to spring back to. <c>GridView</c> is where it bit: a recycled cell arriving mid-pop
        /// from its last life stayed at scale zero for ever, so the type kills the channel and
        /// resets the scale by hand every time it binds one. Landing on the rest scale rather
        /// than freezing makes that true everywhere instead of in the one file that noticed.
        /// </para>
        /// </summary>
        public static Tw Pop(Transform tr, float from = 0f, float dur = .42f, float delay = 0f)
        {
            if (tr == null) return Run(0.001f, Ease.Linear, null);

            KillChannel(tr, "scale");            // land a pop in flight, so `to` is a real size
            var to = tr.localScale;
            // callers often hide the object first; treat that as "pop up to full size"
            if (to.sqrMagnitude < 1e-6f) to = Vector3.one;
            tr.localScale = to * from;

            return Run(dur, Ease.OutBack, t => { if (tr) tr.localScale = Vector3.LerpUnclamped(to * from, to, t); }, tr, "scale")
                   .Delay(delay)
                   .OnAbandon(() => { if (tr) tr.localScale = to; });
        }

        /// <summary>A quick rattle. Borrows the position and returns it - see <see cref="Punch"/>.</summary>
        public static Tw Shake(RectTransform rt, float amount = 18f, float dur = .4f)
        {
            if (rt == null) return Run(0.001f, Ease.Linear, null);

            KillChannel(rt, "shake");
            var home = rt.anchoredPosition;
            Action rest = () => { if (rt) rt.anchoredPosition = home; };
            float seed = UnityEngine.Random.value * 100f;

            return Run(dur, Ease.Linear, t =>
            {
                if (!rt) return;
                float damp = (1f - t) * (1f - t);
                rt.anchoredPosition = home + new Vector2(
                    (Mathf.PerlinNoise(seed, t * 22f) - .5f) * 2f,
                    (Mathf.PerlinNoise(seed + 31f, t * 22f) - .5f) * 2f) * amount * damp;
            }, rt, "shake").OnDone(rest).OnAbandon(rest);
        }

        /// <summary>
        /// Endless gentle bob, used for idle life on menus. An idle borrows the value it waves
        /// about for as long as it runs, so ending one lands on the rest position rather than
        /// on wherever the wave had got to.
        /// </summary>
        public static Tw Bob(RectTransform rt, float amplitude = 12f, float period = 2.4f, float phase = 0f)
        {
            if (rt == null) return Run(0.001f, Ease.Linear, null);

            KillChannel(rt, "bob");
            var home = rt.anchoredPosition;
            float t0 = Time.unscaledTime;

            return Run(3600f, Ease.Linear, _ =>
            {
                if (!rt) return;
                float w = (Time.unscaledTime - t0) / period * Mathf.PI * 2f + phase;
                rt.anchoredPosition = home + new Vector2(0f, Mathf.Sin(w) * amplitude);
            }, rt, "bob").OnAbandon(() => { if (rt) rt.anchoredPosition = home; });
        }

        /// <summary>
        /// Endless gentle swell. Borrowed like <see cref="Bob"/>, and the two callers that kill
        /// a breathe in order to punch the same control are why: they used to punch whatever
        /// scale the breath happened to be holding at that instant.
        /// </summary>
        /// <summary>
        /// How long a breath takes to come up to its full size, so nothing ever begins one by
        /// jumping.
        /// </summary>
        const float Ramp = .50f;

        public static Tw Breathe(Transform tr, float amplitude = .04f, float period = 2.2f, float phase = 0f)
        {
            if (tr == null) return Run(0.001f, Ease.Linear, null);

            KillChannel(tr, "breathe");
            var home = tr.localScale;
            float t0 = Time.unscaledTime;

            return Run(3600f, Ease.Linear, _ =>
            {
                if (!tr) return;

                float on = Time.unscaledTime - t0;

                // **It fades in rather than starting mid-breath, and that is what a phase costs
                // if you do not.** A phase exists to stop a row of things breathing in lockstep,
                // and `sin(phase)` is not zero — so every caller passing one used to snap its
                // target to a different size on the frame the breath began. One control doing
                // that is a twitch nobody sees; a *board* doing it is thirty flowers each
                // jumping to a size of their own in one frame, which is what "the background
                // resets suddenly" was. Smoothstepped, so the fade has no corner at either end.
                float k = on < Ramp ? on / Ramp : 1f;
                k = k * k * (3f - 2f * k);

                float w = on / period * Mathf.PI * 2f + phase;
                tr.localScale = home * (1f + Mathf.Sin(w) * amplitude * k);
            }, tr, "breathe").OnAbandon(() => { if (tr) tr.localScale = home; });
        }
    }
}
