namespace GlimmerGrove.Modes
{
    /// <summary>
    /// Where the readouts sit under a grove, and whether anything in that band collides.
    ///
    /// <para>
    /// <b>Whether two things on a screen overlap is arithmetic, so it goes in Domain and gets a
    /// test.</b> <c>ChapterMap</c> made that argument for map nodes and <c>KeeperBand</c> for the
    /// basket; a row of typed constants with a paragraph explaining why they clear each other is
    /// a paragraph that will be wrong within one drop.
    /// </para>
    /// <para>
    /// This mode's band is the lightest of the three, and deliberately: there is nothing in hand
    /// to show. A grove deals no procession — the whole level is the grid — so the band carries
    /// the taps left and the critters, and everything else is on the board where the player is
    /// already looking.
    /// </para>
    /// </summary>
    public static class BudBand
    {
        /// <summary>How much room the band takes at the foot of the board.</summary>
        public const float BandHeight = 132f;

        // ------------------------------------------------------------------ the key row
        /// <summary>
        /// How tall the row under the grove is, where its key sits in it, and where the caption
        /// under that sits.
        ///
        /// <para>
        /// <b>One key, and it is the hint.</b> <c>PlayScreen</c>'s row is the same shape and the
        /// same numbers for the same reason — a glade's undo and restart used to stand either
        /// side of it and were taken away, because a restart is a forfeit and belongs behind a
        /// deliberate tap rather than under a thumb that is already reaching across the board.
        /// A grove has no undo to lose.
        /// </para>
        /// <para>
        /// The row stands inside the safe area rather than on the display's edge, unlike the
        /// glade's: this mode keeps its bottom inset because the band above it is already the
        /// tightest thing on the screen, and giving up the inset here would buy the grove
        /// nothing it does not already have.
        /// </para>
        /// </summary>
        public const float KeyBarHeight = 250f;

        public const float KeySize = 156f, KeyCentre = 158f, KeyCaption = 50f;

        /// <summary>The clear air between the key row and the foot of the band above it.</summary>
        public const float KeyClear = 18f;

        /// <summary>
        /// How much of the host the grove itself may use, measured from the bottom.
        ///
        /// Derived from the key row rather than typed, for <c>PanelStack</c>'s reason: the number
        /// that says where the board stops and the number that says where the row under it
        /// begins are one number, and two copies of it is how a key comes to be drawn through a
        /// grid.
        /// </summary>
        public static float BoardFloor => KeyBarHeight + KeyClear;

        public const float PlateHeight = 108f, PlateWidth = 660f;
        public const float BottomClearance = 24f;

        /// <summary>The plate fits inside its band, the grove clears the plate, and the key row
        /// clears both.</summary>
        public static bool Clears => PlateHeight <= BandHeight
                                  && BoardFloor >= BandHeight + BottomClearance
                                  && KeyCentre + KeySize * .5f < KeyBarHeight
                                  && KeyCaption > 0f
                                  && KeyCaption + 18f < KeyCentre - KeySize * .5f;

        /// <summary>The colour in hand, and the two behind it.</summary>
        public const int Lookahead = 2;

        public const float HandSize = 70f, QueueSize = 34f;
        public const float HandX = -230f, QueueX = -158f, QueueGap = 44f;

        public static float QueueCentre(int n) => QueueX + n * QueueGap;

        public const float TapsX = -20f, CrittersX = 190f;
        public const float PipSize = 96f, LabelDrop = -34f;

        /// <summary>The queue reads left to right and clears the colour in hand.</summary>
        public static bool QueueFits
        {
            get
            {
                float handRight = HandX + HandSize * .5f;
                float first = QueueCentre(0) - QueueSize * .5f;
                float last = QueueCentre(Lookahead - 1) + QueueSize * .5f;

                return first > handRight && last < TapsX - 70f
                    && HandX - HandSize * .5f > -PlateWidth * .5f;
            }
        }

        /// <summary>The two readouts clear each other and both stay on the plate.</summary>
        public static bool ReadoutsFit
        {
            get
            {
                // Half the caption box, which is the widest either readout gets.
                const float half = 95f;
                return CrittersX - TapsX > half * 2f
                    && TapsX - half > -PlateWidth * .5f
                    && CrittersX + half < PlateWidth * .5f;
            }
        }

        // ------------------------------------------------------------------ the legend
        /// <summary>
        /// Where the colour legend sits, measured <em>down</em> from the top of the safe area.
        ///
        /// <para>
        /// This is what <c>ModeScreen</c>'s default board inset used to be, so the legend takes
        /// the strip the grove used to start at and the grove starts below it. Anchored to the
        /// top rather than floated above the board on purpose: the readouts are already there,
        /// so the legend joins a row that exists instead of introducing a second thing that has
        /// to be kept clear of a grove whose height varies with the device.
        /// </para>
        /// </summary>
        public const float LegendTop = 350f;

        /// <summary>How tall the legend plate is, and the clear air under it.</summary>
        public const float LegendHeight = 96f, LegendGap = 16f;

        /// <summary>
        /// Where the grove's own host begins, which is the legend's foot plus that air.
        ///
        /// Derived rather than typed, for <c>PanelStack</c>'s reason: the number that decides
        /// where a board starts and the number that decides where the thing above it ends are
        /// one number, and two copies of it is how a legend comes to be drawn through a grid.
        /// </summary>
        public static float BoardCeiling => LegendTop + LegendHeight + LegendGap;

        /// <summary>The legend's centre, which is what <c>UIKit.Box</c> takes.</summary>
        public static float LegendCentre => LegendTop + LegendHeight * .5f;

        /// <summary>
        /// How wide one recipe's slot is, and the air between two of them.
        ///
        /// <b>The three recipes are three separate cards, not one long plate.</b> One plate
        /// behind all nine flowers was reported as visually confusing, and correctly: nine
        /// coloured shapes and four operators in a single box read as one row of thirteen
        /// things rather than as three statements, so the eye has to find the groupings for
        /// itself every time it looks. Three plates do that work in the layout. The gap went up
        /// with them — at eight units apart three cards read as one card with lines drawn on
        /// it, which is the worst of both.
        /// </summary>
        public const float ChipWidth = 322f, ChipGap = 18f;

        /// <summary>
        /// How wide the card behind one recipe is drawn, inside its slot.
        ///
        /// Narrower than the slot, so the air between two cards is the gap plus the margin on
        /// each — which is what makes three of them read as three at a glance rather than as a
        /// strip that happens to have seams.
        /// </summary>
        public const float ChipPlateWidth = 302f;

        /// <summary>And how tall, inside the band. The card is the legend's full height.</summary>
        public const float ChipPlateHeight = 88f;

        /// <summary>How many the legend shows. Three, and it is <c>BudMixing</c> that says so.</summary>
        public static int Chips => BudMixing.Recipes.Count;

        public static float LegendWidth => Chips * ChipWidth + (Chips - 1) * ChipGap;

        /// <summary>Where one recipe's centre sits, counting from the left.</summary>
        public static float ChipCentre(int index)
            => -LegendWidth * .5f + ChipWidth * .5f + index * (ChipWidth + ChipGap);

        /// <summary>
        /// The narrowest canvas this game is drawn on, in reference units, and the side inset
        /// the board host already takes off it.
        /// </summary>
        public const float Canvas = 1080f, SideInset = 24f;

        /// <summary>The three recipes fit across the narrowest screen this game is drawn on.</summary>
        public static bool LegendFits => LegendWidth <= Canvas - SideInset * 2f;

        /// <summary>
        /// A card is wider than what it holds and narrower than its slot, and the cards clear
        /// each other.
        ///
        /// <paramref name="fault"/> names the first thing that is not true, so a failure reads
        /// as an instruction rather than as a boolean.
        /// </summary>
        public static bool CardsAreClear(out string fault)
        {
            fault = null;

            float content = (MadeX + MadeSize * .5f) - (LeafX - LeafSize * .5f);
            if (ChipPlateWidth < content + 8f)
            {
                fault = $"a card is {ChipPlateWidth:0} wide against {content:0} of recipe in it";
                return false;
            }

            if (ChipPlateWidth > ChipWidth)
            {
                fault = $"a card is {ChipPlateWidth:0} wide inside a {ChipWidth:0} slot";
                return false;
            }

            float air = ChipGap + (ChipWidth - ChipPlateWidth);
            if (air < 24f)
            {
                fault = $"two cards leave {air:0} between them, which reads as one plate with " +
                        "a seam rather than as two cards";
                return false;
            }

            if (ChipPlateHeight > LegendHeight)
            {
                fault = $"a card is {ChipPlateHeight:0} tall in a {LegendHeight:0} band";
                return false;
            }

            return true;
        }

        // ------------------------------------------------------------------ one recipe
        /// <summary>
        /// Where each piece of one recipe sits, in units either side of the chip's own centre.
        ///
        /// From a table rather than accumulated down a method, for <c>ReadoutRow</c>'s reason —
        /// the pieces are small and close, so whether they collide is arithmetic and belongs
        /// where it can be read at a glance and proved without an Editor.
        /// </summary>
        public const float LeafX = -117f, PlusX = -63f, HandX2 = -9f, EqualsX = 45f, MadeX = 108f;

        /// <summary>How big a flower on the legend is drawn: the two sides, and what they make.</summary>
        public const float LeafSize = 56f, MadeSize = 74f;

        /// <summary>Half the box an operator glyph draws in.</summary>
        public const float GlyphHalf = 14f;

        /// <summary>
        /// Every piece of one recipe clears its neighbour, and the whole of it stays on its chip.
        ///
        /// <paramref name="fault"/> names the first pair that does not, so a failure reads as an
        /// instruction rather than as a boolean.
        /// </summary>
        public static bool ChipIsClear(out string fault)
        {
            fault = null;

            // Left edge of the first flower to right edge of the last, both measured as the
            // shapes they draw rather than as the boxes they are given.
            float left = LeafX - LeafSize * .5f;
            float right = MadeX + MadeSize * .5f;

            if (right - left > ChipWidth)
            {
                fault = $"one recipe draws {right - left:0} wide inside a {ChipWidth:0} chip";
                return false;
            }

            if (LeafX + LeafSize * .5f >= PlusX - GlyphHalf)
            {
                fault = "the flower on the board runs into the plus sign";
                return false;
            }

            if (PlusX + GlyphHalf >= HandX2 - LeafSize * .5f)
            {
                fault = "the plus sign runs into the colour in hand";
                return false;
            }

            if (HandX2 + LeafSize * .5f >= EqualsX - GlyphHalf)
            {
                fault = "the colour in hand runs into the equals sign";
                return false;
            }

            if (EqualsX + GlyphHalf >= MadeX - MadeSize * .5f)
            {
                fault = "the equals sign runs into what the two of them make";
                return false;
            }

            return true;
        }
    }
}
