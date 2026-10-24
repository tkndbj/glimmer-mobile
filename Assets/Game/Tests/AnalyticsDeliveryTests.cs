using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    public sealed class AnalyticsDeliveryTests
    {
        sealed class Harness
        {
            public double Now;
            public bool NativeEnabled = true;
            public readonly List<bool> CollectionCalls = new List<bool>();
            public readonly List<string> Sent = new List<string>();
            public readonly List<AnalyticsDeliveryIssue> Diagnostics = new List<AnalyticsDeliveryIssue>();
            public Action<bool> Set;
            public Func<string, IReadOnlyDictionary<string, object>, AnalyticsSendResult> Send;
            public readonly AnalyticsDelivery Delivery;

            public Harness()
            {
                Delivery = new AnalyticsDelivery(value =>
                {
                    CollectionCalls.Add(value);
                    if (Set != null) Set(value);
                    else NativeEnabled = value;
                }, (name, properties) =>
                {
                    if (Send != null) return Send(name, properties);
                    Sent.Add(name);
                    return AnalyticsSendResult.Accepted;
                }, () => Now, (issue, count) => Diagnostics.Add(issue));
            }

            public void Track(string name = "level_completed", IReadOnlyDictionary<string, object> values = null)
                => Delivery.Track(name, values);

            public void Activate()
            {
                Delivery.SetCollectionAllowed(true);
                Delivery.MarkReady();
            }

            public void Advance(double seconds = 60d)
            {
                Now += seconds;
                Delivery.Tick();
            }
        }

        [Test]
        public void StartupExplicitlyDisablesAPersistedNativeGrant()
        {
            var h = new Harness();
            h.Delivery.SetCollectionAllowed(false);
            h.Delivery.MarkReady();
            Assert.IsFalse(h.NativeEnabled);
            CollectionAssert.AreEqual(new[] { false }, h.CollectionCalls);
            Assert.AreEqual(false, h.Delivery.AppliedCollection);
        }

        [Test]
        public void ANewProcessReconcilesNativeStateRatherThanTrustingAnOldManagedCache()
        {
            bool native = false;
            var first = new Harness { Set = value => native = value };
            first.Activate();
            Assert.IsTrue(native);
            var next = new Harness { Set = value => native = value };
            next.Delivery.MarkReady();
            Assert.IsFalse(native);
            Assert.AreEqual(0, next.Delivery.PendingCount);
        }

        [Test]
        public void NothingTouchesTheSdkUntilItsDependencyCheckCompletes()
        {
            var h = new Harness();
            h.Delivery.SetCollectionAllowed(true);
            h.Track();
            h.Delivery.Tick();
            Assert.IsEmpty(h.CollectionCalls);
            Assert.IsEmpty(h.Sent);
            Assert.AreEqual(1, h.Delivery.PendingCount);
            h.Delivery.MarkReady();
            CollectionAssert.AreEqual(new[] { true }, h.CollectionCalls);
            Assert.AreEqual(1, h.Sent.Count);
        }

        [Test]
        public void SdkReadinessCannotMakeAnEarlierUnpermittedEventReplayable()
        {
            var h = new Harness();
            h.Track("lesson_shown");
            h.Delivery.MarkReady();
            h.Track("lesson_finished");
            h.Delivery.SetCollectionAllowed(true);
            h.Track("level_completed");
            h.Delivery.Tick();
            CollectionAssert.AreEqual(new[] { "level_completed" }, h.Sent);
            Assert.AreEqual(2, h.Delivery.Count(AnalyticsDeliveryIssue.Excluded));
        }

        [Test]
        public void PendingEventsOwnTheirSnapshotOfTheReusedTelemetryDictionary()
        {
            var h = new Harness();
            h.Delivery.SetCollectionAllowed(true);
            var values = new Dictionary<string, object> { ["level_id"] = "original", ["stars"] = 3 };
            h.Track(values: values);
            values["level_id"] = "later";
            values.Clear();
            IReadOnlyDictionary<string, object> received = null;
            h.Send = (name, snapshot) =>
            {
                received = snapshot;
                return AnalyticsSendResult.Accepted;
            };
            h.Delivery.MarkReady();
            Assert.IsNotNull(received);
            Assert.AreEqual("original", received["level_id"]);
            Assert.AreEqual(3, received["stars"]);
            Assert.AreEqual(0, h.Delivery.PendingCount);
        }

        [Test]
        public void WithdrawalClearsPendingEventsAndARegrantCannotRecoverThem()
        {
            var h = new Harness();
            h.Delivery.SetCollectionAllowed(true);
            h.Track("before_withdrawal");
            h.Delivery.SetCollectionAllowed(false);
            h.Track("while_denied");
            h.Delivery.SetCollectionAllowed(true);
            h.Delivery.MarkReady();
            Assert.IsEmpty(h.Sent);
            Assert.AreEqual(1, h.Delivery.Count(AnalyticsDeliveryIssue.ClearedOnWithdrawal));
            Assert.AreEqual(1, h.Delivery.Count(AnalyticsDeliveryIssue.Excluded));
        }

        [Test]
        public void AFailedEnableRetainsEligibleEventsAndBacksOff()
        {
            var h = new Harness();
            int calls = 0;
            h.Set = value => { if (++calls == 1) throw new InvalidOperationException(); h.NativeEnabled = value; };
            h.Activate();
            h.Track();
            h.Delivery.Tick();
            Assert.IsNull(h.Delivery.AppliedCollection);
            Assert.AreEqual(1, h.CollectionCalls.Count);
            Assert.AreEqual(1, h.Delivery.PendingCount);
            Assert.IsEmpty(h.Sent);
            h.Advance(.99d);
            Assert.AreEqual(1, h.CollectionCalls.Count);
            h.Advance(.01d);
            Assert.AreEqual(2, h.CollectionCalls.Count);
            Assert.AreEqual(1, h.Sent.Count);
        }

        [Test]
        public void AThrowingSetterInvalidatesItsCacheEvenIfTheNativeSideAlreadyChanged()
        {
            var h = new Harness();
            h.Activate();
            h.Set = value => { h.NativeEnabled = value; throw new InvalidOperationException(); };
            h.Delivery.SetCollectionAllowed(false);
            Assert.IsFalse(h.NativeEnabled);
            Assert.IsNull(h.Delivery.AppliedCollection);
            h.Set = value => h.NativeEnabled = value;
            h.Delivery.SetCollectionAllowed(true);
            Assert.IsTrue(h.NativeEnabled);
            CollectionAssert.AreEqual(new[] { true, false, true }, h.CollectionCalls);
        }

        [Test]
        public void AFailedDisableClosesCustomDeliveryImmediatelyAndIsRetried()
        {
            var h = new Harness();
            h.Activate();
            h.Track();
            h.Set = value => { throw new InvalidOperationException(); };
            h.Delivery.SetCollectionAllowed(false);
            h.Track("after_withdrawal");
            h.Delivery.Tick();
            Assert.AreEqual(0, h.Delivery.PendingCount);
            Assert.IsEmpty(h.Sent);
            h.Set = value => h.NativeEnabled = value;
            h.Advance(1d);
            Assert.IsFalse(h.NativeEnabled);
            Assert.AreEqual(false, h.Delivery.AppliedCollection);
        }

        [Test]
        public void PreHandoffRetriesPreserveFifoAndDoNotRunEveryFrame()
        {
            var h = new Harness();
            h.Activate();
            int attempts = 0;
            h.Send = (name, values) =>
            {
                if (++attempts == 1) return AnalyticsSendResult.Retry;
                h.Sent.Add(name);
                return AnalyticsSendResult.Accepted;
            };
            h.Track("first");
            h.Track("second");
            h.Delivery.Tick();
            for (int i = 0; i < 100; i++) h.Delivery.Tick();
            Assert.AreEqual(1, attempts);
            Assert.AreEqual(2, h.Delivery.PendingCount);
            h.Advance(1d);
            CollectionAssert.AreEqual(new[] { "first", "second" }, h.Sent);
            Assert.AreEqual(3, attempts);
        }

        [Test]
        public void ARepeatedlyFailingPayloadIsReportedWithoutStrandingLaterEvents()
        {
            var h = new Harness();
            h.Activate();
            h.Send = (name, values) =>
            {
                if (name == "poison") return AnalyticsSendResult.Retry;
                h.Sent.Add(name);
                return AnalyticsSendResult.Accepted;
            };
            h.Track("poison");
            h.Track("later");
            for (int i = 0; i < AnalyticsDelivery.MaxSendAttempts; i++) h.Advance();
            CollectionAssert.AreEqual(new[] { "later" }, h.Sent);
            Assert.AreEqual(0, h.Delivery.PendingCount);
            Assert.AreEqual(1, h.Delivery.Count(AnalyticsDeliveryIssue.RetryLimit));
            Assert.AreEqual(AnalyticsDelivery.MaxSendAttempts, h.Delivery.Count(AnalyticsDeliveryIssue.SendFailure));
        }

        [Test]
        public void AnUncertainPurchaseHandoffIsReportedAndNeverReplayed()
        {
            var h = new Harness();
            h.Activate();
            h.Send = (name, values) =>
            {
                h.Sent.Add(name); // Simulates native acceptance followed by an exception.
                return AnalyticsSendResult.Unconfirmed;
            };
            h.Track("store_purchase_granted");
            h.Delivery.Tick();
            h.Advance();
            Assert.AreEqual(1, h.Sent.Count);
            Assert.AreEqual(0, h.Delivery.PendingCount);
            Assert.AreEqual(1, h.Delivery.Count(AnalyticsDeliveryIssue.UnconfirmedHandoff));
        }

        [Test]
        public void AnUnexpectedTransportExceptionDoesNotStrandTheQueueOrGetBlindlyReplayed()
        {
            var h = new Harness();
            h.Activate();
            h.Send = (name, values) =>
            {
                if (name == "uncertain") throw new InvalidOperationException("not logged");
                h.Sent.Add(name);
                return AnalyticsSendResult.Accepted;
            };
            h.Track("uncertain");
            h.Track("later");
            h.Delivery.Tick();
            h.Advance();
            CollectionAssert.AreEqual(new[] { "later" }, h.Sent);
            Assert.AreEqual(1, h.Delivery.Count(AnalyticsDeliveryIssue.UnconfirmedHandoff));
        }

        [Test]
        public void SuccessfulHandoffsAreNotRepeatedByTicksOrRepeatedEligibilityNotifications()
        {
            var h = new Harness();
            h.Activate();
            h.Track();
            h.Delivery.Tick();
            h.Delivery.MarkReady();
            h.Delivery.SetCollectionAllowed(true);
            h.Advance();
            Assert.AreEqual(1, h.Sent.Count);
            Assert.AreEqual(1, h.CollectionCalls.Count);
        }

        [Test]
        public void AFrameDrainsABoundedBatchRatherThanBlockingOnTheWholeQueue()
        {
            var h = new Harness();
            h.Activate();
            for (int i = 0; i < 20; i++) h.Track("event_" + i);
            Assert.IsEmpty(h.Sent);
            h.Delivery.Tick();
            Assert.AreEqual(AnalyticsDelivery.MaxPerTick, h.Sent.Count);
            Assert.AreEqual(20 - AnalyticsDelivery.MaxPerTick, h.Delivery.PendingCount);
        }

        [Test]
        public void QueueOverflowIsBoundedAndDiagnosed()
        {
            var h = new Harness();
            h.Delivery.SetCollectionAllowed(true);
            for (int i = 0; i < AnalyticsDelivery.MaxPending + 1; i++) h.Track();
            Assert.AreEqual(AnalyticsDelivery.MaxPending, h.Delivery.PendingCount);
            Assert.AreEqual(1, h.Delivery.Count(AnalyticsDeliveryIssue.Overflow));
            CollectionAssert.Contains(h.Diagnostics, AnalyticsDeliveryIssue.Overflow);
            h.Delivery.MarkReady();
            for (int i = 0; i < AnalyticsDelivery.MaxPending; i++) h.Delivery.Tick();
            Assert.AreEqual(AnalyticsDelivery.MaxPending, h.Sent.Count);
        }

        [Test]
        public void PendingPayloadsAreBoundedAndOnlyContainScalarSnapshots()
        {
            var h = new Harness();
            h.Delivery.SetCollectionAllowed(true);
            var values = new Dictionary<string, object>();
            for (int i = 0; i < 100; i++) values["parameter_" + i] = new string('x', 1000);
            h.Track(values: values);
            IReadOnlyDictionary<string, object> received = null;
            h.Send = (name, snapshot) =>
            {
                received = snapshot;
                return AnalyticsSendResult.Accepted;
            };
            h.Delivery.MarkReady();
            Assert.IsNotNull(received);
            Assert.AreEqual(AnalyticsDelivery.MaxParameters, received.Count);
            foreach (var pair in received) Assert.AreEqual(100, ((string)pair.Value).Length);
        }

        [Test]
        public void DiagnosticVolumeGrowsLogarithmicallyRatherThanPerEvent()
        {
            var h = new Harness();
            for (int i = 0; i < 100; i++) h.Track();
            Assert.AreEqual(100, h.Delivery.Count(AnalyticsDeliveryIssue.Excluded));
            Assert.AreEqual(7, h.Diagnostics.Count); // 1, 2, 4, 8, 16, 32, 64.
        }

        [Test]
        public void AThrowingDiagnosticCannotInterruptDelivery()
        {
            int sent = 0;
            var delivery = new AnalyticsDelivery(value => { }, (name, values) =>
            {
                sent++;
                return AnalyticsSendResult.Accepted;
            }, () => 0d, (issue, count) => { throw new InvalidOperationException(); });
            delivery.Track("excluded", null);
            delivery.SetCollectionAllowed(true);
            delivery.MarkReady();
            delivery.Track("permitted", null);
            delivery.Tick();
            Assert.AreEqual(1, sent);
        }

        [Test]
        public void WithdrawalDuringAHandoffCannotDequeueAnEmptyQueue()
        {
            var h = new Harness();
            h.Activate();
            h.Send = (name, values) =>
            {
                h.Sent.Add(name);
                h.Delivery.SetCollectionAllowed(false);
                return AnalyticsSendResult.Accepted;
            };
            h.Track("first");
            h.Track("cleared");
            h.Delivery.Tick();
            h.Delivery.Tick();
            CollectionAssert.AreEqual(new[] { "first" }, h.Sent);
            Assert.AreEqual(0, h.Delivery.PendingCount);
            Assert.IsFalse(h.NativeEnabled);
        }

        [Test]
        public void WithdrawalDuringTheCollectionSetterCannotLeaveAFalseManagedCache()
        {
            var h = new Harness();
            h.Set = value =>
            {
                h.NativeEnabled = value;
                if (value) h.Delivery.SetCollectionAllowed(false);
            };
            h.Delivery.SetCollectionAllowed(true);
            h.Track();
            h.Delivery.MarkReady();
            Assert.AreEqual(true, h.Delivery.AppliedCollection);
            Assert.IsEmpty(h.Sent);
            h.Delivery.Tick();
            Assert.IsFalse(h.NativeEnabled);
            Assert.AreEqual(false, h.Delivery.AppliedCollection);
        }

        [Test]
        public void AThrowingPayloadSnapshotIsDiagnosedBeforeAnyHandoff()
        {
            var h = new Harness();
            h.Activate();
            h.Track(values: new Dictionary<string, object> { ["value"] = new ThrowingValue() });
            h.Track("later");
            h.Delivery.Tick();
            CollectionAssert.AreEqual(new[] { "later" }, h.Sent);
            Assert.AreEqual(1, h.Delivery.Count(AnalyticsDeliveryIssue.InvalidPayload));
        }

        sealed class ThrowingValue
        {
            public override string ToString() => throw new InvalidOperationException("not logged");
        }

        [Test]
        public void ClockFailuresStayInsideAnalyticsAndCanRecover()
        {
            bool fail = true;
            int sent = 0;
            var delivery = new AnalyticsDelivery(value => { }, (name, values) =>
            {
                sent++;
                return AnalyticsSendResult.Accepted;
            }, () => fail ? throw new InvalidOperationException() : 1d);
            delivery.SetCollectionAllowed(true);
            delivery.Track("pending", null);
            delivery.MarkReady();
            Assert.AreEqual(1, delivery.Count(AnalyticsDeliveryIssue.DriverFailure));
            Assert.AreEqual(1, delivery.PendingCount);
            fail = false;
            delivery.Tick();
            Assert.AreEqual(1, sent);
        }
    }
}
