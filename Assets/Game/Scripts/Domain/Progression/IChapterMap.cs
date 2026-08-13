using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// Answers "which chapter does this glade belong to, and do we know it at all?"
    ///
    /// The second half is the part that matters. Deriving currency has to count only
    /// glades that genuinely exist, or a save listing ten thousand invented level ids
    /// would mint currency out of nothing. The server enforces exactly this rule
    /// against its own copy of the catalog; this interface exists so the client can
    /// enforce the same one, and so both can be run against the same test vectors
    /// without either needing a real catalog to be built.
    /// </summary>
    public interface IChapterMap
    {
        bool TryGetChapter(LevelId level, out ChapterId chapter);
    }

    // The live implementation is CatalogIndex itself. There is no adapter between the
    // two on purpose: the index already *is* the set of glades that genuinely exist,
    // which is the exact question this interface asks, and a wrapper would only add a
    // place for the two answers to diverge.

    /// <summary>A fixed mapping. Used by the shared reward vectors and by tests.</summary>
    public sealed class FixedChapterMap : IChapterMap
    {
        readonly Dictionary<LevelId, ChapterId> _chapters = new Dictionary<LevelId, ChapterId>();

        public FixedChapterMap() { }

        public FixedChapterMap(IEnumerable<KeyValuePair<string, string>> levelToChapter)
        {
            if (levelToChapter == null) return;

            foreach (var pair in levelToChapter)
            {
                if (!LevelId.TryParse(pair.Key, out var level, out _)) continue;
                if (!ChapterId.TryParse(pair.Value, out var chapter, out _)) continue;
                _chapters[level] = chapter;
            }
        }

        public void Add(string level, string chapter)
            => _chapters[LevelId.Parse(level)] = ChapterId.Parse(chapter);

        public bool TryGetChapter(LevelId level, out ChapterId chapter)
            => _chapters.TryGetValue(level, out chapter);
    }
}
