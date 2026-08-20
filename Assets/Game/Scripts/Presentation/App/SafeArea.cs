using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// How much of the display the system has taken: a camera cutout, a notch, a home
    /// indicator, a rounded corner.
    ///
    /// <para>
    /// <b>The canvas is not the screen, and that is the whole of it.</b> Everything here is
    /// laid out against a canvas width-matched at 1080 (see <c>Boot.BuildCanvas</c>), while
    /// <see cref="Screen.safeArea"/> is reported in device pixels — 141 of them across the
    /// top of an iPhone 13 Pro Max. Dividing by the canvas's own scale factor is what turns
    /// one into the other, and it is the step a hand-tuned margin skips: a constant chosen to
    /// clear one phone's camera is wrong on every other phone, and wrong in the invisible
    /// direction on a device with no cutout at all, where it becomes a strip of wasted screen
    /// nothing explains.
    /// </para>
    /// <para>
    /// <b>An inset of zero is the ordinary answer.</b> Every device without a cutout, every
    /// Android phone with the status bar hidden, and the Editor all report a safe area that
    /// is the whole screen — so a screen that moves its chrome into <see cref="Node"/> is
    /// pixel-identical to what it was before on all of them. That property is what makes this
    /// safe to adopt one screen at a time rather than in one sweep.
    /// </para>
    /// <para>
    /// <b>Chrome moves; art does not.</b> A backdrop, a fade or a field is supposed to run
    /// under the cutout — letterboxing the picture to avoid a camera is a worse answer than
    /// the camera. Only the things a player has to read or press belong inside the inset, so
    /// this is a layer a screen opts controls into rather than something applied to the whole
    /// of it.
    /// </para>
    /// </summary>
    public static class SafeArea
    {
        /// <summary>Canvas-unit insets on each edge. All zero on a display with nothing in the way.</summary>
        public readonly struct Insets
        {
            public readonly float Left, Right, Bottom, Top;

            public Insets(float left, float right, float bottom, float top)
            {
                Left = left;
                Right = right;
                Bottom = bottom;
                Top = top;
            }

            public bool IsNone => Left <= 0f && Right <= 0f && Bottom <= 0f && Top <= 0f;

            public bool Equals(Insets o)
                => Mathf.Approximately(Left, o.Left) && Mathf.Approximately(Right, o.Right)
                && Mathf.Approximately(Bottom, o.Bottom) && Mathf.Approximately(Top, o.Top);
        }

        /// <summary>
        /// The insets for a canvas, in that canvas's own units.
        ///
        /// Degenerate readings are treated as "nothing in the way" rather than trusted. A
        /// zero-sized screen or safe area happens briefly during a resize and on some Android
        /// devices on the first frame after a rotation, and an inset derived from it would
        /// push the whole header off the top of the display.
        /// </summary>
        public static Insets For(Canvas canvas)
        {
            int w = Screen.width, h = Screen.height;
            if (w <= 0 || h <= 0) return default;

            var safe = Screen.safeArea;
            if (safe.width <= 0f || safe.height <= 0f) return default;

            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            if (scale <= 0f) scale = 1f;

            return new Insets(Mathf.Max(0f, safe.xMin) / scale,
                              Mathf.Max(0f, w - safe.xMax) / scale,
                              Mathf.Max(0f, safe.yMin) / scale,
                              Mathf.Max(0f, h - safe.yMax) / scale);
        }

        /// <summary>The insets for the game's canvas.</summary>
        public static Insets Current => For(Flow.Canvas);

        public static float Top => Current.Top;
        public static float Bottom => Current.Bottom;

        /// <summary>
        /// A full-screen node inset to the safe area, for a screen to build its chrome into.
        ///
        /// <para>
        /// It re-applies itself rather than measuring once. iOS reports its safe area a frame
        /// or two after launch on a cold start, Android reports a different one when the
        /// gesture bar appears, and a tablet in split view is resized while the app is running
        /// — so a value read in <c>Build</c> and never looked at again is right most of the
        /// time and wrong exactly when somebody is watching. The check is one rect comparison
        /// per frame per open screen.
        /// </para>
        /// </summary>
        public static RectTransform Node(string name, Transform parent)
        {
            var rt = UIKit.Node(name, parent);
            rt.gameObject.AddComponent<SafeAreaFitter>();
            return rt;
        }
    }

    /// <summary>
    /// Keeps one stretched <see cref="RectTransform"/> inside the display's safe area.
    ///
    /// Attached by <see cref="SafeArea.Node"/>; there is no reason to add one by hand.
    /// </summary>
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rt;
        Canvas _canvas;
        SafeArea.Insets _applied;
        bool _ever;

        void Awake()
        {
            _rt = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();
            Apply();
        }

        // The canvas is found again on enable because a screen is built before it is
        // parented in some flows, and a fitter with no canvas would divide by a scale of 1
        // and inset by raw device pixels — which on a 3x display is three times too much.
        void OnEnable()
        {
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            Apply();
        }

        void Update() => Apply();

        void Apply()
        {
            if (_rt == null) return;

            var insets = SafeArea.For(_canvas);
            if (_ever && insets.Equals(_applied)) return;

            _applied = insets;
            _ever = true;

            _rt.anchorMin = Vector2.zero;
            _rt.anchorMax = Vector2.one;
            _rt.offsetMin = new Vector2(insets.Left, insets.Bottom);
            _rt.offsetMax = new Vector2(-insets.Right, -insets.Top);
        }
    }
}
