using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The daily chests.
    ///
    /// Three properties carry the feature and are pinned hardest here. A chest's contents
    /// must be a <em>fact</em> about that chest rather than about the moment it was
    /// tapped, or force-quitting the opening animation becomes a reroll. A chest must pay
    /// exactly once, however many devices claim it and however often a reply is lost.
    /// And the day must roll over without anything having to fire at midnight.
    ///
    /// The fourth property — that this rolls chests identically to the server — is not
    /// here. It lives in <see cref="RewardVectorTests"/>, against the shared vectors,
    /// because a test that only proved the client agreed with itself would be exactly
    /// the test that lets the two halves drift.
    /// </summary>
    public sealed class DailyChestTests
    {
        const string Key = "test_player";

        // ------------------------------------------------------------ the day
        [Test]
        public void ADayIsWholeDaysSinceTheEpochInUtc()
        {
            Assert.AreEqual(0, DailyRules.DayKeyFor(0));
            Assert.AreEqual(0, DailyRules.DayKeyFor(DailyRules.SecondsPerDay - 1));
            Assert.AreEqual(1, DailyRules.DayKeyFor(DailyRules.SecondsPerDay));
            Assert.AreEqual(2, DailyRules.DayKeyFor(DailyRules.SecondsPerDay * 2 + 5));
        }

        [Test]
        public void ANegativeOrZeroClockReadsAsDayZeroRatherThanThrowing()
        {
            Assert.AreEqual(0, DailyRules.DayKeyFor(-5));
            Assert.AreEqual(0, DailyRules.DayKeyFor(0));
        }

        [Test]
        public void TheResetCountdownRunsToTheNextMidnight()
        {
            long midnight = DailyRules.SecondsPerDay * 20315;

            Assert.AreEqual(DailyRules.SecondsPerDay, DailyRules.SecondsUntilReset(midnight));
            Assert.AreEqual(1, DailyRules.SecondsUntilReset(midnight + DailyRules.SecondsPerDay - 1));
            Assert.AreEqual(DailyRules.SecondsPerDay - 60, DailyRules.SecondsUntilReset(midnight + 60));
        }

        // ----------------------------------------------------------- the roll
        /// <summary>
        /// The property that stops a player rerolling a prize they do not like by killing
        /// the app during the opening animation.
        /// </summary>
        [Test]
        public void AChestHoldsTheSameThingEveryTimeItIsAsked()
        {
            var table = DailyChestTable.Default;

            for (int chest = 0; chest < table.ChestCount; chest++)
            {
                var first = table.Roll(Key, 20315, chest);

                for (int again = 0; again < 5; again++)
                    CollectionAssert.AreEqual(Describe(first), Describe(table.Roll(Key, 20315, chest)),
                                              "a chest's contents must not depend on when it is opened");
            }
        }

        [Test]
        public void DifferentPlayersDaysAndChestsRollDifferently()
        {
            var table = DailyChestTable.Default;

            var seen = new HashSet<string>();
            int total = 0;

            foreach (string key in new[] { "a", "b", "c", "d", "e", "f", "g", "h" })
                for (int day = 20315; day < 20325; day++)
                    for (int chest = 0; chest < table.ChestCount; chest++)
                    {
                        seen.Add(string.Join(",", Describe(table.Roll(key, day, chest))));
                        total++;
                    }

            // Not an exact count — that would be a change-detector. The point is that the
            // generator is not collapsing everything onto a handful of outcomes, which a
            // broken seed absolutely would.
            Assert.Greater(seen.Count, total / 4,
                           "the seed is not spreading; a chest looks nearly the same for everyone");
        }

        [Test]
        public void EveryChestAlwaysPaysSomething()
        {
            var table = DailyChestTable.Default;

            foreach (string key in new[] { "a", "bb", "ccc", "dddd" })
                for (int day = 0; day < 40; day++)
                    for (int chest = 0; chest < table.ChestCount; chest++)
                    {
                        var drops = table.Roll(key, day, chest);
                        Assert.IsNotEmpty(drops, "a chest that can pay nothing is a chest that reads as broken");

                        foreach (var drop in drops)
                            Assert.Greater(drop.Amount, 0, "a zero-amount drop would render as '+0'");
                    }
        }

        /// <summary>
        /// The case that would otherwise pay a player half of what the server grants.
        ///
        /// A chest whose floor and whose bonus are both credits produces two drops of one
        /// kind, and both would carry the id <c>daily:{day}:{chest}:credits</c> — so the
        /// second award is refused as a duplicate of the first. The server sums them.
        /// Merging at the source is what keeps the two halves talking about one number.
        /// </summary>
        [Test]
        public void DropsOfTheSameKindAreSummedIntoOne()
        {
            var table = Read(new DailyChestDto
            {
                runsPerChest = 3,
                chests = new[]
                {
                    new DailyChestEntryDto
                    {
                        guaranteed = new[] { Band("credits", 100, 100) },
                        options = new[] { Option("credits", 7, 7, 1) },
                    },
                },
            });

            var drops = table.Roll(Key, 20315, 0);

            Assert.AreEqual(1, drops.Count, "two credit drops must arrive as one award");
            Assert.AreEqual(ChestDropKind.Credits, drops[0].Kind);
            Assert.AreEqual(107, drops[0].Amount);
        }

        [Test]
        public void AFixedBandAlwaysPaysItsOneValue()
        {
            var table = Read(new DailyChestDto
            {
                runsPerChest = 1,
                chests = new[]
                {
                    new DailyChestEntryDto
                    {
                        guaranteed = new[] { Band("gems", 4, 4) },
                        options = new DailyOptionDto[0],
                    },
                },
            });

            for (int day = 0; day < 50; day++)
            {
                var drops = table.Roll(Key, day, 0);
                Assert.AreEqual(1, drops.Count);
                Assert.AreEqual(4, drops[0].Amount);
            }
        }

        [Test]
        public void EveryRolledAmountLandsInsideItsAuthoredBand()
        {
            var table = Read(new DailyChestDto
            {
                runsPerChest = 3,
                chests = new[]
                {
                    new DailyChestEntryDto
                    {
                        guaranteed = new[] { Band("credits", 17, 23) },
                        options = new[] { Option("gems", 2, 5, 1), Option("hearts", 1, 1, 1) },
                    },
                },
            });

            for (int day = 0; day < 400; day++)
                foreach (var drop in table.Roll(Key, day, 0))
                {
                    if (drop.Kind == ChestDropKind.Credits) Assert.That(drop.Amount, Is.InRange(17, 23));
                    if (drop.Kind == ChestDropKind.Gems) Assert.That(drop.Amount, Is.InRange(2, 5));
                    if (drop.Kind == ChestDropKind.Hearts) Assert.AreEqual(1, drop.Amount);
                }
        }

        /// <summary>
        /// The odds shown to the player have to be the odds the generator actually uses,
        /// or the disclosure is a decoration. Checked statistically over a wide sample
        /// with a loose tolerance — the point is that a 70/30 table is not behaving like
        /// a 50/50 one, not that the modulo has no bias.
        /// </summary>
        [Test]
        public void TheWeightsAreTheOddsThePlayerIsShown()
        {
            var table = Read(new DailyChestDto
            {
                runsPerChest = 3,
                chests = new[]
                {
                    new DailyChestEntryDto
                    {
                        guaranteed = new[] { Band("credits", 1, 1) },
                        options = new[] { Option("gems", 1, 1, 70), Option("hearts", 1, 1, 30) },
                    },
                },
            });

            var chest = table.Chest(0);
            Assert.AreEqual(70f, chest.ChanceOf(0), 0.01f);
            Assert.AreEqual(30f, chest.ChanceOf(1), 0.01f);

            int gems = 0, samples = 4000;
            for (int day = 0; day < samples; day++)
                foreach (var drop in table.Roll(Key, day, 0))
                    if (drop.Kind == ChestDropKind.Gems) gems++;

            float observed = 100f * gems / samples;
            Assert.That(observed, Is.InRange(64f, 76f),
                        $"the table says 70% gems and the generator produced {observed:0.#}%");
        }

        [Test]
        public void TheChancesAcrossAChestSumToOneHundred()
        {
            var table = DailyChestTable.Default;

            for (int i = 0; i < table.ChestCount; i++)
            {
                var chest = table.Chest(i);
                if (chest.Options.Count == 0) continue;

                float total = 0f;
                for (int o = 0; o < chest.Options.Count; o++) total += chest.ChanceOf(o);

                Assert.AreEqual(100f, total, 0.01f,
                                $"chest {i}'s published odds do not add up, so one of them is a lie");
            }
        }

        // ---------------------------------------------------- reading the table
        [Test]
        public void AnAbsentDailyBlockIsNotAnError()
        {
            var problems = new List<string>();
            var table = DailyChestTable.Resolve(null, problems);

            Assert.IsEmpty(problems);
            Assert.AreSame(DailyChestTable.Default, table);
        }

        [Test]
        public void AChestThatGuaranteesNothingIsRefused()
        {
            var problems = new List<string>();
            var table = DailyChestTable.Resolve(new DailyChestDto
            {
                runsPerChest = 3,
                chests = new[] { new DailyChestEntryDto { options = new[] { Option("gems", 1, 1, 1) } } },
            }, problems);

            Assert.IsNotEmpty(problems);
            Assert.AreSame(DailyChestTable.Default, table, "a chest that can pay nothing must not ship");
        }

        [Test]
        public void AZeroWeightOptionIsRefusedBecauseItMakesTheOddsALie()
        {
            var problems = new List<string>();
            DailyChestTable.Resolve(new DailyChestDto
            {
                runsPerChest = 3,
                chests = new[]
                {
                    new DailyChestEntryDto
                    {
                        guaranteed = new[] { Band("credits", 1, 1) },
                        options = new[] { Option("gems", 1, 1, 0) },
                    },
                },
            }, problems);

            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void AnUnknownRewardKindIsSkippedRatherThanFatal()
        {
            var problems = new List<string>();
            var table = DailyChestTable.Resolve(new DailyChestDto
            {
                runsPerChest = 3,
                chests = new[]
                {
                    new DailyChestEntryDto
                    {
                        guaranteed = new[] { Band("credits", 5, 5), Band("moonstones", 1, 1) },
                        options = new DailyOptionDto[0],
                    },
                },
            }, problems);

            Assert.IsNotEmpty(problems, "content written against a newer build should say so");

            var drops = table.Roll(Key, 20315, 0);
            Assert.AreEqual(1, drops.Count, "the unknown kind is dropped, the known one survives");
            Assert.AreEqual(ChestDropKind.Credits, drops[0].Kind);
        }

        [Test]
        public void MoreChestsThanTheCeilingAreRefused()
        {
            var chests = new DailyChestEntryDto[DailyRules.MaxChests + 1];
            for (int i = 0; i < chests.Length; i++)
                chests[i] = new DailyChestEntryDto
                {
                    guaranteed = new[] { Band("credits", 1, 1) },
                    options = new DailyOptionDto[0],
                };

            var problems = new List<string>();
            var table = DailyChestTable.Resolve(new DailyChestDto { runsPerChest = 3, chests = chests },
                                                problems);

            Assert.IsNotEmpty(problems);
            Assert.AreSame(DailyChestTable.Default, table);
        }

        [Test]
        public void AnUnwrittenRunsPerChestInheritsRatherThanDisablingTheFeature()
        {
            var problems = new List<string>();
            var table = DailyChestTable.Resolve(new DailyChestDto
            {
                runsPerChest = -1,                       // the JsonUtility "not written" marker
                chests = new[]
                {
                    new DailyChestEntryDto
                    {
                        guaranteed = new[] { Band("credits", 1, 1) },
                        options = new DailyOptionDto[0],
                    },
                },
            }, problems);

            Assert.IsEmpty(problems);
            Assert.AreEqual(DailyChestTable.Default.RunsPerChest, table.RunsPerChest);
        }

        [Test]
        public void ChestsAreEarnedAtRisingMultiplesOfTheRunCount()
        {
            var table = DailyChestTable.Default;

            Assert.AreEqual(table.RunsPerChest, table.RunsFor(0));
            Assert.AreEqual(table.RunsPerChest * 2, table.RunsFor(1));
            Assert.AreEqual(table.RunsPerChest * table.ChestCount, table.RunsForAll);
        }

        // ----------------------------------------------------------- the merge
        [Test]
        public void TheLaterDayWinsOutright()
        {
            var yesterday = State(20315, runs: 9, claimed: 3);
            var today = State(20316, runs: 1, claimed: 0);

            var merged = DailyChests.Join(yesterday, today);

            Assert.AreEqual(20316, merged.dayKey);
            Assert.AreEqual(1, merged.runs, "yesterday's runs must not become a head start on today");
            Assert.AreEqual(0, merged.claimed);
        }

        [Test]
        public void WithinOneDayBothCountsTakeTheLarger()
        {
            var phone = State(20316, runs: 5, claimed: 1);
            var tablet = State(20316, runs: 2, claimed: 0);

            var merged = DailyChests.Join(phone, tablet);

            Assert.AreEqual(5, merged.runs);
            Assert.AreEqual(1, merged.claimed,
                            "taking the smaller claim count would let a paid chest be opened again");
        }

        [Test]
        public void TheDailyMergeIsAJoin()
        {
            var a = State(20316, runs: 5, claimed: 1);
            var b = State(20316, runs: 2, claimed: 2);

            var forward = DailyChests.Join(a, b);
            var backward = DailyChests.Join(b, a);

            Assert.AreEqual(forward.dayKey, backward.dayKey);
            Assert.AreEqual(forward.runs, backward.runs);
            Assert.AreEqual(forward.claimed, backward.claimed);

            var twice = DailyChests.Join(forward, b);
            Assert.AreEqual(forward.runs, twice.runs, "merging twice must change nothing");
            Assert.AreEqual(forward.claimed, twice.claimed);
        }

        [Test]
        public void MergingAgainstNothingKeepsWhatThereIs()
        {
            var mine = State(20316, runs: 4, claimed: 1);

            Assert.AreEqual(4, DailyChests.Join(mine, null).runs);
            Assert.AreEqual(4, DailyChests.Join(null, mine).runs);
            Assert.AreEqual(0, DailyChests.Join(null, null).runs);
        }

        // -------------------------------------------------- the pre-sign-in gate
        /// <summary>
        /// A backend that claims to exist and nothing else. Enough to put
        /// <see cref="DailyChests.CanOpen"/> into the branch that matters.
        /// </summary>
        sealed class PresentBackend : Cloud.ICloudSaveBackend
        {
            public bool IsAvailable => true;
            public Cloud.CloudIdentity CurrentIdentity => Cloud.CloudIdentity.None;

            static Task<(Cloud.CloudResult, Cloud.CloudIdentity)> NoIdentity()
                => Task.FromResult((Cloud.CloudResult.Failed(Cloud.CloudFailure.Offline),
                                    Cloud.CloudIdentity.None));

            static Task<(Cloud.CloudResult, List<Cloud.CloudWalletState>)> NoWallet()
                => Task.FromResult((Cloud.CloudResult.Failed(Cloud.CloudFailure.Offline),
                                    new List<Cloud.CloudWalletState>()));

            public Task<(Cloud.CloudResult result, Cloud.CloudIdentity identity)> SignInAsync(
                CancellationToken c = default) => NoIdentity();

            public Task<(Cloud.CloudResult result, Cloud.CloudIdentity identity)> ResumeAsync(
                CancellationToken c = default)
                => Task.FromResult((Cloud.CloudResult.Success, Cloud.CloudIdentity.None));

            public Task<(Cloud.CloudResult result, Cloud.CloudIdentity identity)> LinkAsync(
                Cloud.LinkCredential cr, CancellationToken c = default) => NoIdentity();

            public Task<(Cloud.CloudResult result, Cloud.CloudIdentity identity)> SignInWithCredentialAsync(
                Cloud.LinkCredential cr, CancellationToken c = default) => NoIdentity();

            public Task<(Cloud.CloudResult result, Cloud.CloudSnapshot snapshot)> PullAsync(
                string u, CancellationToken c = default)
                => Task.FromResult((Cloud.CloudResult.Failed(Cloud.CloudFailure.Offline),
                                    Cloud.CloudSnapshot.Missing));

            public Task<Cloud.CloudResult> PushAsync(string u, SaveFileDto s, SaveDelta d,
                                                     CancellationToken c = default)
                => Task.FromResult(Cloud.CloudResult.Failed(Cloud.CloudFailure.Offline));

            public Task<(Cloud.CloudResult result, List<Cloud.CloudWalletState> wallets)> ReadWalletAsync(
                string u, CancellationToken c = default) => NoWallet();

            public Task<(Cloud.CloudResult result, List<Cloud.CloudWalletState> wallets)> SubmitSpendsAsync(
                string u, IReadOnlyList<SpendEntryDto> s, CancellationToken c = default) => NoWallet();

            public Task<(Cloud.CloudResult result, List<Cloud.CloudWalletState> wallets)> SubmitAwardsAsync(
                string u, IReadOnlyList<GrantEntryDto> a, CancellationToken c = default) => NoWallet();

            public Task<(Cloud.CloudResult result, List<Cloud.CloudWalletState> wallets)> RedeemPurchaseAsync(
                string u, Cloud.PurchaseReceipt r, CancellationToken c = default) => NoWallet();

            public Task<(Cloud.CloudResult result, Dictionary<Content.LevelId, Social.LevelStats> stats)>
                ReadGroveStatsAsync(CancellationToken c = default)
                => Task.FromResult((Cloud.CloudResult.Failed(Cloud.CloudFailure.Offline),
                                    new Dictionary<Content.LevelId, Social.LevelStats>()));
        }

        [TearDown]
        public void ResetCloud()
        {
            Cloud.CloudSaveService.UseBackend(null);
            CloudState.Reset();
        }

        /// <summary>
        /// The guard that stops a player being shown one reward and given another.
        ///
        /// A chest is seeded from the account id so the server can recompute it. Before
        /// the first sign-in there is no account id, and no scheme can invent one the
        /// server would agree with — so the chest waits rather than paying out a number
        /// the server will overrule.
        /// </summary>
        [Test]
        public void AChestCannotBeOpenedBeforeTheAccountExists()
        {
            CloudState.Reset();
            Cloud.CloudSaveService.UseBackend(new PresentBackend());

            Assert.IsFalse(DailyChests.CanOpen,
                           "a chest rolled without an account id is one the server would " +
                           "re-roll differently");
        }

        [Test]
        public void SigningInOnceUnlocksChestsForGood()
        {
            CloudState.Reset();
            Cloud.CloudSaveService.UseBackend(new PresentBackend());
            CloudState.SignIn("uid_abc123");

            Assert.IsTrue(DailyChests.CanOpen,
                          "the id is stored in the save, so every later session opens " +
                          "chests offline quite happily");
        }

        [Test]
        public void WithNoBackendAtAllTheGateLifts()
        {
            CloudState.Reset();
            Cloud.CloudSaveService.UseBackend(null);

            Assert.IsTrue(DailyChests.CanOpen,
                          "nothing is adjudicated without a backend, so there is no second " +
                          "opinion for the client's roll to disagree with");
        }

        // ------------------------------------------------------------ helpers
        static DailyStateDto State(int dayKey, int runs, int claimed)
            => new DailyStateDto { dayKey = dayKey, runs = runs, claimed = claimed };

        static DailyDropDto Band(string kind, int min, int max)
            => new DailyDropDto { kind = kind, min = min, max = max };

        static DailyOptionDto Option(string kind, int min, int max, int weight)
            => new DailyOptionDto { kind = kind, min = min, max = max, weight = weight };

        static DailyChestTable Read(DailyChestDto dto)
        {
            var problems = new List<string>();
            var table = DailyChestTable.Resolve(dto, problems);
            Assert.IsEmpty(problems, string.Join("; ", problems));
            return table;
        }

        static List<string> Describe(List<ChestDrop> drops)
        {
            var parts = new List<string>();
            foreach (var drop in drops) parts.Add($"{ChestDropKinds.Id(drop.Kind)}={drop.Amount}");
            return parts;
        }
    }
}
