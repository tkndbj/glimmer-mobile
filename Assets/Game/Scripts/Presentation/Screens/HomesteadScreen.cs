using System;
using System.Collections.Generic;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The Grovement: a floor of tiles the player owns, buys and builds on.
    ///
    /// <para>
    /// <b>This replaced a ladder of floating islands, and the difference is not decorative.</b>
    /// An island carried hand-authored slots, each with a position, a size and a role, so the
    /// player's decision was which of eleven pre-placed dots got which sticker — every grove
    /// came out with the same composition and different stickers on it. A field of identical
    /// tiles moves the composition to the player: where a thing goes is now as much their
    /// choice as what it is. That is why the slot-kind rule went with the islands (see
    /// <c>HomesteadSlotKind</c>) — it existed to stop a sprinkle of dots looking accidental,
    /// and there are no dots.
    /// </para>
    /// <para>
    /// <b>What it costs and what it does not.</b> The save file gained one field — which
    /// regions of the floor were bought (invariant 15, a union-joined id set) — because land is
    /// paid for now rather than earned from chapters, and that is the one thing here that could
    /// not stay derived. It gained nothing else: a tile is a slot, its id is permanent, and an
    /// untouched tile writes no row, so a three-hundred-tile floor with two things on it costs
    /// two rows exactly as ten islands did. A piece two tiles wide still writes one row — its
    /// footprint is derived from the catalog (<see cref="GroveOccupancy"/>).
    /// </para>
    /// <para>
    /// <b>Two things a field needs that islands did not.</b> Depth has to be computed rather
    /// than authored, because what stands in front of what is now a consequence of where the
    /// player put things — see <c>GroveFootprint.Depth</c>. And the tiles have to be culled,
    /// because a floor is hundreds of them and a phone shows dozens; see
    /// <see cref="GroveFieldView"/>, which is <c>GridView</c>'s bargain in two dimensions.
    /// </para>
    /// </summary>
    public sealed class HomesteadScreen : View, IDrawsGroveArt
    {
        public override string Track => "mus_menu";

        /// <summary>
        /// The one screen in the game that takes two fingers. See <see cref="View.WantsMultiTouch"/>
        /// for why it is declared rather than switched on, and why a board must never inherit it.
        /// </summary>
        public override bool WantsMultiTouch => true;

        public override bool OnBack()
        {
            // The back key ends the ceremony rather than the screen, which is what it means
            // everywhere else in the game: it closes the innermost thing that is open. Leaving
            // instead would cost nothing that is stored — the land is bought either way — but
            // it would answer "let me get on with it" by taking the grove away.
            if (_rise != null) { _rise.Skip(); return true; }

            Flow.Go<HomeScreen>();
            return true;
        }

        /// <summary>
        /// Ground bought a moment ago, which this screen was opened to show arriving. Null on
        /// every ordinary visit.
        ///
        /// <para>
        /// Set by <c>GroveLandOverlay</c> through <c>Flow.Go</c>, which is what makes the
        /// purchase and the ceremony one act rather than two: a player who buys land is taken
        /// back to their grove and shown it happening, instead of walking back to a floor that
        /// is simply bigger than it was. Nothing about it is stored — the purchase is already
        /// recorded and this is only a request to <em>animate</em> it, so a player who kills
        /// the app mid-ceremony finds their land exactly where they left it, undecorated.
        /// </para>
        /// </summary>
        public GroveRegion Arriving { get; set; }

        /// <summary>
        /// How many stars the grove was worth before that purchase, or -1 when nothing is
        /// arriving.
        ///
        /// <para>
        /// Carried rather than derived because it cannot be: land is bought in the shop, so by
        /// the time this screen exists the score has already moved, and the star row's own
        /// rule — celebrate what was not there a moment ago — has no "a moment ago" to compare
        /// against on a screen that has just been built. Without it the reward for the single
        /// most expensive thing in the game would be a number that had quietly changed while
        /// the player was looking somewhere else, which is exactly what drawing the score here
        /// was meant to fix.
        /// </para>
        /// </summary>
        public int ArrivingStars { get; set; } = -1;

        const float HeaderHeight = 214f;

        /// <summary>
        /// Size of the ring marking a buildable tile with nothing on it.
        ///
        /// Grown from 64 when the floor was reported hard to aim at: the ring is the one thing
        /// on an empty tile that says <em>here</em>, and at the old size it read as a speck
        /// between two fences.
        /// </summary>
        const float EmptyMark = 84f;

        /// <summary>
        /// The top-left pair — the way out and the way to the boards.
        ///
        /// Declared once because they are only a pair while they agree about size and
        /// baseline: held as literals at their two call sites, a change to one is a change
        /// somebody has to remember to make twice, and the failure is a row that is subtly
        /// not a row.
        /// </summary>
        static readonly Vector2 NavSize = new Vector2(112f, 112f);
        const float NavX = 92f, NavY = -104f, NavGap = 16f;

        RectTransform _viewport;
        GroveFieldView _field;
        Text _summary;

        RectTransform _shop;
        Text _scoreValue, _scoreNext;
        StarRow _scoreStars;

        /// <summary>
        /// Stars drawn last, so a star won while the player is standing here arrives as
        /// something rather than as a number that was already different.
        ///
        /// Session-local and deliberately not stored: the save already knows everything the
        /// score is derived from, and a "stars last seen" field would be a stored count of
        /// exactly the shape invariant 11b forbids — merged across devices it could only ever
        /// re-celebrate or silently swallow. -1 means nothing has been drawn yet, which is
        /// what makes the first paint of a screen quiet.
        /// </summary>
        int _starsShown = -1;

        bool _presented, _teaching, _taught;

        /// <summary>
        /// The arrival ceremony, and the ground it is still holding back.
        ///
        /// <para>
        /// Two fields rather than one because they answer at different times.
        /// <see cref="_pending"/> is set the instant this screen is built, which is what keeps
        /// the new land off the very first paint — the iris opens on the grove as it was, and
        /// nothing has to be un-drawn. <see cref="_rise"/> exists only once there is a floor to
        /// stage it against, and takes the question over from there.
        /// </para>
        /// </summary>
        GroveRegion _pending;
        GroveRise _rise;

        protected override void Build()
        {
            // Read before anything can draw. Everything below asks whether ground is being
            // withheld, and a screen that painted the new land once and took it away again
            // would spend the whole ceremony undoing its own first frame.
            _pending = Arriving;

            // The hub's own sky and nothing else from it. The grove here is the content, so
            // laying the hub's ground and decoration behind it would be two groves in one
            // picture — and this one is supposed to be the player's.
            Scenery.Cover(Content, "home_sky", .05f, .42f);
            Fireflies.Spawn(Content, 16, new Color(1f, .93f, .70f), 6f, 20f);

            BuildField();
            BuildHeader();

            // The catalog is a body, read on entering the feature. Both this and the art load
            // asynchronously and both repaint, because a screen is built in the frame it is
            // asked for and the first paint would otherwise be the only one.
            Warm();
            HomesteadArt.OpenAsync(() => { if (this) Repaint(); });

            HomesteadCatalog.Changed += Reload;
            HomesteadLedger.Changed += Repaint;
            HomesteadLayout.Changed += Repaint;
            // Art claimed for a piece the player has just placed lands a moment after the
            // placement does, and until it does the tile draws nothing (invariant 7b).
            HomesteadArt.Changed += Repaint;
            // Buying land adds ground, which is a different set of tiles rather than a different
            // look on the same ones — so it re-measures and refills rather than rebinding.
            GroveLand.Changed += Regrow;
            PlayerProgression.Changed += Repaint;

            // Residents are derived from the keeper ladder, so a run finished in this session
            // can wake a friend while the player is standing here.
            PlayerProgress.Reloaded += Repaint;
            PlayerProgress.RecordChanged += OnRecord;
        }

        void OnDestroy()
        {
            HomesteadCatalog.Changed -= Reload;
            HomesteadLedger.Changed -= Repaint;
            HomesteadLayout.Changed -= Repaint;
            HomesteadArt.Changed -= Repaint;
            GroveLand.Changed -= Regrow;
            PlayerProgression.Changed -= Repaint;
            PlayerProgress.Reloaded -= Repaint;
            PlayerProgress.RecordChanged -= OnRecord;

            // Unless the shop is what replaced this screen, which is not a special case so much
            // as the general one: Destroy lands at the end of the frame, so the incoming screen
            // has already built *and painted* by the time this runs. Releasing here pulled every
            // sprite out from under a shop that had already drawn it, and nothing repaints.
            // HomesteadArt owns the rule so a third screen cannot forget half of it.
            HomesteadArt.CloseUnlessWanted();
        }

        void OnRecord(LevelRecord record) => Repaint();

        /// <summary>Takes newly bought ground: re-measures the field and refills it, in place.</summary>
        void Regrow()
        {
            if (_field == null) return;

            // A ceremony owns the ground while it is running, and refilling the field under one
            // would throw away the tiles it is in the middle of raising. Unreachable today —
            // land is only sold in the shop, which is a different screen — and one line rather
            // than a comment explaining why it cannot happen.
            if (_rise != null) return;

            // The ground itself changed, so a bar anchored to a tile is anchored to a fact that
            // no longer holds.
            CloseEditor();

            ShowOwned();
            _field.Rebuild();
            Repaint();
        }

        async void Warm()
        {
            await HomesteadService.EnsureAsync();
            if (!this) return;

            // The art set is derived from the catalog, so it can only be asked for once the
            // catalog is in hand. Asking twice is free — the scope reports itself loaded.
            HomesteadArt.OpenAsync(() => { if (this) Repaint(); });
            Reload();
        }

        // ----------------------------------------------------------------- field
        void BuildField()
        {
            // Laid inside the display's safe area rather than across the whole panel. A field
            // that runs under a camera cutout is a field with tiles the player cannot see and
            // cannot reliably tap, and the corner where a home indicator sits is exactly where
            // a thumb rests to pan. The node re-fits itself (see SafeArea), so a late reading —
            // iOS reports its inset a frame or two after a cold start — moves the field rather
            // than leaving it where a stale number put it. On a display with nothing in the way
            // every inset is zero and this is the layout it always was.
            var stage = SafeArea.Node("Stage", Content);

            _viewport = UIKit.Node("Viewport", stage);
            // No nav bar on this screen. It is the one page in the game that wants the whole
            // display: a floor is panned and zoomed, and a strip of chrome across the bottom is
            // both a slice of grove nobody can see and a row of buttons a dragging thumb keeps
            // catching. The corner arrow is the way out.
            _viewport.offsetMin = new Vector2(0f, 24f);
            _viewport.offsetMax = new Vector2(0f, -HeaderHeight);

            _field = GroveFieldView.Attach(_viewport, HomesteadCatalog.Current.Floor,
                                           (col, row) => new TileCell(this));
            _field.TileTapped = Tap;
            _field.TileHeld = Hold;
            _field.Hit = Hit;

            // Tapping the sky puts the editing controls away, exactly as tapping a tile does.
            // The two have to agree: the sky is the largest target on this screen and the one a
            // player aims at when they mean "never mind".
            _field.TappedNothing = CloseEditor;
            ShowOwned();
        }

        /// <summary>
        /// Takes a new floor: throws every tile away and opens the camera on the hall.
        ///
        /// Called when the catalog is published and once when the body has been read — the two
        /// moments the <em>ground</em> differs. Everything else is a <see cref="Repaint"/>,
        /// which rebinds the tiles that exist without moving the camera, because a player who
        /// places a bench has not asked to be taken anywhere.
        /// </summary>
        void Reload()
        {
            if (_field == null) return;

            var catalog = HomesteadCatalog.Current;
            var floor = catalog.Floor;

            // Called at least twice in the ordinary case — the catalog raises its event and
            // Warm calls this directly — and it throws every tile away when it runs. A ceremony
            // already staged against this same ground must not be rebuilt out from under
            // itself; one staged against ground that has genuinely been republished is a
            // ceremony about a floor that no longer exists, so the land is simply delivered and
            // nothing is lost but the show.
            if (_rise != null)
            {
                if (_rise.Stages(floor)) { Repaint(); return; }
                _rise.Skip();
            }

            CloseEditor();

            _field.SetFloor(floor);

            // How far the tallest and widest piece in the catalog reaches beyond its tile, so
            // the culling window keeps a tile alive while its picture is on screen.
            GroveTileArt.Reach(catalog, out float up, out float side);
            _field.SetReach(up, side);

            ShowOwned();
            _field.Rebuild();

            // A ceremony frames its own shot, and it has to be allowed to: the whole point of
            // the framing is that it is not where the player would have been left.
            if (!OpenRise(floor))
            {
                // Opened on the hall rather than on the field's origin, which is the corner of
                // a diamond and therefore the emptiest place on the screen. The hall is two
                // tiles deep, so its centre rather than its anchor.
                if (GroveFloor.TryParse(floor.HallTile, out int col, out int row))
                    _field.CentreOn(floor.HallFootprint.CentreCol(col), floor.HallFootprint.CentreRow(row));
                else
                    _field.CentreOn(floor.Cols / 2, floor.Rows / 2);
            }

            Repaint();

            // The body usually arrives before the transition finishes and sometimes after it.
            // Both of these are attempted from both ends rather than from whichever happens to
            // be second: a lesson shown once in a player's life must not be spent on a screen
            // that had nothing on it yet, and neither must a ceremony — see Teach and
            // StartRise.
            StartRise();
            Teach();
        }

        // -------------------------------------------------------------- arriving
        /// <summary>
        /// Stages the ceremony for ground bought a moment ago, if there is any. True once
        /// something is framing the camera, so the ordinary opening shot stands aside.
        /// </summary>
        bool OpenRise(GroveFloor floor)
        {
            if (_rise != null) return true;
            if (_pending == null) return false;

            // The catalog is a body and may have been republished between the purchase and
            // this screen, so the region is looked up again rather than trusted. A region that
            // is no longer on the floor is simply not celebrated; the land is still owned.
            var region = _pending;
            if (floor.IsEmpty || floor.Region(region.Id) == null)
            {
                _pending = null;
                return false;
            }

            _rise = GroveRise.Play(Content, _field, floor, region,
                                   (col, row) => GroveLand.IsOwned(floor, col, row)
                                              && !region.Holds(col, row),
                                   OnRiseDone);

            return _rise != null;
        }

        /// <summary>
        /// Starts the ceremony once there is both a staged one and a screen the player can
        /// see. Does nothing twice — <c>GroveRise.Begin</c> holds that rule rather than a flag
        /// here, so a second caller cannot get it wrong.
        /// </summary>
        void StartRise()
        {
            if (_presented) _rise?.Begin();
        }

        /// <summary>
        /// Hands the ground back. The withholding stops, the field is re-tested, and whatever
        /// the ceremony was standing in front of happens now: the star the purchase earned,
        /// and the first-visit lessons it was holding up.
        /// </summary>
        void OnRiseDone()
        {
            _rise = null;
            _pending = null;

            if (_field != null)
            {
                ShowOwned();
                _field.Revisit();
            }

            Repaint();
            CelebrateArrival();
            Teach();
        }

        /// <summary>
        /// A star won by the purchase, landed now rather than while the player was in the shop.
        ///
        /// <para>
        /// Measured against the reading taken before the money was spent (see
        /// <see cref="ArrivingStars"/>) rather than against <see cref="_starsShown"/>, which
        /// this screen's first paint has already moved to the new figure — quietly, and
        /// deliberately, because a baseline taken on a blank grove is how a celebration comes
        /// to mean nothing.
        /// </para>
        /// </summary>
        void CelebrateArrival()
        {
            if (ArrivingStars < 0 || _scoreStars == null) return;

            int before = ArrivingStars;
            ArrivingStars = -1;

            if (!HomesteadCatalog.IsLoaded) return;

            int stars = Mathf.Min(GroveScore.Of(HomesteadCatalog.Current).Stars, _scoreStars.Count);
            if (stars > before) _scoreStars.Reveal(stars, .12f, .32f);
        }

        /// <summary>
        /// Which ground exists. Unowned land is not drawn at all — see
        /// <c>GroveFieldView.SetVisible</c> for why a field of padlocks was the wrong screen.
        ///
        /// <para>
        /// Ground bought a moment ago is owned and still withheld, which is the one place
        /// those two come apart. Kept as an <c>and</c> of two questions rather than folded
        /// into one predicate: what the player owns is <c>GroveLand</c>'s answer and never
        /// this screen's, and a ceremony that could make land look unowned is a ceremony one
        /// bug away from selling it twice.
        /// </para>
        /// </summary>
        bool Owned(int col, int row)
            => GroveLand.IsOwned(HomesteadCatalog.Current.Floor, col, row)
            && !Withheld(col, row);

        bool Withheld(int col, int row)
            => _rise != null
                ? _rise.Hides(col, row)
                : _pending != null && _pending.Holds(col, row);

        /// <summary>
        /// True exactly once per tile of arriving ground, on the bind that first draws it.
        /// The cell's signal to rise into place rather than appear — see <c>GroveRise</c>.
        /// </summary>
        bool TakeArrival(int col, int row) => _rise != null && _rise.TakeArrival(col, row);

        /// <summary>
        /// Tells the field which ground exists and how far it reaches.
        ///
        /// The bounds come from the regions rather than from a sweep of every tile — see
        /// <c>GroveLand.OwnedBounds</c>. Held as one method because the two have to be set
        /// together: a predicate without matching bounds is a field the camera can drag off.
        /// </summary>
        void ShowOwned()
        {
            var floor = HomesteadCatalog.Current.Floor;

            GroveLand.OwnedBounds(floor, out int minCol, out int minRow, out int maxCol, out int maxRow);
            _field.SetVisible(Owned, minCol, minRow, maxCol, maxRow);
        }

        /// <summary>Redraws the tiles that exist, in place, without moving the camera.</summary>
        void Repaint()
        {
            if (_field == null) return;

            // The boxes describe what is drawn, so they are only valid for as long as the
            // drawing is. Cleared here rather than at each writer, because this is the one
            // method every change already comes through.
            _hits.Clear();

            _field.Refresh();
            PaintSummary();
            PaintScore();
        }

        // ---------------------------------------------------------------- header
        void BuildHeader()
        {
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .06f, .09f, .82f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            // Grown by whatever the system has taken from the top, because the fade is what
            // the banner and the summary are read against and both have just moved down by
            // that much. It is the one piece of the header that stays full-bleed — a gradient
            // that stopped at the safe edge would draw a visible horizontal seam across the
            // sky, which is worse than the cutout it was avoiding. Zero on a plain display.
            frt.sizeDelta = new Vector2(0f, 268f + SafeArea.Top);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);

            // Everything from here down is chrome, so it lives in the safe layer: on a phone
            // with a camera cutout the back arrow, the banner and the shop button all sat
            // under it, which is what this screen was reported for. Content stays full-bleed
            // and keeps the sky and the fade above — see View.Safe.
            var chrome = Safe;

            var banner = UIKit.Img("Banner", chrome, Art.S("Ui/banner"), Color.white,
                                   new Vector2(430f, 114f), new Vector2(.5f, 1f), new Vector2(0f, -102f));
            UIKit.Shrinkable(
                UIKit.Titled("Title", banner.transform, Loc.Get("ui.grove.title").ToUpperInvariant(), 32,
                             new Color(.36f, .24f, .16f), TextAnchor.MiddleCenter,
                             new Vector2(300f, 46f), new Vector2(.5f, .5f),
                             new Vector2(0f, 114f * UIKit.PillFaceLift), 0f, 2f), 20);
            banner.transform.localScale = Vector3.zero;
            Tween.Pop(banner.transform, 0f, .6f, .1f);

            // The way out, where the balance used to be. The nav bar is gone from this screen
            // (see BuildField), so the corner needs an exit rather than a readout — and the
            // balance was the wrong thing to put here anyway: nothing on this screen is bought.
            // Land and decor are both bought in the shop, which shows the balance itself.
            UIKit.IconButton("Back", chrome, Skins.Nav, "ic_left", NavSize,
                             new Vector2(0f, 1f), new Vector2(NavX, NavY),
                             () => Flow.Go<HomeScreen>());

            _summary = UIKit.Shrinkable(
                UIKit.Titled("Summary", chrome, string.Empty, 26,
                             new Color(1f, .96f, .88f, .72f), TextAnchor.MiddleCenter,
                             new Vector2(720f, 34f), new Vector2(.5f, 1f), new Vector2(0f, -172f), 3f, 0f), 18);

            // The shop is a screen of its own rather than a panel over this one, for
            // CompanionScreen's reason: what it lists is unbounded, and a grid that scrolls
            // inside a scrim is a worse place to browse than a page that owns the display.
            // Placed through UIKit.Corner because Box pivots at centre: passing the margin
            // straight in put half the button past the right edge of the screen.
            var shopSize = new Vector2(230f, 96f);
            var shopAnchor = new Vector2(1f, 1f);
            var shop = UIKit.TextButton("Shop", chrome, "btn_orange", Loc.Get("ui.grove.shop"), 28,
                                        shopSize, shopAnchor,
                                        UIKit.Corner(shopSize, shopAnchor, 28f, 62f),
                                        () => Flow.Go<HomesteadShopScreen>());
            UIKit.Shrinkable(shop.Label, 18);
            UIKit.FitLabel(shop);

            // Kept so the shop lesson can ring the real button rather than describe where it is.
            _shop = (RectTransform)shop.transform;

            // The way to the boards, beside the way out and the same size as it. The two are a
            // pair — both are "leave this screen" — so they read as a row rather than as a
            // control and a smaller afterthought stacked under it.
            //
            // It is deliberately *not* the score box in the corner: that box is a readout with
            // every raycast target switched off, because this screen is panned and pinched and
            // a control there would swallow a drag begun where a right thumb rests. A separate
            // button costs one glyph and leaves the gesture alone.
            UIKit.IconButton("Boards", chrome, Skins.Nav, "ic_trophy", NavSize,
                             new Vector2(0f, 1f), new Vector2(NavX + NavSize.x + NavGap, NavY),
                             () => Flow.Go<LeaderboardScreen>());

            BuildScore(chrome);
        }

        void PaintSummary()
        {
            if (!_summary) return;

            var catalog = HomesteadCatalog.Current;

            if (!HomesteadCatalog.IsLoaded)
            {
                _summary.text = Loc.Get("ui.grove.loading");
                return;
            }

            if (catalog.Floor.IsEmpty)
            {
                _summary.text = Loc.Get("ui.grove.unavailable");
                return;
            }

            _summary.text = Loc.Format("ui.grove.summary",
                                       HomesteadLayout.OccupiedCount(catalog),
                                       GroveLand.OwnedTileCount(catalog.Floor),
                                       HomesteadLayout.VarietyCount(catalog));
        }

        // ----------------------------------------------------------------- score
        /// <summary>Widest the star row may grow before it is packed tighter.</summary>
        const float StarsWidth = 292f;

        /// <summary>
        /// What this grove is worth, and the stars that has earned.
        ///
        /// <para>
        /// <b>It is a readout and not a control, and every graphic in it is non-interactive.</b>
        /// This screen is panned and pinched, so a box in the corner that swallowed a drag
        /// would break the one gesture the whole page is built on — and the corner it sits in
        /// is where a right thumb rests. <see cref="UIKit.Img"/> and <see cref="UIKit.Label"/>
        /// both leave <c>raycastTarget</c> off, so a drag begun on top of this reaches the
        /// field exactly as if the box were not there.
        /// </para>
        /// <para>
        /// The star row's size comes from the ladder's length rather than from a constant,
        /// because the ladder is content (<c>GroveScoreTable</c>) and a drop may lengthen it.
        /// Five stars at the shipped spacing, eight packed a little tighter, and neither draws
        /// off the side of the box.
        /// </para>
        /// </summary>
        void BuildScore(Transform parent)
        {
            var size = new Vector2(340f, 196f);
            var anchor = new Vector2(1f, 0f);

            var box = UIKit.Img("Score", parent, Art.Round(28), new Color(.06f, .12f, .17f, .74f),
                                size, anchor, UIKit.Corner(size, anchor, 28f, 28f));
            var rt = (RectTransform)box.transform;

            var edge = UIKit.Img("Edge", rt, Art.RoundOutline(28, 3f), new Color(1f, 1f, 1f, .13f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            UIKit.Shrinkable(
                UIKit.Titled("Label", rt, Loc.Get("ui.grove.score").ToUpperInvariant(), 22,
                             new Color(1f, .96f, .88f, .70f), TextAnchor.MiddleCenter,
                             new Vector2(300f, 30f), new Vector2(.5f, 1f), new Vector2(0f, -26f),
                             outline: 3f, shadow: 0f), 16);

            _scoreValue = UIKit.Shrinkable(
                UIKit.Titled("Value", rt, string.Empty, 44, Pal.Gold, TextAnchor.MiddleCenter,
                             new Vector2(300f, 56f), new Vector2(.5f, 1f), new Vector2(0f, -74f),
                             outline: 4f, shadow: 4f), 26);

            int rungs = Mathf.Max(1, HomesteadCatalog.Current.Scores.StarCount);
            float spacing = Mathf.Min(40f, StarsWidth / rungs);

            _scoreStars = StarRow.Create(rt, new Vector2(.5f, 1f), new Vector2(0f, -128f),
                                         spacing * .82f, spacing, 0, false, rungs);

            _scoreNext = UIKit.Shrinkable(
                UIKit.Titled("Next", rt, string.Empty, 22, new Color(1f, .96f, .88f, .58f),
                             TextAnchor.MiddleCenter, new Vector2(310f, 28f), new Vector2(.5f, 1f),
                             new Vector2(0f, -168f), outline: 3f, shadow: 0f), 15);

            rt.localScale = Vector3.zero;
            Tween.Pop(rt, 0f, .5f, .18f);

            // Drawn once here so the box never appears blank. The catalog is a body and may
            // not have arrived, in which case this is an honest zero that the first Repaint
            // replaces — see PaintScore for why that first real reading does not celebrate.
            PaintScore();
        }

        /// <summary>
        /// Redraws the standing, and celebrates a star that was not there a moment ago.
        ///
        /// <para>
        /// The whole reading is taken in one call (<see cref="GroveScore.Of"/>) so the number
        /// and the stars can never come from two different moments — the mistake the victory
        /// panel's separately derived reward row spent a version proving is real.
        /// </para>
        /// <para>
        /// A star gained while this screen is open re-runs the row's fanfare rather than
        /// appearing. That is the point of drawing this here at all: buying land or a companion
        /// happens in the shop, so without it the reward for a purchase would be a number that
        /// had quietly changed by the time the player came back.
        /// </para>
        /// </summary>
        void PaintScore()
        {
            if (!_scoreValue) return;

            var standing = GroveScore.Of(HomesteadCatalog.Current);

            _scoreValue.text = Compact.Number(standing.Score);

            if (_scoreNext)
                _scoreNext.text = standing.IsTopped
                    ? Loc.Get("ui.grove.score_top")
                    : Loc.Format("ui.grove.score_next", Compact.Number(standing.ToNext));

            if (!_scoreStars) return;

            // A ladder can be re-published under an open screen — the catalog is a body and a
            // content refresh swaps it whole — so a row built for five rungs may be looking at
            // six. Rebuilding it is not worth a frame's work; drawing what it can hold is
            // honest, and the next visit builds the right row.
            int stars = Mathf.Min(standing.Stars, _scoreStars.Count);

            // The baseline is only taken once there is a real catalog to compare against.
            // Without that the empty grove drawn before the body arrives would be the
            // baseline, and every visit would open with a fanfare for stars the player won
            // weeks ago — which is the fastest way to make a celebration mean nothing.
            bool settled = HomesteadCatalog.IsLoaded;

            if (settled && _starsShown >= 0 && stars > _starsShown) _scoreStars.Reveal(stars, .1f, .3f);
            else _scoreStars.SetInstant(stars);

            if (settled) _starsShown = stars;
        }

        // ------------------------------------------------------------ what is drawn
        /// <summary>
        /// The piece a stand draws: the best home the player owns on the hall, and whatever
        /// the stand names everywhere else.
        ///
        /// <para>
        /// Held in one place because two things need the same answer and a disagreement between
        /// them is invisible: the cell that <em>paints</em> the tile, and the box that decides
        /// what a finger <em>hit</em>. If those drifted, the player would be picking pieces from
        /// somewhere other than where the picture puts them.
        /// </para>
        /// </summary>
        static HomesteadPiece PieceOf(HomesteadCatalog catalog, GroveStand stand)
            => stand.IsHall
                ? HomesteadLedger.BestDwelling(catalog)
                : catalog.Find(stand.PieceId);

        /// <summary>The stand covering a tile — anchored on it or reaching over it — or an invalid one.</summary>
        static bool StandAt(int col, int row, out GroveStand stand)
            => HomesteadLayout.TryStandAt(HomesteadCatalog.Current, col, row, out stand);

        readonly Dictionary<long, GroveHit> _hits = new Dictionary<long, GroveHit>();

        /// <summary>
        /// The box and mask of the art drawn from a tile, in field space — what
        /// <see cref="GrovePick"/> tests a tap against. Answers for an anchor only; a tile a
        /// footprint reaches over draws nothing of its own.
        ///
        /// <para>
        /// Cached per tile because this is asked for every live tile on every tap <em>and</em>
        /// on every frame of a move drag. Sixty tiles a frame under a moving thumb is exactly
        /// the continuous garbage the field's depth comparer is held as a field to avoid. The
        /// cache is cleared by <see cref="Repaint"/>, which is the one door every change to
        /// the picture comes through.
        /// </para>
        /// </summary>
        GroveHit Hit(int col, int row)
        {
            long key = GroveOccupancy.Key(col, row);
            if (_hits.TryGetValue(key, out var cached)) return cached;

            var catalog = HomesteadCatalog.Current;
            var hit = new GroveHit(col, row, 0f, 0f, 0f, 0f);

            if (HomesteadLayout.Occupancy(catalog).TryAnchored(col, row, out var stand))
                hit = GroveTileArt.Hit(PieceOf(catalog, stand), stand);

            _hits[key] = hit;
            return hit;
        }

        // --------------------------------------------------------------- editing
        /// <summary>
        /// How far above a stand's centre the edit bar floats, before zoom.
        ///
        /// Above the piece rather than over it: both controls act on the thing standing there,
        /// and a bar drawn across it would hide what the player is deciding about — the same
        /// reason the victory panel's route note is a bubble hanging below its row rather than
        /// a panel over it.
        /// </summary>
        const float BarLift = 176f;

        /// <summary>What a piece is drawn at while it is in the air.</summary>
        const float GhostAlpha = .78f;

        RectTransform _bar;
        Image _ghost;
        MarkSet _origin, _target;
        int _editCol, _editRow;
        bool _editing, _dragging;
        int _dropCol, _dropRow;
        GroveMovePlan _plan;

        string EditSlot => GroveFloor.TileId(_editCol, _editRow);

        /// <summary>
        /// Says that what was asked for does not fit — the one refusal on this screen that has
        /// to be said out loud, because a picker closing over an unchanged grove teaches that
        /// the control is broken. Public so the picker, which is a panel over this screen, can
        /// have the grove say it after the panel has gone.
        /// </summary>
        public void SayNoRoom() => Scenery.Toast(Content, Loc.Get("ui.grove.no_room"));

        /// <summary>
        /// A finger rested on a tile with something on it: offer the two things that can be
        /// done to it.
        ///
        /// <para>
        /// <b>Why editing is a long press and not a mode.</b> The grove deliberately has no
        /// edit toggle — a mode changes what every other control on the screen does, on a
        /// screen whose whole vocabulary is "tap the thing you want to change". A long press is
        /// the one gesture that can say <em>this one, differently</em> without taking the
        /// screen over, and it leaves the tap free to go on meaning exactly what it meant.
        /// </para>
        /// <para>
        /// Nothing opens for the hall or for bare ground. The hall is derived from the best home
        /// the player owns rather than placed (invariant 16), so it can neither be picked up nor
        /// swapped into, and an empty tile already has a tap that does the useful thing. A tile
        /// a piece reaches over resolves to that piece, so holding the far end of a bridge
        /// picks the bridge up.
        /// </para>
        /// </summary>
        void Hold(int col, int row)
        {
            if (!StandAt(col, row, out var stand) || stand.IsHall) return;

            _editCol = stand.AnchorCol;
            _editRow = stand.AnchorRow;
            _editing = true;

            EnsureBar();
            EnsureMarks();
            _bar.gameObject.SetActive(true);
            PlaceBar();
            Tween.Pop(_bar, .6f, .26f);

            // The press has already happened by the time this fires, so the player gets no
            // feedback from the button they did not touch. This is the whole acknowledgement
            // that the hold worked, and without it a long press feels like a tap that failed.
            Audio.SfxVaried("tick", .5f);
        }

        void CloseEditor()
        {
            _editing = false;
            _dragging = false;

            if (_bar) _bar.gameObject.SetActive(false);
            if (_ghost) _ghost.gameObject.SetActive(false);
            _target?.Hide();
            _origin?.Hide();
        }

        void EnsureBar()
        {
            if (_bar != null) return;

            _bar = UIKit.Box("EditBar", Content, new Vector2(356f, 96f),
                             new Vector2(.5f, .5f), Vector2.zero);

            var move = UIKit.TextButton("Move", _bar, "btn_aqua", Loc.Get("ui.grove.move"), 30,
                                        new Vector2(168f, 92f), new Vector2(.5f, .5f),
                                        new Vector2(-90f, 0f), MoveHint);

            var handle = move.gameObject.AddComponent<DragHandle>();
            handle.Began = BeginMove;
            handle.Moved = DragMove;
            handle.Ended = EndMove;

            UIKit.TextButton("Flip", _bar, "btn_violet", Loc.Get("ui.grove.flip"), 30,
                             new Vector2(168f, 92f), new Vector2(.5f, .5f),
                             new Vector2(90f, 0f), FlipHere);
        }

        /// <summary>
        /// Tapping the move handle rather than dragging it. It is the likeliest first thing
        /// anybody does with it, and a control that answers a tap with nothing at all is a
        /// control the player concludes is broken.
        /// </summary>
        void MoveHint() => Scenery.Toast(Content, Loc.Get("ui.grove.move_hint"));

        /// <summary>The stand being edited, re-read each time because a repaint may have moved it.</summary>
        bool EditStand(out GroveStand stand)
            => HomesteadLayout.Occupancy(HomesteadCatalog.Current).TryAnchored(_editCol, _editRow, out stand)
            && !stand.IsHall;

        /// <summary>
        /// Keeps the bar over its stand as the floor is panned and zoomed under it, and takes it
        /// away when that stand leaves the window.
        ///
        /// <para>
        /// Followed every frame rather than placed once, because the bar is anchored to a tile
        /// and the tile moves for reasons the bar never hears about. Closing on the way out is
        /// deliberate: controls pointing at a piece the player can no longer see are controls
        /// that will be used on the wrong piece. Closed too when the stand is gone — a row
        /// merged in from another device can take it away under an open bar.
        /// </para>
        /// </summary>
        void PlaceBar()
        {
            if (_bar == null || _field == null || _viewport == null) return;

            if (!EditStand(out var stand)) { CloseEditor(); return; }

            var world = _field.TileWorld(stand.CentreCol, stand.CentreRow);

            if (!_viewport.rect.Contains(_viewport.InverseTransformPoint(world)))
            {
                CloseEditor();
                return;
            }

            _origin.Light(stand.AnchorCol, stand.AnchorRow, stand.Footprint);

            _bar.position = world;
            _bar.anchoredPosition += new Vector2(0f, BarLift * _field.Zoom);
        }

        void LateUpdate()
        {
            // After the field has applied this frame's pan and zoom, never before it.
            if (!_editing) return;

            // The origin keeps its light through a drag as well, so the piece in the air can
            // always be seen to have come from somewhere.
            if (_dragging)
            {
                if (EditStand(out var stand)) _origin.Light(stand.AnchorCol, stand.AnchorRow, stand.Footprint);
                if (_plan.Ok) _target.Light(_plan.AnchorCol, _plan.AnchorRow, _plan.Footprint);
            }
            else PlaceBar();
        }

        // ------------------------------------------------------------- move drag
        void BeginMove(PointerEventData e)
        {
            if (!_editing) return;

            var catalog = HomesteadCatalog.Current;
            if (!EditStand(out var stand)) { CloseEditor(); return; }

            var piece = catalog.Find(stand.PieceId);
            if (!piece.IsValid) { CloseEditor(); return; }

            EnsureGhost();

            ((RectTransform)_ghost.transform).sizeDelta =
                HomesteadArt.SizeOnFloor(piece, GroveTileArt.PieceScale) * _field.Zoom;

            // Painted through the shared path rather than from a still, because a resident is a
            // flipbook and has no single sprite — and an Image with no sprite is a white
            // rectangle, not a blank (invariant 7b). Paint leaves it transparent when the art
            // has not arrived, which is why the alpha is applied on top rather than assigned.
            HomesteadArt.Paint(_ghost, piece);
            _ghost.color = new Color(_ghost.color.r, _ghost.color.g, _ghost.color.b,
                                     _ghost.color.a * GhostAlpha);

            _ghost.transform.localScale = new Vector3(stand.Flipped ? -1f : 1f, 1f, 1f);

            _ghost.gameObject.SetActive(true);
            _bar.gameObject.SetActive(false);

            _dragging = true;
            _plan = default;

            DragMove(e);
        }

        /// <summary>
        /// The finger moving with a piece in the air: the ghost follows it, and the ground
        /// under it says what a drop would do.
        ///
        /// <para>
        /// The answer is <see cref="HomesteadLayout.PlanMove"/>'s, drawn as the footprint lit
        /// where the piece would land — green for room or a swap, red for a refusal — so the
        /// drop can do exactly what was shown. A piece two tiles long shows its two tiles,
        /// which is the first time the floor says out loud how much of it a thing takes.
        /// </para>
        /// </summary>
        void DragMove(PointerEventData e)
        {
            if (!_dragging) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    Content, e.position, e.pressEventCamera, out var local))
                ((RectTransform)_ghost.transform).anchoredPosition = local;

            EnsureMarks();

            if (!_field.TryTileAt(e.position, e.pressEventCamera, out _dropCol, out _dropRow))
            {
                _plan = default;
                _target.Hide();
                return;
            }

            _plan = HomesteadLayout.PlanMove(HomesteadCatalog.Current, EditSlot, _dropCol, _dropRow);

            if (_plan.Ok) _target.Light(_plan.AnchorCol, _plan.AnchorRow, _plan.Footprint, MarkSet.Room);
            else if (_plan.Result == GrovePlaceResult.NoRoom)
                _target.Light(_plan.AnchorCol, _plan.AnchorRow, _plan.Footprint, MarkSet.NoRoom);
            else _target.Hide();
        }

        void EndMove(PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;

            if (_ghost) _ghost.gameObject.SetActive(false);
            _target?.Hide();

            var result = _plan.Ok
                ? HomesteadLayout.Move(HomesteadCatalog.Current, EditSlot, _dropCol, _dropRow)
                : _plan.Result;

            if (result == GrovePlaceResult.Placed)
            {
                // Follow the piece. Somebody who has just moved something is far likelier to
                // move it again than to be finished with it, and reopening where it landed
                // makes the second adjustment cost a drag rather than another hold.
                _editCol = _plan.AnchorCol;
                _editRow = _plan.AnchorRow;

                Audio.SfxVaried("tick", .62f);
            }
            else if (result == GrovePlaceResult.NoRoom) SayNoRoom();

            _plan = default;

            if (!_editing) return;
            _bar.gameObject.SetActive(true);
            PlaceBar();
        }

        void FlipHere()
        {
            if (!_editing) return;

            if (HomesteadLayout.Flip(HomesteadCatalog.Current, EditSlot) == GrovePlaceResult.NoRoom)
                SayNoRoom();
        }

        void EnsureGhost()
        {
            if (_ghost != null) return;

            _ghost = UIKit.Img("Ghost", Content, null, Color.white, new Vector2(140f, 140f),
                               new Vector2(.5f, .5f), Vector2.zero);
            _ghost.preserveAspect = true;
            _ghost.raycastTarget = false;
            _ghost.gameObject.SetActive(false);
        }

        /// <summary>
        /// The two footprint lights: where the piece is, and where it would land.
        ///
        /// <para>
        /// Generated rather than addressed, for <c>Art.Bloom</c>'s reason — they appear under a
        /// moving finger, which is the worst moment on this screen for a sprite that has not
        /// arrived. They are parented to the screen rather than to the field so that they are
        /// drawn over every tile rather than sorted among them: a light under the piece it is
        /// naming would be hidden by exactly the sprite whose tile is in question.
        /// </para>
        /// </summary>
        void EnsureMarks()
        {
            _origin ??= new MarkSet(this, "Origin", Pal.A(Pal.Sun, .50f));
            _target ??= new MarkSet(this, "Drop", MarkSet.Room);
        }

        /// <summary>
        /// A footprint's worth of tile lights, one diamond per tile, pooled.
        ///
        /// A single diamond used to name a tile; a piece that stands on four needs four, and a
        /// light that showed one tile of a house would say the house was smaller than it is —
        /// which is the misunderstanding footprints exist to end. Kept on the screen rather than
        /// the field for the reason <see cref="EnsureMarks"/> gives, so each diamond is placed
        /// in world space every frame from <see cref="Light"/>.
        /// </summary>
        sealed class MarkSet
        {
            public static readonly Color Room = Pal.A(Pal.Mint, .58f);
            public static readonly Color NoRoom = new Color(1f, .36f, .30f, .55f);

            readonly HomesteadScreen _screen;
            readonly string _name;
            readonly List<Image> _marks = new List<Image>(4);
            Color _colour;

            public MarkSet(HomesteadScreen screen, string name, Color colour)
            {
                _screen = screen;
                _name = name;
                _colour = colour;
            }

            public void Light(int anchorCol, int anchorRow, GroveFootprint footprint)
                => Light(anchorCol, anchorRow, footprint, _colour);

            public void Light(int anchorCol, int anchorRow, GroveFootprint footprint, Color colour)
            {
                var field = _screen._field;
                if (field == null) return;

                _colour = colour;
                int wanted = footprint.TileCount;

                while (_marks.Count < wanted) _marks.Add(Make());

                int i = 0;
                for (int c = 0; c < footprint.Cols; c++)
                    for (int r = 0; r < footprint.Rows; r++)
                    {
                        var mark = _marks[i++];
                        mark.gameObject.SetActive(true);
                        mark.color = colour;
                        ((RectTransform)mark.transform).sizeDelta =
                            new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight) * field.Zoom;
                        mark.transform.position = field.TileWorld(anchorCol + c, anchorRow + r);
                    }

                for (; i < _marks.Count; i++) _marks[i].gameObject.SetActive(false);
            }

            public void Hide()
            {
                foreach (var mark in _marks) mark.gameObject.SetActive(false);
            }

            Image Make()
            {
                var mark = UIKit.Img(_name, _screen.Content, Art.IsoTile(128), _colour,
                                     new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight),
                                     new Vector2(.5f, .5f), Vector2.zero);
                mark.raycastTarget = false;
                mark.gameObject.SetActive(false);
                return mark;
            }
        }

        /// <summary>
        /// Turns a drag that begins on one control into three callbacks.
        ///
        /// <para>
        /// This is why moving is behind a handle rather than behind a drag on the piece itself.
        /// Unity routes a drag to the first ancestor of the pressed object that handles one, so
        /// a handle that takes the drag is also a handle the field never sees — and the floor
        /// does not pan out from under the thing being moved. A bare drag on a piece would be
        /// indistinguishable from a pan, on a screen that has to be panned.
        /// </para>
        /// </summary>
        sealed class DragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public Action<PointerEventData> Began, Moved, Ended;

            public void OnBeginDrag(PointerEventData e) => Began?.Invoke(e);
            public void OnDrag(PointerEventData e) => Moved?.Invoke(e);
            public void OnEndDrag(PointerEventData e) => Ended?.Invoke(e);
        }

        // ------------------------------------------------------------------ tile
        /// <summary>
        /// One tile: the ground, whatever is anchored on it, and the marks that say what it is.
        ///
        /// <para>
        /// Built once and rebound as the camera moves it across the field — see
        /// <see cref="GroveFieldView"/>. Everything that can differ between tiles is a field
        /// here rather than a fresh object, because the alternative is building and destroying a
        /// subtree per tile per pan, which is the cost that made a floor look impossible before
        /// culling existed.
        /// </para>
        /// <para>
        /// <b>Two nodes, one per layer.</b> The ground lives in the field's ground layer and
        /// the art in its piece layer, so no tile's skirt can paint over the base of a piece
        /// behind it — see the field for the report that bought this. A tile a footprint
        /// reaches over draws its ground and nothing else: the art is the anchor's.
        /// </para>
        /// </summary>
        sealed class TileCell : GroveFieldView.ITileCell
        {
            readonly HomesteadScreen _screen;
            readonly Image _ground, _art, _ring;

            /// <summary>
            /// Everything each node draws, held one level below it.
            ///
            /// <para>
            /// <b>The split exists so a tile can be moved without moving the tile.</b> The
            /// nodes' positions are the field's — written by <c>GroveFieldView</c> every time a
            /// cell is recycled onto new coordinates, and the pick box is derived from the same
            /// arithmetic. A rise animated on them would therefore be fighting the one transform
            /// that has to be authoritative, and a cell recycled mid-rise would drag its old
            /// destination onto its new tile. Animating a child means the offset is purely
            /// cosmetic and can be abandoned at any moment by writing two zeroes.
            /// </para>
            /// </summary>
            readonly RectTransform _body, _groundBody;

            bool _rising;
            int _riseCol, _riseRow;

            public RectTransform Ground { get; }

            public RectTransform Root { get; }

            public int Depth { get; private set; }

            public TileCell(HomesteadScreen screen)
            {
                _screen = screen;

                Ground = UIKit.Node("Tile", null);
                Ground.sizeDelta = new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight);

                _groundBody = UIKit.Node("B", Ground);

                _ground = UIKit.Img("G", _groundBody, null, Color.white,
                                    new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight),
                                    new Vector2(.5f, .5f), Vector2.zero);
                _ground.raycastTarget = false;
                _ground.preserveAspect = false;

                Root = UIKit.Node("Stand", null);
                Root.sizeDelta = new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight);

                _body = UIKit.Node("B", Root);

                // A ring rather than a fill, and only on tiles you can build on: an empty tile
                // has to look like an invitation rather than like a hole, and the whole floor is
                // empty on the first visit.
                _ring = UIKit.Img("R", _body, Art.Ring(96, 7f), Pal.A(Pal.Cream, .30f),
                                  new Vector2(EmptyMark, EmptyMark * .5f), new Vector2(.5f, .5f),
                                  Vector2.zero);
                _ring.raycastTarget = false;

                _art = UIKit.Img("A", _body, null, Color.white, new Vector2(140f, 140f),
                                 new Vector2(.5f, .5f), Vector2.zero);
                _art.preserveAspect = true;
                _art.raycastTarget = false;
            }

            public void Bind(int col, int row)
            {
                // A cell recycled onto another tile while it was still rising has to let go of
                // the rise, or the new tile inherits the old one's offset for the rest of it.
                if (_rising && (col != _riseCol || row != _riseRow)) EndRise();

                var catalog = HomesteadCatalog.Current;
                var floor = catalog.Floor;

                GroveTileArt.LayGround(_ground, floor);

                // What this tile shows is whatever is anchored on it — the best home the player
                // owns on the hall, whatever they placed, or the starter companion on the one
                // tile that has one and has never been touched (see HomesteadLayout.Shown). A
                // tile another stand reaches over shows the ground and nothing else.
                var index = HomesteadLayout.Occupancy(catalog);
                bool anchored = index.TryAnchored(col, row, out var stand);
                bool covered = !anchored && index.IsCovered(col, row);

                var piece = anchored ? PieceOf(catalog, stand) : default;
                bool drawn = anchored && piece.IsValid;

                Depth = anchored ? stand.Depth : GroveFootprint.Single.Depth(col, row);

                _art.gameObject.SetActive(drawn);
                if (drawn) GroveTileArt.LayPiece(_art, piece, stand);

                _ring.gameObject.SetActive(!anchored && !covered);
                if (_ring.gameObject.activeSelf)
                {
                    // Reset before restarting, and that is not tidiness. Tween.Breathe captures
                    // the transform's current scale as the value it oscillates about, and killing
                    // one leaves the transform wherever in the cycle it stopped — so a recycled
                    // cell would take a mid-breath scale as its new rest point and the rings
                    // would drift larger every time the player panned across them.
                    _ring.transform.localScale = Vector3.one;

                    // Phased off the tile's own coordinates so the field breathes as a field
                    // rather than pulsing in unison, which reads as a fault rather than as life.
                    Tween.Breathe(_ring.transform, .10f, 2.4f, (col * .37f + row * .61f) % 1f);
                }

                // Ground the player has just paid for arrives out of the floor rather than
                // switching on. Asked last, so the tile is fully drawn before it starts moving.
                if (_screen.TakeArrival(col, row)) BeginRise(col, row);
            }

            /// <summary>
            /// One tile of new ground travelling up into its place, overshooting a little as it
            /// lands. Both nodes travel together, so what stands on the tile rises with it.
            ///
            /// <para>
            /// On a channel so a second rise replaces the first rather than running beside it,
            /// and owned by the body so it dies with the cell. The overshoot is <c>OutBack</c>
            /// read unclamped — clamping it would flatten precisely the part of the motion that
            /// makes ground feel like it has weight.
            /// </para>
            /// </summary>
            void BeginRise(int col, int row)
            {
                _rising = true;
                _riseCol = col;
                _riseRow = row;

                Tween.Run(GroveGrowth.RiseSeconds, Ease.OutBack, t =>
                {
                    var at = new Vector2(0f, Mathf.LerpUnclamped(-GroveRise.Lift, 0f, t));
                    var scale = Vector3.one * Mathf.LerpUnclamped(GroveRise.RiseFrom, 1f, t);

                    if (_body) { _body.anchoredPosition = at; _body.localScale = scale; }
                    if (_groundBody) { _groundBody.anchoredPosition = at; _groundBody.localScale = scale; }
                }, _body, "rise").OnDone(EndRise);
            }

            void EndRise()
            {
                _rising = false;
                Tween.KillChannel(_body, "rise");

                if (_body) { _body.anchoredPosition = Vector2.zero; _body.localScale = Vector3.one; }
                if (_groundBody) { _groundBody.anchoredPosition = Vector2.zero; _groundBody.localScale = Vector3.one; }
            }
        }

        // ------------------------------------------------------------------- tap
        void Tap(int col, int row)
        {
            // A tap anywhere puts the editing controls away, and does nothing else. One tap to
            // dismiss is what every panel here does, and answering the dismissing tap with a
            // picker as well would be two responses to one gesture — the mistake the hub's "+"
            // buttons made before AdOfferOverlay became one destination.
            if (_editing) { CloseEditor(); return; }

            // No land branch: ground the player does not own is not drawn, so there is nothing
            // here to tap. Expanding is done in the shop, where the other things they buy are.

            // Whatever covers the tile is what was tapped: a tap on the far tile of a bridge is
            // a tap on the bridge, and the picker opens on its anchor so choosing "take it
            // away" takes the whole thing away.
            if (StandAt(col, row, out var stand))
            {
                // A home goes to the home panel in every state — the question at a house is
                // never "shall I buy this one item", it is "where am I on the ladder".
                if (stand.IsHall) { Flow.Modal<HomesteadHomeOverlay>(); return; }

                col = stand.AnchorCol;
                row = stand.AnchorRow;
            }

            Flow.Modal<HomesteadPickerOverlay>(v => v.Slot = new HomesteadSlot(col, row));
        }

        // ------------------------------------------------------------------ tips
        public override void OnPresented()
        {
            _presented = true;
            StartRise();
            Teach();
        }

        /// <summary>
        /// The two things a first visit has to be told: what this place is, and where the
        /// things it is built from come from.
        ///
        /// <para>
        /// <b>They are ordinary lessons, on the ordinary ledger.</b> A grove tip is a
        /// <c>Mechanic</c> like a crossing is — a permanent id, strings derived from it, and
        /// <c>TipLedger</c> recording that this player has met it. That is what makes them
        /// shown once in a lifetime rather than once per install: the ledger is a union-joined
        /// set in the save file, so a second device does not re-teach what the first one
        /// taught, and it cost no new field to say so. They are deliberately not in
        /// <c>Mechanic.TeachingOrder</c>, which is the board scan's queue — nothing about a
        /// glade implies the player has opened the Grovement.
        /// </para>
        /// <para>
        /// <b>Nothing is taught over an empty screen.</b> The catalog is a body read on
        /// entering the feature, so on a cold start it can land after the transition has
        /// finished — and a welcome tip spent while the grove behind it is still blank is
        /// spent for good. So this is attempted from both <see cref="OnPresented"/> and
        /// <see cref="Reload"/>, does nothing until there is a grove to point at, and does
        /// nothing twice.
        /// </para>
        /// </summary>
        void Teach()
        {
            if (_taught || _teaching || !_presented) return;
            if (!HomesteadCatalog.IsLoaded || HomesteadCatalog.Current.Floor.IsEmpty) return;

            // Ground arriving takes the screen, and a lesson raised over it would be a modal
            // in front of the thing the player just paid to watch. The ceremony calls this
            // itself when it hands the screen back, so nothing is lost by waiting — and a
            // first visit cannot be one of these anyway, since land costs credits.
            if (_pending != null || _rise != null) return;

            var queue = new List<Mechanic>(2);

            if (!TipLedger.HasSeen(Mechanic.Grove)) queue.Add(Mechanic.Grove);
            if (!TipLedger.HasSeen(Mechanic.GroveShop)) queue.Add(Mechanic.GroveShop);

            _taught = true;
            if (queue.Count == 0) return;

            _teaching = true;

            // A beat after the iris, so the first thing the player sees is their own grove
            // and the second is somebody explaining it.
            Tween.After(.45f, () => ShowTip(queue, 0), this);
        }

        /// <summary>
        /// Shows one tip and, when it is dismissed, the next.
        ///
        /// Chained on dismissal rather than raised together, which is <c>PlayScreen</c>'s rule
        /// and for its reason: two modals at once means meeting the second before reading the
        /// first. The editing controls are put away first because the second tip cuts a hole
        /// around the shop button, and a bar floating over the field inside that hole would be
        /// lit by a lesson that is not about it.
        /// </summary>
        void ShowTip(List<Mechanic> queue, int index)
        {
            if (!this) return;

            if (index >= queue.Count) { _teaching = false; return; }

            CloseEditor();

            var mechanic = queue[index];

            Flow.Modal<TipOverlay>(v =>
            {
                v.Mechanic = mechanic;

                // The welcome has nothing to ring: it is about the whole screen, and a hole
                // cut around one tile would say it is about that tile.
                v.Target = mechanic.Equals(Mechanic.GroveShop) ? _shop : null;

                v.Dismissed = () => Tween.After(.18f, () => ShowTip(queue, index + 1), this);
            });
        }
    }
}
