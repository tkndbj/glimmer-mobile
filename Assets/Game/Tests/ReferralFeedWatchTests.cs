using System;
using System.Collections.Generic;
using GlimmerGrove.Referral;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The listener's lifetime: one attached exactly while somebody is watching, the app is in
    /// the foreground and an account is signed in — and stopped exactly once, every way out.
    ///
    /// <para>
    /// <b>This is the fixture the drop exists for.</b> A stream left open is a radio kept warm
    /// for a page nobody is looking at, and one left pointed at the account a player just
    /// switched away from is worse than that. Neither is visible in a render, a compile or a
    /// content gate, and neither would ever have been executed by a test if the lifetime had
    /// stayed inline in <c>ReferralLedger</c> — it would have needed a Firestore, a signed-in
    /// account and a save file to run once. It takes a function that opens a listener, so here
    /// it takes one that counts.
    /// </para>
    /// </summary>
    public sealed class ReferralFeedWatchTests
    {
        /// <summary>A listener that records being opened and closed, and complains about a second close.</summary>
        sealed class Handle : IDisposable
        {
            public readonly string Account;
            public int Closed;

            public Handle(string account) { Account = account; }

            public void Dispose() => Closed++;
        }

        sealed class Feed
        {
            public readonly List<Handle> Opened = new List<Handle>();
            public string Account = "uid-a";
            public bool Available = true;
            public bool Refuse;

            public Action Moved;

            public ReferralFeedWatch Watch;

            public Feed()
            {
                Watch = new ReferralFeedWatch(
                    open: moved => { Moved = moved; return Open(); },
                    account: () => Account,
                    available: () => Available,
                    moved: () => Signals++);
            }

            public int Signals;

            Handle Open()
            {
                if (Refuse) return null;
                var handle = new Handle(Account);
                Opened.Add(handle);
                return handle;
            }

            public Handle Live => Opened.Count > 0 ? Opened[Opened.Count - 1] : null;

            /// <summary>
            /// Listeners this feed opened that are still running and should not be: everything
            /// unclosed, less the one that is legitimately live right now.
            ///
            /// <para>
            /// <c>Settle</c> always closes before it opens, so the live one is the last opened.
            /// Counting it as a leak is what this property got wrong first time — and a leak
            /// check that fires on the correct case is a leak check that gets deleted.
            /// </para>
            /// </summary>
            public int Leaked
            {
                get
                {
                    int open = 0;
                    for (int i = 0; i < Opened.Count; i++) if (Opened[i].Closed == 0) open++;
                    return Watch.IsWatching ? open - 1 : open;
                }
            }

            public int DoubleClosed
            {
                get
                {
                    int twice = 0;
                    for (int i = 0; i < Opened.Count; i++) if (Opened[i].Closed > 1) twice++;
                    return twice;
                }
            }
        }

        // ------------------------------------------------------------- the decision
        [Test]
        public void AListenerIsWantedOnlyWhenAllFourThingsHold()
        {
            Assert.IsTrue(ReferralFeedWatch.ShouldWatch(1, false, "uid-a", true));

            Assert.IsFalse(ReferralFeedWatch.ShouldWatch(0, false, "uid-a", true), "nobody watching");
            Assert.IsFalse(ReferralFeedWatch.ShouldWatch(1, true, "uid-a", true), "backgrounded");
            Assert.IsFalse(ReferralFeedWatch.ShouldWatch(1, false, "", true), "signed out");
            Assert.IsFalse(ReferralFeedWatch.ShouldWatch(1, false, null, true), "signed out, as null");
            Assert.IsFalse(ReferralFeedWatch.ShouldWatch(1, false, "uid-a", false), "no backend");
        }

        // ------------------------------------------------------------- holding
        [Test]
        public void OneHolderOpensOneListenerAndReleasingItClosesIt()
        {
            var feed = new Feed();

            var hold = feed.Watch.Hold();
            Assert.IsTrue(feed.Watch.IsWatching);
            Assert.AreEqual(1, feed.Opened.Count);

            hold.Dispose();
            Assert.IsFalse(feed.Watch.IsWatching);
            Assert.AreEqual(0, feed.Leaked, "nothing left running");
            Assert.AreEqual(1, feed.Opened.Count, "and nothing opened on the way out");
        }

        [Test]
        public void TwoHoldersAreStillOneListenerAndTheLastOneOutTurnsItOff()
        {
            var feed = new Feed();

            var first = feed.Watch.Hold();
            var second = feed.Watch.Hold();
            Assert.AreEqual(1, feed.Opened.Count, "two holders, one stream");

            first.Dispose();
            Assert.IsTrue(feed.Watch.IsWatching, "somebody is still looking");
            Assert.AreEqual(0, feed.Live.Closed);

            second.Dispose();
            Assert.IsFalse(feed.Watch.IsWatching);
            Assert.AreEqual(0, feed.Leaked);
        }

        [Test]
        public void ReleasingTheSameHolderTwiceIsANoOp()
        {
            // A screen being destroyed and its object being switched off both reach for this.
            // A count that went negative would keep the listener down for the rest of the
            // process, which is a bug that only shows up on the *second* visit to the page.
            var feed = new Feed();

            var hold = feed.Watch.Hold();
            hold.Dispose();
            hold.Dispose();

            Assert.AreEqual(0, feed.Watch.Watchers);
            Assert.AreEqual(0, feed.DoubleClosed, "and the stream is not stopped twice either");

            using (feed.Watch.Hold()) Assert.IsTrue(feed.Watch.IsWatching, "and it can still be taken again");
        }

        // ------------------------------------------------------------- the app going away
        [Test]
        public void BackgroundingStopsTheListenerAndResumingOpensAFreshOne()
        {
            var feed = new Feed();

            using (feed.Watch.Hold())
            {
                var first = feed.Live;

                feed.Watch.Pause();
                Assert.IsFalse(feed.Watch.IsWatching, "no stream held across a backgrounding");
                Assert.AreEqual(1, first.Closed);

                Assert.IsTrue(feed.Watch.Resume(), "and it says it re-attached, so the caller knows to ask");
                Assert.AreEqual(2, feed.Opened.Count);
                Assert.AreEqual(0, feed.Leaked);
            }

            Assert.AreEqual(0, feed.Leaked, "and both are closed by the time the page is gone");
        }

        [Test]
        public void BackgroundingWithNobodyWatchingChangesNothing()
        {
            var feed = new Feed();

            feed.Watch.Pause();
            Assert.IsFalse(feed.Watch.Resume(), "there was nothing to re-attach");
            Assert.AreEqual(0, feed.Opened.Count, "and nothing was opened to find that out");
        }

        [Test]
        public void ResumingWhileBackgroundedTwiceOverDoesNotStack()
        {
            var feed = new Feed();

            using (feed.Watch.Hold())
            {
                feed.Watch.Pause();
                feed.Watch.Pause();
                feed.Watch.Resume();
                feed.Watch.Resume();

                Assert.IsTrue(feed.Watch.IsWatching);
                Assert.AreEqual(2, feed.Opened.Count, "one close and one open, however many times it is told");
                Assert.AreEqual(0, feed.Leaked);
            }
        }

        // ------------------------------------------------------------- the account
        [Test]
        public void SwitchingAccountRepointsTheListener()
        {
            // The one that matters: a stream left open against the account a player has just
            // switched away from keeps reporting somebody else's referrals to this device.
            var feed = new Feed();

            using (feed.Watch.Hold())
            {
                var mine = feed.Live;
                Assert.AreEqual("uid-a", mine.Account);

                feed.Account = "uid-b";
                feed.Watch.Settle();

                Assert.AreEqual(1, mine.Closed, "the old one is stopped");
                Assert.AreEqual(2, feed.Opened.Count);
                Assert.AreEqual("uid-b", feed.Live.Account, "and the new one is pointed at the new account");
                Assert.AreEqual(0, feed.Leaked);
            }
        }

        [Test]
        public void SigningOutStopsTheListenerAndSigningInBringsItBack()
        {
            var feed = new Feed();

            using (feed.Watch.Hold())
            {
                var mine = feed.Live;

                feed.Account = string.Empty;
                feed.Watch.Settle();
                Assert.IsFalse(feed.Watch.IsWatching, "a stream against a signed-out uid is a refusal on a loop");
                Assert.AreEqual(1, mine.Closed);

                feed.Account = "uid-a";
                feed.Watch.Settle();
                Assert.IsTrue(feed.Watch.IsWatching);
                Assert.AreEqual(0, feed.Leaked);
            }
        }

        [Test]
        public void SettlingRepeatedlyOnTheSameAccountDoesNotChurn()
        {
            var feed = new Feed();

            using (feed.Watch.Hold())
            {
                for (int i = 0; i < 5; i++) feed.Watch.Settle();

                Assert.AreEqual(1, feed.Opened.Count, "idempotent, which is what lets every path call it");
                Assert.AreEqual(0, feed.Live.Closed);
            }
        }

        // ------------------------------------------------------------- a backend that cannot
        [Test]
        public void ABackendThatCannotWatchIsAskedAgainNextTime()
        {
            // Answering null must not be remembered as "watching this account", or the listener
            // could never be attached again for the rest of the session — which is exactly the
            // state a device that opened the page while signed out would be stuck in.
            var feed = new Feed { Refuse = true };

            using (feed.Watch.Hold())
            {
                Assert.IsFalse(feed.Watch.IsWatching);

                feed.Refuse = false;
                feed.Watch.Settle();
                Assert.IsTrue(feed.Watch.IsWatching, "asked again rather than written off");
            }

            Assert.AreEqual(0, feed.Leaked);
        }

        // ------------------------------------------------------------- the teardown
        [Test]
        public void ResetLeavesNothingRunning()
        {
            var feed = new Feed();

            feed.Watch.Hold();
            feed.Watch.Hold();
            feed.Watch.Reset();

            Assert.IsFalse(feed.Watch.IsWatching);
            Assert.AreEqual(0, feed.Watch.Watchers);
            Assert.AreEqual(0, feed.Leaked, "however many holders there were");
        }

        [Test]
        public void AListenerIsNeverStoppedTwiceByAnyRoute()
        {
            var feed = new Feed();

            var hold = feed.Watch.Hold();
            feed.Watch.Pause();          // closes it
            hold.Dispose();              // the holder goes after it is already down
            feed.Watch.Resume();         // and nothing is standing to re-open
            feed.Watch.Reset();

            Assert.AreEqual(0, feed.DoubleClosed);
            Assert.AreEqual(0, feed.Leaked);
            Assert.IsFalse(feed.Watch.IsWatching);
        }

        // ------------------------------------------------------------- the signal
        [Test]
        public void TheFeedMovingRaisesTheSignalItWasGiven()
        {
            var feed = new Feed();

            using (feed.Watch.Hold())
            {
                Assert.IsNotNull(feed.Moved, "the listener was handed something to call");
                feed.Moved();
                feed.Moved();
                Assert.AreEqual(2, feed.Signals);
            }
        }
    }
}
