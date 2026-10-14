namespace GlimmerGrove.Layout
{
    /// <summary>
    /// Where everything on a Shuffle hand sits: the panel, three cards across it, and the four
    /// things on each card measured down from its own top edge.
    ///
    /// <para>
    /// <b>Domain, so the overlap is a test</b> (<c>ShuffleHandTests</c>, invariant 8a's rule
    /// about any two things on a screen): three cards of this width across a 1080 canvas is the
    /// tightest row the game draws, and a name that wraps a third line under a note is exactly
    /// the fault a compile and a validator cannot see. <c>Tools/render_shuffle.py</c> draws it
    /// with the real face.
    /// </para>
    /// <para>
    /// A class of figures rather than constants, so a second hand shape (a four-card hand, a
    /// tablet's wider row) is a second instance and not a second file.
    /// </para>
    /// </summary>
    public sealed class ShuffleHandLayout
    {
        public static readonly ShuffleHandLayout Default = new ShuffleHandLayout();

        /// <summary>
        /// The hand's window. It wears the Deals sheet's frame but no longer its width (1000): the
        /// owner asked for the panel, the cards and the text bigger (2026-10-09, "it's hard to read
        /// the upgrades"), so it is as wide as the canvas allows with a margin, and the cards, the
        /// picture and every type size grew with it.
        /// </summary>
        public float PanelWidth = 1050f;

        /// <summary>
        /// How far the frame's banner reaches down into the panel: <c>VictoryFrame.BannerY</c>
        /// plus half of <c>VictoryFrame.BannerSize.y</c> (30 + 157 / 2). A copy, because Domain
        /// cannot read Presentation; <c>ShuffleTests.TheHandsCrestFootIsTheFramesBanner</c> holds
        /// the two together.
        /// </summary>
        public float CrestFoot = 108.5f;

        /// <summary>Under the banner: the wave line's centre, down from the panel's top.</summary>
        public float NoteY = 150f, NoteHeightLine = 44f;

        public float CardWidth = 322f, CardGap = 14f;

        public float CardHeight = 620f;

        /// <summary>The cards' top edge, down from the panel's top.</summary>
        public float CardsTop = 196f;

        public float Inset = 18f;

        public float SeatSize = 190f, IconSize = 164f;

        /// <summary>The seat's centre, down from a card's top.</summary>
        public float SeatDown = 118f;

        public int NameSize = 40, NameFloor = 26;

        public float NameHeight = 92f, NameDown = 266f;

        public int NoteSize = 32, NoteFloor = 20;

        public float NoteHeight = 162f, NoteDown = 400f;

        /// <summary>
        /// The row of diamonds under the note (MODES.md 59n): green when the card goes with a
        /// secret the build has started, purple when it brings its evolution closer. Their
        /// centre down from the card's top, their size and the air between two.
        /// </summary>
        public float DiamondDown = 499f, DiamondSize = 30f, DiamondGap = 10f;

        public float TierHeight = 60f, TierDown = 546f;

        public int TierSize = 30, TierFloor = 18;

        /// <summary>Air under the cards to the panel's foot.</summary>
        public float Foot = 34f;

        /// <summary>
        /// The most cards one row carries. A wider hand (<c>grand_hand</c>, five cards) is laid
        /// out as rows of this many, the last row centred under the one above: three over two.
        /// </summary>
        public int PerRow = 3;

        /// <summary>Air between two rows of cards.</summary>
        public float RowGap = 24f;

        /// <summary>The panel's height for a hand of three or fewer: one row.</summary>
        public float PanelHeight => CardsTop + CardHeight + Foot;

        /// <summary>How many rows a hand of <paramref name="cards"/> is laid out in.</summary>
        public int Rows(int cards) => cards <= PerRow ? 1 : (cards + PerRow - 1) / PerRow;

        /// <summary>The panel's height for a hand of <paramref name="cards"/>, every row and the air between.</summary>
        public float PanelHeightFor(int cards)
        {
            int rows = Rows(cards);
            return CardsTop + rows * CardHeight + (rows - 1) * RowGap + Foot;
        }

        /// <summary>The cards' centre line, down from the panel's top (what <c>UIKit.Box</c> pivots on).</summary>
        public float CardsCentre => CardsTop + CardHeight * .5f;

        /// <summary>
        /// Where card <paramref name="index"/> of a hand of <paramref name="cards"/> stands: its
        /// centre across the panel (nought the middle) and its <b>top edge</b> down from the
        /// panel's top - what the overlay's cards pivot on. Each row is centred on its own, so the
        /// two under three sit in the gaps.
        /// </summary>
        public void Place(int index, int cards, out float x, out float top)
        {
            int row = cards <= PerRow ? 0 : index / PerRow;
            int first = row * PerRow;
            int inRow = cards <= PerRow ? cards : System.Math.Min(PerRow, cards - first);
            int at = cards <= PerRow ? index : index - first;

            float pitch = CardWidth + CardGap;
            x = -(inRow - 1) * pitch * .5f + at * pitch;
            top = CardsTop + row * (CardHeight + RowGap);
        }

        /// <summary>How wide three cards and their gaps run.</summary>
        public float RowWidth(int cards) => cards * CardWidth + (cards - 1) * CardGap;

        /// <summary>
        /// Whether every piece clears its neighbours and the row fits its panel, naming the
        /// fault when not.
        /// </summary>
        public bool IsClear(int cards, out string fault)
        {
            fault = null;

            int widest = cards < PerRow ? cards : PerRow;
            if (RowWidth(widest) + Inset * 2f > PanelWidth)
            {
                fault = $"{widest} cards run {RowWidth(widest)} across a {PanelWidth} panel";
                return false;
            }

            if (RowGap < 0f)
            {
                fault = $"two rows of cards overlap by {-RowGap}";
                return false;
            }

            if (PanelWidth > Content.ChapterMap.Width)
            {
                fault = $"the panel is {PanelWidth} across, wider than the {Content.ChapterMap.Width} canvas";
                return false;
            }

            if (NoteY - NoteHeightLine * .5f < CrestFoot)
            {
                fault = $"the wave line begins {NoteY - NoteHeightLine * .5f} down, under the frame's banner ({CrestFoot})";
                return false;
            }

            if (CardsTop < NoteY + NoteHeightLine * .5f)
            {
                fault = $"the cards begin {CardsTop} down and the wave line ends {NoteY + NoteHeightLine * .5f}";
                return false;
            }

            float seatFoot = SeatDown + SeatSize * .5f;
            float nameTop = NameDown - NameHeight * .5f;
            float nameFoot = NameDown + NameHeight * .5f;
            float noteTop = NoteDown - NoteHeight * .5f;
            float noteFoot = NoteDown + NoteHeight * .5f;
            float diamondTop = DiamondDown - DiamondSize * .5f;
            float diamondFoot = DiamondDown + DiamondSize * .5f;
            float tierTop = TierDown - TierHeight * .5f;
            float tierFoot = TierDown + TierHeight * .5f;

            if (SeatDown - SeatSize * .5f < Inset) { fault = "the seat touches the card's top"; return false; }
            if (seatFoot > nameTop) { fault = $"the seat ends {seatFoot} down and the name begins {nameTop}"; return false; }
            if (nameFoot > noteTop) { fault = $"the name ends {nameFoot} down and the note begins {noteTop}"; return false; }
            if (noteFoot > diamondTop) { fault = $"the note ends {noteFoot} down and the diamonds begin {diamondTop}"; return false; }
            if (diamondFoot > tierTop) { fault = $"the diamonds end {diamondFoot} down and the tier begins {tierTop}"; return false; }
            if (DiamondSize * 2f + DiamondGap > CardWidth - Inset * 2f) { fault = "two diamonds do not fit across a card"; return false; }
            if (tierFoot + Inset > CardHeight) { fault = $"the tier ends {tierFoot} down a card {CardHeight} tall"; return false; }
            if (IconSize >= SeatSize) { fault = "the picture does not fit its seat"; return false; }

            // The whole panel, centred, fits the shortest canvas this game lays out for.
            if (PanelHeightFor(cards) > PanelStack.TallestPanel)
            {
                fault = $"the panel is {PanelHeightFor(cards)} tall against a ceiling of {PanelStack.TallestPanel}";
                return false;
            }

            return true;
        }
    }
}
