using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// When this device asks the server to rebuild its public card.
    ///
    /// <para>
    /// Run offline, and that is the point of the type being what it is. A publish policy's
    /// failures are all invisible in a screenshot: republishing on every sync costs a function
    /// invocation per player per sync for ever and looks identical to not doing so; dropping a
    /// change made while a call was in flight loses a purchase from the board and looks
    /// identical to a slow network. So it holds no clock, no socket and no Unity types, and it
    /// is proved a thousand simulated seconds at a time — <c>SyncScheduler</c>'s bargain, and
    /// <c>TweenCycle</c>'s reason.
    /// </para>
    /// </summary>
    public sealed class GrovePublishPolicyTests
    {
        const float Long = GrovePublishPolicy.MaxRetrySeconds * 4f;

        static GrovePublishAction Settle(GrovePublishPolicy policy, float seconds = Long)
            => policy.Tick(seconds);

        [Test]
        public void AFreshPolicyOwesNothing()
        {
            var policy = new GrovePublishPolicy();

            Assert.IsFalse(policy.HasWork);
            Assert.AreEqual(GrovePublishAction.None, Settle(policy));
        }

        [Test]
        public void AChangeIsPublishedAfterTheDebounce()
        {
            var policy = new GrovePublishPolicy();
            policy.Request("a", true);

            Assert.IsTrue(policy.HasWork);

            // Not before the debounce has run out: a shopping trip is five purchases and a
            // rearrangement, and each one arriving as its own write is what this exists to stop.
            Assert.AreEqual(GrovePublishAction.None,
                            policy.Tick(GrovePublishPolicy.DebounceSeconds * .5f));

            Assert.AreEqual(GrovePublishAction.Publish,
                            policy.Tick(GrovePublishPolicy.DebounceSeconds));
        }

        [Test]
        public void AGroveWorthNothingIsNeverPublished()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", worthPublishing: false);

            Assert.IsFalse(policy.HasWork);
            Assert.AreEqual(GrovePublishAction.None, Settle(policy));
        }

        [Test]
        public void ABurstOfChangesBecomesOnePublish()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", true);
            policy.Tick(GrovePublishPolicy.DebounceSeconds * .4f);
            policy.Request("b", true);
            policy.Tick(GrovePublishPolicy.DebounceSeconds * .4f);
            policy.Request("c", true);

            // The debounce restarts on each request, so nothing has gone yet.
            Assert.AreEqual(GrovePublishAction.None,
                            policy.Tick(GrovePublishPolicy.DebounceSeconds * .4f));

            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));
            Assert.AreEqual("c", policy.WantedFingerprint);

            policy.Succeeded("c");
            Assert.IsFalse(policy.HasWork);
        }

        [Test]
        public void RepublishingWhatIsAlreadyOnTheBoardIsDropped()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", true);
            Settle(policy);
            policy.Succeeded("a");

            // This is the case that makes the whole thing cheap: a sync raised by a star, a
            // heart or a chest reaches the ledgers' events and changes nothing a visitor can
            // see, so the fingerprint is identical and no call is made.
            policy.Request("a", true);

            Assert.IsFalse(policy.HasWork);
            Assert.AreEqual(GrovePublishAction.None, Settle(policy));
        }

        [Test]
        public void AChangeMadeWhileAPublishIsInFlightSurvivesIt()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", true);
            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));

            // The player buys something while the call is out.
            policy.Request("b", true);

            // The reply is for the *old* fingerprint, and saying so is what stops the new work
            // being cleared by the success of a call that never carried it — SyncScheduler's
            // rule, and the reason a rename used to be lost to one unlucky second.
            policy.Succeeded("a");

            Assert.IsTrue(policy.HasWork);
            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));
            Assert.AreEqual("b", policy.WantedFingerprint);
        }

        [Test]
        public void AFailureBacksOffAndKeepsTheWork()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", true);
            Settle(policy);
            policy.Failed();

            Assert.IsTrue(policy.HasWork);

            // Not immediately, and not after the debounce either: the first retry is further
            // away than that.
            Assert.AreEqual(GrovePublishAction.None,
                            policy.Tick(GrovePublishPolicy.FirstRetrySeconds * .5f));

            Assert.AreEqual(GrovePublishAction.Publish,
                            policy.Tick(GrovePublishPolicy.FirstRetrySeconds));
        }

        [Test]
        public void TheBackoffDoublesAndThenStops()
        {
            var policy = new GrovePublishPolicy();
            policy.Request("a", true);

            float last = 0f;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                Assert.AreEqual(GrovePublishAction.Publish, Settle(policy),
                                $"attempt {attempt} never fired");
                policy.Failed();

                float wait = policy.SecondsUntilAttempt;

                Assert.LessOrEqual(wait, GrovePublishPolicy.MaxRetrySeconds,
                                   "the backoff grew past its ceiling");
                Assert.GreaterOrEqual(wait, last > 0f ? last : 0f,
                                      "the backoff shrank without anything having changed");
                last = wait;
            }

            Assert.AreEqual(GrovePublishPolicy.MaxRetrySeconds, last);
        }

        [Test]
        public void APermanentRefusalIsDroppedRatherThanRetriedForEver()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", true);
            Settle(policy);

            // Invariant 13a: a client that keeps resubmitting a refusal that will still be
            // true tomorrow is a loop for the life of the account.
            policy.Refused();

            Assert.IsFalse(policy.HasWork);
            Assert.AreEqual(GrovePublishAction.None, Settle(policy));

            // And the next real change still asks, because dropping is not disabling.
            policy.Request("b", true);
            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));
        }

        [Test]
        public void TheTimerDoesNotRunWhileTheNetworkIsDown()
        {
            var policy = new GrovePublishPolicy();

            policy.NetworkChanged(false);
            policy.Request("a", true);

            Assert.AreEqual(GrovePublishAction.None, Settle(policy));

            // Coming back does not fire on the same frame — reachability flips somewhat before
            // an interface carries traffic, and attempting then buys one guaranteed failure.
            policy.NetworkChanged(true);
            Assert.AreEqual(GrovePublishAction.None, policy.Tick(0f));
            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));
        }

        [Test]
        public void RegainingTheNetworkClearsABackoff()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", true);
            Settle(policy);
            policy.Failed();
            policy.Failed();

            policy.NetworkChanged(false);
            policy.NetworkChanged(true);

            // A backoff exists because the server or the network was failing; the network
            // coming back is genuinely new information about one of those.
            Assert.AreEqual(GrovePublishAction.Publish,
                            policy.Tick(GrovePublishPolicy.DebounceSeconds));
        }

        [Test]
        public void RegainingTheNetworkWithNothingOwedAsksForNothing()
        {
            var policy = new GrovePublishPolicy();

            policy.NetworkChanged(false);
            policy.NetworkChanged(true);

            Assert.IsFalse(policy.HasWork);
            Assert.AreEqual(GrovePublishAction.None, Settle(policy));
        }

        // ------------------------------------------------------------ withdrawal
        [Test]
        public void OptingOutTakesTheCardDown()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", true);
            Settle(policy);
            policy.Succeeded("a");

            policy.RequestWithdrawal();

            Assert.AreEqual(GrovePublishAction.Withdraw, Settle(policy));
        }

        [Test]
        public void AWithdrawalOutranksAPendingPublish()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", true);
            policy.RequestWithdrawal();

            // Publishing and then deleting would leave a device that died between the two
            // having published something somebody asked it not to.
            Assert.AreEqual(GrovePublishAction.Withdraw, Settle(policy));
        }

        [Test]
        public void AWithdrawalIsOwedEvenWhenThisDeviceNeverPublished()
        {
            // Another device may have. This one cannot know, so it asks.
            var policy = new GrovePublishPolicy();
            policy.RequestWithdrawal();

            Assert.IsTrue(policy.HasWork);
            Assert.AreEqual(GrovePublishAction.Withdraw, Settle(policy));
        }

        [Test]
        public void AfterAWithdrawalTheNextPublishIsAskedForAfresh()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", true);
            Settle(policy);
            policy.Succeeded("a");

            policy.RequestWithdrawal();
            Settle(policy);
            policy.Succeeded(string.Empty);

            Assert.IsEmpty(policy.PublishedFingerprint);

            // The identical grove has to go back up: the card was deleted, so "already
            // published" is no longer true of anything.
            policy.Request("a", true);
            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));
        }

        // -------------------------------------------------------------- accounts
        [Test]
        public void SwitchingAccountForgetsWhatWasPublished()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", true);
            Settle(policy);
            policy.Succeeded("a");

            policy.Forget();

            Assert.IsFalse(policy.HasWork);
            Assert.IsEmpty(policy.PublishedFingerprint);

            // Invariant 17's discipline: the fingerprint described the *outgoing* account's
            // card. Kept, an incoming player whose grove happened to look the same would be
            // suppressed for ever and never reach the board at all.
            policy.Request("a", true);
            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));
        }

        // ----------------------------------------------------------- remembering
        [Test]
        public void AGroveUnchangedSinceTheLastLaunchPublishesNothing()
        {
            var policy = new GrovePublishPolicy();

            // What the device noted last time it ran.
            policy.Adopt("a");

            policy.Request("a", true);

            Assert.IsFalse(policy.HasWork,
                           "a relaunch with an unchanged grove asked for a write");
            Assert.AreEqual(GrovePublishAction.None, Settle(policy));
        }

        [Test]
        public void AGroveChangedSinceTheLastLaunchStillPublishes()
        {
            var policy = new GrovePublishPolicy();

            policy.Adopt("a");
            policy.Request("b", true);

            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));
        }

        [Test]
        public void ARememberedFingerprintNeverOverridesWorkAlreadyOwed()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", true);

            // Adopting here would mark as published something that has not been sent, and the
            // request would then be dropped for ever. The note is only ever taken by a policy
            // with nothing to do.
            policy.Adopt("a");

            Assert.IsTrue(policy.HasWork);
            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));
        }

        [Test]
        public void APublishIsNeverStartedTwice()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", true);
            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));

            // Still in flight. A second start would be a second call, and a refusal recorded
            // as a failure would back the timer off for a reason that was never real.
            Assert.AreEqual(GrovePublishAction.None, Settle(policy));
            Assert.AreEqual(GrovePublishAction.None, Settle(policy));
        }
    }
}
