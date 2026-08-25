using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Draws a hand tracing a route, over and over, for as long as whatever built it is alive.
    ///
    /// <para>
    /// The route is given in the host's own space — real points on the real board, worked out
    /// by whoever knows what is being taught — so this type holds no opinion about grids,
    /// cells or modes. All it knows is that a finger comes down at one end, travels, and lifts
    /// off at the other, and <see cref="CoachStroke"/> owns when each of those happens.
    /// </para>
    /// <para>
    /// <b>One looping tween, no state.</b> Every frame reads the whole gesture out of a single
    /// elapsed time, which is what makes it safe on a panel with several exits: there is
    /// nothing scheduled to cancel, and killing the owner kills the demonstration wherever it
    /// had got to. A chain of <c>Tween.After</c> beats would have to be unwound correctly on
    /// every one of those exits, and this project has already recorded what that costs.
    /// </para>
    /// <para>
    /// The ink behind the fingertip is a row of dots rather than a drawn line, and that is
    /// deliberate: a solid line is what a <em>finished</em> channel looks like on this board,
    /// and a demonstration that leaves one behind would be showing the player a channel they
    /// have not drawn. Dots read as a path being suggested.
    /// </para>
    /// </summary>
    public static class CoachHand
    {
        /// <summary>
        /// Where the fingertip sits in <see cref="Art.Hand"/>, as a pivot.
        ///
        /// <para>
        /// Derived from that glyph's geometry rather than eyeballed, and it has to move whenever
        /// the finger does — the whole hand is positioned by this point, so a stale one slides
        /// the fingertip off the route it is tracing and the demonstration quietly stops pointing
        /// at anything. Run the tip of the index cone back through <c>HandSd</c>'s tilt to
        /// re-derive it.
        /// </para>
        /// </summary>
        static readonly Vector2 Fingertip = new Vector2(.244f, .929f);

        /// <summary>Drawn about the size of one board cell — a hand that dwarfs the grove
        /// obscures the very thing it is pointing at.</summary>
        const float HandSize = 156f;

        /// <summary>How far the hand rises off the board between strokes.</summary>
        const float Rise = 38f;

        const float DotSize = 18f;

        /// <summary>
        /// Starts a demonstration under <paramref name="parent"/>.
        /// </summary>
        /// <param name="route">Points in the parent's local space, in the order they are visited.</param>
        /// <param name="tint">The colour of the pair being demonstrated, so the ink says whose it is.</param>
        /// <param name="cells">How far the route reaches in board cells, which decides its pace.</param>
        /// <param name="owner">Killed with this; the tween never outlives it.</param>
        public static RectTransform Show(RectTransform parent, IList<Vector2> route, Color tint,
                                         int cells, UnityEngine.Object owner)
        {
            if (parent == null || route == null || route.Count < 2) return null;

            var lengths = new float[route.Count - 1];
            float total = 0f;
            for (int i = 0; i < lengths.Length; i++)
            {
                lengths[i] = Vector2.Distance(route[i], route[i + 1]);
                total += lengths[i];
            }
            if (total <= 0f) return null;

            var root = UIKit.Node("Coach", parent);

            // Bounded by the route's own length rather than by the dot spacing, for
            // GridView's reason: a route across a wide grove must not cost more objects than
            // one across a narrow one, and nothing here is worth a hundred images.
            int dots = Mathf.Clamp(Mathf.RoundToInt(cells * 2.2f), 5, 28);
            var ink = new Image[dots];
            var at = new float[dots];

            for (int i = 0; i < dots; i++)
            {
                at[i] = (i + .5f) / dots;
                var p = PointOn(route, lengths, at[i]);

                ink[i] = UIKit.Img("Ink" + i, root, Art.Disc(32), Pal.A(tint, 0f),
                                   Vector2.one * DotSize, new Vector2(.5f, .5f), p);
            }

            var hand = UIKit.Img("Hand", root, Art.Hand(160), Color.white,
                                 Vector2.one * HandSize, new Vector2(.5f, .5f), route[0]);
            var hrt = hand.rectTransform;
            hrt.pivot = Fingertip;
            hrt.anchoredPosition = route[0];

            float draw = CoachStroke.DrawSeconds(cells);
            float cycle = CoachStroke.Cycle(draw);

            // Linear and looped without ping-pong: the easing belongs to the gesture, which
            // CoachStroke has already applied, and a demonstration played backwards every
            // other repeat would be teaching the route in reverse.
            Tween.Run(cycle, Ease.Linear, t =>
            {
                if (!hand) return;

                var beat = CoachStroke.At(t * cycle, draw);
                var p = PointOn(route, lengths, beat.Along);

                hrt.anchoredPosition = p + new Vector2(0f, beat.Lift * Rise);
                hrt.localScale = Vector3.one * (1f + beat.Lift * .10f - beat.Press * .09f);
                hand.color = new Color(1f, 1f, 1f, beat.Alpha);

                for (int i = 0; i < ink.Length; i++)
                {
                    if (!ink[i]) continue;

                    // A dot lights just as the fingertip reaches it rather than all at once,
                    // so the ink reads as being laid down by the hand and not as a route the
                    // hand happens to be following.
                    float lit = Mathf.Clamp01((beat.Trail - at[i]) / .07f) * beat.TrailAlpha;
                    ink[i].color = Pal.A(tint, lit * .85f);
                    ink[i].rectTransform.localScale = Vector3.one * (.55f + lit * .45f);
                }
            }, owner, "coach").Loop(-1, false);

            return root;
        }

        static Vector2 PointOn(IList<Vector2> route, IReadOnlyList<float> lengths, float along)
        {
            if (!CoachStroke.Walk(lengths, along, out int seg, out float f)) return route[0];
            return Vector2.Lerp(route[seg], route[seg + 1], f);
        }
    }
}
