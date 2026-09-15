using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The keeper wall: a chapter that cannot be entered until the account has reached a level.
    ///
    /// <para>
    /// <b>Why it exists.</b> A star gate is always about the chapter <em>behind</em> this one, so
    /// the first chapter of a lane has nothing to ask for and stands open to a brand-new account.
    /// That is right for a mode's opening chapter and wrong for a second lane beside it: the
    /// Infinite lane is one chapter of one level, played for how far it gets, and a player who
    /// meets it before they have met the mode is being offered the ending first.
    /// </para>
    /// <para>
    /// <b>What makes it dangerous is that it can only be wrong in one direction and silently.</b>
    /// A wall set too high is a lane padlocked for the life of the build with every file correct
    /// — which is why the content gates measure it against what the shipped catalog can pay for —
    /// and a wall the unlock rule never reaches is a lane that reads as gated and is not. This
    /// fixture pins the second; <c>ContentValidation.ValidateKeeperWalls</c> and
    /// <c>content.py</c> pin the first.
    /// </para>
    /// <para>
    /// <b>The walls here are placed around the level the fixture is really running at</b>, which
    /// is <c>HomeLadderGateTests</c>' shape and for its reason: the keeper level is read off
    /// <see cref="PlayerProgression"/> rather than taken as an argument, so driving it would mean
    /// authoring a star ledger to assert something about a manifest. What is asserted is the
    /// <em>decision</em>, which is the half that can be wrong.
    /// </para>
    /// </summary>
    public sealed class KeeperWallTests
    {
        static int Rank => PlayerProgression.Level.Level;

        // ------------------------------------------------------------------ fixtures
        /// <summary>
        /// An ordinary two-chapter ladder, plus a one-chapter lane beside it carrying a wall.
        ///
        /// <para>
        /// <b>The lane is one chapter of one level on purpose.</b> That is the shape the Infinite
        /// lane really has, and it is the shape every other gate in this file is blind to: it has
        /// no chapter behind it, so no star gate, and no level before its own, so no chain. If
        /// the wall is asked anywhere other than at the chapter head, this lane is the one board
        /// where nothing asks at all.
        /// </para>
        /// </summary>
        static CatalogIndex Catalog(int wall)
        {
            var builder = new CatalogIndexBuilder();

            builder.Add(new ManifestChapterDto
            {
                id = "s01_main", order = 10, version = 1, mode = "siege",
                levels = new[] { "main_a", "main_b", "main_c" },
            }, 1);

            builder.Add(new ManifestChapterDto
            {
                id = "s02_endless", order = 20, version = 1, mode = "siege", track = "infinite",
                minKeeperLevel = wall,
                levels = new[] { "endless_a" },
            }, 1);

            return builder.Build();
        }

        static readonly ChapterId Endless = ChapterId.Parse("s02_endless");
        static readonly LevelId EndlessLevel = LevelId.Parse("endless_a");

        [SetUp]
        public void StartFromTheShippedRules()
        {
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
            EndlessLedger.LoadFrom(new SaveFileDto());
        }

        [TearDown]
        public void Clear()
        {
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
            EndlessLedger.LoadFrom(new SaveFileDto());
        }

        // ============================================================ the arithmetic
        [Test]
        public void BothHalvesAreNeededAndNeitherAlone()
        {
            var behind = ChapterId.Parse("s01_main");

            // Invariant 15a, which companions learned the expensive way: a rule answered in two
            // predicates is a rule somebody checks half of. `IsOpen` is the conjunction and is
            // the only thing entitled to answer.
            Assert.IsFalse(new ChapterGate(behind, 6, 2, 9, 10, 10).IsOpen, "stars short");
            Assert.IsFalse(new ChapterGate(behind, 6, 9, 9, 10, 4).IsOpen, "level short");
            Assert.IsFalse(new ChapterGate(behind, 6, 2, 9, 10, 4).IsOpen, "both short");
            Assert.IsTrue(new ChapterGate(behind, 6, 9, 9, 10, 10).IsOpen, "both met");
        }

        [Test]
        public void TheThresholdIsInclusive()
        {
            Assert.IsFalse(new ChapterGate(ChapterId.None, 0, 0, 0, 10, 9).IsOpen);
            Assert.IsTrue(new ChapterGate(ChapterId.None, 0, 0, 0, 10, 10).IsOpen,
                          "reaching the level is meeting the wall, not passing it");
        }

        [Test]
        public void AWallAloneIsStillWorthPrinting()
        {
            // The Infinite lane's own shape: nothing behind it, so the star half names no
            // chapter and asks for nothing - and the gate still has a sentence to say.
            var gate = new ChapterGate(ChapterId.None, 0, 0, 0, 10, 4);

            Assert.IsTrue(gate.Exists, "a refusal nobody can print is a padlock with no reason");
            Assert.IsTrue(gate.NeedsLevel);
            Assert.AreEqual(6, gate.LevelsRemaining);
        }

        [Test]
        public void TheLevelHalfIsTheOneReportedWhenBothStand()
        {
            // Invariant 16s read across: when two refusals apply, the one to say is the one the
            // nearer currency cannot answer. Telling this player to earn four more stars would
            // send them back to a chapter they may already have finished.
            var gate = new ChapterGate(ChapterId.Parse("s01_main"), 6, 2, 9, 10, 4);

            Assert.IsTrue(gate.NeedsLevel);
            Assert.AreEqual(4, gate.Remaining, "and the star half is still readable");
        }

        [Test]
        public void AGateWithNoWallIsExactlyWhatItWasBefore()
        {
            // The whole catalog authored before this field existed, in one case.
            var gate = new ChapterGate(ChapterId.Parse("s01_main"), 6, 6, 9);

            Assert.AreEqual(0, gate.RequiredLevel);
            Assert.IsFalse(gate.NeedsLevel);
            Assert.AreEqual(0, gate.LevelsRemaining);
            Assert.IsTrue(gate.IsOpen);
        }

        [Test]
        public void ANegativeWallIsReadAsNoWall()
        {
            // The safe direction, twice over: a manifest typo must not shut a lane nobody can
            // ever open, and `JsonUtility` writes a zero into every field an older file lacked.
            Assert.AreEqual(0, new ChapterGate(ChapterId.None, 0, 0, 0, -5, 0).RequiredLevel);
            Assert.IsTrue(new ChapterGate(ChapterId.None, 0, 0, 0, -5, 0).IsOpen);
        }

        // ============================================================== the manifest
        [Test]
        public void TheManifestFieldReachesTheIndex()
        {
            // A wall authored and never carried is a wall nothing can ask about. It is index
            // knowledge because `LevelUnlock` answers for a whole lane before any body is read.
            Assert.AreEqual(10, Catalog(10).FindChapter(Endless).MinKeeperLevel);
            Assert.AreEqual(0, Catalog(10).FindChapter(ChapterId.Parse("s01_main")).MinKeeperLevel);
        }

        [Test]
        public void AnAbsentFieldIsNoWall()
        {
            var index = Catalog(0);

            Assert.AreEqual(0, index.FindChapter(Endless).MinKeeperLevel);
            Assert.IsTrue(LevelUnlock.IsChapterUnlocked(index, Endless),
                          "every chapter authored before this field existed keeps working");
        }

        // =============================================================== the decision
        [Test]
        public void ALaneBehindAWallAboveThisAccountIsShut()
        {
            var index = Catalog(Rank + 5);

            Assert.IsFalse(LevelUnlock.IsChapterUnlocked(index, Endless));
            Assert.IsFalse(LevelUnlock.IsUnlocked(index, EndlessLevel));
        }

        [Test]
        public void ALaneWhoseWallThisAccountHasReachedIsOpen()
        {
            var index = Catalog(Rank);

            Assert.IsTrue(LevelUnlock.IsChapterUnlocked(index, Endless));
            Assert.IsTrue(LevelUnlock.IsUnlocked(index, EndlessLevel));
        }

        [Test]
        public void TheGateNamesTheLevelSoTheRefusalIsActionable()
        {
            var gate = LevelUnlock.GateFor(Catalog(Rank + 5), Endless);

            Assert.AreEqual(Rank + 5, gate.RequiredLevel);
            Assert.AreEqual(Rank, gate.KeeperLevel, "so a readout can say level 4 of 9");
            Assert.IsTrue(gate.NeedsLevel);
            Assert.IsFalse(gate.IsOpen);
        }

        [Test]
        public void TheWallIsAskedEvenThoughNothingStandsBeforeThatLevel()
        {
            // **The regression this fixture is really for.** `IsUnlocked` used to answer true for
            // any level with nothing before it, before anything looked at a gate — harmless while
            // the only gate was on the chapter behind, because a lane's first chapter has none.
            // On a lane of one chapter of one level that shortcut is every board there is, so the
            // wall would have been dead code on the only lane that has one.
            var index = Catalog(Rank + 5);

            Assert.IsTrue(LevelUnlock.IsChapterHead(index, EndlessLevel),
                          "the endless level is its chapter's head, which is what asks the gate");
            Assert.IsFalse(index.Previous(EndlessLevel).IsValid,
                           "and it has nothing before it, which is what used to short it out");
            Assert.IsFalse(LevelUnlock.IsUnlocked(index, EndlessLevel));
        }

        [Test]
        public void TheOrdinaryLaddersFirstLevelIsStillAlwaysOpen()
        {
            // The other side of that reordering: the very first level of a mode has nothing
            // before it either, and must still be open to somebody who has played nothing.
            var index = Catalog(Rank + 5);

            Assert.IsTrue(LevelUnlock.IsUnlocked(index, LevelId.Parse("main_a")));
            Assert.IsFalse(LevelUnlock.IsUnlocked(index, LevelId.Parse("main_b")),
                           "and the chain inside a chapter is untouched");
        }

        [Test]
        public void AWallNeverReachesTheLaneBesideIt()
        {
            // Invariant 20a: the ladders never chain. A wall on the endless lane is a fact about
            // that lane, and the ordinary one must not so much as notice it.
            var index = Catalog(Rank + 5);

            Assert.IsTrue(LevelUnlock.IsChapterUnlocked(index, ChapterId.Parse("s01_main")));
            Assert.IsFalse(LevelUnlock.GateFor(index, ChapterId.Parse("s01_main")).Exists);
        }

        // ============================================================== the migration
        [Test]
        public void ALaneAlreadyRunIsNeverTakenBack()
        {
            // The only case here with live players in it, and the one that needed a second
            // ledger. An endless run is never *cleared* — it leaves a wave count and nothing
            // else — so asking `PlayerProgress.IsCleared` alone would padlock the lane under
            // somebody who had already played it the day a wall was put in front of it. That is
            // exactly what the monotonic clause exists to prevent, arriving through the one lane
            // that does not write the ledger it reads.
            var index = Catalog(Rank + 5);
            Assert.IsFalse(LevelUnlock.IsUnlocked(index, EndlessLevel), "shut before the run");

            EndlessLedger.Record(EndlessLevel, 14);

            Assert.IsTrue(LevelUnlock.IsUnlocked(index, EndlessLevel),
                          "somebody who has held out to wave 14 is not sent back behind a wall");
            Assert.IsTrue(LevelUnlock.IsChapterUnlocked(index, Endless));
        }

        [Test]
        public void KeepingWhatWasPlayedHandsNothingElseOver()
        {
            // The other half of the same rule, which is what stops it being a hole: a best on one
            // level is not a way into anything else.
            var index = Catalog(Rank + 5);
            EndlessLedger.Record(EndlessLevel, 14);

            Assert.IsFalse(LevelUnlock.IsUnlocked(index, LevelId.Parse("main_b")),
                           "still the chain");
        }

        [Test]
        public void AWaveOfNoughtIsNotARun()
        {
            // `Record` refuses it and so does the reading, which matters because a zero is what
            // `JsonUtility` writes into a row that never existed.
            var index = Catalog(Rank + 5);

            EndlessLedger.Record(EndlessLevel, 0);

            Assert.IsFalse(LevelUnlock.HasEverPlayed(EndlessLevel));
            Assert.IsFalse(LevelUnlock.IsUnlocked(index, EndlessLevel));
        }
    }
}
