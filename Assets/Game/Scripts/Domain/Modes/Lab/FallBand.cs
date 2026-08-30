namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The band under a well: how far the board's floor sits above the bottom of the screen, and
    /// where the blend legend goes in the space that leaves.
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
    /// </summary>
    public static class FallBand
    {
        /// <summary>
        /// How far the board's host sits above the bottom of the safe area.
        ///
        /// <c>FallScreen.HostInset</c> reads it rather than repeating 250, so the legend and the
        /// board cannot come to disagree about where the floor is — which is exactly how a
        /// carefully measured band ends up under a tray.
        /// </summary>
        public const float BoardFloor = 250f;

        /// <summary>How tall the legend plate is.</summary>
        public const float LegendHeight = 92f;

        /// <summary>Its centre, measured up from the bottom of the safe area.</summary>
        public const float LegendCentre = 152f;

        /// <summary>
        /// Clear air under the legend. A home indicator lives here on most phones, and the safe
        /// area does not always account for it on the shapes this game is drawn on.
        /// </summary>
        public const float BottomClearance = 24f;

        public static float LegendTop => LegendCentre + LegendHeight * .5f;
        public static float LegendBottom => LegendCentre - LegendHeight * .5f;

        // ------------------------------------------------------------------ one recipe
        /// <summary>How wide one blend recipe is drawn.</summary>
        public const float ChipWidth = 330f;

        /// <summary>Air between two of them.</summary>
        public const float ChipGap = 8f;

        /// <summary>How many the legend shows. Three, and it is <c>FallMixing</c> that says so.</summary>
        public static int Chips => FallMixing.Recipes.Count;

        /// <summary>How wide the three of them are together.</summary>
        public static float LegendWidth => Chips * ChipWidth + (Chips - 1) * ChipGap;

        /// <summary>
        /// The narrowest canvas this game is drawn on, in reference units.
        ///
        /// The legend has to fit inside it with the safe area's own side inset taken off, or the
        /// outer recipes run off a 4:3 portrait screen — which is the shape that is never the one
        /// anybody happens to have open.
        /// </summary>
        public const float Canvas = 1080f, SideInset = 24f;

        /// <summary>Where one recipe's centre sits, counting from the left.</summary>
        public static float ChipCentre(int index)
            => -LegendWidth * .5f + ChipWidth * .5f + index * (ChipWidth + ChipGap);
    }
}
