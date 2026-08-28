using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Analytics;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove.Store
{
    /// <summary>Where the shop is, as one word, so a header can say it.</summary>
    public enum StoreStatus
    {
        /// <summary>No store SDK in this build. The tab is not offered.</summary>
        Unavailable = 0,

        /// <summary>Talking to the store. Cards are drawn with their prices still blank.</summary>
        Connecting,

        /// <summary>Prices are in. Everything works.</summary>
        Ready,

        /// <summary>The store refused to connect. Retryable, and said so on the screen.</summary>
        Offline,
    }

    /// <summary>What a card can say about itself, one member per honest sentence.</summary>
    public enum StoreOfferState
    {
        /// <summary>Buyable. The card shows the store's own price.</summary>
        Ready = 0,

        /// <summary>Prices have not arrived yet.</summary>
        Loading,

        /// <summary>The store could not be reached.</summary>
        Offline,

        /// <summary>
        /// The store has never heard of this product. Its card is hidden entirely rather
        /// than greyed — a product not yet created, or not for sale in this storefront, is
        /// not something a player can do anything about, and a dead card is worse than no
        /// card.
        /// </summary>
        Missing,

        /// <summary>A one-time product this account already holds.</summary>
        Owned,

        /// <summary>
        /// A heart container a bigger one already covers, so buying it would hand over
        /// nothing.
        ///
        /// <para>
        /// Its own state rather than <see cref="Owned"/>, because the two are different
        /// sentences and only one of them is true: a player who bought the 50 does not own
        /// the 10, and a card marked YOURS over a purchase they never made is the shop
        /// telling them something they can check and find wrong. What they need to know is
        /// that this rung is already included in what they hold — which is also the answer
        /// to "why can I not buy it".
        /// </para>
        /// <para>
        /// Drawn rather than hidden, for <see cref="Missing"/>'s reason read the other way
        /// round: a ladder with a rung cut out of it reads as a shop that has lost something,
        /// where the whole ladder with the lower rungs marked included reads as what it is —
        /// a player at the top of it. See <c>HeartContainerLedger.Covers</c>.
        /// </para>
        /// </summary>
        Included,

        /// <summary>Bought, paid for, and waiting on a connection to be credited.</summary>
        AwaitingGrant,

        /// <summary>
        /// The payment sheet has been asked for and has not answered yet.
        ///
        /// <para>
        /// Its own state because the sheet is not always visible: on Android it is a separate
        /// activity, so the app is backgrounded and comes back to a card that has to say
        /// something about the tap that took the player away. Without it the card reads as
        /// buyable again, and the second tap is the one that gets refused for a purchase
        /// already in flight.
        /// </para>
        /// </summary>
        Purchasing,
    }

    /// <summary>What a card offers: a state, and a price when there is one.</summary>
    public readonly struct StoreOffer
    {
        public readonly StoreOfferState State;

        /// <summary>The store's own formatted price. Empty in every state but Ready.</summary>
        public readonly string Price;

        public StoreOffer(StoreOfferState state, string price = null)
        {
            State = state;
            Price = price ?? string.Empty;
        }

        public bool CanBuy => State == StoreOfferState.Ready;
    }

    /// <summary>What arrived, so a celebration can be built from it.</summary>
    public readonly struct StoreGrant
    {
        public readonly StoreProduct Product;
        public readonly long Credits;
        public readonly long Gems;

        /// <summary>
        /// Where the heart refill cap stood before this purchase, when it was a container.
        ///
        /// <para>
        /// Read before the entitlement is applied, because after it the old figure is gone —
        /// and "5 → 20" is the whole of what a receipt for a container has to say. Zero on
        /// every currency product. See <c>HeartContainerLedger</c>.
        /// </para>
        /// </summary>
        public readonly int CapacityWas;

        public StoreGrant(StoreProduct product, long credits, long gems, int capacityWas = 0)
        {
            Product = product;
            Credits = credits;
            Gems = gems;
            CapacityWas = capacityWas < 0 ? 0 : capacityWas;
        }

        public bool IsValid => Product != null;

        /// <summary>True when this purchase raised the heart cap rather than paying currency.</summary>
        public bool IsContainer => Product != null && Product.IsContainer;
    }

    /// <summary>Why a gem-priced good could not be bought.</summary>
    public enum GoodOfferState
    {
        Ready = 0,

        /// <summary>Not enough gems. The shop offers the gem shelf rather than greying out.</summary>
        ShortOfGems,

        /// <summary>
        /// Buying would push the player past the heart ceiling, so some of what they paid
        /// for would evaporate.
        ///
        /// <para>
        /// Refused rather than silently clamped, and that is a deliberate departure from
        /// how every free grant in this game behaves. A chest opened at the ceiling loses
        /// its surplus and that is fine — nobody paid for it. Taking gems for hearts that
        /// are thrown away on arrival is a different thing entirely, and it is the kind of
        /// thing a player notices exactly once.
        /// </para>
        /// </summary>
        HeartsNearlyFull,

        /// <summary>
        /// Buying would push the boost past <c>HeartRules.MaxBoostHours</c>, wasting hours
        /// that were paid for. Refused for <see cref="HeartsNearlyFull"/>'s reason.
        /// </summary>
        BoostNearlyFull,

        /// <summary>The catalog does not carry this good. A content mistake, said plainly.</summary>
        Missing,
    }

    /// <summary>
    /// The shop, and the one path real money takes into this game.
    ///
    /// <para>
    /// <b>The ordering here is the entire safety property of the feature</b>, so it is
    /// worth stating before anything else. A purchase arrives from the store as an
    /// <em>unfinished</em> transaction. It is handed to our own server, which asks Apple or
    /// Google whether it really happened, records it against a globally unique key, and
    /// grants the currency. Only then is the transaction confirmed with the store. Nothing
    /// in between is optimistic: no balance moves on this device on its own say-so, and no
    /// transaction is ever confirmed for a grant that did not land.
    /// </para>
    /// <para>
    /// Everything that can go wrong is therefore some flavour of "the transaction is still
    /// unfinished", and both stores re-deliver an unfinished transaction on every launch
    /// until it is confirmed. A crash between the payment and the grant, a flat battery, a
    /// tunnel, a server outage, a force-quit — all of them come back as the same thing, and
    /// all of them are recovered by the same retry. That is why there is no per-purchase
    /// state anywhere in the save file: the store is already keeping the record, far more
    /// reliably than a client could.
    /// </para>
    /// <para>
    /// <b>Google's three-day rule is the one real deadline.</b> An unacknowledged Play
    /// purchase is refunded automatically after three days, and confirming is what
    /// acknowledges it. That is the reason the retry below is aggressive — immediately, then
    /// on a doubling backoff, then on every reconnection and every foreground — rather than
    /// polite. It is also, on balance, a good rule: a player whose grant never lands gets
    /// their money back without asking.
    /// </para>
    /// <para>
    /// <b>What is deliberately not here.</b> There is no local receipt validation. Unity's
    /// <c>CrossPlatformValidator</c> runs on the device, which is the one machine an
    /// attacker owns, so it stops nobody who matters and adds a second implementation to
    /// keep alive. The only validation that counts is the server asking the store, over TLS,
    /// with a key only we hold — see <c>firebase/functions/src/receipts.ts</c>.
    /// </para>
    /// </summary>
    public static class StoreService
    {
        static IStoreBackend _backend = new NullStoreBackend();

        /// <summary>Transactions the store has completed and the server has not yet honoured.</summary>
        static readonly Dictionary<string, StorePurchase> _pending =
            new Dictionary<string, StorePurchase>(StringComparer.Ordinal);

        /// <summary>
        /// The retry policy, borrowed whole from the sync rather than written again.
        ///
        /// It is the same problem — work that is owed, a network that comes and goes, and a
        /// backoff that must not punish a device for being in a tunnel — and it is already
        /// proved offline by <c>SyncSchedulerTests</c>. A second implementation would be a
        /// second thing to get wrong, in the feature where getting it wrong costs money.
        /// </summary>
        static readonly SyncScheduler _retry = new SyncScheduler();

        static bool _draining;
        static bool _connecting;

        /// <summary>
        /// The product whose payment sheet is open, or empty.
        ///
        /// <para>
        /// Deliberately not persisted and deliberately cleared by every outcome. It is a fact
        /// about this run of the process — if the app dies with a sheet open, the transaction
        /// comes back through the ordinary re-delivery path and this has nothing to add.
        /// </para>
        /// </summary>
        static string _checkout = string.Empty;

        /// <summary>Raised when anything a shop screen draws has changed.</summary>
        public static event Action Changed;

        /// <summary>Raised once per purchase, after the server has granted it.</summary>
        public static event Action<StoreGrant> Granted;

        /// <summary>Raised when a purchase attempt ended without a transaction.</summary>
        public static event Action<string, StoreFailure, string> Failed;

        public static bool IsAvailable => _backend != null && _backend.IsAvailable;

        /// <summary>True while a purchase is bought and not yet credited.</summary>
        public static bool HasUnredeemed => _pending.Count > 0;

        public static StoreStatus Status
        {
            get
            {
                if (!IsAvailable) return StoreStatus.Unavailable;
                if (_backend.IsConnected) return StoreStatus.Ready;
                return _connecting ? StoreStatus.Connecting : StoreStatus.Offline;
            }
        }

        /// <summary>
        /// Installs the store. Called once from <c>Boot</c>, before anything can open a shop.
        /// </summary>
        public static void UseBackend(IStoreBackend backend)
        {
            if (_backend != null)
            {
                _backend.PurchasePending -= OnPurchasePending;
                _backend.PurchaseFailed -= OnPurchaseFailed;
                _backend.Changed -= Raise;
            }

            _backend = backend ?? new NullStoreBackend();

            _backend.PurchasePending += OnPurchasePending;
            _backend.PurchaseFailed += OnPurchaseFailed;
            _backend.Changed += Raise;
        }

        /// <summary>
        /// Connects and fetches prices, in the background, from the splash.
        ///
        /// <para>
        /// Deliberately not lazy. Fetching product metadata is a round trip to the store and
        /// takes a second or more on a cold cellular connection, and a shop that opens with
        /// no prices on it for that second is a shop players back out of. It is also the one
        /// call that can be made long before anybody wants it, because it needs no account
        /// and changes nothing.
        /// </para>
        /// <para>
        /// It must never be awaited on the boot path. A store that cannot be reached must
        /// cost the shop tab and nothing else.
        /// </para>
        /// </summary>
        public static void BeginConnect(CancellationToken cancellation = default)
        {
            if (!IsAvailable)
            {
                // Said out loud, because this is indistinguishable on screen from a store
                // that could not be reached, and the causes are nothing alike: this one is a
                // build that has no store SDK compiled into it at all.
                Debug.Log("[Store] no store backend; the shop will show its unavailable state");
                return;
            }

            if (_backend.IsConnected || _connecting) return;

            _ = ReportConnectAsync(cancellation);
        }

        /// <summary>
        /// Connects, and says what happened.
        ///
        /// <para>
        /// Its own method because <see cref="BeginConnect"/> is fire-and-forget, and an
        /// awaited task whose result is discarded reports nothing anywhere — which is
        /// exactly what happened the first time this shipped to a device: the shop drew its
        /// empty state, and the device log carried not one line explaining why. The one call
        /// that decides whether the shop works at all has to leave a trace of its outcome.
        /// </para>
        /// </summary>
        static async Task ReportConnectAsync(CancellationToken cancellation)
        {
            var result = await ConnectAsync(cancellation);

            if (!result.Ok)
            {
                Debug.LogWarning($"[Store] could not connect ({result.Failure}: {result.Message}). " +
                                 "The shop will offer nothing until this succeeds.");
                return;
            }

            // The count is the useful half. A connection that succeeds and returns nothing is
            // the commonest failure by far, and it means the store — not this code — has no
            // products to give: an agreement not yet active, products still short of their
            // metadata, or a catalog that has not propagated yet.
            var catalog = StoreRules.Catalog;
            int priced = 0;
            foreach (var product in catalog.Products)
            {
                var info = _backend.Info(product.Id);
                if (info != null && info.HasPrice) priced++;
            }

            if (priced == 0)
            {
                Debug.LogWarning($"[Store] connected, but the store priced 0 of " +
                                 $"{catalog.Products.Count} product(s). Nothing is buyable. " +
                                 "Check the store agreement is active and every product is at " +
                                 "least 'Ready to Submit'.");
                return;
            }

            Debug.Log($"[Store] connected: {priced} of {catalog.Products.Count} product(s) priced");

            // Named individually, because a product missing from one storefront while the
            // rest work is a per-product configuration mistake and the id is the whole clue.
            foreach (var product in catalog.Products)
            {
                var info = _backend.Info(product.Id);
                if (info == null || !info.HasPrice)
                    Debug.LogWarning($"[Store] '{product.Id}' has no price; its card is hidden");
            }
        }

        public static async Task<StoreResult> ConnectAsync(CancellationToken cancellation = default)
        {
            if (!IsAvailable) return StoreResult.Failed(StoreFailure.Unavailable);
            if (_connecting) return StoreResult.Failed(StoreFailure.NotConnected, "already connecting");

            var catalog = StoreRules.Catalog;
            var requests = new List<StoreProductRequest>(catalog.Products.Count);
            foreach (var product in catalog.Products)
                requests.Add(new StoreProductRequest(product.Id, product.Kind));

            if (requests.Count == 0) return StoreResult.Failed(StoreFailure.UnknownProduct, "empty catalog");

            _connecting = true;
            Raise();

            StoreResult result;
            try
            {
                result = await _backend.ConnectAsync(requests, cancellation);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                result = StoreResult.Failed(StoreFailure.Error, e.Message);
            }
            finally
            {
                _connecting = false;
            }

            Raise();
            return result;
        }

        // ------------------------------------------------------------------- offers
        /// <summary>
        /// What one card can say. Every state renders a different sentence, which is
        /// <c>AdOfferState</c>'s rule: a single greyed-out button teaches a player that the
        /// shop is broken, and only one of these states is anything like broken.
        /// </summary>
        public static StoreOffer OfferFor(StoreProduct product)
        {
            if (product == null || !product.IsValid) return new StoreOffer(StoreOfferState.Missing);
            if (!IsAvailable) return new StoreOffer(StoreOfferState.Missing);

            if (IsAwaitingGrant(product.Id)) return new StoreOffer(StoreOfferState.AwaitingGrant);

            if (string.Equals(_checkout, product.Id, StringComparison.Ordinal))
                return new StoreOffer(StoreOfferState.Purchasing);

            if (!_backend.IsConnected)
                return new StoreOffer(_connecting ? StoreOfferState.Loading : StoreOfferState.Offline);

            var info = _backend.Info(product.Id);
            if (info == null || !info.HasPrice) return new StoreOffer(StoreOfferState.Missing);

            // Our own record first, and the store's second. They answer different questions:
            // the store knows what this *store account* has bought, and the ledger knows what
            // this *game account* holds — which is what a player who signed in on a friend's
            // phone, or linked after buying as a guest, actually cares about. Either saying
            // yes is enough to stop offering it, because neither can sell it again.
            if (product.IsContainer && HeartContainerLedger.IsHeld(product.Id))
                return new StoreOffer(StoreOfferState.Owned);

            // A smaller vessel than the one they hold. The cap is the largest container held
            // and never the sum, so this would take real money and change nothing a player
            // can see — which is the one thing a shop must never sell, and it is not a state
            // either store can refuse for us: these are three separate non-consumables, so
            // both would happily charge for the 10 to somebody holding the 50.
            if (HeartContainerLedger.Covers(product))
                return new StoreOffer(StoreOfferState.Included);

            // A refunded container is buyable again, and the store's own receipt must not be
            // allowed to say otherwise: both stores keep a record of a refunded
            // non-consumable for a while, so trusting `Owned` here would leave the player
            // looking at a card marked YOURS for something they no longer hold and cannot
            // get back. The ledger is the authority on this one product.
            if (product.IsOneTime && info.Owned && !HeartContainerLedger.WasRevoked(product.Id))
                return new StoreOffer(StoreOfferState.Owned);

            return new StoreOffer(StoreOfferState.Ready, info.Price);
        }

        /// <summary>True when a transaction for this product is bought and not yet credited.</summary>
        public static bool IsAwaitingGrant(string productId)
        {
            foreach (var pair in _pending)
                if (string.Equals(pair.Value.ProductId, productId, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Opens the payment sheet.
        ///
        /// Returns only whether the sheet could be opened. Whether it was paid for arrives
        /// later, through <see cref="Granted"/> or <see cref="Failed"/>, because on Android
        /// the sheet routinely outlives the process that opened it.
        /// </summary>
        public static StoreResult Buy(StoreProduct product)
        {
            if (product == null || !product.IsValid)
                return StoreResult.Failed(StoreFailure.UnknownProduct, "no such product");

            var offer = OfferFor(product);
            if (!offer.CanBuy)
            {
                return StoreResult.Failed(
                    offer.State == StoreOfferState.Owned ||
                    offer.State == StoreOfferState.Included ? StoreFailure.AlreadyOwned
                    : offer.State == StoreOfferState.AwaitingGrant ? StoreFailure.AwaitingGrant
                    : offer.State == StoreOfferState.Offline ? StoreFailure.NotConnected
                    : StoreFailure.UnknownProduct);
            }

            Telemetry.Track("store_checkout_opened",
                            "product", product.Id,
                            "shelf", product.Shelf.ToString(),
                            "credits", product.Credits,
                            "gems", product.Gems);

            _checkout = product.Id;
            Raise();

            var opened = _backend.Buy(product.Id);

            // Cleared here only when the sheet never opened. Every other way out of a
            // checkout — a transaction, a cancellation, a decline — arrives as an event, and
            // clearing it optimistically would put the card back to "buy" while a sheet was
            // still in front of the player.
            if (!opened.Ok) { _checkout = string.Empty; Raise(); }

            return opened;
        }

        /// <summary>
        /// Asks the store to re-deliver everything this account holds.
        ///
        /// <para>
        /// Apple requires a control for this in any app selling a non-consumable, and the
        /// starter bundle is one. It is also the manual version of what this class does
        /// automatically, so it is the right thing to point a player at when a purchase has
        /// not landed — it costs nothing and it cannot double-grant, because every
        /// re-delivered transaction carries the same id the server has already recorded.
        /// </para>
        /// </summary>
        public static StoreResult Restore()
        {
            if (!IsAvailable) return StoreResult.Failed(StoreFailure.Unavailable);

            Telemetry.Track("store_restore_requested");
            return _backend.Restore();
        }

        // ---------------------------------------------------------------- redemption
        static void OnPurchasePending(StorePurchase purchase)
        {
            if (purchase == null || !purchase.IsValid) return;

            // Keyed by store and transaction, so a re-delivery of something already queued
            // replaces it rather than queuing a second copy — which is what an app resumed
            // twice while offline would otherwise accumulate.
            _pending[purchase.Key] = purchase;
            if (string.Equals(_checkout, purchase.ProductId, StringComparison.Ordinal))
                _checkout = string.Empty;

            Telemetry.Track("store_purchase_pending",
                            "product", purchase.ProductId, "store", purchase.Store);

            Raise();
            Drain();
        }

        static void OnPurchaseFailed(string productId, StoreFailure failure, string message)
        {
            if (string.IsNullOrEmpty(productId) || string.Equals(_checkout, productId, StringComparison.Ordinal))
                _checkout = string.Empty;

            // Cancelling is not a failure and is deliberately not tracked as one: a
            // cancellation rate that counts people closing a sheet they opened by accident
            // makes the shop look broken in every dashboard it appears in.
            if (failure != StoreFailure.Cancelled)
            {
                Telemetry.Track("store_purchase_failed",
                                "product", productId ?? string.Empty,
                                "reason", failure.ToString());
            }

            try { Failed?.Invoke(productId, failure, message); }
            catch (Exception e) { Debug.LogException(e); }

            Raise();
        }

        /// <summary>
        /// Advances the retry policy. Driven from <c>Boot.Pump</c> beside the sync's own
        /// tick, so the whole thing holds no clock and can be run a thousand simulated
        /// frames at a time offline — <c>RunScreen.Tick</c>'s bargain, in the feature where a
        /// mistake is charged to somebody's card.
        /// </summary>
        public static void Tick(float deltaSeconds, bool networkReachable)
        {
            _retry.NetworkChanged(networkReachable);
            if (_retry.Tick(deltaSeconds)) Drain();
        }

        /// <summary>
        /// The app came back to the foreground. Clears the backoff and tries at once.
        ///
        /// Worth its own entry point because a device that has been asleep for a night has
        /// a backoff sitting at its five-minute ceiling, and five minutes of a player
        /// staring at a shop that owes them gems is five minutes too many.
        /// </summary>
        public static void Resumed()
        {
            _retry.Settled();
            if (_pending.Count > 0) Drain();
        }

        static async void Drain()
        {
            // Nothing owed: settle the policy so a later network change does not fire a
            // pointless attempt.
            if (_pending.Count == 0) { _retry.Succeeded(); return; }

            // A drain is already running. Deliberately *not* settled here — the work is
            // still owed, and reporting success on behalf of an attempt that has not
            // finished would clear a backoff the in-flight drain is about to need.
            if (_draining) return;

            _draining = true;
            bool allDone = true;

            try
            {
                // A copy, because redeeming awaits and the store may deliver another
                // transaction into the dictionary while it does.
                var batch = new List<StorePurchase>(_pending.Values);

                foreach (var purchase in batch)
                {
                    bool honoured = await RedeemAsync(purchase);
                    if (!honoured) allDone = false;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                allDone = false;
            }
            finally
            {
                _draining = false;
            }

            if (allDone && _pending.Count == 0) _retry.Succeeded();
            else { _retry.Request(); _retry.Failed(); }

            Raise();
        }

        /// <summary>
        /// Hands one transaction to the server and, only if that works, confirms it.
        ///
        /// <para>
        /// Returns whether the transaction is finished with. False leaves it in the queue
        /// and therefore unconfirmed with the store, which is the safe direction in every
        /// case: an unconfirmed purchase is re-delivered, and on Google it is eventually
        /// refunded. A confirmed purchase that was never granted is gone for ever.
        /// </para>
        /// <para>
        /// <b>A refused receipt is deliberately never confirmed — with exactly one
        /// exception.</b> The temptation is to clear it out so the queue stops retrying, and
        /// that is precisely the wrong move, because "the server refused" covers a product that
        /// has not been added to <c>config/products</c> yet as well as a genuinely bad receipt.
        /// Confirming the first case would charge a player for a configuration mistake and
        /// destroy the evidence; leaving it lets the store's own refund path resolve it, and
        /// lets a re-seed fix it retroactively the next time the app opens.
        /// </para>
        /// <para>
        /// The exception is <see cref="CloudFailure.AlreadyRedeemed"/>, and it is the exception
        /// because it is the one refusal that cannot become a success later: the transaction was
        /// granted to a different account, a receipt document is never deleted and its owner is
        /// never rewritten. Leaving that one unfinished is not a harmless retry — it is a loop
        /// for the life of the install, and on Google it ends in an auto-refund after three days
        /// whose sweep reverses the grant against <em>the account that actually paid</em>. So
        /// finishing it is what protects that account rather than what abandons this one.
        /// Nothing is granted here and nothing can be: the server refused, and confirming is
        /// purely a message to the store saying this device has nothing further to do with the
        /// transaction. Reached by switching accounts and by deleting one — both re-deliver a
        /// purchase belonging to the previous account, for ever.
        /// </para>
        /// </summary>
        static async Task<bool> RedeemAsync(StorePurchase purchase)
        {
            var receipt = new PurchaseReceipt
            {
                Store = purchase.Store,
                TransactionId = purchase.TransactionId,
                ProductId = purchase.ProductId,
                Payload = purchase.Payload,
            };

            var (result, redemption) = await CloudSaveService.RedeemPurchaseAsync(receipt);

            if (result.Failure == CloudFailure.AlreadyRedeemed)
            {
                // Finished and dropped, granting nothing. See the remarks: this is the only
                // refusal that can never turn into a success, and the account it protects by
                // being closed out is the one that paid for it.
                Debug.LogWarning($"[Store] {purchase.ProductId} belongs to another account " +
                                 $"({result.Message}); finishing the transaction rather than " +
                                 "asking again for the life of this install");

                _pending.Remove(purchase.Key);
                _backend.Confirm(purchase);
                return true;
            }

            if (!result.Ok)
            {
                Debug.LogWarning($"[Store] {purchase.ProductId} not credited yet " +
                                 $"({result.Failure}: {result.Message}); the transaction stays " +
                                 "unfinished and will be retried");
                return false;
            }

            _pending.Remove(purchase.Key);
            _backend.Confirm(purchase);

            var product = StoreRules.Find(purchase.ProductId);

            // What the *server* says it granted, not what the balance appears to have moved
            // by. A background sync can land inside the await above, so a subtraction of two
            // readings is occasionally wrong — and the place it would be wrong is the panel
            // that says thank you after somebody has paid.
            long credits = redemption.AmountOf(Currency.Credits);
            long gems = redemption.AmountOf(Currency.Gems);

            // A heart container is recorded here, and it is recorded on *every* successful
            // redemption rather than only on the first — including the ones the server
            // reports as already granted. That is the property the whole feature rests on
            // rather than an optimisation. A capacity is idempotent, so re-applying it is
            // free; and both stores re-deliver a non-consumable for ever, so a player who
            // reinstalls, changes phone or loses their save gets it back by tapping Restore,
            // with no state of ours involved and nothing for support to repair. Granting only
            // on first delivery would make that impossible for the one purchase in this shop
            // that can never be bought a second time.
            //
            // Note what it does *not* wait for: the sync. The entitlement is in the save the
            // moment the receipt is honoured, so a player who buys on a train sees their bar
            // grow before the next wallet reply arrives — the reply's only job here is to
            // carry a refund back the other way.
            int capacityWas = 0;
            bool entitled = false;

            if (product != null && product.IsContainer)
            {
                capacityWas = HeartContainerLedger.RefillCap;
                entitled = HeartContainerLedger.Grant(product);

                if (entitled)
                {
                    // Before the celebration and before the sync, for CompanionLedger.TryBuy's
                    // reason: the receipt is already confirmed with the store, so this write is
                    // the only record on this device that it happened.
                    SaveService.Save();
                    CloudSaveService.RequestSync();
                }
            }

            // `linked` is the number the whole guest-purchase feature is answered by: what
            // share of real money lands on an account that dies with the installation. It is
            // read here rather than inferred later because this is the only moment both facts
            // are in hand at once, and because a purchase credited on the launch after a crash
            // is exactly the case a session-level property would attribute to the wrong state.
            Telemetry.Track("store_purchase_granted",
                            "product", purchase.ProductId,
                            "store", purchase.Store,
                            "credits", credits,
                            "gems", gems,
                            "capacity", product != null ? product.HeartCapacity : 0,
                            "already", redemption.AlreadyGranted,
                            "linked", Cloud.CloudSaveService.IsLinked);

            // Nothing to celebrate on a retry, which is what every launch after an
            // interrupted purchase looks like: the server had already honoured it, and
            // congratulating somebody for reopening the app is how a celebration stops
            // meaning anything.
            //
            // A container reads that question differently and has to. `GrantedAnything` is
            // about currency, and a container grants none — so what decides is whether this
            // device's own ledger moved. That answers both halves at once: the first purchase
            // celebrates, and a re-delivery onto a device that already holds it does not,
            // which is exactly the retry case above. A Restore onto a fresh install *does*
            // celebrate, and should: from the player's side something they had lost has just
            // come back.
            bool worthShowing = product != null &&
                                (product.IsContainer ? entitled : redemption.GrantedAnything);

            if (worthShowing)
            {
                var grant = new StoreGrant(product, credits, gems, capacityWas);
                try { Granted?.Invoke(grant); }
                catch (Exception e) { Debug.LogException(e); }
            }

            return true;
        }

        // --------------------------------------------------------------- gem goods
        /// <summary>
        /// Whether a gem-priced good can be bought right now, and why not when it cannot.
        ///
        /// The two "nearly full" states are the interesting ones — see
        /// <see cref="GoodOfferState.HeartsNearlyFull"/>.
        /// </summary>
        public static GoodOfferState OfferForGood(StoreGood good)
        {
            if (good == null || !good.IsValid) return GoodOfferState.Missing;

            if (good.Kind == StoreGoodKind.Hearts)
            {
                if (Wallet.Hearts.Count + good.Amount > HeartRules.Ceiling)
                    return GoodOfferState.HeartsNearlyFull;
            }
            else if (good.Kind == StoreGoodKind.HeartBoost)
            {
                long ceiling = HeartRules.MaxBoostHours * 3600L;
                if (Wallet.HeartBoostSecondsLeft + good.Amount * 3600L > ceiling)
                    return GoodOfferState.BoostNearlyFull;
            }

            if (!PlayerProgression.CanAfford(Currency.Gems, good.Gems)) return GoodOfferState.ShortOfGems;

            return GoodOfferState.Ready;
        }

        /// <summary>
        /// Buys a good with gems: hearts, or a faster clock.
        ///
        /// <para>
        /// No network, no receipt, no server round trip — and that is not a shortcut, it is
        /// the reason goods are priced in gems at all. A gem debit is a
        /// <c>CurrencyLedger.TrySpend</c>, which carries an idempotency key, is refused by
        /// the server on the next sync if the derived balance could not cover it, and works
        /// on a plane. Hearts are then granted exactly the way a chest grants them. This is
        /// the same two lines that buy a companion, and it needs no more than they do.
        /// </para>
        /// <para>
        /// The debit goes first. If the process dies between the two the player has lost the
        /// gems and not received the hearts, which is a window of one disk write; the other
        /// order would hand out hearts for nothing whenever a debit was refused, which is
        /// not a window at all but a rule.
        /// </para>
        /// </summary>
        public static GoodOfferState TryBuyGood(StoreGood good)
        {
            var state = OfferForGood(good);
            if (state != GoodOfferState.Ready) return state;

            if (!PlayerProgression.TrySpend(Currency.Gems, good.Gems, good.SpendReason))
                return GoodOfferState.ShortOfGems;

            switch (good.Kind)
            {
                case StoreGoodKind.Hearts:
                    Wallet.GrantHearts(good.Amount);
                    break;

                case StoreGoodKind.HeartBoost:
                    Wallet.GrantHeartBoost(good.Amount);
                    break;
            }

            Telemetry.Track("store_good_bought",
                            "good", good.Id,
                            "kind", StoreGoodKinds.Id(good.Kind),
                            "amount", good.Amount,
                            "gems", good.Gems);

            // The debit is owed to the server. Requesting rather than syncing outright is
            // the debounce doing its job: a player buying two things in a row is one write.
            CloudSaveService.RequestSync();

            Raise();
            return GoodOfferState.Ready;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Puts the service back to its installed state. Tests use this.</summary>
        internal static void Reset()
        {
            UseBackend(new NullStoreBackend());
            _pending.Clear();
            _draining = false;
            _connecting = false;
            _checkout = string.Empty;
        }
    }
}
