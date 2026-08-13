namespace GlimmerGrove.Content
{
    /// <summary>
    /// Where content lives, expressed as source-relative paths.
    ///
    /// Every source — the bundled files, the on-device cache, the CDN — uses the
    /// same relative layout, so a path means the same thing wherever it is resolved.
    /// That symmetry is what makes remote delivery a swap of one object rather than
    /// a rewrite of the loader.
    /// </summary>
    public static class ContentPaths
    {
        public const string Root = "Content";

        /// <summary>Index of every chapter the build or the server knows about.</summary>
        public const string Manifest = Root + "/manifest.json";

        /// <summary>
        /// The XP curve and reward table. Content rather than code so rewards can be
        /// retuned without a store review — the same argument that put levels here.
        /// </summary>
        public const string Progression = Root + "/progression.json";

        public static string Chapter(ChapterId id) => $"{Root}/chapters/{id.Value}.json";

        public static string Localisation(string languageCode) => $"{Root}/loc/{languageCode}.json";
    }
}
