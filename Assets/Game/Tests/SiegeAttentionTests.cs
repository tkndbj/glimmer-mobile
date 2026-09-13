using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The readings that say whether a player ever looked at the hill.
    ///
    /// <para>
    /// Worth pinning because every one of them fails silently and none of them can be checked by
    /// eye: a wrong average is a column of plausible seconds, and the fault it is meant to catch
    /// — a mode where the eye never leaves the gem field — looks exactly like a mode that is
    /// working. There is also no second chance at the data, because a run happens once.
    /// </para>
    /// <para>
    /// What is <em>not</em> tested here is that the board calls these in the right places; that
    /// is what a device and a dashboard are for. What is tested is the arithmetic underneath,
    /// which is the half a report cannot tell you is wrong.
    /// </para>
    /// </summary>
    public sealed class SiegeAttentionTests
    {
        /// <summary>
        /// Drives the clock the way a board does, one tick at a time.
        ///
        /// <para>
        /// <b>The loop counts ticks, not seconds.</b> Written as <c>for (float t = 0; t &lt;
        /// seconds; t += .1f)</c> it ran thirty or thirty-one times depending on which way
        /// <c>0.1f</c> accumulated, so three seconds of waiting measured 3.0 or 3.1 and three of
        /// these tests failed on arithmetic rather than on anything they were about. The same
        /// trap the board itself is held to: a float may not decide a count.
        /// </para>
        /// </summary>
        static void Wait(SiegeAttention seen, int seconds)
        {
            for (int tick = 0; tick < seconds * 10; tick++) seen.Tick(.1f);
        }

        [Test]
        public void AnUntouchedRunReadsAsNothing()
        {
            var seen = new SiegeAttention();

            Assert.AreEqual(0, seen.BombsDropped);
            Assert.AreEqual(0, seen.BombsTapped);
            Assert.AreEqual(0f, seen.MeanWait, "no tapped bombs is nought, not a division by zero");
            Assert.AreEqual(0f, seen.LongestWait);
        }

        [Test]
        public void ABombsWaitIsMeasuredFromWhenItWasDropped()
        {
            var seen = new SiegeAttention();

            seen.BombDropped(1);
            Wait(seen, 3);
            seen.BombTapped(1);

            Assert.AreEqual(1, seen.BombsDropped);
            Assert.AreEqual(1, seen.BombsTapped);
            Assert.AreEqual(3f, seen.MeanWait, .05f);
            Assert.AreEqual(3f, seen.LongestWait, .05f);
        }

        /// <summary>
        /// The measurement the whole event exists for. A bomb nobody tapped must not pull the
        /// average <em>down</em> — which is what folding in its current age would do, and it
        /// would do it harder the longer the bomb was ignored.
        /// </summary>
        [Test]
        public void ABombNobodyTappedDoesNotFlatterTheAverage()
        {
            var seen = new SiegeAttention();

            seen.BombDropped(1);
            Wait(seen, 2);
            seen.BombTapped(1);

            // Dropped and left standing for the rest of the run.
            seen.BombDropped(2);
            Wait(seen, 30);

            Assert.AreEqual(2, seen.BombsDropped);
            Assert.AreEqual(1, seen.BombsTapped);
            Assert.AreEqual(2f, seen.MeanWait, .05f,
                            "the average is over the bombs that were actually tapped");
            Assert.AreEqual(1, seen.BombsDropped - seen.BombsTapped,
                            "and the one nobody touched is counted as exactly that");
        }

        [Test]
        public void TheLongestWaitIsTheWorstOneNotTheLastOne()
        {
            var seen = new SiegeAttention();

            seen.BombDropped(1);
            Wait(seen, 9);
            seen.BombTapped(1);

            seen.BombDropped(2);
            Wait(seen, 1);
            seen.BombTapped(2);

            Assert.AreEqual(9f, seen.LongestWait, .05f);
            Assert.AreEqual(5f, seen.MeanWait, .05f);
        }

        /// <summary>
        /// A tap on a bomb the board never recorded must not invent a wait of the whole run so
        /// far. It still counts as a tap, because the player made the decision.
        /// </summary>
        [Test]
        public void ATapOnAnUnknownBombCountsButCarriesNoWait()
        {
            var seen = new SiegeAttention();

            Wait(seen, 20);
            seen.BombTapped(99);

            Assert.AreEqual(1, seen.BombsTapped);
            Assert.AreEqual(0f, seen.LongestWait, "no drop was recorded, so there is no wait");
        }

        [Test]
        public void CogsAreCountedThreeWays()
        {
            var seen = new SiegeAttention();

            seen.CogDropped();
            seen.CogDropped();
            seen.CogDropped();
            seen.CogTaken();
            seen.CogTrampled();

            Assert.AreEqual(3, seen.CogsDropped);
            Assert.AreEqual(1, seen.CogsTaken);
            Assert.AreEqual(1, seen.CogsTrampled);
        }

        /// <summary>
        /// Two numbers rather than a rate, so a run that met one boss and a run that met two are
        /// distinguishable — a ratio alone would read them as the same player.
        /// </summary>
        [Test]
        public void ABossIsCountedWhetherOrNotItsColourWasBanked()
        {
            var seen = new SiegeAttention();

            seen.BossMet(fuelled: true);
            seen.BossMet(fuelled: false);

            Assert.AreEqual(2, seen.BossesMet);
            Assert.AreEqual(1, seen.BossesMetFuelled);
        }

        /// <summary>
        /// The clock is the board's, not the wall's: a resumed app clamps its delta, and the
        /// readings have to be measured against the time the board actually played.
        /// </summary>
        [Test]
        public void TheClockOnlyCountsWhatTheBoardWasGiven()
        {
            var seen = new SiegeAttention();

            Wait(seen, 5);

            Assert.AreEqual(5f, seen.Elapsed, .05f);
        }
    }
}
