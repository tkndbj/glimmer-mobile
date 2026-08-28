namespace GlimmerGrove.Layout
{
    /// <summary>
    /// The offer that follows a lost run: how tall it is, and where the word DEFEAT sits above
    /// it.
    ///
    /// <para>
    /// <b>The panel has four heights, which is why this is arithmetic and not a constant.</b> A
    /// player short of gems gets a line telling them so; one with a heart in hand gets a button
    /// to restart instead of paying. Both are optional and they combine, so the panel is one of
    /// four sizes — and the version of this that lived as absolute offsets in the overlay drew
    /// its explanatory note straight through the line above it on exactly one of the branches.
    /// That is the failure <c>PanelStack</c> exists because of, and this is the same rule for a
    /// panel that stacks buttons rather than sections.
    /// </para>
    /// <para>
    /// <b>The banner counts against the panel twice over.</b> A modal is centred, so its top
    /// edge is half its own height above the middle, the title ribbon stands
    /// <c>PanelStack.TitleOverhang</c> above that, and the word stands above the ribbon — so the
    /// binding constraint is <c>H/2 + overhang + gap + banner ≤ canvas/2</c>. The obvious
    /// reading (everything ≤ canvas) is wrong by half the panel and passes layouts whose word is
    /// drawn off the top of a tablet, which is precisely the mistake <c>PanelStack.TallestPanel</c>
    /// records having made.
    /// </para>
    /// </summary>
    public static class ContinuePanel
    {
        // ------------------------------------------------------------------ the sections
        /// <summary>Clear air under the title ribbon before anything is drawn.</summary>
        public const float HeadRoom = 150f;

        /// <summary>
        /// The offer, in one sentence.
        ///
        /// <para>
        /// <b>One row rather than four, and that is the whole of what this panel says.</b> It
        /// was a large figure, a line explaining what buying it did, a line saying what the
        /// player was holding and — sometimes — a line saying it was not enough: four rows to
        /// carry one idea, and reported from play as too much to read at the moment somebody has
        /// just lost. A sentence with the number inside it says the same thing once, and can be
        /// set large enough that it is actually read.
        /// </para>
        /// </summary>
        public const float OfferH = 170f, OfferGap = 18f;

        /// <summary>Shown only when the price cannot be met. The one line that is actionable.</summary>
        public const float ShortH = 58f, ShortGap = 8f;

        /// <summary>The button that spends, or that goes and gets the gems to spend with.</summary>
        public const float ButtonH = 148f, ButtonGap = 16f;

        /// <summary>The free way out, always drawn.</summary>
        public const float GiveUpH = 96f;

        /// <summary>Clear air under the last button.</summary>
        public const float FootRoom = 46f;

        public const float Width = 880f, ContentWidth = 720f;

        // ------------------------------------------------------------------ the word
        /// <summary>How tall the defeat banner is.</summary>
        public const float BannerHeight = 112f;

        /// <summary>Air between the word and the panel's title ribbon.</summary>
        public const float BannerGap = 26f;

        // ------------------------------------------------------------------ its timing
        /// <summary>
        /// How the moment is paced: the word arrives, is allowed to land, rises, and only then
        /// is the question asked underneath it.
        ///
        /// <para>
        /// <b>Sequenced rather than simultaneous, and slower than it first shipped.</b> The two
        /// happening at once reads as a header sliding into place above a form; one after the
        /// other reads as a sentence — <em>this happened, so: this question</em>. The first cut
        /// ran the rise a third of a second after the pop and was reported as too fast to
        /// register, which is the failure a celebration and a defeat share: the beat is the
        /// content, and there is nothing else on screen competing for it.
        /// </para>
        /// </summary>
        public const float BannerPop = .40f, BannerHold = .38f, BannerRise = .50f;

        /// <summary>When the panel may begin arriving: after the word has finished moving.</summary>
        public static float PanelDelay => BannerPop + BannerHold + BannerRise;

        /// <summary>How long the panel takes to arrive once it starts.</summary>
        public const float PanelEnter = .36f;

        /// <summary>
        /// Where the banner rests, measured up from the middle of the screen — clear of the
        /// panel's own top edge and of the ribbon standing proud of it.
        /// </summary>
        public static float BannerCentre(float panelHeight)
            => panelHeight * .5f + PanelStack.TitleOverhang + BannerGap + BannerHeight * .5f;

        /// <summary>Everything the banner adds above the panel's top edge.</summary>
        public static float BannerOverhang
            => PanelStack.TitleOverhang + BannerGap + BannerHeight;

        // ------------------------------------------------------------------ the sum
        /// <summary>
        /// How tall the panel has to be. <paramref name="short"/> is the line shown when the
        /// price cannot be met, and is the only row that comes and goes.
        /// </summary>
        public static float HeightFor(bool @short)
        {
            float y = HeadRoom;

            y += OfferH + OfferGap;
            if (@short) y += ShortH + ShortGap;
            y += ButtonH + ButtonGap;

            return y + GiveUpH + FootRoom;
        }

        /// <summary>The tallest the panel ever is, which is the one that has to fit.</summary>
        public static float Tallest => HeightFor(true);

        /// <summary>
        /// Whether every shape of this panel, banner and all, is drawn on screen on the shortest
        /// canvas this game is ever laid out on.
        /// </summary>
        public static bool IsClear(out string fault)
        {
            float room = PanelStack.TightestCanvas * .5f;
            float needed = Tallest * .5f + BannerOverhang;

            if (needed > room)
            {
                fault = "the defeat banner is drawn " + (needed - room).ToString("0") +
                        " unit(s) off the top of the shortest canvas this game is drawn on";
                return false;
            }

            fault = null;
            return true;
        }
    }
}
