using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// Decides what the player is allowed to open.
    ///
    /// Kept apart from both the catalog and the save file on purpose. The catalog
    /// knows the order, progress knows what is cleared, and this is the only place
    /// that turns those two facts into a rule — so changing the rule later (star
    /// gates between chapters, a skip-ahead offer, an event track) is a change here
    /// and nowhere else.
    /// </summary>
    public static class LevelUnlock
    {
        public static bool IsUnlocked(LevelCatalog catalog, LevelId id)
        {
            if (catalog == null || !catalog.Contains(id)) return false;

            var previous = catalog.Previous(id);
            if (previous == null) return true;            // the very first level

            return PlayerProgress.IsCleared(previous.Id);
        }

        /// <summary>
        /// Where the player should be sent by default: the first unlocked level they
        /// have not cleared, or the last level once the grove is finished.
        /// </summary>
        public static LevelDefinition NextToPlay(LevelCatalog catalog)
        {
            if (catalog == null || catalog.IsEmpty) return null;

            foreach (var level in catalog.Levels)
            {
                if (PlayerProgress.IsCleared(level.Id)) continue;
                if (IsUnlocked(catalog, level.Id)) return level;
            }

            return catalog.Last;
        }

        /// <summary>The level a "next" button should lead to, or null at the end.</summary>
        public static LevelDefinition After(LevelCatalog catalog, LevelId id)
            => catalog?.Next(id);

        // ------------------------------------------------------------- chapters
        /// <summary>
        /// A chapter opens once its first level does. Expressed in terms of the
        /// level rule rather than duplicating it, so a future change to how levels
        /// unlock carries through to chapters automatically.
        /// </summary>
        public static bool IsChapterUnlocked(LevelCatalog catalog, ChapterId chapter)
        {
            var definition = catalog?.FindChapter(chapter);
            if (definition == null || definition.LevelCount == 0) return false;

            return IsUnlocked(catalog, definition.LevelIds[0]);
        }

        /// <summary>The chapter the map should open on: wherever the player is up to.</summary>
        public static ChapterDefinition CurrentChapter(LevelCatalog catalog)
        {
            if (catalog == null || catalog.IsEmpty) return null;

            var next = NextToPlay(catalog);
            var chapter = next != null ? catalog.ChapterOf(next) : null;
            return chapter ?? catalog.FindChapter(catalog.First.Chapter);
        }

        public static ChapterDefinition ChapterBefore(LevelCatalog catalog, ChapterId chapter)
            => Neighbour(catalog, chapter, -1);

        public static ChapterDefinition ChapterAfter(LevelCatalog catalog, ChapterId chapter)
            => Neighbour(catalog, chapter, +1);

        static ChapterDefinition Neighbour(LevelCatalog catalog, ChapterId chapter, int step)
        {
            if (catalog == null) return null;

            var chapters = catalog.Chapters;
            for (int i = 0; i < chapters.Count; i++)
            {
                if (chapters[i].Id != chapter) continue;
                int j = i + step;
                return j >= 0 && j < chapters.Count ? chapters[j] : null;
            }
            return null;
        }
    }
}
