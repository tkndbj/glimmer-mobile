using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Content;
using GlimmerGrove.Content.Sources;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Homestead
{
    /// <summary>The grove catalog, plus anything that went wrong reading it.</summary>
    public sealed class HomesteadLoadResult
    {
        public readonly HomesteadCatalog Catalog;
        public readonly IReadOnlyList<string> Problems;

        public HomesteadLoadResult(HomesteadCatalog catalog, IReadOnlyList<string> problems)
        {
            Catalog = catalog;
            Problems = problems;
        }

        public bool Success => Catalog != null;
        public bool HasProblems => Problems.Count > 0;
    }

    /// <summary>
    /// Fetches the grove catalog and maps it. Knows nothing about caching or when.
    ///
    /// Kept apart from whoever calls it for <see cref="Content.ChapterLoader"/>'s reason:
    /// this is the I/O, the screen owns the policy about when to do it. That split is what
    /// lets the Editor validate the catalog with the same code the game loads it with.
    /// </summary>
    public sealed class HomesteadLoader
    {
        readonly IContentSource _source;

        public HomesteadLoader(IContentSource source) => _source = source;

        public async Task<HomesteadLoadResult> LoadAsync(CancellationToken cancellation = default)
        {
            var problems = new List<string>();

            var fetch = await _source.FetchAsync(ContentPaths.Homestead, cancellation);

            if (!fetch.Success)
            {
                // Not an error the player should see. A build that ships no grove catalog is a
                // build with the Grovement switched off, which is a legitimate state — the
                // screen says so and every other screen is unaffected.
                problems.Add($"grove catalog is unreadable ({fetch.Error})");
                return new HomesteadLoadResult(null, problems);
            }

            if (!HomesteadMapper.TryRead(fetch.Text, problems, out var catalog))
                return new HomesteadLoadResult(null, problems);

            // The residents are the companion roster, which arrives on the manifest rather than
            // in this file — so the catalog the mapper produces is the authored half and this
            // is where the two are joined. Composed here rather than inside the mapper so the
            // mapper stays a statement about one file, which is what lets the Editor validate
            // the body against a roster it chooses.
            return new HomesteadLoadResult(catalog.WithResidents(AvatarCatalog.All), problems);
        }
    }
}
