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

        /// <summary>Milestones reached by play, collected or not.</summary>
        public readonly int Milestones;

        /// <summary>Credits the track has paid, already inside derived earnings.</summary>
        public readonly long Credits;

        /// <summary>Glades still needed for the next milestone, or 0 when the track is done.</summary>
        public readonly int ToNext;

        /// <summary>The goal of the next milestone, or 0 when the track is done.</summary>
        public readonly int NextGoal;

        /// <summary>
        /// Milestones reached but not yet taken. What a badge counts, and the reason the
        /// hub keeps an event's box after the window closes.
        /// </summary>
        public readonly int Waiting;

        /// <summary>
        /// Credits sitting in reached-but-uncollected milestones.
        ///
        /// Not in <see cref="Credits"/> and deliberately not added to it anywhere: the two
        /// answer different questions, and the whole point of collecting by hand is that
        /// "earned" and "in your purse" stop being the same number for a while.
        /// </summary>
        public readonly long WaitingCredits;

        public EventProgress(int finished, int milestones, long credits, int toNext, int nextGoal,
                             int waiting = 0, long waitingCredits = 0)
        {
            Finished = finished;
            Milestones = milestones;
            Credits = credits;
            ToNext = toNext;
            NextGoal = nextGoal;
            Waiting = waiting;
            WaitingCredits = waitingCredits;
        }

        public static readonly EventProgress None = new EventProgress(0, 0, 0, 0, 0);

        public bool IsComplete => NextGoal == 0;

        /// <summary>Whether anything is waiting to be taken, for a line that mentions it.</summary>
        public bool AnyWaiting => Waiting > 0;
    }

    /// <summary>
    /// What an event has paid a player, derived from the glades they finished inside its
    /// window.
    ///
    /// <para>
    /// <b>Nothing is claimed or granted, and that is still the entire design.</b> An event's
    /// progress is a count of level records whose first clear falls between the event's two
    /// timestamps — facts already in the save file for a completely different reason — so
    /// the reward folds straight into the derived earnings that
    /// <see cref="Progression.ProgressionLedger"/> already computes and the server already
    /// recomputes on every sync. There is no claim to reject and no id to resubmit forever.
    /// </para>
    /// <para>
    /// <b>One thing is stored, and it is not the reward.</b> Save schema v11 added a floor
    /// per event — the largest milestone goal the player has actually <em>taken</em> — so
    /// that a rung arrives when it is tapped rather than while a defeat screen is up. That
    /// is the one change v10 made to the streak, applied here for the same reason and in the
    /// same shape. Note what did not change: the payout is still derived, still recomputed
    /// by the server, and still bounded below by the wallet's earned floor. The stored
    /// number does not say what the player is owed, only how much of it they have asked for,
    /// and the server clamps it to the milestones the star ledger supports before paying —
    /// so the worst a forged floor can do is collect early what was already coming. See
    /// <see cref="EventCollection"/>, which owns the rule.
    /// </para>
    /// <para>
    /// The floor arrives as a parameter rather than being read from that type, so everything
    /// here stays a pure function of its arguments. That is what lets the reward vectors pin
    /// both sides of the client/server pair against the same table.
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
        /// <para>
        /// Milestones are assumed sorted by goal, which the reader guarantees — an
        /// out-of-order track is refused there rather than sorted here, because a track
        /// whose rungs were silently reordered is a track paying different rewards than the
        /// one that was authored.
        /// </para>
        /// <para>
        /// <paramref name="collectedGoal"/> is the player's floor for this event: every rung
        /// asking for that many glades or fewer has been handed over and is inside their
        /// balance, and everything past it is reached but waiting. It is <b>clamped to the
        /// glades actually finished</b> before it is used, which is the whole security
        /// property — the number is written by the client, and clamping it here means the
        /// most an edited one can do is take early what play had already earned. The server
        /// applies the identical clamp; see invariant 13.
        /// </para>
        /// </summary>
        public static EventProgress ProgressOf(GroveEvent groveEvent,
                                               IReadOnlyDictionary<LevelId, LevelRecord> records,
                                               int collectedGoal)
        {
            if (groveEvent == null || !groveEvent.IsValid) return EventProgress.None;

            int finished = Finished(groveEvent, records);
            int floor = collectedGoal < 0 ? 0 : collectedGoal > finished ? finished : collectedGoal;

            int reached = 0;
            long credits = 0;
            int waiting = 0;
            long waitingCredits = 0;
            int nextGoal = 0;

            for (int i = 0; i < groveEvent.Milestones.Count; i++)
            {
                var milestone = groveEvent.Milestones[i];

                if (finished >= milestone.Goal)
                {
                    reached++;

                    if (milestone.Goal <= floor) credits += milestone.Credits;
                    else { waiting++; waitingCredits += milestone.Credits; }

                    continue;
                }

                nextGoal = milestone.Goal;
                break;
            }

            int toNext = nextGoal == 0 ? 0 : nextGoal - finished;
            return new EventProgress(finished, reached, credits, toNext < 0 ? 0 : toNext, nextGoal,
                                     waiting, waitingCredits);
        }

        /// <summary>
        /// What every event has paid, across the whole calendar.
        ///
        /// <para>
        /// Folded into derived earnings by <c>ProgressionLedger</c>. Events that have not
        /// started yet contribute nothing and events that have ended contribute exactly
        /// what they always did, so this is monotonic over time as well as over records —
        /// which is what the earned floor needs it to be. Collecting is monotonic for the
        /// same reason: a floor only rises.
        /// </para>
        /// <para>
        /// <b>A null <paramref name="collected"/> pays nothing</b>, which is the safe
        /// default rather than an oversight. A caller who has not been given the floors
        /// cannot know what has been taken, and this file's standing bargain — stated in
        /// <c>earnedCredits</c> on the server too — is that understating is recoverable
        /// through the wallet's earned floor while a giveaway is not.
        /// </para>
        /// </summary>
        public static long CreditsFrom(IReadOnlyList<GroveEvent> events,
                                       IReadOnlyDictionary<LevelId, LevelRecord> records,
                                       IReadOnlyDictionary<string, int> collected)
        {
            if (events == null || records == null || collected == null) return 0;

            long credits = 0;

            for (int i = 0; i < events.Count; i++)
            {
                var groveEvent = events[i];
                if (groveEvent == null) continue;

                collected.TryGetValue(groveEvent.Id ?? string.Empty, out int floor);
                credits += ProgressOf(groveEvent, records, floor).Credits;
            }

            return credits;
        }
    }
}
