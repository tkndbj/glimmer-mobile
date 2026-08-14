namespace GlimmerGrove.Progression
{
    /// <summary>
    /// The honorific under a player's name, derived from their keeper level.
    ///
    /// Kept in Domain rather than beside the profile screen because it is a rule about
    /// progression, not a decoration: the moment ranks or any social surface ships, the
    /// server has to agree with the client about what to call somebody, and a copy of
    /// these thresholds living in a screen would be the copy that drifts.
    ///
    /// Keys are written out rather than composed from the tier index, so the build's
    /// localisation scan can see every one of them.
    /// </summary>
    public static class KeeperTitle
    {
        /// <summary>Lowest level of each tier, ascending. Parallel to <see cref="Keys"/>.</summary>
        static readonly int[] Floors = { 0, 5, 10, 20, 35 };

        static readonly string[] Keys =
        {
            "ui.title.seedling",
            "ui.title.sapling",
            "ui.title.keeper",
            "ui.title.warden",
            "ui.title.elder",
        };

        /// <summary>The loc key for a keeper level. Never returns null.</summary>
        public static string KeyFor(int level)
        {
            int tier = 0;
            for (int i = 0; i < Floors.Length; i++)
                if (level >= Floors[i]) tier = i;

            return Keys[tier];
        }

        /// <summary>
        /// The level at which the next honorific arrives, or 0 when this is the last
        /// one. Lets the profile say what is coming rather than only what is held.
        /// </summary>
        public static int NextTierLevel(int level)
        {
            foreach (int floor in Floors)
                if (floor > level) return floor;

            return 0;
        }
    }
}
