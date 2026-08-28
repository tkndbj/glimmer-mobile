using GlimmerGrove.Layout;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The defeat panel's action stack, which is the arithmetic a screenshot cannot check.
    ///
    /// <para>
    /// The panel can now take five shapes — try again; wait; wait and watch; wait and pay; wait
    /// and both — and until the third way out was added its height was two hand-written
    /// constants with the button offsets under them written out one at a time. That is exactly
    /// the arrangement <c>PanelStack</c> was lifted out of a panel that had been drawing its
    /// last paragraph 78 units into its own close button, in English, on the one aspect ratio
    /// it was tuned on.
    /// </para>
    /// </summary>
    public sealed class DefeatPanelTests
    {
        /// <summary>
        /// Nothing overlaps in any shape the panel can take.
        ///
        /// Structurally guaranteed by a cursor — but that is a claim about the implementation,
        /// and this is the property. A row placed by an absolute offset would pass a compile
        /// and fail this.
        /// </summary>
        [Test]
        public void EveryShapeLeavesClearAirBetweenItsRows()
        {
            for (int i = 0; i < 8; i++)
            {
                bool canRetry = (i & 1) != 0, watching = (i & 2) != 0, rescuing = (i & 4) != 0;
                var stack = DefeatPanel.Of(canRetry, watching, rescuing);

                float last = DefeatPanel.StackTop;
                string what = $"retry={canRetry} watch={watching} rescue={rescuing}";

                last = Follows(stack.HasRetry, stack.Retry, DefeatPanel.RetryHeight, last, what);
                last = Follows(stack.HasNote, stack.Note, DefeatPanel.NoteHeight, last, what);
                last = Follows(stack.HasWatch, stack.Watch, DefeatPanel.ActionHeight, last, what);
                last = Follows(stack.HasRescue, stack.Rescue, DefeatPanel.ActionHeight, last, what);
                last = Follows(true, stack.Glades, DefeatPanel.GladesHeight, last, what);

                Assert.GreaterOrEqual(stack.Height, last,
                    $"the panel ends before its last row does ({what})");
            }
        }

        /// <summary>One row, checked against the bottom of the one before it.</summary>
        static float Follows(bool drawn, float centre, float height, float after, string what)
        {
            if (!drawn) return after;

            float top = centre - height * .5f;
            Assert.GreaterOrEqual(top, after,
                $"a row begins {after - top} above the bottom of the one before it ({what})");

            return centre + height * .5f;
        }

        /// <summary>
        /// A player who can still play is never sold a way to play.
        ///
        /// Not a layout rule — it is the reason a defeat is not an advertisement, and it is
        /// asserted here because this is the one place that decides whether the controls are
        /// drawn at all.
        /// </summary>
        [Test]
        public void ARetryButtonCrowdsOutBothOffers()
        {
            var stack = DefeatPanel.Of(canRetry: true, watching: true, rescuing: true);

            Assert.IsTrue(stack.HasRetry);
            Assert.IsFalse(stack.HasWatch);
            Assert.IsFalse(stack.HasRescue);
            Assert.IsFalse(stack.HasNote, "there is no wait to explain when there is a heart");
        }

        /// <summary>
        /// The free way back is always above the paid one.
        ///
        /// A panel that puts a price above a video at the moment somebody has just been stopped
        /// from playing is the shape a store reviewer is right to call a dark pattern, and it
        /// is the shape that costs a submission rather than a metric. It is a fact about the
        /// stack rather than about the screen precisely so it can be stated here.
        /// </summary>
        [Test]
        public void TheVideoIsAlwaysAboveThePrice()
        {
            var stack = DefeatPanel.Of(canRetry: false, watching: true, rescuing: true);

            Assert.IsTrue(stack.HasWatch && stack.HasRescue);
            Assert.Less(stack.Watch, stack.Rescue);
        }

        /// <summary>Back to the map is drawn in every shape. It is the exit that always works.</summary>
        [Test]
        public void ThereIsAlwaysAWayBackToTheMap()
        {
            for (int i = 0; i < 8; i++)
            {
                var stack = DefeatPanel.Of((i & 1) != 0, (i & 2) != 0, (i & 4) != 0);
                Assert.Greater(stack.Glades, 0f);
            }
        }

        /// <summary>
        /// The tallest shape still fits the shortest canvas this game is drawn on.
        ///
        /// <para>
        /// <see cref="PanelStack.TallestPanel"/> counts the title ribbon's overhang at
        /// <em>both</em> ends, because a modal is centred: the binding constraint is
        /// <c>H/2 + overhang ≤ canvas/2</c>, and the obvious reading is 87 units too generous.
        /// This is the assertion that fails on a machine rather than on a 4:3 tablet when a
        /// fourth way out is added.
        /// </para>
        /// </summary>
        [Test]
        public void TheTallestShapeFitsATabletHeldInPortrait()
            => Assert.LessOrEqual(DefeatPanel.Tallest, PanelStack.TallestPanel,
                                  $"the defeat panel reaches {DefeatPanel.Tallest} and the " +
                                  $"shortest canvas holds {PanelStack.TallestPanel}");

        /// <summary>
        /// The stack begins clear of the heart row and of the free-glade line that replaces it,
        /// in both the shapes the line can be drawn in.
        /// </summary>
        [Test]
        public void TheStackClearsWhateverIsDrawnAboveIt()
        {
            Assert.GreaterOrEqual(DefeatPanel.StackTop,
                                  DefeatPanel.HeartsCentre + DefeatPanel.HeartsHeight * .5f,
                                  "the heart row is drawn into the buttons");

            foreach (bool close in new[] { false, true })
                Assert.GreaterOrEqual(DefeatPanel.StackTop,
                                      DefeatPanel.FreeCentre(close) + DefeatPanel.FreeHeight * .5f,
                                      $"the free-glade line is drawn into the buttons (close={close})");
        }

        /// <summary>
        /// The free-glade line clears the near-miss line above it when both are drawn.
        ///
        /// The two coexist — a run can be both close and free, and the early glades this line
        /// is written for are exactly where a near miss is most likely — so the line's room
        /// begins under the near-miss slot rather than under the ribbon.
        /// </summary>
        [Test]
        public void TheFreeLineClearsTheNearMissLineAboveIt()
            => Assert.GreaterOrEqual(
                   DefeatPanel.FreeCentre(close: true) - DefeatPanel.FreeHeight * .5f,
                   DefeatPanel.CloseCentre + DefeatPanel.CloseHeight * .5f,
                   "the free-glade line is drawn through the near-miss line");

        /// <summary>
        /// It is centred in the room it has, which is the whole point of deriving it.
        ///
        /// <para>
        /// Stated as the property rather than as the number: the air above the line and the air
        /// below it are the two halves of one gap. A typed centre passes a compile and fails
        /// this, which is what happened — the line shipped 274 down with 74 units of unused
        /// paper over it and 14 under it, and was reported as sitting on the try-again button.
        /// </para>
        /// </summary>
        [Test]
        public void TheFreeLineIsCentredInTheRoomItHas()
        {
            foreach (bool close in new[] { false, true })
            {
                float top = close ? DefeatPanel.CloseCentre + DefeatPanel.CloseHeight * .5f
                                  : DefeatPanel.PaperTop;

                float above = DefeatPanel.FreeCentre(close) - DefeatPanel.FreeHeight * .5f - top;
                float below = DefeatPanel.StackTop
                            - (DefeatPanel.FreeCentre(close) + DefeatPanel.FreeHeight * .5f);

                Assert.AreEqual(above, below, .001f,
                    $"the free-glade line has {above} above it and {below} below (close={close})");
                Assert.Greater(above, 0f, $"there is no air around the free-glade line (close={close})");
            }
        }

        /// <summary>
        /// A run that was not close gives the line more room, not the same room lower down —
        /// the near-miss slot is reserved on every defeat and filled on few, and the empty one
        /// is the void the line used to be pushed under.
        /// </summary>
        [Test]
        public void AnEmptyNearMissSlotIsGivenToTheFreeLine()
            => Assert.Less(DefeatPanel.FreeCentre(close: false),
                           DefeatPanel.FreeCentre(close: true));
    }
}
