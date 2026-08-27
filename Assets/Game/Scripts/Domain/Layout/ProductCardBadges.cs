namespace GlimmerGrove.Layout
{
    /// <summary>
    /// Where a shop card's two marks sit: the bonus ribbon across its top-left corner and the
    /// badge seal on its top-right.
    ///
    /// <para>
    /// <b>Here rather than beside the card</b>, for <see cref="WheelPanel"/>'s reason and
    /// <c>ChapterMap</c>'s before it (invariant 8a): whether two things on a screen overlap is
    /// arithmetic, and arithmetic inside a <c>MonoBehaviour</c> is arithmetic nothing can
    /// check. What makes this the case the house rule was written for is that the two things
    /// which overlapped were <em>on different cards</em>. The seal hung 38 units past its own
    /// plate and the next column's ribbon reached 22 past its plate's other edge, so with a
    /// gutter of 34 between them the badge and its neighbour's "+40% EXTRA" shared 26 units of
    /// the screen — and which of the two was drawn on top was whatever order <c>GridView</c>
    /// happened to have recycled those cells in, so it was not even reliably wrong. Every check
    /// in this project reads one object at a time, and neither number is wrong on its own.
    /// </para>
    /// <para>
    /// <b>The seal is measured as the circle it is, not as the square its sprite is.</b>
    /// <c>seal_gold</c> is a disc inside a square texture with a quarter of its width empty at
    /// the corners, so treating it as a rotated square overstates its reach by about a sixth —
    /// which is a sixth of a badge of clear air bought at the price of pushing it into the
    /// picture underneath. <see cref="Face"/> is the flat maroon field inside the rim, and it
    /// is what the caption has to fit: text sized against the sprite spilled across the rim
    /// onto the plate, where the dark lettering it was given for a gold ground was drawn on a
    /// dark ground and simply disappeared.
    /// </para>
    /// <para>
    /// Every number is in the <b>reference card's</b> units — the shelf's 508x560 — because the
    /// card scales one layout rather than carrying two (see <c>ProductCard</c>). Only a
    /// decorated card wears either mark, and the only decorated card is the shop's, whose grid
    /// pitch <em>is</em> its cell width, which is what lets a card know where its neighbour's
    /// ribbon starts without being told.
    /// </para>
    /// </summary>
    public static class ProductCardBadges
    {
        // ------------------------------------------------------------------ the card
        /// <summary>The reference cell, and the plate drawn inside it.</summary>
        public const float CardWidth = 508f, CardHeight = 560f;

        /// <summary>How much of the cell is air around the plate, across and down.</summary>
        public const float PlateInsetX = 34f, PlateInsetY = 40f;

        public const float PlateWidth = CardWidth - PlateInsetX;
        public const float PlateHeight = CardHeight - PlateInsetY;

        /// <summary>
        /// The grid's column pitch, which is the cell width: cards are centred in their cells,
        /// so the plate opposite starts one pitch away less half a plate.
        /// </summary>
        public const float Pitch = CardWidth;

        /// <summary>Clear air demanded between two marks, and between a mark and a plate edge.</summary>
        public const float Clearance = 10f, Margin = 6f;

        // ------------------------------------------------------------------ the ribbon
        /// <summary>The bonus ribbon's cloth, and the angle it is pinned at.</summary>
        public const float RibbonWidth = 230f, RibbonHeight = 62f, RibbonTilt = -8f;

        /// <summary>Its centre, in from the plate's left edge and down from the plate's top.</summary>
        public const float RibbonInset = 96f, RibbonDrop = 26f;

        /// <summary>
        /// How far the pinned ribbon reaches either side of its own centre.
        ///
        /// A rotated rectangle, so both of its sides contribute: the long side foreshortens and
        /// the short side starts to count, which is why a ribbon 230 across occupies 236.
        /// </summary>
        public static float RibbonReach =>
            RibbonWidth * .5f * Cos(RibbonTilt) + RibbonHeight * .5f * Sin(RibbonTilt);

        /// <summary>
        /// Where the next column's ribbon begins, measured from <em>this</em> card's plate
        /// centre. The number the seal has to stay behind.
        /// </summary>
        public static float NeighbourRibbon =>
            Pitch - PlateWidth * .5f + RibbonInset - RibbonReach;

        // ------------------------------------------------------------------ the seal
        /// <summary>How wide the badge's sprite is drawn, and the angle it is stuck on at.</summary>
        public const float SealSize = 164f, SealTilt = 11f;

        /// <summary>
        /// How much of that sprite is the disc itself, corner to corner of the texture being
        /// empty. Measured off <c>seal_gold</c>: opaque from 12 to 156 of 169.
        /// </summary>
        public const float SealDisc = .86f;

        /// <summary>
        /// The flat field inside the rim, as a fraction of the sprite, and where its centre sits
        /// relative to the sprite's own. Measured off the same texture — the disc is drawn a
        /// little high and a little left, and a caption centred on the sprite instead of on the
        /// field is a caption sitting low.
        /// </summary>
        public const float Face = .538f, FaceShift = -.009f, FaceRise = .021f;

        /// <summary>
        /// How much of that field a two-line caption may use. Kept well inside it, because the
        /// field is round and a box is not: the corners of the caption are what touch the rim.
        /// </summary>
        public const float FaceTextWidth = .82f, FaceTextHeight = .48f;

        /// <summary>The caption's size, and the floor best-fit may shrink a long one to.</summary>
        public const int TextSize = 18, TextFloor = 10;

        /// <summary>How far the badge reaches from its centre, in any direction.</summary>
        public static float SealReach => SealSize * SealDisc * .5f;

        /// <summary>
        /// The badge's centre, in from the plate's right edge.
        ///
        /// <para>
        /// The smaller of two demands, and stating both is the point: it has to stay on its own
        /// plate, <em>and</em> it has to stay behind the next column's ribbon. Today the plate
        /// is the binding one and the ribbon is cleared with room to spare — but a wider ribbon
        /// or a narrower gutter would swap them over, and a rule that only names the constraint
        /// that happens to bind is a rule that stops being true without anybody editing it.
        /// </para>
        /// </summary>
        public static float SealInset
        {
            get
            {
                float onItsOwnPlate = PlateWidth * .5f - Margin - SealReach;
                float behindTheNeighbour = NeighbourRibbon - Clearance - SealReach;

                return PlateWidth * .5f - Min(onItsOwnPlate, behindTheNeighbour);
            }
        }

        /// <summary>
        /// The badge's centre, down from the plate's top edge.
        ///
        /// <para>
        /// It is allowed to overhang the top — a badge tucked entirely inside its plate reads
        /// as a printed label rather than as something stuck on, and there is nothing above it
        /// on its own card to be drawn through. What is above it is the <em>row</em> above,
        /// whose plate ends <see cref="PlateInsetY"/> away, so the overhang is exactly that gap
        /// less the clearance every other pair of marks here keeps.
        /// </para>
        /// </summary>
        public static float SealDrop => SealReach - (PlateInsetY - Clearance);

        /// <summary>The caption's box, and its centre against the sprite's.</summary>
        public static float TextWidth => SealSize * Face * FaceTextWidth;
        public static float TextHeight => SealSize * Face * FaceTextHeight;
        public static float TextShift => SealSize * FaceShift;
        public static float TextRise => SealSize * FaceRise;

        // ------------------------------------------------------------------ arithmetic
        static float Cos(float degrees) => (float)System.Math.Cos(System.Math.Abs(degrees) * Rad);
        static float Sin(float degrees) => (float)System.Math.Sin(System.Math.Abs(degrees) * Rad);
        static float Min(float a, float b) => a < b ? a : b;

        const double Rad = System.Math.PI / 180.0;
    }
}
