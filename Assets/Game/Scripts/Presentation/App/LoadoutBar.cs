using System;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Localization;
using GlimmerGrove.Utilities;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// What the player is taking into the next siege, along the foot of the map: the four turrets
    /// standing on the line, and the five kit slots under them.
    ///
    /// <para>
    /// <b>A readout that is also the door.</b> The map used to carry a LOADOUT button in the
    /// bottom corner, which said the feature existed and nothing about what was in it — so the one
    /// screen where somebody is about to choose a level told them nothing about what they were
    /// choosing it with. This says both: it shows the line, and tapping anywhere on it opens the
    /// shelf. The button is gone, because two doors to one room is one door too many.
    /// </para>
    /// <para>
    /// <b>Two rows, and they are deliberately not the same size.</b> A turret is a permanent choice
    /// out of twenty and a kit is a consumable that runs out, so the line reads first and the kits
    /// read as what is under it. Both rows are evenly spread across the whole width rather than
    /// packed to a side, which is the only arrangement that stays centred when the safe area
    /// changes shape.
    /// </para>
    /// <para>
    /// <b>The same furniture as the action bar in a run</b> (<c>UtilityBar</c>): the same well, the
    /// same shelf, the same badge in the same corner. A kit in the corner of the map and the same
    /// kit in the corner of a board should not be two different objects — and it costs no art.
    /// </para>
    /// <para>
    /// <b>It bleeds to the bottom of the screen and stands its cells <c>UtilityBar.Foot</c> above
    /// it</b>, because a shelf that stops short of the edge leaves a strip of map under it that
    /// scrolls, a shelf that gives the inset up in full puts the kit row on the home indicator,
    /// and a shelf that honours it in full is 94 units of empty plate — which is the gap that was
    /// reported off an iPhone. <see cref="Height"/> is what a caller must inset a scroller by; it
    /// already includes the foot.
    /// </para>
    /// </summary>
    public sealed class LoadoutBar : MonoBehaviour
    {
        /// <summary>How many kit slots the row draws, filled or not. <c>UtilityBar.Slots</c>.</summary>
        public const int Kits = UtilityBar.Slots;

        const float Pad = 18f, Gap = 12f;

        /// <summary>
        /// How big a cell is and how far apart they sit.
        ///
        /// <b>Packed rather than spread across the width.</b> Even shares of the whole bar put a
        /// hand's width of nothing between four cells, which reads as four things that happen to
        /// be on the same shelf rather than as a line. A fixed gap and a centred row is the
        /// arrangement that makes them one object — and it lets the cells be as big as they should
        /// be, which even shares could not without touching.
        /// </summary>
        const float TurretCell = 208f, KitCell = 164f;
        const float TurretGap = 14f, KitGap = 12f;

        /// <summary>The tab on the top edge, which is what says the shelf is a way in.</summary>
        const float TabW = 300f, TabH = 66f;

        /// <summary>What the bar is, before the display's own foot is added to it.</summary>
        const float Bare = Pad + TurretCell + Gap + KitCell + Pad;

        /// <summary>
        /// How much room the bar takes at the foot of the screen, the display's foot included.
        ///
        /// <para>
        /// <b>Read rather than assumed by whatever is above it.</b> A scroller inset by the bare
        /// height leaves its last row under a home indicator on the phones that have one, which is
        /// invisible on every device without.
        /// </para>
        /// <para>
        /// <b><c>UtilityBar.Foot</c> rather than the whole inset, and that is a lesson this bar
        /// was written after and did not take.</b> It honoured <c>SafeArea.Bottom</c> in full — 94
        /// units of it on an iPhone — and every one of those units was empty shelf under the kit
        /// row, which is the *same report from the same device* the action bar had already
        /// answered: a gap at the foot of the screen. The answer there is a ceiling rather than
        /// the inset, and it is one number for both bars because they are one shelf drawn in two
        /// places (invariant 42b) — the cells stand clear of the indicator pill and nothing else
        /// is spent on it.
        /// </para>
        /// </summary>
        public static float Height => Bare + UtilityBar.Foot;

        /// <summary>The turret cells, left to right, in the order <c>WardLine.Colours</c> names.</summary>
        readonly List<Image> _turrets = new List<Image>(WardLine.Colours.Length);

        /// <summary>One kit cell: the picture and the count over it, or nothing at all.</summary>
        sealed class Kit
        {
            public UtilityItem Item;
            public Image Face, Badge;
            public Text Count;
        }

        readonly List<Kit> _kits = new List<Kit>(Kits);

        /// <summary>
        /// Builds the bar along the bottom of <paramref name="host"/>.
        ///
        /// <b>Built once and repainted, never rebuilt</b> — <c>GridView</c>'s rule (invariant 16d):
        /// a repaint that destroys its cells replays their entrance, and both ledgers here raise
        /// <c>Changed</c> on load and on every sync.
        /// </summary>
        public static LoadoutBar Build(RectTransform host, Action tapped)
        {
            var rt = UIKit.Node("LoadoutBar", host);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(.5f, 0f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0f, Height);

            var bar = rt.gameObject.AddComponent<LoadoutBar>();
            bar.Compose(rt, tapped);
            return bar;
        }

        void Compose(RectTransform rt, Action tapped)
        {
            // **The whole bar is the button.** Nine controls that all do the same thing is nine
            // chances to miss; one surface that opens the shelf is the affordance the corner
            // button used to be, in the place a player is already looking.
            // **The kit's own navy plate**, which is what every other surface in this UI a
            // player reads something off is drawn on (`Skins.PlateBlue`, invariant 44h) — the
            // action bar's tray is the board's furniture and read as a different screen under a
            // map.
            var shelf = UIKit.Button("Shelf", rt, Art.S("Ui/" + Skins.PlateBlue), Vector2.zero,
                                     new Vector2(.5f, .5f), Vector2.zero, tapped);
            var srt = (RectTransform)shelf.transform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = Vector2.zero;

            // A press-scale on something that fills the foot of the screen reads as the screen
            // flinching, so the shelf answers a tap with its sound and nothing else.
            shelf.PressScale = 1f;

            // Measured from the physical bottom of the display, exactly as the action bar's
            // cells are: the shelf runs to the edge and the cells stand `Foot` above it.
            float foot = UtilityBar.Foot;
            float kitMid = foot + Pad + KitCell * .5f;
            float turretMid = kitMid + KitCell * .5f + Gap + TurretCell * .5f;

            // **A tab on the top edge rather than a caption inside the bar.** It is the one thing
            // here that has to say *this is a control*, so it sits proud of the shelf where a tab
            // on a folder does — and it is orange, which is this UI's own colour for a way
            // forward (`Skins.Buy`, the shop's own buy face).
            // **A button rather than a picture, and it has to be its own.** It stands *proud of*
            // the bar's top edge, so it is outside the shelf's rect — and uGUI raycasts a rect,
            // not a drawing. Left as an image it was the one part of the bar that looked most like
            // a control and was the only part that answered nothing.
            var tab = UIKit.Button("Tab", rt, Art.S("Ui/" + Skins.Buy), new Vector2(TabW, TabH),
                                   new Vector2(.5f, 1f), new Vector2(0f, TabH * .5f - 8f), tapped);

            UIKit.Titled("Caption", tab.transform,
                         Loc.Get("ui.loadout.title").ToUpperInvariant(), 30, Pal.Cream,
                         TextAnchor.MiddleCenter, new Vector2(TabW - 70f, TabH - 18f),
                         new Vector2(.5f, .5f), new Vector2(14f, TabH * UIKit.PillFaceLift), 0f, 3f);

            var gear = UIKit.Img("Gear", tab.transform, Art.S("Ui/ic_gear"), Pal.Cream,
                                 Vector2.one * 32f, new Vector2(0f, .5f),
                                 new Vector2(40f, TabH * UIKit.PillFaceLift));
            gear.preserveAspect = true;
            gear.raycastTarget = false;

            BuildTurrets(rt, turretMid);
            BuildKits(rt, kitMid);

            // Both ledgers, and the loadout as well: a turret bought elsewhere, a kit spent in a
            // run and a line re-stood on the shelf all change what this says. Detached first,
            // because a subscription that assumes one build per screen doubles the day somebody
            // rebuilds one.
            WardLoadout.Changed -= Paint;
            WardLoadout.Changed += Paint;
            UtilityLedger.Changed -= Paint;
            UtilityLedger.Changed += Paint;

            Paint();
            Dress();
        }

        void OnDestroy()
        {
            WardLoadout.Changed -= Paint;
            UtilityLedger.Changed -= Paint;
            AssetLibrary.ReleaseScope(BarScope);
        }

        /// <summary>
        /// Its own scope for the four turret bodies.
        ///
        /// <b>Never <c>AssetLibrary.LineScope</c></b>, which belongs to whichever board is up:
        /// taking it here would release a live run's turrets when the map was left (invariant 7b's
        /// second rule — an address owned by another scope is never re-claimed).
        /// </summary>
        const string BarScope = "map_loadout";

        // ------------------------------------------------------------------ the rows
        void BuildTurrets(RectTransform rt, float mid)
        {
            int n = WardLine.Colours.Length;
            float step = TurretCell + TurretGap;

            for (int i = 0; i < n; i++)
            {
                // Centred and packed, so the four read as one line. See `TurretCell`.
                float x = (i - (n - 1) * .5f) * step;

                // **The shop's own card frame**, which is what every other thing in this game a
                // player owns or wants is drawn in - a turret in the same frame as a product is a
                // turret that reads as belonging to the same shelf.
                var cell = UIKit.Img("Turret" + i, rt, Art.S("Ui/" + Skins.Card), Color.white,
                                     Vector2.one * TurretCell, new Vector2(.5f, 0f),
                                     new Vector2(x, mid));

                // The colour of the seat, drawn behind the picture. It is the one thing this row
                // has to say that a turret's own silhouette does not: which of the four it is
                // standing on.
                var rim = UIKit.Img("Rim", cell.transform, Art.RoundOutline(26, 5f),
                                    Pal.A(SiegeView.TintOf(i), .85f),
                                    Vector2.one * (TurretCell - 10f));
                rim.raycastTarget = false;

                var body = UIKit.Img("Body", cell.transform, null, Color.white,
                                     Vector2.one * (TurretCell - 44f));
                body.preserveAspect = true;
                body.raycastTarget = false;

                // An `Image` with no sprite is a white rectangle rather than a blank (invariant
                // 7b), so it is off until the scope lands.
                body.enabled = false;

                _turrets.Add(body);
            }
        }

        void BuildKits(RectTransform rt, float mid)
        {
            var items = UtilityLedger.Catalog.Items;
            float step = KitCell + KitGap;

            for (int i = 0; i < Kits; i++)
            {
                float x = (i - (Kits - 1) * .5f) * step;

                var cell = UIKit.Img("Kit" + i, rt, Art.S("Ui/" + Skins.Card), Color.white,
                                     Vector2.one * KitCell, new Vector2(.5f, 0f),
                                     new Vector2(x, mid));

                var kit = new Kit { Item = i < items.Count ? items[i] : null };

                if (kit.Item != null)
                {
                    kit.Face = UIKit.Img("Face", cell.transform, Art.S(kit.Item.Art), Color.white,
                                         Vector2.one * (KitCell - 44f));
                    kit.Face.preserveAspect = true;
                    kit.Face.raycastTarget = false;

                    // Top-right, over the cell's own rim, which is where the action bar puts it —
                    // the corner nothing else uses.
                    kit.Badge = UIKit.Img("Badge", cell.transform, Art.Disc(96), Pal.Ink,
                                          Vector2.one * 56f, new Vector2(1f, 1f),
                                          new Vector2(-4f, 4f));
                    kit.Badge.raycastTarget = false;

                    // Shrinkable, because the ceiling is a hundred and a `UIKit.Label` that
                    // overflows is not clipped — it keeps drawing out over the cell beside it.
                    kit.Count = UIKit.Shrinkable(
                        UIKit.Label("Count", kit.Badge.transform, "0", 32, Pal.Cream,
                                    TextAnchor.MiddleCenter, Vector2.one * 50f,
                                    new Vector2(.5f, .5f), Vector2.zero, FontStyle.Bold), 16);
                }

                _kits.Add(kit);
            }
        }

        // ------------------------------------------------------------------ painting
        /// <summary>
        /// What is standing and what is carried, now.
        ///
        /// <b>A kit nobody holds shows an empty well rather than a dimmed picture</b>, because the
        /// question this row answers is what the player is taking in — and a greyed icon answers
        /// "what exists" instead, which is the shop's question and is asked one screen away.
        /// </summary>
        void Paint()
        {
            if (this == null) return;

            for (int i = 0; i < _kits.Count; i++)
            {
                var kit = _kits[i];
                if (kit.Item == null) continue;

                int held = UtilityLedger.Held(kit.Item);

                if (kit.Face != null) kit.Face.enabled = held > 0;
                if (kit.Badge != null) kit.Badge.enabled = held > 0;
                if (kit.Count != null) kit.Count.text = held.ToString();
            }

            Dress();
        }

        /// <summary>Puts whichever turret bodies are already in hand onto the row.</summary>
        void Dress()
        {
            var line = WardLoadout.Line;

            for (int i = 0; i < _turrets.Count; i++)
            {
                var model = line.At(i);
                if (model == null) continue;

                var sprite = AssetLibrary.Sprite(AssetManifest.WardArt(model, i));

                _turrets[i].sprite = sprite;
                _turrets[i].enabled = sprite != null;
            }
        }

        /// <summary>
        /// Loads the four turrets standing on the line, and dresses the row when they land.
        ///
        /// <b><c>async void</c> with the exception caught</b>, which is <c>CompanionArt.Load</c>'s
        /// shape and for its reason: a scope that failed to load must not vanish silently, and the
        /// bar behind it is already drawn.
        /// </summary>
        public async void Load()
        {
            var line = WardLoadout.Line;
            var wanted = new List<AssetRequest>(WardLine.Colours.Length);

            for (int i = 0; i < WardLine.Colours.Length; i++)
            {
                var model = line.At(i);
                if (model != null) wanted.Add(AssetRequest.Sprite(AssetManifest.WardArt(model, i)));
            }

            try { await AssetLibrary.EnsureScopeAsync(BarScope, wanted); }
            catch (Exception e) { Debug.LogException(e); return; }

            if (this != null) Dress();
        }
    }
}
