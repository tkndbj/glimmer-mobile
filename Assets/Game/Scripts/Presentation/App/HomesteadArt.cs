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
    /// Draws the grove: what a piece looks like, how big it is, and where it sits on a tile.
    ///
    /// <para>
    /// <b>It no longer owns the art's lifetime — <see cref="GroveArtLoader"/> and <see cref="AssetHold"/> do.</b> The two
    /// were one file and one type, which read as economy and was not: painting is a pure
    /// question about a piece and a sprite that is already in hand, while lifetime is a
    /// concurrent state machine over four scopes with its own queue, its own generations and
    /// its own cancellation. Nothing in this half needs any of that, and every reader of it was
    /// paying to scroll past it. They are still one seam to a screen — it asks
    /// <see cref="GroveArtLoader"/> for the art and this for the picture.
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
        /// <summary>
        /// A piece's still sprite at one facing. Null for an animated one, which has frames.
        ///
        /// <para>
        /// A piece with one facing is one sprite at its own address, which is what every piece
        /// in the grove was and what <c>AssetManifest</c> addresses. A piece with four is a
        /// <em>frame folder</em> indexed by facing — the same machinery an animated piece uses,
        /// for a different reason, which is why a piece may never be both (see
        /// <see cref="HomesteadPiece.Facings"/>).
        /// </para>
        /// </summary>
        public static Sprite Still(HomesteadPiece piece, int facing = 0)
        {
            if (!piece.IsValid || piece.Animated) return null;
            if (piece.Facings <= 1) return AssetLibrary.Peek<Sprite>(AssetManifest.ArtRoot + piece.Art);

            var frames = AssetLibrary.PeekFrames(AssetManifest.ArtRoot + piece.Art);
            if (frames == null || frames.Length == 0) return null;
            return frames[GroveFootprint.Quarter(facing) % frames.Length];
        }

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
        public static void Paint(Image target, HomesteadPiece piece, int facing = 0)
        {
            if (target == null) return;

            // **Ensure rather than Attach, and the reel is asked for before anything is
            // stopped.** A tile is rebound on every repaint of the grove, and a repaint is
            // raised by the ledger, the layout, the wallet and the art scope — so a sync
            // landing a few seconds after a placement raises three of them and every animated
            // piece on the floor snapped back to its first frame at once. Reported as the tiles
            // reloading while the player was building. See Flipbook.Ensure for the split.
            if (piece.IsValid && piece.Animated)
            {
                var frames = AssetLibrary.PeekFrames(AssetManifest.ArtRoot + piece.Art);
                if (frames != null && frames.Length > 0)
                {
                    target.color = Fade(target.color, 1f);
                    Flipbook.Ensure(target, frames, 12f);
                    return;
                }
            }

            // Nothing below this line animates, so whatever was running has to be stopped
            // first — every flipbook the image carries, not the first one found: two repaints
            // in one frame stacked two, GetComponent stopped one, and the other went on
            // painting a well's frames into a tile that had been re-sized for brambles. See
            // Flipbook.Detach for the report that found it.
            Flipbook.Detach(target);

            // Either there is no piece, or its frames have not arrived. Hidden rather than
            // white; the repaint puts it back.
            if (!piece.IsValid || piece.Animated)
            {
                target.sprite = null;
                target.color = Fade(target.color, 0f);
                return;
            }

            var sprite = Still(piece, facing);
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

            // The authored size first, and that is the fix for pieces drawing small: a box
            // taken from the catalog is the same box before the sprite arrives and after, so
            // there is no window in which a tile can be laid out around a placeholder and
            // then painted with the real picture. See HomesteadPiece.ArtWidth. Only a piece
            // with no authored size — a resident projected from the roster — is measured off
            // the sprite, and falls back to a square while it is in flight.
            if (piece.HasArtSize) return new Vector2(piece.ArtWidth, piece.ArtHeight) * k;

            var sprite = piece.Animated ? FirstFrame(piece) : Still(piece);
            if (sprite == null) return new Vector2(140f, 140f) * k;

            return new Vector2(sprite.rect.width, sprite.rect.height) * k;
        }

        static Sprite FirstFrame(HomesteadPiece piece)
        {
            var frames = AssetLibrary.PeekFrames(AssetManifest.ArtRoot + piece.Art);
            return frames != null && frames.Length > 0 ? frames[0] : null;
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
                ? AssetLibrary.AtlasSprite(BrowseAtlasFor(GroveShelves.Of(piece)), piece.Id)
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
        /// <summary>
        /// Names frame <c>i</c> of a piece, held once rather than converted at the call site.
        ///
        /// <para>
        /// A method group turned into a delegate inline allocates one every time, and this is
        /// read on every cell of a scrolling grid — which is precisely the cost the run cache
        /// below exists to remove, so paying it to reach the cache would be comic.
        /// </para>
        /// </summary>
        static readonly Func<string, int, string> ThumbFrameName = GroveThumbs.Frame;

        public static Sprite[] ThumbFrames(HomesteadPiece piece)
            => piece.IsValid
                ? AssetLibrary.AtlasRun(BrowseAtlasFor(GroveShelves.Of(piece)), piece.Id,
                                        ThumbFrameName, GroveThumbs.MaxFrames)
                : Array.Empty<Sprite>();

        /// <summary>
        /// A shelf's atlas address, worked out once per shelf.
        ///
        /// <para>
        /// <c>AssetManifest.BrowseAtlas</c> concatenates, and this is asked on every rebind of
        /// every cell of a grid built around recycling cells so that it would not allocate. A
        /// shelf's address cannot change while the game is running, so it is worth exactly one
        /// string each.
        /// </para>
        /// </summary>
        static readonly Dictionary<GroveShelf, string> _browseAtlases =
            new Dictionary<GroveShelf, string>();

        static string BrowseAtlasFor(GroveShelf shelf)
        {
            if (_browseAtlases.TryGetValue(shelf, out string known)) return known;

            string address = AssetManifest.BrowseAtlas(shelf);
            _browseAtlases[shelf] = address;
            return address;
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

            // A piece that moves in the grove moves here too, which is the only way a player
            // browsing the shop can know that it does. This reverses a rule this file used to
            // state outright — "nothing in a grid ever animates" — and the reason it was safe
            // to reverse is PhaseOf: what made a moving grid unreadable was everything on it
            // moving in step.
            var frames = ThumbFrames(piece);

            // Ensure rather than Attach, for Paint's reason: a shelf is rebound whenever the
            // ledger, the catalog or the wallet moves — which a purchase does immediately and
            // the sync it triggers does again a few seconds later — and restarting every reel
            // on the page is what "the shop refreshes when I buy something" was.
            if (frames.Length > 1)
            {
                target.color = Fade(target.color, 1f);
                Flipbook.Ensure(target, frames, 12f).Offset = PhaseOf(piece.Id, frames.Length);
                return;
            }

            // A browse cell is recycled, so it can arrive carrying a flipbook — or two — that
            // previous bindings left on it. Nothing below animates, so every one is stopped.
            Flipbook.Detach(target);

            var sprite = frames.Length == 1 ? frames[0] : null;
            target.sprite = sprite;
            target.color = Fade(target.color, sprite == null ? 0f : 1f);
        }
    }
}
