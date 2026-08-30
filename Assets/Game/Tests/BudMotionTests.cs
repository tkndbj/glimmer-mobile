using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// How long a grove's chain takes, which is the one subsystem whose failures only show up
    /// in play — so the arithmetic lives in Domain and is proved without an Editor.
    ///
    /// <para>
    /// The property every one of these defends is the same: <b>the rate gives way, but never
    /// past the point where the eye can follow it.</b> A nine-wave chain is the best thing that
    /// happens in this mode and it must not become a nine-second freeze; it must also not become
    /// a blur, because the whole mode is the paying out.
    /// </para>
    /// </summary>
    public sealed class BudMotionTests
    {
        // ------------------------------------------------------------------ the cascade
        /// <summary>
        /// The ceiling holds over every chain a grove can actually produce.
        ///
        /// <b>Past that the floor governs, and that is the design rather than a leak.</b> Two
        /// bounds pull opposite ways — a chain may not take for ever, and a wave may not be too
        /// fast to read — and where they meet the floor wins, because a cascade nobody can
        /// follow pays out nothing and the whole mode is the paying out. `BudChain.Most` is the
        /// longest chain this ladder distinguishes and the shipped board's biggest tap is nine,
        /// so the crossover is a long way past anything a player meets.
        /// </summary>
        [Test]
        public void EveryChainAGroveCanProduceRunsInsideTheCeiling()
        {
            for (int waves = 1; waves <= BudChain.Most; waves++)
                Assert.LessOrEqual(BudTempo.Cascade(waves), BudTempo.Ceiling + .0001f,
                                   $"a {waves}-wave chain runs for " +
                                   $"{BudTempo.Cascade(waves):0.00}s");
        }

        [Test]
        public void AndAnAbsurdlyLongOneIsBoundedByTheFloorInstead()
        {
            for (int waves = 1; waves <= 60; waves++)
            {
                float most = BudTempo.Ceiling > BudTempo.MinWave * waves
                           ? BudTempo.Ceiling : BudTempo.MinWave * waves;

                Assert.LessOrEqual(BudTempo.Cascade(waves), most + .0001f,
                                   $"a {waves}-wave chain runs past both bounds at " +
                                   $"{BudTempo.Cascade(waves):0.00}s");
            }
        }

        [Test]
        public void AndNoWaveIsEverTooFastToRead()
        {
            for (int waves = 1; waves <= 40; waves++)
                Assert.GreaterOrEqual(BudTempo.Wave(waves), BudTempo.MinWave - .0001f);
        }

        // ------------------------------------------------------------------ inside one wave
        /// <summary>
        /// <b>The ripple may never eat the beat it lives in.</b> Everything in one wave goes off
        /// at the same instant as far as the model is concerned, and the view deals them a few
        /// tens of milliseconds apart so a wave of thirteen reads as thirteen things rather than
        /// as one flat flicker. If that spread could ever reach the beat, a long wave would still
        /// be going off when the next one started and the chain would stop reading as waves at
        /// all — which is the one thing the stagger was added to improve.
        /// </summary>
        [Test]
        public void TheRippleAlwaysEndsInsideItsOwnWave()
        {
            for (int waves = 1; waves <= 12; waves++)
            {
                float beat = BudTempo.Wave(waves);

                for (int inWave = 1; inWave <= 40; inWave++)
                    for (int nth = 0; nth < inWave; nth++)
                        Assert.Less(BudTempo.StaggerAt(nth, inWave, beat), beat,
                                    $"the {nth}th of {inWave} in a {waves}-wave chain is dealt " +
                                    $"after its own beat of {beat:0.000}s has ended");
            }
        }

        [Test]
        public void TheRippleRunsInOrderAndTheFirstOneIsImmediate()
        {
            float beat = BudTempo.Wave(3);

            Assert.AreEqual(0f, BudTempo.StaggerAt(0, 8, beat), .0001f,
                            "the first flower of a wave goes off on the beat, not after it");

            float last = -1f;
            for (int nth = 0; nth < 8; nth++)
            {
                float at = BudTempo.StaggerAt(nth, 8, beat);
                Assert.GreaterOrEqual(at, last, "the ripple never runs backwards");
                last = at;
            }
        }

        [Test]
        public void AWaveOfOneIsNotRippledAtAll()
        {
            for (int waves = 1; waves <= 9; waves++)
                Assert.AreEqual(0f, BudTempo.StaggerAt(0, 1, BudTempo.Wave(waves)), .0001f);
        }

        // ------------------------------------------------------------------ the charge
        /// <summary>
        /// **Every wave has a wind-up and a burst, and both fit inside it.** The charge is what
        /// gives the player the moment where they can see which flowers matched; if it could
        /// ever take the whole wave there would be no burst left inside the beat, and if it
        /// could reach nought the chain would go back to blinking.
        /// </summary>
        [Test]
        public void EveryWaveIsAWindUpAndABurstAndBothFitInsideIt()
        {
            for (int waves = 1; waves <= 40; waves++)
            {
                float wave = BudTempo.Wave(waves);
                float charge = BudTempo.Charge(wave);
                float burn = BudTempo.Burn(wave);

                Assert.Greater(charge, 0f, $"a {waves}-wave chain has no wind-up");
                Assert.Greater(burn, 0f, $"a {waves}-wave chain has no burst");
                Assert.LessOrEqual(charge + burn, wave + .0001f,
                                   $"in a {waves}-wave chain the wind-up and the burst come to " +
                                   $"{charge + burn:0.000}s against a wave of {wave:0.000}s");
            }
        }

        /// <summary>
        /// And the wind-up is long enough to be *seen* even on the longest chain. Below about a
        /// tenth of a second a spin is a flicker, which is the state the mode shipped in.
        /// </summary>
        [Test]
        public void TheWindUpIsNeverTooShortToRead()
        {
            for (int waves = 1; waves <= 40; waves++)
                Assert.GreaterOrEqual(BudTempo.Charge(BudTempo.Wave(waves)), .10f,
                                      $"a {waves}-wave chain winds up in " +
                                      $"{BudTempo.Charge(BudTempo.Wave(waves)):0.000}s");
        }

        /// <summary>
        /// A burst outlives the beat that spawned it, deliberately: one wave's petals are still
        /// falling while the next winds up, which is what makes a long chain one event.
        /// </summary>
        [Test]
        public void ThePetalsOutliveTheWaveThatThrewThem()
        {
            for (int waves = 1; waves <= 12; waves++)
            {
                float wave = BudTempo.Wave(waves);
                Assert.Greater(BudTempo.Shrapnel(wave), BudTempo.Burn(wave),
                               $"in a {waves}-wave chain the petals land before the wave ends");
            }
        }

        // ------------------------------------------------------------------ the chain
        [Test]
        public void ABoltLandsWellInsideTheBeatThatThrewIt()
        {
            for (int waves = 1; waves <= 12; waves++)
            {
                float beat = BudTempo.Wave(waves);

                Assert.Less(BudTempo.Strike(beat) + BudTempo.Linger(beat), beat + .0001f,
                            $"in a {waves}-wave chain a bolt is still fading when the next " +
                            "wave starts, so colour would arrive after the burst that sent it");
            }
        }

        // ------------------------------------------------------------------ the screen
        /// <summary>
        /// <b>Nought on an ordinary tap.</b> A flash on every tap is a flash that says nothing,
        /// and this mode's ordinary tap is one wave — the whole ladder exists to say <em>how</em>
        /// good, which is the same argument <c>BudChain</c> makes about the count.
        /// </summary>
        [Test]
        public void TheScreenOnlyAnswersAChainWorthCounting()
        {
            Assert.AreEqual(0f, BudTempo.Bloom(1), .0001f);
            Assert.AreEqual(0f, BudTempo.Bloom(BudChain.CountFrom - 1), .0001f);
            Assert.Greater(BudTempo.Bloom(BudChain.CountFrom), 0f);
        }

        [Test]
        public void AndItClimbsWithoutEverWhitingOutTheBoard()
        {
            float last = -1f;
            for (int waves = 1; waves <= 40; waves++)
            {
                float bloom = BudTempo.Bloom(waves);
                Assert.GreaterOrEqual(bloom, last, "a longer chain is never answered more quietly");
                Assert.LessOrEqual(bloom, .30f,
                                   "a flash past about a third of white takes the grove away " +
                                   "from the player at the moment they most want to watch it");
                last = bloom;
            }
        }
    }
}
