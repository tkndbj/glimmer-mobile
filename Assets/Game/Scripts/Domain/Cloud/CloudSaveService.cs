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

        /// <summary>
        /// Pull, join, push. Safe to call at any time: a second call while one is in
        /// flight returns immediately rather than racing it, because two syncs merging
        /// the same file concurrently would each push a snapshot missing the other's
        /// work.
        /// </summary>
        public static async Task<CloudResult> SyncAsync(CancellationToken cancellation = default)
        {
            if (!IsAvailable) return CloudResult.Failed(CloudFailure.Offline, "no cloud backend");
            if (!SaveService.IsLoaded) return CloudResult.Failed(CloudFailure.Error, "save not loaded");

            if (Interlocked.CompareExchange(ref _syncing, 1, 0) != 0)
                return CloudResult.Failed(CloudFailure.Error, "a sync is already running");

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
                Volatile.Write(ref _syncing, 0);
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

            var (result, identity) = await _backend.LinkAsync(credential, cancellation);

            // Not a fault: the player linked this provider on another device. The caller
            // has to ask before anything can be done about it, because the answer costs
            // one of the two accounts.
            if (result.Failure == CloudFailure.AlreadyLinkedElsewhere) return result;
            if (!result.Ok) return result;

            if (identity.IsValid) CloudState.SignIn(identity.UserId);
            SaveService.Save();

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

            var (signIn, identity) = await _backend.SignInWithCredentialAsync(credential, cancellation);
            if (!signIn.Ok) return signIn;
            if (!identity.IsValid) return CloudResult.Failed(CloudFailure.Unauthenticated, "no user id");

            // Local state is dropped before the pull, so a failure here leaves the device
            // on a clean signed-in account rather than on the old one wearing a new name.
            SaveService.Wipe();
            CloudState.SignIn(identity.UserId);

            var (pull, snapshot) = await _backend.PullAsync(identity.UserId, cancellation);
            if (!pull.Ok) return pull;

            if (snapshot != null && snapshot.Exists && snapshot.Save != null)
            {
                SaveService.Adopt(snapshot.Save);
                CloudState.SignIn(identity.UserId);      // the document may predate the link
            }

            PlayerProgression.Invalidate();
            SaveService.Flush();
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
                    state.EarnedFloor);
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
