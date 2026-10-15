using System.Collections.Generic;
using GlimmerGrove.Localization;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A turret arriving - the ceremony a player meets the moment they have paid for one.
    ///
    /// <para>
    /// <b>It exists because the first attempt was a flourish on a shop panel and read as one.</b>
    /// The purchase used to celebrate itself in place: the price pill became EQUIP, a ring of
    /// waves left the turret and some confetti fell over the panel that had just taken nine
    /// thousand credits. Every piece of that was individually fine and the whole was not a
    /// payoff - which is exactly the note <c>CompanionUnlockOverlay</c> already carries about its
    /// own history, <em>a transaction panel is the wrong place for a payoff, because it is still
    /// wearing the furniture of a decision the player has already made</em>. The preview panel
    /// hands over to this and gets out of the way, which is the companion flow, arrived at the
    /// same way and for the same reason.
    /// </para>
    /// <para>
    /// <b>What makes it a turret's ceremony rather than a companion's is that it fires.</b> A
    /// companion is a portrait and the reveal's job is to show it; a turret is a <em>verb</em>,
    /// and the thing bought is the projectile and the arrangement of raiders its ability is
    /// about. So the middle of this screen is <see cref="WardFiringStage"/> - the shipped one,
    /// drawing through the board's own anchors and scales - running its volley on a loop under
    /// the fanfare. A reveal that stood a still picture of a turret would be hiding the one
    /// thing that was paid for.
    /// </para>
    /// <para>
    /// <b>The room wears the seat's colour, not a rarity's.</b> A turret is bought for one of
    /// the line's four colours (<c>WardHolding</c>), and which one is the fact a player most
    /// needs carried out of this screen - so the sky, the fans, the waves and the rim are all
    /// <c>SiegeView.TintOf(Colour)</c>, and <see cref="Chroma"/> is built around it rather than
    /// looked up from a tier. The tier decides how <em>loud</em> the room is and nothing else.
    /// </para>
    /// <para>
    /// <b>It ends on the next thing they want.</b> The button stands the turret on the seat that
    /// raised this, because the whole reason somebody bought a turret is to use it, and making
    /// them close a celebration to go and find the cell again is a ceremony that gets in the way.
    /// </para>
    /// <para>
    /// <b>Its verb is a summoning</b>, and the upgrade's is an ascension - the two ceremonies
    /// must never be one ceremony at two strengths (invariant 37z's complaint about two bosses
    /// told apart by a hue, asked of a celebration). Here light is pulled <em>in</em> to a point
    /// and goes nova outward; there a star falls, its power runs <em>down</em> a conduit into the
    /// turret and rises back up out of it. See <see cref="Play"/>.
    /// </para>
    /// <para>
    /// <b>The light is <see cref="CeremonyFx"/></b>: pooled, additive, one loop, its own canvas.
    /// Every piece is generated (<c>Art</c>, <c>Lightning</c>), so nothing on this screen has an
    /// address that can arrive as a white rectangle (invariant 7b).
    /// </para>
    /// </summary>
    public sealed class WardRevealOverlay : ModalView
    {
        /// <summary>
        /// The turret being revealed.
        ///
        /// A property rather than a field, because <see cref="WardModel"/> is not
        /// <c>[Serializable]</c> and a public field earns a warning about serialisation that will
        /// never happen - <c>WardPreviewOverlay.Model</c>'s note.
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
        /// proportions a phone draws them on the hill - which is the whole point of showing them
        /// rather than a picture of a turret.
        /// </summary>
        const float PlateW = 760f, PlateH = 700f, StageW = 700f, StageH = 640f, StageCell = 112f;

        /// <summary>
        /// Where each band sits, stated as a <em>middle</em> - <c>UIKit.Box</c> pivots at centre
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
        /// Nothing in this project can look at this screen - there is no render for it and every
        /// numeric gate reads the model - so the one class of fault that would otherwise ship
        /// unseen is a caption drawn through the thing above it. <c>WardRevealTests</c> walks
        /// these edges; it is a poor substitute for a picture and it is what there is.
        /// </summary>
        internal const float NameH = 130f, NoteH = 120f, ActH = 150f, RuleH = 10f;

        const float FanSize = 1220f;
        const float PipSize = 26f, PipGap = 38f;

        const float VignetteAlpha = CeremonySky.VignetteAlpha, FanAlpha = .32f, Fan2Alpha = .22f, GlowAlpha = .44f;

        /// <summary>
        /// Its own hold, never the board's - <see cref="WardFiringStage"/>'s
        /// rule, and its reason: a live board's four turrets must not be released because a
        /// celebration closed.
        /// </summary>
        const string RevealScope = "ward_reveal";

        /// <summary>How flat the summoning circle lies - the hill's own perspective on a ring.</summary>
        const float FloorSquash = .36f;

        // The light's sprites. Generated and cached by key in `Art`, so naming them twice costs
        // nothing; named here so the pool is warmed with exactly what the ceremony throws.
        static Sprite GlowArt => Art.Glow(64, 1.6f);
        static Sprite SparkArt => Art.Spark(64);
        static Sprite GlintArt => Art.Glint(128, 4);
        static Sprite CrystalArt => Art.Crystal(64);
        static Sprite WaveArt => Art.Wave(256, 9f);
        static Sprite RingArt => Art.Ring(256, 5f);

        Image _sky, _vignette, _hush, _fan, _fan2, _glow, _flash, _rim, _plate;
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

        CeremonyFx _fx;
        RectTransform _floor;
        GlyphPart[] _glyph;
        Image _coreBody, _coreHot;
        float _spin, _spinRate = 1f;
        bool _ambient;

        /// <summary>
        /// Every one-shot piece of light still alive, so a skip can take them all away at once - a
        /// flare left burning over a settled screen is the leak <see cref="Skip"/> exists to make
        /// impossible.
        /// </summary>
        readonly List<GameObject> _transient = new List<GameObject>();

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

            // Over the plate and under the captions, so light can burn across the turret and
            // never across a word. Warmed now, while the room is still fading in.
            _fx = CeremonyFx.Attach(Content, "Light",
                                    (GlowArt, 110), (SparkArt, 150), (GlintArt, 60),
                                    (CrystalArt, 28), (WaveArt, 12), (RingArt, 8));

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
        /// wearing tier one - the mistake <c>GroveLand.NextForSale</c> made and paid for.
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

            // The ladder as it is authored, read off the roster rather than typed - so a drop
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
        /// The scheme's job is the same - a tint, a partner to cross it with, an accent and a
        /// deep to sit it all on - so the struct is right and only the way in differs. The
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
            //
            // Shared with the rank ceremony and the upgrade one (`CeremonySky`), which is why it
            // is not the seat's own deep hue any more: all three stood their subject on a
            // near-black room and all three came back as *so dark*. **The seat's colour has not
            // gone anywhere** - the fans, the waves, the glow and the rim below are still
            // `SiegeView.TintOf(Colour)`, which is what this screen's remarks are about; what
            // changed is the ground it is lit against, not the light.
            _sky = CeremonySky.Ground(Content, Skip);

            BuildAurora();

            Fireflies.Spawn(Content, 14, Pal.A(_c.Partner, .6f), 4f, 13f);

            // Last, so it holds the aurora and the fireflies in too. Tinted with the room
            // rather than with ink, or the corners end up the one grey thing on a coloured
            // screen - see `CeremonySky.VignetteInk`, which is where that now lives.
            _vignette = CeremonySky.Veil(Content);

            // The hush: the room dimmed under the gathering and uncovered on the break. In the
            // wash's own ink rather than black, for `CeremonySky.Ink`'s reason. See `Play`.
            _hush = UIKit.Img("Hush", Content, Art.Pixel, Pal.A(CeremonySky.Ink, 0f));
            UIKit.StretchTo((RectTransform)_hush.transform, 0, 0, 0, 0);
            _hush.raycastTarget = false;

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

        // Where the three masses of light sit. A composition rather than a scatter - one high
        // and left, one across the middle, one low - so the frame is lit unevenly, the way a
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
        /// snaps back every time the tween wraps - which on something this large is the most
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

            // What it *does*, in the roster's own words - the reason one turret is worth more
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
            // something*; nothing here does - the money has already changed hands, and this key
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
        /// <b>Four movements: the room hushes, the light is summoned, it collapses, and it goes
        /// nova.</b> The first cut was gather-break-settle in two thirds of a second, and every
        /// beat of it was a ring or a fan - the owner's verdict was <em>dull</em>, and the reason
        /// is that nothing in it had a <em>shape over time</em>. This one is built on the oldest
        /// rule a payoff has: the release is only as big as the tension before it. So the room
        /// darkens, light is dragged out of the corners into a core over a summoning circle, the
        /// core beats faster and faster with lightning crawling off it, and then everything is
        /// pulled into a single point and held there - silent and still for a sixth of a second -
        /// before it detonates into the turret.
        /// </para>
        /// <para>
        /// <b>The hush is the trick, and it is transient on purpose.</b> The room is bright by the
        /// owner's rule (<c>CeremonySky</c>), and additive light on a bright room has nowhere to
        /// go - so for one second the ground is dimmed under the gathering and the light reads as
        /// light, and on the break the dim is lifted in the same instant the white-out lands. The
        /// screen does not get <em>lit</em>, it gets <em>uncovered</em>, which is a far bigger
        /// change than any flash could make on its own. The settled screen is exactly as bright
        /// as it always was.
        /// </para>
        /// <para>
        /// <b>One sound, on the break.</b> The companion reveal shipped ringing on all six of
        /// its beats and played back as a pile-up rather than as a fanfare; this is the same
        /// class of moment and gets the same treatment. It is deliberately not <c>unlock</c>,
        /// which is a rising bell phrase - C5, G5, C6 - and reads as a dong laid over a picture
        /// that is not a bell. The silence before it is now part of the sound.
        /// </para>
        /// </summary>
        void Play()
        {
            var cue = new Cue(this);

            // -- the hush ----------------------------------------------------
            cue.With(() =>
            {
                Tween.Fade(_sky, 1f, .30f);
                Tween.Fade(_vignette, VignetteAlpha, .40f);
                Tween.Fade(_hush, HushAlpha, .50f, Ease.OutCubic);

                if (_aurora != null)
                    for (int i = 0; i < _aurora.Length; i++)
                        Tween.Fade(_aurora[i], AuroraAlpha[i], .70f);

                Summon();
            });

            // -- the heartbeat ------------------------------------------------
            // Each beat closer to the last, so the core is visibly about to give - the gap is the
            // tension, and a steady pulse would be a light that is merely on.
            for (int i = 0; i < BeatGaps.Length; i++)
            {
                int beat = i;
                cue.Then(BeatGaps[i], () => Heartbeat(beat));
            }

            // -- the collapse --------------------------------------------------
            cue.Then(CollapseAfter, Implode);

            // -- the nova ------------------------------------------------------
            cue.Then(HoldFor, Nova);

            // -- the name ----------------------------------------------------
            cue.Then(.46f, () =>
            {
                SetAlpha(_name, 1f);
                _name.transform.localScale = Vector3.one * 2.1f;
                Tween.Scale(_name.transform, 1f, .30f, Ease.InCubic)
                     .OnDone(() =>
                     {
                         Tween.Punch(_name.transform, .16f, .34f);
                         Underline(new Vector2(0f, NameY), NameW(), Pal.Lift(_tint, .45f));
                     });
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

                // A bead of light running out to each end of the rule as it draws, so the rule is
                // something being drawn rather than something growing.
                Bead(new Vector2(0f, RuleY), new Vector2(-210f, RuleY));
                Bead(new Vector2(0f, RuleY), new Vector2(210f, RuleY));

                Tween.Fade(_note, .88f, .34f);
            });

            // -- the rung ----------------------------------------------------
            cue.Then(.14f, null).Repeat(_tier, .10f, i =>
            {
                if (_pips == null || i >= _pips.Length || !_pips[i]) return;

                Tween.Pop(_pips[i].transform, 0f, .40f);

                var at = ((RectTransform)_pips[i].transform).anchoredPosition;
                Twinkle(at, Pal.Lift(_c.Nth(i), .55f), 170f, .48f);
                _fx.Spray(SparkArt, at, new[] { Pal.Lift(_c.Nth(i), .3f), Color.white }, 10,
                          140f, 320f, 16f, .45f, gravity: 500f, stretch: .03f);
            });

            // -- settling ----------------------------------------------------
            cue.Then(.24f, () =>
            {
                Tween.Fade(_sub, .90f, .34f);

                Tween.Pop(_actRt, .6f, .46f);
                Tween.Fade(_dismiss, 1f, .3f);
                _dismiss.blocksRaycasts = true;

                _settled = true;
                Ambient();
            });
        }

        // ----------------------------------------------------------------- timing
        /// <summary>
        /// The heartbeat's gaps, shortening. The last is the shortest, which is what makes the
        /// collapse after it feel like the beat that did not come.
        /// </summary>
        static readonly float[] BeatGaps = { .24f, .22f, .17f, .13f, .10f };

        /// <summary>From the last beat to the collapse, and from the collapse to the nova.</summary>
        const float CollapseAfter = .12f, HoldFor = .17f;

        /// <summary>How long light is dragged into the core - the gathering ends on the collapse.</summary>
        static float GatherFor
        {
            get
            {
                float sum = CollapseAfter;
                foreach (var gap in BeatGaps) sum += gap;
                return sum;
            }
        }

        /// <summary>How dark the room is held under the gathering. Gone on the break.</summary>
        const float HushAlpha = .52f, HushDeep = .68f;

        // ----------------------------------------------------------------- summon
        /// <summary>
        /// Where the light gathers and the turret will stand.
        ///
        /// <b>The turret's own middle, read off the stage</b> rather than the plate's: the stage
        /// stands its turret low and keeps the top for the bolt, so the plate's centre is empty
        /// air two cells above the thing being revealed, and a nova there would detonate over its
        /// head.
        /// </summary>
        Vector2 Core => new Vector2(0f, PlateY) + (_stage != null ? _stage.TurretPoint : Vector2.zero);

        /// <summary>Where the summoning circle lies - on the floor under the core, at the turret's feet.</summary>
        Vector2 Floor => Core + new Vector2(0f, -StageCell * 1.05f);

        /// <summary>
        /// The opening: a circle drawn on the floor, a core over it, and light dragged in out of
        /// the dark along spirals.
        ///
        /// <b>A circle on the floor rather than a ring in the air</b>, because the floor is what
        /// says <em>something is about to stand here</em>. It is squashed to the hill's own
        /// perspective, and every part of it turns - the outer ring slowly, the arcs inside it
        /// fast and against each other - so it reads as machinery winding up rather than as a
        /// decal.
        /// </summary>
        void Summon()
        {
            _floor = UIKit.Box("Circle", _fx.Node, Vector2.one, new Vector2(.5f, .5f), Floor);
            _floor.localScale = new Vector3(.5f, .5f * FloorSquash, 1f);
            _transient.Add(_floor.gameObject);

            _glyph = new[]
            {
                Glyph(Art.Glow(128, 1.4f), _tint, 760f, .38f, 0f),
                Glyph(Art.Ring(256, 4f), Pal.Lift(_tint, .35f), 640f, .85f, 26f),
                Glyph(Art.HexRing(256, 6f), _c.Partner, 500f, .75f, -48f),
                Glyph(Art.Arc(256, 12f, .62f), Pal.Lift(_c.Accent, .3f), 580f, .9f, 150f),
                Glyph(Art.Arc(256, 9f, .45f), Pal.Lift(_c.Partner, .4f), 400f, .9f, -210f),
            };

            Tween.Run(.48f, Ease.OutBack, t =>
            {
                if (_floor) _floor.localScale = new Vector3(Mathf.Lerp(.5f, 1f, t), Mathf.Lerp(.5f, 1f, t) * FloorSquash, 1f);
            }, this);

            foreach (var g in _glyph)
            {
                float to = g.Img.color.a;
                SetAlpha(g.Img, 0f);
                Tween.Fade(g.Img, to, .45f, Ease.OutCubic);
            }

            // The core: a hot point inside a coloured body, small at first.
            _coreBody = _fx.Light("Core", Art.Glow(128, 1.8f), Pal.A(Pal.Lift(_tint, .35f), 0f),
                                  Vector2.one * 70f, Core);
            _coreHot = _fx.Light("Hot", Art.Glow(64, 3f), Pal.A(Color.white, 0f),
                                 Vector2.one * 40f, Core);
            _transient.Add(_coreBody.gameObject);
            _transient.Add(_coreHot.gameObject);

            Tween.Fade(_coreBody, .95f, .35f);
            Tween.Fade(_coreHot, 1f, .35f);

            var colours = new[]
            {
                Pal.Lift(_tint, .35f), Pal.Lift(_c.Partner, .3f), Pal.Lift(_c.Accent, .4f), Color.white,
            };

            _fx.Vortex(GlowArt, Core, colours, 66f + _tier * 6f, GatherFor - .10f, 380f, 760f, 26f);
            _fx.Vortex(SparkArt, Core, colours, 30f, GatherFor - .10f, 300f, 620f, 18f, 3.4f);
        }

        struct GlyphPart
        {
            public Image Img;
            public float Speed;
        }

        GlyphPart Glyph(Sprite sprite, Color colour, float size, float alpha, float speed)
        {
            var img = UIKit.Img("Glyph", _floor, sprite, Pal.A(colour, alpha), Vector2.one * size,
                                new Vector2(.5f, .5f), Vector2.zero);
            img.raycastTarget = false;
            Additive.Lit(img);

            return new GlyphPart { Img = img, Speed = speed };
        }

        /// <summary>The circle's turn. Its own clock, so it can wind up on the collapse.</summary>
        void Update()
        {
            if (_glyph == null || !_floor) return;

            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _spin += dt * _spinRate;

            for (int i = 0; i < _glyph.Length; i++)
                if (_glyph[i].Img)
                    _glyph[i].Img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, _glyph[i].Speed * _spin);
        }

        /// <summary>
        /// One beat of the core: it swells and kicks, a ring closes on it, and lightning crawls
        /// off it a little further each time.
        /// </summary>
        void Heartbeat(int beat)
        {
            if (!_coreBody) return;

            float k = (beat + 1f) / BeatGaps.Length;
            float size = Mathf.Lerp(90f, 210f, k);

            Kick(_coreBody, size, 1.35f);
            Kick(_coreHot, size * .42f, 1.5f);

            _spinRate = Mathf.Lerp(1f, 2.6f, k);

            // A ring closing *in* - the idiom this game keeps for "this one" rather than "something
            // went off here" (CRAFT: a mechanic the board cannot show reads as a bug).
            _fx.Emit(new CeremonyFx.Mote
            {
                Sprite = WaveArt, Colour = Pal.A(Pal.Lift(_c.Nth(beat), .3f), .75f), At = Core,
                Life = .30f, Size = 620f - beat * 40f, SizeEnd = size * .6f, Grow = Ease.InCubic,
                FadeIn = .35f, FadePower = .8f,
            });

            Crackle(Core, size * .3f, 150f + 55f * beat, 3 + beat, Pal.Lift(_tint, .25f), 3.2f + beat * .5f,
                    .16f, 2);
        }

        /// <summary>
        /// Everything pulled into one point. The circle winds up and is swallowed, the core
        /// shrinks to a pinprick, the room goes darker still - and then nothing moves.
        /// </summary>
        void Implode()
        {
            _spinRate = 7f;

            Tween.Fade(_hush, HushDeep, .14f);

            if (_floor)
            {
                var floor = _floor;
                var from = floor.localScale;
                Tween.Run(.16f, Ease.InCubic, t =>
                {
                    if (floor) floor.localScale = Vector3.LerpUnclamped(from, new Vector3(.06f, .06f * FloorSquash, 1f), t);
                }, this);
            }

            Shrink(_coreBody, 18f, .15f);
            Shrink(_coreHot, 10f, .15f);

            // The last thing the eye sees before the bang: one tight ring snapping shut.
            _fx.Emit(new CeremonyFx.Mote
            {
                Sprite = RingArt, Colour = Pal.A(Color.white, .9f), At = Core, Life = .15f,
                Size = 380f, SizeEnd = 8f, Grow = Ease.InCubic, FadeIn = .2f, FadePower = .4f,
            });
        }

        // ------------------------------------------------------------------- nova
        /// <summary>
        /// The break: the room is uncovered, the core goes nova, and the turret is standing in it.
        ///
        /// <para>
        /// <b>Built like a real flare rather than a stack of rings.</b> A white-hot core, a
        /// four-point star with needle rays turning as it fades, an anamorphic streak thrown
        /// across the screen and a pillar of light standing up out of the floor - each a
        /// different <em>shape</em> of the same light, which is what makes a flash read as an
        /// event with a body rather than a frame that went white. All of it additive, so where
        /// the pieces cross they burn white the way light does.
        /// </para>
        /// <para>
        /// <b>Then the matter</b>: sparks hot enough to streak, slowed by the air and pulled
        /// down; soft embers; crystal shards tumbling; and a slow rain of glints from above that
        /// is still falling when the name lands. The rain replaces the confetti, which reads the
        /// same at a level clear as at this - CRAFT's rule that a celebration should say how big
        /// it was, not merely that something was good.
        /// </para>
        /// </summary>
        void Nova()
        {
            Audio.Sfx("win", .62f);

            _flash.color = new Color(1f, .99f, .94f, .92f);
            Tween.Fade(_flash, 0f, .42f, Ease.OutQuad);

            Tween.Fade(_hush, 0f, .36f, Ease.OutQuad);

            Tween.Shake((RectTransform)Content, 26f, .42f);
            Zoom(1.075f, .55f);

            _spinRate = 1f;
            ClearTransient();

            _fanRt.localScale = Vector3.one * .35f;
            Tween.Scale(_fanRt, 1f, .70f, Ease.OutQuint);
            Tween.Fade(_fan, FanAlpha, .55f);
            Spin();

            _fan2Rt.localScale = Vector3.one * .3f;
            Tween.Scale(_fan2Rt, 1f, .82f, Ease.OutQuint);
            Tween.Fade(_fan2, Fan2Alpha, .6f);
            Counterspin();

            Tween.Fade(_glow, GlowAlpha, .5f);

            var at = Core;
            var hot = Pal.Lift(_tint, .5f);

            // -- the flare: core, star, streak, pillar ------------------------
            Flare(Art.Glow(128, 1.6f), at, Pal.A(Color.white, .95f), new Vector2(120f, 120f),
                  new Vector2(1100f, 1100f), 0f, 0f, .60f, Ease.OutQuint);

            Flare(GlintArt, at, Pal.A(Pal.Lift(_tint, .7f), 1f), new Vector2(320f, 320f),
                  new Vector2(1500f + _tier * 80f, 1500f + _tier * 80f), 0f, 38f, .78f, Ease.OutCubic);

            Flare(Art.Glow(64, 1.4f), at, Pal.A(hot, .9f), new Vector2(1900f, 80f),
                  new Vector2(2300f, 8f), 0f, 0f, .62f, Ease.OutCubic);

            Pillar(at, hot);

            // -- the waves ----------------------------------------------------
            for (int i = 0; i < 2 + _tier; i++)
                _fx.Shock(WaveArt, at, Pal.A(Pal.Lift(_c.Nth(i), .25f), .85f), 140f, 1500f + i * 160f,
                          .80f, i * .07f);

            _fx.Shock(RingArt, Floor, Pal.A(hot, .9f), 120f, 1700f, .70f, 0f, FloorSquash);

            // -- the matter ---------------------------------------------------
            var bright = new[] { Color.white, Pal.Lift(_tint, .4f), Pal.Lift(_c.Partner, .35f), Pal.Lift(_c.Accent, .35f) };

            _fx.Spray(SparkArt, at, bright, 56 + _tier * 10, 700f, 1750f, 28f, .95f,
                      gravity: 1100f, drag: 2.4f, stretch: .055f);

            _fx.Spray(GlowArt, at, bright, 26, 260f, 720f, 30f, 1.4f, gravity: 260f, drag: 1.6f,
                      stretch: 0f, lift: 120f);

            _fx.Shards(CrystalArt, at, bright, 14 + _tier * 2, 520f, 1150f, 42f, 1.35f);

            Crackle(at, 70f, 560f, 7, hot, 6f, .34f, 3);

            _fx.Starfall(GlintArt, 520f, 1000f, new[] { Pal.Lift(Pal.Gold, .3f), Pal.Cream, Pal.Lift(_tint, .5f) },
                         16f + _tier * 4f, 2.4f, 46f);

            // The plate arrives with the bang and the turret stands up inside it. The stage's own
            // white-out is off, because the screen is already white - two flashes a frame apart
            // is one flash with a seam in it.
            Tween.Pop(_plateRt, .18f, .78f);
            _stage?.Claim(whiteOut: false);
        }

        // ------------------------------------------------------------------ parts
        /// <summary>
        /// One shaped piece of the flare: grows from one size to another, turns, and fades.
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

        /// <summary>
        /// A column of light standing up out of the floor and narrowing as it fades - the moment
        /// read from across the room. It grows out of its foot (<c>Art.Shaft</c>) rather than
        /// being cut off at it.
        /// </summary>
        void Pillar(Vector2 at, Color colour)
        {
            var img = _fx.Light("Pillar", Art.Shaft(64, 256, 1.5f, .08f), Pal.A(colour, .85f),
                                new Vector2(320f, 2100f), at);
            _transient.Add(img.gameObject);

            var rt = img.rectTransform;
            rt.pivot = new Vector2(.5f, 0f);
            rt.anchoredPosition = Floor;

            Tween.Run(1.1f, Ease.Linear, t =>
            {
                if (!img) return;

                float e = Ease.OutCubic(t);
                rt.sizeDelta = new Vector2(Mathf.Lerp(320f, 36f, e), Mathf.Lerp(1500f, 2300f, e));
                img.color = Pal.A(colour, .85f * (1f - t * t));
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
        void Twinkle(Vector2 at, Color colour, float size, float life)
        {
            _fx.Emit(new CeremonyFx.Mote
            {
                Sprite = GlintArt, Colour = colour, At = at, Life = life, Size = size * .3f,
                SizeEnd = size, Grow = Ease.OutCubic, Rotation = 0f, Spin = 60f, FadePower = 1.8f,
            });
        }

        /// <summary>A bead of light running along a line and gone.</summary>
        void Bead(Vector2 from, Vector2 to)
        {
            var colour = Pal.A(Pal.Lift(_tint, .6f), 1f);

            _fx.Emit(new CeremonyFx.Mote
            {
                Sprite = GlowArt, Colour = colour, At = from, Velocity = (to - from) / .34f,
                Life = .40f, Size = 40f, SizeEnd = 18f, Drag = 1.5f, Stretch = .06f, FadePower = 1.2f,
            });
        }

        /// <summary>
        /// A streak of light drawn under a caption as it lands, and a glint at each end of it.
        /// The name is the second thing a player reads; this is what makes it the second event.
        /// </summary>
        void Underline(Vector2 at, float width, Color colour)
        {
            Flare(Art.Glow(64, 1.4f), at, Pal.A(colour, .8f), new Vector2(width * .2f, 40f),
                  new Vector2(width * 1.35f, 10f), 0f, 0f, .70f, Ease.OutCubic);

            Twinkle(at + new Vector2(-width * .5f, 0f), Pal.A(Color.white, .95f), 150f, .55f);
            Twinkle(at + new Vector2(width * .5f, 0f), Pal.A(Color.white, .95f), 150f, .55f);
        }

        /// <summary>How wide the name is drawn, for the streak under it.</summary>
        float NameW() => _name ? Mathf.Clamp(_name.preferredWidth, 240f, 900f) : 600f;

        /// <summary>
        /// The whole room leaning in and settling back - a camera punch. Owned by this view, so
        /// a skip kills it and puts the scale back.
        /// </summary>
        void Zoom(float from, float life)
        {
            var content = (RectTransform)Content;
            content.localScale = Vector3.one * from;

            Tween.Run(life, Ease.OutCubic, t =>
            {
                if (content) content.localScale = Vector3.one * Mathf.Lerp(from, 1f, t);
            }, this);
        }

        /// <summary>A light kicking up past a new size and settling onto it.</summary>
        void Kick(Image img, float size, float over)
        {
            if (!img) return;

            // Owned by the light and on one channel with `Shrink`, so a kick still settling when
            // the collapse begins is replaced rather than fought.
            var rt = img.rectTransform;
            Tween.Run(.20f, Ease.OutCubic, t =>
            {
                if (rt) rt.sizeDelta = Vector2.one * size * Mathf.Lerp(over, 1f, t);
            }, img, "size");
        }

        void Shrink(Image img, float size, float life)
        {
            if (!img) return;

            var rt = img.rectTransform;
            var from = rt.sizeDelta;
            Tween.Run(life, Ease.InCubic, t =>
            {
                if (rt) rt.sizeDelta = Vector2.LerpUnclamped(from, Vector2.one * size, t);
            }, img, "size");
        }

        /// <summary>
        /// What stays alive after the noise: embers lifting off the turret's feet, and a glint now
        /// and then catching the plate's rim. Quiet, slow and endless, so the screen the player
        /// reads their turret's note on is still a lit room rather than a stopped one.
        /// </summary>
        void Ambient()
        {
            if (_ambient) return;
            _ambient = true;

            _fx.Rise(GlowArt, Floor + new Vector2(0f, 30f), 170f,
                     new[] { Pal.Lift(_tint, .45f), Pal.Lift(_c.Partner, .4f), Pal.Cream }, 7f, -1f, 20f, 110f);

            _fx.Run(1.4f, -1f, i =>
            {
                // Walked round the rim by the golden ratio, so successive glints never land
                // near each other and the whole rim is visited evenly.
                var edge = RimPoint(Mathf.Repeat(i * .618034f, 1f));

                return new CeremonyFx.Mote
                {
                    Sprite = GlintArt, Colour = Pal.A(Pal.Lift(_tint, .7f), .95f),
                    At = new Vector2(0f, PlateY) + edge, Life = .8f, Size = 20f, SizeEnd = 110f,
                    Grow = Ease.InOutSine, FadeIn = .45f, FadePower = 1.4f, Spin = 45f,
                };
            });

            if (_glow)
                Tween.Run(2.8f, Ease.InOutSine, t =>
                {
                    if (_glow) SetAlpha(_glow, Mathf.Lerp(GlowAlpha, GlowAlpha * .7f, t));
                }, _glow, "breath").Loop(-1, true);
        }

        /// <summary>
        /// A point on the plate's rim, <paramref name="u"/> of the way round it clockwise from the
        /// top-left, inset onto the drawn rim line rather than the plate's outer edge.
        /// </summary>
        static Vector2 RimPoint(float u)
        {
            const float Inset = 6f;
            float w = PlateW - Inset * 2f, h = PlateH - Inset * 2f;
            float d = Mathf.Repeat(u, 1f) * 2f * (w + h);

            if (d < w) return new Vector2(-w * .5f + d, h * .5f);
            d -= w;
            if (d < h) return new Vector2(w * .5f, h * .5f - d);
            d -= h;
            if (d < w) return new Vector2(w * .5f - d, -h * .5f);
            d -= w;
            return new Vector2(-w * .5f, -h * .5f + d);
        }

        void ClearTransient()
        {
            foreach (var go in _transient)
                if (go) Destroy(go);

            _transient.Clear();
            _glyph = null;
            _floor = null;
            _coreBody = _coreHot = null;
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
        /// the fanfare is what an earning sounds like - and the earning already sounded.
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
        /// because every element already exists - the beats reveal things rather than build
        /// them. That agreement is exactly what a skip path normally gets wrong. Pending beats
        /// are killed by owner; <see cref="Cue"/> schedules every one of them against this
        /// component, which is what makes them cancellable as a group.
        /// </para>
        /// </summary>
        void Skip()
        {
            if (_settled) return;

            Tween.KillAll(this);

            // The light first: every mote back in its pool and every flare, bolt and circle
            // gone, so nothing from a beat that never finished is left burning on the screen.
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
            // it - but the aurora's fade was this view's and has just been assigned instead.
            _settled = true;

            // Straight into the settled room's own life, so a skip lands on the same screen the
            // sequence would have ended on rather than a stiller one.
            Ambient();
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
