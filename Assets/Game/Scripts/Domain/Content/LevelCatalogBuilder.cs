using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Assembles a <see cref="LevelCatalog"/> from chapters that may have arrived
    /// from different places and in any order.
    ///
    /// Every rejection is recorded rather than thrown. A single malformed chapter
    /// downloaded from the CDN must never stop the game booting, so the builder's
    /// job is to salvage what it can and hand the problems back for reporting.
    /// </summary>
    public sealed class LevelCatalogBuilder
    {
        readonly Dictionary<ChapterId, ChapterDefinition> _chapters = new Dictionary<ChapterId, ChapterDefinition>();
        readonly Dictionary<LevelId, LevelDefinition> _levels = new Dictionary<LevelId, LevelDefinition>();
        readonly List<string> _problems = new List<string>();

        public IReadOnlyList<string> Problems => _problems;
        public bool HasProblems => _problems.Count > 0;

        public void Report(string problem) => _problems.Add(problem);

        /// <summary>
        /// Adds a chapter and its levels. A later chapter with the same id replaces
        /// an earlier one wholesale — that is how a remote pack overrides a bundled
        /// one without any merge ambiguity.
        /// </summary>
        public void AddChapter(ChapterDefinition chapter, IEnumerable<LevelDefinition> levels)
        {
            if (chapter == null) return;

            if (_chapters.ContainsKey(chapter.Id))
                _problems.Add($"chapter '{chapter.Id}' declared twice, the later one wins");

            _chapters[chapter.Id] = chapter;

            foreach (var level in levels)
            {
                if (level == null) continue;

                if (level.Chapter != chapter.Id)
                {
                    _problems.Add($"level '{level.Id}' claims chapter '{level.Chapter}' but was filed under '{chapter.Id}'");
                    continue;
                }
                if (_levels.TryGetValue(level.Id, out var existing) && existing.Chapter != chapter.Id)
                {
                    _problems.Add($"level id '{level.Id}' is used by both '{existing.Chapter}' and '{chapter.Id}'");
                    continue;
                }
                _levels[level.Id] = level;
            }
        }

        public LevelCatalog Build()
        {
            var chapters = new List<ChapterDefinition>(_chapters.Values);
            chapters.Sort((a, b) =>
            {
                int byOrder = a.Order.CompareTo(b.Order);
                return byOrder != 0 ? byOrder : a.Id.CompareTo(b.Id);
            });

            var ordered = new List<LevelDefinition>(_levels.Count);
            var placed = new HashSet<LevelId>();

            // chapter order drives play order; a chapter's own list drives order within it
            foreach (var chapter in chapters)
            {
                foreach (var lid in chapter.LevelIds)
                {
                    if (!_levels.TryGetValue(lid, out var level))
                    {
                        _problems.Add($"chapter '{chapter.Id}' lists unknown level '{lid}'");
                        continue;
                    }
                    if (!placed.Add(lid))
                    {
                        _problems.Add($"level '{lid}' listed more than once in chapter '{chapter.Id}'");
                        continue;
                    }
                    ordered.Add(level);
                }
            }

            // a level that parsed but no chapter listed would silently vanish; say so
            foreach (var kv in _levels)
                if (!placed.Contains(kv.Key))
                    _problems.Add($"level '{kv.Key}' is not listed by chapter '{kv.Value.Chapter}' and will not appear");

            return new LevelCatalog(chapters.ToArray(), ordered.ToArray());
        }
    }
}
