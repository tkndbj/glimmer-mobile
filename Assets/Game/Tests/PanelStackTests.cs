using GlimmerGrove.Layout;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Whether an explanatory panel's sections fit inside it, and inside the screen.
    ///
    /// <para>
    /// The rule this exists to keep is the one the information panel broke silently: its height
    /// was a hand-written number, a fourth section was added, and the last paragraph had been
    /// drawn through the close button ever since. Nothing could catch it — a compile cannot see
    /// a coordinate, a validator does not read Presentation, and a screenshot on one aspect
    /// ratio shows a paragraph that happens to be short enough in English.
    /// </para>
    /// <para>
    /// So the arithmetic moved into Domain, which is <c>ChapterMap</c>'s argument for map nodes
    /// and <c>ReadoutRow</c>'s for a row of numbers, and these are the cases that would have
    /// failed the day it broke.
    /// </para>
    /// </summary>
    public sealed class PanelStackTests
    {
        [Test]
        public void EverySectionCountThePanelCanUseLeavesClearAir()
        {
            for (int n = 1; n <= PanelStack.Most; n++)
                Assert.IsTrue(PanelStack.IsClear(n, out string fault), $"{n} sections: {fault}");
        }

        [Test]
        public void TheHeightGrowsByExactlyOnePitchPerSection()
        {
            // The property that makes a derived height worth having: adding a section costs
            // its own room and nothing else moves.
            for (int n = 1; n < PanelStack.Most; n++)
                Assert.AreEqual(PanelStack.Pitch,
                                PanelStack.HeightFor(n + 1) - PanelStack.HeightFor(n), 0.001f,
                                $"between {n} and {n + 1} sections");
        }

        [Test]
        public void TheLastParagraphNeverReachesTheButton()
        {
            // The failure that shipped, stated directly. It is the pair the old hand-written
            // height got wrong, and it got it wrong by 78 units.
            for (int n = 1; n <= PanelStack.Most; n++)
            {
                float lastBottom = PanelStack.TopOf(n - 1) + PanelStack.SectionHeight;
                float buttonTop = PanelStack.HeightFor(n) - PanelStack.ButtonBottom - PanelStack.ButtonHeight;

                Assert.GreaterOrEqual(buttonTop, lastBottom,
                                      $"{n} sections: the paragraph runs into the button");
                Assert.AreEqual(PanelStack.FootGap, buttonTop - lastBottom, 0.001f,
                                "and the clear air between them is the one that was authored");
            }
        }

        [Test]
        public void ASectionIsPitchedFurtherThanItIsDeep()
        {
            // The fact IsClear cannot assert without the compiler folding it to a literal: a
            // pitch tightened below a section's own depth would draw each paragraph over the
            // seat of the next, and every other case here would still pass.
            Assert.Greater(PanelStack.Pitch, PanelStack.SectionHeight);
            Assert.Greater(PanelStack.Gap, 0f);
            Assert.Greater(PanelStack.FootGap, 0f);
        }

        [Test]
        public void SectionsDoNotOverlapEachOther()
        {
            for (int n = 0; n < PanelStack.Most - 1; n++)
                Assert.AreEqual(PanelStack.Gap,
                                PanelStack.TopOf(n + 1) - (PanelStack.TopOf(n) + PanelStack.SectionHeight),
                                0.001f, $"between section {n} and {n + 1}");
        }

        /// <summary>
        /// The panel is drawn with its title standing proud of it, and the canvas matches on
        /// width — so a 4:3 tablet in portrait is the shortest screen this game is ever drawn
        /// on and the only one worth measuring against.
        /// </summary>
        [Test]
        public void TheTallestPanelStillFitsTheShortestScreenTitleAndAll()
        {
            Assert.LessOrEqual(PanelStack.HeightFor(PanelStack.Most), PanelStack.TallestPanel);
            Assert.IsFalse(PanelStack.IsClear(PanelStack.Most + 1, out string fault),
                           "and one more must be refused rather than clipped");
            StringAssert.Contains("canvas", fault);
        }

        /// <summary>
        /// The ceiling counts the title's overhang <b>twice</b>, and the first version of it
        /// counted once — which passes a panel whose title is drawn off the top of a tablet.
        /// </summary>
        [Test]
        public void TheCeilingIsHalfTheCanvasBecauseAPanelIsCentred()
        {
            // A centred panel of height H puts its top edge H/2 above the middle, and the
            // ribbon another overhang above that. So the room is (canvas/2 - overhang) * 2.
            Assert.AreEqual(PanelStack.TightestCanvas / 2f,
                            PanelStack.TallestPanel / 2f + PanelStack.TitleOverhang, 0.001f);

            Assert.Less(PanelStack.TallestPanel, PanelStack.TightestCanvas - PanelStack.TitleOverhang,
                        "counting the overhang once is the mistake this replaced");
        }

        [Test]
        public void TheTextColumnFitsInsideTheSectionItIsDrawnIn()
        {
            Assert.LessOrEqual(PanelStack.TextLeft + PanelStack.TextWidth,
                               PanelStack.Width - PanelStack.HostInset);
            Assert.LessOrEqual(PanelStack.Width, CanvasFit.PhoneWidth,
                               "and the panel inside the narrowest canvas, which is a phone's — "
                               + "a tablet's is wider, never narrower");
        }

        [Test]
        public void TheInformationPanelsFiveSectionsFit()
        {
            // The count the glade information panel reaches when a chapter carries the free
            // opening. Named rather than left implicit, because it is the case that made all
            // of this necessary and the one a sixth section would break.
            Assert.GreaterOrEqual(PanelStack.Most, 5);
            Assert.IsTrue(PanelStack.IsClear(5, out string fault), fault);
        }

        [Test]
        public void APanelOfNothingIsRefusedRatherThanMeasured()
        {
            Assert.IsFalse(PanelStack.IsClear(0, out string fault));
            Assert.IsNotNull(fault);
        }
    }
}
