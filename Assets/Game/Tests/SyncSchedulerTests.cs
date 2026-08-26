using GlimmerGrove.Cloud;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// When a sync runs.
    ///
    /// <para>
    /// Worth a fixture of its own because the failures it exists to prevent are all
    /// invisible in the Editor, where the network never drops and the process is never
    /// frozen. A sync fired as the app is backgrounded may simply never resume; one that
    /// failed on a train used to be forgotten entirely. Both were reported as "it does
    /// not save", and neither could be reproduced by anyone holding a laptop.
    /// </para>
    /// <para>
    /// The policy holds no clock and no socket precisely so this file can exist — it is
    /// handed elapsed time and told whether the network is up, which is the same bargain
    /// <c>RunScreen.Tick</c> makes.
    /// </para>
    /// </summary>
    public sealed class SyncSchedulerTests
    {
        /// <summary>Runs the clock forward in small steps, and counts the syncs asked for.</summary>
        static int Advance(SyncScheduler schedule, float seconds, float step = 0.25f)
        {
            int fired = 0;
            for (float elapsed = 0f; elapsed < seconds; elapsed += step)
                if (schedule.Tick(step)) fired++;

            return fired;
        }

        [Test]
        public void NothingRunsUntilSomethingAsks()
        {
            var schedule = new SyncScheduler();

            Assert.AreEqual(0, Advance(schedule, 600f, 5f),
                            "an idle game must not write to the server on a timer");
        }

        [Test]
        public void ARequestWaitsOutTheDebounceAndThenRunsOnce()
        {
            var schedule = new SyncScheduler();
            schedule.Request();

            Assert.AreEqual(0, Advance(schedule, SyncScheduler.DebounceSeconds - 0.5f),
                            "a change is not sent the instant it is made");

            Assert.IsTrue(schedule.HasWork);
            Assert.AreEqual(1, Advance(schedule, 2f), "and then it is sent exactly once");

            schedule.Succeeded();
            Assert.AreEqual(0, Advance(schedule, 600f, 5f), "with nothing owed afterwards");
            Assert.IsFalse(schedule.HasWork);
        }

        [Test]
        public void ABurstOfChangesIsOneSync()
        {
            var schedule = new SyncScheduler();

            // A rename, then a companion, then a rename again — three taps in a panel.
            for (int i = 0; i < 3; i++)
            {
                schedule.Request();
                Advance(schedule, 1f);
            }

            Assert.AreEqual(1, Advance(schedule, SyncScheduler.DebounceSeconds + 1f),
                            "Firestore bills per write, so a burst has to coalesce");
        }

        [Test]
        public void AFailureIsRetriedFurtherAndFurtherOut()
        {
            var schedule = new SyncScheduler();
            schedule.Request();

            Assert.AreEqual(1, Advance(schedule, SyncScheduler.DebounceSeconds + 1f));
            schedule.Failed();

            // Still owed, and not attempted again immediately.
            Assert.IsTrue(schedule.HasWork);
            Assert.AreEqual(0, Advance(schedule, SyncScheduler.FirstRetrySeconds - 1f));
            Assert.AreEqual(1, Advance(schedule, 2f));

            schedule.Failed();
            Assert.AreEqual(0, Advance(schedule, SyncScheduler.FirstRetrySeconds + 1f),
                            "the second wait is longer than the first");
            Assert.AreEqual(1, Advance(schedule, SyncScheduler.FirstRetrySeconds + 1f));
        }

        [Test]
        public void TheBackoffIsBoundedAndSucceedingClearsIt()
        {
            var schedule = new SyncScheduler();
            schedule.Request();

            for (int attempt = 0; attempt < 12; attempt++)
            {
                Advance(schedule, SyncScheduler.MaxRetrySeconds + 5f, 1f);
                schedule.Failed();
            }

            // Bounded: however long it has been failing, one more window is enough.
            Assert.AreEqual(1, Advance(schedule, SyncScheduler.MaxRetrySeconds + 5f, 1f),
                            "a device must not back off into never trying again");

            schedule.Succeeded();
            schedule.Request();

            Assert.AreEqual(1, Advance(schedule, SyncScheduler.DebounceSeconds + 1f),
                            "a success clears the backoff — the next change is prompt again");
        }

        [Test]
        public void NothingIsAttemptedWhileThereIsNoNetwork()
        {
            var schedule = new SyncScheduler();
            schedule.NetworkChanged(false);
            schedule.Request();

            Assert.AreEqual(0, Advance(schedule, 600f, 5f),
                            "an attempt with no radio is a guaranteed failure and a backoff");
            Assert.IsTrue(schedule.HasWork, "and the change is still owed");
        }

        /// <summary>
        /// The case the owner asked about: rename while offline, then come back online.
        ///
        /// The debounce does not drain in the tunnel, so the first attempt happens a
        /// moment <em>after</em> the signal returns rather than on the frame the interface
        /// comes up — which is the one attempt certain to fail.
        /// </summary>
        [Test]
        public void ComingBackOnlineSendsWhatWasWaiting()
        {
            var schedule = new SyncScheduler();
            schedule.NetworkChanged(false);
            schedule.Request();
            Advance(schedule, 600f, 5f);

            schedule.NetworkChanged(true);

            Assert.AreEqual(0, Advance(schedule, SyncScheduler.ReconnectSeconds - 0.5f),
                            "reachability flips before the link carries traffic");
            Assert.AreEqual(1, Advance(schedule, 2f));
        }

        /// <summary>
        /// Coming back online is worth a sync even with nothing local to send: the point
        /// of reconnecting is as much what the player's other device did while this one
        /// was away. It costs a read, and no write at all when the two already agree.
        /// </summary>
        [Test]
        public void ComingBackOnlineSyncsEvenWithNothingPending()
        {
            var schedule = new SyncScheduler();
            schedule.NetworkChanged(false);
            Advance(schedule, 60f, 5f);

            schedule.NetworkChanged(true);
            Assert.AreEqual(1, Advance(schedule, SyncScheduler.ReconnectSeconds + 1f));
        }

        [Test]
        public void ANetworkThatStaysUpChangesNothing()
        {
            var schedule = new SyncScheduler();

            for (int i = 0; i < 100; i++) schedule.NetworkChanged(true);

            Assert.AreEqual(0, Advance(schedule, 600f, 5f),
                            "the poll runs every frame, so only the transition may act");
        }

        /// <summary>
        /// The one-second race that would otherwise lose a rename: the player renames
        /// while a push is already in flight, so the snapshot being pushed cannot contain
        /// it. The request has to survive that push succeeding.
        /// </summary>
        [Test]
        public void AChangeMadeDuringASyncSurvivesItSucceeding()
        {
            var schedule = new SyncScheduler();
            schedule.Request();
            Assert.AreEqual(1, Advance(schedule, SyncScheduler.DebounceSeconds + 1f));

            // ... the push is in flight, and the player renames themselves.
            schedule.Request();
            schedule.Succeeded();

            Assert.IsTrue(schedule.HasWork, "the sync that just finished never carried it");
            Assert.AreEqual(1, Advance(schedule, SyncScheduler.DebounceSeconds + 1f));
        }

        [Test]
        public void OnlyOneSyncIsAskedForAtATime()
        {
            var schedule = new SyncScheduler();
            schedule.Request();

            Assert.AreEqual(1, Advance(schedule, SyncScheduler.DebounceSeconds + 1f));
            Assert.AreEqual(0, Advance(schedule, 600f, 5f),
                            "a slow sync must not be started a second time behind itself");
        }

        [Test]
        public void ForegroundingClearsABackoffWithoutAskingForASync()
        {
            var schedule = new SyncScheduler();
            schedule.Request();
            Advance(schedule, SyncScheduler.DebounceSeconds + 1f);

            for (int attempt = 0; attempt < 6; attempt++)
            {
                schedule.Failed();
                Advance(schedule, SyncScheduler.MaxRetrySeconds + 5f, 1f);
            }

            schedule.Succeeded();
            schedule.Settled();

            Assert.AreEqual(0, Advance(schedule, 600f, 5f), "settling asks for nothing by itself");

            schedule.Request();
            Assert.AreEqual(1, Advance(schedule, SyncScheduler.DebounceSeconds + 1f),
                            "and the next change is prompt rather than an hour away");
        }
    }
}
