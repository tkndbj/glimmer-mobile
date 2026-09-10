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

        /// <summary>Side padding on the grid's viewport, which is what leaves room for three.</summary>
        const float SidePad = 40f;

        /// <summary>
        /// Three across, and as wide as three will go — the shop's own grid, which this panel
        /// is a smaller window onto.
        ///
        /// <para>
        /// It was four at 214, which is what a card has to shrink to before four of them fit a
        /// 960-wide panel: a 112-unit picture of a fence, a name shrunk to fifteen point, and a
        /// grid that read as a list of icons rather than as the shelf the player had just come
        /// from. Three is the number the card was designed at (<see cref="PieceCard"/>), so the
        /// only thing that differs between here and the shop is how much room there is to draw
        /// it in.
        /// </para>
        /// </summary>
        const int Columns = 3;

        /// <summary>
        /// Derived rather than typed, because it is exactly "what is left over": a mistyped
        /// number here is a column drawn off the side of the panel, which is the one fault in a
        /// grid nothing else can see.
        /// </summary>
        const float CellW = (PanelW - SidePad * 2f) / Columns;
        const float CellH = CellW;

        RectTransform _viewport;
        GridView _grid;
        Text _hint;

        /// <summary>
        /// What the grid is showing. An invalid entry is the "take it away" cell, which is why
        /// this is a list of pieces rather than a list of ids: the clear option has no id and
        /// inventing one would put an empty string through the same paths a real piece takes.
        /// </summary>
        readonly List<HomesteadPiece> _items = new List<HomesteadPiece>();

        /// <summary>
        /// Where the next offer is assembled before it is compared with the one on screen.
        /// Held rather than allocated per call: this runs on every sync while the panel is open.
        /// </summary>
        readonly List<HomesteadPiece> _offer = new List<HomesteadPiece>();

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
            _viewport.offsetMin = new Vector2(SidePad, FootRoom);
            _viewport.offsetMax = new Vector2(-SidePad, -HeadRoom);

            _grid = GridView.Attach(_viewport, Columns, CellW, CellH,
                                    parent => new PickerCell(this, parent), 8f, 30f);
        }

        /// <summary>
        /// Works out what is on offer, and hands it to the grid as a new page only when it is
        /// genuinely a different one.
        ///
        /// <para>
        /// <b>The comparison is the point rather than a saving.</b> <c>GridView.Show</c> resets
        /// the scroll and replays the entrance, which is right for a list that has changed and
        /// wrong for one that has not — and this is subscribed to
        /// <c>HomesteadLedger.Changed</c>, which every sync raises by adopting a merge whether
        /// or not anything moved. A placement asks for a sync, so a panel left open a few
        /// seconds after one used to throw the player back to the top of the list for nothing.
        /// </para>
        /// </summary>
        void Reload()
        {
            if (_grid == null) return;

            var catalog = HomesteadCatalog.Current;
            string standing = HomesteadLayout.At(Slot.Id);

            _offer.Clear();

            // "Take it away" leads, and only when there is something to take. A clear button
            // sitting first on an empty slot would be the panel's most prominent control doing
            // nothing, which is how a player learns to stop reading the first row.
            if (!string.IsNullOrEmpty(standing)) _offer.Add(default);

            // Residents first, because they are the half of the catalog nobody can be sold
            // outright and the half a player is proudest of. Decor then follows in catalog
            // order, which is the author's order — cheap and small to large and expensive.
            // Two passes rather than a sort: the catalog is already in the order both halves
            // want, so sorting would be a second opinion about it that a drop could break.
            foreach (var piece in catalog.Pieces)
                if (piece.IsResident && Offerable(piece, standing)) _offer.Add(piece);

            foreach (var piece in catalog.Pieces)
                if (!piece.IsResident && Offerable(piece, standing)) _offer.Add(piece);

            // Nothing the player owns belongs here. Said plainly, with the kind named, because
            // an empty grid is indistinguishable from a broken one — and this is the only place
            // in the feature that can explain what a slot is for without labelling all eleven
            // of them on the island itself.
            if (_hint) _hint.gameObject.SetActive(_offer.Count == 0);

            if (Same(_items, _offer)) { Repaint(); return; }

            _items.Clear();
            _items.AddRange(_offer);

            _grid.Show(_items.Count);
        }

        /// <summary>
        /// Whether a piece belongs on the shelf: held, placeable, and — for anything sold by
        /// the copy — with a copy left to place.
        ///
        /// <para>
        /// <b>A piece with none left is taken off the list rather than dimmed, and that is the
        /// owner's call over this panel's first design.</b> It used to stay, wearing "None left
        /// — tap to buy", on the argument that a piece which vanished when it ran out would
        /// read as a piece that had been taken away. What playing it found is the other half of
        /// that trade: the row a player is choosing from fills up with things they cannot
        /// choose, and a dead-looking cell among live ones reads as the panel being broken
        /// rather than as an offer. What is lost is a route to the shop that the button at the
        /// foot of this panel already provides.
        /// </para>
        /// <para>
        /// <b>What is standing on this very tile is offered whatever the stock says</b>, and
        /// that clause is load-bearing rather than kind: a copy is only spent because it is out
        /// in the grove, so a player who placed their last fence here would otherwise open the
        /// panel and find no cell marked as the one they are looking at — and no way to take it
        /// away except the cross, which says nothing about what is there.
        /// </para>
        /// </summary>
        static bool Offerable(HomesteadPiece piece, string standing)
        {
            if (!piece.CanBePlaced || !HomesteadLedger.IsHeld(piece)) return false;
            if (!piece.IsStocked || HomesteadLedger.Available(piece) > 0) return true;

            return string.Equals(piece.Id, standing, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether two offers are the same pieces in the same order.
        ///
        /// By id, because that is what a cell draws from; the "take it away" entry is an
        /// invalid piece whose id is null, which compares equal to itself and to nothing else.
        /// </summary>
        static bool Same(List<HomesteadPiece> a, List<HomesteadPiece> b)
        {
            if (a.Count != b.Count) return false;

            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i].Id, b[i].Id, StringComparison.Ordinal)) return false;

            return true;
        }

        /// <summary>Redraws the cells in place: for art arriving, and for a list that has not moved.</summary>
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
            readonly Image _plate, _art, _cross, _tick;
            readonly Text _name, _stock;

            HomesteadPiece _piece;

            public RectTransform Root { get; }

            public PickerCell(HomesteadPickerOverlay panel, RectTransform parent)
            {
                _panel = panel;

                // **The shop's card, built by the one thing that knows how.** This used to be a
                // drawn rounded box with a traced outline over it, tinted per state — the shape
                // this UI drew before it had a kit — so a player walked out of a shelf of kit
                // cards into a grid of something else, one tap away. See <see cref="PieceCard"/>.
                var cell = PieceCard.Touch("Cell", parent, CellW, () => _panel.Choose(_piece));
                Root = (RectTransform)cell.transform;

                _plate = PieceCard.Plate(Root, CellW);

                _art = PieceCard.Picture(_plate, CellW);

                // A cross rather than an empty plate, because "nothing" needs a shape or it
                // reads as a cell whose art has not loaded. Drawn where the picture would be
                // and at the size the shop's padlock is, so the two read as the same kind of
                // statement about a card.
                _cross = PieceCard.Glyph("X", _plate, CellW, Art.S("Ui/ic_close"),
                                         new Color(1f, .96f, .88f, .70f), .58f);

                _name = PieceCard.Name(_plate, CellW);
                _stock = PieceCard.Line(_plate, CellW);

                // **What is standing on this tile wears a badge rather than a gold rim.** The
                // rim was how this said it while the plate was a drawn box, and a card that
                // carries its own keyline has nothing to trace — the shop met the same problem
                // and answered it with a corner mark, so this answers it the same way. Built
                // last so it paints over everything, and in the corner opposite the shop's
                // leaf, which costs nothing and keeps two different marks in two places.
                _tick = UIKit.Img("Here", _plate.transform, Art.S("Ui/ic_check"), Pal.Gold,
                                  new Vector2(48f, 48f) * PieceCard.ScaleFor(CellW),
                                  new Vector2(1f, 1f),
                                  new Vector2(-30f, -30f) * PieceCard.ScaleFor(CellW));
                _tick.preserveAspect = true;
                _tick.raycastTarget = false;
            }

            /// <summary>
            /// How many of this one are left to place.
            ///
            /// <para>
            /// Nothing is drawn for a resident or an earned piece: those cannot run out, and a
            /// count on one would be inviting the player to worry about a number that has no
            /// meaning.
            /// </para>
            /// <para>
            /// <b>There is no "none left" line here any more.</b> A piece with nothing left to
            /// place is not on the shelf at all (see <see cref="Offerable"/>), and the one that
            /// is offered with none left — the piece standing on this very tile — is already
            /// marked as standing here, which is the true thing to say about it. A count of
            /// nought under it would be the same cell saying two things, one of them useless.
            /// </para>
            /// </summary>
            void PaintStock(bool standing)
            {
                bool stocked = _piece.IsStocked;
                int left = stocked ? HomesteadLedger.Available(_piece) : 0;

                _stock.gameObject.SetActive(stocked && left > 0);
                if (_stock.gameObject.activeSelf)
                {
                    _stock.text = Loc.Format("ui.grove.stock_left", left);
                    _stock.color = Pal.A(Pal.Mint, .95f);
                }

                _art.color = Color.white;
                _name.color = standing ? Pal.Cream : new Color(1f, .96f, .88f, .82f);
            }

            public void Bind(int index)
            {
                _piece = index >= 0 && index < _panel._items.Count ? _panel._items[index] : default;

                bool standing = _piece.IsValid
                    && string.Equals(_piece.Id, HomesteadLayout.At(_panel.Slot.Id), StringComparison.Ordinal);

                _art.gameObject.SetActive(_piece.IsValid);
                _cross.gameObject.SetActive(!_piece.IsValid);
                _tick.gameObject.SetActive(standing);

                if (_piece.IsValid) HomesteadArt.PaintThumb(_art, _piece);

                _name.text = _piece.IsValid ? Loc.Get(_piece.NameKey) : Loc.Get("ui.grove.clear");

                PaintStock(standing);
            }
        }

        void Choose(HomesteadPiece piece)
        {
            // Tapping what is already here means "yes, that one" — so the panel simply goes.
            // It is asked before the stock branch below and that ordering is the whole of it:
            // a piece is only ever out of stock because its copies are standing in the grove,
            // so the one standing on this very tile is always at nought, and without this the
            // player would tap the thing they are looking at and be sold another (see
            // Offerable, which is why it is on the shelf at all).
            if (piece.IsValid
                && string.Equals(piece.Id, HomesteadLayout.At(Slot.Id), StringComparison.Ordinal))
            {
                Close();
                return;
            }

            // Nothing left to place: this goes and buys more rather than doing nothing. A dead
            // cell is the refusal HintPrompt exists to prevent one screen over — the player has
            // just told us exactly what they want, which is the worst possible moment to teach
            // them that a control does not work.
            //
            // Reachable only from a shelf that has gone stale under an open panel — the last
            // copy spent on another device, or a merge landing — because a depleted piece is
            // taken off the list rather than dimmed. Kept because a panel that is a few seconds
            // out of date is an ordinary thing, and because a tap that does nothing at all is
            // the one answer this must never give.
            //
            // Closed first and the panel raised from the continuation, so the buy panel lands
            // over the grove rather than over a picker that is about to be destroyed —
            // HomesteadBuyOverlay.OnBuy's ordering, for its reason.
            if (piece.IsStocked && HomesteadLedger.Available(piece) <= 0)
            {
                Close(() => Flow.Modal<HomesteadBuyOverlay>(v => v.Piece = piece));
                return;
            }

            // Place first, close second. The screen behind repaints on HomesteadLayout.Changed,
            // so by the time the panel has faded the slot is already showing what was chosen —
            // which is the whole feedback for the tap. The footprint is fitted around the tile
            // the player touched, so a two-wide piece lands wherever it fits beside it.
            var result = HomesteadLayout.TryPlace(HomesteadCatalog.Current, Slot.Col, Slot.Row,
                                                  piece.IsValid ? piece.Id : string.Empty,
                                                  out _, out _);

            if (result == GrovePlaceResult.Placed)
            {
                Audio.Sfx("pop", .6f);

                // This panel draws thumbnails; an island draws the real thing. Claiming loads
                // the piece's own art into the grove's scope, where it belongs now that it is
                // standing on an island — without it the slot would be empty until the whole
                // grove was reloaded.
                HomesteadArt.Claim(piece);
            }

            // A refusal is said by the grove, after this panel has gone: a picker that closes
            // over a floor that did not change teaches the player the control is broken, and a
            // toast under a closing panel is a toast nobody reads.
            if (result == GrovePlaceResult.NoRoom)
            {
                Close(() => (Flow.Current as HomesteadScreen)?.SayNoRoom());
                return;
            }

            Close();
        }
    }
}
