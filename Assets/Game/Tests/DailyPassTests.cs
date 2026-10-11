using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Store;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The seven-day passes, the client half, and the welcome bundle's value seal.
    ///
    /// <para>
    /// <b>The catalog</b> reads a pass as currency paid in daily pieces and keeps it off every
    /// ladder: no tier, no bonus, and the exchange rate the deal and bundle seals are measured by
    /// is the one the packs alone imply. A pass any server rule would refuse is dropped by name.
    /// <b>The ledger</b> is a mirror of the wallet: nothing is offered until a reply has said what
    /// the account holds, a claim adopts the server's balances only for the account that asked,
    /// and no answer leaves a claim marked as running. The server half is held by
    /// <c>firebase/functions/test/daily-pass.mjs</c>.
    /// </para>
    /// </summary>
    public sealed class DailyPassTests
    {
        const string Uid = "uid-pass";
        const string GemPass = "gg_gem_pass_7d";
        const long Day = 20_400L;

        sealed class FixedClock : IGameClock
        {
            public long Now;
            public long UtcNowUnix => Now;
            public bool IsTrusted => false;
        }

        FixedClock _clock;
        Cloud _cloud;

        [SetUp]
        public void Open()
        {
            _clock = new FixedClock { Now = Day * 86400L + 3600L };
            GameClock.Set(_clock);

            SaveService.Unload();
            EndlessCoins.UseStore(new EndlessCoins.MemoryStore());
            SaveService.LoadWith(new AccountSwitchTests.MemoryStore());
            CloudState.SignIn(Uid);

            _cloud = new Cloud { Session = Uid };
            CloudSaveService.UseBackend(_cloud);
            DailyPassLedger.ResetForTests();
        }

        [TearDown]
        public void Close()
        {
            DailyPassLedger.ResetForTests();
            CloudSaveService.UseBackend(null);
            SaveService.Unload();
            EndlessCoins.UseStore(null);
            GameClock.Set(new DeviceClock());
        }

        /// <summary>
        /// The shipped store block, read with the managed parser (<c>TestJson</c>) rather than
        /// <c>JsonUtility</c>, so these run offline - a fixture about money that only the Editor can
        /// run is a fixture that is not run.
        /// </summary>
        static StoreCatalog Shipped()
        {
            string path = Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets", "Content", "progression.json");
            if (!File.Exists(path)) Assert.Inconclusive("progression.json is not on disk");

            var store = TestJson.Child(TestJson.Object(TestJson.Parse(File.ReadAllText(path))), "store");
            var products = new List<StoreProductDto>();
            foreach (var raw in TestJson.Children(store, "products"))
            {
                var map = TestJson.Object(raw);
                products.Add(new StoreProductDto
                {
                    id = TestJson.Str(map, "id"),
                    kind = TestJson.Str(map, "kind"),
                    shelf = TestJson.Str(map, "shelf"),
                    credits = TestJson.Long(map, "credits"),
                    gems = TestJson.Long(map, "gems"),
                    heartCapacity = TestJson.Int(map, "heartCapacity"),
                    passDays = TestJson.Int(map, "passDays"),
                    referenceUsdCents = TestJson.Int(map, "referenceUsdCents"),
                    badge = TestJson.Str(map, "badge"),
                });
            }

            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto { products = products.ToArray() }, problems);
            Assert.IsEmpty(problems, string.Join("; ", problems));
            return catalog;
        }

        static StoreProductDto Pack(string id, string shelf, long credits, long gems, int cents, int days = 0,
                                    string kind = "consumable")
            => new StoreProductDto
            {
                id = id, kind = kind, shelf = shelf, credits = credits, gems = gems,
                referenceUsdCents = cents, passDays = days,
            };

        static StoreDto Store(params StoreProductDto[] products) => new StoreDto { products = products };

        // ------------------------------------------------------------ the catalog
        [Test]
        public void TheShippedPassesAreReadAsDailyPieces()
        {
            var catalog = Shipped();

            var gems = catalog.Find(GemPass);
            Assert.IsNotNull(gems, "the gem pass ships");
            Assert.IsTrue(gems.IsDailyPass);
            Assert.AreEqual(7, gems.PassDays);
            Assert.AreEqual(500, gems.Gems, "a pass's amount is one day's");
            Assert.AreEqual(3500, gems.TotalGems);
            Assert.AreEqual(StoreProductKind.Consumable, gems.Kind, "bought again once it is paid out");

            var coins = catalog.Find("gg_coin_pass_7d");
            Assert.IsNotNull(coins, "the coin pass ships");
            Assert.AreEqual(20000, coins.Credits);
            Assert.AreEqual(140000, coins.TotalCredits);
        }

        [Test]
        public void APassIsOnNoLadder()
        {
            var catalog = Shipped();
            var pass = catalog.Find(GemPass);

            Assert.AreEqual(0, pass.Tier, "a pass is not a rung");
            Assert.AreEqual(0, pass.BonusPercent, "and claims no bonus over one");
            Assert.AreNotSame(pass, catalog.GemBase, "nor is it what a gem's price is measured off");

            // The same packs without the passes must imply the same rate, so the deal and bundle
            // seals do not move because a pass went on sale.
            var withPass = StoreCatalog.Resolve(Store(
                Pack("gg_gems_1", "gems", 0, 100, 99), Pack("gg_coins_1", "coins", 7500, 0, 199),
                Pack(GemPass, "gems", 0, 500, 199, 7)), new List<string>());
            var without = StoreCatalog.Resolve(Store(
                Pack("gg_gems_1", "gems", 0, 100, 99), Pack("gg_coins_1", "coins", 7500, 0, 199)),
                new List<string>());

            Assert.AreEqual(without.CreditsPerGem, withPass.CreditsPerGem);
            Assert.AreEqual("gg_gems_1", withPass.GemBase.Id);
            Assert.IsFalse(withPass.Find(GemPass).IsOneTime);
        }

        [Test]
        public void APassAServerWouldRefuseIsDropped()
        {
            var cases = new[]
            {
                Pack("p_one_day", "gems", 0, 500, 199, 1),
                Pack("p_too_long", "gems", 0, 500, 199, StoreLimits.MaxPassDays + 1),
                Pack("p_once", "gems", 0, 500, 199, 7, "nonconsumable"),
                Pack("p_bundle_shelf", "bundles", 0, 500, 199, 7),
                Pack("p_too_big", "gems", 0, StoreLimits.MaxGrant, 199, 7),
            };

            foreach (var dto in cases)
            {
                var problems = new List<string>();
                var catalog = StoreCatalog.Resolve(Store(Pack("gg_gems_1", "gems", 0, 100, 99), dto), problems);
                Assert.IsNull(catalog.Find(dto.id), dto.id + " is dropped");
                Assert.IsTrue(problems.Exists(p => p.Contains(dto.id)), dto.id + " is named");
            }
        }

        [Test]
        public void AGemShortfallIsNeverAnsweredWithAPass()
        {
            var catalog = StoreCatalog.Resolve(Store(Pack("gg_coins_1", "coins", 7500, 0, 199),
                                                     Pack(GemPass, "gems", 0, 500, 199, 7)), new List<string>());
            Assert.IsFalse(catalog.HasGems, "a pass pays its gems over days, so it is no answer to 'I need gems now'");
        }

        // ------------------------------------------------------------ the welcome seal
        [Test]
        public void TheWelcomeBundlesSealIsDerivedFromThePacks()
        {
            var catalog = Shipped();
            var starter = catalog.Find("gg_bundle_starter");
            Assert.IsNotNull(starter);

            // 500 gems at 100 for $0.99 is $4.95; 22,500 coins at 7,500 for $1.99 is $5.97;
            // $10.92 of packs for $2.99 is 365%, floored to ten.
            Assert.AreEqual(360, DealValue.BundlePercent(starter, catalog));
        }

        [Test]
        public void APassesValueSealCountsEveryDayItPays()
        {
            var catalog = Shipped();

            // 3,500 gems at 100 for $0.99 is $34.65 against $1.99: 1741%, floored to ten.
            Assert.AreEqual(1740, DealValue.PassPercent(catalog.Find(GemPass), catalog));
            // 140,000 coins at 7,500 for $1.99 is $37.15 against $1.99: 1866%, floored to ten.
            Assert.AreEqual(1860, DealValue.PassPercent(catalog.Find("gg_coin_pass_7d"), catalog));
            Assert.AreEqual(0, DealValue.PassPercent(catalog.Find("gg_bundle_starter"), catalog), "a bundle is not a pass");
        }

        [Test]
        public void ASealIsNeverPrintedForWhatIsNotABundleBargain()
        {
            var catalog = Shipped();
            Assert.AreEqual(0, DealValue.BundlePercent(catalog.Find("gg_gems_1"), catalog), "the base pack is worth itself");
            Assert.AreEqual(0, DealValue.BundlePercent(catalog.Find(GemPass), catalog), "a pass is not a bundle");
            Assert.AreEqual(0, DealValue.BundlePercent(catalog.Find("gg_heart_vessel_1"), catalog), "a vessel is not currency");
            Assert.AreEqual(0, DealValue.BundlePercent(null, catalog));
        }

        // ------------------------------------------------------------ the ledger
        static CloudWalletState Reply(params CloudPassRow[] rows)
        {
            var state = new CloudWalletState { Currency = "gems", CarriesDailyPasses = true };
            state.DailyPasses.AddRange(rows);
            return state;
        }

        static CloudPassRow Row(string id, int remaining, long day)
            => new CloudPassRow { Id = id, Remaining = remaining, Day = (int)day };

        [Test]
        public void NothingIsKnownUntilAReplyCarriesPasses()
        {
            Assert.IsFalse(DailyPassLedger.Known);

            DailyPassLedger.ApplyServerState(false, new List<CloudPassRow> { Row(GemPass, 6, Day) });
            Assert.IsFalse(DailyPassLedger.Known, "a deployment that predates passes says nothing");
            Assert.IsFalse(DailyPassLedger.Holds(GemPass));

            DailyPassLedger.ApplyServerState(true, new List<CloudPassRow>());
            Assert.IsTrue(DailyPassLedger.Known, "an empty list is an answer");
            Assert.IsFalse(DailyPassLedger.Holds(GemPass));
        }

        [Test]
        public void APassClaimsOncePerUtcDay()
        {
            DailyPassLedger.ApplyServerState(true, new List<CloudPassRow> { Row(GemPass, 6, Day) });

            Assert.IsTrue(DailyPassLedger.Holds(GemPass));
            Assert.IsFalse(DailyPassLedger.CanClaim(GemPass, _clock.Now), "the purchase paid today's piece");
            Assert.IsTrue(DailyPassLedger.CanClaim(GemPass, (Day + 1) * 86400L), "the next UTC day may claim");
            Assert.IsTrue(DailyPassLedger.CanClaim(GemPass, (Day + 5) * 86400L), "and a missed day is never lost");
            Assert.AreEqual(86400L - 3600L, DailyPassLedger.SecondsToNextClaim(_clock.Now));
        }

        [Test]
        public void AReplyReplacesTheMirrorWhole()
        {
            DailyPassLedger.ApplyServerState(true, new List<CloudPassRow> { Row(GemPass, 6, Day) });
            DailyPassLedger.ApplyServerState(true, new List<CloudPassRow>());

            Assert.IsFalse(DailyPassLedger.Holds(GemPass), "a pass missing from the reply is owed nothing");
        }

        [Test]
        public void AClaimAdoptsWhatTheServerAnswered()
        {
            DailyPassLedger.ApplyServerState(true, new List<CloudPassRow> { Row(GemPass, 6, Day - 1) });
            _cloud.Answer = new DailyPassClaim(DailyPassClaimOutcome.Paid, 0L, 500L);
            _cloud.Wallets = new List<CloudWalletState> { Reply(Row(GemPass, 5, Day)) };

            var claim = Run(DailyPassLedger.ClaimAsync(Shipped().Find(GemPass)));

            Assert.AreEqual(DailyPassClaimOutcome.Paid, claim.Outcome);
            Assert.AreEqual(500L, claim.Gems);
            Assert.AreEqual(GemPass, _cloud.Asked, "the claim names the product");
            Assert.AreEqual(5, DailyPassLedger.StandingOf(GemPass).Remaining, "the mirror is the server's answer");
            Assert.IsFalse(DailyPassLedger.CanClaim(GemPass, _clock.Now), "and today is spent");
            Assert.IsFalse(DailyPassLedger.IsClaiming(GemPass), "no answer leaves a claim marked running");
        }

        [Test]
        public void AClaimForAnotherAccountIsNeverAdopted()
        {
            DailyPassLedger.ApplyServerState(true, new List<CloudPassRow> { Row(GemPass, 6, Day - 1) });
            _cloud.Answer = new DailyPassClaim(DailyPassClaimOutcome.Paid, 0L, 500L);
            _cloud.Wallets = new List<CloudWalletState> { Reply(Row(GemPass, 5, Day)) };
            _cloud.SwitchTo = "uid-somebody-else";

            var claim = Run(DailyPassLedger.ClaimAsync(Shipped().Find(GemPass)));

            Assert.AreEqual(DailyPassClaimOutcome.Unavailable, claim.Outcome,
                            "the server paid the account that asked; this one learns on its own sync");
            Assert.AreEqual(6, DailyPassLedger.StandingOf(GemPass).Remaining, "nothing was adopted");
            Assert.IsFalse(DailyPassLedger.IsClaiming(GemPass));
        }

        [Test]
        public void AFailedCallChangesNothing()
        {
            DailyPassLedger.ApplyServerState(true, new List<CloudPassRow> { Row(GemPass, 6, Day - 1) });
            _cloud.Fails = true;

            var claim = Run(DailyPassLedger.ClaimAsync(Shipped().Find(GemPass)));

            Assert.AreEqual(DailyPassClaimOutcome.Unavailable, claim.Outcome);
            Assert.IsTrue(DailyPassLedger.CanClaim(GemPass, _clock.Now), "the card can be tapped again");
            Assert.IsFalse(DailyPassLedger.IsClaiming(GemPass));
        }

        [Test]
        public void OnlyAPassCanBeClaimed()
        {
            var claim = Run(DailyPassLedger.ClaimAsync(Shipped().Find("gg_gems_1")));
            Assert.AreEqual(DailyPassClaimOutcome.Unavailable, claim.Outcome);
            Assert.IsNull(_cloud.Asked, "and nothing is asked of the server");
        }

        // ------------------------------------------------------------ the shop tab's badge
        [Test]
        public void TheShopTabWearsOneMarkAndTheRewardComesFirst()
        {
            Assert.AreEqual(ShopTabMark.Gift, ShopTabBadge.Of(passClaimable: true, dealOffered: true),
                            "a reward waiting outranks a deal");
            Assert.AreEqual(ShopTabMark.Gift, ShopTabBadge.Of(true, false));
            Assert.AreEqual(ShopTabMark.Deal, ShopTabBadge.Of(false, true), "claimed, the deal's alert comes back");
            Assert.AreEqual(ShopTabMark.None, ShopTabBadge.Of(false, false), "and with neither, a bare tab");
        }

        [Test]
        public void AReadyRewardIsOnlyWhatTheShopCanClaim()
        {
            ProgressionRules.Reset();       // the built-in catalog, which carries both passes
            Assert.IsFalse(DailyPassLedger.AnyClaimable(_clock.Now), "nothing until a reply has spoken");

            DailyPassLedger.ApplyServerState(true, new List<CloudPassRow> { Row(GemPass, 6, Day) });
            Assert.IsFalse(DailyPassLedger.AnyClaimable(_clock.Now), "today's piece is already paid");
            Assert.IsTrue(DailyPassLedger.AnyClaimable((Day + 1) * 86400L), "the next UTC day it is ready");

            DailyPassLedger.ApplyServerState(true, new List<CloudPassRow> { Row("gg_not_a_product", 6, Day - 1) });
            Assert.IsFalse(DailyPassLedger.AnyClaimable(_clock.Now),
                           "a pass this catalog does not sell has no card to claim from, so no badge");
        }

        static T Run<T>(Task<T> task)
        {
            Assert.IsTrue(task.Wait(5000), "the claim finished");
            return task.Result;
        }

        // ------------------------------------------------------------ the double
        /// <summary>
        /// A cloud that answers pass claims the way the test says and refuses everything else, so
        /// a call this fixture did not mean to make fails loudly.
        /// </summary>
        sealed class Cloud : ICloudSaveBackend, IDailyPassBackend
        {
            public string Session;
            public DailyPassClaim Answer = new DailyPassClaim(DailyPassClaimOutcome.Unavailable);
            public List<CloudWalletState> Wallets = new List<CloudWalletState>();
            public bool Fails;
            public string SwitchTo;
            public string Asked;

            public bool IsAvailable => true;

            public CloudIdentity CurrentIdentity
                => string.IsNullOrEmpty(Session) ? CloudIdentity.None : new CloudIdentity(Session, true);

            public Task<(CloudResult result, DailyPassClaim claim, List<CloudWalletState> wallets)> ClaimDailyPassAsync(
                string productId, CancellationToken cancellation = default)
            {
                Asked = productId;
                if (SwitchTo != null) { Session = SwitchTo; CloudState.SignIn(SwitchTo); }

                return Task.FromResult(Fails
                    ? (CloudResult.Failed(CloudFailure.Offline, "down"), new DailyPassClaim(DailyPassClaimOutcome.Unavailable),
                       new List<CloudWalletState>())
                    : (CloudResult.Success, Answer, Wallets));
            }

            static Task<(CloudResult, List<CloudWalletState>)> No()
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"), new List<CloudWalletState>()));

            public Task<(CloudResult result, List<CloudWalletState> wallets, CloudRedemption redemption)>
                RedeemPurchaseAsync(string userId, PurchaseReceipt receipt, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"),
                                    new List<CloudWalletState>(), CloudRedemption.Nothing));

            public Task<(CloudResult result, CloudIdentity identity)> SignInAsync(CancellationToken c = default)
                => Task.FromResult((CloudResult.Success, CurrentIdentity));

            public Task<(CloudResult result, CloudIdentity identity)> ResumeAsync(CancellationToken c = default)
                => Task.FromResult((CloudResult.Success, CurrentIdentity));

            public Task<(CloudResult result, CloudIdentity identity)> LinkAsync(
                LinkCredential cr, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"), CloudIdentity.None));

            public Task<(CloudResult result, CloudIdentity identity)> SignInWithCredentialAsync(
                LinkCredential cr, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"), CloudIdentity.None));

            public Task<(CloudResult result, string appleAuthorizationCode)> ReauthenticateAsync(
                LinkCredential credential, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"), string.Empty));

            public Task<CloudResult> DeleteAccountAsync(
                string userId, string appleAuthorizationCode = null, CancellationToken c = default)
                => Task.FromResult(CloudResult.Failed(CloudFailure.Rejected, "not this fixture"));

            public Task<(CloudResult result, CloudSnapshot snapshot)> PullAsync(string userId, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"), CloudSnapshot.Missing));

            public Task<CloudResult> PushAsync(string userId, SaveFileDto snapshot, SaveDelta delta, CancellationToken c = default)
                => Task.FromResult(CloudResult.Failed(CloudFailure.Rejected, "not this fixture"));

            public Task<(CloudResult result, List<CloudWalletState> wallets)> ReadWalletAsync(
                string userId, CancellationToken c = default) => No();

            public Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitSpendsAsync(
                string userId, IReadOnlyList<SpendSubmission> spends, CancellationToken c = default) => No();

            public Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitAwardsAsync(
                string userId, IReadOnlyList<GrantEntryDto> awards, CancellationToken c = default) => No();

            public Task<(CloudResult result, Release.ReleaseRequirement requirement)> ReadReleaseAsync(
                string platform, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "not this fixture"),
                                    Release.ReleaseRequirement.None));
        }
    }
}
