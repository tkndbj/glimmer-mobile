namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The band under a grove: how far the board's floor sits above the bottom of the screen, and
    /// where the basket and its keys go in the room that leaves.
    ///
    /// <para>
    /// <b>Whether two things on a screen overlap is arithmetic, so it lives here and gets a
    /// test.</b> <c>ChapterMap</c> made that argument for map nodes, <c>WeaveBand</c> for the undo
    /// key, <c>FallBand</c> for the blend legend and <c>PanelStack</c> for a modal's sections —
    /// and the reason it keeps being made is that every time it was a paragraph instead, the
    /// paragraph was wrong.
    /// </para>
    /// <para>
    /// <b>Everything here is a centre.</b> <c>UIKit.Box</c> pivots at centre whatever it is
    /// anchored to, so a number that meant a top would have to be converted by its caller — and
    /// <c>WheelPanel</c> is the one that proves some caller forgets.
    /// </para>
    /// </summary>
    public static class KeeperBand
    {
        /// <summary>
        /// How far the board's host sits above the bottom of the safe area.
        ///
        /// <c>KeeperScreen.HostInset</c> reads it rather than repeating the number, so the basket
        /// and the grove cannot come to disagree about where the floor is.
        /// </summary>
        public const float BoardFloor = 236f;

        /// <summary>
        /// The band at the <em>bottom of the host</em> the basket takes, and the plate drawn
        /// inside it.
        ///
        /// <para>
        /// <b>Measured from the host rather than from the safe area, because that is where the
        /// basket is actually drawn.</b> The first version of this described a basket anchored to
        /// the safe floor and gave it a centre to sit at — numbers the view never read, so the
        /// test over them passed while proving nothing about the screen. That is <c>WheelPanel</c>'s
        /// fault exactly: it had the arithmetic and the test and still drew a row through its
        /// neighbour, because one number in the stack meant something different from the rest.
        /// Every number here is one the view consumes.
        /// </para>
        /// </summary>
        public const float BasketHeight = 152f, PlateHeight = 124f;

        /// <summary>
        /// Clear air under the whole board. A home indicator lives here on most phones, and the
        /// safe area does not always account for it on the shapes this game is drawn on.
        /// </summary>
        public const float BottomClearance = 24f;

        /// <summary>
        /// The plate fits the band it is drawn in, and the band clears the bottom of the screen.
        ///
        /// The claim <c>KeeperBandTests</c> pins, and it is about the two relationships that can
        /// actually break: a plate taller than its band overhangs the grove above it, and a board
        /// floor under the clearance puts the basket on the home indicator.
        /// </summary>
        public static bool Clears => PlateHeight <= BasketHeight
                                  && BoardFloor >= BasketHeight + BottomClearance;

        // ------------------------------------------------------------------ inside the basket
        /// <summary>How wide the basket plate is drawn.</summary>
        public const float PlateWidth = 660f;

        /// <summary>How much of the procession is shown. Four is enough to plan and few enough to hold.</summary>
        public const int Lookahead = 4;

        /// <summary>The tile in hand, and the ones queued behind it.</summary>
        public const float HandSize = 92f, QueueSize = 46f;

        /// <summary>Where the tile in hand sits, and where the queue starts, from the plate's centre.</summary>
        public const float HandX = -232f, QueueX = -118f, QueueGap = 62f;

        /// <summary>Where the count of tiles left sits, and where the compost key sits beside it.</summary>
        public const float CountX = 150f, CompostX = 268f;

        /// <summary>How big the compost key is drawn.</summary>
        public const float CompostSize = 96f;

        /// <summary>Where the <paramref name="n"/>th queued tile sits, counting from nought.</summary>
        public static float QueueCentre(int n) => QueueX + n * QueueGap;

        /// <summary>
        /// Whether the queue clears the tile in hand on its left and the count on its right.
        ///
        /// The claim <c>KeeperBandTests</c> pins, and the reason the numbers above are here rather
        /// than typed into the view: a fifth queued tile would silently draw over the count, and a
        /// screenshot on one aspect ratio is not a proof.
        /// </summary>
        public static bool QueueFits
        {
            get
            {
                float handRight = HandX + HandSize * .5f;
                float first = QueueCentre(0) - QueueSize * .5f;
                float last = QueueCentre(Lookahead - 2) + QueueSize * .5f;
                float countLeft = CountX - 95f;

                return first > handRight && last < countLeft
                    && CompostX + CompostSize * .5f < PlateWidth * .5f;
            }
        }
    }
}
