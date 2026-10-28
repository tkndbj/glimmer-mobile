using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Analytics;
using GlimmerGrove.Privacy;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// <c>boot_ready</c> and <c>tutorial_started</c> (<see cref="BootAnalytics"/>): once per launch,
    /// through the ordinary sinks, held like every event until collection opens (invariant 55b) -
    /// and crash reporting on exactly the analytics answer (<see cref="CrashConsent"/>).
    /// </summary>
    public sealed class BootAnalyticsTests
    {
        sealed class Spy : IAnalyticsSink
        {
            public readonly List<(string Name, Dictionary<string, object> Values)> Events
                = new List<(string, Dictionary<string, object>)>();

            public void Track(string eventName, IReadOnlyDictionary<string, object> properties)
            {
                var copy = new Dictionary<string, object>();
                if (properties != null) foreach (var kv in properties) copy[kv.Key] = kv.Value;
                Events.Add((eventName, copy));
            }
        }

        Spy _spy;

        [SetUp]
        public void Attach()
        {
            BootAnalytics.Reset();
            Telemetry.Clear();
            _spy = new Spy();
            Telemetry.AddSink(_spy);
        }

        [TearDown]
        public void Detach()
        {
            Telemetry.Clear();
            BootAnalytics.Reset();
        }

        [Test]
        public void BootReadyIsSentOncePerLaunchWithItsThreeFigures()
        {
            Assert.IsTrue(BootAnalytics.NoteReady(2345L, "process", true, 10105));
            Assert.IsFalse(BootAnalytics.NoteReady(9999L, "process", false, 10105), "a second screen sent it again");

            Assert.AreEqual(1, _spy.Events.Count);
            var (name, values) = _spy.Events[0];
            Assert.AreEqual("boot_ready", name);
            Assert.AreEqual(2345L, values["load_ms"]);
            Assert.AreEqual("process", values["load_clock"]);
            Assert.AreEqual(true, values["first_launch"]);
            Assert.AreEqual(10105, values["build"]);
        }

        [Test]
        public void ANonsenseWaitIsClampedRatherThanReported()
        {
            BootAnalytics.NoteReady(-5L, null, false, 0);
            Assert.AreEqual(0L, _spy.Events[0].Values["load_ms"]);
            Assert.AreEqual("engine", _spy.Events[0].Values["load_clock"]);

            BootAnalytics.Reset();
            BootAnalytics.NoteReady(long.MaxValue, "engine", false, 10105);
            Assert.AreEqual(BootAnalytics.MaxLoadMs, _spy.Events[1].Values["load_ms"]);
        }

        [Test]
        public void TheFirstTutorialOfAnInstallIsAStartAndALaterOneAResume()
        {
            Assert.IsTrue(BootAnalytics.NoteTutorial(startedBefore: false));
            Assert.IsFalse(BootAnalytics.NoteTutorial(startedBefore: false), "twice in one launch");

            BootAnalytics.Reset();                     // the next launch, tutorial unfinished
            Assert.IsTrue(BootAnalytics.NoteTutorial(startedBefore: true));

            CollectionAssert.AreEqual(new[] { "tutorial_started", "tutorial_resumed" },
                _spy.Events.ConvertAll(e => e.Name));
            Assert.IsEmpty(_spy.Events[0].Values, "a funnel step carries no parameters");
        }

        /// <summary>Raised on the splash's successor, long before UMP may have answered: they must wait, not vanish.</summary>
        [Test]
        public void BothWaitInTheDeliveryQueueUntilCollectionOpens()
        {
            var sent = new List<string>();
            var delivery = new AnalyticsDelivery(_ => { },
                (name, _) => { sent.Add(name); return AnalyticsSendResult.Accepted; }, () => 0d);
            delivery.SetCollectionAllowed(AnalyticsConsent.For(AdPrivacySignals.Restricted).Collect);
            Telemetry.AddSink(delivery);

            BootAnalytics.NoteTutorial(startedBefore: false);
            BootAnalytics.NoteReady(1800L, "process", true, 10105);
            Assert.IsEmpty(sent, "sent before collection opened");

            delivery.MarkReady();
            CollectionAssert.AreEqual(new[] { "tutorial_started", "boot_ready" }, sent);
        }

        /// <summary>The attribution sink is an allow-list (invariant 55b): neither name may reach an ad network.</summary>
        [Test]
        public void NeitherEventIsOnTheAttributionAllowList()
        {
            string sink = File.ReadAllText(Path.Combine(TestJson.RepoRoot(),
                "Assets", "Game", "Scripts", "Telemetry", "AppsFlyerSink.cs"));
            StringAssert.DoesNotContain("BootAnalytics", sink);
            StringAssert.DoesNotContain("boot_ready", sink);
            StringAssert.DoesNotContain("tutorial_started", sink);
            StringAssert.DoesNotContain("tutorial_resumed", sink);
        }

        [Test]
        public void TheWatchSkipsTheSplashAndHearsTheTutorial()
        {
            string watch = File.ReadAllText(Path.Combine(TestJson.RepoRoot(),
                "Assets", "Game", "Scripts", "Presentation", "App", "LaunchWatch.cs"));
            StringAssert.Contains("screen is SplashScreen) return;", watch);
            StringAssert.Contains("if (screen is TutorialScreen) NoteTutorial();", watch);
            StringAssert.Contains("BootAnalytics.NoteTutorial(before)", watch);

            string boot = File.ReadAllText(Path.Combine(TestJson.RepoRoot(),
                "Assets", "Game", "Scripts", "Presentation", "App", "Boot.cs"));
            Assert.Less(boot.IndexOf("LaunchWatch.Attach();", System.StringComparison.Ordinal),
                        boot.IndexOf("Flow.Go<SplashScreen>(instant: true);", System.StringComparison.Ordinal),
                        "the watch must be attached before the splash can hand over");
        }

        // ------------------------------------------------------------------ crash reporting

        [Test]
        public void CrashReportsFollowTheAnalyticsAnswer()
        {
            var outside = new AdPrivacySignals(false, ConsentStatus.Unknown, false, false,
                TrackingStatus.NotSupported, ConsentModeValues.Parse("3333"));
            var denied = new AdPrivacySignals(true, ConsentStatus.Denied, false, false,
                TrackingStatus.NotSupported, ConsentModeValues.Parse("2222"));
            var granted = new AdPrivacySignals(true, ConsentStatus.Granted, false, false,
                TrackingStatus.NotSupported, ConsentModeValues.Parse("1111"));

            Assert.IsFalse(CrashConsent.Allows(AnalyticsConsent.For(AdPrivacySignals.Restricted)), "before the answer");
            Assert.IsTrue(CrashConsent.Allows(AnalyticsConsent.For(outside)));
            Assert.IsFalse(CrashConsent.Allows(AnalyticsConsent.For(denied)));
            Assert.IsTrue(CrashConsent.Allows(AnalyticsConsent.For(granted)));

            // Child-directed collects nothing, whatever a purpose says.
            Assert.IsFalse(CrashConsent.Allows(new AnalyticsConsent(false, true, true, true, true)));
        }

        [Test]
        public void CrashReportingStartsOffOnBothPlatforms()
        {
            string manifest = File.ReadAllText(Path.Combine(TestJson.RepoRoot(),
                "Assets", "Plugins", "Android", "GlimmerMeasurement.androidlib", "AndroidManifest.xml"));
            StringAssert.IsMatch(@"firebase_crashlytics_collection_enabled""\s*android:value=""false""", manifest);

            string plist = File.ReadAllText(Path.Combine(TestJson.RepoRoot(),
                "Assets", "Game", "Editor", "IosPrivacyPlist.cs"));
            StringAssert.Contains("SetBoolean(\"FirebaseCrashlyticsCollectionEnabled\", false)", plist);
        }

        /// <summary>Firebase requires every Firebase package on one version.</summary>
        [Test]
        public void CrashlyticsIsOnTheSameFirebaseVersionEverywhere()
        {
            string root = TestJson.RepoRoot();
            string packages = File.ReadAllText(Path.Combine(root, "Packages", "manifest.json"));
            StringAssert.Contains("com.google.firebase.crashlytics-13.15.0.tgz", packages);
            StringAssert.Contains("com.google.firebase.analytics-13.15.0.tgz", packages);

            StringAssert.IsMatch(@"com\.google\.firebase\.crashlytics';\s*version = \$FIREBASE;",
                File.ReadAllText(Path.Combine(root, "GooglePackages", "fetch.ps1")));
            StringAssert.Contains("fetch 'com.google.firebase.crashlytics'",
                File.ReadAllText(Path.Combine(root, "GooglePackages", "fetch.sh")));

            string gradle = File.ReadAllText(Path.Combine(root, "Assets", "Plugins", "Android", "mainTemplate.gradle"));
            StringAssert.Contains("com.google.firebase:firebase-crashlytics-unity:13.15.0", gradle);
            StringAssert.Contains("com.google.firebase:firebase-crashlytics-ndk:", gradle);

            string asmdef = File.ReadAllText(Path.Combine(root, "Assets", "Game", "Scripts", "Telemetry",
                "GlimmerGrove.Telemetry.asmdef"));
            StringAssert.Contains("\"GLIMMER_CRASHLYTICS\"", asmdef);
        }
    }
}
