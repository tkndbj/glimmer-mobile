namespace GlimmerGrove.Layout
{
    /// <summary>
    /// Where the pieces of a lane's hub sit: the keeper's rank badge, the plate of lines saying
    /// what the lane is, the medal carrying its record, and the key that starts it.
    ///
    /// <para>
    /// <b>The rank is the hero and the record stands under the lines, since 2026-09-28</b>, at
    /// the owner's instruction: the badge a keeper holds goes where the best-wave medal was, at
    /// the medal's size, and the medal moves down to sit above the key, drawn at
    /// <see cref="RecordScale"/>. The column grew by the medal, which the squarest phone could not
    /// hold at full size - so the column is <b>drawn to fit</b> (<see cref="ScaleIn"/>) with a
    /// floor under it (<see cref="MinScale"/>), and the gate asks the floor rather than a
    /// fit at full size.
    /// </para>
    ///
    /// <para>
    /// <b>Here rather than beside the screen, for <see cref="PanelStack"/>'s reason</b> (invariant
    /// 8a, earned a seventh time): whether two things on a screen overlap is arithmetic, and
    /// arithmetic inside a <c>MonoBehaviour</c> is arithmetic nothing can check. This one has the
    /// tightest budget of any stack in the game and the least room to be wrong in - it is drawn
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
        /// <see cref="PanelHeight"/> is derived from this, so the plate grows with it and
        /// <see cref="IsClear"/> is what refuses one row too many rather than a phone.
        /// </summary>
        public const int Points = 4;

        // ------------------------------------------------------------------ the medal
        /// <summary>
        /// The medal's own block before <see cref="RecordScale"/>: a starburst, a medallion
        /// carrying the furthest wave, and a plate under it naming what the number is.
        ///
        /// <para>
        /// <b>It was the hero until 2026-09-28</b> ("the record is what this lane is"); the owner
        /// moved the keeper's rank badge into its place and the medal under the lines. The rank
        /// shared this box for a day and has its own now (<see cref="HeroHeight"/>).
        /// </para>
        /// <para>
        /// <b>Derived from the plate that hangs off its foot, never typed.</b> It was typed, and
        /// was one unit short of what it held - caught by <see cref="IsClear"/> rather than by a
        /// phone, which is the whole reason this arithmetic is not in the screen.
        /// </para>
        /// </summary>
        public static float MedalHeight => PlateDown + PlateHeight * .5f;

        /// <summary>The medal's own centre, inside <see cref="MedalHeight"/>.</summary>
        public static float MedalCentre => MedalHeight * .5f;

        // ------------------------------------------------------------------ the rank
        /// <summary>
        /// The rank's block: the badge, and its name on a plate under it.
        ///
        /// <para>
        /// <b>Its own numbers since 2026-09-28, and no longer the medal's box.</b> The owner asked
        /// for the badge bigger and centred between the RANKED pill and its name; in the medal's
        /// box it could grow only by overlapping its own plate. The medal keeps its composition
        /// (<see cref="MedalHeight"/>) and the rank gets a taller one.
        /// </para>
        /// </summary>
        public static float HeroHeight => RankPlateDown + PlateHeight * .5f;

        /// <summary>The rank badge's square, and where the rank's name plate is read.</summary>
        public const float BadgeSize = 355f, RankPlateDown = 330f;

        /// <summary>
        /// How much of <see cref="BadgeSize"/> the tallest badge's ink fills (measured off the
        /// seven 512 PNGs: 444 of 512 at most). The ink, not the square, is what must stand clear.
        /// </summary>
        public const float BadgeInk = .87f;

        /// <summary>
        /// The badge's centre, measured down from the column's top: <b>midway between the pill
        /// above the column</b> (<see cref="HeadClear"/> over its top) <b>and the top of the
        /// name plate</b>, so the air above the badge and below it is the same. Derived.
        /// </summary>
        public static float BadgeDown => (RankPlateDown - PlateHeight * .5f - HeadClear) * .5f;

        // ------------------------------------------------------------------ the crest
        /// <summary>
        /// A lane whose hero is a picture rather than the rank (<c>EndlessHub.HubLane.Crest</c>:
        /// the Shuffle lane's, at the owner's instruction on 2026-10-09 - a rank is nothing a
        /// dealt hand says anything about). Drawn in the rank's own box, as wide as a 3:2
        /// picture is at that height, so nothing under it moves.
        /// </summary>
        public static float CrestWidth => HeroHeight * 1.5f;

        /// <summary>How far the crest breathes and how long one breath takes: slow and gentle.</summary>
        public const float CrestBreath = .025f, CrestPeriod = 4.6f;

        // ------------------------------------------------------------------ the record
        /// <summary>
        /// The best-wave medal under the lines: the same composition the hero used to be - burst,
        /// disc, nameplate - drawn whole at this scale, so nothing about it is a second design.
        /// </summary>
        public const float RecordScale = .54f;

        /// <summary>The block the record takes in the column, as drawn. Derived.</summary>
        public static float RecordHeight => MedalHeight * RecordScale;

        /// <summary>
        /// The starburst behind the medal - <b>the hero's own box</b>, never larger. See the class
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
        /// <b>Seventy rather than ninety, and the fourth line is what bought it.</b> The plate is
        /// derived from <see cref="Points"/>, so a fourth row grew the column by 96 units and
        /// <c>EndlessHubTests.TheColumnFitsTheShortestCanvasTheMapLeavesIt</c> refused it at once:
        /// the squarest phone this game supports leaves the column 20 units of air and no more.
        /// Four rows at seventy grow it by sixteen, which fits with room still spare - and the
        /// seat and its mark come down with the row, because a seat as tall as its row is what
        /// <see cref="IsClear"/> refuses next.
        public const float RowHeight = 70f, RowGap = 6f;

        /// <summary>The framed seat a row's mark sits in, and the mark inside it.</summary>
        public const float SlotSize = 68f, IconSize = 52f;

        /// <summary>How tall the plate has to be to hold <see cref="Points"/> rows. Derived.</summary>
        public static float PanelHeight
            => PanelPad * 2f + RowHeight * Points + RowGap * (Points - 1);

        // ------------------------------------------------------------------ the way in
        /// <summary>The way in. The hub's own key and the home screen's are the same size.</summary>
        public const float ButtonWidth = 620f, ButtonHeight = 178f;

        // ------------------------------------------------------------------ the checkpoint
        /// <summary>
        /// The checkpoint bar over the key (MODES.md 43f): where the next run opens, and the way
        /// to the sheet that changes it. <b>The key's width, so the two read as one control</b> -
        /// the bar says where the key will take you - and a slim pill rather than a second key,
        /// because the key is the one thing on this screen that asks to be pressed.
        /// <para>
        /// <b>It cost the medal a little</b>: <see cref="RecordScale"/> went .62 to .54 in the same
        /// change, because the column was already drawn below full size on a 16:9 phone and every
        /// unit the bar adds is a unit off every other piece.
        /// </para>
        /// </summary>
        public const float CheckpointWidth = ButtonWidth, CheckpointHeight = 88f;

        // ------------------------------------------------------------------ the air
        /// <summary>
        /// The gaps: under the rank, under the lines, under the medal, and between the checkpoint
        /// bar and the key it speaks for - closer than the rest, because the two are one thought.
        /// </summary>
        const float HeroGap = 16f, RecordGap = 20f, CheckpointGap = 22f, ButtonGap = 14f;

        /// <summary>
        /// The least the column is ever drawn at. <see cref="ScaleIn"/> shrinks it to the band it
        /// is given and never below this; <c>EndlessHubTests</c> asks it of the shortest canvas
        /// with both switcher pills drawn, which is the tightest band this game can hand it.
        /// <b>.78 until the checkpoint bar (2026-09-29)</b>: that case - a catalog with a second
        /// mode, on a 7:4 phone - is not one the shipped catalog draws, and the one it does draw
        /// holds the column at about x0.84 on the same phone.
        /// </summary>
        public const float MinScale = .72f;

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

        public static float RecordCentre
            => HeroHeight + HeroGap + PanelHeight + RecordGap + RecordHeight * .5f;

        /// <summary>Where row <paramref name="index"/> is read, measured down from the plate's top.</summary>
        public static float RowCentre(int index)
            => PanelPad + RowHeight * .5f + index * (RowHeight + RowGap);

        public static float CheckpointCentre
            => HeroHeight + HeroGap + PanelHeight + RecordGap + RecordHeight + CheckpointGap
             + CheckpointHeight * .5f;

        public static float ButtonCentre
            => CheckpointCentre + CheckpointHeight * .5f + ButtonGap + ButtonHeight * .5f;

        /// <summary>How tall the whole column is. Derived, never typed.</summary>
        public static float Height => ButtonCentre + ButtonHeight * .5f;

        /// <summary>
        /// The scale the column is drawn at in a band <paramref name="band"/> tall: whole where it
        /// fits, shrunk uniformly to the band where it does not, and never below
        /// <see cref="MinScale"/>. The screen scales the column about its top edge, so every
        /// centre above is multiplied by this and nothing else moves.
        /// </summary>
        public static float ScaleIn(float band)
        {
            if (band >= Height) return 1f;
            float s = band / Height;
            return s > MinScale ? s : MinScale;
        }

        /// <summary>
        /// Where the column's top edge sits inside a band <paramref name="band"/> tall, measured
        /// down from the band's own top, once it is drawn at <see cref="ScaleIn"/>.
        ///
        /// Never negative: a band too short to hold the column even at its floor starts it at the
        /// top and lets it run past the bottom, which is a composition somebody can see is wrong
        /// rather than one hanging off the top of the screen where nobody can.
        /// </summary>
        public static float TopIn(float band)
        {
            float slack = band - Height * ScaleIn(band);
            return slack > 0f ? slack * Lift : 0f;
        }

        /// <summary>Whether a band this tall holds the column at its floor scale or better.</summary>
        public static bool Fits(float band) => band >= Height * MinScale;

        /// <summary>
        /// Whether the column leaves clear air everywhere and fits inside the reference width.
        ///
        /// <paramref name="fault"/> names what went wrong, so a failure reads as an instruction
        /// rather than as a boolean - <see cref="PanelStack.IsClear"/>'s rule.
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

            float[] centres = { HeroCentre, PanelCentre, RecordCentre, CheckpointCentre, ButtonCentre };
            float[] heights = { HeroHeight, PanelHeight, RecordHeight, CheckpointHeight, ButtonHeight };
            string[] names = { "the rank", "the plate", "the record", "the checkpoint", "the button" };

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
            if (BurstSize > MedalHeight)
            {
                fault = $"the starburst is {BurstSize} across in a {MedalHeight} block, so it is " +
                        $"drawn {BurstSize - MedalHeight} outside the column";
                return false;
            }

            if (DiscDown + DiscSize * .5f > MedalHeight)
            {
                fault = "the medal is drawn past the foot of its block";
                return false;
            }

            // The badge's ink stands clear of the pill above the column and of its own plate.
            float ink = BadgeSize * BadgeInk * .5f;
            if (BadgeDown - ink < -HeadClear || BadgeDown + ink > RankPlateDown - PlateHeight * .5f)
            {
                fault = $"a {BadgeSize} badge centred {BadgeDown} down touches the pill above " +
                        "it or the plate under it";
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
