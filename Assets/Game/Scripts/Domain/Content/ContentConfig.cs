namespace GlimmerGrove.Content
{
    /// <summary>
    /// Where content comes from and whether remote delivery is switched on.
    ///
    /// Remote is off until a CDN actually exists, and the game is fully playable in
    /// that state — the seam is built now so that turning it on later is a one line
    /// change here rather than a refactor of the loader. Set <see cref="RemoteBaseUrl"/>
    /// to the folder that holds manifest.json and serve it over HTTPS.
    /// </summary>
    public static class ContentConfig
    {
        /// <summary>
        /// CDN root, e.g. "https://cdn.example.com/glimmer/v1". Empty disables remote.
        /// Version the path itself so a future breaking format change can be served
        /// alongside the old one instead of replacing it under live players.
        /// </summary>
        public static string RemoteBaseUrl = string.Empty;

        /// <summary>Pull newer content in the background after the game has started.</summary>
        public static bool RemoteRefreshEnabled = true;

        /// <summary>
        /// This build's number, compared against a chapter's minAppVersion so content
        /// needing newer client code is simply not shown to older installs.
        /// </summary>
        public static int AppVersion = 1;

        public static int NetworkTimeoutSeconds = 15;

        public static bool RemoteAvailable
            => RemoteRefreshEnabled && !string.IsNullOrEmpty(RemoteBaseUrl);
    }
}
