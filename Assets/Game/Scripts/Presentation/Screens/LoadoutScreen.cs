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
    /// <b>Browsing never loads the real turrets.</b> The grid draws one uncoloured thumbnail per
    /// model out of the shared UI set; the four a run actually stands come out of
    /// <c>AssetLibrary.LineScope</c> when a board is built. That is invariant 16c's rule — a shelf
    /// costs the shelf rather than the catalog — and it is what keeps twenty models times four
    /// colours off a phone's memory.
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

        RectTransform _viewport, _grid, _line;
        Text _summary;

        readonly List<SlotView> _slots = new List<SlotView>();

        const float HeaderHeight = 232f;
        const float LineHeight = 250f;
        const float TabsHeight = 104f;
        const int Columns = 3;
        const float CellW = 320f, CellH = 344f;
        const int CellRadius = 28;

        /// <summary>The parts of a slot that change when the line does.</summary>
        sealed class SlotView
        {
            public int Colour;
            public Image Seat, Edge, Icon;
            public Text Name;
        }

        protected override void Build()
        {
            Scenery.Layered(Content, "home", .26f);

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

            if (this != null) Paint();
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

            UIKit.Titled("Title", Safe, Loc.Get("ui.loadout.title").ToUpperInvariant(), 44,
                         Pal.Cream, TextAnchor.MiddleCenter, new Vector2(560f, 70f),
                         new Vector2(.5f, 1f), new Vector2(0f, -102f), 0f, 3f);

            _summary = UIKit.Label("Summary", Safe, string.Empty, 26, Pal.A(Pal.Cream, .64f),
                                   TextAnchor.MiddleCenter, new Vector2(640f, 40f),
                                   new Vector2(.5f, 1f), new Vector2(0f, -156f));

            // The two balances a shelf here spends. Built out of the same pill the shop draws,
            // so a price on a cell and the purse above it read as the same currency.
            var row = UIKit.Row("Balances", Safe, new Vector2(640f, 68f), new Vector2(.5f, 1f),
                                new Vector2(0f, -196f), 14f);

            Balance(row, Pal.Gold, null, Compact.Number(Profile.Coins));
            Balance(row, Pal.Bloom, "ic_gem", Compact.Number(Profile.Gems));
        }

        /// <summary>One balance pill. The shop's, without the flare registry a shelf needs.</summary>
        static void Balance(Transform row, Color tint, string icon, string value)
        {
            var pill = UIKit.Img("Pill", row, Art.Round(20), new Color(.04f, .09f, .12f, .82f),
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

                var seat = UIKit.Img("Seat", box, Art.Round(24), new Color(1f, 1f, 1f, .06f));
                UIKit.StretchTo((RectTransform)seat.transform, 0, 0, 0, 0);

                var edge = UIKit.Img("Edge", box, Art.RoundOutline(24, 4f),
                                     new Color(1f, 1f, 1f, .12f));
                UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

                var icon = UIKit.Img("Turret", box, null, Color.white, new Vector2(112f, 112f),
                                     new Vector2(.5f, 1f), new Vector2(0f, -16f));
                icon.preserveAspect = true;

                var name = UIKit.Label("Name", box, string.Empty, 22, Pal.A(Pal.Cream, .86f),
                                       TextAnchor.MiddleCenter, new Vector2(width - 16f, 34f),
                                       new Vector2(.5f, 0f), new Vector2(0f, 18f));
                UIKit.Shrinkable(name, 14);

                box.gameObject.AddComponent<Btn>().Setup(() => Choose(colour));

                _slots.Add(new SlotView
                {
                    Colour = colour, Seat = seat, Edge = edge, Icon = icon, Name = name,
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
            var row = UIKit.Node("Tabs", Safe);
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

            var seat = UIKit.Img("Seat", box, Art.Round(20),
                                 on ? Pal.A(Pal.Sun, .22f) : new Color(1f, 1f, 1f, .05f));
            UIKit.StretchTo((RectTransform)seat.transform, 0, 0, 0, 0);

            if (on)
            {
                var rim = UIKit.Img("Rim", box, Art.RoundOutline(20, 3f), Pal.A(Pal.Sun, .72f));
                UIKit.StretchTo((RectTransform)rim.transform, 0, 0, 0, 0);
            }

            var label = UIKit.Titled("T", box, Loc.Get(key).ToUpperInvariant(), 30,
                                     on ? Pal.Cream : Pal.A(Pal.Cream, .70f),
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
        void Rebuild()
        {
            for (int i = Safe.childCount - 1; i >= 0; i--)
            {
                var child = Safe.GetChild(i).gameObject;
                child.transform.SetParent(null, false);
                Destroy(child);
            }

            _slots.Clear();

            BuildGrid();
            BuildLine();
            BuildTabs();
            BuildHeader();
            Paint();
        }
    }
}
