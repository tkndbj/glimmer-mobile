using System;
using GlimmerGrove.Challenges;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Daily Challenges: the room behind the hub's banner, holding the slate of puzzle
    /// challenges — one card per row of <c>challenges.json</c>, today's marked.
    ///
    /// <para>
    /// <b>The whole slate is drawn, not only today's.</b> A daily challenge is one row a day
    /// (<see cref="ChallengeCalendar"/>), and that row wears the badge; the rest stand under
    /// it so every genre can be played and judged. Narrowing the list to the day's row is one
    /// line in <see cref="Build"/>, deliberately left for the day the slate is judged.
    /// </para>
    /// <para>
    /// <b>A card says the genre in one sentence</b> (<c>challenge.{id}.blurb</c>, derived from
    /// the id so a stranger to the file can name it), because a genre nobody has read the rule
    /// of is a card nobody taps.
    /// </para>
    /// <para>
    /// <b>A long list is a <c>GridView</c></b> (invariant 44ma): seven cards today, more the
    /// day the slate grows, and none of them is rebuilt when the page scrolls.
    /// </para>
    /// </summary>
    public sealed class DailyChallengesScreen : View
    {
        public override string Track => "mus_menu";

        const float ChromeSize = 92f;
        const float BannerH = 138f;
        const float RuleH = 96f;
        const float HeaderHeight = 22f + BannerH + 14f + RuleH + 8f;
        const float CardW = 960f, CardH = 236f, PlateW = 940f, PlateH = 208f;

        GridView _grid;
        Text _empty;

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

            int count = ChallengeRules.Table.Count;
            _grid.Show(count);
            _empty.enabled = count == 0;

            NavBar.Build(Content, NavBar.Tab.Home);
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
            UIKit.Shrinkable(
                UIKit.Titled("Rule", Safe, Loc.Get("ui.challenges.rule"), 26, new Color(1f, .96f, .88f, .82f),
                             TextAnchor.MiddleCenter, new Vector2(900f, RuleH), new Vector2(.5f, 1f),
                             new Vector2(0f, cy - BannerH * .5f - 14f - RuleH * .5f), 2f, 0f, wrap: true),
                18);
        }

        void Open(ChallengeDefinition def)
        {
            if (def == null) return;
            Flow.Go<ChallengeScreen>(v => v.Id = def.Id);
        }

        /// <summary>The hardware key goes back to the hub, which is the only way in.</summary>
        public override bool OnBack() { Flow.Go<HomeScreen>(); return true; }

        // ------------------------------------------------------------------ a card
        sealed class Card : IGridCell
        {
            readonly DailyChallengesScreen _screen;
            readonly Btn _button;
            readonly Text _name, _blurb, _today;
            readonly Image _gem, _todayPlate;

            ChallengeDefinition _def;

            public RectTransform Root { get; }

            public Card(DailyChallengesScreen screen, RectTransform parent)
            {
                _screen = screen;

                Root = UIKit.Node("Card", parent);
                Root.sizeDelta = new Vector2(CardW, CardH);

                _button = UIKit.Button("Hit", Root, Art.S("Ui/" + Skins.PlateBlue), new Vector2(PlateW, PlateH),
                                       new Vector2(.5f, .5f), Vector2.zero, () => _screen.Open(_def));
                _button.PressScale = .985f;

                var t = _button.transform;

                // The genre's mark: a gem, one of the four, chosen off the row's place in the slate.
                _gem = UIKit.Img("Gem", t, null, Color.white, Vector2.one * 118f, new Vector2(0f, .5f),
                                 new Vector2(96f, 4f));
                _gem.preserveAspect = true;
                _gem.raycastTarget = false;

                _name = UIKit.Titled("Name", t, string.Empty, 40, Pal.Cream, TextAnchor.MiddleLeft,
                                     new Vector2(600f, 56f), new Vector2(0f, .5f), new Vector2(178f + 300f, 40f), 3f, 3f);

                _blurb = UIKit.Shrinkable(
                    UIKit.Titled("Blurb", t, string.Empty, 24, new Color(1f, .96f, .88f, .82f), TextAnchor.UpperLeft,
                                 new Vector2(640f, 90f), new Vector2(0f, .5f), new Vector2(178f + 320f, -30f), 2f, 0f, wrap: true),
                    16);

                var tag = new Vector2(190f, 54f);
                _todayPlate = UIKit.Img("Today", t, Art.Round(24), Pal.A(Pal.Gold, .95f), tag, new Vector2(1f, 1f),
                                        UIKit.Corner(tag, new Vector2(1f, 1f), 18f, 14f));
                _todayPlate.raycastTarget = false;
                _today = UIKit.Titled("TodayText", _todayPlate.transform, Loc.Get("ui.challenges.today"), 22, Pal.Ink,
                                      TextAnchor.MiddleCenter, tag, default, default, 0f, 0f);
            }

            public void Bind(int index)
            {
                var table = ChallengeRules.Table;
                _def = index >= 0 && index < table.Count ? table.All[index] : null;
                if (_def == null) return;

                _name.text = Loc.Get(_def.NameKey);
                _blurb.text = Loc.Get(_def.BlurbKey);

                _gem.sprite = ChallengeArt.Gem(index % ChallengeColours.Count);
                _gem.enabled = _gem.sprite != null;

                var today = ChallengeCalendar.Today(table, ChallengeCalendar.DayOf(DateTime.UtcNow));
                bool mine = today != null && today.Id == _def.Id;
                _todayPlate.enabled = mine;
                _today.enabled = mine;
            }
        }
    }
}
