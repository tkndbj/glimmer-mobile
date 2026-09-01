using GlimmerGrove.Layout;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// How wide the canvas is on the display it is drawn on.
    ///
    /// <para>
    /// The rule this exists to keep is the one a tablet broke: every layout in this game is a
    /// vertical stack of fixed-unit chrome, the canvas is width-matched, and a 4:3 display
    /// therefore hands that stack 1440 units of height where a phone hands it 2340. Nothing
    /// could see it — no constant is wrong, no compile fails, no validator reads Presentation,
    /// and a screenshot on a phone is correct — so it was reported from an iPad as everything
    /// overlapping.
    /// </para>
    /// <para>
    /// The cases below are the ones that matter: that every real phone comes back untouched
    /// (the promise the change was made under), that every real tablet gets the height the
    /// layouts were built against, and that the two numbers the rest of the game reads —
    /// <see cref="CanvasFit.ShortestCanvas"/> and the hub's own budget — are still true of what
    /// the function actually returns.
    /// </para>
    /// </summary>
    public sealed class CanvasFitTests
    {
        // Portrait device sizes, in real device pixels, of things this game is shipped to.
        static readonly (string name, int w, int h)[] Phones =
        {
            ("iPhone SE (16:9)",        750, 1334),
            ("iPhone 8 Plus",           1080, 1920),
            ("iPhone 13",               1170, 2532),
            ("iPhone 15 Pro Max",       1290, 2796),
            ("Pixel 8",                 1080, 2400),
            ("Galaxy S24",              1080, 2340),
            ("budget 18:9 Android",     720, 1440),
        };

        static readonly (string name, int w, int h)[] Tablets =
        {
            ("iPad 10.2 (4:3)",         1620, 2160),
            ("iPad Pro 12.9 (4:3)",     2048, 2732),
            ("iPad Pro 11 (1.43)",      1668, 2388),
            ("iPad mini (1.52)",        1488, 2266),
            ("Android tablet (16:10)",  1200, 1920),
            ("Galaxy Fold, unfolded",   1812, 2176),
        };

        // ------------------------------------------------------------------ phones
        /// <summary>
        /// The promise the whole change was made under. A phone is drawn exactly as it was, so
        /// nothing that ships today can move.
        /// </summary>
        [Test]
        public void EveryPhoneIsDrawnAtTheWidthItAlwaysWas()
        {
            foreach (var (name, w, h) in Phones)
            {
                Assert.IsFalse(CanvasFit.IsShort(w, h), name + " is a phone");
                Assert.AreEqual(CanvasFit.PhoneWidth, CanvasFit.WidthFor(w, h), .001f, name);
                Assert.AreEqual(1f, CanvasFit.ScaleFor(w, h), .001f, name + " draws at full size");
            }
        }

        /// <summary>
        /// The squarest phone that ships is 16:9, and it is the one closest to the threshold —
        /// so it is the case a floor set carelessly would take with it.
        /// </summary>
        [Test]
        public void TheSquarestPhoneIsStillClearOfTheFloor()
        {
            const float SixteenNine = 16f / 9f;

            Assert.Greater(SixteenNine, CanvasFit.PhoneFloor,
                           "a 16:9 phone must be left alone, and it is the nearest one to the line");

            Assert.Less(16f / 10f, CanvasFit.PhoneFloor,
                        "and the tallest tablet must not be, or a 16:10 slate keeps a phone's canvas");
        }

        // ------------------------------------------------------------------ tablets
        [Test]
        public void EveryTabletIsGivenTheHeightTheLayoutsWereBuiltAgainst()
        {
            foreach (var (name, w, h) in Tablets)
            {
                Assert.IsTrue(CanvasFit.IsShort(w, h), name + " is squarer than a phone");
                Assert.AreEqual(CanvasFit.ShortHeight, CanvasFit.HeightFor(w, h), .01f,
                                name + " should be handed exactly the short-display budget");
                Assert.Greater(CanvasFit.WidthFor(w, h), CanvasFit.PhoneWidth,
                               name + " should be drawn on a wider canvas, so smaller");
            }
        }

        /// <summary>
        /// A canvas is only ever <em>wider</em> than a phone's, never narrower — the whole game
        /// is laid out in boxes up to 1080 units across (a modal is 960), and a canvas narrower
        /// than that would cut them off rather than shrink them.
        /// </summary>
        [Test]
        public void NoDisplayIsEverGivenLessWidthThanAPhone()
        {
            for (int h = 400; h <= 4000; h += 20)
                for (int w = 400; w <= 4000; w += 20)
                    Assert.GreaterOrEqual(CanvasFit.WidthFor(w, h), CanvasFit.PhoneWidth,
                                          w + "x" + h);
        }

        // ------------------------------------------------------------------ the guarantee
        /// <summary>
        /// <see cref="CanvasFit.ShortestCanvas"/> is what <c>PanelStack</c> and everything under
        /// it measure against, so it has to be a bound on what the function returns rather than
        /// a number beside it.
        /// </summary>
        [Test]
        public void NoDisplayProducesACanvasShorterThanTheStatedFloor()
        {
            for (int h = 400; h <= 4000; h += 10)
                for (int w = 400; w <= 4000; w += 10)
                    Assert.GreaterOrEqual(CanvasFit.HeightFor(w, h),
                                          CanvasFit.ShortestCanvas - .01f, w + "x" + h);
        }

        /// <summary>
        /// The number the short-display budget was chosen for, stated as the sum it came from.
        ///
        /// <para>
        /// The hub is the deepest layout in the game and the only screen with no elastic region
        /// at all: 856 units of chrome down from the top, a companion centre-anchored 130 below
        /// the middle reaching 261 above its own centre, and a play key whose top sits 482 above
        /// the bottom with the companion reaching 285 below its centre. Both clearances are
        /// asserted rather than the one that happens to bind today, because which of them binds
        /// is exactly what a retune moves.
        /// </para>
        /// </summary>
        [Test]
        public void AShortDisplayHasRoomForTheDeepestScreenInTheGame()
        {
            const float ChromeTop = 856f;        // HomeScreen: the feature row's underside
            const float HeroAbove = 131f;        // the companion's reach above the canvas middle
            const float ChromeBottom = 482f;     // the play key's top edge, from the bottom
            const float HeroBelow = 415f;        // its reach below the canvas middle

            float half = CanvasFit.ShortHeight * .5f;

            Assert.Greater(half - HeroAbove, ChromeTop,
                           "the companion is drawn through the streak and event boxes");
            Assert.Greater(half - HeroBelow, ChromeBottom,
                           "the companion is drawn through the play key");
        }

        // ------------------------------------------------------------------ shape
        [Test]
        public void ADegenerateReadingIsTreatedAsAPhoneRatherThanTrusted()
        {
            // A zero-sized screen happens during a resize and on the first frame after some
            // rotations. Dividing by it would be a canvas width of infinity.
            Assert.AreEqual(CanvasFit.PhoneWidth, CanvasFit.WidthFor(0, 0), .001f);
            Assert.AreEqual(CanvasFit.PhoneWidth, CanvasFit.WidthFor(1080, 0), .001f);
            Assert.AreEqual(CanvasFit.PhoneWidth, CanvasFit.WidthFor(0, 1920), .001f);
            Assert.IsFalse(CanvasFit.IsShort(0, 0));
        }

        /// <summary>
        /// The same display always answers the same thing, whatever units it is measured in — a
        /// canvas that depended on a device's pixel count rather than its shape would move under
        /// a player switching to a lower render scale.
        /// </summary>
        [Test]
        public void OnlyTheShapeOfADisplayDecidesAnything()
        {
            foreach (var (name, w, h) in Tablets)
            {
                Assert.AreEqual(CanvasFit.WidthFor(w, h), CanvasFit.WidthFor(w * 2, h * 2), .001f, name);
                Assert.AreEqual(CanvasFit.WidthFor(w, h), CanvasFit.WidthFor(w * .5f, h * .5f), .001f, name);
            }
        }

        /// <summary>
        /// Squarer means smaller, monotonically — a wider canvas is the same interface drawn
        /// smaller, so the width may only ever rise as the display squares up. A rule with a
        /// dip in it would draw one tablet larger than a squarer one for no reason anybody
        /// could name.
        /// </summary>
        [Test]
        public void TheSquarerADisplayIsTheSmallerItDrawsAndItNeverTurnsBack()
        {
            float previous = 0f;

            for (float aspect = 2.4f; aspect >= 1.0f; aspect -= 0.01f)
            {
                float width = CanvasFit.WidthFor(1000f, 1000f * aspect);

                Assert.GreaterOrEqual(width, CanvasFit.PhoneWidth, "aspect " + aspect);
                Assert.GreaterOrEqual(width, previous - .001f, "aspect " + aspect);
                previous = width;
            }
        }
    }
}
