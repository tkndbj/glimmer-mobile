using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>Base class for a full screen of UI. Subclasses build themselves in Build().</summary>
    public abstract class View : MonoBehaviour
    {
        public RectTransform Root { get; private set; }
        public RectTransform Content { get; private set; }
        protected CanvasGroup Group;

        RectTransform _safe;

        /// <summary>
        /// A layer inset to the display's safe area, for chrome a cutout or a home indicator
        /// must not sit on top of.
        ///
        /// <para>
        /// <b>Only controls belong here.</b> <see cref="Content"/> stays full-bleed and is
        /// where backdrops, fades and playfields go: letterboxing a painting to avoid a
        /// camera is a worse picture than the camera. What goes in here is what the player
        /// has to read or press — a back arrow, a banner, a readout, a button.
        /// </para>
        /// <para>
        /// Created on first use rather than always, so a screen that never asks for it costs
        /// nothing, and created <em>inside</em> <see cref="Content"/> so the order a screen
        /// builds in still decides what draws over what. On any display with nothing in the
        /// way — every device without a cutout, and the Editor — its insets are zero and the
        /// layout is exactly what it was. See <see cref="SafeArea"/>.
        /// </para>
        /// </summary>
        protected RectTransform Safe
            => _safe != null ? _safe : (_safe = SafeArea.Node("Safe", Content));

        internal void Init()
        {
            Root = (RectTransform)transform;
            Group = UIKit.Group(Root);
            Content = UIKit.Node("Content", Root);
            Build();
        }

        protected abstract void Build();

        /// <summary>Music track for this screen, null keeps whatever is playing.</summary>
        public virtual string Track => null;

        /// <summary>Called once the incoming transition has finished.</summary>
        public virtual void OnPresented() { }

        /// <summary>
        /// False while this screen is still assembling something the player must not watch
        /// arrive. <see cref="Flow"/> holds the iris shut until it turns true.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Almost every screen here builds itself inside <see cref="Init"/> and is finished
        /// before the transition starts, which is why this defaults to true and why nothing
        /// needed it for a long time. The map is the exception: it cannot draw until its
        /// chapter's body has been read and that chapter's art is resident, and neither is
        /// guaranteed to be in hand. Without a gate the iris opened on a bare screen and the
        /// chapter arrived a moment later — the swap the transition exists to hide, happening
        /// in full view.
        /// </para>
        /// <para>
        /// This is a <em>declaration</em> rather than a call into <see cref="Flow"/>, for the
        /// reason <see cref="WantsMultiTouch"/> is: a screen that told the transition when to
        /// continue would have to tell it on every path out of its own loading, including the
        /// ones that fail, and this project has paid for that shape more than once.
        /// </para>
        /// </remarks>
        public virtual bool Ready => true;

        /// <summary>Return true to swallow the hardware back button.</summary>
        public virtual bool OnBack() => false;

        /// <summary>
        /// Whether this screen needs more than one finger at a time.
        ///
        /// <para>
        /// Multi-touch is <b>off for the whole game</b> — <c>Boot</c> turns it off before the
        /// first frame — because a board that accepted two fingers would let a player turn two
        /// conduits in one tap, and a move counter that can be beaten by having two thumbs is
        /// not a move counter. Exactly one screen needs it back: the grove, whose field is
        /// pinch-zoomed.
        /// </para>
        /// <para>
        /// <b>Declared rather than set.</b> A screen that switched the flag on in <c>Build</c>
        /// would have to switch it off again on every way out, and this project has twice paid
        /// for a rule shaped like that — the pause menu that only unlatched from its buttons,
        /// and the art scope only one of two screens remembered to release. <see cref="Flow"/>
        /// applies this on every screen change, so the board cannot inherit the grove's setting
        /// however the player left it.
        /// </para>
        /// </summary>
        public virtual bool WantsMultiTouch => false;
    }

    /// <summary>Screen stack plus the iris transition that hides the swap.</summary>
    public static class Flow
    {
        public static Canvas Canvas;
        public static RectTransform Screens;
        public static RectTransform Overlays;
        public static RectTransform Effects;
        public static View Current;
        public static bool Busy { get; private set; }

        static readonly List<View> _modals = new List<View>();
        static Image _iris;
        static Image _flash;

        public static Vector2 Size => Screens.rect.size;

        internal static void Init(Canvas canvas)
        {
            Canvas = canvas;
            var root = (RectTransform)canvas.transform;
            Screens = UIKit.Node("Screens", root);
            Overlays = UIKit.Node("Overlays", root);
            Effects = UIKit.Node("Effects", root);

            _iris = UIKit.Img("Iris", Effects, Art.Disc(256), Pal.A(Pal.Slate, 1f),
                              Vector2.one * 3400f, new Vector2(.5f, .5f), Vector2.zero);
            _iris.raycastTarget = false;
            _iris.transform.localScale = Vector3.zero;
            _iris.gameObject.SetActive(false);

            _flash = UIKit.Img("Flash", Effects, Art.Pixel, new Color(1, 1, 1, 0));
            _flash.raycastTarget = false;
        }

        // ------------------------------------------------------------ screens
        public static void Go<T>(Action<T> configure = null, bool instant = false) where T : View
            => Go(typeof(T), v => configure?.Invoke((T)v), instant);

        /// <summary>
        /// The same, told which screen at run time rather than at compile time.
        ///
        /// It exists so a mode can <em>name</em> its screen (see <c>ModeLook.Screen</c>) instead
        /// of every caller switching on the mode to pick one. The generic overload above is the
        /// ordinary way in and now simply calls this, so there is one implementation of what a
        /// screen change does rather than two that could come to disagree about, say, whether
        /// multi-touch is reapplied.
        /// </summary>
        public static void Go(Type view, Action<View> configure = null, bool instant = false)
        {
            if (Busy) return;
            if (view == null || !typeof(View).IsAssignableFrom(view))
            {
                Debug.LogError($"[Flow] '{view}' is not a screen");
                return;
            }

            Busy = true;

            void Swap()
            {
                foreach (var m in _modals) if (m) UnityEngine.Object.Destroy(m.gameObject);
                _modals.Clear();
                if (Current) UnityEngine.Object.Destroy(Current.gameObject);

                var rt = UIKit.Node(view.Name, Screens);
                var screen = (View)rt.gameObject.AddComponent(view);
                configure?.Invoke(screen);
                screen.Init();
                Current = screen;
                if (screen.Track != null) Audio.Music(screen.Track);

                // Applied on every swap rather than only when it changes, so the answer is
                // always the incoming screen's own — see View.WantsMultiTouch.
                Input.multiTouchEnabled = screen.WantsMultiTouch;
            }

            if (instant)
            {
                Swap();
                Busy = false;
                Current.OnPresented();
                return;
            }

            // No sound of its own. A screen change is always something the player just
            // tapped, and the button they tapped has already made a noise — laying a
            // whoosh over it turned every navigation into two overlapping sounds, which
            // is what made the hub's feature boxes and its buttons seem to disagree about
            // how loud a tap is. The iris carries the transition; the tap carries the
            // moment. Whoosh is still the right sound where nothing was tapped — a board
            // resetting, a panel dealing out its rows.
            IrisClose(() =>
            {
                Swap();
                WhenReady(Current, () => IrisOpen(() => { Busy = false; Current.OnPresented(); }));
            });
        }

        /// <summary>
        /// Longest the iris will stay shut waiting on a screen that is not
        /// <see cref="View.Ready"/>.
        /// </summary>
        /// <remarks>
        /// A ceiling rather than a promise. Whatever a screen is waiting for can fail in a way
        /// it does not notice — a file that never arrives, a task that never completes — and
        /// the alternative to giving up is a slate disc over the whole game with no way out of
        /// it. A half-drawn map the player can leave beats a screen they have to kill the app
        /// to escape. Generous because it should never be reached: the wait it exists for is a
        /// local file read, and one long enough to hit this is a bug rather than a slow phone.
        /// </remarks>
        const float ReadyTimeout = 5f;

        static void WhenReady(View view, Action done)
        {
            if (view == null || view.Ready) { done(); return; }
            Tween.Inst.StartCoroutine(Waiting(view, done));
        }

        static IEnumerator Waiting(View view, Action done)
        {
            float waited = 0f;

            // Unscaled, like every other clock driving the chrome — a transition must not
            // stretch because something paused the game underneath it.
            while (view && !view.Ready && waited < ReadyTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            done();
        }

        static void IrisClose(Action done)
        {
            _iris.gameObject.SetActive(true);
            _iris.raycastTarget = true;
            _iris.transform.localScale = Vector3.zero;
            Tween.Run(.30f, Ease.InCubic,
                t => _iris.transform.localScale = Vector3.one * Mathf.Lerp(0f, 1.05f, t), _iris)
                 .OnDone(() => done());
        }

        static void IrisOpen(Action done)
        {
            Tween.Run(.42f, Ease.OutCubic,
                t => _iris.transform.localScale = Vector3.one * Mathf.Lerp(1.05f, 0f, t), _iris)
                 .Delay(.06f)
                 .OnDone(() =>
                 {
                     _iris.raycastTarget = false;
                     _iris.gameObject.SetActive(false);
                     done();
                 });
        }

        /// <summary>Bright wash across the whole screen; used when a level completes.</summary>
        public static void Flash(Color colour, float peak = .8f, float dur = .5f)
        {
            if (_flash == null) return;
            _flash.color = Pal.A(colour, 0f);
            Tween.Run(dur, Ease.OutQuint, t =>
            {
                var c = _flash.color;
                c.a = t < .18f ? Mathf.Lerp(0f, peak, t / .18f) : Mathf.Lerp(peak, 0f, (t - .18f) / .82f);
                _flash.color = c;
            }, _flash, "flash");
        }

        // ------------------------------------------------------------- modals
        public static T Modal<T>(Action<T> configure = null) where T : View
        {
            var rt = UIKit.Node(typeof(T).Name, Overlays);
            var view = rt.gameObject.AddComponent<T>();
            configure?.Invoke(view);
            view.Init();
            _modals.Add(view);
            return view;
        }

        public static void Dismiss(View v)
        {
            _modals.Remove(v);
            if (v) UnityEngine.Object.Destroy(v.gameObject);
        }

        public static bool HasModal => _modals.Count > 0;

        internal static void HandleBack()
        {
            if (Busy) return;

            // A modal speaks for itself: every OnBack here routes through ModalView.Close,
            // which plays `back` already.
            for (int i = _modals.Count - 1; i >= 0; i--)
                if (_modals[i] && _modals[i].OnBack()) return;

            // A screen does not. Its OnBack navigates through Go, which no longer makes a
            // sound of its own, and there is no button here to have made one — this is the
            // hardware key. Without this line, five screens would retreat in silence while
            // the back key drawn in their own corner clicked.
            if (Current != null && Current.OnBack()) Audio.SfxVaried("back", .5f);
        }
    }
}
