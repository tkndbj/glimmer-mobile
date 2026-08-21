using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// A tween dies with its owner — the one sentence <see cref="Tw"/> has always carried,
    /// and for the life of the game it was not true.
    ///
    /// <para>
    /// The guard was written as <c>owner != null &amp;&amp; owner.Equals(null)</c>: "an owner
    /// was given, and it has been destroyed". But <c>UnityEngine.Object</c> overloads
    /// <c>==</c> to answer null for a destroyed object as well as an unset one, so the first
    /// clause is false in exactly the case the second exists to detect. The condition could
    /// never be true, and every tween whose owner had been destroyed ran on to completion
    /// and called its <c>OnDone</c>.
    /// </para>
    /// <para>
    /// That is invisible in almost every case, which is why it survived: the <c>apply</c>
    /// bodies all guard their own target, so the motion simply stops. The callbacks do not.
    /// It surfaced as a NullReferenceException per token when a payout chip was dismissed
    /// with coins still in the air — <c>Payout.Land</c> landing them on a glyph that no
    /// longer existed. Same shape as the <see cref="TweenCycleTests"/> bug and the same
    /// lesson: this subsystem's failures are invisible in a screenshot, so the rule has to
    /// be a thing that can be asserted rather than a thing that was reasoned about.
    /// </para>
    /// </summary>
    public sealed class TweenOwnerTests
    {
        static Object Destroyed()
        {
            var o = ScriptableObject.CreateInstance<ScriptableObject>();
            Object.DestroyImmediate(o);
            return o;
        }

        [Test]
        public void ALiveOwnerIsNotOrphaned()
        {
            var o = ScriptableObject.CreateInstance<ScriptableObject>();
            try
            {
                Assert.IsFalse(Tween.Orphaned(o));
            }
            finally
            {
                Object.DestroyImmediate(o);
            }
        }

        [Test]
        public void NoOwnerAtAllIsNotOrphaned()
        {
            // An untethered tween is a legitimate thing to ask for — most callers here pass
            // no owner — so "nobody said" must never be read as "the owner has gone".
            Assert.IsFalse(Tween.Orphaned(null));
        }

        [Test]
        public void ADestroyedOwnerIsOrphaned()
        {
            Assert.IsTrue(Tween.Orphaned(Destroyed()));
        }

        // ------------------------------------------------------- the rule, not the predicate
        //
        // The four above prove the predicate. These four prove the wiring, and that split is
        // the point: the predicate was never what was broken. Driving Tick directly is what
        // makes the difference between "this expression is correct" and "this tween did not
        // call its OnDone", and only the second sentence is the bug.

        [Test]
        public void ALiveOwnerRunsToCompletionAndCallsBack()
        {
            var owner = ScriptableObject.CreateInstance<ScriptableObject>();
            try
            {
                bool done = false;
                float last = -1f;
                Tween.Run(.2f, Ease.Linear, t => last = t, owner).OnDone(() => done = true);

                Tween.Inst.Tick(.5f, .5f);

                Assert.IsTrue(done, "a tween whose owner is alive must finish");
                Assert.AreEqual(1f, last, 1e-4f, "and must be applied at its end phase");
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void ADestroyedOwnerNeitherAppliesNorCallsBack()
        {
            var owner = ScriptableObject.CreateInstance<ScriptableObject>();
            bool done = false, applied = false;
            Tween.Run(.2f, Ease.Linear, _ => applied = true, owner).OnDone(() => done = true);

            Object.DestroyImmediate(owner);
            Tween.Inst.Tick(.5f, .5f);

            Assert.IsFalse(applied, "a dead owner's tween must not go on animating");
            Assert.IsFalse(done, "and must not run the callback that assumes it is still there");
        }

        [Test]
        public void AnUnownedTweenStillFinishes()
        {
            // Most tweens here pass no owner at all, and several sequences depend on an
            // OnDone chained onto one of them - Overlays.Close among them. "Nobody said" must
            // never be read as "the owner has gone".
            bool done = false;
            Tween.Run(.2f, Ease.Linear, null).OnDone(() => done = true);

            Tween.Inst.Tick(.5f, .5f);

            Assert.IsTrue(done);
        }

        [Test]
        public void ATokenAimedAtADismissedChipNeverLands()
        {
            // The shape this shipped in. TokenFlight owns each token's flight by the token's
            // own image, and the chip it is aimed at belongs to the panel above it; dismissing
            // the panel destroys both, and every token still in the air used to arrive anyway
            // and call Payout.Land on a glyph that no longer existed - one NullReferenceException
            // per token in flight.
            var token = new GameObject("Tok");
            int landed = 0;
            Tween.Run(.3f, Ease.Linear, null, token).OnDone(() => landed++);

            Object.DestroyImmediate(token);
            Tween.Inst.Tick(1f, 1f);

            Assert.AreEqual(0, landed, "a token whose flight outlived its panel must not land");
        }

        [Test]
        public void TheCheckThisReplacesCouldNeverHaveFired()
        {
            // The whole bug in one assertion, and the reason the fix is not a tidying.
            // Written against the old form directly, so it goes red on any attempt to put
            // it back.
            Object owner = Destroyed();

            Assert.IsFalse(owner != null,
                           "Unity's == answers null for a destroyed object, so the old guard's " +
                           "first clause was false in exactly the case it existed to catch");
            Assert.IsFalse(owner != null && owner.Equals(null),
                           "which made the whole condition unreachable");
            Assert.IsTrue(Tween.Orphaned(owner),
                          "and the managed reference check is what makes it reachable");
        }
    }
}
