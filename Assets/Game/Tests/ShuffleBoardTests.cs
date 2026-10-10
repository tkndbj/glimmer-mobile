using System;
using System.Collections.Generic;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Shuffle;
using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The chain that carries a Shuffle run to the Shuffle board, joint by joint -
    /// <see cref="EndlessBoardTests"/>' argument for the second lane.
    ///
    /// <para>
    /// A run reaches the board only if the ledger records it, the record asks for a sync, the
    /// merge keeps the larger best, the receipt's card carries it, the fingerprint moves so a
    /// publish is owed, and the publish gate lets a keeper who has only ever played Shuffle
    /// through. Each joint fails silently. The server half is <c>shuffleWave</c> in
    /// <c>functions/src/grove.ts</c>, driven by <c>firebase/functions/test/grove.mjs</c>.
    /// </para>
    /// </summary>
    public sealed class ShuffleBoardTests
    {
        static readonly LevelId Lane = LevelId.Parse("s13_shuffle");
        static readonly LevelId Other = LevelId.Parse("s99_elsewhere");

        [SetUp]
        public void Reset()
        {
            ShuffleLedger.LoadFrom(new SaveFileDto());
            CloudSaveService.ForgetSyncRequestForTests();
            SyncTriggers.Attach();
        }

        [TearDown]
        public void Restore()
        {
            ShuffleLedger.LoadFrom(new SaveFileDto());
            CloudSaveService.ForgetSyncRequestForTests();
        }

        static ShuffleBestDto[] Rows(params (string level, int wave)[] rows)
        {
            var dto = new ShuffleBestDto[rows.Length];
            for (int i = 0; i < rows.Length; i++)
                dto[i] = new ShuffleBestDto { level = rows[i].level, wave = rows[i].wave };
            return dto;
        }

        static SaveFileDto Saved(params (string level, int wave)[] rows)
            => new SaveFileDto { shuffleBest = Rows(rows) };

        // ------------------------------------------------------------- the ledger
        [Test]
        public void ABestIsAFloorAndTheLanesBestIsTheBestOfEveryRow()
        {
            Assert.IsTrue(ShuffleLedger.Record(Lane, 12));
            Assert.IsFalse(ShuffleLedger.Record(Lane, 9), "a worse run lowered the best");
            Assert.IsTrue(ShuffleLedger.Record(Other, 31));

            Assert.AreEqual(12, ShuffleLedger.BestFor(Lane));
            Assert.AreEqual(31, ShuffleLedger.Best);
        }

        [Test]
        public void ARunThatClearedNothingRecordsNothing()
        {
            Assert.IsFalse(ShuffleLedger.Record(Lane, 0));
            Assert.IsFalse(ShuffleLedger.Record(Lane, -3));
            Assert.AreEqual(0, ShuffleLedger.Best);
        }

        [Test]
        public void ABestIsBoundedLikeTheInfiniteLanes()
        {
            ShuffleLedger.Record(Lane, int.MaxValue);
            Assert.AreEqual(ShuffleLedger.MaxWave, ShuffleLedger.Best);
            Assert.AreEqual(Progression.EndlessLedger.MaxWave, ShuffleLedger.MaxWave,
                            "both lanes are bounded by the server's one MAX_WAVE");
        }

        [Test]
        public void TheMergeKeepsTheLargerBestWhicheverSideHoldsIt()
        {
            // The fault this ledger exists to close: the level record's `bestMoves` is joined by
            // the smaller, so a sync after a better run used to put the worse one back.
            var mine = Rows(("s13_shuffle", 9), ("b_level", 4));
            var theirs = Rows(("s13_shuffle", 14), ("a_level", 2));

            foreach (var joined in new[] { ShuffleLedger.Join(mine, theirs), ShuffleLedger.Join(theirs, mine) })
            {
                Assert.AreEqual(3, joined.Length);
                Assert.AreEqual("a_level", joined[0].level, "rows are written sorted, so SaveDelta can walk them");
                Assert.AreEqual("b_level", joined[1].level);
                Assert.AreEqual("s13_shuffle", joined[2].level);
                Assert.AreEqual(14, joined[2].wave);
            }
        }

        [Test]
        public void TheFullSaveMergeKeepsTheLargerShuffleBest()
        {
            var a = Saved(("s13_shuffle", 14));
            var b = Saved(("s13_shuffle", 9));
            b.updatedUnix = a.updatedUnix + 100;      // the newer file holds the worse run

            Assert.AreEqual(14, SaveMerge.Join(a, b).shuffleBest[0].wave);
            Assert.AreEqual(14, SaveMerge.Join(b, a).shuffleBest[0].wave);
        }

        [Test]
        public void AJoinDropsNoughtsAndClampsAndStopsAtTheRulesBound()
        {
            var many = new List<ShuffleBestDto>();
            for (int i = 0; i < ShuffleLedger.MaxRows + 10; i++)
                many.Add(new ShuffleBestDto { level = "l" + i.ToString("D3"), wave = 5 });
            many.Add(new ShuffleBestDto { level = "zero", wave = 0 });
            many.Add(null);
            many.Add(new ShuffleBestDto { level = "", wave = 9 });

            var joined = ShuffleLedger.Join(many.ToArray(), null);
            Assert.AreEqual(ShuffleLedger.MaxRows, joined.Length,
                            "a list longer than firestore.rules allows loses every save write");

            var big = ShuffleLedger.Join(Rows(("a", 1_000_000)), Rows());
            Assert.AreEqual(ShuffleLedger.MaxWave, big[0].wave);
        }

        [Test]
        public void AJoinAgainstNothingStillSorts()
        {
            // `CompanionLedger.Join`'s trap: handing one side straight back skips the sort, and
            // an unsorted file reads as changed on every launch.
            var joined = ShuffleLedger.Join(Rows(("b", 3), ("a", 5)), null);
            Assert.AreEqual("a", joined[0].level);
        }

        [Test]
        public void TheBestOffASaveReadsAsTheServerReadsIt()
        {
            Assert.AreEqual(0, ShuffleLedger.BestIn(null));
            Assert.AreEqual(0, ShuffleLedger.BestIn(new SaveFileDto()));
            Assert.AreEqual(31, ShuffleLedger.BestIn(Saved(("a", 12), ("b", 31), ("", 900))));
            Assert.AreEqual(0, ShuffleLedger.BestIn(Saved(("a", -9))));
            Assert.AreEqual(ShuffleLedger.MaxWave, ShuffleLedger.BestIn(Saved(("a", 1_000_000))));

            // An id no shipped catalog could hold is refused, as `shuffleWave` refuses it.
            Assert.AreEqual(3, ShuffleLedger.BestIn(Saved((new string('x', LevelId.MaxLength + 1), 400), ("a", 3))));

            // The walk stops at the rules' bound, as the server's does.
            var rows = new List<(string, int)>();
            for (int i = 0; i < ShuffleLedger.MaxRows; i++) rows.Add(("l" + i, 1));
            rows.Add(("late", 50));
            Assert.AreEqual(1, ShuffleLedger.BestIn(Saved(rows.ToArray())));
        }

        // ---------------------------------------------------------------- the chain
        [Test]
        public void ANewBestAsksForASyncAndALoadDoesNot()
        {
            ShuffleLedger.LoadFrom(Saved(("s13_shuffle", 40)));
            Assert.AreEqual(40, ShuffleLedger.Best);
            Assert.IsFalse(CloudSaveService.IsSyncPending, "a save being read asked for a sync");

            Assert.IsFalse(ShuffleLedger.Record(Lane, 12));
            Assert.IsFalse(CloudSaveService.IsSyncPending);

            Assert.IsTrue(ShuffleLedger.Record(Lane, 41));
            Assert.IsTrue(CloudSaveService.IsSyncPending,
                          "a run that beat the record never reaches the server promptly");
        }

        [Test]
        public void TheLedgerSurvivesTheSavesOwnRoundTrip()
        {
            ShuffleLedger.Record(Lane, 23);

            var dto = new SaveFileDto();
            ShuffleLedger.WriteInto(dto);
            ShuffleLedger.LoadFrom(new SaveFileDto());
            Assert.AreEqual(0, ShuffleLedger.Best);

            ShuffleLedger.LoadFrom(dto);
            Assert.AreEqual(23, ShuffleLedger.BestFor(Lane));
        }

        // ------------------------------------------------------------- the publish
        [Test]
        public void TheCardOfASaveCarriesTheShuffleBest()
        {
            var card = GroveCard.OfSave(Saved(("s13_shuffle", 17)), "uid", 3, 1_000L);

            Assert.AreEqual(17, card.ShuffleWave);
            Assert.AreEqual(0, card.BestWave, "a Shuffle best reached the Infinite lane's figure");
        }

        [Test]
        public void ANewShuffleBestOwesAPublishAndNoBestOwesNothingNew()
        {
            var before = new GroveCard("uid", "Fern", 4, 9, 1L);
            var still = new GroveCard("uid", "Fern", 4, 9, 1L, null, null, null, 0);
            var after = new GroveCard("uid", "Fern", 4, 9, 1L, null, null, null, 17);

            // A keeper who has never run Shuffle keeps the fingerprint they already had, so
            // shipping this board republishes nobody it does not concern.
            Assert.AreEqual(before.Fingerprint(), still.Fingerprint());
            Assert.AreNotEqual(before.Fingerprint(), after.Fingerprint());
        }

        [Test]
        public void AShuffleWaveAloneIsWorthPublishing()
        {
            // The Shuffle lane opens before the Infinite one, so this keeper is real.
            var shuffleOnly = new GroveCard("uid", "Fern", 6, 0, 1L, null, null, null, 12);
            Assert.IsTrue(GrovePublishPolicy.WorthPublishing(shuffleOnly));
            Assert.IsFalse(GrovePublishPolicy.WorthPublishing(new GroveCard("uid", "Fern", 6, 0, 1L)));
        }

        // ----------------------------------------------------------------- the tabs
        [Test]
        public void EveryTabIsABoardTheServerWritesAndWearsALanesName()
        {
            CollectionAssert.AreEqual(new[] { LeaderboardBoard.Endless, LeaderboardBoard.Shuffle },
                                      LeaderboardScreen.Offers);

            foreach (string boardId in LeaderboardScreen.Offers)
            {
                Assert.IsTrue(LeaderboardBoard.IsKnown(boardId), boardId);
                Assert.IsTrue(LeaderboardBoard.IsWaves(boardId), boardId + " draws rows in waves");
                Assert.DoesNotThrow(() => LeaderboardScreen.TabLabel(boardId), boardId + " has no tab name");
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => LeaderboardScreen.TabLabel(LeaderboardBoard.Global));
        }
    }
}
