using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using GlimmerGrove.Store;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// What is said between paying and being thanked, and — the half that matters — every way
    /// the saying of it comes to an end.
    ///
    /// <para>
    /// <b>The panel this drives is raised over whatever the player is looking at, including a
    /// live siege</b>, where a modal holds the run (invariant 39i). So "it cannot get stuck" is
    /// not a nicety here: a wait that never finishes is a game that has stopped, on a screen the
    /// player paid money to be standing on. That claim is made by two independent things and
    /// this fixture drives both. <see cref="ArrivalWatch"/> reads whether the transaction is
    /// still owed off <c>StoreService</c> itself rather than off an event, so no ending can be
    /// missed by nobody having thought to announce it; and it hands out a way out after
    /// <see cref="ArrivalWatch.Patience"/> whatever happens, because a refused receipt is
    /// deliberately retried for the life of the install (invariant 18a) and "wait for it" is
    /// therefore not a bounded instruction.
    /// </para>
    /// <para>
    /// <b>The scoping is tested as hard as the mechanism, and it is the easier thing to get
    /// wrong.</b> Both stores re-deliver an unfinished transaction on every launch for ever, so
    /// a panel raised on every pending purchase would stand in front of a player whose receipt
    /// the server will not honour at every single launch. The announcement therefore fires only
    /// for a checkout <em>this process opened</em>, and the negative case below is the one that
    /// stops that shipping.
    /// </para>
    /// </summary>
    public sealed class StoreArrivalTests
    {
        const string Uid = "uid-mine";
        const string Gems = "gg_gems_1";

        Store _store;
        Cloud _cloud;

        readonly List<(string Key, string Product, bool WasPending)> _heard =
            new List<(string, string, bool)>();

        [SetUp]
        public void Open()
        {
            SaveService.Unload();
            SaveService.LoadWith(new MemoryStore());
            CloudState.SignIn(Uid);

            _cloud = new Cloud { Session = Uid };
            CloudSaveService.UseBackend(_cloud);

            _store = new Store();
            StoreService.UseBackend(_store);

            _heard.Clear();
            StoreService.CheckoutLanded += Hear;
        }

        [TearDown]
        public void Close()
        {
            StoreService.CheckoutLanded -= Hear;
            StoreService.Reset();
            CloudSaveService.UseBackend(null);
            SaveService.Unload();
        }

        /// <summary>
        /// Records the announcement <em>and what was true when it landed</em>. The second half is
        /// the assertion that cannot be made afterwards: the redemption finishes inline against
        /// an honouring double, so by the time the test body runs the transaction is long gone.
        /// </summary>
        void Hear(string key, StoreProduct product)
            => _heard.Add((key, product?.Id, StoreService.IsPending(key)));

        /// <summary>Opens the sheet and hands the transaction back, the way a real purchase goes.</summary>
        string Pay(string productId, string transactionId)
        {
            Assert.IsTrue(StoreService.Buy(StoreRules.Find(productId)).Ok,
                          "the fixture could not open a payment sheet, so nothing after this means anything");

            _store.Deliver(productId, transactionId);
            return Store.KeyOf(transactionId);
        }

        // ============================================================== what is announced
        /// <summary>
        /// The ordinary purchase: tapped here, paid for here, so it is worth saying something
        /// about.
        /// </summary>
        [Test]
        public void ATransactionTheseHandsPaidForIsAnnounced()
        {
            string key = Pay(Gems, "txn-just-bought");

            Assert.AreEqual(1, _heard.Count, "a purchase the player just made said nothing");
            Assert.AreEqual(key, _heard[0].Key, "the announcement must name the transaction, not the product");
            Assert.AreEqual(Gems, _heard[0].Product);
        }

        /// <summary>
        /// The case that decides whether this feature is a help or a fault, and it is the one a
        /// reading of the code will not produce on its own.
        ///
        /// <para>
        /// Both stores re-deliver an unfinished transaction on every launch for ever, and a
        /// receipt the server refuses is deliberately left unfinished (invariant 18a). Announced
        /// on every pending purchase, a player in that state meets a panel about a purchase they
        /// made last week every time they open the game — a fault with no way for them to clear
        /// it, in front of a run they were trying to play.
        /// </para>
        /// </summary>
        [Test]
        public void ARedeliveryNobodyJustAskedForIsSilent()
        {
            _store.Deliver(Gems, "txn-from-a-previous-launch");

            CollectionAssert.IsEmpty(_heard,
                                     "the store re-delivering its own queue is not news about anything " +
                                     "the player just did");
        }

        /// <summary>
        /// A cancelled sheet clears the checkout, so the transaction that turns up afterwards is
        /// not the one that was cancelled — it is a re-delivery, and it is silent.
        /// </summary>
        [Test]
        public void ASheetTheyBackedOutOfLeavesNothingListening()
        {
            Assert.IsTrue(StoreService.Buy(StoreRules.Find(Gems)).Ok);
            _store.Cancel(Gems);

            _store.Deliver(Gems, "txn-arriving-after-a-cancel");

            CollectionAssert.IsEmpty(_heard);
        }

        /// <summary>
        /// The announcement has to reach its listener while there is still something to wait for.
        ///
        /// <para>
        /// A redemption can finish inside the call that delivers the transaction — the double
        /// here answers synchronously and a warm connection is not far off it — so announcing
        /// after the drain would hand a panel a transaction that had already been honoured,
        /// which draws a spinner for something that has finished. The ordering in
        /// <c>StoreService.OnPurchasePending</c> is the whole of the fix and nothing else can
        /// see it.
        /// </para>
        /// </summary>
        [Test]
        public void TheAnnouncementArrivesBeforeTheRedemptionCanFinishIt()
        {
            Pay(Gems, "txn-honoured-immediately");

            Assert.AreEqual(1, _heard.Count);
            Assert.IsTrue(_heard[0].WasPending,
                          "the transaction was already finished with when the panel was told about it");
            Assert.IsFalse(StoreService.HasUnredeemed, "and it did finish, inline, as the fixture assumes");
        }

        // ================================================================== how it ends
        /// <summary>The ordinary ending: the server honours it and the watch stops waiting.</summary>
        [Test]
        public void AWatchSettlesWhenTheServerHonoursTheReceipt()
        {
            _cloud.Refuses = CloudFailure.Offline;
            string key = Pay(Gems, "txn-stuck-in-a-tunnel");

            var watch = new ArrivalWatch();
            Assert.IsTrue(watch.Watch(key));
            Assert.IsFalse(watch.Tick(0f), "nothing has changed while the receipt is still owed");
            Assert.IsFalse(watch.Settled);

            _cloud.Refuses = CloudFailure.None;
            StoreService.Resumed();

            Assert.IsTrue(watch.Tick(0f), "the honoured receipt was not noticed");
            Assert.IsTrue(watch.Settled);
        }

        /// <summary>
        /// The ending nothing announces, and the reason the watch reads the queue rather than an
        /// event.
        ///
        /// <para>
        /// A receipt already granted to another account is confirmed and dropped, granting
        /// nothing and celebrating nothing (invariant 18a's one exception). No <c>Granted</c>
        /// fires, so a panel wired to that event alone would sit over the game for ever on the
        /// one path where the player is owed nothing and can do nothing about it.
        /// </para>
        /// </summary>
        [Test]
        public void AWatchSettlesOnARefusalThatIsClosedOutRatherThanRetried()
        {
            _cloud.Refuses = CloudFailure.Offline;
            string key = Pay(Gems, "txn-from-the-old-account");

            var watch = new ArrivalWatch();
            Assert.IsTrue(watch.Watch(key));

            _cloud.Refuses = CloudFailure.AlreadyRedeemed;
            StoreService.Resumed();

            watch.Tick(0f);
            Assert.IsTrue(watch.Settled,
                          "nothing was granted, so nothing was announced — and the panel is still up");
        }

        /// <summary>
        /// The other unannounced ending: a sign-out or a test abandoning the session empties the
        /// queue outright.
        /// </summary>
        [Test]
        public void AWatchSettlesWhenTheQueueIsAbandoned()
        {
            _cloud.Refuses = CloudFailure.Offline;
            string key = Pay(Gems, "txn-abandoned");

            var watch = new ArrivalWatch();
            Assert.IsTrue(watch.Watch(key));

            StoreService.Reset();

            watch.Tick(0f);
            Assert.IsTrue(watch.Settled);
        }

        /// <summary>
        /// The ending that has nothing to do with the store: however long it takes, the player is
        /// owed a way back to their game.
        ///
        /// <para>
        /// This is the case that cannot be fixed by getting the queue right, because the queue is
        /// right — a refused receipt is <em>supposed</em> to stay in it, so a panel that waits
        /// only on the transaction waits for the life of the install.
        /// </para>
        /// </summary>
        [Test]
        public void AWayOutIsOfferedEvenWhenNothingEverArrives()
        {
            _cloud.Refuses = CloudFailure.Rejected;
            string key = Pay(Gems, "txn-the-server-will-never-honour");

            var watch = new ArrivalWatch();
            Assert.IsTrue(watch.Watch(key));

            for (int i = 0; i < 600 && !watch.Relaxed; i++) watch.Tick(1f / 60f);

            Assert.IsTrue(watch.Relaxed, "the player was held in front of a spinner with no way out");
            Assert.IsFalse(watch.Settled, "and it must still say it is waiting, because it is");
            Assert.IsTrue(StoreService.HasUnredeemed, "the purchase is still owed and still being retried");
        }

        // ============================================================== more than one
        /// <summary>
        /// Two purchases seconds apart is ordinary — a mistap, or a second pack straight after
        /// the first. The panel is one panel, and it may not go away while either is owed.
        /// </summary>
        [Test]
        public void AWatchHoldsUntilTheLastOfThemLands()
        {
            _cloud.Refuses = CloudFailure.Offline;
            string first = Pay(Gems, "txn-one");
            string second = Pay("gg_gems_2", "txn-two");

            var watch = new ArrivalWatch();
            Assert.IsTrue(watch.Watch(first));
            Assert.IsTrue(watch.Watch(second));
            Assert.AreEqual(2, watch.Count);

            // The outage lifts for one of them only, which is what a drain part-way through a
            // patchy connection really looks like.
            _cloud.Refused.Add("txn-one");
            StoreService.Resumed();
            watch.Tick(0f);

            Assert.AreEqual(1, watch.Count);
            Assert.IsFalse(watch.Settled, "one of them is still owed");

            _cloud.Refuses = CloudFailure.None;
            StoreService.Resumed();
            watch.Tick(0f);
            Assert.IsTrue(watch.Settled);
        }

        /// <summary>
        /// A wait that renews itself every time something joins it is a promise that can be
        /// broken for ever by the store being busy. The player has been waiting since the first
        /// one.
        /// </summary>
        [Test]
        public void ASecondArrivalDoesNotRestartTheClock()
        {
            _cloud.Refuses = CloudFailure.Offline;
            string first = Pay(Gems, "txn-one");

            var watch = new ArrivalWatch();
            watch.Watch(first);
            watch.Tick(ArrivalWatch.Patience - .5f);
            Assert.IsFalse(watch.Relaxed);

            string second = Pay("gg_gems_2", "txn-two");
            Assert.IsTrue(watch.Watch(second));

            watch.Tick(.5f);
            Assert.IsTrue(watch.Relaxed, "the second purchase put the way out back out of reach");
        }

        /// <summary>
        /// A control that appears under a thumb and then disappears is worse than one that was
        /// never offered.
        /// </summary>
        [Test]
        public void TheWayOutIsNeverTakenBack()
        {
            _cloud.Refuses = CloudFailure.Offline;
            var watch = new ArrivalWatch();

            watch.Watch(Pay(Gems, "txn-one"));
            watch.Tick(ArrivalWatch.Patience);
            Assert.IsTrue(watch.Relaxed);

            watch.Watch(Pay("gg_gems_2", "txn-two"));
            watch.Tick(0f);

            Assert.IsTrue(watch.Relaxed);
        }

        // ================================================================== the edges
        /// <summary>
        /// The fast path, and the reason the panel is deferred by
        /// <see cref="ArrivalWatch.Grace"/> at all: by the time the beat comes round the
        /// redemption has usually finished, and there is nothing left to draw a spinner about.
        /// </summary>
        [Test]
        public void ATransactionAlreadyFinishedWithIsNeverWatched()
        {
            string key = Pay(Gems, "txn-honoured-immediately");
            Assert.IsFalse(StoreService.HasUnredeemed);

            var watch = new ArrivalWatch();

            Assert.IsFalse(watch.Watch(key));
            Assert.IsTrue(watch.Settled);
        }

        [Test]
        public void OneTransactionIsWatchedOnce()
        {
            _cloud.Refuses = CloudFailure.Offline;
            string key = Pay(Gems, "txn-announced-twice-somehow");

            var watch = new ArrivalWatch();

            Assert.IsTrue(watch.Watch(key));
            Assert.IsFalse(watch.Watch(key), "the same transaction was queued twice");
            Assert.AreEqual(1, watch.Count);
        }

        /// <summary>
        /// Dismissing the panel ends the panel's interest and nothing else: the purchase is still
        /// owed, and <c>StoreService</c> is still retrying it on <c>Boot</c>'s clock.
        /// </summary>
        [Test]
        public void ClearingTheWatchDoesNotAbandonThePurchase()
        {
            _cloud.Refuses = CloudFailure.Offline;
            var watch = new ArrivalWatch();
            watch.Watch(Pay(Gems, "txn-dismissed"));

            watch.Clear();

            Assert.IsTrue(watch.Settled);
            Assert.IsTrue(StoreService.HasUnredeemed, "the panel took the purchase with it");
        }

        // ================================================================== the scaffolding
        sealed class MemoryStore : ISaveStore
        {
            SaveFileDto _file;

            public SaveFileDto Load() => _file ?? new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                levels = new LevelRecordDto[0],
                progression = ProgressionStateDto.Unwritten(),
                cloud = new CloudStateDto(),
                legacyImportDone = true,
            };

            public bool Save(SaveFileDto dto) { _file = dto; return true; }

            public void Delete() => _file = null;
        }

        /// <summary>
        /// A store that is connected, quotes a price, opens a sheet and hands transactions back.
        ///
        /// <para>
        /// Richer than <c>StoreReceiptTests</c>'s double on purpose: that fixture only needs
        /// deliveries, where this one needs the <em>checkout</em> to really open, because the
        /// whole of what is being tested is the difference between a purchase somebody is
        /// standing in front of and one the store is re-delivering out of its own queue.
        /// </para>
        /// </summary>
        sealed class Store : IStoreBackend
        {
            const string Name = "test";

            /// <summary>The key <c>StorePurchase</c> derives, so a test can name one.</summary>
            public static string KeyOf(string transactionId) => Name + "__" + transactionId;

            /// <summary>Transaction ids this device has told the store it is finished with.</summary>
            public readonly List<string> Confirmed = new List<string>();

            public event Action<StorePurchase> PurchasePending;
            public event Action<string, StoreFailure, string> PurchaseFailed;
            public event Action Changed;

            public bool IsAvailable => true;
            public bool IsConnected => true;

            public void Deliver(string productId, string transactionId)
                => PurchasePending?.Invoke(new StorePurchase
                {
                    ProductId = productId,
                    TransactionId = transactionId,
                    Store = Name,
                    Payload = "{}",
                });

            /// <summary>The player closed the payment sheet.</summary>
            public void Cancel(string productId)
                => PurchaseFailed?.Invoke(productId, StoreFailure.Cancelled, "closed the sheet");

            public void Confirm(StorePurchase purchase) => Confirmed.Add(purchase.TransactionId);

            public Task<StoreResult> ConnectAsync(IReadOnlyList<StoreProductRequest> products,
                                                  CancellationToken c = default)
                => Task.FromResult(StoreResult.Success);

            public StoreProductInfo Info(string productId)
                => new StoreProductInfo { ProductId = productId, Price = "$0.99", CurrencyCode = "USD" };

            public StoreResult Buy(string productId) => StoreResult.Success;

            public StoreResult Restore() => StoreResult.Success;

            void Unused() => Changed?.Invoke();
        }

        /// <summary>
        /// A cloud that honours a receipt, or refuses it in whichever way the test asks for.
        /// Everything off the redemption path answers <see cref="CloudFailure.Rejected"/>, so a
        /// call this fixture did not intend to make fails loudly rather than being satisfied.
        /// </summary>
        sealed class Cloud : ICloudSaveBackend
        {
            public string Session;

            /// <summary>How the server refuses, or <c>None</c> to honour every receipt.</summary>
            public CloudFailure Refuses = CloudFailure.None;

            /// <summary>
            /// Which transactions the refusal applies to. Empty means all of them, which is what
            /// an outage looks like; naming ids is how a test honours one receipt of two, since a
            /// drain redeems the whole queue in one pass and a single flag cannot tell them
            /// apart.
            /// </summary>
            public readonly HashSet<string> Refused = new HashSet<string>(StringComparer.Ordinal);

            bool Refusing(string transactionId)
                => Refuses != CloudFailure.None &&
                   (Refused.Count == 0 || Refused.Contains(transactionId));

            public bool IsAvailable => true;

            public CloudIdentity CurrentIdentity
                => string.IsNullOrEmpty(Session) ? CloudIdentity.None : new CloudIdentity(Session, true);

            public Task<(CloudResult result, List<CloudWalletState> wallets, CloudRedemption redemption)>
                RedeemPurchaseAsync(string userId, PurchaseReceipt receipt, CancellationToken c = default)
                => Task.FromResult(
                    Refusing(receipt.TransactionId)
                        ? (CloudResult.Failed(Refuses, "refused"),
                           new List<CloudWalletState>(), CloudRedemption.Nothing)
                        : (CloudResult.Success, new List<CloudWalletState>(), CloudRedemption.Nothing));

            public Task<(CloudResult result, CloudIdentity identity)> SignInAsync(CancellationToken c = default)
                => Task.FromResult((CloudResult.Success, CurrentIdentity));

            public Task<(CloudResult result, CloudIdentity identity)> ResumeAsync(CancellationToken c = default)
                => Task.FromResult((CloudResult.Success, CurrentIdentity));

            public Task<(CloudResult result, CloudIdentity identity)> LinkAsync(
                LinkCredential cr, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"),
                                    CloudIdentity.None));

            public Task<(CloudResult result, CloudIdentity identity)> SignInWithCredentialAsync(
                LinkCredential cr, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"),
                                    CloudIdentity.None));

            public Task<(CloudResult result, string appleAuthorizationCode)> ReauthenticateAsync(
                LinkCredential credential, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"),
                                    string.Empty));

            public Task<CloudResult> DeleteAccountAsync(
                string userId, string appleAuthorizationCode = null, CancellationToken c = default)
                => Task.FromResult(CloudResult.Failed(CloudFailure.Rejected, "not this fixture"));

            public Task<(CloudResult result, CloudSnapshot snapshot)> PullAsync(
                string userId, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"),
                                    CloudSnapshot.Missing));

            public Task<CloudResult> PushAsync(
                string userId, SaveFileDto snapshot, SaveDelta delta, CancellationToken c = default)
                => Task.FromResult(CloudResult.Failed(CloudFailure.Rejected, "not this fixture"));

            public Task<(CloudResult result, List<CloudWalletState> wallets)> ReadWalletAsync(
                string userId, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"),
                                    new List<CloudWalletState>()));

            public Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitSpendsAsync(
                string userId, IReadOnlyList<SpendSubmission> spends, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"),
                                    new List<CloudWalletState>()));

            public Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitAwardsAsync(
                string userId, IReadOnlyList<GrantEntryDto> awards, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"),
                                    new List<CloudWalletState>()));

            public Task<(CloudResult result, Dictionary<Content.LevelId, Social.LevelStats> stats)>
                ReadGroveStatsAsync(CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"),
                                    new Dictionary<Content.LevelId, Social.LevelStats>()));

            /// <summary>
            /// Nothing to say about releases, and a failure rather than "nothing is required" —
            /// see <c>NullCloudBackend.ReadReleaseAsync</c>. A double that answered success here
            /// would clear a standing update wall on behalf of a fixture that is about something
            /// else entirely.
            /// </summary>
            public Task<(CloudResult result, Release.ReleaseRequirement requirement)> ReadReleaseAsync(
                string platform, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "not this fixture"),
                                    Release.ReleaseRequirement.None));
        }
    }
}
