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
    /// dashboard reads the same card for the life of the lane. <b>The tuning's revision rides
    /// along</b> (MODES.md 59l; nought is the build's own figures), so a dashboard can tell a run
    /// played before a retune from one played after it, and so do the run's grade and score (59o).
    /// </summary>
    public static class ShuffleAnalytics
    {
        public const string Ended = "shuffle_ended";

        /// <summary>One refresh bought (MODES.md 59p): what it cost and how deep into the run.</summary>
        public const string Refreshed = "shuffle_refresh";

        /// <summary>
        /// A refresh was paid for: the price, which refresh of the run it was and at which hand,
        /// so the doubling can be read against how far players actually climb it.
        /// </summary>
        public static void TrackRefresh(LevelId level, ShuffleRun run, long gems)
        {
            if (run == null) return;

            Telemetry.Track(Refreshed,
                "level_id", level.Value,
                "gems", gems,
                "refresh", run.Refreshes,
                "hand", run.Hands,
                "tuning", run.Tuning.Revision);
        }

        /// <summary>A run was put away from the pause menu, picked up again, or could not be (MODES.md 59s).</summary>
        public const string Resumed = "shuffle_resumed";

        /// <summary>
        /// A suspended run was picked up: the wave it reopened on and the hands it carried, so a
        /// dashboard can read how often runs outlive the app and how deep they are when they do.
        /// </summary>
        public static void TrackResumed(LevelId level, ShuffleRun run, int wave)
        {
            if (run == null) return;

            Telemetry.Track(Resumed,
                "level_id", level.Value,
                "outcome", "resumed",
                "wave", wave,
                "hand", run.Hands,
                "tuning", run.Tuning.Revision);
        }

        /// <summary>
        /// A run was put away from the pause menu (SAVE &amp; QUIT): the wave it will reopen on
        /// and the hands it holds, so a dashboard can read how often players save rather than
        /// close the app, and whether they come back.
        /// </summary>
        public static void TrackSaved(LevelId level, ShuffleRun run, int wave)
        {
            if (run == null) return;

            Telemetry.Track(Resumed,
                "level_id", level.Value,
                "outcome", "saved",
                "wave", wave,
                "hand", run.Hands,
                "tuning", run.Tuning.Revision);
        }

        /// <summary>
        /// A suspended run could not be picked up and was let go, with why - a card withdrawn by
        /// an update, a tuning this build cannot read, or a damaged value. Should be rare; a
        /// spike after a release is a release that broke resuming.
        /// </summary>
        public static void TrackDropped(LevelId level, string fault)
        {
            string why = string.IsNullOrEmpty(fault) ? "unknown" : fault.Length <= 100 ? fault : fault.Substring(0, 100);

            Telemetry.Track(Resumed,
                "level_id", level.Value,
                "outcome", "dropped",
                "fault", why);
        }

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

            // The secrets and evolutions it unlocked (59n), by their permanent ids.
            var bonds = new StringBuilder(64);
            var unlocked = run.Build.Bonds;

            for (int i = 0; i < unlocked.Count; i++)
            {
                if (i > 0) bonds.Append(',');
                bonds.Append(unlocked[i].Id);
            }

            Telemetry.Track(Ended,
                "level_id", level.Id.Value,
                "waves", waves,
                "hands", run.Hands,
                "refreshes", run.Refreshes,
                "cards", taken.Count,
                "build", held.ToString(),
                "bonds", bonds.ToString(),
                "seed", (long)run.Seed,
                "tuning", run.Tuning.Revision,

                // The grade and score (MODES.md 59o), so the floors calibrated on bots
                // (`ShuffleGradeTests`) can be read against what players actually reach.
                "grade", ShuffleGrades.Id(ShuffleGrades.Of(run.Build)),
                "score", ShuffleGrades.Score(run.Build));
        }
    }
}
