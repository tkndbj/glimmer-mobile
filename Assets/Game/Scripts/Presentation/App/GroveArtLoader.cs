using System;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Async;
using GlimmerGrove.Homestead;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Takes out holds on the art the grove draws. One method per thing a screen can be
    /// showing, and each hands back an <see cref="AssetHold"/> the caller disposes when it goes.
    ///
    /// <para>
    /// <b>This used to be four named scopes and about three hundred lines of machinery keeping
    /// them honest</b> — a gate per scope guarding "is it loaded or only starting", a generation
    /// counter so a superseded load could not announce itself, a queue so flicking through tabs
    /// did not load eight atlases, a <c>Changed</c> event so a screen could hear about art
    /// somebody else had claimed, and a marker interface read off <c>Flow.Current</c> so a
    /// leaving screen could decide whether to free anything. Every one of those was working
    /// around the same missing idea: a scope had no notion of how many screens wanted it. With
    /// reference counting they are all simply gone — see <see cref="AssetHold"/>.
    /// </para>
    /// <para>
    /// <b>What is left is the part that was always the real content: which addresses.</b> Each
    /// of these is bounded by what is on the screen rather than by how much content exists —
    /// a visit costs one grove however long the leaderboard is, a shelf costs one atlas however
    /// many pieces the catalog grows to. That is invariant 7b, and it is the only thing this
    /// file now has an opinion about.
    /// </para>
    /// <para>
    /// <b>Every caller repaints from the callback.</b> Loading is asynchronous and a screen is
    /// built in the frame it is asked for, so the first paint would otherwise be the only one —
    /// and an <c>Image</c> with no sprite is a solid white rectangle rather than a blank.
    /// </para>
    /// </summary>
    public static class GroveArtLoader
    {
        /// <summary>
        /// The player's own grove: the ground, the home ladder, and whatever they have placed.
        ///
        /// <para>
        /// <b>Bounded by the grove, not by the catalog.</b> This used to be every piece that
        /// exists, which was affordable at forty and is wrong at four hundred: a screen showing
        /// at most one piece per tile was paying for the entire shop.
        /// </para>
        /// <para>
        /// Re-run it — <see cref="AssetHold.LoadAsync"/> on the same hold — whenever the grove's
        /// contents change. It is a replacement rather than a reload, so placing one bench costs
        /// one bench and not the floor.
        /// </para>
        /// </summary>
        public static List<AssetRequest> Grove()
            => AssetManifest.GroveAssets(HomesteadCatalog.Current, HomesteadLayout.PlacedIds());

        /// <summary>
        /// Somebody else's grove: the ground, the home ladder, and whatever is standing in
        /// <em>that</em> one.
        ///
        /// <para>
        /// Its own hold rather than a share of the player's, so leaving a visit cannot free art
        /// the Grovement behind it is drawing — and consecutive visits are different groves, so
        /// the hold is reloaded rather than reused. The counting makes both safe without
        /// anybody having to notice.
        /// </para>
        /// </summary>
        public static List<AssetRequest> Visit(IEnumerable<string> pieceIds)
            => AssetManifest.GroveAssets(HomesteadCatalog.Current, pieceIds);

        /// <summary>
        /// The home ladder and the ground, for a panel that draws a dwelling at full size.
        ///
        /// The same set <see cref="Grove"/> starts from, without anything the player has placed
        /// — which is the whole of what a home panel can show.
        /// </summary>
        public static List<AssetRequest> Homes()
            => AssetManifest.GroveAssets(HomesteadCatalog.Current, null);

        /// <summary>One shelf of the shop: its thumbnail atlas, and nothing else.</summary>
        public static List<AssetRequest> Shelf(GroveShelf shelf)
            => AssetManifest.GroveShelfAssets(shelf);

        /// <summary>The tab row's emblems, packed as one small atlas.</summary>
        public static List<AssetRequest> Tabs() => AssetManifest.GroveTabAssets();

        /// <summary>
        /// Every shelf's thumbnails, because the picker lists everything the player holds.
        ///
        /// Still bounded by the number of shelves rather than by the size of the catalog, which
        /// is the property paging exists to hold.
        /// </summary>
        public static List<AssetRequest> Picker() => AssetManifest.GrovePickerAssets();

        /// <summary>One piece's full-size art, for something just chosen or just bought.</summary>
        public static List<AssetRequest> Piece(HomesteadPiece piece)
            => AssetManifest.PieceAssets(piece);

        /// <summary>
        /// Opens a hold, fills it, and calls back once the art is genuinely there.
        ///
        /// <para>
        /// <paramref name="host"/> is checked after the await rather than captured as a flag: a
        /// panel can be dismissed while its art is arriving, and a callback into a destroyed
        /// object is a null reference in whichever field it touches first.
        /// </para>
        /// </summary>
        public static AssetHold Open(string name, IReadOnlyList<AssetRequest> requests,
                                     MonoBehaviour host, Action onReady = null,
                                     IProgress<float> progress = null)
        {
            var hold = AssetLibrary.Hold(name);
            Fill(hold, requests, host, onReady, progress);
            return hold;
        }

        /// <summary>
        /// Refills a hold that is already open, and calls back when the new art has landed.
        ///
        /// For a screen whose set changes while it is up — the grove when a piece is placed, the
        /// shop when the shelf changes. What it already holds and still wants is not re-fetched.
        /// </summary>
        public static void Fill(AssetHold hold, IReadOnlyList<AssetRequest> requests,
                                MonoBehaviour host, Action onReady = null,
                                IProgress<float> progress = null)
        {
            if (hold == null) return;

            Fire.AndForget(
                async () =>
                {
                    await hold.LoadAsync(requests, progress);
                    if (host != null) onReady?.Invoke();
                },
                "GroveArtLoader." + hold.Name);
        }

        /// <summary>
        /// Adds to a hold without letting anything go, for a piece the player has just picked
        /// up but not yet put down.
        ///
        /// <para>
        /// The draft's ghost is drawn at full size from the grove's own art, and the piece was
        /// chosen out of a panel that holds thumbnails — so without this the ghost is invisible
        /// until the piece is placed and the whole set is recomputed. It used to be a static
        /// call that reached into a global scope and raised an event; it is now the screen
        /// adding to the hold it owns.
        /// </para>
        /// </summary>
        public static void Add(AssetHold hold, IReadOnlyList<AssetRequest> requests,
                               MonoBehaviour host, Action onReady = null)
        {
            if (hold == null) return;

            Fire.AndForget(
                async () =>
                {
                    await hold.AddAsync(requests);
                    if (host != null) onReady?.Invoke();
                },
                "GroveArtLoader.add");
        }
    }
}
