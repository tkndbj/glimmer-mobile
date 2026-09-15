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
    /// Crests are generated rather than painted wherever they can be, for the reason
    /// <see cref="Art.Gem"/> gives — nothing to register, nothing to scope, and no frame
    /// where the box shows a white rectangle because the art had not arrived.
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
        /// A ring of tally marks that lights round as the ladder is climbed.
        ///
        /// What the keeper's season wears, and the reason it exists rather than the flower:
        /// the unit is a <em>mark</em>, so the crest that counts them is the one picture on
        /// the page that says what the number beside it means without a caption.
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
                case Watch: PaintWatch(host, size, tint, progress01); return;
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
        /// A ring of twelve tally marks, lighting clockwise from the top, round a core that
        /// grows with the track.
        ///
        /// <para>
        /// <b>Twelve rather than one per rung.</b> A forty-rung season would put forty pips
        /// on a 150-unit disc — measured, they touch — and a ring nobody can count is a
        /// texture rather than a tally. Twelve reads at a glance and divides the ladder into
        /// something a player can say out loud ("three round"), which is the same argument
        /// the hub's own bar makes for pipping every fifth rung rather than every one.
        /// </para>
        /// <para>
        /// The lit ones are ceilinged rather than rounded, so the first mark a player earns
        /// lights one immediately: a crest that stays dark through the first three rungs is
        /// a crest that reads as broken before it reads as empty.
        /// </para>
        /// </summary>
        static void PaintWatch(RectTransform host, float size, Color tint, float progress01)
        {
            const int Marks = 12;

            float done = Mathf.Clamp01(progress01);
            int lit = done <= 0f ? 0 : Mathf.Clamp(Mathf.CeilToInt(done * Marks), 1, Marks);

            // The ring the marks sit on, dark, so an unlit pip reads as a place for one
            // rather than as an absence.
            UIKit.Img("Band", host, Art.Ring(128, 9f), new Color(.06f, .10f, .16f, .85f),
                      Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);

            float radius = size * .40f;
            float pip = size * .13f;

            for (int i = 0; i < Marks; i++)
            {
                // Clockwise from the top, which is the only direction a dial may fill.
                float angle = Mathf.PI * .5f - i * (Mathf.PI * 2f / Marks);
                var at = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);

                bool on = i < lit;

                if (on)
                {
                    UIKit.Img("G" + i, host, Art.Glow(64, 2f), Pal.A(tint, .55f),
                              Vector2.one * (pip * 2.2f), new Vector2(.5f, .5f), at);
                }

                UIKit.Img("M" + i, host, Art.Disc(64),
                          on ? Pal.A(Color.Lerp(tint, Color.white, .30f), 1f)
                             : new Color(.26f, .32f, .40f, .92f),
                          Vector2.one * pip, new Vector2(.5f, .5f), at);
            }

            // The core, growing with the track rather than sitting at a fixed size — a full
            // crest and an empty one differ in more than which pips are lit.
            float core = size * .26f * (.34f + .66f * done);

            UIKit.Img("Core", host, Art.Glow(96, 2f), Pal.A(Pal.Sun, .40f),
                      Vector2.one * (core * 2.1f), new Vector2(.5f, .5f), Vector2.zero);
            UIKit.Img("Eye", host, Art.Disc(64), Pal.Sun,
                      Vector2.one * core, new Vector2(.5f, .5f), Vector2.zero);
            UIKit.Img("Gloss", host, Art.Disc(32), new Color(1f, 1f, 1f, .55f),
                      Vector2.one * (core * .40f), new Vector2(.5f, .5f),
                      new Vector2(-core * .17f, core * .17f));
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
