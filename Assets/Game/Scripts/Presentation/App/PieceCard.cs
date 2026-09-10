using System;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The card a grove piece is drawn in, wherever it is being browsed.
    ///
    /// <para>
    /// <b>Two screens offer the same catalog and one of them was drawn twice.</b>
    /// <c>HomesteadShopScreen</c> sells a piece and <c>HomesteadPickerOverlay</c> stands one on
    /// a tile, and until this existed the picker carried its own chrome — a drawn rounded box
    /// with a traced outline over it, which is the shape this UI used before it had a kit. So a
    /// player walked from a shelf of kit cards straight into a grid of something else, one tap
    /// apart, and the two could only be brought back together by hand. Reported from a device
    /// as exactly that.
    /// </para>
    /// <para>
    /// <b>It is a builder rather than a table of numbers, and that is the point.</b> A table
    /// says what the card should be and leaves each caller to assemble it; two callers
    /// assembling one design is two designs a week later. What both screens hold now is the
    /// object this hands back — the plate, the picture, the name and the one line under it —
    /// and everything a screen has of its own (a padlock, a price, a "take it away" cross)
    /// goes on top of that.
    /// </para>
    /// <para>
    /// <b>One design, drawn at whatever size the screen has room for.</b> The numbers are the
    /// shop's, because that is where the card was tuned; every one of them is scaled by
    /// <see cref="ScaleFor"/>, so a panel three across a 960-wide plate gets the same card the
    /// shop draws three across the display. A caller passing <see cref="DesignSize"/> gets the
    /// design untouched.
    /// </para>
    /// </summary>
    public static class PieceCard
    {
        /// <summary>The cell size the card was designed at: the shop's grid, three across.</summary>
        public const float DesignSize = 344f;

        /// <summary>
        /// The corner the plate is drawn with, for anything a caller has to lay over it.
        ///
        /// The plate is <c>Skins.Card</c> and carries its own keyline, so this is <em>not</em>
        /// for tracing a rim round it — see <c>HomesteadShopScreen.ShopCell</c> for the tinted
        /// box and traced outline this kit replaced. It is here so a veil covering the plate
        /// has the same corner the plate does.
        /// </summary>
        public const int Radius = 30;

        /// <summary>How far this card is from the one it was designed as.</summary>
        public static float ScaleFor(float cell) => cell / DesignSize;

        /// <summary>
        /// The invisible rectangle that takes the tap, and the cell's root.
        ///
        /// <para>
        /// A transparent <c>Image</c> rather than the plate itself, because the plate is
        /// inset — a thumb landing in the gutter between two cards would otherwise hit
        /// neither, which reads as a grid that ignores you rather than as a near miss.
        /// </para>
        /// </summary>
        public static Btn Touch(string name, RectTransform parent, float cell, Action onTap)
        {
            float k = ScaleFor(cell);

            var button = UIKit.Button(name, parent, Art.Pixel,
                                      new Vector2(cell - 16f * k, cell - 20f * k),
                                      new Vector2(.5f, 1f), Vector2.zero, onTap);

            button.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            return button;
        }

        /// <summary>
        /// The plate itself: the kit's card, sunk into the screen, drawn at white.
        ///
        /// <para>
        /// It carries its own keyline and its own face, so there is nothing to trace and
        /// nothing to tint — which is what lets a cell say which state it is in with a padlock,
        /// a badge and a caption rather than with a colour that has to be legible against
        /// forty different pictures.
        /// </para>
        /// </summary>
        public static Image Plate(RectTransform root, float cell)
        {
            float k = ScaleFor(cell);

            return UIKit.Img("Plate", root, Art.S("Ui/" + Skins.Card), Color.white,
                             new Vector2(cell - 28f * k, cell - 34f * k),
                             new Vector2(.5f, .5f), Vector2.zero);
        }

        /// <summary>
        /// Where a picture's centre sits inside the plate, and how big the box is.
        ///
        /// <para>
        /// <b>Computed rather than typed.</b> The art used to be pinned a fixed distance from
        /// the top of the plate, which left it riding high in a box whose real bounds are the
        /// plate's top edge and the caption's — so every cell had a band of empty plate under
        /// the picture and none above it. This is the middle of the space the labels actually
        /// leave, so a change to either label moves the picture with it instead of quietly
        /// unbalancing the card.
        /// </para>
        /// </summary>
        public static float ArtCentre(float cell)
        {
            float k = ScaleFor(cell);
            float plateH = cell - 34f * k;
            float captionTop = (82f + 42f * .5f) * k;

            return -(plateH - (captionTop + plateH) * .5f);
        }

        /// <summary>How wide the picture is drawn. A square, whatever the art's own shape.</summary>
        public static float ArtBox(float cell) => 196f * ScaleFor(cell);

        /// <summary>
        /// The picture. Left with no sprite, because what goes on it is the caller's — the
        /// shop's thumbnail, the picker's, or nothing at all while the atlas is still arriving.
        ///
        /// An <c>Image</c> with no sprite is a solid white rectangle rather than a blank
        /// (invariant 7b), so both painters hide it until they have something to draw.
        /// </summary>
        public static Image Picture(Image plate, float cell)
        {
            float box = ArtBox(cell);

            var art = UIKit.Img("A", plate.transform, null, Color.white,
                                new Vector2(box, box), new Vector2(.5f, 1f),
                                new Vector2(0f, ArtCentre(cell)));

            art.preserveAspect = true;
            art.raycastTarget = false;
            return art;
        }

        /// <summary>
        /// A glyph standing exactly where the picture does and at a fraction of its size: the
        /// shop's padlock, the picker's "take it away" cross.
        ///
        /// Here rather than measured at each call site because the two are the same statement —
        /// "this cell is not a picture of a thing" — and a glyph half a card away from where
        /// the picture would have been is a card that looks broken rather than empty.
        /// </summary>
        public static Image Glyph(string name, Image plate, float cell, Sprite sprite,
                                  Color colour, float ofArt)
        {
            float box = ArtBox(cell) * ofArt;

            var glyph = UIKit.Img(name, plate.transform, sprite, colour,
                                  new Vector2(box, box), new Vector2(.5f, 1f),
                                  new Vector2(0f, ArtCentre(cell)));

            glyph.preserveAspect = true;
            glyph.raycastTarget = false;
            return glyph;
        }

        /// <summary>What the thing is called. Shrinks rather than clipping — every name is a loc key.</summary>
        public static Text Name(Image plate, float cell)
        {
            float k = ScaleFor(cell);

            return UIKit.Shrinkable(
                UIKit.Titled("N", plate.transform, string.Empty, Pt(30f * k), Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(cell - 60f * k, 42f * k),
                             new Vector2(.5f, 0f), new Vector2(0f, 82f * k), 3f, 3f),
                Pt(17f * k));
        }

        /// <summary>
        /// The one line under the name, and there is exactly one because a cell that stacks a
        /// price over a requirement over a balance is a receipt.
        /// </summary>
        public static Text Line(Image plate, float cell)
        {
            float k = ScaleFor(cell);

            return UIKit.Shrinkable(
                UIKit.Titled("S", plate.transform, string.Empty, Pt(24f * k), Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(cell - 52f * k, 60f * k),
                             new Vector2(.5f, 0f), new Vector2(0f, LineY(cell)), 3f, 0f),
                Pt(16f * k));
        }

        /// <summary>Where the second line sits, for a caller placing something beside it.</summary>
        public static float LineY(float cell) => 34f * ScaleFor(cell);

        /// <summary>
        /// A point size, never below one.
        ///
        /// <c>Text.fontSize</c> is an int and <c>UIKit.Shrinkable</c> clamps its floor against
        /// it, so a card scaled down far enough to round a size to nought would be a label that
        /// draws nothing at all rather than a small one.
        /// </summary>
        static int Pt(float size) => Mathf.Max(1, Mathf.RoundToInt(size));
    }
}
