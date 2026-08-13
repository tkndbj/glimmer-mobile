using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Assembles a <see cref="CatalogIndex"/> from manifest entries.
    ///
    /// Every rejection is recorded rather than thrown, for the same reason the rest of
    /// the content system works that way: a manifest can arrive from a CDN, and one
    /// malformed entry must cost that chapter rather than the launch. The builder
    /// salvages what it can and hands the problems back to be reported — as warnings
    /// at runtime, as build failures in the Editor.
    /// </summary>
    public sealed class CatalogIndexBuilder
    {
        readonly List<ChapterIndexEntry> _chapters = new List<ChapterIndexEntry>();
        readonly HashSet<ChapterId> _chapterIds = new HashSet<ChapterId>();
        readonly Dictionary<LevelId, ChapterId> _levelChapter = new Dictionary<LevelId, ChapterId>();
        readonly List<string> _problems = new List<string>();

        public IReadOnlyList<string> Problems => _problems;
        public bool HasProblems => _problems.Count > 0;

        public void Report(string problem) => _problems.Add(problem);

        /// <summary>
        /// Reads one manifest entry. Returns false when the chapter is not for this
        /// client — retired, or needing newer code — which is a decision rather than a
        /// problem and so is reported to nobody.
        /// </summary>
        public bool Add(ManifestChapterDto entry, int appVersion)
        {
            if (entry == null) return false;
            if (entry.disabled) return false;

            if (!ChapterId.TryParse(entry.id, out var chapterId, out string idError))
            {
                _problems.Add($"manifest lists chapter '{entry.id}' which is rejected: {idError}");
                return false;
            }

            // Content that needs newer client code is skipped whole, never half-read.
            if (entry.minAppVersion > 0 && appVersion < entry.minAppVersion) return false;

            if (!_chapterIds.Add(chapterId))
            {
                _problems.Add($"manifest lists chapter '{chapterId}' twice; the later entry is ignored");
                return false;
            }

            var levelIds = ReadLevelIds(entry, chapterId);
            if (levelIds.Count == 0)
                _problems.Add($"chapter '{chapterId}' lists no levels and will show as empty");

            _chapters.Add(new ChapterIndexEntry(chapterId, entry.order, entry.version, levelIds));
            return true;
        }

        List<LevelId> ReadLevelIds(ManifestChapterDto entry, ChapterId chapterId)
        {
            var levelIds = new List<LevelId>();
            if (entry.levels == null) return levelIds;

            foreach (var raw in entry.levels)
            {
                if (!LevelId.TryParse(raw, out var levelId, out string error))
                {
                    _problems.Add($"chapter '{chapterId}' lists level '{raw}' which is rejected: {error}");
                    continue;
                }

                // A level id belongs to exactly one chapter, forever. Two chapters
                // claiming one would make a save record ambiguous about what it paid.
                if (_levelChapter.TryGetValue(levelId, out var owner))
                {
                    _problems.Add($"level id '{levelId}' is claimed by both '{owner}' and '{chapterId}'");
                    continue;
                }

                _levelChapter[levelId] = chapterId;
                levelIds.Add(levelId);
            }

            return levelIds;
        }

        public CatalogIndex Build()
        {
            // Sparse orders let a chapter slot between two shipped ones. Ties break on
            // id so the result is deterministic rather than dependent on file order —
            // a tie is still an authoring mistake, and validation says so.
            _chapters.Sort((a, b) =>
            {
                int byOrder = a.Order.CompareTo(b.Order);
                return byOrder != 0 ? byOrder : a.Id.CompareTo(b.Id);
            });

            var levelIds = new List<LevelId>();
            var levelOrder = new Dictionary<LevelId, int>();

            foreach (var chapter in _chapters)
                foreach (var levelId in chapter.LevelIds)
                {
                    levelOrder[levelId] = levelIds.Count;
                    levelIds.Add(levelId);
                }

            return new CatalogIndex(_chapters.ToArray(), levelIds.ToArray(), levelOrder, _levelChapter);
        }
    }
}
