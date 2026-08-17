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
        /// <summary>
        /// What one record is worth under the given chapter's rule.
        ///
        /// <para>
        /// <paramref name="playerKey"/> seeds the golden bonus — see
        /// <see cref="GoldenTable"/> — and an empty one means "no account yet, pay the
        /// base". It is a parameter rather than a read of <see cref="RewardSeed"/> because
        /// this is a pure function and has to stay one: the shared vectors run it offline
        /// against fixed inputs, and a hidden dependency on the signed-in account would
        /// make the two halves of invariant 9a untestable against each other.
        /// </para>
        /// <para>
        /// The bonus multiplies credits and never XP. Keeping XP exact is what lets a
        /// companion unlock be promised at a rank and arrive there — a levelling curve
        /// with variance in it turns "three ranks to go" into a guess.
        /// </para>
        /// </summary>
        public static ProgressionTotals Value(LevelRecord record, ChapterId chapter,
                                              ProgressionTable table, string playerKey = null)
        {
            if (record == null) return ProgressionTotals.Zero;

            int stars = ClampStars(record.Stars);
            if (stars <= 0) return ProgressionTotals.Zero;

            table ??= ProgressionTable.Default;
            var rule = table.RuleFor(chapter);

            long credits = GoldenTable.Apply(rule.CreditsFor(stars),
                                             table.Golden.PercentFor(playerKey, record.Id));

            return new ProgressionTotals(rule.XpFor(stars), credits, 1, stars);
        }

        /// <summary>
        /// What a run added, as the difference between the record before and after.
        /// A worse replay yields zero without needing a rule that says so.
        ///
        /// The golden multiplier is the same on both sides of the subtraction — it belongs
        /// to the glade, not to the run — so improving a record pays the bonus on the
        /// improvement and nothing on what was already banked.
        /// </summary>
        public static ProgressionTotals Delta(LevelRecord before, LevelRecord after,
                                              ChapterId chapter, ProgressionTable table,
                                              string playerKey = null)
            => Value(after, chapter, table, playerKey) - Value(before, chapter, table, playerKey);

        /// <summary>
        /// Folds every record the player holds into one total, counting only glades the
        /// chapter map recognises.
        /// </summary>
        /// <param name="collectedEvents">
        /// Each event's collected floor, keyed by event id — how much of a track the player
        /// has actually taken. Null pays no event credits at all, which is the safe default
        /// rather than an oversight: see <c>EventLedger.CreditsFrom</c>.
        /// </param>
        public static ProgressionTotals Compute(IEnumerable<LevelRecord> records,
                                                IChapterMap chapters, ProgressionTable table,
                                                string playerKey = null,
                                                IReadOnlyList<Events.GroveEvent> events = null,
                                                IReadOnlyDictionary<string, int> collectedEvents = null)
        {
            table ??= ProgressionTable.Default;

            long xp = 0, credits = 0;
            int cleared = 0, stars = 0;

            if (records == null || chapters == null)
                return new ProgressionTotals(0, 0, 0, 0);

            bool wantsEvents = events != null && events.Count > 0;

            // Built only when there is a calendar to answer for. The event track needs a
            // lookup by level id, and this loop is already visiting every record — walking
            // them a second time per event would be the same work multiplied by however
            // many events the game has ever run.
            var byId = wantsEvents
                ? new Dictionary<LevelId, LevelRecord>()
                : null;

            var counted = new HashSet<LevelId>();

            foreach (var record in records)
            {
                if (record == null) continue;

                // A level the catalog has never heard of is not evidence of anything.
                if (!chapters.TryGetChapter(record.Id, out var chapter)) continue;

                // Two records for one glade is a malformed save, not two clears.
                if (!counted.Add(record.Id)) continue;

                byId?.Add(record.Id, record);

                var totals = Value(record, chapter, table, playerKey);
                xp += totals.Xp;
                credits += totals.EarnedCredits;
                cleared += totals.ClearedGlades;
                stars += totals.TotalStars;
            }

            // Event tracks are earned, not granted — see EventLedger for why that is the
            // only shape they can safely take. They add to the same derived total, so the
            // earned floor covers them and there is nothing to claim or confirm. What the
            // floors decide is only *when* a rung lands: a milestone the player has reached
            // but not yet collected is not in this number, which is what makes collecting
            // one move a balance rather than replay an arithmetic they were paid for weeks
            // ago. Since a balance is max(derived, earned floor), a floor that is behind can
            // never take currency away from anybody — the worst case is a page offering a
            // flower whose credits are already banked.
            if (wantsEvents) credits += Events.EventLedger.CreditsFrom(events, byId, collectedEvents);

            return new ProgressionTotals(xp, credits, cleared, stars);
        }

        // There is no catalog-shaped overload here any more, and deliberately not: a
        // CatalogIndex *is* an IChapterMap, so the live call binds to the one above with
        // nothing in between. An overload taking the concrete type would also have
        // silently out-resolved it and recursed.

        static int ClampStars(int stars)
            => stars < 0 ? 0 : stars > LevelRecord.MaxStars ? LevelRecord.MaxStars : stars;
    }
}
