using System;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Declared by any screen that draws from the grove's asset scope.
    ///
    /// The marker <see cref="HomesteadArt.CloseUnlessWanted"/> reads, so a screen handing over
    /// to another one that needs the same art does not release it out from under a page that
    /// has already drawn — see that method for what happened when the rule was a check each
    /// screen had to remember. Adding a third grove screen means adding this and nothing else.
    /// </summary>
    public interface IDrawsGroveArt { }

    /// <summary>
    /// Draws the grove, and owns the lifetime of its art.
    ///
    /// <para>
    /// <see cref="CompanionArt"/>'s shape, for its reasons: a screen showing the whole
    /// catalog brackets itself with <see cref="OpenAsync"/> and <see cref="Close"/> so the
    /// art lives exactly as long as it is on screen. This one has the stronger case — a
    /// roster is bounded by how many friends a game wants, while a shop grows at every drop
    /// for the life of the product.
    /// </para>
    /// <para>
    /// <b>There are two kinds of drawing here and they are deliberately not the same.</b>
    /// <see cref="Paint"/> puts a piece on an island at full size out of the grove's own
    /// scope; <see cref="PaintThumb"/> puts it in a browse grid at thumbnail size out of a
    /// shelf's atlas. Keeping them apart is what lets the grove stay bounded by the player's
    /// own grove while the shop stays bounded by one shelf — and it is what makes a grid of
    /// forty cells one draw call instead of forty.
    /// </para>
    /// <para>
    /// The one thing to know before editing: <b>loading is asynchronous and a screen is
    /// built in the frame it is asked for</b>, so every caller repaints when the callback
    /// arrives. Without that the first paint is the only one, and an <c>Image</c> with no
    /// sprite is a solid white rectangle rather than a blank — invariant 7b, and the whole
    /// reason <c>Art.Bloom</c> and <c>Art.Dial</c> are generated.
    /// </para>
    /// </summary>
    public static class HomesteadArt
    {
        /// <summary>
        /// The floor's tile sprite: the content's own, or a generated diamond until there is one.
        ///
        /// <para>
        /// <b>Peeked, not loaded.</b> Every reader here draws before the scope has landed and
        /// repaints when it does, so a null is the ordinary state rather than a fault — see
        /// <see cref="AssetLibrary.Peek{T}"/> for why asking the loud way printed a screenful
        /// of warnings every time the Grovement was opened.
        /// </para>
        /// <para>
        /// It falls back rather than returning null, which is the one place in this file that
        /// does. A piece whose art has not arrived is hidden and repainted; the ground cannot
        /// be, because a floor of nothing is not a screen a player can be shown while they wait.
        /// </para>
        /// </summary>
        public static Sprite Tile(GroveFloor floor)
        {
            if (floor != null && !string.IsNullOrEmpty(floor.TileArt))
            {
                var art = AssetLibrary.Peek<Sprite>(AssetManifest.ArtRoot + floor.TileArt);
                if (art != null) return art;
            }

            return Art.IsoTile(256);
        }

        /// <summary>
        /// How big a floor tile draws, and how far to drop it so its <em>top face</em> lands on
        /// the tile's point.
        ///
        /// <para>
        /// <b>An isometric tile sprite is not its top face.</b> The shipped art is a block: a
        /// 418x209 grass surface with 78 pixels of side wall painted under it. Centring the
        /// image on the tile point would sit every tile 39 pixels too high and the grid would
        /// not line up with itself. The offset is <em>derived</em> rather than authored — the top
        /// face of an isometric tile is 2:1 by definition, so whatever is left below it is
        /// skirt — which means a re-cut tile with a deeper side wall needs no number changed.
        /// </para>
        /// <para>
        /// That is <c>UIKit.PillFaceLift</c>'s lesson for the fourth time: where the visual base
        /// of a painted shape sits inside its rectangle is a fact about the image, and centring
        /// instead of measuring is a mistake this project keeps making.
        /// </para>
        /// <para>
        /// The skirt is what makes the field read as solid ground rather than as floating
        /// lozenges, and it only works because the tiles are drawn back to front — see
        /// <c>GroveFieldView.Restack</c>. A tile's wall is covered by whatever stands in front
        /// of it.
        /// </para>
        /// </summary>
        /// <summary>
        /// How much wider than its grid step a tile is drawn. See <see cref="TileDraw"/>.
        /// </summary>
        public const float TileOverlap = 1.06f;

        public static Vector2 TileDraw(GroveFloor floor, out float drop)
        {
            var sprite = Tile(floor);

            float w = GroveFloor.TileWidth;
            float aspect = sprite == null || sprite.rect.width <= 0f
                ? .5f
                : sprite.rect.height / sprite.rect.width;

            // Drawn a little larger than the step it occupies. The tiles are rounded blocks, so
            // laid edge to edge they leave a small diamond of background showing wherever four
            // corners meet — the floor reads as scattered lozenges rather than as ground. A few
            // per cent of overlap closes it, and it is invisible because the field is drawn back
            // to front anyway (see GroveFieldView.Restack).
            float draw = w * TileOverlap;
            var size = new Vector2(draw, draw * aspect);

            // Everything below the top face is side wall, so the sprite hangs by half of it to
            // put its *surface* on the tile's point.
            drop = (size.y - GroveFloor.TileHeight * TileOverlap) * .5f;
            return size;
        }

        /// <summary>A piece's still sprite. Null for an animated one, which has frames instead.</summary>
        public static Sprite Still(HomesteadPiece piece)
            => !piece.IsValid || piece.Animated
                ? null
                : AssetLibrary.Peek<Sprite>(AssetManifest.ArtRoot + piece.Art);

        /// <summary>
        /// Whether <see cref="Paint"/> would draw anything right now — that is, whether this
        /// piece's full-size art is resident.
        ///
        /// <para>
        /// For a caller that can fall back to <see cref="PaintThumb"/>, which is a different
        /// scope and therefore available at different times: the shop holds shelf atlases and
        /// the grove holds the real thing, and the one screen reachable from both is the home
        /// panel. Asking here rather than at the call site is the point — "is it animated, and
        /// if so are its frames in" is exactly the pair of facts <see cref="Paint"/> already
        /// knows and a second copy of would get wrong the first time a decor piece was
        /// animated.
        /// </para>
        /// </summary>
        public static bool HasArt(HomesteadPiece piece)
        {
            if (!piece.IsValid) return false;

            if (!piece.Animated) return Still(piece) != null;

            var frames = AssetLibrary.PeekFrames(AssetManifest.ArtRoot + piece.Art);
            return frames != null && frames.Length > 0;
        }

        /// <summary>
        /// Puts a piece on an <see cref="Image"/> at full size, animating it when it has frames.
        ///
        /// <para>
        /// <b>For an island, not for a grid</b> — a browse cell wants <see cref="PaintThumb"/>.
        /// </para>
        /// <para>
        /// Every resident with a flipbook is animated and no decor is, which sounds like an
        /// argument for deciding this from the kind — it is not. A still resident and a
        /// flickering lantern are both obviously reasonable, and the catalog says which this
        /// is, so nothing here has to guess.
        /// </para>
        /// <para>
        /// The image is hidden rather than left white when its art has not arrived. That is
        /// the difference between a load and a glitch, and on this screen it would be forty
        /// white rectangles at once.
        /// </para>
        /// </summary>
        public static void Paint(Image target, HomesteadPiece piece)
        {
            if (target == null) return;

            // Deferred destruction means an old flipbook would spend a frame fighting the new
            // sprite for the same Image.
            var running = target.GetComponent<Flipbook>();
            if (running) { running.enabled = false; UnityEngine.Object.Destroy(running); }

            if (!piece.IsValid)
            {
                target.sprite = null;
                target.color = Fade(target.color, 0f);
                return;
            }

            if (piece.Animated)
            {
                var frames = AssetLibrary.PeekFrames(AssetManifest.ArtRoot + piece.Art);
                if (frames != null && frames.Length > 0)
                {
                    target.color = Fade(target.color, 1f);
                    Flipbook.Attach(target, piece.Art, 12f);
                    return;
                }

                // Frames not in yet. Hidden rather than white; the repaint puts it back.
                target.sprite = null;
                target.color = Fade(target.color, 0f);
                return;
            }

            var sprite = Still(piece);
            target.sprite = sprite;
            target.color = Fade(target.color, sprite == null ? 0f : 1f);
        }

        static Color Fade(Color c, float alpha) { c.a = alpha; return c; }

        /// <summary>
        /// The size a piece draws at, in the plot's own pixels.
        ///
        /// <para>
        /// Native art size times the piece's scale times the slot's, all measured against how
        /// wide the island is actually being drawn. Everything therefore lives in the plot's
        /// coordinate space, so an author who authors <c>scale: 1</c> gets the proportion the
        /// art pack was drawn at — which is right, because the plots and the decor were cut
        /// from one scene. A screen that sized pieces in screen pixels would need every number
        /// in the catalog re-tuned the first time a plot was drawn larger.
        /// </para>
        /// <para>
        /// Falls back to a square when the sprite has not arrived, so the layout does not jump
        /// when it does.
        /// </para>
        /// </summary>
        public static Vector2 SizeOf(HomesteadPiece piece, float plotScale, float slotScale)
        {
            float k = plotScale * slotScale * (piece.IsValid ? piece.Scale : 1f);

            var sprite = piece.Animated ? FirstFrame(piece) : Still(piece);
            if (sprite == null) return new Vector2(140f, 140f) * k;

            return new Vector2(sprite.rect.width, sprite.rect.height) * k;
        }

        /// <summary>
        /// The size a piece draws at when it is standing on a floor tile.
        ///
        /// <para>
        /// Native art size times the piece's own scale times one number for the whole field.
        /// The islands had a scale per slot as well, because they were fixed compositions where
        /// front and centre was drawn bigger than back and left; on a field every tile is the
        /// same distance from the eye, so the only honest scale is the one that makes a piece
        /// the right size against a tile, and the rest is a fact about the piece.
        /// </para>
        /// <para>
        /// Falls back to a square when the sprite has not arrived, so the layout does not jump
        /// when it does.
        /// </para>
        /// </summary>
        public static Vector2 SizeOnFloor(HomesteadPiece piece, float floorScale)
        {
            float k = floorScale * (piece.IsValid ? piece.Scale : 1f);

            var sprite = piece.Animated ? FirstFrame(piece) : Still(piece);
            if (sprite == null) return new Vector2(140f, 140f) * k;

            return new Vector2(sprite.rect.width, sprite.rect.height) * k;
        }

        static Sprite FirstFrame(HomesteadPiece piece)
        {
            var frames = AssetLibrary.PeekFrames(AssetManifest.ArtRoot + piece.Art);
            return frames != null && frames.Length > 0 ? frames[0] : null;
        }

        // ------------------------------------------------------------- lifetime
        /// <summary>
        /// Loads what the <em>grove screen</em> draws — the islands, the home ladder and the
        /// pieces the player has placed — then calls back so the screen can redraw.
        ///
        /// <para>
        /// <b>Not the whole catalog, and that is the point.</b> This used to load every piece
        /// that exists, which was affordable at forty and is not at four hundred: a screen
        /// that shows at most one piece per slot was paying for the entire shop. What it asks
        /// for now is a function of the player's own grove, so the cost stops growing when the
        /// catalog does. Browsing is a different question with a different answer — see
        /// <see cref="OpenShelfAsync"/>.
        /// </para>
        /// <para>
        /// The callback is the whole point of the shape — see the type's remarks. It also
        /// fires immediately when the scope is already warm, so a caller never needs to check.
        /// </para>
        /// </summary>
        public static void OpenAsync(Action onReady = null)
        {
            if (AssetLibrary.IsScopeLoaded(AssetLibrary.HomesteadScope))
            {
                onReady?.Invoke();
                return;
            }

            Load(onReady);
        }

        static async void Load(Action onReady)
        {
            try
            {
                await AssetLibrary.EnsureScopeAsync(
                    AssetLibrary.HomesteadScope,
                    AssetManifest.GroveAssets(HomesteadCatalog.Current, HomesteadLayout.PlacedIds()));

                onReady?.Invoke();
            }
            catch (Exception e)
            {
                // async void swallows exceptions; a grove that failed to load must not vanish
                // in silence.
                Debug.LogException(e);
            }
        }

        // --------------------------------------------------------------- visiting
        /// <summary>
        /// Loads a grove that is not the player's: the ground, the home ladder, and whatever
        /// is standing in <em>that</em> grove.
        ///
        /// <para>
        /// Into its own scope, for invariant 7b's reason — see
        /// <see cref="AssetLibrary.GroveVisitScope"/>. It is bounded by the visited grove
        /// rather than by the catalog, exactly as <see cref="OpenAsync"/> is, so walking a
        /// leaderboard costs one grove at a time however long the list is.
        /// </para>
        /// <para>
        /// Unlike <see cref="OpenAsync"/> this always loads: consecutive visits are different
        /// groves, so a "already loaded" check keyed on the scope would draw the second
        /// keeper's floor with the first keeper's furniture on it. <c>EnsureScopeAsync</c>
        /// replaces the scope, which is what makes that safe rather than merely wasteful.
        /// </para>
        /// </summary>
        public static async void OpenVisitAsync(IEnumerable<string> pieceIds, Action onReady = null)
        {
            try
            {
                await AssetLibrary.EnsureScopeAsync(
                    AssetLibrary.GroveVisitScope,
                    AssetManifest.GroveAssets(HomesteadCatalog.Current, pieceIds));

                onReady?.Invoke();
            }
            catch (Exception e)
            {
                // async void swallows exceptions; a visit that failed to load must not vanish
                // in silence.
                Debug.LogException(e);
            }
        }

        /// <summary>Drops a visited grove's art. Always safe: a scope nobody opened is a no-op.</summary>
        public static void CloseVisit() => AssetLibrary.ReleaseScope(AssetLibrary.GroveVisitScope);

        // -------------------------------------------------------------- browsing
        /// <summary>
        /// Loads one shelf of the shop: the tab row's emblems and that shelf's thumbnails.
        ///
        /// See <see cref="Browse"/> for why flicking through tabs is safe, and
        /// <c>AssetManifest.GroveShelfAssets</c> for why a browse screen loads no real art.
        /// </summary>
        public static void OpenShelfAsync(GroveShelf shelf, Action onReady = null)
            => Browse("shelf:" + GroveShelves.Key(shelf), AssetManifest.GroveShelfAssets(shelf), onReady);

        /// <summary>
        /// Loads the tab row's emblems, into a scope that outlives the shelf being shown.
        ///
        /// <para>
        /// Separate from <see cref="OpenShelfAsync"/> because the two have different lifetimes,
        /// and sharing one was a visible fault: <c>EnsureScopeAsync</c> releases before it
        /// loads, so every tab tap destroyed the atlas all eight tabs draw from and immediately
        /// asked for it back. See <c>AssetLibrary.HomesteadTabScope</c>.
        /// </para>
        /// <para>
        /// Released by <see cref="Close"/> along with everything else the grove holds, so the
        /// one door every screen already leaves through covers it and nothing new has to be
        /// remembered.
        /// </para>
        /// </summary>
        /// <remarks>
        /// The in-flight guard is not defensive padding: <c>IsScopeLoaded</c> goes true the
        /// instant a load <em>starts</em>, which is precisely the check <see cref="Browse"/>
        /// records as the home of the shop's old double-load. A second caller answered from it
        /// would be told the emblems were ready and would paint a row of blanks, and
        /// <c>EnsureScopeAsync</c> releases before it loads, so starting a second load would
        /// tear the atlas out from under the first caller's row.
        /// </remarks>
        static bool _tabsLoading;
        static Action _tabsWaiting;

        public static async void OpenTabsAsync(Action onReady = null)
        {
            if (!_tabsLoading && AssetLibrary.IsScopeLoaded(AssetLibrary.HomesteadTabScope))
            {
                onReady?.Invoke();
                return;
            }

            _tabsWaiting += onReady;
            if (_tabsLoading) return;

            _tabsLoading = true;

            try
            {
                await AssetLibrary.EnsureScopeAsync(AssetLibrary.HomesteadTabScope,
                                                    AssetManifest.GroveTabAssets());
            }
            catch (Exception e)
            {
                // async void swallows exceptions; a row of blank tabs must not happen silently.
                Debug.LogException(e);
            }
            finally
            {
                _tabsLoading = false;
            }

            var waiting = _tabsWaiting;
            _tabsWaiting = null;
            waiting?.Invoke();
        }

        /// <summary>
        /// Loads what the picker draws, which is every shelf.
        ///
        /// Every tile of the floor takes everything, so the picker lists everything the player
        /// holds — see <c>HomesteadPickerOverlay</c>. That is eight thumbnail atlases rather
        /// than two, and it is still bounded by the number of shelves rather than by the size
        /// of the catalog, which is the property that matters.
        /// </summary>
        public static void OpenPickerAsync(Action onReady = null)
            => Browse("pick", AssetManifest.GrovePickerAssets(), onReady);

        /// <summary>What the browse scope holds, what was last asked for, and who is waiting.</summary>
        static string _loaded, _wanted;
        static bool _loading;
        static Action _waiting;
        static IReadOnlyList<AssetRequest> _queued;

        /// <summary>
        /// Makes <paramref name="key"/> the contents of the browse scope, and calls back when
        /// it is genuinely there.
        ///
        /// <para>
        /// <b>This is where the shop's double-load lived.</b> The old version asked
        /// <c>IsScopeLoaded</c>, which goes true the instant a load <em>starts</em>, and
        /// recorded the kind before awaiting — so a second caller in the same frame was told
        /// "already loaded" while nothing was, and a first visit (where the catalog was still
        /// arriving) asked for an empty set, got no scope at all, and then asked again. Two
        /// loads, two callbacks, two full repaints, and a grid that animated itself in twice.
        /// </para>
        /// <para>
        /// Three rules fix it and each earns its keep. A request for what is <b>already
        /// loaded</b> calls back at once. A request for what is <b>already in flight</b> joins
        /// that load's callback list rather than starting a second — which matters because
        /// <c>EnsureScopeAsync</c> releases before it loads, so a second one would tear the art
        /// out from under a screen already drawing it. And a request for something <b>else</b>
        /// while a load is running is <em>queued</em>, replacing whatever was queued before: a
        /// player flicking through eight tabs performs two loads rather than eight, and the
        /// callbacks of the shelves they flicked past are dropped rather than repainting a
        /// screen that has moved on.
        /// </para>
        /// </summary>
        static void Browse(string key, IReadOnlyList<AssetRequest> requests, Action onReady)
        {
            if (!_loading && string.Equals(_loaded, key, StringComparison.Ordinal))
            {
                onReady?.Invoke();
                return;
            }

            if (string.Equals(_wanted, key, StringComparison.Ordinal))
            {
                _waiting += onReady;
                if (!_loading) Pump(requests);
                return;
            }

            _wanted = key;
            _waiting = onReady;

            if (_loading) _queued = requests;
            else Pump(requests);
        }

        static async void Pump(IReadOnlyList<AssetRequest> requests)
        {
            _loading = true;
            string key = _wanted;

            try
            {
                await AssetLibrary.EnsureScopeAsync(AssetLibrary.HomesteadShopScope, requests);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                _loading = false;
            }

            // Something newer was asked for while this was in flight. Its callers are on
            // _waiting; this load's are not, because they were dropped when _wanted moved.
            if (!string.Equals(_wanted, key, StringComparison.Ordinal))
            {
                var next = _queued;
                _queued = null;
                if (next != null) Pump(next);
                return;
            }

            _loaded = key;
            _queued = null;

            var ready = _waiting;
            _waiting = null;

            try { ready?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// A piece's browse thumbnail: a small sprite out of its shelf's atlas.
        ///
        /// <para>
        /// <b>Never the piece's real art.</b> A grid cell draws at about 170 points and the art
        /// behind it is cut at 512 for an island, so browsing through the real thing pays
        /// sixteen times the pixels it can show — and pays again in draw calls, because a
        /// texture each is a batch each. One atlas per shelf answers both at once: a grid is
        /// one texture, so it is one batch however many cells are on it.
        /// </para>
        /// <para>
        /// The atlas is chosen from the piece rather than passed in, because the picker draws
        /// two shelves side by side — the slot's decor and every resident — and a caller
        /// choosing the atlas is a caller that can choose the wrong one.
        /// </para>
        /// </summary>
        public static Sprite Thumb(HomesteadPiece piece)
            => piece.IsValid
                ? AssetLibrary.AtlasSprite(AssetManifest.BrowseAtlas(GroveShelves.Of(piece)), piece.Id)
                : null;

        /// <summary>
        /// Every thumbnail frame a piece has in its shelf's atlas, in order.
        ///
        /// <para>
        /// <b>Counted by asking rather than by being told.</b> The catalog says a piece is
        /// animated; it does not say how long the loop is, and a second number saying so would
        /// be a number for a re-generated flipbook to put out of step with the atlas. So this
        /// walks upwards until the atlas stops answering, bounded by
        /// <see cref="GroveThumbs.MaxFrames"/>.
        /// </para>
        /// <para>
        /// A still piece answers with one sprite, which is the same thing <see cref="Thumb"/>
        /// returns — frame zero is the bare id. So a caller needs no branch on whether the
        /// piece moves.
        /// </para>
        /// </summary>
        public static Sprite[] ThumbFrames(HomesteadPiece piece)
        {
            if (!piece.IsValid) return Array.Empty<Sprite>();

            string atlas = AssetManifest.BrowseAtlas(GroveShelves.Of(piece));
            var found = new List<Sprite>(piece.Animated ? 8 : 1);

            for (int i = 0; i < GroveThumbs.MaxFrames; i++)
            {
                var sprite = AssetLibrary.AtlasSprite(atlas, GroveThumbs.Frame(piece.Id, i));
                if (sprite == null) break;

                found.Add(sprite);
            }

            return found.ToArray();
        }

        /// <summary>
        /// Where in its loop a cell starts, so a grid of lit things does not beat as one.
        ///
        /// <para>
        /// <b>This is what makes an animated grid readable</b>, and it is the reason the rule
        /// against it could be lifted. Eight torches flickering in lockstep is a strobe; the
        /// same eight out of phase is a row of separate fires. Derived from the id rather than
        /// randomised, so two devices — and two visits to the same shelf — draw the same thing.
        /// </para>
        /// </summary>
        static float PhaseOf(string id, int frames)
        {
            if (frames <= 1 || string.IsNullOrEmpty(id)) return 0f;

            unchecked
            {
                // FNV-1a, for the reason the chest roll uses it: a stable, well-spread answer
                // from a short string, with no dependence on the runtime's string hashing.
                uint h = 2166136261u;
                foreach (char c in id) h = (h ^ c) * 16777619u;

                return h % (uint)frames;
            }
        }

        /// <summary>The emblem a shelf's tab wears, out of the tab row's own small atlas.</summary>
        public static Sprite ShelfMark(GroveShelf shelf)
            => AssetLibrary.AtlasSprite(AssetManifest.TabAtlas, GroveShelves.Key(shelf));

        /// <summary>
        /// Puts a piece's thumbnail on an <see cref="Image"/>, hiding it until the atlas is in.
        ///
        /// Hidden rather than left white for invariant 7b's reason, which a browse grid feels
        /// hardest: an <c>Image</c> with no sprite is a solid white rectangle, and this screen
        /// would show forty of them at once.
        /// </summary>
        public static void PaintThumb(Image target, HomesteadPiece piece)
        {
            if (target == null) return;

            // A browse cell is recycled, so it can arrive carrying a flipbook a previous
            // binding left on it. Destroyed rather than re-pointed, because Destroy lands at
            // the end of the frame and an old one would spend that frame fighting the new
            // sprite for the same Image.
            var running = target.GetComponent<Flipbook>();
            if (running) { running.enabled = false; UnityEngine.Object.Destroy(running); }

            // A piece that moves in the grove moves here too, which is the only way a player
            // browsing the shop can know that it does. This reverses a rule this file used to
            // state outright — "nothing in a grid ever animates" — and the reason it was safe
            // to reverse is PhaseOf: what made a moving grid unreadable was everything on it
            // moving in step.
            var frames = ThumbFrames(piece);

            if (frames.Length > 1)
            {
                target.color = Fade(target.color, 1f);
                Flipbook.Attach(target, frames, 12f).Offset = PhaseOf(piece.Id, frames.Length);
                return;
            }

            var sprite = frames.Length == 1 ? frames[0] : null;
            target.sprite = sprite;
            target.color = Fade(target.color, sprite == null ? 0f : 1f);
        }

        /// <summary>
        /// Moves one piece's art into the grove's own scope, because it has just been placed.
        ///
        /// <para>
        /// Without this, a piece chosen in the picker would be drawn on the island from
        /// nowhere: the picker holds thumbnails, and an island wants the real thing. Adding
        /// rather than reloading is deliberate — rebuilding the grove scope to take on one
        /// sprite would tear down and re-fetch every other piece on the islands, which is a
        /// stall and a visible blink for nothing.
        /// </para>
        /// </summary>
        /// <summary>
        /// Raised when art the grove draws has arrived and something on screen may now be
        /// drawable that was not a moment ago.
        ///
        /// <para>
        /// <b>An event rather than a callback on <see cref="Claim"/>, and that is the whole
        /// fix.</b> Loading is asynchronous (invariant 7b), so a screen drawing a scope's art
        /// has to repaint when it lands — and the version of this that took an <c>onReady</c>
        /// argument put that obligation on whoever called it. The picker called it and did not
        /// repaint anything, because the picker is not what draws the grove: it placed the
        /// piece, the grove repainted on <c>HomesteadLayout.Changed</c> a frame later with the
        /// sprite still missing, and the tile stayed empty until some unrelated edit repainted
        /// it again. Reported from play as pieces appearing one operation late. A caller cannot
        /// forget to raise an event, and a screen that draws this art subscribes once.
        /// </para>
        /// </summary>
        public static event Action Changed;

        public static async void Claim(HomesteadPiece piece)
        {
            if (!piece.IsValid) return;

            try
            {
                await AssetLibrary.AddToScopeAsync(AssetLibrary.HomesteadScope,
                                                   AssetManifest.PieceAssets(piece));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }

            Raise();
        }

        /// <summary>
        /// Never lets one bad subscriber stop the others hearing about art that has landed —
        /// the guard <c>HomesteadLayout.Raise</c> uses, for its reason.
        /// </summary>
        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// Drops the grove's art. Residents survive — their frames are the board's critter
        /// sets, which are global, and an address that is global stays global.
        ///
        /// <b>A screen leaving should call <see cref="CloseUnlessWanted"/>, not this.</b>
        /// </summary>
        public static void Close()
        {
            _loaded = _wanted = null;
            _waiting = null;
            _queued = null;

            AssetLibrary.ReleaseScope(AssetLibrary.HomesteadScope);
            AssetLibrary.ReleaseScope(AssetLibrary.HomesteadShopScope);
            AssetLibrary.ReleaseScope(AssetLibrary.HomesteadTabScope);
        }

        /// <summary>
        /// Drops the grove's art unless the screen that just took over draws it too.
        ///
        /// <para>
        /// <b>Why a leaving screen may not simply <see cref="Close"/>.</b> <c>Destroy</c> lands
        /// at the end of the frame, so by the time an outgoing screen's <c>OnDestroy</c> runs,
        /// the incoming one has already been built <em>and painted</em> — with the sprites this
        /// call is about to release. Nothing repaints afterwards, so the shop drew forty empty
        /// plates for the whole visit and only came right when the player left and returned.
        /// That is one bug, not two: it is the same order of operations that makes
        /// <c>Flow.Current</c> readable here at all.
        /// </para>
        /// <para>
        /// The rule lives here rather than as a check in each screen's <c>OnDestroy</c> for the
        /// reason every "remember to do this" in this project has eventually earned: the shop
        /// carried the check and the grove screen did not, so the pair only worked in one
        /// direction. A screen declares that it draws grove art by implementing
        /// <see cref="IDrawsGroveArt"/>, and this reads the declaration.
        /// </para>
        /// </summary>
        public static void CloseUnlessWanted()
        {
            if (Flow.Current is IDrawsGroveArt) return;

            Close();
        }
    }
}
