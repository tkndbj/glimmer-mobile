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
        /// The cover's pixel size. It is named by <c>AssetManifest</c>, not here — invariant 7,
        /// which keeps every asset path in one place; this class owns only geometry.
        ///
        /// <para>
        /// <b>Aspect only</b>, so the importer's cap on the texture does not matter and neither
        /// does the source's absolute size: every length below is a multiple of a scale that
        /// divides one of these back out again.
        /// </para>
        /// </summary>
        public const float ArtWidth = 941f, ArtHeight = 1672f;

        /// <summary>
        /// How far down the picture the wordmark's lowest ink reaches, as a fraction of its
        /// height.
        ///
        /// <para>
        /// Measured off the art rather than judged: the lettering plus its dark rim ends at
        /// about 677 of 1672 rows, and the glow and the burst's lower spike below that are not
        /// ink and may be drawn over. <b>Anything that re-cuts or replaces the cover has to
        /// re-measure this</b>, because it is the one number here that a wrong value moves the
        /// bar straight onto the word, on every device at once, with nothing to say so.
        /// </para>
        /// <para>
        /// <b>It stopped being where the bar hangs from and became a ceiling the bar may not
        /// cross</b>, and that is a fact about this cover rather than a change of mind. The
        /// frame before it carried its wordmark in the bottom tenth with clear ground under it,
        /// so "hang the bar under the word" and "put the bar at the foot of the screen" were the
        /// same instruction. This one carries the wordmark across the middle with three turrets
        /// firing below it, so they are opposite instructions — and a bar obeying the first
        /// would be drawn across the muzzle flash of the red turret. See <see cref="Fit"/>.
        /// </para>
        /// </summary>
        public const float WordFootUv = .405f;

        /// <summary>
        /// Where the wordmark's highest ink starts, same space — the top of the dark burst the
        /// lettering is set on, not the top of the letters, because the burst is part of the
        /// mark and a crop through its spikes reads as damage.
        /// </summary>
        public const float WordHeadUv = .210f;

        /// <summary>The middle of the mark, so the two ends cannot be centred on separately.</summary>
        public const float MarkCentreUv = (WordHeadUv + WordFootUv) * .5f;

        /// <summary>
        /// Where the mark's middle wants to sit on the canvas, as a fraction down from the top.
        ///
        /// <para>
        /// <b>This is the number that replaced bottom-alignment, and the cover is why.</b> The
        /// frame before this one put everything the screen was for — its wordmark, and the strip
        /// of ground the bar stood on — inside its bottom tenth, so standing the picture on the
        /// canvas floor and letting the crop come off the top was free: what it ate was sky. This
        /// cover carries its mark across the <em>middle</em>, with three turrets under it and a
        /// quarter of the frame in gems above. Bottom-aligned, a display squarer than about 5:4
        /// crops nearly seventeen hundred units off the top and the crop lands on the logo —
        /// tested, on a 1:1 canvas, which is a foldable opened or a tablet in split view.
        /// </para>
        /// <para>
        /// So the picture is hung on the mark instead and the crop is taken from whichever end
        /// has room, clamped so neither edge can pull away from the canvas. Thirty per cent
        /// rather than half because the art is composed that way: the mark sits above centre with
        /// its subject below it, and centring the mark would push the turrets off the bottom on
        /// exactly the canvases this exists to fix.
        /// </para>
        /// </summary>
        public const float MarkOnCanvas = .30f;

        /// <summary>
        /// The widest the wordmark reaches, ink and dark rim together, as fractions across the
        /// picture. This is the extent the crop may never eat into; see <see cref="WordMargin"/>.
        ///
        /// <para>
        /// <b>One pair rather than two.</b> The frame before this one set its mark on two lines
        /// and kept a second span for the lower one, because the bar sat directly under it and a
        /// bar wider than the word above it reads as a different object. This mark is one line,
        /// so a second pair would be two constants holding the same number — and the day
        /// somebody re-measured one of them the two would quietly disagree about what the
        /// wordmark is.
        /// </para>
        /// </summary>
        public const float WordLeftUv = .085f, WordRightUv = .918f;

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

        /// <summary>How much of the wordmark's width the bar spans, and how tall it is drawn.</summary>
        public const float BarSpan = .68f, BarHeight = 28f;

        /// <summary>
        /// How far the bar stands above the system's bottom inset, over and above
        /// <see cref="Pad"/>.
        ///
        /// <para>
        /// The bottom eighth of this cover is deep shadow and the bar is read against it, so
        /// this is bounded from both ends by the picture rather than by taste: too little and
        /// the bar is jammed into the edge of the display, too much and it climbs out of the
        /// dark band and onto the lit rock the turrets stand on.
        /// </para>
        /// </summary>
        public const float Foot = 60f;

        /// <summary>
        /// The least air the bar will leave under the wordmark.
        ///
        /// <para>
        /// It does not bind on any display this game is drawn on and it is kept anyway, because
        /// it is the only thing standing between a re-cut cover and a loading bar drawn across
        /// the logo. A guard that has stopped firing is not a guard that has stopped being
        /// needed — the cover it was written for is already not the cover it guards.
        /// </para>
        /// </summary>
        public const float MinGap = 14f;

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
        /// The picture is scaled to cover and <em>capped</em> so the wordmark never clips at the
        /// sides. On everything up to about 19.9:9 the cap does not bite and the fit is a plain
        /// cover; past that it holds the zoom and leaves a band of open sky at the top, which
        /// <see cref="SplashPlan.SkyHeight"/> reports and the screen fills with a gradient
        /// matched to the picture's own top edge. That band is at the very top of the tallest
        /// displays there are — under the status bar and the camera — which is why buying the
        /// wordmark with it is a good trade.
        /// </para>
        /// <para>
        /// Up the screen it is hung on the mark rather than stood on the floor — see
        /// <see cref="MarkOnCanvas"/> — and then clamped, so whichever end the crop comes off,
        /// neither edge of the picture can pull away from the canvas and show through.
        /// </para>
        /// <para>
        /// The bar stands at the <em>foot</em> of the canvas — <see cref="Pad"/> and
        /// <see cref="Foot"/> above whatever inset the system has taken — and is then
        /// <em>capped</em> so it can never come closer than <see cref="MinGap"/> to the
        /// lettering. On this cover the cap cannot bite, because the wordmark is across the
        /// middle and the bar is in the bottom tenth; it is what makes the arithmetic survive a
        /// cover whose mark sits lower, and a bar drawn across the logo is the one outcome here
        /// worth giving up the inset for.
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

            // Hung on the mark, then clamped to the canvas. `limit` is how far the picture can
            // travel before one of its own edges comes away from an edge of the canvas, so the
            // clamp is what keeps the cover a cover; inside it, the crop is split between top and
            // bottom in whatever proportion puts the mark where it was composed to be.
            float pictureY;
            if (height >= canvasH)
            {
                float limit = (height - canvasH) * .5f;
                float wanted = canvasH * (.5f - MarkOnCanvas) - height * (.5f - MarkCentreUv);
                pictureY = wanted > limit ? limit : (wanted < -limit ? -limit : wanted);
            }
            else
            {
                // The capped-zoom case: the picture is shorter than the canvas, so there is no
                // crop to place. It stands on the floor and the sky band is the rest.
                pictureY = (height - canvasH) * .5f;
            }

            float skyHeight = System.Math.Max(0f, canvasH - height);

            float wordFoot = pictureY + height * (.5f - WordFootUv);

            float half = BarHeight * .5f;
            float ideal = -canvasH * .5f + safeBottom + Pad + Foot + half;
            float cap = wordFoot - MinGap - half;

            float barY = System.Math.Min(ideal, cap);

            float span = width * (WordRightUv - WordLeftUv) * BarSpan;
            float room = System.Math.Max(MinBarWidth, canvasW - SideMargin * 2f);
            float barWidth = System.Math.Max(MinBarWidth, System.Math.Min(span, room));

            float barX = ((WordLeftUv + WordRightUv) * .5f - .5f) * width;

            return new SplashPlan(width, height, pictureY, barX, barY, barWidth, wordFoot, skyHeight);
        }
    }
}
