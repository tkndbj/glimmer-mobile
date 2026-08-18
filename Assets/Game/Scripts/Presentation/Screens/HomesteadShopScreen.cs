using System;
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
    /// Everything that can stand in a grove: what the player holds, what play will earn them,
    /// and what credits will buy.
    ///
    /// <para>
    /// A screen rather than a panel, for <c>CompanionScreen</c>'s reason: the catalog is
    /// unbounded — forty pieces today and two hundred after a few years of drops — and a grid
    /// that scrolls inside a scrim is a worse place to browse than a page that owns the
    /// display.
    /// </para>
    /// <para>
    /// <b>It shows the earned half as prominently as the priced half, and residents first.</b>
    /// That is not politeness. Most of what furnishes a grove is earned, and a shop that led
    /// with prices would teach a player that the grove is something you buy — which is exactly
    /// the reading the feature is built to avoid, and would make the thing they assembled feel
    /// like a receipt rather than a record. A resident cannot be bought at any price and the
    /// build gate proves it, so the top of this page is the part money cannot reach.
    /// </para>
    /// <para>
    /// A cell that is short of credits still opens its panel rather than greying out, which is
    /// the call <c>CompanionUnlockOverlay</c> makes and for the same reason: that is the moment
    /// a player has decided they want something, which is the best moment in the game to offer
    /// a video and the worst to teach them a control is dead.
    /// </para>
    /// <para>
    /// <b>It pages by slot kind, and that is a memory decision as much as a browsing one.</b>
    /// A single grid over the whole catalog has to load the whole catalog — a hundred textures
    /// today and several hundred after a few drops — to show the nine cells that fit on a
    /// phone. Kinds are what the content is already organised around (see
    /// <c>HomesteadSlotKind</c>), so a tab is both the obvious way to find a fence and the
    /// unit the art scope loads in: switching tabs replaces one kind's art with another's and
    /// the grove's own scope is never touched. What the shop costs is therefore the largest
    /// single kind, whatever the catalog grows to.
    /// </para>
    /// </summary>
    public sealed class HomesteadShopScreen : View, IDrawsGroveArt
    {
        public override string Track => "mus_menu";

        const float HeaderHeight = 268f;
        const int Columns = 3;
        const float CellW = 320f;
        const float CellH = 384f;
        const int CellRadius = 30;

        /// <summary>
        /// The tabs, in the order they are drawn. Residents lead for the reason the type's
        /// remarks give — the top of the page is the part money cannot reach — and the home
        /// follows, because it is the one thing here worth saving for.
        /// </summary>
        static readonly HomesteadSlotKind[] Tabs =
        {
            HomesteadSlotKind.Structure,
            HomesteadSlotKind.Canopy,
            HomesteadSlotKind.Bed,
            HomesteadSlotKind.Edge,
            HomesteadSlotKind.Path,
            HomesteadSlotKind.Ground,
        };

        const float TabRow = 104f;

        RectTransform _viewport, _grid, _tabs;
        Text _summary, _coins;

        /// <summary>Which tab is showing. Reset on every visit, deliberately: a shop that
        /// opens where you left it is a shop that opens somewhere you have to notice.</summary>
        HomesteadSlotKind _kind = HomesteadSlotKind.Structure;

        protected override void Build()
        {
            Scenery.Layered(Content, "home", .26f);
            Fireflies.Spawn(Content, 14, new Color(1f, .93f, .70f), 6f, 20f);

            BuildGrid();
            BuildHeader();
            // A side page of the Grovement rather than the Grovement itself, so the tab stays
            // live: the most natural way back from a shop is the thing it belongs to.
            NavBar.Build(Content, NavBar.Tab.Grove, onSidePage: true);

            Warm();
            HomesteadArt.OpenKindAsync(_kind, () => { if (this) Paint(); });

            HomesteadLedger.Changed += Paint;
            HomesteadCatalog.Changed += Paint;

            // The earned half of every unlock rule is derived from the star ledger, so a run
            // finished in this session changes what this page says without anything here
            // knowing a run happened.
            PlayerProgress.Reloaded += Paint;
            PlayerProgress.RecordChanged += OnRecord;
        }

        void OnDestroy()
        {
            HomesteadLedger.Changed -= Paint;
            HomesteadCatalog.Changed -= Paint;
            PlayerProgress.Reloaded -= Paint;
            PlayerProgress.RecordChanged -= OnRecord;

            // The grove screen draws from the same scope, so going back does not free art it is
            // about to ask for again — the bargain CompanionScreen makes with the profile. The
            // check itself lives in HomesteadArt now, because this screen having it and the
            // grove screen not having it is exactly how the grid ended up empty.
            HomesteadArt.CloseUnlessWanted();
        }

        public override bool OnBack() { Flow.Go<HomesteadScreen>(); return true; }

        void OnRecord(LevelRecord record) => Paint();

        async void Warm()
        {
            await HomesteadService.EnsureAsync();
            if (!this) return;

            HomesteadArt.OpenKindAsync(_kind, () => { if (this) Paint(); });
            Paint();
        }

        /// <summary>
        /// Switches tab: repaints at once so the labels move under the thumb, and loads the
        /// new kind's art behind that, repainting again when it lands.
        ///
        /// The grid is drawn before the art arrives on purpose — a tab that does nothing for
        /// a moment reads as a dead control, and every cell hides its own image until its
        /// sprite is in (invariant 7b) rather than flashing white.
        /// </summary>
        void Show(HomesteadSlotKind kind)
        {
            if (_kind == kind) return;

            _kind = kind;
            Audio.Sfx("tap", .5f);

            Paint();
            HomesteadArt.OpenKindAsync(kind, () => { if (this) Paint(); });
        }

        // ---------------------------------------------------------------- header
        void BuildHeader()
        {
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .06f, .09f, .82f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            frt.sizeDelta = new Vector2(0f, 318f);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);

            UIKit.IconButton("Back", Content, Skins.Nav, "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, 1f), new Vector2(96f, -132f), () => Flow.Go<HomesteadScreen>());

            var banner = UIKit.Img("Banner", Content, Art.S("Ui/banner"), Color.white,
                                   new Vector2(520f, 140f), new Vector2(.5f, 1f), new Vector2(0f, -128f));
            UIKit.Shrinkable(
                UIKit.Titled("Title", banner.transform, Loc.Get("ui.grove.shop").ToUpperInvariant(), 40,
                             new Color(.36f, .24f, .16f), TextAnchor.MiddleCenter,
                             new Vector2(360f, 58f), new Vector2(.5f, .5f),
                             new Vector2(0f, 140f * UIKit.PillFaceLift), 0f, 2f), 24);

            // The balance, because every price on this page is measured against it and a
            // player deciding between two pieces should not have to leave to find out.
            var pillSize = new Vector2(212f, 76f);
            var pillAnchor = new Vector2(1f, 1f);
            var pill = UIKit.Img("Coins", Content, Art.Round(22), new Color(.04f, .09f, .12f, .80f),
                                 pillSize, pillAnchor, UIKit.Corner(pillSize, pillAnchor, 28f, 94f));
            var edge = UIKit.Img("Edge", pill.transform, Art.RoundOutline(22, 3f), Pal.A(Pal.Gold, .45f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            var coin = UIKit.Img("Icon", pill.transform, null, Color.white, new Vector2(56f, 56f),
                                 new Vector2(0f, .5f), new Vector2(42f, 0f));
            coin.preserveAspect = true;
            Flipbook.Attach(coin, "Ui/Coin", 11f);

            _coins = UIKit.Titled("V", pill.transform, Profile.Short(Profile.Coins), 32, Pal.Cream,
                                  TextAnchor.MiddleCenter, new Vector2(112f, 46f), new Vector2(.5f, .5f),
                                  new Vector2(14f, 0f), 3f, 3f);

            _summary = UIKit.Shrinkable(
                UIKit.Titled("Summary", Content, string.Empty, 26,
                             new Color(1f, .96f, .88f, .72f), TextAnchor.MiddleCenter,
                             new Vector2(760f, 34f), new Vector2(.5f, 1f), new Vector2(0f, -216f), 3f, 0f), 18);

            BuildTabs();
        }

        /// <summary>
        /// One tab per kind of slot, drawn as its own art rather than a word.
        ///
        /// A glyph per tab and no label, because six translated nouns across a 1080 phone is
        /// six truncated words — and the picture answers the question better anyway: the tab
        /// that holds fences has a fence on it. The one that is showing wears a lit plate and
        /// grows; the rest sit back. Nothing here is a scroll view: six is the whole
        /// vocabulary of the content, and it is not going to become sixty.
        /// </summary>
        void BuildTabs()
        {
            _tabs = UIKit.Node("Tabs", Content);
            _tabs.anchorMin = new Vector2(0f, 1f);
            _tabs.anchorMax = new Vector2(1f, 1f);
            _tabs.pivot = new Vector2(.5f, 1f);
            _tabs.sizeDelta = new Vector2(0f, TabRow);

            // Directly under the header, and the viewport starts under *that* — the row is a
            // band of its own rather than an overlay. Placed against the header's height
            // rather than a constant so the two cannot drift apart.
            _tabs.anchoredPosition = new Vector2(0f, -HeaderHeight);

            PaintTabs();
        }

        void PaintTabs()
        {
            if (_tabs == null) return;

            for (int i = _tabs.childCount - 1; i >= 0; i--)
            {
                var old = _tabs.GetChild(i).gameObject;
                old.SetActive(false);              // Destroy only lands at end of frame
                Destroy(old);
            }

            const float step = 168f;
            float left = -(Tabs.Length - 1) * step * .5f;

            for (int i = 0; i < Tabs.Length; i++)
            {
                var kind = Tabs[i];
                bool live = kind == _kind;

                var cell = UIKit.Button("T_" + kind, _tabs, Art.Pixel, new Vector2(step - 8f, TabRow),
                                        new Vector2(.5f, .5f), new Vector2(left + i * step, 0f),
                                        () => Show(kind));
                cell.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

                var plate = UIKit.Img("P", cell.transform, Art.Round(22),
                                      live ? new Color(.10f, .26f, .27f, .96f)
                                           : new Color(.06f, .12f, .16f, .72f),
                                      new Vector2(step - 22f, TabRow - 16f), new Vector2(.5f, .5f),
                                      Vector2.zero);

                var edge = UIKit.Img("E", plate.transform, Art.RoundOutline(22, live ? 3f : 2f),
                                     live ? Pal.A(Pal.Mint, .70f) : new Color(1f, .97f, .90f, .12f));
                UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

                // The tab wears the cheapest piece of its own kind, so it labels itself out of
                // the content and a drop that adds a kind of thing needs no new icon. Falls
                // back to a ring when the catalog has nothing of that kind at all, which is a
                // content fault the validator already warns about.
                var mark = UIKit.Img("A", plate.transform, null, live ? Color.white : new Color(1f, 1f, 1f, .55f),
                                     new Vector2(TabRow - 40f, TabRow - 40f), new Vector2(.5f, .5f),
                                     Vector2.zero);
                mark.preserveAspect = true;
                mark.raycastTarget = false;
                HomesteadArt.Paint(mark, Emblem(kind));

                if (live) Tween.Pop(plate.transform, .86f, .3f);
            }
        }

        /// <summary>
        /// The piece a tab draws itself with. The rule lives in <c>HomesteadCatalog</c>,
        /// because the scope that has to have loaded it reads the same one.
        /// </summary>
        static HomesteadPiece Emblem(HomesteadSlotKind kind)
            => HomesteadCatalog.Emblem(HomesteadCatalog.Current, kind);

        // ------------------------------------------------------------------ grid
        void BuildGrid()
        {
            _viewport = UIKit.Node("Viewport", Content);
            _viewport.offsetMin = new Vector2(0f, NavBar.Height);
            _viewport.offsetMax = new Vector2(0f, -HeaderHeight - TabRow);

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

        void Paint()
        {
            if (_grid == null) return;

            for (int i = _grid.childCount - 1; i >= 0; i--)
            {
                var old = _grid.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }

            var catalog = HomesteadCatalog.Current;

            // The home ladder collapses to one cell: the rung the player is being offered, or
            // the one they live in once they are at the top. Five cells drawing five names over
            // one house is what it looked like first, and it read as a bug rather than as a
            // ladder — the ladder belongs on the home panel, where the pips can show it.
            var rung = HomesteadLedger.NextDwelling(catalog);
            if (!rung.IsValid) rung = HomesteadLedger.BestDwelling(catalog);

            // Residents and the home lead on every tab — the top of the page is the part money
            // cannot reach, and the ladder is the one thing here worth saving for, so neither
            // is worth hiding behind a tab somebody has to find. Everything after them is the
            // tab's own kind, in the catalog's order, which for decor runs cheap to expensive.
            var ordered = new List<HomesteadPiece>();
            foreach (var piece in catalog.Pieces)
            {
                if (piece.IsDwelling)
                {
                    if (string.Equals(piece.Id, rung.Id, StringComparison.Ordinal)) ordered.Add(piece);
                    continue;
                }

                if (piece.IsResident || piece.Slot == _kind) ordered.Add(piece);
            }

            var index = new Dictionary<string, int>();
            for (int i = 0; i < catalog.Pieces.Count; i++) index[catalog.Pieces[i].Id] = i;
            ordered.Sort((a, b) => a.IsResident != b.IsResident
                                       ? (a.IsResident ? -1 : 1)
                                       : index[a.Id].CompareTo(index[b.Id]));

            PaintTabs();

            for (int i = 0; i < ordered.Count; i++)
            {
                float x = (i % Columns - (Columns - 1) * .5f) * CellW;
                float y = -(i / Columns) * CellH - CellH * .5f - 12f;
                Cell(ordered[i], new Vector2(x, y), i);
            }

            int rows = (ordered.Count + Columns - 1) / Columns;
            _grid.sizeDelta = new Vector2(0f, rows * CellH + 40f);

            if (_coins) _coins.text = Profile.Short(Profile.Coins);

            if (_summary)
                _summary.text = HomesteadCatalog.IsLoaded && catalog.PieceCount > 0
                    ? Loc.Format("ui.grove.held", HomesteadLedger.HeldCount(catalog), catalog.PieceCount)
                    : Loc.Get("ui.grove.loading");
        }

        // ------------------------------------------------------------------ cell
        void Cell(HomesteadPiece piece, Vector2 at, int index)
        {
            bool held = HomesteadLedger.IsHeld(piece);

            var cell = UIKit.Button("G_" + piece.Id, _grid, Art.Pixel,
                                    new Vector2(CellW - 16f, CellH - 20f), new Vector2(.5f, 1f), at,
                                    () => Tap(piece));
            cell.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

            // A locked plate is *lighter* than a held one, which looks backwards and is not.
            // The art on it is the thing that has to read, and half this catalog is dark —
            // a brown log or a bramble on a near-black plate is a black rectangle, which is
            // exactly what shipped. Held cells are marked out by their mint edge, their
            // caption and the absence of a padlock, none of which depend on the plate.
            var plate = UIKit.Img("Plate", cell.transform, Art.Round(CellRadius),
                                  held ? new Color(.07f, .16f, .17f, .93f) : new Color(.11f, .18f, .24f, .90f),
                                  new Vector2(CellW - 28f, CellH - 34f), new Vector2(.5f, .5f), Vector2.zero);

            // A home wears gold whether or not it is held, because the ladder is the one thing
            // on this page a player is meant to be saving for rather than browsing.
            var edge = UIKit.Img("Edge", plate.transform,
                                 Art.RoundOutline(CellRadius, piece.IsDwelling ? 4f : held ? 3f : 2f),
                                 piece.IsDwelling ? Pal.A(Pal.Gold, .70f)
                                                  : held ? Pal.A(Pal.Mint, .55f)
                                                         : new Color(1f, .97f, .90f, .14f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            // Locked art draws in **its own colours**, barely knocked back. Tinting it toward
            // a grey silhouette was the obvious idea and it is wrong here: a tint multiplies,
            // so it only ever darkens, and the pieces that most need to be recognised before
            // you buy them — a fallen log, brambles, a cave — are the dark ones. A shop whose
            // locked half is unreadable is a shop that cannot sell anything. The padlock says
            // "not yours"; the picture is there to say what it is.
            //
            // The tint is set here rather than after Paint because Paint only ever writes
            // alpha: an image whose art has not arrived is hidden rather than left white
            // (invariant 7b), and it must not undo whoever decided what colour the thing is.
            var art = UIKit.Img("A", plate.transform, null,
                                held ? Color.white : new Color(.88f, .92f, .96f, 1f),
                                new Vector2(170f, 170f), new Vector2(.5f, 1f), new Vector2(0f, -104f));
            art.preserveAspect = true;
            art.raycastTarget = false;
            HomesteadArt.Paint(art, piece);

            if (!held)
            {
                var padlock = UIKit.Img("Lock", plate.transform, Art.S("Ui/padlock"), Color.white,
                                        new Vector2(66f, 66f), new Vector2(1f, 1f), new Vector2(-24f, -24f));
                padlock.preserveAspect = true;
                padlock.raycastTarget = false;
            }

            if (piece.IsResident)
            {
                var mark = UIKit.Img("Leaf", plate.transform, Art.Leaf(64), Pal.A(Pal.Verdant, .85f),
                                     new Vector2(44f, 44f), new Vector2(0f, 1f), new Vector2(26f, -26f));
                mark.raycastTarget = false;
            }

            UIKit.Shrinkable(
                UIKit.Titled("N", plate.transform, Loc.Get(piece.NameKey), 30,
                             held ? Pal.Cream : new Color(1f, .95f, .88f, .62f),
                             TextAnchor.MiddleCenter, new Vector2(CellW - 60f, 42f), new Vector2(.5f, 0f),
                             new Vector2(0f, 82f), 3f, 3f), 17);

            var (line, tint) = StatusOf(piece, held);
            UIKit.Shrinkable(
                UIKit.Titled("S", plate.transform, line, 24, tint, TextAnchor.MiddleCenter,
                             new Vector2(CellW - 52f, 60f), new Vector2(.5f, 0f),
                             new Vector2(0f, 34f), 3f, 0f), 16);

            cell.transform.localScale = Vector3.zero;
            Tween.Pop(cell.transform, 0f, .5f, .04f * Mathf.Min(index, 12));
        }

        /// <summary>
        /// The one line under a piece, and there is exactly one because a cell that stacks a
        /// price over a requirement over a balance is a receipt.
        ///
        /// Each state renders a different sentence, which is <c>AdOfferState</c>'s bargain: a
        /// single "locked" would draw the same caption for a piece 40 credits away and one
        /// that will never be for sale, and only one of those resolves by playing for an hour.
        /// </summary>
        static (string, Color) StatusOf(HomesteadPiece piece, bool held)
        {
            // A home is never "yours" in the sense the rest of the grid means it — the player
            // always has one. What the cell has to say is whether this is the next one up.
            if (piece.IsDwelling)
                return held
                    ? (Loc.Get("ui.grove.home_best"), Pal.A(Pal.Gold, .95f))
                    : (Loc.Format("ui.grove.price", piece.Cost),
                       Profile.CanAfford(piece.Cost) ? Pal.A(Pal.Sun, .95f) : Pal.A(Pal.Sun, .58f));

            if (held) return (Loc.Get("ui.grove.yours"), Pal.A(Pal.Mint, .95f));

            if (piece.RequiresLevel.IsValid)
                return (Loc.Format("ui.grove.needs_glade", LevelName(piece.RequiresLevel)),
                        Pal.A(Pal.Aqua, .95f));

            if (piece.RequiresChapter.IsValid)
                return (Loc.Format("ui.grove.needs_chapter", ChapterName(piece.RequiresChapter)),
                        Pal.A(Pal.Aqua, .95f));

            if (piece.IsForSale)
                return (Loc.Format("ui.grove.price", piece.Cost),
                        Profile.CanAfford(piece.Cost) ? Pal.A(Pal.Sun, .95f) : Pal.A(Pal.Sun, .58f));

            // Left over: no requirement, no price, and not held — which the catalog cannot
            // produce, since a piece with neither is a starter. Said plainly rather than left
            // blank, so a content mistake shows up on the screen it broke.
            return (Loc.Get("ui.grove.not_for_sale"), new Color(1f, .96f, .88f, .55f));
        }

        /// <summary>
        /// A glade's name from its id alone, with no file read — which is invariant 5a
        /// earning its keep. This screen names up to two hundred requirements and would
        /// otherwise have to load every chapter body to do it.
        /// </summary>
        static string LevelName(LevelId id) => Loc.Get(LevelDefinition.DefaultNameKey(id));

        static string ChapterName(ChapterId id)
        {
            var chapter = GameContent.FindChapter(id);
            return chapter != null ? Loc.Get(chapter.NameKey) : Loc.Get("ui.grove.soon");
        }

        void Tap(HomesteadPiece piece)
        {
            // A home goes to the home panel in every state — held, next, or five rungs away.
            // The question at a house is never "shall I buy this one item"; it is "where am I
            // on the ladder", and that panel is the only thing that answers it.
            if (piece.IsDwelling) { Flow.Modal<HomesteadHomeOverlay>(); return; }

            if (HomesteadLedger.IsHeld(piece))
            {
                Scenery.Toast(Content, Loc.Format("ui.grove.already", Loc.Get(piece.NameKey)), Pal.Mint);
                return;
            }

            if (!piece.IsForSale)
            {
                var (line, _) = StatusOf(piece, false);
                Scenery.Toast(Content, line, Pal.Aqua);
                return;
            }

            Flow.Modal<HomesteadBuyOverlay>(v => v.Piece = piece);
        }
    }
}
