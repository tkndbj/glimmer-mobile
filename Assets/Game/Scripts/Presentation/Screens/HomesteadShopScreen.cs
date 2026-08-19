using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Everything that can stand in a grove: what the player holds, what play will earn them,
    /// and what credits will buy.
    ///
    /// <para>
    /// A screen rather than a panel, for <c>CompanionScreen</c>'s reason: the catalog is
    /// unbounded — two hundred pieces today and several hundred after a few years of drops —
    /// and a grid that scrolls inside a scrim is a worse place to browse than a page that owns
    /// the display.
    /// </para>
    /// <para>
    /// <b>It pages by shelf, and a shelf is one idea used three times.</b> A tab, an asset
    /// scope and a browse atlas are all the same division of the catalog (see
    /// <c>GroveShelf</c>), so what this screen costs is one shelf's thumbnails whatever the
    /// catalog grows to — and one draw call for the whole grid, because a shelf is one texture.
    /// </para>
    /// <para>
    /// <b>Residents have their own shelf now, and that is a correction.</b> They used to be
    /// pinned to the top of every tab, because a resident fits every kind of slot — so the
    /// fences tab opened on creatures, every tab's asset scope carried the whole roster, and
    /// the one thing on this page that money could not reach was repeated six times. They are
    /// also no longer a private list of five: a resident <em>is</em> a companion (see
    /// <c>GroveResidents</c>), so this shelf and the profile's roster are two views of one
    /// thing — buy Coral in either place and she is yours in both.
    /// </para>
    /// <para>
    /// A cell that is short of credits still opens its panel rather than greying out, which is
    /// the call <c>CompanionUnlockOverlay</c> makes and for the same reason: that is the moment
    /// a player has decided they want something, which is the best moment in the game to offer
    /// a video and the worst to teach them a control is dead.
    /// </para>
    /// </summary>
    public sealed class HomesteadShopScreen : View, IDrawsGroveArt, IDrawsCompanionArt
    {
        public override string Track => "mus_menu";

        const float HeaderHeight = 268f;
        const int Columns = 3;
        const float CellW = 320f;
        const float CellH = 384f;
        const int CellRadius = 30;
        const float TabRow = 104f;

        /// <summary>
        /// Where a cell's picture sits, and why it is computed rather than typed.
        ///
        /// The art used to be pinned a fixed distance from the top of the plate, which left it
        /// riding high in a box whose real bounds are the plate's top edge and the caption's —
        /// so every cell had a band of empty plate under the picture and none above it. This is
        /// the middle of the space the labels actually leave, so a change to either label moves
        /// the art with it instead of quietly unbalancing the cell.
        /// </summary>
        const float PlateH = CellH - 34f;
        const float CaptionTop = 82f + 42f * .5f;
        static readonly float ArtCentre = -(PlateH - (CaptionTop + PlateH) * .5f);
        const float ArtBox = 176f;

        RectTransform _viewport, _tabs;
        GridView _grid;
        Text _summary, _coins;

        readonly List<HomesteadPiece> _items = new List<HomesteadPiece>();

        /// <summary>
        /// What the land shelf shows. A parallel list rather than a piece with a price bolted
        /// on, because a region is genuinely a different thing — it has a size instead of a
        /// picture, and pretending otherwise would put a fake <c>HomesteadPiece</c> into the
        /// one list every other part of this screen trusts.
        /// </summary>
        readonly List<GroveRegion> _land = new List<GroveRegion>();

        bool OnLand => _shelf == GroveShelf.Land;
        readonly Dictionary<GroveShelf, ShelfTab> _tabViews = new Dictionary<GroveShelf, ShelfTab>();

        /// <summary>Which shelf is showing. Reset on every visit, deliberately: a shop that
        /// opens where you left it is a shop that opens somewhere you have to notice.</summary>
        GroveShelf _shelf = GroveShelf.Residents;

        protected override void Build()
        {
            Scenery.Layered(Content, "home", .26f);
            Fireflies.Spawn(Content, 14, new Color(1f, .93f, .70f), 6f, 20f);

            BuildGrid();
            BuildHeader();
            // A side page of the Grovement rather than the Grovement itself, so the tab stays
            // live: the most natural way back from a shop is the thing it belongs to.
            NavBar.Build(Content, NavBar.Tab.Grove, onSidePage: true);

            Warm();

            // Every one of these is a *repaint*, not a rebuild — see GridView.Refresh. That is
            // the difference between "the shop updated" and "the shop flickered".
            HomesteadLedger.Changed += Repaint;
            GroveLand.Changed += Reload;
            HomesteadCatalog.Changed += Reload;
            PlayerProgression.Changed += Repaint;

            // The earned half of every unlock rule is derived from the star ledger, so a run
            // finished in this session changes what this page says without anything here
            // knowing a run happened.
            PlayerProgress.Reloaded += Repaint;
            PlayerProgress.RecordChanged += OnRecord;
        }

        void OnDestroy()
        {
            HomesteadLedger.Changed -= Repaint;
            GroveLand.Changed -= Reload;
            HomesteadCatalog.Changed -= Reload;
            PlayerProgression.Changed -= Repaint;
            PlayerProgress.Reloaded -= Repaint;
            PlayerProgress.RecordChanged -= OnRecord;

            // The grove screen draws from the same scope, so going back does not free art it is
            // about to ask for again — the bargain CompanionScreen makes with the profile. The
            // check itself lives in HomesteadArt, because this screen having it and the grove
            // screen not having it is exactly how the grid ended up empty.
            HomesteadArt.CloseUnlessWanted();
            CompanionArt.CloseUnlessWanted();
        }

        public override bool OnBack() { Flow.Go<HomesteadScreen>(); return true; }

        void OnRecord(LevelRecord record) => Repaint();

        async void Warm()
        {
            await HomesteadService.EnsureAsync();
            if (!this) return;

            Reload();
        }

        /// <summary>
        /// Switches shelf: the grid takes the new list at once, and the art follows.
        ///
        /// <para>
        /// The grid is filled before the atlas arrives on purpose — a tab that does nothing for
        /// a moment reads as a dead control — and every cell hides its own image until its
        /// sprite is in (invariant 7b) rather than flashing white. The second pass is a
        /// <see cref="Repaint"/>, so it rebinds the same cells in place instead of playing the
        /// entrance a second time.
        /// </para>
        /// </summary>
        void Show(GroveShelf shelf)
        {
            if (_shelf == shelf) return;

            _shelf = shelf;

            // No sound here. The tab is a Btn and voices itself on the way down, so this was a
            // second one — and it asked for "tap", which is not an address the game carries, so
            // every tab change threw an InvalidKeyException out of Addressables. Two faults in
            // one line, and the fix for both is the rule Btn already states: one sound per tap.
            Reload();
        }

        // ---------------------------------------------------------------- header
        void BuildHeader()
        {
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .06f, .09f, .82f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            frt.sizeDelta = new Vector2(0f, 318f);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);

            UIKit.IconButton("Back", Content, Skins.Nav, "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, 1f), new Vector2(96f, -132f), () => Flow.Go<HomesteadScreen>());

            var banner = UIKit.Img("Banner", Content, Art.S("Ui/banner"), Color.white,
                                   new Vector2(520f, 140f), new Vector2(.5f, 1f), new Vector2(0f, -128f));
            UIKit.Shrinkable(
                UIKit.Titled("Title", banner.transform, Loc.Get("ui.grove.shop").ToUpperInvariant(), 40,
                             new Color(.36f, .24f, .16f), TextAnchor.MiddleCenter,
                             new Vector2(360f, 58f), new Vector2(.5f, .5f),
                             new Vector2(0f, 140f * UIKit.PillFaceLift), 0f, 2f), 24);

            // The balance, because every price on this page is measured against it and a
            // player deciding between two pieces should not have to leave to find out.
            var pillSize = new Vector2(212f, 76f);
            var pillAnchor = new Vector2(1f, 1f);
            var pill = UIKit.Img("Coins", Content, Art.Round(22), new Color(.04f, .09f, .12f, .80f),
                                 pillSize, pillAnchor, UIKit.Corner(pillSize, pillAnchor, 28f, 94f));
            var edge = UIKit.Img("Edge", pill.transform, Art.RoundOutline(22, 3f), Pal.A(Pal.Gold, .45f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            var coin = UIKit.Img("Icon", pill.transform, null, Color.white, new Vector2(56f, 56f),
                                 new Vector2(0f, .5f), new Vector2(42f, 0f));
            coin.preserveAspect = true;
            Flipbook.Attach(coin, "Ui/Coin", 11f);

            _coins = UIKit.Titled("V", pill.transform, Profile.Short(Profile.Coins), 32, Pal.Cream,
                                  TextAnchor.MiddleCenter, new Vector2(112f, 46f), new Vector2(.5f, .5f),
                                  new Vector2(14f, 0f), 3f, 3f);

            // The shelf's own name lives here rather than under its tab. Eight translated nouns
            // across a 1080 phone is eight truncated words; one, under the tab row, is the line
            // that says which shelf you are looking at — and it is the only place the count of
            // what you hold on it can go without turning every cell into a receipt.
            _summary = UIKit.Shrinkable(
                UIKit.Titled("Summary", Content, string.Empty, 26,
                             new Color(1f, .96f, .88f, .72f), TextAnchor.MiddleCenter,
                             new Vector2(760f, 34f), new Vector2(.5f, 1f), new Vector2(0f, -216f), 3f, 0f), 18);

            BuildTabs();
        }

        /// <summary>
        /// One tab per shelf, drawn as its own art rather than a word, and built exactly once.
        ///
        /// <para>
        /// <b>Built once and restyled</b> — the row used to be destroyed and rebuilt on every
        /// repaint, so eight tabs flashed every time anything on the page changed, including
        /// when its own art arrived. Nothing about a tab depends on the catalog except its
        /// emblem, and an emblem changes only when a content drop lands.
        /// </para>
        /// <para>
        /// A glyph per tab and no label: the picture answers the question better anyway — the
        /// tab that holds fences has a fence on it — and the shelf's name is spelled out under
        /// the row. The one that is showing wears a lit plate and grows; the rest sit back.
        /// Nothing here is a scroll view: eight is the whole vocabulary of the content, and it
        /// is not going to become sixty.
        /// </para>
        /// </summary>
        void BuildTabs()
        {
            _tabs = UIKit.Node("Tabs", Content);
            _tabs.anchorMin = new Vector2(0f, 1f);
            _tabs.anchorMax = new Vector2(1f, 1f);
            _tabs.pivot = new Vector2(.5f, 1f);
            _tabs.sizeDelta = new Vector2(0f, TabRow);

            // Directly under the header, and the viewport starts under *that* — the row is a
            // band of its own rather than an overlay. Placed against the header's height
            // rather than a constant so the two cannot drift apart.
            _tabs.anchoredPosition = new Vector2(0f, -HeaderHeight);

            var shelves = GroveShelves.All;

            // The step is derived from how many shelves there are, not typed. A drop that adds
            // a kind of thing adds a tab, and a row that had been hand-spaced would put it off
            // the edge of the screen.
            float step = Mathf.Min(168f, 1040f / shelves.Length);

            for (int i = 0; i < shelves.Length; i++)
            {
                var shelf = shelves[i];
                float x = (i - (shelves.Length - 1) * .5f) * step;

                _tabViews[shelf] = new ShelfTab(_tabs, shelf, step, x, () => Show(shelf));
            }

            PaintTabs();
        }

        void PaintTabs()
        {
            foreach (var pair in _tabViews) pair.Value.Restyle(pair.Key == _shelf);
        }

        // ------------------------------------------------------------------ grid
        void BuildGrid()
        {
            _viewport = UIKit.Node("Viewport", Content);
            _viewport.offsetMin = new Vector2(0f, NavBar.Height);
            _viewport.offsetMax = new Vector2(0f, -HeaderHeight - TabRow);

            _grid = GridView.Attach(_viewport, Columns, CellW, CellH,
                                    parent => new ShopCell(this, parent));
        }

        /// <summary>
        /// Rebuilds the list this shelf shows and hands it to the grid as a new page.
        ///
        /// Called when the shelf changes, when the catalog is republished, and once when the
        /// body has been read — the three moments the <em>contents</em> of the page differ.
        /// Everything else is a <see cref="Repaint"/>.
        /// </summary>
        void Reload()
        {
            if (_grid == null) return;

            var catalog = HomesteadCatalog.Current;

            _items.Clear();
            _land.Clear();

            if (OnLand)
            {
                // Cheapest first, and owned land last: the shelf is a thing to buy, so what is
                // already bought belongs at the bottom of it rather than in the way.
                foreach (var region in catalog.Floor.Regions)
                    if (region.IsValid && !region.IsStarter) _land.Add(region);

                _land.Sort((a, b) =>
                {
                    bool oa = GroveLand.IsOwned(a), ob = GroveLand.IsOwned(b);
                    return oa != ob ? (oa ? 1 : -1) : a.Cost.CompareTo(b.Cost);
                });
            }
            else if (_shelf == GroveShelf.Home)
            {
                // The ladder collapses to one cell: the rung being offered, or the one the
                // player lives in once they are at the top. Five cells drawing five names over
                // one house read as a bug rather than as a ladder — the ladder belongs on the
                // home panel, where the pips can show it.
                var rung = HomesteadLedger.NextDwelling(catalog);
                if (!rung.IsValid) rung = HomesteadLedger.BestDwelling(catalog);
                if (rung.IsValid) _items.Add(rung);
            }
            else
            {
                // Catalog order, which is the author's order: for decor cheap to expensive, and
                // for residents the keeper ladder, because that is the order a player meets
                // them in. No sort, and therefore no second opinion about the order to drift.
                foreach (var piece in catalog.Pieces)
                    if (GroveShelves.Of(piece) == _shelf) _items.Add(piece);
            }

            PaintTabs();
            PaintSummary();

            _grid.Show(OnLand ? _land.Count : _items.Count);

            // The atlas last, so the grid is on screen before the pictures are. The callback
            // rebinds rather than refilling, so nothing plays its entrance twice.
            HomesteadArt.OpenShelfAsync(_shelf, () => { if (this) Repaint(); });
            if (OnLand) return;
        }

        /// <summary>Redraws what is on screen: same cells, same place, no entrance.</summary>
        void Repaint()
        {
            if (_grid == null) return;

            _grid.Refresh();
            PaintTabs();
            PaintSummary();

            if (_coins) _coins.text = Profile.Short(Profile.Coins);
        }

        void PaintSummary()
        {
            if (!_summary) return;

            if (!HomesteadCatalog.IsLoaded)
            {
                _summary.text = Loc.Get("ui.grove.loading");
                return;
            }

            int held = 0, total;

            if (OnLand)
            {
                foreach (var region in _land)
                    if (GroveLand.IsOwned(region)) held++;

                total = _land.Count;
            }
            else
            {
                foreach (var piece in _items)
                    if (HomesteadLedger.IsHeld(piece)) held++;

                total = _items.Count;
            }

            _summary.text = Loc.Format("ui.grove.shelf", Loc.Get(GroveShelves.NameKey(_shelf)),
                                       held, total);
        }

        // ------------------------------------------------------------------ cell
        /// <summary>
        /// One tab: built once, restyled on every shelf change.
        ///
        /// A small class rather than a rebuild for <c>CompanionScreen.CellView</c>'s reason —
        /// the objects that change are the two whose look actually differs, and everything else
        /// is left alone.
        /// </summary>
        sealed class ShelfTab
        {
            readonly Image _plate, _edge, _mark;
            readonly GroveShelf _shelf;

            public ShelfTab(RectTransform row, GroveShelf shelf, float step, float x, Action onTap)
            {
                _shelf = shelf;

                var cell = UIKit.Button("T_" + shelf, row, Art.Pixel, new Vector2(step - 8f, TabRow),
                                        new Vector2(.5f, .5f), new Vector2(x, 0f), onTap);
                cell.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

                _plate = UIKit.Img("P", cell.transform, Art.Round(22), new Color(.06f, .12f, .16f, .72f),
                                   new Vector2(step - 22f, TabRow - 16f), new Vector2(.5f, .5f),
                                   Vector2.zero);

                _edge = UIKit.Img("E", _plate.transform, Art.RoundOutline(22, 2f),
                                  new Color(1f, .97f, .90f, .12f));
                UIKit.StretchTo((RectTransform)_edge.transform, 0, 0, 0, 0);

                _mark = UIKit.Img("A", _plate.transform, null, Color.white,
                                  new Vector2(TabRow - 40f, TabRow - 40f), new Vector2(.5f, .5f),
                                  Vector2.zero);
                _mark.preserveAspect = true;
                _mark.raycastTarget = false;
            }

            public void Restyle(bool live)
            {
                if (!_plate) return;

                _plate.color = live ? new Color(.10f, .26f, .27f, .96f)
                                    : new Color(.06f, .12f, .16f, .72f);

                _edge.sprite = Art.RoundOutline(22, live ? 3f : 2f);
                _edge.color = live ? Pal.A(Pal.Mint, .70f) : new Color(1f, .97f, .90f, .12f);

                // The emblem comes out of the tab row's own little atlas, so a tab can be drawn
                // before its shelf has been loaded — which is the whole point, since a tab has
                // to be readable before anybody chooses it.
                // Land has no browse atlas — a region is a rectangle, not an object — so its tab
                // wears the generated tile, which is also what its cells draw.
                var mark = _shelf == GroveShelf.Land ? Art.IsoTile(128) : HomesteadArt.ShelfMark(_shelf);
                _mark.sprite = mark;
                _mark.preserveAspect = true;
                _mark.color = mark == null
                    ? new Color(1f, 1f, 1f, 0f)
                    : _shelf == GroveShelf.Land
                        ? (live ? Pal.Verdant : Pal.A(Pal.Verdant, .55f))
                        : (live ? Color.white : new Color(1f, 1f, 1f, .55f));

                if (live) Tween.Pop(_plate.transform, .86f, .3f);
            }
        }

        /// <summary>
        /// One grid cell, built once and rebound as it is recycled.
        ///
        /// <para>
        /// Everything about it that can change with the row is held as a field, because the
        /// alternative — destroying and rebuilding — is what made the shop flicker and what
        /// would make a four-hundred-piece catalog stutter on every tap. See
        /// <see cref="GridView"/>.
        /// </para>
        /// </summary>
        sealed class ShopCell : IGridCell
        {
            readonly HomesteadShopScreen _screen;
            readonly Btn _button;
            readonly Image _plate, _edge, _art, _lock, _leaf;
            readonly Text _name, _status;

            HomesteadPiece _piece;

            public RectTransform Root { get; }

            public ShopCell(HomesteadShopScreen screen, RectTransform parent)
            {
                _screen = screen;

                _button = UIKit.Button("Cell", parent, Art.Pixel,
                                       new Vector2(CellW - 16f, CellH - 20f), new Vector2(.5f, 1f),
                                       Vector2.zero,
                                       () => { if (_region != null) _screen.TapLand(_region);
                                               else _screen.Tap(_piece); });
                _button.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
                Root = (RectTransform)_button.transform;

                _plate = UIKit.Img("Plate", Root, Art.Round(CellRadius), Color.white,
                                   new Vector2(CellW - 28f, CellH - 34f), new Vector2(.5f, .5f),
                                   Vector2.zero);

                _edge = UIKit.Img("Edge", _plate.transform, Art.RoundOutline(CellRadius, 2f), Color.white);
                UIKit.StretchTo((RectTransform)_edge.transform, 0, 0, 0, 0);

                _art = UIKit.Img("A", _plate.transform, null, Color.white,
                                 new Vector2(ArtBox, ArtBox), new Vector2(.5f, 1f),
                                 new Vector2(0f, ArtCentre));
                _art.preserveAspect = true;
                _art.raycastTarget = false;

                _lock = UIKit.Img("Lock", _plate.transform, Art.S("Ui/padlock"), Color.white,
                                  new Vector2(66f, 66f), new Vector2(1f, 1f), new Vector2(-24f, -24f));
                _lock.preserveAspect = true;
                _lock.raycastTarget = false;

                _leaf = UIKit.Img("Leaf", _plate.transform, Art.Leaf(64), Pal.A(Pal.Verdant, .85f),
                                  new Vector2(44f, 44f), new Vector2(0f, 1f), new Vector2(26f, -26f));
                _leaf.raycastTarget = false;

                _name = UIKit.Shrinkable(
                    UIKit.Titled("N", _plate.transform, string.Empty, 30, Pal.Cream,
                                 TextAnchor.MiddleCenter, new Vector2(CellW - 60f, 42f),
                                 new Vector2(.5f, 0f), new Vector2(0f, 82f), 3f, 3f), 17);

                _status = UIKit.Shrinkable(
                    UIKit.Titled("S", _plate.transform, string.Empty, 24, Pal.Cream,
                                 TextAnchor.MiddleCenter, new Vector2(CellW - 52f, 60f),
                                 new Vector2(.5f, 0f), new Vector2(0f, 34f), 3f, 0f), 16);
            }

            GroveRegion _region;

            public void Bind(int index)
            {
                if (_screen.OnLand) { BindLand(index); return; }

                _region = null;
                _piece = index >= 0 && index < _screen._items.Count ? _screen._items[index] : default;

                bool held = HomesteadLedger.IsHeld(_piece);

                // A locked plate is *lighter* than a held one, which looks backwards and is not.
                // The art on it is the thing that has to read, and half this catalog is dark —
                // a brown log or a bramble on a near-black plate is a black rectangle, which is
                // exactly what shipped. Held cells are marked out by their mint edge, their
                // caption and the absence of a padlock, none of which depend on the plate.
                _plate.color = held ? new Color(.07f, .16f, .17f, .93f)
                                    : new Color(.11f, .18f, .24f, .90f);

                // A home wears gold whether or not it is held, because the ladder is the one
                // thing on this page a player is meant to be saving for rather than browsing.
                _edge.sprite = Art.RoundOutline(CellRadius, _piece.IsDwelling ? 4f : held ? 3f : 2f);
                _edge.color = _piece.IsDwelling ? Pal.A(Pal.Gold, .70f)
                            : held ? Pal.A(Pal.Mint, .55f)
                                   : new Color(1f, .97f, .90f, .14f);

                // Locked art draws in **its own colours**, barely knocked back. Tinting it
                // toward a grey silhouette was the obvious idea and it is wrong here: a tint
                // multiplies, so it only ever darkens, and the pieces that most need to be
                // recognised before you buy them — a fallen log, brambles, a cave — are the dark
                // ones. A shop whose locked half is unreadable is a shop that cannot sell
                // anything. The padlock says "not yours"; the picture says what it is.
                _art.color = held ? Color.white : new Color(.88f, .92f, .96f, 1f);
                HomesteadArt.PaintThumb(_art, _piece);

                _lock.gameObject.SetActive(!held && _piece.IsValid);

                // The leaf marks what play alone will reach, which on the residents' shelf is
                // most of it — the half of this page that money is not the only way through.
                _leaf.gameObject.SetActive(_piece.IsValid && !held && _piece.HasRequirement);

                _name.text = _piece.IsValid ? Loc.Get(_piece.NameKey) : string.Empty;
                _name.color = held ? Pal.Cream : new Color(1f, .95f, .88f, .62f);

                var (line, tint) = StatusOf(_piece, held);
                _status.text = line;
                _status.color = tint;
            }

            /// <summary>
            /// One stretch of ground: how big it is, what it costs, and whether it is already
            /// yours. No picture, because a region is a rectangle — the tile glyph stands in for
            /// it and the size line is what the player is actually judging.
            /// </summary>
            void BindLand(int index)
            {
                _piece = default;
                _region = index >= 0 && index < _screen._land.Count ? _screen._land[index] : null;

                bool owned = GroveLand.IsOwned(_region);

                _plate.color = owned ? new Color(.07f, .16f, .17f, .93f)
                                     : new Color(.11f, .18f, .24f, .90f);

                _edge.sprite = Art.RoundOutline(CellRadius, owned ? 3f : 2f);
                _edge.color = owned ? Pal.A(Pal.Mint, .55f) : new Color(1f, .97f, .90f, .14f);

                _art.color = owned ? Pal.A(Pal.Verdant, .85f) : Pal.A(Pal.Verdant, .55f);
                var running = _art.GetComponent<Flipbook>();
                if (running) { running.enabled = false; UnityEngine.Object.Destroy(running); }
                _art.sprite = Art.IsoTile(256);

                _lock.gameObject.SetActive(false);
                _leaf.gameObject.SetActive(false);

                _name.text = _region == null ? string.Empty : Loc.Get(_region.NameKey);
                _name.color = owned ? Pal.Cream : new Color(1f, .95f, .88f, .82f);

                if (_region == null) { _status.text = string.Empty; return; }

                _status.text = owned
                    ? Loc.Format("ui.land.size", _region.Cols, _region.Rows)
                    : Loc.Format("ui.grove.price", _region.Cost);

                _status.color = owned
                    ? Pal.A(Pal.Mint, .95f)
                    : Profile.CanAfford(_region.Cost) ? Pal.A(Pal.Sun, .95f) : Pal.A(Pal.Sun, .58f);
            }
        }

        /// <summary>
        /// The one line under a piece, and there is exactly one because a cell that stacks a
        /// price over a requirement over a balance is a receipt.
        ///
        /// Each state renders a different sentence, which is <c>AdOfferState</c>'s bargain: a
        /// single "locked" would draw the same caption for a piece 40 credits away and one
        /// that will never be for sale, and only one of those resolves by playing for an hour.
        /// </summary>
        static (string, Color) StatusOf(HomesteadPiece piece, bool held)
        {
            if (!piece.IsValid) return (string.Empty, Pal.Cream);

            // A home is never "yours" in the sense the rest of the grid means it — the player
            // always has one. What the cell has to say is whether this is the next one up.
            if (piece.IsDwelling)
                return held
                    ? (Loc.Get("ui.grove.home_best"), Pal.A(Pal.Gold, .95f))
                    : (Loc.Format("ui.grove.price", piece.Cost),
                       Profile.CanAfford(piece.Cost) ? Pal.A(Pal.Sun, .95f) : Pal.A(Pal.Sun, .58f));

            if (held) return (Loc.Get("ui.grove.yours"), Pal.A(Pal.Mint, .95f));

            // The free route first, and the price second, wherever both exist — which is
            // CompanionUnlockOverlay's rule and for its reason: a panel that leads with the
            // price reads as a paywall on something the player was going to be given anyway.
            if (piece.RequiresKeeperLevel > 0 && !piece.IsForSale)
                return (Loc.Format("ui.grove.needs_level", piece.RequiresKeeperLevel),
                        Pal.A(Pal.Aqua, .95f));

            if (piece.RequiresLevel.IsValid)
                return (Loc.Format("ui.grove.needs_glade", LevelName(piece.RequiresLevel)),
                        Pal.A(Pal.Aqua, .95f));

            if (piece.RequiresChapter.IsValid)
                return (Loc.Format("ui.grove.needs_chapter", ChapterName(piece.RequiresChapter)),
                        Pal.A(Pal.Aqua, .95f));

            if (piece.IsForSale)
                return (Loc.Format("ui.grove.price", piece.Cost),
                        Profile.CanAfford(piece.Cost) ? Pal.A(Pal.Sun, .95f) : Pal.A(Pal.Sun, .58f));

            // Left over: no requirement, no price, and not held — which the catalog cannot
            // produce, since a piece with neither is a starter. Said plainly rather than left
            // blank, so a content mistake shows up on the screen it broke.
            return (Loc.Get("ui.grove.not_for_sale"), new Color(1f, .96f, .88f, .55f));
        }

        /// <summary>
        /// A glade's name from its id alone, with no file read — which is invariant 5a
        /// earning its keep. This screen names up to two hundred requirements and would
        /// otherwise have to load every chapter body to do it.
        /// </summary>
        static string LevelName(LevelId id) => Loc.Get(LevelDefinition.DefaultNameKey(id));

        static string ChapterName(ChapterId id)
        {
            var chapter = GameContent.FindChapter(id);
            return chapter != null ? Loc.Get(chapter.NameKey) : Loc.Get("ui.grove.soon");
        }

        /// <summary>
        /// Ground: owned land says so, and anything else opens the panel that sells it.
        ///
        /// Short of credits still opens it rather than greying the cell, which is this shop's
        /// rule everywhere — that is the moment a player has decided they want something.
        /// </summary>
        void TapLand(GroveRegion region)
        {
            if (GroveLand.IsOwned(region))
            {
                Scenery.Toast(Content, Loc.Format("ui.land.owned", Loc.Get(region.NameKey)), Pal.Mint);
                return;
            }

            Flow.Modal<GroveLandOverlay>(v => v.Region = region);
        }

        void Tap(HomesteadPiece piece)
        {
            if (!piece.IsValid) return;

            // A home goes to the home panel in every state — held, next, or five rungs away.
            // The question at a house is never "shall I buy this one item"; it is "where am I
            // on the ladder", and that panel is the only thing that answers it.
            if (piece.IsDwelling) { Flow.Modal<HomesteadHomeOverlay>(); return; }

            if (HomesteadLedger.IsHeld(piece))
            {
                Scenery.Toast(Content, Loc.Format("ui.grove.already", Loc.Get(piece.NameKey)), Pal.Mint);
                return;
            }

            // A resident is a companion, so it is offered by the companion's own panel — one
            // ceremony, one set of numbers, and a reveal the player has seen before. Wearing is
            // switched off: somebody buying a friend to stand by their pond has said nothing
            // about who they want on their nameplate.
            if (piece.IsResident)
            {
                var companion = GroveResidents.CompanionOf(piece);
                if (companion.IsValid)
                {
                    Flow.Modal<CompanionUnlockOverlay>(v => { v.Avatar = companion; v.WearOnBuy = false; });
                    return;
                }
            }

            if (!piece.IsForSale)
            {
                var (line, _) = StatusOf(piece, false);
                Scenery.Toast(Content, line, Pal.Aqua);
                return;
            }

            Flow.Modal<HomesteadBuyOverlay>(v => v.Piece = piece);
        }
    }
}
