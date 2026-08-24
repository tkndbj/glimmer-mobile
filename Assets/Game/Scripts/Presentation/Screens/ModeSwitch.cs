using System;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The control in the map's bottom corner that swaps which way of playing you are looking
    /// at.
    ///
    /// <para>
    /// A drop-up rather than a tab row along the top, and that is a decision about the screen
    /// it lives on rather than a taste. The map is one long vertical strip that is dragged
    /// through, its top is already carrying the chapter's name and both chapter arrows, and
    /// every pixel of chrome there is a pixel of grove nobody can see. A corner pill costs one
    /// corner and opens over the map only while it is being used.
    /// </para>
    /// <para>
    /// Built as its own file rather than as four more methods on <c>LevelsScreen</c>, which is
    /// a thousand lines already. It knows nothing about chapters, layouts or progress: handed
    /// the catalog, the mode being shown and somewhere to send the answer, it does one thing.
    /// </para>
    /// <para>
    /// It draws nothing at all when there is one mode, which is what makes it safe to build
    /// unconditionally: a switcher offering a single choice is a control that teaches people
    /// their taps do nothing, and this game has already learned that lesson twice.
    /// </para>
    /// </summary>
    public static class ModeSwitch
    {
        const float PillW = 300f, PillH = 96f;
        const float RowW = 470f, RowH = 118f, RowGap = 12f;

        /// <summary>
        /// Where a row's text block sits, measured from the row's own centre.
        ///
        /// A row is <c>RowW - 28</c> wide, so it runs from <c>-(RowW - 28) / 2</c> to the same
        /// on the right. The mark takes the first 74 of that; the text gets the rest, less a
        /// margin on the right so a long translation stops short of the rim rather than on it.
        /// </summary>
        const float RowInner = RowW - 28f;
        const float MarkGutter = 78f, RightPad = 18f;
        const float TextW = RowInner - MarkGutter - RightPad;
        const float TextX = (MarkGutter - RightPad) * .5f;

        /// <summary>
        /// Puts the switcher in <paramref name="host"/>'s bottom-right corner.
        ///
        /// <paramref name="host"/> should be the screen's safe-area layer: this is chrome, and
        /// a control under a phone's home indicator is a control nobody can press.
        /// </summary>
        public static void Build(RectTransform host, CatalogIndex index, GameMode current,
                                 Action<GameMode> choose)
        {
            if (index == null || !index.HasSeveralModes) return;

            var pill = UIKit.Button("ModeSwitch", host, Art.S("Ui/btn_blue"),
                                    new Vector2(PillW, PillH), new Vector2(1f, 0f),
                                    UIKit.Corner(new Vector2(PillW, PillH), new Vector2(1f, 0f), 34f, 34f),
                                    null);

            float lift = PillH * UIKit.PillFaceLift;

            var mark = UIKit.Img("Mark", pill.transform, ModeLooks.Of(current).Mark(),
                                 ModeLooks.Of(current).Accent, Vector2.one * 46f,
                                 new Vector2(.5f, .5f), new Vector2(-96f, lift));
            mark.preserveAspect = true;

            // Clear of both the mark on its left (which ends at -73) and the chevron on its
            // right (which starts at 103), rather than overlapping the mark by ten pixels as
            // it did — invisible on "Glades" and not on a longer word in another language.
            var label = UIKit.Titled("Name", pill.transform, Loc.Get(current.NameKey), 34, Pal.Cream,
                                     TextAnchor.MiddleLeft, new Vector2(160f, PillH * .6f),
                                     new Vector2(.5f, .5f), new Vector2(14f, lift), 0f, 3f);
            UIKit.Shrinkable(label);

            UIKit.Titled("Chevron", pill.transform, "▲", 22, Pal.A(Pal.Cream, .70f),
                         TextAnchor.MiddleCenter, new Vector2(30f, 30f), new Vector2(.5f, .5f),
                         new Vector2(118f, lift), 0f, 2f);

            pill.Setup(() => Open(host, index, current, choose));
        }

        /// <summary>
        /// Opens the list above the pill.
        ///
        /// <para>
        /// The veil is the whole reason this is one method rather than a small component: it
        /// swallows every tap outside the list, so there is no corner of the screen where a tap
        /// does nothing while a menu is open, and it is the one thing that has to be destroyed
        /// with the list however the list goes away. Hiding before destroying is the house rule
        /// - <c>Destroy</c> lands at the end of the frame, so a menu closed on the same frame
        /// something else opens would be drawn over its replacement for it.
        /// </para>
        /// </summary>
        static void Open(RectTransform host, CatalogIndex index, GameMode current,
                         Action<GameMode> choose)
        {
            var veil = UIKit.Node("ModeVeil", host);
            UIKit.StretchTo(veil, 0, 0, 0, 0);

            // Invisible, and still the thing that catches every tap outside the list. A modal
            // over a *decision* earns a dim — it is saying "answer this first". This one is a
            // two-item menu in a corner, and darkening the whole map to open it made the map
            // look switched off; the list has its own plate and rim, so it reads as raised
            // without the screen behind it having to be pushed down.
            //
            // An Image with no sprite hit-tests its whole rect whatever its alpha, so nothing
            // about swallowing the tap depended on the colour being visible.
            var catcher = veil.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            void Close()
            {
                if (!veil) return;
                veil.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(veil.gameObject);
            }

            veil.gameObject.AddComponent<Btn>().Setup(Close, silent: true);

            var modes = index.Modes;
            float height = modes.Count * RowH + (modes.Count - 1) * RowGap;

            var list = UIKit.Box("Modes", veil, new Vector2(RowW, height), new Vector2(1f, 0f),
                                 UIKit.Corner(new Vector2(RowW, height), new Vector2(1f, 0f),
                                              34f, 34f + PillH + 18f));

            var plate = UIKit.Img("Plate", list, Art.Round(28), new Color(.05f, .11f, .16f, .95f));
            UIKit.StretchTo((RectTransform)plate.transform, -14, -14, -14, -14);
            plate.transform.SetAsFirstSibling();

            var edge = UIKit.Img("Edge", plate.transform, Art.RoundOutline(28, 3f), new Color(1, 1, 1, .16f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            for (int i = 0; i < modes.Count; i++)
            {
                var mode = modes[i];

                // Bottom-up, so the entry nearest the pill is the first one in the list and the
                // order does not appear to flip when the menu opens upward.
                float y = -(height * .5f) + RowH * .5f + i * (RowH + RowGap);
                Row(list, mode, mode == current, y, () => { Close(); choose?.Invoke(mode); });
            }

            list.localScale = new Vector3(1f, .82f, 1f);
            Tween.Scale(list, Vector3.one, .18f, Ease.OutBack);
            Audio.Sfx("click", .4f);
        }

        static void Row(RectTransform parent, GameMode mode, bool selected, float y, Action tap)
        {
            var row = UIKit.Box("Mode_" + mode.Value, parent, new Vector2(RowW - 28f, RowH),
                                new Vector2(.5f, .5f), new Vector2(0f, y));

            var accent = ModeLooks.Of(mode).Accent;

            // The row's own hit area. UIKit.Img leaves raycastTarget off on everything it
            // builds, so a row made only of pictures is a row no tap ever reaches — invisible
            // in the Editor's hierarchy and obvious the first time somebody presses it.
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            var seat = UIKit.Img("Seat", row, Art.Round(22),
                                 selected ? Pal.A(accent, .18f) : new Color(1f, 1f, 1f, .05f));
            UIKit.StretchTo((RectTransform)seat.transform, 0, 0, 0, 0);

            if (selected)
            {
                var rim = UIKit.Img("Rim", row, Art.RoundOutline(22, 3f), Pal.A(accent, .70f));
                UIKit.StretchTo((RectTransform)rim.transform, 0, 0, 0, 0);
            }

            var mark = UIKit.Img("Mark", row, ModeLooks.Of(mode).Mark(), accent, Vector2.one * 52f,
                                 new Vector2(0f, .5f), new Vector2(48f, 0f));
            mark.preserveAspect = true;

            // Both labels are centred boxes placed at a measured centre, never boxes anchored to
            // the row's left edge. UIKit.Box always pivots at centre, so anchoring a 330-wide
            // box 90px from the left edge puts its first 75px *outside* the row — and a Text is
            // left-aligned inside that box, so the words began past the plate entirely. It is
            // the same trap PillFaceLift and the win panel's rank word each record; the fix is
            // to say where the middle of the text block goes, which is what TextX and TextW are.
            var name = UIKit.Titled("Name", row, Loc.Get(mode.NameKey), 34,
                                    selected ? Pal.Cream : Pal.A(Pal.Cream, .82f),
                                    TextAnchor.MiddleLeft, new Vector2(TextW, 40f),
                                    new Vector2(.5f, .5f), new Vector2(TextX, 20f), 0f, 2f);
            UIKit.Shrinkable(name);

            // The tagline is the only place the game ever says what a mode *is*, and it is here
            // rather than on a first-run panel because this is where somebody is deciding.
            var tag = UIKit.Label("Tag", row, Loc.Get(mode.TaglineKey), 24,
                                  Pal.A(Pal.Cream, .60f), TextAnchor.MiddleLeft,
                                  new Vector2(TextW, 40f), new Vector2(.5f, .5f),
                                  new Vector2(TextX, -22f));
            UIKit.Shrinkable(tag, 14);

            row.gameObject.AddComponent<Btn>().Setup(tap);
        }
    }
}
