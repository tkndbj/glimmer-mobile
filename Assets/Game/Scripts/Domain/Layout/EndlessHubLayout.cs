namespace GlimmerGrove.Layout
{
    /// <summary>
    /// Where the pieces of a lane's hub sit: the medal carrying its record, the plate of lines
    /// saying what it is, and the key that starts it.
    ///
    /// <para>
    /// <b>Here rather than beside the screen, for <see cref="PanelStack"/>'s reason</b> (invariant
    /// 8a, earned a seventh time): whether two things on a screen overlap is arithmetic, and
    /// arithmetic inside a <c>MonoBehaviour</c> is arithmetic nothing can check. This one has the
    /// tightest budget of any stack in the game and the least room to be wrong in — it is drawn
    /// between two pieces of furniture that were sized without it, the map's header column above
    /// and the loadout shelf below.
    /// </para>
    /// <para>
    /// <b>Every number is a centre, measured down from the column's top edge</b>, which is
    /// <c>WheelPanel</c>'s hard-won rule: one number in a stack meaning something different from
    /// the rest drew a row through its neighbour with the test passing throughout, because the
    /// test checked arithmetic the panel did not use. Nothing here is a top or a bottom. The
    /// caller negates once, at the point of placement, because <c>UIKit.Box</c> counts upwards.
    /// </para>
    /// <para>
    /// <b>Everything each piece draws fits inside the box the stack gives it</b>, which is the
    /// other half of the rule and the one a render caught: a mark is measured as the shape it
    /// <em>draws</em> (<c>ProductCardBadges</c>' lesson), so the starburst behind the medal is the
    /// hero's own box exactly. Drawn any larger it stands outside the column, and the thing
    /// immediately above the column is the track pill, which nothing here can see.
    /// </para>
    /// </summary>
    public static class EndlessHubLayout
    {
        /// <summary>
        /// How many lines the hub's plate says.
        ///
        /// <b>A constant rather than a count of whatever resolves</b>, because the keys are
        /// derived (<c>GameTrack.PointKey</c>) and a missing string resolves to something: a hub
        /// that sized itself by asking would quietly draw two rows on the day one was mistyped.
        /// Three is what the band holds; a fourth fails <see cref="IsClear"/> rather than a phone.
        /// </summary>
        public const int Points = 3;

        // ------------------------------------------------------------------ the medal
        /// <summary>
        /// The hero block: a starburst, a medallion carrying the furthest wave, and a plate under
        /// it naming what the number is.
        ///
        /// <para>
        /// <b>The record is the hero because the record is what this lane is.</b> Every other lane
        /// in the game is a chain of levels and is drawn as one; this one is a single run played
        /// for how far it got, so the number it is graded on is the only thing on the screen worth
        /// making large. The first cut of this hub made an emblem the hero and printed the record
        /// as a line of text under it, which is the same screen with the subject buried.
        /// </para>
        /// <para>
        /// <b>Derived from the plate that hangs off its foot, never typed.</b> It was typed, and
        /// was one unit short of what it held — caught by <see cref="IsClear"/> rather than by a
        /// phone, which is the whole reason this arithmetic is not in the screen.
        /// </para>
        /// </summary>
        public static float HeroHeight => PlateDown + PlateHeight * .5f;

        /// <summary>
        /// The starburst behind the medal — <b>the hero's own box</b>, never larger. See the class
        /// note for why.
        /// </summary>
        public const float BurstSize = 310f;

        /// <summary>The medallion, and where its centre sits inside the hero block.</summary>
        public const float DiscSize = 222f;

        /// <summary>
        /// Half the burst, so the burst's top edge is the column's top edge and nothing the medal
        /// draws reaches above it.
        /// </summary>
        public const float DiscDown = BurstSize * .5f;

        /// <summary>The plate under the medal, and where it is read.</summary>
        public const float PlateWidth = 360f, PlateHeight = 78f, PlateDown = 284f;

        // ------------------------------------------------------------------ the lines
        /// <summary>
        /// The plate the three lines are read on, and the air inside it.
        ///
        /// <b>A plate rather than three loose rows</b>, which is the whole of what the second cut
        /// of this screen changed: text and marks floating on a patterned ground read as a
        /// settings page, where the same words inside a framed panel read as part of a game.
        /// </summary>
        public const float PanelWidth = 840f, PanelPad = 16f;

        /// <summary>One line's row, and the air between two of them.</summary>
        public const float RowHeight = 90f, RowGap = 6f;

        /// <summary>The framed seat a row's mark sits in, and the mark inside it.</summary>
        public const float SlotSize = 88f, IconSize = 68f;

        /// <summary>How tall the plate has to be to hold <see cref="Points"/> rows. Derived.</summary>
        public static float PanelHeight
            => PanelPad * 2f + RowHeight * Points + RowGap * (Points - 1);

        // ------------------------------------------------------------------ the way in
        /// <summary>The way in. The hub's own key and the home screen's are the same size.</summary>
        public const float ButtonWidth = 620f, ButtonHeight = 178f;

        // ------------------------------------------------------------------ the air
        /// <summary>The gaps: under the medal, and above the key.</summary>
        const float HeroGap = 24f, PanelGap = 34f;

        /// <summary>
        /// Air the column leaves under the header before it begins.
        ///
        /// <b>Not a gap inside the stack, which is why it is not one of the two above.</b> It is
        /// what the column owes the furniture it is drawn beneath: the top of the column is a
        /// spiked starburst and the bottom of the header is a pill, and two things a few units
        /// apart read as touching whatever the arithmetic says.
        /// </summary>
        public const float HeadClear = 36f;

        /// <summary>
        /// Where the column sits in a band with room to spare: a little above centre.
        ///
        /// <b>A tall phone's slack has to go somewhere, and the foot is where a screen wants
        /// it.</b> The loadout shelf's tab stands proud of its own plate, and the eye should end
        /// on the key rather than on a strip of empty ground under it.
        /// </summary>
        public const float Lift = .42f;

        // ------------------------------------------------------------------ the stack
        public static float HeroCentre => HeroHeight * .5f;

        public static float PanelCentre => HeroHeight + HeroGap + PanelHeight * .5f;

        /// <summary>Where row <paramref name="index"/> is read, measured down from the plate's top.</summary>
        public static float RowCentre(int index)
            => PanelPad + RowHeight * .5f + index * (RowHeight + RowGap);

        public static float ButtonCentre
            => HeroHeight + HeroGap + PanelHeight + PanelGap + ButtonHeight * .5f;

        /// <summary>How tall the whole column is. Derived, never typed.</summary>
        public static float Height => ButtonCentre + ButtonHeight * .5f;

        /// <summary>
        /// Where the column's top edge sits inside a band <paramref name="band"/> tall, measured
        /// down from the band's own top.
        ///
        /// Never negative: a band too short to hold the column starts it at the top and lets it
        /// run past the bottom, which is a composition somebody can see is wrong rather than one
        /// hanging off the top of the screen where nobody can.
        /// </summary>
        public static float TopIn(float band)
        {
            float slack = band - Height;
            return slack > 0f ? slack * Lift : 0f;
        }

        /// <summary>Whether a band this tall holds the column at all.</summary>
        public static bool Fits(float band) => band >= Height;

        /// <summary>
        /// Whether the column leaves clear air everywhere and fits inside the reference width.
        ///
        /// <paramref name="fault"/> names what went wrong, so a failure reads as an instruction
        /// rather than as a boolean — <see cref="PanelStack.IsClear"/>'s rule.
        ///
        /// <para>
        /// It checks the <em>stack</em>, which is a fact about the constants above and not about
        /// any caller. What it cannot check is the band, because that is a fact about the device:
        /// <c>EndlessHubTests</c> asks <see cref="Fits"/> about the shortest canvas this game is
        /// drawn on with the map's own header and shelf taken out of it, which is the only
        /// question with a real answer.
        /// </para>
        /// </summary>
        public static bool IsClear(out string fault)
        {
            fault = null;

            float[] centres = { HeroCentre, PanelCentre, ButtonCentre };
            float[] heights = { HeroHeight, PanelHeight, ButtonHeight };
            string[] names = { "the medal", "the plate", "the button" };

            for (int i = 1; i < centres.Length; i++)
            {
                float above = centres[i - 1] + heights[i - 1] * .5f;
                float below = centres[i] - heights[i] * .5f;

                if (below < above)
                {
                    fault = $"{names[i - 1]} ends {above} down and {names[i]} begins {below} " +
                            $"down, so the two overlap by {above - below}";
                    return false;
                }
            }

            // Everything the medal draws has to fit the box the stack gave it, or the column has
            // a piece standing outside the column. See the class note.
            if (BurstSize > HeroHeight)
            {
                fault = $"the starburst is {BurstSize} across in a {HeroHeight} block, so it is " +
                        $"drawn {BurstSize - HeroHeight} outside the column";
                return false;
            }

            if (DiscDown + DiscSize * .5f > HeroHeight || PlateDown + PlateHeight * .5f > HeroHeight)
            {
                fault = "the medal or its plate is drawn past the foot of the hero block";
                return false;
            }

            // The rows have to fit the plate they are read on, which is what makes the plate's
            // height derived rather than typed.
            float last = RowCentre(Points - 1) + RowHeight * .5f;

            if (last + PanelPad > PanelHeight)
            {
                fault = $"{Points} rows reach {last} down a plate {PanelHeight} tall";
                return false;
            }

            if (SlotSize >= RowHeight)
            {
                fault = $"a {SlotSize} seat does not fit a {RowHeight} row";
                return false;
            }

            if (IconSize >= SlotSize)
            {
                fault = $"a {IconSize} mark does not fit a {SlotSize} seat";
                return false;
            }

            float widest = PanelWidth > ButtonWidth ? PanelWidth : ButtonWidth;

            if (widest > Content.ChapterMap.Width)
            {
                fault = $"the widest piece is {widest} across, which does not fit the " +
                        $"{Content.ChapterMap.Width} reference width";
                return false;
            }

            return true;
        }
    }
}
