using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Analytics;
using GlimmerGrove.Ads;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    public sealed class AdRevenueTests
    {
        sealed class Sink : IAnalyticsSink
        {
            public readonly List<string> Names = new List<string>();
            public readonly List<Dictionary<string, object>> Values = new List<Dictionary<string, object>>();
            public Action AfterTrack;
            public int Thread;

            public void Track(string name, IReadOnlyDictionary<string, object> values)
            {
                Thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                Names.Add(name);
                var copy = new Dictionary<string, object>();
                foreach (var pair in values) copy[pair.Key] = pair.Value;
                Values.Add(copy);
                AfterTrack?.Invoke();
            }
        }

        static AdRevenueSource Source(AdRevenueOwner owner = AdRevenueOwner.LevelPlay,
            string network = "Google Bidding", string instance = "verified-id")
            => new AdRevenueSource("unit", network, instance, owner);

        static AdRevenueImpression Impression(string auction = "auction", double? revenue = .000123d,
            string network = "Google Bidding", string instance = "verified-id", string unit = "unit",
            string name = "google_bidding", string format = "rewardedVideo")
            => new AdRevenueImpression(unit, auction, network, instance, name, format, revenue, "BID", "win_bonus");

        static AdRevenueTracker Active(IAnalyticsSink sink, params AdRevenueSource[] sources)
        {
            var tracker = new AdRevenueTracker(sink, sources.Length == 0 ? new[] { Source() } : sources);
            tracker.SetCollectionAllowed(true);
            return tracker;
        }

        [Test]
        public void UsesStandardEventAndUnscaledNumericUsdValueWithoutIdentifiers()
        {
            var sink = new Sink();
            var tracker = Active(sink);
            tracker.Capture(Impression());
            Assert.IsEmpty(sink.Names);
            tracker.Pump();
            CollectionAssert.AreEqual(new[] { "ad_impression" }, sink.Names);
            var values = sink.Values[0];
            Assert.AreEqual(.000123d, values["value"]);
            Assert.IsInstanceOf<double>(values["value"]);
            Assert.AreEqual("USD", values["currency"]);
            Assert.AreEqual("LevelPlay", values["ad_platform"]);
            Assert.AreEqual("Google Bidding", values["ad_source"]);
            Assert.AreEqual("google_bidding", values["ad_unit_name"]);
            Assert.AreEqual("rewardedVideo", values["ad_format"]);
            Assert.AreEqual("BID", values["revenue_precision"]);
            Assert.AreEqual("win_bonus", values["ad_placement"]);
            Assert.AreEqual(8, values.Count);
        }

        [Test]
        public void RepeatedSdkCallbackIsCountedOnlyOnce()
        {
            var sink = new Sink();
            var tracker = Active(sink);
            tracker.Capture(Impression());
            tracker.Pump();
            tracker.Capture(Impression(revenue: 99d));
            tracker.Pump();
            Assert.AreEqual(1, sink.Names.Count);
            Assert.AreEqual(.000123d, sink.Values[0]["value"]);
            Assert.AreEqual(1, tracker.Count(AdRevenueIssue.Duplicate));
        }

        [Test]
        public void SameAuctionAcrossDifferentAdUnitsIsNotCollapsed()
        {
            var sink = new Sink();
            var tracker = Active(sink, Source(), new AdRevenueSource("other", "Google Bidding", "verified-id", AdRevenueOwner.LevelPlay));
            tracker.Capture(Impression());
            tracker.Capture(Impression(unit: "other"));
            tracker.Pump();
            Assert.AreEqual(2, sink.Names.Count);
        }

        [Test]
        public void EmptyOwnershipRegistryCannotForwardAnyNetwork()
        {
            var sink = new Sink();
            var tracker = new AdRevenueTracker(sink, Array.Empty<AdRevenueSource>());
            tracker.SetCollectionAllowed(true);
            tracker.Capture(Impression());
            tracker.Pump();
            Assert.IsEmpty(sink.Names);
            Assert.AreEqual(1, tracker.Count(AdRevenueIssue.UnverifiedSource));
        }

        [TestCase("Google Bidding", "other", "unit")]
        [TestCase("AdMob", "verified-id", "unit")]
        [TestCase("ironSource Exchange", "verified-id", "unit")]
        [TestCase("Google Bidding", "verified-id", "other")]
        public void NetworkNameOrUnverifiedInstanceOrUnitCannotAuthorizeRevenue(string network, string instance, string unit)
        {
            var sink = new Sink();
            var tracker = Active(sink);
            tracker.Capture(Impression(network: network, instance: instance, unit: unit));
            tracker.Pump();
            Assert.IsEmpty(sink.Names);
        }

        [TestCase("Google Bidding")]
        [TestCase("AdMob")]
        public void ReviewedExactBidderNameCanIdentifyTheVerifiedGoogleInstance(string sdkNetwork)
        {
            var sink = new Sink();
            var tracker = Active(sink, new AdRevenueSource("unit", sdkNetwork, null, AdRevenueOwner.LevelPlay, "google_bidding"));
            tracker.Capture(Impression(network: sdkNetwork));
            tracker.Capture(Impression(auction: "standard", network: sdkNetwork, name: "standard_admob"));
            tracker.Capture(Impression(auction: "prefix", network: sdkNetwork, name: "google_bidding_other"));
            tracker.Pump();
            Assert.AreEqual(1, sink.Names.Count);
            Assert.AreEqual(2, tracker.Count(AdRevenueIssue.UnverifiedSource));
        }

        [Test]
        public void AReviewedIdCannotFallBackToNameWhenTheIdDoesNotMatch()
        {
            var sink = new Sink();
            var tracker = Active(sink, new AdRevenueSource("unit", "Google Bidding", "different", AdRevenueOwner.LevelPlay, "google_bidding"));
            tracker.Capture(Impression());
            tracker.Pump();
            Assert.IsEmpty(sink.Names);
        }

        [Test]
        public void GoogleSdkBidderLabelsStillRequireThePinnedPartnerUnitId()
        {
            var sink = new Sink();
            var tracker = Active(sink, new AdRevenueSource("unit", "admob", "partner-unit", AdRevenueOwner.LevelPlay));
            tracker.Capture(Impression(network: "admob", instance: "partner-unit", name: "Bidding"));
            tracker.Capture(Impression(auction: "standard", network: "admob", instance: "standard-unit", name: "Bidding"));
            tracker.Pump();
            Assert.AreEqual(1, sink.Names.Count);
            Assert.AreEqual(1, tracker.Count(AdRevenueIssue.UnverifiedSource));
        }

        [Test]
        public void AutomaticAdMobOwnerCannotBeForwardedManually()
        {
            var sink = new Sink();
            var tracker = Active(sink, Source(AdRevenueOwner.Automatic));
            tracker.Capture(Impression());
            tracker.Pump();
            Assert.IsEmpty(sink.Names);
            Assert.AreEqual(1, tracker.Count(AdRevenueIssue.AutomaticSource));
        }

        [Test]
        public void ConflictingOwnershipFailsClosedAndConfigurationIsCopied()
        {
            var sink = new Sink();
            var sources = new[] { Source(), Source(AdRevenueOwner.Automatic) };
            var tracker = new AdRevenueTracker(sink, sources);
            sources[1] = new AdRevenueSource("other", "other", "other", AdRevenueOwner.LevelPlay);
            tracker.SetCollectionAllowed(true);
            tracker.Capture(Impression());
            tracker.Pump();
            Assert.IsEmpty(sink.Names);
        }

        [TestCase(null)]
        [TestCase(-.01d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void MissingOrInvalidRevenueDoesNotBecomeZeroOrAnEstimate(double? revenue)
        {
            var sink = new Sink();
            var tracker = Active(sink);
            tracker.Capture(Impression(revenue: revenue));
            tracker.Pump();
            Assert.IsEmpty(sink.Names);
            Assert.AreEqual(1, tracker.Count(AdRevenueIssue.InvalidRevenue));
        }

        [Test]
        public void AReportedZeroRevenueIsAValidImpression()
        {
            var sink = new Sink();
            var tracker = Active(sink);
            tracker.Capture(Impression(revenue: 0d));
            tracker.Pump();
            Assert.AreEqual(0d, sink.Values[0]["value"]);
        }

        [Test]
        public void MissingIdentityAndOversizedMetadataNeverBypassDeduplication()
        {
            var sink = new Sink();
            var tracker = Active(sink);
            tracker.Capture(null);
            tracker.Capture(Impression(auction: null));
            tracker.Capture(Impression(auction: new string('a', 101)));
            tracker.Capture(Impression(auction: "network", network: null));
            tracker.Capture(Impression(auction: "instance", instance: null));
            tracker.Capture(Impression(auction: "format", format: " "));
            tracker.Pump();
            Assert.IsEmpty(sink.Names);
            Assert.AreEqual(6, tracker.Count(AdRevenueIssue.InvalidMetadata));
        }

        [Test]
        public void PayloadNamesAreBoundedAndAnAbsentUnitNameUsesTheKnownUnit()
        {
            var sink = new Sink();
            var tracker = Active(sink);
            tracker.Capture(Impression(name: new string('a', 200), format: new string('f', 200)));
            tracker.Capture(Impression(auction: "fallback", name: null));
            tracker.Pump();
            Assert.AreEqual(100, ((string)sink.Values[0]["ad_unit_name"]).Length);
            Assert.AreEqual(100, ((string)sink.Values[0]["ad_format"]).Length);
            Assert.AreEqual("unit", sink.Values[1]["ad_unit_name"]);
        }

        [Test]
        public void AGrantCannotReplayAnEarlierExcludedImpression()
        {
            var sink = new Sink();
            var tracker = new AdRevenueTracker(sink, new[] { Source() });
            tracker.Capture(Impression());
            tracker.SetCollectionAllowed(true);
            tracker.Capture(Impression());
            tracker.Capture(Impression(auction: "new"));
            tracker.Pump();
            Assert.AreEqual(1, sink.Names.Count);
            Assert.AreEqual(1, tracker.Count(AdRevenueIssue.Excluded));
            Assert.AreEqual(1, tracker.Count(AdRevenueIssue.Duplicate));
        }

        [Test]
        public void WithdrawalAndRegrantCannotResurrectBufferedOrDeniedRevenue()
        {
            var sink = new Sink();
            var tracker = Active(sink);
            tracker.Capture(Impression());
            tracker.SetCollectionAllowed(false);
            tracker.Capture(Impression(auction: "denied"));
            tracker.SetCollectionAllowed(true);
            tracker.Capture(Impression());
            tracker.Capture(Impression(auction: "denied"));
            tracker.Pump();
            Assert.IsEmpty(sink.Names);
            Assert.AreEqual(1, tracker.Count(AdRevenueIssue.ClearedOnWithdrawal));
        }

        [Test]
        public void ANewProcessStartsClosedAndDoesNotReplayAnOldProcessInbox()
        {
            var sink = new Sink();
            var first = Active(sink);
            first.Capture(Impression());
            var restarted = new AdRevenueTracker(sink, new[] { Source() });
            restarted.Capture(Impression(auction: "before-reconciliation"));
            restarted.Pump();
            restarted.SetCollectionAllowed(true);
            restarted.Pump();
            Assert.IsEmpty(sink.Names);
            Assert.AreEqual(0, restarted.PendingCount);
            restarted.Capture(Impression(auction: "new-session"));
            restarted.Pump();
            Assert.AreEqual(1, sink.Names.Count);
        }

        [Test]
        public void BackgroundCallbacksAreDeduplicatedAndOnlyTheMainThreadCallsTheSink()
        {
            var sink = new Sink();
            var tracker = Active(sink);
            int main = Thread.CurrentThread.ManagedThreadId;
            Parallel.For(0, 1000, i => tracker.Capture(Impression()));
            Assert.IsEmpty(sink.Names);
            tracker.Pump();
            Assert.AreEqual(main, sink.Thread);
            Assert.AreEqual(1, sink.Names.Count);
            Assert.AreEqual(999, tracker.Count(AdRevenueIssue.Duplicate));
        }

        [Test]
        public void MemoryAndWorkPerFrameStayBounded()
        {
            var sink = new Sink();
            var tracker = Active(sink);
            for (int i = 0; i < AdRevenueTracker.MaxSeen + 10; i++) tracker.Capture(Impression(auction: "auction-" + i));
            Assert.AreEqual(AdRevenueTracker.MaxPending, tracker.PendingCount);
            Assert.AreEqual(AdRevenueTracker.MaxSeen, tracker.SeenCount);
            tracker.Pump();
            Assert.AreEqual(AdRevenueTracker.MaxPerTick, sink.Names.Count);
            Assert.AreEqual(AdRevenueTracker.MaxPending - AdRevenueTracker.MaxPerTick, tracker.PendingCount);
            Assert.Greater(tracker.Count(AdRevenueIssue.Overflow), 0);
        }

        [Test]
        public void AnUncertainSinkHandoffIsDiagnosedWithoutReplayingRevenueOrBlockingLaterEvents()
        {
            var sink = new Sink { AfterTrack = () => throw new InvalidOperationException("not logged") };
            var tracker = Active(sink);
            tracker.Capture(Impression());
            Assert.DoesNotThrow(() => tracker.Pump());
            sink.AfterTrack = null;
            tracker.Capture(Impression());
            tracker.Capture(Impression(auction: "next"));
            tracker.Pump();
            Assert.AreEqual(2, sink.Names.Count);
            Assert.AreEqual(1, tracker.Count(AdRevenueIssue.UnconfirmedHandoff));
        }

        [Test]
        public void WithdrawalDuringDeliveryStopsTheRemainderOfTheBatch()
        {
            var sink = new Sink();
            var tracker = Active(sink);
            sink.AfterTrack = () => tracker.SetCollectionAllowed(false);
            tracker.Capture(Impression());
            tracker.Capture(Impression(auction: "second"));
            tracker.Pump();
            Assert.AreEqual(1, sink.Names.Count);
            Assert.AreEqual(0, tracker.PendingCount);
        }

        [Test]
        public void FirebasePreparationRetriesPreserveValueAndDoNotCreateASecondRevenueEvent()
        {
            int attempts = 0;
            double now = 0d;
            var received = new List<double>();
            var delivery = new AnalyticsDelivery(allowed => { }, (name, values) =>
            {
                if (++attempts == 1) return AnalyticsSendResult.Retry;
                received.Add((double)values["value"]);
                return AnalyticsSendResult.Accepted;
            }, () => now);
            delivery.SetCollectionAllowed(true);
            delivery.MarkReady();
            var tracker = Active(delivery);
            tracker.Capture(Impression());
            tracker.Pump();
            delivery.Tick();
            tracker.Capture(Impression());
            tracker.Pump();
            now = 1d;
            delivery.Tick();
            CollectionAssert.AreEqual(new[] { .000123d }, received);
            Assert.AreEqual(2, attempts);
        }

        [Test]
        public void FirebaseUncertainNativeAcceptanceDoesNotReplayAcrossPumpsOrNewTracker()
        {
            int attempts = 0;
            var delivery = new AnalyticsDelivery(allowed => { }, (name, values) =>
            { attempts++; return AnalyticsSendResult.Unconfirmed; }, () => 0d);
            delivery.SetCollectionAllowed(true);
            delivery.MarkReady();
            var tracker = Active(delivery);
            tracker.Capture(Impression());
            tracker.Pump();
            delivery.Tick();
            tracker = Active(delivery);
            tracker.Pump();
            delivery.Tick();
            Assert.AreEqual(1, attempts);
            Assert.AreEqual(1, delivery.Count(AnalyticsDeliveryIssue.UnconfirmedHandoff));
        }

        [Test]
        public void DiagnosticsRunOnThePumpAndCannotThrowIntoGameplay()
        {
            int calls = 0;
            var tracker = new AdRevenueTracker(new Sink(), Array.Empty<AdRevenueSource>(), (issue, count) =>
            { calls++; throw new InvalidOperationException("not logged"); });
            Task.Run(() => { for (int i = 0; i < 1000; i++) tracker.CallbackFailed(); }).GetAwaiter().GetResult();
            Assert.AreEqual(0, calls);
            Assert.DoesNotThrow(() => tracker.Pump());
            Assert.AreEqual(1, calls);
            tracker.Pump();
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void AnUnknownSourceExposesOnlyBoundedAdConfigurationAndWithdrawalForgetsIt()
        {
            var tracker = Active(new Sink());
            tracker.Capture(Impression(network: "unverified-network", name: new string('n', 200)));
            var source = tracker.LastUnverifiedSource;
            Assert.AreEqual("unverified-network", source.Network);
            Assert.AreEqual("verified-id", source.InstanceId);
            Assert.AreEqual("unit", source.AdUnitId);
            Assert.AreEqual(100, source.InstanceName.Length);
            Assert.AreEqual(AdRevenueOwner.Unverified, source.Owner);
            tracker.SetCollectionAllowed(false);
            Assert.IsNull(tracker.LastUnverifiedSource.Network);
            tracker.Capture(Impression(auction: "denied", network: "not-retained"));
            Assert.IsNull(tracker.LastUnverifiedSource.Network);
        }

        [Test]
        public void AllConfiguredPlacementsCanShareOnePartnerWithoutLosingOrDuplicatingTheirImpressions()
        {
            var sink = new Sink();
            var units = AdConfig.AdUnits();
            var sources = new List<AdRevenueSource>();
            foreach (string unit in units.Values)
                sources.Add(new AdRevenueSource(unit, "admob", "verified-partner", AdRevenueOwner.LevelPlay));
            var tracker = Active(sink, sources.ToArray());
            foreach (string unit in units.Values)
                tracker.Capture(Impression(unit: unit, network: "admob", instance: "verified-partner"));
            while (tracker.PendingCount > 0) tracker.Pump();
            Assert.AreEqual(units.Count, sink.Names.Count);
            foreach (string unit in units.Values)
            {
                tracker.Capture(Impression(unit: unit, network: "admob", instance: "verified-partner"));
                tracker.Capture(Impression(auction: "standard", unit: unit, network: "admob", instance: "standard-unit"));
            }
            tracker.Pump();
            Assert.AreEqual(units.Count, sink.Names.Count);
            Assert.AreEqual(units.Count, tracker.Count(AdRevenueIssue.Duplicate));
            Assert.AreEqual(units.Count, tracker.Count(AdRevenueIssue.UnverifiedSource));
        }
    }
}
