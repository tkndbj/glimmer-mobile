using System;
using System.Collections.Generic;
using System.Threading;
using GlimmerGrove.Cloud;
using GlimmerGrove.Daily;
using GlimmerGrove.Events;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The season pass: two tracks up one vine, with viewport-bounded row recycling and
    /// server-authoritative premium claims.
    ///
    /// <para>
    /// <b>The backdrop is the painting, not a shade over it.</b> This screen used to lay an
    /// 88%-opaque near-black plate over <c>Bg/event_*</c> — a sunrise sky, a flowered verge and
    /// a leaf canopy, all of it invisible — so the one page in the game whose whole job is to
    /// look like a celebration read as a spreadsheet in a cave. What makes bright art safe
    /// behind text is the same thing that makes it safe behind a board (see CRAFT.md): every
    /// piece of type here sits on an <em>opaque</em> plate of its own, so brightening what is
    /// behind them widens the separation rather than closing it. The only shading left is a
    /// gradient that deepens toward the footer, where the dock and the nav bar are.
    /// </para>
    /// <para>
    /// <b>A claim is a payout, not a toast.</b> Every other place this game hands over currency
    /// — the chest, the shop, the wheel's prize — breaks the reward into tokens that fly into
    /// the hub's own pills and step the number as they land (<see cref="RewardFlight"/>). This
    /// screen had a spark burst and a line of text, which is the largest moment on the page
    /// drawn as the smallest change on it. It now runs the same cascade, which is why the
    /// header carries live credit and gem pills at all: <see cref="ResourceSlots"/> is how a
    /// payout finds somewhere to land, and a reward that lands somewhere is worth more than a
    /// reward that is merely granted.
    /// </para>
    /// <para>
    /// <b>Nothing here is load-bearing.</b> The grant happens first and the animation is a
    /// picture of it: a player who backgrounds the app mid-cascade is paid exactly the same as
    /// one who watches it. That is why the rows repaint immediately and the flight runs on a
    /// layer of its own — a recycled row must never be able to strand a token, and a token must
    /// never be able to hold a row in a half-drawn state.
    /// </para>
    /// <para>
    /// <b>Buying the pass is not gated on having read it.</b> It used to be: a null pass state
    /// short-circuited the tap with "connect to check your pass", so an unreachable
    /// <c>eventPass</c> function made the product unbuyable rather than merely unknown. The
    /// receipt is the authority (invariant 18a) — <c>redeemPurchase</c> writes the entitlement
    /// itself, a refused receipt is never confirmed, and both stores re-deliver for ever — so
    /// the sale is allowed whenever the <em>store</em> is ready and <see cref="StoreTap"/> says
    /// which of the six things is wrong when it is not.
    /// </para>
    /// </summary>
    public sealed class EventScreen : View
    {
        public override string Track => "mus_menu";

        // ------------------------------------------------------------------ geometry
        /// <summary>
        /// One tier's slice of the track. The cards are <see cref="CardHeight"/>, so the
        /// difference is the air between them — 82 units, where it used to be 32 and every
        /// reward read as stacked on the one above it. The divider sits in the middle of it.
        /// </summary>
        const float RowHeight = 272f, CardHeight = 190f;

        /// <summary>Dock height. Two pills, no second row — see <c>BuildFooter</c>.</summary>
        const float Footer = 214f;

        /// <summary>Every fifth rung is a cache and every tenth a hoard. Content decides the
        /// amounts; this decides only how loudly they are drawn.</summary>
        const int CacheEvery = 5, HoardEvery = 10;

        // ------------------------------------------------------------------- palette
        // Brighter than the old set, because they are now read against a lit sky rather than
        // against black. Every card body is opaque: see the class remarks.
        static readonly Color Ink = new Color(.045f, .12f, .125f);
        static readonly Color Mint = Pal.Hex("#8CF0B4");
        static readonly Color Leaf = Pal.Hex("#1B7C55");
        static readonly Color LeafDeep = Pal.Hex("#166348");
        static readonly Color Gold = Pal.Hex("#FFC93C");
        static readonly Color Amber = Pal.Hex("#9A5E10");
        static readonly Color AmberDeep = Pal.Hex("#7C4A0B");
        static readonly Color Plum = Pal.Hex("#7B3FB0");
        static readonly Color PlumDeep = Pal.Hex("#4A1F72");
        static readonly Color Cream = Pal.Hex("#FFF6E2");
        static readonly Color Dock = new Color(.035f, .105f, .115f, .96f);

        static readonly Vector2 Top = new Vector2(.5f, 1f), Centre = new Vector2(.5f, .5f);
        static readonly Vector2 Left = new Vector2(0f, .5f), Right = new Vector2(1f, .5f);

        readonly List<Row> _rows = new List<Row>();
        GroveEvent _event;
        EventProgress _progress;
        EventPassState _pass;

        RectTransform _list, _viewport, _fx, _payAnchor;
        Image _fill, _fillHead;
        Text _progressLabel, _timer, _offerLabel, _hint;
        Btn _collect, _offerBtn;
        Image _collectBadge;
        Text _collectBadgeText;
        CancellationTokenSource _lifetime;
        bool _requesting, _claiming, _retreat, _refreshPending;
        float _width, _visible, _nextClock, _header;
        int _first = -1, _generation;

        /// <summary>
        /// The window of rungs a claim has just swept, so the cards inside it stamp their seal
        /// rather than simply appearing already collected.
        ///
        /// <para>
        /// Carried as a goal range rather than as a card reference because the rows recycle:
        /// by the time the repaint runs, the card that was tapped may be bound to a different
        /// tier entirely. A range survives that, and it is also what makes "collect all" stamp
        /// every rung it took in sequence instead of only the last one.
        /// </para>
        /// </summary>
        int _stampFrom, _stampTo;
        bool _stampPremium;

        sealed class Card
        {
            public Image Face, Edge, Gloss, Rays, Art, Seal, Ring, Lock;
            public Text Amount, Status, Title;
            public Button Button;
            public bool Ready;
        }

        sealed class Row
        {
            public int Index;
            public RectTransform Root;
            public Image Badge, BadgeRim;
            public RectTransform Divider;
            public Text Number;
            public Card Free, Premium;
        }

        // ---------------------------------------------------------------------- build
        protected override void Build()
        {
            _event = GroveEvents.Featured;
            if (_event == null) { _retreat = true; return; }

            _lifetime = new CancellationTokenSource();
            _width = Mathf.Min(1000f, Flow.Size.x - 64f);

            BuildBackdrop();
            BuildHeader();
            BuildTracks();
            BuildFooter();
            NavBar.Build(Content, NavBar.Tab.Home);

            // Last, and on Content rather than Safe, so a token drawn on it passes over the
            // dock and the nav bar on its way to a pill. It is also the tween owner for the
            // whole cascade, so everything in the air dies with the screen.
            _fx = UIKit.Node("Payout", Content);
            var fxBlank = _fx.gameObject.AddComponent<Image>();
            fxBlank.color = Color.clear;
            fxBlank.raycastTarget = false;

            Refresh();

            int focus = Mathf.Clamp(_progress.Milestones - 1, 0, _event.Milestones.Count - 1);
            _list.anchoredPosition = new Vector2(
                0f, Mathf.Clamp(focus * RowHeight, 0f, Mathf.Max(0f, _list.rect.height - _visible)));
            BindRows(true);
        }

        /// <summary>
        /// The painting, a warm bloom over it, drifting motes, and one gradient that deepens
        /// toward the dock.
        ///
        /// <para>
        /// The gradient is the whole readability budget. It is transparent across the top two
        /// thirds — where the header's own plates carry their contrast — and reaches its
        /// deepest exactly where the track scrolls under the dock, so a reward card leaving the
        /// viewport dims into the furniture instead of being cut off by it.
        /// </para>
        /// </summary>
        void BuildBackdrop()
        {
            // No flat shade and almost no vignette. Both were what made a sunrise read as a
            // cave: a vignette costs the corners several times what it costs the middle, so at
            // the .55 every other screen takes it turns a lit sky into a bruise. What is left
            // is enough to stop the canopy's hard leaf edges cutting the frame.
            Scenery.Layered(Content, "event", 0f, .14f);

            // Sunrise, gathered. The painted sky already carries a sun low on the left; this
            // puts the game's own light where the title sits, which is what ties the type to
            // the art. Peach rather than white, because a white bloom over a coloured sky
            // desaturates it and desaturation is the exact failure this screen was fixing.
            UIKit.Img("Dawn", Content, Art.Glow(256, 1.9f), new Color(1f, .72f, .38f, .22f),
                      new Vector2(_width * 2.1f, _width * 1.2f), Top, new Vector2(0f, 180f));

            // One wash, and it is anchored to the bottom rather than stretched over the whole
            // screen: the readability budget is only needed where the dock and the nav bar are,
            // and spending it in the middle is what greys out the picture the page is for.
            float wash = Mathf.Max(760f, Flow.Size.y * .56f);
            var depth = UIKit.Img("Depth", Content,
                                  Art.Gradient(new Color(.015f, .075f, .085f, .94f),
                                               new Color(.02f, .09f, .10f, .46f),
                                               new Color(.03f, .10f, .12f, 0f), 256),
                                  Color.white, new Vector2(Flow.Size.x + 8f, wash),
                                  new Vector2(.5f, 0f), new Vector2(0f, wash * .5f));
            depth.type = Image.Type.Simple;

            Fireflies.Spawn(Content, 22, new Color(1f, .93f, .70f), 8f, 26f);
        }

        // --------------------------------------------------------------------- header
        void BuildHeader()
        {
            // A running cursor rather than forty literals, so the one adaptive number below
            // (the showcase's height on a short phone) moves everything under it and the
            // viewport's top is whatever this ends at. Two screens of different aspect were
            // laid out by hand once and the shorter one lost its column headings behind the
            // first row.
            float y = 22f;

            const float ChromeSize = 92f;
            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * ChromeSize,
                             new Vector2(0f, 1f), new Vector2(76f, -(y + ChromeSize * .5f)),
                             () => Flow.Go<HomeScreen>());
            UIKit.IconButton("Info", Safe, Skins.Aside, "ic_info", Vector2.one * ChromeSize,
                             new Vector2(1f, 1f), new Vector2(-76f, -(y + ChromeSize * .5f)),
                             () => Flow.Modal<EventInfoOverlay>(v => v.For(_event)));

            // The pills are what makes the payout cascade possible at all — see the class
            // remarks — and they belong at the top for the ordinary reason every shop and hub
            // puts them there: a page that spends and pays currency should say how much of it
            // you have without being asked.
            Pill(ResourceSlots.Kind.Credits, -116f, -(y + ChromeSize * .5f),
                             Pal.Gold, null, Compact.Number(Profile.Coins),
                             v => Compact.Number(v));
            Pill(ResourceSlots.Kind.Gems, 116f, -(y + ChromeSize * .5f),
                            Pal.Bloom, Art.S("Ui/ic_gem"), Compact.Number(Profile.Gems),
                            v => Compact.Number(v));
            y += ChromeSize + 24f;

            Caption("Eyebrow", Safe, Loc.Get("ui.pass.eyebrow"), 23, Mint,
                    new Vector2(700f, 34f), Top, new Vector2(0f, -(y + 17f)));
            y += 40f;

            UIKit.Titled("Title", Safe, Loc.Get(_event.NameKey), 66, Cream, TextAnchor.MiddleCenter,
                         new Vector2(760f, 84f), Top, new Vector2(0f, -(y + 42f)), 3f, 4f);
            y += 92f;

            // The clock is a chip rather than a line of loose gold text. A season deadline is
            // the one number on this page that changes while you are looking at it, and a
            // border is what stops it reading as part of the title block.
            var clock = Panel("SeasonClock", Safe, new Vector2(470f, 52f), Top,
                              new Vector2(0f, -(y + 26f)), new Color(.06f, .16f, .17f, .92f));
            Panel("ClockEdge", clock.transform, new Vector2(470f, 52f), Centre, Vector2.zero,
                  Pal.A(Gold, .55f), true);
            _timer = Caption("SeasonTime", clock.transform, "", 24, Gold,
                             new Vector2(430f, 40f), Centre, Vector2.zero);
            y += 68f;

            y += BuildShowcase(y);
            y += 18f;

            _progressLabel = Caption("Progress", Safe, "", 26, Cream,
                                     new Vector2(_width, 36f), Top, new Vector2(0f, -(y + 18f)));
            y += 44f;

            BuildRail(y + 13f);
            y += 44f;

            BuildHeadings(y + 27f);
            y += 62f;

            _header = y;
        }

        /// <summary>
        /// One resource readout, registered with <see cref="ResourceSlots"/> as it is built.
        ///
        /// Registration happens here rather than at the call site for <c>HomeScreen</c>'s
        /// reason: the row is rebuilt on every navigation, and the one place guaranteed to run
        /// on a rebuild is the builder.
        /// </summary>
        void Pill(ResourceSlots.Kind kind, float x, float y, Color tint, Sprite icon,
                  string value, Func<long, string> format)
        {
            var bg = UIKit.Img("Pill", Safe, Art.Round(22), new Color(.035f, .095f, .115f, .88f),
                               new Vector2(212f, 74f), Top, new Vector2(x, y));
            var edge = UIKit.Img("Edge", bg.transform, Art.RoundOutline(22, 3f), Pal.A(tint, .5f));
            UIKit.StretchTo((RectTransform)edge.transform, 0f, 0f, 0f, 0f);

            var glow = UIKit.Img("Glow", bg.transform, Art.Glow(96, 2f), Pal.A(tint, .30f),
                                 new Vector2(96f, 96f), Left, new Vector2(40f, 0f));
            var ic = UIKit.Img("Icon", bg.transform, icon, Color.white,
                               new Vector2(54f, 54f), Left, new Vector2(40f, 0f));
            ic.preserveAspect = true;
            if (icon == null) Flipbook.Attach(ic, "Ui/Coin", 11f);
            Tween.Breathe(ic.transform, .05f, 2.4f, x * .01f);

            var text = UIKit.Titled("V", bg.transform, value, 30, Cream, TextAnchor.MiddleCenter,
                                    new Vector2(112f, 44f), Centre, new Vector2(22f, 0f), 3f, 3f);

            ResourceSlots.Register(kind, (RectTransform)ic.transform, text, glow, tint, format);
        }

        /// <summary>
        /// The pass, sold. Returns its height so the header cursor can step past it.
        ///
        /// <para>
        /// It shrinks on a short display rather than being cut: the tiers below it are the
        /// page, and a 16:9 phone that spent 208 units on a sales banner would show two of them.
        /// </para>
        /// </summary>
        float BuildShowcase(float y)
        {
            float h = Flow.Size.y >= 2050f ? 214f : 172f;

            var hero = Panel("PassShowcase", Safe, new Vector2(_width, h), Top,
                             new Vector2(0f, -(y + h * .5f)), PlumDeep);

            // A plate rather than a flat fill: the top half is lit, which is what every jelly
            // button in this game does and what makes a rectangle read as an object.
            var gloss = UIKit.Img("Gloss", hero.transform, Art.Round(24), new Color(1f, 1f, 1f, .085f),
                                  new Vector2(_width - 10f, h * .48f), Top, new Vector2(0f, -5f));
            gloss.type = Image.Type.Sliced;

            Panel("ShowcaseEdge", hero.transform, new Vector2(_width, h), Centre, Vector2.zero,
                  Pal.A(Gold, .70f), true);
            Sheen.Attach((RectTransform)hero.transform, 4.2f, .20f);

            float artX = -168f;
            UIKit.Img("TreasureGlow", hero.transform, Art.Glow(), new Color(1f, .78f, .30f, .46f),
                      new Vector2(330f, h * 1.25f), Right, new Vector2(artX + 6f, 0f));
            var rays = UIKit.Img("TreasureRays", hero.transform, Art.Rays(256, 14),
                                 new Color(1f, .86f, .46f, .22f),
                                 new Vector2(300f, 300f), Right, new Vector2(artX + 6f, 0f));
            Spin(rays.rectTransform, 26f);

            var chest = UIKit.Img("Treasure", hero.transform, Art.S("Ui/Shop/coins_4"), Color.white,
                                  new Vector2(214f, h * .84f), Right, new Vector2(artX, -6f));
            chest.preserveAspect = true;
            Tween.Bob(chest.rectTransform, 7f, 2.9f);

            var gems = UIKit.Img("Gems", hero.transform, Art.S("Ui/Shop/gems_3"), Color.white,
                                 new Vector2(116f, 98f), Right, new Vector2(artX + 96f, -40f));
            gems.preserveAspect = true;
            Tween.Bob(gems.rectTransform, 5f, 3.4f, 1.1f);

            var crown = UIKit.Img("Crown", hero.transform, Art.S("Ui/Win/crown"), Color.white,
                                  new Vector2(120f, 80f), Right, new Vector2(artX - 4f, h * .30f));
            crown.preserveAspect = true;
            Tween.Breathe(crown.transform, .07f, 2.6f);

            float tw = _width - 340f;
            Caption("HeroKicker", hero.transform, Loc.Get("ui.pass.hero_kicker"), 22, Gold,
                    new Vector2(tw, 34f), Left, new Vector2(tw / 2f + 28f, h * .28f), TextAnchor.MiddleLeft);
            Caption("HeroTitle", hero.transform, Loc.Get("ui.pass.hero_title"), 37, Cream,
                    new Vector2(tw, 52f), Left, new Vector2(tw / 2f + 28f, h * .04f), TextAnchor.MiddleLeft);
            Caption("HeroDetail", hero.transform, Loc.Format("ui.pass.hero_detail", _event.Milestones.Count),
                    24, Mint, new Vector2(tw, 38f), Left,
                    new Vector2(tw / 2f + 28f, -h * .24f), TextAnchor.MiddleLeft);

            // The banner arrives rather than appears. Build runs once per navigation, so this
            // is not GridView's trap (16d) — nothing here repaints, and a screen whose
            // headline element simply exists on frame one reads as a document.
            hero.transform.localScale = Vector3.zero;
            Tween.Pop(hero.transform, 0f, .52f, .06f);

            return h;
        }

        /// <summary>
        /// How far up the vine this player is. A capsule with a lit head rather than a flat
        /// bar, because the head is the only part of a progress rail anybody actually looks at.
        /// </summary>
        void BuildRail(float y)
        {
            var rail = Panel("ProgressRail", Safe, new Vector2(_width - 24f, 28f), Top,
                             new Vector2(0f, -y), new Color(.03f, .12f, .11f, .94f));
            Panel("RailEdge", rail.transform, new Vector2(_width - 24f, 28f), Centre, Vector2.zero,
                  Pal.A(Mint, .40f), true);

            // A hairline of the fill's own colour along the empty track. A rail at 0 of 40 is
            // otherwise a black slab with a border, which reads as a control that failed to
            // load rather than as a journey nobody has started — and 0 of 40 is what every new
            // player meets first.
            UIKit.Img("RailGhost", rail.transform, Art.Round(9), Pal.A(Mint, .10f),
                      new Vector2(_width - 32f, 18f), Centre, Vector2.zero).type = Image.Type.Sliced;

            _fill = UIKit.Img("ProgressFill", rail.transform, Art.Round(9), Mint,
                              new Vector2(1f, 18f), Left, new Vector2(4f, 0f));
            _fill.type = Image.Type.Sliced;
            _fill.rectTransform.pivot = Left;

            _fillHead = UIKit.Img("FillHead", rail.transform, Art.Glow(64, 1.8f),
                                  Pal.A(Cream, .85f), new Vector2(50f, 50f), Left,
                                  new Vector2(4f, 0f));
        }

        /// <summary>
        /// Which column is which, as two plates rather than two words. They are the only
        /// headings on the page and they sit directly above forty cards wearing the same two
        /// colours, so making them look like the cards is what says the colour means something.
        /// </summary>
        void BuildHeadings(float y)
        {
            float w = _width * .46f;

            var free = Panel("FreeHeading", Safe, new Vector2(w, 54f), Top,
                             new Vector2(-_width * .25f, -y), LeafDeep);
            Panel("FreeEdge", free.transform, new Vector2(w, 54f), Centre, Vector2.zero,
                  Pal.A(Mint, .60f), true);
            Caption("FreeText", free.transform, Loc.Get("ui.pass.free"), 27, Mint,
                    new Vector2(w - 24f, 40f), Centre, Vector2.zero);

            var prem = Panel("PremiumHeading", Safe, new Vector2(w, 54f), Top,
                             new Vector2(_width * .25f, -y), AmberDeep);
            Panel("PremiumEdge", prem.transform, new Vector2(w, 54f), Centre, Vector2.zero,
                  Pal.A(Gold, .70f), true);
            // No glyph. A crown small enough to fit beside the caption is a smudge at this
            // size, and the plate's own gold already says which column this is.
            Caption("PremiumText", prem.transform, Loc.Get("ui.pass.premium"), 27, Gold,
                    new Vector2(w - 24f, 40f), Centre, Vector2.zero);

            free.transform.localScale = Vector3.zero;
            prem.transform.localScale = Vector3.zero;
            Tween.Pop(free.transform, 0f, .46f, .16f);
            Tween.Pop(prem.transform, 0f, .46f, .22f);
        }

        // --------------------------------------------------------------------- tracks
        void BuildTracks()
        {
            _viewport = UIKit.Node("RewardViewport", Safe);
            UIKit.StretchTo(_viewport, 32f, NavBar.Height + Footer, 32f, _header);
            _viewport.gameObject.AddComponent<RectMask2D>();
            _viewport.gameObject.AddComponent<Image>().color = Color.clear;

            _list = UIKit.Node("RewardTrack", _viewport);
            _list.anchorMin = new Vector2(0f, 1f);
            _list.anchorMax = new Vector2(1f, 1f);
            _list.pivot = Top;
            _list.sizeDelta = new Vector2(0f, _event.Milestones.Count * RowHeight + 24f);
            _list.anchoredPosition = Vector2.zero;

            Canvas.ForceUpdateCanvases();
            _visible = Mathf.Max(220f, _viewport.rect.height);

            int count = Mathf.Min(_event.Milestones.Count, Mathf.CeilToInt(_visible / RowHeight) + 2);
            for (int i = 0; i < count; i++) _rows.Add(BuildRow());

            var scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = _list;
            scroll.viewport = _viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.decelerationRate = .08f;
            scroll.scrollSensitivity = 55f;
            scroll.onValueChanged.AddListener(_ => BindRows(false));
        }

        Row BuildRow()
        {
            var row = new Row { Root = UIKit.Box("Tier", _list, new Vector2(_width, RowHeight), Top, Vector2.zero) };

            // The vine. A soft capsule rather than a hairline, because it is the one continuous
            // thing on the page and a 3-pixel grey line reads as a table rule.
            UIKit.Img("VineGlow", row.Root, Art.SoftCapsule(34, 96), Pal.A(Mint, .22f),
                      new Vector2(34f, RowHeight), Centre, Vector2.zero);
            UIKit.Img("VineCore", row.Root, Art.Capsule(16, 48), Pal.A(LeafDeep, .95f),
                      new Vector2(16f, RowHeight), Centre, Vector2.zero);
            UIKit.Img("VineLit", row.Root, Art.Capsule(6, 48), Pal.A(Mint, .70f),
                      new Vector2(6f, RowHeight), Centre, new Vector2(-3f, 0f));

            row.Free = BuildCard(row, false);
            row.Premium = BuildCard(row, true);

            // The tier medallion is built after the cards so it draws over their inner edges;
            // the vine passes behind it and the number sits on it.
            row.Badge = UIKit.Img("TierMedallion", row.Root, Art.Disc(), Ink,
                                  new Vector2(76f, 76f), Centre, Vector2.zero);
            row.BadgeRim = UIKit.Img("MedallionRim", row.Root, Art.Ring(96, 4f), Pal.A(Mint, .7f),
                                     new Vector2(76f, 76f), Centre, Vector2.zero);
            row.Number = Caption("TierNumber", row.Root, "", 29, Cream,
                                 new Vector2(64f, 48f), Centre, Vector2.zero);

            row.Divider = BuildDivider(row.Root);
            return row;
        }

        /// <summary>
        /// What separates one tier from the next: a rule that fades to nothing at both ends,
        /// pinched into a glint where it crosses the vine.
        ///
        /// <para>
        /// A flat hairline was the obvious thing and is wrong here for a reason worth writing
        /// down: a straight line of constant alpha across a <em>painted</em> backdrop reads as
        /// a seam in the picture rather than as an ornament, because nothing else on the page
        /// has a hard edge that does not belong to an object. A soft capsule has no edge to
        /// catch, and the glint gives the eye the thing it was looking for — a mark at the
        /// middle — without drawing a boundary anywhere.
        /// </para>
        /// <para>
        /// It is the row's own child at the row's bottom edge, so recycling carries it for
        /// free, and the last row's is hidden rather than special-cased into existence.
        /// </para>
        /// </summary>
        RectTransform BuildDivider(RectTransform parent)
        {
            var host = UIKit.Box("Divider", parent, new Vector2(_width, 40f),
                                 new Vector2(.5f, 0f), Vector2.zero);

            float seg = _width * .40f;
            foreach (int side in new[] { -1, 1 })
            {
                var rule = UIKit.Img("Rule", host, Art.SoftCapsule(14, 64), Pal.A(Cream, .22f),
                                     new Vector2(14f, seg), Centre,
                                     new Vector2(side * (seg * .5f + 46f), 0f));
                rule.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

            // Gold, and bigger than it looks like it needs to be. The first cut drew it mint
            // on a mint vine, which is the same mistake as the gold padlock on the amber card
            // one level up: an ornament has to differ from what it sits on, not merely exist.
            UIKit.Img("Glint", host, Art.Glint(128, 4), Pal.A(Gold, .85f),
                      new Vector2(76f, 76f), Centre, Vector2.zero);
            return host;
        }

        Card BuildCard(Row row, bool premium)
        {
            const float Gap = 68f, Pad = 6f;
            float w = _width * .5f - Gap - Pad;
            float x = (premium ? 1f : -1f) * (Gap + w * .5f);

            var face = Panel(premium ? "PremiumReward" : "FreeReward", row.Root,
                             new Vector2(w, CardHeight), Centre, new Vector2(x, 0f), Ink);
            var card = new Card { Face = face };

            // Behind everything on the card, and off unless the rung is a cache — see Paint.
            card.Rays = UIKit.Img("CardRays", face.transform, Art.Rays(256, 12),
                                  new Color(1f, .9f, .55f, .16f),
                                  new Vector2(CardHeight * 1.7f, CardHeight * 1.7f), Left,
                                  new Vector2(84f, 0f));
            Spin(card.Rays.rectTransform, premium ? 18f : -18f);

            card.Gloss = UIKit.Img("Gloss", face.transform, Art.Round(24), new Color(1f, 1f, 1f, .075f),
                                   new Vector2(w - 8f, CardHeight * .46f), Top, new Vector2(0f, -4f));
            card.Gloss.type = Image.Type.Sliced;

            card.Edge = Panel("CardEdge", face.transform, new Vector2(w, CardHeight), Centre,
                              Vector2.zero, premium ? Gold : Mint, true);

            // The pulse a claimable card wears. Driven from Update rather than by a looping
            // tween, so a recycled row cannot leave one running against a card that is no
            // longer ready — every frame reads the flag, and the flag is set by Paint.
            card.Ring = Panel("ReadyRing", face.transform, new Vector2(w + 16f, CardHeight + 16f),
                              Centre, Vector2.zero, Pal.A(Mint, 0f), true);

            card.Title = Caption("RewardKind", face.transform, "", 21, premium ? Gold : Mint,
                                 new Vector2(w - 32f, 32f), Top, new Vector2(0f, -24f));

            card.Art = UIKit.Img("RewardArt", face.transform, null, Color.white,
                                 new Vector2(134f, 118f), Left, new Vector2(80f, -2f));
            card.Art.preserveAspect = true;

            card.Amount = Caption("Amount", face.transform, "", 37, Cream,
                                  new Vector2(w - 150f, 70f), Right,
                                  new Vector2(-(w - 150f) / 2f - 8f, 2f));

            card.Status = Caption("ClaimState", face.transform, "", 22, Cream,
                                  new Vector2(w - 40f, 32f), new Vector2(.5f, 0f), new Vector2(0f, 24f));

            // A gold padlock on an amber card is a stain rather than a symbol — the two are
            // the same hue — so it gets an ink chip of its own. The chip is what carries the
            // contrast; the glyph only has to be legible against the chip.
            card.Lock = UIKit.Img("LockChip", face.transform, Art.Disc(), new Color(.03f, .09f, .08f, .82f),
                                  new Vector2(48f, 48f), new Vector2(0f, 1f), new Vector2(32f, -30f));
            var padlock = UIKit.Img("Padlock", card.Lock.transform, Art.S("Ui/padlock"), Cream,
                                    new Vector2(28f, 28f), Centre, Vector2.zero);
            padlock.preserveAspect = true;

            card.Seal = UIKit.Img("ClaimSeal", face.transform, Art.S("Ui/ic_check"), Mint,
                                  new Vector2(34f, 34f), new Vector2(1f, 1f), new Vector2(-28f, -26f));

            face.raycastTarget = true;
            card.Button = face.gameObject.AddComponent<Button>();
            card.Button.targetGraphic = face;
            card.Button.transition = Selectable.Transition.None;
            card.Button.onClick.AddListener(() => Claim(row.Index, premium));
            return card;
        }

        // --------------------------------------------------------------------- footer
        /// <summary>
        /// Two pills and one line of terms.
        ///
        /// <para>
        /// <b>Restore and Play are gone deliberately.</b> Restore lives on the shop screen,
        /// which is the one place a store account is the subject and the only place a review
        /// needs to find it; a second copy here was a 22-point control that overflowed its own
        /// pill. Play duplicated the hub's route into the event and was the only control on the
        /// page that navigated away from the thing the page is selling.
        /// </para>
        /// </summary>
        void BuildFooter()
        {
            var dock = Panel("PurchaseDock", Safe, new Vector2(_width + 48f, Footer),
                             new Vector2(.5f, 0f), new Vector2(0f, NavBar.Height + Footer * .5f), Dock);
            Panel("DockEdge", dock.transform, new Vector2(_width + 48f, Footer), Centre, Vector2.zero,
                  new Color(1f, 1f, 1f, .10f), true);

            _hint = Caption("PurchaseTerms", dock.transform, "", 23, Pal.A(Cream, .82f),
                            new Vector2(_width - 24f, 38f), Top, new Vector2(0f, -30f));

            var offer = UIKit.TextButton("UpgradePass", dock.transform, "btn_orange", "", 33,
                                         new Vector2(_width * .585f, 112f), Centre,
                                         new Vector2(-_width * .205f, -14f), BuyPass);
            _offerBtn = offer;
            _offerLabel = offer.Label;
            Sheen.Attach((RectTransform)offer.transform, 3.6f, .22f);

            _collect = UIKit.TextButton("CollectAll", dock.transform, "btn_green",
                                        Loc.Get("ui.pass.claim_all"), 29,
                                        new Vector2(_width * .355f, 112f), Centre,
                                        new Vector2(_width * .30f, -14f), ClaimAll);

            // How many rungs are waiting, on the button that takes them. A count is the one
            // thing that turns "collect all" from a control into a reason to press it, and it
            // is the same badge the hub already wears over the event tile.
            _collectBadge = UIKit.Img("WaitingBadge", _collect.transform, Art.Disc(), Pal.Rose,
                                      new Vector2(52f, 52f), new Vector2(1f, 1f), new Vector2(-6f, 2f));
            _collectBadgeText = Caption("WaitingCount", _collectBadge.transform, "", 26, Cream,
                                        new Vector2(48f, 36f), Centre, Vector2.zero);

            // Where a payout leaves from when no single card owns it — collecting the whole
            // track at once is several rungs on several rows, and aiming those at one of them
            // would say the others paid nothing.
            _payAnchor = UIKit.Box("PayFrom", dock.transform, Vector2.one * 8f, Centre,
                                   new Vector2(_width * .30f, -14f));

            // Slid rather than popped: a full-width bar that scales up from nothing reads as a
            // glitch, where the same bar arriving from below the fold reads as a dock.
            var seat = dock.rectTransform.anchoredPosition;
            dock.rectTransform.anchoredPosition = seat + new Vector2(0f, -Footer);
            Tween.Move(dock.rectTransform, seat, .38f, Ease.OutCubic).Delay(.08f);
        }

        // ----------------------------------------------------------------- lifecycle
        void OnEnable()
        {
            EventCollection.Changed += Refresh;
            StoreService.Changed += Refresh;
            StoreService.EventPassVerified += OnPassVerified;
            StoreService.Failed += OnPurchaseFailed;
            CloudSaveService.IdentityChanged += OnIdentity;
            CloudSaveService.Synced += OnSynced;
            PlayerProgression.Changed += OnWallet;
        }

        void OnDisable()
        {
            EventCollection.Changed -= Refresh;
            StoreService.Changed -= Refresh;
            StoreService.EventPassVerified -= OnPassVerified;
            StoreService.Failed -= OnPurchaseFailed;
            CloudSaveService.IdentityChanged -= OnIdentity;
            CloudSaveService.Synced -= OnSynced;
            PlayerProgression.Changed -= OnWallet;
            _generation++;
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
        }

        public override void OnPresented() { if (_retreat) Flow.Go<HomeScreen>(); else ReadPass(); }

        void OnIdentity()
        {
            _pass = null; _generation++; _requesting = false; _claiming = false;
            Refresh(); ReadPass();
        }

        void OnPassVerified(StoreProduct product)
        {
            if (product?.Id != _event?.PremiumProductId) return;
            if (_requesting) _refreshPending = true;
            else ReadPass();
        }

        void OnSynced() { if (!_claiming) ReadPass(); }

        void OnApplicationFocus(bool focused) { if (focused && _event != null) ReadPass(); }

        /// <summary>
        /// The wallet moved. Written through <see cref="ResourceSlots.Repaint"/> rather than
        /// straight onto the label, because a payout owns the readout while its tokens are in
        /// the air: writing the true figure mid-cascade jumps the pill forward and the next
        /// token drags it back down.
        /// </summary>
        void OnWallet()
        {
            ResourceSlots.Repaint(ResourceSlots.Kind.Credits, Profile.Coins);
            ResourceSlots.Repaint(ResourceSlots.Kind.Gems, Profile.Gems);
        }

        void OnPurchaseFailed(string id, StoreFailure failure, string detail)
        {
            if (id != _event?.PremiumProductId) return;
            var wording = StoreWording.Failure(failure);
            Scenery.Toast(Content, Loc.Get(wording.Key), wording.Tint, 3f);
            Refresh();
        }

        void Update()
        {
            if (_event == null) return;
            Pulse();
            if (Time.unscaledTime < _nextClock) return;
            _nextClock = Time.unscaledTime + 1f;
            RefreshClock();
            RefreshOffer();
        }

        /// <summary>
        /// The breath on everything that is waiting to be taken: every claimable card's ring,
        /// and the collect badge.
        ///
        /// One driver over every row rather than a looping tween per card, because a row is
        /// recycled under the animation — a tween owned by a card would go on brightening a
        /// ring around a rung that is no longer ready, and killing it would be one more thing
        /// <see cref="Paint"/> has to remember.
        /// </summary>
        void Pulse()
        {
            float k = .5f + .5f * Mathf.Sin(Time.unscaledTime * 3.1f);
            for (int i = 0; i < _rows.Count; i++)
            {
                PulseCard(_rows[i].Free, k, Mint);
                PulseCard(_rows[i].Premium, k, Gold);
            }
            if (_collectBadge && _collectBadge.enabled)
                _collectBadge.transform.localScale = Vector3.one * (1f + .07f * k);
        }

        static void PulseCard(Card card, float k, Color tint)
        {
            if (card?.Ring == null) return;
            card.Ring.color = card.Ready ? Pal.A(tint, .18f + .52f * k) : new Color(0f, 0f, 0f, 0f);
        }

        // -------------------------------------------------------------------- binding
        void BindRows(bool force)
        {
            if (_list == null) return;
            int first = Mathf.Clamp(Mathf.FloorToInt(_list.anchoredPosition.y / RowHeight),
                                    0, Mathf.Max(0, _event.Milestones.Count - _rows.Count));
            if (!force && _first == first) return;
            _first = first;

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                row.Index = first + i;
                var tier = _event.Milestones[row.Index];
                bool reached = _progress.Finished >= tier.Goal;

                row.Root.anchoredPosition = new Vector2(0f, -(row.Index + .5f) * RowHeight - 12f);
                row.Number.text = (row.Index + 1).ToString();
                row.Badge.color = reached ? Leaf : Ink;
                row.BadgeRim.color = reached ? Pal.A(Gold, .95f) : Pal.A(Mint, .45f);
                row.Number.color = reached ? Cream : Pal.A(Cream, .6f);
                row.Divider.gameObject.SetActive(row.Index < _event.Milestones.Count - 1);

                Paint(row.Free, tier, false);
                Paint(row.Premium, tier, true);
            }
        }

        void Paint(Card card, EventMilestone tier, bool premium)
        {
            bool reached = _progress.Finished >= tier.Goal;
            bool owned = !premium || (_pass != null && _pass.Owned);
            bool taken = premium ? owned && _pass.CollectedGoal >= tier.Goal
                                 : GroveEvents.IsCollected(_event, tier);
            bool ready = reached && owned && !taken;
            bool cache = tier.Goal % CacheEvery == 0, hoard = tier.Goal % HoardEvery == 0;

            long coins = premium ? tier.PremiumCredits : tier.Credits;
            long gems = premium ? tier.PremiumGems : 0;

            // A card is opaque and coloured by what it is worth, not by how dark the page is.
            // Ready is the brightest state on the screen, because it is the only one asking for
            // a tap; taken drops to a muted body so a finished rung is legible and quiet.
            card.Face.color = premium
                ? (taken ? new Color(.20f, .15f, .07f) : hoard ? PlumDeep : cache ? Amber : AmberDeep)
                : (taken ? new Color(.09f, .18f, .15f) : ready ? Leaf : LeafDeep);

            card.Edge.color = Pal.A(premium ? Gold : Mint, taken ? .26f : ready || cache ? .95f : .45f);
            card.Gloss.color = new Color(1f, 1f, 1f, taken ? .03f : .075f);
            card.Rays.enabled = cache && !taken;
            card.Rays.color = new Color(1f, .9f, .55f, hoard ? .22f : .14f);

            card.Art.sprite = Art.S(gems > 0
                ? (hoard ? "Ui/Shop/gems_5" : cache ? "Ui/Shop/gems_4" : "Ui/Shop/gems_2")
                : (hoard ? "Ui/Shop/coins_4" : cache ? "Ui/Shop/coins_3" : "Ui/Shop/coins_1"));
            card.Art.color = Pal.A(Color.white, taken ? .45f : 1f);

            card.Title.text = Loc.Get(cache ? "ui.pass.cache" : gems > 0 ? "ui.pass.gems" : "ui.pass.coins");
            card.Title.color = Pal.A(premium ? Gold : Mint, taken ? .55f : 1f);

            card.Amount.text = coins > 0 && gems > 0
                ? Loc.Format("ui.pass.pair", Compact.Number(coins), Compact.Number(gems))
                : Compact.Number(gems > 0 ? gems : coins);
            card.Amount.fontSize = coins > 0 && gems > 0 ? 27 : 37;
            card.Amount.color = Pal.A(Cream, taken ? .55f : 1f);

            card.Status.text = Loc.Get(taken ? "ui.pass.claimed"
                                     : ready ? "ui.pass.claim"
                                     : premium && !owned ? "ui.pass.pass_required"
                                     : "ui.pass.keep_playing");
            card.Status.color = taken ? Pal.A(Cream, .5f)
                              : ready ? Cream
                              : premium ? Pal.A(Gold, .9f)
                              : Pal.A(Cream, .62f);

            card.Lock.gameObject.SetActive(premium && !owned);
            card.Seal.gameObject.SetActive(taken);
            card.Ready = ready;
            card.Ring.enabled = ready;
            card.Button.interactable = ready && !_claiming;
            card.Face.gameObject.SetActive(!premium || _event.HasPremium);

            // Interrupted mid-punch, a card keeps whatever scale the tween stopped at. Reset
            // on every paint is the cheap half of the fix; the other half is that the flashy
            // part of a claim happens on the payout layer and not on the row (see Pay).
            card.Face.transform.localScale = Vector3.one;

            if (taken && premium == _stampPremium && tier.Goal > _stampFrom && tier.Goal <= _stampTo)
                Stamp(card, tier.Goal - _stampFrom);
        }

        /// <summary>The seal landing, staggered by how far up the swept range this rung sits.</summary>
        static void Stamp(Card card, int order)
        {
            var seal = card.Seal.transform;
            seal.localScale = Vector3.zero;
            Tween.Pop(seal, 0f, .42f, Mathf.Min(.5f, .04f * Mathf.Max(0, order - 1)));
        }

        void Refresh()
        {
            if (_event == null || _list == null) return;

            _progress = GroveEvents.ProgressOf(_event);
            _progressLabel.text = Loc.Format("ui.pass.progress",
                                             Mathf.Min(_progress.Finished, _event.FinalGoal), _event.FinalGoal);

            float track = _width - 32f;
            float run = Mathf.Max(1f, track * Mathf.Clamp01((float)_progress.Finished / Mathf.Max(1, _event.FinalGoal)));
            _fill.rectTransform.sizeDelta = new Vector2(run, 18f);
            _fillHead.rectTransform.anchoredPosition = new Vector2(4f + run, 0f);
            _fillHead.enabled = _progress.Finished > 0;

            BindRows(true);
            RefreshClock();
            RefreshOffer();
        }

        void RefreshClock()
        {
            long left = _event.SecondsLeftAt(GameClock.NowUnix());
            _timer.text = left <= 0 ? Loc.Get("ui.event.ended")
                                    : Loc.Format("ui.pass.time", left / 86400, left % 86400 / 3600);
            _timer.color = left <= 0 ? Pal.A(Cream, .7f) : left < 86400 ? Pal.Ember : Gold;
        }

        StoreProduct Product => StoreRules.Find(_event.PremiumProductId);

        void RefreshOffer()
        {
            if (_offerLabel == null) return;

            bool owned = _pass != null && _pass.Owned;
            bool live = _event.IsLiveAt(GameClock.NowUnix());
            var product = Product;
            var offer = product != null ? StoreService.OfferFor(product) : default;

            string caption = Loc.Get(owned ? "ui.pass.unlocked"
                                   : _requesting ? "ui.pass.connecting"
                                   : "ui.pass.upgrade");

            // The price the moment the store knows it. It used to wait on the pass state as
            // well, so a client that could reach the store but not our own function showed
            // "Unlock Bloom Pass" with no price on a product it could perfectly well sell.
            if (!owned && !_requesting && live && offer.CanBuy)
                caption = Loc.Format("ui.pass.buy", offer.Price);

            _offerBtn.SetCaption(caption);

            _hint.text = Loc.Get(owned ? "ui.pass.owned_hint"
                               : !live ? "ui.pass.closed_hint"
                               : "ui.pass.purchase_hint");

            _collect.SetCaption(Loc.Get(_claiming ? "ui.pass.claiming" : "ui.pass.claim_all"));

            int waiting = Waiting();
            if (_collectBadge)
            {
                _collectBadge.enabled = waiting > 0;
                _collectBadgeText.enabled = waiting > 0;
                _collectBadgeText.text = waiting > 9 ? "9+" : waiting.ToString();
                if (waiting == 0) _collectBadge.transform.localScale = Vector3.one;
            }
            if (_collect) _collect.Interactable = !_claiming;
        }

        /// <summary>
        /// How many rungs a tap on COLLECT ALL would actually hand over, both tracks counted.
        ///
        /// Counted rather than read off <see cref="EventProgress.Waiting"/>, which knows only
        /// about the free track: a player who has bought the pass and cleared ten glades is
        /// owed up to twenty rewards, and a badge that said ten would be wrong in the one
        /// direction that costs somebody money they earned.
        /// </summary>
        int Waiting()
        {
            int waiting = 0;
            bool owned = _pass != null && _pass.Owned;
            foreach (var tier in _event.Milestones)
            {
                if (_progress.Finished < tier.Goal) break;
                if (!GroveEvents.IsCollected(_event, tier)) waiting++;
                if (owned && _pass.CollectedGoal < tier.Goal &&
                    (tier.PremiumCredits > 0 || tier.PremiumGems > 0)) waiting++;
            }
            return waiting;
        }

        // ----------------------------------------------------------------- pass state
        async void ReadPass()
        {
            if (_event == null || !_event.HasPremium || _requesting || _lifetime == null) return;

            _requesting = true;
            int generation = _generation;
            RefreshOffer();
            try
            {
                var (result, state) = await CloudSaveService.EventPassAsync(_event.Id, 0, _lifetime.Token);
                if (this && generation == _generation) _pass = result.Ok ? state : null;
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                if (this && generation == _generation)
                {
                    _requesting = false;
                    Refresh();
                    if (_refreshPending) { _refreshPending = false; ReadPass(); }
                }
            }
        }

        /// <summary>
        /// Opens the payment sheet, or says why it cannot.
        ///
        /// <para>
        /// <b>A pass state we have not read is not a reason to refuse the sale.</b> The
        /// entitlement is written by <c>redeemPurchase</c> from the receipt, a refused receipt
        /// is never confirmed, and both stores re-deliver an unfinished transaction for ever
        /// (invariant 18a) — so the only thing a failed read costs is the price on the button.
        /// Refusing here made an undeployed function look exactly like a broken shop, which is
        /// how this screen shipped with a product nobody could buy.
        /// </para>
        /// </summary>
        void BuyPass()
        {
            if (_pass != null && _pass.Owned) { Scenery.Toast(Content, Loc.Get("ui.pass.owned_hint"), Gold); return; }
            if (!_event.IsLiveAt(GameClock.NowUnix())) { Scenery.Toast(Content, Loc.Get("ui.pass.closed_hint"), Gold); return; }

            // A read already in flight settles in a moment and may carry an "owned" we would
            // otherwise sell over. Everything else is the store's to answer.
            if (_requesting) { Scenery.Toast(Content, Loc.Get("ui.pass.connecting"), Pal.Aqua); return; }

            StoreTap.Buy(this, Product);
        }

        // -------------------------------------------------------------------- claiming
        void Claim(int index, bool premium)
        {
            if (_claiming || index < 0 || index >= _event.Milestones.Count) return;
            var tier = _event.Milestones[index];
            var card = CardFor(index, premium);

            if (premium) { ClaimPremium(tier.Goal, card); return; }

            long coins = FreeWorth(tier.Goal);
            if (coins <= 0) return;

            // Snapshotted before the grant, which is the shape RewardFlight insists on: the
            // ledger moves the instant Collect returns, and deriving "what the pill said
            // before" afterwards is only exact where nothing can clamp the grant.
            var flight = RewardFlight.Begin();
            int floor = EventCollection.CollectedGoal(_event.Id);
            if (GroveEvents.Collect(_event, tier.Goal) <= 0) return;
            Pay(card, floor, tier.Goal, false, flight, coins, 0L);
        }

        void ClaimAll()
        {
            if (_claiming) return;

            int goal = 0;
            foreach (var tier in _event.Milestones) if (tier.Goal <= _progress.Finished) goal = tier.Goal;
            if (goal == 0) { Scenery.Toast(Content, Loc.Get("ui.pass.keep_playing"), Mint); return; }

            long coins = FreeWorth(goal);
            int floor = EventCollection.CollectedGoal(_event.Id);
            var flight = coins > 0 ? RewardFlight.Begin() : null;
            bool collected = GroveEvents.Collect(_event, goal) > 0;

            // The premium half is the server's, so it goes second and celebrates itself. The
            // free half is already banked either way — a premium claim that fails must never
            // swallow rewards the player has in hand.
            if (collected && flight != null) Pay(null, floor, goal, false, flight, coins, 0L);

            if (_pass != null && _pass.Owned && _pass.CollectedGoal < goal) ClaimPremium(goal, null);
            else if (!collected) Scenery.Toast(Content, Loc.Get("ui.pass.up_to_date"), Mint);
        }

        async void ClaimPremium(int goal, Card card)
        {
            if (_claiming || _lifetime == null) return;

            _claiming = true;
            int generation = _generation;
            int floor = _pass?.CollectedGoal ?? 0;
            Refresh();
            try
            {
                var (result, state) = await CloudSaveService.EventPassAsync(_event.Id, goal, _lifetime.Token);
                if (!this || generation != _generation) return;

                if (result.Ok)
                {
                    _pass = state;

                    // AfterGrant rather than Begin: the wallet reply has already been applied
                    // by the time this resumes, so the snapshot has to be today's balance with
                    // the grant taken back off it — the shop's case exactly, and safe for the
                    // same reason (credits and gems are the two things nothing clamps).
                    if (state.Credits > 0 || state.Gems > 0)
                        Pay(card, floor, goal, true,
                            RewardFlight.AfterGrant(state.Credits, state.Gems), state.Credits, state.Gems);
                    else
                        Scenery.Toast(Content, Loc.Get("ui.pass.up_to_date"), Mint);
                }
                else Scenery.Toast(Content, Loc.Get("ui.pass.retry"), Gold, 3f);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (this) Scenery.Toast(Content, Loc.Get("ui.pass.retry"), Gold);
            }
            finally { if (this && generation == _generation) { _claiming = false; Refresh(); } }
        }

        /// <summary>What the free track still owes up to and including <paramref name="goal"/>.</summary>
        long FreeWorth(int goal)
        {
            long worth = 0;
            foreach (var tier in _event.Milestones)
                if (!GroveEvents.IsCollected(_event, tier) && tier.Goal <= goal &&
                    tier.Goal <= _progress.Finished) worth += tier.Credits;
            return worth;
        }

        /// <summary>
        /// The reward handed over: the card lights, a ring breaks out of it, and the currency
        /// leaves as tokens that fly into the pills at the top and step them as they land.
        ///
        /// <para>
        /// <b>Everything that moves is drawn on the payout layer, never on the row.</b> The
        /// cascade shrinks and fades whatever it is thrown from — that is what makes it read
        /// as the card being emptied rather than copied — and a recycled row bound to another
        /// tier would be left at a tenth of its size with nothing to put it back. So a decoy
        /// carrying the card's own art is stood at the card's position and thrown instead, and
        /// the real card simply repaints as collected under it.
        /// </para>
        /// <para>
        /// The repaint happens first, so a player who leaves mid-flight comes back to a page
        /// that already says the reward is theirs.
        /// </para>
        /// </summary>
        void Pay(Card card, int floor, int goal, bool premium, RewardFlight flight, long coins, long gems)
        {
            // Two origins, not one. The burst comes off the middle of the card because that is
            // where the eye is, and the decoy leaves from the *art*, because a copy of the coin
            // pile standing beside the coin pile reads as a duplicate rather than as the prize
            // being lifted out.
            Vector2 at = card != null && card.Face != null ? TokenFlight.LocalIn(_fx, card.Face.transform)
                       : _payAnchor != null ? TokenFlight.LocalIn(_fx, _payAnchor)
                       : Vector2.zero;
            Vector2 from = card != null && card.Art != null ? TokenFlight.LocalIn(_fx, card.Art.transform) : at;
            Sprite art = card != null && card.Art != null ? card.Art.sprite : null;

            _stampFrom = floor;
            _stampTo = goal;
            _stampPremium = premium;
            Refresh();
            _stampTo = 0;

            // The currency's own colour, not the track's. Which column a reward came from is
            // what the card says; what the celebration is *for* is the money, and this game
            // already means gold by credits and pink by gems everywhere else it pays either.
            Color tone = coins > 0 ? Pal.Gold : Pal.Bloom;

            Audio.Sfx("collect");
            Audio.Sfx("chime", .8f, 1f, .10f);

            bool big = coins + gems * 20L >= 900L;
            if (big) Audio.Sfx("reward", .9f, 1f, .18f);

            Shock(at, tone, big);
            Burst.Sparks(_fx, at, tone, big ? 26 : 16, big ? 330f : 250f, 24f, .68f);
            Rise(at, coins, gems, tone);

            bool any = false;
            bool paired = coins > 0 && gems > 0;
            if (coins > 0)
                any |= flight.Add(new ChestDrop(ChestDropKind.Credits, (int)Mathf.Min(coins, int.MaxValue)),
                                  Decoy(from, art ?? Art.S("Ui/Shop/coins_1"), -1f, paired));
            if (gems > 0)
                any |= flight.Add(new ChestDrop(ChestDropKind.Gems, (int)Mathf.Min(gems, int.MaxValue)),
                                  Decoy(from, Art.S("Ui/Shop/gems_3"), 1f, paired));

            if (!any) { Scenery.Toast(Content, Loc.Get("ui.pass.collected"), Mint); return; }
            flight.Play(_fx, null);
        }

        /// <summary>
        /// A throwaway copy of the reward, stood where the card is so the cascade has something
        /// of its own to empty. See <see cref="Pay"/> for why the real card is never thrown.
        /// </summary>
        RectTransform Decoy(Vector2 at, Sprite art, float side, bool paired)
        {
            var img = UIKit.Img("Given", _fx, art, Color.white, new Vector2(132f, 116f),
                                Centre, at + (paired ? new Vector2(side * 46f, 0f) : Vector2.zero));
            img.preserveAspect = true;
            Tween.After(2.4f, () => { if (img) Destroy(img.gameObject); }, _fx);
            return img.rectTransform;
        }

        /// <summary>
        /// The ring that breaks out of a claimed card. Two on a big one.
        ///
        /// The first cut drew a 7-unit line fading on the square of the remaining time, and it
        /// was invisible in a capture taken while it was on screen — a shockwave has to be
        /// thick and bright for the first third of its life or it is an artefact rather than an
        /// impact.
        /// </summary>
        void Shock(Vector2 at, Color tint, bool big)
        {
            for (int i = 0; i < (big ? 2 : 1); i++)
            {
                var ring = UIKit.Img("Shock", _fx, Art.Ring(160, 16f), Pal.A(Pal.Lift(tint, .35f), 1f),
                                     new Vector2(190f, 190f), Centre, at);
                var rt = ring.rectTransform;
                Tween.Run(big ? .82f : .66f, Ease.OutQuint, t =>
                {
                    if (!rt) return;
                    rt.localScale = Vector3.one * Mathf.Lerp(.28f, big ? 4.8f : 3.4f, t);
                    ring.color = Pal.A(Pal.Lift(tint, .35f), t < .28f ? 1f : 1f - (t - .28f) / .72f);
                }, ring).Delay(i * .13f).OnDone(() => { if (ring) Destroy(ring.gameObject); });
            }
            if (big) Burst.Confetti(_fx, 46);
        }

        /// <summary>The figure, lifted off the card and let go — the one place the amount is
        /// printed at a size that says it matters.</summary>
        void Rise(Vector2 at, long coins, long gems, Color tint)
        {
            string line = coins > 0 && gems > 0
                ? Loc.Format("ui.pass.pair", Compact.Number(coins), Compact.Number(gems))
                : Loc.Format("ui.event.reward", Compact.Number(coins > 0 ? coins : gems));

            var label = UIKit.Titled("Given", _fx, line, coins > 0 && gems > 0 ? 40 : 56, tint,
                                     TextAnchor.MiddleCenter, new Vector2(460f, 130f), Centre,
                                     at + new Vector2(0f, 20f), 4f, 5f);
            var rt = label.rectTransform;
            var from = rt.anchoredPosition;
            Tween.Run(1.05f, Ease.OutCubic, t =>
            {
                if (!rt) return;
                rt.anchoredPosition = from + new Vector2(0f, 168f * t);
                rt.localScale = Vector3.one * Mathf.Lerp(.55f, 1.12f, Mathf.Min(1f, t * 4.5f));
                label.color = Pal.A(tint, t < .6f ? 1f : 1f - (t - .6f) / .4f);
            }, label).OnDone(() => { if (label) Destroy(label.gameObject); });
        }

        /// <summary>The card a tier is drawn on right now, or null if it has scrolled away.</summary>
        Card CardFor(int index, bool premium)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Index == index) return premium ? _rows[i].Premium : _rows[i].Free;
            return null;
        }

        // ---------------------------------------------------------------------- helpers
        static Image Panel(string name, Transform parent, Vector2 size, Vector2 anchor, Vector2 pos,
                           Color color, bool outline = false)
        {
            var image = UIKit.Img(name, parent, outline ? Art.RoundOutline(24, 3f) : Art.Round(24),
                                  color, size, anchor, pos);
            image.type = Image.Type.Sliced;
            return image;
        }

        static Text Caption(string name, Transform parent, string value, int size, Color color,
                            Vector2 box, Vector2 anchor, Vector2 pos,
                            TextAnchor align = TextAnchor.MiddleCenter)
            => UIKit.Shrinkable(UIKit.Label(name, parent, value, size, color, align, box, anchor, pos),
                                Mathf.Max(18, size - 10));

        /// <summary>A slow endless turn, for the fans behind a prize.</summary>
        static void Spin(RectTransform rt, float degreesPerSecond)
            => Tween.Run(360f / Mathf.Max(1f, Mathf.Abs(degreesPerSecond)), Ease.Linear,
                         t => { if (rt) rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sign(degreesPerSecond) * 360f * t); },
                         rt).Loop(-1, false);
    }
}
