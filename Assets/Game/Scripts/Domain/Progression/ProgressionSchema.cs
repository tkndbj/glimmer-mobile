namespace GlimmerGrove.Progression
{
    /// <summary>
    /// The contract for <c>progression.json</c>, versioned independently of the catalog.
    ///
    /// It would have been tidier to reuse <c>ContentSchema.Version</c> for everything in
    /// the content folder, and that is wrong for a reason worth writing down. The reward
    /// table is delivered on its own — the manifest carries a <c>progressionVersion</c>
    /// precisely so it can be refetched without touching a chapter — and it changes at a
    /// completely different rate from the catalog. Rewards get retuned; the manifest's
    /// shape almost never moves.
    ///
    /// Share one number between them and a change to the *catalog* format invalidates
    /// the *reward* file for every client that has not updated. Those players would
    /// silently fall back to the built-in curve, losing the live economy tuning, over a
    /// change that had nothing to do with the economy. Two formats, two readers, two
    /// versions.
    ///
    /// The rules are otherwise identical to <c>ContentSchema</c>: read anything at or
    /// below this version, skip — never crash on — anything above it, and treat a new
    /// optional field as non-breaking.
    /// </summary>
    public static class ProgressionSchema
    {
        public const int Version = 1;

        /// <summary>Oldest reward table this build can still read.</summary>
        public const int MinimumSupported = 1;

        public static bool CanRead(int schemaVersion)
            => schemaVersion >= MinimumSupported && schemaVersion <= Version;

        public static string Explain(int schemaVersion)
        {
            if (schemaVersion > Version)
                return $"needs progression schema v{schemaVersion}, this build reads up to v{Version} — update the app";
            if (schemaVersion < MinimumSupported)
                return $"uses retired progression schema v{schemaVersion}, this build needs at least v{MinimumSupported}";
            return null;
        }
    }
}
