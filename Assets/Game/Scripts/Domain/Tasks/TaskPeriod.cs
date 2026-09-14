using System;

namespace GlimmerGrove.Tasks
{
    /// <summary>
    /// How often a task's slate is dealt again.
    ///
    /// Two, and only two, on purpose. A daily loop is the thing that brings somebody back
    /// tonight; a weekly one is the thing that gives a week a shape. A third cadence — a
    /// monthly, a seasonal — would be a third slate on one screen, and every screen in this
    /// game that grew a third responsibility was one too many (<c>CRAFT.md</c>). A season is
    /// an <em>event</em>, and events already exist.
    /// </summary>
    public enum TaskPeriod
    {
        Daily = 0,
        Weekly = 1,
    }

    /// <summary>
    /// The permanent ids a content file and a claim use for a period.
    ///
    /// Strings on the wire, for the reason every other id here is a string: a claim id
    /// (<c>task:daily:20700:d_play:credits</c>) is what the server keys a grant on, and an
    /// enum's numbering is an implementation detail that must not be able to leak into a
    /// database key.
    /// </summary>
    public static class TaskPeriods
    {
        public const string Daily = "daily";
        public const string Weekly = "weekly";

        public static readonly TaskPeriod[] All = { TaskPeriod.Daily, TaskPeriod.Weekly };

        public static string Id(TaskPeriod period)
            => period == TaskPeriod.Weekly ? Weekly : Daily;

        public static bool TryParse(string id, out TaskPeriod period)
        {
            if (string.Equals(id, Daily, StringComparison.Ordinal)) { period = TaskPeriod.Daily; return true; }
            if (string.Equals(id, Weekly, StringComparison.Ordinal)) { period = TaskPeriod.Weekly; return true; }
            period = TaskPeriod.Daily;
            return false;
        }
    }
}
