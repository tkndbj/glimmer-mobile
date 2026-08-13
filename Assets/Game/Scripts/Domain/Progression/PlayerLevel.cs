namespace GlimmerGrove.Progression
{
    /// <summary>
    /// A resolved position on the XP curve: which level, and how far through it.
    ///
    /// Carries everything a HUD needs so no screen has to do curve arithmetic of its
    /// own — a bar that computed its own fill would be a second implementation of the
    /// curve, and the two would disagree the first time the table was retuned.
    /// </summary>
    public readonly struct PlayerLevel
    {
        public readonly int Level;

        /// <summary>Lifetime XP this level was resolved from.</summary>
        public readonly long TotalXp;

        /// <summary>XP earned since reaching <see cref="Level"/>.</summary>
        public readonly long XpIntoLevel;

        /// <summary>XP the whole of this level costs. 0 at the cap.</summary>
        public readonly long XpForNextLevel;

        public readonly bool IsMaxLevel;

        public PlayerLevel(int level, long totalXp, long xpIntoLevel, long xpForNextLevel, bool isMaxLevel)
        {
            Level = level;
            TotalXp = totalXp;
            XpIntoLevel = xpIntoLevel;
            XpForNextLevel = xpForNextLevel;
            IsMaxLevel = isMaxLevel;
        }

        /// <summary>XP still owed before the next level. 0 at the cap.</summary>
        public long XpRemaining
        {
            get
            {
                if (IsMaxLevel) return 0L;
                long left = XpForNextLevel - XpIntoLevel;
                return left < 0 ? 0L : left;
            }
        }

        /// <summary>0..1 through this level. Reads as full at the cap rather than empty.</summary>
        public float Progress01
        {
            get
            {
                if (IsMaxLevel || XpForNextLevel <= 0L) return 1f;
                float t = XpIntoLevel / (float)XpForNextLevel;
                return t < 0f ? 0f : t > 1f ? 1f : t;
            }
        }

        public override string ToString()
            => IsMaxLevel ? $"level {Level} (max, {TotalXp} xp)"
                          : $"level {Level} ({XpIntoLevel}/{XpForNextLevel} xp)";
    }
}
