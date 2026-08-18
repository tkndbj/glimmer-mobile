using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Content.Sources;
using UnityEngine;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Pulls newer content from the CDN into the on-device cache, in the background.
    ///
    /// Nothing here is on the boot path. The player starts playing immediately with
    /// whatever is already local, and a fortnightly content drop simply appears on
    /// the launch after it downloaded. That trade — one session of latency for zero
    /// risk of a slow or hostile network delaying the game — is what every shipped
    /// live puzzle game settles on.
    ///
    /// The manifest is written last and atomically. A refresh interrupted halfway
    /// therefore leaves the old manifest pointing at the old chapters, which are all
    /// still present; there is no state in which the cache describes files it lacks.
    /// </summary>
    public sealed class ContentRefresher
    {
        readonly RemoteContentSource _remote;
        readonly CacheContentSource _cache;
        readonly IContentSource _local;

        public ContentRefresher(RemoteContentSource remote, CacheContentSource cache, IContentSource local)
        {
            _remote = remote;
            _cache = cache;
            _local = local;
        }

        /// <summary>True when the cache changed and a restart would show new content.</summary>
        public async Task<bool> RefreshAsync(CancellationToken cancellation = default)
        {
            if (!_remote.IsConfigured) return false;

            var remoteFetch = await _remote.FetchAsync(ContentPaths.Manifest, cancellation);
            if (!remoteFetch.Success)
            {
                Debug.Log($"[Content] no refresh this session ({remoteFetch.Error})");
                return false;
            }

            var remoteManifest = ContentMapper.ReadManifest(remoteFetch.Text, out string error);
            if (remoteManifest == null)
            {
                Debug.LogWarning("[Content] remote manifest rejected: " + error);
                return false;
            }

            var localManifest = await ReadLocalManifestAsync(cancellation);
            var localVersions = ChapterVersions(localManifest);
            var wanted = new List<ManifestChapterDto>();

            int localProgressionVersion = localManifest?.progressionVersion ?? 0;
            bool wantProgression = remoteManifest.progressionVersion > localProgressionVersion;

            // The grove catalog, versioned in the manifest exactly as the reward table is —
            // a body with no chapter to belong to. The cache check is not redundant with the
            // version: a client that has never downloaded content has neither, and a version
            // comparison alone would call the missing file up to date.
            int localGroveVersion = localManifest?.groveVersion ?? 0;
            bool wantGrove = remoteManifest.groveVersion > localGroveVersion
                          || (remoteManifest.groveVersion > 0 && !_cache.Has(ContentPaths.Homestead));

            foreach (var entry in remoteManifest.chapters)
            {
                if (entry == null || entry.disabled) continue;
                if (!ChapterId.TryParse(entry.id, out var id, out _)) continue;
                if (entry.minAppVersion > 0 && ContentConfig.AppVersion < entry.minAppVersion) continue;

                bool have = localVersions.TryGetValue(id, out int localVersion);
                bool cached = _cache.Has(ContentPaths.Chapter(id));

                if (have && localVersion >= entry.version && cached) continue;
                wanted.Add(entry);
            }

            if (wanted.Count == 0 && !wantProgression && !wantGrove) return false;

            int written = 0;
            foreach (var entry in wanted)
            {
                if (cancellation.IsCancellationRequested) break;
                if (await FetchChapterAsync(entry, cancellation)) written++;
            }

            if (written != wanted.Count)
            {
                Debug.LogWarning($"[Content] refresh incomplete, {written} of {wanted.Count} chapters cached; " +
                                 "keeping the previous manifest");
                return false;
            }

            if (wantProgression && !await FetchProgressionAsync(cancellation))
            {
                Debug.LogWarning("[Content] progression table could not be refreshed; " +
                                 "keeping the previous manifest");
                return false;
            }

            if (wantGrove && !await FetchGroveAsync(cancellation))
            {
                Debug.LogWarning("[Content] grove catalog could not be refreshed; " +
                                 "keeping the previous manifest");
                return false;
            }

            // Only now is it safe to say the cache holds this manifest's world.
            bool ok = await _cache.WriteAsync(ContentPaths.Manifest, remoteFetch.Text, cancellation);
            if (ok) Debug.Log($"[Content] cached {written} updated chapter(s)" +
                              (wantProgression ? " and a new reward table" : "") +
                              (wantGrove ? " and a new grove catalog" : "") + ", live next launch");
            return ok;
        }

        /// <summary>
        /// Pulls a retuned reward table. Parsed before it is cached, exactly as a
        /// chapter is, so a malformed download can never reach the disk and be read
        /// back on a launch where nothing is around to notice.
        /// </summary>
        async Task<bool> FetchProgressionAsync(CancellationToken cancellation)
        {
            var fetch = await _remote.FetchAsync(ContentPaths.Progression, cancellation);
            if (!fetch.Success)
            {
                Debug.LogWarning("[Content] could not download the reward table: " + fetch.Error);
                return false;
            }

            var problems = new List<string>();
            if (!Progression.ProgressionTable.TryRead(fetch.Text, out _, problems))
            {
                Debug.LogWarning("[Content] downloaded reward table is malformed, discarding: " +
                                 string.Join("; ", problems));
                return false;
            }

            return await _cache.WriteAsync(ContentPaths.Progression, fetch.Text, cancellation);
        }

        /// <summary>
        /// Pulls a retuned grove catalog. Parsed before it is cached, exactly as a chapter and
        /// the reward table are, so a malformed download can never reach the disk and be read
        /// back on a launch where nothing is around to notice.
        /// </summary>
        async Task<bool> FetchGroveAsync(CancellationToken cancellation)
        {
            var fetch = await _remote.FetchAsync(ContentPaths.Homestead, cancellation);
            if (!fetch.Success)
            {
                Debug.LogWarning("[Content] could not download the grove catalog: " + fetch.Error);
                return false;
            }

            var problems = new List<string>();
            if (!Homestead.HomesteadMapper.TryRead(fetch.Text, problems, out _))
            {
                Debug.LogWarning("[Content] downloaded grove catalog is malformed, discarding: " +
                                 string.Join("; ", problems));
                return false;
            }

            return await _cache.WriteAsync(ContentPaths.Homestead, fetch.Text, cancellation);
        }

        async Task<bool> FetchChapterAsync(ManifestChapterDto entry, CancellationToken cancellation)
        {
            var id = ChapterId.Parse(entry.id);
            string path = ContentPaths.Chapter(id);

            var fetch = await _remote.FetchAsync(path, cancellation);
            if (!fetch.Success)
            {
                Debug.LogWarning($"[Content] could not download chapter '{id}': {fetch.Error}");
                return false;
            }

            // Parse before caching, so a corrupt download is never written to disk.
            var problems = new List<string>();
            if (!ContentMapper.TryReadChapter(fetch.Text, problems, out _))
            {
                Debug.LogWarning($"[Content] downloaded chapter '{id}' is malformed, discarding: " +
                                 string.Join("; ", problems));
                return false;
            }

            return await _cache.WriteAsync(path, fetch.Text, cancellation);
        }

        /// <summary>The manifest the device already has, cache shadowing bundled.</summary>
        async Task<ManifestDto> ReadLocalManifestAsync(CancellationToken cancellation)
        {
            var fetch = await _local.FetchAsync(ContentPaths.Manifest, cancellation);
            if (!fetch.Success) return null;

            return ContentMapper.ReadManifest(fetch.Text, out _);
        }

        static Dictionary<ChapterId, int> ChapterVersions(ManifestDto manifest)
        {
            var versions = new Dictionary<ChapterId, int>();
            if (manifest?.chapters == null) return versions;

            foreach (var entry in manifest.chapters)
            {
                if (entry == null) continue;
                if (!ChapterId.TryParse(entry.id, out var id, out _)) continue;
                versions[id] = entry.version;
            }
            return versions;
        }
    }
}
