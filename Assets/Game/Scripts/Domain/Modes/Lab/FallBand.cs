namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The band under a well: how far the board's floor sits above the bottom of the screen,
    /// where the blend legend goes in the space that leaves, and how much of the board's own
    /// host the tray takes.
    ///
    /// <para>
    /// <b>Whether two things on a screen overlap is arithmetic, so it lives here and gets a
    /// test.</b> <c>ChapterMap</c> made that argument for map nodes, <c>RippleBand</c> for the
    /// undo key and the standing line, <c>ReadoutRow</c> for a row of numbers and
    /// <c>PanelStack</c> for a modal's sections — and the reason it keeps being made is that
    /// every time it was a paragraph instead, the paragraph was wrong. <c>PanelStack</c>'s was
    /// wrong by 78 units for as long as that panel had existed.
    /// </para>
    /// <para>
    /// <b>Everything here is a centre.</b> <c>UIKit.Box</c> pivots at centre whatever it is
    /// anchored to, so a number that means a top has to be converted by its caller — and
    /// <c>WheelPanel</c> is the one that proves some caller forgets: it had the arithmetic
    /// <em>and</em> the test and still drew a row through its neighbour, because one number in
    /// the stack meant something different from the rest.
    /// </para>
    /// <para>
    /// <b>The band has two shapes, and the reason is that this is the only board in the game
    /// bound by its own height.</b> Every mode fits its grid with
    /// <c>min(width / columns, usableHeight / rows)</c>, and Budburst's groves and
    /// Groovekeeper's grooves are square or wider, so the width term always wins and their
    /// furniture is spent out of slack they were never going to use. A well is 4x6 to 6x10 —
    /// tall — so the height term wins, and every unit given to a header, a floor and a tray
    /// comes straight off the cell. On a phone that costs nothing anybody notices: 796 units of
    /// 2340 leaves a 6x10 well a cell of 151. On a 4:3 display the same 796 came out of 1440
    /// and left a cell of <b>62</b>, which is what "the grid is tiny compared to the other
    /// modes" is — the other modes were never measured against the axis that shrank.
    /// <c>CanvasFit</c> gives most of that back by itself; this gives back the rest, and
    /// only where it was taken. <see cref="Of"/> answers <c>false</c> to exactly what shipped,
    /// so a phone is untouched, by construction rather than by inspection.
    /// </para>
    /// </summary>
    public static class FallBand
    {
        // ------------------------------------------------------------------ the phone's band
        /// <summary>
        /// How far the board's host sits above the bottom of the safe area, on a phone.
        ///
        /// <c>FallScreen.HostInset</c> reads it through <see cref="Of"/> rather than repeating
        /// 250, so the legend and the board cannot come to disagree about where the floor is —
        /// which is exactly how a carefully measured band ends up under a tray.
        /// </summary>
        public const float BoardFloor = 250f;

        /// <summary>How tall the legend plate is drawn at full size.</summary>
        public const float LegendHeight = 92f;

        /// <summary>Its centre, measured up from the bottom of the safe area, on a phone.</summary>
        public const float LegendCentre = 152f;

        /// <summary>
        /// How much of the board's host the tray takes, on a phone.
        ///
        /// <c>FallView</c> reads it through <see cref="Of"/> rather than holding its own 196 —
        /// which is what it did, in a field assigned inline, with nothing anywhere able to say
        /// whether the number still cleared what it was drawn over.
        /// </summary>
        public const float TrayHeight = 196f;

        /// <summary>
        /// Clear air under the legend. A home indicator lives here on most phones, and the safe
        /// area does not always account for it on the shapes this game is drawn on.
        /// </summary>
        public const float BottomClearance = 24f;

        // ------------------------------------------------------------------ the short one
        /// <summary>
        /// How large the legend and the tray are drawn on a short display.
        ///
        /// <para>
        /// A scale rather than a second set of coordinates, and that is the whole reason this
        /// was affordable. Both are a plate with a table of fixed offsets inside it — three
        /// recipes at ±114, a queue at −196 and −78, a count at +150 — so a second set of
        /// numbers would be a second layout to keep in step with the first, and the first is
        /// already the one nobody looks at. Scaling the plate moves everything on it at once
        /// and cannot draw two pieces of it apart.
        /// </para>
        /// <para>
        /// It is safe to be this small because a short display is a physically large one. At
        /// this scale on a 4:3 tablet a legend dot is about 3.7mm across against 1.8mm on a
        /// phone — the interface is smaller as a fraction of the screen and larger in the hand,
        /// which is the same bargain <c>CanvasFit</c> strikes for everything else.
        /// </para>
        /// </summary>
        public const float ShortLegendScale = .68f, ShortTrayScale = .715f;

        /// <summary>Where the legend sits on a short display, and what the board's floor becomes.</summary>
        public const float ShortLegendCentre = 68f, ShortBoardFloor = 128f;

        // ------------------------------------------------------------------ one display's band
        /// <summary>The band as it is drawn on one shape of display.</summary>
        public readonly struct Band
        {
            /// <summary>How large the legend plate and everything on it is drawn.</summary>
            public readonly float LegendScale;

            /// <summary>The legend's centre, up from the bottom of the safe area.</summary>
            public readonly float LegendCentre;

            /// <summary>How far the board's host sits above the bottom of the safe area.</summary>
            public readonly float BoardFloor;

            /// <summary>How much of the host's height the tray takes.</summary>
            public readonly float TrayHeight;

            public Band(float legendScale, float legendCentre, float boardFloor, float trayHeight)
            {
                LegendScale = legendScale;
                LegendCentre = legendCentre;
                BoardFloor = boardFloor;
                TrayHeight = trayHeight;
            }

            /// <summary>What the legend actually occupies, which is its plate times its scale.</summary>
            public float LegendHeight => FallBand.LegendHeight * LegendScale;

            /// <summary>How wide it actually is, by the same reckoning.</summary>
            public float LegendWidth => FallBand.LegendWidth * LegendScale;

            public float LegendTop => LegendCentre + LegendHeight * .5f;
            public float LegendBottom => LegendCentre - LegendHeight * .5f;

            /// <summary>How large the tray plate and everything on it is drawn.</summary>
            public float TrayScale => TrayHeight / FallBand.TrayHeight;
        }

        /// <summary>
        /// The band for this display: the shipped one, or the one that gives a height-bound
        /// well its room back.
        ///
        /// <paramref name="shortCanvas"/> is <c>CanvasFit.IsShort</c> — squarer than a phone —
        /// asked once by the screen and handed down, rather than read twice and risked once.
        /// </summary>
        public static Band Of(bool shortCanvas)
            => shortCanvas
                ? new Band(ShortLegendScale, ShortLegendCentre, ShortBoardFloor,
                           TrayHeight * ShortTrayScale)
                : new Band(1f, LegendCentre, BoardFloor, TrayHeight);

        // ------------------------------------------------------------------ one recipe
        /// <summary>How wide one blend recipe is drawn.</summary>
        public const float ChipWidth = 330f;

        /// <summary>Air between two of them.</summary>
        public const float ChipGap = 8f;

        /// <summary>How many the legend shows. Three, and it is <c>FallMixing</c> that says so.</summary>
        public static int Chips => FallMixing.Recipes.Count;

        /// <summary>How wide the three of them are together, before any scaling.</summary>
        public static float LegendWidth => Chips * ChipWidth + (Chips - 1) * ChipGap;

        /// <summary>
        /// The narrowest canvas this game is drawn on, in reference units.
        ///
        /// The legend has to fit inside it with the safe area's own side inset taken off, or the
        /// outer recipes run off the screen. Still a phone's 1080: <c>CanvasFit</c> only ever
        /// widens a canvas, so the narrowest one is the one that was never widened.
        /// </summary>
        public const float Canvas = 1080f, SideInset = 24f;

        /// <summary>Where one recipe's centre sits, counting from the left.</summary>
        public static float ChipCentre(int index)
            => -LegendWidth * .5f + ChipWidth * .5f + index * (ChipWidth + ChipGap);
    }
}
