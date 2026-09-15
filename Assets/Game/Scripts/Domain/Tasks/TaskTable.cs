using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;

namespace GlimmerGrove.Tasks
{
    /// <summary>
    /// Which tasks are dealt on a given day or week.
    ///
    /// <para>
    /// A pure function of the period key and the slate, so every device and the server
    /// answer it identically with nothing stored: day <c>k</c> deals the slate's entries
    /// <c>k·n … k·n+n-1</c>, wrapping. Over a slate of ten dealt three at a time, ten
    /// consecutive periods show every task and no two consecutive periods share one, which
    /// is what "cycled every day" was asked to mean.
    /// </para>
    /// <para>
    /// <b>Global rather than per player, deliberately.</b> Everybody sees the same three on
    /// the same day, which is what lets a player talk about "today's tasks", lets support
    /// answer a ticket from the calendar alone, and costs the server nothing to check.
    /// Per-player rotation would buy variety nobody can see and a seed nobody can reason
    /// about.
    /// </para>
    /// <para>
    /// <b>Editing the slate moves the rotation</b>, and that is accepted rather than hidden:
    /// a content push that adds a task re-deals every period after it, and the server only
    /// <em>logs</em> a claim for a task that the current slate would not have dealt (13a) —
    /// the bound on a claim is the per-period allowance, never the rotation.
    /// </para>
    /// </summary>
    public static class TaskRotation
    {
        /// <summary>
        /// The slate indices dealt for <paramref name="key"/>, in dealt order. Fewer than
        /// <paramref name="perPeriod"/> when the slate is shorter than that.
        /// </summary>
        public static int[] Indices(int slateCount, int key, int perPeriod)
        {
            if (slateCount <= 0 || perPeriod <= 0) return new int[0];

            int n = perPeriod < slateCount ? perPeriod : slateCount;
            var picked = new int[n];

            // Long arithmetic, because a key in the tens of thousands times a per-period
            // count is comfortably inside an int today and this is a formula the server
            // mirrors for the life of the game.
            long start = (long)(key < 0 ? 0 : key) * perPeriod;

            for (int i = 0; i < n; i++) picked[i] = (int)((start + i) % slateCount);
            return picked;
        }
    }

    /// <summary>
    /// The task rules, immutable once built: the chest ladder, the two slates and how many of
    /// each slate are dealt at a time.
    ///
    /// <para>
    /// Content, for <see cref="DailyChestTable"/>'s reason and one more: the task slate is
    /// the live-ops surface of the game. A holiday week wants a different weekly slate, a
    /// chapter drop wants a task about its new boss, and a build that has to go through two
    /// store reviews to change either is a slate that never changes. It rides in
    /// <c>progression.json</c> as an optional block, so a client that predates it keeps its
    /// built-in table and a client that has it reads an older file and does the same.
    /// </para>
    /// <para>
    /// <b>Refused whole on a structural fault, degraded on an unknown</b> — the split
    /// <see cref="DailyChestTable.Resolve"/> draws. A tier with no floor, a task naming a
    /// tier that does not exist, or a duplicated id is a table that would pay something it
    /// cannot describe, so the built-in one stands; a task naming a goal this build has never
    /// heard of is a newer content pack reaching an older build, and is dropped by name.
    /// </para>
    /// </summary>
    public sealed class TaskTable
    {
        readonly ChestTier[] _tiers;
        readonly Dictionary<string, ChestTier> _tierById;
        readonly TaskDefinition[] _daily;
        readonly TaskDefinition[] _weekly;
        readonly Dictionary<string, TaskDefinition> _byId;

        TaskTable(int activePerPeriod, ChestTier[] tiers, TaskDefinition[] daily, TaskDefinition[] weekly)
        {
            ActivePerPeriod = activePerPeriod < 1 ? 1 : activePerPeriod;
            _tiers = tiers ?? new ChestTier[0];
            _daily = daily ?? new TaskDefinition[0];
            _weekly = weekly ?? new TaskDefinition[0];

            _tierById = new Dictionary<string, ChestTier>(StringComparer.Ordinal);
            foreach (var tier in _tiers) _tierById[tier.Id] = tier;

            _byId = new Dictionary<string, TaskDefinition>(StringComparer.Ordinal);
            foreach (var task in _daily) _byId[task.Id] = task;
            foreach (var task in _weekly) _byId[task.Id] = task;
        }

        /// <summary>How many of a slate are dealt per period. Three by default.</summary>
        public int ActivePerPeriod { get; }

        /// <summary>The chest ladder, humblest first.</summary>
        public IReadOnlyList<ChestTier> Tiers => _tiers;

        /// <summary>The whole authored slate for a period, retired entries included.</summary>
        public IReadOnlyList<TaskDefinition> Slate(TaskPeriod period)
            => period == TaskPeriod.Weekly ? _weekly : _daily;

        public ChestTier Tier(string id)
            => id != null && _tierById.TryGetValue(id, out var tier) ? tier : null;

        /// <summary>A task by id, retired or not. Null for an id this table has never held.</summary>
        public TaskDefinition Find(string id)
            => id != null && _byId.TryGetValue(id, out var task) ? task : null;

        /// <summary>
        /// The tasks dealt for one period key, in dealt order.
        ///
        /// Retired tasks are out of the rotation before the indices are taken, so retiring
        /// one re-deals the periods after it exactly as adding one does — the honest reading
        /// of "the slate changed", and the reason the server never refuses on the rotation.
        /// </summary>
        public List<TaskDefinition> Active(TaskPeriod period, int key)
        {
            var slate = Slate(period);
            var live = new List<TaskDefinition>(slate.Count);
            foreach (var task in slate) if (!task.Retired) live.Add(task);

            var picked = TaskRotation.Indices(live.Count, key, ActivePerPeriod);
            var active = new List<TaskDefinition>(picked.Length);
            foreach (int i in picked) active.Add(live[i]);
            return active;
        }

        // ------------------------------------------------------------- built in
        /// <summary>
        /// The table that ships inside the build, for a first launch that has not reached
        /// the content yet and for a malformed file. The shape is the design: every tier
        /// guarantees credits, so no chest is ever a disappointment; the utilities and hearts
        /// come in as the weighted pick, so what a chest is worth is a list that sums to a
        /// hundred; and each rung guarantees strictly more than the one below it.
        /// </summary>
        public static readonly TaskTable Default = BuildDefault();

        static TaskTable BuildDefault()
        {
            var wood = new ChestTier("wood", 1, new ChestDefinition(
                new[] { new ChestBand(ChestDropKind.Credits, 70, 110) },
                new[]
                {
                    new ChestOption(new ChestBand(ChestDropKind.Credits, 50, 90), 40),
                    new ChestOption(new ChestBand(ChestDropKind.Hearts, 1, 1), 25),
                    new ChestOption(new ChestBand(ChestDropKind.Utility, 1, 1, "mending"), 20),
                    new ChestOption(new ChestBand(ChestDropKind.Gems, 1, 2), 15),
                }), 1);

            var silver = new ChestTier("silver", 2, new ChestDefinition(
                new[]
                {
                    new ChestBand(ChestDropKind.Credits, 140, 200),
                    new ChestBand(ChestDropKind.Utility, 1, 1, "firepot"),
                },
                new[]
                {
                    new ChestOption(new ChestBand(ChestDropKind.Gems, 2, 4), 35),
                    new ChestOption(new ChestBand(ChestDropKind.Hearts, 1, 2), 25),
                    new ChestOption(new ChestBand(ChestDropKind.Utility, 1, 1, "surge"), 20),
                    new ChestOption(new ChestBand(ChestDropKind.HeartBoost, 12, 12), 20),
                }), 2);

            var gold = new ChestTier("gold", 3, new ChestDefinition(
                new[]
                {
                    new ChestBand(ChestDropKind.Credits, 280, 380),
                    new ChestBand(ChestDropKind.Gems, 3, 5),
                },
                new[]
                {
                    new ChestOption(new ChestBand(ChestDropKind.Utility, 1, 1, "stormcall"), 30),
                    new ChestOption(new ChestBand(ChestDropKind.Gems, 5, 8), 30),
                    new ChestOption(new ChestBand(ChestDropKind.Hearts, 2, 3), 20),
                    new ChestOption(new ChestBand(ChestDropKind.HeartBoost, 24, 24), 20),
                }), 3);

            var royal = new ChestTier("royal", 4, new ChestDefinition(
                new[]
                {
                    new ChestBand(ChestDropKind.Credits, 550, 750),
                    new ChestBand(ChestDropKind.Gems, 8, 12),
                    new ChestBand(ChestDropKind.Utility, 1, 1, "stormcall"),
                },
                new[]
                {
                    new ChestOption(new ChestBand(ChestDropKind.Gems, 12, 20), 40),
                    new ChestOption(new ChestBand(ChestDropKind.Utility, 3, 3, "firepot"), 25),
                    new ChestOption(new ChestBand(ChestDropKind.Hearts, 5, 5), 15),
                    new ChestOption(new ChestBand(ChestDropKind.HeartBoost, 24, 24), 20),
                }), 5);

            var daily = new[]
            {
                new TaskDefinition("d_play", TaskPeriod.Daily, TaskGoal.Runs, 2, wood),
                new TaskDefinition("d_win", TaskPeriod.Daily, TaskGoal.Wins, 1, wood),
                new TaskDefinition("d_stars", TaskPeriod.Daily, TaskGoal.Stars, 5, silver),
                new TaskDefinition("d_matches", TaskPeriod.Daily, TaskGoal.Matches, 60, wood),
                new TaskDefinition("d_raiders", TaskPeriod.Daily, TaskGoal.Raiders, 40, wood),
                new TaskDefinition("d_charms", TaskPeriod.Daily, TaskGoal.Charms, 2, silver),
                new TaskDefinition("d_cogs", TaskPeriod.Daily, TaskGoal.Cogs, 3, wood),
                new TaskDefinition("d_bombs", TaskPeriod.Daily, TaskGoal.Bombs, 1, silver),
                new TaskDefinition("d_utility", TaskPeriod.Daily, TaskGoal.Utilities, 1, silver),
                new TaskDefinition("d_three", TaskPeriod.Daily, TaskGoal.ThreeStars, 1, silver),
            };

            var weekly = new[]
            {
                new TaskDefinition("w_play", TaskPeriod.Weekly, TaskGoal.Runs, 12, silver),
                new TaskDefinition("w_win", TaskPeriod.Weekly, TaskGoal.Wins, 7, gold),
                new TaskDefinition("w_stars", TaskPeriod.Weekly, TaskGoal.Stars, 24, gold),
                new TaskDefinition("w_raiders", TaskPeriod.Weekly, TaskGoal.Raiders, 300, gold),
                new TaskDefinition("w_bosses", TaskPeriod.Weekly, TaskGoal.Bosses, 2, royal),
                new TaskDefinition("w_charms", TaskPeriod.Weekly, TaskGoal.Charms, 10, gold),
                new TaskDefinition("w_three", TaskPeriod.Weekly, TaskGoal.ThreeStars, 3, royal),
                new TaskDefinition("w_waves", TaskPeriod.Weekly, TaskGoal.Waves, 12, gold),
                new TaskDefinition("w_streak", TaskPeriod.Weekly, TaskGoal.Streak, 5, royal),
                new TaskDefinition("w_cogs", TaskPeriod.Weekly, TaskGoal.Cogs, 20, silver),
            };

            return new TaskTable(3, new[] { wood, silver, gold, royal }, daily, weekly);
        }

        // ------------------------------------------------------------- building
        /// <summary>The most tasks one slate may hold, and the most tiers. Sanity bounds, not tuning.</summary>
        public const int MaxSlate = 64;
        public const int MaxTiers = 8;

        /// <summary>
        /// Reads the optional <c>tasks</c> block. Never throws and never returns null:
        /// anything wrong is named in <paramref name="problems"/> and the built-in table
        /// stands, because a content mistake must fail a build and never a session.
        /// </summary>
        public static TaskTable Resolve(TaskTableDto dto, List<string> problems)
        {
            if (problems == null) problems = new List<string>();

            // Absent is not an error. JsonUtility instantiates the block whether or not the
            // file wrote one, so "absent" is a block with none of its arrays — a value a real
            // one cannot hold, which is the fixed shape for a serialised class field.
            if (dto == null || !dto.IsAuthored) return Default;

            var tiers = ReadTiers(dto, problems);
            if (tiers == null) return Default;

            var tierById = new Dictionary<string, ChestTier>(StringComparer.Ordinal);
            foreach (var tier in tiers) tierById[tier.Id] = tier;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var daily = ReadSlate(dto.daily, TaskPeriod.Daily, tierById, ids, problems);
            if (daily == null) return Default;

            var weekly = ReadSlate(dto.weekly, TaskPeriod.Weekly, tierById, ids, problems);
            if (weekly == null) return Default;

            int active = dto.activePerPeriod > 0 ? dto.activePerPeriod : Default.ActivePerPeriod;

            return new TaskTable(active, tiers, daily, weekly);
        }

        static ChestTier[] ReadTiers(TaskTableDto dto, List<string> problems)
        {
            if (dto.tiers == null || dto.tiers.Length == 0)
            {
                problems.Add("tasks block lists no chest tiers; using the built-in table");
                return null;
            }

            if (dto.tiers.Length > MaxTiers)
            {
                problems.Add($"tasks block lists {dto.tiers.Length} tiers, more than the supported " +
                             $"{MaxTiers}; using the built-in table");
                return null;
            }

            var tiers = new ChestTier[dto.tiers.Length];
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < dto.tiers.Length; i++)
            {
                var entry = dto.tiers[i];
                if (entry == null) { problems.Add($"tasks tier {i} is empty"); return null; }

                if (!TaskDefinition.IsValidId(entry.id))
                {
                    problems.Add($"tasks tier {i} id '{entry.id}' is rejected: ids are lower-case " +
                                 "letters, digits and underscores, because they name art and copy");
                    return null;
                }

                if (!seen.Add(entry.id))
                {
                    problems.Add($"tasks tier '{entry.id}' is listed twice");
                    return null;
                }

                var chest = ReadChest(entry.chest, entry.id, problems);
                if (chest == null) return null;

                if (entry.marks < 0 || entry.marks > ChestTier.MaxMarks)
                {
                    problems.Add($"tasks tier '{entry.id}' is worth {entry.marks} marks, outside " +
                                 $"0..{ChestTier.MaxMarks}; a tier worth more than a whole season " +
                                 "track is a typo rather than a tuning");
                    return null;
                }

                tiers[i] = new ChestTier(entry.id, i + 1, chest, entry.marks);
            }

            return tiers;
        }

        /// <summary>
        /// One tier's chest, read with the daily table's own reader so a band that is legal
        /// on a daily chest is legal here and nowhere is the rule written twice.
        /// </summary>
        static ChestDefinition ReadChest(DailyChestEntryDto chest, string tierId, List<string> problems)
        {
            if (chest == null || !chest.IsAuthored)
            {
                problems.Add($"tasks tier '{tierId}' has no chest; every tier must pay something");
                return null;
            }

            var local = new List<string>();
            var read = DailyChestTable.ReadChest(chest, $"tier '{tierId}'", local);
            foreach (var problem in local) problems.Add("tasks " + problem);
            return read;
        }

        static TaskDefinition[] ReadSlate(TaskEntryDto[] entries, TaskPeriod period,
                                         Dictionary<string, ChestTier> tiers, HashSet<string> ids,
                                         List<string> problems)
        {
            string slate = TaskPeriods.Id(period);

            if (entries == null || entries.Length == 0)
            {
                problems.Add($"tasks block lists no {slate} tasks; using the built-in table");
                return null;
            }

            if (entries.Length > MaxSlate)
            {
                problems.Add($"tasks block lists {entries.Length} {slate} tasks, more than the " +
                             $"supported {MaxSlate}; using the built-in table");
                return null;
            }

            var read = new List<TaskDefinition>(entries.Length);

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null) { problems.Add($"{slate} task {i} is empty"); return null; }

                if (!TaskDefinition.IsValidId(entry.id))
                {
                    problems.Add($"{slate} task {i} id '{entry.id}' is rejected: ids are lower-case " +
                                 "letters, digits and underscores of at most " +
                                 $"{TaskDefinition.MaxIdLength}, because they are written into " +
                                 "save files and claim ids");
                    return null;
                }

                if (!ids.Add(entry.id))
                {
                    problems.Add($"task id '{entry.id}' is listed twice; an id is written into the " +
                                 "save and a claim, so two tasks sharing one would pay as one");
                    return null;
                }

                // Skipped rather than fatal: an unknown goal is how a newer content pack reaches
                // an older build, and dropping the entry degrades the slate instead of the game.
                var goal = TaskGoals.Parse(entry.goal);
                if (goal == TaskGoal.None)
                {
                    problems.Add($"{slate} task '{entry.id}' names unknown goal '{entry.goal}'; skipped");
                    continue;
                }

                if (entry.target < 1)
                {
                    problems.Add($"{slate} task '{entry.id}' has target {entry.target}; a task that " +
                                 "asks for nothing is a chest handed out for opening the screen");
                    return null;
                }

                if (entry.tier == null || !tiers.TryGetValue(entry.tier, out var tier))
                {
                    problems.Add($"{slate} task '{entry.id}' pays tier '{entry.tier}', which the block " +
                                 "does not define; a task paying a chest nobody can price is a claim " +
                                 "the server can never confirm");
                    return null;
                }

                read.Add(new TaskDefinition(entry.id, period, goal, entry.target, tier, entry.retired));
            }

            if (read.Count == 0)
            {
                problems.Add($"every {slate} task was skipped; using the built-in table");
                return null;
            }

            return read.ToArray();
        }
    }
}
