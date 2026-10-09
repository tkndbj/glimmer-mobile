using System.Text;
using GlimmerGrove.Analytics;
using GlimmerGrove.Content;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// What a Shuffle run says about itself when it ends: how far it got, and the cards it held.
    ///
    /// <b>One event beside the level's own</b> (<c>LevelAnalytics.TrackCompleted</c> still
    /// fires, with the lane's own level id), because the question this lane raises is which
    /// cards carry a run and which never get taken - and that is a question about the build,
    /// which no other event knows. Card ids are permanent (<c>ShuffleCard.Id</c>), so a
    /// dashboard reads the same card for the life of the lane.
    /// </summary>
    public static class ShuffleAnalytics
    {
        public const string Ended = "shuffle_ended";

        public static void TrackRun(LevelDefinition level, int waves, ShuffleRun run)
        {
            if (level == null || run == null) return;

            var held = new StringBuilder(128);
            var taken = run.Build.Taken;

            for (int i = 0; i < taken.Count; i++)
            {
                if (i > 0) held.Append(',');
                held.Append(taken[i].Id);
            }

            Telemetry.Track(Ended,
                "level_id", level.Id.Value,
                "waves", waves,
                "hands", run.Hands,
                "cards", taken.Count,
                "build", held.ToString(),
                "seed", (long)run.Seed);
        }
    }
}
