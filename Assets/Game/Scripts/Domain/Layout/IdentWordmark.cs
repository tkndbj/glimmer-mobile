namespace GlimmerGrove.Layout
{
    /// <summary>How big the publisher card's wordmark is drawn, and where its rule sits.</summary>
    public readonly struct IdentPlan
    {
        internal IdentPlan(float width, float height, float ruleWidth, float ruleY)
        {
            Width = width;
            Height = height;
            RuleWidth = ruleWidth;
            RuleY = ruleY;
        }

        /// <summary>The mark, drawn at this size, centred on the canvas.</summary>
        public readonly float Width, Height;

        /// <summary>The rule under it: how wide it reaches, and how far below the centre it sits.</summary>
        public readonly float RuleWidth, RuleY;
    }

    /// <summary>
    /// The publisher card's mark: one baked wordmark, and the arithmetic that sizes it.
    ///
    /// <para>
    /// <b>The glyphs are not here and are not in the build either.</b> They are baked from
    /// Orbitron by <c>Tools/make_ident_art.py</c> into a single white-on-transparent PNG, which
    /// the launch screen draws twice — once as the letters, and once as the <c>Mask</c> that
    /// clips a neon sweep to their shapes. A second font in <c>Assets</c> would be a file, a
    /// manifest entry, an Addressables row, an audit and a typeface loaded at runtime, all for
    /// nine letters that never change.
    /// </para>
    /// <para>
    /// <b>The mark's aspect is deliberately not a constant here.</b> It is read off the sprite,
    /// so re-cutting the bake at a different weight or tracking cannot leave a number in this
    /// file quietly describing the previous cut — the class that was here before this one held a
    /// whole stroke alphabet, and every one of those coordinates was a thing that could drift
    /// from what shipped.
    /// </para>
    /// <para>
    /// <b>Here rather than beside the screen</b>, for <see cref="SplashCover"/>'s reason:
    /// whether the mark fits the narrowest canvas this game can be drawn on is arithmetic, and
    /// arithmetic inside a <c>MonoBehaviour</c> is arithmetic nothing can check.
    /// </para>
    /// </summary>
    public static class IdentWordmark
    {
        /// <summary>
        /// How much of the canvas's width the mark takes.
        ///
        /// <para>
        /// A fraction rather than a size, so a tablet's wider canvas draws a proportionally wider
        /// mark instead of the same one stranded in the middle of more room.
        /// </para>
        /// </summary>
        public const float WidthFraction = .58f;

        /// <summary>
        /// The widest the mark is ever drawn, whatever the canvas.
        ///
        /// <para>
        /// The fraction alone would draw it at 1253 units on a 1:1 canvas, where it stops being a
        /// card and becomes a banner. A studio card is a small thing in the middle of a lot of
        /// black; that is most of what makes it read as one.
        /// </para>
        /// </summary>
        public const float MaxWidth = 780f;

        /// <summary>The least air the mark keeps from the sides of the canvas.</summary>
        public const float SideMargin = 112f;

        /// <summary>How far past the mark the rule reaches, and how far below the centre it sits.</summary>
        public const float RuleOverhang = 18f, RuleDrop = 84f;

        /// <summary>
        /// Where the mark goes on a canvas this wide, given the aspect of the sprite that carries
        /// it.
        /// </summary>
        /// <param name="canvasWidth">
        /// Canvas width in reference units: 1080 on a phone, wider on anything squarer — see
        /// <see cref="CanvasFit"/>. A degenerate reading answers zeroes rather than a negative
        /// mark, because a zero-sized screen is reported briefly during a resize and on some
        /// Android devices on the first frame after a rotation.
        /// </param>
        /// <param name="aspect">The sprite's width over its height, read off the sprite.</param>
        public static IdentPlan Fit(float canvasWidth, float aspect)
        {
            if (canvasWidth <= 0f || aspect <= 0f) return default;

            float room = canvasWidth - SideMargin * 2f;
            if (room <= 0f) return default;

            float width = canvasWidth * WidthFraction;
            if (width > room) width = room;
            if (width > MaxWidth) width = MaxWidth;

            return new IdentPlan(width, width / aspect, width + RuleOverhang * 2f, -RuleDrop);
        }
    }
}
