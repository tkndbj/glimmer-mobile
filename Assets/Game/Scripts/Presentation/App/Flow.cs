using System;
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

        /// <summary>Return true to swallow the hardware back button.</summary>
        public virtual bool OnBack() => false;
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
        {
            if (Busy) return;
            Busy = true;

            void Swap()
            {
                foreach (var m in _modals) if (m) UnityEngine.Object.Destroy(m.gameObject);
                _modals.Clear();
                if (Current) UnityEngine.Object.Destroy(Current.gameObject);

                var rt = UIKit.Node(typeof(T).Name, Screens);
                var view = rt.gameObject.AddComponent<T>();
                configure?.Invoke(view);
                view.Init();
                Current = view;
                if (view.Track != null) Audio.Music(view.Track);
            }

            if (instant)
            {
                Swap();
                Busy = false;
                Current.OnPresented();
                return;
            }

            Audio.SfxVaried("whoosh", .45f);
            IrisClose(() =>
            {
                Swap();
                IrisOpen(() => { Busy = false; Current.OnPresented(); });
            });
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
            for (int i = _modals.Count - 1; i >= 0; i--)
                if (_modals[i] && _modals[i].OnBack()) return;
            if (Current != null && Current.OnBack()) return;
        }
    }
}
