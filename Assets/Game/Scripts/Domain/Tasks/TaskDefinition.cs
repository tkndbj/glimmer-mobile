using System;
using GlimmerGrove.Daily;

namespace GlimmerGrove.Tasks
{
    /// <summary>
    /// One rung of the chest ladder: a permanent id, its place on the ladder, and what
    /// opening one pays.
    ///
    /// <para>
    /// <b>Every reward in the task system is a chest, and a chest is one of these.</b> A
    /// task names a tier rather than a prize, so retuning what a tier pays retunes every
    /// task that pays it at once, and the odds a player is shown for a wooden chest are the
    /// odds for every wooden chest — one disclosure per tier rather than one per task.
    /// </para>
    /// <para>
    /// <b>The id is permanent</b> (invariant 1 applied to art and copy): the tier's picture
    /// (<see cref="Icon"/>, <see cref="Reel"/>) and its name (<see cref="NameKey"/>) are
    /// derived from it, so anything holding a tier id can draw and name the chest without
    /// reading the table. Rank is its position on the ladder, authored by order, and is
    /// what the gates check rises with the floor — a dearer chest that pays less reads as
    /// the game punishing the player for the harder task.
    /// </para>
    /// </summary>
    public sealed class ChestTier
    {
        public ChestTier(string id, int rank, ChestDefinition chest)
        {
            Id = id ?? string.Empty;
            Rank = rank < 1 ? 1 : rank;
            Chest = chest ?? new ChestDefinition(null, null);
        }

        public string Id { get; }

        /// <summary>One for the humblest chest, counting up. Position on the ladder.</summary>
        public int Rank { get; }

        public ChestDefinition Chest { get; }

        /// <summary>Derived from the id and never authored, for <c>UtilityItem.NameKey</c>'s reason.</summary>
        public string NameKey => "chest." + Id + ".name";

        /// <summary>The closed chest, small and resident: <c>Ui/Chest/{id}</c>.</summary>
        public string Icon => "Ui/Chest/" + Id;

        /// <summary>The opening reel, scoped to the screens that open one: <c>Chests/{id}</c>.</summary>
        public string Reel => "Chests/" + Id;

        public override string ToString() => Id + " #" + Rank;
    }

    /// <summary>
    /// One task: a goal, how much of it, which chest it pays, and the slate it belongs to.
    ///
    /// <para>
    /// A task authors no reward of its own — it names a <see cref="ChestTier"/> — and no
    /// copy of its own: its title is <see cref="NameKey"/>, derived from the id, and takes
    /// <see cref="Target"/> as its one argument, so a target retuned in content changes the
    /// sentence without touching a translation.
    /// </para>
    /// <para>
    /// <b>An id is permanent and a retired task is kept.</b> A claim for a task travels in the
    /// save and reaches the server as <c>task:{period}:{key}:{id}:{currency}</c>; a task
    /// deleted from the table would leave that claim naming a chest nobody can price, which
    /// is the unconfirmed-for-ever loop invariant 13a forbids. So a task that should stop
    /// being dealt is marked <see cref="Retired"/> — it leaves the rotation and keeps its
    /// tier — and the spent id joins the table in <c>CLAUDE.md</c>.
    /// </para>
    /// </summary>
    public sealed class TaskDefinition
    {
        public TaskDefinition(string id, TaskPeriod period, TaskGoal goal, int target,
                              ChestTier tier, bool retired = false)
        {
            Id = id ?? string.Empty;
            Period = period;
            Goal = goal;
            Target = target < 1 ? 1 : target;
            Tier = tier;
            Retired = retired;
        }

        public string Id { get; }

        public TaskPeriod Period { get; }

        public TaskGoal Goal { get; }

        /// <summary>How many of the goal finishes the task. Always at least one.</summary>
        public int Target { get; }

        public ChestTier Tier { get; }

        /// <summary>Out of the rotation, still priced. See the class remarks.</summary>
        public bool Retired { get; }

        /// <summary>
        /// The title, taking <see cref="Target"/> as <c>{0}</c>. Derived, never authored,
        /// so <c>content.py</c> can prove every shipped task resolves one.
        /// </summary>
        public string NameKey => "task." + Id + ".name";

        /// <summary>
        /// The title for a target of exactly one, where a language cannot say "1 battles".
        /// Optional: a task whose target can never be one authors none, and one that can
        /// authors the singular sentence here.
        /// </summary>
        public string NameOneKey => "task." + Id + ".name_one";

        /// <summary>The title as the player reads it, with the target in it.</summary>
        public string Title
            => Target == 1 && Localization.Loc.Has(NameOneKey)
             ? Localization.Loc.Get(NameOneKey)
             : Localization.Loc.Format(NameKey, Target);

        public override string ToString()
            => $"{Id} ({TaskGoals.Id(Goal)} x{Target} -> {Tier?.Id ?? "?"})";

        /// <summary>
        /// What a legal task id looks like: lower-case letters, digits and underscores, so
        /// it survives a claim id (colon-separated), a Firestore document id and a loc key.
        /// </summary>
        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength) return false;

            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }

            return true;
        }

        /// <summary>
        /// Bounded so a claim id stays inside the server's 64-character limit with the
        /// period, a five-figure key and a currency around it.
        /// </summary>
        public const int MaxIdLength = 32;
    }
}
