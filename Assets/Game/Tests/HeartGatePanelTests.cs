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
        /// Nothing overlaps, in any of the four shapes the panel can take.
        ///
        /// Structurally guaranteed by a cursor — but that is a claim about the implementation,
        /// and this is the property. A row placed by an absolute offset would compile and fail
        /// here.
        ///
        /// Four rather than two since a second panel started drawing this column: the restart
        /// gate over a run has no shop to send anybody to, so its paid row is a rescue that can
        /// genuinely not exist. Both conditional rows are now walked together, which is the
        /// combination neither caller draws on its own and therefore the one nobody would see.
        /// </summary>
        [Test]
        public void NoShapeLetsTwoRowsTouch()
        {
            foreach (bool watching in new[] { false, true })
            foreach (bool buying in new[] { false, true })
            {
                var stack = HeartGatePanel.Of(watching, buying);
                float last = HeartGatePanel.StackTop;
                string what = $"video={watching}, paid={buying}";

                last = Follows(stack.HasWatch, stack.Watch, HeartGatePanel.ActionHeight, last, what);
                last = Follows(stack.HasPaid, stack.Paid, HeartGatePanel.ActionHeight, last, what);
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
        /// Each row is drawn when it is asked for and not otherwise, and the two are
        /// independent.
        ///
        /// The independence is the part worth asserting. Both callers happen to pass the video
        /// flag from the same reading, so a column that quietly tied the paid row to it would
        /// look correct on the out-of-hearts panel — which always wants both — and would silently
        /// drop the rescue on the restart gate whenever no video had loaded.
        /// </summary>
        [Test]
        public void EachRowIsDrawnExactlyWhenItIsAskedFor()
        {
            Assert.IsFalse(HeartGatePanel.Of(watching: false, buying: true).HasWatch);
            Assert.IsTrue(HeartGatePanel.Of(watching: true, buying: false).HasWatch);

            Assert.IsFalse(HeartGatePanel.Of(watching: true, buying: false).HasPaid);
            Assert.IsTrue(HeartGatePanel.Of(watching: false, buying: true).HasPaid);
        }

        /// <summary>
        /// A row that is not drawn gives its room back rather than leaving a hole.
        ///
        /// What a cursor buys over four typed offsets, and the reason the shortest shape is
        /// worth having at all: the restart gate with no video and no affordable rescue is a
        /// title, a sentence, a clock and one way out, and reserving two buttons of air under it
        /// would read as a panel that failed to finish drawing.
        /// </summary>
        [Test]
        public void AnUndrawnRowCostsThePanelNoHeight()
        {
            float full = HeartGatePanel.Of(watching: true, buying: true).Height;
            float bare = HeartGatePanel.Of(watching: false, buying: false).Height;

            Assert.AreEqual(2f * (HeartGatePanel.ActionHeight + HeartGatePanel.Gap), full - bare,
                            "two undrawn rows should give back exactly their own room");
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
        public void TheVideoIsAboveThePaidWayAndTheWayOutIsUnderBoth()
        {
            var stack = HeartGatePanel.Of(watching: true, buying: true);

            Assert.Less(stack.Watch, stack.Paid);
            Assert.Less(stack.Paid, stack.Ok);
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
