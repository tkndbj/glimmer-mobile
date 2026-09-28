using System.Collections.Generic;
using GlimmerGrove.Ads;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// A rewarded video's coins reaching the screen without a restart: the wallet is watched
    /// from the view until the server's grant lands, and never longer.
    ///
    /// <para>
    /// The fault this answers was invisible to every gate: the sync the ad started read the
    /// balance a few seconds before LevelPlay's callback paid it, so the coins sat on the server
    /// until the next sync. These drive <see cref="AdGrantWatch"/> the way
    /// <c>CloudSaveService.PumpAdGrant</c> does, with a dictionary standing in for the ledgers.
    /// </para>
    /// </summary>
    public sealed class AdGrantWatchTests
    {
        const string Uid = "uid-a";
        const string Credits = "credits";

        readonly Dictionary<string, long> _granted = new Dictionary<string, long>();

        long Granted(string currency) => _granted.TryGetValue(currency, out var v) ? v : 0;

        AdGrantWatch.Step Next(AdGrantWatch watch, double now, string uid = Uid)
            => watch.Next(now, uid, Granted);

        [SetUp]
        public void Reset() => _granted.Clear();

        [Test]
        public void AnUnarmedWatchDoesNothing()
        {
            var watch = new AdGrantWatch();
            Assert.AreEqual(AdGrantWatch.Step.None, Next(watch, 0));
            Assert.IsFalse(watch.Armed);
        }

        [Test]
        public void AViewAttachesAListenerAndAsksOnceStraightAway()
        {
            var watch = new AdGrantWatch();
            _granted[Credits] = 1000;
            watch.Expect(Credits, 1000, Uid, 0);

            Assert.AreEqual(AdGrantWatch.Step.Attach, Next(watch, 0));
            watch.Attached(true);

            // The callback may have landed before the listener was attached.
            Assert.AreEqual(AdGrantWatch.Step.Refresh, Next(watch, 0.1));
            watch.Refreshed();

            Assert.AreEqual(AdGrantWatch.Step.None, Next(watch, 0.2), "no change, no read");
        }

        [Test]
        public void EachChangeToTheWalletIsOneReadAndTheGrantDetaches()
        {
            var watch = new AdGrantWatch();
            _granted[Credits] = 1000;
            watch.Expect(Credits, 1000, Uid, 0);
            Next(watch, 0); watch.Attached(true);
            Next(watch, 0.1); watch.Refreshed();

            watch.Signal();
            Assert.AreEqual(AdGrantWatch.Step.Refresh, Next(watch, 3));

            // The read is still out: a second signal waits for it rather than stacking a read.
            watch.Signal();
            Assert.AreEqual(AdGrantWatch.Step.None, Next(watch, 3.1));

            _granted[Credits] = 1300;
            watch.Refreshed();

            Assert.AreEqual(AdGrantWatch.Step.Detach, Next(watch, 3.2));
            Assert.IsFalse(watch.Armed);
            Assert.AreEqual(AdGrantWatch.Step.None, Next(watch, 3.3), "detached exactly once");
        }

        [Test]
        public void TheWindowClosesAndTheListenerGoesWithIt()
        {
            var watch = new AdGrantWatch();
            watch.Expect(Credits, 0, Uid, 0);
            Next(watch, 0); watch.Attached(true);
            Next(watch, 0.1); watch.Refreshed();

            Assert.AreEqual(AdGrantWatch.Step.Detach, Next(watch, AdGrantWatch.WindowSeconds));
            Assert.IsFalse(watch.Armed);
        }

        [Test]
        public void AnAccountSwitchStopsTheWatch()
        {
            var watch = new AdGrantWatch();
            watch.Expect(Credits, 0, Uid, 0);
            Next(watch, 0); watch.Attached(true);

            Assert.AreEqual(AdGrantWatch.Step.Detach, Next(watch, 1, "uid-b"));
            Assert.IsFalse(watch.Armed);
        }

        [Test]
        public void WithNoListenerItAsksOnATimerInsideTheWindow()
        {
            var watch = new AdGrantWatch();
            watch.Expect(Credits, 0, Uid, 0);
            Next(watch, 0); watch.Attached(false);
            Next(watch, 0.1); watch.Refreshed();

            Assert.AreEqual(AdGrantWatch.Step.None, Next(watch, AdGrantWatch.PollSeconds - 1));
            Assert.AreEqual(AdGrantWatch.Step.Refresh, Next(watch, AdGrantWatch.PollSeconds));
            watch.Refreshed();

            // And it never detaches what it never attached.
            Assert.AreEqual(AdGrantWatch.Step.None, Next(watch, AdGrantWatch.WindowSeconds));
            Assert.IsFalse(watch.Armed);
        }

        [Test]
        public void ASecondViewKeepsTheFirstBaselineAndExtendsTheWindow()
        {
            var watch = new AdGrantWatch();
            watch.Expect(Credits, 1000, Uid, 0);
            Next(watch, 0); watch.Attached(true);
            Next(watch, 0.1); watch.Refreshed();

            // The first grant lands between the two views, then the second view arms again.
            watch.Expect(Credits, 1300, Uid, 60);
            _granted[Credits] = 1300;

            Assert.AreEqual(AdGrantWatch.Step.Detach, Next(watch, 60.1),
                "the first grant already cleared the earlier baseline");

            // Re-armed with the window measured from the second view.
            watch.Expect(Credits, 1300, Uid, 61);
            Assert.AreEqual(AdGrantWatch.Step.Attach, Next(watch, 61));
            watch.Attached(true);
            Assert.AreEqual(AdGrantWatch.Step.Refresh, Next(watch, 61.1));
            watch.Refreshed();
            Assert.IsTrue(watch.Armed);
            Assert.AreNotEqual(AdGrantWatch.Step.Detach, Next(watch, 61 + AdGrantWatch.WindowSeconds - 1));
        }

        [Test]
        public void NoAccountMeansNothingIsArmed()
        {
            var watch = new AdGrantWatch();
            watch.Expect(Credits, 0, "", 0);
            Assert.IsFalse(watch.Armed);
            Assert.AreEqual(AdGrantWatch.Step.None, Next(watch, 0, ""));
        }
    }
}
