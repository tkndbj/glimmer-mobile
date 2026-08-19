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
    /// <b>It lists everything held, because every tile takes everything.</b> On the islands a
    /// slot had a role and this panel showed only what suited it — the rim offered fences, the
    /// flower bed offered flowers. That rule existed to stop a sprinkle of pre-placed dots
    /// looking accidental; the floor has no dots, so where a thing goes is the player's
    /// decision and narrowing the list would be taking the feature back out. See
    /// <c>GroveFloor</c>.
    /// </para>
    /// <para>
    /// Which means the panel now loads every shelf's browse atlas rather than two. That is the
    /// one real cost of the change and it is bounded by the number of shelves rather than by
    /// the catalog — eight small thumbnail pages, not eight shelves of real art.
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

        /// <summary>
        /// The middle of the space the caption leaves. See <c>HomesteadShopScreen</c>'s note —
        /// this one was worse: the box was pinned 22 pixels below the plate's top edge with a
        /// centre pivot, so its upper half hung 39 pixels off the plate entirely.
        /// </summary>
        const float PlateH = CellH - 26f;
        const float CaptionTop = 34f + 54f * .5f;
        static readonly float ArtCentre = -(PlateH - (CaptionTop + PlateH) * .5f);
        const float ArtBox = 112f;

        RectTransform _viewport;
        GridView _grid;
        Text _hint;

        /// <summary>
        /// What the grid is showing. An invalid entry is the "take it away" cell, which is why
        /// this is a list of pieces rather than a list of ids: the clear option has no id and
        /// inventing one would put an empty string through the same paths a real piece takes.
        /// </summary>
        readonly List<HomesteadPiece> _items = new List<HomesteadPiece>();

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

            // Brown, not cream: this is the only text in the panel that sits on the paper
            // rather than on one of the dark plates, and cream on cream is the mistake the
            // beacon's gold-out-of-gold ring already made once.
            _hint = UIKit.Shrinkable(
                UIKit.Titled("Hint", Panel, Loc.Get("ui.grove.fits_ground"), 28,
                             new Color(.36f, .24f, .16f, .85f), TextAnchor.UpperCenter,
                             new Vector2(PanelW - 180f, 120f), new Vector2(.5f, 1f),
                             new Vector2(0f, -HeadRoom - 40f), 0f, 0f, true), 19);

            Reload();

            // Every shelf, because every tile takes everything. The art may still be arriving:
            // this panel can be opened in the same second the screen behind it was, and an
            // Image with no sprite is a white rectangle.
            HomesteadArt.OpenPickerAsync(() => { if (this) Repaint(); });

            // A purchase made through the shop cannot reach here (the shop is a screen and
            // this closes first), but a piece earned by a run finishing elsewhere can, and a
            // content refresh can republish the catalog under an open panel.
            HomesteadLedger.Changed += Reload;
            HomesteadCatalog.Changed += Reload;
        }

        void OnDestroy()
        {
            HomesteadLedger.Changed -= Reload;
            HomesteadCatalog.Changed -= Reload;
        }

        public override bool OnBack() { Close(); return true; }

        // ------------------------------------------------------------------ grid
        void BuildGrid()
        {
            _viewport = UIKit.Node("Viewport", Panel);
            _viewport.offsetMin = new Vector2(40f, FootRoom);
            _viewport.offsetMax = new Vector2(-40f, -HeadRoom);

            _grid = GridView.Attach(_viewport, Columns, CellW, CellH,
                                    parent => new PickerCell(this, parent), 8f, 30f);
        }

        /// <summary>Rebuilds what is on offer. See <see cref="Repaint"/> for the cheaper half.</summary>
        void Reload()
        {
            if (_grid == null) return;

            var catalog = HomesteadCatalog.Current;
            string standing = HomesteadLayout.At(Slot.Id);

            _items.Clear();

            // "Take it away" leads, and only when there is something to take. A clear button
            // sitting first on an empty slot would be the panel's most prominent control doing
            // nothing, which is how a player learns to stop reading the first row.
            if (!string.IsNullOrEmpty(standing)) _items.Add(default);

            // Residents first, because they are the half of the catalog nobody can be sold
            // outright and the half a player is proudest of. Decor then follows in catalog
            // order, which is the author's order — cheap and small to large and expensive.
            // Two passes rather than a sort: the catalog is already in the order both halves
            // want, so sorting would be a second opinion about it that a drop could break.
            foreach (var piece in catalog.Pieces)
                if (piece.IsResident && piece.CanBePlaced && HomesteadLedger.IsHeld(piece))
                    _items.Add(piece);

            foreach (var piece in catalog.Pieces)
                if (!piece.IsResident && piece.CanBePlaced && HomesteadLedger.IsHeld(piece))
                    _items.Add(piece);

            // Nothing the player owns belongs here. Said plainly, with the kind named, because
            // an empty grid is indistinguishable from a broken one — and this is the only place
            // in the feature that can explain what a slot is for without labelling all eleven
            // of them on the island itself.
            if (_hint) _hint.gameObject.SetActive(_items.Count == 0);

            _grid.Show(_items.Count);
        }

        /// <summary>Redraws the cells in place: for art arriving, and nothing else.</summary>
        void Repaint()
        {
            if (_grid != null) _grid.Refresh();
        }

        /// <summary>
        /// One cell, built once and rebound as it is recycled — <see cref="GridView"/>'s
        /// bargain. The clear option is a cell like any other, drawn from an invalid piece.
        /// </summary>
        sealed class PickerCell : IGridCell
        {
            readonly HomesteadPickerOverlay _panel;
            readonly Image _plate, _edge, _art, _cross;
            readonly Text _name;

            HomesteadPiece _piece;

            public RectTransform Root { get; }

            public PickerCell(HomesteadPickerOverlay panel, RectTransform parent)
            {
                _panel = panel;

                var cell = UIKit.Button("Cell", parent, Art.Pixel,
                                        new Vector2(CellW - 12f, CellH - 12f), new Vector2(.5f, 1f),
                                        Vector2.zero, () => _panel.Choose(_piece));
                cell.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
                Root = (RectTransform)cell.transform;

                // Lifted off near-black for HomesteadShopScreen's reason: a fallen log or a
                // bramble on a very dark plate is a dark rectangle, and this panel is where
                // somebody picks between forty of them.
                _plate = UIKit.Img("Plate", Root, Art.Round(CellRadius), Color.white,
                                   new Vector2(CellW - 26f, CellH - 26f), new Vector2(.5f, .5f),
                                   Vector2.zero);

                _edge = UIKit.Img("Edge", _plate.transform, Art.RoundOutline(CellRadius, 2f), Color.white);
                UIKit.StretchTo((RectTransform)_edge.transform, 0, 0, 0, 0);

                _art = UIKit.Img("A", _plate.transform, null, Color.white,
                                 new Vector2(ArtBox, ArtBox), new Vector2(.5f, 1f),
                                 new Vector2(0f, ArtCentre));
                _art.preserveAspect = true;
                _art.raycastTarget = false;

                // A cross rather than an empty plate, because "nothing" needs a shape or it
                // reads as a cell whose art has not loaded.
                _cross = UIKit.Img("X", _plate.transform, Art.S("Ui/ic_close"),
                                   new Color(1f, .96f, .88f, .70f),
                                   new Vector2(74f, 74f), new Vector2(.5f, 1f),
                                   new Vector2(0f, ArtCentre));
                _cross.preserveAspect = true;
                _cross.raycastTarget = false;

                _name = UIKit.Shrinkable(
                    UIKit.Titled("N", _plate.transform, string.Empty, 24, Pal.Cream,
                                 TextAnchor.MiddleCenter, new Vector2(CellW - 44f, 54f),
                                 new Vector2(.5f, 0f), new Vector2(0f, 34f), 3f, 2f), 15);
            }

            public void Bind(int index)
            {
                _piece = index >= 0 && index < _panel._items.Count ? _panel._items[index] : default;

                bool standing = _piece.IsValid
                    && string.Equals(_piece.Id, HomesteadLayout.At(_panel.Slot.Id), StringComparison.Ordinal);

                _plate.color = standing ? new Color(.10f, .24f, .26f, .96f)
                                        : new Color(.12f, .19f, .25f, .88f);

                _edge.sprite = Art.RoundOutline(CellRadius, standing ? 4f : 2f);
                _edge.color = standing ? Pal.Gold : new Color(1f, .97f, .90f, .16f);

                _art.gameObject.SetActive(_piece.IsValid);
                _cross.gameObject.SetActive(!_piece.IsValid);

                if (_piece.IsValid) HomesteadArt.PaintThumb(_art, _piece);

                _name.text = _piece.IsValid ? Loc.Get(_piece.NameKey) : Loc.Get("ui.grove.clear");
                _name.color = standing ? Pal.Cream : new Color(1f, .96f, .88f, .82f);
            }
        }

        void Choose(HomesteadPiece piece)
        {
            // Place first, close second. The screen behind repaints on HomesteadLayout.Changed,
            // so by the time the panel has faded the slot is already showing what was chosen —
            // which is the whole feedback for the tap.
            if (HomesteadLayout.Place(Slot.Id, piece.IsValid ? piece.Id : string.Empty))
            {
                Audio.Sfx("pop", .6f);

                // This panel draws thumbnails; an island draws the real thing. Claiming loads
                // the piece's own art into the grove's scope, where it belongs now that it is
                // standing on an island — without it the slot would be empty until the whole
                // grove was reloaded.
                HomesteadArt.Claim(piece);
            }

            Close();
        }
    }
}
