using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Events
{
    /// <summary>
    /// The calendar as the game reads it: which season is running, and how far through it
    /// this player is.
    ///
    /// <para>
    /// A facade over <see cref="CatalogIndex.Events"/> and <see cref="SeasonLedger"/>, in the
    /// same spirit as <c>PlayerProgression</c> over <c>ProgressionLedger</c>. The ledger's
    /// arithmetic stays a pure function of its arguments so it can be run against the shared
    /// vectors, and this is the one place that hands it the live catalog and the trusted
    /// clock.
    /// </para>
    /// <para>
    /// Nothing is cached. Both questions are a walk over a handful of seasons and a
    /// dictionary lookup, and the alternative is a cache to invalidate on two events, a
    /// content refresh and a clock correction — which is more moving parts than the work it
    /// saves.
    /// </para>
    /// </summary>
    public static class GroveEvents
    {
        /// <summary>The season running right now, or null. Judged on the trusted clock.</summary>
        public static GroveEvent Live => GameContent.Index.LiveEventAt(GameClock.NowUnix());

        /// <summary>Every season the catalog holds, past and future, in start order.</summary>
        public static IReadOnlyList<GroveEvent> All => GameContent.Index.Events;

        /// <summary>How far through a track this player is, and how much of it is waiting.</summary>
        public static EventProgress ProgressOf(GroveEvent season) => SeasonLedger.ProgressOf(season);

        /// <summary>
        /// The season whose box the hub should show, or null.
        ///
        /// <para>
        /// The live one, or — when nothing is running — the most recent closed one still
        /// holding a chest the player has not taken. Rewards are claimed by hand, so a window
        /// closing must not take an earned chest with it: the marks stop growing at the
        /// deadline, the chests do not expire, and there has to be a way back to the page
        /// that holds them. Nothing else changes about a closed season, which is why this is a
        /// second reader rather than a change to <see cref="Live"/>.
        /// </para>
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
        /// Every season rather than the live one, because a closed track can still be holding
        /// something and a badge that stopped counting it would be advertising a smaller
        /// number than the page shows.
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

        /// <summary>Seconds until the live season closes, or 0 when there is not one.</summary>
        public static long SecondsLeft
        {
            get
            {
                var live = Live;
                return live == null ? 0 : live.SecondsLeftAt(GameClock.NowUnix());
            }
        }
    }
}
