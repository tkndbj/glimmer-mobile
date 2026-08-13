using System.IO;
using GlimmerGrove.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Upgrading a live save file from v1 to v2.
    ///
    /// This is the code that runs once on every existing player's device the first
    /// time they open the update, and it is the only code in the game that can
    /// silently destroy an account. Everything here is a regression guard against a
    /// specific way of doing that.
    /// </summary>
    public sealed class SaveSchemaV2Tests
    {
        string _dir;

        [SetUp]
        public void MakeDir()
        {
            _dir = Path.Combine(Application.temporaryCachePath, "v2tests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void RemoveDir()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        // ------------------------------------------------------------- checksum
        /// <summary>
        /// The checksum covers the serialised object, so a file written by v1 can never
        /// hash to what v2 computes — v2's object has fields v1 had never heard of.
        /// Without the version check in <see cref="SaveChecksum.Verify"/>, shipping any
        /// new field would fail every save on every device, fall through to a backup
        /// that fails identically, and hand the player a brand-new game.
        /// </summary>
        [Test]
        public void AV1FileWhoseChecksumCannotMatchIsStillLoaded()
        {
            var v1 = new SaveFileDto
            {
                schemaVersion = SaveSchema.FlatWalletVersion,
                settings = new SettingsDto(),
                wallet = new WalletDto { coins = 900, gems = 4, hearts = 3, displayName = "Mossy" },
                levels = new[] { new LevelRecordDto { levelId = "c01_first_light", stars = 3, bestMoves = 12 } },
                legacyImportDone = true,
                checksum = "0123456789abcdef",           // a hash this build can never reproduce
            };

            File.WriteAllText(Path.Combine(_dir, SaveSchema.FileName), JsonUtility.ToJson(v1, true));

            var loaded = new SaveStore(_dir).Load();

            Assert.AreEqual(1, loaded.levels.Length,
                            "a schema upgrade must never read as corruption");
            Assert.AreEqual("c01_first_light", loaded.levels[0].levelId);
            Assert.AreEqual(3, loaded.levels[0].stars);
        }

        /// <summary>
        /// The relaxation above is scoped to a version change and nothing else — a
        /// current-version file that has actually been damaged must still be caught.
        /// </summary>
        [Test]
        public void ACurrentVersionFileWithABadChecksumIsStillRejected()
        {
            var store = new SaveStore(_dir);

            var good = new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                levels = new[] { new LevelRecordDto { levelId = "kept", stars = 1 } },
            };
            store.Save(good);                                   // becomes the backup

            var newer = new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                levels = new[] { new LevelRecordDto { levelId = "damaged", stars = 1 } },
            };
            store.Save(newer);

            string path = Path.Combine(_dir, SaveSchema.FileName);
            var tampered = JsonUtility.FromJson<SaveFileDto>(File.ReadAllText(path));
            tampered.levels = new LevelRecordDto[0];            // valid JSON, missing data
            File.WriteAllText(path, JsonUtility.ToJson(tampered, true));

            var loaded = store.Load();

            Assert.AreEqual("kept", loaded.levels[0].levelId,
                            "integrity checking must still work within a version");
        }

        // -------------------------------------------------------------- wallet
        [Test]
        public void AV1FlatBalanceBecomesAGrantedBaseline()
        {
            Wallet.LoadFrom(new SaveFileDto
            {
                schemaVersion = SaveSchema.FlatWalletVersion,
                wallet = new WalletDto { coins = 777, gems = 9, hearts = 2, displayName = "Fern" },
            });

            Assert.AreEqual(777, Wallet.Ledger(Currency.Credits).GrantedBaseline,
                            "whatever a player held has to survive the move to a derived balance");
            Assert.AreEqual(9, Wallet.Ledger(Currency.Gems).GrantedBaseline);
            Assert.AreEqual(777, Wallet.Ledger(Currency.Credits).BalanceFrom(0));
        }

        [Test]
        public void ABrandNewSaveGetsTheSeedThroughTheSamePath()
        {
            Wallet.LoadFrom(new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                wallet = WalletDto.Unwritten(),
            });

            Assert.AreEqual(Currency.SeedCredits, Wallet.Ledger(Currency.Credits).GrantedBaseline,
                            "no version check to get wrong: an absent ledger is an absent ledger");
            Assert.AreEqual(Currency.SeedGems, Wallet.Ledger(Currency.Gems).GrantedBaseline);
        }

        [Test]
        public void MigrationCannotRunTwiceAndDoubleABalance()
        {
            var ledger = new CurrencyLedger(Currency.Credits);
            ledger.GrantLocally(777);

            // A v2 file that still carries the retired v1 mirror fields, which is
            // exactly what this build writes.
            Wallet.LoadFrom(new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                wallet = new WalletDto
                {
                    coins = 777, gems = -1, hearts = 5,
                    currencies = new[] { ledger.ToDto() },
                },
            });

            Assert.AreEqual(777, Wallet.Ledger(Currency.Credits).GrantedBaseline,
                            "an existing ledger wins; folding the mirror in again would double it");
        }

        [Test]
        public void TheWalletSurvivesAWriteAndReload()
        {
            Wallet.LoadFrom(new SaveFileDto
            {
                schemaVersion = SaveSchema.FlatWalletVersion,
                wallet = new WalletDto { coins = 500, gems = 3, hearts = 4, displayName = "Bracken" },
            });
            Wallet.Ledger(Currency.Credits).TrySpend(120, 0, "hint", out _);

            var written = new SaveFileDto();
            Wallet.WriteInto(written);

            // Round-trip through JSON, because that is what actually happens.
            var reloaded = JsonUtility.FromJson<SaveFileDto>(JsonUtility.ToJson(written, true));
            Wallet.LoadFrom(reloaded);

            Assert.AreEqual(500, Wallet.Ledger(Currency.Credits).GrantedBaseline);
            Assert.AreEqual(120, Wallet.Ledger(Currency.Credits).PendingSpend,
                            "an unconfirmed debit has to survive a restart or it is charged twice");
            Assert.AreEqual(380, Wallet.Ledger(Currency.Credits).BalanceFrom(0));
            Assert.AreEqual("Bracken", Wallet.DisplayName);
        }

        // ---------------------------------------------------------- progression
        [Test]
        public void AnAbsentProgressionSectionReadsAsNoFloorRatherThanAFloorOfZero()
        {
            ProgressionStore.LoadFrom(new SaveFileDto { schemaVersion = SaveSchema.FlatWalletVersion });

            Assert.AreEqual(0, ProgressionStore.XpHighWater);
            Assert.AreEqual(0, ProgressionStore.LevelHighWater);
            Assert.IsTrue(ProgressionStore.RaiseXp(10), "a v1 file must not pin the floor");
        }

        [Test]
        public void HighWaterMarksOnlyEverRise()
        {
            ProgressionStore.LoadFrom(new SaveFileDto
            {
                progression = new ProgressionStateDto { xpHighWater = 900, levelHighWater = 5 },
            });

            Assert.IsFalse(ProgressionStore.RaiseXp(400));
            Assert.IsFalse(ProgressionStore.RaiseLevel(2));
            Assert.AreEqual(900, ProgressionStore.XpHighWater);
            Assert.AreEqual(5, ProgressionStore.LevelHighWater);

            Assert.IsTrue(ProgressionStore.RaiseLevel(6));
        }
    }
}
