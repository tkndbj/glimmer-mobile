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
    /// <b>One tall page you scroll down, from level 1 to the top of the ladder.</b> It is a
    /// <see cref="GridView"/> of one column (invariant 44ma), so seventy levels cost the cells
    /// that fit the glass and a repaint is a rebind rather than a rebuild. Each row is a node on
    /// a spine — the level-selection pack's discs, lit for a level reached, locked for one
    /// above, crowned for the one the player stands on — beside a plate that says what the
    /// level opens: a turret off the shelf (the roster's own gate, so the sentence cannot drift
    /// from the price), the Infinite lane's wall, an honorific. <b>Every fact on the page is
    /// derived</b>: the standing level from <see cref="PlayerProgression.Level"/>, the count
    /// bought from <see cref="KeeperLedger"/>, the prices from the published ladder, the unlocks
    /// from the catalog and the manifest. Nothing here has its own table to go stale.
    /// </para>
    /// <para>
    /// <b>Buying is one tap and the level moves at once</b>: <see cref="KeeperLedger.TryBuy"/>
    /// debits, counts and invalidates, and this page hears the change and rebinds. A refusal
    /// from the server arrives through the same event and takes the row back the same way. The
    /// key is drawn on the <em>next</em> row and in the hero, because a player scrolled to the
    /// top of the ladder should not have to hunt for it.
    /// </para>
    /// <para>
    /// <b>Transparent by the owner's instruction</b>: a bought level is drawn as bought, the
    /// count is printed under the hero, and the one thing a bought level never moves — the
    /// rank — is said on the page rather than discovered on the boards.
    /// </para>
    /// </summary>
    public sealed class KeeperScreen : View
    {
        public override string Track => "mus_menu";

        const float ChromeSize = 92f;
        const float BannerH = 138f;
        const float HeroH = 300f;
        const float NoteH = 34f;
        const float HeadingH = 62f;
        const float Width = 1000f;

        /// <summary>One level is one row: the plate, and the gap under it.</summary>
        public const float RowH = 156f, RowGap = 14f, CellH = RowH + RowGap;

        /// <summary>The spine the nodes stand on, and the disc that stands on it.</summary>
        public const float SpineX = -410f, SpineW = 14f, NodeSize = 128f;

        /// <summary>The plate beside the spine.</summary>
        public const float PlateLeft = -330f, PlateRight = 500f;
        public const float PlateW = PlateRight - PlateLeft, PlateX = (PlateLeft + PlateRight) * .5f;

        /// <summary>The buy key at the plate's right end, and the chip that stands there otherwise.</summary>
        public const float KeyW = 250f, KeyH = 84f, ChipW = 190f, ChipH = 46f;

        static readonly Vector2 Top = new Vector2(.5f, 1f);
        static readonly Vector2 Left = new Vector2(0f, .5f);
        static readonly Vector2 Right = new Vector2(1f, .5f);
        static readonly Vector2 Centre = new Vector2(.5f, .5f);

        // ------------------------------------------------------------------ state
        GridView _grid;
        RectTransform _viewport;
        AssetHold _shelfArt;

        // the hero, repainted in place
        Text _levelNumber, _honorific, _xpLine, _boughtLine, _keyLabel, _keyPrice;
        Image _xpFill, _keyIcon;
        RectTransform _medallion;
        Btn _buy;
        int _drawnLevel = -1;

        /// <summary>The ladder rows read, once per repaint, so seventy binds share one walk.</summary>
        readonly Dictionary<int, RowFacts> _facts = new Dictionary<int, RowFacts>();
        int _standing = 1, _earned = 1, _top;
        KeeperOffer _offer;

        // ------------------------------------------------------------------ build
        protected override void Build()
        {
            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 22, new Color(1f, .93f, .70f), 6f, 22f);

            float y = 22f;
            y = BuildHeader(y);
            y = BuildHero(y);
            y = BuildNote(y);
            y = BuildHeadings(y);

            // The board. The viewport is anchored to the safe layer's foot and the nav bar's top,
            // so a tall phone gets more rows rather than more air.
            _viewport = UIKit.Node("Ladder", Safe);
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);
            _viewport.offsetMin = new Vector2(0f, NavBar.Height + 16f);
            _viewport.offsetMax = new Vector2(0f, -y);

            _grid = GridView.Attach(_viewport, 1, Width, CellH, parent => new LevelCell(this, parent),
                                    padTop: 6f, padBottom: 30f);

            NavBar.Build(Content, NavBar.Tab.Home, onSidePage: true);
            HoldShelfArt();

            Repaint(show: true);
        }

        /// <summary>
        /// The turret thumbnails live in the shop's shelf scope (invariant 7b); a row drawn
        /// before they land shows the plate with no picture, and rebinds when they do.
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
            if (_grid) _grid.ScrollTo(_standing - 1, centre: true);
        }

        public override bool OnBack() { Flow.Go<HomeScreen>(); return true; }

        void OnChanged()
        {
            if (!_grid) return;
            Repaint(show: false);
        }

        // ------------------------------------------------------------------ chrome
        float BuildHeader(float y)
        {
            float cy = -(y + BannerH * .5f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * ChromeSize,
                             new Vector2(0f, 1f), new Vector2(76f, cy), () => Flow.Go<HomeScreen>());

            var ribbon = Scenery.TitleRibbon(Safe, Loc.Get("ui.keeper.page_title").ToUpperInvariant(),
                                             new Vector2(720f, BannerH), Top, new Vector2(0f, cy), 42);
            ribbon.transform.localScale = Vector3.zero;
            Tween.Pop(ribbon.transform, 0f, .5f, .06f);
            y += BannerH + 4f;

            UIKit.Shrinkable(
                UIKit.Titled("Sub", Safe, Loc.Get("ui.keeper.subtitle"), 24,
                             new Color(.86f, .90f, 1f, .78f), TextAnchor.MiddleCenter,
                             new Vector2(880f, 32f), Top, new Vector2(0f, -(y + 16f)), 3f, 3f), 15);
            y += 32f + 14f;

            float py = -(y + ChromeSize * .5f);
            Pill(ResourceSlots.Kind.Hearts, -232f, py, Pal.Rose, Art.S("Ui/ic_heart"),
                 Profile.HeartsLabel(), v => Profile.HeartsLabel((int)v));
            Pill(ResourceSlots.Kind.Credits, 0f, py, Pal.Gold, null,
                 Compact.Number(Profile.Coins), v => Compact.Number(v));
            Pill(ResourceSlots.Kind.Gems, 232f, py, Pal.Bloom, Art.S("Ui/ic_gem"),
                 Compact.Number(Profile.Gems), v => Compact.Number(v));

            // All three, because two of them are what this page spends and the third moves on a
            // timer while somebody reads the ladder (invariant 44j).
            WalletWatch.Attach(this, ResourceSlots.Kind.Hearts, ResourceSlots.Kind.Credits,
                               ResourceSlots.Kind.Gems);

            return y + ChromeSize + 16f;
        }

        /// <summary>One resource readout, registered with <see cref="ResourceSlots"/> as it is built.</summary>
        void Pill(ResourceSlots.Kind kind, float x, float y, Color tint, Sprite icon,
                  string value, Func<long, string> format)
        {
            var bg = UIKit.Img("Pill", Safe, Art.S("Ui/" + Skins.Trough), Color.white,
                               new Vector2(212f, 78f), Top, new Vector2(x, y));

            var glow = UIKit.Img("Glow", bg.transform, Art.Glow(96, 2f), Pal.A(tint, .30f),
                                 new Vector2(96f, 96f), Left, new Vector2(52f, 0f));
            var ic = UIKit.Img("Icon", bg.transform, icon, Color.white,
                               new Vector2(52f, 52f), Left, new Vector2(52f, 0f));
            ic.preserveAspect = true;
            if (icon == null) Flipbook.Attach(ic, "Ui/Coin", 11f);
            Tween.Breathe(ic.transform, .05f, 2.4f, x * .01f);

            var text = UIKit.Titled("V", bg.transform, value, 30, Pal.Cream, TextAnchor.MiddleCenter,
                                    new Vector2(112f, 44f), Centre, new Vector2(24f, 0f), 3f, 3f);

            ResourceSlots.Register(kind, (RectTransform)ic.transform, text, glow, tint, format);
        }

        // ------------------------------------------------------------------ the hero
        /// <summary>
        /// Where the player stands: the level as a medallion, the honorific under it, the XP
        /// bar to the next earned level, and the key that buys the next one outright.
        ///
        /// <b>The XP bar is the earned level's</b> — a bought level adds a whole rung and moves
        /// no XP, so the bar keeps meaning "how far to the next level by play" whatever was
        /// bought. Saying otherwise would be a bar that jumps to empty when you spend.
        /// </summary>
        float BuildHero(float y)
        {
            var plate = UIKit.Img("Hero", Safe, Art.S("Ui/" + Skins.Panel), Color.white,
                                  new Vector2(Width, HeroH), Top, new Vector2(0f, -(y + HeroH * .5f)));
            var host = plate.transform;

            // The medallion, left: a gold disc with the level in it and a halo behind it. Drawn
            // rather than cut - the same disc the hub and the profile wear (invariant 7b).
            _medallion = UIKit.Box("Medallion", host, new Vector2(200f, 200f), Left, new Vector2(130f, 12f));
            UIKit.Halo(_medallion, Pal.Gold, 300f, .30f);
            var ring = UIKit.Img("Ring", _medallion, Art.Ring(256, 14f), Pal.Gold, new Vector2(196f, 196f), Centre, Vector2.zero);
            ring.raycastTarget = false;
            Tween.RotateBy((RectTransform)ring.transform, 360f, 48f).Loop(-1, false);
            UIKit.Img("Disc", _medallion, Art.Disc(256), Pal.Gold, new Vector2(164f, 164f), Centre, Vector2.zero);
            _levelNumber = UIKit.Titled("N", _medallion, "1", 72, new Color(.30f, .20f, .05f),
                                        TextAnchor.MiddleCenter, new Vector2(160f, 100f), Centre,
                                        new Vector2(0f, 2f), 0f, 0f);
            _honorific = UIKit.Shrinkable(
                UIKit.Titled("Title", host, string.Empty, 26, Pal.Gold, TextAnchor.MiddleCenter,
                             new Vector2(260f, 34f), Left, new Vector2(130f, -112f), 3f, 2f), 16);

            // The bar, middle. The kit's trough with the kit's fill in it.
            const float BarLeft = 260f, BarRight = 690f, BarW = BarRight - BarLeft, BarX = (BarLeft + BarRight) * .5f;
            UIKit.Titled("Head", host, Loc.Get("ui.keeper.level_heading").ToUpperInvariant(), 26, Pal.Gold,
                         TextAnchor.MiddleLeft, new Vector2(BarW, 34f), Left, new Vector2(BarX, 88f), 3f, 2f);
            var track = UIKit.Img("XpTrack", host, Art.S("Ui/" + Skins.Trough), Color.white,
                                  new Vector2(BarW, 40f), Left, new Vector2(BarX, 34f));
            _xpFill = UIKit.Img("XpFill", track.transform, Art.S("Ui/" + Skins.Fill), Pal.Mint,
                                new Vector2(0f, 30f), Left, new Vector2(5f, 0f));
            var fillRT = (RectTransform)_xpFill.transform;
            fillRT.pivot = new Vector2(0f, .5f);
            fillRT.sizeDelta = new Vector2(0f, 30f);
            _xpLine = UIKit.Shrinkable(
                UIKit.Titled("XpText", host, string.Empty, 22, new Color(.86f, .90f, 1f, .85f),
                             TextAnchor.MiddleLeft, new Vector2(BarW, 30f), Left, new Vector2(BarX, -6f), 2f, 2f), 14);
            _boughtLine = UIKit.Shrinkable(
                UIKit.Titled("Bought", host, string.Empty, 22, Pal.Gold, TextAnchor.MiddleLeft,
                             new Vector2(BarW, 30f), Left, new Vector2(BarX, -40f), 2f, 2f), 14);

            // The key, right. Repainted rather than rebuilt, so a purchase does not replay its pop.
            _buy = UIKit.TextButton("Buy", host, Skins.Buy, string.Empty, 28, new Vector2(KeyW, 96f),
                                    Right, new Vector2(-40f - KeyW * .5f, 22f), Buy);
            _keyLabel = _buy.Label;
            UIKit.OneLine(_buy, 16);
            var priceRow = UIKit.Box("Price", host, new Vector2(KeyW, 44f), Right, new Vector2(-40f - KeyW * .5f, -48f));
            _keyPrice = UIKit.Titled("Amount", priceRow, string.Empty, 30, Pal.Cream, TextAnchor.MiddleCenter,
                                     new Vector2(KeyW - 60f, 44f), Centre, new Vector2(-22f, 0f), 3f, 2f);
            _keyIcon = UIKit.Img("Icon", priceRow, null, Color.white, new Vector2(40f, 40f), Right, new Vector2(-18f, 0f));
            _keyIcon.preserveAspect = true;

            return y + HeroH + 10f;
        }

        /// <summary>The one thing a bought level never moves, said where the key is.</summary>
        float BuildNote(float y)
        {
            UIKit.Shrinkable(
                UIKit.Titled("Note", Safe, Loc.Get("ui.keeper.rank_note"), 22,
                             new Color(.86f, .90f, 1f, .70f), TextAnchor.MiddleCenter,
                             new Vector2(Width - 40f, NoteH), Top, new Vector2(0f, -(y + NoteH * .5f)), 2f, 2f), 14);
            return y + NoteH + 6f;
        }

        float BuildHeadings(float y)
        {
            float cy = -(y + HeadingH * .5f);
            UIKit.Titled("LadderHead", Safe, Loc.Get("ui.keeper.ladder_heading").ToUpperInvariant(), 30, Pal.Gold,
                         TextAnchor.MiddleLeft, new Vector2(500f, HeadingH), Top, new Vector2(-Width * .5f + 270f, cy), 3f, 3f);
            _topLine = UIKit.Titled("TopHead", Safe, string.Empty, 24, new Color(.86f, .90f, 1f, .72f),
                                    TextAnchor.MiddleRight, new Vector2(440f, HeadingH), Top,
                                    new Vector2(Width * .5f - 240f, cy), 2f, 2f);
            return y + HeadingH;
        }

        Text _topLine;

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
            // above it (a retune that lowered the top), so the crowned row is always on the page.
            _top = Mathf.Max(ladder.Sells ? ladder.Top : _standing, _standing, 1);

            ReadFacts(ladder);
            PaintHero();

            if (_topLine) _topLine.text = Loc.Format("ui.keeper.to_top", _top);

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
                if (_drawnLevel >= 0 && _drawnLevel != level.Level) Tween.Punch(_medallion, .16f, .4f);
                _drawnLevel = level.Level;
            }
            if (_honorific) _honorific.text = Loc.Get(KeeperTitle.KeyFor(level.Level));

            if (_xpFill)
            {
                const float BarW = 690f - 260f;
                float w = (BarW - 12f) * earned.Progress01;
                var rt = (RectTransform)_xpFill.transform;
                Tween.Run(.6f, Ease.OutCubic, t => { if (rt) rt.sizeDelta = new Vector2(Mathf.Lerp(rt.sizeDelta.x, w, t), 30f); }, _xpFill);
            }
            if (_xpLine)
                _xpLine.text = earned.IsMaxLevel
                    ? Loc.Get("ui.profile.xp_max")
                    : Loc.Format("ui.profile.xp", earned.XpIntoLevel, earned.XpForNextLevel);

            int bought = KeeperLedger.Bought;
            if (_boughtLine)
                _boughtLine.text = bought > 0 ? Loc.Format("ui.keeper.bought_n", bought)
                                              : Loc.Get("ui.keeper.bought_none");

            PaintKey();
        }

        void PaintKey()
        {
            if (!_buy) return;

            if (_offer.Sold)
            {
                _buy.Interactable = true;
                _buy.SetCaption(Loc.Format("ui.keeper.buy", _offer.Level).ToUpperInvariant());
                Reskin(_buy, _offer.Currency == Currency.Gems ? Skins.Gem : Skins.Buy);
                _keyPrice.text = Compact.Number(_offer.Price);
                _keyIcon.enabled = true;
                _keyIcon.sprite = _offer.Currency == Currency.Gems ? Art.S("Ui/ic_gem") : Art.CoinFace();
            }
            else
            {
                _buy.Interactable = false;
                _buy.SetCaption(Loc.Get(_offer.AtTop ? "ui.keeper.key_top" : "ui.keeper.key_not_sold").ToUpperInvariant());
                Reskin(_buy, Skins.Shut);
                _keyPrice.text = string.Empty;
                _keyIcon.enabled = false;
            }
        }

        static void Reskin(Btn key, string skin)
        {
            var img = key.GetComponent<Image>();
            if (img) img.sprite = Art.S("Ui/" + skin);
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
                    // The ledger's change has already repainted; bring the crowned row into view.
                    if (_grid) _grid.ScrollTo(_standing - 1, centre: true);
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

        // ------------------------------------------------------------------ the rows
        /// <summary>What one level opens, and what it costs to buy. Read once per repaint.</summary>
        sealed class RowFacts
        {
            public int Level;
            public string Currency;
            public long Price;
            public bool Sold;
            public readonly List<string> Lines = new List<string>(3);
            public string Thumb;                          // a turret's shelf thumbnail, or null
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

            // Turrets, by the roster's gate. A starter has no gate and no line.
            foreach (var model in WardLedger.Catalog.Models)
            {
                if (model == null || model.MinLevel <= 0 || model.MinLevel > _top) continue;
                var facts = At(model.MinLevel);
                facts.Lines.Add(Loc.Format("ui.keeper.unlocks_turret", Loc.Get(model.NameKey)));
                if (facts.Thumb == null) facts.Thumb = "Ui/" + model.Thumb;
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
                    At(chapter.MinKeeperLevel).Lines.Add(
                        Loc.Format("ui.keeper.unlocks_chapter", Loc.Get(chapter.NameKey)));
                }
            }

            // Honorifics, where the title changes.
            for (int level = 2; level <= _top; level++)
            {
                string key = KeeperTitle.KeyFor(level);
                if (key != KeeperTitle.KeyFor(level - 1))
                    At(level).Lines.Add(Loc.Format("ui.keeper.title_tier", Loc.Get(key)));
            }
        }

        RowFacts FactsFor(int level) => _facts.TryGetValue(level, out var f) ? f : null;

        /// <summary>The state a row is drawn in.</summary>
        enum RowState { Reached, Bought, Standing, Next, Locked }

        RowState StateOf(int level)
        {
            if (level == _standing) return RowState.Standing;
            if (level < _standing) return level > _earned ? RowState.Bought : RowState.Reached;
            return level == _standing + 1 ? RowState.Next : RowState.Locked;
        }

        /// <summary>
        /// One level of the ladder: a node on the spine, the plate beside it. Every field is
        /// written on every bind (invariant 44mc) - a recycled cell that leaves a field alone
        /// shows the previous row's answer.
        /// </summary>
        sealed class LevelCell : IGridCell
        {
            readonly KeeperScreen _screen;
            readonly Image _spineAbove, _spineBelow, _node, _pool, _thumb, _priceIcon;
            readonly Text _number, _title, _line1, _line2, _line3, _chipText, _price;
            readonly Image _chip;
            readonly Btn _buy;
            readonly Image _plate;
            int _pulsing = -1;

            public RectTransform Root { get; }

            public LevelCell(KeeperScreen screen, RectTransform parent)
            {
                _screen = screen;

                Root = UIKit.Node("Level", parent);
                Root.sizeDelta = new Vector2(Width, CellH);

                // The light behind the next row, first so it sits under the plate (48i).
                _pool = UIKit.Img("Light", Root, Art.Glow(128, 1.35f), Pal.A(Pal.Sun, 0f),
                                  new Vector2(PlateW + 160f, RowH + 140f), Centre, new Vector2(PlateX, 0f));

                // The spine: two halves so the segment above the node and the one below can be
                // lit apart - the run up to the standing level is lit, everything past it is not.
                _spineAbove = UIKit.Img("SpineUp", Root, Art.Pixel, Color.white,
                                        new Vector2(SpineW, CellH * .5f), new Vector2(.5f, 1f), new Vector2(SpineX, CellH * .5f));
                _spineBelow = UIKit.Img("SpineDown", Root, Art.Pixel, Color.white,
                                        new Vector2(SpineW, CellH * .5f), new Vector2(.5f, 0f), new Vector2(SpineX, -CellH * .5f));

                _plate = UIKit.Img("Plate", Root, Art.S("Ui/" + Skins.PlateNavy), Color.white,
                                   new Vector2(PlateW, RowH), Centre, new Vector2(PlateX, 0f));
                var plate = _plate.transform;

                _node = UIKit.Img("Node", Root, null, Color.white, new Vector2(NodeSize, NodeSize), Centre, new Vector2(SpineX, 0f));
                _node.preserveAspect = true;
                _number = UIKit.Titled("N", _node.transform, string.Empty, 34, new Color(.20f, .16f, .06f),
                                       TextAnchor.MiddleCenter, new Vector2(NodeSize, 60f), Centre, new Vector2(0f, 16f), 0f, 0f);

                const float TextLeft = 36f;
                _thumb = UIKit.Img("Thumb", plate, null, Color.white, new Vector2(84f, 84f), Left, new Vector2(TextLeft + 42f, 0f));
                _thumb.preserveAspect = true;

                _title = UIKit.Titled("Title", plate, string.Empty, 30, Pal.Gold, TextAnchor.MiddleLeft,
                                      new Vector2(440f, 36f), Left, new Vector2(TextLeft + 220f, 42f), 3f, 3f);
                _line1 = Line(plate, "L1", 8f);
                _line2 = Line(plate, "L2", -20f);
                _line3 = Line(plate, "L3", -48f);

                // The right end: a chip, or the key. Both built, one shown.
                _chip = UIKit.Img("Chip", plate, Art.Round(23), Pal.Gold, new Vector2(ChipW, ChipH), Right, new Vector2(-30f - ChipW * .5f, 0f));
                _chipText = UIKit.Titled("ChipText", _chip.transform, string.Empty, 20, new Color(.30f, .20f, .05f),
                                         TextAnchor.MiddleCenter, new Vector2(ChipW - 16f, ChipH), Centre, Vector2.zero, 0f, 0f);
                UIKit.Shrinkable(_chipText, 12);

                _buy = UIKit.TextButton("Buy", plate, Skins.Buy, string.Empty, 24, new Vector2(KeyW, KeyH),
                                        Right, new Vector2(-30f - KeyW * .5f, 12f), () => _screen.Buy());
                UIKit.OneLine(_buy, 14);
                _price = UIKit.Titled("Price", plate, string.Empty, 22, Pal.Cream, TextAnchor.MiddleRight,
                                      new Vector2(KeyW - 50f, 30f), Right, new Vector2(-30f - 46f - (KeyW - 50f) * .5f, -46f), 2f, 2f);
                _priceIcon = UIKit.Img("PriceIcon", plate, null, Color.white, new Vector2(30f, 30f), Right, new Vector2(-30f - 20f, -46f));
                _priceIcon.preserveAspect = true;
            }

            static Text Line(Transform plate, string name, float y)
                => UIKit.Shrinkable(
                       UIKit.Titled(name, plate, string.Empty, 22, Pal.Cream, TextAnchor.MiddleLeft,
                                    new Vector2(440f, 28f), Left, new Vector2(36f + 220f, y), 2f, 2f), 13);

            public void Bind(int index)
            {
                int level = index + 1;
                var facts = _screen.FactsFor(level);
                var state = _screen.StateOf(level);

                bool reached = level <= _screen._standing;
                bool crowned = state == RowState.Standing;
                bool next = state == RowState.Next;

                // The spine: lit up to and including the standing level.
                var lit = Pal.A(Pal.Gold, .95f);
                var dim = new Color(1f, 1f, 1f, .10f);
                _spineAbove.color = level == 1 ? Color.clear : (reached ? lit : dim);
                _spineBelow.color = level >= _screen._top ? Color.clear : (level < _screen._standing ? lit : dim);

                // The node: the pack's three discs.
                _node.sprite = Art.S(crowned ? "Ui/keeper_node_crown" : reached ? "Ui/keeper_node_open" : "Ui/keeper_node_locked");
                _node.enabled = _node.sprite != null;
                _number.text = level.ToString();
                _number.color = reached ? new Color(.20f, .16f, .06f) : new Color(.95f, .95f, .98f);

                // The plate and its light.
                _plate.color = reached || next ? Color.white : new Color(.72f, .76f, .84f, .92f);
                _pool.color = Pal.A(Pal.Sun, next ? .34f : 0f);

                _title.text = Loc.Format("ui.keeper.level_n", level).ToUpperInvariant();
                _title.color = crowned ? Pal.Sun : reached ? Pal.Gold : new Color(.80f, .84f, .92f);

                // What the level opens, then what it costs when it has yet to be reached.
                var lines = facts?.Lines;
                int n = lines?.Count ?? 0;
                _line1.text = n > 0 ? lines[0] : (reached ? Loc.Get("ui.keeper.reached")
                                                  : facts != null && facts.Sold ? PriceLine(facts) : Loc.Get("ui.keeper.earn_it"));
                _line2.text = n > 1 ? lines[1] : string.Empty;
                _line3.text = n > 2 ? lines[2] : string.Empty;
                var ink = reached ? Pal.Cream : new Color(.80f, .84f, .92f);
                _line1.color = n > 0 ? ink : new Color(.86f, .90f, 1f, .62f);
                _line2.color = ink;
                _line3.color = ink;

                // The thumbnail, or the space it would take.
                var thumb = facts?.Thumb != null ? Art.S(facts.Thumb) : null;
                _thumb.sprite = thumb;
                _thumb.enabled = thumb != null;
                _thumb.color = reached || next ? Color.white : new Color(1f, 1f, 1f, .55f);
                float textX = thumb != null ? 36f + 220f : 36f + 220f - 60f;
                ((RectTransform)_title.transform).anchoredPosition = new Vector2(textX, 42f);
                ((RectTransform)_line1.transform).anchoredPosition = new Vector2(textX, 8f);
                ((RectTransform)_line2.transform).anchoredPosition = new Vector2(textX, -20f);
                ((RectTransform)_line3.transform).anchoredPosition = new Vector2(textX, -48f);

                // The right end.
                var offer = _screen._offer;
                bool key = next && offer.Sold && offer.Level == level;
                _buy.gameObject.SetActive(key);
                _price.enabled = key;
                _priceIcon.enabled = key;
                if (key)
                {
                    _buy.SetCaption(Loc.Get("ui.keeper.buy_short").ToUpperInvariant());
                    var img = _buy.GetComponent<Image>();
                    if (img) img.sprite = Art.S("Ui/" + (offer.Currency == Currency.Gems ? Skins.Gem : Skins.Buy));
                    _price.text = Compact.Number(offer.Price);
                    _priceIcon.sprite = offer.Currency == Currency.Gems ? Art.S("Ui/ic_gem") : Art.CoinFace();
                }

                string chip = crowned ? Loc.Get("ui.keeper.here")
                            : state == RowState.Bought ? Loc.Get("ui.keeper.bought_chip")
                            : next && !offer.Sold && offer.AtTop ? string.Empty
                            : string.Empty;
                _chip.gameObject.SetActive(chip.Length > 0);
                _chipText.text = chip.ToUpperInvariant();
                _chip.color = crowned ? Pal.Sun : Pal.Mint;

                // The breath on the next row, keyed on the row it was started for (44mc).
                if (next && _pulsing != level)
                {
                    _pulsing = level;
                    Tween.Breathe(_pool.transform, .06f, 2.0f, level * .13f);
                }
                else if (!next && _pulsing >= 0)
                {
                    _pulsing = -1;
                    Tween.KillChannel(_pool.transform, "breathe");
                    _pool.transform.localScale = Vector3.one;
                }
            }

            static string PriceLine(RowFacts facts)
                => Loc.Format(facts.Currency == Currency.Gems ? "ui.keeper.price_gems" : "ui.keeper.price_coins",
                              Compact.Number(facts.Price));
        }
    }
}
