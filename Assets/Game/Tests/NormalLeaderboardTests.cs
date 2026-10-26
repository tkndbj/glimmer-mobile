using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    public sealed class NormalLeaderboardTests
    {
        static NormalScores Scores(params NormalTargetDto[] rows)
            => NormalScores.Resolve(new NormalScoringDto { version = 1, levels = rows }, new List<string>());

        static CatalogIndex Index()
        {
            var builder = new CatalogIndexBuilder();
            builder.Add(new ManifestChapterDto { id = "s01_test", version = 1, order = 1,
                mode = "siege", levels = new[] { "s01_one", "s01_two" } }, 1);
            builder.Add(new ManifestChapterDto { id = "s02_test", version = 1, order = 2,
                mode = "siege", track = "infinite", levels = new[] { "s02_infinite" } }, 1);
            builder.Add(new ManifestChapterDto { id = "s03_test", version = 1, order = 3,
                mode = "siege", track = "shuffle", levels = new[] { "s03_shuffle" } }, 1);
            Assert.IsFalse(builder.HasProblems);
            return builder.Build();
        }

        [Test]
        public void PointsAgreeWithEveryServerVector()
        {
            foreach (var raw in TestJson.Children(TestJson.ReadShared("normal-score-vectors.json"), "points"))
            {
                var row = TestJson.Object(raw);
                Assert.AreEqual(TestJson.Int(row, "score"), NormalScores.Points(TestJson.Int(row, "stars"),
                    TestJson.Int(row, "moves"), TestJson.Int(row, "target")), TestJson.Str(row, "name"));
            }
        }

        [Test]
        public void ReplaysAndDeviceMergesCountOnlyIndependentBestsOnce()
        {
            var scores = Scores(new NormalTargetDto { level = "s01_one", target = 20 });
            var a = new SaveFileDto { levels = new[] { new LevelRecordDto { levelId = "s01_one", stars = 3, bestMoves = 20 } } };
            var b = new SaveFileDto { levels = new[] { new LevelRecordDto { levelId = "s01_one", stars = 2, bestMoves = 15, clears = 999 } } };
            var index = Index();
            Assert.AreEqual(1300, scores.In(a, index));
            Assert.AreEqual(1400, scores.In(SaveMerge.Join(a, b), index));
            Assert.AreEqual(1400, scores.In(SaveMerge.Join(b, a), index));
            Assert.AreEqual(1400, scores.In(SaveMerge.Join(SaveMerge.Join(a, b), b), index));
            Assert.AreEqual(1400, scores.In(new SaveFileDto { levels = new[] { a.levels[0], b.levels[0] } }, index));
        }

        [Test]
        public void InfiniteShuffleUnknownAndUnclearedRecordsCannotAddPoints()
        {
            var rows = new[] { "s02_infinite", "s03_shuffle", "s99_unknown", "s01_one" };
            var targets = new List<NormalTargetDto>();
            var records = new List<LevelRecordDto>();
            foreach (string id in rows)
            {
                targets.Add(new NormalTargetDto { level = id, target = 20 });
                records.Add(new LevelRecordDto { levelId = id, stars = id == "s01_one" ? 0 : 3, bestMoves = 1 });
            }
            Assert.AreEqual(0, Scores(targets.ToArray()).In(new SaveFileDto { levels = records.ToArray() }, Index()));
        }

        [Test]
        public void NormalOnlyPlayersQualifyAndImprovementChangesTheFingerprint()
        {
            var a = new GroveCard("uid", "Fern", 1, 0, 1L, normalScore: 1300);
            var b = new GroveCard("uid", "Fern", 1, 0, 2L, normalScore: 1400);
            Assert.IsTrue(GrovePublishPolicy.WorthPublishing(a));
            Assert.AreNotEqual(a.Fingerprint(), b.Fingerprint());
            Assert.AreEqual(a.Fingerprint(), new GroveCard("uid", "Fern", 1, 0, 99L, normalScore: 1300).Fingerprint());
            Assert.AreEqual(0, new GroveCard("uid", "Fern", 1, 0, 1L).NormalScore);
        }

        [Test]
        public void InvalidConfigFailsClosedAndDoesNotChangeSaves()
        {
            var problems = new List<string>();
            Assert.AreSame(NormalScores.Empty, NormalScores.Resolve(new NormalScoringDto { version = 2 }, problems));
            Assert.IsNotEmpty(problems);
            var duplicate = new NormalTargetDto { level = "s01_one", target = 20 };
            Assert.AreSame(NormalScores.Empty, Scores(duplicate, duplicate));
            Assert.AreEqual(0, NormalScores.Empty.In(null, Index()));
            Assert.IsTrue(LeaderboardBoard.IsKnown(LeaderboardBoard.Normal));
            Assert.AreEqual(0, new LeaderboardEntry(1, "uid", "Fern", "", 1).NormalScore);
        }
    }
}
