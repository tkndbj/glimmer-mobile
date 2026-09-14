using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Daily;
using GlimmerGrove.Modes;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Tasks
{
    /// <summary>What a task row draws.</summary>
    public enum TaskState
    {
        /// <summary>Counting. The bar is what the player looks at.</summary>
        Open,

        /// <summary>Done and unclaimed. This is the one that shines.</summary>
        Ready,

        /// <summary>Paid. Waits for the next period.</summary>
        Claimed,
    }

    /// <summary>
    /// The tasks: what has been done this day and this week, which tasks that finishes,
    /// and what claiming one pays.
    ///
    /// <para>
    /// <b>The state is counters, never task progress.</b> Each period stores one count per
    /// <see cref="TaskGoal"/> — runs finished today, raiders felled this week — and a task's
    /// progress is <em>derived</em>: the count for its goal, clamped to its target. Storing
    /// per-task progress would be a second copy of the same fact, and the copy that breaks
    /// the moment the slate is retuned: a task added by a content push mid-week would start
    /// from nothing on a device that had already done the thing it asks for. A counter also
    /// merges by <c>max</c> without a special case (invariant 11b), which per-task progress
    /// under a rotation that can change does not.
    /// </para>
    /// <para>
    /// <b>The other stored thing is which task ids were claimed in the period</b>, a set
    /// joined by union: claiming cannot be undone, and a set of ids survives the slate being
    /// retuned underneath it. Together with the period key that is the whole of the save
    /// section (<see cref="TaskStateDto"/>); nothing about what a chest contained is stored
    /// anywhere, for <see cref="DailyChests"/>' reason — it is recomputed from the player, the
    /// period and the task whenever it is needed, on every device and on the server.
    /// </para>
    /// <para>
    /// <b>Currency is a claim with a derived id</b> (invariant 10a):
    /// <c>task:{period}:{key}:{id}:{currency}</c>, so two devices claiming one task produce
    /// one entry, and the server re-rolls the chest from the same three facts and pays its
    /// own answer. Hearts, boosts and utilities are banked here and now through
    /// <see cref="BankedDrop"/>, exactly as a daily chest's were.
    /// </para>
    /// </summary>
    public static class TaskLedger
    {
        /// <summary>The counters and claims of one period, mutable while the period lasts.</summary>
        sealed class PeriodState
        {
            public int Key;
            public readonly Dictionary<TaskGoal, int> Counts = new Dictionary<TaskGoal, int>();
            public readonly HashSet<string> Claimed = new HashSet<string>(StringComparer.Ordinal);

            public void Clear(int key)
            {
                Key = key;
                Counts.Clear();
                Claimed.Clear();
            }
        }

        static readonly PeriodState _daily = new PeriodState();
        static readonly PeriodState _weekly = new PeriodState();

        /// <summary>The seed tag, shared with the server. Contract (invariant 9c).</summary>
        public const string SeedTag = "task";

        /// <summary>
        /// The most goals a period may carry rows for, and the most claims. Pinned to the
        /// security rules' list bounds by a test, because a client writing a longer list
        /// loses <em>every</em> save write (invariant 12a).
        /// </summary>
        public const int MaxGoals = 32;
        public const int MaxClaimed = 32;

        /// <summary>
        /// Raised when a counter or a claim moves, including when a period rolls over on a
        /// read. Screens follow this rather than a timer, for <see cref="DailyChests"/>' reason.
        /// </summary>
        public static event Action Changed;

        /// <summary>
        /// Raised when a counter first reaches a dealt task's target, carrying the task.
        /// The hook a screen uses to celebrate; nothing depends on it.
        /// </summary>
        public static event Action<TaskDefinition> Completed;

        static TaskTable Table => ProgressionRules.Table.Tasks;

        static string PlayerKey => RewardSeed.PlayerKey;

        /// <summary>
        /// Whether a chest may be claimed yet. The daily chests' gate, for the daily chests'
        /// reason: a chest is rolled from the account id so the server can recompute it, and
        /// before the first sign-in there is no account id to roll from. See
        /// <see cref="RewardSeed.IsAdjudicable"/>.
        /// </summary>
        public static bool CanClaim => RewardSeed.IsAdjudicable;

        // ------------------------------------------------------------- reading
        public static int Key(TaskPeriod period)
        {
            Sync();
            return Of(period).Key;
        }

        /// <summary>The tasks dealt for the current period, in dealt order.</summary>
        public static List<TaskDefinition> Active(TaskPeriod period)
        {
            Sync();
            return Table.Active(period, Of(period).Key);
        }

        /// <summary>How much of a task's goal has been done this period. Unclamped.</summary>
        public static int Count(TaskPeriod period, TaskGoal goal)
        {
            Sync();
            return Of(period).Counts.TryGetValue(goal, out int n) ? n : 0;
        }

        /// <summary>A task's progress, clamped to its target.</summary>
        public static int Progress(TaskDefinition task)
        {
            if (task == null) return 0;
            int count = Count(task.Period, task.Goal);
            return count > task.Target ? task.Target : count;
        }

        public static bool IsClaimed(TaskDefinition task)
        {
            if (task == null) return false;
            Sync();
            return Of(task.Period).Claimed.Contains(task.Id);
        }

        public static TaskState StateOf(TaskDefinition task)
        {
            if (task == null) return TaskState.Open;
            if (IsClaimed(task)) return TaskState.Claimed;
            return Progress(task) >= task.Target ? TaskState.Ready : TaskState.Open;
        }

        /// <summary>Dealt tasks finished and not yet claimed, over both periods. The hub's badge.</summary>
        public static int ReadyCount
        {
            get
            {
                int ready = 0;
                foreach (var period in TaskPeriods.All)
                    foreach (var task in Active(period))
                        if (StateOf(task) == TaskState.Ready) ready++;
                return ready;
            }
        }

        /// <summary>Dealt tasks already paid this period.</summary>
        public static int ClaimedCount(TaskPeriod period)
        {
            int claimed = 0;
            foreach (var task in Active(period))
                if (StateOf(task) == TaskState.Claimed) claimed++;
            return claimed;
        }

        /// <summary>True when every dealt task of the period has been paid.</summary>
        public static bool IsComplete(TaskPeriod period)
        {
            var active = Active(period);
            return active.Count > 0 && ClaimedCount(period) >= active.Count;
        }

        public static long SecondsUntilReset(TaskPeriod period)
        {
            long now = GameClock.NowUnix();
            return period == TaskPeriod.Weekly
                ? WeeklyRules.SecondsUntilReset(now)
                : DailyRules.SecondsUntilReset(now);
        }

        /// <summary>
        /// What a task's chest holds, without claiming it. For the opening overlay and the
        /// collect animation, so both read one list. Empty for a task that is not dealt.
        /// </summary>
        public static List<ChestDrop> Preview(TaskDefinition task)
        {
            if (task == null) return new List<ChestDrop>();
            Sync();
            return task.Tier.Chest.Roll(SeedFor(task, Of(task.Period).Key));
        }

        /// <summary>
        /// The seed a task's chest is rolled from: the player, this feature, and the period
        /// and task that earned it. The subject layout is contract with the server's
        /// <c>subjectSeed</c>; see <see cref="ChestSeed"/>.
        /// </summary>
        public static ChestSeed SeedFor(TaskDefinition task, int key)
            => ChestSeed.ForSubject(PlayerKey, SeedTag, Subject(task.Period, key, task.Id));

        /// <summary>The subject half of the seed and of the claim id: <c>{period}:{key}:{id}</c>.</summary>
        public static string Subject(TaskPeriod period, int key, string taskId)
            => TaskPeriods.Id(period) + ":" + key + ":" + taskId;

        // ------------------------------------------------------------- counting
        /// <summary>
        /// Records that a goal happened, in both periods at once.
        ///
        /// <para>
        /// Both, because a run finished today is also a run finished this week; the two
        /// slates count the same world at two scales. Bounded per goal rather than per task,
        /// so a very long week cannot grow a number without limit — there is nothing above
        /// the largest target on the slate to earn, and a counter that only ever needs to
        /// reach a target has no business being a million.
        /// </para>
        /// </summary>
        public static void Note(TaskGoal goal, int amount = 1)
        {
            if (goal == TaskGoal.None || amount <= 0) return;
            Sync();

            bool moved = false;
            var finished = new List<TaskDefinition>();

            foreach (var period in TaskPeriods.All)
            {
                var state = Of(period);
                int before = state.Counts.TryGetValue(goal, out int n) ? n : 0;
                int cap = CeilingFor(period, goal);

                if (before >= cap) continue;

                int after = before + amount;
                if (after > cap) after = cap;

                state.Counts[goal] = after;
                moved = true;

                // Only a task that crossed its line on this note is news. One that was already
                // over it would otherwise be announced on every raider felled for a week.
                foreach (var task in Table.Active(period, state.Key))
                    if (task.Goal == goal && before < task.Target && after >= task.Target)
                        finished.Add(task);
            }

            if (!moved) return;

            SaveService.MarkDirty();
            Raise();

            foreach (var task in finished)
            {
                Telemetry.Track("task_completed",
                                "period", TaskPeriods.Id(task.Period),
                                "key", Of(task.Period).Key,
                                "task", task.Id,
                                "tier", task.Tier.Id);

                try { Completed?.Invoke(task); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
        }

        /// <summary>
        /// The most a counter needs to reach: the largest target any task on the slate asks of
        /// this goal, retired ones included, or a floor of one so a goal nothing asks for
        /// still records that it happened once. Read from the slate rather than fixed, so a
        /// retune that raises a target is never capped by a number written here.
        /// </summary>
        static int CeilingFor(TaskPeriod period, TaskGoal goal)
        {
            int cap = 1;
            foreach (var task in Table.Slate(period))
                if (task.Goal == goal && task.Target > cap) cap = task.Target;
            return cap;
        }

        /// <summary>
        /// The facts every mode reports when a run ends: that it ended, how it ended, and how
        /// it was graded. Called from <see cref="RunLedger"/> beside the streak, so a mode
        /// that ends a run without going through the ledger counts for nothing here either.
        /// </summary>
        public static void RecordRun(RunOutcome run)
        {
            Note(TaskGoal.Runs);
            if (!run.Won) return;

            Note(TaskGoal.Wins);
            Note(TaskGoal.Stars, run.Stars);
            if (run.Stars >= 3) Note(TaskGoal.ThreeStars);
        }

        /// <summary>
        /// The facts only a siege can report: what happened on the hill and on the field.
        /// Called where the board is still in reach, which the mode-blind ledger is not.
        /// </summary>
        public static void RecordSiege(SiegeAttention seen, int wavesCleared, int matches)
        {
            if (seen == null) return;

            Note(TaskGoal.Matches, matches);
            Note(TaskGoal.Raiders, seen.RaidersFelled);
            Note(TaskGoal.Bosses, seen.BossesFelled);
            Note(TaskGoal.Charms, seen.CharmsSprung);
            Note(TaskGoal.Cogs, seen.CogsTaken);
            Note(TaskGoal.Bombs, seen.BombsTapped);
            Note(TaskGoal.Waves, wavesCleared);
        }

        // ------------------------------------------------------------- claiming
        /// <summary>
        /// Claims a finished task and applies what its chest holds.
        ///
        /// <para>
        /// Two independent guards stop a task paying twice, which is the failure that
        /// matters most here. The claimed set refuses a second attempt at the same id; and
        /// every currency award carries an id derived from the period and the task, so even
        /// a save file edited to forget the claim collides with an entry already in the
        /// ledger, and the server refuses it a third time on top.
        /// </para>
        /// </summary>
        public static bool TryClaim(TaskDefinition task, out List<ChestDrop> drops)
        {
            drops = null;
            if (task == null) return false;
            Sync();

            // Only a dealt task can be claimed. A task finished under yesterday's slate and
            // not claimed before midnight is gone with the day — a chest that banks is a chest
            // nobody has to come back tomorrow for, which is DailyChests' rule kept.
            if (!IsDealt(task)) return false;
            if (StateOf(task) != TaskState.Ready) return false;

            // Checked here as well as in the UI. A reward the server would recompute
            // differently must not be claimable through any path, and a guard that lives
            // only in a screen is a guard the next screen forgets.
            if (!CanClaim) return false;

            var state = Of(task.Period);
            var rolled = task.Tier.Chest.Roll(SeedFor(task, state.Key));

            state.Claimed.Add(task.Id);
            Apply(rolled, task, state.Key);

            SaveService.Save();
            Raise();

            Telemetry.Track("task_claimed",
                            "period", TaskPeriods.Id(task.Period),
                            "key", state.Key,
                            "task", task.Id,
                            "tier", task.Tier.Id,
                            "drops", Describe(rolled));

            drops = rolled;
            return true;
        }

        static bool IsDealt(TaskDefinition task)
        {
            foreach (var dealt in Active(task.Period))
                if (string.Equals(dealt.Id, task.Id, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Hands out one chest's contents: banked kinds here and now, currency as a claim.
        /// The split <see cref="DailyChests"/> drew, for its reason — currency is the thing
        /// an attacker forges, so it is the thing the server adjudicates.
        /// </summary>
        static void Apply(List<ChestDrop> drops, TaskDefinition task, int key)
        {
            long now = GameClock.NowUnix();

            for (int i = 0; i < drops.Count; i++)
            {
                var drop = drops[i];
                if (!drop.IsValid) continue;
                if (BankedDrop.Apply(drop)) continue;
                if (!drop.IsCurrency) continue;

                string currency = ChestDropKinds.CurrencyOf(drop.Kind);
                PlayerProgression.Award(
                    currency, drop.Amount,
                    GrantEntry.TaskChestId(task.Period, key, task.Id, currency),
                    GrantEntry.TaskChestReason, now);
            }
        }

        static string Describe(List<ChestDrop> drops)
        {
            var parts = new string[drops.Count];
            for (int i = 0; i < drops.Count; i++) parts[i] = drops[i].ToString();
            return string.Join(",", parts);
        }

        // ------------------------------------------------------------ the period
        static PeriodState Of(TaskPeriod period) => period == TaskPeriod.Weekly ? _weekly : _daily;

        /// <summary>
        /// Rolls the periods over when the calendar has moved on. Lazy, for
        /// <see cref="DailyChests"/>' reason: a comparison at the top of every read cannot be
        /// forgotten by a caller and cannot arrive late. Only ever forward — a merged save
        /// carrying a key from a device whose clock was ahead must not be reset back into a
        /// period it has already been paid for.
        /// </summary>
        static void Sync()
        {
            long now = GameClock.NowUnix();
            bool moved = false;

            int today = DailyRules.DayKeyFor(now);
            if (today > _daily.Key) { _daily.Clear(today); moved = true; }

            int week = WeeklyRules.WeekKeyFor(now);
            if (week > _weekly.Key) { _weekly.Clear(week); moved = true; }

            if (!moved) return;

            SaveService.MarkDirty();
            Raise();
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            Read(_daily, dto?.tasks?.daily);
            Read(_weekly, dto?.tasks?.weekly);
            Raise();
        }

        /// <summary>
        /// One place a row is judged, shared by the load and the join so the two cannot come
        /// to different conclusions about what a well-formed period looks like.
        /// </summary>
        static void Read(PeriodState state, TaskPeriodDto dto)
        {
            state.Clear(dto == null || dto.key < 0 ? 0 : dto.key);
            if (dto == null || state.Key <= 0) { state.Clear(0); return; }

            if (dto.counts != null)
            {
                foreach (var row in dto.counts)
                {
                    if (row == null || row.count <= 0) continue;

                    // An unknown goal is carried nowhere: it is a row a newer build wrote and
                    // this one cannot count, and dropping it costs a newer build one period's
                    // worth of that count on this device — which the join's max on the other
                    // device already holds.
                    var goal = TaskGoals.Parse(row.goal);
                    if (goal == TaskGoal.None) continue;

                    if (state.Counts.TryGetValue(goal, out int held) && held >= row.count) continue;
                    state.Counts[goal] = row.count;
                }
            }

            if (dto.claimed != null)
            {
                foreach (var id in dto.claimed)
                    if (TaskDefinition.IsValidId(id)) state.Claimed.Add(id);
            }
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            if (dto == null) return;
            dto.tasks = new TaskStateDto { daily = Write(_daily), weekly = Write(_weekly) };
        }

        /// <summary>
        /// Sorted, for the reason every id-keyed section here sorts: <c>SaveDelta</c> walks
        /// two arrays in order, so an unsorted writer makes every launch look changed and
        /// pushes a document write for ever.
        /// </summary>
        static TaskPeriodDto Write(PeriodState state)
        {
            var counts = new List<TaskCountDto>(state.Counts.Count);
            foreach (var pair in state.Counts)
                if (pair.Value > 0) counts.Add(new TaskCountDto { goal = TaskGoals.Id(pair.Key), count = pair.Value });
            counts.Sort((a, b) => string.CompareOrdinal(a.goal, b.goal));

            var claimed = new List<string>(state.Claimed);
            claimed.Sort(string.CompareOrdinal);

            return new TaskPeriodDto
            {
                key = state.Key,
                counts = counts.ToArray(),
                claimed = claimed.ToArray(),
            };
        }

        /// <summary>
        /// Joins two devices' periods.
        ///
        /// The later key wins outright — an older period's counters describe a period that is
        /// over, and carrying them forward would hand the player a head start on one they have
        /// not played. Within a shared period every count takes the larger value and the claims
        /// take the union: both are records of things that happened, the awards behind a claim
        /// are deduplicated by their own ids, and taking the smaller would let a task be paid
        /// twice. Written through the same reader as the load, so a malformed row is judged
        /// once.
        /// </summary>
        internal static TaskStateDto Join(TaskStateDto mine, TaskStateDto other)
            => new TaskStateDto
            {
                daily = JoinPeriod(mine?.daily, other?.daily),
                weekly = JoinPeriod(mine?.weekly, other?.weekly),
            };

        static TaskPeriodDto JoinPeriod(TaskPeriodDto a, TaskPeriodDto b)
        {
            var mine = new PeriodState();
            var other = new PeriodState();
            Read(mine, a);
            Read(other, b);

            if (mine.Key > other.Key) return Write(mine);
            if (other.Key > mine.Key) return Write(other);

            foreach (var pair in other.Counts)
                if (!mine.Counts.TryGetValue(pair.Key, out int held) || pair.Value > held)
                    mine.Counts[pair.Key] = pair.Value;

            foreach (var id in other.Claimed) mine.Claimed.Add(id);

            return Write(mine);
        }

        /// <summary>Forgets everything. Dev only, and used by the wipe.</summary>
        internal static void Reset()
        {
            _daily.Clear(0);
            _weekly.Clear(0);
        }
    }
}
