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
    /// is a window of days the streak survives without being played — which is why the row
    /// that sells it turns into a row that reports it.
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
        /// One night is one <b>row</b>, the full width of the page.
        ///
        /// <para>
        /// <b>It was a four-across grid of tiles and that was the mistake.</b> A tile is a
        /// column of three things stacked in 240 units — a night, a picture, an amount — so
        /// every one of them is cramped, the reward is the size of a thumbnail, and what the
        /// night actually pays has to be squeezed into two words. A row is 1000 units with the
        /// reward on the left, a sentence in the middle and the answer on the right, which is
        /// the shape the tasks page and the season ladder already use for exactly this reason:
        /// a list of rewards is a <em>list</em>.
        /// </para>
        /// <para>
        /// It costs a scroll. Seven rows is taller than the band under the heading on every
        /// phone, so the board opens scrolled to the night that can be taken
        /// (<see cref="FocusOnPending"/>) rather than to the top — a page whose one action is
        /// below the fold is a page with no action on it.
        /// </para>
        /// <para>
        /// <b>Taller again after the owner played it</b>, and for the season ladder's reason
        /// rather than a second one: the row is a picture of a prize, and at 156 with an 88
        /// reward in it the prize was the smallest thing on the card. The page already scrolls,
        /// so the change buys a bigger picture and costs nothing but how many rows a phone shows
        /// at once. The reward grows with it — see <see cref="RewardTall"/>.
        /// </para>
        /// </summary>
        const float RowH = 184f;
        const float RowGap = 12f;

        /// <summary>
        /// The well a night's reward stands in at the left of its row, and how big the reward
        /// is drawn in it.
        ///
        /// <para>
        /// <b><see cref="RewardTall"/> is a <em>drawn</em> height</b>, which is the only unit a
        /// chest and a gem can share: a chest's icon carries the lid's headroom (see
        /// <see cref="ChestPack"/>), so a box set straight from a height would draw the chest
        /// two thirds the size of the gem on the row above it and float it high. The pack
        /// converts once, here as on the hub and the tasks page.
        /// </para>
        /// </summary>
        const float SeatSize = 148f, SeatX = 114f, RewardTall = 110f;

        /// <summary>Where the sentence starts, and how much of the row it may have.</summary>
        const float TextX = 206f, TextW = 450f;

        /// <summary>
        /// The radius the row's plate is rounded to.
        ///
        /// Written once because three things have to agree about it: the rim drawn on the
        /// card's edge, the mask that keeps a waiting row's light inside that edge
        /// (<see cref="Aura"/>), and the render mirror. A mask a few units off the shape it is
        /// clipping to is light outside the plate, which is the fault it exists to stop.
        /// </summary>
        const int CardRound = 30;

        /// <summary>
        /// The <b>COLLECT</b> key at the right end of a row, and the pill that stands in the
        /// same place when there is nothing to collect yet.
        ///
        /// <para>
        /// <b>One width, because they are one answer in two moods</b> — the right end carries
        /// exactly one of them at a time, so two footprints would be the row changing shape
        /// according to what it had to say. The pill was the narrower of the two by sixteen
        /// units for no reason anybody wrote down, and those sixteen units are what the longest
        /// line it can say needed.
        /// </para>
        /// </summary>
        const float KeyW = 212f, KeyH = 84f;

        /// <summary>
        /// The room <see cref="Scenery.Pill"/> really leaves its words: twenty units off the
        /// left of the plate and sixteen off the right.
        ///
        /// <para>
        /// <b>It is written down because the line is re-fitted on every repaint and the two have
        /// to agree</b> — a fitter measuring against a width the pill does not have is a fitter
        /// that lets the text spill anyway, which is the hero clock's rule (<see cref="ClockRoom"/>)
        /// said about the other pill on this page. This one spends its life changing between
        /// <c>TONIGHT</c>, <c>TOMORROW NIGHT</c>, <c>IN 5 NIGHTS</c> and <c>CONNECT ONCE</c>, and
        /// the longest of those is half again the shortest: built at 23 for one of them, it was
        /// drawn straight out through the side of the plate for another, because a <c>Text</c>
        /// that overflows is not clipped and nothing says so.
        /// </para>
        /// <para>
        /// The floor is the headroom a translation gets. English settles the longest line at 17
        /// of a possible 23, so a language needing a fifth more room than ours still fits.
        /// </para>
        /// </summary>
        const float MarkH = 62f;
        const int MarkType = 23, MarkLeast = 14;
        const float MarkRoom = KeyW - 20f - 16f;

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
        /// What <see cref="Scenery.Pill"/> really leaves its words at 340 wide: the glyph's
        /// lane comes off the height and sixteen units come off the right. Written here
        /// rather than at the call site because the clock is re-fitted on every tick and the
        /// two have to agree — a fitter shrinking against a width the pill does not have is a
        /// fitter that lets the text spill anyway (the season's rule).
        ///
        /// <para>
        /// <see cref="ClockType"/> is beside it because a fit that is re-run has to start from
        /// the size the pill was designed at, not from the size the last line left it at — this
        /// pill says "a night is waiting", "2 nights are waiting", a countdown and "safe for 4
        /// more days", and each long one filed the type down a point that no short one gave
        /// back. See <see cref="UIKit.OneLineLabel"/>.
        /// </para>
        /// </summary>
        const float ClockRoom = 340f - 54f * .82f - 16f;
        const int ClockType = 23, ClockLeast = 14;

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

        /// <summary>The board's list, its own height and the band it scrolls inside.</summary>
        RectTransform _nights;
        ScrollRect _scroll;
        float _boardH, _bandH;

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

            /// <summary>The well the reward stands in. What the holy ring is hung on.</summary>
            public RectTransform Seat;

            /// <summary>"NIGHT 3", and under it what the night pays in words.</summary>
            public Text Title, Sub;

            /// <summary>The three answers the right end of a row can carry, one at a time.</summary>
            public RectTransform Collect, Mark, Seal;

            /// <summary>
            /// The turning fan, the halo and the ring that opens out of it — the whole of the
            /// light around a reward that can be taken.
            ///
            /// Built when a row becomes the one on offer and destroyed when it stops being it,
            /// rather than built for every row and hidden: these are three looping tweens each,
            /// and seven rows' worth of them running behind a scroll is a page that costs
            /// something to look at.
            /// </summary>
            public RectTransform Aura;

            public Image Pool;
            public Image Rim;
            public Btn Tap;
            public StreakRung Rung;
            public CanvasGroup Group;

            /// <summary>
            /// Whether the light is already running on this tile.
            ///
            /// Held rather than re-derived, because <see cref="Shine"/>, the ring and the bob
            /// are looping tweens: started on every repaint they would restart on every counter
            /// that moves, which is a row that jumps each time anything else on the page
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

            float y = 22f;
            y = BuildHeader(y);
            y = BuildHero(y);
            y = BuildShield(y);
            y = BuildHeading(y);
            BuildBoard(y);

            NavBar.Build(Content, NavBar.Tab.Home);
            HoldReels();

            Repaint();

            // After the paint, because the row that gets the focus is the one the paint just
            // lit, and before the frame ends, so the board is never seen at the top first.
            FocusOnPending();
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
            if (_collecting) return;
            Settle();
        }

        /// <summary>
        /// Whether the page is drawing a state the ledger has left behind — the one question
        /// that decides between a repaint and a redraw, asked from every place that has to
        /// decide it.
        ///
        /// <para>
        /// <b>It is one predicate because it was three, and they disagreed.</b> A night being
        /// taken asked only whether the lap had moved; the clock asked only whether the day
        /// had; and neither asked whether the ladder underneath them was still the same
        /// object. Every combination they each missed is a page that keeps drawing the old
        /// lap — the board is a <em>window</em> onto one lap of the ladder, so when the window
        /// moves the page is a different set of rows rather than different words on the same
        /// ones, and no repaint can get there.
        /// </para>
        /// <para>
        /// The four facts are the four the build reads: which lap is shown, how long the
        /// streak is, whether today is already kept, and which ladder is being drawn. The
        /// ladder is compared by <em>identity</em> rather than by length, because a content
        /// push can hand back a table of the same length paying different rungs.
        /// </para>
        /// </summary>
        bool Stale
            => DailyStreak.BoardFirstNight != _first
            || DailyStreak.Days != _days
            || DailyStreak.PlayedToday != _playedToday
            || !ReferenceEquals(DailyStreak.Ladder, _ladder);

        /// <summary>
        /// Brings the page up to date by whichever of the two means is honest, and is the only
        /// way anything here answers a change.
        ///
        /// <para>
        /// Every path that finishes a night — a chest ceremony, a currency flight, a flight
        /// that had nothing to throw — ends here rather than making the same choice for
        /// itself. That is what makes the lap roll over <b>on the night it rolls over on</b>:
        /// taking night seven while the streak stands at eight moves the window to nights
        /// 8–14, and the page that was showing 1–7 has to become a different page.
        /// </para>
        /// </summary>
        void Settle()
        {
            if (this == null || Content == null) return;

            if (Stale) Rebuild();
            else Repaint();
        }

        /// <summary>
        /// Draws the page again from scratch.
        ///
        /// <para>
        /// <see cref="View.ClearContent"/> empties the page and drops the base class's own
        /// handle into it; what is left here is this screen's handles. They are cleared rather
        /// than left to <c>Build</c> to overwrite because not every one of them is written on
        /// every path — the shield row builds nothing when the ladder sells no shield — so a
        /// field left alone is a field still pointing at a destroyed widget that a repaint
        /// will happily write to.
        /// </para>
        /// </summary>
        void Rebuild()
        {
            if (Content == null) return;

            ClearContent();

            _count = _caption = _state = _lap = _shieldHint = _shieldName = null;
            _bar = _shieldCrest = null;
            _fill = _flame = _heroHost = null;
            _shieldBtn = null;
            _nights = null;
            _scroll = null;
            _tiles.Clear();

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

            if (Stale)
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
                Scenery.Pill(plate.transform, string.Empty, ClockType, new Vector2(ClockW, ClockH),
                             new Vector2(1f, 1f),
                             UIKit.Corner(new Vector2(ClockW, ClockH), new Vector2(1f, 1f), 28f, 20f),
                             new Color(.05f, .09f, .18f, .78f), "ic_streak"),
                ClockRoom, ClockType, ClockLeast);

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
        /// The board's heading, pinned above it rather than scrolling with it.
        ///
        /// <b>It used to carry a line saying the rows could be tapped, and does not need to
        /// any more.</b> That hint existed because a tile was a night, a picture and an amount
        /// with nothing on it that looked like a control; a row ends in a key that says
        /// <b>COLLECT</b>, which is the same sentence said where the tap actually is. Two of
        /// them is one too many.
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

            return y + HeadingH;
        }

        // ---------------------------------------------------------------- board
        /// <summary>
        /// One lap of the ladder, one row a night.
        ///
        /// <para>
        /// The list always scrolls, which is a change from the grid it replaces: seven rows is
        /// taller than the band under the heading on every phone this game runs on, and a
        /// ladder is content — <see cref="StreakRules.MaxRungs"/> allows thirty — so a board
        /// sized to fit the shipped seven would be a code change waiting on a content change
        /// (invariant 4). What the grid bought was "no scroll" and what it cost was every row
        /// being a thumbnail; a list of rewards is a list.
        /// </para>
        /// <para>
        /// So the one thing that has to be handled is the fold, and it is handled by
        /// <see cref="FocusOnPending"/>: the board opens on the night that can be taken rather
        /// than on night one.
        /// </para>
        /// </summary>
        void BuildBoard(float top)
        {
            float bottom = BoardFoot;

            var band = UIKit.Node("Board", Safe);
            UIKit.StretchTo(band, 0f, bottom, 0f, top);
            band.gameObject.AddComponent<RectMask2D>();

            _bandH = Mathf.Max(0f, Flow.Size.y - top - bottom);
            _boardH = _rungs * (RowH + RowGap) - RowGap;

            var nights = UIKit.Node("Nights", band);
            nights.anchorMin = new Vector2(0f, 1f);
            nights.anchorMax = new Vector2(1f, 1f);
            nights.pivot = new Vector2(.5f, 1f);
            nights.sizeDelta = new Vector2(0f, _boardH);
            nights.anchoredPosition = Vector2.zero;
            _nights = nights;

            // Every waiting row's pool of light lives here, built before any card, so all of
            // them are **under every card**. A pool hung off a card would have to be either a
            // child — which draws over the card it is meant to light — or a sibling inserted
            // beside it, which draws over the row above, because a light worth seeing reaches
            // further than the twelve units between two rows (the tasks page's rule).
            var lights = UIKit.Node("Lights", nights);
            lights.anchorMin = new Vector2(0f, 1f);
            lights.anchorMax = new Vector2(1f, 1f);
            lights.pivot = new Vector2(.5f, 1f);
            UIKit.StretchTo(lights, 0f, 0f, 0f, 0f);

            for (int i = 0; i < _rungs; i++)
                Row(nights, lights, _first + i, i * (RowH + RowGap), i);

            // Invisible, but drags have to land on something: every Image this UI builds is
            // raycast-transparent, so without a catcher the list could not be scrolled.
            var catcher = band.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            _scroll = band.gameObject.AddComponent<ScrollRect>();
            _scroll.content = nights;
            _scroll.viewport = band;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.elasticity = .14f;
            _scroll.inertia = true;
            _scroll.decelerationRate = .04f;
            _scroll.scrollSensitivity = 55f;
        }

        /// <summary>
        /// Opens the board on the night that can be taken.
        ///
        /// <para>
        /// A list taller than its band starts at the top, and the top of this one is the
        /// oldest night — which on any streak past its third day is a row that has already
        /// been paid. The one row with something to do would then be below the fold, which
        /// makes a page whose whole point is that one tap look like a page with nothing on it.
        /// </para>
        /// <para>
        /// Centred in the band rather than scrolled to the top of the viewport, so the rows
        /// either side are visible: a row alone at the top of a list reads as the end of the
        /// list. Clamped at both ends, because a content position past either is a list that
        /// springs back the first time it is touched.
        /// </para>
        /// </summary>
        void FocusOnPending()
        {
            if (_nights == null || _boardH <= _bandH) return;

            int pending = DailyStreak.FirstPending;
            if (pending <= 0) return;

            int index = pending - _first;
            if (index < 0 || index >= _rungs) return;

            float rowTop = index * (RowH + RowGap);
            float want = Mathf.Clamp(rowTop - (_bandH - RowH) * .5f, 0f, _boardH - _bandH);

            _nights.anchoredPosition = new Vector2(0f, want);
        }

        /// <summary>
        /// One night: the reward in a well at the left, what it is in the middle, and what to
        /// do about it at the right.
        ///
        /// <para>
        /// Everything that changes with state is written by <see cref="Paint"/>; this builds
        /// the furniture once. The three answers the right end can carry — a <b>COLLECT</b>
        /// key, a seal, or the night it will be earned on — are all built and only one is ever
        /// shown, which is what lets a repaint be a repaint (<c>CRAFT.md</c>: Show animates,
        /// Refresh does not).
        /// </para>
        /// </summary>
        void Row(RectTransform parent, RectTransform lights, int night, float top, int index)
        {
            var entry = new NightTile { Night = night, Rung = _ladder.Rung(night) };
            _tiles.Add(entry);

            float cy = -(top + RowH * .5f);

            // The pool a waiting row stands in, built dark. See BuildBoard for why it is not a
            // child of the card.
            entry.Pool = UIKit.Img("Light_" + night, lights, Art.Glow(128, 1.35f), Pal.A(Pal.Sun, 0f),
                                   new Vector2(Width + 150f, RowH + 130f), Top, new Vector2(0f, cy));

            // `Skins.PlateNavy` and not `Skins.Panel`, which is the opposite of what a *tile*
            // wanted: a plate reads as a hole at 240 square on a blue ground and reads as a row
            // at 1000 wide with a picture and a sentence on it. The tasks page and the season
            // ladder are the same plate for the same reason — one reward row means one thing
            // across this game, so all three are re-cut by one name (invariant 44).
            var card = UIKit.Img("N" + night, parent, Art.S("Ui/" + Skins.PlateNavy), Color.white,
                                 new Vector2(Width, RowH), Top, new Vector2(0f, cy));
            entry.Card = card;
            entry.Root = (RectTransform)card.transform;
            entry.Group = UIKit.Group(entry.Root);

            // The well, and the reward standing in it.
            var seat = UIKit.Img("Seat", entry.Root, Art.S("Ui/" + Skins.Slot), Color.white,
                                 new Vector2(SeatSize, SeatSize), Left, new Vector2(SeatX, 0f));
            entry.Seat = (RectTransform)seat.transform;

            Reward(entry);

            entry.Title = UIKit.Shrinkable(
                UIKit.Titled("Title", entry.Root, Loc.Format("ui.streak.day_n", night).ToUpperInvariant(),
                             31, Pal.Cream, TextAnchor.MiddleLeft, new Vector2(TextW, 42f), Left,
                             new Vector2(TextX + TextW * .5f, 30f), 3f, 3f), 18);

            entry.Sub = UIKit.Shrinkable(
                UIKit.Titled("Sub", entry.Root, Says(entry.Rung), 25, Pal.A(Pal.Cream, .84f),
                             TextAnchor.MiddleLeft, new Vector2(TextW, 36f), Left,
                             new Vector2(TextX + TextW * .5f, -26f), 3f, 3f), 15);

            // --- the right end, three answers and one of them showing
            //
            // **The key says COLLECT, which is the whole of what was missing.** The row was
            // tappable before and nothing said so: a glowing reward is an invitation and a word
            // is an instruction, and on a page whose one action is this tap the instruction is
            // worth the eighty units it costs.
            var collect = UIKit.Img("Collect", entry.Root, Art.S("Ui/" + Skins.Affirm), Color.white,
                                    new Vector2(KeyW, KeyH), Right, new Vector2(-130f, 0f));

            UIKit.Shrinkable(
                UIKit.Titled("CollectText", collect.transform,
                             Loc.Get("ui.streak.collect").ToUpperInvariant(), 34, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(KeyW - 36f, 52f), Centre,
                             new Vector2(0f, KeyH * UIKit.PillFaceLift), 4f, 4f), 20);

            entry.Collect = (RectTransform)collect.transform;
            entry.Collect.gameObject.SetActive(false);

            // What a night still ahead says: the day it lands on, quietly, so a row that can do
            // nothing still answers the question a player is asking of it.
            entry.Mark = (RectTransform)Scenery.Pill(
                entry.Root, string.Empty, MarkType, new Vector2(KeyW, MarkH), Right,
                new Vector2(-130f, 0f), new Color(.05f, .09f, .18f, .70f)).transform.parent;
            entry.Mark.gameObject.SetActive(false);

            var seal = UIKit.Img("Seal", entry.Root, Art.S("Ui/seal_gold"), Color.white,
                                 new Vector2(84f, 84f), Right, new Vector2(-146f, 0f));
            seal.preserveAspect = true;

            var tick = UIKit.Img("Tick", seal.transform, Art.S("Ui/ic_check"), Pal.Cream,
                                 new Vector2(44f, 44f), Centre, Vector2.zero);
            tick.preserveAspect = true;

            entry.Seal = (RectTransform)seal.transform;
            entry.Seal.gameObject.SetActive(false);

            // The rim, on the card's own edge and over everything on it — the half of the
            // light that has to be a child, because a light drawn *behind* a card the kit cuts
            // opaque is a light with a hole in the middle of it.
            entry.Rim = UIKit.Img("Rim", entry.Root, Art.RoundOutline(CardRound, 7f), Pal.A(Pal.Sun, 0f));
            UIKit.StretchTo((RectTransform)entry.Rim.transform, 0f, 0f, 0f, 0f);

            // The whole row is the button, and the COLLECT key is a label on it. A key that was
            // the only target would be a smaller target for the same action, and there is
            // nothing else on a row to tap by mistake (the tasks page's rule).
            entry.Tap = UIKit.Button("Tap", entry.Root, Art.Pixel, new Vector2(Width, RowH),
                                     Centre, Vector2.zero, () => Take(entry));
            entry.Tap.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            entry.Tap.ClickSfx = null;
            entry.Tap.PressScale = .98f;
            entry.Tap.gameObject.SetActive(false);

            entry.Root.localScale = Vector3.zero;
            Tween.Pop(entry.Root, 0f, .46f, .20f + index * .04f);
        }

        /// <summary>
        /// What a night pays, in words. The line under the night's own number.
        ///
        /// A chest names itself — "Royal Chest" is the whole answer, and it is the same name
        /// the ceremony puts on its ribbon — where a figure needs its amount and its unit.
        /// This is the room a row buys over a tile: the grid had two words to say it in.
        /// </summary>
        static string Says(StreakRung rung)
        {
            if (rung.IsChest) return Loc.Get(rung.Tier.NameKey);

            var drop = rung.AsDrop();
            return drop.IsValid
                ? RewardArt.Amount(drop) + " " + RewardArt.Name(drop.Kind, drop.Item)
                : Loc.Get("ui.streak.rung_none");
        }

        /// <summary>
        /// The reward itself, in the well at the left of the row.
        ///
        /// <b>Sized and placed in *drawn* units, through <see cref="ChestPack"/>.</b> The
        /// closed chest icon is frame nought of the opening reel, so its sprite carries the
        /// lid's headroom — a box set straight from a height draws a chest two thirds of it and
        /// floats it high, which is a row whose reward is smaller than the gem on the row above
        /// for a reason nothing on the screen explains.
        /// </summary>
        void Reward(NightTile entry)
        {
            var rung = entry.Rung;
            var at = new Vector2(SeatX, 0f);

            if (rung.IsChest)
            {
                float tall = RewardTall;
                var box = new Vector2(tall / ChestPack.Fill * ChestPack.Aspect, tall / ChestPack.Fill);

                var chest = UIKit.Img("Chest", entry.Root, Art.S(rung.Tier.Icon), Color.white,
                                      box, Left, at + new Vector2(0f, tall * ChestPack.Lift));
                chest.preserveAspect = true;
                entry.Icon = chest;
                return;
            }

            var drop = rung.AsDrop();

            if (!drop.IsValid)
            {
                var spark = UIKit.Img("Icon", entry.Root, Art.S("Ui/ic_star"), Pal.A(Pal.Cream, .92f),
                                      new Vector2(RewardTall, RewardTall), Left, at);
                spark.preserveAspect = true;
                entry.Icon = spark;
                return;
            }

            // Never tinted: every reward glyph carries its own colour. See RewardArt.
            var icon = UIKit.Img("Icon", entry.Root, RewardArt.Icon(drop.Kind, drop.Item), Color.white,
                                 new Vector2(RewardTall, RewardTall), Left, at);
            icon.preserveAspect = true;
            entry.Icon = icon;

            // Credits have no still sprite — they are the spinning coin — so the glyph is
            // finished here rather than by `Icon`. Without this a credit night draws as a
            // white square, which is what an Image with no sprite actually is (invariant 7b).
            RewardArt.Glyph(icon, drop.Kind, 10f);
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
            // this pill spends its life changing between them. **From `ClockType` rather than
            // from wherever the last line left it**, or the fit only ever runs downhill.
            UIKit.OneLineLabel(_state, ClockRoom, ClockType, ClockLeast);
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

            // **A night that is owed and cannot yet be handed over.** A rung paying a chest
            // needs an account id to roll it against, because the server re-rolls the same
            // chest from the same seed and pays what *it* gets — so before the first sign-in
            // there is nothing honest to open (`RewardSeed.IsAdjudicable`). It is asked of the
            // rung rather than of the page, because a night paying a figure needs none of that
            // and must not be held up by it.
            bool blocked = waiting && tile.Rung.IsChest && !DailyStreak.CanClaimChests;

            // **The light comes off a row that will refuse, and this is the half that matters
            // more than the words.** The halo and the turning fan are the loudest thing on the
            // page and the page has exactly one at a time (48i) — pointed at a tap that answers
            // with an apology, they are the game asking for something it is about to refuse.
            bool lit = waiting && !blocked && tile.Night == DailyStreak.FirstPending;

            if (tile.Group) tile.Group.alpha = state == Night.Ahead ? .74f : 1f;

            // Every waiting row is a button, and every one of them takes the *earliest*
            // waiting night — see Take. A tap that did nothing would be a broken button
            // (invariant 16o), and a tap that quietly reached past an older night would be a
            // reward stranded behind a newer one.
            if (tile.Tap) tile.Tap.gameObject.SetActive(waiting);

            // The right end carries one answer at a time, and the order is the order a player
            // needs them in: something to do, then something done, then when it will be.
            //
            // A blocked night takes the *pill* rather than a greyed key, and that is the same
            // choice the shelf made about its padlock strip (42e): the green key is this page's
            // one instruction, so a dead one on the single row that ought to be offering
            // something reads as broken, where the pill is already the widget for "this row can
            // do nothing right now, and here is why".
            if (tile.Collect) tile.Collect.gameObject.SetActive(waiting && !blocked);
            if (tile.Seal) tile.Seal.gameObject.SetActive(kept);
            if (tile.Mark) tile.Mark.gameObject.SetActive(blocked || (!waiting && !kept));

            if (tile.Mark && tile.Mark.gameObject.activeSelf)
            {
                var text = tile.Mark.GetComponentInChildren<Text>();
                if (text)
                {
                    // A night still ahead says *when*, which is the one question a row that can
                    // do nothing is still being asked — "when do I get the Royal Chest" is the
                    // reason somebody scrolls to the bottom of this list at all. Written out
                    // per case rather than composed, because the build gate scans the source
                    // for key-shaped literals and a concatenated key is invisible to it.
                    int away = tile.Night - _days;

                    // A blocked night answers *what to do* rather than *when*, which is the
                    // shelf's rule about a padlock strip saying "Level 26" rather than LOCKED
                    // (42e): one word that says a player cannot have this and not what would
                    // change that is half a sentence. Amber rather than the schedule's quiet
                    // cream, because it is news rather than a date.
                    text.text = (blocked ? Loc.Get("ui.streak.needs_connection")
                               : state == Night.Tonight ? Loc.Get("ui.streak.tonight")
                               : away <= 1 ? Loc.Get("ui.streak.in_one")
                               : Loc.Format("ui.streak.in_many", away)).ToUpperInvariant();

                    text.color = blocked ? Pal.Sun
                               : state == Night.Tonight ? Pal.Aqua : Pal.A(Pal.Cream, .60f);

                    // **Measured from `MarkType` rather than from whatever the last line left
                    // behind**, which is why `OneLineLabel` asks for it: the fit only ever
                    // shrinks, so a pill that said TOMORROW NIGHT and then says TONIGHT would
                    // keep the smaller type for the life of the screen and walk itself down a
                    // point every time a longer line came through.
                    UIKit.OneLineLabel(text, MarkRoom, MarkType, MarkLeast);
                }
            }

            // The night is gold while it is the player's to take, mint once it is theirs, and
            // cream the rest of the time. Colour on the *title* rather than on a chip, because
            // a row has a line of text where a tile had none: the words are the element every
            // row has, so the words are what carries the state.
            if (tile.Title)
                tile.Title.color = lit ? Pal.Gold : kept ? Pal.Mint : Pal.Cream;

            // A kept night recedes without going grey — it still has to read.
            if (tile.Card) tile.Card.color = kept ? new Color(.90f, .94f, 1f, 1f) : Color.white;
            if (tile.Icon)
            {
                tile.Icon.color = kept ? new Color(.84f, .88f, .94f, 1f) : Color.white;

                // Re-asserted rather than assumed. A repaint is this page's drawing of a state
                // and `enabled` is as much a part of that state as the tint beside it — the
                // collect path switched it off once and no repaint ever switched it back, which
                // is the whole of how an icon went missing until the screen was rebuilt.
                tile.Icon.enabled = true;
            }
            if (tile.Sub) tile.Sub.color = Pal.A(Pal.Cream, kept ? .66f : .84f);

            if (lit && !tile.Lit)
            {
                tile.Aura = Aura(tile.Seat);
                if (tile.Icon) Tween.Bob((RectTransform)tile.Icon.transform, 6f, 1.5f);
                if (tile.Collect) Tween.Breathe(tile.Collect, .035f, 1.5f);
                Sheen.Attach(tile.Root, 2.8f);
                Shine(tile, true);
                tile.Lit = true;
            }
            else if (!lit && tile.Lit)
            {
                if (tile.Aura) Destroy(tile.Aura.gameObject);
                tile.Aura = null;
                if (tile.Icon) Tween.KillChannel(tile.Icon.transform, "bob");
                if (tile.Collect) Tween.KillChannel(tile.Collect, "breathe");
                Shine(tile, false);
                tile.Lit = false;
            }
        }

        /// <summary>
        /// The light around a reward that can be taken: a halo, and a turning fan over it.
        ///
        /// <para>
        /// <b>This is what "ready" looks like, and it is deliberately more than a glow.</b> The
        /// page has exactly one of these at a time — only the oldest waiting night may be taken
        /// — so it can afford to be the loudest thing on the screen, and it has to be: the row
        /// under it is one of seven that otherwise look alike. The fan says <em>light is coming
        /// out of this</em>, which is the thing that could not be said by tinting the plate.
        /// </para>
        /// <para>
        /// <b>A third piece was here and the owner had it taken out: a gold ring that scaled
        /// out of the seat and faded as it grew, on a loop.</b> It said <em>and it is still
        /// happening</em>, which the fan already says by turning — so what it really added was
        /// a second thing moving on its own clock over a reward the player is meant to be
        /// looking at, and two sharp things on one row is the complaint this page's own copy
        /// recorded about the pool under it (see <see cref="Shine"/>). The halo and the fan are
        /// what is left; nothing else about the row moved.
        /// </para>
        /// <para>
        /// Both are generated shapes rather than art — see <see cref="Art.Rays"/> — so the
        /// effect costs no addresses and tints to whatever the palette says. They live on one
        /// node hung <em>behind</em> the well, so a payout can throw the lot away in a single
        /// call without touching the row underneath, and so the reward itself is never drawn
        /// through.
        /// </para>
        /// </summary>
        RectTransform Aura(RectTransform seat)
        {
            if (seat == null) return null;

            // <b>The light is held inside the card, and the card's own shape is what holds
            // it.</b> A fan 2.15 wells across is 318 units on a row 184 tall, so two thirds of
            // what a player could see of it was drawn *outside* the plate — over the night above
            // and the night below, which on a list is a light belonging to no row. The pieces
            // are not shrunk to fit, because they cannot be: `Art.Rays` is hollow in the middle
            // so the eye only ever sees the band between a quarter and three quarters of its
            // radius, and at any size that band clears a 148-unit well it is already past a
            // 184-unit row. So the fan keeps its reach and the plate keeps it in.
            //
            // A <c>RectMask2D</c> would be the cheap answer and it is the wrong one: it clips to
            // a *rectangle* where this plate is rounded, so each corner leaks a square nub of
            // light outside the silhouette (44i's rule about measuring against the corner that
            // is really there — a render found both of them). A <see cref="Mask"/> over
            // <see cref="Art.Round"/> clips to the shape itself. It is safe here for the one
            // reason a mask usually is not: the sprite is <em>generated</em>, so the compression
            // that speckles a masked texture (see the publisher card) cannot reach it.
            var clip = UIKit.Img("AuraClip", seat.parent, Art.Round(CardRound), Color.white);
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var gate = (RectTransform)clip.transform;

            // <b>Directly under the well, and *not* first.</b> The card is an opaque plate the
            // kit cuts, and a sibling before it is a light with a card drawn on top of it — the
            // whole effect invisible, on the one row it exists for. (The render mirror composited
            // the ring after the card and so could not see it, which is 44d's rule about a mirror
            // with its own idea of the order.) Inserting at the well's own index puts every ray
            // over the plate and every one of them under the reward.
            gate.SetSiblingIndex(seat.GetSiblingIndex());

            var host = UIKit.Box("Aura", gate, Vector2.zero, Left, new Vector2(SeatX, 0f));

            UIKit.Halo(host, Pal.Gold, SeatSize * 2.4f, .46f);

            var rays = UIKit.Img("Rays", host, Art.Rays(256, 14), Pal.A(Pal.Sun, .34f),
                                 Vector2.one * SeatSize * 2.15f, Centre, Vector2.zero);
            var rrt = (RectTransform)rays.transform;
            Tween.Run(14f, Ease.Linear,
                      t => { if (rrt) rrt.localRotation = Quaternion.Euler(0f, 0f, t * 360f); },
                      rays).Loop(-1, false);

            // The clip rather than the host, so one <c>Destroy</c> still takes the whole light
            // away when the row stops being the one on offer.
            return gate;
        }

        /// <summary>
        /// The light the whole row stands in: a warm pool that reaches past the card and a
        /// bright rim on its edge, breathing together on one tween.
        ///
        /// <para>
        /// <b>Two pieces rather than one, because a card is opaque.</b> A glow behind the kit's
        /// navy card is a glow with a card-shaped hole punched out of the middle of it, and a
        /// glow in front of it washes out everything written on the row — so the light outside
        /// the card is a pool and the light on the card is its edge. Together they read as one
        /// thing lit from behind (the tasks page's finding).
        /// </para>
        /// <para>
        /// It breathes rather than flashing (37h's rule about the one ward that may flash). It
        /// was written as the <em>slow</em> half of a pair against a ring that scaled out of the
        /// seat on its own clock — and that ring is gone at the owner's instruction, which
        /// settles the pairing the other way: the pool breathes and the fan turns, and nothing
        /// on the row is sharp.
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
            Settle();
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
                Settle();
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

            // <b>The reward stays on the row.</b> This used to hide the icon the moment the
            // flight left — "a reward that vanishes from the face it was printed on is the
            // point" — and the icon then never came back, because nothing re-enabled it: a
            // repaint writes the icon's *colour* and has never written its `enabled`, so a
            // collected night was a row with a hole in it for the life of the screen and the
            // repair was to leave the page and come back. The flight spawns its own tokens and
            // only reads this rect for a start point (`RewardFlight.Add`), so nothing here was
            // ever consumed — the disappearance was decoration, and the seal stamped over it
            // already says the night has been taken.
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
                Settle();
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

        // ----------------------------------------------------------------- foot
        /// <summary>
        /// How much room the board leaves under itself.
        ///
        /// The page used to stand a PLAY A LEVEL button here whenever the flame was out or at
        /// risk, which made the board's height a function of the state and cost a redraw
        /// every time the answer moved. Nothing is drawn under the board now, so the clearance
        /// is the nav bar and a margin, and the board is the same height in every state.
        /// </summary>
        float BoardFoot => NavBar.Height + 20f;

        public override bool OnBack() { Flow.Go<HomeScreen>(); return true; }
    }
}
