using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Ads;
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

        /// <summary>
        /// Raised when <em>which</em> account this device is, or <em>how</em> it is signed in,
        /// has changed — a silent anonymous sign-in, a provider linked, an account switched, a
        /// mismatch opening or closing.
        ///
        /// <para>
        /// This exists because every screen that says something about the account samples
        /// <see cref="IsLinked"/> once, in <c>Build</c>, and nothing used to tell any of them
        /// when that answer moved. The panel that changes it has several exits — two providers,
        /// a corner cross, the scrim, the back key — so a callback from the panel fires from
        /// some of them and not others, which is exactly the bug the companion screens hit when
        /// a purchase reported only through the "wear" button. An event cannot be forgotten.
        /// </para>
        /// <para>
        /// Raised from <see cref="NoteIdentity"/> alone, and only on a real change, so it is
        /// safe to call from anywhere and safe to subscribe to with a plain repaint.
        /// </para>
        /// </summary>
        public static event Action IdentityChanged;

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

        /// <summary>
        /// What the account this device is signed in as is called, empty if it has no name.
        ///
        /// <para>
        /// For display only — see <see cref="CloudIdentity.Label"/>. It is what makes switching
        /// between two of one person's own accounts a thing they can be sure they did: without
        /// it, both sides of the switch say "your progress is saved online" and neither says
        /// which grove is on the phone.
        /// </para>
        /// </summary>
        public static string AccountLabel => IsAvailable ? _backend.CurrentIdentity.Label : string.Empty;

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
        /// <para>
        /// Counted off the records rather than through <c>PlayerProgression</c>, which drops any
        /// glade the catalog does not know. That is right for the reward arithmetic and wrong
        /// here: this answer decides whether a switch reports "welcome back" or "a new grove",
        /// and it must not turn on whether the content index has finished loading.
        /// </para>
        /// </summary>
        public static bool HoldsAGrove
            => PlayerProgress.ClearedCount > 0
            || Progression.CompanionLedger.BoughtCount > 0
            || Homestead.HomesteadLedger.BoughtCount > 0
            || Homestead.GroveLand.BoughtCount > 0;

        /// <summary>Chosen once, in <c>Boot</c>, before anything asks for a sync.</summary>
        public static void UseBackend(ICloudSaveBackend backend)
            => _backend = backend ?? new NullCloudBackend();

        /// <summary>
        /// The backend, for <see cref="Social.GroveBoard"/>.
        ///
        /// <para>
        /// Internal rather than public, and shared rather than duplicated. There is one
        /// session and one set of credentials, so a second backend would be a second thing to
        /// authenticate and a second dark path to keep working in a build with no Firebase.
        /// The boards are a separate <em>service</em> because they must never sit on the
        /// critical path of a sync — see <c>GroveBoard</c> — but they are not a separate
        /// connection.
        /// </para>
        /// </summary>
        internal static ICloudSaveBackend Backend => _backend;

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
        /// never two readings of a wall clock, for the reason <c>RunScreen.Tick</c> gives: the
        /// device's clock can jump — a timezone, an NTP correction, a player winding it
        /// forward for a daily chest — and a retry timer driven by one would either fire
        /// in a storm or never fire again.
        /// </para>
        /// </summary>
        public static void Tick(float deltaSeconds, bool networkReachable)
        {
            // Before the availability guard, deliberately: swapping the null backend for a real
            // one is itself a change of identity, and it is the first one that ever happens.
            WatchIdentity(deltaSeconds);

            if (!IsAvailable) return;

            _schedule.NetworkChanged(networkReachable);

            // Driven from here rather than from Boot so the boards follow the save's
            // lifecycle exactly and there is one place to wire instead of two. It cannot
            // fail a sync: everything it starts is best-effort and awaited by nobody.
            Social.GroveBoard.Tick(deltaSeconds, networkReachable);

            if (!_schedule.Tick(deltaSeconds)) return;

            _ = RunScheduledSyncAsync();
        }

        // What the last raise of IdentityChanged described. Compared rather than assumed, so
        // NoteIdentity is idempotent and every path may call it without coordinating.
        static bool _sampled, _wasAvailable, _wasLinked, _wasMismatched;
        static string _wasUserId = string.Empty;
        static float _identityWatch;

        /// <summary>How often the identity is re-read when nothing has asked it to be.</summary>
        const float IdentityPollSeconds = .5f;

        /// <summary>
        /// The backstop under <see cref="NoteIdentity"/>.
        ///
        /// <para>
        /// Every operation that moves the account calls <c>NoteIdentity</c> directly, so this
        /// poll is not what makes the feature work — it is what makes it impossible to break.
        /// The SDK can also change identity without being asked (a token refresh that fails, a
        /// provider revoked on the device, a restore), and this file has recorded twice now
        /// that a step somebody has to remember at a new call site is a step that gets
        /// forgotten. The cost is bounded by the poll interval rather than by the frame rate,
        /// because <c>CurrentIdentity</c> walks the user's provider list and builds a label
        /// every time it is read, and doing that sixty times a second to answer a question
        /// that changes a handful of times per install is how a menu screen starts allocating.
        /// </para>
        /// </summary>
        static void WatchIdentity(float deltaSeconds)
        {
            _identityWatch += deltaSeconds;
            if (_identityWatch < IdentityPollSeconds) return;

            _identityWatch = 0f;
            NoteIdentity();
        }

        /// <summary>
        /// Re-reads who this device is and raises <see cref="IdentityChanged"/> if the answer
        /// moved. Cheap, idempotent and safe to call from anywhere.
        ///
        /// <para>
        /// The first sample is recorded silently. There is nothing meaningful to announce about
        /// the state the game booted in — anything built after it reads the current values in
        /// its own <c>Build</c> — and raising there would fire the event before <c>Boot</c> has
        /// finished wiring, at the one moment a subscriber is most likely to be half-built.
        /// </para>
        /// </summary>
        public static void NoteIdentity()
        {
            bool available = IsAvailable;
            var identity = available ? _backend.CurrentIdentity : CloudIdentity.None;

            bool linked = available && identity.IsLinked;
            bool mismatched = AccountMismatched;
            string userId = identity.UserId ?? string.Empty;

            if (_sampled && available == _wasAvailable && linked == _wasLinked
                && mismatched == _wasMismatched && userId == _wasUserId) return;

            bool announce = _sampled;
            bool justLinked = announce && linked && !_wasLinked;

            _sampled = true;
            _wasAvailable = available;
            _wasLinked = linked;
            _wasMismatched = mismatched;
            _wasUserId = userId;

            if (!announce) return;

            // The one edge worth counting, at the one place every route through it passes:
            // linking, switching to a linked account and resuming one all end here. Raised
            // from the transition rather than from the panel because the panel has four exits
            // and two of the three routes do not involve it at all.
            if (justLinked) Analytics.Telemetry.Track("account_linked");

            // Guarded for the reason StoreService guards its own raise: these subscribers are
            // screens, one of them can be mid-teardown, and an exception thrown out of a
            // notification would abandon whichever account operation was reporting its result.
            try { IdentityChanged?.Invoke(); }
            catch (Exception error) { Debug.LogException(error); }
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
        static async Task<CloudResult> AuthoriseAsync(
            CancellationToken cancellation, bool repair = true)
        {
            switch (AccountGate.Decide(CloudState.UserId, _backend.CurrentIdentity.UserId))
            {
                case AccountGateVerdict.Proceed:
                    return Agreed();

                case AccountGateVerdict.Adopt:
                    CloudState.SignIn(_backend.CurrentIdentity.UserId);
                    return Agreed();

                case AccountGateVerdict.Refuse:
                    return Reconcile(_backend.CurrentIdentity.UserId, repair);
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
                return Reconcile(identity.UserId, repair);

            CloudState.SignIn(identity.UserId);      // a no-op when they already agree
            return Agreed();
        }

        static CloudResult Agreed()
        {
            AccountMismatched = false;

            // Every path that settles an identity ends here, so this is where the announcement
            // is immediate rather than up to half a second late. The poll in Tick still runs;
            // this is only the difference between a screen repainting on the frame the player
            // finished linking and repainting shortly afterwards, which on the one screen that
            // says "you are not signed in" is worth the line.
            NoteIdentity();
            return CloudResult.Success;
        }

        /// <summary>
        /// The session is one account and the save on disk is another. Finish the change of
        /// account on this device, rather than stopping and telling the player about it.
        ///
        /// <para>
        /// <b>Forward is the only direction this can be finished in, and that is a fact about
        /// how the two sides are written rather than a preference.</b> Firebase persists its
        /// signed-in user the moment it signs in, and this device only ever becomes a different
        /// account because somebody tapped a provider and chose one — there is no other way for
        /// the session to move. So a disagreement means the authentication got further than the
        /// file did: a process death, a crash, or a write that failed between the two. Carrying
        /// on to the account the player actually chose is what they asked for, and it costs
        /// nothing, because the grove being left is archived on the way past
        /// (<see cref="SaveService.SwitchTo"/>) and was pushed to the server before the switch
        /// began.
        /// </para>
        /// <para>
        /// This is what removed the state that produced the report this whole change came from.
        /// A device between two accounts used to sit there refusing every read and write, with
        /// the profile screen saying "this phone is signed in as someone else" and the only way
        /// out being a button that led to a destructive prompt. It was never a state anybody
        /// could act on, because the information needed to resolve it was on the device the
        /// whole time.
        /// </para>
        /// <para>
        /// <paramref name="repair"/> is false on exactly one path, and it is the money one. A
        /// store receipt is redeemed against whichever account is authorised, so repairing
        /// first would move a purchase made under one account onto another that happened to be
        /// signed in — a window that is only a few seconds wide and is a support case with a
        /// proof of purchase attached when it opens. Refusing there costs nothing at all: both
        /// stores re-deliver an unfinished transaction for ever, and the next sync will have
        /// repaired the device long before the retry.
        /// </para>
        /// </summary>
        static CloudResult Reconcile(string sessionUserId, bool repair)
        {
            if (!repair) return Disagreed(sessionUserId);

            // Note what is deliberately not here: RunGuard.Resolve. A switch clears the
            // in-flight run marker because the run belongs to the player who was playing it,
            // and this path can only be reached by a switch that died before it finished — so
            // the marker is from the previous session and Boot.Claim has already charged and
            // cleared it, before anything could ask for a sync. Calling it here would take a
            // heart from whichever run happens to be open now.
            if (SaveService.SwitchTo(sessionUserId) == SaveService.SwapResult.Refused)
                return Disagreed(sessionUserId);

            Debug.Log("[Cloud] the session had moved ahead of the save; " +
                      "the account change was completed on this device");

            PlayerProgression.Invalidate();
            Social.GroveBoard.Forget();
            Raise(Synced);

            return Agreed();
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
            NoteIdentity();
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
        /// worth hiding: it reports <see cref="SwitchOutcome.SameAccount"/>, so a player who
        /// picked the wrong entry from the provider's account chooser is told plainly rather
        /// than watching nothing happen.
        /// </para>
        /// <para>
        /// <b>Switching back is free.</b> The grove being left is archived on this device as
        /// well as pushed, so returning to it later restores from disk and needs no network at
        /// all — which is what makes moving between two accounts an ordinary thing to do rather
        /// than a download each way.
        /// </para>
        /// </summary>
        public static Task<SwitchResult> SwitchAccountAsync(
            LinkCredential credential, CancellationToken cancellation = default)
            => BecomeAsync(credential, BecomeMode.Switch, cancellation);

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
        /// <para>
        /// It is still archived locally, because that is free and the alternative is deleting
        /// something on a player's own device to save a few hundred kilobytes. Nothing offers
        /// a way back to it — an anonymous account cannot be signed into again once the session
        /// has moved — so the copy is a courtesy for a support case, never a promise, and the
        /// panel says as much before the second tap.
        /// </para>
        /// </summary>
        public static Task<SwitchResult> AdoptLinkedAccountAsync(
            LinkCredential credential, CancellationToken cancellation = default)
            => BecomeAsync(credential, BecomeMode.Adopt, cancellation);

        /// <summary>
        /// A sync that waits for a running one rather than reporting that one was running.
        ///
        /// <para>
        /// <see cref="SyncAsync"/> answers <see cref="CloudFailure.Busy"/> the instant the latch
        /// is held, which is right for a background sync — a second one has nothing to add —
        /// and wrong for both of the syncs a switch runs, because there it becomes a sentence
        /// on screen. The securing one would say "we could not save your grove" about a grove
        /// that is being saved right now; the catch-up one would say "your grove will load once
        /// you are online" to somebody who is. And this is not a rare collision: a sync starts
        /// on every foreground, which is precisely when a player opens the account panel.
        /// </para>
        /// <para>
        /// It is <see cref="ClaimAsync"/>'s reasoning applied one level up, and the same budget.
        /// Only contention is retried — a real failure is reported at once, because waiting out
        /// ten seconds to repeat an answer already known is worse than giving it.
        /// </para>
        /// </summary>
        static async Task<CloudResult> SyncPatientlyAsync(CancellationToken cancellation)
        {
            const int TimeoutMs = 10000;
            const int PollMs = 50;

            var result = await SyncAsync(cancellation);

            for (int waited = 0; waited < TimeoutMs && result.Failure == CloudFailure.Busy; waited += PollMs)
            {
                await Task.Delay(PollMs, cancellation);
                result = await SyncAsync(cancellation);
            }

            return result;
        }

        /// <summary>
        /// Which of the two reasons a device has for becoming a different account. They differ
        /// in exactly one thing — whether the outgoing grove is pushed to the server first —
        /// which is the decision worth naming rather than passing as a bare flag.
        /// </summary>
        enum BecomeMode
        {
            /// <summary>Deliberate, from a linked account. The outgoing grove is saved first.</summary>
            Switch,

            /// <summary>Consented, from a guest. The outgoing grove is unreachable; see the caller.</summary>
            Adopt,
        }

        /// <summary>
        /// Becomes the account a credential names.
        ///
        /// <para>
        /// One method for both routes in, because they differ in exactly one decision — whether
        /// the outgoing grove is pushed to the server first — and everything after that
        /// decision is the part where a mistake loses somebody's account. Two copies of it
        /// would be two chances to get the order below wrong.
        /// </para>
        /// <para>
        /// <b>The order is the design: secure, authenticate, swap, catch up.</b> It used to be
        /// secure, authenticate, <em>fetch</em>, replace — and the fetch is what made the
        /// switch breakable. Reading the incoming grove over the network was the step that
        /// decided whether the switch happened at all, and it ran in the frame after an OAuth
        /// browser handed control back: the process has just been foregrounded, the database
        /// stream has just been re-authenticated, and one unlucky read left the device
        /// authenticated as one player, holding another's save, syncing nothing, and telling
        /// its owner so in a sentence nobody could act on.
        /// </para>
        /// <para>
        /// The swap is local now (<see cref="SaveService.SwitchTo"/>) — the outgoing grove is
        /// archived under its own account and the incoming one restored from this device if it
        /// has been played here before — so once the credential is in hand the switch is
        /// finished and cannot stop halfway. The server is asked afterwards, by an ordinary
        /// sync, which pulls and joins exactly as it does on any launch and retries on a
        /// backoff if it cannot. Its failure no longer undoes anything; it only decides which
        /// of two true sentences the screen gets to say.
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
            // Kept, and it is now the only step that can refuse a switch. The grove is archived
            // on this device a moment later either way, so this is not what stops it being lost
            // here — it is what keeps the promise the button makes about *another* device: sign
            // in over there and it is waiting. A phone that switches away offline and is then
            // reinstalled would have nothing to fetch, which is the one loss left, and one
            // round trip is a cheap way to make it unreachable.
            //
            // A grove already on the server costs nothing: SaveDelta finds nothing changed and
            // the push is skipped entirely.
            if (mode == BecomeMode.Switch && CloudState.IsSignedIn)
            {
                var secured = await SyncPatientlyAsync(cancellation);
                if (!secured.Ok)
                    return SwitchResult.Failed(SwitchOutcome.NotSecured, secured);
            }

            if (!await ClaimAsync(cancellation))
                return SwitchResult.Failed(SwitchOutcome.Refused, CloudFailure.Busy, "a sync is already running");

            bool restored;

            try
            {
                string outgoing = CloudState.UserId;

                // ------------------------------------------------------------ authenticate
                var (signIn, identity) = await _backend.SignInWithCredentialAsync(credential, cancellation);
                if (!signIn.Ok) return SwitchResult.Failed(SwitchOutcome.Refused, signIn);
                if (!identity.IsValid)
                    return SwitchResult.Failed(SwitchOutcome.Refused, CloudFailure.Unauthenticated, "no user id");

                // Already here. Nothing is touched, and it is worth reporting rather than
                // hiding: picking the wrong entry out of a provider's account chooser is an
                // ordinary mistake, and a switch that silently does nothing looks broken.
                if (string.Equals(identity.UserId, outgoing, StringComparison.Ordinal))
                {
                    AccountMismatched = false;
                    return SwitchResult.Done(SwitchOutcome.SameAccount, PlayerProgress.ClearedCount);
                }

                // ---------------------------------------------------------------- the swap
                // The marker for a run this device left in flight belongs to the player who
                // was playing it. Charging the incoming account a heart for a run it never
                // started is small, silent and impossible to explain afterwards. Resolved
                // before the archive is taken, so the charge travels with the grove it belongs
                // to rather than being applied to whichever one comes back next.
                RunGuard.Resolve();

                // The published card, the cached boards and the fingerprint that says what is
                // already on the board all belong to the outgoing account. Kept, the incoming
                // player's grove would look already-published and never reach the board at
                // all — invariant 17's discipline applied to a cache.
                Social.GroveBoard.Forget();

                // outgoingIsSafe, and both modes earn it differently. A switch got here only
                // because the securing sync above succeeded, so the grove being left is on the
                // server whatever the archive manages. An adopt is leaving an anonymous grove
                // the player was told they are leaving. Neither is a reason to stop for a full
                // disk, which is what the flag turns off.
                var swap = SaveService.SwitchTo(identity.UserId, outgoingIsSafe: true);
                if (swap == SaveService.SwapResult.Refused)
                    return SwitchResult.Failed(SwitchOutcome.Refused, CloudFailure.Error,
                                               "the grove could not be swapped on this device");

                restored = swap == SaveService.SwapResult.Restored;

                PlayerProgression.Invalidate();
                SaveService.Flush();

                AccountMismatched = false;
            }
            finally
            {
                Release();
            }

            // Both outside the latch. Raising Synced under it would deadlock any handler
            // that repaints by asking for a sync, and SyncAsync claims the latch itself.
            Raise(Synced);

            // ----------------------------------------------------------------- catch up
            // Not housekeeping and not the switch either. It creates the document for an
            // account that has never played, reconciles the wallet for one that has, and —
            // when this device has no archive of theirs — is where their grove actually
            // arrives. Its failure does not undo anything: the account has changed, the
            // previous grove is archived here and on the server, and the scheduler retries.
            var sync = await SyncPatientlyAsync(cancellation);

            // Read off the device rather than out of the reply, because the two genuinely
            // differ: a grove restored from this device's own archive never came from a reply
            // at all, and what the player is about to look at is what they want confirmed.
            bool found = restored || HoldsAGrove;

            var outcome = found ? SwitchOutcome.Adopted
                        : sync.Ok ? SwitchOutcome.Started
                        : SwitchOutcome.Pending;

            return SwitchResult.Done(outcome, PlayerProgress.ClearedCount);
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
            // happened to be signed in is a support case with a receipt attached. Hence
            // repair: false — a sync in this state completes the account change the player
            // asked for, which is right for progress and wrong for a payment made under the
            // account being left. Refusing costs nothing: both stores re-deliver an unfinished
            // transaction for ever, and by the retry the device has repaired itself.
            var authorised = await AuthoriseAsync(cancellation, repair: false);
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

                // Refunded heart containers. Applied from whichever rows carry them — the
                // list is an account fact repeated per currency row, and the ledger's own
                // union makes applying it several times the same as applying it once.
                //
                // This is the only direction a container ever moves without a receipt, and
                // it is the half a client-held entitlement cannot see for itself: buy, spend,
                // refund, repeat is the commonest way a mobile economy leaks (invariant 18c),
                // and a permanent upgrade that outlived its refund would be exactly that.
                HeartContainerLedger.ApplyServerRevocations(state.RevokedContainers);

                // Where the bonus wheel has got to. Applied from whichever rows carry it — the
                // position is an account fact repeated per currency row, and the stand only ever
                // moves forward, so applying it several times is applying it once.
                //
                // This is the one number in the reply the client has no copy of and cannot
                // derive: the slice a spin lands on is a pure function of (account, day, spin
                // index), so the index has to come from the side that grants the views. See
                // WheelStand.
                WheelStand.ApplyServerState(state.CarriesWheel, state.WheelDay, state.WheelSpins);
            }

            SaveService.MarkDirty();
            PlayerProgression.Invalidate();
        }

        // ---------------------------------------------------------------- deleting
        /// <summary>
        /// Deletes this device's account — on the server, and then on the device.
        ///
        /// <para>
        /// <b>Server first, device second, and never the other way round.</b> The local grove
        /// is the only evidence left that the deletion was ever asked for: erase it first and a
        /// refused call leaves a player with an empty phone and a full account, which is the
        /// one outcome here that is worse than the deletion failing. So nothing local is
        /// touched until the server has said the account is gone, and every failure this can
        /// report is a failure that changed nothing at all — which is what
        /// <see cref="AccountDeletion.Untouched"/> promises the panel.
        /// </para>
        /// <para>
        /// <b>Under the sync latch for the whole of it.</b> A sync is pull → join → push, and
        /// one already in flight would push the grove back into a document this call is in the
        /// middle of deleting — recreating <c>players/{uid}</c> seconds after it went, under a
        /// uid nothing can ever authenticate as again. That is the orphan the server's own
        /// ordering cannot prevent, because it is the client that causes it. Holding the latch
        /// across the erase and the swap closes it.
        /// </para>
        /// <para>
        /// <b>What the device is left as.</b> A fresh anonymous account with an empty grove,
        /// minted by the backend as part of the call. Not "signed out": there is no sign-in
        /// screen in this game, so a device holding no account at all would be a state nothing
        /// else in the codebase knows how to draw — see <c>AccountOverlay</c> on why there is
        /// deliberately no sign-out button. A player who deletes their account is not leaving
        /// the game, they are starting it again.
        /// </para>
        /// </summary>
        /// <param name="credential">
        /// The provider to re-authenticate against before deleting, for an account that has
        /// one — see <see cref="AccountDeletion.Verdict.Reauthenticate"/>. Left invalid for a
        /// guest, who has no provider and must not be locked out of their own deletion for it.
        /// </param>
        public static async Task<DeleteResult> DeleteAccountAsync(
            LinkCredential credential = default, CancellationToken cancellation = default)
        {
            if (!IsAvailable)
                return DeleteResult.Failed(AccountDeletion.Outcome.Failed, CloudFailure.Offline,
                                           "no cloud backend");
            if (!SaveService.IsLoaded)
                return DeleteResult.Failed(AccountDeletion.Outcome.Failed, CloudFailure.Error,
                                           "save not loaded");

            // Read from the session rather than from the save, because the session is what the
            // server will authenticate the call as. If the two disagree the backend refuses —
            // it compares them itself — and that refusal is the right answer: a device caught
            // between two accounts must not be allowed to guess which one to destroy.
            string doomed = _backend.CurrentIdentity.UserId;
            if (string.IsNullOrEmpty(doomed))
                return DeleteResult.Failed(AccountDeletion.Outcome.Failed,
                                           CloudFailure.Unauthenticated, "not signed in");

            if (!await ClaimAsync(cancellation))
                return DeleteResult.Failed(AccountDeletion.Outcome.Busy, CloudFailure.Busy,
                                           "a sync is already running");

            try
            {
                // --------------------------------------------------------- prove it is them
                // Inside the latch and before anything is removed. A refusal here — a closed
                // sheet, the wrong account, no network — has cost nothing at all, which is
                // what lets every failure this method reports say "nothing has been deleted"
                // and be telling the truth.
                string appleCode = null;

                if (credential.IsValid)
                {
                    var (proof, code) = await _backend.ReauthenticateAsync(credential, cancellation);
                    if (!proof.Ok)
                    {
                        Debug.LogWarning($"[Account] delete not authorised: {proof.Failure} · {proof.Message}");
                        return DeleteResult.From(proof);
                    }

                    appleCode = code;
                }

                var result = await _backend.DeleteAccountAsync(doomed, appleCode, cancellation);
                if (!result.Ok)
                {
                    Debug.LogWarning($"[Account] delete refused: {result.Failure} · {result.Message}");
                    return DeleteResult.From(result);
                }

                // ------------------------------------------------------------- the device
                // The account is gone by here. Nothing below may report a failure, because
                // there is nothing left to retry and telling a player their deletion did not
                // work would be false.
                SaveService.EraseAccount(doomed);

                // Whoever the backend signed this device in as while it was deleting. Empty
                // only if that sign-in failed — a flat network at the wrong moment — in which
                // case the save simply names nobody until the next launch picks an account up,
                // which is precisely the state a first install is in.
                string fresh = _backend.CurrentIdentity.UserId;
                if (!string.IsNullOrEmpty(fresh)) SaveService.SwitchTo(fresh, outgoingIsSafe: true);

                AccountMismatched = false;
                PlayerProgression.Invalidate();
                SaveService.Flush();
            }
            finally
            {
                Release();
            }

            // Outside the latch, for RunScheduledSync's reason: a handler that repaints by
            // asking for a sync would deadlock against a latch this call is still holding.
            Raise(Synced);
            Raise(IdentityChanged);

            // The backoff belongs to the account that has just stopped existing. Left alone, a
            // device that had been failing to sync would carry that penalty into a brand new
            // grove and appear not to be saving.
            ResetBackoff();
            RequestSync();

            return DeleteResult.Done();
        }

        static void Raise(Action handler)
        {
            try { handler?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
