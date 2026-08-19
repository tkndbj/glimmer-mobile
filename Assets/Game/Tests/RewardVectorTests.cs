using System;
using System.Collections.Generic;
using System.IO;
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
            /// The event calendar. Top level rather than inside <see cref="progression"/>
            /// because it is a fact about the catalog — which glades, when — in the same
            /// sense <see cref="levelChapters"/> is, and the reward table has no field for
            /// it to land in.
            /// </summary>
            public ManifestEventDto[] events;
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

            /// <summary>
            /// How much of each event's track this player has collected. Absent — which
            /// JsonUtility reads as null — means nothing has been taken, and therefore that
            /// no event pays. That is the safe default rather than the convenient one: a
            /// case that means to be paid says so.
            /// </summary>
            public EventFloor[] collected;

            public long credits;
            public long xp;
        }

        [Serializable]
        public sealed class EventFloor
        {
            public string id;
            public int collectedGoal;
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
            || problem == "streak block lists no rungs; using the built-in ladder";

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

        /// <summary>
        /// The calendar, built through the same reader the game uses.
        ///
        /// Deliberately not hand-assembled: a track the builder would refuse — goals out of
        /// order, a milestone past the number of glades — must not be able to pass here and
        /// then be rejected in production.
        /// </summary>
        static IReadOnlyList<GroveEvent> EventsFrom(VectorFile file)
        {
            if (file.events == null || file.events.Length == 0) return null;

            var builder = new CatalogIndexBuilder();

            // The events name glades, and the builder drops an event whose glades no
            // chapter holds — so the chapters have to go in first.
            var byChapter = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var entry in file.levelChapters ?? new LevelChapterDto[0])
            {
                if (!byChapter.TryGetValue(entry.chapterId, out var list))
                    byChapter[entry.chapterId] = list = new List<string>();
                list.Add(entry.levelId);
            }

            int order = 0;
            foreach (var pair in byChapter)
                builder.Add(new ManifestChapterDto
                {
                    id = pair.Key, order = order += 10, version = 1, levels = pair.Value.ToArray(),
                }, 1);

            foreach (var groveEvent in file.events) builder.AddEvent(groveEvent);

            var index = builder.Build();

            Assert.AreEqual(file.events.Length, index.Events.Count,
                            "the vector file's events were not all accepted by the builder: " +
                            string.Join("; ", builder.Problems));

            return index.Events;
        }

        /// <summary>
        /// A case's collected floors, as the map the derivation wants.
        ///
        /// Never null, so a case that names none is asking to be paid nothing rather than
        /// falling through to some older behaviour — which is exactly the distinction the
        /// "a track nobody has collected pays nothing yet" case exists to pin.
        /// </summary>
        static Dictionary<string, int> FloorsFrom(VectorCase test)
        {
            var floors = new Dictionary<string, int>(StringComparer.Ordinal);
            if (test.collected == null) return floors;

            foreach (var floor in test.collected)
            {
                if (floor == null || string.IsNullOrEmpty(floor.id)) continue;
                floors[floor.id] = floor.collectedGoal;
            }

            return floors;
        }

        // -------------------------------------------------------------- the test
        [Test]
        public void EveryRewardVectorMatches()
        {
            var file = Load();
            var table = TableFrom(file);
            var chapters = ChaptersFrom(file);
            var events = EventsFrom(file);

            var failures = new List<string>();

            foreach (var test in file.cases)
            {
                var totals = ProgressionLedger.Compute(RecordsFrom(test), chapters, table,
                                                       test.playerKey, events, FloorsFrom(test));

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
                                             "inherits", "pay nothing", "golden",
                                             "outside its window", "whole track" })
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

        static string Describe(IEnumerable<ChestDrop> drops)
        {
            var parts = new List<string>();
            foreach (var drop in drops) parts.Add($"{ChestDropKinds.Id(drop.Kind)}={drop.Amount}");
            return parts.Count == 0 ? "(nothing)" : string.Join(",", parts);
        }

        static string Describe(DropVector[] drops)
        {
            var parts = new List<string>();
            foreach (var drop in drops ?? new DropVector[0]) parts.Add($"{drop.kind}={drop.amount}");
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
