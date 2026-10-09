using GlimmerGrove.Wards;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// The line every Shuffle run stands: four of one turret at the first star, for everybody.
    ///
    /// <para>
    /// <b>The same line for everyone is the whole of what makes a best wave comparable</b>, and
    /// it is why the lane reads no loadout and no star ledger: a player who bought the shelf and
    /// one who bought nothing open on the same four posts, and what separates their runs is the
    /// cards they took. The turret is <see cref="WardModel.Elemental"/> (the Breaker), which is
    /// the turret the owner named and the one whose four elemental reels are resident in every
    /// siege cast - so the line costs the run no scope beyond its four chassis.
    /// </para>
    /// <para>
    /// <b>Read off the live roster, never a constant model</b>, so a retune of the Breaker's
    /// figures reaches this lane with the rest of the game. A roster with no Breaker in it -
    /// which no shipped roster is, and a test holds - falls back to the starter rather than to
    /// nothing, for <see cref="WardLine.Resolve"/>'s reason: the answer is always four turrets.
    /// </para>
    /// </summary>
    public static class ShuffleLine
    {
        /// <summary>The id of the turret the line stands. <see cref="WardModel.Elemental"/>.</summary>
        public static string TurretId => WardModel.Elemental;

        /// <summary>The line, off <paramref name="catalog"/>. Null is the shipped roster.</summary>
        public static WardLine Of(WardCatalog catalog)
        {
            catalog = catalog ?? WardCatalog.Default;
            return WardLine.Uniform(catalog, catalog.Find(TurretId) ?? catalog.Starter, WardStars.Least);
        }

        /// <summary>The line, off the roster the game is running.</summary>
        public static WardLine Line => Of(Progression.ProgressionRules.Table.Wards);
    }
}
