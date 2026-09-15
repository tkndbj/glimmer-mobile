using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using GlimmerGrove.Utilities;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Where a player arranges the line they will take into every siege: which turret stands on
    /// each colour, and what they are carrying.
    ///
    /// <para>
    /// <b>Set once and used everywhere, which is the whole shape of the feature.</b> Nothing about
    /// a level, a chapter or a mode reaches the loadout — a line is a fact about the account
    /// (<c>WardLoadout</c>), so a player arranges it here and walks into any rung with it. That is
    /// the same argument invariant 39a makes for holding utility stock account-wide: a loadout
    /// kept per level would make the turrets part of a board's difficulty, which is exactly what
    /// invariant 29c refuses a companion's ability.
    /// </para>
    /// <para>
    /// <b>The four slots are colours rather than positions</b>, because colour is what the
    /// decision is actually about: the elemental double, a bulwark's soak and which ward a cog
    /// upgrades are all colour questions, and a level may stand fewer than four wards. See
    /// <c>WardLine</c>.
    /// </para>
    /// <para>
    /// <b>Every turret is drawn in the colour it would stand on.</b> The four seats wear the line
    /// the player takes into a run, and the grid wears whichever seat is being filled — so tapping
    /// a different slot turns the whole screen that colour, which says what no caption can: a
    /// turret has no colour of its own, and the colour is the seat. It is the real board sprite
    /// rather than a thumbnail, which is <em>not</em> invariant 16c being relaxed — a turret is cut
    /// at 192x240, smaller than the thumbnail it replaces, so the true picture costs no more than
    /// the cheap one. It is still held rather than global, and still leaves
    /// with the screen.
    /// </para>
    /// </summary>
    public sealed partial class LoadoutScreen : View
    {
        public override string Track => "mus_menu";

        /// <summary>Which tab is showing: the turrets, or what the player carries.</summary>
        public enum Shelf { Wards, Items }

        Shelf _shelf = Shelf.Wards;

        /// <summary>
        /// Which colour of the line the grid is filling.
        ///
        /// <b>A selection rather than a drag, and it is the one interaction decision here.</b> The
        /// alternative — drag a turret from the grid onto a slot — reads well on a desk and badly
        /// on a phone, where the grid scrolls under the same finger. Tapping a slot and then a
        /// turret is two taps that can each be taken back.
        /// </summary>
        int _slot;

        RectTransform _viewport, _grid, _line, _tabRow;

        /// <summary>
        /// The KIT tab, kept so a lesson can ring the real control rather than a description of
        /// where it is (<c>TipOverlay.Target</c>).
        ///
        /// <b>Re-taken on every <see cref="Rebuild"/></b>, because that throws the tab row away
        /// and builds it again - a field holding the old one would by then be a destroyed object,
        /// and <c>TipOverlay</c> answers a destroyed target by quietly drawing no ring at all.
        /// </summary>
        RectTransform _kitTab;

        readonly List<SlotView> _slots = new List<SlotView>();

        /// <summary>
        /// The turret picture in each shelf cell, so art arriving can be <em>dressed</em> on rather
        /// than repainted in.
        ///
        /// <b>This is what stopped the screen reloading the instant it opened.</b> The scope is
        /// asynchronous, so the shelf is drawn once with no sprites and again when they land - and
        /// the second one was a full <c>Paint</c>, which destroys every cell, builds them afresh
        /// and replays the entrance. That is invariant 16d exactly: a new list is a <c>Show</c> and
        /// the same list redrawn is a <c>Refresh</c>, and art arriving is the second one.
        /// </summary>
        readonly Dictionary<string, Image> _shelfIcons = new Dictionary<string, Image>();

        /// <summary>Whether this shelf has already made its entrance. See <see cref="_shelfIcons"/>.</summary>
        bool _shown;

        const float HeaderHeight = 232f;
        const float LineHeight = 250f;
        const float TabsHeight = 104f;

        /// <summary>
        /// The air either side of the row, in canvas units.
        ///
        /// <b>Units rather than a share of the display</b>, which is <c>CanvasFit</c>'s own
        /// doctrine: a margin does not grow because a screen is squarer, it is drawn smaller
        /// along with everything else.
        /// </summary>
        const float RowGutter = 38f;

        /// <summary>
        /// How wide one row of the shelf is drawn: the canvas, less a gutter either side.
        ///
        /// <para>
        /// <b>The canvas rather than 1,080</b>, because <c>CanvasFit</c> widens a squarer
        /// display's canvas instead of scaling a phone's — every phone is 1,080 units across and
        /// a tablet is 1,350 to 1,620. Pinned to the phone's row, this shelf would draw as a
        /// 1,004-unit column down the middle of a 1,620-unit screen with three hundred units of
        /// nothing either side, every card drawn at two thirds the size for no reason at all.
        /// </para>
        /// <para>
        /// <b>And it is not invariant 37cc being broken; it is 37cc's own reason read the other
        /// way.</b> A siege field may not spend a tablet's extra width because its cell is
        /// square, so every unit it takes across it takes back out of the hill and the ward line
        /// — the width was bought to buy height, and is not the board's to spend. A shelf is a
        /// scrolling grid that fights nothing for height. <b>Before spending a tablet's width,
        /// ask what the thing being widened would be taking it from.</b>
        /// </para>
        /// <para>
        /// <b><c>Boot.CanvasWidth</c> rather than the viewport's own rect</b>, which is 43c's
        /// rule the other way up and worth stating once: a constant lies when the thing it
        /// describes has moved, and a <em>rect</em> lies when nothing has laid it out yet. This
        /// is read while the grid is being built, a frame before uGUI resolves anything, and
        /// <c>CanvasFit.WidthFor</c> is a pure function of the screen — which is the same reason
        /// <c>SplashScreen.Fit</c> reads it rather than measuring.
        /// </para>
        /// </summary>
        static float RowSpan => Boot.CanvasWidth - RowGutter * 2f;

        /// <summary>
        /// How many cells across: <b>two on a phone and four on a tablet</b>.
        ///
        /// <para>
        /// <b>A declared pair rather than a count derived from the width, and the argument is
        /// the threshold rather than the arithmetic</b> — which is worth saying plainly, because
        /// the arithmetic one was written here first and is no longer true. This number used to
        /// be defended as "twenty divides by four": at three across, twenty turrets were six
        /// full rows and a row of two, so the bottom-right cell — where anybody looks for "the
        /// last one" — was <em>empty</em>, and it was reported as the shelf being in the wrong
        /// order when the order was right the whole time. <b>That stopped being the reason the
        /// day <see cref="TierBadge"/> shipped.</b> A band starts a fresh row, the bands hold
        /// ten, seven and three, and <em>no</em> column count leaves all three full — two across
        /// leaves 0, 1 and 1 over; three leaves 1, 1 and 0; four leaves 2, 3 and 3. It does not
        /// matter, and the render is what says so: a part-full row under a header reads as the
        /// end of that band rather than as a hole in a grid, which is the whole of what 37bc
        /// bought by laying this out with a cursor instead of <c>i / Columns</c>.
        /// </para>
        /// <para>
        /// <b>So what decides it is that every tablet should draw the same shelf.</b> The switch
        /// is <c>CanvasFit</c>'s own threshold, which sits in the gap between the squarest phone
        /// (16:9) and the tallest tablet (16:10): no shipping display is near it and none can be
        /// on both sides of it from one frame to the next. A count ramped off the canvas width
        /// would answer <b>three</b> on a 16:10 tablet and <b>four</b> on a 4:3 — two devices a
        /// player would call the same thing, drawing two different screens, with the ragged
        /// bands landing in different places on each. A threshold has one answer per kind of
        /// display, which is the same shape, for the same reason, as <c>PhoneFloor</c> itself.
        /// </para>
        /// <para>
        /// <b>Whoever changes the roster's size owns both numbers</b> — not because the total
        /// has to divide, but because the card is sized by them: four across a phone drew each
        /// turret 130 units wide, and six across a tablet would do it again.
        /// </para>
        /// <para>
        /// <b>What two across costs is the scroll</b>, which is the trade and not a fault: ten
        /// rows on a phone where there were five. What it buys is a card wide enough for the
        /// picture to be the thing being judged — this shelf's whole job is a choice between
        /// twenty silhouettes, and at four across a phone drew each one 130 units wide.
        /// </para>
        /// </summary>
        static int Columns => Boot.ShortCanvas ? TabletColumns : PhoneColumns;

        const int PhoneColumns = 2, TabletColumns = 4;

        /// <summary>
        /// The cell this card was drawn at — a phone's, two across — and what
        /// <see cref="Scale"/> is measured against.
        /// </summary>
        const float DesignW = 492f;

        /// <summary>
        /// How far this card is from the one it was designed as, which is
        /// <c>PieceCard.ScaleFor</c>'s idiom and is here for its reason: one design, drawn at
        /// whatever size the row has room for.
        ///
        /// <para>
        /// <b>It is 1 on every phone by construction</b> — 1,080 less two gutters, less one gap,
        /// halved, is <see cref="DesignW"/> exactly — so a tablet is the only display this
        /// arithmetic does anything on, and a change to the card is still a change to the
        /// numbers a phone draws.
        /// </para>
        /// <para>
        /// <b>The gaps are deliberately outside it.</b> Scaling them would make
        /// <see cref="CellW"/> depend on a scale derived from <see cref="CellW"/>; they are two
        /// small constants in units, so a tablet's denser grid simply keeps a phone's gutters
        /// between its cards.
        /// </para>
        /// </summary>
        static float Scale => CellW / DesignW;

        /// <summary>
        /// One cell, and the vertical structure inside it - shared by both shelves, because the
        /// two grids differing anywhere is a difference nobody chose.
        ///
        /// <para>
        /// <b>The width is derived, never typed</b> (invariant 16l): the row is
        /// <see cref="RowSpan"/> and the gaps are known, so the cell is what is left over
        /// divided by the columns. Typed as a literal beside a column count it is two numbers
        /// that have to agree, and the day one moves the grid draws off-centre or overlaps with
        /// every gate green - a layout fault no test in this project can see.
        /// </para>
        /// <para>
        /// <b>The height was authored rather than scaled off the width</b>, because a card is a
        /// stack of a picture, a name and a strip and only the picture grows with the cell.
        /// Twice the width at twice the height is a card two thirds air; this is the same
        /// five-part structure with the picture given the room the second column paid for. What
        /// it <em>does</em> follow is <see cref="Scale"/>, which is a different thing: the card
        /// keeps its proportions on a display that draws the whole interface smaller.
        /// </para>
        /// <para>
        /// <b>And it is one structure again, which it briefly was not.</b> A kit cell used to be
        /// 36 units taller than a turret's to carry a sentence about what the item does; the
        /// turret shelf had already dropped its own for the reason below, and the kit's went the
        /// same way on the owner's call. There is nothing left for the two to differ about, so
        /// the ward-only numbers are gone rather than kept equal — two constants holding one
        /// figure is the shape this project keeps recording as the thing that drifts.
        /// </para>
        /// <para>
        /// <b>What a thing does is a sentence, and a sentence belongs on the panel.</b> Twenty
        /// turrets or four utilities, each with a line of small print, is a wall of prose on a
        /// screen whose job is a choice between pictures — and every one of those sentences is
        /// already drawn one tap in, on the panel where somebody is deciding rather than
        /// scanning. What the space buys is a price big enough to read at a glance, which is the
        /// number a shelf is really about.
        /// </para>
        /// </summary>
        static float CellW => (RowSpan - (Columns - 1) * CellGapX) / Columns;

        static float CellH => 470f * Scale;

        /// <summary>
        /// The plate's corner, and it <b>does not scale with the cell</b>.
        ///
        /// <para>
        /// <b>A nine-sliced sprite's corner is a fact about the sprite, not about the rect</b>
        /// (invariant 44a from the other end): the kit plate under every cell draws its corner
        /// at one sprite pixel per UI unit whatever size the card is, so the generated veil and
        /// rim that have to sit on top of it keep the radius they always had. Scaling it with
        /// the card is how a veil comes to show four slivers of plate at its corners.
        /// </para>
        /// <para>
        /// <b>The price strip's seat is the opposite case and does scale</b>: it is a generated
        /// sprite with nothing to match, so its corner is a ratio of its own height rather than
        /// a number that has to agree with a bought one.
        /// </para>
        /// </summary>
        const int CellRadius = 24;
        const float CellGapX = 20f, CellGapY = 22f;

        /// <summary>The picture: square, centred, a margin down from the cell's own top.</summary>
        static float IconTop => 30f * Scale;
        static float IconBox => CellW * .55f;

        /// <summary>The name's middle, measured down from the cell's top.</summary>
        static float NameY => 336f * Scale;

        /// <summary>
        /// Where a held turret's star ladder sits, measured down from the cell's top.
        ///
        /// <b>Between the name and the foot</b>, which is the band a for-sale card spends on its
        /// price — so a cell is the same height whichever it is, and the eye finds one thing or
        /// the other in the same place rather than in two.
        /// </summary>
        static float StarsY => 395f * Scale;

        /// <summary>
        /// How wide one of those stars is drawn.
        ///
        /// <b>Passed rather than left to <c>WardStarRow</c>'s default</b>, which is the size the
        /// preview panel draws at: a row keeps its own arithmetic (its width, its step, where
        /// the upgrade ceremony drops a star) and every screen says how big it wants it. At the
        /// default a five-star ladder is 158 units under a 468-wide name, which reads as a
        /// readout that belongs to something else.
        /// </summary>
        static float StarSize => 32f * Scale;

        /// <summary>The price strip's middle, measured <em>up</em> from the cell's foot.</summary>
        static float FootY => 57f * Scale;

        /// <summary>
        /// A scaled size as a font size.
        ///
        /// <c>Text.fontSize</c> is an int and <c>UIKit.Shrinkable</c> clamps its floor against
        /// it, so both have to be rounded at the same moment rather than one of them drifting a
        /// point on a tablet. The same helper, for the same reason, as <c>PieceCard.Pt</c>.
        /// </summary>
        static int Pt(float size) => Mathf.Max(1, Mathf.RoundToInt(size));

        /// <summary>The parts of a slot that change when the line does.</summary>
        sealed class SlotView
        {
            public int Colour;
            public Image Seat, Edge, Icon, Glow;
            public Text Name;

            /// <summary>The padlock over a seat the player has not reached yet.</summary>
            public Image Lock;
        }

        /// <summary>
        /// How many levels of this mode's own ladder the player has cleared.
        ///
        /// <b>Read once per paint rather than per slot</b>, since it walks the mode's levels — and
        /// held in a field rather than recomputed, because the line is painted on every repaint
        /// and the answer cannot change while this screen is open.
        /// </summary>
        int _cleared;

        protected override void Build()
        {
            Scenery.Plain(Content);

            BuildGrid();
            BuildLine();
            BuildTabs();
            BuildHeader();

            Paint();

            // The shelf's twenty thumbnails. Asked for rather than awaited: the grid draws itself
            // now and repaints when they land, because an `Image` with a null sprite is a white
            // rectangle rather than a blank (invariant 7b) and a screen that waited would show
            // nothing at all on a slow load.
            Browse();

            // Repainted on the ledgers' own events rather than on a callback from whatever panel
            // did the buying, which is `CompanionScreen`'s hard-won rule: a callback has to be
            // threaded through every exit a panel has, and the silent ones are exactly how a thing
            // somebody just bought stays behind a padlock until the screen is left and re-entered.
            WardLedger.Changed += Paint;
            WardLoadout.Changed += Paint;
            UtilityLedger.Changed += Paint;
            PlayerProgression.Changed += Paint;
        }

        /// <summary>
        /// Loads the roster's thumbnails, and repaints when they arrive.
        ///
        /// <b>`async void` with the exception caught</b>, which is <c>CompanionArt.Load</c>'s
        /// shape and for its reason: a scope that failed to load must not vanish silently.
        /// </summary>
        AssetHold _shelfArt;

        void Browse() => Run(async token =>
        {
            _shelfArt = _shelfArt ?? AssetLibrary.Hold("ward_shelf");
            await _shelfArt.LoadAsync(AssetManifest.WardShelfAssets(WardLedger.Catalog.Models),
                                      null, token);

            if (Living) Dress();
        });

        /// <summary>
        /// Puts the art on what is already drawn, and moves nothing.
        ///
        /// The answer to a scope arriving, and to nothing else - see <see cref="_shelfIcons"/>.
        /// </summary>
        void Dress()
        {
            PaintLine();

            foreach (var pair in _shelfIcons)
            {
                if (pair.Value == null) continue;

                var model = WardLedger.Catalog.Find(pair.Key);
                if (model == null) continue;

                pair.Value.sprite = AssetLibrary.Sprite(AssetManifest.WardArt(model, _slot));
            }
        }

        void OnDestroy()
        {
            // Let go on the way out, which is the whole reason it is held rather than global:
            // twenty pictures resident for the life of a session to draw one screen is memory
            // bounded by how much content exists rather than by what is on the screen (7b).
            _shelfArt?.Dispose();

            WardLedger.Changed -= Paint;
            WardLoadout.Changed -= Paint;
            UtilityLedger.Changed -= Paint;
            PlayerProgression.Changed -= Paint;
        }

        // ----------------------------------------------------------------- chrome
        void BuildHeader()
        {
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64),
                                 new Color(.02f, .06f, .09f, .82f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            frt.sizeDelta = new Vector2(0f, HeaderHeight + LineHeight);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);
            frt.SetAsFirstSibling();

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, 1f), new Vector2(96f, -110f), () => Flow.Go<LevelsScreen>());

            // The two lessons again, at the player's asking. Top-right, in the orange the info
            // key is cut in on every other screen that has one (`Skins.Aside`), and mirrored on
            // the back key so the header reads as a pair of corners with a ribbon between them.
            //
            // **It is here because this screen is one a player comes back to.** A board lesson is
            // shown once in a lifetime and that is right - the board it is about will not be
            // there next time. The shelf is furniture: somebody who bought their first turret
            // months ago and is now spending gems on a second colour is entitled to re-read the
            // rule about which is which, and `TipLedger` on its own can only ever say no.
            UIKit.IconButton("Info", Safe, Skins.Aside, "ic_info", new Vector2(118f, 118f),
                             new Vector2(1f, 1f), new Vector2(-96f, -110f), Review);

            var banner = Scenery.TitleRibbon(Safe, Loc.Get("ui.loadout.title").ToUpperInvariant(),
                                             new Vector2(520f, 140f), new Vector2(.5f, 1f),
                                             new Vector2(0f, -110f), 42, 22f);
            banner.transform.localScale = Vector3.zero;
            Tween.Pop(banner.transform, 0f, .6f, .1f);

            // The two balances a shelf here spends. Built out of the same pill the shop draws,
            // so a price on a cell and the purse above it read as the same currency.
            //
            // The caption that used to sit above this row is gone: it counted the turrets held
            // out of the twenty, which every cell on the shelf already says by being lit or not,
            // and it is the last of the small print both shops shed.
            var row = UIKit.Row("Balances", Safe, new Vector2(640f, 68f), new Vector2(.5f, 1f),
                                new Vector2(0f, -208f), 14f);

            Balance(row, Pal.Gold, null, Compact.Number(Profile.Coins));
            Balance(row, Pal.Bloom, "ic_gem", Compact.Number(Profile.Gems));
        }

        /// <summary>One balance pill. The shop's, without the flare registry a shelf needs.</summary>
        static void Balance(Transform row, Color tint, string icon, string value)
        {
            var pill = UIKit.Img("Pill", row, Art.S("Ui/" + Skins.Trough), Color.white,
                                 new Vector2(206f, 62f), new Vector2(.5f, .5f), Vector2.zero);

            var edge = UIKit.Img("Edge", pill.transform, Art.RoundOutline(20, 2.5f),
                                 Pal.A(tint, .45f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            var glyph = UIKit.Img("Icon", pill.transform,
                                  icon == null ? null : Art.S("Ui/" + icon), Color.white,
                                  new Vector2(46f, 46f), new Vector2(0f, .5f),
                                  new Vector2(36f, 0f));
            glyph.preserveAspect = true;

            if (icon == null) Flipbook.Attach(glyph, "Ui/Coin", 11f);

            UIKit.Shrinkable(
                UIKit.Titled("V", pill.transform, value, 28, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(112f, 42f), new Vector2(.5f, .5f), new Vector2(16f, 0f),
                             3f, 3f), 18);
        }

        public override bool OnBack() { Flow.Go<LevelsScreen>(); return true; }

        // ----------------------------------------------------------------- tips
        /// <summary>Whether the incoming transition has finished. See <see cref="Teach"/>.</summary>
        bool _presented;

        /// <summary>Whether this visit has already decided about the shelf's lessons.</summary>
        bool _taught;

        /// <summary>A beat after the iris, so the screen is read before somebody explains it.</summary>
        const float TeachDelay = .45f;

        public override void OnPresented()
        {
            _presented = true;
            Teach();
        }

        /// <summary>
        /// The two things a first visit has to be told, once, and then never unasked again.
        ///
        /// <para>
        /// <b>Both are rules a player would otherwise learn by buying the wrong thing.</b> The
        /// four boxes at the top are <em>colours</em> rather than positions and a turret is bought
        /// for one colour at a time (<c>Mechanic.LoadoutSeats</c>, invariant 42c); the second tab
        /// holds the half of a loadout that runs out (<c>Mechanic.LoadoutKit</c>). Neither is
        /// visible until money has changed hands, which is the one time a lesson is worth a modal.
        /// </para>
        /// <para>
        /// Ordinary lessons on the ordinary ledger, exactly as the grove's two are: a permanent
        /// id, strings derived from it, and <c>TipLedger</c> recording that this player has met
        /// them - so they are shown once in a lifetime rather than once per install, and it cost
        /// no new field to say so. They are deliberately absent from <c>Mechanic.TeachingOrder</c>,
        /// which is a board's queue: nothing about a siege implies the player has opened the
        /// shelf.
        /// </para>
        /// <para>
        /// <b>And once is not the end of them</b> - see the info key in <see cref="BuildHeader"/>
        /// and <see cref="Review"/>.
        /// </para>
        /// </summary>
        void Teach()
        {
            if (_taught || !this || !_presented) return;

            _taught = true;

            var queue = new List<ScreenLesson>(2);

            ScreenLessons.Offer(queue, Mechanic.LoadoutSeats, _line);
            ScreenLessons.Offer(queue, Mechanic.LoadoutKit, _kitTab);

            if (queue.Count == 0) return;

            // A turret panel left up across a navigation would put a lesson behind it, and a
            // lesson nobody sees is one that can never be shown again - so `ScreenLessons.Show`
            // waits for a clear screen rather than this giving up in front of one.
            Tween.After(TeachDelay, () =>
            {
                if (!this) return;
                ScreenLessons.Show(this, queue);
            }, this);
        }

        /// <summary>
        /// Puts both lessons back up, at the player's asking.
        ///
        /// <para>
        /// <b>The same panels through the same chain</b>, which is <c>RunLessons.Review</c>'s rule
        /// and for its reason: a second way of raising a tip is a second thing that can disagree
        /// about how many are on screen at once. What differs is only the ledger - a player who
        /// pressed a button has asked, so these are queued whether or not they have been met
        /// (<c>ScreenLessons.Add</c>).
        /// </para>
        /// <para>
        /// <b>The controls are re-read rather than remembered</b>, because <see cref="Rebuild"/>
        /// destroys the tab row every time somebody swaps shelves - a queue built at
        /// <c>Build</c> time and kept would by now be pointing at an object that no longer exists,
        /// and <c>TipOverlay</c> answers that by silently drawing no ring.
        /// </para>
        /// <para>
        /// Refused while anything else is up, rather than queued behind it: a lesson is a modal
        /// about the screen underneath, and a panel over that screen is a state where the screen
        /// is owned by something else.
        /// </para>
        /// </summary>
        void Review()
        {
            if (!this || Flow.HasModal) return;

            // A tip pointing at the KIT tab while the KIT shelf is open still reads correctly -
            // the tab is where it always is, selected or not - so there is deliberately nothing
            // here that changes what the player was looking at.
            var queue = new List<ScreenLesson>(2);

            ScreenLessons.Add(queue, Mechanic.LoadoutSeats, _line);
            ScreenLessons.Add(queue, Mechanic.LoadoutKit, _kitTab);

            ScreenLessons.Show(this, queue);
        }

        // ----------------------------------------------------------------- the line
        /// <summary>
        /// The four colour slots, above the grid.
        ///
        /// <b>Always four, whatever a level stands.</b> A line is arranged against the mode rather
        /// than against a rung — a player choosing a turret has not chosen a level yet — and a
        /// siege that stands three wards simply never draws the fourth.
        /// </summary>
        void BuildLine()
        {
            _line = UIKit.Node("Line", Safe);
            _line.anchorMin = new Vector2(0f, 1f);
            _line.anchorMax = new Vector2(1f, 1f);
            _line.pivot = new Vector2(.5f, 1f);
            _line.sizeDelta = new Vector2(0f, LineHeight);
            _line.anchoredPosition = new Vector2(0f, -HeaderHeight);

            _slots.Clear();

            int count = WardLine.Colours.Length;
            float width = 168f, gap = 18f;
            float span = count * width + (count - 1) * gap;

            for (int i = 0; i < count; i++)
            {
                int colour = i;
                float x = -span * .5f + width * .5f + i * (width + gap);

                var box = UIKit.Box("Slot" + i, _line, new Vector2(width, LineHeight - 48f),
                                    new Vector2(.5f, .5f), new Vector2(x, -6f));

                var hit = box.gameObject.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);
                hit.raycastTarget = true;

                // The hub's own plate rather than a 6%-white wash inside a traced rim, which is
                // the shape this UI drew before it had a kit. Which slot you are on is the
                // sprite and the gold rim, not a tint: the mould paints its own keyline and its
                // own two-tone face, and neither survives being multiplied by a colour.
                var seat = UIKit.Img("Seat", box, Art.S("Ui/" + Skins.PlateBlue), Color.white);
                UIKit.StretchTo((RectTransform)seat.transform, 0, 0, 0, 0);

                var edge = UIKit.Img("Edge", box, Art.RoundOutline(26, 6f), Skins.PlateEdge);
                UIKit.StretchTo((RectTransform)edge.transform, -2, -2, -2, -2);

                // **Centre, not top.** `UIKit.Box` pivots at the middle whatever it is anchored
                // to, so a 112-tall picture anchored to the top edge at -16 hung 40 units of
                // itself off the top of its own box. It is placed by its centre now, which is
                // what "-70" is: 14 of margin plus half the picture.
                // The slot's own colour, kept as a light rather than as a plate tint. It is the
                // one thing about these four boxes a player cannot read off the turret standing
                // in them, and the plate can no longer carry it: the mould paints its own
                // keyline and two-tone face, and neither survives being multiplied by a colour.
                var glow = UIKit.Img("Glow", box, Art.Glow(128, 1.9f), Color.white,
                                     new Vector2(150f, 150f), new Vector2(.5f, 1f),
                                     new Vector2(0f, -74f));
                glow.raycastTarget = false;

                // **A four-by-five box now that the picture is the turret itself.** A board sprite
                // is 192x240 where the thumbnail was square, and `preserveAspect` fits *inside* a
                // box — so left at 112x112 the turret would have drawn 90 wide and read as having
                // shrunk. 112x140 keeps it the height it was, and the seat has the room: the name
                // sits 167 down and this reaches 144.
                var icon = UIKit.Img("Turret", box, null, Color.white, new Vector2(112f, 140f),
                                     new Vector2(.5f, 1f), new Vector2(0f, -74f));
                icon.preserveAspect = true;

                var name = UIKit.Label("Name", box, string.Empty, 22, Pal.A(Pal.Cream, .86f),
                                       TextAnchor.MiddleCenter, new Vector2(width - 16f, 34f),
                                       new Vector2(.5f, 0f), new Vector2(0f, 18f));
                UIKit.Shrinkable(name, 14);

                // **The padlock is drawn over the seat rather than instead of it**, so a player
                // can still see which turret is standing there while they are told they cannot
                // change it - a blank box would read as a seat with nothing in it, which is the
                // opposite of true: the line always stands four.
                var shut = UIKit.Img("Lock", box, Art.S("Ui/ic_lock"), Pal.A(Pal.Cream, .92f),
                                     new Vector2(52f, 52f), new Vector2(.5f, 1f),
                                     new Vector2(0f, -74f));
                shut.preserveAspect = true;
                shut.raycastTarget = false;
                shut.enabled = false;

                box.gameObject.AddComponent<Btn>().Setup(() => Choose(colour));

                _slots.Add(new SlotView
                {
                    Colour = colour, Seat = seat, Edge = edge, Icon = icon, Name = name,
                    Glow = glow, Lock = shut,
                });
            }
        }

        /// <summary>
        /// Taps a seat, or says why it cannot be arranged yet.
        ///
        /// <b>It answers out loud rather than doing nothing.</b> A control that is live and
        /// silently refuses is one nobody learns - and what a shut seat wants to say is a number
        /// the player can act on, which is how many rungs are left before the line grows.
        /// </summary>
        void Choose(int colour)
        {
            if (!WardSeats.IsOpen(colour, _cleared))
            {
                int want = WardSeats.Needed(colour) - _cleared;
                Scenery.Toast(Content, Loc.Format("ui.loadout.seat_shut",
                                                  want < 1 ? 1 : want), Pal.Gold, 2.4f);
                return;
            }

            if (_slot == colour && _shelf == Shelf.Wards) return;

            _slot = colour;
            _shelf = Shelf.Wards;
            Paint();
        }

        // ----------------------------------------------------------------- tabs
        void BuildTabs()
        {
            var row = _tabRow = UIKit.Node("Tabs", Safe);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(.5f, 1f);
            row.sizeDelta = new Vector2(0f, TabsHeight);
            row.anchoredPosition = new Vector2(0f, -HeaderHeight - LineHeight);

            Tab(row, "TabWards", "ui.loadout.wards", Shelf.Wards, -150f);
            Tab(row, "TabItems", "ui.loadout.items", Shelf.Items, 150f);
        }

        void Tab(RectTransform parent, string id, string key, Shelf shelf, float x)
        {
            var box = UIKit.Box(id, parent, new Vector2(280f, 72f), new Vector2(.5f, .5f),
                                new Vector2(x, -8f));

            if (shelf == Shelf.Items) _kitTab = box;

            var hit = box.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            bool on = _shelf == shelf;

            var seat = UIKit.Img("Seat", box, Art.S("Ui/" + (on ? Skins.PlateOrange : Skins.PlateBlue)),
                                 Color.white);
            UIKit.StretchTo((RectTransform)seat.transform, 0, 0, 0, 0);

            if (on)
            {
                var rim = UIKit.Img("Rim", box, Art.RoundOutline(22, 5f), Skins.PlateEdge);
                UIKit.StretchTo((RectTransform)rim.transform, -2, -2, -2, -2);
            }

            var label = UIKit.Titled("T", box, Loc.Get(key).ToUpperInvariant(), 30,
                                     on ? Pal.Sun : Pal.Cream,
                                     TextAnchor.MiddleCenter, new Vector2(250f, 48f),
                                     new Vector2(.5f, .5f), Vector2.zero, 0f, 2f);
            UIKit.Shrinkable(label, 18);

            box.gameObject.AddComponent<Btn>().Setup(() =>
            {
                if (_shelf == shelf) return;
                _shelf = shelf;
                Rebuild();
            });
        }

        // ----------------------------------------------------------------- grid
        void BuildGrid()
        {
            _viewport = UIKit.Node("Viewport", Safe);
            _viewport.offsetMin = new Vector2(0f, 40f);
            _viewport.offsetMax = new Vector2(0f, -(HeaderHeight + LineHeight + TabsHeight));

            var catcher = _viewport.gameObject.AddComponent<Image>();
            catcher.color = new Color(0, 0, 0, 0);
            catcher.raycastTarget = true;
            _viewport.gameObject.AddComponent<RectMask2D>();

            _grid = UIKit.Node("Grid", _viewport);
            _grid.anchorMin = new Vector2(0f, 1f);
            _grid.anchorMax = new Vector2(1f, 1f);
            _grid.pivot = new Vector2(.5f, 1f);
            _grid.anchoredPosition = Vector2.zero;

            var scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = _grid;
            scroll.viewport = _viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = .14f;
            scroll.inertia = true;
            scroll.decelerationRate = .04f;
            scroll.scrollSensitivity = 55f;
        }

        /// <summary>
        /// Rebuilds the whole screen below the header.
        ///
        /// The answer to changing <em>which shelf</em> is showing, and to nothing else:
        /// <see cref="Paint"/> takes every other change, for <c>CompanionScreen</c>'s reason — a
        /// rebuild replays the entrance, so a small confirmation would be answered with the
        /// animation that says "you have just arrived".
        /// </summary>
        /// <summary>
        /// Swaps the shelf, which is the grid and the tab row and nothing else.
        ///
        /// <para>
        /// <b>It used to throw away everything under <c>Safe</c> and build the screen again</b>,
        /// which meant the ribbon, the back key and the two balance pills were destroyed and
        /// re-created on every tap between TURRETS and KIT — so the heading popped in each time,
        /// reported as exactly that. None of them depends on which shelf is showing. It is the
        /// same fault five screens in this project have had one at a time and the same fix:
        /// rebuild the part that differs, repaint the rest (<c>GridView</c>'s Show/Refresh rule,
        /// asked of a screen).
        /// </para>
        /// <para>
        /// Hidden before being destroyed, and the sibling order restored after. <c>Destroy</c>
        /// lands at the end of the frame, so without the first the outgoing grid is drawn over
        /// its replacement for a frame; and a node built now is appended last, so without the
        /// second the new grid would sit above the header in paint order.
        /// </para>
        /// </summary>
        /// <summary>
        /// How many cells across the grid was last painted at. See <see cref="Update"/>.
        /// </summary>
        int _painted;

        /// <summary>
        /// Repaints the shelf when the display changes shape, which is <c>CanvasFitter</c>'s
        /// argument one layer up: a column count read once is right on every device that never
        /// changes shape and silently wrong on the ones that do — an iPad entering split view, a
        /// foldable being opened, and Android reporting a different size for a frame or two
        /// after a rotation.
        ///
        /// <para>
        /// <b>Watched rather than subscribed to</b>, because nothing raises an event for it, and
        /// it is a property read and an int compare a frame. The entrance is not replayed: a
        /// resize is a redraw, not an arrival (invariant 16d).
        /// </para>
        /// </summary>
        void Update()
        {
            if (_grid == null || _painted == Columns) return;
            Paint();
        }

        void Rebuild()
        {
            Drop(_viewport);
            Drop(_tabRow);

            _shown = false;

            BuildGrid();
            BuildTabs();

            _viewport.SetSiblingIndex(0);
            _tabRow.SetSiblingIndex(1);

            Paint();
        }

        static void Drop(RectTransform rt)
        {
            if (!rt) return;
            rt.gameObject.SetActive(false);
            Destroy(rt.gameObject);
        }
    }
}
