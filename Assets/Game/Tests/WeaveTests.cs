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
                var run = new WeaveBoard(layout);

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
            var run = new WeaveBoard(layout);
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
            var run = new WeaveBoard(layout);

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
            var run = new WeaveBoard(layout);
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
            var run = new WeaveBoard(layout);

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
            var run = new WeaveBoard(layout);

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
            var run = new WeaveBoard(layout);

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
            var run = new WeaveBoard(grove);

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
            var run = new WeaveBoard(grove);

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
            var run = new WeaveBoard(grove);

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
            var run = new WeaveBoard(layout);

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
            var run = new WeaveBoard(layout);

            Assert.IsTrue(run.Draw(0, layout.Solution(0)));
            Assert.IsTrue(run.Draw(0, layout.Solution(0)), "a pair could not redraw its own route");
            Assert.IsTrue(run.IsJoined(0));
        }

        [Test]
        public void ErasingAChannelFreesItsGroundButKeepsItsEnds()
        {
            var layout = Grove();
            var run = new WeaveBoard(layout);
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

            var run = new WeaveBoard(layout);

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

            var run = new WeaveBoard(layout);
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
        static List<int> Shortcut(WeaveBoard run, WeaveLayout layout, int pair)
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

        // ------------------------------------------------------------------ the coaching hand
        /// <summary>
        /// A 5x5 grove built so the coaching hand has a decision to make about rings.
        ///
        /// <code>
        ///    0  1  2  3  4      pair 0 runs corner-to-middle (0 -> 12) and both of its elbows
        ///    5  6  7  8  9      are blocked: a ring on 2 sits on the across-first one and a
        ///   10 11 12 13 14      ring on 10 sits on the down-first one.
        ///   15 16 17 18 19      pair 1 runs straight down the right edge (4 -> 24) and its
        ///   20 21 22 23 24      elbow is clear.
        /// </code>
        ///
        /// Both pairs are the same distance apart, so pair 0 is reached first and rejected on
        /// its rings rather than on its length — which is exactly the case the preference is
        /// about.
        /// </summary>
        static WeaveLayout RingedElbows()
            => ByHand(5, 5, new[]
            {
                Pair(0, 12, new[] { 0, 5, 6, 7, 12 }, new[] { 10 }),
                Pair(4, 24, new[] { 4, 9, 14, 19, 24 }, new[] { 2 }),
            });

        [Test]
        public void ARingIsAvoidedWhenTheBoardOffersARouteThatCan()
        {
            // The rule, on a board built to have the choice — rather than on the shipped groves,
            // where whether a clear elbow exists at all is a fact about how crowded the chapter
            // got. It did get crowded: the Nightloom hangs six rings on eighty cells and has no
            // clear elbow anywhere, which is legal and which is why asserting this against the
            // catalog turned a rule into a coincidence.
            var layout = RingedElbows();
            var walk = layout.CoachRoute();

            Assert.AreEqual(4, walk[0], "the demonstration should have moved to the clear pair");
            Assert.AreEqual(24, walk[walk.Length - 1]);

            for (int i = 1; i < walk.Length - 1; i++)
                Assert.AreEqual(-1, layout.BeadOwner(walk[i]),
                                "a ring was crossed while a route existed that need not have");
        }

        [Test]
        public void ARingIsCrossedRatherThanFallingBackToTheCarvedWalk()
        {
            // The second pass, and the judgement behind it: a demonstration that clips somebody
            // else's ring is slightly muddled, where the carved walk is the *answer* to part of
            // the grove and wanders besides. So a crowded board gets the muddled one.
            //
            // Two rings on one pair, which the generator would never deal (WeaveGenerator
            // .MostBeads is one apiece) and WeaveLayout has no opinion about — it is the cheapest
            // way to leave a single pair with no clear elbow at all.
            var layout = ByHand(5, 5, new[]
            {
                Pair(0, 12, new[] { 0, 5, 6, 7, 12 }, new[] { 2, 10 }),
            });

            var walk = layout.CoachRoute();

            Assert.AreEqual(0, walk[0]);
            Assert.AreEqual(12, walk[walk.Length - 1]);
            Assert.AreEqual(5, walk.Length, "an elbow across a 5x5 corner is five cells");

            // Three corners is an elbow; the carved walk here turns twice and would give four.
            Assert.AreEqual(3, layout.Corners(walk).Length,
                            "it fell back to the carved walk instead of crossing a ring");
        }

        [Test]
        public void ADemonstrationNeverRunsOverSomebodyElsesCrystal()
        {
            // Refused in *both* passes, unlike a ring, and the asymmetry is the point: an
            // illegal demonstration reads as permission, where an impossible one only reads as
            // decoration. Pair 1's crystal sits in the middle of pair 0's only two elbows.
            var layout = ByHand(5, 5, new[]
            {
                Pair(0, 12, new[] { 0, 5, 6, 7, 12 }, null),
                Pair(2, 10, new[] { 2, 7, 6, 5, 10 }, null),
            });

            var walk = layout.CoachRoute();

            for (int i = 1; i < walk.Length - 1; i++)
                Assert.AreEqual(-1, layout.EndpointAt(walk[i]),
                                "the hand was traced over an endpoint");
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

        // ================================================================== the ink
        /// <summary>
        /// Three cells across, one pair from corner to corner along the top, so every figure
        /// below can be counted by hand: the floor is three cells and the way round is five.
        /// </summary>
        static WeaveLayout Corner()
            => ByHand(3, 3, new[] { Pair(0, 2, new[] { 0, 1, 2 }) });

        static List<int> Across => new List<int> { 0, 1, 2 };
        static List<int> Round => new List<int> { 0, 3, 4, 5, 2 };

        [Test]
        public void AGroveWithNoBudgetIsNeverLostAndThatIsWhatUnlimitedMeans()
        {
            var run = new WeaveRun(Corner());

            Assert.IsFalse(run.Ink.Bounded);
            Assert.AreEqual(WeaveInk.Unlimited, run.Ink.Left);
            Assert.IsTrue(run.Affords(1_000_000));
            Assert.IsFalse(run.Verdict.IsLost, "a grove with no ink budget cannot be lost");
        }

        [Test]
        public void AChannelCostsACellOfInkForEveryCellItCovers()
        {
            var run = new WeaveRun(Corner(), 12);

            Assert.AreEqual(12, run.Ink.Left);
            Assert.IsTrue(run.Draw(0, Round));

            Assert.AreEqual(5, run.Ink.Spent);
            Assert.AreEqual(7, run.Ink.Left);
        }

        [Test]
        public void ARunWithNoRedrawInItSpendsExactlyWhatItOccupies()
        {
            // The whole reason the meter and the grade can be one number. A cell of light per
            // cell covered means the ink a clean run spends *is* Occupied, so what the player
            // watches count down is what their stars are read off rather than a second number
            // that can quietly disagree with it.
            var layout = Grove(7, 7, 9, 4, 3);
            var run = new WeaveRun(layout, 10_000);

            for (int p = 0; p < layout.Pairs.Count; p++)
                Assert.IsTrue(run.Draw(p, layout.Solution(p)));

            Assert.IsTrue(run.IsSolved);
            Assert.AreEqual(run.Occupied, run.Ink.Spent);
        }

        [Test]
        public void ARedrawIsChargedAgainAndTheOldLightIsGone()
        {
            var run = new WeaveRun(Corner(), 12);

            Assert.IsTrue(run.Draw(0, Round));
            Assert.IsTrue(run.Draw(0, Across), "a pair could not redraw over its own ground");

            Assert.AreEqual(8, run.Ink.Spent, "a redraw was not charged");
            Assert.AreEqual(4, run.Ink.Left);
        }

        [Test]
        public void ARefusedChannelIsChargedNothing()
        {
            var run = new WeaveRun(Corner(), 12);

            Assert.IsFalse(run.Draw(0, new List<int> { 0, 9999 }));
            Assert.AreEqual(0, run.Ink.Spent);
            Assert.IsFalse(run.CanUndo, "a refused channel was written down as a stroke");
        }

        [Test]
        public void TakingAChannelBackOnTheBoardFreesTheGroundAndNotTheLight()
        {
            // The board is what a route change goes through, and it has no idea what light is.
            // That is the whole reason erasing cannot refund: a redraw is an erase and a draw.
            var run = new WeaveRun(Corner(), 12);

            Assert.IsTrue(run.Draw(0, Round));
            run.Board.Erase(0);

            Assert.AreEqual(-1, run.OwnerOf(3), "the ground did not come free");
            Assert.AreEqual(5, run.Ink.Spent, "erasing a channel handed its light back");
        }

        [Test]
        public void APairMayCrossTheChannelItAlreadyHasStanding()
        {
            // What makes an abandoned redraw free. Nothing is taken up until a replacement
            // lands, so a pair really is standing on its own ground while it is being drawn
            // again — and being refused for colliding with yourself is not a rule anybody could
            // read off the board.
            var board = new WeaveBoard(Corner());

            Assert.IsTrue(board.Draw(0, Round));
            Assert.IsTrue(board.Free(0, 4), "a pair was refused a cell of its own channel");
            Assert.IsTrue(board.IsLegal(0, Across));
        }

        [Test]
        public void ADrawReportsWhatItReplacedSoAnUndoCanPutItBack()
        {
            var board = new WeaveBoard(Corner());

            Assert.IsTrue(board.Draw(0, Round, out var first));
            CollectionAssert.IsEmpty(first, "the first channel of a pair replaced nothing");

            Assert.IsTrue(board.Draw(0, Across, out var second));
            CollectionAssert.AreEqual(Round, second);
        }

        // ------------------------------------------------------------------ the pressure
        [Test]
        public void InkReadsItsPressureAsFractionsOfItsOwnBudget()
        {
            // A tenth is under one channel on any grove that ships, whatever size it is — which
            // is why this is a fraction and not a count of cells.
            var ink = new WeaveInk(100);
            Assert.AreEqual(InkPressure.Easy, ink.Pressure);

            ink.Spend(74);
            Assert.AreEqual(InkPressure.Easy, ink.Pressure, "26 of 100 is still room to work in");

            ink.Spend(1);
            Assert.AreEqual(InkPressure.Low, ink.Pressure, "a quarter left is worth watching");

            ink.Spend(65);
            Assert.AreEqual(InkPressure.Critical, ink.Pressure, "a tenth left is the last channel");

            Assert.AreEqual(InkPressure.Easy, new WeaveInk(WeaveInk.Unlimited).Pressure,
                            "a grove with no budget is never under pressure");
        }

        // ------------------------------------------------------------------ undo
        [Test]
        public void UndoHandsBackTheChannelAndTheLightTogether()
        {
            var run = new WeaveRun(Corner(), 12);

            Assert.IsFalse(run.CanUndo, "there was something to undo on an untouched grove");
            Assert.IsTrue(run.Draw(0, Round));
            Assert.IsTrue(run.CanUndo);

            Assert.IsTrue(run.TryUndo(out int pair));

            Assert.AreEqual(0, pair);
            Assert.IsFalse(run.IsJoined(0), "the channel was not taken back");
            Assert.AreEqual(0, run.Ink.Spent, "the light was not handed back");
            Assert.AreEqual(WeaveStrokes.Allowance - 1, run.UndosLeft);
        }

        [Test]
        public void UndoingARedrawPutsTheRouteThatWasThereBack()
        {
            // The case that makes this an undo rather than an erase. A pair being redrawn had a
            // perfectly good channel a moment ago, and taking the new one away while leaving the
            // pair bare would cost the player something they never asked to lose.
            var run = new WeaveRun(Corner(), 20);

            Assert.IsTrue(run.Draw(0, Round));
            Assert.IsTrue(run.Draw(0, Across));
            Assert.IsTrue(run.TryUndo(out _));

            Assert.IsTrue(run.IsJoined(0), "the route the redraw replaced was not put back");
            CollectionAssert.AreEqual(Round, run.PathOf(0));
            Assert.AreEqual(5, run.Ink.Spent, "only the redraw's own light should have come back");
        }

        [Test]
        public void ThereAreExactlyTwoUndosAGroveAndTheyDoNotComeBack()
        {
            var run = new WeaveRun(Corner(), 60);

            Assert.AreEqual(2, WeaveStrokes.Allowance, "the mode's whole correction budget");
            Assert.AreEqual(WeaveStrokes.Allowance, run.UndosLeft);

            for (int i = 0; i < WeaveStrokes.Allowance; i++)
            {
                Assert.IsTrue(run.Draw(0, Round));
                Assert.IsTrue(run.TryUndo(out _));
            }

            Assert.IsTrue(run.Draw(0, Round));
            Assert.IsFalse(run.CanUndo, "a third undo was offered");
            Assert.IsFalse(run.TryUndo(out _));
            Assert.IsTrue(run.IsJoined(0), "a refused undo took the channel anyway");
        }

        [Test]
        public void TheStrokeStackIsPlainArithmeticAndNeedsNoGrove()
        {
            // WeaveStrokes exists apart from the board precisely so the rule that matters — two,
            // and they do not come back — is provable over integers.
            var strokes = new WeaveStrokes();

            Assert.IsFalse(strokes.CanUndo, "an untouched stack offered an undo");

            strokes.Note(1, new[] { 4, 5 }, 7);
            Assert.AreEqual(1, strokes.Count);
            Assert.IsTrue(strokes.TryUndo(out var stroke));

            Assert.AreEqual(1, stroke.Pair);
            Assert.AreEqual(7, stroke.Cost);
            CollectionAssert.AreEqual(new[] { 4, 5 }, stroke.Replaced);
            Assert.AreEqual(WeaveStrokes.Allowance - 1, strokes.Left);
            Assert.AreEqual(0, strokes.Count);

            strokes.Reset();
            Assert.AreEqual(WeaveStrokes.Allowance, strokes.Left, "a restart hands the allowance back");
        }

        [Test]
        public void ARestartHandsBackTheChannelsTheInkAndTheUndos()
        {
            var run = new WeaveRun(Corner(), 20);

            Assert.IsTrue(run.Draw(0, Round));
            Assert.IsTrue(run.TryUndo(out _));
            Assert.IsTrue(run.Draw(0, Across));

            run.Restart();

            Assert.AreEqual(0, run.Joined);
            Assert.AreEqual(0, run.Ink.Spent);
            Assert.AreEqual(WeaveStrokes.Allowance, run.UndosLeft);
            Assert.IsFalse(run.CanUndo, "a restart left the strokes of the run before it");
        }

        // ------------------------------------------------------------------ losing
        [Test]
        public void AGroveIsLostWhenTheLightLeftCannotCoverTheCheapestFinish()
        {
            //  A . B   Two pairs down the outside columns, three cells of floor each, dealt
            //  . . .   nine. Joining the first one the long way leaves four, which still covers
            //  a . b   the three the other one needs at its very best — so the run carries on.
            //
            // The floor is what makes any of this provable rather than a guess: nothing the
            // player could do next finishes for less, including taking the first channel up
            // again, so a run is only ever ended on a board that genuinely cannot be finished.
            var grove = ByHand(3, 3, new[]
            {
                Pair(0, 6, new[] { 0, 3, 6 }),
                Pair(2, 8, new[] { 2, 5, 8 }),
            });

            var run = new WeaveRun(grove, 9);
            Assert.AreEqual(6, run.Verdict.Floor, "two pairs, three cells of floor each");
            Assert.IsTrue(run.Verdict.IsPlaying);

            Assert.IsTrue(run.Draw(0, new List<int> { 0, 1, 4, 7, 6 }));

            Assert.AreEqual(4, run.Ink.Left);
            Assert.AreEqual(3, run.Verdict.Floor, "one pair left, and its floor is three");
            Assert.IsFalse(run.Verdict.IsLost, "four cells of light still covers a floor of three");

            Assert.IsTrue(run.Draw(1, new List<int> { 2, 5, 8 }));
            Assert.IsTrue(run.Verdict.IsSolved, "a finished grove is never a lost one");
        }

        [Test]
        public void AGroveRunsDryWhenAChannelSprawls()
        {
            // The same board dealt seven cells, which is one more than the six it takes to draw
            // both pairs straight — and the sprawl this mode exists to price. The first channel
            // wanders five cells over a route worth three, and what is left cannot cover the
            // other pair's floor, so the run is over the moment it lands.
            var grove = ByHand(3, 3, new[]
            {
                Pair(0, 6, new[] { 0, 3, 6 }),
                Pair(2, 8, new[] { 2, 5, 8 }),
            });

            var run = new WeaveRun(grove, 7);
            Assert.AreEqual(6, run.Verdict.Floor, "the board can be drawn in six, so seven is winnable");
            Assert.IsFalse(run.Verdict.IsLost);

            Assert.IsTrue(run.Draw(0, new List<int> { 0, 1, 4, 7, 6 }));

            Assert.AreEqual(2, run.Ink.Left);
            Assert.AreEqual(3, run.Verdict.Floor);
            Assert.IsTrue(run.Verdict.IsLost, "a grove that cannot afford its own floor is over");
        }

        [Test]
        public void APairWalledInIsNotSomethingTheRunCanContinueWith()
        {
            //  A B a   The half a floor cannot see. B takes the middle column and A is sealed
            //  . B .   into the left of the board — so the light left may look like enough while
            //  a B A   there is no channel anywhere that could be drawn with it.
            //
            // Without this the player would sit in front of a board that cannot be finished and
            // will not end, which is exactly the state invariant 20g is about.
            var grove = ByHand(3, 3, new[]
            {
                Pair(0, 8, new[] { 0, 3, 6, 7, 8 }),
                Pair(1, 7, new[] { 1, 4, 7 }),
            });

            var run = new WeaveRun(grove, 5);

            Assert.AreEqual(5, run.Board.Reach(0), "the board opens with a way through");
            Assert.IsTrue(run.Draw(1, new List<int> { 1, 4, 7 }));

            Assert.AreEqual(-1, run.Board.Reach(0), "a walled pair has no route at any price");
            Assert.AreEqual(2, run.Ink.Left);
            Assert.IsTrue(run.Verdict.IsLost);
        }

        [Test]
        public void TheFloorCountsAJoinedPairThatStillOwesABead()
        {
            // A channel that reached its critter without threading its ring has to be drawn
            // again, and the light already spent on it is spent. A floor calling that pair
            // finished would let a grove run dry while the meter still read affordable.
            var grove = ByHand(3, 3, new[] { Pair(0, 2, new[] { 0, 3, 4, 5, 2 }, new[] { 4 }) });
            var run = new WeaveRun(grove, 20);

            Assert.IsTrue(run.Draw(0, Across), "going round a bead is legal");
            Assert.IsFalse(run.IsSolved);
            Assert.AreEqual(grove.Straight(0), run.Verdict.Floor,
                            "a pair that still owes a bead has to be paid for in full again");

            Assert.IsTrue(run.Draw(0, Round));
            Assert.IsTrue(run.IsSolved);
            Assert.AreEqual(0, run.Verdict.Floor);
        }

        // ------------------------------------------------------------------ ending the run
        [Test]
        public void ALostRunOnlyEndsOnceAndOnlyOnceItIsOwedFor()
        {
            // The guard that used to be three booleans in an if on the screen. A run decided
            // twice charges two hearts for one loss, and one decided before the first channel
            // lands charges a heart for a board the player never touched.
            var grove = ByHand(3, 3, new[]
            {
                Pair(0, 6, new[] { 0, 3, 6 }),
                Pair(2, 8, new[] { 2, 5, 8 }),
            });

            var run = new WeaveRun(grove, 7);
            Assert.IsTrue(run.Draw(0, new List<int> { 0, 1, 4, 7, 6 }));

            var lost = run.Verdict;
            Assert.IsTrue(lost.IsLost);

            Assert.IsTrue(lost.EndsTheRun(live: true, committed: true));
            Assert.IsFalse(lost.EndsTheRun(live: false, committed: true),
                           "a run already ending was decided a second time");
            Assert.IsFalse(lost.EndsTheRun(live: true, committed: false),
                           "a run nobody has drawn on yet was charged a heart");

            var playing = new WeaveRun(grove, 40).Verdict;
            Assert.IsFalse(playing.EndsTheRun(live: true, committed: true));
        }

        [Test]
        public void EveryGroveTheChapterShipsIsDealtEnoughLightToBeDrawnAtAll()
        {
            // WeaveMode.Validate makes this check on every build, over the board that actually
            // ships. Made here over the shapes the chapter is built from and a spread of seeds,
            // so a retune of LevelTuning's factors cannot quietly deal a grove less ink than its
            // pairs need even with nobody in anybody's way.
            (int w, int h, int pairs, int beads)[] rungs =
            {
                (5, 6, 3, 0), (5, 7, 4, 0), (6, 6, 4, 1), (6, 7, 4, 2), (6, 8, 5, 2),
                (7, 7, 5, 3), (7, 8, 5, 3), (7, 8, 6, 4), (7, 9, 6, 4), (7, 9, 6, 5),
            };

            foreach (var rung in rungs)
                for (uint seed = 1; seed <= 8; seed++)
                {
                    var grove = WeaveGenerator.Build(rung.w, rung.h, rung.pairs, seed, rung.beads);

                    // The real thing rather than the arithmetic restated, so this cannot pass
                    // against a formula that has stopped being the one the game deals.
                    int ink = new Content.LevelTuning(grove.Par,
                                                      Content.LevelTuning.DefaultGoldFactor,
                                                      Content.LevelTuning.DefaultSilverFactor)
                        .MoveBudget;

                    // Room to spare, not merely enough. The floor is what a perfect arrangement
                    // costs before the board forces a single detour, and every shipped grove
                    // forces some (WeaveGenerator.MinSlack), so ink that only just covered the
                    // floor would be a grove nobody could finish.
                    Assert.Less(grove.StraightTotal * 1.2f, ink,
                                $"a {rung.w}x{rung.h} grove of {rung.pairs} pairs and " +
                                $"{rung.beads} beads is dealt {ink} cells of ink against a floor " +
                                $"of {grove.StraightTotal}");
                }
        }

        // ------------------------------------------------------------------ the readout row
        [Test]
        public void EveryRowOfReadoutsAModeMayAskForLeavesRoomToReadThem()
        {
            // The spacing stopped being one arrangement the moment a mode could ask for fewer
            // than three, and a case nobody exercises is a case nobody has looked at.
            for (int count = 1; count <= ReadoutRow.Most; count++)
                Assert.IsTrue(ReadoutRow.IsClear(count, out string fault), fault);

            Assert.AreEqual(0f, ReadoutRow.XFor(0, 1), "one number belongs in the middle");
            Assert.AreEqual(0f, ReadoutRow.XFor(1, 3), "the middle of three is the middle");
            Assert.AreEqual(-ReadoutRow.XFor(1, 2), ReadoutRow.XFor(0, 2),
                            "a pair has to straddle the centre evenly");

            Assert.IsFalse(ReadoutRow.IsClear(ReadoutRow.Most + 1, out _),
                           "a row wider than the screen was called clear");
        }

        // ------------------------------------------------------------------ the band below it
        [Test]
        public void TheBandBelowAWeaveDoesNotSitOnItself()
        {
            // What used to be a paragraph of prose claiming three numbers cleared each other —
            // and the paragraph was wrong the first time it was written, because UIKit.Box pivots
            // at centre whatever it is anchored to. Geometry belongs where it can be checked, for
            // ChapterMap's reason (invariant 8a).
            Assert.IsTrue(WeaveBand.IsClear(out string fault), fault);

            Assert.Greater(WeaveBand.KeyToNotice, 0f, "the undo key and the standing line overlap");
            Assert.GreaterOrEqual(WeaveBand.NoticeToBoard, 0f,
                                  "the standing line is drawn over the grove");
            Assert.Greater(WeaveBand.BoardFloor, WeaveBand.UndoTop,
                           "the grove is drawn over the undo key");
        }

        [Test]
        public void ResettingReturnsTheGroveToItsEndpointsAndItsBeads()
        {
            var layout = Grove(7, 7, 9, 4, 3);
            var run = new WeaveBoard(layout);

            run.DrawSolution();
            run.Reset();

            Assert.AreEqual(0, run.Joined);
            Assert.AreEqual(layout.Beads.Count, run.BeadsLeft);
            Assert.AreEqual(layout.Pairs.Count * 2 + layout.Beads.Count, run.Occupied,
                            "only the crystals, the critters and the beads should be left standing");
        }
    }
}
