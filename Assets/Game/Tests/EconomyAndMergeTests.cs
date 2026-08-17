using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The currency ledger and the save merge.
    ///
    /// Both exist to make multi-device play safe, and both are the kind of code whose
    /// bugs are invisible until they are expensive: a merge that loses a spend gives
    /// currency away, one that counts it twice takes it, and either only shows up once
    /// real players have two devices and real money is involved.
    /// </summary>
    public sealed class EconomyAndMergeTests
    {
        // --------------------------------------------------------------- ledger
        [Test]
        public void BalanceIsEarnedPlusGrantedMinusSpent()
        {
            var ledger = new CurrencyLedger(Currency.Credits);
            ledger.GrantLocally(500);
            Assert.IsTrue(ledger.TrySpend(120, derivedEarned: 300, "hint", out _));

            Assert.AreEqual(300 + 500 - 120, ledger.BalanceFrom(300));
        }

        [Test]
        public void SpendingMoreThanTheBalanceIsRefused()
        {
            var ledger = new CurrencyLedger(Currency.Credits);
            ledger.GrantLocally(50);

            Assert.IsFalse(ledger.TrySpend(100, derivedEarned: 10, "too much", out _));
            Assert.AreEqual(60, ledger.BalanceFrom(10), "a refused spend must not be recorded");
        }

        [Test]
        public void ABalanceNeverGoesNegative()
        {
            var ledger = new CurrencyLedger(Currency.Credits);
            ledger.GrantLocally(100);
            ledger.TrySpend(100, 0, "all of it", out _);

            // A reward retune that lowers derived earnings below what was already spent.
            Assert.AreEqual(0, ledger.BalanceFrom(0));
        }

        [Test]
        public void TheEarnedHighWaterFloorsAReducedReward()
        {
            var ledger = new CurrencyLedger(Currency.Credits);
            Assert.IsTrue(ledger.RaiseEarnedHighWater(400));

            Assert.AreEqual(400, ledger.BalanceFrom(120),
                            "retuning a reward downwards must not take currency off a player");
            Assert.IsFalse(ledger.RaiseEarnedHighWater(300), "the floor only ratchets upwards");
        }

        [Test]
        public void ConfirmedSpendsAreDroppedFromThePendingQueue()
        {
            var ledger = new CurrencyLedger(Currency.Credits);
            ledger.GrantLocally(1000);
            ledger.TrySpend(100, 0, "booster", out var entry);

            Assert.AreEqual(900, ledger.BalanceFrom(0));

            // The server folds that debit into its baseline and names it.
            ledger.ApplyServerState(grantedBaseline: 1000, spentBaseline: 100,
                                    confirmedSpendIds: new[] { entry.Id }, confirmedThroughUnix: 0);

            Assert.AreEqual(0, ledger.PendingSpend, "a confirmed debit must leave the queue");
            Assert.AreEqual(900, ledger.BalanceFrom(0), "and must not be charged a second time");
        }

        [Test]
        public void ASpendIdIsOnlyEverCountedOnce()
        {
            var ledger = new CurrencyLedger(Currency.Credits);
            ledger.GrantLocally(1000);
            ledger.TrySpend(100, 0, "booster", out var entry);

            // A file that somehow lists the same debit twice — a bad merge, a manual
            // edit — must still charge for it once.
            var dto = ledger.ToDto();
            dto.pendingSpends = new[] { entry.ToDto(), entry.ToDto() };

            var reloaded = CurrencyLedger.FromDto(dto);

            Assert.AreEqual(100, reloaded.PendingSpend);
            Assert.AreEqual(900, reloaded.BalanceFrom(0));
        }

        [Test]
        public void ServerBaselinesAreAdoptedRatherThanMaxed()
        {
            var ledger = new CurrencyLedger(Currency.Credits);
            ledger.GrantLocally(1000);

            // A refund legitimately lowers what was granted; a ledger that only ever
            // rose could not represent it.
            ledger.ApplyServerState(grantedBaseline: 400, spentBaseline: 0, null, 0);

            Assert.AreEqual(400, ledger.GrantedBaseline);
        }

        [Test]
        public void LedgersRoundTripThroughTheirDto()
        {
            var ledger = new CurrencyLedger(Currency.Gems);
            ledger.GrantLocally(42);
            ledger.RaiseEarnedHighWater(7);
            ledger.TrySpend(10, 0, "skin", out _);
            ledger.ConfirmedThroughUnix = 555;

            var back = CurrencyLedger.FromDto(ledger.ToDto());

            Assert.AreEqual(Currency.Gems, back.Currency);
            Assert.AreEqual(42, back.GrantedBaseline);
            Assert.AreEqual(7, back.EarnedHighWater);
            Assert.AreEqual(10, back.PendingSpend);
            Assert.AreEqual(555, back.ConfirmedThroughUnix);
        }

        // ---------------------------------------------------------- ledger merge
        [Test]
        public void MergingUnionsPendingSpendsInsteadOfSummingOrMaxing()
        {
            var a = new CurrencyLedger(Currency.Credits);
            a.GrantLocally(1000);
            a.TrySpend(100, 0, "on device a", out _);

            var b = new CurrencyLedger(Currency.Credits);
            b.GrantLocally(1000);
            b.TrySpend(50, 0, "on device b", out _);

            a.MergeFrom(b);

            Assert.AreEqual(150, a.PendingSpend,
                            "taking the larger counter would forgive a spend; summing counters " +
                            "would charge twice — only a union of identified debits is right");
        }

        [Test]
        public void MergingTheSameLedgerTwiceChangesNothing()
        {
            var a = new CurrencyLedger(Currency.Credits);
            a.GrantLocally(1000);
            a.TrySpend(100, 0, "x", out _);

            var b = CurrencyLedger.FromDto(a.ToDto());

            a.MergeFrom(b);
            a.MergeFrom(b);

            Assert.AreEqual(100, a.PendingSpend, "a join must be idempotent");
        }

        [Test]
        public void ADebitTheServerHasAlreadyAbsorbedIsNotChargedAgainOnMerge()
        {
            // Device A synced: the debit is in the baseline and out of the queue.
            var synced = new CurrencyLedger(Currency.Credits);
            synced.GrantLocally(1000);
            synced.TrySpend(100, 0, "booster", out var entry);
            synced.ApplyServerState(1000, 100, new[] { entry.Id }, confirmedThroughUnix: entry.Unix);

            // Device B has been offline and still holds it as pending.
            var stale = new CurrencyLedger(Currency.Credits);
            stale.GrantLocally(1000);

            var staleDto = stale.ToDto();
            staleDto.pendingSpends = new[] { entry.ToDto() };
            stale = CurrencyLedger.FromDto(staleDto);

            synced.MergeFrom(stale);

            Assert.AreEqual(100, synced.Spent, "the debit is in the baseline; queueing it too double-charges");
            Assert.AreEqual(900, synced.BalanceFrom(0));
        }

        // ------------------------------------------------------------ save merge
        static SaveFileDto File(string levelId, int stars, int moves, long updatedUnix = 100)
            => new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                updatedUnix = updatedUnix,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                levels = new[]
                {
                    new LevelRecordDto
                    {
                        levelId = levelId, stars = stars, bestMoves = moves, clears = 1,
                        firstClearedUnix = updatedUnix, lastPlayedUnix = updatedUnix,
                    },
                },
                progression = ProgressionStateDto.Unwritten(),
            };

        static LevelRecordDto Find(SaveFileDto dto, string levelId)
        {
            foreach (var record in dto.levels)
                if (record.levelId == levelId) return record;
            return null;
        }

        [Test]
        public void MergingKeepsTheBestOfEachMeasureIndependently()
        {
            var mine = File("a", stars: 3, moves: 40, updatedUnix: 100);
            var theirs = File("a", stars: 1, moves: 20, updatedUnix: 200);

            var merged = SaveMerge.Join(mine, theirs);
            var record = Find(merged, "a");

            Assert.AreEqual(3, record.stars, "the better star rating survives");
            Assert.AreEqual(20, record.bestMoves, "so does the better move count, from the other device");
            Assert.AreEqual(100, record.firstClearedUnix, "first clear is the earlier of the two");
        }

        [Test]
        public void MergingKeepsLevelsOnlyOneSideHas()
        {
            var mine = File("a", 3, 10);
            var theirs = File("b", 2, 20);

            var merged = SaveMerge.Join(mine, theirs);

            Assert.AreEqual(2, merged.levels.Length);
            Assert.IsNotNull(Find(merged, "a"));
            Assert.IsNotNull(Find(merged, "b"));
        }

        [Test]
        public void MergingIsCommutative()
        {
            var mine = File("a", 3, 40, 100);
            var theirs = File("a", 1, 20, 200);

            var oneWay = Find(SaveMerge.Join(mine, theirs), "a");
            var otherWay = Find(SaveMerge.Join(theirs, mine), "a");

            Assert.AreEqual(oneWay.stars, otherWay.stars);
            Assert.AreEqual(oneWay.bestMoves, otherWay.bestMoves);
            Assert.AreEqual(oneWay.firstClearedUnix, otherWay.firstClearedUnix,
                            "which device syncs first must not change the answer");
        }

        [Test]
        public void MergingIsIdempotent()
        {
            var mine = File("a", 3, 40, 100);
            var theirs = File("a", 1, 20, 200);

            var once = SaveMerge.Join(mine, theirs);
            var twice = SaveMerge.Join(once, theirs);

            Assert.AreEqual(Find(once, "a").stars, Find(twice, "a").stars);
            Assert.AreEqual(Find(once, "a").bestMoves, Find(twice, "a").bestMoves);
            Assert.AreEqual(once.levels.Length, twice.levels.Length);
        }

        [Test]
        public void AnUnclearedRecordDoesNotWinTheFirstClearTimestamp()
        {
            var cleared = File("a", 3, 10, 500);
            var opened = File("a", 0, 0, 100);
            opened.levels[0].firstClearedUnix = 0;       // played, never cleared

            var record = Find(SaveMerge.Join(opened, cleared), "a");

            Assert.AreEqual(500, record.firstClearedUnix, "zero means never, not earliest");
            Assert.AreEqual(10, record.bestMoves, "and zero moves is not a perfect score");
        }

        [Test]
        public void HighWaterMarksTakeTheLargerValue()
        {
            var mine = File("a", 1, 10);
            mine.progression = new ProgressionStateDto { xpHighWater = 900, levelHighWater = 4 };

            var theirs = File("a", 1, 10);
            theirs.progression = new ProgressionStateDto { xpHighWater = 300, levelHighWater = 7 };

            var merged = SaveMerge.Join(mine, theirs);

            Assert.AreEqual(900, merged.progression.xpHighWater);
            Assert.AreEqual(7, merged.progression.levelHighWater);
        }

        [Test]
        public void TheLegacyImportFlagStaysSetOnceEitherSideHasRunIt()
        {
            var imported = File("a", 1, 10);
            imported.legacyImportDone = true;

            var fresh = File("b", 1, 10);
            fresh.legacyImportDone = false;

            Assert.IsTrue(SaveMerge.Join(fresh, imported).legacyImportDone,
                          "re-importing would fold the same pre-1.0 stars in twice");
        }

        [Test]
        public void PreferencesTakeTheMoreRecentFile()
        {
            var older = File("a", 1, 10, updatedUnix: 100);
            older.settings = new SettingsDto { music = StoredFlag.From(true) };
            older.wallet.displayName = "Old Name";
            older.wallet.displayNameSetUnix = 100;

            var newer = File("a", 1, 10, updatedUnix: 500);
            newer.settings = new SettingsDto { music = StoredFlag.From(false) };
            newer.wallet.displayName = "New Name";
            newer.wallet.displayNameSetUnix = 500;

            var merged = SaveMerge.Join(older, newer);

            Assert.IsFalse(merged.settings.music.Resolve(true),
                           "muting is an instruction, not a value to maximise");

            // Dated by its own stamp, not by the file's. The file's date is stamped with
            // "now" every time the sync takes a snapshot, so reading recency off it made
            // the local device win every comparison — see SaveMerge.Chosen.
            Assert.AreEqual("New Name", merged.wallet.displayName);
            Assert.AreEqual(500, merged.wallet.displayNameSetUnix,
                            "the stamp has to travel with the value it dates");
        }

        [Test]
        public void TheMergedRevisionIsAheadOfBothInputs()
        {
            var mine = File("a", 1, 10);
            mine.cloud = new CloudStateDto { revision = 4, userId = "u1", deviceId = "mine" };

            var theirs = File("a", 1, 10);
            theirs.cloud = new CloudStateDto { revision = 9, userId = "u1", deviceId = "theirs" };

            var merged = SaveMerge.Join(mine, theirs);

            Assert.AreEqual(10, merged.cloud.revision, "a backend using revision for concurrency must accept it");
            Assert.AreEqual("mine", merged.cloud.deviceId, "the merging device keeps its own id");
        }

        [Test]
        public void MergingWithNothingIsSafe()
        {
            var mine = File("a", 3, 10);

            Assert.AreSame(mine, SaveMerge.Join(mine, null));
            Assert.AreSame(mine, SaveMerge.Join(null, mine));
            Assert.IsNull(SaveMerge.Join(null, null));
        }
    }
}
