using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Events
{
    /// <summary>
    /// How far each event's reward track has been handed over, and the act of handing one
    /// over.
    ///
    /// <para>
    /// <b>Why this exists at all.</b> <see cref="EventLedger"/> argues at length that an
    /// event needs no stored state, and it was right about the arithmetic: an event's
    /// progress is a count of dated clears and its payout is a pure function of that count.
    /// What it could not settle is where the reward <em>arrives</em>. Folded straight into
    /// derived earnings, a milestone is a number that moves behind the defeat screen of the
    /// run that crossed it — the same mistake the streak shipped and then undid in save
    /// schema v10. So the arithmetic stays derived and only the moment moves: a milestone is
    /// reached by playing and collected by tapping, and this is the one number that records
    /// which of the two has happened.
    /// </para>
    /// <para>
    /// <b>It is a floor, not a set.</b> One integer per event — the largest goal already
    /// taken — for the reason invariant 11b gives and the streak's
    /// <c>collectedThroughDay</c> demonstrates: it only ever rises, so two devices join with
    /// <c>max</c> and no rule anywhere has to decide which of them is behind. A set of
    /// collected rungs would be mergeable too, but a floor makes "tapping a later rung takes
    /// the earlier ones with it" the only representable behaviour rather than a rule
    /// somebody has to remember, and that is the only reading which cannot silently drop a
    /// reward.
    /// </para>
    /// <para>
    /// <b>Nothing here can pay twice, and that is structural.</b> Collecting raises a floor
    /// which raises a <em>derived</em> total; a balance is <c>max(derived, earnedFloor)</c>
    /// plus grants minus spends. Every failure this feature could have — an unseeded file,
    /// a device that merges in clears the other had already been paid for, a save edited to
    /// claim a track that was never played — lands on the same side of that maximum. The
    /// worst outcome is a collect that pays nothing visible, which is why the seeding below
    /// is a courtesy rather than a safeguard.
    /// </para>
    /// <para>
    /// <b>The forging bound.</b> The floor is written by the client, so it can be edited. It
    /// buys nothing: the server clamps it to the milestones the star ledger actually
    /// supports before paying — see <c>eventCredits</c> in <c>functions/src/progression.ts</c>
    /// — so the most a forged floor can extract is exactly what an honest player who
    /// collected everything already gets. That is invariant 13's first category, a derivable
    /// reward, with the client choosing only <em>when</em> inside a range the server owns.
    /// </para>
    /// </summary>
    public static class EventCollection
    {
        static readonly Dictionary<string, int> _floors = new Dictionary<string, int>(StringComparer.Ordinal);

        static bool _seeded;

        /// <summary>Raised when a track's floor moved. The cue a page redraws on.</summary>
        public static event Action Changed;

        /// <summary>
        /// Raised when a milestone was handed over, carrying the event and the rung.
        /// For the page drawing the flower at the time; nothing depends on it.
        /// </summary>
        public static event Action<GroveEvent, EventMilestone> Collected;

        // ------------------------------------------------------------- reading
        /// <summary>The largest goal already collected on this event. 0 when none.</summary>
        public static int CollectedGoal(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return 0;
            return _floors.TryGetValue(eventId, out int goal) ? goal : 0;
        }

        /// <summary>
        /// Every floor, for the one caller that needs them all at once.
        ///
        /// <see cref="Progression.ProgressionLedger"/> derives the whole calendar's payout in
        /// a single pass and cannot afford a lookup per event that walks a facade back to a
        /// dictionary. Handed out read-only rather than copied: it is read inside a
        /// recompute that already runs on every star.
        /// </summary>
        public static IReadOnlyDictionary<string, int> Floors => _floors;

        /// <summary>True when this rung's reward has already been taken.</summary>
        public static bool IsCollected(GroveEvent groveEvent, EventMilestone milestone)
            => groveEvent != null && milestone.Goal <= CollectedGoal(groveEvent.Id);

        /// <summary>
        /// True when tapping this rung would hand something over: reached by play, and not
        /// yet taken.
        /// </summary>
        public static bool IsCollectable(GroveEvent groveEvent, EventMilestone milestone,
                                         IReadOnlyDictionary<LevelId, LevelRecord> records)
        {
            if (groveEvent == null || !groveEvent.IsValid) return false;
            if (milestone.Goal <= CollectedGoal(groveEvent.Id)) return false;

            return EventLedger.Finished(groveEvent, records) >= milestone.Goal;
        }

        // ------------------------------------------------------------- writing
        /// <summary>
        /// Hands over every uncollected rung of <paramref name="groveEvent"/> up to and
        /// including <paramref name="goal"/>, and returns how many that swept.
        ///
        /// <para>
        /// The floor is raised once, at the end, rather than a rung at a time — the opposite
        /// of <c>DailyStreak.Collect</c>, and for a reason rather than by oversight. A streak
        /// rung <em>grants</em>: it puts hearts in a save, which cannot be recomputed, so a
        /// process killed mid-sweep must not come back able to grant them again. An event
        /// rung grants nothing. It raises a floor that a derived total reads, so the whole
        /// sweep is one idempotent assignment and a process killed halfway simply comes back
        /// with the rungs still waiting.
        /// </para>
        /// <para>
        /// The count that comes back is what the animation needs; the payout it should show
        /// is the difference between the derived totals either side of the call, which the
        /// caller can read from <see cref="Progression.PlayerProgression"/>. Deliberately not
        /// returned from here: this type does not know the reward table, the golden bands or
        /// the earned floor, and a number it computed itself would be a second opinion about
        /// what the player was paid.
        /// </para>
        /// </summary>
        public static int Collect(GroveEvent groveEvent, int goal,
                                  IReadOnlyDictionary<LevelId, LevelRecord> records)
        {
            if (groveEvent == null || !groveEvent.IsValid) return 0;

            int floor = CollectedGoal(groveEvent.Id);
            if (goal <= floor) return 0;

            // Only rungs the player has actually reached. Tapping is the trigger, play is
            // the entitlement, and the two are checked separately so a stale button on a
            // page that has not repainted cannot collect a rung ahead of the glades.
            int finished = EventLedger.Finished(groveEvent, records);
            if (finished < goal) return 0;

            int swept = 0;
            int through = floor;

            for (int i = 0; i < groveEvent.Milestones.Count; i++)
            {
                var milestone = groveEvent.Milestones[i];
                if (milestone.Goal <= floor || milestone.Goal > goal) continue;

                swept++;
                if (milestone.Goal > through) through = milestone.Goal;

                Telemetry.Track("event_collected", "event", groveEvent.Id,
                                "goal", milestone.Goal, "credits", milestone.Credits);

                try { Collected?.Invoke(groveEvent, milestone); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }

            if (swept == 0) return 0;

            _floors[groveEvent.Id] = through;

            SaveService.Save();
            Raise();

            return swept;
        }

        /// <summary>
        /// Marks everything already reached as already collected, once, on the first launch
        /// of a build that collects by hand.
        ///
        /// <para>
        /// Pre-v11 an event paid the instant a glade was cleared. A file written then has
        /// already had every reached milestone folded into its earned floor, so leaving the
        /// floors at zero would show a returning player a track full of rewards they have in
        /// fact been paid — and tapping them would move no number, because the derived total
        /// would climb back to a floor it never left. Nothing is lost either way; what is at
        /// stake is whether the page tells the truth.
        /// </para>
        /// <para>
        /// It runs when the catalog is there to be read rather than during the save
        /// migration, because a migration cannot see content: the file is loaded before the
        /// manifest and would have no idea which events exist or how far through them the
        /// player is.
        /// </para>
        /// </summary>
        public static void SeedIfNeeded(IReadOnlyList<GroveEvent> events,
                                        IReadOnlyDictionary<LevelId, LevelRecord> records)
        {
            if (_seeded || events == null) return;

            for (int i = 0; i < events.Count; i++)
            {
                var groveEvent = events[i];
                if (groveEvent == null || !groveEvent.IsValid) continue;

                int finished = EventLedger.Finished(groveEvent, records);
                int reached = 0;

                for (int m = 0; m < groveEvent.Milestones.Count; m++)
                {
                    var milestone = groveEvent.Milestones[m];
                    if (finished < milestone.Goal) break;
                    if (milestone.Goal > reached) reached = milestone.Goal;
                }

                if (reached <= 0) continue;
                if (reached > CollectedGoal(groveEvent.Id)) _floors[groveEvent.Id] = reached;
            }

            _seeded = true;

            // Marked rather than written: this runs inside the first derivation, possibly
            // mid-frame, and the next ordinary save carries it. Losing the flag costs one
            // repeat of a pass that is idempotent anyway.
            SaveService.MarkDirty();
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            _floors.Clear();
            _seeded = dto != null && dto.eventsSeeded;

            Absorb(_floors, dto?.events);

            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.eventsSeeded = _seeded;
            dto.events = Rows(_floors);
        }

        /// <summary>
        /// Unions two calendars of floors, taking the larger of each.
        ///
        /// A join in the strict sense — idempotent and order-independent — because every
        /// value in it only rises. An event one device has never heard of is carried through
        /// untouched, which is what lets a client on last month's content sync with one that
        /// has the new calendar without either of them losing a track.
        /// </summary>
        public static EventStateDto[] Join(EventStateDto[] mine, EventStateDto[] other)
        {
            // No early return for an empty side, deliberately. Handing one array straight
            // back would skip the sort and the deduplication, so a malformed file joined
            // against nothing would come out still malformed — and <c>SaveDelta</c> walks
            // these in order, so it would then read as changed on every single sync.
            var byId = new Dictionary<string, int>(StringComparer.Ordinal);
            Absorb(byId, mine);
            Absorb(byId, other);

            return Rows(byId);
        }

        /// <summary>
        /// The floors as rows, <b>sorted by id</b>.
        ///
        /// Not tidiness. <see cref="SaveChecksum"/> hashes the serialised file and
        /// <c>SaveDelta</c> decides whether to sync by walking these in order, so rows in
        /// dictionary order would make an unchanged save look changed on every launch —
        /// a write and an upload for nothing, forever.
        /// </summary>
        static EventStateDto[] Rows(Dictionary<string, int> floors)
        {
            if (floors == null || floors.Count == 0) return Array.Empty<EventStateDto>();

            var ids = new List<string>(floors.Keys);
            ids.Sort(StringComparer.Ordinal);

            var rows = new EventStateDto[ids.Count];
            for (int i = 0; i < ids.Count; i++)
                rows[i] = new EventStateDto { id = ids[i], collectedGoal = floors[ids[i]] };

            return rows;
        }

        static void Absorb(Dictionary<string, int> into, EventStateDto[] rows)
        {
            if (rows == null) return;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.id)) continue;

                int goal = row.collectedGoal < 0 ? 0 : row.collectedGoal;

                // Two rows for one event is a malformed file, not two tracks. The larger
                // wins for the same reason the merge takes the larger: a floor only rises,
                // so the bigger number is the one that knows more.
                if (into.TryGetValue(row.id, out int held) && held >= goal) continue;

                into[row.id] = goal;
            }
        }

        /// <summary>Test seam: forgets everything, as a fresh install would.</summary>
        internal static void ResetForTests()
        {
            _floors.Clear();
            _seeded = false;
        }
    }
}
