using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Ranks;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The rank ladder, rung by rung: the badge a keeper wears now, and exactly what every one
    /// of them asks for.
    ///
    /// <para>
    /// <b>The page exists because a badge with no requirements beside it is a puzzle.</b> A rank
    /// drawn on the map says what somebody has and nothing about what it took or what is next,
    /// and a progression display that cannot be interrogated reads as decoration. So every rung
    /// prints every line it asks for, with the player's own figure against each — which is also
    /// what makes the whole ladder honest: there is nothing here a player cannot check.
    /// </para>
    ///
    /// <para>
    /// <b>A locked rung is a darker plate, never a fainter one.</b> The first cut of this page
    /// drew every unearned card at 38% alpha, which is the reflex and is wrong twice over: a
    /// see-through card reads as art that failed to load rather than as a thing not yet won
    /// (invariant 7b's neighbour — a half-drawn plate says "broken", not "later"), and it takes
    /// the whole bottom of the page down with it, which is four of the seven rungs on a phone
    /// and the only reason anybody scrolls. So <em>nothing on this page is transparent</em>: an
    /// unearned rung stands on <see cref="Skins.Panel"/>, which is a solid, muted navy plainly
    /// below <see cref="Skins.PlateNavy"/> in value, its wells are full strength, and its ink is
    /// an opaque steel. What says "not yet" is the plate's own value, the padlock in the answer
    /// column and the drained ordinal chip — three solid things instead of one faded one.
    /// </para>
    /// <para>
    /// <b>And the badge is never dimmed at all</b>, locked or not. It is the thing the player is
    /// working toward and the only reason to scroll; a page of seven bright medallions down a
    /// ladder is a trophy case, which is what this screen is for.
    /// </para>
    ///
    /// <para>
    /// <b>Every rung wears its own metal</b> (<see cref="RankLook"/>). The badges are a copper,
    /// silver, gold, violet, crimson, ice and prism ramp and the page used to throw all of it
    /// away onto one navy plate with one gold rim seven times; now the accent is spent on the
    /// seat's rim, the glow behind the badge, the ordinal chip, the hairline, the unmet marks
    /// and the link down to the next rung — never on the badge, which is finished art
    /// (invariant 44g).
    /// </para>
    /// <para>
    /// <b>The rungs are chained.</b> A short link in each gap joins one badge seat to the next,
    /// lit through the run a player holds and dark above it, so the column of badges reads as a
    /// ladder climbed from the bottom rather than as seven unrelated cards stacked up
    /// (<c>render_ranks.py</c> was written to ask exactly that question and the answer was no).
    /// The hero repeats it in miniature: every rung in the game as one strip of seats, filled as
    /// far as the player has come.
    /// </para>
    ///
    /// <para>
    /// <b>Three states and one of them shines.</b> Earned rungs carry a tick and a burst behind
    /// the badge, the rung being climbed carries a pool of light, a bright rim and the only
    /// progress bar on the page, and everything above it is a muted plate behind a padlock.
    /// Exactly one row shines, which is what makes a glance at the page enough — seven glowing
    /// rows would say nothing at all.
    /// </para>
    /// <para>
    /// <b>The answer column carries one answer per row and the first of them is a word</b>
    /// (invariant 48g). A climbing row used to carry <em>nothing</em> there, which left a hole
    /// down the one column a reader scans; it shows how far up the rung stands now. The bar at
    /// the foot is the fine reading of the same number and the chip is the coarse one, which is
    /// the ordinary division between a column and a gauge.
    /// </para>
    ///
    /// <para>
    /// <b>Built once and repainted from the ledger</b> (<c>CRAFT.md</c>: Show animates, Refresh
    /// does not). A rank can move while this page is standing — a merge landing another device's
    /// battles is the ordinary case — and the one thing a repaint cannot do is change which
    /// rungs exist, so a retuned ladder rebuilds whole.
    /// </para>
    /// <para>
    /// <b>No nav bar, and a back key to the map.</b> This is a side page off
    /// <see cref="LevelsScreen"/>, which has no bar either, and it is reached from that screen's
    /// badge — so the loop is map, page, map. <c>MapMemory</c> puts the player back on the
    /// chapter they left (invariant 8b), which is why the back key needs no argument.
    /// </para>
    /// </summary>
    public sealed class RanksScreen : View
    {
        public override string Track => "mus_menu";

        // The stack, in canvas units from the top of the safe area.
        const float ChromeSize = 92f;
        const float BannerH = 138f;

        /// <summary>
        /// The hero, which grew to carry the pip strip. See <see cref="BuildPips"/>: the strip is
        /// the one control on the page that answers "how far up am I" without scrolling, and a
        /// band it has to share is a band it loses.
        /// </summary>
        const float HeroH = 420f;

        /// <summary>
        /// How wide a card is drawn, against the 1080 this game is designed at.
        ///
        /// <b>Nearly full bleed on purpose.</b> At 1000 the page read as a column of chips on a
        /// wall; a rank card is the most important thing on its own screen and should take the
        /// screen, which is also what makes room for a badge big enough to see the metal on.
        /// </summary>
        const float Width = 1024f;

        /// <summary>A row's fixed part, what each requirement line adds, and its plain foot.</summary>
        /// <remarks>
        /// <b>The gap is wide enough to hang a link in</b>, which is the only reason it is 28 and
        /// not the 20 every other list on this page's shelf uses — see <see cref="BuildRow"/>.
        /// </remarks>
        const float RowHead = 214f, LineH = 66f, RowFoot = 26f, RowGap = 28f;

        /// <summary>
        /// One requirement's own well, and the furniture in it.
        ///
        /// <b>Every line sits in a trough rather than floating on the plate</b>, which is the
        /// single change that made this page read as a game rather than as a list. A checklist
        /// of bare sentences on a card is a paragraph; the same sentences in wells are rows a
        /// player counts. The well is inset from the card so the plate's own moulding still
        /// frames them.
        /// </summary>
        const float WellInset = 30f, WellH = 56f;

        /// <summary>
        /// The band under the last requirement line, which is the bar's seat.
        ///
        /// <b>Every row pays for it, including the ones that never draw a bar</b>, and that is
        /// the point: only the rung being climbed shows one, and which rung that is changes
        /// while the page is standing — a repaint cannot move a row that has already been laid
        /// out, so a row sized without room for a bar would have the bar printed through its own
        /// last line the moment it became the one being climbed. `render_ranks.py` drew exactly
        /// that before this was a band. Trough plus air above and below.
        ///
        /// <para>
        /// <b>Only the row being climbed pays for it.</b> Every other row gets
        /// <see cref="RowFoot"/>, because a reserved band with no bar in it is dead space and
        /// there were five of them down the page. That is affordable because the page is rebuilt
        /// whenever the held rung moves (<see cref="LadderKey"/>) — a promotion happens a
        /// handful of times in an account's life, and the card's <em>shape</em> genuinely depends
        /// on which rung is being climbed, so rebuilding is the honest answer rather than a
        /// dodge.
        /// </para>
        /// </summary>
        const float BarBand = 84f;

        /// <summary>The badge, and the moulded seat it stands in.</summary>
        const float BadgeSize = 176f, BadgeSeat = 198f;

        /// <summary>The hero's badge, and where its writing starts from the plate's left edge.</summary>
        /// <remarks>
        /// Derived rather than typed at three call sites, because `UIKit.Box` always pivots at
        /// centre and every one of those sites has to add half its own width to it — which is
        /// exactly the arithmetic that put the update wall's sentence 89 units out (49h).
        /// </remarks>
        const float HeroBadge = 268f, HeroBadgeX = 216f;
        const float TextX = HeroBadgeX + HeroBadge * .5f + 40f;

        /// <summary>
        /// One seat on the hero's strip, the air between two of them, and the picture inside.
        ///
        /// <b>Sized from the room rather than typed</b>, because the ladder is content: seven
        /// rungs fit at these figures with air to spare, and a ladder long enough to overrun the
        /// plate closes the gaps and then the seats instead of drawing off the edge. See
        /// <see cref="BuildPips"/>.
        /// </summary>
        const float PipSeat = 68f, PipGap = 22f, PipFace = .84f;

        /// <summary>The link hung in the gap between two rungs. See <see cref="BuildRow"/>.</summary>
        const float LinkWidth = 14f;

        static readonly Vector2 Top = new Vector2(.5f, 1f);
        static readonly Vector2 Left = new Vector2(0f, .5f);
        static readonly Vector2 Centre = new Vector2(.5f, .5f);

        /// <summary>
        /// The bar's orange, pre-divided against <see cref="Skins.Fill"/> exactly as
        /// <c>TasksScreen.BarOrange</c> is, and copied rather than shared for that field's own
        /// reason: it is not a colour, it is what the kit's off-white fill has to be multiplied
        /// by to come out orange, and it may not be reasoned about — only drawn.
        /// </summary>
        /// <remarks>
        /// <b>Deliberately not the rung's own metal</b> (<see cref="RankLook"/>), which was tried
        /// and is the one place the accent must not go: a bar is read for its <em>length</em>,
        /// and silver at rung 2 or ice at rung 6 against this trough is a fill you have to look
        /// for. Gold is what this game already fills a bar with everywhere else (45h), and the
        /// rim, the chip and the seat are carrying the rank's colour a hand's width above it.
        /// </remarks>
        static readonly Color BarOrange = new Color(1f, .588f, .118f, 1f);

        /// <summary>The bar's trough and the fill inside it, both grown with the rest of the page.</summary>
        const float BarTrough = 36f, BarH = 32f;

        /// <summary>
        /// A locked rung's writing: a cool steel, and <b>opaque</b>.
        ///
        /// It was cream at 48% alpha, which is the same mistake the plate made one size down —
        /// faded ink over a faded card is two thin things on top of each other, and a caption
        /// that is hard to read is a caption a player assumes is broken rather than one they
        /// understand to be out of reach. Recede by <em>value</em>, at full strength.
        /// </summary>
        static readonly Color LockedInk = Pal.Hex("#93A6C4");

        /// <summary>A locked rung's own name, one step brighter than its lines. See <see cref="LockedInk"/>.</summary>
        static readonly Color LockedName = Pal.Hex("#C0CFE4");

        /// <summary>One rung's row, kept so a repaint can write onto it.</summary>
        sealed class Row
        {
            public RankDefinition Rung;
            public Color Metal;
            public RectTransform Root;
            public Image Plate;
            public Image Badge;
            public Image Burst;
            public Image Halo;
            public Image SeatRim;
            public Image Rim;
            public Image Pool;
            public Image Seal;
            public Image Lock;
            public Image Standing;
            public Text Percent;
            public Image Link;
            public Image Chip;
            public Image ChipRim;
            public Text Ordinal;
            public Text Name;
            public Text Blurb;
            public Image Rule;
            public readonly List<Image> Wells = new List<Image>();
            public RectTransform Bar;
            public readonly List<Text> Lines = new List<Text>();
            public readonly List<Text> Counts = new List<Text>();
            public readonly List<Image> Marks = new List<Image>();

            /// <summary>
            /// Whether the light is running on this row right now.
            ///
            /// The thing being turned on and off rather than a state it agrees with, for
            /// <c>TasksScreen.Row.Lit</c>'s reason: a guard on a state is a guard that stops
            /// firing the moment two states stop meaning the same thing.
            /// </summary>
            public bool Lit;
        }

        readonly List<Row> _rows = new List<Row>();

        Image _heroPlate;
        Image _heroBadge;
        Image _heroRays;
        Image _heroGlow;
        Image _heroRim;
        Image _heroRule;
        Text _heroKicker;
        Text _heroName;
        Text _heroBlurb;
        Text _heroCount;

        /// <summary>The hero's strip: one seat per rung, the picture in it, and its rim.</summary>
        readonly List<Image> _pipBadges = new List<Image>();
        readonly List<Image> _pipRims = new List<Image>();

        /// <summary>The rung ids this page was built for, so a retuned ladder rebuilds whole.</summary>
        string _built;

        protected override void Build()
        {
            _rows.Clear();
            _pipBadges.Clear();
            _pipRims.Clear();
            _heroBadge = null;

            // The profile's ground rather than the hub's: this is a page made of plates laid
            // across the width, so a painted *place* under it would only ever show in the gaps.
            // `TasksScreen.Build` records the distinction.
            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 18, new Color(1f, .93f, .70f), 6f, 22f);

            _built = LadderKey();

            float y = 22f;
            y = BuildHeader(y);
            y = BuildHero(y);
            BuildList(y);
        }

        void OnEnable() { RankLedger.Changed += OnChanged; }
        void OnDisable() { RankLedger.Changed -= OnChanged; }

        public override bool OnBack() { Flow.Go<LevelsScreen>(); return true; }

        /// <summary>
        /// The ladder as a string, so a content push that changes which rungs exist is
        /// distinguishable from one that only moves a target. The first is a different page and
        /// the second is a repaint.
        /// </summary>
        /// <remarks>
        /// <b>The held ordinal is part of it</b>, not only the rung ids: the row being climbed is
        /// the only one that reserves a seat for a bar (<see cref="BarBand"/>), so a promotion
        /// changes the <em>shape</em> of two cards and not just their colours. A repaint cannot
        /// move a row that has already been laid out, so that is a rebuild.
        /// </remarks>
        string LadderKey()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var rung in RankLedger.Ladder.Rungs) sb.Append(rung.Id).Append('|');
            return sb.Append('#').Append(RankLedger.Ordinal).ToString();
        }

        void OnChanged()
        {
            if (!Living) return;

            if (LadderKey() != _built) { ClearContent(); Build(); return; }
            Repaint();
        }

        // ------------------------------------------------------------------ chrome
        float BuildHeader(float y)
        {
            float cy = -(y + BannerH * .5f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * ChromeSize,
                             new Vector2(0f, 1f), new Vector2(76f, cy),
                             () => Flow.Go<LevelsScreen>());

            var ribbon = Scenery.TitleRibbon(Safe, Loc.Get("ui.ranks.title").ToUpperInvariant(),
                                             new Vector2(720f, BannerH), Top, new Vector2(0f, cy), 42);
            ribbon.transform.localScale = Vector3.zero;
            Tween.Pop(ribbon.transform, 0f, .5f, .06f);
            y += BannerH + 4f;

            UIKit.Shrinkable(
                UIKit.Titled("Sub", Safe, Loc.Get("ui.ranks.subtitle"), 24,
                             new Color(.86f, .90f, 1f, .78f), TextAnchor.MiddleCenter,
                             new Vector2(880f, 32f), Top, new Vector2(0f, -(y + 16f)), 3f, 3f), 15);

            return y + 32f + 16f;
        }

        /// <summary>
        /// The badge the player wears now, large, in its own metal, over a strip of every rung
        /// there is.
        ///
        /// <para>
        /// It repeats the first row of the list when the first rung is held, and that repetition
        /// is the point: the question the page is opened with is "what am I", and making
        /// somebody find their own row in a list of seven to answer it is a page that starts
        /// with a search.
        /// </para>
        /// </summary>
        float BuildHero(float y)
        {
            _heroPlate = UIKit.Img("Hero", Safe, Art.S("Ui/" + Skins.Panel), Color.white,
                                   new Vector2(Width, HeroH), Top, new Vector2(0f, -(y + HeroH * .5f)));
            var plate = _heroPlate.transform;

            var clip = UIKit.Node("Clip", plate);
            UIKit.StretchTo(clip, 8f, 8f, 8f, 8f);
            clip.gameObject.AddComponent<RectMask2D>();

            _heroRays = UIKit.Img("Rays", clip, Art.Rays(512, 14), Pal.A(Pal.Sun, .17f),
                                  new Vector2(HeroH * 2.3f, HeroH * 2.3f), Centre, new Vector2(0f, 24f));
            _heroRays.raycastTarget = false;

            float left = -Width * .5f;
            float bx = left + HeroBadgeX;
            const float by = 34f;

            _heroGlow = UIKit.Img("Glow", plate, Art.Glow(128, 2.1f), Pal.A(Pal.Sun, .30f),
                                  Vector2.one * (HeroBadge * 1.5f), Centre, new Vector2(bx, by));

            // The seat, so the badge stands in the plate rather than on it. The kit's slot is
            // the same moulding the action bar's cells wear, which is what makes a picture
            // dropped into it read as equipment rather than as a sticker.
            var seat = UIKit.Img("Seat", plate, Art.S("Ui/" + Skins.Slot), Color.white,
                                 Vector2.one * (HeroBadge * 1.12f), Centre, new Vector2(bx, by));
            seat.raycastTarget = false;

            // The same metal rim every card's seat wears, so the hero reads as one of them made
            // large rather than as a different object that happens to hold a badge.
            _heroRim = UIKit.Img("SeatRim", plate, Art.RoundOutline(24, 6f), Pal.Sun,
                                 Vector2.one * (HeroBadge * 1.12f), Centre, new Vector2(bx, by));

            _heroBadge = UIKit.Img("Badge", plate, null, Color.white,
                                   Vector2.one * HeroBadge, Centre, new Vector2(bx, by));
            _heroBadge.preserveAspect = true;

            float textX = left + TextX;
            float textW = Width * .5f - textX - 40f;

            _heroKicker = UIKit.Shrinkable(
                UIKit.Titled("Kicker", plate, Loc.Get("ui.ranks.mark"), 24,
                             Pal.A(Pal.Sun, .85f), TextAnchor.MiddleLeft,
                             new Vector2(textW, 30f), Centre,
                             new Vector2(textX + textW * .5f, 148f), 0f, 2f), 14);

            _heroName = UIKit.Shrinkable(
                UIKit.Titled("Name", plate, string.Empty, 60, Pal.Gold,
                             TextAnchor.MiddleLeft, new Vector2(textW, 74f), Centre,
                             new Vector2(textX + textW * .5f, 84f)), 28);

            _heroBlurb = UIKit.Shrinkable(
                UIKit.Titled("Blurb", plate, string.Empty, 28, Pal.A(Pal.Cream, .84f),
                             TextAnchor.UpperLeft, new Vector2(textW, 92f), Centre,
                             new Vector2(textX + textW * .5f, 4f), 0f, 2f, wrap: true), 18);

            _heroCount = UIKit.Shrinkable(
                Scenery.Pill(plate, string.Empty, 28, new Vector2(textW, 62f), Centre,
                             new Vector2(textX + textW * .5f, -74f),
                             new Color(.05f, .09f, .18f, .80f), "ic_star"), 17);

            _heroRule = UIKit.Img("Rule", plate, Art.Pixel, Pal.A(Pal.Sun, .18f),
                                  new Vector2(Width - WellInset * 2f, 2f), Centre,
                                  new Vector2(0f, -116f));

            BuildPips(plate, -160f);

            return y + HeroH + 22f;
        }

        /// <summary>
        /// Every rung in the game as one strip of seats, filled as far as the player has come.
        ///
        /// <para>
        /// <b>It is the only thing on the page that answers "how far up am I" without
        /// scrolling</b>, which on a phone is four of seven rungs away. The pill above it says
        /// the same thing in words and the two are not redundant: "3 of 7" is a fact and a strip
        /// three-sevenths full is a shape, and a shape is what a glance reads.
        /// </para>
        /// <para>
        /// <b>An unearned seat is drawn empty rather than dimmed</b>, which is the same rule the
        /// cards follow one size up: a strip of seven bright badges says nothing at all, and a
        /// strip of seven faint ones says the art is broken. A hole is unambiguous.
        /// </para>
        /// </summary>
        void BuildPips(Transform plate, float cy)
        {
            var ladder = RankLedger.Ladder;
            int n = ladder.Count;
            if (n <= 0) return;

            // Sized from the room rather than typed: the ladder is content and a longer one has
            // to fit on the same plate. Close the air first, then the seats.
            float room = Width - 80f;
            float pitch = Mathf.Min(PipSeat + PipGap, room / n);
            float seat = Mathf.Min(PipSeat, pitch - 8f);
            float x0 = -pitch * (n - 1) * .5f;

            for (int i = 0; i < n; i++)
            {
                var rung = ladder.At(i + 1);
                float x = x0 + pitch * i;

                UIKit.Img("PipSeat", plate, Art.S("Ui/" + Skins.Slot), Color.white,
                          Vector2.one * seat, Centre, new Vector2(x, cy));

                var badge = UIKit.Img("Pip", plate, rung != null ? Art.S(rung.Icon) : null,
                                      Color.white, Vector2.one * (seat * PipFace), Centre,
                                      new Vector2(x, cy));
                badge.preserveAspect = true;
                _pipBadges.Add(badge);

                var rim = UIKit.Img("PipRim", plate, Art.RoundOutline(18, 4f),
                                    Pal.A(Pal.Sun, 0f), Vector2.one * (seat + 6f), Centre,
                                    new Vector2(x, cy));
                _pipRims.Add(rim);
            }
        }

        // ------------------------------------------------------------------ the ladder
        /// <summary>
        /// Every rung in one scrolling band down to the bottom of the screen. The band is
        /// measured rather than assumed, exactly as the task slates are: on a tall phone the
        /// list may fit and sit still, on a short one it scrolls, and neither shape is written
        /// down.
        /// </summary>
        void BuildList(float top)
        {
            var band = UIKit.Node("Ladder", Safe);
            UIKit.StretchTo(band, 0f, 20f, 0f, top);
            band.gameObject.AddComponent<RectMask2D>();

            var list = UIKit.Node("List", band);
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(.5f, 1f);

            // Every row's pool and every link lives here, built first so all of them are **under
            // every plate**. A light hung off a row's own plate would have to be a child, which
            // draws over the plate it is meant to light, or a sibling, which draws over the row
            // above — the arrangement `TasksScreen.BuildSlates` records.
            var lights = UIKit.Node("Lights", list);
            lights.anchorMin = new Vector2(0f, 1f);
            lights.anchorMax = new Vector2(1f, 1f);
            lights.pivot = new Vector2(.5f, 1f);
            UIKit.StretchTo(lights, 0f, 0f, 0f, 0f);

            var ladder = RankLedger.Ladder;

            float y = 4f;
            if (ladder.IsEmpty)
            {
                // The honest answer to a content file with no ladder in it. There is no built-in
                // one to fall back to (`RankLadder`), so there is nothing true to draw.
                UIKit.Shrinkable(
                    UIKit.Titled("None", list, Loc.Get("ui.ranks.unranked_blurb"), 26,
                                 Pal.A(Pal.Cream, .7f), TextAnchor.MiddleCenter,
                                 new Vector2(Width, 80f), Top, new Vector2(0f, -(y + 40f)),
                                 0f, 2f, wrap: true), 16);
                y += 96f;
            }
            else
            {
                int last = ladder.Count - 1;
                for (int i = 0; i < ladder.Count; i++)
                {
                    y += BuildRow(list, lights, ladder.At(i + 1), y, i < last) + RowGap;
                }
            }

            list.sizeDelta = new Vector2(0f, y);
            list.anchoredPosition = Vector2.zero;

            var catcher = band.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
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

        /// <summary>One rung. Returns how tall it came out, which its own line count decides.</summary>
        float BuildRow(RectTransform list, RectTransform lights, RankDefinition rung, float y,
                       bool chained)
        {
            var lines = rung.Requirements;

            // See `BarBand`: the bar's seat belongs to the one row that draws a bar, and the
            // page is rebuilt when that moves.
            bool climbing = ReferenceEquals(RankLedger.Next, rung);
            float height = RowHead + lines.Count * LineH + (climbing ? BarBand : RowFoot);
            float cy = -(y + height * .5f);

            var row = new Row { Rung = rung, Metal = RankLook.Metal(rung) };

            row.Pool = UIKit.Img("Light_" + rung.Id, lights, Art.Glow(128, 1.35f), Pal.A(Pal.Sun, 0f),
                                 new Vector2(Width + 280f, height + 200f), Top, new Vector2(0f, cy));

            float left = -Width * .5f;
            float badgeX = left + WellInset + BadgeSeat * .5f;

            // **The link down to the next rung**, hung in the gap under the badge column. It is
            // the whole reason the gap is 28 rather than 20: a chain of seats reads as a ladder
            // climbed from the bottom where seven stacked cards read as seven cards, and there
            // is nowhere else on a full-bleed plate to draw one. It belongs to the row above it
            // so the run lights from the bottom without any row knowing what is under it.
            if (chained)
            {
                row.Link = UIKit.Img("Link_" + rung.Id, lights, Art.Capsule(14, 36),
                                     Pal.A(Pal.Cream, .12f),
                                     new Vector2(LinkWidth, RowGap + 12f), Top,
                                     new Vector2(badgeX, cy - height * .5f - RowGap * .5f));
            }

            row.Plate = UIKit.Img("Row_" + rung.Id, list, Art.S("Ui/" + Skins.PlateNavy), Color.white,
                                  new Vector2(Width, height), Top, new Vector2(0f, cy));
            row.Root = (RectTransform)row.Plate.transform;

            // The rim is drawn over the plate rather than behind it. It went behind once on the
            // streak board and was simply invisible: the kit's plates are opaque (invariant 48i).
            row.Rim = UIKit.Img("Rim", row.Root, Art.RoundOutline(30, 8f), Pal.A(row.Metal, 0f));
            UIKit.StretchTo((RectTransform)row.Rim.transform, 0f, 0f, 0f, 0f);

            float headY = height * .5f - RowHead * .5f;

            // The light behind an earned badge, which is what makes a held rank read as *won*
            // rather than as ticked off. Drawn before the seat so it comes out from behind the
            // moulding.
            //
            // <b>A fan and not the kit's starburst, and the reason is the card's own edge.</b>
            // `Skins.Badge` was tried first and fails twice over: it is a chunky eight-pointed
            // star with a hard rim, so at any size that reads as rays it overhangs the plate by
            // its points and lands on the wall — the badge column is inset only
            // <see cref="WellInset"/> — and at half strength it composites to a grey-blue haze
            // over `PlateNavy`'s saturated blue, which is a smudge rather than light (invariant
            // 44g arriving through the alpha instead of the tint). `Art.Rays` fades to nothing
            // at its own rim, so it may be drawn larger than the plate and simply is not there
            // when it gets to the edge.
            row.Burst = UIKit.Img("Burst", row.Root, Art.Rays(256, 16), Pal.A(row.Metal, 0f),
                                  Vector2.one * (BadgeSeat * 1.60f), Centre, new Vector2(badgeX, headY));
            row.Burst.preserveAspect = true;

            row.Halo = UIKit.Img("Glow", row.Root, Art.Glow(128, 2.1f), Pal.A(row.Metal, .18f),
                                 Vector2.one * (BadgeSeat * 1.34f), Centre, new Vector2(badgeX, headY));

            var seat = UIKit.Img("Seat", row.Root, Art.S("Ui/" + Skins.Slot), Color.white,
                                 Vector2.one * BadgeSeat, Centre, new Vector2(badgeX, headY));
            seat.raycastTarget = false;

            // The seat's own rim in the rung's metal — the piece that does most of the work of
            // telling seven cards apart, because it is a hard edge on a dark hole rather than a
            // wash over a bright plate. A tint over the plate was tried and silver over
            // `PlateNavy` is invisible.
            row.SeatRim = UIKit.Img("SeatRim", row.Root, Art.RoundOutline(24, 5f), row.Metal,
                                    Vector2.one * BadgeSeat, Centre, new Vector2(badgeX, headY));

            // **Never dimmed, in any state.** See the class remarks: the badge is the reason to
            // scroll and a page of bright medallions is a trophy case.
            row.Badge = UIKit.Img("Badge", row.Root, Art.S(rung.Icon), Color.white,
                                  Vector2.one * BadgeSize, Centre, new Vector2(badgeX, headY));
            row.Badge.preserveAspect = true;

            // The right end carries one answer, and the first of them is a word — the streak
            // board's rule (invariant 48g), which is what stops three glyphs meaning three
            // different things in the same column.
            float answerX = Width * .5f - WellInset - AnswerSize * .5f;

            row.Seal = UIKit.Img("Seal", row.Root, Art.Disc(96), Pal.Mint,
                                 Vector2.one * AnswerSize, Centre, new Vector2(answerX, headY));
            var tick = UIKit.Img("Tick", row.Seal.transform, Art.S("Ui/ic_check"), Color.white,
                                 Vector2.one * (AnswerSize * .58f), Centre, Vector2.zero);
            tick.preserveAspect = true;

            // A padlock in a chip rather than a padlock floating on a plate. `Skins.Resting` is
            // the kit's own "not a control" square, which is exactly what this is.
            row.Lock = UIKit.Img("Lock", row.Root, Art.S("Ui/" + Skins.Resting), Color.white,
                                 Vector2.one * AnswerSize, Centre, new Vector2(answerX, headY));
            var shackle = UIKit.Img("Shackle", row.Lock.transform, Art.S("Ui/ic_lock"), LockedInk,
                                    Vector2.one * (AnswerSize * .56f), Centre, Vector2.zero);
            shackle.preserveAspect = true;

            // And the climbing row's answer: how far up this rung stands. Before this the one
            // column a reader scans had a hole in it on the one row they came to look at.
            row.Standing = UIKit.Img("Standing", row.Root, Art.S("Ui/" + Skins.Slot), Color.white,
                                     Vector2.one * AnswerSize, Centre, new Vector2(answerX, headY));
            var standRim = UIKit.Img("Rim", row.Standing.transform, Art.RoundOutline(20, 4f), row.Metal);
            UIKit.StretchTo((RectTransform)standRim.transform, 0f, 0f, 0f, 0f);
            row.Percent = UIKit.Shrinkable(
                UIKit.Titled("Pct", row.Standing.transform, string.Empty, 30, row.Metal,
                             TextAnchor.MiddleCenter, new Vector2(AnswerSize - 12f, 40f), Centre,
                             Vector2.zero, 0f, 2f), 16);

            float textX = badgeX + BadgeSeat * .5f + 28f;
            float textW = (answerX - AnswerSize * .5f - 24f) - textX;

            // **The ordinal, above the name, and in a chip rather than on the plate.** A ladder
            // wants a number on it: "RANK 3" says where this badge sits without the player
            // counting rows, and it is the one line on the card that is true whether or not the
            // rung is held. It is the rung's metal on a dark chip rather than metal ink on the
            // plate, because a pale metal written straight onto `PlateNavy` is the yellow-bar-on-
            // a-yellow-card fault (`Skins.Buy`) with a different pair of colours.
            //
            // **The radius is 14 and not a capsule's 20**, which is invariant 44a in miniature:
            // `Art.Round(20)` is a 48-unit bitmap whose nine-slice borders come to 46, so drawn
            // 40 tall it has no middle left to repeat and both curves are squashed into each
            // other. 14 leaves 34 of the 40, so the corner is the corner it was drawn as.
            row.Chip = UIKit.Img("Chip", row.Root, Art.Round(14), new Color(0f, 0f, 0f, .40f),
                                 new Vector2(ChipWidth, ChipHeight), Centre,
                                 new Vector2(textX + ChipWidth * .5f, headY + 66f));
            row.ChipRim = UIKit.Img("ChipRim", row.Chip.transform, Art.RoundOutline(14, 3f), row.Metal);
            UIKit.StretchTo((RectTransform)row.ChipRim.transform, 0f, 0f, 0f, 0f);

            row.Ordinal = UIKit.Shrinkable(
                UIKit.Titled("Ordinal", row.Chip.transform,
                             Loc.Format("ui.ranks.ordinal", rung.Ordinal), 22, row.Metal,
                             TextAnchor.MiddleCenter, new Vector2(ChipWidth - 16f, 28f), Centre,
                             Vector2.zero, 0f, 2f), 13);

            row.Name = UIKit.Shrinkable(
                UIKit.Titled("Name", row.Root, rung.Name, 50, Pal.Gold, TextAnchor.MiddleLeft,
                             new Vector2(textW, 58f), Centre,
                             new Vector2(textX + textW * .5f, headY + 12f)), 24);

            row.Blurb = UIKit.Shrinkable(
                UIKit.Titled("Blurb", row.Root, Loc.Get(rung.BlurbKey), 26, Pal.A(Pal.Cream, .76f),
                             TextAnchor.UpperLeft, new Vector2(textW, 62f), Centre,
                             new Vector2(textX + textW * .5f, headY - 52f), 0f, 2f, wrap: true), 16);

            // A hairline between the head and the checklist, so the card reads as two zones
            // rather than as one crowded one, and in the rung's metal so it is one more place
            // the colour lands. Barely there: a rule that can be *seen* is a rule that competes
            // with the plate's own moulding.
            row.Rule = UIKit.Img("Rule", row.Root, Art.Pixel, Pal.A(row.Metal, .30f),
                                 new Vector2(Width - WellInset * 2f, 2f), Centre,
                                 new Vector2(0f, height * .5f - RowHead + 6f));

            float wellW = Width - WellInset * 2f;
            float lineY = height * .5f - RowHead - LineH * .5f;

            foreach (var line in lines)
            {
                var well = UIKit.Img("Well", row.Root, Art.S("Ui/" + Skins.Trough), Color.white,
                                     new Vector2(wellW, WellH), Centre, new Vector2(0f, lineY));
                row.Wells.Add(well);

                float wellLeft = -wellW * .5f;

                // **A ring rather than a star**, and the whole reason is that a star is a
                // currency on this very page: a line reading "Earn 60 stars" with a star in
                // front of it says two different things with one glyph, which the render caught
                // and no fixture could. A tick replaces the ring when the line is met.
                var mark = UIKit.Img("M", well.transform, Art.Ring(64, 9f), Pal.A(row.Metal, .55f),
                                     Vector2.one * MarkSize, Centre,
                                     new Vector2(wellLeft + 26f + MarkSize * .5f, 0f));
                mark.preserveAspect = true;
                row.Marks.Add(mark);

                float saidX = wellLeft + 26f + MarkSize + 22f;
                float saidW = wellW - (saidX - wellLeft) - CountWidth - 34f;

                row.Lines.Add(UIKit.Shrinkable(
                    UIKit.Titled("L", well.transform, line.Sentence(GameContent.Index), 30, Pal.Cream,
                                 TextAnchor.MiddleLeft, new Vector2(saidW, WellH - 10f), Centre,
                                 new Vector2(saidX + saidW * .5f, 0f), 0f, 2f), 17));

                row.Counts.Add(UIKit.Shrinkable(
                    UIKit.Titled("C", well.transform, string.Empty, 32, Pal.Gold,
                                 TextAnchor.MiddleRight, new Vector2(CountWidth, WellH - 10f), Centre,
                                 new Vector2(wellW * .5f - 26f - CountWidth * .5f, 0f), 0f, 2f), 18));

                lineY -= LineH;
            }

            // The only bar on the page, and only the rung being climbed ever shows it.
            var trough = UIKit.Img("Trough", row.Root, Art.S("Ui/" + Skins.Trough), Color.white,
                                   new Vector2(wellW, BarTrough), Centre,
                                   new Vector2(0f, -height * .5f + BarBand * .5f));
            trough.gameObject.SetActive(climbing);

            var fill = UIKit.Img("Fill", trough.transform, Art.S("Ui/" + Skins.Fill), BarOrange,
                                 new Vector2(0f, BarH), Left, new Vector2(3f, 0f));
            row.Bar = (RectTransform)fill.transform;
            row.Bar.pivot = new Vector2(0f, .5f);

            _rows.Add(row);
            return height;
        }

        /// <summary>The checklist's own furniture, named because three call sites place against it.</summary>
        const float MarkSize = 34f, CountWidth = 200f, AnswerSize = 88f;

        /// <summary>The ordinal's chip. Wide enough for "RANK 10" before the caption shrinks.</summary>
        const float ChipWidth = 136f, ChipHeight = 40f;

        // ------------------------------------------------------------------ the repaint
        /// <summary>
        /// Writes the state onto rows that already exist, replaying no entrance — a rank moving
        /// is a redraw and not a new page (<c>CRAFT.md</c>: Show animates, Refresh does not).
        /// </summary>
        void Repaint()
        {
            var ladder = RankLedger.Ladder;
            var index = GameContent.Index;
            var held = RankLedger.Held;
            var next = RankLedger.Next;

            PaintHero(ladder, held);

            foreach (var row in _rows)
            {
                bool earned = ladder.IsHeld(row.Rung, index);
                bool climbing = next != null && ReferenceEquals(next, row.Rung);
                bool live = earned || climbing;

                // **Value, never alpha.** A locked rung stands on the muted plate at full
                // strength; see the class remarks for the page this rule was bought by.
                row.Plate.sprite = Art.S("Ui/" + (live ? Skins.PlateNavy : Skins.Panel));

                row.Badge.color = Color.white;
                row.Burst.color = Pal.A(row.Metal, earned ? .34f : 0f);
                row.Halo.color = Pal.A(row.Metal, earned ? .22f : climbing ? .24f : .10f);
                row.SeatRim.color = Pal.A(row.Metal, earned ? .95f : climbing ? .80f : .45f);

                row.Name.color = earned ? Pal.Gold : climbing ? Pal.Cream : LockedName;
                row.Blurb.color = live ? Pal.A(Pal.Cream, .76f) : LockedInk;
                row.Rule.color = Pal.A(row.Metal, live ? .34f : .20f);

                row.Chip.color = new Color(0f, 0f, 0f, live ? .40f : .26f);
                row.ChipRim.color = Pal.A(row.Metal, live ? .95f : .55f);
                row.Ordinal.color = live ? row.Metal : Pal.A(row.Metal, .75f);

                if (row.Link != null)
                    row.Link.color = earned ? Pal.A(row.Metal, .85f) : Pal.A(Pal.Cream, .12f);

                // The wells stay at full strength on every row, which is the other half of the
                // no-transparency rule: a faded checklist under a solid plate is the one thing
                // on the card that looks unfinished rather than unearned.
                foreach (var well in row.Wells) well.color = Color.white;

                // One answer per row and exactly one of the three is up (invariant 48g).
                row.Seal.gameObject.SetActive(earned);
                row.Standing.gameObject.SetActive(climbing);
                row.Lock.gameObject.SetActive(!live);

                if (climbing)
                    row.Percent.text = Loc.Format(
                        "ui.ranks.percent",
                        Mathf.RoundToInt(Mathf.Clamp01(RankLedger.Progress01) * 100f));

                // Only when the light is *not* running: `Shine` owns the rim while a rung is
                // being climbed, and a repaint that wrote it as well would fight the tween.
                if (!climbing) row.Rim.color = Pal.A(row.Metal, earned ? .55f : .22f);

                for (int i = 0; i < row.Lines.Count; i++)
                {
                    var line = row.Rung.Requirements[i];
                    long have = line.Held(index);
                    bool met = have >= line.Target;

                    row.Marks[i].sprite = met ? Art.S("Ui/ic_check") : Art.Ring(64, 9f);
                    row.Marks[i].color = met ? Pal.Mint : Pal.A(row.Metal, live ? .70f : .45f);

                    row.Lines[i].color = earned ? Pal.A(Pal.Cream, .82f)
                                       : climbing ? Pal.Cream
                                       : LockedInk;

                    // A met line prints its own target rather than the figure that passed it:
                    // "250 / 250" is what finishing looks like, and a lifetime tally that went
                    // on climbing afterwards would read as a bar that overflowed.
                    long shown = have > line.Target ? line.Target : have;
                    row.Counts[i].text = Loc.Format("ui.ranks.fraction",
                                                    Compact.Number(shown),
                                                    Compact.Number(line.Target));
                    row.Counts[i].color = met ? Pal.Mint : climbing ? Pal.Gold : LockedInk;
                }

                PaintBar(row, climbing);

                if (row.Lit != climbing) { row.Lit = climbing; Shine(row, climbing, earned); }
            }
        }

        void PaintHero(RankLadder ladder, RankDefinition held)
        {
            if (_heroBadge == null) return;

            var rung = held ?? ladder.At(1);
            var metal = RankLook.Metal(held);

            // **The first rung, ghosted, for an account below it** — the profile's medallion and
            // the map's badge both take this stance and this is the third. A hero seat standing
            // empty is a hole at the top of the page on the one launch where the page has the
            // most to prove, and the player looking at it is exactly the player it is for. It is
            // the one transparent thing here and <see cref="RankLook.Ghost"/> says why that is
            // not the fault the rest of the page was rebuilt to fix: a faded *card* reads as
            // broken art, where a faded badge under the word "Unranked" reads as what is next.
            //
            // The pips below it stay empty, and the two are not in disagreement: one badge
            // ghosted is a preview, seven ghosted is a strip that says nothing (`BuildPips`).
            // **There is no early return here**, which is where the profile's version of this
            // can afford one: that method paints a badge and its name and nothing else, where
            // this one owns the rays, the rim, the glow, the rule, the kicker, the name, the
            // blurb, the count and the pips. An empty ladder is precisely when
            // `ui.ranks.unranked_blurb` most needs to be on the screen, so the badge switches
            // itself off and everything below it still runs.
            bool drawn = rung != null;
            if (_heroBadge.enabled != drawn) _heroBadge.enabled = drawn;
            if (drawn)
            {
                _heroBadge.sprite = Art.S(rung.Icon);
                _heroBadge.color = held != null ? Color.white : RankLook.Ghost;
            }

            _heroRays.color = Pal.A(held != null ? metal : Pal.Sun, .17f);
            _heroRim.color = Pal.A(held != null ? metal : Pal.Sun, held != null ? .95f : .35f);
            _heroGlow.color = Pal.A(held != null ? metal : Pal.Sun, held != null ? .34f : .18f);
            _heroRule.color = Pal.A(held != null ? metal : Pal.Sun, .22f);
            _heroKicker.color = Pal.A(held != null ? metal : Pal.Sun, .90f);

            _heroName.text = held != null ? held.Name : Loc.Get("ui.ranks.unranked");
            _heroName.color = held != null ? Pal.Gold : Pal.A(Pal.Cream, .7f);

            _heroBlurb.text = held != null ? Loc.Get(held.BlurbKey)
                                           : Loc.Get("ui.ranks.unranked_blurb");

            _heroCount.text = ladder.IsEmpty
                ? string.Empty
                : Loc.Format("ui.ranks.held_count", RankLedger.Ordinal, ladder.Count);

            int ordinal = RankLedger.Ordinal;
            for (int i = 0; i < _pipBadges.Count; i++)
            {
                bool lit = i < ordinal;
                _pipBadges[i].enabled = lit;
                _pipRims[i].color = Pal.A(RankLook.Metal(i + 1), i == ordinal - 1 ? .95f : 0f);
            }
        }

        /// <summary>
        /// The bar, on the rung being climbed and nowhere else. Its trough is the fill's parent,
        /// so switching the trough carries both.
        /// </summary>
        void PaintBar(Row row, bool climbing)
        {
            if (row.Bar == null) return;

            var trough = row.Bar.parent as RectTransform;
            if (trough == null) return;

            if (trough.gameObject.activeSelf != climbing) trough.gameObject.SetActive(climbing);
            if (!climbing) return;

            float room = trough.sizeDelta.x - 6f;
            row.Bar.sizeDelta = new Vector2(room * Mathf.Clamp01(RankLedger.Progress01), BarH);
        }

        /// <summary>
        /// The pool and the rim on the rung being climbed. <c>TasksScreen.Shine</c>'s loop, with
        /// its channel, and turned down: there is exactly <em>one</em> of these on this page
        /// where that one can have six, so it can afford to be a swell rather than a flicker,
        /// and it is the only thing on a page of seven plates that moves at all.
        /// </summary>
        /// <remarks>
        /// <b>The pool is the rung's own metal and the rim is not.</b> A pale metal breathing
        /// behind a card is still a pool of that colour; a pale metal rim against
        /// <c>Pal.Radiance</c>'s swell has nowhere left to go at the top of the tween, so the
        /// rim keeps the near-white it has always had and the colour is carried by the light
        /// underneath it.
        /// </remarks>
        static void Shine(Row row, bool on, bool earned)
        {
            if (row.Pool) Tween.KillChannel(row.Pool.transform, "holy");
            if (row.Rim) Tween.KillChannel(row.Rim.transform, "holy");

            if (!on)
            {
                // **It has to be told where to land**, because the tween outlives the repaint
                // that set the resting colour a few lines earlier and would otherwise fade an
                // earned rung's rim down to a locked one's. It is very nearly dead code — a
                // promotion rebuilds the page (`LadderKey`) — and that is exactly why it would
                // have gone unnoticed.
                if (row.Pool) Tween.Tint(row.Pool, Pal.A(row.Metal, 0f), .3f);
                if (row.Rim) Tween.Tint(row.Rim, Pal.A(row.Metal, earned ? .55f : .22f), .3f);
                return;
            }

            var metal = row.Metal;
            Tween.Run(2.1f, Ease.InOutSine, t =>
            {
                if (row.Pool) row.Pool.color = Pal.A(metal, Mathf.Lerp(.14f, .34f, t));
                if (row.Rim) row.Rim.color = Pal.A(Pal.Radiance, Mathf.Lerp(.45f, .95f, t));
            }, row.Pool, "holy").Loop(-1, true);
        }
    }
}
