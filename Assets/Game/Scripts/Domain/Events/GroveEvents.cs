using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Events
{
    /// <summary>
    /// The calendar as the game reads it: which event is running, and how far through it
    /// this player is.
    ///
    /// <para>
    /// A facade over <see cref="CatalogIndex.Events"/> and <see cref="EventLedger"/>, in
    /// the same spirit as <c>PlayerProgression</c> over <c>ProgressionLedger</c>. The
    /// ledger stays a pure function of its arguments so it can be run against the shared
    /// vectors, and this is the one place that hands it the live catalog, the live save and
    /// the trusted clock.
    /// </para>
    /// <para>
    /// Nothing is cached. Both questions are a walk over a handful of events and a
    /// dictionary lookup per glade, and the alternative is a cache to invalidate on two
    /// events, a content refresh and a clock correction — which is more moving parts than
    /// the work it saves.
    /// </para>
    /// </summary>
    public static class GroveEvents
    {
        /// <summary>The event running right now, or null. Judged on the trusted clock.</summary>
        public static GroveEvent Live => GameContent.Index.LiveEventAt(GameClock.NowUnix());

        /// <summary>Every event the catalog holds, past and future, in start order.</summary>
        public static IReadOnlyList<GroveEvent> All => GameContent.Index.Events;

        /// <summary>How far through a track this player is.</summary>
        public static EventProgress ProgressOf(GroveEvent groveEvent)
            => EventLedger.ProgressOf(groveEvent, PlayerProgress.RecordsById);

        /// <summary>Seconds until the live event closes, or 0 when there is not one.</summary>
        public static long SecondsLeft
        {
            get
            {
                var live = Live;
                return live == null ? 0 : live.SecondsLeftAt(GameClock.NowUnix());
            }
        }

        /// <summary>
        /// The next glade of the live event this player has not finished inside the window,
        /// or <see cref="LevelId.None"/>.
        ///
        /// What the event's play button aims at. In event order rather than catalog order,
        /// because the track's own list is the order somebody authored for it.
        /// </summary>
        public static LevelId NextGlade(GroveEvent groveEvent)
        {
            if (groveEvent == null || !groveEvent.IsValid) return LevelId.None;

            var records = PlayerProgress.RecordsById;

            foreach (var levelId in groveEvent.Levels)
            {
                if (!records.TryGetValue(levelId, out var record) || record == null ||
                    !record.IsCleared)
                {
                    return levelId;
                }

                long at = record.FirstClearedUnix;
                if (at < groveEvent.StartUnix || at >= groveEvent.EndUnix) return levelId;
            }

            return LevelId.None;
        }
    }
}
