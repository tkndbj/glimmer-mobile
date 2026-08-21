using GlimmerGrove.Content;

namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// The level funnel, named once so every sink sees the same event shapes.
    ///
    /// These events are what difficulty tuning actually needs: how often a level
    /// is started, how often it is finished, in how many moves, how often a player
    /// walks away instead, and how often one is lost outright.
    /// Everything else can be added later; without these, a level that is quietly
    /// killing retention in one market is invisible.
    ///
    /// <see cref="Defeated"/> is the one that decides whether the difficulty is tuned
    /// correctly. A glade players lose repeatedly is not "hard" once hearts gate
    /// play — it is a wall they pay to hit, and it needs to be visible from day one
    /// rather than inferred from a drop in <see cref="Completed"/>.
    /// </summary>
    public static class LevelAnalytics
    {
        public const string Started = "level_started";
        public const string Completed = "level_completed";
        public const string Abandoned = "level_abandoned";
        public const string Defeated = "level_defeated";
        public const string HintUsed = "level_hint_used";

        public static void TrackStarted(LevelDefinition level, int attempt)
        {
            if (level == null) return;
            Telemetry.Track(Started,
                "level_id", level.Id.Value,
                "chapter_id", level.Chapter.Value,
                "attempt", attempt,
                "par", level.Tuning.Par);
        }

        public static void TrackCompleted(LevelDefinition level, int moves, int stars,
                                          int hintsUsed, float seconds, bool firstClear)
        {
            if (level == null) return;
            Telemetry.Track(Completed,
                "level_id", level.Id.Value,
                "chapter_id", level.Chapter.Value,
                "moves", moves,
                "stars", stars,
                "par", level.Tuning.Par,
                "hints_used", hintsUsed,
                "seconds", Round(seconds),
                "first_clear", firstClear);
        }

        public static void TrackAbandoned(LevelDefinition level, int moves, float seconds, string reason)
        {
            if (level == null) return;
            Telemetry.Track(Abandoned,
                "level_id", level.Id.Value,
                "chapter_id", level.Chapter.Value,
                "moves", moves,
                "seconds", Round(seconds),
                "reason", reason);
        }

        /// <summary>
        /// A lost run. Carries the hearts left afterwards, because the
        /// question that matters is not just how often players lose but how often
        /// losing is what stops them playing.
        /// </summary>
        public static void TrackDefeated(LevelDefinition level, int moves, float seconds,
                                         int heartsLeft, string reason)
        {
            if (level == null) return;
            Telemetry.Track(Defeated,
                "reason", reason,
                "level_id", level.Id.Value,
                "chapter_id", level.Chapter.Value,
                "moves", moves,
                "seconds", Round(seconds),
                "hearts_left", heartsLeft);
        }

        /// <summary>
        /// A hint was spent on this glade.
        ///
        /// <paramref name="hintsRemaining"/> is what the <em>account</em> holds afterwards,
        /// not what is left on this board — the per-glade allowance is gone, and the pool
        /// refills on a clock and is spent across every glade. Worth knowing before reading
        /// a chart: a zero here means the player is now waiting, which is a fact about their
        /// session rather than about this level.
        /// </summary>
        public static void TrackHintUsed(LevelDefinition level, int hintsRemaining, int moves)
        {
            if (level == null) return;
            Telemetry.Track(HintUsed,
                "level_id", level.Id.Value,
                "chapter_id", level.Chapter.Value,
                "hints_remaining", hintsRemaining,
                "moves", moves);
        }

        static float Round(float seconds) => UnityEngine.Mathf.Round(seconds * 10f) / 10f;
    }
}
