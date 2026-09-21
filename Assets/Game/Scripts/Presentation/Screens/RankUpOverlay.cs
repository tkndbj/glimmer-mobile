using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Localization;
using GlimmerGrove.Ranks;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A rank arriving: the ceremony a run's ending stops for, before it says anything about the
    /// run.
    ///
    /// <para>
    /// <b>What makes this a rank's ceremony rather than a second turret reveal is that a rank
    /// has a below and an above.</b> Everything else this game celebrates is a <em>thing</em>
    /// — a turret, a chest, a companion — and the shape those reveals share is right for a
    /// thing: the room gathers, it breaks, and the object is standing there. A rung is not an
    /// object. It is a <em>position</em>, and the two facts a player wants out of it are how
    /// they got here and how far up the ladder here is. So this is built as a climb: a shaft of
    /// light falling past the frame, the badge below rising into the light that becomes the
    /// badge above, and a rail at the foot that lights from the bottom to the rung reached.
    /// Copying the reveal's collapsing rings would have said "you have been given something",
    /// which is the one thing a rank never is.
    /// </para>
    /// <para>
    /// <b>The gathering is made out of the rung's own requirements, and that is why it works for
    /// a ladder nobody has authored yet.</b> One mote of light flies in per line of the rung
    /// (<c>RankDefinition.Requirements</c>) — so a rung asking one thing is met by a single
    /// comet and a rung asking eight by a swarm, and a content push that retunes the ladder
    /// redraws this without a build (invariant 52a). Nothing here is keyed on a rung id, a
    /// count of rungs or a measure: the badge, the name and the blurb are derived from the id
    /// (invariant 52f), the metal is arithmetic on the ordinal (<see cref="RankLook"/>), and the
    /// rail is drawn from <c>RankLadder.Count</c>.
    /// </para>
    /// <para>
    /// <b>It draws nothing it cannot resolve.</b> A rung whose badge is not on this build's disc
    /// is drawn as light alone rather than as a white rectangle (invariant 7b, and
    /// <see cref="RankArt"/>'s whole argument); a name or blurb this build's
    /// <c>loc/en.json</c> cannot resolve is left out rather than printed as a raw key. Both are
    /// reachable the day a remote content push adds a rung ahead of the client that reads it.
    /// </para>
    /// <para>
    /// <b>No new art and no new address.</b> Every sprite here is either procedural
    /// (<see cref="Art"/>) or already in the global preload set — the badges are
    /// <c>AssetManifest.RankAssets</c> and the key is the interface kit's. That is deliberate
    /// rather than lucky: art written with the Editor closed is unaddressed, and an unaddressed
    /// sprite two cells wide over the loudest moment in the game is the fault this project has
    /// paid for more than any other.
    /// </para>
    /// <para>
    /// <b>Everything is built hidden and then revealed</b>, never built by the beats —
    /// <c>WinOverlay</c>'s rule, and what makes <see cref="Skip"/> one pass of assignments
    /// rather than a second choreography that can disagree with the first.
    /// </para>
    /// </summary>
    public sealed class RankUpOverlay : ModalView
    {
        /// <summary>
        /// The rung reached. A property rather than a field because <see cref="RankDefinition"/>
        /// is not <c>[Serializable]</c> — <c>WardRevealOverlay.Model</c>'s note.
        /// </summary>
        public RankDefinition Rung { get; set; }

        /// <summary>
        /// The rung below it, or null at the bottom of the ladder.
        ///
        /// Drawn as the badge that rises up the shaft and is spent becoming this one, so null is
        /// an ordinary state and not a fault: the first rank in the game is climbed to from
        /// nothing, and what rises then is the light alone.
        /// </summary>
        public RankDefinition From { get; set; }

        /// <summary>
        /// What happens when this is done with — the run's own panel, normally.
        ///
        /// <para>
        /// <b>Fired on close and again on destroy, and <see cref="RankCeremony"/> holds the latch
        /// that makes the second one free.</b> A panel that never arrives leaves somebody on a
        /// finished board being told nothing about what it paid, so the continuation may not
        /// depend on this overlay ending tidily: a screen change destroys every modal, and that
        /// has to come out here as "carry on" rather than as silence.
        /// </para>
        /// </summary>
        public System.Action Then { get; set; }

        // ------------------------------------------------------------------ geometry
        /// <summary>
        /// Where each band sits, as a <em>middle</em> — <c>UIKit.Box</c> pivots at centre
        /// whatever it is anchored to, which is the arithmetic two screens in this project have
        /// already recorded getting wrong and drawing one caption through another.
        ///
        /// <para>
        /// The canvas is 1920 tall in reference units on every device — the scaler matches on
        /// width, so a squarer screen grows <em>wider</em> and never shorter (<c>CanvasFit</c>)
        /// — so this runs from +960 to -960 and each band below has been checked against its
        /// neighbours' <em>edges</em> rather than their centres: the eyebrow +662..+718, the
        /// badge +520..+80, the name -10..-150, the rule -175..-185, the blurb -210..-350, the
        /// rail -453..-487, its caption -528..-572 and the key -655..-805.
        /// </para>
        /// </summary>
        const float EyebrowY = 690f, BadgeY = 300f, NameY = -80f, RuleY = -180f;
        const float BlurbY = -280f, RailY = -470f, RailTextY = -550f, ActY = -730f;

        /// <summary>
        /// How tall each band is drawn. Named so the gaps above can be checked.
        ///
        /// <para>
        /// <b>A shrinkable label needs a box taller than its own type</b>, and the eyebrow's was
        /// not: <c>UIKit.Shrinkable</c> is uGUI's Best Fit over a <em>wrapped</em>, truncating
        /// label, so it measures a line box of 1.2x the size and refuses any size whose line box
        /// is taller than the plate. At 44 in a 44-unit band it settled on 36 — a caption a fifth
        /// smaller than the one written down, on every device, for ever, with nothing to say so.
        /// <c>render_rank_ceremony.py --captions</c> found it before this screen was ever drawn;
        /// the rule it buys is that every band here is at least 1.2x the type it carries.
        /// </para>
        /// </summary>
        const float EyebrowH = 56f, NameH = 140f, RuleH = 10f, BlurbH = 140f;
        const float RailTextH = 44f, ActH = 150f;

        /// <summary>The badge, and the light it stands in.</summary>
        const float BadgeSize = 440f, HaloSize = 980f, FanSize = 1320f;

        /// <summary>
        /// The shaft: the column of light the climb happens inside.
        ///
        /// Narrow enough that the badge breaks out of it on both sides, which is what stops it
        /// reading as a background panel the badge is sitting on.
        /// </summary>
        const float ShaftWidth = 300f, ShaftGlowWidth = 720f;

        /// <summary>
        /// The rail at the foot: one pip per rung of the whole ladder.
        ///
        /// <see cref="PipMax"/> is the size a pip is drawn at when there is room; a long ladder
        /// closes the gap rather than overlapping, because <c>RankLadder.MaxRungs</c> is 24 and
        /// twenty-four 34-unit pips across 860 would touch.
        /// </summary>
        const float RailWidth = 860f, RailTrough = 12f, PipMax = 34f;

        /// <summary>
        /// How much larger the rung reached is drawn than the rest of the rail.
        ///
        /// Named because <c>render_rank_ceremony.py</c> draws it too and a mirror that reported
        /// a pip the same size as its neighbours would be answering the wrong question about the
        /// one thing the rail exists to say (invariant 44d).
        /// </summary>
        const float PipHeldScale = 1.4f;

        /// <summary>How far the whole composition drifts upward on the ascent.</summary>
        const float Climb = 76f;

        const float VignetteAlpha = CeremonySky.VignetteAlpha, FanAlpha = .30f, Fan2Alpha = .20f;
        const float HaloAlpha = .52f, ShaftAlpha = .34f, ShaftGlowAlpha = .26f;

        /// <summary>How long one mote takes to reach the core, and how far apart they leave.</summary>
        const float MoteFlight = .92f, MoteGap = .13f;

        /// <summary>How many falling streaks the shaft carries. See <see cref="BuildShaft"/>.</summary>
        const int Streaks = 14;

        /// <summary>
        /// The gap between the breath and the break, named because the fanfare is placed against
        /// it. See <see cref="Play"/>.
        /// </summary>
        const float StrikeGap = .26f;

        /// <summary>
        /// How far into <c>rankup</c> its rise tops out, in seconds.
        ///
        /// <para>
        /// <b>Measured off the shipped wav, not read off the file name.</b> The clip is called a
        /// buildup and is not one in the shape that name suggests: its amplitude is loudest in
        /// its first tenth and decays from there, and what actually rises is the <em>pitch</em>
        /// - a dominant partial climbing 301 Hz to 5.5 kHz over this many seconds, after which
        /// it falls away into a sparkle tail. Placed by the clip's length, its arrival would
        /// have landed three quarters of a second after the badge; placed by its loudest
        /// instant, the top of the sweep would have come in the middle of the climb. So the
        /// screen's strike is aligned to <em>this</em> instant and the tail rings on through the
        /// ascent, which is the whole of what one sound has to do here.
        /// </para>
        /// </summary>
        const float FanfareRise = .55f;

        // ------------------------------------------------------------------ the parts
        Image _sky, _vignette, _fanA, _fanB, _halo, _core, _flash;
        Image _shaft, _shaftGlow, _badge, _ghost;
        Image _rule, _railFill;
        Image[] _aurora, _pips;
        RectTransform _stage, _fanRtA, _fanRtB, _shaftRt, _badgeRt, _ghostRt, _railRt;
        Text _eyebrow, _name, _blurb, _railText;
        Btn _act;
        CanvasGroup _dismiss;
        RectTransform _actRt;

        readonly List<Image> _motes = new List<Image>();

        Chroma _c;
        Color _metal;
        bool _settled, _struck, _spent, _skipped, _rang;
        int _pipHeld;

        // ------------------------------------------------------------------- building
        protected override void Build()
        {
            if (Rung == null) { Close(Carry); return; }

            _metal = RankLook.Metal(Rung.Ordinal);
            _c = SchemeFor(_metal);

            BuildRoom();
            BuildShaft();
            BuildStage();
            BuildCaptions();
            BuildRail();
            BuildButtons();

            // Above everything and built last, so the strike whites out the whole composition
            // rather than whatever happened to be built before it.
            _flash = UIKit.Img("Flash", Content, Art.Pixel, new Color(1f, .99f, .94f, 0f));
            UIKit.StretchTo((RectTransform)_flash.transform, 0, 0, 0, 0);
            _flash.raycastTarget = false;

            Telemetry.Track("rank_ceremony", "rank", Rung.Id, "ordinal", Rung.Ordinal);

            Play();
        }

        /// <summary>
        /// The rung's whole colour scheme, built around its metal.
        ///
        /// <para>
        /// <b><see cref="Chroma.Of"/> answers a rarity and this is answering a position</b>, so
        /// the struct is right and only the way in differs — <c>WardRevealOverlay.SchemeFor</c>
        /// makes the same move around a seat's colour and says why. The deep is the metal taken
        /// almost to black, which is what keeps a copper room copper in its corners instead of
        /// grey.
        /// </para>
        /// <para>
        /// <b>The partner and the accent sit either side of the metal rather than a fifth of a
        /// turn from it, and the contact sheet is what decided that.</b> The turret reveal is
        /// built around one of the line's four <em>primaries</em>, where a wide shift lands on
        /// another primary and reads as a second light. A metal is not a primary: gold's hue is
        /// .105, so a fifth of a turn from it is .295, which is <em>green</em> — and the sheet
        /// drew Goldbrand's arrival in a green room with a gold badge sitting in it, looking
        /// like art that had been filed under the wrong colour (the fault
        /// <c>make_charm_gems.py --check</c> exists to catch on a gem). Narrow and symmetric,
        /// every metal gets a room in its own family, the two fans still cross in two hues, and
        /// the badge stays the subject of its own ceremony. Measured by eye on
        /// <c>render_rank_ceremony.py --contact</c>, which is the only thing that can answer it.
        /// </para>
        /// </summary>
        static Chroma SchemeFor(Color metal)
        {
            Color.RGBToHSV(metal, out float h, out float s, out float v);

            var partner = Color.HSVToRGB(Mathf.Repeat(h - .07f, 1f), Mathf.Min(1f, s * .92f), v);
            var accent = Color.HSVToRGB(Mathf.Repeat(h + .06f, 1f), Mathf.Min(1f, s * .70f),
                                        Mathf.Min(1f, v * 1.14f));
            var deep = Color.HSVToRGB(h, Mathf.Min(1f, s * .90f), v * .14f);

            return new Chroma(metal, partner, accent, deep);
        }

        /// <summary>The room: a graded sky, three drifting masses of light, and a vignette.</summary>
        void BuildRoom()
        {
            // The bottom layer, covering the screen, so a tap anywhere that is not a control
            // lands here and skips. It is also what `Scrim` would normally be — there is no
            // panel here to dim behind, so the room is the scrim.
            //
            // Shared with the two turret ceremonies (`CeremonySky`), which is why it is not the
            // rung's own deep hue any more: all three stood their subject on a near-black room
            // and all three came back as *so dark*.
            _sky = CeremonySky.Ground(Content, Skip, silent: true);

            _aurora = new Image[AuroraHome.Length];

            for (int i = 0; i < _aurora.Length; i++)
            {
                _aurora[i] = UIKit.Img("Aurora" + i, Content, Art.Glow(256, 1.7f),
                                       Pal.A(_c.Nth(i + 1), 0f), Vector2.one * AuroraSize[i],
                                       new Vector2(.5f, .5f), AuroraHome[i]);
                _aurora[i].raycastTarget = false;
                Drift(i);
            }

            // Last, so it holds the aurora and the fireflies in too. Tinted with the room
            // rather than with ink, or the corners end up the one grey thing on a coloured
            // screen — see `CeremonySky.VignetteInk`, which is where that now lives.
            _vignette = CeremonySky.Veil(Content);
        }

        /// <summary>
        /// The shaft, and the light that falls through it.
        ///
        /// <para>
        /// <b>The streaks fall and that is what makes the screen rise.</b> Nothing in this
        /// composition actually moves upward by more than <see cref="Climb"/> — a badge that
        /// travelled far enough up to read as climbing would leave the screen — so the ascent is
        /// carried by the ground going the other way, which is how every lift in every film is
        /// shot. It is also why the streaks start before the badge exists and never stop: motion
        /// that begins at the moment of the reveal reads as an effect fired at the badge, and
        /// motion that was already there reads as a place the badge arrived in.
        /// </para>
        /// <para>
        /// Each streak owns its own looping tween, so the loop is killed with the streak and a
        /// skip cannot strand one mid-fall. They are children of <see cref="_shaftRt"/> and the
        /// shaft is scaled open on the first beat, so one tween opens all of it.
        /// </para>
        /// </summary>
        void BuildShaft()
        {
            _shaftGlow = UIKit.Img("ShaftGlow", Content, Art.Glow(128, 1.5f), Pal.A(_c.Partner, 0f),
                                   new Vector2(ShaftGlowWidth, 2100f), new Vector2(.5f, .5f),
                                   new Vector2(0f, BadgeY * .35f));
            _shaftGlow.raycastTarget = false;

            _shaft = UIKit.Img("Shaft", Content,
                               Art.Gradient(Pal.A(_metal, 0f), Pal.A(_metal, .9f), Pal.A(_metal, 0f)),
                               new Color(1f, 1f, 1f, 0f),
                               new Vector2(ShaftWidth, 2100f), new Vector2(.5f, .5f),
                               new Vector2(0f, BadgeY * .35f));
            _shaftRt = (RectTransform)_shaft.transform;
            _shaftRt.localScale = new Vector3(.12f, 1f, 1f);
            _shaft.raycastTarget = false;

            for (int i = 0; i < Streaks; i++) BuildStreak(i);
        }

        void BuildStreak(int index)
        {
            float thickness = Random.Range(4f, 8f);
            float length = Random.Range(120f, 420f);
            float x = Random.Range(-ShaftWidth * .42f, ShaftWidth * .42f);
            float period = Random.Range(1.1f, 2.6f);

            var streak = UIKit.Img("Streak" + index, _shaftRt, Art.SoftCapsule(8, 96),
                                   Pal.A(Pal.Lift(_metal, .5f), 0f),
                                   new Vector2(thickness, length), new Vector2(.5f, .5f),
                                   new Vector2(x, 0f));
            streak.raycastTarget = false;

            var rt = (RectTransform)streak.transform;
            float offset = Random.value;

            Tween.Run(period, Ease.Linear, t =>
            {
                if (!rt) return;

                float k = Mathf.Repeat(t + offset, 1f);
                rt.anchoredPosition = new Vector2(x, Mathf.Lerp(1180f, -1180f, k));

                // Brightest through the middle of the run, so a streak is never seen to appear
                // or to stop — the wrap is the one frame a looping thing can give itself away on.
                streak.color = Pal.A(Pal.Lift(_metal, .5f), Mathf.Sin(k * Mathf.PI) * .55f);
            }, streak, "fall").Loop(-1, false);
        }

        // Where the three masses of light sit. A composition rather than a scatter — one high
        // and left, one across the middle, one low — so the frame is lit unevenly, the way a
        // place is. `CompanionRevealOverlay`'s table, and its reasoning.
        static readonly Vector2[] AuroraHome =
            { new Vector2(-380f, 660f), new Vector2(420f, 140f), new Vector2(-260f, -640f) };
        static readonly float[] AuroraSize = { 1180f, 1000f, 1260f };
        static readonly float[] AuroraAlpha = { .20f, .16f, .13f };

        /// <summary>
        /// One blob's endless wander. Both axes are whole multiples of the loop, or the drift
        /// snaps back every time the tween wraps — which on something this large is the most
        /// visible thing on screen.
        /// </summary>
        void Drift(int index)
        {
            var blob = _aurora[index];
            var home = AuroraHome[index];
            float span = 44f + index * 13f;
            float period = 15f + index * 4f;

            Tween.Run(period, Ease.Linear, t =>
            {
                if (!blob) return;

                float a = t * Mathf.PI * 2f;
                ((RectTransform)blob.transform).anchoredPosition =
                    home + new Vector2(Mathf.Sin(a) * span, Mathf.Cos(a * 2f) * span * .6f);
            }, blob, "drift").Loop(-1, false);
        }

        /// <summary>
        /// The badge and everything that moves with it, under one node so the ascent is one
        /// tween rather than six that can drift apart.
        /// </summary>
        void BuildStage()
        {
            _stage = UIKit.Box("Stage", Content, Vector2.zero, new Vector2(.5f, .5f), Vector2.zero);

            // What `ModalView.Close` scales out. An overlay that skips `MakePanel` has to name
            // its own Panel: leaving it null throws out of the click handler *after* the content
            // has faded and *before* `Flow.Dismiss` runs, stranding an invisible full-screen
            // blocker with `_closing` already latched. Two screens in this project have walked
            // into that, and this one would strand the run's own panel with it.
            Panel = _stage;

            _fanA = UIKit.Img("Fan", _stage, Art.Rays(512, 14), Pal.A(_metal, 0f),
                              Vector2.one * FanSize, new Vector2(.5f, .5f), new Vector2(0f, BadgeY));
            _fanRtA = (RectTransform)_fanA.transform;
            _fanRtA.localScale = Vector3.zero;
            _fanA.raycastTarget = false;

            _fanB = UIKit.Img("Fan2", _stage, Art.Rays(256, 7), Pal.A(_c.Partner, 0f),
                              Vector2.one * (FanSize * .72f), new Vector2(.5f, .5f),
                              new Vector2(0f, BadgeY));
            _fanRtB = (RectTransform)_fanB.transform;
            _fanRtB.localScale = Vector3.zero;
            _fanB.raycastTarget = false;

            _halo = UIKit.Img("Halo", _stage, Art.Glow(256, 1.8f), Pal.A(_metal, 0f),
                              Vector2.one * HaloSize, new Vector2(.5f, .5f), new Vector2(0f, BadgeY));
            _halo.raycastTarget = false;

            // The gathering point. It is the whole of the first movement — the motes fly into
            // it, it draws breath, and the strike is it bursting — so it exists from the first
            // frame and the badge is what replaces it.
            _core = UIKit.Img("Core", _stage, Art.Glow(128, 2.4f), Pal.A(Pal.Lift(_metal, .55f), 0f),
                              Vector2.one * 260f, new Vector2(.5f, .5f), new Vector2(0f, BadgeY));
            _core.raycastTarget = false;

            // The rung below, rising into the light that becomes the rung above. Absent at the
            // bottom of the ladder, and absent for a rung whose badge this build does not carry
            // — `RankArt.Paint` switches the node off rather than handing an `Image` a null
            // sprite, which is a white rectangle at the graphic's own colour (invariant 7b).
            _ghost = UIKit.Img("Ghost", _stage, null, RankLook.Ghost,
                               Vector2.one * (BadgeSize * .42f), new Vector2(.5f, .5f),
                               new Vector2(0f, -760f));
            _ghost.preserveAspect = true;
            _ghost.raycastTarget = false;
            _ghostRt = (RectTransform)_ghost.transform;

            if (From == null || !RankArt.Paint(_ghost, From.Id)) _ghost.enabled = false;
            else _ghost.color = Pal.A(Color.white, 0f);

            _badge = UIKit.Img("Badge", _stage, null, Color.white,
                               Vector2.one * BadgeSize, new Vector2(.5f, .5f),
                               new Vector2(0f, BadgeY));
            _badge.preserveAspect = true;
            _badge.raycastTarget = false;
            _badgeRt = (RectTransform)_badge.transform;
            _badgeRt.localScale = Vector3.zero;

            // False is an ordinary answer — see the class remarks — and leaves the strike
            // landing on light alone, which is still a strike.
            RankArt.Paint(_badge, Rung.Id);
        }

        // ---------------------------------------------------------------- the captions
        void BuildCaptions()
        {
            // **Cream with the dark outline every caption here carries, and not the rung's
            // metal.** Over a near-black room the metal lifted toward white was the obvious
            // choice; over this one it is gold on peach. Driving it dark instead was worse
            // still — the outline is dark too, so at 44pt the letterforms filled in and the
            // line became a smudge. The name below it is cream on this same ground and reads at
            // every rung, which is the answer; and this line says RANK EARNED rather than
            // naming the rank, so it is the one caption here with no colour to carry.
            _eyebrow = UIKit.Shrinkable(
                UIKit.Titled("Eyebrow", Content, Loc.Get("ui.rankup.title"), 44,
                             Pal.A(Pal.Cream, .95f),
                             TextAnchor.MiddleCenter,
                             new Vector2(900f, EyebrowH), new Vector2(.5f, .5f),
                             new Vector2(0f, EyebrowY), outline: 3f, shadow: 3f), 26);
            SetAlpha(_eyebrow, 0f);

            // The name is the one thing on this screen a player repeats afterwards, so it is the
            // largest type in the game and is shrunk rather than allowed to spill — a rank name
            // is content and a translation is routinely half again as long (invariant 19n).
            _name = UIKit.Shrinkable(
                UIKit.Titled("Name", Content, NameOf(Rung), 100, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(940f, NameH),
                             new Vector2(.5f, .5f), new Vector2(0f, NameY),
                             outline: 5f, shadow: 6f), 52);
            SetAlpha(_name, 0f);
            _name.transform.localScale = Vector3.zero;

            _rule = UIKit.Img("Rule", Content, Art.SoftCapsule(10, 120), Pal.A(_metal, 0f),
                              new Vector2(0f, RuleH), new Vector2(.5f, .5f), new Vector2(0f, RuleY));
            _rule.raycastTarget = false;

            _blurb = UIKit.Shrinkable(
                UIKit.Titled("Blurb", Content, BlurbOf(Rung), 38, Pal.A(Pal.Cream, .88f),
                             TextAnchor.UpperCenter, new Vector2(880f, BlurbH),
                             new Vector2(.5f, .5f), new Vector2(0f, BlurbY),
                             outline: 3f, shadow: 3f, wrap: true), 24);
            SetAlpha(_blurb, 0f);
        }

        /// <summary>
        /// The rung's name, or nothing this build can say.
        ///
        /// <see cref="Loc.Get(string)"/> answers the key itself for a string it has never heard
        /// of, which on a screen this size would be <c>rank.foo.name</c> in ninety-point type.
        /// A rung is content and the ladder is published separately from the strings
        /// (<c>seed-config.mjs</c>), so a client meeting a rung whose copy it has not got is
        /// reachable rather than hypothetical — and the honest drawing of it is the badge with
        /// no name under it.
        /// </summary>
        static string NameOf(RankDefinition rung)
            => rung != null && Loc.Has(rung.NameKey) ? rung.Name : string.Empty;

        static string BlurbOf(RankDefinition rung)
            => rung != null && Loc.Has(rung.BlurbKey) ? Loc.Get(rung.BlurbKey) : string.Empty;

        // -------------------------------------------------------------------- the rail
        /// <summary>
        /// The ladder itself, at the foot: one pip per rung, lighting from the bottom to the one
        /// reached.
        ///
        /// <para>
        /// <b>This is the half that makes it a rank rather than a prize.</b> A badge on its own
        /// says "you got a thing"; the same badge over a rail with three of seven lamps lit says
        /// where you are and that there is further to go, which is the only sentence a rank has
        /// to say. It is drawn from <c>RankLadder.Count</c>, so a ladder that grows or shrinks
        /// draws correctly with nothing to keep in step.
        /// </para>
        /// <para>
        /// The pips close up rather than overlapping on a long ladder: <c>MaxRungs</c> is 24 and
        /// twenty-four at <see cref="PipMax"/> across <see cref="RailWidth"/> would touch. A
        /// ladder of one draws a single pip in the middle, which is degenerate and correct.
        /// </para>
        /// </summary>
        void BuildRail()
        {
            var ladder = RankLedger.Ladder;
            int count = Mathf.Max(1, ladder.Count);

            _railRt = UIKit.Box("Rail", Content, new Vector2(RailWidth, PipMax),
                                new Vector2(.5f, .5f), new Vector2(0f, RailY));

            // Dark rather than white-at-a-low-alpha: the rail crosses the brightest part of
            // the room, where a cream trough is nothing at all. See `CeremonySky.Ink`.
            var trough = UIKit.Img("Trough", _railRt, Art.SoftCapsule((int)RailTrough, 96),
                                   Pal.A(CeremonySky.Ink, .30f),
                                   new Vector2(RailWidth, RailTrough), new Vector2(.5f, .5f),
                                   Vector2.zero);
            trough.raycastTarget = false;

            _railFill = UIKit.Img("Fill", _railRt, Art.SoftCapsule((int)RailTrough, 96), Pal.A(_metal, .9f),
                                  new Vector2(0f, RailTrough), new Vector2(0f, .5f), Vector2.zero);
            _railFill.raycastTarget = false;

            // <b>The one pivot on this screen that is not centre, and it has to be set by hand.</b>
            // `UIKit.Box` always pivots at centre whatever it is anchored to, so a bar widened by
            // its `sizeDelta` grows in <em>both</em> directions — the light would run left out of
            // the rail as far as it ran right along it. Left-pivoted, widening is travel.
            _railFill.rectTransform.pivot = new Vector2(0f, .5f);
            _railFill.rectTransform.anchoredPosition = Vector2.zero;

            float step = count > 1 ? RailWidth / (count - 1) : 0f;
            float pip = count > 1 ? Mathf.Min(PipMax, step * .72f) : PipMax;

            _pips = new Image[count];
            _pipHeld = Mathf.Clamp(Rung.Ordinal, 1, count);

            for (int i = 0; i < count; i++)
            {
                float x = count > 1 ? -RailWidth * .5f + step * i : 0f;
                int ordinal = i + 1;

                // <b>Every pip is built dark, including the ones the player already held.</b>
                // The rail stands there unlit through the whole gathering, which is where its
                // anticipation comes from — and the climb then lights it from the bottom, so
                // what a player watches is this rung being stood on top of every rung below it.
                // Built pre-lit, the ascent would have had nothing left to say and the rail
                // would have been a static readout with a bar sliding along it.
                //
                // There can never be a gap in the run: a rung is only held on top of an unbroken
                // run from the bottom (`RankLadder.Held`), so lighting them in order is drawing
                // the only state the ledger can produce.
                _pips[i] = UIKit.Img("Pip" + ordinal, _railRt, Art.Disc(64),
                                     Pal.A(CeremonySky.Ink, .34f),
                                     Vector2.one * pip, new Vector2(.5f, .5f), new Vector2(x, 0f));
                _pips[i].raycastTarget = false;
            }

            _railText = UIKit.Shrinkable(
                UIKit.Titled("RailText", Content,
                             Loc.Format("ui.ranks.held_count", _pipHeld, count), 34,
                             Pal.A(Pal.Cream, .78f), TextAnchor.MiddleCenter,
                             new Vector2(880f, RailTextH), new Vector2(.5f, .5f),
                             new Vector2(0f, RailTextY), outline: 3f, shadow: 3f), 22);
            SetAlpha(_railText, 0f);
        }

        void BuildButtons()
        {
            // Green, not gold. `Skins` names are roles (invariant 44) and green has meant "do
            // the thing" since this interface was written; gold is what a purchase wears, and
            // nothing here costs anything.
            _act = UIKit.Button("Act", Content, Art.S("Ui/" + Skins.Settled),
                                new Vector2(620f, ActH), new Vector2(.5f, .5f),
                                new Vector2(0f, ActY), Act);

            _actRt = (RectTransform)_act.transform;
            _actRt.localScale = Vector3.zero;

            UIKit.Shrinkable(
                UIKit.Titled("Label", _act.transform, Loc.Get("ui.rankup.onward"), 46, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(460f, 66f), new Vector2(.5f, .5f),
                             new Vector2(0f, ActH * UIKit.PillFaceLift), 0f, 3f), 26);

            // Appears with the key rather than at the start. Before the payoff there is nothing
            // to dismiss and a cross would only invite skipping past it; tapping the room
            // already skips, which is the affordance that matters early.
            var cross = UIKit.IconButton("Dismiss", Content, Skins.Nav, "ic_close",
                                         new Vector2(84f, 84f), new Vector2(1f, 1f),
                                         new Vector2(-72f, -96f), Act);

            _dismiss = UIKit.Group((RectTransform)cross.transform);
            _dismiss.alpha = 0f;
            _dismiss.blocksRaycasts = false;
        }

        // ---------------------------------------------------------------------- playing
        /// <summary>
        /// The sequence, in the order it is seen. Gaps, never absolute times — see
        /// <see cref="Cue"/>, which exists because this project has already shipped a
        /// celebration whose beats collided at one star count and not at another.
        ///
        /// <para>
        /// Four movements. The shaft opens and the rung below rises into it; the lines that were
        /// met arrive as motes and are gathered; the gathering draws breath and breaks; and the
        /// climb settles — the rail lights, the name strikes, and the way out appears. The
        /// breath before the break is the whole trick: take it out and a ceremony becomes an
        /// announcement.
        /// </para>
        /// <para>
        /// <b>One sound, at the owner's instruction, and it is placed rather than triggered.</b>
        /// This shipped with seven - a rise, a tick per mote, a tick on the breath, a bang and a
        /// bell on the break, the badge landing and the name - and played back as a pile-up
        /// rather than as a fanfare, which is the note <c>WardRevealOverlay</c> already carries
        /// about the companion reveal ringing on all six of its beats. It is now <c>rankup</c>
        /// and nothing else: every other beat here is silent, and the panel this hands on to is
        /// closed quietly, so the ceremony's one sound is not followed by a dismissal whoosh.
        /// </para>
        /// <para>
        /// <b>Placed by its rise, which is why it is scheduled ahead of the beat it belongs
        /// to.</b> A sound fired <em>on</em> the strike would be a rising arpeggio climbing
        /// after the thing it is meant to announce; fired at the top of the ceremony it would
        /// arrive at whatever moment a rung's line count happened to put there, because the
        /// gathering is as long as the rung has requirements. So the strike's own time is read
        /// off the playhead and the clip is started <see cref="FanfareRise"/> before it.
        /// </para>
        /// </summary>
        void Play()
        {
            var cue = new Cue(this);
            int lines = Mathf.Max(1, Rung.Requirements.Count);

            // -- the shaft opens ---------------------------------------------
            cue.With(() =>
            {
                Tween.Fade(_sky, 1f, .28f);
                Tween.Fade(_vignette, VignetteAlpha, .42f);

                for (int i = 0; i < _aurora.Length; i++)
                    Tween.Fade(_aurora[i], AuroraAlpha[i], .70f);

                Tween.Scale(_shaftRt, new Vector3(1f, 1f, 1f), .62f, Ease.OutQuint);
                Tween.Fade(_shaft, ShaftAlpha, .55f);
                Tween.Fade(_shaftGlow, ShaftGlowAlpha, .70f);
            });

            // -- the rung below rises ----------------------------------------
            cue.Then(.16f, RaiseGhost);

            // -- the lines that were met arrive ------------------------------
            cue.Then(.22f, null).Repeat(lines, MoteGap, i => LaunchMote(i, lines));

            // -- the breath --------------------------------------------------
            cue.Then(MoteFlight + .06f, () =>
            {
                Tween.Run(.24f, Ease.InCubic, t =>
                {
                    if (!_core) return;
                    _core.transform.localScale = Vector3.one * Mathf.Lerp(1.35f, .18f, t);
                    _core.color = Pal.A(Pal.Lift(_metal, .55f), Mathf.Lerp(.95f, 1f, t));
                }, _core, "core");
            });

            // -- the break ---------------------------------------------------
            // The one sound, started early enough that its rise tops out on the frame the badge
            // lands. The strike's time is taken off the playhead rather than re-derived, because
            // the gathering's length depends on how many lines the rung asks for - which is the
            // very drift `Cue` exists to make unrepresentable.
            float strikeAt = cue.Playhead + StrikeGap;
            Tween.After(Mathf.Max(0f, strikeAt - FanfareRise), Fanfare, this);

            cue.Then(StrikeGap, Strike);

            // -- the climb ---------------------------------------------------
            cue.Then(.40f, Ascend);

            // -- the name ----------------------------------------------------
            cue.Then(.34f, Name);

            // -- what it means -----------------------------------------------
            cue.Then(.26f, Tell);

            // -- the way out -------------------------------------------------
            cue.Then(.30f, Settle);
        }

        /// <summary>
        /// The rung below, rising up the shaft and spent on the way. Nothing at all at the
        /// bottom of the ladder, which is the correct drawing of a first rank.
        /// </summary>
        void RaiseGhost()
        {
            if (_ghost == null || !_ghost.enabled) return;

            float travel = MoteGap * Mathf.Max(1, Rung.Requirements.Count) + MoteFlight + .2f;

            Tween.Run(travel, Ease.InOutSine, t =>
            {
                if (!_ghostRt) return;

                _ghostRt.anchoredPosition = new Vector2(0f, Mathf.Lerp(-760f, BadgeY, t));

                // Brightest halfway up and gone by the top: it is not arriving, it is being
                // spent. A ghost that faded in and stayed would read as the wrong badge.
                float lit = Mathf.Sin(t * Mathf.PI);
                _ghost.color = Pal.A(Color.white, lit * RankLook.Ghost.a);
                _ghostRt.localScale = Vector3.one * Mathf.Lerp(1f, .35f, t * t);
            }, _ghost, "rise");
        }

        /// <summary>
        /// One line of the rung, arriving as light.
        ///
        /// <para>
        /// It spirals in rather than flying straight, because a straight line from the rim to
        /// the middle reads as a projectile and the thing being drawn is a gathering. The
        /// starting angle is spread evenly so a rung of eight lines arrives as a ring closing
        /// rather than as a stream from one side, and the size runs down with the count so a
        /// rung asking one thing is a comet and a rung asking eight is a swarm — the loudness of
        /// the gathering then says something true about the rung rather than being a constant.
        /// </para>
        /// </summary>
        void LaunchMote(int index, int count)
        {
            float size = Mathf.Lerp(120f, 52f, Mathf.Clamp01((count - 1) / 7f));
            var colour = _c.Nth(index);

            var mote = UIKit.Img("Mote" + index, _stage, Art.Spark(96), Pal.A(Pal.Lift(colour, .4f), 0f),
                                 Vector2.one * size, new Vector2(.5f, .5f), new Vector2(0f, BadgeY));
            mote.raycastTarget = false;
            _motes.Add(mote);

            var rt = (RectTransform)mote.transform;

            // Spread round the whole circle, with the first line arriving from above so a
            // one-line rung is not a comet coming in from an arbitrary corner.
            float from = Mathf.PI * .5f + index * Mathf.PI * 2f / count;
            float radius = 1250f;
            float turns = 1.15f;
            float spin = Random.Range(-220f, 220f);

            Tween.Run(MoteFlight, Ease.InCubic, t =>
            {
                if (!rt) return;

                float angle = from + turns * Mathf.PI * 2f * t;
                float r = Mathf.Lerp(radius, 0f, t);

                rt.anchoredPosition = new Vector2(Mathf.Cos(angle) * r,
                                                  BadgeY + Mathf.Sin(angle) * r * .78f);
                rt.localScale = Vector3.one * Mathf.Lerp(.65f, 1.25f, t);
                rt.localRotation = Quaternion.Euler(0f, 0f, spin * t);
                mote.color = Pal.A(Pal.Lift(colour, .4f), Mathf.Clamp01(t * 3.2f));
            }, mote).OnDone(() => Land(mote, index, count));
        }

        /// <summary>A line landing: the core takes it, and says so.</summary>
        void Land(Image mote, int index, int count)
        {
            if (mote) Destroy(mote.gameObject);
            _motes.Remove(mote);

            if (_core == null || _struck) return;

            // Each arrival leaves the core a little larger and a little brighter than the last,
            // so the gathering visibly accumulates rather than pulsing in place.
            float share = (index + 1) / (float)count;

            _core.color = Pal.A(Pal.Lift(_metal, .55f), Mathf.Lerp(.35f, .95f, share));

            // <b>Grown on the core's own channel rather than punched, and that is not a style
            // choice.</b> `Tween.Punch` borrows the scale on a channel of its own and hands it
            // back at the end — so the last line's punch is still running when the breath begins
            // a fifth of a second later, the two write `localScale` from different channels, and
            // the punch's restore then puts back the scale the breath had spent its whole
            // duration taking away. The core would simply never contract, which is the beat the
            // whole strike is paid for out of. Same channel, so the breath supersedes this
            // cleanly (`Tween.Add` kills a channel before starting on it).
            float to = Mathf.Lerp(.55f, 1.35f, share);
            float from = _core.transform.localScale.x;

            Tween.Run(.22f, Ease.OutBack, t =>
            {
                if (!_core) return;
                _core.transform.localScale = Vector3.one * Mathf.LerpUnclamped(from, to, t);
            }, _core, "core");

            if (_halo) Tween.Fade(_halo, HaloAlpha * share * .7f, .24f);

            Burst.Sparks(_stage, new Vector2(0f, BadgeY), Pal.Lift(_c.Nth(index), .3f),
                         5, 130f, 16f, .38f);
        }

        /// <summary>
        /// The break: the gathering becomes the badge.
        ///
        /// <para>
        /// <b>It lands rather than blooms.</b> The badge falls the last of its travel on
        /// <see cref="Ease.InCubic"/> from three times its size and is punched at the bottom —
        /// which is a forging, where an <c>OutBack</c> bloom would be a flower opening. The
        /// difference is the one thing a player feels about a rank.
        /// </para>
        /// </summary>
        void Strike()
        {
            _struck = true;

            if (_flash)
            {
                _flash.color = new Color(1f, .99f, .94f, .94f);
                Tween.Fade(_flash, 0f, .42f, Ease.OutQuad);
            }

            Tween.Shake((RectTransform)Content, 30f, .46f);

            if (_core)
            {
                Tween.Run(.34f, Ease.OutQuint, t =>
                {
                    if (!_core) return;
                    _core.transform.localScale = Vector3.one * Mathf.Lerp(.18f, 2.6f, t);
                    _core.color = Pal.A(Pal.Lift(_metal, .55f), .95f * (1f - t));
                }, _core, "core");
            }

            if (_ghost) { _ghost.enabled = false; }

            _fanRtA.localScale = Vector3.one * .34f;
            Tween.Scale(_fanRtA, 1f, .72f, Ease.OutQuint);
            Tween.Fade(_fanA, FanAlpha, .55f);
            Spin(_fanRtA, 68f, 1f);

            _fanRtB.localScale = Vector3.one * .28f;
            Tween.Scale(_fanRtB, 1f, .86f, Ease.OutQuint);
            Tween.Fade(_fanB, Fan2Alpha, .62f);
            Spin(_fanRtB, 49f, -1f);

            Tween.Fade(_halo, HaloAlpha, .5f);

            for (int i = 0; i < 4; i++) Shockwave(i * .075f, _c.Nth(i));

            Burst.Sparks(_stage, new Vector2(0f, BadgeY), Pal.Lift(_metal, .35f), 22, 340f, 34f, .78f);
            Burst.Confetti(Content, 54);

            // The landing. Scale only — the badge is already where it belongs, so a drop would
            // have to be undone and the ascent below starts from its resting place.
            _badgeRt.localScale = Vector3.one * 3f;
            Tween.Scale(_badgeRt, 1f, .17f, Ease.InCubic)
                 .OnDone(() => { if (_badgeRt) Tween.Punch(_badgeRt, .19f, .38f); });

            SetAlpha(_eyebrow, 0f);
            Tween.Fade(_eyebrow, .95f, .34f);
        }

        /// <summary>
        /// The climb: the composition drifts up, and the rail lights to the rung reached.
        ///
        /// <para>
        /// <b>The rail is lit by a light running along it rather than by the pip simply coming
        /// on</b>, because a pip that changes colour is a state and a light that travels is a
        /// journey — and the pips below the new one light as it passes them, which is the whole
        /// ladder saying that this rung stands on those.
        /// </para>
        /// </summary>
        void Ascend()
        {
            if (_stage)
            {
                var rt = _stage;
                Tween.Run(1.0f, Ease.OutQuint, t =>
                {
                    if (!rt) return;
                    rt.anchoredPosition = new Vector2(0f, Climb * t);
                }, rt, "climb");
            }

            int count = _pips != null ? _pips.Length : 0;
            if (count == 0) return;

            float target = count > 1 ? (_pipHeld - 1) / (float)(count - 1) : 1f;
            int lit = 0;

            Tween.Run(.72f, Ease.OutCubic, t =>
            {
                if (!_railFill) return;

                float reach = target * t;
                _railFill.rectTransform.sizeDelta = new Vector2(RailWidth * reach, RailTrough);

                // Each pip the light passes comes on once, in its own metal.
                while (lit < count)
                {
                    float at = count > 1 ? lit / (float)(count - 1) : 0f;
                    if (at > reach + 1e-4f) break;

                    LightPip(lit);
                    lit++;
                }
            }, _railFill, "rail").OnDone(() =>
            {
                // The rung reached, last and loudest. Done here rather than in the walk so it
                // cannot be lit by a rounding error a frame early.
                if (_pips == null) return;

                int index = _pipHeld - 1;
                if (index < 0 || index >= _pips.Length || !_pips[index]) return;

                // Larger than the rest and staying that way, because "which one is me" has to be
                // answerable from the rail alone a second after the light has stopped moving. A
                // `Pop` would have landed it back at the size of every other pip.
                _pips[index].color = Pal.A(Pal.Lift(_metal, .35f), 1f);
                _pips[index].transform.localScale = Vector3.one * .4f;
                Tween.Scale(_pips[index].transform, PipHeldScale, .44f, Ease.OutBack);
                Burst.Sparks(_railRt, _pips[index].rectTransform.anchoredPosition,
                             Pal.Lift(_metal, .3f), 9, 150f, 16f, .48f);
            });
        }

        void LightPip(int index)
        {
            if (_pips == null || index < 0 || index >= _pips.Length || !_pips[index]) return;
            if (index == _pipHeld - 1) return;          // the rung reached is lit by Ascend

            _pips[index].color = Pal.A(RankLook.Metal(index + 1), .92f);
            Tween.Punch(_pips[index].transform, .32f, .26f);
        }

        /// <summary>The name striking in. Nothing at all when this build cannot say it.</summary>
        void Name()
        {
            if (_name == null || _name.text.Length == 0) return;

            SetAlpha(_name, 1f);
            _name.transform.localScale = Vector3.one * 2.2f;
            Tween.Scale(_name.transform, 1f, .26f, Ease.InCubic)
                 .OnDone(() => Tween.Punch(_name.transform, .16f, .34f));
        }

        /// <summary>The rule, the blurb and where this stands on the ladder.</summary>
        void Tell()
        {
            if (_rule)
            {
                Tween.Fade(_rule, .85f, .26f);
                Tween.Run(.34f, Ease.OutCubic, t =>
                {
                    if (!_rule) return;
                    var rt = _rule.rectTransform;
                    rt.sizeDelta = new Vector2(Mathf.Lerp(0f, 420f, t), rt.sizeDelta.y);
                }, _rule);
            }

            if (_blurb && _blurb.text.Length > 0) Tween.Fade(_blurb, .88f, .34f);
            if (_railText) Tween.Fade(_railText, .78f, .34f);
        }

        /// <summary>The way out. Nothing before this point can be dismissed by mistake.</summary>
        void Settle()
        {
            Tween.Pop(_actRt, .6f, .46f);

            if (_dismiss)
            {
                Tween.Fade(_dismiss, 1f, .3f);
                _dismiss.blocksRaycasts = true;
            }

            _settled = true;
        }

        // ------------------------------------------------------------------- the parts
        /// <summary>
        /// The ceremony's one sound, and the only one it makes.
        ///
        /// <para>
        /// Latched, because <see cref="Skip"/> has to be able to ring it. Pending beats are
        /// killed by owner, so a player who taps through before this has started would otherwise
        /// take their rank in silence - and a skip means <em>get to it</em>, not <em>I do not
        /// want this</em>.
        /// </para>
        /// </summary>
        void Fanfare()
        {
            if (_rang) return;
            _rang = true;

            Audio.Sfx("rankup", .7f);
        }

        /// <summary>A ring leaving the strike, in one of the room's three lights.</summary>
        void Shockwave(float delay, Color colour)
        {
            var ring = UIKit.Img("Wave", _stage, Art.Ring(256, 10f), Pal.A(colour, 0f),
                                 Vector2.one * 440f, new Vector2(.5f, .5f), new Vector2(0f, BadgeY));
            ring.raycastTarget = false;

            Tween.Run(.78f, Ease.OutQuint, t =>
            {
                if (!ring) return;
                ring.transform.localScale = Vector3.one * Mathf.Lerp(.2f, 3.6f, t);
                ring.color = Pal.A(colour, .8f * (1f - t));
            }, ring).Delay(delay).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        void Spin(RectTransform rt, float period, float direction)
        {
            Tween.Run(period, Ease.Linear, t =>
            {
                if (rt) rt.localRotation = Quaternion.Euler(0f, 0f, t * 360f * direction);
            }, rt, "spin").Loop(-1, false);
        }

        // -------------------------------------------------------------------- the skip
        /// <summary>
        /// Ends the sequence now, in the state it was heading for.
        ///
        /// <para>
        /// One pass of assignments rather than a second choreography, which is only possible
        /// because every element already exists — the beats reveal things rather than build
        /// them. That agreement is exactly what a skip path normally gets wrong. Pending beats
        /// are killed by owner, which <see cref="Cue"/> makes possible by scheduling every one
        /// of them against this component.
        /// </para>
        /// <para>
        /// <b>The two endless loops are restarted rather than left dead.</b> A skip landing
        /// before the break kills the beat that would have started the fans, and a fan frozen
        /// mid-turn is the one part of this that reads as a bug rather than as a fast-forward.
        /// The streaks and the drift are owned by their own objects and were never this view's
        /// to kill.
        /// </para>
        /// </summary>
        void Skip()
        {
            if (_settled) return;

            _skipped = true;
            _struck = true;

            // Killed by owner, which reaches every beat `Cue` scheduled against this view.
            Tween.KillAll(this);

            // Including the fanfare, if it had not started - so it is rung here instead. A rank
            // taken in silence because somebody was in a hurry is the one thing a skip may not
            // cost.
            Fanfare();

            // And by channel for the two that are owned by their targets and are *not* heading
            // where the skip puts them: the breath ends on a bright core that the strike has to
            // put out, and the rising ghost ends on a position the badge occupies. Everything
            // else in flight converges on the value assigned below, so killing it would buy a
            // snap in place of the last tenth of a fade.
            if (_core) Tween.KillChannel(_core, "core");
            if (_ghost) Tween.KillChannel(_ghost, "rise");

            // And the rail's, whose *ending* is the thing that has to be stopped rather than its
            // value: it converges on the width assigned below, but its `OnDone` then re-plays the
            // held pip's arrival from a fifth of its size — a pip snapping small and growing back
            // after the skip has already settled the page.
            if (_railFill) Tween.KillChannel(_railFill, "rail");

            if (_sky) _sky.color = Color.white;
            if (_vignette) SetAlpha(_vignette, VignetteAlpha);
            if (_flash) SetAlpha(_flash, 0f);

            for (int i = 0; _aurora != null && i < _aurora.Length; i++)
                SetAlpha(_aurora[i], AuroraAlpha[i]);

            if (_shaftRt) { _shaftRt.localScale = Vector3.one; SetAlpha(_shaft, ShaftAlpha); }
            if (_shaftGlow) SetAlpha(_shaftGlow, ShaftGlowAlpha);

            foreach (var mote in _motes) if (mote) Destroy(mote.gameObject);
            _motes.Clear();

            if (_ghost) _ghost.enabled = false;
            if (_core) SetAlpha(_core, 0f);

            if (_fanRtA) { _fanRtA.localScale = Vector3.one; SetAlpha(_fanA, FanAlpha); Spin(_fanRtA, 68f, 1f); }
            if (_fanRtB) { _fanRtB.localScale = Vector3.one; SetAlpha(_fanB, Fan2Alpha); Spin(_fanRtB, 49f, -1f); }

            if (_halo) SetAlpha(_halo, HaloAlpha);
            if (_badgeRt) _badgeRt.localScale = Vector3.one;
            if (_stage) _stage.anchoredPosition = new Vector2(0f, Climb);

            SetAlpha(_eyebrow, .95f);

            if (_name && _name.text.Length > 0)
            {
                SetAlpha(_name, 1f);
                _name.transform.localScale = Vector3.one;
            }

            if (_rule)
            {
                SetAlpha(_rule, .85f);
                _rule.rectTransform.sizeDelta = new Vector2(420f, RuleH);
            }

            if (_blurb && _blurb.text.Length > 0) SetAlpha(_blurb, .88f);
            if (_railText) SetAlpha(_railText, .78f);

            if (_railFill)
            {
                int count = _pips != null ? _pips.Length : 0;
                float target = count > 1 ? (_pipHeld - 1) / (float)(count - 1) : 1f;
                _railFill.rectTransform.sizeDelta = new Vector2(RailWidth * target, RailTrough);
            }

            for (int i = 0; _pips != null && i < _pips.Length; i++)
            {
                if (!_pips[i]) continue;

                _pips[i].transform.localScale =
                    Vector3.one * (i + 1 == _pipHeld ? PipHeldScale : 1f);
                _pips[i].color = i + 1 == _pipHeld ? Pal.A(Pal.Lift(_metal, .35f), 1f)
                               : i + 1 < _pipHeld ? Pal.A(RankLook.Metal(i + 1), .92f)
                                                  : Pal.A(CeremonySky.Ink, .34f);
            }

            if (_actRt) _actRt.localScale = Vector3.one;
            if (_dismiss) { _dismiss.alpha = 1f; _dismiss.blocksRaycasts = true; }

            _settled = true;
        }

        static void SetAlpha(Graphic g, float a)
        {
            if (g == null) return;
            var c = g.color; c.a = a; g.color = c;
        }

        // --------------------------------------------------------------------- leaving
        /// <summary>
        /// Takes it away and lets the run's own panel through.
        ///
        /// Quiet, because what follows is the victory or defeat panel's own entrance and a
        /// backing-out whoosh underneath it is one sound too many — <c>ModalView.Close</c>'s
        /// own note about the case it was written for.
        /// </summary>
        void Act()
        {
            Telemetry.Track("rank_ceremony_done", "rank", Rung != null ? Rung.Id : string.Empty,
                            "skipped", _skipped);

            Close(Carry, quiet: true);
        }

        /// <summary>
        /// Hands on, exactly once.
        ///
        /// <para>
        /// <b>Also called from <see cref="OnDestroy"/></b>, which is the half that matters: a
        /// screen change destroys every modal without closing it, and a continuation dropped
        /// there is a player left on a finished board with nothing to press. The latch is
        /// <see cref="RankCeremony"/>'s, so a chain of ceremonies still raises one panel.
        /// </para>
        /// </summary>
        void Carry()
        {
            if (_spent) return;
            _spent = true;

            var then = Then;
            Then = null;

            then?.Invoke();
        }

        void OnDestroy() => Carry();

        /// <summary>
        /// Back finishes the sequence rather than closing, once. A player pressing back halfway
        /// through almost always means "get to it", and closing would throw away the arrival of
        /// something they cannot earn twice; pressing it again then leaves.
        /// </summary>
        public override bool OnBack()
        {
            if (!_settled) { Skip(); return true; }

            Act();
            return true;
        }
    }
}
