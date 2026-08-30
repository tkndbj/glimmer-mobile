using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// How a chain escalates, held to the claim that made it necessary.
    ///
    /// <para>
    /// <b>This fixture exists because the first attempt was invisible and shipped.</b> Every wave
    /// drew the same event and the escalation was carried entirely by numbers — a bigger swell, a
    /// harder shake, a brighter flash. Played through seven levels, that reads as no change at
    /// all, and it was reported in exactly those words. So the rule being tested is not "the
    /// numbers climb"; it is that <b>each wave draws a kind of thing the wave before it did
    /// not</b>, which is the only escalation anybody can actually see.
    /// </para>
    /// </summary>
    public sealed class BudSpectacleTests
    {
        [Test]
        public void EveryWaveOfAChainDrawsSomethingTheOneBeforeItDidNot()
        {
            int was = 0;

            for (int wave = 1; wave <= BudSpectacle.ConfettiFrom; wave++)
            {
                int kinds = BudSpectacle.Of(wave).Kinds;

                Assert.Greater(kinds, was,
                               $"wave {wave} draws {kinds} kind(s) of thing against wave " +
                               $"{wave - 1}'s {was} — a wave that adds nothing is a wave the " +
                               "player cannot tell from the last one");
                was = kinds;
            }

            Assert.AreEqual(BudSpectacle.MostKinds, was,
                            "and by the top rung every kind is on");
        }

        /// <summary>
        /// And nothing is ever taken away again. A layer that switched off would read as the
        /// chain running out of steam at the exact moment it is running hardest.
        /// </summary>
        [Test]
        public void AndNothingIsEverTakenAwayAgain()
        {
            var last = BudSpectacle.Of(1);

            for (int wave = 2; wave <= 40; wave++)
            {
                var now = BudSpectacle.Of(wave);

                Assert.IsTrue(!last.Sweep || now.Sweep, "the sweep went away at wave " + wave);
                Assert.IsTrue(!last.Fireworks || now.Fireworks, "the fireworks went away");
                Assert.IsTrue(!last.Rays || now.Rays, "the backlight went away");
                Assert.IsTrue(!last.Confetti || now.Confetti, "the confetti went away");
                Assert.GreaterOrEqual(now.Ripple, last.Ripple);
                Assert.GreaterOrEqual(now.Tint, last.Tint);
                Assert.GreaterOrEqual(now.Rockets, last.Rockets);

                last = now;
            }
        }

        /// <summary>
        /// The first three rungs land inside what the shipped boards actually run.
        ///
        /// <b>The mistake this prevents has already been made twice in this mode.</b>
        /// <c>BudTempo</c>'s first swell ladder spread its whole range over nine waves on a board
        /// whose best tap ran three, so the escalation was real and nobody ever saw it. Most taps
        /// here run one or two waves, so a ladder whose first new thing arrives at wave four is a
        /// ladder for a chain that hardly happens.
        /// </summary>
        [Test]
        public void TheFirstRungsLandOnWavesOrdinaryPlayActuallyReaches()
        {
            Assert.LessOrEqual(BudSpectacle.SweepFrom, 2,
                               "the second wave is the commonest chain in the mode, so it has " +
                               "to be the one that already looks different");
            Assert.LessOrEqual(BudSpectacle.FireworksFrom, 3);
            Assert.Greater(BudSpectacle.Of(1).Ripple, 0f,
                           "even a one-wave tap makes the rest of the grove move");
        }

        /// <summary>
        /// Nothing takes the screen. A tint past about a quarter of white takes the board away
        /// from the player at the moment they most want to watch it — <c>BudTempo.Bloom</c>'s
        /// bound, and this one stacks on top of it.
        /// </summary>
        [Test]
        public void AndTheColourNeverTakesTheBoardAway()
        {
            for (int wave = 1; wave <= 40; wave++)
            {
                var layers = BudSpectacle.Of(wave);

                Assert.LessOrEqual(layers.Tint, .25f);
                Assert.LessOrEqual(layers.Ripple, 1f);
                Assert.LessOrEqual(layers.Rockets, 8);
            }
        }

        // ------------------------------------------------------------------ the grove's jolt
        /// <summary>
        /// The jolt crosses the board inside the wave that threw it.
        ///
        /// <b>It is the one effect that touches every cell</b>, so a jolt still travelling when
        /// the next wave charges would put two gestures on the same transforms — the bug this
        /// mode's view has already paid for twice.
        /// </summary>
        [Test]
        public void TheJoltAlwaysCrossesTheGroveInsideItsOwnWave()
        {
            for (int waves = 1; waves <= BudChain.Most; waves++)
            {
                float burn = BudTempo.Burn(BudTempo.Wave(waves));
                float over = BudSpectacle.RippleOver(burn);

                Assert.Less(over, burn + .0001f,
                            $"a chain of {waves} gives each wave {burn:0.000}s to burn in and " +
                            $"the jolt takes {over:0.000}s to cross the grove");
                Assert.Greater(over, 0f);
            }
        }

        [Test]
        public void AndItFallsAwayWithDistanceRatherThanShakingEverythingAtOnce()
        {
            const float far = 100f;

            float near = BudSpectacle.RippleForce(1f, 0f, far);
            float mid = BudSpectacle.RippleForce(1f, 50f, far);
            float edge = BudSpectacle.RippleForce(1f, far, far);

            Assert.Greater(near, mid, "a cell beside the burst is knocked harder than one across " +
                                      "the grove, or the jolt reads as the whole board shaking");
            Assert.Greater(mid, edge);
            Assert.AreEqual(0f, edge, .0001f, "and it has died by the far edge");
        }

        [Test]
        public void AndItArrivesLaterTheFurtherOutItGets()
        {
            Assert.AreEqual(0f, BudSpectacle.RippleAt(0f, 100f), .0001f);
            Assert.Greater(BudSpectacle.RippleAt(60f, 100f), BudSpectacle.RippleAt(20f, 100f));
            Assert.LessOrEqual(BudSpectacle.RippleAt(400f, 100f), 1f,
                               "and a cell beyond the far edge does not arrive after the wave " +
                               "it belongs to has ended");
        }
    }
}
