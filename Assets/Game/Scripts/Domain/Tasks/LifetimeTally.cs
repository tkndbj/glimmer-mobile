using System;
using System.Collections.Generic;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Tasks
{
    /// <summary>
    /// What this account has ever done, counted per <see cref="TaskGoal"/> and never reset.
    ///
    /// <para>
    /// <b>The same counted verbs as the task slates, at a third cadence.</b> A day and a week
    /// are windows that reset and deal a slate; "for ever" is a window that does neither, so it
    /// is not a <see cref="TaskPeriod"/> and deliberately not a member of that enum — the
    /// rotation, the reset clock and every claim id are facts about a window that ends. What it
    /// shares is the registry: one hook in <see cref="TaskLedger.Note"/> feeds both, so every
    /// call site that already reports a verb reports it here too, and a verb added for a future
    /// mode is countable for ever the day it is countable for a task. There is one list of
    /// counted verbs in this game rather than two that drift.
    /// </para>
    /// <para>
    /// <b>Monotone, so it merges</b> (invariant 11b). Every row is a count of things that
    /// happened rather than a level held, so the join is a per-goal <c>max</c> and two devices
    /// are never ambiguous. It is the one count in this file that may be stored because it is
    /// the one that cannot fall.
    /// </para>
    /// <para>
    /// <b>It rides inside the existing <c>tasks</c> map</b>, which is invariant 12a's other
    /// half: <c>hasOnly</c> is an allow-list over the <em>top level</em> of the document, so a
    /// key added inside a map already listed there costs no <c>firestore.rules</c> release and
    /// has no deploy ordering at all — an old ruleset accepts it and a new one bounds it. That
    /// is also why it is not a section of its own, and the reason it belongs here rather than
    /// beside the ranks that read it.
    /// </para>
    /// <para>
    /// <b>It buys nothing.</b> Currency derives from the star ledger and from nothing else
    /// (invariant 9), so a forged row moves a badge and never a balance — invariant 13's fourth
    /// clause, which is what makes a client-written count safe here at all. The <see
    /// cref="Ceiling"/> is the bound that keeps a forged one inside an honest range.
    /// </para>
    /// </summary>
    public static class LifetimeTally
    {
        static readonly Dictionary<TaskGoal, int> _counts = new Dictionary<TaskGoal, int>();

        /// <summary>
        /// The most any one counter may reach.
        ///
        /// <para>
        /// <b>A sanity bound rather than tuning</b>, and deliberately not read off the live
        /// ladder the way <c>TaskLedger.CeilingFor</c> reads off the slate. A period's counter
        /// may be clamped to the largest target on its slate because the period resets and the
        /// clamp heals itself within a day; this one never resets, so a ceiling derived from
        /// today's ladder would quietly destroy the real figure the moment a content push raised
        /// a target, leaving a progress bar frozen below a line it had already crossed. So the
        /// bound is absolute, far above anything a requirement could reasonably ask, and low
        /// enough that no single note can overflow the <c>int</c> this travels as.
        /// <c>check_ranks</c> refuses a requirement above it.
        /// </para>
        /// </summary>
        public const int Ceiling = 999_999_999;

        /// <summary>
        /// The most rows this may write, pinned to <see cref="TaskLedger.MaxGoals"/> because it
        /// is the same registry and travels under the same rules bound (invariant 12b).
        /// </summary>
        public const int MaxGoals = TaskLedger.MaxGoals;

        /// <summary>Raised when a counter moves. Screens follow this rather than polling.</summary>
        public static event Action Changed;

        // ------------------------------------------------------------------ reading
        /// <summary>
        /// How many of <paramref name="goal"/> this account has ever done: the larger of what
        /// was counted and what the rest of the save proves.
        ///
        /// <para>
        /// <b>The floor is the whole reason this reads correctly on an account older than the
        /// feature.</b> A counter that starts at nought on the day it ships tells a player who
        /// has cleared sixty glades that they have played no battles, and every rung asking
        /// about play is then a wall in front of exactly the players who earned it. Some of
        /// these facts are already provable from records the save has always kept — a cleared
        /// glade is a run that was played and won — so the reading is <c>max(counted, proved)</c>
        /// and the tally only ever has to carry what nothing else can.
        /// </para>
        /// <para>
        /// It stays monotone, which is what makes it legal at all: both halves only rise, so
        /// their maximum does too. And it is a <em>reading</em> rather than a write — nothing
        /// seeds the stored row from the floor, so no device ever pushes a number it did not
        /// count, and the merge has nothing new to decide (invariant 14a's floor, as a floor
        /// rather than as a payment).
        /// </para>
        /// </summary>
        public static long Count(TaskGoal goal)
        {
            if (goal == TaskGoal.None) return 0L;

            long counted = _counts.TryGetValue(goal, out int n) && n > 0 ? n : 0L;
            long proved = FloorFor(goal);
            return counted > proved ? counted : proved;
        }

        /// <summary>Only what was counted, with no floor under it. For the fixtures and the wire.</summary>
        internal static int Counted(TaskGoal goal)
            => _counts.TryGetValue(goal, out int n) && n > 0 ? n : 0;

        /// <summary>
        /// What the rest of the save already proves about a verb, or nought when it proves
        /// nothing.
        ///
        /// <para>
        /// Every entry here is a <em>lower bound</em> that is true by construction rather than
        /// an estimate: a glade cannot be cleared without a run being played and won, and the
        /// waves in <c>endlessBest</c> are waves that were seen off. A verb with no such proof —
        /// a raider felled, a charm sprung — answers nought and is counted from the day this
        /// ships, which is honest and is why the shipped ladder leans on the ones that are
        /// provable.
        /// </para>
        /// </summary>
        static long FloorFor(TaskGoal goal)
        {
            switch (goal)
            {
                // A cleared glade is a run that happened and a run that was won.
                case TaskGoal.Runs:
                case TaskGoal.Wins:
                    return PlayerProgress.ClearedCount;

                // The stars a ledger holds were earned at least once.
                case TaskGoal.Stars:
                    return PlayerProgress.TotalStars(Content.GameContent.Index);

                // Every wave in the Infinite lane's lifetime tally was seen off. It is a floor
                // rather than the answer because the goal also counts waves on the laddered
                // chapters, which nothing records per account.
                case TaskGoal.Waves:
                    return EndlessLedger.LifetimeWaves;

                default:
                    return 0L;
            }
        }

        // ------------------------------------------------------------------ writing
        /// <summary>
        /// Counts <paramref name="amount"/> more of a verb.
        ///
        /// <para>
        /// Called from <see cref="TaskLedger.Note"/> and from nowhere else, which is what makes
        /// this complete: every verb this game counts is reported there already, so a future
        /// mode that reports one gets the lifetime count without being taught about ranks.
        /// </para>
        /// <para>
        /// It marks the save dirty on its own rather than leaning on the period counters
        /// doing it, because a goal already at its slate's ceiling moves nothing there and
        /// would otherwise be counted for ever only until the app was next closed.
        /// </para>
        /// </summary>
        internal static void Note(TaskGoal goal, int amount)
        {
            if (goal == TaskGoal.None || amount <= 0) return;

            int before = Counted(goal);
            if (before >= Ceiling) return;

            long after = (long)before + amount;
            if (after > Ceiling) after = Ceiling;

            _counts[goal] = (int)after;

            SaveService.MarkDirty();
            Raise();
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        /// <summary>
        /// Reads the rows of one file. Shared by the load and the join, for
        /// <c>TaskLedger.Read</c>'s reason: two readers would eventually disagree about what a
        /// well-formed row is.
        /// </summary>
        static void Read(Dictionary<TaskGoal, int> into, TaskCountDto[] rows)
        {
            into.Clear();
            if (rows == null) return;

            foreach (var row in rows)
            {
                if (row == null || row.count <= 0) continue;

                // An unknown goal is dropped rather than carried, exactly as a period's is: it
                // is a row a newer build wrote and this one cannot count. Dropping it costs
                // that build its count on this device, which the other device's `max` still
                // holds — and unlike a companion or a purchase, nothing here is confiscated,
                // because a count buys nothing (invariant 13's fourth clause).
                var goal = TaskGoals.Parse(row.goal);
                if (goal == TaskGoal.None) continue;

                int value = row.count > Ceiling ? Ceiling : row.count;
                if (into.TryGetValue(goal, out int held) && held >= value) continue;
                into[goal] = value;
            }
        }

        internal static void LoadFrom(TaskCountDto[] rows)
        {
            Read(_counts, rows);
            Raise();
        }

        /// <summary>
        /// Sorted and capped, for the reason every id-keyed section here is: <c>SaveDelta</c>
        /// walks two arrays in order, so an unsorted writer makes every launch look changed; and
        /// a list one row over the rules' bound loses <em>every</em> save write (invariant 12b),
        /// so the writer caps what it sends rather than trusting the registry to stay short.
        /// </summary>
        internal static TaskCountDto[] Write() => Write(_counts);

        static TaskCountDto[] Write(Dictionary<TaskGoal, int> counts)
        {
            var rows = new List<TaskCountDto>(counts.Count);
            foreach (var pair in counts)
                if (pair.Value > 0)
                    rows.Add(new TaskCountDto { goal = TaskGoals.Id(pair.Key), count = pair.Value });

            rows.Sort((a, b) => string.CompareOrdinal(a.goal, b.goal));
            if (rows.Count > MaxGoals) rows.RemoveRange(MaxGoals, rows.Count - MaxGoals);

            return rows.ToArray();
        }

        /// <summary>
        /// Joins two devices' tallies: a per-goal <c>max</c>, with no key to decide and nothing
        /// to lose. The simplest merge in the save, and that is the point of the shape.
        /// </summary>
        internal static TaskCountDto[] Join(TaskCountDto[] mine, TaskCountDto[] other)
        {
            var a = new Dictionary<TaskGoal, int>();
            var b = new Dictionary<TaskGoal, int>();
            Read(a, mine);
            Read(b, other);

            foreach (var pair in b)
                if (!a.TryGetValue(pair.Key, out int held) || pair.Value > held)
                    a[pair.Key] = pair.Value;

            return Write(a);
        }

        /// <summary>Forgets everything. Dev only, and used by the wipe.</summary>
        internal static void Reset() => _counts.Clear();
    }
}
