namespace GlimmerGrove.Content
{
    /// <summary>
    /// The contract between this build and the content it reads.
    ///
    /// A shipped app lives for years in the wild. Some players never update, so the
    /// server will eventually be serving content newer than a client understands.
    /// The rule that makes that survivable: a client reads anything at or below its
    /// own version, ignores fields it does not recognise, and skips — never crashes
    /// on — anything above it.
    ///
    /// Bump <see cref="Version"/> only for a change old clients could not cope with.
    /// Adding an optional field is not such a change; removing or repurposing one is.
    /// </summary>
    public static class ContentSchema
    {
        /// <summary>
        /// v2 moved chapter membership and order into the manifest, so the boot path
        /// reads one small file instead of every chapter body. Raised rather than made
        /// optional because a v1 manifest lists no levels at all, and a client that
        /// tried to read one would see a game with no glades in it — a silent empty
        /// catalog is far worse than a clear refusal.
        ///
        /// It cost nothing to raise: remote delivery was still switched off and one
        /// chapter had shipped, so there was no content anywhere to migrate. The same
        /// change made after a CDN goes live is a migration under live players.
        /// </summary>
        public const int Version = 2;

        /// <summary>Oldest content this build can still read.</summary>
        public const int MinimumSupported = 2;

        public static bool CanRead(int schemaVersion)
            => schemaVersion >= MinimumSupported && schemaVersion <= Version;

        public static string Explain(int schemaVersion)
        {
            if (schemaVersion > Version)
                return $"needs schema v{schemaVersion}, this build reads up to v{Version} — update the app";
            if (schemaVersion < MinimumSupported)
                return $"uses retired schema v{schemaVersion}, this build needs at least v{MinimumSupported}";
            return null;
        }
    }
}
