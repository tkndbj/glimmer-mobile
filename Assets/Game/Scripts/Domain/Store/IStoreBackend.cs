using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GlimmerGrove.Store
{
    /// <summary>
    /// Why a store call did not do what was asked.
    ///
    /// <para>
    /// Shaped like <c>CloudFailure</c> and <c>AdOfferState</c> and for their reason: each
    /// member renders a different sentence, because "something went wrong" on a screen
    /// that just took somebody's money is how a player decides they have been charged for
    /// nothing. Three of these are not failures at all.
    /// </para>
    /// </summary>
    public enum StoreFailure
    {
        None = 0,

        /// <summary>
        /// No store SDK in this build. The shop is not drawn at all — see
        /// <see cref="NullStoreBackend"/>.
        /// </summary>
        Unavailable,

        /// <summary>
        /// The SDK is here but the store refused to connect: no Play Services, a device
        /// with purchases disabled, a build not yet published to a track. Retryable, and by
        /// far the most common thing to see during development.
        /// </summary>
        NotConnected,

        /// <summary>
        /// This product id is not one the store knows about. Almost always a product that
        /// has not been created in App Store Connect or the Play Console yet, or one still
        /// waiting for review; occasionally a storefront where it is not for sale. Its card
        /// is hidden rather than greyed, because there is nothing the player can do.
        /// </summary>
        UnknownProduct,

        /// <summary>The player closed the payment sheet. Not an error, and never reported as one.</summary>
        Cancelled,

        /// <summary>
        /// The store says this account already owns this non-consumable. Not an error
        /// either: it is what a second tap on the starter bundle looks like, and the right
        /// response is to restore rather than to charge.
        /// </summary>
        AlreadyOwned,

        /// <summary>
        /// The payment itself failed — declined card, parental controls, insufficient
        /// funds, an unauthorised device. Nothing was charged.
        /// </summary>
        PaymentFailed,

        /// <summary>
        /// The purchase went through and the currency has not arrived yet, because this
        /// device could not reach our server to have the receipt validated.
        ///
        /// <para>
        /// The single most important state in this enum, and the only one that is neither a
        /// success nor a failure. The money has moved; the transaction stays unfinished with
        /// the store, so it is re-delivered on every launch until it is redeemed, and the
        /// player is told plainly that it will land. Reporting this as an error is how
        /// somebody asks for a refund for a purchase that was about to work.
        /// </para>
        /// </summary>
        AwaitingGrant,

        /// <summary>
        /// The store is holding the purchase until somebody else approves it — Apple's Ask
        /// to Buy, or a Play payment method that settles later.
        ///
        /// <para>
        /// Nothing has been charged and nothing is owed, so this is not queued for retry;
        /// if the approval comes, the transaction arrives on a later launch through the
        /// ordinary re-delivery path and is granted then. It exists as its own member
        /// because from the player's side they tapped buy and nothing happened, and a game
        /// aimed at a family audience meets this a great deal more often than its share of
        /// the enum suggests.
        /// </para>
        /// </summary>
        Deferred,

        /// <summary>Anything else. Logged, and shown as a retry rather than as a wall.</summary>
        Error,
    }

    public readonly struct StoreResult
    {
        public readonly bool Ok;
        public readonly StoreFailure Failure;
        public readonly string Message;

        StoreResult(bool ok, StoreFailure failure, string message)
        {
            Ok = ok;
            Failure = failure;
            Message = message ?? string.Empty;
        }

        public static readonly StoreResult Success = new StoreResult(true, StoreFailure.None, null);

        public static StoreResult Failed(StoreFailure failure, string message = null)
            => new StoreResult(false, failure, message);

        /// <summary>True when asking again later is worth doing.</summary>
        public bool IsRetryable
            => !Ok && (Failure == StoreFailure.NotConnected || Failure == StoreFailure.AwaitingGrant ||
                       Failure == StoreFailure.Error);
    }

    /// <summary>What a product should be fetched as. See <see cref="StoreProductKind"/>.</summary>
    public readonly struct StoreProductRequest
    {
        public readonly string ProductId;
        public readonly StoreProductKind Kind;

        public StoreProductRequest(string productId, StoreProductKind kind)
        {
            ProductId = productId ?? string.Empty;
            Kind = kind;
        }
    }

    /// <summary>
    /// What the store says about a product, as opposed to what the content file says.
    ///
    /// <para>
    /// One field here matters above all the others and it is <see cref="Price"/>: an
    /// already-formatted string in the player's own currency, with the right symbol on the
    /// right side of the number and the right number of decimal places for their locale.
    /// Building that from a number and a currency code is a mistake every project makes
    /// once — there is no correct client-side rule for it, the stores hand it over already
    /// done, and drawing anything else is both wrong and a review risk.
    /// </para>
    /// </summary>
    public sealed class StoreProductInfo
    {
        public string ProductId = string.Empty;

        /// <summary>Localised and formatted by the store. Drawn verbatim, never rebuilt.</summary>
        public string Price = string.Empty;

        /// <summary>ISO currency code, for analytics only.</summary>
        public string CurrencyCode = string.Empty;

        /// <summary>
        /// The price as a number, for analytics only.
        ///
        /// Revenue reporting needs a figure it can sum; a display needs
        /// <see cref="Price"/>. Never draw this — it has no currency symbol, no locale and
        /// no rounding rule, and it is a decimal because a double cannot hold money.
        /// </summary>
        public decimal PriceValue;

        /// <summary>
        /// True when the store says this account already holds this non-consumable.
        ///
        /// Only ever meaningful for a one-time product; a consumable is never "owned",
        /// because it is used up the moment it is granted.
        /// </summary>
        public bool Owned;

        public bool HasPrice => !string.IsNullOrEmpty(Price);
    }

    /// <summary>
    /// A transaction the store has completed and this game has not yet honoured.
    ///
    /// <para>
    /// The word to hold on to is <b>unfinished</b>. Both stores keep re-delivering a
    /// transaction until the app confirms it, and that is not a nuisance to be worked
    /// around — it is the entire reason a purchase cannot be lost to a dropped connection,
    /// a crash, or a battery running out between the payment sheet and the grant. So
    /// nothing here is confirmed until our own server has said the currency is in the
    /// account, and the ordering of those two steps is the whole safety property of this
    /// feature.
    /// </para>
    /// <para>
    /// Google Play adds a deadline to that: an unacknowledged purchase is <b>automatically
    /// refunded after three days</b>. Confirming is what acknowledges it, so a device that
    /// is offline for a long weekend after buying something gets its money back rather than
    /// its gems — correct, and the reason the retry here is aggressive rather than polite.
    /// </para>
    /// </summary>
    public sealed class StorePurchase
    {
        /// <summary>Which product. Looked up in <see cref="StoreCatalog"/> to name it.</summary>
        public string ProductId = string.Empty;

        /// <summary>
        /// The store's own transaction identifier, and the idempotency key for the grant.
        ///
        /// Never generated here and never a receipt hash: it is the string the store will
        /// give back identically on every re-delivery, which is what makes redeeming twice
        /// a no-op instead of a second payout.
        /// </summary>
        public string TransactionId = string.Empty;

        /// <summary><c>apple</c> or <c>google</c>, in the spelling the server expects.</summary>
        public string Store = string.Empty;

        /// <summary>
        /// Google Play's purchase token. Empty on Apple, where the transaction id is enough
        /// for the server to ask the App Store Server API directly.
        /// </summary>
        public string Payload = string.Empty;

        public bool IsValid => ProductId.Length > 0 && TransactionId.Length > 0 && Store.Length > 0;

        /// <summary>A stable key for the in-flight set, unique across both stores.</summary>
        public string Key => Store + "__" + TransactionId;
    }

    /// <summary>
    /// Somewhere purchases can be made, with no vendor type anywhere in it.
    ///
    /// <para>
    /// Exactly the bargain <c>ICloudSaveBackend</c> and <c>IAdProvider</c> already make,
    /// and for a sharper version of their reason. The parts of this feature most likely to
    /// be wrong are the ordering of redeem-then-confirm, the retry policy, and what the
    /// shop says in each of nine states — and not one of those needs a store, a device, or
    /// a real card to exercise. Keeping the SDK behind an interface is what lets all of it
    /// be proved offline, on a machine with no Editor open.
    /// </para>
    /// <para>
    /// Two rules the implementation must honour, both of which are about money and neither
    /// of which this side can enforce:
    /// </para>
    /// <list type="number">
    /// <item><b>Never confirm a transaction this game has not granted.</b> Confirming is
    /// the irrevocable half — it tells the store the goods were delivered, and on Google it
    /// is what stops the automatic refund. A transaction confirmed before the server
    /// granted is a purchase the player paid for and can never be given.</item>
    /// <item><b>Report every completed transaction, including ones from a previous
    /// launch.</b> The whole recovery story is that an unconfirmed purchase comes back. An
    /// implementation that only raised purchases it had itself initiated would lose every
    /// purchase interrupted by a crash.</item>
    /// </list>
    /// </summary>
    public interface IStoreBackend
    {
        /// <summary>False when no store SDK is compiled in; the shop is then not drawn.</summary>
        bool IsAvailable { get; }

        /// <summary>True once the store has answered and product metadata is in hand.</summary>
        bool IsConnected { get; }

        /// <summary>
        /// Raised for every completed transaction that has not been confirmed, including
        /// ones that completed during a previous launch. See the type's remarks.
        /// </summary>
        event Action<StorePurchase> PurchasePending;

        /// <summary>Raised when a purchase attempt ended without a transaction.</summary>
        event Action<string, StoreFailure, string> PurchaseFailed;

        /// <summary>Raised when the connection state or a product's metadata changes.</summary>
        event Action Changed;

        /// <summary>
        /// Connects and fetches metadata for exactly these products.
        ///
        /// Both stores answer only for the ids they were asked about, so a product missing
        /// from this list has no price and cannot be bought — which is why the list comes
        /// from the catalog rather than being written out here.
        /// </summary>
        Task<StoreResult> ConnectAsync(IReadOnlyList<StoreProductRequest> products,
                                       CancellationToken cancellation = default);

        /// <summary>What the store says about a product, or null before it has answered.</summary>
        StoreProductInfo Info(string productId);

        /// <summary>
        /// Opens the payment sheet. The result is delivered through
        /// <see cref="PurchasePending"/> or <see cref="PurchaseFailed"/>, never returned —
        /// the sheet outlives the call, and on Android it outlives the process.
        /// </summary>
        StoreResult Buy(string productId);

        /// <summary>
        /// Tells the store the goods have been delivered. Only ever called after the server
        /// has granted. See the type's remarks.
        /// </summary>
        void Confirm(StorePurchase purchase);

        /// <summary>
        /// Asks the store to re-deliver everything this account holds.
        ///
        /// Required by Apple for any app selling non-consumables, and useful on both
        /// platforms as the manual escape hatch when an automatic retry has not run.
        /// </summary>
        StoreResult Restore();
    }

    /// <summary>
    /// The store for a build with no store SDK, and for every Editor session.
    ///
    /// <para>
    /// Reports unavailable rather than pretending to work. That is the same call
    /// <c>NullAdProvider</c> and <c>NullCloudBackend</c> make and it is worth restating
    /// here, because the tempting alternative — a fake store that grants instantly so the
    /// shop can be clicked through in the Editor — is a debug faucet in the one screen
    /// where a debug faucet is indistinguishable from a compromise. If a fake is ever
    /// wanted, it belongs behind a define that no build can carry.
    /// </para>
    /// </summary>
    public sealed class NullStoreBackend : IStoreBackend
    {
        public bool IsAvailable => false;

        public bool IsConnected => false;

        public event Action<StorePurchase> PurchasePending;

        public event Action<string, StoreFailure, string> PurchaseFailed;

        public event Action Changed;

        public Task<StoreResult> ConnectAsync(IReadOnlyList<StoreProductRequest> products,
                                              CancellationToken cancellation = default)
            => Task.FromResult(StoreResult.Failed(StoreFailure.Unavailable, "no store backend configured"));

        public StoreProductInfo Info(string productId) => null;

        public StoreResult Buy(string productId)
            => StoreResult.Failed(StoreFailure.Unavailable, "no store backend configured");

        public void Confirm(StorePurchase purchase) { }

        public StoreResult Restore()
            => StoreResult.Failed(StoreFailure.Unavailable, "no store backend configured");

        /// <summary>Silences the "never used" warnings without weakening the interface.</summary>
        void Unused()
        {
            PurchasePending?.Invoke(null);
            PurchaseFailed?.Invoke(null, StoreFailure.Unavailable, null);
            Changed?.Invoke();
        }
    }
}
