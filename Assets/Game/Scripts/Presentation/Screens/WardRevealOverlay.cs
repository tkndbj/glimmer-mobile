using GlimmerGrove.Localization;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A turret arriving — the ceremony a player meets the moment they have paid for one.
    ///
    /// <para>
    /// <b>It exists because the first attempt was a flourish on a shop panel and read as one.</b>
    /// The purchase used to celebrate itself in place: the price pill became EQUIP, a ring of
    /// waves left the turret and some confetti fell over the panel that had just taken nine
    /// thousand credits. Every piece of that was individually fine and the whole was not a
    /// payoff — which is exactly the note <c>CompanionUnlockOverlay</c> already carries about its
    /// own history, <em>a transaction panel is the wrong place for a payoff, because it is still
    /// wearing the furniture of a decision the player has already made</em>. The preview panel
    /// hands over to this and gets out of the way, which is the companion flow, arrived at the
    /// same way and for the same reason.
    /// </para>
    /// <para>
    /// <b>What makes it a turret's ceremony rather than a companion's is that it fires.</b> A
    /// companion is a portrait and the reveal's job is to show it; a turret is a <em>verb</em>,
    /// and the thing bought is the projectile and the arrangement of raiders its ability is
    /// about. So the middle of this screen is <see cref="WardFiringStage"/> — the shipped one,
    /// drawing through the board's own anchors and scales — running its volley on a loop under
    /// the fanfare. A reveal that stood a still picture of a turret would be hiding the one
    /// thing that was paid for.
    /// </para>
    /// <para>
    /// <b>The room wears the seat's colour, not a rarity's.</b> A turret is bought for one of
    /// the line's four colours (<c>WardHolding</c>), and which one is the fact a player most
    /// needs carried out of this screen — so the sky, the fans, the waves and the rim are all
    /// <c>SiegeView.TintOf(Colour)</c>, and <see cref="Chroma"/> is built around it rather than
    /// looked up from a tier. The tier decides how <em>loud</em> the room is and nothing else.
    /// </para>
    /// <para>
    /// <b>It ends on the next thing they want.</b> The button stands the turret on the seat that
    /// raised this, because the whole reason somebody bought a turret is to use it, and making
    /// them close a celebration to go and find the cell again is a ceremony that gets in the way.
    /// </para>
    /// </summary>
    public sealed class WardRevealOverlay : ModalView
    {
        /// <summary>
        /// The turret being revealed.
        ///
        /// A property rather than a field, because <see cref="WardModel"/> is not
        /// <c>[Serializable]</c> and a public field earns a warning about serialisation that will
        /// never happen — <c>WardPreviewOverlay.Model</c>'s note.
        /// </summary>
        public WardModel Model { get; set; }

        /// <summary>Which of the line's four colours it was bought for.</summary>
        public int Colour { get; set; }

        /// <summary>Raised after anything lands here, so the shelf behind can repaint.</summary>
        public System.Action Changed { get; set; }

        // ------------------------------------------------------------------ shape
        /// <summary>
        /// The plate, and the stage standing on it.
        ///
        /// <b>The cell is a board's</b>, so the bolt, the flash and the impact are drawn at the
        /// proportions a phone draws them on the hill — which is the whole point of showing them
        /// rather than a picture of a turret.
        /// </summary>
        const float PlateW = 760f, PlateH = 700f, StageW = 700f, StageH = 640f, StageCell = 112f;

        /// <summary>
        /// Where each band sits, stated as a <em>middle</em> — <c>UIKit.Box</c> pivots at centre
        /// whatever it is anchored to, which is the arithmetic <c>WardPreviewOverlay</c> already
        /// records getting wrong once and drawing a stage straight through two captions.
        ///
        /// <para>
        /// The canvas is 1080 x <c>Boot.RefHeight</c>, so this runs from +960 to -960 and every
        /// band below has been checked against its neighbours' edges rather than its centres:
        /// the plate spans +560..-140, the pips -217..-243, the name -265..-395, the rule
        /// -401..-411, the note -432..-552 and the key -585..-735. The gaps are what stops a
        /// wrapped ability note sitting on the keyline above it.
        /// </para>
        /// </summary>
        internal const float PlateY = 210f, PipY = -230f, NameY = -330f, RuleY = -406f;
        internal const float NoteY = -492f, ActY = -660f;

        /// <summary>
        /// How tall each of those bands is drawn.
        ///
        /// <b>Named rather than typed at the call site, so the spacing above can be checked.</b>
        /// Nothing in this project can look at this screen — there is no render for it and every
        /// numeric gate reads the model — so the one class of fault that would otherwise ship
        /// unseen is a caption drawn through the thing above it. <c>WardRevealTests</c> walks
        /// these edges; it is a poor substitute for a picture and it is what there is.
        /// </summary>
        internal const float NameH = 130f, NoteH = 120f, ActH = 150f, RuleH = 10f;

        const float FanSize = 1220f;
        const float PipSize = 26f, PipGap = 38f;

        const float VignetteAlpha = .70f, FanAlpha = .32f, Fan2Alpha = .22f, GlowAlpha = .44f;

        /// <summary>
        /// Its own hold, never the board's — <see cref="WardFiringStage"/>'s
        /// rule, and its reason: a live board's four turrets must not be released because a
        /// celebration closed.
        /// </summary>
        const string RevealScope = "ward_reveal";

        Image _sky, _vignette, _fan, _fan2, _glow, _flash, _rim, _plate;
        Image[] _aurora;
        Image[] _pips;
        Image _rule;
        RectTransform _plateRt, _fanRt, _fan2Rt;
        Text _name, _note, _sub;
        Btn _act;
        Text _label;
        CanvasGroup _dismiss;
        RectTransform _actRt;

        WardFiringStage _stage;

        int _tier;
        Chroma _c;
        Color _tint;
        bool _settled;

        protected override void Build()
        {
            if (Model == null) { Close(); return; }

            _tint = SiegeView.TintOf(Colour);
            _tier = TierOf(Model);
            _c = SchemeFor(_tint);

            BuildStage();
            BuildTurret();
            BuildCaptions();
            BuildButtons();

            // Above everything, and last, so the break whites out the whole composition.
            _flash = UIKit.Img("Flash", Content, Art.Pixel, new Color(1f, .99f, .94f, 0f));
            UIKit.StretchTo((RectTransform)_flash.transform, 0, 0, 0, 0);
            _flash.raycastTarget = false;

            Play();
        }

        // ------------------------------------------------------------------- tier
        /// <summary>
        /// How grand this should be, 1 to 5, from the turret's rung on its own ladder.
        ///
        /// <para>
        /// <b>A rung and never a price</b>, which is invariant 16j's trap met on a second
        /// roster: half of this shelf is priced in gems and half in credits, and six hundred
        /// gems does not compare with nine thousand credits in either direction. Sorting the
        /// two together would put the dearest turrets in the game at the bottom of the ladder
        /// wearing tier one — the mistake <c>GroveLand.NextForSale</c> made and paid for.
        /// <c>WardModel.Order</c> is authored and runs cheapest-first <em>inside</em> each
        /// currency, so the rung is the one comparable thing there is.
        /// </para>
        /// <para>
        /// <b>A presentation decision and deliberately not a content field</b>, which is
        /// <c>CompanionRevealOverlay.TierOf</c>'s argument: it buys nothing and gates nothing,
        /// so authoring it would be inventing a rarity system nobody asked for and another
        /// number per turret to keep in step with the price. Derived, it is automatically right
        /// for every turret a drop adds.
        /// </para>
        /// </summary>
        internal static int TierOf(WardModel model)
        {
            if (model == null || model.IsStarter) return 1;

            // The ladder as it is authored, read off the roster rather than typed — so a drop
            // that lengthens it still lands its turrets across all five tiers instead of bunching
            // them at the bottom, and a shelf selling in two currencies gets a rung per currency
            // rather than one ladder measured in two kinds of money that do not compare.
            //
            // Since 2026-09-18 every turret is priced in credits, so this is one ladder of
            // Orders 2..30. The split by currency is kept rather than simplified away because it
            // costs nothing standing and is the whole rule the day a gem price comes back.
            int first = int.MaxValue, last = int.MinValue;

            foreach (var other in WardLedger.Catalog.Models)
            {
                if (other == null || other.IsStarter) continue;
                if (other.ForGems != model.ForGems) continue;

                if (other.Order < first) first = other.Order;
                if (other.Order > last) last = other.Order;
            }

            if (first > last) return 1;

            int rungs = last - first + 1;
            int rung = Mathf.Clamp(model.Order - first, 0, rungs - 1);

            return Mathf.Clamp(1 + rung * 5 / Mathf.Max(1, rungs), 1, 5);
        }

        // ----------------------------------------------------------------- chroma
        /// <summary>
        /// The room's three-colour scheme, built around the seat rather than looked up.
        ///
        /// <b><see cref="Chroma.Of"/> answers a rarity and this room is answering a colour.</b>
        /// The scheme's job is the same — a tint, a partner to cross it with, an accent and a
        /// deep to sit it all on — so the struct is right and only the way in differs. The
        /// partner is a quarter-turn round the wheel so the two fans cross in two hues rather
        /// than one, and the deep is the tint taken almost to black, which is what keeps a red
        /// room red in its corners instead of grey.
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

        // ------------------------------------------------------------------ stage
        /// <summary>
        /// The room: a coloured sky, drifting light behind it, a vignette, two crossing fans and
        /// a warm core. <c>CompanionRevealOverlay</c>'s composition, in the seat's colour.
        /// </summary>
        void BuildStage()
        {
            // The bottom layer, covering the screen, so a tap anywhere that is not a control
            // lands here and skips.
            _sky = UIKit.Img("Sky", Content,
                             Art.Gradient(Color.Lerp(_c.Deep, _c.Partner, .30f),
                                          _c.Deep,
                                          Color.Lerp(_c.Deep, Color.black, .40f)),
                             new Color(1f, 1f, 1f, 0f));
            UIKit.StretchTo((RectTransform)_sky.transform, 0, 0, 0, 0);
            _sky.raycastTarget = true;
            _sky.gameObject.AddComponent<Btn>().Setup(Skip);

            BuildAurora();

            Fireflies.Spawn(Content, 14, Pal.A(_c.Partner, .6f), 4f, 13f);

            // Tinted with the room rather than with ink, or the corners end up the one grey
            // thing on a coloured screen.
            _vignette = UIKit.Img("Vignette", Content, Art.Vignette(256),
                                  Pal.A(Color.Lerp(_c.Deep, Color.black, .55f), 0f));
            UIKit.StretchTo((RectTransform)_vignette.transform, 0, 0, 0, 0);
            _vignette.raycastTarget = false;

            _fan = UIKit.Img("Fan", Content, Art.Rays(512, 10 + _tier * 4), Pal.A(_tint, 0f),
                             Vector2.one * FanSize, new Vector2(.5f, .5f),
                             new Vector2(0f, PlateY));
            _fanRt = (RectTransform)_fan.transform;
            _fanRt.localScale = Vector3.zero;
            _fan.raycastTarget = false;

            _fan2 = UIKit.Img("Fan2", Content, Art.Rays(256, 6 + _tier), Pal.A(_c.Partner, 0f),
                              Vector2.one * (FanSize * .74f), new Vector2(.5f, .5f),
                              new Vector2(0f, PlateY));
            _fan2Rt = (RectTransform)_fan2.transform;
            _fan2Rt.localScale = Vector3.zero;
            _fan2.raycastTarget = false;

            _glow = UIKit.Img("Glow", Content, Art.Glow(256, 1.7f), Pal.A(_tint, 0f),
                              Vector2.one * 980f, new Vector2(.5f, .5f), new Vector2(0f, PlateY));
            _glow.raycastTarget = false;
        }

        // Where the three masses of light sit. A composition rather than a scatter — one high
        // and left, one across the middle, one low — so the frame is lit unevenly, the way a
        // place is. CompanionRevealOverlay's table, and its reasoning.
        static readonly Vector2[] AuroraHome = { new Vector2(-360f, 620f), new Vector2(400f, 120f), new Vector2(-250f, -600f) };
        static readonly float[] AuroraSize = { 1180f, 980f, 1240f };
        static readonly float[] AuroraAlpha = { .20f, .16f, .13f };

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
            float span = 46f + index * 12f;
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
        /// The plate the turret stands on, and the stage that draws it firing.
        ///
        /// <b>A plate rather than the bare room</b>, for the reason the companion reveal gives
        /// its portrait one: a lit object floating on a gradient has nothing to be lit against,
        /// and the stage is masked, so it needs an edge for the mask to read as deliberate.
        /// </summary>
        void BuildTurret()
        {
            _plate = UIKit.Img("Plate", Content, Art.Round(34),
                               Pal.A(Color.Lerp(_c.Deep, Color.black, .35f), .96f),
                               new Vector2(PlateW, PlateH), new Vector2(.5f, .5f),
                               new Vector2(0f, PlateY));
            _plateRt = (RectTransform)_plate.transform;
            _plateRt.localScale = Vector3.zero;
            _plate.raycastTarget = false;

            // What ModalView.Close() scales out. Every overlay that skips MakePanel has to name
            // its own Panel: leaving it null throws out of the click handler *after* the content
            // has faded and *before* Flow.Dismiss runs, stranding an invisible full-screen
            // blocker with _closing already latched. CompanionRevealOverlay's note, verbatim,
            // because it is the same trap and this is the second screen to walk into it.
            Panel = _plateRt;

            _rim = UIKit.Img("Rim", _plate.transform, Art.RoundOutline(34, 5f), Pal.A(_tint, .92f));
            UIKit.StretchTo((RectTransform)_rim.transform, 0, 0, 0, 0);
            _rim.raycastTarget = false;

            _stage = WardFiringStage.Attach(_plateRt, new Vector2(StageW, StageH),
                                            new Vector2(.5f, .5f), Vector2.zero,
                                            StageCell, RevealScope);
            _stage.Show(Model, Colour);
        }

        // --------------------------------------------------------------- captions
        void BuildCaptions()
        {
            _name = UIKit.Shrinkable(
                UIKit.Titled("Name", Content, Loc.Get(Model.NameKey), 92, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(920f, NameH), new Vector2(.5f, .5f),
                             new Vector2(0f, NameY), outline: 5f, shadow: 6f), 52);
            SetAlpha(_name, 0f);
            _name.transform.localScale = Vector3.zero;

            _rule = UIKit.Img("Rule", Content, Art.SoftCapsule(10, 120), Pal.A(_tint, 0f),
                              new Vector2(0f, RuleH), new Vector2(.5f, .5f), new Vector2(0f, RuleY));
            _rule.raycastTarget = false;

            // What it *does*, in the roster's own words — the reason one turret is worth more
            // than another, and the one caption here a player has not already read on the shelf.
            _note = UIKit.Shrinkable(
                UIKit.Titled("Note", Content, Loc.Get(Model.NoteKey), 38, Pal.A(Pal.Cream, .88f),
                             TextAnchor.UpperCenter, new Vector2(880f, NoteH), new Vector2(.5f, .5f),
                             new Vector2(0f, NoteY), outline: 3f, shadow: 3f, wrap: true), 26);
            SetAlpha(_note, 0f);

            _sub = UIKit.Shrinkable(
                UIKit.Titled("Sub", Content, Loc.Get("ui.loadout.joined"), 34,
                             Pal.A(_c.Accent, .90f), TextAnchor.MiddleCenter,
                             new Vector2(860f, 52f), new Vector2(.5f, .5f),
                             new Vector2(0f, PipY + 46f), outline: 3f, shadow: 3f), 24);
            SetAlpha(_sub, 0f);

            // The rung, as pips rather than as stars: a star is the game's mark for how well a
            // level was played and it is on the map, the victory panel and the record line. One
            // here would be a fifth meaning for it. These are the seat's own colour, so what
            // they say is "this is a high rung of *this* ladder" and nothing about mastery.
            _pips = new Image[_tier];
            float left = -(_tier - 1) * PipGap * .5f;

            for (int i = 0; i < _tier; i++)
            {
                _pips[i] = UIKit.Img("Pip" + i, Content, Art.Disc(64), Pal.A(Pal.Lift(_tint, .45f), .95f),
                                     Vector2.one * PipSize, new Vector2(.5f, .5f),
                                     new Vector2(left + i * PipGap, PipY));
                _pips[i].raycastTarget = false;
                _pips[i].transform.localScale = Vector3.zero;
            }
        }

        void BuildButtons()
        {
            // Green, not the price pill. `Skins.Buy` is orange because it means *this costs
            // something*; nothing here does — the money has already changed hands, and this key
            // only stands the turret on the seat. Wearing a price pill it would be asking for a
            // second payment, which is precisely the confusion `Skins.Settled`'s own note is
            // about (44: the colour names are roles, so the wrong role is a lie).
            _act = UIKit.Button("Act", Content, Art.S("Ui/" + Skins.Settled), new Vector2(620f, ActH),
                                new Vector2(.5f, .5f), new Vector2(0f, ActY), Act);

            _actRt = (RectTransform)_act.transform;
            _actRt.localScale = Vector3.zero;

            _label = UIKit.Shrinkable(
                UIKit.Titled("Label", _act.transform, Loc.Get("ui.loadout.stand"), 46, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(460f, 66f), new Vector2(.5f, .5f),
                             new Vector2(0f, ActH * UIKit.PillFaceLift), 0f, 3f), 26);

            // Appears with the button rather than at the start. Before the payoff there is
            // nothing to dismiss and a cross would only invite skipping past the reward; tapping
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
        /// The sequence, in the order it is seen. Gaps, never absolute times.
        ///
        /// <para>
        /// Three movements: the room gathers, it breaks, and the turret is standing there
        /// firing. The pause before the break is the whole trick — take it out and the reveal
        /// becomes an announcement.
        /// </para>
        /// <para>
        /// <b>One sound, on the break.</b> The companion reveal shipped ringing on all six of
        /// its beats and played back as a pile-up rather than as a fanfare; this is the same
        /// class of moment and gets the same treatment. It is deliberately not <c>unlock</c>,
        /// which is a rising bell phrase — C5, G5, C6 — and reads as a dong laid over a picture
        /// that is not a bell.
        /// </para>
        /// </summary>
        void Play()
        {
            var cue = new Cue(this);

            // -- gathering ---------------------------------------------------
            cue.With(() =>
            {
                Tween.Fade(_sky, 1f, .30f);
                Tween.Fade(_vignette, VignetteAlpha, .40f);

                if (_aurora != null)
                    for (int i = 0; i < _aurora.Length; i++)
                        Tween.Fade(_aurora[i], AuroraAlpha[i], .70f);
            });

            // Rings collapsing inward. Anticipation is the only thing on screen for half a
            // second, which is what makes the break feel earned rather than sudden.
            cue.Then(.10f, () => Collapse(0));
            cue.Then(.13f, () => Collapse(1));
            cue.Then(.13f, () => Collapse(2));

            // -- the break ---------------------------------------------------
            cue.Then(.30f, () =>
            {
                Audio.Sfx("win", .62f);

                _flash.color = new Color(1f, .99f, .94f, .92f);
                Tween.Fade(_flash, 0f, .38f, Ease.OutQuad);

                Tween.Shake((RectTransform)Content, 26f, .42f);

                _fanRt.localScale = Vector3.one * .35f;
                Tween.Scale(_fanRt, 1f, .70f, Ease.OutQuint);
                Tween.Fade(_fan, FanAlpha, .55f);
                Spin();

                _fan2Rt.localScale = Vector3.one * .3f;
                Tween.Scale(_fan2Rt, 1f, .82f, Ease.OutQuint);
                Tween.Fade(_fan2, Fan2Alpha, .6f);
                Counterspin();

                Tween.Fade(_glow, GlowAlpha, .5f);

                for (int i = 0; i < 2 + _tier; i++) Shockwave(i * .07f, _c.Nth(i));

                Burst.Confetti(Content, 40 + _tier * 22);

                // The plate arrives with the bang and the turret stands up inside it. The
                // stage's own white-out is off, because the screen is already white — two
                // flashes a frame apart is one flash with a seam in it.
                Tween.Pop(_plateRt, .18f, .78f);
                _stage?.Claim(whiteOut: false);
            });

            // -- the name ----------------------------------------------------
            cue.Then(.46f, () =>
            {
                SetAlpha(_name, 1f);
                _name.transform.localScale = Vector3.one * 2.1f;
                Tween.Scale(_name.transform, 1f, .30f, Ease.InCubic)
                     .OnDone(() => Tween.Punch(_name.transform, .16f, .34f));
            });

            cue.Then(.22f, () =>
            {
                Tween.Fade(_rule, .85f, .26f);
                Tween.Run(.34f, Ease.OutCubic, t =>
                {
                    if (!_rule) return;
                    var rt = _rule.rectTransform;
                    rt.sizeDelta = new Vector2(Mathf.Lerp(0f, 420f, t), rt.sizeDelta.y);
                }, _rule);

                Tween.Fade(_note, .88f, .34f);
            });

            // -- the rung ----------------------------------------------------
            cue.Then(.14f, null).Repeat(_tier, .10f, i =>
            {
                if (_pips == null || i >= _pips.Length || !_pips[i]) return;

                Tween.Pop(_pips[i].transform, 0f, .40f);
                Burst.Sparks((RectTransform)_pips[i].transform, Vector2.zero,
                             Pal.Lift(_c.Nth(i), .2f), 6, 110f, 13f, .42f);
            });

            // -- settling ----------------------------------------------------
            cue.Then(.24f, () =>
            {
                Tween.Fade(_sub, .90f, .34f);

                Tween.Pop(_actRt, .6f, .46f);
                Tween.Fade(_dismiss, 1f, .3f);
                _dismiss.blocksRaycasts = true;

                _settled = true;
            });
        }

        // ------------------------------------------------------------------ parts
        /// <summary>A ring closing on the plate, before there is anything there to close on.</summary>
        void Collapse(int index)
        {
            var ring = UIKit.Img("In" + index, Content, Art.Ring(256, 7f), Pal.A(_c.Nth(index), 0f),
                                 Vector2.one * 1100f, new Vector2(.5f, .5f),
                                 new Vector2(0f, PlateY));
            ring.raycastTarget = false;

            Tween.Run(.44f, Ease.InCubic, t =>
            {
                if (!ring) return;
                ring.transform.localScale = Vector3.one * Mathf.Lerp(1f, .16f, t);
                ring.color = Pal.A(_c.Nth(index), .70f * Mathf.Sin(t * Mathf.PI));
            }, ring).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        /// <summary>A ring leaving it, on the break.</summary>
        void Shockwave(float delay, Color colour)
        {
            var ring = UIKit.Img("Wave", Content, Art.Ring(256, 10f), Pal.A(colour, 0f),
                                 Vector2.one * 420f, new Vector2(.5f, .5f),
                                 new Vector2(0f, PlateY));
            ring.raycastTarget = false;

            Tween.Run(.72f, Ease.OutQuint, t =>
            {
                if (!ring) return;
                ring.transform.localScale = Vector3.one * Mathf.Lerp(.2f, 3.4f, t);
                ring.color = Pal.A(colour, .8f * (1f - t));
            }, ring).Delay(delay).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        void Spin()
        {
            Tween.Run(64f, Ease.Linear, t =>
            {
                if (_fanRt) _fanRt.localRotation = Quaternion.Euler(0f, 0f, t * 360f);
            }, _fanRt, "spin").Loop(-1, false);
        }

        void Counterspin()
        {
            Tween.Run(47f, Ease.Linear, t =>
            {
                if (_fan2Rt) _fan2Rt.localRotation = Quaternion.Euler(0f, 0f, -t * 360f);
            }, _fan2Rt, "spin").Loop(-1, false);
        }

        // -------------------------------------------------------------------- act
        /// <summary>Whether this turret is already the one standing on the seat that raised this.</summary>
        bool Standing
        {
            get
            {
                var stood = WardLoadout.Line.At(Colour);
                return stood != null && stood.Id == Model.Id;
            }
        }

        /// <summary>
        /// Stands it on the seat and leaves.
        ///
        /// <b>A mechanism rather than a bell</b>, which is the shelf's own note: standing a
        /// turret is an action a player takes several times in a row and one tap to undo, where
        /// the fanfare is what an earning sounds like — and the earning already sounded.
        /// </summary>
        void Act()
        {
            if (Model == null) { Close(); return; }

            if (Standing) { Close(); return; }

            if (WardLoadout.Choose(WardLine.Colours[Colour], Model.Id))
            {
                Audio.Sfx("stand", .5f);
                Changed?.Invoke();
                Close(quiet: true);
                return;
            }

            Audio.Sfx("blocked", .4f);
        }

        // ------------------------------------------------------------------- skip
        /// <summary>
        /// Ends the sequence now, in the state it was heading for.
        ///
        /// <para>
        /// One pass of assignments rather than a second choreography, which is only possible
        /// because every element already exists — the beats reveal things rather than build
        /// them. That agreement is exactly what a skip path normally gets wrong. Pending beats
        /// are killed by owner; <see cref="Cue"/> schedules every one of them against this
        /// component, which is what makes them cancellable as a group.
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
            // that would have started the turn, and a fan frozen mid-turn is the one part of
            // this that reads as a bug rather than as a fast-forward.
            if (_fanRt) { _fanRt.localScale = Vector3.one; SetAlpha(_fan, FanAlpha); Spin(); }
            if (_fan2Rt) { _fan2Rt.localScale = Vector3.one; SetAlpha(_fan2, Fan2Alpha); Counterspin(); }

            if (_glow) SetAlpha(_glow, GlowAlpha);

            if (_plateRt) _plateRt.localScale = Vector3.one;

            if (_name) { SetAlpha(_name, 1f); _name.transform.localScale = Vector3.one; }
            if (_note) SetAlpha(_note, .88f);
            if (_sub) SetAlpha(_sub, .90f);

            if (_rule)
            {
                SetAlpha(_rule, .85f);
                _rule.rectTransform.sizeDelta = new Vector2(420f, _rule.rectTransform.sizeDelta.y);
            }

            if (_pips != null)
                foreach (var pip in _pips)
                    if (pip) pip.transform.localScale = Vector3.one;

            if (_actRt) _actRt.localScale = Vector3.one;
            if (_dismiss) { _dismiss.alpha = 1f; _dismiss.blocksRaycasts = true; }

            // The drift is owned by each blob rather than by this view, so KillAll never touched
            // it — but the aurora's fade was this view's and has just been assigned instead.
            _settled = true;
        }

        static void SetAlpha(Graphic g, float a)
        {
            if (g == null) return;
            var c = g.color; c.a = a; g.color = c;
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
