using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The upgrade ceremony's arithmetic: where its bands sit, and that the star it drops lands in
    /// the slot it was bought for.
    ///
    /// <para>
    /// <b>This exists for <c>WardRevealTests</c>' reason and it is the same poor substitute.</b>
    /// There is a render for the hub, the shop, the siege and Prismvale, and each of them has
    /// caught faults every numeric gate was green through; there is none for a modal ceremony. So
    /// the one class of fault that would otherwise ship unseen here — a caption drawn through the
    /// thing above it, a band off the canvas — is checked as edges. If this screen ever gets a
    /// render, that is the better instrument.
    /// </para>
    /// </summary>
    public sealed class WardUpgradeRevealTests
    {
        /// <summary>The canvas the ceremony is laid out on: 1080 wide by <c>Boot.RefHeight</c>.</summary>
        const float Top = Boot.RefHeight * .5f, Bottom = -Boot.RefHeight * .5f;
        const float HalfWide = Boot.RefWidth * .5f;

        [Test]
        public void TheBandsDescendAndNoneOfThemOverlaps()
        {
            var bands = new (string name, float top, float bottom)[]
            {
                Named("title", WardUpgradeRevealOverlay.TitleY, WardUpgradeRevealOverlay.TitleH),
                Named("name", WardUpgradeRevealOverlay.NameY, WardUpgradeRevealOverlay.NameH),
                Named("stars", WardUpgradeRevealOverlay.StarsY, WardUpgradeRevealOverlay.StarsH),
                Named("plate", WardUpgradeRevealOverlay.PlateY, WardUpgradeRevealOverlay.PlateH),
                Named("bars", WardUpgradeRevealOverlay.BarsY, WardUpgradeRevealOverlay.BarsH),
                Named("key", WardUpgradeRevealOverlay.ActY, WardUpgradeRevealOverlay.ActH),
            };

            for (int i = 1; i < bands.Length; i++)
                Assert.Less(bands[i].top, bands[i - 1].bottom,
                            $"{bands[i].name} runs into {bands[i - 1].name}");
        }

        [Test]
        public void EveryBandIsFarEnoughFromTheOneAboveToRead()
        {
            // Touching is not the bar: a title whose descenders start on the name under it is
            // legible and reads as a mistake, which is the whole reason these are spaced rather
            // than merely made not to overlap.
            Assert.GreaterOrEqual(Gap(WardUpgradeRevealOverlay.TitleY, WardUpgradeRevealOverlay.TitleH,
                                      WardUpgradeRevealOverlay.NameY, WardUpgradeRevealOverlay.NameH),
                                  20f, "the name under the title");

            Assert.GreaterOrEqual(Gap(WardUpgradeRevealOverlay.NameY, WardUpgradeRevealOverlay.NameH,
                                      WardUpgradeRevealOverlay.StarsY, WardUpgradeRevealOverlay.StarsH),
                                  20f, "the ladder under the name");

            Assert.GreaterOrEqual(Gap(WardUpgradeRevealOverlay.StarsY, WardUpgradeRevealOverlay.StarsH,
                                      WardUpgradeRevealOverlay.PlateY, WardUpgradeRevealOverlay.PlateH),
                                  20f, "the plate under the ladder");

            Assert.GreaterOrEqual(Gap(WardUpgradeRevealOverlay.BarsY, WardUpgradeRevealOverlay.BarsH,
                                      WardUpgradeRevealOverlay.ActY, WardUpgradeRevealOverlay.ActH),
                                  20f, "the key under the bars");
        }

        [Test]
        public void NothingIsDrawnOffTheCanvas()
        {
            // Both ends, because the two failures look completely different: a title off the top
            // is a word with its head cut off, and a key off the bottom is a control nobody can
            // press.
            Assert.LessOrEqual(WardUpgradeRevealOverlay.TitleY + WardUpgradeRevealOverlay.TitleH * .5f,
                               Top, "the title's head");

            Assert.GreaterOrEqual(WardUpgradeRevealOverlay.ActY - WardUpgradeRevealOverlay.ActH * .5f,
                                  Bottom, "the key's foot");
        }

        [Test]
        public void TheGainBesideABarStaysOnTheCanvas()
        {
            // The mark is written in the column outside the bars' own host, which only exists
            // because that host is narrower than the canvas. A wider host would put a "+12"
            // off the side of the screen, and nothing but this would say so — see
            // `WardUpgradeRevealOverlay.Mark`.
            const float MarkX = 74f, MarkW = 128f;

            float right = WardUpgradeRevealOverlay.BarsW * .5f + MarkX + MarkW * .5f;

            Assert.LessOrEqual(right, HalfWide, "the gain mark's right edge");
        }

        [Test]
        public void TheStarFallsIntoTheSlotItWasBoughtFor()
        {
            // The ceremony aims at `WardStarRow.XOf` rather than at arithmetic of its own, so what
            // is pinned is that the row really puts its stars there — which is what stops the
            // falling star landing beside the one it becomes the day the size or the gap moves.
            float size = WardUpgradeRevealOverlay.StarSize;
            float width = WardStarRow.Width(size);

            for (int i = 0; i < WardStars.Most; i++)
            {
                float x = WardStarRow.XOf(i, size);

                Assert.GreaterOrEqual(x - size * .5f, -width * .5f - .01f, $"star {i} off the left");
                Assert.LessOrEqual(x + size * .5f, width * .5f + .01f, $"star {i} off the right");
            }

            Assert.Less(WardStarRow.XOf(0, size), WardStarRow.XOf(WardStars.Most - 1, size),
                        "the ladder runs left to right");

            // And the row fits the band it is hosted in.
            Assert.LessOrEqual(width, Boot.RefWidth, "the ladder is wider than the canvas");
        }

        static (float top, float bottom) Edges(float middle, float height)
            => (middle + height * .5f, middle - height * .5f);

        static (string, float, float) Named(string name, float middle, float height)
        {
            var (top, bottom) = Edges(middle, height);
            return (name, top, bottom);
        }

        static float Gap(float upperMiddle, float upperHeight, float lowerMiddle, float lowerHeight)
            => (upperMiddle - upperHeight * .5f) - (lowerMiddle + lowerHeight * .5f);
    }
}
