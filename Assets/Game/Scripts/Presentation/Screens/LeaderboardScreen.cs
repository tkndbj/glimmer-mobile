using System.Collections.Generic;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Social;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Where every keeper's grove stands: two boards, a standing, and a way into somebody
    /// else's village.
    ///
    /// <para>
    /// <b>The standing comes first and the list comes second, and that ordering is the whole
    /// design.</b> A hundred rows is a hundred rows however many players there are, so on any
    /// board worth having almost nobody is on it — and a screen that leads with a list the
    /// player is not on has told them, at a glance, that this feature is not about them. The
    /// percentile is about them, it is available to everybody from the first grove they buy
    /// anything for, and it comes from one small document rather than from an ordering nothing
    /// maintains. See <see cref="GroveRankTable"/>.
    /// </para>
    /// <para>
    /// <b>Two boards and no third.</b> The global hundred is aspirational; a league board is
    /// reachable, because a league is the star rating already drawn over the player's own
    /// grove (<see cref="GroveLeague"/>). What is deliberately missing is a "keepers near you"
    /// list, which needs an exact global ordering — the one thing this design refuses to
    /// maintain, and the reason the whole feature costs two scheduled documents.
    /// </para>
    /// <para>
    /// <b>Every refusal renders a sentence.</b> No backend, no session, opted out, nothing
    /// published yet, a board that has never been built, a fetch that failed — six states, and
    /// each says which one it is. That is <c>AdOfferState</c>'s rule and it is here for its
    /// reason: a screen that shows an empty list for six different causes teaches players that
    /// the feature is broken.
    /// </para>
    /// </summary>
    public sealed class LeaderboardScreen : View, IDrawsCompanionArt
    {
        public override string Track => "mus_menu";

        const float HeaderHeight = 470f;
        const float RowHeight = 132f;

        /// <summary>
        /// Breathing room between the last row and the nav bar. The boards are a tab now, so
        /// the bar is drawn here exactly as it is on the shop, and the list stops above it —
        /// a row half-covered by the bar is a row nobody can tap.
        /// </summary>
        const float BottomPad = 24f;

        /// <summary>Which board is being drawn. Empty means the player's own league.</summary>
        string _boardId;

        RectTransform _viewport;
        GridView _grid;
        Text _standing, _empty, _boardCaption;
        Btn _globalTab, _leagueTab;

        LeaderboardBoard _board = LeaderboardBoard.None;
        bool _fetching;
        bool _failed;

        protected override void Build()
        {
            Scenery.Layered(Content, "home", .26f);
            Fireflies.Spawn(Content, 14, new Color(1f, .93f, .70f), 6f, 20f);

            // Finest groves by default, and deliberately: the league is where the player is
            // already standing — it is their own star rating, drawn over their own grove —
            // so opening onto it shows them what they mostly know. The global hundred is the
            // aspirational half of the feature and the one worth arriving on, and the
            // standing above it is what keeps the screen about the player either way.
            if (string.IsNullOrEmpty(_boardId)) _boardId = LeaderboardBoard.Global;

            BuildList();
            BuildHeader();
            NavBar.Build(Content, NavBar.Tab.Ranks);

            // Portraits, for the row avatars. Arriving from the profile or the roster the
            // scope is usually warm and this repaints immediately.
            CompanionArt.OpenAsync(() => { if (this) Repaint(); });

            // The distribution decides the one line the player is actually here for, so it is
            // asked for on arrival rather than at boot: a player who never opens this screen
            // should never pay for the read.
            GroveBoard.BeginRanksRefresh();

            GroveRanks.Changed += Repaint;
            GroveBoard.Published += OnPublished;

            Fetch();
            PaintStanding();
        }

        void OnDestroy()
        {
            GroveRanks.Changed -= Repaint;
            GroveBoard.Published -= OnPublished;

            if (Flow.Current is ProfileScreen || Flow.Current is CompanionScreen) return;
            CompanionArt.CloseUnlessWanted();
        }

        /// <summary>Opens straight onto a particular board. Used by nothing yet; kept for a deep link.</summary>
        public void ShowBoard(string boardId)
        {
            if (LeaderboardBoard.IsKnown(boardId)) _boardId = boardId;
        }

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

            var banner = UIKit.Img("Banner", chrome, Art.S("Ui/banner"), Color.white,
                                   new Vector2(430f, 114f), new Vector2(.5f, 1f), new Vector2(0f, -102f));
            UIKit.Shrinkable(
                UIKit.Titled("Title", banner.transform, Loc.Get("ui.board.title").ToUpperInvariant(), 32,
                             new Color(.36f, .24f, .16f), TextAnchor.MiddleCenter,
                             new Vector2(300f, 46f), new Vector2(.5f, .5f),
                             new Vector2(0f, 114f * UIKit.PillFaceLift), 0f, 2f), 20);
            banner.transform.localScale = Vector3.zero;
            Tween.Pop(banner.transform, 0f, .6f, .1f);

            // Home, not the Grovement. This is a tab of its own now and can be reached from
            // the bar on any screen, so the one destination that is right however the player
            // arrived is the way back — ShopScreen's rule, and the other four tabs are one
            // tap away in the bar below regardless.
            UIKit.IconButton("Back", chrome, Skins.Nav, "ic_left", new Vector2(112f, 112f),
                             new Vector2(0f, 1f), new Vector2(92f, -104f),
                             () => Flow.Go<HomeScreen>());

            BuildStanding(chrome);
            BuildTabs(chrome);

            _boardCaption = UIKit.Shrinkable(
                UIKit.Titled("BoardCaption", chrome, string.Empty, 22,
                             new Color(1f, .96f, .88f, .62f), TextAnchor.MiddleCenter,
                             new Vector2(900f, 30f), new Vector2(.5f, 1f), new Vector2(0f, -444f),
                             3f, 0f), 15);
        }

        /// <summary>
        /// The player's own row, and the line that says where they stand.
        ///
        /// <para>
        /// Drawn from the local derivation rather than from the published card, and
        /// deliberately: a player who has just bought something must see it here immediately,
        /// and the publish is debounced by ten seconds (<see cref="GrovePublishPolicy"/>). The
        /// two agree for every honest player — the server recomputes the same summation from
        /// the same sets — so the local number is a prediction that is almost always exactly
        /// right, which is the same bargain every balance in this game already makes.
        /// </para>
        /// </summary>
        void BuildStanding(Transform chrome)
        {
            var size = new Vector2(900f, 150f);
            var box = UIKit.Img("Standing", chrome, Art.Round(28), new Color(.06f, .12f, .17f, .74f),
                                size, new Vector2(.5f, 1f), new Vector2(0f, -246f));
            var rt = (RectTransform)box.transform;

            var edge = UIKit.Img("Edge", rt, Art.RoundOutline(28, 3f), new Color(1f, 1f, 1f, .13f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            var standing = GroveScore.Of(HomesteadCatalog.Current);

            UIKit.Shrinkable(
                UIKit.Titled("Worth", rt, Compact.Number(standing.Score), 46, Pal.Gold,
                             TextAnchor.MiddleCenter, new Vector2(300f, 56f),
                             new Vector2(0f, .5f), new Vector2(170f, 18f), 4f, 4f), 26);

            UIKit.Shrinkable(
                UIKit.Titled("WorthLabel", rt, Loc.Get("ui.grove.score").ToUpperInvariant(), 20,
                             new Color(1f, .96f, .88f, .62f), TextAnchor.MiddleCenter,
                             new Vector2(300f, 26f), new Vector2(0f, .5f), new Vector2(170f, -32f),
                             3f, 0f), 14);

            _standing = UIKit.Shrinkable(
                UIKit.Titled("Standing", rt, string.Empty, 30, new Color(1f, .96f, .88f, .92f),
                             TextAnchor.MiddleCenter, new Vector2(500f, 96f),
                             new Vector2(1f, .5f), new Vector2(-270f, 0f), 3f, 2f), 18);
            _standing.horizontalOverflow = HorizontalWrapMode.Wrap;

            rt.localScale = Vector3.zero;
            Tween.Pop(rt, 0f, .5f, .16f);
        }

        void BuildTabs(Transform chrome)
        {
            var size = new Vector2(300f, 88f);

            _globalTab = UIKit.TextButton("Global", chrome, "btn_orange",
                                          Loc.Get("ui.board.global"), 28, size,
                                          new Vector2(.5f, 1f), new Vector2(-158f, -374f),
                                          () => Select(LeaderboardBoard.Global));
            UIKit.Shrinkable(_globalTab.Label, 18);
            UIKit.FitLabel(_globalTab);

            _leagueTab = UIKit.TextButton("League", chrome, Skins.Alternate,
                                          Loc.Get("ui.board.league"), 28, size,
                                          new Vector2(.5f, 1f), new Vector2(158f, -374f),
                                          () => Select(GroveBoard.MyLeagueId()));
            UIKit.Shrinkable(_leagueTab.Label, 18);
            UIKit.FitLabel(_leagueTab);

            StyleTabs();
        }

        /// <summary>
        /// Which tab is live, said with the skin the rest of the game already uses for "this
        /// is the affirmative one". The plate is read off the button rather than held, because
        /// a second reference to it is a second thing that can be pointed at the wrong object.
        /// </summary>
        void StyleTabs()
        {
            bool global = _boardId == LeaderboardBoard.Global;

            Skin(_globalTab, global);
            Skin(_leagueTab, !global);
        }

        static void Skin(Btn tab, bool live)
        {
            if (!tab) return;

            // "Ui/" because a skin name is a name, not an address. `UIKit.TextButton` adds the
            // folder for its callers, so the bare names in `Skins` only ever reach `Art.S`
            // through it — and this is the one place that reached past it. Without the prefix
            // there is no location for the key, so both tabs threw and then drew with **no
            // sprite at all**, which is a white rectangle rather than a missing decoration
            // (invariant 7b). `StreakScreen.Skin` writes it the same way.
            var plate = tab.GetComponent<Image>();
            if (plate) plate.sprite = Art.S("Ui/" + (live ? "btn_orange" : Skins.Alternate));
        }

        void Select(string boardId)
        {
            if (!LeaderboardBoard.IsKnown(boardId) || boardId == _boardId) return;

            _boardId = boardId;
            StyleTabs();
            Audio.Sfx("pop", .5f);

            Fetch();
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

        async void Fetch()
        {
            if (_fetching) return;

            _fetching = true;
            _failed = false;
            PaintEmpty();

            var (result, board) = await GroveBoard.FetchBoardAsync(_boardId);

            _fetching = false;
            if (!this) return;                       // the screen went away while we waited

            _failed = !result.Ok;
            _board = board ?? LeaderboardBoard.None;

            // A new list, so it animates. A repaint of the same list does not — the rule
            // GridView exists to keep, and the reason the shop stopped flickering.
            _grid?.Show(_board.Entries.Count);
            PaintEmpty();
            PaintCaption();
            PaintStanding();
        }

        void OnPublished()
        {
            // Our own row may have moved, and the cached boards were dropped when the card
            // was published, so this is a real fetch rather than a redraw.
            PaintStanding();
            Fetch();
        }

        void Repaint()
        {
            _grid?.Refresh();
            PaintStanding();
            PaintCaption();
        }

        // ------------------------------------------------------------------ copy
        /// <summary>
        /// The line under the player's own worth, and it says exactly one true thing.
        ///
        /// <para>
        /// Upward only, which is <see cref="LevelStats.IsWorthSaying"/>'s argument in the one
        /// place it matters most: this screen exists to make somebody want to build, and being
        /// told they are behind almost everybody is the single most reliable way to make them
        /// stop. So a grove worth nothing gets an invitation rather than a percentile, and a
        /// population too small to speak from gets silence rather than a number.
        /// </para>
        /// </summary>
        void PaintStanding()
        {
            if (!_standing) return;

            var standing = GroveScore.Of(HomesteadCatalog.Current);

            if (!GroveBoard.IsAvailable)
            {
                _standing.text = Loc.Get("ui.board.offline");
                return;
            }

            if (!GroveBoard.OptedIn)
            {
                _standing.text = Loc.Get("ui.board.opted_out");
                return;
            }

            if (standing.Score < GrovePublishPolicy.Worth)
            {
                _standing.text = Loc.Get("ui.board.unranked");
                return;
            }

            int rank = _board.RankOf(CloudState.UserId);
            if (rank > 0)
            {
                _standing.text = Loc.Format("ui.board.placed", rank);
                return;
            }

            int top = GroveRanks.Table.TopPercent(standing.Score);
            _standing.text = top > 0
                ? Loc.Format("ui.board.top_percent", top)
                : Loc.Get("ui.board.building");
        }

        void PaintCaption()
        {
            if (!_boardCaption) return;

            if (_board.Population > 0)
            {
                _boardCaption.text = _boardId == LeaderboardBoard.Global
                    ? Loc.Format("ui.board.of_keepers", Compact.Number(_board.Population))
                    : Loc.Format("ui.board.of_league",
                                 Loc.Get(GroveLeague.NameKey(GroveLeague.StarsOf(_boardId))),
                                 Compact.Number(_board.Population));
                return;
            }

            _boardCaption.text = string.Empty;
        }

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
            else if (_failed) _empty.text = Loc.Get("ui.board.failed");
            else _empty.text = Loc.Get("ui.board.no_rows");
        }

        // ------------------------------------------------------------------- rows
        void Visit(LeaderboardEntry entry)
        {
            if (!entry.IsValid) return;

            Audio.Sfx("pop", .5f);
            Flow.Go<GroveVisitScreen>(screen => screen.Visit(entry.OwnerId, entry.Name));
        }

        /// <summary>
        /// One row: place, portrait, name, worth.
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
            readonly Image _plate, _edge, _portrait;
            readonly Text _place, _name, _worth;
            readonly Btn _button;

            LeaderboardEntry _entry;

            public RectTransform Root { get; }

            public Row(LeaderboardScreen screen, RectTransform parent)
            {
                _screen = screen;

                Root = UIKit.Node("Row", parent);
                Root.sizeDelta = new Vector2(960f, RowHeight);

                _button = UIKit.Button("Hit", Root, Art.Round(26), new Vector2(940f, 118f),
                                       new Vector2(.5f, .5f), Vector2.zero, Open);
                _plate = _button.GetComponent<Image>();
                _plate.color = new Color(.06f, .12f, .17f, .70f);

                _edge = UIKit.Img("Edge", _plate.transform, Art.RoundOutline(26, 3f),
                                  new Color(1f, 1f, 1f, .12f));
                UIKit.StretchTo((RectTransform)_edge.transform, 0, 0, 0, 0);

                _place = UIKit.Shrinkable(
                    UIKit.Titled("Place", _plate.transform, string.Empty, 34,
                                 new Color(1f, .96f, .88f, .86f), TextAnchor.MiddleCenter,
                                 new Vector2(110f, 60f), new Vector2(0f, .5f), new Vector2(74f, 0f),
                                 3f, 2f), 18);

                _portrait = UIKit.Img("Portrait", _plate.transform, null, Color.white,
                                      new Vector2(84f, 84f), new Vector2(0f, .5f), new Vector2(180f, 0f));
                _portrait.preserveAspect = true;

                _name = UIKit.Shrinkable(
                    UIKit.Titled("Name", _plate.transform, string.Empty, 30,
                                 new Color(1f, .97f, .90f), TextAnchor.MiddleLeft,
                                 new Vector2(400f, 44f), new Vector2(0f, .5f), new Vector2(440f, 16f),
                                 3f, 2f), 18);

                _worth = UIKit.Shrinkable(
                    UIKit.Titled("Worth", _plate.transform, string.Empty, 26, Pal.Gold,
                                 TextAnchor.MiddleLeft, new Vector2(400f, 34f),
                                 new Vector2(0f, .5f), new Vector2(440f, -26f), 3f, 0f), 16);
            }

            public void Bind(int index)
            {
                var entries = _screen._board.Entries;
                _entry = index >= 0 && index < entries.Count ? entries[index] : default;

                _place.text = _entry.Rank.ToString();
                _name.text = _entry.Name;
                _worth.text = Loc.Format("ui.board.row_worth", Compact.Number(_entry.Score),
                                         _entry.KeeperLevel);

                CompanionArt.Paint(_portrait, AvatarCatalog.Resolve(_entry.AvatarId));

                // The player's own row is lit rather than merely present. A list somebody is
                // on and cannot find is a list that did not answer the question they opened it
                // with — and this is the one row on the screen they are looking for.
                bool mine = !string.IsNullOrEmpty(CloudState.UserId)
                         && _entry.OwnerId == CloudState.UserId;

                _plate.color = mine ? new Color(.16f, .12f, .05f, .84f)
                                    : new Color(.06f, .12f, .17f, .70f);
                _edge.color = mine ? Pal.A(Pal.Gold, .55f) : new Color(1f, 1f, 1f, .12f);
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
