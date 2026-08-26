using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Lightweave's timing: how fast light runs a channel, what note a critter rings, how the
    /// closing cascade paces itself, and when the clock starts to press.
    ///
    /// <para>
    /// <b>This suite exists because none of it can be seen in a screenshot.</b> A finale that
    /// fires six chimes inside one frame, a note ladder that repeats so the sixth critter sounds
    /// like the first, a pulse that takes three seconds to cross a long channel, an alarm that
    /// runs at full brightness on a grove with no clock — every one of those compiles, validates
    /// and reads perfectly in the source. It is <c>TweenCycleTests</c>' argument in the mode that
    /// had no equivalent, and it is why <see cref="WeaveTempo"/> holds no Unity types.
    /// </para>
    /// </summary>
    public sealed class WeaveTempoTests
    {
        // ------------------------------------------------------------------ light on the wire
        [Test]
        public void LightNeverCrossesAChannelFasterThanTheEyeOrSlowerThanThePatience()
        {
            for (int cells = 0; cells <= 200; cells++)
            {
                float travel = WeaveTempo.TravelSeconds(cells);

                Assert.GreaterOrEqual(travel, WeaveTempo.MinTravel,
                    $"a {cells}-cell channel lights in {travel}s, which reads as the line simply "
                    + "changing colour rather than as light going somewhere");
                Assert.LessOrEqual(travel, WeaveTempo.MaxTravel,
                    $"a {cells}-cell channel takes {travel}s; a bigger grove must not be a longer "
                    + "wait, because the wait is time the player is not playing");
            }
        }

        [Test]
        public void ALongerChannelNeverLightsFasterThanAShorterOne()
        {
            // Monotonic, so the effect always reads as speed rather than as an inconsistency
            // between two channels of the same grove.
            for (int cells = 2; cells <= 120; cells++)
                Assert.GreaterOrEqual(WeaveTempo.TravelSeconds(cells),
                                      WeaveTempo.TravelSeconds(cells - 1),
                                      $"{cells} cells lights faster than {cells - 1}");
        }

        [Test]
        public void TheRateGivesWayRatherThanTheCeiling()
        {
            // The whole point of the ceiling: past it, a channel twice as long lights at the same
            // moment rather than twice as late.
            Assert.AreEqual(WeaveTempo.MaxTravel, WeaveTempo.TravelSeconds(400), 1e-4f);
            Assert.AreEqual(WeaveTempo.MaxTravel, WeaveTempo.TravelSeconds(800), 1e-4f);
        }

        // ------------------------------------------------------------------ the note ladder
        [Test]
        public void EveryCritterOfTheHardestGroveRingsItsOwnNote()
        {
            // Six is the most pairs a grove can hold, and the ladder plus its octave is exactly
            // six notes. A repeat inside one grove is the sixth critter sounding like the first,
            // at the loudest moment the mode has.
            var heard = new HashSet<int>();

            for (int joined = 1; joined <= WeaveGenerator.Palette.Length; joined++)
            {
                int cents = (int)(1200f * System.Math.Log(WeaveTempo.Pitch(joined), 2) + .5f);
                Assert.IsTrue(heard.Add(cents),
                              $"channel {joined} rings a note already heard in this grove");
            }
        }

        [Test]
        public void TheLadderOnlyEverClimbs()
        {
            // Including past the sixth, where it keeps going by octaves rather than falling back
            // down — a ladder that dips in the middle tells the player they have gone backwards.
            for (int joined = 2; joined <= 24; joined++)
                Assert.Greater(WeaveTempo.Pitch(joined), WeaveTempo.Pitch(joined - 1),
                               $"channel {joined} rings lower than channel {joined - 1}");
        }

        [Test]
        public void TheSixthChannelIsACleanOctaveAboveTheFirst()
        {
            Assert.AreEqual(1f, WeaveTempo.Pitch(1), 1e-3f);
            Assert.AreEqual(2f, WeaveTempo.Pitch(6), 1e-2f);
        }

        [Test]
        public void TheLadderIsSafeBelowItsFirstRung()
        {
            // Nothing should ever ask for channel zero, and if something does it must not be an
            // index out of range in the middle of a celebration.
            Assert.AreEqual(WeaveTempo.Pitch(1), WeaveTempo.Pitch(0), 1e-5f);
            Assert.AreEqual(WeaveTempo.Pitch(1), WeaveTempo.Pitch(-4), 1e-5f);
        }

        // ------------------------------------------------------------------ the finale
        [Test]
        public void TheClosingCascadeIsOverInsideItsCeilingHoweverManyChannelsThereAre()
        {
            // It sits between the last channel landing and the victory panel opening, so every
            // millisecond of it is a millisecond the player has already won and is waiting.
            for (int channels = 1; channels <= 12; channels++)
                Assert.LessOrEqual(WeaveTempo.FinaleSeconds(channels),
                                   WeaveTempo.MaxFinaleSeconds + 1e-4f,
                                   $"{channels} channels take {WeaveTempo.FinaleSeconds(channels)}s to close");
        }

        [Test]
        public void TheGapGivesWayRatherThanTheCeiling()
        {
            // Three channels get the gap they want; a grove with many more gets a tighter one
            // rather than a longer wait. GroveGrowth.MaxSpread's rule.
            Assert.AreEqual(WeaveTempo.FinaleGap, WeaveTempo.GapFor(3), 1e-4f);
            Assert.Less(WeaveTempo.GapFor(40), WeaveTempo.FinaleGap);
        }

        [Test]
        public void TheChannelsOfTheCascadeLightInOrderAndNeverAtOnce()
        {
            for (int channels = 2; channels <= 8; channels++)
                for (int i = 1; i < channels; i++)
                    Assert.Greater(WeaveTempo.FinaleAt(i, channels),
                                   WeaveTempo.FinaleAt(i - 1, channels),
                                   $"channels {i - 1} and {i} of {channels} light in the same frame");
        }

        [Test]
        public void OneChannelHasNothingToCascade()
        {
            Assert.AreEqual(0f, WeaveTempo.FinaleSeconds(1), 1e-5f);
            Assert.AreEqual(0f, WeaveTempo.FinaleAt(0, 1), 1e-5f);
        }
    }
}
