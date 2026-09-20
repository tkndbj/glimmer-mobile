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
    /// <para>
    /// <b>Three states and one of them shines.</b> Earned rungs carry a tick, the rung being
    /// climbed carries a pool of light, a gold rim and the only progress bar on the page, and
    /// everything above it is dimmed behind a padlock. Exactly one row shines, which is what
    /// makes a glance at the page enough — seven glowing rows would say nothing at all.
    /// Dimming is <em>alpha</em> throughout and never a tint, because <c>Image.color</c> is a
    /// multiply and would take these saturated metals toward black along their own hue
    /// (invariant 44g).
    /// </para>
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
        const float HeroH = 384f;

        /// <summary>
        /// How wide a card is drawn, against the 1080 this game is designed at.
        ///
        /// <b>Nearly full bleed on purpose.</b> At 1000 the page read as a column of chips on a
        /// wall; a rank card is the most important thing on its own screen and should take the
        /// screen, which is also what makes room for a badge big enough to see the metal on.
        /// </summary>
        const float Width = 1024f;

        /// <summary>A row's fixed part, what each requirement line adds, and its plain foot.</summary>
        const float RowHead = 214f, LineH = 68f, RowFoot = 26f, RowGap = 20f;

        /// <summary>
        /// One requirement's own well, and the furniture in it.
        ///
        /// <b>Every line sits in a trough rather than floating on the plate</b>, which is the
        /// single change that made this page read as a game rather than as a list. A checklist
        /// of bare sentences on a card is a paragraph; the same sentences in wells are rows a
        /// player counts. The well is inset from the card so the plate's own moulding still
        /// frames them.
        /// </summary>
        const float WellInset = 30f, WellH = 58f;

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

        static readonly Vector2 Top = new Vector2(.5f, 1f);
        static readonly Vector2 Left = new Vector2(0f, .5f);
        static readonly Vector2 Right = new Vector2(1f, .5f);
        static readonly Vector2 Centre = new Vector2(.5f, .5f);

        /// <summary>
        /// The bar's orange, pre-divided against <see cref="Skins.Fill"/> exactly as
        /// <c>TasksScreen.BarOrange</c> is, and copied rather than shared for that field's own
        /// reason: it is not a colour, it is what the kit's off-white fill has to be multiplied
        /// by to come out orange, and it may not be reasoned about — only drawn.
        /// </summary>
        static readonly Color BarOrange = new Color(1f, .588f, .118f, 1f);

        /// <summary>The bar's trough and the fill inside it, both grown with the rest of the page.</summary>
        const float BarTrough = 36f, BarH = 32f;

        /// <summary>How a plate or a well nobody has earned yet is drawn. See the class remarks.</summary>
        static readonly Color Unearned = new Color(1f, 1f, 1f, .38f);

        /// <summary>
        /// And how an unearned <em>badge</em> is drawn, which is deliberately less faded.
        ///
        /// A locked card should recede; the picture on it should not, because it is the thing
        /// the player is working toward and the only reason to scroll down the page. At the
        /// plate's own alpha it sank into its dark seat and read as missing art rather than as
        /// not yet — which is what `render_ranks.py --tall` is for, since the locked rungs are
        /// below the fold on every phone.
        /// </summary>
        static readonly Color Unlit = new Color(1f, 1f, 1f, .60f);

        static readonly Color LockedInk = new Color(1f, .953f, .863f, .48f);

        /// <summary>One rung's row, kept so a repaint can write onto it.</summary>
        sealed class Row
        {
            public RankDefinition Rung;
            public RectTransform Root;
            public Image Plate;
            public Image Badge;
            public Image Rim;
            public Image Pool;
            public Image Seal;
            public Image Lock;
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

        Image _heroBadge;
        Text _heroName;
        Text _heroBlurb;
        Text _heroCount;

        /// <summary>The rung ids this page was built for, so a retuned ladder rebuilds whole.</summary>
        string _built;

        protected override void Build()
        {
            _rows.Clear();
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
        /// The badge the player wears now, large, with its name, its line and how far up the
        /// ladder it stands.
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
            var plate = UIKit.Img("Hero", Safe, Art.S("Ui/" + Skins.Panel), Color.white,
                                  new Vector2(Width, HeroH), Top, new Vector2(0f, -(y + HeroH * .5f)));

            var clip = UIKit.Node("Clip", plate.transform);
            UIKit.StretchTo(clip, 8f, 8f, 8f, 8f);
            clip.gameObject.AddComponent<RectMask2D>();

            var rays = UIKit.Img("Rays", clip, Art.Rays(512, 14), Pal.A(Pal.Sun, .17f),
                                 new Vector2(HeroH * 2.3f, HeroH * 2.3f), Centre, new Vector2(0f, 10f));
            rays.raycastTarget = false;

            float left = -Width * .5f;
            float bx = left + HeroBadgeX;

            UIKit.Img("Glow", plate.transform, Art.Glow(128, 2.1f), Pal.A(Pal.Sun, .30f),
                      Vector2.one * (HeroBadge * 1.5f), Centre, new Vector2(bx, 14f));

            // The seat, so the badge stands in the plate rather than on it. The kit's slot is
            // the same moulding the action bar's cells wear, which is what makes a picture
            // dropped into it read as equipment rather than as a sticker.
            var seat = UIKit.Img("Seat", plate.transform, Art.S("Ui/" + Skins.Slot), Color.white,
                                 Vector2.one * (HeroBadge * 1.12f), Centre, new Vector2(bx, 14f));
            seat.raycastTarget = false;

            _heroBadge = UIKit.Img("Badge", plate.transform, null, Color.white,
                                   Vector2.one * HeroBadge, Centre, new Vector2(bx, 14f));
            _heroBadge.preserveAspect = true;

            float textX = left + TextX;
            float textW = Width * .5f - textX - 40f;

            UIKit.Shrinkable(
                UIKit.Titled("Kicker", plate.transform, Loc.Get("ui.ranks.mark"), 24,
                             Pal.A(Pal.Sun, .85f), TextAnchor.MiddleLeft,
                             new Vector2(textW, 30f), Centre,
                             new Vector2(textX + textW * .5f, 112f), 0f, 2f), 14);

            _heroName = UIKit.Shrinkable(
                UIKit.Titled("Name", plate.transform, string.Empty, 60, Pal.Gold,
                             TextAnchor.MiddleLeft, new Vector2(textW, 74f), Centre,
                             new Vector2(textX + textW * .5f, 50f)), 28);

            _heroBlurb = UIKit.Shrinkable(
                UIKit.Titled("Blurb", plate.transform, string.Empty, 28, Pal.A(Pal.Cream, .84f),
                             TextAnchor.UpperLeft, new Vector2(textW, 96f), Centre,
                             new Vector2(textX + textW * .5f, -34f), 0f, 2f, wrap: true), 18);

            _heroCount = UIKit.Shrinkable(
                Scenery.Pill(plate.transform, string.Empty, 28, new Vector2(textW, 66f), Centre,
                             new Vector2(textX + textW * .5f, -122f),
                             new Color(.05f, .09f, .18f, .80f), "ic_star"), 17);

            return y + HeroH + 22f;
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

            // Every row's pool lives here, built first so all of them are **under every plate**.
            // A light hung off a row's own plate would have to be a child, which draws over the
            // plate it is meant to light, or a sibling, which draws over the row above — the
            // arrangement `TasksScreen.BuildSlates` records.
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
                foreach (var rung in ladder.Rungs)
                {
                    y += BuildRow(list, lights, rung, y) + RowGap;
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
        float BuildRow(RectTransform list, RectTransform lights, RankDefinition rung, float y)
        {
            var lines = rung.Requirements;

            // See `BarBand`: the bar's seat belongs to the one row that draws a bar, and the
            // page is rebuilt when that moves.
            bool climbing = ReferenceEquals(RankLedger.Next, rung);
            float height = RowHead + lines.Count * LineH + (climbing ? BarBand : RowFoot);
            float cy = -(y + height * .5f);

            var row = new Row { Rung = rung };

            row.Pool = UIKit.Img("Light_" + rung.Id, lights, Art.Glow(128, 1.35f), Pal.A(Pal.Sun, 0f),
                                 new Vector2(Width + 280f, height + 200f), Top, new Vector2(0f, cy));

            row.Plate = UIKit.Img("Row_" + rung.Id, list, Art.S("Ui/" + Skins.PlateNavy), Color.white,
                                  new Vector2(Width, height), Top, new Vector2(0f, cy));
            row.Root = (RectTransform)row.Plate.transform;

            // The rim is drawn over the plate rather than behind it. It went behind once on the
            // streak board and was simply invisible: the kit's plates are opaque (invariant 48i).
            row.Rim = UIKit.Img("Rim", row.Root, Art.RoundOutline(30, 8f), Pal.A(Pal.Sun, 0f));
            UIKit.StretchTo((RectTransform)row.Rim.transform, 0f, 0f, 0f, 0f);

            float left = -Width * .5f;
            float badgeX = left + WellInset + BadgeSeat * .5f;
            float headY = height * .5f - RowHead * .5f;

            UIKit.Img("Glow", row.Root, Art.Glow(128, 2.1f), Pal.A(Pal.Sun, .16f),
                      Vector2.one * (BadgeSeat * 1.3f), Centre, new Vector2(badgeX, headY));

            var seat = UIKit.Img("Seat", row.Root, Art.S("Ui/" + Skins.Slot), Color.white,
                                 Vector2.one * BadgeSeat, Centre, new Vector2(badgeX, headY));
            seat.raycastTarget = false;

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

            row.Lock = UIKit.Img("Lock", row.Root, Art.S("Ui/ic_lock"), LockedInk,
                                 Vector2.one * (AnswerSize * .82f), Centre, new Vector2(answerX, headY));
            row.Lock.preserveAspect = true;

            float textX = badgeX + BadgeSeat * .5f + 28f;
            float textW = (answerX - AnswerSize * .5f - 24f) - textX;

            // **The ordinal, above the name.** A ladder wants a number on it: "RANK 3" says
            // where this badge sits without the player counting rows, and it is the one line on
            // the card that is true whether or not the rung is held.
            row.Ordinal = UIKit.Shrinkable(
                UIKit.Titled("Ordinal", row.Root, Loc.Format("ui.ranks.ordinal", rung.Ordinal), 22,
                             Pal.A(Pal.Sun, .85f), TextAnchor.MiddleLeft,
                             new Vector2(textW, 28f), Centre,
                             new Vector2(textX + textW * .5f, headY + 62f), 0f, 2f), 13);

            row.Name = UIKit.Shrinkable(
                UIKit.Titled("Name", row.Root, rung.Name, 46, Pal.Gold, TextAnchor.MiddleLeft,
                             new Vector2(textW, 56f), Centre,
                             new Vector2(textX + textW * .5f, headY + 16f)), 24);

            row.Blurb = UIKit.Shrinkable(
                UIKit.Titled("Blurb", row.Root, Loc.Get(rung.BlurbKey), 26, Pal.A(Pal.Cream, .76f),
                             TextAnchor.UpperLeft, new Vector2(textW, 62f), Centre,
                             new Vector2(textX + textW * .5f, headY - 46f), 0f, 2f, wrap: true), 16);

            // A hairline between the head and the checklist, so the card reads as two zones
            // rather than as one crowded one. Pixel-thin and barely there: a rule that can be
            // *seen* is a rule that competes with the plate's own moulding.
            row.Rule = UIKit.Img("Rule", row.Root, Art.Pixel, new Color(1f, 1f, 1f, .10f),
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
                var mark = UIKit.Img("M", well.transform, Art.Ring(64, 9f), Pal.A(Pal.Cream, .32f),
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

                row.Plate.color = earned || climbing ? Color.white : Unearned;
                row.Badge.color = earned ? Color.white : Unlit;
                row.Name.color = earned ? Pal.Gold : climbing ? Pal.Cream : LockedInk;
                row.Blurb.color = earned ? Pal.A(Pal.Cream, .76f) : LockedInk;
                row.Ordinal.color = earned || climbing ? Pal.A(Pal.Sun, .85f) : LockedInk;
                row.Rule.color = new Color(1f, 1f, 1f, earned || climbing ? .10f : .05f);

                // The wells are dimmed with the plate rather than left at full strength over a
                // faded card — a checklist that stays crisp on a locked rung reads as the only
                // live thing on it, which is the opposite of what a padlock is saying.
                var wellInk = earned || climbing ? Color.white : Unearned;
                foreach (var well in row.Wells) well.color = wellInk;

                row.Seal.gameObject.SetActive(earned);
                row.Lock.gameObject.SetActive(!earned && !climbing);

                // Only when the light is *not* running: `Shine` owns the rim while a rung is
                // being climbed, and a repaint that wrote it as well would fight the tween.
                if (!climbing) row.Rim.color = earned ? Pal.A(Pal.Sun, .32f) : Pal.A(Pal.Sun, 0f);

                for (int i = 0; i < row.Lines.Count; i++)
                {
                    var line = row.Rung.Requirements[i];
                    long have = line.Held(index);
                    bool met = have >= line.Target;

                    row.Marks[i].sprite = met ? Art.S("Ui/ic_check") : Art.Ring(64, 9f);
                    row.Marks[i].color = met ? Pal.Mint : Pal.A(Pal.Cream, .30f);

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

                if (row.Lit != climbing) { row.Lit = climbing; Shine(row, climbing); }
            }
        }

        void PaintHero(RankLadder ladder, RankDefinition held)
        {
            if (_heroBadge == null) return;

            var rung = held ?? ladder.At(1);

            _heroBadge.sprite = rung != null ? Art.S(rung.Icon) : null;
            _heroBadge.color = held != null ? Color.white : Unlit;

            _heroName.text = held != null ? held.Name : Loc.Get("ui.ranks.unranked");
            _heroName.color = held != null ? Pal.Gold : Pal.A(Pal.Cream, .7f);

            _heroBlurb.text = held != null ? Loc.Get(held.BlurbKey)
                                           : Loc.Get("ui.ranks.unranked_blurb");

            _heroCount.text = ladder.IsEmpty
                ? string.Empty
                : Loc.Format("ui.ranks.held_count", RankLedger.Ordinal, ladder.Count);
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

            Tween.Run(2.1f, Ease.InOutSine, t =>
            {
                if (row.Pool) row.Pool.color = Pal.A(Pal.Sun, Mathf.Lerp(.12f, .30f, t));
                if (row.Rim) row.Rim.color = Pal.A(Pal.Radiance, Mathf.Lerp(.45f, .95f, t));
            }, row.Pool, "holy").Loop(-1, true);
        }
    }
}
