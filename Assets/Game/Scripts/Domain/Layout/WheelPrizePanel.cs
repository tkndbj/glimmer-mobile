namespace GlimmerGrove.Layout
{
    /// <summary>Where each part of the wheel's prize panel sits, measured from its top edge.</summary>
    public readonly struct WheelPrizeStack
    {
        /// <summary>The coin's centre, and how wide across it is drawn.</summary>
        public readonly float CoinCentre, CoinSize;

        /// <summary>The figure's centre — one <c>Payout</c> chip, under the coin.</summary>
        public readonly float AmountCentre;

        /// <summary>The one button's centre.</summary>
        public readonly float ButtonCentre;

        /// <summary>How tall the panel has to be to hold all of it.</summary>
        public readonly float Height;

        public WheelPrizeStack(float coinCentre, float coinSize, float amountCentre,
                               float buttonCentre, float height)
        {
            CoinCentre = coinCentre;
            CoinSize = coinSize;
            AmountCentre = amountCentre;
            ButtonCentre = buttonCentre;
            Height = height;
        }
    }

    /// <summary>
    /// The prize panel's geometry: what the wheel's video actually paid, handed over.
    ///
    /// <para>
    /// <b>Here rather than beside the panel</b>, for <see cref="WheelPanel"/>'s reason and
    /// <c>ChapterMap</c>'s before it (invariant 8a): whether two things on a screen overlap is
    /// arithmetic, and arithmetic inside a <c>MonoBehaviour</c> is arithmetic nothing can
    /// check. This panel is three rows and every one of them is drawn every time, so the height
    /// is a single derivation and <c>WheelPrizePanelTests</c> holds it under
    /// <see cref="PanelStack.TallestPanel"/> — the shortest canvas this game is drawn on, with
    /// the title ribbon's overhang counted at both ends because a modal is centred.
    /// </para>
    /// <para>
    /// Every number is a <b>centre</b>, including the ones a reader might expect to be a top.
    /// That is not a style preference: <c>UIKit.Box</c> pivots every box centrally whatever it
    /// is anchored to, so a row described as a top and handed to the overlay as a position is
    /// drawn half its own height too high — which is exactly what
    /// <see cref="WheelPanel.StatusHeight"/>'s row did for as long as the wheel existed, while
    /// its own test passed on the arithmetic the panel did not use.
    /// </para>
    /// </summary>
    public static class WheelPrizePanel
    {
        /// <summary>How wide the panel is, and how wide the rows inside it may be.</summary>
        public const float Width = 880f, ContentWidth = 720f;

        /// <summary>Clear air under the title ribbon before the coin starts.</summary>
        public const float HeadRoom = 146f;

        /// <summary>The coin, and the air under it.</summary>
        public const float CoinSize = 240f, CoinFoot = 6f;

        /// <summary>
        /// The figure, and the air under it.
        ///
        /// <para>
        /// A <c>Payout</c> chip is 112 tall whatever it is carrying, so the height is that
        /// rather than a guess at it. The foot is wider than the air above the figure because
        /// what follows is a solid coloured button the width of the panel, and the same gap
        /// reads as half as much against one — <c>PanelStack.FootGap</c>'s reason.
        /// </para>
        /// </summary>
        public const float AmountHeight = 112f, AmountFoot = 40f;

        /// <summary>The button, and the air below it to the frame's bottom edge.</summary>
        public const float ButtonHeight = 132f, FootRoom = 54f;

        /// <summary>
        /// The panel, laid out top to bottom.
        ///
        /// A cursor rather than absolute offsets, so a row inserted in the middle does not need
        /// every number below it edited by hand.
        /// </summary>
        public static WheelPrizeStack Of()
        {
            float y = HeadRoom;

            float coin = y + CoinSize * .5f;
            y += CoinSize + CoinFoot;

            float amount = y + AmountHeight * .5f;
            y += AmountHeight + AmountFoot;

            float button = y + ButtonHeight * .5f;
            y += ButtonHeight + FootRoom;

            return new WheelPrizeStack(coin, CoinSize, amount, button, y);
        }

        /// <summary>
        /// The tallest this panel ever gets, which is also the only height it ever has: every
        /// row is drawn every time, so there is no state in which it is shorter.
        /// </summary>
        public static float Tallest => Of().Height;
    }
}
