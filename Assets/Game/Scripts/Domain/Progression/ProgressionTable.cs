using System;
using System.Collections.Generic;
using GlimmerGrove.Ads;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using UnityEngine;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// What one glade pays out, given the best result the player holds on it.
    ///
    /// Deliberately a function of the <em>record</em> and not of a run. A player who
    /// replays a cleared glade and does worse has earned nothing new, and that falls
    /// out of the arithmetic rather than needing a rule to catch it.
    /// </summary>
    public readonly struct RewardRule
    {
        public readonly int XpFirstClear;
        public readonly int XpPerStar;
        public readonly int CreditsFirstClear;
        public readonly int CreditsPerStar;

        public RewardRule(int xpFirstClear, int xpPerStar, int creditsFirstClear, int creditsPerStar)
        {
            XpFirstClear = Max0(xpFirstClear);
            XpPerStar = Max0(xpPerStar);
            CreditsFirstClear = Max0(creditsFirstClear);
            CreditsPerStar = Max0(creditsPerStar);
        }

        /// <summary>Used when no file ships and when a field is left unwritten.</summary>
        public static readonly RewardRule Default = new RewardRule(40, 20, 30, 15);

        public long XpFor(int stars) => stars <= 0 ? 0L : XpFirstClear + (long)XpPerStar * stars;

        public long CreditsFor(int stars)
            => stars <= 0 ? 0L : CreditsFirstClear + (long)CreditsPerStar * stars;

        /// <summary>Fills unwritten (-1) fields from a fallback, so overrides stay partial.</summary>
        internal static RewardRule Resolve(RewardRuleDto dto, RewardRule fallback)
        {
            if (dto == null) return fallback;

            return new RewardRule(
                dto.xpFirstClear < 0 ? fallback.XpFirstClear : dto.xpFirstClear,
                dto.xpPerStar < 0 ? fallback.XpPerStar : dto.xpPerStar,
                dto.creditsFirstClear < 0 ? fallback.CreditsFirstClear : dto.creditsFirstClear,
                dto.creditsPerStar < 0 ? fallback.CreditsPerStar : dto.creditsPerStar);
        }

        static int Max0(int v) => v < 0 ? 0 : v;
    }

    /// <summary>
    /// The XP curve and the reward rules, immutable once built.
    ///
    /// The curve is stored as cumulative totals computed once at load, so resolving a
    /// level from an XP total is a binary search rather than a loop that has to be
    /// trusted to terminate. Authoring stays in increments because that is what a
    /// designer can reason about; the two representations never diverge because only
    /// one of them is written down.
    ///
    /// Everything here is content, not code. Retuning rewards must not require a
    /// store review, and it is only safe to retune at all because XP is derived from
    /// the player's ledger rather than accumulated into it — see
    /// <see cref="ProgressionLedger"/>.
    /// </summary>
    public sealed class ProgressionTable
    {
        public const int DefaultMaxLevel = 500;

        /// <summary>Hard ceiling on the authored cap, so a bad file cannot allocate wildly.</summary>
        public const int MaxSupportedLevel = 10000;

        readonly long[] _cumulative;                 // [i] = total XP to *reach* level i+1
        readonly RewardRule _defaultRule;
        readonly Dictionary<ChapterId, RewardRule> _chapterRules;

        ProgressionTable(long[] cumulative, RewardRule defaultRule,
                         Dictionary<ChapterId, RewardRule> chapterRules,
                         DailyChestTable daily, AdRewardTable ads, StreakTable streak,
                         GoldenTable golden)
        {
            _cumulative = cumulative;
            _defaultRule = defaultRule;
            _chapterRules = chapterRules;
            Daily = daily ?? DailyChestTable.Default;
            Ads = ads ?? AdRewardTable.Default;
            Streak = streak ?? StreakTable.Default;
            Golden = golden ?? GoldenTable.Default;
        }

        /// <summary>
        /// The daily chest rules, published with the curve rather than beside it.
        ///
        /// One table, one fetch, one atomic swap. Two separately-loaded tables would let
        /// a player briefly hold a new drop table against an old reward rate, which is
        /// precisely the window an economy exploit lives in.
        /// </summary>
        public DailyChestTable Daily { get; }

        /// <summary>
        /// What rewarded ads pay, published with the curve for the same reason the chest
        /// table is.
        ///
        /// The window this closes is narrower than the chest one but the same shape: an ad
        /// payout loaded separately from the reward curve would let a player briefly hold a
        /// retuned ad reward against an untuned economy, and the whole point of tuning them
        /// together is that they are one number seen from two sides.
        /// </summary>
        public AdRewardTable Ads { get; }

        /// <summary>
        /// What a run of consecutive days pays, published with the curve for the same
        /// reason the two above it are.
        ///
        /// The window this closes is the sharpest of the three, because a streak rung and
        /// a chest are competing for the same evening: a client holding a retuned streak
        /// ladder against an untuned chest table has two rewards that were balanced against
        /// each other and no longer are, and the player feels it as one of them being
        /// pointless.
        /// </summary>
        public StreakTable Streak { get; }

        /// <summary>
        /// How often a glade pays more than the reward rule says. Published with the curve
        /// because it <em>is</em> the curve seen from another angle — the average multiplier
        /// multiplies every credit figure above it, so tuning one without the other moves
        /// the economy by a factor nobody wrote down.
        /// </summary>
        public GoldenTable Golden { get; }

        /// <summary>
        /// A usable curve for when no file could be read. The game stays playable and
        /// the validator fails the build, which is the right way round — a content
        /// problem should stop a build, never a player's session.
        /// </summary>
        public static readonly ProgressionTable Default = Build(
            new[] { 100, 150, 200, 260, 330, 410, 500, 600, 710, 830, 960, 1100 },
            tailXpToNext: 1250,
            tailXpIncrement: 150,
            maxLevel: DefaultMaxLevel,
            defaultRule: RewardRule.Default,
            chapterRules: new Dictionary<ChapterId, RewardRule>(),
            daily: DailyChestTable.Default,
            ads: AdRewardTable.Default,
            streak: StreakTable.Default,
            golden: GoldenTable.Default);

        /// <summary>Highest level this curve defines. Level 1 always exists.</summary>
        public int MaxLevel => _cumulative.Length;

        public RewardRule DefaultRule => _defaultRule;

        /// <summary>The rule for a chapter, falling back to the default when unlisted.</summary>
        public RewardRule RuleFor(ChapterId chapter)
            => _chapterRules.TryGetValue(chapter, out var rule) ? rule : _defaultRule;

        public bool HasOverrideFor(ChapterId chapter) => _chapterRules.ContainsKey(chapter);

        /// <summary>Total XP needed to stand at the start of <paramref name="level"/>.</summary>
        public long XpToReach(int level)
        {
            if (level <= 1) return 0L;
            if (level > MaxLevel) level = MaxLevel;
            return _cumulative[level - 1];
        }

        /// <summary>XP required to move from <paramref name="level"/> to the next. 0 at the cap.</summary>
        public long XpToNext(int level)
        {
            if (level >= MaxLevel) return 0L;
            if (level < 1) level = 1;
            return XpToReach(level + 1) - XpToReach(level);
        }

        /// <summary>Resolves an XP total into a level and the progress through it.</summary>
        public PlayerLevel LevelFor(long xp)
        {
            if (xp < 0) xp = 0;

            // Largest level whose cumulative requirement is still met.
            int lo = 1, hi = MaxLevel;
            while (lo < hi)
            {
                int mid = lo + (hi - lo + 1) / 2;
                if (_cumulative[mid - 1] <= xp) lo = mid;
                else hi = mid - 1;
            }

            long into = xp - XpToReach(lo);
            return new PlayerLevel(lo, xp, into, XpToNext(lo), lo >= MaxLevel);
        }

        // ------------------------------------------------------------- building
        /// <summary>
        /// Reads a progression file. Never throws: content can arrive from a CDN, so a
        /// malformed table degrades to the built-in curve and names what was wrong.
        /// </summary>
        public static bool TryRead(string json, out ProgressionTable table, List<string> problems)
        {
            table = Default;
            if (problems == null) problems = new List<string>();

            ProgressionDto dto;
            try
            {
                dto = JsonUtility.FromJson<ProgressionDto>(json);
            }
            catch (Exception e)
            {
                problems.Add("progression file is not valid JSON: " + e.Message);
                return false;
            }

            return TryBuild(dto, out table, problems);
        }

        /// <summary>
        /// Builds a table from an already-parsed file.
        ///
        /// Split from <see cref="TryRead"/> so the reward rules can be exercised without
        /// a JSON serialiser in the way — the shared reward vectors are run offline
        /// against this, where <c>JsonUtility</c> does not exist.
        /// </summary>
        public static bool TryBuild(ProgressionDto dto, out ProgressionTable table, List<string> problems)
        {
            table = Default;
            if (problems == null) problems = new List<string>();

            if (dto == null) { problems.Add("progression file is empty"); return false; }

            // Its own schema, not the catalog's — see ProgressionSchema for why.
            string schemaProblem = ProgressionSchema.Explain(dto.schemaVersion);
            if (schemaProblem != null) { problems.Add("progression file " + schemaProblem); return false; }

            if (dto.xpToNext == null || dto.xpToNext.Length == 0)
            {
                problems.Add("progression file has no xpToNext band; the curve would be undefined");
                return false;
            }

            for (int i = 0; i < dto.xpToNext.Length; i++)
            {
                if (dto.xpToNext[i] > 0) continue;
                problems.Add($"progression xpToNext[{i}] is {dto.xpToNext[i]}; every band must cost " +
                             "at least 1 XP or a player would gain unbounded levels at once");
                return false;
            }

            if (dto.tailXpToNext <= 0)
            {
                problems.Add($"progression tailXpToNext is {dto.tailXpToNext}; levels past the authored " +
                             "band must still cost something");
                return false;
            }

            if (dto.tailXpIncrement < 0)
            {
                problems.Add($"progression tailXpIncrement is {dto.tailXpIncrement}; a shrinking tail " +
                             "eventually costs nothing per level");
                return false;
            }

            int maxLevel = dto.maxLevel > 0 ? dto.maxLevel : DefaultMaxLevel;
            if (maxLevel > MaxSupportedLevel)
            {
                problems.Add($"progression maxLevel {maxLevel} exceeds the supported ceiling " +
                             $"{MaxSupportedLevel}; clamped");
                maxLevel = MaxSupportedLevel;
            }

            var defaultRule = RewardRule.Resolve(dto.rewards, RewardRule.Default);
            var chapterRules = new Dictionary<ChapterId, RewardRule>();

            if (dto.chapterRewards != null)
            {
                foreach (var entry in dto.chapterRewards)
                {
                    if (entry == null) continue;

                    if (!ChapterId.TryParse(entry.chapterId, out var chapterId, out string idError))
                    {
                        problems.Add($"progression chapterRewards entry '{entry.chapterId}' rejected: {idError}");
                        continue;
                    }

                    if (chapterRules.ContainsKey(chapterId))
                    {
                        problems.Add($"progression lists chapter '{chapterId}' twice; the first wins");
                        continue;
                    }

                    chapterRules[chapterId] = RewardRule.Resolve(entry.AsRule(), defaultRule);
                }
            }

            // Optional, and reported through the same problems list — a daily block that
            // does not read is a content error worth failing a build over, but never a
            // reason to discard a perfectly good XP curve.
            var daily = DailyChestTable.Resolve(dto.daily, problems);

            // Same bargain as the daily block, one step further: an unreadable ads block
            // costs the live ad tuning and nothing else. It must not discard the XP curve,
            // and it must not take the chest table down with it either.
            var ads = AdRewardTable.Resolve(dto.ads, problems);

            // And the same bargain again. An unreadable streak ladder costs the live
            // tuning of one feature; it must not take the curve, the chests or the ads
            // down with it.
            var streak = StreakTable.Resolve(dto.streak, problems);

            // And once more. Of the four optional blocks this is the one whose absence is
            // least visible — every glade simply pays exactly what the rule says, which is
            // a working game — so it must be reported loudly and never allowed to be fatal.
            var golden = GoldenTable.Resolve(dto.golden, problems);

            table = Build(dto.xpToNext, dto.tailXpToNext, dto.tailXpIncrement, maxLevel,
                          defaultRule, chapterRules, daily, ads, streak, golden);
            return true;
        }

        static ProgressionTable Build(int[] xpToNext, int tailXpToNext, int tailXpIncrement,
                                      int maxLevel, RewardRule defaultRule,
                                      Dictionary<ChapterId, RewardRule> chapterRules,
                                      DailyChestTable daily, AdRewardTable ads,
                                      StreakTable streak, GoldenTable golden)
        {
            if (maxLevel < 1) maxLevel = 1;

            var cumulative = new long[maxLevel];
            cumulative[0] = 0L;                       // level 1 costs nothing to reach

            for (int level = 1; level < maxLevel; level++)
            {
                long step = level - 1 < xpToNext.Length
                    ? xpToNext[level - 1]
                    : tailXpToNext + (long)tailXpIncrement * (level - 1 - xpToNext.Length);

                if (step < 1) step = 1;               // the reader rejects this; belt and braces
                cumulative[level] = cumulative[level - 1] + step;
            }

            return new ProgressionTable(cumulative, defaultRule, chapterRules, daily, ads, streak, golden);
        }
    }
}
