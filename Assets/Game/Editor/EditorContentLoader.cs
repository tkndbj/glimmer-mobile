using System.Collections.Generic;
using System.Threading;
using GlimmerGrove.Content;
using GlimmerGrove.Content.Sources;

namespace GlimmerGrove.EditorTools
{
    /// <summary>Everything the Editor tools need: the index, every body, and the problems.</summary>
    public sealed class EditorContent
    {
        public readonly LevelCatalog Catalog;
        public readonly IReadOnlyList<string> Problems;

        public EditorContent(LevelCatalog catalog, IReadOnlyList<string> problems)
        {
            Catalog = catalog;
            Problems = problems;
        }

        public CatalogIndex Index => Catalog.Index;

        /// <summary>Every chapter body, in index order.</summary>
        public IReadOnlyList<ChapterBody> Bodies
        {
            get
            {
                var list = new List<ChapterBody>();
                foreach (var body in Catalog.LoadedBodies()) list.Add(body);
                return list;
            }
        }

        /// <summary>Every level in the game, in play order. Editor only, and only here.</summary>
        public IEnumerable<LevelDefinition> AllLevels()
        {
            foreach (var chapter in Index.Chapters)
            {
                if (!Catalog.TryResidentChapter(chapter.Id, out var body)) continue;

                foreach (var level in body.InIndexOrder(chapter.LevelIds)) yield return level;
            }
        }
    }

    /// <summary>
    /// Loads the bundled content synchronously for Editor tooling.
    ///
    /// In the Editor, StreamingAssets is an ordinary folder, so the bundled source
    /// completes without ever yielding — blocking on it here is safe and keeps the
    /// validation tools plain synchronous methods. It deliberately reads only what
    /// ships in the build, never the device cache, because a build must be judged on
    /// its own content rather than on whatever a previous run downloaded.
    ///
    /// It also reads *every* chapter body, which the game never does. That asymmetry is
    /// the point: validation must see the whole catalog, and the player must not have to
    /// load it. Keeping the eager path in a file marked Editor is what stops the
    /// convenience leaking back into the boot path.
    /// </summary>
    public static class EditorContentLoader
    {
        public static EditorContent Load()
        {
            var repository = new LevelRepository(new BundledContentSource());
            var result = repository.LoadEverythingAsync(CancellationToken.None).GetAwaiter().GetResult();

            return new EditorContent(result.Catalog, result.Problems);
        }
    }
}
