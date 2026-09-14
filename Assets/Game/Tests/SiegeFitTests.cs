using GlimmerGrove.Layout;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// How big a siege board is drawn, on every display this game runs on.
    ///
    /// <para>
    /// <b><c>SiegeBandTests</c> asks how a board of a given size divides; this asks what size it
    /// is in the first place.</b> That gap is where a tablet fault lived: the bands were swept
    /// over every span a board could have and every one of them divided correctly, while the
    /// <em>cell</em> those spans were measured against was a third bigger on a tablet than on a
    /// phone and nothing anywhere said so. The bands fixture could not see it — handed the
    /// tablet's own cell it would have agreed the division was right — and a render could not
    /// either, because a render draws one display at a time and every one of them had been taken
    /// at a phone.
    /// </para>
    /// <para>
    /// <b>What it holds is <c>CanvasFit</c>'s promise, applied to the one screen that does not
    /// get it for free.</b> A squarer display is given a wider canvas so every layout keeps its
    /// sizes in units and is simply drawn smaller; that works because every other screen here is
    /// a vertical stack of fixed-height chrome. A board laid out to the width is not, so it has
    /// to be capped by hand (invariant 37cc, <c>SiegeView.CellFor</c>) — and this is what says
    /// the cap is still doing its job.
    /// </para>
    /// </summary>
    public sealed class SiegeFitTests
    {
        /// <summary>
        /// A display, as its screen reports itself. Real devices from both store listings, plus
        /// a foldable at the squarest shape anything reaches.
        /// </summary>
        static readonly (string Name, float W, float H)[] Displays =
        {
            ("19.5:9 phone", 1080f, 2340f),
            ("20:9 phone", 1080f, 2400f),
            ("18:9 phone", 1080f, 2160f),
            ("16:9 phone", 1080f, 1920f),
            ("4:3 tablet", 1536f, 2048f),
            ("iPad 10.9", 1640f, 2360f),
            ("16:10 tablet", 1200f, 1920f),
            ("3:2 tablet", 1240f, 1860f),
            ("foldable", 1800f, 2070f),
        };

        /// <summary>
        /// How much of the canvas the board is not given: <c>ModeScreen</c> builds its host inside
        /// the safe area with these insets, and the two rows are the two a run screen really uses
        /// — <c>ProtoScreen</c>'s 330 under the board, and <c>ModeScreen</c>'s own 350.
        ///
        /// <para>
        /// <b>Swept rather than copied, because a hand copy of a number that lives somewhere else
        /// is a number that drifts</b> (the lesson <c>Tools/verify/rungs.py</c> exists for). Every
        /// claim below holds for both, so a retune of either moves nothing here.
        /// </para>
        /// </summary>
        static readonly (float Side, float Top, float Foot)[] Insets =
        {
            (24f, 250f, 330f),
            (24f, 250f, 350f),
        };

        /// <summary>An 8x5 field, which is every board this mode ships.</summary>
        const int W = 8, H = 5;

        /// <summary>Air between the board and the edges of its host. <c>ProtoView.Margin</c>.</summary>
        const float Margin = 18f;

        struct Board
        {
            public float Cell, Span;
            public SiegeView.Bands Bands;
        }

        static Board Measure(float screenW, float screenH, (float Side, float Top, float Foot) inset)
        {
            float canvasW = CanvasFit.WidthFor(screenW, screenH);
            float canvasH = CanvasFit.HeightFor(screenW, screenH);

            var room = new Vector2(canvasW - inset.Side * 2f,
                                   canvasH - inset.Top - inset.Foot);

            // `ProtoView.Begin` floors the cell before anything is measured against it, so the
            // fixture has to as well — a third of a unit is nothing, and a fixture that measured
            // a board the game never draws is a fixture agreeing with itself.
            float cell = Mathf.Floor(SiegeView.CellFor(room, CanvasFit.ScaleFor(screenW, screenH), W, H));
            float span = Mathf.Max(cell * H, room.y - Margin * 2f);

            return new Board { Cell = cell, Span = span, Bands = SiegeView.Bands.Of(span, cell, H) };
        }

        /// <summary>
        /// The one that would have caught it. Everything else in this game keeps its size in
        /// units on a tablet and is drawn smaller because the canvas is wider; a board laid out
        /// to the width does the opposite unless it is stopped, and a cell a third too big takes
        /// the difference out of the hill, because a cell is square.
        /// </summary>
        [Test]
        public void TheBoardIsDrawnAtTheSameSizeInUnitsOnEveryDisplay()
        {
            foreach (var inset in Insets)
            {
                float phone = Measure(1080f, 2340f, inset).Cell;

                foreach (var (name, w, h) in Displays)
                {
                    float cell = Measure(w, h, inset).Cell;

                    Assert.That(cell, Is.EqualTo(phone).Within(5f).Percent,
                                $"{name} draws a {cell} cell against a phone's {phone}");
                }
            }
        }

        /// <summary>
        /// The hill is what a player spends the run looking at, and it is the band that pays for
        /// a field drawn too big — it went to 3.2 cells on a 4:3 against a phone's 6.9, which is
        /// what came back from a tablet as the hill being too small.
        ///
        /// <para>
        /// Three and a half cells is the floor because the 16:9 phone has always sat just under
        /// four and is the shape this mode was tuned on; nothing may go under a phone.
        /// </para>
        /// </summary>
        [Test]
        public void TheHillIsNeverShorterThanOnThePhoneThisModeWasTunedOn()
        {
            foreach (var inset in Insets)
                foreach (var (name, w, h) in Displays)
                {
                    var b = Measure(w, h, inset);

                    Assert.GreaterOrEqual(b.Bands.Hill * b.Span / b.Cell, 3.5f,
                                          $"the hill is only {b.Bands.Hill * b.Span / b.Cell:0.00} "
                                          + $"cells on a {name}");
                }
        }

        /// <summary>
        /// <c>SiegeBandTests</c>' rule, asked of the cell the display really draws rather than a
        /// phone's. A line squeezed onto its own furniture floor is a fuel tube across a turret's
        /// chassis (invariant 37y) and a plinth behind the field's plate (37g) — which is what a
        /// tablet reported as the turrets being obscured by the gem board.
        /// </summary>
        [Test]
        public void TheWardLineNeverLosesItsFurnitureOnAnyDisplay()
        {
            foreach (var inset in Insets)
                foreach (var (name, w, h) in Displays)
                {
                    var b = Measure(w, h, inset);

                    Assert.GreaterOrEqual(b.Bands.Line * b.Span, 1.5f * b.Cell - .5f,
                                          $"the ward line is only {b.Bands.Line * b.Span / b.Cell:0.00} "
                                          + $"cells on a {name}");
                }
        }

        /// <summary>
        /// <c>MaxGemBand</c> is a backstop and must not be the thing deciding the layout.
        ///
        /// <para>
        /// <b>It was, on every tablet, and that is the shape of the fault rather than a detail of
        /// it.</b> A ceiling that binds is a ceiling doing a ratio's job: the field sat on .52 and
        /// the hill and the line divided whatever was left, so the two numbers that were tuned
        /// (<c>HillBand</c> and <c>LineBand</c>) decided nothing at all. Under half the board on
        /// every display is what says the ratio is still in charge.
        /// </para>
        /// </summary>
        [Test]
        public void TheFieldNeverTakesHalfTheBoard()
        {
            foreach (var inset in Insets)
                foreach (var (name, w, h) in Displays)
                {
                    var b = Measure(w, h, inset);

                    Assert.Less(b.Bands.Gems, .5f,
                                $"the field is {b.Bands.Gems:0.000} of the board on a {name}");
                }
        }

        /// <summary>
        /// A degenerate scale is what a divide by a zero-sized screen produces during a rotation,
        /// and on some Android devices on the first frame after one. <c>CanvasFit.IsShort</c>
        /// answers "not short" to a screen of nothing; this answers with a phone's board, which
        /// is the same decision.
        /// </summary>
        [Test]
        public void ADegenerateScaleIsReadAsAPhone()
        {
            var room = new Vector2(1032f, 1760f);
            float phone = SiegeView.CellFor(room, 1f, W, H);

            Assert.AreEqual(phone, SiegeView.CellFor(room, 0f, W, H), 1e-3f);
            Assert.AreEqual(phone, SiegeView.CellFor(room, -1f, W, H), 1e-3f);
            Assert.AreEqual(phone, SiegeView.CellFor(room, 4f, W, H), 1e-3f,
                            "a scale above one would make a board bigger than a phone's");
        }

        /// <summary>A board of no cells is a board of nothing rather than a divide by nought.</summary>
        [Test]
        public void ABoardOfNoCellsFitsNothing()
        {
            Assert.AreEqual(0f, SiegeView.CellFor(new Vector2(1032f, 1760f), 1f, 0, 0));
        }
    }
}
