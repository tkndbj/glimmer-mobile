#if GLIMMER_IAP
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.Purchasing;

namespace GlimmerGrove.Iap
{
    /// <summary>
    /// Unity In-App Purchasing 5, wrapped so that nothing above it has ever heard of it.
    ///
    /// <para>
    /// The whole file exists to turn one vendor's vocabulary into
    /// <see cref="IStoreBackend"/>'s four verbs. It is deliberately the only place in the
    /// project that compiles against the SDK, and it is deliberately thin: every decision
    /// worth arguing about — when to retry, when to confirm, what a card says in each of
    /// six states, what happens to a purchase made offline — lives in
    /// <see cref="StoreService"/>, where it can be proved without a store, a device or a
    /// real card.
    /// </para>
    /// <para>
    /// <b>Why IAP 5 and not 4.</b> Google Play has required Billing Library 7 or newer of
    /// every new app and every update since August 2025, which means 4.13 at the very
    /// oldest; and 5's model is the one this design wants, because a purchase arrives as an
    /// explicit <c>PendingOrder</c> that stays pending until <c>ConfirmPurchase</c> is
    /// called. In 4 the same thing was expressible but easy to get wrong by accident —
    /// returning the wrong value from one callback finished a transaction that had not been
    /// granted.
    /// </para>
    /// <para>
    /// <b>Two things this deliberately does not do.</b> It does not validate receipts
    /// locally: <c>CrossPlatformValidator</c> runs on the one machine an attacker owns, so
    /// it stops nobody who matters and is a second implementation to keep alive — the
    /// server asks Apple and Google directly instead. And it does not let the SDK
    /// auto-process fetched pending orders (<c>ProcessPendingOrdersOnPurchasesFetched</c>
    /// is switched off), because "process" is the SDK's word and confirming is ours: a
    /// transaction is finished here, and nowhere else, after the grant has landed.
    /// </para>
    /// </summary>
    public sealed class UnityIapBackend : IStoreBackend
    {
        readonly StoreController _controller;

        readonly Dictionary<string, StoreProductInfo> _info =
            new Dictionary<string, StoreProductInfo>(StringComparer.Ordinal);

        /// <summary>
        /// The live <c>PendingOrder</c> for every transaction reported upward, so the
        /// confirmation can find the object again. Keyed by the same string
        /// <see cref="StorePurchase.Key"/> uses, so the two sides cannot drift.
        /// </summary>
        readonly Dictionary<string, PendingOrder> _orders =
            new Dictionary<string, PendingOrder>(StringComparer.Ordinal);

        readonly HashSet<string> _owned = new HashSet<string>(StringComparer.Ordinal);

        TaskCompletionSource<bool> _fetch;
        bool _connected;

        public UnityIapBackend()
        {
            _controller = UnityIAPServices.StoreController();

            _controller.OnStoreConnected += OnStoreConnected;
            _controller.OnStoreDisconnected += OnStoreDisconnected;

            _controller.OnProductsFetched += OnProductsFetched;
            _controller.OnProductsFetchFailed += OnProductsFetchFailed;

            _controller.OnPurchasePending += OnPurchasePendingOrder;
            _controller.OnPurchaseFailed += OnPurchaseFailedOrder;
            _controller.OnPurchaseDeferred += OnPurchaseDeferredOrder;

            _controller.OnPurchasesFetched += OnPurchasesFetched;
            _controller.OnPurchasesFetchFailed += OnPurchasesFetchFailed;

            // Ours to confirm, not the SDK's to process. See the type's remarks.
            _controller.ProcessPendingOrdersOnPurchasesFetched(false);
        }

        public bool IsAvailable => true;

        public bool IsConnected => _connected && _info.Count > 0;

        public event Action<StorePurchase> PurchasePending;

        public event Action<string, StoreFailure, string> PurchaseFailed;

        public event Action Changed;

        // --------------------------------------------------------------- connecting
        public async Task<StoreResult> ConnectAsync(IReadOnlyList<StoreProductRequest> products,
                                                    CancellationToken cancellation = default)
        {
            if (products == null || products.Count == 0)
                return StoreResult.Failed(StoreFailure.UnknownProduct, "nothing to fetch");

            try
            {
                await _controller.Connect();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[IAP] could not connect to the store: " + e.Message);
                return StoreResult.Failed(StoreFailure.NotConnected, e.Message);
            }

            if (cancellation.IsCancellationRequested)
                return StoreResult.Failed(StoreFailure.Error, "cancelled");

            var definitions = new List<ProductDefinition>(products.Count);
            foreach (var request in products)
            {
                definitions.Add(new ProductDefinition(
                    request.ProductId,
                    request.Kind == StoreProductKind.NonConsumable
                        ? ProductType.NonConsumable
                        : ProductType.Consumable));
            }

            // One in flight at a time. A second connect while the first is waiting would
            // leave the first task never completed, and its awaiter never resumed.
            var fetch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _fetch = fetch;

            _controller.FetchProducts(definitions);

            bool ok;
            using (cancellation.Register(() => fetch.TrySetResult(false)))
                ok = await fetch.Task;

            if (!ok) return StoreResult.Failed(StoreFailure.NotConnected, "product fetch failed");

            // Everything the store is still holding for this account, which is how a
            // purchase interrupted by a crash — or made on another device — comes back.
            // Deliberately after the product fetch, so a re-delivered transaction can be
            // named before it is reported.
            _controller.FetchPurchases();

            return StoreResult.Success;
        }

        void OnStoreConnected()
        {
            _connected = true;
            Raise();
        }

        void OnStoreDisconnected(StoreConnectionFailureDescription failure)
        {
            _connected = false;

            Debug.LogWarning($"[IAP] store disconnected: {failure?.Message} " +
                             $"(retryable {failure?.IsRetryable})");
            Raise();
        }

        void OnProductsFetched(List<Product> products)
        {
            if (products != null)
            {
                foreach (var product in products)
                {
                    string id = product?.definition?.id;
                    if (string.IsNullOrEmpty(id)) continue;

                    var metadata = product.metadata;

                    _info[id] = new StoreProductInfo
                    {
                        ProductId = id,
                        Price = metadata?.localizedPriceString ?? string.Empty,
                        CurrencyCode = metadata?.isoCurrencyCode ?? string.Empty,
                        PriceValue = metadata?.localizedPrice ?? 0m,
                        Owned = _owned.Contains(id),
                    };
                }
            }

            _fetch?.TrySetResult(true);
            Raise();
        }

        void OnProductsFetchFailed(ProductFetchFailed failure)
        {
            // Named individually, because the usual cause is one product that has not been
            // created in a console yet — and "product fetch failed" with no id is a message
            // somebody has to go and reproduce before it means anything.
            if (failure?.FailedFetchProducts != null)
            {
                foreach (var definition in failure.FailedFetchProducts)
                    Debug.LogWarning($"[IAP] the store does not know product '{definition?.id}': " +
                                     $"{failure.FailureReason}");
            }

            // Not a hard failure. A partial fetch is normal — a product still in review is
            // missing from one store and present in the other — and the shop draws the cards
            // it has prices for. Only a fetch that produced nothing at all is reported as a
            // failed connection.
            _fetch?.TrySetResult(_info.Count > 0);
            Raise();
        }

        // ---------------------------------------------------------------- purchasing
        public StoreProductInfo Info(string productId)
            => !string.IsNullOrEmpty(productId) && _info.TryGetValue(productId, out var info) ? info : null;

        public StoreResult Buy(string productId)
        {
            if (!_connected) return StoreResult.Failed(StoreFailure.NotConnected);

            var product = _controller.GetProductById(productId);
            if (product == null) return StoreResult.Failed(StoreFailure.UnknownProduct, productId);

            _controller.PurchaseProduct(product);
            return StoreResult.Success;
        }

        public void Confirm(StorePurchase purchase)
        {
            if (purchase == null) return;

            if (!_orders.TryGetValue(purchase.Key, out var order))
            {
                // Reachable and harmless: the app was restarted between the grant landing
                // and this call, so the order object is gone. The transaction is still
                // unfinished with the store, so it is re-delivered on the next fetch and
                // confirmed then — the server's second look at the same id grants nothing.
                Debug.LogWarning($"[IAP] no live order for {purchase.Key}; it will be " +
                                 "confirmed when the store re-delivers it");
                return;
            }

            _orders.Remove(purchase.Key);
            _controller.ConfirmPurchase(order);
        }

        public StoreResult Restore()
        {
            if (!_connected) return StoreResult.Failed(StoreFailure.NotConnected);

            _controller.RestoreTransactions((ok, message) =>
            {
                if (!ok) Debug.LogWarning("[IAP] restore failed: " + message);

                // Whether or not the restore itself reported success, ask for the purchase
                // list again: on Google there is nothing to "restore" in Apple's sense and
                // the fetch is what actually re-delivers anything unfinished.
                _controller.FetchPurchases();
            });

            return StoreResult.Success;
        }

        // ------------------------------------------------------------------- orders
        void OnPurchasePendingOrder(PendingOrder order) => Report(order);

        void OnPurchasesFetched(Orders orders)
        {
            if (orders == null) return;

            // What the account holds. Only meaningful for a non-consumable — a consumable
            // is used up the instant it is granted — and it is what lets the starter bundle
            // draw as owned rather than as a second charge waiting to happen.
            foreach (var confirmed in orders.ConfirmedOrders)
            {
                foreach (var id in ProductIdsOf(confirmed)) _owned.Add(id);
            }

            foreach (var id in _owned)
                if (_info.TryGetValue(id, out var info)) info.Owned = true;

            // Anything unfinished, including transactions from a previous launch. This is
            // the recovery path, and it is the reason a purchase cannot be lost to a crash.
            foreach (var pending in orders.PendingOrders) Report(pending);

            Raise();
        }

        void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure)
        {
            // Worth a warning and nothing more. Since 5.4 a failed query is reported here
            // rather than as an empty list, which matters: an empty list would have been
            // read as "this account owns nothing" and would have shown a bought bundle as
            // available again.
            Debug.LogWarning("[IAP] could not fetch purchases: " + failure?.Message);
        }

        void OnPurchaseDeferredOrder(DeferredOrder order)
        {
            // Apple's Ask to Buy, and Play's pending transactions. The money has not moved
            // and may never move, so nothing is queued — but the player has to be told,
            // because from their side they tapped buy and nothing happened.
            foreach (var id in ProductIdsOf(order))
                PurchaseFailed?.Invoke(id, StoreFailure.Deferred, "awaiting approval");
        }

        void OnPurchaseFailedOrder(FailedOrder order)
        {
            var failure = Classify(order?.FailureReason ?? PurchaseFailureReason.Unknown);
            var ids = ProductIdsOf(order);

            if (ids.Count == 0)
            {
                PurchaseFailed?.Invoke(string.Empty, failure, order?.Details);
                return;
            }

            foreach (var id in ids) PurchaseFailed?.Invoke(id, failure, order?.Details);
        }

        /// <summary>
        /// Turns one pending order into the shape <see cref="StoreService"/> understands,
        /// and keeps the order object so it can be confirmed later.
        ///
        /// <para>
        /// A cart can hold more than one item in IAP 5. This game never builds one — every
        /// purchase is a single tap on a single card — so an order with several items is a
        /// state nothing here can produce, and it is reported by its first item rather than
        /// silently dropped. Should carts ever be used, this is the method to revisit.
        /// </para>
        /// </summary>
        void Report(PendingOrder order)
        {
            if (order == null) return;

            var purchase = Translate(order);
            if (purchase == null) return;

            _orders[purchase.Key] = order;
            PurchasePending?.Invoke(purchase);
        }

        StorePurchase Translate(PendingOrder order)
        {
            var info = order.Info;
            if (info == null) return null;

            var ids = ProductIdsOf(order);
            if (ids.Count == 0)
            {
                Debug.LogWarning("[IAP] a pending order named no product; it cannot be redeemed");
                return null;
            }

            var receipt = UnifiedReceipt.Parse(info.Receipt);

            string storeName = receipt.Store;
            if (string.IsNullOrEmpty(storeName)) storeName = info.Apple != null ? AppleAppStore.Name : GooglePlay.Name;

            bool apple = storeName.IndexOf("Apple", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         storeName.IndexOf("Mac", StringComparison.OrdinalIgnoreCase) >= 0;

            string transactionId = !string.IsNullOrEmpty(info.TransactionID)
                ? info.TransactionID
                : receipt.TransactionID;

            if (string.IsNullOrEmpty(transactionId))
            {
                Debug.LogWarning($"[IAP] a pending order for '{ids[0]}' carried no transaction id");
                return null;
            }

            return new StorePurchase
            {
                ProductId = ids[0],
                TransactionId = transactionId,
                Store = apple ? "apple" : "google",

                // Google's purchase token, which is what the Play Developer API is queried
                // with. Apple needs nothing here: the App Store Server API is asked about
                // the transaction id directly, which is both smaller and impossible to
                // forge, since we fetch it ourselves rather than believing a blob.
                Payload = apple ? string.Empty : GooglePurchaseToken(receipt.Payload),
            };
        }

        static List<string> ProductIdsOf(Order order)
        {
            var ids = new List<string>(1);
            if (order == null) return ids;

            // The order's own record of what was bought, which survives a cart being
            // rebuilt; the cart is the fallback for an order that never carried one.
            var purchased = order.Info?.PurchasedProductInfo;
            if (purchased != null)
            {
                foreach (var entry in purchased)
                    if (!string.IsNullOrEmpty(entry?.productId)) ids.Add(entry.productId);
            }

            if (ids.Count > 0) return ids;

            var items = order.CartOrdered?.Items();
            if (items == null) return ids;

            foreach (var item in items)
            {
                string id = item?.Product?.definition?.id;
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }

            return ids;
        }

        static StoreFailure Classify(PurchaseFailureReason reason)
        {
            switch (reason)
            {
                case PurchaseFailureReason.UserCancelled:
                    return StoreFailure.Cancelled;

                case PurchaseFailureReason.ProductUnavailable:
                    return StoreFailure.UnknownProduct;

                case PurchaseFailureReason.PurchasingUnavailable:
                case PurchaseFailureReason.StoreNotConnected:
                case PurchaseFailureReason.UserNotAuthenticated:
                case PurchaseFailureReason.NotSupported:
                    return StoreFailure.NotConnected;

                case PurchaseFailureReason.PaymentDeclined:
                    return StoreFailure.PaymentFailed;

                // Something is already in flight for this account, or the store believes it
                // has delivered this once already. Both resolve by waiting for the fetch to
                // re-deliver it, so neither is a wall.
                case PurchaseFailureReason.ExistingPurchasePending:
                case PurchaseFailureReason.DuplicateTransaction:
                case PurchaseFailureReason.OrderStateChanged:
                    return StoreFailure.AwaitingGrant;

                case PurchaseFailureReason.OrderCancelled:
                    return StoreFailure.Cancelled;

                default:
                    return StoreFailure.Error;
            }
        }

        void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------------ receipts
        /// <summary>
        /// Google's purchase token, dug out of the receipt's payload.
        ///
        /// <para>
        /// The payload is itself JSON — <c>{"json": "...", "signature": "..."}</c> — whose
        /// <c>json</c> member is a <em>string</em> holding another JSON document, and that
        /// inner document is where <c>purchaseToken</c> lives. Two levels of escaping, and
        /// the whole reason for the plain string scan below: <c>JsonUtility</c> would need
        /// a type per level and would silently give back an empty string if either shape
        /// ever changed, which is exactly the failure that must not be silent here.
        /// </para>
        /// </summary>
        static string GooglePurchaseToken(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return string.Empty;

            string token = JsonField(payload, "purchaseToken");
            if (!string.IsNullOrEmpty(token)) return token;

            // Not found at the top level: unwrap the escaped inner document and try again.
            string inner = JsonField(payload, "json");
            if (!string.IsNullOrEmpty(inner)) token = JsonField(inner, "purchaseToken");

            if (string.IsNullOrEmpty(token))
                Debug.LogWarning("[IAP] no purchaseToken in a Google receipt; the server " +
                                 "cannot validate it and the purchase will stay pending");

            return token ?? string.Empty;
        }

        /// <summary>
        /// The value of one string field, tolerating the escaping a nested payload carries.
        ///
        /// Deliberately not a JSON parser. It reads exactly one shape — a quoted key
        /// followed by a quoted value — which is all either document needs, and it returns
        /// empty rather than throwing on anything else.
        /// </summary>
        static string JsonField(string json, string field)
        {
            if (string.IsNullOrEmpty(json)) return string.Empty;

            int key = json.IndexOf("\"" + field + "\"", StringComparison.Ordinal);
            if (key < 0)
            {
                key = json.IndexOf("\\\"" + field + "\\\"", StringComparison.Ordinal);
                if (key < 0) return string.Empty;
            }

            int colon = json.IndexOf(':', key);
            if (colon < 0) return string.Empty;

            int i = colon + 1;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\\')) i++;
            if (i >= json.Length || json[i] != '"') return string.Empty;

            i++;
            var value = new System.Text.StringBuilder();

            while (i < json.Length)
            {
                char c = json[i];

                if (c == '\\')
                {
                    if (i + 1 >= json.Length) break;

                    char next = json[i + 1];
                    if (next == '"') { i += 2; break; }          // closing quote, escaped

                    value.Append(next == 'n' ? '\n' : next == 't' ? '\t' : next);
                    i += 2;
                    continue;
                }

                if (c == '"') break;                              // closing quote, plain

                value.Append(c);
                i++;
            }

            return value.ToString();
        }

        /// <summary>
        /// The wrapper both stores' receipts arrive in. Unity builds it, so the shape is
        /// stable and <c>JsonUtility</c> is the right tool for this one level.
        /// </summary>
        [Serializable]
        struct UnifiedReceipt
        {
            public string Store;
            public string TransactionID;
            public string Payload;

            public static UnifiedReceipt Parse(string json)
            {
                if (string.IsNullOrEmpty(json)) return default;

                try { return JsonUtility.FromJson<UnifiedReceipt>(json); }
                catch (Exception e)
                {
                    Debug.LogWarning("[IAP] unreadable receipt wrapper: " + e.Message);
                    return default;
                }
            }
        }
    }
}
#endif
