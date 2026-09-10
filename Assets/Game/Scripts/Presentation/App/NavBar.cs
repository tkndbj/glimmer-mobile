using System;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The bottom navigation shared by the hub, the glade map and the storefront.
    ///
    /// <para>
    /// Every tab is a full-height cell that takes the tap — cap, glyph and caption
    /// together — rather than the glyph alone. A 200x176 target is what a thumb actually
    /// hits on a phone; the old 124px icon was the whole hit box and the caption under it
    /// was dead.
    /// </para>
    /// <para>
    /// <b>There is a bar again, and the reason it came back is the reason it went.</b> It was
    /// removed because a plate is the cheap way to guarantee contrast and it cost the bottom
    /// eighth of every screen — the backdrop stopped being the grove and became a slab. What
    /// replaced it was five caps earning their own contrast off a moulded rim and a seat
    /// shadow, which worked. The kit's rail is not that plate: it is a 77-unit strip with a
    /// notch in it that the caps <em>overhang</em>, so it reads as the console they are set
    /// into rather than as a floor laid over the picture. Nothing behind it is lost, because
    /// nothing was ever drawn in the bottom 77 units of any screen.
    /// </para>
    /// <para>
    /// Selection is carried by the cap itself — the kit draws a lit tab and an unlit one as
    /// two different objects, a yellow face in a bright frame against a blue face in a dark
    /// one — plus size, height and a breath. Four signals beat one because half these glyphs
    /// are painted in full colour and half are white silhouettes, so any scheme that leans on
    /// tinting the glyph reads differently depending on which tab you happen to be standing
    /// on.
    /// </para>
    /// <para>
    /// <b>The glyphs are this game's own and never the kit's.</b> The pack ships five caps
    /// carrying a champion, a weapon and a backpack, which name nothing here — so what is used
    /// is the kit's blank face wearing the icons the rest of the UI already draws. A restyle
    /// that quietly renamed five destinations would be a much more expensive change than a
    /// restyle.
    /// </para>
    /// </summary>
    public static class NavBar
    {
        /// <summary>
        /// Vertical space the bar occupies. Screens keep their content above this.
        /// Unchanged across two restyles on purpose — the caps were sized to fit the budget
        /// every screen already reserves, so the look can move without any layout moving.
        /// </summary>
        public const float Height = 236f;

        public enum Tab { None, Home, Shop, Grove, Ranks, Profile }

        /// <summary>
        /// Tab order, left to right. Home leads because it is the way back: it is the
        /// only tab that navigates rather than opening a panel, so it wants the corner
        /// the thumb already rests in. Slot width is derived from this, so adding a
        /// sixth tab re-spaces the bar rather than needing new coordinates.
        /// </summary>
        static readonly Tab[] Order = { Tab.Home, Tab.Shop, Tab.Grove, Tab.Ranks, Tab.Profile };

        /// <summary>
        /// The button itself, and the cell that takes the tap. The button is the whole tab —
        /// icon and caption sit inside it — which is the shape the genre uses and the reason
        /// the icon is allowed to break its top edge: a glyph that overhangs reads as a thing
        /// standing in a slot rather than as a picture printed on a square.
        /// </summary>
        const float CellW = 214f;
        const float CellH = 208f;
        const float BtnW = 200f;
        const float BtnH = 172f;

        /// <summary>How far above the button's own middle the glyph sits, so it overhangs.</summary>
        const float IconY = 40f;
        const float IconSize = 136f;

        /// <summary>
        /// Draws the bar with <paramref name="active"/> marked.
        ///
        /// <para>
        /// <paramref name="onSidePage"/> says the caller is a <em>page belonging to</em> that
        /// tab rather than the tab's own screen. Without it the marked tab is left inert,
        /// which is right when you are standing on it and wrong one page in: a dead control
        /// is the worst thing to put where somebody is looking for the way back. Nothing
        /// passes it today — the grove's shop is the one caller left and it carries the bar
        /// not at all, leaning on its own back arrow instead.
        /// </para>
        /// </summary>
        public static RectTransform Build(Transform parent, Tab active, bool onSidePage = false)
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
                Item(bar, x, Order[i], active == Order[i], onSidePage);
            }

            return bar;
        }

        // --------------------------------------------------------------- one tab
        static void Item(Transform bar, float x, Tab tab, bool active, bool onSidePage)
        {
            string labelKey = LabelKey(tab);

            // the cell is the button; everything below is decoration inside it, so a
            // press squashes plate, glyph and caption as one object
            bool standing = active && !onSidePage;

            var cell = UIKit.Button("Nav_" + tab, bar, Art.Pixel, new Vector2(CellW, CellH),
                                    new Vector2(.5f, .5f), new Vector2(x, 4f), Tap(tab, standing));
            var hit = cell.GetComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            if (standing) { cell.ClickSfx = null; cell.PressScale = .97f; }

            float grow = active ? 1.06f : 1f;
            var face = Skins.Plate;

            if (active) UIKit.Halo(cell.transform, Pal.Sun, BtnW * 1.7f, .30f, new Vector2(0f, 6f));

            // A soft-cornered plate in the tab's own colour, lifted when it is the live one.
            // Selection is the plate and its frame, never the glyph — see Glyph.
            var plate = UIKit.Img("Plate", cell.transform, Art.Round(26),
                                  active ? Lift(face, .22f) : Pal.A(face, .92f),
                                  new Vector2(BtnW * grow, BtnH * grow), new Vector2(.5f, .5f),
                                  new Vector2(0f, 0f));

            // Two rims: a dark seat under everything, and the kit's gold on the live tab.
            var seat = UIKit.Img("Seat", plate.transform, Art.RoundOutline(26, 4f),
                                 new Color(.02f, .06f, .13f, .85f));
            UIKit.StretchTo((RectTransform)seat.transform, 0, 0, 0, 0);

            if (active)
            {
                var lit = UIKit.Img("Lit", plate.transform, Art.RoundOutline(26, 6f), Skins.PlateEdge);
                UIKit.StretchTo((RectTransform)lit.transform, -3, -3, -3, -3);
                Tween.Breathe(plate.transform, .018f, 2.8f);
            }

            // The glyph, big, and hanging over the plate's top edge. Never blacked out: it
            // keeps its own colour whether or not this is the live tab, so a selection can
            // never be read as an icon that has gone dead.
            float size = IconSize * grow;
            var ic = UIKit.Img("Icon", plate.transform, Icon(tab), Color.white,
                               Vector2.one * size, new Vector2(.5f, .5f),
                               new Vector2(0f, IconY * grow));
            ic.preserveAspect = true;

            // Inside the plate, under the glyph - which is what makes the button one object
            // rather than a square with a caption parked beneath it.
            UIKit.Shrinkable(
                UIKit.Titled("L_" + tab, plate.transform, Loc.Get(labelKey), active ? 27 : 25,
                             active ? Pal.Sun : new Color(1f, .97f, .90f, .92f),
                             TextAnchor.MiddleCenter, new Vector2(BtnW - 16f, 34f),
                             new Vector2(.5f, 0f), new Vector2(0f, 28f), 4f, 3f), 17);

            cell.transform.localScale = Vector3.zero;
            Tween.Pop(cell.transform, 0f, .5f, .62f + Mathf.Abs(x) * .00035f)
                 .OnDone(() => { if (cell) cell.Rehome(); });
        }

        /// <summary>The same colour with more light in it, for the live tab.</summary>
        static Color Lift(Color c, float k)
            => new Color(c.r + (1f - c.r) * k, c.g + (1f - c.g) * k, c.b + (1f - c.b) * k, 1f);

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
                case Tab.Grove: return "ui.nav.grovement";
                case Tab.Ranks: return "ui.nav.ranks";
                default: return "ui.nav.profile";
            }
        }

        /// <summary>
        /// Which cap a tab wears.
        ///
        /// <para>
        /// <b>The Grovement's permanent orange did not survive the kit, and losing it is a
        /// gain.</b> It used to keep a colour of its own whether or not it was the live tab —
        /// the one exception to the teal-means-here rule — because it needed to stand out when
        /// you were standing somewhere else. The kit's unlit cap is a *frame* rather than a
        /// flat jelly, so what makes this tab stand out now is its size alone, which is the
        /// signal that was doing most of the work anyway. A third cap colour in a row of five
        /// would be a second thing saying "here" and disagreeing with the first.
        /// </para>
        /// </summary>
        static string CapSkin(Tab tab, bool active) => active ? Skins.CapOn : Skins.CapOff;

        /// <summary>
        /// The pack's own glyphs — painted pictures rather than silhouettes, so nothing here
        /// tints one and a selected tab cannot black its icon out. Cut by
        /// <c>Tools/make_nav_icons.py</c>, which is where a name is re-pointed.
        /// </summary>
        static Sprite Icon(Tab tab)
        {
            switch (tab)
            {
                case Tab.Home: return Art.S("Ui/ic_nav_home");
                case Tab.Shop: return Art.S("Ui/ic_nav_shop");
                case Tab.Grove: return Art.S("Ui/ic_nav_grove");
                case Tab.Ranks: return Art.S("Ui/ic_nav_ranks");
                default: return Art.S("Ui/ic_nav_profile");
            }
        }

        /// <summary>
        /// Every tab is a screen. A tab whose own screen is the one being drawn returns null
        /// and is left inert rather than re-entering it — <paramref name="standing"/>, which is
        /// not the same as being the marked tab. See <see cref="Build"/>.
        ///
        /// <para>
        /// Ranks was the last promise here and the boards ended it, so there is no
        /// coming-soon branch left. <c>ui.soon.ranks</c> and <c>ui.soon.shop</c> stay in the
        /// string files rather than being deleted, because a key removed from a shipped
        /// language file is a warning in every translation that still carries it.
        /// </para>
        /// </summary>
        static Action Tap(Tab tab, bool standing)
        {
            if (standing) return null;

            switch (tab)
            {
                case Tab.Home: return () => Flow.Go<HomeScreen>();

                // The shop is a screen. It stays a tab rather than becoming a button on the
                // hub because it is one of the two places a player goes deliberately rather
                // than being taken — the Grovement is the other — and because a shop reached
                // only from an out-of-hearts prompt is a shop that is only ever seen at the
                // worst moment to be sold anything.
                case Tab.Shop: return () => Flow.Go<ShopScreen>();

                case Tab.Grove: return () => Flow.Go<HomesteadScreen>();

                // The boards. It was the last tab that opened a panel saying "soon", which is
                // the worst thing to leave in a row of five permanent controls — four of them
                // go somewhere and the fifth teaches the player that this one does not.
                case Tab.Ranks: return () => Flow.Go<LeaderboardScreen>();

                default: return () => Flow.Go<ProfileScreen>();
            }
        }
    }
}
