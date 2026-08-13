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
        public const int Version = 1;

        /// <summary>Oldest content this build can still read.</summary>
        public const int MinimumSupported = 1;

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
