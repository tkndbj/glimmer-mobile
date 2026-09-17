using System;
using GlimmerGrove.Content;

namespace GlimmerGrove.Referral
{
    /// <summary>
    /// Whether the milestone chapter is cleared, asked of the index and a star ledger.
    ///
    /// <para>
    /// The device's answer is only a hint — it decides whether to <em>ask</em> the server
    /// after a sync, never whether anybody is paid. The server asks the same question of the
    /// save it holds (<c>referral.ts</c>), which is why the two copies read the same shape: a
    /// chapter's level ids from the manifest, and a star count per id.
    /// </para>
    /// </summary>
    public static class ReferralMilestone
    {
        /// <summary>Every level of the chapter cleared. False for a chapter the index does not hold.</summary>
        public static bool IsComplete(CatalogIndex index, ChapterId chapter, Func<LevelId, bool> cleared)
        {
            if (index == null || !chapter.IsValid || cleared == null) return false;

            var entry = index.FindChapter(chapter);
            if (entry == null || entry.IsEmpty) return false;

            var ids = entry.LevelIds;
            for (int i = 0; i < ids.Count; i++)
                if (!cleared(ids[i])) return false;

            return true;
        }

        /// <summary>How many of the chapter's levels are cleared, and how many there are, for a progress line.</summary>
        public static (int cleared, int total) Progress(CatalogIndex index, ChapterId chapter,
                                                        Func<LevelId, bool> cleared)
        {
            if (index == null || !chapter.IsValid || cleared == null) return (0, 0);

            var entry = index.FindChapter(chapter);
            if (entry == null) return (0, 0);

            int done = 0;
            var ids = entry.LevelIds;
            for (int i = 0; i < ids.Count; i++) if (cleared(ids[i])) done++;

            return (done, ids.Count);
        }
    }
}
