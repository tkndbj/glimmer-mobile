using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Where a stormcall's bolt lands, and what shape it is.
    ///
    /// <para>
    /// <b>This fixture exists because the answer was wrong twice while the strike was a baked
    /// reel, and a player found it both times</b>: the reel's flash had to be put on the raider
    /// by arithmetic about the frame, the sign of that arithmetic was got wrong, and what a
    /// player saw was lightning striking their own turrets. The bolt is drawn now
    /// (<see cref="Lightning"/>), and the guarantee is structural rather than arithmetical -
    /// the channel's last joint <em>is</em> the target - but a guarantee nobody checks is a
    /// comment, so it is held here against the pure joint builder, which needs no screen.
    /// </para>
    /// <para>
    /// <b>Nothing else in this project can see it.</b> No numeric gate opens a picture, and the
    /// render mirror re-derives the y axis the other way up (invariant 44d), so the one thing
    /// that can hold "the bolt lands on the thing it struck" is the shape itself.
    /// </para>
    /// </summary>
    public sealed class SiegeStrikeTests
    {
        static List<Vector2> Trunk(Vector2 from, Vector2 to, int seed, float cell = 130f, float jag = .4f)
            => Lightning.Joints(from, to, cell, jag, new System.Random(seed));

        [Test]
        public void AStrikeLandsOnTheThingItStruckAndLeavesWhereItCameFrom()
        {
            // Cell sizes and hill positions vary by phone, so this is asked at several, including
            // a raider below the middle of the board - which is where the old sign error hid,
            // because at the origin both signs give the same magnitude.
            foreach (float targetY in new[] { 0f, 120f, -260f, 640f })
                foreach (float cell in new[] { 90f, 130f, 170f })
                    for (int seed = 0; seed < 6; seed++)
                    {
                        var from = new Vector2(37f, 1400f);
                        var to = new Vector2(-88f, targetY);
                        var joints = Trunk(from, to, seed, cell);

                        Assert.That(joints[0], Is.EqualTo(from), "the bolt has to leave where it came from");
                        Assert.That(joints[joints.Count - 1], Is.EqualTo(to),
                                    $"a strike aimed at {to} with a {cell} cell landed at {joints[joints.Count - 1]}");
                    }
        }

        [Test]
        public void TheChannelWandersOnlyBetweenItsEnds()
        {
            const float Cell = 130f, Jag = .4f;
            var from = new Vector2(0f, 1200f);
            var to = new Vector2(0f, 0f);

            for (int seed = 0; seed < 20; seed++)
            {
                var joints = Trunk(from, to, seed, Cell, Jag);

                Assert.That(joints.Count, Is.InRange(Lightning.FewestSegments + 1, Lightning.MostSegments + 1));

                for (int i = 0; i < joints.Count; i++)
                {
                    // The line is x = 0, so the wander is |x|; pinched to nought at both ends
                    // and never past the jag in between.
                    Assert.That(Mathf.Abs(joints[i].x), Is.LessThanOrEqualTo(Jag * Cell + .001f),
                                $"joint {i} of seed {seed} wandered {joints[i].x}");
                }

                // And the joints march from the start to the end rather than doubling back: a
                // channel that folds over itself is a scribble, not a bolt.
                for (int i = 1; i < joints.Count; i++)
                    Assert.That(joints[i].y, Is.LessThan(joints[i - 1].y),
                                $"joint {i} of seed {seed} doubled back");
            }
        }

        [Test]
        public void TheSameSeedDrawsTheSameBoltAndANewSeedANewOne()
        {
            var from = new Vector2(12f, 900f);
            var to = new Vector2(-40f, 60f);

            var first = Trunk(from, to, 7);
            var again = Trunk(from, to, 7);
            var other = Trunk(from, to, 8);

            Assert.That(again, Is.EqualTo(first), "two devices drawing one strike draw one channel");

            bool differs = false;
            for (int i = 1; i < first.Count - 1 && i < other.Count - 1; i++)
                if ((first[i] - other[i]).sqrMagnitude > .01f) differs = true;

            Assert.That(differs, "a re-strike down the same channel is a stamp, not lightning");
        }

        [Test]
        public void ABranchLeavesTheTrunkAndNeverReachesTheTarget()
        {
            const float Cell = 130f;
            var from = new Vector2(0f, 1200f);
            var to = new Vector2(0f, 0f);
            var paths = new List<List<Vector2>>();

            for (int seed = 0; seed < 20; seed++)
            {
                Lightning.Build(paths, from, to, Cell, .4f, 3, new System.Random(seed));

                Assert.That(paths.Count, Is.EqualTo(4), "a trunk and three forks");

                var trunk = paths[0];
                for (int b = 1; b < paths.Count; b++)
                {
                    var branch = paths[b];
                    Assert.That(trunk, Does.Contain(branch[0]), "a branch leaves from a joint of the trunk");

                    float away = Vector2.Distance(branch[branch.Count - 1], to);
                    Assert.That(away, Is.GreaterThan(Cell * .5f),
                                "a second line arriving where the first did reads as two bolts");
                }
            }
        }

        [Test]
        public void ALeaderIsTheFirstPartOfTheChannelAndNothingElse()
        {
            // `Reveal` cuts the trunk by length: the leader creeps down the channel the return
            // stroke will light, so what it draws has to be the channel's own first part and
            // its cut end has to sit on the channel, not beside it.
            var trunk = Trunk(new Vector2(0f, 1000f), Vector2.zero, 3);
            var cut = new List<Vector2>();

            float total = 0f;
            for (int i = 0; i + 1 < trunk.Count; i++) total += Vector2.Distance(trunk[i], trunk[i + 1]);

            foreach (float fraction in new[] { 0f, .25f, .5f, .8f, 1f })
            {
                Lightning.Cut(trunk, fraction, cut);

                Assert.That(cut[0], Is.EqualTo(trunk[0]), "a leader starts where the bolt starts");
                for (int i = 0; i + 1 < cut.Count; i++)
                    Assert.That(cut[i], Is.EqualTo(trunk[i]), "a leader is the channel's own joints");

                float drawn = 0f;
                for (int i = 0; i + 1 < cut.Count; i++) drawn += Vector2.Distance(cut[i], cut[i + 1]);
                Assert.That(drawn, Is.EqualTo(total * fraction).Within(.01f),
                            $"a leader at {fraction} draws {drawn} of {total}");
            }

            Lightning.Cut(trunk, 1f, cut);
            Assert.That(cut, Is.EqualTo(trunk), "whole means whole");
        }

        [Test]
        public void ADischargeForksToEveryBodyItHurtAndEndsOnEachOfThem()
        {
            // An overcharge lands on a box and takes the four touching it, and what says who it
            // reached is a fork to each (`SiegeView.Arcburst`). Every fork is a trunk of its
            // own, so every one of them carries the strike's guarantee - and one that stopped
            // short of its raider would be the old sign error, met nine times at once.
            const float Cell = 130f;
            var at = new Vector2(-65f, 420f);
            var bodies = new List<Vector2>
            {
                new Vector2(-65f, 560f), new Vector2(-310f, 400f), new Vector2(190f, 455f),
                new Vector2(-40f, 250f), new Vector2(-300f, 640f),
            };
            var paths = new List<List<Vector2>>();

            for (int seed = 0; seed < 12; seed++)
            {
                Lightning.Spread(paths, at, bodies, Cell, .3f, new System.Random(seed));

                Assert.That(paths.Count, Is.EqualTo(bodies.Count), "a body the blast hurt got no fork");

                for (int i = 0; i < paths.Count; i++)
                {
                    Assert.That(paths[i][0], Is.EqualTo(at), "a fork leaves where the discharge landed");
                    Assert.That(paths[i][paths[i].Count - 1], Is.EqualTo(bodies[i]),
                                $"fork {i} of seed {seed} does not end on its body");
                }
            }
        }

        [Test]
        public void ABodyStandingWhereItLandedGetsNoFork()
        {
            // The raider the box was aimed at is very often standing on the landing itself, and
            // a bolt of no length is a dot with a mitre in it.
            var at = new Vector2(12f, 300f);
            var paths = new List<List<Vector2>>();

            Lightning.Spread(paths, at, new List<Vector2> { at, new Vector2(200f, 300f) }, 130f, .3f,
                             new System.Random(1));

            Assert.That(paths.Count, Is.EqualTo(1));
            Assert.That(paths[0][paths[0].Count - 1], Is.EqualTo(new Vector2(200f, 300f)));
        }

        [Test]
        public void ABurstThrowsItsArmsAllTheWayRoundAndNoFurtherThanItWasAsked()
        {
            const float Cell = 130f, Inner = 24f, Outer = 320f, Jag = .42f;
            const int Arms = 10;
            var at = new Vector2(40f, 380f);
            var paths = new List<List<Vector2>>();

            for (int seed = 0; seed < 20; seed++)
            {
                Lightning.Radiate(paths, at, Inner, Outer, Arms, Cell, Jag, new System.Random(seed));
                Assert.That(paths.Count, Is.EqualTo(Arms));

                var angles = new List<float>();

                foreach (var arm in paths)
                {
                    var root = arm[0] - at;
                    var tip = arm[arm.Count - 1] - at;

                    Assert.That(root.magnitude, Is.EqualTo(Inner).Within(.05f), "an arm starts at the rim of the landing");
                    Assert.That(tip.magnitude, Is.InRange(Inner + (Outer - Inner) * .5f - .05f, Outer + .05f),
                                "an arm reaches between half way and all the way");

                    foreach (var joint in arm)
                        Assert.That((joint - at).magnitude, Is.LessThanOrEqualTo(Outer + Jag * 1f * Cell),
                                    "an arm wandered out of the burst");

                    angles.Add(Mathf.Atan2(tip.y, tip.x));
                }

                // Dealt by sector: no gap between neighbours is wider than two sectors less the
                // jitter either side, so a burst never has a bald side.
                angles.Sort();
                float widest = angles[0] + Mathf.PI * 2f - angles[angles.Count - 1];
                for (int i = 0; i + 1 < angles.Count; i++) widest = Mathf.Max(widest, angles[i + 1] - angles[i]);

                Assert.That(widest, Is.LessThan(Mathf.PI * 2f / Arms * 1.8f),
                            $"seed {seed} left a gap of {widest * Mathf.Rad2Deg:0} degrees");
            }
        }
    }
}
