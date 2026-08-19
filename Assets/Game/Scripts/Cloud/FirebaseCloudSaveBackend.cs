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
        /// Brings the SDK up and reports whoever Firebase restored, creating nobody.
        ///
        /// <para>
        /// Firebase persists the signed-in user and restores it during
        /// <see cref="EnsureReadyAsync"/>, so for the overwhelmingly common case — a launch by
        /// somebody who signed in months ago — this returns their account without a network
        /// round trip. An empty answer means the session is genuinely gone, which is a fact
        /// worth reporting rather than papering over with a new anonymous account; see the
        /// interface for what that used to cost.
        /// </para>
        /// </summary>
        public async Task<(CloudResult result, CloudIdentity identity)> ResumeAsync(
            CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), CloudIdentity.None);

            return (CloudResult.Success, CurrentIdentity);
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
                // Deliberately NOT signed out first, and this is the one line in the file
                // most worth leaving alone.
                //
                // It used to sign out here, so that an abandoned anonymous account could not
                // linger as the current user if the new sign-in failed halfway. That reasoning
                // only holds while this is reached from the one flow that has already decided
                // to abandon the current account. It is now also how a player *switches*
                // accounts and how a device recovers from an interrupted switch — and there,
                // signing out first means the single most ordinary outcome in the whole flow,
                // a player closing the Google sheet without choosing, permanently ends the
                // session they were happily in. The save then names an account nothing can
                // sign back into without another consent screen, and every sync is refused by
                // AccountGate until they work that out for themselves.
                //
                // Firebase replaces the current user atomically on success and leaves it alone
                // on failure, which is the behaviour that is actually wanted: cancelling costs
                // nothing at all.
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
        /// Pulls the <see cref="AuthError"/> out of whichever exception type Auth chose
        /// to throw. Firestore and Functions do not derive from
        /// <see cref="FirebaseException"/> — they throw their own types — so one reaching
        /// here came from Auth, and the <c>Enum.IsDefined</c> check only guards against a
        /// code outside the enum.
        ///
        /// <para>
        /// <b><see cref="FirebaseAccountLinkException"/> is the trap.</b> Despite the name
        /// it derives from <see cref="Exception"/> directly, <i>not</i> from
        /// <see cref="FirebaseException"/> — verified by reflection over
        /// Firebase.Auth.dll 13.15.0. It carries the same <c>int ErrorCode</c>, but a type
        /// check on <c>FirebaseException</c> misses it entirely. It is what the link calls
        /// throw for precisely the case this screen exists to handle — a provider already
        /// attached to another grove — so missing it silently downgrades an expected,
        /// actionable outcome into <see cref="CloudFailure.Error"/> and the player is told
        /// "something went wrong" about a situation the game knows exactly how to resolve.
        /// </para>
        /// </summary>
        static bool TryAuthError(Exception e, out AuthError error)
        {
            int? code = e switch
            {
                FirebaseAccountLinkException link => link.ErrorCode,
                FirebaseException firebase => firebase.ErrorCode,
                _ => null,
            };

            if (code is int value && Enum.IsDefined(typeof(AuthError), value))
            {
                error = (AuthError)value;
                return true;
            }

            error = default;
            return false;
        }

        static bool IsLinked(FirebaseUser user)
        {
            if (user == null) return false;
            foreach (var provider in user.ProviderData)
                if (provider.ProviderId != "firebase") return true;
            return false;
        }

        // ----------------------------------------------------------- grove stats
        /// <summary>
        /// The population's move counts, from the one public document a scheduled job
        /// writes.
        ///
        /// <para>
        /// No sign-in and no user id: this is the same table for everybody, and requiring
        /// authentication for it would mean a first launch could not show it. The security
        /// rules make <c>config/stats</c> world-readable and client-unwritable for the same
        /// reason they do for <c>config/progression</c>.
        /// </para>
        /// <para>
        /// Every failure returns an empty table rather than propagating, and a malformed
        /// entry is skipped rather than poisoning the rest. Nothing on any screen depends
        /// on this arriving — the worst outcome of it being wrong or missing is one
        /// sentence not being drawn — so it must never be able to fail a launch or a sync.
        /// </para>
        /// </summary>
        public async Task<(CloudResult result, Dictionary<Content.LevelId, Social.LevelStats> stats)>
            ReadGroveStatsAsync(CancellationToken cancellation = default)
        {
            var empty = new Dictionary<Content.LevelId, Social.LevelStats>();

            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), empty);

            try
            {
                var snapshot = await _db.Collection("config").Document("stats").GetSnapshotAsync();
                if (!snapshot.Exists) return (CloudResult.Success, empty);

                var document = snapshot.ToDictionary();
                if (!document.TryGetValue("levels", out object raw) ||
                    !(raw is Dictionary<string, object> levels))
                {
                    return (CloudResult.Success, empty);
                }

                var table = new Dictionary<Content.LevelId, Social.LevelStats>(levels.Count);

                foreach (var pair in levels)
                {
                    if (!Content.LevelId.TryParse(pair.Key, out var levelId, out _)) continue;
                    if (!(pair.Value is Dictionary<string, object> entry)) continue;

                    int samples = ReadInt(entry, "samples");
                    if (!(entry.TryGetValue("deciles", out object rawDeciles) &&
                          rawDeciles is List<object> list) || list.Count != 9)
                    {
                        continue;
                    }

                    var deciles = new int[9];
                    bool ascending = true;

                    for (int i = 0; i < 9; i++)
                    {
                        deciles[i] = ToInt(list[i]);

                        // A table that is not ascending is not a decile table, and
                        // interpolating through it would produce percentages at random.
                        if (deciles[i] < 1 || (i > 0 && deciles[i] < deciles[i - 1])) ascending = false;
                    }

                    if (!ascending) continue;

                    table[levelId] = new Social.LevelStats(samples, deciles);
                }

                return (CloudResult.Success, table);
            }
            catch (Exception e)
            {
                return (Classify(e, "read grove stats"), empty);
            }
        }

        static int ReadInt(Dictionary<string, object> document, string key)
            => document.TryGetValue(key, out object value) ? ToInt(value) : 0;

        /// <summary>
        /// Firestore hands numbers back as <c>long</c> or <c>double</c> depending on how
        /// they were written, and a cast that assumes one of them throws on the other.
        /// </summary>
        static int ToInt(object value)
        {
            switch (value)
            {
                case long l: return (int)l;
                case int i: return i;
                case double d: return (int)d;
                case float f: return (int)f;
                default: return 0;
            }
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

        /// <summary>
        /// Offers up awards the client has already applied, for the server to adjudicate.
        ///
        /// Only the ids travel with any authority. The amounts are sent because they make
        /// a support case legible — "the client thought this chest was worth 240" — and
        /// are otherwise ignored: <c>claimAwards</c> re-rolls each chest from the account
        /// id, the day and the index, and grants its own answer.
        /// </summary>
        public async Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitAwardsAsync(
            string userId, IReadOnlyList<GrantEntryDto> awards, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), Empty());

            if (awards == null || awards.Count == 0) return (CloudResult.Success, Empty());

            try
            {
                var payload = new List<object>(awards.Count);
                foreach (var award in awards)
                {
                    if (award == null || string.IsNullOrEmpty(award.id)) continue;
                    payload.Add(new Dictionary<string, object>
                    {
                        { "id", award.id },
                        { "claimedAmount", award.amount },
                        { "unix", award.unix },
                        { "reason", award.reason ?? string.Empty },
                    });
                }

                var reply = await CallAsync("claimAwards",
                                            new Dictionary<string, object> { { "awards", payload } });

                WarnAboutRejections(reply);
                return (CloudResult.Success, ReadWalletStates(reply));
            }
            catch (Exception e)
            {
                return (Classify(e, "claim awards"), Empty());
            }
        }

        public async Task<(CloudResult result, List<CloudWalletState> wallets, CloudRedemption redemption)>
            RedeemPurchaseAsync(string userId, PurchaseReceipt receipt,
                                CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"),
                        Empty(), CloudRedemption.Nothing);

            if (receipt == null || string.IsNullOrEmpty(receipt.TransactionId))
                return (CloudResult.Failed(CloudFailure.Rejected, "receipt has no transaction id"),
                        Empty(), CloudRedemption.Nothing);

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

                return (CloudResult.Success, ReadWalletStates(reply), ReadRedemption(reply));
            }
            catch (Exception e)
            {
                return (Classify(e, "redeem purchase"), Empty(), CloudRedemption.Nothing);
            }
        }

        /// <summary>
        /// What the server says this call granted.
        ///
        /// <para>
        /// A missing <c>granted</c> map reads as "nothing was granted" rather than as an
        /// error, which is the safe direction in both cases it can happen: a retry the
        /// server declined to pay twice, and a server that predates the field. Both should
        /// leave the balances adopted and no celebration shown, and both do.
        /// </para>
        /// </summary>
        static CloudRedemption ReadRedemption(IDictionary<string, object> reply)
        {
            var redemption = new CloudRedemption();
            if (reply == null) return redemption;

            if (reply.TryGetValue("alreadyGranted", out object already) && already is bool flag)
                redemption.AlreadyGranted = flag;

            if (!reply.TryGetValue("granted", out object raw) ||
                !(raw is IDictionary<string, object> granted))
                return redemption;

            foreach (var pair in granted)
            {
                if (string.IsNullOrEmpty(pair.Key)) continue;
                redemption.Granted[pair.Key] = ReadLong(granted, pair.Key);
            }

            return redemption;
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

                if (entry.TryGetValue("confirmedGrantIds", out object grantIds) &&
                    grantIds is IEnumerable<object> grantList)
                {
                    foreach (var id in grantList)
                        if (id is string s && s.Length > 0) state.ConfirmedGrantIds.Add(s);
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

            if (TryAuthError(inner, out var authError))
            {
                switch (authError)
                {
                    // The player linked this provider on another device. Expected, not
                    // a fault, and the only failure here the UI has to talk about.
                    case AuthError.CredentialAlreadyInUse:
                    case AuthError.AccountExistsWithDifferentCredentials:
                    case AuthError.EmailAlreadyInUse:
                        return CloudResult.Failed(CloudFailure.AlreadyLinkedElsewhere, inner.Message);

                    // Backing out of the consent screen is a choice, not an error, so it
                    // is reported as retryable and logged quietly.
                    case AuthError.Cancelled:
                    case AuthError.WebContextCancelled:
                        return CloudResult.Failed(CloudFailure.Offline, "cancelled by the player");

                    case AuthError.NetworkRequestFailed:
                        return CloudResult.Failed(CloudFailure.Offline, inner.Message);
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
