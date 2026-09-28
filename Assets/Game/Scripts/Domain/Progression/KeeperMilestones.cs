using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using GlimmerGrove.Tasks;

namespace GlimmerGrove.Progression
{
    /// <summary>The bounds a published <c>keeperMilestones</c> block is checked against. Mirrored by
    /// <c>seed-config.mjs</c> and <c>content.py</c>, so a block one side accepts the other does too.</summary>
    public static class KeeperMilestoneLimits
    {
        /// <summary>The first level that can pay anything - nobody starts below 1.</summary>
        public const int LowestLevel = 2;

        /// <summary>The most rows a ladder may carry. A ladder, not a table of every level.</summary>
        public const int MaxRows = 64;
    }

    /// <summary>One keeper milestone, as resolved: a level and the chest tier it pays.</summary>
    public readonly struct KeeperMilestone
    {
        public readonly int Level;
        public readonly ChestTier Tier;

        public KeeperMilestone(int level, ChestTier tier)
        {
            Level = level;
            Tier = tier;
        }

        public bool IsValid => Level > 0 && Tier != null;
    }

    /// <summary>
    /// The chests the keeper ladder pays on the way up - the content half of invariant 57d.
    ///
    /// <para>
    /// A milestone is a level and a tier. <b>The level is the effective level</b> - earned plus
    /// bought (<see cref="KeeperLadder.Compose"/>) - because that is the one the page draws and the
    /// one the server can prove from the save and the wallet together; a bought level therefore
    /// passes a milestone exactly as an earned one does. <b>The tier is one of the tasks block's</b>,
    /// so the odds are already published, the season already knows what opening one is worth (47),
    /// and the ceremony is the one every other chest opens.
    /// </para>
    /// <para>
    /// <b>Refused whole on any fault</b>, like the price ladder and for the same reason: a row the
    /// server would not price is a claim it leaves unconfirmed for ever, and a page that offered
    /// such a chest would be showing money that never arrives. Absent pays nothing.
    /// </para>
    /// </summary>
    public sealed class KeeperMilestoneTable
    {
        readonly KeeperMilestone[] _rows;
        readonly Dictionary<int, KeeperMilestone> _byLevel;

        KeeperMilestoneTable(KeeperMilestone[] rows)
        {
            _rows = rows ?? Array.Empty<KeeperMilestone>();
            _byLevel = new Dictionary<int, KeeperMilestone>(_rows.Length);
            foreach (var row in _rows) _byLevel[row.Level] = row;
            Last = _rows.Length > 0 ? _rows[_rows.Length - 1].Level : 0;
        }

        /// <summary>The table that pays nothing - the default, and what a broken block resolves to.</summary>
        public static readonly KeeperMilestoneTable Empty = new KeeperMilestoneTable(Array.Empty<KeeperMilestone>());

        /// <summary>The milestones, lowest level first. Empty when nothing is paid.</summary>
        public IReadOnlyList<KeeperMilestone> Rows => _rows;

        /// <summary>The highest level that pays anything, or 0. What the page has to reach.</summary>
        public int Last { get; }

        /// <summary>Whether any level pays a chest at all.</summary>
        public bool Pays => _rows.Length > 0;

        /// <summary>The milestone at exactly <paramref name="level"/>, or an invalid one.</summary>
        public KeeperMilestone At(int level)
            => _byLevel.TryGetValue(level, out var row) ? row : default;

        /// <summary>
        /// Reads the optional <c>keeperMilestones</c> block against the tiers the tasks block
        /// defines. Never throws and never returns null: anything wrong is named in
        /// <paramref name="problems"/> and the table resolves to <see cref="Empty"/>.
        /// </summary>
        public static KeeperMilestoneTable Resolve(KeeperMilestonesDto dto, TaskTable tasks, int maxLevel,
                                                   List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null || !dto.IsAuthored) return Empty;             // absent pays nothing

            var rows = dto.rows;
            if (rows.Length > KeeperMilestoneLimits.MaxRows)
            {
                problems.Add($"keeperMilestones lists {rows.Length} rows; at most " +
                             $"{KeeperMilestoneLimits.MaxRows} are supported");
                return Empty;
            }

            var resolved = new KeeperMilestone[rows.Length];
            bool ok = true;
            int last = 0;

            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                string where = $"keeperMilestones row {i}";

                if (row == null) { problems.Add($"{where} is empty"); ok = false; continue; }

                if (row.level < KeeperMilestoneLimits.LowestLevel || row.level > maxLevel)
                {
                    problems.Add($"{where} pays at level {row.level}, outside " +
                                 $"{KeeperMilestoneLimits.LowestLevel}..{maxLevel}");
                    ok = false;
                }
                if (row.level <= last)
                {
                    problems.Add($"{where} pays at level {row.level} after level {last}; rows must climb");
                    ok = false;
                }

                var tier = tasks?.Tier(row.tier);
                if (tier == null)
                {
                    problems.Add($"{where} pays chest tier '{row.tier}', which the tasks block does " +
                                 "not define; a chest nobody can price is a claim the server never confirms");
                    ok = false;
                }

                last = row.level;
                resolved[i] = new KeeperMilestone(row.level, tier);
            }

            return ok ? new KeeperMilestoneTable(resolved) : Empty;
        }
    }

    /// <summary>
    /// The milestone chests this account has taken - the ledger half of invariant 57d.
    ///
    /// <para>
    /// <b>What is stored is a floor</b>: the highest milestone level claimed, one monotonic
    /// integer inside the wallet map joined by <c>max</c> (11b, 12a), and nothing else. Which
    /// chests are <em>waiting</em> is derived on every read from the level the player stands at
    /// against that floor, so a milestone added by a content push under a player already past it
    /// is waiting the moment the table arrives, and a level that falls (a refused purchase, 57a)
    /// hides nothing that was already paid.
    /// </para>
    /// <para>
    /// <b>Only the earliest waiting milestone may be taken</b> - the streak's rule (48b) for the
    /// streak's reason: a floor is a floor, so taking a later one first would sweep the earlier
    /// ones behind a single ceremony. A tap on a later one is redirected to the earliest. The
    /// chest is rolled from the account and the level (<see cref="SeedFor"/>), so two devices open
    /// the same one; its currency reaches the wallet as a claim under
    /// <see cref="GrantEntry.KeeperMilestoneId"/>, which the server re-rolls and bounds by the
    /// level the save and the wallet prove; everything else is banked at once
    /// (<see cref="BankedDrop"/>). It feeds the season by naming a tier (47).
    /// </para>
    /// </summary>
    public static class KeeperMilestoneLedger
    {
        /// <summary>Matches the server's <c>KEEPER_MILESTONE_SEED_TAG</c>. Contract (invariant 9c).</summary>
        public const string SeedTag = "milestone";

        /// <summary>Raised whenever a milestone is taken, or the floor moves by sync.</summary>
        public static event Action Changed;

        static string PlayerKey => RewardSeed.PlayerKey;

        /// <summary>The published table - content, so retunable without a build.</summary>
        public static KeeperMilestoneTable Table => ProgressionRules.Table.KeeperMilestones;

        /// <summary>The highest milestone level this account has taken. Nought before the first.</summary>
        public static int ClaimedThrough => Wallet.KeeperMilestonesClaimed;

        /// <summary>
        /// Whether a chest could be handed over right now: a chest is a claim the server
        /// adjudicates, so it needs an account to claim it under (10c's shape).
        /// </summary>
        public static bool CanClaimChests => RewardSeed.IsAdjudicable;

        /// <summary>The level the milestones are read against: earned plus bought.</summary>
        static int Standing => PlayerProgression.Level.Level;

        /// <summary>Whether <paramref name="level"/> is a milestone the player has reached and not taken.</summary>
        public static bool IsWaiting(int level)
        {
            var row = Table.At(level);
            return row.IsValid && level <= Standing && level > ClaimedThrough;
        }

        /// <summary>How many milestones are reached and untaken. What the hub's badge prints.</summary>
        public static int Waiting
        {
            get
            {
                int standing = Standing, claimed = ClaimedThrough, count = 0;
                foreach (var row in Table.Rows)
                {
                    if (row.Level > standing) break;
                    if (row.Level > claimed) count++;
                }
                return count;
            }
        }

        /// <summary>The lowest waiting milestone's level - the one a tap takes - or 0 when none waits.</summary>
        public static int NextWaiting
        {
            get
            {
                int standing = Standing, claimed = ClaimedThrough;
                foreach (var row in Table.Rows)
                {
                    if (row.Level > standing) break;
                    if (row.Level > claimed) return row.Level;
                }
                return 0;
            }
        }

        /// <summary>True when <paramref name="level"/> is the milestone a tap would take right now.</summary>
        public static bool CanCollect(int level)
            => level > 0 && level == NextWaiting && CanClaimChests;

        /// <summary>What a milestone holds, without claiming it. Empty for a level that is no milestone.</summary>
        public static List<ChestDrop> Preview(int level)
        {
            var row = Table.At(level);
            if (!row.IsValid) return new List<ChestDrop>();
            return row.Tier.Chest.Roll(SeedFor(level));
        }

        /// <summary>
        /// The seed a milestone's chest is rolled from: the player, this feature and the level.
        /// The subject layout is contract with the server's <c>subjectSeed</c>; see <see cref="ChestSeed"/>.
        /// </summary>
        public static ChestSeed SeedFor(int level)
            => ChestSeed.ForSubject(PlayerKey, SeedTag, Subject(level));

        /// <summary>The subject half of the seed and of the claim id: the level alone.</summary>
        public static string Subject(int level) => level.ToString();

        /// <summary>
        /// Hands over one milestone and returns what it paid.
        ///
        /// <para>
        /// The floor moves <em>before</em> the reward is handed over, which is
        /// <c>DailyStreak.TryCollect</c>'s ordering and its argument: a process killed mid-payout
        /// must come back with the floor past a chest whose hearts were already banked, because
        /// currency survives a retry (the award carries a derived id) and a banked drop does not.
        /// </para>
        /// </summary>
        public static bool TryCollect(int level, out List<ChestDrop> drops)
        {
            drops = null;
            if (!CanCollect(level)) return false;

            var row = Table.At(level);
            if (!row.IsValid) return false;

            Wallet.RaiseKeeperMilestonesClaimed(level);

            var paid = row.Tier.Chest.Roll(SeedFor(level));
            Apply(paid, level);

            // The season grows on a claimed chest and nowhere else (47): a milestone feeds it
            // exactly as a task's or a streak night's does, by naming a tier.
            Events.SeasonLedger.NoteChest(row.Tier);

            SaveService.Save();
            Raise();

            Telemetry.Track("keeper_milestone_collected",
                            "level", level, "tier", row.Tier.Id, "reward", Describe(paid));

            drops = paid;
            return true;
        }

        /// <summary>Currency is claimed under the milestone's own id; everything else is banked now.</summary>
        static void Apply(List<ChestDrop> drops, int level)
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
                    GrantEntry.KeeperMilestoneId(level, currency),
                    GrantEntry.KeeperMilestoneReason, now);
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
    }
}
