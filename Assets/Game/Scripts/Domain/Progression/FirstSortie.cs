using GlimmerGrove.Content;

namespace GlimmerGrove.Progression
{
    /// <summary>Where the tutorial's finale hands a new player on to. See <see cref="FirstSortie"/>.</summary>
    public enum SortieRoute
    {
        /// <summary>The turret shelf: its two lessons, the guide, then BATTLE.</summary>
        Loadout,

        /// <summary>Straight to the first chapter's map.</summary>
        Map,
    }

    /// <summary>
    /// The new player's way from the tutorial into the game (invariant 53h): one continuous
    /// scene rather than a drop onto the hub.
    ///
    /// <para>
    /// <b>Finishing the tutorial and skipping it end on the same finale</b> - the wordmark, the
    /// line, one key - and part ways at that key. A player who played it through is walked on to
    /// the loadout, where the shelf's own two lessons, a guide and then BATTLE open the ladder's
    /// first level (<see cref="BattleLevel"/>); a player who skipped has said they want to play,
    /// so they go to the map directly. BATTLE used to lead to the map too, and ~12% of v48
    /// installs finished the loadout's lessons and never started a level there. Neither
    /// meets the hub until they choose to, which is also what puts the consent questions after
    /// their first levels rather than in front of them (invariant 55 waits for an idle hub).
    /// </para>
    /// <para>
    /// <b>Nothing is stored.</b> Who is on a first sortie is answered by how a screen was reached
    /// - the tutorial is the only door, and <c>TutorialGate</c> already decides who meets it - so
    /// a player who already owns the game can never see any of this, and a force-quit midway
    /// leaves nothing behind to resume: the next launch lands on the hub like any other.
    /// </para>
    /// </summary>
    public static class FirstSortie
    {
        /// <summary>Where the finale's key leads, for a tutorial played through or skipped.</summary>
        public static SortieRoute After(bool finished) => finished ? SortieRoute.Loadout : SortieRoute.Map;

        /// <summary>
        /// The level the loadout's BATTLE opens: the first level of the default mode's main ladder
        /// this player has not cleared - <c>s01_firstwatch</c> for anybody on a first sortie. Read
        /// off the manifest's order and the save (<see cref="LevelUnlock.NextToPlay(CatalogIndex, GameMode)"/>,
        /// the hub's and the map's own answer), never a level named in code or a position
        /// (invariant 1). <c>None</c> when the catalog has no ladder; the key then goes to the map.
        /// </summary>
        public static LevelId BattleLevel(CatalogIndex index)
            => index == null ? LevelId.None : LevelUnlock.NextToPlay(index, index.DefaultMode);
    }

    /// <summary>
    /// When the map's chapter lesson (<see cref="Mechanic.MapChapterGate"/>) may be shown.
    ///
    /// <para>
    /// <b>Not before the player has cleared <see cref="AfterCleared"/> levels of the ladder</b>,
    /// at the owner's instruction (2026-10-26): the plaque's rule - stars, not clears, open the
    /// next chapter - means nothing to somebody on their first level, and a lesson is spent once
    /// in a lifetime. Ten is the whole of the first chapter, so the panel arrives on the map the
    /// player returns to when they have finished it, which is the first time the rule can stop
    /// them. Counted on the mode's own ladder (<c>WardSeats.ClearedIn</c>), never on a level's
    /// position (invariant 1).
    /// </para>
    /// <para>
    /// A player who already has ten or more is offered it exactly as before, so nobody who has
    /// played past the first chapter sees any change.
    /// </para>
    /// </summary>
    public static class ChapterGateLesson
    {
        public const int AfterCleared = 10;

        public static bool Due(int clearedOnLadder) => clearedOnLadder >= AfterCleared;
    }
}
