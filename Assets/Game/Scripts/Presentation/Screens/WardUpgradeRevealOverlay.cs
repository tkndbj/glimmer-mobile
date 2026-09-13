using System;
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
    /// The first cut celebrated inside the parchment panel the purchase had just been made on — a
    /// small fan behind a star row, a couple of sparks, two short bars — and the owner's verdict
    /// after playing it was one word. Every piece of it was individually defensible and the whole
    /// was the smallest change on the screen at the largest moment in the feature, which is the
    /// fault <c>WardRevealOverlay</c>'s own note records about the purchase that used to celebrate
    /// itself in place, and the one <c>WheelPrizeOverlay</c> was built to fix. The dearest star on
    /// this shelf is a hundred and fifty thousand credits — days of play — and invariant 20m's
    /// rule is that the event is the reward and gets the biggest drawing there is.
    /// </para>
    /// <para>
    /// <b>What separates it from the turret's <em>unlock</em> is what it is about.</b> Those two
    /// ceremonies must not be one ceremony at two strengths, which is invariant 37z's complaint
    /// about two bosses told apart by a hue, asked of a celebration. An unlock is a <em>thing
    /// arriving</em>, so <c>WardRevealOverlay</c> stands the turret on a plate and lets it fire
    /// for ever under its name and its note. An upgrade is the same turret, and what changed is a
    /// star and two readings the player was looking at a second ago — so the <em>star</em> is the
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
    /// <b>And it makes exactly one sound</b>, on the break — see <see cref="Land"/>. What the
    /// player hears across the whole purchase is the coin as the money leaves and the victory as
    /// the star lands.
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
        /// does the same here — a poor substitute for a picture, and what there is.
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
        /// scenery — so the light sits between, and what tells the eye where to look is the star
        /// falling and the turret flaring, which are events rather than lighting.
        /// </summary>
        const float LightY = 210f;

        const float FanSize = 1180f;

        const float VignetteAlpha = .72f, FanAlpha = .30f, Fan2Alpha = .20f, GlowAlpha = .40f;

        /// <summary>
        /// Its own hold, never the board's — <see cref="WardFiringStage"/>'s
        /// rule and its reason: a live board's four turrets must not be released because a
        /// celebration closed. Its own name and not the unlock's, so the two can never be up at
        /// once and release each other's art.
        /// </summary>
        const string UpgradeScope = "ward_upgrade";

        // ----------------------------------------------------------------- timing
        /// <summary>
        /// The sequence, as gaps rather than absolute times — <see cref="Cue"/>'s whole argument.
        ///
        /// <b>Three movements: the room charges, the star lands, and the numbers climb.</b> The
        /// charge is the part that cannot be cut: it is the only stretch where nothing has
        /// happened yet, and taking it out turns the impact from something earned into something
        /// sudden. The numbers come last because they are the reading a player has to be looking
        /// at when the noise has stopped.
        /// </summary>
        const float ChargeFor = .58f, RingGap = .13f, FallFor = .30f;
        const float ToTitle = .40f, ToName = .16f, ToBars = .26f, ToKey = .30f;
        const float Climb = 1.10f;

        static readonly Color Ink = Pal.Cream;

        /// <summary>
        /// What the gain beside a climbing bar is written in.
        ///
        /// <b>The bright green rather than <c>WardStatBars</c>' own dark one</b>, because the two
        /// are drawn on opposite grounds: that one sits on cream parchment and this on a room
        /// taken almost to black. The rule is the contrast against what is behind it, never the
        /// hue — which is the same correction invariant 37l records about a tint that could only
        /// ever darken.
        /// </summary>
        static readonly Color GainInk = Pal.Mint;

        Image _sky, _vignette, _fan, _fan2, _glow, _flash, _plate;
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
            _sky = UIKit.Img("Sky", Content,
                             Art.Gradient(Color.Lerp(_c.Deep, _c.Partner, .28f),
                                          _c.Deep,
                                          Color.Lerp(_c.Deep, Color.black, .42f)),
                             new Color(1f, 1f, 1f, 0f));
            UIKit.StretchTo((RectTransform)_sky.transform, 0, 0, 0, 0);
            _sky.raycastTarget = true;
            _sky.gameObject.AddComponent<Btn>().Setup(Skip);

            BuildAurora();

            Fireflies.Spawn(Content, 12, Pal.A(_c.Partner, .55f), 4f, 12f);

            _vignette = UIKit.Img("Vignette", Content, Art.Vignette(256),
                                  Pal.A(Color.Lerp(_c.Deep, Color.black, .58f), 0f));
            UIKit.StretchTo((RectTransform)_vignette.transform, 0, 0, 0, 0);
            _vignette.raycastTarget = false;

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

        // Where the three masses of light sit — a composition rather than a scatter, so the frame
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
        /// snaps back every time the tween wraps — which on something this large is the most
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
        /// its bolt — that is the argument <see cref="WardFiringStage"/> is built on and the
        /// reason nineteen projectiles were baked — so a ceremony about a turret getting stronger
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
        /// its parent's <em>top</em> — pointed at a full-screen layer that is the top of the
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
            // here would be asking for a second payment — invariant 42a's own note about what the
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
        void Play()
        {
            var cue = new Cue(this);

            // -- the room -----------------------------------------------------
            cue.With(() =>
            {
                Tween.Fade(_sky, 1f, .28f);
                Tween.Fade(_vignette, VignetteAlpha, .38f);

                if (_aurora != null)
                    for (int i = 0; i < _aurora.Length; i++)
                        Tween.Fade(_aurora[i], AuroraAlpha[i], .66f);

                Tween.Fade(UIKit.Group(_plateRt), 1f, .30f);
                Tween.Scale(_plateRt, 1f, .42f, Ease.OutBack);
            });

            // -- the charge ---------------------------------------------------
            // Light dragged in out of the dark, and three rings closing on the plate. Anticipation
            // is the only thing on screen for half a second, which is what makes the landing feel
            // earned rather than sudden.
            cue.Then(.12f, () => Charge(14));

            cue.Then(.06f, () => Collapse(0));
            cue.Then(RingGap, () => Collapse(1));
            cue.Then(RingGap, () => Collapse(2));

            // -- the star falls -----------------------------------------------
            cue.Then(ChargeFor - RingGap * 2f - .18f, Drop);

            // -- the landing --------------------------------------------------
            cue.Then(FallFor, Land);

            // -- the title ----------------------------------------------------
            cue.Then(ToTitle, () =>
            {
                SetAlpha(_title, 1f);
                _title.transform.localScale = Vector3.one * 2.4f;
                Tween.Scale(_title.transform, 1f, .26f, Ease.InCubic)
                     .OnDone(() => Tween.Punch(_title.transform, .18f, .34f));
            });

            cue.Then(ToName, () => Tween.Fade(_name, .92f, .30f));

            // -- and what it bought -------------------------------------------
            cue.Then(ToBars, () =>
            {
                Tween.Fade(UIKit.Group(_barHost), 1f, .26f);
                _bars?.Climb(Was, Now, WardLedger.Catalog, Climb);
            });

            cue.Then(Climb, Landed);

            cue.Then(ToKey, () =>
            {
                Tween.Pop(_actRt, .6f, .44f);
                Tween.Fade(_dismiss, 1f, .28f);
                _dismiss.blocksRaycasts = true;

                _settled = true;
            });
        }

        // ------------------------------------------------------------------ parts
        /// <summary>
        /// Motes dragged in out of the dark, into the slot the star is about to land in.
        ///
        /// <b>Inward rather than outward</b>, which is the one thing that makes this read as a
        /// charge: everything else in this game that throws light throws it away from something.
        ///
        /// <b>And into the <em>slot</em> rather than into the turret</b>, so the gathering and the
        /// thing it is gathering for share one place. Split across the two — light into the turret
        /// and then a star out of the sky — they read as two unrelated events that happened to be
        /// close together.
        /// </summary>
        void Charge(int count)
        {
            var to = SlotAt;

            for (int i = 0; i < count; i++)
            {
                float ang = (i / (float)count) * Mathf.PI * 2f + UnityEngine.Random.Range(-.3f, .3f);
                float far = UnityEngine.Random.Range(520f, 900f);
                float size = UnityEngine.Random.Range(16f, 34f);
                float dur = ChargeFor * UnityEngine.Random.Range(.72f, 1f);

                var from = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * far + to;

                var mote = UIKit.Img("Mote", Content, Art.Spark(64),
                                     Pal.A(Pal.Lift(_c.Nth(i), .35f), 0f),
                                     Vector2.one * size, new Vector2(.5f, .5f), from);
                mote.raycastTarget = false;

                var rt = (RectTransform)mote.transform;

                Tween.Run(dur, Ease.InCubic, t =>
                {
                    if (!rt) return;

                    rt.anchoredPosition = Vector2.Lerp(from, to, t);
                    rt.localScale = Vector3.one * Mathf.Lerp(1.2f, .25f, t);
                    mote.color = Pal.A(mote.color, Mathf.Min(1f, t * 3f) * (1f - t * t));
                }, mote).OnDone(() => { if (mote) Destroy(mote.gameObject); });
            }
        }

        /// <summary>A ring closing on the slot, before there is anything there to close on.</summary>
        void Collapse(int index)
        {
            var ring = UIKit.Img("In" + index, Content, Art.Ring(256, 7f), Pal.A(_c.Nth(index), 0f),
                                 Vector2.one * 1080f, new Vector2(.5f, .5f), SlotAt);
            ring.raycastTarget = false;

            Tween.Run(.46f, Ease.InCubic, t =>
            {
                if (!ring) return;

                ring.transform.localScale = Vector3.one * Mathf.Lerp(1f, .16f, t);
                ring.color = Pal.A(_c.Nth(index), .68f * Mathf.Sin(t * Mathf.PI));
            }, ring).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        /// <summary>Where in the ladder the star being bought stands, on the canvas.</summary>
        Vector2 SlotAt => new Vector2(WardStarRow.XOf(WardStars.Sane(Now.Stars) - 1, StarSize),
                                      StarsY);

        /// <summary>
        /// The star itself, falling into the slot it was bought for.
        ///
        /// <b>It arrives from off the top of the canvas</b> rather than fading up in place, so
        /// there is a moment where the thing about to happen is visible and has not happened —
        /// which is the whole difference between a reward landing and a readout changing. Where it
        /// lands is <see cref="WardStarRow.XOf"/> and never this file's own arithmetic, or the
        /// star would stand beside the one it is supposed to become the day either the size or the
        /// gap moves.
        /// </summary>
        void Drop()
        {
            var slot = SlotAt;
            var from = new Vector2(slot.x, 1180f);

            // **One node carrying both**, rather than a star and a halo moved in step. Two objects
            // are two things a skip has to remember to take away, and the one it forgot would be
            // left as a gold smear over a ladder that had already relit — which is exactly the
            // shape of leak this file's own <see cref="Skip"/> exists to make impossible.
            _falling = UIKit.Box("Falling", Content, Vector2.one * StarSize,
                                 new Vector2(.5f, .5f), from);

            var halo = UIKit.Img("Glow", _falling, Art.Glow(128, 1.8f), Pal.A(Pal.Gold, 0f),
                                 Vector2.one * StarSize * 3.4f, new Vector2(.5f, .5f),
                                 Vector2.zero);
            halo.raycastTarget = false;

            var star = UIKit.Img("Star", _falling, Art.S("Ui/star_full"), Pal.Gold,
                                 Vector2.one * StarSize, new Vector2(.5f, .5f), Vector2.zero);
            star.raycastTarget = false;

            var node = _falling;

            Tween.Fade(halo, .55f, FallFor * .5f);

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
        }

        /// <summary>
        /// The impact: the star is in the ladder, the room breaks open, and the turret takes it.
        ///
        /// <b>One sound in the whole ceremony, and it is here.</b> It shipped with five — a
        /// whoosh under the charge, a bell on the star, the fanfare on the title, a chime under
        /// the climbing bars and a collect when they landed — each one placed on a beat that
        /// deserved marking, and played back as a pile-up rather than as a celebration. The
        /// owner's verdict after a device was to keep the victory and nothing else. That is
        /// <c>WardRevealOverlay</c>'s own note arrived at from the other side: the companion
        /// reveal shipped ringing on all six of its beats and was cut to one for the same reason,
        /// and this screen was written knowing that and did it anyway.
        ///
        /// <b>On the break rather than on the title.</b> With four sounds gone the one left has to
        /// land on the loudest thing on the screen; a fanfare four tenths of a second after the
        /// flash reads as a sound belonging to something else.
        /// </summary>
        void Land()
        {
            _flash.color = new Color(1f, .99f, .94f, .90f);
            Tween.Fade(_flash, 0f, .36f, Ease.OutQuad);

            Tween.Shake((RectTransform)Content, 30f, .46f);
            Audio.Sfx("win", .62f);

            _fanRt.localScale = Vector3.one * .35f;
            Tween.Scale(_fanRt, 1f, .68f, Ease.OutQuint);
            Tween.Fade(_fan, FanAlpha, .52f);
            Spin();

            _fan2Rt.localScale = Vector3.one * .30f;
            Tween.Scale(_fan2Rt, 1f, .80f, Ease.OutQuint);
            Tween.Fade(_fan2, Fan2Alpha, .58f);
            Counterspin();

            Tween.Fade(_glow, GlowAlpha, .48f);

            for (int i = 0; i < 4; i++) Shockwave(i * .07f, _c.Nth(i));

            Burst.Confetti(Content, 86);

            Relight();

            // The turret takes the light rather than arriving in it: `Claim` is the stage's own
            // overcharge — a halo, rings out of the barrel and a spring — with its white-out off,
            // because the screen is already white and two flashes a frame apart is one flash with
            // a seam in it.
            _stage?.Claim(whiteOut: false);
        }

        /// <summary>
        /// The ladder at its new count, with the star that was just bought punched where it stands.
        ///
        /// <b>Rebuilt rather than a sixth star added</b>, because the row is five stars whatever a
        /// turret has earned (<c>WardStarRow</c>) and what changes is which of them are lit.
        /// </summary>
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
            Burst.Sparks(earned, Vector2.zero, Pal.Gold, 20, 320f, 28f, .70f);
        }

        /// <summary>A ring leaving the ladder, on the break.</summary>
        void Shockwave(float delay, Color colour)
        {
            var ring = UIKit.Img("Wave", Content, Art.Ring(256, 10f), Pal.A(colour, 0f),
                                 Vector2.one * 380f, new Vector2(.5f, .5f), SlotAt);
            ring.raycastTarget = false;

            Tween.Run(.74f, Ease.OutQuint, t =>
            {
                if (!ring) return;

                ring.transform.localScale = Vector3.one * Mathf.Lerp(.2f, 3.6f, t);
                ring.color = Pal.A(colour, .78f * (1f - t));
            }, ring).Delay(delay).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        /// <summary>
        /// What the climb was worth, marked on each bar as it settles.
        ///
        /// <b>The gain is said at the end rather than throughout</b>, because during the climb the
        /// figure itself is the thing being watched and a second number beside it moving would be
        /// two readings competing. It rises a little way inside the bar's own row, never out of
        /// it, so the damage mark can never be drawn across the health bar underneath.
        /// </summary>
        void Landed()
        {
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
            Burst.Sparks(_barHost, new Vector2(BarsW * .5f + 40f, y), GainInk, 8, 150f, 16f, .46f);

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
        /// because every element already exists — the beats reveal things rather than build them.
        /// Pending beats are killed by owner; <see cref="Cue"/> schedules every one of them
        /// against this component, which is what makes them cancellable as a group.
        /// </para>
        /// </summary>
        void Skip()
        {
            if (_settled) return;

            Tween.KillAll(this);

            if (_sky) _sky.color = Color.white;
            if (_vignette) SetAlpha(_vignette, VignetteAlpha);
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
