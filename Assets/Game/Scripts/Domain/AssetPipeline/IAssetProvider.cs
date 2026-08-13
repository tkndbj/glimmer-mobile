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
        /// Releases assets the provider owns. Resources cannot release individual
        /// objects, so it treats this as advisory; Addressables genuinely frees them.
        /// </summary>
        void Release(IEnumerable<string> addresses);
    }
}
