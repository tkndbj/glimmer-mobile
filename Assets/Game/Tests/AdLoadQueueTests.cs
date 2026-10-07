using GlimmerGrove.Ads;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Rewarded ad units load one at a time, in order, and a load the network never answers
    /// does not hold the rest of them for the session.
    ///
    /// <para>
    /// The fault this pins: six placements loaded in the same frame the mediation SDK started,
    /// and the hub lurched on every connected launch while six creatives were pre-rendered at
    /// once. The provider cannot be driven offline, so the ordering lives in
    /// <see cref="AdLoadQueue"/> and is proved here.
    /// </para>
    /// </summary>
    public sealed class AdLoadQueueTests
    {
        const double Stall = 30;

        static AdLoadQueue SixRequested()
        {
            var queue = new AdLoadQueue(Stall);
            for (int i = 1; i <= 6; i++) queue.Request("p" + i);
            return queue;
        }

        [Test]
        public void SixRequestsStartOneLoadAndHoldTheRest()
        {
            var queue = SixRequested();

            Assert.IsTrue(queue.TryStart(0, out var first));
            Assert.AreEqual("p1", first);
            Assert.AreEqual("p1", queue.InFlight);
            Assert.AreEqual(5, queue.Waiting);

            Assert.IsFalse(queue.TryStart(0, out var second), "one in flight at a time");
            Assert.IsNull(second);
        }

        [Test]
        public void SettlingTheLoadInFlightStartsTheNextInOrder()
        {
            var queue = SixRequested();
            queue.TryStart(0, out _);

            Assert.IsTrue(queue.Settle("p1"));
            Assert.IsNull(queue.InFlight);

            Assert.IsTrue(queue.TryStart(1, out var next));
            Assert.AreEqual("p2", next);
            Assert.AreEqual(4, queue.Waiting);
        }

        [Test]
        public void TheWholeQueueDrainsInTheOrderItWasAsked()
        {
            var queue = SixRequested();
            var order = new System.Collections.Generic.List<string>();

            while (queue.TryStart(order.Count, out var id))
            {
                order.Add(id);
                queue.Settle(id);
            }

            CollectionAssert.AreEqual(new[] { "p1", "p2", "p3", "p4", "p5", "p6" }, order);
            Assert.AreEqual(0, queue.Waiting);
        }

        [Test]
        public void APlacementAlreadyWaitingOrInFlightIsNotQueuedTwice()
        {
            var queue = new AdLoadQueue(Stall);

            Assert.IsTrue(queue.Request("a"));
            Assert.IsFalse(queue.Request("a"), "already waiting");
            Assert.AreEqual(1, queue.Waiting);

            queue.TryStart(0, out _);
            Assert.IsFalse(queue.Request("a"), "already in flight; the load will answer for itself");
            Assert.AreEqual(0, queue.Waiting);

            queue.Settle("a");
            Assert.IsTrue(queue.Request("a"), "settled, so it may be asked for again");
        }

        [Test]
        public void AnEmptyIdIsRefused()
        {
            var queue = new AdLoadQueue(Stall);

            Assert.IsFalse(queue.Request(null));
            Assert.IsFalse(queue.Request(string.Empty));
            Assert.AreEqual(0, queue.Waiting);
        }

        [Test]
        public void ACallbackForSomethingNotInFlightChangesNothing()
        {
            var queue = SixRequested();
            queue.TryStart(0, out _);

            Assert.IsFalse(queue.Settle("p4"), "p4 is waiting, not in flight");
            Assert.IsFalse(queue.Settle("stranger"));
            Assert.IsFalse(queue.Settle(null));
            Assert.AreEqual("p1", queue.InFlight);
            Assert.AreEqual(5, queue.Waiting);
        }

        [Test]
        public void AStalledLoadIsLetGoOfAndTheNextStarts()
        {
            var queue = SixRequested();
            queue.TryStart(100, out _);

            Assert.IsNull(queue.Expire(100 + Stall - 0.001), "not yet");
            Assert.AreEqual("p1", queue.InFlight);

            Assert.AreEqual("p1", queue.Expire(100 + Stall));
            Assert.IsNull(queue.InFlight);
            Assert.AreEqual(5, queue.Waiting, "a stalled load is not re-queued; the backoff re-asks");

            Assert.IsTrue(queue.TryStart(100 + Stall, out var next));
            Assert.AreEqual("p2", next);
        }

        [Test]
        public void ALoadRestartedAfterSettlingIsJudgedFromItsOwnStart()
        {
            var queue = new AdLoadQueue(Stall);
            queue.Request("a");
            queue.TryStart(0, out _);
            queue.Settle("a");

            queue.Request("a");
            queue.TryStart(Stall - 1, out _);

            Assert.IsNull(queue.Expire(Stall), "the second load is one second old, not thirty");
            Assert.AreEqual("a", queue.Expire(2 * Stall - 1));
        }

        [Test]
        public void NothingExpiresWhenNothingIsInFlight()
        {
            var queue = SixRequested();

            Assert.IsNull(queue.Expire(1_000_000));
            Assert.AreEqual(6, queue.Waiting);
        }
    }
}
