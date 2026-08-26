using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// A tween that <em>borrows</em> a value has to give it back, however it ends.
    ///
    /// <para>
    /// <see cref="Tween.Punch"/> reads a transform's scale, squashes it about for a third of
    /// a second and restores it in its <c>OnDone</c>. A second punch on the same transform
    /// supersedes the first on the <c>punch</c> channel — and supersession used to drop a
    /// tween exactly where it stood, without that restore, so the new punch read a
    /// half-squashed scale as its own rest and handed <em>that</em> back at the end. The
    /// error is multiplicative: every tap during a squash keeps a little more of the squash
    /// for ever, and nothing short of rebuilding the screen puts it back.
    /// </para>
    /// <para>
    /// It was reported against the home screen's companion, which is poked by hand and so is
    /// the one place a human can drive the loop fast enough to see it — but the shape is in
    /// every control here that can be punched twice inside a third of a second: a board tile
    /// tapped repeatedly, the move counter, a chest's thumps, the streak tiles.
    /// </para>
    /// <para>
    /// Driving <see cref="Tween.Tick"/> directly is what makes this assertable at all. The
    /// deformation is invisible in a compile, in a validator and in a screenshot of the
    /// source — the same reason <see cref="TweenOwnerTests"/> and <see cref="TweenCycleTests"/>
    /// exist.
    /// </para>
    /// </summary>
    public sealed class TweenRevertTests
    {
        static Transform Fresh() => new GameObject("Target").transform;

        static void Drop(Transform tr)
        {
            if (tr) Object.DestroyImmediate(tr.gameObject);
        }

        /// <summary>Mid-squash, by construction: a punch is non-uniform at almost every phase.</summary>
        static bool Squashed(Transform tr)
            => !Mathf.Approximately(tr.localScale.x, tr.localScale.y);

        // ------------------------------------------------------------------ the report
        [Test]
        public void SpamPokingNeverDeformsTheTarget()
        {
            var tr = Fresh();
            try
            {
                // Ten taps at about the rate a finger can manage, every one of them landing
                // inside the half-second squash the one before it started.
                for (int i = 0; i < 10; i++)
                {
                    Tween.Punch(tr, .28f, .5f);
                    Tween.Inst.Tick(.08f, .08f);
                }

                // Let the last one finish.
                Tween.Inst.Tick(1f, 1f);

                Assert.AreEqual(1f, tr.localScale.x, 1e-4f, "a spammed punch must not stretch");
                Assert.AreEqual(1f, tr.localScale.y, 1e-4f, "and must not squash");
                Assert.AreEqual(1f, tr.localScale.z, 1e-4f);
            }
            finally
            {
                Drop(tr);
            }
        }

        [Test]
        public void ASupersededPunchPutsTheScaleBackBeforeTheNextOneReadsIt()
        {
            // The mechanism, rather than its symptom. Stopping mid-squash and punching again
            // has to leave the second punch resting on the scale the first one found.
            var tr = Fresh();
            try
            {
                Tween.Punch(tr, .4f, .5f);
                Tween.Inst.Tick(.1f, .1f);
                Assert.IsTrue(Squashed(tr), "the fixture needs a punch actually in flight");

                Tween.Punch(tr, .4f, .5f);
                Tween.Inst.Tick(1f, 1f);

                Assert.AreEqual(Vector3.one, tr.localScale);
            }
            finally
            {
                Drop(tr);
            }
        }

        [Test]
        public void KillingAPunchOutrightRestsRatherThanFreezingMidSquash()
        {
            // What KillChannel means now: end this, and put back what it borrowed. The two
            // screens that kill a breathe in order to punch the same control depend on it.
            var tr = Fresh();
            try
            {
                Tween.Punch(tr, .4f, .5f);
                Tween.Inst.Tick(.1f, .1f);
                Assert.IsTrue(Squashed(tr));

                Tween.KillChannel(tr, "punch");

                Assert.AreEqual(Vector3.one, tr.localScale, "a killed punch leaves no squash behind");
            }
            finally
            {
                Drop(tr);
            }
        }

        [Test]
        public void APunchStillHonoursAScaleItWasHandedAtRest()
        {
            // The rest scale is whatever the caller had set, not a hardcoded one — several
            // call sites punch a control that lives at a size of its own.
            var tr = Fresh();
            try
            {
                tr.localScale = Vector3.one * .5f;

                Tween.Punch(tr, .3f, .4f);
                Tween.Inst.Tick(.1f, .1f);
                Tween.Punch(tr, .3f, .4f);
                Tween.Inst.Tick(1f, 1f);

                Assert.AreEqual(.5f, tr.localScale.x, 1e-4f);
                Assert.AreEqual(.5f, tr.localScale.y, 1e-4f);
            }
            finally
            {
                Drop(tr);
            }
        }

        // ---------------------------------------------------------------- the other direction
        //
        // A pop does not borrow, it arrives - so its resting state is its end rather than its
        // start, and an interrupted one has to land rather than hand anything back. Same
        // defect, opposite resolution.

        [Test]
        public void AnInterruptedPopLandsRatherThanFreezingPartWayUp()
        {
            var tr = Fresh();
            try
            {
                Tween.Pop(tr, 0f, .42f);
                Tween.Inst.Tick(.1f, .1f);
                Assert.Less(tr.localScale.x, 1f, "the fixture needs a pop actually in flight");

                Tween.KillChannel(tr, "scale");

                Assert.AreEqual(Vector3.one, tr.localScale);
            }
            finally
            {
                Drop(tr);
            }
        }

        [Test]
        public void ARecycledCellArrivingMidPopIsNotStuckAtNothing()
        {
            // GridView's case, which is where this was found: a cell handed back to the pool
            // while its entrance was still inside its delay used to keep a scale of zero for
            // ever, and the type resets the scale by hand on every bind because of it.
            var tr = Fresh();
            try
            {
                Tween.Pop(tr, 0f, .42f, .3f);          // still in the delay
                Tween.Inst.Tick(.05f, .05f);
                Assert.AreEqual(Vector3.zero, tr.localScale);

                Tween.KillChannel(tr, "scale");

                Assert.AreEqual(Vector3.one, tr.localScale, "an unfinished entrance is not a hidden cell");
            }
            finally
            {
                Drop(tr);
            }
        }

        [Test]
        public void APopLandingInsideAPopStillSpringsToFullSize()
        {
            var tr = Fresh();
            try
            {
                Tween.Pop(tr, 0f, .42f);
                Tween.Inst.Tick(.1f, .1f);
                Tween.Pop(tr, 0f, .42f);
                Tween.Inst.Tick(1f, 1f);

                Assert.AreEqual(1f, tr.localScale.x, 1e-4f, "and not to a fraction of the one before");
                Assert.AreEqual(1f, tr.localScale.y, 1e-4f);
            }
            finally
            {
                Drop(tr);
            }
        }

        [Test]
        public void ATweenThatMovesAValueSomewhereNewIsNotReverted()
        {
            // The other half of the rule, and the reason this is opt-in. Scale, Move, Fade and
            // the rest are *going* somewhere; superseding one must leave it where it got to,
            // or every cross-fade in the game would snap back before its replacement started.
            var go = new GameObject("Target", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            try
            {
                Tween.Move(rt, new Vector2(100f, 0f), .4f);
                Tween.Inst.Tick(.2f, .2f);
                var midway = rt.anchoredPosition;
                Assert.AreNotEqual(Vector2.zero, midway, "the fixture needs a move actually in flight");

                Tween.KillChannel(rt, "move");

                Assert.AreEqual(midway, rt.anchoredPosition, "a superseded Move stays where it got to");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
