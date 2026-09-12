using System;
using GlimmerGrove.Content;

namespace GlimmerGrove.Wards
{
    /// <summary>
    /// Which seats of the line a player may arrange, and what opens the rest.
    ///
    /// <para>
    /// <b>A line grows over a chapter rather than arriving whole, and this is the loadout's half
    /// of that.</b> Thornwatch's opening rungs stand three wards and three gem colours; the fourth
    /// arrives part-way in. What that buys is the one thing the colour lock costs a new player:
    /// four colours, four turrets and four lanes at once is a lot to hold on the rung where the
    /// verb is still being worked out, and three is the fewest a jewel board can be dealt from at
    /// all (<see cref="Modes.SiegeLayout.MinWards"/> records the measurement).
    /// </para>
    /// <para>
    /// <b>Counted in levels cleared rather than read off the levels themselves.</b> The honest
    /// question is "has this player met a rung that stands a yellow ward", and answering it
    /// directly would mean opening every chapter body in the mode — which the boot path may never
    /// do (invariant 4a) and this screen has no reason to. Levels chain, so how many have been
    /// cleared is the same fact in a form the catalog already holds.
    /// </para>
    /// <para>
    /// <b>A table rather than content, and a test rather than trust.</b> One integer that has to
    /// agree with a chapter's authored ward lines is not worth a config push; what it is worth is
    /// a fixture holding it to those ward lines, so a chapter re-authored to stand four wards from
    /// the first rung fails offline instead of shipping a padlock over a turret that is already
    /// fighting.
    /// </para>
    /// </summary>
    public static class WardSeats
    {
        /// <summary>
        /// How many levels of the mode's own ladder have to be cleared before each seat of
        /// <see cref="WardLine.Colours"/> may be arranged.
        ///
        /// <b>Nought is a real answer and is what the first three carry.</b> A seat the opening
        /// rung already stands is a seat the player is playing with, and a padlock over a turret
        /// that is visibly shooting for them would be the game refusing something it had just
        /// handed over.
        /// </summary>
        public static readonly int[] OpensAfter = { 0, 0, 0, 3 };

        /// <summary>Whether this seat may be arranged, given how many levels have been cleared.</summary>
        public static bool IsOpen(int seat, int cleared)
            => cleared >= Needed(seat);

        /// <summary>
        /// How many clears this seat wants. Nought for anything outside the table, because a seat
        /// this build has never heard of must not be a seat nobody can ever open.
        /// </summary>
        public static int Needed(int seat)
            => seat < 0 || seat >= OpensAfter.Length ? 0 : OpensAfter[seat];

        /// <summary>
        /// How many levels of <paramref name="mode"/>'s own ladder this player has cleared.
        ///
        /// <para>
        /// <b>The main track only</b>, which <c>CatalogIndex.LevelsIn</c> already answers
        /// (invariant 43): an endless lane has one level and no ladder, so counting its clears
        /// would open the whole line on a player who had never met a second colour.
        /// </para>
        /// <para>
        /// <b>Cleared rather than unlocked</b>, for invariant 24's distinction: a level attempted
        /// and lost is not a level met.
        /// </para>
        /// <para>
        /// <b>Handed a predicate rather than reaching for <c>PlayerProgress</c></b>, which is a
        /// static: a rule that reads a global is a rule no fixture can put a player in front of.
        /// </para>
        /// </summary>
        public static int ClearedIn(CatalogIndex catalog, GameMode mode,
                                    Func<LevelId, bool> cleared)
        {
            if (catalog == null || cleared == null) return 0;

            var levels = catalog.LevelsIn(mode);
            int done = 0;

            for (int i = 0; i < levels.Count; i++)
                if (cleared(levels[i])) done++;

            return done;
        }
    }
}
