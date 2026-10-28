using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Privacy;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Invariant 55b's order: UMP answers, consent is applied, then collection opens - and the
    /// attribution SDK starts on the same gate, holding what was raised before it.
    ///
    /// <para>
    /// Android 1.1.4 turned Firebase collection on with every purpose still denied, a few seconds
    /// before UMP's answer granted them; <c>first_open</c> and <c>session_start</c> went out at that
    /// moment as cookieless pings and GA reported no first opens for the build. These hold the fix
    /// with the SDK calls as delegates: <see cref="AnalyticsOpening"/> over the real
    /// <see cref="AnalyticsDelivery"/>, and <see cref="AttributionStart"/> over a recording SDK.
    /// </para>
    /// </summary>
    public sealed class MeasurementGateTests
    {
        static readonly AdPrivacySignals Outside = new AdPrivacySignals(
            false, ConsentStatus.Unknown, false, false, TrackingStatus.NotSupported,
            ConsentModeValues.Parse("3333"));

        static readonly AdPrivacySignals EeaGranted = new AdPrivacySignals(
            true, ConsentStatus.Granted, false, false, TrackingStatus.NotSupported,
            ConsentModeValues.Parse("1111"));

        static readonly AdPrivacySignals EeaDenied = new AdPrivacySignals(
            true, ConsentStatus.Denied, false, false, TrackingStatus.NotSupported,
            ConsentModeValues.Parse("2222"));

        /// <summary>The Firebase half as the build wires it, with the native calls written to one log.</summary>
        sealed class Firebase
        {
            public double Now;
            public readonly List<string> Log = new List<string>();
            public readonly MeasurementGate Gate;
            public readonly AnalyticsDelivery Delivery;
            public readonly AnalyticsOpening Opening;

            public Firebase()
            {
                Gate = new MeasurementGate(() => Now);
                Delivery = new AnalyticsDelivery(on => Log.Add(on ? "collection:on" : "collection:off"),
                    (name, _) => { Log.Add("event:" + name); return AnalyticsSendResult.Accepted; },
                    () => Now);

                var restricted = AnalyticsConsent.For(AdPrivacySignals.Restricted);
                Opening = new AnalyticsOpening(restricted, consent =>
                {
                    Log.Add("consent:" + (consent.AnalyticsStorage ? "granted" : "denied"));
                    return true;
                }, Delivery.MarkReady);
                Delivery.SetCollectionAllowed(restricted.Collect);
            }

            /// <summary>One frame of <c>AnalyticsSetup.Tick</c>.</summary>
            public void Tick(bool asked = true, bool prepared = false)
            {
                Gate.Observe(asked, prepared);
                Opening.TryOpen(Gate.AnalyticsMayOpen);
                Delivery.Tick();
            }

            /// <summary><c>AdPrivacy.Changed</c>, as <c>AnalyticsSetup.PrivacyChanged</c> routes it.</summary>
            public void Answer(AdPrivacySignals signals)
            {
                Gate.Answer(signals);
                Opening.Consent(AnalyticsConsent.For(signals));
                Opening.TryOpen(Gate.AnalyticsMayOpen);
            }
        }

        sealed class RecordingSdk : IAttributionSdk
        {
            public readonly List<string> Log = new List<string>();
            public bool ThrowOnStart;

            public void ApplyConsent(AttributionConsent consent)
                => Log.Add(consent.Anonymised ? "consent:anonymised" : "consent:identified");

            public void Start()
            {
                if (ThrowOnStart) throw new System.InvalidOperationException("no SDK");
                Log.Add("start");
            }

            public void Send(string name, IReadOnlyDictionary<string, string> values)
                => Log.Add("send:" + name + (values != null && values.TryGetValue("k", out var v) ? "=" + v : ""));
        }

        static Dictionary<string, string> Values(string value) => new Dictionary<string, string> { { "k", value } };

        // ------------------------------------------------------------ Firebase: the order

        [Test]
        public void CollectionOpensOnlyAfterTheAnswerHasBeenApplied()
        {
            var f = new Firebase();
            f.Opening.SdkReady();
            f.Delivery.Track("lesson_shown", null);

            // SDK ready, UMP asked, no answer yet: the restrictive consent is pushed so a later
            // time-out has it, and collection stays off.
            f.Now = 1d;
            f.Tick();
            Assert.IsFalse(f.Opening.IsOpen, "collection opened before UMP answered");
            CollectionAssert.DoesNotContain(f.Log, "collection:on");

            f.Now = 1.5d;
            f.Answer(Outside);
            f.Tick();

            Assert.IsTrue(f.Opening.IsOpen);
            CollectionAssert.AreEqual(new[] { "consent:denied", "consent:granted", "collection:on", "event:lesson_shown" },
                f.Log, "UMP's answer, then setConsent, then collection on - and the queued event after both");
        }

        [Test]
        public void AnAnswerBeforeTheSdkIsReadyIsPushedTheMomentItIs()
        {
            var f = new Firebase();
            f.Delivery.Track("level_started", null);
            f.Answer(Outside);
            f.Tick();
            Assert.IsEmpty(f.Log, "nothing native is called before the dependency check");

            f.Opening.SdkReady();
            f.Tick();

            CollectionAssert.AreEqual(new[] { "consent:granted", "collection:on", "event:level_started" }, f.Log);
        }

        [Test]
        public void ThreeSecondsWithoutAnAnswerOpensCollectionDeniedAndTheLateAnswerUpgradesIt()
        {
            var f = new Firebase();
            f.Opening.SdkReady();
            f.Tick();                                  // asked at t = 0

            f.Now = MeasurementGate.AnswerSeconds - 0.01d;
            f.Tick();
            Assert.IsFalse(f.Opening.IsOpen, "opened before the fallback");

            f.Now = MeasurementGate.AnswerSeconds;
            f.Tick();
            Assert.IsTrue(f.Opening.IsOpen, "the fallback never opened collection");
            Assert.IsFalse(f.Opening.Applied.Value.AnalyticsStorage, "the fallback opened with analytics storage granted");
            CollectionAssert.AreEqual(new[] { "consent:denied", "collection:on" }, f.Log);

            f.Now = 5d;
            f.Answer(Outside);
            Assert.IsTrue(f.Opening.Applied.Value.AnalyticsStorage, "the late answer was not applied");
            Assert.AreEqual("consent:granted", f.Log[f.Log.Count - 1]);
        }

        [Test]
        public void ANewEeaPlayerOpensDeniedAtOnceWhileTheFormIsOwed()
        {
            var f = new Firebase();
            f.Opening.SdkReady();
            f.Now = 0.8d;
            f.Tick(asked: true, prepared: true);       // the splash's refresh: form owed, nothing committed

            Assert.IsTrue(f.Opening.IsOpen);
            Assert.IsFalse(f.Opening.Applied.Value.AnalyticsStorage, "first_open must be denied before the form");

            f.Answer(EeaGranted);                      // the hub's form, after the tutorial
            Assert.IsTrue(f.Opening.Applied.Value.AnalyticsStorage);
        }

        // ------------------------------------------------------------ the gate's clock

        [Test]
        public void TheClockRunsFromTheQuestionNotFromInstall()
        {
            double now = 0d;
            var gate = new MeasurementGate(() => now);

            now = 5d;                                  // a slow splash: UMP asked five seconds in
            gate.Observe(true, false);
            now = 5d + MeasurementGate.AnswerSeconds - 0.01d;
            Assert.IsFalse(gate.AnalyticsMayOpen, "timed out a question it had only just put");
            now = 5d + MeasurementGate.AnswerSeconds;
            Assert.IsTrue(gate.AnalyticsMayOpen);
            Assert.IsTrue(gate.IsTimedOut);
        }

        [Test]
        public void AQuestionNobodyAsksStopsHoldingMeasurementBackAtTheCeiling()
        {
            double now = 0d;
            var gate = new MeasurementGate(() => now);
            now = MeasurementGate.UnaskedSeconds - 0.01d;
            Assert.IsFalse(gate.AnalyticsMayOpen);
            now = MeasurementGate.UnaskedSeconds;
            Assert.IsTrue(gate.AnalyticsMayOpen);
        }

        [Test]
        public void ATimeOutIsLatched()
        {
            double now = 0d;
            var gate = new MeasurementGate(() => now);
            gate.Observe(true, false);
            now = MeasurementGate.AnswerSeconds;
            Assert.IsTrue(gate.IsTimedOut);
            now = 0d;                                  // a clock that steps back closes nothing
            Assert.IsTrue(gate.AnalyticsMayOpen);
        }

        // ------------------------------------------------------------ attribution: when it starts

        [Test]
        public void AttributionStartsOnTheAnswerAsThePlayerGaveIt()
        {
            var gate = new MeasurementGate(() => 0d);
            Assert.IsFalse(gate.AttributionMayStart(out _));

            gate.Answer(Outside);
            Assert.IsTrue(gate.AttributionMayStart(out var consent));
            Assert.AreEqual(new AttributionConsent(false, true), consent);
        }

        [Test]
        public void ATimeOutWithNoWordFromUmpStartsAttributionAnonymisedAsAGdprPlayer()
        {
            double now = 0d;
            var gate = new MeasurementGate(() => now);
            gate.Observe(true, false);
            now = MeasurementGate.AnswerSeconds;

            Assert.IsTrue(gate.AttributionMayStart(out var consent));
            Assert.IsTrue(consent.GdprApplies);
            Assert.IsTrue(consent.Anonymised, "the fallback must be the EU-safe answer");
        }

        /// <summary>
        /// The owner's choice (2026-10-28): an AppsFlyer install reported anonymised can never be
        /// attributed, so a new EEA player's attribution waits for the form rather than the clock.
        /// </summary>
        [Test]
        public void AnOwedFormHoldsAttributionPastTheTimeOut()
        {
            double now = 0d;
            var gate = new MeasurementGate(() => now);
            gate.Observe(true, true);
            now = 600d;
            Assert.IsFalse(gate.AttributionMayStart(out _), "attribution started before the owed form");
            Assert.IsTrue(gate.AnalyticsMayOpen);

            gate.Answer(EeaGranted);
            Assert.IsTrue(gate.AttributionMayStart(out var consent));
            Assert.IsFalse(consent.Anonymised);
        }

        // ------------------------------------------------------------ attribution: the queue

        [Test]
        public void HeldEventsAreSentAfterConsentAndStartInTheOrderRaised()
        {
            var sdk = new RecordingSdk();
            var start = new AttributionStart(sdk);

            start.Send("af_achievement_unlocked", Values("siege_fuel"));
            start.Send("af_achievement_unlocked", Values("siege_brim"));
            Assert.IsEmpty(sdk.Log, "an event left before the SDK was started");
            Assert.AreEqual(2, start.Held);

            start.Start(new AttributionConsent(false, true));
            start.Send("l5_reached", Values("s01_warlordsgate"));

            CollectionAssert.AreEqual(new[]
            {
                "consent:identified", "start",
                "send:af_achievement_unlocked=siege_fuel", "send:af_achievement_unlocked=siege_brim",
                "send:l5_reached=s01_warlordsgate",
            }, sdk.Log);
            Assert.AreEqual(0, start.Held);
        }

        [Test]
        public void HeldEventsNeverLeaveUnderAWeakerConsentThanThePlayerGave()
        {
            var sdk = new RecordingSdk();
            var start = new AttributionStart(sdk);
            start.Send("af_achievement_unlocked", Values("siege_fuel"));

            // An EEA player who declined: anonymised before the start, and so before every held event.
            start.Start(AttributionConsent.For(EeaDenied));
            CollectionAssert.AreEqual(new[] { "consent:anonymised", "start", "send:af_achievement_unlocked=siege_fuel" }, sdk.Log);

            // A later change is applied before the next event, in either direction.
            start.Start(AttributionConsent.For(EeaGranted));
            start.Send("l5_reached", Values("s01_warlordsgate"));
            CollectionAssert.AreEqual(new[] { "consent:identified", "send:l5_reached=s01_warlordsgate" },
                sdk.Log.GetRange(3, 2));

            start.Start(AttributionConsent.For(EeaDenied));
            start.Send("af_achievement_unlocked", Values("siege_brim"));
            CollectionAssert.AreEqual(new[] { "consent:anonymised", "send:af_achievement_unlocked=siege_brim" },
                sdk.Log.GetRange(5, 2));
        }

        [Test]
        public void TheQueueIsBoundedAndKeepsTheEarliest()
        {
            var sdk = new RecordingSdk();
            var start = new AttributionStart(sdk);
            for (int i = 0; i < AttributionStart.MaxHeld + 5; i++) start.Send("e", Values(i.ToString()));

            Assert.AreEqual(AttributionStart.MaxHeld, start.Held);
            Assert.AreEqual(5, start.Dropped);

            start.Start(new AttributionConsent(false, true));
            Assert.AreEqual("send:e=0", sdk.Log[2]);
            Assert.AreEqual("send:e=" + (AttributionStart.MaxHeld - 1), sdk.Log[sdk.Log.Count - 1]);
        }

        [Test]
        public void AHeldEventIsACopyOfWhatWasRaised()
        {
            var sdk = new RecordingSdk();
            var start = new AttributionStart(sdk);
            var scratch = Values("first");
            start.Send("e", scratch);
            scratch["k"] = "second";                   // the sink reuses one dictionary

            start.Start(new AttributionConsent(false, true));
            Assert.AreEqual("send:e=first", sdk.Log[2]);
        }

        [Test]
        public void AStartThatThrowsHoldsEverything()
        {
            var sdk = new RecordingSdk { ThrowOnStart = true };
            var start = new AttributionStart(sdk);
            start.Send("e", Values("a"));

            Assert.Throws<System.InvalidOperationException>(() => start.Start(new AttributionConsent(false, true)));
            Assert.IsFalse(start.Started);
            Assert.AreEqual(1, start.Held);
            CollectionAssert.DoesNotContain(sdk.Log, "send:e=a");
        }

        [Test]
        public void AnEeaLaunchHoldsTheTutorialUntilTheFormThenSendsItUnderTheAnswer()
        {
            double now = 0d;
            var gate = new MeasurementGate(() => now);
            var sdk = new RecordingSdk();
            var start = new AttributionStart(sdk);

            void Tick()
            {
                if (!start.Started && gate.AttributionMayStart(out var consent)) start.Start(consent);
            }

            now = 1d;
            gate.Observe(true, true);                  // form owed
            for (int lesson = 0; lesson < 3; lesson++)
            {
                now += 30d;
                start.Send("af_achievement_unlocked", Values("lesson" + lesson));
                Tick();
            }
            Assert.IsEmpty(sdk.Log, "the tutorial left the device before the form");

            gate.Answer(EeaDenied);                     // the hub's form, declined
            Tick();
            Assert.AreEqual("consent:anonymised", sdk.Log[0]);
            Assert.AreEqual("start", sdk.Log[1]);
            Assert.AreEqual(5, sdk.Log.Count);
        }
    }
}
