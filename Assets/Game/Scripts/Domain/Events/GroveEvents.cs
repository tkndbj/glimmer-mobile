using System;
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

        /// <summary>
        /// Every season this player could still have business with, in start order: the
        /// authored calendar, plus any season their own save still holds a row for.
        ///
        /// <para>
        /// <b>The second half is what a repeating season made necessary.</b> A recurrence has no
        /// list — it is a function of the clock — so a cycle that closed exists nowhere except as
        /// an id in somebody's save. Reading only the authored calendar would make a chest earned
        /// last season unreachable the instant the next one opened, which is exactly what
        /// invariant 47c promises never happens; so the save is asked which seasons it still has
        /// something in, and <see cref="CatalogIndex.EventById"/> turns each id back into a
        /// season. A row for a season this build has never heard of contributes nothing and is
        /// skipped, which is the case a rolled-back client is in.
        /// </para>
        /// <para>
        /// It is a walk over a handful of rows and it is not cached, for the reason this class
        /// gives above: the alternative is a cache invalidated by a claim, a sync, a content
        /// refresh and a clock correction, which is more moving parts than the work it saves.
        /// </para>
        /// </summary>
        public static IReadOnlyList<GroveEvent> All
        {
            get
            {
                var index = GameContent.Index;
                var authored = index.Events;

                var held = SeasonLedger.HeldIds;
                if (held == null || held.Count == 0) return authored;

                var all = new List<GroveEvent>(authored.Count + held.Count);
                var seen = new HashSet<string>(StringComparer.Ordinal);

                for (int i = 0; i < authored.Count; i++)
                {
                    all.Add(authored[i]);
                    seen.Add(authored[i].Id);
                }

                for (int i = 0; i < held.Count; i++)
                {
                    if (!seen.Add(held[i])) continue;

                    var season = index.EventById(held[i]);
                    if (season != null && season.IsValid) all.Add(season);
                }

                // Start order, ties on id — `CatalogIndexBuilder.UsableEvents`' rule, restated
                // here because the rows joined in above arrive in save order rather than in
                // calendar order and every caller reads this as a calendar.
                all.Sort((a, b) =>
                {
                    int byStart = a.StartUnix.CompareTo(b.StartUnix);
                    return byStart != 0 ? byStart : string.CompareOrdinal(a.Id, b.Id);
                });

                return all;
            }
        }

        /// <summary>How far through a track this player is, and how much of it is waiting.</summary>
        public static EventProgress ProgressOf(GroveEvent season) => SeasonLedger.ProgressOf(season);

        /// <summary>
        /// The season whose box the hub should show, or null.
        ///
        /// <para>
        /// <b>The oldest season still holding a chest nobody has taken, and failing that the
        /// live one.</b> Rewards are claimed by hand, so a window closing must not take an
        /// earned chest with it — the marks stop growing at the deadline, the chests do not
        /// expire (invariant 47c), and the box is the only way back to the page holding them.
        /// </para>
        /// <para>
        /// <b>Oldest first is the streak's own rule</b> (invariant 48b: only the earliest
        /// waiting night may be taken), and a repeating season is what made it necessary here.
        /// The old rule was "the live one, or when nothing is running the most recent closed one
        /// still owing something" — correct while seasons were authored one at a time, because a
        /// closed one had nothing standing in front of it. On a calendar that never ends there is
        /// <em>always</em> something live, so that rule would have made last season's unopened
        /// chest unreachable from the moment the next one opened: no box, no page, and a badge
        /// counting a chest with nowhere to go (<see cref="Waiting"/> has always counted the
        /// whole calendar).
        /// </para>
        /// <para>
        /// In the ordinary case the live season is itself the oldest one owing anything, so the
        /// box shows what it always showed. It points backwards only while a season the player
        /// has finished with still owes them something, and it stops the moment they take it.
        /// </para>
        /// </summary>
        public static GroveEvent Featured
        {
            get
            {
                var all = All;
                var live = Live;

                if (all == null || all.Count == 0) return live;

                // `All` is in start order, so the first hit is the oldest.
                for (int i = 0; i < all.Count; i++)
                {
                    var candidate = all[i];
                    if (candidate == null || !candidate.IsValid) continue;
                    if (!ProgressOf(candidate).AnyWaiting) continue;

                    return candidate;
                }

                return live;
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
