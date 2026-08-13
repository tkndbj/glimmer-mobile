using System;
using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Every level the game currently knows about, in play order.
    ///
    /// The catalog is the only place that understands sequence. Levels know their
    /// own identity, the catalog knows what comes next, and progress knows what has
    /// been cleared — keeping those three apart is what lets content be reordered
    /// or extended without any of the other two noticing.
    ///
    /// Immutable once built. A content refresh produces a new catalog rather than
    /// mutating this one, so nothing can observe a half-updated world.
    /// </summary>
    public sealed class LevelCatalog
    {
        public static readonly LevelCatalog Empty =
            new LevelCatalog(Array.Empty<ChapterDefinition>(), Array.Empty<LevelDefinition>());

        readonly ChapterDefinition[] _chapters;
        readonly LevelDefinition[] _levels;
        readonly Dictionary<LevelId, int> _levelOrder;
        readonly Dictionary<ChapterId, ChapterDefinition> _chapterById;

        internal LevelCatalog(ChapterDefinition[] chapters, LevelDefinition[] levels)
        {
            _chapters = chapters;
            _levels = levels;

            _levelOrder = new Dictionary<LevelId, int>(levels.Length);
            for (int i = 0; i < levels.Length; i++) _levelOrder[levels[i].Id] = i;

            _chapterById = new Dictionary<ChapterId, ChapterDefinition>(chapters.Length);
            foreach (var c in chapters) _chapterById[c.Id] = c;
        }

        public IReadOnlyList<ChapterDefinition> Chapters => _chapters;

        /// <summary>All levels flattened into play order across every chapter.</summary>
        public IReadOnlyList<LevelDefinition> Levels => _levels;

        public int Count => _levels.Length;
        public bool IsEmpty => _levels.Length == 0;

        public bool Contains(LevelId id) => _levelOrder.ContainsKey(id);

        public LevelDefinition Find(LevelId id)
            => _levelOrder.TryGetValue(id, out int i) ? _levels[i] : null;

        public bool TryFind(LevelId id, out LevelDefinition level)
        {
            if (_levelOrder.TryGetValue(id, out int i)) { level = _levels[i]; return true; }
            level = null;
            return false;
        }

        public ChapterDefinition FindChapter(ChapterId id)
            => _chapterById.TryGetValue(id, out var c) ? c : null;

        /// <summary>The chapter a level belongs to. Never null for a catalogued level.</summary>
        public ChapterDefinition ChapterOf(LevelDefinition level)
            => level == null ? null : FindChapter(level.Chapter);

        /// <summary>
        /// Zero-based position in play order, or -1. Use this for display numbering
        /// and for nothing else — never persist it.
        /// </summary>
        public int OrderOf(LevelId id) => _levelOrder.TryGetValue(id, out int i) ? i : -1;

        public LevelDefinition At(int order)
            => order >= 0 && order < _levels.Length ? _levels[order] : null;

        public LevelDefinition First => _levels.Length > 0 ? _levels[0] : null;
        public LevelDefinition Last => _levels.Length > 0 ? _levels[_levels.Length - 1] : null;

        public LevelDefinition Next(LevelId id)
        {
            int i = OrderOf(id);
            return i >= 0 && i + 1 < _levels.Length ? _levels[i + 1] : null;
        }

        public LevelDefinition Previous(LevelId id)
        {
            int i = OrderOf(id);
            return i > 0 ? _levels[i - 1] : null;
        }

        public bool IsLast(LevelId id) => OrderOf(id) == _levels.Length - 1;

        /// <summary>Levels of one chapter, in order.</summary>
        public IEnumerable<LevelDefinition> LevelsOf(ChapterId chapter)
        {
            var c = FindChapter(chapter);
            if (c == null) yield break;
            foreach (var lid in c.LevelIds)
                if (TryFind(lid, out var level)) yield return level;
        }
    }
}
