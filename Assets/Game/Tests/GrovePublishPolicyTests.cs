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
            policy.Request("a", 1L, true);

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

            policy.Request("a", 1L, worthPublishing: false);

            Assert.IsFalse(policy.HasWork);
            Assert.AreEqual(GrovePublishAction.None, Settle(policy));
        }

        [Test]
        public void ABurstOfChangesBecomesOnePublish()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", 1L, true);
            policy.Tick(GrovePublishPolicy.DebounceSeconds * .4f);
            policy.Request("b", 1L, true);
            policy.Tick(GrovePublishPolicy.DebounceSeconds * .4f);
            policy.Request("c", 1L, true);

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

            policy.Request("a", 1L, true);
            Settle(policy);
            policy.Succeeded("a");

            // This is the case that makes the whole thing cheap: a sync raised by a star, a
            // heart or a chest reaches the ledgers' events and changes nothing a visitor can
            // see, so the fingerprint is identical and no call is made.
            policy.Request("a", 1L, true);

            Assert.IsFalse(policy.HasWork);
            Assert.AreEqual(GrovePublishAction.None, Settle(policy));
        }

        [Test]
        public void AChangeMadeWhileAPublishIsInFlightSurvivesIt()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", 1L, true);
            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));

            // The player buys something while the call is out.
            policy.Request("b", 1L, true);

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

            policy.Request("a", 1L, true);
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
            policy.Request("a", 1L, true);

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

            policy.Request("a", 1L, true);
            Settle(policy);

            // Invariant 13a: a client that keeps resubmitting a refusal that will still be
            // true tomorrow is a loop for the life of the account.
            policy.Refused();

            Assert.IsFalse(policy.HasWork);
            Assert.AreEqual(GrovePublishAction.None, Settle(policy));

            // And the next real change still asks, because dropping is not disabling.
            policy.Request("b", 1L, true);
            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));
        }

        [Test]
        public void TheTimerDoesNotRunWhileTheNetworkIsDown()
        {
            var policy = new GrovePublishPolicy();

            policy.NetworkChanged(false);
            policy.Request("a", 1L, true);

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

            policy.Request("a", 1L, true);
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

            policy.Request("a", 1L, true);
            Settle(policy);
            policy.Succeeded("a");

            policy.RequestWithdrawal();

            Assert.AreEqual(GrovePublishAction.Withdraw, Settle(policy));
        }

        [Test]
        public void AWithdrawalOutranksAPendingPublish()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", 1L, true);
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

            policy.Request("a", 1L, true);
            Settle(policy);
            policy.Succeeded("a");

            policy.RequestWithdrawal();
            Settle(policy);
            policy.Succeeded(string.Empty);

            Assert.IsEmpty(policy.PublishedFingerprint);

            // The identical grove has to go back up: the card was deleted, so "already
            // published" is no longer true of anything.
            policy.Request("a", 1L, true);
            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));
        }

        // -------------------------------------------------------------- accounts
        [Test]
        public void SwitchingAccountForgetsWhatWasPublished()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", 1L, true);
            Settle(policy);
            policy.Succeeded("a");

            policy.Forget();

            Assert.IsFalse(policy.HasWork);
            Assert.IsEmpty(policy.PublishedFingerprint);

            // Invariant 17's discipline: the fingerprint described the *outgoing* account's
            // card. Kept, an incoming player whose grove happened to look the same would be
            // suppressed for ever and never reach the board at all.
            policy.Request("a", 1L, true);
            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));
        }

        // ----------------------------------------------------------- remembering
        [Test]
        public void AGroveUnchangedSinceTheLastLaunchPublishesNothing()
        {
            var policy = new GrovePublishPolicy();

            // What the device noted last time it ran.
            policy.Adopt("a");

            policy.Request("a", 1L, true);

            Assert.IsFalse(policy.HasWork,
                           "a relaunch with an unchanged grove asked for a write");
            Assert.AreEqual(GrovePublishAction.None, Settle(policy));
        }

        [Test]
        public void AGroveChangedSinceTheLastLaunchStillPublishes()
        {
            var policy = new GrovePublishPolicy();

            policy.Adopt("a");
            policy.Request("b", 1L, true);

            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));
        }

        [Test]
        public void ARememberedFingerprintNeverOverridesWorkAlreadyOwed()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", 1L, true);

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

            policy.Request("a", 1L, true);
            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));

            // Still in flight. A second start would be a second call, and a refusal recorded
            // as a failure would back the timer off for a reason that was never real.
            Assert.AreEqual(GrovePublishAction.None, Settle(policy));
            Assert.AreEqual(GrovePublishAction.None, Settle(policy));
        }

        // ============================================================= the proof
        [Test]
        public void ARequestCarriesTheRevisionThePublishHasToProve()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", 41L, true);
            Assert.AreEqual(41L, policy.WantedRevision);

            Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));

            // Read beside the fingerprint, for the same reason: a request arriving a moment
            // later moves the wanted pair, and the reply must be held to what was sent.
            Assert.AreEqual("a", policy.InFlightFingerprint);
            Assert.AreEqual(41L, policy.InFlightRevision);

            policy.Request("b", 42L, true);
            Assert.AreEqual(41L, policy.InFlightRevision);
            Assert.AreEqual(42L, policy.WantedRevision);
        }

        [Test]
        public void AStaleReplyKeepsTheWorkAndBacksOff()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", 41L, true);
            Settle(policy);

            // The server built the card from an older save. Worth trying again — once the
            // caller has pushed again — and never worth recording as published.
            Assert.IsTrue(policy.Stale());
            Assert.IsTrue(policy.HasWork);
            Assert.AreEqual(string.Empty, policy.PublishedFingerprint);
            Assert.AreEqual("a", policy.WantedFingerprint);
            Assert.AreEqual(41L, policy.WantedRevision);

            Assert.AreEqual(GrovePublishAction.None,
                            policy.Tick(GrovePublishPolicy.FirstRetrySeconds * .5f));
            Assert.AreEqual(GrovePublishAction.Publish,
                            policy.Tick(GrovePublishPolicy.FirstRetrySeconds));
        }

        [Test]
        public void AStaleReplyIsAcceptedOnceTheRetriesAreSpent()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", 41L, true);
            Settle(policy);

            for (int i = 0; i < GrovePublishPolicy.MaxStaleRetries; i++)
            {
                Assert.IsTrue(policy.Stale(), $"retry {i + 1} should still be owed");
                Assert.AreEqual(GrovePublishAction.Publish, Settle(policy));
            }

            // A refusal that will still be true tomorrow must not be retried for ever
            // (invariant 13a). Taken as the publish: nothing owed, fingerprint recorded, so
            // the cost is one stale card until the next real change asks afresh.
            Assert.IsFalse(policy.Stale());
            Assert.IsFalse(policy.HasWork);
            Assert.AreEqual("a", policy.PublishedFingerprint);
            Assert.AreEqual(GrovePublishAction.None, Settle(policy));
        }

        [Test]
        public void ASuccessClearsTheStaleCount()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", 41L, true);
            Settle(policy);
            Assert.IsTrue(policy.Stale());
            Settle(policy);
            policy.Succeeded("a");

            // A fresh change starts a fresh count; one earlier stale reply does not bring
            // the next one closer to being accepted unproven.
            policy.Request("b", 42L, true);
            Settle(policy);
            for (int i = 0; i < GrovePublishPolicy.MaxStaleRetries; i++)
            {
                Assert.IsTrue(policy.Stale());
                Settle(policy);
            }
            Assert.IsFalse(policy.Stale());
        }

        [Test]
        public void AStaleReplyDoesNotResurrectAWithdrawalOrEatAnOptIn()
        {
            var policy = new GrovePublishPolicy();

            policy.Request("a", 41L, true);
            Settle(policy);

            // The player opts out while the call is out. The stale verdict on the old call
            // must leave the withdrawal standing rather than put the publish back.
            policy.RequestWithdrawal();
            Assert.IsTrue(policy.Stale());

            Assert.AreEqual(GrovePublishAction.Withdraw, Settle(policy));
        }

        [Test]
        public void StaleIsMeaninglessOutsideAPublish()
        {
            var policy = new GrovePublishPolicy();

            // Nothing in flight, and a withdrawal in flight: neither can be stale, and
            // saying so must change nothing.
            Assert.IsFalse(policy.Stale());
            Assert.IsFalse(policy.HasWork);

            policy.RequestWithdrawal();
            Settle(policy);
            Assert.IsFalse(policy.Stale());
            policy.Succeeded(string.Empty);
            Assert.IsFalse(policy.HasWork);
        }
    }
}
