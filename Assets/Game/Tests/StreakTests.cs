using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The daily streak: the arithmetic, the merge, and the one rule that keeps the
    /// ladder payable.
    ///
    /// <para>
    /// Two of these matter more than the rest. The <b>merge</b> is the reason a streak is
    /// stored as two dates rather than as a count — see invariant 11b, and hearts, which
    /// learned it the expensive way — so what is pinned here is that it is a real join:
    /// idempotent, order-independent, and incapable of resurrecting a streak that was
    /// genuinely broken. And the <b>lap</b> is what makes the ladder agree with the board
    /// it has always been drawn on: night eight pays night one, for ever, because a streak
    /// has no end and a ladder does.
    /// </para>
    /// </summary>
    public sealed class StreakTests
    {
        // A day key in the range real players have. The absolute value never matters —
        // every rule here is about differences — but using 0 or 1 would collide with the
        // "never played" sentinel and prove nothing.
        const int Day = 20_500;

        static StreakStateDto State(int start, int last, int collected = 0)
            => new StreakStateDto
            {
                startDay = start,
                lastPlayedDay = last,
                collectedThroughDay = collected,
            };

        static void AssertSame(StreakStateDto a, StreakStateDto b, string what)
        {
            Assert.AreEqual(a.startDay, b.startDay, what + " (startDay)");
            Assert.AreEqual(a.lastPlayedDay, b.lastPlayedDay, what + " (lastPlayedDay)");
            Assert.AreEqual(a.collectedThroughDay, b.collectedThroughDay,
                            what + " (collectedThroughDay)");
        }

        // ------------------------------------------------------------ the length
        [Test]
        public void APlayerWhoHasNeverFinishedARunHasNoStreak()
        {
            Assert.AreEqual(0, DailyStreak.LengthOf(0, 0, Day));
        }

        [Test]
        public void OneDayIsAStreakOfOne()
        {
            Assert.AreEqual(1, DailyStreak.LengthOf(Day, Day, Day));
        }

        [Test]
        public void TheLengthIsTheSpanInclusive()
        {
            Assert.AreEqual(6, DailyStreak.LengthOf(Day - 5, Day, Day));
        }

        /// <summary>
        /// The flame must not go out at midnight in front of somebody who is mid-session.
        /// A streak survives the whole of the following day and breaks only after it.
        /// </summary>
        [Test]
        public void YesterdayStillCounts()
        {
            Assert.AreEqual(6, DailyStreak.LengthOf(Day - 5, Day, Day + 1),
                            "a streak fed yesterday is still held today");
            Assert.AreEqual(0, DailyStreak.LengthOf(Day - 5, Day, Day + 2),
                            "a whole day passed with nothing finished, so it is gone");
        }

        /// <summary>
        /// A merged file, or one edited by hand, can name a start after the last day
        /// played. The length is a subtraction, and a negative one would draw a streak
        /// counting backwards.
        /// </summary>
        [Test]
        public void AnImpossiblePairReadsAsNoStreak()
        {
            Assert.AreEqual(0, DailyStreak.LengthOf(Day + 3, Day, Day));
        }

        // ----------------------------------------------------------- advancing
        [Test]
        public void AFirstEverRunStartsAStreakOfOne()
        {
            DailyStreak.Advance(0, 0, Day, out int start, out int last);

            Assert.AreEqual(Day, start);
            Assert.AreEqual(Day, last);
            Assert.AreEqual(1, DailyStreak.LengthOf(start, last, Day));
        }

        [Test]
        public void ARunTheDayAfterContinuesTheStreak()
        {
            DailyStreak.Advance(Day - 5, Day - 1, Day, out int start, out int last);

            Assert.AreEqual(Day - 5, start, "the start does not move while the run continues");
            Assert.AreEqual(Day, last);
            Assert.AreEqual(6, DailyStreak.LengthOf(start, last, Day));
        }

        [Test]
        public void AGapStartsOver()
        {
            DailyStreak.Advance(Day - 5, Day - 2, Day, out int start, out int last);

            Assert.AreEqual(Day, start, "a missed day is a new streak, not a continued one");
            Assert.AreEqual(1, DailyStreak.LengthOf(start, last, Day));
        }

        /// <summary>
        /// The second run of an evening must change nothing. It is also what makes
        /// <c>Record</c> safe to call from both the win and the defeat path without either
        /// having to know whether the other already did.
        /// </summary>
        [Test]
        public void ASecondRunTheSameDayChangesNothing()
        {
            DailyStreak.Advance(Day - 5, Day, Day, out int start, out int last);

            Assert.AreEqual(Day - 5, start);
            Assert.AreEqual(Day, last);
        }

        /// <summary>
        /// Both stored fields only ever rise. That is the property the merge rests on —
        /// see invariant 11b — so it is checked against advancing rather than assumed.
        ///
        /// <para>
        /// Only over <em>coherent</em> pairs, and the restriction is a real precondition
        /// rather than a convenience: a start later than the last day played is not a state
        /// the game can reach, and <c>LoadFrom</c> repairs one on read before anything sees
        /// it. Feeding an incoherent pair in here would be testing a case that has already
        /// been made impossible one layer down.
        /// </para>
        /// </summary>
        [Test]
        public void AdvancingNeverMovesEitherDateBackwards()
        {
            int[] offsets = { -9, -7, -2, -1, 0, 4 };

            // Both zero is the other coherent state: never played.
            DailyStreak.Advance(0, 0, Day, out int fs, out int fl);
            Assert.GreaterOrEqual(fs, 0);
            Assert.GreaterOrEqual(fl, 0);

            foreach (int s in offsets)
                foreach (int l in offsets)
                {
                    if (l < s) continue;                    // incoherent; see the summary
                    int start = Day + s, last = Day + l;

                    DailyStreak.Advance(start, last, Day, out int ns, out int nl);
                    Assert.GreaterOrEqual(ns, start, $"start fell from {start} at ({s},{l})");
                    Assert.GreaterOrEqual(nl, last, $"last fell from {last} at ({s},{l})");
                }
        }

        // --------------------------------------------------------------- merge
        [Test]
        public void TheMergeKeepsTheLongerRunOfDays()
        {
            var merged = DailyStreak.Join(State(Day - 5, Day - 1), State(Day - 5, Day));

            AssertSame(State(Day - 5, Day), merged, "the device that played today knows more");
        }

        /// <summary>
        /// The case the whole representation exists for. One device holds a six-day
        /// streak; the other broke it and started again today. Taking the later start is
        /// what stops the stale device resurrecting a streak the player really did lose —
        /// and streak rewards escalate, so resurrecting one is not a cosmetic mistake.
        /// </summary>
        [Test]
        public void ABrokenStreakIsNeverResurrectedByAStaleDevice()
        {
            var merged = DailyStreak.Join(State(Day - 5, Day), State(Day + 2, Day + 2));

            Assert.AreEqual(1, DailyStreak.LengthOf(merged.startDay, merged.lastPlayedDay, Day + 2));
        }

        [Test]
        public void TheMergeIsOrderIndependent()
        {
            var a = State(Day - 5, Day - 1, Day - 3);
            var b = State(Day - 3, Day, Day - 2);

            AssertSame(DailyStreak.Join(a, b), DailyStreak.Join(b, a), "join is commutative");
        }

        [Test]
        public void TheMergeIsIdempotent()
        {
            var a = State(Day - 5, Day - 1, Day - 3);
            var b = State(Day - 3, Day, Day - 2);

            var once = DailyStreak.Join(a, b);
            var twice = DailyStreak.Join(once, b);

            AssertSame(once, twice, "merging the same device twice must change nothing");
        }

        [Test]
        public void TheMergeIsAssociative()
        {
            var a = State(Day - 8, Day - 4, Day - 6);
            var b = State(Day - 3, Day - 1, Day - 3);
            var c = State(Day - 3, Day, Day - 2);

            AssertSame(DailyStreak.Join(DailyStreak.Join(a, b), c),
                       DailyStreak.Join(a, DailyStreak.Join(b, c)),
                       "join is associative");
        }

        /// <summary>
        /// The collected floor merges the same way the dates do, and in the same direction:
        /// the device that has paid more out wins. It can cost a player a night neither
        /// device had collected, which is the safe error — the other one pays a night
        /// twice, and two devices granting the same hearts is the failure the whole
        /// representation exists to make impossible.
        /// </summary>
        [Test]
        public void TheMergeKeepsTheHigherCollectedFloor()
        {
            var merged = DailyStreak.Join(State(Day - 5, Day, Day - 2), State(Day - 5, Day, Day - 1));

            Assert.AreEqual(Day - 1, merged.collectedThroughDay,
                            "the device that had already paid a night out must win");
        }

        [Test]
        public void AnAbsentSectionIsNotAnOpinion()
        {
            var held = State(Day - 5, Day);

            AssertSame(held, DailyStreak.Join(held, null), "a null side must not erase a streak");
            AssertSame(held, DailyStreak.Join(null, held), "nor from the other direction");
        }

        // ---------------------------------------------------------- the ladder
        /// <summary>
        /// The rule that makes the ladder a lap rather than a staircase, and the one the
        /// board has always assumed. A player on night forty is the most engaged player the
        /// game has, and the previous behaviour — repeating the last rung for ever — meant
        /// a tile labelled "night 8" paid night 7's reward, which is the board and the
        /// table telling that player two different things.
        /// </summary>
        [Test]
        public void PastTheEndOfTheLadderTheLapBeginsAgain()
        {
            var table = StreakTable.Default;

            for (int night = 1; night <= table.Length; night++)
            {
                var first = table.Rung(night);

                for (int lap = 1; lap <= 4; lap++)
                {
                    var later = table.Rung(night + lap * table.Length);

                    Assert.AreEqual(first.Kind, later.Kind, $"night {night} on lap {lap + 1}");
                    Assert.AreEqual(first.Amount, later.Amount, $"night {night} on lap {lap + 1}");
                }
            }
        }

        [Test]
        public void ANightNamesItsPlaceOnTheLap()
        {
            var table = StreakTable.Default;

            Assert.AreEqual(1, table.NightInCycle(1));
            Assert.AreEqual(table.Length, table.NightInCycle(table.Length));
            Assert.AreEqual(1, table.NightInCycle(table.Length + 1));
            Assert.AreEqual(2, table.NightInCycle(table.Length + 2));
            Assert.AreEqual(0, table.NightInCycle(0), "there is no night zero");
        }

        /// <summary>
        /// Currency is allowed on the ladder now, and this is the test that says so
        /// deliberately rather than by omission. What makes it safe is not on this side at
        /// all — a night is claimed under an id derived from its calendar day, and the
        /// server grants from its own copy of the ladder against a floor no client can
        /// write. See <c>StreakTable</c> and <c>functions/src/streak.ts</c>.
        /// </summary>
        [Test]
        public void ACurrencyRungIsAdopted()
        {
            foreach (string currency in new[] { ChestDropKinds.Credits, ChestDropKinds.Gems })
            {
                var problems = new List<string>();
                var table = StreakTable.Resolve(new StreakDto
                {
                    rungs = new[]
                    {
                        new StreakRungDto { kind = currency, amount = 25 },
                        new StreakRungDto { kind = ChestDropKinds.Hearts, amount = 1 },
                    },
                }, problems);

                Assert.AreNotSame(StreakTable.Default, table,
                                  $"a ladder paying {currency} must be adopted");
                Assert.AreEqual(ChestDropKinds.Parse(currency), table.Rung(1).Kind);
                Assert.AreEqual(25, table.Rung(1).Amount);
                Assert.IsTrue(table.Rung(1).IsCurrency);
                Assert.IsEmpty(problems, "a legitimate currency rung is not a problem");
            }
        }

        /// <summary>
        /// The ceilings are per kind, and they are shared with the server. A single
        /// seventy-two would have clamped a 150-credit rung down to a rounding error and
        /// the panel would have printed the clamped figure without anybody noticing.
        /// </summary>
        [Test]
        public void EachKindIsClampedToItsOwnCeiling()
        {
            Assert.AreEqual(StreakRules.MaxCreditsPerRung, StreakRules.MaxFor(ChestDropKind.Credits));
            Assert.AreEqual(StreakRules.MaxGemsPerRung, StreakRules.MaxFor(ChestDropKind.Gems));
            Assert.AreEqual(StreakRules.MaxRungAmount, StreakRules.MaxFor(ChestDropKind.Hearts));
            Assert.AreEqual(StreakRules.MaxRungAmount, StreakRules.MaxFor(ChestDropKind.HeartBoost));

            Assert.AreEqual(150, new StreakRung(ChestDropKind.Credits, 150).Amount,
                            "the shipped ladder's first night must survive the clamp");

            Assert.AreEqual(StreakRules.MaxCreditsPerRung,
                            new StreakRung(ChestDropKind.Credits, 1_000_000).Amount);
            Assert.AreEqual(StreakRules.MaxGemsPerRung,
                            new StreakRung(ChestDropKind.Gems, 1_000_000).Amount);
        }

        [Test]
        public void AGoodLadderIsAdopted()
        {
            var problems = new List<string>();
            var table = StreakTable.Resolve(new StreakDto
            {
                rungs = new[]
                {
                    new StreakRungDto(),
                    new StreakRungDto { kind = ChestDropKinds.Hearts, amount = 2 },
                    new StreakRungDto { kind = ChestDropKinds.HeartBoost, amount = 24 },
                },
            }, problems);

            Assert.AreNotSame(StreakTable.Default, table);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems));
            Assert.AreEqual(3, table.Length);
            Assert.AreEqual(ChestDropKind.HeartBoost, table.Rung(3).Kind);
            Assert.AreEqual(24, table.Rung(3).Amount);
        }

        /// <summary>
        /// Position is the day, so a rung cannot be skipped the way a bad ad placement is:
        /// dropping one renumbers every day above it and silently changes what the player
        /// is owed. The whole block is refused instead.
        /// </summary>
        [Test]
        public void OneBadRungRefusesTheWholeLadder()
        {
            var problems = new List<string>();
            var table = StreakTable.Resolve(new StreakDto
            {
                rungs = new[]
                {
                    new StreakRungDto(),
                    new StreakRungDto { kind = "wisdom", amount = 3 },
                    new StreakRungDto { kind = ChestDropKinds.Hearts, amount = 2 },
                },
            }, problems);

            Assert.AreSame(StreakTable.Default, table);
        }

        [Test]
        public void AnAbsentBlockIsNotAnError()
        {
            var problems = new List<string>();

            Assert.AreSame(StreakTable.Default, StreakTable.Resolve(null, problems));
            Assert.AreEqual(0, problems.Count);
        }

        [Test]
        public void ARungCannotPayMoreThanTheCeiling()
        {
            var problems = new List<string>();
            var table = StreakTable.Resolve(new StreakDto
            {
                rungs = new[]
                {
                    new StreakRungDto { kind = ChestDropKinds.HeartBoost, amount = 100_000 },
                },
            }, problems);

            Assert.AreEqual(StreakRules.MaxRungAmount, table.Rung(1).Amount);
            Assert.IsTrue(problems.Count > 0, "clamping silently is how a typo ships");
        }

        // ------------------------------------------------------------ collecting
        //
        // Rewards are handed over when the player taps a night rather than applied when
        // the run ends, so a third date records how far that has got. The risk it adds is
        // exactly one: paying a night twice. Everything below is aimed at that — the
        // migration off the build that paid automatically, the floor a lapsed streak
        // leaves behind, and the sweep that takes earlier nights with a later one.

        static readonly StreakTable Ladder = StreakTable.Default;

        /// <summary>A run of <paramref name="days"/> days ending today, nothing collected.</summary>
        static int StartOf(int days) => Day - days + 1;

        [Test]
        public void ANightIsNotCollectableBeforeItIsEarned()
        {
            int start = StartOf(3);

            Assert.IsTrue(DailyStreak.CollectableAt(start, Day, start - 1, Day, 2, Ladder),
                          "night two has been reached");
            Assert.IsFalse(DailyStreak.CollectableAt(start, Day, start - 1, Day, 4, Ladder),
                           "night four has not happened yet");
        }

        /// <summary>
        /// A tile that asks to be tapped for nothing is worse than one that does not ask at
        /// all, so a blank night is swept silently when a later one is taken rather than
        /// sitting on the board glowing.
        ///
        /// Run against an authored ladder rather than the shipped one, because every night
        /// of the shipped ladder pays — which is a tuning decision that could change back
        /// tomorrow, and this rule must not quietly stop being tested when it does.
        /// </summary>
        [Test]
        public void ANightThatPaysNothingIsNeverCollectable()
        {
            var problems = new List<string>();
            var sparse = StreakTable.Resolve(new StreakDto
            {
                rungs = new[]
                {
                    new StreakRungDto(),                                              // pays nothing
                    new StreakRungDto { kind = ChestDropKinds.Hearts, amount = 1 },
                },
            }, problems);

            int start = StartOf(2);

            Assert.IsFalse(sparse.Rung(1).IsValid, "precondition: the first night pays nothing");
            Assert.IsFalse(DailyStreak.CollectableAt(start, Day, start - 1, Day, 1, sparse));
            Assert.IsTrue(DailyStreak.CollectableAt(start, Day, start - 1, Day, 2, sparse),
                          "the night beside it is unaffected");
        }

        [Test]
        public void ACollectedNightDoesNotComeBack()
        {
            int start = StartOf(3);

            Assert.IsTrue(DailyStreak.CollectableAt(start, Day, start - 1, Day, 2, Ladder));
            Assert.IsFalse(DailyStreak.CollectableAt(start, Day, start + 1, Day, 2, Ladder),
                           "the floor has passed night two");
        }

        /// <summary>
        /// The floor is why taking a later night takes the earlier ones with it. That is
        /// the only reading that cannot lose a reward: a per-night flag would have to be
        /// cleared when a streak breaks, which is not monotonic and so cannot be joined.
        /// </summary>
        [Test]
        public void CollectingANightTakesEveryEarlierOneWithIt()
        {
            int start = StartOf(5);
            int floorAfterTakingNightFour = DailyStreak.DayOfRung(start, 4);

            for (int rung = 1; rung <= 4; rung++)
                Assert.IsTrue(DailyStreak.CollectedAt(start, floorAfterTakingNightFour, rung),
                              $"night {rung} is behind the floor");

            Assert.IsFalse(DailyStreak.CollectedAt(start, floorAfterTakingNightFour, 5),
                           "night five is still waiting");
        }

        [Test]
        public void PendingCountsThePayingNightsStillWaiting()
        {
            int start = StartOf(5);

            // Nothing collected, and every night of the shipped ladder pays.
            Assert.AreEqual(5, DailyStreak.PendingAt(start, Day, start - 1, Day, Ladder));

            // Collected through night three: four and five are left.
            Assert.AreEqual(2, DailyStreak.PendingAt(start, Day, DailyStreak.DayOfRung(start, 3),
                                                     Day, Ladder));

            Assert.AreEqual(0, DailyStreak.PendingAt(start, Day, Day, Day, Ladder));
        }

        [Test]
        public void ABrokenStreakHasNothingWaiting()
        {
            int start = Day - 9;

            Assert.AreEqual(0, DailyStreak.PendingAt(start, Day - 5, start - 1, Day, Ladder),
                            "a streak that lapsed five days ago offers nothing");
        }

        /// <summary>
        /// The migration, and the reason the sentinel is zero rather than a flag. Before
        /// v10 a rung was applied the moment a run ended, so a file arriving with a live
        /// streak and no floor has already been paid for every night it holds. Reading
        /// that as "nothing collected" would light the whole board up and pay it twice.
        /// </summary>
        [Test]
        public void AFileFromBeforeManualCollectionPaysNothingASecondTime()
        {
            int start = StartOf(6);
            int repaired = DailyStreak.RepairCollected(0, Day);

            Assert.AreEqual(Day, repaired);
            Assert.AreEqual(0, DailyStreak.PendingAt(start, Day, repaired, Day, Ladder),
                            "every night it had earned it had also been paid");
        }

        [Test]
        public void AFloorPastTheLastDayPlayedIsPulledBack()
        {
            Assert.AreEqual(Day, DailyStreak.RepairCollected(Day + 40, Day),
                            "an edited file cannot claim to have collected the future");
        }

        [Test]
        public void APlayerWhoHasNeverPlayedHasNoFloorToRepair()
        {
            Assert.AreEqual(0, DailyStreak.RepairCollected(0, 0));
        }

        /// <summary>
        /// Starting a new run seeds the floor to the day before it. That is what stops a
        /// streak the player let lapse from handing over its uncollected nights days
        /// later, and — because a day key is a five-figure number — it is also what keeps
        /// a live file's floor away from the zero that means "pre-v10".
        /// </summary>
        [Test]
        public void ANewRunSeedsTheFloorSoLapsedNightsAreNotOffered()
        {
            int oldStart = Day - 20;
            int seeded = DailyStreak.SeedCollected(oldStart, Day, continues: false);

            Assert.AreEqual(Day - 1, seeded);
            Assert.AreEqual(0, DailyStreak.PendingAt(oldStart, Day - 10, seeded, Day, Ladder),
                            "the lapsed run's nights are behind the new floor");
        }

        [Test]
        public void AContinuedRunLeavesTheFloorWhereItIs()
        {
            int floor = Day - 4;

            Assert.AreEqual(floor, DailyStreak.SeedCollected(floor, Day, continues: true),
                            "a night held on to must not lose the rungs waiting on it");
        }

        [Test]
        public void TheSeededFloorNeverFalls()
        {
            for (int floor = Day - 30; floor <= Day + 5; floor++)
                foreach (bool continues in new[] { true, false })
                    Assert.GreaterOrEqual(DailyStreak.SeedCollected(floor, Day, continues), floor,
                                          $"the floor fell from {floor} (continues: {continues})");
        }

        /// <summary>
        /// The whole first run, night by night: nothing is ever pre-collected, and no
        /// night is ever offered twice. Exhaustive rather than sampled, because the two
        /// off-by-one errors available here — seeding to today instead of yesterday, and
        /// counting the floor as inclusive or not — both show up on exactly one night.
        /// </summary>
        [Test]
        public void EveryNightOfAFirstStreakIsOfferedExactlyOnce()
        {
            const int Length = 7;

            int start = 0, last = 0, floor = 0;
            int offered = 0, paying = 0;

            for (int n = 0; n < Length; n++)
            {
                int today = Day + n;

                DailyStreak.Advance(start, last, today, out int nextStart, out int nextLast);
                bool continues = nextStart == start;
                start = nextStart;
                last = nextLast;
                floor = DailyStreak.SeedCollected(floor, today, continues);

                int rung = DailyStreak.LengthOf(start, last, today);
                if (Ladder.Rung(rung).IsValid) paying++;

                Assert.AreEqual(paying == 0 ? 0 : 1,
                                DailyStreak.PendingAt(start, last, floor, today, Ladder),
                                $"exactly the night just earned is waiting on day {n + 1}");

                if (!DailyStreak.CollectableAt(start, last, floor, today, rung, Ladder)) continue;

                offered++;
                floor = DailyStreak.DayOfRung(start, rung);   // the player collects it
            }

            // Asked of the ladder rather than written down, so retuning which nights pay
            // cannot quietly turn this into a test of nothing.
            Assert.AreEqual(paying, offered, "every paying night was offered exactly once");
            Assert.AreEqual(0, DailyStreak.PendingAt(start, last, floor, Day + Length - 1, Ladder));
        }

        // ------------------------------------------------------------- syncing
        //
        // The dates only started travelling when the ladder started paying money. Before
        // that a streak lived and died on one handset, which is why a player's flame
        // restarted on their tablet — a real bug that hid behind a merge nothing fed.

        static SaveFileDto SaveWith(StreakStateDto streak)
            => new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                daily = new DailyStateDto(),
                ads = new AdStateDto(),
                streak = streak,
                progression = ProgressionStateDto.Unwritten(),
                cloud = new CloudStateDto(),
                levels = new LevelRecordDto[0],
            };

        /// <summary>
        /// A night earned since the last sync has to make the push non-empty, or the delta
        /// says "nothing to send" and the dates never leave the device — which is the state
        /// the feature was already in, silently, for as long as it has existed.
        /// </summary>
        [Test]
        public void AStreakThatMovedIsAChangeWorthPushing()
        {
            var before = SaveWith(new StreakStateDto
            {
                startDay = Day - 4, lastPlayedDay = Day - 1, collectedThroughDay = Day - 2,
            });

            foreach (var after in new[]
            {
                new StreakStateDto { startDay = Day, lastPlayedDay = Day - 1, collectedThroughDay = Day - 2 },
                new StreakStateDto { startDay = Day - 4, lastPlayedDay = Day, collectedThroughDay = Day - 2 },
                new StreakStateDto { startDay = Day - 4, lastPlayedDay = Day - 1, collectedThroughDay = Day - 1 },
            })
            {
                Assert.IsFalse(SaveDelta.Between(before, SaveWith(after)).IsEmpty,
                               "a streak date that moved must reach the other device");
            }
        }

        [Test]
        public void AnUnchangedStreakSendsNothing()
        {
            var streak = new StreakStateDto
            {
                startDay = Day - 4, lastPlayedDay = Day, collectedThroughDay = Day - 1,
            };

            Assert.IsTrue(SaveDelta.Between(SaveWith(streak), SaveWith(streak)).IsEmpty,
                          "an unchanged save must not burn a document write");
        }

        // ---------------------------------------------------------------- laps
        //
        // A streak has no end but a ladder does, so the board shows one lap of it at a
        // time. Everything here guards the seam between the two: a night must never fall
        // between laps, and the run past the end of the ladder must keep paying.

        [Test]
        public void ALapCoversExactlyTheLadderAndThenStartsAgain()
        {
            const int Rungs = 7;

            Assert.AreEqual(1, DailyStreak.CycleOf(1, Rungs));
            Assert.AreEqual(1, DailyStreak.CycleOf(7, Rungs));
            Assert.AreEqual(2, DailyStreak.CycleOf(8, Rungs));
            Assert.AreEqual(2, DailyStreak.CycleOf(14, Rungs));
            Assert.AreEqual(3, DailyStreak.CycleOf(15, Rungs));

            Assert.AreEqual(1, DailyStreak.CycleStart(7, Rungs));
            Assert.AreEqual(8, DailyStreak.CycleStart(8, Rungs));
            Assert.AreEqual(8, DailyStreak.CycleStart(14, Rungs));
        }

        /// <summary>
        /// The bug the lap exists to prevent. A board pinned to the lap the streak is
        /// *currently* on drops night seven off the bottom of the screen the moment night
        /// eight arrives — the reward is still owed, still counted by the badge, and there
        /// is no longer a tile to tap. Paging to the oldest uncollected night instead means
        /// nothing can be stranded.
        /// </summary>
        [Test]
        public void AnUncollectedNightIsNeverLeftBehindByTheNextLap()
        {
            const int Rungs = 7;
            int start = StartOf(8);                       // an eight-night streak ending today
            int floor = DailyStreak.DayOfRung(start, 6);  // nights one to six taken

            int first = DailyStreak.FirstPendingAt(start, Day, floor, Day, Ladder);
            Assert.AreEqual(7, first, "night seven is the oldest still waiting");

            int board = DailyStreak.CycleStart(first, Rungs);
            Assert.AreEqual(1, board, "the board must page back to the lap holding it");
            Assert.Less(first - board, Rungs, "and night seven must fall inside that lap");
        }

        [Test]
        public void AStreakPastTheLadderKeepsPaying()
        {
            int start = StartOf(10);
            int floor = DailyStreak.DayOfRung(start, 7);   // the whole first lap taken

            Assert.AreEqual(3, DailyStreak.PendingAt(start, Day, floor, Day, Ladder),
                            "nights eight, nine and ten are owed");

            int board = DailyStreak.CycleStart(
                DailyStreak.FirstPendingAt(start, Day, floor, Day, Ladder), 7);

            Assert.AreEqual(8, board, "the board has moved on to the second lap");
        }

        /// <summary>
        /// Three weeks, collected as a player actually would, asserting the property the
        /// whole feature rests on: from the second night on there is always exactly one
        /// night waiting, it always pays something, and it is always on the board that is
        /// being drawn. Twenty-one days because the failure this catches — a night falling
        /// between laps — only exists at the seams, and there are two of them.
        /// </summary>
        [Test]
        public void EveryNightOfThreeWeeksIsOnTheBoardAndPays()
        {
            const int Weeks = 3;
            int rungs = Ladder.Length;
            int nights = rungs * Weeks;

            int start = 0, last = 0, floor = 0;

            for (int n = 0; n < nights; n++)
            {
                int today = Day + n;

                DailyStreak.Advance(start, last, today, out int nextStart, out int nextLast);
                bool continues = nextStart == start;
                start = nextStart;
                last = nextLast;
                floor = DailyStreak.SeedCollected(floor, today, continues);

                int night = DailyStreak.LengthOf(start, last, today);
                Assert.AreEqual(n + 1, night, "the streak counts on without resetting");

                int pending = DailyStreak.FirstPendingAt(start, last, floor, today, Ladder);

                // A night the ladder leaves blank simply has nothing waiting on it. The
                // shipped ladder pays on every night, so this does not fire today — but
                // asking rather than hardcoding is what stops the walk silently skipping
                // nights if a future lap leaves one empty.
                if (!Ladder.Rung(night).IsValid)
                {
                    Assert.AreEqual(0, pending, $"night {night} pays nothing, so nothing waits");
                    continue;
                }

                Assert.AreEqual(night, pending, $"night {night} should be the one waiting");
                Assert.IsTrue(Ladder.Rung(night).IsValid, $"night {night} must pay something");

                int board = DailyStreak.CycleStart(pending, rungs);
                Assert.GreaterOrEqual(pending, board, $"night {night} is above the board");
                Assert.Less(pending - board, rungs, $"night {night} is below the board");

                floor = DailyStreak.DayOfRung(start, night);   // the player collects it
                Assert.AreEqual(0, DailyStreak.PendingAt(start, last, floor, today, Ladder),
                                $"nothing should be left over on night {night}");
            }

            Assert.AreEqual(nights, DailyStreak.LengthOf(start, last, Day + nights - 1),
                            "twenty-one nights, and the count never restarted");
        }
    }
}
