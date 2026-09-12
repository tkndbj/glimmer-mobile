using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// How a siege board divides its height, over every screen shape one could be drawn at.
    ///
    /// <para>
    /// <b>This exists because a render can only look at one shape at a time.</b>
    /// <c>Tools/render_siege.py</c> is the instrument for everything about this board that no
    /// number can see — whether the hill reads, whether a fuel tube has fallen behind the field's
    /// plate — and it draws whatever canvas it is given. That is how the ward line came to be able
    /// to collapse to one and a quarter cells on a 4:3 while every phone it was ever looked at on
    /// was fine: each picture was correct, and nothing swept the shapes between them.
    /// </para>
    /// <para>
    /// The bands are arithmetic over three numbers (<c>SiegeView.Bands.Of</c>), so they can be.
    /// </para>
    /// </summary>
    public sealed class SiegeBandTests
    {
        /// <summary>
        /// Board heights a real display produces, at the canvas's own 1080-unit width: a 4:3
        /// tablet at the short end, a 19.5:9 phone at the tall one, and the 16:9 sheet the render
        /// draws by default in between.
        ///
        /// <para>
        /// Nothing shorter than about 900, because below that the field alone is more than half
        /// the board and <c>MaxGemBand</c>'s clamp is what decides the layout rather than the
        /// ratio — a different question from the one this fixture asks, and not a shape any
        /// display in this game's two store listings has.
        /// </para>
        /// </summary>
        static readonly float[] Spans = { 900f, 1100f, 1356f, 1500f, 1752f, 2000f };

        /// <summary>An 8x5 field at the width, which is every board this chapter ships.</summary>
        const float Cell = 130.5f;
        const int Rows = 5;

        [Test]
        public void TheThreeBandsAlwaysDivideTheWholeBoard()
        {
            foreach (var span in Spans)
            {
                var b = SiegeView.Bands.Of(span, Cell, Rows);

                Assert.AreEqual(1f, b.Gems + b.Hill + b.Line, 1e-4f,
                                $"the bands leave a strip of nothing at span {span}");
                Assert.Greater(b.Hill, 0f, $"no hill at span {span}");
                Assert.Greater(b.Line, 0f, $"no ward line at span {span}");
            }
        }

        /// <summary>
        /// The one that would have caught it: a ward's furniture is measured in cells, so the
        /// band it stands in has to be too. Below this the fuel tube is drawn across the turret's
        /// own chassis (invariant 37y) and the plinth disappears behind the field's plate (37g),
        /// and neither is visible in anything but a picture of that particular screen.
        /// </summary>
        [Test]
        public void TheWardLineIsNeverSqueezedBelowItsOwnFurniture()
        {
            foreach (var span in Spans)
            {
                var b = SiegeView.Bands.Of(span, Cell, Rows);

                Assert.GreaterOrEqual(b.Line * span, 1.5f * Cell - .5f,
                                      $"the ward line is only {b.Line * span / Cell:0.00} cells "
                                      + $"at span {span}");
            }
        }

        /// <summary>
        /// The hill is the band a player spends the run looking at, so on the shape this mode is
        /// actually played at it is the biggest one.
        ///
        /// <para>
        /// <b>Tall only, and that is a fact worth having written down rather than a fudged
        /// threshold.</b> The field's height is fixed by the width, so the shorter the display the
        /// more of the board it is: on the 16:9 sheet the render draws by default the field really
        /// is the largest band, at 48% against the hill's 37%. No phone is that shape — a 19.5:9
        /// display gives the board about 1750 units — but a small tablet is, which is what this
        /// says out loud so the next person to read a wide render is not surprised by it.
        /// </para>
        /// </summary>
        [Test]
        public void TheHillIsTheBiggestBandOnAnythingPhoneShaped()
        {
            foreach (var span in Spans)
            {
                var b = SiegeView.Bands.Of(span, Cell, Rows);

                Assert.Greater(b.Hill, b.Line, $"the ward line outgrew the hill at span {span}");

                if (span < 1600f) continue;

                Assert.Greater(b.Hill, b.Gems, $"the field outgrew the hill at span {span}");
            }
        }

        /// <summary>
        /// A degenerate span is what a screen reports for a frame during a rotation, and a board
        /// that divided by it would be a board of NaN.
        /// </summary>
        [Test]
        public void ABoardOfNothingDividesIntoNothing()
        {
            var b = SiegeView.Bands.Of(0f, Cell, Rows);
            Assert.AreEqual(0f, b.Gems + b.Hill + b.Line);
        }
    }
}
