using System;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// One token thrown from somewhere to somewhere, on an arc.
    ///
    /// <para>
    /// This is <see cref="Payout"/>'s flight, lifted out whole because a second caller
    /// appeared — the daily chest, whose rewards fly out of the panel and into the hub's
    /// own resource pills. The two want an identical throw and nothing else in common:
    /// a Payout owns a chip with a number on it, the chest owns nothing and is aiming at
    /// somebody else's readout. Copying forty lines of bezier, spin and scale into the
    /// second one would have left the game with two flights that started identical and
    /// drifted the first time either was retuned.
    /// </para>
    /// <para>
    /// It knows nothing about what is being paid. It takes two points and a sprite, and
    /// calls back when the token arrives — every decision about what that means belongs
    /// to the caller.
    /// </para>
    /// </summary>
    public static class TokenFlight
    {
        /// <summary>
        /// Throws one token from <paramref name="from"/> to <paramref name="to"/>, both in
        /// <paramref name="space"/>'s own coordinates, and destroys it on arrival.
        /// </summary>
        /// <param name="index">
        /// Which token of the handful this is. Only used to alternate the side the arc
        /// bows to, which is what stops a handful tracing one rope.
        /// </param>
        public static void Throw(RectTransform space, Vector2 from, Vector2 to,
                                 Sprite token, Color tint, float size,
                                 int index, float delay, float flight, Action onLand)
        {
            if (space == null) { onLand?.Invoke(); return; }

            var img = UIKit.Img("Tok", space, token, tint,
                                Vector2.one * size, new Vector2(.5f, .5f), from);
            img.preserveAspect = true;
            var rt = (RectTransform)img.transform;

            // Scattered at the source rather than stacked on it, so seven tokens read as a
            // handful thrown and not as one token drawn seven times.
            Vector2 start = from + new Vector2(UnityEngine.Random.Range(-90f, 90f),
                                               UnityEngine.Random.Range(-46f, 46f));
            rt.anchoredPosition = start;
            rt.localScale = Vector3.zero;

            // A straight line between two points on a panel reads as a UI element sliding.
            // The sideways control point is what makes it an arc, and alternating its side
            // is what stops the handful tracing one rope.
            Vector2 span = to - start;
            Vector2 dir = span.sqrMagnitude < 1f ? Vector2.up : span.normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x) * (index % 2 == 0 ? 1f : -1f);
            Vector2 ctrl = (start + to) * .5f
                         + perp * UnityEngine.Random.Range(70f, 200f)
                         + Vector2.up * UnityEngine.Random.Range(40f, 130f);

            float spin = UnityEngine.Random.Range(-220f, 220f);

            // InQuad, not a symmetric ease: the token hangs as it leaves and accelerates
            // into the target. Decelerating on arrival is what makes a magnet look like a lift.
            Tween.Run(flight, Ease.InQuad, t =>
            {
                if (!rt) return;
                float u = 1f - t;
                rt.anchoredPosition = u * u * start + 2f * u * t * ctrl + t * t * to;
                rt.localScale = Vector3.one * Mathf.Lerp(1.2f, .72f, t * t) * Mathf.Min(1f, t * 6f);
                rt.localRotation = Quaternion.Euler(0f, 0f, spin * t);
            }, img).Delay(delay).OnDone(() =>
            {
                if (img) UnityEngine.Object.Destroy(img.gameObject);
                onLand?.Invoke();
            });
        }

        /// <summary>
        /// Where <paramref name="target"/> sits in <paramref name="space"/>'s own coordinates
        /// — the number a child of <paramref name="space"/> anchored at its centre would need.
        ///
        /// Measured through world space rather than read off an anchoredPosition, because the
        /// two ends are routinely anchored to different edges of different objects and their
        /// local numbers are not in the same frame. It is also what lets the chest aim at a
        /// pill belonging to the screen underneath it.
        /// </summary>
        public static Vector2 LocalIn(RectTransform space, Transform target)
        {
            Vector3 local = space.InverseTransformPoint(target.position);
            Vector2 centre = space.rect.center;
            return new Vector2(local.x - centre.x, local.y - centre.y);
        }
    }
}
