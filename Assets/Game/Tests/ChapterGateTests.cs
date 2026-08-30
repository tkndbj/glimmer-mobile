using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// What opens the next chapter.
    ///
    /// <para>
    /// This is the rule with the widest reach of anything in <c>progression.json</c> and the
    /// one with no undo. Everything else in that file can be retuned in either direction and
    /// the worst case is a player who earns a bit more or less than intended; a gate decides
    /// whether somebody can carry on playing at all, and getting it wrong shows up as installs
    /// that stop rather than as an economy that drifts.
    /// </para>
    /// <para>
    /// Split the way the house rules ask for. The arithmetic — how many stars, is it met, how
    /// many are left — is proved against plain integers, and the wiring is proved against a
    /// driven catalog and a driven save. Nothing here needs the Editor, which is the point:
    /// the gate must be provable on the machine of whoever is changing it.
    /// </para>
    /// </summary>
    public sealed class ChapterGateTests
    {
        // ======================================================== the published number
        [Test]
        public void TheShippedGateIsTwentyOfAChaptersThirty()
        {
            // The rule the information panel prints and the number the map counts towards.
            // Written down here so that moving the built-in default is a deliberate act with a
            // failing test in front of it, rather than a one-character edit nobody reviews.
            Assert.AreEqual(2, ChapterGateTable.Default.StarsPerLevel);
            Assert.AreEqual(20, ChapterGateTable.Default.RequiredStars(10));
        }

        [Test]
        public void TheRequirementScalesWithTheChapterRatherThanBeingATotal()
        {
            var gate = ChapterGateTable.Default;

            // Why the file writes stars *per level*. A chapter is not a fixed size, and a
            // total of 20 would be two thirds of a ten-level chapter and a fifth of a
            // fifty-level one - the same authored number meaning two different rules.
            Assert.AreEqual(10, gate.RequiredStars(5));
            Assert.AreEqual(20, gate.RequiredStars(10));
            Assert.AreEqual(100, gate.RequiredStars(50));
        }

        [Test]
        public void AChapterWithNoLevelsAsksForNothing()
        {
            // Not a real chapter, but reachable from a manifest mid-edit, and multiplying by
            // zero must not produce a gate that reads as met-by-default somewhere else.
            Assert.AreEqual(0, ChapterGateTable.Default.RequiredStars(0));
            Assert.AreEqual(0, ChapterGateTable.Default.RequiredStars(-3));
        }

        // ============================================================ reading the file
        static ChapterGateTable Read(ChapterGateDto dto, out List<string> problems)
        {
            problems = new List<string>();
            return ChapterGateTable.Resolve(dto, problems);
        }

        [Test]
        public void AnAbsentBlockLeavesTheBuiltInGateStanding()
        {
            var table = Read(null, out var problems);

            Assert.AreEqual(ChapterGateLimits.DefaultStarsPerLevel, table.StarsPerLevel);
            CollectionAssert.IsEmpty(problems, "absent is not an error");
        }

        [Test]
        public void AnUnwrittenFieldInheritsRatherThanReadingAsZero()
        {
            // The tri-state every optional number in that file uses. Zero is a real value here
            // - it turns the gate off - so "not written" has to be something else.
            var table = Read(new ChapterGateDto(), out var problems);

            Assert.AreEqual(ChapterGateLimits.DefaultStarsPerLevel, table.StarsPerLevel);
            CollectionAssert.IsEmpty(problems);
        }

        [Test]
        public void ZeroIsLegalAndTurnsTheGateOff()
        {
            var table = Read(new ChapterGateDto { starsPerLevel = 0 }, out var problems);

            Assert.AreEqual(0, table.StarsPerLevel);
            Assert.IsTrue(table.IsOpenToAll);
            CollectionAssert.IsEmpty(problems, "switching the gate off is a decision, not a mistake");
        }

        [Test]
        public void AGateNoLevelCouldEverMeetIsClampedAndNamed()
        {
            var table = Read(new ChapterGateDto { starsPerLevel = 4 }, out var problems);

            Assert.AreEqual(ChapterGateLimits.MaxStarsPerLevel, table.StarsPerLevel);
            Assert.AreEqual(1, problems.Count, "the clamp has to be reported, not silent");
            StringAssert.Contains("starsPerLevel", problems[0]);
        }

        [Test]
        public void PerfectPlayIsAllowedWithoutComplaint()
        {
            // Harsh, and the build gate warns about it - but it is a value somebody may
            // genuinely want for a short bonus chapter, so the reader must not refuse it.
            var table = Read(new ChapterGateDto { starsPerLevel = 3 }, out var problems);

            Assert.AreEqual(3, table.StarsPerLevel);
            CollectionAssert.IsEmpty(problems);
        }

        /// <summary>
        /// A minimal file, built rather than parsed.
        ///
        /// <para>
        /// Through <c>TryBuild</c> rather than <c>TryRead</c> deliberately, which is the split
        /// that exists so the reward rules can be exercised with no serialiser in the way:
        /// <c>JsonUtility</c> is a native call, so a test that went through the string would be
        /// counted "needs the Editor" and would stop proving anything on the machine of whoever
        /// is retuning the gate.
        /// </para>
        /// </summary>
        static ProgressionDto File(ChapterGateDto gate) => new ProgressionDto
        {
            schemaVersion = 1,
            maxLevel = 50,
            xpToNext = new[] { 100, 200 },
            tailXpToNext = 300,
            tailXpIncrement = 50,
            chapterGate = gate,
        };

        [Test]
        public void TheBlockIsWiredIntoTheTableTheGameReads()
        {
            // A block that resolves perfectly and is never wired into the table is a retune
            // that silently does nothing, which is how a published lever fails.
            var problems = new List<string>();
            Assert.IsTrue(ProgressionTable.TryBuild(File(new ChapterGateDto { starsPerLevel = 1 }),
                                                    out var table, problems),
                          string.Join("; ", problems));

            Assert.AreEqual(1, table.ChapterGate.StarsPerLevel);
            Assert.AreEqual(10, table.ChapterGate.RequiredStars(10));
        }

        [Test]
        public void AFileWithNoGateBlockStillGetsTheBuiltInOne()
        {
            var problems = new List<string>();
            Assert.IsTrue(ProgressionTable.TryBuild(File(null), out var table, problems),
                          string.Join("; ", problems));

            Assert.AreEqual(ChapterGateLimits.DefaultStarsPerLevel, table.ChapterGate.StarsPerLevel);
        }

        [Test]
        public void AnUnreadableGateNeverTakesTheCurveDownWithIt()
        {
            var problems = new List<string>();
            Assert.IsTrue(ProgressionTable.TryBuild(File(new ChapterGateDto { starsPerLevel = 99 }),
                                                    out var table, problems),
                          "a bad gate is a content error, never a lost session");

            Assert.AreEqual(100, table.XpToReach(2), "the curve is untouched");
            Assert.AreEqual(ChapterGateLimits.MaxStarsPerLevel, table.ChapterGate.StarsPerLevel);
            Assert.AreEqual(1, problems.Count, "and it is still reported");
        }

        // ================================================================ the reading
        [Test]
        public void ExactlyEnoughStarsOpensTheGate()
        {
            var behind = ChapterId.Parse("c01_test");

            Assert.IsFalse(new ChapterGate(behind, 20, 19, 30).IsOpen);
            Assert.IsTrue(new ChapterGate(behind, 20, 20, 30).IsOpen, "the threshold is inclusive");
            Assert.IsTrue(new ChapterGate(behind, 20, 30, 30).IsOpen);
        }

        [Test]
        public void RemainingCountsDownAndStopsAtZero()
        {
            var behind = ChapterId.Parse("c01_test");

            Assert.AreEqual(20, new ChapterGate(behind, 20, 0, 30).Remaining);
            Assert.AreEqual(2, new ChapterGate(behind, 20, 18, 30).Remaining);
            Assert.AreEqual(0, new ChapterGate(behind, 20, 20, 30).Remaining);
            Assert.AreEqual(0, new ChapterGate(behind, 20, 30, 30).Remaining,
                            "a player past the gate is not owed negative stars");
        }

        [Test]
        public void AGateWithNothingBehindItIsOpenAndNotWorthDrawing()
        {
            Assert.IsTrue(ChapterGate.Open.IsOpen);
            Assert.IsFalse(ChapterGate.Open.Exists, "there is no requirement to print");
            Assert.AreEqual(1f, ChapterGate.Open.Fraction, "and no bar to divide by zero");
        }

        [Test]
        public void AChapterTheCatalogDoesNotCarryIsShutRatherThanOpen()
        {
            // The safe direction. A typo in a manifest must not be the thing that opens
            // everything, and it must not print a requirement nobody could work towards either.
            Assert.IsFalse(ChapterGate.Missing.IsOpen);
            Assert.IsFalse(ChapterGate.Missing.Exists);
        }

        [Test]
        public void TheFractionIsBoundedAtBothEnds()
        {
            var behind = ChapterId.Parse("c01_test");

            Assert.AreEqual(0f, new ChapterGate(behind, 20, 0, 30).Fraction);
            Assert.AreEqual(.5f, new ChapterGate(behind, 20, 10, 30).Fraction, 1e-5f);
            Assert.AreEqual(1f, new ChapterGate(behind, 20, 30, 30).Fraction,
                            "a player past the gate does not fill a bar twice");
        }

        // =============================================================== the wiring
        /// <summary>
        /// Two chapters of three levels each, in the ordinary mode, plus a one-level chapter
        /// in a mode of its own — which is what proves the ladders do not chain.
        /// </summary>
        static CatalogIndex TwoChapters()
        {
            var builder = new CatalogIndexBuilder();
            builder.Add(new ManifestChapterDto
            {
                id = "c01_one", order = 10, version = 1,
                levels = new[] { "one_a", "one_b", "one_c" },
            }, 1);
            builder.Add(new ManifestChapterDto
            {
                id = "c02_two", order = 20, version = 1,
                levels = new[] { "two_a", "two_b", "two_c" },
            }, 1);
            builder.Add(new ManifestChapterDto
            {
                id = "b01_thicket", order = 30, version = 1, mode = "bud",
                levels = new[] { "thicket_a" },
            }, 1);
            return builder.Build();
        }

        /// <summary>
        /// Drives the save directly rather than through <c>RecordRun</c>, which would write a
        /// file. Every id given is recorded as cleared with the stars named.
        /// </summary>
        static void Holding(params (string level, int stars)[] records)
        {
            var dto = new SaveFileDto { levels = new LevelRecordDto[records.Length] };

            for (int i = 0; i < records.Length; i++)
                dto.levels[i] = new LevelRecordDto
                {
                    levelId = records[i].level,
                    stars = records[i].stars,
                    bestMoves = 10,
                    clears = 1,
                };

            PlayerProgress.LoadFrom(dto);
        }

        /// <summary>
        /// The live table as well as the save, because <see cref="LevelUnlock"/> reads the
        /// published gate and another fixture in this assembly may have left one behind. The
        /// offline runner does not promise an order, so independence has to be taken rather
        /// than assumed.
        /// </summary>
        [SetUp]
        public void StartFromTheShippedRules()
        {
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
        }

        [TearDown]
        public void ClearTheSave()
        {
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
        }

        [Test]
        public void TheFirstLevelOfTheGameIsAlwaysOpen()
        {
            var index = TwoChapters();
            Holding();

            Assert.IsTrue(LevelUnlock.IsUnlocked(index, LevelId.Parse("one_a")));
        }

        [Test]
        public void InsideAChapterTheChainIsUnchanged()
        {
            var index = TwoChapters();
            Holding(("one_a", 1));

            Assert.IsTrue(LevelUnlock.IsUnlocked(index, LevelId.Parse("one_b")),
                          "one star is a clear, and a clear opens the next level");
            Assert.IsFalse(LevelUnlock.IsUnlocked(index, LevelId.Parse("one_c")));
        }

        [Test]
        public void ClearingAWholeChapterPoorlyDoesNotOpenTheNextOne()
        {
            // The whole point of the change, stated as the case that used to pass and must
            // now fail: three levels cleared, every one of them on a single star.
            var index = TwoChapters();
            Holding(("one_a", 1), ("one_b", 1), ("one_c", 1));

            Assert.IsFalse(LevelUnlock.IsChapterUnlocked(index, ChapterId.Parse("c02_two")));
            Assert.IsFalse(LevelUnlock.IsUnlocked(index, LevelId.Parse("two_a")));
        }

        [Test]
        public void EnoughStarsOpensTheNextChapterWithALevelStillUnfinished()
        {
            // And the case that used to be impossible: a player stuck on the last level of a
            // chapter is no longer stuck on the game. Six of nine stars over two levels meets
            // a gate of six, and the third level of the chapter has never been touched.
            var index = TwoChapters();
            Holding(("one_a", 3), ("one_b", 3));

            Assert.AreEqual(6, LevelUnlock.GateFor(index, ChapterId.Parse("c02_two")).Required);
            Assert.IsTrue(LevelUnlock.IsChapterUnlocked(index, ChapterId.Parse("c02_two")));
            Assert.IsTrue(LevelUnlock.IsUnlocked(index, LevelId.Parse("two_a")));
        }

        [Test]
        public void TheGateNeverOpensAGladeInTheMiddleOfAChapter()
        {
            // A gate is permission to enter a chapter, never permission to skip through it.
            var index = TwoChapters();
            Holding(("one_a", 3), ("one_b", 3), ("one_c", 3));

            Assert.IsTrue(LevelUnlock.IsUnlocked(index, LevelId.Parse("two_a")));
            Assert.IsFalse(LevelUnlock.IsUnlocked(index, LevelId.Parse("two_b")),
                           "still the chain: two_a has not been cleared");
        }

        [Test]
        public void AChapterAlreadyFinishedIsNeverTakenBack()
        {
            // The migration case, and the only one with live players in it. This rule changed
            // under everybody who was already playing: an account that cleared two whole
            // chapters at one star each meets no gate at all. Its own records have to keep it
            // where it was, or the map draws a chapter whose first level is padlocked and whose
            // other levels are open - the chain and the gate disagreeing about one save.
            var index = TwoChapters();
            Holding(("one_a", 1), ("one_b", 1), ("one_c", 1),
                    ("two_a", 1), ("two_b", 1), ("two_c", 1));

            Assert.IsTrue(LevelUnlock.IsChapterUnlocked(index, ChapterId.Parse("c02_two")),
                          "the gate is not met, and the player is already past it");

            foreach (string level in new[] { "two_a", "two_b", "two_c" })
                Assert.IsTrue(LevelUnlock.IsUnlocked(index, LevelId.Parse(level)), level);
        }

        [Test]
        public void BeingPastTheGateDoesNotOpenWhatWasNeverPlayed()
        {
            // The other half of the same rule: keeping what somebody earned must not hand them
            // anything they did not. A cleared last level of a chapter is not a way into the
            // next one.
            var index = TwoChapters();
            Holding(("one_a", 1), ("one_b", 1), ("one_c", 1), ("two_a", 1));

            Assert.IsTrue(LevelUnlock.IsUnlocked(index, LevelId.Parse("two_a")));
            Assert.IsTrue(LevelUnlock.IsUnlocked(index, LevelId.Parse("two_b")),
                          "the chain: two_a is cleared");
            Assert.IsFalse(LevelUnlock.IsUnlocked(index, LevelId.Parse("two_c")),
                           "and no further");
        }

        [Test]
        public void TheGateReadsTheChapterBehindItAndReportsBothNumbers()
        {
            var index = TwoChapters();
            Holding(("one_a", 3), ("one_b", 1));

            var gate = LevelUnlock.GateFor(index, ChapterId.Parse("c02_two"));

            Assert.AreEqual(ChapterId.Parse("c01_one"), gate.Behind);
            Assert.AreEqual(6, gate.Required);
            Assert.AreEqual(4, gate.Held);
            Assert.AreEqual(9, gate.Available, "so a readout can say four of nine");
            Assert.AreEqual(2, gate.Remaining);
            Assert.IsFalse(gate.IsOpen);
        }

        [Test]
        public void AModeIsNeverGatedOnAnotherModesStars()
        {
            // Invariant 20a. The thicket chapter is the first of its own mode, so it is open to
            // a player who has not touched the ordinary game at all.
            var index = TwoChapters();
            Holding();

            Assert.IsTrue(LevelUnlock.IsChapterUnlocked(index, ChapterId.Parse("b01_thicket")));
            Assert.IsTrue(LevelUnlock.IsUnlocked(index, LevelId.Parse("thicket_a")));
        }

        [Test]
        public void GateAfterAnswersForTheChapterInFrontAndIsOpenAtTheEndOfAMode()
        {
            var index = TwoChapters();
            Holding(("one_a", 3), ("one_b", 3));

            Assert.IsTrue(LevelUnlock.GateAfter(index, ChapterId.Parse("c01_one")).IsOpen);
            Assert.IsTrue(LevelUnlock.GateAfter(index, ChapterId.Parse("c02_two")).IsOpen,
                          "nothing is being withheld at the end of a mode");
        }

        [Test]
        public void ContinueNeverPointsAtALevelThePlayerCannotOpen()
        {
            // The trap this change laid for the hub's continue button. Every level of the
            // chapter is cleared, badly, so nothing uncleared is open - and the old fall-through
            // handed back the last level of the mode, which is now padlocked.
            var index = TwoChapters();
            Holding(("one_a", 1), ("one_b", 1), ("one_c", 1));

            var target = LevelUnlock.NextToPlay(index, GameMode.Default);

            Assert.IsTrue(target.IsValid);
            Assert.IsTrue(LevelUnlock.IsUnlocked(index, target),
                          "continue must land somewhere the player is allowed to be");
            Assert.AreEqual(LevelId.Parse("one_c"), target,
                            "the furthest level they can play, which is one worth going back to");
        }

        [Test]
        public void ContinueStillPointsAtTheFirstUnfinishedLevel()
        {
            var index = TwoChapters();
            Holding(("one_a", 3));

            Assert.AreEqual(LevelId.Parse("one_b"), LevelUnlock.NextToPlay(index, GameMode.Default));
        }

        [Test]
        public void AChapterHeadIsTheOnlyLevelThatAsksTheGate()
        {
            var index = TwoChapters();

            Assert.IsTrue(LevelUnlock.IsChapterHead(index, LevelId.Parse("one_a")));
            Assert.IsFalse(LevelUnlock.IsChapterHead(index, LevelId.Parse("one_b")));
            Assert.IsTrue(LevelUnlock.IsChapterHead(index, LevelId.Parse("two_a")));
            Assert.IsFalse(LevelUnlock.IsChapterHead(index, LevelId.Parse("two_c")));
        }
    }
}
