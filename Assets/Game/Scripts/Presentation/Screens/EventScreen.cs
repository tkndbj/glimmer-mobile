using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Events;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The event, as a thing that grows.
    ///
    /// <para>
    /// <b>Why a page and not the panel it replaces.</b> <c>EventOverlay</c> reported: a rail,
    /// three discs, a countdown, and every reward on it already paid. That is the shape a
    /// feature takes when nothing on it can be done — and it was accurate, because until
    /// save schema v11 an event milestone landed in the balance the instant the glade was
    /// cleared. The streak learned the same lesson at v10 and the argument is identical: a
    /// reward that arrives as a number moving behind a defeat screen is not a reward, it is
    /// an accounting entry. So the rungs wait here now, and the page has something to be.
    /// </para>
    /// <para>
    /// <b>Why a vine.</b> Every other progress surface in this game is a row of tiles — the
    /// map, the streak board, the chest panel — and a fourth would have been the same screen
    /// in a different colour. A vine is a different claim: the rungs are not slots that fill
    /// but a single continuous thing that is longer than it was yesterday, which is what an
    /// event actually is. It also solves the layout problem that a tile grid could not. The
    /// number of milestones is content — an author may write anything up to
    /// <see cref="EventRules.MaxMilestones"/> — so nothing here can be positioned by hand;
    /// a curve sampled at each rung's own goal takes any count and puts every rung at a
    /// distance that means something, because the axis is <em>glades</em> rather than rung
    /// index. Two rungs an author placed close together are drawn close together.
    /// </para>
    /// <para>
    /// <b>The three states are three flowers, not three colours.</b> A rung ahead is a tight
    /// bud on a dry stem; one reached is a half-open bloom under a turning fan of light; one
    /// taken is a full flower with a seal on it. <see cref="Art.Bloom"/> quantises its
    /// openness to eighths precisely so this can be tweened, which is what makes collecting
    /// look like the thing it is called.
    /// </para>
    /// <para>
    /// The backdrop is the grove at first light. Not the hub's islands re-graded — that trick
    /// is already spent on the streak page, and a third variation would have read as a set of
    /// filters rather than as a place. This is the ground those islands float above, seen
    /// from down in the planting.
    /// </para>
    /// </summary>
    public sealed class EventScreen : View
    {
        public override string Track => "mus_menu";

        // ------------------------------------------------------------- geometry
        const float BandTop = 470f;          // below the header block
        const float FooterTop = 366f;        // above the nav bar: rail, state line, call to action
        const float RungGap = 250f;          // vertical room the closest pair of rungs wants
        const float VineHead = 240f;         // clearance above the crown flower
        const float VineFoot = 200f;         // clearance below the root rosette
        const float PlateW = 196f;           // a rung's reward plate
        const float Sway = 132f;             // how far the vine wanders off centre
        const float Waves = 2.35f;           // how many bends over its whole length

        const float FocusPad = 200f;         // air left below the deepest live rung at rest
        const int Segments = 88;             // stem quads; one sprite, so they batch
        const float BaseT = .10f;            // where the stem leaves the ground
        const float TopT = .96f;             // where the last rung can sit

        static readonly Color Dry = new Color(.50f, .53f, .43f, .95f);
        static readonly Color DryEdge = new Color(.38f, .42f, .33f, .90f);
        static readonly Color Stem = new Color(.36f, .74f, .40f);
        static readonly Color StemLit = new Color(.62f, .92f, .52f);
        static readonly Color Ahead = new Color(.44f, .62f, .42f, .98f);
        static readonly Color AheadHeart = new Color(.32f, .50f, .32f, .98f);

        // ------------------------------------------------------------- snapshot
        GroveEvent _event;
        EventProgress _progress;
        int _goal;
        bool _closed;

        readonly List<Rung> _rungs = new List<Rung>();
        readonly List<Image> _grown = new List<Image>();

        RectTransform _vine;
        RectTransform _purse;
        Text _purseNumber;
        Text _clock;
        Text _state;
        RectTransform _tip;
        float _vineH;
        long _gathered;
        bool _collecting;

        /// <summary>Set when there was nothing to draw, and acted on in OnPresented.</summary>
        bool _retreat;

        /// <summary>One milestone's flower, and the handles its ceremony needs.</summary>
        sealed class Rung
        {
            public EventMilestone Milestone;
            public RectTransform Root;
            public Image Petals;
            public Image Heart;
            public RectTransform Aura;
            public Btn Tap;
            public Text Reward;
            public Image PlateEdge;
            public Text Prompt;
            public bool Taken;
        }

        // ------------------------------------------------------------- building
        protected override void Build()
        {
            _event = GroveEvents.Featured;

            // Reachable only from a box the hub draws when there is one, but a window can
            // close between the tap and the frame that builds this — and a closed event with
            // nothing waiting stops being featured. Going home beats an empty page.
            //
            // The retreat is *deferred*, and that is not tidiness. `Flow.Go` opens with
            // `if (Busy) return;`, and `Busy` is true for the whole of the swap that calls
            // `Init` — which calls this. Navigating from here is therefore a guaranteed
            // no-op: the empty page stays up, becomes `Flow.Current`, and goes on ticking
            // `Update` for the life of the process. `OnPresented` is the seam that exists
            // for this, because it fires once the iris has finished and `Busy` has cleared.
            if (_event == null) { _retreat = true; return; }

            _retreat = false;
            Snapshot();

            Scenery.Layered(Content, "event", .12f);
            Petals(Content, 18);

            BuildHeader();
            BuildPurse();
            BuildVine();
            BuildFooter();

            NavBar.Build(Content, NavBar.Tab.Home);
        }

        void Snapshot()
        {
            _progress = GroveEvents.ProgressOf(_event);
            _goal = Mathf.Max(1, _event.FinalGoal);
            _closed = _event.HasEndedAt(GameClock.NowUnix());
            _gathered = _progress.Credits;
            _rungs.Clear();
            _grown.Clear();
        }

        void OnEnable() { EventCollection.Changed += OnChanged; }
        void OnDisable() { EventCollection.Changed -= OnChanged; }

        void OnChanged()
        {
            // The ceremony raises this itself, and it is holding handles into the tree it
            // would tear down. It refreshes what actually moved when it finishes.
            if (_collecting) return;
            Rebuild();
        }

        public override void OnPresented()
        {
            if (_retreat) Flow.Go<HomeScreen>();
        }

        void Update()
        {
            // `_event` and not only `_clock`: they are two different conditions, and the
            // gap between them is what turned a page with nothing to show into a
            // NullReferenceException every frame.
            if (_collecting || _event == null || _clock == null) return;
            _clock.text = ClockLine();
        }

        /// <summary>
        /// Hides before destroying, because <c>Destroy</c> is deferred to the end of the
        /// frame and the rebuilt page would otherwise draw over the old one for a frame.
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

            _vine = null; _purse = null; _purseNumber = null;
            _clock = null; _state = null; _tip = null;

            Build();

            // The same retreat, taken here and now rather than deferred. A rebuild is never
            // inside a swap — it comes from a collect finishing or a sync landing — so `Busy`
            // is clear and `Flow.Go` will actually go. Without it, a sync that adopts a save
            // finishing the last event would leave this page empty with no way off it but
            // the back button.
            if (_retreat) Flow.Go<HomeScreen>();
        }

        // --------------------------------------------------------------- header
        void BuildHeader()
        {
            UIKit.IconButton("Back", Content, Skins.Nav, "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, 1f), new Vector2(96f, -132f), () => Flow.Go<HomeScreen>());

            UIKit.IconButton("Info", Content, Skins.Aside, "ic_info", new Vector2(118f, 118f),
                             new Vector2(1f, 1f), new Vector2(-96f, -132f), () =>
                             {
                                 if (Flow.HasModal) return;
                                 Flow.Modal<EventInfoOverlay>(v => v.For(_event));
                             });

            UIKit.Titled("Kind", Content, Loc.Get("ui.event.title").ToUpperInvariant(), 30,
                         Pal.A(Pal.Cream, .78f), TextAnchor.MiddleCenter,
                         new Vector2(600f, 40f), new Vector2(.5f, 1f), new Vector2(0f, -118f), 3f, 3f);

            UIKit.Shrinkable(
                UIKit.Titled("Name", Content, Loc.Get(_event.NameKey), 56, Pal.Bloom,
                             TextAnchor.MiddleCenter, new Vector2(640f, 74f), new Vector2(.5f, 1f),
                             new Vector2(0f, -178f), 4f, 5f), 34);

            _clock = Scenery.Pill(Content, ClockLine(), 26, new Vector2(520f, 66f),
                                  new Vector2(.5f, 1f), new Vector2(0f, -246f),
                                  _closed ? new Color(.10f, .09f, .14f, .74f)
                                          : new Color(.16f, .07f, .13f, .74f),
                                  // No glyph while it is running. There is no clock in the
                                  // set, and the nearest thing to one is `ic_hint`, which is
                                  // a question mark — a deadline is the last thing that
                                  // should be wearing one.
                                  _closed ? "ic_lock" : null);
            UIKit.Shrinkable(_clock, 18);
        }

        string ClockLine()
        {
            if (_event == null) return string.Empty;

            long left = _event.SecondsLeftAt(GameClock.NowUnix());

            if (left > 0) return Loc.Format("ui.event.ends_in", Profile.LongCountdown(left));

            // A closed window stops progress and never stops a reward. Saying only "this one
            // has closed" over a track still holding a flower would read as a loss.
            return _progress.AnyWaiting
                ? Loc.Get("ui.event.closed_waiting")
                : Loc.Get("ui.event.ended");
        }

        // ---------------------------------------------------------------- purse
        /// <summary>
        /// The two numbers that answer the page, side by side above the vine.
        ///
        /// <para>
        /// Glades on the left because that is what the player controls, credits on the right
        /// because that is what it buys. Both live in fixed chrome rather than on the vine,
        /// and the count in particular used to sit at the roots — which put the one figure
        /// summarising the whole page inside a scroll, where a track long enough to need one
        /// simply carried it off the screen.
        /// </para>
        /// <para>
        /// The credits are read from <see cref="EventProgress.Credits"/> rather than counted
        /// here, so the figure is the one the derivation pays and cannot drift from it. It is
        /// also the target every collected reward is thrown at.
        /// </para>
        /// </summary>
        void BuildPurse()
        {
            const float CellW = 348f;
            const float CellH = 132f;
            float x = (CellW + 20f) * .5f;

            BuildGlades(new Vector2(-x, -372f), CellW, CellH);

            _purse = Cell("Purse", new Vector2(x, -372f), CellW, CellH, Pal.Gold);
            UIKit.Halo(_purse, Pal.Gold, 400f, .16f);

            var coin = UIKit.Img("Coin", _purse, null, Color.white, new Vector2(84f, 84f),
                                 new Vector2(0f, .5f), new Vector2(66f, 4f));
            coin.preserveAspect = true;
            RewardArt.Glyph(coin, ChestDropKind.Credits, 10f);

            // A left-anchored label draws from the *left edge of its box*, not from the point
            // it is placed at, so the box has to be pushed out by half its own width or the
            // number lands back under the coin. It did.
            float textX = 126f + (CellW - 138f) * .5f;
            var textBox = new Vector2(CellW - 138f, 58f);

            _purseNumber = UIKit.Titled("Number", _purse, Compact.Number(_gathered), 48, Pal.Gold,
                                        TextAnchor.MiddleLeft, textBox,
                                        new Vector2(0f, .5f), new Vector2(textX, 18f), 4f, 4f);
            UIKit.Shrinkable(_purseNumber, 26);

            UIKit.Titled("Caption", _purse, Loc.Get("ui.event.gathered"), 21,
                         Pal.A(Pal.Cream, .70f), TextAnchor.MiddleLeft,
                         new Vector2(textBox.x, 30f), new Vector2(0f, .5f),
                         new Vector2(textX, -30f), 0f, 3f);

            _purse.localScale = Vector3.zero;
            Tween.Pop(_purse, 0f, .5f, .16f);
        }

        void BuildGlades(Vector2 pos, float w, float h)
        {
            var cell = Cell("Glades", pos, w, h, Pal.Bloom);

            var mark = UIKit.Box("Mark", cell, new Vector2(84f, 84f),
                                 new Vector2(0f, .5f), new Vector2(66f, 4f));

            // The event own mark, opening with the track — the same picture the hub box
            // wears, so the two screens are visibly about the same thing.
            EventMark.Paint(mark, _event.Icon, Pal.Bloom,
                            Mathf.Clamp01(_progress.Finished / (float)_goal));

            float textX = 126f + (w - 138f) * .5f;
            var textBox = new Vector2(w - 138f, 58f);

            UIKit.Shrinkable(
                UIKit.Titled("Number", cell, Loc.Format("ui.home.fraction", _progress.Finished, _goal),
                             48, Pal.Cream, TextAnchor.MiddleLeft, textBox,
                             new Vector2(0f, .5f), new Vector2(textX, 18f), 4f, 4f), 26);

            UIKit.Titled("Caption", cell, Loc.Get("ui.home.glades"), 21,
                         Pal.A(Pal.Cream, .70f), TextAnchor.MiddleLeft,
                         new Vector2(textBox.x, 30f), new Vector2(0f, .5f),
                         new Vector2(textX, -30f), 0f, 3f);

            cell.localScale = Vector3.zero;
            Tween.Pop(cell, 0f, .5f, .10f);
        }

        RectTransform Cell(string name, Vector2 pos, float w, float h, Color rim)
        {
            var cell = UIKit.Box(name, Content, new Vector2(w, h), new Vector2(.5f, 1f), pos);

            var plate = UIKit.Img("Plate", cell, Art.Round(30), new Color(.05f, .11f, .10f, .80f));
            UIKit.StretchTo((RectTransform)plate.transform, 0, 0, 0, 0);

            var edge = UIKit.Img("Edge", cell, Art.RoundOutline(30, 3f), Pal.A(rim, .34f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            return cell;
        }

        // ----------------------------------------------------------------- vine
        /// <summary>
        /// The curve every part of this page is hung on.
        ///
        /// <paramref name="t"/> runs 0 at the soil to 1 at the crown. Both coordinates are a
        /// pure function of it, which is the whole reason the layout survives an author
        /// writing eight milestones instead of three: nothing is positioned, everything is
        /// sampled.
        /// </summary>
        Vector2 Point(float t)
        {
            float y = Mathf.Lerp(-_vineH + VineFoot, -VineHead, t);
            float x = Sway * Mathf.Sin(t * Mathf.PI * Waves);
            return new Vector2(x, y);
        }

        /// <summary>
        /// How tall the vine has to be for its closest pair of rungs to be readable.
        ///
        /// Measured off the <em>smallest gap between consecutive goals</em>, not off the rung
        /// count, and that is the whole reason it is computed rather than chosen. Because the
        /// axis is glades, a track paying at one, two and four puts its first two rungs a
        /// quarter of the vine apart while the last sits half a vine above them — so a height
        /// picked from "three milestones" would crowd two of them into the same flower. The
        /// page scrolls when the answer is taller than the band, which is the right trade:
        /// the alternative is a page that fits and cannot be read.
        /// </summary>
        float VineHeight(float visible)
        {
            int closest = _goal;
            int previous = 0;

            for (int i = 0; i < _event.Milestones.Count; i++)
            {
                int goal = _event.Milestones[i].Goal;
                int step = goal - previous;
                if (step > 0 && step < closest) closest = step;
                previous = goal;
            }

            if (closest < 1) closest = 1;

            float span = RungGap * _goal / (closest * (TopT - BaseT));
            return Mathf.Max(visible, span + VineHead + VineFoot);
        }

        /// <summary>Where a goal of <paramref name="glades"/> sits along the vine.</summary>
        float At(float glades)
            => Mathf.Lerp(BaseT, TopT, Mathf.Clamp01(glades / _goal));

        /// <summary>How far below the vine's own top a point sits. What a scroll offset is in.</summary>
        float Depth(float t) => -Point(t).y;

        void BuildVine()
        {
            var band = UIKit.Node("Band", Content);
            UIKit.StretchTo(band, 0f, NavBar.Height + FooterTop, 0f, BandTop);
            band.gameObject.AddComponent<RectMask2D>();

            float visible = Mathf.Max(320f, Flow.Size.y - BandTop - (NavBar.Height + FooterTop));
            _vineH = VineHeight(visible);

            _vine = UIKit.Node("Vine", band);
            _vine.anchorMin = new Vector2(0f, 1f);
            _vine.anchorMax = new Vector2(1f, 1f);
            _vine.pivot = new Vector2(.5f, 1f);
            _vine.sizeDelta = new Vector2(0f, _vineH);
            _vine.anchoredPosition = Vector2.zero;

            BuildStem();
            BuildRoot();

            for (int i = 0; i < _event.Milestones.Count; i++) BuildRung(i);

            BuildTip();

            if (_vineH <= visible + 1f) return;

            // Every UIKit image is raycast-off, so a scroll region without an explicit
            // catcher cannot be dragged at all.
            var catcher = band.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            var scroll = band.gameObject.AddComponent<ScrollRect>();
            scroll.content = _vine;
            scroll.viewport = band;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = .14f;
            scroll.inertia = true;
            scroll.decelerationRate = .04f;
            scroll.scrollSensitivity = 55f;

            // Where it opens is decided by RestingScroll, which is worth reading.
            _vine.anchoredPosition = new Vector2(0f, Mathf.Clamp(RestingScroll(visible), 0f,
                                                                 _vineH - visible));
        }

        /// <summary>
        /// Where the vine rests when the page opens.
        ///
        /// <para>
        /// Anchored on the <em>lowest rung the player has reached</em> — or the growth tip
        /// when they have reached none — and then scrolled no further than it takes to bring
        /// that just inside the bottom of the band. Everything above follows for free, because
        /// a track is drawn in ascending order, so this is the smallest window containing
        /// every rung that has happened and as much of what is ahead as will fit. On the
        /// shipped track that is all three at once.
        /// </para>
        /// <para>
        /// Three rules were tried and two of them fail. Opening at the top hides the bloom the
        /// player came to collect. Centring on the tip parks mid-vine, with the crown crest
        /// cut off above and the first rung cut off below. Anchoring on the deepest rung still
        /// <em>waiting</em> is nearly right and slices the one just collected in half, which is
        /// a strange thing to do to the reward somebody has this second taken. Reached rather
        /// than waiting is the version that holds, and it degrades the right way: a player who
        /// has finished nothing opens at the roots, looking up the vine.
        /// </para>
        /// </summary>
        float RestingScroll(float visible)
        {
            float focus = At(_progress.Finished);

            for (int i = 0; i < _event.Milestones.Count; i++)
            {
                int goal = _event.Milestones[i].Goal;
                if (_progress.Finished < goal) continue;

                // Deeper down the vine is a smaller t, so the lowest rung is the smallest.
                focus = Mathf.Min(focus, At(goal));
            }

            return Depth(focus) + FocusPad - visible;
        }

        /// <summary>
        /// The stem, drawn twice: the whole length dry, then the grown part over it.
        ///
        /// Both, and not just the part that exists yet. A vine that simply stopped at the
        /// player's progress would hide the shape of what is left — and the distance to the
        /// next flower is the single most persuasive thing this page can show. The dry run
        /// ahead is the plan; the green behind is the receipt.
        /// </summary>
        void BuildStem()
        {
            float grown = At(_progress.Finished);
            var cap = Art.Capsule(24, 96);

            for (int pass = 0; pass < 2; pass++)
            {
                bool live = pass == 1;

                for (int i = 0; i < Segments; i++)
                {
                    float t0 = i / (float)Segments;
                    float t1 = (i + 1) / (float)Segments;
                    float mid = (t0 + t1) * .5f;

                    if (live && mid > grown) break;

                    var a = Point(t0);
                    var b = Point(t1);
                    var d = b - a;

                    float len = d.magnitude + 7f;
                    float thick = Mathf.Lerp(live ? 28f : 21f, live ? 14f : 13f, mid);

                    var seg = UIKit.Img(live ? "G" : "D", _vine, cap,
                                        live ? Stem : Dry,
                                        new Vector2(thick, len), new Vector2(.5f, 1f),
                                        (a + b) * .5f);
                    seg.transform.localRotation =
                        Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f);

                    if (live) _grown.Add(seg);
                }
            }

            // Leaves: on the dry run they are shrivelled and small, on the grown one they are
            // open. Alternating sides so the vine reads as a plant rather than as a cable.
            for (int i = 1; i < 13; i++)
            {
                float t = BaseT + (TopT - BaseT) * (i / 13f);
                bool live = t <= grown;
                float side = i % 2 == 0 ? 1f : -1f;
                float size = Mathf.Lerp(live ? 86f : 62f, live ? 58f : 42f, t);

                var at = Point(t);
                var leaf = UIKit.Img("L", _vine, Art.Leaf(96), live ? Stem : DryEdge,
                                     new Vector2(size * .58f, size), new Vector2(.5f, 1f),
                                     at + new Vector2(side * size * .30f, 0f));
                leaf.transform.localRotation = Quaternion.Euler(0f, 0f, side * -62f);

                if (!live) continue;

                leaf.transform.localScale = Vector3.zero;
                Tween.Pop(leaf.transform, 0f, .42f, .34f + t * .55f)
                     .OnDone(() => { if (leaf) Tween.Bob((RectTransform)leaf.transform, 3.5f, 3.1f + t); });
            }

            // The grown length draws itself on, from the soil upward. It costs nothing —
            // every segment already exists — and it is the difference between arriving at a
            // picture of a plant and watching one.
            int count = _grown.Count;
            for (int i = 0; i < count; i++) _grown[i].enabled = false;

            Tween.Run(.85f, Ease.OutCubic, k =>
            {
                int show = Mathf.CeilToInt(k * count);
                for (int i = 0; i < count; i++)
                {
                    if (_grown[i] == null) continue;
                    _grown[i].enabled = i < show;
                }
            }, this).Delay(.18f);
        }

        /// <summary>The rosette the vine stands in. Decoration, and the only thing here that is.</summary>
        void BuildRoot()
        {
            var at = Point(0f);

            UIKit.Img("Shadow", _vine, Art.Glow(128, 2.2f), new Color(.05f, .12f, .08f, .55f),
                      new Vector2(300f, 120f), new Vector2(.5f, 1f), at + new Vector2(0f, -6f));

            for (int i = 0; i < 5; i++)
            {
                float a = -18f + i * 9f;
                float size = 78f + (i == 2 ? 22f : 0f);
                var leaf = UIKit.Img("R", _vine, Art.Leaf(96), i % 2 == 0 ? Stem : StemLit,
                                     new Vector2(size * .60f, size), new Vector2(.5f, 1f),
                                     at + new Vector2((i - 2) * 44f, 10f));
                leaf.transform.localRotation = Quaternion.Euler(0f, 0f, a * 3.4f);
                leaf.transform.localScale = Vector3.zero;
                Tween.Pop(leaf.transform, 0f, .46f, .10f + i * .04f);
            }

        }

        /// <summary>
        /// The bright node where growth has reached, breathing.
        ///
        /// Only while there is somewhere left to grow: on a finished track it would mark the
        /// crown as unfinished business, which is the opposite of what the page should say
        /// to the one player who has done everything it asked.
        /// </summary>
        void BuildTip()
        {
            if (_progress.Finished >= _goal) return;

            float t = At(_progress.Finished);
            _tip = UIKit.Box("Tip", _vine, new Vector2(64f, 64f), new Vector2(.5f, 1f), Point(t));

            UIKit.Halo(_tip, Pal.Radiance, 200f, .40f);
            UIKit.Img("Core", _tip, Art.Disc(64), Pal.A(Pal.Radiance, .92f),
                      new Vector2(30f, 30f), new Vector2(.5f, .5f), Vector2.zero);

            Tween.Breathe(_tip, .16f, 1.9f);

            // Pollen, forever rather than for a fixed number of puffs: this is the only
            // thing on the page that moves while the player does nothing, and it is what
            // says the vine is still growing. Owner-scoped to the tip, so the chain stops
            // the moment the tip is destroyed.
            Tween.After(.9f, Pollen, _tip);
        }

        void Pollen()
        {
            if (_tip == null) return;
            Burst.Sparks(_tip, Vector2.zero, Pal.Radiance, 5, 90f, 15f, .9f);
            Tween.After(2.4f, Pollen, _tip);
        }

        // ---------------------------------------------------------------- rungs
        void BuildRung(int index)
        {
            var milestone = _event.Milestones[index];

            bool taken = GroveEvents.IsCollected(_event, milestone);
            bool ready = !taken && _progress.Finished >= milestone.Goal;
            bool last = index == _event.Milestones.Count - 1;

            var rung = new Rung { Milestone = milestone, Taken = taken };
            var at = Point(At(milestone.Goal));

            var host = UIKit.Box("M" + index, _vine, new Vector2(260f, 260f),
                                 new Vector2(.5f, 1f), at);
            rung.Root = host;

            // The seat goes behind everything, so a flower on a pale stretch of sky still
            // reads. Same job the streak tile's glow does.
            var seat = UIKit.Img("Seat", host, Art.Glow(128, 2.1f),
                                 new Color(.04f, .10f, .09f, taken ? .46f : .60f),
                                 new Vector2(250f, 250f), new Vector2(.5f, .5f), Vector2.zero);
            seat.transform.SetAsFirstSibling();

            if (ready) rung.Aura = Aura(host);
            else if (taken) UIKit.Halo(host, Pal.Bloom, 220f, .22f);

            float size = taken ? 168f : ready ? 158f : 116f;
            float open = taken ? 1f : ready ? .375f : 0f;

            // Sepals, on the closed bud only. Without them a fully shut Art.Bloom is a disc
            // with a faint scallop on it, and the state that most needs to say "not yet" was
            // reading as a grey pebble — which is the vocabulary of something broken rather
            // than of something growing. Two leaves and a green tint fix it: the difference
            // between the three states is then bud, half-open, flower, which is the claim the
            // page is making.
            if (!taken && !ready)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    var sepal = UIKit.Img("Sepal", host, Art.Leaf(96), Stem,
                                          new Vector2(size * .30f, size * .52f),
                                          new Vector2(.5f, .5f),
                                          new Vector2(side * size * .28f, -size * .20f));
                    sepal.transform.localRotation = Quaternion.Euler(0f, 0f, side * -52f);
                }
            }

            rung.Petals = UIKit.Img("Petals", host, Art.Bloom(160, 6, open),
                                    taken ? Pal.Bloom : ready ? Pal.Rose : Ahead,
                                    Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);

            rung.Heart = UIKit.Img("Heart", host, Art.Bloom(96, 5, Mathf.Max(.25f, open * .55f)),
                                   taken ? Pal.Radiance : ready ? Pal.Sun : AheadHeart,
                                   Vector2.one * size * .46f, new Vector2(.5f, .5f), Vector2.zero);

            // The crown: the rung that finishes the track wears the same crest the streak's
            // last night does, so "this one ends it" means one thing across the game.
            if (last)
            {
                var crest = UIKit.Img("Crest", host, Art.S("Ui/crest_gold"), Color.white,
                                      new Vector2(74f, 82f), new Vector2(.5f, .5f),
                                      new Vector2(0f, size * .66f));
                crest.preserveAspect = true;
                crest.color = taken || ready ? Color.white : new Color(1f, 1f, 1f, .55f);
            }

            BuildRungLabel(rung, at, taken, ready, milestone);

            if (taken) Seal(host, false);

            if (ready)
            {
                rung.Tap = UIKit.Button("Tap", host, null, Vector2.one * 230f,
                                        new Vector2(.5f, .5f), Vector2.zero, () => Take(rung));
                rung.Tap.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
                rung.Tap.ClickSfx = null;
                rung.Tap.PressScale = .94f;
            }

            _rungs.Add(rung);

            host.localScale = Vector3.zero;
            Tween.Pop(host, 0f, .5f, .30f + index * .09f).OnDone(() =>
            {
                if (!host) return;
                // A collected bloom gets no sheen. It reads as a highlight sweeping across a
                // flat surface, which is the vocabulary of a button or a card — and the one
                // thing this rung is not any more is something to press. The seal says taken;
                // a shine on top of it only invites another tap.
                if (ready) Tween.Breathe(host, .045f, 1.7f);
            });
        }

        /// <summary>
        /// The reward, printed on whichever side of the vine the curve is not using.
        ///
        /// A label pinned to a fixed side would land on top of the stem for half the rungs of
        /// any track long enough to bend twice, and which half depends on the milestone goals
        /// an author wrote.
        /// </summary>
        void BuildRungLabel(Rung rung, Vector2 at, bool taken, bool ready, EventMilestone milestone)
        {
            float side = at.x > 0f ? -1f : 1f;
            var pos = new Vector2(side * 190f, 0f);

            var plate = UIKit.Img("Plate", rung.Root, Art.Round(24),
                                  new Color(.05f, .11f, .10f, taken || ready ? .82f : .58f),
                                  new Vector2(PlateW, 96f), new Vector2(.5f, .5f), pos);

            var edge = UIKit.Img("Edge", plate.transform, Art.RoundOutline(24, 3f),
                                 taken ? Pal.A(Pal.Gold, .52f)
                                       : ready ? Pal.A(Pal.Sun, .46f)
                                               : new Color(1f, 1f, 1f, .10f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);
            rung.PlateEdge = edge;

            // Coin then amount, laid out from the plate's left edge. A left-anchored label
            // draws from the *left edge of its own box*, not from the point it is placed at,
            // so the box has to be pushed out by half its width or the text starts back
            // underneath the glyph — which is exactly what "+250" was doing to the coin.
            const float CoinSize = 52f;
            const float CoinLeft = 16f;
            const float TextGap = 12f;
            const float PlatePad = 14f;

            float textLeft = CoinLeft + CoinSize + TextGap;
            float textWide = PlateW - textLeft - PlatePad;

            var coin = UIKit.Img("Coin", plate.transform, null, Color.white,
                                 Vector2.one * CoinSize, new Vector2(0f, .5f),
                                 new Vector2(CoinLeft + CoinSize * .5f, 12f));
            coin.preserveAspect = true;
            RewardArt.Glyph(coin, ChestDropKind.Credits, 10f);
            if (!taken && !ready) coin.color = new Color(1f, 1f, 1f, .45f);

            rung.Reward = UIKit.Shrinkable(
                UIKit.Titled("Amount", plate.transform,
                             Loc.Format("ui.event.reward", Compact.Number(milestone.Credits)), 34,
                             taken ? Pal.Gold : ready ? Pal.Sun : Pal.A(Pal.Cream, .55f),
                             TextAnchor.MiddleLeft, new Vector2(textWide, 44f),
                             new Vector2(0f, .5f),
                             new Vector2(textLeft + textWide * .5f, 12f), 3f, 3f), 20);

            // What the rung asks for, under the amount. Written on every state, including the
            // ones already taken: it is the only place the goals appear, and a track that
            // hides them once they are met cannot be read back as a plan.
            UIKit.Shrinkable(
                UIKit.Titled("Goal", plate.transform,
                             milestone.Goal == 1
                                 ? Loc.Get("ui.event.at_goal_one")
                                 : Loc.Format("ui.event.at_goal", milestone.Goal), 20,
                             Pal.A(Pal.Cream, taken || ready ? .70f : .45f), TextAnchor.MiddleCenter,
                             new Vector2(PlateW - 20f, 28f), new Vector2(.5f, .5f), new Vector2(0f, -26f),
                             0f, 3f), 14);

            if (!ready) return;

            rung.Prompt = UIKit.Shrinkable(
                UIKit.Titled("Take", rung.Root, Loc.Get("ui.event.collect").ToUpperInvariant(), 24,
                             Pal.Radiance, TextAnchor.MiddleCenter, new Vector2(240f, 34f),
                             new Vector2(.5f, .5f), new Vector2(0f, -128f), 3f, 3f), 16);
        }

        /// <summary>
        /// The turning fan of light behind a rung that is waiting. The streak page wears the
        /// same one, deliberately: across this game a slow gold fan means "this is yours,
        /// take it", and a second vocabulary for the same idea would teach nothing twice.
        /// </summary>
        RectTransform Aura(RectTransform host)
        {
            var aura = UIKit.Box("Aura", host, Vector2.zero, new Vector2(.5f, .5f), Vector2.zero);
            aura.SetAsFirstSibling();

            UIKit.Halo(aura, Pal.Gold, 420f, .30f);

            var rays = UIKit.Img("Rays", aura, Art.Rays(256, 14), Pal.A(Pal.Sun, .26f),
                                 Vector2.one * 380f, new Vector2(.5f, .5f), Vector2.zero);
            var rrt = (RectTransform)rays.transform;
            Tween.Run(14f, Ease.Linear,
                      t => { if (rrt) rrt.localRotation = Quaternion.Euler(0f, 0f, t * 360f); },
                      rays).Loop(-1, false);

            var ring = UIKit.Img("Ring", aura, Art.Ring(160, 9f), Pal.A(Pal.Gold, 0f),
                                 Vector2.one * 220f, new Vector2(.5f, .5f), Vector2.zero);
            var rt = (RectTransform)ring.transform;
            Tween.Run(1.6f, Ease.OutCubic, t =>
            {
                if (!ring) return;
                float k = Mathf.Repeat(t, 1f);
                rt.localScale = Vector3.one * Mathf.Lerp(.78f, 1.55f, k);
                ring.color = Pal.A(Pal.Cream, .58f * (1f - k) * Mathf.Clamp01(k * 6f));
            }, ring).Loop(-1, false);

            return aura;
        }

        void Seal(RectTransform host, bool animated)
        {
            var seal = UIKit.Img("Seal", host, Art.S("Ui/seal_gold"), Color.white,
                                 new Vector2(84f, 84f), new Vector2(.5f, .5f),
                                 new Vector2(66f, -66f));
            seal.preserveAspect = true;

            var tick = UIKit.Img("Tick", seal.transform, Art.S("Ui/ic_check"), Pal.Cream,
                                 new Vector2(42f, 42f), new Vector2(.5f, .5f), Vector2.zero);
            tick.preserveAspect = true;

            if (!animated) return;

            seal.transform.localScale = Vector3.one * 2.6f;
            Tween.Scale(seal.transform, 1f, .34f, Ease.OutBack)
                 .OnDone(() => { if (seal) Tween.Punch(host, .10f, .26f); });
        }

        // ------------------------------------------------------------ collecting
        /// <summary>
        /// Hands over the tapped rung and every uncollected one below it.
        ///
        /// <para>
        /// The grant goes first and the animation reports it, which is the rule the streak
        /// page states and this one inherits: a player who kills the app during the burst has
        /// still collected the flower. The rungs to animate are gathered <em>before</em> the
        /// call, because collecting sweeps the ones underneath and they would no longer answer
        /// as collectable afterwards.
        /// </para>
        /// </summary>
        void Take(Rung tapped)
        {
            if (_collecting || tapped == null || tapped.Root == null) return;
            if (!GroveEvents.IsCollectable(_event, tapped.Milestone)) return;

            var taking = new List<Rung>();
            foreach (var rung in _rungs)
            {
                if (rung.Taken || rung.Milestone.Goal > tapped.Milestone.Goal) continue;
                if (!GroveEvents.IsCollectable(_event, rung.Milestone)) continue;
                taking.Add(rung);
            }

            if (taking.Count == 0) return;

            _collecting = true;

            // Switched off rather than made non-interactable: Btn paints a disabled element
            // grey, and these are transparent hit boxes over the flower art.
            foreach (var rung in taking) if (rung.Tap) rung.Tap.gameObject.SetActive(false);

            GroveEvents.Collect(_event, tapped.Milestone.Goal);

            // No haptic. Handheld.Vibrate is a fixed-length pulse on Android, and tapping a
            // later bloom sweeps every earlier one with it — so one tap can open a run of
            // rungs and the buzz sits underneath all of them rather than marking any. The
            // flowers opening one after another is the feedback; see ChestOverlay for the
            // same call made for the same reason.
            var cue = new Cue(this);
            for (int i = 0; i < taking.Count; i++)
            {
                var rung = taking[i];
                cue.Then(i == 0 ? 0f : .34f, () => Open(rung));
            }

            cue.Then(.85f, () =>
            {
                _collecting = false;
                _progress = GroveEvents.ProgressOf(_event);

                if (!_progress.IsComplete || _progress.AnyWaiting) { RefreshChrome(); return; }

                // The track is finished and nothing is left waiting. Worth a moment: this is
                // the last thing the event will ever say to this player.
                Burst.Confetti(Content, 70);
                Scenery.Toast(Content, Loc.Get("ui.event.complete"), Pal.Gold, 2.4f);
                RefreshChrome();
            });
        }

        /// <summary>One flower's whole ceremony.</summary>
        void Open(Rung rung)
        {
            if (rung == null || rung.Root == null) return;

            rung.Taken = true;

            Tween.KillChannel(rung.Root, "breathe");
            Tween.Punch(rung.Root, .24f, .40f);
            Audio.Sfx("unlock", .82f, 1.02f);

            // The fan flares outward rather than fading, so the light looks like it went
            // into the flower rather than like it was switched off.
            if (rung.Aura != null)
            {
                var aura = rung.Aura;
                var fade = UIKit.Group(aura);
                Tween.Run(.36f, Ease.OutQuint, k =>
                {
                    if (!aura) return;
                    aura.localScale = Vector3.one * Mathf.Lerp(1f, 1.8f, k);
                    fade.alpha = 1f - k;
                }, aura).OnDone(() => { if (aura) Destroy(aura.gameObject); });
                rung.Aura = null;
            }

            // The bud opens. Art.Bloom quantises to eighths and caches every step, which is
            // exactly why this can be driven from a tween instead of faked with a scale.
            var petals = rung.Petals;
            var heart = rung.Heart;

            Tween.Run(.62f, Ease.OutBack, k =>
            {
                if (petals)
                {
                    petals.sprite = Art.Bloom(160, 6, Mathf.Lerp(.375f, 1f, k));
                    petals.color = Color.Lerp(Pal.Rose, Pal.Bloom, k);
                    petals.transform.localScale = Vector3.one * Mathf.Lerp(1f, 1.10f, Mathf.Sin(k * Mathf.PI));
                    petals.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 34f, k));
                }
                if (heart)
                {
                    heart.sprite = Art.Bloom(96, 5, Mathf.Lerp(.20f, .55f, k));
                    heart.color = Color.Lerp(Pal.Sun, Pal.Radiance, k);
                }
            }, rung.Root);

            Burst.Sparks(rung.Root, Vector2.zero, Pal.Bloom, 24, 400f, 30f, .74f);
            Tween.After(.14f, () =>
            {
                if (rung.Root) Burst.Sparks(rung.Root, Vector2.zero, Pal.Gold, 14, 250f, 22f, .6f);
            });

            Flow.Flash(Pal.A(Pal.Bloom, 1f), .10f, .34f);

            // Everything that said "not yet" has to stop saying it, in the same beat. The
            // page is not rebuilt after a collect — that would destroy the flower mid-open —
            // so anything the ceremony leaves behind stays behind, and an instruction to tap
            // something already taken is the most confusing thing it could leave.
            if (rung.Reward) rung.Reward.color = Pal.Gold;
            if (rung.PlateEdge) Tween.Tint(rung.PlateEdge, Pal.A(Pal.Gold, .52f), .3f);

            if (rung.Prompt)
            {
                var prompt = rung.Prompt;
                Tween.Run(.26f, Ease.InQuad, k =>
                {
                    if (prompt) prompt.color = Pal.A(Pal.Radiance, 1f - k);
                }, prompt).OnDone(() => { if (prompt) Destroy(prompt.gameObject); });
                rung.Prompt = null;
            }

            Shed(rung.Root, 9);
            Throw(rung);
            Seal(rung.Root, true);

            if (rung.Tap) Destroy(rung.Tap.gameObject);
        }

        /// <summary>
        /// Petals thrown off the flower as it opens, tumbling and settling.
        ///
        /// The one flourish here that is not borrowed from the streak page, and it earns its
        /// keep: sparks say "a thing happened", petals say "a flower opened". They fall
        /// rather than fade because gravity is what tells the eye these are physical.
        /// </summary>
        void Shed(RectTransform host, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float power = Random.Range(120f, 300f);
                float size = Random.Range(24f, 40f);
                float spin = Random.Range(-320f, 320f);
                float life = Random.Range(.85f, 1.35f);

                var petal = UIKit.Img("Petal", host, Art.Bloom(48, 5, 1f),
                                      Color.Lerp(Pal.Bloom, Pal.Cream, Random.value * .55f),
                                      Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);

                var rt = (RectTransform)petal.transform;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float drift = Random.Range(-40f, 40f);

                Tween.Run(life, Ease.OutCubic, k =>
                {
                    if (!rt) return;
                    var p = dir * power * k;
                    p.y -= 340f * k * k;                       // fall
                    p.x += drift * Mathf.Sin(k * 7f);          // flutter
                    rt.anchoredPosition = p;
                    rt.localRotation = Quaternion.Euler(0f, 0f, spin * k);
                    rt.localScale = Vector3.one * Mathf.Lerp(1f, .7f, k);
                    petal.color = Pal.A(petal.color, 1f - k * k);
                }, petal).OnDone(() => { if (petal) Destroy(petal.gameObject); });
            }
        }

        /// <summary>
        /// The reward, thrown from the flower up to the purse on a parabola.
        ///
        /// In <c>Content</c>-local space via <c>InverseTransformPoint</c>, because the flower
        /// lives inside a vine that may be scrolled and the purse does not.
        /// </summary>
        void Throw(Rung rung)
        {
            if (_purse == null || rung.Root == null) return;

            Vector2 from = Content.InverseTransformPoint(rung.Root.position);
            Vector2 to = Content.InverseTransformPoint(_purse.position) + new Vector3(-118f, 4f);

            var flyer = UIKit.Img("Flyer", Content, null, Color.white,
                                  new Vector2(96f, 96f), new Vector2(.5f, .5f), from);
            flyer.preserveAspect = true;
            RewardArt.Glyph(flyer, ChestDropKind.Credits, 12f);

            var trail = UIKit.Img("Trail", flyer.transform, Art.Glow(128, 1.9f), Pal.A(Pal.Gold, .55f),
                                  new Vector2(200f, 200f), new Vector2(.5f, .5f), Vector2.zero);
            trail.transform.SetAsFirstSibling();

            var frt = (RectTransform)flyer.transform;
            float lift = Mathf.Min(300f, Vector2.Distance(from, to) * .34f);
            long paid = rung.Milestone.Credits;

            Tween.Run(.66f, Ease.InOutCubic, k =>
            {
                if (!frt) return;
                var p = Vector2.LerpUnclamped(from, to, k);
                p.y += Mathf.Sin(k * Mathf.PI) * lift;
                frt.anchoredPosition = p;
                frt.localScale = Vector3.one * Mathf.Lerp(1.3f, .58f, k);
                frt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, -28f, k));
            }, flyer).Delay(.16f).OnDone(() =>
            {
                if (flyer) Destroy(flyer.gameObject);
                Land(to, paid);
            });
        }

        void Land(Vector2 at, long paid)
        {
            Audio.Sfx("chime", .85f, 1.06f);
            Burst.Sparks(Content, at, Pal.Gold, 16, 260f, 26f, .55f);

            if (_purse) Tween.Punch(_purse, .16f, .30f);

            long from = _gathered;
            _gathered += paid;

            if (_purseNumber)
            {
                Tween.Punch(_purseNumber.transform, .22f, .34f);
                Roll.Number(_purseNumber, from, _gathered, .5f, Compact.Number, this);
            }

            var rise = UIKit.Shrinkable(
                UIKit.Titled("Won", Content, Loc.Format("ui.event.reward", Compact.Number(paid)), 44, Pal.Gold,
                             TextAnchor.MiddleCenter, new Vector2(320f, 56f), new Vector2(.5f, .5f),
                             at + new Vector2(0f, 40f), 4f, 4f), 26);

            var rrt = (RectTransform)rise.transform;
            var start = rrt.anchoredPosition;

            Tween.Run(1.2f, Ease.OutCubic, k =>
            {
                if (!rise) return;
                rrt.anchoredPosition = start + new Vector2(0f, 110f * k);
                rise.color = Pal.A(Pal.Gold, k < .18f ? k / .18f : 1f - (k - .18f) / .82f);
            }, rise).OnDone(() => { if (rise) Destroy(rise.gameObject); });
        }

        /// <summary>
        /// Re-texts what the ceremony changed, rather than rebuilding under it.
        ///
        /// A full rebuild here would destroy the flowers that have just been opened and pop
        /// them back in, which would undo the whole point of the animation.
        /// </summary>
        void RefreshChrome()
        {
            _progress = GroveEvents.ProgressOf(_event);

            if (_clock) _clock.text = ClockLine();
            if (_state) _state.text = StateLine();
        }

        // --------------------------------------------------------------- footer
        void BuildFooter()
        {
            BuildRail();

            _state = UIKit.Shrinkable(
                UIKit.Titled("State", Content, StateLine(), 27, StateTint(),
                             TextAnchor.MiddleCenter, new Vector2(880f, 42f), new Vector2(.5f, 0f),
                             new Vector2(0f, NavBar.Height + 168f), 3f, 4f), 18);

            var next = GroveEvents.NextGlade(_event);

            // No way in once every glade is finished or the window has shut. A button that
            // reopens a closed event wastes the tap of the player who gave it everything.
            if (!next.IsValid || _closed) return;

            var play = UIKit.TextButton("Play", Content, "btn_violet", Loc.Get("ui.event.cta"), 46,
                                        new Vector2(520f, 132f), new Vector2(.5f, 0f),
                                        new Vector2(0f, NavBar.Height + 76f),
                                        () => PlayRoute.Open(next));
            UIKit.Halo(play.transform, Pal.Bloom, 620f, .30f);

            play.transform.localScale = Vector3.zero;
            Tween.Pop(play.transform, 0f, .5f, .52f).OnDone(() =>
            {
                if (!play) return;
                play.Rehome();
                Sheen.Attach((RectTransform)play.transform, 3.4f);
            });
        }

        /// <summary>
        /// The event's own glades, with what the player has done to each.
        ///
        /// The page would work without it and be worse: an event is a lens on glades that
        /// already exist, so "which ones" is the first question it raises and the vine cannot
        /// answer it. A dot per glade rather than names, because the list is a shape to
        /// glance at and four names is a paragraph.
        /// </summary>
        void BuildRail()
        {
            int count = _event.Levels.Count;
            if (count == 0) return;

            var host = UIKit.Box("Rail", Content, new Vector2(880f, 110f), new Vector2(.5f, 0f),
                                 new Vector2(0f, NavBar.Height + 246f));

            float slot = Mathf.Min(126f, 880f / count);
            float size = Mathf.Min(96f, slot - 16f);
            var records = PlayerProgress.RecordsById;

            for (int i = 0; i < count; i++)
            {
                var levelId = _event.Levels[i];
                records.TryGetValue(levelId, out var record);

                bool cleared = record != null && record.IsCleared;
                bool counted = cleared && record.FirstClearedUnix >= _event.StartUnix
                                       && record.FirstClearedUnix < _event.EndUnix;

                float x = (i - (count - 1) * .5f) * slot;

                var node = UIKit.Box("N" + i, host, Vector2.one * size,
                                     new Vector2(.5f, .5f), new Vector2(x, 6f));

                UIKit.Img("Disc", node, Art.Disc(96),
                          counted ? Pal.Bloom
                                  : cleared ? new Color(.30f, .40f, .36f, .92f)
                                            : new Color(.10f, .16f, .18f, .82f));

                var ring = UIKit.Img("Ring", node, Art.Ring(96, 8f),
                                     counted ? Pal.Cream : new Color(1f, .95f, .84f, .28f));
                UIKit.StretchTo((RectTransform)ring.transform, 0, 0, 0, 0);

                if (counted)
                {
                    var tick = UIKit.Img("Tick", node, Art.S("Ui/ic_check"), new Color(.20f, .08f, .18f),
                                         Vector2.one * size * .46f, new Vector2(.5f, .5f), Vector2.zero);
                    tick.preserveAspect = true;
                }
                else
                {
                    // Cleared before the window opened counts for nothing, and saying so with
                    // a grey tick would be a lie. It gets the same mark an unplayed glade
                    // does, and the info panel explains why.
                    UIKit.Titled("N", node, (i + 1).ToString(), 34,
                                 Pal.A(Pal.Cream, cleared ? .55f : .78f), TextAnchor.MiddleCenter,
                                 Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero, 3f, 3f);
                }

                // Straight into the glade. The whole point of naming them is that they are
                // reachable, and a rail that only reports is a rail that wastes its own space.
                var tap = UIKit.Button("Tap", node, null, Vector2.one * size,
                                       new Vector2(.5f, .5f), Vector2.zero,
                                       () => PlayRoute.Open(levelId));
                tap.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
                tap.PressScale = .92f;

                node.localScale = Vector3.zero;
                Tween.Pop(node, 0f, .42f, .44f + i * .05f);
            }

            // Above the nodes, not below them: the state line sits directly under this rail
            // and a caption hanging off the bottom lands on top of it.
            UIKit.Shrinkable(
                UIKit.Titled("Caption", host, Loc.Get("ui.event.rail"), 21,
                             Pal.A(Pal.Cream, .60f), TextAnchor.MiddleCenter,
                             new Vector2(700f, 30f), new Vector2(.5f, 1f), new Vector2(0f, 22f),
                             0f, 3f), 14);
        }

        string StateLine()
        {
            if (_progress.Waiting == 1) return Loc.Get("ui.event.waiting_one");
            if (_progress.Waiting > 1) return Loc.Format("ui.event.waiting_many", _progress.Waiting);
            if (_progress.IsComplete) return Loc.Get("ui.event.all_taken");
            if (_closed) return Loc.Get("ui.event.ended");
            return Loc.Format("ui.event.to_next", _progress.ToNext);
        }

        Color StateTint()
        {
            if (_progress.AnyWaiting) return Pal.Gold;
            if (_progress.IsComplete) return Pal.Mint;
            return Pal.A(Pal.Cream, .78f);
        }

        // -------------------------------------------------------------- ambience
        /// <summary>
        /// Petals on the air, drifting down across the whole page.
        ///
        /// Local rather than a widget, and it stays that way until a second screen wants
        /// them: <c>Fireflies</c> earned its place in <c>Widgets</c> by being used by three,
        /// and a one-caller helper in a shared file is a shared file that is harder to read.
        /// </summary>
        static void Petals(Transform parent, int count)
        {
            var host = UIKit.Node("Petals", parent);
            host.SetAsFirstSibling();

            for (int i = 0; i < count; i++)
            {
                float size = Random.Range(16f, 34f);
                float x = Random.Range(-.55f, .55f) * Boot.RefWidth;
                float fall = Random.Range(11f, 20f);
                float drift = Random.Range(40f, 130f) * (Random.value < .5f ? -1f : 1f);
                float spin = Random.Range(-70f, 70f);
                float phase = Random.Range(0f, 1f);

                var petal = UIKit.Img("p", host, Art.Bloom(48, 5, 1f),
                                      Pal.A(Color.Lerp(Pal.Bloom, Pal.Cream, Random.value), .55f),
                                      Vector2.one * size, new Vector2(.5f, 1f), new Vector2(x, 0f));
                var rt = (RectTransform)petal.transform;

                Tween.Run(fall, Ease.Linear, k =>
                {
                    if (!rt) return;
                    float t = Mathf.Repeat(k + phase, 1f);
                    rt.anchoredPosition = new Vector2(x + drift * Mathf.Sin(t * 5.2f + phase * 6f),
                                                      120f - t * (Boot.RefHeight + 260f));
                    rt.localRotation = Quaternion.Euler(0f, 0f, spin * t * 6f);
                }, petal).Loop(-1, false);
            }
        }

        public override bool OnBack() { Flow.Go<HomeScreen>(); return true; }
    }
}
