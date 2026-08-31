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

        /// <summary>
        /// <b>And every chain the shipped chapter actually produces is watchable, which is the
        /// bar the mode failed and the reason the ceiling moved.</b>
        ///
        /// <para>
        /// Reported as <em>"the animations happen too fast"</em>. The floor above was doing its
        /// job and was the wrong bar: <c>MinWave</c> asks whether a wave can be <em>told
        /// apart</em> from the one before it, and what a carnival needs is whether each of the
        /// things inside a wave can be <em>watched</em>. Every one of them is a fraction of the
        /// beat, so the beat is the number that decides it. <c>b01_thicket</c>'s finale opens on
        /// an eight-wave tap and every grove in the chapter runs three or more, so those are the
        /// depths the bar has to hold at — a bound satisfied only by chains the boards never
        /// reach is the mistake this mode has made twice already, in <c>BudTempo</c>'s first
        /// swell ladder and in <c>BudSpectacle</c>'s first rungs.
        /// </para>
        /// <para>
        /// A third of a second of wind-up is about the shortest gesture a player registers as
        /// deliberate rather than as a glitch, and it is asserted on the <em>deepest</em> chain
        /// because that is the one the division squeezes hardest.
        /// </para>
        /// </summary>
        [Test]
        public void EveryChainTheShippedChapterReachesIsSlowEnoughToBeWatched()
        {
            // The finale's opening tap, which is the deepest thing anybody meets.
            const int Deepest = 8;

            for (int waves = 1; waves <= Deepest; waves++)
            {
                float beat = BudTempo.Wave(waves);

                Assert.GreaterOrEqual(beat, .80f,
                                      $"a {waves}-wave chain deals each wave {beat:0.000}s, " +
                                      "which is what every effect inside it is a fraction of");
                Assert.GreaterOrEqual(BudTempo.Charge(beat), .30f,
                                      $"a {waves}-wave chain winds up in " +
                                      $"{BudTempo.Charge(beat):0.000}s, which is a flicker " +
                                      "rather than a bunch pointing at itself");
                Assert.GreaterOrEqual(BudTempo.Burn(beat), .40f,
                                      $"a {waves}-wave chain leaves {BudTempo.Burn(beat):0.000}s " +
                                      "for the burst, the ripple, the wash and the fall");
            }
        }

        /// <summary>
        /// And the bound is still a bound. A deep chain has to end — the ceiling moved because
        /// it was set where a cascade could not be watched, not because a cascade is allowed to
        /// run forever, and a mode that freezes the board for ten seconds is a mode nobody taps
        /// twice.
        /// </summary>
        [Test]
        public void AndTheLongestChainStillEndsWhileAnyoneIsStillWatching()
        {
            Assert.LessOrEqual(BudTempo.Cascade(BudChain.Most), 8.5f,
                               $"the deepest chain the ladder distinguishes runs for " +
                               $"{BudTempo.Cascade(BudChain.Most):0.00}s");
            // Everything the player waits through. The word is no longer *added* to the chain —
            // it rides the last wave's answer, so the two overlap by design (see `BudStage`, and
            // `BudStageTests.TheWordArrivesOnTheClimaxRatherThanAfterIt`). The bound has to be
            // stated over the whole thing anyway: an overlap is the reason it is affordable, not
            // a reason to stop counting it.
            float whole = BudTempo.Cascade(BudChain.Most) + BudTempo.Fanfare;

            Assert.LessOrEqual(whole, 11f,
                               $"the deepest chain and the word come to {whole:0.00}s");
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

        /// <summary>
        /// <b>And no two flowers of a wave are ever dealt in the same frame, however many there
        /// are.</b>
        ///
        /// <para>
        /// This is the property the ripple was written for and did not have. It clamped
        /// <c>nth × step</c> to an allowance, so past the point where the two crossed <em>every
        /// remaining flower landed on the allowance</em> — a wave of thirteen was dealt as four
        /// beats and then nine at once, and the bigger the wave the more of it went off
        /// together, which is precisely backwards. Nothing caught it because the old checks
        /// asked only that the ripple was ordered and that it ended inside its beat, and both
        /// of those are true of a clump.
        /// </para>
        /// </summary>
        [Test]
        public void EveryFlowerOfAWaveIsDealtAtItsOwnMoment()
        {
            for (int waves = 1; waves <= BudChain.Most; waves++)
            {
                float beat = BudTempo.Wave(waves);

                for (int inWave = 2; inWave <= 40; inWave++)
                {
                    float last = BudTempo.StaggerAt(0, inWave, beat);

                    for (int nth = 1; nth < inWave; nth++)
                    {
                        float at = BudTempo.StaggerAt(nth, inWave, beat);

                        Assert.Greater(at, last,
                                       $"in a {waves}-wave chain the {nth}th of {inWave} is " +
                                       $"dealt at the same instant as the one before it " +
                                       $"({at:0.0000}s)");
                        last = at;
                    }

                    Assert.LessOrEqual(last, beat * BudTempo.Spread + .0001f,
                                       $"a wave of {inWave} spreads past its own allowance");
                }
            }
        }

        /// <summary>
        /// <b>And the cocoons of a wave are dealt further apart than its flowers, but still
        /// inside the wave.</b>
        ///
        /// <para>
        /// Four cocoons opening is four separate payoffs rather than one gesture said four
        /// times, so they get a wider allowance than the bursts do — but a ripple that could
        /// reach the whole beat would go on opening cocoons after the chain that opened them had
        /// ended, and the last of them would arrive over the word at the end. Both halves are
        /// the point; either one alone is satisfiable by a rule that is wrong.
        /// </para>
        /// </summary>
        [Test]
        public void CocoonsAreDealtFurtherApartThanFlowersAndStillInsideTheWave()
        {
            Assert.Greater(BudTempo.GreetSpread, BudTempo.Spread,
                           "a cocoon opening is dealt no further from the next than a flower is");
            Assert.Less(BudTempo.GreetSpread, 1f,
                        "a wave's cocoons can still be opening after the wave has ended");

            for (int waves = 1; waves <= BudChain.Most; waves++)
            {
                float beat = BudTempo.Wave(waves);

                for (int of = 2; of <= 16; of++)
                {
                    float last = 0f;

                    for (int nth = 1; nth < of; nth++)
                    {
                        float at = BudTempo.StaggerAt(nth, of, beat, BudTempo.GreetSpread);

                        Assert.Greater(at, last,
                                       $"cocoon {nth} of {of} opens with the one before it");
                        Assert.Less(at, beat,
                                    $"cocoon {nth} of {of} opens after its own wave has ended");
                        last = at;
                    }

                    Assert.GreaterOrEqual(
                        last, BudTempo.StaggerAt(of - 1, of, beat) - .0001f,
                        $"a wave of {of} cocoons is packed tighter than a wave of {of} flowers");
                }
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
        /// <b>The escalation has to happen over the waves the mode actually reaches, and it has
        /// to stop somewhere.</b> Both halves were learned by shipping the wrong one.
        ///
        /// <para>
        /// <c>b01_thicket</c> is one board whose best opening tap runs three waves, and most taps
        /// run one or two — so a ladder spreading its range over nine spends almost all of it on
        /// waves nobody sees, which is how the first version came out invisible. That is the
        /// floor below, and it is why the reading is taken between wave one and wave three
        /// rather than across the whole chain.
        /// </para>
        /// <para>
        /// <b>The ceiling is the newer half and it is what this test was missing.</b> The answer
        /// to "invisible" was to raise the swell from a flat .34 to .62–1.20, and the raise was
        /// not what fixed it — <see cref="BudTempo.Peak"/>'s dwell was — so what shipped was a
        /// legible gesture <em>and</em> a flower swelling to half again wider than its own cell,
        /// thirteen at a time. Reported from play as <em>"when a chain reaction happens, it is
        /// too much"</em>. A ladder with a floor and no ceiling can only ever be corrected in
        /// one direction, and this one had to be corrected in the other.
        /// </para>
        /// </summary>
        [Test]
        public void TheLadderIsSpentOnTheWavesTheShippedBoardReachesAndStopsShortOfTooMuch()
        {
            Assert.Greater(BudTempo.Swell(3) - BudTempo.Swell(1), .12f,
                           "waves one to three are too close together to read as an escalation");

            // A flower is drawn at .78 of `_size`, which is .92 of a cell — about .72 of one.
            // Past a scale of about 1.55 it is wider than the square it stands in by more than
            // the plate's own lip, which is a grove losing its grid and an edge burst reaching
            // for the clip.
            for (int wave = 1; wave <= BudChain.Most; wave++)
                Assert.LessOrEqual(1f + BudTempo.Swell(wave), 1.55f,
                                   $"wave {wave} swells to {1f + BudTempo.Swell(wave):0.00} of a cell");
        }

        [Test]
        public void TheSpinClimbsWithTheChainAndIsCappedBeforeItBecomesAFlicker()
        {
            for (int wave = 2; wave <= BudChain.Most; wave++)
                Assert.GreaterOrEqual(BudTempo.WindSpin(wave), BudTempo.WindSpin(wave - 1));

            for (int wave = 1; wave <= 60; wave++)
                Assert.LessOrEqual(BudTempo.WindSpin(wave), BudTempo.SpinMost + .0001f);
        }

        // ------------------------------------------------------------------ the grove falling
        /// <summary>
        /// <b>One gravity, and nothing may bend it.</b>
        ///
        /// <para>
        /// This is <see cref="BudStage"/>'s rule 2 held at the arithmetic that implements it: a
        /// fall's length is its distance at <see cref="BudTempo.Pace"/> and depends on nothing
        /// else — not on how long the wave is, not on how many other pieces are coming down, not
        /// on which wave of the chain it is.
        /// </para>
        /// <para>
        /// <b>What it replaced is the fault the mode was reported for.</b> The old rule fitted a
        /// fall into whatever was left of a per-wave budget, so the moment a wave ran short the
        /// tall drops were the ones made to hurry — and inside one wave a six-row drop fell half
        /// again faster than the one-row drop beside it. That is not a pacing preference: a
        /// board whose pieces fall at three speeds at once does not read as a board falling, and
        /// it was reported as the flowers <em>"skipping frames"</em>, which at better than a
        /// third of a cell a frame is exactly what they were doing.
        /// </para>
        /// </summary>
        [Test]
        public void EveryPieceFallsAtTheModesOwnPaceWhateverElseIsHappening()
        {
            for (int rows = 1; rows <= 12; rows++)
            {
                float fall = BudTempo.Falling(rows);

                Assert.Greater(fall, 0f, $"a {rows}-row drop takes no time at all");
                Assert.AreEqual(BudTempo.Pace, rows * BudTempo.Curve / fall, .0001f,
                                $"a {rows}-row drop is not falling at the mode's own pace");
            }
        }

        /// <summary>
        /// And a taller fall takes longer, in proportion, which is the whole of what makes a
        /// board feel heavy: a flower dropping five rows and one dropping a single row in the
        /// same time reads as teleporting.
        /// </summary>
        [Test]
        public void AndATallerFallTakesLongerInProportion()
        {
            for (int rows = 2; rows <= 12; rows++)
            {
                Assert.Greater(BudTempo.Falling(rows), BudTempo.Falling(rows - 1),
                               $"a {rows}-row drop does not take longer than a {rows - 1}-row one");

                Assert.AreEqual(rows, BudTempo.Falling(rows) / BudTempo.Falling(1), .0001f,
                                $"a {rows}-row drop is not {rows} times a one-row drop");
            }
        }

        /// <summary>
        /// <b>And no piece ever moves far enough between two frames to tear.</b>
        ///
        /// The eye reads a moving picture as continuous while it overlaps itself frame to frame,
        /// so what has to be bounded is how far a flower travels in a sixtieth of a second — at
        /// its <em>fastest</em> instant, which is where the tearing is and which is
        /// <see cref="BudTempo.Curve"/> times its mean. In cells rather than pixels because a
        /// cell is the one length this mode has that is the same on every phone.
        /// </summary>
        [Test]
        public void NoPieceEverFallsFastEnoughToTear()
        {
            const float Frame = 1f / 60f;
            const float Most = .25f;            // a quarter of a cell between two frames

            for (int rows = 1; rows <= 12; rows++)
            {
                float fall = BudTempo.Falling(rows);
                float peak = rows / fall * BudTempo.Curve;

                Assert.LessOrEqual(peak * Frame, Most + .0001f,
                                   $"a {rows}-row drop covers {peak * Frame:0.000} of a cell in a "
                                   + "frame, which is a picture jumping rather than falling");
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
