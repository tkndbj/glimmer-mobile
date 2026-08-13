using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GlimmerGrove.Content.Sources
{
    /// <summary>
    /// Content downloaded on a previous run, held in the app's private storage.
    ///
    /// This is what makes remote delivery invisible to the player: the game never
    /// waits on the network to start, it reads whatever the last refresh left here
    /// and pulls the next update in the background. Writes go through a temporary
    /// file so a process killed mid-write leaves the old copy intact rather than a
    /// truncated one — a half-written chapter would be worse than a stale chapter.
    /// </summary>
    public sealed class CacheContentSource : IWritableContentSource
    {
        public const string FolderName = "content_cache";

        readonly string _root;

        public CacheContentSource(string root = null)
            => _root = root ?? Path.Combine(Application.persistentDataPath, FolderName);

        public string Name => "cache";
        public string Root => _root;

        public bool Has(string relativePath)
            => TryResolve(relativePath, out string full) && File.Exists(full);

        public Task<ContentFetchResult> FetchAsync(string relativePath, CancellationToken cancellation)
        {
            if (!TryResolve(relativePath, out string full))
                return Task.FromResult(ContentFetchResult.Fail("illegal path: " + relativePath, Name));

            try
            {
                if (!File.Exists(full))
                    return Task.FromResult(ContentFetchResult.Fail("not cached: " + relativePath, Name));

                return Task.FromResult(ContentFetchResult.Ok(File.ReadAllText(full), Name));
            }
            catch (Exception e)
            {
                return Task.FromResult(ContentFetchResult.Fail(e.Message, Name));
            }
        }

        public Task<bool> WriteAsync(string relativePath, string text, CancellationToken cancellation)
        {
            if (!TryResolve(relativePath, out string full)) return Task.FromResult(false);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full));

                string temp = full + ".tmp";
                File.WriteAllText(temp, text);

                if (File.Exists(full)) File.Delete(full);
                File.Move(temp, full);

                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Content] could not cache {relativePath}: {e.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>Drops the whole cache, so the next launch falls back to bundled content.</summary>
        public void Clear()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Content] could not clear the cache: {e.Message}");
            }
        }

        /// <summary>
        /// Maps a relative content path onto disk, refusing anything that could climb
        /// out of the cache folder. Paths can originate from a downloaded manifest, so
        /// they are treated as untrusted input.
        /// </summary>
        bool TryResolve(string relativePath, out string full)
        {
            full = null;
            if (string.IsNullOrWhiteSpace(relativePath)) return false;
            if (relativePath.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            if (Path.IsPathRooted(relativePath)) return false;

            string combined = Path.GetFullPath(Path.Combine(_root, relativePath));
            string rootFull = Path.GetFullPath(_root);

            if (!combined.StartsWith(rootFull, StringComparison.Ordinal)) return false;

            full = combined;
            return true;
        }
    }
}
