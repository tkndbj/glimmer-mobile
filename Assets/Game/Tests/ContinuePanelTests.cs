using GlimmerGrove.Layout;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The offer that follows a lost run, and the word standing above it.
    ///
    /// <para>
    /// <b>It earned this file on the first run of it.</b> The banner was written at 120 units
    /// tall beside a panel that had just grown a fourth button, and <see cref="ContinuePanel"/>'s
    /// own check refused it: ten units off the top of a 4:3 canvas. Every number involved was
    /// individually reasonable, which is exactly the failure <c>PanelStack</c> and
    /// <c>WheelPanel</c> both record — the second of those had the arithmetic <em>and</em> a test
    /// and still drew a row through its neighbour, because one number in its stack meant
    /// something other than what the rest meant.
    /// </para>
    /// </summary>
    public sealed class ContinuePanelTests
    {
        /// <summary>
        /// The panel is centred, so its top edge is half its own height above the middle, the
        /// title ribbon stands proud of that, and the word stands above the ribbon. All three
        /// are above the middle and all three have to be paid for out of half a canvas.
        /// </summary>
        [Test]
        public void EveryShapeOfThePanelIsDrawnOnScreenWithItsBanner()
        {
            Assert.IsTrue(ContinuePanel.IsClear(out string fault), fault);
        }

        [Test]
        public void TheCheckIsAgainstHalfTheCanvasAndNotAllOfIt()
        {
            float needed = ContinuePanel.Tallest * .5f + ContinuePanel.BannerOverhang;

            Assert.LessOrEqual(needed, PanelStack.TightestCanvas * .5f,
                               "the obvious reading — everything under one canvas — is wrong by " +
                               "half the panel, and passes layouts whose word is off the top of " +
                               "a tablet");

            Assert.Greater(ContinuePanel.Tallest + ContinuePanel.BannerOverhang,
                           PanelStack.TightestCanvas * .5f,
                           "if this ever stops being true the check has gone slack and would " +
                           "pass anything");
        }

        /// <summary>
        /// Two shapes: the short-of-gems line is the only row that comes and goes, which is why
        /// the offsets are walked rather than written down.
        /// </summary>
        [Test]
        public void TheShortOfGemsLineIsTheOnlyRowThatChangesTheHeight()
        {
            float plain = ContinuePanel.HeightFor(false);
            float shortOfGems = ContinuePanel.HeightFor(true);

            Assert.Greater(shortOfGems, plain, "the short-of-gems line takes room");
            Assert.AreEqual(ContinuePanel.ShortH + ContinuePanel.ShortGap, shortOfGems - plain,
                            .001f, "and takes exactly its own room");

            Assert.AreEqual(shortOfGems, ContinuePanel.Tallest, .001f,
                            "the taller shape is what has to fit");
        }

        /// <summary>
        /// The word must land before the question is asked, or the two read as one screen
        /// arriving rather than as a consequence and a choice.
        /// </summary>
        [Test]
        public void ThePanelWaitsForTheWordToFinishMoving()
        {
            Assert.GreaterOrEqual(ContinuePanel.PanelDelay,
                                  ContinuePanel.BannerPop + ContinuePanel.BannerRise,
                                  "the panel arrives while the word is still travelling");

            Assert.Greater(ContinuePanel.BannerHold, 0f,
                           "the word has to be allowed to land before it moves, or the pop and " +
                           "the rise read as one motion");

            Assert.Less(ContinuePanel.PanelDelay, 2f, "and the whole beat is still a beat");
        }

        [Test]
        public void TheBannerRestsClearOfThePanelAndItsRibbon()
        {
            foreach (bool shortOfGems in new[] { false, true })
            {
                float height = ContinuePanel.HeightFor(shortOfGems);
                float centre = ContinuePanel.BannerCentre(height);

                float bannerFoot = centre - ContinuePanel.BannerHeight * .5f;
                float ribbonTop = height * .5f + PanelStack.TitleOverhang;

                Assert.GreaterOrEqual(bannerFoot, ribbonTop,
                                      "the word is drawn through the title ribbon on the " +
                                      (shortOfGems ? "short" : "affordable") + " shape");
            }
        }
    }
}
