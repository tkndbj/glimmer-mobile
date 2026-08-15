using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Events
{
    /// <summary>How far through one event's track a player is, and what that is worth.</summary>
    public readonly struct EventProgress
    {
        /// <summary>Glades finished inside the window.</summary>
        public readonly int Finished;

        /// <summary>Milestones reached.</summary>
        public readonly int Milestones;

        /// <summary>Credits the track has paid, already inside derived earnings.</summary>
        public readonly long Credits;

        /// <summary>Glades still needed for the next milestone, or 0 when the track is done.</summary>
        public readonly int ToNext;

        /// <summary>The goal of the next milestone, or 0 when the track is done.</summary>
        public readonly int NextGoal;

        public EventProgress(int finished, int milestones, long credits, int toNext, int nextGoal)
        {
            Finished = finished;
            Milestones = milestones;
            Credits = credits;
            ToNext = toNext;
            NextGoal = nextGoal;
        }

        public static readonly EventProgress None = new EventProgress(0, 0, 0, 0, 0);

        public bool IsComplete => NextGoal == 0;
    }

    /// <summary>
    /// What an event has paid a player, derived from the glades they finished inside its
    /// window.
    ///
    /// <para>
    /// <b>Nothing is stored, claimed or granted, and that is the entire design.</b> An
    /// event's progress is a count of level records whose first clear falls between the
    /// event's two timestamps — facts already in the save file for a completely different
    /// reason — so the reward folds straight into the derived earnings that
    /// <see cref="Progression.ProgressionLedger"/> already computes and the server already
    /// recomputes on every sync. There is no claim to reject, no id to resubmit forever, no
    /// new save section, and nothing whatever to merge. An event is the only feature in
    /// this game that could be added without touching the save schema, and it is worth
    /// understanding why before changing it: the moment an event needs its own stored
    /// state, it needs a merge rule, and the merge rule is where features here go wrong.
    /// </para>
    /// <para>
    /// It is also permanent in the right way. A window that has closed cannot reopen, and a
    /// first clear inside it never moves — <c>SaveMerge</c> keeps the earliest — so an
    /// event a player finished last spring still pays them today, and the derived total
    /// never falls. That monotonicity is what lets the reward be derived rather than banked.
    /// </para>
    /// <para>
    /// <b>The honest limit.</b> <c>firstClearedUnix</c> is written by the client, so a
    /// player who edits their save can date an old clear into an event window. That is the
    /// same trust model the star ledger already has — the server believes a save's records
    /// or it believes nothing — and it is bounded the same way: the track pays what the
    /// manifest says and not a coin more, and no amount of forgery produces currency the
    /// event was not going to pay somebody who played it. Closing it properly means the
    /// server timestamping clears itself, which is a change to the sync and not to this.
    /// </para>
    /// </summary>
    public static class EventLedger
    {
        /// <summary>
        /// How many of an event's glades were first cleared inside its window.
        ///
        /// <para>
        /// <b>First</b> clear, deliberately. Counting any clear would let a player finish
        /// the same four glades on the last day of every event forever, which turns a
        /// calendar into a chore list; counting the first is what makes an event about
        /// glades the player had not got to yet.
        /// </para>
        /// </summary>
        public static int Finished(GroveEvent groveEvent, IReadOnlyDictionary<LevelId, LevelRecord> records)
        {
            if (groveEvent == null || !groveEvent.IsValid || records == null) return 0;

            int finished = 0;

            for (int i = 0; i < groveEvent.Levels.Count; i++)
            {
                if (!records.TryGetValue(groveEvent.Levels[i], out var record)) continue;
                if (record == null || !record.IsCleared) continue;

                long at = record.FirstClearedUnix;
                if (at < groveEvent.StartUnix || at >= groveEvent.EndUnix) continue;

                finished++;
            }

            return finished;
        }

        /// <summary>
        /// The whole state of one event's track for one player.
        ///
        /// Milestones are assumed sorted by goal, which the reader guarantees — an
        /// out-of-order track is refused there rather than sorted here, because a track
        /// whose rungs were silently reordered is a track paying different rewards than the
        /// one that was authored.
        /// </summary>
        public static EventProgress ProgressOf(GroveEvent groveEvent,
                                               IReadOnlyDictionary<LevelId, LevelRecord> records)
        {
            if (groveEvent == null || !groveEvent.IsValid) return EventProgress.None;

            int finished = Finished(groveEvent, records);

            int reached = 0;
            long credits = 0;
            int nextGoal = 0;

            for (int i = 0; i < groveEvent.Milestones.Count; i++)
            {
                var milestone = groveEvent.Milestones[i];

                if (finished >= milestone.Goal)
                {
                    reached++;
                    credits += milestone.Credits;
                    continue;
                }

                nextGoal = milestone.Goal;
                break;
            }

            int toNext = nextGoal == 0 ? 0 : nextGoal - finished;
            return new EventProgress(finished, reached, credits, toNext < 0 ? 0 : toNext, nextGoal);
        }

        /// <summary>
        /// What every event has paid, across the whole calendar.
        ///
        /// Folded into derived earnings by <c>ProgressionLedger</c>. Events that have not
        /// started yet contribute nothing and events that have ended contribute exactly
        /// what they always did, so this is monotonic over time as well as over records —
        /// which is what the earned floor needs it to be.
        /// </summary>
        public static long CreditsFrom(IReadOnlyList<GroveEvent> events,
                                       IReadOnlyDictionary<LevelId, LevelRecord> records)
        {
            if (events == null || records == null) return 0;

            long credits = 0;
            for (int i = 0; i < events.Count; i++) credits += ProgressOf(events[i], records).Credits;
            return credits;
        }
    }
}
