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
    /// What the redemption queue does with a transaction the server will not honour.
    ///
    /// <para>
    /// <b>The rule has one exception and the whole fixture is about telling them apart.</b>
    /// Invariant 18a: a refused receipt is never confirmed, because "the server refused" covers
    /// a product missing from <c>config/products</c> as well as a bad receipt, and confirming
    /// the first charges a player for a configuration mistake and destroys the evidence. Every
    /// refusal is therefore left unfinished and retried for ever — which is right, because
    /// every refusal here is temporary.
    /// </para>
    /// <para>
    /// Except one. A receipt already granted to a <em>different account</em> can never become
    /// this account's: the document is never deleted and its owner is never rewritten. Left
    /// unfinished it is a loop for the life of the install, and on Google it ends in the
    /// three-day auto-refund whose sweep reverses the grant against the account that actually
    /// paid — so the account harmed by not confirming is not even the one holding the phone.
    /// It is reached by switching accounts, and now also by deleting one, since both leave the
    /// store re-delivering a purchase belonging to the previous account.
    /// </para>
    /// <para>
    /// Both directions are tested, and the negative one matters more: a fixture that only
    /// proved the new branch would pass just as happily if <em>every</em> refusal started
    /// confirming, which is the mistake invariant 18a is written to prevent.
    /// </para>
    /// </summary>
    public sealed class StoreReceiptTests
    {
        const string Uid = "uid-mine";

        Store _store;
        Cloud _cloud;

        [SetUp]
        public void Open()
        {
            SaveService.Unload();
            GlimmerGrove.Progression.EndlessCoins.UseStore(new GlimmerGrove.Progression.EndlessCoins.MemoryStore());
            SaveService.LoadWith(new MemoryStore());
            CloudState.SignIn(Uid);

            _cloud = new Cloud { Session = Uid };
            CloudSaveService.UseBackend(_cloud);

            _store = new Store();
            StoreService.UseBackend(_store);
        }

        [TearDown]
        public void Close()
        {
            StoreService.Reset();
            CloudSaveService.UseBackend(null);
            SaveService.Unload();
            GlimmerGrove.Progression.EndlessCoins.UseStore(null);
        }

        // ================================================================== the exception
        /// <summary>
        /// The one refusal that is finished rather than retried. Nothing is granted by it —
        /// the server said no — and confirming is only this device telling the store it has
        /// nothing further to do with the transaction.
        /// </summary>
        [Test]
        public void AReceiptBelongingToAnotherAccountIsFinishedRatherThanAskedAboutForEver()
        {
            _cloud.Refuses = CloudFailure.AlreadyRedeemed;

            _store.Deliver("gg_heart_vessel_1", "txn-from-the-old-account");

            Assert.AreEqual(1, _store.Confirmed.Count,
                            "the transaction was left unfinished, so it will be re-delivered for ever");
            Assert.AreEqual("txn-from-the-old-account", _store.Confirmed[0]);
            Assert.IsFalse(StoreService.HasUnredeemed, "it is still sitting in the queue");
        }

        /// <summary>
        /// Confirming it must not look like a purchase landing. Nothing was granted, so nothing
        /// may be celebrated — a thank-you panel over a refusal is worse than the loop.
        /// </summary>
        [Test]
        public void FinishingItGrantsNothingAndCelebratesNothing()
        {
            var grants = new List<StoreGrant>();
            void Watch(StoreGrant g) => grants.Add(g);

            StoreService.Granted += Watch;
            try
            {
                _cloud.Refuses = CloudFailure.AlreadyRedeemed;
                _store.Deliver("gg_gems_1", "txn-somebody-elses");
            }
            finally { StoreService.Granted -= Watch; }

            CollectionAssert.IsEmpty(grants, "a refused receipt announced a grant");
        }

        // =============================================================== the rule it excepts
        /// <summary>
        /// The case invariant 18a is written about: a product the seeder has not published yet.
        /// Temporary, so the transaction stays unfinished and a re-seed fixes it retroactively
        /// on the next launch.
        /// </summary>
        [Test]
        public void AProductTheServerDoesNotKnowAboutIsLeftUnfinished()
        {
            _cloud.Refuses = CloudFailure.Rejected;

            _store.Deliver("gg_gems_9", "txn-not-seeded-yet");

            CollectionAssert.IsEmpty(_store.Confirmed,
                                     "a temporary refusal was confirmed, so the purchase is gone");
            Assert.IsTrue(StoreService.HasUnredeemed, "it must stay queued for the retry");
        }

        [Test]
        public void ADroppedConnectionIsLeftUnfinished()
        {
            _cloud.Refuses = CloudFailure.Offline;

            _store.Deliver("gg_gems_1", "txn-mid-tunnel");

            CollectionAssert.IsEmpty(_store.Confirmed);
            Assert.IsTrue(StoreService.HasUnredeemed);
        }

        /// <summary>
        /// A device caught between two accounts must not confirm either: the receipt may still
        /// be perfectly redeemable once the sync has settled which account this is.
        /// </summary>
        [Test]
        public void AMismatchedDeviceIsLeftUnfinished()
        {
            _cloud.Refuses = CloudFailure.AccountMismatch;

            _store.Deliver("gg_coins_1", "txn-mid-switch");

            CollectionAssert.IsEmpty(_store.Confirmed);
            Assert.IsTrue(StoreService.HasUnredeemed);
        }

        /// <summary>The ordinary path, so the fixture is not only testing refusals.</summary>
        [Test]
        public void AnHonouredReceiptIsConfirmedAndDropped()
        {
            _store.Deliver("gg_gems_1", "txn-mine");

            Assert.AreEqual(1, _store.Confirmed.Count);
            Assert.IsFalse(StoreService.HasUnredeemed);
        }

        // ============================================================ one payment, one receipt
        /// <summary>
        /// The fault this section is written about: one purchase, two thank-you panels.
        ///
        /// <para>
        /// Both stores re-deliver an unfinished transaction and this game asks the server again
        /// every time one arrives, so the redemption path running twice for one payment is the
        /// ordinary case rather than the broken one. What must not happen twice is the
        /// <em>telling</em> — <c>ReceiptQueue</c> exists to draw two grants one after the other
        /// and is right to, so it cannot tell a second payment from a second telling of the
        /// first, and the player is congratulated twice for one charge with every gate green.
        /// </para>
        /// <para>
        /// Driven by delivering the same transaction twice while the server reports a grant
        /// both times, which is the strongest version of it: in life the second redemption
        /// usually comes back <c>alreadyGranted</c> and is dropped by <c>worthShowing</c>
        /// before it reaches the event at all. A fixture relying on that would be testing the
        /// server double rather than the rule.
        /// </para>
        /// </summary>
        [Test]
        public void OnePaymentIsCelebratedOnceHoweverOftenItIsRedeemed()
        {
            _cloud.Redemption = Granting(Currency.Gems, 500L);

            var grants = new List<StoreGrant>();
            void Watch(StoreGrant g) => grants.Add(g);

            StoreService.Granted += Watch;
            try
            {
                _store.Deliver("gg_gems_1", "txn-redelivered");
                _store.Deliver("gg_gems_1", "txn-redelivered");
            }
            finally { StoreService.Granted -= Watch; }

            Assert.AreEqual(1, grants.Count,
                            "one transaction announced two grants, which is two receipt panels");
            Assert.AreEqual("test__txn-redelivered", grants[0].TransactionKey,
                            "a grant with no transaction on it cannot be deduplicated by anything");
        }

        /// <summary>
        /// The other direction, and the one that matters more: a guard written the lazy way —
        /// on the product, or as a single "already celebrated" latch — would swallow the second
        /// of two real purchases, which is a player charged twice and told once. That is the
        /// failure <c>ReceiptQueue</c> was built for in the first place.
        /// </summary>
        [Test]
        public void TwoRealPurchasesAreBothCelebrated()
        {
            _cloud.Redemption = Granting(Currency.Gems, 500L);

            var grants = new List<StoreGrant>();
            void Watch(StoreGrant g) => grants.Add(g);

            StoreService.Granted += Watch;
            try
            {
                _store.Deliver("gg_gems_1", "txn-one");
                _store.Deliver("gg_gems_1", "txn-two");
            }
            finally { StoreService.Granted -= Watch; }

            Assert.AreEqual(2, grants.Count, "the same pack bought twice is two payments");
            CollectionAssert.AreEqual(new[] { "test__txn-one", "test__txn-two" },
                                      new[] { grants[0].TransactionKey, grants[1].TransactionKey });
        }

        /// <summary>
        /// The guard is session state, so it has to be cleared with everything else — a
        /// <c>Reset</c> that left it behind would leave a fixture unable to celebrate a
        /// transaction id an earlier one had used, which is the kind of cross-test coupling
        /// that is found a year later.
        /// </summary>
        [Test]
        public void AResetForgetsWhatHasBeenCelebrated()
        {
            _cloud.Redemption = Granting(Currency.Credits, 1200L);

            var grants = new List<StoreGrant>();
            void Watch(StoreGrant g) => grants.Add(g);

            StoreService.Granted += Watch;
            try
            {
                _store.Deliver("gg_coins_1", "txn-same-id");

                StoreService.Reset();
                StoreService.UseBackend(_store);

                _store.Deliver("gg_coins_1", "txn-same-id");
            }
            finally { StoreService.Granted -= Watch; }

            Assert.AreEqual(2, grants.Count);
        }

        /// <summary>A redemption that reports one currency, the way the server does.</summary>
        static CloudRedemption Granting(string currency, long amount)
        {
            var redemption = new CloudRedemption();
            redemption.Granted[currency] = amount;
            return redemption;
        }

        // ------------------------------------------------------------------ the contract
        /// <summary>
        /// The queue reads a *failure value*, so the value has to mean "never ask again". If
        /// this ever became retryable the loop comes straight back, and nothing else would say
        /// so — the symptom is one log line per launch.
        /// </summary>
        [Test]
        public void AlreadyRedeemedIsNotSomethingToRetry()
        {
            Assert.IsFalse(CloudResult.Failed(CloudFailure.AlreadyRedeemed).IsRetryable);
            Assert.IsFalse(CloudResult.Failed(CloudFailure.Rejected).IsRetryable);

            Assert.IsTrue(CloudResult.Failed(CloudFailure.Offline).IsRetryable,
                          "a dropped connection must still be retried");
            Assert.IsTrue(CloudResult.Failed(CloudFailure.AccountMismatch).IsRetryable);
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

        /// <summary>A store that hands over a transaction and records what was finished.</summary>
        sealed class Store : IStoreBackend
        {
            public readonly List<string> Confirmed = new List<string>();

            public event Action<StorePurchase> PurchasePending;
            public event Action<string, StoreFailure, string> PurchaseFailed;
            public event Action Changed;

            public bool IsAvailable => true;
            public bool IsConnected => true;

            /// <summary>
            /// Delivers a purchase the way both stores do on every launch until it is finished.
            ///
            /// The drain it starts completes inline, because the cloud double answers
            /// synchronously — so a test can assert straight after this call rather than
            /// pumping frames.
            /// </summary>
            public void Deliver(string productId, string transactionId)
                => PurchasePending?.Invoke(new StorePurchase
                {
                    ProductId = productId,
                    TransactionId = transactionId,
                    Store = "test",
                    Payload = "{}",
                });

            public void Confirm(StorePurchase purchase) => Confirmed.Add(purchase.TransactionId);

            public Task<StoreResult> ConnectAsync(IReadOnlyList<StoreProductRequest> products,
                                                  CancellationToken c = default)
                => Task.FromResult(StoreResult.Success);

            public StoreProductInfo Info(string productId) => null;

            public StoreResult Buy(string productId) => StoreResult.Success;

            public StoreResult Restore() => StoreResult.Success;

            void Unused()
            {
                PurchaseFailed?.Invoke(null, StoreFailure.Unavailable, null);
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// A cloud backend that refuses redemptions in whichever way the test asks for.
        ///
        /// Everything off the redemption path answers <see cref="CloudFailure.Rejected"/>
        /// rather than a plausible success, so a call this fixture did not intend to make fails
        /// loudly instead of being quietly satisfied.
        /// </summary>
        sealed class Cloud : ICloudSaveBackend
        {
            public string Session;

            /// <summary>How the server refuses, or <c>None</c> to honour the receipt.</summary>
            public CloudFailure Refuses = CloudFailure.None;

            /// <summary>
            /// What an honoured receipt reports as granted, and whether it had been granted
            /// before.
            ///
            /// <para>
            /// Default is <c>Nothing</c> — nothing granted, already honoured — which is the
            /// re-delivery every launch makes and is right for every refusal fixture above.
            /// A test about a <em>celebration</em> has to set it, because a grant of nothing is
            /// deliberately not worth showing.
            /// </para>
            /// </summary>
            public CloudRedemption Redemption = CloudRedemption.Nothing;

            public bool IsAvailable => true;

            public CloudIdentity CurrentIdentity
                => string.IsNullOrEmpty(Session) ? CloudIdentity.None : new CloudIdentity(Session, true);

            public Task<(CloudResult result, List<CloudWalletState> wallets, CloudRedemption redemption)>
                RedeemPurchaseAsync(string userId, PurchaseReceipt receipt, CancellationToken c = default)
                => Task.FromResult(
                    Refuses == CloudFailure.None
                        ? (CloudResult.Success, new List<CloudWalletState>(), Redemption)
                        : (CloudResult.Failed(Refuses, "refused"),
                           new List<CloudWalletState>(), CloudRedemption.Nothing));

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
