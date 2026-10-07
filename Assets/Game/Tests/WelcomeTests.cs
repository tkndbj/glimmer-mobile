using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Tasks;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The welcome bonus (invariant 58): the table that reads it, the ledger that counts days
    /// and hands turrets over, the join that merges two devices' records, and the shipped block.
    ///
    /// <para>
    /// Four properties carry the feature. A verb counts <em>once per day</em>, however often it
    /// happens, and only while a live quest names it. A quest pays exactly once, on its own one
    /// seat, and never before its days are in. The merge is a join - commutative, idempotent - because
    /// a set of days and a set of claims only ever grow. And the door on the hub is the
    /// ledger's answer and nothing device-local: live and something unclaimed.
    /// </para>
    /// </summary>
    public sealed class WelcomeTests
    {
        sealed class FixedClock : IGameClock
        {
            public long Now;
            public long UtcNowUnix => Now;
            public bool IsTrusted => false;
        }

        const int Day = 20304;
        const long Noon = Day * DailyRules.SecondsPerDay + 12L * 3600L;

        FixedClock _clock;

        [SetUp]
        public void Start()
        {
            _clock = new FixedClock { Now = Noon };
            GameClock.Set(_clock);
            TaskLedger.Reset();
            TaskLedger.LoadFrom(new SaveFileDto());
            WardLedger.LoadFrom(new SaveFileDto());
        }

        [TearDown]
        public void Restore()
        {
            GameClock.Set(new DeviceClock());
            ProgressionRules.Reset();
            TaskLedger.Reset();
            WardLedger.LoadFrom(new SaveFileDto());
        }

        // ------------------------------------------------------------ the verb
        [Test]
        public void TaskClaimsIsACountedVerb()
        {
            Assert.AreEqual(TaskGoal.TaskClaims, TaskGoals.Parse(TaskGoals.TaskClaims));
            Assert.AreEqual(TaskGoals.TaskClaims, TaskGoals.Id(TaskGoal.TaskClaims));
            Assert.Contains(TaskGoal.TaskClaims, TaskGoals.All);
            Assert.IsNotEmpty(TaskGoals.Icon(TaskGoal.TaskClaims));
        }

        [Test]
        public void EveryVerbWithASentenceIsCountedAndSaysItInEnglish()
        {
            var english = ShippedStrings.Table();
            foreach (var goal in WelcomeGoals.All)
            {
                Assert.Contains(goal, TaskGoals.All, $"{goal} has a sentence but is not counted");
                string key = WelcomeGoals.SentenceKey(goal);
                Assert.IsTrue(english.ContainsKey(key), $"'{key}' is not in loc/en.json");
                StringAssert.Contains("{0}", english[key], $"'{key}' has no slot for the day count");
            }
            Assert.IsEmpty(WelcomeGoals.SentenceKey(TaskGoal.Raiders), "a verb with no sentence answers nothing");
        }

        // ----------------------------------------------------------- the table
        [Test]
        public void AnAbsentBlockIsTheFeatureOff()
        {
            var problems = new List<string>();
            Assert.AreSame(WelcomeTable.Empty, WelcomeTable.Resolve(null, WardCatalog.Default, problems));
            Assert.AreSame(WelcomeTable.Empty, WelcomeTable.Resolve(new WelcomeDto(), WardCatalog.Default, problems));
            Assert.IsEmpty(problems, "absent is not an error");

            Publish(null);
            Assert.IsFalse(WelcomeLedger.Live);
            Assert.IsTrue(WelcomeLedger.IsDone, "nothing live is nothing owed");
            Assert.IsFalse(WelcomeLedger.Offered, "no door");

            TaskLedger.Note(TaskGoal.Runs);
            Assert.AreEqual(0, WelcomeLedger.DaysCounted(TaskGoal.Runs), "nothing counts while the block is off");
        }

        [Test]
        public void TheLadderReadsAndEachQuestWearsItsOwnColour()
        {
            var dto = Dto();
            var table = Read(dto);
            Assert.AreEqual(4, table.Quests.Count);
            Assert.AreEqual("leech", table.Quests[0].Ward.Id);
            Assert.AreEqual(TaskGoal.Runs, table.Quests[0].Goal);
            Assert.AreEqual(3, table.Quests[0].Days);
            Assert.AreEqual("ui.welcome.goal.runs", table.Quests[0].SentenceKey);

            for (int i = 0; i < table.Quests.Count; i++)
            {
                Assert.AreEqual(i, table.Quests[i].Ordinal);
                Assert.AreEqual(WardLine.Colours.IndexOf(dto.quests[i].colour[0]), table.Quests[i].Colour,
                                "the authored seat");
            }

            Assert.IsTrue(table.Counts(TaskGoal.Runs));
            Assert.IsFalse(table.Counts(TaskGoal.Raiders));
            Assert.IsNotNull(table.Find("w3"));
            Assert.IsNull(table.Find("w9"));
        }

        /// <summary>
        /// A quest's seat travels with the quest, never with its row: dropping the first quest or
        /// putting a new one ahead of it changes nobody else's prize (the owner, 2026-10-07).
        /// </summary>
        [Test]
        public void RemovingOrInsertingAQuestChangesNoOtherQuestsSeat()
        {
            var before = Read(Dto());

            var dropped = Dto();
            dropped.quests = new[] { dropped.quests[1], dropped.quests[2], dropped.quests[3] };
            var after = Read(dropped);
            foreach (var quest in after.Quests)
                Assert.AreEqual(before.Find(quest.Id).Colour, quest.Colour, $"{quest.Id} after a removal");

            var inserted = Dto();
            inserted.quests = new[]
            {
                new WelcomeQuestDto { id = "w0", ward = "lighthouse", colour = "y", goal = "wins", days = 2 },
                inserted.quests[0], inserted.quests[2], inserted.quests[3],
            };
            after = Read(inserted);
            foreach (var quest in after.Quests)
                if (quest.Id != "w0")
                    Assert.AreEqual(before.Find(quest.Id).Colour, quest.Colour, $"{quest.Id} after an insertion");
        }

        /// <summary>
        /// One loop rather than ten <c>[TestCase]</c>s, because the offline runner skips a
        /// parameterised test and this is the gate that keeps a bad block out of a build.
        /// </summary>
        [Test]
        public void AMalformedRowRefusesTheWholeBlock()
        {
            var cases = new (string field, string value, string why)[]
            {
                ("ward", "nobody", "a turret the roster does not hold"),
                ("ward", "STARTER", "the free starter"),
                ("ward", "leech", "a turret paid twice"),      // the first row's turret, again
                ("goal", "raiders", "a verb with no sentence"),
                ("goal", "jumps", "a verb nobody counts"),
                ("colour", "", "no seat"),
                ("colour", null, "a row written before seats were authored"),
                ("colour", "x", "a seat that does not exist"),
                ("colour", "R", "a seat letter in the wrong case"),
                ("colour", "rg", "two seats"),
                ("days", "1", "a one-day quest"),
                ("days", "40", "more days than the save records"),
                ("days", "2", "a ladder that falls"),
                ("id", "w1", "a repeated id"),
                ("id", "W-2", "a bad id"),
            };

            foreach (var (field, value, why) in cases)
            {
                var dto = Dto();
                var row = dto.quests[2];                   // the third row, so a fall is a fall
                switch (field)
                {
                    case "ward": row.ward = value == "STARTER" ? Starter().Id : value; break;
                    case "goal": row.goal = value; break;
                    case "colour": row.colour = value; break;
                    case "days": row.days = int.Parse(value); break;
                    case "id": row.id = value; break;
                }

                var problems = new List<string>();
                var table = WelcomeTable.Resolve(dto, WardCatalog.Default, problems);
                Assert.AreSame(WelcomeTable.Empty, table, why);
                Assert.IsNotEmpty(problems, why);
            }
        }

        [Test]
        public void TooManyQuestsAreRefused()
        {
            var rows = new List<WelcomeQuestDto>();
            var models = WardCatalog.Default.Models;
            int days = 2;
            foreach (var model in models)
            {
                if (model.IsStarter) continue;
                rows.Add(new WelcomeQuestDto { id = "q" + rows.Count, ward = model.Id, colour = "r", goal = "runs", days = days++ });
                if (rows.Count > WelcomeTable.MaxQuests) break;
            }
            Assume.That(rows.Count, Is.GreaterThan(WelcomeTable.MaxQuests));

            var problems = new List<string>();
            Assert.AreSame(WelcomeTable.Empty,
                           WelcomeTable.Resolve(new WelcomeDto { quests = rows.ToArray() }, WardCatalog.Default, problems));
            Assert.IsNotEmpty(problems);
            Assert.LessOrEqual(WelcomeTable.MaxQuests, WelcomeLedger.MaxRows, "the page's capacity bounds the save's rows");
        }

        // ---------------------------------------------------------- counting
        [Test]
        public void ADayCountsOnceHoweverOftenTheVerbHappens()
        {
            Publish(Dto());
            var quest = Quest("w1");

            TaskLedger.Note(TaskGoal.Runs);
            TaskLedger.Note(TaskGoal.Runs);
            TaskLedger.Note(TaskGoal.Runs, 5);
            Assert.AreEqual(1, WelcomeLedger.DaysCounted(TaskGoal.Runs), "three battles in one day are one day");
            Assert.AreEqual(1, WelcomeLedger.DaysDone(quest));
            Assert.AreEqual(WelcomeState.Open, WelcomeLedger.StateOf(quest));

            _clock.Now += DailyRules.SecondsPerDay;
            TaskLedger.Note(TaskGoal.Runs);
            Assert.AreEqual(2, WelcomeLedger.DaysCounted(TaskGoal.Runs), "tomorrow is a second day");

            _clock.Now += 5 * DailyRules.SecondsPerDay;
            TaskLedger.Note(TaskGoal.Runs);
            Assert.AreEqual(3, WelcomeLedger.DaysCounted(TaskGoal.Runs), "the days need not be in a row");
            Assert.AreEqual(WelcomeState.Ready, WelcomeLedger.StateOf(quest));
            Assert.AreEqual(1, WelcomeLedger.ReadyCount);
        }

        [Test]
        public void OnlyAVerbALiveQuestNamesIsRecorded()
        {
            Publish(Dto());
            TaskLedger.Note(TaskGoal.Raiders, 40);
            TaskLedger.Note(TaskGoal.Wins);
            Assert.AreEqual(0, WelcomeLedger.DaysCounted(TaskGoal.Raiders));
            Assert.AreEqual(0, WelcomeLedger.DaysCounted(TaskGoal.Wins));

            var dto = new SaveFileDto();
            TaskLedger.WriteInto(dto);
            Assert.IsEmpty(dto.tasks.welcome.days, "nothing written for a verb nobody asks about");
        }

        [Test]
        public void ProgressIsDerivedAndClampedAndCompletionIsAnnouncedOnce()
        {
            Publish(Dto());
            var quest = Quest("w1");
            var announced = new List<string>();
            void OnDone(WelcomeQuest q) => announced.Add(q.Id);
            WelcomeLedger.Completed += OnDone;
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    TaskLedger.Note(TaskGoal.Runs);
                    _clock.Now += DailyRules.SecondsPerDay;
                }
            }
            finally { WelcomeLedger.Completed -= OnDone; }

            Assert.AreEqual(6, WelcomeLedger.DaysCounted(TaskGoal.Runs), "the record is the days, unclamped");
            Assert.AreEqual(3, WelcomeLedger.DaysDone(quest), "progress is clamped to the target");
            CollectionAssert.AreEqual(new[] { "w1" }, announced, "crossing the line is announced once");
        }

        [Test]
        public void AClockThatAnswersNothingRecordsNoDay()
        {
            Publish(Dto());
            _clock.Now = 0L;
            TaskLedger.Note(TaskGoal.Runs);
            Assert.AreEqual(0, WelcomeLedger.DaysCounted(TaskGoal.Runs));
        }

        // ---------------------------------------------------------- claiming
        /// <summary>
        /// One quest, one turret: the seat its row wears and no other (the owner, 2026-10-07). It
        /// handed over all four, which was four shelf prices against a price option worth one.
        /// </summary>
        [Test]
        public void ClaimingGrantsTheQuestsOwnSeatOnceAndNeverBeforeTheDaysAreIn()
        {
            Publish(Dto());
            var quest = Quest("w1");
            char seat = WardLine.Colours[quest.Colour];

            Assert.IsFalse(WelcomeLedger.TryClaim(quest, WelcomeReward.Turret), "not enough days");
            foreach (char c in WardLine.Colours) Assert.IsFalse(WardLedger.IsHeld(quest.Ward, c));

            Days(TaskGoal.Runs, 3);
            Assert.AreEqual(WelcomeState.Ready, WelcomeLedger.StateOf(quest));

            Assert.IsTrue(WelcomeLedger.TryClaim(quest, WelcomeReward.Turret));
            Assert.AreEqual(WelcomeState.Claimed, WelcomeLedger.StateOf(quest));
            foreach (char c in WardLine.Colours)
                Assert.AreEqual(c == seat, WardLedger.IsHeld(quest.Ward, c), $"held on {c} only if it is the quest's seat");
            Assert.AreEqual(1, WardLedger.BoughtCount, "one seat row");

            var dto = new SaveFileDto();
            WardLedger.WriteInto(dto);
            CollectionAssert.AreEqual(new[] { WardHolding.Row(quest.Ward, quest.Colour) }, dto.wardsOwned,
                                      "in the purchase spelling, never the bare id that means all four");

            Assert.IsFalse(WelcomeLedger.TryClaim(quest, WelcomeReward.Turret), "a quest pays once");
            Assert.AreEqual(1, WardLedger.BoughtCount);

            // Still live: three quests are owed, so the door stays.
            Assert.IsTrue(WelcomeLedger.Offered);
            Assert.AreEqual(0, WelcomeLedger.ReadyCount);
        }

        [Test]
        public void EachQuestPaysItsOwnColour()
        {
            Publish(Dto());
            foreach (var quest in ProgressionRules.Table.Welcome.Quests)
            {
                Days(quest.Goal, quest.Days);
                Assert.IsTrue(WelcomeLedger.TryClaim(quest, WelcomeReward.Turret), quest.Id);
                for (int c = 0; c < WardLine.Colours.Length; c++)
                    Assert.AreEqual(c == quest.Colour, WardLedger.IsHeld(quest.Ward, c),
                                    $"{quest.Id} on seat {WardLine.Colours[c]}");
            }
            Assert.AreEqual(ProgressionRules.Table.Welcome.Quests.Count, WardLedger.BoughtCount, "one row a quest");
        }

        [Test]
        public void ASeatAlreadyHeldIsKeptAndNothingElseIsAdded()
        {
            Publish(Dto());
            var quest = Quest("w1");
            char seat = WardLine.Colours[quest.Colour];
            char other = WardLine.Colours[(quest.Colour + 1) % WardLine.Colours.Length];

            // Bought on another seat: kept, and the quest adds its own.
            WardLedger.LoadFrom(new SaveFileDto { wardsOwned = new[] { WardHolding.Key(quest.Ward.Id, other) } });
            Days(TaskGoal.Runs, 3);
            Assert.IsTrue(WelcomeLedger.TryClaim(quest, WelcomeReward.Turret));
            Assert.AreEqual(2, WardLedger.BoughtCount, "the bought row and the quest's seat");
            Assert.IsTrue(WardLedger.IsHeld(quest.Ward, other));
            Assert.IsTrue(WardLedger.IsHeld(quest.Ward, seat));

            // Already held on the quest's own seat: nothing new, nothing lost.
            Assert.IsFalse(WardLedger.Grant(quest.Ward, quest.Colour, "test"));
            Assert.AreEqual(2, WardLedger.BoughtCount);
        }

        [Test]
        public void RecordingStopsOnceEveryQuestOnTheVerbIsTaken()
        {
            Publish(Dto());
            var quest = Quest("w1");
            Days(TaskGoal.Runs, 3);
            Assert.IsTrue(WelcomeLedger.TryClaim(quest, WelcomeReward.Turret));

            _clock.Now += DailyRules.SecondsPerDay;
            TaskLedger.Note(TaskGoal.Runs);
            Assert.AreEqual(3, WelcomeLedger.DaysCounted(TaskGoal.Runs), "a taken quest's verb grows no list");
        }

        [Test]
        public void TheDoorGoesWhenTheLastQuestIsTakenAndNeverComesBack()
        {
            Publish(Dto());
            Assert.IsTrue(WelcomeLedger.Offered);

            foreach (var quest in ProgressionRules.Table.Welcome.Quests)
            {
                Days(quest.Goal, quest.Days);
                Assert.IsTrue(WelcomeLedger.TryClaim(quest, WelcomeReward.Turret), quest.Id);
            }

            Assert.IsTrue(WelcomeLedger.IsDone);
            Assert.IsFalse(WelcomeLedger.Offered, "every quest taken: no door");

            // The record survives a round trip through the file, so the next launch agrees.
            var dto = new SaveFileDto();
            TaskLedger.WriteInto(dto);
            TaskLedger.LoadFrom(dto);
            Assert.IsTrue(WelcomeLedger.IsDone);
            Assert.IsFalse(WelcomeLedger.Offered);
            Assert.AreEqual(4, dto.tasks.welcome.claimed.Length);
        }

        [Test]
        public void AQuestTheBlockNoLongerNamesCannotBeTaken()
        {
            Publish(Dto());
            var quest = Quest("w1");
            Days(TaskGoal.Runs, 3);

            // A content push withdraws the block under the open page.
            Publish(null);
            Assert.IsFalse(WelcomeLedger.TryClaim(quest, WelcomeReward.Turret));
            Assert.AreEqual(0, WardLedger.BoughtCount);
        }

        // ------------------------------------------------------------- the price
        [Test]
        public void ThePriceIsTheShelfPriceAndItsIdIsDerived()
        {
            var table = Read(Dto());
            var quest = table.Quests[0];
            // The built-in roster prices this turret in whichever currency it prices it in; the
            // quest follows the roster, which is the point.
            long shelf = quest.Ward.ForGems ? quest.Ward.GemPrice : quest.Ward.CoinPrice;
            Assert.AreEqual(shelf, quest.PriceAmount, "the roster's figure, never authored here");
            Assert.Greater(quest.PriceAmount, 0L);
            Assert.AreEqual(quest.Ward.ForGems ? Currency.Gems : Currency.Credits, quest.PriceCurrency);
            Assert.AreEqual("welcome:w1:" + quest.PriceCurrency, GrantEntry.WelcomeId(quest.Id, quest.PriceCurrency));
            Assert.AreEqual("welcome:w1_leech:credits", GrantEntry.WelcomeId("w1_leech", Currency.Credits));
        }

        [Test]
        public void TakingThePriceAwardsAClaimAndGrantsNoSeat()
        {
            Publish(Dto());
            var quest = Quest("w1");
            Days(TaskGoal.Runs, 3);

            string currency = quest.PriceCurrency;
            long before = PlayerProgression.Balance(currency);
            Assert.IsTrue(WelcomeLedger.TryClaim(quest, WelcomeReward.Price));

            Assert.AreEqual(WelcomeState.Claimed, WelcomeLedger.StateOf(quest));
            Assert.IsTrue(WelcomeLedger.TookPrice(quest));
            Assert.AreEqual(0, WardLedger.BoughtCount, "the price, not the turret");
            Assert.AreEqual(before + quest.PriceAmount, PlayerProgression.Balance(currency),
                            "counted toward the balance now, as every pending claim is");
            bool pending = false;
            foreach (var entry in Wallet.Ledger(currency).PendingGrants)
                if (entry.Id == GrantEntry.WelcomeId(quest.Id, currency) && entry.Amount == quest.PriceAmount) pending = true;
            Assert.IsTrue(pending, "a pending grant under the derived id, for the server to re-price");

            Assert.IsFalse(WelcomeLedger.TryClaim(quest, WelcomeReward.Turret), "one prize per quest");
            Assert.AreEqual(0, WardLedger.BoughtCount);

            var dto = new SaveFileDto();
            TaskLedger.WriteInto(dto);
            CollectionAssert.AreEqual(new[] { "w1" }, dto.tasks.welcome.claimed);
            CollectionAssert.AreEqual(new[] { "w1" }, dto.tasks.welcome.coined, "the choice travels with the claim");
        }

        [Test]
        public void TakingTheTurretRecordsNoPrice()
        {
            Publish(Dto());
            var quest = Quest("w1");
            Days(TaskGoal.Runs, 3);
            Assert.IsTrue(WelcomeLedger.TryClaim(quest, WelcomeReward.Turret));
            Assert.IsFalse(WelcomeLedger.TookPrice(quest));

            var dto = new SaveFileDto();
            TaskLedger.WriteInto(dto);
            Assert.IsEmpty(dto.tasks.welcome.coined);
        }

        [Test]
        public void TheChoiceJoinsByUnionAndNeverOutrunsTheClaims()
        {
            var a = new WelcomeStateDto { claimed = new[] { "w1" }, coined = new[] { "w1" } };
            var b = new WelcomeStateDto { claimed = new[] { "w2" }, coined = new[] { "w3" } };

            var joined = WelcomeLedger.Join(a, b);
            CollectionAssert.AreEqual(new[] { "w1", "w2" }, joined.claimed);
            CollectionAssert.AreEqual(new[] { "w1" }, joined.coined, "a choice about an unclaimed quest is nothing");

            Assert.AreEqual(Show(joined), Show(WelcomeLedger.Join(b, a)), "commutative");
        }

        // ------------------------------------------------------------- the file
        [Test]
        public void TheWriterIsCanonicalAndTheReaderDropsWhatItCannotCount()
        {
            var messy = new WelcomeStateDto
            {
                days = new[]
                {
                    new WelcomeDaysDto { goal = "streak", days = new[] { 9, 3, 3, 0, -1, 7 } },
                    new WelcomeDaysDto { goal = "runs", days = new[] { 5 } },
                    new WelcomeDaysDto { goal = "nothing_anyone_counts", days = new[] { 1, 2 } },
                    new WelcomeDaysDto { goal = "wins", days = new int[0] },
                    null,
                },
                claimed = new[] { "w2", "w1", "", "Bad Id", "w2" },
            };

            var canon = WelcomeLedger.Join(messy, null);
            Assert.AreEqual(2, canon.days.Length, "unknown and empty rows are dropped");
            Assert.AreEqual("runs", canon.days[0].goal, "sorted by goal id");
            Assert.AreEqual("streak", canon.days[1].goal);
            CollectionAssert.AreEqual(new[] { 3, 7, 9 }, canon.days[1].days, "distinct, ascending, positive");
            CollectionAssert.AreEqual(new[] { "w1", "w2" }, canon.claimed, "distinct, sorted, well-formed");

            var again = WelcomeLedger.Join(canon, null);
            CollectionAssert.AreEqual(canon.days[1].days, again.days[1].days, "canonical is a fixed point");
            CollectionAssert.AreEqual(canon.claimed, again.claimed);
        }

        [Test]
        public void TheDayListIsCappedToTheLowestDays()
        {
            var long_ = new int[WelcomeLedger.MaxDays + 10];
            for (int i = 0; i < long_.Length; i++) long_[i] = 100 + i;

            var canon = WelcomeLedger.Join(new WelcomeStateDto
            {
                days = new[] { new WelcomeDaysDto { goal = "runs", days = long_ } },
            }, null);

            Assert.AreEqual(WelcomeLedger.MaxDays, canon.days[0].days.Length);
            Assert.AreEqual(100, canon.days[0].days[0]);
            Assert.AreEqual(100 + WelcomeLedger.MaxDays - 1, canon.days[0].days[WelcomeLedger.MaxDays - 1]);
        }

        [Test]
        public void TheJoinIsAUnionAndAJoin()
        {
            var a = new WelcomeStateDto
            {
                days = new[] { new WelcomeDaysDto { goal = "runs", days = new[] { 1, 2 } } },
                claimed = new[] { "w1" },
            };
            var b = new WelcomeStateDto
            {
                days = new[]
                {
                    new WelcomeDaysDto { goal = "runs", days = new[] { 2, 3 } },
                    new WelcomeDaysDto { goal = "streak", days = new[] { 4 } },
                },
                claimed = new[] { "w2" },
            };

            var ab = WelcomeLedger.Join(a, b);
            var ba = WelcomeLedger.Join(b, a);

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, ab.days[0].days, "days by union");
            Assert.AreEqual("streak", ab.days[1].goal);
            CollectionAssert.AreEqual(new[] { "w1", "w2" }, ab.claimed, "claims by union");

            Assert.AreEqual(Show(ab), Show(ba), "commutative");
            Assert.AreEqual(Show(ab), Show(WelcomeLedger.Join(ab, ab)), "idempotent");
            Assert.AreEqual(Show(ab), Show(WelcomeLedger.Join(ab, a)), "absorbing");
        }

        [Test]
        public void ADayOrAClaimIsADifferenceTheSyncSees()
        {
            Publish(Dto());

            var before = new SaveFileDto();
            TaskLedger.WriteInto(before);

            TaskLedger.Note(TaskGoal.Runs);
            var day = new SaveFileDto();
            TaskLedger.WriteInto(day);
            Assert.IsFalse(SaveDelta.Between(before, day).IsEmpty, "a day counted is worth pushing");

            Days(TaskGoal.Runs, 3);
            var ready = new SaveFileDto();
            TaskLedger.WriteInto(ready);
            Assert.IsTrue(WelcomeLedger.TryClaim(Quest("w1"), WelcomeReward.Turret));
            var claimed = new SaveFileDto();
            TaskLedger.WriteInto(claimed);
            Assert.IsFalse(SaveDelta.Between(ready, claimed).IsEmpty, "a claim is worth pushing");

            var same = new SaveFileDto();
            TaskLedger.WriteInto(same);
            Assert.IsTrue(SaveDelta.Between(claimed, same).IsEmpty, "an unchanged record sends nothing");
        }

        [Test]
        public void TwoDevicesClaimingOneQuestAreOneClaim()
        {
            Publish(Dto());
            Days(TaskGoal.Runs, 3);
            Assert.IsTrue(WelcomeLedger.TryClaim(Quest("w1"), WelcomeReward.Turret));
            var mine = new SaveFileDto();
            TaskLedger.WriteInto(mine);
            Wards.WardLedger.WriteInto(mine);

            // The other device did the same thing on its own three days.
            TaskLedger.Reset();
            TaskLedger.LoadFrom(new SaveFileDto());
            WardLedger.LoadFrom(new SaveFileDto());
            _clock.Now += 30 * DailyRules.SecondsPerDay;
            Days(TaskGoal.Runs, 3);
            Assert.IsTrue(WelcomeLedger.TryClaim(Quest("w1"), WelcomeReward.Turret));
            var other = new SaveFileDto();
            TaskLedger.WriteInto(other);
            Wards.WardLedger.WriteInto(other);

            var joined = TaskLedger.Join(mine.tasks, other.tasks);
            CollectionAssert.AreEqual(new[] { "w1" }, joined.welcome.claimed);
            Assert.AreEqual(6, joined.welcome.days[0].days.Length, "both devices' days");
            Assert.AreEqual(1, WardLedger.Join(mine.wardsOwned, other.wardsOwned).Length,
                            "the same one seat row, once");
        }

        // ------------------------------------------------------ the shipped block
        [Test]
        public void TheShippedBlockReadsAgainstTheShippedRoster()
        {
            string text = File.ReadAllText(Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets",
                                                        "Content", "progression.json"));
            var map = TestJson.Object(TestJson.Parse(text));
            Assert.IsTrue(map.ContainsKey("welcome"), "progression.json carries no welcome block");

            var rows = new List<WelcomeQuestDto>();
            foreach (var child in TestJson.Children(TestJson.Object(map["welcome"]), "quests"))
            {
                var row = TestJson.Object(child);
                rows.Add(new WelcomeQuestDto
                {
                    id = (string)row["id"],
                    ward = (string)row["ward"],
                    colour = row.TryGetValue("colour", out var colour) ? colour as string : null,
                    goal = (string)row["goal"],
                    days = System.Convert.ToInt32(row["days"]),
                });
            }

            var problems = new List<string>();
            var table = WelcomeTable.Resolve(new WelcomeDto { quests = rows.ToArray() }, WardCatalog.Default, problems);
            Assert.IsEmpty(problems, string.Join("; ", problems));
            Assert.AreEqual(4, table.Quests.Count, "the owner's four turrets");

            var english = ShippedStrings.Table();
            foreach (var quest in table.Quests)
            {
                Assert.IsTrue(english.ContainsKey(quest.Ward.NameKey), quest.Ward.NameKey);
                Assert.IsTrue(english.ContainsKey(quest.SentenceKey), quest.SentenceKey);
                Assert.IsFalse(quest.Ward.IsStarter);
            }
        }

        // ------------------------------------------------------------ helpers
        void Days(TaskGoal goal, int days)
        {
            for (int i = 0; i < days; i++)
            {
                TaskLedger.Note(goal);
                _clock.Now += DailyRules.SecondsPerDay;
            }
        }

        static WelcomeQuest Quest(string id)
        {
            var quest = ProgressionRules.Table.Welcome.Find(id);
            Assert.IsNotNull(quest, id);
            return quest;
        }

        static WardModel Starter()
        {
            foreach (var model in WardCatalog.Default.Models)
                if (model.IsStarter) return model;
            Assert.Fail("the built-in roster has no starter");
            return null;
        }

        static WelcomeDto Dto()
            => new WelcomeDto
            {
                quests = new[]
                {
                    new WelcomeQuestDto { id = "w1", ward = "leech", colour = "r", goal = "runs", days = 3 },
                    new WelcomeQuestDto { id = "w2", ward = "lighthouse", colour = "g", goal = "task_claims", days = 5 },
                    new WelcomeQuestDto { id = "w3", ward = "pyre", colour = "b", goal = "challenge_wins", days = 7 },
                    new WelcomeQuestDto { id = "w4", ward = "glacier", colour = "y", goal = "streak", days = 10 },
                },
            };

        static WelcomeTable Read(WelcomeDto dto)
        {
            var problems = new List<string>();
            var table = WelcomeTable.Resolve(dto, WardCatalog.Default, problems);
            Assert.IsEmpty(problems, string.Join("; ", problems));
            Assert.AreNotSame(WelcomeTable.Empty, table);
            return table;
        }

        static void Publish(WelcomeDto welcome)
        {
            var problems = new List<string>();
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                maxLevel = 20,
                xpToNext = new[] { 100, 150 },
                tailXpToNext = 200,
                tailXpIncrement = 50,
                welcome = welcome,
            };
            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, problems), string.Join("; ", problems));
            ProgressionRules.Publish(table);
        }

        static string Show(WelcomeStateDto dto)
        {
            var parts = new List<string>();
            foreach (var row in dto.days) parts.Add(row.goal + ":" + string.Join(",", row.days));
            parts.Add("claimed:" + string.Join(",", dto.claimed));
            parts.Add("coined:" + string.Join(",", dto.coined ?? new string[0]));
            return string.Join(" ", parts);
        }
    }
}
