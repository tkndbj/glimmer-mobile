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
    /// two rows exactly as ten islands did.
    /// </para>
    /// <para>
    /// <b>Two things a field needs that islands did not.</b> Depth has to be computed rather
    /// than authored, because what stands in front of what is now a consequence of where the
    /// player put things — see <c>GroveFloor.DrawOrder</c>. And the tiles have to be culled,
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

        public override bool OnBack() { Flow.Go<HomeScreen>(); return true; }

        const float HeaderHeight = 214f;

        /// <summary>Size of the ring marking a buildable tile with nothing on it.</summary>
        const float EmptyMark = 64f;

        /// <summary>
        /// Art pixels to floor pixels for a piece standing on a tile.
        ///
        /// One number for the whole field rather than a scale per slot, which is what the
        /// islands had. A slot's scale existed to compose a fixed picture — front and centre
        /// bigger than back and left — and on a field every tile is the same distance from the
        /// eye, so the only honest scale is the one that makes a piece the right size against
        /// a tile. What varies is the piece's own <c>Scale</c>, which is a fact about the thing
        /// rather than about where it stands.
        /// </summary>
        const float PieceScale = 1.15f;

        RectTransform _viewport;
        GroveFieldView _field;
        Text _summary;

        protected override void Build()
        {
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
            _viewport = UIKit.Node("Viewport", Content);
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
            _field.Footprint = Footprint;

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

            CloseEditor();

            var floor = HomesteadCatalog.Current.Floor;

            _field.SetFloor(floor);
            ShowOwned();
            _field.Rebuild();

            // Opened on the hall rather than on the field's origin, which is the corner of a
            // diamond and therefore the emptiest place on the screen.
            if (GroveFloor.TryParse(floor.HallTile, out int col, out int row))
                _field.CentreOn(col, row);
            else
                _field.CentreOn(floor.Cols / 2, floor.Rows / 2);

            Repaint();
        }

        /// <summary>
        /// Which ground exists. Unowned land is not drawn at all — see
        /// <c>GroveFieldView.SetVisible</c> for why a field of padlocks was the wrong screen.
        /// </summary>
        static bool Owned(int col, int row)
            => GroveLand.IsOwned(HomesteadCatalog.Current.Floor, col, row);

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
            _boxes.Clear();

            _field.Refresh();
            PaintSummary();
        }

        // ---------------------------------------------------------------- header
        void BuildHeader()
        {
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .06f, .09f, .82f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            frt.sizeDelta = new Vector2(0f, 268f);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);

            var banner = UIKit.Img("Banner", Content, Art.S("Ui/banner"), Color.white,
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
            UIKit.IconButton("Back", Content, Skins.Nav, "ic_left", new Vector2(112f, 112f),
                             new Vector2(0f, 1f), new Vector2(92f, -104f),
                             () => Flow.Go<HomeScreen>());

            _summary = UIKit.Shrinkable(
                UIKit.Titled("Summary", Content, string.Empty, 26,
                             new Color(1f, .96f, .88f, .72f), TextAnchor.MiddleCenter,
                             new Vector2(720f, 34f), new Vector2(.5f, 1f), new Vector2(0f, -172f), 3f, 0f), 18);

            // The shop is a screen of its own rather than a panel over this one, for
            // CompanionScreen's reason: what it lists is unbounded, and a grid that scrolls
            // inside a scrim is a worse place to browse than a page that owns the display.
            // Placed through UIKit.Corner because Box pivots at centre: passing the margin
            // straight in put half the button past the right edge of the screen.
            var shopSize = new Vector2(230f, 96f);
            var shopAnchor = new Vector2(1f, 1f);
            var shop = UIKit.TextButton("Shop", Content, "btn_orange", Loc.Get("ui.grove.shop"), 28,
                                        shopSize, shopAnchor,
                                        UIKit.Corner(shopSize, shopAnchor, 28f, 62f),
                                        () => Flow.Go<HomesteadShopScreen>());
            UIKit.Shrinkable(shop.Label, 18);
            UIKit.FitLabel(shop);
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

        // ------------------------------------------------------------------ tile
        /// <summary>
        /// One tile: the ground, whatever stands on it, and the marks that say what it is.
        ///
        /// <para>
        /// Built once and rebound as the camera moves it across the field — see
        /// <see cref="GroveFieldView"/>. Everything that can differ between tiles is a field
        /// here rather than a fresh object, because the alternative is building and destroying a
        /// subtree per tile per pan, which is the cost that made a floor look impossible before
        /// culling existed.
        /// </para>
        /// </summary>
        // ------------------------------------------------------------ what is drawn
        /// <summary>
        /// What a tile shows: whatever the player put there, the starter friend on the one tile
        /// that draws one, or the best home they own on the hall.
        ///
        /// <para>
        /// Held in one place because two things need the same answer and a disagreement between
        /// them is invisible: the cell that <em>paints</em> the tile, and the box that decides
        /// what a finger <em>hit</em>. If those drifted, the player would be picking pieces from
        /// somewhere other than where the picture puts them.
        /// </para>
        /// </summary>
        static HomesteadPiece PieceOn(HomesteadCatalog catalog, string id)
            => catalog.Floor.IsHall(id)
                ? HomesteadLedger.BestDwelling(catalog)
                : catalog.Find(HomesteadLayout.Shown(catalog, id));

        readonly Dictionary<long, Rect> _boxes = new Dictionary<long, Rect>();

        /// <summary>
        /// The box a tile's art covers, in field space — what <see cref="GrovePick"/> tests a
        /// tap against. A zero rect means nothing stands here.
        ///
        /// <para>
        /// Cached per tile because this is asked for every live tile on every tap <em>and</em>
        /// on every frame of a move drag, and the honest computation of it allocates a tile id
        /// string. Sixty tiles a frame under a moving thumb is exactly the continuous garbage
        /// the field's depth comparer is held as a field to avoid. The cache is cleared by
        /// <see cref="Repaint"/>, which is the one door every change to the picture comes
        /// through.
        /// </para>
        /// </summary>
        Rect Footprint(int col, int row)
        {
            long key = ((long)col << 32) | (uint)row;
            if (_boxes.TryGetValue(key, out var cached)) return cached;

            var catalog = HomesteadCatalog.Current;
            var piece = PieceOn(catalog, GroveFloor.TileId(col, row));

            var box = Rect.zero;
            if (piece.IsValid)
            {
                // The same size and the same lift the cell lays the art out with, so the box is
                // the sprite's own rectangle rather than an approximation of it.
                var size = HomesteadArt.SizeOnFloor(piece, PieceScale);
                box = new Rect(GroveFloor.TileX(col, row) - size.x * .5f,
                               -GroveFloor.TileY(col, row) + size.y * piece.Lift - size.y * .5f,
                               size.x, size.y);
            }

            _boxes[key] = box;
            return box;
        }

        // --------------------------------------------------------------- editing
        /// <summary>
        /// How far above a tile's own point the edit bar floats, before zoom.
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
        Image _ghost, _target, _origin;
        int _editCol, _editRow;
        bool _editing, _dragging, _dropOk;
        int _dropCol, _dropRow;

        string EditSlot => GroveFloor.TileId(_editCol, _editRow);

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
        /// swapped into, and an empty tile already has a tap that does the useful thing.
        /// </para>
        /// </summary>
        void Hold(int col, int row)
        {
            var catalog = HomesteadCatalog.Current;
            string id = GroveFloor.TileId(col, row);

            if (catalog.Floor.IsHall(id)) return;
            if (string.IsNullOrEmpty(HomesteadLayout.Shown(catalog, id))) return;

            _editCol = col;
            _editRow = row;
            _editing = true;

            EnsureBar();
            EnsureMarks();
            _bar.gameObject.SetActive(true);
            PlaceBar();
            Tween.Pop(_bar, .6f, .26f);

            // The press has already happened by the time this fires, so the player gets no
            // feedback from the button they did not touch. This is the whole acknowledgement
            // that the hold worked, and without it a long press feels like a tap that failed.
            Haptic.Tap();
            Audio.SfxVaried("tick", .5f);
        }

        void CloseEditor()
        {
            _editing = false;
            _dragging = false;

            if (_bar) _bar.gameObject.SetActive(false);
            if (_ghost) _ghost.gameObject.SetActive(false);
            if (_target) _target.gameObject.SetActive(false);
            if (_origin) _origin.gameObject.SetActive(false);
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

        /// <summary>
        /// Keeps the bar over its tile as the floor is panned and zoomed under it, and takes it
        /// away when that tile leaves the window.
        ///
        /// <para>
        /// Followed every frame rather than placed once, because the bar is anchored to a tile
        /// and the tile moves for reasons the bar never hears about. Closing on the way out is
        /// deliberate: controls pointing at a piece the player can no longer see are controls
        /// that will be used on the wrong piece.
        /// </para>
        /// </summary>
        void PlaceBar()
        {
            if (_bar == null || _field == null || _viewport == null) return;

            var world = _field.TileWorld(_editCol, _editRow);

            if (!_viewport.rect.Contains(_viewport.InverseTransformPoint(world)))
            {
                CloseEditor();
                return;
            }

            LightTile(_origin, _editCol, _editRow);

            _bar.position = world;
            _bar.anchoredPosition += new Vector2(0f, BarLift * _field.Zoom);
        }

        /// <summary>
        /// Lights the tile being edited, under everything standing on it.
        ///
        /// <para>
        /// <b>Found by looking at it.</b> The bar hangs above its tile, and a tile near the
        /// hall is behind a sprite several tiles tall — so the controls came out floating over
        /// the cottage with nothing at all to say they belonged to the fence behind it. On a
        /// screen whose whole point is that pieces overlap each other, a control anchored to
        /// something has to name what it is anchored to.
        /// </para>
        /// </summary>
        void LightTile(Image mark, int col, int row)
        {
            if (mark == null || _field == null) return;

            mark.gameObject.SetActive(true);
            ((RectTransform)mark.transform).sizeDelta =
                new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight) * _field.Zoom;
            mark.transform.position = _field.TileWorld(col, row);
        }

        void LateUpdate()
        {
            // After the field has applied this frame's pan and zoom, never before it.
            if (!_editing) return;

            // The origin keeps its light through a drag as well, so the piece in the air can
            // always be seen to have come from somewhere.
            if (_dragging) LightTile(_origin, _editCol, _editRow);
            else PlaceBar();
        }

        // ------------------------------------------------------------- move drag
        void BeginMove(PointerEventData e)
        {
            if (!_editing) return;

            var catalog = HomesteadCatalog.Current;
            var piece = catalog.Find(HomesteadLayout.Shown(catalog, EditSlot));
            if (!piece.IsValid) { CloseEditor(); return; }

            EnsureGhost();

            ((RectTransform)_ghost.transform).sizeDelta =
                HomesteadArt.SizeOnFloor(piece, PieceScale) * _field.Zoom;

            // Painted through the shared path rather than from a still, because a resident is a
            // flipbook and has no single sprite — and an Image with no sprite is a white
            // rectangle, not a blank (invariant 7b). Paint leaves it transparent when the art
            // has not arrived, which is why the alpha is applied on top rather than assigned.
            HomesteadArt.Paint(_ghost, piece);
            _ghost.color = new Color(_ghost.color.r, _ghost.color.g, _ghost.color.b,
                                     _ghost.color.a * GhostAlpha);

            _ghost.transform.localScale =
                new Vector3(HomesteadLayout.FlippedAt(EditSlot) ? -1f : 1f, 1f, 1f);

            _ghost.gameObject.SetActive(true);
            _bar.gameObject.SetActive(false);

            _dragging = true;
            _dropOk = false;

            DragMove(e);
        }

        void DragMove(PointerEventData e)
        {
            if (!_dragging) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    Content, e.position, e.pressEventCamera, out var local))
                ((RectTransform)_ghost.transform).anchoredPosition = local;

            _dropOk = _field.TryTileAt(e.position, e.pressEventCamera, out _dropCol, out _dropRow)
                      && (_dropCol != _editCol || _dropRow != _editRow)
                      && !HomesteadCatalog.Current.Floor.IsHall(GroveFloor.TileId(_dropCol, _dropRow));

            EnsureMarks();

            if (_dropOk) LightTile(_target, _dropCol, _dropRow);
            else _target.gameObject.SetActive(false);
        }

        void EndMove(PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;

            if (_ghost) _ghost.gameObject.SetActive(false);
            if (_target) _target.gameObject.SetActive(false);

            if (_dropOk && HomesteadLayout.Move(HomesteadCatalog.Current, EditSlot,
                                                GroveFloor.TileId(_dropCol, _dropRow)))
            {
                // Follow the piece. Somebody who has just moved something is far likelier to
                // move it again than to be finished with it, and reopening where it landed
                // makes the second adjustment cost a drag rather than another hold.
                _editCol = _dropCol;
                _editRow = _dropRow;

                Haptic.Tap();
                Audio.SfxVaried("tick", .62f);
            }

            if (!_editing) return;
            _bar.gameObject.SetActive(true);
            PlaceBar();
        }

        void FlipHere()
        {
            if (_editing && HomesteadLayout.Flip(HomesteadCatalog.Current, EditSlot)) Haptic.Tap();
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
        /// The two tile lights: where the piece is, and where it would land.
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
            _origin = _origin != null ? _origin : Mark("Origin", Pal.A(Pal.Sun, .50f));
            _target = _target != null ? _target : Mark("Drop", Pal.A(Pal.Mint, .58f));
        }

        Image Mark(string name, Color colour)
        {
            var mark = UIKit.Img(name, Content, Art.IsoTile(128), colour,
                                 new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight),
                                 new Vector2(.5f, .5f), Vector2.zero);
            mark.raycastTarget = false;
            mark.gameObject.SetActive(false);
            return mark;
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

        sealed class TileCell : GroveFieldView.ITileCell
        {
            readonly HomesteadScreen _screen;
            readonly Image _ground, _art, _ring;

            public RectTransform Root { get; }

            public TileCell(HomesteadScreen screen)
            {
                _screen = screen;

                Root = UIKit.Node("Tile", null);
                Root.sizeDelta = new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight);

                _ground = UIKit.Img("G", Root, null, Color.white,
                                    new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight),
                                    new Vector2(.5f, .5f), Vector2.zero);
                _ground.raycastTarget = false;
                _ground.preserveAspect = false;

                // A ring rather than a fill, and only on tiles you can build on: an empty tile
                // has to look like an invitation rather than like a hole, and the whole floor is
                // empty on the first visit.
                _ring = UIKit.Img("R", Root, Art.Ring(96, 7f), Pal.A(Pal.Cream, .30f),
                                  new Vector2(EmptyMark, EmptyMark * .5f), new Vector2(.5f, .5f),
                                  Vector2.zero);
                _ring.raycastTarget = false;

                _art = UIKit.Img("A", Root, null, Color.white, new Vector2(140f, 140f),
                                 new Vector2(.5f, .5f), Vector2.zero);
                _art.preserveAspect = true;
                _art.raycastTarget = false;
            }

            public void Bind(int col, int row)
            {
                var catalog = HomesteadCatalog.Current;
                var floor = catalog.Floor;
                string id = GroveFloor.TileId(col, row);

                bool hall = floor.IsHall(id);

                // The ground is a block, not a flat lozenge: its side wall is painted below the
                // top face, so the sprite hangs by half the skirt to put its *surface* on the
                // tile's point. Derived from the art — see HomesteadArt.TileDraw.
                var ground = (RectTransform)_ground.transform;
                ground.sizeDelta = HomesteadArt.TileDraw(floor, out float drop);
                ground.anchoredPosition = new Vector2(0f, -drop);

                _ground.sprite = HomesteadArt.Tile(floor);
                _ground.color = Color.white;

                // The hall is drawn from the best home the player owns rather than placed, so
                // its tile shows a dwelling and accepts nothing. Everything else shows whatever
                // is standing there — or the starter companion, on the one tile that has one
                // and has never been touched (see HomesteadLayout.Shown).
                var piece = PieceOn(catalog, id);

                bool empty = !piece.IsValid;

                _art.gameObject.SetActive(!empty);
                if (!empty)
                {
                    var size = HomesteadArt.SizeOnFloor(piece, PieceScale);
                    ((RectTransform)_art.transform).sizeDelta = size;
                    ((RectTransform)_art.transform).anchoredPosition = new Vector2(0f, size.y * piece.Lift);
                    HomesteadArt.Paint(_art, piece);

                    // Which way it faces, written on every bind rather than only when it is
                    // mirrored. Cells are pooled and rebound as the camera pans, so a scale left
                    // behind by a flipped fence would be inherited by whatever tile reused the
                    // object — the same recycling hazard the breathing ring resets for above.
                    _art.transform.localScale =
                        new Vector3(!hall && HomesteadLayout.FlippedAt(id) ? -1f : 1f, 1f, 1f);
                }

                _ring.gameObject.SetActive(empty && !hall);
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

            var catalog = HomesteadCatalog.Current;
            var floor = catalog.Floor;
            string id = GroveFloor.TileId(col, row);

            // No land branch: ground the player does not own is not drawn, so there is nothing
            // here to tap. Expanding is done in the shop, where the other things they buy are.

            // A home goes to the home panel in every state — the question at a house is never
            // "shall I buy this one item", it is "where am I on the ladder".
            if (floor.IsHall(id)) { Flow.Modal<HomesteadHomeOverlay>(); return; }

            Flow.Modal<HomesteadPickerOverlay>(v => v.Slot = new HomesteadSlot(col, row));
        }
    }
}
