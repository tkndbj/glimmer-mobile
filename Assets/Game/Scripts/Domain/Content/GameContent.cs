using System;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// The catalog the game is currently playing, read synchronously from anywhere.
    ///
    /// Loading is asynchronous and happens once during the splash; everything after
    /// that reads this. Publishing swaps a whole immutable catalog in one assignment,
    /// so a screen can never observe a half-applied content update — it either sees
    /// the old world or the new one.
    /// </summary>
    public static class GameContent
    {
        static LevelCatalog _catalog = LevelCatalog.Empty;

        public static LevelCatalog Catalog => _catalog;

        public static bool IsLoaded { get; private set; }

        /// <summary>Raised on the main thread after a new catalog is published.</summary>
        public static event Action CatalogChanged;

        internal static void Publish(LevelCatalog catalog)
        {
            if (catalog == null) return;

            _catalog = catalog;
            IsLoaded = true;

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
        public static LevelDefinition Find(LevelId id) => _catalog.Find(id);

        public static bool TryFind(LevelId id, out LevelDefinition level) => _catalog.TryFind(id, out level);

        public static ChapterDefinition ChapterOf(LevelDefinition level) => _catalog.ChapterOf(level);

        public static int Count => _catalog.Count;
    }
}
