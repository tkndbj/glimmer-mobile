using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Ranks;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The hall of ranks: one badge at a time on a lit stage, the whole ladder as a rail of
    /// seats under it, and exactly what the chosen rung asks for on a plate at the foot.
    ///
    /// <para>
    /// <b>It is one composed screen and not a list, and that is the whole of the change.</b>
    /// The page before this was seven checklist cards down a scrolling wall — every fact was
    /// on it and none of it felt like a game, which was the owner's verdict on 2026-09-26. A
    /// rank screen in any game people rate is a <em>stage</em>: the badge you hold, large and
    /// lit, the ladder as a row of seats you can see the whole of at once, and the details of
    /// one rung at a time underneath. So the ladder is browsed rather than scrolled — tap a
    /// seat, tap a chevron or swipe the stage — and the stage redresses itself for the rung
    /// chosen: its light, its ring, its name, its plate.
    /// </para>
    ///
    /// <para>
    /// <b>The light carries the rung's metal; the room does not.</b> The wash over the wall is
    /// one deep navy-violet on every rung, because a wash tinted gold over a blue wall is
    /// olive and one tinted copper is mud (invariant 44g, arriving through a blend rather than
    /// a multiply). What changes with the rung is everything that reads as <em>light</em> —
    /// the two ray fans turning behind the badge, the aurora drifting across the room, the
    /// halo, the ring and the spark at its head, the fireflies — which are additive-looking
    /// over a dark ground and stay the colour they were given (<see cref="RankLook"/>).
    /// </para>
    ///
    /// <para>
    /// <b>The ring is the progress, and it is drawn with uGUI's radial fill over a generated
    /// ring</b> — no art, no address, nothing that can arrive as a white rectangle (7b). Its
    /// share is the mean of the rung's clamped line shares (<see cref="Share"/>), the same
    /// arithmetic <see cref="RankLedger.Progress01"/> runs for the next rung, run here for
    /// whichever rung is chosen so a locked rung can honestly show how much of it is already
    /// done. The spark rides the head of the fill so the arc reads as a thing that is
    /// <em>filling</em> rather than as a static band.
    /// </para>
    ///
    /// <para>
    /// <b>Nothing on the rail is dimmed, and a locked seat still shows its badge.</b> A page
    /// of bright medallions is a trophy case, and a ladder whose upper rungs are hidden is a
    /// ladder nobody wants to climb; what says <em>not yet</em> is the seat — smaller badge, dim
    /// rim, a padlock chip on the corner — and never the picture. The one thing that moves
    /// with the ledger is which seats are lit, which is the only reading a glance needs.
    /// </para>
    ///
    /// <para>
    /// <b>Laid out with anchors and nothing is measured off a rect.</b> The stage stretches
    /// between the header and the rail, so a tall phone gives the badge air and the shortest
    /// canvas this game is drawn on (<see cref="CanvasFit.ShortestCanvas"/>) still fits every
    /// band. The plate's band is sized for the tallest rung the ladder ships and scrolls only
    /// if a retune ever gives a rung more lines than that band holds — a rung's lines are
    /// capped (<see cref="RankLadder.MaxRequirements"/>), so the band can never be asked for
    /// more than eight.
    /// </para>
    ///
    /// <para>
    /// <b>Built once, redressed on a tap, repainted from the ledger</b> (<c>CRAFT.md</c>: Show
    /// animates, Refresh does not). A rank can move while the page is standing — a merge
    /// landing another device's battles is the ordinary case — and a repaint rewrites the
    /// ring, the seats, the counts and the bars without replaying any entrance. A retuned
    /// ladder is a different page and rebuilds whole. Choosing a rung <em>is</em> a gesture,
    /// so the swap is allowed to move.
    /// </para>
    ///
    /// <para>
    /// <b>No nav bar, and a back key to the map.</b> This is a side page off
    /// <see cref="LevelsScreen"/>, reached from that screen's badge and the hub's seat, so the
    /// loop is map, hall, map. <c>MapMemory</c> puts the player back on the chapter they left
    /// (invariant 8b), which is why the back key needs no argument.
    /// </para>
    ///
    /// <para>
    /// The mirror is <c>Tools/render_ranks.py</c>, which draws every state of the stage off
    /// the shipped ladder and measures every caption it can say against the box it is drawn
    /// in (invariant 19n) — a rank ladder is content, and a sentence is one retune away from
    /// outgrowing its band at all times.
    /// </para>
    /// </summary>
    public sealed class RanksScreen : View
    {
        public override string Track => "mus_menu";

        // ------------------------------------------------------------------ the stack
        // Every band below is a height, and the bands are stacked by anchors: the header hangs
        // from the top, the rail and the plate stand on the bottom, and the stage stretches
        // between them. Nothing here reads a rect, because a screen cannot trust its own on the
        // frame it is built (`CRAFT.md`).
        const float ChromeSize = 92f, BannerH = 138f;
        const float HeaderTop = 22f;

        /// <summary>The header band: the ribbon and the air under it.</summary>
        public const float HeaderH = HeaderTop + BannerH + 10f;

        /// <summary>
        /// The least the stage may be. On the shortest canvas the game draws on it is exactly
        /// this; on a taller phone it takes every unit the other bands leave, and the cluster
        /// standing in it grows to fill them (<see cref="StageMost"/>).
        /// </summary>
        public const float StageLeast = 820f;

        /// <summary>
        /// How far the hero cluster may grow on a tall canvas, as a scale over
        /// <see cref="StageLeast"/>. A 20:9 phone leaves the stage half as tall again as the
        /// shortest canvas does, and a cluster hung at one size in that room left a dead band
        /// of wall above the rail; so the cluster is scaled to the room instead, which is what
        /// every hero screen in a game people rate does with a taller phone. The cap is where
        /// the chevrons would leave the canvas: <c>(ChevronX + ChevronSize / 2) x 1.24</c> is
        /// 528 of a 540 half-width.
        /// </summary>
        public const float StageMost = 1.24f;

        /// <summary>The rail's band, the seats in it, and the air between two seats.</summary>
        public const float RailH = 150f;
        const float SeatSize = 104f, SeatPitchMost = 140f, SeatFace = .82f, SeatFaceLocked = .64f;

        /// <summary>How much larger the chosen seat stands than its neighbours.</summary>
        const float SeatChosen = 1.22f;

        /// <summary>The chain between two seats: its thickness and the air it leaves each end.</summary>
        const float LinkThick = 12f, LinkGap = 6f;

        /// <summary>The padlock chip on a locked seat's corner.</summary>
        const float LockChip = 34f;

        /// <summary>
        /// The plate: its width, its head, one line's pitch, its foot, and the air under it.
        /// Grown on 2026-09-26 at the owner's instruction ("make the box bigger so texts are
        /// more readable"): the line pitch went 72 -> 88 and the sentence 27 -> 32pt, paid for
        /// by the blurb coming off the stage and <see cref="StageLeast"/> coming down with it.
        /// </summary>
        public const float PlateW = 1024f, PlateHead = 76f, LineH = 88f, PlateFoot = 22f;
        public const float PlateGap = 18f, FootPad = 24f;

        /// <summary>One line's well and the furniture in it.</summary>
        const float WellInset = 26f, WellH = 78f, MarkSize = 38f, CountW = 210f, BarH = 12f;

        /// <summary>How much of the shortest canvas the display's insets may take before the plate's band gives way.</summary>
        const float InsetAllowance = 150f;

        // ------------------------------------------------------------------ the stage
        // Positions inside the stage, **down from its top**. The stage stretches to take a tall
        // phone's air, and the cluster hangs from the top of it so the hero stands high and
        // the air lands above the rail rather than splitting the badge from its name. `UIKit.Box`
        // pivots at centre, so every one of these is a middle and a height (49h's lesson), and
        // the last of them (the pill's foot) has to land inside `StageLeast`.
        const float RingTop = 320f, RingSize = 540f, RingThick = 9f, BadgeSize = 420f;
        const float ChevronX = 380f, ChevronSize = 92f;
        const float ChipTop = RingTop + RingSize * .5f, ChipW = 156f, ChipH = 46f;
        const float EyebrowTop = 646f, EyebrowH = 32f;
        const float NameTop = 704f, NameH = 76f;
        const float PillTop = 790f, PillH = 56f, PillW = 600f;
        const float TextW = 900f;

        const float HaloSize = 980f, CoreSize = 560f, FanSize = 1300f, Fan2Size = 940f, SparkSize = 58f;
        const float FanAlpha = .22f, Fan2Alpha = .13f, HaloAlpha = .46f, CoreAlpha = .34f;

        /// <summary>The two aurora masses and their drift, in the ceremony's own figures.</summary>
        static readonly Vector2[] AuroraHome = { new Vector2(-360f, -240f), new Vector2(380f, -470f) };
        static readonly float[] AuroraSize = { 980f, 820f };
        static readonly float[] AuroraAlpha = { .18f, .13f };

        /// <summary>
        /// The room's wash: one deep navy-violet on every rung, opaque at the top of the stage
        /// and gone by its foot. See the class remarks for why it is not the rung's metal. It
        /// is not black and it is not a vignette — the light on top of it is what keeps this
        /// from being the room three ceremonies came back from as <em>so dark</em> (44m).
        /// </summary>
        static readonly Color WashDeep = Pal.Hex("#0F1552");

        /// <summary>The ring's trough, dark enough to read on the wash and on the light alike.</summary>
        static readonly Color Trough = new Color(.03f, .05f, .14f, .78f);

        /// <summary>A locked rung's writing: a cool steel, and <b>opaque</b> (the old page's lesson).</summary>
        static readonly Color LockedInk = Pal.Hex("#93A6C4");

        /// <summary>
        /// The bar's orange, pre-divided against <see cref="Skins.Fill"/> as every bar in the
        /// game is (45h). Deliberately not the rung's metal — a bar is read for its length, and
        /// silver on a navy trough is a fill you have to look for.
        /// </summary>
        static readonly Color BarOrange = new Color(1f, .588f, .118f, 1f);

        static readonly Vector2 Top = new Vector2(.5f, 1f);
        static readonly Vector2 Bottom = new Vector2(.5f, 0f);
        static readonly Vector2 Centre = new Vector2(.5f, .5f);
        static readonly Vector2 Left = new Vector2(0f, .5f);

        // ------------------------------------------------------------------ state
        sealed class Seat
        {
            public RankDefinition Rung;
            public RectTransform Root;
            public Image Rim;
            public Image Glow;
            public Image Badge;
            public Image Lock;
            public Image Pulse;
            public Image Link;
            public bool Pulsing;
        }

        sealed class Line
        {
            public RankRequirement Req;
            public Image Mark;
            public Text Said;
            public Text Count;
            public RectTransform Fill;
            public float Room;
        }

        /// <summary>
        /// Fits the hero cluster to the stage it stands in: one scale, read off the stage's
        /// own rect, clamped to <c>[1, StageMost]</c>. See <see cref="StageMost"/>.
        /// </summary>
        sealed class StageFit : MonoBehaviour
        {
            RectTransform _stage, _cluster;
            float _seen = -1f;

            public void Init(RectTransform stage, RectTransform cluster)
            {
                _stage = stage;
                _cluster = cluster;
            }

            void LateUpdate()
            {
                if (!_stage || !_cluster) return;

                float h = _stage.rect.height;
                if (h <= 1f || Mathf.Abs(h - _seen) < .5f) return;
                _seen = h;

                float k = Mathf.Clamp(h / StageLeast, 1f, StageMost);
                _cluster.localScale = new Vector3(k, k, 1f);
            }
        }

        readonly List<Seat> _seats = new List<Seat>();
        readonly List<Line> _lines = new List<Line>();

        RectTransform _stage, _cluster, _plateBand, _plate;
        Image _fanA, _fanB, _halo, _core, _track, _ring, _spark, _badge;
        Image[] _aurora;
        Fireflies _flies;
        Image _chip, _chipRim;
        Text _ordinal, _eyebrow, _name, _pill, _plateTitle, _plateCount;
        TextGradient _shimmer;
        Btn _prev, _next;
        Image _plateFace;

        /// <summary>The rung on the stage, by ordinal. Nought only while the ladder is empty.</summary>
        int _chosen;

        /// <summary>The rung ids this page was built for, so a retuned ladder rebuilds whole.</summary>
        string _built;

        protected override void Build()
        {
            _seats.Clear();
            _lines.Clear();
            _built = LadderKey();

            Scenery.Plain(Content);

            var ladder = RankLedger.Ladder;
            _chosen = ladder.IsEmpty ? 0 : Opening(ladder);

            BuildHeader();
            BuildStage();
            BuildRail();
            BuildPlateBand();

            Dress(animate: true);
        }

        void OnEnable() { RankLedger.Changed += OnChanged; }
        void OnDisable() { RankLedger.Changed -= OnChanged; }

        public override bool OnBack() { Flow.Go<LevelsScreen>(); return true; }

        /// <summary>
        /// Which rung the hall opens on: the one held, or the first one to climb when none is.
        /// "What am I" is the question the page is opened with, and the seat being climbed is
        /// a tap away and already pulsing on the rail.
        /// </summary>
        static int Opening(RankLadder ladder)
        {
            int held = RankLedger.Ordinal;
            return held > 0 ? held : 1;
        }

        string LadderKey()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var rung in RankLedger.Ladder.Rungs) sb.Append(rung.Id).Append('|');
            return sb.ToString();
        }

        void OnChanged()
        {
            if (!Living) return;

            if (LadderKey() != _built) { ClearContent(); Build(); return; }
            Repaint();
        }

        // ------------------------------------------------------------------ readings
        /// <summary>
        /// How much of a rung is done: the mean of its clamped line shares, which is
        /// <see cref="RankLedger.Progress01"/>'s arithmetic asked of any rung rather than of
        /// the next one only. A held rung answers one; a rung with no lines answers one too,
        /// because there is nothing left of it to do.
        /// </summary>
        static float Share(RankDefinition rung, CatalogIndex index)
        {
            if (rung == null || rung.Requirements.Count == 0) return 1f;

            float total = 0f;
            for (int i = 0; i < rung.Requirements.Count; i++)
            {
                var line = rung.Requirements[i];
                float share = line.Target <= 0 ? 1f : line.Held(index) / (float)line.Target;
                total += Mathf.Clamp01(share);
            }
            return total / rung.Requirements.Count;
        }

        enum Standing { Held, Earned, Next, Locked }

        static Standing StandingOf(RankDefinition rung)
        {
            int held = RankLedger.Ordinal;
            if (rung.Ordinal == held) return Standing.Held;
            if (rung.Ordinal < held) return Standing.Earned;
            return rung.Ordinal == held + 1 ? Standing.Next : Standing.Locked;
        }

        // ------------------------------------------------------------------ chrome
        void BuildHeader()
        {
            float cy = -(HeaderTop + BannerH * .5f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * ChromeSize,
                             new Vector2(0f, 1f), new Vector2(76f, cy),
                             () => Flow.Go<LevelsScreen>());

            var ribbon = Scenery.TitleRibbon(Safe, Loc.Get("ui.ranks.title").ToUpperInvariant(),
                                             new Vector2(720f, BannerH), Top, new Vector2(0f, cy), 42);
            ribbon.transform.localScale = Vector3.zero;
            Tween.Pop(ribbon.transform, 0f, .5f, .06f);
        }

        // ------------------------------------------------------------------ the stage
        /// <summary>
        /// The room and the light in it, then the ring, then the badge, then the words — in
        /// that order, so each draws over the last.
        /// </summary>
        void BuildStage()
        {
            float bottom = FootPad + PlateBand() + PlateGap + RailH;

            _stage = UIKit.Node("Stage", Safe);
            UIKit.StretchTo(_stage, 0f, bottom, 0f, HeaderH);

            // Clipped, so the fans and the aurora stop at the stage's own edges rather than
            // turning behind the ribbon and the rail.
            _stage.gameObject.AddComponent<RectMask2D>();

            // A swipe turns the page. The handler is on the stage itself, because the event
            // system finds a drag handler by walking *up* from whatever was hit — so a drag
            // that starts on the badge, the name or a chevron still turns the page. The
            // catcher under everything is what makes the empty parts of the stage hittable.
            _stage.gameObject.AddComponent<Swipe>().Swiped = dir => Step(-dir);
            var catcher = UIKit.Img("Catcher", _stage, Art.Pixel, new Color(0f, 0f, 0f, 0f));
            UIKit.StretchTo((RectTransform)catcher.transform, 0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            // The wash: opaque at the top of the stage and gone by its foot, so the room reads
            // as a place the badge is lit in and the wall shows through under it.
            var wash = UIKit.Img("Wash", _stage,
                                 Art.Gradient(Pal.A(Color.white, 0f), Pal.A(Color.white, .80f),
                                              Pal.A(Color.white, .97f)),
                                 WashDeep);
            UIKit.StretchTo((RectTransform)wash.transform, 0f, 0f, 0f, 0f);

            // The fireflies live in the stage, not the cluster, so they fill whatever room a
            // tall canvas gives and are never scaled into blobs.
            _flies = Fireflies.Spawn(_stage, 16, Pal.Sun, 5f, 16f);

            // Everything that is *the hero* stands in one node the size of the least stage,
            // standing on the stage's foot and scaled to the stage by `StageFit` — read off
            // the stage's rect on the frames after the build, never on the build frame
            // (`CRAFT.md`), and re-read whenever the canvas changes shape, which a tablet in
            // split view does. **It stands on the foot, pivoted there, so it grows upward**:
            // the pill is always a hand's width above the rail, and whatever air a tall phone
            // has lands under the ribbon, where the wash and the fans fill it. Centred, the
            // same air split in two and left a dead band of wall between the pill and the
            // seats (the owner, 2026-09-26).
            _cluster = UIKit.Box("Cluster", _stage, new Vector2(Boot.RefWidth, StageLeast), Bottom, Vector2.zero);
            _cluster.pivot = new Vector2(.5f, 0f);
            _cluster.anchoredPosition = Vector2.zero;
            _cluster.gameObject.AddComponent<StageFit>().Init(_stage, _cluster);

            _aurora = new Image[AuroraHome.Length];
            for (int i = 0; i < _aurora.Length; i++)
            {
                // In the stage rather than the cluster, hung from its top: on a tall phone the
                // room above the hero is theirs to fill, and scaling them with the badge would
                // make blobs.
                _aurora[i] = UIKit.Img("Aurora" + i, _stage, Art.Glow(256, 1.7f), Pal.A(Pal.Sun, 0f),
                                       Vector2.one * AuroraSize[i], Top, AuroraHome[i]);
                Drift(_aurora[i], AuroraHome[i], i == 0 ? 120f : 90f, i == 0 ? 17f : 23f);
            }

            _fanA = UIKit.Img("Fan", _cluster, Art.Rays(512, 14), Pal.A(Pal.Sun, 0f),
                              Vector2.one * FanSize, Top, new Vector2(0f, -RingTop));
            Turn((RectTransform)_fanA.transform, 360f, 48f);

            _fanB = UIKit.Img("Fan2", _cluster, Art.Rays(256, 7), Pal.A(Pal.Sun, 0f),
                              Vector2.one * Fan2Size, Top, new Vector2(0f, -RingTop));
            Turn((RectTransform)_fanB.transform, -360f, 31f);

            _halo = UIKit.Img("Halo", _cluster, Art.Glow(256, 1.8f), Pal.A(Pal.Sun, 0f),
                              Vector2.one * HaloSize, Top, new Vector2(0f, -RingTop));

            // A tighter light under the badge itself, so it reads as lit rather than as a
            // sticker on a lit wall.
            _core = UIKit.Img("Core", _cluster, Art.Glow(128, 2.4f), Pal.A(Pal.Sun, 0f),
                              Vector2.one * CoreSize, Top, new Vector2(0f, -RingTop));

            // The ring: a dark trough and a radial fill over it. `fillOrigin` 0 on a
            // `Radial360` is the bottom, and the fill runs clockwise from there, which is
            // where the ordinal chip sits and so where the seam is hidden.
            _track = UIKit.Img("Track", _cluster, Art.Ring(256, RingThick), Trough,
                               Vector2.one * RingSize, Top, new Vector2(0f, -RingTop));

            _ring = UIKit.Img("Ring", _cluster, Art.Ring(256, RingThick), Pal.Sun,
                              Vector2.one * RingSize, Top, new Vector2(0f, -RingTop));
            _ring.type = Image.Type.Filled;
            _ring.fillMethod = Image.FillMethod.Radial360;
            _ring.fillOrigin = (int)Image.Origin360.Bottom;
            _ring.fillClockwise = true;
            _ring.fillAmount = 0f;

            _spark = UIKit.Img("Spark", _cluster, Art.Spark(96), Pal.Sun,
                               Vector2.one * SparkSize, Top, new Vector2(0f, -ChipTop));

            _badge = UIKit.Img("Badge", _cluster, null, Color.white,
                               Vector2.one * BadgeSize, Top, new Vector2(0f, -RingTop));
            _badge.preserveAspect = true;
            // From nothing on the first dress, so the open is an arrival rather than a shrink.
            _badge.transform.localScale = Vector3.zero;

            _prev = UIKit.IconButton("Prev", _cluster, Skins.Nav, "ic_left",
                                     Vector2.one * ChevronSize, Top, new Vector2(-ChevronX, -RingTop),
                                     () => Step(-1));
            _next = UIKit.IconButton("Next", _cluster, Skins.Nav, "ic_right",
                                     Vector2.one * ChevronSize, Top, new Vector2(ChevronX, -RingTop),
                                     () => Step(1));

            // The ordinal, in a chip on the ring's foot. It covers the fill's seam and is the
            // one line on the stage that is true whether or not the rung is held.
            _chip = UIKit.Img("Chip", _cluster, Art.Round(14), new Color(0f, 0f, 0f, .55f),
                              new Vector2(ChipW, ChipH), Top, new Vector2(0f, -ChipTop));
            _chipRim = UIKit.Img("ChipRim", _chip.transform, Art.RoundOutline(14, 3f), Pal.Sun);
            UIKit.StretchTo((RectTransform)_chipRim.transform, 0f, 0f, 0f, 0f);
            _ordinal = UIKit.Shrinkable(
                UIKit.Titled("Ordinal", _chip.transform, string.Empty, 24, Pal.Sun,
                             TextAnchor.MiddleCenter, new Vector2(ChipW - 16f, 30f), Centre,
                             Vector2.zero, 0f, 2f), 14);

            _eyebrow = UIKit.Shrinkable(
                UIKit.Titled("Eyebrow", _cluster, string.Empty, 26, Pal.Sun,
                             TextAnchor.MiddleCenter, new Vector2(TextW, EyebrowH), Top,
                             new Vector2(0f, -EyebrowTop), 0f, 2f), 15);

            // The name wears a shimmer of the rung's metal — `TextGradient` runs across the
            // glyphs, so the light lands as a highlight rather than as a flat tint — over the
            // dark outline every caption here carries, which is what keeps silver legible on
            // a lit room (44n).
            _name = UIKit.Shrinkable(
                UIKit.Titled("Name", _cluster, string.Empty, 78, Color.white,
                             TextAnchor.MiddleCenter, new Vector2(TextW, NameH), Top,
                             new Vector2(0f, -NameTop), 3f, 4f), 34);
            _shimmer = _name.gameObject.AddComponent<TextGradient>();

            _pill = UIKit.Shrinkable(
                Scenery.Pill(_cluster, string.Empty, 26, new Vector2(PillW, PillH), Top,
                             new Vector2(0f, -PillTop), new Color(.04f, .07f, .18f, .82f), "ic_star"), 16);
        }

        /// <summary>One aurora mass wandering a loop round its home.</summary>
        static void Drift(Image blob, Vector2 home, float span, float period)
        {
            var rt = (RectTransform)blob.transform;
            Tween.Run(period, Ease.Linear, t =>
            {
                if (!rt) return;
                float a = t * Mathf.PI * 2f;
                rt.anchoredPosition = home + new Vector2(Mathf.Sin(a) * span, Mathf.Cos(a * 2f) * span * .5f);
            }, blob, "drift").Loop(-1, false);
        }

        /// <summary>A fan turning for ever — a whole turn per loop so the join is invisible.</summary>
        static void Turn(RectTransform rt, float degrees, float period)
        {
            Tween.RotateBy(rt, degrees, period, Ease.Linear).Loop(-1, false);
        }

        // ------------------------------------------------------------------ the rail
        /// <summary>
        /// Every rung as a seat on one rail, chained, standing on the plate's band. Sized from
        /// the room rather than typed: a longer ladder closes the air, then the seats.
        /// </summary>
        void BuildRail()
        {
            var ladder = RankLedger.Ladder;
            int n = ladder.Count;

            float bottom = FootPad + PlateBand() + PlateGap;
            var rail = UIKit.Box("Rail", Safe, new Vector2(PlateW, RailH), Bottom,
                                 new Vector2(0f, bottom + RailH * .5f));
            if (n <= 0) return;

            float pitch = Mathf.Min(SeatPitchMost, (PlateW - 40f) / n);
            float seat = Mathf.Min(SeatSize, pitch - 10f);
            float x0 = -pitch * (n - 1) * .5f;

            // The chain first, so every link is under every seat.
            var links = UIKit.Node("Links", rail);
            for (int i = 0; i < n; i++)
            {
                var rung = ladder.At(i + 1);
                float x = x0 + pitch * i;

                var s = new Seat { Rung = rung };
                s.Root = UIKit.Box("Seat_" + rung.Id, rail, Vector2.one * seat, Centre, new Vector2(x, 0f));

                if (i < n - 1)
                {
                    float length = pitch - seat - LinkGap * 2f;
                    s.Link = UIKit.Img("Link_" + rung.Id, links, Art.Capsule((int)LinkThick, 48),
                                       Pal.A(Pal.Cream, .14f), new Vector2(length, LinkThick), Centre,
                                       new Vector2(x + pitch * .5f, 0f));
                }

                s.Glow = UIKit.Img("Glow", s.Root, Art.Glow(128, 2.0f), Pal.A(Pal.Sun, 0f),
                                   Vector2.one * (seat * 1.9f), Centre, Vector2.zero);

                // The pulse on the seat being climbed, under the moulding so it comes out
                // from behind it. Built for every seat and lit on one, because which seat that
                // is moves while the page is standing.
                s.Pulse = UIKit.Img("Pulse", s.Root, Art.Ring(128, 8f), Pal.A(Pal.Sun, 0f),
                                    Vector2.one * (seat + 16f), Centre, Vector2.zero);

                var slot = UIKit.Img("Slot", s.Root, Art.S("Ui/" + Skins.Slot), Color.white,
                                     Vector2.one * seat, Centre, Vector2.zero);
                slot.raycastTarget = true;
                slot.gameObject.AddComponent<Btn>().Setup(() => Choose(rung.Ordinal));

                s.Rim = UIKit.Img("Rim", s.Root, Art.RoundOutline(20, 5f), Pal.A(Pal.Sun, 0f),
                                  Vector2.one * seat, Centre, Vector2.zero);

                s.Badge = UIKit.Img("Badge", s.Root, null, Color.white,
                                    Vector2.one * (seat * SeatFace), Centre, Vector2.zero);
                s.Badge.preserveAspect = true;
                if (!RankArt.Paint(s.Badge, rung.Id)) s.Badge.enabled = false;

                s.Lock = UIKit.Img("Lock", s.Root, Art.S("Ui/" + Skins.Resting), Color.white,
                                   Vector2.one * LockChip, Centre,
                                   new Vector2(seat * .5f - LockChip * .34f, -seat * .5f + LockChip * .34f));
                var shackle = UIKit.Img("Shackle", s.Lock.transform, Art.S("Ui/ic_lock"), LockedInk,
                                        Vector2.one * (LockChip * .58f), Centre, Vector2.zero);
                shackle.preserveAspect = true;

                _seats.Add(s);
            }
        }

        // ------------------------------------------------------------------ the plate
        /// <summary>
        /// The plate's band: as tall as the tallest rung the ladder ships needs, and never so
        /// tall that the stage is pushed under the header on the shortest canvas.
        /// </summary>
        float PlateBand()
        {
            int most = 0;
            foreach (var rung in RankLedger.Ladder.Rungs)
                if (rung.Requirements.Count > most) most = rung.Requirements.Count;

            float wanted = PlateHeight(Mathf.Max(1, most));
            float room = CanvasFit.ShortestCanvas - InsetAllowance
                       - HeaderH - StageLeast - RailH - PlateGap - FootPad;
            return Mathf.Min(wanted, room);
        }

        public static float PlateHeight(int lines) => PlateHead + lines * LineH + PlateFoot;

        void BuildPlateBand()
        {
            _plateBand = UIKit.Box("PlateBand", Safe, new Vector2(PlateW, PlateBand()), Bottom,
                                   new Vector2(0f, FootPad + PlateBand() * .5f));
            _plateBand.gameObject.AddComponent<RectMask2D>();
        }

        /// <summary>
        /// The chosen rung's plate, built fresh: the old one is hidden and let go, the new one
        /// is laid out at its own height from the top of the band and scrolls only if it
        /// overruns it.
        /// </summary>
        void BuildPlate(RankDefinition rung, bool animate)
        {
            if (_plate != null)
            {
                // Hidden before it is destroyed: `Destroy` lands at the end of the frame, and
                // an outgoing plate would draw over its replacement for one (CRAFT.md).
                _plate.gameObject.SetActive(false);
                Destroy(_plate.gameObject);
                _plate = null;
            }
            _lines.Clear();

            if (rung == null) return;

            var lines = rung.Requirements;
            float height = PlateHeight(lines.Count);
            var standing = StandingOf(rung);
            bool live = standing != Standing.Locked;
            var metal = RankLook.Metal(rung);

            var band = UIKit.Node("Plate_" + rung.Id, _plateBand);
            _plate = band;

            var list = UIKit.Node("List", band);
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(.5f, 1f);
            list.sizeDelta = new Vector2(0f, height);
            list.anchoredPosition = Vector2.zero;

            _plateFace = UIKit.Img("Face", list, Art.S("Ui/" + (live ? Skins.PlateNavy : Skins.Panel)),
                                   Color.white, new Vector2(PlateW, height), Top, new Vector2(0f, -height * .5f));
            var face = _plateFace.transform;

            var rim = UIKit.Img("Rim", face, Art.RoundOutline(30, 6f), Pal.A(metal, live ? .55f : .22f));
            UIKit.StretchTo((RectTransform)rim.transform, 0f, 0f, 0f, 0f);

            float headY = height * .5f - PlateHead * .5f;
            float wellW = PlateW - WellInset * 2f;

            _plateTitle = UIKit.Shrinkable(
                UIKit.Titled("Title", face, Loc.Get("ui.ranks.requirements").ToUpperInvariant(), 28,
                             live ? metal : LockedInk, TextAnchor.MiddleLeft,
                             new Vector2(wellW * .6f, 34f), Centre,
                             new Vector2(-wellW * .5f + wellW * .3f, headY), 0f, 2f), 15);

            _plateCount = UIKit.Shrinkable(
                UIKit.Titled("Count", face, string.Empty, 30, Pal.Cream, TextAnchor.MiddleRight,
                             new Vector2(CountW, 34f), Centre,
                             new Vector2(wellW * .5f - CountW * .5f, headY), 0f, 2f), 15);

            float lineY = height * .5f - PlateHead - LineH * .5f;
            foreach (var req in lines)
            {
                var well = UIKit.Img("Well", face, Art.S("Ui/" + Skins.Trough), Color.white,
                                     new Vector2(wellW, WellH), Centre, new Vector2(0f, lineY));

                float wellLeft = -wellW * .5f;
                var line = new Line { Req = req };

                // A ring rather than a star: a star is a currency on this very plate, and a
                // line reading "Earn 60 stars" with a star in front of it says two things
                // with one glyph. A tick replaces the ring when the line is met.
                line.Mark = UIKit.Img("M", well.transform, Art.Ring(64, 9f), Pal.A(metal, .6f),
                                      Vector2.one * MarkSize, Centre,
                                      new Vector2(wellLeft + 22f + MarkSize * .5f, 0f));
                line.Mark.preserveAspect = true;

                float saidX = wellLeft + 22f + MarkSize + 18f;
                float saidW = wellW - (saidX - wellLeft) - CountW - 30f;

                line.Said = UIKit.Shrinkable(
                    UIKit.Titled("L", well.transform, req.Sentence(GameContent.Index), 32, Pal.Cream,
                                 TextAnchor.MiddleLeft, new Vector2(saidW, 38f), Centre,
                                 new Vector2(saidX + saidW * .5f, 13f), 0f, 2f), 18);

                // The bar under the sentence: the line's own fill, so five lines read as five
                // gauges rather than as five fractions to be subtracted in the head.
                var trough = UIKit.Img("Trough", well.transform, Art.Capsule((int)BarH, 48),
                                       new Color(0f, 0f, 0f, .42f), new Vector2(saidW, BarH), Centre,
                                       new Vector2(saidX + saidW * .5f, -21f));
                var fill = UIKit.Img("Fill", trough.transform, Art.Capsule((int)BarH, 48), BarOrange,
                                     new Vector2(0f, BarH), Left, new Vector2(0f, 0f));
                line.Fill = (RectTransform)fill.transform;
                line.Fill.pivot = new Vector2(0f, .5f);
                line.Room = saidW;

                line.Count = UIKit.Shrinkable(
                    UIKit.Titled("C", well.transform, string.Empty, 32, Pal.Gold,
                                 TextAnchor.MiddleRight, new Vector2(CountW, WellH - 10f), Centre,
                                 new Vector2(wellW * .5f - 22f - CountW * .5f, 0f), 0f, 2f), 18);

                _lines.Add(line);
                lineY -= LineH;
            }

            // The band scrolls only if this rung's plate is taller than it — which no shipped
            // rung is, and a retune that makes one so still reads rather than clips.
            if (height > _plateBand.sizeDelta.y + .5f)
            {
                var scroll = band.gameObject.AddComponent<ScrollRect>();
                scroll.content = list;
                scroll.viewport = _plateBand;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Elastic;
                scroll.elasticity = .14f;
                scroll.inertia = true;
                scroll.decelerationRate = .04f;
                scroll.scrollSensitivity = 55f;
                var catcher = band.gameObject.AddComponent<Image>();
                catcher.color = new Color(0f, 0f, 0f, 0f);
                catcher.raycastTarget = true;
            }

            if (animate)
            {
                var group = UIKit.Group(band);
                group.alpha = 0f;
                Tween.Fade(group, 1f, .22f, Ease.OutQuad);
                list.anchoredPosition = new Vector2(0f, -26f);
                Tween.Move(list, Vector2.zero, .30f, Ease.OutCubic);
            }
        }

        // ------------------------------------------------------------------ choosing
        void Step(int by)
        {
            var ladder = RankLedger.Ladder;
            if (ladder.IsEmpty) return;
            int to = Mathf.Clamp(_chosen + by, 1, ladder.Count);
            if (to == _chosen) return;
            Choose(to);
        }

        /// <summary>
        /// A rung chosen: the stage redresses itself and the seat rises. A gesture, so it is
        /// allowed to move — the one place on this page a change is animated rather than
        /// repainted.
        /// </summary>
        void Choose(int ordinal)
        {
            if (ordinal == _chosen || ordinal < 1 || ordinal > RankLedger.Ladder.Count) return;
            _chosen = ordinal;
            Audio.Sfx("tick", .8f);
            Dress(animate: true);
        }

        /// <summary>
        /// The stage, the rail and the plate written for the chosen rung. With
        /// <paramref name="animate"/> the badge is swapped through a scale and the plate slides
        /// in; without it every value lands where it is, which is what a repaint from the ledger
        /// wants.
        /// </summary>
        void Dress(bool animate)
        {
            var ladder = RankLedger.Ladder;
            var rung = ladder.At(_chosen);
            var index = GameContent.Index;

            DressStage(rung, index, animate);
            BuildPlate(rung, animate);
            Repaint();
        }

        void DressStage(RankDefinition rung, CatalogIndex index, bool animate)
        {
            var metal = RankLook.Metal(rung);
            var lift = Pal.Lift(metal, .45f);
            float dur = animate ? .38f : 0f;

            // The light, cross-faded into the rung's metal. Everything on the stage that reads
            // as light takes the colour; the wash under it does not (class remarks).
            Tint(_fanA, Pal.A(metal, FanAlpha), dur);
            Tint(_fanB, Pal.A(lift, Fan2Alpha), dur);
            Tint(_halo, Pal.A(metal, HaloAlpha), dur);
            Tint(_core, Pal.A(Pal.Lift(metal, .5f), CoreAlpha), dur);
            Tint(_aurora[0], Pal.A(metal, AuroraAlpha[0]), dur);
            Tint(_aurora[1], Pal.A(lift, AuroraAlpha[1]), dur);
            Tint(_ring, metal, dur);
            Tint(_spark, Pal.Lift(metal, .55f), dur);
            Tint(_chipRim, metal, dur);
            _ordinal.color = metal;

            if (_flies != null) Flies(_flies, metal);

            _shimmer.Paint(Pal.Lift(metal, .70f), Pal.Lift(metal, .30f), Pal.Lift(metal, .80f));

            if (rung == null)
            {
                // The honest answer to a content file with no ladder in it (`RankLadder` has
                // no built-in one to fall back to).
                _badge.enabled = false;
                _ordinal.text = string.Empty;
                _eyebrow.text = string.Empty;
                _name.text = Loc.Get("ui.ranks.unranked");
                _pill.text = Loc.Get("ui.ranks.unranked_blurb");
                _prev.Interactable = _next.Interactable = false;
                return;
            }

            _ordinal.text = Loc.Format("ui.ranks.ordinal", rung.Ordinal);
            _name.text = rung.Name;

            var badgeRt = (RectTransform)_badge.transform;
            if (animate)
            {
                // Out, swap, in: the badge leaves through a short shrink and arrives with an
                // overshoot, and the words follow it a beat later so the swap reads as one
                // thing turning rather than five things changing.
                Tween.KillChannel(badgeRt, "breathe");
                Tween.Scale(badgeRt, .55f, .12f, Ease.InCubic).OnDone(() =>
                {
                    if (!_badge) return;
                    RankArt.Paint(_badge, rung.Id);
                    Tween.Scale(badgeRt, 1f, .34f, Ease.OutBack)
                         .OnDone(() => { if (badgeRt) Tween.Breathe(badgeRt, .022f, 3.4f); });
                    Ripple(metal);
                });

                foreach (var t in new[] { _eyebrow, _name })
                {
                    if (!t) continue;
                    var c = t.color; c.a = 0f; t.color = c;
                    Tween.Fade(t, 1f, .26f, Ease.OutQuad).Delay(.10f);
                }
            }
            else
            {
                RankArt.Paint(_badge, rung.Id);
                if (badgeRt.localScale.sqrMagnitude < 1e-6f) badgeRt.localScale = Vector3.one;
                Tween.Breathe(badgeRt, .022f, 3.4f);
            }

            _prev.Interactable = rung.Ordinal > 1;
            _next.Interactable = rung.Ordinal < RankLedger.Ladder.Count;
        }

        /// <summary>A ring of light leaving the badge as a new one lands.</summary>
        void Ripple(Color metal)
        {
            var wave = UIKit.Img("Wave", _cluster, Art.Ring(256, 10f), Pal.A(Pal.Lift(metal, .4f), .8f),
                                 Vector2.one * (BadgeSize * .8f), Top, new Vector2(0f, -RingTop));
            var rt = (RectTransform)wave.transform;
            Tween.Run(.62f, Ease.OutQuint, t =>
            {
                if (!rt) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.8f, 2.1f, t);
                wave.color = Pal.A(Pal.Lift(metal, .4f), .8f * (1f - t));
            }, wave).OnDone(() => { if (wave) Destroy(wave.gameObject); });
        }

        static void Tint(Graphic g, Color to, float dur)
        {
            if (!g) return;
            if (dur <= 0f) { g.color = to; return; }
            Tween.Tint(g, to, dur, Ease.OutQuad);
        }

        /// <summary>The fireflies recoloured in place: their alpha is theirs to breathe, so only the hue is written.</summary>
        static void Flies(Fireflies flies, Color metal)
        {
            var tint = Pal.Lift(metal, .35f);
            foreach (var img in flies.GetComponentsInChildren<Image>())
            {
                var c = img.color;
                img.color = new Color(tint.r, tint.g, tint.b, c.a);
            }
        }

        // ------------------------------------------------------------------ the repaint
        /// <summary>
        /// Writes the ledger's state onto what already stands, replaying no entrance. Which
        /// rung is chosen does not change here; what it is worth might.
        /// </summary>
        void Repaint()
        {
            var ladder = RankLedger.Ladder;
            var index = GameContent.Index;
            var rung = ladder.At(_chosen);

            PaintStage(rung, index);
            PaintRail(ladder);
            PaintPlate(rung, index);
        }

        void PaintStage(RankDefinition rung, CatalogIndex index)
        {
            if (rung == null) return;

            var standing = StandingOf(rung);
            var metal = RankLook.Metal(rung);
            bool live = standing != Standing.Locked;

            _eyebrow.text = EyebrowOf(standing).ToUpperInvariant();
            var ink = live ? Pal.Lift(metal, .25f) : LockedInk;
            _eyebrow.color = Pal.A(ink, _eyebrow.color.a);

            _pill.text = PillOf(rung, standing, index);

            float share = Share(rung, index);
            Tween.KillChannel(_ring, "fill");
            float from = _ring.fillAmount;
            Tween.Run(.8f, Ease.OutCubic, t =>
            {
                if (!_ring) return;
                float f = Mathf.Lerp(from, share, t);
                _ring.fillAmount = f;
                PlaceSpark(f);
            }, _ring, "fill");

            // A seam at the foot when the ring is full is a seam the chip covers; when the
            // ring is empty the spark still stands at the foot, which reads as the start.
            _spark.enabled = share > .004f;
        }

        /// <summary>The spark at the head of the fill — clockwise from the bottom, as the fill runs.</summary>
        void PlaceSpark(float fill)
        {
            float a = fill * Mathf.PI * 2f;
            float r = RingSize * .5f - RingThick * (RingSize / 256f) * .5f;
            ((RectTransform)_spark.transform).anchoredPosition =
                new Vector2(-Mathf.Sin(a) * r, -RingTop - Mathf.Cos(a) * r);
        }

        static string EyebrowOf(Standing standing)
        {
            switch (standing)
            {
                case Standing.Held: return Loc.Get("ui.ranks.mark");
                case Standing.Earned: return Loc.Get("ui.ranks.earned");
                case Standing.Next: return Loc.Get("ui.ranks.next");
                case Standing.Locked: return Loc.Get("ui.ranks.locked");
            }
            return string.Empty;
        }

        /// <summary>The one sentence under the name, per standing (invariant 48g: one answer, and the first of them a word).</summary>
        static string PillOf(RankDefinition rung, Standing standing, CatalogIndex index)
        {
            var ladder = RankLedger.Ladder;
            switch (standing)
            {
                case Standing.Held:
                    return rung.Ordinal >= ladder.Count
                        ? Loc.Get("ui.ranks.top")
                        : Loc.Format("ui.ranks.held_count", RankLedger.Ordinal, ladder.Count);
                case Standing.Earned:
                    return Loc.Format("ui.ranks.held_count", RankLedger.Ordinal, ladder.Count);
                case Standing.Next:
                    return Loc.Format("ui.ranks.progress",
                                      Mathf.RoundToInt(Mathf.Clamp01(Share(rung, index)) * 100f));
                case Standing.Locked:
                    var below = ladder.At(rung.Ordinal - 1);
                    return below != null ? Loc.Format("ui.ranks.locked_hint", below.Name) : string.Empty;
            }
            return string.Empty;
        }

        void PaintRail(RankLadder ladder)
        {
            int held = RankLedger.Ordinal;

            foreach (var s in _seats)
            {
                var standing = StandingOf(s.Rung);
                var metal = RankLook.Metal(s.Rung);
                bool earned = standing == Standing.Held || standing == Standing.Earned;
                bool next = standing == Standing.Next;
                bool chosen = s.Rung.Ordinal == _chosen;

                float face = earned || next ? SeatFace : SeatFaceLocked;
                s.Badge.rectTransform.sizeDelta = Vector2.one * (s.Root.sizeDelta.x * face);

                s.Rim.color = Pal.A(metal, earned ? .95f : next ? .75f : .30f);
                s.Glow.color = Pal.A(metal, earned ? .34f : next ? .18f : 0f);
                s.Lock.gameObject.SetActive(!earned && !next);

                if (s.Link != null)
                    s.Link.color = s.Rung.Ordinal < held ? Pal.A(metal, .85f) : Pal.A(Pal.Cream, .14f);

                // The chosen seat stands up. Tweened rather than set, because a tap is a
                // gesture; a repaint from the ledger lands on the same value and moves nothing.
                float scale = chosen ? SeatChosen : 1f;
                if (Mathf.Abs(s.Root.localScale.x - scale) > .001f)
                    Tween.Scale(s.Root, scale, .28f, Ease.OutBack);

                if (s.Pulsing != next) { s.Pulsing = next; Pulse(s, next, metal); }
            }
        }

        /// <summary>The breath on the seat being climbed: the one thing on the rail that moves by itself.</summary>
        static void Pulse(Seat s, bool on, Color metal)
        {
            Tween.KillChannel(s.Pulse, "pulse");
            if (!on) { s.Pulse.color = Pal.A(metal, 0f); return; }

            var rt = (RectTransform)s.Pulse.transform;
            Tween.Run(1.6f, Ease.OutQuad, t =>
            {
                if (!rt) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.96f, 1.42f, t);
                s.Pulse.color = Pal.A(metal, .85f * (1f - t));
            }, s.Pulse, "pulse").Loop(-1, false);
        }

        void PaintPlate(RankDefinition rung, CatalogIndex index)
        {
            if (rung == null || _plateCount == null) return;

            var standing = StandingOf(rung);
            bool live = standing != Standing.Locked;
            var metal = RankLook.Metal(rung);
            int met = 0;

            foreach (var line in _lines)
            {
                long have = line.Req.Held(index);
                bool done = have >= line.Req.Target;
                if (done) met++;

                line.Mark.sprite = done ? Art.S("Ui/ic_check") : Art.Ring(64, 9f);
                line.Mark.color = done ? Pal.Mint : Pal.A(metal, live ? .70f : .45f);

                line.Said.color = live ? Pal.Cream : LockedInk;

                // A met line prints its own target rather than the figure that passed it:
                // "250 / 250" is what finishing looks like, and a lifetime tally that went on
                // climbing afterwards would read as a bar that overflowed.
                long shown = have > line.Req.Target ? line.Req.Target : have;
                line.Count.text = Loc.Format("ui.ranks.fraction", Compact.Number(shown),
                                             Compact.Number(line.Req.Target));
                line.Count.color = done ? Pal.Mint : live ? Pal.Gold : LockedInk;

                float share = line.Req.Target <= 0 ? 1f : Mathf.Clamp01(have / (float)line.Req.Target);
                line.Fill.sizeDelta = new Vector2(line.Room * share, BarH);
                line.Fill.GetComponent<Image>().color = done ? Pal.Mint : BarOrange;
            }

            _plateCount.text = Loc.Format("ui.ranks.fraction", met, _lines.Count);
            _plateCount.color = met >= _lines.Count && _lines.Count > 0 ? Pal.Mint : Pal.Cream;
        }
    }
}
