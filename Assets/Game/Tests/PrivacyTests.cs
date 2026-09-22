using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Ads;
using GlimmerGrove.Privacy;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Consent, and the order it happens in.
    ///
    /// <para>
    /// Two properties matter more than the rest and are pinned hardest. The mediation SDK must
    /// never be initialised before the consent answer has been applied to it — an SDK that
    /// starts first has already decided what it may collect and has already auctioned on that
    /// decision, and no later call undoes the first request. And an unanswered question must
    /// never read as a yes: every path that fails, times out or is never asked has to land on
    /// the restrictive answer rather than the profitable one.
    /// </para>
    /// <para>
    /// Both are invisible in a screenshot and invisible in the Editor, which never resolves
    /// consent and never shows an ad — the same argument that put <c>TweenCycle</c> and
    /// <c>AccountGate</c> in Domain as pure functions. So the gateway is a seam and the whole
    /// flow runs offline against a fake.
    /// </para>
    /// </summary>
    public sealed class PrivacyTests
    {
        [SetUp]
        public void Reset() => AdPrivacy.Reset();

        [TearDown]
        public void Clear() => AdPrivacy.Reset();

        // ------------------------------------------------------------- the rule
        /// <summary>
        /// Outside GDPR territory the absence of a prompt is not a refusal. A player in a
        /// country that never asks anything is not somebody who declined.
        /// </summary>
        [Test]
        public void PersonalisationIsAllowedWhereTheGdprDoesNotApply()
        {
            var signals = new AdPrivacySignals(false, ConsentStatus.Unknown, false, false,
                                               TrackingStatus.NotSupported);

            Assert.IsTrue(signals.AllowsPersonalisation);
        }

        /// <summary>
        /// Inside it, nothing but an explicit yes will do — and silence is the commonest
        /// state, because it is what a failed or dismissed form leaves behind.
        /// </summary>
        [Test]
        public void SilenceIsNotConsentWhereTheGdprApplies()
        {
            var unknown = new AdPrivacySignals(true, ConsentStatus.Unknown, false, false,
                                               TrackingStatus.NotSupported);
            var denied = new AdPrivacySignals(true, ConsentStatus.Denied, false, false,
                                              TrackingStatus.NotSupported);
            var granted = new AdPrivacySignals(true, ConsentStatus.Granted, false, false,
                                               TrackingStatus.NotSupported);

            Assert.IsFalse(unknown.AllowsPersonalisation, "never asked is not agreement");
            Assert.IsFalse(denied.AllowsPersonalisation);
            Assert.IsTrue(granted.AllowsPersonalisation);
        }

        /// <summary>
        /// A US opt-out and a child-directed build both override a yes. Two separate laws, and
        /// the reason they are separate fields: CCPA is opt-out and the GDPR is opt-in, so one
        /// shared "consented" flag would mean opposite things in each.
        /// </summary>
        [Test]
        public void AnOptOutAndAChildDirectedBuildBothOverrideConsent()
        {
            var sold = new AdPrivacySignals(false, ConsentStatus.Granted, doNotSell: true,
                                            childDirected: false, TrackingStatus.Authorized);
            var child = new AdPrivacySignals(false, ConsentStatus.Granted, doNotSell: false,
                                             childDirected: true, TrackingStatus.Authorized);

            Assert.IsFalse(sold.AllowsPersonalisation);
            Assert.IsFalse(child.AllowsPersonalisation);
        }

        /// <summary>
        /// The default before anything is resolved is the restrictive one. Getting this
        /// backwards would mean every launch personalised ads for a few hundred milliseconds
        /// on the strength of an answer nobody had given.
        /// </summary>
        [Test]
        public void TheStartingStateIsRestrictive()
        {
            Assert.IsFalse(AdPrivacy.Signals.AllowsPersonalisation);
            Assert.IsFalse(AdPrivacy.IsResolved);
            Assert.AreEqual(ConsentStatus.Unknown, AdPrivacy.Signals.Gdpr);
            Assert.IsTrue(AdPrivacy.Signals.GdprApplies, "assumed until a CMP says otherwise");
        }

        /// <summary>
        /// Android and old iOS have no prompt to answer, and that must read as permission
        /// rather than as a refusal — otherwise every Android player would be treated as
        /// having declined a question their platform never asks.
        /// </summary>
        [Test]
        public void NoTrackingPromptIsNotARefusal()
        {
            Assert.IsTrue(new AdPrivacySignals(false, ConsentStatus.Granted, false, false,
                                               TrackingStatus.NotSupported).AllowsDeviceId);

            Assert.IsFalse(new AdPrivacySignals(false, ConsentStatus.Granted, false, false,
                                                TrackingStatus.Denied).AllowsDeviceId);

            Assert.IsFalse(new AdPrivacySignals(false, ConsentStatus.Granted, false, false,
                                                TrackingStatus.NotDetermined).AllowsDeviceId,
                           "an unanswered prompt is not permission");
        }

        // ------------------------------------------------------------ the order
        /// <summary>
        /// The property the whole feature exists for: privacy reaches the provider before the
        /// provider starts. Recorded as a sequence rather than as two booleans, because the
        /// bug this prevents is an ordering bug and a pair of flags cannot see one.
        /// </summary>
        [Test]
        public async Task ConsentIsAppliedBeforeMediationStarts()
        {
            var provider = new RecordingProvider();
            AdPrivacy.Install(new FakeGateway(Granted));
            RewardedAds.Install(provider);

            await RewardedAds.StartAsync();

            CollectionAssert.AreEqual(new[] { "privacy", "init" }, provider.Calls);
        }

        /// <summary>
        /// A gateway that throws must not stop the game starting, and must not be read as a
        /// yes. This is the failure that actually happens: a CMP whose servers are unreachable
        /// on a train.
        /// </summary>
        [Test]
        public async Task AGatewayThatThrowsLeavesTheRestrictiveAnswerAndStillStartsMediation()
        {
            var provider = new RecordingProvider();
            AdPrivacy.Install(new ThrowingGateway());
            RewardedAds.Install(provider);

            await RewardedAds.StartAsync();

            Assert.IsFalse(AdPrivacy.Signals.AllowsPersonalisation);
            CollectionAssert.AreEqual(new[] { "privacy", "init" }, provider.Calls,
                                      "the game still starts; it simply does not personalise");
        }

        /// <summary>
        /// Starting twice does not re-ask or re-initialise. The splash is not the only thing
        /// that could ever call this, and a second consent form on a resume would be the most
        /// annoying possible bug.
        /// </summary>
        [Test]
        public async Task StartingTwiceAsksOnce()
        {
            var gateway = new FakeGateway(Granted);
            var provider = new RecordingProvider();
            AdPrivacy.Install(gateway);
            RewardedAds.Install(provider);

            await RewardedAds.StartAsync();
            await RewardedAds.StartAsync();

            Assert.AreEqual(1, gateway.Resolves);
            CollectionAssert.AreEqual(new[] { "privacy", "init" }, provider.Calls);
        }

        /// <summary>
        /// A withdrawal reaches the SDK without an app restart — which is the whole point of
        /// <c>ApplyPrivacy</c> being separate from initialisation rather than a parameter of it.
        /// </summary>
        [Test]
        public async Task RevisitingCarriesTheNewAnswerToTheProvider()
        {
            var gateway = new FakeGateway(Granted) { Revisited = Denied };
            var provider = new RecordingProvider();
            AdPrivacy.Install(gateway);
            RewardedAds.Install(provider);

            await RewardedAds.StartAsync();
            Assert.IsTrue(provider.Last.AllowsPersonalisation);

            await AdPrivacy.RevisitAsync();

            Assert.IsFalse(provider.Last.AllowsPersonalisation);
            CollectionAssert.AreEqual(new[] { "privacy", "init", "privacy" }, provider.Calls);
        }

        /// <summary>
        /// The app's COPPA classification is folded in by <c>AdPrivacy</c> rather than trusted
        /// from the gateway, so a CMP cannot accidentally claim a child-directed build is not
        /// one. Pinned because it is one line that would never be noticed if it were deleted.
        /// </summary>
        [Test]
        public async Task TheAppsOwnChildClassificationOverridesTheGateway()
        {
            AdPrivacy.Install(new FakeGateway(new AdPrivacySignals(
                false, ConsentStatus.Granted, false, childDirected: true, TrackingStatus.NotSupported)));

            await AdPrivacy.ResolveAsync();

            Assert.AreEqual(AdPrivacy.ChildDirected, AdPrivacy.Signals.ChildDirected);
        }

        /// <summary>
        /// A build with no ad SDK asks nobody anything. Consent exists to be handed to
        /// mediation, so a form shown where no ad can ever appear collects an answer nothing
        /// will use — and spends the one chance to ask on it.
        /// </summary>
        [Test]
        public async Task ABuildWithNoAdProviderNeverPromptsForConsent()
        {
            var gateway = new FakeGateway(Granted);
            AdPrivacy.Install(gateway);
            RewardedAds.Install(null);

            await RewardedAds.StartAsync();

            Assert.AreEqual(0, gateway.Resolves);
            Assert.IsFalse(AdPrivacy.IsResolved);
        }

        // -------------------------------------------------------- Apple's prompt
        /// <summary>
        /// <b>Apple's tracking prompt comes after the consent form has been answered, never
        /// while it is up.</b> Apple rejected 1.0.2 (5.1.1(iv), 2026-09-22) because the form
        /// was still on screen when the tracking dialog landed on it, so after "Ask App Not to
        /// Track" the player was asked about personalised ads. Recorded as a sequence: a
        /// gateway that shows a form and does not return until it is dismissed, and a prompt
        /// that must not have been asked before that.
        /// </summary>
        [Test]
        public async Task ApplesPromptIsAskedOnlyAfterTheConsentFormIsDismissed()
        {
            var log = new List<string>();
            var gateway = new FormGateway(log, Granted);
            var prompt = new RecordingPrompt(log, TrackingStatus.NotDetermined, TrackingStatus.Denied);
            AdPrivacy.Install(gateway);
            AdPrivacy.Install(prompt);

            var resolving = AdPrivacy.ResolveAsync();

            Assert.AreEqual(0, prompt.Requests, "the form is on screen; Apple has not been asked");
            gateway.Dismiss();
            await resolving;

            CollectionAssert.AreEqual(new[] { "form shown", "form dismissed", "apple asked" }, log);
            Assert.AreEqual(TrackingStatus.Denied, AdPrivacy.Signals.Tracking);
        }

        /// <summary>
        /// A consent question left open — the CMP unreachable, the form unloadable — does not
        /// spend Apple's one question on this launch. If it did, the form would come *after*
        /// the tracking dialog on the next launch, which is the order Apple refuses. The
        /// status is still read, so a device that answered on an earlier launch carries it.
        /// </summary>
        [Test]
        public async Task AnOpenConsentQuestionLeavesApplesPromptForALaterLaunch()
        {
            var log = new List<string>();
            var prompt = new RecordingPrompt(log, TrackingStatus.Denied, TrackingStatus.Authorized);
            AdPrivacy.Install(new FakeGateway(AdPrivacySignals.Restricted));
            AdPrivacy.Install(prompt);

            await AdPrivacy.ResolveAsync();

            Assert.AreEqual(0, prompt.Requests, "an open question asks Apple nothing");
            Assert.AreEqual(TrackingStatus.Denied, AdPrivacy.Signals.Tracking,
                            "but the answer already on the device is carried");
            Assert.IsTrue(AdPrivacy.IsResolved, "and the game still starts");
        }

        /// <summary>
        /// The same rule on the path that really fails: a gateway that throws. It was already
        /// pinned to leave the restrictive answer; it must also leave Apple unasked.
        /// </summary>
        [Test]
        public async Task AGatewayThatThrowsLeavesAppleUnasked()
        {
            var prompt = new RecordingPrompt(new List<string>(), TrackingStatus.NotDetermined,
                                             TrackingStatus.Authorized);
            AdPrivacy.Install(new ThrowingGateway());
            AdPrivacy.Install(prompt);

            await AdPrivacy.ResolveAsync();

            Assert.AreEqual(0, prompt.Requests);
        }

        /// <summary>
        /// Where no form is owed the question is closed without one, and Apple is asked on the
        /// first launch as before — a player outside the EEA must not lose the prompt to a
        /// gate written for the EEA. Both spellings a CMP can answer with: "does not apply",
        /// and "applies and answered".
        /// </summary>
        [Test]
        public async Task ASettledConsentAnswerAsksAppleOnTheSameLaunch()
        {
            foreach (var settled in new[]
                     {
                         new AdPrivacySignals(false, ConsentStatus.Unknown, false, false, TrackingStatus.NotDetermined),
                         new AdPrivacySignals(false, ConsentStatus.Granted, false, false, TrackingStatus.NotDetermined),
                         Granted,
                         Denied,
                     })
            {
                AdPrivacy.Reset();
                var prompt = new RecordingPrompt(new List<string>(), TrackingStatus.NotDetermined,
                                                 TrackingStatus.Authorized);
                AdPrivacy.Install(new FakeGateway(settled));
                AdPrivacy.Install(prompt);

                await AdPrivacy.ResolveAsync();

                Assert.AreEqual(1, prompt.Requests, settled.ToString());
                Assert.AreEqual(TrackingStatus.Authorized, AdPrivacy.Signals.Tracking);
            }
        }

        /// <summary>
        /// The predicate itself, so a change to it is a change to a named thing. Restricted is
        /// deliberately open: it is what every failure path returns.
        /// </summary>
        [Test]
        public void TheConsentQuestionIsOpenExactlyWhenTheGdprAppliesAndNobodyAnswered()
        {
            Assert.IsFalse(AdPrivacySignals.Restricted.ConsentSettled);
            Assert.IsFalse(new AdPrivacySignals(true, ConsentStatus.Unknown, false, false,
                                                TrackingStatus.NotDetermined).ConsentSettled);
            Assert.IsTrue(new AdPrivacySignals(false, ConsentStatus.Unknown, false, false,
                                               TrackingStatus.NotDetermined).ConsentSettled,
                          "no form owed is a closed question");
            Assert.IsTrue(Granted.ConsentSettled);
            Assert.IsTrue(Denied.ConsentSettled, "a refusal is an answer");
        }

        // ------------------------------------------------------------- fixtures
        // ------------------------------------------------- what a subscriber is handed
        /// <summary>
        /// <b>A <c>Changed</c> handler must read the signals it is given, never
        /// <see cref="AdPrivacy.Signals"/> or <see cref="AdPrivacy.IsResolved"/>.</b>
        ///
        /// <para>
        /// <c>ResolveAsync</c> raises the event and sets <c>IsResolved</c> on the line after
        /// it, so for the duration of the one callback that carries the answer the flag still
        /// says the answer has not arrived. A handler that asks it returns early and is never
        /// called again, because consent is answered once.
        /// </para>
        /// <para>
        /// That is not a bug to be fixed here — moving the assignment would make <c>Commit</c>'s
        /// own early-out swallow the event whenever a player's real answer happens to equal the
        /// restrictive default, which is exactly the answer that matters most. So the ordering
        /// is pinned instead, and this test is the warning: both measurement SDKs were wired
        /// against the flag and both silently did nothing on a device, with healthy-looking
        /// startup logs either way.
        /// </para>
        /// </summary>
        [Test]
        public void AChangedHandlerIsGivenTheAnswerTheFlagDoesNotYetCarry()
        {
            AdPrivacy.Install(new FakeGateway(Granted));

            AdPrivacySignals handed = AdPrivacySignals.Restricted;
            bool flagDuringCallback = true;
            int calls = 0;

            System.Action<AdPrivacySignals> handler = signals =>
            {
                handed = signals;
                flagDuringCallback = AdPrivacy.IsResolved;
                calls++;
            };

            AdPrivacy.Changed += handler;
            try
            {
                AdPrivacy.ResolveAsync().GetAwaiter().GetResult();
            }
            finally
            {
                AdPrivacy.Changed -= handler;
            }

            Assert.AreEqual(1, calls, "consent is answered once, so there is no second chance");
            Assert.IsTrue(handed.AllowsPersonalisation,
                          "the event argument carries the real answer");
            Assert.IsFalse(flagDuringCallback,
                           "IsResolved is still false inside the callback - a handler that reads "
                           + "it instead of its argument does nothing, for ever");
            Assert.IsTrue(AdPrivacy.IsResolved, "and is true once the call returns");
        }

        /// <summary>
        /// The case that stops the ordering being "fixed" by moving one line: a player whose
        /// real answer equals the restrictive default must still produce an event, or every
        /// refusal in the EEA would be indistinguishable from never having been asked.
        /// </summary>
        [Test]
        public void ARefusalStillRaisesTheEventEvenThoughItMatchesTheDefault()
        {
            AdPrivacy.Install(new FakeGateway(AdPrivacySignals.Restricted));

            int calls = 0;
            System.Action<AdPrivacySignals> handler = _ => calls++;

            AdPrivacy.Changed += handler;
            try
            {
                AdPrivacy.ResolveAsync().GetAwaiter().GetResult();
            }
            finally
            {
                AdPrivacy.Changed -= handler;
            }

            Assert.AreEqual(1, calls,
                            "the restrictive answer is an answer; it must not be swallowed as "
                            + "'nothing changed'");
        }

        static AdPrivacySignals Granted => new AdPrivacySignals(
            true, ConsentStatus.Granted, false, false, TrackingStatus.NotSupported);

        static AdPrivacySignals Denied => new AdPrivacySignals(
            true, ConsentStatus.Denied, false, false, TrackingStatus.NotSupported);

        sealed class FakeGateway : IConsentGateway
        {
            readonly AdPrivacySignals _resolved;

            public FakeGateway(AdPrivacySignals resolved) { _resolved = resolved; }

            public AdPrivacySignals Revisited;
            public int Resolves;

            public Task<AdPrivacySignals> ResolveAsync(CancellationToken cancellation = default)
            {
                Resolves++;
                return Task.FromResult(_resolved);
            }

            public bool CanRevisit => true;

            public Task<AdPrivacySignals> RevisitAsync(CancellationToken cancellation = default)
                => Task.FromResult(Revisited);
        }

        sealed class ThrowingGateway : IConsentGateway
        {
            public Task<AdPrivacySignals> ResolveAsync(CancellationToken cancellation = default)
                => throw new System.InvalidOperationException("the CMP is unreachable");

            public bool CanRevisit => false;

            public Task<AdPrivacySignals> RevisitAsync(CancellationToken cancellation = default)
                => throw new System.InvalidOperationException("the CMP is unreachable");
        }

        /// <summary>
        /// A gateway with a form on screen: <c>ResolveAsync</c> does not return until
        /// <see cref="Dismiss"/>, which is the contract the real gateway now keeps.
        /// </summary>
        sealed class FormGateway : IConsentGateway
        {
            readonly List<string> _log;
            readonly AdPrivacySignals _answer;
            readonly TaskCompletionSource<bool> _dismissed =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public FormGateway(List<string> log, AdPrivacySignals answer)
            {
                _log = log;
                _answer = answer;
            }

            public async Task<AdPrivacySignals> ResolveAsync(CancellationToken cancellation = default)
            {
                _log.Add("form shown");
                await _dismissed.Task;
                _log.Add("form dismissed");
                return _answer;
            }

            public void Dismiss() => _dismissed.TrySetResult(true);

            public bool CanRevisit => true;

            public Task<AdPrivacySignals> RevisitAsync(CancellationToken cancellation = default)
                => Task.FromResult(_answer);
        }

        /// <summary>
        /// Apple's prompt as a recorder: what the device already says, what it would answer if
        /// asked, and how many times it was asked.
        /// </summary>
        sealed class RecordingPrompt : ITrackingPrompt
        {
            readonly List<string> _log;
            readonly TrackingStatus _current;
            readonly TrackingStatus _answer;

            public RecordingPrompt(List<string> log, TrackingStatus current, TrackingStatus answer)
            {
                _log = log;
                _current = current;
                _answer = answer;
            }

            public int Requests;

            public TrackingStatus Status => Requests > 0 ? _answer : _current;

            public Task<TrackingStatus> RequestAsync(CancellationToken cancellation = default)
            {
                Requests++;
                _log.Add("apple asked");
                return Task.FromResult(_answer);
            }
        }

        /// <summary>Records the order it was called in, which is the thing under test.</summary>
        sealed class RecordingProvider : IAdProvider
        {
            public readonly List<string> Calls = new List<string>();
            public AdPrivacySignals Last;

            public bool IsInitialized { get; private set; }

            public bool IsReady(string placementId) => false;

            public event System.Action<string> ReadinessChanged { add { } remove { } }

            public void ApplyPrivacy(AdPrivacySignals signals)
            {
                Calls.Add("privacy");
                Last = signals;
            }

            public Task InitializeAsync(CancellationToken cancellation = default)
            {
                Calls.Add("init");
                IsInitialized = true;
                return Task.CompletedTask;
            }

            public Task<AdShowResult> ShowAsync(AdImpression impression,
                                                CancellationToken cancellation = default)
                => Task.FromResult(AdShowResult.Failed(AdOutcome.Unavailable, impression));
        }
    }
}
