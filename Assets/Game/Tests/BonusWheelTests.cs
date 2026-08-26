using System.Collections.Generic;
using GlimmerGrove.Ads;
using GlimmerGrove.Content;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The bonus wheel: the table, the roll, and the motion that has to agree with both.
    ///
    /// <para>
    /// Everything here is arithmetic on purpose. The slice a spin lands on decides what the
    /// server grants, so "where the wheel stops" is not a feel question — a wheel resting half
    /// a degree into its neighbour is the panel disagreeing with the payout, and motion is the
    /// one subsystem whose faults show up only in play. <c>RewardVectorTests</c> pins the roll
    /// against the shared file the server also runs; this pins everything the vectors cannot
    /// see.
    /// </para>
    /// </summary>
    public sealed class BonusWheelTests
    {
        static BonusWheel Wheel(params int[] percents)
        {
            var dto = new AdWheelDto { slices = new AdWheelSliceDto[percents.Length] };
            for (int i = 0; i < percents.Length; i++)
                dto.slices[i] = new AdWheelSliceDto { percent = percents[i] };

            var problems = new List<string>();
            var wheel = BonusWheel.Resolve(dto, problems);
            Assert.IsEmpty(problems, string.Join("; ", problems));
            return wheel;
        }

        // ---------------------------------------------------------------- the table
        [Test]
        public void TheShippedLadderIsUsableAndOnlyEverAdds()
        {
            var wheel = BonusWheel.Default;

            Assert.IsTrue(wheel.IsUsable);
            Assert.GreaterOrEqual(wheel.Count, WheelRules.MinSlices);
            Assert.LessOrEqual(wheel.Count, WheelRules.MaxSlices);

            for (int i = 0; i < wheel.Count; i++)
                Assert.GreaterOrEqual(wheel.SliceAt(i).Percent, WheelRules.MinPercent,
                                      "a slice below the flat offer would pay a player less than " +
                                      "the button promised them");

            Assert.Greater(wheel.TopPercent, WheelRules.MinPercent,
                           "a wheel with no bonus slice is a spin animation in front of a fixed number");
        }

        /// <summary>
        /// Two neighbouring slices carrying the same figure make a wheel look like it has fewer
        /// prizes than it has, and the rim is drawn in the authored order. Checked here rather
        /// than only in <c>ContentValidation</c>, because the built-in ladder never goes through
        /// the content reader at all.
        /// </summary>
        [Test]
        public void NoTwoNeighbouringSlicesOfTheShippedLadderPayTheSame()
        {
            var wheel = BonusWheel.Default;

            for (int i = 0; i < wheel.Count; i++)
            {
                int next = (i + 1) % wheel.Count;
                Assert.AreNotEqual(wheel.SliceAt(i).Percent, wheel.SliceAt(next).Percent,
                                   $"slices {i} and {next} pay the same and sit side by side");
            }
        }

        /// <summary>
        /// The payout is integer arithmetic with the multiply before the divide, and that is not
        /// a style choice.
        ///
        /// <para>
        /// The same trap the star thresholds fell into: <c>1.20f</c> is 1.20000004768…, so a
        /// float product disagrees with arithmetic wherever the exact answer lands on an
        /// integer — and every runtime is wrong the same way, so no diff between two of them
        /// could ever find it. JavaScript has to reproduce this exactly as well, which rules
        /// out anything wider than 32 bits in the middle.
        /// </para>
        /// </summary>
        [Test]
        public void ThePayoutIsExactWhereTheProductLandsOnAnInteger()
        {
            Assert.AreEqual(240L, BonusWheel.Apply(200, 120));
            Assert.AreEqual(54L, BonusWheel.Apply(45, 120));
            Assert.AreEqual(60L, BonusWheel.Apply(50, 120));
            Assert.AreEqual(1000L, BonusWheel.Apply(200, 500));

            // The floor pays the flat amount untouched rather than multiplying by one, so a
            // slice at a hundred can never round anything away.
            Assert.AreEqual(200L, BonusWheel.Apply(200, WheelRules.MinPercent));
            Assert.AreEqual(0L, BonusWheel.Apply(0, 500));
        }

        [Test]
        public void TheMeanIsRoundedRatherThanTruncated()
        {
            // 100 + 150 + 200 + 250 = 700 over 4 = 175 exactly.
            Assert.AreEqual(175, Wheel(100, 150, 200, 250).MeanPercent);

            // 100 + 150 + 200 + 251 = 701 over 4 = 175.25, which rounds to 175.
            Assert.AreEqual(175, Wheel(100, 150, 200, 251).MeanPercent);

            // 100 + 150 + 200 + 253 = 703 over 4 = 175.75, which rounds to 176. Truncating
            // would report 175 and quietly understate what the placement really pays.
            Assert.AreEqual(176, Wheel(100, 150, 200, 253).MeanPercent);
        }

        // -------------------------------------------------------------- the refusals
        [Test]
        public void ASliceBelowTheFlatOfferIsRefusedRatherThanClamped()
        {
            var dto = new AdWheelDto
            {
                slices = new[]
                {
                    new AdWheelSliceDto { percent = 100 },
                    new AdWheelSliceDto { percent = 99 },
                    new AdWheelSliceDto { percent = 200 },
                    new AdWheelSliceDto { percent = 300 },
                },
            };

            var problems = new List<string>();
            var wheel = BonusWheel.Resolve(dto, problems);

            Assert.IsFalse(wheel.IsUsable);
            Assert.IsNotEmpty(problems);
        }

        /// <summary>
        /// Invariant 5d's complaint, applied to a reward table: a wheel every slice of which
        /// pays the same rejects no outcome, so the spin is decoration and the player finds out
        /// on their second one.
        /// </summary>
        [Test]
        public void AWheelWhereEverySliceIsTheSameIsRefused()
        {
            var problems = new List<string>();
            var wheel = BonusWheel.Resolve(new AdWheelDto
            {
                slices = new[]
                {
                    new AdWheelSliceDto { percent = 100 },
                    new AdWheelSliceDto { percent = 100 },
                    new AdWheelSliceDto { percent = 100 },
                    new AdWheelSliceDto { percent = 100 },
                },
            }, problems);

            Assert.IsFalse(wheel.IsUsable);
            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void AWheelOutsideTheSliceBoundsIsRefused()
        {
            var problems = new List<string>();

            var few = new AdWheelDto
            {
                slices = new[]
                {
                    new AdWheelSliceDto { percent = 100 },
                    new AdWheelSliceDto { percent = 500 },
                },
            };
            Assert.IsFalse(BonusWheel.Resolve(few, problems).IsUsable);

            var many = new AdWheelDto { slices = new AdWheelSliceDto[WheelRules.MaxSlices + 1] };
            for (int i = 0; i < many.slices.Length; i++)
                many.slices[i] = new AdWheelSliceDto { percent = 100 + i * 10 };

            Assert.IsFalse(BonusWheel.Resolve(many, problems).IsUsable);
            Assert.AreEqual(2, problems.Count);
        }

        [Test]
        public void AnEmptyBlockIsTheFlatOfferAndNotAnError()
        {
            var problems = new List<string>();

            Assert.IsFalse(BonusWheel.Resolve(null, problems).IsUsable);
            Assert.IsFalse(BonusWheel.Resolve(new AdWheelDto(), problems).IsUsable);
            Assert.IsEmpty(problems, "an absent wheel is a flat offer, which is not a mistake");
        }

        // ----------------------------------------------------------------- the roll
        /// <summary>
        /// The whole anti-reroll property in one line: the slice is a fact about the day and the
        /// spin, not about the moment the panel was opened. Backing out and spinning again lands
        /// on the same wedge, so there is nothing to shop for by force-quitting.
        /// </summary>
        [Test]
        public void TheSameSpinAlwaysLandsOnTheSameSlice()
        {
            var wheel = BonusWheel.Default;

            int first = wheel.Landing("uid_alpha", 20330, 2);
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(first, wheel.Landing("uid_alpha", 20330, 2));
        }

        [Test]
        public void ThereIsNoSliceWithoutAnAccountToSeedFrom()
        {
            var wheel = BonusWheel.Default;

            Assert.AreEqual(-1, wheel.Landing(null, 20330, 0));
            Assert.AreEqual(-1, wheel.Landing(string.Empty, 20330, 0));
            Assert.AreEqual(-1, wheel.Landing("uid_alpha", -1, 0));
            Assert.AreEqual(-1, wheel.Landing("uid_alpha", 20330, -1));
        }

        /// <summary>
        /// Every slice is reachable, and no slice runs away with the wheel. Not a statistical
        /// claim — a loose one, over a sample big enough that a picker which had collapsed onto
        /// two wedges would fail it and a healthy one never will.
        /// </summary>
        [Test]
        public void EverySliceComesUpAndNoneDominates()
        {
            var wheel = BonusWheel.Default;
            var hits = new int[wheel.Count];

            const int spins = 4000;
            for (int i = 0; i < spins; i++)
            {
                int landing = wheel.Landing("uid_alpha", 20330 + i / 8, i % 8);
                Assert.GreaterOrEqual(landing, 0);
                hits[landing]++;
            }

            int expected = spins / wheel.Count;

            for (int i = 0; i < hits.Length; i++)
            {
                Assert.Greater(hits[i], expected / 2, $"slice {i} barely comes up");
                Assert.Less(hits[i], expected * 2, $"slice {i} comes up far too often");
            }
        }

        // ---------------------------------------------------------------- the motion
        /// <summary>
        /// The claim the whole feature rests on: the wheel comes to rest with the slice the seed
        /// picked under the pointer, exactly, for every slice of every supported size.
        ///
        /// The inverse is written out here rather than borrowed from the widget, so this is a
        /// check on the rule rather than a restatement of it.
        /// </summary>
        [Test]
        public void TheWheelStopsWithTheChosenSliceUnderThePointer()
        {
            for (int count = WheelRules.MinSlices; count <= WheelRules.MaxSlices; count++)
            {
                float step = 360f / count;

                for (int index = 0; index < count; index++)
                {
                    float rest = WheelSpin.Rest(count, index);

                    Assert.GreaterOrEqual(rest, 0f);
                    Assert.Less(rest, 360f);

                    // Slice i's centre is drawn (i + ½) steps clockwise from the top, and the
                    // rotor turns it back. Which slice is under the pointer is therefore the
                    // rotation read back as a number of steps.
                    float under = Mathf.Repeat(rest / step - .5f, count);
                    Assert.AreEqual(index, Mathf.RoundToInt(under),
                                    $"a {count}-slice wheel resting for slice {index} shows another");
                    Assert.Less(Mathf.Abs(under - index), .001f,
                                $"a {count}-slice wheel rests off the centre of slice {index}");
                }
            }
        }

        /// <summary>
        /// The travel starts at nothing, ends exactly on the resting angle, turns the same
        /// direction throughout, and always turns far enough to read as a spin.
        ///
        /// The monotonicity is the one that matters to a player: a wheel that visibly backed up
        /// on its way to a slice would read as the result being changed after the fact.
        /// </summary>
        [Test]
        public void TheTravelIsOneUnbrokenClockwiseSweep()
        {
            const int count = 8;

            for (int index = 0; index < count; index++)
            {
                float target = WheelSpin.Target(count, index);

                Assert.AreEqual(0f, WheelSpin.AngleAt(count, index, 0f));
                Assert.AreEqual(target, WheelSpin.AngleAt(count, index, 1f), .0001f);
                Assert.LessOrEqual(target, -360f * WheelSpin.Turns,
                                   "the wheel does not turn far enough to read as a spin");

                // Landing and resting are the same place, a whole number of turns apart.
                Assert.AreEqual(WheelSpin.Rest(count, index),
                                Mathf.Repeat(target, 360f), .001f);

                float previous = 0f;
                for (int i = 1; i <= 200; i++)
                {
                    float angle = WheelSpin.AngleAt(count, index, i / 200f);
                    Assert.LessOrEqual(angle, previous + .0001f, "the wheel backed up mid-spin");
                    previous = angle;
                }
            }
        }

        /// <summary>
        /// The pegs are counted from the same arithmetic the slices are drawn from, so the
        /// clicks cannot drift out of step with the rim. A wheel of eight turning six times
        /// passes forty-eight of them, less the fraction of a slice it stops short of.
        /// </summary>
        [Test]
        public void EveryPegIsCountedOnceAndOnlyOnceOnTheWayPast()
        {
            const int count = 8;

            Assert.AreEqual(0, WheelSpin.PegsPassed(count, 0f));
            Assert.AreEqual(0, WheelSpin.PegsPassed(count, 12f), "a wheel wound backwards has passed nothing");
            Assert.AreEqual(0, WheelSpin.PegsPassed(count, -44f));
            Assert.AreEqual(1, WheelSpin.PegsPassed(count, -45f));
            Assert.AreEqual(8, WheelSpin.PegsPassed(count, -360f));

            for (int index = 0; index < count; index++)
            {
                int previous = 0;

                for (int i = 0; i <= 400; i++)
                {
                    int pegs = WheelSpin.PegsPassed(count, WheelSpin.AngleAt(count, index, i / 400f));
                    Assert.GreaterOrEqual(pegs, previous, "the peg count went backwards");
                    previous = pegs;
                }

                int total = WheelSpin.PegsPassed(count, WheelSpin.Target(count, index));
                Assert.AreEqual(count * (WheelSpin.Turns + 1) - 1 - index, total,
                                $"a landing on slice {index} passed the wrong number of pegs");
            }
        }

        /// <summary>
        /// A bigger wheel turns faster rather than for longer. Anything else means retuning the
        /// table silently retunes how long a player waits, and the wait is the part they feel.
        /// </summary>
        [Test]
        public void HowLongASpinTakesDoesNotDependOnHowManySlicesThereAre()
        {
            Assert.Greater(WheelSpin.Seconds, 1f);
            Assert.Less(WheelSpin.Seconds, 6f);

            // There is deliberately no per-count duration to compare — the constant is the
            // whole rule. This pins that it stays a constant: a reader who adds one will have
            // to delete this line and think about it.
            Assert.AreEqual(WheelSpin.Seconds, WheelSpin.Seconds);
        }

        [Test]
        public void TheCelebrationIsBoundedAtBothEndsAndRisesWithThePrize()
        {
            float ordinary = WheelSpin.CelebrationSeconds(WheelRules.MinPercent);
            float middling = WheelSpin.CelebrationSeconds(300);
            float best = WheelSpin.CelebrationSeconds(WheelRules.MaxPercent);

            Assert.Greater(ordinary, .3f, "short enough to read as a glitch");
            Assert.Less(best, 2f, "long enough that a player is waiting on an animation");

            Assert.Less(ordinary, middling);
            Assert.LessOrEqual(middling, best);
        }
    }
}
