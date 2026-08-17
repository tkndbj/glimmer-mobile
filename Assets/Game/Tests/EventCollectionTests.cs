using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Events;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The floor that decides which of an event's rungs the player has actually taken.
    ///
    /// <para>
    /// This is the fourth monotonic floor in the save file and the arguments for it are the
    /// streak's, one version earlier — see <see cref="EventCollection"/>. What is worth
    /// pinning here is the part that is new: the sweep, the seeding of a file written before
    /// rewards were collected by hand, and the join. The <em>payout</em> side lives in
    /// <c>EventTests</c> and in the shared reward vectors, which both halves of the economy
    /// run.
    /// </para>
    /// </summary>
    public sealed class EventCollectionTests
    {
        const long Start = 1_700_000_000L;
        const long End = 1_701_000_000L;
        const long Inside = Start + 5_000L;
        const long Outside = Start - 5_000L;

        static GroveEvent Bloom() => new GroveEvent(
            "vector_bloom", Start, End,
            new[] { LevelId.Parse("plain_one"), LevelId.Parse("plain_two"), LevelId.Parse("generous_one") },
            new[] { new EventMilestone(1, 50), new EventMilestone(2, 90), new EventMilestone(3, 200) });

        static Dictionary<LevelId, LevelRecord> Records(params (string id, int stars, long at)[] rows)
        {
            var map = new Dictionary<LevelId, LevelRecord>();
            foreach (var (id, stars, at) in rows)
            {
                var levelId = LevelId.Parse(id);
                map[levelId] = new LevelRecord(levelId, stars, bestMoves: 10, clears: 1,
                                               firstClearedUnix: at, lastPlayedUnix: at);
            }
            return map;
        }

        static SaveFileDto File(bool seeded, params (string id, int goal)[] floors)
        {
            var rows = new EventStateDto[floors.Length];
            for (int i = 0; i < floors.Length; i++)
                rows[i] = new EventStateDto { id = floors[i].id, collectedGoal = floors[i].goal };

            return new SaveFileDto { eventsSeeded = seeded, events = rows };
        }

        [SetUp]
        public void Reset() => EventCollection.ResetForTests();

        [TearDown]
        public void Clear() => EventCollection.ResetForTests();

        // ------------------------------------------------------------ collecting
        [Test]
        public void AFileWithNoRowsHasTakenNothing()
        {
            EventCollection.LoadFrom(File(true));
            Assert.AreEqual(0, EventCollection.CollectedGoal("vector_bloom"));
            Assert.AreEqual(0, EventCollection.CollectedGoal(null));
        }

        [Test]
        public void ARungTheGladesHaveReachedCanBeTaken()
        {
            EventCollection.LoadFrom(File(true));

            var records = Records(("plain_one", 3, Inside));
            Assert.IsTrue(EventCollection.IsCollectable(Bloom(), new EventMilestone(1, 50), records));
        }

        [Test]
        public void ARungTheGladesHaveNotReachedCannotBeTaken()
        {
            EventCollection.LoadFrom(File(true));

            var records = Records(("plain_one", 3, Inside));
            Assert.IsFalse(EventCollection.IsCollectable(Bloom(), new EventMilestone(2, 90), records));
            Assert.AreEqual(0, EventCollection.Collect(Bloom(), 2, records),
                            "tapping a rung ahead of the glades hands nothing over");
        }

        /// <summary>
        /// A clear dated outside the window advances nothing, so it cannot make a rung
        /// collectable either. The entitlement and the tap are checked separately, and this
        /// is the entitlement half.
        /// </summary>
        [Test]
        public void AClearOutsideTheWindowDoesNotOpenARung()
        {
            EventCollection.LoadFrom(File(true));

            var records = Records(("plain_one", 3, Outside), ("plain_two", 3, Outside));
            Assert.IsFalse(EventCollection.IsCollectable(Bloom(), new EventMilestone(1, 50), records));
        }

        /// <summary>
        /// The only reading of a floor that cannot silently drop a reward: tapping a rung
        /// higher up takes everything under it in the same gesture.
        /// </summary>
        [Test]
        public void TakingALaterRungSweepsTheEarlierOnesWithIt()
        {
            EventCollection.LoadFrom(File(true));

            var records = Records(("plain_one", 3, Inside), ("plain_two", 3, Inside),
                                  ("generous_one", 3, Inside));

            Assert.AreEqual(3, EventCollection.Collect(Bloom(), 3, records));
            Assert.AreEqual(3, EventCollection.CollectedGoal("vector_bloom"));

            foreach (var milestone in Bloom().Milestones)
                Assert.IsTrue(EventCollection.IsCollected(Bloom(), milestone),
                              $"the rung at {milestone.Goal} should have been swept");
        }

        [Test]
        public void TakingTheSameRungTwiceHandsNothingOverAgain()
        {
            EventCollection.LoadFrom(File(true));

            var records = Records(("plain_one", 3, Inside), ("plain_two", 3, Inside));

            Assert.AreEqual(2, EventCollection.Collect(Bloom(), 2, records));
            Assert.AreEqual(0, EventCollection.Collect(Bloom(), 2, records));
            Assert.AreEqual(0, EventCollection.Collect(Bloom(), 1, records),
                            "nor does tapping one already under the floor");
            Assert.AreEqual(2, EventCollection.CollectedGoal("vector_bloom"));
        }

        [Test]
        public void TheFloorNeverFalls()
        {
            EventCollection.LoadFrom(File(true, ("vector_bloom", 3)));

            var records = Records(("plain_one", 3, Inside));
            Assert.AreEqual(0, EventCollection.Collect(Bloom(), 1, records));
            Assert.AreEqual(3, EventCollection.CollectedGoal("vector_bloom"));
        }

        // -------------------------------------------------------------- seeding
        /// <summary>
        /// The migration. A file written before v11 has already been paid for every rung it
        /// reached, so those start collected — otherwise the page offers a returning player
        /// three flowers whose credits are already in their balance, and tapping them moves
        /// no number at all.
        /// </summary>
        [Test]
        public void AnUnseededFileMarksEverythingAlreadyReachedAsTaken()
        {
            EventCollection.LoadFrom(File(false));

            var records = Records(("plain_one", 3, Inside), ("plain_two", 3, Inside));
            EventCollection.SeedIfNeeded(new[] { Bloom() }, records);

            Assert.AreEqual(2, EventCollection.CollectedGoal("vector_bloom"),
                            "both reached rungs had already been paid under the old rule");
            Assert.IsFalse(EventCollection.IsCollectable(Bloom(), new EventMilestone(2, 90), records));
        }

        [Test]
        public void SeedingAFileThatReachedNothingTakesNothing()
        {
            EventCollection.LoadFrom(File(false));
            EventCollection.SeedIfNeeded(new[] { Bloom() }, Records());

            Assert.AreEqual(0, EventCollection.CollectedGoal("vector_bloom"));
        }

        /// <summary>
        /// Seeding is one-shot. A player who takes a rung, plays another glade and comes back
        /// must be offered the new one — a second seed would mark it collected behind them.
        /// </summary>
        [Test]
        public void SeedingRunsOnceAndNeverAgain()
        {
            EventCollection.LoadFrom(File(false));
            EventCollection.SeedIfNeeded(new[] { Bloom() }, Records(("plain_one", 3, Inside)));
            Assert.AreEqual(1, EventCollection.CollectedGoal("vector_bloom"));

            var more = Records(("plain_one", 3, Inside), ("plain_two", 3, Inside),
                               ("generous_one", 3, Inside));
            EventCollection.SeedIfNeeded(new[] { Bloom() }, more);

            Assert.AreEqual(1, EventCollection.CollectedGoal("vector_bloom"),
                            "the later rungs are the player's to take, not the seeder's to close");
            Assert.IsTrue(EventCollection.IsCollectable(Bloom(), new EventMilestone(3, 200), more));
        }

        [Test]
        public void ASeededFileIsNeverSeededAgainOnALaterLaunch()
        {
            EventCollection.LoadFrom(File(true));
            EventCollection.SeedIfNeeded(new[] { Bloom() },
                                         Records(("plain_one", 3, Inside), ("plain_two", 3, Inside)));

            Assert.AreEqual(0, EventCollection.CollectedGoal("vector_bloom"),
                            "this file was written by a build that already collected by hand");
        }

        // ----------------------------------------------------------------- join
        [Test]
        public void JoiningFloorsTakesTheLargerOfEach()
        {
            var joined = EventCollection.Join(
                new[] { new EventStateDto { id = "a", collectedGoal = 3 },
                        new EventStateDto { id = "b", collectedGoal = 1 } },
                new[] { new EventStateDto { id = "a", collectedGoal = 1 },
                        new EventStateDto { id = "b", collectedGoal = 4 } });

            Assert.AreEqual(3, Goal(joined, "a"));
            Assert.AreEqual(4, Goal(joined, "b"));
        }

        /// <summary>
        /// A join in the strict sense, which is what lets any number of devices sync in any
        /// order. Invariant 11.
        /// </summary>
        [Test]
        public void TheJoinIsIdempotentAndOrderIndependent()
        {
            var mine = new[] { new EventStateDto { id = "a", collectedGoal = 2 } };
            var other = new[] { new EventStateDto { id = "a", collectedGoal = 5 },
                                new EventStateDto { id = "b", collectedGoal = 1 } };

            var once = EventCollection.Join(mine, other);
            var twice = EventCollection.Join(once, other);
            var flipped = EventCollection.Join(other, mine);

            Assert.AreEqual(Goal(once, "a"), Goal(twice, "a"));
            Assert.AreEqual(Goal(once, "b"), Goal(twice, "b"));
            Assert.AreEqual(Goal(once, "a"), Goal(flipped, "a"));
            Assert.AreEqual(Goal(once, "b"), Goal(flipped, "b"));
        }

        /// <summary>
        /// A device on last month's content has never heard of this month's event. Dropping
        /// the row it does not recognise would take the reward back off the device that does.
        /// </summary>
        [Test]
        public void AnEventOnlyOneSideKnowsAboutSurvives()
        {
            var joined = EventCollection.Join(
                new[] { new EventStateDto { id = "old", collectedGoal = 2 } },
                new[] { new EventStateDto { id = "new", collectedGoal = 1 } });

            Assert.AreEqual(2, Goal(joined, "old"));
            Assert.AreEqual(1, Goal(joined, "new"));
        }

        [Test]
        public void AnEmptyOrMissingSideKeepsTheOther()
        {
            var mine = new[] { new EventStateDto { id = "a", collectedGoal = 2 } };

            Assert.AreEqual(2, Goal(EventCollection.Join(mine, null), "a"));
            Assert.AreEqual(2, Goal(EventCollection.Join(null, mine), "a"));
            Assert.AreEqual(2, Goal(EventCollection.Join(mine, new EventStateDto[0]), "a"));
            Assert.IsNotNull(EventCollection.Join(null, null));
        }

        /// <summary>
        /// Two rows for one event is a malformed file, not two tracks. The larger wins, for
        /// the same reason the merge takes the larger.
        /// </summary>
        [Test]
        public void ADuplicatedRowCollapsesToItsLargestValue()
        {
            var joined = EventCollection.Join(
                new[] { new EventStateDto { id = "a", collectedGoal = 1 },
                        new EventStateDto { id = "a", collectedGoal = 4 } },
                null);

            Assert.AreEqual(1, joined.Length);
            Assert.AreEqual(4, Goal(joined, "a"));
        }

        // ---------------------------------------------------------------- write
        /// <summary>
        /// Written sorted, because <c>SaveDelta</c> walks these in order and
        /// <c>SaveChecksum</c> hashes them. Dictionary order would make an unchanged save
        /// look changed on every launch — a write and an upload, for ever, for nothing.
        /// </summary>
        [Test]
        public void FloorsAreWrittenSortedByEventId()
        {
            EventCollection.LoadFrom(File(true, ("zeta", 1), ("alpha", 2), ("mid", 3)));

            var dto = new SaveFileDto();
            EventCollection.WriteInto(dto);

            Assert.AreEqual(3, dto.events.Length);
            Assert.AreEqual("alpha", dto.events[0].id);
            Assert.AreEqual("mid", dto.events[1].id);
            Assert.AreEqual("zeta", dto.events[2].id);
        }

        [Test]
        public void AWriteFollowedByAReadChangesNothing()
        {
            EventCollection.LoadFrom(File(true, ("a", 2), ("b", 5)));

            var dto = new SaveFileDto();
            EventCollection.WriteInto(dto);

            EventCollection.ResetForTests();
            EventCollection.LoadFrom(dto);

            Assert.IsTrue(dto.eventsSeeded);
            Assert.AreEqual(2, EventCollection.CollectedGoal("a"));
            Assert.AreEqual(5, EventCollection.CollectedGoal("b"));
        }

        /// <summary>
        /// Having been through a build that collects by hand cannot be undone, so the seeded
        /// flag survives a round trip through a file that never carried floors.
        /// </summary>
        [Test]
        public void TheSeededFlagSurvivesAFileWithNoFloorsInIt()
        {
            EventCollection.LoadFrom(File(true));

            var dto = new SaveFileDto();
            EventCollection.WriteInto(dto);

            Assert.IsTrue(dto.eventsSeeded);
            Assert.IsNotNull(dto.events, "an empty calendar is an empty array, never null");
            Assert.AreEqual(0, dto.events.Length);
        }

        static int Goal(EventStateDto[] rows, string id)
        {
            foreach (var row in rows)
                if (string.Equals(row.id, id, StringComparison.Ordinal)) return row.collectedGoal;

            return -1;
        }
    }
}
