using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using GlimmerGrove.Events;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The repeating season's arithmetic: which cycle is running, what id it wears, and what
    /// an id can be read back as.
    ///
    /// <para>
    /// <b>Every case here is offline and none of them touches <c>JsonUtility</c>, a
    /// <c>MonoBehaviour</c> or a clock.</b> That is deliberate and it is invariant 29e: a
    /// fixture that needs the Editor is the one nobody runs on the way past, and this is the
    /// arithmetic a whole feature's security rests on — the id a phone writes into a save row
    /// and a grant-log key, and the bound that decides how much a forged save can extract.
    /// </para>
    /// <para>
    /// The server's half of the same contract is <c>firebase/functions/test/season-pass.mjs</c>,
    /// which runs the same cases against <c>seasonCycleIndex</c> and <c>usableSeason</c>. Two
    /// copies of one rule, both run by a gate, which is the shape invariant 9a insists on
    /// wherever an arithmetic is written twice.
    /// </para>
    /// </summary>
    public sealed class SeasonCycleTests
    {
        const long Start = 1_789_430_400L;                  // 2026-09-15 00:00 UTC
        const long Period = 42L * 24L * 60L * 60L;          // the shipped six weeks

        static SeasonCycle Cycle(string id = "watch", long start = Start, long period = Period)
            => new SeasonCycle(id, start, period, Rungs(), "watch", 350);

        static IReadOnlyList<EventMilestone> Rungs() => new List<EventMilestone>
        {
            new EventMilestone(5, "wood", "silver"),
            new EventMilestone(10, "silver", "gold"),
        };

        // ------------------------------------------------------------------ validity
        [Test]
        public void ACycleWithNoPeriodIsNotValid()
        {
            Assert.IsFalse(Cycle(period: 0).IsValid);
            Assert.IsFalse(Cycle(period: -1).IsValid, "a period that runs backwards is not a period");
            Assert.IsFalse(Cycle(id: string.Empty).IsValid);
            Assert.IsFalse(new SeasonCycle("watch", Start, Period, null).IsValid,
                           "a ladder with no rungs pays nothing, so it is not a season");
            Assert.IsTrue(Cycle().IsValid);
        }

        // ------------------------------------------------------------------ the calendar
        [Test]
        public void TheCycleRunningIsFlooredElapsedTimeOverThePeriod()
        {
            var cycle = Cycle();

            Assert.AreEqual(0, cycle.IndexAt(Start));
            Assert.AreEqual(0, cycle.IndexAt(Start + Period - 1));
            Assert.AreEqual(1, cycle.IndexAt(Start + Period));
            Assert.AreEqual(37, cycle.IndexAt(Start + 37 * Period));
            Assert.AreEqual(37, cycle.IndexAt(Start + 38 * Period - 1));
        }

        /// <summary>
        /// A clock before the first cycle answers "no cycle", never cycle nought.
        ///
        /// C#'s integer division truncates toward zero, so <c>-1 / period</c> is <c>0</c> — a
        /// device whose clock is a day slow, or a build shipped a week before the season opens,
        /// would otherwise be inside cycle nought early and earning marks toward it.
        /// </summary>
        [Test]
        public void ABeforeTheFirstCycleThereIsNoCycle()
        {
            var cycle = Cycle();

            Assert.AreEqual(-1, cycle.IndexAt(Start - 1));
            Assert.AreEqual(-1, cycle.IndexAt(0));
            Assert.AreEqual(-1, cycle.IndexAt(long.MinValue / 2));
            Assert.IsNull(cycle.LiveAt(Start - 1));
        }

        [Test]
        public void ACycleBeyondTheIdCeilingIsNoCycle()
        {
            var cycle = Cycle();

            Assert.AreEqual(SeasonCycle.MaxIndex, cycle.IndexAt(Start + SeasonCycle.MaxIndex * Period));
            Assert.AreEqual(-1, cycle.IndexAt(Start + (SeasonCycle.MaxIndex + 1L) * Period),
                            "past the ceiling there is no id to mint, so there is no season");
        }

        [Test]
        public void AWindowIsTheCyclesOwnAndTheyAbut()
        {
            var cycle = Cycle();

            Assert.AreEqual(Start, cycle.StartOf(0));
            Assert.AreEqual(Start + Period, cycle.EndOf(0));
            Assert.AreEqual(cycle.EndOf(0), cycle.StartOf(1),
                            "back to back: one closes exactly as the next opens, with no gap");
            Assert.AreEqual(Start + 9 * Period, cycle.StartOf(9));
        }

        // ------------------------------------------------------------------ the bound
        /// <summary>
        /// The one rule that keeps invariant 47c true on a calendar that never ends.
        ///
        /// A forged save's reach used to be bounded by the ladder being a finite list; a
        /// recurrence has no list, so what bounds it is the clock — a cycle that has not opened
        /// does not exist, and the most any save can extract is one ladder per elapsed period.
        /// </summary>
        [Test]
        public void ACycleThatHasNotOpenedDoesNotExist()
        {
            var cycle = Cycle();

            Assert.IsTrue(cycle.HasOpenedBy(0, Start));
            Assert.IsFalse(cycle.HasOpenedBy(1, Start));
            Assert.IsFalse(cycle.HasOpenedBy(1, Start + Period - 1), "not one second early");
            Assert.IsTrue(cycle.HasOpenedBy(1, Start + Period), "and exactly on the boundary it has");

            Assert.IsFalse(cycle.HasOpenedBy(SeasonCycle.MaxIndex, Start),
                           "a forged far-future cycle is the case this exists for");
            Assert.IsFalse(cycle.HasOpenedBy(-1, Start));
            Assert.IsFalse(cycle.HasOpenedBy(SeasonCycle.MaxIndex + 1, long.MaxValue / 2));
        }

        [Test]
        public void AClosedCycleStillExists()
        {
            var cycle = Cycle();

            Assert.IsTrue(cycle.HasOpenedBy(0, Start + 9 * Period),
                          "a season's chests never expire, so a closed cycle is still resolvable");
            Assert.IsNotNull(cycle.EventById("watch_0000"));
        }

        // ------------------------------------------------------------------ ids
        [Test]
        public void AnIdIsTheStemAndAPaddedNumber()
        {
            var cycle = Cycle();

            Assert.AreEqual("watch_0000", cycle.IdFor(0));
            Assert.AreEqual("watch_0037", cycle.IdFor(37));
            Assert.AreEqual("watch_9999", cycle.IdFor(SeasonCycle.MaxIndex));
            Assert.AreEqual(string.Empty, cycle.IdFor(-1));
            Assert.AreEqual(string.Empty, cycle.IdFor(SeasonCycle.MaxIndex + 1));
        }

        /// <summary>
        /// The padding is what makes ordinal order calendar order, which the save's own row
        /// sort and the eviction at the ceiling both rely on without saying so.
        /// </summary>
        [Test]
        public void OrdinalOrderIsCalendarOrder()
        {
            var cycle = Cycle();

            var ids = new List<string> { cycle.IdFor(10), cycle.IdFor(2), cycle.IdFor(1) };
            ids.Sort(string.CompareOrdinal);

            CollectionAssert.AreEqual(new[] { "watch_0001", "watch_0002", "watch_0010" }, ids);
        }

        [Test]
        public void EveryIdRoundTrips()
        {
            var cycle = Cycle();

            foreach (int index in new[] { 0, 1, 9, 10, 99, 100, 1234, SeasonCycle.MaxIndex })
                Assert.AreEqual(index, cycle.IndexOf(cycle.IdFor(index)), cycle.IdFor(index));
        }

        /// <summary>
        /// Every other spelling names nothing.
        ///
        /// Two spellings of one cycle would be two save rows and two sets of grant-log keys for
        /// one ladder — which reads as a reset to the player who hits it and as nothing at all
        /// to everybody else.
        /// </summary>
        [Test]
        public void NoOtherSpellingNamesACycle()
        {
            var cycle = Cycle();

            foreach (var bad in new[]
            {
                "watch_37", "watch_00037", "watch_-001", "watch_", "watch", "watch_abcd",
                "Watch_0000", "watch_0000 ", " watch_0000", "other_0000", "watch_000a",
                "watch_0000_0000", string.Empty, null,
            })
                Assert.AreEqual(-1, cycle.IndexOf(bad), bad ?? "null");
        }

        [Test]
        public void AStemThatIsAPrefixOfAnotherDoesNotStealItsIds()
        {
            var watch = Cycle("watch");
            var watcher = Cycle("watcher");

            Assert.AreEqual(-1, watch.IndexOf("watcher_0001"),
                            "the stem must match exactly, not merely start the id");
            Assert.AreEqual(1, watcher.IndexOf("watcher_0001"));
        }

        /// <summary>
        /// An id is built with the invariant culture, always.
        ///
        /// A device set to a locale whose digits are not ASCII would otherwise write a save row
        /// and a claim id no other device can read and no server can parse — and it would do it
        /// silently, on that player's phone only.
        /// </summary>
        [Test]
        public void AnIdIsBuiltInTheInvariantCultureWhateverTheDeviceIsSetTo()
        {
            var was = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ar-SA");
                Assert.AreEqual("watch_0037", Cycle().IdFor(37));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = was;
            }
        }

        // ------------------------------------------------------------------ names
        [Test]
        public void ANameComesFromAPoolThatWraps()
        {
            Assert.AreEqual("ui.season.0.name", SeasonCycle.NameKeyFor(0));
            Assert.AreEqual("ui.season.1.name", SeasonCycle.NameKeyFor(1));
            Assert.AreEqual("ui.season.11.name", SeasonCycle.NameKeyFor(SeasonCycle.NamePoolSize - 1));
            Assert.AreEqual("ui.season.0.name", SeasonCycle.NameKeyFor(SeasonCycle.NamePoolSize),
                            "the thirteenth season wears the first name again");
            Assert.AreEqual("ui.season.5.name", SeasonCycle.NameKeyFor(SeasonCycle.NamePoolSize + 5));
        }

        /// <summary>
        /// A negative index cannot reach a key with a minus sign in it.
        ///
        /// C#'s <c>%</c> keeps the sign of the dividend, so the naive form would build
        /// <c>ui.season.-5.name</c> — a key that resolves to nothing, drawn as an empty banner
        /// on the hub's largest card.
        /// </summary>
        [Test]
        public void ANegativeIndexStillLandsInThePool()
        {
            Assert.AreEqual("ui.season.7.name", SeasonCycle.NameKeyFor(-5));
            Assert.IsFalse(SeasonCycle.NameKeyFor(-1).Contains("-"));
        }

        // ------------------------------------------------------------------ materialising
        [Test]
        public void ACycleBecomesAnOrdinarySeason()
        {
            var cycle = Cycle();
            var season = cycle.EventFor(3);

            Assert.IsNotNull(season);
            Assert.AreEqual("watch_0003", season.Id);
            Assert.AreEqual(Start + 3 * Period, season.StartUnix);
            Assert.AreEqual(Start + 4 * Period, season.EndUnix);
            Assert.AreEqual(350, season.PassGems);
            Assert.AreEqual("watch", season.Icon);
            Assert.AreEqual(2, season.Milestones.Count, "and it pays the authored ladder");
            Assert.IsTrue(season.IsValid);
            Assert.IsTrue(season.IsLiveAt(Start + 3 * Period));
            Assert.IsFalse(season.IsLiveAt(Start + 4 * Period));
        }

        [Test]
        public void ACycleTakesItsNameFromThePoolAndItsBlurbFromTheOneEverybodyShares()
        {
            var cycle = Cycle();

            Assert.AreEqual("ui.season.3.name", cycle.EventFor(3).NameKey);
            Assert.AreEqual(SeasonCycle.BlurbKey, cycle.EventFor(3).BlurbKey);
            Assert.AreEqual(SeasonCycle.BlurbKey, cycle.EventFor(99).BlurbKey,
                            "one blurb rather than one per slot: it says what a watch is");
        }

        /// <summary>
        /// An authored season still derives its keys from its id, which is the rule
        /// <see cref="SeasonCycle"/> is the single exception to.
        /// </summary>
        [Test]
        public void AnAuthoredSeasonStillNamesItselfAfterItsId()
        {
            var season = new GroveEvent("yule_feast", Start, Start + Period, Rungs());

            // Asserted through the derivation rather than against a spelled-out key, and that
            // is not squeamishness: a season's name key is *derived*, so the string table has
            // no entry for an id invented in a fixture — and `loc.py` scans this source for
            // key-shaped literals and refuses one it cannot find (invariant 6). Writing the
            // rule out is also the stronger assertion of the two.
            Assert.AreEqual(GroveEvent.DefaultNameKey("yule_feast"), season.NameKey);
            Assert.AreEqual(GroveEvent.DefaultBlurbKey("yule_feast"), season.BlurbKey);
            StringAssert.StartsWith("ui.event.", season.NameKey);
            StringAssert.EndsWith(".name", season.NameKey);
        }

        [Test]
        public void AnIdFromAnotherCycleMaterialisesNothing()
        {
            var cycle = Cycle();

            Assert.IsNull(cycle.EventById("other_0001"));
            Assert.IsNull(cycle.EventById("watch"));
            Assert.IsNull(cycle.EventById(null));
            Assert.IsNull(cycle.EventFor(-1));
        }

        [Test]
        public void AnInvalidCycleMaterialisesNothing()
        {
            var cycle = Cycle(period: 0);

            Assert.IsNull(cycle.LiveAt(Start));
            Assert.IsNull(cycle.EventFor(0));
            Assert.AreEqual(-1, cycle.IndexOf("watch_0000"));
        }
    }
}
