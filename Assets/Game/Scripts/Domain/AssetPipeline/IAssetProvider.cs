using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GlimmerGrove.AssetPipeline
{
    /// <summary>
    /// Where loadable assets come from.
    ///
    /// One seam for every sprite, clip and font in the game. Before this existed,
    /// `Resources.Load` was called from a dozen files, which meant the delivery
    /// mechanism was welded to every call site — and `Resources/` in particular can
    /// never be patched or streamed, because Unity force-loads it into the build's
    /// serialised blob.
    ///
    /// Implementations are interchangeable: Resources today, Addressables once the
    /// package is installed, a stub in tests. Nothing above this interface changes.
    /// </summary>
    public interface IAssetProvider
    {
        string Name { get; }

        /// <summary>True when loads can genuinely stream rather than block.</summary>
        bool IsAsynchronous { get; }

        T Load<T>(string address) where T : Object;

        /// <summary>Every asset under a folder-like address, sorted by name.</summary>
        T[] LoadAll<T>(string address) where T : Object;

        Task<T> LoadAsync<T>(string address, CancellationToken cancellation) where T : Object;

        /// <summary>
        /// Every asset under a folder-like address, without blocking the frame.
        ///
        /// <para>
        /// <b>It exists because a preload that blocks is not a preload.</b> Frame folders are
        /// not a rarity here — a grove piece that can be turned draws its four facings out of
        /// one (<c>AssetManifest.AddPiece</c>), so sixty-eight of the eighty-six pieces in the
        /// catalog are sets, and every turret reel, every raider cast and every baked effect is
        /// one too. With only a synchronous <see cref="LoadAll{T}"/> to call,
        /// <c>AssetLibrary.PreloadAsync</c> warmed each of them with
        /// <c>WaitForCompletion</c> — so a screen's whole art set arrived inside a single
        /// frame, and the "asynchronous" load the batching was built around could not yield
        /// once. The Grovement opening on a decorated grove was one long stall, which is the
        /// shape it was reported as.
        /// </para>
        /// <para>
        /// Kept beside the synchronous one rather than replacing it: a draw-time call site that
        /// finds its frames missing genuinely cannot wait a frame (invariant 7b — an
        /// <c>Image</c> with no sprite is a white rectangle), so <see cref="LoadAll{T}"/> stays
        /// as the last resort it always was, and this is what everything that can wait uses.
        /// </para>
        /// </summary>
        Task<T[]> LoadAllAsync<T>(string address, CancellationToken cancellation) where T : Object;

        /// <summary>
        /// Releases assets the provider owns. Resources cannot release individual
        /// objects, so it treats this as advisory; Addressables genuinely frees them.
        /// </summary>
        void Release(IEnumerable<string> addresses);
    }
}
