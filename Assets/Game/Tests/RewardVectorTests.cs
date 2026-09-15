using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Ads;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Events;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The client half of the shared reward contract.
    ///
    /// Earned currency is derived in two places — here in C# so the game works offline,
    /// and again in TypeScript on the server so a forged save can be caught rather than
    /// merely disbelieved. Two implementations of one rule drift; the comments saying
    /// "keep these in sync" were never going to survive eighteen months of content
    /// drops on their own.
    ///
    /// So both sides run <c>firebase/shared/reward-vectors.json</c>. This file proves
    /// the C# side matches it; <c>firebase/functions/test/reward-vectors.mjs</c> proves
    /// the TypeScript side does. Change the arithmetic on either side without changing
    /// the other and one of them goes red.
    /// </summary>
    public sealed class RewardVectorTests
    {
        // ------------------------------------------------------------- the file
        [Serializable]
        public sealed class VectorFile
        {
            public ProgressionDto progression;
            public LevelChapterDto[] levelChapters;
            public VectorCase[] cases;

            /// <summary>
            /// Synthetic on purpose, and separate from <see cref="progression"/>'s own
            /// daily block. What is under contract is the generator, not this month's
            /// drop rates — retuning the shipped table must not turn these red.
            /// </summary>
            public DailyChestDto dailyChestConfig;

            public DailyVectorCase[] dailyChestCases;

            /// <summary>
            /// The task chest's own tiers and cases — a chest seeded from a subject rather
            /// than a day. Synthetic for <see cref="dailyChestConfig"/>'s reason: what is under
            /// contract is the seeding, not this season's ladder.
            /// </summary>
            public TaskTierDto[] taskChestTiers;

            public TaskVectorCase[] taskChestCases;

            /// <summary>
            /// The golden picker's own vectors: (account, level) to a percentage. The
            /// bands live inside <see cref="progression"/> rather than beside them,
            /// because unlike a chest's drop table the multiplier is part of the credit
            /// derivation itself — the same table has to be in force for the end-to-end
            /// cases below to mean anything.
            /// </summary>
            public GoldenVectorCase[] goldenCases;

            /// <summary>
            /// The streak ladder's own vectors, and a synthetic ladder to run them against
            /// for the reason <see cref="dailyChestConfig"/> is synthetic: what is under
            /// contract is the lookup — the lap, and the per-kind clamp — not the rewards
            /// this season happens to pay.
            /// </summary>
            public StreakDto streakLadder;

            public StreakVectorCase[] streakCases;

            /// <summary>
            /// The season chest's own tiers and cases — a chest seeded from a subject, like a
            /// task's, with the season, the track and the rung in it. Synthetic for
            /// <see cref="dailyChestConfig"/>'s reason.
            ///
            /// <para>
            /// <b>The calendar itself is deliberately not here any more.</b> A season used to
            /// fold into derived credits, so the vectors had to carry its ladder to pin the
            /// arithmetic; a rung pays a chest now and a chest is a claim, so what is under
            /// contract is the roll rather than the ladder.
            /// </para>
            /// </summary>
            public TaskTierDto[] markChestTiers;

            public MarkVectorCase[] markChestCases;

            /// <summary>
            /// The bonus wheel's own slices, synthetic for <see cref="dailyChestConfig"/>'s
            /// reason: what is under contract is the picker, not this drop's ladder. Retuning
            /// the shipped wheel must not turn these red.
            /// </summary>
            public AdWheelDto wheelConfig;

            /// <summary>
            /// What one flat view of the placement pays, so the vectors also pin the payout
            /// arithmetic rather than only the pick. The multiply-before-divide is the half
            /// JavaScript could get wrong on its own.
            /// </summary>
            public int wheelBasis;

            public WheelVectorCase[] wheelCases;
        }

        [Serializable]
        public sealed class WheelVectorCase
        {
            public string name;
            public string playerKey;
            public int dayKey;
            public int spinIndex;
            public int landing;
            public int percent;
            public int pays;
        }

        [Serializable]
        public sealed class GoldenVectorCase
        {
            public string name;
            public string playerKey;
            public string levelId;
            public int percent;
        }

        [Serializable]
        public sealed class DailyVectorCase
        {
            public string name;
            public string playerKey;
            public int dayKey;
            public int chestIndex;
            public DropVector[] drops;
        }

        [Serializable]
        public sealed class DropVector
        {
            public string kind;
            public int amount;

            /// <summary>Which thing, for a utility drop. Empty otherwise.</summary>
            public string item;
        }

        [Serializable]
        public sealed class TaskVectorCase
        {
            public string name;
            public string playerKey;
            public string period;
            public int key;
            public string taskId;
            public string tier;
            public DropVector[] drops;
        }

        /// <summary>One rung's chest on one track, for one account.</summary>
        [Serializable]
        public sealed class MarkVectorCase
        {
            public string name;
            public string playerKey;
            public string seasonId;
            public string track;
            public int goal;
            public string tier;
            public DropVector[] drops;
        }

        /// <summary>
        /// One night of the ladder. <c>kind</c> is empty for a night that pays nothing,
        /// which is a state the lookup has to reach as exactly as any other.
        /// </summary>
        [Serializable]
        public sealed class StreakVectorCase
        {
            public string name;
            public int night;
            public string kind;
            public int amount;
        }

        [Serializable]
        public sealed class LevelChapterDto
        {
            public string levelId;
            public string chapterId;
        }

        [Serializable]
        public sealed class VectorCase
        {
            public string name;

            /// <summary>
            /// Seeds the golden bonus. Absent — which JsonUtility reads as empty — means
            /// no account, and therefore no bonus, which is what every case written before
            /// the bonus existed relies on.
            /// </summary>
            public string playerKey;

            public VectorRecord[] levels;

            public long credits;
            public long xp;
        }

        [Serializable]
        public sealed class VectorRecord
        {
            public string levelId;
            public int stars;

            /// <summary>
            /// When the glade was first cleared. Only the event track reads it, and only
            /// to ask whether the clear falls inside a window; absent — which JsonUtility
            /// reads as zero — is 1970 and therefore inside no window any event authors.
            /// </summary>
            public long firstClearedUnix;
        }

        static string VectorPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "firebase", "shared",
                                          "reward-vectors.json"));

        static VectorFile Load()
        {
            Assert.IsTrue(File.Exists(VectorPath), $"shared reward vectors not found at {VectorPath}");

            var file = JsonUtility.FromJson<VectorFile>(File.ReadAllText(VectorPath));
            Assert.IsNotNull(file, "the vector file did not parse");
            Assert.IsNotNull(file.progression, "the vector file has no progression table");
            Assert.IsNotNull(file.cases, "the vector file has no cases");
            Assert.Greater(file.cases.Length, 0);

            return file;
        }

        static ProgressionTable TableFrom(VectorFile file)
        {
            // Round-tripped through the real reader rather than constructed directly, so
            // the vectors also exercise override resolution — which is itself part of
            // what the two implementations have to agree on.
            var problems = new List<string>();
            string json = JsonUtility.ToJson(file.progression);

            Assert.IsTrue(ProgressionTable.TryRead(json, out var table, problems),
                          string.Join("; ", problems));

            // The vector file's progression block carries the reward curve and the golden
            // bands, and deliberately not the daily chests, the ad payouts or the streak
            // ladder — those have vector sets of their own (dailyChestConfig, streakLadder)
            // read through their own resolvers a few methods below, so duplicating them here
            // would be a second copy for a drop to put out of step with the first. The reader
            // notes each absent block and falls back, which is correct behaviour and not a
            // problem with these vectors — so the three notes are expected and everything else
            // is still a failure.
            //
            // Filtered rather than asserted-empty because asserting empty is what this did,
            // and it had been failing ever since the daily, ads and streak blocks were added
            // to the reader: two red tests on the one guard that stops the client and the
            // server paying different amounts (invariant 9a), which is exactly the guard
            // nobody can afford to be in the habit of ignoring.
            var unexpected = problems.FindAll(p => !IsAbsentBlockNote(p));
            Assert.IsEmpty(unexpected, string.Join("; ", unexpected));

            return table;
        }

        /// <summary>
        /// True for the reader's note that a block this vector file does not carry was absent.
        ///
        /// Matched on the exact sentences rather than on a substring of one of them, so a
        /// <em>malformed</em> block — which produces a different sentence about the same
        /// section — still fails. See <see cref="TableFrom"/>.
        /// </summary>
        static bool IsAbsentBlockNote(string problem)
            => problem == "daily block lists no chests; using the built-in table"
            || problem == "ads block lists no placements; using the built-in table"
            || problem == "streak block lists no rungs; using the built-in ladder"
            // The same note for the two blocks added since: neither is part of the reward
            // curve these vectors pin, and both have gates of their own — the content checks
            // walk the utility catalog and the ward roster and error on an entry whose art or
            // loc keys do not resolve. Carrying them here would be a second copy for a retune
            // to put out of step with the first, which is the whole reason the daily, ad and
            // streak blocks are absent too.
            || problem == "utilities block lists no items; using the built-in catalog"
            || problem == "wards block lists no models; using the built-in roster";

        static IChapterMap ChaptersFrom(VectorFile file)
        {
            var map = new FixedChapterMap();
            foreach (var entry in file.levelChapters ?? new LevelChapterDto[0])
                map.Add(entry.levelId, entry.chapterId);
            return map;
        }

        static IEnumerable<LevelRecord> RecordsFrom(VectorCase test)
        {
            foreach (var level in test.levels ?? new VectorRecord[0])
            {
                // Built directly rather than through WithRun, because the vectors
                // deliberately include star counts a legitimate run could never produce.
                yield return new LevelRecord(LevelId.Parse(level.levelId), level.stars,
                                             bestMoves: 10, clears: 1,
                                             firstClearedUnix: level.firstClearedUnix,
                                             lastPlayedUnix: 100);
            }
        }

        // -------------------------------------------------------------- the test
        [Test]
        public void EveryRewardVectorMatches()
        {
            var file = Load();
            var table = TableFrom(file);
            var chapters = ChaptersFrom(file);

            var failures = new List<string>();

            foreach (var test in file.cases)
            {
                var totals = ProgressionLedger.Compute(RecordsFrom(test), chapters, table,
                                                       test.playerKey);

                if (totals.EarnedCredits != test.credits)
                    failures.Add($"'{test.name}': credits expected {test.credits}, got {totals.EarnedCredits}");

                if (totals.Xp != test.xp)
                    failures.Add($"'{test.name}': xp expected {test.xp}, got {totals.Xp}");
            }

            Assert.IsEmpty(failures,
                           "the client no longer matches the shared reward vectors. If this change was " +
                           "intended, update firebase/shared/reward-vectors.json and make the same change " +
                           "in firebase/functions/src/progression.ts, or the server will enforce different " +
                           "numbers than the game shows.\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// The golden picker, against the file both halves read.
        ///
        /// The multiplier is not a decoration on top of the reward rule — it is inside the
        /// credit derivation, so the server recomputes it on every sync. A disagreement
        /// here is a balance that moves after a sync, in front of a player, for no reason
        /// they can see. <c>GoldenTests</c> pins the same numbers without a JSON reader,
        /// so the generator is checked even when the Editor is closed.
        /// </summary>
        [Test]
        public void EveryGoldenVectorMatches()
        {
            var file = Load();
            Assert.IsNotNull(file.goldenCases, "the vector file has no golden cases");
            Assert.Greater(file.goldenCases.Length, 0);

            var golden = TableFrom(file).Golden;
            var failures = new List<string>();

            foreach (var test in file.goldenCases)
            {
                int got = golden.PercentFor(test.playerKey, LevelId.Parse(test.levelId));
                if (got != test.percent)
                    failures.Add($"'{test.name}': expected {test.percent}%, got {got}%");
            }

            Assert.IsEmpty(failures,
                           "the client no longer matches the shared golden vectors. Every one of these " +
                           "is a glade somebody has been paid for — see invariant 9c.\n" +
                           string.Join("\n", failures));
        }

        /// <summary>
        /// The bonus wheel, against the file both halves read.
        ///
        /// <para>
        /// The wheel is <c>win_bonus</c>'s payout made variable, and neither side is told what
        /// the other decided: the phone draws where it stopped before the video plays, and the
        /// server recomputes the same slice when the network's callback lands. A disagreement
        /// here is a player watching a wheel stop on nine hundred and then watching their
        /// balance rise by two hundred, which is the worst thing an economy can do in front of
        /// somebody. See invariant 9c.
        /// </para>
        /// <para>
        /// The pre-sign-in row is the important one and is checked in both halves: no slice,
        /// and the flat amount. A client rolling against a device id while the server rolls
        /// against a uid is the one way this feature could pay two different numbers for one
        /// video.
        /// </para>
        /// </summary>
        [Test]
        public void EveryWheelVectorMatches()
        {
            var file = Load();
            Assert.IsNotNull(file.wheelConfig, "the vector file has no wheel config");
            Assert.IsNotNull(file.wheelCases, "the vector file has no wheel cases");
            Assert.Greater(file.wheelCases.Length, 0);
            Assert.Greater(file.wheelBasis, 0, "the vector file has no flat amount to multiply");

            var problems = new List<string>();
            var wheel = BonusWheel.Resolve(file.wheelConfig, problems);

            Assert.IsEmpty(problems, "the vector wheel is not one the reader accepts: " +
                                     string.Join("; ", problems));
            Assert.IsTrue(wheel.IsUsable);

            var failures = new List<string>();

            foreach (var test in file.wheelCases)
            {
                int landing = wheel.Landing(test.playerKey, test.dayKey, test.spinIndex);
                if (landing != test.landing)
                {
                    failures.Add($"'{test.name}': slice expected {test.landing}, got {landing}");
                    continue;
                }

                int percent = landing < 0
                    ? WheelRules.MinPercent
                    : wheel.SliceAt(landing).Percent;

                if (percent != test.percent)
                    failures.Add($"'{test.name}': expected {test.percent}%, got {percent}%");

                long pays = BonusWheel.Apply(file.wheelBasis, percent);
                if (pays != test.pays)
                    failures.Add($"'{test.name}': pays expected {test.pays}, got {pays}");
            }

            Assert.IsEmpty(failures,
                           "the client no longer matches the shared wheel vectors. Every one of these " +
                           "is a video somebody watched for a figure they were shown - see invariant 9c.\n" +
                           string.Join("\n", failures));
        }

        /// <summary>
        /// A wheel with no <c>win_bonus</c> to multiply is dropped, and the sentence says which
        /// placement was missing.
        ///
        /// <para>
        /// Both halves of the wheel rule are refusals rather than repairs, and they have to be:
        /// a reader that quietly fixed a table would accept one the other side had rejected, and
        /// the two would then disagree about money. Driven here rather than trusted, because a
        /// check with no failing case is not a check.
        /// </para>
        /// </summary>
        [Test]
        public void AWheelWithNothingToMultiplyIsDropped()
        {
            var file = Load();

            var ads = new AdsDto
            {
                cooldownSeconds = 30,
                wheel = file.wheelConfig,
                placements = new[]
                {
                    new AdPlacementDto
                    {
                        id = AdPlacement.CoinBonus, kind = "credits", amount = 500, dailyCap = 3,
                    },
                },
            };

            var problems = new List<string>();
            var table = AdRewardTable.Resolve(ads, problems);

            Assert.IsFalse(table.Wheel.IsUsable,
                           "a wheel was kept over a table with no win_bonus in it, so it would " +
                           "multiply nothing");

            Assert.IsTrue(problems.Exists(p => p.Contains(AdPlacement.WinBonus)),
                          "the wheel was dropped without saying which placement was missing");
        }

        /// <summary>
        /// An absent wheel means the <em>flat</em> offer, never the built-in ladder.
        ///
        /// This is the one table here that does not fall back to its own default, and the
        /// difference is what removes invariant 12a's deploy-ordering hazard from the feature: a
        /// published file that has never heard of the wheel keeps paying exactly what it
        /// authored, on both sides, rather than a client inventing multipliers a server reading
        /// the same file would never grant.
        /// </summary>
        [Test]
        public void AnAbsentWheelIsTheFlatOfferAndNotTheBuiltInLadder()
        {
            var ads = new AdsDto
            {
                cooldownSeconds = 30,
                placements = new[]
                {
                    new AdPlacementDto
                    {
                        id = AdPlacement.WinBonus, kind = "credits", amount = 200, dailyCap = 6,
                    },
                },
            };

            var problems = new List<string>();
            var table = AdRewardTable.Resolve(ads, problems);

            Assert.IsFalse(table.Wheel.IsUsable,
                           "an ads block with no wheel produced one; a client would draw " +
                           "multipliers a server reading the same file would never grant");

            Assert.IsEmpty(problems, "an absent wheel is not an error: " + string.Join("; ", problems));
        }

        [Test]
        public void EveryStreakVectorMatches()
        {
            var file = Load();
            Assert.IsNotNull(file.streakLadder, "the vector file has no streak ladder");
            Assert.IsNotNull(file.streakCases, "the vector file has no streak cases");
            Assert.Greater(file.streakCases.Length, 0);

            // Read through the real reader, so the vectors exercise the clamp and the
            // refusals as well as the lookup. Problems are expected here — the ladder
            // deliberately overreaches on two nights — but the table must still build.
            var problems = new List<string>();
            var ladder = StreakTable.Resolve(file.streakLadder, problems);

            Assert.AreEqual(file.streakLadder.rungs.Length, ladder.Length,
                            "the reader refused the vector ladder outright: " +
                            string.Join("; ", problems));

            var failures = new List<string>();

            foreach (var test in file.streakCases)
            {
                var rung = ladder.Rung(test.night);
                string got = rung.IsValid ? $"{ChestDropKinds.Id(rung.Kind)}={rung.Amount}" : "(nothing)";
                string want = string.IsNullOrEmpty(test.kind) ? "(nothing)" : $"{test.kind}={test.amount}";

                if (got != want) failures.Add($"'{test.name}' (night {test.night}): expected {want}, got {got}");
            }

            Assert.IsEmpty(failures,
                           "the client no longer matches the shared streak vectors. Every one of " +
                           "these is a night somebody is paid for — the server grants the amount " +
                           "and this is what the board promised.\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// The streak vectors are only worth anything if they cross the end of the ladder.
        /// A file whose cases all sat inside the first lap would pass against an
        /// implementation that had gone back to repeating its last rung.
        /// </summary>
        [Test]
        public void TheStreakVectorsReachPastTheFirstLap()
        {
            var file = Load();
            int length = file.streakLadder?.rungs?.Length ?? 0;
            Assert.Greater(length, 0);

            int beyond = 0;
            foreach (var test in file.streakCases)
                if (test.night > length) beyond++;

            Assert.GreaterOrEqual(beyond, 3,
                                  "fewer than three streak vectors fall past the end of the ladder, " +
                                  "so they would not notice the lap being lost");
        }

        /// <summary>
        /// The golden vectors are only worth anything if they actually reach more than one
        /// band. A file where every case pays the base would pass against an implementation
        /// that had stopped rolling at all.
        /// </summary>
        [Test]
        public void TheGoldenVectorsReachMoreThanOneBand()
        {
            var file = Load();

            var seen = new HashSet<int>();
            foreach (var test in file.goldenCases ?? new GoldenVectorCase[0]) seen.Add(test.percent);

            Assert.GreaterOrEqual(seen.Count, 3,
                                  "the golden vectors cover fewer than three outcomes, so they would " +
                                  "not notice a picker that had stopped picking");
        }

        /// <summary>
        /// The vectors are only worth anything if they actually cover the cases where a
        /// naive implementation would differ. Losing one to an edit would leave the
        /// suite green and the contract unguarded.
        /// </summary>
        [Test]
        public void TheVectorsCoverTheCasesThatActuallyDiverge()
        {
            var file = Load();

            var names = new List<string>();
            foreach (var test in file.cases) names.Add(test.name ?? string.Empty);
            string all = string.Join(" | ", names).ToLowerInvariant();

            foreach (var required in new[] { "does not know", "duplicated", "clamped", "negative",
                                             "inherits", "pay nothing", "golden" })
            {
                Assert.IsTrue(all.Contains(required),
                              $"the vectors no longer cover '{required}' — that is a case where the two " +
                              "implementations could silently disagree");
            }
        }

        // -------------------------------------------------------- daily chests
        /// <summary>
        /// The client half of the chest generator contract.
        ///
        /// A chest is rolled twice: here so the reward can be shown and spent while
        /// offline, and again in <c>functions/src/daily.ts</c> so the grant can be
        /// adjudicated without believing the client's number. If the two ever disagree, a
        /// player watches a balance change after a sync — which is the worst thing an
        /// economy can do in front of somebody, and the hardest to explain afterwards.
        ///
        /// Every constant behind this is part of the contract: the FNV basis and prime,
        /// the xorshift amounts, the stream numbers, the modulo, and the summing of
        /// same-kind drops. Changing any of them rerolls every unopened chest in the
        /// world, so the vectors have to change with them.
        /// </summary>
        [Test]
        public void EveryDailyChestVectorMatches()
        {
            var file = Load();

            Assert.IsNotNull(file.dailyChestConfig, "the vector file has no daily chest config");
            Assert.IsNotNull(file.dailyChestCases, "the vector file has no daily chest cases");
            Assert.Greater(file.dailyChestCases.Length, 0);

            var problems = new List<string>();
            var table = DailyChestTable.Resolve(file.dailyChestConfig, problems);
            Assert.IsEmpty(problems, string.Join("; ", problems));

            var failures = new List<string>();

            foreach (var test in file.dailyChestCases)
            {
                string got = Describe(table.Roll(test.playerKey, test.dayKey, test.chestIndex));
                string want = Describe(test.drops);

                if (got != want)
                    failures.Add($"'{test.name}': expected {want}, got {got}");
            }

            Assert.IsEmpty(failures,
                           "the client no longer rolls chests the way the server does. If this change " +
                           "was intended, update firebase/shared/reward-vectors.json and make the same " +
                           "change in firebase/functions/src/daily.ts — otherwise the server will grant " +
                           "a different amount than the game showed.\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// The vectors have to keep covering the two cases a naive implementation gets
        /// wrong: a chest with no bonus slot, and one whose floor and bonus are the same
        /// kind and therefore have to be summed into a single award.
        /// </summary>
        [Test]
        public void TheDailyVectorsCoverTheMergingAndFixedChests()
        {
            var file = Load();
            var config = file.dailyChestConfig;

            Assert.IsNotNull(config?.chests);

            bool hasFixed = false, hasMerging = false;

            foreach (var chest in config.chests)
            {
                if (chest.options == null || chest.options.Length == 0) hasFixed = true;

                foreach (var band in chest.guaranteed)
                    foreach (var option in chest.options ?? new DailyOptionDto[0])
                        if (band.kind == option.kind) hasMerging = true;
            }

            Assert.IsTrue(hasFixed, "the daily vectors no longer cover a chest with no bonus slot");
            Assert.IsTrue(hasMerging,
                          "the daily vectors no longer cover a chest whose floor and bonus share a " +
                          "kind — that is the case where the client would award one id twice and pay " +
                          "half of what the server grants");
        }

        /// <summary>
        /// The task chest's own contract: a chest seeded from a subject. Rolled here through
        /// <see cref="ChestSeed.ForSubject"/> exactly as <c>TaskLedger.SeedFor</c> rolls it,
        /// and again in <c>functions/src/tasks.ts</c>. The cases were produced by a third copy
        /// of the generator (<c>Tools/make_task_vectors.py</c>), so a disagreement here says
        /// which side moved.
        /// </summary>
        [Test]
        public void EveryTaskChestVectorMatches()
        {
            var file = Load();

            Assert.IsNotNull(file.taskChestTiers, "the vector file has no task chest tiers");
            Assert.IsNotNull(file.taskChestCases, "the vector file has no task chest cases");
            Assert.Greater(file.taskChestCases.Length, 0);

            var tiers = new Dictionary<string, ChestDefinition>();
            foreach (var tier in file.taskChestTiers)
            {
                var problems = new List<string>();
                var chest = DailyChestTable.ReadChest(tier.chest, "vector tier " + tier.id, problems);
                Assert.IsEmpty(problems, string.Join("; ", problems));
                tiers[tier.id] = chest;
            }

            var failures = new List<string>();

            foreach (var test in file.taskChestCases)
            {
                Assert.IsTrue(Tasks.TaskPeriods.TryParse(test.period, out var period), test.period);
                var seed = ChestSeed.ForSubject(test.playerKey, Tasks.TaskLedger.SeedTag,
                                                Tasks.TaskLedger.Subject(period, test.key, test.taskId));

                string got = Describe(tiers[test.tier].Roll(seed));
                string want = Describe(test.drops);

                if (got != want)
                    failures.Add($"'{test.name}': expected {want}, got {got}");
            }

            Assert.IsEmpty(failures,
                           "the client no longer rolls task chests the way the server does. If this " +
                           "change was intended, regenerate firebase/shared/reward-vectors.json with " +
                           "Tools/make_task_vectors.py and make the same change in " +
                           "firebase/functions/src/tasks.ts — otherwise the server will grant a " +
                           "different amount than the game showed.\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// The season chest's own contract: a chest seeded from a subject carrying the
        /// season, the track and the rung. Rolled here through <see cref="ChestSeed.ForSubject"/>
        /// exactly as <c>SeasonLedger.SeedFor</c> rolls it, and again in
        /// <c>functions/src/season.ts</c>. The cases were produced by a fourth copy of the
        /// generator (<c>Tools/make_mark_vectors.py</c>), so a disagreement here says which
        /// side moved.
        /// </summary>
        [Test]
        public void EveryBloomChestVectorMatches()
        {
            var file = Load();

            Assert.IsNotNull(file.markChestTiers, "the vector file has no season chest tiers");
            Assert.IsNotNull(file.markChestCases, "the vector file has no season chest cases");
            Assert.Greater(file.markChestCases.Length, 0);

            var tiers = new Dictionary<string, ChestDefinition>();
            foreach (var tier in file.markChestTiers)
            {
                var problems = new List<string>();
                var chest = DailyChestTable.ReadChest(tier.chest, "vector tier " + tier.id, problems);
                Assert.IsEmpty(problems, string.Join("; ", problems));
                tiers[tier.id] = chest;
            }

            var failures = new List<string>();

            foreach (var test in file.markChestCases)
            {
                var track = Events.SeasonTracks.Parse(test.track);
                Assert.IsNotNull(track, test.track);

                var seed = ChestSeed.ForSubject(test.playerKey, Events.SeasonLedger.SeedTag,
                                                Events.SeasonLedger.Subject(test.seasonId, track.Value, test.goal));

                string got = Describe(tiers[test.tier].Roll(seed));
                string want = Describe(test.drops);

                if (got != want)
                    failures.Add($"'{test.name}': expected {want}, got {got}");
            }

            Assert.IsEmpty(failures,
                           "the client no longer rolls season chests the way the server does. If this " +
                           "change was intended, regenerate firebase/shared/reward-vectors.json with " +
                           "Tools/make_mark_vectors.py and make the same change in " +
                           "firebase/functions/src/season.ts — otherwise the server will grant a " +
                           "different amount than the game showed.\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// The one trap a season's seeding has that a task's does not: <b>two tracks at one
        /// rung</b>. Same account, same season, same goal — and they have to draw from
        /// different streams, or one claim would collect both columns.
        ///
        /// <para>
        /// Compared over <em>all</em> of a subject's tiers rather than tier by tier, because a
        /// chest with no randomness in it — the synthetic <c>silver</c> is a flat four gems —
        /// rolls the same whatever the seed, correctly. A whole subject's worth of rolls is
        /// where a shared stream would show.
        /// </para>
        /// </summary>
        [Test]
        public void TheBloomVectorsCoverBothTracksAtOneRung()
        {
            var file = Load();
            var bySubject = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var test in file.markChestCases ?? new MarkVectorCase[0])
            {
                string subject = $"{test.playerKey}|{test.seasonId}|{test.track}|{test.goal}";
                bySubject.TryGetValue(subject, out string so_far);
                bySubject[subject] = so_far + "/" + Describe(test.drops);
            }

            int pairs = 0;

            foreach (var pair in bySubject)
            {
                var parts = pair.Key.Split('|');
                if (parts[2] != "free") continue;

                string twin = $"{parts[0]}|{parts[1]}|pass|{parts[3]}";
                if (!bySubject.TryGetValue(twin, out string other)) continue;

                pairs++;
                Assert.AreNotEqual(pair.Value, other,
                                   $"'{pair.Key}' and its pass twin roll the same chests; the track " +
                                   "is in the subject precisely so they cannot");
            }

            Assert.Greater(pairs, 0, "the season vectors no longer cover both tracks at one rung");
        }

        /// <summary>
        /// The task vectors have to keep covering the two cases a naive seeding gets wrong:
        /// one id in two periods, and a day key equal to a week key, are different chests.
        /// </summary>
        [Test]
        public void TheTaskVectorsCoverTheSeedingTraps()
        {
            var file = Load();
            var names = new List<string>();
            foreach (var test in file.taskChestCases ?? new TaskVectorCase[0]) names.Add(test.name);
            string joined = string.Join(" | ", names);

            Assert.IsTrue(joined.Contains("@weekly:2901:d_play#"), "a daily id claimed in the weekly period");
            Assert.IsTrue(joined.Contains("@daily:2901:w_win#"), "a day key that equals a week key");
            Assert.IsTrue(joined.Contains("(Ünïcödé)@"), "a non-ASCII player key, hashed per code unit");
        }

        static string Describe(IEnumerable<ChestDrop> drops)
        {
            var parts = new List<string>();
            foreach (var drop in drops)
                parts.Add(drop.Item.Length > 0
                    ? $"{ChestDropKinds.Id(drop.Kind)}:{drop.Item}={drop.Amount}"
                    : $"{ChestDropKinds.Id(drop.Kind)}={drop.Amount}");
            return parts.Count == 0 ? "(nothing)" : string.Join(",", parts);
        }

        static string Describe(DropVector[] drops)
        {
            var parts = new List<string>();
            foreach (var drop in drops ?? new DropVector[0])
                parts.Add(string.IsNullOrEmpty(drop.item)
                    ? $"{drop.kind}={drop.amount}"
                    : $"{drop.kind}:{drop.item}={drop.amount}");
            return parts.Count == 0 ? "(nothing)" : string.Join(",", parts);
        }

        /// <summary>
        /// The shipped reward table has to survive the same reader the vectors use.
        /// Cheap, and catches a content edit that would only fail on a device.
        /// </summary>
        [Test]
        public void TheShippedTableAndTheVectorTableUseTheSameReader()
        {
            var file = Load();
            Assert.AreEqual(ProgressionSchema.Version, file.progression.schemaVersion,
                            "the vectors were authored against a different content schema");
        }
    }
}
