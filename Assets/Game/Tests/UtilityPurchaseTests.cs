using System.Collections.Generic;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Utilities;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The stops on an order: how many of a utility the shop will sell right now, and what a
    /// purchase of more than one actually charges.
    ///
    /// <para>
    /// <b>It exists because the ceiling moved.</b> A player could hold nine, so every order was
    /// for one and there was nothing to bound — <c>UtilityLedger</c> said as much, and said why.
    /// At a hundred a shelf that sold one at a time would be a hundred taps, so the panel counts
    /// out loud, and a stepper is only honest if both its stops are: what there is room for, and
    /// what the gems in hand will cover. A stepper reading twelve over a button that will sell
    /// two is the panel lying about the one thing it exists to be exact about.
    /// </para>
    /// <para>
    /// It runs against a save that never reaches a disk — <c>SaveService.LoadWith</c> takes an
    /// <c>ISaveStore</c>, which is the seam <c>GroveStockPurchaseTests</c> established for
    /// exactly this. A fresh account is seeded <c>Currency.SeedGems</c>, so every case below
    /// says out loud what it is holding.
    /// </para>
    /// </summary>
    public sealed class UtilityPurchaseTests
    {
        [SetUp]
        public void Open()
        {
            SaveService.Unload();
            GlimmerGrove.Progression.EndlessCoins.UseStore(new GlimmerGrove.Progression.EndlessCoins.MemoryStore());
            SaveService.LoadWith(new MemoryStore());
            ProgressionRules.Reset();
        }

        [TearDown]
        public void Restore()
        {
            SaveService.Unload();
            GlimmerGrove.Progression.EndlessCoins.UseStore(null);
            ProgressionRules.Reset();
        }

        static UtilityItem Firepot => UtilityLedger.Catalog.Find("firepot");

        /// <summary>
        /// Puts a known number of gems in the wallet.
        ///
        /// <c>GrantLocally</c> is the account seed's door and nothing else's (invariant 10a),
        /// which is exactly why it is right here and wrong everywhere else: a fixture needs an
        /// opening balance, not an award. <c>HeartRescueTests.Hold</c> establishes the same way.
        /// </summary>
        static void Hold(long gems)
        {
            long already = PlayerProgression.Gems;
            if (gems > already) Wallet.Ledger(Currency.Gems).GrantLocally(gems - already);

            Assert.AreEqual(gems, PlayerProgression.Gems, "the fixture did not take");
        }

        // ============================================================ the ceiling
        /// <summary>
        /// The number the shelf, the panel and the badge all rest on. Pinned rather than
        /// described, because "up to a hundred" is a decision about the shop and the three
        /// places that draw it read it from here.
        /// </summary>
        [Test]
        public void EveryUtilityMayBeStockedAHundredDeep()
        {
            foreach (var item in UtilityCatalog.Default.Items)
                Assert.AreEqual(100, item.MaxHeld,
                    $"'{item.Id}' does not carry the shipped ceiling");
        }

        // ============================================================ the stops
        [Test]
        public void ThePurseIsTheStopWhenItIsTheTighterOfTheTwo()
        {
            var item = Firepot;
            Hold(item.GemPrice * 4L);

            Assert.AreEqual(4, UtilityLedger.MaxQuantity(item));
        }

        [Test]
        public void TheRoomLeftIsTheStopWhenItIsTheTighterOfTheTwo()
        {
            var item = Firepot;
            Hold(item.GemPrice * 500L);

            UtilityLedger.Grant(item.Id, item.MaxHeld - 3);

            Assert.AreEqual(3, UtilityLedger.MaxQuantity(item),
                "a purse with hundreds in it must not offer more than the pack will take");
        }

        [Test]
        public void AFullPackSellsNothingHoweverRichTheKeeperIs()
        {
            var item = Firepot;
            Hold(item.GemPrice * 500L);

            UtilityLedger.Grant(item.Id, item.MaxHeld);

            Assert.AreEqual(0, UtilityLedger.MaxQuantity(item));
            Assert.AreEqual(UtilityRefusal.Full, UtilityLedger.WhyNotBuy(item, 1));
        }

        /// <summary>
        /// A purse that cannot cover one sells none, rather than one it cannot pay for.
        ///
        /// Priced above the account seed rather than emptied down to nothing: a fresh account is
        /// given <c>Currency.SeedGems</c> and there is deliberately no door that takes money back
        /// out of a wallet, so the honest way to be short is to be short of something.
        /// </summary>
        [Test]
        public void APurseShortOfOneSellsNothingHoweverMuchRoomThereIs()
        {
            var dear = new UtilityItem("dear", UtilityKind.Blast, 10,
                                       gemPrice: (int)Currency.SeedGems + 1,
                                       maxHeld: 100, order: 1);

            Assert.AreEqual(0, UtilityLedger.MaxQuantity(dear));
        }

        /// <summary>
        /// A chest-only utility has no stops at all, because it has no price. Nought rather than
        /// the room left, so a panel opened over one draws a stepper that cannot move rather
        /// than one that can be walked up to a purchase the ledger will refuse.
        /// </summary>
        [Test]
        public void AChestOnlyUtilityIsNeverSold()
        {
            var free = new UtilityItem("gift", UtilityKind.Blast, 10, gemPrice: 0,
                                       maxHeld: 100, order: 1);

            Assert.AreEqual(0, UtilityLedger.MaxQuantity(free));
        }

        /// <summary>
        /// The two stops are told apart by the room left, which is what lets a refused <c>+</c>
        /// say <em>which</em> wall it hit.
        ///
        /// <para>
        /// <c>UtilityBuyOverlay</c> answers a refused tap upward with "not enough gems" or "no
        /// room for more", and it decides between them with <c>RoomFor(item) &lt;= quantity</c>
        /// rather than by re-deriving the purse. This is the property that makes that reading
        /// right: <see cref="UtilityLedger.MaxQuantity"/> is the lesser of the two, so at the top
        /// stop the room is *equal* to it when the pack is the tighter and *greater* when the
        /// purse is. Pinned rather than described, because the panel's branch is one comparison
        /// and a panel naming the wrong wall sends a player who is full to the gem shelf.
        /// </para>
        /// </summary>
        [Test]
        public void AtTheTopStopTheRoomLeftSaysWhichOfTheTwoWallsWasHit()
        {
            var item = Firepot;

            // The purse is the tighter: the room is strictly greater than the stop, so the wall
            // is money and the panel offers the shelf.
            Hold(item.GemPrice * 4L);

            int stop = UtilityLedger.MaxQuantity(item);
            Assert.AreEqual(4, stop);
            Assert.Greater(UtilityLedger.RoomFor(item), stop,
                "a purse-bound stop must leave room, or the panel names the wrong wall");

            // The pack is the tighter: the room is exactly the stop, so the wall is one money
            // cannot climb and the panel says so instead (invariant 15a's ordering).
            Hold(item.GemPrice * 500L);
            UtilityLedger.Grant(item.Id, item.MaxHeld - 3);

            stop = UtilityLedger.MaxQuantity(item);
            Assert.AreEqual(3, stop);
            Assert.AreEqual(stop, UtilityLedger.RoomFor(item),
                "a room-bound stop must equal the room, or a full pack is sent to the gem shelf");
        }

        // ============================================================ the charge
        [Test]
        public void AnOrderOfSeveralChargesTheWholeOrderAndLandsAllOfThem()
        {
            var item = Firepot;
            Hold(item.GemPrice * 7L);

            Assert.IsTrue(UtilityLedger.TryBuy(item, 7, out var refusal), refusal.ToString());

            Assert.AreEqual(7, UtilityLedger.Held(item));
            Assert.AreEqual(0L, PlayerProgression.Gems, "the whole order was not charged");
        }

        /// <summary>
        /// One gem short of the total is a refusal, not a smaller order. The panel clamps the
        /// stepper to what can be paid for; the ledger's job is to refuse anything that got past
        /// that, and to refuse it <em>before</em> taking anything — a partial delivery is the
        /// failure invariant 23 names about a continue that does not continue.
        /// </summary>
        [Test]
        public void AnOrderTheKeeperCannotQuiteCoverIsRefusedWhole()
        {
            var item = Firepot;
            Hold(item.GemPrice * 3L - 1L);

            Assert.IsFalse(UtilityLedger.TryBuy(item, 3, out var refusal));
            Assert.AreEqual(UtilityRefusal.Poor, refusal);

            Assert.AreEqual(0, UtilityLedger.Held(item));
            Assert.AreEqual(item.GemPrice * 3L - 1L, PlayerProgression.Gems);
        }

        [Test]
        public void AnOrderPastTheCeilingIsRefusedRatherThanClamped()
        {
            var item = Firepot;
            Hold(item.GemPrice * 500L);

            UtilityLedger.Grant(item.Id, item.MaxHeld - 2);

            Assert.IsFalse(UtilityLedger.TryBuy(item, 3, out var refusal));
            Assert.AreEqual(UtilityRefusal.Full, refusal);
            Assert.AreEqual(item.MaxHeld - 2, UtilityLedger.Held(item));
        }

        // -------------------------------------------------------- the harness
        /// <summary>
        /// A save file that never reaches a disk. Kept as it was given rather than round
        /// tripped through JSON, for <c>GroveStockPurchaseTests.MemoryStore</c>'s reason:
        /// serialisation is <c>SaveStoreTests</c>' subject and borrowing it here would only put
        /// <c>JsonUtility</c> in the way of a question about money.
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
