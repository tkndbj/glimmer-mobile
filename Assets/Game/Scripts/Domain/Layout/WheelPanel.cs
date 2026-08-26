namespace GlimmerGrove.Layout
{
    /// <summary>Where each part of the bonus wheel's panel sits, measured from its top edge.</summary>
    public readonly struct WheelStack
    {
        /// <summary>The wheel's centre, and how wide across it is drawn.</summary>
        public readonly float WheelCentre, WheelSize;

        /// <summary>The odds line's centre — one sentence, under the rim.</summary>
        public readonly float OddsCentre;

        /// <summary>The status paragraph's <em>top</em>, because it is set from its head down.</summary>
        public readonly float StatusTop;

        /// <summary>The one button's centre: spin, then the video, then collect.</summary>
        public readonly float ButtonCentre;

        /// <summary>How tall the panel has to be to hold all of it.</summary>
        public readonly float Height;

        public WheelStack(float wheelCentre, float wheelSize, float oddsCentre,
                          float statusTop, float buttonCentre, float height)
        {
            WheelCentre = wheelCentre;
            WheelSize = wheelSize;
            OddsCentre = oddsCentre;
            StatusTop = statusTop;
            ButtonCentre = buttonCentre;
            Height = height;
        }
    }

    /// <summary>
    /// The bonus wheel panel's geometry.
    ///
    /// <para>
    /// <b>In Domain because whether two things on a screen overlap is arithmetic.</b> That is
    /// the house rule <c>ChapterMap</c>, <c>WeaveBand</c>, <c>ReadoutRow</c> and
    /// <c>PanelStack</c> each earned the hard way, and this panel is the tallest thing added
    /// to the game in a while: a 560-unit wheel with four rows around it. Left in the overlay
    /// it would be five constants and a cursor that no test can reach, which is exactly the
    /// arrangement that drew <c>GladeRewardsOverlay</c>'s last paragraph 78 units into its own
    /// close button — invisible in English, on the one device it was tuned on.
    /// </para>
    /// <para>
    /// The height is <b>derived</b> rather than typed, so a row inserted above cannot leave a
    /// stale number behind it, and <c>WheelPanelTests</c> holds the result under
    /// <see cref="PanelStack.TallestPanel"/> — the shortest canvas this game is drawn on, with
    /// the title ribbon's overhang counted at <em>both</em> ends because a modal is centred.
    /// </para>
    /// <para>
    /// The wheel's own diameter is the one number here worth watching. It is what a player
    /// looks at, so the pressure is always to make it bigger, and it is also nine tenths of the
    /// budget: at the shipped 560 the panel has about 170 units of headroom, so a wheel much
    /// past 730 stops fitting a 4:3 tablet held in portrait. The test says so by failing rather
    /// than this comment saying so by being read.
    /// </para>
    /// </summary>
    public static class WheelPanel
    {
        /// <summary>How wide the panel is, and how wide the rows inside it may be.</summary>
        public const float Width = 900f, ContentWidth = 740f;

        /// <summary>Clear air under the title ribbon before the wheel starts.</summary>
        public const float HeadRoom = 150f;

        /// <summary>The wheel, and the air under it.</summary>
        public const float WheelSize = 560f, WheelFoot = 26f;

        /// <summary>The odds line, and the air under it.</summary>
        public const float OddsHeight = 56f, OddsFoot = 8f;

        /// <summary>The status paragraph, and the air under it.</summary>
        public const float StatusHeight = 92f, StatusFoot = 10f;

        /// <summary>The button, and the air below it to the frame's bottom edge.</summary>
        public const float ButtonHeight = 148f, FootRoom = 46f;

        /// <summary>
        /// The panel, laid out top to bottom.
        ///
        /// A cursor rather than absolute offsets, for <c>AdOfferOverlay</c>'s reason: a row
        /// inserted in the middle must not need every number below it edited by hand, because
        /// that is the edit somebody makes four fifths of.
        /// </summary>
        public static WheelStack Of()
        {
            float y = HeadRoom;

            float wheel = y + WheelSize * .5f;
            y += WheelSize + WheelFoot;

            float odds = y + OddsHeight * .5f;
            y += OddsHeight + OddsFoot;

            float status = y;
            y += StatusHeight + StatusFoot;

            float button = y + ButtonHeight * .5f;
            y += ButtonHeight + FootRoom;

            return new WheelStack(wheel, WheelSize, odds, status, button, y);
        }

        /// <summary>
        /// The tallest this panel ever gets. It takes one shape, and that is worth stating
        /// rather than assuming — every row is always drawn, so there is no state in which it
        /// is shorter and none in which it is taller.
        /// </summary>
        public static float Tallest => Of().Height;
    }
}
