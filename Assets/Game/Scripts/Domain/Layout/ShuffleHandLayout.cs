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

        /// <summary>The Deals sheet's width (<c>ChallengeTierOverlay.PanelW</c>), because the hand wears its frame.</summary>
        public float PanelWidth = 1000f;

        /// <summary>
        /// How far the frame's banner reaches down into the panel: <c>VictoryFrame.BannerY</c>
        /// plus half of <c>VictoryFrame.BannerSize.y</c> (30 + 157 / 2). A copy, because Domain
        /// cannot read Presentation; <c>ShuffleTests.TheHandsCrestFootIsTheFramesBanner</c> holds
        /// the two together.
        /// </summary>
        public float CrestFoot = 108.5f;

        /// <summary>Under the banner: the wave line's centre, down from the panel's top.</summary>
        public float NoteY = 150f, NoteHeightLine = 44f;

        public float CardWidth = 300f, CardGap = 18f;

        public float CardHeight = 520f;

        /// <summary>The cards' top edge, down from the panel's top.</summary>
        public float CardsTop = 196f;

        public float Inset = 18f;

        public float SeatSize = 176f, IconSize = 150f;

        /// <summary>The seat's centre, down from a card's top.</summary>
        public float SeatDown = 116f;

        public int NameSize = 34, NameFloor = 22;

        public float NameHeight = 84f, NameDown = 252f;

        public int NoteSize = 24, NoteFloor = 16;

        public float NoteHeight = 120f, NoteDown = 362f;

        public float TierHeight = 56f, TierDown = 468f;

        public int TierSize = 26, TierFloor = 16;

        /// <summary>Air under the cards to the panel's foot.</summary>
        public float Foot = 34f;

        public float PanelHeight => CardsTop + CardHeight + Foot;

        /// <summary>The cards' centre line, down from the panel's top (what <c>UIKit.Box</c> pivots on).</summary>
        public float CardsCentre => CardsTop + CardHeight * .5f;

        /// <summary>How wide three cards and their gaps run.</summary>
        public float RowWidth(int cards) => cards * CardWidth + (cards - 1) * CardGap;

        /// <summary>
        /// Whether every piece clears its neighbours and the row fits its panel, naming the
        /// fault when not.
        /// </summary>
        public bool IsClear(int cards, out string fault)
        {
            fault = null;

            if (RowWidth(cards) + Inset * 2f > PanelWidth)
            {
                fault = $"{cards} cards run {RowWidth(cards)} across a {PanelWidth} panel";
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
            float tierTop = TierDown - TierHeight * .5f;
            float tierFoot = TierDown + TierHeight * .5f;

            if (SeatDown - SeatSize * .5f < Inset) { fault = "the seat touches the card's top"; return false; }
            if (seatFoot > nameTop) { fault = $"the seat ends {seatFoot} down and the name begins {nameTop}"; return false; }
            if (nameFoot > noteTop) { fault = $"the name ends {nameFoot} down and the note begins {noteTop}"; return false; }
            if (noteFoot > tierTop) { fault = $"the note ends {noteFoot} down and the tier begins {tierTop}"; return false; }
            if (tierFoot + Inset > CardHeight) { fault = $"the tier ends {tierFoot} down a card {CardHeight} tall"; return false; }
            if (IconSize >= SeatSize) { fault = "the picture does not fit its seat"; return false; }

            // The whole panel, centred, fits the shortest canvas this game lays out for.
            if (PanelHeight > PanelStack.TallestPanel)
            {
                fault = $"the panel is {PanelHeight} tall against a ceiling of {PanelStack.TallestPanel}";
                return false;
            }

            return true;
        }
    }
}
