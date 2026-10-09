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

        // ------------------------------------------------------------ several at once
        [Test]
        public void EveryOfferedDealIsListedClosingSoonestFirst()
        {
            DealLedger.ApplyServerState(carried: true, new string[0]);
            DealLedger.Adopt(ShopDeals.Read(Doc(
                Row(Other, 9000, 300, Now - 60, Now + 7200),
                Row(Id, 26000, 900, Now - 60, Now + 3600),
                Row("d202610091400bbbb", 1000, 10, Now - 60, Now + 3600),
                Row("d202610091400cccc", 1000, 10, Now - 7200, Now - 60))));

            var offered = DealLedger.OfferedAllAt(Now);
            CollectionAssert.AreEqual(new[] { "d202610091400bbbb", Id, Other },
                                      offered.ConvertAll(d => d.Id), "ended ones left out; a tie broken by id");

            DealLedger.ApplyServerState(carried: true, new[] { Id });
            CollectionAssert.AreEqual(new[] { "d202610091400bbbb", Other },
                                      DealLedger.OfferedAllAt(Now).ConvertAll(d => d.Id), "a bought one leaves");
        }

        [Test]
        public void ARowReadsTheLatestCopyOfItsDeal()
        {
            DealLedger.ApplyServerState(carried: true, new string[0]);
            DealLedger.Adopt(ShopDeals.Read(Doc(Row(Id, 26000, 900, Now - 60, Now + 3600))));
            var handed = DealLedger.Offered;

            // Ended from the admin page: the next read publishes an end of now.
            DealLedger.Adopt(ShopDeals.Read(Doc(Row(Id, 26000, 900, Now - 60, Now))));
            Assert.IsTrue(handed.IsLive(Now), "the copy a panel was handed still thinks it is on sale");
            Assert.IsFalse(DealLedger.Find(Id).IsLive(Now), "the latest copy knows it ended");
            Assert.IsNull(DealLedger.Offered, "and it is no longer offered");
            Assert.AreEqual(DealBuy.Ended, DealLedger.TryBuy(DealLedger.Find(Id)), "a buy of the latest copy is refused here, before any charge");
            Assert.IsNull(DealLedger.Find("d202601010000zzzz"));
        }

        [Test]
        public void TheCheckBeforeABuyAnswersFalseWithNobodyToAsk()
        {
            var check = DealLedger.RefreshNowAsync();
            Assert.IsTrue(check.IsCompleted, "never left waiting");
            Assert.IsFalse(check.Result, "no backend is a failed read, said as needing a connection");
        }

        // ------------------------------------------------------------ shown once
        [Test]
        public void ADealShownIsRememberedAndTheMergeIsAJoin()
        {
            DealSeen.Reset();
            DealSeen.Mark(new[] { Id, "not an id", null });
            Assert.IsTrue(DealSeen.Has(Id));
            Assert.AreEqual(1, DealSeen.Count, "junk is never recorded");
            CollectionAssert.AreEqual(new[] { Id }, DealSeen.Write());

            var a = new[] { "d202601010000aaaa", Id };
            var b = new[] { Other };
            var c = new[] { "d202512310000aaaa" };
            CollectionAssert.AreEqual(DealSeen.Join(a, b), DealSeen.Join(b, a), "commutative");
            CollectionAssert.AreEqual(DealSeen.Join(DealSeen.Join(a, b), c), DealSeen.Join(a, DealSeen.Join(b, c)), "associative");
            CollectionAssert.AreEqual(DealSeen.Join(a, a), DealSeen.Join(a, DealSeen.Join(a, a)), "idempotent");
            CollectionAssert.AreEqual(new[] { "d202512310000aaaa", "d202601010000aaaa", Id, Other },
                                      DealSeen.Join(DealSeen.Join(a, b), c), "sorted, so SaveDelta can walk it");
            DealSeen.Reset();
        }

        [Test]
        public void TheRememberedSetKeepsTheNewestAndReadsBackWhatItWrote()
        {
            var many = new List<string>();
            for (int i = 0; i < DealSeen.MaxIds + 10; i++) many.Add($"d2026{(i / 60 + 1):00}01{(i % 24):00}{(i % 60):00}aaaa");
            var joined = DealSeen.Join(many.ToArray(), null);
            Assert.AreEqual(DealSeen.MaxIds, joined.Length, "bounded by the rules' cap");
            many.Sort(System.StringComparer.Ordinal);
            Assert.AreEqual(many[many.Count - 1], joined[joined.Length - 1], "the newest kept");
            Assert.AreEqual(many[10], joined[0], "the oldest dropped");

            DealSeen.LoadFrom(joined);
            CollectionAssert.AreEqual(joined, DealSeen.Write(), "a round trip moves nothing (11f)");
            DealSeen.Reset();
        }

        // ------------------------------------------------------------ when
        static List<ShopDeal> Live(long secondsLeft) => new List<ShopDeal> { new ShopDeal(Id, 1, 1, Now - 60, Now + secondsLeft) };

        [Test]
        public void ANewPlayerIsNeverPrompted()
        {
            Assert.AreEqual(DealTrigger.None, DealPrompt.Choose(2, false, true, true, true, true, Live(60), Now));
            Assert.AreEqual(DealTrigger.Shortfall, DealPrompt.Choose(3, false, true, true, true, true, Live(60), Now));
        }

        [Test]
        public void OnePopupASessionAndOnlyWithSomethingUnseen()
        {
            Assert.AreEqual(DealTrigger.None, DealPrompt.Choose(9, true, true, true, true, true, Live(60), Now), "one a session");
            Assert.AreEqual(DealTrigger.None, DealPrompt.Choose(9, false, true, true, true, true, new List<ShopDeal>(), Now));

            var offered = Live(9999);
            Assert.AreEqual(0, DealPrompt.Unseen(offered, id => id == Id).Count, "a shown deal is never shown again");
            Assert.AreEqual(1, DealPrompt.Unseen(offered, id => false).Count);
        }

        [Test]
        public void TheTriggersAreAskedInOrderAndWhereTheyBelong()
        {
            var day = Live(86400);
            Assert.AreEqual(DealTrigger.Shortfall, DealPrompt.Choose(9, false, true, true, false, true, day, Now),
                            "a shortfall is answered on the screen it happened on");
            Assert.AreEqual(DealTrigger.None, DealPrompt.Choose(9, false, false, true, false, true, day, Now),
                            "a win waits for the hub");
            Assert.AreEqual(DealTrigger.Win, DealPrompt.Choose(9, false, false, true, true, true, day, Now));
            Assert.AreEqual(DealTrigger.None, DealPrompt.Choose(9, false, false, false, true, true, day, Now),
                            "a calm hub alone is not a moment");
            Assert.AreEqual(DealTrigger.LastHours, DealPrompt.Choose(9, false, false, false, true, true,
                                                                     Live(DealPrompt.LastHoursSeconds), Now));
            Assert.AreEqual(DealTrigger.None, DealPrompt.Choose(9, false, false, false, false, true,
                                                                Live(60), Now), "the last hours wait for the hub");
            Assert.AreEqual("shortfall", DealPrompt.Id(DealTrigger.Shortfall));
            Assert.AreEqual("win", DealPrompt.Id(DealTrigger.Win));
            Assert.AreEqual("last_hours", DealPrompt.Id(DealTrigger.LastHours));
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
