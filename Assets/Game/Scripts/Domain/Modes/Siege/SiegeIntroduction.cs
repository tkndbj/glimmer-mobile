using System;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The opening encounter's bounded choreography and forgiving combat contract.
    /// Only a layout explicitly authored with introduction=true uses these rules.
    /// </summary>
    public static class SiegeIntroduction
    {
        public const float RiftAt = .25f;
        public const float EmergeAt = .85f;
        public const float LandAt = 1.65f;
        public const float SpeakAt = 1.95f;
        public const float SummonAt = 4.15f;
        public const float PlayAt = 5.35f;
        public const float RetreatFor = 3.4f;

        /// <summary>Even an unattended first encounter leaves three quarters of each ward.</summary>
        public static int MinimumHealth(int full) => Math.Max(1, (full * 3 + 3) / 4);

        public static int Damage(int health, int full, int requested)
            => Math.Min(Math.Max(0, health - MinimumHealth(full)), Math.Min(1, Math.Max(0, requested)));

        public static int HealthAfter(int health, int full, int damage)
            => health - Damage(health, full, damage);
    }
}
