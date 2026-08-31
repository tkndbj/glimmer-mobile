using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Which glades cost a heart.
    ///
    /// <para>
    /// The heart gate is the only rule in this game that can stop somebody playing, so the
    /// window that suspends it is worth proving rather than eyeballing — in both directions.
    /// Too narrow and a beginner is charged for our teaching; too wide and the gate the whole
    /// free-play economy is paced by quietly stops existing for a chapter at a time.
    /// </para>
    /// <para>
    /// Everything here runs against a driven catalog and a published rules table, with no
    /// Editor and no content files, which is the point: whoever retunes <c>graceLevels</c> can
    /// prove what they did on their own machine.
    /// </para>
    /// </summary>
    public sealed class HeartStakeTests
    {
        /// <summary>
        /// The published table and the save, both ways round. The rule reads a table another
        /// fixture may have published and a record another fixture may have loaded, and the
        /// offline runner promises no order — so independence is taken rather than assumed,
        /// exactly as <c>ChapterGateTests</c> takes it next door.
        /// </summary>
        [SetUp]
        public void StartFromTheShippedRules()
        {
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
        }

        [TearDown]
        public void Restore()
        {
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
        }

        /// <summary>
        /// Drives the save directly rather than through <c>RecordRun</c>, which would write a
        /// file. Every id given is recorded as finished; anything else is a glade the player has
        /// either never met or never beaten, which the rule treats identically.
        /// </summary>
        static void Finished(params string[] levels)
        {
            var dto = new SaveFileDto { levels = new LevelRecordDto[levels.Length] };

            for (int i = 0; i < levels.Length; i++)
                dto.levels[i] = new LevelRecordDto
                {
                    levelId = levels[i],
                    stars = 1,
                    bestMoves = 10,
                    clears = 1,
                };

            PlayerProgress.LoadFrom(dto);
        }

        /// <summary>A glade attempted and lost: a record exists, and it holds no star.</summary>
        static void Attempted(string level)
        {
            PlayerProgress.LoadFrom(new SaveFileDto
            {
                levels = new[]
                {
                    new LevelRecordDto { levelId = level, stars = 0, bestMoves = 0, clears = 0 },
                },
            });
        }

        /// <summary>Publishes a rules table whose only interesting field is the free window.</summary>
        static void Grace(int levels)
        {
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 10,
                hearts = new HeartsDto { graceLevels = levels },
            };

            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, new List<string>()));
            ProgressionRules.Publish(table);
        }

        /// <summary>
        /// Two chapters of the ordinary mode and two of a second one, which is the shape the
        /// whole rule turns on: a mode's own first chapter, not the catalog's.
        /// </summary>
        static CatalogIndex Catalog()
        {
            var builder = new CatalogIndexBuilder();
            builder.Add(new ManifestChapterDto
            {
                id = "c01_one", order = 10, version = 1,
                levels = new[] { "g1", "g2", "g3", "g4", "g5" },
            }, 1);
            builder.Add(new ManifestChapterDto
            {
                id = "c02_two", order = 20, version = 1,
                levels = new[] { "g6", "g7", "g8" },
            }, 1);
            builder.Add(new ManifestChapterDto
            {
                id = "b01_thicket", order = 30, version = 1, mode = "bud",
                levels = new[] { "w1", "w2", "w3", "w4" },
            }, 1);
            builder.Add(new ManifestChapterDto
            {
                id = "b02_grove", order = 40, version = 1, mode = "bud",
                levels = new[] { "w5", "w6" },
            }, 1);
            return builder.Build();
        }

        static bool Free(CatalogIndex index, string level)
            => HeartStake.IsFree(index, LevelId.Parse(level));

        // ------------------------------------------------------------------ the window
        [Test]
        public void TheShippedWindowIsTheFirstThreeOfAMode()
        {
            // Written down so that moving the built-in number is a deliberate act with a
            // failing test in front of it rather than a one-character edit nobody reviews.
            Assert.AreEqual(3, HeartLimits.DefaultGraceLevels);
            Assert.AreEqual(3, HeartRuleTable.Default.GraceLevels);
        }

        [Test]
        public void TheFirstThreeOfTheFirstChapterAreFreeAndTheFourthIsNot()
        {
            Grace(3);
            var index = Catalog();

            Assert.IsTrue(Free(index, "g1"));
            Assert.IsTrue(Free(index, "g2"));
            Assert.IsTrue(Free(index, "g3"));
            Assert.IsFalse(Free(index, "g4"), "the window closes on the fourth board");
            Assert.IsFalse(Free(index, "g5"));
        }

        [Test]
        public void LaterChaptersAreUntouched()
        {
            // The half of the rule that has to hold for the economy to be unmoved: a chapter
            // shipping in a year is priced exactly as the four before it were.
            Grace(3);
            var index = Catalog();

            Assert.IsFalse(Free(index, "g6"), "the head of the second chapter is not a beginning");
            Assert.IsFalse(Free(index, "g7"));
            Assert.IsFalse(Free(index, "g8"));
        }

        [Test]
        public void EveryModeGetsItsOwnOpening()
        {
            // Why the window is per mode rather than once per account. Budburst is tapped
            // rather than turned and is lost on a satchel rather than on turns, so somebody arriving at
            // it having finished four glade chapters is a beginner again in every sense that
            // decides whether a heart should be taken off them.
            Grace(3);
            var index = Catalog();

            Assert.IsTrue(Free(index, "w1"));
            Assert.IsTrue(Free(index, "w2"));
            Assert.IsTrue(Free(index, "w3"));
            Assert.IsFalse(Free(index, "w4"));
            Assert.IsFalse(Free(index, "w5"), "and not the mode's second chapter");
        }

        [Test]
        public void TheWindowStopsAtTheEndOfTheFirstChapter()
        {
            // A published number longer than the chapter it lands in must not spill into the
            // next one — "the first chapter is free" is what the information panel prints, and
            // a window running past it would make that sentence untrue.
            Grace(HeartLimits.MaxGraceLevels);
            var index = Catalog();

            Assert.IsTrue(Free(index, "g5"), "the whole of a five-level first chapter");
            Assert.IsFalse(Free(index, "g6"), "and nothing beyond it");
            Assert.IsFalse(Free(index, "w5"), "in either mode");
        }

        [Test]
        public void NoughtTurnsTheWindowOffEntirely()
        {
            Grace(0);
            var index = Catalog();

            Assert.IsFalse(Free(index, "g1"), "the very first board of the game");
            Assert.IsFalse(Free(index, "w1"));
        }

        // ------------------------------------------------------------- the safe direction
        [Test]
        public void AnythingTheCatalogDoesNotCarryIsPricedLikeEveryOtherGlade()
        {
            // The safe direction, and it is the opposite of the unlock rule's. A typo must not
            // be the thing that hands a glade out free, because a free glade is an economy
            // decision and a locked one is only a nuisance.
            Grace(3);
            var index = Catalog();

            Assert.IsFalse(Free(index, "nonesuch"));
            Assert.IsFalse(HeartStake.IsFree(index, LevelId.None));
            Assert.IsFalse(HeartStake.IsFree(null, LevelId.Parse("g1")),
                           "and a catalog that has not loaded charges rather than gives away");
        }

        // ------------------------------------------------------------- what the panel prints
        [Test]
        public void TheCountIsReportedPerChapterForThePanel()
        {
            Grace(3);
            var index = Catalog();

            Assert.AreEqual(3, HeartStake.FreeLevelsIn(index, ChapterId.Parse("c01_one")));
            Assert.AreEqual(0, HeartStake.FreeLevelsIn(index, ChapterId.Parse("c02_two")));
            Assert.AreEqual(3, HeartStake.FreeLevelsIn(index, ChapterId.Parse("b01_thicket")),
                            "a mode's own first chapter, not the catalog's");
            Assert.AreEqual(0, HeartStake.FreeLevelsIn(index, ChapterId.Parse("b02_grove")));
        }

        [Test]
        public void TheCountNeverExceedsTheChapterItDescribes()
        {
            // The panel says "the first N levels of this chapter", so N has to be a number the
            // chapter can actually hold. Two windows, one shorter chapter.
            Grace(4);
            var index = Catalog();

            Assert.AreEqual(4, HeartStake.FreeLevelsIn(index, ChapterId.Parse("c01_one")));
            Assert.AreEqual(4, HeartStake.FreeLevelsIn(index, ChapterId.Parse("b01_thicket")),
                            "exactly the whole of a four-level chapter");

            Grace(HeartLimits.MaxGraceLevels);
            Assert.AreEqual(5, HeartStake.FreeLevelsIn(index, ChapterId.Parse("c01_one")));
            Assert.AreEqual(4, HeartStake.FreeLevelsIn(index, ChapterId.Parse("b01_thicket")));
        }

        [Test]
        public void TheCountAndTheRuleAlwaysAgree()
        {
            // The panel's number and the charge are two readings of one window, and a panel
            // that promises a free board the run then charges for is worse than no panel. Held
            // together by walking every level of every mode rather than by trusting that they
            // are written from the same field.
            //
            // Asked of the opening clause rather than of the whole price, because the count is
            // the opening clause: what a replay costs is a fact about the player and not about
            // the chapter, and the panel states that one as a rule with no number in it.
            Grace(3);
            var index = Catalog();

            foreach (var mode in index.Modes)
            {
                var chapters = index.ChaptersIn(mode);
                for (int c = 0; c < chapters.Count; c++)
                {
                    var chapter = chapters[c];
                    int promised = HeartStake.FreeLevelsIn(index, chapter.Id);
                    int actual = 0;

                    for (int i = 0; i < chapter.LevelIds.Count; i++)
                        if (HeartStake.IsOpening(index, chapter.LevelIds[i])) actual++;

                    Assert.AreEqual(promised, actual, $"{chapter.Id.Value} says one thing and prices another");
                }
            }
        }

        // ------------------------------------------------------------------ the replay
        [Test]
        public void AGladeAlreadyFinishedIsFreeHoweverFarIntoTheGameItIs()
        {
            // The second clause. Nothing about position and nothing about the window: the
            // player beat this board, so going back to it cannot cost them anything.
            Grace(3);
            var index = Catalog();
            Finished("g8", "w6");

            Assert.IsTrue(Free(index, "g8"), "the last glade of the second chapter");
            Assert.IsTrue(Free(index, "w6"), "and in the other mode, on its own record");
            Assert.IsFalse(Free(index, "g7"), "its neighbour, which was never finished");
        }

        [Test]
        public void AGladeTriedAndLostStillCostsAHeart()
        {
            // The distinction the whole clause turns on, and one a record alone does not make:
            // attempting a glade writes a record too. Only a star says it was beaten, and only
            // a beaten board is free — otherwise one lost run would buy every later run on that
            // board, and the gate would stop existing for exactly whoever is stuck.
            Grace(3);
            var index = Catalog();
            Attempted("g4");

            Assert.IsFalse(Free(index, "g4"));
        }

        [Test]
        public void TheTwoClausesAreToldApartRatherThanMerelyCounted()
        {
            // The defeat panel prints one sentence per reason, so the reason has to be the true
            // one — "one of the free levels" over the last glade of a chapter is a panel nobody
            // believes twice.
            Grace(3);
            var index = Catalog();
            Finished("g2", "g8");

            Assert.AreEqual(HeartPrice.Opening, HeartStake.PriceOf(index, LevelId.Parse("g2")),
                            "an opening glade that also happens to be finished leads with the "
                            + "window, which is the half a beginner has not worked out yet");
            Assert.AreEqual(HeartPrice.Replay, HeartStake.PriceOf(index, LevelId.Parse("g8")));
            Assert.AreEqual(HeartPrice.Charged, HeartStake.PriceOf(index, LevelId.Parse("g7")));
            Assert.AreEqual(HeartPrice.Charged, HeartStake.PriceOf(index, LevelId.None));
        }

        [Test]
        public void AFinishedGladeIsFreeEvenWhenTheCatalogCannotNameIt()
        {
            // A clear is the record of a run that was won, and it means what it means whether
            // or not the index currently carries the glade — one held back by minAppVersion is
            // still a board they beat. Note it is not the typo case above: a record saying
            // "finished" cannot be produced by a mistyped id, only by a run that was won.
            Grace(3);
            Finished("g9");

            Assert.IsTrue(HeartStake.IsFree(Catalog(), LevelId.Parse("g9")));
            Assert.IsTrue(HeartStake.IsFree(null, LevelId.Parse("g9")),
                          "and before the catalog has loaded at all");
        }

        [Test]
        public void TheWindowBeingOffDoesNotTurnTheReplayRuleOff()
        {
            // The two clauses are independent, and only one of them is content. A market that
            // needed graceLevels pushed to nought must not lose the replay rule with it.
            Grace(0);
            var index = Catalog();
            Finished("g1");

            Assert.IsTrue(Free(index, "g1"), "finished, so free whatever the window says");
            Assert.IsFalse(Free(index, "g2"), "and its unfinished neighbour is not");
        }

        [Test]
        public void TheReplayRuleCountsNothingTowardsTheChaptersFreeOpenings()
        {
            // What the panel prints is about the chapter, so finishing glades must not inflate
            // it — a player replaying their way through a chapter would otherwise be told the
            // first ten levels of it are free.
            Grace(3);
            var index = Catalog();
            Finished("g1", "g2", "g3", "g4", "g5");

            Assert.AreEqual(3, HeartStake.FreeLevelsIn(index, ChapterId.Parse("c01_one")));
            Assert.AreEqual(0, HeartStake.FreeLevelsIn(index, ChapterId.Parse("c02_two")));
        }

        // ------------------------------------------------------ the gate on the way in
        //
        // A heart is charged when a run ends badly and the gate is asked when a run begins, and
        // the rule joining those two moments is that a run may only start if the player could
        // pay for it if it went wrong. It was written into the map's node tap and nowhere else,
        // so the victory panel's "next", an event's tile and — worst — the restart key all
        // opened charged runs on an empty bar. The restart was unbounded, because at nought
        // hearts the abandonment it pays for takes nothing at all: Wallet.TrySpendHeart reports
        // "already out" rather than refusing, so the board came back free, for ever.
        //
        // Everything below runs over plain integers, deliberately: what the gate is about is
        // arithmetic, and a rule that can stop somebody playing should be provable without a
        // wallet, a save or an Editor.

        [Test]
        public void AChargedRunNeedsAHeartAndAFreeOneNeverDoes()
        {
            Assert.IsFalse(HeartStake.CanBegin(HeartPrice.Charged, 0));
            Assert.IsTrue(HeartStake.CanBegin(HeartPrice.Charged, 1));

            // Both free clauses, on an empty bar. A run that costs nothing to lose cannot
            // coherently be refused for lack of something to lose — and the replay clause is
            // what keeps the whole of what somebody has beaten open while their hearts fill.
            Assert.IsTrue(HeartStake.CanBegin(HeartPrice.Opening, 0));
            Assert.IsTrue(HeartStake.CanBegin(HeartPrice.Replay, 0));
        }

        [Test]
        public void TheGateIsAHeartInHandRatherThanThePublishedDefeatCost()
        {
            // hearts > 0, exactly as Hearts.CanPlay and Wallet.TrySpendHeart both read it. A
            // published cost above one is a decision about how much a defeat takes, not about
            // who is allowed to sit down — reading it as an entry requirement would lock a
            // player out of the game holding a heart.
            Assert.Greater(HeartLimits.MaxDefeatCost, 1, "the cost is retunable, so this matters");
            Assert.IsTrue(HeartStake.CanBegin(HeartPrice.Charged, 1));
        }

        [Test]
        public void RestartingAChargedRunPaysForTheOneBeingLeftBeforeTheGateIsAsked()
        {
            // The bug this exists to refuse, in one line: one heart is enough to abandon a run
            // and nowhere near enough to begin another, and the old code did both anyway —
            // charging the heart, dropping the player to nought, and dealing a fresh board that
            // was never paid for. Two is the honest floor, and it is not a stricter rule than
            // the map's: leaving to the glades and walking back in spends the same heart and is
            // refused at exactly the same point.
            Assert.IsFalse(HeartStake.CanRestart(HeartPrice.Charged, 1, owed: true));
            Assert.IsTrue(HeartStake.CanRestart(HeartPrice.Charged, 2, owed: true));
        }

        [Test]
        public void RestartingOnAnEmptyBarIsRefusedHoweverOftenItIsAsked()
        {
            // The unbounded case as reported: at nought hearts the forfeit silently takes
            // nothing, so every previous restart succeeded and the gate had ceased to exist.
            for (int i = 0; i < 5; i++)
                Assert.IsFalse(HeartStake.CanRestart(HeartPrice.Charged, 0, owed: true),
                               "a restart cannot become free by being asked again");
        }

        [Test]
        public void ARestartBeforeTheRunIsCommittedIsPricedLikeAnEntryAndNotLikeTwo()
        {
            // Nothing has been charged yet, so there is nothing to pay for on the way out: a
            // player putting back a board they have not touched is asking the same question the
            // door already answered. Demanding two here would refuse the commonest harmless tap
            // in the game to somebody who had just legitimately walked through the door with
            // their last heart.
            Assert.IsTrue(HeartStake.CanRestart(HeartPrice.Charged, 1, owed: false));
            Assert.IsFalse(HeartStake.CanRestart(HeartPrice.Charged, 0, owed: false));
        }

        [Test]
        public void AFreeRunIsRestartedForNothingWhateverTheBarSays()
        {
            // Both clauses, committed and not. A mode's opening glades are where the one player
            // this gate would shut out is the one still working out what the verb is, and a
            // replay is a board they have already beaten — neither takes a heart on any exit,
            // so neither has anything for the gate to refuse.
            foreach (var price in new[] { HeartPrice.Opening, HeartPrice.Replay })
                foreach (bool owed in new[] { true, false })
                    Assert.IsTrue(HeartStake.CanRestart(price, 0, owed),
                                  price + " restart was refused on an empty bar");
        }

        [Test]
        public void ARestartIsTheSameAnswerAsLeavingAndWalkingBackIn()
        {
            // The property the rule is really about, over the whole range rather than at the
            // one boundary: a restart may go ahead exactly when the round trip through the map
            // would have been let through. Anything else and the header key is either a
            // loophole or a punishment.
            var index = Catalog();
            var glade = LevelId.Parse("g4");
            Grace(3);

            // Both clauses driven rather than assumed: outside the window, and finished
            // nothing. The [SetUp] above already says so, and saying it here as well is what
            // lets this run under a bare reflection runner that does not honour one.
            Finished();

            Assert.AreEqual(HeartPrice.Charged, HeartStake.PriceOf(index, glade),
                            "the fixture wants a charged glade");

            for (int hearts = 0; hearts <= 4; hearts++)
            {
                int afterLeaving = hearts - HeartRules.DefeatCost;

                Assert.AreEqual(HeartStake.CanBegin(index, glade, afterLeaving),
                                HeartStake.CanRestart(HeartPrice.Charged, hearts, owed: true),
                                "restart and the round trip disagreed at " + hearts + " hearts");
            }
        }

        [Test]
        public void TheFreeOnesAreThePrefixOfTheChapterRatherThanAnyThreeOfIt()
        {
            // A window that was not a prefix would read as arbitrary on the map: the player
            // meets these boards in order, so the free ones have to be the ones at the front.
            Grace(3);
            var index = Catalog();

            var chapter = index.FindChapter(ChapterId.Parse("c01_one"));
            bool charging = false;

            for (int i = 0; i < chapter.LevelIds.Count; i++)
            {
                bool free = HeartStake.IsFree(index, chapter.LevelIds[i]);
                if (!free) charging = true;
                else Assert.IsFalse(charging, "a free glade appeared after a charged one");
            }
        }
    }
}
