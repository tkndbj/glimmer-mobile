using System;
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
        /// An island's sprite, or null while the scope is still loading.
        ///
        /// <b>Peeked, not loaded.</b> Every reader here draws before the scope has landed and
        /// repaints when it does, so a null is the ordinary state rather than a fault — see
        /// <see cref="AssetLibrary.Peek{T}"/> for why asking the loud way printed a screenful
        /// of warnings every time the Grovement was opened.
        /// </summary>
        public static Sprite Plot(HomesteadPlot plot)
            => plot == null || string.IsNullOrEmpty(plot.Art)
                ? null
                : AssetLibrary.Peek<Sprite>(AssetManifest.ArtRoot + plot.Art);

        /// <summary>A piece's still sprite. Null for an animated one, which has frames instead.</summary>
        public static Sprite Still(HomesteadPiece piece)
            => !piece.IsValid || piece.Animated
                ? null
                : AssetLibrary.Peek<Sprite>(AssetManifest.ArtRoot + piece.Art);

        /// <summary>
        /// Puts a piece on an <see cref="Image"/>, animating it when it has frames.
        ///
        /// <para>
        /// Every resident is a flipbook and no decor is, which sounds like an argument for
        /// deciding this from the kind — it is not. A still resident and a flickering lantern
        /// are both obviously reasonable, and the catalog says which this is, so nothing here
        /// has to guess.
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
        /// catalog does. Browsing is a different question with a different scope — see
        /// <see cref="OpenKindAsync"/>.
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

        /// <summary>
        /// Loads one kind of piece — a shop tab, or the picker's list for one slot.
        ///
        /// Replaces whatever kind was loaded before, which is what bounds browsing to the
        /// largest single kind rather than the whole catalog. The grove's own art is in a
        /// different scope and is untouched by this, so switching tabs behind an open grove
        /// cannot pull a placed piece out from under it.
        /// </summary>
        public static void OpenKindAsync(HomesteadSlotKind kind, Action onReady = null)
        {
            // Already showing this kind: call back and do nothing else. Three callers reach
            // here for the same kind in one frame — the shop's Build, its Warm, and a buy
            // panel opening over it — and EnsureScopeAsync releases before it loads, so
            // without this the art would be torn down and re-fetched under a screen already
            // drawing it. The same shape OpenAsync has always had, for the same reason.
            if (_kind == kind && AssetLibrary.IsScopeLoaded(AssetLibrary.HomesteadShopScope))
            {
                onReady?.Invoke();
                return;
            }

            LoadKind(kind, onReady);
        }

        /// <summary>Which kind the shop scope currently holds. See <see cref="OpenKindAsync"/>.</summary>
        static HomesteadSlotKind? _kind;

        static async void LoadKind(HomesteadSlotKind kind, Action onReady)
        {
            try
            {
                _kind = kind;

                await AssetLibrary.EnsureScopeAsync(
                    AssetLibrary.HomesteadShopScope,
                    AssetManifest.GroveKindAssets(HomesteadCatalog.Current, kind));

                onReady?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Moves one piece's art into the grove's own scope, because it has just been placed.
        ///
        /// <para>
        /// Without this, a piece chosen in the picker is drawn from the <em>picker's</em>
        /// scope, and the next tab switch or the walk back to the hub would free art the grove
        /// is now showing. Adding rather than reloading is deliberate: rebuilding the grove
        /// scope to take on one sprite would tear down and re-fetch every other piece on the
        /// islands, which is a stall and a visible blink for nothing.
        /// </para>
        /// </summary>
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
            }
        }

        /// <summary>
        /// Drops the grove's art. Residents survive — their frames are the board's critter
        /// sets, which are global, and an address that is global stays global.
        ///
        /// <b>A screen leaving should call <see cref="CloseUnlessWanted"/>, not this.</b>
        /// </summary>
        public static void Close()
        {
            _kind = null;
            AssetLibrary.ReleaseScope(AssetLibrary.HomesteadScope);
            AssetLibrary.ReleaseScope(AssetLibrary.HomesteadShopScope);
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
