using System;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>Terse builders for the whole UI. Everything is uGUI, built in code.</summary>
    public static class UIKit
    {
        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            return rt;
        }

        /// <summary>Free-floating box anchored to a point of the parent.</summary>
        public static RectTransform Box(string name, Transform parent, Vector2 size,
                                        Vector2 anchor, Vector2 pos)
        {
            var rt = Node(name, parent);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(.5f, .5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        /// <summary>
        /// The <c>pos</c> for a box tucked into a corner of its parent with an even margin.
        ///
        /// <para>
        /// <see cref="Box"/> <b>always</b> pivots at centre, so anchoring something to a corner
        /// and passing the margin directly puts half of it off the screen — the trap the win
        /// panel's two lines already fell into once, and which shipped again on the Grovement's
        /// shop button and the shop's own coin pill. Half a control hanging past the right edge
        /// is not a rounding error; the label is simply cut in two.
        /// </para>
        /// <para>
        /// <paramref name="anchor"/> is the corner the box is anchored to, so (1,1) is top
        /// right. The returned offset moves it inward by the margin plus half its own size on
        /// whichever axes that corner pins.
        /// </para>
        /// </summary>
        public static Vector2 Corner(Vector2 size, Vector2 anchor, Vector2 margin)
        {
            float x = anchor.x >= .5f ? -(margin.x + size.x * .5f) : (margin.x + size.x * .5f);
            float y = anchor.y >= .5f ? -(margin.y + size.y * .5f) : (margin.y + size.y * .5f);
            return new Vector2(x, y);
        }

        public static Vector2 Corner(Vector2 size, Vector2 anchor, float marginX, float marginY)
            => Corner(size, anchor, new Vector2(marginX, marginY));

        public static Image Img(string name, Transform parent, Sprite sprite, Color colour,
                                Vector2 size = default, Vector2 anchor = default, Vector2 pos = default)
        {
            RectTransform rt;
            if (size == default && anchor == default) rt = Node(name, parent);
            else rt = Box(name, parent, size, anchor == default ? new Vector2(.5f, .5f) : anchor, pos);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = colour;
            img.raycastTarget = false;
            if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
            return img;
        }

        public static Image Fill(Transform parent, Color colour, string name = "Fill")
        {
            var img = Img(name, parent, Art.Pixel, colour);
            return img;
        }

        /// <summary>
        /// A single line of text by default: <paramref name="wrap"/> is off because most
        /// labels here are chrome — counts, captions, headings — sized to their box, and
        /// for those wrapping is worse than spilling. A coin count that folds onto two
        /// lines breaks the HUD; one that overhangs by a few pixels does not.
        ///
        /// <para>
        /// Anything the player reads as a <i>sentence</i> must pass <c>wrap: true</c>.
        /// Without it the string renders as one unbroken line and simply leaves the
        /// screen — there is no clipping to hint that text is missing, which is how
        /// <c>ui.account.guest_body</c> shipped unreadable.
        /// </para>
        /// </summary>
        public static Text Label(string name, Transform parent, string text, int size, Color colour,
                                 TextAnchor anchor = TextAnchor.MiddleCenter,
                                 Vector2 boxSize = default, Vector2 anchorPt = default, Vector2 pos = default,
                                 FontStyle style = FontStyle.Normal, bool wrap = false)
        {
            RectTransform rt;
            if (boxSize == default && anchorPt == default) rt = Node(name, parent);
            else rt = Box(name, parent, boxSize, anchorPt == default ? new Vector2(.5f, .5f) : anchorPt, pos);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Art.Font;
            t.text = text;
            t.fontSize = size;
            t.color = colour;
            t.alignment = anchor;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            return t;
        }

        /// <summary>Chunky readable text: dark outline plus a drop shadow.</summary>
        public static Text Titled(string name, Transform parent, string text, int size, Color colour,
                                  TextAnchor anchor = TextAnchor.MiddleCenter,
                                  Vector2 boxSize = default, Vector2 anchorPt = default, Vector2 pos = default,
                                  float outline = 4f, float shadow = 5f, bool wrap = false)
        {
            var t = Label(name, parent, text, size, colour, anchor, boxSize, anchorPt, pos, wrap: wrap);
            if (outline > 0f)
            {
                var o = t.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0.09f, 0.14f, 0.20f, .95f);
                o.effectDistance = new Vector2(outline, outline);
                o.useGraphicAlpha = true;
            }
            if (shadow > 0f)
            {
                var s = t.gameObject.AddComponent<Shadow>();
                s.effectColor = new Color(0f, 0f, 0f, .35f);
                s.effectDistance = new Vector2(0f, -shadow);
            }
            return t;
        }

        // ------------------------------------------------------------- buttons
        public static Btn Button(string name, Transform parent, Sprite skin, Vector2 size,
                                 Vector2 anchor, Vector2 pos, Action onClick)
        {
            var rt = Box(name, parent, size, anchor, pos);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = skin;
            img.color = Color.white;
            img.raycastTarget = true;
            if (skin != null && skin.border != Vector4.zero) img.type = Image.Type.Sliced;
            var btn = rt.gameObject.AddComponent<Btn>();
            btn.Setup(onClick);
            return btn;
        }

        /// <summary>
        /// The jelly button art carries a moulded base below its lit face, so the face
        /// centre sits above the middle of the sprite. Labels and glyphs are lifted by
        /// this fraction of the button height to land optically centred at any size.
        /// Measured from the sprites: 8.8% for the pills, 8.1% for the squares.
        /// </summary>
        public const float PillFaceLift = 0.088f;
        public const float SquareFaceLift = 0.081f;

        /// <summary>
        /// The map node is a disc seen at an angle: its white face sits in the upper
        /// half of the sprite and the rim and base fill the bottom third. A glyph
        /// centred on the sprite lands on the rim, not the face. Measured the same way
        /// as the two above, from the white face of <c>node_open</c> and of every
        /// <c>node_s*</c> skin — they share one disc, so a single number covers all of
        /// them and a glade cannot drift as it earns stars.
        /// </summary>
        public const float NodeFaceLift = 0.165f;

        /// <summary>
        /// Pill button with a label, and optionally a glyph in front of it. Returns the
        /// Btn, carrying its label as <see cref="Btn.Label"/> and its glyph as
        /// <see cref="Btn.Icon"/> — so a disabled button dims both together, and a repaint
        /// goes through <see cref="Btn.SetCaption"/> rather than hunting for a child.
        /// </summary>
        /// <remarks>
        /// The glyph and the label are centred as one block rather than placed at fixed
        /// offsets, which is what keeps "▶ WATCH" and "▶ FINDING A VIDEO..." both looking
        /// deliberate on the same button. A layout group would do the same work, but only
        /// through a forced rebuild on every frame a countdown ticks; measuring once per
        /// changed caption is the same result for none of the cost.
        /// </remarks>
        public static Btn TextButton(string name, Transform parent, string skin, string text, int fontSize,
                                     Vector2 size, Vector2 anchor, Vector2 pos, Action onClick,
                                     string icon = null)
        {
            var b = Button(name, parent, Art.S("Ui/" + skin), size, anchor, pos, onClick);

            float lift = size.y * PillFaceLift;

            b.Label = Titled("Text", b.transform, text, fontSize, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(size.x - 40f, size.y * .72f), new Vector2(.5f, .5f),
                             new Vector2(0f, lift));
            b.LabelWidth = size.x - 40f;

            if (string.IsNullOrEmpty(icon)) return b;

            var glyph = Img("Icon", b.transform, Art.S("Ui/" + icon), Pal.Cream,
                            Vector2.one * (size.y * .34f), new Vector2(.5f, .5f), new Vector2(0f, lift));
            glyph.preserveAspect = true;
            b.Icon = glyph;

            // Best-fit, and only on a button that carries a glyph. The block below is
            // measured from the label's box, so a caption that overflowed its box would be
            // drawn wider than the width the glyph was placed against and the pair would
            // read as off-centre. Shrinking to fit keeps the drawn text inside what was
            // measured. Short captions reach neither case — best-fit never grows text past
            // the size it was asked for, so "WATCH" renders exactly as it did.
            Shrinkable(b.Label, Mathf.Max(1, fontSize / 2));

            FitLabel(b);
            return b;
        }

        /// <summary>Gap between a pill button's glyph and its label.</summary>
        const float GlyphGap = 18f;

        /// <summary>
        /// Re-centres a pill button's glyph and label as one block, after either has
        /// changed. A no-op on a button with no glyph, so a paint path can call it
        /// unconditionally — though <see cref="Btn.SetCaption"/> is what should be calling
        /// it, so that nothing has to remember to.
        /// </summary>
        /// <remarks>
        /// <see cref="Text.preferredWidth"/> is read directly rather than through a layout
        /// pass: uGUI computes it from the font's cached character info on demand, so it is
        /// correct in the same frame the caption was assigned. It is then clamped to the
        /// width the button was built with, so a long translation eats into the gap instead
        /// of pushing the glyph off the button.
        /// </remarks>
        public static void FitLabel(Btn button)
        {
            if (button == null || button.Icon == null || button.Label == null) return;

            var glyphRt = (RectTransform)button.Icon.transform;
            var labelRt = button.Label.rectTransform;

            float glyph = glyphRt.sizeDelta.x;
            float room = Mathf.Max(0f, button.LabelWidth - glyph - GlyphGap);
            float text = Mathf.Min(button.Label.preferredWidth, room);

            labelRt.sizeDelta = new Vector2(text, labelRt.sizeDelta.y);

            float block = glyph + GlyphGap + text;
            glyphRt.anchoredPosition = new Vector2(-block * .5f + glyph * .5f, glyphRt.anchoredPosition.y);
            labelRt.anchoredPosition = new Vector2(block * .5f - text * .5f, labelRt.anchoredPosition.y);
        }

        /// <summary>Square button carrying a white glyph.</summary>
        public static Btn IconButton(string name, Transform parent, string skin, string icon,
                                     Vector2 size, Vector2 anchor, Vector2 pos, Action onClick,
                                     float iconScale = .5f)
        {
            var b = Button(name, parent, Art.S("Ui/" + skin), size, anchor, pos, onClick);
            var ic = Img("Icon", b.transform, Art.S("Ui/" + icon), Pal.Cream,
                         Vector2.one * (Mathf.Min(size.x, size.y) * iconScale), new Vector2(.5f, .5f),
                         new Vector2(0f, size.y * SquareFaceLift));
            ic.preserveAspect = true;
            b.Icon = ic;
            return b;
        }

        // ------------------------------------------------------------ trimmings
        /// <summary>Soft coloured halo behind something bright.</summary>
        public static Image Halo(Transform parent, Color colour, float size, float alpha = .55f,
                                 Vector2 pos = default)
        {
            var img = Img("Halo", parent, Art.Glow(128, 2.1f), Pal.A(colour, alpha),
                          Vector2.one * size, new Vector2(.5f, .5f), pos);
            img.transform.SetAsFirstSibling();
            return img;
        }

        public static RectTransform Row(string name, Transform parent, Vector2 size, Vector2 anchor,
                                        Vector2 pos, float spacing, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var rt = Box(name, parent, size, anchor, pos);
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.childAlignment = align;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
            g.childControlWidth = false;
            g.childControlHeight = false;
            return rt;
        }

        /// <summary>
        /// Lets a label shrink to fit its box instead of spilling out of it.
        ///
        /// <para>
        /// The default here is <see cref="HorizontalWrapMode.Overflow"/>, which is right
        /// for chrome sized to its content and wrong for anything holding a translated
        /// string: German is routinely half again the length of English, and overflow has
        /// no clipping to hint that text has left the panel. Best-fit caps at the size the
        /// caller asked for, so nothing ever grows — a short string looks exactly as it
        /// did, and only a long one gets smaller.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <b>Wrapping is switched on here, and has to be.</b> uGUI's best-fit only shrinks
        /// text that fails to fit <em>vertically</em>; with <see cref="HorizontalWrapMode.Overflow"/>
        /// a single line can never fail that test, so best-fit measured a box the text was
        /// already inside and did nothing at all. That is why the streak page's state line
        /// ran out of its pill while being marked shrinkable. With wrapping on, an over-long
        /// line folds, the fold overflows vertically, and best-fit then does the shrinking
        /// it was asked for — so in practice the text shrinks first and only folds once it
        /// has hit <paramref name="minSize"/>. Short chrome — a coin count, a day number —
        /// reaches neither case and is unaffected.
        /// </remarks>
        public static Text Shrinkable(Text text, int minSize = 16)
        {
            if (text == null) return null;

            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.resizeTextMaxSize = text.fontSize;
            text.resizeTextMinSize = Mathf.Clamp(minSize, 1, text.fontSize);
            text.resizeTextForBestFit = true;
            return text;
        }

        public static CanvasGroup Group(RectTransform rt)
        {
            var cg = rt.GetComponent<CanvasGroup>();
            if (cg == null) cg = rt.gameObject.AddComponent<CanvasGroup>();
            return cg;
        }

        /// <summary>Full screen click blocker, used behind modal panels.</summary>
        public static Image Scrim(Transform parent, float alpha = .68f, Action onClick = null)
        {
            var img = Img("Scrim", parent, Art.Pixel, new Color(0.04f, 0.07f, 0.10f, 0f));
            img.raycastTarget = true;
            Tween.Fade(img, alpha, .25f);
            if (onClick != null) img.gameObject.AddComponent<Btn>().Setup(onClick, silent: true);
            return img;
        }

        public static void StretchTo(RectTransform rt, float l, float b, float r, float t)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }
    }
}
