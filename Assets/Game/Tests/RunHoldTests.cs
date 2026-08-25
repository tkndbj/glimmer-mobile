using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The latch that decides when a run is allowed to begin.
    ///
    /// <para>
    /// It exists because the question used to be answered by a board's own <c>Locked</c> flag,
    /// which several things write — and one of them is an animation scheduled before anybody
    /// knew a lesson was going to be shown. A first-timer's tip latched the board when the
    /// screen was presented, the intro sweep unlatched it a beat later, and the countdown ran
    /// for as long as the player took to read a lesson they are only ever offered once. Nothing
    /// offline could see that: both writes are correct and only their order is wrong.
    /// </para>
    /// <para>
    /// So what is pinned here is the property that makes the shape safe rather than merely
    /// different — that no caller can free a run somebody else is still holding, in any order,
    /// however many times it says so.
    /// </para>
    /// </summary>
    public sealed class RunHoldTests
    {
        [Test]
        public void ARunIsHeldFromTheMomentItExists()
        {
            var hold = new RunHold(RunHold.Opening);

            Assert.IsTrue(hold.Held, "a screen that has not been presented is not a run in progress");
            Assert.IsTrue(hold.Holds(RunHold.Opening));
        }

        [Test]
        public void AHoldWithNoReasonsIsFree()
        {
            Assert.IsFalse(new RunHold().Held);
        }

        [Test]
        public void ReleasingTheLastReasonFreesTheRun()
        {
            var hold = new RunHold(RunHold.Opening);

            Assert.IsTrue(hold.Release(RunHold.Opening));
            Assert.IsFalse(hold.Held);
        }

        [Test]
        public void OneReasonCannotFreeARunAnotherIsStillHolding()
        {
            var hold = new RunHold(RunHold.Opening);
            hold.Take(RunHold.Teaching);

            hold.Release(RunHold.Opening);

            Assert.IsTrue(hold.Held, "a lesson is still on screen; the clock must not start");
            Assert.AreEqual(1, hold.Count);
        }

        /// <summary>
        /// The exact sequence the screen runs, and the reason the two lines are in the order
        /// they are: taking the second reason before releasing the first means the run is never
        /// free for even one frame — and on a mode whose start edge is polled, one frame of free
        /// is the edge itself.
        /// </summary>
        [Test]
        public void HandingOverFromOneReasonToAnotherNeverBlinksFree()
        {
            var hold = new RunHold(RunHold.Opening);

            hold.Take(RunHold.Teaching);
            Assert.IsTrue(hold.Held);

            hold.Release(RunHold.Opening);
            Assert.IsTrue(hold.Held);

            hold.Release(RunHold.Teaching);
            Assert.IsFalse(hold.Held, "the last lesson is closed, so the run may begin");
        }

        [Test]
        public void TakingTheSameReasonTwiceIsTakingItOnce()
        {
            var hold = new RunHold();

            hold.Take(RunHold.Teaching);
            hold.Take(RunHold.Teaching);
            hold.Release(RunHold.Teaching);

            Assert.IsFalse(hold.Held, "a counted latch would still be held here, and stuck");
        }

        [Test]
        public void ReleasingSomethingNobodyIsHoldingChangesNothing()
        {
            var hold = new RunHold(RunHold.Opening);

            Assert.IsFalse(hold.Release(RunHold.Teaching));
            Assert.IsTrue(hold.Held);
            Assert.AreEqual(1, hold.Count);
        }

        /// <summary>
        /// A panel with several exits reporting twice is this project's oldest bug, and it must
        /// cost nothing here: the second release is over a reason that is already gone.
        /// </summary>
        [Test]
        public void ADoubleReleaseCannotFreeAReasonTakenAfterwards()
        {
            var hold = new RunHold();

            hold.Take(RunHold.Teaching);
            hold.Release(RunHold.Teaching);
            hold.Release(RunHold.Teaching);

            hold.Take(RunHold.Opening);

            Assert.IsTrue(hold.Held, "the stale release must not carry over onto the next reason");
        }

        [Test]
        public void OrderOfReleaseDoesNotMatter()
        {
            var first = new RunHold(RunHold.Opening, RunHold.Teaching);
            var second = new RunHold(RunHold.Opening, RunHold.Teaching);

            first.Release(RunHold.Opening);
            first.Release(RunHold.Teaching);

            second.Release(RunHold.Teaching);
            second.Release(RunHold.Opening);

            Assert.IsFalse(first.Held);
            Assert.IsFalse(second.Held);
        }

        [Test]
        public void AnEmptyReasonIsNotAReason()
        {
            var hold = new RunHold();

            hold.Take(null);
            hold.Take(string.Empty);

            Assert.IsFalse(hold.Held, "a mistyped reason must not hold a run for ever");
        }

        [Test]
        public void WhatIsHoldingTheRunCanBeRead()
        {
            var hold = new RunHold(RunHold.Opening, RunHold.Teaching);

            StringAssert.Contains(RunHold.Opening, hold.ToString());
            StringAssert.Contains(RunHold.Teaching, hold.ToString());

            hold.ReleaseAll();
            Assert.AreEqual("free", hold.ToString());
        }
    }
}
