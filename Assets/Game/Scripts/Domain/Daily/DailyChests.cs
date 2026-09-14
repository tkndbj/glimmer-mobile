using System;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Daily
{
    /// <summary>
    /// <b>Retired in place.</b> The daily chest ladder — three chests earned by finishing
    /// runs, reset at midnight — was folded into the tasks on 2026-09-14: "finish N runs
    /// today" is one row of the daily slate, paying a tiered chest like every other task.
    /// See <c>Tasks.TaskLedger</c>.
    ///
    /// <para>
    /// What stays is the wire. <see cref="SaveFileDto.daily"/> is still written, joined and
    /// mapped, because a rolled-back client writes it and the security rules' allow-list
    /// cannot drop a key without refusing every save write (invariant 12a) — and the server's
    /// <c>daily:</c> claim path still pays a chest such a client opens. Nothing on this build
    /// counts a run into it or opens one from it; the counters it carries describe a day an
    /// older client played.
    /// </para>
    /// <para>
    /// The chest engine it was built on — <see cref="ChestRandom"/>,
    /// <see cref="ChestDefinition"/>, <see cref="DailyChestTable"/> — is what every task chest
    /// rolls with, and the <c>daily</c> block of <c>progression.json</c> is still seeded so the
    /// server can price an older client's claims. Both are live; only the ladder is not.
    /// </para>
    /// </summary>
    public static class DailyChests
    {
        static int _dayKey;
        static int _runs;
        static int _claimed;

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            var daily = dto?.daily;

            _dayKey = daily == null || daily.dayKey < 0 ? 0 : daily.dayKey;
            _runs = daily == null || daily.runs < 0 ? 0 : daily.runs;
            _claimed = daily == null || daily.claimed < 0 ? 0 : daily.claimed;
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.daily = new DailyStateDto
            {
                dayKey = _dayKey,
                runs = _runs,
                claimed = _claimed,
            };
        }

        /// <summary>
        /// Joins two devices' days, exactly as it always did: the later day outright, and
        /// the larger counts within a shared day. Kept so a rolled-back client's day merges
        /// the way it expects rather than being flattened by this build.
        /// </summary>
        internal static DailyStateDto Join(DailyStateDto mine, DailyStateDto other)
        {
            if (mine == null && other == null) return new DailyStateDto();
            if (mine == null) return Copy(other);
            if (other == null) return Copy(mine);

            if (mine.dayKey > other.dayKey) return Copy(mine);
            if (other.dayKey > mine.dayKey) return Copy(other);

            return new DailyStateDto
            {
                dayKey = mine.dayKey,
                runs = Math.Max(mine.runs, other.runs),
                claimed = Math.Max(mine.claimed, other.claimed),
            };
        }

        static DailyStateDto Copy(DailyStateDto d)
            => new DailyStateDto { dayKey = d.dayKey, runs = d.runs, claimed = d.claimed };

        /// <summary>Forgets the carried day. Dev only, and used by the wipe.</summary>
        internal static void Reset()
        {
            _dayKey = 0;
            _runs = 0;
            _claimed = 0;
        }
    }
}
