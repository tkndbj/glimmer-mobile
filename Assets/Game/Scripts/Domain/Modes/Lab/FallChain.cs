namespace GlimmerGrove.Modes
{
    /// <summary>
    /// How far a chain ran, and what the screen is entitled to say about it.
    ///
    /// <para>
    /// <b>The ladder is here rather than in the view for the reason every other rule is.</b> A
    /// <c>switch</c> inside a <c>MonoBehaviour</c> is the one place in this project nothing can
    /// be proved, and this one decides both what a player is told and how loud it is said — the
    /// two things a celebration can most easily get wrong in opposite directions. Shouting on
    /// every burst is noise nobody reads after ten minutes; staying silent on a six-wave chain
    /// wastes the best thing the mode does.
    /// </para>
    /// <para>
    /// <b>A single burst is not a chain and is deliberately not counted.</b> Most drops that do
    /// anything at all burst exactly one mote, so a count starting at one would put a number on
    /// the screen almost every turn and mean nothing by the second level. The count starts where
    /// the wash reached something — which is the thing actually worth noticing — and the *name*
    /// starts one rung above that again.
    /// </para>
    /// <para>
    /// The words are written out rather than built from the tier, for invariant 6's reason: a
    /// key assembled at runtime is a key the build's string scanner cannot see, and it ships
    /// missing in whichever language nobody tested.
    /// </para>
    /// </summary>
    public static class FallChain
    {
        /// <summary>Waves below this are a burst rather than a chain: no count and no name.</summary>
        public const int CountFrom = 2;

        /// <summary>Waves below this are counted but not named.</summary>
        public const int NameFrom = 3;

        /// <summary>Where the ladder stops climbing. Past it every chain is the top word.</summary>
        public const int TopTier = 4;

        /// <summary>Whether this drop is worth counting out loud.</summary>
        public static bool Counts(int waves) => waves >= CountFrom;

        /// <summary>
        /// How far up the ladder a chain of this length reaches, 0 upward. Drives how big and
        /// how bright the screen draws it, so that a five is visibly a bigger event than a two
        /// without anybody having to read the number.
        /// </summary>
        public static int Tier(int waves)
        {
            int tier = waves - CountFrom;
            if (tier < 0) return 0;
            return tier > TopTier ? TopTier : tier;
        }

        /// <summary>
        /// What a chain of this length is called, or null for one that is only counted.
        /// </summary>
        public static string WordKey(int waves)
        {
            if (waves < NameFrom) return null;

            switch (waves)
            {
                case 3: return "mode.fall.chain_amazing";
                case 4: return "mode.fall.chain_epic";
                case 5: return "mode.fall.chain_legendary";
                default: return "mode.fall.chain_unreal";
            }
        }

        /// <summary>
        /// How big the running count is drawn, in points, for the wave that has just landed.
        ///
        /// It climbs with the wave rather than with the chain's final length, because the number
        /// appears while the chain is still running and nobody knows how it ends — which is the
        /// whole tension of watching one. Capped so a runaway chain does not draw a number
        /// wider than the well.
        /// </summary>
        public static int PointsFor(int wave)
        {
            int points = 74 + (wave - 1) * 16;
            return points > 150 ? 150 : points;
        }

        /// <summary>How big the word is drawn. One size above the count that preceded it.</summary>
        public static int WordPointsFor(int waves)
        {
            int points = 86 + Tier(waves) * 14;
            return points > 142 ? 142 : points;
        }
    }
}
