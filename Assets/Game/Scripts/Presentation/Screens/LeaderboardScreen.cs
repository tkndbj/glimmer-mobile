using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Frames;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Social;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Where every keeper stands: one board, and the two ways into somebody else's game.
    ///
    /// <para>
    /// <b>The list is the screen.</b> The player's own standing used to be drawn above it in a
    /// box of its own — their grove's worth and where that put them — and it is gone: the same
    /// two numbers are on the profile, the row a player is looking for is lit in the list
    /// (<see cref="Row.Bind"/>), and a panel restating what the screen below it already says is
    /// a header the player scrolls past to reach the thing they came for.
    /// </para>
    /// <para>
    /// <b>One board, and the tabs went with the other one.</b> The Endless Watch is how far
    /// anybody has held the line on the Infinite lane (invariant 43). The finest groves — what
    /// a keeper has <em>built</em> — is <b>held</b> while the Grovement is rebuilt, and a hold
    /// is drawn by taking the board away rather than by greying a tab: a tab that cannot be
    /// tapped is the broken button invariant 16o refuses, and a lone tab is a caption wearing a
    /// control's clothes. <b>Nothing server-side moved</b> — <c>LeaderboardBoard.Global</c> is
    /// still a live id and <c>BOARD_IDS</c> still names it, so the document goes on being
    /// written and is never pruned (invariant 19k), and putting the board back is this screen
    /// alone. A board id that had been <em>spent</em> could not come back at all.
    /// </para>
    /// <para>
    /// <b>What used to stand where the second tab was is MY LEAGUE, and it is gone for good.</b>
    /// Nine more boards, nine queries and nine counts a night bought a second cut of the
    /// <em>same</em> number the global board is ordered on, into bands nothing in the game ever
    /// named — and "where do I stand" was already answered exactly by the published
    /// distribution (<see cref="GroveRanks"/>, invariant 19c), which is what the profile prints.
    /// Those ids are spent; the global board's is not.
    /// </para>
    /// <para>
    /// What is still deliberately missing is a "keepers near you" list, which needs an exact
    /// global ordering — the one thing this design refuses to maintain, and the reason the
    /// whole feature costs three scheduled documents.
    /// </para>
    /// <para>
    /// <b>Every refusal renders a sentence.</b> No backend, no session, opted out, nothing
    /// published yet, a board that has never been built, a fetch that failed — six states, and
    /// each says which one it is. That is <c>AdOfferState</c>'s rule and it is here for its
    /// reason: a screen that shows an empty list for six different causes teaches players that
    /// the feature is broken.
    /// </para>
    /// </summary>
    public sealed class LeaderboardScreen : View
    {
        public override string Track => "mus_menu";

        /// <summary>
        /// Everything above the list: the banner, and nothing else now. It was 470 while a
        /// standing box stood under the banner and 312 while two board tabs did; each removal
        /// moves everything below it up by its own height, so this moves with them or the list
        /// begins in a strip of empty sky.
        /// </summary>
        const float HeaderHeight = 208f;

        /// <summary>
        /// How tall a row is, and the badge is what decides it.
        ///
        /// <para>
        /// <b>132 while a row carried a companion's head, 176 now.</b> A portrait is a face and
        /// reads at any size; a rank badge is a piece of shaped metal whose whole meaning is in
        /// its silhouette — bronze against silver against gold, wings against spikes — and at
        /// 84 units the seven of them are one smudge. The badge is cut at 256 and drawn at 132
        /// on the map and the ranks page (<c>RankBadge.MarkSize</c>, <c>RanksScreen</c>), so a
        /// row that draws it smaller than every other screen in the game is the one place a
        /// player cannot tell two ranks apart.
        /// </para>
        /// <para>
        /// A taller row is fewer rows on a screen, which is the cost and is worth paying here:
        /// this list is scrolled rather than scanned, and it is a hundred rows long however
        /// many of them are visible at once.
        /// </para>
        /// </summary>
        const float RowHeight = 176f;

        /// <summary>The plate inside a row, and the badge on it. See <see cref="RowHeight"/>.</summary>
        const float PlateWidth = 940f, PlateHeight = 160f, BadgeSize = 132f;

        // **The name frame on the player's own row covers the whole plate.** As wide as the
        // plate and three to one like the paintings are, so it stands 313 tall on a 160 plate
        // with its hole centred on the plate (`FrameY` lifts the painting so the hole, which
        // sits a little below its middle, lands on the row's centre line). Everything the row
        // says — place, badge, name, figure — moves inside the hole, and the row is raised over
        // its neighbours because the dragon and the flames overhang them (`Row.Seat`). It was
        // 640 wide over the plate to the right of the badge first, and the owner asked for the
        // whole card (2026-09-22). Mirrored by render_boards.py.
        const float FrameW = PlateWidth, FrameH = FrameW / 3f, FrameX = PlateWidth * .5f;
        const float FrameY = 7f;

        /// <summary>
        /// Breathing room between the last row and the nav bar. The boards are a tab now, so
        /// the bar is drawn here exactly as it is on the shop, and the list stops above it —
        /// a row half-covered by the bar is a row nobody can tap.
        /// </summary>
        const float BottomPad = 24f;

        /// <summary>
        /// Which board is being drawn. One of <see cref="LeaderboardBoard.All"/>, and while the
        /// finest groves are held it is always <see cref="LeaderboardBoard.Endless"/>.
        /// </summary>
        string _boardId;

        RectTransform _viewport;
        GridView _grid;
        Text _empty;

        LeaderboardBoard _board = LeaderboardBoard.None;
        bool _fetching;
        bool _failed;

        protected override void Build()
        {
            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 14, new Color(1f, .93f, .70f), 6f, 20f);

            // The Endless Watch, because it is the only board on offer while the finest
            // groves are held. It is a board a player has to have gone and played a lane to be
            // on, which is the one thing holding the other cost: an account that has never run
            // the Infinite lane opens on a list it cannot be in, and `PaintEmpty` is what keeps
            // that honest rather than blank.
            if (!Offered(_boardId)) _boardId = LeaderboardBoard.Endless;

            BuildList();
            BuildHeader();
            NavBar.Build(Content, NavBar.Tab.Ranks);

            // **Nothing is opened for the badges, and that is the point of them being badges.**
            // A row used to draw a companion, whose art is a scope this screen had to hold open
            // for its whole life and repaint when it arrived (invariant 7b). The seven rank
            // badges are small and global — they are already resident for the map's own readout
            // — so a hundred rows cost no scope, no hold, no arrival repaint and no white
            // rectangle while a load is in flight.

            // Asked for on arrival rather than at boot, so a player who never opens this
            // screen never pays for the read. Nothing here draws it any more — the profile is
            // where the percentile is said — but this is still the screen a player reaches
            // first, and the fetch is once a session however many times it is asked for.
            GroveBoard.BeginRanksRefresh();

            GroveBoard.Published += OnPublished;

            // **The one frame this screen draws is the player's own**, and it is the one thing
            // on the page that is a scope (7b): a name frame is a 2048-wide painting, so it is
            // held here for the screen's life and the grid is refreshed when it lands. A
            // stranger's frame is not on the published card yet, so no other row wears one.
            FrameLedger.Changed += OnFrameChanged;
            HoldFrame();

            Fetch();
        }

        void OnDestroy()
        {
            GroveBoard.Published -= OnPublished;
            FrameLedger.Changed -= OnFrameChanged;
            _frameArt?.Dispose();
            _frameArt = null;
        }

        AssetHold _frameArt;

        void OnFrameChanged()
        {
            if (!Living) return;
            HoldFrame();
        }

        void HoldFrame() => Run(async token =>
        {
            var worn = FrameLedger.Worn;
            _frameArt = _frameArt ?? AssetLibrary.Hold("board-frame");
            await _frameArt.LoadAsync(worn != null
                                          ? AssetManifest.FrameAssets(worn.Id)
                                          : new System.Collections.Generic.List<AssetRequest>(),
                                      null, token);
            if (!Living) return;
            _grid?.Refresh();
        });

        /// <summary>Opens straight onto a particular board. Used by nothing yet; kept for a deep link.</summary>
        public void ShowBoard(string boardId)
        {
            if (Offered(boardId)) _boardId = boardId;
        }

        /// <summary>
        /// Whether this screen will draw a board at all.
        ///
        /// Narrower than <see cref="LeaderboardBoard.IsKnown"/> on purpose, and named for its
        /// narrowness (invariant 15a): the global board is still a perfectly valid id the server
        /// still writes, and what has changed is only that nothing here offers it. A deep link
        /// carrying it lands on the board this screen does draw rather than on a list with no
        /// way off it.
        /// </summary>
        static bool Offered(string boardId) => LeaderboardBoard.IsEndless(boardId);

        // ------------------------------------------------------------------ header
        void BuildHeader()
        {
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .06f, .09f, .82f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            frt.sizeDelta = new Vector2(0f, (HeaderHeight + 30f) + SafeArea.Top);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);

            var chrome = Safe;

            var banner = Scenery.TitleRibbon(chrome, Loc.Get("ui.board.title").ToUpperInvariant(),
                                             new Vector2(470f, 128f), new Vector2(.5f, 1f),
                                             new Vector2(0f, -106f), 38, 20f);
            banner.transform.localScale = Vector3.zero;
            Tween.Pop(banner.transform, 0f, .6f, .1f);

            // Home, not the Grovement. This is a tab of its own now and can be reached from
            // the bar on any screen, so the one destination that is right however the player
            // arrived is the way back — ShopScreen's rule, and the other tabs are one tap
            // away in the bar below regardless.
            UIKit.IconButton("Back", chrome, Skins.Nav, "ic_left", new Vector2(112f, 112f),
                             new Vector2(0f, 1f), new Vector2(92f, -104f),
                             () => Flow.Go<HomeScreen>());

            // The corner opposite the way out, which is where every other page in this game
            // keeps its explanation (the tasks page, the streak, the map, the loadout).
            //
            // **It is here because a board is a tally rather than a live reading**, and that
            // is the one fact this screen cannot say by drawing itself: a keeper who has just
            // spent thirty thousand credits and finds the list unmoved has two readings
            // available, "the boards are broken" and "what I built did not count", and both
            // are wrong. The panel is handed the board already in hand rather than fetching
            // one, so it can name when *this* tally was taken without spending a read.
            UIKit.IconButton("Info", chrome, Skins.Aside, "ic_info", new Vector2(112f, 112f),
                             new Vector2(1f, 1f), new Vector2(-92f, -104f),
                             () => { if (!Flow.HasModal) Flow.Modal<RanksInfoOverlay>(panel => panel.Board = _board); });

            // **The board caption and the two tabs are both gone, and for two different
            // reasons.** The caption said how many keepers the board holds, which is a fact
            // about the population rather than about the player's standing. The tabs chose
            // between two boards and there is one, so what they would draw now is a pair of
            // plates of which one refuses and one re-enters the screen you are standing on —
            // and a lone tab is a caption that looks like a control. The board this screen
            // draws is named by the rows themselves (`Row.Bind` prints the figure it is
            // ordered on) and by the info panel in the corner.
        }

        // -------------------------------------------------------------------- list
        void BuildList()
        {
            _viewport = UIKit.Node("Viewport", Safe);
            _viewport.offsetMin = new Vector2(0f, NavBar.Height + BottomPad);
            _viewport.offsetMax = new Vector2(0f, -HeaderHeight);

            _grid = GridView.Attach(_viewport, 1, 960f, RowHeight,
                                    parent => new Row(this, parent));

            _empty = UIKit.Shrinkable(
                UIKit.Titled("Empty", Safe, string.Empty, 28, new Color(1f, .96f, .88f, .72f),
                             TextAnchor.UpperCenter, new Vector2(760f, 200f), new Vector2(.5f, 1f),
                             new Vector2(0f, -(HeaderHeight + 60f)), 3f, 0f), 18);
            _empty.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        void Fetch() => Run(async token =>
        {
            if (_fetching) return;

            _fetching = true;
            _failed = false;
            PaintEmpty();

            var (result, board) = await GroveBoard.FetchBoardAsync(_boardId, token);

            _fetching = false;
            if (!Living) return;                     // the screen went away while we waited

            _failed = !result.Ok;
            _board = board ?? LeaderboardBoard.None;

            // A new list, so it animates. A repaint of the same list does not — the rule
            // GridView exists to keep, and the reason the shop stopped flickering.
            _grid?.Show(_board.Entries.Count);
            PaintEmpty();
        });

        void OnPublished()
        {
            // Our own row may have moved, and the cached boards were dropped when the card
            // was published, so this is a real fetch rather than a redraw.
            Fetch();
        }

        // ------------------------------------------------------------------ copy
        /// <summary>
        /// Six ways for a list to be empty, and each says which one it is.
        ///
        /// A board with rows on it is the only case that draws nothing here — which is why the
        /// label is cleared last rather than first.
        /// </summary>
        void PaintEmpty()
        {
            if (!_empty) return;

            if (_board.Entries.Count > 0) { _empty.text = string.Empty; return; }

            if (!GroveBoard.IsAvailable) _empty.text = Loc.Get("ui.board.offline");
            else if (_fetching) _empty.text = Loc.Get("ui.board.loading");

            // **A fetch that failed because this phone has no signal is a different sentence
            // from one that failed, and only one of them tells the player what to do.** Both
            // are true when the radio is off; "the boards could not be reached, try again in a
            // moment" reads as *the game is broken* to somebody sitting in a tunnel, and it is
            // the reading they keep, because they will not try again in a moment. The radio is
            // asked *after* the request has already failed rather than before it is made —
            // see `Net`, which may never refuse anything, only explain something that has.
            else if (_failed)
                _empty.text = Loc.Get(Net.Offline ? "ui.board.no_connection" : "ui.board.failed");

            else _empty.text = Loc.Get("ui.board.no_rows");
        }

        // ------------------------------------------------------------------- rows
        /// <summary>
        /// Opens the keeper a row names.
        ///
        /// <para>
        /// <b>Straight in, because a row leads to one place again.</b> It used to walk into a
        /// stranger's grovement; then there were two destinations and <c>KeeperOverlay</c> was
        /// the chooser between them, because a row that silently picked one would leave the
        /// other reachable from nowhere. The grovement is <b>held</b> while it is rebuilt, so
        /// there is one door left — and a chooser with one door in it is a confirmation for a
        /// free navigation, which this game keeps to exactly three (none of them this).
        /// </para>
        /// <para>
        /// <b>What that costs is where the row said <em>who</em> it is at a readable size</b>,
        /// which the panel was worth having for as much as for the choice. The profile says the
        /// same thing one tap in and says more of it, so the loss is an introduction rather than
        /// an answer. <c>KeeperOverlay</c> is left standing untouched beside the screens it
        /// opened, for <c>NavBar.Order</c>'s reason: putting the chooser back is this method.
        /// </para>
        /// </summary>
        void Visit(LeaderboardEntry entry)
        {
            if (!entry.IsValid) return;

            // Silent, for the row's own reason: it is a button and has already clicked.
            Flow.Go<PublicProfileScreen>(v => v.Show(entry.OwnerId, entry));
        }

        /// <summary>
        /// One row: place, badge, name, figure.
        ///
        /// <para>
        /// Built once and rebound as it scrolls (<see cref="GridView"/>), so a hundred-row
        /// board costs the same objects as a five-row one. That is invariant 16d, and it
        /// applies here for the reason it applies to the shop: this list is bounded today at a
        /// hundred and is exactly the kind of thing a later drop lengthens.
        /// </para>
        /// </summary>
        sealed class Row : IGridCell
        {
            readonly LeaderboardScreen _screen;
            readonly Image _plate, _badge;
            readonly Text _place, _name, _worth;
            readonly Btn _button;
            readonly NameFrame _frame;

            LeaderboardEntry _entry;

            // **Where the name and the figure stand: bare, and inside a frame.** Bare is where
            // every row has always put them. Framed, the frame takes the whole of the plate to
            // the right of the badge (`FrameX` is the middle of that span), overhangs the plate
            // by a horn's worth above and below, and the two lines move into its hole at a
            // size the hole can hold. Both layouts are written on every bind (44l, 44mc): a
            // recycled cell rebound from the player's row to a stranger's would otherwise keep
            // the smaller type and the hole's seat.
            const float NameX = 515f, NameY = 24f, NameH = 50f;
            const float WorthX = 515f, WorthY = -26f, WorthH = 40f;
            const int NameSize = 34, WorthSize = 28;
            const float BareW = 430f;
            const float PlaceX = 78f, PlaceW = 120f, PlaceH = 66f;
            const int PlaceSize = 40;
            const float BadgeX = 214f;

            static readonly Vector2 NameBare = new Vector2(NameX, NameY);
            static readonly Vector2 WorthBare = new Vector2(WorthX, WorthY);
            static readonly Vector2 PlaceBare = new Vector2(PlaceX, 0f);
            static readonly Vector2 BadgeBare = new Vector2(BadgeX, 0f);

            // Inside the hole (619 by 123 at the plate's width): the place, the badge and the
            // two lines in a row, each sized to the hole's height rather than the plate's.
            const float FramedPlaceX = 50f, FramedPlaceW = 84f, FramedPlaceH = 60f;
            const int FramedPlaceSize = 34;
            const float FramedBadgeX = 150f, FramedBadgeSize = 104f;
            const float FramedTextLeft = 214f, FramedTextRight = 16f;
            const int FramedNameSize = 30, FramedWorthSize = 24;
            const float FramedNameH = 40f, FramedWorthH = 32f;

            public RectTransform Root { get; }

            public Row(LeaderboardScreen screen, RectTransform parent)
            {
                _screen = screen;

                Root = UIKit.Node("Row", parent);
                Root.sizeDelta = new Vector2(960f, RowHeight);

                // **The hub's own plate, which is the Battle key's mould sliced both ways.** A
                // row was a translucent near-black box with a 12%-white outline traced round it,
                // and a hundred of those down a screen is the "simple dark frames" this replaced.
                // Whose row it is is the *sprite* rather than a tint, for the reason the nav bar
                // and both shops now share: the mould paints its own keyline, its own two-tone
                // face and its own highlight, and none of those survives being multiplied by a
                // colour.
                _button = UIKit.Button("Hit", Root, Art.S("Ui/" + Skins.PlateBlue),
                                       new Vector2(PlateWidth, PlateHeight),
                                       new Vector2(.5f, .5f), Vector2.zero, Open);
                _plate = _button.GetComponent<Image>();

                _place = UIKit.Shrinkable(
                    UIKit.Titled("Place", _plate.transform, string.Empty, 40,
                                 Pal.Cream, TextAnchor.MiddleCenter,
                                 new Vector2(120f, 66f), new Vector2(0f, .5f), new Vector2(78f, 0f),
                                 3f, 2f), 20);

                // The badge. `preserveAspect`, because the seven are not one shape: a crowned
                // one is wider than it is tall and a plain hexagon is not, and a set stretched
                // to a square box is a ladder whose rungs are different animals. It never draws
                // a raycast, so the whole plate stays one target.
                _badge = UIKit.Img("Badge", _plate.transform, null, Color.white,
                                   new Vector2(BadgeSize, BadgeSize), new Vector2(0f, .5f),
                                   new Vector2(214f, 0f));
                _badge.preserveAspect = true;
                _badge.raycastTarget = false;

                // The frame, built once and shown on the player's own row only. It stands
                // *under* the name and the figure in sibling order, so both print over its
                // hole rather than under its bars.
                _frame = NameFrame.Build("Frame", _plate.transform, new Vector2(FrameW, FrameH),
                                         new Vector2(0f, .5f), new Vector2(FrameX, FrameY));

                _name = UIKit.Shrinkable(
                    UIKit.Titled("Name", _plate.transform, string.Empty, NameSize,
                                 new Color(1f, .97f, .90f), TextAnchor.MiddleLeft,
                                 new Vector2(BareW, NameH), new Vector2(0f, .5f), NameBare,
                                 3f, 2f), 20);

                _worth = UIKit.Shrinkable(
                    UIKit.Titled("Worth", _plate.transform, string.Empty, WorthSize, Pal.Gold,
                                 TextAnchor.MiddleLeft, new Vector2(BareW, WorthH),
                                 new Vector2(0f, .5f), WorthBare, 3f, 0f), 18);
            }

            /// <summary>
            /// Stand the name and the figure bare, or inside the frame's hole. A framed row is
            /// also raised to the top of its siblings, because the frame overhangs the plate
            /// and a neighbour built later would otherwise draw over the dragon's horns (44mc,
            /// read the other way up: this cell rises rather than sinks).
            /// </summary>
            void Seat(FrameDefinition frame)
            {
                bool framed = frame != null && _frame.Drawn;
                var placeRt = (RectTransform)_place.transform;
                var badgeRt = (RectTransform)_badge.transform;
                var nameRt = (RectTransform)_name.transform;
                var worthRt = (RectTransform)_worth.transform;

                if (!framed)
                {
                    Place(placeRt, PlaceBare, PlaceW, PlaceH);
                    Place(badgeRt, BadgeBare, BadgeSize, BadgeSize);
                    Place(nameRt, NameBare, BareW, NameH);
                    Place(worthRt, WorthBare, BareW, WorthH);
                    Size(_place, PlaceSize);
                    Size(_name, NameSize);
                    Size(_worth, WorthSize);
                    return;
                }

                // The hole in the plate's own coordinates: the frame is anchored at its centre.
                var hole = NameFrame.HoleIn(frame, new Vector2(FrameW, FrameH));
                float x0 = FrameX + hole.xMin;
                float cy = FrameY + hole.center.y;
                float left = x0 + FramedTextLeft;
                float width = FrameX + hole.xMax - FramedTextRight - left;
                float split = cy + 4f;                          // the name above, the figure below

                Place(placeRt, new Vector2(x0 + FramedPlaceX, cy), FramedPlaceW, FramedPlaceH);
                Place(badgeRt, new Vector2(x0 + FramedBadgeX, cy), FramedBadgeSize, FramedBadgeSize);
                Place(nameRt, new Vector2(left + width * .5f, split + FramedNameH * .5f), width, FramedNameH);
                Place(worthRt, new Vector2(left + width * .5f, split - FramedWorthH * .5f), width, FramedWorthH);
                Size(_place, FramedPlaceSize);
                Size(_name, FramedNameSize);
                Size(_worth, FramedWorthSize);
                Root.SetAsLastSibling();
            }

            static void Place(RectTransform rt, Vector2 pos, float w, float h)
            {
                rt.anchoredPosition = pos;
                rt.sizeDelta = new Vector2(w, h);
            }

            static void Size(Text text, int size)
            {
                text.fontSize = size;
                text.resizeTextMaxSize = size;
            }

            public void Bind(int index)
            {
                var entries = _screen._board.Entries;
                _entry = index >= 0 && index < entries.Count ? entries[index] : default;

                _place.text = _entry.Rank.ToString();
                _name.text = _entry.Name;

                // What a row says is the board's decision and not the row's, because every row
                // on one list says the same thing — see `LeaderboardBoard.IsEndless`. The figure
                // a board is *ordered* on is the one it has to print, or the list reads as
                // shuffled: a wave board drawn with grove worth on it would descend by a number
                // nobody can see and ascend by one they can.
                _worth.text = LeaderboardBoard.IsEndless(_screen._boardId)
                    ? Loc.Format("ui.board.row_wave", _entry.Wave, _entry.KeeperLevel)
                    : Loc.Format("ui.board.row_worth", Compact.Number(_entry.Score),
                                 _entry.KeeperLevel);

                // **The badge is the server's answer, and an absent one draws nothing.** Below
                // the first rung is an ordinary state and so is a card published before this
                // server learned to derive a rung, and neither may be drawn as a greyed picture
                // of somebody else's badge — that would be a sentence the row does not mean.
                // `RankArt` switches the node off rather than handing it a null sprite, which
                // is a white rectangle (invariant 7b) and would be one on every row at once.
                RankArt.Paint(_badge, _entry.RungId);

                // The player's own row is lit rather than merely present. A list somebody is
                // on and cannot find is a list that did not answer the question they opened it
                // with — and this is the one row on the screen they are looking for.
                bool mine = !string.IsNullOrEmpty(CloudState.UserId)
                         && _entry.OwnerId == CloudState.UserId;

                _plate.sprite = Art.S("Ui/" + (mine ? Skins.PlateOrange : Skins.PlateBlue));

                // The frame the player wears, on their row and no other. `Show` asks for the
                // painting and draws nothing until the screen's hold has landed it, and the
                // seat is written either way (44l): a stranger's row rebound onto this cell
                // must take the frame off *and* put the lines back where they stand bare.
                // And on no row at all while the feature is withheld (`FramesScreen.Offered`).
                var frame = mine && FramesScreen.Offered ? FrameLedger.Worn : null;
                _frame.Show(frame);
                Seat(frame);
            }

            void Open() => _screen.Visit(_entry);
        }

        public override bool OnBack()
        {
            Flow.Go<HomeScreen>();
            return true;
        }
    }
}
