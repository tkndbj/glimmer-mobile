using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Wards;

namespace GlimmerGrove.Tasks
{
    /// <summary>Which of a finished quest's two prizes the player took.</summary>
    public enum WelcomeReward
    {
        /// <summary>The turret, on every seat. An entitlement, granted here with no server.</summary>
        Turret,

        /// <summary>
        /// The turret's shelf price in its currency, for a player who already holds it (or
        /// wants the money). <b>Currency, so a claim</b> (invariant 10a): the amount reaches the
        /// balance as a pending grant under <c>welcome:{quest}:{currency}</c>, and the server
        /// re-prices it off the published roster (<c>welcome.ts</c>).
        /// </summary>
        Price,
    }

    /// <summary>What a welcome row draws.</summary>
    public enum WelcomeState
    {
        /// <summary>Counting days. The pips are what the player looks at.</summary>
        Open,

        /// <summary>Enough days. The turret is waiting to be taken.</summary>
        Ready,

        /// <summary>Taken. The turret is theirs on every seat.</summary>
        Claimed,
    }

    /// <summary>
    /// The welcome bonus: which days each counted verb happened on, which quests have been
    /// taken, and the one place a quest pays.
    ///
    /// <para>
    /// <b>The state is days per verb, never progress per quest.</b> Each verb a live quest
    /// names keeps the set of day keys it happened on, and a quest's progress is <em>derived</em>:
    /// the size of its verb's set, clamped to its target. That is <see cref="TaskLedger"/>'s
    /// argument one level up: per-quest progress is a second copy of the same fact, and the copy
    /// that breaks when the block is retuned - a quest added by a content push would start from
    /// nothing on a device that had already played those days. A set of days also merges by
    /// union without a special case (invariant 11b), where a <em>count</em> of days could not:
    /// two devices at three and two are equally consistent with "five different days" and
    /// "three, two of them the same".
    /// </para>
    /// <para>
    /// <b>The other stored thing is which quest ids were claimed</b>, a set joined by union:
    /// claiming cannot be undone, and a set of ids survives the block being retuned underneath
    /// it. Both ride inside the <c>tasks</c> map (<c>TaskStateDto.welcome</c>) for
    /// <see cref="LifetimeTally"/>'s reason: <c>hasOnly</c> is an allow-list over the document's
    /// top level, so a key inside a map already listed there costs no <c>firestore.rules</c>
    /// release and has no deploy ordering - the ruleset already deployed accepts it.
    /// </para>
    /// <para>
    /// <b>A quest pays one of two things, and the two are defended differently.</b> The turret
    /// is an entitlement granted here with no server: a client-held, union-joined set of
    /// permanent rows (<c>wardsOwned</c>, invariant 15) that buys no currency and nothing that
    /// reaches a public card, so a forged row buys a silhouette (<see cref="WardLedger"/>'s own
    /// argument, invariant 13's fourth clause). The turret's <em>price</em>, taken instead, is
    /// currency, so it is a claim (10a): a pending grant under a derived id that the server
    /// re-prices off the published roster and pays once per quest (<c>welcome.ts</c>). Which was
    /// taken is written beside the claim (<c>coined</c>) so the page can say so and the server
    /// can see the choice. The claim set makes either once per account, and the save carries
    /// the claim, the choice and the rows in one write.
    /// </para>
    /// <para>
    /// <b>Nothing here ends and nothing here is device-local.</b> Whether the hub shows the
    /// door is <see cref="Offered"/>: the block is live and a quest is still unclaimed. Once
    /// every quest is taken the door is gone on every device the account reaches, for ever,
    /// because the claim set is in the save and only grows (the owner, 2026-10-07).
    /// </para>
    /// </summary>
    public static class WelcomeLedger
    {
        /// <summary>The most days recorded per verb. <see cref="WelcomeTable.MaxDays"/>; pinned to the rules.</summary>
        public const int MaxDays = WelcomeTable.MaxDays;

        /// <summary>The most verb rows written, and the most claims. <see cref="WelcomeTable.MaxQuests"/>; pinned to the rules.</summary>
        public const int MaxRows = WelcomeTable.MaxQuests;

        static readonly Dictionary<TaskGoal, SortedSet<int>> _days = new Dictionary<TaskGoal, SortedSet<int>>();
        static readonly HashSet<string> _claimed = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The quests taken as their price rather than as the turret. A subset of the claims.</summary>
        static readonly HashSet<string> _coined = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Raised when a day or a claim lands, and on every load. Screens follow this.</summary>
        public static event Action Changed;

        /// <summary>Raised when a verb's days first reach a quest's target, carrying the quest.</summary>
        public static event Action<WelcomeQuest> Completed;

        static WelcomeTable Table => ProgressionRules.Table.Welcome;

        // ------------------------------------------------------------- reading
        /// <summary>Whether the block is published at all.</summary>
        public static bool Live => !Table.IsEmpty;

        /// <summary>
        /// Whether every live quest has been taken. True for an empty table too, so the two
        /// reasons the door is absent read as one answer.
        /// </summary>
        public static bool IsDone
        {
            get
            {
                var quests = Table.Quests;
                for (int i = 0; i < quests.Count; i++)
                    if (!_claimed.Contains(quests[i].Id)) return false;
                return true;
            }
        }

        /// <summary>Whether the hub should show the door: something is live and still unclaimed.</summary>
        public static bool Offered => Live && !IsDone;

        /// <summary>How many different days this verb has happened on. Unclamped.</summary>
        public static int DaysCounted(TaskGoal goal)
            => _days.TryGetValue(goal, out var set) ? set.Count : 0;

        /// <summary>A quest's progress in days, clamped to its target.</summary>
        public static int DaysDone(WelcomeQuest quest)
        {
            if (quest == null) return 0;
            int n = DaysCounted(quest.Goal);
            return n > quest.Days ? quest.Days : n;
        }

        public static bool IsClaimed(WelcomeQuest quest)
            => quest != null && _claimed.Contains(quest.Id);

        /// <summary>Whether a taken quest was taken as its price. False for one taken as the turret.</summary>
        public static bool TookPrice(WelcomeQuest quest)
            => quest != null && _coined.Contains(quest.Id);

        public static WelcomeState StateOf(WelcomeQuest quest)
        {
            if (quest == null) return WelcomeState.Open;
            if (IsClaimed(quest)) return WelcomeState.Claimed;
            return DaysDone(quest) >= quest.Days ? WelcomeState.Ready : WelcomeState.Open;
        }

        /// <summary>Quests finished and not yet taken. The door's badge.</summary>
        public static int ReadyCount
        {
            get
            {
                int ready = 0;
                var quests = Table.Quests;
                for (int i = 0; i < quests.Count; i++)
                    if (StateOf(quests[i]) == WelcomeState.Ready) ready++;
                return ready;
            }
        }

        // ------------------------------------------------------------- counting
        /// <summary>
        /// Records that a verb happened today.
        ///
        /// <para>
        /// Called from <see cref="TaskLedger.Note"/> and from nowhere else, which is what makes
        /// this complete: every verb this game counts is reported there already. Only a verb a
        /// live quest names is recorded, and only while a quest naming it is still unclaimed -
        /// a set that kept growing after the last quest was taken would be a list in every save
        /// for ever, about nothing.
        /// </para>
        /// <para>
        /// The day is the device's UTC day (<see cref="DailyRules.DayKeyFor"/>), the same key the
        /// task slates roll on, so "a different day" means the same thing on the tasks page and
        /// here. A clock that answers nought records nothing rather than a day nobody lived.
        /// </para>
        /// </summary>
        internal static void Note(TaskGoal goal)
        {
            if (goal == TaskGoal.None) return;

            var table = Table;
            if (table.IsEmpty || !table.Counts(goal)) return;

            bool wanted = false;
            var quests = table.Quests;
            for (int i = 0; i < quests.Count; i++)
                if (quests[i].Goal == goal && !_claimed.Contains(quests[i].Id)) { wanted = true; break; }
            if (!wanted) return;

            int day = DailyRules.DayKeyFor(GameClock.NowUnix());
            if (day <= 0) return;

            if (!_days.TryGetValue(goal, out var set))
            {
                set = new SortedSet<int>();
                _days[goal] = set;
            }

            int before = set.Count;
            if (before >= MaxDays) return;
            if (!set.Add(day)) return;
            int after = set.Count;

            SaveService.MarkDirty();
            Raise();

            // Only a quest that crossed its line on this note is news.
            for (int i = 0; i < quests.Count; i++)
            {
                var quest = quests[i];
                if (quest.Goal != goal || _claimed.Contains(quest.Id)) continue;
                if (before >= quest.Days || after < quest.Days) continue;

                Telemetry.Track("welcome_quest_completed",
                                "quest", quest.Id, "ward", quest.Ward.Id,
                                "goal", TaskGoals.Id(goal), "days", quest.Days);

                try { Completed?.Invoke(quest); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
        }

        // ------------------------------------------------------------- claiming
        /// <summary>
        /// Takes a finished quest as the turret or as its price: records the claim and the
        /// choice, and hands the prize over.
        ///
        /// <para>
        /// <b>All in one write.</b> The claim and the choice are added, the rows or the pending
        /// grant are added, and then the save is written once - so a process killed in between
        /// leaves memory, never a file, half done. A second device that claims the same quest
        /// produces the same id, and every half joins by union, so two claims are one.
        /// </para>
        /// <para>
        /// <b>The turret asks no server</b> (see the class note). <b>The price is a claim</b>:
        /// <see cref="PlayerProgression.Award"/> under <see cref="GrantEntry.WelcomeId"/>, counted
        /// toward the balance now and confirmed or re-priced by the server on the next sync; a
        /// refused claim is dropped with the balance it inflated, which is every claim's fate
        /// (45d). A turret already held on some seats keeps what it has and gains the rest.
        /// </para>
        /// </summary>
        public static bool TryClaim(WelcomeQuest quest, WelcomeReward reward)
        {
            if (quest == null || quest.Ward == null) return false;

            // Only a live quest can be taken: one the published block still names.
            if (Table.Find(quest.Id) == null) return false;
            if (StateOf(quest) != WelcomeState.Ready) return false;

            // A price of nought is a quest nothing can be taken as money for - the gate refuses
            // a starter, so this is belt and braces - and it falls back to the turret.
            if (reward == WelcomeReward.Price && quest.PriceAmount <= 0) reward = WelcomeReward.Turret;

            _claimed.Add(quest.Id);

            if (reward == WelcomeReward.Price)
            {
                _coined.Add(quest.Id);
                PlayerProgression.Award(quest.PriceCurrency, quest.PriceAmount,
                                        GrantEntry.WelcomeId(quest.Id, quest.PriceCurrency),
                                        GrantEntry.WelcomeReason, GameClock.NowUnix());
            }
            else
            {
                WardLedger.Grant(quest.Ward, "welcome:" + quest.Id);
            }

            SaveService.Save();
            Raise();

            Telemetry.Track("welcome_claimed",
                            "quest", quest.Id, "ward", quest.Ward.Id,
                            "goal", TaskGoals.Id(quest.Goal), "days", quest.Days,
                            "reward", reward == WelcomeReward.Price ? "price" : "turret",
                            "amount", reward == WelcomeReward.Price ? quest.PriceAmount : 0L,
                            "done", IsDone);
            return true;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        /// <summary>
        /// Reads one file's rows. Shared by the load and the join, for <c>TaskLedger.Read</c>'s
        /// reason: two readers would eventually disagree about what a well-formed row is.
        ///
        /// An unknown verb is dropped rather than carried, as the lifetime tally drops one: it
        /// is a row a newer build wrote and this one cannot count, and a day buys nothing. The
        /// lowest <see cref="MaxDays"/> days are kept, deterministically, so a join and a load
        /// cannot disagree about which.
        /// </summary>
        static void Read(Dictionary<TaskGoal, SortedSet<int>> days, HashSet<string> claimed,
                         HashSet<string> coined, WelcomeStateDto dto)
        {
            days.Clear();
            claimed.Clear();
            coined.Clear();
            if (dto == null) return;

            if (dto.days != null)
            {
                foreach (var row in dto.days)
                {
                    if (row == null || row.days == null || row.days.Length == 0) continue;

                    var goal = TaskGoals.Parse(row.goal);
                    if (goal == TaskGoal.None) continue;

                    if (!days.TryGetValue(goal, out var set))
                    {
                        set = new SortedSet<int>();
                        days[goal] = set;
                    }

                    foreach (int day in row.days)
                        if (day > 0) set.Add(day);

                    while (set.Count > MaxDays) set.Remove(set.Max);
                    if (set.Count == 0) days.Remove(goal);
                }
            }

            if (dto.claimed != null)
                foreach (var id in dto.claimed)
                    if (WelcomeTable.IsValidId(id)) claimed.Add(id);

            // A choice is only ever made about a claim, so a coined id with no claim beside it is
            // a row this build cannot read as anything and is dropped (a merge rewrites both).
            if (dto.coined != null)
                foreach (var id in dto.coined)
                    if (WelcomeTable.IsValidId(id) && claimed.Contains(id)) coined.Add(id);
        }

        internal static void LoadFrom(WelcomeStateDto dto)
        {
            Read(_days, _claimed, _coined, dto);
            Raise();
        }

        /// <summary>
        /// Sorted and capped, for the reason every id-keyed section of the save is:
        /// <c>SaveDelta</c> walks two arrays in order, so an unsorted writer makes every launch
        /// look changed; and a list over the rules' bound loses <em>every</em> save write
        /// (invariant 12b), so the writer caps what it sends.
        /// </summary>
        internal static WelcomeStateDto Write() => Write(_days, _claimed, _coined);

        static WelcomeStateDto Write(Dictionary<TaskGoal, SortedSet<int>> days, HashSet<string> claimed,
                                     HashSet<string> coined)
        {
            var rows = new List<WelcomeDaysDto>(days.Count);
            foreach (var pair in days)
            {
                if (pair.Value.Count == 0) continue;
                var list = new int[pair.Value.Count];
                pair.Value.CopyTo(list);
                rows.Add(new WelcomeDaysDto { goal = TaskGoals.Id(pair.Key), days = list });
            }
            rows.Sort((a, b) => string.CompareOrdinal(a.goal, b.goal));
            if (rows.Count > MaxRows) rows.RemoveRange(MaxRows, rows.Count - MaxRows);

            var ids = new List<string>(claimed);
            ids.Sort(string.CompareOrdinal);
            if (ids.Count > MaxRows) ids.RemoveRange(MaxRows, ids.Count - MaxRows);

            var priced = new List<string>();
            foreach (var id in coined)
                if (ids.Contains(id)) priced.Add(id);
            priced.Sort(string.CompareOrdinal);

            return new WelcomeStateDto { days = rows.ToArray(), claimed = ids.ToArray(), coined = priced.ToArray() };
        }

        /// <summary>
        /// Joins two devices' records: the union of days per verb and the union of claims. Both
        /// are records of things that happened, so neither side can be wrong about them, and the
        /// turret rows a claim wrote join by union beside this in <c>wardsOwned</c>.
        /// </summary>
        internal static WelcomeStateDto Join(WelcomeStateDto mine, WelcomeStateDto other)
        {
            var days = new Dictionary<TaskGoal, SortedSet<int>>();
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            var coined = new HashSet<string>(StringComparer.Ordinal);
            Read(days, claimed, coined, mine);

            var otherDays = new Dictionary<TaskGoal, SortedSet<int>>();
            var otherClaimed = new HashSet<string>(StringComparer.Ordinal);
            var otherCoined = new HashSet<string>(StringComparer.Ordinal);
            Read(otherDays, otherClaimed, otherCoined, other);

            foreach (var pair in otherDays)
            {
                if (!days.TryGetValue(pair.Key, out var set))
                {
                    set = new SortedSet<int>();
                    days[pair.Key] = set;
                }
                foreach (int day in pair.Value) set.Add(day);
                while (set.Count > MaxDays) set.Remove(set.Max);
            }

            foreach (var id in otherClaimed) claimed.Add(id);
            foreach (var id in otherCoined) coined.Add(id);

            return Write(days, claimed, coined);
        }

        /// <summary>Forgets everything. Dev only, and used by the wipe.</summary>
        internal static void Reset()
        {
            _days.Clear();
            _claimed.Clear();
            _coined.Clear();
        }
    }
}
