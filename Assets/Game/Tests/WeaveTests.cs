using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Lightweave: join every crystal to the critter wanting its colour, cross nothing, and
    /// thread every bead on the way.
    ///
    /// <para>
    /// <b>The case this suite exists for is <see cref="EveryGeneratedGroveCanActuallyBeSolved"/>.</b>
    /// The whole design rests on the generator carving a solution and handing over its ends, so
    /// that solvability is a property of how a board was built. If that ever stopped being true
    /// the game would ship groves nobody can finish, and a player has no way to tell an
    /// impossible board from a hard one — they would simply lose hearts until they stopped
    /// playing. So it is checked by <em>playing</em> the generator's own arrangement through the
    /// same rules the player is held to, over hundreds of seeds.
    /// </para>
    /// <para>
    /// <b>What is deliberately not tested here any more.</b> This mode used to be won only by
    /// covering every cell of the grove, and a good half of the old suite pinned that rule and
    /// the per-pair detour bar that went with it. Both are gone: a channel now runs wherever the
    /// player likes, and what makes a board hard is that the pairs cannot <em>all</em> take their
    /// shortest route at once. So the arithmetic those tests protected has been replaced rather
    /// than deleted — see <see cref="WeaveSolver.Tally.Slack"/>, which is the one number the
    /// whole ladder is now authored against.
    /// </para>
    /// </summary>
    public sealed class WeaveTests
    {
        static WeaveLayout Grove(uint seed = 7, int w = 7, int h = 9, int pairs = 4, int beads = 0)
            => WeaveGenerator.Build(w, h, pairs, seed, beads);

        /// <summary>
        /// A grove built by hand, so the floor arithmetic can be checked against a board small
        /// enough to reason about on paper.
        /// </summary>
        static WeaveLayout ByHand(int width, int height,
                                  (int heart, int critter, int[] route, int[] beads)[] pairs)
        {
            var made = new WeavePair[pairs.Length];
            var routes = new int[pairs.Length][];
            var beads = new List<WeaveBead>();

            for (int p = 0; p < pairs.Length; p++)
            {
                made[p] = new WeavePair(pairs[p].heart, pairs[p].critter,
                                        WeaveGenerator.Palette[p]);
                routes[p] = pairs[p].route;

                if (pairs[p].beads == null) continue;
                foreach (int cell in pairs[p].beads) beads.Add(new WeaveBead(cell, p));
            }

            return new WeaveLayout(width, height, made, routes, beads.ToArray());
        }

        static (int, int, int[], int[]) Pair(int heart, int critter, int[] route, int[] beads = null)
            => (heart, critter, route, beads);

        // ------------------------------------------------------------------ the floor
        /// <summary>
        /// <see cref="WeaveLayout.Straight"/> with no bead is the straight line: one more cell
        /// than the distance between the ends.
        /// </summary>
        [Test]
        public void APairWithNoBeadHasTheStraightLineForItsFloor()
        {
            //  . . .   The pair sits in opposite corners of a 3x3: two across and two down, so
            //  . . .   the shortest route that could join them is five cells. The route carved
            //  . . .   here is the whole grove, which does not change the floor.
            var grove = ByHand(3, 3, new[] { Pair(0, 8, new[] { 0, 1, 2, 5, 4, 3, 6, 7, 8 }) });

            Assert.AreEqual(5, grove.Straight(0));
            Assert.AreEqual(9, grove.Solution(0).Count);
        }

        [Test]
        public void ABeadOffTheDirectCorridorLiftsItsPairsFloor()
        {
            //  A . a   0 -> 2 is three cells the direct way. The bead sits at the middle of the
            //  . o .   grove, which is off every shortest route between them, so the floor rises
            //  . . .   to five: down, across, and back up.
            var bare = ByHand(3, 3, new[] { Pair(0, 2, new[] { 0, 1, 2 }) });
            Assert.AreEqual(3, bare.Straight(0));

            var beaded = ByHand(3, 3, new[] { Pair(0, 2, new[] { 0, 3, 4, 5, 2 }, new[] { 4 }) });
            Assert.AreEqual(5, beaded.Straight(0),
                            "a bead the direct route misses has to lift the floor past it");
            Assert.AreEqual(5, beaded.Solution(0).Count, "and the carved route pays exactly that");
        }

        [Test]
        public void ABeadOnTheDirectCorridorLiftsNothing()
        {
            // Which is precisely why the generator refuses to place one there — a bead a player
            // threads without going out of their way is decoration. See WeaveGenerator.Thread.
            var grove = ByHand(3, 3, new[] { Pair(0, 2, new[] { 0, 1, 2 }, new[] { 1 }) });

            Assert.AreEqual(3, grove.Straight(0));
        }

        [Test]
        public void TheFloorThroughSeveralBeadsIsTheShortestTourAndNotTheAuthoredOrder()
        {
            //  A . o   Two beads, on the corners the pair does not occupy. Whichever is threaded
            //  . . .   first, the tour is the same length — and it is the whole 3x3, so this is
            //  o . a   also a check that the subset arithmetic does not double-count a leg.
            var grove = ByHand(3, 3,
                new[] { Pair(0, 8, new[] { 0, 1, 2, 5, 4, 3, 6, 7, 8 }, new[] { 2, 6 }) });

            Assert.AreEqual(9, grove.Straight(0),
                            "0->2->6->8 and 0->6->2->8 are both eight steps, so nine cells");
            Assert.AreEqual(0, (grove.Straight(0) - 5) % 2,
                            "a bead tour must keep the parity of the straight line between the ends");
        }

        [Test]
        public void ParIsTheGrovesFloorPlusOneForEveryDecisionOnIt()
        {
            // Par drives the clock and the star lines, so what it counts is worth pinning
            // exactly rather than trusting to read correctly.
            var grove = ByHand(3, 3, new[] { Pair(0, 2, new[] { 0, 3, 4, 5, 2 }, new[] { 4 }) });

            Assert.AreEqual(5, grove.StraightTotal);
            Assert.AreEqual(5 + 1 + 1, grove.Par, "one pair and one bead, a cell of looking each");
        }

        // ------------------------------------------------------------------ how much is forced
        [Test]
        public void AGroveWhereEveryPairCanGoStraightAtOnceIsForcedNothingAtAll()
        {
            //  A B     Two pairs running straight down their own columns. Both take their floor
            //  . .     at the same time without meeting, so neither asks the player anything —
            //  a b     and this is exactly the shape the chapter used to be full of.
            var grove = ByHand(2, 3, new[]
            {
                Pair(0, 4, new[] { 0, 2, 4 }),
                Pair(1, 5, new[] { 1, 3, 5 }),
            });

            var tally = WeaveSolver.Measure(grove);

            Assert.IsTrue(tally.Solved);
            Assert.AreEqual(0, tally.Slack, "two straight drags force nothing");
            Assert.IsTrue(tally.Taut);

            Assert.IsTrue(WeaveSolver.AnyTautSolution(grove, out bool decided));
            Assert.IsTrue(decided, "a six-cell grove is decided well inside the budget");
        }

        [Test]
        public void ABoardNobodyCanSolveIsReportedImpossibleRatherThanMerelyNotFound()
        {
            //  A B     Both pairs join opposite corners of a 3x3, so their routes have to cross
            //  . . .   and no arrangement exists. The distinction matters: a capped search that
            //  b a     found nothing proves nothing, and a validator wired to that would fail a
            //          build over a board that is fine.
            var grove = ByHand(3, 3, new[]
            {
                Pair(0, 8, new[] { 0, 1, 2, 5, 8 }),
                Pair(2, 6, new[] { 2, 3, 4, 7, 6 }),
            });

            var tally = WeaveSolver.Measure(grove);

            Assert.IsFalse(tally.Solved);
            Assert.IsTrue(tally.Exhausted, "a nine-cell grove is searched to the end");
            Assert.IsTrue(tally.Impossible);
        }

        [Test]
        public void SlackIsAlwaysEvenSoTwoIsTheSmallestBarThereIs()
        {
            // Every step of a route changes the distance to the far end by exactly one, so a
            // route and the floor it is measured against always share a parity and their
            // difference cannot be odd. That is why MinSlack reads as "not everybody at once"
            // rather than as a number somebody picked: a bar of 1 or 3 would be silently
            // identical to 2 or 4.
            Assert.AreEqual(0, WeaveGenerator.MinSlack % 2,
                            "an odd slack bar means the same thing as the even one below it");
            Assert.AreEqual(0, WeaveSolver.Latitude % 2,
                            "an odd latitude counts exactly what the even one below it counts");

            for (uint seed = 1; seed <= 40; seed++)
            {
                var grove = Grove(seed, 6, 7, 4, 1);
                var tally = WeaveSolver.Measure(grove, 40);
                if (!tally.Solved) continue;

                Assert.AreEqual(0, tally.Slack % 2,
                    $"seed {seed} needs an odd number of cells of detour, which no route on a "
                    + "grid can require");
            }
        }

        [Test]
        public void TheGeneratorNoLongerDealsAGroveEveryPairCanWalkStraightAcross()
        {
            // The acceptance bar, over the shapes the chapter actually uses. Before there was
            // anything measuring this, every one of the ten shipped groves let some crystal
            // reach its critter by a straight drag and the mode came back from play as "each
            // critter is literally next to their matching light".
            var shapes = new[] { (5, 6, 3, 0), (6, 7, 4, 1), (6, 8, 5, 2), (7, 9, 6, 4) };

            foreach (var (w, h, pairs, beads) in shapes)
            {
                int met = 0;
                for (uint seed = 1; seed <= 30; seed++)
                {
                    var grove = WeaveGenerator.Build(w, h, pairs, seed, beads);
                    bool taut = WeaveSolver.AnyTautSolution(grove, out bool decided);
                    if (grove.IsComplete && decided && !taut) met++;
                }

                // Not every seed, because Build settles for the best board it saw rather than
                // failing — a level that is a little easy beats one that cannot be finished.
                // The bar has to be met by the great majority or authoring a rung is guesswork.
                Assert.Greater(met, 22,
                    $"only {met} of 30 seeds at {w}x{h}/{pairs} with {beads} bead(s) were dealt a "
                    + "grove that contests anything, so the generator can no longer find one");
            }
        }

        // ------------------------------------------------------------------ the generator
        [Test]
        public void EveryGeneratedGroveCanActuallyBeSolved()
        {
            // Two hundred seeds, played rather than trusted, and played all the way to IsSolved
            // so the beads are proved too. A generator that quietly produces an unfinishable
            // board is the one failure this mode cannot survive.
            for (uint seed = 1; seed <= 200; seed++)
            {
                var layout = Grove(seed, 7, 9, 4, 3);
                var run = new WeaveRun(layout);

                Assert.IsTrue(run.DrawSolution(),
                              $"seed {seed} produced a grove its own solution cannot solve");
                Assert.IsTrue(run.IsSolved, $"seed {seed} did not finish");
            }
        }

        [Test]
        public void EveryGeneratedGrovesCarveReachesAllOfItsGround()
        {
            // No longer the win condition and still the generator's bar: a carve that reaches
            // every cell is what pushes the endpoints out to the edges and interleaves the
            // routes between them. A carve that leaves islands behind deals a board where every
            // pair has its own quiet corner and never meets anybody.
            //
            // Asserted over every seed rather than as a proportion, because the way this fails
            // is not one unlucky board — it is the generator quietly falling back to something
            // slack for most of them, which is exactly what a budget-less walk did here.
            for (uint seed = 1; seed <= 150; seed++)
            {
                var layout = Grove(seed, 7, 9, 4, 2);

                Assert.IsTrue(layout.IsComplete,
                              $"seed {seed} left {layout.Count - layout.SolutionLength} of its "
                              + $"{layout.Count} cells untouched");
                Assert.GreaterOrEqual(layout.Coverage, WeaveGenerator.MinCoverage);
            }
        }

        [Test]
        public void NoPairIsCloseEnoughToItsCritterToBeJoinedByAReflex()
        {
            // The placement bar, and it is the whole of what "place the critters cleverly" means
            // here. A crystal three cells from its critter is joined by a flick rather than a
            // decision, and a board with two of those on it hands away a third of itself before
            // the thinking starts.
            for (uint seed = 1; seed <= 150; seed++)
            {
                var layout = Grove(seed, 7, 9, 6, 4);
                int bar = WeaveGenerator.MinReach(layout.Width, layout.Height);

                for (int p = 0; p < layout.Pairs.Count; p++)
                {
                    int apart = layout.Distance(layout.Pairs[p].Heart, layout.Pairs[p].Critter) + 1;
                    Assert.GreaterOrEqual(apart, bar,
                        $"seed {seed} pair {p} has its ends {apart} cell(s) apart, under the "
                        + $"{bar} this grove requires");
                }
            }
        }

        [Test]
        public void EveryBeadSitsOffEveryShortestRouteBetweenItsOwnPairsEnds()
        {
            // A bead a player threads on the way past without going out of their way is
            // decoration, which is invariant 5d's rule for this mode. It is countable before the
            // bead is placed, so it is checked rather than hoped for.
            for (uint seed = 1; seed <= 120; seed++)
            {
                var layout = Grove(seed, 7, 9, 6, 6);

                foreach (var bead in layout.Beads)
                {
                    var ends = layout.Pairs[bead.Pair];
                    int direct = layout.Distance(ends.Heart, ends.Critter);
                    int around = layout.Distance(ends.Heart, bead.Cell)
                               + layout.Distance(bead.Cell, ends.Critter);

                    Assert.Greater(around, direct,
                        $"seed {seed}: the bead on cell {bead.Cell} lies on a shortest route for "
                        + "its own pair, so threading it asks for nothing");
                }
            }
        }

        [Test]
        public void ABeadIsNeverStoodOnAnEndpointAndNeverSharedByTwoPairs()
        {
            for (uint seed = 1; seed <= 120; seed++)
            {
                var layout = Grove(seed, 7, 9, 6, 6);
                var seen = new HashSet<int>();

                foreach (var bead in layout.Beads)
                {
                    Assert.IsTrue(seen.Add(bead.Cell),
                                  $"seed {seed}: two beads share cell {bead.Cell}");
                    Assert.AreEqual(-1, layout.EndpointAt(bead.Cell),
                                  $"seed {seed}: the bead on {bead.Cell} is standing on an endpoint");
                    Assert.AreEqual(bead.Pair, layout.BeadOwner(bead.Cell));
                }
            }
        }

        [Test]
        public void NoPairIsEverGivenMoreBeadsThanTheModeAllows()
        {
            var layout = Grove(11, 7, 9, 6, 99);

            Assert.LessOrEqual(layout.Beads.Count, WeaveGenerator.MostBeads(6));
            for (int p = 0; p < layout.Pairs.Count; p++)
                Assert.LessOrEqual(layout.BeadsOf(p).Count, 1,
                    "a channel with two beads is a tour to remember rather than a route to find");
        }

        [Test]
        public void TheChannelsOfAGroveAreNotAllTheSameLength()
        {
            // A full grove split evenly is four identical problems. The generator's slack is what
            // buys one long snake and one short hop, and it is the only thing between "solvable
            // and challenging" and "solvable and monotonous".
            int varied = 0;

            for (uint seed = 1; seed <= 60; seed++)
            {
                var layout = Grove(seed);

                int shortest = int.MaxValue, longest = 0;
                for (int p = 0; p < layout.Pairs.Count; p++)
                {
                    int length = layout.Solution(p).Count;
                    if (length < shortest) shortest = length;
                    if (length > longest) longest = length;
                }

                if (longest >= shortest * 2) varied++;
            }

            Assert.GreaterOrEqual(varied, 25,
                                  $"only {varied} of 60 groves had a channel twice another's "
                                  + "length; the walk budget has stopped varying them");
        }

        [Test]
        public void NoTwoPairsShareAnEndpointOrACellOfTheSolution()
        {
            for (uint seed = 1; seed <= 120; seed++)
            {
                var layout = Grove(seed, 7, 9, 4, 2);
                var used = new HashSet<int>();

                for (int p = 0; p < layout.Pairs.Count; p++)
                    foreach (int cell in layout.Solution(p))
                        Assert.IsTrue(used.Add(cell),
                                      $"seed {seed}: cell {cell} is in two solutions at once");
            }
        }

        [Test]
        public void EveryPairWearsItsOwnColour()
        {
            var layout = Grove();
            var seen = new HashSet<int>();

            foreach (var pair in layout.Pairs)
            {
                Assert.AreNotEqual(Energy.None, pair.Colour);
                Assert.IsTrue(seen.Add(pair.Colour),
                              "two pairs share a colour, so the player cannot tell which "
                              + "crystal belongs to which critter");
            }
        }

        [Test]
        public void TheSameSeedDealsTheSameGroveDownToItsBeads()
        {
            var a = Grove(4242, 7, 9, 6, 4);
            var b = Grove(4242, 7, 9, 6, 4);

            Assert.AreEqual(a.Par, b.Par);
            Assert.AreEqual(a.Beads.Count, b.Beads.Count);

            for (int p = 0; p < a.Pairs.Count; p++)
            {
                Assert.AreEqual(a.Pairs[p].Heart, b.Pairs[p].Heart);
                Assert.AreEqual(a.Pairs[p].Critter, b.Pairs[p].Critter);
            }
            for (int i = 0; i < a.Beads.Count; i++)
            {
                Assert.AreEqual(a.Beads[i].Cell, b.Beads[i].Cell);
                Assert.AreEqual(a.Beads[i].Pair, b.Beads[i].Pair);
            }
        }

        [Test]
        public void DifferentSeedsDealDifferentGroves()
        {
            var a = Grove(1);
            var b = Grove(2);

            bool same = a.Pairs[0].Heart == b.Pairs[0].Heart
                     && a.Pairs[0].Critter == b.Pairs[0].Critter;
            Assert.IsFalse(same, "two seeds produced the same opening");
        }

        // ------------------------------------------------------------------ the rules
        [Test]
        public void AChannelMustRunBetweenItsOwnTwoEndpoints()
        {
            var layout = Grove();
            var run = new WeaveRun(layout);
            var solution = layout.Solution(0);

            Assert.IsFalse(run.IsLegal(0, new List<int> { solution[0] }),
                           "one cell is not a channel");
            Assert.IsFalse(run.IsLegal(1, solution),
                           "a channel may not be claimed by the wrong pair");
            Assert.IsTrue(run.IsLegal(0, solution));
        }

        [Test]
        public void AChannelMayBeDrawnFromEitherEnd()
        {
            var layout = Grove();
            var run = new WeaveRun(layout);

            var backwards = new List<int>(layout.Solution(0));
            backwards.Reverse();

            Assert.IsTrue(run.Draw(0, backwards),
                          "dragging from the critter has to work as well as from the crystal");
            Assert.IsTrue(run.IsJoined(0));
        }

        [Test]
        public void AChannelMustStepOrthogonallyAndMayNotDoubleBack()
        {
            var layout = Grove();
            var run = new WeaveRun(layout);
            var ends = layout.Pairs[0];

            Assert.IsFalse(run.IsLegal(0, new List<int> { ends.Heart, ends.Critter }),
                           "the ends are not adjacent, so that is a jump");

            var doubled = new List<int>(layout.Solution(0));
            doubled.Insert(1, doubled[0]);
            Assert.IsFalse(run.IsLegal(0, doubled), "a channel may not visit a cell twice");
        }

        [Test]
        public void TwoChannelsMayNeverShareACell()
        {
            // The rule the entire puzzle rests on. Without it every pair is joined by walking
            // straight there and the grove is a formality.
            var layout = Grove();
            var run = new WeaveRun(layout);

            Assert.IsTrue(run.Draw(0, layout.Solution(0)));

            var trespass = new List<int> { layout.Pairs[1].Heart };
            foreach (int cell in layout.Solution(0))
                if (layout.Adjacent(trespass[trespass.Count - 1], cell)) { trespass.Add(cell); break; }

            if (trespass.Count > 1)
                Assert.IsFalse(run.Draw(1, trespass),
                               "a channel was allowed to cross another");
        }

        [Test]
        public void AChannelMayNotRunThroughSomebodyElsesEndpoint()
        {
            var layout = Grove();
            var run = new WeaveRun(layout);

            for (int p = 0; p < layout.Pairs.Count; p++)
            {
                Assert.AreEqual(p, run.OwnerOf(layout.Pairs[p].Heart));
                Assert.AreEqual(p, run.OwnerOf(layout.Pairs[p].Critter));
                Assert.IsFalse(run.Free(p == 0 ? 1 : 0, layout.Pairs[p].Heart),
                               "another pair was allowed onto this crystal");
            }
        }

        // ------------------------------------------------------------------ the beads
        [Test]
        public void ABeadIsItsOwnPairsGroundFromTheFirstFrameAndNobodyElsesEver()
        {
            // Half of what a bead is for. It is a doorway to one colour and a wall to the other
            // five, and the wall half is what pushes the other channels into each other.
            var layout = Grove(7, 7, 9, 4, 3);
            var run = new WeaveRun(layout);

            Assert.Greater(layout.Beads.Count, 0, "this seed should carry beads");

            foreach (var bead in layout.Beads)
            {
                Assert.AreEqual(bead.Pair, run.OwnerOf(bead.Cell),
                                "a bead is standing on the board before anything is drawn");
                Assert.IsTrue(run.Free(bead.Pair, bead.Cell),
                              "a pair must be able to draw through its own bead");

                int stranger = bead.Pair == 0 ? 1 : 0;
                Assert.IsFalse(run.Free(stranger, bead.Cell),
                               "another channel was allowed into a bead that is not its own");
            }
        }

        [Test]
        public void AChannelThatMissesItsOwnBeadIsLegalAndSimplyDoesNotFinishTheGrove()
        {
            //  A . a   The pair can reach its critter along the top row, which is legal and
            //  . o .   leaves the bead untouched. Refusing the drag instead would mean a finger
            //  . . .   that reached its critter being rejected for a reason elsewhere on the
            //          board, which is the fault the fill rule had.
            var grove = ByHand(3, 3, new[] { Pair(0, 2, new[] { 0, 3, 4, 5, 2 }, new[] { 4 }) });
            var run = new WeaveRun(grove);

            var past = new List<int> { 0, 1, 2 };
            Assert.IsTrue(run.IsLegal(0, past), "going round a bead is not an illegal move");
            Assert.IsTrue(run.Draw(0, past));

            Assert.AreEqual(1, run.Joined, "the critter is awake");
            Assert.IsFalse(run.IsThreaded(0));
            Assert.AreEqual(1, run.BeadsLeft);
            Assert.IsFalse(run.IsSolved,
                           "a grove with a bead still waiting was reported finished");

            // And threading it finishes the grove.
            Assert.IsTrue(run.Draw(0, new List<int> { 0, 3, 4, 5, 2 }));
            Assert.IsTrue(run.IsThreaded(0));
            Assert.AreEqual(0, run.BeadsLeft);
            Assert.IsTrue(run.IsSolved);
        }

        [Test]
        public void TakingAChannelBackUnthreadsItsBeadsAndLeavesThemStanding()
        {
            var grove = ByHand(3, 3, new[] { Pair(0, 2, new[] { 0, 3, 4, 5, 2 }, new[] { 4 }) });
            var run = new WeaveRun(grove);

            Assert.IsTrue(run.Draw(0, grove.Solution(0)));
            Assert.IsTrue(run.IsThreaded(0));

            run.Erase(0);

            Assert.IsFalse(run.IsThreaded(0), "a bead stayed lit after its channel was taken up");
            Assert.AreEqual(0, run.OwnerOf(4), "a bead does not stop being a bead");
            Assert.AreEqual(1, run.BeadsLeft);
        }

        [Test]
        public void ARefusedRedrawLeavesTheBeadsExactlyAsTheyWere()
        {
            var grove = ByHand(3, 3, new[] { Pair(0, 2, new[] { 0, 3, 4, 5, 2 }, new[] { 4 }) });
            var run = new WeaveRun(grove);

            Assert.IsTrue(run.Draw(0, grove.Solution(0)));
            Assert.IsTrue(run.IsThreaded(0));

            Assert.IsFalse(run.Draw(0, new List<int> { 0, 9999 }));

            Assert.IsTrue(run.IsJoined(0), "a refused redraw destroyed the channel that was there");
            Assert.IsTrue(run.IsThreaded(0), "and it unthreaded a bead on the way");
        }

        // ------------------------------------------------------------------ the board
        [Test]
        public void ARefusedChannelLeavesTheBoardExactlyAsItWas()
        {
            var layout = Grove();
            var run = new WeaveRun(layout);

            Assert.IsTrue(run.Draw(0, layout.Solution(0)));
            int occupied = run.Occupied;

            Assert.IsFalse(run.Draw(0, new List<int> { layout.Pairs[0].Heart, 9999 }));

            Assert.IsTrue(run.IsJoined(0));
            Assert.AreEqual(occupied, run.Occupied);
        }

        [Test]
        public void APairMayRedrawOverItsOwnGround()
        {
            // Redrawing has to take the old channel up first, or a pair is refused for colliding
            // with itself — which would make any route change impossible without erasing.
            var layout = Grove();
            var run = new WeaveRun(layout);

            Assert.IsTrue(run.Draw(0, layout.Solution(0)));
            Assert.IsTrue(run.Draw(0, layout.Solution(0)), "a pair could not redraw its own route");
            Assert.IsTrue(run.IsJoined(0));
        }

        [Test]
        public void ErasingAChannelFreesItsGroundButKeepsItsEnds()
        {
            var layout = Grove();
            var run = new WeaveRun(layout);
            var solution = layout.Solution(0);

            run.Draw(0, solution);
            run.Erase(0);

            Assert.IsFalse(run.IsJoined(0));
            Assert.AreEqual(-1, run.OwnerOf(solution[solution.Count / 2]),
                            "the middle of an erased channel has to become free ground again");
            Assert.AreEqual(0, run.OwnerOf(layout.Pairs[0].Heart),
                            "a crystal does not stop being a crystal");
        }

        [Test]
        public void ABeadlessGroveIsFinishedTheMomentTheLastCritterWakes()
        {
            // What the opening rungs of the chapter are: the mode's own rule and nothing added
            // to it. Nothing else may creep back in — this is the assertion that would fail if
            // a coverage condition ever returned.
            var layout = Grove(3, 5, 6, 3);
            Assert.IsFalse(layout.HasBeads);

            var run = new WeaveRun(layout);

            for (int p = 0; p < layout.Pairs.Count; p++)
            {
                Assert.IsFalse(run.IsSolved, "solved before the last pair was joined");
                Assert.IsTrue(run.Draw(p, layout.Solution(p)));
            }

            Assert.IsTrue(run.IsSolved);
        }

        [Test]
        public void AGroveIsFinishedWithGroundToSpareAndThatIsTheWholePoint()
        {
            // The rule this mode shipped with and no longer has. A weave used to require that
            // every cell of the grove was covered, which sent the player the long way round for
            // a reason nothing on the board could show, and produced a state — every critter
            // awake, nothing happening — that was reported from play as a bug.
            //
            // Shown by taking the generator's own arrangement, which does fill the grove, and
            // shortening one channel through the ground that frees up. Note what this test may
            // not do: route every pair greedily by its own shortest line and expect that to
            // work. It does not, on any board this generator now deals — that is what
            // MinSlack means, and an earlier version of this test asserting otherwise failed
            // for exactly the right reason.
            var layout = Grove(9, 7, 9, 4);
            Assert.IsFalse(layout.HasBeads, "a bead would make a shortcut a different question");

            var run = new WeaveRun(layout);
            Assert.IsTrue(run.DrawSolution());
            Assert.AreEqual(layout.Count, run.Occupied, "the carved arrangement fills the grove");

            int shortened = -1;
            for (int p = 0; p < layout.Pairs.Count && shortened < 0; p++)
            {
                run.Erase(p);

                var direct = Shortcut(run, layout, p);
                Assert.IsNotNull(direct, $"pair {p} lost its own route when it was taken up");

                if (direct.Count < layout.Solution(p).Count)
                {
                    Assert.IsTrue(run.Draw(p, direct));
                    shortened = p;
                }
                else
                {
                    Assert.IsTrue(run.Draw(p, layout.Solution(p)));
                }
            }

            Assert.GreaterOrEqual(shortened, 0, "no channel on this grove could be shortened");
            Assert.AreEqual(run.Pairs, run.Joined);
            Assert.Less(run.Occupied, layout.Count,
                        "this test is worthless unless the shortcut really did leave ground bare");
            Assert.IsTrue(run.IsSolved,
                          "a grove was refused for having ground left over, which is the rule "
                          + "this mode deliberately no longer has");
        }

        /// <summary>
        /// The shortest legal route between one pair's ends over whatever ground is free, found
        /// by breadth-first search. This is the route a player draws when nothing is in the way.
        /// </summary>
        static List<int> Shortcut(WeaveRun run, WeaveLayout layout, int pair)
        {
            int from = layout.Pairs[pair].Heart, to = layout.Pairs[pair].Critter;

            var came = new Dictionary<int, int> { { from, -1 } };
            var queue = new Queue<int>();
            queue.Enqueue(from);

            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                if (at == to) break;

                int x = at % layout.Width, y = at / layout.Width;
                for (int s = 0; s < WeaveLayout.Steps.Length; s++)
                {
                    int nx = x + WeaveLayout.Steps[s].dx, ny = y + WeaveLayout.Steps[s].dy;
                    if (!layout.Inside(nx, ny)) continue;

                    int next = layout.Index(nx, ny);
                    if (came.ContainsKey(next)) continue;
                    if (next != to && !run.Free(pair, next)) continue;

                    came[next] = at;
                    queue.Enqueue(next);
                }
            }

            if (!came.ContainsKey(to)) return null;

            var path = new List<int>();
            for (int at = to; at >= 0; at = came[at]) path.Add(at);
            path.Reverse();
            return path;
        }

        // ------------------------------------------------------------------ the lesson's stroke
        [Test]
        public void ABeadCanBeDemonstratedWithoutGivingAwayItsOwnRoute()
        {
            // The bead lesson traces a hand straight across a ring, and what it must never do is
            // trace the carved solution — that is the answer to part of the grove. So the two
            // cells it picks are opposite neighbours chosen by geometry, and every shipped grove
            // with beads has to be able to offer at least one.
            var layout = Grove(7, 7, 9, 4, 3);
            Assert.Greater(layout.Beads.Count, 0, "this seed should carry beads");

            int offered = 0;

            for (int b = 0; b < layout.Beads.Count; b++)
            {
                if (!layout.StrokeThrough(b, out int from, out int to)) continue;

                offered++;
                int cell = layout.Beads[b].Cell;

                Assert.IsTrue(layout.Adjacent(from, cell), "the stroke does not reach the ring");
                Assert.IsTrue(layout.Adjacent(cell, to), "the stroke does not leave the ring");
                Assert.AreNotEqual(from, to, "the hand came back the way it went in");

                // Opposite rather than merely adjacent: a right angle through a ring reads as
                // the hand arriving at it, which is the one thing this lesson is not about.
                Assert.AreEqual(2, layout.Distance(from, to), "the stroke turns a corner");

                Assert.Less(layout.Reserved(from), 0, "the stroke starts on somebody's ground");
                Assert.Less(layout.Reserved(to), 0, "the stroke ends on somebody's ground");
            }

            Assert.Greater(offered, 0, "no bead on this grove could be demonstrated");
        }

        [Test]
        public void ABeadWithNoRoomToBeTracedIsRefusedRatherThanGuessedAt()
        {
            //  A o a   The bead sits in the corner of a 3x1 grove, so neither axis offers two
            //          free opposite neighbours: left is the crystal and right is the critter.
            var layout = ByHand(3, 1, new[]
            {
                Pair(0, 2, new[] { 0, 1, 2 }, new[] { 1 }),
            });

            Assert.IsFalse(layout.StrokeThrough(0, out int from, out int to));
            Assert.AreEqual(-1, from);
            Assert.AreEqual(-1, to);

            // And a bead that does not exist is refused the same way rather than throwing at a
            // player who has reached a lesson nobody could reproduce.
            Assert.IsFalse(layout.StrokeThrough(4, out _, out _));
            Assert.IsFalse(layout.StrokeThrough(-1, out _, out _));
        }

        [Test]
        public void ADemonstrationRunsAcrossWhereItCanAndDownWhereItCannot()
        {
            //  A . a   Both axes are free around the bead, so across is taken: a horizontal
            //  . o .   stroke is the one that cannot be mistaken for the hand simply arriving
            //  . . .   from off the board.
            var open = ByHand(3, 3, new[] { Pair(0, 2, new[] { 0, 3, 4, 5, 2 }, new[] { 4 }) });

            Assert.IsTrue(open.StrokeThrough(0, out int from, out int to));
            Assert.AreEqual(3, from);
            Assert.AreEqual(5, to);

            //  . . .   Here the crystal and the critter *are* the bead's left and right, so
            //  A o a   across is refused and down is what is left — the branch that has to
            //  . . .   exist, because ground reserved to somebody is never traced over.
            var pinched = ByHand(3, 3, new[] { Pair(3, 5, new[] { 3, 4, 5 }, new[] { 4 }) });

            Assert.IsTrue(pinched.StrokeThrough(0, out from, out to));
            Assert.AreEqual(1, from);
            Assert.AreEqual(7, to);
        }

        [Test]
        public void ResettingReturnsTheGroveToItsEndpointsAndItsBeads()
        {
            var layout = Grove(7, 7, 9, 4, 3);
            var run = new WeaveRun(layout);

            run.DrawSolution();
            run.Reset();

            Assert.AreEqual(0, run.Joined);
            Assert.AreEqual(layout.Beads.Count, run.BeadsLeft);
            Assert.AreEqual(layout.Pairs.Count * 2 + layout.Beads.Count, run.Occupied,
                            "only the crystals, the critters and the beads should be left standing");
        }
    }
}
