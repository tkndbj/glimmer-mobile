using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Where a panel sits in the modal stack, so what draws over what is a <em>declaration</em>
    /// rather than an accident of which one was raised first.
    ///
    /// <para>
    /// <b>It exists because creation order is not the same as importance.</b> Every modal is a
    /// child of one node and Unity draws the last sibling on top, which is right for the
    /// overwhelming majority of panels — one is raised from another and belongs above it. It is
    /// wrong for exactly one kind: a panel that is raised on a <em>timer</em>. A first-timer's
    /// lesson is scheduled a beat after the board arrives (<c>RunScreen.LessonDelay</c>) and
    /// chained a beat after each dismissal, and a player who opens the pause menu inside one of
    /// those beats had raised their panel first — so the tip landed on top of it, over a menu
    /// that could still be pressed through the hole cut in the tip's own dim.
    /// </para>
    /// <para>
    /// Sequencing alone fixes the case that was reported (see <c>RunLessons.ShowLesson</c>,
    /// which now waits for a clear screen), and this is the half that makes it
    /// <em>unrepresentable</em> rather than remembered: whatever order two panels are raised in,
    /// a lesson cannot cover a menu. The numbers are spaced so a layer can be added between two
    /// of them without renumbering anything.
    /// </para>
    /// </summary>
    public static class ModalLayer
    {
        /// <summary>
        /// A lesson over the board. The bottom of the stack, because it is the only panel here
        /// that appears without the player having asked for it at that moment.
        /// </summary>
        public const int Teaching = 0;

        /// <summary>Everything else: menus, offers, prompts, receipts. The default.</summary>
        public const int Panel = 10;

        /// <summary>
        /// A lesson pointing at a control that is on a <em>panel</em> rather than on the board.
        ///
        /// <para>
        /// <b>It is the one place <see cref="Teaching"/> is not merely conservative but fatal.</b>
        /// A tip cuts its spotlight out of its own dim, so the thing it is pointing at is only
        /// visible through the hole — put the tip underneath the panel carrying that control and
        /// the panel hides the lesson and its subject together. The win panel's wheel tip drew
        /// exactly nothing for that reason, and because a tip is marked seen once in a player's
        /// life it was spent on a frame nobody saw.
        /// </para>
        /// <para>
        /// Above <see cref="Panel"/> is safe here only because a tip on a panel is raised by
        /// that panel, which checks it is still the one being looked at first
        /// (<see cref="Flow.IsTopModal"/>) — and once up, a tip swallows every tap, including
        /// the one through its own hole. What is being bought is the licence to cover
        /// <em>one</em> panel, not the licence <see cref="Teaching"/> exists to withhold.
        /// </para>
        /// </summary>
        public const int Coaching = 20;
    }

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
            => _safe != null ? _safe : (_safe = SafeArea.Node("Safe", Content, SafeEdges));

        /// <summary>
        /// Which edges <see cref="Safe"/> insets. All four unless a screen says otherwise, and
        /// the only screens that do are the run screens, which give up the top — see
        /// <c>RunScreen.SafeEdges</c>. Read once, when the layer is first asked for.
        /// </summary>
        protected virtual SafeArea.Edges SafeEdges => SafeArea.Edges.All;

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

        /// <summary>
        /// True once this view has begun going away and is only finishing its exit animation.
        ///
        /// <para>
        /// It exists for <see cref="Flow.Modal{T}"/>, which refuses to raise a second copy of a
        /// panel that is already up. Without this the refusal would also swallow the legitimate
        /// case — a panel closing and the next one of the same type opening behind it, which is
        /// exactly how <c>RunLessons</c> walks a player through a board's tips. A view on its
        /// way out is not "already up".
        /// </para>
        /// <para>
        /// Declared here rather than on <c>ModalView</c> because <see cref="Flow"/> holds
        /// <see cref="View"/> and may not reach downwards; false is the honest answer for a
        /// screen, which is swapped rather than dismissed.
        /// </para>
        /// </summary>
        public virtual bool IsLeaving => false;

        /// <summary>
        /// Which layer of the modal stack this panel belongs to. See <see cref="ModalLayer"/>.
        ///
        /// <para>
        /// Declared here rather than on <c>ModalView</c> for <see cref="IsLeaving"/>'s reason —
        /// <see cref="Flow"/> holds <see cref="View"/> and may not reach downwards. A screen is
        /// never in the stack, so the value it inherits is never read.
        /// </para>
        /// </summary>
        public virtual int Layer => ModalLayer.Panel;

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
        /// <summary>
        /// Raises a modal panel — or hands back the one that is already up.
        ///
        /// <para>
        /// <b>Idempotent by type, and that is the substance rather than a nicety.</b> A button
        /// can be pressed twice before the panel it raises has drawn a single frame: a fast
        /// double-tap, a stylus, a screen reader, an accessibility switch, a phone that dropped
        /// a frame. Every one of those used to build two panels — two scrims, two entrance
        /// chimes, and a player who dismisses one and finds an identical one behind it. On the
        /// gem shelf raised over a lost run that is worse than untidy: the second copy owns the
        /// same <c>Bought</c> callback, so a purchase would be reported twice into whatever
        /// raised it.
        /// </para>
        /// <para>
        /// <b>The existing panel is returned unconfigured, and that is deliberate.</b> A second
        /// call is a duplicate of a request already granted — the panel in front of the player
        /// was configured by the first one and is live. Re-running <paramref name="configure"/>
        /// on it would rewrite the callbacks of a panel mid-interaction, which is a subtler
        /// version of the bug this exists to stop.
        /// </para>
        /// <para>
        /// <b>A panel on its way out does not count as already up.</b> A modal stays in the
        /// stack for the fifth of a second its exit takes, and a sequence that closes one and
        /// opens the next of the same type — <c>RunLessons</c> walking a board's tips — must
        /// not be refused. See <see cref="View.IsLeaving"/>.
        /// </para>
        /// </summary>
        public static T Modal<T>(Action<T> configure = null) where T : View
        {
            var live = LiveModal<T>();
            if (live != null) return live;

            var rt = UIKit.Node(typeof(T).Name, Overlays);
            var view = rt.gameObject.AddComponent<T>();
            configure?.Invoke(view);
            view.Init();

            // By layer first and by arrival second, so a panel raised on a timer cannot end up
            // over one the player asked for. See ModalLayer.
            int at = _modals.Count;
            while (at > 0 && _modals[at - 1].Layer > view.Layer) at--;
            _modals.Insert(at, view);
            Restack();

            return view;
        }

        /// <summary>
        /// Puts the overlay node's children back into <see cref="_modals"/> order.
        ///
        /// <para>
        /// Walked forwards with <c>SetAsLastSibling</c> rather than by computing an index for
        /// the newcomer, because the two are not the same list: <c>Destroy</c> lands at the end
        /// of the frame, so a panel already dismissed is still a child for the rest of it and
        /// any index taken from <see cref="Overlays"/> would be off by however many of those
        /// there are. Doing it this way needs no such arithmetic — every live panel is lifted
        /// above every leftover, in order, and the leftovers sink to the bottom where they
        /// belong for the frame they have left.
        /// </para>
        /// <para>
        /// Cheap enough to be unconditional: the stack is two or three panels deep at its worst
        /// and this runs once, when one is raised.
        /// </para>
        /// </summary>
        static void Restack()
        {
            for (int i = 0; i < _modals.Count; i++)
                if (_modals[i]) _modals[i].Root.SetAsLastSibling();
        }

        /// <summary>
        /// The modal of this type that is up and staying up, or null.
        ///
        /// Walked from the top down, so the answer is the one the player is looking at rather
        /// than the oldest of several — a distinction that cannot arise while
        /// <see cref="Modal{T}"/> refuses duplicates, and would matter the moment somebody
        /// added a way round it.
        /// </summary>
        public static T LiveModal<T>() where T : View
        {
            for (int i = _modals.Count - 1; i >= 0; i--)
            {
                if (!_modals[i] || _modals[i].IsLeaving) continue;
                if (_modals[i] is T match) return match;
            }

            return null;
        }

        public static void Dismiss(View v)
        {
            _modals.Remove(v);
            if (v) UnityEngine.Object.Destroy(v.gameObject);
        }

        public static bool HasModal => _modals.Count > 0;

        /// <summary>
        /// Whether <paramref name="v"/> is the topmost panel that is up and staying up.
        ///
        /// <para>
        /// For a panel that raises a lesson over <em>itself</em> on a timer, which is
        /// <c>WinOverlay.TeachTheWheel</c> and nothing else. Such a tip sits above
        /// <see cref="ModalLayer.Panel"/> (see <see cref="ModalLayer.Coaching"/>), so the check
        /// <see cref="ModalLayer.Teaching"/> makes structurally has to be made here instead:
        /// several seconds of victory sequence run before the beat fires, and a player who
        /// tapped the offer inside one of them has a panel up that the lesson would cover.
        /// </para>
        /// <para>
        /// A panel on its way out does not count, for <see cref="View.IsLeaving"/>'s reason —
        /// a lesson chained behind a panel that is still fading must not be refused by it.
        /// </para>
        /// </summary>
        public static bool IsTopModal(View v)
        {
            if (!v || v.IsLeaving) return false;

            for (int i = _modals.Count - 1; i >= 0; i--)
            {
                var m = _modals[i];
                if (!m || m.IsLeaving) continue;
                return ReferenceEquals(m, v);
            }

            return false;
        }

        /// <summary>
        /// Whether anything above <paramref name="layer"/> is up and staying up.
        ///
        /// <para>
        /// For a panel that raises itself on a timer and has to decide whether now is a good
        /// moment — <c>RunLessons</c> is the one caller and the reason this exists. A panel on
        /// its way out does not count, for <see cref="View.IsLeaving"/>'s reason: a lesson
        /// chained behind the tip that is still fading must not be refused by it.
        /// </para>
        /// </summary>
        public static bool HasModalAbove(int layer)
        {
            for (int i = _modals.Count - 1; i >= 0; i--)
            {
                var m = _modals[i];
                if (!m || m.IsLeaving) continue;
                if (m.Layer > layer) return true;
            }

            return false;
        }

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
