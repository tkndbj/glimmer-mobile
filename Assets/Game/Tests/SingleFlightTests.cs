using System;
using System.Threading.Tasks;
using GlimmerGrove.Cloud;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// One call in flight at a time (invariant 17b).
    ///
    /// <para>
    /// What this guards is the anonymous sign-in: two requests on the wire at once are two
    /// accounts, and the live project held seventeen such pairs on 2026-10-02. The class is
    /// pure so that can be proved here, with a counter where the provider would be, rather
    /// than argued about a DLL nothing offline can call.
    /// </para>
    /// </summary>
    public sealed class SingleFlightTests
    {
        const double Patience = 60d;

        double _now;
        int _started;
        TaskCompletionSource<bool> _wire;

        SingleFlight _flight;

        [SetUp]
        public void Open()
        {
            _now = 100d;
            _started = 0;
            _wire = null;
            _flight = new SingleFlight(Patience, () => _now);
        }

        /// <summary>A request that stays out until the test lands it.</summary>
        Task Send()
        {
            _started++;
            _wire = new TaskCompletionSource<bool>();
            return _wire.Task;
        }

        /// <summary>The fault itself: a second caller while the first request is out.</summary>
        [Test]
        public void ASecondCallerJoinsTheRequestThatIsOut()
        {
            var first = _flight.Run(Send);
            var second = _flight.Run(Send);

            Assert.AreEqual(1, _started, "two requests on the wire are two accounts");
            Assert.AreSame(first, second, "the second caller waits on the first one's answer");
            Assert.IsTrue(_flight.IsInFlight);
        }

        /// <summary>
        /// Nothing is cached but the fact that a call is out - or a device that signed out
        /// could never sign in again, and a failed attempt could never be retried.
        /// </summary>
        [Test]
        public void AFinishedRequestIsNeverHandedOutAgain()
        {
            var first = _flight.Run(Send);
            _wire.SetResult(true);

            Assert.IsFalse(_flight.IsInFlight);

            var second = _flight.Run(Send);

            Assert.AreEqual(2, _started);
            Assert.AreNotSame(first, second);
        }

        [Test]
        public void AFailedRequestIsRetriedByTheNextCaller()
        {
            var first = _flight.Run(Send);
            _wire.SetException(new InvalidOperationException("no network"));

            var second = _flight.Run(Send);

            Assert.IsTrue(first.IsFaulted);
            Assert.AreEqual(2, _started, "a failure must not be the answer for the life of the process");
            Assert.IsFalse(second.IsCompleted);
        }

        /// <summary>
        /// The SDK takes no timeout, so a request lost on a dropped connection would hold
        /// every later caller for ever. Inside the bound a caller joins; past it the request
        /// is given up on and the next caller sends another.
        /// </summary>
        [Test]
        public void ARequestThatNeverComesBackIsGivenUpOnAfterItsPatience()
        {
            _flight.Run(Send);

            _now += Patience - 1d;
            _flight.Run(Send);
            Assert.AreEqual(1, _started, "still inside the bound, so still the one request");

            _now += 1d;
            Assert.IsFalse(_flight.IsInFlight);
            _flight.Run(Send);
            Assert.AreEqual(2, _started, "a lost request must not hold the door shut for ever");
        }

        /// <summary>
        /// The patience is measured from the request that is out, not from the one before it -
        /// or a retry after a lost request would be given up on at once.
        /// </summary>
        [Test]
        public void ThePatienceStartsAgainWithEachRequest()
        {
            _flight.Run(Send);
            _now += Patience;
            _flight.Run(Send);

            _now += Patience - 1d;
            _flight.Run(Send);

            Assert.AreEqual(2, _started);
        }

        /// <summary>
        /// Every caller meets a failure the same way, at the await, whichever of them happened
        /// to start the request - so a throw before any task exists comes back as a faulted one.
        /// </summary>
        [Test]
        public void ARequestThatThrowsBeforeItStartsIsAFaultedTaskNotAnException()
        {
            Task task = null;

            Assert.DoesNotThrow(() => task = _flight.Run(() => throw new InvalidOperationException("sdk not ready")));

            Assert.IsNotNull(task);
            Assert.IsTrue(task.IsFaulted);
            Assert.IsFalse(_flight.IsInFlight, "and it holds nobody");
        }

        [Test]
        public void NothingToStartIsNothingToWaitFor()
        {
            Assert.IsTrue(_flight.Run(null).IsCompleted);
            Assert.IsTrue(_flight.Run(() => null).IsCompleted);
            Assert.IsFalse(_flight.IsInFlight);
        }
    }
}
