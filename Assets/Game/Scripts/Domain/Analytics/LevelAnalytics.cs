using GlimmerGrove.Content;

namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// The level funnel, named once so every sink sees the same event shapes.
    ///
    /// These four events are what difficulty tuning actually needs: how often a level
    /// is started, how often it is finished, in how many moves, and how often a player
    /// walks away instead. Everything else can be added later; without these, a level
    /// that is quietly killing retention in one market is invisible.
    /// </summary>
    public static class LevelAnalytics
    {
        public const string Started = "level_started";
        public const string Completed = "level_completed";
        public const string Abandoned = "level_abandoned";
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
