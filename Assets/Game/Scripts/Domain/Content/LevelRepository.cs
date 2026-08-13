using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Content.Sources;

namespace GlimmerGrove.Content
{
    /// <summary>The catalog plus everything that went wrong assembling it.</summary>
    public sealed class ContentLoadResult
    {
        public readonly LevelCatalog Catalog;
        public readonly IReadOnlyList<string> Problems;

        public ContentLoadResult(LevelCatalog catalog, IReadOnlyList<string> problems)
        {
            Catalog = catalog;
            Problems = problems;
        }

        public bool HasProblems => Problems.Count > 0;
    }

    /// <summary>
    /// Reads a manifest and its chapters from a source, and assembles a catalog.
    ///
    /// It knows nothing about where the bytes came from, which is what lets exactly
    /// the same code path serve the bundled build, the on-device cache, an Editor
    /// validation pass and a future CDN.
    /// </summary>
    public sealed class LevelRepository
    {
        readonly IContentSource _source;

        public LevelRepository(IContentSource source) => _source = source;

        public async Task<ContentLoadResult> LoadAsync(CancellationToken cancellation = default)
        {
            var builder = new LevelCatalogBuilder();

            var manifestFetch = await _source.FetchAsync(ContentPaths.Manifest, cancellation);
            if (!manifestFetch.Success)
            {
                builder.Report($"could not read {ContentPaths.Manifest} ({manifestFetch.Error})");
                return new ContentLoadResult(LevelCatalog.Empty, builder.Problems);
            }

            var manifest = ContentMapper.ReadManifest(manifestFetch.Text, out string manifestError);
            if (manifest == null)
            {
                builder.Report(manifestError);
                return new ContentLoadResult(LevelCatalog.Empty, builder.Problems);
            }

            foreach (var entry in manifest.chapters)
            {
                if (cancellation.IsCancellationRequested) break;
                await LoadChapterAsync(entry, builder, cancellation);
            }

            return new ContentLoadResult(builder.Build(), builder.Problems);
        }

        async Task LoadChapterAsync(ManifestChapterDto entry, LevelCatalogBuilder builder,
                                    CancellationToken cancellation)
        {
            if (entry == null || entry.disabled) return;

            if (!ChapterId.TryParse(entry.id, out var chapterId, out string idError))
            {
                builder.Report($"manifest lists chapter '{entry.id}' which is rejected: {idError}");
                return;
            }

            // Content that needs newer client code is skipped, never half-loaded.
            if (entry.minAppVersion > 0 && ContentConfig.AppVersion < entry.minAppVersion)
                return;

            string path = ContentPaths.Chapter(chapterId);
            var fetch = await _source.FetchAsync(path, cancellation);
            if (!fetch.Success)
            {
                builder.Report($"chapter '{chapterId}' listed in the manifest but unreadable ({fetch.Error})");
                return;
            }

            if (!ContentMapper.TryReadChapter(fetch.Text, builder, entry.order, out var chapter, out var levels))
                return;

            if (chapter.Id != chapterId)
            {
                builder.Report($"chapter file at {path} calls itself '{chapter.Id}'");
                return;
            }

            builder.AddChapter(chapter, levels);
        }
    }
}
