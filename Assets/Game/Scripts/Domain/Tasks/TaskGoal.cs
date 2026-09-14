using System;

namespace GlimmerGrove.Tasks
{
    /// <summary>
    /// The things a task can ask a player to do, each one a count the game already keeps.
    ///
    /// <para>
    /// <b>A goal is a verb the game can count, and a task is content that names one with a
    /// number.</b> That split is the whole design: a goal is code, because counting it means
    /// a hook at the moment it happens, and a task is a row in <c>progression.json</c>,
    /// because "fell forty raiders" and "fell sixty" are the same hook with a different
    /// target — so a live-ops retune, a new task, or a whole new slate is a content push
    /// and never a build (invariant 4). What <em>is</em> a build is a new verb.
    /// </para>
    /// <para>
    /// <b>The ordinals here never reach a file.</b> Progress is stored under
    /// <see cref="TaskGoals"/>' string ids, so this enum can be reordered freely and a goal
    /// retired from it leaves a row the reader skips rather than a row that now means
    /// something else. A goal is nonetheless never deleted while any shipped task names it,
    /// because a task the save holds a claim for has to keep resolving (invariant 1).
    /// </para>
    /// <para>
    /// Every goal is a <em>monotone</em> count within a period — a thing that happened, never
    /// a level held — which is what lets the counters merge by <c>max</c> across devices
    /// (invariant 11b) without a single special case.
    /// </para>
    /// </summary>
    public enum TaskGoal
    {
        None = 0,

        /// <summary>Runs finished, won or lost. Counted where the run resolves.</summary>
        Runs,

        /// <summary>Runs cleared.</summary>
        Wins,

        /// <summary>Stars earned, summed over cleared runs.</summary>
        Stars,

        /// <summary>Runs cleared at three stars.</summary>
        ThreeStars,

        /// <summary>Matches made on the gem field, summed over runs.</summary>
        Matches,

        /// <summary>Raiders felled on the hill.</summary>
        Raiders,

        /// <summary>Bosses felled.</summary>
        Bosses,

        /// <summary>Charms set off.</summary>
        Charms,

        /// <summary>Cogs picked up.</summary>
        Cogs,

        /// <summary>Bombs tapped.</summary>
        Bombs,

        /// <summary>Utilities used from the action bar.</summary>
        Utilities,

        /// <summary>Waves seen off, summed over runs.</summary>
        Waves,

        /// <summary>Streak nights collected on the streak page.</summary>
        Streak,
    }

    /// <summary>
    /// The permanent ids a content file and a save use for goals. Never renamed, never
    /// reused: a save carries progress under these strings, and a task in a published
    /// table names one.
    /// </summary>
    public static class TaskGoals
    {
        public const string Runs = "runs";
        public const string Wins = "wins";
        public const string Stars = "stars";
        public const string ThreeStars = "three_stars";
        public const string Matches = "matches";
        public const string Raiders = "raiders";
        public const string Bosses = "bosses";
        public const string Charms = "charms";
        public const string Cogs = "cogs";
        public const string Bombs = "bombs";
        public const string Utilities = "utilities";
        public const string Waves = "waves";
        public const string Streak = "streak";

        /// <summary>Every goal that can be counted, in enum order. For the gates and the tests.</summary>
        public static readonly TaskGoal[] All =
        {
            TaskGoal.Runs, TaskGoal.Wins, TaskGoal.Stars, TaskGoal.ThreeStars, TaskGoal.Matches,
            TaskGoal.Raiders, TaskGoal.Bosses, TaskGoal.Charms, TaskGoal.Cogs, TaskGoal.Bombs,
            TaskGoal.Utilities, TaskGoal.Waves, TaskGoal.Streak,
        };

        public static TaskGoal Parse(string id)
        {
            switch (id)
            {
                case Runs: return TaskGoal.Runs;
                case Wins: return TaskGoal.Wins;
                case Stars: return TaskGoal.Stars;
                case ThreeStars: return TaskGoal.ThreeStars;
                case Matches: return TaskGoal.Matches;
                case Raiders: return TaskGoal.Raiders;
                case Bosses: return TaskGoal.Bosses;
                case Charms: return TaskGoal.Charms;
                case Cogs: return TaskGoal.Cogs;
                case Bombs: return TaskGoal.Bombs;
                case Utilities: return TaskGoal.Utilities;
                case Waves: return TaskGoal.Waves;
                case Streak: return TaskGoal.Streak;
                default: return TaskGoal.None;
            }
        }

        public static string Id(TaskGoal goal)
        {
            switch (goal)
            {
                case TaskGoal.Runs: return Runs;
                case TaskGoal.Wins: return Wins;
                case TaskGoal.Stars: return Stars;
                case TaskGoal.ThreeStars: return ThreeStars;
                case TaskGoal.Matches: return Matches;
                case TaskGoal.Raiders: return Raiders;
                case TaskGoal.Bosses: return Bosses;
                case TaskGoal.Charms: return Charms;
                case TaskGoal.Cogs: return Cogs;
                case TaskGoal.Bombs: return Bombs;
                case TaskGoal.Utilities: return Utilities;
                case TaskGoal.Waves: return Waves;
                case TaskGoal.Streak: return Streak;
                default: return string.Empty;
            }
        }

        /// <summary>
        /// The picture a goal is drawn with on a task row: an address under <c>Ui/</c>,
        /// written here so <c>artnames.py</c> and the reward-art fixture can hold every one of
        /// them to disk. A goal with no picture is a row with a white rectangle on it
        /// (invariant 7b), so there is no default.
        /// </summary>
        public static string Icon(TaskGoal goal)
        {
            switch (goal)
            {
                case TaskGoal.Runs: return "Ui/ic_battle";
                case TaskGoal.Wins: return "Ui/ic_trophy";
                case TaskGoal.Stars: return "Ui/ic_star3d";
                case TaskGoal.ThreeStars: return "Ui/ic_stars";
                case TaskGoal.Matches: return "Ui/ic_gem";
                case TaskGoal.Raiders: return "Ui/Task/raiders";
                case TaskGoal.Bosses: return "Ui/Task/boss";
                case TaskGoal.Charms: return "Ui/Task/charm";
                case TaskGoal.Cogs: return "Ui/Task/cog";
                case TaskGoal.Bombs: return "Ui/Utility/firepot";
                case TaskGoal.Utilities: return "Ui/Utility/surge";
                case TaskGoal.Waves: return "Ui/Task/wave";
                case TaskGoal.Streak: return "Ui/ic_streak";
                default: return string.Empty;
            }
        }
    }
}
