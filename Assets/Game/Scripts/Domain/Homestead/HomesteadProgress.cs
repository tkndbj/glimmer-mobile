using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// The two questions the grove asks about what the player has done.
    ///
    /// <para>
    /// An interface rather than direct calls to <c>PlayerProgress</c>, for
    /// <see cref="IGameClock"/>'s reason: the earned half of every unlock rule here is a
    /// question about the star ledger, and a rule worth having is a rule worth pinning
    /// offline. Nothing in <see cref="HomesteadLedger"/> or <see cref="HomesteadCatalog"/>
    /// then needs a save file to be tested, which is what keeps the shipped ladder — thirty
    /// pieces and every plot — checkable in the EditMode suite rather than by hand.
    /// </para>
    /// </summary>
    public interface IHomesteadProgress
    {
        /// <summary>Whether this glade has ever been cleared.</summary>
        bool IsCleared(LevelId level);

        /// <summary>
        /// Whether every glade in this chapter has been cleared.
        ///
        /// "Every glade cleared" rather than "reached" or "three-starred" because it is the
        /// only reading a player can check by looking at the map, and the plot ladder is the
        /// most visible reward in the game — a rule somebody can be wrong about is a support
        /// ticket per drop.
        /// </summary>
        bool IsChapterFinished(ChapterId chapter);
    }

    /// <summary>The real answer, read from the player's records against the live catalog.</summary>
    public sealed class LivePlayerProgress : IHomesteadProgress
    {
        public bool IsCleared(LevelId level) => level.IsValid && PlayerProgress.IsCleared(level);

        public bool IsChapterFinished(ChapterId chapter)
        {
            if (!chapter.IsValid) return false;

            var levels = GameContent.Index.LevelsOf(chapter);

            // An unknown chapter has no levels, and "all zero of them are cleared" is
            // vacuously true — which would hand out a plot for a chapter that does not
            // exist. A chapter nobody can play is not a chapter anybody finished.
            if (levels.Count == 0) return false;

            for (int i = 0; i < levels.Count; i++)
                if (!PlayerProgress.IsCleared(levels[i])) return false;

            return true;
        }
    }

    /// <summary>
    /// Where the grove reads progress from. Swapped by tests, and by nothing else.
    /// </summary>
    public static class HomesteadProgress
    {
        static IHomesteadProgress _source = new LivePlayerProgress();

        public static IHomesteadProgress Source => _source;

        public static void Set(IHomesteadProgress source)
            => _source = source ?? new LivePlayerProgress();

        public static bool IsCleared(LevelId level) => _source.IsCleared(level);

        public static bool IsChapterFinished(ChapterId chapter) => _source.IsChapterFinished(chapter);
    }
}
