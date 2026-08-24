using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Buying grove stock: the one path in this feature that takes a player's credits.
    ///
    /// <para>
    /// <b>Why this file exists.</b> Everything <em>around</em> the purchase was covered —
    /// <see cref="GroveStockTests"/> pins the offer arithmetic, every refusal, the merge, the
    /// migration and the ceilings; the shared vectors pin the server's agreement. The line
    /// that actually moves money was not covered anywhere, offline or in the Editor, and
    /// "the ledger it copies has the same gap" is the weakest argument available in this
    /// project. A purchase debits once and grants once, and nothing asserted either.
    /// </para>
    /// <para>
    /// <b>It runs offline, against a save that never reaches a disk.</b>
    /// <see cref="SaveService.LoadWith"/> takes an <see cref="ISaveStore"/>, which is the seam
    /// <c>AccountSwitchTests</c> established for exactly this reason: the rules worth proving
    /// here are about a ledger and a wallet, and the disk round trip that would otherwise be
    /// dragged in is <c>SaveStoreTests</c>' subject. A fresh account is seeded
    /// <see cref="Currency.SeedCredits"/>, so the fixtures below are priced against that
    /// rather than against a granted balance no real player would have.
    /// </para>
    /// <para>
    /// <b>One property here is deliberately not covered, and it is recorded rather than left
    /// silent.</b> <c>TryBuy</c> debits <em>before</em> it grants, so a process killed between
    /// the two leaves a player who paid and did not receive — visible in the spend log and
    /// fixable by support — rather than stock nobody paid for, which is indistinguishable from
    /// a forged save and therefore invisible. Reversing those two statements is not detectable
    /// by any test in this file, and that was measured rather than assumed: the mutation passes
    /// the whole suite. It is unreachable because <see cref="HomesteadLedger.OfferFor"/> gates
    /// on affordability first, so no path exists where the offer says yes and the debit says
    /// no. Closing it would need a seam that makes <c>PlayerProgression.TrySpend</c> fail on
    /// demand — a refactor of a class this feature does not own, to guard an ordering whose
    /// failure is rare and whose consequence already errs in the recoverable direction. If a
    /// spender seam is ever added for another reason, this is the first test to write against
    /// it.
    /// </para>
    /// <para>
    /// Everything else here was mutation-checked. Granting one copy instead of the bundle,
    /// granting the bundle while ignoring the quantity, and debiting once per bundle instead of
    /// once per order are each caught by two or more of the cases below.
    /// </para>
    /// </summary>
    public sealed class GroveStockPurchaseTests
    {
        /// <summary>Priced so several bundles fit inside the account seed, and so do their sums.</summary>
        const int FencePrice = 90;
        const int FenceBundle = 10;
        const int WellPrice = 400;

        [SetUp]
        public void Open()
        {
            SaveService.Unload();
            SaveService.LoadWith(new MemoryStore());

            HomesteadLedger.ResetForTests();
            HomesteadLayout.ResetForTests();
            HomesteadCatalog.Publish(Catalog());
        }

        [TearDown]
        public void Close()
        {
            HomesteadLedger.ResetForTests();
            HomesteadLayout.ResetForTests();
            HomesteadCatalog.Publish(null);
            SaveService.Unload();
        }

        // ------------------------------------------------------------- fixtures
        static HomesteadPiece Bundled(string id, int cost, int bundle)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Decor,
                                  cost, LevelId.None, ChapterId.None, 1f, .5f,
                                  HomesteadSlotKind.Edge, 0, 0, bundle);

        static HomesteadPiece Free(string id)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Decor,
                                  0, LevelId.None, ChapterId.None, 1f, .5f);

        static HomesteadCatalog Catalog()
            => new HomesteadCatalog(
                HomesteadCatalog.Current.Floor,
                new List<HomesteadPiece>
                {
                    Bundled("fence", FencePrice, FenceBundle),
                    Bundled("well", WellPrice, 1),
                    Free("pebble"),
                },
                null);

        static HomesteadPiece Find(string id) => HomesteadCatalog.Current.Find(id);

        /// <summary>How many credits the player is holding right now.</summary>
        static long Credits => PlayerProgression.Credits;

        /// <summary>Every debit this account has booked, in the order it booked them.</summary>
        static List<SpendEntryDto> Spends()
        {
            var dto = new SaveFileDto();
            Wallet.WriteInto(dto);

            var rows = new List<SpendEntryDto>();
            foreach (var ledger in dto.wallet?.currencies ?? Array.Empty<CurrencyLedgerDto>())
            {
                if (!string.Equals(ledger.currency, Currency.Credits, StringComparison.Ordinal)) continue;
                foreach (var spend in ledger.pendingSpends ?? Array.Empty<SpendEntryDto>()) rows.Add(spend);
            }

            return rows;
        }

        // ============================================================ the seed
        [Test]
        public void AFreshAccountIsSeededAndTheFixturesFitInsideIt()
        {
            // Not a tautology: every assertion below is priced against this, so a retune of
            // the seed that quietly made these orders unaffordable would otherwise show up as
            // a dozen mysterious failures about stock rather than one about money.
            Assert.AreEqual(Currency.SeedCredits, Credits);
            Assert.Greater(Credits, FencePrice * 3L, "three bundles must be affordable");
        }

        // ======================================================= one purchase
        [Test]
        public void BuyingOneBundleChargesOncePriceAndGrantsTheWholeBundle()
        {
            var fence = Find("fence");
            long before = Credits;

            Assert.IsTrue(HomesteadLedger.TryBuy(fence));

            Assert.AreEqual(before - FencePrice, Credits, "charged exactly the bundle price");
            Assert.AreEqual(FenceBundle, HomesteadLedger.Copies(fence), "and delivered the bundle");
            Assert.AreEqual(FenceBundle, HomesteadLedger.Available(fence), "none of them placed yet");
            Assert.IsTrue(HomesteadLedger.IsHeld(fence));
        }

        [Test]
        public void APieceWithNoBundleDeliversOneCopy()
        {
            var well = Find("well");
            long before = Credits;

            Assert.IsTrue(HomesteadLedger.TryBuy(well));

            Assert.AreEqual(before - WellPrice, Credits);
            Assert.AreEqual(1, HomesteadLedger.Copies(well));
        }

        // ======================================================== an order
        [Test]
        public void BuyingThreeBundlesChargesThreeTimesAndGrantsThreeTimes()
        {
            var fence = Find("fence");
            long before = Credits;

            Assert.IsTrue(HomesteadLedger.TryBuy(fence, 3));

            Assert.AreEqual(before - FencePrice * 3L, Credits);
            Assert.AreEqual(FenceBundle * 3, HomesteadLedger.Copies(fence));
        }

        [Test]
        public void AnOrderIsOneDebitAndNeverOnePerBundle()
        {
            // The reason TryBuy does not loop over single purchases. A loop would book a spend
            // entry per bundle — three idempotency keys for one decision — and each one is
            // separately refusable by the next sync, so a player could be charged for three
            // fences and receive two with nothing on screen able to explain the difference.
            var fence = Find("fence");

            Assert.IsTrue(HomesteadLedger.TryBuy(fence, 3));

            var spends = Spends();
            Assert.AreEqual(1, spends.Count, "one decision is one debit");
            Assert.AreEqual(FencePrice * 3L, spends[0].amount);
            StringAssert.Contains("fence", spends[0].reason ?? string.Empty,
                                  "the debit names what it paid for, for support to read");
        }

        [Test]
        public void BuyingTwiceAccumulatesCopiesAndDebitsSeparately()
        {
            var fence = Find("fence");
            long before = Credits;

            Assert.IsTrue(HomesteadLedger.TryBuy(fence, 2));
            Assert.IsTrue(HomesteadLedger.TryBuy(fence, 1));

            Assert.AreEqual(before - FencePrice * 3L, Credits);
            Assert.AreEqual(FenceBundle * 3, HomesteadLedger.Copies(fence));
            Assert.AreEqual(2, Spends().Count, "two decisions are two debits");
        }

        // ====================================================== the refusals
        [Test]
        public void AnUnaffordableOrderTakesNothingAndGrantsNothing()
        {
            var fence = Find("fence");
            long before = Credits;

            // Far beyond the seed, and clamped by the offer to what the balance can carry —
            // so the refusal has to come from the price rather than from the clamp letting a
            // smaller order through silently.
            int wanted = (int)(before / FencePrice) + 50;

            Assert.IsFalse(HomesteadLedger.TryBuy(fence, wanted));

            Assert.AreEqual(before, Credits, "a refused purchase is free");
            Assert.AreEqual(0, HomesteadLedger.Copies(fence));
            Assert.IsEmpty(Spends(), "and books no debit at all");
        }

        [Test]
        public void AFreePieceCannotBeBoughtAndCostsNothingToTry()
        {
            long before = Credits;

            Assert.IsFalse(HomesteadLedger.TryBuy(Find("pebble")));

            Assert.AreEqual(before, Credits);
            Assert.IsEmpty(Spends());
        }

        [Test]
        public void APieceThatIsNotInTheCatalogIsNeverCharged()
        {
            long before = Credits;

            Assert.IsFalse(HomesteadLedger.TryBuy(default));

            Assert.AreEqual(before, Credits);
            Assert.IsEmpty(Spends());
        }

        // =================================================== what reaches disk
        [Test]
        public void APurchaseSurvivesAWriteAndReadUnchanged()
        {
            var fence = Find("fence");
            Assert.IsTrue(HomesteadLedger.TryBuy(fence, 2));

            var written = new SaveFileDto();
            HomesteadLedger.WriteInto(written);

            HomesteadLedger.ResetForTests();
            Assert.AreEqual(0, HomesteadLedger.Copies(fence), "the ledger really was emptied");

            HomesteadLedger.LoadFrom(written);

            Assert.AreEqual(FenceBundle * 2, HomesteadLedger.Copies(fence),
                            "copies are what a grove is worth; losing them is losing the purchase");
            CollectionAssert.AreEqual(new[] { "fence" }, written.homesteadOwned,
                                      "and the v19 mirror names it, for a rolled-back client");
        }

        [Test]
        public void TheDebitAndTheCopiesLandInTheSameSavedFile()
        {
            // TryBuy debits first and records second, deliberately: a process killed between
            // them leaves a player who paid and did not receive, which support can see in the
            // spend log and put right. The other order leaves stock nobody paid for, which is
            // indistinguishable from a forged save and therefore invisible. What must not
            // happen is the two landing in *different* saves, so this asserts one file holds
            // both.
            var fence = Find("fence");
            Assert.IsTrue(HomesteadLedger.TryBuy(fence, 2));

            var file = SaveService.Snapshot();

            Assert.IsNotNull(file.homesteadStock);
            Assert.AreEqual(1, file.homesteadStock.Length);
            Assert.AreEqual("fence", file.homesteadStock[0].id);
            Assert.AreEqual(FenceBundle * 2, file.homesteadStock[0].copies);

            long debited = 0L;
            foreach (var ledger in file.wallet?.currencies ?? Array.Empty<CurrencyLedgerDto>())
            {
                if (!string.Equals(ledger.currency, Currency.Credits, StringComparison.Ordinal)) continue;
                foreach (var spend in ledger.pendingSpends ?? Array.Empty<SpendEntryDto>()) debited += spend.amount;
            }

            Assert.AreEqual(FencePrice * 2L, debited, "the money and the goods are in one file");
        }

        // -------------------------------------------------------- the harness
        /// <summary>
        /// A save file that never reaches a disk. Kept as it was given rather than round
        /// tripped through JSON, for <c>AccountSwitchTests.MemoryStore</c>'s reason:
        /// serialisation is <c>SaveStoreTests</c>' subject and borrowing it here would only
        /// put <c>JsonUtility</c> in the way of a question about money.
        /// </summary>
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

                // Otherwise the load reaches LegacyPlayerPrefsImport, which is PlayerPrefs,
                // which is the Editor. There is no legacy build to import from in a test.
                legacyImportDone = true,
            };

            public bool Save(SaveFileDto dto)
            {
                _file = dto;
                return true;
            }

            public void Delete() => _file = null;
        }
    }
}
