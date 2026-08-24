using System;
using System.Threading;
using System.Threading.Tasks;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// The catalog the game is currently playing, read from anywhere.
    ///
    /// Publishing swaps a whole immutable catalog in one assignment, so a screen can
    /// never observe a half-applied content update — it either sees the old world or
    /// the new one.
    ///
    /// The conveniences here are all index questions, which are always answerable
    /// without touching a file. Anything needing a level's grid or art goes through
    /// <see cref="LevelAsync"/> and is therefore honest about the fact that it may have
    /// to read one — a screen that wants a board can await; a screen that wants a name
    /// or a position in the order never has to.
    /// </summary>
    public static class GameContent
    {
        static LevelCatalog _catalog = LevelCatalog.Empty;

        public static LevelCatalog Catalog => _catalog;

        public static CatalogIndex Index => _catalog.Index;

        public static bool IsLoaded { get; private set; }

        /// <summary>Raised on the main thread after a new catalog is published.</summary>
        public static event Action CatalogChanged;

        internal static void Publish(LevelCatalog catalog)
        {
            if (catalog == null) return;

            _catalog = catalog;
            IsLoaded = true;

            // The roster travels in the manifest, so it arrives with the index rather
            // than through a second fetch. Published before CatalogChanged so anything
            // redrawing on that event already sees the new companions.
            Progression.AvatarCatalog.Publish(catalog.Index.Companions);

            try
            {
                CatalogChanged?.Invoke();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        // -------------------------------------------------------- conveniences
        public static int Count => _catalog.Count;

        public static bool Contains(LevelId id) => _catalog.Contains(id);

        public static ChapterId ChapterOf(LevelId id) => _catalog.ChapterOf(id);

        /// <summary>
        /// How a glade is played, answered off the index so a screen can route to the right
        /// interaction without opening a chapter body first.
        /// </summary>
        public static GameMode ModeOf(LevelId id) => _catalog.Index.ModeOf(id);

        public static ChapterIndexEntry FindChapter(ChapterId id) => _catalog.FindChapter(id);

        /// <summary>The level's definition, reading its chapter body if need be.</summary>
        public static Task<LevelDefinition> LevelAsync(LevelId id, CancellationToken cancellation = default)
            => _catalog.LevelAsync(id, cancellation);

        public static Task<ChapterBody> ChapterAsync(ChapterId id, CancellationToken cancellation = default)
            => _catalog.ChapterAsync(id, cancellation);

        /// <summary>The definition if its chapter is already resident, which it is while playing it.</summary>
        public static bool TryResident(LevelId id, out LevelDefinition level)
            => _catalog.TryResidentLevel(id, out level);
    }
}
