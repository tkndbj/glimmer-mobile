using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// Decides what the player is allowed to open.
    ///
    /// Kept apart from both the catalog and the save file on purpose. The index knows
    /// the order, progress knows what is cleared, and this is the only place that turns
    /// those two facts into a rule — so changing the rule later (star gates between
    /// chapters, a skip-ahead offer, an event track) is a change here and nowhere else.
    ///
    /// It works entirely in <see cref="LevelId"/> against the <see cref="CatalogIndex"/>,
    /// never in loaded definitions. Deciding what is unlocked is something the map does
    /// for a whole chapter at a time and the home screen does at launch, so it must not
    /// be able to pull a chapter body in behind itself.
    /// </summary>
    public static class LevelUnlock
    {
        public static bool IsUnlocked(CatalogIndex index, LevelId id)
        {
            if (index == null || !index.Contains(id)) return false;

            var previous = index.Previous(id);
            if (!previous.IsValid) return true;            // the very first level

            return PlayerProgress.IsCleared(previous);
        }

        /// <summary>
        /// Where the player should be sent by default: the first unlocked level they
        /// have not cleared, or the last level once the grove is finished.
        /// </summary>
        public static LevelId NextToPlay(CatalogIndex index)
        {
            if (index == null || index.IsEmpty) return LevelId.None;

            foreach (var id in index.LevelIds)
            {
                if (PlayerProgress.IsCleared(id)) continue;
                if (IsUnlocked(index, id)) return id;
            }

            return index.Last;
        }

        /// <summary>The level a "next" button should lead to, or none at the end.</summary>
        public static LevelId After(CatalogIndex index, LevelId id)
            => index?.Next(id) ?? LevelId.None;

        // ------------------------------------------------------------- chapters
        /// <summary>
        /// A chapter opens once its first level does. Expressed in terms of the level
        /// rule rather than duplicating it, so a future change to how levels unlock
        /// carries through to chapters automatically.
        /// </summary>
        public static bool IsChapterUnlocked(CatalogIndex index, ChapterId chapter)
        {
            var entry = index?.FindChapter(chapter);
            if (entry == null || entry.IsEmpty) return false;

            return IsUnlocked(index, entry.FirstLevel);
        }

        /// <summary>The chapter the map should open on: wherever the player is up to.</summary>
        public static ChapterIndexEntry CurrentChapter(CatalogIndex index)
        {
            if (index == null || index.IsEmpty) return null;

            var next = NextToPlay(index);
            var chapter = next.IsValid ? index.FindChapter(index.ChapterOf(next)) : null;
            return chapter ?? index.FirstChapter;
        }

        public static ChapterIndexEntry ChapterBefore(CatalogIndex index, ChapterId chapter)
            => index?.ChapterNeighbour(chapter, -1);

        public static ChapterIndexEntry ChapterAfter(CatalogIndex index, ChapterId chapter)
            => index?.ChapterNeighbour(chapter, +1);
    }
}
