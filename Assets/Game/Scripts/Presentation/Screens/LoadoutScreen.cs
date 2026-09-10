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
    /// the cheap one. It is still a scope (<c>AssetLibrary.WardShelfScope</c>) and still leaves
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
        /// How many cells across, and it is <b>four because the roster is twenty</b>.
        ///
        /// <para>
        /// <b>A grid whose count does not divide by its columns has a hole in the corner the eye
        /// reads as the end.</b> At three across, twenty turrets are six full rows and a row of
        /// two - so the bottom-right cell, which is where anybody looks for "the last one", was
        /// <em>empty</em>, and the last thing actually drawn there was whatever sat at the end of
        /// the row above. It was reported as the shelf being in the wrong order, and the order was
        /// right: apex was rung twenty the whole time, sitting in the middle of a ragged row.
        /// Four columns is five full rows and nothing left over.
        /// </para>
        /// <para>
        /// <b>Whoever changes the roster's size owns this number.</b> Twenty into four is exact;
        /// twenty-one into four is a ragged row again, and the fix is the columns or the roster,
        /// never a tidier ending drawn on top of it.
        /// </para>
        /// </summary>
        const int Columns = 4;

        /// <summary>
        /// One cell, and the vertical structure inside it - shared by both shelves, because the
        /// two grids differing anywhere is a difference nobody chose.
        ///
        /// <para>
        /// <b>Narrower because there are four of them</b>: the row is the same 1,004 units it
        /// always was, so the cell is what gives. Everything below the picture moved up by exactly
        /// what the picture lost, which is why <see cref="CellH"/> fell by the same 46.
        /// </para>
        /// </summary>
        const float CellW = 236f, CellH = 298f;
        const int CellRadius = 24;
        const float CellGapX = 20f, CellGapY = 22f;

        /// <summary>The picture: square, centred, a margin down from the cell's own top.</summary>
        const float IconTop = 18f, IconBox = CellW * .55f;

        /// <summary>The name's middle and the note's middle, measured down from the cell's top.</summary>
        const float NameY = 169f, NoteY = 215f;

        /// <summary>The price strip's middle, measured <em>up</em> from the cell's foot.</summary>
        const float FootY = 28f;

        /// <summary>
        /// A turret's cell, which is shorter than a utility's and says less.
        ///
        /// <b>It carries a name and a price and nothing else.</b> The one-line description of what
        /// an ability does went with the height: twenty of them stacked four across is a wall of
        /// small print on a screen whose job is a choice between pictures, and the sentence is
        /// still there one tap in, on the panel where somebody is actually deciding. What the
        /// space bought is a price big enough to read at a glance, which is the number the shelf
        /// is really about.
        /// </summary>
        const float WardCellH = 262f, WardNameY = 172f, WardFootY = 34f;

        /// <summary>The parts of a slot that change when the line does.</summary>
        sealed class SlotView
        {
            public int Colour;
            public Image Seat, Edge, Icon, Glow;
            public Text Name;
        }

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
        async void Browse()
        {
            try
            {
                await AssetLibrary.EnsureScopeAsync(
                    AssetLibrary.WardShelfScope,
                    AssetManifest.WardShelfAssets(WardLedger.Catalog.Models));
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return;
            }

            if (this != null) Dress();
        }

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
            // Released on the way out, which is the whole reason it is a scope: twenty pictures
            // resident for the life of a session to draw one screen is memory bounded by how much
            // content exists rather than by what is on the screen (invariant 7b).
            AssetLibrary.ReleaseScope(AssetLibrary.WardShelfScope);

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

                box.gameObject.AddComponent<Btn>().Setup(() => Choose(colour));

                _slots.Add(new SlotView
                {
                    Colour = colour, Seat = seat, Edge = edge, Icon = icon, Name = name, Glow = glow,
                });
            }
        }

        void Choose(int colour)
        {
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
