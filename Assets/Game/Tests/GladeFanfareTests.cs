using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// How long a solved glade celebrates for, which is the one subsystem whose failures only
    /// show up in play — so the arithmetic lives in Domain and is proved without an Editor.
    ///
    /// <para>
    /// The property every one of these defends is <c>BudMotionTests</c>' property: <b>the rate
    /// gives way, but never past the point where the eye can follow it.</b> A glade is the mode
    /// where that bites hardest, because the celebration walks the network the player just
    /// finished — so its length is a fact about the <em>board</em>, and nothing but a bound
    /// stops a deep grove turning the payoff into a wait.
    /// </para>
    /// </summary>
    public sealed class GladeFanfareTests
    {
        /// <summary>The deepest network in the forty shipped glades, measured off the content.</summary>
        const int Shipped = 15;

        // ------------------------------------------------------------------ the surge
        [Test]
        public void EveryGroveAShippedChapterHoldsSurgesInsideTheCeiling()
        {
            for (int rings = 1; rings <= Shipped; rings++)
                Assert.LessOrEqual(GladeFanfare.Surge(rings), GladeFanfare.SurgeCeiling + .0001f,
                                   $"a {rings}-ring grove takes " +
                                   $"{GladeFanfare.Surge(rings):0.00}s to light");
        }

        /// <summary>
        /// And past that the floor governs, which is the design rather than a leak.
        ///
        /// Two bounds pull opposite ways — the light may not take for ever, and a ring may not
        /// be too fast to read — and where they meet the floor wins, because a payoff nobody can
        /// follow pays out nothing.
        /// </summary>
        [Test]
        public void AndAnAbsurdlyDeepOneIsBoundedByTheFloorInstead()
        {
            for (int rings = 1; rings <= 80; rings++)
            {
                float most = GladeFanfare.SurgeCeiling > GladeFanfare.MinRing * rings
                           ? GladeFanfare.SurgeCeiling : GladeFanfare.MinRing * rings;

                Assert.LessOrEqual(GladeFanfare.Surge(rings), most + .0001f,
                                   $"a {rings}-ring grove runs past both bounds at " +
                                   $"{GladeFanfare.Surge(rings):0.00}s");
            }
        }

        [Test]
        public void AndNoRingIsEverTooFastToFollow()
        {
            for (int rings = 1; rings <= 80; rings++)
                Assert.GreaterOrEqual(GladeFanfare.Ring(rings), GladeFanfare.MinRing - .0001f);
        }

        [Test]
        public void AShallowGroveGetsTheFullRingRatherThanARushedOne()
        {
            // Four rings against a 1.35s ceiling has room to spare, so nothing should be
            // compressed: a small board is the one case where the light can afford to stroll.
            Assert.AreEqual(GladeFanfare.RingFull, GladeFanfare.Ring(4), .0001f);
        }

        [Test]
        public void RingsLightInOrderAndTheFirstIsTheCrystalItself()
        {
            Assert.AreEqual(0f, GladeFanfare.RingAt(0, 10), .0001f);

            for (int d = 1; d < 10; d++)
                Assert.Greater(GladeFanfare.RingAt(d, 10), GladeFanfare.RingAt(d - 1, 10),
                               $"ring {d} does not land after ring {d - 1}");
        }

        // ------------------------------------------------------------------ inside one ring
        /// <summary>
        /// A wide ring ripples, and the ripple never eats the beat it lives in.
        ///
        /// Otherwise a ring twelve tiles across would still be lighting when the next one
        /// started, and the light would stop reading as travelling outward at all.
        /// </summary>
        [Test]
        public void ARingsRippleNeverEatsItsOwnBeat()
        {
            for (int rings = 1; rings <= 40; rings++)
            {
                float ring = GladeFanfare.Ring(rings);
                for (int wide = 2; wide <= 24; wide++)
                    for (int nth = 0; nth < wide; nth++)
                        Assert.LessOrEqual(GladeFanfare.StaggerAt(nth, wide, ring), ring * .5f + .0001f,
                                           $"the {nth} of {wide} tiles in a {ring:0.000}s ring " +
                                           "lands past half the beat");
            }
        }

        [Test]
        public void ALoneTileInARingWaitsForNothing()
        {
            Assert.AreEqual(0f, GladeFanfare.StaggerAt(0, 1, .12f), .0001f);
            Assert.AreEqual(0f, GladeFanfare.StaggerAt(0, 8, .12f), .0001f);
        }

        // ------------------------------------------------------------------ the notes
        /// <summary>
        /// The surge never sounds more notes than <c>Audio.PlayOne</c> has voices to spare.
        ///
        /// A deep grove walks twenty-odd rings inside a second and a third; sounding all of
        /// them is not a crescendo, it cuts itself off, because the pool is ten deep.
        /// </summary>
        [Test]
        public void ADeepGroveNeverSoundsMoreNotesThanTheCeiling()
        {
            for (int rings = 1; rings <= GladeFanfare.DeepestGrove; rings++)
            {
                int stride = GladeFanfare.NoteStride(rings);
                Assert.GreaterOrEqual(stride, 1);

                int notes = 0;
                for (int d = 0; d < rings; d++) if (d % stride == 0) notes++;

                Assert.LessOrEqual(notes, GladeFanfare.MostNotes,
                                   $"a {rings}-ring grove sounds {notes} notes");
            }
        }

        [Test]
        public void AndAShallowOneSoundsEveryRing()
        {
            for (int rings = 1; rings <= GladeFanfare.MostNotes; rings++)
                Assert.AreEqual(1, GladeFanfare.NoteStride(rings));
        }

        [Test]
        public void ThePitchClimbsWithTheLightAndStopsWhereItIsToldTo()
        {
            Assert.AreEqual(GladeFanfare.Lowest, GladeFanfare.Pitch(0, 12), .0001f);
            Assert.AreEqual(GladeFanfare.Highest, GladeFanfare.Pitch(11, 12), .0001f);

            for (int d = 1; d < 12; d++)
                Assert.Greater(GladeFanfare.Pitch(d, 12), GladeFanfare.Pitch(d - 1, 12));

            // A grove one ring deep has nowhere to climb to, and must not divide by nothing.
            Assert.AreEqual(GladeFanfare.Lowest, GladeFanfare.Pitch(0, 1), .0001f);
        }

        // ------------------------------------------------------------------ a critter waking
        /// <summary>
        /// A leap falls for longer than it rises.
        ///
        /// A landing quicker than the jump reads as a snap back to the ground rather than as
        /// weight, which is the one thing the whole gesture is for.
        /// </summary>
        [Test]
        public void ACritterFallsForLongerThanItRises()
        {
            Assert.Greater(GladeFanfare.Land, GladeFanfare.Leap);
            Assert.AreEqual(GladeFanfare.Pump, GladeFanfare.Leap + GladeFanfare.Land, .0001f);
        }

        // ------------------------------------------------------------------ the whole thing
        /// <summary>
        /// <b>The bound this file exists for.</b> Every beat here is tuned on its own merits and
        /// the sequence is the sum of them, so the way this goes wrong is one beat lengthened
        /// for a good reason walking the whole celebration past the point where the player is
        /// waiting rather than watching.
        /// </summary>
        [Test]
        public void NoGroveCelebratesForLongerThanACelebrationCanLast()
        {
            for (int rings = 1; rings <= GladeFanfare.DeepestGrove; rings++)
                Assert.LessOrEqual(GladeFanfare.Total(rings), GladeFanfare.Longest + .0001f,
                                   $"a {rings}-ring grove celebrates for " +
                                   $"{GladeFanfare.Total(rings):0.00}s");
        }

        /// <summary>
        /// And it is long enough to be one. The failure in the other direction is silent — a
        /// sequence that has quietly compressed to a second still plays, still sounds and still
        /// reaches the panel, and only reads as the game skipping the payoff.
        /// </summary>
        [Test]
        public void AndEveryGroveGetsLongEnoughToBeWatched()
        {
            for (int rings = 1; rings <= GladeFanfare.DeepestGrove; rings++)
                Assert.GreaterOrEqual(GladeFanfare.Total(rings), 2.2f,
                                      $"a {rings}-ring grove is over in " +
                                      $"{GladeFanfare.Total(rings):0.00}s");
        }

        [Test]
        public void ADeeperGroveIsNeverCelebratedForLessTimeThanAShallowerOne()
        {
            for (int rings = 2; rings <= GladeFanfare.DeepestGrove; rings++)
                Assert.GreaterOrEqual(GladeFanfare.Total(rings), GladeFanfare.Total(rings - 1) - .0001f,
                                      $"{rings} rings celebrates for less than {rings - 1}");
        }

        /// <summary>
        /// The beats arrive in the order the sequence describes, which is the one thing a set of
        /// separately-tuned constants can lose without anything failing to compile.
        /// </summary>
        [Test]
        public void TheBeatsLandInTheOrderTheyAreWrittenIn()
        {
            for (int rings = 1; rings <= GladeFanfare.DeepestGrove; rings++)
            {
                Assert.Greater(GladeFanfare.BloomAt(rings), GladeFanfare.Hush,
                               "the bloom lands during the hush");
                Assert.Greater(GladeFanfare.BloomAt(rings),
                               GladeFanfare.Hush + GladeFanfare.Surge(rings),
                               "the bloom lands before the light has finished travelling");
                Assert.Greater(GladeFanfare.Total(rings), GladeFanfare.BloomAt(rings),
                               "the panel is raised before the bloom");
            }
        }

        /// <summary>
        /// The two shockwave rings both cross the grove and fade before the panel covers it.
        ///
        /// They leave the middle during the bloom, so what has to hold is that the second one's
        /// whole life fits inside what is left of the sequence — a ring still expanding under a
        /// scrim is a ring nobody sees the end of.
        /// </summary>
        [Test]
        public void BothShockwavesFinishBeforeThePanelCoversTheBoard()
        {
            float last = GladeFanfare.WaveGap + GladeFanfare.WaveCross;
            Assert.LessOrEqual(last, GladeFanfare.Bloom + GladeFanfare.Settle + .0001f,
                               $"the second shockwave is still crossing {last:0.00}s in");
        }
    }
}
