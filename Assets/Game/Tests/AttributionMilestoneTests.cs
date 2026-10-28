using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Analytics;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// <c>l5_reached</c>: the first clear of the level content names, once per account, derived from
    /// the save. Raised beside <c>af_level_achieved</c>, which stays as it is - it fires on every win
    /// and ~96% of starters win the first board, so it is nearly an install event, and old builds
    /// keep sending it, so its meaning cannot move.
    /// </summary>
    public sealed class AttributionMilestoneTests
    {
        static readonly LevelId Named = LevelId.Parse("s01_warlordsgate");
        static readonly LevelId Other = LevelId.Parse("s01_thornhollow");

        sealed class Spy : IAnalyticsSink
        {
            public readonly List<string> Levels = new List<string>();

            public void Track(string eventName, IReadOnlyDictionary<string, object> properties)
            {
                if (eventName == AttributionMilestones.L5Reached) Levels.Add((string)properties["level_id"]);
            }
        }

        Spy _spy;

        [SetUp]
        public void Start()
        {
            Publish(new AttributionDto { l5Reached = Named.Value });
            PlayerProgress.LoadFrom(new SaveFileDto());
            Telemetry.Clear();
            _spy = new Spy();
            Telemetry.AddSink(_spy);
        }

        [TearDown]
        public void Restore()
        {
            Telemetry.Clear();
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
        }

        /// <summary><c>RunLedger.Win</c>'s order: the record read, the milestone noted, the record written.</summary>
        static void Win(LevelId level, int stars)
        {
            bool wasCleared = PlayerProgress.Record(level).IsCleared;
            AttributionMilestones.NoteWin(level, wasCleared, stars);
            PlayerProgress.RecordRun(level, stars, 20);
        }

        [Test]
        public void TheFirstClearOfTheNamedLevelIsSentOnceAndReplaysAreNot()
        {
            Win(Named, 2);
            Win(Named, 3);
            Win(Named, 1);
            CollectionAssert.AreEqual(new[] { Named.Value }, _spy.Levels);
        }

        [Test]
        public void OnlyTheNamedLevelSendsIt()
        {
            Win(Other, 3);
            Win(LevelId.Parse("s01_firstwatch"), 3);
            Assert.IsEmpty(_spy.Levels);
        }

        [Test]
        public void AWinWithoutStarsIsNotAClearAndTheLaterClearSendsIt()
        {
            Win(Named, 0);
            Assert.IsEmpty(_spy.Levels);
            Win(Named, 1);
            CollectionAssert.AreEqual(new[] { Named.Value }, _spy.Levels);
        }

        [Test]
        public void ARestoredSaveAlreadyHoldsTheClearSoAReinstallSendsNothing()
        {
            Win(Named, 3);
            var saved = new SaveFileDto();
            PlayerProgress.WriteInto(saved);

            PlayerProgress.LoadFrom(new SaveFileDto());   // reinstall
            PlayerProgress.LoadFrom(saved);               // the cloud restores the account
            Win(Named, 3);

            Assert.AreEqual(1, _spy.Levels.Count, "a restored account sent l5_reached a second time");
        }

        [Test]
        public void ADifferentAccountOnTheSameDeviceSendsItForItself()
        {
            Win(Named, 3);
            PlayerProgress.LoadFrom(new SaveFileDto());   // an account switch to a fresh account
            Win(Named, 2);
            Assert.AreEqual(2, _spy.Levels.Count);
        }

        [Test]
        public void AnAbsentOrFaultyBlockSendsNothing()
        {
            Publish(null);
            Win(Named, 3);
            Assert.IsEmpty(_spy.Levels);

            var problems = new List<string>();
            Assert.AreSame(AttributionTable.Empty, AttributionTable.Resolve(new AttributionDto { l5Reached = "Not An Id" }, problems));
            Assert.IsNotEmpty(problems, "a bad id was refused without saying so");
        }

        /// <summary>
        /// The shipped file names the level by id (invariant 1), and it is the siege ladder's fifth
        /// board today. Positions move; if this fails after a reshuffle, the owner decides whether the
        /// milestone moves too - the code never needs to.
        /// </summary>
        [Test]
        public void TheShippedBlockNamesTheLaddersFifthLevelById()
        {
            var root = TestJson.Object(TestJson.Parse(File.ReadAllText(ContentFile("progression.json"))));
            Assert.IsTrue(root.ContainsKey("attribution"), "progression.json carries no attribution block");
            string named = TestJson.Str(TestJson.Object(root["attribution"]), "l5Reached");
            Assert.AreEqual(Named.Value, named);

            var manifest = TestJson.Object(TestJson.Parse(File.ReadAllText(ContentFile("manifest.json"))));
            foreach (var child in TestJson.Children(manifest, "chapters"))
            {
                var row = TestJson.Object(child);
                if (TestJson.Str(row, "id") != "s01_thornwatch") continue;
                var levels = new List<string>();
                foreach (var level in TestJson.Children(row, "levels")) levels.Add((string)level);
                Assert.AreEqual(named, levels[4], "l5_reached no longer stands on the ladder's fifth level");
                return;
            }
            Assert.Fail("the siege ladder's first chapter is not in the manifest");
        }

        /// <summary>The ledger reads the record before it is written, which is the whole of "once".</summary>
        [Test]
        public void RunLedgerNotesTheMilestoneFromTheRecordBeforeTheRun()
        {
            string source = File.ReadAllText(Path.Combine(TestJson.RepoRoot(),
                "Assets", "Game", "Scripts", "Domain", "Board", "RunLedger.cs"));
            StringAssert.Contains("AttributionMilestones.NoteWin(level.Id, before.IsCleared, stars);", source);
        }

        static void Publish(AttributionDto attribution)
        {
            var problems = new List<string>();
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                maxLevel = 20,
                xpToNext = new[] { 100, 150 },
                tailXpToNext = 200,
                tailXpIncrement = 50,
                attribution = attribution,
            };
            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, problems), string.Join("; ", problems));
            ProgressionRules.Publish(table);
        }

        static string ContentFile(string name)
            => Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets", "Content", name);
    }
}
