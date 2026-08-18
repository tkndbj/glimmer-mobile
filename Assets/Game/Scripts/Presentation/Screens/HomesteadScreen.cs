using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The Grovement: the islands the player owns, and everything standing on them.
    ///
    /// <para>
    /// <b>What this screen is for.</b> Every other reward in the game is a number that goes
    /// up — stars, credits, keeper level, a standing on a node. This is the one that is a
    /// <em>thing the player made</em>, and it is the only place in the game where two
    /// accounts at the same progress look different. That is the whole design: land is
    /// earned by finishing chapters, residents are earned by waking them in a glade, decor
    /// is bought, and the arrangement is nobody's but theirs.
    /// </para>
    /// <para>
    /// <b>Nothing here touches a board.</b> No bonus, no buff, no faster hearts. That is a
    /// rule rather than a phase: par is derived from the board, stars from par, the clock
    /// from par and the server's earnings from all three, so a grove that granted anything
    /// would make every glade a different difficulty per player and no validator could prove
    /// one fair again. A place is worth more than a stat screen anyway.
    /// </para>
    /// <para>
    /// <b>There is no edit mode.</b> A slot is tapped and a picker opens — always, whether it
    /// is empty or full. A mode toggle would be a control that changes what every other
    /// control does, on a screen whose entire vocabulary is "tap the thing you want to
    /// change". Empty slots wear a soft ring so the interaction teaches itself; that is the
    /// price of having no mode and it is worth paying.
    /// </para>
    /// <para>
    /// The whole screen is built from the catalog. A drop that ships a plot, ten decor pieces
    /// and a resident changes <c>homestead.json</c> and nothing here.
    /// </para>
    /// </summary>
    public sealed class HomesteadScreen : View, IDrawsGroveArt
    {
        public override string Track => "mus_menu";

        // ------------------------------------------------------------- geometry
        const float HeaderHeight = 262f;

        /// <summary>Size of the ring marking a slot with nothing in it.</summary>
        const float EmptyMark = 74f;

        RectTransform _viewport, _canvas;
        Text _summary;
        ScrollRect _scroll;

        /// <summary>
        /// Where every island sits, derived from the real height of its art.
        ///
        /// This used to be a fixed 3400px canvas with a <c>y</c> fraction authored per plot,
        /// and it was wrong in the only way that mattered: the ten shipped islands total
        /// 4,632px of art before a single gap, so every consecutive pair overlapped and the
        /// one plot a new player owns sat below the scrollable area and could not be reached.
        /// See <see cref="HomesteadMap"/> — the canvas height is now a consequence of the
        /// content rather than a number somebody has to keep in step with it.
        /// </summary>
        HomesteadLayoutMap _map = new HomesteadLayoutMap(new PlotPlacement[0], 0f);

        /// <summary>
        /// The canvas's top edge, which is where the map's own coordinates are measured from.
        /// Every island anchors here — see <see cref="DrawPlot"/> for what anchoring them to
        /// the centre instead cost.
        /// </summary>
        static readonly Vector2 TopAnchor = new Vector2(.5f, 1f);

        /// <summary>
        /// Whether the scroll has been parked against <em>final</em> geometry. See
        /// <see cref="Paint"/>: it stays false while any island's art is still arriving,
        /// because the height it would be parked against is a guess until then.
        /// </summary>
        bool _parked;

        protected override void Build()
        {
            // The hub's own sky and nothing else from it. The islands here are the content, so
            // laying the hub's ground and decoration behind them would be two groves in one
            // picture — and this one is supposed to be the player's.
            Scenery.Cover(Content, "home_sky", .05f, .42f);
            Fireflies.Spawn(Content, 16, new Color(1f, .93f, .70f), 6f, 20f);

            BuildCanvas();
            BuildHeader();
            NavBar.Build(Content, NavBar.Tab.Grove);

            // The catalog is a body, read on entering the feature. Both this and the art load
            // asynchronously and both repaint, because a screen is built in the frame it is
            // asked for and the first paint would otherwise be the only one.
            Warm();
            HomesteadArt.OpenAsync(() => { if (this) Paint(); });

            HomesteadCatalog.Changed += Paint;
            HomesteadLedger.Changed += Paint;
            HomesteadLayout.Changed += Paint;

            // Land and residents are derived from the star ledger, so a run finished in this
            // session can open a plot while the player is standing on it.
            PlayerProgress.Reloaded += Paint;
            PlayerProgress.RecordChanged += OnRecord;
        }

        void OnDestroy()
        {
            HomesteadCatalog.Changed -= Paint;
            HomesteadLedger.Changed -= Paint;
            HomesteadLayout.Changed -= Paint;
            PlayerProgress.Reloaded -= Paint;
            PlayerProgress.RecordChanged -= OnRecord;

            // The picker is a modal over this screen, so it is gone with it. Dropping the art
            // here is the whole bargain the scope makes — see HomesteadArt.
            //
            // Unless the shop is what replaced this screen, which is not a special case so
            // much as the general one: Destroy lands at the end of the frame, so the incoming
            // screen has already built *and painted* by the time this runs. Releasing here
            // pulled every decor sprite out from under a shop that had already drawn it, and
            // nothing repaints — which is why the grid was a wall of empty plates until the
            // player left and came back. HomesteadArt owns the rule so a third screen drawing
            // grove art cannot forget half of it.
            HomesteadArt.CloseUnlessWanted();
        }

        void OnRecord(LevelRecord record) => Paint();

        async void Warm()
        {
            await HomesteadService.EnsureAsync();
            if (!this) return;

            // The art set is derived from the catalog, so it can only be asked for once the
            // catalog is in hand. Asking twice is free — the scope reports itself loaded.
            HomesteadArt.OpenAsync(() => { if (this) Paint(); });
            Paint();
        }

        // ---------------------------------------------------------------- canvas
        void BuildCanvas()
        {
            _viewport = UIKit.Node("Viewport", Content);
            _viewport.offsetMin = new Vector2(0f, NavBar.Height);
            _viewport.offsetMax = new Vector2(0f, -HeaderHeight);

            var catcher = _viewport.gameObject.AddComponent<Image>();
            catcher.color = new Color(0, 0, 0, 0);
            catcher.raycastTarget = true;
            _viewport.gameObject.AddComponent<RectMask2D>();

            _canvas = UIKit.Node("Grove", _viewport);
            _canvas.anchorMin = new Vector2(0f, 1f);
            _canvas.anchorMax = new Vector2(1f, 1f);
            _canvas.pivot = new Vector2(.5f, 1f);
            _canvas.anchoredPosition = Vector2.zero;

            _scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _canvas;
            _scroll.viewport = _viewport;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.elasticity = .14f;
            _scroll.inertia = true;
            _scroll.decelerationRate = .04f;
            _scroll.scrollSensitivity = 55f;
        }

        // ---------------------------------------------------------------- header
        void BuildHeader()
        {
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .06f, .09f, .82f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            frt.sizeDelta = new Vector2(0f, 316f);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);

            var banner = UIKit.Img("Banner", Content, Art.S("Ui/banner"), Color.white,
                                   new Vector2(560f, 148f), new Vector2(.5f, 1f), new Vector2(0f, -128f));
            UIKit.Shrinkable(
                UIKit.Titled("Title", banner.transform, Loc.Get("ui.grove.title").ToUpperInvariant(), 40,
                             new Color(.36f, .24f, .16f), TextAnchor.MiddleCenter,
                             new Vector2(392f, 60f), new Vector2(.5f, .5f),
                             new Vector2(0f, 148f * UIKit.PillFaceLift), 0f, 2f), 24);
            banner.transform.localScale = Vector3.zero;
            Tween.Pop(banner.transform, 0f, .6f, .1f);

            _summary = UIKit.Shrinkable(
                UIKit.Titled("Summary", Content, string.Empty, 26,
                             new Color(1f, .96f, .88f, .72f), TextAnchor.MiddleCenter,
                             new Vector2(720f, 34f), new Vector2(.5f, 1f), new Vector2(0f, -212f), 3f, 0f), 18);

            // The shop is a screen of its own rather than a panel over this one, for
            // CompanionScreen's reason: what it lists is unbounded, and a grid that scrolls
            // inside a scrim is a worse place to browse than a page that owns the display.
            // Placed through UIKit.Corner because Box pivots at centre: passing the margin
            // straight in put half the button past the right edge of the screen, which is
            // exactly how it shipped.
            var shopSize = new Vector2(230f, 96f);
            var shopAnchor = new Vector2(1f, 1f);
            var shop = UIKit.TextButton("Shop", Content, "btn_orange", Loc.Get("ui.grove.shop"), 28,
                                        shopSize, shopAnchor,
                                        UIKit.Corner(shopSize, shopAnchor, 28f, 74f),
                                        () => Flow.Go<HomesteadShopScreen>());
            UIKit.Shrinkable(shop.Label, 18);
            UIKit.FitLabel(shop);
        }

        // ----------------------------------------------------------------- paint
        /// <summary>
        /// Rebuilds the grove from the catalog, the ledger and the layout.
        ///
        /// <para>
        /// A full rebuild rather than a patch, and that is a considered choice rather than the
        /// lazy one. <c>CompanionScreen</c> went the other way and was right to: it repaints
        /// on a change to <em>which of thirty cells wears a ring</em>, where a rebuild would
        /// replay the entrance animation and cost the roster's size on every tap. Here the
        /// events are a piece placed, a piece bought and a plot opened — each of which changes
        /// what a slot <em>is</em>, several slots at once for a plot, and there are around
        /// sixty of them with no per-object animation to interrupt. One description of the
        /// screen is worth more than a second one that has to be kept in step.
        /// </para>
        /// </summary>
        void Paint()
        {
            if (_canvas == null) return;

            for (int i = _canvas.childCount - 1; i >= 0; i--)
            {
                var old = _canvas.GetChild(i).gameObject;
                old.SetActive(false);              // Destroy only lands at end of frame
                Destroy(old);
            }

            var catalog = HomesteadCatalog.Current;
            float width = Flow.Size.x;

            // Derived from the art, every paint. Doing it here rather than once means a
            // sprite arriving late — the scope loads asynchronously — re-measures the grove
            // instead of leaving it laid out against a square guess.
            _map = HomesteadMap.Build(catalog, width, AspectOf);

            _canvas.sizeDelta = new Vector2(0f, _map.CanvasHeight);

            foreach (var placement in _map.Placements) DrawPlot(placement, width);

            if (_summary) _summary.text = SummaryText(catalog);

            // Parked on the bottom island once, on the first paint that has something to show.
            // It is the only one a new player owns, so opening on the empty sky above it is
            // the difference between a grove and a bug.
            //
            // Positioned directly rather than through verticalNormalizedPosition, which is
            // the obvious call and does not work: a ScrollRect resolves a normalised value
            // against content bounds it recomputes in its own LateUpdate, so setting it in the
            // frame the canvas was resized is interpreted against the *old* height and
            // silently lands somewhere else. It reported 0.000 — the bottom — while the
            // starter island sat 2,785px below the screen. The arithmetic here is the same
            // arithmetic that just sized the canvas, so there is nothing to be out of step
            // with.
            // Latched only once every island has been measured, which is the second half of
            // the same bug. An island whose sprite has not arrived is laid out as a square
            // (see AspectOf), so the canvas height on the paint before the art lands is a
            // guess — parking against it and latching leaves the grove resting somewhere in
            // the middle of itself for the whole visit. Re-parking is free while the guess
            // stands and stops the moment it does not, so nothing fights the player's own
            // scrolling: by the time they can drag, the art is in.
            if (catalog.PlotCount > 0 && !_parked)
            {
                _parked = Measured(catalog);
                _canvas.anchoredPosition =
                    new Vector2(0f, Mathf.Max(0f, _map.CanvasHeight - _viewport.rect.height));
            }
        }

        /// <summary>
        /// Whether every island's art is in hand, so the grove's height is a measurement
        /// rather than the square guess <see cref="AspectOf"/> falls back to.
        /// </summary>
        static bool Measured(HomesteadCatalog catalog)
        {
            if (catalog == null || catalog.PlotCount == 0) return false;

            foreach (var plot in catalog.Plots)
                if (HomesteadArt.Plot(plot) == null) return false;

            return true;
        }

        /// <summary>
        /// The shape of a plot's art: sprite height over width.
        ///
        /// One before the scope has loaded, which lays a not-yet-arrived island out as a
        /// square rather than collapsing the grove on top of itself. The repaint that follows
        /// the load measures it properly.
        /// </summary>
        static float AspectOf(HomesteadPlot plot)
        {
            var sprite = HomesteadArt.Plot(plot);
            return sprite == null ? 1f : sprite.rect.height / sprite.rect.width;
        }

        string SummaryText(HomesteadCatalog catalog)
        {
            if (!HomesteadCatalog.IsLoaded) return Loc.Get("ui.grove.loading");
            if (catalog.PlotCount == 0) return Loc.Get("ui.grove.unavailable");

            // Variety rather than a slot count, because a grove of forty benches is one idea
            // and what a player is actually building is a mix.
            return Loc.Format("ui.grove.summary",
                              HomesteadLayout.OccupiedCount(catalog),
                              HomesteadLedger.HeldPlotCount(catalog),
                              catalog.PlotCount,
                              HomesteadLayout.VarietyCount(catalog));
        }

        // ------------------------------------------------------------------ plot
        void DrawPlot(PlotPlacement placement, float canvasWidth)
        {
            var plot = placement.Plot;
            var sprite = HomesteadArt.Plot(plot);

            float w = placement.Width;
            float h = placement.Height;

            // The map measures from the canvas's top-left, so an island is anchored to the
            // canvas's *top* edge and hung downward by its centre's depth. Anchoring it to the
            // centre and passing the same number — which is how it shipped — puts every island
            // half a canvas too low: the grove was 6,261px tall, so the whole content sat
            // 3,130px below where the ScrollRect believed it was. The bottom four islands were
            // then outside the scrollable range in a way no amount of dragging could reach,
            // because a ScrollRect bounds itself by the content *rect*, never by where the
            // children happen to have been drawn. What a player saw was the middle of the
            // ladder — every island locked, and the one they owned unreachable below the
            // last inch of scroll.
            var at = new Vector2(placement.CentreX - canvasWidth * .5f, -placement.CentreY);

            bool held = HomesteadLedger.IsPlotHeld(plot);
            float fill = held ? HomesteadLayout.FillOf(plot) : 0f;
            var stage = GroveTending.Of(fill);

            var island = UIKit.Img("P_" + plot.Id, _canvas, sprite,
                                   // Hidden rather than white while the scope loads. An Image
                                   // with no sprite is a solid rectangle, and this is six of
                                   // them across the screen — invariant 7b.
                                   sprite == null ? new Color(1f, 1f, 1f, 0f)
                                                  : held ? TintFor(fill)
                                                         : new Color(.46f, .54f, .60f, .92f),
                                   new Vector2(w, h), TopAnchor, at);
            island.raycastTarget = false;

            var host = (RectTransform)island.transform;

            // A soft seat under each island, which is what stops one reading as a sticker on
            // the sky. Behind the art, so it never darkens the grass.
            var seat = UIKit.Img("Seat", host, Art.Glow(128, 1.9f), new Color(.02f, .07f, .10f, .30f),
                                 new Vector2(w * .92f, h * .42f), new Vector2(.5f, 0f), new Vector2(0f, h * .10f));
            seat.raycastTarget = false;
            seat.transform.SetAsFirstSibling();

            if (!held) { DrawLock(plot, host, w, h); return; }

            // A finished island is lit from within and has fireflies over it. Spent only at
            // the top stage, because motion is the loudest thing on a screen of still islands
            // — an effect every plot wears is an effect that singles out none, which is the
            // lesson the map's rank marks already learned.
            if (stage == TendedStage.Bloomed)
            {
                var lit = UIKit.Img("Lit", host, Art.Glow(128, 1.7f), new Color(1f, .86f, .52f, .22f),
                                    new Vector2(w * 1.15f, h * .85f), new Vector2(.5f, .5f),
                                    new Vector2(0f, h * .16f));
                lit.raycastTarget = false;
                lit.transform.SetAsFirstSibling();
                Tween.Breathe(lit.transform, .05f, 4.2f, plot.X * 3f);

                Fireflies.Spawn(host, 7, new Color(1f, .93f, .68f), 5f, 13f);
            }

            // Native pixels of the plot art to drawn pixels. Everything a slot holds is sized
            // in this space, so the catalog's numbers mean the same thing on every screen.
            float plotScale = sprite != null ? w / sprite.rect.width : 1f;

            // Back to front, so a piece nearer the viewer draws over one behind it. Depth is a
            // fact about where the slot is, which is why nothing here needs a sort key in the
            // content and why an author cannot get it wrong.
            var order = new List<HomesteadSlot>(plot.Slots);
            order.Sort((a, b) => b.Y.CompareTo(a.Y));

            foreach (var slot in order)
            {
                if (slot.IsHearth) DrawHearth(slot, host, w, h, plotScale);
                else DrawSlot(slot, host, w, h, plotScale);
            }

            DrawPlate(plot, host, w, h, stage);
        }

        /// <summary>
        /// How lit an island is, from the fill of its slots.
        ///
        /// <para>
        /// Cool and slightly grey when nothing has been placed, full colour once it is going.
        /// A tint only ever multiplies, so this darkens a bare island rather than brightening a
        /// full one — which is the right way round: the bare state is the one that should look
        /// like it is waiting for something.
        /// </para>
        /// <para>
        /// It reaches white well before the island is full, at about seven tenths. The last few
        /// slots are the expensive ones, and a player should not have to buy a bridge before
        /// their island stops looking overcast.
        /// </para>
        /// </summary>
        static Color TintFor(float fill)
            => Color.Lerp(new Color(.80f, .87f, .90f), Color.white, Mathf.Clamp01(fill * 1.4f));

        /// <summary>
        /// The island's name and how far along it is, on the rock below the grass.
        ///
        /// <para>
        /// <b>A name is what makes somewhere a place.</b> Ten unnamed rocks are scenery; The
        /// Meadow, three of ten, is somewhere the player is working on. The count is the same
        /// number the tinting is derived from, said out loud — because a signal a player can
        /// see but not read is a signal they cannot aim at.
        /// </para>
        /// </summary>
        void DrawPlate(HomesteadPlot plot, RectTransform host, float w, float h, TendedStage stage)
        {
            bool done = stage == TendedStage.Bloomed;

            string line = done
                ? Loc.Get(plot.NameKey)
                : Loc.Format("ui.grove.plate", Loc.Get(plot.NameKey),
                             HomesteadLayout.OccupiedCount(plot), plot.PlaceableCount);

            var label = UIKit.Shrinkable(
                UIKit.Titled("Plate", host, line, 27,
                             done ? Pal.A(Pal.Gold, .96f) : new Color(1f, .97f, .90f, .80f),
                             TextAnchor.MiddleCenter, new Vector2(w * .8f, 40f), new Vector2(.5f, .5f),
                             new Vector2(0f, -h * .26f), 3f, 3f), 17);

            label.raycastTarget = false;
        }

        void DrawLock(HomesteadPlot plot, RectTransform host, float w, float h)
        {
            var padlock = UIKit.Img("Lock", host, Art.S("Ui/padlock"), new Color(1f, .97f, .90f, .92f),
                                    new Vector2(84f, 84f), new Vector2(.5f, .5f), new Vector2(0f, h * .12f));
            padlock.preserveAspect = true;
            padlock.raycastTarget = false;

            // Named by its chapter rather than by a level count, because that is the condition
            // and a player can check it on the map. A chapter the catalog does not carry yet is
            // one that has not shipped — the plot is authored ahead of it on purpose, so the
            // ladder is visible before it is walkable, and the copy says so.
            var chapter = GameContent.FindChapter(plot.RequiresChapter);

            string line = chapter != null
                ? Loc.Format("ui.grove.locked_chapter", Loc.Get(chapter.NameKey))
                : Loc.Get("ui.grove.locked_soon");

            UIKit.Shrinkable(
                UIKit.Titled("Need", host, line, 26, new Color(1f, .96f, .88f, .86f),
                             TextAnchor.MiddleCenter, new Vector2(w * .86f, 60f), new Vector2(.5f, .5f),
                             new Vector2(0f, -h * .04f), 3f, 3f), 18);
        }

        // ---------------------------------------------------------------- hearth
        /// <summary>
        /// The home, drawn from what the player owns rather than from what they placed.
        ///
        /// <para>
        /// <b>This is the one slot with nothing in the save file.</b> A dwelling is an
        /// entitlement — see <c>HomesteadPieceKind.Dwelling</c> — so the hearth shows the best
        /// tier held and upgrading is instantaneous and impossible to get wrong. Buying is the
        /// moment the home changes, which is the whole point of it: a purchase that then has to
        /// be found in a picker and put down is a purchase a player can make and not see, and
        /// this feature has already made that mistake once.
        /// </para>
        /// <para>
        /// Tapping it opens the home panel rather than a picker, because the question a player
        /// has at their own front door is "what would a better one look like", not "what else
        /// could stand here".
        /// </para>
        /// </summary>
        void DrawHearth(HomesteadSlot slot, RectTransform host, float w, float h, float plotScale)
        {
            var home = HomesteadLedger.BestDwelling(HomesteadCatalog.Current);

            var at = new Vector2((slot.X - .5f) * w, (slot.Y - .5f) * h);
            float touch = Mathf.Max(EmptyMark * 1.4f, 150f * plotScale);

            var cell = UIKit.Button("H_" + slot.Id, host, Art.Pixel, Vector2.one * touch,
                                    new Vector2(.5f, .5f), at, OpenHome);
            cell.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

            if (!home.IsValid)
            {
                // No home in the catalog at all, which is a content fault rather than a state a
                // player can reach — every grove starts with the first rung. Drawn as a ring so
                // it is visible in the Editor instead of being an invisible hole.
                var ring = UIKit.Img("Empty", cell.transform, Art.Ring(96, 7f),
                                     new Color(1f, .98f, .90f, .26f),
                                     Vector2.one * EmptyMark, new Vector2(.5f, .5f), Vector2.zero);
                ring.raycastTarget = false;
                return;
            }

            var size = HomesteadArt.SizeOf(home, plotScale, slot.Scale);

            var art = UIKit.Img("A", cell.transform, null, Color.white, size,
                                new Vector2(.5f, .5f), new Vector2(0f, size.y * home.Lift));
            art.preserveAspect = true;
            art.raycastTarget = false;
            HomesteadArt.Paint(art, home);

            DrawHomeLife(home, (RectTransform)art.transform, size);
        }

        /// <summary>
        /// What tells the five homes apart: smoke, a lit window, lanterns, a gilded roof.
        ///
        /// <para>
        /// <b>The tier drives this, not the sprite</b>, and that is deliberate rather than a
        /// placeholder trick that will be thrown away. Every rung currently draws the same
        /// cottage, so the ladder has to read as a ladder from something else — and even once
        /// real buildings land, smoke from a chimney is the strongest "somebody lives here"
        /// signal available for the number of bytes, and it cannot be painted into a sprite
        /// because it has to move.
        /// </para>
        /// <para>
        /// All of it is generated (<c>Art.Glow</c>, <c>Art.Disc</c>) for <c>Art.Bloom</c>'s
        /// reason: this is the centrepiece of the screen, and an <c>Image</c> whose sprite has
        /// not arrived is a white rectangle. The one thing on the grove that must never look
        /// broken is the player's own house.
        /// </para>
        /// </summary>
        void DrawHomeLife(HomesteadPiece home, RectTransform art, Vector2 size)
        {
            // A warm window. From the second rung, because the first is the empty cabin
            // somebody has just been given rather than one they have made anything of.
            if (home.Tier >= 2)
            {
                var glow = UIKit.Img("Warm", art, Art.Glow(128, 2.0f), new Color(1f, .82f, .42f, .40f),
                                     size * .48f, new Vector2(.5f, .5f), new Vector2(0f, -size.y * .06f));
                glow.raycastTarget = false;
                glow.transform.SetAsFirstSibling();
                Tween.Breathe(glow.transform, .06f, 3.4f, home.Tier * .7f);
            }

            // Chimney smoke: three puffs on one endless clock, each drifting up and fading out
            // a third of a cycle apart. One tween for the lot rather than three, because three
            // would drift out of phase with each other over a long session — the same reason
            // TweenCycle exists.
            if (home.Tier >= 2)
            {
                var puffs = new Image[3];
                for (int i = 0; i < puffs.Length; i++)
                {
                    puffs[i] = UIKit.Img("Puff" + i, art, Art.Disc(64), new Color(1f, 1f, 1f, 0f),
                                         Vector2.one * (size.x * .10f), new Vector2(.5f, 1f),
                                         new Vector2(size.x * .18f, 0f));
                    puffs[i].raycastTarget = false;
                }

                float rise = size.y * .55f;
                float t0 = Time.unscaledTime;

                Tween.Run(3600f, Ease.Linear, _ =>
                {
                    if (!art) return;

                    for (int i = 0; i < puffs.Length; i++)
                    {
                        if (!puffs[i]) continue;

                        float phase = Mathf.Repeat((Time.unscaledTime - t0) / 2.8f + i / 3f, 1f);
                        var rt = (RectTransform)puffs[i].transform;

                        rt.anchoredPosition = new Vector2(size.x * .18f + phase * size.x * .07f,
                                                          phase * rise);
                        rt.localScale = Vector3.one * (.55f + phase * .9f);
                        puffs[i].color = new Color(1f, 1f, 1f, .34f * (1f - phase) * Mathf.Min(1f, phase * 6f));
                    }
                }, art, "smoke");
            }

            // Lanterns at the door, then a gilded ridge on the last rung. Each rung adds
            // something rather than replacing what came before, so the ladder reads as one
            // house being improved rather than four unrelated houses.
            if (home.Tier >= 3)
                for (int side = -1; side <= 1; side += 2)
                {
                    var lamp = UIKit.Img("Lamp", art, Art.Glow(64, 2.4f), new Color(1f, .78f, .38f, .75f),
                                         Vector2.one * (size.x * .13f), new Vector2(.5f, 0f),
                                         new Vector2(side * size.x * .30f, size.y * .10f));
                    lamp.raycastTarget = false;
                    Tween.Breathe(lamp.transform, .10f, 2.2f + side * .4f, side);
                }

            if (home.Tier >= 4)
            {
                var crown = UIKit.Img("Crown", art, Art.Glow(128, 2.6f), Pal.A(Pal.Gold, .34f),
                                      new Vector2(size.x * .86f, size.y * .34f), new Vector2(.5f, 1f),
                                      new Vector2(0f, size.y * .06f));
                crown.raycastTarget = false;
                Tween.Breathe(crown.transform, .05f, 3.8f, 1.4f);
            }

            if (home.Tier >= 5)
                Fireflies.Spawn(art, 5, new Color(1f, .90f, .60f), 5f, 11f);
        }

        void OpenHome() => Flow.Modal<HomesteadHomeOverlay>();

        // ------------------------------------------------------------------ slot
        void DrawSlot(HomesteadSlot slot, RectTransform host, float w, float h, float plotScale)
        {
            var at = new Vector2((slot.X - .5f) * w, (slot.Y - .5f) * h);
            var piece = HomesteadLayout.PieceAt(HomesteadCatalog.Current, slot.Id);

            // The tap target is the slot, never the sprite. A pebble is 38px of art and a
            // thumb is not, so a button sized to its contents would leave half the grove
            // untappable — and an empty slot has no contents at all.
            float touch = Mathf.Max(EmptyMark * 1.15f, 96f * plotScale);

            var cell = UIKit.Button("S_" + slot.Id, host, Art.Pixel, Vector2.one * touch,
                                    new Vector2(.5f, .5f), at, () => Open(slot));
            cell.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

            if (!piece.IsValid)
            {
                // The soft ring that stands in for an edit mode. Faint on purpose: it has to
                // be findable while somebody is arranging and forgettable while they are
                // looking at what they built.
                //
                // Its size says what the slot is for. A player cannot be told a slot's kind in
                // words without a label on every ring, and eleven labels on one island is a
                // form rather than a garden — but a big ring for the island's anchor and a
                // small one for a pebble reads immediately and costs nothing.
                float mark = EmptyMark * RingScale(slot.Kind);

                var ring = UIKit.Img("Empty", cell.transform, Art.Ring(96, 7f),
                                     new Color(1f, .98f, .90f, .26f),
                                     Vector2.one * mark, new Vector2(.5f, .5f), Vector2.zero);
                ring.raycastTarget = false;
                Tween.Breathe(ring.transform, .05f, 3.1f, slot.X * 2.3f);
                return;
            }

            var size = HomesteadArt.SizeOf(piece, plotScale, slot.Scale);

            // Lifted by its own height so the art stands on the slot rather than being centred
            // over it. A property of the image, not of the slot — UIKit.PillFaceLift is the
            // same lesson, learned here for the fourth time.
            var art = UIKit.Img("A", cell.transform, null, Color.white, size,
                                new Vector2(.5f, .5f), new Vector2(0f, size.y * piece.Lift));
            art.preserveAspect = true;
            art.raycastTarget = false;
            HomesteadArt.Paint(art, piece);

            // Residents breathe and decor does not. Motion is the loudest thing on a still
            // screen, so it is spent on the half of the catalog that is alive.
            if (piece.IsResident) Tween.Bob((RectTransform)art.transform, 5f, 2.6f + slot.X * .7f);
        }

        /// <summary>How big an empty slot's ring is, as a multiple of <see cref="EmptyMark"/>.</summary>
        static float RingScale(HomesteadSlotKind kind)
        {
            switch (kind)
            {
                case HomesteadSlotKind.Structure: return 1.35f;
                case HomesteadSlotKind.Canopy: return 1.2f;
                case HomesteadSlotKind.Path: return .78f;
                case HomesteadSlotKind.Edge: return .86f;
                default: return 1f;
            }
        }

        void Open(HomesteadSlot slot)
            => Flow.Modal<HomesteadPickerOverlay>(v => v.Slot = slot);
    }
}
