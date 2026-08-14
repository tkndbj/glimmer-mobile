using System;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The bottom navigation shared by the hub and the glade map.
    ///
    /// <para>
    /// Every tab is a full-height cell that takes the tap — cap, glyph and caption
    /// together — rather than the glyph alone. A 200x176 target is what a thumb
    /// actually hits on a phone; the old 124px icon was the whole hit box and the
    /// caption under it was dead.
    /// </para>
    /// <para>
    /// <b>There is no bar.</b> The five caps sit straight on the grove: no plate, no
    /// wash, no seam. A plate is the cheap way to guarantee contrast, and it costs the
    /// bottom eighth of every screen — the backdrop stops being the grove and becomes a
    /// slab. Instead each cap earns its own contrast the way the rest of this UI does,
    /// from the moulded jelly art's dark rim plus a soft seat shadow beneath it. That
    /// matters because <c>grove_near</c> runs from near-white inside the light shaft to
    /// dark teal beside it, so a flat shape would vanish on one half of the screen.
    /// Anything added here that spans the full width — a rail, a seam, a gradient with
    /// a visible edge — puts the slab back.
    /// </para>
    /// <para>
    /// Selection is carried by size, colour and height together rather than by a single
    /// marker: the live cap swells, lifts, turns teal and breathes. Three signals beat
    /// one because half these glyphs are painted in full colour and half are white
    /// silhouettes, so any scheme that leans on tinting the glyph reads differently
    /// depending on which tab you happen to be standing on.
    /// </para>
    /// </summary>
    public static class NavBar
    {
        /// <summary>
        /// Vertical space the bar occupies. Screens keep their content above this.
        /// Unchanged from the plated design on purpose — the caps were sized to fit the
        /// budget that every screen already reserves, so dropping the plate costs no
        /// layout anywhere else.
        /// </summary>
        public const float Height = 206f;

        public enum Tab { None, Home, Shop, Items, Ranks, Profile }

        /// <summary>
        /// Tab order, left to right. Home leads because it is the way back: it is the
        /// only tab that navigates rather than opening a panel, so it wants the corner
        /// the thumb already rests in. Slot width is derived from this, so adding a
        /// sixth tab re-spaces the bar rather than needing new coordinates.
        /// </summary>
        static readonly Tab[] Order = { Tab.Home, Tab.Shop, Tab.Items, Tab.Ranks, Tab.Profile };

        const float CellW = 200f;
        const float CellH = 176f;

        /// <summary>
        /// Cap sizes. The live one is roughly a quarter wider, which is most of what says "here";
        /// both are sized so the taller cap plus its caption fits inside <see cref="Height"/>.
        /// </summary>
        const float CapLive = 152f;
        const float CapRest = 124f;

        public static RectTransform Build(Transform parent, Tab active)
        {
            var bar = UIKit.Box("NavBar", parent, new Vector2(0f, Height), new Vector2(.5f, 0f),
                                new Vector2(0f, Height * .5f));
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.sizeDelta = new Vector2(0f, Height);

            float slot = Boot.RefWidth / (float)Order.Length;
            for (int i = 0; i < Order.Length; i++)
            {
                float x = (i - (Order.Length - 1) * .5f) * slot;
                Item(bar, x, Order[i], active == Order[i]);
            }

            return bar;
        }

        // --------------------------------------------------------------- one tab
        static void Item(Transform bar, float x, Tab tab, bool active)
        {
            string labelKey = LabelKey(tab);

            // the cell is the button; everything below is decoration inside it, so a
            // press squashes cap, glyph and caption as one object
            var cell = UIKit.Button("Nav_" + tab, bar, Art.Pixel, new Vector2(CellW, CellH),
                                    new Vector2(.5f, .5f), new Vector2(x, 4f), Tap(tab, active));
            var hit = cell.GetComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            if (active && Tap(tab, true) == null) { cell.ClickSfx = null; cell.PressScale = .97f; }

            float cap = active ? CapLive : CapRest;
            float capY = active ? 22f : 14f;

            // Every tab carries a cap, and the live one differs by size, colour and
            // height at once. It has to: half these glyphs are painted in full colour
            // and half are white silhouettes, so a single marker that works under one
            // would be the wrong marker under the next.
            if (active) UIKit.Halo(cell.transform, Pal.Aqua, cap * 1.62f, .30f, new Vector2(0f, capY));

            // what a plate used to do — a soft dark seat under the cap, sized to it, so
            // the tab holds its own over both the light shaft and the dark teal
            var seat = UIKit.Img("Seat", cell.transform, Art.Glow(128, 1.7f),
                                 new Color(.02f, .07f, .09f, active ? .42f : .34f),
                                 new Vector2(cap * 1.18f, cap * .74f), new Vector2(.5f, .5f),
                                 new Vector2(0f, capY - cap * .40f));
            seat.transform.SetAsFirstSibling();

            var capImg = UIKit.Img("Cap", cell.transform, Art.S(active ? "Ui/jelly_teal" : "Ui/sq_dark"),
                                   Color.white, Vector2.one * cap, new Vector2(.5f, .5f),
                                   new Vector2(0f, capY));
            capImg.preserveAspect = true;
            if (active) Tween.Breathe(capImg.transform, .025f, 2.8f);

            // The jelly art carries a moulded base below its lit face, so the face
            // centre sits above the sprite's middle — the glyph rides up by the same
            // fraction the rest of the UI uses, or it looks low in the cap.
            var ic = UIKit.Img("Icon", capImg.transform, Icon(tab),
                               active ? Pal.Cream : new Color(1f, .97f, .90f, .82f),
                               Vector2.one * (active ? cap * .54f : cap * .50f), new Vector2(.5f, .5f),
                               new Vector2(0f, cap * UIKit.SquareFaceLift));
            ic.preserveAspect = true;

            UIKit.Titled("L_" + tab, cell.transform, Loc.Get(labelKey), active ? 26 : 24,
                         active ? Pal.Cream : new Color(1f, .96f, .88f, .70f), TextAnchor.MiddleCenter,
                         new Vector2(CellW, 32f), new Vector2(.5f, .5f), new Vector2(0f, -66f), 4f, 3f);

            cell.transform.localScale = Vector3.zero;
            Tween.Pop(cell.transform, 0f, .5f, .62f + Mathf.Abs(x) * .00035f)
                 .OnDone(() => { if (cell) cell.Rehome(); });
        }

        // ------------------------------------------------------------- tab tables
        /// <summary>
        /// Written out per tab, never built by concatenation: the build gate scans the
        /// source for key-shaped literals and a composed key is invisible to it.
        /// </summary>
        static string LabelKey(Tab tab)
        {
            switch (tab)
            {
                case Tab.Home: return "ui.nav.home";
                case Tab.Shop: return "ui.nav.shop";
                case Tab.Items: return "ui.nav.items";
                case Tab.Ranks: return "ui.nav.ranks";
                default: return "ui.nav.profile";
            }
        }

        static string SoonKey(Tab tab)
        {
            switch (tab)
            {
                case Tab.Shop: return "ui.soon.shop";
                case Tab.Items: return "ui.soon.items";
                default: return "ui.soon.ranks";
            }
        }

        static Sprite Icon(Tab tab)
        {
            switch (tab)
            {
                case Tab.Home: return Art.S("Ui/ic_home");
                case Tab.Shop: return Art.S("Ui/ic_chest");
                case Tab.Items: return Art.S("Ui/ic_gift");
                case Tab.Ranks: return Art.S("Ui/ic_trophy");
                default: return Art.S("Ui/ic_profile");
            }
        }

        /// <summary>
        /// Home and Profile are screens; the rest are still promises. A tab already
        /// standing on its own screen returns null and is left inert rather than
        /// re-entering it.
        /// </summary>
        static Action Tap(Tab tab, bool active)
        {
            if (active && (tab == Tab.Home || tab == Tab.Profile)) return null;
            if (tab == Tab.Home) return () => Flow.Go<HomeScreen>();
            if (tab == Tab.Profile) return () => Flow.Go<ProfileScreen>();

            string title = LabelKey(tab), body = SoonKey(tab);
            var glyph = Icon(tab);
            return () => Flow.Modal<ComingSoonOverlay>(v => v.Configure(title, glyph, body));
        }
    }
}
