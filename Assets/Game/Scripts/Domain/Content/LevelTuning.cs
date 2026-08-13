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

        /// <summary>Minimum turns needed to solve the board, computed at authoring time.</summary>
        public readonly int Par;

        /// <summary>Move budget multipliers over par for three and two stars.</summary>
        public readonly float GoldFactor;
        public readonly float SilverFactor;

        public readonly int HintAllowance;

        public LevelTuning(int par, float goldFactor, float silverFactor, int hintAllowance)
        {
            Par = Mathf.Max(1, par);
            GoldFactor = goldFactor > 0f ? goldFactor : DefaultGoldFactor;
            SilverFactor = silverFactor > 0f ? silverFactor : DefaultSilverFactor;
            HintAllowance = Mathf.Max(0, hintAllowance);
        }

        public static LevelTuning Default(int par)
            => new LevelTuning(par, DefaultGoldFactor, DefaultSilverFactor, DefaultHintAllowance);

        public int GoldThreshold => Mathf.CeilToInt(Par * GoldFactor);
        public int SilverThreshold => Mathf.CeilToInt(Par * SilverFactor);

        public int StarsFor(int moves)
        {
            if (moves <= GoldThreshold) return 3;
            if (moves <= SilverThreshold) return 2;
            return 1;
        }
    }
}
