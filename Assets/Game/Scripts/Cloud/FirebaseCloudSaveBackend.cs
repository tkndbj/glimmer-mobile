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
    public sealed class FirebaseCloudSaveBackend : ICloudSaveBackend, Social.IGroveBoardBackend,
                                                   Referral.IReferralBackend
    {
        /// <summary>Must match <c>REGION</c> in the functions' config.ts.</summary>
        public const string FunctionsRegion = "europe-west1";

        const string PlayersCollection = "players";
        const string PrivateCollection = "private";
        const string WalletDocument = "wallet";

        /// <summary>
        /// How long any one call to the SDK may take before it is reported as
        /// <see cref="CloudFailure.Offline"/> and retried later.
        ///
        /// <para>
        /// <b>Every call, without exception, because the failure is the latch.</b> A Firestore
        /// write completes only when the backend acknowledges it, so on a connection that drops
        /// after the request went out it never completes at all — and one call that never
        /// completes holds <c>CloudSaveService</c>'s sync latch for the life of the process,
        /// which every later sync, switch and link reports as a failure of its own. A callable
        /// has a client timeout of its own; the auth exchanges and the document operations do
        /// not, and it is easier to prove "every call has one" than "the ones that need one
        /// have one". See <see cref="CloudCancel.Within"/>. The interactive provider sheets are
        /// the one thing not under a deadline: a player may take as long as they like to pick
        /// an account, and closing the sheet is its own answer.
        /// </para>
        /// </summary>
        const int ReadSeconds = 30;
        const int WriteSeconds = 30;
        const int AuthSeconds = 60;
        const int CallSeconds = 70;

        /// <summary>A deletion walks several collections; give it the room a cold start needs.</summary>
        const int DeleteSeconds = 120;

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
                // Shared rather than made here, because analytics starts at the same moment
                // and Firebase refuses any call made while a check is in flight. See
                // FirebaseReady - the refusal lands on whoever asked second, which has
                // already been this backend once, reported as a cloud fault.
                var status = await FirebaseReady.EnsureAsync();
                if (status != DependencyStatus.Available)
                {
                    Debug.LogWarning($"[Cloud] Firebase unavailable on this device ({status}); staying local");
                    _unavailable = true;
                    return false;
                }

                _app = FirebaseApp.DefaultInstance;
                _auth = FirebaseAuth.DefaultInstance;
                _db = FirebaseFirestore.DefaultInstance;

                // No offline cache, and this is the first thing said to the instance because
                // it can only be said before anything else is.
                //
                // The game has its own local save and its own merge; a sync is pull, join,
                // push, and every one of them retries on a backoff. Firestore's cache is a
                // second copy of the same documents that answers when the first cannot, and
                // every time it has answered it has answered wrongly: a restored Android backup
                // served another install's name, wallet and roster to a client that could not
                // authenticate (see LauncherManifest.xml); an offline pull served a stale save
                // that was then merged and queued as a write nobody could see; and a write
                // queued while offline survives an account switch in the previous account's
                // queue and is replayed the next time that account signs in, with a revision
                // the rules refuse. With the cache off, an offline read fails at once with
                // Unavailable — which the sync reports as Offline and retries when the app is
                // next foregrounded — and a write never outlives the call that made it.
                try { _db.Settings.PersistenceEnabled = false; }
                catch (Exception e) { Debug.LogWarning("[Cloud] could not turn the Firestore cache off: " + e.Message); }

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
                if (_auth.CurrentUser == null)
                    await CloudCancel.Within(_auth.SignInAnonymouslyAsync(), AuthSeconds, cancellation);

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
                if (_auth.CurrentUser == null)
                    await CloudCancel.Within(_auth.SignInAnonymouslyAsync(), AuthSeconds, cancellation);

                var (upgraded, refusal) = await NativeIfRequiredAsync(credential);
                if (refusal.HasValue) return (refusal.Value, CloudIdentity.None);
                credential = upgraded;

                if (credential.HasToken)
                    await CloudCancel.Within(_auth.CurrentUser.LinkWithCredentialAsync(ToCredential(credential)),
                                             AuthSeconds, cancellation);
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
                    await CloudCancel.Within(_auth.SignInAndRetrieveDataWithCredentialAsync(ToCredential(credential)),
                                             AuthSeconds, cancellation);
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
                var snapshot = await CloudCancel.Within(
                    _db.Collection("config").Document("stats").GetSnapshotAsync(), ReadSeconds, cancellation);
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

        // --------------------------------------------------------- the release gate
        /// <summary>
        /// What the deployment requires of a client on this platform, from the one public
        /// document a release publishes.
        ///
        /// <para>
        /// No sign-in and no user id, for <see cref="ReadGroveStatsAsync"/>'s reason and one
        /// sharper than it: the builds this gate exists to stop are quite often builds whose
        /// problem is that they can no longer talk to this deployment, and a wall behind
        /// authentication is a wall that cannot close on them. <c>config/release</c> is
        /// world-readable in <c>firestore.rules</c> exactly as <c>config/stats</c> is.
        /// </para>
        /// <para>
        /// <b>Every shape of "there is nothing here" succeeds with
        /// <see cref="Release.ReleaseRequirement.None"/></b> — an unseeded project, a document
        /// with no block for this platform, a block with no minimum. Only a genuine failure to
        /// reach Firestore is reported as one, because that is the single distinction
        /// <c>ReleaseGate</c> cannot make for itself and the one that decides whether a standing
        /// wall stays up.
        /// </para>
        /// <para>
        /// A published block that names no store link falls back to the platform's derived one,
        /// which exists on Android and cannot on iOS — see <c>ReleasePlatform.FallbackStoreUrl</c>.
        /// Whether what comes out of that is enforceable is not decided here; the requirement
        /// answers it, in one place, for every reader.
        /// </para>
        /// </summary>
        public async Task<(CloudResult result, Release.ReleaseRequirement requirement)> ReadReleaseAsync(
            string platform, CancellationToken cancellation = default)
        {
            // Answered without a round trip: a build with no store behind it has nowhere a wall
            // could send anybody, so there is nothing to read and nothing to enforce.
            if (string.IsNullOrEmpty(platform))
                return (CloudResult.Success, Release.ReleaseRequirement.None);

            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"),
                        Release.ReleaseRequirement.None);

            try
            {
                var snapshot = await CloudCancel.Within(
                    _db.Collection("config").Document("release").GetSnapshotAsync(), ReadSeconds, cancellation);

                if (!snapshot.Exists) return (CloudResult.Success, Release.ReleaseRequirement.None);

                var document = snapshot.ToDictionary();

                if (!document.TryGetValue(platform, out object raw) ||
                    !(raw is Dictionary<string, object> block))
                {
                    return (CloudResult.Success, Release.ReleaseRequirement.None);
                }

                int minimum = ReadInt(block, "minimum");

                string store = block.TryGetValue("store", out object rawStore) && rawStore is string named
                             ? named
                             : string.Empty;

                if (string.IsNullOrEmpty(store)) store = Release.ReleasePlatform.FallbackStoreUrl;

                return (CloudResult.Success, new Release.ReleaseRequirement(minimum, store));
            }
            catch (Exception e)
            {
                return (Classify(e, "read release"), Release.ReleaseRequirement.None);
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
                var snapshot = await CloudCancel.Within(PlayerDoc(userId).GetSnapshotAsync(), ReadSeconds, cancellation);

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
                    await CloudCancel.Within(
                        PlayerDoc(userId).SetAsync(FirestoreSaveMapper.ToDocument(snapshot), SetOptions.Overwrite),
                        WriteSeconds, cancellation);
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

                await CloudCancel.Within(PlayerDoc(userId).UpdateAsync(updates), WriteSeconds, cancellation);
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
                var reply = await CallAsync("getWallet", new Dictionary<string, object>(), cancellation);
                return (CloudResult.Success, ReadWalletStates(reply));
            }
            catch (Exception e)
            {
                return (Classify(e, "read wallet"), Empty());
            }
        }

        public async Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitSpendsAsync(
            string userId, IReadOnlyList<SpendSubmission> spends, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), Empty());

            if (spends == null || spends.Count == 0) return (CloudResult.Success, Empty());

            try
            {
                var payload = new List<object>(spends.Count);
                foreach (var submission in spends)
                {
                    var spend = submission.Spend;
                    if (spend == null || string.IsNullOrEmpty(spend.id)) continue;
                    if (string.IsNullOrEmpty(submission.Currency)) continue;

                    // The currency is the ledger's, never assumed. This line once read
                    // `Currency.Credits` for every debit — "one currency spends today" — and by
                    // the time gems were spent nothing here said so: every gem debit was taken
                    // from the server's credit balance and the season pass, priced in gems,
                    // was refused as underpaid on every sync for the life of the account.
                    payload.Add(new Dictionary<string, object>
                    {
                        { "id", spend.id },
                        { "currency", submission.Currency },
                        { "amount", spend.amount },
                        { "unix", spend.unix },
                        { "reason", spend.reason ?? string.Empty },
                    });
                }

                var reply = await CallAsync("submitSpends",
                                            new Dictionary<string, object> { { "spends", payload } },
                                            cancellation);

                WarnAboutRejections(reply);

                // Refusals ride back on every row, as an award's do: the ledger that holds the
                // entry drops it and the balance it took (see CloudWalletState.RejectedSpendIds).
                var states = ReadWalletStates(reply);
                var refused = Rejected(reply);
                foreach (var state in states) state.RejectedSpendIds.AddRange(refused);
                return (CloudResult.Success, states);
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
                                            new Dictionary<string, object> { { "awards", payload } },
                                            cancellation);

                // A refused claim is handed back on every row rather than warned about and
                // forgotten: the ledger drops it, or it is resubmitted and refused for ever.
                var states = ReadWalletStates(reply);
                var refused = Rejected(reply);
                foreach (var state in states) state.RejectedGrantIds.AddRange(refused);

                return (CloudResult.Success, states);
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
                }, cancellation);

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
        public async Task<(CloudResult result, Social.GrovePublication published)> PublishGroveAsync(
            string userId, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), Social.GrovePublication.Unproven);

            if (string.IsNullOrEmpty(userId))
                return (CloudResult.Failed(CloudFailure.Unauthenticated, "no user id"), Social.GrovePublication.Unproven);

            try
            {
                var reply = await CallAsync("publishGrove", new Dictionary<string, object>(), cancellation);

                // Absent and zero are different answers. A deployment that predates the field
                // reports nothing, and the client must not read that as "built from nothing"
                // — see GrovePublication for why that would be a retry loop.
                long revision = reply != null && reply.ContainsKey("revision") ? ReadLong(reply, "revision") : -1L;

                return (CloudResult.Success,
                        new Social.GrovePublication(ReadCard(userId, ReadMap(reply, "card")), revision));
            }
            catch (Exception e)
            {
                return (Classify(e, "publish grove"), Social.GrovePublication.Unproven);
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
                await CallAsync("withdrawGrove", new Dictionary<string, object>(), cancellation);
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
                var snapshot = await CloudCancel.Within(
                    _db.Collection(NamesCollection).Document(nameKey).GetSnapshotAsync(), ReadSeconds, cancellation);
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
                }, cancellation);

                return (CloudResult.Success, ReadClaim(reply));
            }
            catch (Exception e)
            {
                return (Classify(e, "claim name"), Social.NameClaim.Unavailable);
            }
        }

        /// <summary>
        /// Reports a keeper's name, or what they have built.
        ///
        /// <para>
        /// A function rather than a document write, for <c>ClaimNameAsync</c>'s reason and one
        /// of its own: the record and the count the threshold reads have to move together, and
        /// a takedown decided by a number the client writes is a takedown anybody can trigger.
        /// </para>
        /// <para>
        /// <b>The subject rides in the body rather than in the callable's name.</b> It was
        /// <c>reportKeeperName</c> until this drop and is <c>reportKeeper</c> now — a callable's
        /// name is not an id anything is keyed on, so renaming it cost a deploy, an invoker
        /// binding and a delete, which is a price only payable before a client ships. What the
        /// body buys is that a third subject is one word and no ops at all.
        /// </para>
        /// </summary>
        public async Task<(CloudResult result, Social.NameReportOutcome outcome)> ReportKeeperAsync(
            string keeperId, Social.ReportSubject subject,
            CancellationToken cancellation = default)
        {
            if (string.IsNullOrEmpty(keeperId))
                return (CloudResult.Failed(CloudFailure.Rejected, "no keeper"),
                        Social.NameReportOutcome.Unavailable);

            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"),
                        Social.NameReportOutcome.Unavailable);

            try
            {
                var reply = await CallAsync("reportKeeper", new Dictionary<string, object>
                {
                    { "keeperId", keeperId },

                    // Through `ReportSubjects.Wire`, which is the one place the spelling lives:
                    // the server keys a collection on this string, so a tidy-up here would file
                    // every later report into a collection nothing reads.
                    { "subject", Social.ReportSubjects.Wire(subject) },
                }, cancellation);

                return (CloudResult.Success, ReadReport(reply));
            }
            catch (Exception e)
            {
                return (Classify(e, "report keeper"), Social.NameReportOutcome.Unavailable);
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
                var snapshot = await CloudCancel.Within(
                    _db.Collection(GrovesCollection).Document(ownerId).GetSnapshotAsync(), ReadSeconds, cancellation);
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
                var snapshot = await CloudCancel.Within(
                    _db.Collection(BoardsCollection).Document(boardId).GetSnapshotAsync(), ReadSeconds, cancellation);
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
                        //
                        // Every row carries both figures whichever board it came off, because a
                        // row is the same row (`LeaderboardEntry.Wave`); which one is drawn is
                        // the board's decision, and an absent `wave` reads as nought exactly as
                        // it does on the card it was copied from.
                        rows.Add(new Social.LeaderboardEntry(
                            rank, ownerId, Text(entry, "name"), Text(entry, "avatar"),
                            (int)ReadLong(entry, "level"), ReadLong(entry, "score"),
                            (int)ReadLong(entry, "stars"), (int)ReadLong(entry, "wave"),
                            Text(entry, "rung")));

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
        /// Reads the published distributions: grove worth, and the Endless Watch's waves.
        ///
        /// <see cref="ReadGroveStatsAsync"/>'s twin in every respect that matters: one document,
        /// once a session, and every failure an empty answer rather than a propagated exception,
        /// because nothing anywhere waits on it.
        ///
        /// <para>
        /// <b>The wave pair is read exactly as the worth pair is, through one reader.</b> Two
        /// copies of "nine ascending numbers and a sample count" would be two places for the
        /// ascending check to be forgotten — and that check is what stands between a malformed
        /// document and a screen printing percentages drawn at random.
        /// </para>
        /// </summary>
        public async Task<(CloudResult result, Social.GroveRankPublication published)>
            ReadGroveRanksAsync(CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"),
                        Social.GroveRankPublication.None);

            try
            {
                var snapshot = await CloudCancel.Within(
                    _db.Collection("config").Document("groveRanks").GetSnapshotAsync(), ReadSeconds, cancellation);

                // An absent document is the ordinary first-day state rather than a failure: no
                // job has run yet, so there is nothing to say and every reader draws no standing.
                if (!snapshot.Exists)
                    return (CloudResult.Success, Social.GroveRankPublication.None);

                var document = snapshot.ToDictionary();

                var population = new Dictionary<string, int>();
                if (document.TryGetValue("population", out object rawPop) &&
                    rawPop is IDictionary<string, object> counts)
                {
                    foreach (var pair in counts)
                        if (Social.LeaderboardBoard.IsKnown(pair.Key))
                            population[pair.Key] = (int)ToLong(pair.Value);
                }

                // `waveDeciles` is additive: a document written before the Endless Watch shipped
                // simply has none, and an absent table is what every reader already treats as
                // "nothing to say" (invariant 19k's additive half).
                return (CloudResult.Success, new Social.GroveRankPublication(
                    ReadRankTable(document, "deciles", "samples"),
                    ReadRankTable(document, "waveDeciles", "waveSamples"),
                    population,
                    ReadLong(document, "builtUnix")));
            }
            catch (Exception e)
            {
                return (Classify(e, "read grove ranks"), Social.GroveRankPublication.None);
            }
        }

        /// <summary>
        /// Nine ascending numbers and the sample they were measured over, or
        /// <see cref="Social.GroveRankTable.None"/>.
        ///
        /// <b>A table that is not ascending is not a decile table</b>, and interpolating through
        /// one produces percentages at random — so it is refused outright rather than repaired.
        /// <c>ReadGroveStatsAsync</c> refuses the same way, for the same reason.
        /// </summary>
        static Social.GroveRankTable ReadRankTable(IDictionary<string, object> document,
                                                   string decileKey, string sampleKey)
        {
            if (!document.TryGetValue(decileKey, out object raw) || !(raw is IEnumerable<object> list))
                return Social.GroveRankTable.None;

            var deciles = new List<long>(9);
            foreach (var value in list) deciles.Add(ToLong(value));

            if (deciles.Count != 9) return Social.GroveRankTable.None;

            for (int i = 0; i < deciles.Count; i++)
                if (deciles[i] < 1L || (i > 0 && deciles[i] < deciles[i - 1]))
                    return Social.GroveRankTable.None;

            return new Social.GroveRankTable((int)ReadLong(document, sampleKey), deciles);
        }

        /// <summary>
        /// Turns a card document into a <see cref="Social.GroveCard"/>.
        ///
        /// <para>
        /// Sanitising rather than trusting, and wanted twice over here: this document was
        /// built from another player's save, and a visitor may be a content drop behind the
        /// keeper they are visiting. A malformed row is skipped rather than poisoning the card,
        /// and an id this build has never heard of is carried through — <c>WardLine.Resolve</c>
        /// falls back to this build's own starter, which every drawing path already handles.
        ///
        /// <para>
        /// <b>Fields the Grovement used to write are simply not read.</b> The server still puts
        /// <c>score</c>, <c>land</c>, <c>placed</c>, <c>companions</c>, <c>dwelling</c> and
        /// <c>hall</c> on a card it publishes for an account whose save still carries a grove;
        /// nothing draws them any more, so they are ignored here rather than parsed into a
        /// shape with no reader.
        /// </para>
        /// </para>
        /// </summary>
        static Social.GroveCard ReadCard(string ownerId, IDictionary<string, object> document)
        {
            if (document == null) return Social.GroveCard.Empty;

            // The turret line, as slots plus the rung each seat stands at. A malformed row is
            // skipped rather than poisoning the line, and a missing seat is simply missing —
            // `WardLine.Resolve` fills it with this build's own starter, which is the path every
            // board already takes for a turret that was renamed or retired.
            var line = new List<Wards.WardSlot>(Wards.WardLine.Colours.Length);
            var rungs = new List<int>(Wards.WardLine.Colours.Length);

            if (document.TryGetValue("line", out object rawLine) &&
                rawLine is IEnumerable<object> seats)
            {
                foreach (var element in seats)
                {
                    if (!(element is IDictionary<string, object> seat)) continue;

                    string colour = Text(seat, "c");
                    string ward = Text(seat, "w");
                    if (colour.Length != 1 || ward.Length == 0) continue;
                    if (Wards.WardLine.Colours.IndexOf(colour[0]) < 0) continue;

                    line.Add(new Wards.WardSlot(colour[0], ward));
                    rungs.Add((int)ReadLong(seat, "s"));

                    if (line.Count >= Wards.WardLine.Colours.Length) break;
                }
            }

            return new Social.GroveCard(
                ownerId,
                Text(document, "name"),
                (int)ReadLong(document, "level"),
                (int)ReadLong(document, "wave"),
                ReadLong(document, "builtUnix"),
                line,
                rungs,

                // The rank the server derived, not the one this card's owner claimed — see
                // `rungOf` in functions/src/grove.ts. Absent on every card written before that
                // deployment and on any keeper below the first rung, and both read as empty,
                // which every drawing path already takes as "no badge".
                Text(document, "rung"));
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

        // ------------------------------------------------------- proving who is asking
        /// <summary>
        /// Takes the player back through their provider and checks the answer against the
        /// account that is already signed in.
        ///
        /// <para>
        /// <b>Firebase's own mismatch check does the work.</b> <c>ReauthenticateAndRetrieveData</c>
        /// refuses a credential belonging to anybody but the current user, with
        /// <see cref="AuthError.UserMismatch"/>, and — unlike a sign-in — leaves the session
        /// untouched when it does. Comparing uids by hand afterwards would be a second answer
        /// to a question the SDK already answers correctly, and it would compare them *after*
        /// the session had already moved.
        /// </para>
        /// <para>
        /// The Apple authorization code rides back out on <c>AccessToken</c>, which is where
        /// this codebase carries it: Firebase's fourth <c>GetCredential</c> parameter is named
        /// after Google's access token and, for <c>apple.com</c>, must hold Apple's
        /// authorization code. Empty for Google, and empty for a provider reached through the
        /// web flow, because neither yields one and neither needs one.
        /// </para>
        /// </summary>
        public async Task<(CloudResult result, string appleAuthorizationCode)> ReauthenticateAsync(
            LinkCredential credential, CancellationToken cancellation = default)
        {
            if (!credential.IsValid)
                return (CloudResult.Failed(CloudFailure.Rejected, "no provider named"), string.Empty);

            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), string.Empty);

            var user = _auth?.CurrentUser;
            if (user == null)
                return (CloudResult.Failed(CloudFailure.Unauthenticated, "not signed in"), string.Empty);

            try
            {
                var (upgraded, refusal) = await NativeIfRequiredAsync(credential);
                if (refusal.HasValue) return (refusal.Value, string.Empty);
                credential = upgraded;

                if (credential.HasToken)
                    await CloudCancel.Within(user.ReauthenticateAndRetrieveDataAsync(ToCredential(credential)),
                                             AuthSeconds, cancellation);
                else
                    await user.ReauthenticateWithProviderAsync(Provider(credential.ProviderId));

                string appleCode = credential.ProviderId == LinkCredential.Apple
                    ? credential.AccessToken ?? string.Empty
                    : string.Empty;

                return (CloudResult.Success, appleCode);
            }
            catch (Exception e)
            {
                return (Classify(e, "reauthenticate"), string.Empty);
            }
        }

        // ------------------------------------------------------- deleting the account
        /// <summary>
        /// Erases the account, then leaves this device signed in as a brand new anonymous one.
        ///
        /// <para>
        /// <b>The session is checked against the caller's id before anything is sent.</b>
        /// Nothing in the request names an account — the server takes it from the token — so a
        /// device whose session has moved would delete whichever account it is *now* rather
        /// than the one the player was looking at. That is <see cref="AccountGate"/>'s hazard
        /// with the one consequence it cannot repair, and the window is entirely ordinary: a
        /// provider sheet backgrounds the app, and the confirmation panel is on screen across
        /// it.
        /// </para>
        /// <para>
        /// <b>Signing back in is part of this call rather than left to the caller.</b> Firebase
        /// keeps handing back the deleted user until it is told to forget it, so a device that
        /// only deleted would hold a session for an account that no longer exists — which is
        /// indistinguishable, from every screen in the game, from being signed in. It signs
        /// out and takes a fresh anonymous account, which is what the player is actually left
        /// holding: a new grove they can play immediately.
        /// </para>
        /// <para>
        /// <b>A failure to sign back in is still a success.</b> The account really is deleted
        /// by then, and reporting a failure would send the panel down a path that offers to
        /// try again — deleting an account that is already gone, on a device that has just
        /// lost its network. The next launch signs in anonymously by itself.
        /// </para>
        /// </summary>
        public async Task<CloudResult> DeleteAccountAsync(
            string userId, string appleAuthorizationCode = null,
            CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable");

            if (string.IsNullOrEmpty(userId))
                return CloudResult.Failed(CloudFailure.Unauthenticated, "no user id");

            var signedIn = _auth?.CurrentUser;
            if (signedIn == null)
                return CloudResult.Failed(CloudFailure.Unauthenticated, "not signed in");

            if (!string.Equals(signedIn.UserId, userId, StringComparison.Ordinal))
                return CloudResult.Failed(CloudFailure.AccountMismatch,
                                          "the session is not the account being deleted");

            try
            {
                var payload = new Dictionary<string, object>();

                // Sent only when there is one. An empty string would be forwarded to Apple as
                // an authorization code and refused, which turns "nothing to revoke" into a
                // logged failure that reads like a broken credential.
                if (!string.IsNullOrEmpty(appleAuthorizationCode))
                    payload["appleAuthorizationCode"] = appleAuthorizationCode;

                await CallAsync("deleteAccount", payload, cancellation, DeleteSeconds);
            }
            catch (Exception e)
            {
                return Classify(e, "delete account");
            }

            try
            {
                // Out first, unconditionally. SignInAsync only mints an anonymous account when
                // nobody is signed in, and the user it is holding right now is the deleted one.
                _auth.SignOut();
                await CloudCancel.Within(_auth.SignInAnonymouslyAsync(), AuthSeconds, cancellation);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Cloud] the account was deleted but this device could not take " +
                                 "a new one yet: " + e.Message);
            }

            return CloudResult.Success;
        }

        // ------------------------------------------------------------- plumbing
        // ---------------------------------------------------------------- referrals
        /// <summary>
        /// Reads the referral state, minting a code on the first ask. A function rather than a
        /// document read because the read <em>settles</em>: the server judges the invitee's
        /// milestone off the save it holds while it answers, and a client that could read the
        /// document could not make that happen.
        /// </summary>
        public async Task<(CloudResult result, Referral.ReferralReply reply)> ReadReferralAsync(
            CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), new Referral.ReferralReply());

            try
            {
                var reply = await CallAsync("getReferral", new Dictionary<string, object>(), cancellation);
                return (CloudResult.Success, ReadReferral(reply));
            }
            catch (Exception e)
            {
                return (Classify(e, "read referral"), new Referral.ReferralReply());
            }
        }

        /// <summary>
        /// Watches <c>players/{uid}/private/referral</c> — a counter the server bumps whenever
        /// this account's referral state moves, and nothing else.
        ///
        /// <para>
        /// <b>Why that document and not the real one.</b> <c>referrals/{uid}</c> is refused to
        /// every client by <c>firestore.rules</c>, on purpose: it names the referrer, and a code
        /// owner's document names every invitee. A listener there would hand a caller the list
        /// of people who typed their code. The private counter is already owner-read and
        /// server-write-only, so this costs **no rules release** and can leak nothing — the
        /// answer still comes from <c>getReferral</c>.
        /// </para>
        /// <para>
        /// <b>The callback does as little as is possible.</b> The Firestore SDK's threading for
        /// snapshot listeners is not something this project can read off a DLL, so nothing here
        /// assumes it: the handler sets a flag and returns, and the main thread picks it up
        /// (<c>ReferralLedger.Pump</c>). That also means a detach racing a delivery cannot be
        /// running game code at the time.
        /// </para>
        /// <para>
        /// The first delivery is the document as it stands, not a change — Firestore always
        /// opens a listener with a snapshot. It is left to fire, and it is what the ledger
        /// wants: it carries the counter, and the ledger compares that with the stamp on its
        /// cached answer to decide whether a call is needed at all
        /// (<c>ReferralLedger.NeedsAsk</c>). That comparison is what a screen open costs now.
        /// </para>
        /// </summary>
        public IDisposable WatchReferral(Action<long> onChanged)
        {
            if (onChanged == null || _db == null) return null;

            string uid = _auth?.CurrentUser?.UserId;
            if (string.IsNullOrEmpty(uid)) return null;

            try
            {
                var doc = PlayerDoc(uid).Collection("private").Document("referral");
                return new ReferralWatchHandle(doc.Listen(snapshot => onChanged(FeedRevOf(snapshot))));
            }
            catch (Exception e)
            {
                // A watch is an optimisation over the poll that stands behind it, so failing to
                // attach one is worth a line in the log and nothing else.
                Debug.LogWarning($"[Cloud] could not watch the referral feed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// The feed document's counter: its <c>rev</c>, nought for a document that does not
        /// exist (nothing has ever happened to this account), and
        /// <c>ReferralState.UnknownFeed</c> for anything that cannot be read — which the ledger
        /// treats as "ask", so a fault here costs a call and never a stale badge. Runs on the
        /// listener's thread and touches nothing but the snapshot.
        /// </summary>
        static long FeedRevOf(DocumentSnapshot snapshot)
        {
            try
            {
                if (snapshot == null) return Referral.ReferralState.UnknownFeed;
                if (!snapshot.Exists) return 0L;

                var data = snapshot.ToDictionary();
                if (data == null || !data.ContainsKey("rev")) return Referral.ReferralState.UnknownFeed;

                long rev = ReadLong(data, "rev");
                return rev < 0 ? Referral.ReferralState.UnknownFeed : rev;
            }
            catch (Exception)
            {
                return Referral.ReferralState.UnknownFeed;
            }
        }

        /// <summary>
        /// Stops a snapshot listener, exactly once.
        ///
        /// <para>
        /// <c>ListenerRegistration</c> is itself <c>IDisposable</c> and this is not merely a
        /// wrapper for the sake of one: the <c>null</c> exchange is what makes a second
        /// <c>Dispose</c> — from a screen tearing down and a pause arriving in the same frame —
        /// a no-op rather than a second <c>Stop</c> against native state.
        /// </para>
        /// </summary>
        sealed class ReferralWatchHandle : IDisposable
        {
            ListenerRegistration _registration;

            public ReferralWatchHandle(ListenerRegistration registration) { _registration = registration; }

            public void Dispose()
            {
                var registration = System.Threading.Interlocked.Exchange(ref _registration, null);
                if (registration == null) return;

                try { registration.Stop(); }
                catch (Exception e) { Debug.LogWarning($"[Cloud] referral watch would not stop: {e.Message}"); }
            }
        }

        public async Task<(CloudResult result, Referral.ReferralReply reply)> RedeemReferralAsync(
            string code, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), new Referral.ReferralReply());

            try
            {
                var reply = await CallAsync("redeemReferral", new Dictionary<string, object>
                {
                    { "code", code ?? string.Empty },
                }, cancellation);

                return (CloudResult.Success, ReadReferral(reply));
            }
            catch (Exception e)
            {
                return (Classify(e, "redeem referral"), new Referral.ReferralReply());
            }
        }

        /// <summary>
        /// Asks the server to pay a chest. The server rolls it, records the grant against a
        /// derived id and answers with the drops and the balances (invariant 51); the client
        /// predicts nothing. The kind and the goal ride in the body under the spellings
        /// <c>referral.ts</c> parses.
        /// </summary>
        public async Task<(CloudResult result, Referral.ReferralReply reply)> ClaimReferralAsync(
            Referral.ReferralClaimKind kind, int goal, int index, CancellationToken cancellation = default)
        {
            if (!await EnsureReadyAsync())
                return (CloudResult.Failed(CloudFailure.Offline, "Firebase unavailable"), new Referral.ReferralReply());

            try
            {
                var reply = await CallAsync("claimReferral", new Dictionary<string, object>
                {
                    { "kind", kind == Referral.ReferralClaimKind.Invitee ? "invitee" : "rung" },
                    { "goal", goal },
                    { "index", index },
                }, cancellation);

                return (CloudResult.Success, ReadReferral(reply));
            }
            catch (Exception e)
            {
                return (Classify(e, "claim referral"), new Referral.ReferralReply());
            }
        }

        /// <summary>
        /// Reads a referral reply. Every call answers the state after it, so one reader
        /// serves all three; the redeem outcome and the claim outcome are read when present
        /// and left at <c>Unavailable</c> otherwise. A spelling this build does not know reads
        /// as <c>Unavailable</c> rather than as any real answer, because every real answer
        /// changes what a screen draws.
        /// </summary>
        static Referral.ReferralReply ReadReferral(IDictionary<string, object> reply)
        {
            var read = new Referral.ReferralReply();
            if (reply == null) return read;

            if (reply.TryGetValue("state", out object rawState) && rawState is IDictionary<string, object> state)
            {
                var paid = new List<string>();
                if (state.TryGetValue("paid", out object rawPaid) && rawPaid is IEnumerable<object> paidList)
                {
                    foreach (var p in paidList)
                        if (p is string subject && subject.Length > 0) paid.Add(subject);
                }

                read.State = new Referral.ReferralState(
                    state.TryGetValue("code", out object code) ? code as string : string.Empty,
                    (int)ReadLong(state, "bound"),
                    (int)ReadLong(state, "finished"),
                    paid.ToArray(),
                    ReadBool(state, "referred"),
                    ReadBool(state, "milestoneReached"),
                    ReadBool(state, "canRedeem"),
                    SaveSchema.NowUnix());
            }

            switch (reply.TryGetValue("outcome", out object o) ? o as string : null)
            {
                case "bound":            read.Redeem = Referral.ReferralRedeemOutcome.Bound; break;
                case "unknown_code":     read.Redeem = Referral.ReferralRedeemOutcome.UnknownCode; break;
                case "own_code":         read.Redeem = Referral.ReferralRedeemOutcome.OwnCode; break;
                case "already_referred": read.Redeem = Referral.ReferralRedeemOutcome.AlreadyReferred; break;
                case "full":             read.Redeem = Referral.ReferralRedeemOutcome.Full; break;
                case "too_late":         read.Redeem = Referral.ReferralRedeemOutcome.TooLate; break;
                case "no_save":          read.Redeem = Referral.ReferralRedeemOutcome.NoSave; break;
            }

            switch (reply.TryGetValue("claim", out object c) ? c as string : null)
            {
                case "paid":         read.Claim = Referral.ReferralClaimOutcome.Paid; break;
                case "already_paid": read.Claim = Referral.ReferralClaimOutcome.AlreadyPaid; break;
                case "not_yet":      read.Claim = Referral.ReferralClaimOutcome.NotYet; break;
                case "unknown":      read.Claim = Referral.ReferralClaimOutcome.Unknown; break;
            }

            if (reply.TryGetValue("drops", out object rawDrops) && rawDrops is IEnumerable<object> drops)
            {
                foreach (var element in drops)
                {
                    if (!(element is IDictionary<string, object> drop)) continue;

                    var kind = Daily.ChestDropKinds.Parse(drop.TryGetValue("kind", out object k) ? k as string : null);
                    int amount = (int)ReadLong(drop, "amount");
                    string item = drop.TryGetValue("item", out object it) ? it as string : null;

                    // A kind this build cannot name is dropped rather than drawn as a white
                    // rectangle (7b). The currency behind it is in the wallet reply regardless.
                    if (kind == Daily.ChestDropKind.None || amount <= 0) continue;
                    read.Drops.Add(new Daily.ChestDrop(kind, amount, item));
                }
            }

            read.Wallets = ReadWalletStates(reply);
            return read;
        }

        static bool ReadBool(IDictionary<string, object> map, string key)
            => map.TryGetValue(key, out object value) && value is bool b && b;

        static long ReadLongValue(object value)
        {
            switch (value)
            {
                case long l: return l;
                case int i: return i;
                case double d: return (long)d;
                case string s: return long.TryParse(s, out long parsed) ? parsed : 0;
                default: return 0;
            }
        }

        async Task<IDictionary<string, object>> CallAsync(
            string name, Dictionary<string, object> data,
            CancellationToken cancellation = default, int seconds = CallSeconds)
        {
            var result = await CloudCancel.Within(_functions.GetHttpsCallable(name).CallAsync(data),
                                                  seconds, cancellation);
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

                // The Infinite lane's spent day, read exactly as the wheel's is and for the
                // same reason: "did the key arrive" first, the number second, because a
                // deployment that predates the field sends nothing and a fresh account
                // honestly answers nought.
                if (entry.TryGetValue("endlessPaid", out object lane) && lane != null)
                {
                    state.CarriesEndless = true;
                    state.EndlessPaid = (int)ReadLong(entry, "endlessPaid");
                    state.EndlessDay = (int)ReadLong(entry, "endlessDay");
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
        /// <summary>The ids a reply's top-level <c>rejected</c> list names. Empty when it names none.</summary>
        static List<string> Rejected(IDictionary<string, object> reply)
        {
            var ids = new List<string>();
            if (reply == null) return ids;
            if (!reply.TryGetValue("rejected", out object raw) || !(raw is IEnumerable<object> list)) return ids;

            foreach (var id in list)
                if (id is string s && s.Length > 0) ids.Add(s);

            return ids;
        }

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

            // Somebody walked away from a screen while its read was out — the ordinary end of
            // work, not a fault. Reported as anything else it would reach a player as "the
            // boards could not be reached", on a board they are no longer looking at, and it
            // would teach whoever reads the log to ignore a class of message that also carries
            // real network failures. See CloudCancel for what a token can and cannot do here.
            if (inner is OperationCanceledException)
                return CloudResult.Failed(CloudFailure.Cancelled, "the caller gave up");

            // A deadline, not a choice: the network did not answer in time. Retryable and
            // expected, which is exactly what Offline means to the scheduler — and the one
            // answer that keeps a hung write from being reported as anything a player has to
            // act on. See CloudCancel.Within.
            if (inner is TimeoutException)
                return CloudResult.Failed(CloudFailure.Offline, inner.Message);

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

                    // The credential is real and belongs to somebody else. Only reachable from
                    // a re-authentication, where it is the answer that matters most: it is what
                    // a player picking the wrong entry out of an account chooser produces, and
                    // reporting it as an error would tell them the app is broken when they have
                    // simply mistapped. Nothing has moved — see ReauthenticateAsync.
                    case AuthError.UserMismatch:
                        return CloudResult.Failed(CloudFailure.AccountMismatch, inner.Message);
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

                    // `redeemPurchase` alone raises this, and only from the branch where the
                    // receipt exists against a different account. It is the one refusal here
                    // that no later state can turn into a success, which is what makes it safe
                    // for the store queue to stop asking — see CloudFailure.AlreadyRedeemed.
                    case FunctionsErrorCode.AlreadyExists:
                        return CloudResult.Failed(CloudFailure.AlreadyRedeemed, functions.Message);
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
