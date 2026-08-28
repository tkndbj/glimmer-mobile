using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The hedge: a barrier grown along the edge <em>between</em> two cells, which no channel may
    /// cross.
    ///
    /// <para>
    /// <b>A fixture of its own, for <c>BriarTests</c>' reason.</b> A mechanic that changes what a
    /// board <em>costs</em> touches more than the rule it adds — the floor every threshold in the
    /// mode derives from, the bound that decides whether a run is lost, the reading a ladder is
    /// authored against and the demonstration a lesson traces. Those are five separate claims and
    /// they belong somewhere they can be read as a set, on boards small enough to check by hand,
    /// rather than scattered through a suite about beads.
    /// </para>
    /// <para>
    /// <b>The one that would have shipped a broken chapter</b> is
    /// <see cref="AHedgeLengthensTheFloorOfEveryPairItStandsAcross"/>. Distances here were
    /// Manhattan, which walks straight through a barrier, so a hedged grove would have been graded
    /// against a floor no arrangement of it could reach — three stars under the best possible play,
    /// on every board in the chapter, with the board still solvable and every other check green.
    /// That is invariant 22's stranded band, and invariant 20i is where the reasoning lives.
    /// </para>
    /// </summary>
    public sealed class WeaveHedgeTests
    {
        /// <summary>
        /// A grove built by hand: the pairs, their carved routes, and the hedges grown on it.
        ///
        /// Small enough to reason about on paper, which is the whole point — every number below
        /// is one somebody can check by counting cells, rather than one read back out of the
        /// generator that produced it.
        /// </summary>
        static WeaveLayout ByHand(int width, int height,
                                  (int heart, int critter, int[] route)[] pairs,
                                  WeaveHedge[] hedges,
                                  WeaveBead[] beads = null)
        {
            var made = new WeavePair[pairs.Length];
            var routes = new int[pairs.Length][];

            for (int p = 0; p < pairs.Length; p++)
            {
                made[p] = new WeavePair(pairs[p].heart, pairs[p].critter,
                                        WeaveGenerator.Palette[p]);
                routes[p] = pairs[p].route;
            }

            return new WeaveLayout(width, height, made, routes, beads, hedges);
        }

        /// <summary>
        /// One row of five, joined end to end:
        /// <code>
        ///   0  1  2  3  4
        /// </code>
        /// The whole board is that row, so every route is forced and every number is countable.
        /// </summary>
        static WeaveLayout OneRow(params WeaveHedge[] hedges)
            => ByHand(5, 1,
                      new[] { (0, 4, new[] { 0, 1, 2, 3, 4 }) },
                      hedges);

        // ------------------------------------------------------------------ the rule itself
        [Test]
        public void TwoCellsWithAHedgeBetweenThemAreNoLongerNeighbours()
        {
            // Upright, between cells 1 and 2 of the row. Adjacent is the one place the rule is
            // enforced — WeaveBoard walks a path through it, the view refuses a drag through it,
            // the solver builds its neighbour table from it and a demonstration is checked
            // against it — so this is the assertion the whole mechanic rests on.
            var open = OneRow();
            var walled = OneRow(new WeaveHedge(1, true, 1));

            Assert.IsTrue(open.Adjacent(1, 2), "cells side by side on an open grove are neighbours");
            Assert.IsFalse(walled.Adjacent(1, 2), "a hedge between two cells is not a way");

            // And only that way: the barrier is one edge, not a wall round a cell.
            Assert.IsTrue(walled.Adjacent(0, 1));
            Assert.IsTrue(walled.Adjacent(2, 3));
        }

        [Test]
        public void AChannelDrawnAcrossAHedgeIsRefusedWhole()
        {
            var walled = OneRow(new WeaveHedge(1, true, 1));
            var board = new WeaveBoard(walled);

            Assert.IsFalse(board.Draw(0, new[] { 0, 1, 2, 3, 4 }),
                "a channel stepping across a hedge is not a legal channel");

            Assert.AreEqual(0, board.PathOf(0).Count,
                "a refused channel leaves the board exactly as it was");
        }

        [Test]
        public void APairWalledAwayFromItsCritterCanReachItNoWayAtAll()
        {
            // A single row cut in two is the only shape where a barrier really does seal a pair
            // off, and it is what WeaveVerdict needs to see: a grove that cannot be finished at
            // any price has to end the run rather than sit there affordable for ever.
            var walled = OneRow(new WeaveHedge(1, true, 1));
            var board = new WeaveBoard(walled);

            Assert.AreEqual(-1, board.Reach(0),
                "Reach walks over open ways only, or a walled-off pair reads as affordable");
        }

        // ------------------------------------------------------------------ the floor
        [Test]
        public void AGroveWithNothingGrownMeasuresEveryDistanceAsAStraightLine()
        {
            // The claim that made this mechanic free for everything already shipped. A
            // breadth-first walk over an open rectangular grid returns exactly |dx| + |dy|, so
            // every par, star line and seed of the first two chapters is unmoved — and the offline
            // runtime diff (Tools/verify/weave.py) proves it board for board on the real ones.
            var open = ByHand(6, 5, new[] { (0, 29, new[] { 0 }) }, null);

            for (int a = 0; a < open.Count; a++)
                for (int b = 0; b < open.Count; b++)
                    Assert.AreEqual(open.Distance(a, b), open.Span(a, b),
                        $"span {a}->{b} is not the straight line on a grove with no hedges");
        }

        /// <summary>
        /// Two rows of five with a hedge climbing down the middle:
        /// <code>
        ///   0  1 | 2  3  4
        ///   5  6 | 7  8  9        (the bar is the hedge, two cells long)
        /// </code>
        /// The pair runs 0 -> 4. Straight it is five cells; round the hedge it is seven, because
        /// leaving a monotone route and coming back always costs two.
        /// </summary>
        [Test]
        public void AHedgeLengthensTheFloorOfEveryPairItStandsAcross()
        {
            var pairs = new[] { (0, 4, new[] { 0, 1, 2, 3, 4 }) };

            var open = ByHand(5, 2, pairs, null);
            var walled = ByHand(5, 2, pairs, new[] { new WeaveHedge(1, true, 2) });

            Assert.AreEqual(5, open.Straight(0), "five cells on open ground");
            Assert.AreEqual(WeaveHedges.Unreachable, walled.Span(0, 4),
                "the hedge spans both rows, so there is no way round it at all");

            // With the run cut back to one row there is a way round, and it costs exactly two.
            var round = ByHand(5, 2, pairs, new[] { new WeaveHedge(1, true, 1) });

            Assert.AreEqual(7, round.Straight(0),
                "a monotone route that has to leave its line and come back costs two more");
            Assert.Greater(round.StraightTotal, open.StraightTotal,
                "if the floor did not move, every threshold derived from it would be a fiction");
        }

        [Test]
        public void EveryThresholdRisesWithTheHedgesAndNoneOfThemIsAuthored()
        {
            var pairs = new[] { (0, 4, new[] { 0, 1, 2, 3, 4 }) };

            var open = ByHand(5, 2, pairs, null);
            var walled = ByHand(5, 2, pairs, new[] { new WeaveHedge(1, true, 1) });

            // Par is the floor plus a cell of looking for each decision: one per pair, one per
            // bead and one per hedge. So a hedge is priced twice — for the light it really costs,
            // and for being one more thing to weigh up before the first channel goes down.
            Assert.AreEqual(open.StraightTotal + 1, open.Par);
            Assert.AreEqual(walled.StraightTotal + 1 + 1, walled.Par);

            var below = LevelTuning.Default(open.Par);
            var above = LevelTuning.Default(walled.Par);

            Assert.Greater(above.GoldThreshold, below.GoldThreshold, "three stars");
            Assert.Greater(above.SilverThreshold, below.SilverThreshold, "two stars");
            Assert.Greater(above.MoveBudget, below.MoveBudget, "the ink");
        }

        // ------------------------------------------------------------------ invariant 5d
        [Test]
        public void AHedgeThatShutsNoShortestRouteIsScenery()
        {
            // The corridor between 0 and 4 runs along the top row, so a hedge below it changes
            // nothing: the player draws the line they were going to draw and never touches it.
            // Countable before it is placed, which is what invariant 5d asks of any mechanic.
            var pairs = new[] { (0, 4, new[] { 0, 1, 2, 3, 4 }) };

            var idle = ByHand(5, 2, pairs, new[] { new WeaveHedge(6, true, 1) });
            var real = ByHand(5, 2, pairs, new[] { new WeaveHedge(1, true, 1) });

            Assert.IsFalse(idle.HedgesBite,
                "a barrier off every shortest route rejects no arrangement");
            Assert.IsTrue(real.HedgesBite,
                "a barrier across the only corridor is doing the work it was grown for");

            Assert.AreEqual(idle.StraightTotal, idle.UnhedgedTotal);
            Assert.Greater(real.StraightTotal, real.UnhedgedTotal);
        }

        // ------------------------------------------------------------------ the generator
        [Test]
        public void EveryHedgedGroveTheGeneratorDealsIsStillSolvableByItsOwnSolution()
        {
            // The reason hedges go up *before* the carve. Walling a finished arrangement would
            // mean re-proving it and sometimes failing; growing them first makes a hedged board
            // solvable for exactly the reason an open one is.
            for (uint seed = 1; seed <= 60; seed++)
            {
                var grove = WeaveGenerator.Build(8, 10, 6, seed, 6, 2);
                var board = new WeaveBoard(grove);

                Assert.IsTrue(board.DrawSolution() && board.IsSolved,
                    $"seed {seed} deals a hedged grove its own solution cannot finish");
            }
        }

        [Test]
        public void NoHedgeTheGeneratorGrowsEverSealsAnythingOff()
        {
            // A pocket cut out of the grove is not a harder board: the carve cannot fill it, and a
            // pair dealt inside one could not be joined at any price — a run that cannot be
            // finished and will not end, which is the one state invariant 20g forbids. Checked as
            // the property rather than as the guard, so it holds however the guard is written.
            for (uint seed = 1; seed <= 60; seed++)
            {
                var grove = WeaveGenerator.Build(8, 10, 6, seed, 6, 3);

                var seen = new bool[grove.Count];
                var stack = new Stack<int>();
                stack.Push(0);
                seen[0] = true;
                int found = 1;

                while (stack.Count > 0)
                {
                    int at = stack.Pop();
                    int x = at % grove.Width, y = at / grove.Width;

                    for (int d = 0; d < WeaveLayout.Steps.Length; d++)
                    {
                        int nx = x + WeaveLayout.Steps[d].dx, ny = y + WeaveLayout.Steps[d].dy;
                        if (!grove.Inside(nx, ny)) continue;

                        int next = grove.Index(nx, ny);
                        if (seen[next] || !grove.Adjacent(at, next)) continue;

                        seen[next] = true;
                        found++;
                        stack.Push(next);
                    }
                }

                Assert.AreEqual(grove.Count, found,
                    $"seed {seed} grows a fence that cuts {grove.Count - found} cell(s) off the "
                    + "grove");
            }
        }

        [Test]
        public void EveryHedgeGrownReachesASideOfTheGroveAndLeavesAWayPast()
        {
            // Anchored rather than free-floating, which is what makes a hedge do any work: on
            // open ground there are a great many shortest routes between two cells, so a barrier
            // dropped in the middle is walked round for nothing. And never all the way across,
            // or the doorway this mechanic exists to make is a wall.
            for (uint seed = 1; seed <= 40; seed++)
            {
                var grove = WeaveGenerator.Build(9, 11, 6, seed, 6, 3);

                foreach (var hedge in grove.Hedges)
                {
                    int along = hedge.Upright ? grove.Height : grove.Width;
                    int start = hedge.Upright ? hedge.Cell / grove.Width
                                              : hedge.Cell % grove.Width;

                    Assert.GreaterOrEqual(hedge.Length, WeaveGenerator.MinHedge,
                        $"seed {seed} grew a hedge of {hedge.Length}");
                    Assert.Less(hedge.Length, along,
                        $"seed {seed} grew a hedge right across the grove");
                    Assert.IsTrue(start == 0 || start + hedge.Length == along,
                        $"seed {seed} grew a hedge starting at {start} that touches no side");
                }
            }
        }

        [Test]
        public void AGroveAskedForNoHedgesIsDealtFromExactlyTheRollsItAlwaysWas()
        {
            // The reason Attempt does not roll when a level asks for none. Two chapters are pinned
            // board for board by WeaveLadderTests and by Tools/verify/weave.py, so a single roll
            // consumed here would have re-dealt twenty shipped groves — and every one of them
            // would still have been solvable, full and measured, which is what makes it the kind
            // of change nothing notices.
            for (uint seed = 1; seed <= 30; seed++)
            {
                var grove = WeaveGenerator.Build(7, 9, 6, seed, 5);

                Assert.AreEqual(0, grove.Hedges.Count, "a grove asked for none grew one");
                Assert.AreEqual(grove.StraightTotal, grove.UnhedgedTotal,
                    "a grove with no hedges has the floors of an open one");
                Assert.IsFalse(grove.HedgesBite);
            }
        }

        [Test]
        public void TheSameSeedGrowsTheSameFence()
        {
            // Everything else about this mode rests on it: a retry has to meet the board the
            // player just failed, and the fence is the *first* thing rolled, so a fence that
            // differed would re-deal every walk after it.
            for (uint seed = 1; seed <= 25; seed++)
            {
                var a = WeaveGenerator.Build(8, 11, 6, seed, 6, 3);
                var b = WeaveGenerator.Build(8, 11, 6, seed, 6, 3);

                Assert.AreEqual(a.Hedges.Count, b.Hedges.Count, $"seed {seed}");
                Assert.AreEqual(a.Par, b.Par, $"seed {seed}");

                for (int i = 0; i < a.Hedges.Count; i++)
                {
                    Assert.AreEqual(a.Hedges[i].Cell, b.Hedges[i].Cell, $"seed {seed} hedge {i}");
                    Assert.AreEqual(a.Hedges[i].Upright, b.Hedges[i].Upright, $"seed {seed}");
                    Assert.AreEqual(a.Hedges[i].Length, b.Hedges[i].Length, $"seed {seed}");
                }
            }
        }

        // ------------------------------------------------------------------ what is drawn
        [Test]
        public void EveryEdgeOfAHedgeIsInsideTheGroveAndIsTheOneItIsDrawnOn()
        {
            // A flat run is a walk along a row, so one that overshot would not fall off the end of
            // the array — it would wrap onto the next row and close a way on the far side of the
            // grove, which nothing draws and everything enforces. HedgeEdge is the one place that
            // is decided, and it is what the view walks to draw a run.
            for (uint seed = 1; seed <= 40; seed++)
            {
                var grove = WeaveGenerator.Build(9, 10, 6, seed, 6, 3);

                foreach (var hedge in grove.Hedges)
                    for (int step = 0; step < hedge.Length; step++)
                    {
                        Assert.IsTrue(grove.HedgeEdge(hedge, step, out int a, out int b),
                            $"seed {seed} has a hedge running off the grove at step {step}");

                        Assert.AreEqual(1, grove.Distance(a, b),
                            $"seed {seed} walls two cells that are not side by side");
                        Assert.IsFalse(grove.Adjacent(a, b),
                            $"seed {seed} draws a hedge between {a} and {b} without enforcing it");
                    }

                Assert.AreEqual(grove.Hedges.Count, new HashSet<string>(
                    Spellings(grove)).Count, $"seed {seed} grew the same hedge twice");
            }
        }

        static IEnumerable<string> Spellings(WeaveLayout grove)
        {
            foreach (var hedge in grove.Hedges) yield return hedge.ToString();
        }

        [Test]
        public void ADemonstrationNeverTracesAStepAcrossAHedge()
        {
            // An illegal demonstration reads as permission, where an impossible one only reads as
            // decoration — so the coaching route is held to the same Adjacent every finger is.
            for (uint seed = 1; seed <= 30; seed++)
            {
                var grove = WeaveGenerator.Build(8, 10, 6, seed, 6, 2);
                var walk = grove.CoachRoute();

                Assert.Greater(walk.Length, 1, $"seed {seed} has nothing to demonstrate");

                for (int i = 1; i < walk.Length; i++)
                    Assert.IsTrue(grove.Adjacent(walk[i - 1], walk[i]),
                        $"seed {seed} demonstrates a step across a hedge at {i}");
            }
        }
    }
}
