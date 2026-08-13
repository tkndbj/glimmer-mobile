#if GLIMMER_FIREBASE
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using Firebase.Firestore;
using Firebase.Functions;
using GlimmerGrove.Persistence;
using UnityEngine;

namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// The Firebase implementation of <see cref="ICloudSaveBackend"/>.
    ///
    /// Lives in its own assembly so the Firebase SDK never becomes a dependency of
    /// <c>GlimmerGrove.Domain</c>. That is not tidiness: it is what keeps the merge,
    /// the reward arithmetic and the ledger — the parts most likely to be wrong —
    /// runnable in the EditMode test suite with no SDK installed and no network.
    ///
    /// <para>
    /// The whole file compiles out when <c>GLIMMER_FIREBASE</c> is undefined, which is
    /// how the project stays buildable before the SDK is installed. The define comes
    /// from asmdef <c>versionDefines</c>, never Player Settings — those are stored per
    /// build target, so one added on Standalone is silently absent on Android and iOS.
    /// </para>
    ///
    /// <para>
    /// <b>Threading.</b> Firebase completes its tasks on background threads, but every
    /// <c>await</c> here resumes on Unity's synchronisation context because the sync is
    /// always started from the main thread. Nothing may be marked
    /// <c>ConfigureAwait(false)</c>: adopting a merged save raises events that screens
    /// are subscribed to, and touching the UI off the main thread is a crash rather
    /// than an exception.
    /// </para>
    /// </summary>
    public sealed class FirebaseCloudSaveBackend : ICloudSaveBackend
    {
        /// <summary>Must match <c>REGION</c> in the functions' config.ts.</summary>
        public const string FunctionsRegion = "europe-west1";

        const string PlayersCollection = "players";
        const string PrivateCollection = "private";
        const string WalletDocument = "wallet";

        FirebaseApp _app;
        FirebaseAuth _auth;
        FirebaseFirestore _db;
        FirebaseFunctions _functions;

        bool _ready;
        bool _unavailable;

        public bool IsAvailable => !_unavailable;

        public CloudIdentity CurrentIdentity
        {
            get
            {
                var user = _ready ? _auth?.CurrentUser : null;
                return user == null ? CloudIdentity.None
                                    : new CloudIdentity(user.UserId, IsLinked(user));
            }
        }

        // ------------------------------------------------------------ lifecycle
        /// <summary>
        /// Resolves the native dependencies once. On Android this can genuinely fail —
        /// an ancient Play Services, a device with none at all — and the answer is to
        /// mark the backend unavailable and let the game carry on locally, not to
        /// spend the session retrying something that will not change.
        /// </summary>
        async Task<bool> EnsureReadyAsync()
        {
            if (_ready) return true;
            if (_unavailable) return false;

            try
            {
                var status = await FirebaseApp.CheckAndFixDependenciesAsync();
                if (status != DependencyStatus.Available)
                {
                    Debug.LogWarning($"[Cloud] Firebase unavailable on this device ({status}); staying local");
                    _unavailable = true;
                    return false;
                }

                _app = FirebaseApp.DefaultInstance;
                _auth = FirebaseAuth.DefaultInstance;
                _db = FirebaseFirestore.DefaultInstance;
                _functions = FirebaseFunctions.GetInstance(_app, FunctionsRegion);

                _ready = true;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Cloud] Firebase failed to initialise: " + e.Message);
                _unavailable = true;
                return false;
            }
        }

        // --------------------------------------------------------------- signin
        public async Task<(CloudResult result, CloudIdentity identity)> SignInAsync(
            CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), CloudIdentity.None);

            try
            {
                if (_auth.CurrentUser == null) await _auth.SignInAnonymouslyAsync();

                var user = _auth.CurrentUser;
                if (user == null)
                    return (CloudResult.Failed(CloudFailure.Unauthenticated, "no user after sign in"),
                            CloudIdentity.None);

                return (CloudResult.Success, new CloudIdentity(user.UserId, IsLinked(user)));
            }
            catch (Exception e)
            {
                return (Classify(e, "sign in"), CloudIdentity.None);
            }
        }

        /// <summary>
        /// Attaches a permanent provider to the anonymous account, keeping its uid.
        ///
        /// Linking rather than signing in is the whole point: the uid is what
        /// <c>players/{uid}</c> and the wallet are keyed on, so carrying it across is
        /// what stops the player arriving at a brand-new empty account while their
        /// grove sits on an anonymous one nobody can reach again.
        /// </summary>
        public async Task<(CloudResult result, CloudIdentity identity)> LinkAsync(
            LinkCredential credential, CancellationToken cancellation = default)
        {
            if (!credential.IsValid)
                return (CloudResult.Failed(CloudFailure.Rejected, "no provider named"), CloudIdentity.None);

            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), CloudIdentity.None);

            try
            {
                if (_auth.CurrentUser == null) await _auth.SignInAnonymouslyAsync();

                if (credential.HasToken)
                    await _auth.CurrentUser.LinkWithCredentialAsync(ToCredential(credential));
                else
                    await _auth.CurrentUser.LinkWithProviderAsync(Provider(credential.ProviderId));

                var user = _auth.CurrentUser;
                return (CloudResult.Success, new CloudIdentity(user.UserId, IsLinked(user)));
            }
            catch (Exception e)
            {
                return (Classify(e, "link"), CloudIdentity.None);
            }
        }

        /// <summary>
        /// Adopts the account the credential already belongs to. Destructive — see the
        /// interface. The caller is responsible for having asked first.
        /// </summary>
        public async Task<(CloudResult result, CloudIdentity identity)> SignInWithCredentialAsync(
            LinkCredential credential, CancellationToken cancellation = default)
        {
            if (!credential.IsValid)
                return (CloudResult.Failed(CloudFailure.Rejected, "no provider named"), CloudIdentity.None);

            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), CloudIdentity.None);

            try
            {
                // Signing out first so the abandoned anonymous account cannot linger as
                // the current user if the new sign-in fails halfway.
                _auth.SignOut();

                if (credential.HasToken)
                    await _auth.SignInAndRetrieveDataWithCredentialAsync(ToCredential(credential));
                else
                    await _auth.SignInWithProviderAsync(Provider(credential.ProviderId));

                var user = _auth.CurrentUser;
                if (user == null)
                    return (CloudResult.Failed(CloudFailure.Unauthenticated, "no user after sign in"),
                            CloudIdentity.None);

                return (CloudResult.Success, new CloudIdentity(user.UserId, IsLinked(user)));
            }
            catch (Exception e)
            {
                return (Classify(e, "sign in with credential"), CloudIdentity.None);
            }
        }

        /// <summary>
        /// Firebase drives the OAuth flow itself, so neither Apple's nor Google's Unity
        /// plugin is a dependency of this game. One path, both providers, both platforms.
        /// </summary>
        static FederatedOAuthProvider Provider(string providerId)
        {
            var data = new FederatedOAuthProviderData { ProviderId = providerId };

            // Only what the account actually needs. Asking for more than a display name
            // makes the consent screen longer and gives the game data it does not want
            // to be responsible for.
            data.Scopes = providerId == LinkCredential.Apple
                ? new List<string> { "name", "email" }
                : new List<string> { "profile" };

            return new FederatedOAuthProvider(data);
        }

        static Credential ToCredential(LinkCredential credential)
            => credential.ProviderId == LinkCredential.Apple
                ? OAuthProvider.GetCredential(LinkCredential.Apple, credential.IdToken,
                                              credential.RawNonce, null)
                : GoogleAuthProvider.GetCredential(credential.IdToken, credential.AccessToken);

        /// <summary>
        /// Auth reports through <see cref="FirebaseException"/> with an
        /// <see cref="AuthError"/> code. Firestore and Functions do not derive from it —
        /// they throw their own types — so a FirebaseException reaching here came from
        /// Auth, and the code check only guards against a value outside the enum.
        /// </summary>
        static bool IsAuthError(FirebaseException e)
            => Enum.IsDefined(typeof(AuthError), e.ErrorCode);

        static bool IsLinked(FirebaseUser user)
        {
            if (user == null) return false;
            foreach (var provider in user.ProviderData)
                if (provider.ProviderId != "firebase") return true;
            return false;
        }

        // ----------------------------------------------------------- save document
        DocumentReference PlayerDoc(string uid) => _db.Collection(PlayersCollection).Document(uid);

        public async Task<(CloudResult result, CloudSnapshot snapshot)> PullAsync(
            string userId, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), CloudSnapshot.Missing);

            if (string.IsNullOrEmpty(userId))
                return (CloudResult.Failed(CloudFailure.Unauthenticated, "no user id"), CloudSnapshot.Missing);

            try
            {
                var snapshot = await PlayerDoc(userId).GetSnapshotAsync();

                if (!snapshot.Exists)
                    return (CloudResult.Success, CloudSnapshot.Missing);   // a first sync, not a failure

                var dto = FirestoreSaveMapper.FromDocument(snapshot.ToDictionary());
                return (CloudResult.Success, new CloudSnapshot(dto, true));
            }
            catch (Exception e)
            {
                return (Classify(e, "pull"), CloudSnapshot.Missing);
            }
        }

        public async Task<CloudResult> PushAsync(
            string userId, SaveFileDto snapshot, SaveDelta delta, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync()) return CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable");
            if (string.IsNullOrEmpty(userId)) return CloudResult.Failed(CloudFailure.Unauthenticated, "no user id");
            if (snapshot == null) return CloudResult.Failed(CloudFailure.Rejected, "nothing to push");

            delta ??= SaveDelta.Everything;
            if (delta.IsEmpty) return CloudResult.Success;

            try
            {
                if (delta.IsFullWrite)
                {
                    // Nothing on the server to merge against. Overwrite rather than
                    // merge: the snapshot is already the join of local and remote, so a
                    // field-level merge would be a second, weaker merge fighting the
                    // real one.
                    await PlayerDoc(userId).SetAsync(FirestoreSaveMapper.ToDocument(snapshot),
                                                     SetOptions.Overwrite);
                    return CloudResult.Success;
                }

                // A partial update, addressed by field path. `levels.c01_first_light`
                // replaces one glade's record and leaves the other two thousand alone.
                var updates = new Dictionary<string, object>();

                foreach (var pair in FirestoreSaveMapper.HeaderFields(snapshot))
                    updates[pair.Key] = pair.Value;

                if (delta.ChangedLevelIds.Count > 0)
                {
                    var levels = FirestoreSaveMapper.LevelMap(snapshot);
                    foreach (var levelId in delta.ChangedLevelIds)
                    {
                        if (!levels.TryGetValue(levelId, out object record)) continue;
                        updates[FirestoreSaveMapper.LevelFieldPath(levelId)] = record;
                    }
                }

                await PlayerDoc(userId).UpdateAsync(updates);
                return CloudResult.Success;
            }
            catch (Exception e)
            {
                return Classify(e, "push");
            }
        }

        // ---------------------------------------------------------------- wallet
        /// <summary>
        /// Reads the server's balances. These come from a document the client cannot
        /// write, which is the whole reason they can be believed.
        /// </summary>
        public async Task<(CloudResult result, List<CloudWalletState> wallets)> ReadWalletAsync(
            string userId, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), Empty());

            try
            {
                var reply = await CallAsync("getWallet", new Dictionary<string, object>());
                return (CloudResult.Success, ReadWalletStates(reply));
            }
            catch (Exception e)
            {
                return (Classify(e, "read wallet"), Empty());
            }
        }

        public async Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitSpendsAsync(
            string userId, IReadOnlyList<SpendEntryDto> spends, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), Empty());

            if (spends == null || spends.Count == 0) return (CloudResult.Success, Empty());

            try
            {
                var payload = new List<object>(spends.Count);
                foreach (var spend in spends)
                {
                    if (spend == null || string.IsNullOrEmpty(spend.id)) continue;
                    payload.Add(new Dictionary<string, object>
                    {
                        { "id", spend.id },
                        { "currency", Currency.Credits },   // one currency spends today
                        { "amount", spend.amount },
                        { "unix", spend.unix },
                        { "reason", spend.reason ?? string.Empty },
                    });
                }

                var reply = await CallAsync("submitSpends",
                                            new Dictionary<string, object> { { "spends", payload } });

                WarnAboutRejections(reply);
                return (CloudResult.Success, ReadWalletStates(reply));
            }
            catch (Exception e)
            {
                return (Classify(e, "submit spends"), Empty());
            }
        }

        public async Task<(CloudResult result, List<CloudWalletState> wallets)> RedeemPurchaseAsync(
            string userId, PurchaseReceipt receipt, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), Empty());

            if (receipt == null || string.IsNullOrEmpty(receipt.TransactionId))
                return (CloudResult.Failed(CloudFailure.Rejected, "receipt has no transaction id"), Empty());

            try
            {
                var reply = await CallAsync("redeemPurchase", new Dictionary<string, object>
                {
                    { "receipt", new Dictionary<string, object>
                        {
                            { "store", receipt.Store ?? string.Empty },
                            { "transactionId", receipt.TransactionId },
                            { "productId", receipt.ProductId ?? string.Empty },
                            { "payload", receipt.Payload ?? string.Empty },
                        }
                    },
                });

                return (CloudResult.Success, ReadWalletStates(reply));
            }
            catch (Exception e)
            {
                return (Classify(e, "redeem purchase"), Empty());
            }
        }

        // ------------------------------------------------------------- plumbing
        async Task<IDictionary<string, object>> CallAsync(string name, Dictionary<string, object> data)
        {
            var result = await _functions.GetHttpsCallable(name).CallAsync(data);
            return result.Data as IDictionary<string, object>;
        }

        static List<CloudWalletState> Empty() => new List<CloudWalletState>();

        static List<CloudWalletState> ReadWalletStates(IDictionary<string, object> reply)
        {
            var states = Empty();
            if (reply == null) return states;

            if (!reply.TryGetValue("wallets", out object raw) || !(raw is IEnumerable<object> list))
                return states;

            foreach (var element in list)
            {
                if (!(element is IDictionary<string, object> entry)) continue;

                string currency = entry.TryGetValue("currency", out object c) ? c as string : null;
                if (string.IsNullOrEmpty(currency)) continue;

                var state = new CloudWalletState
                {
                    Currency = currency,
                    GrantedBaseline = ReadLong(entry, "grantedBaseline"),
                    SpentBaseline = ReadLong(entry, "spentBaseline"),
                    ConfirmedThroughUnix = ReadLong(entry, "confirmedThroughUnix"),
                    EarnedFloor = ReadLong(entry, "earnedFloor"),
                };

                if (entry.TryGetValue("confirmedSpendIds", out object ids) && ids is IEnumerable<object> idList)
                {
                    foreach (var id in idList)
                        if (id is string s && s.Length > 0) state.ConfirmedSpendIds.Add(s);
                }

                states.Add(state);
            }

            return states;
        }

        static long ReadLong(IDictionary<string, object> map, string key)
        {
            if (!map.TryGetValue(key, out object value) || value == null) return 0;

            switch (value)
            {
                case long l: return l;
                case int i: return i;
                case double d: return (long)d;
                case string s: return long.TryParse(s, out long parsed) ? parsed : 0;
                default: return 0;
            }
        }

        /// <summary>
        /// A debit the server refused is a bug or an attack, never routine — the client
        /// checks affordability before recording one. Worth a loud log either way.
        /// </summary>
        static void WarnAboutRejections(IDictionary<string, object> reply)
        {
            if (reply == null) return;
            if (!reply.TryGetValue("rejected", out object raw) || !(raw is IEnumerable<object> list)) return;

            foreach (var id in list)
                Debug.LogWarning($"[Cloud] the server refused debit {id}; the client thought it was affordable");
        }

        /// <summary>
        /// Turns an SDK exception into something the sync can act on. The distinction
        /// that matters is retryable versus not: a network blip should be tried again
        /// on the next sync, while a rejected write never will be and should stop.
        /// </summary>
        static CloudResult Classify(Exception e, string what)
        {
            var inner = e is AggregateException aggregate ? aggregate.Flatten().InnerException ?? e : e;

            if (inner is FirebaseException auth && IsAuthError(auth))
            {
                switch ((AuthError)auth.ErrorCode)
                {
                    // The player linked this provider on another device. Expected, not
                    // a fault, and the only failure here the UI has to talk about.
                    case AuthError.CredentialAlreadyInUse:
                    case AuthError.AccountExistsWithDifferentCredentials:
                    case AuthError.EmailAlreadyInUse:
                        return CloudResult.Failed(CloudFailure.AlreadyLinkedElsewhere, auth.Message);

                    // Backing out of the consent screen is a choice, not an error, so it
                    // is reported as retryable and logged quietly.
                    case AuthError.Cancelled:
                    case AuthError.WebContextCancelled:
                        return CloudResult.Failed(CloudFailure.Offline, "cancelled by the player");

                    case AuthError.NetworkRequestFailed:
                        return CloudResult.Failed(CloudFailure.Offline, auth.Message);
                }
            }

            if (inner is FunctionsException functions)
            {
                switch (functions.ErrorCode)
                {
                    case FunctionsErrorCode.Unauthenticated:
                        return CloudResult.Failed(CloudFailure.Unauthenticated, functions.Message);
                    case FunctionsErrorCode.PermissionDenied:
                    case FunctionsErrorCode.InvalidArgument:
                    case FunctionsErrorCode.FailedPrecondition:
                        return CloudResult.Failed(CloudFailure.Rejected, functions.Message);
                    case FunctionsErrorCode.Unavailable:
                    case FunctionsErrorCode.DeadlineExceeded:
                        return CloudResult.Failed(CloudFailure.Offline, functions.Message);
                }
            }

            if (inner is FirestoreException firestore)
            {
                switch (firestore.ErrorCode)
                {
                    case FirestoreError.PermissionDenied:
                        // Almost always the security rules doing their job. Logged at
                        // error level because in a shipped build it means a write the
                        // client believed was valid is being refused every sync.
                        Debug.LogError($"[Cloud] {what} denied by security rules: {firestore.Message}");
                        return CloudResult.Failed(CloudFailure.Rejected, firestore.Message);
                    case FirestoreError.Unauthenticated:
                        return CloudResult.Failed(CloudFailure.Unauthenticated, firestore.Message);
                    case FirestoreError.Unavailable:
                    case FirestoreError.DeadlineExceeded:
                        return CloudResult.Failed(CloudFailure.Offline, firestore.Message);
                }
            }

            Debug.LogWarning($"[Cloud] {what} failed: {inner.Message}");
            return CloudResult.Failed(CloudFailure.Error, inner.Message);
        }
    }
}
#endif
