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

        /// <summary>How far through a track this player is, and how much of it they hold.</summary>
        public static EventProgress ProgressOf(GroveEvent groveEvent)
            => EventLedger.ProgressOf(groveEvent, PlayerProgress.RecordsById,
                                      EventCollection.CollectedGoal(groveEvent?.Id));

        /// <summary>
        /// Hands over every uncollected rung of this event up to and including
        /// <paramref name="goal"/>, and returns how many that swept.
        ///
        /// The one write in this facade, here rather than on <see cref="EventCollection"/>'s
        /// own surface for the reason the reads are: this is the type that knows the live
        /// save, and a screen should not have to fetch the record map to collect a flower.
        /// </summary>
        public static int Collect(GroveEvent groveEvent, int goal)
            => EventCollection.Collect(groveEvent, goal, PlayerProgress.RecordsById);

        /// <summary>True when tapping this rung would hand something over.</summary>
        public static bool IsCollectable(GroveEvent groveEvent, EventMilestone milestone)
            => EventCollection.IsCollectable(groveEvent, milestone, PlayerProgress.RecordsById);

        /// <summary>True when this rung's reward is already in the player's balance.</summary>
        public static bool IsCollected(GroveEvent groveEvent, EventMilestone milestone)
            => EventCollection.IsCollected(groveEvent, milestone);

        /// <summary>
        /// The event whose box the hub should show, or null.
        ///
        /// The live one, or — when nothing is running — the most recent closed one still
        /// holding a reward the player has not taken. Rewards are collected by hand now, so
        /// a window closing must not take an earned flower with it: the glades stop counting
        /// at the deadline, the reward does not expire, and there has to be a way back to
        /// the page that holds it. Nothing else changes about a closed event, which is why
        /// this is a second reader rather than a change to <see cref="Live"/>.
        /// </summary>
        public static GroveEvent Featured
        {
            get
            {
                var live = Live;
                if (live != null) return live;

                var all = All;
                if (all == null) return null;

                long now = GameClock.NowUnix();
                GroveEvent best = null;

                for (int i = 0; i < all.Count; i++)
                {
                    var candidate = all[i];
                    if (candidate == null || !candidate.IsValid) continue;
                    if (!candidate.HasEndedAt(now)) continue;
                    if (!ProgressOf(candidate).AnyWaiting) continue;

                    if (best == null || candidate.EndUnix > best.EndUnix) best = candidate;
                }

                return best;
            }
        }

        /// <summary>
        /// Rungs waiting across the whole calendar. What the hub's badge counts.
        ///
        /// Every event rather than the live one, because a closed track can still be
        /// holding something and a badge that stopped counting it would be advertising a
        /// smaller number than the page shows.
        /// </summary>
        public static int Waiting
        {
            get
            {
                var all = All;
                if (all == null) return 0;

                int waiting = 0;
                for (int i = 0; i < all.Count; i++) waiting += ProgressOf(all[i]).Waiting;
                return waiting;
            }
        }

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
