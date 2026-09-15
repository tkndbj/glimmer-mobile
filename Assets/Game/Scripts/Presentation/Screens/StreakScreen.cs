using System;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The streak: how long the flame has been lit, the lap of nights that pays for it, and
    /// the one thing on the page that is for sale.
    ///
    /// <para>
    /// <b>The page is the tasks page's page, and that is the whole of the rebuild.</b> This
    /// screen used to stand on its own night backdrop with a board of coloured jelly squares
    /// and a floating count — a design from before this game had an interface kit, and the
    /// last screen still wearing it. It is the same furniture as Tasks &amp; Bonuses and The
    /// Bloom now: the kit's plates on <see cref="Scenery.Plain"/>, a title ribbon over the
    /// wallet, one hero plate carrying the number the page is graded on, one offer row, and a
    /// board under a heading. A streak, a slate and a season are the same idea at three
    /// cadences and there was never a reason for them to be three designs (invariant 47g).
    /// </para>
    /// <para>
    /// <b>A night pays a chest, so a night opens the same ceremony every other chest opens</b>
    /// (<see cref="ChestOverlay"/> through <see cref="ChestClaim"/>). That is what let the
    /// ladder become coins, gems and chests without this file learning anything about odds,
    /// utilities or lids — and it is why only the <em>earliest</em> waiting night can be
    /// taken: the collected floor is a floor, so a sweep would grant three chests behind one
    /// animation. See <see cref="DailyStreak.CollectableAt"/>.
    /// </para>
    /// <para>
    /// <b>The shield is bought here and nowhere else.</b> A gem debit is an ordinary spend
    /// (invariant 18), so there is no store sheet, no receipt and nothing to wait on: the
    /// purchase is <see cref="DailyStreak.TryBuyShield"/> and the page repaints. What it buys
    /// is a window of days the streak survives without being played — which is why a
    /// protected page stops asking the player to hurry, and why the row that sells it turns
    /// into a row that reports it.
    /// </para>
    /// <para>
    /// <b>Nothing here ends.</b> The count above the board climbs for ever and the ladder
    /// laps under it: night eight pays night one, night fifteen opens the third week, and the
    /// board is a window onto whichever lap holds the oldest thing the player has not taken.
    /// Every tile is labelled with its <em>absolute</em> night, because a board that
    /// renumbered itself at the end of a week would read as the streak having been reset.
    /// </para>
    /// </summary>
    public sealed class StreakScreen : View
    {
        public override string Track => "mus_menu";

        // The stack, in canvas units from the top of the safe area.
        const float ChromeSize = 92f;
        const float BannerH = 138f;
        const float HeroH = 240f;
        /// <summary>
        /// How tall the offer row is.
        ///
        /// <b>Taller than the season's pass row it was copied from</b>, and the reason is that
        /// it is not the same row. The pass sits under a hero on a page of forty cards and is
        /// one of many things to read; this is the <em>only</em> thing on its page that is for
        /// sale, it sits between the hero and the board, and at the pass's 132 it read as a
        /// status strip rather than as an offer. A row nobody sees is a row nobody buys.
        /// </summary>
        const float ShieldH = 168f;
        const float HeadingH = 62f;
        const float Width = 1000f;

        /// <summary>
        /// A night tile, and how many sit in a row.
        ///
        /// Four across is what fits at a size a thumb can read on a phone: a tile has to
        /// carry a caption, a reward — which for a chest night is a picture with an aspect of
        /// its own — and an amount, and five across takes the chest below the size where a
        /// silver and a gold are told apart at a glance.
        /// </summary>
        const float TileW = 240f;
        const float TileH = 320f;
        const float TileGap = 18f;
        const int PerRow = 4;

        /// <summary>
        /// How much of the board band's spare height falls <em>above</em> the tiles.
        ///
        /// Not a half, because the band is measured from under the heading: centring puts a
        /// hole between the heading and the thing it names. See <see cref="BuildBoard"/>.
        /// </summary>
        const float BoardLead = .28f;

        /// <summary>
        /// The well a night's reward stands in, and how big the reward is drawn in it.
        ///
        /// <para>
        /// <b><see cref="RewardTall"/> is a <em>drawn</em> height</b>, which is the only unit a
        /// chest and a gem can share: a chest's icon carries the lid's headroom (see
        /// <see cref="ChestPack"/>), so a box set straight from a height would draw the chest
        /// two thirds the size of the gem on the tile beside it and float it high. The pack
        /// converts once, here as on the hub and the tasks page.
        /// </para>
        /// </summary>
        const float SeatSize = 176f, SeatY = 14f, RewardTall = 126f;

        static readonly Vector2 Top = new Vector2(.5f, 1f);
        static readonly Vector2 Left = new Vector2(0f, .5f);
        static readonly Vector2 Right = new Vector2(1f, .5f);
        static readonly Vector2 Centre = new Vector2(.5f, .5f);

        /// <summary>
        /// The bar's orange and its full green, pre-divided for <see cref="Skins.Fill"/>'s
        /// multiply. The tasks page's numbers, and deliberately the same two: a bar means one
        /// thing across this game, and a third screen inventing its own would be a third
        /// answer to a question a swatch sheet settled once (invariants 37l, 45h).
        /// </summary>
        static readonly Color BarOrange = new Color(1f, .588f, .118f, 1f);
        static readonly Color BarFull = new Color(.376f, .922f, .275f, 1f);
        const float BarH = 26f;

        /// <summary>
        /// What <see cref="Scenery.Pill"/> really leaves its words at 300 wide: the glyph's
        /// lane comes off the height and sixteen units come off the right. Written here
        /// rather than at the call site because the clock is re-fitted on every tick and the
        /// two have to agree — a fitter shrinking against a width the pill does not have is a
        /// fitter that lets the text spill anyway (the season's rule).
        /// </summary>
        const float ClockRoom = 340f - 54f * .82f - 16f;

        // --------------------------------------------------------------- state
        StreakTable _ladder;
        int _rungs;
        int _days;
        int _pending;
        int _first;
        int _cycle;
        bool _playedToday;

        Text _count, _caption, _state, _lap, _shieldHint, _shieldName;
        Image _bar;
        RectTransform _fill, _flame, _heroHost;
        Btn _shieldBtn;
        Image _shieldCrest;
        float _track;
        bool? _barFull;

        float _clockTick;

        /// <summary>
        /// Whether the page was built with something to ask for, so a repaint can notice that
        /// it no longer is.
        ///
        /// The footer is the one part of this page whose <em>shape</em> depends on the state
        /// rather than its words — it takes height out of the board — so when the answer
        /// moves the honest response is a redraw. It moves for three reasons and only one of
        /// them is midnight: finishing a glade, and buying a shield, both take the ask away
        /// while the page is open.
        /// </summary>
        bool _asking;

        /// <summary>
        /// True while a night is being handed over. The page is mid-animation and describes
        /// state it is in the middle of changing, so a rebuild underneath it would destroy
        /// the tiles the sequence is still animating.
        /// </summary>
        bool _collecting;

        /// <summary>The reels, so the first tap on a chest night finds its lid already loaded.</summary>
        AssetHold _reels;

        readonly List<NightTile> _tiles = new List<NightTile>();

        /// <summary>How a single night reads. Derived on every tile from the same facts.</summary>
        enum Night
        {
            /// <summary>Reached and paid. Wears a seal.</summary>
            Kept,

            /// <summary>Reached, pays something, not yet taken. This is the one that shines.</summary>
            Waiting,

            /// <summary>The night a run finished today would land on.</summary>
            Tonight,

            /// <summary>Still ahead.</summary>
            Ahead,
        }

        /// <summary>
        /// The pieces of one night a payout has to reach back into.
        ///
        /// Held rather than re-found because the payout retints the card, stamps a seal and
        /// throws the reward across the screen, and hunting for those by name afterwards is
        /// how an animation ends up drawing the wrong tile.
        /// </summary>
        sealed class NightTile
        {
            public int Night;
            public RectTransform Root;
            public Image Card;
            public Image Icon;

            /// <summary>The coloured plate the night rides on. Repainted per state.</summary>
            public Image Chip;

            public Text ChipText;

            /// <summary>What the night pays, on its own strip. Dimmed with the rest when kept.</summary>
            public Text Amount;
            public Image Pool;
            public Image Rim;
            public RectTransform Halo;
            public RectTransform Seal;
            public Btn Tap;
            public StreakRung Rung;
            public CanvasGroup Group;

            /// <summary>
            /// Whether the light is already running on this tile.
            ///
            /// Held rather than re-derived, because <see cref="Shine"/> and the bob are
            /// looping tweens: started on every repaint they would restart on every counter
            /// that moves, which is a tile that jumps each time anything else on the page
            /// changes (invariant 16k).
            /// </summary>
            public bool Lit;
        }

        // ---------------------------------------------------------------- build
        protected override void Build()
        {
            _ladder = DailyStreak.Ladder;
            _rungs = Mathf.Max(1, _ladder.Length);
            _days = DailyStreak.Days;
            _pending = DailyStreak.Pending;
            _playedToday = DailyStreak.PlayedToday;
            _first = DailyStreak.BoardFirstNight;
            _cycle = DailyStreak.CycleOf(_first, _rungs);
            _tiles.Clear();
            _barFull = null;

            // The quiet ground every screen that is a list rather than a place stands on. The
            // painting belongs on the hub and the map; this page is plates.
            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 22, new Color(1f, .93f, .70f), 6f, 22f);

            _asking = Asking;

            float y = 22f;
            y = BuildHeader(y);
            y = BuildHero(y);
            y = BuildShield(y);
            y = BuildHeading(y);
            BuildBoard(y);
            BuildFooter();

            NavBar.Build(Content, NavBar.Tab.Home);
            HoldReels();

            Repaint();
        }

        void OnEnable()
        {
            DailyStreak.Changed += OnChanged;
        }

        void OnDisable()
        {
            DailyStreak.Changed -= OnChanged;
        }

        void OnDestroy()
        {
            _reels?.Dispose();
            _reels = null;
        }

        /// <summary>
        /// The chest reels, for the ceremony. Held here rather than only by the overlay so
        /// the first tap on a waiting chest night does not open a chest whose lid has not
        /// loaded (invariant 7b).
        /// </summary>
        void HoldReels() => Run(async token =>
        {
            _reels = _reels ?? AssetLibrary.Hold("chests");
            await _reels.LoadAsync(AssetManifest.ChestAssets(ProgressionRules.Table.Tasks), null, token);
        });

        /// <summary>
        /// Redraws on any change the page did not make itself.
        ///
        /// A payout raises this too and must not be allowed to act on it: the page is halfway
        /// through an animation that is already showing the new state, and tearing the board
        /// down under it would leave the sequence running against destroyed tiles.
        /// </summary>
        void OnChanged()
        {
            if (this == null || Content == null || _collecting) return;

            // The board is a window onto one lap. Taking the last night of a lap moves that
            // window, which is a different set of tiles rather than different words on the
            // same ones — the only case here a redraw is the honest answer to.
            if (DailyStreak.BoardFirstNight != _first || DailyStreak.Days != _days ||
                DailyStreak.PlayedToday != _playedToday)
            {
                Rebuild();
                return;
            }

            Repaint();
        }

        /// <summary>
        /// Draws the page again from scratch.
        ///
        /// The old children are hidden before they are destroyed: <c>Destroy</c> is deferred
        /// to the end of the frame, so without this the outgoing board draws over the
        /// incoming one for one frame.
        /// </summary>
        void Rebuild()
        {
            if (Content == null) return;

            for (int i = Content.childCount - 1; i >= 0; i--)
            {
                var child = Content.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            _count = _caption = _state = _lap = _shieldHint = _shieldName = null;
            _bar = _shieldCrest = null;
            _fill = _flame = _heroHost = null;
            _shieldBtn = null;

            Build();
        }

        /// <summary>
        /// Ticks the countdown, and rebuilds the page on the day it runs out.
        ///
        /// This is the one screen a player might sit on late at night watching the clock. A
        /// stale number reads as the game having already taken the streak away, and a page
        /// still showing "the flame goes out in 0h 00m" ten minutes after midnight is worse
        /// than that — it is wrong.
        ///
        /// Polled every second whatever the state, not only while the streak is at risk.
        /// Midnight passes just as often for a player who has already played today, and after
        /// it their board is a day stale — the night they kept is now the night before, and
        /// the reward waiting on it is drawn on the wrong tile.
        /// </summary>
        void Update()
        {
            if (_collecting) return;

            _clockTick += Time.unscaledDeltaTime;
            if (_clockTick < 1f) return;
            _clockTick = 0f;

            if (DailyStreak.Days != _days || DailyStreak.PlayedToday != _playedToday)
            {
                Rebuild();
                return;
            }

            RefreshState();
        }

        // --------------------------------------------------------------- header
        /// <summary>
        /// The corners, the page's name, what it is, and the wallet — the tasks page's order,
        /// for its reason: the first thing read on a page about what there is to earn should
        /// be what the page is, and the pills are where <see cref="RewardFlight"/> lands a
        /// night's tokens.
        /// </summary>
        float BuildHeader(float y)
        {
            float cy = -(y + BannerH * .5f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * ChromeSize,
                             new Vector2(0f, 1f), new Vector2(76f, cy),
                             () => Flow.Go<HomeScreen>());
            UIKit.IconButton("Info", Safe, Skins.Aside, "ic_info", Vector2.one * ChromeSize,
                             new Vector2(1f, 1f), new Vector2(-76f, cy),
                             () => { if (!Flow.HasModal) Flow.Modal<StreakInfoOverlay>(); });

            var ribbon = Scenery.TitleRibbon(Safe, Loc.Get("ui.streak.title").ToUpperInvariant(),
                                             new Vector2(720f, BannerH), Top, new Vector2(0f, cy), 42);
            ribbon.transform.localScale = Vector3.zero;
            Tween.Pop(ribbon.transform, 0f, .5f, .06f);
            y += BannerH + 4f;

            UIKit.Shrinkable(
                UIKit.Titled("Sub", Safe, Loc.Get("ui.streak.subtitle"), 24,
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

            // **All three, including the hearts.** This page used to watch credits and gems by
            // hand and draw a hearts pill it never wrote to — hearts move on a refill timer
            // rather than on a spend, so it was the one of the three that could go stale while
            // somebody sat here waiting for midnight.
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

        // ----------------------------------------------------------------- hero
        /// <summary>
        /// What the page is graded on, in the middle of the plate: the flame, the count at a
        /// size that says it matters, how far along the lap that is, and the one line that
        /// changes while the page is open.
        ///
        /// <para>
        /// <b>The count is the hero for the Infinite lane's medal's reason</b> (43b): a thing
        /// graded on one number puts that number in the middle of the screen. Under it the bar
        /// measures the <em>lap</em> rather than the streak — a streak has no end, so a bar
        /// against it would be a bar that can never fill, which is the opposite of what a bar
        /// is for.
        /// </para>
        /// </summary>
        float BuildHero(float y)
        {
            var plate = UIKit.Img("Hero", Safe, Art.S("Ui/" + Skins.Panel), Color.white,
                                  new Vector2(Width, HeroH), Top, new Vector2(0f, -(y + HeroH * .5f)));
            _heroHost = (RectTransform)plate.transform;

            var clip = UIKit.Node("Clip", plate.transform);
            UIKit.StretchTo(clip, 6f, 6f, 6f, 6f);
            clip.gameObject.AddComponent<RectMask2D>();

            var rays = UIKit.Img("Rays", clip, Art.Rays(512, 14), Pal.A(Pal.Sun, .16f),
                                 new Vector2(760f, 760f), Left, new Vector2(150f, 10f));
            Tween.Run(42f, Ease.Linear,
                      t => { if (rays) rays.transform.localRotation = Quaternion.Euler(0, 0, t * 360f); },
                      rays, "spin").Loop(-1, false);

            // The flame, and the light behind it. Animated only while the streak is alive,
            // exactly as the hub's chip does it: a flame that flickers for a player with no
            // streak is decoration claiming to be a state.
            UIKit.Img("Glow", plate.transform, Art.Glow(128, 2f),
                      Pal.A(new Color(1f, .62f, .22f), _days > 0 ? .34f : .10f),
                      new Vector2(260f, 260f), Left, new Vector2(128f, 6f));

            var flame = UIKit.Img("Flame", plate.transform, null, Color.white,
                                  new Vector2(168f, 168f), Left, new Vector2(128f, 6f));
            flame.preserveAspect = true;
            _flame = (RectTransform)flame.transform;

            if (_days > 0)
            {
                Flipbook.Attach(flame, "Ui/Flame", 9f);
                Tween.Breathe(_flame, .05f, 2.6f);
            }
            else
            {
                var frames = Art.Frames("Ui/Flame");
                flame.sprite = frames != null && frames.Length > 0 ? frames[0] : null;
                flame.color = new Color(1f, 1f, 1f, .45f);
            }

            // The count, counted up rather than printed: it is the one number a player came to
            // see, and a number that arrives is worth a third of a second. Shrinkable because
            // it has no ceiling — the whole point of the feature is that it does not stop.
            _count = UIKit.Shrinkable(
                UIKit.Titled("Count", plate.transform, _days > 0 ? "0" : "—", 58, Pal.Cream,
                             TextAnchor.MiddleLeft, new Vector2(300f, 72f), Left,
                             new Vector2(236f + 150f, 34f), 4f, 5f), 30);

            if (_days > 0)
            {
                var label = _count;
                Tween.Value(0f, _days, .7f,
                            v => { if (label) label.text = Mathf.RoundToInt(v).ToString(); },
                            Ease.OutCubic, label).Delay(.18f);
            }

            _caption = UIKit.Shrinkable(
                UIKit.Titled("Cap", plate.transform, string.Empty, 24, Pal.A(Pal.Cream, .82f),
                             TextAnchor.MiddleLeft, new Vector2(300f, 32f), Left,
                             new Vector2(236f + 150f, -12f), 3f, 3f), 15);

            // How far through the lap, at the right end of the plate. A fraction rather than a
            // second bar, because the bar beside it is already measuring the same run and two
            // bars on one plate would be two readings of one thing (37v's rule about what a
            // corner owes).
            const float LapW = 232f;
            float lx = Width * .5f - LapW * .5f - 26f;

            UIKit.Img("LapWell", plate.transform, Art.S("Ui/" + Skins.Trough), Color.white,
                      new Vector2(LapW, 104f), Centre, new Vector2(lx, -22f));

            _lap = UIKit.Shrinkable(
                UIKit.Titled("Lap", plate.transform, string.Empty, 40, Pal.Gold,
                             TextAnchor.MiddleCenter, new Vector2(LapW - 24f, 52f), Centre,
                             new Vector2(lx, -8f), 3f, 4f), 22);

            UIKit.Shrinkable(
                UIKit.Titled("LapCap", plate.transform, Loc.Get("ui.streak.nights"), 21,
                             Pal.A(Pal.Cream, .74f), TextAnchor.MiddleCenter,
                             new Vector2(LapW - 24f, 28f), Centre, new Vector2(lx, -48f), 0f, 0f), 13);

            _track = 460f;
            var trough = UIKit.Img("Trough", plate.transform, Art.S("Ui/" + Skins.Trough), Color.white,
                                   new Vector2(_track, 30f), Left, new Vector2(236f + _track * .5f, -52f));
            var fill = UIKit.Img("Fill", trough.transform, Art.S("Ui/" + Skins.Fill), BarOrange,
                                 new Vector2(0f, BarH), Left, new Vector2(4f, 0f));
            _fill = (RectTransform)fill.transform;
            _fill.pivot = new Vector2(0f, .5f);
            _bar = fill;

            // The one line that changes while the page is open, so it is a pill rather than
            // loose type: a countdown ticking on bare plate reads as a glitch. Placed with
            // `UIKit.Corner`, because `UIKit.Box` always pivots at centre — handed the margin
            // directly, a 340-wide pill tucked 28 units from the plate's right edge hangs 142
            // of them off it (the season's trap, and the win panel's before that).
            const float ClockW = 340f, ClockH = 54f;

            _state = UIKit.OneLineLabel(
                Scenery.Pill(plate.transform, string.Empty, 23, new Vector2(ClockW, ClockH),
                             new Vector2(1f, 1f),
                             UIKit.Corner(new Vector2(ClockW, ClockH), new Vector2(1f, 1f), 28f, 20f),
                             new Color(.05f, .09f, .18f, .78f), "ic_streak"),
                ClockRoom, 14);

            plate.transform.localScale = Vector3.zero;
            Tween.Pop(plate.transform, 0f, .5f, .10f);

            return y + HeroH + 14f;
        }

        // --------------------------------------------------------------- shield
        /// <summary>
        /// The one thing on this page that is for sale, and the one row that changes shape
        /// when it is bought: an offer while no window is running, a plain report of how many
        /// days are left once one is. A build whose content authored no price draws neither
        /// and the row costs nothing.
        ///
        /// <para>
        /// The season's pass row, deliberately — a gem-priced permanent thing on a plate with
        /// a crest, a sentence and a price — with one difference that matters. A pass is held
        /// for ever, so its bought state is a single word; a shield <em>runs out</em>, so its
        /// bought state is a countdown in days, which is the whole of what the player paid for
        /// and therefore the thing the row has to say.
        /// </para>
        /// </summary>
        float BuildShield(float y)
        {
            if (!DailyStreak.SellsShield) return y;

            var plate = UIKit.Img("Shield", Safe, Art.S("Ui/" + Skins.PlateBlue), Color.white,
                                  new Vector2(Width, ShieldH), Top, new Vector2(0f, -(y + ShieldH * .5f)));

            UIKit.Img("Glow", plate.transform, Art.Glow(128, 2f), Pal.A(Pal.Mint, .24f),
                      new Vector2(262f, 262f), Left, new Vector2(112f, 0f));

            _shieldCrest = UIKit.Img("Crest", plate.transform, Art.S("Ui/shield"), Color.white,
                                     new Vector2(126f, 126f), Left, new Vector2(112f, 0f));
            _shieldCrest.preserveAspect = true;

            // The room between the crest and the button, measured rather than guessed: the
            // crest ends at 156 and the button's near edge is at 1000 - 152 - 134 = 714. A
            // Unity label that overflows is not clipped and nothing says so (invariant 37n),
            // so the box is the gap and the fitter does the rest.
            const float HintW = 490f;

            _shieldName = UIKit.Shrinkable(
                UIKit.Titled("Name", plate.transform, Loc.Get("ui.streak.shield"), 36, Pal.Cream,
                             TextAnchor.MiddleLeft, new Vector2(HintW, 44f), Left,
                             new Vector2(196f + HintW * .5f, 24f), 3f, 4f), 21);

            _shieldHint = UIKit.Shrinkable(
                UIKit.Titled("Hint", plate.transform, string.Empty, 23, Pal.A(Pal.Cream, .82f),
                             TextAnchor.MiddleLeft, new Vector2(HintW, 34f), Left,
                             new Vector2(196f + HintW * .5f, -24f), 0f, 0f), 14);

            // `UIKit.Button` makes no label, so `SetCaption` would have nothing to write into.
            // `TextButton` is the one that builds a caption, and a price wants it anyway: a
            // trailing glyph is a *unit* on the number a caption ends with (`Btn.IconTrails`),
            // which is how every other price in this game says which currency it is.
            _shieldBtn = UIKit.TextButton("Buy", plate.transform, Skins.Gem, string.Empty, 36,
                                          new Vector2(272f, 104f), Right, new Vector2(-150f, 0f),
                                          BuyShield, Art.S("Ui/ic_gem"), iconTrails: true);

            plate.transform.localScale = Vector3.zero;
            Tween.Pop(plate.transform, 0f, .5f, .14f);

            return y + ShieldH + 14f;
        }

        /// <summary>
        /// Buys a window of protected days, or says which wall it met.
        ///
        /// Four answers rather than one, and the order is the order a player needs them in:
        /// already protected first (which is not a failure and says so quietly), then having
        /// nothing to protect, then the price. A gem debit is an ordinary spend, so there is
        /// no third party to wait on and no state this screen has to fetch before it can sell.
        /// </summary>
        void BuyShield()
        {
            if (_collecting || Flow.HasModal) return;

            switch (DailyStreak.TryBuyShield())
            {
                case DailyStreak.ShieldBuy.Bought:
                    Audio.Sfx("collect", .8f);
                    if (_shieldBtn) Tween.Punch(_shieldBtn.transform, .14f, .32f);
                    if (_shieldCrest)
                    {
                        Tween.Punch(_shieldCrest.transform, .2f, .4f);
                        Burst.Sparks(_shieldCrest.transform, Vector2.zero, Pal.Mint, 18, 320f, 26f, .62f);
                    }
                    Burst.Confetti(Content, 28);
                    Scenery.Toast(Content,
                                  Loc.Format("ui.streak.shield_bought", DailyStreak.ShieldDays),
                                  Pal.Mint, 2.8f);
                    Repaint();
                    return;

                case DailyStreak.ShieldBuy.Held:
                    Scenery.Toast(Content, Loc.Get("ui.streak.shield_held"), Pal.Mint);
                    return;

                case DailyStreak.ShieldBuy.NoStreak:
                    Scenery.Toast(Content, Loc.Get("ui.streak.shield_none"), Pal.Gold, 2.6f);
                    return;

                case DailyStreak.ShieldBuy.NotSold:
                    return;

                default:
                    // The one refusal worth sending somewhere: a player short of gems is a
                    // player one screen away from having them.
                    Scenery.Toast(Content, Loc.Format("ui.streak.shield_too_poor",
                                                      DailyStreak.ShieldGems), Pal.Rose, 2.8f);
                    return;
            }
        }

        // -------------------------------------------------------------- heading
        /// <summary>
        /// The board's heading, pinned above it rather than scrolling with it, and the line
        /// that says the tiles can be tapped.
        ///
        /// A tile carries a night, a picture and an amount and nothing on it says it is a
        /// button. Saying so once above the board is the arrangement every table in this game
        /// already uses — and it is the only reason a player would discover that a waiting
        /// night is taken by tapping it rather than by playing another glade.
        /// </summary>
        float BuildHeading(float y)
        {
            float cy = -(y + HeadingH * .5f);

            UIKit.Shrinkable(
                UIKit.Titled("H", Safe,
                             (_cycle > 1
                                 ? Loc.Format("ui.streak.week_n", _cycle)
                                 : Loc.Get("ui.streak.week_one")).ToUpperInvariant(),
                             28, Pal.Gold, TextAnchor.MiddleLeft, new Vector2(420f, 38f), Top,
                             new Vector2(-Width * .5f + 210f + 8f, cy), 3f, 3f), 17);

            UIKit.Shrinkable(
                UIKit.Titled("Hint", Safe, Loc.Get("ui.streak.board_hint"), 22,
                             Pal.A(Pal.Cream, .74f), TextAnchor.MiddleRight,
                             new Vector2(520f, 32f), Top,
                             new Vector2(Width * .5f - 260f - 8f, cy), 0f, 0f), 14);

            return y + HeadingH;
        }

        // ---------------------------------------------------------------- board
        /// <summary>
        /// One lap of the ladder, four nights to a row.
        ///
        /// <para>
        /// It scrolls only when it has to. A ladder is content —
        /// <see cref="StreakRules.MaxRungs"/> allows thirty — so a board sized to the shipped
        /// seven is a code change waiting on a content change, which is the failure invariant
        /// 4 exists to prevent. But a <c>ScrollRect</c> that is always there is not free: it
        /// clamps its content to the top of the viewport, so the seven that do fit would sit
        /// hard against the heading instead of centred in the space they have. The band is
        /// measured off the canvas rather than a reference height, or the shipped ladder is
        /// centred on exactly one device.
        /// </para>
        /// </summary>
        void BuildBoard(float top)
        {
            float bottom = FooterTop;

            var band = UIKit.Node("Board", Safe);
            UIKit.StretchTo(band, 0f, bottom, 0f, top);
            band.gameObject.AddComponent<RectMask2D>();

            int rows = Mathf.CeilToInt(_rungs / (float)PerRow);
            float boardH = rows * (TileH + TileGap) - TileGap;

            float slack = Mathf.Max(0f, (Flow.Size.y - top - bottom) - boardH);

            // Biased toward the heading rather than centred. A board centred in whatever is
            // left hangs a long way under the line that names it, which reads as two things
            // on the page rather than one thing with a title on it; the spare below is then
            // breathing room over the nav bar, which is what it should look like.
            slack *= BoardLead;

            var nights = UIKit.Node("Nights", band);
            nights.anchorMin = new Vector2(0f, 1f);
            nights.anchorMax = new Vector2(1f, 1f);
            nights.pivot = new Vector2(.5f, 1f);
            nights.sizeDelta = new Vector2(0f, boardH);
            nights.anchoredPosition = new Vector2(0f, -slack * .5f);

            // Every waiting tile's pool of light lives here, built before any card, so all of
            // them are **under every card**. A pool hung off a card would have to be either a
            // child — which draws over the card it is meant to light — or a sibling inserted
            // beside it, which draws over its neighbour, because a light worth seeing reaches
            // further than the eighteen units between two tiles (the tasks page's rule).
            var lights = UIKit.Node("Lights", nights);
            lights.anchorMin = new Vector2(0f, 1f);
            lights.anchorMax = new Vector2(1f, 1f);
            lights.pivot = new Vector2(.5f, 1f);
            UIKit.StretchTo(lights, 0f, 0f, 0f, 0f);

            for (int i = 0; i < _rungs; i++)
            {
                int row = i / PerRow;
                int col = i % PerRow;
                int inRow = Mathf.Min(PerRow, _rungs - row * PerRow);

                float x = (col - (inRow - 1) * .5f) * (TileW + TileGap);
                float ty = -(row * (TileH + TileGap) + TileH * .5f);

                Tile(nights, lights, _first + i, new Vector2(x, ty), i);
            }

            if (slack > 0f) return;

            // Invisible, but drags have to land on something: every Image this UI builds is
            // raycast-transparent, so without a catcher a long ladder could not be scrolled.
            var catcher = band.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            var scroll = band.gameObject.AddComponent<ScrollRect>();
            scroll.content = nights;
            scroll.viewport = band;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = .14f;
            scroll.inertia = true;
            scroll.decelerationRate = .04f;
            scroll.scrollSensitivity = 55f;
        }

        /// <summary>
        /// One night: which night it is, what it pays, and whether it has been taken.
        ///
        /// <para>
        /// Everything that changes with state is written by <see cref="Paint"/>; this builds
        /// the furniture once. The reward is a picture with an amount under it — a chest for a
        /// chest night, the currency's own glyph for a figure — because a player scanning the
        /// board is reading pictures, and the tier name under a chest is what tells a silver
        /// from a gold when the two are forty pixels apart.
        /// </para>
        /// </summary>
        void Tile(RectTransform parent, RectTransform lights, int night, Vector2 at, int index)
        {
            var entry = new NightTile { Night = night, Rung = _ladder.Rung(night) };
            _tiles.Add(entry);

            // The pool a waiting night stands in, built dark. See BuildBoard for why it is
            // not a child of the card.
            entry.Pool = UIKit.Img("Light_" + night, lights, Art.Glow(128, 1.35f), Pal.A(Pal.Sun, 0f),
                                   new Vector2(TileW + 130f, TileH + 120f), Top, at);

            // `Skins.Panel` rather than `Skins.Card`, and that one swap is most of the
            // rebuild. The kit's card is the darkest plate it cuts — right for a wide row on
            // the tasks page, where the row is full of content and the card is a backing — and
            // wrong for a small tile on a blue ground, where seven of them read as seven holes
            // punched in the page rather than as seven things. The panel is the light one.
            var card = UIKit.Img("N" + night, parent, Art.S("Ui/" + Skins.Panel), Color.white,
                                 new Vector2(TileW, TileH), Top, at);
            entry.Card = card;
            entry.Root = (RectTransform)card.transform;
            entry.Group = UIKit.Group(entry.Root);

            var halo = UIKit.Img("Halo", entry.Root, Art.Glow(128, 2f), Pal.A(Pal.Gold, 0f),
                                 new Vector2(TileW * 1.5f, TileW * 1.5f), Centre, new Vector2(0f, 6f));
            entry.Halo = (RectTransform)halo.transform;
            halo.transform.SetAsFirstSibling();

            // **The night rides a coloured chip, and the colour is the state.** It used to be
            // an inset trough, which is the kit's *darkest* plate sitting inside its second
            // darkest — two dark rectangles stacked, on a tile that then said nothing about
            // itself until you read the seal. A player scanning a board reads colour, and this
            // is the one element every tile has.
            entry.Chip = UIKit.Img("Chip", entry.Root, null, Color.white,
                                   new Vector2(TileW - 44f, 54f), Top, new Vector2(0f, -22f));

            entry.ChipText = UIKit.Shrinkable(
                UIKit.Titled("ChipText", entry.Root,
                             Loc.Format("ui.streak.day_n", night).ToUpperInvariant(),
                             25, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(TileW - 66f, 36f), Top,
                             new Vector2(0f, -22f + 54f * UIKit.PillFaceLift), 3f, 3f), 15);

            // The seat the reward stands in. The kit's inset well, which is what gives the
            // middle of the tile something to be about — a picture floating in the centre of a
            // plate is a picture nobody put anywhere.
            UIKit.Img("Seat", entry.Root, Art.S("Ui/" + Skins.Slot), Color.white,
                      new Vector2(SeatSize, SeatSize), Centre, new Vector2(0f, SeatY));

            UIKit.Img("Lamp", entry.Root, Art.Glow(128, 1.9f), Pal.A(Pal.Sun, .16f),
                      new Vector2(SeatSize + 24f, SeatSize + 24f), Centre, new Vector2(0f, SeatY));

            Reward(entry);

            // The seal a taken night wears, on the seat's own corner rather than over the
            // reward: the chip already says the night was kept, so this is the confirmation on
            // top of the colour rather than the signal itself.
            var seal = UIKit.Img("Seal", entry.Root, Art.S("Ui/seal_gold"), Color.white,
                                 new Vector2(66f, 66f), Centre,
                                 new Vector2(TileW * .29f, -TileW * .22f + 14f));
            seal.preserveAspect = true;

            var tick = UIKit.Img("Tick", seal.transform, Art.S("Ui/ic_check"), Pal.Cream,
                                 new Vector2(34f, 34f), Centre, Vector2.zero);
            tick.preserveAspect = true;

            entry.Seal = (RectTransform)seal.transform;
            entry.Seal.gameObject.SetActive(false);

            // The rim, on the card's own edge and over everything on it — the half of the
            // light that has to be a child, because a light drawn *behind* a card the kit cuts
            // opaque is a light with a hole in the middle of it.
            entry.Rim = UIKit.Img("Rim", entry.Root, Art.RoundOutline(30, 7f), Pal.A(Pal.Sun, 0f));
            UIKit.StretchTo((RectTransform)entry.Rim.transform, 0f, 0f, 0f, 0f);

            // The whole tile is the button. A small "collect" chip inside it would be a
            // smaller target for the same action, and there is nothing else on a night to tap
            // by mistake (the tasks page's rule).
            entry.Tap = UIKit.Button("Tap", entry.Root, Art.Pixel, new Vector2(TileW, TileH),
                                     Centre, Vector2.zero, () => Take(entry));
            entry.Tap.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            entry.Tap.ClickSfx = null;
            entry.Tap.PressScale = .95f;
            entry.Tap.gameObject.SetActive(false);

            entry.Root.localScale = Vector3.zero;
            Tween.Pop(entry.Root, 0f, .46f, .22f + index * .05f);
        }

        /// <summary>
        /// What the night pays, printed on its own face.
        ///
        /// A rung that pays nothing is not left blank: saying "the flame lights" is the
        /// difference between "this night is worth nothing" and "this night is where it
        /// starts".
        /// </summary>
        void Reward(NightTile entry)
        {
            var rung = entry.Rung;

            // The strip the amount stands on, at the foot of every tile. A figure printed
            // straight onto the plate floats; a figure on a trough is a readout, and it is what
            // gives the tile a bottom edge so the reward above it is standing on something
            // rather than drifting in a box.
            UIKit.Img("Well", entry.Root, Art.S("Ui/" + Skins.Trough), Color.white,
                      new Vector2(TileW - 44f, 58f), new Vector2(.5f, 0f), new Vector2(0f, 26f));

            if (rung.IsChest)
            {
                // <b>Sized and placed in *drawn* units, through `ChestPack`.</b> The closed
                // icon is frame nought of the opening reel, so its sprite carries the lid's
                // headroom — a box set straight from a height draws a chest two thirds of it
                // and floats it high, which is a tile whose reward is smaller than the gem on
                // the tile beside it for a reason nothing on the screen explains. The pack owns
                // that conversion and four screens now share it.
                float tall = RewardTall;
                var box = new Vector2(tall / ChestPack.Fill * ChestPack.Aspect, tall / ChestPack.Fill);

                var chest = UIKit.Img("Chest", entry.Root, Art.S(rung.Tier.Icon), Color.white,
                                      box, Centre, new Vector2(0f, SeatY + tall * ChestPack.Lift));
                chest.preserveAspect = true;
                entry.Icon = chest;

                entry.Amount = UIKit.Shrinkable(
                    UIKit.Titled("Amt", entry.Root, Loc.Get(rung.Tier.NameKey), 25, Pal.Cream,
                                 TextAnchor.MiddleCenter, new Vector2(TileW - 62f, 38f),
                                 new Vector2(.5f, 0f), new Vector2(0f, 26f), 3f, 3f), 14);
                return;
            }

            var drop = rung.AsDrop();

            if (!drop.IsValid)
            {
                var spark = UIKit.Img("Icon", entry.Root, Art.S("Ui/ic_star"), Pal.A(Pal.Cream, .92f),
                                      new Vector2(104f, 104f), Centre, new Vector2(0f, SeatY));
                spark.preserveAspect = true;
                entry.Icon = spark;

                entry.Amount = UIKit.Shrinkable(
                    UIKit.Titled("Amt", entry.Root, Loc.Get("ui.streak.rung_none"), 23, Pal.Cream,
                                 TextAnchor.MiddleCenter, new Vector2(TileW - 62f, 44f),
                                 new Vector2(.5f, 0f), new Vector2(0f, 26f), 3f, 3f), 13);
                return;
            }

            // Never tinted: every reward glyph carries its own colour. See RewardArt.
            var icon = UIKit.Img("Icon", entry.Root, RewardArt.Icon(drop.Kind, drop.Item), Color.white,
                                 new Vector2(RewardTall, RewardTall), Centre, new Vector2(0f, SeatY));
            icon.preserveAspect = true;
            entry.Icon = icon;

            // Credits have no still sprite — they are the spinning coin — so the glyph is
            // finished here rather than by `Icon`. Without this a credit night draws as a
            // white square, which is what an Image with no sprite actually is (invariant 7b).
            RewardArt.Glyph(icon, drop.Kind, 10f);

            entry.Amount = UIKit.Shrinkable(
                UIKit.Titled("Amt", entry.Root, RewardArt.Amount(drop), 36, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(TileW - 62f, 46f),
                             new Vector2(.5f, 0f), new Vector2(0f, 26f), 3f, 4f), 20);
        }

        // -------------------------------------------------------------- painting
        /// <summary>
        /// Writes the ledger onto the hero, the shield row and the board. No entrance and no
        /// rebuild: a night taken behind this page moves a bar, and a bar moving is what a
        /// repaint is for (<c>CRAFT.md</c>: Show animates, Refresh does not).
        /// </summary>
        void Repaint()
        {
            if (this == null || Content == null) return;

            _days = DailyStreak.Days;
            _pending = DailyStreak.Pending;
            _playedToday = DailyStreak.PlayedToday;

            if (_count && _days <= 0) _count.text = "—";

            if (_caption)
                _caption.text = Loc.Get(_days == 1 ? "ui.streak.day" : "ui.streak.days");

            // The lap, and the bar under it. Both measure the run of nights on the board
            // rather than the whole streak, which has no end.
            int done = Mathf.Clamp(_days - _first + 1, 0, _rungs);

            if (_lap) _lap.text = Loc.Format("ui.tasks.fraction", done, _rungs);

            bool full = done >= _rungs;
            if (_bar && _barFull != full)
            {
                var colour = full ? BarFull : BarOrange;
                if (_barFull == null) _bar.color = colour;
                else Tween.Tint(_bar, colour, .35f);
                _barFull = full;
            }

            if (_fill)
            {
                float target = (_track - 8f) * Mathf.Clamp01(done / (float)_rungs);
                Tween.KillChannel(_fill, "bar");
                float from = _fill.sizeDelta.x;
                Tween.Run(.6f, Ease.OutCubic,
                          t => { if (_fill) _fill.sizeDelta = new Vector2(Mathf.Lerp(from, target, t), BarH); },
                          _fill, "bar");
            }

            RefreshState();
            RefreshShield();

            foreach (var tile in _tiles) Paint(tile);

            // The footer takes height out of the board, so when the page stops (or starts)
            // having something to ask for, a repaint is not enough. Checked last, so the
            // words are right for the frame before the redraw lands.
            if (_asking != Asking) Rebuild();
        }

        /// <summary>
        /// The pill on the hero: what the streak is doing right now, in the order a player
        /// needs it.
        ///
        /// <para>
        /// A waiting reward first, because it is the only state with something to <em>do</em>.
        /// Then the protection, because a player who has paid for it should be told it is
        /// working every time they open the page. Then the clock, which is the only state that
        /// is urgent — and a protected streak never reaches it, which is the whole of what was
        /// bought.
        /// </para>
        /// </summary>
        void RefreshState()
        {
            if (!_state) return;

            string line;
            Color tint;

            if (_pending > 0)
            {
                line = _pending == 1
                    ? Loc.Get("ui.streak.waiting_one")
                    : Loc.Format("ui.streak.waiting_many", _pending);
                tint = Pal.Gold;
            }
            else if (DailyStreak.AtRisk)
            {
                line = Loc.Format("ui.streak.explain_risk_clock",
                                  Profile.Countdown(DailyStreak.SecondsUntilLost));
                tint = new Color(1f, .62f, .50f);
            }
            else if (DailyStreak.IsProtected)
            {
                int left = DailyStreak.ShieldDaysLeft;
                line = left == 1
                    ? Loc.Get("ui.streak.shield_left_one")
                    : Loc.Format("ui.streak.shield_left_many", left);
                tint = Pal.Mint;
            }
            else if (_days <= 0)
            {
                line = Loc.Get("ui.streak.explain_none");
                tint = Pal.A(Pal.Cream, .88f);
            }
            else
            {
                line = Loc.Get("ui.streak.done_today");
                tint = Pal.Mint;
            }

            _state.text = line;
            _state.color = tint;

            // Re-fitted on every write, because the size was chosen for the words that were in
            // it at the time: "2 nights are waiting" and "23:59:07" are different lengths, and
            // this pill spends its life changing between them.
            UIKit.OneLineLabel(_state, ClockRoom, 14);
        }

        /// <summary>
        /// The shield row: what it costs, or how long is left of the one that is running.
        ///
        /// The price is content and is known offline, which is the whole of what a gem price
        /// buys over a real-money one — the button never has to say "connecting", and there is
        /// no state in which this page can draw an offer it cannot sell.
        /// </summary>
        void RefreshShield()
        {
            if (_shieldBtn == null) return;

            bool on = DailyStreak.IsProtected;
            bool anything = _days > 0;

            // The glyph changes rather than going away. `UIKit.FitLabel` centres the caption
            // and the glyph as one block on `Icon != null` — it does not read `enabled` — so
            // hiding it would leave the caption sitting left of centre with a gem's worth of
            // gap beside it. A tick is the right mark for the state anyway.
            if (_shieldBtn.Icon)
                _shieldBtn.Icon.sprite = Art.S(on ? "Ui/ic_check" : "Ui/ic_gem");

            _shieldBtn.SetCaption(on
                ? Loc.Format("ui.streak.shield_days_left", DailyStreak.ShieldDaysLeft)
                : Loc.Format("ui.streak.shield_price", Compact.Number(DailyStreak.ShieldGems)));

            _shieldBtn.Interactable = !on && anything;

            if (_shieldHint)
                _shieldHint.text = on
                    ? Loc.Format("ui.streak.shield_on_hint", DailyStreak.ShieldDays)
                    : anything ? Loc.Format("ui.streak.shield_hint", DailyStreak.ShieldDays)
                    : Loc.Get("ui.streak.shield_none");

            if (_shieldName) _shieldName.color = on ? Pal.Mint : Pal.Cream;

            if (_shieldCrest)
                _shieldCrest.color = on ? Color.white : new Color(.82f, .86f, .92f, 1f);
        }

        /// <summary>How a night reads, from the stored dates and nothing else.</summary>
        Night StateOf(int night)
        {
            if (DailyStreak.IsWaiting(night)) return Night.Waiting;
            if (night <= _days) return Night.Kept;
            if (!_playedToday && night == _days + 1) return Night.Tonight;
            return Night.Ahead;
        }

        void Paint(NightTile tile)
        {
            if (tile == null || !tile.Root) return;

            var state = StateOf(tile.Night);
            bool waiting = state == Night.Waiting;
            bool kept = state == Night.Kept;

            if (tile.Group) tile.Group.alpha = state == Night.Ahead ? .72f : 1f;
            if (tile.Seal) tile.Seal.gameObject.SetActive(kept);

            // Every waiting tile is a button, and every one of them takes the *earliest*
            // waiting night — see Take. A tap that did nothing would be a broken button
            // (invariant 16o), and a tap that quietly reached past an older night would be a
            // reward stranded behind a newer one.
            if (tile.Tap) tile.Tap.gameObject.SetActive(waiting);

            bool lit = waiting && tile.Night == DailyStreak.FirstPending;

            // The chip is the state, and it is the one thing on a tile that is coloured. Four
            // answers rather than five: a night waiting its turn wears the same gold as the one
            // on offer, because both are the player's — what separates them is the light, which
            // is the thing that says *this* one is tappable now.
            if (tile.Chip)
            {
                tile.Chip.sprite = Art.S("Ui/" + ChipSkin(state));

                // `Image.color` is a multiply (invariant 37l), so a chip is never tinted: the
                // kit cuts each of these at the colour it means, and lifting one toward white
                // would only wash it out.
                tile.Chip.color = Color.white;
                tile.Chip.type = tile.Chip.sprite != null && tile.Chip.sprite.border != Vector4.zero
                               ? Image.Type.Sliced : Image.Type.Simple;
            }

            if (tile.ChipText)
                tile.ChipText.color = state == Night.Ahead ? Pal.A(Pal.Cream, .88f) : Pal.Cream;

            // A kept night recedes without going grey: the chip is already green, so the plate
            // and the reward only have to stop competing with the ones still to come.
            if (tile.Card) tile.Card.color = kept ? new Color(.88f, .92f, .98f, 1f) : Color.white;
            if (tile.Icon) tile.Icon.color = kept ? new Color(.82f, .86f, .92f, 1f) : Color.white;
            if (tile.Amount) tile.Amount.color = kept ? Pal.A(Pal.Cream, .72f) : Pal.Cream;

            if (lit && !tile.Lit)
            {
                if (tile.Halo) Tween.Tint(tile.Halo.GetComponent<Image>(), Pal.A(Pal.Gold, .55f), .4f);
                if (tile.Icon) Tween.Bob((RectTransform)tile.Icon.transform, 7f, 1.5f, tile.Night * .4f);
                Sheen.Attach(tile.Root, 2.8f);
                Shine(tile, true);
                tile.Lit = true;
            }
            else if (!lit && tile.Lit)
            {
                if (tile.Halo) Tween.Tint(tile.Halo.GetComponent<Image>(), Pal.A(Pal.Gold, 0f), .3f);
                if (tile.Icon) Tween.KillChannel(tile.Icon.transform, "bob");
                Shine(tile, false);
                tile.Lit = false;
            }

        }

        /// <summary>
        /// Which of the kit's jelly plates a night's chip wears.
        ///
        /// <b>Gold means "this is yours", aqua means "this is tonight's", green means "taken"
        /// and the kit's dark square means "not yet".</b> Each is a literal rather than a name
        /// assembled from the state, which is what keeps them inside the four `artnames.py`
        /// can see in this method — an address a gate cannot read is an address that draws a
        /// white rectangle the day somebody renames a file (invariant 7b).
        /// </summary>
        static string ChipSkin(Night state)
        {
            switch (state)
            {
                case Night.Kept: return "sq_green";
                case Night.Waiting: return "sq_orange";
                case Night.Tonight: return "sq_aqua";
                default: return Skins.Resting;
            }
        }

        /// <summary>
        /// The light a waiting night stands in: a warm pool that reaches past the card and a
        /// bright rim on its edge, breathing together on one tween.
        ///
        /// <para>
        /// <b>Two pieces rather than one, because a card is opaque.</b> A glow behind the kit's
        /// navy card is a glow with a card-shaped hole punched out of the middle of it, and a
        /// glow in front of it washes out everything printed on the tile — so the light outside
        /// the card is a pool and the light on the card is its edge. Together they read as one
        /// thing lit from behind (the tasks page's finding).
        /// </para>
        /// <para>
        /// It breathes rather than flashing, which matters less here than on a list of six —
        /// only one tile is ever lit — but the rule is the rule: a flash is for the one thing
        /// that has just happened, never for a state (invariant 37h).
        /// </para>
        /// </summary>
        static void Shine(NightTile tile, bool on)
        {
            if (tile.Pool) Tween.KillChannel(tile.Pool.transform, "holy");
            if (tile.Rim) Tween.KillChannel(tile.Rim.transform, "holy");

            if (!on)
            {
                if (tile.Pool) Tween.Tint(tile.Pool, Pal.A(Pal.Sun, 0f), .3f);
                if (tile.Rim) Tween.Tint(tile.Rim, Pal.A(Pal.Sun, 0f), .3f);
                return;
            }

            Tween.Run(1.8f, Ease.InOutSine, t =>
            {
                if (tile.Pool) tile.Pool.color = Pal.A(Pal.Sun, Mathf.Lerp(.42f, .80f, t));
                if (tile.Rim) tile.Rim.color = Pal.A(Pal.Radiance, Mathf.Lerp(.55f, 1f, t));
            }, tile.Pool, "holy").Loop(-1, true);
        }

        // ------------------------------------------------------------ collecting
        /// <summary>
        /// Takes a night.
        ///
        /// <para>
        /// <b>Whichever tile was tapped, the night taken is the earliest one waiting.</b> The
        /// collected floor is a floor — taking night five would take four with it — so only
        /// the oldest can be handed over, and a tap on a newer one is redirected rather than
        /// swallowed: a button that does nothing is a broken button (16o), and the player gets
        /// every night they are owed by tapping the same number of times either way.
        /// </para>
        /// <para>
        /// The two shapes end differently and deliberately so. A chest night opens the
        /// ceremony every chest in this game opens — the grant happens inside it, so a player
        /// who kills the app mid-reel has still collected the night. A currency night throws
        /// its tokens straight at the wallet pills, because a chest panel wrapped around a
        /// number is a lid with nothing under it.
        /// </para>
        /// </summary>
        void Take(NightTile tapped)
        {
            if (_collecting || tapped == null || Flow.HasModal) return;

            int night = DailyStreak.FirstPending;
            if (night <= 0) return;

            var tile = Find(night) ?? tapped;
            var rung = _ladder.Rung(night);

            if (rung.IsChest && !DailyStreak.CanClaimChests)
            {
                Scenery.Toast(Content, Loc.Get("ui.chest.needs_connection"), Pal.Rose, 3f);
                return;
            }

            if (!DailyStreak.CanCollect(night)) return;

            _collecting = true;

            Audio.Sfx("collect", .6f);
            Tween.KillChannel(tile.Root, "breathe");
            Tween.Punch(tile.Root, .16f, .34f);

            if (rung.IsChest) TakeChest(tile, night);
            else TakeCurrency(tile, night, rung);
        }

        NightTile Find(int night)
        {
            foreach (var tile in _tiles) if (tile.Night == night) return tile;
            return null;
        }

        /// <summary>
        /// A chest night. The overlay claims it — the grant happens at the start of the
        /// ceremony, not here — so the page is only ever asked whether the night is still
        /// waiting, and a second device that got there first is answered by the overlay
        /// closing itself.
        /// </summary>
        void TakeChest(NightTile tile, int night)
        {
            if (tile.Icon) Burst.Sparks(tile.Icon.transform, Vector2.zero, Pal.Gold, 18, 320f, 26f, .6f);

            Flow.Modal<ChestOverlay>(v => v.Claim = ChestClaim.ForStreakNight(night));

            // The ledger raised Changed inside the overlay's Build; the repaint was held back
            // by _collecting so the tile did not turn grey under the ceremony. Let it through
            // now, while the scrim covers it.
            _collecting = false;

            if (DailyStreak.BoardFirstNight != _first) Rebuild();
            else Repaint();
        }

        /// <summary>
        /// A currency night: the grant first and the animation reporting it, exactly as the
        /// chest overlay does. A player who kills the app mid-flight has still collected the
        /// night, and a reward that depended on an animation finishing would be a reward a
        /// slow phone could lose.
        /// </summary>
        void TakeCurrency(NightTile tile, int night, StreakRung rung)
        {
            // Snapshotted *before* the grant, which is what `Begin` insists on: a snapshot
            // taken afterwards would have to be derived, and deriving it is wrong the moment a
            // rule can clamp what was granted.
            var flight = RewardFlight.Begin();

            if (!DailyStreak.TryCollect(night, out var drops))
            {
                _collecting = false;
                Repaint();
                return;
            }

            var source = tile.Icon ? (RectTransform)tile.Icon.transform : tile.Root;
            var tint = RewardArt.Tint(rung.Kind, string.Empty);

            Burst.Sparks(tile.Root, Vector2.zero, tint, 22, 360f, 28f, .7f);
            Flow.Flash(Pal.A(tint, 1f), .08f, .32f);

            // The flame answers. It is the thing the page is about and the thing the eye is
            // already on, so a night being taken has to move it — otherwise the only feedback
            // is a token landing on a pill at the top of the screen.
            if (_flame) Tween.Punch(_flame, .14f, .34f);
            if (_heroHost) Tween.Punch(_heroHost, .06f, .30f);

            bool flying = false;
            foreach (var drop in drops) flying |= flight.Add(drop, source);

            // Hidden rather than destroyed: the tile keeps its layout, and a reward that
            // vanishes from the face it was printed on is the point.
            if (tile.Icon) tile.Icon.enabled = false;

            Stamp(tile);

            if (!flying)
            {
                Finish();
                return;
            }

            flight.Play(Content, Finish);

            void Finish()
            {
                _collecting = false;
                if (this == null || Content == null) return;

                if (DailyStreak.BoardFirstNight != _first) Rebuild();
                else Repaint();
            }
        }

        /// <summary>
        /// The seal, stamped rather than faded in. A seal that arrives from above and
        /// overshoots reads as something being pressed onto the tile; one that appears reads
        /// as a sprite being switched on.
        /// </summary>
        void Stamp(NightTile tile)
        {
            if (!tile.Seal) return;

            tile.Seal.gameObject.SetActive(true);
            tile.Seal.localScale = Vector3.one * 2.6f;
            Tween.Scale(tile.Seal, 1f, .34f, Ease.OutBack)
                 .OnDone(() => { if (tile.Root) Tween.Punch(tile.Root, .10f, .26f); });
        }

        // --------------------------------------------------------------- footer
        /// <summary>
        /// Whether the page has something to ask for.
        ///
        /// A night already kept does not: an instruction to do a thing that has been done is
        /// worse than no button. A <em>protected</em> streak does not either, which is the
        /// whole of what the shield was bought for — a page that sold somebody a week away
        /// and then spent that week telling them to play would be selling one thing and
        /// saying another.
        /// </summary>
        bool Asking => _days <= 0 || DailyStreak.AtRisk;

        /// <summary>
        /// How much room the footer needs above the nav bar, which is what the board's band
        /// is measured against. Derived rather than written twice, so the board cannot
        /// overlap the button on the one state that has one.
        /// </summary>
        float FooterTop => NavBar.Height + 20f + (_asking ? 176f : 0f);

        /// <summary>
        /// The way out, on the one page whose subject is a thing that runs out.
        ///
        /// It is on this page rather than left to the nav bar because the page that says the
        /// flame is going out is the page that should carry the thing that stops it — a
        /// player who has to find their own way back to the map has been told about a
        /// problem and handed no answer.
        /// </summary>
        void BuildFooter()
        {
            if (!_asking) return;

            var play = UIKit.TextButton("Play", Safe, Skins.Affirm, Loc.Get("ui.streak.cta"), 44,
                                        new Vector2(560f, 132f), new Vector2(.5f, 0f),
                                        new Vector2(0f, NavBar.Height + 32f),
                                        () => Flow.Go<LevelsScreen>());
            UIKit.Halo(play.transform, Pal.Mint, 640f, .24f);

            play.transform.localScale = Vector3.zero;
            Tween.Pop(play.transform, 0f, .55f, .34f).OnDone(() =>
            {
                if (!play) return;
                play.Rehome();
                Sheen.Attach((RectTransform)play.transform, 3.4f);
            });
        }

        public override bool OnBack() { Flow.Go<HomeScreen>(); return true; }
    }
}
