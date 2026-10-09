using System.Collections.Generic;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Store;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Limited-time shop deals (invariant 60), the client half.
    ///
    /// <para>
    /// <b>The two ids are a wire contract</b> with <c>firebase/functions/src/deals.ts</c> (held there
    /// by <c>test/deals.mjs</c>): the server pays the coins under exactly the claim id this device
    /// queued, so a drift is coins counted twice or never confirmed. <b>The reader mirrors the
    /// server's</b>, so a row the server would refuse to price is never offered. <b>The ledger</b>
    /// offers nothing until the server has said what this account owns, offers the live deal
    /// closing soonest, debits the gems and queues the coins under the derived ids, refuses a
    /// second purchase, and takes the coins back when the server refuses the gems.
    /// </para>
    /// </summary>
    public sealed class ShopDealTests
    {
        sealed class FixedClock : IGameClock
        {
            public long Now;
            public long UtcNowUnix => Now;
            public bool IsTrusted => false;
        }

        const long Now = 1_791_000_000L;
        const string Id = "d202610091530k3x9";
        const string Other = "d202610091600aaaa";

        FixedClock _clock;

        [SetUp]
        public void Start()
        {
            _clock = new FixedClock { Now = Now };
            GameClock.Set(_clock);
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
            ProgressionStore.LoadFrom(new SaveFileDto());
            Wallet.LoadFrom(new SaveFileDto());
            PlayerProgression.Invalidate();
            DealLedger.ResetForTests();
        }

        [TearDown]
        public void Restore()
        {
            DealLedger.ResetForTests();
            GameClock.Set(new DeviceClock());
            ProgressionRules.Reset();
            Wallet.LoadFrom(new SaveFileDto());
            PlayerProgression.Invalidate();
        }

        static Dictionary<string, object> Row(string id, long credits, long gems, long start, long end)
            => new Dictionary<string, object>
            {
                ["id"] = id, ["credits"] = credits, ["gems"] = gems, ["startUnix"] = start, ["endUnix"] = end,
            };

        static Dictionary<string, object> Doc(params object[] rows)
            => new Dictionary<string, object> { ["schema"] = 1L, ["deals"] = new List<object>(rows) };

        static void Gems(long gems) => Wallet.Ledger(Currency.Gems).GrantLocally(gems);

        // ------------------------------------------------------------ the ids
        [Test]
        public void TheIdsAreTheServersSpelling()
        {
            Assert.AreEqual("deal:" + Id, SpendEntry.ShopDealId(Id));
            Assert.AreEqual("deal:" + Id + ":credits", GrantEntry.ShopDealId(Id));
            Assert.AreEqual(Id, SpendEntry.DealOfShopDealId(SpendEntry.ShopDealId(Id)));
            Assert.IsNull(SpendEntry.DealOfShopDealId(GrantEntry.ShopDealId(Id)), "a grant id is not a debit");
            Assert.IsNull(SpendEntry.DealOfShopDealId("pass:watch_0001"));
            Assert.IsNull(SpendEntry.DealOfShopDealId("deal:"));
            Assert.LessOrEqual(GrantEntry.ShopDealId(Id).Length, 64, "the server's id bound");
        }

        [Test]
        public void ADealIdIsWhatTheServerMints()
        {
            Assert.IsTrue(ShopDeals.IsId(Id));
            Assert.IsFalse(ShopDeals.IsId("D202610091530k3x9"));
            Assert.IsFalse(ShopDeals.IsId("d20261009153k3x9"));
            Assert.IsFalse(ShopDeals.IsId("d202610091530K3X9"));
            Assert.IsFalse(ShopDeals.IsId("d202610091530k3x9x"));
            Assert.IsFalse(ShopDeals.IsId(null));
        }

        // ------------------------------------------------------------ the reader
        [Test]
        public void TheReaderTakesSoundRowsAndDropsTheRest()
        {
            var deals = ShopDeals.Read(Doc(
                Row(Id, 26000, 900, Now - 60, Now + 3600),
                Row("bad", 26000, 900, Now - 60, Now + 3600),
                Row(Other, 0, 900, Now - 60, Now + 3600),
                Row(Other, 26000, 900, Now + 60, Now + 60),
                Row(Other, 26000, ShopDeals.MaxGems + 1, Now - 60, Now + 3600),
                new Dictionary<string, object> { ["id"] = Other, ["credits"] = 1.5, ["gems"] = 1L,
                                                 ["startUnix"] = Now, ["endUnix"] = Now + 1 },
                Row(Id, 1, 1, Now - 60, Now + 3600)));

            Assert.AreEqual(1, deals.Count, "one sound row; a duplicate id is read once");
            Assert.AreEqual(26000L, deals[0].Credits);
            Assert.AreEqual(900L, deals[0].Gems);

            // Firestore hands a whole number back as a double when it was written as one.
            var asDouble = ShopDeals.Read(Doc(new Dictionary<string, object>
            {
                ["id"] = Id, ["credits"] = 26000d, ["gems"] = 900d, ["startUnix"] = (double)Now, ["endUnix"] = (double)(Now + 10),
            }));
            Assert.AreEqual(1, asDouble.Count);

            Assert.AreEqual(0, ShopDeals.Read(new Dictionary<string, object>
                { ["schema"] = 2L, ["deals"] = new List<object> { Row(Id, 1, 1, Now, Now + 1) } }).Count,
                "another schema lists nothing");
            Assert.AreEqual(0, ShopDeals.Read(null).Count);
        }

        // ------------------------------------------------------------ offering
        [Test]
        public void NothingIsOfferedUntilTheServerHasSaidWhatIsOwned()
        {
            DealLedger.Adopt(ShopDeals.Read(Doc(Row(Id, 26000, 900, Now - 60, Now + 3600))));
            Assert.IsNull(DealLedger.Offered, "a reinstall must not sell a deal twice");

            DealLedger.ApplyServerState(carried: false, new string[0]);
            Assert.IsNull(DealLedger.Offered, "a deployment that predates deals says nothing");

            DealLedger.ApplyServerState(carried: true, new string[0]);
            Assert.AreEqual(Id, DealLedger.Offered?.Id);

            DealLedger.ApplyServerState(carried: true, new[] { Id });
            Assert.IsNull(DealLedger.Offered, "bought on another phone");
        }

        [Test]
        public void TheLiveDealClosingSoonestIsOffered()
        {
            DealLedger.ApplyServerState(carried: true, new string[0]);
            DealLedger.Adopt(ShopDeals.Read(Doc(
                Row(Other, 9000, 300, Now - 60, Now + 7200),
                Row(Id, 26000, 900, Now - 60, Now + 3600))));
            Assert.AreEqual(Id, DealLedger.Offered.Id);

            _clock.Now = Now + 3600;
            Assert.AreEqual(Other, DealLedger.Offered.Id, "the first ran out on the clock, with no fetch");

            _clock.Now = Now + 7200;
            Assert.IsNull(DealLedger.Offered);
        }

        // ------------------------------------------------------------ buying
        [Test]
        public void BuyingDebitsTheGemsAndQueuesTheCoinsUnderTheDerivedIds()
        {
            DealLedger.ApplyServerState(carried: true, new string[0]);
            DealLedger.Adopt(ShopDeals.Read(Doc(Row(Id, 26000, 900, Now - 60, Now + 3600))));
            Gems(1000);
            PlayerProgression.Invalidate();

            long gems = PlayerProgression.Gems, credits = PlayerProgression.Credits;
            var deal = DealLedger.Offered;

            Assert.AreEqual(DealBuy.Bought, DealLedger.TryBuy(deal));
            Assert.AreEqual(gems - 900L, PlayerProgression.Gems);
            Assert.AreEqual(credits + 26000L, PlayerProgression.Credits, "the coins are spendable at once");
            Assert.IsTrue(Wallet.Ledger(Currency.Gems).HasPending(SpendEntry.ShopDealId(Id)));
            Assert.IsTrue(Wallet.Ledger(Currency.Credits).HasGranted(GrantEntry.ShopDealId(Id)));

            Assert.IsTrue(DealLedger.IsBought(Id));
            Assert.IsNull(DealLedger.Offered, "a bought deal leaves the shop");
            Assert.AreEqual(DealBuy.AlreadyBought, DealLedger.TryBuy(deal), "and is sold once");
            Assert.AreEqual(gems - 900L, PlayerProgression.Gems, "charged once");
        }

        [Test]
        public void AShortBalanceOrAClosedDealBuysNothing()
        {
            DealLedger.ApplyServerState(carried: true, new string[0]);
            DealLedger.Adopt(ShopDeals.Read(Doc(Row(Id, 26000, 900, Now - 60, Now + 3600))));
            var deal = DealLedger.Offered;

            // One short, whatever the fresh wallet's seed is.
            PlayerProgression.Invalidate();
            Gems(899L - PlayerProgression.Gems);
            PlayerProgression.Invalidate();
            Assert.AreEqual(899L, PlayerProgression.Gems);
            long credits = PlayerProgression.Credits;
            Assert.AreEqual(DealBuy.TooPoor, DealLedger.TryBuy(deal));
            Assert.AreEqual(credits, PlayerProgression.Credits, "no coins without the gems");
            Assert.IsFalse(Wallet.Ledger(Currency.Credits).HasGranted(GrantEntry.ShopDealId(Id)));

            Gems(1);
            _clock.Now = Now + 3600;
            Assert.AreEqual(DealBuy.Ended, DealLedger.TryBuy(deal));
            Assert.AreEqual(DealBuy.Ended, DealLedger.TryBuy(null));
        }

        [Test]
        public void ARefusedDebitTakesTheCoinsBack()
        {
            DealLedger.ApplyServerState(carried: true, new string[0]);
            DealLedger.Adopt(ShopDeals.Read(Doc(Row(Id, 26000, 900, Now - 60, Now + 3600))));
            Gems(1000);
            PlayerProgression.Invalidate();
            long gems = PlayerProgression.Gems, credits = PlayerProgression.Credits;

            Assert.AreEqual(DealBuy.Bought, DealLedger.TryBuy(DealLedger.Offered));

            // The server's reply: the debit refused. The ledger drops the gems and announces it,
            // and the deal ledger takes the coins with them.
            var ledger = Wallet.Ledger(Currency.Gems);
            ledger.ApplyServerState(ledger.GrantedBaseline, ledger.SpentBaseline, new List<string>(), 0L,
                                    rejectedSpendIds: new List<string> { SpendEntry.ShopDealId(Id) });
            PlayerProgression.Invalidate();

            Assert.AreEqual(gems, PlayerProgression.Gems, "the gems are back");
            Assert.AreEqual(credits, PlayerProgression.Credits, "and the coins are gone");
            Assert.IsFalse(DealLedger.IsBought(Id));
            Assert.AreEqual(Id, DealLedger.Offered?.Id, "so the deal is on offer again while it runs");
        }

        [Test]
        public void ARefusalOfAnythingElseTouchesNoDeal()
        {
            Wallet.Ledger(Currency.Credits).TryAward(GrantEntry.ShopDealId(Id), 26000L, Now, SpendEntry.ShopDealReason, out _);
            DealLedger.OnSpendRejected(Currency.Gems, SpendEntry.SeasonPassId("watch_0001"));
            DealLedger.OnSpendRejected(Currency.Gems, GrantEntry.ShopDealId(Id));
            Assert.IsTrue(Wallet.Ledger(Currency.Credits).HasGranted(GrantEntry.ShopDealId(Id)));
        }

        [Test]
        public void WithdrawingAGrantRemovesOnlyThatPendingAward()
        {
            var ledger = new CurrencyLedger(Currency.Credits);
            ledger.TryAward("a", 10L, Now, "r", out _);
            ledger.TryAward("b", 20L, Now, "r", out _);

            Assert.IsTrue(ledger.WithdrawGrant("a"));
            Assert.IsFalse(ledger.WithdrawGrant("a"), "already gone");
            Assert.IsFalse(ledger.WithdrawGrant("missing"));
            Assert.AreEqual(20L, ledger.PendingGrant);
        }
    }
}
