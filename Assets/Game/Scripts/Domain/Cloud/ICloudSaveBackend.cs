using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Cloud
{
    /// <summary>Why a cloud call did not do what was asked.</summary>
    public enum CloudFailure
    {
        None = 0,

        /// <summary>No network, or the request timed out. Retryable, and expected.</summary>
        Offline,

        /// <summary>Not signed in, or the token expired. Retry after authenticating.</summary>
        Unauthenticated,

        /// <summary>The server rejected the write — a stale revision, or a failed check.</summary>
        Rejected,

        /// <summary>
        /// The provider account is already attached to a different Glimmer Grove account.
        ///
        /// Not an error — it is the normal result of a player who linked on one device
        /// installing on a second one. It cannot be resolved silently, because adopting
        /// the other account means abandoning whatever this device has played, so it is
        /// reported separately for the UI to ask about.
        /// </summary>
        AlreadyLinkedElsewhere,

        /// <summary>
        /// A sync is already in flight, so this one did nothing.
        ///
        /// Not a failure of anything, and specifically not one the retry backoff should
        /// count: the work is still owed and the right response is to ask again in a
        /// moment, not to wait five minutes on the assumption the network is broken.
        /// </summary>
        Busy,

        /// <summary>
        /// The player closed the provider's sheet without choosing. Not an error at all.
        ///
        /// <para>
        /// Separated from <see cref="Offline"/> because it used to be reported as one, and the
        /// screen then said "no internet connection" to somebody who had simply changed their
        /// mind — which is the exact class of wrong sentence this account flow was rewritten to
        /// remove. Reachable only from an interactive sign-in, so nothing that retries on a
        /// backoff ever sees it.
        /// </para>
        /// </summary>
        Cancelled,

        /// <summary>
        /// The session is authenticated as one account and the local save belongs to another.
        ///
        /// <para>
        /// A transient now rather than a dead end: a sync completes the account change on this
        /// device and carries on — see <c>CloudSaveService.Reconcile</c>, and
        /// <see cref="AccountGate"/> for why finishing forward is the only safe direction. It
        /// survives as an answer for the two places that must still refuse: redeeming a receipt,
        /// which would otherwise credit a purchase to the wrong account, and a device that
        /// cannot file the grove it is holding anywhere.
        /// </para>
        /// </summary>
        AccountMismatch,

        /// <summary>Anything else. Logged, never surfaced to the player as a wall.</summary>
        Error,
    }

    public readonly struct CloudResult
    {
        public readonly bool Ok;
        public readonly CloudFailure Failure;
        public readonly string Message;

        CloudResult(bool ok, CloudFailure failure, string message)
        {
            Ok = ok;
            Failure = failure;
            Message = message ?? string.Empty;
        }

        public static readonly CloudResult Success = new CloudResult(true, CloudFailure.None, null);

        public static CloudResult Failed(CloudFailure failure, string message = null)
            => new CloudResult(false, failure, message);

        /// <summary>True when trying again later is worth doing.</summary>
        public bool IsRetryable => !Ok && Failure != CloudFailure.Rejected;
    }

    /// <summary>The account the player is playing as.</summary>
    public readonly struct CloudIdentity
    {
        public readonly string UserId;

        /// <summary>
        /// False for a device-bound anonymous account. Anonymous is the right default —
        /// nothing should stand between a first launch and the first glade — but an
        /// anonymous account dies with the app, so the player has to be offered a
        /// permanent one before they have anything worth losing.
        /// </summary>
        public readonly bool IsLinked;

        /// <summary>
        /// What to call this account on screen — the provider's email or display name, empty
        /// when there is none.
        ///
        /// <para>
        /// For display and for nothing else: never compared, never stored, never keyed on. The
        /// uid is the identity (invariant 1 applies to it in full); this is a label a provider
        /// may change, may localise, and may withhold entirely — Apple's relay address is only
        /// offered on the first authorisation, so a returning player has a name and no email.
        /// </para>
        /// <para>
        /// It exists because switching between two of one person's own accounts is the case
        /// this whole flow is for, and without it the panel says "your progress is saved
        /// online" about an account it cannot name. Two Google accounts belonging to the same
        /// person frequently share a display name, which is why the email is preferred when
        /// the provider gives one.
        /// </para>
        /// </summary>
        public readonly string Label;

        public CloudIdentity(string userId, bool isLinked, string label = null)
        {
            UserId = userId;
            IsLinked = isLinked;
            Label = label ?? string.Empty;
        }

        public bool IsValid => !string.IsNullOrEmpty(UserId);

        public static readonly CloudIdentity None = new CloudIdentity(string.Empty, false);
    }

    /// <summary>What the server holds for an account, if anything.</summary>
    public sealed class CloudSnapshot
    {
        public readonly SaveFileDto Save;
        public readonly bool Exists;

        public CloudSnapshot(SaveFileDto save, bool exists)
        {
            Save = save;
            Exists = exists;
        }

        public static readonly CloudSnapshot Missing = new CloudSnapshot(null, false);
    }

    /// <summary>
    /// The server's authoritative view of one currency.
    ///
    /// Balances are never taken from a client. This is the shape the answer comes back
    /// in, and <see cref="CurrencyLedger.ApplyServerState"/> is what adopts it.
    /// </summary>
    public sealed class CloudWalletState
    {
        public string Currency;
        public long GrantedBaseline;
        public long SpentBaseline;
        public long ConfirmedThroughUnix;

        /// <summary>
        /// The server's floor under derived earnings. Adopted by the client so both
        /// sides agree on what is spendable — the server's figure is the one that
        /// governs, so a client showing more would show currency that cannot be used.
        /// </summary>
        public long EarnedFloor;

        /// <summary>Spend ids the server has folded into the baseline.</summary>
        public List<string> ConfirmedSpendIds = new List<string>();

        /// <summary>
        /// Award ids the server has recorded and folded into
        /// <see cref="GrantedBaseline"/>.
        ///
        /// Until an id appears here the client keeps counting that award locally, so a
        /// dropped reply costs a resubmission rather than a player's daily chest. Once it
        /// does appear the local copy is dropped, because it is inside the baseline now
        /// and counting both would show a balance the server will not honour.
        /// </summary>
        public List<string> ConfirmedGrantIds = new List<string>();

        /// <summary>
        /// Heart containers the server has <b>revoked</b> — receipts it granted and has since
        /// reversed because the store refunded or charged back the payment.
        ///
        /// <para>
        /// An account fact rather than a currency one, carried on every row because the reply
        /// is a list of currency rows and repeating one short list is cheaper than giving four
        /// callables a second shape. A reader may take it from any row.
        /// </para>
        /// <para>
        /// <b>A revocation list, never an ownership list</b>, and that is the safety of the
        /// whole design. An id absent from here means nothing at all, so a short reply, a cold
        /// account or a deployment that predates the field can never take back something
        /// somebody paid for; an entry can only be produced by a refund that really happened.
        /// See <c>HeartContainerLedger.ApplyServerRevocations</c> and invariant 18c.
        /// </para>
        /// </summary>
        public List<string> RevokedContainers = new List<string>();

        /// <summary>
        /// Whether this reply carried the bonus wheel's position at all.
        ///
        /// <para>
        /// Reported separately from the numbers, and that separation is the whole safety of
        /// the feature. A brand-new account legitimately answers day 0, spin 0, which is
        /// indistinguishable from a deployment that has never heard of the wheel unless the
        /// presence of the key is a fact in its own right — and reading those zeros as an
        /// answer would draw a wheel in front of a server that grants the flat amount. False
        /// on any reply from before the field existed, which is what makes shipping the client
        /// ahead of the functions cost a feature nobody has seen rather than a payout nobody
        /// honours. See <c>WheelStand.ApplyServerState</c> and invariant 12a.
        /// </para>
        /// </summary>
        public bool CarriesWheel;

        /// <summary>The UTC day <see cref="WheelSpins"/> is counted for.</summary>
        public int WheelDay;

        /// <summary>
        /// How many <c>win_bonus</c> views this server has granted the account today, and
        /// therefore which spin of the wheel the next one is.
        ///
        /// <para>
        /// Server-owned because it decides money: the slice is a pure function of (account,
        /// day, spin index), so a counter the client kept for itself would drift the first time
        /// a verification callback was delayed past the next win — and the visible form of that
        /// drift is a wheel landing on five hundred while the balance rises by two. It rides
        /// here for <see cref="RevokedContainers"/>'s reason, on the document no client may
        /// write, repeated on every currency row.
        /// </para>
        /// </summary>
        public int WheelSpins;
    }

    /// <summary>
    /// How the player is proving who they are.
    ///
    /// Two shapes, because there are two ways to run the flow and the choice should not
    /// leak into the game.
    ///
    /// <para>
    /// With only a <see cref="ProviderId"/>, Firebase runs the whole OAuth flow itself.
    /// That is how this ships: one code path for Apple and Google, on both platforms,
    /// with no third-party sign-in plugin to keep alive — which matters, because the
    /// Google Sign-In Unity plugin is archived.
    /// </para>
    ///
    /// <para>
    /// With tokens supplied, a native sign-in sheet did the work and Firebase only
    /// verifies the result. Slicker, and worth moving to later. Apple needs the raw
    /// nonce alongside the identity token — its token embeds a hash of the nonce, and
    /// without it Firebase cannot tell a fresh sign-in from a replayed one.
    /// </para>
    /// </summary>
    public readonly struct LinkCredential
    {
        /// <summary><c>google.com</c> or <c>apple.com</c>.</summary>
        public readonly string ProviderId;

        /// <summary>The OIDC identity token, when a native sheet produced one.</summary>
        public readonly string IdToken;

        /// <summary>
        /// Google's OAuth access token — and, for Apple, the <em>authorization code</em>.
        ///
        /// <para>
        /// This said "unused by Apple" for as long as nothing on iOS produced an Apple token,
        /// and it was wrong. Firebase's <c>GetCredential</c> names its fourth parameter
        /// <c>accessToken</c>, and for <c>apple.com</c> what belongs there is the
        /// <c>authorizationCode</c> off Apple's credential. Sending only the identity token is
        /// refused with <c>"Invalid OAuth response from apple.com"</c> — indistinguishable from
        /// a malformed token or a mismatched nonce, which is what made it expensive to find.
        /// </para>
        /// </summary>
        public readonly string AccessToken;

        /// <summary>The unhashed nonce that was passed to Apple. Unused by Google.</summary>
        public readonly string RawNonce;

        public LinkCredential(string providerId, string idToken = null,
                              string accessToken = null, string rawNonce = null)
        {
            ProviderId = providerId;
            IdToken = idToken;
            AccessToken = accessToken;
            RawNonce = rawNonce;
        }

        /// <summary>A provider is all that is required; tokens are the optional shortcut.</summary>
        public bool IsValid => !string.IsNullOrEmpty(ProviderId);

        /// <summary>True when a native sheet already produced a token to verify.</summary>
        public bool HasToken => !string.IsNullOrEmpty(IdToken);

        public const string Google = "google.com";
        public const string Apple = "apple.com";

        public static LinkCredential ForGoogle() => new LinkCredential(Google);
        public static LinkCredential ForApple() => new LinkCredential(Apple);
    }

    /// <summary>
    /// What one redemption actually granted, as the server reports it.
    ///
    /// <para>
    /// Carried back rather than inferred from the balance moving, and the difference is not
    /// theoretical: a background sync can land between the two readings a subtraction would
    /// need, and the number it would corrupt is the one on a panel that says "thank you"
    /// after somebody has paid. A figure about money that is occasionally wrong is worse
    /// than no figure at all.
    /// </para>
    /// <para>
    /// <see cref="AlreadyGranted"/> is the other half. A retry of a receipt the server has
    /// already honoured succeeds and grants nothing — which is exactly right, and is also
    /// what happens on every launch after a purchase the store re-delivers until it is
    /// confirmed. Congratulating somebody for reopening the app is how a celebration stops
    /// meaning anything.
    /// </para>
    /// </summary>
    public sealed class CloudRedemption
    {
        /// <summary>Currency id to amount. Empty on a retry.</summary>
        public readonly Dictionary<string, long> Granted = new Dictionary<string, long>();

        /// <summary>True when this transaction had already been honoured.</summary>
        public bool AlreadyGranted;

        public long AmountOf(string currency)
            => currency != null && Granted.TryGetValue(currency, out long amount) ? amount : 0L;

        public bool GrantedAnything
        {
            get
            {
                foreach (var pair in Granted) if (pair.Value > 0) return true;
                return false;
            }
        }

        public static readonly CloudRedemption Nothing = new CloudRedemption { AlreadyGranted = true };
    }

    /// <summary>A store receipt to be validated server-side and turned into a grant.</summary>
    public sealed class PurchaseReceipt
    {
        /// <summary>The store's transaction id. The idempotency key for the grant.</summary>
        public string TransactionId;

        /// <summary>Product as configured in the store, e.g. <c>credits_pouch_small</c>.</summary>
        public string ProductId;

        /// <summary>The opaque receipt payload, exactly as the store gave it.</summary>
        public string Payload;

        /// <summary><c>apple</c> or <c>google</c>.</summary>
        public string Store;
    }

    /// <summary>
    /// Somewhere a save can be stored and an economy can be adjudicated.
    ///
    /// Deliberately an interface in Domain with no vendor type anywhere in it, exactly
    /// as <c>IAssetProvider</c> was before Addressables. The Firebase implementation
    /// lives in its own assembly, so the whole progression system stays testable with
    /// no SDK present and no network — which matters because the parts most likely to
    /// be wrong are the merge and the arithmetic, and neither needs a server to check.
    ///
    /// <para>
    /// Two rules the implementation must honour, both of which live on the server and
    /// cannot be enforced from here:
    /// </para>
    /// <list type="number">
    /// <item><b>The client may never raise its own granted balance.</b> Security rules
    /// have to reject a client write to that field outright. A client that can grant
    /// itself currency can print money, and once real purchases exist that is the only
    /// field an attacker is interested in.</item>
    /// <item><b>Receipt validation must be idempotent on the transaction id.</b> Store
    /// receipts can be replayed, and are, at scale. Granting per validated receipt
    /// rather than per validated <em>transaction</em> funds unlimited accounts from one
    /// purchase.</item>
    /// </list>
    /// </summary>
    public interface ICloudSaveBackend
    {
        /// <summary>False when no backend is configured; the game then stays local-only.</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Who is signed in right now, read synchronously so UI can paint without waiting.
        ///
        /// The distinction that matters is <see cref="CloudIdentity.IsLinked"/>, not
        /// whether a user exists: an anonymous account has a perfectly good uid, and
        /// treating "has a uid" as "is safe" tells a guest their progress is protected
        /// when it dies with the installation. Returns <see cref="CloudIdentity.None"/>
        /// before the SDK has initialised, which errs toward offering to sign in.
        /// </summary>
        CloudIdentity CurrentIdentity { get; }

        /// <summary>
        /// Signs in, anonymously if the player has not linked an account. Must never be
        /// on the boot path — a first launch cannot be allowed to wait on a network.
        /// </summary>
        Task<(CloudResult result, CloudIdentity identity)> SignInAsync(
            CancellationToken cancellation = default);

        /// <summary>
        /// Brings the SDK up and reports who is already signed in, <b>creating nobody</b>.
        ///
        /// <para>
        /// The distinction from <see cref="SignInAsync"/> is the entire point and it is not a
        /// convenience. Once a save names an account, minting a fresh anonymous one is the
        /// worst available answer: the new account can never match the save, so every
        /// subsequent sync is refused by <see cref="AccountGate"/> and the device quietly
        /// stops backing up a grove the player believes is safe. That is what a cancelled
        /// sign-in sheet used to cost. This asks the only question that is safe to ask on
        /// behalf of a save that already has an owner — "is that owner still signed in?" — and
        /// an honest "no" is a retry rather than a new identity.
        /// </para>
        /// <para>
        /// Returns <see cref="CloudIdentity.None"/> with a successful result when the SDK is
        /// up and nobody is signed in; that is an answer, not a failure.
        /// </para>
        /// </summary>
        Task<(CloudResult result, CloudIdentity identity)> ResumeAsync(
            CancellationToken cancellation = default);

        /// <summary>
        /// Links the anonymous account to a permanent one, keeping its progress.
        ///
        /// Worth offering at a natural high point — a first chapter cleared — rather
        /// than on launch. An anonymous account is lost with the app, so a player who
        /// reinstalls before linking loses everything, and a player asked to sign in
        /// before they have played anything mostly just declines.
        /// </summary>
        Task<(CloudResult result, CloudIdentity identity)> LinkAsync(
            LinkCredential credential, CancellationToken cancellation = default);

        /// <summary>
        /// Signs in to the account the credential already belongs to, abandoning the
        /// anonymous one.
        ///
        /// Only ever called after <see cref="LinkAsync"/> reports
        /// <see cref="CloudFailure.AlreadyLinkedElsewhere"/>, and only after the player
        /// has been told plainly what it costs. Whatever this device has played and not
        /// synced is gone afterwards — the two accounts cannot be merged, because
        /// currency has been granted and spent separately on each and no join can
        /// reconcile that without inventing or destroying money.
        /// </summary>
        Task<(CloudResult result, CloudIdentity identity)> SignInWithCredentialAsync(
            LinkCredential credential, CancellationToken cancellation = default);

        Task<(CloudResult result, CloudSnapshot snapshot)> PullAsync(
            string userId, CancellationToken cancellation = default);

        /// <summary>
        /// Writes the merged save back, sending only what <paramref name="delta"/> says
        /// changed.
        ///
        /// The delta is a hint about payload size, never about correctness: the
        /// implementation is free to write the whole document, and the server validates
        /// the result either way. What it buys is a sync that uploads one glade's record
        /// rather than the entire ledger, which at a large catalog is the difference
        /// between a rounding error and a noticeable amount of somebody's mobile data.
        /// </summary>
        Task<CloudResult> PushAsync(
            string userId, SaveFileDto snapshot, SaveDelta delta,
            CancellationToken cancellation = default);

        /// <summary>
        /// Reads the server's balances.
        ///
        /// Called on every sync, not only when there is something to submit. A purchase
        /// made on one device has to reach the other, and the other may have nothing of
        /// its own to send — without this, a second handset would never see it.
        /// </summary>
        Task<(CloudResult result, List<CloudWalletState> wallets)> ReadWalletAsync(
            string userId, CancellationToken cancellation = default);

        /// <summary>
        /// Submits pending debits for confirmation. Safe to call with entries the server
        /// has already seen: that is what the idempotency key on each one is for.
        /// </summary>
        Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitSpendsAsync(
            string userId, IReadOnlyList<SpendEntryDto> spends,
            CancellationToken cancellation = default);

        /// <summary>
        /// Submits awards the client has applied optimistically, for the server to
        /// adjudicate.
        ///
        /// <para>
        /// The amounts on the entries are a <em>claim</em>, not an instruction. The server
        /// recomputes what each award was worth from its own copy of the rules — for a
        /// daily chest, by re-rolling the same deterministic sequence from the account id,
        /// the day and the chest index — and grants its own figure. A client that inflates
        /// an amount therefore gains nothing, which is what allows the reward to be shown
        /// and spent offline in the first place.
        /// </para>
        /// <para>
        /// Safe to call with entries the server has already recorded: each id is derived
        /// from what earned it rather than generated, so the second submission collides
        /// with the first in the database and confirms rather than grants.
        /// </para>
        /// </summary>
        Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitAwardsAsync(
            string userId, IReadOnlyList<GrantEntryDto> awards,
            CancellationToken cancellation = default);

        /// <summary>
        /// Hands a store receipt to the server, which validates it and grants what its own
        /// catalog says the product is worth.
        ///
        /// The amount is <b>reported back</b> rather than left to be inferred — see
        /// <see cref="CloudRedemption"/>.
        /// </summary>
        Task<(CloudResult result, List<CloudWalletState> wallets, CloudRedemption redemption)>
            RedeemPurchaseAsync(string userId, PurchaseReceipt receipt,
                                CancellationToken cancellation = default);

        /// <summary>
        /// Reads the published move-count deciles for every glade.
        ///
        /// <para>
        /// The one read here that is not about this player. It needs no user id and no
        /// sign-in — it is a single public document, written by a scheduled job that
        /// samples the population — so it is safe to call on a launch that has not
        /// authenticated, and safe to fail: every reader treats an absent table as
        /// "nothing to say" and draws nothing.
        /// </para>
        /// <para>
        /// Deliberately <b>not</b> routed through the content sources, even though it looks
        /// like content. It is derived from live players rather than authored, it changes
        /// daily, and it must never end up cached in a shipped build — a snapshot of it in
        /// StreamingAssets would be quoting last quarter's population forever.
        /// </para>
        /// </summary>
        Task<(CloudResult result, Dictionary<Content.LevelId, Social.LevelStats> stats)> ReadGroveStatsAsync(
            CancellationToken cancellation = default);
    }
}
