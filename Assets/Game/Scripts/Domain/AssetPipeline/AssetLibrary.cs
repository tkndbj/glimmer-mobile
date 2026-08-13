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
    /// It caches by address and remembers which scope each address belongs to.
    /// Callers never say which — they ask for an address and the library knows,
    /// because the chapter's asset set was registered when that chapter was entered.
    /// That keeps every UI call site oblivious to memory management while still
    /// making it possible to drop a whole chapter's art in one call.
    /// </summary>
    public static class AssetLibrary
    {
        static IAssetProvider _provider = new ResourcesAssetProvider();

        static readonly Dictionary<string, UnityEngine.Object> _globalOne = new Dictionary<string, UnityEngine.Object>();
        static readonly Dictionary<string, UnityEngine.Object> _chapterOne = new Dictionary<string, UnityEngine.Object>();
        static readonly Dictionary<string, Sprite[]> _globalSets = new Dictionary<string, Sprite[]>();
        static readonly Dictionary<string, Sprite[]> _chapterSets = new Dictionary<string, Sprite[]>();

        /// <summary>Addresses owned by the resident chapter.</summary>
        static readonly HashSet<string> _chapterAddresses = new HashSet<string>(StringComparer.Ordinal);

        public static IAssetProvider Provider => _provider;

        public static ChapterId LoadedChapter { get; private set; } = ChapterId.None;

        /// <summary>
        /// Swaps the backing provider. Call at boot, before anything loads — assets
        /// already cached from the old provider are dropped rather than migrated.
        /// </summary>
        public static void UseProvider(IAssetProvider provider)
        {
            if (provider == null || provider == _provider) return;

            ReleaseChapter();
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

        // ------------------------------------------------------------- chapters
        /// <summary>
        /// Makes <paramref name="chapter"/> the resident one, loading its art and
        /// releasing the previous chapter's. Returns immediately when it is already
        /// resident, which is the common case of replaying a level.
        /// </summary>
        public static async Task EnsureChapterAsync(ChapterDefinition chapter, LevelCatalog catalog,
                                                    IProgress<float> progress = null,
                                                    CancellationToken cancellation = default)
        {
            if (chapter == null) { progress?.Report(1f); return; }
            if (LoadedChapter == chapter.Id) { progress?.Report(1f); return; }

            ReleaseChapter();

            var requests = AssetManifest.ChapterAssets(chapter, catalog);
            foreach (var request in requests) _chapterAddresses.Add(request.Address);

            LoadedChapter = chapter.Id;
            await PreloadAsync(requests, progress, cancellation);
        }

        /// <summary>Drops the resident chapter's art. Safe when none is loaded.</summary>
        public static void ReleaseChapter()
        {
            if (_chapterAddresses.Count == 0) { LoadedChapter = ChapterId.None; return; }

            _chapterOne.Clear();
            _chapterSets.Clear();
            _provider.Release(_chapterAddresses);
            _chapterAddresses.Clear();
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
            => _chapterAddresses.Contains(address) ? _chapterOne : _globalOne;

        static Dictionary<string, Sprite[]> SetCacheFor(string address)
            => _chapterAddresses.Contains(address) ? _chapterSets : _globalSets;

        /// <summary>Diagnostics for the profiler and the dev overlay.</summary>
        public static string Describe()
            => $"provider={_provider.Name} global={_globalOne.Count + _globalSets.Count} " +
               $"chapter={_chapterOne.Count + _chapterSets.Count} ({LoadedChapter})";
    }
}
