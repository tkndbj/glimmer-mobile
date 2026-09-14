using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Tasks;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The tasks: the calendar they run on, the rotation that deals them, the table that
    /// reads them, the ledger that counts and claims them, and the join that merges two
    /// devices' periods.
    ///
    /// <para>
    /// Four properties carry the feature and are pinned hardest. A period rolls over
    /// without anything firing at midnight, and only ever forward. Progress is derived
    /// from counters, so a task can be added by content mid-period and read correctly at
    /// once. A task pays exactly once, however many devices claim it. And the merge is a
    /// join — commutative, idempotent — because a counter of things that happened only
    /// rises. The fifth, that the chest rolls identically to the server, is in
    /// <see cref="RewardVectorTests"/> against the shared vectors.
    /// </para>
    /// </summary>
    public sealed class TaskTests
    {
        sealed class FixedClock : IGameClock
        {
            public long Now;
            public long UtcNowUnix => Now;
            public bool IsTrusted => false;
        }

        // Monday 2025-08-04 00:00 UTC is day 20304 -> week (20304+3)/7 = 2901, exactly.
        const int Monday = 20304;
        const long Noon = Monday * DailyRules.SecondsPerDay + 12L * 3600L;

        FixedClock _clock;

        [SetUp]
        public void Start()
        {
            _clock = new FixedClock { Now = Noon };
            GameClock.Set(_clock);
            TaskLedger.Reset();
            TaskLedger.LoadFrom(new SaveFileDto());
        }

        [TearDown]
        public void Restore()
        {
            GameClock.Set(new DeviceClock());
            ProgressionRules.Reset();
            TaskLedger.Reset();
            Cloud.CloudSaveService.UseBackend(null);
            CloudState.Reset();
        }

        // ------------------------------------------------------------ the week
        [Test]
        public void AWeekBeginsOnMondayUtc()
        {
            Assert.AreEqual(Monday, WeeklyRules.WeekStartDay(WeeklyRules.WeekOfDay(Monday)),
                            "a Monday is the first day of its own week");
            Assert.AreEqual(WeeklyRules.WeekOfDay(Monday), WeeklyRules.WeekOfDay(Monday + 6),
                            "the following Sunday is the same week");
            Assert.AreEqual(WeeklyRules.WeekOfDay(Monday) + 1, WeeklyRules.WeekOfDay(Monday + 7),
                            "the next Monday is the next week");
        }

        [Test]
        public void TheEpochsThursdayIsNotAWeekAnybodyHas()
        {
            Assert.AreEqual(0, WeeklyRules.WeekKeyFor(0));
            Assert.AreEqual(0, WeeklyRules.WeekKeyFor(-5));
            Assert.AreEqual(1, WeeklyRules.WeekOfDay(4), "Monday the 5th of January 1970 is week one");
        }

        [Test]
        public void TheWeeklyCountdownRunsToTheNextMondayMidnight()
        {
            long mondayMidnight = Monday * DailyRules.SecondsPerDay;

            Assert.AreEqual(WeeklyRules.SecondsPerWeek, WeeklyRules.SecondsUntilReset(mondayMidnight));
            Assert.AreEqual(1, WeeklyRules.SecondsUntilReset(mondayMidnight + WeeklyRules.SecondsPerWeek - 1));
            Assert.AreEqual(WeeklyRules.SecondsPerWeek - 3600, WeeklyRules.SecondsUntilReset(mondayMidnight + 3600));
        }

        // -------------------------------------------------------- the rotation
        [Test]
        public void TenTasksDealtThreeAtATimeShowEveryTaskInTenPeriodsWithNoRepeatBetweenNeighbours()
        {
            var seen = new HashSet<int>();
            int[] previous = new int[0];

            for (int key = 0; key < 10; key++)
            {
                var dealt = TaskRotation.Indices(10, key, 3);
                Assert.AreEqual(3, dealt.Length);
                Assert.AreEqual(3, new HashSet<int>(dealt).Count, "a period never deals one task twice");

                foreach (int i in dealt)
                {
                    Assert.IsFalse(System.Array.IndexOf(previous, i) >= 0,
                                   "two consecutive periods must not share a task");
                    seen.Add(i);
                }
                previous = dealt;
            }

            Assert.AreEqual(10, seen.Count, "every task on the slate is dealt within ten periods");
        }

        [Test]
        public void AShortSlateDealsAllOfItselfAndAnEmptyOneNothing()
        {
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, TaskRotation.Indices(2, 7, 3));
            Assert.IsEmpty(TaskRotation.Indices(0, 7, 3));
            Assert.IsEmpty(TaskRotation.Indices(5, 7, 0));
        }

        [Test]
        public void TheRotationIsAPureFunctionOfTheKey()
        {
            CollectionAssert.AreEqual(TaskRotation.Indices(10, 20304, 3), TaskRotation.Indices(10, 20304, 3));
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, TaskRotation.Indices(10, 4, 3),
                                      "key k deals k*3 .. k*3+2 modulo the slate: 12,13,14 -> 2,3,4");
        }

        [Test]
        public void ARetiredTaskLeavesTheRotationAndKeepsItsPrice()
        {
            var table = Read(Dto(retireFirstDaily: true));

            foreach (int key in new[] { 0, 1, 2, 3, 4 })
                foreach (var task in table.Active(TaskPeriod.Daily, key))
                    Assert.AreNotEqual("d_play", task.Id, "a retired task is never dealt");

            Assert.IsNotNull(table.Find("d_play"), "a retired task still resolves, so a claim for it still pays");
            Assert.IsTrue(table.Find("d_play").Retired);
        }

        // ----------------------------------------------------------- the table
        [Test]
        public void TheBuiltInTableIsWellFormed()
        {
            var table = TaskTable.Default;

            Assert.AreEqual(4, table.Tiers.Count);
            Assert.GreaterOrEqual(table.Slate(TaskPeriod.Daily).Count, 10);
            Assert.GreaterOrEqual(table.Slate(TaskPeriod.Weekly).Count, 10);
            Assert.AreEqual(3, table.ActivePerPeriod);

            long previous = 0;
            foreach (var tier in table.Tiers)
            {
                long floor = 0;
                foreach (var band in tier.Chest.Guaranteed) floor += band.Min;
                Assert.Greater(floor, previous, $"tier '{tier.Id}' must guarantee more than the one below it");
                previous = floor;

                Assert.IsNotEmpty(tier.Chest.Guaranteed, "every tier pays something");
                Assert.AreEqual(100f, TotalChance(tier.Chest), .01f, "the odds sum to a hundred");
            }

            foreach (var period in TaskPeriods.All)
                foreach (var task in table.Slate(period))
                {
                    Assert.AreEqual(period, task.Period);
                    Assert.AreNotEqual(TaskGoal.None, task.Goal);
                    Assert.Greater(task.Target, 0);
                    Assert.IsNotNull(task.Tier);
                }
        }

        [Test]
        public void AnAbsentTasksBlockIsNotAnError()
        {
            var problems = new List<string>();
            Assert.AreSame(TaskTable.Default, TaskTable.Resolve(null, problems));
            Assert.AreSame(TaskTable.Default, TaskTable.Resolve(new TaskTableDto(), problems),
                           "JsonUtility instantiates an unwritten block; that is absence, not a fault");
            Assert.IsEmpty(problems);
        }

        [Test]
        public void ATaskNamingAnUnknownTierIsRefusedWhole()
        {
            var dto = Dto();
            dto.daily[0].tier = "platinum";

            var problems = new List<string>();
            Assert.AreSame(TaskTable.Default, TaskTable.Resolve(dto, problems));
            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void ADuplicatedTaskIdIsRefusedWhole()
        {
            var dto = Dto();
            dto.weekly[0].id = dto.daily[0].id;

            var problems = new List<string>();
            Assert.AreSame(TaskTable.Default, TaskTable.Resolve(dto, problems));
            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void AnUnknownGoalIsSkippedRatherThanFatal()
        {
            var dto = Dto();
            dto.daily[1].goal = "moonwalks";

            var problems = new List<string>();
            var table = TaskTable.Resolve(dto, problems);

            Assert.AreNotSame(TaskTable.Default, table);
            Assert.IsNull(table.Find(dto.daily[1].id));
            Assert.IsNotEmpty(problems, "skipped, but named");
        }

        [Test]
        public void ATierWithNoFloorIsRefused()
        {
            var dto = Dto();
            dto.tiers[0].chest.guaranteed = new DailyDropDto[0];

            var problems = new List<string>();
            Assert.AreSame(TaskTable.Default, TaskTable.Resolve(dto, problems));
            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void AnIdThatCouldNotSurviveAClaimIsRefused()
        {
            Assert.IsTrue(TaskDefinition.IsValidId("d_play_2"));
            Assert.IsFalse(TaskDefinition.IsValidId("d:play"), "a colon is the claim id's separator");
            Assert.IsFalse(TaskDefinition.IsValidId("D_play"), "upper case is a second spelling of one id");
            Assert.IsFalse(TaskDefinition.IsValidId(""));
            Assert.IsFalse(TaskDefinition.IsValidId(new string('a', TaskDefinition.MaxIdLength + 1)));
        }

        // ---------------------------------------------------------- the ledger
        [Test]
        public void TheFirstReadOpensTodayAndThisWeek()
        {
            Assert.AreEqual(Monday, TaskLedger.Key(TaskPeriod.Daily));
            Assert.AreEqual(WeeklyRules.WeekOfDay(Monday), TaskLedger.Key(TaskPeriod.Weekly));
        }

        [Test]
        public void ANoteCountsInBothPeriodsAndProgressIsDerivedFromIt()
        {
            Publish(Read(Dto()));

            TaskLedger.Note(TaskGoal.Raiders, 7);
            TaskLedger.Note(TaskGoal.Raiders, 5);

            Assert.AreEqual(10, TaskLedger.Count(TaskPeriod.Daily, TaskGoal.Raiders),
                            "bounded by the largest daily target for the goal");
            Assert.AreEqual(12, TaskLedger.Count(TaskPeriod.Weekly, TaskGoal.Raiders));

            var daily = Find("d_raiders");
            Assert.AreEqual(10, TaskLedger.Progress(daily), "progress is the count clamped to the target");
            Assert.AreEqual(TaskState.Ready, TaskLedger.StateOf(daily));

            var weekly = Find("w_raiders");
            Assert.AreEqual(12, TaskLedger.Progress(weekly));
            Assert.AreEqual(TaskState.Open, TaskLedger.StateOf(weekly));
        }

        [Test]
        public void ACounterIsBoundedByTheLargestTargetOnTheSlate()
        {
            Publish(Read(Dto()));

            for (int i = 0; i < 100; i++) TaskLedger.Note(TaskGoal.Raiders, 10);

            Assert.AreEqual(10, TaskLedger.Count(TaskPeriod.Daily, TaskGoal.Raiders),
                            "nothing above the largest daily target is worth recording");
            Assert.AreEqual(100, TaskLedger.Count(TaskPeriod.Weekly, TaskGoal.Raiders));
        }

        [Test]
        public void MidnightRollsTheDayAndLeavesTheWeek()
        {
            Publish(Read(Dto()));
            TaskLedger.Note(TaskGoal.Runs, 2);

            _clock.Now = Noon + DailyRules.SecondsPerDay;

            Assert.AreEqual(Monday + 1, TaskLedger.Key(TaskPeriod.Daily));
            Assert.AreEqual(0, TaskLedger.Count(TaskPeriod.Daily, TaskGoal.Runs), "a new day starts empty");
            Assert.AreEqual(2, TaskLedger.Count(TaskPeriod.Weekly, TaskGoal.Runs), "the week carries on");
        }

        [Test]
        public void NextMondayRollsTheWeek()
        {
            Publish(Read(Dto()));
            TaskLedger.Note(TaskGoal.Runs, 2);

            _clock.Now = Noon + WeeklyRules.SecondsPerWeek;

            Assert.AreEqual(WeeklyRules.WeekOfDay(Monday) + 1, TaskLedger.Key(TaskPeriod.Weekly));
            Assert.AreEqual(0, TaskLedger.Count(TaskPeriod.Weekly, TaskGoal.Runs));
        }

        [Test]
        public void APeriodNeverRollsBackwards()
        {
            var ahead = new SaveFileDto
            {
                tasks = new TaskStateDto
                {
                    daily = new TaskPeriodDto { key = Monday + 3, counts = Counts(TaskGoals.Runs, 1), claimed = new string[0] },
                    weekly = new TaskPeriodDto { key = WeeklyRules.WeekOfDay(Monday) + 2, counts = new TaskCountDto[0], claimed = new string[0] },
                },
            };

            TaskLedger.LoadFrom(ahead);

            Assert.AreEqual(Monday + 3, TaskLedger.Key(TaskPeriod.Daily),
                            "a merged save from a fast clock is not reset into a day it has been paid for");
            Assert.AreEqual(1, TaskLedger.Count(TaskPeriod.Daily, TaskGoal.Runs));
        }

        [Test]
        public void ClaimingPaysOnceAndOnlyWhenReady()
        {
            Publish(Read(Dto()));
            SignedIn();

            var task = Find("d_raiders");
            Assert.IsFalse(TaskLedger.TryClaim(task, out _), "not finished");

            TaskLedger.Note(TaskGoal.Raiders, 10);
            Assert.IsTrue(TaskLedger.TryClaim(task, out var drops));
            Assert.IsNotEmpty(drops);
            Assert.AreEqual(TaskState.Claimed, TaskLedger.StateOf(task));

            Assert.IsFalse(TaskLedger.TryClaim(task, out _), "a chest pays once");
            Assert.AreEqual(1, TaskLedger.ClaimedCount(TaskPeriod.Daily));
        }

        [Test]
        public void ATaskThatIsNotDealtCannotBeClaimed()
        {
            Publish(Read(Dto()));
            SignedIn();

            // Whichever daily task is not on today's slate.
            TaskDefinition undealt = null;
            var dealt = new HashSet<string>();
            foreach (var d in TaskLedger.Active(TaskPeriod.Daily)) dealt.Add(d.Id);
            foreach (var d in ProgressionRules.Table.Tasks.Slate(TaskPeriod.Daily))
                if (!dealt.Contains(d.Id)) { undealt = d; break; }
            Assert.IsNotNull(undealt);

            TaskLedger.Note(undealt.Goal, undealt.Target);
            Assert.IsFalse(TaskLedger.TryClaim(undealt, out _),
                           "a finished task that is not on the slate pays nothing");
        }

        [Test]
        public void AClaimCarriesADerivedCurrencyId()
        {
            Assert.AreEqual("task:daily:20304:d_play:credits",
                            GrantEntry.TaskChestId(TaskPeriod.Daily, 20304, "d_play", Currency.Credits));
            Assert.AreEqual("task:weekly:2901:w_win:gems",
                            GrantEntry.TaskChestId(TaskPeriod.Weekly, 2901, "w_win", Currency.Gems));
            Assert.AreEqual("daily:20304:d_play", TaskLedger.Subject(TaskPeriod.Daily, 20304, "d_play"));
        }

        [Test]
        public void AChestHoldsTheSameThingEveryTimeItIsAskedAndDiffersByTaskAndPeriod()
        {
            Publish(Read(Dto()));
            SignedIn();

            var task = Find("d_raiders");
            var first = Describe(TaskLedger.Preview(task));
            for (int i = 0; i < 5; i++)
                Assert.AreEqual(first, Describe(TaskLedger.Preview(task)), "a chest is a fact about the task and the period");

            var seen = new HashSet<string>();
            foreach (var period in TaskPeriods.All)
                foreach (var d in ProgressionRules.Table.Tasks.Slate(period))
                    for (int key = 0; key < 8; key++)
                        seen.Add(Describe(d.Tier.Chest.Roll(TaskLedger.SeedFor(d, key))));

            Assert.Greater(seen.Count, 20, "the seed is not spreading across tasks and periods");
        }

        [Test]
        public void CompletionIsAnnouncedOnceWhenTheLineIsCrossed()
        {
            Publish(Read(Dto()));

            var finished = new List<string>();
            void OnDone(TaskDefinition t) => finished.Add(t.Id);
            TaskLedger.Completed += OnDone;
            try
            {
                TaskLedger.Note(TaskGoal.Raiders, 9);
                Assert.IsEmpty(finished);
                TaskLedger.Note(TaskGoal.Raiders, 1);
                CollectionAssert.AreEqual(new[] { "d_raiders" }, finished);
                TaskLedger.Note(TaskGoal.Raiders, 1);
                Assert.AreEqual(1, finished.Count, "announced on the crossing, not on every note after it");
            }
            finally { TaskLedger.Completed -= OnDone; }
        }

        // ----------------------------------------------------------- the merge
        [Test]
        public void TheLaterPeriodWinsOutright()
        {
            var yesterday = Period(Monday, Counts(TaskGoals.Runs, 9), "d_play");
            var today = Period(Monday + 1, Counts(TaskGoals.Runs, 1));

            var merged = TaskLedger.Join(State(yesterday, null), State(today, null)).daily;

            Assert.AreEqual(Monday + 1, merged.key);
            Assert.AreEqual(1, merged.counts[0].count, "yesterday's runs must not become a head start on today");
            Assert.IsEmpty(merged.claimed);
        }

        [Test]
        public void WithinOnePeriodCountsTakeTheLargerAndClaimsTheUnion()
        {
            var phone = Period(Monday, Counts(TaskGoals.Runs, 5, TaskGoals.Wins, 1), "d_play");
            var tablet = Period(Monday, Counts(TaskGoals.Runs, 2, TaskGoals.Stars, 4), "d_win");

            var merged = TaskLedger.Join(State(phone, null), State(tablet, null)).daily;

            Assert.AreEqual(5, CountOf(merged, TaskGoals.Runs));
            Assert.AreEqual(1, CountOf(merged, TaskGoals.Wins));
            Assert.AreEqual(4, CountOf(merged, TaskGoals.Stars));
            CollectionAssert.AreEqual(new[] { "d_play", "d_win" }, merged.claimed,
                                      "taking the smaller claim set would let a paid chest be opened again");
        }

        [Test]
        public void TheTaskMergeIsAJoin()
        {
            var a = State(Period(Monday, Counts(TaskGoals.Runs, 5), "d_play"),
                          Period(2901, Counts(TaskGoals.Wins, 2)));
            var b = State(Period(Monday, Counts(TaskGoals.Runs, 2, TaskGoals.Stars, 3), "d_win"),
                          Period(2900, Counts(TaskGoals.Wins, 9), "w_win"));

            var forward = TaskLedger.Join(a, b);
            var backward = TaskLedger.Join(b, a);
            Assert.AreEqual(Describe(forward), Describe(backward), "commutative");

            var twice = TaskLedger.Join(forward, b);
            Assert.AreEqual(Describe(forward), Describe(twice), "idempotent");

            Assert.AreEqual(2901, forward.weekly.key, "the later week wins outright");
            Assert.IsEmpty(forward.weekly.claimed);
        }

        [Test]
        public void MergingAgainstNothingKeepsWhatThereIsAndWritesSorted()
        {
            var mine = State(Period(Monday, Counts(TaskGoals.Wins, 1, TaskGoals.Bombs, 2), "d_win", "d_bombs"), null);

            var merged = TaskLedger.Join(mine, null);
            Assert.AreEqual("bombs", merged.daily.counts[0].goal, "rows come out sorted whatever went in");
            Assert.AreEqual("d_bombs", merged.daily.claimed[0]);

            var empty = TaskLedger.Join(null, null);
            Assert.AreEqual(0, empty.daily.key);
            Assert.IsEmpty(empty.weekly.counts);
        }

        [Test]
        public void AnUnknownGoalRowIsDroppedAndAnUnknownClaimIsKept()
        {
            var state = State(Period(Monday, Counts("moonwalks", 3, TaskGoals.Runs, 1), "d_future"), null);

            var merged = TaskLedger.Join(state, null);
            Assert.AreEqual(1, merged.daily.counts.Length, "a goal this build cannot count is not carried");
            CollectionAssert.AreEqual(new[] { "d_future" }, merged.daily.claimed,
                                      "a claim for a task this build does not know is a claim a newer build made");
        }

        // ----------------------------------------------------------- the delta
        [Test]
        public void ACounterThatMovedIsAChangeWorthPushing()
        {
            var before = SaveWith(State(Period(Monday, Counts(TaskGoals.Runs, 1)), null));
            var after = SaveWith(State(Period(Monday, Counts(TaskGoals.Runs, 2)), null));

            Assert.IsFalse(SaveDelta.Between(before, after).IsEmpty);

            var claimed = SaveWith(State(Period(Monday, Counts(TaskGoals.Runs, 1), "d_play"), null));
            Assert.IsFalse(SaveDelta.Between(before, claimed).IsEmpty, "a claim has to reach the other device");
        }

        [Test]
        public void AnUnchangedSlateSendsNothing()
        {
            var state = State(Period(Monday, Counts(TaskGoals.Runs, 1), "d_play"), Period(2901, Counts(TaskGoals.Wins, 3)));
            Assert.IsTrue(SaveDelta.Between(SaveWith(state), SaveWith(state)).IsEmpty,
                          "an unchanged save must not burn a document write");
        }

        // ------------------------------------------------------------ the gate
        [Test]
        public void AChestCannotBeClaimedBeforeTheAccountExists()
        {
            CloudState.Reset();
            Cloud.CloudSaveService.UseBackend(new DailyChestTests.PresentBackend());

            Assert.IsFalse(TaskLedger.CanClaim,
                           "a chest rolled without an account id is one the server would re-roll differently");
        }

        // ------------------------------------------------------ the contract
        /// <summary>
        /// Four of the shared vectors, inline, so the seeding is proved without the Editor
        /// (invariant 29e: a fixture that needs JsonUtility compares nothing offline). The
        /// full set is <see cref="RewardVectorTests.EveryTaskChestVectorMatches"/>; these are
        /// the same tiers and the same expected drops, copied from the file by hand.
        /// </summary>
        [Test]
        public void TheSubjectSeedingMatchesTheSharedVectorsInline()
        {
            var problems = new List<string>();

            var wood = DailyChestTable.ReadChest(new DailyChestEntryDto
            {
                guaranteed = new[] { new DailyDropDto { kind = "credits", min = 10, max = 99 } },
                options = new[]
                {
                    new DailyOptionDto { kind = "credits", min = 5, max = 5, weight = 2 },
                    new DailyOptionDto { kind = "hearts", min = 1, max = 3, weight = 7 },
                    new DailyOptionDto { kind = "gems", min = 1, max = 2, weight = 1 },
                },
            }, "wood", problems);

            var gold = DailyChestTable.ReadChest(new DailyChestEntryDto
            {
                guaranteed = new[]
                {
                    new DailyDropDto { kind = "credits", min = 100, max = 100 },
                    new DailyDropDto { kind = "utility", item = "firepot", min = 1, max = 1 },
                },
                options = new[]
                {
                    new DailyOptionDto { kind = "utility", item = "firepot", min = 2, max = 2, weight = 1 },
                    new DailyOptionDto { kind = "utility", item = "surge", min = 1, max = 1, weight = 1 },
                    new DailyOptionDto { kind = "heart_boost", min = 6, max = 12, weight = 1 },
                },
            }, "gold", problems);

            var royal = DailyChestTable.ReadChest(new DailyChestEntryDto
            {
                guaranteed = new[] { new DailyDropDto { kind = "credits", min = 1, max = 100000 } },
                options = new[] { new DailyOptionDto { kind = "gems", min = 1, max = 50, weight = 1 } },
            }, "royal", problems);

            Assert.IsEmpty(problems, string.Join("; ", problems));

            string Roll(ChestDefinition chest, TaskPeriod period, int key, string task)
                => Describe(chest.Roll(ChestSeed.ForSubject("uid_abc123", TaskLedger.SeedTag,
                                                            TaskLedger.Subject(period, key, task))));

            Assert.AreEqual("95 credits,3 hearts", Roll(wood, TaskPeriod.Daily, 20304, "d_play"));
            Assert.AreEqual("100 credits,1 utility:firepot,7 heart_boost", Roll(gold, TaskPeriod.Daily, 20304, "d_play"));
            Assert.AreEqual("79056 credits,43 gems", Roll(royal, TaskPeriod.Daily, 20304, "d_play"));
            Assert.AreEqual("61920 credits,46 gems", Roll(royal, TaskPeriod.Weekly, 2901, "w_win"));

            // The two traps: one id in the other period, and a day key equal to a week key.
            Assert.AreEqual("37 credits,1 hearts", Roll(wood, TaskPeriod.Weekly, 2901, "d_play"));
            Assert.AreEqual("100 credits,1 utility:firepot,1 utility:surge", Roll(gold, TaskPeriod.Daily, 2901, "w_win"));
        }

        // ------------------------------------------------------ the refusal
        /// <summary>
        /// A claim the server refuses used to stay in the ledger for ever: counted toward the
        /// balance, resubmitted every sync, refused every time. The server's answer governs.
        /// </summary>
        [Test]
        public void ARefusedClaimIsDroppedFromTheLedgerWithTheBalanceItInflated()
        {
            var ledger = new CurrencyLedger(Currency.Credits);
            Assert.IsTrue(ledger.TryAward("task:daily:20304:d_play:credits", 90, 1_700_000_000, GrantEntry.TaskChestReason, out _));
            Assert.IsTrue(ledger.TryAward("task:daily:20304:d_win:credits", 80, 1_700_000_000, GrantEntry.TaskChestReason, out _));
            Assert.AreEqual(170, ledger.BalanceFrom(0));

            ledger.ApplyServerState(90, 0, new string[0], 0, 0,
                                    confirmedGrantIds: new[] { "task:daily:20304:d_play:credits" },
                                    rejectedGrantIds: new[] { "task:daily:20304:d_win:credits" });

            Assert.AreEqual(90, ledger.BalanceFrom(0), "the confirmed claim is in the baseline; the refused one is gone");
            Assert.IsFalse(ledger.HasGranted("task:daily:20304:d_win:credits"));
        }

        // --------------------------------------------------------- the art
        /// <summary>
        /// Every picture a task row or the hub's ladder asks for is one the manifest loads, and
        /// every tier's reel is one the chest scope declares. Both addresses are built from an
        /// id, so <c>artnames.py</c> reads neither — a missing one is a white rectangle on the
        /// first screen after the splash (invariant 7b).
        /// </summary>
        [Test]
        public void EveryGoalGlyphAndChestPictureIsLoadedByTheManifest()
        {
            var global = new HashSet<string>();
            foreach (var request in AssetPipeline.AssetManifest.GlobalAssets()) global.Add(request.Address);

            foreach (var goal in TaskGoals.All)
            {
                string icon = TaskGoals.Icon(goal);
                Assert.IsNotEmpty(icon, $"goal '{TaskGoals.Id(goal)}' has no picture");
                Assert.That(global, Contains.Item(AssetPipeline.AssetManifest.ArtRoot + icon),
                            $"goal '{TaskGoals.Id(goal)}' draws '{icon}', which the manifest does not load");
            }

            var reels = new HashSet<string>();
            foreach (var request in AssetPipeline.AssetManifest.ChestAssets(TaskTable.Default))
            {
                Assert.AreEqual(AssetPipeline.AssetKind.SpriteSet, request.Kind, "a reel is a folder of frames");
                reels.Add(request.Address);
            }

            foreach (var tier in TaskTable.Default.Tiers)
            {
                Assert.That(global, Contains.Item(AssetPipeline.AssetManifest.ArtRoot + tier.Icon),
                            $"tier '{tier.Id}' draws '{tier.Icon}', which the manifest does not load");
                Assert.That(reels, Contains.Item(AssetPipeline.AssetManifest.ArtRoot + tier.Reel),
                            $"tier '{tier.Id}' opens with '{tier.Reel}', which no scope declares");
            }
        }

        // --------------------------------------------------- the security rules
        [Test]
        public void TheListBoundsMatchTheSecurityRules()
        {
            string rules = System.IO.File.ReadAllText(
                System.IO.Path.Combine(
                    System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName,
                    "firebase", "firestore.rules"));

            Assert.IsTrue(rules.Contains("'tasks'"), "tasks is not in the hasOnly list; every save write would be rejected");
            Assert.IsTrue(rules.Contains($"p.counts.size() <= {TaskLedger.MaxGoals}"),
                          $"the rules' counts bound does not match TaskLedger.MaxGoals ({TaskLedger.MaxGoals})");
            Assert.IsTrue(rules.Contains($"p.claimed.size() <= {TaskLedger.MaxClaimed}"),
                          $"the rules' claimed bound does not match TaskLedger.MaxClaimed ({TaskLedger.MaxClaimed})");

            Assert.LessOrEqual(TaskGoals.All.Length, TaskLedger.MaxGoals,
                               "more goals than the rules allow rows for is a save write refused");
            Assert.LessOrEqual(TaskTable.Default.ActivePerPeriod, TaskLedger.MaxClaimed);
        }

        // ------------------------------------------------------------ helpers
        static void Publish(TaskTable tasks)
        {
            var problems = new List<string>();
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                maxLevel = 20,
                xpToNext = new[] { 100, 150 },
                tailXpToNext = 200,
                tailXpIncrement = 50,
                tasks = Dto(),
            };
            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, problems), string.Join("; ", problems));
            ProgressionRules.Publish(table);
        }

        static void SignedIn()
        {
            CloudState.Reset();
            Cloud.CloudSaveService.UseBackend(null);
        }

        static TaskDefinition Find(string id)
        {
            var task = ProgressionRules.Table.Tasks.Find(id);
            Assert.IsNotNull(task, id);
            return task;
        }

        /// <summary>
        /// A synthetic slate whose daily rotation deals every raider task on every day:
        /// three dailies and three weeklies, so each is always dealt, plus enough more that
        /// the rotation still has something to leave out.
        /// </summary>
        static TaskTableDto Dto(bool retireFirstDaily = false)
        {
            DailyChestEntryDto Chest(int floor)
                => new DailyChestEntryDto
                {
                    guaranteed = new[] { new DailyDropDto { kind = "credits", min = floor, max = floor + 10 } },
                    options = new[]
                    {
                        new DailyOptionDto { kind = "gems", min = 1, max = 2, weight = 60 },
                        new DailyOptionDto { kind = "hearts", min = 1, max = 1, weight = 40 },
                    },
                };

            return new TaskTableDto
            {
                activePerPeriod = 3,
                tiers = new[]
                {
                    new TaskTierDto { id = "wood", chest = Chest(50) },
                    new TaskTierDto { id = "gold", chest = Chest(200) },
                },
                daily = new[]
                {
                    new TaskEntryDto { id = "d_play", goal = "runs", target = 2, tier = "wood", retired = retireFirstDaily },
                    new TaskEntryDto { id = "d_raiders", goal = "raiders", target = 10, tier = "wood" },
                    new TaskEntryDto { id = "d_win", goal = "wins", target = 1, tier = "gold" },
                    new TaskEntryDto { id = "d_bombs", goal = "bombs", target = 1, tier = "gold" },
                },
                weekly = new[]
                {
                    new TaskEntryDto { id = "w_raiders", goal = "raiders", target = 100, tier = "gold" },
                    new TaskEntryDto { id = "w_win", goal = "wins", target = 5, tier = "gold" },
                    new TaskEntryDto { id = "w_play", goal = "runs", target = 9, tier = "wood" },
                },
            };
        }

        static TaskTable Read(TaskTableDto dto)
        {
            var problems = new List<string>();
            var table = TaskTable.Resolve(dto, problems);
            Assert.IsEmpty(problems, string.Join("; ", problems));
            Assert.AreNotSame(TaskTable.Default, table);
            return table;
        }

        static float TotalChance(ChestDefinition chest)
        {
            float total = 0f;
            for (int i = 0; i < chest.Options.Count; i++) total += chest.ChanceOf(i);
            return total;
        }

        static TaskCountDto[] Counts(params object[] pairs)
        {
            var rows = new List<TaskCountDto>();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                rows.Add(new TaskCountDto { goal = (string)pairs[i], count = (int)pairs[i + 1] });
            return rows.ToArray();
        }

        static TaskPeriodDto Period(int key, TaskCountDto[] counts, params string[] claimed)
            => new TaskPeriodDto { key = key, counts = counts, claimed = claimed };

        static TaskStateDto State(TaskPeriodDto daily, TaskPeriodDto weekly)
            => new TaskStateDto { daily = daily, weekly = weekly };

        static SaveFileDto SaveWith(TaskStateDto tasks)
            => new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                levels = new LevelRecordDto[0],
                tasks = tasks,
            };

        static int CountOf(TaskPeriodDto period, string goal)
        {
            foreach (var row in period.counts ?? new TaskCountDto[0])
                if (row.goal == goal) return row.count;
            return 0;
        }

        static string Describe(TaskStateDto state)
            => Describe(state.daily) + " | " + Describe(state.weekly);

        static string Describe(TaskPeriodDto p)
        {
            var parts = new List<string> { "k=" + p.key };
            foreach (var row in p.counts ?? new TaskCountDto[0]) parts.Add(row.goal + "=" + row.count);
            foreach (var id in p.claimed ?? new string[0]) parts.Add("!" + id);
            return string.Join(",", parts);
        }

        static string Describe(List<ChestDrop> drops)
        {
            var parts = new List<string>();
            foreach (var drop in drops) parts.Add(drop.ToString());
            return string.Join(",", parts);
        }
    }
}
