using GlimmerGrove.AssetPipeline;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
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
    public sealed class GroveVisitScreen : View
    {
        public override string Track => "mus_menu";

        /// <summary>The grove is panned and pinched exactly as the player's own is.</summary>
        public override bool WantsMultiTouch => true;

        /// <summary>
        /// Everything above the floor. It grew by the height of one small line when the card's
        /// age was added under the stars — the field begins where this ends, so the two move
        /// together or the grove is drawn under its own header.
        /// </summary>
        const float HeaderHeight = 248f;

        string _ownerId;
        string _knownName;

        GroveCard _card = GroveCard.Empty;
        bool _fetching;
        bool _failed;

        RectTransform _viewport;
        GroveFieldView _field;
        Text _name, _worth, _age, _status;
        StarRow _stars;

        /// <summary>
        /// Says the screen is still fetching, and goes as soon as it is not.
        ///
        /// <para>
        /// A visit is the slowest door in the game — a network round trip for the card and then
        /// the art for whatever that card turned out to be standing on — and until this existed
        /// the whole of it was drawn as an empty floor. An empty floor is also what a keeper
        /// with nothing in their grove looks like, so the screen was saying two completely
        /// different things with one picture.
        /// </para>
        /// </summary>
        BusyVeil _busy;

        /// <summary>This keeper's art, held only while their grove is on screen.</summary>
        AssetHold _art;

        Btn _report;
        bool _reporting;

        /// <summary>Opens on a keeper. The name is what the board already knew, so the
        /// header has something to say while the card is in flight.</summary>
        public void Visit(string ownerId, string knownName)
        {
            _ownerId = ownerId ?? string.Empty;
            _knownName = knownName ?? string.Empty;
        }

        protected override void Build()
        {
            // **A daylight sky is darkened by nothing.** This carried a flat shade and a
            // near-black vignette that cost the corners .42 — the numbers a screen whose
            // backdrop is only a *ground* can afford — and over a village lit by a sun they
            // were most of "dark and dead": the brightest thing on the screen was being
            // dimmed before anything was drawn over it. What is left is the little that
            // still has a job, which is keeping the corners off the header's text.
            var sky = Scenery.Cover(Content, "home_sky", 0f, .14f);
            Scenery.Sun(sky);
            Fireflies.Spawn(Content, 16, new Color(1f, .93f, .70f), 6f, 20f);

            BuildField();
            BuildHeader();

            // Built last so it draws over the field and the header, and so both of the states
            // it describes are already on screen underneath it.
            _busy = BusyVeil.Attach(Safe, Loc.Get("ui.visit.loading"));

            Open();

            // A visited grove's art lands after the floor does, exactly as the player's own
            // does, so the tiles have to be repainted when it arrives (invariant 7b).
            HomesteadCatalog.Changed += Reload;
        }

        void OnDestroy()
        {
            HomesteadCatalog.Changed -= Reload;

            // Lets go of this keeper's art and of nothing else. The visitor's own grove, if
            // they have one open behind this, is holding its own — which is what a count buys
            // over a scope: two groves' worth of pieces can overlap completely and neither
            // screen has to know the other exists.
            _art?.Dispose();
        }

        /// <summary>
        /// Everything this screen has to fetch before it can draw, in the fewest round trips
        /// it can be done in.
        ///
        /// <para>
        /// <b>The catalog and the card are asked for at the same time, and that is the change
        /// worth naming.</b> They used to run in series — read the grove body off disk, and
        /// only then open the network — which put a file read and a JSON parse in front of the
        /// slowest thing on the screen for no reason at all: neither answer feeds the other.
        /// The card is a network round trip and the catalog is local, so the wait is now the
        /// longer of the two rather than the sum.
        /// </para>
        /// <para>
        /// <b>The art can only be asked for afterwards</b>, and that is not an oversight: what
        /// a visit loads is a function of what is standing in <em>this</em> grove, which is the
        /// whole reason a visit costs one grove rather than one shop (invariant 7b). So the two
        /// phases are genuinely serial, and the readout says so by staying up across both.
        /// </para>
        /// </summary>
        void Open() => Run(async token =>
        {
            _fetching = true;
            _failed = false;
            PaintHeader();

            // Deliberately not given this screen's token: the catalog is shared, and a visitor
            // tapping back out must not cancel a read the Grovement behind them is waiting on.
            var catalog = HomesteadService.EnsureAsync();
            var fetch = FetchCardAsync(token);

            await Task.WhenAll(catalog, fetch);

            _fetching = false;
            if (!Living) return;

            Reload();
            PaintHeader();

            // Nothing to draw and nothing to load. The header already says which of the two
            // reasons it is, so the readout has no more work to do.
            if (!_card.IsValid) { _busy?.Done(); return; }

            _art = GroveArtLoader.Open("grove_visit", GroveArtLoader.Visit(PlacedPieceIds()),
                                       this, OnArtReady, _busy);
        });

        /// <summary>
        /// The card, or an empty one and a reason. Separated from <see cref="Open"/> so the two
        /// fetches can be awaited together without the result-tuple gymnastics that needs.
        /// </summary>
        async Task FetchCardAsync(CancellationToken cancellation)
        {
            var (result, card) = await GroveBoard.FetchCardAsync(_ownerId, cancellation);

            _failed = !result.Ok || card == null || !card.IsValid;
            _card = card ?? GroveCard.Empty;
        }

        void OnArtReady()
        {
            if (!Living) return;

            _busy?.Done();
            Repaint();
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

            var catalog = HomesteadCatalog.Current;
            var floor = catalog.Floor;

            _field.SetFloor(floor);

            GroveTileArt.Reach(catalog, out float up, out float side, out float down);
            _field.SetReach(up, side, down);

            ShowOwned();
            _field.Rebuild();

            // Opened on *this keeper's* hall — the seat on the card, not the floor's
            // constant. The constant is where every hall stood before a home could be moved
            // (invariant 16q), and a reader left pointing at it opens a visitor on the empty
            // ground a keeper moved their house away from, which reads as a grove with no
            // town hall. The card's own accessor carries the fallback, so a card that never
            // moved its hall still opens where the floor says.
            if (_card.HallSeat(floor, out int col, out int row))
                _field.CentreOn(floor.HallFootprint.CentreCol(col), floor.HallFootprint.CentreRow(row));
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
            // **.42, where it was .82.** A gradient this deep is a black bar across the top
            // of a daylight sky, and it was holding up nothing: the banner is a ribbon, the
            // two corner controls are skinned buttons and the summary carries a 3-unit
            // outline, so every element under it already earns its own contrast. What is left
            // is enough to seat the ribbon against the sky and not enough to say night.
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .06f, .09f, .42f));
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

            // When the picture was taken.
            //
            // A visited grove is drawn from a card the server rebuilt the last time its owner's
            // device synced, so it is never "now" and sometimes days old — and a visitor with
            // no way to tell cannot separate "this keeper has not played since Tuesday" from
            // "this game is showing me the wrong grove". The second reading was the one that
            // arrived, and it is the expensive one: a player who has decided a feature is
            // broken stops opening it. Small and dim on purpose — it is an answer to a question
            // somebody has, not a thing to look at.
            _age = UIKit.Shrinkable(
                UIKit.Titled("Age", chrome, string.Empty, 20, new Color(1f, .96f, .88f, .62f),
                             TextAnchor.MiddleCenter, new Vector2(640f, 26f),
                             new Vector2(.5f, 1f), new Vector2(0f, -218f), 2f, 0f), 14);

            // Small, quiet, and in the corner opposite Back. A report control is not something
            // a screen should invite — it is something a player has to be able to find once
            // they have already decided. So it is sized and coloured like chrome rather than
            // like an action, which is the same call the grove's score readout makes.
            _report = UIKit.TextButton("Report", chrome, Skins.Alternate,
                                       Loc.Get("ui.visit.report"), 24,
                                       new Vector2(232f, 72f),
                                       new Vector2(1f, 1f), new Vector2(-136f, -104f),
                                       OnReport);

            if (_report != null && _report.Label != null) UIKit.Shrinkable(_report.Label, 16);

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

            if (_age)
            {
                // Empty rather than guessed for a card with no stamp on it — see Since.Describe.
                string when = _card.IsValid
                    ? Since.Describe(_card.PublishedUnix, SaveSchema.NowUnix())
                    : string.Empty;

                _age.text = when.Length > 0 ? Loc.Format("ui.visit.updated", when) : string.Empty;
            }

            PaintReport();

            if (!_status) return;

            if (_card.IsValid) _status.text = string.Empty;
            else if (_fetching) _status.text = Loc.Get("ui.board.loading");
            else if (_failed) _status.text = Loc.Get("ui.visit.gone");
            else _status.text = Loc.Get("ui.board.loading");
        }

        // ---------------------------------------------------------------- report
        /// <summary>
        /// Whether the control is worth offering, and what it says.
        ///
        /// <para>
        /// <b>Hidden rather than greyed wherever it cannot work.</b> That is the inverse of
        /// <c>AdOfferState</c>'s rule and deliberate: that panel greys and explains because the
        /// player went looking for it, whereas nobody arrives at a grove wanting to report it,
        /// so a dead control here would only ever read as a broken game. There is nothing to
        /// report before the card lands, on the player's own grove, or with no boards at all.
        /// </para>
        /// <para>
        /// It stays visible and disabled once used, rather than vanishing. A control that
        /// disappears after a tap leaves somebody wondering whether the tap registered — which
        /// is the one question this feature must not leave open, since the obvious response is
        /// to report again.
        /// </para>
        /// </summary>
        void PaintReport()
        {
            if (_report == null) return;

            bool worth = _card.IsValid
                         && !string.IsNullOrEmpty(_ownerId)
                         && _ownerId != CloudState.UserId
                         && GroveBoard.IsAvailable;

            _report.gameObject.SetActive(worth);
            if (!worth) return;

            bool sent = NameReports.AlreadySent(_ownerId);

            _report.Interactable = !sent && !_reporting;

            if (_report.Label != null)
                _report.Label.text = Loc.Get(sent ? "ui.visit.report_sent" : "ui.visit.report");
        }

        void OnReport()
        {
            if (_reporting || NameReports.AlreadySent(_ownerId)) return;

            Flow.Modal<ReportNameOverlay>(v => v.OnConfirm = Send);
        }

        void Send() => Run(async token =>
        {
            if (_reporting) return;

            _reporting = true;
            PaintReport();

            // Captured before the await rather than read after it. `Flow.Go` builds a fresh
            // screen today so the field cannot move underneath this, which is exactly why it
            // would go unnoticed: the day somebody makes this screen reusable, a report fired
            // at one keeper lands against whoever the screen is showing when the reply arrives.
            // A moderation action recorded against the wrong account is not a failure worth
            // leaving one line away.
            string owner = _ownerId;

            var (result, outcome) = await GroveBoard.ReportNameAsync(owner, token);

            _reporting = false;

            // The screen can be gone: a report is the one call here a player can start and then
            // immediately walk away from, because the panel that raised it has already closed.
            if (!Living) return;

            PaintReport();

            // Three sentences for four outcomes, and the collapse is deliberate. Somebody who
            // reported a name twice is told exactly what somebody who reported it once is told,
            // because the difference is ours to care about and not theirs — see
            // NameReportOutcome for why the server tells the client so little.
            string line =
                !result.Ok || outcome == NameReportOutcome.Unavailable ? "ui.visit.report_failed"
                : outcome == NameReportOutcome.Throttled ? "ui.visit.report_limit"
                : "ui.visit.report_done";

            Scenery.Toast(Content, Loc.Get(line), Pal.Cream, 2.6f, new Vector2(.5f, 0f), 320f);
        });

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

            public RectTransform Ground { get; }

            public RectTransform Root { get; }

            public int Depth { get; private set; }

            public VisitTile(GroveVisitScreen screen)
            {
                _screen = screen;

                Ground = UIKit.Node("Tile", null);
                Ground.sizeDelta = new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight);

                _ground = UIKit.Img("G", Ground, null, Color.white,
                                    new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight),
                                    new Vector2(.5f, .5f), Vector2.zero);
                _ground.raycastTarget = false;
                _ground.preserveAspect = false;

                Root = UIKit.Node("Stand", null);
                Root.sizeDelta = new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight);

                _art = UIKit.Img("A", Root, null, Color.white, new Vector2(140f, 140f),
                                 new Vector2(.5f, .5f), Vector2.zero);
                _art.preserveAspect = true;
                _art.raycastTarget = false;
            }

            /// <summary>
            /// Laid out through <see cref="GroveTileArt"/>, the same function the player's own
            /// grove uses, so a visited grove and the same grove seen by its owner cannot differ.
            /// </summary>
            public void Bind(int col, int row)
            {
                var catalog = HomesteadCatalog.Current;

                GroveTileArt.LayGround(_ground, catalog.Floor);

                var index = _screen._card.Occupancy(catalog);
                bool anchored = index.TryAnchored(col, row, out var stand);

                var piece = anchored
                    ? (stand.IsHall ? _screen._card.Dwelling(catalog) : catalog.Find(stand.PieceId))
                    : default;

                Depth = anchored ? stand.Depth : GroveFootprint.Single.Depth(col, row);

                bool drawn = anchored && piece.IsValid;
                _art.gameObject.SetActive(drawn);
                if (drawn) GroveTileArt.LayPiece(_art, piece, stand);
            }
        }

        public override bool OnBack()
        {
            Flow.Go<LeaderboardScreen>();
            return true;
        }
    }
}
