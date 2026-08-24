using System;
using System.Collections.Generic;
using GlimmerGrove.Homestead;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The order newly bought ground arrives in.
    ///
    /// <para>
    /// This is animation arithmetic, which is the one class of rule in this project whose
    /// failures are invisible in a screenshot and obvious only in motion — <c>TweenCycle</c>'s
    /// lesson, and the reason <see cref="GroveGrowth"/> holds no Unity types. A wave that runs
    /// inward, a region that takes six seconds, or thirty-six thuds fired inside one all
    /// compile, validate and look perfectly reasonable in the source.
    /// </para>
    /// <para>
    /// Two properties carry the whole feature and neither is obvious from reading the code.
    /// The ground has to grow <em>out of</em> the grove the player already had, which is a
    /// statement about the seed rather than about the walk; and the ceremony has to take about
    /// the same time however much land was bought, which is a statement about the gap giving
    /// way rather than the total.
    /// </para>
    /// </summary>
    public sealed class GroveGrowthTests
    {
        static GroveRegion Region(int col, int row, int cols, int rows)
            => new GroveRegion("r", col, row, cols, rows, 1000);

        /// <summary>Everything in this box was owned before the purchase.</summary>
        static Func<int, int, bool> Owns(int col, int row, int cols, int rows)
            => (c, r) => c >= col && c < col + cols && r >= row && r < row + rows;

        static Func<int, int, bool> Nothing => (c, r) => false;

        static int RingAt(GroveRegion region, int[] rings, int col, int row)
            => rings[(row - region.Row) * region.Cols + (col - region.Col)];

        // -------------------------------------------------------------- the walk
        [Test]
        public void EveryTileOfTheRegionIsReached()
        {
            var region = Region(4, 4, 6, 5);
            var rings = GroveGrowth.Rings(region, Owns(0, 4, 4, 5), 2, 6);

            Assert.AreEqual(region.TileCount, rings.Length);

            foreach (int ring in rings)
                Assert.GreaterOrEqual(ring, 0, "a tile with no place in the wave would never be drawn");
        }

        [Test]
        public void TheFirstRingIsExactlyTheEdgeAgainstLandAlreadyHeld()
        {
            // Owned: the four columns to the left. The region is bought to the right of it, so
            // its own left column is the only part touching what the player had.
            var region = Region(4, 0, 6, 4);
            var rings = GroveGrowth.Rings(region, Owns(0, 0, 4, 4), 2, 2);

            for (int r = 0; r < region.Rows; r++)
                for (int c = 0; c < region.Cols; c++)
                {
                    int ring = rings[r * region.Cols + c];

                    if (c == 0) Assert.AreEqual(0, ring, "the touching column starts the wave");
                    else Assert.AreEqual(c, ring, "and it spreads one column at a time");
                }
        }

        [Test]
        public void TheWaveSpreadsOutwardsRatherThanInReadingOrder()
        {
            var region = Region(4, 4, 6, 6);
            var rings = GroveGrowth.Rings(region, Owns(0, 4, 4, 6), 2, 6);

            // Every tile above ring zero has a neighbour one step nearer the old grove. That is
            // what makes it a wave rather than a set of tiles that happen to be numbered.
            for (int r = 0; r < region.Rows; r++)
                for (int c = 0; c < region.Cols; c++)
                {
                    int ring = rings[r * region.Cols + c];
                    if (ring == 0) continue;

                    Assert.IsTrue(Lower(region, rings, c, r, ring),
                                  $"ring {ring} at {c},{r} is stranded from the ring before it");
                }
        }

        static bool Lower(GroveRegion region, int[] rings, int c, int r, int ring)
        {
            return At(region, rings, c - 1, r) == ring - 1
                || At(region, rings, c + 1, r) == ring - 1
                || At(region, rings, c, r - 1) == ring - 1
                || At(region, rings, c, r + 1) == ring - 1;
        }

        static int At(GroveRegion region, int[] rings, int c, int r)
            => c < 0 || r < 0 || c >= region.Cols || r >= region.Rows ? -1 : rings[r * region.Cols + c];

        [Test]
        public void GroundTheRegionItselfHoldsIsNotMistakenForLandAlreadyOwned()
        {
            // The purchase is recorded before anything animates, so the obvious predicate — "is
            // this owned" — answers true for the new region as well. Read literally that seeds
            // every tile at ring zero and the whole lot lands in one frame, which is the
            // ceremony not happening rather than a ceremony that looks wrong.
            var region = Region(4, 0, 6, 4);
            var rings = GroveGrowth.Rings(region, (c, r) => c < 10 && r < 4, 2, 2);

            Assert.AreEqual(6, GroveGrowth.RingCount(rings),
                            "the region's own tiles must not seed the wave");
        }

        // ------------------------------------------------------------- the corner
        [Test]
        public void GroundThatTouchesNothingStartsAtTheCornerNearestTheHall()
        {
            // The shipped floor really is like this: dusk_field at (0,0) meets the starter
            // hearthstead at (4,4) only diagonally, so a player who owns nothing else buys land
            // with no edge against anything they hold.
            var region = Region(0, 0, 4, 4);
            var rings = GroveGrowth.Rings(region, Owns(4, 4, 6, 6), 6, 6);

            int seeds = 0;
            foreach (int ring in rings) if (ring == 0) seeds++;

            Assert.AreEqual(1, seeds, "one tile starts it");
            Assert.AreEqual(0, RingAt(region, rings, 3, 3), "and it is the corner facing the hall");
        }

        [Test]
        public void ARegionOwningTheWholeFloorAloneStillGrowsFromSomewhere()
        {
            var region = Region(0, 0, 4, 4);
            var rings = GroveGrowth.Rings(region, Nothing, 0, 0);

            Assert.AreEqual(0, RingAt(region, rings, 0, 0));
            Assert.AreEqual(7, GroveGrowth.RingCount(rings));
        }

        [Test]
        public void ARegionWithNoTilesIsNoCeremonyRatherThanACrash()
        {
            Assert.AreEqual(0, GroveGrowth.Rings(null, Nothing, 0, 0).Length);
            Assert.AreEqual(0, GroveGrowth.Rings(new GroveRegion("r", 0, 0, 0, 0, 0), Nothing, 0, 0).Length);
            Assert.AreEqual(0, GroveGrowth.RingCount(Array.Empty<int>()));
            Assert.AreEqual(0f, GroveGrowth.Spread(0));
        }

        // ---------------------------------------------------------------- timing
        [Test]
        public void TheWholeWaveFitsInsideItsCeilingHoweverDeepItIs()
        {
            // Two of the shipped shapes and one nobody has authored yet. A drop that sells a
            // bigger stretch of ground must not silently ship a longer interruption.
            foreach (int rings in new[] { 1, 2, 4, 7, 9, 24, 60 })
            {
                float spread = GroveGrowth.Spread(rings);

                Assert.LessOrEqual(spread, GroveGrowth.MaxSpread + .0001f,
                                   $"{rings} rings runs long");
                Assert.GreaterOrEqual(spread, GroveGrowth.RiseSeconds - .0001f,
                                      "and never shorter than one tile's own travel");
            }
        }

        [Test]
        public void AShallowWaveKeepsTheAuthoredRhythmRatherThanBeingStretchedToFill()
        {
            // The ceiling is a ceiling. A four-ring wave has room to spare and must not be
            // slowed down to use it, or every small region would feel laboured.
            Assert.AreEqual(GroveGrowth.RingGap, GroveGrowth.GapFor(4), .0001f);
            Assert.Less(GroveGrowth.GapFor(24), GroveGrowth.RingGap);
        }

        [Test]
        public void GroundArrivesInOrderAndNeverBeforeTheRingBeforeIt()
        {
            const int Rings = 9;
            float last = -1f;

            for (int ring = 0; ring < Rings; ring++)
            {
                float delay = GroveGrowth.DelayOf(ring, Rings);
                Assert.Greater(delay, last, "a later ring that lands first is a wave running backwards");
                last = delay;
            }

            Assert.AreEqual(0f, GroveGrowth.DelayOf(0, Rings), "and the first ring waits for nothing");
        }

        // ---------------------------------------------------------------- sound
        [Test]
        public void TheNumberOfSoundsIsBoundedHoweverManyTilesWereBought()
        {
            foreach (int rings in new[] { 1, 4, 7, 9, 24, 60 })
            {
                int voices = 0;
                for (int ring = 0; ring < rings; ring++)
                    if (GroveGrowth.Speaks(ring, rings)) voices++;

                Assert.LessOrEqual(voices, GroveGrowth.MaxVoices, $"{rings} rings is a machine gun");
                Assert.GreaterOrEqual(voices, 1, "and silence is not the alternative");
                Assert.IsTrue(GroveGrowth.Speaks(0, rings), "the first ring always lands audibly");
            }
        }

        [Test]
        public void ASmallRegionSpeaksOnEveryRing()
        {
            for (int ring = 0; ring < 4; ring++)
                Assert.IsTrue(GroveGrowth.Speaks(ring, 4));
        }

        [Test]
        public void RingsOutsideTheWaveSayNothing()
        {
            Assert.IsFalse(GroveGrowth.Speaks(-1, 6));
            Assert.IsFalse(GroveGrowth.Speaks(6, 6));
            Assert.IsFalse(GroveGrowth.Speaks(0, 0));
        }

        [Test]
        public void ThePitchClimbsAcrossTheWaveAndStaysInBand()
        {
            const int Rings = 9;
            float last = -1f;

            for (int ring = 0; ring < Rings; ring++)
            {
                float pitch = GroveGrowth.Pitch(ring, Rings);

                Assert.Greater(pitch, last, "a wave that repeats one note is a wave nobody hears travel");
                Assert.GreaterOrEqual(pitch, GroveGrowth.LowPitch - .0001f);
                Assert.LessOrEqual(pitch, GroveGrowth.HighPitch + .0001f);

                last = pitch;
            }

            Assert.AreEqual(GroveGrowth.LowPitch, GroveGrowth.Pitch(0, Rings), .0001f);
            Assert.AreEqual(GroveGrowth.HighPitch, GroveGrowth.Pitch(Rings - 1, Rings), .0001f);
            Assert.AreEqual(GroveGrowth.LowPitch, GroveGrowth.Pitch(0, 1), .0001f);
        }

        // ------------------------------------------------------------ the shipped floor
        [Test]
        public void EveryRegionOfTheShippedFloorMakesASensibleWaveFromTheStarterLand()
        {
            // The shape of the shipped 14x14, bought one region at a time by somebody who owns
            // only the starter. Nine walks, and what is asserted is the property the ceremony
            // depends on rather than any particular number: it starts in one place, it reaches
            // everything, and it is over inside the ceiling.
            var floor = new[]
            {
                Region(0, 0, 4, 4), Region(4, 0, 6, 4), Region(10, 0, 4, 4),
                Region(0, 4, 4, 6), Region(10, 4, 4, 6),
                Region(0, 10, 4, 4), Region(4, 10, 6, 4), Region(10, 10, 4, 4),
            };

            var starter = Owns(4, 4, 6, 6);

            foreach (var region in floor)
            {
                var rings = GroveGrowth.Rings(region, starter, 6, 6);
                var seen = new HashSet<int>();

                foreach (int ring in rings)
                {
                    Assert.GreaterOrEqual(ring, 0, region.Id);
                    seen.Add(ring);
                }

                int count = GroveGrowth.RingCount(rings);

                Assert.AreEqual(count, seen.Count, "a wave with a gap in it stutters");
                Assert.LessOrEqual(GroveGrowth.Spread(count), GroveGrowth.MaxSpread + .0001f);
            }
        }
    }
}
