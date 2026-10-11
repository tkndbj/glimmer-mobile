using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The client half of the chapter standing's shared contract (invariant 62), and the
    /// table it is read against.
    ///
    /// <para>
    /// A finished chapter's score is derived twice - here, to fold the player's own save on the
    /// handset, and in <c>functions/src/standings.ts</c>, to fold everybody else's into the cut
    /// points this is compared against. Two copies of one rule drift, and this one drifts
    /// silently: a player is told a number that is quietly about a different population. So
    /// both run <c>standingCases</c> in <c>firebase/shared/grove-vectors.json</c>;
    /// <c>firebase/functions/test/standings.mjs</c> is the other half.
    /// </para>
    /// </summary>
    public sealed class ChapterStandingTests
    {
        // ------------------------------------------------------------- the file
        sealed class StandingCase
        {
            public string Name;
            public List<LevelId> Chapter;
            public Dictionary<LevelId, LevelRecord> Records;
            public long? Score;

            public LevelRecord RecordOf(LevelId id)
                => Records.TryGetValue(id, out var record) ? record : LevelRecord.Empty(id);

            public static StandingCase From(Dictionary<string, object> map)
            {
                var chapter = new List<LevelId>();
                foreach (object raw in TestJson.Children(map, "chapter"))
                    chapter.Add(LevelId.Parse((string)raw));

                var records = new Dictionary<LevelId, LevelRecord>();
                foreach (var pair in TestJson.Child(map, "levels"))
                {
                    var entry = TestJson.Object(pair.Value);
                    var id = LevelId.Parse(pair.Key);
                    records[id] = new LevelRecord(id, TestJson.Int(entry, "stars"), TestJson.Int(entry, "bestMoves"),
                                                  1, 100L, 100L);
                }

                long? score = null;
                if (map.TryGetValue("score", out object rawScore) && rawScore != null)
                    score = TestJson.Long(map, "score");

                return new StandingCase
                {
                    Name = TestJson.Str(map, "name", "(unnamed)"),
                    Chapter = chapter,
                    Records = records,
                    Score = score,
                };
            }
        }

        static List<StandingCase> Load(out Dictionary<string, object> constants)
        {
            var file = TestJson.ReadShared("grove-vectors.json");
            constants = TestJson.Child(file, "standingConstants");

            var cases = new List<StandingCase>();
            foreach (object raw in TestJson.Children(file, "standingCases"))
                cases.Add(StandingCase.From(TestJson.Object(raw)));

            Assert.Greater(cases.Count, 0, "the vector file has no standing cases");
            return cases;
        }

        [SetUp]
        public void Reset()
        {
            ChapterStandings.Clear();
            PlayerProgress.LoadFrom(new SaveFileDto());
        }

        [TearDown]
        public void Restore()
        {
            ChapterStandings.Clear();
            PlayerProgress.LoadFrom(new SaveFileDto());
        }

        // ------------------------------------------------------- the shared contract
        [Test]
        public void EveryVectorCaseScoresTheWayTheServerScores()
        {
            var cases = Load(out _);
            var failures = new List<string>();

            foreach (var c in cases)
            {
                long? got = ChapterStanding.ScoreOf(c.Chapter, c.RecordOf);
                if (got != c.Score)
                    failures.Add($"{c.Name}: expected {(c.Score.HasValue ? c.Score.Value.ToString() : "null")}, " +
                                 $"got {(got.HasValue ? got.Value.ToString() : "null")}");
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void TheConstantsAreTheOnesTheVectorsWereDrawnWith()
        {
            Load(out var constants);

            Assert.AreEqual(ChapterStanding.StarWeight, TestJson.Long(constants, "starWeight"));
            Assert.AreEqual(ChapterStanding.MovesCap, TestJson.Int(constants, "movesCap"));
            Assert.AreEqual(LevelRecord.MaxStars, TestJson.Int(constants, "maxStars"));
        }

        // ------------------------------------------------------- off the save itself
        static readonly ChapterIndexEntry Chapter = new ChapterIndexEntry(
            ChapterId.Parse("s01_test"), 10, 1,
            new[] { LevelId.Parse("s01_a"), LevelId.Parse("s01_b") });

        static SaveFileDto SaveWith(params (string id, int stars, int moves)[] levels)
        {
            var dto = new SaveFileDto();
            var rows = new List<LevelRecordDto>();
            foreach (var (id, stars, moves) in levels)
                rows.Add(new LevelRecordDto { levelId = id, stars = stars, bestMoves = moves, clears = 1 });
            dto.levels = rows.ToArray();
            return dto;
        }

        [Test]
        public void AChapterHalfDoneHasNoScoreAndAFinishedOneHasTheFoldedScore()
        {
            PlayerProgress.LoadFrom(SaveWith(("s01_a", 3, 10)));
            Assert.IsNull(ChapterStanding.ScoreOf(Chapter));

            PlayerProgress.LoadFrom(SaveWith(("s01_a", 3, 10), ("s01_b", 2, 7)));
            Assert.AreEqual(5L * ChapterStanding.StarWeight - 17L, ChapterStanding.ScoreOf(Chapter));

            Assert.IsNull(ChapterStanding.ScoreOf((ChapterIndexEntry)null));
        }

        // ----------------------------------------------------------------- the table
        static StandingTable Table(int samples, Func<int, long> cutAt, int minimum = StandingTable.DefaultMinimumSamples)
        {
            var cuts = new List<long>(StandingTable.CutCount);
            for (int p = 1; p <= StandingTable.CutCount; p++) cuts.Add(cutAt(p));
            return new StandingTable(samples, minimum, cuts);
        }

        [Test]
        public void ATableNeedsNinetyNineAscendingCutsAndEnoughFinishers()
        {
            Assert.IsFalse(StandingTable.None.IsUsable);
            Assert.IsFalse(new StandingTable(500, 200, new long[] { 1, 2, 3 }).IsUsable, "three cuts is not a table");
            Assert.IsFalse(Table(StandingTable.DefaultMinimumSamples - 1, p => p * 10L).IsUsable, "one short of the minimum");
            Assert.IsTrue(Table(StandingTable.DefaultMinimumSamples, p => p * 10L).IsUsable, "on the minimum");
            Assert.IsFalse(Table(5000, p => p == 50 ? 0L : p * 10L).IsUsable, "a dip is not ascending");
            Assert.IsTrue(Table(5000, p => 7L).IsUsable, "every cut equal is ascending");

            // The publisher's own minimum governs; a document that omits it reads as the default.
            Assert.IsTrue(Table(20, p => p * 10L, minimum: 20).IsUsable);
            Assert.AreEqual(StandingTable.DefaultMinimumSamples, new StandingTable(1, 0, null).MinimumSamples);
        }

        [Test]
        public void TheShareAtOrBelowIsTheCountOfCutsAtOrBelow()
        {
            var table = Table(1000, p => p * 10L);          // p1 = 10 … p99 = 990

            Assert.AreEqual(0, table.PercentAtOrBelow(9), "under every cut");
            Assert.AreEqual(1, table.PercentAtOrBelow(10), "on the first cut");
            Assert.AreEqual(50, table.PercentAtOrBelow(505), "between p50 and p51");
            Assert.AreEqual(99, table.PercentAtOrBelow(990), "on the last cut");
            Assert.AreEqual(99, table.PercentAtOrBelow(long.MaxValue), "above every cut");
            Assert.AreEqual(-1, StandingTable.None.PercentAtOrBelow(500), "no table, nothing to say");
        }

        [Test]
        public void TopPercentIsTheComplementClampedToOneAndNinetyNine()
        {
            var table = Table(1000, p => p * 10L);

            Assert.AreEqual(StandingTable.MaxTop, table.TopPercent(1), "behind everybody is still top 99");
            Assert.AreEqual(50, table.TopPercent(505));
            Assert.AreEqual(StandingTable.MinTop, table.TopPercent(990), "on p99 is top 1");
            Assert.AreEqual(StandingTable.MinTop, table.TopPercent(long.MaxValue));
            Assert.AreEqual(-1, StandingTable.None.TopPercent(505));
        }

        [Test]
        public void ATiedScoreStandsAtTheTopOfItsOwnTie()
        {
            // Half the finishers on 10, half on 20: somebody on 10 is at or above half the
            // population, which is top 50 rather than top 99.
            var table = Table(1000, p => p <= 50 ? 10L : 20L);

            Assert.AreEqual(50, table.TopPercent(10));
            Assert.AreEqual(StandingTable.MinTop, table.TopPercent(20));
            Assert.AreEqual(StandingTable.MaxTop, table.TopPercent(9));
        }

        // ----------------------------------------------------------- what is shown
        [Test]
        public void TheMapSaysTopOneWhenTheTableCannotSay()
        {
            Assert.AreEqual(ChapterStanding.WhenUnknown, ChapterStanding.Shown(123, StandingTable.None));
            Assert.AreEqual(ChapterStanding.WhenUnknown, ChapterStanding.Shown(123, Table(10, p => p * 10L)), "too few finishers");
            Assert.AreEqual(50, ChapterStanding.Shown(505, Table(1000, p => p * 10L)));
        }

        [Test]
        public void APublicationIsHeldPerChapterAndReplacedWhole()
        {
            int raised = 0;
            Action onChanged = () => raised++;
            ChapterStandings.Changed += onChanged;
            try
            {
                Assert.IsFalse(ChapterStandings.IsLoaded);
                Assert.IsFalse(ChapterStandings.TableFor(Chapter.Id).IsUsable);

                ChapterStandings.Publish(new ChapterStandingPublication(
                    new Dictionary<string, StandingTable>
                    {
                        ["s01_test"] = Table(1000, p => p * 10L),
                        ["s02_other"] = Table(1000, p => p * 20L),
                    }, 1700000000L));

                Assert.IsTrue(ChapterStandings.IsLoaded);
                Assert.AreEqual(1, raised);
                Assert.AreEqual(1700000000L, ChapterStandings.BuiltUnix);
                Assert.IsTrue(ChapterStandings.TableFor(Chapter.Id).IsUsable);
                Assert.IsTrue(ChapterStandings.TableFor(ChapterId.Parse("s02_other")).IsUsable);

                // The second publication names one chapter; the other is gone, not kept.
                ChapterStandings.Publish(new ChapterStandingPublication(
                    new Dictionary<string, StandingTable> { ["s01_test"] = Table(1000, p => p * 10L) }, 1700003600L));

                Assert.IsTrue(ChapterStandings.TableFor(Chapter.Id).IsUsable);
                Assert.IsFalse(ChapterStandings.TableFor(ChapterId.Parse("s02_other")).IsUsable);
                Assert.IsFalse(ChapterStandings.TableFor(ChapterId.None).IsUsable);

                // Nothing at all is the honest "nothing to say", not an error.
                ChapterStandings.Publish(ChapterStandingPublication.None);
                Assert.IsTrue(ChapterStandings.IsLoaded);
                Assert.IsFalse(ChapterStandings.TableFor(Chapter.Id).IsUsable);
            }
            finally
            {
                ChapterStandings.Changed -= onChanged;
            }
        }
    }
}
