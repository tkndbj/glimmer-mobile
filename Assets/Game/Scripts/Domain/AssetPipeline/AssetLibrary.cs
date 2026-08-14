using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Content;
using UnityEngine;

namespace GlimmerGrove.AssetPipeline
{
    /// <summary>
    /// The game's one way to get hold of an asset.
    ///
    /// It caches by address and remembers which <em>scope</em> each address belongs to.
    /// Callers never say which — they ask for an address and the library knows, because
    /// the scope's asset set was registered when that part of the game was entered.
    /// That keeps every UI call site oblivious to memory management while still making
    /// it possible to drop a whole screen's art in one call.
    ///
    /// <para>
    /// Scopes are <b>named, and there can be any number of them</b>. That generality is
    /// the point rather than speculative flexibility: chapters were the first thing that
    /// needed loading and dropping as a unit, companions are the second, and a shop or a
    /// seasonal event will be the third. When the only transient scope was hardcoded as
    /// "the chapter", the second one had nowhere to go but a parallel copy of the same
    /// four fields and the same release logic — which is exactly how two caches drift
    /// until one of them leaks.
    /// </para>
    /// <para>
    /// An address already held globally stays global. A scope may ask for something the
    /// boot preload already warmed, and it must not become that scope's property — the
    /// scope would free it on exit and the chrome would vanish from a screen that never
    /// asked for anything.
    /// </para>
    /// </summary>
    public static class AssetLibrary
    {
        /// <summary>Art owned by the chapter currently being played.</summary>
        public const string ChapterScope = "chapter";

        /// <summary>Companion portraits, held only while a screen is showing them.</summary>
        public const string CompanionScope = "companions";

        sealed class Scope
        {
            public readonly HashSet<string> Addresses = new HashSet<string>(StringComparer.Ordinal);
            public readonly Dictionary<string, UnityEngine.Object> One = new Dictionary<string, UnityEngine.Object>();
            public readonly Dictionary<string, Sprite[]> Sets = new Dictionary<string, Sprite[]>();
        }

        static IAssetProvider _provider = new ResourcesAssetProvider();

        static readonly Dictionary<string, UnityEngine.Object> _globalOne = new Dictionary<string, UnityEngine.Object>();
        static readonly Dictionary<string, Sprite[]> _globalSets = new Dictionary<string, Sprite[]>();

        static readonly Dictionary<string, Scope> _scopes = new Dictionary<string, Scope>(StringComparer.Ordinal);

        /// <summary>Address to the scope holding it. Absent means global.</summary>
        static readonly Dictionary<string, Scope> _owner = new Dictionary<string, Scope>(StringComparer.Ordinal);

        public static IAssetProvider Provider => _provider;

        public static ChapterId LoadedChapter { get; private set; } = ChapterId.None;

        /// <summary>
        /// Swaps the backing provider. Call at boot, before anything loads — assets
        /// already cached from the old provider are dropped rather than migrated.
        /// </summary>
        public static void UseProvider(IAssetProvider provider)
        {
            if (provider == null || provider == _provider) return;

            ReleaseAllScopes();
            _globalOne.Clear();
            _globalSets.Clear();
            _provider = provider;

            Debug.Log($"[Assets] provider is now '{provider.Name}'");
        }

        // ------------------------------------------------------------- fetching
        public static Sprite Sprite(string address) => Get<Sprite>(address);

        public static AudioClip Clip(string address) => Get<AudioClip>(address);

        public static Font Font(string address) => Get<Font>(address);

        public static T Get<T>(string address) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(address)) return null;

            var cache = OneCacheFor(address);
            if (cache.TryGetValue(address, out var cached)) return cached as T;

            var loaded = _provider.Load<T>(address);
            if (loaded == null) Debug.LogWarning($"[Assets] missing {address}");

            // Misses are cached too, so a bad address costs one failed load rather
            // than one per frame that asks for it.
            cache[address] = loaded;
            return loaded;
        }

        /// <summary>Animation frames under a folder-like address, sorted by name.</summary>
        public static Sprite[] Frames(string address)
        {
            if (string.IsNullOrEmpty(address)) return Array.Empty<Sprite>();

            var cache = SetCacheFor(address);
            if (cache.TryGetValue(address, out var cached)) return cached;

            var loaded = _provider.LoadAll<Sprite>(address);
            if (loaded == null || loaded.Length == 0)
            {
                Debug.LogWarning($"[Assets] missing frames {address}");
                loaded = Array.Empty<Sprite>();
            }

            cache[address] = loaded;
            return loaded;
        }

        // --------------------------------------------------------------- scopes
        /// <summary>
        /// Makes <paramref name="requests"/> the contents of a named scope, releasing
        /// whatever that scope held before. Loading the same set twice is not free —
        /// callers that can be re-entered should check <see cref="IsScopeLoaded"/>.
        /// </summary>
        public static async Task EnsureScopeAsync(string key,
                                                  IReadOnlyList<AssetRequest> requests,
                                                  IProgress<float> progress = null,
                                                  CancellationToken cancellation = default)
        {
            if (string.IsNullOrEmpty(key)) { progress?.Report(1f); return; }

            ReleaseScope(key);

            if (requests == null || requests.Count == 0) { progress?.Report(1f); return; }

            var scope = new Scope();
            _scopes[key] = scope;

            foreach (var request in requests)
            {
                // Already global: leave it there. Claiming it would mean freeing the
                // game's chrome the moment this scope closed. This is also what keeps
                // the worn companion's portrait alive on the hub after the profile —
                // which loaded the whole roster — is closed again.
                if (_globalOne.ContainsKey(request.Address) || _globalSets.ContainsKey(request.Address))
                    continue;

                // Owned by a different scope: leave it there too. Two scopes sharing an
                // address means whichever closed first would free it under the other.
                if (_owner.ContainsKey(request.Address)) continue;

                if (scope.Addresses.Add(request.Address)) _owner[request.Address] = scope;
            }

            await PreloadAsync(requests, progress, cancellation);
        }

        /// <summary>Drops a scope's assets. Safe when it was never loaded.</summary>
        public static void ReleaseScope(string key)
        {
            if (string.IsNullOrEmpty(key) || !_scopes.TryGetValue(key, out var scope)) return;

            _scopes.Remove(key);
            foreach (var address in scope.Addresses) _owner.Remove(address);

            scope.One.Clear();
            scope.Sets.Clear();
            _provider.Release(scope.Addresses);
            scope.Addresses.Clear();
        }

        public static bool IsScopeLoaded(string key)
            => !string.IsNullOrEmpty(key) && _scopes.ContainsKey(key);

        /// <summary>
        /// Promotes an address out of whatever scope owns it and into the global set,
        /// keeping whatever is already cached.
        ///
        /// This exists for art that a screen loaded but the game goes on showing after
        /// that screen closes. The concrete case is choosing a companion: the picker
        /// loaded every portrait into its own scope, and the one just chosen is now
        /// wanted on the hub — without this, closing the picker would release the
        /// portrait the hub is about to draw, and the player's new companion would
        /// simply not appear.
        ///
        /// Cheap and safe to call for an address that is already global, or one nothing
        /// has loaded yet; the caller warms it afterwards either way.
        /// </summary>
        public static void Pin(string address)
        {
            if (string.IsNullOrEmpty(address)) return;
            if (!_owner.TryGetValue(address, out var scope)) return;

            if (scope.One.TryGetValue(address, out var one))
            {
                _globalOne[address] = one;
                scope.One.Remove(address);
            }

            if (scope.Sets.TryGetValue(address, out var set))
            {
                _globalSets[address] = set;
                scope.Sets.Remove(address);
            }

            // Dropped from the scope's address list as well, so releasing that scope no
            // longer tells the provider to free it.
            scope.Addresses.Remove(address);
            _owner.Remove(address);
        }

        public static void ReleaseAllScopes()
        {
            var keys = new List<string>(_scopes.Keys);
            foreach (var key in keys) ReleaseScope(key);
            LoadedChapter = ChapterId.None;
        }

        // ------------------------------------------------------------- chapters
        /// <summary>
        /// Makes <paramref name="chapter"/> the resident one, loading its art and
        /// releasing the previous chapter's. Returns immediately when it is already
        /// resident, which is the common case of replaying a level.
        /// </summary>
        public static async Task EnsureChapterAsync(ChapterBody chapter,
                                                    IProgress<float> progress = null,
                                                    CancellationToken cancellation = default)
        {
            if (chapter == null) { progress?.Report(1f); return; }
            if (LoadedChapter == chapter.Id) { progress?.Report(1f); return; }

            LoadedChapter = chapter.Id;
            await EnsureScopeAsync(ChapterScope, AssetManifest.ChapterAssets(chapter), progress, cancellation);
        }

        /// <summary>Drops the resident chapter's art. Safe when none is loaded.</summary>
        public static void ReleaseChapter()
        {
            ReleaseScope(ChapterScope);
            LoadedChapter = ChapterId.None;
        }

        // ------------------------------------------------------------ preloading
        /// <summary>
        /// Warms a batch of assets, reporting 0..1 as it goes. Work is done in small
        /// batches so the frame drawing the progress bar still gets to run.
        /// </summary>
        public static async Task PreloadAsync(IReadOnlyList<AssetRequest> requests,
                                              IProgress<float> progress = null,
                                              CancellationToken cancellation = default,
                                              int batchSize = 8)
        {
            if (requests == null || requests.Count == 0) { progress?.Report(1f); return; }

            for (int i = 0; i < requests.Count; i += batchSize)
            {
                if (cancellation.IsCancellationRequested) return;

                int end = Mathf.Min(i + batchSize, requests.Count);
                var batch = new List<Task>(end - i);

                for (int k = i; k < end; k++)
                {
                    var request = requests[k];
                    if (AlreadyCached(request)) continue;
                    batch.Add(WarmAsync(request, cancellation));
                }

                if (batch.Count > 0) await Task.WhenAll(batch);
                progress?.Report(end / (float)requests.Count);
            }

            progress?.Report(1f);
        }

        static bool AlreadyCached(AssetRequest request)
            => request.Kind == AssetKind.SpriteSet
                ? SetCacheFor(request.Address).ContainsKey(request.Address)
                : OneCacheFor(request.Address).ContainsKey(request.Address);

        /// <summary>
        /// Loads one request into the right cache under its real type. Sprite sets go
        /// through the synchronous path because loading a whole folder has no
        /// streaming equivalent in either provider.
        /// </summary>
        static async Task WarmAsync(AssetRequest request, CancellationToken cancellation)
        {
            switch (request.Kind)
            {
                case AssetKind.SpriteSet:
                    Frames(request.Address);
                    return;

                case AssetKind.AudioClip:
                    await WarmOneAsync<AudioClip>(request.Address, cancellation);
                    return;

                case AssetKind.Font:
                    await WarmOneAsync<Font>(request.Address, cancellation);
                    return;

                default:
                    await WarmOneAsync<Sprite>(request.Address, cancellation);
                    return;
            }
        }

        static async Task WarmOneAsync<T>(string address, CancellationToken cancellation)
            where T : UnityEngine.Object
        {
            var loaded = await _provider.LoadAsync<T>(address, cancellation);
            if (cancellation.IsCancellationRequested) return;

            if (loaded == null) Debug.LogWarning($"[Assets] missing {address}");
            OneCacheFor(address)[address] = loaded;
        }

        // ------------------------------------------------------------- internals
        static Dictionary<string, UnityEngine.Object> OneCacheFor(string address)
            => _owner.TryGetValue(address, out var scope) ? scope.One : _globalOne;

        static Dictionary<string, Sprite[]> SetCacheFor(string address)
            => _owner.TryGetValue(address, out var scope) ? scope.Sets : _globalSets;

        /// <summary>Diagnostics for the profiler and the dev overlay.</summary>
        public static string Describe()
        {
            int scoped = 0;
            foreach (var scope in _scopes.Values) scoped += scope.One.Count + scope.Sets.Count;

            return $"provider={_provider.Name} global={_globalOne.Count + _globalSets.Count} " +
                   $"scoped={scoped} in {_scopes.Count} scope(s) (chapter={LoadedChapter})";
        }
    }
}
