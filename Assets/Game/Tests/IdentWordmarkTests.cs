using GlimmerGrove.Layout;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// How big the publisher card's mark is drawn.
    ///
    /// <para>
    /// The card is the first thing a player ever sees and there is nothing else that can check
    /// it: the glyphs are inside a PNG, so no compile and no validator can reach them, and the
    /// only other instrument is looking at one phone. What can be checked is the arithmetic
    /// around them — that the mark fits the narrowest canvas this game can be drawn on, that it
    /// does not grow into a banner on the widest, and that a degenerate reading answers nothing
    /// rather than something inverted.
    /// </para>
    /// <para>
    /// The aspect below is the shipped bake's, read once and passed in. It is deliberately not a
    /// constant in <see cref="IdentWordmark"/>: the mark is measured off the sprite at runtime,
    /// so a re-cut at another weight cannot leave a number quietly describing the previous one.
    /// </para>
    /// </summary>
    public sealed class IdentWordmarkTests
    {
        /// <summary>Orbitron Black at the shipped tracking: 1800 x 161.</summary>
        const float Aspect = 1800f / 161f;

        /// <summary>
        /// The canvases <see cref="CanvasFit"/> can produce: a phone is 1080 across and anything
        /// squarer is handed a wider canvas, up to a square one in split view.
        /// </summary>
        static readonly float[] Widths = { 1080f, 1241f, 1350f, 1440f, 1624f, 1800f, 2160f };

        [Test]
        public void TheMarkFitsEveryCanvasWithItsMarginsIntact()
        {
            foreach (var w in Widths)
            {
                var plan = IdentWordmark.Fit(w, Aspect);

                Assert.Greater(plan.Width, 0f, $"canvas {w}: the mark does not fit at all");
                Assert.LessOrEqual(plan.Width, w - IdentWordmark.SideMargin * 2f + .01f,
                                   $"canvas {w}: the mark is wider than the air it was given");
                Assert.AreEqual(plan.Width / Aspect, plan.Height, .01f,
                                $"canvas {w}: the mark was drawn out of proportion");
            }
        }

        /// <summary>
        /// It never grows into a banner. A fraction of the canvas is right on a phone and wrong
        /// on a square one, where it would draw over a thousand units of lettering across a card
        /// whose whole job is to be a small thing in a lot of black.
        /// </summary>
        [Test]
        public void TheMarkIsCappedOnTheWidestCanvases()
        {
            foreach (var w in Widths)
                Assert.LessOrEqual(IdentWordmark.Fit(w, Aspect).Width, IdentWordmark.MaxWidth + .01f,
                                   $"canvas {w}: the mark outgrew its cap");

            Assert.AreEqual(IdentWordmark.MaxWidth, IdentWordmark.Fit(2160f, Aspect).Width, .01f,
                            "the cap did not bind on the widest canvas there is");
        }

        /// <summary>
        /// The rule is measured from the mark rather than typed, so it cannot be left describing
        /// a mark that has since been re-cut — and it always overhangs, because a rule that
        /// stops short of the word reads as an underline that missed.
        /// </summary>
        [Test]
        public void TheRuleIsMeasuredFromTheMarkAndClearsItsBaseline()
        {
            foreach (var w in Widths)
            {
                var plan = IdentWordmark.Fit(w, Aspect);

                Assert.AreEqual(plan.Width + IdentWordmark.RuleOverhang * 2f, plan.RuleWidth, .01f,
                                $"canvas {w}: the rule is not the mark plus its overhang");
                Assert.Less(plan.RuleY, -plan.Height * .5f,
                            $"canvas {w}: the rule is drawn through the lettering");
            }
        }

        /// <summary>
        /// A degenerate canvas is reported briefly during a resize and on some Android devices on
        /// the first frame after a rotation. So is a sprite that failed to load. Both must answer
        /// with zeroes rather than something the screen would try to draw.
        /// </summary>
        [Test]
        public void ADegenerateReadingIsRefusedRatherThanInverted()
        {
            Assert.AreEqual(0f, IdentWordmark.Fit(0f, Aspect).Width);
            Assert.AreEqual(0f, IdentWordmark.Fit(-10f, Aspect).Width);
            Assert.AreEqual(0f, IdentWordmark.Fit(1080f, 0f).Width);
            Assert.AreEqual(0f, IdentWordmark.Fit(100f, Aspect).Width,
                            "a canvas narrower than its own margins must answer nothing");
        }
    }
}
