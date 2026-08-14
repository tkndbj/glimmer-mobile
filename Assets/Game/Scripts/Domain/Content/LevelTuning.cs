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

        public LevelTuning(int par, float goldFactor, float silverFactor, int hintAllowance,
                           float budgetFactor = 0f)
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

        public int StarsFor(int moves)
        {
            if (moves <= GoldThreshold) return 3;
            if (moves <= SilverThreshold) return 2;
            return 1;
        }
    }
}
