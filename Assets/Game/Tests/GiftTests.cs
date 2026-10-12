using System.Collections.Generic;
using GlimmerGrove.Cloud;
using GlimmerGrove.Gifts;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Admin gifts (invariant 63), the client half.
    ///
    /// <para>
    /// <b>The id is a wire contract</b> with <c>firebase/functions/src/gifts.ts</c> (held there by
    /// <c>test/gifts.mjs</c>): the server pays a gift under exactly the claim id this device queues,
    /// so a drift is a gift counted twice or never confirmed. <b>The reader mirrors the server's</b>,
    /// so a row the server would refuse to pay is never shown. <b>The ledger</b> waits on nothing
    /// until the server has said what is owed, lists what is owed and not yet claimed, queues each
    /// currency under its derived id on Claim, pays once however often Claim is pressed, keeps a
    /// half-paid gift waiting for its other half, and is emptied by the reply that confirms the
    /// claims - every order of events ending with the player paid exactly once.
    /// </para>
    /// </summary>
    public sealed class GiftTests
    {
        sealed class FixedClock : IGameClock
        {
            public long Now;
            public long UtcNowUnix => Now;
            public bool IsTrusted => false;
        }

        const long Now = 1_791_700_000L;
        const string Id = "g202610111530k3x9";
        const string Other = "g202610111600aaaa";

        [SetUp]
        public void Start()
        {
            GameClock.Set(new FixedClock { Now = Now });
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
            ProgressionStore.LoadFrom(new SaveFileDto());
            Wallet.LoadFrom(new SaveFileDto());
            PlayerProgression.Invalidate();
            GiftLedger.ResetForTests();
        }

        [TearDown]
        public void Restore()
        {
            GiftLedger.ResetForTests();
            GameClock.Set(new DeviceClock());
            ProgressionRules.Reset();
            Wallet.LoadFrom(new SaveFileDto());
            PlayerProgression.Invalidate();
        }

        static CloudGiftRow Row(string id, long credits, long gems, long at = Now - 60)
            => new CloudGiftRow { Id = id, Credits = credits, Gems = gems, AtUnix = at };

        static List<CloudGiftRow> Rows(params CloudGiftRow[] rows) => new List<CloudGiftRow>(rows);

        static long Balance(string currency)
        {
            PlayerProgression.Invalidate();
            return currency == Currency.Credits ? PlayerProgression.Credits : PlayerProgression.Gems;
        }

        // ------------------------------------------------------------ the ids
        [Test]
        public void TheIdIsTheServersSpelling()
        {
            Assert.AreEqual("gift:" + Id + ":credits", GrantEntry.GiftId(Id, Currency.Credits));
            Assert.AreEqual("gift:" + Id + ":gems", GrantEntry.GiftId(Id, Currency.Gems));
            Assert.AreEqual("admin_gift", GrantEntry.GiftReason);
            Assert.LessOrEqual(GrantEntry.GiftId(Id, Currency.Credits).Length, 64, "the server's id bound");
        }

        [Test]
        public void AGiftIdIsWhatTheServerMints()
        {
            Assert.IsTrue(AdminGifts.IsId(Id));
            Assert.IsFalse(AdminGifts.IsId("d202610111530k3x9"), "a deal's letter");
            Assert.IsFalse(AdminGifts.IsId("G202610111530k3x9"));
            Assert.IsFalse(AdminGifts.IsId("g20261011153k3x9"));
            Assert.IsFalse(AdminGifts.IsId("g202610111530K3X9"));
            Assert.IsFalse(AdminGifts.IsId("g202610111530k3x9x"));
            Assert.IsFalse(AdminGifts.IsId(null));
        }

        // ------------------------------------------------------------ the reader
        [Test]
        public void TheReaderTakesSoundRowsAndDropsTheRest()
        {
            var gifts = AdminGifts.Read(Rows(
                Row(Id, 500, 20),
                Row("bad", 500, 20),
                Row(Other, 0, 0),
                Row(Other, -1, 5),
                Row(Other, AdminGifts.MaxCredits + 1, 0),
                Row(Other, 0, AdminGifts.MaxGems + 1),
                Row(Id, 1, 1),
                Row(Other, 0, 7)));

            Assert.AreEqual(2, gifts.Count, "two sound rows; a duplicate id is read once");
            Assert.AreEqual(Id, gifts[0].Id);
            Assert.AreEqual(500L, gifts[0].Credits);
            Assert.AreEqual(20L, gifts[0].Gems);
            Assert.AreEqual(Other, gifts[1].Id);
            Assert.AreEqual(0L, gifts[1].Credits);
            Assert.AreEqual(7L, gifts[1].Gems);
            Assert.AreEqual(0, AdminGifts.Read(null).Count);
        }

        // ------------------------------------------------------------ waiting
        [Test]
        public void NothingWaitsUntilTheServerHasSaidWhatIsOwed()
        {
            Assert.IsFalse(GiftLedger.Known);
            Assert.AreEqual(0, GiftLedger.Waiting.Count);

            GiftLedger.ApplyServerState(carried: false, Rows(Row(Id, 500, 20)));
            Assert.IsFalse(GiftLedger.Known, "a deployment that predates gifts says nothing");
            Assert.AreEqual(0, GiftLedger.Waiting.Count);

            GiftLedger.ApplyServerState(carried: true, Rows(Row(Id, 500, 20)));
            Assert.IsTrue(GiftLedger.Known);
            Assert.AreEqual(1, GiftLedger.Waiting.Count);
            Assert.AreEqual(Id, GiftLedger.Waiting[0].Id);

            GiftLedger.ApplyServerState(carried: true, Rows());
            Assert.AreEqual(0, GiftLedger.Waiting.Count, "an empty list clears what was owed");
        }

        [Test]
        public void TheLedgerRaisesChangedOnlyWhenSomethingMoved()
        {
            int raised = 0;
            void Count() => raised++;
            GiftLedger.Changed += Count;
            try
            {
                GiftLedger.ApplyServerState(carried: true, Rows(Row(Id, 500, 20)));
                Assert.AreEqual(1, raised, "the first answer");
                GiftLedger.ApplyServerState(carried: true, Rows(Row(Id, 500, 20)));
                Assert.AreEqual(1, raised, "the same answer again is not a change (44m)");
                GiftLedger.ApplyServerState(carried: true, Rows(Row(Id, 500, 20), Row(Other, 0, 7)));
                Assert.AreEqual(2, raised, "a new gift is");
            }
            finally
            {
                GiftLedger.Changed -= Count;
            }
        }

        // ------------------------------------------------------------ claiming
        [Test]
        public void ClaimQueuesEachCurrencyUnderItsIdAndPaysAtOnce()
        {
            GiftLedger.ApplyServerState(carried: true, Rows(Row(Id, 500, 20)));
            long credits = Balance(Currency.Credits), gems = Balance(Currency.Gems);

            var claim = GiftLedger.Claim(GiftLedger.Waiting);
            Assert.IsTrue(claim.Any);
            Assert.AreEqual(500L, claim.Credits);
            Assert.AreEqual(20L, claim.Gems);
            Assert.AreEqual(1, claim.Gifts);

            Assert.AreEqual(credits + 500L, Balance(Currency.Credits), "spendable at once");
            Assert.AreEqual(gems + 20L, Balance(Currency.Gems));
            Assert.IsTrue(Wallet.Ledger(Currency.Credits).HasGranted(GrantEntry.GiftId(Id, Currency.Credits)));
            Assert.IsTrue(Wallet.Ledger(Currency.Gems).HasGranted(GrantEntry.GiftId(Id, Currency.Gems)));

            Assert.AreEqual(0, GiftLedger.Waiting.Count, "a claimed gift no longer waits");
            Assert.AreEqual(1, GiftLedger.Pending.Count, "though the server has not yet answered");
        }

        [Test]
        public void ClaimingTwiceOrOnTwoPhonesPaysOnce()
        {
            GiftLedger.ApplyServerState(carried: true, Rows(Row(Id, 500, 20)));
            var gifts = GiftLedger.Waiting;
            GiftLedger.Claim(gifts);
            long credits = Balance(Currency.Credits), gems = Balance(Currency.Gems);

            var again = GiftLedger.Claim(gifts);
            Assert.IsFalse(again.Any, "nothing left to grant");
            Assert.AreEqual(credits, Balance(Currency.Credits));
            Assert.AreEqual(gems, Balance(Currency.Gems));

            // A wallet read that raced the claim still lists the gift: the queue says it is held.
            GiftLedger.ApplyServerState(carried: true, Rows(Row(Id, 500, 20)));
            Assert.AreEqual(0, GiftLedger.Waiting.Count, "a listed gift this device has queued does not wait again");
        }

        [Test]
        public void AHalfPaidGiftWaitsForItsOtherHalfOnly()
        {
            // The server paid the credits on another phone (or this one, before a reinstall): the
            // reply lists the gift with the credits at nought and the gems still owed.
            GiftLedger.ApplyServerState(carried: true, Rows(Row(Id, 0, 20)));
            Assert.AreEqual(1, GiftLedger.Waiting.Count);
            long credits = Balance(Currency.Credits);

            var claim = GiftLedger.Claim(GiftLedger.Waiting);
            Assert.AreEqual(0L, claim.Credits, "nothing is queued for a currency the gift no longer carries");
            Assert.AreEqual(20L, claim.Gems);
            Assert.AreEqual(credits, Balance(Currency.Credits));
            Assert.IsFalse(Wallet.Ledger(Currency.Credits).HasGranted(GrantEntry.GiftId(Id, Currency.Credits)));
            Assert.IsTrue(Wallet.Ledger(Currency.Gems).HasGranted(GrantEntry.GiftId(Id, Currency.Gems)));
            Assert.AreEqual(0, GiftLedger.Waiting.Count);
        }

        [Test]
        public void AGiftWhoseGemsWereQueuedStillWaitsForItsCredits()
        {
            // One half queued (a process killed between the two awards): the gift waits for the rest.
            GiftLedger.ApplyServerState(carried: true, Rows(Row(Id, 500, 20)));
            PlayerProgression.Award(Currency.Gems, 20L, GrantEntry.GiftId(Id, Currency.Gems), GrantEntry.GiftReason, Now);

            Assert.AreEqual(1, GiftLedger.Waiting.Count, "the credits are still owed");
            var claim = GiftLedger.Claim(GiftLedger.Waiting);
            Assert.AreEqual(500L, claim.Credits);
            Assert.AreEqual(0L, claim.Gems, "the gems were already queued and are not counted twice");
        }

        [Test]
        public void TheConfirmingReplyEmptiesTheQueueAndTheList()
        {
            GiftLedger.ApplyServerState(carried: true, Rows(Row(Id, 500, 20)));
            GiftLedger.Claim(GiftLedger.Waiting);
            long credits = Balance(Currency.Credits), gems = Balance(Currency.Gems);

            // The server pays both halves: the baselines carry them and the ids are confirmed, and
            // the same reply lists no gift. The balance does not move - it was counted once already.
            var creditLedger = Wallet.Ledger(Currency.Credits);
            var gemLedger = Wallet.Ledger(Currency.Gems);
            creditLedger.ApplyServerState(creditLedger.GrantedBaseline + 500L, creditLedger.SpentBaseline, new string[0], 0L,
                                          confirmedGrantIds: new[] { GrantEntry.GiftId(Id, Currency.Credits) });
            gemLedger.ApplyServerState(gemLedger.GrantedBaseline + 20L, gemLedger.SpentBaseline, new string[0], 0L,
                                       confirmedGrantIds: new[] { GrantEntry.GiftId(Id, Currency.Gems) });
            GiftLedger.ApplyServerState(carried: true, Rows());

            Assert.AreEqual(credits, Balance(Currency.Credits), "counted once: the queue gave way to the baseline");
            Assert.AreEqual(gems, Balance(Currency.Gems));
            Assert.IsFalse(creditLedger.HasGranted(GrantEntry.GiftId(Id, Currency.Credits)));
            Assert.AreEqual(0, GiftLedger.Pending.Count);
            Assert.AreEqual(0, GiftLedger.Waiting.Count);
        }

        [Test]
        public void ARefusedClaimIsDroppedWithItsBalanceAndTheGiftIsOfferedAgainIfStillListed()
        {
            // Only a forged id is ever refused; the shape still has to be sound: the balance it
            // inflated goes, and whatever the server still lists is offered again rather than lost.
            GiftLedger.ApplyServerState(carried: true, Rows(Row(Id, 500, 0)));
            long before = Balance(Currency.Credits);
            GiftLedger.Claim(GiftLedger.Waiting);
            Assert.AreEqual(before + 500L, Balance(Currency.Credits));

            var ledger = Wallet.Ledger(Currency.Credits);
            ledger.ApplyServerState(ledger.GrantedBaseline, ledger.SpentBaseline, new string[0], 0L,
                                    rejectedGrantIds: new[] { GrantEntry.GiftId(Id, Currency.Credits) });
            GiftLedger.ApplyServerState(carried: true, Rows(Row(Id, 500, 0)));

            Assert.AreEqual(before, Balance(Currency.Credits), "the inflated balance is gone");
            Assert.AreEqual(1, GiftLedger.Waiting.Count, "and the gift the server still lists waits again");
        }

        [Test]
        public void SwitchingAccountsForgetsTheLastAccountsGifts()
        {
            GiftLedger.ApplyServerState(carried: true, Rows(Row(Id, 500, 20)));
            GiftLedger.OnIdentityChanged();
            Assert.IsFalse(GiftLedger.Known);
            Assert.AreEqual(0, GiftLedger.Waiting.Count, "the next wallet reply says the new account's");
        }

        [Test]
        public void TheWaitingRuleIsPure()
        {
            var pending = new List<AdminGift>
            {
                new AdminGift(Id, 500, 20, Now), new AdminGift(Other, 0, 7, Now),
            };
            var held = new HashSet<string> { GrantEntry.GiftId(Id, Currency.Credits), GrantEntry.GiftId(Other, Currency.Gems) };

            var waiting = GiftLedger.WaitingOf(pending, (gift, currency) => held.Contains(GrantEntry.GiftId(gift, currency)));
            Assert.AreEqual(1, waiting.Count, "the first still owes its gems; the second is wholly queued");
            Assert.AreEqual(Id, waiting[0].Id);

            Assert.AreEqual(2, GiftLedger.WaitingOf(pending, null).Count, "nothing held: everything waits");
            Assert.AreEqual(0, GiftLedger.WaitingOf(null, null).Count);
        }

        // ------------------------------------------------------------ the prompt
        [Test]
        public void ThePromptShowsEveryWaitingGiftWheneverOneIsNew()
        {
            var a = new AdminGift(Id, 500, 20, Now - 60);
            var b = new AdminGift(Other, 0, 7, Now);
            var waiting = new List<AdminGift> { a, b };
            var shown = new HashSet<string>();

            var show = GiftPrompt.Choose(waiting, shown.Contains);
            Assert.AreEqual(2, show.Count, "nothing shown yet: both");

            shown.Add(Id);
            shown.Add(Other);
            Assert.AreEqual(0, GiftPrompt.Choose(waiting, shown.Contains).Count, "both shown this session: not again");

            var c = new AdminGift("g202610111700zzzz", 100, 0, Now + 60);
            waiting.Add(c);
            show = GiftPrompt.Choose(waiting, shown.Contains);
            Assert.AreEqual(3, show.Count, "a new gift raises the panel with everything still waiting");

            Assert.AreEqual(0, GiftPrompt.Choose(new List<AdminGift>(), shown.Contains).Count);
            Assert.AreEqual(0, GiftPrompt.Choose(null, shown.Contains).Count);
        }
    }
}
