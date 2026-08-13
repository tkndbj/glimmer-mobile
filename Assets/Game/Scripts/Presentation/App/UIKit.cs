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

        /// <summary>Pill button with a label. Returns the Btn; label is child "Text".</summary>
        public static Btn TextButton(string name, Transform parent, string skin, string text, int fontSize,
                                     Vector2 size, Vector2 anchor, Vector2 pos, Action onClick)
        {
            var b = Button(name, parent, Art.S("Ui/" + skin), size, anchor, pos, onClick);
            Titled("Text", b.transform, text, fontSize, Pal.Cream, TextAnchor.MiddleCenter,
                   new Vector2(size.x - 40f, size.y * .72f), new Vector2(.5f, .5f),
                   new Vector2(0f, size.y * PillFaceLift));
            return b;
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
