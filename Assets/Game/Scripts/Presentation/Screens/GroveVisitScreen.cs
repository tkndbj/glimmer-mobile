using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using GlimmerGrove.Social;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Somebody else's grove, drawn from their published card and nothing else.
    ///
    /// <para>
    /// <b>A screen of its own rather than a mode on <see cref="HomesteadScreen"/>.</b> That
    /// screen is a thousand lines of editing — a picker, a move drag, a flip, a long press, a
    /// shop button, a tending readout that changes as you fill the place — and every one of
    /// those would need a branch saying "not while visiting". A mode toggle that changes what
    /// every control on a screen does is exactly what invariant 16 refused for the grove's own
    /// editing, and refusing it here costs one file that can only read. What the two share is
    /// what they should share: the floor geometry, the tile view, the art loader and the piece
    /// sizes.
    /// </para>
    /// <para>
    /// <b>It draws a <see cref="GroveCard"/>, never a ledger.</b> Which is what makes it
    /// read-only by construction rather than by discipline — there is nothing here to write
    /// to. The card is the same projection this device publishes for its own grove
    /// (<see cref="GroveCard.OfPlayer"/>), so visiting your own grove and looking at it are
    /// the same picture, which is the property a second description of one grove would lose.
    /// </para>
    /// <para>
    /// <b>Ids this build does not know are drawn as nothing rather than as an error.</b> A
    /// visitor one content drop behind will meet pieces and land that do not exist for them
    /// yet; <see cref="GroveCard.PieceAt"/> resolves those to an invalid piece and the tile
    /// simply stands empty. A slightly emptier grove is a much better failure than a refusal,
    /// and it costs nothing to arrange.
    /// </para>
    /// </summary>
    public sealed class GroveVisitScreen : View, IDrawsGroveArt
    {
        public override string Track => "mus_menu";

        /// <summary>The grove is panned and pinched exactly as the player's own is.</summary>
        public override bool WantsMultiTouch => true;

        const float HeaderHeight = 230f;

        /// <summary>Art pixels to floor pixels. The same number the player's own grove uses.</summary>
        const float PieceScale = 1.15f;

        string _ownerId;
        string _knownName;

        GroveCard _card = GroveCard.Empty;
        bool _fetching;
        bool _failed;

        RectTransform _viewport;
        GroveFieldView _field;
        Text _name, _worth, _status;
        StarRow _stars;

        /// <summary>Opens on a keeper. The name is what the board already knew, so the
        /// header has something to say while the card is in flight.</summary>
        public void Visit(string ownerId, string knownName)
        {
            _ownerId = ownerId ?? string.Empty;
            _knownName = knownName ?? string.Empty;
        }

        protected override void Build()
        {
            Scenery.Cover(Content, "home_sky", .05f, .42f);
            Fireflies.Spawn(Content, 16, new Color(1f, .93f, .70f), 6f, 20f);

            BuildField();
            BuildHeader();

            // The catalog is a body and a visitor may have arrived here without ever opening
            // their own grove, so it cannot be assumed to be in hand.
            Warm();

            // A visited grove's art lands after the floor does, exactly as the player's own
            // does, so the tiles have to be repainted when it arrives (invariant 7b).
            HomesteadArt.Changed += Repaint;
            HomesteadCatalog.Changed += Reload;
        }

        void OnDestroy()
        {
            HomesteadArt.Changed -= Repaint;
            HomesteadCatalog.Changed -= Reload;

            // The visitor's own grove keeps its art; this drops only the stranger's. Two scopes
            // is what makes that possible — see AssetLibrary.GroveVisitScope. Unconditional
            // rather than guarded, because nothing else in the game draws this scope, so there
            // is no incoming screen that could want it.
            HomesteadArt.CloseVisit();

            // The player's own grove art is a different question, and one this screen must not
            // answer wrongly: leaving a visit for anything that is not a grove screen should
            // free it, and CloseUnlessWanted is what already knows that.
            HomesteadArt.CloseUnlessWanted();
        }

        async void Warm()
        {
            await HomesteadService.EnsureAsync();
            if (!this) return;

            Reload();
            Fetch();
        }

        async void Fetch()
        {
            if (_fetching || string.IsNullOrEmpty(_ownerId)) return;

            _fetching = true;
            _failed = false;
            PaintHeader();

            var (result, card) = await GroveBoard.FetchCardAsync(_ownerId);

            _fetching = false;
            if (!this) return;

            _failed = !result.Ok || card == null || !card.IsValid;
            _card = card ?? GroveCard.Empty;

            // The art has to be asked for before the tiles are painted, and it is asked for
            // with *this* grove's pieces rather than the catalog's — the whole reason a visit
            // costs one grove and not one shop.
            HomesteadArt.OpenVisitAsync(PlacedPieceIds(), () => { if (this) Repaint(); });

            Reload();
            PaintHeader();
        }

        System.Collections.Generic.List<string> PlacedPieceIds()
        {
            var ids = new System.Collections.Generic.List<string>(_card.OccupiedCount + 1);

            foreach (var pair in _card.Placements) ids.Add(pair.Value.PieceId);
            if (!string.IsNullOrEmpty(_card.DwellingId)) ids.Add(_card.DwellingId);

            return ids;
        }

        // ----------------------------------------------------------------- field
        void BuildField()
        {
            var stage = SafeArea.Node("Stage", Content);

            _viewport = UIKit.Node("Viewport", stage);
            _viewport.offsetMin = new Vector2(0f, 24f);
            _viewport.offsetMax = new Vector2(0f, -HeaderHeight);

            _field = GroveFieldView.Attach(_viewport, HomesteadCatalog.Current.Floor,
                                           (col, row) => new VisitTile(this));

            // Deliberately no TileTapped, no TileHeld and no Footprint. There is nothing on
            // this screen to pick up, so a tap has nothing to answer — and a tap that opened a
            // picker over a grove the player cannot change would be a control that lies.
            ShowOwned();
        }

        /// <summary>Which ground this keeper owns. Unowned land is not drawn — the same rule the
        /// player's own grove follows, so a visited grove reads as a place rather than a plan.</summary>
        bool Owned(int col, int row)
            => _card.OwnsLand(HomesteadCatalog.Current.Floor, col, row);

        void ShowOwned()
        {
            var floor = HomesteadCatalog.Current.Floor;

            // Bounds over the regions the card owns, walked the way GroveLand.OwnedBounds walks
            // them — regions rather than tiles, because a field is allowed to be large and
            // asking every tile is quietly quadratic in the size of the floor.
            int minCol = int.MaxValue, minRow = int.MaxValue, maxCol = int.MinValue, maxRow = int.MinValue;

            foreach (var region in floor.Regions)
            {
                if (!_card.OwnsLand(region)) continue;

                if (region.Col < minCol) minCol = region.Col;
                if (region.Row < minRow) minRow = region.Row;
                if (region.Col + region.Cols - 1 > maxCol) maxCol = region.Col + region.Cols - 1;
                if (region.Row + region.Rows - 1 > maxRow) maxRow = region.Row + region.Rows - 1;
            }

            if (minCol > maxCol) { minCol = minRow = 0; maxCol = maxRow = 0; }

            _field.SetVisible(Owned, minCol, minRow, maxCol, maxRow);
        }

        void Reload()
        {
            if (_field == null) return;

            var floor = HomesteadCatalog.Current.Floor;

            _field.SetFloor(floor);
            ShowOwned();
            _field.Rebuild();

            if (GroveFloor.TryParse(floor.HallTile, out int col, out int row))
                _field.CentreOn(col, row);
            else
                _field.CentreOn(floor.Cols / 2, floor.Rows / 2);

            Repaint();
        }

        void Repaint()
        {
            _field?.Refresh();
            PaintHeader();
        }

        // ---------------------------------------------------------------- header
        void BuildHeader()
        {
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .06f, .09f, .82f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            frt.sizeDelta = new Vector2(0f, 268f + SafeArea.Top);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);

            var chrome = Safe;

            UIKit.IconButton("Back", chrome, Skins.Nav, "ic_left", new Vector2(112f, 112f),
                             new Vector2(0f, 1f), new Vector2(92f, -104f),
                             () => Flow.Go<LeaderboardScreen>());

            _name = UIKit.Shrinkable(
                UIKit.Titled("Name", chrome, _knownName, 38, new Color(1f, .97f, .90f),
                             TextAnchor.MiddleCenter, new Vector2(640f, 52f),
                             new Vector2(.5f, 1f), new Vector2(0f, -92f), 4f, 3f), 22);

            _worth = UIKit.Shrinkable(
                UIKit.Titled("Worth", chrome, string.Empty, 28, Pal.Gold,
                             TextAnchor.MiddleCenter, new Vector2(640f, 36f),
                             new Vector2(.5f, 1f), new Vector2(0f, -140f), 3f, 2f), 18);

            int rungs = Mathf.Max(1, HomesteadCatalog.Current.Scores.StarCount);
            _stars = StarRow.Create(chrome, new Vector2(.5f, 1f), new Vector2(0f, -186f),
                                    30f, 36f, 0, false, rungs);

            _status = UIKit.Shrinkable(
                UIKit.Titled("Status", Safe, string.Empty, 28, new Color(1f, .96f, .88f, .78f),
                             TextAnchor.UpperCenter, new Vector2(760f, 160f), new Vector2(.5f, 1f),
                             new Vector2(0f, -(HeaderHeight + 120f)), 3f, 0f), 18);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;

            PaintHeader();
        }

        /// <summary>
        /// The name, the worth and the stars — and, when there is no grove to show, the reason.
        ///
        /// Four states and each renders its own sentence, which is <c>AdOfferState</c>'s rule.
        /// A blank floor with no explanation is how a player concludes a feature is broken, and
        /// three of these four are perfectly ordinary: a keeper who opted out after the board
        /// was built, a fetch that has not landed, and a network that is not there.
        /// </summary>
        void PaintHeader()
        {
            if (_name) _name.text = _card.IsValid && _card.Name.Length > 0 ? _card.Name : _knownName;

            if (_worth)
                _worth.text = _card.IsValid
                    ? Loc.Format("ui.board.row_worth", Compact.Number(_card.Score), _card.KeeperLevel)
                    : string.Empty;

            if (_stars) _stars.SetInstant(_card.IsValid ? Mathf.Min(_card.Stars, _stars.Count) : 0);

            if (!_status) return;

            if (_card.IsValid) _status.text = string.Empty;
            else if (_fetching) _status.text = Loc.Get("ui.board.loading");
            else if (_failed) _status.text = Loc.Get("ui.visit.gone");
            else _status.text = Loc.Get("ui.board.loading");
        }

        // ------------------------------------------------------------------ tile
        /// <summary>
        /// One tile of a visited grove: ground, and whatever the card says stands on it.
        ///
        /// <para>
        /// Deliberately simpler than the player's own tile. There is no breathing ring on an
        /// empty tile, because that ring is an invitation to place something and there is
        /// nothing here to place — an invitation on a screen with no control behind it is
        /// worse than a plain gap.
        /// </para>
        /// </summary>
        sealed class VisitTile : GroveFieldView.ITileCell
        {
            readonly GroveVisitScreen _screen;
            readonly Image _ground, _art;

            public RectTransform Root { get; }

            public VisitTile(GroveVisitScreen screen)
            {
                _screen = screen;

                Root = UIKit.Node("Tile", null);
                Root.sizeDelta = new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight);

                _ground = UIKit.Img("G", Root, null, Color.white,
                                    new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight),
                                    new Vector2(.5f, .5f), Vector2.zero);
                _ground.raycastTarget = false;
                _ground.preserveAspect = false;

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

                // The ground hangs by half its skirt, exactly as it does on the player's own
                // floor — the top face of an isometric tile is 2:1 and whatever is painted
                // below it is wall. Derived from the art rather than typed.
                var ground = (RectTransform)_ground.transform;
                ground.sizeDelta = HomesteadArt.TileDraw(floor, out float drop);
                ground.anchoredPosition = new Vector2(0f, -drop);
                _ground.sprite = HomesteadArt.Tile(floor);
                _ground.color = Color.white;

                bool hall = floor.IsHall(id);

                var piece = hall
                    ? _screen._card.Dwelling(catalog)
                    : _screen._card.PieceAt(catalog, id);

                bool empty = !piece.IsValid;
                _art.gameObject.SetActive(!empty);
                if (empty) return;

                var size = HomesteadArt.SizeOnFloor(piece, PieceScale);
                ((RectTransform)_art.transform).sizeDelta = size;
                ((RectTransform)_art.transform).anchoredPosition = new Vector2(0f, size.y * piece.Lift);
                HomesteadArt.Paint(_art, piece);

                // Written on every bind rather than only when mirrored: cells are pooled and
                // rebound as the camera pans, so a scale left behind by a flipped fence would
                // be inherited by whatever tile reused the object.
                _art.transform.localScale =
                    new Vector3(!hall && _screen._card.FlippedAt(id) ? -1f : 1f, 1f, 1f);
            }
        }

        public override bool OnBack()
        {
            Flow.Go<LeaderboardScreen>();
            return true;
        }
    }
}
