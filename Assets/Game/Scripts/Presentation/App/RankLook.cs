using GlimmerGrove.Ranks;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The metal a rung wears: one accent colour per rank, so seven badges read as a ladder
    /// rather than as seven pictures on seven identical blue cards.
    ///
    /// <para>
    /// <b>This exists because the page had no colour in it and the art did.</b> The badges are
    /// copper, silver, gold, violet, crimson, ice and prism — a ramp anybody can read at a
    /// glance — and every card they stood on was the same navy plate with the same gold rim, so
    /// the one thing the art already said was the one thing the screen threw away. The accent
    /// is spent on the furniture the badge stands in (its seat, its rim, its glow, its ordinal
    /// chip and the link to the rung below), never on the badge itself: the picture is finished
    /// art and tinting it would be <c>Image.color</c>'s multiply taking a saturated metal toward
    /// black along its own hue (invariant 44g).
    /// </para>
    /// <para>
    /// <b>It is arithmetic on the ordinal and never a choice</b>, which is invariant 7c's shape
    /// said about a ladder instead of a chapter: a retune that adds a rung, renames one or
    /// reorders the lot draws correctly with no table to keep in step, and a ladder longer than
    /// the ramp wraps rather than failing. A map keyed on rung id would be a second content file
    /// nothing gates — <c>check_ranks</c> walks derived names precisely so a rung's art cannot
    /// go missing (invariant 52f), and a hand-keyed colour table would put that back.
    /// </para>
    /// <para>
    /// <b>Measured off the badges rather than typed</b> (invariant 44b): each value is the mean
    /// of the most chromatic decile of its own PNG's opaque pixels, lifted to a usable value.
    /// Two were then settled by eye and it is worth saying which and why — <c>silverwatch</c>
    /// measures as the blue of its gem inlays where the badge plainly reads as steel, and
    /// <c>gemfire</c> measures as a blue indistinguishable from <c>frozencrest</c>'s because it
    /// is a prism spread across cyan, blue and violet. Gemfire takes the cyan end, which is the
    /// one hue nothing below it wears.
    /// </para>
    /// </summary>
    public static class RankLook
    {
        /// <summary>
        /// The ramp, in ladder order. Seven entries because seven rungs ship; an eighth rung
        /// would wear the first metal again rather than draw nothing.
        /// </summary>
        static readonly Color[] Metals =
        {
            Pal.Hex("#F08A46"),   // 1 Cinderling  — copper
            Pal.Hex("#C6D8EE"),   // 2 Silverwatch — steel
            Pal.Hex("#FFB524"),   // 3 Goldbrand   — gold
            Pal.Hex("#9A4CF2"),   // 4 Duskcrown   — violet
            Pal.Hex("#F64A38"),   // 5 Fireheart   — crimson
            Pal.Hex("#2F9CFF"),   // 6 Frozencrest — ice
            Pal.Hex("#3BE9D8"),   // 7 Gemfire     — prism, at its cyan end
        };

        /// <summary>
        /// The accent for a rung at <paramref name="ordinal"/>, which is 1-based as
        /// <see cref="RankDefinition.Ordinal"/> is. Anything outside the ladder answers the
        /// first metal rather than throwing — a colour is not worth a crash, and the caller
        /// that could pass a nought is the one drawing an account with no rank at all.
        /// </summary>
        public static Color Metal(int ordinal)
            => Metals[Mathf.Max(0, ordinal - 1) % Metals.Length];

        /// <summary>The accent for a rung, or gold for none — see <see cref="Metal(int)"/>.</summary>
        public static Color Metal(RankDefinition rung)
            => rung == null ? Pal.Gold : Metal(rung.Ordinal);

        /// <summary>
        /// How a badge nobody has earned yet is drawn where the game shows one anyway: the
        /// first rung, ghosted, standing in for the rank an account has not reached.
        ///
        /// <para>
        /// <b>Alpha and never a tint</b>, because <c>Image.color</c> is a multiply and takes
        /// these saturated metals toward black along their own hue — a ghosted bronze done with
        /// a tint is mud (invariant 44g).
        /// </para>
        /// <para>
        /// <b>And it is the one transparent thing on the ranks page, deliberately.</b> That
        /// page's rule is that an unearned rung recedes by <em>value</em> and never by alpha,
        /// because a faded card reads as art that failed to load — but this is not a card
        /// standing empty, it is a picture of the badge you are about to earn with the word
        /// <em>Unranked</em> beside it, which is an invitation rather than a hole. An empty
        /// medallion invites nobody.
        /// </para>
        /// <para>
        /// <b>Named here because three screens draw it</b> — <see cref="RankBadge"/> under the
        /// map's back key, the profile's medallion and the ranks page's hero. The first two
        /// still carry their own copy of the figure; folding them in is a rename in two files
        /// and no behaviour, and is worth doing the next time either is opened.
        /// </para>
        /// </summary>
        public static readonly Color Ghost = new Color(1f, 1f, 1f, .38f);
    }
}
