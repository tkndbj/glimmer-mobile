using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// The rules a level is played by, whichever mode it belongs to.
    ///
    /// <para>
    /// <b>One field on <see cref="LevelDefinition"/> instead of one per mode</b>, and that is the
    /// whole point of it. A level used to carry a nullable <c>Layout</c>, a nullable
    /// <c>Hollow</c> and a nullable <c>Lab</c>, exactly one of which was set — so every reader
    /// had to know the full list, adding a mode meant editing every one of them, and "not a
    /// board, therefore a hollow" was correct right up until it silently was not. That reasoning
    /// crashed an Android build the day a third kind of level appeared.
    /// </para>
    /// <para>
    /// Implementations are immutable and hold only what their mode needs. Nothing here knows how
    /// a level is <em>drawn</em> — that is <c>ModeLook</c>, on the other side of the layering
    /// line, because Domain must never reference Presentation.
    /// </para>
    /// </summary>
    public interface ILevelRules
    {
        /// <summary>Which mode these rules belong to. Never <see cref="GameMode.None"/>.</summary>
        GameMode Mode { get; }
    }

    /// <summary>
    /// Everything the content pipeline needs to know about one way of playing: how to read a
    /// level of it, and how to tell a good one from a broken one.
    ///
    /// <para>
    /// <b>Adding a mode is a new subclass and one line in <see cref="LevelModes"/>.</b> No
    /// switch anywhere gains a case, no shared file gains a field, and nothing else in the game
    /// has to learn the mode exists — the mapper, the validator and the catalog all ask the
    /// registry rather than enumerating what they know about.
    /// </para>
    /// </summary>
    public abstract class LevelMode
    {
        /// <summary>The permanent id this mode is known by in content, analytics and loc keys.</summary>
        public abstract GameMode Mode { get; }

        /// <summary>
        /// Reads this mode's block out of a level, or reports why it cannot.
        ///
        /// Returning false with nothing added to <paramref name="problems"/> means "this level is
        /// not mine" — the mapper then tries the next mode. Returning false <em>with</em> a
        /// problem means "this is mine and it is broken", which is refused rather than passed on,
        /// because a half-read level is not a degraded level, it is a different one nobody
        /// authored.
        /// </summary>
        public abstract bool TryRead(LevelDto dto, LevelId id, ICollection<string> problems,
                                     out ILevelRules rules);

        /// <summary>
        /// Whether this level's block was authored at all. Asked before <see cref="TryRead"/> so
        /// a malformed block still reaches its own reader and is told what is wrong with it,
        /// rather than falling through to another mode and being reported as something else.
        ///
        /// <b>Never test the DTO's block for null</b> — <c>JsonUtility</c> instantiates a
        /// <c>[Serializable]</c> class field on every level in the game, so absence has to be a
        /// value a real block cannot hold.
        /// </summary>
        public abstract bool Claims(LevelDto dto);

        /// <summary>
        /// The tuning a level of this mode gets. Most modes derive it; a glade derives par from
        /// its board and a score attack has no par at all.
        /// </summary>
        public abstract LevelTuning Tune(LevelDto dto, ILevelRules rules);

        /// <summary>
        /// Proves a level of this mode is worth shipping, adding to <paramref name="issues"/>.
        /// Base does nothing, because a mode with no authored difficulty has nothing to prove.
        /// </summary>
        public virtual void Validate(LevelDefinition level, List<LevelIssue> issues) { }

        /// <summary>
        /// What the level's record is counted in — turns, sparks, tiles. Used for the map badge
        /// and the victory panel, which must word one run identically.
        ///
        /// A loc key stem rather than a word, so it translates and pluralises.
        /// </summary>
        public virtual string RecordStem => "ui.rank.record";
    }
}
