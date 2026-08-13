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
    /// Reads the manifest and hands back a catalog.
    ///
    /// It reads the manifest and nothing else. That is the whole point: the boot path
    /// touches one small file however large the game becomes, and chapter bodies are
    /// fetched by the catalog when a player actually walks into one. The previous
    /// design opened and parsed every chapter on every launch, which cost a frame per
    /// chapter on Android — where StreamingAssets can only be reached through
    /// UnityWebRequest — and grew forever.
    ///
    /// It knows nothing about where the bytes came from, which is what lets exactly the
    /// same code path serve the bundled build, the on-device cache and a future CDN.
    /// </summary>
    public sealed class LevelRepository
    {
        readonly IContentSource _source;

        public LevelRepository(IContentSource source) => _source = source;

        /// <summary>
        /// The boot path. Reads the manifest, builds the index, and returns a catalog
        /// that will fetch chapter bodies on demand.
        /// </summary>
        public async Task<ContentLoadResult> LoadAsync(CancellationToken cancellation = default)
        {
            var problems = new List<string>();

            var index = await ReadIndexAsync(problems, cancellation);
            if (index == null) return new ContentLoadResult(LevelCatalog.Empty, problems);

            var catalog = new LevelCatalog(index, new ChapterLoader(_source), new ChapterResidency());
            return new ContentLoadResult(catalog, problems);
        }

        /// <summary>
        /// Reads the manifest *and* every chapter body, for tooling that genuinely needs
        /// the whole game in hand — the Editor validators, the authoring reports and the
        /// tests. The game never calls this; that separation is what stops a convenience
        /// for the Editor turning back into a cost the player pays at launch.
        /// </summary>
        public async Task<ContentLoadResult> LoadEverythingAsync(CancellationToken cancellation = default)
        {
            var problems = new List<string>();

            var index = await ReadIndexAsync(problems, cancellation);
            if (index == null) return new ContentLoadResult(LevelCatalog.Empty, problems);

            var loader = new ChapterLoader(_source);
            var bodies = new List<ChapterBody>(index.ChapterCount);

            foreach (var chapter in index.Chapters)
            {
                if (cancellation.IsCancellationRequested) break;

                var result = await loader.LoadAsync(chapter.Id, cancellation);
                problems.AddRange(result.Problems);

                if (result.Body == null) continue;
                bodies.Add(result.Body);

                VerifyAgainstIndex(chapter, result.Body, problems);
            }

            return new ContentLoadResult(LevelCatalog.FromLoaded(index, bodies), problems);
        }

        async Task<CatalogIndex> ReadIndexAsync(List<string> problems, CancellationToken cancellation)
        {
            var fetch = await _source.FetchAsync(ContentPaths.Manifest, cancellation);
            if (!fetch.Success)
            {
                problems.Add($"could not read {ContentPaths.Manifest} ({fetch.Error})");
                return null;
            }

            var manifest = ContentMapper.ReadManifest(fetch.Text, out string manifestError);
            if (manifest == null)
            {
                problems.Add(manifestError);
                return null;
            }

            var builder = new CatalogIndexBuilder();
            foreach (var entry in manifest.chapters) builder.Add(entry, ContentConfig.AppVersion);

            var index = builder.Build();
            problems.AddRange(builder.Problems);
            return index;
        }

        /// <summary>
        /// The manifest says which levels a chapter has; the body says what they are.
        /// Any disagreement is an authoring bug that only tooling can see, because at
        /// runtime a level the index does not name is simply never asked for.
        /// </summary>
        static void VerifyAgainstIndex(ChapterIndexEntry entry, ChapterBody body, List<string> problems)
        {
            var listed = new HashSet<LevelId>();
            foreach (var id in entry.LevelIds) listed.Add(id);

            foreach (var id in entry.LevelIds)
                if (!body.Contains(id))
                    problems.Add($"manifest lists '{id}' in chapter '{entry.Id}', " +
                                 "but the chapter file does not contain it");

            foreach (var level in body.Levels)
                if (!listed.Contains(level.Id))
                    problems.Add($"chapter '{entry.Id}' contains level '{level.Id}', which the manifest " +
                                 "does not list; it will not appear in the game — run Content ▸ Sync Manifest");
        }
    }
}
