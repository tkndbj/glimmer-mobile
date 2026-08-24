using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Lightweave: join every crystal to the critter wanting its colour, with no two channels
    /// crossing.
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
    /// </summary>
    public sealed class WeaveTests
    {
        static WeaveLayout Grove(uint seed = 7, int w = 7, int h = 9, int pairs = 4)
            => WeaveGenerator.Build(w, h, pairs, seed);

        // ------------------------------------------------------------------ the generator
        [Test]
        public void EveryGeneratedGroveCanActuallyBeSolved()
        {
            // Three hundred seeds, played rather than trusted. A generator that quietly produces
            // an impossible board is the one failure this mode cannot survive.
            for (uint seed = 1; seed <= 300; seed++)
            {
                var layout = Grove(seed);
                var run = new WeaveRun(layout);

                Assert.IsTrue(run.DrawSolution(),
                              $"seed {seed} produced a grove its own solution cannot solve");
                Assert.IsTrue(run.IsSolved, $"seed {seed} did not finish");
            }
        }

        [Test]
        public void EveryGeneratedGroveFillsItsGroundCompletely()
        {
            // Difficulty here is congestion, and the strongest form of it is a grove with no spare
            // ground at all: every cell belongs to exactly one channel, so the no-crossing rule
            // bites on every step rather than only in the tight corners.
            //
            // Asserted over every seed rather than as a proportion, because the way this fails is
            // not one unlucky board — it is the generator quietly falling back to something slack
            // for most of them, which is exactly what a budget-less walk did here: 3,993 attempts
            // in every 4,000 were rejected and more than half of all seeds came back as the
            // straight-rows fallback. A per-board test cannot see that; only the sweep can.
            for (uint seed = 1; seed <= 200; seed++)
            {
                var layout = Grove(seed);

                Assert.IsTrue(layout.IsComplete,
                              $"seed {seed} left {layout.Count - layout.SolutionLength} of its "
                              + $"{layout.Count} cells spare, so there is room to wander");
                Assert.GreaterOrEqual(layout.Coverage, WeaveGenerator.MinCoverage);
            }
        }

        [Test]
        public void TheChannelsOfAGroveAreNotAllTheSameLength()
        {
            // A full grove split four ways evenly is four identical problems. The generator's
            // slack is what buys one long snake and one short hop, and it is the only thing
            // between "solvable and challenging" and "solvable and monotonous".
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

            Assert.GreaterOrEqual(varied, 30,
                                  $"only {varied} of 60 groves had a channel twice another's "
                                  + "length; the walk budget has stopped varying them");
        }

        [Test]
        public void EveryPairIsFarEnoughApartToBeWorthDrawing()
        {
            for (uint seed = 1; seed <= 120; seed++)
            {
                var layout = Grove(seed);

                for (int p = 0; p < layout.Pairs.Count; p++)
                {
                    var solution = layout.Solution(p);
                    Assert.GreaterOrEqual(solution.Count, WeaveGenerator.MinPathLength,
                                          $"seed {seed} pair {p} is a two-step walk");

                    Assert.IsFalse(layout.Adjacent(layout.Pairs[p].Heart, layout.Pairs[p].Critter),
                                   $"seed {seed} pair {p} has its ends touching");
                }
            }
        }

        [Test]
        public void NoTwoPairsShareAnEndpointOrACellOfTheSolution()
        {
            for (uint seed = 1; seed <= 120; seed++)
            {
                var layout = Grove(seed);
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
        public void TheSameSeedDealsTheSameGrove()
        {
            var a = Grove(4242);
            var b = Grove(4242);

            Assert.AreEqual(a.SolutionLength, b.SolutionLength);
            for (int p = 0; p < a.Pairs.Count; p++)
            {
                Assert.AreEqual(a.Pairs[p].Heart, b.Pairs[p].Heart);
                Assert.AreEqual(a.Pairs[p].Critter, b.Pairs[p].Critter);
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

            // A path for pair 1 that deliberately runs over pair 0's ground.
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

            // Every endpoint is owned from the first frame, before anything is drawn.
            for (int p = 0; p < layout.Pairs.Count; p++)
            {
                Assert.AreEqual(p, run.OwnerOf(layout.Pairs[p].Heart));
                Assert.AreEqual(p, run.OwnerOf(layout.Pairs[p].Critter));
                Assert.IsFalse(run.Free(p == 0 ? 1 : 0, layout.Pairs[p].Heart),
                               "another pair was allowed onto this crystal");
            }
        }

        [Test]
        public void ARefusedChannelLeavesTheBoardExactlyAsItWas()
        {
            var layout = Grove();
            var run = new WeaveRun(layout);

            Assert.IsTrue(run.Draw(0, layout.Solution(0)));
            int occupied = run.Occupied;

            Assert.IsFalse(run.Draw(0, new List<int> { layout.Pairs[0].Heart, 9999 }));

            Assert.IsTrue(run.IsJoined(0), "a refused redraw destroyed the channel that was there");
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
        public void TheGroveIsSolvedOnlyWhenEveryPairIsJoined()
        {
            var layout = Grove();
            var run = new WeaveRun(layout);

            for (int p = 0; p < layout.Pairs.Count; p++)
            {
                Assert.IsFalse(run.IsSolved, "solved before the last pair was joined");
                Assert.IsTrue(run.Draw(p, layout.Solution(p)));
                Assert.AreEqual(p + 1, run.Joined);
            }

            Assert.IsTrue(run.IsSolved);
        }

        [Test]
        public void ResettingReturnsTheGroveToItsEndpoints()
        {
            var layout = Grove();
            var run = new WeaveRun(layout);

            run.DrawSolution();
            run.Reset();

            Assert.AreEqual(0, run.Joined);
            Assert.AreEqual(layout.Pairs.Count * 2, run.Occupied,
                            "only the crystals and critters should be left standing");
        }
    }
}
