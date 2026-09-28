using System;
using System.Collections;
using GlimmerGrove.AssetPipeline;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The pointing hand a screen recording is played with: the owner's drawing, moved like a
    /// thumb.
    ///
    /// <para>
    /// <b>It is not <see cref="CoachHand"/>.</b> The coaching hand demonstrates a gesture on a
    /// loop and the board waits for the player to copy it; this one <em>is</em> the player. It
    /// travels to a gem, presses, drags it onto its neighbour and lifts, and the board answers
    /// the drag through the same door a finger reaches (<c>SiegeView.Swipe</c>) at the moment
    /// the fingertip has covered enough of the cell for a real drag to have fired. What a viewer
    /// sees is cause and effect in that order, which is the whole of what makes a recording read
    /// as somebody playing rather than as a board playing itself.
    /// </para>
    /// <para>
    /// <b>Every motion is a coroutine the screen yields on</b>, paced on the unscaled clock and
    /// stepped by hand rather than through <c>Tween</c>, so a move and the wait for it are one
    /// thing and nothing can be left running after the screen has moved on. The easing is a
    /// thumb's: a reach that starts fast and settles, a drag that starts slower than the reach,
    /// a press that dips the hand and puts a touch ring under the tip.
    /// </para>
    /// <para>
    /// <b>The fingertip is a pivot in sprite space, measured rather than typed.</b>
    /// <see cref="Fingertip"/> is read off the cut PNG by <c>Tools/make_showcase_hand.py</c>,
    /// whose <c>--check</c> refuses a drawing whose tip has moved from this constant, because a
    /// hand pressing a cell three pixels beside the one that moves is the one fault a viewer
    /// cannot un-see. Falls back to the procedural coaching hand if the picture is not resident
    /// (invariant 7b): a hand is never a white rectangle.
    /// </para>
    /// </summary>
    public sealed class ShowcaseHand
    {
        /// <summary>The picture's address under <c>Art/Ui/</c>.</summary>
        public const string Picture = "showcase_hand";

        /// <summary>Where the fingertip is in the picture, as a pivot: measured by the tool.</summary>
        public static readonly Vector2 Fingertip = new Vector2(.242f, .998f);

        /// <summary>The picture's height on the canvas. About a cell and a half, which is a thumb's.</summary>
        public const float Tall = 250f;

        /// <summary>How far the hand rises off the glass between touches.</summary>
        const float Lift = 22f;

        /// <summary>How the hand tilts, so the finger reads as coming in from the right hand.</summary>
        const float Tilt = -8f;

        /// <summary>The touch ring: its rest size and how far a press swells it.</summary>
        const float RingSize = 132f, RingSwell = 1.55f;

        readonly RectTransform _root, _hand;
        readonly Image _img, _shadow, _ring;
        readonly CanvasGroup _group;

        float _press;

        /// <summary>Where the fingertip is, in <see cref="Space"/>.</summary>
        public Vector2 At { get; private set; }

        /// <summary>The node every position here is measured in: a stretched child of the host.</summary>
        public RectTransform Space => _root;

        public bool Pressed => _press > .5f;

        ShowcaseHand(RectTransform root, RectTransform hand, Image img, Image shadow, Image ring,
                     CanvasGroup group)
        {
            _root = root;
            _hand = hand;
            _img = img;
            _shadow = shadow;
            _ring = ring;
            _group = group;
        }

        /// <summary>
        /// Builds the hand in <paramref name="host"/>, hidden, parked at <paramref name="at"/>.
        ///
        /// <b>In the screen's own content rather than on the board</b>, for the tutorial's
        /// pointer's reason: it outlives a cascade rebuilding the gems under it and is drawn
        /// above every layer of the board rather than inside one of them.
        /// </summary>
        public static ShowcaseHand Build(RectTransform host, Vector2 at)
        {
            var root = UIKit.Node("Hand", host);
            var group = UIKit.Group(root);
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // The ring sits under the hand so the press reads as glass lighting under a thumb.
            var ring = UIKit.Img("Touch", root, Art.Ring(128, 9f), Pal.A(Pal.Cream, 0f),
                                 Vector2.one * RingSize, new Vector2(.5f, .5f), at);
            ring.raycastTarget = false;

            var sprite = AssetLibrary.Peek<Sprite>(AssetManifest.Ui(Picture));
            var pivot = Fingertip;

            if (sprite == null)
            {
                sprite = Art.Hand(160);
                pivot = Art.HandFingertip;
            }

            float wide = Tall * sprite.rect.width / sprite.rect.height;
            var size = new Vector2(wide, Tall);

            // A soft shadow the hand's own shape, dropped down and right, so it floats.
            var shadow = UIKit.Img("Shadow", root, sprite, new Color(0f, 0f, 0f, .30f), size,
                                   new Vector2(.5f, .5f), at);
            shadow.raycastTarget = false;
            shadow.rectTransform.pivot = pivot;
            shadow.rectTransform.anchoredPosition = at + new Vector2(10f, -14f);
            shadow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Tilt);

            var img = UIKit.Img("Finger", root, sprite, Color.white, size, new Vector2(.5f, .5f), at);
            img.raycastTarget = false;

            var hand = img.rectTransform;
            hand.pivot = pivot;
            hand.anchoredPosition = at;
            hand.localRotation = Quaternion.Euler(0f, 0f, Tilt);

            var made = new ShowcaseHand(root, hand, img, shadow, ring, group) { At = at };
            made.Seat(at, 0f);
            return made;
        }

        public void Destroy()
        {
            if (_root) UnityEngine.Object.Destroy(_root.gameObject);
        }

        // ------------------------------------------------------------------ motions
        /// <summary>Fades the hand in over a beat.</summary>
        public IEnumerator Show(float seconds = .35f)
        {
            yield return Ramp(seconds, t => { if (_group) _group.alpha = t; });
        }

        public IEnumerator Hide(float seconds = .25f)
        {
            float from = _group ? _group.alpha : 0f;
            yield return Ramp(seconds, t => { if (_group) _group.alpha = from * (1f - t); });
        }

        /// <summary>
        /// How long a reach takes: a thumb crosses a phone in about a third of a second and
        /// never teleports, so short hops still take a beat.
        /// </summary>
        public static float Travel(Vector2 from, Vector2 to)
            => Mathf.Clamp(.30f + Vector2.Distance(from, to) / 1900f, .34f, .85f);

        /// <summary>
        /// Carries the hand to <paramref name="to"/>, lifted off the glass, with a thumb's
        /// easing: quick to leave, slow to arrive.
        /// </summary>
        public IEnumerator MoveTo(Vector2 to, float seconds = -1f)
        {
            var from = At;
            if (seconds < 0f) seconds = Travel(from, to);

            // A little bow in the path, so two reaches in a row do not draw one straight line.
            var side = new Vector2(-(to.y - from.y), to.x - from.x).normalized
                     * Mathf.Min(40f, Vector2.Distance(from, to) * .12f);

            yield return Ramp(seconds, t =>
            {
                float e = Ease.InOutSine(t);
                var p = Vector2.Lerp(from, to, e) + side * Mathf.Sin(t * Mathf.PI);
                Seat(p, 1f - _press);
            });

            Seat(to, 1f - _press);
        }

        /// <summary>Presses: the hand dips onto the glass and the touch ring lights under the tip.</summary>
        public IEnumerator Press(float seconds = .11f)
        {
            float from = _press;
            yield return Ramp(seconds, t =>
            {
                _press = Mathf.Lerp(from, 1f, Ease.OutQuad(t));
                Seat(At, 1f - _press);
            });
            _press = 1f;
            Seat(At, 0f);
        }

        /// <summary>Lifts: the hand comes off the glass and the ring fades.</summary>
        public IEnumerator Release(float seconds = .13f)
        {
            float from = _press;
            yield return Ramp(seconds, t =>
            {
                _press = Mathf.Lerp(from, 0f, Ease.OutQuad(t));
                Seat(At, 1f - _press);
            });
            _press = 0f;
            Seat(At, 1f);
        }

        /// <summary>
        /// Drags the pressed hand to <paramref name="to"/>, and calls <paramref name="atFraction"/>
        /// once the tip has covered <paramref name="fraction"/> of the way - which is where a
        /// real drag fires (<c>CellDrag.Threshold</c> is well under a cell), so the gems begin
        /// to move while the thumb is still travelling, exactly as they do under a finger.
        /// </summary>
        public IEnumerator DragTo(Vector2 to, float seconds, float fraction, Action atFraction)
        {
            var from = At;
            bool fired = false;

            yield return Ramp(seconds, t =>
            {
                float e = Ease.OutQuad(t);
                Seat(Vector2.Lerp(from, to, e), 0f);

                if (!fired && e >= fraction)
                {
                    fired = true;
                    atFraction?.Invoke();
                }
            });

            if (!fired) atFraction?.Invoke();
            Seat(to, 0f);
        }

        /// <summary>A whole tap: reach, press, act, hold a beat, lift.</summary>
        public IEnumerator Tap(Vector2 at, Action act, float hold = .06f)
        {
            yield return MoveTo(at);
            yield return Press(.14f);
            act?.Invoke();
            if (hold > 0f) yield return new WaitForSecondsRealtime(hold);
            yield return Release();
        }

        /// <summary>A second tap on the same spot, without the reach: press, act, lift.</summary>
        public IEnumerator TapAgain(Action act, float hold = .05f)
        {
            yield return Press(.11f);
            act?.Invoke();
            if (hold > 0f) yield return new WaitForSecondsRealtime(hold);
            yield return Release(.14f);
        }

        // ------------------------------------------------------------------ drawing
        /// <summary>Seats the fingertip at <paramref name="at"/>, <paramref name="lift"/> off the glass (0..1).</summary>
        void Seat(Vector2 at, float lift)
        {
            At = at;
            if (!_hand) return;

            lift = Mathf.Clamp01(lift);
            float scale = 1f + lift * .06f - (1f - lift) * .04f;

            _hand.anchoredPosition = at + new Vector2(0f, lift * Lift);
            _hand.localScale = Vector3.one * scale;

            if (_shadow)
            {
                // The shadow stays on the glass and drifts further from the hand as it lifts.
                _shadow.rectTransform.anchoredPosition = at + new Vector2(8f + lift * 10f, -10f - lift * 16f);
                _shadow.rectTransform.localScale = Vector3.one * scale;
                _shadow.color = new Color(0f, 0f, 0f, .18f + lift * .12f);
            }

            if (_ring)
            {
                float press = 1f - lift;
                _ring.rectTransform.anchoredPosition = at;
                _ring.rectTransform.localScale = Vector3.one * (.72f + press * (RingSwell - .72f));
                _ring.color = Pal.A(Pal.Cream, press * .62f);
            }
        }

        /// <summary>Steps <paramref name="apply"/> from 0 to 1 over <paramref name="seconds"/> on the unscaled clock.</summary>
        static IEnumerator Ramp(float seconds, Action<float> apply)
        {
            if (seconds <= 0f) { apply(1f); yield break; }

            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / seconds;
                apply(t < 1f ? t : 1f);
                yield return null;
            }
        }
    }
}
