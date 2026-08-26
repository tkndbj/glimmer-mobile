using GlimmerGrove.Layout;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The bonus wheel panel's arithmetic.
    ///
    /// <para>
    /// Every one of these fails on a machine rather than on a 4:3 tablet, which is the whole
    /// reason the geometry left the overlay. A panel that overruns its canvas, or draws a
    /// paragraph through a button, is invisible in a compile, in a validator and in a
    /// screenshot taken on the one aspect ratio it was tuned on.
    /// </para>
    /// </summary>
    public sealed class WheelPanelTests
    {
        [Test]
        public void ThePanelFitsATabletHeldInPortrait()
            => Assert.LessOrEqual(WheelPanel.Tallest, PanelStack.TallestPanel,
                                  $"the wheel panel reaches {WheelPanel.Tallest} and the shortest " +
                                  $"canvas holds {PanelStack.TallestPanel}. The wheel's own " +
                                  "diameter is nine tenths of the budget — shrink that before " +
                                  "anything else");

        /// <summary>
        /// Every row clears the one above it, read as boxes rather than as centres.
        ///
        /// The status paragraph is the odd one out and the reason this is worth a test: it is
        /// placed from its <em>top</em> while its neighbours are placed from their centres, so
        /// a reader comparing the two numbers directly gets the wrong answer by 46 units.
        /// </summary>
        [Test]
        public void NoRowIsDrawnThroughTheOneAboveIt()
        {
            var s = WheelPanel.Of();

            float wheelFoot = s.WheelCentre + s.WheelSize * .5f;
            float oddsHead = s.OddsCentre - WheelPanel.OddsHeight * .5f;
            float oddsFoot = s.OddsCentre + WheelPanel.OddsHeight * .5f;
            float statusFoot = s.StatusTop + WheelPanel.StatusHeight;
            float buttonHead = s.ButtonCentre - WheelPanel.ButtonHeight * .5f;

            Assert.GreaterOrEqual(s.WheelCentre - s.WheelSize * .5f, WheelPanel.HeadRoom,
                                  "the wheel is drawn up into the title ribbon");
            Assert.LessOrEqual(wheelFoot, oddsHead, "the odds line is drawn over the rim");
            Assert.LessOrEqual(oddsFoot, s.StatusTop, "the status is drawn over the odds line");
            Assert.LessOrEqual(statusFoot, buttonHead, "the status is drawn into the button");
            Assert.LessOrEqual(s.ButtonCentre + WheelPanel.ButtonHeight * .5f, s.Height,
                               "the button is drawn off the bottom of the frame");
        }

        /// <summary>
        /// The height is derived from the parts, not typed beside them.
        ///
        /// Driven by moving a part and watching the total follow, because a constant that
        /// happens to equal the sum today is indistinguishable from one that is derived — right
        /// up until somebody inserts a row.
        /// </summary>
        [Test]
        public void TheHeightIsTheSumOfWhatIsInIt()
        {
            var s = WheelPanel.Of();

            float expected = WheelPanel.HeadRoom
                           + WheelPanel.WheelSize + WheelPanel.WheelFoot
                           + WheelPanel.OddsHeight + WheelPanel.OddsFoot
                           + WheelPanel.StatusHeight + WheelPanel.StatusFoot
                           + WheelPanel.ButtonHeight + WheelPanel.FootRoom;

            Assert.AreEqual(expected, s.Height, .001f);
        }

        /// <summary>
        /// There is room to grow the wheel, and how much is worth knowing rather than
        /// rediscovering: the diameter is the number everybody wants to raise.
        /// </summary>
        [Test]
        public void ThereIsHeadroomAndItIsWorthAtLeastAModestWheel()
        {
            float spare = PanelStack.TallestPanel - WheelPanel.Tallest;

            Assert.Greater(spare, 0f);
            Assert.Less(WheelPanel.WheelSize + spare, 800f,
                        "if the wheel could grow past 800 units the budget has moved and this " +
                        "test is no longer telling anybody anything");
        }

        /// <summary>The rows sit inside the panel's own width, with the frame showing round them.</summary>
        [Test]
        public void TheRowsFitInsideTheFrame()
        {
            Assert.Less(WheelPanel.ContentWidth, WheelPanel.Width);
            Assert.LessOrEqual(WheelPanel.WheelSize, WheelPanel.ContentWidth);
        }
    }
}
