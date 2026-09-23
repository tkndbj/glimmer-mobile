using GlimmerGrove.Challenges;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Daily Challenges: the room behind the hub's banner, holding one card per genre with
    /// today's level, the plays left and a badge saying so, under a band that says what the
    /// day allows and sells more.
    ///
    /// <para>
    /// <b>A card is a genre, not a row</b> (invariant 56f). A genre deals one level a play,
    /// rotating through its rows day by day, so the thing a player chooses is the kind of
    /// puzzle and the level is the calendar's answer — the card names today's level under the
    /// genre's own name so the two are not confused, and every player on the same day sees
    /// the same name. Adding a level to a genre changes nothing here; adding a genre is a
    /// build (56a) and adds a card.
    /// </para>
    /// <para>
    /// <b>Everything on a card is painted, never drawn</b> (44j's rule about readouts): the
    /// plays left, the badge and the deal band all move without anybody tapping — a deal
    /// bought on the sheet, a day turning while the page stands, a sync arriving with plays
    /// spent on another phone — so the page listens to <see cref="ChallengeLedger.Changed"/>
    /// and repaints, cells and band alike, rather than being rebuilt.
    /// </para>
    /// <para>
    /// <b>A long list is a <c>GridView</c></b> (invariant 44ma): four cards today, more the
    /// day a genre is added, and none of them is rebuilt when the page scrolls.
    /// </para>
    /// </summary>
    public sealed class DailyChallengesScreen : View
    {
        public override string Track => "mus_menu";

        const float ChromeSize = 92f;
        const float BannerH = 138f;
        const float RuleH = 76f;
        const float DealH = 112f, DealW = 960f, DealKeyW = 220f, DealKeyH = 84f;
        const float HeaderHeight = 22f + BannerH + 10f + RuleH + 10f + DealH + 10f;
        const float CardW = 960f, CardH = 250f, PlateW = 940f, PlateH = 222f;

        /// <summary>
        /// The genre's picture: a square on the plate's left, and where the text starts after it.
        /// The owner's marks carry their own frame, so the card draws nothing round them.
        /// </summary>
        const float MarkSize = 176f, MarkX = 118f, TextX = 226f;

        GridView _grid;
        Text _empty, _dealLine;
        Btn _dealKey;

        /// <summary>How many genres the grid was last shown with, so a repaint can tell a resize from a refresh.</summary>
        int _shown;

        protected override void Build()
        {
            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 22, new Color(1f, .93f, .70f), 6f, 22f);

            BuildHeader();

            var viewport = UIKit.Node("Viewport", Safe);
            viewport.offsetMin = new Vector2(0f, NavBar.Height + 12f);
            viewport.offsetMax = new Vector2(0f, -HeaderHeight);

            _grid = GridView.Attach(viewport, 1, CardW, CardH, parent => new Card(this, parent));

            _empty = UIKit.Shrinkable(
                UIKit.Titled("Empty", Safe, Loc.Get("ui.challenges.empty"), 28, new Color(1f, .96f, .88f, .72f),
                             TextAnchor.UpperCenter, new Vector2(760f, 200f), new Vector2(.5f, 1f),
                             new Vector2(0f, -(HeaderHeight + 60f)), 3f, 0f, wrap: true), 18);

            _shown = ChallengeRules.Table.Genres.Count;
            _grid.Show(_shown);
            _empty.enabled = _shown == 0;

            NavBar.Build(Content, NavBar.Tab.Home);

            ChallengeLedger.Changed += OnLedgerChanged;
            ChallengeRules.Changed += OnLedgerChanged;

            PaintDeal();
        }

        void OnDestroy()
        {
            ChallengeLedger.Changed -= OnLedgerChanged;
            ChallengeRules.Changed -= OnLedgerChanged;
        }

        void OnLedgerChanged()
        {
            // Guarded because the event can arrive from a save load during teardown, and
            // painting onto a destroyed screen throws where nobody is looking.
            if (this == null || !_grid) return;

            int count = ChallengeRules.Table.Genres.Count;
            if (count != _shown)
            {
                _shown = count;
                _grid.Show(count, animate: false);
            }
            else _grid.Refresh();

            if (_empty) _empty.enabled = count == 0;
            PaintDeal();
        }

        void BuildHeader()
        {
            float cy = -(22f + BannerH * .5f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * ChromeSize,
                             new Vector2(0f, 1f), new Vector2(76f, cy),
                             () => Flow.Go<HomeScreen>());

            var ribbon = Scenery.TitleRibbon(Safe, Loc.Get("ui.challenges.title").ToUpperInvariant(),
                                             new Vector2(720f, BannerH), new Vector2(.5f, 1f),
                                             new Vector2(0f, cy), 42);
            ribbon.transform.localScale = Vector3.zero;
            Tween.Pop(ribbon.transform, 0f, .5f, .06f);

            // The one rule every card shares, said once above them rather than on each.
            float ruleY = cy - BannerH * .5f - 10f - RuleH * .5f;
            UIKit.Shrinkable(
                UIKit.Titled("Rule", Safe, Loc.Get("ui.challenges.rule"), 24, new Color(1f, .96f, .88f, .82f),
                             TextAnchor.MiddleCenter, new Vector2(900f, RuleH), new Vector2(.5f, 1f),
                             new Vector2(0f, ruleY), 2f, 0f, wrap: true),
                16);

            // The deal band: what the day allows, and the key to the sheet that sells more. A
            // plate rather than a bare line, because it is the one thing on the page that is
            // both a readout and a door.
            float dealY = ruleY - RuleH * .5f - 10f - DealH * .5f;
            var band = UIKit.Img("Deal", Safe, Art.S("Ui/" + Skins.PlateNavy), Color.white,
                                 new Vector2(DealW, DealH), new Vector2(.5f, 1f), new Vector2(0f, dealY));
            band.type = Image.Type.Sliced;
            band.raycastTarget = false;

            _dealLine = UIKit.Shrinkable(
                UIKit.Titled("Line", band.transform, string.Empty, 26, Pal.Cream, TextAnchor.MiddleLeft,
                             new Vector2(DealW - DealKeyW - 90f, DealH - 20f), new Vector2(0f, .5f),
                             new Vector2(36f + (DealW - DealKeyW - 90f) * .5f, 0f), 2f, 2f, wrap: true),
                16);

            _dealKey = UIKit.TextButton("Deals", band.transform, Skins.Buy, Loc.Get("ui.challenges.deals").ToUpperInvariant(),
                                        26, new Vector2(DealKeyW, DealKeyH), new Vector2(1f, .5f),
                                        new Vector2(-(24f + DealKeyW * .5f), 0f),
                                        () => { if (!Flow.HasModal) Flow.Modal<ChallengeTierOverlay>(); });
        }

        /// <summary>
        /// Writes the allowance onto the band: the running deal and its days, or the free figure.
        /// The key hides when the file sells nothing, because a door onto an empty sheet is a tap
        /// a player learns not to make.
        /// </summary>
        void PaintDeal()
        {
            if (!_dealLine) return;

            var held = ChallengeLedger.HeldTier;
            if (held != null)
            {
                int days = ChallengeLedger.DaysLeft(held);
                string left = days == 1 ? Loc.Get("ui.challenges.days_left_one") : Loc.Format("ui.challenges.days_left", days);
                _dealLine.text = Loc.Format("ui.challenges.held_deal", Loc.Get(held.NameKey), held.Plays, left);
            }
            else
            {
                _dealLine.text = Loc.Format("ui.challenges.free_deal", ChallengeRules.Table.FreePlays);
            }

            if (_dealKey) _dealKey.gameObject.SetActive(ChallengeRules.Table.Tiers.Count > 0);
        }

        void Open(ChallengeGenre genre)
        {
            if (!ChallengeLedger.CanPlay(genre))
            {
                Scenery.Toast(Content, Loc.Get("ui.challenges.no_plays"), Pal.Gold, 2.4f);
                return;
            }

            Flow.Go<ChallengeScreen>(v => v.Genre = genre);
        }

        /// <summary>The hardware key goes back to the hub, which is the only way in.</summary>
        public override bool OnBack() { Flow.Go<HomeScreen>(); return true; }

        // ------------------------------------------------------------------ a card
        /// <summary>
        /// One genre. Every field is written on every bind (44mc): a recycled cell that leaves a
        /// field alone is the previous genre's answer showing through.
        /// </summary>
        sealed class Card : IGridCell
        {
            readonly DailyChallengesScreen _screen;
            readonly Btn _button;
            readonly Text _name, _blurb, _level, _plays;
            readonly Image _mark, _playsPlate;
            readonly WaitingBadge _badge;

            ChallengeGenre _genre;

            public RectTransform Root { get; }

            public Card(DailyChallengesScreen screen, RectTransform parent)
            {
                _screen = screen;

                Root = UIKit.Node("Card", parent);
                Root.sizeDelta = new Vector2(CardW, CardH);

                _button = UIKit.Button("Hit", Root, Art.S("Ui/" + Skins.PlateBlue), new Vector2(PlateW, PlateH),
                                       new Vector2(.5f, .5f), Vector2.zero, () => _screen.Open(_genre));
                _button.PressScale = .985f;

                var t = _button.transform;

                // The genre's picture (`ChallengeArt.GenreMark`), owner-drawn and framed in its own
                // colour. Written on every bind rather than here, because the cell is recycled.
                _mark = UIKit.Img("Mark", t, null, Color.white, Vector2.one * MarkSize, new Vector2(0f, .5f),
                                  new Vector2(MarkX, 0f));
                _mark.preserveAspect = true;
                _mark.raycastTarget = false;

                _name = UIKit.Titled("Name", t, string.Empty, 40, Pal.Cream, TextAnchor.MiddleLeft,
                                     new Vector2(480f, 56f), new Vector2(0f, .5f), new Vector2(TextX + 240f, 58f), 3f, 3f);

                _blurb = UIKit.Shrinkable(
                    UIKit.Titled("Blurb", t, string.Empty, 22, new Color(1f, .96f, .88f, .82f), TextAnchor.UpperLeft,
                                 new Vector2(620f, 66f), new Vector2(0f, .5f), new Vector2(TextX + 310f, -4f), 2f, 0f, wrap: true),
                    15);

                // Today's level, named so a player who talks to a friend can say which one it was.
                _level = UIKit.Shrinkable(
                    UIKit.Titled("Level", t, string.Empty, 22, Pal.A(Pal.Gold, .95f), TextAnchor.MiddleLeft,
                                 new Vector2(400f, 40f), new Vector2(0f, .5f), new Vector2(TextX + 200f, -66f), 2f, 0f),
                    15);

                // The plays left, on the right, as a pill. Colour says the state as well as the
                // words do, but the words are what carry it (a pill that only changed colour
                // would be the tile-board lesson of 48g).
                var pillSize = new Vector2(250f, 54f);
                _playsPlate = UIKit.Img("Plays", t, Art.Round(24), Pal.A(Pal.Gold, .95f), pillSize, new Vector2(1f, .5f),
                                        new Vector2(-(26f + pillSize.x * .5f), -66f));
                _playsPlate.raycastTarget = false;
                _plays = UIKit.Shrinkable(
                    UIKit.Titled("PlaysText", _playsPlate.transform, string.Empty, 22, Pal.Ink,
                                 TextAnchor.MiddleCenter, pillSize, default, default, 0f, 0f), 14);

                // Built last, so it sits over the plate's own tap area (see WaitingBadge).
                _badge = WaitingBadge.Disc(t);
            }

            public void Bind(int index)
            {
                var table = ChallengeRules.Table;
                if (index < 0 || index >= table.Genres.Count) return;
                _genre = table.Genres[index];

                string spelling = ChallengeGenres.NameOf(_genre);
                _name.text = Loc.Get("challenge.genre." + spelling + ".name");
                _blurb.text = Loc.Get("challenge.genre." + spelling + ".blurb");

                _mark.sprite = ChallengeArt.GenreMark(_genre);
                _mark.enabled = _mark.sprite != null;

                var level = ChallengeLedger.Current(_genre);
                _level.text = level == null ? string.Empty
                            : Loc.Format("ui.challenges.today_level", Loc.Get(level.NameKey));

                int left = ChallengeLedger.PlaysLeft(_genre);
                int allowance = ChallengeLedger.Allowance;
                bool open = left > 0 && level != null;

                _plays.text = open ? Loc.Format("ui.challenges.plays_left", left, allowance)
                                   : Loc.Get("ui.challenges.spent");
                _playsPlate.color = open ? Pal.A(Pal.Gold, .95f) : new Color(.62f, .66f, .74f, .95f);

                _button.Interactable = true;
                _badge.Paint(open ? left : 0);
            }
        }
    }
}
