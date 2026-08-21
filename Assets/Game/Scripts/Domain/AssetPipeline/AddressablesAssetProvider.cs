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
            // A cached handle that has gone invalid is dropped and loaded again rather than
            // answered with null. Returning null and *keeping* the dead entry made the
            // address permanently unloadable for the life of the process: every later call
            // took the same branch, so one release race turned into art that silently never
            // appeared again — invariant 7b's white rectangle, with nothing in the log.
            if (_handles.TryGetValue(address, out var existing))
            {
                if (existing.IsValid()) return existing.Result as T;
                _handles.Remove(address);
            }

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

        /// <summary>
        /// Loads one address, and survives the scope being released underneath it.
        ///
        /// <para>
        /// <b>A handle can die while this method is awaiting it, and that is ordinary rather
        /// than exceptional.</b> A scope's load is started when a screen opens and released
        /// when it closes (<c>AssetLibrary.ReleaseScope</c>), so opening a visited grove and
        /// going straight back — a tap and a tap — releases handles that are still in flight.
        /// Every member of a released <c>AsyncOperationHandle</c> throws, including
        /// <c>Status</c>, so reading it to decide whether the load worked threw
        /// "Attempting to use an invalid operation handle" out of the continuation, where
        /// nothing was catching it. It surfaced as an exception per unfinished address every
        /// time somebody left a grove quickly.
        /// </para>
        /// <para>
        /// The validity check therefore comes <em>first</em>, before the cancellation token
        /// and before the status. The token is not a substitute for it: a release and a
        /// cancellation are different events and only one of them trips the token.
        /// </para>
        /// </summary>
        public async Task<T> LoadAsync<T>(string address, CancellationToken cancellation) where T : Object
        {
            if (_handles.TryGetValue(address, out var existing))
            {
                if (!existing.IsValid()) _handles.Remove(address);          // see Load<T>
                else if (existing.IsDone) return existing.Result as T;
                else
                {
                    // Already in flight. Awaiting it is the point: reading `Result` off an
                    // unfinished handle yields null, so a second request for an address the
                    // first had not finished loading used to come back empty — which is the
                    // same white rectangle by a different route.
                    await existing.Task;
                    return existing.IsValid() && existing.Status == AsyncOperationStatus.Succeeded
                        ? existing.Result as T
                        : null;
                }
            }

            var handle = Addressables.LoadAssetAsync<T>(address);
            _handles[address] = handle;

            var result = await handle.Task;

            if (!handle.IsValid()) return null;
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
