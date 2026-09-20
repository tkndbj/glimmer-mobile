using System;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>Shared set dressing: parallax skies, vignettes, toasts and star rows.</summary>
    public static class Scenery
    {
        /// <summary>
        /// Three painted layers that drift against each other.
        ///
        /// <para>
        /// <paramref name="vignette"/> is separate from <paramref name="dim"/> because they
        /// darken different things: the shade is flat and costs the whole picture the same,
        /// where the vignette costs the corners several times what it costs the middle. A
        /// screen whose content is a centred column can afford a heavy one; a screen whose
        /// backdrop <em>is</em> the mood — the season pass, whose art is a sunrise — cannot,
        /// and passing 0 there is what lets it keep its own sky. The default is what every
        /// screen built before the parameter existed was already getting.
        /// </para>
        /// </summary>
        public static RectTransform Layered(Transform parent, string prefix, float dim = .18f,
                                            float vignette = .55f)
        {
            var host = UIKit.Node("Backdrop", parent);
            AddLayer(host, prefix + "_sky", 8f, 1.06f);
            AddLayer(host, prefix + "_ground", 20f, 1.08f);
            AddLayer(host, prefix + "_deco", 38f, 1.11f);

            if (dim > 0f) UIKit.Img("Shade", host, Art.Pixel, new Color(.04f, .08f, .12f, dim));
            if (vignette > 0f)
            {
                var vig = UIKit.Img("Vignette", host, Art.Vignette(256), new Color(.02f, .05f, .09f, vignette));
                vig.type = Image.Type.Simple;
            }
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

        /// <summary>
        /// A sun, high and a little to the right, for a screen whose backdrop is daylight.
        ///
        /// <para>
        /// <b>It is drawn where the art was lit from, not where it looks nice.</b> Every piece
        /// in the grove is rendered by one rig in <c>Tools/make_grove_art.py</c>, and that
        /// rig's key projects onto this screen's axes as .996 up and .078 right — all but
        /// straight overhead, leaning a hair right. So the offset here is that ratio and not a
        /// taste: a sun drawn on the opposite side from the one the models are lit by is the
        /// fault <c>37aj</c> names, where the shape is right and the sign is wrong and no
        /// numeric gate can see it. If <c>KEY</c> ever moves, this moves with it.
        /// </para>
        ///
        /// <para>
        /// Three layers, because one is a circle and a sun is not: a wide halo that reaches
        /// most of the way down the sky, a tighter core, and a small hard disc that is the
        /// thing actually being looked at. Added <em>after</em> the shade and the vignette, so
        /// the one part of the picture that is supposed to be the brightest is not the part
        /// being darkened.
        /// </para>
        ///
        /// <para>
        /// Every measurement is a share of <see cref="Boot.RefHeight"/> rather than of the
        /// canvas width, because the width is the one thing that is not the same on every
        /// display (invariant 37cc): sized against it, a tablet would get a sun half again as
        /// large for no reason anybody could state. The height is what does not move.
        /// </para>
        /// </summary>
        public static void Sun(Transform host, float strength = 1f)
        {
            const float H = Boot.RefHeight;

            // Where the disc stands, and it is **not** the top of the screen. Every screen
            // that carries this also carries a header fade — a gradient 268 units deep plus
            // whatever the notch has taken — so a sun drawn against the top edge is a sun
            // drawn behind the only thing on the sky that is not the sky. This clears the
            // deepest that fade gets (268 + a tall cutout) and nothing more, so the sun is as
            // high as it can be and still be a sun rather than a smudge under the banner.
            const float Rise = H * .25f;

            // Sideways, from the centre. See above — `key . right / key . up`, and nothing
            // else: this is where the models were lit from, not where it looks best.
            var at = new Vector2(H * .078f, -Rise);

            var halo = new Color(1f, .88f, .58f, .30f * strength);
            var core = new Color(1f, .94f, .72f, .46f * strength);
            var face = new Color(1f, .985f, .90f, .92f * strength);

            UIKit.Img("SunHalo", host, Art.Glow(256, 1.35f), halo,
                      new Vector2(H * 1.10f, H * 1.10f), new Vector2(.5f, 1f), at);
            UIKit.Img("SunCore", host, Art.Glow(192, 2.8f), core,
                      new Vector2(H * .46f, H * .46f), new Vector2(.5f, 1f), at);
            UIKit.Img("SunFace", host, Art.Disc(128), face,
                      new Vector2(H * .095f, H * .095f), new Vector2(.5f, 1f), at);
        }

        // -------------------------------------------------------------------- the kit
        /// <summary>
        /// The quiet ground every screen that is not the hub stands on: a flat blue scattered
        /// with the game's own confetti — hearts, stars, crowns, leaves — all of it within a
        /// shade or two of the ground itself.
        ///
        /// <para>
        /// <b>Separate from <see cref="Room"/> on purpose, and the difference is what each
        /// screen is for.</b> The hub is a place: one object stands in the middle of it and the
        /// painting is composed around that. A board, a profile or a shop shelf is a *list* —
        /// its whole surface is plates and rows, and a composed picture behind one is a picture
        /// nobody can see any of. So this is texture rather than scenery: enough that the screen
        /// is not a flat fill, never enough to be looked at.
        /// </para>
        /// <para>
        /// It replaced <see cref="Layered"/> with the grove's own three layers on the four
        /// screens that used it, which were three sprites and a parallax rig each to draw
        /// something a dim of .22 to .26 was mostly hiding anyway.
        /// </para>
        /// </summary>
        /// <summary>
        /// The two walls <see cref="Plain"/> can be asked for: the blue one every list screen
        /// in the game stands on, and the Infinite lane's purple one.
        ///
        /// <para>
        /// <b>Named rather than passed as a literal</b>, because an address typed at a call
        /// site is invisible to <c>artnames.py</c> the moment anything builds it (invariant 7)
        /// — and because these are the two ends of a decision rather than two strings: a
        /// second ground is the only thing that separates the ranked lane from the ladder
        /// beside it, so the day there is a third it wants to be in this list and not spread
        /// across three screens.
        /// </para>
        /// </summary>
        public const string WallPlain = "Bg/plain";
        public const string WallRanked = "Bg/plain_ranked";

        public static RectTransform Plain(Transform parent, string wall = WallPlain)
        {
            var host = UIKit.Node("Plain", parent);

            var s = Art.S(wall);
            if (s == null)
            {
                // The frame between a cold boot and the bundle arriving. An `Image` with no
                // sprite is a white rectangle over the whole screen (invariant 7b).
                UIKit.Img("Flat", host, Art.Pixel, Skins.Sky).raycastTarget = false;
                return host;
            }

            var img = UIKit.Img("Bg", host, s, Color.white);
            var rt = (RectTransform)img.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.anchoredPosition = Vector2.zero;
            var fit = img.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = s.rect.width / s.rect.height;
            img.raycastTarget = false;

            // No parallax and no vignette. The pattern is uniform, so drifting it says nothing
            // and darkening its corners darkens a colour that was chosen.
            //
            // **`Bg/plain` and `Bg/hub_room` are the same picture today**, at the owner's
            // instruction: every screen is the one blue wall, so the hub and the pages off it
            // read as one place. They are still two addresses, and deliberately — that is the
            // seam that lets the hub be re-cut as somewhere without touching six list screens,
            // and it has been used once already. What it costs while they agree is one
            // full-screen texture resident twice, which is about two thirds of a megabyte.
            // What distinguishes the two is the *treatment* rather than the art:
            // `Room` carries a dim, a vignette and a parallax and this carries none.
            //
            // **`Bg/plain_ranked` is the same wall in the Infinite lane's purple**, scattered
            // with crowns instead of the blue one's confetti, and it is drawn through this
            // same call for that reason: the ranked lane is not a different *kind* of screen,
            // it is the same list furniture on a ground that says which track you are on. See
            // `WallRanked` and `EndlessHub.Build`.
            return host;
        }

        /// <summary>
        /// The world the hub and the storefront both stand in: one authored painting,
        /// enveloped.
        ///
        /// <para>
        /// <b>It is a picture again, and this one was drawn for the job rather than cut out of
        /// a pack.</b> The three that came before were a forest, a machine room and a slice of
        /// a level map, and each was a picture of somewhere <em>else</em> with an interface put
        /// on top; this one is composed around the thing the screen actually holds — a lit
        /// plinth, dead centre, with the eye led to it by a stair, a ring on the floor and a
        /// shaft of light. The companion stands on the plinth and the plates stand either side
        /// of the light.
        /// </para>
        /// <para>
        /// <b>It lives under <c>Art/Bg/</c> and not under <c>Art/Ui/</c>, which is a memory
        /// decision rather than a filing one</b>: <c>ArtImportRules</c> caps the UI folder at
        /// 1024 and a backdrop at 2048, and 1024 on a 1920 canvas is a soft picture behind
        /// crisp text.
        /// </para>
        /// <para>
        /// <b>No shade and only a light vignette.</b> The painting is already dark at its
        /// corners and bright where the plinth is, which is the job a vignette was doing for a
        /// flat fill; dimming it further is undoing the composition.
        /// <paramref name="dim"/> is kept and ignored, because a dozen call sites pass it.
        /// </para>
        /// </summary>
        public static RectTransform Room(Transform parent, float dim = .10f, float vignette = .52f)
        {
            var host = UIKit.Node("Room", parent);

            var s = Art.S("Bg/hub_room");
            if (s != null)
            {
                var img = UIKit.Img("Bg", host, s, Color.white);
                var rt = (RectTransform)img.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
                rt.anchoredPosition = Vector2.zero;
                var fit = img.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = s.rect.width / s.rect.height;
                rt.localScale = Vector3.one * 1.04f;
                Parallax.Attach(rt, 8f);
                img.raycastTarget = false;
            }
            else
            {
                // The frame between a cold boot and the bundle arriving, rather than a missing
                // file - and an `Image` with no sprite is a white rectangle over the whole
                // screen (invariant 7b), which is the one thing a backdrop may never be.
                UIKit.Img("Flat", host, Art.Pixel, Skins.Sky).raycastTarget = false;
            }

            if (vignette > 0f)
            {
                var vig = UIKit.Img("Vignette", host, Art.Vignette(256),
                                    Pal.A(Color.black, vignette * .40f));
                vig.type = Image.Type.Simple;
                vig.raycastTarget = false;
            }
            return host;
        }

        /// <summary>
        /// The heading every screen wears: the modal panel's own ribbon, off square, with the
        /// word set on it in cream.
        ///
        /// <para>
        /// <b>It came off `ModalView.MakePanel` because the owner liked it there.</b> That was
        /// the one place in the game a heading was a real cloth ribbon rather than a wooden
        /// plaque with brown lettering on it — the shape this UI drew before it had a kit, and
        /// the one four screens still had at the top of them. So the ribbon is the heading now,
        /// and the panel and the screens draw the same object.
        /// </para>
        /// <para>
        /// <b>The tilt is the point and is why this is a function.</b> A ribbon hung dead
        /// straight reads as a plate; -1.6 degrees is what makes it cloth, and it is a number
        /// four call sites would otherwise each have to remember. So are the ink and the
        /// outline: the sprite is orange, so the word is cream over a dark outline, and a
        /// screen that reached for its own brown would be writing on it as if it were wood.
        /// </para>
        /// <para>
        /// <paramref name="caption"/> narrows the word's own box where something else has to
        /// fit beside it — the glade map's two chapter chevrons sit inside the ribbon's width,
        /// so its title gets less room than the ribbon has.
        /// </para>
        /// <para>
        /// The caption is kept to one line and shrunk to fit rather than trusted to be short — three of the four are
        /// translated nouns and the fourth is a chapter name authored per drop. Returns the
        /// ribbon, so a caller can pop it or hang a chevron off it.
        /// </para>
        /// </summary>
        public static Image TitleRibbon(Transform parent, string text, Vector2 size,
                                        Vector2 anchor, Vector2 pos, int fontSize = 44,
                                        float floor = 24f, float caption = 0f)
        {
            var ribbon = UIKit.Img("Banner", parent, Art.S("Ui/ribbon_orange"), Color.white,
                                   size, anchor, pos);
            ribbon.transform.localRotation = Quaternion.Euler(0f, 0f, -1.6f);

            // **One line, always.** `UIKit.Shrinkable` is the wrong fitter for a heading: best
            // fit keeps a caption inside its box by letting it *wrap*, so a long title comes out
            // as two or three stacked lines standing well outside the cloth rather than as one
            // smaller line on it. Reported from the glade map, where "THE ENDLESS WATCH" was
            // three of them. See `UIKit.OneLineLabel`.
            float room = caption > 0f ? caption : size.x * .74f;

            UIKit.OneLineLabel(
                UIKit.Titled("Title", ribbon.transform, text, fontSize, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(room, size.y * .5f),
                             new Vector2(.5f, .5f), Vector2.zero, 4f, 4f),
                room, fontSize, Mathf.RoundToInt(floor));

            return ribbon;
        }

        /// <summary>
        /// The kit's rail across the top or the foot of a screen.
        ///
        /// <para>
        /// Stretched to the full width rather than nine-sliced, because the kit draws a notch
        /// in the middle of each one and a slice would stretch exactly that. What a plain
        /// stretch does at this canvas width is <em>shrink</em> the sprite by a fifth, which is
        /// nothing a player can see on a bar and is what keeps the notch.
        /// </para>
        /// </summary>
        public static Image Rail(Transform parent, bool top)
        {
            var img = UIKit.Img(top ? "RailTop" : "RailFoot", parent,
                                Art.S("Ui/" + (top ? Skins.Rail : Skins.RailFoot)), Color.white,
                                new Vector2(Boot.RefWidth, top ? RailTopH : RailFootH),
                                new Vector2(.5f, top ? 1f : 0f),
                                new Vector2(0f, top ? -RailTopH * .5f : RailFootH * .5f));

            var rt = (RectTransform)img.transform;
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.sizeDelta = new Vector2(0f, top ? RailTopH : RailFootH);
            return img;
        }

        /// <summary>
        /// How tall each rail draws. The kit's own proportions at this canvas width — 1330x80
        /// and 1333x95 taken to 1080 — so the notch keeps its shape.
        /// </summary>
        public const float RailTopH = 65f;
        public const float RailFootH = 77f;

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

        /// <summary>How wide a toast is, and the air either side of the words inside it.</summary>
        const float ToastWide = 920f, ToastPad = 34f, ToastLeast = 148f;

        /// <summary>
        /// Floating message that rises and fades. Sits below the header by default.
        ///
        /// <para>
        /// <b>It grows to fit what it is asked to say.</b> It was a fixed 148-unit box, which is
        /// two lines: a mode's one-sentence rule ran to five and the rest of it was drawn outside
        /// the plate, because a <c>Text</c> that overflows is not clipped and nothing says so.
        /// The height is now measured off the text itself.
        /// </para>
        /// <para>
        /// <b>And it renders markup</b>, which is why <c>ui.tip</c>-style emphasis in a mode's
        /// refusal reads as bold rather than as the letters <c>&lt;b&gt;</c>. Safe here and
        /// nowhere else by default: a toast is always a loc string, never a player's own text
        /// (see <c>UIKit.Titled</c>).
        /// </para>
        /// </summary>
        public static void Toast(Transform parent, string message, Color? tint = null, float hold = 1.9f,
                                 Vector2 anchor = default, float y = 300f)
        {
            if (anchor == default) anchor = new Vector2(.5f, 0f);
            var bg = UIKit.Img("Toast", parent, Art.Round(30), new Color(.05f, .11f, .16f, .0f),
                               new Vector2(ToastWide, ToastLeast), anchor, new Vector2(0f, y - 50f));
            var edge = UIKit.Img("Edge", bg.transform, Art.RoundOutline(30, 3f), new Color(1, 1, 1, 0f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            // Built at the width it will really have, so `preferredHeight` below is the height
            // this string really needs rather than the height of one endless line.
            var t = UIKit.Titled("Text", bg.transform, message, 34, tint ?? Pal.Cream,
                                 TextAnchor.MiddleCenter,
                                 boxSize: new Vector2(ToastWide - ToastPad * 2f, ToastLeast),
                                 anchorPt: new Vector2(.5f, .5f),
                                 outline: 3f, shadow: 3f, wrap: true, rich: true);

            bg.rectTransform.sizeDelta =
                new Vector2(ToastWide, Mathf.Max(ToastLeast, t.preferredHeight + 44f));
            ((RectTransform)t.transform).sizeDelta =
                new Vector2(ToastWide - ToastPad * 2f, bg.rectTransform.sizeDelta.y - 24f);

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

    /// <summary>
    /// Row of stars that can be filled with a rising fanfare.
    ///
    /// <para>
    /// Three of them for a glade's rating, which is what it was built for and still the
    /// default; the count is a parameter because the grove's worth is read against a ladder
    /// whose length is content (see <c>GroveScoreTable</c>) and could be four rungs or six
    /// after any drop. One row rather than a second one beside it, so a star pops, chimes and
    /// sparkles the same way wherever the game shows one.
    /// </para>
    /// </summary>
    public sealed class StarRow : MonoBehaviour
    {
        Image[] _full, _empty;
        float _size;

        /// <summary>How far the middle star rides above its neighbours, as a fraction of size.</summary>
        const float ArcLift = .22f;

        public static StarRow Create(Transform parent, Vector2 anchor, Vector2 pos, float size,
                                     float spacing, int filled = 0, bool arc = false, int count = 3)
        {
            count = Mathf.Max(1, count);

            // The box has to allow for the arc, and the stars have to be centred inside it.
            // It used to be exactly `size` tall whether or not the middle star was lifted, so
            // an arced row's real extent ran from -size/2 to +size/2 + lift — the group sat
            // half a lift high, and on the victory panel that was enough to push the middle
            // star into the ribbon above it. Rows without an arc are unaffected: the lift is
            // zero and every number below is what it always was.
            float lift = arc ? size * ArcLift : 0f;

            var rt = UIKit.Box("Stars", parent, new Vector2(spacing * count, size + lift), anchor, pos);
            var row = rt.gameObject.AddComponent<StarRow>();
            row.Build(size, spacing, filled, arc, count);
            return row;
        }

        void Build(float size, float spacing, int filled, bool arc, int count)
        {
            _size = size;
            _full = new Image[count];
            _empty = new Image[count];
            for (int i = 0; i < count; i++)
            {
                float lift = arc ? size * ArcLift : 0f;

                // Centred on the row whatever the count: three stars step -1, 0, 1 exactly as
                // they always did, and an even row straddles the middle rather than leaning.
                float x = (i - (count - 1) * .5f) * spacing;

                // Half a lift down, so the arc straddles the row's centre instead of growing
                // out of the top of it. Only a row with a true middle has one to lift.
                bool middle = count % 2 == 1 && i == count / 2;
                float y = (middle ? lift : 0f) - lift * .5f;
                _empty[i] = UIKit.Img("e" + i, transform, Art.S("Ui/star_empty"), new Color(1, 1, 1, .55f),
                                      Vector2.one * size, new Vector2(.5f, .5f), new Vector2(x, y));
                _empty[i].preserveAspect = true;
                _full[i] = UIKit.Img("f" + i, transform, Art.S("Ui/star_full"), Color.white,
                                     Vector2.one * size, new Vector2(.5f, .5f), new Vector2(x, y));
                _full[i].preserveAspect = true;
                _full[i].gameObject.SetActive(i < filled);
            }
        }

        /// <summary>How many stars this row draws, filled or not.</summary>
        public int Count => _full == null ? 0 : _full.Length;

        public void SetInstant(int filled)
        {
            for (int i = 0; i < _full.Length; i++)
            {
                _full[i].gameObject.SetActive(i < filled);
                _full[i].transform.localScale = Vector3.one;
            }
        }

        /// <summary>Pop stars in one by one with a rising chime.</summary>
        public void Reveal(int filled, float startDelay = .25f, float gap = .38f, Action onDone = null)
        {
            for (int i = 0; i < _full.Length; i++) _full[i].gameObject.SetActive(false);
            for (int i = 0; i < Mathf.Min(filled, _full.Length); i++)
            {
                int k = i;
                Tween.After(startDelay + gap * i, () =>
                {
                    if (this == null || _full[k] == null) return;
                    _full[k].gameObject.SetActive(true);
                    _full[k].transform.localScale = Vector3.one * 2.4f;
                    Tween.Scale(_full[k].transform, 1f, .5f, Ease.OutBack);
                    // .55 rather than .75: with the victory panel's decorative sounds gone
                    // this is the loudest thing on it after the board's own fanfare, and a
                    // three-star reveal is three of them in a row.
                    Audio.Sfx("star", .55f, 1f + k * .16f);
                    Burst.Sparks(_full[k].transform, Vector2.zero, Pal.Gold, 10, _size * 1.5f, _size * .28f, .6f);
                    Tween.Punch(transform, .07f, .3f);
                }, this);
            }
            if (onDone != null) Tween.After(startDelay + gap * Mathf.Max(1, filled) + .2f, onDone, this);
        }
    }
}
