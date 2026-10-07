namespace GlimmerGrove
{
    /// <summary>
    /// The geometry of the hub's foot: the stack of controls standing on the nav bar, in each
    /// of the two shapes it can take.
    ///
    /// <para>
    /// <b>One stack, two states, and the arithmetic is the same in both</b> (<c>HomeScreen</c>'s
    /// own note: every row's centre is the sum of what is under it, so moving one number moves
    /// the rest). The welcome door (invariant 58) stands under the Daily Challenges door while
    /// the bonus is live and unclaimed, and the three controls above it move up to make room -
    /// and, because the room is not there to be had, shrink a little. The squarest canvas this
    /// game is drawn on is 1080 x <see cref="ShortestCanvas"/> (<c>Layout.CanvasFit</c> widens
    /// anything squarer than 7:4), the feature row hangs <see cref="FeatureBottom"/> down from
    /// the top of the safe area, and the two stacks grow toward each other: the welcome state
    /// has to clear that canvas as the plain one does, and <c>HubFootTests</c> holds it to.
    /// </para>
    /// <para>
    /// <b>The sizes are read off this, never typed at a call site</b>, so the hub, the render
    /// mirror (<c>render_home.py</c>) and the test read one table. Every field is named after
    /// the constant it replaced on <c>HomeScreen</c>.
    /// </para>
    /// </summary>
    public readonly struct HubFoot
    {
        /// <summary>The shortest canvas the hub is ever drawn on. <c>render_home.py --welcome</c> prints the same figure.</summary>
        public const float ShortestCanvas = 1890f;

        /// <summary>
        /// Where the hub's top stack ends: the feature row's foot, measured down from the top of
        /// the safe area (<c>HomeScreen.RowTop + RowHeight</c>).
        /// </summary>
        public const float FeatureBottom = 570f + 300f;

        /// <summary>The width every row of the foot shares with the rows above it.</summary>
        public const float Width = 960f;

        /// <summary>The loadout strip's own stack: air, the caption's band, air, the cells, air.</summary>
        public const float LinePad = 8f, LineHeadH = 34f, LineHeadGap = 8f, LineFoot = 10f;

        /// <summary>Whether the welcome door stands in this stack.</summary>
        public readonly bool Welcome;

        /// <summary>The air between rows.</summary>
        public readonly float Gap;

        /// <summary>The welcome door's height; nought when it is absent.</summary>
        public readonly float WelcomeH;

        /// <summary>The Daily Challenges slot: the key at its foot and the picture rising out of it.</summary>
        public readonly float ChallengeH;

        /// <summary>The BATTLE key.</summary>
        public readonly float PlayW, PlayH;

        /// <summary>How big a turret cell on the loadout strip is, and how far apart the four stand.</summary>
        public readonly float LineCell, LineCellGap, LineStar;

        HubFoot(bool welcome, float gap, float welcomeH, float challengeH, float playW, float playH,
                float lineCell, float lineCellGap, float lineStar)
        {
            Welcome = welcome;
            Gap = gap;
            WelcomeH = welcomeH;
            ChallengeH = challengeH;
            PlayW = playW;
            PlayH = playH;
            LineCell = lineCell;
            LineCellGap = lineCellGap;
            LineStar = lineStar;
        }

        /// <summary>
        /// The stack for whether the door shows.
        ///
        /// <b>The plain figures are the ones the hub has always drawn</b>; the welcome figures
        /// are the plain ones with 174 units found: 40 off the challenge slot (the picture is
        /// drawn 56 shorter and rises less), 18 off the key, 18 off a cell, and two off each of
        /// the five gaps - judged on the mirror, and held to the canvas by the test.
        /// </summary>
        public static HubFoot For(bool welcome)
            => welcome
               ? new HubFoot(true, 12f, 136f, 224f, 620f, 160f, 150f, 20f, 20f)
               : new HubFoot(false, 14f, 0f, 280f, 620f, 178f, 168f, 20f, 20f);

        /// <summary>The loadout strip's height, summed rather than typed (<c>HomeScreen.LineH</c>).</summary>
        public float LineH => LinePad + LineHeadH + LineHeadGap + LineCell + LineFoot;

        /// <summary>Where a cell's centre sits against the strip's own centre.</summary>
        public float LineCellY => LineH * .5f - LinePad - LineHeadH - LineHeadGap - LineCell * .5f;

        /// <summary>The welcome door's centre above the nav bar. Meaningless when absent.</summary>
        public float WelcomeY => NavBar.Height + Gap + WelcomeH * .5f;

        /// <summary>The challenge slot's centre: on the nav bar, or on the welcome door.</summary>
        public float ChallengeY
            => (Welcome ? WelcomeY + WelcomeH * .5f + Gap : NavBar.Height + Gap) + ChallengeH * .5f;

        public float LineY => ChallengeY + ChallengeH * .5f + Gap + LineH * .5f;

        public float PlayY => LineY + LineH * .5f + Gap + PlayH * .5f;

        /// <summary>The top of the stack, measured up from the bottom of the canvas.</summary>
        public float Top => PlayY + PlayH * .5f;

        /// <summary>
        /// What the squarest canvas has left between the feature row and the BATTLE key. Negative
        /// is the hub overlapping itself.
        /// </summary>
        public float Spare => ShortestCanvas - FeatureBottom - Top;
    }
}
