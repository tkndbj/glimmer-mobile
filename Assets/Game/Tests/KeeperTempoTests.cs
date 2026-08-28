using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// How long a Groovekeeper planting takes, and where the celebration ladder stops.
    ///
    /// <para>
    /// <b>Motion is the one subsystem here whose failures show up only in play</b>, so the
    /// arithmetic lives in Domain and is held to its claims offline. The load-bearing one is that
    /// a cascade is <em>bounded</em>: the board is latched for exactly as long as one runs, so an
    /// unbounded cascade is an unbounded freeze, and the reward for a big flourish has to be the
    /// flourish rather than the waiting.
    /// </para>
    /// </summary>
    public sealed class KeeperTempoTests
    {
        [Test]
        public void ACascadeIsBoundedHoweverManyFlowersOpen()
        {
            for (int blooms = 1; blooms <= KeeperFlourish.Most + 4; blooms++)
                Assert.LessOrEqual(KeeperTempo.Cascade(blooms), KeeperTempo.Ceiling,
                                   blooms + " flower(s) runs past the ceiling");

            Assert.AreEqual(0f, KeeperTempo.Cascade(0));
        }

        [Test]
        public void ABiggerFlourishOpensFasterRatherThanLastingLonger()
        {
            // The rate giving way, expressed as division. A five that took five times as long as
            // a one would be a punishment wearing a celebration's clothes.
            float one = KeeperTempo.Petal(1);
            float five = KeeperTempo.Petal(KeeperFlourish.Most);

            Assert.Greater(one, 0f);
            Assert.LessOrEqual(five, one);
        }

        [Test]
        public void TheCountNeverOutlastsTheFlowerItBelongsTo()
        {
            for (int blooms = 1; blooms <= KeeperFlourish.Most; blooms++)
                Assert.LessOrEqual(KeeperTempo.CountPop(blooms), KeeperTempo.Petal(blooms) + 1e-4f,
                                   "the count for a " + blooms + "-flourish outlives its flower");
        }

        [Test]
        public void ThePitchClimbsAndThenHolds()
        {
            // Past a point a rising pitch stops reading as excitement and starts reading as a
            // fault, so it is bounded rather than open-ended.
            float last = 0f;
            for (int flower = 1; flower <= 12; flower++)
            {
                float pitch = KeeperTempo.Pitch(flower);
                Assert.GreaterOrEqual(pitch, last);
                Assert.LessOrEqual(pitch, 1.7f);
                last = pitch;
            }
        }

        [Test]
        public void TheEntranceIsBoundedAndUnrollsFromTheMiddle()
        {
            // The furthest cell still has a third of the entrance left to land in, so the board
            // is never still arriving when the first lesson goes up.
            for (int w = 4; w <= 9; w++)
                for (int h = 4; h <= 9; h++)
                    for (int x = 0; x < w; x++)
                        for (int y = 0; y < h; y++)
                        {
                            float delay = KeeperTempo.EntranceDelay(x, y, w, h);
                            Assert.GreaterOrEqual(delay, 0f);
                            Assert.LessOrEqual(delay, KeeperTempo.Entrance * .62f + 1e-4f);
                        }

            Assert.AreEqual(0f, KeeperTempo.EntranceDelay(2, 2, 5, 5),
                            "the middle of the grove arrives first");
        }

        [Test]
        public void AFlourishIsCountedFromTwoAndNamedFromThree()
        {
            // Most plantings that do anything open exactly one tile, so a count starting at one
            // would put a number on the screen almost every turn and mean nothing by the second
            // level.
            Assert.IsFalse(KeeperFlourish.Counts(1));
            Assert.IsTrue(KeeperFlourish.Counts(KeeperFlourish.CountFrom));

            Assert.IsNull(KeeperFlourish.WordKey(1));
            Assert.IsNull(KeeperFlourish.WordKey(KeeperFlourish.NameFrom - 1));
            Assert.IsNotNull(KeeperFlourish.WordKey(KeeperFlourish.NameFrom));
        }

        [Test]
        public void TheLadderStopsWhereTheRulesDo()
        {
            // Five is a fact about the board rather than a taste: a planting is checked against
            // the cell it lands on and the four beside it, so nothing can ever open six. That is
            // what makes the top rung mean something.
            Assert.AreEqual(KeeperFlourish.Most - KeeperFlourish.CountFrom, KeeperFlourish.TopTier);
            Assert.AreEqual(KeeperFlourish.Tier(KeeperFlourish.Most),
                            KeeperFlourish.Tier(KeeperFlourish.Most + 3));

            Assert.LessOrEqual(KeeperFlourish.PointsFor(20), 150);
            Assert.LessOrEqual(KeeperFlourish.WordPointsFor(20), 142);
        }

        [Test]
        public void EveryNamedFlourishHasItsOwnWord()
        {
            // Written out rather than built from the tier, for invariant 6's reason - so this is
            // the check that no two rungs quietly share one.
            string three = KeeperFlourish.WordKey(3);
            string four = KeeperFlourish.WordKey(4);
            string five = KeeperFlourish.WordKey(KeeperFlourish.Most);

            Assert.AreNotEqual(three, four);
            Assert.AreNotEqual(four, five);
            Assert.AreEqual(five, KeeperFlourish.WordKey(9), "past the top rung the word holds");
        }
    }

    /// <summary>
    /// Where the basket sits under a grove, and whether the things in it clear each other.
    ///
    /// <c>FallBand</c>'s argument: whether two things on a screen overlap is arithmetic, so it
    /// lives in Domain and gets a test rather than a paragraph — and every time it was a
    /// paragraph instead, the paragraph was wrong.
    /// </summary>
    public sealed class KeeperBandTests
    {
        [Test]
        public void TheBasketSitsInsideTheRoomTheBoardLeavesIt()
        {
            Assert.IsTrue(KeeperBand.Clears,
                          "the basket runs past the board's floor or into the home indicator");
        }

        [Test]
        public void TheQueueClearsTheTileInHandAndTheCount()
        {
            Assert.IsTrue(KeeperBand.QueueFits,
                          "the procession draws over the tile in hand, the count, or the edge of "
                          + "the plate");
        }

        [Test]
        public void TheProcessionShowsEnoughToPlanWith()
        {
            // Four is the tile in hand and three behind it. Fewer and a heartbed cannot be
            // counted towards; more and the plate stops being readable at a glance.
            Assert.GreaterOrEqual(KeeperBand.Lookahead, 3);
            Assert.LessOrEqual(KeeperBand.Lookahead, 5);
        }
    }
}
