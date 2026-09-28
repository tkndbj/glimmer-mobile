using System;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The keeper ladder: where the player stands, what every level opens, and the one key
    /// that buys the next level outright (invariant 57).
    ///
    /// <para>
    /// <b>A climb, not a list.</b> The page is a night sky with a lit path winding up it: the
    /// top of the ladder at the top, level 1 at the foot, the level the player stands on
    /// crowned and throwing a beam at the next one. Every level is a disc on the path — the
    /// level-selection pack's three, green for reached, silver for above, crowned for here —
    /// and what a level opens stands beside it as a picture on a pedestal (a turret off the
    /// shelf, the Infinite lane) or hangs under it as a banner (an honorific). <b>The page says
    /// almost nothing in words</b>, by the owner's instruction: a number on every disc, a name
    /// under every prize, and one key. The rebuild of 2026-09-27 replaced a checklist of plates
    /// whose icons overlapped and whose three sentences of small print said what the picture
    /// already did.
    /// </para>
    /// <para>
    /// It is a <see cref="GridView"/> of one column (invariant 44ma), so seventy levels cost
    /// the cells that fit the glass and a repaint is a rebind. A cell draws <em>half</em> of the
    /// path to each neighbour, so the two halves meet on the cell boundary and no piece of a
    /// recycled cell ever reaches into another one's disc. <b>Where things stand is arithmetic
    /// on the level</b> (<see cref="SwingOf"/>, <see cref="ColumnsFor"/>), and
    /// <c>Tools/render_keeper_ladder.py</c> draws every cell of the whole ladder and refuses an
    /// overlap anywhere on it.
    /// </para>
    /// <para>
    /// <b>Every fact on the page is derived</b>: the standing level from
    /// <see cref="PlayerProgression.Level"/>, the count bought from <see cref="KeeperLedger"/>,
    /// the prices from the published ladder, the prizes from the catalog and the manifest.
    /// Buying is one tap on the docked key and the level moves at once:
    /// <see cref="KeeperLedger.TryBuy"/> debits, counts and invalidates, this page hears the
    /// change and rebinds, and a refusal from the server takes it back the same way. <b>A
    /// bought level is drawn as bought</b> — a mint run of path and the currency it was bought
    /// with on its disc — and the count sits on the medallion, which is the transparency the
    /// owner asked for said without a sentence.
    /// </para>
    /// </summary>
    public sealed class KeeperScreen : View
    {
        public override string Track => "mus_menu";

        /// <summary>The canvas is width-matched (<see cref="Boot.RefWidth"/>); everything here is measured across it.</summary>
        const float PageW = 1080f;

        // ------------------------------------------------------------------ the top bar
        const float ChromeSize = 92f, TopY = 22f, PillW = 212f, PillH = 78f;

        // ------------------------------------------------------------------ the hero
        const float HeroTop = 124f;
        const float Emblem = 260f, EmblemY = 138f;
        const float BannerW = 440f, BannerH = 118f, BannerY = 124f;
        const float BarW = 640f, BarH = 58f, BarGap = 16f, HeroFoot = 18f;

        /// <summary>Where the hero ends and the climb begins, below the safe layer's top.</summary>
        const float HeroBottom = HeroTop + EmblemY + BannerY + BannerH * .5f + BarGap + BarH + HeroFoot;

        /// <summary>
        /// The pack disc's white face: its centre stands this fraction of the disc's size above
        /// the sprite's middle, and it is this wide and tall. Measured off
        /// <c>keeper_node_open</c> (the three discs share one mould).
        /// </summary>
        const float FaceLift = .184f, FaceW = .453f, FaceH = .3125f;

        // ------------------------------------------------------------------ the dock
        const float DockH = 172f, KeyW = 760f, KeyH = 136f, TagW = 262f, TagH = 88f;

        // ------------------------------------------------------------------ the climb
        /// <summary>One level is one band of the sky.</summary>
        public const float CellH = 232f;
        const float PadTop = 70f, PadBottom = DockH + 36f;

        /// <summary>The path's serpentine: how far a disc swings off the middle, and how fast.</summary>
        public const float Swing = 118f, Turn = 1.05f;

        public const float NodeSize = 150f, CrownSize = 200f;
        const float TrackW = 16f, TrackEdge = 32f;

        /// <summary>
        /// A prize stands in a column this far off the middle — past the path's widest swing
        /// plus half a disc, so no prize can meet the path whatever the level.
        /// </summary>
        public const float ColumnX = 360f;
        const float PedestalW = 176f, PedestalH = 108f, PedestalY = -30f;
        const float Prize = 124f, PrizeY = 26f, CaptionY = -96f, CaptionRoom = 220f;
        const float RibbonW = 280f, RibbonH = 80f, RibbonY = -84f;

        static readonly Color SkyTop = new Color32(10, 18, 66, 255);
        static readonly Color SkyMiddle = new Color32(38, 26, 104, 255);
        static readonly Color SkyBottom = new Color32(86, 34, 118, 255);
        static readonly Color TrackDim = new Color32(70, 62, 140, 255);
        static readonly Color TrackShade = new Color32(16, 14, 52, 217);
        static readonly Color Asleep = new Color32(200, 206, 232, 255);
        static readonly Color Unlit = new Color32(150, 156, 196, 255);
        static readonly Color FaceWhite = new Color32(252, 252, 252, 255);
        static readonly Color DarkNumber = new Color(.20f, .16f, .06f);
        static readonly Color SilverNumber = new Color32(70, 76, 104, 255);

        static readonly Vector2 Top = new Vector2(.5f, 1f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 TopRight = new Vector2(1f, 1f);
        static readonly Vector2 Foot = new Vector2(.5f, 0f);
        static readonly Vector2 Left = new Vector2(0f, .5f);
        static readonly Vector2 Centre = new Vector2(.5f, .5f);

        // ------------------------------------------------------------------ state
        GridView _grid;
        RectTransform _viewport;
        AssetHold _shelfArt;

        // the hero, repainted in place
        Text _levelNumber, _honorific, _xpLine, _boughtText, _keyCaption, _keyPrice;
        Image _xpFill, _keyIcon, _keyTag, _keyGlow;
        RectTransform _emblem, _boughtChip;
        Btn _buy;
        int _drawnLevel = -1;

        /// <summary>The ladder rows read, once per repaint, so seventy binds share one walk.</summary>
        readonly Dictionary<int, RowFacts> _facts = new Dictionary<int, RowFacts>();
        int _standing = 1, _earned = 1, _top;
        KeeperOffer _offer;

        // ------------------------------------------------------------------ build
        protected override void Build()
        {
            BuildSky();
            BuildTopBar();
            BuildHero();

            // The climb. It runs down behind the dock to the nav bar, so the foot of the ladder
            // scrolls up out from under the key rather than being cut off above it.
            _viewport = UIKit.Node("Climb", Safe);
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);
            _viewport.offsetMin = new Vector2(0f, NavBar.Height);
            _viewport.offsetMax = new Vector2(0f, -HeroBottom);

            _grid = GridView.Attach(_viewport, 1, PageW, CellH, parent => new LevelCell(this, parent),
                                    padTop: PadTop, padBottom: PadBottom);

            BuildDock();
            NavBar.Build(Content, NavBar.Tab.Home, onSidePage: true);
            HoldShelfArt();

            Repaint(show: true);
        }

        /// <summary>
        /// The turret thumbnails live in the shop's shelf scope (invariant 7b); a pedestal drawn
        /// before they land stands empty, and rebinds when they do.
        /// </summary>
        void HoldShelfArt() => Run(async token =>
        {
            _shelfArt = _shelfArt ?? AssetLibrary.Hold("ward_shelf");
            await _shelfArt.LoadAsync(AssetManifest.WardShelfAssets(WardLedger.Catalog.Models), null, token);
            if (_grid) _grid.Refresh();
        });

        void OnEnable()
        {
            KeeperLedger.Changed += OnChanged;
            PlayerProgression.Changed += OnChanged;
            ProgressionRules.Changed += OnChanged;
        }

        void OnDisable()
        {
            KeeperLedger.Changed -= OnChanged;
            PlayerProgression.Changed -= OnChanged;
            ProgressionRules.Changed -= OnChanged;
        }

        void OnDestroy()
        {
            _shelfArt?.Dispose();
            _shelfArt = null;
        }

        public override void OnPresented()
        {
            // Open on the level the player stands on, centred: the page is about where you are
            // before it is about where you could be. On the build frame the viewport has no
            // height yet, so this waits for the present.
            if (_grid) _grid.ScrollTo(IndexOf(_standing), centre: true);
        }

        public override bool OnBack() { Flow.Go<HomeScreen>(); return true; }

        void OnChanged()
        {
            if (!_grid) return;
            Repaint(show: false);
        }

        /// <summary>The ladder is drawn top-down, so the highest level is row nought.</summary>
        int IndexOf(int level) => _top - level;

        // ------------------------------------------------------------------ the sky
        /// <summary>
        /// A night sky rather than the wall every other page stands on: a three-stop gradient,
        /// a fixed scatter of stars twinkling out of phase, and motes drifting up. All of it
        /// generated, so it costs no art and no address.
        /// </summary>
        void BuildSky()
        {
            var sky = UIKit.Img("Sky", Content, Art.Gradient(SkyBottom, SkyMiddle, SkyTop), Color.white);
            UIKit.StretchTo((RectTransform)sky.transform, 0, 0, 0, 0);

            // A fixed seed, so every open draws the same sky and nothing on it moves between two
            // visits but the twinkle.
            var rnd = new System.Random(57);
            for (int i = 0; i < 46; i++)
            {
                float u = .02f + (float)rnd.NextDouble() * .96f;
                float v = .15f + (float)rnd.NextDouble() * .84f;
                float size = 24f + (float)rnd.NextDouble() * 38f;
                float alpha = .35f + (float)rnd.NextDouble() * .5f;
                var star = UIKit.Img("Star", Content, Art.Glow(64, 2.6f), new Color(1f, .96f, .84f, alpha),
                                     Vector2.one * size, new Vector2(u, v), Vector2.zero);
                Tween.Breathe(star.transform, .35f, 1.8f + (float)rnd.NextDouble() * 2.4f, i * .9f);
            }

            Fireflies.Spawn(Content, 18, new Color(1f, .88f, .55f), 6f, 18f);
        }

        // ------------------------------------------------------------------ the top bar
        void BuildTopBar()
        {
            float cy = -(TopY + ChromeSize * .5f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * ChromeSize,
                             TopLeft, new Vector2(76f, cy), () => Flow.Go<HomeScreen>());

            // The page's name, in the bar rather than on a ribbon of its own: the hero is the
            // page's headline and a second one above it would be two.
            float left = 76f + ChromeSize * .5f + 22f;
            float right = PageW - 40f - PillW * 2f - 16f - 20f;
            var title = UIKit.Titled("Title", Safe, Loc.Get("ui.keeper.page_title").ToUpperInvariant(), 46, Pal.Gold,
                                     TextAnchor.MiddleLeft, new Vector2(right - left, ChromeSize), TopLeft,
                                     new Vector2((left + right) * .5f, cy), 3f, 3f);
            UIKit.OneLineLabel(title, right - left, 46, 26);

            Pill(ResourceSlots.Kind.Gems, -(40f + PillW * .5f), cy, Pal.Bloom, Art.S("Ui/ic_gem"),
                 Compact.Number(Profile.Gems), v => Compact.Number(v));
            Pill(ResourceSlots.Kind.Credits, -(40f + PillW * 1.5f + 16f), cy, Pal.Gold, null,
                 Compact.Number(Profile.Coins), v => Compact.Number(v));

            // The two this page spends (invariant 44j).
            WalletWatch.Attach(this, ResourceSlots.Kind.Credits, ResourceSlots.Kind.Gems);
        }

        /// <summary>One resource readout, registered with <see cref="ResourceSlots"/> as it is built.</summary>
        void Pill(ResourceSlots.Kind kind, float x, float y, Color tint, Sprite icon,
                  string value, Func<long, string> format)
        {
            var bg = UIKit.Img("Pill", Safe, Art.S("Ui/" + Skins.Trough), Color.white,
                               new Vector2(PillW, PillH), TopRight, new Vector2(x, y));

            var glow = UIKit.Img("Glow", bg.transform, Art.Glow(96, 2f), Pal.A(tint, .30f),
                                 new Vector2(96f, 96f), Left, new Vector2(46f, 0f));
            var ic = UIKit.Img("Icon", bg.transform, icon, Color.white,
                               new Vector2(52f, 52f), Left, new Vector2(46f, 0f));
            ic.preserveAspect = true;
            if (icon == null) Flipbook.Attach(ic, "Ui/Coin", 11f);
            Tween.Breathe(ic.transform, .05f, 2.4f, x * .01f);

            var text = UIKit.Titled("V", bg.transform, value, 30, Pal.Cream, TextAnchor.MiddleCenter,
                                    new Vector2(118f, 44f), Centre, new Vector2(28f, 0f), 3f, 3f);

            ResourceSlots.Register(kind, (RectTransform)ic.transform, text, glow, tint, format);
        }

        // ------------------------------------------------------------------ the hero
        /// <summary>
        /// Where the player stands: the crowned disc at the size of a medallion under a turning
        /// fan of light, the honorific on the banner hung from it, and the XP bar.
        ///
        /// <b>The XP bar is the earned level's</b> — a bought level adds a whole rung and moves
        /// no XP, so the bar keeps meaning "how far to the next level by play" whatever was
        /// bought. Saying otherwise would be a bar that jumps to empty when you spend.
        /// </summary>
        void BuildHero()
        {
            float ey = -(HeroTop + EmblemY);

            var fan = UIKit.Img("Fan", Safe, Art.Rays(512, 16), Pal.A(Pal.Sun, .30f),
                                new Vector2(760f, 760f), Top, new Vector2(0f, ey));
            Tween.RotateBy((RectTransform)fan.transform, 360f, 90f).Loop(-1, false);
            var halo = UIKit.Img("Halo", Safe, Art.Glow(128, 1.8f), Pal.A(Pal.Sun, .42f),
                                 new Vector2(560f, 560f), Top, new Vector2(0f, ey));
            Tween.Breathe(halo.transform, .06f, 3.2f);

            _emblem = UIKit.Box("Emblem", Safe, Vector2.one * Emblem, Top, new Vector2(0f, ey));
            var disc = UIKit.Img("Disc", _emblem, Art.S("Ui/keeper_node_crown"), Color.white,
                                 Vector2.one * Emblem, Centre, Vector2.zero);
            disc.preserveAspect = true;
            _levelNumber = UIKit.Titled("N", _emblem, "1", 96, DarkNumber, TextAnchor.MiddleCenter,
                                        new Vector2(Emblem * FaceW * .92f, 110f), Centre,
                                        new Vector2(0f, Emblem * FaceLift), 0f, 0f);
            _emblem.localScale = Vector3.zero;
            Tween.Pop(_emblem, 0f, .5f, .06f);

            // How many of the levels were bought, as a chip on the medallion's shoulder.
            _boughtChip = UIKit.Box("Bought", _emblem, new Vector2(196f, 54f), Centre,
                                    new Vector2(Emblem * .56f, Emblem * .30f));
            UIKit.Img("Plate", _boughtChip, Art.Round(27), Pal.Mint);
            _boughtText = UIKit.Titled("T", _boughtChip, string.Empty, 26, DarkNumber, TextAnchor.MiddleCenter,
                                       new Vector2(172f, 54f), Centre, Vector2.zero, 0f, 0f);

            float by = ey - BannerY;
            var banner = UIKit.Img("Banner", Safe, Art.S("Ui/" + Skins.Title), Color.white,
                                   new Vector2(BannerW, BannerH), Top, new Vector2(0f, by));
            _honorific = UIKit.Titled("Title", banner.transform, string.Empty, 40, Pal.Cream, TextAnchor.MiddleCenter,
                                      new Vector2(BannerW * .62f, BannerH * .5f), Centre, new Vector2(0f, 10f), 3f, 3f);

            float barY = by - BannerH * .5f - BarGap - BarH * .5f;
            var track = UIKit.Img("XpTrack", Safe, Art.S("Ui/" + Skins.Trough), Color.white,
                                  new Vector2(BarW, BarH), Top, new Vector2(0f, barY));
            _xpFill = UIKit.Img("XpFill", track.transform, Art.S("Ui/" + Skins.Fill), Pal.Mint,
                                new Vector2(0f, BarH - 12f), Left, new Vector2(6f, 0f));
            var fillRT = (RectTransform)_xpFill.transform;
            fillRT.pivot = new Vector2(0f, .5f);
            fillRT.sizeDelta = new Vector2(0f, BarH - 12f);
            _xpLine = UIKit.Titled("XpText", track.transform, string.Empty, 30, Pal.Cream, TextAnchor.MiddleCenter,
                                   new Vector2(BarW - 40f, BarH), Centre, Vector2.zero, 3f, 2f);
        }

        // ------------------------------------------------------------------ the dock
        /// <summary>
        /// The one key, docked over the foot of the climb where a thumb already is, so it is
        /// never scrolled away: the caption on the left, the price in a well on the right.
        /// </summary>
        void BuildDock()
        {
            var fade = UIKit.Img("Fade", Safe, Art.FadeUp(64), Pal.A(SkyBottom, .92f),
                                 new Vector2(PageW + 40f, DockH + 60f), Foot,
                                 new Vector2(0f, NavBar.Height + (DockH + 60f) * .5f));
            fade.raycastTarget = false;

            float cy = NavBar.Height + DockH * .5f;
            _keyGlow = UIKit.Img("KeyGlow", Safe, Art.Glow(128, 2f), Pal.A(Pal.Sun, .40f),
                                 new Vector2(KeyW * 1.2f, KeyH * 2.2f), Foot, new Vector2(0f, cy));
            Tween.Breathe(_keyGlow.transform, .05f, 2.0f);

            _buy = UIKit.Button("Buy", Safe, Art.S("Ui/" + Skins.Buy), new Vector2(KeyW, KeyH), Foot,
                                new Vector2(0f, cy), Buy);
            float lift = KeyH * UIKit.PillFaceLift;

            float captionRoom = KeyW - 44f - TagW - 30f;
            _keyCaption = UIKit.Titled("Caption", _buy.transform, string.Empty, 44, Pal.Cream, TextAnchor.MiddleCenter,
                                       new Vector2(captionRoom, KeyH * .72f), Centre,
                                       new Vector2(-KeyW * .5f + 22f + captionRoom * .5f, lift), 3f, 3f);

            _keyTag = UIKit.Img("Tag", _buy.transform, Art.S("Ui/" + Skins.Trough), Color.white,
                                new Vector2(TagW, TagH), Centre, new Vector2(KeyW * .5f - 22f - TagW * .5f, lift));
            _keyIcon = UIKit.Img("Icon", _keyTag.transform, null, Color.white, new Vector2(56f, 56f), Left,
                                 new Vector2(44f, 0f));
            _keyIcon.preserveAspect = true;
            _keyPrice = UIKit.Titled("Amount", _keyTag.transform, string.Empty, 40, Pal.Cream, TextAnchor.MiddleCenter,
                                     new Vector2(TagW - 96f, TagH), Centre, new Vector2(26f, 0f), 3f, 3f);

            _buy.transform.localScale = Vector3.zero;
            Tween.Pop(_buy.transform, 0f, .45f, .18f);
            _buy.Rehome();
        }

        // ---------------------------------------------------------------- repaint
        /// <summary>
        /// Reads every fact the page draws, once, and rebinds. <paramref name="show"/> spends
        /// the entrance; a repaint on a change does not (invariant 44ma).
        /// </summary>
        void Repaint(bool show)
        {
            var ladder = KeeperLedger.Ladder;
            _standing = PlayerProgression.Level.Level;
            _earned = PlayerProgression.EarnedLevel.Level;
            _offer = KeeperLedger.Next();

            // The page reaches the ladder's top, or the standing level if a player somehow stands
            // above it (a retune that lowered the top), so the crowned disc is always on the page.
            _top = Mathf.Max(ladder.Sells ? ladder.Top : _standing, _standing, 1);

            ReadFacts(ladder);
            PaintHero();

            if (show) _grid.Show(_top, animate: true);
            else _grid.Refresh();
        }

        void PaintHero()
        {
            var level = PlayerProgression.Level;
            var earned = PlayerProgression.EarnedLevel;

            if (_levelNumber)
            {
                _levelNumber.text = level.Level.ToString();
                UIKit.OneLineLabel(_levelNumber, Emblem * FaceW * .92f, 96, 48);
                if (_drawnLevel >= 0 && _drawnLevel != level.Level) Tween.Punch(_emblem, .16f, .4f);
                _drawnLevel = level.Level;
            }
            if (_honorific)
            {
                _honorific.text = Loc.Get(KeeperTitle.KeyFor(level.Level)).ToUpperInvariant();
                UIKit.OneLineLabel(_honorific, BannerW * .62f, 40, 22);
            }

            if (_xpFill)
            {
                float w = (BarW - 12f) * (earned.IsMaxLevel ? 1f : earned.Progress01);
                var rt = (RectTransform)_xpFill.transform;
                Tween.Run(.6f, Ease.OutCubic, t => { if (rt) rt.sizeDelta = new Vector2(Mathf.Lerp(rt.sizeDelta.x, w, t), BarH - 12f); }, _xpFill);
            }
            if (_xpLine)
            {
                _xpLine.text = (earned.IsMaxLevel
                    ? Loc.Get("ui.keeper.xp_max")
                    : Loc.Format("ui.keeper.xp_short", Compact.Number(earned.XpIntoLevel), Compact.Number(earned.XpForNextLevel)))
                    .ToUpperInvariant();
                UIKit.OneLineLabel(_xpLine, BarW - 40f, 30, 18);
            }

            int bought = KeeperLedger.Bought;
            if (_boughtChip)
            {
                _boughtChip.gameObject.SetActive(bought > 0);
                _boughtText.text = Loc.Format("ui.keeper.bought_count", bought).ToUpperInvariant();
                UIKit.OneLineLabel(_boughtText, 172f, 26, 16);
            }

            PaintKey();
        }

        void PaintKey()
        {
            if (!_buy) return;

            var img = _buy.GetComponent<Image>();
            if (_offer.Sold)
            {
                _buy.Interactable = true;
                if (img) img.sprite = Art.S("Ui/" + (_offer.Currency == Currency.Gems ? Skins.Gem : Skins.Buy));
                _keyCaption.text = Loc.Format("ui.keeper.buy", _offer.Level).ToUpperInvariant();
                _keyTag.gameObject.SetActive(true);
                _keyPrice.text = Compact.Number(_offer.Price);
                _keyIcon.sprite = _offer.Currency == Currency.Gems ? Art.S("Ui/ic_gem") : Art.CoinFace();
                _keyIcon.enabled = _keyIcon.sprite != null;
                _keyGlow.enabled = true;
                UIKit.OneLineLabel(_keyPrice, TagW - 96f, 40, 22);
                SeatCaption(KeyW - 44f - TagW - 30f, -KeyW * .5f + 22f + (KeyW - 44f - TagW - 30f) * .5f);
            }
            else
            {
                _buy.Interactable = false;
                if (img) img.sprite = Art.S("Ui/" + Skins.Shut);
                _keyCaption.text = Loc.Get(_offer.AtTop ? "ui.keeper.key_top" : "ui.keeper.key_not_sold").ToUpperInvariant();
                _keyTag.gameObject.SetActive(false);
                _keyGlow.enabled = false;
                SeatCaption(KeyW - 60f, 0f);
            }
        }

        /// <summary>The caption owns the whole key when there is no price, and the left of it when there is.</summary>
        void SeatCaption(float room, float x)
        {
            var rt = _keyCaption.rectTransform;
            rt.sizeDelta = new Vector2(room, rt.sizeDelta.y);
            rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
            UIKit.OneLineLabel(_keyCaption, room, 44, 24);
        }

        // ------------------------------------------------------------------ buying
        void Buy()
        {
            var offer = KeeperLedger.Next();

            switch (KeeperLedger.TryBuy())
            {
                case KeeperBuy.Bought:
                    Audio.Sfx("unlock", .9f);
                    Scenery.Toast(Content, Loc.Format("ui.keeper.bought_toast", offer.Level), Pal.Mint, 2.2f);
                    // The ledger's change has already repainted; bring the crowned disc into view.
                    if (_grid) _grid.ScrollTo(IndexOf(_standing), centre: true);
                    break;

                case KeeperBuy.TooPoor:
                    if (offer.Currency == Currency.Gems)
                    {
                        Scenery.Toast(Content, Loc.Format("ui.keeper.too_poor_gems", offer.Price), Pal.Bloom, 2.4f);
                        if (!Flow.HasModal) Flow.Modal<GemShopOverlay>();
                    }
                    else
                    {
                        Scenery.Toast(Content, Loc.Format("ui.keeper.too_poor_coins", offer.Price), Pal.Gold, 2.4f);
                    }
                    break;

                case KeeperBuy.AtTop:
                    Scenery.Toast(Content, Loc.Get("ui.keeper.at_top"), Pal.Gold);
                    break;

                default:
                    Scenery.Toast(Content, Loc.Get("ui.keeper.not_sold"), Pal.Gold);
                    break;
            }
        }

        // ------------------------------------------------------------------ the facts
        /// <summary>One thing a level opens, drawn on a pedestal: its picture and its name.</summary>
        readonly struct PrizeFact
        {
            public readonly string Sprite, Name;
            public PrizeFact(string sprite, string name) { Sprite = sprite; Name = name; }
        }

        /// <summary>What one level opens, and what it costs to buy. Read once per repaint.</summary>
        sealed class RowFacts
        {
            public int Level;
            public string Currency;
            public long Price;
            public bool Sold;
            public readonly List<PrizeFact> Prizes = new List<PrizeFact>(2);
            public string Title;                          // the honorific this level confers, or null
        }

        /// <summary>
        /// Walks the catalog, the manifest and the honorific table once, so a bind is a lookup.
        /// The turret gate is the roster's own (<see cref="WardModel.MinLevel"/>), the lane's
        /// wall the manifest's (<c>minKeeperLevel</c>), the honorific <see cref="KeeperTitle"/>'s
        /// - three sources, none of them this screen's.
        /// </summary>
        void ReadFacts(KeeperLadder ladder)
        {
            _facts.Clear();

            RowFacts At(int level)
            {
                if (!_facts.TryGetValue(level, out var facts))
                {
                    facts = new RowFacts { Level = level };
                    facts.Sold = ladder.PriceFor(level, out facts.Currency, out facts.Price);
                    _facts[level] = facts;
                }
                return facts;
            }

            for (int level = 1; level <= _top; level++) At(level);

            // Turrets, by the roster's gate. A starter has no gate and no pedestal.
            foreach (var model in WardLedger.Catalog.Models)
            {
                if (model == null || model.MinLevel <= 0 || model.MinLevel > _top) continue;
                At(model.MinLevel).Prizes.Add(new PrizeFact("Ui/" + model.Thumb, Loc.Get(model.NameKey)));
            }

            // Lanes and chapters walled behind a keeper level, by the manifest.
            var index = GameContent.Index;
            if (index != null)
            {
                var walled = new HashSet<int>();
                foreach (var chapter in index.Chapters)
                {
                    if (chapter == null || chapter.MinKeeperLevel <= 0 || chapter.MinKeeperLevel > _top) continue;
                    if (!walled.Add(chapter.MinKeeperLevel)) continue;
                    string mark = chapter.Track.IsMain ? "Ui/crest_gold" : "Ui/ic_endless";
                    At(chapter.MinKeeperLevel).Prizes.Add(new PrizeFact(mark, Loc.Get(chapter.NameKey)));
                }
            }

            // Honorifics, where the title changes.
            for (int level = 2; level <= _top; level++)
            {
                string key = KeeperTitle.KeyFor(level);
                if (key != KeeperTitle.KeyFor(level - 1)) At(level).Title = Loc.Get(key);
            }
        }

        RowFacts FactsFor(int level) => _facts.TryGetValue(level, out var f) ? f : null;

        /// <summary>Where a level's disc stands across the page: a serpentine, so the path winds.</summary>
        public static float SwingOf(int level) => Swing * Mathf.Sin(level * Turn);

        /// <summary>
        /// Where a level's prizes stand. One stands across the page from the way the disc leans;
        /// two stand either side of the path. A third is never drawn - no level opens more than
        /// two things that stand on a pedestal (the honorific hangs from the disc instead).
        /// </summary>
        public static int ColumnsFor(int level, int count, float[] into)
        {
            float lean = SwingOf(level) >= 0f ? 1f : -1f;
            if (count <= 0) return 0;
            into[0] = -lean * ColumnX;
            if (count == 1) return 1;
            into[1] = lean * ColumnX;
            return 2;
        }

        // ------------------------------------------------------------------ the climb
        /// <summary>
        /// One level of the climb: half the path to each neighbour, up to two prizes on
        /// pedestals, the disc, and a banner when the level confers a title. Every field is
        /// written on every bind (invariant 44mc) - a recycled cell that leaves a field alone
        /// shows the previous level's answer.
        /// </summary>
        sealed class LevelCell : IGridCell
        {
            readonly KeeperScreen _screen;
            readonly Image _beam, _fan, _glow;
            readonly Image _edgeUp, _edgeDown, _coreUp, _coreDown;
            readonly PrizeView[] _prizes = new PrizeView[2];
            readonly Image _disc, _face, _badge, _ribbon;
            readonly Text _number, _ribbonText;
            readonly RectTransform _discRT;
            readonly float[] _columns = new float[2];
            int _pulsing = -1;

            public RectTransform Root { get; }

            sealed class PrizeView
            {
                public RectTransform Root;
                public Image Glow, Pedestal, Picture;
                public Text Caption;
            }

            public LevelCell(KeeperScreen screen, RectTransform parent)
            {
                _screen = screen;

                Root = UIKit.Node("Level", parent);
                Root.sizeDelta = new Vector2(PageW, CellH);

                // The light first, so everything else in the cell stands in it.
                _beam = UIKit.Img("Beam", Root, Art.S("Ui/" + Skins.Beam), Pal.A(Pal.Sun, .55f),
                                  new Vector2(230f, 470f), Centre, Vector2.zero);
                ((RectTransform)_beam.transform).pivot = new Vector2(.5f, 0f);
                _fan = UIKit.Img("Fan", Root, Art.Rays(256, 12), Pal.A(Pal.Sun, .45f),
                                 new Vector2(420f, 420f), Centre, Vector2.zero);
                Tween.RotateBy((RectTransform)_fan.transform, 360f, 60f).Loop(-1, false);
                _glow = UIKit.Img("Glow", Root, Art.Glow(128, 1.9f), Pal.A(Pal.Sun, 0f),
                                  new Vector2(340f, 340f), Centre, Vector2.zero);

                // The path: two halves, the shade under the core, each pivoted at the disc.
                _edgeUp = Track("EdgeUp", TrackEdge);
                _edgeDown = Track("EdgeDown", TrackEdge);
                _coreUp = Track("CoreUp", TrackW);
                _coreDown = Track("CoreDown", TrackW);

                for (int i = 0; i < _prizes.Length; i++) _prizes[i] = BuildPrize(i);

                _discRT = UIKit.Box("Disc", Root, Vector2.one * NodeSize, Centre, Vector2.zero);
                _disc = UIKit.Img("Face", _discRT, null, Color.white);
                _disc.preserveAspect = true;
                _face = UIKit.Img("Blank", _discRT, Art.Disc(128), FaceWhite, Vector2.one, Centre, Vector2.zero);
                _number = UIKit.Titled("N", _discRT, string.Empty, 48, DarkNumber, TextAnchor.MiddleCenter,
                                       new Vector2(NodeSize, 80f), Centre, Vector2.zero, 0f, 0f);
                _badge = UIKit.Img("Paid", _discRT, null, Color.white, new Vector2(48f, 48f), Centre, Vector2.zero);
                _badge.preserveAspect = true;

                _ribbon = UIKit.Img("Ribbon", Root, Art.S("Ui/" + Skins.Title), Color.white,
                                    new Vector2(RibbonW, RibbonH), Centre, Vector2.zero);
                _ribbonText = UIKit.Titled("T", _ribbon.transform, string.Empty, 30, Pal.Cream, TextAnchor.MiddleCenter,
                                           new Vector2(RibbonW * .62f, RibbonH * .5f), Centre, new Vector2(0f, 7f), 2f, 2f);
            }

            Image Track(string name, float width)
            {
                var img = UIKit.Img(name, Root, Art.Pixel, Color.white, new Vector2(width, 10f), Centre, Vector2.zero);
                ((RectTransform)img.transform).pivot = new Vector2(.5f, 0f);
                return img;
            }

            PrizeView BuildPrize(int i)
            {
                var p = new PrizeView { Root = UIKit.Box("Prize" + i, Root, new Vector2(CaptionRoom + 20f, CellH), Centre, Vector2.zero) };
                p.Glow = UIKit.Img("Glow", p.Root, Art.Glow(128, 2f), Pal.A(Pal.Sun, .22f),
                                   new Vector2(260f, 260f), Centre, new Vector2(0f, PrizeY));
                p.Pedestal = UIKit.Img("Pedestal", p.Root, Art.S("Ui/" + Skins.Lander), Color.white,
                                       new Vector2(PedestalW, PedestalH), Centre, new Vector2(0f, PedestalY));
                p.Picture = UIKit.Img("Picture", p.Root, null, Color.white, Vector2.one * Prize, Centre, new Vector2(0f, PrizeY));
                p.Picture.preserveAspect = true;
                p.Caption = UIKit.Titled("Name", p.Root, string.Empty, 28, Pal.Cream, TextAnchor.MiddleCenter,
                                         new Vector2(CaptionRoom, 40f), Centre, new Vector2(0f, CaptionY), 2f, 2f);
                return p;
            }

            public void Bind(int index)
            {
                int level = _screen._top - index;
                var facts = _screen.FactsFor(level);
                int standing = _screen._standing, earned = _screen._earned, top = _screen._top;

                bool reached = level <= standing;
                bool crowned = level == standing;
                bool next = level == standing + 1;
                bool lit = reached || next;
                float x = SwingOf(level);

                // The crowned cell sinks under its neighbours, so its beam and its fan are light
                // falling behind the discs above rather than a sheet laid over them (44mc).
                if (crowned) Root.SetAsFirstSibling();

                // ---------------------------------------------------------------- light
                _beam.enabled = crowned;
                _fan.enabled = crowned;
                ((RectTransform)_beam.transform).anchoredPosition = new Vector2(x, 0f);
                ((RectTransform)_fan.transform).anchoredPosition = new Vector2(x, 0f);
                ((RectTransform)_glow.transform).anchoredPosition = new Vector2(x, 0f);
                _glow.color = Pal.A(Pal.Sun, crowned ? .55f : next ? .40f : 0f);

                // ---------------------------------------------------------------- the path
                // The run up to the standing level is lit: gold where it was earned, mint where
                // it was bought, so a bought level is drawn as bought.
                Half(_edgeUp, _coreUp, x, SwingOf(level + 1), CellH, level < top,
                     SegmentColour(level + 1, standing, earned));
                Half(_edgeDown, _coreDown, x, SwingOf(level - 1), -CellH, level > 1,
                     SegmentColour(level, standing, earned));

                // ---------------------------------------------------------------- prizes
                int count = facts == null ? 0 : Mathf.Min(facts.Prizes.Count, _prizes.Length);
                ColumnsFor(level, count, _columns);
                for (int i = 0; i < _prizes.Length; i++)
                {
                    var p = _prizes[i];
                    bool on = i < count;
                    p.Root.gameObject.SetActive(on);
                    if (!on) continue;

                    var prize = facts.Prizes[i];
                    p.Root.anchoredPosition = new Vector2(_columns[i], 0f);
                    p.Picture.sprite = Art.S(prize.Sprite);
                    p.Picture.enabled = p.Picture.sprite != null;
                    p.Picture.color = lit ? Color.white : Pal.A(Unlit, .9f);
                    p.Pedestal.color = lit ? Color.white : Unlit;
                    p.Glow.color = Pal.A(Pal.Sun, lit ? .22f : .08f);
                    p.Caption.text = prize.Name.ToUpperInvariant();
                    p.Caption.color = lit ? Pal.Cream : new Color32(190, 196, 226, 255);
                    UIKit.OneLineLabel(p.Caption, CaptionRoom, 28, 18);
                }

                // ---------------------------------------------------------------- the disc
                float size = crowned ? CrownSize : NodeSize;
                _discRT.sizeDelta = Vector2.one * size;
                _discRT.anchoredPosition = new Vector2(x, 0f);

                _disc.sprite = Art.S(crowned ? "Ui/keeper_node_crown" : reached ? "Ui/keeper_node_open" : "Ui/keeper_node_locked");
                _disc.enabled = _disc.sprite != null;
                _disc.color = reached || next ? Color.white : Asleep;

                // A locked disc carries a padlock on its face; the face is laid over it again so
                // the disc can carry its number instead. Silver already says locked.
                var faceRT = (RectTransform)_face.transform;
                faceRT.sizeDelta = new Vector2(size * FaceW, size * FaceH);
                faceRT.anchoredPosition = new Vector2(0f, size * FaceLift);
                _face.enabled = !reached && _disc.enabled;
                _face.color = next ? FaceWhite : FaceWhite * Asleep;

                var numberRT = _number.rectTransform;
                numberRT.anchoredPosition = new Vector2(0f, size * FaceLift);
                _number.text = level.ToString();
                _number.color = reached ? DarkNumber : SilverNumber;
                UIKit.OneLineLabel(_number, size * FaceW * .9f, crowned ? 64 : 48, 28);

                // What a bought level was bought with, on its shoulder.
                bool paid = reached && !crowned && level > earned;
                _badge.enabled = paid;
                if (paid)
                {
                    _badge.sprite = facts != null && facts.Currency == Currency.Gems ? Art.S("Ui/ic_gem") : Art.CoinFace();
                    _badge.enabled = _badge.sprite != null;
                    ((RectTransform)_badge.transform).anchoredPosition = new Vector2(size * .36f, size * .30f);
                }

                // ---------------------------------------------------------------- the banner
                bool titled = facts != null && !string.IsNullOrEmpty(facts.Title);
                _ribbon.gameObject.SetActive(titled);
                if (titled)
                {
                    ((RectTransform)_ribbon.transform).anchoredPosition = new Vector2(x, RibbonY);
                    _ribbon.color = reached ? Color.white : new Color32(170, 176, 210, 255);
                    _ribbonText.text = facts.Title.ToUpperInvariant();
                    UIKit.OneLineLabel(_ribbonText, RibbonW * .62f, 30, 18);
                }

                // The breath on the next disc, keyed on the level it was started for (44mc).
                if (next && _pulsing != level)
                {
                    _pulsing = level;
                    Tween.Breathe(_glow.transform, .10f, 1.8f, level * .13f);
                }
                else if (!next && _pulsing >= 0)
                {
                    _pulsing = -1;
                    Tween.KillChannel(_glow.transform, "breathe");
                    _glow.transform.localScale = Vector3.one;
                }
            }

            /// <summary>Gold for a level earned, mint for one bought, nothing for one above.</summary>
            static Color? SegmentColour(int upper, int standing, int earned)
            {
                if (upper > standing) return null;
                return upper > earned ? Pal.Mint : Pal.Gold;
            }

            /// <summary>
            /// Half the path to a neighbour, which draws the other half: from this disc to the
            /// midpoint, which always falls on the cell's own edge.
            /// </summary>
            static void Half(Image edge, Image core, float x, float toX, float toY, bool exists, Color? lit)
            {
                edge.enabled = exists;
                core.enabled = exists;
                if (!exists) return;

                float dx = (toX - x) * .5f, dy = toY * .5f;
                float length = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(-dx, dy) * Mathf.Rad2Deg;

                foreach (var bar in new[] { edge, core })
                {
                    var rt = (RectTransform)bar.transform;
                    rt.anchoredPosition = new Vector2(x, 0f);
                    rt.sizeDelta = new Vector2(rt.sizeDelta.x, length);
                    rt.localRotation = Quaternion.Euler(0f, 0f, angle);
                }

                edge.color = TrackShade;
                core.color = lit ?? TrackDim;
            }
        }
    }
}
