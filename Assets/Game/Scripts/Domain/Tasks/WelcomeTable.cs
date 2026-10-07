using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Wards;

namespace GlimmerGrove.Tasks
{
    /// <summary>
    /// One welcome quest: a turret, the verb that earns it and how many distinct days of it.
    ///
    /// <para>
    /// <b>A quest is a counted verb and a number of days, and the prize is an entitlement.</b>
    /// The verb is one of <see cref="TaskGoal"/>'s - the same registry the slates, the lifetime
    /// tally and the ranks read, so a quest costs no new hook - and the number is days rather
    /// than a count, because the thing being bought is a return visit: a player who fells forty
    /// raiders in one sitting has done something, but not the thing a welcome bonus is for.
    /// The days need not be consecutive (the owner, 2026-10-07): a streak already punishes a
    /// missed day, and a second thing that did would be two reasons to give up on one bad week.
    /// </para>
    /// </summary>
    public sealed class WelcomeQuest
    {
        public string Id { get; }
        public WardModel Ward { get; }
        public TaskGoal Goal { get; }
        public int Days { get; }

        /// <summary>Its row on the page, nought first. Decides the colour its turret is drawn in.</summary>
        public int Ordinal { get; }

        /// <summary>
        /// The seat colour the turret is shown in (0..3), one per row so four quests show four
        /// colours. A picture only: the turret is granted on every seat (<see cref="WelcomeLedger"/>).
        /// </summary>
        public int Colour => Ordinal % WardLine.Colours.Length;

        /// <summary>The loc key of the sentence the row says, with the day count as <c>{0}</c>.</summary>
        public string SentenceKey => WelcomeGoals.SentenceKey(Goal);

        /// <summary>
        /// The other thing a finished quest may be taken as: the turret's own shelf price, in the
        /// currency the shelf sells it for (the owner, 2026-10-07: a player who already holds the
        /// turret takes its worth instead). <b>Derived from the roster, never authored</b>, so a
        /// retune of the shelf retunes this, and the server reads the same roster through the
        /// seeder (<c>welcome.ts</c>).
        /// </summary>
        public string PriceCurrency => Ward != null && Ward.ForGems ? Currency.Gems : Currency.Credits;

        /// <summary>The amount of <see cref="PriceCurrency"/> the quest pays instead of the turret.</summary>
        public long PriceAmount => Ward == null ? 0L : Ward.ForGems ? Ward.GemPrice : Ward.CoinPrice;

        public WelcomeQuest(string id, WardModel ward, TaskGoal goal, int days, int ordinal)
        {
            Id = id;
            Ward = ward;
            Goal = goal;
            Days = days;
            Ordinal = ordinal;
        }
    }

    /// <summary>
    /// The sentence each verb says on a welcome row.
    ///
    /// <b>Literal keys in a switch, never built from the goal's id</b> (invariant 6: a key built
    /// by concatenation is a key <c>loc.py</c> cannot see), and the registry of which verbs may
    /// be a welcome quest at all: a goal with no sentence here is refused by the table, because
    /// a row that cannot say what it asks for is a row nobody can finish on purpose.
    /// </summary>
    public static class WelcomeGoals
    {
        public static string SentenceKey(TaskGoal goal)
        {
            switch (goal)
            {
                case TaskGoal.Runs: return "ui.welcome.goal.runs";
                case TaskGoal.Wins: return "ui.welcome.goal.wins";
                case TaskGoal.TaskClaims: return "ui.welcome.goal.task_claims";
                case TaskGoal.ChallengePlays: return "ui.welcome.goal.challenge_plays";
                case TaskGoal.ChallengeWins: return "ui.welcome.goal.challenge_wins";
                case TaskGoal.Streak: return "ui.welcome.goal.streak";
                default: return string.Empty;
            }
        }

        /// <summary>Every verb that has a sentence, for the gates and the tests.</summary>
        public static readonly TaskGoal[] All =
        {
            TaskGoal.Runs, TaskGoal.Wins, TaskGoal.TaskClaims,
            TaskGoal.ChallengePlays, TaskGoal.ChallengeWins, TaskGoal.Streak,
        };
    }

    /// <summary>
    /// The welcome bonus: a short ladder of turrets handed to an account for coming back on
    /// several different days.
    ///
    /// <para>
    /// <b>Content, not code</b> (invariant 4): which turrets, which verbs and how many days are
    /// rows of the <c>welcome</c> block in <c>progression.json</c>, so a retune is a content push.
    /// What is code is the verb registry (<see cref="WelcomeGoals"/>) and the counting
    /// (<see cref="WelcomeLedger"/>).
    /// </para>
    /// <para>
    /// <b>Absent switches the feature off</b> (<see cref="Empty"/>): the hub draws no door, the
    /// ledger records nothing and nothing already granted is touched - a turret handed over is
    /// an owned row in <c>wardsOwned</c> and the roster neither knows nor cares why. That makes
    /// the block's removal the emergency stop, and it is why there is no built-in table.
    /// </para>
    /// <para>
    /// <b>The ladder climbs</b>: a later quest never asks for fewer days than the one before it,
    /// so the page reads as a staircase and the first prize is the soonest. A one-day quest is
    /// refused - it would pay on the visit the player is already making.
    /// </para>
    /// </summary>
    public sealed class WelcomeTable
    {
        /// <summary>
        /// The most quests a block may hold. <b>The page's capacity, not a sanity bound</b>:
        /// <c>WelcomeScreen</c> draws every row under its header with no scroll, and five rows is
        /// what the squarest canvas holds. The save's list bounds follow it (<c>WelcomeLedger.MaxRows</c>).
        /// </summary>
        public const int MaxQuests = 5;

        /// <summary>
        /// The most days a quest may ask for, and the most days the save records per verb
        /// (<see cref="WelcomeLedger.MaxDays"/>). Low enough that a forged list stays small.
        /// </summary>
        public const int MaxDays = 32;

        /// <summary>The fewest days a quest may ask for. One would pay on the first visit.</summary>
        public const int LeastDays = 2;

        public static readonly WelcomeTable Empty = new WelcomeTable(Array.Empty<WelcomeQuest>());

        readonly WelcomeQuest[] _quests;

        WelcomeTable(WelcomeQuest[] quests) { _quests = quests; }

        public IReadOnlyList<WelcomeQuest> Quests => _quests;

        public bool IsEmpty => _quests.Length == 0;

        public WelcomeQuest Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _quests.Length; i++)
                if (string.Equals(_quests[i].Id, id, StringComparison.Ordinal)) return _quests[i];
            return null;
        }

        /// <summary>Whether any quest counts this verb - the one question the ledger's hook asks.</summary>
        public bool Counts(TaskGoal goal)
        {
            for (int i = 0; i < _quests.Length; i++)
                if (_quests[i].Goal == goal) return true;
            return false;
        }

        /// <summary>Whether an id may be a quest id: short, lower-case, and able to survive a claim list.</summary>
        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 32) return false;
            if (id[0] < 'a' || id[0] > 'z') return false;
            for (int i = 1; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>
        /// Reads the optional <c>welcome</c> block against the turret roster. Never throws and
        /// never returns null: anything wrong is named in <paramref name="problems"/> and the
        /// table resolves to <see cref="Empty"/>, because a content mistake must fail a build and
        /// never a session - and a quest that cannot pay must not be shown.
        /// </summary>
        public static WelcomeTable Resolve(WelcomeDto dto, WardCatalog wards, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null || !dto.IsAuthored) return Empty;             // absent is the feature off

            var rows = dto.quests;
            if (rows.Length > MaxQuests)
            {
                problems.Add($"welcome lists {rows.Length} quests; at most {MaxQuests} are supported");
                return Empty;
            }

            wards ??= WardCatalog.Default;

            var quests = new WelcomeQuest[rows.Length];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var wardIds = new HashSet<string>(StringComparer.Ordinal);
            bool ok = true;
            int lastDays = 0;

            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                string where = $"welcome quest {i}";

                if (row == null) { problems.Add($"{where} is empty"); ok = false; continue; }

                if (!IsValidId(row.id))
                {
                    problems.Add($"{where} has a bad id '{row.id}' (a-z, 0-9, _; at most 32)");
                    ok = false;
                }
                else if (!ids.Add(row.id))
                {
                    problems.Add($"{where} repeats id '{row.id}'");
                    ok = false;
                }

                var ward = wards.Find(row.ward);
                if (ward == null)
                {
                    problems.Add($"{where} pays turret '{row.ward}', which the roster does not hold");
                    ok = false;
                }
                else if (ward.IsStarter)
                {
                    problems.Add($"{where} pays turret '{row.ward}', which is the free starter and already held");
                    ok = false;
                }
                else if (!wardIds.Add(ward.Id))
                {
                    problems.Add($"{where} pays turret '{row.ward}' a second time");
                    ok = false;
                }

                var goal = TaskGoals.Parse(row.goal);
                if (goal == TaskGoal.None)
                {
                    problems.Add($"{where} counts '{row.goal}', which is not a verb this build counts");
                    ok = false;
                }
                else if (string.IsNullOrEmpty(WelcomeGoals.SentenceKey(goal)))
                {
                    problems.Add($"{where} counts '{row.goal}', which has no sentence (WelcomeGoals)");
                    ok = false;
                }

                if (row.days < LeastDays || row.days > MaxDays)
                {
                    problems.Add($"{where} asks for {row.days} days, outside {LeastDays}..{MaxDays}");
                    ok = false;
                }
                else if (row.days < lastDays)
                {
                    problems.Add($"{where} asks for {row.days} days after {lastDays}; the ladder must climb");
                    ok = false;
                }

                lastDays = row.days;
                quests[i] = new WelcomeQuest(row.id, ward, goal, row.days, i);
            }

            return ok ? new WelcomeTable(quests) : Empty;
        }
    }
}
