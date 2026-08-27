using GlimmerGrove.Layout;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The wheel prize panel's arithmetic.
    ///
    /// <para>
    /// The same guard <c>WheelPanelTests</c> keeps over the wheel itself, and written for the
    /// same reason: this panel is only ever seen after a video has been watched, so a row drawn
    /// through the one above it would be found by players rather than by us.
    /// </para>
    /// </summary>
    public sealed class WheelPrizePanelTests
    {
        [Test]
        public void ThePanelFitsATabletHeldInPortrait()
            => Assert.LessOrEqual(WheelPrizePanel.Tallest, PanelStack.TallestPanel,
                                  $"the prize panel reaches {WheelPrizePanel.Tallest} and the " +
                                  $"shortest canvas holds {PanelStack.TallestPanel}");

        /// <summary>Every row clears the one above it, read as boxes rather than as centres.</summary>
        [Test]
        public void NoRowIsDrawnThroughTheOneAboveIt()
        {
            var s = WheelPrizePanel.Of();

            float coinHead = s.CoinCentre - s.CoinSize * .5f;
            float coinFoot = s.CoinCentre + s.CoinSize * .5f;
            float amountHead = s.AmountCentre - WheelPrizePanel.AmountHeight * .5f;
            float amountFoot = s.AmountCentre + WheelPrizePanel.AmountHeight * .5f;
            float buttonHead = s.ButtonCentre - WheelPrizePanel.ButtonHeight * .5f;

            Assert.GreaterOrEqual(coinHead, WheelPrizePanel.HeadRoom,
                                  "the coin is drawn up into the title ribbon");
            Assert.LessOrEqual(coinFoot, amountHead, "the figure is drawn over the coin");
            Assert.LessOrEqual(amountFoot, buttonHead, "the figure is drawn into the button");
            Assert.LessOrEqual(s.ButtonCentre + WheelPrizePanel.ButtonHeight * .5f, s.Height,
                               "the button is drawn off the bottom of the frame");
        }

        /// <summary>
        /// The height is derived from the parts, not typed beside them — driven by summing them
        /// independently, because a constant that happens to equal the sum today is
        /// indistinguishable from one that is derived until somebody inserts a row.
        /// </summary>
        [Test]
        public void TheHeightIsTheSumOfWhatIsInIt()
        {
            var s = WheelPrizePanel.Of();

            float expected = WheelPrizePanel.HeadRoom
                           + WheelPrizePanel.CoinSize + WheelPrizePanel.CoinFoot
                           + WheelPrizePanel.AmountHeight + WheelPrizePanel.AmountFoot
                           + WheelPrizePanel.ButtonHeight + WheelPrizePanel.FootRoom;

            Assert.AreEqual(expected, s.Height, .001f);
        }

        /// <summary>The rows sit inside the panel's own width, with the frame showing round them.</summary>
        [Test]
        public void TheRowsFitInsideTheFrame()
        {
            Assert.Less(WheelPrizePanel.ContentWidth, WheelPrizePanel.Width);
            Assert.LessOrEqual(WheelPrizePanel.CoinSize, WheelPrizePanel.ContentWidth);
        }

        /// <summary>
        /// It is shorter than the wheel it follows, which is what lets the celebration read as
        /// the wheel's answer rather than as a second panel of the same weight.
        /// </summary>
        [Test]
        public void ItIsShorterThanTheWheelItFollows()
            => Assert.Less(WheelPrizePanel.Tallest, WheelPanel.Tallest);
    }
}
