using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The tutorial finale's stack (invariant 53h): the wordmark, the line under it, and the one
    /// key - hung from the top of the safe area and never reaching the ward line.
    ///
    /// <para>
    /// <b>Hung from the top rather than placed about the middle</b>, because the middle of that
    /// screen is the ward line - three turrets, their bars and their crests - and a key drawn
    /// over them reads as something dropped on the board (<c>TutorialScreen</c>'s own note on
    /// the line it replaced). So the stack lives in the band between the top of the safe area
    /// and <see cref="Floor"/> above the middle, is centred in that band, and is
    /// <b>scaled as one</b> when the band is short - a squarer phone draws the same picture
    /// smaller rather than a different picture.
    /// </para>
    /// <para>
    /// Pure arithmetic over the safe area's height and the wordmark's shape, so
    /// <c>FirstSortieTests</c> holds it on every canvas the game can be drawn on and
    /// <c>render_tutorial.py --finale</c> draws the same numbers.
    /// </para>
    /// </summary>
    public readonly struct FinaleStack
    {
        /// <summary>The least air between the top of the safe area and the wordmark.</summary>
        public const float Top = 64f;

        /// <summary>How far above the middle of the safe area the stack must end.</summary>
        public const float Floor = 170f;

        /// <summary>The wordmark's width. Its height is its own aspect's.</summary>
        public const float LogoW = 900f;

        /// <summary>The line's box: room for two lines of the 66-point title.</summary>
        public const float LineW = 880f, LineH = 156f;

        public const float KeyW = 460f, KeyH = 122f;

        /// <summary>The air under the wordmark and over the key.</summary>
        public const float LogoGap = 18f, KeyGap = 40f;

        /// <summary>
        /// The shipped wordmark's width over its height, for a frame on which its sprite has not
        /// arrived: the stack is laid out the same either way. <c>FirstSortieTests</c> reads the
        /// PNG to keep this true.
        /// </summary>
        public const float ShippedAspect = 1400f / 567f;

        public readonly float LogoH;

        /// <summary>The stack's own height, before <see cref="Scale"/>.</summary>
        public readonly float Height;

        /// <summary>How much the stack is drawn at: 1, or less on a short canvas.</summary>
        public readonly float Scale;

        /// <summary>Where the stack's top edge sits, measured down from the top of the safe area.</summary>
        public readonly float StackTop;

        FinaleStack(float logoH, float height, float scale, float stackTop)
        {
            LogoH = logoH;
            Height = height;
            Scale = scale;
            StackTop = stackTop;
        }

        /// <param name="safeHeight">The safe area's height, in canvas units.</param>
        /// <param name="logoAspect">The wordmark's width over its height; anything not positive is the shipped one.</param>
        public static FinaleStack For(float safeHeight, float logoAspect)
        {
            float aspect = logoAspect > 0f ? logoAspect : ShippedAspect;
            float logoH = LogoW / aspect;
            float height = logoH + LogoGap + LineH + KeyGap + KeyH;

            float room = safeHeight * .5f - Floor - Top;
            float scale = room <= 0f ? 0f : Mathf.Min(1f, room / height);

            // Centred in the band it is allowed, so a tall phone does not leave the whole of the
            // spare room under the key and a short one has none to give.
            float spare = Mathf.Max(0f, room - height * scale);

            return new FinaleStack(logoH, height, scale, Top + spare * .5f);
        }

        /// <summary>Centres within the stack, measured down from its top edge, before <see cref="Scale"/>.</summary>
        public float LogoY => LogoH * .5f;
        public float LineY => LogoH + LogoGap + LineH * .5f;
        public float KeyY => LogoH + LogoGap + LineH + KeyGap + KeyH * .5f;

        /// <summary>Where the stack ends, measured down from the top of the safe area.</summary>
        public float Bottom => StackTop + Height * Scale;
    }

    /// <summary>
    /// The first sortie's foot on the loadout (invariant 53h): the guide standing in the
    /// bottom-right corner with his speech bubble to his left, and later the BATTLE key that
    /// replaces them.
    ///
    /// <para>
    /// <b>Laid out against the figure's silhouette rather than its box</b>, which is what lets
    /// the bubble sit close enough to read as his. Sprocket's box is mostly air in its upper
    /// left - he faces left with his blaster low - so the bubble may tuck
    /// <see cref="HeadClear"/> of his width into that air, provided it stays above
    /// <see cref="GunLine"/>; <c>make_sortie_art.py --check</c> proves every frame of both reels
    /// keeps that corner empty, so the two numbers are a fact about the art and not a hope.
    /// </para>
    /// <para>
    /// <b>Coordinates are the safe area's own, origin bottom-left, y up</b>, and every position
    /// is a centre (<c>UIKit.Box</c> pivots at centre). <see cref="Band"/> is how much of the
    /// foot the dock takes, which is what the shelf's viewport is raised by so no cell is ever
    /// left under it.
    /// </para>
    /// </summary>
    public readonly struct GuideDock
    {
        /// <summary>The figure's height. Its width is the art's aspect.</summary>
        public const float FigureH = 300f;

        /// <summary>Air between the figure and the right and bottom edges of the safe area.</summary>
        public const float Margin = 24f, Foot = 18f;

        /// <summary>The share of the figure's width, from its left edge, that is empty above <see cref="GunLine"/>.</summary>
        public const float HeadClear = .22f;

        /// <summary>The height, as a share of the figure's, above which <see cref="HeadClear"/> holds.</summary>
        public const float GunLine = .42f;

        /// <summary>Where the visor is, as a share of the figure's height: what the beak points at.</summary>
        public const float Visor = .56f;

        /// <summary>The bubble's widest, and the least air left of it.</summary>
        public const float BubbleMaxW = 720f, Left = 24f;

        /// <summary>The bubble's stack: top padding, body, gap, key, bottom padding.</summary>
        public const float PadTop = 28f, BodyGap = 18f, ButtonH = 92f, ButtonW = 300f, PadBottom = 24f;

        /// <summary>The body's side padding inside the bubble.</summary>
        public const float BodyInset = 36f;

        /// <summary>The beak's size, and how far its centre stands off the bubble's edge.</summary>
        public const float Beak = 46f, BeakOut = 10f;

        /// <summary>The bubble's corner radius (<c>Art.Round(28)</c>, <c>TipOverlay</c>'s paper).</summary>
        public const float Corner = 28f;

        /// <summary>Air over the tallest thing in the dock, before the shelf starts.</summary>
        public const float Headroom = 20f;

        /// <summary>The BATTLE key: the hub's own size (<c>HubFoot</c>'s plain shape).</summary>
        public const float KeyW = 620f, KeyH = 178f, KeyFoot = 30f;

        /// <summary>The figure's art, width over height. <c>FirstSortieTests</c> reads the PNG.</summary>
        public const float ShippedAspect = 248f / 311f;

        public readonly Vector2 FigureSize, FigureCentre;
        public readonly Vector2 BubbleSize, BubbleCentre;
        public readonly Vector2 BeakCentre;

        /// <summary>How tall the dock is while the guide stands.</summary>
        public readonly float Band;

        GuideDock(Vector2 figureSize, Vector2 figureCentre, Vector2 bubbleSize, Vector2 bubbleCentre,
                  Vector2 beakCentre, float band)
        {
            FigureSize = figureSize;
            FigureCentre = figureCentre;
            BubbleSize = bubbleSize;
            BubbleCentre = bubbleCentre;
            BeakCentre = beakCentre;
            Band = band;
        }

        /// <summary>The body's width for a dock this wide: what the caller measures the sentence against.</summary>
        public static float BodyWidth(float safeWidth, float figureAspect)
            => BubbleWidth(safeWidth, figureAspect) - BodyInset * 2f;

        static float FigureWidth(float figureAspect)
            => FigureH * (figureAspect > 0f ? figureAspect : ShippedAspect);

        static float BubbleRight(float safeWidth, float figureAspect)
        {
            float figureLeft = safeWidth - Margin - FigureWidth(figureAspect);
            return figureLeft + FigureWidth(figureAspect) * HeadClear;
        }

        static float BubbleWidth(float safeWidth, float figureAspect)
            => Mathf.Min(BubbleMaxW, BubbleRight(safeWidth, figureAspect) - Left);

        /// <param name="safeWidth">The safe area's width, in canvas units.</param>
        /// <param name="figureAspect">The figure's width over its height; anything not positive is the shipped one.</param>
        /// <param name="bodyHeight">The sentence's measured height at <see cref="BodyWidth"/>.</param>
        public static GuideDock For(float safeWidth, float figureAspect, float bodyHeight)
        {
            float fw = FigureWidth(figureAspect);
            var figureSize = new Vector2(fw, FigureH);
            var figureCentre = new Vector2(safeWidth - Margin - fw * .5f, Foot + FigureH * .5f);

            float right = BubbleRight(safeWidth, figureAspect);
            float width = BubbleWidth(safeWidth, figureAspect);
            float height = PadTop + Mathf.Max(0f, bodyHeight) + BodyGap + ButtonH + PadBottom;

            // Never below the gun line, so the tuck stays in the air the art leaves. The beak
            // points at the visor, lifted only as far as it takes to stand on the bubble's
            // straight edge rather than on its rounded corner.
            float gun = Foot + FigureH * GunLine;
            float visor = Foot + FigureH * Visor;
            float bottom = Mathf.Max(gun, visor - height + Beak);

            var bubbleSize = new Vector2(width, height);
            var bubbleCentre = new Vector2(right - width * .5f, bottom + height * .5f);
            var beakCentre = new Vector2(right + BeakOut,
                                         Mathf.Clamp(visor, bottom + Corner + Beak * .5f,
                                                     bottom + height - Corner - Beak * .5f));

            float top = Mathf.Max(Foot + FigureH, bottom + height);

            return new GuideDock(figureSize, figureCentre, bubbleSize, bubbleCentre, beakCentre,
                                 top + Headroom);
        }

        /// <summary>The bubble's key, as a centre inside the bubble measured up from its bottom edge.</summary>
        public static float ButtonY => PadBottom + ButtonH * .5f;

        /// <summary>The body's centre inside the bubble, measured down from its top edge.</summary>
        public static float BodyY(float bodyHeight) => PadTop + bodyHeight * .5f;

        /// <summary>The BATTLE key's centre above the safe area's bottom edge.</summary>
        public static float KeyY => KeyFoot + KeyH * .5f;

        /// <summary>How tall the dock is while the BATTLE key stands.</summary>
        public static float KeyBand => KeyFoot + KeyH + Headroom;
    }
}
