using GlimmerGrove.AssetPipeline;
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
    public sealed class HomesteadShopScreen : View
    {

        /// <summary>The tab row's emblems, which outlive every shelf shown in the row.</summary>
        AssetHold _tabArt;

        /// <summary>The shelf on show. Refilled as the player changes tab, never replaced.</summary>
        AssetHold _shelfArt;
        public override string Track => "mus_menu";

        const float HeaderHeight = 268f;
        const int Columns = 3;
        /// <summary>
        /// Three across the screen, and as wide as three will go. 344 leaves 24 units of margin
        /// at each edge, which is what the grid actually needs; it was 320 and left 60 a side —
        /// a band of nothing on a screen whose whole job is showing pictures of things to buy.
        /// </summary>
        const float CellW = 344f;

        /// <summary>
        /// Square, and the same shape the money shop's cards are. A grid of two different
        /// rectangles across two shops one tap apart is two designs; and a piece of decor has
        /// no natural aspect of its own to argue for one, since what a cell holds is a picture,
        /// a name and a line.
        /// </summary>
        const float CellH = CellW;
        const float TabRow = 150f;

        /// <summary>
        /// How wide one shelf tab is drawn, whatever the catalog holds. See <c>BuildTabs</c> for
        /// why this is a constant rather than the screen's width over the number of shelves.
        /// </summary>
        const float TabStep = 178f;

        /// <summary>
        /// Breathing room under the last row. There is no nav bar here — the back arrow and
        /// the back key are the way out, so the grid runs to the bottom of the safe area
        /// rather than stopping short of a control that is not there.
        /// </summary>
        const float BottomPad = 24f;

        RectTransform _viewport, _tabs;
        GridView _grid;
        Text _summary;

        readonly List<HomesteadPiece> _items = new List<HomesteadPiece>();

        /// <summary>
        /// What the land shelf shows. A parallel list rather than a piece with a price bolted
        /// on, because a region is genuinely a different thing — it has a size instead of a
        /// picture, and pretending otherwise would put a fake <c>HomesteadPiece</c> into the
        /// one list every other part of this screen trusts.
        /// </summary>
        readonly List<GroveRegion> _land = new List<GroveRegion>();

        /// <summary>
        /// Where <see cref="Reload"/> builds the next page before deciding whether it is a new
        /// one. Kept as fields rather than made per call so a screen that repaints on four
        /// events does not allocate a list on each of them.
        /// </summary>
        readonly List<HomesteadPiece> _nextItems = new List<HomesteadPiece>();
        readonly List<GroveRegion> _nextLand = new List<GroveRegion>();

        /// <summary>
        /// Which shelf the grid is currently paged to, or null before it has been filled at all.
        ///
        /// <b>It is not the same question as <see cref="_shelf"/>.</b> Two shelves can hold two
        /// lists that compare equal to a stale one — the land list survives a trip through the
        /// fences tab untouched, so coming back to it would compare equal to itself and be
        /// <em>refreshed</em> into a grid still sized and scrolled for fences. A list comparison
        /// alone cannot see that; what changed is not the page's contents but which page the
        /// grid is holding.
        /// </summary>
        GroveShelf? _paged;

        bool OnLand => _shelf == GroveShelf.Land;
        readonly Dictionary<GroveShelf, ShelfTab> _tabViews = new Dictionary<GroveShelf, ShelfTab>();

        /// <summary>Which shelf is showing. Reset on every visit, deliberately: a shop that
        /// opens where you left it is a shop that opens somewhere you have to notice.</summary>
        GroveShelf _shelf = GroveShelf.Residents;

        protected override void Build()
        {
            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 14, new Color(1f, .93f, .70f), 6f, 20f);

            BuildGrid();
            BuildHeader();

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
            _tabArt?.Dispose();
            _shelfArt?.Dispose();
        }

        public override bool OnBack() { Flow.Go<HomesteadScreen>(); return true; }

        void OnRecord(LevelRecord record) => Repaint();

        void Warm() => Run(async token =>
        {
            await HomesteadService.EnsureAsync();
            if (Living) Reload();
        });

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
            RevealTab();
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

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, 1f), new Vector2(96f, -132f), () => Flow.Go<HomesteadScreen>());

            // The money shop's header, exactly - the kit's ribbon with the word bent to its
            // own curve and lifted onto the flag. Two shops one tap apart should not be two
            // designs, and this was a wooden banner with brown lettering: the last thing on
            // either screen still wearing the look that came before the kit.
            var banner = UIKit.Img("Banner", Safe, Art.S("Ui/" + Skins.Title), Color.white,
                                   new Vector2(440f, 104f), new Vector2(.5f, 1f), new Vector2(0f, -112f));
            UIKit.Arced("Title", banner.transform, Loc.Get("ui.grove.shop").ToUpperInvariant(), 40,
                        Pal.Sun, 620f, new Vector2(.5f, .5f),
                        new Vector2(0f, 104f * Skins.RibbonLift), 3f, 3f, 2f);

            // The balance, because every price on this page is measured against it and a
            // player deciding between two pieces should not have to leave to find out.
            // The money shop's balance pill, to the unit: the kit's trough drawn at white, the
            // hub's own spinning coin 62 in from the edge (the trough clips 43 of each end
            // whatever width it is drawn at), and no "+" - the panel a plus would open is the
            // screen you are already standing on.
            var pillSize = new Vector2(228f, 74f);
            var pillAnchor = new Vector2(1f, 1f);
            var pill = UIKit.Img("Coins", Safe, Art.S("Ui/" + Skins.Trough), Color.white,
                                 pillSize, pillAnchor, UIKit.Corner(pillSize, pillAnchor, 28f, 94f));

            var glow = UIKit.Img("Glow", pill.transform, Art.Glow(96, 2f), Pal.A(Pal.Gold, .22f),
                                 new Vector2(96f, 96f), new Vector2(0f, .5f), new Vector2(62f, 0f));
            glow.raycastTarget = false;

            var coin = UIKit.Img("Icon", pill.transform, null, Color.white, new Vector2(48f, 48f),
                                 new Vector2(0f, .5f), new Vector2(62f, 0f));
            coin.preserveAspect = true;
            Flipbook.Attach(coin, "Ui/Coin", 11f);

            var coins = UIKit.Titled("V", pill.transform, Compact.Number(Profile.Coins), 30, Pal.Cream,
                                     TextAnchor.MiddleCenter, new Vector2(120f, 44f), new Vector2(.5f, .5f),
                                     new Vector2(26f, 0f), 3f, 3f);

            // **Registered rather than written by hand**, which is what every other balance pill
            // in the game does and what this one was the last to do. It buys two things: the
            // repaint stops being this screen's to remember (`WalletWatch`), and a payout drawn
            // over this screen — gems bought from a land region's own out-of-funds panel arrive
            // through the receipt queue — has somewhere to fly to. A cascade with no registered
            // slot pays silently.
            ResourceSlots.Register(ResourceSlots.Kind.Credits, (RectTransform)coin.transform,
                                   coins, glow, Pal.Gold, Compact.Number);

            WalletWatch.Attach(this, ResourceSlots.Kind.Credits);

            // **The shelf's name moved onto its tab and this line kept the count.** The old
            // argument against naming the tabs was that nine translated nouns across a phone is
            // nine truncated words - and the money shop names five, so the shape had to match.
            // What makes it fit is that these nouns are short by design ("trees", "paths",
            // "friends") on a plate 100 units wide. What could never go on a tab is how much of
            // the shelf you already hold, which is the one number somebody comparing two pieces
            // wants, so that is all this says now.
            _summary = UIKit.Shrinkable(
                UIKit.Titled("Summary", Safe, string.Empty, 26, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(760f, 34f), new Vector2(.5f, 1f), new Vector2(0f, -226f), 3f, 0f), 18);

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
        /// <b>It scrolls sideways, and that is what let the tabs be a proper size.</b> The row
        /// used to divide the screen's width by however many shelves there were — nine of them
        /// at 115 units each, which is a plate barely wider than the emblem on it and a name
        /// shrunk to eleven point. Dividing by the count is the right rule for a row that must
        /// fit; it is the wrong rule the moment the row is allowed not to. Every tab is
        /// <see cref="TabStep"/> wide now whatever the catalog holds, and a drop that adds a
        /// kind of thing makes the row longer rather than making every tab thinner.
        /// </para>
        /// <para>
        /// The scroll is only armed when the row is actually wider than the screen, so eight
        /// shelves or four sit still and centred exactly as they did.
        /// </para>
        /// </summary>
        void BuildTabs()
        {
            var shelves = GroveShelves.Tabs;

            float span = shelves.Length * TabStep;
            bool scrolls = span > Boot.RefWidth;

            // The viewport is the band; the row inside it is as long as the tabs need. A
            // `RectMask2D` rather than a `Mask`, for `GridView`'s reason: it costs no extra
            // draw call and it clips by rectangle, which is all a strip needs.
            var band = UIKit.Node("TabBand", Safe);
            band.anchorMin = new Vector2(0f, 1f);
            band.anchorMax = new Vector2(1f, 1f);
            band.pivot = new Vector2(.5f, 1f);
            band.sizeDelta = new Vector2(0f, TabRow);

            // Directly under the header, and the grid's viewport starts under *that* — the row
            // is a band of its own rather than an overlay. Placed against the header's height
            // rather than a constant so the two cannot drift apart.
            band.anchoredPosition = new Vector2(0f, -HeaderHeight);

            _tabs = UIKit.Node("Tabs", band);
            _tabs.anchorMin = _tabs.anchorMax = new Vector2(.5f, .5f);
            _tabs.pivot = new Vector2(.5f, .5f);
            _tabs.sizeDelta = new Vector2(span, TabRow);
            _tabs.anchoredPosition = Vector2.zero;

            if (scrolls)
            {
                // A drag anywhere in the band has to move the row, including on the gaps
                // between tabs — a strip that only scrolls when your thumb lands on a button is
                // a strip that reads as stuck (`GridView`'s note about dead space).
                var catcher = band.gameObject.AddComponent<Image>();
                catcher.color = new Color(0f, 0f, 0f, 0f);
                catcher.raycastTarget = true;

                band.gameObject.AddComponent<RectMask2D>();

                var scroll = band.gameObject.AddComponent<ScrollRect>();
                scroll.content = _tabs;
                scroll.viewport = band;
                scroll.horizontal = true;
                scroll.vertical = false;
                scroll.movementType = ScrollRect.MovementType.Elastic;
                scroll.elasticity = .14f;
                scroll.inertia = true;
                scroll.decelerationRate = .04f;
                scroll.scrollSensitivity = 55f;
            }

            for (int i = 0; i < shelves.Length; i++)
            {
                var shelf = shelves[i];
                float x = (i - (shelves.Length - 1) * .5f) * TabStep;

                _tabViews[shelf] = new ShelfTab(_tabs, shelf, TabStep, x, () => Show(shelf));
            }

            PaintTabs();
            RevealTab();

            // The emblems, in a scope of their own that survives every shelf change — see
            // its own hold. Asked for once here rather than on every shelf,
            // which is the whole point of it having a lifetime of its own.
            _tabArt = GroveArtLoader.Open("grove_tabs", GroveArtLoader.Tabs(), this, PaintTabs);
        }

        void PaintTabs()
        {
            foreach (var pair in _tabViews) pair.Value.Restyle(pair.Key == _shelf);
        }

        /// <summary>
        /// Slides the row so the shelf being shown is on screen.
        ///
        /// <para>
        /// The row is centred in its band, so with nine tabs at <see cref="TabStep"/> the first
        /// two and the last two start outside it — and the shelf this screen opens on is the
        /// first one. A strip that opens with its own selection off the left edge is a strip
        /// that reads as having lost it.
        /// </para>
        /// <para>
        /// Set directly rather than tweened, and clamped here rather than left to the
        /// <c>ScrollRect</c>: its elastic clamp runs in its own <c>LateUpdate</c> against bounds
        /// it recomputes there, so a position written on the frame the row was built is a
        /// position it has not agreed to yet.
        /// </para>
        /// </summary>
        void RevealTab()
        {
            if (_tabs == null) return;

            float span = _tabs.sizeDelta.x;
            float slack = (span - Boot.RefWidth) * .5f;
            if (slack <= 0f) return;

            var shelves = GroveShelves.Tabs;
            int at = System.Array.IndexOf(shelves, _shelf);
            if (at < 0) return;

            float x = (at - (shelves.Length - 1) * .5f) * TabStep;
            _tabs.anchoredPosition = new Vector2(Mathf.Clamp(-x, -slack, slack), 0f);
        }

        // ------------------------------------------------------------------ grid
        void BuildGrid()
        {
            _viewport = UIKit.Node("Viewport", Safe);
            _viewport.offsetMin = new Vector2(0f, BottomPad);
            _viewport.offsetMax = new Vector2(0f, -HeaderHeight - TabRow);

            _grid = GridView.Attach(_viewport, Columns, CellW, CellH,
                                    parent => new ShopCell(this, parent));
        }

        /// <summary>
        /// Rebuilds the list this shelf shows, and hands it to the grid as a new page
        /// <em>only if it is one</em>.
        ///
        /// <para>
        /// Called when the shelf changes, when the catalog is republished and once when the
        /// body has been read — but also, and this is the half that shipped a bug, whenever a
        /// <b>sync</b> lands: <c>SaveService.Adopt</c> re-reads the whole save and
        /// <c>GroveLand.LoadFrom</c> raises <c>Changed</c> unconditionally, whether or not a
        /// single region moved. A purchase asks for a sync, so every piece a player bought was
        /// followed a few seconds later by <c>GridView.Show</c> replaying the entrance of every
        /// cell <em>and throwing the scroll back to the top</em> — which is a picture of the
        /// shop reloading itself, and is exactly the fault invariant 16k names about the grove's
        /// own tiles. This screen's subscriptions were annotated "every one of these is a
        /// repaint, not a rebuild", and for two of the four that was simply not true.
        /// </para>
        /// <para>
        /// <b>The fix is to compare rather than to unsubscribe</b>, because the events are not
        /// wrong: buying land genuinely does move a region to the bottom of the land shelf, and
        /// a republished catalog genuinely can change what is on sale. What must not happen is a
        /// <em>new page</em> being declared when the page has not changed — which is
        /// <c>HomesteadPickerOverlay</c>'s rule, and it is one comparison rather than a guard
        /// each caller has to remember.
        /// </para>
        /// </summary>
        void Reload()
        {
            if (_grid == null) return;

            var catalog = HomesteadCatalog.Current;

            var items = _nextItems;
            var land = _nextLand;

            items.Clear();
            land.Clear();

            if (OnLand)
            {
                // In ladder order, and owned land last: the shelf is a thing to buy, so what is
                // already bought belongs at the bottom of it rather than in the way.
                //
                // The rung rather than the price, which is the same correction GroveLand.
                // NextForSale needed and for the same reason — half this shelf is priced in gems
                // and a gem region's Cost is nought, so sorting on the credit figure would have
                // stood the five most expensive stretches in the game at the top of the list
                // looking free.
                foreach (var region in catalog.Floor.Regions)
                    if (region.IsValid && !region.IsStarter) land.Add(region);

                land.Sort((a, b) =>
                {
                    bool oa = GroveLand.IsOwned(a), ob = GroveLand.IsOwned(b);
                    return oa != ob ? (oa ? 1 : -1)
                                    : GroveLand.Rung(a).CompareTo(GroveLand.Rung(b));
                });
            }
            else
            {
                // Catalog order, which is the author's order: for decor cheap to expensive, and
                // for residents the keeper ladder, because that is the order a player meets
                // them in. No sort, and therefore no second opinion about the order to drift.
                foreach (var piece in catalog.Pieces)
                    if (GroveShelves.Of(piece) == _shelf) items.Add(piece);
            }

            // Asked before the lists are replaced, because the question is whether this page is
            // the one already on screen — which is the shelf as well as its contents.
            bool moved = _paged != _shelf
                      || (OnLand ? !Same(_land, land) : !Same(_items, items));

            _items.Clear(); _items.AddRange(items);
            _land.Clear(); _land.AddRange(land);

            PaintTabs();
            PaintSummary();

            // `Show` is a new page and animates; `Refresh` is this page redrawn and does not —
            // GridView's own split, and the whole of what stops a sync throwing the player back
            // to the top of a hundred and fifty cells.
            if (moved) _grid.Show(OnLand ? _land.Count : _items.Count);
            else _grid.Refresh();

            _paged = _shelf;

            // The atlas last, so the grid is on screen before the pictures are. The callback
            // rebinds rather than refilling, so nothing plays its entrance twice.
            // The same hold, refilled. Flicking through tabs therefore lets go of the shelf
            // being left as it takes the next one, rather than destroying the atlas the row
            // above is drawing from and asking for it straight back.
            if (_shelfArt == null)
                _shelfArt = GroveArtLoader.Open("grove_shelf", GroveArtLoader.Shelf(_shelf), this, Repaint);
            else
                GroveArtLoader.Fill(_shelfArt, GroveArtLoader.Shelf(_shelf), this, Repaint);
        }

        /// <summary>
        /// Whether two pages are the same page.
        ///
        /// <b>By id and in order</b>, because that is what a cell draws from and what its place
        /// in the grid is decided by — <c>HomesteadPickerOverlay.Same</c>, asked of the shop.
        /// </summary>
        static bool Same(List<HomesteadPiece> a, List<HomesteadPiece> b)
        {
            if (a.Count != b.Count) return false;

            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i].Id, b[i].Id, StringComparison.Ordinal)) return false;

            return true;
        }

        /// <summary>
        /// The same question about the land shelf, where owning a region <em>moves</em> it to the
        /// bottom of the list — so the order is part of what is compared rather than incidental
        /// to it, and buying land is correctly a new page while a sync that changed nothing is
        /// correctly not one.
        /// </summary>
        static bool Same(List<GroveRegion> a, List<GroveRegion> b)
        {
            if (a.Count != b.Count) return false;

            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i].Id, b[i].Id, StringComparison.Ordinal)) return false;

            return true;
        }

        /// <summary>Redraws what is on screen: same cells, same place, no entrance.</summary>
        void Repaint()
        {
            if (_grid == null) return;

            _grid.Refresh();
            PaintTabs();
            PaintSummary();

            // The balance is deliberately not written here — see the pill in `BuildHeader`.
        }

        void PaintSummary()
        {
            if (!_summary) return;

            // **Silent unless it has news.** It counted how much of the shelf is already yours,
            // which every cell on that shelf says for itself by wearing a padlock or not — and
            // the shelf's own name, which is now written on its tab. What is left is the one
            // state the grid cannot draw, because in it there is no grid.
            _summary.text = HomesteadCatalog.IsLoaded ? string.Empty : Loc.Get("ui.grove.loading");
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
            readonly Text _name;
            readonly GroveShelf _shelf;

            public ShelfTab(RectTransform row, GroveShelf shelf, float step, float x, Action onTap)
            {
                _shelf = shelf;

                var cell = UIKit.Button("T_" + shelf, row, Art.Pixel, new Vector2(step - 8f, TabRow),
                                        new Vector2(.5f, .5f), new Vector2(x, 0f), onTap);
                cell.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

                // The money shop's tab, which is the bar at the foot of the screen one level
                // down: a rounded plate, a dark seat, a gold frame when it is the live one, the
                // emblem hanging over the top edge and the name inside.
                _plate = UIKit.Img("P", cell.transform, Art.Round(30), Skins.Plate,
                                   new Vector2(step - 10f, TabRow - 18f), new Vector2(.5f, .5f),
                                   new Vector2(0f, -4f));

                var seat = UIKit.Img("Seat", _plate.transform, Art.RoundOutline(30, 4f),
                                     new Color(.02f, .06f, .13f, .85f));
                UIKit.StretchTo((RectTransform)seat.transform, 0, 0, 0, 0);

                _edge = UIKit.Img("E", _plate.transform, Art.RoundOutline(30, 6f), Skins.PlateEdge);
                UIKit.StretchTo((RectTransform)_edge.transform, -2, -2, -2, -2);
                _edge.enabled = false;

                _mark = UIKit.Img("A", _plate.transform, null, Color.white,
                                  new Vector2(92f, 92f), new Vector2(.5f, .5f), new Vector2(0f, 30f));
                _mark.preserveAspect = true;
                _mark.raycastTarget = false;

                _name = UIKit.Shrinkable(
                    UIKit.Titled("L", _plate.transform,
                                 Loc.Get(GroveShelves.NameKey(shelf)).ToUpperInvariant(),
                                 20, Pal.Cream, TextAnchor.MiddleCenter,
                                 new Vector2(step - 16f, 28f), new Vector2(.5f, 0f),
                                 new Vector2(0f, 19f), 3f, 2f), 11);
                _name.raycastTarget = false;
            }

            /// <summary>
            /// Restyles the tab.
            ///
            /// <para>
            /// <b>Nothing here animates and nothing here changes size, which is the money
            /// shop's rule and the end of a bug this class carried twice.</b> The live tab used
            /// to spring from .86, and because <c>Tween.Pop</c> reads the transform's current
            /// scale as the size to spring back to, a second pop landing inside the first one's
            /// 0.3s captured a half-sprung scale as its new resting size — so a tab ended up
            /// permanently smaller, and smaller again on the next one. That was patched by
            /// resetting the scale first; it is now fixed by there being no scale to reset.
            /// Reported from play both times, as filter buttons that shrank and then as one tab
            /// smaller than its neighbours.
            /// </para>
            /// <para>
            /// <c>_painted</c> and <c>_live</c> go with it: the assignments below are
            /// idempotent (Unity's own setters compare before dirtying a mesh), so there is
            /// nothing left that must not repeat.
            /// </para>
            /// </summary>
            public void Restyle(bool live)
            {
                if (!_plate) return;

                // Selection is the plate's colour and its gold frame, never the emblem: two of
                // these nine marks are painted pictures and the rest are flat, so anything
                // leaning on a tint reads differently depending on which tab you stand on.
                _plate.color = live ? new Color(.098f, .467f, .757f, 1f) : Skins.Plate;
                _edge.enabled = live;
                _name.color = live ? Pal.Sun : Pal.Cream;

                // The emblem comes out of the tab row's own little atlas, so a tab can be drawn
                // before its shelf has been loaded — which is the whole point, since a tab has
                // to be readable before anybody chooses it.
                // Land has no browse atlas — a region is a rectangle, not an object — so its tab
                // wears the generated tile, which is also what its cells draw.
                var mark = _shelf == GroveShelf.Land ? Art.IsoTile(128) : HomesteadArt.ShelfMark(_shelf);
                _mark.sprite = mark;
                _mark.preserveAspect = true;
                _mark.color = mark == null ? new Color(1f, 1f, 1f, 0f)
                            : _shelf == GroveShelf.Land ? Pal.Verdant
                            : Color.white;
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
            readonly Image _plate, _art, _veil, _lock, _leaf, _money;
            readonly Text _name, _status;

            HomesteadPiece _piece;

            public RectTransform Root { get; }

            public ShopCell(HomesteadShopScreen screen, RectTransform parent)
            {
                _screen = screen;

                // **The money shop's card, to the sprite, and now built by the one thing that
                // knows how.** These were a drawn rounded box tinted per state with a traced
                // outline over it - the shape this UI drew before it had a kit. `ProductCard`
                // uses `Skins.Card` and nothing else, so this does too: it carries its own
                // keyline, so there is no rim to trace, and its own face, so there is nothing to
                // tint. Which state a cell is in is said by the padlock, the caption and the
                // leaf, none of which ever depended on the plate.
                //
                // The plate, the picture and the two lines come from `PieceCard` because the
                // picker draws the same card at its own size, and two screens assembling one
                // design is two designs a week later. Everything below them is this screen's.
                _button = PieceCard.Touch("Cell", parent, CellW,
                                          () => { if (_region != null) _screen.TapLand(_region);
                                                  else _screen.Tap(_piece); });
                Root = (RectTransform)_button.transform;

                _plate = PieceCard.Plate(Root, CellW);
                _art = PieceCard.Picture(_plate, CellW);

                // Over the picture rather than pinned to a corner, and drawn at white - the
                // same lock on the same decision the profile, the companion shelf and the ward
                // loadout make. Built after the art so it paints over it (uGUI draws in sibling
                // order), and before the labels so it never covers a price.
                // **The veil, and where it sits in the order is the whole of it.** uGUI paints
                // in sibling order, so this is built after the picture and before the padlock:
                // it darkens the thing that is not yours and leaves the lock — and the name and
                // the price under it — at full strength. The companion shelf says the same thing
                // by knocking its portrait back; this says it over the whole plate, which is
                // what a cell holding a fence rather than a face needs.
                _veil = UIKit.Img("Veil", _plate.transform, Art.Round(PieceCard.Radius),
                                  new Color(0f, 0f, 0f, .48f));
                UIKit.StretchTo((RectTransform)_veil.transform, 0, 0, 0, 0);
                _veil.raycastTarget = false;

                _lock = PieceCard.Glyph("Lock", _plate, CellW, Art.S("Ui/ic_padlock"),
                                        Color.white, .58f);

                _leaf = UIKit.Img("Leaf", _plate.transform, Art.Leaf(64), Pal.A(Pal.Verdant, .85f),
                                  new Vector2(44f, 44f), new Vector2(0f, 1f), new Vector2(26f, -26f));
                _leaf.raycastTarget = false;

                _name = PieceCard.Name(_plate, CellW);
                _status = PieceCard.Line(_plate, CellW);

                // The currency, as a picture rather than as the word "coins". The money shop
                // has never written its currency out and this one did on every priced cell,
                // which is the same number said twice - once in figures and once in a noun that
                // has to be translated. Placed against the caption's own measured width, so it
                // stays beside the number rather than at a fixed offset that a four-digit price
                // would run into.
                _money = UIKit.Img("Money", _plate.transform, null, Color.white,
                                   new Vector2(32f, 32f), new Vector2(.5f, 0f),
                                   new Vector2(0f, PieceCard.LineY(CellW)));
                _money.preserveAspect = true;
                _money.raycastTarget = false;
            }

            /// <summary>
            /// Puts the currency glyph beside the price, or takes it away.
            ///
            /// <para>
            /// <see cref="Currency.Credits"/> has no still picture in this UI, only the
            /// <c>Ui/Coin</c> flipbook, so it is a reel that is attached once and detached for a
            /// gem price - an <c>Image</c> with a cleared sprite is a white rectangle rather
            /// than nothing (invariant 7b), and <c>Attach</c> restarts a reel, so asking for it
            /// on every bind would snap the coin back to frame nought as the grid scrolls.
            /// </para>
            /// </summary>
            void Money(int currency)
            {
                if (currency == 0)
                {
                    Flipbook.Detach(_money);
                    _money.enabled = false;
                    _status.rectTransform.anchoredPosition = new Vector2(0f, PieceCard.LineY(CellW));
                    return;
                }

                _money.enabled = true;

                if (currency == 2)
                {
                    Flipbook.Detach(_money);
                    _money.sprite = Art.S("Ui/ic_gem");
                }
                else if (_money.GetComponent<Flipbook>() == null)
                {
                    Flipbook.Attach(_money, "Ui/Coin", 11f);
                }

                // The pair is centred as one block: the caption shifts right by half the glyph
                // and the gap, and the glyph sits off its left edge. `preferredWidth` is
                // answered from cached glyph metrics in the same frame, so no layout pass is
                // forced (`UIKit.CentreGlyph`'s note, which this is a small copy of - that one
                // works on a `Btn` and there is none here).
                const float Gap = 10f;
                float half = (32f + Gap) * .5f;

                float y = PieceCard.LineY(CellW);

                _status.rectTransform.anchoredPosition = new Vector2(half, y);
                _money.rectTransform.anchoredPosition =
                    new Vector2(half - _status.preferredWidth * .5f - half, y);
            }

            GroveRegion _region;

            public void Bind(int index)
            {
                if (_screen.OnLand) { BindLand(index); return; }

                _region = null;
                _piece = index >= 0 && index < _screen._items.Count ? _screen._items[index] : default;

                bool held = HomesteadLedger.IsHeld(_piece);

                // **Locked art draws in its own colours and the veil over it does the dimming.**
                // Knocking the picture back here was the old answer and it is the wrong one: a
                // tint multiplies, so it only ever darkens, and the pieces that most need to be
                // recognised before they are bought — a fallen log, brambles, a cave — are the
                // dark ones. A shop whose locked half is unreadable cannot sell anything. The
                // padlock says "not yours"; the picture says what it is.
                _art.color = Color.white;
                HomesteadArt.PaintThumb(_art, _piece);

                // **Only where there is a gate, which is the residents' shelf and the land.** A
                // padlock over a fence, a tree or a path is a lie: it is not locked, it is
                // simply unbought, and every one of those cells already says its price. A
                // companion is the one piece here that can be refused for a reason money cannot
                // answer (a keeper level - invariant 15a), and ground is sold one rung at a time
                // up an authored ladder (16j), so those two are the ones a lock is about.
                //
                // Read off the shelf rather than off the requirement, because that is the rule
                // as stated: a resident whose gate the player has already passed is still a
                // companion, and a row of companions where some wear a lock and some do not
                // reads as a bug rather than as a distinction.
                bool gated = !held && _piece.IsValid && _screen._shelf == GroveShelf.Residents;

                _veil.enabled = gated;
                _lock.gameObject.SetActive(gated);

                // The leaf marks what play alone will reach. It used to be every piece with a
                // requirement, which put it on the whole residents' shelf — and that stopped
                // being true when a companion started needing its gate *and* its price, so it
                // was marking money-only cells as free. HomesteadLedger owns which is which.
                _leaf.gameObject.SetActive(_piece.IsValid && !held && HomesteadLedger.HasFreeRoute(_piece));

                _name.text = _piece.IsValid ? Loc.Get(_piece.NameKey) : string.Empty;
                _name.color = held ? Pal.Cream : new Color(1f, .95f, .88f, .62f);

                var (line, tint, money) = StatusOf(_piece, held);
                _status.text = line;
                _status.color = tint;
                Money(money);
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

                _art.color = Pal.A(Pal.Verdant, .85f);
                var running = _art.GetComponent<Flipbook>();
                if (running) { running.enabled = false; UnityEngine.Object.Destroy(running); }
                _art.sprite = Art.IsoTile(256);

                // Ground behind the ladder or behind a price is locked in exactly the sense the
                // rest of the shop means it, so it wears the same padlock. It used to wear none,
                // which left the one shelf in the game where "not yours yet" was said only in
                // words.
                _veil.enabled = !owned && _region != null;
                _lock.gameObject.SetActive(!owned && _region != null);
                _leaf.gameObject.SetActive(false);

                _name.text = _region == null ? string.Empty : Loc.Get(_region.NameKey);
                _name.color = owned ? Pal.Cream : new Color(1f, .95f, .88f, .82f);

                if (_region == null) { _status.text = string.Empty; return; }

                if (owned)
                {
                    _status.text = Loc.Format("ui.land.size", _region.Cols, _region.Rows);
                    _status.color = Pal.A(Pal.Mint, .95f);
                    Money(0);
                    return;
                }

                // Ground further up the ladder names the stretch that comes first rather than
                // quoting a price, which is StatusOf's rule one line down: the refusal that
                // binds is the one to print, and money is not the answer to this one. Aqua,
                // because that is what every other gate on this shelf is painted.
                var next = GroveLand.NextForSale(HomesteadCatalog.Current.Floor);
                if (next != null && !ReferenceEquals(next, _region))
                {
                    _status.text = Loc.Format("ui.land.earlier_first", Loc.Get(next.NameKey));
                    _status.color = Pal.A(Pal.Aqua, .95f);
                    Money(0);
                    return;
                }

                bool gems = _region.IsGemPriced;

                _status.text = Compact.Number(_region.Price);
                Money(gems ? 2 : 1);

                // Measured against the wallet this stretch is actually bought with. Reading the
                // credit balance for a gem price would paint nearly every gem stretch as
                // affordable, since credits outnumber gems here by about a hundred to one.
                var tint = gems ? Pal.Bloom : Pal.Sun;
                _status.color = PlayerProgression.CanAfford(_region.PaidIn, _region.Price)
                    ? Pal.A(tint, .95f) : Pal.A(tint, .58f);
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
        /// <remarks>
        /// The third value is the currency the line is quoting - nought for a line that is not a
        /// price at all, 1 for credits, 2 for gems - which is what lets the cell put a glyph
        /// beside the number instead of writing the word out. It is returned rather than worked
        /// out again at the call site because the branch that chose the sentence is the only
        /// place that knows, and asking twice is how the picture and the words come to disagree.
        /// </remarks>
        static (string, Color, int) StatusOf(HomesteadPiece piece, bool held)
        {
            if (!piece.IsValid) return (string.Empty, Pal.Cream, 0);

            // A home is never "yours" in the sense the rest of the grid means it — the player
            // always has one. What the cell has to say is whether this is the next one up.
            if (piece.IsDwelling)
            {
                if (held) return (Loc.Get("ui.grove.home_best"), Pal.A(Pal.Gold, .95f), 0);

                // The gate leads when it binds, for the same reason the general branch below
                // does it: a cell quoting the price alone would show the half a player can act
                // on and hide the half stopping them. It is said here rather than falling
                // through, because a home never reaches that branch — it answers first.
                if (piece.RequiresKeeperLevel > 0 && Profile.Rank < piece.RequiresKeeperLevel)
                    return (Loc.Format("ui.grove.needs_level", piece.RequiresKeeperLevel),
                            Pal.A(Pal.Aqua, .95f), 0);

                return (Compact.Number(piece.Cost),
                        Profile.CanAfford(piece.Cost) ? Pal.A(Pal.Sun, .95f) : Pal.A(Pal.Sun, .58f), 1);
            }

            // **A stocked piece the player holds is priced exactly like one they do not**, which
            // is the owner's call and the one the ledger was already making: `OfferFor` never
            // answers `AlreadyHeld` for stock, because a player with three fences may want three
            // more. The cell used to lead with the count — "3 yours · 1,500" — and the count was
            // the half nobody was asking the shop about; what they came for is the price, and a
            // shelf where owned cells are quoted differently reads as a shelf where owned cells
            // are not for sale. Same colour as an ordinary price, deliberately: there is nothing
            // different about this cell.
            //
            // The stock ceiling is `GroveStock.MaxCopies` at 9,999, so a price quoted here is
            // never one the panel behind it will refuse.
            if (held && piece.IsStocked)
                return (Compact.Number(piece.Cost),
                        Profile.CanAfford(piece.Cost) ? Pal.A(Pal.Sun, .95f) : Pal.A(Pal.Sun, .58f), 1);

            // Everything that is not stock genuinely is bought once — a resident, a home rung,
            // anything earned — so "Yours" is the whole answer there (invariant 15).
            if (held) return (Loc.Get("ui.grove.yours"), Pal.A(Pal.Mint, .95f), 0);

            // The keeper gate leads whenever it is the refusal that binds, priced or not.
            // It used to be drawn only on a resident with no price, back when reaching the
            // gate handed the companion over; now the gate and the price are both required,
            // so a cell that showed the price alone was quoting the half a player could act
            // on and hiding the half stopping them.
            if (piece.RequiresKeeperLevel > 0 && Profile.Rank < piece.RequiresKeeperLevel)
                return (Loc.Format("ui.grove.needs_level", piece.RequiresKeeperLevel),
                        Pal.A(Pal.Aqua, .95f), 0);

            if (piece.RequiresLevel.IsValid)
                return (Loc.Format("ui.grove.needs_glade", LevelName(piece.RequiresLevel)),
                        Pal.A(Pal.Aqua, .95f), 0);

            if (piece.RequiresChapter.IsValid)
                return (Loc.Format("ui.grove.needs_chapter", ChapterName(piece.RequiresChapter)),
                        Pal.A(Pal.Aqua, .95f), 0);

            if (piece.IsForSale)
                return (Compact.Number(piece.Cost),
                        Profile.CanAfford(piece.Cost) ? Pal.A(Pal.Sun, .95f) : Pal.A(Pal.Sun, .58f), 1);

            // Left over: no requirement, no price, and not held — which the catalog cannot
            // produce, since a piece with neither is a starter. Said plainly rather than left
            // blank, so a content mistake shows up on the screen it broke.
            return (Loc.Get("ui.grove.not_for_sale"), new Color(1f, .96f, .88f, .55f), 0);
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

            // **Holding one is not a reason to refuse the tap, and this guard was why stock
            // could not be re-bought at all.** It toasted "{piece} is yours, tap a spot to place
            // it" and returned, so the shop's own half of the catalog — which `HomesteadLedger`
            // deliberately never marks `AlreadyHeld` — had no route to the panel that sells it.
            // The screen was refusing what the rules allowed, which is the shape of fault that
            // reads as a broken button rather than as a decision.
            //
            // Nothing is needed in its place: every path below lands on a panel that states the
            // held case properly. A resident answers `CompanionPurchaseState.AlreadyHeld`, and
            // anything else non-stocked reaches `HomesteadBuyOverlay`, whose `AlreadyHeld` branch
            // says "Yours" with the buy button dead.

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
                var (line, _, _) = StatusOf(piece, false);
                Scenery.Toast(Content, line, Pal.Aqua);
                return;
            }

            Flow.Modal<HomesteadBuyOverlay>(v => v.Piece = piece);
        }
    }
}
