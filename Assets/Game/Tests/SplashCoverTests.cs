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
        /// The shapes this game is actually drawn on, as whole canvases rather than heights.
        ///
        /// <para>
        /// <b>It used to be a list of heights at a fixed 1080 wide, and that is how a cropped
        /// wordmark got past it.</b> A phone is drawn at <see cref="CanvasFit.PhoneWidth"/> and
        /// is never squarer than <see cref="CanvasFit.PhoneFloor"/>, so 1080x1890 is the
        /// squarest canvas that can exist at that width — every shorter entry in the old list
        /// was a shape no display produces. Anything squarer than a phone is handed
        /// <see cref="CanvasFit.ShortHeight"/> and a <em>wider</em> canvas instead, and those are
        /// the shapes the fixture had none of: a 4:3 tablet is 1620x2160, a foldable opened is
        /// nearer 1800x2160, and split view can reach 1:1. Those are the canvases that crop the
        /// most off this cover, so they are the ones that decide whether the mark survives.
        /// </para>
        /// </summary>
        static readonly (float W, float H)[] Canvases =
        {
            // phones: 1080 across, from the squarest that can exist to a 21:9
            (1080f, 1890f), (1080f, 1922f), (1080f, 1998f), (1080f, 2160f),
            (1080f, 2279f), (1080f, 2344f), (1080f, 2398f), (1080f, 2516f),

            // squarer than a phone: 2160 tall, widened — tablet, foldable, split view
            (1241f, 2160f), (1350f, 2160f), (1440f, 2160f),
            (1624f, 2160f), (1800f, 2160f), (2160f, 2160f),
        };

        /// <summary>Bottom insets: none, a home indicator, and an Android navigation bar.</summary>
        static readonly float[] Insets = { 0f, 93f, 144f };

        /// <summary>
        /// Nothing anywhere shows through. The picture fills the width on its own; up the
        /// screen it fills what it can and the sky band is exactly the rest, which is the one
        /// place the two numbers have to agree — a band a unit short is a line of empty canvas
        /// across the top of the launch screen.
        /// </summary>
        [Test]
        public void NothingShowsThroughOnAnyCanvasThisGameIsDrawnOn()
        {
            foreach (var (w, h) in Canvases)
            {
                var plan = SplashCover.Fit(w, h, 0f);

                Assert.GreaterOrEqual(plan.Width, w - .01f, $"canvas {w}x{h}: picture is narrower than the screen");

                float top = plan.PictureY + plan.Height * .5f;
                float bottom = plan.PictureY - plan.Height * .5f;

                Assert.LessOrEqual(bottom, -h * .5f + .01f, $"canvas {w}x{h}: ground short of the bottom edge");
                Assert.GreaterOrEqual(top + plan.SkyHeight, h * .5f - .01f,
                                      $"canvas {w}x{h}: the sky band does not reach the top edge");
                Assert.LessOrEqual(plan.SkyHeight, System.Math.Max(0f, h - plan.Height) + .01f,
                                   $"canvas {w}x{h}: the sky band overruns the picture");
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
            foreach (var (w, h) in Canvases)
            {
                var plan = SplashCover.Fit(w, h, 0f);
                float wordHalf = plan.Width * (SplashCover.WordRightUv - SplashCover.WordLeftUv) * .5f;

                Assert.LessOrEqual(wordHalf, w * .5f - SplashCover.WordMargin + .01f,
                                   $"canvas {w}x{h}: the wordmark is clipped at the sides");

                if (plan.SkyHeight > .01f)
                    Assert.Greater(wordHalf, w * .5f - SplashCover.WordMargin - 1f,
                                   $"canvas {w}x{h}: sky was added on a canvas the picture could have covered");
            }
        }

        /// <summary>
        /// The mark is wholly on screen whatever shape the display is — the property
        /// <see cref="SplashCover.MarkOnCanvas"/> exists for, and the one a bottom-aligned fit
        /// fails.
        ///
        /// <para>
        /// <b>This is the test that was passing for the wrong reason.</b> Its assertions have
        /// not changed; the list of canvases under them has. The cover this file was written for
        /// carried its mark in the bottom tenth, so standing the picture on the canvas floor
        /// kept it on screen by construction and no shape could fail — and the fixture had no
        /// widened canvases in it to try. The cover that replaced it carries the mark across the
        /// middle, and bottom-aligned it is cropped through the logo on anything squarer than
        /// about 5:4.
        /// </para>
        /// </summary>
        [Test]
        public void TheWordmarkIsWhollyOnScreenOnEveryCanvas()
        {
            foreach (var (w, h) in Canvases)
            {
                var plan = SplashCover.Fit(w, h, 0f);
                float wordHead = plan.PictureY + plan.Height * (.5f - SplashCover.WordHeadUv);

                Assert.Less(wordHead, h * .5f, $"canvas {w}x{h}: the wordmark's top is cropped away");
                Assert.Greater(plan.WordFoot, -h * .5f, $"canvas {w}x{h}: the wordmark's foot is off the bottom");
            }
        }

        /// <summary>
        /// Neither edge of the picture ever pulls away from the canvas, however far the mark
        /// would have liked to move it. The clamp in <see cref="SplashCover.Fit"/> is the only
        /// thing holding this, and what it is holding against is a canvas shape rather than a
        /// mistake — the squarer the display, the further the mark wants to travel.
        /// </summary>
        [Test]
        public void HangingThePictureOnTheMarkNeverUncoversAnEdge()
        {
            foreach (var (w, h) in Canvases)
            {
                var plan = SplashCover.Fit(w, h, 0f);
                if (plan.SkyHeight > .01f) continue;      // the capped-zoom case, covered above

                Assert.LessOrEqual(plan.PictureY - plan.Height * .5f, -h * .5f + .01f,
                                   $"canvas {w}x{h}: the picture came away from the bottom edge");
                Assert.GreaterOrEqual(plan.PictureY + plan.Height * .5f, h * .5f - .01f,
                                      $"canvas {w}x{h}: the picture came away from the top edge");
            }
        }

        /// <summary>
        /// The property the whole file exists for. A bar drawn over the lettering is the one
        /// outcome that reads as a broken build, and it is invisible to every other check.
        /// </summary>
        [Test]
        public void TheBarNeverDrawsOnTheWordmark()
        {
            foreach (var (w, h) in Canvases)
                foreach (var inset in Insets)
                {
                    var plan = SplashCover.Fit(w, h, inset);
                    float barTop = plan.BarY + SplashCover.BarHeight * .5f;

                    Assert.LessOrEqual(barTop, plan.WordFoot - SplashCover.MinGap + .01f,
                                       $"canvas {w}x{h}, inset {inset}: the bar is on the word");
                }
        }

        [Test]
        public void TheBarStaysOnTheCanvas()
        {
            foreach (var (w, h) in Canvases)
                foreach (var inset in Insets)
                {
                    var plan = SplashCover.Fit(w, h, inset);
                    string what = $"canvas {w}x{h}, inset {inset}";

                    Assert.GreaterOrEqual(plan.BarX - plan.BarWidth * .5f, -w * .5f, what + ": bar off the left");
                    Assert.LessOrEqual(plan.BarX + plan.BarWidth * .5f, w * .5f, what + ": bar off the right");
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
            foreach (var (w, h) in Canvases)
                foreach (var inset in Insets)
                {
                    var plan = SplashCover.Fit(w, h, inset);

                    float floor = -h * .5f + inset;
                    float barBottom = plan.BarY - SplashCover.BarHeight * .5f;
                    float headroom = plan.WordFoot - SplashCover.MinGap - SplashCover.BarHeight - floor;

                    if (headroom >= SplashCover.Pad)
                        Assert.GreaterOrEqual(barBottom, floor + SplashCover.Pad - .01f,
                                              $"canvas {w}x{h}, inset {inset}: the bar sat in the inset with room to spare");
                }
        }

        /// <summary>
        /// The bar is placed by the foot rule and the wordmark ceiling never binds.
        ///
        /// <para>
        /// A ceiling that binds is a ceiling doing a ratio's job (invariant 37cc): if
        /// <see cref="SplashCover.MinGap"/> were what decided where the bar went on some canvas,
        /// then <see cref="SplashCover.Foot"/> and <see cref="SplashCover.Pad"/> — the two
        /// numbers anybody would reach for to tune it — would be deciding nothing there, and no
        /// other check in this file could tell. The guard is kept because a future cover may set
        /// its mark low again; it must not be load-bearing for <em>this</em> one.
        /// </para>
        /// </summary>
        [Test]
        public void TheBarIsPlacedByTheFootRuleAndNotByTheWordmarkCeiling()
        {
            foreach (var (w, h) in Canvases)
                foreach (var inset in Insets)
                {
                    var plan = SplashCover.Fit(w, h, inset);
                    float wanted = -h * .5f + inset + SplashCover.Pad + SplashCover.Foot
                                   + SplashCover.BarHeight * .5f;

                    Assert.AreEqual(wanted, plan.BarY, .01f,
                                    $"canvas {w}x{h}, inset {inset}: the wordmark ceiling moved the bar");
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
            var shortCanvas = SplashCover.Fit(1080f, 1920f, 0f);
            var tallCanvas = SplashCover.Fit(1080f, 2400f, 0f);

            Assert.Greater(tallCanvas.Width, shortCanvas.Width, "a taller canvas draws the picture wider");
            Assert.Greater(tallCanvas.BarWidth, shortCanvas.BarWidth, "the bar did not follow the word");
            Assert.LessOrEqual(tallCanvas.BarWidth, 1080f - SplashCover.SideMargin * 2f + .01f,
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
