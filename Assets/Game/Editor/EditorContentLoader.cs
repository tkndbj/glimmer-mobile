using System.Collections.Generic;
using System.Threading;
using GlimmerGrove.Content;
using GlimmerGrove.Content.Sources;
using GlimmerGrove.Homestead;
using GlimmerGrove.Progression;

namespace GlimmerGrove.EditorTools
{
    /// <summary>Everything the Editor tools need: the index, every body, and the problems.</summary>
    public sealed class EditorContent
    {
        public readonly LevelCatalog Catalog;
        public readonly IReadOnlyList<string> Problems;

        /// <summary>
        /// The grove catalog, read eagerly here and lazily in the game.
        ///
        /// Never null — an unreadable or absent <c>homestead.json</c> gives
        /// <see cref="HomesteadCatalog.Empty"/>, which every tool reads as "no grove" and
        /// none of them crash on. Its own problems are folded into <see cref="Problems"/>,
        /// because a caller checking for content errors should not have to know the grove is
        /// a separate file.
        /// </summary>
        public readonly HomesteadCatalog Homestead;

        public EditorContent(LevelCatalog catalog, IReadOnlyList<string> problems,
                             HomesteadCatalog homestead)
        {
            Catalog = catalog;
            Problems = problems;
            Homestead = homestead ?? HomesteadCatalog.Empty;
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
            var source = new BundledContentSource();

            var repository = new LevelRepository(source);
            var result = repository.LoadEverythingAsync(CancellationToken.None).GetAwaiter().GetResult();

            var problems = new List<string>(result.Problems);

            // The roster, published exactly as GameContent.Publish does at boot — and it has to
            // happen before the grove is read, because the grove's residents are projected from
            // it. Without this the Editor would validate and pack the *built-in fallback* five
            // companions rather than the thirty-one the manifest ships, and every check would
            // pass while the shop's residents shelf came out nearly empty on the device.
            AvatarCatalog.Publish(result.Catalog.Index.Companions);

            // The grove, read here and only here in one go. The game loads it when the
            // Grovement is opened; validation has to see it whether or not anybody opens
            // anything, which is the same asymmetry chapter bodies have above.
            var grove = new HomesteadLoader(source).LoadAsync(CancellationToken.None)
                                                   .GetAwaiter().GetResult();

            foreach (var problem in grove.Problems) problems.Add(problem);

            return new EditorContent(result.Catalog, problems, grove.Catalog);
        }
    }
}
