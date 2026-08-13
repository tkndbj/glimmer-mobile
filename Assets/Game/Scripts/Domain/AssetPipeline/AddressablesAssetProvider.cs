// The Addressables backend. Inert until the package is installed AND
// GLIMMER_ADDRESSABLES is added to Scripting Define Symbols — see CONTENT.md.
//
// It is guarded rather than unconditional because a missing package is a compile
// error, not a graceful degradation, and the game must stay buildable either way.
// Nothing above IAssetProvider changes when this becomes the active provider.
#if GLIMMER_ADDRESSABLES

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace GlimmerGrove.AssetPipeline
{
    /// <summary>
    /// Loads through Addressables, which is what makes art streamable and patchable.
    ///
    /// The two things Resources cannot do and this can: assets stay out of the build's
    /// serialised blob until something asks for them, and <see cref="Release"/>
    /// genuinely frees memory rather than merely suggesting it. That is what lets a
    /// chapter's backdrops be dropped on leaving it.
    ///
    /// Addresses are identical to the old Resources paths ("Art/Ui/btn_green"), so
    /// marking the existing assets addressable with those names is a migration of the
    /// asset database rather than of any code.
    /// </summary>
    public sealed class AddressablesAssetProvider : IAssetProvider
    {
        readonly Dictionary<string, AsyncOperationHandle> _handles =
            new Dictionary<string, AsyncOperationHandle>();

        public string Name => "addressables";

        public bool IsAsynchronous => true;

        public T Load<T>(string address) where T : Object
        {
            if (_handles.TryGetValue(address, out var existing))
                return existing.IsValid() ? existing.Result as T : null;

            // Synchronous only where a call site genuinely cannot wait. Prefer
            // LoadAsync, and prefer preloading the chapter over either.
            var handle = Addressables.LoadAssetAsync<T>(address);
            var result = handle.WaitForCompletion();

            _handles[address] = handle;
            return result;
        }

        public T[] LoadAll<T>(string address) where T : Object
        {
            if (_handles.TryGetValue(address, out var existing) && existing.IsValid())
                return Sorted(existing.Result as IList<T>);

            // Folder-shaped addresses map to Addressables labels: mark a frame folder
            // with a label matching its old Resources path.
            var handle = Addressables.LoadAssetsAsync<T>(address, null);
            var list = handle.WaitForCompletion();

            _handles[address] = handle;
            return Sorted(list);
        }

        public async Task<T> LoadAsync<T>(string address, CancellationToken cancellation) where T : Object
        {
            if (_handles.TryGetValue(address, out var existing))
                return existing.IsValid() ? existing.Result as T : null;

            var handle = Addressables.LoadAssetAsync<T>(address);
            _handles[address] = handle;

            var result = await handle.Task;
            if (cancellation.IsCancellationRequested) return null;

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogWarning($"[Assets] addressables could not load {address}");
                return null;
            }
            return result;
        }

        public void Release(IEnumerable<string> addresses)
        {
            if (addresses == null) return;

            foreach (var address in addresses)
            {
                if (!_handles.TryGetValue(address, out var handle)) continue;

                if (handle.IsValid()) Addressables.Release(handle);
                _handles.Remove(address);
            }
        }

        static T[] Sorted<T>(IList<T> list) where T : Object
        {
            if (list == null) return System.Array.Empty<T>();

            var array = new T[list.Count];
            list.CopyTo(array, 0);
            if (array.Length > 1)
                System.Array.Sort(array, (a, b) => string.CompareOrdinal(a.name, b.name));
            return array;
        }
    }
}

#endif
