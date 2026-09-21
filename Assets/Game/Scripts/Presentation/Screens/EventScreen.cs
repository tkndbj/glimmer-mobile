using System;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Cloud;
using GlimmerGrove.Daily;
using GlimmerGrove.Events;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The season: how many marks have been grown, and the forty rungs that pays.
    ///
    /// <para>
    /// <b>The page is the tasks page's page, and that is the whole of the rebuild.</b> The
    /// old one was a painted sky with two vines climbing it, forty milestone cards drawn
    /// from a palette of its own and a dock at the foot — a screen that looked like nothing
    /// else in the game, on a ground the interface kit does not use. It is the same furniture
    /// as Tasks &amp; Bonuses now: the kit's plates on <see cref="Scenery.Plain"/>, a title
    /// ribbon over the wallet, one hero plate, and a list of cards under pinned headings. A
    /// season and a slate are the same idea at two cadences, and there was never a reason for
    /// them to be two designs.
    /// </para>
    /// <para>
    /// <b>A rung pays a chest, so a rung opens the same ceremony every other chest opens</b>
    /// (<see cref="ChestOverlay"/> through <see cref="ChestClaim"/>). That is what took a
    /// thousand lines out of this file: the payout cascade, the decoys, the shockwaves and
    /// the rising figures all belonged to a screen that was handing over currency itself, and
    /// this one hands over a chest and lets the chest do it.
    /// </para>
    /// <para>
    /// <b>The pass is bought with gems, so this screen sells it itself.</b> No store sheet, no
    /// receipt, no round trip and nothing to wait on: a gem debit is an ordinary spend
    /// (invariant 18), so the purchase is <see cref="SeasonLedger.TryBuyPass"/> and the page
    /// repaints. What used to be here — a pass state fetched once per visit, a price that
    /// arrived from the store, a refusal for every one of six reasons the sheet could fail —
    /// went with the real-money product.
    /// </para>
    /// </summary>
    public sealed class EventScreen : View
    {
        public override string Track => "mus_menu";

        // The stack, in canvas units from the top of the safe area.
        const float ChromeSize = 92f;
        const float HeroH = 240f;
        /// <summary>
        /// The pass row is the one thing on this page that is for sale, and at 132 it was the
        /// shortest plate on a page whose hero is 240 and whose rung cards are 192 — the offer
        /// read as a footnote between them. Grown at the owner's instruction; everything inside
        /// it is measured off the new height rather than left where the short plate put it, so
        /// the crest, the two lines and the key all grow with the box.
        /// </summary>
        const float PassH = 176f;
        const float HeadingH = 62f;
        const float Width = 1000f;

        static readonly Vector2 Top = new Vector2(.5f, 1f);
        static readonly Vector2 Left = new Vector2(0f, .5f);
        static readonly Vector2 Centre = new Vector2(.5f, .5f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);

        /// <summary>
        /// The value badge in the pass plate's top-left corner: how big, how far in, and which
        /// way it leans.
        ///
        /// <para>
        /// <b>Smaller than the shop's seal and it has to be.</b> A storefront card is a tall
        /// column with an empty corner; this plate is 176 tall with a 128 crest centred in it,
        /// so there is no corner here that is actually empty. At 164, or at 88 set 50 in, the
        /// two bursts overlap into one mushy double star — which the render showed and no
        /// numeric gate could.
        /// </para>
        /// <para>
        /// <b>A badge that grows has to move as it grows, and that is the whole finding.</b>
        /// Grown twice at the owner's instruction, 92 to 110 to 124. The crest is centred in
        /// the plate and to the right, so clearance is bought going <em>up and left</em>, not
        /// by size alone: 124 set 42 in crowds the crest and 138 buries it, while the same 124
        /// set 36 in keeps its gap. The caption settles at 17 against a floor of 10
        /// (<c>render_season.py</c> prints it) where at 92 it sat at 13, and that headroom for
        /// a longer translation is the other thing the size bought.
        /// </para>
        /// <para>
        /// <b>The inset is bounded by the margin, not by the crest.</b> The plate is
        /// <see cref="Width"/> in a canvas that is never narrower than 1080, so there are 40
        /// units of gutter either side. At 36 the badge overhangs 26 of them, so it needs a
        /// safe area of 1052 against the 1080 the narrowest canvas here is — which portrait
        /// always is, since the horizontal insets are nought and a tablet is wider still.
        /// </para>
        /// <para>
        /// <b>The tilt is positive where the shop's is negative</b>, for the reason
        /// <c>ProductCardBadges.SealTilt</c> gives: a badge leaning *right* is a negative
        /// number, and a mark in the left corner should lean away from the plate rather than
        /// into it.
        /// </para>
        /// <para>
        /// <b>Rose rather than gold, which is the shop's own choice and for this reason.</b>
        /// <c>ProductCard</c> seals in <see cref="Pal.Rose"/>; here it is load-bearing rather
        /// than a house style, because the thing this badge stands beside is a *gold burst*
        /// and two gold bursts touching read as one shape somebody drew badly.
        /// </para>
        /// </summary>
        const float PassBadge = 124f, PassBadgeInset = 36f, PassBadgeDrop = 20f,
                    PassBadgeTilt = 9f;

        /// <summary>
        /// The bar's orange and its full green, pre-divided for <see cref="Skins.Fill"/>'s
        /// multiply. The tasks page's numbers, and deliberately the same two: a bar means one
        /// thing across this game, and a season that invented its own would be a third answer
        /// to a question a swatch sheet settled once (invariant 37l, 45h).
        /// </summary>
        /// <summary>
        /// What the countdown pill really leaves its words: `Scenery.Pill` reserves the
        /// glyph's lane off the height and 16 units on the right. Written here rather than at
        /// the call site because the clock is re-fitted on every tick and the two have to
        /// agree — a fitter shrinking against a width the pill does not have is a fitter that
        /// lets the text spill anyway.
        ///
        /// <para>
        /// <see cref="ClockType"/> is beside it because a fit that is re-run has to start from
        /// the size the pill was designed at rather than from the size the last line left it
        /// at: this one changes between "41d 23h", "23:59:07" and "the season has ended", so
        /// every long line filed the type down a point that no short one gave back. See
        /// <see cref="UIKit.OneLineLabel"/>.
        /// </para>
        /// </summary>
        const float ClockRoom = 300f - 50f * .82f - 16f;
        const int ClockType = 22, ClockLeast = 14;

        static readonly Color BarOrange = new Color(1f, .588f, .118f, 1f);
        static readonly Color BarFull = new Color(.376f, .922f, .275f, 1f);
        const float BarH = 26f;

        GroveEvent _season;
        EventProgress _progress;
        SeasonLadder _ladder;

        /// <summary>
        /// The "connect once" plate, or null on every account that has ever been online.
        /// Built at most once per visit; see <see cref="BuildConnectBanner"/>.
        /// </summary>
        ConnectBanner _connect;

        Image _bar;
        RectTransform _fill, _mark;

        /// <summary>
        /// How many rungs the mark was last drawn open to, so it is redrawn when that moves
        /// and not on every repaint.
        ///
        /// <para>
        /// <see cref="SeasonCrest.Paint"/> <em>adds</em> to its host rather than replacing what
        /// is there, so calling it on a repaint would stack a second crest on the first — and a
        /// crest painted at nought on the way in would then never open at all, which is what
        /// this used to do.
        /// </para>
        /// <para>
        /// <b>It is kept although the season this game ships wears a crest that does not
        /// open.</b> A crest is chosen in the manifest and <see cref="SeasonCrest.Bloom"/>
        /// still fills with the track, so a season that picks it would need exactly this; and
        /// the guard is what stops the repaint stacking, which every crest needs whether it
        /// uses the number or not.
        /// </para>
        /// </summary>
        int _markRungs = -1;
        Text _grown, _toNext, _clock, _passCaption, _passHint, _rungs;
        Image _passValue;
        Btn _passBtn;
        float _track;

        /// <summary>
        /// Whether the bar was last drawn full, or null before the first paint. Nullable for
        /// the tasks page's reason: a page opened on a finished run should find the bar
        /// already green rather than watch it arrive.
        /// </summary>
        bool? _barFull;

        /// <summary>The reels, so the first tap on a ready rung finds its lid already loaded.</summary>
        AssetHold _reels;

        bool _claiming, _retreat;

        /// <summary>
        /// Bumped whenever the account or the screen changes, so a reply that arrives after
        /// either is dropped rather than written onto a page it does not describe.
        /// </summary>
        int _generation;

        float _clockTick;

        // ---------------------------------------------------------------------- build
        protected override void Build()
        {
            _season = GroveEvents.Featured;
            if (_season == null) { _retreat = true; return; }

            // Dropped for `TasksScreen.Build`'s reason: a banner kept across a rebuild is either
            // a destroyed object or a sentence `BuildConnectBanner` will not overwrite, since it
            // returns early once the gate has opened.
            _connect = null;

            // The quiet ground every screen that is a list rather than a place stands on. A
            // season is a list of rewards; the painting belongs on the hub and the map.
            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 22, new Color(1f, .93f, .70f), 6f, 22f);

            float y = 22f;
            y = BuildHeader(y);
            y = BuildHero(y);
            y = BuildPassBanner(y);
            y = BuildConnectBanner(y);
            y = BuildHeadings(y);

            _ladder = new SeasonLadder(OnRungTapped);
            _ladder.Build(Safe, Width, y, NavBar.Height + 20f, _season);

            NavBar.Build(Content, NavBar.Tab.Home);
            HoldReels();

            Repaint();
            _ladder.FocusOnNext(_progress, Owned);
            _ladder.Repaint(_progress, Owned, force: true);
        }

        void HoldReels() => Run(async token =>
        {
            _reels = _reels ?? AssetLibrary.Hold("chests");
            await _reels.LoadAsync(AssetManifest.ChestAssets(ProgressionRules.Table.Tasks), null, token);
        });

        void OnDestroy()
        {
            _reels?.Dispose();
            _reels = null;
        }

        // --------------------------------------------------------------- the chrome
        /// <summary>
        /// The corners, the season's name, what it is, and the wallet — the tasks page's
        /// order, for its reason: the first thing read on a page about what there is to earn
        /// should be what the page is, and the pills are still where
        /// <see cref="RewardFlight"/> lands a chest's tokens.
        /// </summary>
        float BuildHeader(float y)
        {
            const float BannerH = 138f;
            float cy = -(y + BannerH * .5f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * ChromeSize,
                             new Vector2(0f, 1f), new Vector2(76f, cy),
                             () => Flow.Go<HomeScreen>());
            UIKit.IconButton("Info", Safe, Skins.Aside, "ic_info", Vector2.one * ChromeSize,
                             new Vector2(1f, 1f), new Vector2(-76f, cy),
                             () => { if (!Flow.HasModal) Flow.Modal<EventInfoOverlay>(v => v.Season = _season); });

            var ribbon = Scenery.TitleRibbon(Safe, Loc.Get(_season.NameKey).ToUpperInvariant(),
                                             new Vector2(720f, BannerH), Top, new Vector2(0f, cy), 42);
            ribbon.transform.localScale = Vector3.zero;
            Tween.Pop(ribbon.transform, 0f, .5f, .06f);
            y += BannerH + 4f;

            UIKit.Shrinkable(
                UIKit.Titled("Sub", Safe, Loc.Get("ui.mark.subtitle"), 24,
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
            // somebody sat here reading the ladder.
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
        /// What the season is graded on, in the middle of the plate: the crest, the mark count
        /// at a size that says it matters, the run to the next rung, and the clock.
        ///
        /// <para>
        /// <b>The count is the hero for the reason the Infinite lane's medal is</b> (43b): a
        /// track graded on one number puts that number in the middle of the screen. Under it
        /// the bar measures the run between the <em>last</em> rung and the next rather than
        /// the whole ladder — a bar that crawls across forty rungs is a bar that never visibly
        /// moves, where this one fills every few chests.
        /// </para>
        /// </summary>
        float BuildHero(float y)
        {
            const float WIDTH = Width;

            var plate = UIKit.Img("Hero", Safe, Art.S("Ui/" + Skins.Panel), Color.white,
                                  new Vector2(Width, HeroH), Top, new Vector2(0f, -(y + HeroH * .5f)));

            var clip = UIKit.Node("Clip", plate.transform);
            UIKit.StretchTo(clip, 6f, 6f, 6f, 6f);
            clip.gameObject.AddComponent<RectMask2D>();

            var rays = UIKit.Img("Rays", clip, Art.Rays(512, 14), Pal.A(Pal.Sun, .16f),
                                 new Vector2(760f, 760f), Left, new Vector2(150f, 10f));
            Tween.Run(42f, Ease.Linear,
                      t => { if (rays) rays.transform.localRotation = Quaternion.Euler(0, 0, t * 360f); },
                      rays, "spin").Loop(-1, false);

            // The season's own crest. Drawn by PaintMark on the first repaint rather than here,
            // because a crest that fills with the track (SeasonCrest.Bloom) has to be painted
            // once the progress is known — the shipped one is a fixed emblem and does not care,
            // and a screen that only draws the one crest its manifest happens to name today is
            // a screen that breaks on the next season.
            _mark = UIKit.Box("Mark", plate.transform, new Vector2(150f, 150f), Left,
                              new Vector2(132f, 14f));
            Tween.Breathe(_mark, .05f, 2.6f);

            _grown = UIKit.Shrinkable(
                UIKit.Titled("Grown", plate.transform, string.Empty, 58, Pal.Cream,
                             TextAnchor.MiddleLeft, new Vector2(460f, 72f), Left,
                             new Vector2(236f + 230f, 34f), 4f, 5f), 30);

            _toNext = UIKit.Shrinkable(
                UIKit.Titled("ToNext", plate.transform, string.Empty, 24, Pal.A(Pal.Cream, .82f),
                             TextAnchor.MiddleLeft, new Vector2(460f, 32f), Left,
                             new Vector2(236f + 230f, -12f), 3f, 3f), 15);

            // How far up the ladder, at the right end of the plate.
            //
            // The count of marks is what the season is *graded* on and the count of rungs is
            // what it is *spent* on, and they answer different questions — "am I getting
            // anywhere" against "how much is left". Drawn as a fraction rather than as a bar,
            // because the bar beside it is already measuring the run between two rungs and two
            // bars on one plate would be two readings of one thing (37v's rule about what a
            // corner owes).
            const float RungsW = 232f;
            float rx = WIDTH * .5f - RungsW * .5f - 26f;

            UIKit.Img("RungsWell", plate.transform, Art.S("Ui/" + Skins.Trough), Color.white,
                      new Vector2(RungsW, 104f), Centre, new Vector2(rx, -22f));

            _rungs = UIKit.Shrinkable(
                UIKit.Titled("Rungs", plate.transform, string.Empty, 40, Pal.Gold,
                             TextAnchor.MiddleCenter, new Vector2(RungsW - 24f, 52f), Centre,
                             new Vector2(rx, -8f), 3f, 4f), 22);

            UIKit.Shrinkable(
                UIKit.Titled("RungsCap", plate.transform, Loc.Get("ui.mark.rungs"), 21,
                             Pal.A(Pal.Cream, .74f), TextAnchor.MiddleCenter,
                             new Vector2(RungsW - 24f, 28f), Centre, new Vector2(rx, -48f), 0f, 0f), 13);

            _track = 460f;
            var trough = UIKit.Img("Trough", plate.transform, Art.S("Ui/" + Skins.Trough), Color.white,
                                   new Vector2(_track, 30f), Left, new Vector2(236f + _track * .5f, -52f));
            var fill = UIKit.Img("Fill", trough.transform, Art.S("Ui/" + Skins.Fill), BarOrange,
                                 new Vector2(0f, BarH), Left, new Vector2(4f, 0f));
            _fill = (RectTransform)fill.transform;
            _fill.pivot = new Vector2(0f, .5f);
            _bar = fill;

            // <b>Placed with `UIKit.Corner`, because `UIKit.Box` always pivots at centre.</b>
            // Handed the margin directly, a 300-wide pill tucked 28 units from the plate's
            // right edge hangs 122 of them off it — which reads from the outside as the
            // countdown overflowing its container, and is really the container standing in
            // the wrong place. The same trap the win panel's corner buttons are documented
            // for.
            const float ClockW = 300f, ClockH = 50f;

            _clock = UIKit.OneLineLabel(
                Scenery.Pill(plate.transform, string.Empty, ClockType, new Vector2(ClockW, ClockH),
                             new Vector2(1f, 1f),
                             UIKit.Corner(new Vector2(ClockW, ClockH), new Vector2(1f, 1f), 28f, 22f),
                             new Color(.05f, .09f, .18f, .78f), "ic_restart"),
                // What `Scenery.Pill` really leaves the words: the glyph's lane and the
                // right-hand padding come off the 300. Measured rather than guessed, or the
                // fitter is shrinking against a width the pill does not have.
                ClockRoom, ClockType, ClockLeast);

            plate.transform.localScale = Vector3.zero;
            Tween.Pop(plate.transform, 0f, .5f, .10f);

            return y + HeroH + 14f;
        }

        // ------------------------------------------------------------------ the pass
        /// <summary>
        /// The one thing on this page that is for sale, and the one row that changes shape
        /// when it is bought: an offer while it is not held, a plain "unlocked" strip once it
        /// is. A season with no product draws neither and the row costs nothing.
        /// </summary>
        /// <summary>
        /// The standing "connect once" plate, under the pass and above the ladder.
        ///
        /// <para>
        /// <b>Built only while the gate is shut, and the gate never shuts again.</b> Asked
        /// here rather than in the repaint because the answer decides how much room the ladder
        /// below starts at: a band that can appear later would have to relay the whole page,
        /// and the only transition that exists is the one that takes it *away* (see
        /// <see cref="ConnectBanner.Show"/>).
        /// </para>
        /// <para>
        /// Under the pass rather than over it, because the pass is what the page is selling and
        /// this is a note about the page. Above the ladder rather than below it, because the
        /// ladder scrolls and a sentence explaining why nothing on it can be taken must not be
        /// something a player has to scroll to find.
        /// </para>
        /// </summary>
        float BuildConnectBanner(float y)
        {
            if (SeasonLedger.CanClaim) return y;

            _connect = ConnectBanner.Build(Safe, Width, y);
            return y + ConnectBanner.Height + ConnectBanner.Gap;
        }

        float BuildPassBanner(float y)
        {
            if (!_season.HasPremium) return y;

            var plate = UIKit.Img("Pass", Safe, Art.S("Ui/" + Skins.PlateViolet), Color.white,
                                  new Vector2(Width, PassH), Top, new Vector2(0f, -(y + PassH * .5f)));

            UIKit.Img("Glow", plate.transform, Art.Glow(128, 2f), Pal.A(Pal.Bloom, .22f),
                      new Vector2(300f, 300f), Left, new Vector2(116f, 0f));

            // Gold rather than the kit's own white. `Skins.Badge` is a plain burst, and a white
            // one on a violet plate reads as a shape somebody forgot to fill in; gold is what
            // this UI already means by "the good one", and it is the one mark on the page that
            // is about a purchase.
            var crest = UIKit.Img("Crest", plate.transform, Art.S("Ui/" + Skins.Badge), Pal.Gold,
                                  new Vector2(128f, 128f), Left, new Vector2(116f, 0f));
            crest.preserveAspect = true;

            var seal = UIKit.Img("Seal", crest.transform, Art.S("Ui/ic_gem"), Pal.Cream,
                                 new Vector2(58f, 58f), Centre, Vector2.zero);
            seal.preserveAspect = true;

            // The room between the crest and the button, measured rather than guessed, and
            // re-measured when the plate grew: the crest ends at 116 + 64 = 180, the words
            // start at `TextX` and the button's near edge is at 1000 - 166 - 150 = 684. A Unity
            // label that overflows is not clipped and nothing says so (invariant 37n), so the
            // box is the gap less a hair and the fitter does the rest.
            const float TextX = 200f;
            const float HintW = 470f;

            _passCaption = UIKit.Shrinkable(
                UIKit.Titled("Name", plate.transform, Loc.Get("ui.mark.pass"), 38, Pal.Cream,
                             TextAnchor.MiddleLeft, new Vector2(HintW, 46f), Left,
                             new Vector2(TextX + HintW * .5f, 38f), 3f, 4f), 22);

            // <b>The sentence is given two lines rather than one, which is what the taller
            // plate is for.</b> `UIKit.Shrinkable` wraps and then truncates, so a one-line box
            // makes a long sentence *smaller*, not wider — on a narrower column it came out at
            // its floor and clipped. Both hints here wrap to two lines at 26 and stand 62 units
            // tall, which is the box; the offer is now read at 26 where it used to be read at
            // 15, and it is the line the purchase is decided on.
            _passHint = UIKit.Shrinkable(
                UIKit.Titled("Hint", plate.transform, string.Empty, 26, Pal.A(Pal.Cream, .80f),
                             TextAnchor.MiddleLeft, new Vector2(HintW, 64f), Left,
                             new Vector2(TextX + HintW * .5f, -32f), 0f, 0f), 17);

            // <b>`UIKit.Button` makes no label, so `SetCaption` had nothing to write into</b> —
            // the button drew as an empty box for as long as it existed. `TextButton` is the
            // one that builds a caption, and a price wants it anyway: a trailing glyph is a
            // *unit* on the number a caption ends with (`Btn.IconTrails`), which is how every
            // other price in this game says which currency it is. So the caption is the figure
            // and the gem beside it is the word, and neither has to fit the other in.
            _passBtn = UIKit.TextButton("Buy", plate.transform, Skins.Gem, string.Empty, 38,
                                        new Vector2(300f, 104f), new Vector2(1f, .5f),
                                        new Vector2(-166f, 0f), BuyPass,
                                        Art.S("Ui/ic_gem"), iconTrails: true);

            // <b>What the paid column is worth against what it costs</b>, in the shop's own
            // grammar — the same burst the storefront seals a pack with, tilted the other way
            // because this one sits in the left corner. It is the last thing built on the
            // plate so it draws over the crest's outer points rather than under them.
            //
            // <b>The figure is derived, never authored</b> (<see cref="SeasonValue"/>): the
            // pass price, the ladder and every tier's chest are content, so a badge carrying a
            // typed number would be the one thing on this page a retune could make into a lie.
            // Nought means there is nothing honest to claim and no badge is built at all.
            int value = SeasonValue.PassPercent(_season);
            if (value > 0)
            {
                _passValue = UIKit.Img("Value", plate.transform, Art.S("Ui/" + Skins.Badge),
                                       Pal.Rose, new Vector2(PassBadge, PassBadge), TopLeft,
                                       new Vector2(PassBadgeInset, -PassBadgeDrop));
                _passValue.preserveAspect = true;
                _passValue.raycastTarget = false;
                _passValue.transform.localRotation = Quaternion.Euler(0f, 0f, PassBadgeTilt);

                // Sized against the flat field inside the burst rather than against the sprite,
                // which is `ProductCardBadges`' measurement and its reason: a caption centred
                // on the sprite sits low, and one sized to the whole texture says its piece
                // across the rim. Shrinkable because a translation of "VALUE" is not three
                // letters everywhere, and the floor is the shop's — below it Best Fit stops
                // shrinking and the label overflows unclipped and unreported (invariant 19n).
                UIKit.Shrinkable(
                    UIKit.Titled("VT", _passValue.transform,
                                 Loc.Format("ui.mark.pass_value", value),
                                 ProductCardBadges.TextSize, Pal.Cream, TextAnchor.MiddleCenter,
                                 new Vector2(PassBadge * ProductCardBadges.Face
                                             * ProductCardBadges.FaceTextWidth,
                                             PassBadge * ProductCardBadges.Face
                                             * ProductCardBadges.FaceTextHeight),
                                 Centre,
                                 new Vector2(PassBadge * ProductCardBadges.FaceShift,
                                             PassBadge * ProductCardBadges.FaceRise),
                                 0f, 0f, wrap: true),
                    ProductCardBadges.TextFloor);
            }

            Sheen.Attach((RectTransform)plate.transform, 4.6f);

            plate.transform.localScale = Vector3.zero;
            Tween.Pop(plate.transform, 0f, .5f, .14f);

            return y + PassH + 14f;
        }

        /// <summary>
        /// The two column headings, pinned above the list rather than scrolling with it.
        ///
        /// A rung card carries two chests and nothing on it says which column is which — the
        /// pictures are the same four tiers on both tracks. Repeating the words on forty cards
        /// would be eighty labels saying one thing; saying it once above the list is the
        /// arrangement every table in this game already uses.
        /// </summary>
        float BuildHeadings(float y)
        {
            float cy = -(y + HeadingH * .5f);

            UIKit.Shrinkable(
                UIKit.Titled("HFree", Safe, Loc.Get("ui.mark.free").ToUpperInvariant(), 24, Pal.Mint,
                             TextAnchor.MiddleCenter, new Vector2(220f, 34f), Top,
                             new Vector2(SeasonLadder.FreeX, cy), 3f, 3f), 14);

            UIKit.Shrinkable(
                UIKit.Titled("HPass", Safe, Loc.Get("ui.mark.pass").ToUpperInvariant(), 24, Pal.Bloom,
                             TextAnchor.MiddleCenter, new Vector2(220f, 34f), Top,
                             new Vector2(SeasonLadder.PassX, cy), 3f, 3f), 14);

            return y + HeadingH;
        }

        // ----------------------------------------------------------------- lifecycle
        void OnEnable()
        {
            SeasonLedger.Changed += OnChanged;
            CloudSaveService.IdentityChanged += OnIdentity;
            CloudSaveService.Synced += OnSynced;
        }

        void OnDisable()
        {
            SeasonLedger.Changed -= OnChanged;
            CloudSaveService.IdentityChanged -= OnIdentity;
            CloudSaveService.Synced -= OnSynced;
            _generation++;
        }

        public override void OnPresented()
        {
            if (_retreat) Flow.Go<HomeScreen>();
        }

        void OnChanged()
        {
            if (this == null || Content == null || _claiming) return;
            Repaint();
        }

        /// <summary>
        /// The account changed under the screen. Everything this page draws comes out of the
        /// save, and the save has already been swapped by the time this runs, so a repaint is
        /// the whole of what is owed.
        /// </summary>
        void OnIdentity()
        {
            _generation++;
            _claiming = false;
            Repaint();
        }

        void OnSynced() { if (!_claiming) Repaint(); }

        /// <summary>
        /// The ladder follows the scroll, and the clock ticks once a second.
        ///
        /// Repainting the ladder on every frame is what a recycler is for — it binds only the
        /// rows that moved — and the clock is a label, so it is written on its own slower beat
        /// rather than every frame like the rows.
        /// </summary>
        void Update()
        {
            if (_season == null) return;

            _ladder?.Repaint(_progress, Owned);

            _clockTick += Time.unscaledDeltaTime;
            if (_clockTick < 1f) return;
            _clockTick = 0f;
            RefreshClock();
        }

        // ----------------------------------------------------------------- the pass
        bool Owned => _season != null && SeasonLedger.OwnsPass(_season.Id);

        /// <summary>
        /// Buys the pass, or says which wall it met.
        ///
        /// <para>
        /// Four answers rather than one, and the order is the order a player needs them in:
        /// already held first (which is not a failure and says so quietly), then the season
        /// being over, then the price. A gem debit is an ordinary spend, so there is no third
        /// party to wait on and no state this screen has to fetch before it can sell.
        /// </para>
        /// </summary>
        void BuyPass()
        {
            if (_claiming || Flow.HasModal || _season == null) return;

            switch (SeasonLedger.TryBuyPass(_season))
            {
                case SeasonLedger.PassBuy.Bought:
                    Audio.Sfx("collect", .8f);
                    if (_passBtn) Tween.Punch(_passBtn.transform, .14f, .32f);
                    Burst.Confetti(Content, 34);
                    Scenery.Toast(Content, Loc.Get("ui.mark.bought"), Pal.Mint, 2.6f);
                    Repaint();
                    return;

                case SeasonLedger.PassBuy.Held:
                    Scenery.Toast(Content, Loc.Get("ui.mark.owned_hint"), Pal.Gold);
                    return;

                case SeasonLedger.PassBuy.Closed:
                    Scenery.Toast(Content, Loc.Get("ui.mark.closed_hint"), Pal.Gold);
                    return;

                default:
                    // The one refusal worth sending somewhere: a player short of gems is a
                    // player one screen away from having them.
                    Scenery.Toast(Content, Loc.Format("ui.mark.too_poor", _season.PassGems),
                                  Pal.Rose, 2.8f);
                    return;
            }
        }

        // ------------------------------------------------------------------ claiming
        /// <summary>
        /// A chest was tapped.
        ///
        /// <para>
        /// Four answers, in the order a player needs them: the wall credits cannot climb comes
        /// first (15a's rule, read across), then the connection, then "not yet", and only then
        /// the ceremony. A tap on a rung that is merely locked says how far off it is rather
        /// than doing nothing, because a button that swallows a tap is a broken button (16o).
        /// </para>
        /// </summary>
        void OnRungTapped(int index, SeasonTrack track)
        {
            if (_claiming || Flow.HasModal || _season == null) return;
            if (index < 0 || index >= _season.Milestones.Count) return;

            var rung = _season.Milestones[index];
            if (!rung.Pays(track)) return;

            if (track == SeasonTrack.Pass && !Owned)
            {
                Scenery.Toast(Content, Loc.Get("ui.mark.pass_required"), Pal.Bloom, 2.4f);
                return;
            }

            if (SeasonLedger.IsClaimed(_season, rung, track)) return;

            if (_progress.Marks < rung.Goal)
            {
                Scenery.Toast(Content, Loc.Format("ui.mark.needs_more", rung.Goal - _progress.Marks),
                              Pal.Aqua, 2.2f);
                return;
            }

            if (!SeasonLedger.CanClaim)
            {
                Scenery.Toast(Content, Loc.Get("ui.chest.needs_connection"), Pal.Rose, 3f);
                return;
            }

            _claiming = true;

            Audio.Sfx("collect", .6f);

            var chest = _ladder.ChestOf(index, track);
            if (chest) Burst.Sparks(chest, Vector2.zero, Pal.Gold, 16, 300f, 26f, .6f);

            Flow.Modal<ChestOverlay>(v => v.Claim = ChestClaim.ForSeason(_season, rung, track));

            // The ledger raised Changed inside the overlay's Build; the repaint was held back
            // by _claiming so the row did not turn grey under the ceremony. Paint it now,
            // while the scrim covers it, and let the next Changed through.
            _claiming = false;
            Repaint();
        }

        // ------------------------------------------------------------------ painting
        /// <summary>
        /// Writes the ledger onto the hero, the pass row and the ladder. No entrance and no
        /// rebuild: a chest claimed behind this page moves a bar, and a bar moving is what a
        /// repaint is for.
        /// </summary>
        void Repaint()
        {
            if (this == null || _season == null || Content == null) return;

            _progress = GroveEvents.ProgressOf(_season);

            // The one transition this plate has: a first-ever sign-in landing while somebody is
            // standing here. It only ever goes down, never up, so nothing below it has to move.
            _connect?.Show(!SeasonLedger.CanClaim);

            PaintMark();

            if (_grown) _grown.text = Loc.Format("ui.mark.grown", _progress.Marks);

            if (_rungs)
                _rungs.text = Loc.Format("ui.tasks.fraction", _progress.Rungs,
                                         _season.Milestones.Count);

            if (_toNext)
            {
                _toNext.text = _progress.IsComplete
                    ? Loc.Get("ui.mark.complete")
                    : Loc.Format("ui.mark.to_next", _progress.ToNext);
                _toNext.color = _progress.IsComplete ? Pal.Gold : Pal.A(Pal.Cream, .82f);
            }

            // Orange while it is climbing, green the moment a rung is reached — and only on
            // the paint that changed it, or a tint restarts on every chest claimed. Instantly
            // the first time, because a page opened on a finished run should find it already
            // green rather than watch it arrive.
            bool full = _progress.IsComplete || _progress.ToNext01 >= 1f;
            if (_bar && _barFull != full)
            {
                var colour = full ? BarFull : BarOrange;
                if (_barFull == null) _bar.color = colour;
                else Tween.Tint(_bar, colour, .35f);
                _barFull = full;
            }

            if (_fill)
            {
                float target = (_track - 8f) * Mathf.Clamp01(_progress.ToNext01);
                Tween.KillChannel(_fill, "bar");
                float from = _fill.sizeDelta.x;
                Tween.Run(.6f, Ease.OutCubic,
                          t => { if (_fill) _fill.sizeDelta = new Vector2(Mathf.Lerp(from, target, t), BarH); },
                          _fill, "bar");
            }

            RefreshClock();
            RefreshPass();

            _ladder?.Repaint(_progress, Owned, force: true);
        }

        /// <summary>
        /// Opens the mark to where the ladder is, and only when that has moved. See
        /// <see cref="_markRungs"/> for why it is not simply repainted.
        /// </summary>
        void PaintMark()
        {
            if (!_mark || _markRungs == _progress.Rungs) return;
            _markRungs = _progress.Rungs;

            for (int i = _mark.childCount - 1; i >= 0; i--)
            {
                var child = _mark.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            int rungs = Mathf.Max(1, _season.Milestones.Count);
            SeasonCrest.Paint(_mark, _season.Icon, Pal.Bloom, _progress.Rungs / (float)rungs);
        }

        void RefreshClock()
        {
            if (!_clock || _season == null) return;

            long left = _season.SecondsLeftAt(GameClock.NowUnix());
            _clock.text = left <= 0 ? Loc.Get("ui.event.ended")
                                    : Loc.Format("ui.event.ends_in", Profile.LongCountdown(left));

            // Re-fitted on every write, because the size was chosen for the words that were in
            // it at the time: "41d 23h" and "23:59:07" are different lengths, and a countdown
            // spends its life changing between them. **From `ClockType` rather than from
            // wherever the last line left it**, or the fit only ever runs downhill.
            UIKit.OneLineLabel(_clock, ClockRoom, ClockType, ClockLeast);
        }

        /// <summary>
        /// The pass row: what it costs, or that it is held.
        ///
        /// The price is the season's own number and is known offline, which is the whole of
        /// what changed when the product became a gem price — the button never has to say
        /// "connecting", and there is no state in which this page can draw a pass it cannot
        /// sell.
        /// </summary>
        void RefreshPass()
        {
            if (_passBtn == null) return;

            bool owned = Owned;
            bool live = _season.IsLiveAt(GameClock.NowUnix());

            // The glyph changes rather than going away. `UIKit.FitLabel` centres the caption
            // and the glyph as one block on `Icon != null` — it does not read `enabled` — so
            // hiding it would leave "Unlocked" sitting left of centre with a gem's worth of
            // gap beside it. A tick is the right mark for the state anyway: trailing a
            // caption, a glyph is the *unit* on what the caption says, and what "Unlocked"
            // needs after it is a tick, not a price.
            if (_passBtn.Icon)
                _passBtn.Icon.sprite = Art.S(owned ? "Ui/ic_check" : "Ui/ic_gem");
            _passBtn.SetCaption(owned ? Loc.Get("ui.mark.unlocked")
                                      : Loc.Format("ui.mark.buy", Compact.Number(_season.PassGems)));
            _passBtn.Interactable = !owned && live;

            if (_passHint)
                _passHint.text = Loc.Get(owned ? "ui.mark.owned_hint"
                                       : !live ? "ui.mark.closed_hint"
                                       : "ui.mark.purchase_hint");

            if (_passCaption)
                _passCaption.color = owned ? Pal.Mint : Pal.Cream;

            // A value badge is a sales mark, so it goes the moment there is nothing to sell —
            // held, or the watch closed. Kept built rather than destroyed, because a cycle
            // rolls over under a resident page (invariant 47j) and the next season's badge is
            // the same widget with a different figure.
            if (_passValue)
                _passValue.gameObject.SetActive(!owned && live);
        }

        public override bool OnBack()
        {
            Flow.Go<HomeScreen>();
            return true;
        }
    }
}
