using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// One card, drawn at whatever size the screen has room for.
    ///
    /// <para>
    /// The grove's shop and its picker offer the same catalog, and until <see cref="PieceCard"/>
    /// existed they drew it two ways — the shop in the interface kit's card, the picker in a
    /// drawn box with a traced outline, which is the shape this UI used before it had a kit. The
    /// two are one design now, and these are the two things that would silently make them two
    /// again: the design's own numbers drifting, and the scaling stopping being a scaling.
    /// </para>
    /// <para>
    /// Arithmetic only, and deliberately so: every method here answers a number before anything
    /// is built, which is what lets a card be proved without a canvas. What it cannot see is
    /// whether the card <em>looks</em> right, and no gate in this project can — that is what a
    /// render is for.
    /// </para>
    /// </summary>
    public sealed class PieceCardTests
    {
        /// <summary>
        /// The numbers the shop used to type, now that they live in one place.
        ///
        /// <para>
        /// Written out rather than re-derived, which is the point of a fixture like this: a test
        /// that recomputed the card from the same expressions the card uses would agree with a
        /// wrong card. These are what <c>HomesteadShopScreen</c> drew before the card was
        /// lifted out of it, so a failure here means the shop's own look moved.
        /// </para>
        /// </summary>
        [Test]
        public void TheCardAtItsDesignSizeIsTheOneTheShopAlwaysDrew()
        {
            const float cell = PieceCard.DesignSize;

            Assert.AreEqual(1f, PieceCard.ScaleFor(cell), 1e-4f);
            Assert.AreEqual(196f, PieceCard.ArtBox(cell), 1e-4f, "the picture");
            Assert.AreEqual(-103.5f, PieceCard.ArtCentre(cell), 1e-4f, "where the picture sits");
            Assert.AreEqual(34f, PieceCard.LineY(cell), 1e-4f, "the line under the name");
        }

        /// <summary>
        /// Everything about the card scales together, so a panel with less room draws the same
        /// card smaller rather than a different one.
        ///
        /// <para>
        /// The picture riding at a fixed height inside a shrunken plate is the exact fault the
        /// shop's own note records — a band of empty plate under the picture and none above it —
        /// and it is invisible at the size the numbers were tuned at, because at that size
        /// everything is right by construction.
        /// </para>
        /// </summary>
        [Test]
        public void ASmallerCardIsTheSameCardSmaller()
        {
            const float half = PieceCard.DesignSize * .5f;

            Assert.AreEqual(.5f, PieceCard.ScaleFor(half), 1e-4f);
            Assert.AreEqual(PieceCard.ArtBox(PieceCard.DesignSize) * .5f,
                            PieceCard.ArtBox(half), 1e-4f);
            Assert.AreEqual(PieceCard.ArtCentre(PieceCard.DesignSize) * .5f,
                            PieceCard.ArtCentre(half), 1e-4f);
            Assert.AreEqual(PieceCard.LineY(PieceCard.DesignSize) * .5f,
                            PieceCard.LineY(half), 1e-4f);
        }

        /// <summary>
        /// The picture stays on the plate at every size.
        ///
        /// <para>
        /// <c>ArtCentre</c> is measured down from the plate's top edge and the plate's own
        /// height is measured from the cell, so the two are only guaranteed to agree while the
        /// arithmetic that placed them does. A picture hanging off the top of its card is the
        /// shape of fault a scaling gets wrong — right in proportion, wrong in fact — and it is
        /// invisible at the size the numbers were tuned at, where everything is right by
        /// construction.
        /// </para>
        /// <para>
        /// Deliberately <b>not</b> a check that the picture clears the caption: the box is
        /// square and the art inside it is drawn <c>preserveAspect</c>, so the corners of the
        /// box are empty and the design has always let it reach into the band the two labels
        /// sit in. Asserting otherwise would be a rule this card never had.
        /// </para>
        /// </summary>
        [Test]
        public void ThePictureStaysOnThePlate()
        {
            foreach (float cell in new[] { 200f, 240f, PieceCard.DesignSize, 420f })
            {
                float plateH = cell - 34f * PieceCard.ScaleFor(cell);
                float half = PieceCard.ArtBox(cell) * .5f;

                // Both measured down from the plate's top edge, which is what ArtCentre is
                // relative to. The box is anchored at the plate's top, so its centre is negative.
                float top = -PieceCard.ArtCentre(cell) - half;
                float bottom = -PieceCard.ArtCentre(cell) + half;

                Assert.GreaterOrEqual(top, 0f, $"the picture hangs off the top at {cell}");
                Assert.LessOrEqual(bottom, plateH, $"the picture hangs off the foot at {cell}");
            }
        }

    }
}
