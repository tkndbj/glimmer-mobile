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
    public sealed class FirebaseCloudSaveBackend : ICloudSaveBackend, Social.IGroveBoardBackend
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
                                    : new CloudIdentity(user.UserId, IsLinked(user), Label(user));
            }
        }

        /// <summary>
        /// What to show a player who is deciding which of their accounts this phone is on.
        ///
        /// <para>
        /// Email first, and that ordering is the whole value of it: two Google accounts
        /// belonging to one person routinely carry the same display name, so a panel labelled
        /// with the name would say the same thing on both sides of a switch. Apple only hands
        /// over an address on the first authorisation, so a returning Apple player falls
        /// through to the name, and an anonymous account has neither and gets nothing — which
        /// the screen reads as "no account to name" rather than as an empty field.
        /// </para>
        /// </summary>
        static string Label(FirebaseUser user)
        {
            if (user == null || user.IsAnonymous) return string.Empty;

            if (!string.IsNullOrEmpty(user.Email)) return user.Email;
            if (!string.IsNullOrEmpty(user.DisplayName)) return user.DisplayName;

            // The provider's own record, for the case where the account carries neither.
            foreach (var provider in user.ProviderData)
            {
                if (provider == null) continue;
                if (!string.IsNullOrEmpty(provider.Email)) return provider.Email;
                if (!string.IsNullOrEmpty(provider.DisplayName)) return provider.DisplayName;
            }

            return string.Empty;
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

                return (CloudResult.Success, new CloudIdentity(user.UserId, IsLinked(user), Label(user)));
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

                var (upgraded, refusal) = await NativeIfRequiredAsync(credential);
                if (refusal.HasValue) return (refusal.Value, CloudIdentity.None);
                credential = upgraded;

                if (credential.HasToken)
                    await _auth.CurrentUser.LinkWithCredentialAsync(ToCredential(credential));
                else
                    await _auth.CurrentUser.LinkWithProviderAsync(Provider(credential.ProviderId));

                var user = _auth.CurrentUser;
                return (CloudResult.Success, new CloudIdentity(user.UserId, IsLinked(user), Label(user)));
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
                var (upgraded, refusal) = await NativeIfRequiredAsync(credential);
                if (refusal.HasValue) return (refusal.Value, CloudIdentity.None);
                credential = upgraded;

                if (credential.HasToken)
                    await _auth.SignInAndRetrieveDataWithCredentialAsync(ToCredential(credential));
                else
                    await _auth.SignInWithProviderAsync(Provider(credential.ProviderId));

                var user = _auth.CurrentUser;
                if (user == null)
                    return (CloudResult.Failed(CloudFailure.Unauthenticated, "no user after sign in"),
                            CloudIdentity.None);

                return (CloudResult.Success, new CloudIdentity(user.UserId, IsLinked(user), Label(user)));
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
        /// <summary>
        /// Fills in the identity token for the one provider that cannot use the generic path.
        ///
        /// <para>
        /// <b>This is not an optimisation and it is not optional.</b> FirebaseAuth on iOS calls
        /// <c>fatalError</c> — not an exception, an immediate process kill no <c>catch</c> can
        /// intercept — the moment <c>apple.com</c> reaches <see cref="Provider"/>:
        /// <c>"Sign in with Apple is not supported via generic IDP"</c>. So the check has to
        /// happen <em>before</em> the call rather than around it, which is the whole reason this
        /// is a separate step instead of a try/catch at the call site.
        /// </para>
        /// <para>
        /// Everything else keeps the path it had. Google uses the generic provider on both
        /// platforms, Apple uses it on Android, and a credential that already carries a token
        /// is returned untouched — so a caller that obtained one some other way still works.
        /// </para>
        /// <para>
        /// A cancelled sheet is reported as <see cref="CloudFailure.Cancelled"/> rather than an
        /// error, for the reason <c>SignInWithCredentialAsync</c> gives one comment further
        /// down: closing a consent screen is the most ordinary outcome in the flow, and calling
        /// it a failure tells a player their progress could not be saved because they changed
        /// their mind.
        /// </para>
        /// </summary>
        static async Task<(LinkCredential credential, CloudResult? refusal)> NativeIfRequiredAsync(
            LinkCredential credential)
        {
            if (credential.HasToken) return (credential, null);

            if (credential.ProviderId == LinkCredential.Apple && AppleSignIn.IsSupported)
            {
                var apple = await AppleSignIn.RequestAsync();

                switch (apple.Outcome)
                {
                    case AppleSignIn.Outcome.Succeeded:
                        return (new LinkCredential(LinkCredential.Apple, apple.IdToken,
                                                   apple.AuthorizationCode, apple.RawNonce), null);

                    case AppleSignIn.Outcome.Cancelled:
                        return (credential, CloudResult.Failed(CloudFailure.Cancelled,
                                                               "sign in with Apple was cancelled"));

                    default:
                        return (credential, CloudResult.Failed(CloudFailure.Rejected,
                                                               apple.Error ?? "sign in with Apple failed"));
                }
            }

            if (credential.ProviderId == LinkCredential.Google && GoogleSignIn.IsSupported)
            {
                var google = await GoogleSignIn.RequestAsync(GoogleClientId);

                switch (google.Outcome)
                {
                    case GoogleSignIn.Outcome.Succeeded:
                        return (new LinkCredential(LinkCredential.Google, google.IdToken,
                                                   google.AccessToken), null);

                    case GoogleSignIn.Outcome.Cancelled:
                        return (credential, CloudResult.Failed(CloudFailure.Cancelled,
                                                               "signing in with Google was cancelled"));

                    default:
                        return (credential, CloudResult.Failed(CloudFailure.Rejected,
                                                               google.Error ?? "signing in with Google failed"));
                }
            }

            return (credential, null);
        }

        /// <summary>
        /// The <em>iOS</em> OAuth client id.
        ///
        /// <para>
        /// Deliberately not the web client the hosted handler used: a native PKCE flow
        /// authenticates as the iOS client, which is the one whose reversed-id redirect scheme
        /// is registered in this app's <c>Info.plist</c>. It is read out of the bundled
        /// <c>GoogleService-Info.plist</c> by <see cref="GoogleSignIn.ClientId"/> rather than
        /// written down here, because Firebase's managed <c>AppOptions</c> does not expose it
        /// and a copy in C# is a copy that can drift from the plist the same tool generated.
        /// </para>
        /// </summary>
        static string GoogleClientId => GoogleSignIn.ClientId;

        static FederatedOAuthProvider Provider(string providerId)
        {
            var data = new FederatedOAuthProviderData { ProviderId = providerId };

            // Only what the account actually needs, and the address is now part of that.
            // Nothing is stored and nothing is sent anywhere — see CloudIdentity.Label — but
            // switching between two of one person's own accounts is what this flow is for, and
            // two Google accounts belonging to the same person routinely carry the same display
            // name. Without the address the panel cannot tell them apart, which is the whole
            // difficulty a player reported. Both consent screens list it in one line.
            data.Scopes = providerId == LinkCredential.Apple
                ? new List<string> { "name", "email" }
                : new List<string> { "profile", "email" };

            return new FederatedOAuthProvider(data);
        }

        static Credential ToCredential(LinkCredential credential)
            => credential.ProviderId == LinkCredential.Apple
                ? OAuthProvider.GetCredential(LinkCredential.Apple, credential.IdToken,
                                              credential.RawNonce, credential.AccessToken)
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

        // ---------------------------------------------------------- the grove board
        /// <summary>Where a published grove lives. Client-readable, server-written.</summary>
        const string GrovesCollection = "groves";

        /// <summary>Reserved keeper names, keyed by the fold. See functions/src/names.ts.</summary>
        const string NamesCollection = "names";

        /// <summary>Where the published boards live, one document each.</summary>
        const string BoardsCollection = "leaderboards";

        /// <summary>
        /// Asks the server to rebuild this account's card.
        ///
        /// <para>
        /// The request body is empty and stays empty. Everything on a card is recomputed by
        /// the function from the save document it reads with its own credentials — see
        /// <c>functions/src/grove.ts</c> — so there is nothing here for a modified client to
        /// put its thumb on. The reply carries the card that was actually written, which is
        /// what the profile draws afterwards rather than its own prediction.
        /// </para>
        /// </summary>
        public async Task<(CloudResult result, Social.GroveCard card)> PublishGroveAsync(
            string userId, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), Social.GroveCard.Empty);

            if (string.IsNullOrEmpty(userId))
                return (CloudResult.Failed(CloudFailure.Unauthenticated, "no user id"), Social.GroveCard.Empty);

            try
            {
                var reply = await CallAsync("publishGrove", new Dictionary<string, object>());
                return (CloudResult.Success, ReadCard(userId, ReadMap(reply, "card")));
            }
            catch (Exception e)
            {
                return (Classify(e, "publish grove"), Social.GroveCard.Empty);
            }
        }

        /// <summary>
        /// Takes this account's card down.
        ///
        /// An account that has no card is a success rather than an error — the function says
        /// so and this passes it through, because a withdrawal that can never succeed is a
        /// device retrying for the life of the account (invariant 13a).
        /// </summary>
        public async Task<CloudResult> WithdrawGroveAsync(
            string userId, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable");

            if (string.IsNullOrEmpty(userId))
                return CloudResult.Failed(CloudFailure.Unauthenticated, "no user id");

            try
            {
                await CallAsync("withdrawGrove", new Dictionary<string, object>());
                return CloudResult.Success;
            }
            catch (Exception e)
            {
                return Classify(e, "withdraw grove");
            }
        }

        /// <summary>
        /// Reads who holds a reserved name.
        ///
        /// <para>
        /// A direct document read by id, deliberately — this runs while somebody is typing, so
        /// it is the one call in the whole feature whose cost could ever matter, and a callable
        /// here would add a function invocation and a cold start to every pause in a text
        /// field. One read, no index, and the same price at any player count.
        /// </para>
        /// <para>
        /// An absent document is a success with nobody holding it, which is the ordinary answer
        /// and must never be an error: the overwhelming majority of names anybody types are
        /// free, and a "free" that arrived as an exception would be a free name reported as a
        /// fault.
        /// </para>
        /// </summary>
        public async Task<(CloudResult result, string holderId)> ReadNameHolderAsync(
            string nameKey, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), string.Empty);

            if (string.IsNullOrEmpty(nameKey))
                return (CloudResult.Failed(CloudFailure.Rejected, "no name key"), string.Empty);

            try
            {
                var snapshot = await _db.Collection(NamesCollection).Document(nameKey).GetSnapshotAsync();
                if (!snapshot.Exists) return (CloudResult.Success, string.Empty);

                var data = snapshot.ToDictionary();
                string holder = data != null && data.TryGetValue("uid", out object uid) ? uid as string : null;

                return (CloudResult.Success, holder ?? string.Empty);
            }
            catch (Exception e)
            {
                return (Classify(e, "read name holder"), string.Empty);
            }
        }

        /// <summary>
        /// Takes a name for this account.
        ///
        /// The one call here that is a function rather than a document operation, because it is
        /// the only one that has to be adjudicated: the reservation is created and the previous
        /// one released in a single transaction, which no client write could ever be.
        /// </summary>
        public async Task<(CloudResult result, Social.NameClaim claim)> ClaimNameAsync(
            string storedName, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), Social.NameClaim.Unavailable);

            try
            {
                var reply = await CallAsync("claimName", new Dictionary<string, object>
                {
                    { "name", storedName ?? string.Empty },
                });

                return (CloudResult.Success, ReadClaim(reply));
            }
            catch (Exception e)
            {
                return (Classify(e, "claim name"), Social.NameClaim.Unavailable);
            }
        }

        /// <summary>
        /// Reports a keeper's name.
        ///
        /// A function rather than a document write, for <c>ClaimNameAsync</c>'s reason and one
        /// of its own: the record and the count the threshold reads have to move together, and
        /// a takedown decided by a number the client writes is a takedown anybody can trigger.
        /// </summary>
        public async Task<(CloudResult result, Social.NameReportOutcome outcome)> ReportKeeperNameAsync(
            string keeperId, CancellationToken cancellation = default)
        {
            if (string.IsNullOrEmpty(keeperId))
                return (CloudResult.Failed(CloudFailure.Rejected, "no keeper"),
                        Social.NameReportOutcome.Unavailable);

            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"),
                        Social.NameReportOutcome.Unavailable);

            try
            {
                var reply = await CallAsync("reportKeeperName", new Dictionary<string, object>
                {
                    { "keeperId", keeperId },
                });

                return (CloudResult.Success, ReadReport(reply));
            }
            catch (Exception e)
            {
                return (Classify(e, "report name"), Social.NameReportOutcome.Unavailable);
            }
        }

        /// <summary>
        /// Reads a report reply.
        ///
        /// An outcome this build does not recognise is read as
        /// <see cref="Social.NameReportOutcome.Reported"/> rather than as a failure, which is
        /// the opposite of <see cref="ReadClaim"/> and deliberate. A claim that is not
        /// understood must not be treated as settled, because something local depends on it; a
        /// report has no local consequence at all, and the one thing an older client must not
        /// do is tell somebody their report failed when the server took it.
        /// </summary>
        static Social.NameReportOutcome ReadReport(IDictionary<string, object> reply)
        {
            if (reply == null) return Social.NameReportOutcome.Unavailable;

            string outcome = reply.TryGetValue("outcome", out object o) ? o as string : null;

            switch (outcome)
            {
                case "duplicate": return Social.NameReportOutcome.Duplicate;
                case "throttled": return Social.NameReportOutcome.Throttled;
                default:          return Social.NameReportOutcome.Reported;
            }
        }

        /// <summary>
        /// Reads a claim reply.
        ///
        /// An outcome this build does not recognise is read as
        /// <see cref="Social.NameClaimOutcome.Unavailable"/> rather than as a refusal — an
        /// older client meeting a newer server must fall back to "nothing was decided here",
        /// which leaves the rename local and lets the next publish settle it, rather than
        /// telling somebody their name was rejected for a reason it cannot name.
        /// </summary>
        static Social.NameClaim ReadClaim(IDictionary<string, object> reply)
        {
            if (reply == null) return Social.NameClaim.Unavailable;

            string outcome = reply.TryGetValue("outcome", out object o) ? o as string : null;

            var parsed = Social.NameClaimOutcome.Unavailable;
            switch (outcome)
            {
                case "claimed":   parsed = Social.NameClaimOutcome.Claimed; break;
                case "unchanged": parsed = Social.NameClaimOutcome.Unchanged; break;
                case "taken":     parsed = Social.NameClaimOutcome.Taken; break;
                case "refused":   parsed = Social.NameClaimOutcome.Refused; break;
                case "cooldown":  parsed = Social.NameClaimOutcome.Cooldown; break;
            }

            return new Social.NameClaim
            {
                Outcome = parsed,
                Name = (reply.TryGetValue("name", out object n) ? n as string : null) ?? string.Empty,
                Key = (reply.TryGetValue("key", out object k) ? k as string : null) ?? string.Empty,
                CooldownSeconds = (int)ReadLong(reply, "cooldownSeconds"),
            };
        }

        /// <summary>
        /// Reads one keeper's published grove.
        ///
        /// A direct document read rather than a callable: it is a public document by design,
        /// the rules already say who may read it, and routing it through a function would add
        /// an invocation and a cold start to the one interaction on the board that has to feel
        /// immediate. An absent card is success with an empty answer — the owner may have
        /// opted out between the board being built and the row being tapped, which is ordinary
        /// rather than a fault.
        /// </summary>
        public async Task<(CloudResult result, Social.GroveCard card)> ReadGroveCardAsync(
            string ownerId, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), Social.GroveCard.Empty);

            if (string.IsNullOrEmpty(ownerId))
                return (CloudResult.Failed(CloudFailure.Rejected, "no owner id"), Social.GroveCard.Empty);

            try
            {
                var snapshot = await _db.Collection(GrovesCollection).Document(ownerId).GetSnapshotAsync();
                if (!snapshot.Exists) return (CloudResult.Success, Social.GroveCard.Empty);

                return (CloudResult.Success, ReadCard(ownerId, snapshot.ToDictionary()));
            }
            catch (Exception e)
            {
                return (Classify(e, "read grove card"), Social.GroveCard.Empty);
            }
        }

        /// <summary>Reads one published board. One document, whole.</summary>
        public async Task<(CloudResult result, Social.LeaderboardBoard board)> ReadLeaderboardAsync(
            string boardId, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"),
                        Social.LeaderboardBoard.None);

            if (!Social.LeaderboardBoard.IsKnown(boardId))
                return (CloudResult.Failed(CloudFailure.Rejected, "unknown board"),
                        Social.LeaderboardBoard.None);

            try
            {
                var snapshot = await _db.Collection(BoardsCollection).Document(boardId).GetSnapshotAsync();
                if (!snapshot.Exists)
                    return (CloudResult.Success, new Social.LeaderboardBoard(boardId, null, 0L, 0));

                var document = snapshot.ToDictionary();
                var rows = new List<Social.LeaderboardEntry>();

                if (document.TryGetValue("entries", out object raw) && raw is IEnumerable<object> list)
                {
                    int rank = 0;
                    foreach (var element in list)
                    {
                        if (!(element is IDictionary<string, object> entry)) continue;
                        rank++;

                        string ownerId = Text(entry, "uid");
                        if (ownerId.Length == 0) continue;

                        // The rank is the row's position rather than a field, so a board
                        // written with a gap cannot draw two keepers at the same place.
                        rows.Add(new Social.LeaderboardEntry(
                            rank, ownerId, Text(entry, "name"), Text(entry, "avatar"),
                            (int)ReadLong(entry, "level"), ReadLong(entry, "score"),
                            (int)ReadLong(entry, "stars")));

                        if (rows.Count >= Social.LeaderboardBoard.MaxRows) break;
                    }
                }

                return (CloudResult.Success,
                        new Social.LeaderboardBoard(boardId, rows,
                                                    ReadLong(document, "builtUnix"),
                                                    (int)ReadLong(document, "population")));
            }
            catch (Exception e)
            {
                return (Classify(e, "read leaderboard"), Social.LeaderboardBoard.None);
            }
        }

        /// <summary>
        /// Reads the published distribution of grove worth.
        ///
        /// <see cref="ReadGroveStatsAsync"/>'s twin in every respect that matters: one public
        /// document, no sign-in, and every failure an empty answer rather than a propagated
        /// exception, because nothing anywhere waits on it.
        /// </summary>
        public async Task<(CloudResult result, Social.GroveRankTable table,
                           Dictionary<string, int> population, long builtUnix)> ReadGroveRanksAsync(
            CancellationToken cancellation = default)
        {
            var noPopulation = new Dictionary<string, int>();

            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"),
                        Social.GroveRankTable.None, noPopulation, 0L);

            try
            {
                var snapshot = await _db.Collection("config").Document("groveRanks").GetSnapshotAsync();
                if (!snapshot.Exists)
                    return (CloudResult.Success, Social.GroveRankTable.None, noPopulation, 0L);

                var document = snapshot.ToDictionary();
                var table = Social.GroveRankTable.None;

                if (document.TryGetValue("deciles", out object raw) && raw is IEnumerable<object> list)
                {
                    var deciles = new List<long>(9);
                    foreach (var value in list) deciles.Add(ToLong(value));

                    // A table that is not ascending is not a decile table, and interpolating
                    // through one produces percentages at random. ReadGroveStatsAsync refuses
                    // the same way, for the same reason.
                    bool ascending = deciles.Count == 9;
                    for (int i = 0; ascending && i < deciles.Count; i++)
                        if (deciles[i] < 1L || (i > 0 && deciles[i] < deciles[i - 1])) ascending = false;

                    if (ascending)
                        table = new Social.GroveRankTable((int)ReadLong(document, "samples"), deciles);
                }

                var population = new Dictionary<string, int>();
                if (document.TryGetValue("population", out object rawPop) &&
                    rawPop is IDictionary<string, object> counts)
                {
                    foreach (var pair in counts)
                        if (Social.GroveLeague.IsKnown(pair.Key))
                            population[pair.Key] = (int)ToLong(pair.Value);
                }

                return (CloudResult.Success, table, population, ReadLong(document, "builtUnix"));
            }
            catch (Exception e)
            {
                return (Classify(e, "read grove ranks"), Social.GroveRankTable.None, noPopulation, 0L);
            }
        }

        /// <summary>
        /// Turns a card document into a <see cref="Social.GroveCard"/>.
        ///
        /// <para>
        /// Sanitising rather than trusting, which is <c>HomesteadMapper</c>'s stance and is
        /// wanted twice over here: this document was built from another player's save, and a
        /// visitor may be a content drop behind the keeper they are visiting. A malformed row
        /// is skipped rather than poisoning the card, and an id this build has never heard of
        /// is carried through — <see cref="Social.GroveCard.PieceAt"/> resolves it to an
        /// invalid piece, which every drawing path already skips.
        /// </para>
        /// </summary>
        static Social.GroveCard ReadCard(string ownerId, IDictionary<string, object> document)
        {
            if (document == null) return Social.GroveCard.Empty;

            var land = new List<string>();
            if (document.TryGetValue("land", out object rawLand) && rawLand is IEnumerable<object> landList)
                foreach (var id in landList)
                    if (id is string text && text.Length > 0) land.Add(text);

            var placed = new Dictionary<string, Homestead.Placement>(StringComparer.Ordinal);
            if (document.TryGetValue("placed", out object rawPlaced) &&
                rawPlaced is IDictionary<string, object> rows)
            {
                foreach (var pair in rows)
                {
                    if (string.IsNullOrEmpty(pair.Key)) continue;

                    // Two shapes, because a flipped piece is the exception: a bare string is
                    // the piece id, and a map carries the facing with it. That keeps the
                    // common row to a single value and the document to about a third of what
                    // a uniform map would cost across a full floor.
                    switch (pair.Value)
                    {
                        case string pieceId when pieceId.Length > 0:
                            placed[pair.Key] = new Homestead.Placement(pieceId, 0L, false);
                            break;

                        case IDictionary<string, object> entry:
                            string id = Text(entry, "piece");
                            if (id.Length == 0) break;
                            placed[pair.Key] = new Homestead.Placement(id, 0L, ReadLong(entry, "flip") != 0L);
                            break;
                    }
                }
            }

            return new Social.GroveCard(
                ownerId,
                Text(document, "name"),
                Text(document, "avatar"),
                (int)ReadLong(document, "level"),
                ReadLong(document, "score"),
                (int)ReadLong(document, "stars"),
                Text(document, "league"),
                ReadLong(document, "builtUnix"),
                Text(document, "dwelling"),
                land,
                placed);
        }

        static IDictionary<string, object> ReadMap(IDictionary<string, object> reply, string key)
            => reply != null && reply.TryGetValue(key, out object raw)
                ? raw as IDictionary<string, object>
                : null;

        static string Text(IDictionary<string, object> map, string key)
            => map != null && map.TryGetValue(key, out object value) && value is string text
                ? text
                : string.Empty;

        static long ToLong(object value)
        {
            switch (value)
            {
                case long l: return l;
                case int i: return i;
                case double d: return (long)d;
                case float f: return (long)f;
                default: return 0L;
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

                if (entry.TryGetValue("confirmedGrantIds", out object grantIds) &&
                    grantIds is IEnumerable<object> grantList)
                {
                    foreach (var id in grantList)
                        if (id is string s && s.Length > 0) state.ConfirmedGrantIds.Add(s);
                }

                // Refunded heart containers. Repeated on every currency row — see
                // CloudWalletState.RevokedContainers — and absent entirely on a deployment
                // that predates the field, which reads as "nothing was refunded" and is the
                // right answer for every account until one is.
                if (entry.TryGetValue("containersRevoked", out object revoked) &&
                    revoked is IEnumerable<object> revokedList)
                {
                    foreach (var id in revokedList)
                        if (id is string s && s.Length > 0) state.RevokedContainers.Add(s);
                }

                // The bonus wheel's position. Read as "did the key arrive" first and as a number
                // second, because a fresh account's honest answer is zero and a deployment that
                // predates the field also sends nothing — and only one of those two means the
                // wheel may be drawn. See CloudWalletState.CarriesWheel.
                if (entry.TryGetValue("wheelSpins", out object spins) && spins != null)
                {
                    state.CarriesWheel = true;
                    state.WheelSpins = (int)ReadLong(entry, "wheelSpins");
                    state.WheelDay = (int)ReadLong(entry, "wheelDay");
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

                    // Backing out of the consent screen is a choice, not an error. It used to
                    // be reported as Offline, which put "no internet connection" on screen in
                    // front of somebody who had simply changed their mind.
                    case AuthError.Cancelled:
                    case AuthError.WebContextCancelled:
                        return CloudResult.Failed(CloudFailure.Cancelled, "cancelled by the player");

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
