namespace GlimmerGrove.Modes
{
    /// <summary>
    /// A post put back as a suspended Shuffle run left it (MODES.md 59s).
    ///
    /// <para>
    /// <b>Only what outlives a wave.</b> A run is suspended when a hand is owed, which is a
    /// breather or the frame a wave steps out, so what a post carries forward is its health,
    /// whether it is standing, the fuel in its tube, the charges it has banked and its rank.
    /// Everything with a clock on it (a douse, a shackle, a seal, rubble, a barrier, a cooldown)
    /// is a thing a raider did inside a wave and starts the resumed run clear.
    /// </para>
    /// <para>
    /// <b>After <see cref="Refit"/>, never before</b>: the build decides a post's full health,
    /// capacity and charge cap, and this writes inside them, so a figure written by an older
    /// build or a different line is clamped rather than trusted.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>
        /// Writes post <paramref name="ward"/>'s carried state, answering whether there was such
        /// a post. A fallen post is written exactly as <see cref="Topple"/> leaves one.
        /// </summary>
        internal bool Seat(int ward, bool standing, int health, float fuel, int charges, int rank)
        {
            if (ward < 0 || ward >= _wards.Length) return false;

            var post = _wards[ward];

            post.Rank = rank < 0 ? 0 : rank > SiegeTuning.MaxRank ? SiegeTuning.MaxRank : rank;

            if (!standing)
            {
                post.Alive = false;
                post.Health = 0;
                post.Fuel = 0f;
                post.Charges = 0;
                return true;
            }

            post.Alive = true;
            post.Health = health < 1 ? 1 : health > post.Full ? post.Full : health;
            post.Fuel = fuel < 0f || float.IsNaN(fuel) ? 0f : fuel > post.Capacity ? post.Capacity : fuel;
            post.Charges = charges < 0 ? 0 : charges > post.ChargeCap ? post.ChargeCap : charges;
            return true;
        }
    }
}
