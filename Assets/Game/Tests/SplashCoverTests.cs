using GlimmerGrove.Layout;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The launch screen's picture and the loading bar under its wordmark.
    ///
    /// <para>
    /// This is the layout with the least to check it anywhere else in the game. The thing the
    /// bar must not collide with is painted <em>into a texture</em>, so there is no rect to
    /// measure at runtime, nothing for a validator to walk, and no compile that can fail — the
    /// only other instrument is looking at one phone, which is exactly how the panel that
    /// <c>PanelStack</c> came out of drew its last paragraph 78 units into its own close
    /// button for two releases.
    /// </para>
    /// </summary>
    public sealed class SplashCoverTests
    {
        /// <summary>
        /// The shapes this game is actually drawn on: the canvas is width-matched at 1080, so
        /// its height is 1080 divided by the display's aspect. 4:3 is the shortest thing a
        /// tablet gives us and 20:9 the tallest a phone does.
        /// </summary>
        static readonly float[] Canvases = { 1440f, 1620f, 1920f, 2160f, 2340f, 2400f, 2520f };

        /// <summary>Bottom insets: none, a home indicator, and an Android navigation bar.</summary>
        static readonly float[] Insets = { 0f, 93f, 144f };

        const float W = 1080f;

        /// <summary>
        /// Nothing anywhere shows through. The picture fills the width on its own; up the
        /// screen it fills what it can and the sky band is exactly the rest, which is the one
        /// place the two numbers have to agree — a band a unit short is a line of empty canvas
        /// across the top of the launch screen.
        /// </summary>
        [Test]
        public void NothingShowsThroughOnAnyCanvasThisGameIsDrawnOn()
        {
            foreach (var h in Canvases)
            {
                var plan = SplashCover.Fit(W, h, 0f);

                Assert.GreaterOrEqual(plan.Width, W - .01f, $"canvas {h}: picture is narrower than the screen");

                float top = plan.PictureY + plan.Height * .5f;
                float bottom = plan.PictureY - plan.Height * .5f;

                Assert.LessOrEqual(bottom, -h * .5f + .01f, $"canvas {h}: ground short of the bottom edge");
                Assert.GreaterOrEqual(top + plan.SkyHeight, h * .5f - .01f,
                                      $"canvas {h}: the sky band does not reach the top edge");
                Assert.LessOrEqual(plan.SkyHeight, System.Math.Max(0f, h - plan.Height) + .01f,
                                   $"canvas {h}: the sky band overruns the picture");
            }
        }

        /// <summary>
        /// The wordmark is four fifths of the picture's width, so a pure cover fit shaves the
        /// outer letters off on the tallest phones — the one crop nobody would accept, because
        /// it is the brand. It is bought with a band of open sky at the very top, and this is
        /// both halves of that trade: the letters keep their margin, and the band is only ever
        /// spent on canvases that could not have had both.
        /// </summary>
        [Test]
        public void TheWordmarkKeepsItsMarginOnEveryCanvasAndOnlyThenIsSkyAdded()
        {
            foreach (var h in Canvases)
            {
                var plan = SplashCover.Fit(W, h, 0f);
                float wordHalf = plan.Width * (SplashCover.WordRightUv - SplashCover.WordLeftUv) * .5f;

                Assert.LessOrEqual(wordHalf, W * .5f - SplashCover.WordMargin + .01f,
                                   $"canvas {h}: the wordmark is clipped at the sides");

                if (plan.SkyHeight > .01f)
                    Assert.Greater(wordHalf, W * .5f - SplashCover.WordMargin - 1f,
                                   $"canvas {h}: sky was added on a canvas the picture could have covered");
            }
        }

        /// <summary>
        /// The crop comes off the top, so the wordmark and the band of ground it stands on are
        /// on screen whatever shape the display is. A centred crop passes the coverage test
        /// above and fails this one on a tablet, which is why both are here.
        /// </summary>
        [Test]
        public void TheWordmarkIsWhollyOnScreenOnEveryCanvas()
        {
            foreach (var h in Canvases)
            {
                var plan = SplashCover.Fit(W, h, 0f);

                // The upper line's head, measured off the same frame the foot was.
                float wordHead = plan.PictureY + plan.Height * (.5f - .763f);

                Assert.Less(wordHead, h * .5f, $"canvas {h}: the wordmark's top is cropped away");
                Assert.Greater(plan.WordFoot, -h * .5f, $"canvas {h}: the wordmark's foot is off the bottom");
            }
        }

        /// <summary>
        /// The property the whole file exists for. A bar drawn over the lettering is the one
        /// outcome that reads as a broken build, and it is invisible to every other check.
        /// </summary>
        [Test]
        public void TheBarNeverDrawsOnTheWordmark()
        {
            foreach (var h in Canvases)
                foreach (var inset in Insets)
                {
                    var plan = SplashCover.Fit(W, h, inset);
                    float barTop = plan.BarY + SplashCover.BarHeight * .5f;

                    Assert.LessOrEqual(barTop, plan.WordFoot - SplashCover.MinGap + .01f,
                                       $"canvas {h}, inset {inset}: the bar is on the word");
                }
        }

        [Test]
        public void TheBarStaysOnTheCanvas()
        {
            foreach (var h in Canvases)
                foreach (var inset in Insets)
                {
                    var plan = SplashCover.Fit(W, h, inset);
                    string what = $"canvas {h}, inset {inset}";

                    Assert.GreaterOrEqual(plan.BarX - plan.BarWidth * .5f, -W * .5f, what + ": bar off the left");
                    Assert.LessOrEqual(plan.BarX + plan.BarWidth * .5f, W * .5f, what + ": bar off the right");
                    Assert.Greater(plan.BarY - SplashCover.BarHeight * .5f, -h * .5f, what + ": bar off the bottom");
                }
        }

        /// <summary>
        /// Where there is room for both, the bar clears the system's inset. Where there is not
        /// — a short canvas with a navigation bar on it — it gives up the inset rather than the
        /// word, which is the ordering <see cref="SplashCover.Fit"/> is built on, and the case
        /// that is easiest to get backwards.
        /// </summary>
        [Test]
        public void TheBarClearsTheSystemInsetWhereverBothWillFit()
        {
            foreach (var h in Canvases)
                foreach (var inset in Insets)
                {
                    var plan = SplashCover.Fit(W, h, inset);

                    float floor = -h * .5f + inset;
                    float barBottom = plan.BarY - SplashCover.BarHeight * .5f;
                    float headroom = plan.WordFoot - SplashCover.MinGap - SplashCover.BarHeight - floor;

                    if (headroom >= SplashCover.Pad)
                        Assert.GreaterOrEqual(barBottom, floor + SplashCover.Pad - .01f,
                                              $"canvas {h}, inset {inset}: the bar sat in the inset with room to spare");
                }
        }

        /// <summary>
        /// The bar is a fraction of the word above it rather than a typed width, so it scales
        /// with the crop. A taller canvas crops the sides and draws the picture — and therefore
        /// the word — larger.
        /// </summary>
        [Test]
        public void TheBarIsMeasuredAgainstTheWordAboveIt()
        {
            var shortCanvas = SplashCover.Fit(W, 1920f, 0f);
            var tallCanvas = SplashCover.Fit(W, 2400f, 0f);

            Assert.Greater(tallCanvas.Width, shortCanvas.Width, "a taller canvas draws the picture wider");
            Assert.Greater(tallCanvas.BarWidth, shortCanvas.BarWidth, "the bar did not follow the word");
            Assert.LessOrEqual(tallCanvas.BarWidth, W - SplashCover.SideMargin * 2f + .01f,
                               "the bar outgrew the margin it is allowed");
        }

        /// <summary>
        /// A degenerate canvas is reported briefly during a resize and on some Android devices
        /// on the first frame after a rotation. It must answer with zeroes rather than an
        /// infinity that puts the picture somewhere unrecoverable.
        /// </summary>
        [Test]
        public void ADegenerateCanvasIsRefusedRatherThanScaled()
        {
            Assert.AreEqual(0f, SplashCover.Fit(0f, 1920f, 0f).Height);
            Assert.AreEqual(0f, SplashCover.Fit(1080f, 0f, 0f).Height);
        }
    }
}
