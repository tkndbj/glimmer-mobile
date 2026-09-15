using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Draws the crest a season wears, and owns the list of crests that exist.
    ///
    /// <para>
    /// A manifest names a crest; this decides what that name looks like. Keeping the two
    /// apart is what lets a season pick its own without a code change and without the
    /// content author being able to break a screen: an unrecognised name lands on
    /// <see cref="Default"/> rather than on a missing sprite, so the worst a typo can do
    /// is draw the wrong emblem. See <c>ManifestEventDto.icon</c>.
    /// </para>
    /// <para>
    /// <b>It is <c>SeasonCrest</c> rather than <c>EventMark</c> because a "mark" became a
    /// noun with a job.</b> A mark is what a claimed chest earns toward a season; the
    /// picture a season wears is its crest, and one word meaning both would be the sort of
    /// overload that reads fine to whoever wrote it and to nobody else.
    /// </para>
    /// <para>
    /// <b>The live crest is a bought sprite and the generated ones are what is left.</b> A
    /// generated emblem costs nothing to register and nothing to scope, which is <see
    /// cref="Art.Gem"/>'s bargain and is why the rest of this file draws rather than loads —
    /// and it buys a picture nobody drew. The crest a season actually wears is on the first
    /// screen after the splash and on a page selling a pass, so it is cut from the interface
    /// kit like every other surface either screen is made of; <see cref="Watch"/> records what
    /// that cost and what it bought.
    /// </para>
    /// </summary>
    public static class SeasonCrest
    {
        /// <summary>
        /// A flower that opens with the track. Written out rather than composed, because
        /// this string has to match what a manifest may say and a computed one is
        /// invisible to anyone reading either end.
        /// </summary>
        public const string Bloom = "bloom";

        /// <summary>
        /// The crown, which is what the keeper's season wears.
        ///
        /// <para>
        /// <b>The string is permanent and the picture behind it is not.</b> A manifest names a
        /// crest, so this name is content that has shipped; what it <em>looks like</em> is a
        /// decision this file owns and has already changed once — it was a ring of twelve pips
        /// filling clockwise, and the owner rejected it on sight. Re-pointing a crest costs
        /// nothing and renaming one is a content push, so the name stays.
        /// </para>
        /// </summary>
        public const string Watch = "watch";

        /// <summary>What a season with no icon, or an icon this build has never heard of, wears.</summary>
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
                case Watch: PaintCrest(host, size); return;
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

        /// <summary>
        /// The crown, drawn from `Ui/ic_season` and nothing else.
        ///
        /// <para>
        /// <b>A bought sprite rather than a generated shape, and the owner rejected the
        /// generated one on sight.</b> What stood here was a dark band carrying twelve pips
        /// that lit clockwise round a growing core — every part of it real, correct, and drawn
        /// by nobody, which beside a card cut from a licensed kit reads as a placeholder
        /// (invariant 49h, which the update wall's mark paid for first). It is cut by
        /// `Tools/make_season_crest.py`, out of the same kit every plate on both screens is cut
        /// from, and that tool records why a crown rather than any of the shields, stars and
        /// badges it was surveyed against.
        /// </para>
        /// <para>
        /// <b>It ignores both arguments, and only one of those is free.</b> The tint is a
        /// painted piece's own — a colour multiply over a gold crown on a violet plate gives a
        /// muddy one, which is `FeatureCard`'s finding said about an emblem instead of a plate.
        /// The progress is the real trade: the pips filled as the ladder filled, which the
        /// crown cannot do. What buys it back is that <em>both</em> callers already print the
        /// count and draw a bar directly under the crest, so the reading was being made twice
        /// and only one of the two was drawn by an artist. A crest says which season this is;
        /// the bar says how far through it the player is. The other shape — a crest lit as the
        /// track fills — is what invariant 37m refuses from the other end: it is dim on the
        /// first frame of every season anybody opens.
        /// </para>
        /// <para>
        /// <c>preserveAspect</c> is not optional. The crown is wider than it is tall and the
        /// sprite is square with symmetric margin, so it is the right picture in a square box
        /// and a squashed one without.
        /// </para>
        /// </summary>
        static void PaintCrest(RectTransform host, float size)
        {
            var img = UIKit.Img("Crest", host, Art.S("Ui/ic_season"), Color.white,
                                Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);
            img.preserveAspect = true;
        }

        /// <summary>The crest every season wore before there were any: a tinted trio of stars.</summary>
        static void PaintStars(RectTransform host, float size, Color tint)
        {
            var img = UIKit.Img("Stars", host, Art.S("Ui/ic_stars"), tint,
                                Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);
            img.preserveAspect = true;
        }
    }
}
