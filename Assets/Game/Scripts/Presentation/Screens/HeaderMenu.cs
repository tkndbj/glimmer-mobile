using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// One row of a header drop-down: what it says, what colour it wears, and what it does.
    ///
    /// <b>Words rather than a domain type</b>, because this control carries rows that are not all
    /// the same kind of thing — ways of playing, ladders inside one of them, and a workbench that
    /// is neither. The caller resolves its own text, which is what keeps <c>Loc</c> out of a row
    /// that has no key.
    /// </summary>
    public readonly struct HeaderRow
    {
        public readonly string Id, Title, Tagline;
        public readonly Color Accent;
        public readonly bool Selected;
        public readonly Action Tap;

        public HeaderRow(string id, string title, string tagline, Color accent, bool selected,
                         Action tap)
        {
            Id = id;
            Title = title;
            Tagline = tagline;
            Accent = accent;
            Selected = selected;
            Tap = tap;
        }
    }

    /// <summary>
    /// The pill-and-drop-down that lives under the map's chapter plaque.
    ///
    /// <para>
    /// <b>One control used twice, and the second use is what made it worth extracting.</b> It was
    /// <c>ModeSwitch</c> and nothing else for as long as a mode was the only thing a player could
    /// switch between; a mode with two <em>ladders</em> in it (<c>GameTrack</c>) needs the same
    /// pill saying a different word, and a second copy of the veil, the squash, the latch and the
    /// row would be a second place every one of those could stop being true. What is here is the
    /// furniture; what a caller supplies is the rows.
    /// </para>
    /// <para>
    /// <b>It draws nothing at all when there is one row</b>, which is what makes it safe for a
    /// screen to build unconditionally: a switcher offering a single choice is a control that
    /// teaches people their taps do nothing, and this game has already learned that twice.
    /// </para>
    /// </summary>
    public static class HeaderMenu
    {
        /// <summary>
        /// The pill, and the gap between it and the list it opens.
        ///
        /// Sized as a header control under a 476-wide plaque rather than as chrome tucked in a
        /// corner: a narrow pill under a wide plaque reads as an afterthought rather than as part
        /// of the same piece of furniture.
        /// </summary>
        const float PillW = 372f, PillH = 116f, MenuGap = 16f;

        /// <summary>How tall the pill is, for whoever is stacking it.</summary>
        public const float PillHeight = PillH;

        const float RowW = 500f, RowH = 124f, RowGap = 12f;

        /// <summary>
        /// The shortest a row may be squeezed to, and how much air is kept under the list.
        ///
        /// Below <see cref="MinRowH"/> a row's two lines — its name and its one-line tagline —
        /// stop being two lines and start being a smudge, so this is the number that says the
        /// control needs rethinking rather than shrinking again.
        /// </summary>
        const float MinRowH = 96f, ListFoot = 120f;

        const float RowInner = RowW - 28f;
        const float TextW = RowInner - 56f;

        /// <summary>
        /// How the list arrives and leaves.
        ///
        /// The exit is deliberately quicker than the entrance and eased the other way. An entrance
        /// is an invitation and can afford an overshoot; an exit is the answer to a tap that has
        /// already been made, so anything slower than about a sixth of a second reads as the menu
        /// arguing about it.
        /// </summary>
        const float EntryTime = .22f, ExitTime = .15f;
        const float ExitRise = 26f, ExitSquash = .86f;

        /// <summary>
        /// Puts a switcher in <paramref name="host"/>, centred on <paramref name="y"/> measured
        /// down from the host's top edge, and hands back the pill it drew — or <c>null</c> when it
        /// drew nothing.
        ///
        /// <paramref name="host"/> should be the screen's safe-area layer: this is chrome, and a
        /// control under a notch is a control nobody can read.
        /// </summary>
        /// <remarks>
        /// <b>Null is the answer that matters</b> — it is what says the switcher is not on screen
        /// at all, which is exactly when a lesson pointing at it must not be spent.
        /// </remarks>
        public static RectTransform Build(RectTransform host, string skin, string label,
                                          IReadOnlyList<HeaderRow> rows, float y)
        {
            if (host == null || rows == null || rows.Count < 2) return null;

            var pill = UIKit.Button("HeaderMenu", host, Art.S("Ui/" + skin),
                                    new Vector2(PillW, PillH), new Vector2(.5f, 1f),
                                    new Vector2(0f, y), null);

            float lift = PillH * UIKit.PillFaceLift;

            // Dead centre, with the chevron out at the rim. A centred name under a centred plaque
            // is the axis the whole header is built on; balancing the word against a glyph beside
            // it would put it off that axis by half the glyph.
            var name = UIKit.Titled("Name", pill.transform, label, 38, Pal.Cream,
                                    TextAnchor.MiddleCenter, new Vector2(PillW - 128f, PillH * .6f),
                                    new Vector2(.5f, .5f), new Vector2(0f, lift), 0f, 3f);
            UIKit.Shrinkable(name);

            // Pointing down, because the list opens downward. It turns rather than being swapped
            // for the other glyph while the menu is open: a mark that turns is the same mark
            // saying the same thing about the same list.
            var chevron = UIKit.Titled("Chevron", pill.transform, "▼", 26, Pal.A(Pal.Cream, .78f),
                                       TextAnchor.MiddleCenter, new Vector2(34f, 34f),
                                       new Vector2(.5f, .5f),
                                       new Vector2(PillW * .5f - 40f, lift), 0f, 2f);

            pill.Setup(() => Open(host, rows, y, (RectTransform)chevron.transform));

            return (RectTransform)pill.transform;
        }

        /// <summary>
        /// Opens the list below the pill.
        ///
        /// <para>
        /// The veil is the whole reason this is one method rather than a small component: it
        /// swallows every tap outside the list, so there is no corner of the screen where a tap
        /// does nothing while a menu is open, and it is the one thing that has to be destroyed
        /// with the list however the list goes away.
        /// </para>
        /// <para>
        /// <b>It leaves the way it arrived.</b> Nothing replaces it: it opens over a map that is
        /// already there and simply stops existing, so hiding it instantly is a list that vanishes
        /// mid-tap, which reads as a dropped frame rather than as a menu closing. It falls back
        /// into the pill it came out of and is destroyed when that lands — and the veil keeps
        /// eating taps for those few frames, with the close latched, or a second tap during the
        /// exit reaches the pill underneath and opens a second menu over the one still leaving.
        /// </para>
        /// </summary>
        static void Open(RectTransform host, IReadOnlyList<HeaderRow> rows, float pillY,
                         RectTransform chevron)
        {
            var veil = UIKit.Node("MenuVeil", host);
            UIKit.StretchTo(veil, 0, 0, 0, 0);

            // Invisible, and still the thing that catches every tap outside the list. A modal over
            // a *decision* earns a dim; this is a short menu under the header, and darkening the
            // map to open it made the map look switched off. An Image with no sprite hit-tests its
            // whole rect whatever its alpha, so nothing about swallowing the tap depended on the
            // colour being visible.
            var catcher = veil.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            int count = rows.Count;

            // **A fixed row height stopped being safe at eight rows.** The list hangs from under
            // the header and grows downwards, so its height is a count times a constant - fine at
            // three and off the bottom of a short phone at eight. It is squeezed rather than
            // scrolled deliberately: a two-line row still reads at 96 units, and a scrolling list
            // of ways to play is a list that hides one of them behind a gesture nobody would think
            // to make. The floor says how many rows this control can honestly carry.
            float top = pillY - PillH * .5f - MenuGap;
            float room = Mathf.Max(0f, veil.rect.height + top - ListFoot);
            float rowH = count > 1
                ? Mathf.Clamp((room - (count - 1) * RowGap) / count, MinRowH, RowH)
                : RowH;

            float height = count * rowH + (count - 1) * RowGap;
            float listY = top - height * .5f;

            var list = UIKit.Box("Rows", veil, new Vector2(RowW, height), new Vector2(.5f, 1f),
                                 new Vector2(0f, listY));

            // One group for the whole list, so the exit is a single fade rather than a fade per
            // plate, rim, seat and line — which would be a dozen tweens racing to the same frame.
            var group = list.gameObject.AddComponent<CanvasGroup>();

            var plate = UIKit.Img("Plate", list, Art.Round(28), new Color(.05f, .11f, .16f, .95f));
            UIKit.StretchTo((RectTransform)plate.transform, -14, -14, -14, -14);
            plate.transform.SetAsFirstSibling();

            var edge = UIKit.Img("Edge", plate.transform, Art.RoundOutline(28, 3f),
                                 new Color(1, 1, 1, .16f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            bool closing = false;

            void Close()
            {
                if (closing || !veil) return;
                closing = true;

                if (chevron) Tween.Rotate(chevron, 0f, ExitTime, Ease.OutQuad);

                Tween.Move(list, new Vector2(0f, listY + ExitRise), ExitTime, Ease.InQuad);
                Tween.Scale(list, new Vector3(1f, ExitSquash, 1f), ExitTime, Ease.InQuad);
                Tween.Fade(group, 0f, ExitTime, Ease.InQuad).OnDone(() =>
                {
                    if (!veil) return;
                    veil.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(veil.gameObject);
                });
            }

            veil.gameObject.AddComponent<Btn>().Setup(Close, silent: true);

            for (int i = 0; i < count; i++)
            {
                var row = rows[i];

                // Top-down, so the entry nearest the pill is the first one in the list and the
                // order reads the way the list opens.
                float rowY = height * .5f - rowH * .5f - i * (rowH + RowGap);
                var tap = row.Tap;

                Row(list, row.Id, row.Title, row.Tagline, row.Accent, row.Selected, rowY, rowH,
                    () => { Close(); tap?.Invoke(); });
            }

            if (chevron) Tween.Rotate(chevron, 180f, EntryTime, Ease.OutBack);

            group.alpha = 0f;
            list.localScale = new Vector3(1f, ExitSquash, 1f);
            list.anchoredPosition = new Vector2(0f, listY + ExitRise);

            Tween.Move(list, new Vector2(0f, listY), EntryTime, Ease.OutCubic);
            Tween.Scale(list, Vector3.one, EntryTime, Ease.OutBack);
            Tween.Fade(group, 1f, EntryTime * .6f, Ease.OutQuad);

            // No sound here: the button that opened this list already spoke on pointer down, and a
            // second click as the list unrolls is one tap making two noises.
        }

        /// <summary>One row of the list.</summary>
        static void Row(RectTransform parent, string id, string title, string tagline,
                        Color accent, bool selected, float y, float height, Action tap)
        {
            var row = UIKit.Box(id, parent, new Vector2(RowInner, height),
                                new Vector2(.5f, .5f), new Vector2(0f, y));

            // The two lines sit either side of the row's middle by a fraction of its height rather
            // than by a fixed offset, so a squeezed row closes the gap between them instead of
            // letting the tagline slide out of the seat. See MinRowH.
            float split = height * .165f;

            // The row's own hit area. UIKit.Img leaves raycastTarget off on everything it builds,
            // so a row made only of pictures is a row no tap ever reaches — invisible in the
            // Editor's hierarchy and obvious the first time somebody presses it.
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            var seat = UIKit.Img("Seat", row, Art.Round(22),
                                 selected ? Pal.A(accent, .18f) : new Color(1f, 1f, 1f, .05f));
            UIKit.StretchTo((RectTransform)seat.transform, 0, 0, 0, 0);

            // The colour, and this is the only thing carrying it. It rings the row the player is
            // already in rather than being decoration on all of them.
            if (selected)
            {
                var rim = UIKit.Img("Rim", row, Art.RoundOutline(22, 3f), Pal.A(accent, .70f));
                UIKit.StretchTo((RectTransform)rim.transform, 0, 0, 0, 0);
            }

            var name = UIKit.Titled("Name", row, title, 36,
                                    selected ? Pal.Cream : Pal.A(Pal.Cream, .82f),
                                    TextAnchor.MiddleCenter, new Vector2(TextW, 42f),
                                    new Vector2(.5f, .5f), new Vector2(0f, split), 0f, 2f);
            UIKit.Shrinkable(name);

            // The tagline is the only place the game says what one of these *is*, and it is here
            // rather than on a first-run panel because this is where somebody is deciding.
            var tag = UIKit.Label("Tag", row, tagline, 24,
                                  Pal.A(Pal.Cream, .60f), TextAnchor.MiddleCenter,
                                  new Vector2(TextW, 40f), new Vector2(.5f, .5f),
                                  new Vector2(0f, -split - 2f));
            UIKit.Shrinkable(tag, 14);

            row.gameObject.AddComponent<Btn>().Setup(tap);
        }
    }
}
