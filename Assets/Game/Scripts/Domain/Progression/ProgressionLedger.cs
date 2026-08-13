using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>What a set of records is worth. Always derived, never stored.</summary>
    public readonly struct ProgressionTotals
    {
        public readonly long Xp;
        public readonly long EarnedCredits;
        public readonly int ClearedGlades;
        public readonly int TotalStars;

        public ProgressionTotals(long xp, long earnedCredits, int clearedGlades, int totalStars)
        {
            Xp = xp;
            EarnedCredits = earnedCredits;
            ClearedGlades = clearedGlades;
            TotalStars = totalStars;
        }

        public static readonly ProgressionTotals Zero = new ProgressionTotals(0, 0, 0, 0);

        public static ProgressionTotals operator -(ProgressionTotals a, ProgressionTotals b)
            => new ProgressionTotals(a.Xp - b.Xp,
                                     a.EarnedCredits - b.EarnedCredits,
                                     a.ClearedGlades - b.ClearedGlades,
                                     a.TotalStars - b.TotalStars);

        public bool IsZero => Xp == 0 && EarnedCredits == 0 && ClearedGlades == 0 && TotalStars == 0;
    }

    /// <summary>
    /// Turns the star ledger into XP and credits.
    ///
    /// This is the heart of the progression design, and it is a pure function on
    /// purpose. Nothing accumulates: a player's XP is recomputed from the records
    /// they hold every time it is asked for. That single decision is what makes the
    /// system survive the things that break accumulators —
    ///
    /// <list type="bullet">
    /// <item>replaying a glade awards nothing, because the record did not improve</item>
    /// <item>two devices cannot double-count, because there is no counter to count twice</item>
    /// <item>the curve can be retuned after launch and every player is consistent</item>
    /// <item>a lost or damaged value recomputes instead of being gone</item>
    /// </list>
    ///
    /// <para>
    /// <b>This rule exists twice.</b> The server re-derives credits from the same
    /// records in <c>firebase/functions/src/progression.ts</c>, which is what lets a
    /// forged save be caught rather than merely disbelieved. The two are held together
    /// by <c>firebase/shared/reward-vectors.json</c>, which both sides run as a test —
    /// so a change to one that is not mirrored in the other fails a build rather than
    /// quietly desynchronising the economy. If you change the arithmetic here, change
    /// it there, and add a vector proving what changed.
    /// </para>
    ///
    /// <para>
    /// Three rules make the two agree exactly, and each has a reason:
    /// a glade the catalog cannot vouch for earns nothing (or an invented level id
    /// would mint currency); stars are clamped to three (or a forged record would);
    /// and a level id counts once however many times it appears.
    /// </para>
    /// </summary>
    public static class ProgressionLedger
    {
        /// <summary>What one record is worth under the given chapter's rule.</summary>
        public static ProgressionTotals Value(LevelRecord record, ChapterId chapter, ProgressionTable table)
        {
            if (record == null) return ProgressionTotals.Zero;

            int stars = ClampStars(record.Stars);
            if (stars <= 0) return ProgressionTotals.Zero;

            table ??= ProgressionTable.Default;
            var rule = table.RuleFor(chapter);

            return new ProgressionTotals(rule.XpFor(stars), rule.CreditsFor(stars), 1, stars);
        }

        /// <summary>
        /// What a run added, as the difference between the record before and after.
        /// A worse replay yields zero without needing a rule that says so.
        /// </summary>
        public static ProgressionTotals Delta(LevelRecord before, LevelRecord after,
                                              ChapterId chapter, ProgressionTable table)
            => Value(after, chapter, table) - Value(before, chapter, table);

        /// <summary>
        /// Folds every record the player holds into one total, counting only glades the
        /// chapter map recognises.
        /// </summary>
        public static ProgressionTotals Compute(IEnumerable<LevelRecord> records,
                                                IChapterMap chapters, ProgressionTable table)
        {
            table ??= ProgressionTable.Default;

            long xp = 0, credits = 0;
            int cleared = 0, stars = 0;

            if (records == null || chapters == null)
                return new ProgressionTotals(0, 0, 0, 0);

            var counted = new HashSet<LevelId>();

            foreach (var record in records)
            {
                if (record == null) continue;

                // A level the catalog has never heard of is not evidence of anything.
                if (!chapters.TryGetChapter(record.Id, out var chapter)) continue;

                // Two records for one glade is a malformed save, not two clears.
                if (!counted.Add(record.Id)) continue;

                var totals = Value(record, chapter, table);
                xp += totals.Xp;
                credits += totals.EarnedCredits;
                cleared += totals.ClearedGlades;
                stars += totals.TotalStars;
            }

            return new ProgressionTotals(xp, credits, cleared, stars);
        }

        /// <summary>Convenience for the live catalog.</summary>
        public static ProgressionTotals Compute(IEnumerable<LevelRecord> records,
                                                LevelCatalog catalog, ProgressionTable table)
            => Compute(records, new CatalogChapterMap(catalog), table);

        static int ClampStars(int stars)
            => stars < 0 ? 0 : stars > LevelRecord.MaxStars ? LevelRecord.MaxStars : stars;
    }
}
