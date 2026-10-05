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
    /// never be initialised before the consent answer has been applied to it - an SDK that
    /// starts first has already decided what it may collect and has already auctioned on that
    /// decision, and no later call undoes the first request. And an unanswered question must
    /// never read as a yes: every path that fails, times out or is never asked has to land on
    /// the restrictive answer rather than the profitable one.
    /// </para>
    /// <para>
    /// Both are invisible in a screenshot and invisible in the Editor, which never resolves
    /// consent and never shows an ad - the same argument that put <c>TweenCycle</c> and
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
        /// Inside it, nothing but an explicit yes will do - and silence is the commonest
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
        /// rather than as a refusal - otherwise every Android player would be treated as
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
        /// on a train. The splash leaves the question open; the hub tries once more, fails,
        /// and commits the restrictive answer - and mediation starts on that.
        /// </summary>
        [Test]
        public async Task AGatewayThatThrowsLeavesTheRestrictiveAnswerAndStillStartsMediation()
        {
            var provider = new RecordingProvider();
            AdPrivacy.Install(new ThrowingGateway());
            RewardedAds.Install(provider);

            var starting = RewardedAds.StartAsync();
            await AdPrivacy.PrepareAsync();

            Assert.IsEmpty(provider.Calls, "an open question starts nothing on the splash");
            Assert.IsTrue(AdPrivacy.Owed, "the hub asks again");

            await AdPrivacy.AskAsync(Idle);
            await starting;

            Assert.IsFalse(AdPrivacy.Signals.AllowsPersonalisation);
            CollectionAssert.AreEqual(new[] { "privacy", "init" }, provider.Calls,
                                      "the game still starts; it simply does not personalise");
            Assert.IsFalse(AdPrivacy.Owed, "and the launch does not ask in a loop");
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
            await AdPrivacy.PrepareAsync();

            Assert.AreEqual(1, gateway.Refreshes);
            Assert.AreEqual(0, gateway.Asks);
            CollectionAssert.AreEqual(new[] { "privacy", "init" }, provider.Calls);
        }

        /// <summary>
        /// A withdrawal reaches the SDK without an app restart - which is the whole point of
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

            await AdPrivacy.PrepareAsync();

            Assert.AreEqual(AdPrivacy.ChildDirected, AdPrivacy.Signals.ChildDirected);
        }

        /// <summary>
        /// A build with no ad SDK asks nobody anything. Consent exists to be handed to
        /// mediation, so a form shown where no ad can ever appear collects an answer nothing
        /// will use - and spends the one chance to ask on it. Nothing is owed, so the hub's
        /// poll never fires either.
        /// </summary>
        [Test]
        public async Task ABuildWithNoAdProviderNeverPromptsForConsent()
        {
            var gateway = new FakeGateway(Granted);
            AdPrivacy.Install(gateway);
            RewardedAds.Install(null);

            await RewardedAds.StartAsync();
            await AdPrivacy.AskAsync(Idle);

            Assert.AreEqual(0, gateway.Refreshes);
            Assert.AreEqual(0, gateway.Asks);
            Assert.IsFalse(AdPrivacy.IsResolved);
            Assert.IsFalse(AdPrivacy.Owed);
        }

        // ------------------------------------------------------- the two moments
        /// <summary>
        /// <b>Nothing is put in front of a new player before the hub</b> (invariant 55). The
        /// splash's step only refreshes: the form owed in the EEA and Apple's prompt owed on a
        /// fresh iPhone are both left for the hub, and nothing that needs the answer has
        /// started - mediation included.
        /// </summary>
        [Test]
        public async Task TheSplashShowsANewPlayerNothing()
        {
            var log = new List<string>();
            var gateway = new FormGateway(log, Granted);
            var prompt = new RecordingPrompt(log, TrackingStatus.NotDetermined, TrackingStatus.Authorized);
            var provider = new RecordingProvider();
            AdPrivacy.Install(gateway);
            AdPrivacy.Install(prompt);
            RewardedAds.Install(provider);

            var starting = RewardedAds.StartAsync();
            await AdPrivacy.PrepareAsync();

            CollectionAssert.IsEmpty(log, "no form and no Apple prompt on the splash");
            Assert.IsEmpty(provider.Calls, "mediation waits for the form");
            Assert.IsFalse(AdPrivacy.IsResolved);
            Assert.IsTrue(AdPrivacy.Owed);

            var asking = AdPrivacy.AskAsync(Idle);
            gateway.Dismiss();
            await asking;
            await starting;

            CollectionAssert.AreEqual(new[] { "form shown", "form dismissed", "apple asked" }, log);
            CollectionAssert.AreEqual(new[] { "privacy", "init" }, provider.Calls);
            Assert.AreEqual(TrackingStatus.Authorized, provider.Last.Tracking,
                            "mediation is handed the answer as it stands once the hub has asked");
            Assert.IsFalse(AdPrivacy.Owed);
        }

        /// <summary>
        /// <b>A player who answered before is never asked again</b> - the property this update
        /// has to keep for the installed base. The CMP reports a stored answer as settled and
        /// iOS reports a stored tracking answer, so nothing is owed: the answer is committed on
        /// the splash, mediation starts there, and the hub has nothing to show. Every stored
        /// shape: consented, refused, outside the EEA, Android.
        /// </summary>
        [Test]
        public async Task APlayerWhoHasAnsweredIsNeverAskedAgain()
        {
            foreach (var (stored, tracking) in new[]
                     {
                         (Granted, TrackingStatus.Authorized),
                         (Denied, TrackingStatus.Denied),
                         (OutsideTheEea, TrackingStatus.Denied),
                         (OutsideTheEea, TrackingStatus.Restricted),
                         (Granted, TrackingStatus.NotSupported),
                     })
            {
                AdPrivacy.Reset();
                var log = new List<string>();
                var gateway = new FakeGateway(stored);
                var prompt = new RecordingPrompt(log, tracking, TrackingStatus.Authorized);
                var provider = new RecordingProvider();
                AdPrivacy.Install(gateway);
                AdPrivacy.Install(prompt);
                RewardedAds.Install(provider);

                await RewardedAds.StartAsync();

                Assert.IsTrue(AdPrivacy.IsResolved, $"{stored}: settled on the splash");
                Assert.IsFalse(AdPrivacy.Owed, $"{stored}: the hub has nothing to ask");
                CollectionAssert.AreEqual(new[] { "privacy", "init" }, provider.Calls);
                Assert.AreEqual(tracking, AdPrivacy.Signals.Tracking, "the stored answer is carried");

                await AdPrivacy.AskAsync(Idle);

                Assert.AreEqual(0, gateway.Asks, $"{stored}: no form");
                Assert.AreEqual(0, prompt.Requests, $"{stored}: no Apple prompt");
            }
        }

        /// <summary>
        /// Outside the EEA nothing is owed but Apple's prompt, so everything starts on the
        /// splash with the device id withheld, and the hub asks Apple and carries the answer to
        /// mediation as a change - which is the path a new iPhone player outside Europe takes.
        /// </summary>
        [Test]
        public async Task OutsideTheEeaEverythingStartsOnTheSplashAndAppleWaitsForTheHub()
        {
            var prompt = new RecordingPrompt(new List<string>(), TrackingStatus.NotDetermined,
                                             TrackingStatus.Authorized);
            var provider = new RecordingProvider();
            AdPrivacy.Install(new FakeGateway(OutsideTheEea));
            AdPrivacy.Install(prompt);
            RewardedAds.Install(provider);

            await RewardedAds.StartAsync();

            Assert.AreEqual(0, prompt.Requests, "Apple is not asked on the splash");
            CollectionAssert.AreEqual(new[] { "privacy", "init" }, provider.Calls);
            Assert.IsFalse(provider.Last.AllowsDeviceId, "and the device id is withheld until it is");
            Assert.IsTrue(AdPrivacy.Owed);

            await AdPrivacy.AskAsync(Idle);

            Assert.AreEqual(1, prompt.Requests);
            CollectionAssert.AreEqual(new[] { "privacy", "init", "privacy" }, provider.Calls);
            Assert.IsTrue(provider.Last.AllowsDeviceId);
            Assert.IsFalse(AdPrivacy.Owed);
        }

        /// <summary>
        /// A form that loaded after the player had left the hub is dropped unseen and asked
        /// again on their return - nothing committed meanwhile, nothing lost, and the question
        /// not quietly closed by a player who never saw it.
        /// </summary>
        [Test]
        public async Task AFormThePlayerWalkedAwayFromIsAskedOnTheirReturn()
        {
            var log = new List<string>();
            var gateway = new FormGateway(log, Granted);
            var prompt = new RecordingPrompt(log, TrackingStatus.NotDetermined, TrackingStatus.Denied);
            AdPrivacy.Install(gateway);
            AdPrivacy.Install(prompt);

            await AdPrivacy.PrepareAsync();
            await AdPrivacy.AskAsync(Away);

            CollectionAssert.IsEmpty(log, "nothing was laid over a run");
            Assert.IsFalse(AdPrivacy.IsResolved);
            Assert.IsTrue(AdPrivacy.Owed, "still owed");

            var asking = AdPrivacy.AskAsync(Idle);
            gateway.Dismiss();
            await asking;

            CollectionAssert.AreEqual(new[] { "form shown", "form dismissed", "apple asked" }, log);
            Assert.IsTrue(AdPrivacy.Signals.AllowsPersonalisation);
            Assert.IsFalse(AdPrivacy.Owed);
        }

        /// <summary>
        /// The player leaves the hub in the moment between the form and Apple's prompt: the
        /// consent answer is committed at once, so everything waiting on it starts, and Apple
        /// stays owed for their return rather than landing on a run.
        /// </summary>
        [Test]
        public async Task ApplesPromptWaitsForTheHubWhenThePlayerLeavesAfterTheForm()
        {
            var log = new List<string>();
            bool idle = true;
            var gateway = new FormGateway(log, Granted) { OnDismiss = () => idle = false };
            var prompt = new RecordingPrompt(log, TrackingStatus.NotDetermined, TrackingStatus.Denied);
            AdPrivacy.Install(gateway);
            AdPrivacy.Install(prompt);

            await AdPrivacy.PrepareAsync();
            var asking = AdPrivacy.AskAsync(() => idle);
            gateway.Dismiss();
            await asking;

            Assert.AreEqual(0, prompt.Requests);
            Assert.IsTrue(AdPrivacy.IsResolved, "the consent half is committed");
            Assert.IsTrue(AdPrivacy.Owed, "Apple's half is still owed");

            idle = true;
            await AdPrivacy.AskAsync(() => idle);

            Assert.AreEqual(1, gateway.Shows, "the form is not shown twice");
            Assert.AreEqual(1, prompt.Requests);
            Assert.AreEqual(TrackingStatus.Denied, AdPrivacy.Signals.Tracking);
            Assert.IsFalse(AdPrivacy.Owed);
        }

        /// <summary>
        /// The hub's step cannot be started twice at once - the poll reads <c>Owed</c> every
        /// frame, and two forms in flight would be two forms on screen.
        /// </summary>
        [Test]
        public async Task TheHubAsksOneQuestionAtATime()
        {
            var gateway = new FormGateway(new List<string>(), Granted);
            AdPrivacy.Install(gateway);

            await AdPrivacy.PrepareAsync();
            var first = AdPrivacy.AskAsync(Idle);

            Assert.IsFalse(AdPrivacy.Owed, "not owed while it is being asked");
            await AdPrivacy.AskAsync(Idle);

            gateway.Dismiss();
            await first;

            Assert.AreEqual(1, gateway.Shows);
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

            await AdPrivacy.PrepareAsync();
            var asking = AdPrivacy.AskAsync(Idle);

            Assert.AreEqual(0, prompt.Requests, "the form is on screen; Apple has not been asked");
            gateway.Dismiss();
            await asking;

            CollectionAssert.AreEqual(new[] { "form shown", "form dismissed", "apple asked" }, log);
            Assert.AreEqual(TrackingStatus.Denied, AdPrivacy.Signals.Tracking);
        }

        /// <summary>
        /// A consent question left open - the CMP unreachable, the form unloadable - does not
        /// spend Apple's one question on this launch. If it did, the form would come *after*
        /// the tracking dialog on the next launch, which is the order Apple refuses. The
        /// status is still read, so a device that answered on an earlier launch carries it.
        /// </summary>
        [Test]
        public async Task AnOpenConsentQuestionLeavesApplesPromptForALaterLaunch()
        {
            var log = new List<string>();
            var prompt = new RecordingPrompt(log, TrackingStatus.NotDetermined, TrackingStatus.Authorized);
            AdPrivacy.Install(new FakeGateway(AdPrivacySignals.Restricted));
            AdPrivacy.Install(prompt);

            await AdPrivacy.PrepareAsync();
            await AdPrivacy.AskAsync(Idle);

            Assert.AreEqual(0, prompt.Requests, "an open question asks Apple nothing");
            Assert.AreEqual(TrackingStatus.NotDetermined, AdPrivacy.Signals.Tracking);
            Assert.IsTrue(AdPrivacy.IsResolved, "and the game still starts");
            Assert.IsFalse(AdPrivacy.Owed, "and the launch does not ask in a loop");
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

            await AdPrivacy.PrepareAsync();
            await AdPrivacy.AskAsync(Idle);

            Assert.AreEqual(0, prompt.Requests);
        }

        /// <summary>
        /// Where no form is owed the question is closed without one, and Apple is asked on the
        /// hub of the first launch - a player outside the EEA must not lose the prompt to a gate
        /// written for the EEA. Both spellings a CMP can answer with: "does not apply", and
        /// "applies and answered".
        /// </summary>
        [Test]
        public async Task ASettledConsentAnswerAsksAppleOnTheHubOfTheSameLaunch()
        {
            foreach (var settled in new[]
                     {
                         new AdPrivacySignals(false, ConsentStatus.Unknown, false, false, TrackingStatus.NotDetermined),
                         OutsideTheEea,
                         Granted,
                         Denied,
                     })
            {
                AdPrivacy.Reset();
                var prompt = new RecordingPrompt(new List<string>(), TrackingStatus.NotDetermined,
                                                 TrackingStatus.Authorized);
                AdPrivacy.Install(new FakeGateway(settled));
                AdPrivacy.Install(prompt);

                await AdPrivacy.PrepareAsync();
                Assert.AreEqual(0, prompt.Requests, $"{settled}: not on the splash");

                await AdPrivacy.AskAsync(Idle);

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
        /// A commit raises the event and sets <c>IsResolved</c> on the line after it, so for the duration of the one callback that carries the answer the flag still
        /// says the answer has not arrived. A handler that asks it returns early and is never
        /// called again, because consent is answered once.
        /// </para>
        /// <para>
        /// That is not a bug to be fixed here - moving the assignment would make <c>Commit</c>'s
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
                AdPrivacy.PrepareAsync().GetAwaiter().GetResult();
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
        /// refusal in the EEA would be indistinguishable from never having been asked. It is
        /// the hub that commits it - a CMP that cannot be reached leaves the splash with an
        /// open question - so the hub is what is driven here.
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
                AdPrivacy.PrepareAsync().GetAwaiter().GetResult();
                AdPrivacy.AskAsync(Idle).GetAwaiter().GetResult();
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

        /// <summary>What UMP answers outside the EEA and the UK: no form owed.</summary>
        static AdPrivacySignals OutsideTheEea => new AdPrivacySignals(
            false, ConsentStatus.Granted, false, false, TrackingStatus.NotSupported);

        /// <summary>The player is standing on an idle hub.</summary>
        static bool Idle() => true;

        /// <summary>The player has walked into a run, or a panel is open.</summary>
        static bool Away() => false;

        /// <summary>
        /// A CMP holding a stored answer: the splash's refresh reports it, and the hub's ask
        /// shows nothing and reports it again.
        /// </summary>
        sealed class FakeGateway : IConsentGateway
        {
            readonly AdPrivacySignals _resolved;

            public FakeGateway(AdPrivacySignals resolved) { _resolved = resolved; }

            public AdPrivacySignals Revisited;
            public int Refreshes;
            public int Asks;

            public Task<AdPrivacySignals> RefreshAsync(CancellationToken cancellation = default)
            {
                Refreshes++;
                return Task.FromResult(_resolved);
            }

            public Task<ConsentAsk> AskAsync(System.Func<bool> mayPresent,
                                             CancellationToken cancellation = default)
            {
                Asks++;
                return Task.FromResult(ConsentAsk.Answered(_resolved));
            }

            public bool CanRevisit => true;

            public Task<AdPrivacySignals> RevisitAsync(CancellationToken cancellation = default)
                => Task.FromResult(Revisited);
        }

        sealed class ThrowingGateway : IConsentGateway
        {
            public Task<AdPrivacySignals> RefreshAsync(CancellationToken cancellation = default)
                => throw new System.InvalidOperationException("the CMP is unreachable");

            public Task<ConsentAsk> AskAsync(System.Func<bool> mayPresent,
                                             CancellationToken cancellation = default)
                => throw new System.InvalidOperationException("the CMP is unreachable");

            public bool CanRevisit => false;

            public Task<AdPrivacySignals> RevisitAsync(CancellationToken cancellation = default)
                => throw new System.InvalidOperationException("the CMP is unreachable");
        }

        /// <summary>
        /// A new player in the EEA: the refresh says a form is owed, and the hub's ask shows it
        /// and does not return until <see cref="Dismiss"/> - the contract the real gateway
        /// keeps. Honours <c>mayPresent</c> the way the real one does, by dropping the form
        /// unseen.
        /// </summary>
        sealed class FormGateway : IConsentGateway
        {
            readonly List<string> _log;
            readonly AdPrivacySignals _answer;
            readonly TaskCompletionSource<bool> _dismissed =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            bool _answered;

            public FormGateway(List<string> log, AdPrivacySignals answer)
            {
                _log = log;
                _answer = answer;
            }

            /// <summary>Runs as the form closes, before the gateway returns - the player's next move.</summary>
            public System.Action OnDismiss;

            public int Shows;

            public Task<AdPrivacySignals> RefreshAsync(CancellationToken cancellation = default)
                => Task.FromResult(_answered ? _answer : AdPrivacySignals.Restricted);

            public async Task<ConsentAsk> AskAsync(System.Func<bool> mayPresent,
                                                   CancellationToken cancellation = default)
            {
                if (_answered) return ConsentAsk.Answered(_answer);
                if (!mayPresent()) return ConsentAsk.NotNow;

                Shows++;
                _log.Add("form shown");
                await _dismissed.Task;
                _log.Add("form dismissed");
                OnDismiss?.Invoke();

                _answered = true;
                return ConsentAsk.Answered(_answer);
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
