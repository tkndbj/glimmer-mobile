using System.Collections.Generic;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Currency the player was <em>given</em> rather than earned, and the queue that
    /// carries it until the server confirms it.
    ///
    /// This is the half of the economy that has to be right for the daily chests to be
    /// safe to ship. The client may never raise its own granted baseline — that is the
    /// field an attacker wants, and the reason the security rules make the wallet
    /// document server-only — so an award opened offline lands here instead, carrying an
    /// id derived from what earned it.
    ///
    /// The derived id is what makes everything else fall out: a chest claimed on two
    /// devices produces byte-identical entries that union to one, a resubmission after a
    /// lost reply confirms rather than pays, and a save file edited to forget a claim
    /// still collides with the entry already in the ledger.
    /// </summary>
    public sealed class AwardLedgerTests
    {
        const long T0 = 1_700_000_000;
        const string Reason = GrantEntry.DailyChestReason;

        static string Id(int day, int chest) => GrantEntry.DailyChestId(day, chest, Currency.Credits);

        static CurrencyLedger Fresh()
        {
            var ledger = new CurrencyLedger(Currency.Credits);
            ledger.GrantLocally(1000);              // the account seed
            return ledger;
        }

        // ----------------------------------------------------------- awarding
        [Test]
        public void AnAwardIsSpendableImmediatelyWithoutRaisingTheGrantedBaseline()
        {
            var ledger = Fresh();
            long baselineBefore = ledger.GrantedBaseline;

            Assert.IsTrue(ledger.TryAward(Id(20315, 0), 240, T0, Reason, out _));

            Assert.AreEqual(baselineBefore, ledger.GrantedBaseline,
                            "the client must never raise the field the server owns");
            Assert.AreEqual(240, ledger.PendingGrant);
            Assert.AreEqual(1240, ledger.BalanceFrom(0),
                            "a chest opened on a plane has to be spendable on that plane");
        }

        [Test]
        public void TheSameAwardCannotBeTakenTwice()
        {
            var ledger = Fresh();

            Assert.IsTrue(ledger.TryAward(Id(20315, 0), 240, T0, Reason, out _));
            Assert.IsFalse(ledger.TryAward(Id(20315, 0), 240, T0 + 60, Reason, out _),
                           "an id that has already paid must not pay again");

            Assert.AreEqual(240, ledger.PendingGrant);
        }

        [Test]
        public void DifferentDaysAndChestsAreDifferentAwards()
        {
            var ledger = Fresh();

            Assert.IsTrue(ledger.TryAward(Id(20315, 0), 10, T0, Reason, out _));
            Assert.IsTrue(ledger.TryAward(Id(20315, 1), 20, T0, Reason, out _));
            Assert.IsTrue(ledger.TryAward(Id(20316, 0), 40, T0, Reason, out _));

            Assert.AreEqual(70, ledger.PendingGrant);
        }

        [Test]
        public void ChestIdsNameTheDayTheChestAndTheCurrency()
        {
            Assert.AreEqual("daily:20315:2:credits", GrantEntry.DailyChestId(20315, 2, Currency.Credits));
            Assert.AreEqual("daily:20315:2:gems", GrantEntry.DailyChestId(20315, 2, Currency.Gems));

            Assert.AreNotEqual(GrantEntry.DailyChestId(20315, 2, Currency.Credits),
                               GrantEntry.DailyChestId(20315, 2, Currency.Gems),
                               "one chest paying two currencies is two awards, not one");
        }

        [Test]
        public void NothingAndNegativeAmountsAreRefused()
        {
            var ledger = Fresh();

            Assert.IsFalse(ledger.TryAward(Id(20315, 0), 0, T0, Reason, out _));
            Assert.IsFalse(ledger.TryAward(Id(20315, 0), -5, T0, Reason, out _));
            Assert.IsFalse(ledger.TryAward(string.Empty, 100, T0, Reason, out _));
            Assert.IsFalse(ledger.TryAward(null, 100, T0, Reason, out _));

            Assert.AreEqual(0, ledger.PendingGrant);
        }

        // ----------------------------------------------------------- the merge
        /// <summary>
        /// The reason the id is derived rather than generated. Two devices that open
        /// Tuesday's chest offline both produce <c>daily:20315:0:credits</c>, the union
        /// keeps one, and nothing anywhere has to notice.
        /// </summary>
        [Test]
        public void TwoDevicesClaimingOneChestOfflinePayOnce()
        {
            var phone = Fresh();
            var tablet = Fresh();

            phone.TryAward(Id(20315, 0), 240, T0, Reason, out _);
            tablet.TryAward(Id(20315, 0), 240, T0 + 30, Reason, out _);

            phone.MergeFrom(tablet);

            Assert.AreEqual(1, phone.PendingGrants.Count);
            Assert.AreEqual(240, phone.PendingGrant, "merging must not double an award");
        }

        [Test]
        public void AwardsFromBothDevicesSurviveTheMerge()
        {
            var phone = Fresh();
            var tablet = Fresh();

            phone.TryAward(Id(20315, 0), 100, T0, Reason, out _);
            tablet.TryAward(Id(20316, 0), 200, T0, Reason, out _);

            phone.MergeFrom(tablet);

            Assert.AreEqual(300, phone.PendingGrant, "neither device's chest may be lost");
        }

        [Test]
        public void TheAwardMergeIsIdempotent()
        {
            var phone = Fresh();
            var tablet = Fresh();

            phone.TryAward(Id(20315, 0), 100, T0, Reason, out _);
            tablet.TryAward(Id(20316, 1), 200, T0, Reason, out _);

            phone.MergeFrom(tablet);
            long once = phone.PendingGrant;

            phone.MergeFrom(tablet);
            phone.MergeFrom(tablet);

            Assert.AreEqual(once, phone.PendingGrant);
        }

        // ------------------------------------------------------ server confirms
        [Test]
        public void AConfirmedAwardMovesIntoTheBaselineWithoutBeingCountedTwice()
        {
            var ledger = Fresh();
            ledger.TryAward(Id(20315, 0), 240, T0, Reason, out _);

            Assert.AreEqual(1240, ledger.BalanceFrom(0));

            // The server has recorded it: the baseline now includes it, so the queue must not.
            ledger.ApplyServerState(grantedBaseline: 1240, spentBaseline: 0,
                                    confirmedSpendIds: null, confirmedThroughUnix: T0,
                                    earnedFloor: 0,
                                    confirmedGrantIds: new List<string> { Id(20315, 0) });

            Assert.AreEqual(0, ledger.PendingGrant);
            Assert.AreEqual(1240, ledger.BalanceFrom(0), "the balance must not move when it is confirmed");
        }

        /// <summary>
        /// A dropped reply is the ordinary case, not the exception. The award stays local
        /// and gets resubmitted; the id makes that safe.
        /// </summary>
        [Test]
        public void AnUnconfirmedAwardIsKeptRatherThanQuietlyDropped()
        {
            var ledger = Fresh();
            ledger.TryAward(Id(20315, 0), 240, T0, Reason, out _);

            // The server answered, but about nothing in particular — no ids came back.
            ledger.ApplyServerState(grantedBaseline: 1000, spentBaseline: 0,
                                    confirmedSpendIds: null, confirmedThroughUnix: T0 + 10_000,
                                    earnedFloor: 0, confirmedGrantIds: null);

            Assert.AreEqual(240, ledger.PendingGrant,
                            "guessing wrong about a debit charges late; guessing wrong here deletes money");
            Assert.AreEqual(1240, ledger.BalanceFrom(0));
        }

        [Test]
        public void ConfirmingAnAwardTwiceChangesNothing()
        {
            var ledger = Fresh();
            ledger.TryAward(Id(20315, 0), 240, T0, Reason, out _);

            var confirmed = new List<string> { Id(20315, 0) };

            ledger.ApplyServerState(1240, 0, null, T0, 0, confirmed);
            ledger.ApplyServerState(1240, 0, null, T0, 0, confirmed);

            Assert.AreEqual(1240, ledger.BalanceFrom(0));
        }

        [Test]
        public void AnAwardCanBeSpentBeforeItIsConfirmed()
        {
            var ledger = Fresh();
            ledger.TryAward(Id(20315, 2), 500, T0, Reason, out _);

            Assert.IsTrue(ledger.TrySpend(1400, 0, "hint", out _));
            Assert.AreEqual(100, ledger.BalanceFrom(0));
        }

        // ------------------------------------------------------- the file bridge
        [Test]
        public void AwardsSurviveARoundTripThroughTheSaveFile()
        {
            var ledger = Fresh();
            ledger.TryAward(Id(20315, 0), 240, T0, Reason, out _);
            ledger.TryAward(Id(20315, 1), 15, T0, Reason, out _);

            var reloaded = CurrencyLedger.FromDto(ToDto(ledger));

            Assert.AreEqual(2, reloaded.PendingGrants.Count);
            Assert.AreEqual(255, reloaded.PendingGrant);
            Assert.AreEqual(ledger.BalanceFrom(0), reloaded.BalanceFrom(0));
        }

        [Test]
        public void ADuplicatedEntryInAFileIsReadOnce()
        {
            var dto = new CurrencyLedgerDto
            {
                currency = Currency.Credits,
                grantedBaseline = 0,
                pendingGrants = new[]
                {
                    new GrantEntryDto { id = Id(20315, 0), amount = 240, unix = T0, reason = Reason },
                    new GrantEntryDto { id = Id(20315, 0), amount = 240, unix = T0, reason = Reason },
                },
            };

            var ledger = CurrencyLedger.FromDto(dto);

            Assert.AreEqual(1, ledger.PendingGrants.Count);
            Assert.AreEqual(240, ledger.PendingGrant, "a hand-edited file must not double an award");
        }

        [Test]
        public void AFileWrittenBeforeAwardsExistedReadsCleanly()
        {
            var ledger = CurrencyLedger.FromDto(new CurrencyLedgerDto
            {
                currency = Currency.Credits,
                grantedBaseline = 1250,
                pendingGrants = null,
            });

            Assert.AreEqual(0, ledger.PendingGrant);
            Assert.AreEqual(1250, ledger.BalanceFrom(0));
        }

        /// <summary>Reaches the internal writer the save file uses.</summary>
        static CurrencyLedgerDto ToDto(CurrencyLedger ledger)
        {
            var wallet = new SaveFileDto();
            var dto = new CurrencyLedgerDto
            {
                currency = ledger.Currency,
                grantedBaseline = ledger.GrantedBaseline,
                spentBaseline = ledger.SpentBaseline,
                earnedHighWater = ledger.EarnedHighWater,
                confirmedThroughUnix = ledger.ConfirmedThroughUnix,
                pendingSpends = new SpendEntryDto[0],
                pendingGrants = new GrantEntryDto[ledger.PendingGrants.Count],
            };

            for (int i = 0; i < ledger.PendingGrants.Count; i++)
                dto.pendingGrants[i] = ledger.PendingGrants[i].ToDto();

            _ = wallet;
            return dto;
        }
    }
}
