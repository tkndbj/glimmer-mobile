using GlimmerGrove.Layout;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The two marks a shop card wears, and the neighbour they have to share a row with.
    ///
    /// <para>
    /// Written the way <c>WheelPanelTests</c> had to be rewritten: against what is actually
    /// handed to the card, rather than against arithmetic sitting beside it. The fault this
    /// pins was two correct numbers on two different cards — a badge hanging past its own
    /// plate and a ribbon reaching past its neighbour's — so no check that reads one object at
    /// a time could ever have seen it, and it was found by somebody looking at the shop.
    /// </para>
    /// </summary>
    public sealed class ProductCardBadgeTests
    {
        /// <summary>The badge's centre and its outer edge, from its own plate's centre.</summary>
        static float SealCentre => ProductCardBadges.PlateWidth * .5f - ProductCardBadges.SealInset;
        static float SealEdge => SealCentre + ProductCardBadges.SealReach;

        [Test]
        public void TheBadgeStaysOnItsOwnCard()
            => Assert.LessOrEqual(SealEdge, ProductCardBadges.PlateWidth * .5f,
                                  $"the badge reaches {SealEdge} and its plate ends at " +
                                  $"{ProductCardBadges.PlateWidth * .5f}");

        /// <summary>
        /// The one that shipped broken. The shop is two columns at one cell of pitch, so the
        /// card opposite starts a pitch away less half a plate — and its ribbon is pinned far
        /// enough round the corner to reach back over the gutter.
        /// </summary>
        [Test]
        public void TheBadgeIsClearOfTheNextColumnsBonusRibbon()
        {
            float gap = ProductCardBadges.NeighbourRibbon - SealEdge;

            Assert.GreaterOrEqual(gap, ProductCardBadges.Clearance,
                                  $"the badge reaches {SealEdge}, the next card's ribbon begins " +
                                  $"at {ProductCardBadges.NeighbourRibbon}");
        }

        /// <summary>
        /// A ribbon is a rotated rectangle, so it is wider than its own cloth — measured rather
        /// than assumed, because reading its width alone is what made the overlap invisible.
        /// </summary>
        [Test]
        public void APinnedRibbonReachesFurtherThanItsClothIsWide()
            => Assert.Greater(ProductCardBadges.RibbonReach, ProductCardBadges.RibbonWidth * .5f);

        /// <summary>
        /// The badge may overhang the top of its plate — that is what makes it read as stuck on
        /// — but not so far that it reaches the card above, whose plate ends one inset away.
        /// </summary>
        [Test]
        public void TheBadgeDoesNotReachTheCardAbove()
        {
            float rise = ProductCardBadges.SealReach - ProductCardBadges.SealDrop;

            Assert.Greater(rise, 0f, "the badge no longer overhangs its plate at all");
            Assert.LessOrEqual(rise, ProductCardBadges.PlateInsetY - ProductCardBadges.Clearance,
                               $"the badge rises {rise} into a gap of {ProductCardBadges.PlateInsetY}");
        }

        /// <summary>
        /// The caption fits the flat field inside the rim — its <em>corners</em>, because the
        /// field is round and a text box is not. It was sized against the sprite, which is
        /// nearly twice as wide as the field, so a two-word badge spilled across the rim onto
        /// the plate.
        /// </summary>
        [Test]
        public void TheCaptionFitsInsideTheSealsFace()
        {
            float w = ProductCardBadges.TextWidth * .5f;
            float h = ProductCardBadges.TextHeight * .5f;
            float corner = (float)System.Math.Sqrt(w * w + h * h);
            float field = ProductCardBadges.SealSize * ProductCardBadges.Face * .5f;

            Assert.LessOrEqual(corner, field,
                               $"the caption's corner reaches {corner} of a field {field} across");
        }

        /// <summary>
        /// The caption is centred on the field rather than on the sprite. The two are not the
        /// same point — the disc is drawn a little high and a little left in its texture — and
        /// a badge centred on the texture sits low in the one it is read against.
        /// </summary>
        [Test]
        public void TheCaptionIsCentredOnTheFieldRatherThanOnTheSprite()
        {
            Assert.AreNotEqual(0f, ProductCardBadges.TextRise);
            Assert.AreEqual(ProductCardBadges.SealSize * ProductCardBadges.FaceRise,
                            ProductCardBadges.TextRise, .001f);
            Assert.AreEqual(ProductCardBadges.SealSize * ProductCardBadges.FaceShift,
                            ProductCardBadges.TextShift, .001f);
        }

        /// <summary>
        /// The seal is measured as the disc it is rather than as the square its texture is —
        /// the difference is a sixth of a badge, and spending it would push the mark into the
        /// picture underneath for no gain.
        /// </summary>
        [Test]
        public void TheBadgeIsMeasuredAsADiscRatherThanAsItsTexture()
            => Assert.Less(ProductCardBadges.SealReach, ProductCardBadges.SealSize * .5f);
    }
}
