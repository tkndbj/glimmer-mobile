using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using GlimmerGrove.Tasks;
using GlimmerGrove.Wards;

namespace GlimmerGrove.Progression
{
    /// <summary>What clearing a step's level for the first time hands over.</summary>
    public enum FirstStepKind
    {
        /// <summary>
        /// A turret on one seat, granted with no server - a client-held entitlement that buys no
        /// currency (invariant 15, <see cref="WardLedger"/>'s own argument), exactly as the welcome
        /// bonus hands one over.
        /// </summary>
        Turret,

        /// <summary>
        /// Nothing at all: the welcome bonus's progress is shown, so a new player sees the reason
        /// to come back tomorrow at the moment they have just earned a piece of it. A moment, so it
        /// stores nothing.
        /// </summary>
        Welcome,

        /// <summary>
        /// Chests of one tier. <b>Currency, so a claim</b> (invariant 10a): each chest is rolled
        /// from the account and the step and its currency is claimed under
        /// <c>steps:{level}:{n}:{currency}</c>, which the server re-rolls and pays only once the
        /// save it holds shows the level cleared (<c>steps.ts</c>).
        /// </summary>
        Chests,
    }

    /// <summary>The kinds' spellings. Written out, never built from the enum's name.</summary>
    public static class FirstStepKinds
    {
        public static string Id(FirstStepKind kind)
        {
            switch (kind)
            {
                case FirstStepKind.Turret: return "turret";
                case FirstStepKind.Welcome: return "welcome";
                case FirstStepKind.Chests: return "chests";
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <summary>The kind a spelling names, or false for one this build does not know.</summary>
        public static bool TryParse(string id, out FirstStepKind kind)
        {
            switch (id)
            {
                case "turret": kind = FirstStepKind.Turret; return true;
                case "welcome": kind = FirstStepKind.Welcome; return true;
                case "chests": kind = FirstStepKind.Chests; return true;
                default: kind = FirstStepKind.Turret; return false;
            }
        }
    }

    /// <summary>One first step, as resolved.</summary>
    public sealed class FirstStep
    {
        public LevelId Level { get; }
        public FirstStepKind Kind { get; }

        /// <summary>A turret step's turret. Null on every other kind.</summary>
        public WardModel Ward { get; }

        /// <summary>A turret step's seat, as a colour index (0..3). Nought on every other kind.</summary>
        public int Colour { get; }

        /// <summary>A chest step's tier. Null on every other kind.</summary>
        public ChestTier Tier { get; }

        /// <summary>A chest step's chest count. Nought on every other kind.</summary>
        public int Count { get; }

        public FirstStep(LevelId level, FirstStepKind kind, WardModel ward, int colour, ChestTier tier, int count)
        {
            Level = level;
            Kind = kind;
            Ward = ward;
            Colour = colour;
            Tier = tier;
            Count = count;
        }
    }

    /// <summary>
    /// The first steps: what a new player is handed the first time they clear a named level.
    ///
    /// <para>
    /// <b>Why it exists.</b> Most players left inside their first ten minutes, before the sixth
    /// level. The steps put a felt payoff on the first levels - a turret worth equipping at once,
    /// the welcome bonus's first day ticked, a pair of chests - so progress is something that
    /// happens to a new player rather than something they have to go looking for.
    /// </para>
    /// <para>
    /// <b>Keyed on a level id, never a position</b> (invariant 1): which level a step stands on is
    /// content, so moving a step is a content push and inserting a level moves nothing. One step
    /// a level, because one celebration after a run is the whole budget (the owner: progress felt,
    /// never a flood of panels).
    /// </para>
    /// <para>
    /// <b>Refused whole on any fault</b>, like the keeper milestones and for their reason: a chest
    /// the server would not price is a claim left unconfirmed for ever, and a step that cannot
    /// pay must not be shown. Absent is the feature off.
    /// </para>
    /// </summary>
    public sealed class FirstStepTable
    {
        /// <summary>The most steps a block may hold. Matches the rules' bound on <c>tasks.stepsEarned</c>.</summary>
        public const int MaxSteps = 16;

        /// <summary>The most chests one step may hand over.</summary>
        public const int MaxChests = 4;

        public static readonly FirstStepTable Empty = new FirstStepTable(Array.Empty<FirstStep>());

        readonly FirstStep[] _steps;
        readonly Dictionary<LevelId, FirstStep> _byLevel;

        FirstStepTable(FirstStep[] steps)
        {
            _steps = steps;
            _byLevel = new Dictionary<LevelId, FirstStep>(steps.Length);
            foreach (var step in steps) _byLevel[step.Level] = step;
        }

        public IReadOnlyList<FirstStep> Steps => _steps;

        public bool IsEmpty => _steps.Length == 0;

        /// <summary>The step standing on <paramref name="level"/>, or null.</summary>
        public FirstStep At(LevelId level)
            => level.IsValid && _byLevel.TryGetValue(level, out var step) ? step : null;

        /// <summary>
        /// Reads the optional <c>firstSteps</c> block against the turret roster and the chest
        /// tiers. Never throws and never returns null: anything wrong is named in
        /// <paramref name="problems"/> and the table resolves to <see cref="Empty"/>.
        ///
        /// <para>
        /// Whether each level is one the catalog ships is a question for the gates that hold the
        /// manifest (<c>content.py</c>, <c>seed-config.mjs</c>, <c>FirstStepsTests</c>): this table
        /// is read with no catalog, and a step on a level nobody can clear simply never pays.
        /// </para>
        /// </summary>
        public static FirstStepTable Resolve(FirstStepsDto dto, WardCatalog wards, TaskTable tasks,
                                             List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null || !dto.IsAuthored) return Empty;

            var rows = dto.steps;
            if (rows.Length > MaxSteps)
            {
                problems.Add($"firstSteps lists {rows.Length} steps; at most {MaxSteps} are supported");
                return Empty;
            }

            wards ??= WardCatalog.Default;

            var steps = new FirstStep[rows.Length];
            var levels = new HashSet<LevelId>();
            bool ok = true;

            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                string where = $"firstSteps step {i}";

                if (row == null) { problems.Add($"{where} is empty"); ok = false; continue; }

                if (!LevelId.TryParse(row.level, out var level, out string error))
                {
                    problems.Add($"{where} names level '{row.level}': {error}");
                    ok = false;
                }
                else if (!levels.Add(level))
                {
                    problems.Add($"{where} names level '{row.level}' a second time; one step a level");
                    ok = false;
                }

                if (!FirstStepKinds.TryParse(row.kind, out var kind))
                {
                    problems.Add($"{where} is of kind '{row.kind}'; a step is 'turret', 'welcome' or 'chests'");
                    ok = false;
                    continue;
                }

                WardModel ward = null;
                ChestTier tier = null;
                int colour = 0, count = 0;

                // Each kind reads its own fields and refuses the others by name (5f): JsonUtility
                // drops nothing it recognises, so a turret written onto a chest step would
                // otherwise be a promise in the file that nothing pays.
                bool hasWard = !string.IsNullOrEmpty(row.ward) || !string.IsNullOrEmpty(row.colour);
                bool hasTier = !string.IsNullOrEmpty(row.tier) || row.count != 0;

                switch (kind)
                {
                    case FirstStepKind.Turret:
                        if (hasTier) { problems.Add($"{where} is a turret step and names a chest tier or count"); ok = false; }

                        ward = wards.Find(row.ward);
                        if (ward == null)
                        {
                            problems.Add($"{where} hands over turret '{row.ward}', which the roster does not hold");
                            ok = false;
                        }
                        else if (ward.IsStarter)
                        {
                            problems.Add($"{where} hands over turret '{row.ward}', which is the free starter and already held");
                            ok = false;
                        }

                        colour = row.colour != null && row.colour.Length == 1 ? WardLine.Colours.IndexOf(row.colour[0]) : -1;
                        if (colour < 0)
                        {
                            problems.Add($"{where} hands its turret over on seat '{row.colour}'; a step names one of " +
                                         $"'{WardLine.Colours}'");
                            ok = false;
                            colour = 0;
                        }
                        break;

                    case FirstStepKind.Welcome:
                        if (hasWard || hasTier)
                        {
                            problems.Add($"{where} is a welcome step and names a turret or a chest; it hands over nothing");
                            ok = false;
                        }
                        break;

                    case FirstStepKind.Chests:
                        if (hasWard) { problems.Add($"{where} is a chest step and names a turret or a seat"); ok = false; }

                        tier = tasks?.Tier(row.tier);
                        if (tier == null)
                        {
                            problems.Add($"{where} pays chest tier '{row.tier}', which the tasks block does not define; " +
                                         "a chest nobody can price is a claim the server never confirms");
                            ok = false;
                        }

                        count = row.count;
                        if (count < 1 || count > MaxChests)
                        {
                            problems.Add($"{where} pays {row.count} chests, outside 1..{MaxChests}");
                            ok = false;
                        }
                        break;
                }

                steps[i] = new FirstStep(level, kind, ward, colour, tier, count);
            }

            return ok ? new FirstStepTable(steps) : Empty;
        }
    }

    /// <summary>
    /// Which first steps this account has earned and which it has been handed - the ledger half
    /// of <see cref="FirstStepTable"/>.
    ///
    /// <para>
    /// <b>Earned is noted at the first clear, in the run's own save.</b> <see cref="RunLedger.Win"/>
    /// calls <see cref="NoteFirstClear"/> before the record is written, so the step and the clear
    /// land in one file: a process killed on the victory panel comes back holding both, and the
    /// moment that would have celebrated it finds the step still waiting. <b>Only a clear made by
    /// a build that knows the step earns it</b>, which is what makes this a new player's welcome:
    /// an account that cleared these levels before the steps shipped never earned them, and a
    /// returning player is not handed a pile of gifts for boards they finished months ago.
    /// </para>
    /// <para>
    /// <b>Two sets of tokens, both joined by union</b> (invariant 11b): <c>stepsEarned</c> holds
    /// the level ids, <c>stepsTaken</c> what was handed - the level id for a turret whose ceremony
    /// was shown, <c>{level}:{n}</c> for each chest opened. A chest is taken in the save
    /// <em>before</em> its drops are banked (<c>DailyStreak.TryCollect</c>'s ordering), so a
    /// process killed mid-payout comes back with the chest gone and its currency still claimed
    /// under a derived id, never a second set of banked drops. Both ride inside the <c>tasks</c>
    /// map, for <see cref="LifetimeTally"/>'s reason: a key inside a map already on
    /// <c>hasOnly</c> costs no rules release.
    /// </para>
    /// </summary>
    public static class FirstStepsLedger
    {
        /// <summary>Matches the server's <c>STEPS_SEED_TAG</c>. Contract (invariant 9c).</summary>
        public const string SeedTag = "steps";

        /// <summary>The most earned tokens kept. Matches the rules' bound on <c>tasks.stepsEarned</c>.</summary>
        public const int MaxEarned = FirstStepTable.MaxSteps;

        /// <summary>
        /// The most taken tokens kept: every step, each with its chests. Matches the rules' bound
        /// on <c>tasks.stepsTaken</c>.
        /// </summary>
        public const int MaxTaken = 64;

        static readonly SortedSet<string> _earned = new SortedSet<string>(StringComparer.Ordinal);
        static readonly SortedSet<string> _taken = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>Raised when a step is earned or handed over, and on every load.</summary>
        public static event Action Changed;

        /// <summary>The published table - content, so retunable without a build.</summary>
        public static FirstStepTable Table => ProgressionRules.Table.FirstSteps;

        static string PlayerKey => RewardSeed.PlayerKey;

        // ------------------------------------------------------------- reading
        /// <summary>Whether this account earned the step: cleared its level first on a build that knew it.</summary>
        public static bool IsEarned(FirstStep step) => step != null && _earned.Contains(step.Level.Value);

        /// <summary>Whether a turret step's ceremony is still owed. False on every other kind.</summary>
        public static bool TurretWaiting(FirstStep step)
            => step != null && step.Kind == FirstStepKind.Turret
            && IsEarned(step) && !_taken.Contains(step.Level.Value);

        /// <summary>Whether chest <paramref name="n"/> of a chest step has been opened.</summary>
        public static bool IsOpened(FirstStep step, int n)
            => step != null && _taken.Contains(ChestToken(step.Level, n));

        /// <summary>How many of a chest step's chests are earned and still shut.</summary>
        public static int ChestsWaiting(FirstStep step)
        {
            if (step == null || step.Kind != FirstStepKind.Chests || !IsEarned(step)) return 0;

            int waiting = 0;
            for (int n = 0; n < step.Count; n++)
                if (!IsOpened(step, n)) waiting++;
            return waiting;
        }

        /// <summary>The lowest shut chest of a chest step, or -1.</summary>
        public static int NextChest(FirstStep step)
        {
            if (step == null || step.Kind != FirstStepKind.Chests || !IsEarned(step)) return -1;

            for (int n = 0; n < step.Count; n++)
                if (!IsOpened(step, n)) return n;
            return -1;
        }

        /// <summary>
        /// Whether a chest could be handed over right now: a chest is a claim the server
        /// adjudicates, so it needs an account to claim it under (invariant 10c).
        /// </summary>
        public static bool CanClaimChests => RewardSeed.IsAdjudicable;

        /// <summary>
        /// The first step in the table still owed a ceremony - a turret not yet shown, or a chest
        /// step with a shut chest - or null. What a calm hub asks.
        /// </summary>
        public static FirstStep Owed
        {
            get
            {
                var steps = Table.Steps;
                for (int i = 0; i < steps.Count; i++)
                {
                    var step = steps[i];
                    if (TurretWaiting(step) || ChestsWaiting(step) > 0) return step;
                }
                return null;
            }
        }

        /// <summary>The seed a step's chest is rolled from. Contract with the server's <c>subjectSeed</c>.</summary>
        public static ChestSeed SeedFor(LevelId level, int n)
            => ChestSeed.ForSubject(PlayerKey, SeedTag, Subject(level, n));

        /// <summary>The subject half of the seed: the level and the chest's index. Contract.</summary>
        public static string Subject(LevelId level, int n) => $"{level.Value}:{n}";

        /// <summary>What a step's chest holds, without opening it. Empty for anything that is no chest.</summary>
        public static List<ChestDrop> Preview(FirstStep step, int n)
        {
            if (step == null || step.Kind != FirstStepKind.Chests || n < 0 || n >= step.Count)
                return new List<ChestDrop>();
            return step.Tier.Chest.Roll(SeedFor(step.Level, n));
        }

        static string ChestToken(LevelId level, int n) => $"{level.Value}:{n}";

        // ------------------------------------------------------------- writing
        /// <summary>
        /// A level was cleared for the first time. Called by <see cref="RunLedger.Win"/> before
        /// the run's record is written, so whatever this marks lands in the same save.
        ///
        /// <para>
        /// <b>A turret is handed over here and now</b>, on its one seat, so the board the player
        /// opens next can stand it whether or not any ceremony ever runs - the ceremony is a
        /// report, never the grant (<c>RewardFlight</c>'s rule: a reward already banked must never
        /// depend on an animation). A seat the player already holds - bought earlier - is kept as
        /// it is and the step is marked taken at once: there is nothing new to celebrate.
        /// </para>
        /// <para>
        /// A welcome step stores nothing; it is a moment the victory panel's exit answers.
        /// </para>
        /// </summary>
        internal static void NoteFirstClear(LevelId level)
        {
            var step = Table.At(level);
            if (step == null || step.Kind == FirstStepKind.Welcome) return;

            string token = level.Value;
            if (_earned.Contains(token) || _earned.Count >= MaxEarned) return;

            _earned.Add(token);

            if (step.Kind == FirstStepKind.Turret
                && !WardLedger.Grant(step.Ward, step.Colour, "steps:" + token)
                && _taken.Count < MaxTaken)
                _taken.Add(token);

            Telemetry.Track("first_step_earned", "level", token, "kind", FirstStepKinds.Id(step.Kind));

            SaveService.MarkDirty();
            Raise();
        }

        /// <summary>
        /// A turret step's ceremony has been raised. Marked when it opens, not when it is
        /// answered - a process killed with it up must not raise it again, and the turret it
        /// reports was handed over at the clear.
        /// </summary>
        public static void MarkShown(FirstStep step)
        {
            if (!TurretWaiting(step) || _taken.Count >= MaxTaken) return;

            _taken.Add(step.Level.Value);
            SaveService.Save();
            Raise();
        }

        /// <summary>
        /// Opens one chest of a chest step and returns what it paid.
        ///
        /// <para>
        /// The record moves <em>before</em> the reward is handed over, which is
        /// <c>KeeperMilestoneLedger.TryCollect</c>'s ordering and its argument: currency survives
        /// a retry (the award carries a derived id) and a banked drop does not. It feeds the
        /// season by naming a tier (47), as every other chest that is not a referral's does.
        /// </para>
        /// </summary>
        public static bool TryOpen(FirstStep step, int n, out List<ChestDrop> drops)
        {
            drops = null;
            if (step == null || step.Kind != FirstStepKind.Chests) return false;
            if (n < 0 || n >= step.Count || !IsEarned(step) || IsOpened(step, n)) return false;
            if (!CanClaimChests || _taken.Count >= MaxTaken) return false;

            // Only a step the published table still names: a withdrawn one pays nothing.
            var live = Table.At(step.Level);
            if (live == null || live.Kind != FirstStepKind.Chests || live.Tier == null) return false;

            _taken.Add(ChestToken(step.Level, n));

            var paid = live.Tier.Chest.Roll(SeedFor(step.Level, n));
            Apply(paid, step.Level, n);

            Events.SeasonLedger.NoteChest(live.Tier);

            SaveService.Save();
            Raise();

            Telemetry.Track("first_step_chest_opened", "level", step.Level.Value, "chest", n,
                            "tier", live.Tier.Id, "reward", Describe(paid));

            drops = paid;
            return true;
        }

        /// <summary>Currency is claimed under the step's own id; everything else is banked now.</summary>
        static void Apply(List<ChestDrop> drops, LevelId level, int n)
        {
            long now = GameClock.NowUnix();

            for (int i = 0; i < drops.Count; i++)
            {
                var drop = drops[i];
                if (!drop.IsValid) continue;
                if (BankedDrop.Apply(drop)) continue;
                if (!drop.IsCurrency) continue;

                string currency = ChestDropKinds.CurrencyOf(drop.Kind);
                PlayerProgression.Award(currency, drop.Amount,
                                        GrantEntry.FirstStepId(level.Value, n, currency),
                                        GrantEntry.FirstStepReason, now);
            }
        }

        static string Describe(List<ChestDrop> drops)
        {
            var parts = new string[drops.Count];
            for (int i = 0; i < drops.Count; i++) parts[i] = drops[i].ToString();
            return string.Join(",", parts);
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        /// <summary>
        /// Whether a token may be an earned token: a level id. One reading for the load and the
        /// join, so the two cannot disagree about what a well-formed row is.
        /// </summary>
        internal static bool IsEarnedToken(string token) => LevelId.TryParse(token, out _, out _);

        /// <summary>A taken token: a level id, or a level id and a chest index under <see cref="FirstStepTable.MaxChests"/>.</summary>
        internal static bool IsTakenToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;

            int colon = token.IndexOf(':');
            if (colon < 0) return IsEarnedToken(token);

            if (!IsEarnedToken(token.Substring(0, colon))) return false;

            string index = token.Substring(colon + 1);
            return int.TryParse(index, out int n) && n >= 0 && n < FirstStepTable.MaxChests
                && n.ToString() == index;
        }

        internal static void LoadFrom(string[] earned, string[] taken)
        {
            Read(_earned, earned, IsEarnedToken, MaxEarned);
            Read(_taken, taken, IsTakenToken, MaxTaken);
            Raise();
        }

        internal static string[] WriteEarned() => ToArray(_earned);

        internal static string[] WriteTaken() => ToArray(_taken);

        /// <summary>The union of two devices' earned tokens, sorted and capped.</summary>
        internal static string[] JoinEarned(string[] mine, string[] other)
            => Join(mine, other, IsEarnedToken, MaxEarned);

        /// <summary>The union of two devices' taken tokens, sorted and capped.</summary>
        internal static string[] JoinTaken(string[] mine, string[] other)
            => Join(mine, other, IsTakenToken, MaxTaken);

        /// <summary>
        /// Sorted, deduplicated and capped - the lowest kept, deterministically, so a join and a
        /// load cannot disagree about which. A list over the rules' bound loses every save write
        /// (invariant 12b), so the writer is what caps it.
        /// </summary>
        static void Read(SortedSet<string> into, string[] tokens, Func<string, bool> valid, int cap)
        {
            into.Clear();
            if (tokens == null) return;

            foreach (var token in tokens)
                if (valid(token)) into.Add(token);

            while (into.Count > cap) into.Remove(into.Max);
        }

        static string[] Join(string[] mine, string[] other, Func<string, bool> valid, int cap)
        {
            var union = new SortedSet<string>(StringComparer.Ordinal);
            if (mine != null) foreach (var token in mine) if (valid(token)) union.Add(token);
            if (other != null) foreach (var token in other) if (valid(token)) union.Add(token);
            while (union.Count > cap) union.Remove(union.Max);
            return ToArray(union);
        }

        static string[] ToArray(SortedSet<string> set)
        {
            var array = new string[set.Count];
            set.CopyTo(array);
            return array;
        }

        /// <summary>Forgets everything. Dev only, and used by the wipe.</summary>
        internal static void Reset()
        {
            _earned.Clear();
            _taken.Clear();
        }
    }
}
