using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Draws the mark an event wears, and owns the list of marks that exist.
    ///
    /// <para>
    /// A manifest names a mark; this decides what that name looks like. Keeping the two
    /// apart is what lets an event pick its own without a code change and without the
    /// content author being able to break a screen: an unrecognised name lands on
    /// <see cref="Default"/> rather than on a missing sprite, so the worst a typo can do
    /// is draw the wrong flower. See <c>ManifestEventDto.icon</c>.
    /// </para>
    /// <para>
    /// Marks are generated rather than painted wherever they can be, for the reason
    /// <see cref="Art.Gem"/> gives — nothing to register, nothing to scope, and no frame
    /// where the box shows a white rectangle because the art had not arrived.
    /// </para>
    /// </summary>
    public static class EventMark
    {
        /// <summary>
        /// A flower that opens with the track. Written out rather than composed, because
        /// this string has to match what a manifest may say and a computed one is
        /// invisible to anyone reading either end.
        /// </summary>
        public const string Bloom = "bloom";

        /// <summary>What an event with no icon, or an icon this build has never heard of, wears.</summary>
        public const string Default = "";

        /// <summary>
        /// Fills <paramref name="host"/> with the mark, sized to it.
        ///
        /// <paramref name="progress01"/> is how far through the track the player is. Only
        /// marks that can say something with it use it; the rest ignore it, which is why it
        /// is a parameter here rather than a property of the caller's own drawing.
        /// </summary>
        public static void Paint(RectTransform host, string icon, Color tint, float progress01)
        {
            if (host == null) return;

            float size = Mathf.Min(host.sizeDelta.x, host.sizeDelta.y);

            switch (icon)
            {
                case Bloom: PaintBloom(host, size, tint, progress01); return;
                default: PaintStars(host, size, tint); return;
            }
        }

        /// <summary>
        /// Two rings of petals and a lit centre.
        ///
        /// <para>
        /// The layering is what makes it look like a flower rather than a blob, and it is
        /// three tinted copies of one mask rather than three textures: the outer ring in the
        /// event's own colour, a smaller one turned half a petal and lifted toward white so
        /// the petals read as overlapping, and a warm core. <see cref="Art.Bloom"/> caches by
        /// size and openness, so the inner ring shares nothing with the outer and both are
        /// generated once for the whole run.
        /// </para>
        /// <para>
        /// It opens with the track. That is the point of choosing a flower: a player who
        /// glances at the box learns where they are without reading the bar under it, and
        /// the mark stops being decoration.
        /// </para>
        /// </summary>
        static void PaintBloom(RectTransform host, float size, Color tint, float progress01)
        {
            float open = Mathf.Clamp01(progress01);
            int outer = Mathf.RoundToInt(size);
            int inner = Mathf.RoundToInt(size * .60f);

            UIKit.Img("Petals", host, Art.Bloom(outer, 6, open), Pal.A(tint, .97f),
                      Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);

            var mid = UIKit.Img("Inner", host, Art.Bloom(inner, 6, open),
                                Pal.A(Color.Lerp(tint, Color.white, .46f), .95f),
                                Vector2.one * (size * .60f), new Vector2(.5f, .5f), Vector2.zero);
            // half a petal, so the inner ring sits in the gaps of the outer one
            mid.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);

            // The core grows with the flower rather than sitting at a fixed size, or a bud
            // is mostly centre and reads as a coin.
            float core = size * .21f * (.30f + .70f * open);
            UIKit.Img("Core", host, Art.Disc(64), Pal.Sun,
                      Vector2.one * core, new Vector2(.5f, .5f), Vector2.zero);
            UIKit.Img("Gloss", host, Art.Disc(32), new Color(1f, 1f, 1f, .55f),
                      Vector2.one * (core * .40f), new Vector2(.5f, .5f),
                      new Vector2(-core * .17f, core * .17f));
        }

        /// <summary>The mark every event wore before there were any: a tinted trio of stars.</summary>
        static void PaintStars(RectTransform host, float size, Color tint)
        {
            var img = UIKit.Img("Stars", host, Art.S("Ui/ic_stars"), tint,
                                Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);
            img.preserveAspect = true;
        }
    }
}
