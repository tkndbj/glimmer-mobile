using System;
using System.Collections.Generic;
using GlimmerGrove.Ads;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using GlimmerGrove.Store;
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
        public static readonly RewardRule Default = new RewardRule(60, 30, 80, 40);

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
                         GoldenTable golden, HeartRuleTable hearts, HintRuleTable hints,
                         StoreCatalog store,
                         AccountPromptRuleTable prompts, ChapterGateTable chapterGate,
                         ContinueTable carryOn)
        {
            _cumulative = cumulative;
            _defaultRule = defaultRule;
            _chapterRules = chapterRules;
            Daily = daily ?? DailyChestTable.Default;
            Ads = ads ?? AdRewardTable.Default;
            Streak = streak ?? StreakTable.Default;
            Golden = golden ?? GoldenTable.Default;
            Hearts = hearts ?? HeartRuleTable.Default;
            Hints = hints ?? HintRuleTable.Default;
            Store = store ?? StoreCatalog.Default;
            Prompts = prompts ?? AccountPromptRuleTable.Default;
            ChapterGate = chapterGate ?? ChapterGateTable.Default;
            Continue = carryOn ?? ContinueTable.Default;
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
        /// The heart gate, published with the curve because it multiplies every number in
        /// it. XP and credits are paid per finished glade; the gate decides how many
        /// glades a day a player gets to finish. Retuning one without the other changes the
        /// economy by a factor nobody wrote down — the same argument the golden bands make,
        /// one level up.
        /// </summary>
        public HeartRuleTable Hearts { get; }

        /// <summary>
        /// The hint pool, published with the curve because it is the gate's other half.
        ///
        /// The heart table decides how many attempts a day a player gets; this decides how
        /// many of those attempts they can buy their way out of. Both multiply the number of
        /// glades finished per day, which is what every credit figure above them is paid per,
        /// so tuning one without the other in front of you moves the economy by a factor
        /// nobody wrote down — the argument <see cref="Hearts"/> already makes, for the
        /// resource sitting on the other side of the same decision.
        /// </summary>
        public HintRuleTable Hints { get; }

        /// <summary>
        /// What the shop sells, published with the curve because it is the curve's exit.
        ///
        /// <para>
        /// Every other table here decides what play <em>pays</em>; this one decides what
        /// money buys, and the only way to know whether a coin pack is generous or insulting
        /// is to hold it against how much a day of play earns. A shop tuned without the
        /// reward rates in front of you is a shop priced against a game you are guessing at.
        /// </para>
        /// <para>
        /// It rides here for a second reason the others do not have: the server reads the
        /// same block. See <c>StoreCatalog</c>.
        /// </para>
        /// </summary>
        public StoreCatalog Store { get; }

        /// <summary>
        /// How often an anonymous player may be asked to attach a real account.
        ///
        /// Published for the reason the ad cooldown is: it paces an interruption, nothing
        /// adjudicates it, and the right value is found from live link rates rather than
        /// guessed before launch. See <see cref="AccountPromptRuleTable"/>.
        /// </summary>
        public AccountPromptRuleTable Prompts { get; }

        /// <summary>
        /// How much of a chapter opens the chapter after it.
        ///
        /// Published with the curve because it is what every number above it is paid
        /// <em>per</em>. The reward rule says what a glade is worth, the heart gate says how
        /// many a player gets to attempt in a day, and this says how many are worth attempting
        /// at all — a gate tightened without the curve in front of you is a game whose next
        /// chapter costs more replays than the replays pay for. See <see cref="ChapterGateTable"/>.
        /// </summary>
        public ChapterGateTable ChapterGate { get; }

        /// <summary>
        /// What it costs to carry a lost run on.
        ///
        /// <para>
        /// Rides with the curve for the reason every other block here does: one table, one
        /// fetch, one atomic swap. A separately-loaded price would let a player briefly hold a
        /// new continue cost against an old heart gate, and those two numbers only mean
        /// anything beside each other — what a second chance is worth is entirely a question
        /// of what losing costs. See <see cref="ContinueTable"/>.
        /// </para>
        /// </summary>
        public ContinueTable Continue { get; }

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
            golden: GoldenTable.Default,
            hearts: HeartRuleTable.Default,
            hints: HintRuleTable.Default,
            store: StoreCatalog.Default,
            prompts: AccountPromptRuleTable.Default,
            chapterGate: ChapterGateTable.Default,
            carryOn: ContinueTable.Default);

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

            // And once more, for the block with the widest blast radius. An unreadable
            // hearts block costs the live pacing of the gate and nothing else; it must not
            // take the curve or any of the four tables above it down with it, and it must
            // never be able to leave the game unplayable — which is why every field in it
            // clamps to a built-in rather than rejecting the block.
            var hearts = HeartRuleTable.Resolve(dto.hearts, problems);

            // And once more, for the gate's other half. An unreadable hints block costs the
            // live pacing of the hint pool and nothing else — every field clamps to a
            // built-in rather than rejecting the block, so the worst case is a pool that
            // refills at the shipped rate instead of the published one.
            var hints = HintRuleTable.Resolve(dto.hints, problems);

            // And once more, for the only block whose failure costs real money rather than
            // live tuning. An unreadable product is dropped by name rather than clamped —
            // see StoreCatalog.Resolve — so the worst case is a shelf with a card missing
            // from it, which is visible at a glance, instead of a card promising an amount
            // the server would refuse to honour.
            var store = StoreCatalog.Resolve(dto.store, problems);

            // And the only block that is not about the game at all. An unreadable prompts
            // block costs the live pacing of one panel; the built-in pacing is a working game
            // and a working shop.
            var prompts = AccountPromptRuleTable.Resolve(dto.prompts, problems);

            // And once more, for the block that paces the content rather than the economy. An
            // unreadable chapter gate costs the live pacing of how a catalog opens up and
            // nothing else — the built-in gate is a working game — and it clamps rather than
            // rejecting, because the one value that must never be reachable by a typo is a
            // requirement no amount of play could meet.
            var chapterGate = ChapterGateTable.Resolve(dto.chapterGate, problems);

            // And the price of a second chance, which is neither an economy block nor a
            // pacing one but sits between them: it decides what losing actually costs, so it
            // is tuned against the heart gate above rather than against the shop below. An
            // unreadable block costs live tuning and never the feature — see ContinueTable.
            var carryOn = ContinueTable.Resolve(dto.continueRun, problems);

            table = Build(dto.xpToNext, dto.tailXpToNext, dto.tailXpIncrement, maxLevel,
                          defaultRule, chapterRules, daily, ads, streak, golden, hearts, hints,
                          store, prompts, chapterGate, carryOn);
            return true;
        }

        static ProgressionTable Build(int[] xpToNext, int tailXpToNext, int tailXpIncrement,
                                      int maxLevel, RewardRule defaultRule,
                                      Dictionary<ChapterId, RewardRule> chapterRules,
                                      DailyChestTable daily, AdRewardTable ads,
                                      StreakTable streak, GoldenTable golden,
                                      HeartRuleTable hearts, HintRuleTable hints,
                                      StoreCatalog store,
                                      AccountPromptRuleTable prompts, ChapterGateTable chapterGate,
                                      ContinueTable carryOn)
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

            return new ProgressionTable(cumulative, defaultRule, chapterRules, daily, ads, streak,
                                        golden, hearts, hints, store, prompts,
                                        chapterGate, carryOn);
        }
    }
}
