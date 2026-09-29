using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The Infinite lane's checkpoints (MODES.md 43f): where a run may open, what it opens with,
    /// and what it is paid.
    ///
    /// <para>
    /// <b>Three promises, each held here and nowhere else.</b> A run opened at a checkpoint meets
    /// exactly the wave a run that walked there meets; it is paid for the waves it saw off and
    /// never for the ones it skipped; and a checkpoint is open only where the lane's best has
    /// been, whatever the device remembers choosing. Every offline gate that reads content is
    /// blind to all three, because the table is four numbers a row and the rules are arithmetic
    /// on the board.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class EndlessCheckpointTests
    {
        static readonly LevelId Lane = Id("s02_endless");

        [SetUp]
        public void Clean()
        {
            EndlessCheckpoints.UseStore(new EndlessCoins.MemoryStore());
            EndlessLedger.LoadFrom(new SaveFileDto());
        }

        [TearDown]
        public void Restore()
        {
            EndlessCheckpoints.UseStore(null);
            EndlessLedger.LoadFrom(new SaveFileDto());
            ProgressionRules.Reset();
        }

        static LevelId Id(string raw)
        {
            Assert.IsTrue(LevelId.TryParse(raw, out var id, out string error), error);
            return id;
        }

        static SiegeLayout Endless()
        {
            Assert.IsTrue(ProtoGrid.TryRead(new[]
            {
                "ryybgyyg",
                "bybgrgyy",
                "rbryyggr",
                "grgrgbbr",
                "yybgrrbg",
            }, 8, 5, SiegeLayout.Cells, out var grid, out string error), error);

            return new SiegeLayout(grid, "rgby", "rgby", null, null, 4,
                                   new SiegeEndless("rgby", 4, 20, .55f));
        }

        static SiegeLayout Laddered()
        {
            Assert.IsTrue(ProtoGrid.TryRead(new[]
            {
                "ryybgyyg",
                "bybgrgyy",
                "rbryyggr",
                "grgrgbbr",
                "yybgrrbg",
            }, 8, 5, SiegeLayout.Cells, out var grid, out string error), error);

            return new SiegeLayout(grid, "rgby", "rgby", new[] { "rgby", "rgbyrgby" }, null, 0);
        }

        static EndlessCheckpointsDto Rows(params (int wave, int unlockAt, int cogs)[] rows)
        {
            var dto = new EndlessCheckpointsDto { rows = new EndlessCheckpointDto[rows.Length] };
            for (int i = 0; i < rows.Length; i++)
                dto.rows[i] = new EndlessCheckpointDto { wave = rows[i].wave, unlockAt = rows[i].unlockAt, cogs = rows[i].cogs };
            return dto;
        }

        static EndlessCheckpointsDto Shipped => Rows((9, 20, 2), (17, 30, 2), (22, 40, 2), (27, 50, 2), (32, 60, 2), (37, 70, 2));

        static void Publish(EndlessCheckpointsDto checkpoints)
        {
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 10,
                endlessCheckpoints = checkpoints,
            };

            var problems = new List<string>();
            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, problems), string.Join("; ", problems));
            Assert.IsEmpty(problems, string.Join("; ", problems));
            ProgressionRules.Publish(table);
        }

        static void Advance(SiegeBoard board, float seconds)
        {
            for (float t = 0f; t < seconds && !board.IsFinished; t += 1f / 60f) board.Advance(1f / 60f);
        }

        // ------------------------------------------------------------------ the shipped table
        /// <summary>
        /// The table that ships is read clean, and every checkpoint in it opens on the wave after
        /// a boss wave - the owner's rule, asked of the real schedule rather than of a copy.
        /// Read off the file on disk, because a table the reader refuses offers nothing on a
        /// device and says so nowhere a player can see.
        /// </summary>
        [Test]
        public void TheShippedTableIsReadCleanAndEveryCheckpointFollowsABoss()
        {
            string path = Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets", "Content", "progression.json");
            var file = TestJson.Object(TestJson.Parse(File.ReadAllText(path)));
            var block = TestJson.Child(file, "endlessCheckpoints");
            Assert.IsNotNull(block, "progression.json carries no endlessCheckpoints block");

            var list = TestJson.Children(block, "rows");
            var dto = new EndlessCheckpointsDto { rows = new EndlessCheckpointDto[list.Count] };
            for (int i = 0; i < list.Count; i++)
            {
                var row = TestJson.Object(list[i]);
                dto.rows[i] = new EndlessCheckpointDto
                {
                    wave = TestJson.Int(row, "wave"),
                    unlockAt = TestJson.Int(row, "unlockAt"),
                    cogs = TestJson.Int(row, "cogs"),
                };
            }

            var problems = new List<string>();
            var table = EndlessCheckpointTable.Resolve(dto, problems);

            Assert.IsEmpty(problems, string.Join("; ", problems));
            Assert.IsTrue(table.Any, "the shipped table resolved to nothing");

            foreach (var row in table.Rows)
            {
                Assert.IsTrue(SiegeEndless.IsBossWave(row.Wave - 1), $"wave {row.Wave} does not follow a boss");
                Assert.IsFalse(SiegeEndless.IsBossWave(row.Wave), $"wave {row.Wave} is itself a boss wave");
                Assert.GreaterOrEqual(row.UnlockAt, row.Wave, $"wave {row.Wave} opens before anybody has been there");
            }
        }

        // ------------------------------------------------------------------ the reader
        /// <summary>Every fault refuses the whole block, and each one is named.</summary>
        [Test]
        public void AFaultyTableIsRefusedWholeAndSaysWhy()
        {
            var cases = new Dictionary<string, EndlessCheckpointsDto>
            {
                { "a boss wave next",       Rows((20, 30, 2)) },                   // wave 21 is a duel
                { "wave one",               Rows((1, 10, 2)) },
                { "not climbing",           Rows((17, 30, 2), (9, 40, 2)) },
                { "open before it is seen", Rows((17, 12, 2)) },
                { "unlock not climbing",    Rows((9, 30, 2), (17, 30, 2)) },
                { "a rank past the top",    Rows((17, 30, SiegeTuning.MaxRank + 1)) },
                { "negative cogs",          Rows((17, 30, -1)) },
                { "too many rows",          Rows((5, 10, 0), (9, 20, 0), (13, 30, 0), (17, 40, 0), (22, 50, 0),
                                                 (27, 60, 0), (32, 70, 0), (37, 80, 0), (42, 90, 0)) },
            };

            foreach (var pair in cases)
            {
                var problems = new List<string>();
                var table = EndlessCheckpointTable.Resolve(pair.Value, problems);

                Assert.IsFalse(table.Any, $"{pair.Key}: a faulty table offered checkpoints");
                Assert.IsNotEmpty(problems, $"{pair.Key}: refused without saying why");
            }
        }

        /// <summary>Absent is not a fault: the lane opens at wave one, exactly as before checkpoints.</summary>
        [Test]
        public void AnAbsentTableOffersNothingAndIsNotAFault()
        {
            var problems = new List<string>();

            Assert.IsFalse(EndlessCheckpointTable.Resolve(null, problems).Any);
            Assert.IsFalse(EndlessCheckpointTable.Resolve(new EndlessCheckpointsDto(), problems).Any);
            Assert.IsEmpty(problems);
        }

        // ------------------------------------------------------------------ the unlock rule
        /// <summary>
        /// A checkpoint is open at a best of exactly its line and not one wave short - the whole
        /// of "clear twenty to open nine" - and the count and the next shut row follow it.
        /// </summary>
        [Test]
        public void ACheckpointOpensAtItsLineAndNotBefore()
        {
            var table = EndlessCheckpointTable.Resolve(Shipped, null);

            Assert.AreEqual(0, table.OpenCount(19));
            Assert.AreEqual(9, table.NextLocked(19).Wave);
            Assert.AreEqual(20, table.NextLocked(19).UnlockAt);

            Assert.AreEqual(1, table.OpenCount(20));
            Assert.AreEqual(17, table.NextLocked(20).Wave);

            Assert.AreEqual(3, table.OpenCount(45));
            Assert.AreEqual(6, table.OpenCount(9999));
            Assert.IsFalse(table.NextLocked(9999).IsValid, "a lane past every line still has one shut");
        }

        /// <summary>
        /// <b>The best is the authority and the choice is a hint.</b> A remembered wave that is not
        /// a checkpoint, or one the best has not opened, opens at wave one - never somewhere
        /// unearned.
        /// </summary>
        [Test]
        public void AChoiceTheBestDoesNotSupportOpensAtWaveOne()
        {
            var table = EndlessCheckpointTable.Resolve(Shipped, null);

            Assert.AreEqual(17, table.Resolve(17, 30).Wave);
            Assert.IsFalse(table.Resolve(17, 29).IsValid, "a checkpoint opened one wave short of its line");
            Assert.IsFalse(table.Resolve(18, 9999).IsValid, "a wave that is no checkpoint opened");
            Assert.IsFalse(table.Resolve(1, 9999).IsValid, "the beginning is not a checkpoint");

            Assert.IsTrue(table.Resolve(18, 9999).Start.IsOpening);
        }

        // ------------------------------------------------------------------ the device's choice
        /// <summary>
        /// Choosing writes only an open checkpoint, the next run starts there with the row's head
        /// start, and choosing the beginning puts it back - each move raising one change.
        /// </summary>
        [Test]
        public void ChoosingAnOpenCheckpointMovesTheNextRunAndNothingElse()
        {
            Publish(Shipped);
            EndlessLedger.Record(Lane, 42);

            int raised = 0;
            System.Action count = () => raised++;
            EndlessCheckpoints.Changed += count;

            try
            {
                Assert.IsTrue(EndlessCheckpoints.StartFor(Lane).IsOpening, "a fresh device opened past wave one");

                Assert.IsFalse(EndlessCheckpoints.Choose(Lane, 27), "a shut checkpoint was chosen");
                Assert.IsFalse(EndlessCheckpoints.Choose(Lane, 18), "a wave that is no checkpoint was chosen");
                Assert.AreEqual(0, raised, "a refused choice raised a change");
                Assert.IsTrue(EndlessCheckpoints.StartFor(Lane).IsOpening);

                Assert.IsTrue(EndlessCheckpoints.Choose(Lane, 22));
                var start = EndlessCheckpoints.StartFor(Lane);
                Assert.AreEqual(22, start.Wave);
                Assert.AreEqual(2, start.Ranks);
                Assert.AreEqual(1, raised);

                Assert.IsTrue(EndlessCheckpoints.Choose(Lane, 22), "choosing the same start again was refused");
                Assert.AreEqual(1, raised, "choosing what was already chosen raised a change");

                Assert.IsTrue(EndlessCheckpoints.Choose(Lane, 1));
                Assert.IsTrue(EndlessCheckpoints.StartFor(Lane).IsOpening);
                Assert.AreEqual(2, raised);
            }
            finally
            {
                EndlessCheckpoints.Changed -= count;
            }
        }

        /// <summary>
        /// A remembered choice is re-asked on every read: a table retuned under it, or a best that
        /// is not there (an account that never went so far), opens at wave one.
        /// </summary>
        [Test]
        public void ARememberedChoiceIsReAskedOnEveryRead()
        {
            Publish(Shipped);
            EndlessLedger.Record(Lane, 42);
            Assert.IsTrue(EndlessCheckpoints.Choose(Lane, 22));

            // The lane's best gone - the same device signed into an account that never went so far.
            EndlessLedger.LoadFrom(new SaveFileDto());
            Assert.IsTrue(EndlessCheckpoints.StartFor(Lane).IsOpening, "an unearned checkpoint opened");

            // And back again: the choice was kept, only the best decides whether it stands.
            EndlessLedger.Record(Lane, 42);
            Assert.AreEqual(22, EndlessCheckpoints.StartFor(Lane).Wave);

            // A table retuned so that wave is no longer a checkpoint.
            Publish(Rows((17, 30, 2)));
            Assert.IsTrue(EndlessCheckpoints.StartFor(Lane).IsOpening, "a withdrawn checkpoint opened");
        }

        // ------------------------------------------------------------------ the board
        /// <summary>
        /// <b>The wave a checkpoint opens on is the wave a walked run meets.</b> The first muster
        /// of a run opened at wave seventeen sends exactly what the lane's schedule says wave
        /// seventeen sends - its size, its kinds, its colours and its surged health - and that is
        /// measurably harder than wave one, so it is not wave one relabelled.
        /// </summary>
        [Test]
        public void ACheckpointRunMeetsTheRealWave()
        {
            const int wave = 17;
            var layout = Endless();
            var board = SiegeBoard.Build(layout, null, new SiegeStart(wave, 2));

            Assert.AreEqual(wave, board.StartWave);
            Assert.IsFalse(board.Opened, "a checkpoint run opened with its wave already on the hill");
            Assert.Greater(board.BeforeFirstWave, 0f, "a checkpoint run skipped the count-in");
            Assert.IsEmpty(board.Raiders);

            Advance(board, SiegeTuning.FirstWaveAfter + .5f);

            Assert.IsTrue(board.Opened, "the first wave never came");
            Assert.AreEqual(wave, board.Wave, "the run did not open on its checkpoint");

            int index = wave - 1;
            Assert.AreEqual(layout.SizeOf(index), board.Raiders.Count, "the wave sent the wrong number");

            var surge = layout.SurgeOf(index);
            for (int i = 0; i < board.Raiders.Count; i++)
            {
                var sent = board.Raiders[i];
                var real = new SiegeRaider(0, layout.ColourAt(index, i), layout.KindAt(index, i), 0,
                                           0f, surge, layout.ShareAt(index, i));

                Assert.AreEqual(real.Kind, sent.Kind, $"raider {i}'s kind");
                Assert.AreEqual(real.Colour, sent.Colour, $"raider {i}'s colour");
                Assert.AreEqual(real.MaxHealth, sent.MaxHealth, $"raider {i}'s health");
            }

            Assert.Greater(surge.HealthTenths, layout.SurgeOf(0).HealthTenths,
                           "wave seventeen is no tougher than wave one");
        }

        /// <summary>Every turret stands the head start up the ladder, and the ladder still has room above it.</summary>
        [Test]
        public void EveryTurretStartsWithTheHeadStart()
        {
            var board = SiegeBoard.Build(Endless(), null, new SiegeStart(17, 2));

            foreach (var ward in board.Wards)
            {
                Assert.AreEqual(2, ward.Rank);
                Assert.AreEqual(3, ward.Level, "the badge a player reads");
                Assert.IsTrue(ward.Upgradable, "a head start filled the ladder");
            }

            var capped = SiegeBoard.Build(Endless(), null, new SiegeStart(17, 99));
            foreach (var ward in capped.Wards) Assert.AreEqual(SiegeTuning.MaxRank, ward.Rank);
        }

        /// <summary>
        /// <b>Paid for the waves it saw off, never for the ones it skipped.</b> The record takes
        /// the absolute wave - the run really did get that far - and what pays is the difference.
        /// On a run opened at wave one the two readings are one number, so nothing that shipped
        /// before checkpoints reads differently.
        /// </summary>
        [Test]
        public void ACheckpointRunIsPaidOnlyForTheWavesItSawOff()
        {
            var board = SiegeBoard.Build(Endless(), null, new SiegeStart(17, 2));

            Assert.AreEqual(16, board.WavesCleared, "before its first wave the run stands at its checkpoint");
            Assert.AreEqual(0, board.WavesThisRun, "a run was paid for waves it skipped");

            // Nobody playing: the line falls, and whatever was seen off is the difference.
            Advance(board, 60f * 600f);
            Assert.IsTrue(board.IsFinished, "the line never fell");
            Assert.AreEqual(board.WavesCleared - 16, board.WavesThisRun);
            Assert.GreaterOrEqual(board.WavesThisRun, 0);

            var walked = SiegeBoard.Build(Endless());
            Advance(walked, 60f * 600f);
            Assert.AreEqual(walked.WavesCleared, walked.WavesThisRun, "a run from wave one pays differently");
        }

        /// <summary>
        /// A laddered chapter never opens part-way: its waves are an authored list with a boss at
        /// the end, so the start is ignored on the board and on the rules alike.
        /// </summary>
        [Test]
        public void ALadderedLevelIgnoresAStart()
        {
            var layout = Laddered();
            var board = SiegeBoard.Build(layout, null, new SiegeStart(17, 2));

            Assert.AreEqual(1, board.StartWave);
            foreach (var ward in board.Wards) Assert.AreEqual(0, ward.Rank);

            Assert.IsTrue(new SiegeRules(layout).From(new SiegeStart(17, 2)).Start.IsOpening,
                          "a chapter's rules carried a checkpoint");
            Assert.AreEqual(17, new SiegeRules(Endless()).From(new SiegeStart(17, 2)).Start.Wave,
                            "an endless lane's rules dropped its checkpoint");
        }

        /// <summary>A restart deals the run's own start again: the rules carry it, not the screen.</summary>
        [Test]
        public void ARestartOpensWhereTheRunBegan()
        {
            var rules = new SiegeRules(Endless(), null).From(new SiegeStart(22, 2));

            var first = (SiegeBoard)rules.Fresh();
            var again = (SiegeBoard)rules.Fresh();

            Assert.AreEqual(22, first.StartWave);
            Assert.AreEqual(22, again.StartWave);
            Assert.AreEqual(2, again.Wards[0].Rank);
        }
    }
}
