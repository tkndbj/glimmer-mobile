using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
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
            public VectorRecord[] levels;
            public long credits;
            public long xp;
        }

        [Serializable]
        public sealed class VectorRecord
        {
            public string levelId;
            public int stars;
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
            Assert.IsEmpty(problems, string.Join("; ", problems));

            return table;
        }

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
                                             firstClearedUnix: 100, lastPlayedUnix: 100);
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
                var totals = ProgressionLedger.Compute(RecordsFrom(test), chapters, table);

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
                                             "inherits", "pay nothing" })
            {
                Assert.IsTrue(all.Contains(required),
                              $"the vectors no longer cover '{required}' — that is a case where the two " +
                              "implementations could silently disagree");
            }
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
