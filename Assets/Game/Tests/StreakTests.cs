using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using GlimmerGrove.Tasks;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The daily streak: the arithmetic, the merge, the shield, and the one rule that keeps
    /// the ladder payable.
    ///
    /// <para>
    /// Three of these matter more than the rest. The <b>merge</b> is the reason a streak is
    /// stored as dates rather than as a count — see invariant 11b, and hearts, which learned
    /// it the expensive way — so what is pinned here is that it is a real join: idempotent,
    /// order-independent, and incapable of resurrecting a streak that was genuinely broken.
    /// The <b>lap</b> is what makes the ladder agree with the board it has always been drawn
    /// on: night eight pays night one, for ever, because a streak has no end and a ladder
    /// does. And the <b>shield</b> is the newest and the easiest to get wrong in a way nobody
    /// would notice — it must keep a streak across days nobody played and buy <em>no</em>
    /// nights, which is the difference between a retention feature and a currency printer.
    /// </para>
    /// </summary>
    public sealed class StreakTests
    {
        // A day key in the range real players have. The absolute value never matters —
        // every rule here is about differences — but using 0 or 1 would collide with the
        // "never played" sentinel and prove nothing.
        const int Day = 20_500;

        /// <summary>The shipped window. Written here so a content retune cannot move a case.</summary>
        const int Days = StreakRules.DefaultShieldDays;

        // The pure rules take the shield as two arguments so they can be exercised without a
        // clock, a save or a content file. Every case below that is not *about* the shield
        // passes no shield at all, which is what keeps the unprotected behaviour pinned in
        // its own right rather than as a special case of the protected one.
        static int Len(int start, int last, int today, int shieldFrom = 0)
            => DailyStreak.LengthOf(start, last, today, shieldFrom, Days);

        static void Adv(int start, int last, int today, out int nextStart, out int nextLast)
            => DailyStreak.Advance(start, last, today, 0, Days, out nextStart, out nextLast, out _);

        static void Adv(int start, int last, int today, int shieldFrom,
                        out int nextStart, out int nextLast, out int forgiven)
            => DailyStreak.Advance(start, last, today, shieldFrom, Days,
                                   out nextStart, out nextLast, out forgiven);

        static bool Can(int start, int last, int floor, int today, int rung, StreakTable ladder,
                        int shieldFrom = 0)
            => DailyStreak.CollectableAt(start, last, floor, today, rung, ladder, shieldFrom, Days);

        static bool Waiting(int start, int last, int floor, int today, int rung, StreakTable ladder,
                            int shieldFrom = 0)
            => DailyStreak.WaitingAt(start, last, floor, today, rung, ladder, shieldFrom, Days);

        static int Pend(int start, int last, int floor, int today, StreakTable ladder,
                        int shieldFrom = 0)
            => DailyStreak.PendingAt(start, last, floor, today, ladder, shieldFrom, Days);

        static int First(int start, int last, int floor, int today, StreakTable ladder,
                         int shieldFrom = 0)
            => DailyStreak.FirstPendingAt(start, last, floor, today, ladder, shieldFrom, Days);

        /// <summary>
        /// The reader, given the tiers the shipped content resolves against.
        ///
        /// A chest rung names an id out of the tasks block, so the reader takes a lookup —
        /// which is what lets a ladder be proved against a synthetic ladder of tiers as well
        /// as against the shipped one.
        /// </summary>
        static StreakTable Resolve(StreakDto dto, List<string> problems)
            => StreakTable.Resolve(dto, TaskTable.Default.Tier, problems);

        static StreakStateDto State(int start, int last, int collected = 0, int shield = 0)
            => new StreakStateDto
            {
                startDay = start,
                lastPlayedDay = last,
                collectedThroughDay = collected,
                shieldFromDay = shield,
            };

        static void AssertSame(StreakStateDto a, StreakStateDto b, string what)
        {
            Assert.AreEqual(a.startDay, b.startDay, what + " (startDay)");
            Assert.AreEqual(a.lastPlayedDay, b.lastPlayedDay, what + " (lastPlayedDay)");
            Assert.AreEqual(a.collectedThroughDay, b.collectedThroughDay,
                            what + " (collectedThroughDay)");
            Assert.AreEqual(a.shieldFromDay, b.shieldFromDay, what + " (shieldFromDay)");
        }

        // ------------------------------------------------------------ the length
        [Test]
        public void APlayerWhoHasNeverFinishedARunHasNoStreak()
        {
            Assert.AreEqual(0, Len(0, 0, Day));
        }

        [Test]
        public void OneDayIsAStreakOfOne()
        {
            Assert.AreEqual(1, Len(Day, Day, Day));
        }

        [Test]
        public void TheLengthIsTheSpanInclusive()
        {
            Assert.AreEqual(6, Len(Day - 5, Day, Day));
        }

        /// <summary>
        /// The flame must not go out at midnight in front of somebody who is mid-session.
        /// A streak survives the whole of the following day and breaks only after it.
        /// </summary>
        [Test]
        public void YesterdayStillCounts()
        {
            Assert.AreEqual(6, Len(Day - 5, Day, Day + 1),
                            "a streak fed yesterday is still held today");
            Assert.AreEqual(0, Len(Day - 5, Day, Day + 2),
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
            Assert.AreEqual(0, Len(Day + 3, Day, Day));
        }

        // ----------------------------------------------------------- advancing
        [Test]
        public void AFirstEverRunStartsAStreakOfOne()
        {
            Adv(0, 0, Day, out int start, out int last);

            Assert.AreEqual(Day, start);
            Assert.AreEqual(Day, last);
            Assert.AreEqual(1, Len(start, last, Day));
        }

        [Test]
        public void ARunTheDayAfterContinuesTheStreak()
        {
            Adv(Day - 5, Day - 1, Day, out int start, out int last);

            Assert.AreEqual(Day - 5, start, "the start does not move while the run continues");
            Assert.AreEqual(Day, last);
            Assert.AreEqual(6, Len(start, last, Day));
        }

        [Test]
        public void AGapStartsOver()
        {
            Adv(Day - 5, Day - 2, Day, out int start, out int last);

            Assert.AreEqual(Day, start, "a missed day is a new streak, not a continued one");
            Assert.AreEqual(1, Len(start, last, Day));
        }

        /// <summary>
        /// The second run of an evening must change nothing. It is also what makes
        /// <c>Record</c> safe to call from both the win and the defeat path without either
        /// having to know whether the other already did.
        /// </summary>
        [Test]
        public void ASecondRunTheSameDayChangesNothing()
        {
            Adv(Day - 5, Day, Day, out int start, out int last);

            Assert.AreEqual(Day - 5, start);
            Assert.AreEqual(Day, last);
        }

        /// <summary>
        /// Every stored field only ever rises. That is the property the merge rests on —
        /// see invariant 11b — so it is checked against advancing rather than assumed.
        ///
        /// <para>
        /// Only over <em>coherent</em> pairs, and the restriction is a real precondition
        /// rather than a convenience: a start later than the last day played is not a state
        /// the game can reach, and <c>LoadFrom</c> repairs one on read before anything sees
        /// it. Feeding an incoherent pair in here would be testing a case that has already
        /// been made impossible one layer down.
        /// </para>
        /// <para>
        /// Swept over shields as well, because the shield is the one thing that can move the
        /// <em>start</em> of a run that is continuing — and a start that moved the wrong way
        /// would be a merge that silently lengthens a streak on every sync.
        /// </para>
        /// </summary>
        [Test]
        public void AdvancingNeverMovesEitherDateBackwards()
        {
            int[] offsets = { -9, -7, -2, -1, 0, 4 };
            int[] shields = { 0, Day - 8, Day - 3, Day, Day + 2 };

            // Both zero is the other coherent state: never played.
            Adv(0, 0, Day, out int fs, out int fl);
            Assert.GreaterOrEqual(fs, 0);
            Assert.GreaterOrEqual(fl, 0);

            foreach (int s in offsets)
                foreach (int l in offsets)
                {
                    if (l < s) continue;                    // incoherent; see the summary
                    int start = Day + s, last = Day + l;

                    foreach (int shield in shields)
                    {
                        Adv(start, last, Day, shield, out int ns, out int nl, out _);
                        Assert.GreaterOrEqual(ns, start, $"start fell from {start} at ({s},{l},{shield})");
                        Assert.GreaterOrEqual(nl, last, $"last fell from {last} at ({s},{l},{shield})");
                    }
                }
        }

        // --------------------------------------------------------------- shield
        //
        // The shield is a window of days the streak survives without being played. Its whole
        // promise has an exact shape — a fixed span from the day it was bought, which playing
        // does not extend — and the way it goes wrong is not a crash: it is a streak that
        // quietly credits nights nobody played, which would sell a week of chests for a
        // hundred and twenty gems.

        [Test]
        public void AShieldCoversTheDayItWasBoughtAndNoMoreThanItsSpan()
        {
            Assert.IsTrue(DailyStreak.ShieldCovers(Day, Day, Days), "the day it was bought");
            Assert.IsTrue(DailyStreak.ShieldCovers(Day, Day + Days - 1, Days), "the last day");
            Assert.IsFalse(DailyStreak.ShieldCovers(Day, Day + Days, Days), "one day past it");
            Assert.IsFalse(DailyStreak.ShieldCovers(Day, Day - 1, Days), "the day before it");
            Assert.IsFalse(DailyStreak.ShieldCovers(0, Day, Days), "no shield covers nothing");
        }

        [Test]
        public void AProtectedStreakSurvivesTheDaysNobodyPlayed()
        {
            int start = Day - 19, last = Day;               // night twenty, played today
            int shield = Day + 1;                           // bought and the player vanishes

            for (int away = 1; away <= Days; away++)
                Assert.AreEqual(20, Len(start, last, Day + away, shield),
                                $"still night twenty {away} day(s) later");

            Assert.AreEqual(0, Len(start, last, Day + Days + 2, shield),
                            "and gone once the window has passed");
        }

        /// <summary>
        /// The rule the whole feature turns on. A protected day that nobody played keeps the
        /// streak and buys <b>no night</b>: the start slides forward by exactly the days
        /// forgiven, so a player on night twenty who is away for five protected days comes
        /// back to night twenty-one rather than to night twenty-six.
        ///
        /// <para>
        /// Getting this wrong is not a visible bug. It is six chests handed over for one
        /// purchase, with every gate green and every number plausible — and because a night
        /// is a claim, it would be six chests the server would pay for.
        /// </para>
        /// </summary>
        [Test]
        public void ProtectedDaysKeepTheStreakAndBuyNoNights()
        {
            int start = Day - 19, last = Day;               // night twenty
            int shield = Day + 1;

            Adv(start, last, Day + 6, shield, out int ns, out int nl, out int forgiven);

            Assert.AreEqual(5, forgiven, "five days between the last played one and this one");
            Assert.AreEqual(start + 5, ns, "the start slides by exactly the days forgiven");
            Assert.AreEqual(Day + 6, nl);
            Assert.AreEqual(21, Len(ns, nl, Day + 6, shield), "one night on, not six");
        }

        /// <summary>
        /// The floor is a <em>day</em>, and every night's day has just slid — so it has to
        /// slide with them. Without this a protected player comes back to nights they have
        /// already been paid for sitting on the board asking to be paid again.
        /// </summary>
        [Test]
        public void TheCollectedFloorSlidesWithTheForgivenDays()
        {
            int start = Day - 19, last = Day;
            int floor = Day;                                // every night so far collected
            int shield = Day + 1;

            Adv(start, last, Day + 6, shield, out int ns, out int nl, out int forgiven);
            int moved = DailyStreak.SeedCollected(floor, Day + 6, continues: true, forgiven);

            Assert.AreEqual(floor + forgiven, moved);
            Assert.AreEqual(1, Pend(ns, nl, moved, Day + 6, StreakTable.Default, shield),
                            "only the night just earned is waiting");
        }

        [Test]
        public void AShieldDoesNotStretchPastItsWindow()
        {
            int start = Day - 3, last = Day;
            int shield = Day + 1;

            // The last day the window covers is shield + Days - 1. A run resumed on the day
            // after that is still alive (yesterday always counts); one after that is not.
            Assert.AreNotEqual(0, Len(start, last, shield + Days, shield),
                               "the day after the window still has yesterday's grace");
            Assert.AreEqual(0, Len(start, last, shield + Days + 1, shield),
                            "and the day after that is a broken streak");
        }

        /// <summary>
        /// Playing inside the window must write nothing to the shield, which is what makes
        /// "it does not extend it" true by construction rather than by a rule somebody has to
        /// remember. The entitlement is one date and <c>Advance</c> never returns one.
        /// </summary>
        [Test]
        public void PlayingInsideTheWindowDoesNotMoveIt()
        {
            int shield = Day;
            int start = Day - 2, last = Day - 1;

            for (int n = 0; n < Days; n++)
            {
                Adv(start, last, Day + n, shield, out int ns, out int nl, out _);
                start = ns;
                last = nl;
            }

            Assert.AreEqual(Day, shield, "nothing in the advance rule can touch the window");
            Assert.AreEqual(0, Len(start, last, Day + Days + 2, shield),
                            "so a week of play inside it does not buy a second week of cover");
        }

        /// <summary>
        /// "At risk" is "doing nothing today loses it", which is <em>not</em> the same as "no
        /// shield covers tomorrow" — and the difference is the last day of a window.
        ///
        /// <para>
        /// Yesterday always counts, so a window ending tonight still leaves tomorrow to play.
        /// Reading the shield directly put the clock up a day early with the shield row beside
        /// it still reporting a day left, which is the page contradicting itself on the one
        /// state the player paid to handle. Pinned over the whole window rather than on the
        /// boundary, because the boundary is the only place it was ever wrong.
        /// </para>
        /// </summary>
        [Test]
        public void AProtectedStreakIsOnlyAtRiskOnTheDayAfterItsWindow()
        {
            int last = Day;                 // played today
            int shield = Day + 1;           // and protected from tomorrow

            // A run is at risk on `today` when it would not survive `today + 1`.
            bool Risk(int today) => !DailyStreak.Survives(last, today + 1, shield, Days);

            for (int away = 1; away <= Days; away++)
                Assert.IsFalse(Risk(Day + away),
                               $"day {away} of the window is covered, so nothing is urgent yet");

            Assert.IsFalse(Risk(Day + Days),
                           "the day after the window still has yesterday's grace");
            Assert.IsTrue(Risk(Day + Days + 1),
                          "and the day after that is the one where doing nothing loses it");
        }

        [Test]
        public void TheMergeKeepsTheLaterShield()
        {
            var merged = DailyStreak.Join(State(Day - 5, Day, 0, Day - 9), State(Day - 5, Day, 0, Day));

            Assert.AreEqual(Day, merged.shieldFromDay,
                            "a purchase cannot be undone, so the later date knows more");
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

            Assert.AreEqual(1, Len(merged.startDay, merged.lastPlayedDay, Day + 2));
        }

        [Test]
        public void TheMergeIsOrderIndependent()
        {
            var a = State(Day - 5, Day - 1, Day - 3, Day - 8);
            var b = State(Day - 3, Day, Day - 2, Day - 1);

            AssertSame(DailyStreak.Join(a, b), DailyStreak.Join(b, a), "join is commutative");
        }

        [Test]
        public void TheMergeIsIdempotent()
        {
            var a = State(Day - 5, Day - 1, Day - 3, Day - 8);
            var b = State(Day - 3, Day, Day - 2, Day - 1);

            var once = DailyStreak.Join(a, b);
            var twice = DailyStreak.Join(once, b);

            AssertSame(once, twice, "merging the same device twice must change nothing");
        }

        [Test]
        public void TheMergeIsAssociative()
        {
            var a = State(Day - 8, Day - 4, Day - 6, Day - 20);
            var b = State(Day - 3, Day - 1, Day - 3, Day - 9);
            var c = State(Day - 3, Day, Day - 2, 0);

            AssertSame(DailyStreak.Join(DailyStreak.Join(a, b), c),
                       DailyStreak.Join(a, DailyStreak.Join(b, c)),
                       "join is associative");
        }

        /// <summary>
        /// The collected floor merges the same way the dates do, and in the same direction:
        /// the device that has paid more out wins. It can cost a player a night neither
        /// device had collected, which is the safe error — the other one pays a night
        /// twice, and two devices granting the same chest is the failure the whole
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
            var held = State(Day - 5, Day, 0, Day - 2);

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
                    Assert.AreSame(first.Tier, later.Tier, $"night {night} on lap {lap + 1} (chest)");
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
        /// The shipped ladder is coins, gems and chests, and nothing else. Asked of the
        /// table rather than of the file, so a retune that reached for a heart is refused
        /// here as well as by the reader.
        /// </summary>
        [Test]
        public void TheShippedLadderPaysOnlyCoinsGemsAndChests()
        {
            var table = StreakTable.Default;

            for (int night = 1; night <= table.Length; night++)
            {
                var rung = table.Rung(night);
                Assert.IsTrue(rung.IsValid, $"night {night} pays nothing");

                if (rung.IsChest)
                {
                    Assert.AreNotEqual("wood", rung.Tier.Id,
                                       "a streak asks for seven consecutive days; the humblest " +
                                       "chest in the game is what a daily task pays for two runs");
                    continue;
                }

                Assert.IsTrue(StreakRules.IsPayableKind(rung.Kind),
                              $"night {night} pays {rung.Kind}");
            }
        }

        /// <summary>
        /// Currency is allowed on the ladder, and this is the test that says so deliberately
        /// rather than by omission. What makes it safe is not on this side at all — a night
        /// is claimed under an id derived from its calendar day, and the server grants from
        /// its own copy of the ladder against a floor no client can write. See
        /// <c>StreakTable</c> and <c>functions/src/streak.ts</c>.
        /// </summary>
        [Test]
        public void ACurrencyRungIsAdopted()
        {
            foreach (string currency in new[] { ChestDropKinds.Credits, ChestDropKinds.Gems })
            {
                var problems = new List<string>();
                var table = Resolve(new StreakDto
                {
                    rungs = new[]
                    {
                        new StreakRungDto { kind = currency, amount = 25 },
                        new StreakRungDto { tier = "silver" },
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
        /// A chest rung names a tier out of the tasks block, which is invariant 45's bargain
        /// read across: one authored chest, one published disclosure, one retune.
        /// </summary>
        [Test]
        public void AChestRungNamesATierFromTheTasksBlock()
        {
            var problems = new List<string>();
            var table = Resolve(new StreakDto
            {
                rungs = new[] { new StreakRungDto { tier = "royal" } },
            }, problems);

            Assert.IsEmpty(problems, string.Join("; ", problems));
            Assert.IsTrue(table.Rung(1).IsChest);
            Assert.AreEqual("royal", table.Rung(1).Tier.Id);
            Assert.IsFalse(table.Rung(1).AsDrop().IsValid,
                           "a chest has contents rather than an amount");
        }

        [Test]
        public void AChestRungNamingAnUnknownTierRefusesTheWholeLadder()
        {
            var problems = new List<string>();
            var table = Resolve(new StreakDto
            {
                rungs = new[]
                {
                    new StreakRungDto { kind = ChestDropKinds.Credits, amount = 100 },
                    new StreakRungDto { tier = "platinum" },
                },
            }, problems);

            Assert.AreSame(StreakTable.Default, table,
                           "a night paying a chest nobody can price is a claim the server " +
                           "can never confirm");
            Assert.IsTrue(problems.Count > 0);
        }

        [Test]
        public void ARungCannotNameBothAKindAndATier()
        {
            var problems = new List<string>();

            Assert.AreSame(StreakTable.Default, Resolve(new StreakDto
            {
                rungs = new[]
                {
                    new StreakRungDto { kind = ChestDropKinds.Gems, amount = 5, tier = "gold" },
                },
            }, problems));

            Assert.IsTrue(problems.Count > 0);
        }

        /// <summary>
        /// Hearts and boosts reach this ladder through a chest tier now, so a file still
        /// naming one is refused <b>by name</b> rather than skipped (invariant 5f): skipping
        /// it would renumber every night above it and silently change what the player is
        /// owed.
        /// </summary>
        [Test]
        public void ARetiredKindIsRefusedByName()
        {
            foreach (string kind in new[] { ChestDropKinds.Hearts, ChestDropKinds.HeartBoost })
            {
                var problems = new List<string>();
                var table = Resolve(new StreakDto
                {
                    rungs = new[]
                    {
                        new StreakRungDto { kind = ChestDropKinds.Credits, amount = 100 },
                        new StreakRungDto { kind = kind, amount = 2 },
                    },
                }, problems);

                Assert.AreSame(StreakTable.Default, table, kind + " must not be authorable");
                Assert.IsTrue(problems.Count > 0, "and the refusal has to say so");
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

            Assert.AreEqual(150, StreakRung.Currency(ChestDropKind.Credits, 150).Amount,
                            "an ordinary figure must survive the clamp");

            Assert.AreEqual(StreakRules.MaxCreditsPerRung,
                            StreakRung.Currency(ChestDropKind.Credits, 1_000_000).Amount);
            Assert.AreEqual(StreakRules.MaxGemsPerRung,
                            StreakRung.Currency(ChestDropKind.Gems, 1_000_000).Amount);
        }

        [Test]
        public void AGoodLadderIsAdopted()
        {
            var problems = new List<string>();
            var table = Resolve(new StreakDto
            {
                rungs = new[]
                {
                    new StreakRungDto(),
                    new StreakRungDto { kind = ChestDropKinds.Gems, amount = 12 },
                    new StreakRungDto { tier = "gold" },
                },
            }, problems);

            Assert.AreNotSame(StreakTable.Default, table);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems));
            Assert.AreEqual(3, table.Length);
            Assert.AreEqual(ChestDropKind.Gems, table.Rung(2).Kind);
            Assert.AreEqual(12, table.Rung(2).Amount);
            Assert.AreEqual("gold", table.Rung(3).Tier.Id);
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
            var table = Resolve(new StreakDto
            {
                rungs = new[]
                {
                    new StreakRungDto(),
                    new StreakRungDto { kind = "wisdom", amount = 3 },
                    new StreakRungDto { kind = ChestDropKinds.Credits, amount = 200 },
                },
            }, problems);

            Assert.AreSame(StreakTable.Default, table);
        }

        [Test]
        public void AnAbsentBlockIsNotAnError()
        {
            var problems = new List<string>();

            Assert.AreSame(StreakTable.Default, Resolve(null, problems));
            Assert.AreEqual(0, problems.Count);
        }

        [Test]
        public void ARungCannotPayMoreThanTheCeiling()
        {
            var problems = new List<string>();
            var table = Resolve(new StreakDto
            {
                rungs = new[]
                {
                    new StreakRungDto { kind = ChestDropKinds.Gems, amount = 100_000 },
                },
            }, problems);

            Assert.AreEqual(StreakRules.MaxGemsPerRung, table.Rung(1).Amount);
            Assert.IsTrue(problems.Count > 0, "clamping silently is how a typo ships");
        }

        // --------------------------------------------------------- the shield price
        [Test]
        public void AnAbsentShieldPriceIsTheBuiltInOne()
        {
            var table = Resolve(new StreakDto
            {
                rungs = new[] { new StreakRungDto { kind = ChestDropKinds.Credits, amount = 100 } },
            }, new List<string>());

            Assert.AreEqual(StreakRules.DefaultShieldGems, table.ShieldGems,
                            "a shield that silently stopped being offered is a feature " +
                            "disappearing from a screen with every gate green");
            Assert.AreEqual(StreakRules.DefaultShieldDays, table.ShieldDays);
            Assert.IsTrue(table.SellsShield);
        }

        [Test]
        public void AnExplicitZeroWithdrawsTheOffer()
        {
            var table = Resolve(new StreakDto
            {
                rungs = new[] { new StreakRungDto { kind = ChestDropKinds.Credits, amount = 100 } },
                shieldGems = -1,
            }, new List<string>());

            Assert.IsFalse(table.SellsShield);
        }

        [Test]
        public void TheShieldWindowIsClampedToSomethingSayable()
        {
            var problems = new List<string>();
            var table = Resolve(new StreakDto
            {
                rungs = new[] { new StreakRungDto { kind = ChestDropKinds.Credits, amount = 100 } },
                shieldDays = 900,
                shieldGems = 10_000_000,
            }, problems);

            Assert.AreEqual(StreakRules.MaxShieldDays, table.ShieldDays);
            Assert.AreEqual(StreakRules.MaxShieldGems, table.ShieldGems);
            Assert.IsTrue(problems.Count >= 2, "both clamps have to be said out loud");
        }

        // ------------------------------------------------------------ collecting
        //
        // Rewards are handed over when the player taps a night rather than applied when
        // the run ends, so a third date records how far that has got. The risk it adds is
        // exactly one: paying a night twice. Everything below is aimed at that — the
        // migration off the build that paid automatically, the floor a lapsed streak
        // leaves behind, and the rule that only the oldest waiting night may be taken.

        static readonly StreakTable Ladder = StreakTable.Default;

        /// <summary>A run of <paramref name="days"/> days ending today, nothing collected.</summary>
        static int StartOf(int days) => Day - days + 1;

        [Test]
        public void ANightIsNotWaitingBeforeItIsEarned()
        {
            int start = StartOf(3);

            Assert.IsTrue(Waiting(start, Day, start - 1, Day, 2, Ladder),
                          "night two has been reached");
            Assert.IsFalse(Waiting(start, Day, start - 1, Day, 4, Ladder),
                           "night four has not happened yet");
        }

        /// <summary>
        /// <b>Only the oldest waiting night may be taken, and that is what paying a chest
        /// cost.</b> The floor is a floor — taking night three would take one and two with it
        /// — which was invisible while every rung was a figure and is not once a rung opens a
        /// ceremony: a sweep would grant three chests behind one animation, which is the
        /// "reward that arrives while a panel is up" failure this game has already made twice.
        /// </summary>
        [Test]
        public void OnlyTheOldestWaitingNightIsCollectable()
        {
            int start = StartOf(3);
            int floor = start - 1;                       // nothing taken yet

            Assert.AreEqual(3, Pend(start, Day, floor, Day, Ladder), "three nights are owed");

            Assert.IsTrue(Can(start, Day, floor, Day, 1, Ladder), "night one is the oldest");
            Assert.IsFalse(Can(start, Day, floor, Day, 2, Ladder), "night two has to wait its turn");
            Assert.IsFalse(Can(start, Day, floor, Day, 3, Ladder));

            // And taking it moves the offer on by exactly one.
            floor = DailyStreak.DayOfRung(start, 1);
            Assert.IsFalse(Can(start, Day, floor, Day, 1, Ladder));
            Assert.IsTrue(Can(start, Day, floor, Day, 2, Ladder));
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
            var sparse = Resolve(new StreakDto
            {
                rungs = new[]
                {
                    new StreakRungDto(),                                              // pays nothing
                    new StreakRungDto { kind = ChestDropKinds.Gems, amount = 4 },
                },
            }, problems);

            int start = StartOf(2);

            Assert.IsFalse(sparse.Rung(1).IsValid, "precondition: the first night pays nothing");
            Assert.IsFalse(Can(start, Day, start - 1, Day, 1, sparse));
            Assert.IsTrue(Can(start, Day, start - 1, Day, 2, sparse),
                          "the night beside it is unaffected, and is the oldest that pays");
        }

        [Test]
        public void ACollectedNightDoesNotComeBack()
        {
            int start = StartOf(3);

            Assert.IsTrue(Waiting(start, Day, start - 1, Day, 2, Ladder));
            Assert.IsFalse(Waiting(start, Day, start + 1, Day, 2, Ladder),
                           "the floor has passed night two");
        }

        /// <summary>
        /// The floor is why taking a later night would take the earlier ones with it, which
        /// is the reason only the earliest may be taken at all. It is still the only reading
        /// that cannot lose a reward: a per-night flag would have to be cleared when a streak
        /// breaks, which is not monotonic and so cannot be joined.
        /// </summary>
        [Test]
        public void TheFloorCoversEveryNightBelowIt()
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
            Assert.AreEqual(5, Pend(start, Day, start - 1, Day, Ladder));

            // Collected through night three: four and five are left.
            Assert.AreEqual(2, Pend(start, Day, DailyStreak.DayOfRung(start, 3), Day, Ladder));

            Assert.AreEqual(0, Pend(start, Day, Day, Day, Ladder));
        }

        [Test]
        public void ABrokenStreakHasNothingWaiting()
        {
            int start = Day - 9;

            Assert.AreEqual(0, Pend(start, Day - 5, start - 1, Day, Ladder),
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
            Assert.AreEqual(0, Pend(start, Day, repaired, Day, Ladder),
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
            int seeded = DailyStreak.SeedCollected(oldStart, Day, continues: false, forgiven: 0);

            Assert.AreEqual(Day - 1, seeded);
            Assert.AreEqual(0, Pend(oldStart, Day - 10, seeded, Day, Ladder),
                            "the lapsed run's nights are behind the new floor");
        }

        [Test]
        public void AContinuedRunLeavesTheFloorWhereItIs()
        {
            int floor = Day - 4;

            Assert.AreEqual(floor, DailyStreak.SeedCollected(floor, Day, continues: true, forgiven: 0),
                            "a night held on to must not lose the rungs waiting on it");
        }

        [Test]
        public void TheSeededFloorNeverFalls()
        {
            for (int floor = Day - 30; floor <= Day + 5; floor++)
                foreach (bool continues in new[] { true, false })
                    foreach (int forgiven in new[] { 0, 1, 5 })
                        Assert.GreaterOrEqual(
                            DailyStreak.SeedCollected(floor, Day, continues, forgiven), floor,
                            $"the floor fell from {floor} ({continues}, {forgiven})");
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

                Adv(start, last, today, out int nextStart, out int nextLast);
                bool continues = nextStart == start;
                start = nextStart;
                last = nextLast;
                floor = DailyStreak.SeedCollected(floor, today, continues, 0);

                int rung = Len(start, last, today);
                if (Ladder.Rung(rung).IsValid) paying++;

                Assert.AreEqual(paying == 0 ? 0 : 1, Pend(start, last, floor, today, Ladder),
                                $"exactly the night just earned is waiting on day {n + 1}");

                if (!Can(start, last, floor, today, rung, Ladder)) continue;

                offered++;
                floor = DailyStreak.DayOfRung(start, rung);   // the player collects it
            }

            // Asked of the ladder rather than written down, so retuning which nights pay
            // cannot quietly turn this into a test of nothing.
            Assert.AreEqual(paying, offered, "every paying night was offered exactly once");
            Assert.AreEqual(0, Pend(start, last, floor, Day + Length - 1, Ladder));
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
        ///
        /// The shield is the sharpest case: a player who paid to be away and then opened the
        /// game on their tablet must not find the streak they bought protection for already
        /// broken.
        /// </summary>
        [Test]
        public void AStreakThatMovedIsAChangeWorthPushing()
        {
            var before = SaveWith(State(Day - 4, Day - 1, Day - 2, Day - 9));

            foreach (var after in new[]
            {
                State(Day, Day - 1, Day - 2, Day - 9),
                State(Day - 4, Day, Day - 2, Day - 9),
                State(Day - 4, Day - 1, Day - 1, Day - 9),
                State(Day - 4, Day - 1, Day - 2, Day),
            })
            {
                Assert.IsFalse(SaveDelta.Between(before, SaveWith(after)).IsEmpty,
                               "a streak date that moved must reach the other device");
            }
        }

        [Test]
        public void AnUnchangedStreakSendsNothing()
        {
            var streak = State(Day - 4, Day, Day - 1, Day - 3);

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

            int first = First(start, Day, floor, Day, Ladder);
            Assert.AreEqual(7, first, "night seven is the oldest still waiting");

            int board = DailyStreak.CycleStart(first, Rungs);
            Assert.AreEqual(1, board, "the board must page back to the lap holding it");
            Assert.Less(first - board, Rungs, "and night seven must fall inside that lap");
        }

        /// <summary>
        /// The board's window moves on the tap that empties the lap, and that is the fact the
        /// screen keys its redraw on.
        ///
        /// <para>
        /// The reported case, exactly: a player standing on night eight who had not taken
        /// night seven. Before the tap the window is lap one, because that is where the oldest
        /// thing owed is; after it the window is lap two, because night eight is now the
        /// oldest. It is a different set of rows rather than different words on the same ones,
        /// which is why <c>StreakScreen</c> asks this question rather than repainting — a page
        /// that only repainted would leave the player looking at seven collected nights with
        /// nothing to tap and their reward apparently gone.
        /// </para>
        /// </summary>
        [Test]
        public void TakingTheLastNightOfALapMovesTheBoardOntoTheNext()
        {
            int rungs = Ladder.Length;
            int start = StartOf(rungs + 1);                    // a streak one night into lap two
            int before = DailyStreak.DayOfRung(start, rungs - 1);   // everything but the crest taken

            Assert.AreEqual(rungs, First(start, Day, before, Day, Ladder),
                            "the crest of the first lap is what is owed");
            Assert.AreEqual(1, DailyStreak.CycleStart(First(start, Day, before, Day, Ladder), rungs),
                            "so the board is still showing the first lap");

            // The tap. TryCollect moves the floor to the night's own calendar day, which is
            // the whole of what collecting a night writes down.
            int after = DailyStreak.DayOfRung(start, rungs);

            int next = First(start, Day, after, Day, Ladder);
            Assert.AreEqual(rungs + 1, next, "the first night of lap two is now the oldest owed");
            Assert.AreEqual(rungs + 1, DailyStreak.CycleStart(next, rungs),
                            "and the board has to move with it");

            Assert.IsTrue(Can(start, Day, after, Day, rungs + 1, Ladder),
                          "the night the new board opens on is takeable at once");
            Assert.IsTrue(Ladder.Rung(rungs + 1).IsValid,
                          "and it pays, because the ladder laps rather than running out");
        }

        [Test]
        public void AStreakPastTheLadderKeepsPaying()
        {
            int start = StartOf(10);
            int floor = DailyStreak.DayOfRung(start, 7);   // the whole first lap taken

            Assert.AreEqual(3, Pend(start, Day, floor, Day, Ladder),
                            "nights eight, nine and ten are owed");

            int board = DailyStreak.CycleStart(First(start, Day, floor, Day, Ladder), 7);

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

                Adv(start, last, today, out int nextStart, out int nextLast);
                bool continues = nextStart == start;
                start = nextStart;
                last = nextLast;
                floor = DailyStreak.SeedCollected(floor, today, continues, 0);

                int night = Len(start, last, today);
                Assert.AreEqual(n + 1, night, "the streak counts on without resetting");

                int pending = First(start, last, floor, today, Ladder);

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

                int board = DailyStreak.CycleStart(pending, rungs);
                Assert.GreaterOrEqual(pending, board, $"night {night} is above the board");
                Assert.Less(pending - board, rungs, $"night {night} is below the board");

                floor = DailyStreak.DayOfRung(start, night);   // the player collects it
                Assert.AreEqual(0, Pend(start, last, floor, today, Ladder),
                                $"nothing should be left over on night {night}");
            }

            Assert.AreEqual(nights, Len(start, last, Day + nights - 1),
                            "twenty-one nights, and the count never restarted");
        }

        /// <summary>
        /// The same walk with a shield in the middle of it — a fortnight of play, a week
        /// away under cover, and then play again. The count has to carry on from where it
        /// was, every night has to be offered exactly once, and no night may be offered for
        /// a day nobody played.
        ///
        /// <para>
        /// This is the case the whole shield rests on and the only one that exercises the
        /// start and the floor sliding together. Either on its own is a bug that looks like
        /// the other one working.
        /// </para>
        /// </summary>
        [Test]
        public void AProtectedWeekKeepsTheCountAndOffersNoNightForIt()
        {
            int start = 0, last = 0, floor = 0, shield = 0;
            int offered = 0;

            // A fortnight of ordinary play.
            for (int n = 0; n < 14; n++)
            {
                int today = Day + n;
                Adv(start, last, today, shield, out int ns, out int nl, out int forgiven);
                bool continues = ns != today;
                start = ns; last = nl;
                floor = DailyStreak.SeedCollected(floor, today, continues, forgiven);

                int night = Len(start, last, today, shield);
                Assert.AreEqual(n + 1, night);

                floor = DailyStreak.DayOfRung(start, night);
                offered++;
            }

            // The shield is bought on the last day played, and the player disappears.
            shield = Day + 13;

            for (int away = 1; away <= Days; away++)
                Assert.AreEqual(14, Len(start, last, Day + 13 + away, shield),
                                $"still night fourteen {away} day(s) later");

            // And comes back on the last day the window covers.
            int back = Day + 13 + Days - 1;
            Adv(start, last, back, shield, out int bs, out int bl, out int f2);
            bool cont = bs != back;

            Assert.IsTrue(cont, "the run continued");
            Assert.AreEqual(Days - 2, f2, "the days between the last played one and this one");

            start = bs; last = bl;
            floor = DailyStreak.SeedCollected(floor, back, cont, f2);

            Assert.AreEqual(15, Len(start, last, back, shield),
                            "one night on from fourteen, not a week on");
            Assert.AreEqual(1, Pend(start, last, floor, back, Ladder, shield),
                            "exactly the night just earned is waiting");
            Assert.AreEqual(15, First(start, last, floor, back, Ladder, shield));

            floor = DailyStreak.DayOfRung(start, 15);
            offered++;

            Assert.AreEqual(15, offered, "fifteen nights played, fifteen nights offered");
            Assert.AreEqual(0, Pend(start, last, floor, back, Ladder, shield));
        }
    }
}
