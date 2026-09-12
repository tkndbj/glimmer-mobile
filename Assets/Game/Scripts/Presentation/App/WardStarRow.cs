using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A turret's place on the upgrade ladder, drawn as five stars with the earned ones lit.
    ///
    /// <para>
    /// <b>Five always, with the unearned ones dim rather than absent.</b> A row that grew as a
    /// player upgraded would say how far they have come and never how far there is to go — and
    /// how far there is to go is the whole reason a shelf shows a ladder at all. It is the same
    /// argument <c>ShopLadder</c> makes about a picture ladder being exactly as long as its shelf
    /// (invariant 18e).
    /// </para>
    /// <para>
    /// <b>Its own file because two screens draw it.</b> The shelf's cells and the preview panel
    /// both show the same row, and two of them is two designs a week later — <c>PieceCard</c>'s
    /// rule (invariant 16l).
    /// </para>
    /// </summary>
    public static class WardStarRow
    {
        /// <summary>How wide a star is drawn at the size the shelf's cells use.</summary>
        public const float Star = 26f;

        /// <summary>The gap between two stars, as a fraction of one.</summary>
        const float Gap = .22f;

        /// <summary>
        /// Lit and unlit.
        ///
        /// <b>The unearned star is drawn rather than left out</b>, and it is the kit's own hollow
        /// star rather than the filled one dimmed: a row of five where two are gold and three are
        /// outlines reads as "two of five" at a glance, where two tints of one shape reads as a
        /// row that has been faded.
        /// </summary>
        static readonly Color Lit = Pal.Gold;
        static readonly Color Unlit = new Color(1f, 1f, 1f, .45f);

        /// <summary>How wide a whole row is drawn at <paramref name="size"/> a star.</summary>
        public static float Width(float size) => WardStars.Most * size + (WardStars.Most - 1) * size * Gap;

        /// <summary>How far apart two stars stand at <paramref name="size"/>.</summary>
        public static float Step(float size) => size * (1f + Gap);

        /// <summary>
        /// Where the <paramref name="index"/>-th star stands, measured from the row's own middle.
        ///
        /// <b>Public because the upgrade ceremony drops a star into a slot</b>, and a screen
        /// working that position out for itself is a second copy of this row's arithmetic - which
        /// would land the falling star beside the one it is supposed to become the moment either
        /// the size or the gap moves. <see cref="Build"/> uses it too, so there is one.
        /// </summary>
        public static float XOf(int index, float size)
            => -Width(size) * .5f + size * .5f + index * Step(size);

        /// <summary>
        /// Draws the row centred on <paramref name="at"/>, measured from the parent's own anchor.
        ///
        /// <b>Nothing here is a control.</b> A star is a readout; what buys one is the panel's own
        /// button, so every piece is <c>raycastTarget = false</c> and a tap falls through to the
        /// cell underneath.
        /// </summary>
        public static RectTransform Build(RectTransform parent, Vector2 at, int stars,
                                          float size = Star)
        {
            float width = Width(size);

            var row = UIKit.Box("Stars", parent, new Vector2(width, size),
                                new Vector2(.5f, 1f), at);

            int lit = WardStars.Sane(stars);

            for (int i = 0; i < WardStars.Most; i++)
            {
                bool earned = i < lit;

                var star = UIKit.Img("S" + i, row,
                                     Art.S(earned ? "Ui/star_full" : "Ui/star_empty"),
                                     earned ? Lit : Unlit,
                                     new Vector2(size, size), new Vector2(.5f, .5f),
                                     new Vector2(XOf(i, size), 0f));
                star.raycastTarget = false;
            }

            return row;
        }
    }
}
