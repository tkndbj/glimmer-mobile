using System;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Tasks &amp; Bonuses: today's slate, this week's slate, and the ladder of chests they pay.
    ///
    /// <para>
    /// <b>One page, three bands.</b> The ladder across the top says what there is to earn
    /// and is tappable so a chest can explain itself before anybody has one
    /// (<see cref="ChestOddsOverlay"/>). Under it the two slates, each a heading with its
    /// own clock and a row per dealt task. A row is a picture of one
    /// <see cref="TaskDefinition"/> in one <see cref="TaskState"/>: counting, ready, or
    /// paid — and the whole row is the button when it is ready, because a small chip
    /// inside it would be a smaller target for the same action and there is nothing else
    /// on a row to tap by mistake (the streak page's rule).
    /// </para>
    /// <para>
    /// <b>Built once, repainted from the ledger.</b> A counter moving is a redraw, not a
    /// new list, so <see cref="Repaint"/> writes the bar and the state onto rows that
    /// already exist and replays no entrance (<c>CRAFT.md</c>: Show animates, Refresh does
    /// not). The one thing a repaint cannot do is change <em>which</em> tasks are dealt —
    /// that happens at midnight — so when the dealt ids differ from the built ones the
    /// page rebuilds whole, which is the honest answer to a different slate.
    /// </para>
    /// <para>
    /// The chest reels are a scope of their own, opened on arrival so the ceremony a tap
    /// starts finds its frames already here (invariant 7b); the overlay holds the same
    /// scope for the frame it can outlive this page by.
    /// </para>
    /// </summary>
    public sealed class TasksScreen : View
    {
        public override string Track => "mus_menu";

        // The stack, in canvas units from the top of the safe area.
        const float ChromeSize = 92f;
        const float LadderH = 252f;
        const float HeadingH = 76f;
        const float RowH = 168f;
        const float RowGap = 14f;
        const float Width = 1000f;

        static readonly Vector2 Top = new Vector2(.5f, 1f);
        static readonly Vector2 Left = new Vector2(0f, .5f);
        static readonly Vector2 Centre = new Vector2(.5f, .5f);

        /// <summary>
        /// What the progress bar's orange has to be written as on <see cref="Skins.Fill"/>, and
        /// why it is a number here rather than a colour out of <see cref="Pal"/>.
        ///
        /// <para>
        /// The kit's fill is documented as a white sprite and is not one: it runs from a warm
        /// off-white at the top (241, 240, 233) down to (188, 181, 166), and <c>Image.color</c>
        /// is a <b>multiply</b> (invariant 37l) — so whatever is written here is not the colour
        /// the bar comes out, it is the colour the multiply <em>needs</em> to land on one.
        /// `Pal.Gold` over it lands on a <b>brown</b> across the bottom two thirds, where a bar
        /// spends most of its area, and `Pal.Amber` lands a shade redder and softer than an
        /// orange bar wants. So this is pre-divided rather than named, and it is the one number
        /// on this screen that may not be reasoned about — only drawn.
        /// </para>
        /// <para>
        /// <b>The other half of it is height, and that took a swatch sheet to find.</b> The
        /// obvious fix for a dull bar is a bright gloss along the top, and drawn side by side at
        /// the size a bar is actually drawn <em>every</em> version of that reads worse: a
        /// highlight over a 20-unit bar covers half of it and washes the colour out, and lifting
        /// the tint toward white takes the chroma with it (44g's rule, from the other end). What
        /// works is a saturated tint drawn <b>taller in its trough</b> — 26 of the trough's 30
        /// rather than 20, because at that size the dark border was a third of what the eye was
        /// averaging. The gloss is deliberately absent rather than merely turned down, and
        /// <c>render_tasks.py</c> is where a colour question gets settled here.
        /// </para>
        /// </summary>
        static readonly Color BarOrange = new Color(1f, .588f, .118f, 1f);

        /// <summary>
        /// And what it turns when it is full. Pre-divided for <see cref="BarOrange"/>'s reason
        /// — `Pal.Mint` straight lands washy beside the orange — and a <em>colour</em> rather
        /// than a second readout, because the bar is already saying the same thing with its
        /// length: the change is what makes a glance at the list enough.
        /// </summary>
        static readonly Color BarFull = new Color(.376f, .922f, .275f, 1f);

        /// <summary>How tall the fill is drawn inside its 30-unit trough. See <see cref="BarOrange"/>.</summary>
        const float BarH = 26f;

        /// <summary>One dealt task's row, kept so a repaint can write onto it.</summary>
        sealed class Row
        {
            public TaskDefinition Task;
            public RectTransform Root;
            public Image Card;
            public RectTransform Fill;
            public Text Count;
            public Text Hint;
            public Image Chest;
            public RectTransform Halo;
            public RectTransform Seal;
            public Btn Tap;
            public CanvasGroup Group;
            public TaskState Painted;
            public float Track;
            public Image Pool;
            public Image Rim;
            public Image Bar;
            public bool? Full;
        }

        readonly List<Row> _rows = new List<Row>();
        readonly Dictionary<TaskPeriod, Text> _clocks = new Dictionary<TaskPeriod, Text>();
        readonly Dictionary<TaskPeriod, Text> _tallies = new Dictionary<TaskPeriod, Text>();

        float _clockTick;
        bool _claiming;
        AssetHold _reels;

        protected override void Build()
        {
            _rows.Clear();
            _clocks.Clear();
            _tallies.Clear();

            // The profile's ground rather than the hub's world. `Scenery.Room` is a painting of
            // somewhere — a forest with a bridge in it — and this page is a list of plates laid
            // over the whole width of it, so the picture was only ever visible in the gaps
            // between rows. `Scenery.Plain` is a uniform pattern that is a *ground* rather than
            // a place, which is what a page made of furniture wants under it, and it carries no
            // parallax and no vignette for the same reason.
            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 22, new Color(1f, .93f, .70f), 6f, 22f);

            float y = 22f;
            y = BuildHeader(y);
            y = BuildLadder(y);
            BuildSlates(y);

            NavBar.Build(Content, NavBar.Tab.Home);
            HoldReels();
        }

        void OnEnable() { TaskLedger.Changed += OnChanged; }
        void OnDisable() { TaskLedger.Changed -= OnChanged; }

        void OnDestroy()
        {
            _reels?.Dispose();
            _reels = null;
        }

        /// <summary>
        /// The reels, for the ceremony. Held here rather than only by the overlay so the
        /// first tap on a ready row does not open a chest whose lid has not loaded.
        /// </summary>
        void HoldReels() => Run(async token =>
        {
            _reels = _reels ?? AssetLibrary.Hold("chests");
            await _reels.LoadAsync(AssetManifest.ChestAssets(ProgressionRules.Table.Tasks), null, token);
        });

        /// <summary>
        /// A counter moved, a chest was claimed, or a period rolled over. Repaint in place
        /// unless the slate itself changed, in which case the page is a different page.
        /// </summary>
        void OnChanged()
        {
            if (this == null || Content == null) return;
            if (_claiming) return;

            if (!SameSlate()) { Rebuild(); return; }
            Repaint();
        }

        bool SameSlate()
        {
            int i = 0;
            foreach (var period in TaskPeriods.All)
                foreach (var task in TaskLedger.Active(period))
                {
                    if (i >= _rows.Count) return false;
                    if (!string.Equals(_rows[i].Task.Id, task.Id, StringComparison.Ordinal)) return false;
                    i++;
                }
            return i == _rows.Count;
        }

        void Rebuild()
        {
            for (int i = Content.childCount - 1; i >= 0; i--)
            {
                var child = Content.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            Build();
        }

        // --------------------------------------------------------------- header
        /// <summary>
        /// The chrome, the name and the wallet, in that order down the page.
        ///
        /// <para>
        /// <b>The banner and the pills changed places.</b> The page led with its wallet and
        /// named itself underneath, which is the arrangement a shop wants — on a page whose
        /// subject is what there is to <em>do</em>, the first thing read should be what the page
        /// is. The pills keep every other property they had: they are still what
        /// <c>RewardFlight</c> lands a chest's tokens on, and a page that pays currency still
        /// says how much of it you have without being asked.
        /// </para>
        /// <para>
        /// The name takes the corner buttons' row rather than a row of its own, because the
        /// ribbon is taller than they are and the corners are empty beside it — the whole swap
        /// costs the page nothing in height. The subtitle stays with the banner it explains.
        /// </para>
        /// </summary>
        float BuildHeader(float y)
        {
            // The ribbon's own row, with the two corner buttons standing in it.
            const float BannerH = 138f;
            float cy = -(y + BannerH * .5f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * ChromeSize,
                             new Vector2(0f, 1f), new Vector2(76f, cy),
                             () => Flow.Go<HomeScreen>());
            UIKit.IconButton("Info", Safe, Skins.Aside, "ic_info", Vector2.one * ChromeSize,
                             new Vector2(1f, 1f), new Vector2(-76f, cy),
                             () => { if (!Flow.HasModal) Flow.Modal<TasksInfoOverlay>(); });

            var ribbon = Scenery.TitleRibbon(Safe, Loc.Get("ui.tasks.title").ToUpperInvariant(),
                                             new Vector2(720f, BannerH), Top, new Vector2(0f, cy), 42);
            ribbon.transform.localScale = Vector3.zero;
            Tween.Pop(ribbon.transform, 0f, .5f, .06f);
            y += BannerH + 4f;

            UIKit.Shrinkable(
                UIKit.Titled("Sub", Safe, Loc.Get("ui.tasks.subtitle"), 24, new Color(.86f, .90f, 1f, .78f),
                             TextAnchor.MiddleCenter, new Vector2(800f, 32f), Top,
                             new Vector2(0f, -(y + 16f)), 3f, 3f), 15);
            y += 32f + 14f;

            float py = -(y + ChromeSize * .5f);
            Pill(ResourceSlots.Kind.Hearts, -232f, py, Pal.Rose, Art.S("Ui/ic_heart"),
                 Profile.HeartsLabel(), v => Profile.HeartsLabel((int)v));
            Pill(ResourceSlots.Kind.Credits, 0f, py, Pal.Gold, null,
                 Compact.Number(Profile.Coins), v => Compact.Number(v));
            Pill(ResourceSlots.Kind.Gems, 232f, py, Pal.Bloom, Art.S("Ui/ic_gem"),
                 Compact.Number(Profile.Gems), v => Compact.Number(v));
            y += ChromeSize + 18f;

            return y;
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

        // --------------------------------------------------------------- ladder
        /// <summary>
        /// The chests, as the pack the hub draws (<see cref="ChestPack"/>) — a symmetric arch
        /// with the grandest at the crest, packed until they overlap, each one tappable for its
        /// odds. The ladder is the "bonuses" half of the page's name: it is what there is, where
        /// the slates are what to do about it.
        ///
        /// <para>
        /// <b>It used to be four icons spread evenly across the plate with their names under
        /// them, and the rebuild cost the names.</b> A packed row has no room for four captions
        /// — measured, they overlap by a third and print as one run of letters — and four
        /// captions is not what the chests are for anyway. What replaces them is one line saying
        /// the row can be tapped, which is the thing the old version never said and the only
        /// reason a player would ever discover the odds panel at all.
        /// </para>
        /// <para>
        /// <b>These four stand a little apart where the hub's touch.</b> Same shape, one sign:
        /// the hub's box is a picture of a pack and wants the chests pressed together, while
        /// every chest here is a <em>button</em>, and four buttons that overlap are four targets
        /// whose edges belong to whichever was drawn last.
        /// </para>
        /// </summary>
        float BuildLadder(float y)
        {
            const float Tall = 196f;
            const float Short = 136f;
            const float Dip = 20f;
            const float Floor = -70f;    // the caption under the pack is what stops it going lower
            const float Overlap = -.05f;  // negative: the chests stand a little apart here

            var plate = UIKit.Img("Ladder", Safe, Art.S("Ui/" + Skins.Panel), Color.white,
                                  new Vector2(Width, LadderH), Top, new Vector2(0f, -(y + LadderH * .5f)));

            // The rays and the pool the chests stand in, clipped to the plate: both reach past
            // it, and both are what make a row of pictures read as treasure rather than as an
            // inventory (the hub's card, invariant 45g).
            var clip = UIKit.Node("Clip", plate.transform);
            UIKit.StretchTo(clip, 6f, 6f, 6f, 6f);
            clip.gameObject.AddComponent<RectMask2D>();

            var rays = UIKit.Img("Rays", clip, Art.Rays(512, 14), Pal.A(Pal.Sun, .20f),
                                 new Vector2(820f, 820f), Centre, new Vector2(0f, 22f));
            Tween.Run(36f, Ease.Linear,
                      t => { if (rays) rays.transform.localRotation = Quaternion.Euler(0, 0, t * 360f); },
                      rays, "spin").Loop(-1, false);

            UIKit.Img("Shelf", clip, Art.Glow(128, 1.7f), new Color(.02f, .06f, .16f, .40f),
                      new Vector2(900f, 160f), Centre, new Vector2(0f, -62f));

            var tiers = ProgressionRules.Table.Tasks.Tiers;
            var lit = ReadyTiers();
            var seats = ChestPack.Lay(tiers, Tall, Short, Dip, Floor, Overlap);

            foreach (var seat in seats)
                UIKit.Img("S_" + seat.Tier.Id, plate.transform, Art.Glow(128, 1.9f),
                          new Color(.01f, .05f, .13f, .46f),
                          new Vector2(seat.Width * 1.30f, seat.Tall * .22f), Centre,
                          new Vector2(seat.X, seat.Foot - 2f));

            foreach (var seat in ChestPack.InDrawOrder(seats))
            {
                var tier = seat.Tier;
                bool ready = lit.Contains(tier.Id);

                // The button is the sprite's own box rather than the drawn chest, so the tap
                // target keeps the art's aspect; where two overlap the taller one is later in
                // the hierarchy and wins, which is the chest a thumb was aiming at anyway.
                var btn = UIKit.Button("T_" + tier.Id, plate.transform, Art.S(tier.Icon),
                                       seat.Box, Centre, seat.Anchor, () => OpenOdds(tier));
                btn.GetComponent<Image>().preserveAspect = true;
                btn.PressScale = .92f;

                if (ready)
                {
                    var halo = UIKit.Halo(btn.transform, Pal.Gold, seat.Tall * 1.9f, .5f);
                    ((RectTransform)halo.transform).anchoredPosition =
                        new Vector2(0f, -seat.Tall * ChestPack.Lift);
                    Tween.Breathe(btn.transform, .06f, 1.6f, seat.Index * .4f);
                }
                else
                {
                    btn.GetComponent<Image>().color = new Color(.90f, .92f, .96f, 1f);
                }

                btn.transform.localScale = Vector3.zero;
                Tween.Pop(btn.transform, 0f, .5f, .18f + seat.Index * .07f)
                     .OnDone(() => { if (btn) btn.Rehome(); });
            }

            UIKit.Shrinkable(
                UIKit.Titled("Tap", plate.transform, Loc.Get("ui.tasks.ladder_hint"), 22,
                             Pal.A(Pal.Cream, .78f), TextAnchor.MiddleCenter,
                             new Vector2(Width - 80f, 30f), Centre,
                             new Vector2(0f, -LadderH * .5f + 22f), 3f, 3f), 14);

            plate.transform.localScale = Vector3.zero;
            Tween.Pop(plate.transform, 0f, .5f, .10f);

            return y + LadderH + 16f;
        }

        static HashSet<string> ReadyTiers()
        {
            var lit = new HashSet<string>(StringComparer.Ordinal);
            foreach (var period in TaskPeriods.All)
                foreach (var task in TaskLedger.Active(period))
                    if (TaskLedger.StateOf(task) == TaskState.Ready) lit.Add(task.Tier.Id);
            return lit;
        }

        void OpenOdds(ChestTier tier)
        {
            if (Flow.HasModal) return;
            Flow.Modal<ChestOddsOverlay>(v => v.Tier = tier);
        }

        // --------------------------------------------------------------- slates
        /// <summary>
        /// Both slates in one scrolling band from the ladder to the nav bar. The band is
        /// measured rather than assumed: on a tall phone everything fits and the list sits
        /// still, on a short one it scrolls, and neither shape is written down.
        /// </summary>
        void BuildSlates(float top)
        {
            float bottom = NavBar.Height + 20f;

            var band = UIKit.Node("Slates", Safe);
            UIKit.StretchTo(band, 0f, bottom, 0f, top);
            band.gameObject.AddComponent<RectMask2D>();

            var list = UIKit.Node("List", band);
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(.5f, 1f);

            // Every finished row's light lives here, and this node is built first so all of
            // them are **under every card**. A pool hung off a row's own card would have to be
            // either a child — which draws over the card it is meant to light — or a sibling
            // inserted beside it, which draws over the row above, because a light worth seeing
            // reaches further than the fourteen units between two rows.
            var lights = UIKit.Node("Lights", list);
            lights.anchorMin = new Vector2(0f, 1f);
            lights.anchorMax = new Vector2(1f, 1f);
            lights.pivot = new Vector2(.5f, 1f);
            UIKit.StretchTo(lights, 0f, 0f, 0f, 0f);

            float y = 8f;
            int index = 0;
            foreach (var period in TaskPeriods.All)
            {
                y = BuildHeading(list, period, y);

                foreach (var task in TaskLedger.Active(period))
                {
                    BuildRow(list, lights, task, y, index++);
                    y += RowH + RowGap;
                }

                y += 18f;
            }

            list.sizeDelta = new Vector2(0f, y);
            list.anchoredPosition = Vector2.zero;

            var catcher = band.gameObject.AddComponent<Image>();
            catcher.color = new Color(0, 0, 0, 0);
            catcher.raycastTarget = true;

            var scroll = band.gameObject.AddComponent<ScrollRect>();
            scroll.content = list;
            scroll.viewport = band;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = .14f;
            scroll.inertia = true;
            scroll.decelerationRate = .04f;
            scroll.scrollSensitivity = 55f;

            Repaint();
        }

        float BuildHeading(RectTransform list, TaskPeriod period, float y)
        {
            bool weekly = period == TaskPeriod.Weekly;

            var title = UIKit.Titled("H_" + TaskPeriods.Id(period), list,
                                     Loc.Get(weekly ? "ui.tasks.weekly" : "ui.tasks.daily").ToUpperInvariant(),
                                     30, Pal.Gold, TextAnchor.MiddleLeft, new Vector2(420f, 40f),
                                     Top, new Vector2(-Width * .5f + 210f + 8f, -(y + HeadingH * .5f)), 3f, 3f);
            UIKit.Shrinkable(title, 18);

            // The tally sits beside the title and the clock at the far end: "2 / 3" is a
            // reading of the slate and the countdown is a fact about the calendar, and a
            // heading that put them together would read as one number.
            _tallies[period] = UIKit.Titled("T", list, string.Empty, 26, Pal.A(Pal.Cream, .9f),
                                            TextAnchor.MiddleLeft, new Vector2(160f, 34f), Top,
                                            new Vector2(-Width * .5f + 420f + 8f + 80f, -(y + HeadingH * .5f)), 0f, 0f);

            _clocks[period] = UIKit.Shrinkable(
                Scenery.Pill(list, ClockLine(period), 22, new Vector2(320f, 52f), Top,
                             new Vector2(Width * .5f - 160f, -(y + HeadingH * .5f)),
                             new Color(.05f, .09f, .18f, .78f), "ic_restart"), 14);

            return y + HeadingH;
        }

        /// <summary>
        /// One task, on the kit's card: the goal's glyph in a seat, the title, the bar and
        /// the chest it pays. Everything that changes with state is written by
        /// <see cref="Paint"/>; this builds the furniture once.
        /// </summary>
        void BuildRow(RectTransform list, RectTransform lights, TaskDefinition task, float y, int index)
        {
            var row = new Row { Task = task };

            // The pool a finished row stands in, built dark. See BuildSlates for why it is not
            // a child of the card.
            row.Pool = UIKit.Img("Light_" + task.Id, lights, Art.Glow(128, 1.35f), Pal.A(Pal.Sun, 0f),
                                 new Vector2(Width + 150f, RowH + 130f), Top,
                                 new Vector2(0f, -(y + RowH * .5f)));

            var card = UIKit.Img("Row_" + task.Id, list, Art.S("Ui/" + Skins.Card), Color.white,
                                 new Vector2(Width, RowH), Top, new Vector2(0f, -(y + RowH * .5f)));
            row.Card = card;
            row.Root = (RectTransform)card.transform;
            row.Group = UIKit.Group(row.Root);

            // The goal's picture, on the kit's own inset seat.
            var seat = UIKit.Img("Seat", row.Root, Art.S("Ui/" + Skins.Slot), Color.white,
                                 new Vector2(124f, 124f), Left, new Vector2(84f, 0f));
            var glyph = UIKit.Img("Glyph", seat.transform, Art.S(TaskGoals.Icon(task.Goal)), Color.white,
                                  new Vector2(82f, 82f), Centre, new Vector2(0f, 2f));
            glyph.preserveAspect = true;

            const float TextX = 166f;
            const float TextW = 520f;

            UIKit.Shrinkable(
                UIKit.Titled("Title", row.Root, task.Title, 29, Pal.Cream,
                             TextAnchor.MiddleLeft, new Vector2(TextW, 40f), Left,
                             new Vector2(TextX + TextW * .5f, 36f), 3f, 3f), 17);

            // The bar: the kit's trough and fill, left-pivoted so the fill grows from the
            // seat outward, and the count written beside it rather than on it.
            row.Track = TextW - 130f;
            var trough = UIKit.Img("Trough", row.Root, Art.S("Ui/" + Skins.Trough), Color.white,
                                   new Vector2(row.Track, 30f), Left, new Vector2(TextX + row.Track * .5f, -22f));
            var fill = UIKit.Img("Fill", trough.transform, Art.S("Ui/" + Skins.Fill), BarOrange,
                                 new Vector2(0f, BarH), Left, new Vector2(4f, 0f));
            row.Fill = (RectTransform)fill.transform;
            row.Fill.pivot = new Vector2(0f, .5f);
            row.Bar = fill;

            row.Count = UIKit.Shrinkable(
                UIKit.Titled("Count", row.Root, string.Empty, 24, Pal.Cream, TextAnchor.MiddleLeft,
                             new Vector2(124f, 32f), Left, new Vector2(TextX + row.Track + 12f + 62f, -22f), 3f, 3f), 14);

            row.Hint = UIKit.Shrinkable(
                UIKit.Titled("Hint", row.Root, string.Empty, 22, Pal.Gold, TextAnchor.MiddleLeft,
                             new Vector2(TextW, 30f), Left, new Vector2(TextX + TextW * .5f, -56f), 0f, 0f), 13);

            // The chest it pays, at the far end. Lit and breathing when it can be taken.
            var halo = UIKit.Img("Halo", row.Root, Art.Glow(128, 2f), Pal.A(Pal.Gold, 0f),
                                 new Vector2(230f, 230f), new Vector2(1f, .5f), new Vector2(-96f, 0f));
            row.Halo = (RectTransform)halo.transform;

            var chest = UIKit.Img("Chest", row.Root, Art.S(task.Tier.Icon), Color.white,
                                  new Vector2(96f, 133f), new Vector2(1f, .5f), new Vector2(-96f, 2f));
            chest.preserveAspect = true;
            row.Chest = chest;

            // The seal a paid task wears over its chest.
            var seal = UIKit.Img("Seal", row.Root, Art.Disc(96), Pal.Mint,
                                 new Vector2(64f, 64f), new Vector2(1f, .5f), new Vector2(-64f, -34f));
            var tick = UIKit.Img("Tick", seal.transform, Art.S("Ui/ic_check"), Color.white,
                                 new Vector2(38f, 38f), Centre, Vector2.zero);
            tick.preserveAspect = true;
            row.Seal = (RectTransform)seal.transform;
            row.Seal.gameObject.SetActive(false);

            // The rim, on the card's own edge and over everything on it — the half of the holy
            // light that has to be a child, because a light drawn *behind* a card the kit cuts
            // opaque is a light with a hole in the middle of it.
            row.Rim = UIKit.Img("Rim", row.Root, Art.RoundOutline(30, 7f), Pal.A(Pal.Sun, 0f));
            UIKit.StretchTo((RectTransform)row.Rim.transform, 0f, 0f, 0f, 0f);

            // The whole row is the button, and only while there is something to take.
            row.Tap = UIKit.Button("Tap", row.Root, Art.Pixel, new Vector2(Width, RowH), Centre,
                                   Vector2.zero, () => Claim(row));
            row.Tap.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            row.Tap.ClickSfx = null;
            row.Tap.PressScale = .97f;
            row.Tap.gameObject.SetActive(false);

            _rows.Add(row);

            row.Root.localScale = Vector3.zero;
            Tween.Pop(row.Root, 0f, .46f, .22f + index * .05f);
        }

        // -------------------------------------------------------------- painting
        /// <summary>
        /// Writes the ledger onto the rows and headings. No entrance and no rebuild: a run
        /// that finished behind this page moves a bar, and a bar moving is what a repaint
        /// is for.
        /// </summary>
        void Repaint()
        {
            foreach (var row in _rows) Paint(row);

            foreach (var period in TaskPeriods.All)
            {
                if (_tallies.TryGetValue(period, out var tally) && tally)
                    tally.text = Loc.Format("ui.tasks.fraction",
                                            TaskLedger.ClaimedCount(period),
                                            TaskLedger.Active(period).Count);

                if (_clocks.TryGetValue(period, out var clock) && clock)
                    clock.text = ClockLine(period);
            }
        }

        void Paint(Row row)
        {
            if (row == null || !row.Root) return;

            var task = row.Task;
            var state = TaskLedger.StateOf(task);
            int done = TaskLedger.Progress(task);
            float fill01 = Mathf.Clamp01(done / (float)task.Target);

            // Orange while it is counting, green the moment it is full. Only on the paint that
            // *changed* it, or a tint restarts on every counter that moves — and instantly the
            // first time, because a page opened on a finished task should find it already green
            // rather than watch it arrive.
            bool full = done >= task.Target;
            if (row.Bar && row.Full != full)
            {
                var colour = full ? BarFull : BarOrange;
                if (row.Full == null) row.Bar.color = colour;
                else Tween.Tint(row.Bar, colour, .35f);
                row.Full = full;
            }

            float target = (row.Track - 8f) * fill01;
            if (row.Fill)
            {
                Tween.KillChannel(row.Fill, "bar");
                float from = row.Fill.sizeDelta.x;
                Tween.Run(.6f, Ease.OutCubic,
                          t => { if (row.Fill) row.Fill.sizeDelta = new Vector2(Mathf.Lerp(from, target, t), BarH); },
                          row.Fill, "bar");
            }

            if (row.Count) row.Count.text = Loc.Format("ui.tasks.fraction", done, task.Target);

            bool ready = state == TaskState.Ready;
            bool claimed = state == TaskState.Claimed;

            if (row.Hint)
            {
                row.Hint.text = ready
                    ? (TaskLedger.CanClaim ? Loc.Get("ui.tasks.tap_to_claim") : Loc.Get("ui.chest.needs_connection"))
                    : claimed ? Loc.Get("ui.tasks.claimed")
                    : string.Empty;
                row.Hint.color = ready ? Pal.Gold : Pal.A(Pal.Cream, .7f);
            }

            if (row.Group) row.Group.alpha = claimed ? .62f : 1f;
            if (row.Seal) row.Seal.gameObject.SetActive(claimed);
            if (row.Tap) row.Tap.gameObject.SetActive(ready);

            // The chest lights and breathes only when it can be taken, and only starts
            // doing so on the paint that made it ready — a breathe restarted on every
            // repaint is a chest that jumps each time a counter moves.
            if (ready && row.Painted != TaskState.Ready)
            {
                if (row.Halo) Tween.Tint(row.Halo.GetComponent<Image>(), Pal.A(Pal.Gold, .55f), .4f);
                if (row.Chest)
                {
                    Tween.Breathe(row.Chest.transform, .07f, 1.5f);
                    Sheen.Attach(row.Root, 2.8f);
                }
                if (row.Card) row.Card.color = Color.white;
                Shine(row, true);
            }
            else if (!ready && row.Painted == TaskState.Ready)
            {
                if (row.Halo) Tween.Tint(row.Halo.GetComponent<Image>(), Pal.A(Pal.Gold, 0f), .3f);
                if (row.Chest) Tween.KillChannel(row.Chest.transform, "breathe");
                Shine(row, false);
            }

            if (claimed && row.Chest) row.Chest.color = new Color(.78f, .82f, .88f, 1f);

            row.Painted = state;
        }

        /// <summary>
        /// The light a finished task stands in: a warm pool that reaches past the card and a
        /// bright rim on its edge, breathing together on one tween.
        ///
        /// <para>
        /// <b>Two pieces rather than one, because a card is opaque.</b> A glow behind the kit's
        /// navy plate is a glow with a card-shaped hole punched out of the middle of it, and a
        /// glow in front of it washes out everything written on the row — so the light outside
        /// the card is a pool and the light on the card is its edge. Together they read as one
        /// thing lit from behind.
        /// </para>
        /// <para>
        /// <b>It breathes rather than flashing.</b> There can be six of these on the page at
        /// once — every task on both slates can be finished and unclaimed at midnight — so
        /// anything sharper than a slow swell is six things flashing at a player who is trying
        /// to read a list, which is 37h's rule about the one ward that may flash.
        /// </para>
        /// </summary>
        static void Shine(Row row, bool on)
        {
            if (row.Pool) Tween.KillChannel(row.Pool.transform, "holy");
            if (row.Rim) Tween.KillChannel(row.Rim.transform, "holy");

            if (!on)
            {
                if (row.Pool) Tween.Tint(row.Pool, Pal.A(Pal.Sun, 0f), .3f);
                if (row.Rim) Tween.Tint(row.Rim, Pal.A(Pal.Sun, 0f), .3f);
                return;
            }

            Tween.Run(1.8f, Ease.InOutSine, t =>
            {
                if (row.Pool) row.Pool.color = Pal.A(Pal.Sun, Mathf.Lerp(.45f, .85f, t));
                if (row.Rim) row.Rim.color = Pal.A(Pal.Radiance, Mathf.Lerp(.55f, 1f, t));
            }, row.Pool, "holy").Loop(-1, true);
        }

        string ClockLine(TaskPeriod period)
        {
            long left = TaskLedger.SecondsUntilReset(period);
            return Loc.Format("ui.tasks.resets_in",
                              period == TaskPeriod.Weekly ? Profile.LongCountdown(left) : Profile.Countdown(left));
        }

        /// <summary>Ticks the two reset clocks once a second. Only the labels.</summary>
        void Update()
        {
            _clockTick += Time.unscaledDeltaTime;
            if (_clockTick < 1f) return;
            _clockTick = 0f;

            foreach (var pair in _clocks)
                if (pair.Value) pair.Value.text = ClockLine(pair.Key);
        }

        // -------------------------------------------------------------- claiming
        /// <summary>
        /// Opens the chest. The ceremony claims it — the grant happens at the start of the
        /// overlay, not here — so a row is only ever asked whether it is still ready, and a
        /// second device that got there first is answered by the overlay closing itself.
        /// </summary>
        void Claim(Row row)
        {
            if (_claiming || Flow.HasModal || row == null) return;
            if (TaskLedger.StateOf(row.Task) != TaskState.Ready) return;

            if (!TaskLedger.CanClaim)
            {
                Scenery.Toast(Content, Loc.Get("ui.chest.needs_connection"), Pal.Rose, 3f);
                return;
            }

            _claiming = true;

            Audio.Sfx("collect", .6f);
            Tween.Punch(row.Root, .12f, .3f);
            Burst.Sparks(row.Chest.transform, Vector2.zero, Pal.Gold, 16, 300f, 26f, .6f);

            var overlay = Flow.Modal<ChestOverlay>(v => v.Claim = ChestClaim.ForTask(row.Task));

            // The ledger raised Changed inside the overlay's Build; the repaint was held back
            // by _claiming so the row did not turn grey under the ceremony. Paint it now,
            // while the scrim covers it, and let the next Changed through.
            _claiming = false;
            Repaint();

            if (overlay == null) return;
        }

        public override bool OnBack()
        {
            Flow.Go<HomeScreen>();
            return true;
        }
    }
}
