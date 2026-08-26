using GlimmerGrove.Content;
using GlimmerGrove.Layout;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The out-of-hearts panel's column of ways out — the arithmetic a screenshot cannot check.
    ///
    /// <para>
    /// It grew a third button, which is the point at which a hand-written height stops being
    /// survivable: two typed constants and three offsets under them were right for as long as
    /// nobody added anything, and adding something is exactly what happened. Same lesson
    /// <c>PanelStack</c> was lifted out of a panel drawing its last paragraph 78 units into its
    /// own close button, in English, on the one aspect ratio it was tuned on.
    /// </para>
    /// </summary>
    public sealed class HeartGatePanelTests
    {
        /// <summary>
        /// Nothing overlaps, in either shape the panel can take.
        ///
        /// Structurally guaranteed by a cursor — but that is a claim about the implementation,
        /// and this is the property. A row placed by an absolute offset would compile and fail
        /// here.
        /// </summary>
        [Test]
        public void NeitherShapeLetsTwoRowsTouch()
        {
            foreach (bool watching in new[] { false, true })
            {
                var stack = HeartGatePanel.Of(watching);
                float last = HeartGatePanel.StackTop;
                string what = watching ? "with a video" : "with no video";

                last = Follows(stack.HasWatch, stack.Watch, HeartGatePanel.ActionHeight, last, what);
                last = Follows(true, stack.Shop, HeartGatePanel.ActionHeight, last, what);
                last = Follows(true, stack.Ok, HeartGatePanel.OkHeight, last, what);

                Assert.GreaterOrEqual(stack.Height, last,
                    $"the panel ends before its last button does ({what})");
            }
        }

        static float Follows(bool drawn, float centre, float height, float after, string what)
        {
            if (!drawn) return after;

            float top = centre - height * .5f;
            Assert.GreaterOrEqual(top, after,
                $"a button begins {after - top} above the bottom of the one before it ({what})");

            return centre + height * .5f;
        }

        /// <summary>
        /// The shop is offered whether or not there is a video, and the video is not.
        ///
        /// Not a layout fact. Hearts sell for gems, which need no store connection and may
        /// already be in hand, so the shop is the way out that always works — where an ad
        /// button drawn against an empty inventory is a control that cannot.
        /// </summary>
        [Test]
        public void TheShopIsAlwaysOfferedAndTheVideoIsNot()
        {
            Assert.IsFalse(HeartGatePanel.Of(watching: false).HasWatch);
            Assert.IsTrue(HeartGatePanel.Of(watching: true).HasWatch);

            Assert.Greater(HeartGatePanel.Of(watching: false).Shop, 0f);
            Assert.Greater(HeartGatePanel.Of(watching: true).Shop, 0f);
        }

        /// <summary>
        /// The free way back is above the paid one, and the way that costs nothing at all is
        /// under both.
        ///
        /// <c>DefeatPanel</c>'s rule, and it is not a layout preference: a price above a
        /// rewarded video at the moment somebody has been stopped from playing is the shape a
        /// store reviewer is right to object to.
        /// </summary>
        [Test]
        public void TheVideoIsAboveTheShopAndTheWayOutIsUnderBoth()
        {
            var stack = HeartGatePanel.Of(watching: true);

            Assert.Less(stack.Watch, stack.Shop);
            Assert.Less(stack.Shop, stack.Ok);
        }

        /// <summary>
        /// The taller shape still fits the shortest canvas this game is drawn on.
        ///
        /// <see cref="PanelStack.TallestPanel"/> counts the title ribbon's overhang at
        /// <em>both</em> ends, because a modal is centred. This is the assertion that fails on
        /// a machine rather than on a 4:3 tablet when a fourth way out is added.
        /// </summary>
        [Test]
        public void TheTallerShapeFitsATabletHeldInPortrait()
            => Assert.LessOrEqual(HeartGatePanel.Tallest, PanelStack.TallestPanel,
                                  $"the panel reaches {HeartGatePanel.Tallest} and the shortest " +
                                  $"canvas holds {PanelStack.TallestPanel}");

        /// <summary>
        /// The column clears the countdown above it.
        ///
        /// The countdown's box is centred 500 down and is 84 tall. Written out rather than
        /// derived, because those two numbers live in the panel and the point of this case is
        /// to notice when one of them moves.
        /// </summary>
        [Test]
        public void TheColumnClearsTheCountdown()
            => Assert.GreaterOrEqual(HeartGatePanel.StackTop, 500f + 84f * .5f);

        /// <summary>The panel fits the reference width, with room for the ribbon's inset.</summary>
        [Test]
        public void ThePanelFitsTheReferenceWidth()
            => Assert.LessOrEqual(HeartGatePanel.Width, ChapterMap.Width);
    }
}
