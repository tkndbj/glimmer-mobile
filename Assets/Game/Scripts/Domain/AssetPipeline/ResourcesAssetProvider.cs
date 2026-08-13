using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GlimmerGrove.AssetPipeline
{
    /// <summary>
    /// Loads from <c>Resources/</c>. The fallback, and what ships until Addressables
    /// is installed.
    ///
    /// Everything here works, but understand its two hard limits, because they are
    /// the reason the interface above it exists:
    ///
    ///  - Every asset under Resources is force-included in the build and indexed at
    ///    startup, whether the player ever reaches it or not. This is why the splash
    ///    needs a loading bar.
    ///  - Individual assets cannot be released. <see cref="Release"/> can only drop
    ///    this provider's own references and ask Unity to sweep unreferenced objects.
    ///
    /// Neither can be fixed inside this class; they are properties of Resources.
    /// </summary>
    public sealed class ResourcesAssetProvider : IAssetProvider
    {
        public string Name => "resources";

        /// <summary>Resources has an async API, but the assets are already in the build.</summary>
        public bool IsAsynchronous => true;

        public T Load<T>(string address) where T : Object
            => UnityEngine.Resources.Load<T>(address);

        public T[] LoadAll<T>(string address) where T : Object
        {
            var all = UnityEngine.Resources.LoadAll<T>(address);
            if (all != null && all.Length > 1)
                System.Array.Sort(all, (a, b) => string.CompareOrdinal(a.name, b.name));
            return all;
        }

        public Task<T> LoadAsync<T>(string address, CancellationToken cancellation) where T : Object
        {
            var tcs = new TaskCompletionSource<T>();
            var request = UnityEngine.Resources.LoadAsync<T>(address);

            request.completed += _ =>
            {
                if (cancellation.IsCancellationRequested) { tcs.TrySetResult(null); return; }
                tcs.TrySetResult(request.asset as T);
            };

            return tcs.Task;
        }

        /// <summary>
        /// Advisory only. Resources keeps its own reference to everything it has
        /// loaded, so the most this can do is ask Unity to collect assets nothing
        /// else is pointing at.
        /// </summary>
        public void Release(IEnumerable<string> addresses)
        {
            UnityEngine.Resources.UnloadUnusedAssets();
        }
    }
}
