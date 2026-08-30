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

        // --------------------------------------------------- the shape of a wind-up
        /// <summary>
        /// <b>The property the escalation rests on: a deeper wave winds up bigger.</b>
        ///
        /// It cannot wind up for <em>longer</em> — <c>Wave</c> divides the ceiling across the
        /// chain, so wave nine of nine gets less time than the only wave of a one-wave tap — so
        /// amplitude is the only axis left, and a chain that did not grow along it would be nine
        /// identical events in a row.
        /// </summary>
        [Test]
        public void EveryWaveOfAChainWindsUpBiggerThanTheOneBefore()
        {
            for (int wave = 2; wave <= BudChain.Most; wave++)
                Assert.GreaterOrEqual(BudTempo.Swell(wave), BudTempo.Swell(wave - 1),
                                      $"wave {wave} swells less than wave {wave - 1}");

            Assert.Greater(BudTempo.Swell(BudChain.Most), BudTempo.Swell(1),
                           "the deepest wave a chain reaches swells no more than the first");
        }

        /// <summary>
        /// And it is bounded, because a flower is drawn at about .72 of its cell — so past this
        /// a bunch stops crowding its neighbours and the grid stops being a grid.
        /// </summary>
        [Test]
        public void AndNoWaveEverSwellsPastTheCeiling()
        {
            for (int wave = 1; wave <= 60; wave++)
                Assert.LessOrEqual(BudTempo.Swell(wave), BudTempo.SwellMost + .0001f,
                                   $"wave {wave} swells to {BudTempo.Swell(wave):0.00}");
        }

        [Test]
        public void AWindUpStartsAtRestAndEndsAtItsFullSwell()
        {
            for (int wave = 1; wave <= BudChain.Most; wave++)
            {
                Assert.AreEqual(1f, BudTempo.WindScale(0f, wave), .0001f,
                                $"wave {wave} starts somewhere other than rest");
                Assert.AreEqual(1f + BudTempo.Swell(wave), BudTempo.WindScale(1f, wave), .0001f,
                                $"wave {wave} does not reach its own swell");
            }
        }

        /// <summary>
        /// <b>It gathers before it grows, and that is the whole difference between "about to
        /// explode" and "getting bigger".</b> The dip is small and early, and it has to actually
        /// be below rest — a curve that only ever rises is a flower being inflated by something
        /// outside it.
        /// </summary>
        [Test]
        public void AndItDipsBelowRestBeforeItRises()
        {
            for (int wave = 1; wave <= BudChain.Most; wave++)
            {
                float lowest = 1f;
                float at = 0f;

                for (int k = 0; k <= 200; k++)
                {
                    float t = k / 200f;
                    float s = BudTempo.WindScale(t, wave);
                    if (s < lowest) { lowest = s; at = t; }
                }

                Assert.AreEqual(1f - BudTempo.Recoil, lowest, .0005f,
                                $"wave {wave} gathers to {lowest:0.000} rather than " +
                                $"{1f - BudTempo.Recoil:0.000}");
                Assert.AreEqual(BudTempo.Crouch, at, .02f,
                                $"wave {wave} is at its lowest {at:0.00} of the way through " +
                                "rather than at the end of the crouch");
            }
        }

        /// <summary>
        /// Both phases are monotonic. A wind-up that wavered would read as a wobble, which says
        /// "this flower is unwell" rather than "this flower is about to go off".
        /// </summary>
        [Test]
        public void AWindUpNeverChangesItsMindOnTheWayDownOrUp()
        {
            for (int wave = 1; wave <= BudChain.Most; wave++)
                for (int k = 1; k <= 200; k++)
                {
                    float a = (k - 1) / 200f, b = k / 200f;
                    bool gathering = b <= BudTempo.Crouch;

                    float from = BudTempo.WindScale(a, wave), to = BudTempo.WindScale(b, wave);

                    if (gathering)
                        Assert.LessOrEqual(to, from + .0001f,
                                           $"wave {wave} grows during its crouch at {b:0.00}");
                    else if (a >= BudTempo.Crouch)
                        Assert.GreaterOrEqual(to, from - .0001f,
                                              $"wave {wave} shrinks while growing at {b:0.00}");
                }
        }

        /// <summary>
        /// <b>The flower reaches full size early and stays there, and this is the case that
        /// matters most in this file.</b>
        ///
        /// <para>
        /// The wind-up first shipped as a single accelerating curve, which sounds right and is
        /// wrong: an accelerating curve is near its destination only at the very end, so a
        /// flower was within 5% of its peak for about 3% of the beat — <b>a frame and a half at
        /// 60fps</b>. The peak was a flash rather than a state, so making it bigger changed a
        /// number nobody could see, and it was reported from play as no change at all on a build
        /// that was genuinely running it.
        /// </para>
        /// <para>
        /// A third of the wind-up is the bar. It is asserted as a <em>dwell</em> rather than as
        /// a curve shape on purpose: what went wrong was not the easing, it was how long the
        /// gesture was legible for, and that is the thing to hold a line under.
        /// </para>
        /// </summary>
        [Test]
        public void AFlowerHoldsItsFullSizeLongEnoughToBeSeen()
        {
            for (int wave = 1; wave <= BudChain.Most; wave++)
            {
                float full = 1f + BudTempo.Swell(wave);
                int near = 0;

                for (int k = 0; k <= 1000; k++)
                    if (BudTempo.WindScale(k / 1000f, wave) >= full - .05f) near++;

                float dwell = near / 1001f;
                Assert.GreaterOrEqual(dwell, .30f,
                                      $"wave {wave} is within 5% of its peak for {dwell * 100f:0}% " +
                                      "of its wind-up, which is a flash rather than a size");
            }
        }

        /// <summary>
        /// And it really is a hold: nothing moves at all once the growing is done, so the last
        /// stretch is a flower sitting at full size waiting to go off.
        /// </summary>
        [Test]
        public void AndOnceItHasArrivedItDoesNotMoveAgain()
        {
            for (int wave = 1; wave <= BudChain.Most; wave++)
            {
                float full = 1f + BudTempo.Swell(wave);

                for (int k = 0; k <= 100; k++)
                {
                    float t = BudTempo.Peak + (1f - BudTempo.Peak) * (k / 100f);
                    Assert.AreEqual(full, BudTempo.WindScale(t, wave), .0001f,
                                    $"wave {wave} is still moving at {t:0.00}");
                }
            }
        }

        /// <summary>
        /// The whitening is held back while the flower is still growing — the charge's job is to
        /// say <em>which</em> flowers matched, and a bunch that goes white has stopped saying it
        /// — and spends the rest during the hold, where it is free to.
        /// </summary>
        [Test]
        public void AFlowerOnlyGoesCriticalOnceItHasStoppedGrowing()
        {
            Assert.AreEqual(0f, BudTempo.WindWhite(0f), .0001f);
            Assert.AreEqual(BudTempo.Matched, BudTempo.WindWhite(BudTempo.Peak), .0001f);
            Assert.AreEqual(BudTempo.Critical, BudTempo.WindWhite(1f), .0001f);

            for (int k = 1; k <= 200; k++)
            {
                float a = (k - 1) / 200f, b = k / 200f;
                Assert.GreaterOrEqual(BudTempo.WindWhite(b), BudTempo.WindWhite(a) - .0001f,
                                      $"the whitening goes backwards at {b:0.00}");
                if (b <= BudTempo.Peak)
                    Assert.LessOrEqual(BudTempo.WindWhite(b), BudTempo.Matched + .0001f,
                                       $"it is already {BudTempo.WindWhite(b):0.00} white at " +
                                       $"{b:0.00}, while it is still growing");
            }

            Assert.Less(BudTempo.Critical, 1f, "a flower that goes pure white has no colour left to burst in");
        }

        /// <summary>
        /// The grove's own answer climbs with the chain, and starts above the threshold at which
        /// a scale change on a whole screen is noticed at all. It replaced a punch of 1.2%, which
        /// was not.
        /// </summary>
        [Test]
        public void TheGroveHeavesHarderEveryWaveAndEnoughToBeFelt()
        {
            Assert.AreEqual(0f, BudTempo.Heave(0), .0001f, "a wave that does not count moves nothing");
            Assert.GreaterOrEqual(BudTempo.Heave(1), .02f, "the first heave is under the noticing threshold");

            for (int wave = 2; wave <= BudChain.Most; wave++)
                Assert.GreaterOrEqual(BudTempo.Heave(wave), BudTempo.Heave(wave - 1),
                                      $"wave {wave} heaves less than wave {wave - 1}");

            for (int wave = 1; wave <= 60; wave++)
                Assert.LessOrEqual(BudTempo.Heave(wave), .09f,
                                   $"wave {wave} throws the whole grove {BudTempo.Heave(wave) * 100f:0}%");
        }

        /// <summary>
        /// <b>The escalation has to happen over the waves the mode actually reaches.</b>
        /// <c>b01_thicket</c> is one board whose best opening tap runs three waves, and most taps
        /// run one or two — so a ladder spreading its range over nine spends almost all of it on
        /// waves nobody sees, which is how the first version came out invisible. Wave three has
        /// to be clearly bigger than wave one, and both clearly bigger than the flat 1.34 this
        /// replaced.
        /// </summary>
        [Test]
        public void TheLadderIsSpentOnTheWavesTheShippedBoardReaches()
        {
            const float WasFlat = .34f;

            Assert.Greater(BudTempo.Swell(1), WasFlat * 1.5f,
                           "the first wave — which is most taps — barely moved");
            Assert.Greater(BudTempo.Swell(3) - BudTempo.Swell(1), .35f,
                           "waves one to three are too close together to read as an escalation");
        }

        [Test]
        public void TheSpinClimbsWithTheChainAndIsCappedBeforeItBecomesAFlicker()
        {
            for (int wave = 2; wave <= BudChain.Most; wave++)
                Assert.GreaterOrEqual(BudTempo.WindSpin(wave), BudTempo.WindSpin(wave - 1));

            for (int wave = 1; wave <= 60; wave++)
                Assert.LessOrEqual(BudTempo.WindSpin(wave), BudTempo.SpinMost + .0001f);
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

        // ------------------------------------------------------------------ one bunch
        /// <summary>
        /// Three alike is the rule being met and nine alike is a third of the grove going at
        /// once, and the mode drew them identically until <c>BudChain.Blast</c> existed.
        /// </summary>
        [Test]
        public void ABiggerBunchIsDrawnBiggerAndTheRungsAreOrdered()
        {
            Assert.AreEqual(BudBlast.Small, BudChain.Blast(BudLayout.Bunch),
                            "the smallest bunch the rule allows is the bottom rung");
            Assert.AreEqual(BudBlast.Small, BudChain.Blast(BudChain.BigFrom - 1));
            Assert.AreEqual(BudBlast.Big, BudChain.Blast(BudChain.BigFrom));
            Assert.AreEqual(BudBlast.Big, BudChain.Blast(BudChain.HugeFrom - 1));
            Assert.AreEqual(BudBlast.Huge, BudChain.Blast(BudChain.HugeFrom));

            Assert.Greater(BudChain.Force(BudBlast.Big), BudChain.Force(BudBlast.Small));
            Assert.Greater(BudChain.Force(BudBlast.Huge), BudChain.Force(BudBlast.Big));

            Assert.AreEqual(1f, BudChain.Force(BudBlast.Small), .0001f,
                            "an ordinary burst is drawn exactly as it always was, so the rung " +
                            "is something added rather than a retune of what shipped");
        }

        /// <summary>
        /// And the loudest rung stays inside what a cell can hold. Every dimension of a burst is
        /// scaled by this one number, so a force that reached three would draw a single flower's
        /// ring across a third of the grove.
        /// </summary>
        [Test]
        public void ButNeverSoMuchBiggerThatOneBurstOwnsTheGrove()
        {
            for (int bunch = 0; bunch < 64; bunch++)
                Assert.LessOrEqual(BudChain.Force(BudChain.Blast(bunch)), 2.2f);
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
