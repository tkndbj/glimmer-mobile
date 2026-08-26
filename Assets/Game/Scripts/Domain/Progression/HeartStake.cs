using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// Which glades cost a heart, and which are free to fail.
    ///
    /// <para>
    /// <b>The rule.</b> The first <see cref="HeartRules.GraceLevels"/> glades of the first
    /// chapter of <em>each</em> mode cost nothing, however they end. Everything after them is
    /// unchanged: a loss, a forfeit and a run the process never finished all cost what they
    /// have always cost.
    /// </para>
    /// <para>
    /// <b>Why it exists.</b> The heart gate is the only thing in this game that can stop
    /// somebody playing, and the worst possible moment to meet it is while they are still
    /// working out what the verb is. A player who loses their first three boards to a rule they
    /// have not been taught yet is being charged for our teaching, and they have not yet
    /// decided they like the game enough to wait eight hours. Per mode rather than once per
    /// account, because a mode shipped a year from now is somebody's first board of that mode —
    /// Lightweave is dragged rather than tapped and is lost on ink rather than turns, so a
    /// player arriving at it is a beginner again in every sense that matters here.
    /// </para>
    /// <para>
    /// <b>It is asked, never stored.</b> Nothing about a free run reaches the save file, the
    /// wire or the server: a heart is simply not spent, and the star ledger — which is what
    /// every reward derives from — cannot tell the difference. That is deliberate and it is
    /// what makes the window free to retune from a config push at any time (invariant 14: a
    /// rule that can be expressed as a function of things already known should be).
    /// </para>
    /// <para>
    /// <b>On keying a rule to position.</b> Invariant 1 forbids keying <em>stored</em> things —
    /// save data, analytics, remote config — on a level's position, and nothing here is stored.
    /// This is the same shape as <see cref="LevelUnlock"/>'s chain, which has always asked
    /// <c>CatalogIndex.Previous</c>: a derived reading of the order the manifest publishes. A
    /// drop that inserts a glade at the head of chapter one moves the window onto it, which is
    /// the intended meaning — "the first boards a player meets" — and moves nothing anybody
    /// has already earned.
    /// </para>
    /// </summary>
    public static class HeartStake
    {
        /// <summary>
        /// Whether this glade costs a heart when it goes wrong.
        ///
        /// <para>
        /// The one question, asked by all four places that can take a heart for a run — the
        /// defeat (<c>RunLedger.Loss</c>), the abandonment (<c>RunScreen</c>), the door onto
        /// the board (<c>LevelsScreen</c>) and the marker a crash leaves behind
        /// (<c>RunGuard</c>, through the screen that decides whether to write one). A second
        /// copy of it would be a second place a free glade can quietly start charging.
        /// </para>
        /// <para>
        /// False for anything the catalog does not carry, which is the safe direction: an
        /// unknown level is not one of the first three of anything, so it is priced like every
        /// other glade rather than being handed out free by a typo.
        /// </para>
        /// </summary>
        public static bool IsFree(CatalogIndex index, LevelId level)
            => IsFree(index, level, HeartRules.Table);

        /// <summary>
        /// The same question of a table that is not (yet) the live one.
        ///
        /// <para>
        /// The overload exists for <c>ContentValidation</c>, which checks the table a build
        /// <em>would</em> publish rather than the one running — a different object, possibly
        /// different numbers. Without it the validator had to re-implement the rule to report
        /// on it, which is a copy of a rule about charging players kept in the one place whose
        /// job is to prove such copies do not exist.
        /// </para>
        /// </summary>
        public static bool IsFree(CatalogIndex index, LevelId level, HeartRuleTable hearts)
        {
            int grace = hearts?.GraceLevels ?? 0;
            if (grace <= 0 || index == null || !index.Contains(level)) return false;

            // Position within this level's own mode, which the index already keeps and which
            // starts at the mode's first chapter. Cheaper than walking a chapter's level list,
            // and it is the same reading the unlock chain takes.
            int order = index.OrderOf(level);
            if (order < 0 || order >= grace) return false;

            // The window stops at the end of the first chapter even when the published number
            // is longer than that chapter is. A chapter is where a mode's teaching is bounded,
            // and a window spilling into the second one would make "the first chapter is free"
            // untrue in the one place it is printed — see FreeLevelsIn.
            var first = index.FirstChapterIn(index.ModeOf(level));
            return first != null && index.ChapterOf(level) == first.Id;
        }

        /// <summary>
        /// How many of this chapter's glades are free to fail — nought for every chapter but
        /// the first of its mode.
        ///
        /// <para>
        /// What the information panel prints, and it is a count rather than a sentence for the
        /// reason every number on that panel is read from a table: a panel explaining the game
        /// is the first thing to rot when the game is retuned, and copy holding its own "three"
        /// would keep saying three the day the window is pushed to five.
        /// </para>
        /// </summary>
        public static int FreeLevelsIn(CatalogIndex index, ChapterId chapter)
            => FreeLevelsIn(index, chapter, HeartRules.Table);

        /// <summary>The same count, of a table that is not (yet) the live one.</summary>
        public static int FreeLevelsIn(CatalogIndex index, ChapterId chapter, HeartRuleTable hearts)
        {
            int grace = hearts?.GraceLevels ?? 0;
            if (grace <= 0 || index == null) return 0;

            var entry = index.FindChapter(chapter);
            if (entry == null) return 0;

            var first = index.FirstChapterIn(entry.Mode);
            if (first == null || first.Id != entry.Id) return 0;

            return grace < entry.LevelCount ? grace : entry.LevelCount;
        }
    }
}
