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
            if (!CloudState.IsSignedIn)
            {
                var (signIn, identity) = await _backend.SignInAsync(cancellation);
                if (!signIn.Ok) return signIn;
                if (!identity.IsValid) return CloudResult.Failed(CloudFailure.Unauthenticated, "no user id");

                CloudState.SignIn(identity.UserId);
            }

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
                var (result, identity) = await _backend.LinkAsync(credential, cancellation);

                // Not a fault: the player linked this provider on another device. The
                // caller has to ask before anything can be done about it, because the
                // answer costs one of the two accounts.
                if (result.Failure == CloudFailure.AlreadyLinkedElsewhere) return result;
                if (!result.Ok) return result;

                if (identity.IsValid) CloudState.SignIn(identity.UserId);
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
        /// </summary>
        public static async Task<CloudResult> AdoptLinkedAccountAsync(
            LinkCredential credential, CancellationToken cancellation = default)
        {
            if (!IsAvailable) return CloudResult.Failed(CloudFailure.Offline, "no cloud backend");
            if (!SaveService.IsLoaded) return CloudResult.Failed(CloudFailure.Error, "save not loaded");

            if (!await ClaimAsync(cancellation))
                return CloudResult.Failed(CloudFailure.Error, "a sync is already running");

            try
            {
                var (signIn, identity) = await _backend.SignInWithCredentialAsync(credential, cancellation);
                if (!signIn.Ok) return signIn;
                if (!identity.IsValid) return CloudResult.Failed(CloudFailure.Unauthenticated, "no user id");

                // Local state is dropped before the pull, so a failure here leaves the
                // device on a clean signed-in account rather than on the old one wearing
                // a new name.
                SaveService.Wipe();
                CloudState.SignIn(identity.UserId);

                var (pull, snapshot) = await _backend.PullAsync(identity.UserId, cancellation);
                if (!pull.Ok) return pull;

                if (snapshot != null && snapshot.Exists && snapshot.Save != null)
                {
                    SaveService.Adopt(snapshot.Save);
                    CloudState.SignIn(identity.UserId);  // the document may predate the link
                }

                PlayerProgression.Invalidate();
                SaveService.Flush();
            }
            finally
            {
                Release();
            }

            // Both outside the latch. Raising Synced under it would deadlock any handler
            // that repaints by asking for a sync, and SyncAsync claims the latch itself.
            Raise(Synced);
            return await SyncAsync(cancellation);
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
        public static async Task<CloudResult> RedeemPurchaseAsync(
            PurchaseReceipt receipt, CancellationToken cancellation = default)
        {
            if (!IsAvailable) return CloudResult.Failed(CloudFailure.Offline, "no cloud backend");
            if (receipt == null || string.IsNullOrEmpty(receipt.TransactionId))
                return CloudResult.Failed(CloudFailure.Rejected, "receipt has no transaction id");

            if (!CloudState.IsSignedIn)
            {
                var (signIn, identity) = await _backend.SignInAsync(cancellation);
                if (!signIn.Ok) return signIn;
                CloudState.SignIn(identity.UserId);
            }

            var (result, wallets) = await _backend.RedeemPurchaseAsync(
                CloudState.UserId, receipt, cancellation);

            if (!result.Ok) return result;

            ApplyWalletStates(wallets);
            SaveService.Save();
            Raise(Synced);

            return CloudResult.Success;
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
