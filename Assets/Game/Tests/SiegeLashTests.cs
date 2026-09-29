using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Where a curse's whip lands, and when any of it is drawn.
    ///
    /// <para>
    /// <b><c>SiegeStrikeTests</c>' bargain, struck for the lash</b>: a whip reaches the raider
    /// it was thrown at because its last joint <em>is</em> that raider, and a guarantee nobody
    /// checks is a comment. No numeric gate opens a picture and the render mirror re-derives
    /// the y axis the other way up (invariant 44d), so what holds "the curse lands on the body
    /// it marked" is the shape itself, asked of the pure builders (<see cref="Lash.Point"/>,
    /// <see cref="Lash.Window"/>, <see cref="Lash.Trace"/>), which need no screen.
    /// </para>
    /// <para>
    /// <b>And the window is the half a bolt never had.</b> A bolt is revealed from its start; a
    /// whip is drawn between a tail and a head that both move, so there are two new ways for
    /// it to be wrong - drawn before it has left, and still drawn after it has gone in. Either
    /// is a black line standing on the board with nothing to explain it.
    /// </para>
    /// </summary>
    public sealed class SiegeLashTests
    {
        const float Cell = 130f, Jag = .17f;

        static Lash.Whip Whip(Vector2 from, Vector2 to, float bow, int seed, float leaves = 0f) => new Lash.Whip
        {
            From = from,
            To = to,
            Bow = bow,
            Leaves = leaves,
            Flight = .2f,
            Held = .1f,
            Withdraw = .2f,
            Seed = seed,
        };

        [Test]
        public void AWhipLandsOnTheBodyItWasThrownAtAndLeavesWhereItCameFrom()
        {
            // Thrown from the field, which is below the middle of the board, at a hill above
            // it - and to either side, because the bow changes hand with the side.
            foreach (float targetX in new[] { -340f, 0f, 410f })
                foreach (float bow in new[] { -180f, 0f, 220f })
                    foreach (float clock in new[] { 0f, .07f, .31f, 5f })
                        for (int seed = 0; seed < 6; seed++)
                        {
                            var from = new Vector2(65f, -520f);
                            var to = new Vector2(targetX, 640f);
                            var whip = Whip(from, to, bow, seed);

                            Assert.That(Lash.Point(whip, 0f, clock, Cell, Jag), Is.EqualTo(from),
                                        "a whip has to leave where it was thrown from");
                            Assert.That(Lash.Point(whip, 1f, clock, Cell, Jag), Is.EqualTo(to),
                                        $"a whip thrown at {to} landed somewhere else");
                        }
        }

        [Test]
        public void AWhipStraysNoFurtherThanItsBowAndItsWander()
        {
            var from = new Vector2(0f, -500f);
            var to = new Vector2(0f, 700f);

            foreach (float bow in new[] { -200f, 0f, 200f })
                for (int seed = 0; seed < 12; seed++)
                {
                    var whip = Whip(from, to, bow, seed);

                    for (int i = 0; i <= 40; i++)
                    {
                        float t = i / 40f;
                        var at = Lash.Point(whip, t, .13f * seed, Cell, Jag);

                        // Straight up the y axis, so everything sideways is the bow and the wander.
                        Assert.That(Mathf.Abs(at.x), Is.LessThanOrEqualTo(Mathf.Abs(bow) + Jag * Cell + .01f),
                                    $"seed {seed} strayed {at.x} at {t}");
                        Assert.That(at.y, Is.EqualTo(Mathf.Lerp(from.y, to.y, t)).Within(.01f),
                                    "a whip may bow and writhe sideways; it may not run ahead of itself");
                    }
                }
        }

        [Test]
        public void NothingIsDrawnBeforeAWhipLeavesOrAfterItHasGoneIn()
        {
            var whip = Whip(new Vector2(0f, -400f), new Vector2(200f, 500f), 120f, 3, leaves: .25f);
            var path = new List<Vector2>();

            foreach (float clock in new[] { -1f, 0f, .24f, .25f })
            {
                Lash.Window(whip, clock, out float tail, out float head);
                Assert.That(head, Is.LessThanOrEqualTo(tail), $"drawn at {clock}, before it left");

                Lash.Trace(whip, tail, head, clock, Cell, Jag, path, null);
                Assert.That(path, Is.Empty);
            }

            foreach (float clock in new[] { whip.Ends, whip.Ends + .01f, whip.Ends + 10f })
            {
                Lash.Window(whip, clock, out float tail, out float head);
                Assert.That(head, Is.LessThanOrEqualTo(tail), $"still drawn at {clock}, after it went in");
            }
        }

        [Test]
        public void TheHeadOnlyAdvancesAndTheTailOnlyFollows()
        {
            var whip = Whip(new Vector2(0f, -400f), new Vector2(-150f, 500f), -90f, 9, leaves: .1f);

            float lastHead = 0f, lastTail = 0f;

            for (int i = 0; i <= 200; i++)
            {
                float clock = whip.Ends * 1.1f * i / 200f;
                Lash.Window(whip, clock, out float tail, out float head);

                Assert.That(head, Is.InRange(0f, 1f));
                Assert.That(tail, Is.InRange(0f, 1f));
                Assert.That(tail, Is.LessThanOrEqualTo(head), $"the tail passed the head at {clock}");
                Assert.That(head, Is.GreaterThanOrEqualTo(lastHead), $"the head fell back at {clock}");
                Assert.That(tail, Is.GreaterThanOrEqualTo(lastTail), $"the tail fell back at {clock}");

                lastHead = head;
                lastTail = tail;
            }

            // It lands when it says it lands, and it stands whole for as long as it is held.
            Lash.Window(whip, whip.Lands, out float t0, out float h0);
            Assert.That(h0, Is.EqualTo(1f));
            Assert.That(t0, Is.EqualTo(0f));

            // A hair inside the hold rather than on its last instant: three floats added are not
            // the same float twice, and the instant itself is the one the tail starts on.
            Lash.Window(whip, whip.Lands + whip.Held * .99f, out float t1, out float h1);
            Assert.That(h1, Is.EqualTo(1f));
            Assert.That(t1, Is.EqualTo(0f), "a held whip had already started to go in");
        }

        [Test]
        public void ALandedWhipsLastJointIsItsTargetAndEveryJointHasAWidth()
        {
            var path = new List<Vector2>();
            var girth = new List<float>();

            for (int seed = 0; seed < 8; seed++)
            {
                var from = new Vector2(-200f + seed * 40f, -450f);
                var to = new Vector2(300f - seed * 70f, 600f);
                var whip = Whip(from, to, 140f, seed);

                // Standing, then half way in.
                foreach (float clock in new[] { whip.Lands + .05f, whip.Lands + whip.Held + whip.Withdraw * .5f })
                {
                    Lash.Window(whip, clock, out float tail, out float head);
                    Lash.Trace(whip, tail, head, clock, Cell, Jag, path, girth);

                    Assert.That(path.Count, Is.GreaterThanOrEqualTo(4));
                    Assert.That(girth.Count, Is.EqualTo(path.Count), "a joint with no width of its own");
                    Assert.That(path[path.Count - 1], Is.EqualTo(to),
                                $"seed {seed} at {clock}: the whip does not end on its body");

                    for (int i = 0; i < girth.Count; i++)
                        Assert.That(girth[i], Is.InRange(.0001f, 1f + Lash.Swell + .001f),
                                    $"joint {i} is {girth[i]} of a width");
                }
            }
        }

        [Test]
        public void AWhipIsThinWhereItHasLetGoAndFullWhereItIsRooted()
        {
            const float Reach = 900f;

            // Rooted at its source (not loose), landed (no swell): as wide at its root as along it.
            Assert.That(Lash.Girth(0f, Reach, Cell, 0f, 0f), Is.EqualTo(1f).Within(.001f));
            Assert.That(Lash.Girth(.5f, Reach, Cell, 0f, 0f), Is.EqualTo(1f).Within(.001f));

            // Let go: the tail is a point and the body is whole.
            Assert.That(Lash.Girth(0f, Reach, Cell, 1f, 0f), Is.EqualTo(Lash.TailLeast).Within(.001f));
            Assert.That(Lash.Girth(.6f, Reach, Cell, 1f, 0f), Is.EqualTo(1f).Within(.001f));

            // Flying: swollen behind the head, and closed at the very tip.
            float behindTheHead = 1f - Cell * .2f / Reach;
            Assert.That(Lash.Girth(behindTheHead, Reach, Cell, 0f, 1f), Is.GreaterThan(1.5f));
            Assert.That(Lash.Girth(1f, Reach, Cell, 0f, 1f), Is.LessThan(.2f));
        }
    }
}
