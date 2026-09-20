using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// How much of the bottom of the display the on-screen keyboard has taken, and the fitter
    /// that keeps a panel clear of it.
    ///
    /// <para>
    /// <b>This is <see cref="SafeArea"/>'s argument about a different intruder.</b> A cutout
    /// takes the top of the display and never moves; a keyboard takes the bottom, arrives when
    /// a field is focused and goes again — but the measurement is the same measurement and the
    /// trap is the same trap. <see cref="TouchScreenKeyboard.area"/> is reported in device
    /// pixels, everything here is laid out in canvas units, and dividing by the canvas's own
    /// scale factor is the step a hand-tuned margin skips. A constant chosen to clear one
    /// phone's keyboard is wrong on every other phone, and wrong in the invisible direction in
    /// the Editor, where there is no keyboard at all.
    /// </para>
    /// <para>
    /// <b>Only the height is read, and that is deliberate.</b> The rect's origin has meant
    /// different things on the two platforms across Unity versions — y measured from the top on
    /// one and from the bottom on the other — and a reading that is upside down puts a panel off
    /// the top of the screen rather than merely in the wrong place. A height needs no origin,
    /// and the one fact a soft keyboard cannot disagree about is that it grows from the bottom
    /// edge. So the height is the whole of what this asks for.
    /// </para>
    /// <para>
    /// <b>A reading of nothing is the ordinary answer</b>, exactly as an inset of zero is on a
    /// display with no cutout: the Editor, every desktop build and every device with the
    /// keyboard down all report nought, and a panel carrying a fitter is then pixel-identical to
    /// one without. That property is what makes it safe to attach to every modal rather than to
    /// the two that happen to hold a field today.
    /// </para>
    /// </summary>
    public static class SoftKeyboard
    {
        /// <summary>
        /// A keyboard covering more of the display than this is not a keyboard, it is a bad
        /// reading — the shape a resized activity or a mid-animation query produces. Refused
        /// rather than clamped: a clamp would still shove the panel most of the way up, and
        /// being wrong by a whole screen is worse than being wrong by a keyboard.
        /// </summary>
        const float MaxShare = .85f;

        /// <summary>
        /// How far above the keyboard a lifted panel sits. Air rather than a seam — a panel
        /// whose bottom edge touches the keyboard's top reads as being under it.
        /// </summary>
        public const float Margin = 40f;

        /// <summary>The room the keyboard has taken at the bottom of a canvas, in that canvas's own units.</summary>
        public static float RoomFor(Canvas canvas)
        {
            if (!TouchScreenKeyboard.visible) return 0f;

            float pixels = TouchScreenKeyboard.area.height;

            // Nought until the keyboard has finished animating in, and on any platform with no
            // soft keyboard to report.
            if (pixels <= 1f) return 0f;

            int height = Screen.height;
            if (height <= 0 || pixels > height * MaxShare) return 0f;

            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            if (scale <= 0f) scale = 1f;

            return pixels / scale;
        }

        /// <summary>The room the keyboard has taken on the game's canvas.</summary>
        public static float Room => RoomFor(Flow.Canvas);

        /// <summary>
        /// Keeps one panel above the keyboard, sliding it up when the keyboard arrives and back
        /// down when it goes. Attached by <c>ModalView.MakePanel</c>; there is no reason to add
        /// one by hand.
        /// </summary>
        public static KeyboardLift Lift(RectTransform panel, float margin = Margin)
        {
            if (panel == null) return null;

            var fitter = panel.gameObject.AddComponent<KeyboardLift>();
            fitter.Seat(panel.anchoredPosition, margin);
            return fitter;
        }
    }

    /// <summary>
    /// Holds a panel above the on-screen keyboard.
    ///
    /// <para>
    /// <b>It moves the panel and nothing else.</b> The scrim stays where it is, because a scrim
    /// that shifted with the panel would leave a strip of undimmed screen along one edge; and
    /// the panel's rest position is remembered rather than read back each frame, so the lift is
    /// always measured from where the layout put it rather than from wherever the last frame
    /// left it. Reading it back would integrate this component's own output and the panel would
    /// walk up the screen.
    /// </para>
    /// <para>
    /// <b>It never pushes the head off the top.</b> The lift is capped by the room above the
    /// panel, so on a display too short to hold the panel and the keyboard at once the panel
    /// stops against the top edge instead of climbing out of sight — which is also the honest
    /// answer if a platform ever reports a keyboard taller than the space there is.
    /// </para>
    /// <para>
    /// <b>In <c>LateUpdate</c>, on unscaled time.</b> After the tweens, because the entrance
    /// still owns the panel on the frames this is first asked; and unscaled because the ads
    /// plugin's consent stub leaves <c>Time.timeScale</c> at nought, which would otherwise
    /// freeze the slide with the panel half way up.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardLift : MonoBehaviour
    {
        /// <summary>How fast the slide settles — about a fifth of a second, which is roughly what a keyboard takes.</summary>
        const float Rate = 14f;

        /// <summary>Under this many units of difference the slide is finished, rather than approaching for ever.</summary>
        const float Settled = .5f;

        RectTransform _rt;
        Canvas _canvas;
        Vector2 _rest;
        float _margin = SoftKeyboard.Margin;
        float _lift;

        void Awake()
        {
            _rt = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();
        }

        // Found again on enable for SafeAreaFitter's reason: a panel is built before it is
        // parented in some flows, and a fitter with no canvas would divide by a scale of one and
        // lift by raw device pixels, which on a 3x display is three times too far.
        void OnEnable()
        {
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
        }

        /// <summary>Where the layout put the panel, and how much air to leave above the keyboard.</summary>
        internal void Seat(Vector2 rest, float margin)
        {
            if (_rt == null) _rt = (RectTransform)transform;
            _rest = rest;
            _margin = margin;
        }

        void LateUpdate()
        {
            if (_rt == null) return;

            float want = Wanted();

            _lift = Mathf.Abs(want - _lift) <= Settled
                ? want
                : Mathf.Lerp(_lift, want, 1f - Mathf.Exp(-Rate * Time.unscaledDeltaTime));

            // Nothing is written while the panel is at rest and the keyboard is down, which is
            // the whole of the time on every panel that holds no field.
            if (_lift == 0f && _rt.anchoredPosition == _rest) return;

            _rt.anchoredPosition = new Vector2(_rest.x, _rest.y + _lift);
        }

        /// <summary>How far this panel has to rise for its own bottom edge to clear the keyboard.</summary>
        float Wanted()
        {
            float room = SoftKeyboard.RoomFor(_canvas);
            if (room <= 0f) return 0f;

            var parent = _rt.parent as RectTransform;
            if (parent == null) return 0f;

            var box = parent.rect;
            float half = _rt.rect.height * .5f;

            float lift = box.yMin + room + _margin - (_rest.y - half);
            if (lift <= 0f) return 0f;

            float headroom = box.yMax - _margin - (_rest.y + half);
            return headroom <= 0f ? 0f : Mathf.Min(lift, headroom);
        }
    }
}
