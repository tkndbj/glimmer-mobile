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

        // ------------------------------------------------------------- fixtures
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
