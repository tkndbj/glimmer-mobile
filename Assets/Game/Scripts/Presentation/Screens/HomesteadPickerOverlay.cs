using GlimmerGrove.AssetPipeline;
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

        /// <summary>The art this panel draws, held for exactly as long as it is up.</summary>
        AssetHold _held;
        /// <summary>
        /// What to do with the piece the player picks. Set by the caller before Build runs.
        ///
        /// <para>
        /// <b>This panel used to place things, and it no longer does.</b> It was opened on a
        /// tile and wrote into it, which made it a per-tile picker: it needed to know what was
        /// standing there, it had a "take it away" row of its own, and it had to offer a piece
        /// whose last copy was the one being looked at. It is an <em>inventory</em> now — the
        /// catalogue of what the player holds — so it hands the choice back and closes, and the
        /// grove turns it into a ghost the player drags (see <c>GroveDraftView</c>). Every one
        /// of those special cases went with the slot.
        /// </para>
        /// <para>
        /// A property rather than a field for <c>CompanionUnlockOverlay.Avatar</c>'s reason: a
        /// public delegate field earns a UAC1001 warning about serialisation that will never
        /// happen.
        /// </para>
        /// </summary>
        public System.Action<string> Chosen { get; set; }

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
            _held = GroveArtLoader.Open("grove_picker", GroveArtLoader.Picker(), this, Repaint);

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

            _held?.Dispose();
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

            _offer.Clear();

            // No "take it away" row. Taking something away is done to the thing itself — tap it
            // on the floor and the draft offers it — which is where it belongs: this panel is
            // no longer opened *at* anything, so it has nothing to clear.

            // Residents first, because they are the half of the catalog nobody can be sold
            // outright and the half a player is proudest of. Decor then follows in catalog
            // order, which is the author's order — cheap and small to large and expensive.
            // Two passes rather than a sort: the catalog is already in the order both halves
            // want, so sorting would be a second opinion about it that a drop could break.
            foreach (var piece in catalog.Pieces)
                if (piece.IsResident && Offerable(piece)) _offer.Add(piece);

            foreach (var piece in catalog.Pieces)
                if (!piece.IsResident && Offerable(piece)) _offer.Add(piece);

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
        /// <summary>
        /// Whether a piece belongs on the shelf: one the player holds and has a copy of.
        ///
        /// <para>
        /// It used to take the id standing on the slot this panel was opened at, and offer that
        /// one whatever the stock said — because a copy is only ever out of stock by standing in
        /// the grove, so the piece being looked at was always at nought. That clause went with
        /// the slot: the panel is not opened at anything now, and a piece whose copies are all
        /// out in the grove is moved by tapping it there rather than by finding it in here.
        /// </para>
        /// </summary>
        static bool Offerable(HomesteadPiece piece)
            => piece.CanBePlaced
            && HomesteadLedger.IsHeld(piece)
            && (!piece.IsStocked || HomesteadLedger.Available(piece) > 0);

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
            void PaintStock()
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
                _name.color = new Color(1f, .96f, .88f, .82f);
            }

            public void Bind(int index)
            {
                _piece = index >= 0 && index < _panel._items.Count ? _panel._items[index] : default;

                // Nothing is "the one standing here" any more: the panel is a catalogue rather
                // than a picker aimed at a tile, so the tick has nothing to mark.
                _art.gameObject.SetActive(_piece.IsValid);
                _cross.gameObject.SetActive(!_piece.IsValid);
                _tick.gameObject.SetActive(false);

                if (_piece.IsValid) HomesteadArt.PaintThumb(_art, _piece);

                _name.text = _piece.IsValid ? Loc.Get(_piece.NameKey) : Loc.Get("ui.grove.clear");

                PaintStock();
            }
        }

        /// <summary>
        /// Hands the choice back to the grove and closes.
        ///
        /// <para>
        /// Nothing is written here. The player has said <em>what</em>, and the grove asks
        /// <em>where</em> with a ghost they can drag, turn and confirm — so a tap in this panel
        /// can never put something down in a place they did not look at. That was the old
        /// behaviour and it is the reason the footprint mismatch went unnoticed for so long: a
        /// piece appeared somewhere near where the panel was opened and the player had no
        /// picture of what it would take.
        /// </para>
        /// <para>
        /// Out of stock goes and buys more rather than doing nothing, which is the one case
        /// worth keeping from the old path: the player has just told us exactly what they want,
        /// and that is the worst possible moment to teach them a control does not work. It is
        /// reachable only from a list that has gone stale under an open panel, because a
        /// depleted piece is taken off the list rather than dimmed (invariant 16l).
        /// </para>
        /// </summary>
        void Choose(HomesteadPiece piece)
        {
            if (!piece.IsValid) { Close(); return; }

            if (piece.IsStocked && HomesteadLedger.Available(piece) <= 0)
            {
                // Closed first and the buy panel raised from the continuation, so it lands over
                // the grove rather than over a picker that is about to be destroyed.
                Close(() => Flow.Modal<HomesteadBuyOverlay>(v => v.Piece = piece));
                return;
            }

            // The full-size art the ghost needs is asked for by the *screen*, out of the hold
            // it owns — see HomesteadScreen.Take. This panel used to reach into a global scope
            // and raise an event to tell the grove about it, which is one object writing into
            // another's memory and hoping it noticed.
            string id = piece.Id;
            Close(() => Chosen?.Invoke(id));
        }
    }
}
