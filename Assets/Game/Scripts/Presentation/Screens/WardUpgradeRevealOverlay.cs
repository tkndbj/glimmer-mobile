using System;
using System.Collections.Generic;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The moment after a star is bought: the room charges, the star falls into the ladder, the
    /// turret takes the power, and the two figures it paid for climb in front of the player.
    ///
    /// <para>
    /// <b>It is a room rather than a panel, and that is the whole of what was wrong with it.</b>
    /// The first cut celebrated inside the parchment panel the purchase had just been made on - a
    /// small fan behind a star row, a couple of sparks, two short bars - and the owner's verdict
    /// after playing it was one word. Every piece of it was individually defensible and the whole
    /// was the smallest change on the screen at the largest moment in the feature, which is the
    /// fault <c>WardRevealOverlay</c>'s own note records about the purchase that used to celebrate
    /// itself in place, and the one <c>WheelPrizeOverlay</c> was built to fix. The dearest star on
    /// this shelf is a hundred and fifty thousand credits - days of play - and invariant 20m's
    /// rule is that the event is the reward and gets the biggest drawing there is.
    /// </para>
    /// <para>
    /// <b>What separates it from the turret's <em>unlock</em> is what it is about.</b> Those two
    /// ceremonies must not be one ceremony at two strengths, which is invariant 37z's complaint
    /// about two bosses told apart by a hue, asked of a celebration. An unlock is a <em>thing
    /// arriving</em>, so <c>WardRevealOverlay</c> stands the turret on a plate and lets it fire
    /// for ever under its name and its note. An upgrade is the same turret, and what changed is a
    /// star and two readings the player was looking at a second ago - so the <em>star</em> is the
    /// hero here: it falls out of the sky into the slot it bought, the light of the impact is what
    /// the turret takes, and the numbers are the last word rather than the first.
    /// </para>
    /// <para>
    /// <b>The numbers are still the payoff, which is what the first cut got right.</b> They are
    /// simply drawn at a size worth looking at (<c>WardStatBars</c>'s own scale, invariant 16l's
    /// rule about one design at whatever size there is room for) and each one is punched and
    /// marked with what it gained as it lands.
    /// </para>
    /// <para>
    /// <b>The room wears the seat's colour</b>, which is the unlock's rule: a turret is upgraded
    /// for one of the line's four colours, and which one is the fact a player most needs carried
    /// out of this screen.
    /// </para>
    /// <para>
    /// <b>And it makes exactly one sound</b>, on the break - see <see cref="Land"/>. What the
    /// player hears across the whole purchase is the coin as the money leaves and the victory as
    /// the star lands.
    /// </para>
    /// <para>
    /// <b>Its verb is an ascension.</b> The star falls as a comet, its power runs down a lightning
    /// conduit into the turret, and rises back up out of it in rings - the opposite direction to
    /// the unlock's summoning, where light is pulled into a point and goes nova outward. The light
    /// is <see cref="CeremonyFx"/>, pooled and additive on its own canvas, and every piece of it is
    /// generated, so nothing here can arrive as a white rectangle (invariant 7b).
    /// </para>
    /// </summary>
    public sealed class WardUpgradeRevealOverlay : ModalView
    {
        public WardModel Model { get; set; }

        /// <summary>Which seat of the line, as a colour index.</summary>
        public int Colour { get; set; }

        /// <summary>What it was worth, and what it is worth now.</summary>
        public WardBuild Was { get; set; }
        public WardBuild Now { get; set; }

        /// <summary>Raised when this closes, so the shelf behind repaints.</summary>
        public Action Changed { get; set; }

        // ------------------------------------------------------------------ bands
        /// <summary>
        /// Where each band sits, measured from the middle of the canvas with y running up.
        ///
        /// <b>Internal rather than private because nothing in this project can look at this
        /// screen.</b> There is a render for the hub, the shop, the siege and Prismvale, and every
        /// one of them has caught faults each numeric gate was green through; there is none for a
        /// modal ceremony, so the fault that would otherwise ship unseen is a caption drawn
        /// through the thing above it. <c>WardRevealTests</c> walks these edges for the unlock and
        /// does the same here - a poor substitute for a picture, and what there is.
        /// </summary>
        internal const float TitleY = 660f, NameY = 534f, StarsY = 410f, PlateY = 40f,
                             BarsY = -430f, ActY = -724f;

        /// <summary>How tall each of those bands is drawn.</summary>
        internal const float TitleH = 120f, NameH = 64f, StarsH = 104f, PlateH = 560f,
                             ActH = 150f;

        internal const float PlateW = 620f, ActW = 520f;

        /// <summary>The star ladder, drawn far larger than the shelf's own row.</summary>
        internal const float StarSize = 92f;

        /// <summary>The bars, and the multiple of their design they are drawn at.</summary>
        internal const float BarsW = 780f, BarScale = 1.35f;

        /// <summary>How tall the pair really is once scaled, which is what the bands are checked against.</summary>
        internal static float BarsH => WardStatBars.HeightAt(BarScale);

        const float StageW = 560f, StageH = 500f, StageCell = 88f;

        /// <summary>
        /// Where the room's own light is centred.
        ///
        /// <b>Between the ladder and the turret rather than on either.</b> The two things this
        /// ceremony is about stand 370 apart, and a fan centred on one of them says the other is
        /// scenery - so the light sits between, and what tells the eye where to look is the star
        /// falling and the turret flaring, which are events rather than lighting.
        /// </summary>
        const float LightY = 210f;

        const float FanSize = 1180f;

        const float VignetteAlpha = CeremonySky.VignetteAlpha, FanAlpha = .30f, Fan2Alpha = .20f, GlowAlpha = .40f;

        /// <summary>
        /// Its own hold, never the board's - <see cref="WardFiringStage"/>'s
        /// rule and its reason: a live board's four turrets must not be released because a
        /// celebration closed. Its own name and not the unlock's, so the two can never be up at
        /// once and release each other's art.
        /// </summary>
        const string UpgradeScope = "ward_upgrade";

        /// <summary>How flat a ring lifting off the turret lies - the hill's own perspective.</summary>
        const float FloorSquash = .34f;

        /// <summary>How dark the room is held under the charge. Lifted on the landing.</summary>
        const float HushAlpha = .46f;

        // The light's sprites, generated and cached by key in `Art`; named here so the pool is
        // warmed with exactly what the ceremony throws.
        static Sprite GlowArt => Art.Glow(64, 1.6f);
        static Sprite SparkArt => Art.Spark(64);
        static Sprite GlintArt => Art.Glint(128, 4);
        static Sprite WaveArt => Art.Wave(256, 9f);
        static Sprite RingArt => Art.Ring(256, 5f);

        // ----------------------------------------------------------------- timing
        /// <summary>
        /// The sequence, as gaps rather than absolute times - <see cref="Cue"/>'s whole argument.
        ///
        /// <b>The room charges, the star lands, the power runs into the turret, and the numbers
        /// climb.</b> The charge is the part that cannot be cut: it is the only stretch where
        /// nothing has happened yet, and taking it out turns the impact from something earned into
        /// something sudden. The numbers come last because they are the reading a player has to be
        /// looking at when the noise has stopped.
        /// </summary>
        const float ChargeFor = .58f, RingGap = .13f, FallFor = .30f;
        const float ToConduit = .16f, ToSurge = .22f, ConduitFor = .50f;
        const float ToTitle = .24f, ToName = .16f, ToBars = .26f, ToKey = .30f;
        const float Climb = 1.10f;

        static readonly Color Ink = Pal.Cream;

        /// <summary>
        /// What the gain beside a climbing bar is written in.
        ///
        /// <b>The bright green rather than <c>WardStatBars</c>' own dark one</b>, because the two
        /// are drawn on opposite grounds: that one sits on cream parchment and this on a room
        /// taken almost to black. The rule is the contrast against what is behind it, never the
        /// hue - which is the same correction invariant 37l records about a tint that could only
        /// ever darken.
        /// </summary>
        static readonly Color GainInk = Pal.Mint;

        Image _sky, _vignette, _hush, _fan, _fan2, _glow, _flash, _plate;
        Image[] _aurora;
        RectTransform _plateRt, _fanRt, _fan2Rt, _ladder, _barHost;
        RectTransform _falling;
        bool _relit;
        Text _title, _name;
        Btn _act;
        CanvasGroup _dismiss;
        RectTransform _actRt;

        WardStatBars _bars;
        WardFiringStage _stage;

        CeremonyFx _fx;
        bool _ambient;

        /// <summary>The spark riding each bar's leading edge while it climbs.</summary>
        readonly List<RectTransform> _heads = new List<RectTransform>();

        /// <summary>
        /// Every one-shot piece of light still alive, so a skip can take them all away at once - a
        /// flare left burning over a settled screen is the leak <see cref="Skip"/> exists to make
        /// impossible.
        /// </summary>
        readonly List<GameObject> _transient = new List<GameObject>();

        Chroma _c;
        Color _tint;
        bool _settled;

        // ------------------------------------------------------------------ build
        protected override void Build()
        {
            if (Model == null || !Now.Has) { Close(); return; }

            _tint = SiegeView.TintOf(Colour);
            _c = SchemeFor(_tint);

            BuildRoom();
            BuildTurret();

            // Over the plate and under the ladder and captions, so light can burn across the
            // turret and never across a word or a star. Warmed now, while the room fades in.
            _fx = CeremonyFx.Attach(Content, "Light",
                                    (GlowArt, 110), (SparkArt, 160), (GlintArt, 60),
                                    (WaveArt, 10), (RingArt, 8));

            BuildLadder();
            BuildCaptions();
            BuildBars();
            BuildButtons();

            // Above everything, and last, so the impact whites out the whole composition.
            _flash = UIKit.Img("Flash", Content, Art.Pixel, new Color(1f, .99f, .94f, 0f));
            UIKit.StretchTo((RectTransform)_flash.transform, 0, 0, 0, 0);
            _flash.raycastTarget = false;

            Play();
        }

        void OnDestroy() => Changed?.Invoke();

        /// <summary>
        /// The room's three-colour scheme, built around the seat.
        ///
        /// <c>WardRevealOverlay.SchemeFor</c>'s arithmetic, deliberately: the two ceremonies
        /// differ in what they do and must not differ in what red <em>means</em>.
        /// </summary>
        static Chroma SchemeFor(Color tint)
        {
            Color.RGBToHSV(tint, out float h, out float s, out float v);

            var partner = Color.HSVToRGB(Mathf.Repeat(h + .18f, 1f), Mathf.Min(1f, s * .86f), v);
            var accent = Color.HSVToRGB(Mathf.Repeat(h - .10f, 1f), Mathf.Min(1f, s * .70f),
                                        Mathf.Min(1f, v * 1.12f));
            var deep = Color.HSVToRGB(h, Mathf.Min(1f, s * .92f), v * .16f);

            return new Chroma(tint, partner, accent, deep);
        }

        // ------------------------------------------------------------------- room
        void BuildRoom()
        {
            // The bottom layer, covering the screen, so a tap anywhere that is not a control
            // lands here and skips to the end.
            //
            // Shared with the rank ceremony and the turret reveal (`CeremonySky`), which is why
            // it is not the seat's own deep hue any more: all three stood their subject on a
            // near-black room and all three came back as *so dark*. The seat's colour is still
            // on the fans, the light and the rim below - the ground changed, not the lighting.
            _sky = CeremonySky.Ground(Content, Skip);

            BuildAurora();

            Fireflies.Spawn(Content, 12, Pal.A(_c.Partner, .55f), 4f, 12f);

            // Last of the ground, so it holds the aurora and the fireflies in too.
            _vignette = CeremonySky.Veil(Content);

            // The hush: dimmed under the charge, uncovered on the landing. In the wash's own ink,
            // for `CeremonySky.Ink`'s reason. See `Play`.
            _hush = UIKit.Img("Hush", Content, Art.Pixel, Pal.A(CeremonySky.Ink, 0f));
            UIKit.StretchTo((RectTransform)_hush.transform, 0, 0, 0, 0);
            _hush.raycastTarget = false;

            _fan = UIKit.Img("Fan", Content, Art.Rays(512, 20), Pal.A(_tint, 0f),
                             Vector2.one * FanSize, new Vector2(.5f, .5f),
                             new Vector2(0f, LightY));
            _fanRt = (RectTransform)_fan.transform;
            _fanRt.localScale = Vector3.zero;
            _fan.raycastTarget = false;

            _fan2 = UIKit.Img("Fan2", Content, Art.Rays(256, 8), Pal.A(_c.Partner, 0f),
                              Vector2.one * (FanSize * .72f), new Vector2(.5f, .5f),
                              new Vector2(0f, LightY));
            _fan2Rt = (RectTransform)_fan2.transform;
            _fan2Rt.localScale = Vector3.zero;
            _fan2.raycastTarget = false;

            _glow = UIKit.Img("Glow", Content, Art.Glow(256, 1.7f), Pal.A(_tint, 0f),
                              Vector2.one * 940f, new Vector2(.5f, .5f), new Vector2(0f, LightY));
            _glow.raycastTarget = false;
        }

        // Where the three masses of light sit - a composition rather than a scatter, so the frame
        // is lit unevenly the way a place is. The unlock's table, and its reasoning.
        static readonly Vector2[] AuroraHome =
        {
            new Vector2(-340f, 600f), new Vector2(380f, 90f), new Vector2(-240f, -620f),
        };

        static readonly float[] AuroraSize = { 1140f, 960f, 1200f };
        static readonly float[] AuroraAlpha = { .19f, .15f, .12f };

        void BuildAurora()
        {
            _aurora = new Image[3];

            for (int i = 0; i < 3; i++)
            {
                _aurora[i] = UIKit.Img("Aurora" + i, Content, Art.Glow(256, 1.7f),
                                       Pal.A(_c.Nth(i + 1), 0f), Vector2.one * AuroraSize[i],
                                       new Vector2(.5f, .5f), AuroraHome[i]);
                _aurora[i].raycastTarget = false;
                Drift(i);
            }
        }

        /// <summary>
        /// One blob's endless wander. Both axes are whole multiples of the loop, or the drift
        /// snaps back every time the tween wraps - which on something this large is the most
        /// visible thing on screen.
        /// </summary>
        void Drift(int index)
        {
            var blob = _aurora[index];
            var home = AuroraHome[index];
            float span = 44f + index * 12f;
            float period = 15f + index * 4f;

            Tween.Run(period, Ease.Linear, t =>
            {
                if (!blob) return;

                float a = t * Mathf.PI * 2f;
                ((RectTransform)blob.transform).anchoredPosition =
                    home + new Vector2(Mathf.Sin(a) * span, Mathf.Cos(a * 2f) * span * .6f);
            }, blob, "drift").Loop(-1, false);
        }

        // ----------------------------------------------------------------- turret
        /// <summary>
        /// The plate the turret stands on, firing.
        ///
        /// <b>It is here on the strength of what an upgrade <em>is</em>.</b> A turret's power is
        /// its bolt - that is the argument <see cref="WardFiringStage"/> is built on and the
        /// reason nineteen projectiles were baked - so a ceremony about a turret getting stronger
        /// with no turret on it would be celebrating a number rather than a thing. It arrives
        /// already standing rather than springing up, which is what keeps it from being the
        /// unlock: nothing new turned up here.
        /// </summary>
        void BuildTurret()
        {
            _plate = UIKit.Img("Plate", Content, Art.Round(34),
                               Pal.A(Color.Lerp(_c.Deep, Color.black, .38f), .96f),
                               new Vector2(PlateW, PlateH), new Vector2(.5f, .5f),
                               new Vector2(0f, PlateY));
            _plateRt = (RectTransform)_plate.transform;
            _plate.raycastTarget = false;

            // What ModalView.Close() scales out. Every overlay that skips MakePanel has to name
            // its own Panel: leaving it null throws out of the click handler *after* the content
            // has faded and *before* Flow.Dismiss runs, stranding an invisible full-screen
            // blocker with the close already latched. The unlock's note, verbatim, because this
            // is the third screen that would otherwise walk into it.
            Panel = _plateRt;

            var rim = UIKit.Img("Rim", _plate.transform, Art.RoundOutline(34, 5f),
                                Pal.A(_tint, .90f));
            UIKit.StretchTo((RectTransform)rim.transform, 0, 0, 0, 0);
            rim.raycastTarget = false;

            _stage = WardFiringStage.Attach(_plateRt, new Vector2(StageW, StageH),
                                            new Vector2(.5f, .5f), Vector2.zero,
                                            StageCell, UpgradeScope);
            _stage.Show(Model, Colour);

            var group = UIKit.Group(_plateRt);
            group.alpha = 0f;
            _plateRt.localScale = Vector3.one * .86f;
        }

        // ----------------------------------------------------------------- ladder
        /// <summary>
        /// The star ladder, hosted in a box of its own.
        ///
        /// <b>A box rather than <c>Content</c> directly</b>, because <c>WardStarRow</c> anchors to
        /// its parent's <em>top</em> - pointed at a full-screen layer that is the top of the
        /// display, which moves with every screen shape. Hosted, the row is centred on a band this
        /// file states.
        /// </summary>
        void BuildLadder()
        {
            _ladder = UIKit.Box("Ladder", Content,
                                new Vector2(WardStarRow.Width(StarSize), StarSize),
                                new Vector2(.5f, .5f), new Vector2(0f, StarsY));

            WardStarRow.Build(_ladder, new Vector2(0f, -StarSize * .5f), Was.Stars, StarSize);
        }

        void BuildCaptions()
        {
            _title = UIKit.Shrinkable(
                UIKit.Titled("Title", Content, Loc.Get("ui.loadout.upgrade_done"), 96, Pal.Gold,
                             TextAnchor.MiddleCenter, new Vector2(940f, TitleH),
                             new Vector2(.5f, .5f), new Vector2(0f, TitleY), 5f, 6f), 48);
            SetAlpha(_title, 0f);
            _title.transform.localScale = Vector3.zero;

            _name = UIKit.Shrinkable(
                UIKit.Titled("Name", Content, Loc.Get(Model.NameKey), 44,
                             Pal.A(Pal.Cream, .92f), TextAnchor.MiddleCenter,
                             new Vector2(880f, NameH), new Vector2(.5f, .5f),
                             new Vector2(0f, NameY), 3f, 4f), 26);
            SetAlpha(_name, 0f);
        }

        /// <summary>
        /// The two readings, on a host of their own for <see cref="BuildLadder"/>'s reason.
        ///
        /// <b>Drawn at <see cref="BarScale"/> rather than at the panel's size</b>, because this is
        /// the one screen whose entire subject is these two numbers moving. They start at what the
        /// turret was worth, so the climb is something the player watches happen rather than a
        /// state they are shown.
        /// </summary>
        void BuildBars()
        {
            _barHost = UIKit.Box("Bars", Content, new Vector2(BarsW, BarsH),
                                 new Vector2(.5f, .5f), new Vector2(0f, BarsY));

            _bars = WardStatBars.Build(_barHost, BarsH * .5f, BarsW, Ink, scale: BarScale);
            _bars.Set(Was, WardLedger.Catalog);

            var group = UIKit.Group(_barHost);
            group.alpha = 0f;
        }

        void BuildButtons()
        {
            // Green, never the price pill: the money has already changed hands and this key only
            // leaves. `Skins.Buy` is orange because it means *this costs something*, so wearing it
            // here would be asking for a second payment - invariant 42a's own note about what the
            // colour names mean.
            _act = UIKit.Button("Act", Content, Art.S("Ui/" + Skins.Settled),
                                new Vector2(ActW, ActH), new Vector2(.5f, .5f),
                                new Vector2(0f, ActY), () => Close());

            _actRt = (RectTransform)_act.transform;
            _actRt.localScale = Vector3.zero;

            UIKit.Shrinkable(
                UIKit.Titled("Label", _act.transform, Loc.Get("ui.ok.done"), 44, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(380f, 64f),
                             new Vector2(.5f, .5f),
                             new Vector2(0f, ActH * UIKit.PillFaceLift), 0f, 3f), 26);

            // Appears with the key rather than at the start. Before the payoff there is nothing to
            // dismiss and a cross would only invite skipping past the thing just paid for; tapping
            // the room already skips, which is the affordance that matters early.
            var cross = UIKit.IconButton("Dismiss", Content, Skins.Nav, "ic_close",
                                         new Vector2(84f, 84f), new Vector2(1f, 1f),
                                         new Vector2(-72f, -96f), () => Close());

            _dismiss = UIKit.Group((RectTransform)cross.transform);
            _dismiss.alpha = 0f;
            _dismiss.blocksRaycasts = false;
        }

        // ------------------------------------------------------------------- play
        /// <summary>
        /// The sequence. Gaps, never absolute times - <see cref="Cue"/>'s whole argument.
        ///
        /// <para>
        /// <b>Five movements, and each one hands the light to the next.</b> The ladder wakes - a
        /// shimmer runs along the stars already earned to the empty slot, and light is dragged
        /// into it. The star falls out of the sky as a comet and lands in the slot. Its power runs
        /// <em>down</em> a lightning conduit into the turret. The turret takes it and the power
        /// rises back up out of it in rings. And the two readings climb, each with a spark riding
        /// its leading edge. One continuous path of light from the top of the screen to the
        /// numbers - which is what makes this a story about the turret getting stronger rather
        /// than a star and a turret that happened to be on screen together.
        /// </para>
        /// <para>
        /// <b>The room hushes under the charge and is uncovered on the landing</b>, the unlock's
        /// trick and its reason: additive light on a bright room has nowhere to go.
        /// </para>
        /// </summary>
        void Play()
        {
            var cue = new Cue(this);

            // -- the room -----------------------------------------------------
            cue.With(() =>
            {
                Tween.Fade(_sky, 1f, .28f);
                Tween.Fade(_vignette, VignetteAlpha, .38f);
                Tween.Fade(_hush, HushAlpha, .45f, Ease.OutCubic);

                if (_aurora != null)
                    for (int i = 0; i < _aurora.Length; i++)
                        Tween.Fade(_aurora[i], AuroraAlpha[i], .66f);

                Tween.Fade(UIKit.Group(_plateRt), 1f, .30f);
                Tween.Scale(_plateRt, 1f, .42f, Ease.OutBack);
            });

            // -- the charge ---------------------------------------------------
            // The ladder wakes along its earned stars toward the slot, and light is dragged into
            // the slot out of the dark. Anticipation is the only thing on screen for half a
            // second, which is what makes the landing feel earned rather than sudden.
            cue.Then(.12f, () =>
            {
                Shimmer();
                Charge(ChargeFor - .12f);
            });

            cue.Then(.06f, () => Collapse(0));
            cue.Then(RingGap, () => Collapse(1));
            cue.Then(RingGap, () => Collapse(2));

            // -- the star falls -----------------------------------------------
            cue.Then(ChargeFor - RingGap * 2f - .18f, Drop);

            // -- the landing --------------------------------------------------
            cue.Then(FallFor, Land);

            // -- the conduit --------------------------------------------------
            cue.Then(ToConduit, Conduit);

            // -- the turret takes it ------------------------------------------
            cue.Then(ToSurge, Surge);

            // -- the title ----------------------------------------------------
            cue.Then(ToTitle, () =>
            {
                SetAlpha(_title, 1f);
                _title.transform.localScale = Vector3.one * 2.4f;
                Tween.Scale(_title.transform, 1f, .26f, Ease.InCubic)
                     .OnDone(() =>
                     {
                         Tween.Punch(_title.transform, .18f, .34f);
                         Underline(new Vector2(0f, TitleY - TitleH * .30f), TitleW(), Pal.Lift(Pal.Gold, .25f));
                     });
            });

            cue.Then(ToName, () => Tween.Fade(_name, .92f, .30f));

            // -- and what it bought -------------------------------------------
            cue.Then(ToBars, () =>
            {
                Tween.Fade(UIKit.Group(_barHost), 1f, .26f);
                _bars?.Climb(Was, Now, WardLedger.Catalog, Climb);
                Ride();
            });

            cue.Then(Climb, Landed);

            cue.Then(ToKey, () =>
            {
                Tween.Pop(_actRt, .6f, .44f);
                Tween.Fade(_dismiss, 1f, .28f);
                _dismiss.blocksRaycasts = true;

                _settled = true;
                Ambient();
            });
        }

        // ------------------------------------------------------------------ parts
        /// <summary>
        /// A glint running along the stars already held, left to right, ending on the slot - the
        /// ladder saying where the next one goes before it arrives.
        /// </summary>
        void Shimmer()
        {
            int held = WardStars.Sane(Was.Stars);

            for (int i = 0; i < held; i++)
                Twinkle(new Vector2(WardStarRow.XOf(i, StarSize), StarsY), Pal.A(Pal.Lift(Pal.Gold, .45f), .95f),
                        StarSize * 1.9f, .42f, i * .07f);

            Twinkle(SlotAt, Pal.A(Color.white, .9f), StarSize * 2.4f, .5f, held * .07f);
        }

        /// <summary>
        /// Light dragged in out of the dark along spirals, into the slot the star is about to land
        /// in, and a glow in the slot swelling as it fills.
        ///
        /// <b>Inward rather than outward</b>, which is the one thing that makes this read as a
        /// charge: everything else in this game that throws light throws it away from something.
        /// <b>And into the <em>slot</em> rather than into the turret</b>, so the gathering and the
        /// thing it is gathering for share one place.
        /// </summary>
        void Charge(float seconds)
        {
            var to = SlotAt;
            var golds = new[] { Pal.Lift(Pal.Gold, .3f), Pal.Lift(_tint, .4f), Pal.Lift(_c.Partner, .35f), Color.white };

            _fx.Vortex(GlowArt, to, golds, 70f, seconds, 260f, 620f, 22f, 3f);
            _fx.Vortex(SparkArt, to, golds, 30f, seconds, 200f, 520f, 15f, 3.6f);

            var well = _fx.Light("Well", Art.Glow(128, 1.8f), Pal.A(Pal.Lift(Pal.Gold, .3f), 0f),
                                 Vector2.one * 40f, to);
            _transient.Add(well.gameObject);

            var rt = well.rectTransform;
            Tween.Run(seconds + FallFor, Ease.Linear, t =>
            {
                if (!well) return;

                // Swelling, and flickering faster the fuller it gets - a light about to give.
                float pulse = .82f + .18f * Mathf.Sin(t * t * 60f);
                rt.sizeDelta = Vector2.one * Mathf.Lerp(40f, 230f, t * t) * pulse;
                well.color = Pal.A(well.color, Mathf.Lerp(.2f, .95f, t));
            }, well);
        }

        /// <summary>A ring closing on the slot, before there is anything there to close on.</summary>
        void Collapse(int index)
        {
            _fx.Emit(new CeremonyFx.Mote
            {
                Sprite = WaveArt, Colour = Pal.A(Pal.Lift(_c.Nth(index), .25f), .72f), At = SlotAt,
                Life = .46f, Size = 1080f, SizeEnd = 170f, Grow = Ease.InCubic, FadeIn = .4f, FadePower = .6f,
            });
        }

        /// <summary>Where in the ladder the star being bought stands, on the canvas.</summary>
        Vector2 SlotAt => new Vector2(WardStarRow.XOf(WardStars.Sane(Now.Stars) - 1, StarSize),
                                      StarsY);

        /// <summary>
        /// Where the turret's middle stands, read off the stage - which stands its turret low and
        /// keeps the top for the bolt, so the plate's own centre is air above it.
        /// </summary>
        Vector2 TurretAt => new Vector2(0f, PlateY) + (_stage != null ? _stage.TurretPoint : Vector2.zero);

        /// <summary>The turret's feet, where the rings it sends up are born.</summary>
        Vector2 FootAt => TurretAt + new Vector2(0f, -StageCell * 1.0f);

        /// <summary>
        /// The star itself, falling into the slot it was bought for - as a comet.
        ///
        /// <b>It arrives from off the top of the canvas</b> rather than fading up in place, so
        /// there is a moment where the thing about to happen is visible and has not happened -
        /// which is the whole difference between a reward landing and a readout changing. Where it
        /// lands is <see cref="WardStarRow.XOf"/> and never this file's own arithmetic, or the
        /// star would stand beside the one it is supposed to become the day either the size or the
        /// gap moves.
        ///
        /// <b>The tail is particles shed where the star <em>is</em></b>, never a picture hung behind
        /// it: the star turns as it falls, and a tail parented to it would turn with it.
        /// </summary>
        void Drop()
        {
            var slot = SlotAt;
            var from = new Vector2(slot.x, 1180f);

            // **One node carrying both**, rather than a star and a halo moved in step. Two objects
            // are two things a skip has to remember to take away, and the one it forgot would be
            // left as a gold smear over a ladder that had already relit.
            _falling = UIKit.Box("Falling", Content, Vector2.one * StarSize,
                                 new Vector2(.5f, .5f), from);

            var halo = UIKit.Img("Glow", _falling, Art.Glow(128, 1.8f), Pal.A(Pal.Gold, 0f),
                                 Vector2.one * StarSize * 3.4f, new Vector2(.5f, .5f),
                                 Vector2.zero);
            halo.raycastTarget = false;
            Additive.Lit(halo);

            var star = UIKit.Img("Star", _falling, Art.S("Ui/star_full"), Pal.Gold,
                                 Vector2.one * StarSize, new Vector2(.5f, .5f), Vector2.zero);
            star.raycastTarget = false;

            var node = _falling;

            Tween.Fade(halo, .75f, FallFor * .5f);

            Tween.Run(FallFor, Ease.InQuad, t =>
            {
                if (!node) return;

                node.anchoredPosition = Vector2.Lerp(from, slot, t);
                node.localScale = Vector3.one * Mathf.Lerp(2.6f, 1f, t);
                node.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-160f, 0f, t));
            }, _falling).OnDone(() =>
            {
                if (node) Destroy(node.gameObject);
                if (_falling == node) _falling = null;
            });

            var tail = new[] { Pal.Lift(Pal.Gold, .35f), Pal.Lift(Pal.Gold, .1f), Color.white, Pal.Lift(_tint, .45f) };

            _fx.Run(120f, FallFor, i =>
            {
                if (!node) return default;

                bool ember = i % 3 == 0;
                return new CeremonyFx.Mote
                {
                    Sprite = ember ? SparkArt : GlowArt,
                    Colour = tail[i % tail.Length],
                    At = node.anchoredPosition + new Vector2(UnityEngine.Random.Range(-14f, 14f), 10f),
                    Velocity = new Vector2(UnityEngine.Random.Range(-90f, 90f), UnityEngine.Random.Range(80f, 260f)),
                    Life = ember ? UnityEngine.Random.Range(.35f, .6f) : UnityEngine.Random.Range(.22f, .36f),
                    Size = ember ? 18f : StarSize * node.localScale.x * .9f,
                    SizeEnd = ember ? 4f : 12f,
                    Gravity = ember ? 500f : 0f,
                    Drag = 2f,
                    Stretch = ember ? .08f : 0f,
                    FadePower = 1.3f,
                };
            });
        }

        /// <summary>
        /// The impact: the star is in the ladder, the room is uncovered, and the light of the
        /// landing bursts out of the slot.
        ///
        /// <b>One sound in the whole ceremony, and it is here.</b> It shipped with five - a
        /// whoosh under the charge, a bell on the star, the fanfare on the title, a chime under
        /// the climbing bars and a collect when they landed - each one placed on a beat that
        /// deserved marking, and played back as a pile-up rather than as a celebration. The
        /// owner's verdict after a device was to keep the victory and nothing else. The conduit
        /// and the surge after it are silent on purpose: they are the landing's consequence, and
        /// a second sound would make them a second event.
        /// </summary>
        void Land()
        {
            _flash.color = new Color(1f, .99f, .94f, .90f);
            Tween.Fade(_flash, 0f, .36f, Ease.OutQuad);

            Tween.Fade(_hush, 0f, .34f, Ease.OutQuad);

            Tween.Shake((RectTransform)Content, 30f, .46f);
            Zoom(1.06f, .5f);
            Audio.Sfx("win", .62f);

            ClearTransient();

            _fanRt.localScale = Vector3.one * .35f;
            Tween.Scale(_fanRt, 1f, .68f, Ease.OutQuint);
            Tween.Fade(_fan, FanAlpha, .52f);
            Spin();

            _fan2Rt.localScale = Vector3.one * .30f;
            Tween.Scale(_fan2Rt, 1f, .80f, Ease.OutQuint);
            Tween.Fade(_fan2, Fan2Alpha, .58f);
            Counterspin();

            Tween.Fade(_glow, GlowAlpha, .48f);

            var at = SlotAt;
            var gold = Pal.Lift(Pal.Gold, .3f);

            // The flare: a hot core, a turning star of needles, and a streak thrown along the ladder.
            Flare(Art.Glow(128, 1.6f), at, Pal.A(Color.white, .95f), Vector2.one * 90f,
                  Vector2.one * 760f, 0f, 0f, .55f, Ease.OutQuint);
            Flare(GlintArt, at, Pal.A(gold, 1f), Vector2.one * 220f, Vector2.one * 1150f, 0f, 32f,
                  .72f, Ease.OutCubic);
            Flare(Art.Glow(64, 1.4f), at, Pal.A(gold, .85f), new Vector2(1500f, 64f),
                  new Vector2(1900f, 8f), 0f, 0f, .58f, Ease.OutCubic);

            for (int i = 0; i < 4; i++)
                _fx.Shock(WaveArt, at, Pal.A(Pal.Lift(_c.Nth(i), .25f), .82f), 120f, 1300f + i * 140f,
                          .74f, i * .07f);

            var bright = new[] { Color.white, gold, Pal.Lift(_tint, .4f), Pal.Lift(_c.Accent, .35f) };

            _fx.Spray(SparkArt, at, bright, 64, 600f, 1500f, 26f, .9f, gravity: 1200f, drag: 2.5f,
                      stretch: .055f, lift: 220f);
            _fx.Spray(GlowArt, at, bright, 22, 220f, 600f, 28f, 1.3f, gravity: 280f, drag: 1.6f,
                      stretch: 0f, lift: 120f);

            Crackle(at, 50f, 380f, 6, gold, 5f, .30f, 3);

            _fx.Starfall(GlintArt, 520f, 1000f, new[] { gold, Pal.Cream, Pal.Lift(_tint, .5f) }, 22f, 2.2f, 44f);

            Relight();
        }

        /// <summary>
        /// The star's power running down into the turret: a bolt from the slot to the turret's
        /// middle, struck three times and flickering out, with beads of light pouring down it.
        ///
        /// <b>This is the beat the first two cuts were missing.</b> Without it the star landing and
        /// the turret flaring were two events that happened to be close together - the very
        /// complaint this file's charge note records about light split across two places. With it
        /// they are cause and effect, drawn.
        /// </summary>
        void Conduit()
        {
            var from = SlotAt + new Vector2(0f, -StarSize * .45f);
            var to = TurretAt;
            var gold = Pal.Lift(Pal.Gold, .2f);

            var bolt = Lightning.Grow("Conduit", _fx.Node, gold, 7f, .55f);
            _transient.Add(bolt.Host.gameObject);

            int seed = UnityEngine.Random.Range(1, 1 << 30);
            int struck = 0;

            bolt.Strike(from, to, 100f, .38f, 3, seed);
            bolt.Reveal = 0f;

            var host = bolt.Host;
            var group = bolt.Group;

            // The leader opening down the channel, then re-strikes on new seeds, then the decay.
            Tween.Run(ConduitFor, Ease.Linear, t =>
            {
                if (!host) return;

                bolt.Reveal = Mathf.Clamp01(t / .18f);

                int k = Mathf.Min(3, Mathf.FloorToInt(t * 6f));
                if (k != struck)
                {
                    struck = k;
                    bolt.Strike(from, to, 100f, .38f, 3, seed + k * 7919);
                    if (t >= .18f) bolt.Reveal = 1f;
                }

                group.alpha = t < .5f ? 1f : (1f - (t - .5f) / .5f) * (.75f + .25f * Mathf.Sin(t * 80f));
            }, this).OnDone(() => { if (host) Destroy(host.gameObject); });

            // Beads pouring down the channel, so the power is seen to *travel*.
            var beads = new[] { Color.white, gold, Pal.Lift(_tint, .5f) };
            for (int i = 0; i < 14; i++)
            {
                float delay = i * .018f;
                float life = ToSurge - delay + .02f;

                _fx.Emit(new CeremonyFx.Mote
                {
                    Sprite = i % 2 == 0 ? GlowArt : SparkArt,
                    Colour = beads[i % beads.Length],
                    At = from + new Vector2(UnityEngine.Random.Range(-16f, 16f), 0f),
                    Velocity = (to - from) / Mathf.Max(.05f, life),
                    Life = life,
                    Delay = delay,
                    Size = i % 2 == 0 ? 34f : 22f,
                    SizeEnd = 14f,
                    Stretch = .05f,
                    FadeIn = .15f,
                    FadePower = .4f,
                });
            }
        }

        /// <summary>
        /// The turret takes the power, and it rises back up out of it - rings lifting off its feet
        /// one after another, a column standing up behind it, sparks streaming upward.
        ///
        /// <b>Up, because this is an upgrade.</b> The unlock's light goes <em>out</em>, from a
        /// point, because something arrived; this goes <em>up</em>, out of a thing that was already
        /// there, because it became more. The rings are squashed to the floor's perspective, so
        /// they read as halos lifting off the turret rather than as targets drawn on the screen.
        /// </summary>
        void Surge()
        {
            var at = TurretAt;
            var foot = FootAt;
            var lift = Pal.Lift(_tint, .45f);
            var gold = Pal.Lift(Pal.Gold, .3f);

            Zoom(1.03f, .36f);

            // The stage's own overcharge - a halo, rings out of the barrel and a spring - with its
            // white-out off, because a flash here would be a second break.
            _stage?.Claim(whiteOut: false);

            Flare(GlintArt, at, Pal.A(lift, 1f), Vector2.one * 140f, Vector2.one * 760f, 0f, -30f,
                  .62f, Ease.OutCubic);
            Flare(Art.Glow(128, 1.6f), at, Pal.A(Color.white, .8f), Vector2.one * 80f,
                  Vector2.one * 520f, 0f, 0f, .45f, Ease.OutQuint);

            Column(foot, lift);

            for (int i = 0; i < 5; i++)
                _fx.Emit(new CeremonyFx.Mote
                {
                    Sprite = RingArt,
                    Colour = Pal.A(i % 2 == 0 ? lift : gold, .95f),
                    At = foot,
                    Velocity = new Vector2(0f, 760f),
                    Drag = 2.4f,
                    Life = .78f,
                    Delay = i * .085f,
                    Size = 150f,
                    SizeEnd = 380f + i * 30f,
                    Grow = Ease.OutCubic,
                    Squash = FloorSquash,
                    FadeIn = .12f,
                    FadePower = 1.5f,
                });

            var up = new[] { Color.white, lift, gold, Pal.Lift(_c.Partner, .4f) };

            _fx.Run(80f, .7f, i => new CeremonyFx.Mote
            {
                Sprite = i % 3 == 0 ? GlowArt : SparkArt,
                Colour = up[i % up.Length],
                At = foot + new Vector2(UnityEngine.Random.Range(-90f, 90f), UnityEngine.Random.Range(-10f, 30f)),
                Velocity = new Vector2(UnityEngine.Random.Range(-60f, 60f), UnityEngine.Random.Range(480f, 980f)),
                Life = UnityEngine.Random.Range(.45f, .8f),
                Size = UnityEngine.Random.Range(14f, 26f),
                SizeEnd = 4f,
                Gravity = -120f,
                Drag = 1.4f,
                Stretch = .06f,
                FadeIn = .1f,
                FadePower = 1.2f,
            });

            _fx.Shock(WaveArt, at, Pal.A(lift, .8f), 100f, 900f, .6f);
        }

        /// <summary>The ladder at its new count, with the star that was just bought punched where it stands.</summary>
        void Relight()
        {
            if (_ladder == null || _relit) return;

            _relit = true;

            for (int i = _ladder.childCount - 1; i >= 0; i--)
                Destroy(_ladder.GetChild(i).gameObject);

            var row = WardStarRow.Build(_ladder, new Vector2(0f, -StarSize * .5f), Now.Stars,
                                        StarSize);

            int lit = WardStars.Sane(Now.Stars);
            var earned = row.childCount >= lit && lit > 0 ? row.GetChild(lit - 1) : null;

            if (earned == null) return;

            Tween.Pop(earned, .30f, .46f);

            // The stars already held answer the landing, nearest first - a ripple out of the slot.
            for (int i = 0; i < lit - 1 && i < row.childCount; i++)
            {
                var other = row.GetChild(i);
                Tween.After((lit - 1 - i) * .06f, () => { if (other) Tween.Punch(other, .22f, .36f); }, this);
            }
        }

        /// <summary>
        /// A spark riding the leading edge of each bar while it climbs, shedding embers behind it,
        /// and flaring where it stops.
        ///
        /// <b>Parented to the fill's right edge</b>, so it travels with the climb without being
        /// told where the climb is: the fill grows from the left (<c>WardStatBars.Paint</c>), and
        /// a child anchored to its right edge is carried along by the layout itself.
        /// </summary>
        void Ride()
        {
            _heads.Clear();

            Head(_bars?.DamageFill);
            Head(_bars?.HealthFill);

            if (_heads.Count == 0) return;

            var sparks = new[] { Color.white, Pal.Lift(Pal.Gold, .35f), Pal.Lift(GainInk, .4f) };

            _fx.Run(46f, Climb * .9f, i =>
            {
                if (_heads.Count == 0) return default;

                var head = _heads[i % _heads.Count];
                if (!head) return default;

                Vector2 at = _fx.Node.InverseTransformPoint(head.position);

                return new CeremonyFx.Mote
                {
                    Sprite = SparkArt,
                    Colour = sparks[i % sparks.Length],
                    At = at,
                    Velocity = new Vector2(UnityEngine.Random.Range(-260f, -60f), UnityEngine.Random.Range(40f, 260f)),
                    Life = UnityEngine.Random.Range(.3f, .55f),
                    Size = UnityEngine.Random.Range(10f, 18f),
                    SizeEnd = 3f,
                    Gravity = 700f,
                    Drag = 1.8f,
                    Stretch = .05f,
                    FadePower = 1.2f,
                };
            });
        }

        void Head(RectTransform fill)
        {
            if (fill == null) return;

            float tall = fill.rect.height;

            var node = UIKit.Box("Head", fill, Vector2.one * tall, new Vector2(1f, .5f),
                                 new Vector2(-tall * .5f, 0f));

            var glow = Additive.Lit(UIKit.Img("Glow", node, Art.Glow(64, 1.6f),
                                              Pal.A(Pal.Lift(Pal.Gold, .4f), .9f),
                                              Vector2.one * tall * 3.2f, new Vector2(.5f, .5f), Vector2.zero));
            glow.raycastTarget = false;

            var glint = Additive.Lit(UIKit.Img("Glint", node, GlintArt, Pal.A(Color.white, 1f),
                                               Vector2.one * tall * 2.6f, new Vector2(.5f, .5f), Vector2.zero));
            glint.raycastTarget = false;

            var spin = glint.rectTransform;
            Tween.Run(.6f, Ease.Linear, t =>
            {
                if (spin) spin.localRotation = Quaternion.Euler(0f, 0f, t * 90f);
            }, glint).Loop(-1, false);

            _heads.Add(node);
            _transient.Add(node.gameObject);
        }

        /// <summary>
        /// What the climb was worth: each bar's head flares where it stopped and goes out, and the
        /// gain is written beside the figure.
        /// </summary>
        void Landed()
        {
            foreach (var head in _heads)
            {
                if (!head) continue;

                Vector2 at = _fx.Node.InverseTransformPoint(head.position);
                Twinkle(at, Pal.A(Color.white, 1f), 170f, .5f, 0f);
                Destroy(head.gameObject);
            }

            _heads.Clear();

            Mark(_bars?.DamageRow, SiegeTuning.DamageFine(0, Now.PowerHundredths)
                                 - SiegeTuning.DamageFine(0, Was.PowerHundredths));

            Mark(_bars?.HealthRow, SiegeTuning.HealthOf(Now) - SiegeTuning.HealthOf(Was));
        }

        /// <summary>
        /// One row's gain, written in the empty column beside the bars.
        ///
        /// <b>Beside the figure rather than over it</b>, which is the one placement here a test
        /// cannot check and a picture would have caught: a mark drawn on the row itself lands on
        /// the very number it is about, and one that merely rises out of the way crosses the other
        /// bar on its way. The host is 780 of a 1080 canvas, so the column outside it is 150 wide
        /// and belongs to nothing.
        ///
        /// <b>Where the row sits is read off the row</b> and never recomputed: <c>WardStatBars</c>
        /// anchors each one to its host's <em>top</em>, so its own offset plus half the host's
        /// height is its middle, whatever the scale.
        /// </summary>
        void Mark(RectTransform row, int gain)
        {
            if (row == null || _barHost == null || gain <= 0) return;

            float y = BarsH * .5f + row.anchoredPosition.y;

            Tween.Punch(row, .10f, .34f);

            var spot = new Vector2(0f, BarsY) + new Vector2(BarsW * .5f + 40f, y);
            _fx.Spray(SparkArt, spot, new[] { Pal.Lift(GainInk, .3f), Color.white }, 10, 120f, 300f,
                      16f, .46f, gravity: 500f, stretch: .03f);

            var plus = UIKit.Titled("Gain", _barHost, "+" + gain, 40, GainInk,
                                    TextAnchor.MiddleLeft, new Vector2(128f, 60f),
                                    new Vector2(.5f, .5f),
                                    new Vector2(BarsW * .5f + 74f, y), 3f, 3f);
            plus.raycastTarget = false;

            var rt = plus.rectTransform;
            var home = rt.anchoredPosition;

            Tween.Run(.90f, Ease.OutCubic, t =>
            {
                if (!plus) return;

                rt.anchoredPosition = home + new Vector2(0f, 24f * t);
                SetAlpha(plus, t < .25f ? t / .25f : 1f - (t - .25f) / .75f);
            }, plus).OnDone(() => { if (plus) Destroy(plus.gameObject); });
        }

        /// <summary>
        /// What stays alive after the noise: embers lifting off the turret, and the star just
        /// bought catching the light now and then - so the screen the player reads their numbers
        /// on is still a lit room, and the eye is drawn back to what changed.
        /// </summary>
        void Ambient()
        {
            if (_ambient) return;
            _ambient = true;

            _fx.Rise(GlowArt, FootAt + new Vector2(0f, 30f), 150f,
                     new[] { Pal.Lift(_tint, .45f), Pal.Lift(Pal.Gold, .35f), Pal.Cream }, 6f, -1f, 18f, 100f);

            var slot = SlotAt;
            _fx.Run(.5f, -1f, i => new CeremonyFx.Mote
            {
                Sprite = GlintArt, Colour = Pal.A(Color.white, .95f), At = slot, Life = .9f,
                Size = 30f, SizeEnd = StarSize * 1.8f, Grow = Ease.InOutSine, FadeIn = .45f,
                FadePower = 1.4f, Spin = 40f,
            });

            if (_glow)
                Tween.Run(2.8f, Ease.InOutSine, t =>
                {
                    if (_glow) SetAlpha(_glow, Mathf.Lerp(GlowAlpha, GlowAlpha * .7f, t));
                }, _glow, "breath").Loop(-1, true);
        }

        // ----------------------------------------------------------------- pieces
        /// <summary>
        /// One shaped piece of a flare: grows from one size to another, turns, and fades.
        /// Destroyed when it is done, and listed so a skip can take it away mid-flight.
        /// </summary>
        void Flare(Sprite sprite, Vector2 at, Color colour, Vector2 from, Vector2 to, float turnFrom,
                   float turnTo, float life, Easing ease)
        {
            var img = _fx.Light("Flare", sprite, colour, from, at);
            _transient.Add(img.gameObject);

            var rt = img.rectTransform;
            float alpha = colour.a;

            Tween.Run(life, Ease.Linear, t =>
            {
                if (!img) return;

                float e = ease(t);
                rt.sizeDelta = Vector2.LerpUnclamped(from, to, e);
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(turnFrom, turnTo, e));

                var c = img.color;
                c.a = alpha * (1f - t) * (1f - t);
                img.color = c;
            }, this).OnDone(() => { if (img) Destroy(img.gameObject); });
        }

        /// <summary>A column of light standing up behind the turret and narrowing as it fades.</summary>
        void Column(Vector2 foot, Color colour)
        {
            var img = _fx.Light("Column", Art.Shaft(64, 256, 1.5f, .08f), Pal.A(colour, .8f),
                                new Vector2(240f, 700f), foot);
            _transient.Add(img.gameObject);

            var rt = img.rectTransform;
            rt.pivot = new Vector2(.5f, 0f);
            rt.anchoredPosition = foot;

            Tween.Run(.9f, Ease.Linear, t =>
            {
                if (!img) return;

                float e = Ease.OutCubic(t);
                rt.sizeDelta = new Vector2(Mathf.Lerp(240f, 30f, e), Mathf.Lerp(560f, 980f, e));
                img.color = Pal.A(colour, .8f * (1f - t * t));
            }, this).OnDone(() => { if (img) Destroy(img.gameObject); });
        }

        /// <summary>
        /// Lightning off a point, re-struck on a new seed a few times and flickering out - what
        /// says electricity is that the next frame is unrelated to this one (<c>Lightning</c>).
        /// </summary>
        void Crackle(Vector2 at, float inner, float outer, int arms, Color colour, float width,
                     float life, int strikes)
        {
            var bolt = Lightning.Grow("Arc", _fx.Node, colour, width, .45f);
            _transient.Add(bolt.Host.gameObject);

            int seed = UnityEngine.Random.Range(1, 1 << 30);
            int struck = 0;

            bolt.Nova(at, inner, outer, arms, 100f, .32f, seed);

            var host = bolt.Host;
            var group = bolt.Group;

            Tween.Run(life, Ease.Linear, t =>
            {
                if (!host) return;

                int k = Mathf.Min(strikes - 1, Mathf.FloorToInt(t * strikes));
                if (k != struck)
                {
                    struck = k;
                    bolt.Nova(at, inner, outer, arms, 100f, .32f, seed + k * 7919);
                }

                group.alpha = (1f - t) * (.72f + .28f * Mathf.Sin(t * 70f));
            }, this).OnDone(() => { if (host) Destroy(host.gameObject); });
        }

        /// <summary>A four-point glint flaring on a point and gone - the mark of a thing landing.</summary>
        void Twinkle(Vector2 at, Color colour, float size, float life, float delay)
        {
            _fx.Emit(new CeremonyFx.Mote
            {
                Sprite = GlintArt, Colour = colour, At = at, Life = life, Delay = delay,
                Size = size * .3f, SizeEnd = size, Grow = Ease.OutCubic, Spin = 60f, FadePower = 1.8f,
            });
        }

        /// <summary>A streak of light drawn under a caption as it lands, and a glint at each end.</summary>
        void Underline(Vector2 at, float width, Color colour)
        {
            Flare(Art.Glow(64, 1.4f), at, Pal.A(colour, .8f), new Vector2(width * .2f, 40f),
                  new Vector2(width * 1.35f, 10f), 0f, 0f, .70f, Ease.OutCubic);

            Twinkle(at + new Vector2(-width * .5f, 0f), Pal.A(Color.white, .95f), 150f, .55f, 0f);
            Twinkle(at + new Vector2(width * .5f, 0f), Pal.A(Color.white, .95f), 150f, .55f, 0f);
        }

        /// <summary>How wide the title is drawn, for the streak under it.</summary>
        float TitleW() => _title ? Mathf.Clamp(_title.preferredWidth, 240f, 900f) : 600f;

        /// <summary>The whole room leaning in and settling back. Owned by this view, so a skip puts it back.</summary>
        void Zoom(float from, float life)
        {
            var content = Content;
            content.localScale = Vector3.one * from;

            Tween.Run(life, Ease.OutCubic, t =>
            {
                if (content) content.localScale = Vector3.one * Mathf.Lerp(from, 1f, t);
            }, this);
        }

        void ClearTransient()
        {
            foreach (var go in _transient)
                if (go) Destroy(go);

            _transient.Clear();
            _heads.Clear();
        }

        void Spin()
        {
            Tween.Run(62f, Ease.Linear, t =>
            {
                if (_fanRt) _fanRt.localRotation = Quaternion.Euler(0f, 0f, t * 360f);
            }, _fanRt, "spin").Loop(-1, false);
        }

        void Counterspin()
        {
            Tween.Run(45f, Ease.Linear, t =>
            {
                if (_fan2Rt) _fan2Rt.localRotation = Quaternion.Euler(0f, 0f, -t * 360f);
            }, _fan2Rt, "spin").Loop(-1, false);
        }

        // ------------------------------------------------------------------- skip
        /// <summary>
        /// Ends the sequence now, in the state it was heading for.
        ///
        /// <para>
        /// One pass of assignments rather than a second choreography, which is only possible
        /// because every element already exists - the beats reveal things rather than build them.
        /// Pending beats are killed by owner; <see cref="Cue"/> schedules every one of them
        /// against this component, which is what makes them cancellable as a group.
        /// </para>
        /// </summary>
        void Skip()
        {
            if (_settled) return;

            Tween.KillAll(this);

            // The light first: every mote back in its pool and every flare, bolt and rider gone,
            // so nothing from a beat that never finished is left burning on the screen.
            _fx?.Clear();
            ClearTransient();

            if (Content) Content.localScale = Vector3.one;

            if (_sky) _sky.color = Color.white;
            if (_vignette) SetAlpha(_vignette, VignetteAlpha);
            if (_hush) SetAlpha(_hush, 0f);
            if (_flash) SetAlpha(_flash, 0f);

            if (_aurora != null)
                for (int i = 0; i < _aurora.Length; i++) SetAlpha(_aurora[i], AuroraAlpha[i]);

            // Restarted rather than left dead: a skip landing before the break killed the beat
            // that would have started the turn, and a fan frozen mid-turn is the one part of this
            // that reads as a bug rather than as a fast-forward.
            if (_fanRt) { _fanRt.localScale = Vector3.one; SetAlpha(_fan, FanAlpha); Spin(); }
            if (_fan2Rt) { _fan2Rt.localScale = Vector3.one; SetAlpha(_fan2, Fan2Alpha); Counterspin(); }

            if (_glow) SetAlpha(_glow, GlowAlpha);

            // A star still in the air belongs to nothing once the ladder is relit, and left there
            // it is a second star standing over the one it became.
            if (_falling) { Destroy(_falling.gameObject); _falling = null; }

            // **Killed by the group rather than by this view**, because a fade's owner is the
            // thing it fades: left running, an entrance still in flight would go on lerping from
            // where it was and undo the assignment on the very next frame.
            if (_plateRt)
            {
                var group = UIKit.Group(_plateRt);
                Tween.KillAll(group);
                group.alpha = 1f;
                _plateRt.localScale = Vector3.one;
            }

            Relight();

            if (_title) { SetAlpha(_title, 1f); _title.transform.localScale = Vector3.one; }
            if (_name) SetAlpha(_name, .92f);

            if (_barHost)
            {
                var group = UIKit.Group(_barHost);
                Tween.KillAll(group);
                group.alpha = 1f;

                // `Set` kills the climb channel, so a skip landing mid-climb lands on the figures
                // rather than fighting them.
                _bars?.Set(Now, WardLedger.Catalog);
            }

            if (_actRt) _actRt.localScale = Vector3.one;
            if (_dismiss) { _dismiss.alpha = 1f; _dismiss.blocksRaycasts = true; }

            _settled = true;

            // Straight into the settled room's own life, so a skip lands on the same screen the
            // sequence would have ended on rather than a stiller one.
            Ambient();
        }

        static void SetAlpha(Graphic g, float a)
        {
            if (g == null) return;

            var c = g.color;
            c.a = a;
            g.color = c;
        }

        /// <summary>
        /// Back finishes the sequence rather than closing, once. A player pressing back halfway
        /// through almost always means "get to it", and closing would throw away the reveal of
        /// something they just paid for; pressing it again then leaves.
        /// </summary>
        public override bool OnBack()
        {
            if (!_settled) { Skip(); return true; }

            Close();
            return true;
        }
    }
}
