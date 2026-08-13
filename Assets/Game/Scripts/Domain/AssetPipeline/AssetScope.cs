namespace GlimmerGrove.AssetPipeline
{
    /// <summary>
    /// How long a loaded asset is expected to stay in memory.
    ///
    /// The whole point of having two scopes is that memory use must stop scaling
    /// with the size of the catalog. UI chrome is small and needed everywhere, so it
    /// is loaded once and kept. Chapter art is large and needed briefly, so it is
    /// loaded on entry and dropped on exit — which is what keeps a fiftieth chapter
    /// costing the same at runtime as the first.
    /// </summary>
    public enum AssetScope
    {
        /// <summary>Buttons, icons, fonts, shared sounds. Held for the whole session.</summary>
        Global = 0,

        /// <summary>Backdrops and map strips owned by one chapter. Dropped on leaving it.</summary>
        Chapter = 1,
    }
}
