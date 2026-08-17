using UnityEngine;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Difficulty knobs, deliberately kept apart from the layout.
    ///
    /// These are the numbers you will want to change after launch, once analytics
    /// show a level is too punishing in some markets. Because they live here and
    /// not in the grid, retuning is a small remote payload that never touches the
    /// board a player already has records against.
    /// </summary>
    public sealed class LevelTuning
    {
        public const float DefaultGoldFactor = 1.35f;
        public const float DefaultSilverFactor = 2.00f;
        public const int DefaultHintAllowance = 3;

        /// <summary>
        /// Seconds of clock per par turn — the whole time limit, in one number.
        ///
        /// <para>
        /// A factor over par rather than a count of seconds, for the reason
        /// <see cref="DefaultBudgetFactor"/> is one: a flat limit is a different difficulty on
        /// every board. Sixty seconds is comfortable on a par-34 glade and close to unwinnable
        /// on the par-49 one two levels later, and nothing about authoring a number per level
        /// makes that visible to whoever authored it. Derived from par, the limit scales with a
        /// board's real difficulty and needs no authoring at all.
        /// </para>
        /// <para>
        /// It is content, so retuning it after launch is a chapter push rather than a store
        /// review — which matters more here than for the move budget, because a time limit is
        /// the one rule in the game that a player can fail through no fault of their reasoning.
        /// </para>
        /// </summary>
        public const float DefaultTimeFactor = 2.00f;

        /// <summary>
        /// Where the clock's own star thresholds sit, as fractions of the limit.
        ///
        /// <para>
        /// Fractions of the limit rather than their own factors over par, so that moving
        /// <see cref="TimeFactor"/> moves all three together. Two numbers that both had to be
        /// retuned in step is how the move budget and the one-star line came to need
        /// <see cref="MoveBudget"/>'s floor.
        /// </para>
        /// </summary>
        public const float TimeGoldFraction = .50f;
        public const float TimeSilverFraction = .75f;

        /// <summary>
        /// How many pars' worth of turns a player gets before the run is lost.
        ///
        /// Sits above <see cref="DefaultSilverFactor"/> on purpose: a budget at or below
        /// the one-star line would end runs that were still earning stars, which reads
        /// as the game cheating. Beyond that line a player is no longer solving, they
        /// are flailing, and that is the moment worth costing a heart.
        ///
        /// It is a factor rather than a number so it needs no per-level authoring and
        /// scales with a board's real difficulty — and because it is content, retuning
        /// it after launch is a chapter push rather than a store review.
        /// </summary>
        public const float DefaultBudgetFactor = 2.60f;

        /// <summary>A level authored with this has no budget and cannot be lost on moves.</summary>
        public const float Unlimited = -1f;

        /// <summary>Minimum turns needed to solve the board, computed at authoring time.</summary>
        public readonly int Par;

        /// <summary>Move budget multipliers over par for three and two stars.</summary>
        public readonly float GoldFactor;
        public readonly float SilverFactor;

        public readonly int HintAllowance;

        /// <summary>Turns allowed, as a multiple of par. <see cref="Unlimited"/> for none.</summary>
        public readonly float BudgetFactor;

        /// <summary>
        /// Seconds of clock per par turn. <see cref="Unlimited"/> for an untimed glade.
        /// </summary>
        public readonly float TimeFactor;

        public LevelTuning(int par, float goldFactor, float silverFactor, int hintAllowance,
                           float budgetFactor = 0f, float timeFactor = 0f)
        {
            Par = Mathf.Max(1, par);
            GoldFactor = goldFactor > 0f ? goldFactor : DefaultGoldFactor;
            SilverFactor = silverFactor > 0f ? silverFactor : DefaultSilverFactor;
            HintAllowance = Mathf.Max(0, hintAllowance);

            // 0 means "not authored", which takes the default. Only a deliberate
            // negative turns the budget off, so a level cannot lose its fail state by
            // omission — see the DTO convention in ContentDto.
            BudgetFactor = budgetFactor == 0f ? DefaultBudgetFactor
                         : budgetFactor < 0f ? Unlimited
                         : budgetFactor;

            // Same convention, and it has to be the same one: a tutorial glade that wants no
            // clock has to say so, because an omitted number meaning "untimed" would quietly
            // remove the fail state from every level authored before the clock existed.
            TimeFactor = timeFactor == 0f ? DefaultTimeFactor
                       : timeFactor < 0f ? Unlimited
                       : timeFactor;
        }

        public static LevelTuning Default(int par)
            => new LevelTuning(par, DefaultGoldFactor, DefaultSilverFactor, DefaultHintAllowance);

        public int GoldThreshold => Mathf.CeilToInt(Par * GoldFactor);
        public int SilverThreshold => Mathf.CeilToInt(Par * SilverFactor);

        public bool HasBudget => BudgetFactor > 0f;

        /// <summary>
        /// Turns allowed before the run is lost. <see cref="int.MaxValue"/> when the
        /// level has no budget, so callers can compare without special-casing.
        /// </summary>
        public int MoveBudget
            => HasBudget ? Mathf.Max(SilverThreshold + 1, Mathf.CeilToInt(Par * BudgetFactor))
                         : int.MaxValue;

        // ------------------------------------------------------------------ the clock
        public bool HasTimeLimit => TimeFactor > 0f;

        /// <summary>
        /// The whole clock, in milliseconds. <see cref="int.MaxValue"/> when the glade is
        /// untimed, so callers can compare without special-casing — the same shape
        /// <see cref="MoveBudget"/> uses.
        /// </summary>
        public int TimeLimitMillis
            => HasTimeLimit ? Mathf.CeilToInt(Par * TimeFactor * 1000f) : int.MaxValue;

        /// <summary>Elapsed time at or under which the clock still allows three stars.</summary>
        public int TimeGoldMillis
            => HasTimeLimit ? Mathf.CeilToInt(TimeLimitMillis * TimeGoldFraction) : int.MaxValue;

        /// <summary>Elapsed time at or under which the clock still allows two.</summary>
        public int TimeSilverMillis
            => HasTimeLimit ? Mathf.CeilToInt(TimeLimitMillis * TimeSilverFraction) : int.MaxValue;

        // ------------------------------------------------------------------ the stars
        /// <summary>How many stars the move count alone would allow.</summary>
        public int StarsForMoves(int moves)
        {
            if (moves <= GoldThreshold) return 3;
            if (moves <= SilverThreshold) return 2;
            return 1;
        }

        /// <summary>
        /// How many stars the clock alone would allow.
        ///
        /// <para>
        /// <b>Zero milliseconds is "never timed" and costs nothing</b>, which is the same
        /// convention the save file, <see cref="RunClock.Millis"/> and
        /// <c>LevelRecord.BestMillis</c> all use. A run the clock never started for — a glade
        /// authored already solved, a board finished before the first turn registered — must
        /// not be graded as though it took forever; the honest reading of "no measurement" is
        /// "no penalty", and it is also the only one that cannot punish a player for something
        /// they did not do.
        /// </para>
        /// </summary>
        public int StarsForTime(int millis)
        {
            if (!HasTimeLimit || millis <= 0) return 3;

            if (millis <= TimeGoldMillis) return 3;
            if (millis <= TimeSilverMillis) return 2;
            return 1;
        }

        /// <summary>
        /// The stars a run actually earns: the worse of what its moves allow and what its
        /// clock allows.
        ///
        /// <para>
        /// <b>The worse of the two, not a blend.</b> Three stars therefore means the player was
        /// efficient <em>and</em> quick, which is a claim they can check against either number
        /// on the victory panel. A combined score would be a third number nothing on screen
        /// shows, and a player who lost a star would have no way to know which half cost it.
        /// </para>
        /// <para>
        /// Note what this does <em>not</em> change: the move thresholds keep exactly the
        /// meaning they had, so a glade retuned for time cannot silently move its move
        /// thresholds, and a record set before the clock existed is unaffected — stars are only
        /// ever promoted (<c>LevelRecord.WithRun</c> keeps the max), so nothing already earned
        /// can be taken away by this rule arriving.
        /// </para>
        /// </summary>
        public int StarsFor(int moves, int millis)
        {
            int byMoves = StarsForMoves(moves);
            int byTime = StarsForTime(millis);
            return byMoves < byTime ? byMoves : byTime;
        }
    }
}
