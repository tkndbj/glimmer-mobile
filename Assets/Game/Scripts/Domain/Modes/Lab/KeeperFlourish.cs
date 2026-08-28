namespace GlimmerGrove.Modes
{
    /// <summary>
    /// How many tiles one planting opened at once, and what the screen is entitled to say about
    /// it.
    ///
    /// <para>
    /// <b>The ladder is here rather than in the view for the reason every other rule is.</b> A
    /// <c>switch</c> inside a <c>MonoBehaviour</c> is the one place in this project nothing can
    /// be proved, and this one decides both what a player is told and how loud it is said — the
    /// two things a celebration can most easily get wrong in opposite directions. Shouting on
    /// every bloom is noise nobody reads after ten minutes; staying silent on a four-bloom
    /// wastes the best thing the mode does.
    /// </para>
    /// <para>
    /// <b>A single bloom is not a flourish and is deliberately not counted.</b> Most plantings
    /// that do anything at all open exactly one tile, so a count starting at one would put a
    /// number on the screen almost every turn and mean nothing by the second level. The count
    /// starts where a planting reached past the cell it landed on — which is the thing actually
    /// worth noticing — and the <em>name</em> starts one rung above that again.
    /// </para>
    /// <para>
    /// <b>Five is the ceiling and it is a fact about the board, not a taste.</b> A planting is
    /// checked against the cell it lands on and the four beside it, so nothing can ever open six.
    /// That is what makes the top rung mean something: a player who sees it has done the most the
    /// rules allow, and it cannot be beaten by a bigger board.
    /// </para>
    /// <para>
    /// The words are written out rather than built from the tier, for invariant 6's reason: a key
    /// assembled at runtime is a key the build's string scanner cannot see, and it ships missing
    /// in whichever language nobody tested.
    /// </para>
    /// </summary>
    public static class KeeperFlourish
    {
        /// <summary>Blooms below this are one flower opening: no count and no name.</summary>
        public const int CountFrom = 2;

        /// <summary>Blooms below this are counted but not named.</summary>
        public const int NameFrom = 3;

        /// <summary>The most one planting can ever open: the cell it lands on and its four neighbours.</summary>
        public const int Most = 5;

        /// <summary>Where the ladder stops climbing.</summary>
        public const int TopTier = Most - CountFrom;

        /// <summary>Whether this planting is worth counting out loud.</summary>
        public static bool Counts(int blooms) => blooms >= CountFrom;

        /// <summary>
        /// How far up the ladder a flourish of this size reaches, 0 upward. Drives how big and how
        /// bright the screen draws it, so that a four is visibly a bigger event than a two without
        /// anybody having to read the number.
        /// </summary>
        public static int Tier(int blooms)
        {
            int tier = blooms - CountFrom;
            if (tier < 0) return 0;
            return tier > TopTier ? TopTier : tier;
        }

        /// <summary>What a flourish of this size is called, or null for one that is only counted.</summary>
        public static string WordKey(int blooms)
        {
            if (blooms < NameFrom) return null;

            switch (blooms)
            {
                case 3: return "mode.keeper.flourish_lovely";
                case 4: return "mode.keeper.flourish_radiant";
                default: return "mode.keeper.flourish_glorious";
            }
        }

        /// <summary>
        /// How big the count is drawn, in points.
        ///
        /// Capped so that the top rung cannot draw a number wider than the grove it is drawn over.
        /// </summary>
        public static int PointsFor(int blooms)
        {
            int points = 78 + Tier(blooms) * 18;
            return points > 150 ? 150 : points;
        }

        /// <summary>How big the word is drawn. One size above the count that preceded it.</summary>
        public static int WordPointsFor(int blooms)
        {
            int points = 88 + Tier(blooms) * 16;
            return points > 142 ? 142 : points;
        }
    }
}
