using System;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Content.Sources;
using UnityEngine;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Wires the content system together and hands the game a catalog.
    ///
    /// One place decides the layering, so the rule is legible in a single screen:
    /// read cache first, fall back to what shipped, and never let the network delay
    /// the player. Everything else in the content namespace is deliberately ignorant
    /// of this arrangement.
    /// </summary>
    public static class ContentBootstrap
    {
        static CacheContentSource _cache;
        static IContentSource _local;
        static bool _refreshRunning;

        /// <summary>
        /// The resolved local source, cache over bundled. Localisation reads through
        /// the same chain so a downloaded language pack shadows the shipped one
        /// exactly the way a downloaded chapter does.
        /// </summary>
        public static IContentSource LocalSource => _local;

        /// <summary>Reads the best content already on the device. Never touches the network.</summary>
        public static async Task<ContentLoadResult> LoadAsync(CancellationToken cancellation = default)
        {
            _cache = new CacheContentSource();

            // Order is the policy: downloaded content shadows the bundled build.
            _local = new LayeredContentSource(_cache, new BundledContentSource());

            var result = await new LevelRepository(_local).LoadAsync(cancellation);

            foreach (var problem in result.Problems)
                Debug.LogWarning("[Content] " + problem);

            // Chapter bodies are read on entering a chapter, so their problems arrive
            // long after this method returns. This is the one place that decides such a
            // report is a log line rather than, say, a telemetry event.
            result.Catalog.ProblemReported += problem => Debug.LogWarning("[Content] " + problem);

            if (result.Catalog.IsEmpty)
            {
                // The cache may be the culprit; drop it so the next launch is clean.
                Debug.LogError("[Content] no levels loaded, clearing the content cache");
                _cache.Clear();
            }

            GameContent.Publish(result.Catalog);
            return result;
        }

        /// <summary>
        /// Starts a background pull of newer content. Fire and forget by design — the
        /// result lands in the cache and is picked up on the next launch, so nothing
        /// in the running session has to wait on it or handle its failure.
        /// </summary>
        public static void BeginBackgroundRefresh(CancellationToken cancellation = default)
        {
            if (_refreshRunning || _cache == null) return;
            if (!ContentConfig.RemoteAvailable) return;
            if (Application.internetReachability == NetworkReachability.NotReachable) return;

            _refreshRunning = true;

            var remote = new RemoteContentSource(ContentConfig.RemoteBaseUrl, ContentConfig.NetworkTimeoutSeconds);
            var refresher = new ContentRefresher(remote, _cache, _local);

            _ = RunRefreshAsync(refresher, cancellation);
        }

        static async Task RunRefreshAsync(ContentRefresher refresher, CancellationToken cancellation)
        {
            try
            {
                await refresher.RefreshAsync(cancellation);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Content] background refresh failed: " + e.Message);
            }
            finally
            {
                _refreshRunning = false;
            }
        }
    }
}
