using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Which of a wave's landings are heard.
    ///
    /// <para>
    /// A rule that decides which of twenty things is <em>skipped</em> is the kind that goes
    /// wrong quietly: nothing crashes, no board is misdrawn, and the only symptom is that the
    /// grove sounds thin in a way nobody can put a finger on. So the count, the spacing and the
    /// notes are all held to a line here rather than being read off a listen.
    /// </para>
    /// </summary>
    public sealed class BudChorusTests
    {
        /// <summary>
        /// <b>Never more than the pool can hold.</b> A wave's landings are never the only thing
        /// sounding — the bursts that made the holes are still ringing — so they may take half
        /// of <c>Audio.PlayOne</c>'s voices at most, or they take the sounds they are answering
        /// with them.
        /// </summary>
        [Test]
        public void NoWaveIsEverStruckMoreTimesThanThePoolWillCarry()
        {
            for (int of = 1; of <= 64; of++)
            {
                int voices = 0;
                for (int nth = 0; nth < of; nth++)
                    if (BudChorus.Voiced(nth, of)) voices++;

                Assert.LessOrEqual(voices, BudChorus.MostVoices,
                                   $"a wave of {of} landings is struck {voices} times");
            }
        }

        /// <summary>
        /// And exactly as many as it can carry, which is the other half. A rule that is merely
        /// bounded above could answer "none" and pass; what makes a shower sound like a shower
        /// is that a big wave really does get the full five.
        /// </summary>
        [Test]
        public void AndAWaveThatCanFillThePoolDoesFillIt()
        {
            for (int of = 1; of <= 64; of++)
            {
                int voices = 0;
                for (int nth = 0; nth < of; nth++)
                    if (BudChorus.Voiced(nth, of)) voices++;

                int want = of < BudChorus.MostVoices ? of : BudChorus.MostVoices;
                Assert.AreEqual(want, voices,
                                $"a wave of {of} landings is struck {voices} times rather than " +
                                $"{want}");
            }
        }

        /// <summary>
        /// <b>Spread across the wave rather than taken off the front, and this is the case that
        /// matters.</b> Voicing the first five of twenty is the obvious implementation and it is
        /// the wrong one: it sounds like a five-piece wave followed by silence, so the bigger
        /// the fall the earlier it appears to stop, which is exactly backwards. The bar is that
        /// the last piece to land is always in the back half of the run.
        /// </summary>
        [Test]
        public void TheVoicesAreSpreadAcrossTheWholeWaveAndNotTakenOffTheFront()
        {
            for (int of = BudChorus.MostVoices + 1; of <= 64; of++)
            {
                int last = -1;
                for (int nth = 0; nth < of; nth++)
                    if (BudChorus.Voiced(nth, of)) last = nth;

                Assert.Greater(last, (of - 1) / 2,
                               $"a wave of {of} landings goes quiet after the {last}th, which " +
                               "is the first half of it");
            }
        }

        /// <summary>
        /// The first piece to land is always struck. A shower whose opening arrival is silent
        /// reads as a missed cue however good the rest of it is.
        /// </summary>
        [Test]
        public void TheFirstOneToLandIsAlwaysHeard()
        {
            for (int of = 1; of <= 64; of++)
                Assert.IsTrue(BudChorus.Voiced(0, of),
                              $"the first of {of} landings is silent");
        }

        [Test]
        public void AndNothingOutsideTheWaveIsHeardAtAll()
        {
            Assert.IsFalse(BudChorus.Voiced(0, 0));
            Assert.IsFalse(BudChorus.Voiced(-1, 8));
            Assert.IsFalse(BudChorus.Voiced(8, 8));
            Assert.IsFalse(BudChorus.Voiced(99, 8));
        }

        // ------------------------------------------------------------------ the notes
        /// <summary>
        /// The run climbs and never falls back, because the grove is filling up rather than
        /// emptying — and it stays inside the steps it declares, so a caller cannot be handed a
        /// pitch off the end of the table.
        /// </summary>
        [Test]
        public void TheRunClimbsAndStaysOnItsOwnSteps()
        {
            for (int of = 1; of <= 64; of++)
            {
                float last = -1f;

                for (int nth = 0; nth < of; nth++)
                {
                    int rung = BudChorus.Rung(nth, of);
                    Assert.GreaterOrEqual(rung, 0);
                    Assert.Less(rung, BudChorus.Steps.Length,
                                $"the {nth}th of {of} lands on step {rung}");

                    float pitch = BudChorus.Pitch(nth, of);
                    Assert.GreaterOrEqual(pitch, last - .0001f,
                                          $"the run falls back at the {nth}th of {of}");
                    last = pitch;
                }
            }
        }

        /// <summary>
        /// <b>And every step is a pentatonic one.</b> These overlap each other and they overlap
        /// the bursts, and a set with a semitone in it will sooner or later put two landings a
        /// half-step apart on the same frame — which is the one interval that reads as a mistake
        /// rather than as a chord. It is <c>sfx.tsv</c>'s rule, held here because this is the
        /// one place in the game that picks a pitch by arithmetic rather than by hand.
        /// </summary>
        [Test]
        public void EveryStepOfTheRunIsAPentatonicOne()
        {
            // The root, a tone, a minor third, a fourth and a fifth, as frequency ratios.
            int[] semis = { 0, 2, 3, 5, 7 };

            Assert.AreEqual(semis.Length, BudChorus.Steps.Length,
                            "the run has grown a step nobody named an interval for");

            for (int i = 0; i < semis.Length; i++)
            {
                float want = Ratio(semis[i]);
                Assert.AreEqual(want, BudChorus.Steps[i], .002f,
                                $"step {i} is {BudChorus.Steps[i]:0.0000}, which is not " +
                                $"{semis[i]} semitones ({want:0.0000})");
            }
        }

        static float Ratio(int semitones)
            => (float)System.Math.Pow(2.0, semitones / 12.0);

        /// <summary>
        /// And the run sits under the mode's other voices. A landing is an answer to something
        /// louder that is still sounding, so it may not be pitched up into it.
        /// </summary>
        [Test]
        public void AndItStartsBelowEverythingElseTheGroveIsSaying()
        {
            Assert.Less(BudChorus.Base, 1f,
                        "the landings are pitched at or above the sound they are answering");
            Assert.Greater(BudChorus.Base, .5f,
                           "pitched this far down a short sample is a thud rather than a blop");
        }
    }
}
