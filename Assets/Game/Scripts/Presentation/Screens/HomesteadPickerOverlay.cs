using System;
using System.Collections.Generic;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// What to put in one slot: everything the player holds, and the option to take away
    /// whatever is there.
    ///
    /// <para>
    /// A modal rather than a screen, which is the opposite of the call
    /// <c>CompanionScreen</c> and <see cref="HomesteadShopScreen"/> both make — and for a
    /// reason those two do not have. Placing is not browsing: the player is looking at a
    /// specific gap in a composition they can see behind this panel, and the answer to "what
    /// goes here" depends on what is next to it. A screen would take the grove away at
    /// exactly the moment it is the thing being decided about.
    /// </para>
    /// <para>
    /// It lists what is <b>held</b>, never what exists. What exists is the shop's subject,
    /// and a picker showing padlocks would be a shop with worse browsing that also loses the
    /// player's place. The one door between them is the button at the foot.
    /// </para>
    /// <para>
    /// <b>And it lists only what fits.</b> A slot has a kind — see <c>HomesteadSlotKind</c> —
    /// so the rim offers fences and the flower bed offers flowers. That is what turns placing
    /// into composing: before it, every slot accepted everything, so the only decision on offer
    /// was which of eleven interchangeable dots got which sticker and every grove came out
    /// looking equally accidental. A slot whose kind the player owns nothing for says so and
    /// points at the shop, rather than opening an empty grid.
    /// </para>
    /// <para>
    /// <b>A piece already standing somewhere else is still offered.</b> Holding a piece is
    /// permission to draw it, not possession of a copy — see <see cref="HomesteadPiece"/> —
    /// so a fence can line a whole plot. Hiding placed pieces would be an inventory the save
    /// file deliberately does not have.
    /// </para>
    /// </summary>
    public sealed class HomesteadPickerOverlay : ModalView
    {
        /// <summary>
        /// The slot being filled. Set by the caller before Build runs.
        ///
        /// A property rather than a field for <c>CompanionUnlockOverlay.Avatar</c>'s reason:
        /// <see cref="HomesteadSlot"/> is not <c>[Serializable]</c>, so a public field of that
        /// type earns a UAC1001 warning about serialisation that will never happen.
        /// </summary>
        public HomesteadSlot Slot { get; set; }

        const float PanelW = 960f;
        const float PanelH = 1180f;
        const float HeadRoom = 190f;
        const float FootRoom = 150f;

        const int Columns = 4;
        const float CellW = 214f;
        const float CellH = 214f;
        const int CellRadius = 26;

        RectTransform _viewport, _grid;

        protected override void Build()
        {
            MakePanel(new Vector2(PanelW, PanelH), Loc.Get("ui.grove.place"));

            BuildGrid();

            UIKit.IconButton("Close", Panel, Skins.Nav, "ic_close", new Vector2(96f, 96f),
                             new Vector2(1f, 1f), new Vector2(-46f, -46f), () => Close());

            var shop = UIKit.TextButton("Shop", Panel, "btn_orange", Loc.Get("ui.grove.shop_more"), 28,
                                        new Vector2(440f, 96f), new Vector2(.5f, 0f), new Vector2(0f, 82f),
                                        () => Close(() => Flow.Go<HomesteadShopScreen>()));
            UIKit.Shrinkable(shop.Label, 18);

            Paint();

            // Only this slot's kind, which is all this panel can show — and it is the same
            // scope the shop pages by, so opening a picker over a shop tab of the same kind
            // costs nothing. The art may still be arriving either way: this panel can be
            // opened in the same second the screen behind it was, and an Image with no sprite
            // is a white rectangle.
            HomesteadArt.OpenKindAsync(Slot.Kind, () => { if (this) Paint(); });

            // A purchase made through the shop cannot reach here (the shop is a screen and
            // this closes first), but a piece earned by a run finishing elsewhere can, and a
            // content refresh can republish the catalog under an open panel.
            HomesteadLedger.Changed += Paint;
            HomesteadCatalog.Changed += Paint;
        }

        void OnDestroy()
        {
            HomesteadLedger.Changed -= Paint;
            HomesteadCatalog.Changed -= Paint;
        }

        public override bool OnBack() { Close(); return true; }

        // ------------------------------------------------------------------ grid
        void BuildGrid()
        {
            _viewport = UIKit.Node("Viewport", Panel);
            _viewport.offsetMin = new Vector2(40f, FootRoom);
            _viewport.offsetMax = new Vector2(-40f, -HeadRoom);

            var catcher = _viewport.gameObject.AddComponent<Image>();
            catcher.color = new Color(0, 0, 0, 0);
            catcher.raycastTarget = true;
            _viewport.gameObject.AddComponent<RectMask2D>();

            _grid = UIKit.Node("Grid", _viewport);
            _grid.anchorMin = new Vector2(0f, 1f);
            _grid.anchorMax = new Vector2(1f, 1f);
            _grid.pivot = new Vector2(.5f, 1f);
            _grid.anchoredPosition = Vector2.zero;

            var scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = _grid;
            scroll.viewport = _viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = .14f;
            scroll.inertia = true;
            scroll.decelerationRate = .04f;
            scroll.scrollSensitivity = 55f;
        }

        void Paint()
        {
            if (_grid == null) return;

            for (int i = _grid.childCount - 1; i >= 0; i--)
            {
                var old = _grid.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }

            var catalog = HomesteadCatalog.Current;
            string standing = HomesteadLayout.At(Slot.Id);

            var held = new List<HomesteadPiece>();
            foreach (var piece in catalog.Pieces)
                if (piece.Fits(Slot.Kind) && HomesteadLedger.IsHeld(piece)) held.Add(piece);

            // Residents first, because they are the half of the catalog the player earned and
            // the half nobody can buy. Decor then follows in catalog order, which is the
            // author's order — cheap and small to large and expensive.
            //
            // The position map is built once rather than searched inside the comparator: the
            // catalog is unbounded by design, and a linear scan per comparison is the shape
            // that is free at forty pieces and a visible stall at four hundred.
            var order = new Dictionary<string, int>(catalog.PieceCount, StringComparer.Ordinal);
            for (int i = 0; i < catalog.Pieces.Count; i++) order[catalog.Pieces[i].Id] = i;

            held.Sort((a, b) => a.IsResident != b.IsResident
                                    ? (a.IsResident ? -1 : 1)
                                    : order[a.Id].CompareTo(order[b.Id]));

            int index = 0;

            // "Take it away" leads, and only when there is something to take. A clear button
            // sitting first on an empty slot would be the panel's most prominent control doing
            // nothing, which is how a player learns to stop reading the first row.
            if (!string.IsNullOrEmpty(standing))
                Cell(index++, default, false, Loc.Get("ui.grove.clear"), () => Choose(string.Empty));

            foreach (var piece in held)
            {
                var chosen = piece;
                Cell(index++, piece, string.Equals(piece.Id, standing, StringComparison.Ordinal),
                     Loc.Get(piece.NameKey), () => Choose(chosen.Id));
            }

            // Nothing the player owns belongs here. Said plainly, with the kind named, because
            // an empty grid is indistinguishable from a broken one — and this is the only place
            // in the feature that can explain what a slot is for without labelling all eleven
            // of them on the island itself.
            if (held.Count == 0)
            {
                // Brown, not cream: this is the only text in the panel that sits on the paper
                // rather than on one of the dark plates, and cream on cream is the mistake the
                // beacon's gold-out-of-gold ring already made once.
                UIKit.Shrinkable(
                    UIKit.Titled("Hint", _grid, Loc.Get(FitsKey(Slot.Kind)), 28,
                                 new Color(.36f, .24f, .16f, .85f), TextAnchor.UpperCenter,
                                 new Vector2(PanelW - 180f, 120f), new Vector2(.5f, 1f),
                                 new Vector2(0f, -70f), 0f, 0f, true), 19);
            }

            int rows = Mathf.Max(1, (index + Columns - 1) / Columns);
            _grid.sizeDelta = new Vector2(0f, rows * CellH + 30f);
        }

        void Cell(int index, HomesteadPiece piece, bool standing, string label, Action onTap)
        {
            float x = (index % Columns - (Columns - 1) * .5f) * CellW;
            float y = -(index / Columns) * CellH - CellH * .5f - 8f;

            var cell = UIKit.Button("C" + index, _grid, Art.Pixel, new Vector2(CellW - 12f, CellH - 12f),
                                    new Vector2(.5f, 1f), new Vector2(x, y), onTap);
            cell.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

            // Lifted off near-black for HomesteadShopScreen's reason: a fallen log or a bramble
            // drawn on a very dark plate is a dark rectangle, and this panel is where somebody
            // picks between forty of them.
            var plate = UIKit.Img("Plate", cell.transform, Art.Round(CellRadius),
                                  standing ? new Color(.10f, .24f, .26f, .96f)
                                           : new Color(.12f, .19f, .25f, .88f),
                                  new Vector2(CellW - 26f, CellH - 26f), new Vector2(.5f, .5f), Vector2.zero);

            var edge = UIKit.Img("Edge", plate.transform, Art.RoundOutline(CellRadius, standing ? 4f : 2f),
                                 standing ? Pal.Gold : new Color(1f, .97f, .90f, .16f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            if (piece.IsValid)
            {
                var art = UIKit.Img("A", plate.transform, null, Color.white,
                                    new Vector2(122f, 122f), new Vector2(.5f, 1f), new Vector2(0f, -22f));
                art.preserveAspect = true;
                art.raycastTarget = false;
                HomesteadArt.Paint(art, piece);
            }
            else
            {
                // The clear cell. A cross rather than an empty plate, because "nothing" needs a
                // shape or it reads as a cell whose art has not loaded.
                var cross = UIKit.Img("X", plate.transform, Art.S("Ui/ic_close"),
                                      new Color(1f, .96f, .88f, .70f),
                                      new Vector2(74f, 74f), new Vector2(.5f, 1f), new Vector2(0f, -44f));
                cross.preserveAspect = true;
                cross.raycastTarget = false;
            }

            UIKit.Shrinkable(
                UIKit.Titled("N", plate.transform, label, 24,
                             standing ? Pal.Cream : new Color(1f, .96f, .88f, .82f),
                             TextAnchor.MiddleCenter, new Vector2(CellW - 44f, 54f), new Vector2(.5f, 0f),
                             new Vector2(0f, 34f), 3f, 2f), 15);

            cell.transform.localScale = Vector3.zero;
            Tween.Pop(cell.transform, 0f, .42f, .02f * Mathf.Min(index, 14));
        }

        /// <summary>
        /// The sentence for a slot whose kind the player owns nothing for.
        ///
        /// Written out per kind rather than composed from a noun, for invariant 6's reason —
        /// a key built by concatenation is a key the build gate cannot scan for.
        /// </summary>
        static string FitsKey(HomesteadSlotKind kind)
        {
            switch (kind)
            {
                case HomesteadSlotKind.Structure: return "ui.grove.fits_structure";
                case HomesteadSlotKind.Canopy: return "ui.grove.fits_canopy";
                case HomesteadSlotKind.Bed: return "ui.grove.fits_bed";
                case HomesteadSlotKind.Path: return "ui.grove.fits_path";
                case HomesteadSlotKind.Edge: return "ui.grove.fits_edge";
                default: return "ui.grove.fits_ground";
            }
        }

        void Choose(string pieceId)
        {
            // Place first, close second. The screen behind repaints on HomesteadLayout.Changed,
            // so by the time the panel has faded the slot is already showing what was chosen —
            // which is the whole feedback for the tap.
            if (HomesteadLayout.Place(Slot.Id, pieceId))
            {
                Audio.Sfx("pop", .6f);

                // The piece is being drawn from *this* panel's scope, which the next tab
                // switch or the walk back to the hub will release. Claiming moves it into the
                // grove's own scope, where it belongs now that it is standing on an island.
                HomesteadArt.Claim(HomesteadCatalog.Current.Find(pieceId));
            }

            Close();
        }
    }
}
