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
        /// <summary>Drawn about the size of one board cell — a hand that dwarfs the grove
        /// obscures the very thing it is pointing at.</summary>
        const float HandSize = 156f;

        /// <summary>How far the hand rises off the board between strokes.</summary>
        const float Rise = 38f;

        /// <summary>
        /// How thick the demonstrated line is, against <c>WeaveView.LiveThick</c>'s .24 of a
        /// cell. Deliberately the same weight as the line under a real finger — the ink here is
        /// pretending to be exactly that, so a different thickness would read as a different
        /// thing.
        /// </summary>
        const float InkThick = 15f;

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

            // The ink is a line, not a row of dots, and that is the whole of what it is for.
            // It used to be discs spaced along the route, which reads as a dotted trail — a
            // *path marker*, the thing a map draws to say "go this way". What is being taught
            // here is that the player draws, so the ink has to be the mark a finger leaves: one
            // capsule per straight leg, grown from its own start, with a disc at every corner
            // to round it. That is `WeaveView.Link` and `WeaveView.Knuckle`, deliberately, so
            // the demonstration and the real thing cannot drift apart.
            //
            // It also costs *fewer* objects than the dots did, and bounded by corners rather
            // than by length: an elbow is two legs however wide the grove is.
            // Every point handed in is already a turn — the caller collapses straight runs,
            // because which cells are collinear is a fact about a board and belongs where that
            // can be proved (WeaveLayout.Corners).
            int legs = route.Count - 1;

            var ink = new Image[legs];
            var from = new float[legs];
            var span = new float[legs];
            var joint = new Image[route.Count];

            float run = 0f;
            for (int i = 0; i < legs; i++)
            {
                var a = route[i];
                var b = route[i + 1];
                var delta = b - a;
                float length = delta.magnitude;

                from[i] = run / total;
                run += length;
                span[i] = Mathf.Max(1e-4f, length / total);

                // Pivoted at the leg's own start and rotated to point along it, so growing it
                // is one number: a capsule that grew from its centre would be a line drawn from
                // the middle outwards, which is not how anybody draws.
                ink[i] = UIKit.Img("Ink" + i, root, Art.Capsule(24, 96), Pal.A(tint, 0f),
                                   new Vector2(InkThick, 0f), new Vector2(.5f, 0f), a);
                ink[i].rectTransform.localRotation =
                    Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f);
            }

            for (int i = 0; i < route.Count; i++)
                joint[i] = UIKit.Img("Joint" + i, root, Art.Disc(64), Pal.A(tint, 0f),
                                     Vector2.one * InkThick, new Vector2(.5f, .5f), route[i]);

            var hand = UIKit.Img("Hand", root, Art.Hand(160), Color.white,
                                 Vector2.one * HandSize, new Vector2(.5f, .5f), route[0]);
            var hrt = hand.rectTransform;
            hrt.pivot = Art.HandFingertip;
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

                // Each leg is drawn out to wherever the fingertip has got to, so the line is
                // laid down by the hand rather than revealed behind it.
                for (int i = 0; i < ink.Length; i++)
                {
                    if (!ink[i]) continue;

                    float along = Mathf.Clamp01((beat.Trail - from[i]) / span[i]);
                    var rt = ink[i].rectTransform;
                    rt.sizeDelta = new Vector2(InkThick, span[i] * total * along);
                    ink[i].color = Pal.A(tint, along > 0f ? .85f * beat.TrailAlpha : 0f);
                }

                for (int i = 0; i < joint.Length; i++)
                {
                    if (!joint[i]) continue;

                    // The `> 0` matters for the first corner: the reach and the press both run
                    // with Trail at zero, so without it a dot sits on the board waiting to be
                    // pressed, which gives the answer away before the hand has arrived.
                    bool reached = beat.Trail > 1e-4f &&
                                   beat.Trail >= (i < from.Length ? from[i] : 1f) - 1e-4f;
                    joint[i].color = Pal.A(tint, reached ? .85f * beat.TrailAlpha : 0f);
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
