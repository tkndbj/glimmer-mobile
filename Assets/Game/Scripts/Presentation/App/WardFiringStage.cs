using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A turret standing at the foot of a box, firing at <em>the arrangement of raiders its own
    /// ability is about</em>, over and over.
    ///
    /// <para>
    /// <b>One widget because it is one job.</b> The preview a player opens from the loadout and the
    /// bench used to judge the nineteen projectiles ask exactly the same question — <em>what does
    /// this turret look like when it shoots?</em> — and two answers to that would be two places
    /// where a bolt is anchored, sized or timed differently, which is the fault this project keeps
    /// recording under other names (two copies of one rule, each correct until one of them is not).
    /// </para>
    /// <para>
    /// <b>The number of things it shoots at is the ability, and that is the whole of what this
    /// widget grew.</b> It stood one target and fired one bolt, so a splash turret, a chain turret
    /// and a pierce turret — three of the ten abilities, six of the twenty models — previewed
    /// <em>identically to the free one</em>: the thing being paid for was the only thing not on
    /// screen. A player deciding between them was reading a sentence and watching a bolt that said
    /// nothing. <see cref="Compose"/> stands the arrangement each ability is about, and
    /// <see cref="Volley"/> fires exactly the bolts <c>SiegeBoard.Ability</c> would have reported
    /// against it.
    /// </para>
    /// <para>
    /// <b>It mirrors the rules rather than inventing a demonstration.</b> The extra hits are the
    /// board's own — a splash takes the neighbours inside <c>Extent</c>, a chain arcs to
    /// <c>Extent</c> more, a lance runs the lane every <c>Extent</c> shots — and they are drawn
    /// smaller than the shot that caused them, which is the one thing <c>SiegeBolt.Extra</c> exists
    /// for. A preview that flattered a turret would be worse than none: what it is for is deciding
    /// whether to spend nine thousand credits.
    /// </para>
    /// <para>
    /// <b>It draws through the shipped arithmetic and invents none of its own.</b> The anchors are
    /// <c>SiegeView.HeadAt</c> and <c>SiegeView.MuzzleAt</c>, the sizes are the multiples of a cell
    /// that <c>SiegeView.Bolt</c> uses and the per-turret scale is <c>SiegeView.BoltScale</c>.
    /// </para>
    /// <para>
    /// <b>Its own asset scope, released when the object goes.</b> A turret's body, its recoil and
    /// its three reels are what a run loads for the four it stands (invariant 7b); a panel that
    /// shared a live board's hold would release its line when it closed.
    /// </para>
    /// </summary>
    public sealed class WardFiringStage : MonoBehaviour
    {
        /// <summary>
        /// The three sizes <c>SiegeView</c> draws the exchange at, and how long a bolt is in the
        /// air. Multiples of a cell, so a stage drawn at any size draws them in proportion.
        /// </summary>
        const float MuzzleWide = 2.7f, BoltWide = 1.0f, HitWide = 3.2f;
        const float Flight = .34f, RecoilFor = .18f;

        /// <summary>
        /// What an <em>extra</em> hit is drawn at, against the shot that caused it.
        ///
        /// <b>Smaller, which is the only thing <c>SiegeBolt.Extra</c> is for</b> — a splinter that
        /// looked like a shot would make a splash turret read as firing three times rather than
        /// once and spreading. The board makes the same distinction with the same number.
        /// </summary>
        const float ExtraHit = 1.9f, ExtraBolt = .74f;

        /// <summary>How long the stage waits between volleys, so one can be watched at a time.</summary>
        const float Between = 1.05f;

        /// <summary>
        /// How long apart the bolts of one volley leave the barrel.
        ///
        /// <b>Staggered rather than fired together</b>, because a chain that arrived all at once is
        /// a splash, and the two abilities differing only in <em>which</em> raiders they reach is
        /// exactly what a preview has to make visible (invariant 26h's test, asked of a drawing).
        /// </summary>
        const float ExtraGap = .13f;

        /// <summary>The turret's own footprint, in cells, and how far it stands off the floor.</summary>
        // **The turret's drawn size is the board's**, read rather than copied: `BarrelGap` is a
        // fraction of this width, so a third number here would put a twin turret's bolts beside
        // its barrels instead of on them.
        const float TurretWide = SiegeView.BodyWide, TurretTall = SiegeView.BodyTall,
                    TurretFoot = .10f;

        /// <summary>
        /// The band the targets stand in, measured down from the box's own top in cells.
        ///
        /// <para>
        /// <b>The top is headroom for the primary impact rather than a margin.</b> A hit is drawn
        /// <see cref="HitWide"/> across and centred on what it hit, so anything nearer the top than
        /// half of that has the loudest frame in the whole exchange cut off — the box is masked, so
        /// it is cut rather than merely overhanging.
        /// </para>
        /// <para>
        /// <b>The foot is derived from the stage's own height rather than typed</b>, because the
        /// two stages that draw this are 6.15 and 5.94 cells tall and a column of three has to fit
        /// in both. A typed row would be legible on one and standing on the turret's head on the
        /// other, which is precisely the class of fault only a picture reports.
        /// </para>
        /// </summary>
        const float BandTop = 1.6f, BandClear = .60f;

        RectTransform _node;
        Image _turret, _shadow;
        float _cell;
        AssetHold _art;
        string _name;

        WardModel _model;
        int _colour;
        int _generation;

        /// <summary>How many volleys this stage has fired. Only a pierce turret reads it.</summary>
        int _shots;

        readonly List<Mark> _marks = new List<Mark>(4);

        Coroutine _firing;

        /// <summary>
        /// One thing being shot at: where it stands, how big it is drawn and which raider it is.
        ///
        /// <b>The colour is the mark's rather than the stage's</b>, because a prism turret is
        /// strong against two colours and the only way to show that is to stand one of each.
        /// </summary>
        sealed class Mark
        {
            public Vector2 At;          // anchored, from the box's top
            public float Size;          // units
            public char Colour;
            public string Reel;
            public Image Img;
            public Image Aura;          // a lasting mark: frost, or a burn
            public Color Rest;
        }

        char Letter => WardLine.Colours[Mathf.Clamp(_colour, 0, WardLine.Colours.Length - 1)];
        Color Tint => SiegeView.TintOf(_colour);

        /// <summary>Where the barrel sits, measured down from the box's own top.</summary>
        float BarrelTop => _node.rect.height - (TurretFoot + TurretTall) * _cell;

        /// <summary>
        /// The middle of the turret's own box, in the anchoring the rest of this file uses —
        /// down from the top, so it reads the same way as <see cref="BarrelTop"/> and every
        /// <c>y</c> handed to <see cref="Reel"/>.
        ///
        /// <b>Here rather than in the caller</b>, because the stage is the only thing that knows
        /// where its turret stands, and <see cref="Claim"/>'s rings have to be centred on it.
        /// </summary>
        float TurretMid => -(_node.rect.height - (TurretFoot + TurretTall * .5f) * _cell);

        /// <summary>
        /// The same point in centre anchoring, which is what <c>Burst</c> and <c>UIKit.Halo</c>
        /// place things in. Converting once here is what stops the two spellings drifting.
        /// </summary>
        Vector2 TurretAt => new Vector2(0f, TurretMid + _node.rect.height * .5f);

        /// <summary>The stage's own size in cells, which is what every formation is laid out in.</summary>
        float TallCells => _cell > 0f ? _node.rect.height / _cell : 0f;
        float WideCells => _cell > 0f ? _node.rect.width / _cell : 0f;

        /// <summary>
        /// Builds a stage of <paramref name="size"/> inside <paramref name="parent"/>.
        ///
        /// <b>Masked</b>, because a bolt's frame is over three times as tall as it is wide with its
        /// head near the top: one leaving the barrel hangs well below the floor, and a panel is not
        /// a board with somewhere for it to go.
        /// </summary>
        public static WardFiringStage Attach(RectTransform parent, Vector2 size, Vector2 anchor,
                                             Vector2 pos, float cell, string scope)
        {
            var node = UIKit.Box("Stage", parent, size, anchor, pos);
            node.gameObject.AddComponent<RectMask2D>();

            var stage = node.gameObject.AddComponent<WardFiringStage>();
            stage._node = node;
            stage._cell = cell;
            stage._name = scope;

            stage._shadow = UIKit.Img("Shadow", node, Art.Glow(64, 3f), new Color(0f, 0f, 0f, .40f),
                                      new Vector2(cell * 1.3f, cell * .34f), new Vector2(.5f, 0f),
                                      new Vector2(0f, cell * TurretFoot + cell * .10f));

            stage._turret = UIKit.Img("Turret", node, null, Color.white,
                                      new Vector2(cell * TurretWide, cell * TurretTall),
                                      new Vector2(.5f, 0f),
                                      new Vector2(0f, cell * (TurretFoot + TurretTall * .5f)));
            stage._turret.preserveAspect = true;
            stage._turret.enabled = false;

            return stage;
        }

        /// <summary>
        /// Points the stage at a turret, loads what it draws with, and starts firing.
        ///
        /// <b>Safe to call again</b> — the loadout's bench switches colour with it — and a load
        /// that lands after a second call is discarded rather than drawn, which is what
        /// <see cref="_generation"/> is for.
        /// </summary>
        public void Show(WardModel model, int colour)
        {
            _model = model;
            _colour = colour;
            _generation++;

            Stop();
            Compose();

            // **The first volley is the one being paid for.** A pierce turret lances every
            // `Extent` shots — five, on the earned rung — so a stage counting up from nothing
            // would fire four plain bolts before showing the thing a player is deciding about, and
            // most of them will have closed the panel by then. Seeding the counter moves the
            // *phase* and leaves the rate exactly as authored, which is the only part of it a
            // preview may not flatter.
            _shots = LanceEvery > 1 ? LanceEvery - 1 : 0;

            Load(_generation);
        }

        void OnDestroy()
        {
            Stop();
            _art?.Dispose();
        }

        void Stop()
        {
            if (_firing == null) return;

            StopCoroutine(_firing);
            _firing = null;
        }

        // ----------------------------------------------------------------- the formation
        /// <summary>How often a pierce turret runs its lane, or 1 for anything else.</summary>
        int LanceEvery
            => _model != null && _model.Ability == WardAbility.Pierce && _model.Extent > 0
             ? _model.Extent : 1;

        /// <summary>
        /// Stands the arrangement this turret's ability is about.
        ///
        /// <para>
        /// <b>Each formation is the shape the rule is decided by, not a tidy row.</b> A splash is
        /// decided by <em>where</em> raiders stand, so it gets a clump; a chain by how <em>many</em>
        /// there are, so it gets a line of them; a lance by the lane, so it gets a column marching
        /// in single file — which is precisely the arrangement a splash is worst against and the
        /// reason the two abilities are not the same one twice (<c>WardAbility.Chain</c>'s note).
        /// </para>
        /// <para>
        /// <b>A prism stands two colours</b>, because being strong against two is the whole of what
        /// it buys and one target could only ever show one of them. <b>A rend stands a bulwark</b>,
        /// for the same reason from the other end: the shield is the thing the ability is about, so
        /// a preview without one is a turret whose note has nothing on screen to point at.
        /// </para>
        /// <para>
        /// <b>Everything else stands one</b>, and it is deliberate that most of the roster does:
        /// frost, ember, siphon and beacon all act on the raider that was hit or on the ward
        /// itself, so a second body would be a raider nothing happens to — the decoration invariant
        /// 5d names, drawn on the one screen that exists to say what a turret does.
        /// </para>
        /// </summary>
        void Compose()
        {
            foreach (var mark in _marks)
                if (mark.Img != null) Destroy(mark.Img.gameObject);

            _marks.Clear();
            if (_model == null || _cell <= 0f) return;

            char own = Letter;

            switch (_model.Ability)
            {
                case WardAbility.Splash:
                    // A clump, and **the middle one is the first mark** because it is the one the
                    // bolt hits: the neighbours are what the ability adds, standing half a step
                    // back, which is what "everything in the box around it" looks like.
                    Stand(own, Plain, 0f, .34f, 1.12f);
                    Stand(own, Plain, -1.55f, .06f, .92f);
                    Stand(own, Plain, 1.55f, .06f, .92f);
                    break;

                case WardAbility.Chain:
                    // A line of them, because a chain is worth more the more there are - and the
                    // bolt arcs along it from the near end rather than spreading from the middle.
                    Row(Same(own, 1 + Mathf.Clamp(_model.Extent, 1, 3)), 1.50f, .92f, .40f);
                    break;

                case WardAbility.Pierce:
                    // A column in one lane, nearest first. The near one is what the bolt hits;
                    // what a lance buys is everything standing behind it.
                    Column(Same(own, 3), .80f);
                    break;

                case WardAbility.Prism:
                    // Its own colour and the next **however many its magnitude names**, which is
                    // the set `SiegeWard.StrongAgainst` walks. Drawn rather than counted, because
                    // the whole reason this panel exists is that two rungs of one ability have to
                    // look like two different purchases (invariant 37z).
                    Row(Widened(own), 1.90f, 1.24f, .34f);
                    break;

                case WardAbility.Rend:
                    // The bulwark whose plating the ability is about. A rend previewed against a
                    // plain raider is a note with nothing on screen to point at.
                    Stand(own, Plated, 0f, .30f, 1.42f);
                    break;

                default:
                    Stand(own, Plain, 0f, .30f, 1.55f);
                    break;
            }
        }

        /// <summary>
        /// Every colour a prism turret is strong against: its own first, then as many after it as
        /// its magnitude names.
        ///
        /// <b>The same walk <c>SiegeWard.StrongAgainst</c> makes</b>, and an unauthored nought
        /// means one for the same reason it does there.
        /// </summary>
        char[] Widened(char own)
        {
            int at = WardLine.Colours.IndexOf(own);
            if (at < 0) at = 0;

            int most = WardLine.Colours.Length - 1;
            int extra = _model == null || _model.Magnitude <= 0 ? 1 : _model.Magnitude;
            if (extra > most) extra = most;

            var marks = new char[extra + 1];

            for (int i = 0; i <= extra; i++)
                marks[i] = WardLine.Colours[(at + i) % WardLine.Colours.Length];

            return marks;
        }

        static char[] Same(char colour, int count)
        {
            var all = new char[count < 1 ? 1 : count];
            for (int i = 0; i < all.Length; i++) all[i] = colour;
            return all;
        }

        /// <summary>
        /// Where a target stands, given how far down the band it is.
        ///
        /// <paramref name="down"/> is 0 at the top of the band and 1 at its foot; the band itself
        /// is derived from the stage rather than typed — see <see cref="BandTop"/>.
        /// </summary>
        Vector2 Where(float acrossCells, float down)
        {
            float foot = Mathf.Max(BandTop,
                                   TallCells - (TurretFoot + TurretTall) - BandClear);

            return new Vector2(acrossCells * _cell,
                               -Mathf.Lerp(BandTop, foot, Mathf.Clamp01(down)) * _cell);
        }

        /// <summary>The raider a stage stands unless the ability wants a particular one.</summary>
        ///
        /// <b>The hill's own creeper rather than a reel nothing else draws.</b> It stood a retired
        /// kind's beetle, which was harmless while the cast were monsters and is simply worse now:
        /// the whole job of this panel is showing a player what their money buys, and what it buys
        /// is shooting <em>these</em>.
        const string Plain = "mon_";

        /// <summary>The raider a rend is previewed against: the one whose plating it cuts.</summary>
        const string Plated = "bulwark_";

        /// <summary>One target, standing where it is put.</summary>
        void Stand(char colour, string kind, float across, float down, float size)
            => _marks.Add(new Mark
            {
                At = Where(across, down), Size = _cell * size, Colour = colour,
                Reel = kind + colour,
            });

        /// <summary>
        /// A row of targets across the stage, evenly spread.
        ///
        /// <b>Held inside the box by the mark's own width</b>, because the stage is masked and a
        /// raider whose outer half is cut off reads as a drawing fault rather than as a formation.
        /// </summary>
        void Row(char[] colours, float step, float size, float down)
        {
            int n = colours.Length;

            // The widest a row may be before the outer bodies leave the plate.
            float most = Mathf.Max(.6f, (WideCells - size * 1.15f) / Mathf.Max(1, n - 1));
            float gap = Mathf.Min(step, most);

            for (int i = 0; i < n; i++)
                _marks.Add(new Mark
                {
                    At = Where((i - (n - 1) * .5f) * gap, down),
                    Size = _cell * size,
                    Colour = colours[i],
                    Reel = Plain + colours[i],
                });
        }

        /// <summary>
        /// A column of targets in one lane, nearest first.
        ///
        /// <b>Nearest first</b>, because the one the turret hits is the one at the foot of the
        /// column and what a lance buys is everything standing behind it.
        /// </summary>
        void Column(char[] colours, float size)
        {
            int n = colours.Length;

            for (int i = 0; i < n; i++)
                _marks.Add(new Mark
                {
                    At = Where(0f, n <= 1 ? .3f : 1f - i / (float)(n - 1)),
                    Size = _cell * size,
                    Colour = colours[i],
                    Reel = Plain + colours[i],
                });
        }

        // ----------------------------------------------------------------- loading
        /// <summary>
        /// <b><c>async void</c> with the exception caught</b>, which is <c>CompanionArt.Load</c>'s
        /// shape and for its reason: a scope that failed to load must not vanish silently, and what
        /// is behind it is already drawn.
        /// </summary>
        void Load(int generation)
            => Lifeline.Of(this)?.Run(token => LoadAsync(generation, token), "WardFiringStage.Load");

        async Task LoadAsync(int generation, CancellationToken cancellation)
        {
            if (_model == null) return;

            char colour = Letter;
            var wanted = new List<AssetRequest>(8)
            {
                AssetRequest.Sprite(AssetManifest.SiegeArt(_model.ArtFor(colour))),
                AssetRequest.SpriteSet(AssetManifest.SiegeArt(_model.FireFor(colour))),
                AssetRequest.SpriteSet(AssetManifest.SiegeFx(_model.ShotFor(colour))),
                AssetRequest.SpriteSet(AssetManifest.SiegeFx(_model.MuzzleFor(colour))),
                AssetRequest.SpriteSet(AssetManifest.SiegeFx(_model.HitFor(colour))),
            };

            // Whatever the formation stands, once each. A prism's two colours and a rend's bulwark
            // are different reels from the plain one, and a stage that asked only for its own
            // colour would draw them as white rectangles (invariant 7b).
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var mark in _marks)
                if (seen.Add(mark.Reel))
                    wanted.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt(mark.Reel)));

            _art = _art ?? AssetLibrary.Hold(_name);
            await _art.LoadAsync(wanted, null, cancellation);

            if (this == null || generation != _generation) return;

            Dress();

            // Stopped first, so starting is idempotent whoever got here. `Claim` arms a restart
            // on a timer, and a load that lands *after* that timer would otherwise leave two
            // firing loops running against one turret — two volleys a beat apart, from a race
            // that only shows up on a slow load.
            Stop();

            if (isActiveAndEnabled) _firing = StartCoroutine(Firing());
        }

        void Dress()
        {
            if (_turret != null)
            {
                var body = AssetLibrary.Sprite(AssetManifest.SiegeArt(_model.ArtFor(Letter)));
                _turret.sprite = body;
                _turret.enabled = body != null;
            }

            foreach (var mark in _marks)
            {
                if (mark.Img == null)
                {
                    mark.Img = UIKit.Img("Target", _node, null, Color.white,
                                         new Vector2(mark.Size, mark.Size),
                                         new Vector2(.5f, 1f), mark.At);
                    mark.Img.preserveAspect = true;
                    mark.Img.raycastTarget = false;
                    mark.Rest = Color.white;
                }

                // An `Image` with no sprite is a white rectangle rather than a blank (invariant
                // 7b), so a widget is disabled until its frames are in hand rather than drawn
                // empty.
                var reel = AssetLibrary.Frames(AssetManifest.SiegeArt(mark.Reel));

                if (reel == null || reel.Length == 0) { mark.Img.enabled = false; continue; }

                mark.Img.enabled = true;
                Flipbook.Ensure(mark.Img, reel, 12f);
            }
        }

        // ----------------------------------------------------------------- firing
        /// <summary>
        /// Fires on a loop, so a turret can be watched rather than poked at.
        ///
        /// <b>Real seconds throughout.</b> A modal sets <c>Time.timeScale</c> to nought — and this
        /// widget's whole reason for existing is to be shown inside one — so a coroutine waiting in
        /// scaled seconds is one that never finishes (invariant 30h).
        /// </summary>
        IEnumerator Firing()
        {
            yield return new WaitForSecondsRealtime(.22f);

            while (true)
            {
                yield return Volley();
                yield return new WaitForSecondsRealtime(Between);
            }
        }

        /// <summary>
        /// One shot and whatever the ability adds to it.
        ///
        /// <para>
        /// <b>The extras are the board's own, and which ones they are is read off the same fields
        /// the rules read.</b> <c>SiegeBoard.Ability</c> spreads inside <c>Extent</c>, arcs to
        /// <c>Extent</c> more and lances every <c>Extent</c> shots; nothing here decides any of
        /// that, which is what stops a preview and a hill disagreeing about what a turret does.
        /// </para>
        /// </summary>
        IEnumerator Volley()
        {
            if (_node == null || _model == null || _marks.Count == 0) yield break;

            _shots++;

            int primary = PrimaryMark();

            Shoot(primary, false);

            var extras = Extras(primary);

            for (int i = 0; i < extras.Count; i++)
            {
                yield return new WaitForSecondsRealtime(ExtraGap);
                Shoot(extras[i], true);
            }
        }

        /// <summary>
        /// Which mark the shot itself is aimed at.
        ///
        /// <b>A prism alternates</b>, because both of its two colours are doubles and firing at one
        /// of them for ever would show half the ability. Everything else is aimed at the first,
        /// which the formations put where the rule starts: the middle of a clump, the near end of a
        /// line, the foot of a column.
        /// </summary>
        int PrimaryMark()
            => _model.Ability == WardAbility.Prism && _marks.Count > 1
             ? (_shots - 1) % _marks.Count
             : 0;

        /// <summary>Every mark this volley's ability reaches beyond the one it hit.</summary>
        List<int> Extras(int primary)
        {
            var extras = new List<int>(3);
            if (_model == null) return extras;

            switch (_model.Ability)
            {
                case WardAbility.Splash:
                case WardAbility.Chain:
                    for (int i = 0; i < _marks.Count; i++)
                        if (i != primary) extras.Add(i);
                    break;

                case WardAbility.Pierce:
                    // **Only on a lance volley**, which is the whole of what an authored `Extent`
                    // means here: every fifth shot on the earned rung and every fourth on the
                    // bought one, so the preview says how often as well as what.
                    if (LanceEvery > 0 && _shots % LanceEvery == 0)
                        for (int i = 0; i < _marks.Count; i++)
                            if (i != primary) extras.Add(i);
                    break;
            }

            return extras;
        }

        /// <summary>
        /// One bolt, from the barrel to a mark — <b>or one per barrel, on a turret drawn with
        /// two.</b>
        ///
        /// <para>
        /// <b>The panel had to learn this the day the board did, and it did not.</b> A twin
        /// turret fired from both barrels on the hill and from its middle here, which is the one
        /// disagreement this panel may never have: it is the screen a player opens to decide what
        /// a turret looks like firing, so a preview that flattered or simplified one would be
        /// worse than no preview. The geometry is <c>SiegeView.BarrelStep</c>'s, not a second
        /// copy of it.
        /// </para>
        /// <para>
        /// The recoil, the impact and the marks are the board's single bolt: only the flash and
        /// the comet are per barrel, because a second barrel is drawing and never damage.
        /// </para>
        /// </summary>
        void Shoot(int index, bool extra)
        {
            if (index < 0 || index >= _marks.Count) return;

            var mark = _marks[index];
            var to = mark.At;
            float scale = SiegeView.BoltScale(_model) * (extra ? ExtraBolt : 1f);

            if (!extra) Recoil();

            var frames = AssetLibrary.Frames(AssetManifest.SiegeFx(_model.ShotFor(Letter)));
            int barrels = SiegeView.Barrels(_model);
            float flare = SiegeView.BarrelFlare(_model);

            for (int b = 0; b < barrels; b++)
            {
                float step = SiegeView.BarrelStep(_model, b, _cell);
                var from = new Vector2(step, -BarrelTop);

                // Landing beside the mark rather than on it, so a pair stays parallel instead of
                // converging into one comet a few hundredths of a second in — `SiegeView.Bolt`'s
                // own rule, and the reason the first cut of this read as a single shot.
                var land = to + new Vector2(step * SiegeView.ApartOnArrival, 0f);
                float angle = Aim(from, land);

                Flash(from, angle, extra, flare);

                bool lands = b == barrels - 1;

                if (frames == null || frames.Length == 0)
                {
                    if (lands) Land(mark, scale, extra);
                    continue;
                }

                var bolt = Reel("Bolt", frames, _cell * BoltWide * scale, from, angle,
                                SiegeView.HeadAt, 30f, true);
                var halo = Halo(from, scale);

                Tween.Run(Flight, Ease.Linear, t =>
                {
                    if (bolt == null) return;

                    var at = Vector2.Lerp(from, land, t);
                    Head(bolt, at, SiegeView.HeadAt, angle);

                    if (halo != null) halo.rectTransform.anchoredPosition = at;

                    // The same swell the board draws, which is the only thing `SiegeView` animates
                    // about a bolt - the frames under it are doing the rest.
                    bolt.rectTransform.localScale = Vector3.one * Mathf.Lerp(.86f, 1.12f, t);
                }, bolt).OnDone(() =>
                {
                    if (bolt != null) Destroy(bolt.gameObject);
                    if (halo != null) Destroy(halo.gameObject);
                    if (lands) Land(mark, scale, extra);
                });
            }
        }

        /// <summary>
        /// The angle a reel is turned through to point along its flight.
        ///
        /// <b>Nought is straight up</b>, which is how these are baked: a comet's head is at the top
        /// of its own frame. Rotating <c>(0,1)</c> by θ gives <c>(-sin θ, cos θ)</c>, so the angle
        /// that lands on a direction is <c>atan2(-x, y)</c> — and getting the sign wrong here is
        /// the fault invariant 37aj records, where a mirror that re-derived it in the opposite axis
        /// agreed with itself and disagreed with the game.
        /// </summary>
        static float Aim(Vector2 from, Vector2 to)
        {
            var d = to - from;
            return d.sqrMagnitude < .0001f ? 0f : Mathf.Atan2(-d.x, d.y) * Mathf.Rad2Deg;
        }

        void Flash(Vector2 at, float angle, bool extra, float size)
        {
            var frames = AssetLibrary.Frames(AssetManifest.SiegeFx(_model.MuzzleFor(Letter)));
            if (frames == null || frames.Length == 0) return;

            Sweep(Reel("Muzzle", frames, _cell * MuzzleWide * size * (extra ? ExtraBolt : 1f),
                       at, angle, SiegeView.MuzzleAt, 33f, false), .32f);
        }

        void Land(Mark mark, float scale, bool extra)
        {
            var frames = AssetLibrary.Frames(AssetManifest.SiegeFx(_model.HitFor(Letter)));
            float wide = (extra ? ExtraHit : HitWide) * scale;

            if (frames != null && frames.Length > 0)
                Sweep(Reel("Hit", frames, _cell * wide, mark.At, 0f, .5f, 35f, false), .40f);

            // The thing being shot at flinches, which is what makes this an exchange rather than a
            // turret firing into the air.
            if (mark.Img != null && mark.Img.enabled)
                Tween.Punch(mark.Img.transform, extra ? .09f : .13f, .16f);

            if (!extra) Linger(mark);
        }

        /// <summary>
        /// What an ability leaves behind on the raider it hit.
        ///
        /// <para>
        /// <b>Two of the ten do something a still frame could not show</b>, and both are lasting
        /// rather than instant — a freeze and a burn are the only abilities whose whole value is
        /// what happens <em>after</em> the bolt. Drawn for the length the model authors, in real
        /// seconds, so the preview says how long as well as what.
        /// </para>
        /// <para>
        /// <b>Nothing is drawn for the other eight, deliberately.</b> A splash, a chain, a lance
        /// and a prism are already on the screen as bodies; a rend is the bulwark it is standing in
        /// front of; and a siphon and a beacon are about the ward's fuel rather than about the
        /// hill, which is a readout this stage does not have and would have to invent. An invented
        /// one is a preview making a promise the board does not draw.
        /// </para>
        /// </summary>
        void Linger(Mark mark)
        {
            if (mark.Img == null || !mark.Img.enabled || _model == null) return;

            float seconds = Mathf.Clamp(_model.Extent / 10f, .35f, 3.2f);

            switch (_model.Ability)
            {
                case WardAbility.Frost:
                    Aura(mark, new Color(.62f, .84f, 1f), new Color(.55f, .80f, 1f, .55f), seconds);
                    break;

                case WardAbility.Ember:
                    Aura(mark, new Color(1f, .78f, .52f), new Color(1f, .52f, .18f, .60f), seconds);
                    break;
            }
        }

        /// <summary>A raider wearing a lasting mark, and shedding it again when it runs out.</summary>
        void Aura(Mark mark, Color body, Color glow, float seconds)
        {
            mark.Img.color = body;

            if (mark.Aura == null)
            {
                mark.Aura = UIKit.Img("Aura", _node, Art.Glow(96, 2.2f), glow,
                                      Vector2.one * (mark.Size * 1.35f), new Vector2(.5f, 1f),
                                      mark.At);
                mark.Aura.raycastTarget = false;
                mark.Aura.transform.SetSiblingIndex(mark.Img.transform.GetSiblingIndex());
            }

            mark.Aura.color = glow;
            mark.Aura.enabled = true;

            // **On a channel**, because a burn lasts three seconds and a volley comes round every
            // one: two fading the same raider at once would have it flickering between two
            // strengths, which is `Tween`'s own reason for channels.
            Tween.Run(seconds, Ease.Linear, t =>
            {
                if (mark.Aura != null) mark.Aura.color = Pal.A(glow, glow.a * (1f - t));
                if (mark.Img != null) mark.Img.color = Color.Lerp(body, mark.Rest, t);
            }, mark.Img, "aura").OnDone(() =>
            {
                if (mark.Aura != null) mark.Aura.enabled = false;
                if (mark.Img != null) mark.Img.color = mark.Rest;
            });
        }

        /// <summary>The turret's own recoil frames, exactly as the line plays them.</summary>
        void Recoil()
        {
            if (_turret == null || !_turret.enabled) return;

            var frames = AssetLibrary.Frames(AssetManifest.SiegeArt(_model.FireFor(Letter)));
            if (frames == null || frames.Length == 0) return;

            Flipbook.Attach(_turret, frames, frames.Length / RecoilFor, false).OnFinished = () =>
            {
                if (_turret == null) return;

                Flipbook.Detach(_turret);
                _turret.sprite = AssetLibrary.Sprite(AssetManifest.SiegeArt(_model.ArtFor(Letter)));
            };
        }

        // ------------------------------------------------------------------- claim
        /// <summary>
        /// How long the arrival runs, and the shape of it. The stage stops firing for exactly
        /// this long, so it is one number rather than a set that can disagree.
        /// </summary>
        const float ClaimFor = 1.15f, ClaimFlash = .40f, ClaimStand = .66f, ClaimRingFor = .62f;

        /// <summary>How many waves go out, how far, and how far apart they leave.</summary>
        const int ClaimRings = 3;
        const float ClaimRingTo = 5.4f, ClaimRingGap = .085f;

        /// <summary>
        /// The turret arrives — the moment it stops being something on a shelf and becomes the
        /// player's.
        ///
        /// <para>
        /// <b>Drawn as an arrival rather than as a flourish over what was already there</b>, which
        /// is this project's own language for a thing the player earned (invariant 20m: bare for a
        /// beat, motes gathering back, the thing standing up under a ring). The panel showed this
        /// turret firing before it was bought — that is what the preview is for — so what has
        /// changed is not that it is visible, it is that it is *theirs*, and something has to say
        /// so. A shower of sparks over a turret that never moved would say a purchase went
        /// through; a turret standing up says what was bought.
        /// </para>
        /// <para>
        /// <b>It lives here rather than in the panel because the coordinates do.</b> Where the
        /// turret stands, how big a cell is and what is masked are all facts about the stage, and
        /// a caller placing rings on a turret it cannot measure is the shape this file already
        /// refuses for bolts and muzzle flashes.
        /// </para>
        /// <para>
        /// <b>Firing stops for the length of it and starts again after.</b> A recoil reel half
        /// played under a turret springing up from nothing is the one thing here that would read
        /// as a fault rather than as a flourish — and the restart is pinned to the generation it
        /// was armed in, so a <see cref="Show"/> landing mid-claim (the bench switches colour
        /// under it) leaves one firing loop rather than two.
        /// </para>
        /// </summary>
        /// <param name="whiteOut">
        /// Whether the stage flashes white on its own. <b>False when something bigger is already
        /// doing it</b> — the reveal whites out the whole screen on the same beat, and two flashes
        /// a frame apart is not twice as bright, it is one flash with a seam in it.
        /// </param>
        public void Claim(bool whiteOut = true)
        {
            if (_node == null || _model == null) return;

            Stop();

            int generation = _generation;
            var tint = Tint;
            float y = TurretMid;

            // The white the rest of it resolves out of. Over everything, and gone before the
            // turret has finished standing, so what the eye follows is the turret rather than
            // the flash.
            if (whiteOut)
            {
                var flash = UIKit.Img("Claim", _node, Art.Round(30),
                                      new Color(1f, .99f, .94f, .88f),
                                      new Vector2(_node.rect.width, _node.rect.height),
                                      new Vector2(.5f, .5f), Vector2.zero);
                flash.raycastTarget = false;
                Tween.Fade(flash, 0f, ClaimFlash, Ease.OutQuad)
                     .OnDone(() => { if (flash != null) Destroy(flash.gameObject); });
            }

            // Light behind it, in its own colour, so the turret is lit by the moment rather than
            // merely standing in front of it. Halo puts itself behind its siblings.
            var halo = UIKit.Halo(_node, tint, _cell * 4.6f, 0f, TurretAt);
            halo.raycastTarget = false;
            Tween.Fade(halo, .42f, .22f)
                 .OnDone(() => Tween.Fade(halo, 0f, .72f)
                                    .OnDone(() => { if (halo != null) Destroy(halo.gameObject); }));

            // Waves out of the turret's own middle. Each one a little later and a little wider
            // than the last, which is the shape a payoff keeps reaching for here; the stage is
            // masked, so they leave rather than piling up at the edge of the box.
            for (int i = 0; i < ClaimRings; i++)
                Wave(y, i == 1 ? Pal.Lift(tint, .45f) : tint, i * ClaimRingGap);

            // The turret itself: gone under the white, back on an elastic. `Pop` is the same
            // spring every earned thing in this game arrives on.
            if (_turret != null && _turret.enabled)
            {
                Flipbook.Detach(_turret);
                _turret.sprite = AssetLibrary.Sprite(AssetManifest.SiegeArt(_model.ArtFor(Letter)));
                Tween.Pop(_turret.transform, 0f, ClaimStand, .12f);
            }

            // Its shadow with it, or the turret springs up off a mark that never moved.
            if (_shadow != null)
            {
                _shadow.transform.localScale = new Vector3(.2f, .2f, 1f);
                Tween.Scale(_shadow.transform, 1f, ClaimStand, Ease.OutBack).Delay(.12f);
            }

            Burst.Sparks(_node, TurretAt, tint, 24, 430f, 30f, .78f);

            Tween.After(ClaimFor, () =>
            {
                if (this == null || generation != _generation || _firing != null) return;
                if (isActiveAndEnabled) _firing = StartCoroutine(Firing());
            }, this);
        }

        /// <summary>One ring of <see cref="Claim"/>, leaving the turret and thinning as it goes.</summary>
        void Wave(float y, Color colour, float delay)
        {
            var ring = UIKit.Img("Wave", _node, Art.Ring(160, 11f), Pal.A(colour, .0f),
                                 Vector2.one * (_cell * .8f), new Vector2(.5f, 1f),
                                 new Vector2(0f, y));
            ring.raycastTarget = false;

            var rt = ring.rectTransform;

            Tween.Run(ClaimRingFor, Ease.OutQuint, t =>
            {
                if (ring == null) return;

                float wide = Mathf.Lerp(.8f, ClaimRingTo, t) * _cell;
                rt.sizeDelta = new Vector2(wide, wide);
                ring.color = Pal.A(colour, .85f * (1f - t) * (1f - t));
            }, ring).Delay(delay).OnDone(() => { if (ring != null) Destroy(ring.gameObject); });
        }

        // ----------------------------------------------------------------- widgets
        /// <summary>
        /// One reel on the stage, anchored and turned the way the board anchors and turns it.
        ///
        /// A comet's head sits <c>SiegeView.HeadAt</c> of the way up its own frame and a muzzle
        /// flash sits at <c>MuzzleAt</c>, so drawing one is placing its <em>anchor</em> rather than
        /// its centre — getting that wrong is a bolt that appears to land before it arrives.
        /// </summary>
        Image Reel(string name, Sprite[] frames, float wide, Vector2 at, float angle, float anchor,
                   float fps, bool loop)
        {
            var img = UIKit.Img(name, _node, frames[0], Color.white, new Vector2(wide, wide),
                                new Vector2(.5f, 1f), at);
            img.raycastTarget = false;
            img.preserveAspect = true;

            var first = frames[0];
            float tall = first != null && first.rect.width > 0f
                ? wide * first.rect.height / first.rect.width
                : wide;

            img.rectTransform.sizeDelta = new Vector2(wide, tall);
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            Head(img, at, anchor, angle);

            Flipbook.Attach(img, frames, fps, loop);
            return img;
        }

        /// <summary>
        /// Puts a reel's anchor point on <paramref name="at"/>, not its centre.
        ///
        /// <b>Along the reel's own axis rather than along the box's.</b> A bolt that travels
        /// sideways is turned, so backing the frame off by its head offset in screen-y would put
        /// the head off the line it is flying down — invisible on the one formation that fires
        /// straight up and wrong on every other.
        /// </summary>
        static void Head(Image img, Vector2 at, float anchor, float angle)
        {
            if (img == null) return;

            float back = (anchor - .5f) * img.rectTransform.sizeDelta.y;
            float rad = angle * Mathf.Deg2Rad;
            var up = new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad));

            img.rectTransform.anchoredPosition = at - up * back;
        }

        /// <summary>The light under a bolt's head — the one thing the board tints from `Pal`.</summary>
        Image Halo(Vector2 at, float scale)
        {
            var img = UIKit.Img("Halo", _node, Art.Glow(96, 2.0f), Pal.A(Pal.Lift(Tint, .5f), .8f),
                                Vector2.one * (_cell * 1.7f * scale), new Vector2(.5f, 1f), at);
            img.raycastTarget = false;
            img.transform.SetAsFirstSibling();
            return img;
        }

        /// <summary>Clears a one-shot reel away after it has played.</summary>
        void Sweep(Image img, float after)
        {
            if (img == null) return;
            Tween.After(after, () => { if (img != null) Destroy(img.gameObject); }, img);
        }
    }
}
