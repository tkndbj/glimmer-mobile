using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace GlimmerGrove.Content.Sources
{
    /// <summary>
    /// Content shipped inside the app, under StreamingAssets.
    ///
    /// This is the floor the game can always stand on: it needs no network, no cache
    /// and no permissions, so a fresh install is playable offline the moment it
    /// finishes downloading. StreamingAssets rather than Resources on purpose —
    /// Resources is force-loaded into the build's serialised blob and can never be
    /// patched, while these stay ordinary files that a remote pack can shadow.
    /// </summary>
    public sealed class BundledContentSource : IContentSource
    {
        public string Name => "bundled";

        public Task<ContentFetchResult> FetchAsync(string relativePath, CancellationToken cancellation)
        {
            string full = Path.Combine(Application.streamingAssetsPath, relativePath);

            // Android keeps StreamingAssets compressed inside the APK, so it is only
            // reachable through UnityWebRequest. Everywhere else it is a real file.
            return full.Contains("://") || full.Contains(":///")
                ? UnityWebRequestText.FetchAsync(full, Name, cancellation)
                : Task.FromResult(ReadFile(full));
        }

        ContentFetchResult ReadFile(string fullPath)
        {
            try
            {
                if (!File.Exists(fullPath))
                    return ContentFetchResult.Fail("not bundled: " + fullPath, Name);

                return ContentFetchResult.Ok(File.ReadAllText(fullPath), Name);
            }
            catch (Exception e)
            {
                return ContentFetchResult.Fail(e.Message, Name);
            }
        }
    }
}
