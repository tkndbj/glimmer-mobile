namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// The only progression numbers that are stored, and both are floors.
    ///
    /// XP and level are derived from the star ledger, which is what keeps them
    /// consistent, forgery-resistant and safe to merge. The one thing derivation
    /// cannot promise on its own is that a number never goes <em>down</em>: retuning a
    /// reward, lengthening the curve, or a chapter briefly dropping out of the catalog
    /// would each recompute a smaller value than the player was shown yesterday.
    ///
    /// Watching your level fall is the kind of thing that ends a review with one star,
    /// so these marks ratchet. They are never a source of truth — only a lower bound
    /// on one — which is why they can be merged across devices by simply taking the
    /// larger value.
    /// </summary>
    public static class ProgressionStore
    {
        public static long XpHighWater { get; private set; }
        public static int LevelHighWater { get; private set; }

        /// <summary>Raises the XP floor. Returns true when it actually moved.</summary>
        public static bool RaiseXp(long xp)
        {
            if (xp <= XpHighWater) return false;
            XpHighWater = xp;
            return true;
        }

        /// <summary>Raises the level floor. Returns true when it actually moved.</summary>
        public static bool RaiseLevel(int level)
        {
            if (level <= LevelHighWater) return false;
            LevelHighWater = level;
            return true;
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            var p = dto.progression;

            // negative means the field was never written; there is no floor yet
            XpHighWater = p == null || p.xpHighWater < 0 ? 0L : p.xpHighWater;
            LevelHighWater = p == null || p.levelHighWater < 0 ? 0 : p.levelHighWater;
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.progression = new ProgressionStateDto
            {
                xpHighWater = XpHighWater,
                levelHighWater = LevelHighWater,
            };
        }
    }
}
