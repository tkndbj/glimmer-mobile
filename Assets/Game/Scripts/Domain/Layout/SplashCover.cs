namespace GlimmerGrove.Layout
{
    /// <summary>Where the launch screen's picture and its loading bar sit, in canvas units.</summary>
    public readonly struct SplashPlan
    {
        internal SplashPlan(float width, float height, float pictureY,
                            float barX, float barY, float barWidth, float wordFoot, float skyHeight)
        {
            Width = width;
            Height = height;
            PictureY = pictureY;
            BarX = barX;
            BarY = barY;
            BarWidth = barWidth;
            WordFoot = wordFoot;
            SkyHeight = skyHeight;
        }

        /// <summary>The picture, drawn at this size. Never smaller than the canvas on either axis.</summary>
        public readonly float Width, Height;

        /// <summary>The picture's centre, against the canvas centre. Positive is up.</summary>
        public readonly float PictureY;

        /// <summary>The bar's centre, against the canvas centre.</summary>
        public readonly float BarX, BarY;

        /// <summary>How wide the bar is drawn.</summary>
        public readonly float BarWidth;

        /// <summary>
        /// Where the wordmark's lowest ink lands on this canvas, against its centre.
        ///
        /// Handed back because it is the number every other one here is measured from, and a
        /// caller that wants to hang anything else off the word — a glow, a caption — must
        /// hang it off the same reading rather than a second guess at it.
        /// </summary>
        public readonly float WordFoot;

        /// <summary>
        /// How much open sky stands above the picture, and therefore how much of it the screen
        /// has to draw itself. Zero on every canvas up to about 19.9:9.
        ///
        /// <para>
        /// The band exists because the zoom is capped rather than left to cover (see
        /// <see cref="SplashCover.WordMargin"/>): past that shape a cover fit would shave the
        /// outer letters of the wordmark off. The screen fills it with the picture's own sky
        /// continued rather than with a colour chosen to look like it — see the mirror in
        /// <c>SplashScreen.BuildCover</c>, which is what makes the join exact instead of
        /// nearly right.
        /// </para>
        /// </summary>
        public readonly float SkyHeight;
    }

    /// <summary>
    /// The launch screen is one painted picture with the wordmark baked into it, and a loading
    /// bar under that wordmark. This is where the two are put in the same place on every phone.
    ///
    /// <para>
    /// <b>Here rather than beside the screen, for <c>ChapterMap</c>'s reason</b> (invariant 8a),
    /// which <c>PanelStack</c>, <c>ReadoutRow</c> and <c>RippleBand</c> have already earned:
    /// whether two things on a screen overlap is arithmetic, and arithmetic inside a
    /// <c>MonoBehaviour</c> is arithmetic nothing can check. It earns it harder than any of
    /// them, because the thing the bar must not collide with is <em>painted into a texture</em>
    /// — there is no rect to measure at runtime, no layout to ask, and no way for a compile or
    /// a validator to notice the day somebody re-cuts the art. A number typed by eye against
    /// one phone is wrong on every other one, and wrong invisibly.
    /// </para>
    /// <para>
    /// <b>The picture is cover-fit and bottom-aligned, and both halves are load-bearing.</b>
    /// The canvas is width-matched (see <c>Boot.BuildCanvas</c>), so its height is whatever
    /// the device's aspect makes it — 2400 on a 20:9 phone, and 2160 on anything squarer than
    /// a phone, which <see cref="CanvasFit"/> widens the canvas for instead — and a single
    /// portrait picture cannot be all of those shapes. Cover-fitting keeps it
    /// full-bleed, which is the house rule (letterboxing a painting to dodge a camera is a
    /// worse picture than the camera). Bottom-aligning decides <em>which</em> edge pays for
    /// that: the crop comes off the top, which is sky, because everything the screen is for —
    /// the wordmark, and the band of ground under it the bar stands on — is in the bottom
    /// tenth. Centring the crop instead is the version that reads perfectly on the phone it
    /// was tried on and cuts the word in half on a tablet.
    /// </para>
    /// <para>
    /// <b>The bar is measured against the word, not against the screen.</b> Its width is a
    /// fraction of the wordmark's own width in the picture, so it scales with the crop and
    /// stays visually tied to the thing it sits under; its centre follows the word's centre,
    /// which is not quite the picture's. The one number that is not derived from the art is
    /// how far below the word it hangs, and that is bounded from both sides — see
    /// <see cref="Fit"/>.
    /// </para>
    /// </summary>
    public static class SplashCover
    {
        /// <summary>
        /// The poster and the clip are named by <c>AssetManifest</c>, not here — invariant 7,
        /// which keeps every asset path in one place. This class owns only geometry.
        ///
        /// <para>
        /// The poster is the video's own first frame, which is what makes the handover
        /// invisible: the screen draws the still immediately, the player prepares behind it,
        /// and the frame it starts on is the frame already on screen. It is also the fallback —
        /// a device that cannot decode the clip shows the picture and never says so, which is
        /// the right failure for a launch screen. Both are measured below as one image, because
        /// they are one image.
        /// </para>
        /// </summary>
        /// The source's pixel size — the video's, and the poster's, which are the same frame.
        /// Aspect only, so the importer's cap on the still does not matter.
        /// </summary>
        public const float ArtWidth = 1080f, ArtHeight = 1920f;

        /// <summary>
        /// How far down the picture the wordmark's lowest ink reaches, as a fraction of its
        /// height.
        ///
        /// <para>
        /// Measured off the art rather than judged: the lettering plus its dark rim ends at
        /// 1780 of 1920 rows, and the soft glow below that is not ink and may be drawn over.
        /// <b>Anything that re-cuts or replaces the cover has to re-measure this</b>, because
        /// it is the one number here that a wrong value moves the bar straight onto the word,
        /// on every device at once, with nothing to say so.
        /// </para>
        /// </summary>
        public const float WordFootUv = .930f;

        /// <summary>
        /// The widest the wordmark reaches, ink and dark rim together, as fractions across the
        /// picture — the upper line (GLIMMER). This is the extent the crop may never eat into;
        /// see <see cref="WordMargin"/>.
        /// </summary>
        public const float WordLeftUv = .137f, WordRightUv = .862f;

        /// <summary>
        /// The horizontal span of the lower line (GROOVE), same space. The lower line rather
        /// than the wider one because it is the edge the bar is read against, and a bar wider
        /// than the word directly above it reads as a different object.
        /// </summary>
        public const float LowerLeftUv = .198f, LowerRightUv = .799f;

        /// <summary>
        /// The least clear air the wordmark keeps from the side of the screen.
        ///
        /// <para>
        /// A cover fit on a canvas taller than the art zooms until it fills, and the wordmark
        /// is four fifths of the picture's width — so on the tallest phones a pure cover shaves
        /// the outer letters' rims off. That is the one crop nobody would accept, because it is
        /// the brand, so the zoom is capped here instead and the sky is extended to make up the
        /// difference. See <see cref="SplashPlan.SkyHeight"/>.
        /// </para>
        /// </summary>
        public const float WordMargin = 12f;

        /// <summary>How much of the lower line's width the bar spans, and how tall it is drawn.</summary>
        public const float BarSpan = .86f, BarHeight = 28f;

        /// <summary>
        /// The air between the word's foot and the bar's top: what it wants, and the least it
        /// will accept.
        ///
        /// <para>
        /// Two numbers rather than one because the band under the word is only six per cent of
        /// the picture, and on a display whose system chrome eats a bottom inset there is not
        /// always room for the gap the design wants. <see cref="Gap"/> is what it takes when it
        /// can; <see cref="MinGap"/> is the point past which the bar would be sitting on the
        /// lettering, which is the one outcome worth giving up the inset for.
        /// </para>
        /// </summary>
        public const float Gap = 34f, MinGap = 14f;

        /// <summary>Air between the bar's foot and the system's bottom inset.</summary>
        public const float Pad = 10f;

        /// <summary>How close to the canvas edge the bar may reach.</summary>
        public const float SideMargin = 90f;

        /// <summary>The narrowest a bar may be drawn, whatever the crop does.</summary>
        public const float MinBarWidth = 240f;

        /// <summary>
        /// Where everything goes on a canvas this size.
        /// </summary>
        /// <param name="canvasW">
        /// Canvas width in reference units: 1080 on a phone, and wider on a display
        /// <see cref="CanvasFit"/> has widened the canvas for. Everything below is a function of
        /// it rather than of the constant, which is why a tablet needed no change here.
        /// </param>
        /// <param name="canvasH">Canvas height in reference units, which varies with the device.</param>
        /// <param name="safeBottom">
        /// The bottom inset the system has taken, in canvas units — a home indicator, a gesture
        /// bar. Zero on most displays. See <c>SafeArea</c>.
        /// </param>
        /// <remarks>
        /// <para>
        /// The picture is scaled to cover, <em>capped</em> so the wordmark never clips, and
        /// bottom-aligned. On everything up to about 19.9:9 the cap does not bite and the fit
        /// is a plain cover; past that it holds the zoom and leaves a band of open sky at the
        /// top, which <see cref="SplashPlan.SkyHeight"/> reports and the screen fills with a
        /// gradient matched to the picture's own top edge. That band is at the very top of the
        /// tallest displays there are — under the status bar and the camera — which is why
        /// buying the wordmark with it is a good trade.
        /// </para>
        /// <para>
        /// The bar's height is settled by three claims in order, and the order is the substance.
        /// It wants to sit <see cref="Gap"/> under the word. It is then <em>raised</em> to clear
        /// the system inset if it can. And it is finally <em>capped</em> so it can never come
        /// closer than <see cref="MinGap"/> to the lettering — because on a short canvas with a
        /// large inset those two wants are not both satisfiable, and the honest answer is to
        /// give up the inset rather than the word. A bar drawn a few units into a home indicator
        /// is a decoration behind a translucent pill; a bar drawn across the logo is a bug.
        /// </para>
        /// </remarks>
        public static SplashPlan Fit(float canvasW, float canvasH, float safeBottom)
        {
            if (canvasW <= 0f || canvasH <= 0f) return default;
            if (safeBottom < 0f) safeBottom = 0f;

            // Cover, capped so the wordmark keeps its margin, and never below the scale that
            // fills the width — a picture narrower than the screen would show the canvas
            // through the sides, which no amount of sky can stand in for.
            float fill = canvasW / ArtWidth;
            float cover = System.Math.Max(fill, canvasH / ArtHeight);
            float clip = (canvasW - WordMargin * 2f) / (ArtWidth * (WordRightUv - WordLeftUv));

            float scale = System.Math.Max(fill, System.Math.Min(cover, clip));
            float width = ArtWidth * scale, height = ArtHeight * scale;

            // Bottom-aligned: the picture's foot meets the canvas's, so the crop comes off the
            // top — sky, rather than the word and the ground it stands on. Negative when the
            // cap has bitten, which is the case the sky band exists for.
            float pictureY = (height - canvasH) * .5f;
            float skyHeight = System.Math.Max(0f, canvasH - height);

            float wordFoot = pictureY + height * (.5f - WordFootUv);

            float half = BarHeight * .5f;
            float ideal = wordFoot - Gap - half;
            float floor = -canvasH * .5f + safeBottom + Pad + half;
            float cap = wordFoot - MinGap - half;

            float barY = System.Math.Min(System.Math.Max(ideal, floor), cap);

            float span = width * (LowerRightUv - LowerLeftUv) * BarSpan;
            float room = System.Math.Max(MinBarWidth, canvasW - SideMargin * 2f);
            float barWidth = System.Math.Max(MinBarWidth, System.Math.Min(span, room));

            float barX = ((LowerLeftUv + LowerRightUv) * .5f - .5f) * width;

            return new SplashPlan(width, height, pictureY, barX, barY, barWidth, wordFoot, skyHeight);
        }
    }
}
