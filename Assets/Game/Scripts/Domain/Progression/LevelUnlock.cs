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
        public static LevelId NextToPlay(CatalogIndex index) => NextToPlay(index, GameMode.Default);

        /// <summary>
        /// The same question asked of one way of playing.
        ///
        /// <para>
        /// Every mode keeps its own place, which is the whole of what "the modes are
        /// independent" means in code: the ladders never chain, so finishing one is never the
        /// price of opening another, and a player halfway through both is halfway through both
        /// rather than at whichever the flattened order happened to reach first. The rule
        /// itself is unchanged and is still expressed once - <see cref="IsUnlocked"/> asks
        /// <c>CatalogIndex.Previous</c>, which already stays inside a mode.
        /// </para>
        /// <para>
        /// The mode-less overload answers for the ordinary one, which is what the hub's
        /// continue button and the splash want: a new player has never chosen a mode, and
        /// sending them anywhere else would be the game choosing for them.
        /// </para>
        /// </summary>
        public static LevelId NextToPlay(CatalogIndex index, GameMode mode)
        {
            if (index == null) return LevelId.None;

            var lane = index.LevelsIn(mode);
            if (lane.Count == 0) return LevelId.None;

            for (int i = 0; i < lane.Count; i++)
            {
                if (PlayerProgress.IsCleared(lane[i])) continue;
                if (IsUnlocked(index, lane[i])) return lane[i];
            }

            return index.LastIn(mode);
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
            => CurrentChapter(index, GameMode.Default);

        /// <summary>The chapter one mode's map should open on: wherever the player is up to in it.</summary>
        public static ChapterIndexEntry CurrentChapter(CatalogIndex index, GameMode mode)
        {
            if (index == null) return null;

            var next = NextToPlay(index, mode);
            var chapter = next.IsValid ? index.FindChapter(index.ChapterOf(next)) : null;
            return chapter ?? index.FirstChapterIn(mode) ?? index.FirstChapter;
        }

        public static ChapterIndexEntry ChapterBefore(CatalogIndex index, ChapterId chapter)
            => index?.ChapterNeighbour(chapter, -1);

        public static ChapterIndexEntry ChapterAfter(CatalogIndex index, ChapterId chapter)
            => index?.ChapterNeighbour(chapter, +1);
    }
}
