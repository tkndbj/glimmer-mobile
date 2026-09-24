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

        /// <summary>
        /// The deal band: a plate as wide as the cards under it, the crowned chest on its left
        /// (<see cref="ChallengeArt.Chest"/>), the allowance in the middle and the key to the
        /// sheet on its right. Re-cut on 2026-09-23 at the owner's instruction — the chest, the
        /// larger type and the width all landed together, and the rule line that used to stand
        /// above it is gone: the board teaches that rule on the first move.
        /// </summary>
        const float DealH = 200f, DealW = 1024f, DealKeyW = 200f, DealKeyH = 108f;
        const float ChestSize = 170f, ChestX = 104f, DealTextX = 196f;
        const float HeaderHeight = 22f + BannerH + 12f + DealH + 10f;

        /// <summary>
        /// A card: 28 units off each side of a 1080 canvas rather than 70, so the plate is the
        /// page's width and the picture and the words on it have room to be a size up.
        /// </summary>
        const float CardW = 1040f, CardH = 292f, PlateW = 1024f, PlateH = 264f;

        /// <summary>
        /// The genre's picture: a square on the plate's left, and where the text starts after it.
        /// The owner's marks carry their own frame, so the card draws nothing round them.
        /// </summary>
        const float MarkSize = 216f, MarkX = 132f, TextX = 268f;

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

            // The deal band: what the day allows, and the key to the sheet that sells more. A
            // plate rather than a bare line, because it is the one thing on the page that is
            // both a readout and a door.
            float dealY = cy - BannerH * .5f - 12f - DealH * .5f;
            // Orange, at the owner's instruction: the one warm plate on a page of blue cards,
            // which is what makes the door read as a door rather than as a fifth card.
            var band = UIKit.Img("Deal", Safe, Art.S("Ui/" + Skins.PlateOrange), Color.white,
                                 new Vector2(DealW, DealH), new Vector2(.5f, 1f), new Vector2(0f, dealY));
            band.type = Image.Type.Sliced;
            band.raycastTarget = false;

            // The crowned chest, on the band's left. Null until its address is synced, and
            // drawn as nothing rather than as a white rectangle until then (7b).
            var chest = UIKit.Img("Chest", band.transform, ChallengeArt.Chest(), Color.white, Vector2.one * ChestSize,
                                  new Vector2(0f, .5f), new Vector2(ChestX, 0f));
            chest.preserveAspect = true;
            chest.raycastTarget = false;
            chest.enabled = chest.sprite != null;

            float lineW = DealW - DealTextX - 20f - DealKeyW - 24f;
            _dealLine = UIKit.Shrinkable(
                UIKit.Titled("Line", band.transform, string.Empty, 32, Pal.Cream, TextAnchor.MiddleLeft,
                             new Vector2(lineW, DealH - 24f), new Vector2(0f, .5f),
                             new Vector2(DealTextX + lineW * .5f, 0f), 2f, 2f, wrap: true),
                18);

            // Green (`Skins.Affirm`, the kit's own green pill — a tint cannot reach the season
            // screen's mint on an orange sprite, 44g), because on an orange plate the orange
            // Buy key vanished into its own ground.
            _dealKey = UIKit.TextButton("Deals", band.transform, Skins.Affirm, Loc.Get("ui.challenges.deals").ToUpperInvariant(),
                                        32, new Vector2(DealKeyW, DealKeyH), new Vector2(1f, .5f),
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
                _dealLine.text = Loc.Format("ui.challenges.held_deal", Loc.Get(held.NameKey), held.Plays, TimeLeft(held));
            else
            {
                _dealLine.text = Loc.Format("ui.challenges.free_deal", ChallengeRules.Table.FreePlays);
            }

            if (_dealKey) _dealKey.gameObject.SetActive(ChallengeRules.Table.Tiers.Count > 0);
        }

        /// <summary>
        /// What is left of a running deal, in the unit that fits: whole days rounded up while a
        /// day or more remains, hours on the last day. A window is exactly its days of the clock
        /// (56h), so "1 day left" means up to a day, never a calendar day that ends at midnight.
        /// </summary>
        public static string TimeLeft(ChallengeTier tier)
        {
            long seconds = ChallengeLedger.SecondsLeft(tier);
            if (seconds > Daily.DailyRules.SecondsPerDay)
                return Loc.Format("ui.challenges.days_left", ChallengeLedger.DaysLeft(tier));

            long hours = (seconds + 3599L) / 3600L;
            return hours <= 0L ? Loc.Get("ui.challenges.days_left_one")
                 : hours >= 24L ? Loc.Get("ui.challenges.days_left_one")
                 : Loc.Format("ui.challenges.hours_left", hours);
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

                _name = UIKit.Titled("Name", t, string.Empty, 48, Pal.Cream, TextAnchor.MiddleLeft,
                                     new Vector2(560f, 64f), new Vector2(0f, .5f), new Vector2(TextX + 280f, 74f), 3f, 3f);

                _blurb = UIKit.Shrinkable(
                    UIKit.Titled("Blurb", t, string.Empty, 27, new Color(1f, .96f, .88f, .82f), TextAnchor.UpperLeft,
                                 new Vector2(700f, 80f), new Vector2(0f, .5f), new Vector2(TextX + 350f, -8f), 2f, 0f, wrap: true),
                    17);

                // Today's level, named so a player who talks to a friend can say which one it was.
                _level = UIKit.Shrinkable(
                    UIKit.Titled("Level", t, string.Empty, 26, Pal.A(Pal.Gold, .95f), TextAnchor.MiddleLeft,
                                 new Vector2(420f, 46f), new Vector2(0f, .5f), new Vector2(TextX + 210f, -82f), 2f, 0f),
                    16);

                // The plays left, on the right, as a pill. Colour says the state as well as the
                // words do, but the words are what carry it (a pill that only changed colour
                // would be the tile-board lesson of 48g).
                var pillSize = new Vector2(290f, 66f);
                _playsPlate = UIKit.Img("Plays", t, Art.Round(24), Pal.A(Pal.Gold, .95f), pillSize, new Vector2(1f, .5f),
                                        new Vector2(-(26f + pillSize.x * .5f), -82f));
                _playsPlate.raycastTarget = false;
                _plays = UIKit.Shrinkable(
                    UIKit.Titled("PlaysText", _playsPlate.transform, string.Empty, 26, Pal.Ink,
                                 TextAnchor.MiddleCenter, pillSize, default, default, 0f, 0f), 16);

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
