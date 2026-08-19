using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// Keeps the local save and the server in step.
    ///
    /// One cycle, always the same three steps: pull what the server has, join it with
    /// what this device has, push the result. Never "download and replace", never
    /// "upload and replace", and never a prompt asking the player which of their two
    /// saves to delete — <see cref="SaveMerge"/> exists precisely so that question does
    /// not have to be asked.
    ///
    /// Nothing here is ever on the boot path. A sync runs in the background after the
    /// splash and again when the app is backgrounded, and a failure is a logged warning
    /// rather than anything the player sees. A puzzle game that will not start because
    /// a server is unreachable has traded a real failure for an imaginary one.
    /// </summary>
    public static class CloudSaveService
    {
        static ICloudSaveBackend _backend = new NullCloudBackend();
        static int _syncing;

        static readonly SyncScheduler _schedule = new SyncScheduler();

        /// <summary>Raised after a sync changes the local save, so screens can repaint.</summary>
        public static event Action Synced;

        public static bool IsAvailable => _backend != null && _backend.IsAvailable;

        public static bool IsSyncing => Volatile.Read(ref _syncing) != 0;

        /// <summary>
        /// True only once a permanent provider is attached — never for an anonymous
        /// account, however well it is syncing.
        ///
        /// Worth stating because the mistake is easy and the consequence is not: an
        /// anonymous account has a uid and syncs happily, so anything keying on "is
        /// signed in" will cheerfully tell a guest their progress is protected right up
        /// until they reinstall and it is gone.
        /// </summary>
        public static bool IsLinked => IsAvailable && _backend.CurrentIdentity.IsLinked;

        public static long LastSyncedUnix => CloudState.LastSyncedUnix;

        /// <summary>
        /// True while this device is authenticated as one account and holding another's save.
        ///
        /// <para>
        /// Nothing syncs in this state and nothing may — see <see cref="AccountGate"/> — so it
        /// has to be visible rather than logged. A player here is signed in, so every screen
        /// that reads <see cref="IsLinked"/> would otherwise tell them their grove is backed
        /// up while it is not being backed up at all, which is the one lie this game's account
        /// screens exist to avoid. It clears the moment the two agree again, which is one tap
        /// of whichever provider they use.
        /// </para>
        /// </summary>
        public static bool AccountMismatched { get; private set; }

        /// <summary>
        /// Whether this device holds a grove somebody would mind losing.
        ///
        /// <para>
        /// Asked before a destructive prompt, so that the one place in the game that shows one
        /// does not show it over an empty grove. That is not politeness: a player who has just
        /// signed out, or who has just installed the game to get their account back, meets that
        /// prompt at exactly the moment it is least true, and a warning that cries wolf on a
        /// grove with nothing in it is a warning nobody reads on the grove that has everything.
        /// </para>
        /// <para>
        /// Cleared glades are the headline, and the three purchased sets are here because they
        /// are the parts that are <em>not</em> recoverable by playing again — everything else in
        /// the save is derived from the star ledger and comes back with it.
        /// </para>
        /// </summary>
        public static bool HoldsAGrove
            => PlayerProgression.ClearedGlades > 0
            || Progression.CompanionLedger.BoughtCount > 0
            || Homestead.HomesteadLedger.BoughtCount > 0
            || Homestead.GroveLand.BoughtCount > 0;

        /// <summary>Chosen once, in <c>Boot</c>, before anything asks for a sync.</summary>
        public static void UseBackend(ICloudSaveBackend backend)
            => _backend = backend ?? new NullCloudBackend();

        // ------------------------------------------------------------ the latch
        /// <summary>
        /// Claims the sync latch. Held by a sync, and equally by anything that changes
        /// <em>which account this device is</em> — linking a provider, or adopting the
        /// account one already belongs to.
        ///
        /// <para>
        /// Syncs have always excluded each other. Nothing excluded a sync from an
        /// identity change, and the OAuth consent screen makes that collision routine
        /// rather than rare: opening it backgrounds the app and returning foregrounds it,
        /// so <c>Boot.Pump.OnApplicationPause</c> fires <see cref="BeginSync"/> at the
        /// exact moment the sign-in completes and <see cref="CloudState"/> is being
        /// rewritten. The sync then addresses one account holding the other's
        /// credentials, and the rules refuse it — <i>both</i> directions, since
        /// <c>isOwner(uid)</c> gates reads too.
        /// </para>
        ///
        /// <para>
        /// Observed live on 2026-08-13: a burst of PERMISSION_DENIED on every pull and
        /// push immediately after adopting an account, clearing itself once the racing
        /// cycle finished. Nothing was lost, because the identity change had already
        /// committed and the next clean sync agreed with it — but it logged at error
        /// level, which this file reserves for a write the client believed was valid.
        /// </para>
        /// </summary>
        static bool TryClaim() => Interlocked.CompareExchange(ref _syncing, 1, 0) == 0;

        static void Release() => Volatile.Write(ref _syncing, 0);

        /// <summary>
        /// Waits for the latch rather than refusing the moment it is busy.
        ///
        /// A sync starts every time the app is foregrounded, which is precisely when a
        /// player opens the account screen and taps a provider — so failing fast here
        /// would turn "a background sync happened to be in flight" into "something went
        /// wrong", which is the exact failure this whole change set exists to remove. A
        /// cycle is one pull and one push, so the wait is short and the alternative is
        /// worse. The timeout only exists so a wedged sync cannot make the button dead
        /// forever; reporting it is honest, and the player can tap again.
        /// </summary>
        static async Task<bool> ClaimAsync(CancellationToken cancellation)
        {
            const int TimeoutMs = 10000;
            const int PollMs = 50;

            for (int waited = 0; waited < TimeoutMs; waited += PollMs)
            {
                if (TryClaim()) return true;
                await Task.Delay(PollMs, cancellation);
            }

            return TryClaim();
        }

        // ---------------------------------------------------------------- sync
        /// <summary>
        /// Runs a sync and forgets about it. The result lands in the save file; nothing
        /// in the running session waits on it or has to handle its failure.
        /// </summary>
        public static void BeginSync(CancellationToken cancellation = default)
        {
            if (!IsAvailable) return;
            _ = SyncAsync(cancellation);
        }

        // -------------------------------------------------------- the scheduler
        /// <summary>
        /// Asks for the server to be brought up to date shortly, rather than now.
        ///
        /// <para>
        /// For changes a player makes deliberately and expects to keep — their name,
        /// their companion. Everything else rides the next background sync, which is
        /// right for progress that will change again in a minute anyway and wrong for a
        /// choice made once. Debounced, so a burst is one write, and retried with a
        /// backoff, so making the change on a train still reaches the server when the
        /// train leaves the tunnel. See <see cref="SyncScheduler"/>.
        /// </para>
        /// <para>
        /// Cheap and safe to call from anywhere. It sets a flag; nothing here reaches the
        /// network until <see cref="Tick"/> says so, and a request made while a sync is
        /// already in flight survives that sync rather than being swallowed by it.
        /// </para>
        /// </summary>
        public static void RequestSync() => _schedule.Request();

        /// <summary>
        /// Drives the scheduler. Called every frame by <c>Boot.Pump</c>, which is also
        /// where connectivity is read — <c>Application.internetReachability</c> is a
        /// Presentation-side concern, and the policy is deliberately testable without it.
        ///
        /// <para>
        /// <paramref name="deltaSeconds"/> is elapsed time handed in a frame at a time,
        /// never two readings of a wall clock, for the reason <c>RunClock</c> gives: the
        /// device's clock can jump — a timezone, an NTP correction, a player winding it
        /// forward for a daily chest — and a retry timer driven by one would either fire
        /// in a storm or never fire again.
        /// </para>
        /// </summary>
        public static void Tick(float deltaSeconds, bool networkReachable)
        {
            if (!IsAvailable) return;

            _schedule.NetworkChanged(networkReachable);
            if (!_schedule.Tick(deltaSeconds)) return;

            _ = RunScheduledSyncAsync();
        }

        /// <summary>
        /// Clears any backoff, because something has happened that plausibly fixes
        /// whatever the last failure was — the app being foregrounded, or an account
        /// being linked. Does not itself ask for a sync; the caller is about to.
        /// </summary>
        public static void ResetBackoff() => _schedule.Settled();

        static async Task RunScheduledSyncAsync()
        {
            var result = await SyncAsync();

            // The sync holding the latch may have taken its snapshot before the change
            // this attempt was about, so the work is still owed and is asked for again.
            // That sync reports its own outcome, which is what clears the in-flight mark.
            if (result.Failure == CloudFailure.Busy) _schedule.Request();
        }

        /// <summary>
        /// Fetches the population's move counts and forgets about it.
        ///
        /// <para>
        /// Separate from the sync and deliberately so. It needs no sign-in, it writes
        /// nothing, it touches no save file, and nothing anywhere waits on it — the worst
        /// case of it never completing is one sentence not appearing on a victory panel.
        /// Folding it into <see cref="SyncAsync"/> would put a read nobody needs on the
        /// critical path of the one operation a player's progress depends on.
        /// </para>
        /// </summary>
        public static void BeginStatsRefresh(CancellationToken cancellation = default)
        {
            if (!IsAvailable) return;
            _ = RefreshStatsAsync(cancellation);
        }

        /// <summary>
        /// Reads the published stats and publishes them to <see cref="Social.GroveStats"/>.
        ///
        /// A failure is not reported anywhere and does not need to be: an empty table and
        /// a table that never arrived produce identical behaviour everywhere they are read.
        /// </summary>
        public static async Task<CloudResult> RefreshStatsAsync(CancellationToken cancellation = default)
        {
            if (!IsAvailable) return CloudResult.Failed(CloudFailure.Offline, "no cloud backend");

            var (result, stats) = await _backend.ReadGroveStatsAsync(cancellation);
            if (result.Ok) Social.GroveStats.Publish(stats);

            return result;
        }

        /// <summary>
        /// Pull, join, push. Safe to call at any time: a second call while one is in
        /// flight returns immediately rather than racing it, because two syncs merging
        /// the same file concurrently would each push a snapshot missing the other's
        /// work.
        /// </summary>
        public static async Task<CloudResult> SyncAsync(CancellationToken cancellation = default)
        {
            var result = await RunOnceAsync(cancellation);

            // Every sync feeds the scheduler, however it was started. A failure is exactly
            // what the retry exists for — the sync fired when the app was backgrounded is
            // the one most likely to be interrupted, and before this nothing ever tried it
            // again — and a success is the only thing that clears a backoff. Contention is
            // neither, and is the one outcome that must not be counted.
            if (result.Ok) _schedule.Succeeded();
            else if (result.Failure != CloudFailure.Busy) _schedule.Failed();

            return result;
        }

        // ---------------------------------------------------------- the identity
        /// <summary>
        /// Establishes that the session and the save agree about who this player is, and
        /// refuses everything if they do not.
        ///
        /// <para>
        /// The decision is <see cref="AccountGate.Decide"/> — a pure function, tested offline,
        /// deliberately not inlined here. What this adds is only the plumbing: which call to
        /// make for each verdict, and the rule that the answer is re-decided against whatever
        /// that call returns rather than assumed.
        /// </para>
        /// <para>
        /// Note which sign-in is used where. A save that names nobody may
        /// <see cref="ICloudSaveBackend.SignInAsync"/>, which will create an anonymous account
        /// — that is a first launch, and it is the only moment in the life of an account when
        /// creating one is right. A save that already names somebody may only
        /// <see cref="ICloudSaveBackend.ResumeAsync"/>, which creates nobody. The two used to
        /// be one call, and the difference is a player's grove.
        /// </para>
        /// </summary>
        static async Task<CloudResult> AuthoriseAsync(CancellationToken cancellation)
        {
            switch (AccountGate.Decide(CloudState.UserId, _backend.CurrentIdentity.UserId))
            {
                case AccountGateVerdict.Proceed:
                    return Agreed();

                case AccountGateVerdict.Adopt:
                    CloudState.SignIn(_backend.CurrentIdentity.UserId);
                    return Agreed();

                case AccountGateVerdict.Refuse:
                    return Disagreed(_backend.CurrentIdentity.UserId);
            }

            bool owned = CloudState.IsSignedIn;

            var (result, identity) = owned ? await _backend.ResumeAsync(cancellation)
                                           : await _backend.SignInAsync(cancellation);
            if (!result.Ok) return result;

            // An empty answer from Resume is not a failure of the call — the SDK is up and
            // nobody is signed in — but it is a refusal of this sync, because the account the
            // save belongs to is not available to push to. Retryable, and the account screen's
            // provider buttons are the way a player fixes it deliberately.
            if (!identity.IsValid)
                return CloudResult.Failed(CloudFailure.Unauthenticated,
                                          owned ? "the save's account is not signed in" : "no user id");

            if (AccountGate.Decide(CloudState.UserId, identity.UserId) == AccountGateVerdict.Refuse)
                return Disagreed(identity.UserId);

            CloudState.SignIn(identity.UserId);      // a no-op when they already agree
            return Agreed();
        }

        static CloudResult Agreed()
        {
            AccountMismatched = false;
            return CloudResult.Success;
        }

        /// <summary>
        /// Records that this device is between two accounts, and says so once.
        ///
        /// Logged as a warning rather than an error, and once rather than per attempt: it is a
        /// state the player can be in for hours without anything being wrong with the code, and
        /// a sync fires on every foreground.
        /// </summary>
        static CloudResult Disagreed(string sessionUserId)
        {
            if (!AccountMismatched)
                Debug.LogWarning("[Cloud] this device is signed in as a different account than " +
                                 "the save belongs to; syncing is stopped until they agree");

            AccountMismatched = true;
            return CloudResult.Failed(CloudFailure.AccountMismatch,
                                      "save belongs to another account than the session");
        }

        static async Task<CloudResult> RunOnceAsync(CancellationToken cancellation)
        {
            if (!IsAvailable) return CloudResult.Failed(CloudFailure.Offline, "no cloud backend");
            if (!SaveService.IsLoaded) return CloudResult.Failed(CloudFailure.Error, "save not loaded");

            if (!TryClaim())
                return CloudResult.Failed(CloudFailure.Busy, "a sync is already running");

            // Anything asked for before this point is about to be sent, so the request is
            // consumed here. One arriving after it — a rename made while the push is in
            // flight — survives, because it is a fresh request against a snapshot that
            // never contained it.
            _schedule.Started();

            try
            {
                return await RunSyncAsync(cancellation);
            }
            catch (OperationCanceledException)
            {
                return CloudResult.Failed(CloudFailure.Offline, "cancelled");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Cloud] sync failed: " + e.Message);
                return CloudResult.Failed(CloudFailure.Error, e.Message);
            }
            finally
            {
                Release();
            }
        }

        static async Task<CloudResult> RunSyncAsync(CancellationToken cancellation)
        {
            // Before anything is read and long before anything is written. Every path into
            // this file goes through here for the reason AccountGate gives: a pull followed by
            // a monotonic join followed by a push is, addressed to the wrong account, a way to
            // merge two strangers' groves and overwrite one of them.
            var authorised = await AuthoriseAsync(cancellation);
            if (!authorised.Ok) return authorised;

            string userId = CloudState.UserId;

            var (pull, snapshot) = await _backend.PullAsync(userId, cancellation);
            if (!pull.Ok && pull.Failure != CloudFailure.Rejected) return pull;

            // The local snapshot is taken after the pull, so anything the player did
            // while the request was in flight is included rather than overwritten.
            var local = SaveService.Snapshot();

            SaveFileDto merged = local;
            SaveFileDto remote = null;

            if (snapshot != null && snapshot.Exists && snapshot.Save != null)
            {
                remote = snapshot.Save;
                merged = SaveMerge.Join(local, remote);

                // Adopt before pushing: if the push fails, the device still keeps
                // everything the server knew, and the next sync retries from there.
                SaveService.Adopt(merged);
                PlayerProgression.Invalidate();
            }

            var delta = SaveDelta.Between(remote, merged);

            if (delta.IsEmpty)
            {
                // Both sides already agree. Writing anyway would burn a document write
                // and a chunk of the player's data to say nothing, and backgrounding the
                // app is by far the most common moment a sync runs.
                await ReconcileWalletAsync(userId, cancellation);
                CloudState.MarkSynced(SaveSchema.NowUnix());
                SaveService.Flush();
                Raise(Synced);
                return CloudResult.Success;
            }

            var push = await _backend.PushAsync(userId, merged, delta, cancellation);
            if (!push.Ok) return push;

            await ReconcileWalletAsync(userId, cancellation);

            CloudState.MarkSynced(SaveSchema.NowUnix());
            SaveService.Flush();

            Raise(Synced);
            return CloudResult.Success;
        }

        // -------------------------------------------------------------- linking
        /// <summary>
        /// Turns the anonymous account into a permanent one, keeping its progress.
        ///
        /// Worth offering after a chapter is cleared rather than on first launch. An
        /// anonymous account lives and dies with the installation, so a player who
        /// reinstalls before linking loses everything they had — and a player asked to
        /// sign in before they have played anything mostly declines, which leaves them
        /// exposed to exactly that.
        ///
        /// A sync runs immediately afterwards so the linked account's document exists
        /// before the player closes the game.
        /// </summary>
        public static async Task<CloudResult> LinkAsync(
            LinkCredential credential, CancellationToken cancellation = default)
        {
            if (!IsAvailable) return CloudResult.Failed(CloudFailure.Offline, "no cloud backend");

            if (!await ClaimAsync(cancellation))
                return CloudResult.Failed(CloudFailure.Error, "a sync is already running");

            try
            {
                // Before the provider is touched, not after, and the ordering is the whole
                // fix.
                //
                // Linking attaches a provider to *whichever account the session happens to
                // be*, and the backend will happily create an anonymous one to attach it to
                // if the session has gone. On a save that already names an account that is
                // the leak this file exists to prevent, wearing a friendly name: the grove
                // would be re-owned by the new account and pushed into it on the next sync.
                // It is reachable — a linked player whose session is lost reads as a guest
                // (IsLinked asks the SDK, which has nobody), so the panel offers exactly
                // this button.
                //
                // Refusing after the call would be too late in a way that cannot be undone
                // from here: the provider would already be attached to the junk account, so
                // the player's Apple ID would belong to an empty grove for ever. Authorising
                // first means the junk account is never created and the provider is never
                // moved. AccountGate's answer for (owned save, no session) is Resume, which
                // creates nobody and reports honestly, and the player's route back is the
                // account panel — which is where they already are.
                var authorised = await AuthoriseAsync(cancellation);
                if (!authorised.Ok) return authorised;

                var (result, identity) = await _backend.LinkAsync(credential, cancellation);

                // Not a fault: the player linked this provider on another device. The
                // caller has to ask before anything can be done about it, because the
                // answer costs one of the two accounts.
                if (result.Failure == CloudFailure.AlreadyLinkedElsewhere) return result;
                if (!result.Ok) return result;

                // Linking keeps the uid — that is the entire difference between linking and
                // signing in — so this agrees with the save in every case that is working
                // properly, and CloudState.SignIn is a no-op. It is checked anyway because
                // the one case where it would not agree is the one that costs a grove, and
                // a belt here is one line.
                if (identity.IsValid)
                {
                    if (AccountGate.Decide(CloudState.UserId, identity.UserId) == AccountGateVerdict.Refuse)
                        return Disagreed(identity.UserId);

                    CloudState.SignIn(identity.UserId);
                }

                SaveService.Save();
            }
            finally
            {
                Release();
            }

            // Released first: SyncAsync claims the latch itself, and by this point the
            // identity has settled, so a sync arriving now — including the one the
            // consent screen's foreground fires — sees a consistent account.
            await SyncAsync(cancellation);
            return CloudResult.Success;
        }

        /// <summary>
        /// Makes this device a different account, keeping the grove it is leaving.
        ///
        /// <para>
        /// <b>The one thing this does that adopting does not is the first thing it does:</b> it
        /// pushes the outgoing grove to the server and refuses to go any further if that push
        /// does not land. A switch is a reversible act — the player signs back in and their
        /// grove is there — and it is only reversible because of that step. Without it, "switch
        /// account" is "discard whatever this device has played since its last sync", which is
        /// a very different button wearing the same word.
        /// </para>
        /// <para>
        /// Offered from a linked account. A guest has no way back into what they are leaving,
        /// so for them this is <see cref="AdoptLinkedAccountAsync"/>, which asks first.
        /// </para>
        /// <para>
        /// Signing in with the account already on this device is not an error and not a no-op
        /// worth hiding: it reports <see cref="SwitchOutcome.SameAccount"/> and is exactly how
        /// a device recovers from a switch that was interrupted — see
        /// <see cref="SwitchOutcome.Interrupted"/>.
        /// </para>
        /// </summary>
        public static Task<SwitchResult> SwitchAccountAsync(
            LinkCredential credential, CancellationToken cancellation = default)
            => BecomeAsync(credential, BecomeMode.Switch, cancellation);

        /// <summary>
        /// Lets a device that is between two accounts back in to the one its save belongs to.
        ///
        /// <para>
        /// The recovery from <see cref="SwitchOutcome.Interrupted"/>, and the reason it is a
        /// third entry point rather than a flag is that both of the others would be wrong here.
        /// Switching cannot secure the outgoing grove — the server will not accept a write for
        /// an account the session is not, which is the very state being recovered from — so it
        /// would report a failure the player cannot act on. Adopting would take the credential
        /// at face value and replace the local grove, which is fine when somebody has been told
        /// what it costs and catastrophic as the response to a button that says "sign in".
        /// </para>
        /// <para>
        /// So this becomes the account only if it is <em>already</em> this device's account,
        /// and otherwise reports <see cref="SwitchOutcome.DifferentAccount"/> having touched
        /// nothing. Succeeding restores syncing, which secures everything played meanwhile;
        /// only then is switching to a different account a lossless thing to offer.
        /// </para>
        /// </summary>
        public static Task<SwitchResult> ResumeAccountAsync(
            LinkCredential credential, CancellationToken cancellation = default)
            => BecomeAsync(credential, BecomeMode.Resume, cancellation);

        /// <summary>
        /// Abandons this device's account and adopts the one the provider already owns.
        ///
        /// <b>Destructive, and only callable after the player has been asked.</b> Only
        /// reachable when <see cref="LinkAsync"/> reported
        /// <see cref="CloudFailure.AlreadyLinkedElsewhere"/>.
        ///
        /// <para>
        /// The local save is replaced rather than merged, and that is deliberate. Merging
        /// the two would look generous — the glades really were cleared by this person —
        /// but the wallets cannot come with it: currency was granted and spent separately
        /// against each account, and folding one device's unconfirmed debits into the
        /// other's balance charges a player for something they already paid for. Merging
        /// progress while discarding the wallet is possible, and is a feature to add
        /// deliberately rather than a default to arrive at by accident, because the
        /// stored progression floors would carry across with it and inflate a level the
        /// adopted account's records do not justify.
        /// </para>
        /// <para>
        /// Nothing is secured on the way out, unlike <see cref="SwitchAccountAsync"/>, and that
        /// is not an oversight. The account being left here is an anonymous one; pushing its
        /// grove would file it under a uid that is about to become unreachable for ever, which
        /// costs a round trip to achieve nothing. The player was told what it costs and said
        /// yes — that is the whole difference between this and a switch.
        /// </para>
        /// </summary>
        public static Task<SwitchResult> AdoptLinkedAccountAsync(
            LinkCredential credential, CancellationToken cancellation = default)
            => BecomeAsync(credential, BecomeMode.Adopt, cancellation);

        /// <summary>
        /// Which of the three reasons a device has for becoming a different account. They
        /// differ only in what is owed to the grove being left behind, which is exactly the
        /// decision worth naming rather than passing as a bare flag.
        /// </summary>
        enum BecomeMode
        {
            /// <summary>Deliberate, from a linked account. The outgoing grove is saved first.</summary>
            Switch,

            /// <summary>Consented, from a guest. The outgoing grove is abandoned; see the caller.</summary>
            Adopt,

            /// <summary>Recovery. Proceeds only if the credential names the account already held.</summary>
            Resume,
        }

        /// <summary>
        /// Becomes the account a credential names.
        ///
        /// <para>
        /// One method for both routes in, because they differ in exactly one decision — whether
        /// the outgoing grove is saved first — and everything after that decision is the part
        /// where a mistake loses somebody's account. Two copies of it would be two chances to
        /// get the order below wrong.
        /// </para>
        /// <para>
        /// <b>The order is the design: secure, authenticate, fetch, replace.</b> Nothing local
        /// is destroyed until the replacement is in hand. Every earlier arrangement fails
        /// somewhere real — wiping before the pull leaves a player staring at an empty grove
        /// whenever the network drops between two calls, and replacing before authenticating
        /// gives that away to a consent screen they simply closed. Each step is also its own
        /// answer in <see cref="SwitchOutcome"/>, so the screen can say which one stopped and
        /// whether anything moved.
        /// </para>
        /// <para>
        /// The latch is held across the identity change rather than only across a sync, so a
        /// background sync cannot run against a half-switched device. It is taken <em>after</em>
        /// the securing sync, because that sync claims it itself.
        /// </para>
        /// </summary>
        static async Task<SwitchResult> BecomeAsync(
            LinkCredential credential, BecomeMode mode, CancellationToken cancellation)
        {
            if (!IsAvailable)
                return SwitchResult.Failed(SwitchOutcome.Refused, CloudFailure.Offline, "no cloud backend");
            if (!SaveService.IsLoaded)
                return SwitchResult.Failed(SwitchOutcome.Refused, CloudFailure.Error, "save not loaded");
            if (!credential.IsValid)
                return SwitchResult.Failed(SwitchOutcome.Refused, CloudFailure.Rejected, "no provider named");

            // ------------------------------------------------------------------- secure
            // A grove that is already on the server needs nothing; SyncAsync says so cheaply,
            // pushing only what SaveDelta finds changed and often nothing at all.
            if (mode == BecomeMode.Switch && CloudState.IsSignedIn)
            {
                var secured = await SyncAsync(cancellation);
                if (!secured.Ok)
                    return SwitchResult.Failed(SwitchOutcome.NotSecured, secured);
            }

            if (!await ClaimAsync(cancellation))
                return SwitchResult.Failed(SwitchOutcome.Refused, CloudFailure.Busy, "a sync is already running");

            SwitchOutcome outcome;

            try
            {
                string outgoing = CloudState.UserId;

                // ------------------------------------------------------------ authenticate
                var (signIn, identity) = await _backend.SignInWithCredentialAsync(credential, cancellation);
                if (!signIn.Ok) return SwitchResult.Failed(SwitchOutcome.Refused, signIn);
                if (!identity.IsValid)
                    return SwitchResult.Failed(SwitchOutcome.Refused, CloudFailure.Unauthenticated, "no user id");

                // Already here. Nothing is touched — and this is the branch that makes an
                // interrupted switch recoverable, so it must come before anything destructive
                // and must never be "helpfully" turned into a refresh.
                if (string.Equals(identity.UserId, outgoing, StringComparison.Ordinal))
                {
                    AccountMismatched = false;
                    return SwitchResult.Done(SwitchOutcome.SameAccount);
                }

                // Recovery asked to be let back in, and this is somebody else. Stop here with
                // the save untouched — see SwitchOutcome.DifferentAccount for why that is the
                // only safe answer and what the player is offered instead.
                if (mode == BecomeMode.Resume)
                {
                    AccountMismatched = true;
                    return SwitchResult.Failed(SwitchOutcome.DifferentAccount,
                                               CloudFailure.AccountMismatch,
                                               "the credential names a different account");
                }

                // ------------------------------------------------------------------ fetch
                // Read before write. From here the session is somebody else while the file on
                // disk is still the old account, which is precisely the state AccountGate
                // refuses — so a failure now costs a retry and cannot cost a grove.
                var (pull, snapshot) = await _backend.PullAsync(identity.UserId, cancellation);
                if (!pull.Ok)
                {
                    AccountMismatched = true;
                    return SwitchResult.Failed(SwitchOutcome.Interrupted, pull);
                }

                bool exists = snapshot != null && snapshot.Exists && snapshot.Save != null;

                // ---------------------------------------------------------------- replace
                // The marker for a run this device left in flight belongs to the player who
                // was playing it. Charging the incoming account a heart for a run it never
                // started is small, silent and impossible to explain afterwards.
                RunGuard.Resolve();

                // Forgetting the account matters even though the next line names a new one: a
                // process death in the gap leaves a file owned by nobody, which the gate can
                // adopt safely, rather than an empty grove wearing the outgoing player's uid.
                SaveService.Wipe(forgetAccount: true);
                CloudState.SignIn(identity.UserId);

                if (exists)
                {
                    SaveService.Adopt(snapshot.Save);
                    CloudState.SignIn(identity.UserId);   // the document may predate the link
                }

                PlayerProgression.Invalidate();
                SaveService.Flush();

                AccountMismatched = false;
                outcome = exists ? SwitchOutcome.Adopted : SwitchOutcome.Started;
            }
            finally
            {
                Release();
            }

            // Both outside the latch. Raising Synced under it would deadlock any handler
            // that repaints by asking for a sync, and SyncAsync claims the latch itself.
            Raise(Synced);

            // The grove is already on this device, so this is housekeeping — it creates the
            // document for an account that has never played, and reconciles the wallet for one
            // that has. Its failure does not undo the switch and must not be reported as one.
            await SyncAsync(cancellation);
            return SwitchResult.Done(outcome);
        }

        // ------------------------------------------------------------ spending
        /// <summary>
        /// Brings the local wallet in line with the server's.
        ///
        /// Two halves, and both run every sync. Pending debits are offered up for
        /// confirmation — resubmitting one the server has already seen is harmless and
        /// expected, which is exactly what the idempotency key buys and why this needs
        /// no memory of what it has sent. Then the balances are read back, because a
        /// purchase made on another device has to arrive here even when this one has
        /// nothing of its own to send.
        /// </summary>
        static async Task ReconcileWalletAsync(string userId, CancellationToken cancellation)
        {
            bool reconciled = false;

            // Awards first, debits second. A daily chest opened offline should be able to
            // pay for the spend that followed it, and submitting them the other way round
            // would present the server with a debit against a balance it has not yet
            // credited — refused, and then retried forever.
            var awards = new List<GrantEntryDto>();
            foreach (var ledger in Wallet.Ledgers)
                foreach (var entry in ledger.PendingGrants)
                    awards.Add(entry.ToDto());

            if (awards.Count > 0)
            {
                var (granted, wallets) = await _backend.SubmitAwardsAsync(userId, awards, cancellation);
                if (granted.Ok && wallets != null)
                {
                    ApplyWalletStates(wallets);
                    reconciled = true;
                }
            }

            var pending = new List<SpendEntryDto>();
            foreach (var ledger in Wallet.Ledgers)
                foreach (var entry in ledger.PendingSpends)
                    pending.Add(entry.ToDto());

            if (pending.Count > 0)
            {
                var (submitted, wallets) = await _backend.SubmitSpendsAsync(userId, pending, cancellation);
                if (submitted.Ok && wallets != null)
                {
                    ApplyWalletStates(wallets);
                    return;                       // the reply already carries the balances
                }
            }

            // Skipped only when an award submission already returned fresh balances and
            // there were no debits to send; otherwise a purchase made on another device
            // has to arrive here even when this one has nothing of its own to offer.
            if (reconciled) return;

            var (read, current) = await _backend.ReadWalletAsync(userId, cancellation);
            if (read.Ok && current != null) ApplyWalletStates(current);
        }

        /// <summary>
        /// Hands a store receipt to the server and adopts whatever it grants.
        ///
        /// The balance moves only on the server's say-so. Crediting optimistically here
        /// and reconciling later would mean a rejected or replayed receipt had already
        /// paid out, and taking currency back from a player who believes they bought it
        /// is a support case that cannot be won.
        /// </summary>
        public static async Task<(CloudResult result, CloudRedemption redemption)> RedeemPurchaseAsync(
            PurchaseReceipt receipt, CancellationToken cancellation = default)
        {
            if (!IsAvailable)
                return (CloudResult.Failed(CloudFailure.Offline, "no cloud backend"),
                        CloudRedemption.Nothing);

            if (receipt == null || string.IsNullOrEmpty(receipt.TransactionId))
                return (CloudResult.Failed(CloudFailure.Rejected, "receipt has no transaction id"),
                        CloudRedemption.Nothing);

            // The same gate as a sync, and if anything it matters more here: this is the one
            // call that turns real money into currency, and crediting it to whichever account
            // happened to be signed in is a support case with a receipt attached.
            var authorised = await AuthoriseAsync(cancellation);
            if (!authorised.Ok) return (authorised, CloudRedemption.Nothing);

            var (result, wallets, redemption) = await _backend.RedeemPurchaseAsync(
                CloudState.UserId, receipt, cancellation);

            if (!result.Ok) return (result, CloudRedemption.Nothing);

            ApplyWalletStates(wallets);
            SaveService.Save();
            Raise(Synced);

            return (CloudResult.Success, redemption ?? CloudRedemption.Nothing);
        }

        static void ApplyWalletStates(List<CloudWalletState> wallets)
        {
            if (wallets == null) return;

            foreach (var state in wallets)
            {
                if (state == null || string.IsNullOrEmpty(state.Currency)) continue;

                Wallet.Ledger(state.Currency).ApplyServerState(
                    state.GrantedBaseline,
                    state.SpentBaseline,
                    state.ConfirmedSpendIds,
                    state.ConfirmedThroughUnix,
                    state.EarnedFloor,
                    state.ConfirmedGrantIds);
            }

            SaveService.MarkDirty();
            PlayerProgression.Invalidate();
        }

        static void Raise(Action handler)
        {
            try { handler?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
