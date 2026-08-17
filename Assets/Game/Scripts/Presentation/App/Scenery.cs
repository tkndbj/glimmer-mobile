using System;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>Shared set dressing: parallax skies, vignettes, toasts and star rows.</summary>
    public static class Scenery
    {
        /// <summary>Three painted layers that drift against each other.</summary>
        public static RectTransform Layered(Transform parent, string prefix, float dim = .18f)
        {
            var host = UIKit.Node("Backdrop", parent);
            AddLayer(host, prefix + "_sky", 8f, 1.06f);
            AddLayer(host, prefix + "_ground", 20f, 1.08f);
            AddLayer(host, prefix + "_deco", 38f, 1.11f);

            var shade = UIKit.Img("Shade", host, Art.Pixel, new Color(.04f, .08f, .12f, dim));
            var vig = UIKit.Img("Vignette", host, Art.Vignette(256), new Color(.02f, .05f, .09f, .55f));
            vig.type = Image.Type.Simple;
            return host;
        }

        static void AddLayer(Transform host, string sprite, float strength, float scale)
        {
            var s = Art.S("Bg/" + sprite);
            if (s == null) return;
            var img = UIKit.Img(sprite, host, s, Color.white);
            var rt = (RectTransform)img.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.sizeDelta = new Vector2(s.rect.width, s.rect.height);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one * scale;
            var fit = img.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = s.rect.width / s.rect.height;
            Parallax.Attach(rt, strength);
        }

        /// <summary>Single covering image, used behind the puzzle board.</summary>
        public static RectTransform Cover(Transform parent, string sprite, float dim = 0f, float vignette = .45f)
        {
            var host = UIKit.Node("Backdrop", parent);
            var s = Art.S(sprite.StartsWith("Bg/") ? sprite : "Bg/" + sprite);
            if (s != null)
            {
                var img = UIKit.Img(sprite, host, s, Color.white);
                var rt = (RectTransform)img.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
                rt.anchoredPosition = Vector2.zero;
                var fit = img.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = s.rect.width / s.rect.height;
                rt.localScale = Vector3.one * 1.06f;
                Parallax.Attach(rt, 14f);
            }
            if (dim > 0f) UIKit.Img("Shade", host, Art.Pixel, new Color(.03f, .06f, .09f, dim));
            if (vignette > 0f)
            {
                var vig = UIKit.Img("Vignette", host, Art.Vignette(256), new Color(.01f, .04f, .07f, vignette));
                vig.type = Image.Type.Simple;
            }
            return host;
        }

        /// <summary>Rounded pill with a label, used for counters and headings.</summary>
        public static Text Pill(Transform parent, string text, int fontSize, Vector2 size,
                                Vector2 anchor, Vector2 pos, Color? tint = null, string icon = null)
        {
            var bg = UIKit.Img("Pill", parent, Art.Round(28), tint ?? new Color(.06f, .12f, .17f, .72f),
                               size, anchor, pos);
            var edge = UIKit.Img("Edge", bg.transform, Art.RoundOutline(28, 3f), new Color(1, 1, 1, .13f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            float leftPad = 20f;
            if (icon != null)
            {
                var ic = UIKit.Img("Icon", bg.transform, Art.S("Ui/" + icon), Pal.Cream,
                                   Vector2.one * (size.y * .52f), new Vector2(0f, .5f),
                                   new Vector2(size.y * .42f, 0f));
                ic.preserveAspect = true;
                leftPad = size.y * .82f;
            }
            var t = UIKit.Titled("Text", bg.transform, text, fontSize, Pal.Cream,
                                 icon != null ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter,
                                 outline: 3f, shadow: 3f);
            UIKit.StretchTo((RectTransform)t.transform, leftPad, 0, 16, 4);
            return t;
        }

        /// <summary>Floating message that rises and fades. Sits below the header by default.</summary>
        public static void Toast(Transform parent, string message, Color? tint = null, float hold = 1.9f,
                                 Vector2 anchor = default, float y = 300f)
        {
            if (anchor == default) anchor = new Vector2(.5f, 0f);
            var bg = UIKit.Img("Toast", parent, Art.Round(30), new Color(.05f, .11f, .16f, .0f),
                               new Vector2(920f, 148f), anchor, new Vector2(0f, y - 50f));
            var edge = UIKit.Img("Edge", bg.transform, Art.RoundOutline(30, 3f), new Color(1, 1, 1, 0f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);
            var t = UIKit.Titled("Text", bg.transform, message, 34, tint ?? Pal.Cream, TextAnchor.MiddleCenter,
                                 outline: 3f, shadow: 3f, wrap: true);
            UIKit.StretchTo((RectTransform)t.transform, 30, 8, 30, 12);
            t.color = Pal.A(t.color, 0f);

            var rt = (RectTransform)bg.transform;
            Tween.Tint(bg, new Color(.05f, .11f, .16f, .88f), .3f);
            Tween.Tint(edge, new Color(1, 1, 1, .16f), .3f);
            Tween.Tint(t, tint ?? Pal.Cream, .3f);
            Tween.Move(rt, new Vector2(0f, y), .5f, Ease.OutBack);
            Tween.After(hold, () =>
            {
                if (!bg) return;
                Tween.Tint(bg, new Color(.05f, .11f, .16f, 0f), .45f);
                Tween.Tint(edge, new Color(1, 1, 1, 0f), .45f);
                Tween.Tint(t, Pal.A(tint ?? Pal.Cream, 0f), .45f);
                Tween.Move(rt, new Vector2(0f, y + 60f), .5f, Ease.InQuad).OnDone(() =>
                {
                    if (bg) UnityEngine.Object.Destroy(bg.gameObject);
                });
            });
        }
    }

    /// <summary>Row of three stars that can be filled with a rising fanfare.</summary>
    public sealed class StarRow : MonoBehaviour
    {
        Image[] _full, _empty;
        float _size;

        /// <summary>How far the middle star rides above its neighbours, as a fraction of size.</summary>
        const float ArcLift = .22f;

        public static StarRow Create(Transform parent, Vector2 anchor, Vector2 pos, float size,
                                     float spacing, int filled = 0, bool arc = false)
        {
            // The box has to allow for the arc, and the stars have to be centred inside it.
            // It used to be exactly `size` tall whether or not the middle star was lifted, so
            // an arced row's real extent ran from -size/2 to +size/2 + lift — the group sat
            // half a lift high, and on the victory panel that was enough to push the middle
            // star into the ribbon above it. Rows without an arc are unaffected: the lift is
            // zero and every number below is what it always was.
            float lift = arc ? size * ArcLift : 0f;

            var rt = UIKit.Box("Stars", parent, new Vector2(spacing * 3f, size + lift), anchor, pos);
            var row = rt.gameObject.AddComponent<StarRow>();
            row.Build(size, spacing, filled, arc);
            return row;
        }

        void Build(float size, float spacing, int filled, bool arc)
        {
            _size = size;
            _full = new Image[3];
            _empty = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                float lift = arc ? size * ArcLift : 0f;
                float x = (i - 1) * spacing;

                // Half a lift down, so the arc straddles the row's centre instead of growing
                // out of the top of it.
                float y = (i == 1 ? lift : 0f) - lift * .5f;
                _empty[i] = UIKit.Img("e" + i, transform, Art.S("Ui/star_empty"), new Color(1, 1, 1, .55f),
                                      Vector2.one * size, new Vector2(.5f, .5f), new Vector2(x, y));
                _empty[i].preserveAspect = true;
                _full[i] = UIKit.Img("f" + i, transform, Art.S("Ui/star_full"), Color.white,
                                     Vector2.one * size, new Vector2(.5f, .5f), new Vector2(x, y));
                _full[i].preserveAspect = true;
                _full[i].gameObject.SetActive(i < filled);
            }
        }

        public void SetInstant(int filled)
        {
            for (int i = 0; i < 3; i++)
            {
                _full[i].gameObject.SetActive(i < filled);
                _full[i].transform.localScale = Vector3.one;
            }
        }

        /// <summary>Pop stars in one by one with a rising chime.</summary>
        public void Reveal(int filled, float startDelay = .25f, float gap = .38f, Action onDone = null)
        {
            for (int i = 0; i < 3; i++) _full[i].gameObject.SetActive(false);
            for (int i = 0; i < filled; i++)
            {
                int k = i;
                Tween.After(startDelay + gap * i, () =>
                {
                    if (this == null || _full[k] == null) return;
                    _full[k].gameObject.SetActive(true);
                    _full[k].transform.localScale = Vector3.one * 2.4f;
                    Tween.Scale(_full[k].transform, 1f, .5f, Ease.OutBack);
                    Audio.Sfx("star", .75f, 1f + k * .16f);
                    Burst.Sparks(_full[k].transform, Vector2.zero, Pal.Gold, 10, _size * 1.5f, _size * .28f, .6f);
                    Tween.Punch(transform, .07f, .3f);
                }, this);
            }
            if (onDone != null) Tween.After(startDelay + gap * Mathf.Max(1, filled) + .2f, onDone, this);
        }
    }
}
