using System;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The Grovement: a floor of tiles the player owns, buys and builds on.
    ///
    /// <para>
    /// <b>This replaced a ladder of floating islands, and the difference is not decorative.</b>
    /// An island carried hand-authored slots, each with a position, a size and a role, so the
    /// player's decision was which of eleven pre-placed dots got which sticker — every grove
    /// came out with the same composition and different stickers on it. A field of identical
    /// tiles moves the composition to the player: where a thing goes is now as much their
    /// choice as what it is. That is why the slot-kind rule went with the islands (see
    /// <c>HomesteadSlotKind</c>) — it existed to stop a sprinkle of dots looking accidental,
    /// and there are no dots.
    /// </para>
    /// <para>
    /// <b>What it costs and what it does not.</b> The save file gained one field — which
    /// regions of the floor were bought (invariant 15, a union-joined id set) — because land is
    /// paid for now rather than earned from chapters, and that is the one thing here that could
    /// not stay derived. It gained nothing else: a tile is a slot, its id is permanent, and an
    /// untouched tile writes no row, so a three-hundred-tile floor with two things on it costs
    /// two rows exactly as ten islands did.
    /// </para>
    /// <para>
    /// <b>Two things a field needs that islands did not.</b> Depth has to be computed rather
    /// than authored, because what stands in front of what is now a consequence of where the
    /// player put things — see <c>GroveFloor.DrawOrder</c>. And the tiles have to be culled,
    /// because a floor is hundreds of them and a phone shows dozens; see
    /// <see cref="GroveFieldView"/>, which is <c>GridView</c>'s bargain in two dimensions.
    /// </para>
    /// </summary>
    public sealed class HomesteadScreen : View, IDrawsGroveArt
    {
        public override string Track => "mus_menu";

        /// <summary>
        /// The one screen in the game that takes two fingers. See <see cref="View.WantsMultiTouch"/>
        /// for why it is declared rather than switched on, and why a board must never inherit it.
        /// </summary>
        public override bool WantsMultiTouch => true;

        public override bool OnBack() { Flow.Go<HomeScreen>(); return true; }

        const float HeaderHeight = 214f;

        /// <summary>Size of the ring marking a buildable tile with nothing on it.</summary>
        const float EmptyMark = 64f;

        /// <summary>
        /// Art pixels to floor pixels for a piece standing on a tile.
        ///
        /// One number for the whole field rather than a scale per slot, which is what the
        /// islands had. A slot's scale existed to compose a fixed picture — front and centre
        /// bigger than back and left — and on a field every tile is the same distance from the
        /// eye, so the only honest scale is the one that makes a piece the right size against
        /// a tile. What varies is the piece's own <c>Scale</c>, which is a fact about the thing
        /// rather than about where it stands.
        /// </summary>
        const float PieceScale = 1.15f;

        RectTransform _viewport;
        GroveFieldView _field;
        Text _summary;

        protected override void Build()
        {
            // The hub's own sky and nothing else from it. The grove here is the content, so
            // laying the hub's ground and decoration behind it would be two groves in one
            // picture — and this one is supposed to be the player's.
            Scenery.Cover(Content, "home_sky", .05f, .42f);
            Fireflies.Spawn(Content, 16, new Color(1f, .93f, .70f), 6f, 20f);

            BuildField();
            BuildHeader();

            // The catalog is a body, read on entering the feature. Both this and the art load
            // asynchronously and both repaint, because a screen is built in the frame it is
            // asked for and the first paint would otherwise be the only one.
            Warm();
            HomesteadArt.OpenAsync(() => { if (this) Repaint(); });

            HomesteadCatalog.Changed += Reload;
            HomesteadLedger.Changed += Repaint;
            HomesteadLayout.Changed += Repaint;
            // Buying land adds ground, which is a different set of tiles rather than a different
            // look on the same ones — so it re-measures and refills rather than rebinding.
            GroveLand.Changed += Regrow;
            PlayerProgression.Changed += Repaint;

            // Residents are derived from the keeper ladder, so a run finished in this session
            // can wake a friend while the player is standing here.
            PlayerProgress.Reloaded += Repaint;
            PlayerProgress.RecordChanged += OnRecord;
        }

        void OnDestroy()
        {
            HomesteadCatalog.Changed -= Reload;
            HomesteadLedger.Changed -= Repaint;
            HomesteadLayout.Changed -= Repaint;
            GroveLand.Changed -= Regrow;
            PlayerProgression.Changed -= Repaint;
            PlayerProgress.Reloaded -= Repaint;
            PlayerProgress.RecordChanged -= OnRecord;

            // Unless the shop is what replaced this screen, which is not a special case so much
            // as the general one: Destroy lands at the end of the frame, so the incoming screen
            // has already built *and painted* by the time this runs. Releasing here pulled every
            // sprite out from under a shop that had already drawn it, and nothing repaints.
            // HomesteadArt owns the rule so a third screen cannot forget half of it.
            HomesteadArt.CloseUnlessWanted();
        }

        void OnRecord(LevelRecord record) => Repaint();

        /// <summary>Takes newly bought ground: re-measures the field and refills it, in place.</summary>
        void Regrow()
        {
            if (_field == null) return;

            ShowOwned();
            _field.Rebuild();
            Repaint();
        }

        async void Warm()
        {
            await HomesteadService.EnsureAsync();
            if (!this) return;

            // The art set is derived from the catalog, so it can only be asked for once the
            // catalog is in hand. Asking twice is free — the scope reports itself loaded.
            HomesteadArt.OpenAsync(() => { if (this) Repaint(); });
            Reload();
        }

        // ----------------------------------------------------------------- field
        void BuildField()
        {
            _viewport = UIKit.Node("Viewport", Content);
            // No nav bar on this screen. It is the one page in the game that wants the whole
            // display: a floor is panned and zoomed, and a strip of chrome across the bottom is
            // both a slice of grove nobody can see and a row of buttons a dragging thumb keeps
            // catching. The corner arrow is the way out.
            _viewport.offsetMin = new Vector2(0f, 24f);
            _viewport.offsetMax = new Vector2(0f, -HeaderHeight);

            _field = GroveFieldView.Attach(_viewport, HomesteadCatalog.Current.Floor,
                                           (col, row) => new TileCell(this));
            _field.TileTapped = Tap;
            ShowOwned();
        }

        /// <summary>
        /// Takes a new floor: throws every tile away and opens the camera on the hall.
        ///
        /// Called when the catalog is published and once when the body has been read — the two
        /// moments the <em>ground</em> differs. Everything else is a <see cref="Repaint"/>,
        /// which rebinds the tiles that exist without moving the camera, because a player who
        /// places a bench has not asked to be taken anywhere.
        /// </summary>
        void Reload()
        {
            if (_field == null) return;

            var floor = HomesteadCatalog.Current.Floor;

            _field.SetFloor(floor);
            ShowOwned();
            _field.Rebuild();

            // Opened on the hall rather than on the field's origin, which is the corner of a
            // diamond and therefore the emptiest place on the screen.
            if (GroveFloor.TryParse(floor.HallTile, out int col, out int row))
                _field.CentreOn(col, row);
            else
                _field.CentreOn(floor.Cols / 2, floor.Rows / 2);

            Repaint();
        }

        /// <summary>
        /// Which ground exists. Unowned land is not drawn at all — see
        /// <c>GroveFieldView.SetVisible</c> for why a field of padlocks was the wrong screen.
        /// </summary>
        static bool Owned(int col, int row)
            => GroveLand.IsOwned(HomesteadCatalog.Current.Floor, col, row);

        /// <summary>
        /// Tells the field which ground exists and how far it reaches.
        ///
        /// The bounds come from the regions rather than from a sweep of every tile — see
        /// <c>GroveLand.OwnedBounds</c>. Held as one method because the two have to be set
        /// together: a predicate without matching bounds is a field the camera can drag off.
        /// </summary>
        void ShowOwned()
        {
            var floor = HomesteadCatalog.Current.Floor;

            GroveLand.OwnedBounds(floor, out int minCol, out int minRow, out int maxCol, out int maxRow);
            _field.SetVisible(Owned, minCol, minRow, maxCol, maxRow);
        }

        /// <summary>Redraws the tiles that exist, in place, without moving the camera.</summary>
        void Repaint()
        {
            if (_field == null) return;

            _field.Refresh();
            PaintSummary();
        }

        // ---------------------------------------------------------------- header
        void BuildHeader()
        {
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .06f, .09f, .82f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            frt.sizeDelta = new Vector2(0f, 268f);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);

            var banner = UIKit.Img("Banner", Content, Art.S("Ui/banner"), Color.white,
                                   new Vector2(430f, 114f), new Vector2(.5f, 1f), new Vector2(0f, -102f));
            UIKit.Shrinkable(
                UIKit.Titled("Title", banner.transform, Loc.Get("ui.grove.title").ToUpperInvariant(), 32,
                             new Color(.36f, .24f, .16f), TextAnchor.MiddleCenter,
                             new Vector2(300f, 46f), new Vector2(.5f, .5f),
                             new Vector2(0f, 114f * UIKit.PillFaceLift), 0f, 2f), 20);
            banner.transform.localScale = Vector3.zero;
            Tween.Pop(banner.transform, 0f, .6f, .1f);

            // The way out, where the balance used to be. The nav bar is gone from this screen
            // (see BuildField), so the corner needs an exit rather than a readout — and the
            // balance was the wrong thing to put here anyway: nothing on this screen is bought.
            // Land and decor are both bought in the shop, which shows the balance itself.
            UIKit.IconButton("Back", Content, Skins.Nav, "ic_left", new Vector2(112f, 112f),
                             new Vector2(0f, 1f), new Vector2(92f, -104f),
                             () => Flow.Go<HomeScreen>());

            _summary = UIKit.Shrinkable(
                UIKit.Titled("Summary", Content, string.Empty, 26,
                             new Color(1f, .96f, .88f, .72f), TextAnchor.MiddleCenter,
                             new Vector2(720f, 34f), new Vector2(.5f, 1f), new Vector2(0f, -172f), 3f, 0f), 18);

            // The shop is a screen of its own rather than a panel over this one, for
            // CompanionScreen's reason: what it lists is unbounded, and a grid that scrolls
            // inside a scrim is a worse place to browse than a page that owns the display.
            // Placed through UIKit.Corner because Box pivots at centre: passing the margin
            // straight in put half the button past the right edge of the screen.
            var shopSize = new Vector2(230f, 96f);
            var shopAnchor = new Vector2(1f, 1f);
            var shop = UIKit.TextButton("Shop", Content, "btn_orange", Loc.Get("ui.grove.shop"), 28,
                                        shopSize, shopAnchor,
                                        UIKit.Corner(shopSize, shopAnchor, 28f, 62f),
                                        () => Flow.Go<HomesteadShopScreen>());
            UIKit.Shrinkable(shop.Label, 18);
            UIKit.FitLabel(shop);
        }

        void PaintSummary()
        {
            if (!_summary) return;

            var catalog = HomesteadCatalog.Current;

            if (!HomesteadCatalog.IsLoaded)
            {
                _summary.text = Loc.Get("ui.grove.loading");
                return;
            }

            if (catalog.Floor.IsEmpty)
            {
                _summary.text = Loc.Get("ui.grove.unavailable");
                return;
            }

            _summary.text = Loc.Format("ui.grove.summary",
                                       HomesteadLayout.OccupiedCount(catalog),
                                       GroveLand.OwnedTileCount(catalog.Floor),
                                       HomesteadLayout.VarietyCount(catalog));
        }

        // ------------------------------------------------------------------ tile
        /// <summary>
        /// One tile: the ground, whatever stands on it, and the marks that say what it is.
        ///
        /// <para>
        /// Built once and rebound as the camera moves it across the field — see
        /// <see cref="GroveFieldView"/>. Everything that can differ between tiles is a field
        /// here rather than a fresh object, because the alternative is building and destroying a
        /// subtree per tile per pan, which is the cost that made a floor look impossible before
        /// culling existed.
        /// </para>
        /// </summary>
        sealed class TileCell : GroveFieldView.ITileCell
        {
            readonly HomesteadScreen _screen;
            readonly Image _ground, _art, _ring;

            public RectTransform Root { get; }

            public TileCell(HomesteadScreen screen)
            {
                _screen = screen;

                Root = UIKit.Node("Tile", null);
                Root.sizeDelta = new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight);

                _ground = UIKit.Img("G", Root, null, Color.white,
                                    new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight),
                                    new Vector2(.5f, .5f), Vector2.zero);
                _ground.raycastTarget = false;
                _ground.preserveAspect = false;

                // A ring rather than a fill, and only on tiles you can build on: an empty tile
                // has to look like an invitation rather than like a hole, and the whole floor is
                // empty on the first visit.
                _ring = UIKit.Img("R", Root, Art.Ring(96, 7f), Pal.A(Pal.Cream, .30f),
                                  new Vector2(EmptyMark, EmptyMark * .5f), new Vector2(.5f, .5f),
                                  Vector2.zero);
                _ring.raycastTarget = false;

                _art = UIKit.Img("A", Root, null, Color.white, new Vector2(140f, 140f),
                                 new Vector2(.5f, .5f), Vector2.zero);
                _art.preserveAspect = true;
                _art.raycastTarget = false;
            }

            public void Bind(int col, int row)
            {
                var catalog = HomesteadCatalog.Current;
                var floor = catalog.Floor;
                string id = GroveFloor.TileId(col, row);

                bool hall = floor.IsHall(id);

                // The ground is a block, not a flat lozenge: its side wall is painted below the
                // top face, so the sprite hangs by half the skirt to put its *surface* on the
                // tile's point. Derived from the art — see HomesteadArt.TileDraw.
                var ground = (RectTransform)_ground.transform;
                ground.sizeDelta = HomesteadArt.TileDraw(floor, out float drop);
                ground.anchoredPosition = new Vector2(0f, -drop);

                _ground.sprite = HomesteadArt.Tile(floor);
                _ground.color = Color.white;

                // The hall is drawn from the best home the player owns rather than placed, so
                // its tile shows a dwelling and accepts nothing. Everything else shows whatever
                // is standing there — or the starter companion, on the one tile that has one
                // and has never been touched (see HomesteadLayout.Shown).
                var piece = hall
                    ? HomesteadLedger.BestDwelling(catalog)
                    : catalog.Find(HomesteadLayout.Shown(catalog, id));

                bool empty = !piece.IsValid;

                _art.gameObject.SetActive(!empty);
                if (!empty)
                {
                    var size = HomesteadArt.SizeOnFloor(piece, PieceScale);
                    ((RectTransform)_art.transform).sizeDelta = size;
                    ((RectTransform)_art.transform).anchoredPosition = new Vector2(0f, size.y * piece.Lift);
                    HomesteadArt.Paint(_art, piece);
                }

                _ring.gameObject.SetActive(empty && !hall);
                if (_ring.gameObject.activeSelf)
                {
                    // Reset before restarting, and that is not tidiness. Tween.Breathe captures
                    // the transform's current scale as the value it oscillates about, and killing
                    // one leaves the transform wherever in the cycle it stopped — so a recycled
                    // cell would take a mid-breath scale as its new rest point and the rings
                    // would drift larger every time the player panned across them.
                    _ring.transform.localScale = Vector3.one;

                    // Phased off the tile's own coordinates so the field breathes as a field
                    // rather than pulsing in unison, which reads as a fault rather than as life.
                    Tween.Breathe(_ring.transform, .10f, 2.4f, (col * .37f + row * .61f) % 1f);
                }
            }
        }

        // ------------------------------------------------------------------- tap
        void Tap(int col, int row)
        {
            var catalog = HomesteadCatalog.Current;
            var floor = catalog.Floor;
            string id = GroveFloor.TileId(col, row);

            // No land branch: ground the player does not own is not drawn, so there is nothing
            // here to tap. Expanding is done in the shop, where the other things they buy are.

            // A home goes to the home panel in every state — the question at a house is never
            // "shall I buy this one item", it is "where am I on the ladder".
            if (floor.IsHall(id)) { Flow.Modal<HomesteadHomeOverlay>(); return; }

            Flow.Modal<HomesteadPickerOverlay>(v => v.Slot = new HomesteadSlot(col, row));
        }
    }
}
