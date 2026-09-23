using System.Collections.Generic;

namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// The bounds a published challenge file is checked against, and the numbers used when a
    /// block is unwritten.
    ///
    /// <para>
    /// <see cref="Progression.EndlessLimits"/>' job for the daily challenges, and its argument
    /// transfers whole: content may retune what a clear is worth and what a deal costs, it may
    /// not redefine what the ledger is allowed to hold. Everything here is a compile-time
    /// constant precisely because it is what a published file is checked <em>against</em> — a
    /// limit that could itself be published would not be a limit.
    /// </para>
    /// <para>
    /// <b>The defaults are contract with the server</b> (<c>DEFAULT_CHALLENGES</c> in
    /// <c>functions/src/challenges.ts</c>). Both sides fall back to them when no block has been
    /// published, for the Infinite lane's reason: a server one deploy behind would otherwise
    /// derive a lower keeper level than the device and silently drop what that level gated
    /// (invariant 19a). Held together by <c>challengeCases</c> in the shared vectors.
    /// </para>
    /// </summary>
    public static class ChallengeLimits
    {
        // ------------------------------------------------------- the structural bounds
        /// <summary>
        /// The most lifetime clears one genre's row of the ledger may ever hold.
        ///
        /// <b>Not the ceiling a player experiences</b> — that is
        /// <see cref="ChallengeRewardRule.MaxClears"/>, and it is content. This is the bound the
        /// ledger's own clamp uses, chosen once and chosen wide, because a stored monotonic count
        /// clamped against a <em>published</em> number would be cut downward on whichever device
        /// had fetched a lowered table, and a count that only rises is the whole of what makes it
        /// mergeable (invariant 11b). <see cref="Progression.EndlessLimits.HardMaxWaves"/> carries
        /// the full argument.
        /// </summary>
        public const int HardMaxClears = 1000000;

        /// <summary>
        /// The most genres one day's rows may name. The rules bound the list and a client
        /// writing a longer one loses <em>every</em> save write (invariant 12a); the enum has
        /// four members today and a build can only ever write one row per genre it knows.
        /// </summary>
        public const int MaxTodayRows = 32;

        /// <summary>The most lifetime rows the ledger may carry, one per genre spelling ever cleared.</summary>
        public const int MaxClearRows = 64;

        /// <summary>The most deals a save may hold a purchase of, one row per tier id.</summary>
        public const int MaxTierRows = 16;

        /// <summary>The most plays one day's row may record, attempts or wins. Far above any allowance.</summary>
        public const int MaxPlaysPerDay = 100000;

        // ------------------------------------------------------------ published bounds
        /// <summary>The most free plays a day a content file may promise. A typo guard.</summary>
        public const int MaxFreePlays = 100;

        /// <summary>The most plays a day a deal may promise.</summary>
        public const int MaxTierPlays = 1000;

        /// <summary>The most gems a deal may cost. Matches <c>EventRules.MaxPassGems</c>'s shape.</summary>
        public const int MaxTierGems = 100000;

        /// <summary>The longest a deal may run.</summary>
        public const int MaxTierDays = 365;

        /// <summary>The most deals a file may list. The ledger bounds its rows to the same figure.</summary>
        public const int MaxTiers = MaxTierRows;

        /// <summary>
        /// The most credits a content file may pay for one clear.
        ///
        /// <b>A typo guard, and the serious one.</b> XP buys a keeper level and a keeper level
        /// buys nothing; a credit buys a turret. A misplaced nought here is the whole shelf
        /// handed to everybody at once, so this is set close to the figure that ships.
        /// </summary>
        public const int MaxCoinsPerClear = 200;

        /// <summary>The most XP a content file may pay for one clear. A guard against a typo.</summary>
        public const int MaxXpPerClear = 1000;

        /// <summary>The most lifetime clears a content file may pay for. See <see cref="HardMaxClears"/>.</summary>
        public const int MaxMaxClears = HardMaxClears;

        // ------------------------------------------------------------------ defaults
        /// <summary>Two plays of each genre a day, which is the owner's figure.</summary>
        public const int DefaultFreePlays = 2;

        /// <summary>
        /// Forty credits a clear, so two free plays of four genres are worth 320 a day — a third
        /// of what the chests, tasks, streak and season pay together (936) and a fifth of what a
        /// player who watches every advert collects (~7,160). Half a glade's first clear, for a
        /// puzzle that takes a minute or three.
        /// </summary>
        public const int DefaultCoinsPerClear = 40;

        /// <summary>
        /// Twenty XP a clear: a seventh of a three-starred glade (150) and a little over an
        /// Infinite wave (15). Eight free clears a day are one glade's XP.
        /// </summary>
        public const int DefaultXpPerClear = 20;

        /// <summary>
        /// Twenty-five thousand lifetime clears, which is 500,000 XP at the rate above — a third
        /// of the Infinite lane's ceiling. Honest play is bounded by the allowance: a player on
        /// the largest deal with four genres cannot pass a hundred a day.
        /// </summary>
        public const int DefaultMaxClears = 25000;
    }

    /// <summary>
    /// Which deal governs a day, and how many plays it allows — the one rule about deals both
    /// sides compute.
    ///
    /// <b>Pinned by <c>challengeAllowanceCases</c> in the shared vectors</b>, because the client
    /// draws plays off it and the server bounds coin claims by it (<c>allowanceOn</c> in
    /// <c>functions/src/challenges.ts</c>): a disagreement is a claim refused for a play the
    /// page offered, which is money shown and taken back (45d).
    /// </summary>
    public static class ChallengeAllowance
    {
        /// <summary>
        /// The deal with the most plays whose window covers <paramref name="day"/>, or null.
        /// Two windows can overlap — a larger bought under a smaller — and the larger governs
        /// while it runs, the smaller resuming after, because a date per tier is what is held.
        /// </summary>
        public static ChallengeTier Governing(IReadOnlyList<ChallengeTier> tiers,
                                              IReadOnlyDictionary<string, int> heldFromDay, int day)
        {
            ChallengeTier best = null;
            if (tiers == null || heldFromDay == null) return null;

            for (int i = 0; i < tiers.Count; i++)
            {
                var tier = tiers[i];
                if (tier == null || !heldFromDay.TryGetValue(tier.Id, out int from) || !tier.Covers(from, day)) continue;
                if (best == null || tier.Plays > best.Plays) best = tier;
            }

            return best;
        }

        /// <summary>Plays of each genre allowed on a day: the governing deal's figure, else the free one.</summary>
        public static int On(IReadOnlyList<ChallengeTier> tiers, IReadOnlyDictionary<string, int> heldFromDay,
                             int day, int freePlays)
        {
            var held = Governing(tiers, heldFromDay, day);
            return held != null ? held.Plays : freePlays;
        }
    }

    /// <summary>One deal, as the game reads it. See <see cref="ChallengeTierDto"/>.</summary>
    public sealed class ChallengeTier
    {
        public readonly string Id;
        public readonly int Gems, Plays, Days;

        public ChallengeTier(string id, int gems, int plays, int days)
        {
            Id = id;
            Gems = gems;
            Plays = plays;
            Days = days;
        }

        /// <summary>A deal's name derives from its id and cannot be overridden (invariant 5a).</summary>
        public string NameKey => "challenge.tier." + Id + ".name";

        /// <summary>Whether a deal bought on <paramref name="fromDay"/> still runs on <paramref name="day"/>.</summary>
        public bool Covers(int fromDay, int day)
            => fromDay > 0 && day >= fromDay && day < fromDay + Days;
    }

    /// <summary>
    /// What a cleared level pays — content, not code. See <see cref="ChallengeRewardDto"/>.
    ///
    /// <para>
    /// <b>The XP half is the third source of XP in the game that is not a star</b>, and it is
    /// built exactly as the Infinite lane's was (invariant 9d): a monotonic lifetime tally per
    /// genre joined by <c>max</c>, a rate and a ceiling, with the XP a pure function of the
    /// tally. Nothing is claimed and nothing is granted for it. It is a separate addend in
    /// <c>PlayerProgression</c>, never a clause of <c>ProgressionLedger</c>, so the star rule the
    /// shared reward vectors pin stays a pure function of the star records.
    /// </para>
    /// <para>
    /// <b>The server derives the same figure</b> (<c>challengeXp</c> in
    /// <c>functions/src/challenges.ts</c>) and the two are held together by <c>challengeCases</c>
    /// in the shared vectors, because a keeper level the two halves disagree about is a card
    /// that silently drops whatever that level gated (19a).
    /// </para>
    /// </summary>
    public sealed class ChallengeRewardRule
    {
        ChallengeRewardRule(int coins, int xp, int maxClears)
        {
            Coins = coins;
            Xp = xp;
            MaxClears = maxClears;
        }

        /// <summary>Credits one clear pays. Nought withdraws the payment without withdrawing the mode.</summary>
        public int Coins { get; }

        /// <summary>XP one clear pays. Nought withdraws the payment.</summary>
        public int Xp { get; }

        /// <summary>
        /// The most lifetime clears ever paid for, across every genre. Applied at derivation and
        /// never at storage, for <see cref="ChallengeLimits.HardMaxClears"/>' reason.
        /// </summary>
        public int MaxClears { get; }

        public static readonly ChallengeRewardRule Default = new ChallengeRewardRule(
            ChallengeLimits.DefaultCoinsPerClear,
            ChallengeLimits.DefaultXpPerClear,
            ChallengeLimits.DefaultMaxClears);

        public bool PaysCoins => Coins > 0;
        public bool PaysXp => Xp > 0 && MaxClears > 0;

        /// <summary>
        /// What a lifetime clear count is worth in XP. The ceiling is applied to the count and
        /// not to the product, so the two sides of the wire cannot disagree about rounding.
        /// <c>long</c> throughout because the published maximum times the rate overflows an int.
        /// </summary>
        public long XpFor(long lifetimeClears)
        {
            if (lifetimeClears <= 0L || !PaysXp) return 0L;

            long clears = lifetimeClears > MaxClears ? MaxClears : lifetimeClears;
            return clears * Xp;
        }

        /// <summary>The most this rule could ever pay in XP. Printed by both content gates.</summary>
        public long MaxXp => PaysXp ? (long)MaxClears * Xp : 0L;

        /// <summary>
        /// Reads the optional <c>rewards</c> block. Never throws and never returns null; anything
        /// wrong is named and clamped, and an unwritten block is the built-in figures — on both
        /// sides, for the reason the class comment gives.
        /// </summary>
        public static ChallengeRewardRule Resolve(ChallengeRewardDto dto, List<string> problems)
        {
            problems = problems ?? new List<string>();
            if (dto == null || !dto.IsAuthored) return Default;

            // A written block is read field by field, and a nought in a rate is a withdrawn
            // payment rather than a typo to repair — the Infinite lane's lesson, where the first
            // reader raised a nought ceiling back to the default and the shared vectors caught the
            // server paying nought against it. An unwritten `maxClears` beside a written rate
            // inherits, so a rate with no bound at all cannot be expressed.
            int coins = Clamp(dto.coins, 0, ChallengeLimits.MaxCoinsPerClear, "challenges rewards.coins", problems);
            int xp = Clamp(dto.xp, 0, ChallengeLimits.MaxXpPerClear, "challenges rewards.xp", problems);
            // A written block with no ceiling pays no XP, exactly as the server reads the same
            // block (`challengeXp`: `maxClears <= 0` is nought). It is not inherited, because
            // the two halves must agree on a published file byte for byte; `content.py` and the
            // seeder refuse the shape — a rate beside no ceiling — so it never ships by accident.
            int maxClears = Clamp(dto.maxClears, 0, ChallengeLimits.MaxMaxClears, "challenges rewards.maxClears", problems);

            return new ChallengeRewardRule(coins, xp, maxClears);
        }

        static int Clamp(int authored, int min, int max, string name, List<string> problems)
        {
            if (authored < min)
            {
                problems.Add($"{name} is {authored}, below the supported minimum {min}; clamped");
                return min;
            }

            if (authored > max)
            {
                problems.Add($"{name} is {authored}, above the supported maximum {max}; clamped");
                return max;
            }

            return authored;
        }
    }
}
