using GlimmerGrove.Content;

namespace GlimmerGrove.Layout
{
    /// <summary>
    /// Where the sections of an explanatory panel sit, and how tall the panel has to be to
    /// hold them.
    ///
    /// <para>
    /// <b>Here rather than beside the panel, for <c>ChapterMap</c>'s reason</b> (invariant 8a),
    /// which <c>ReadoutRow</c> and <c>RippleBand</c> have already earned twice: whether two
    /// things on a screen overlap is arithmetic, and arithmetic inside a <c>MonoBehaviour</c>
    /// is arithmetic nothing can check. It became worth separating the moment the section count
    /// stopped being fixed — a height that varies with content is a layout with cases in it,
    /// and a case nobody exercises is a case nobody has looked at.
    /// </para>
    /// <para>
    /// It was worth separating for a second reason: the panel it was lifted out of had a hand
    /// written height that was <b>78 units short</b> of the four sections it was drawing, so
    /// the last paragraph and the close button had been overlapping since the fourth section
    /// was added. Nothing could see it. A compile cannot, a validator cannot, and a screenshot
    /// on the one aspect ratio it was tuned on shows a paragraph that happens to be short
    /// enough in English.
    /// </para>
    /// <para>
    /// Every number is in canvas reference units measured <em>down</em> from the panel's top
    /// edge, which is the direction a panel is read in and the opposite of the sign
    /// <c>UIKit.Box</c> takes — a caller negates once, at the point of placement.
    /// </para>
    /// </summary>
    public static class PanelStack
    {
        // ------------------------------------------------------------ one section
        /// <summary>The glyph's seat: its diameter, and where its centre sits.</summary>
        public const float SeatSize = 100f, SeatCentre = 54f;

        /// <summary>Where the heading's centre sits, and how tall its box is.</summary>
        public const float HeadCentre = 30f, HeadHeight = 42f;

        /// <summary>
        /// Where the paragraph begins, and how tall its box is.
        ///
        /// <para>
        /// The 15 units between the heading's foot and the paragraph's head are deliberately
        /// tighter than the air between sections: a heading and the paragraph under it are one
        /// answer, and spacing them like two would make the panel read as ten things rather
        /// than five. It is also where the room for <see cref="FootGap"/> came from — see there
        /// for why the foot needed it more.
        /// </para>
        /// </summary>
        public const float BodyTop = 66f, BodyHeight = 100f;

        /// <summary>
        /// How wide the panel is, and how much of that the sections are inset by.
        ///
        /// <para>
        /// <b>Width is the only lever this layout has left, which is why it lives here.</b>
        /// The panel is centred and its title ribbon stands proud of the top edge, so the
        /// height is bounded by half the shortest canvas (see <see cref="TightestCanvas"/>) and
        /// five sections spend nearly all of it. A paragraph that needs more room can only get
        /// it sideways. Measured: at 900 wide the longest line on the panel resolved to 22pt in
        /// a five-section box against 25pt in the four-section one it replaced; at 960 it is
        /// within a point of it, and the worst case — a translation half again as long — lands
        /// on exactly the size it always did.
        /// </para>
        /// <para>
        /// 960 is not a new width for this game: the grove's picker already uses it. It leaves
        /// 60 units a side on the 1080 reference width, which <see cref="IsClear"/> checks
        /// rather than trusts.
        /// </para>
        /// </summary>
        public const float Width = 960f, HostInset = 90f;

        /// <summary>
        /// How far from the section's left edge the text column starts, and how wide it is.
        ///
        /// The column clears the seat rather than being centred on the section, so a heading
        /// and its paragraph share one left edge and the glyphs form a column of their own —
        /// which is what lets somebody find the one answer they came for without reading the
        /// panel.
        /// </summary>
        public const float TextLeft = 122f, TextWidth = 700f;

        /// <summary>
        /// The whole of a section, from its top edge to the bottom of its paragraph.
        ///
        /// The paragraph is the deepest thing in it, and it is a fixed box because the text
        /// inside shrinks to fit (<c>UIKit.Shrinkable</c>) rather than growing. That is what
        /// makes this arithmetic true of a translation half again the length of the English,
        /// and it is the property the whole file rests on.
        /// </summary>
        public const float SectionHeight = BodyTop + BodyHeight;

        /// <summary>Clear air between one section's paragraph and the next section's seat.</summary>
        public const float Gap = 30f;

        /// <summary>Top edge to top edge.</summary>
        public const float Pitch = SectionHeight + Gap;

        // ---------------------------------------------------------------- the panel
        /// <summary>Where the first section's top edge sits, clear of the title ribbon.</summary>
        public const float FirstTop = 60f;

        /// <summary>
        /// Clear air between the last paragraph and the button under it.
        ///
        /// <para>
        /// <b>Wider than the gap between sections, and it has to be.</b> Two reasons, both
        /// visible only in a render. A section is followed by another section's glyph, which is
        /// a small disc on a pale ground; the last one is followed by a solid coloured button
        /// the width of the panel, and the same clear air reads as half as much against it. And
        /// <c>UIKit.Shrinkable</c> is Unity's best-fit, which is approximate — a paragraph it
        /// judges to fit can still spill a few units past its box, so the foot has to absorb
        /// what the arithmetic alone would call exact.
        /// </para>
        /// </summary>
        public const float FootGap = 50f;

        /// <summary>The dismissing button: how tall it is, and how far its foot clears the panel.</summary>
        public const float ButtonHeight = 112f, ButtonBottom = 56f;

        /// <summary>
        /// How far the title ribbon stands proud of the panel's top edge.
        ///
        /// <c>ModalView.MakePanel</c> hangs a 130-unit ribbon 22 above the top edge, pivoted at
        /// centre like everything else here — so a panel measured to the canvas without this is
        /// a panel whose title is the first thing off the screen, which is the one part nobody
        /// can do without.
        /// </summary>
        public const float TitleOverhang = 87f;

        /// <summary>
        /// The shortest canvas this game is ever drawn on, in the same reference units.
        ///
        /// <para>
        /// Derived rather than typed, and it has already earned that once. It used to be a 4:3
        /// tablet's <c>1080 x 4/3</c> = 1440, on the reasoning that the scaler matches on
        /// <em>width</em> so the height a device offers is that width times its aspect, and a
        /// tablet is the squarest thing there is. Both halves were true and the answer stopped
        /// being: <see cref="CanvasFit"/> now widens the canvas on anything squarer than a
        /// phone, so <b>no display produces 1440 any more</b> and the tightest is a phone at
        /// <see cref="CanvasFit.PhoneFloor"/>. Left as a literal it would have gone on refusing
        /// panels against a shape nothing is drawn on, which is a stale ceiling in a fit check
        /// — worse than no fit check, because it reads as having been measured.
        /// </para>
        /// </summary>
        public const float TightestCanvas = CanvasFit.ShortestCanvas;

        /// <summary>
        /// The tallest a panel may be, title and all.
        ///
        /// <para>
        /// <b>Twice the overhang, not once, and the difference is 87 units of budget that do
        /// not exist.</b> A modal is centred on the canvas, so its top edge sits half its own
        /// height above the middle and the ribbon another <see cref="TitleOverhang"/> above
        /// that: the binding constraint is <c>H/2 + overhang ≤ canvas/2</c>. Checking
        /// <c>H + overhang ≤ canvas</c> instead — the obvious reading, and the one written
        /// first — passes panels whose title is drawn off the top of a tablet, because it
        /// silently spends the clear air under the panel on a problem that is entirely above
        /// it.
        /// </para>
        /// </summary>
        public const float TallestPanel = TightestCanvas - 2f * TitleOverhang;

        /// <summary>Where a section's top edge sits, counting down from the panel's top.</summary>
        public static float TopOf(int row) => FirstTop + row * Pitch;

        /// <summary>
        /// How tall a panel holding this many sections and one button has to be.
        ///
        /// Derived rather than authored, because the alternative is a number somebody types
        /// once and never revisits when a section is added — which is exactly what this
        /// replaced. Nought sections still leaves room for the button.
        /// </summary>
        public static float HeightFor(int sections)
        {
            float body = sections > 0 ? TopOf(sections - 1) + SectionHeight + FootGap : FirstTop;
            return body + ButtonHeight + ButtonBottom;
        }

        /// <summary>
        /// The button's centre, measured up from the panel's <em>bottom</em> edge, which is
        /// where it is anchored.
        /// </summary>
        public const float ButtonCentre = ButtonBottom + ButtonHeight * .5f;

        /// <summary>
        /// Whether a panel of this many sections leaves clear air everywhere and still fits the
        /// shortest canvas, title and all.
        ///
        /// <paramref name="fault"/> names what went wrong, so a failure reads as an instruction
        /// rather than as a boolean.
        ///
        /// <para>
        /// It answers about the <em>count</em>. That sections clear each other at all is a fact
        /// about the constants above rather than about any caller, so it is asserted by
        /// <c>PanelStackTests</c> instead — a comparison of two compile-time constants folds to
        /// a literal here and the compiler rightly calls the other branch unreachable.
        /// </para>
        /// </summary>
        public static bool IsClear(int sections, out string fault)
        {
            fault = null;

            if (sections < 1)
            {
                fault = $"a panel of {sections} sections has nothing to say";
                return false;
            }

            float height = HeightFor(sections);
            float lastBottom = TopOf(sections - 1) + SectionHeight;
            float buttonTop = height - ButtonBottom - ButtonHeight;

            if (buttonTop < lastBottom)
            {
                fault = $"the last paragraph ends {lastBottom} down and the button begins " +
                        $"{buttonTop} down, so the two overlap by {lastBottom - buttonTop}";
                return false;
            }

            if (height > TallestPanel)
            {
                fault = $"{sections} sections need a panel {height} tall, which is " +
                        $"{height - TallestPanel} more than the shortest canvas holds once the " +
                        $"title's {TitleOverhang} of overhang is counted at both ends " +
                        $"(the tallest a centred panel may be is {TallestPanel})";
                return false;
            }

            if (TextLeft + TextWidth > Width - HostInset)
            {
                fault = $"the text column runs to {TextLeft + TextWidth} inside a section only " +
                        $"{Width - HostInset} wide";
                return false;
            }

            if (Width > ChapterMap.Width)
            {
                fault = $"a panel {Width} wide does not fit the {ChapterMap.Width} reference width";
                return false;
            }

            return true;
        }

        /// <summary>
        /// The most sections this shape can hold before the shortest canvas refuses them.
        ///
        /// Not a limit anybody hits today — it is what a test asserts against, so that adding
        /// the section that would not fit fails on a machine rather than on a tablet.
        /// </summary>
        public static int Most
        {
            get
            {
                int n = 0;
                while (IsClear(n + 1, out _)) n++;
                return n;
            }
        }
    }
}
