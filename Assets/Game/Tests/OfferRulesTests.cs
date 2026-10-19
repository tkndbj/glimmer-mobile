using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Offers;
using GlimmerGrove.Store;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Which personal offer answers which moment for which player (invariant 64), held on the built-in
    /// tuning: every pacing gate in its order, every shipped offer reaching the player it was written
    /// for, and the holdout staying the share it says.
    /// </summary>
    public sealed class OfferRulesTests
    {
        const long Now = 1_800_000_000L;          // a fixed moment, well after any epoch edge
        const string Account = "acct_rules_fixture";

        /// <summary>A store that can sell every offer product, at the built-in catalog's coins.</summary>
        internal static readonly IOfferShelf Everything = new Shelf(null);

        sealed class Shelf : IOfferShelf
        {
            readonly HashSet<string> _unsold;
            public Shelf(HashSet<string> unsold) { _unsold = unsold; }
            public bool CanSell(string productId)
                => StoreCatalog.Default.Find(productId)?.Shelf == StoreShelf.Offers
                && (_unsold == null || !_unsold.Contains(productId));
            public long CreditsOf(string productId) => StoreCatalog.Default.Find(productId)?.Credits ?? 0L;
        }

        static OfferTuning Tuning => OfferTuning.Shipped;

        /// <summary>An account that falls outside the holdout, found once so every test deals.</summary>
        static string Treated
        {
            get
            {
                for (int i = 0; ; i++)
                {
                    string id = Account + i;
                    if (!OfferRules.InHoldout(id, Tuning.HoldoutPercent)) return id;
                }
            }
        }

        static OfferProfile Player(int days = 10, int active = 5, int runs = 12, int loss = 30, int streak = 0,
                                   int purchases = 0, long spend = 0, long top = 0, long sincePurchase = OfferProfile.Never,
                                   int away = 0, int dismissals = 0, long gap = 0, int cleared = 30, int ads = 0)
            => new OfferProfile()
                .Set(OfferMeasure.DaysPlaying, days).Set(OfferMeasure.ActiveDays7, active)
                .Set(OfferMeasure.Runs, runs).Set(OfferMeasure.LossPercent, loss).Set(OfferMeasure.LossStreak, streak)
                .Set(OfferMeasure.Purchases, purchases).Set(OfferMeasure.SpendCents, spend).Set(OfferMeasure.TopCents, top)
                .Set(OfferMeasure.DaysSincePurchase, sincePurchase).Set(OfferMeasure.DaysAway, away)
                .Set(OfferMeasure.Dismissals, dismissals).Set(OfferMeasure.CoinGap, gap)
                .Set(OfferMeasure.ClearedLevels, cleared).Set(OfferMeasure.AdViews7, ads);

        static OfferDeal Deal(OfferProfile who, OfferTrigger moment, OfferHistory history = null, IOfferShelf shelf = null,
                              string account = null, long now = Now)
            => OfferRules.Choose(Tuning, who, moment, history ?? new OfferHistory(null), shelf ?? Everything,
                                 account ?? Treated, now);

        static void Dealt(OfferDeal deal, string offer, string product)
        {
            Assert.IsTrue(deal.Dealt, $"nothing was dealt ({deal.Refusal}); expected {offer}");
            Assert.AreEqual(offer, deal.Offer.Id);
            Assert.AreEqual(product, deal.ProductId);
        }

        // ------------------------------------------------------------------ the holdout
        [Test]
        public void TheHoldoutIsStableAndTheShareItSays()
        {
            int held = 0;
            for (int i = 0; i < 20000; i++)
            {
                string id = "account_" + i;
                bool a = OfferRules.InHoldout(id, 10);
                Assert.AreEqual(a, OfferRules.InHoldout(id, 10), "the arm moved between two asks");
                if (a) held++;
            }
            Assert.That(held, Is.InRange(1700, 2300), $"{held} of 20000 held out at 10%");
            Assert.IsFalse(OfferRules.InHoldout("anyone", 0));
        }

        [Test]
        public void AHeldOutAccountIsNeverDealt()
        {
            string held = null;
            for (int i = 0; held == null; i++)
                if (OfferRules.InHoldout("held_" + i, Tuning.HoldoutPercent)) held = "held_" + i;
            Assert.AreEqual(OfferRefusal.Holdout, Deal(Player(days: 0, active: 1), OfferTrigger.Session, account: held).Refusal);
        }

        // ------------------------------------------------------------------ the pacing, in order
        [Test]
        public void NoAccountDealsNothing()
            => Assert.AreEqual(OfferRefusal.NoAccount, Deal(Player(), OfferTrigger.Win, account: string.Empty).Refusal);

        [Test]
        public void TheFirstLevelsAreTheGames()
            => Assert.AreEqual(OfferRefusal.TooEarly, Deal(Player(days: 0, cleared: 2), OfferTrigger.Session).Refusal);

        [Test]
        public void OneOfferStandsAtATime()
        {
            var history = new OfferHistory(null);
            history.NoteShown("starter", "gg_offer_1", "starter", Now - 60, Now + 3600);
            Assert.AreEqual(OfferRefusal.Standing, Deal(Player(days: 0), OfferTrigger.Session, history).Refusal);

            // Run out, it stands no more: in the same session the session's count says no, and in the
            // next one the gap after it does.
            Assert.AreEqual(OfferRefusal.SessionFull, Deal(Player(days: 0), OfferTrigger.Session, history, now: Now + 3601).Refusal);
            history.BeginSession(Now + 3601);
            Assert.AreEqual(OfferRefusal.TooSoon, Deal(Player(days: 0), OfferTrigger.Session, history, now: Now + 3601).Refusal);
        }

        [Test]
        public void TheDayAndTheSessionAreCounted()
        {
            var history = new OfferHistory(null);
            history.BeginSession(Now - 20000);
            history.NoteShown("a", "gg_offer_1", "special", Now - 20000, Now - 19000);
            history.Expire(Now);
            Assert.AreEqual(OfferRefusal.SessionFull, Deal(Player(), OfferTrigger.Win, history).Refusal);

            history.BeginSession(Now - 10000);
            history.NoteShown("b", "gg_offer_1", "special", Now - 10000, Now - 9000);
            history.Expire(Now);
            history.BeginSession(Now);
            Assert.AreEqual(OfferRefusal.DayFull, Deal(Player(), OfferTrigger.Win, history).Refusal);
        }

        [Test]
        public void AnyMoneyPopupPacesTheNext()
        {
            var history = new OfferHistory(null);
            history.NotePopup(Now - 60);
            Assert.AreEqual(OfferRefusal.TooSoon, Deal(Player(days: 0), OfferTrigger.Session, history).Refusal);
            Assert.IsTrue(Deal(Player(days: 0), OfferTrigger.Session, history, now: Now + Tuning.GapMinutes * 60L).Dealt);
        }

        [Test]
        public void APurchaseQuietensEverything()
        {
            var history = new OfferHistory(null);
            history.NoteBought("gg_gems_1", Now - 3600);
            Assert.AreEqual(OfferRefusal.JustBought, Deal(Player(purchases: 1, top: 99), OfferTrigger.Win, history).Refusal);
        }

        [Test]
        public void EveryCloseInARowWaitsLonger()
        {
            var history = new OfferHistory(null);
            for (int i = 0; i < 3; i++) history.NoteDismissed(Now - 3600);

            Assert.AreEqual(OfferRefusal.BackingOff, Deal(Player(purchases: 1, top: 149, dismissals: 3), OfferTrigger.Win, history).Refusal);
            Assert.AreEqual(Tuning.BackoffHours[2] * 3600L, Tuning.BackoffSeconds(3));
            Assert.AreEqual(Tuning.BackoffHours[2] * 3600L, Tuning.BackoffSeconds(9), "a longer run waits the last entry");
            Assert.AreEqual(0L, Tuning.BackoffSeconds(0));
        }

        // ------------------------------------------------------------------ who gets what
        [Test]
        public void ANewPlayerIsDealtTheStarterOnceEver()
        {
            var history = new OfferHistory(null);
            Dealt(Deal(Player(days: 0, active: 1), OfferTrigger.Session, history), "starter", "gg_offer_1");

            history.NoteShown("starter", "gg_offer_1", "starter", Now, Now + 60);
            var later = Deal(Player(days: 1, active: 2), OfferTrigger.Session, history, now: Now + 10 * 86400L);
            Assert.IsFalse(later.Dealt && later.Offer.Id == "starter", "the starter came back");
        }

        [Test]
        public void AnEngagedPlayerWhoNeverPaidIsDealtAFirstPurchase()
            => Dealt(Deal(Player(days: 6, active: 5), OfferTrigger.Win), "first_buy", "gg_offer_1");

        [Test]
        public void ANearMissHelpsAStrugglingPlayer()
            => Dealt(Deal(Player(loss: 60), OfferTrigger.NearMiss), "near_miss", "gg_offer_2");

        [Test]
        public void ALossHelpsAStuckPlayer()
            => Dealt(Deal(Player(streak: 3), OfferTrigger.Loss), "stuck_help", "gg_offer_2");

        [Test]
        public void ACruisingPlayerIsNotOfferedRescue()
            => Assert.IsFalse(Deal(Player(loss: 10), OfferTrigger.NearMiss).Dealt);

        [Test]
        public void AShortfallIsAnsweredWithTheCheapestOfferThatCoversIt()
        {
            Dealt(Deal(Player(gap: 15000), OfferTrigger.Shortfall), "coin_gap_free", "gg_offer_3");
            Dealt(Deal(Player(gap: 8000), OfferTrigger.Shortfall), "coin_gap_free", "gg_offer_2");

            // More than a never-paid player's ladder reaches: nothing, rather than a $40 first purchase.
            Assert.AreEqual(OfferRefusal.NothingFits, Deal(Player(gap: 70000), OfferTrigger.Shortfall).Refusal);

            // A payer's ladder reaches further.
            Dealt(Deal(Player(gap: 70000, purchases: 2, top: 799, spend: 1000), OfferTrigger.Shortfall), "coin_gap_payer", "gg_offer_5");
        }

        [Test]
        public void AShortfallWithNoGapIsNoShortfall()
            => Assert.AreEqual(OfferRefusal.NothingFits, Deal(Player(gap: 0), OfferTrigger.Shortfall).Refusal);

        [Test]
        public void APayerIsOfferedOneStepAboveTheirLargestPurchase()
        {
            Dealt(Deal(Player(purchases: 1, top: 99, spend: 99), OfferTrigger.Win), "step_entry", "gg_offer_3");
            Dealt(Deal(Player(purchases: 1, top: 399, spend: 399), OfferTrigger.Win), "step_entry", "gg_offer_3");
            Dealt(Deal(Player(purchases: 2, top: 799, spend: 1200), OfferTrigger.Win), "step_mid", "gg_offer_4");
            Dealt(Deal(Player(purchases: 2, top: 1499, spend: 2000), OfferTrigger.Win), "step_high", "gg_offer_5");
            Dealt(Deal(Player(purchases: 5, top: 1499, spend: 6000), OfferTrigger.Win), "step_big", "gg_offer_6");
            Dealt(Deal(Player(purchases: 3, top: 3999, spend: 6000), OfferTrigger.Win), "step_whale", "gg_offer_7");
        }

        [Test]
        public void APayerWhoKeepsSayingNoIsOfferedLess()
        {
            var history = new OfferHistory(null);
            for (int i = 0; i < 3; i++) history.NoteDismissed(Now - 30L * 86400L);
            Dealt(Deal(Player(purchases: 1, top: 799, spend: 799, dismissals: 3), OfferTrigger.Win, history), "step_down", "gg_offer_2");
        }

        [Test]
        public void AReturningPlayerIsWelcomedBack()
        {
            Dealt(Deal(Player(away: 5), OfferTrigger.Session), "comeback", "gg_offer_2");
            Dealt(Deal(Player(away: 5, purchases: 1, top: 399, spend: 399, sincePurchase: 30), OfferTrigger.Session),
                  "comeback_payer", "gg_offer_3");
        }

        [Test]
        public void AProductTheStoreCannotSellIsNeverDealt()
        {
            var shelf = new Shelf(new HashSet<string> { "gg_offer_3" });
            Dealt(Deal(Player(gap: 15000), OfferTrigger.Shortfall, shelf: shelf), "coin_gap_free", "gg_offer_4");
            Assert.AreEqual(OfferRefusal.NothingFits,
                            Deal(Player(purchases: 1, top: 99, spend: 99), OfferTrigger.Win, shelf: shelf).Refusal);
        }

        [Test]
        public void AnOfferOnItsCooldownWaits()
        {
            var history = new OfferHistory(null);
            history.NoteShown("first_buy", "gg_offer_1", "value", Now - 3L * 86400L, Now - 3L * 86400L + 60);
            history.Expire(Now);
            history.BeginSession(Now);
            Assert.IsFalse(Deal(Player(days: 6, active: 5), OfferTrigger.Win, history).Dealt, "first_buy came back inside its week");
            history.BeginSession(Now + 5L * 86400L);
            Dealt(Deal(Player(days: 6, active: 5), OfferTrigger.Win, history, now: Now + 5L * 86400L), "first_buy", "gg_offer_1");
        }

        [Test]
        public void TheSameAnswerAllDay()
        {
            var a = Deal(Player(purchases: 1, top: 99, spend: 99), OfferTrigger.Win);
            var b = Deal(Player(purchases: 1, top: 99, spend: 99), OfferTrigger.Win, now: Now + 3600);
            Assert.AreEqual(a.Offer?.Id, b.Offer?.Id);
        }

        // ------------------------------------------------------------------ the history
        [Test]
        public void LossesInARowAreCountedOnOneLevel()
        {
            var history = new OfferHistory(null);
            history.NoteRun("s01_a", false);
            history.NoteRun("s01_a", false);
            Assert.AreEqual(2, history.LossStreak);
            history.NoteRun("s01_b", false);
            Assert.AreEqual(1, history.LossStreak, "a loss on another level starts a new streak");
            history.NoteRun("s01_b", true);
            Assert.AreEqual(0, history.LossStreak);
            Assert.AreEqual(75, history.LossPercent);
        }

        [Test]
        public void TheWindowsAreBounded()
        {
            var history = new OfferHistory(null);
            for (int i = 0; i < 100; i++) history.NoteRun("x", i % 2 == 0);
            Assert.AreEqual(OfferHistory.RunWindow, history.Runs);

            for (int i = 0; i < 500; i++) history.NoteAdView(Now - 100 + i % 50);
            Assert.LessOrEqual(history.AdViews7(Now), OfferHistory.MostAds);
            Assert.AreEqual(0, history.AdViews7(Now + OfferHistory.AdWindowSeconds + 200));

            for (int i = 0; i < 200; i++) history.NoteShown("o" + i, "gg_offer_1", "special", Now + i, Now + i + 1);
            Assert.LessOrEqual(history.Log.marks.Count, OfferHistory.MostMarks);
        }

        [Test]
        public void ABoughtOfferStopsStandingAndEndsTheRunOfCloses()
        {
            var history = new OfferHistory(null);
            history.NoteShown("near_miss", "gg_offer_2", "rescue", Now, Now + 3600);
            history.NoteDismissed(Now);
            Assert.IsNull(history.NoteBought("gg_gems_1", Now + 10), "another product is not this offer");
            Assert.AreEqual(0, history.Dismissals, "any purchase ends a run of closes");
            Assert.IsNotNull(history.Active);

            Assert.AreEqual("near_miss", history.NoteBought("gg_offer_2", Now + 20));
            Assert.IsNull(history.Active);
            Assert.AreEqual(1, history.BuysOf("near_miss"));
        }

        [Test]
        public void DaysAwayAreCountedAtTheSessionStart()
        {
            var history = new OfferHistory(null);
            history.BeginSession(Now);
            Assert.AreEqual(0, history.AwayDays);
            history.BeginSession(Now + 4L * 86400L);
            Assert.AreEqual(4, history.AwayDays);
            Assert.AreEqual(2, history.ActiveDays7(Now + 4L * 86400L));
            Assert.AreEqual(4, history.DaysPlaying(Now + 4L * 86400L));
        }

        // ------------------------------------------------------------------ the server's half
        [Test]
        public void ThePayerFactsOnlyClimbAndWaitForAReply()
        {
            PayerLedger.ResetForTests();
            Assert.IsFalse(PayerLedger.Known);

            PayerLedger.ApplyServerState(false, 3, 300, 100, 5);
            Assert.IsFalse(PayerLedger.Known, "a reply that did not carry the fields said nothing");

            PayerLedger.ApplyServerState(true, 2, 500, 399, Now);
            PayerLedger.ApplyServerState(true, 1, 99, 99, Now - 100);
            Assert.AreEqual(2, PayerLedger.Purchases);
            Assert.AreEqual(500, PayerLedger.SpendCents);
            Assert.AreEqual(399, PayerLedger.TopCents);
            Assert.AreEqual(Now, PayerLedger.LastUnix);
            PayerLedger.ResetForTests();
        }

        // ------------------------------------------------------------------ the seal
        [Test]
        public void TheSealIsTheShopsOwnArithmetic()
        {
            // The shipped shop's base packs: 100 gems for 99 cents, 7,500 coins for 199.
            var dto = new StoreDto
            {
                products = new[]
                {
                    new StoreProductDto { id = "gg_gems_1", kind = "consumable", shelf = "gems", gems = 100, referenceUsdCents = 99 },
                    new StoreProductDto { id = "gg_coins_1", kind = "consumable", shelf = "coins", credits = 7500, referenceUsdCents = 199 },
                    new StoreProductDto { id = "gg_offer_1", kind = "consumable", shelf = "offers", credits = 10000, gems = 300, referenceUsdCents = 99 },
                    new StoreProductDto { id = "gg_offer_7", kind = "consumable", shelf = "offers", credits = 560000, gems = 17000, referenceUsdCents = 7999 },
                },
            };
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(dto, problems);
            CollectionAssert.IsEmpty(problems);

            Assert.AreEqual(460, OfferValue.Extra(catalog.Find("gg_offer_1"), catalog));
            Assert.AreEqual(290, OfferValue.Extra(catalog.Find("gg_offer_7"), catalog));
            Assert.AreEqual(0, OfferValue.Extra(catalog.Find("gg_gems_1"), catalog), "the base pack is worth itself");
        }

        [Test]
        public void AnOfferProductMustBeARepeatablePackOfCurrency()
        {
            var dto = new StoreDto
            {
                products = new[]
                {
                    new StoreProductDto { id = "gg_gems_1", kind = "consumable", shelf = "gems", gems = 100, referenceUsdCents = 99 },
                    new StoreProductDto { id = "bad_once", kind = "nonconsumable", shelf = "offers", gems = 100, referenceUsdCents = 99 },
                    new StoreProductDto { id = "bad_badge", kind = "consumable", shelf = "offers", gems = 100, referenceUsdCents = 99, badge = "popular" },
                    new StoreProductDto { id = "bad_pass", kind = "consumable", shelf = "offers", gems = 100, passDays = 7, referenceUsdCents = 99 },
                },
            };
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(dto, problems);
            Assert.IsNull(catalog.Find("bad_once"));
            Assert.IsNull(catalog.Find("bad_badge"));
            Assert.IsNull(catalog.Find("bad_pass"));
            Assert.AreEqual(3, problems.Count, string.Join("; ", problems));
        }
    }
}
