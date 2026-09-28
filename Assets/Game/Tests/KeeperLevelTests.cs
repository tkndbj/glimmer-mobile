using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Ranks;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Keeper levels bought outright (invariant 57), the client half.
    ///
    /// <para>
    /// <b>The price rule exists twice and a drift is a purchase refused</b> - the device shows a
    /// price and debits it, the server holds the debit to its own copy, and a disagreement is a
    /// level taken back from a player who was just shown its cost. So both halves run
    /// <c>firebase/shared/grove-vectors.json</c>: this fixture through the shipped reader and
    /// <c>firebase/functions/test/keeper.mjs</c> through the compiled function, with
    /// <c>Tools/make_keeper_vectors.py</c> writing the cases from the prose rule. Read through
    /// <see cref="TestJson"/> for invariant 29e's reason: a vector file only the Editor can read
    /// is not a guard.
    /// </para>
    /// <para>
    /// <b>The rest is the level rule</b>: the effective level is earned plus bought, the rank
    /// ladder reads earned alone, the floors ratchet earned alone, a purchase debits then counts,
    /// a refusal takes the level and everything above it back, and the server's count is folded
    /// in upward always and downward only when nothing is in flight.
    /// </para>
    /// </summary>
    public sealed class KeeperLevelTests
    {
        // ------------------------------------------------------------- the file
        static Dictionary<string, object> _file;

        static Dictionary<string, object> File()
            => _file ??= TestJson.ReadShared("grove-vectors.json");

        static bool Flag(Dictionary<string, object> map, string key)
            => map.TryGetValue(key, out object v) && v is bool b && b;

        static KeeperLadderDto LadderDto(object raw)
        {
            if (raw == null) return null;
            var map = TestJson.Object(raw);
            var dto = new KeeperLadderDto { top = TestJson.Int(map, "top", -1) };

            if (map.TryGetValue("anchors", out object rows) && rows is List<object> list)
            {
                dto.anchors = new KeeperAnchorDto[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    var row = TestJson.Object(list[i]);
                    dto.anchors[i] = new KeeperAnchorDto
                    {
                        level = TestJson.Int(row, "level"),
                        currency = TestJson.Str(row, "currency"),
                        price = TestJson.Int(row, "price"),
                    };
                }
            }
            return dto;
        }

        static Dictionary<string, KeeperLadder> Ladders()
        {
            var out_ = new Dictionary<string, KeeperLadder>();
            foreach (var pair in TestJson.Child(File(), "keeperPriceLadders"))
            {
                var problems = new List<string>();
                var ladder = KeeperLadder.Resolve(LadderDto(pair.Value), problems);
                Assert.IsTrue(ladder.Sells, $"the '{pair.Key}' ladder should resolve: {string.Join("; ", problems)}");
                out_[pair.Key] = ladder;
            }
            Assert.Greater(out_.Count, 0, "the vector file has no keeper ladders");
            return out_;
        }

        // ----------------------------------------------------------- the table
        /// <summary>
        /// Publishes a whole reward table carrying the block under test, through the shipped
        /// reader - <c>XpBoostTests.Publish</c>'s argument: a table assembled by the test would
        /// prove nothing about the one the game builds.
        /// </summary>
        static void Publish(KeeperLadderDto keeperLevels, int maxLevel = 500)
        {
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                maxLevel = maxLevel,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 10,
                keeperLevels = keeperLevels,
            };

            var problems = new List<string>();
            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, problems), string.Join("; ", problems));
            ProgressionRules.Publish(table);
        }

        static KeeperLadderDto Shipped()
        {
            var text = System.IO.File.ReadAllText(Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets",
                                                               "Content", "progression.json"));
            var map = TestJson.Object(TestJson.Parse(text));
            Assert.IsTrue(map.ContainsKey("keeperLevels"), "progression.json carries no keeperLevels block");
            return LadderDto(map["keeperLevels"]);
        }

        static SaveFileDto SaveWith(int bought, long credits = 0L, long gems = 0L)
        {
            var wallet = new WalletDto { keeperLevelsBought = bought };
            var dto = new SaveFileDto { wallet = wallet };
            Wallet.LoadFrom(dto);
            if (credits > 0) Wallet.Ledger(Currency.Credits).GrantLocally(credits);
            if (gems > 0) Wallet.Ledger(Currency.Gems).GrantLocally(gems);
            return dto;
        }

        [SetUp]
        public void Reset()
        {
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
            ProgressionStore.LoadFrom(new SaveFileDto());
            Wallet.LoadFrom(new SaveFileDto());
            PlayerProgression.Invalidate();
        }

        [TearDown]
        public void Restore() => Reset();

        // ------------------------------------------------------- the shared contract
        [Test]
        public void EveryVectorCasePricesTheWayTheServerPrices()
        {
            var ladders = Ladders();
            int seen = 0;

            foreach (object raw in TestJson.Children(File(), "keeperPriceCases"))
            {
                var c = TestJson.Object(raw);
                string name = TestJson.Str(c, "name");
                var ladder = ladders[TestJson.Str(c, "ladder")];
                int level = TestJson.Int(c, "level");

                bool sold = ladder.PriceFor(level, out string currency, out long price);
                Assert.AreEqual(Flag(c, "sold"), sold, name);
                if (sold)
                {
                    Assert.AreEqual(TestJson.Str(c, "currency"), currency, name);
                    Assert.AreEqual(TestJson.Long(c, "price"), price, name);
                }
                seen++;
            }

            Assert.Greater(seen, 20, "the vector file has too few keeper cases to mean anything");
        }

        [Test]
        public void EveryRefusedBlockResolvesToNothing()
        {
            int seen = 0;
            foreach (object raw in TestJson.Children(File(), "keeperPriceRejected"))
            {
                var c = TestJson.Object(raw);
                var problems = new List<string>();
                var ladder = KeeperLadder.Resolve(LadderDto(c.TryGetValue("block", out object b) ? b : null), problems);

                Assert.IsFalse(ladder.Sells, TestJson.Str(c, "name"));
                Assert.AreEqual(0, ladder.Top, TestJson.Str(c, "name"));
                seen++;
            }
            Assert.Greater(seen, 8);
        }

        [Test]
        public void EverySpendIdParsesTheWayTheServerParses()
        {
            foreach (object raw in TestJson.Children(File(), "keeperSpendIds"))
            {
                var c = TestJson.Object(raw);
                string id = TestJson.Str(c, "id");
                bool invalid = Flag(c, "invalid");

                bool ok = SpendEntry.TryParseKeeperLevelId(id, out int ordinal, out int level);
                Assert.AreEqual(!invalid, ok, $"'{id}'");
                if (ok)
                {
                    Assert.AreEqual(TestJson.Int(c, "ordinal"), ordinal, id);
                    Assert.AreEqual(TestJson.Int(c, "level"), level, id);
                    Assert.AreEqual(id, SpendEntry.KeeperLevelId(ordinal, level), "round trip");
                }
            }
        }

        [Test]
        public void TheShippedLadderIsTheOneTheVectorsPin()
        {
            var problems = new List<string>();
            var shipped = KeeperLadder.Resolve(Shipped(), problems);
            Assert.IsTrue(shipped.Sells, string.Join("; ", problems));

            var pinned = Ladders()["shipped"];
            Assert.AreEqual(pinned.Top, shipped.Top, "re-run Tools/make_keeper_vectors.py after retuning the ladder");
            Assert.AreEqual(pinned.Anchors.Count, shipped.Anchors.Count);
            for (int i = 0; i < pinned.Anchors.Count; i++)
            {
                Assert.AreEqual(pinned.Anchors[i].Level, shipped.Anchors[i].Level);
                Assert.AreEqual(pinned.Anchors[i].Currency, shipped.Anchors[i].Currency);
                Assert.AreEqual(pinned.Anchors[i].Price, shipped.Anchors[i].Price);
            }
        }

        [Test]
        public void TheShippedLadderIsCoinsFirstThenGemsAndClimbs()
        {
            var ladder = KeeperLadder.Resolve(Shipped(), new List<string>());
            long last = 0;
            string lastCurrency = Currency.Credits;

            for (int level = ladder.First; level <= ladder.Top; level++)
            {
                Assert.IsTrue(ladder.PriceFor(level, out string currency, out long price), $"level {level}");
                Assert.Greater(price, 0L, $"level {level}");

                // Once the ladder has switched to gems it never switches back.
                if (lastCurrency == Currency.Gems) Assert.AreEqual(Currency.Gems, currency, $"level {level}");
                // And within a currency the price never falls.
                if (currency == lastCurrency) Assert.GreaterOrEqual(price, last, $"level {level}");

                last = price;
                lastCurrency = currency;
            }
            Assert.IsFalse(ladder.PriceFor(1, out _, out _), "level 1 is where everybody starts");
            Assert.IsFalse(ladder.PriceFor(ladder.Top + 1, out _, out _), "nothing past the top");
        }

        [Test]
        public void ATopAboveTheCurveWithdrawsTheLadder()
        {
            Publish(new KeeperLadderDto
            {
                top = 40,
                anchors = new[] { new KeeperAnchorDto { level = 2, currency = "gems", price = 10 },
                                  new KeeperAnchorDto { level = 40, currency = "gems", price = 90 } },
            }, maxLevel: 30);

            Assert.IsFalse(ProgressionRules.Table.KeeperLevels.Sells);
            Assert.IsFalse(KeeperLedger.Next().Sold);
        }

        [Test]
        public void ADefaultTableSellsNothing()
        {
            Assert.IsFalse(ProgressionTable.Default.KeeperLevels.Sells);
            Assert.IsFalse(KeeperLedger.Next().Sold, "nothing is offered against a ladder nobody published");
            Assert.AreEqual(KeeperBuy.NotSold, KeeperLedger.TryBuy());
        }

        // ------------------------------------------------------------ the level
        [Test]
        public void TheLevelIsEarnedPlusBoughtAndTheRankReadsEarnedAlone()
        {
            Publish(Shipped());
            SaveWith(bought: 3);
            PlayerProgression.Invalidate();

            Assert.AreEqual(1, PlayerProgression.EarnedLevel.Level, "no XP is level 1");
            Assert.AreEqual(4, PlayerProgression.Level.Level, "plus three bought");
            Assert.AreEqual(3, KeeperLedger.Bought);

            // What the rank ladder is asked: the earned level, never the effective one.
            Assert.AreEqual(1L, new LedgerRankSource(GameContent.Index).KeeperLevel);

            // The XP bar is the earned level's: nothing bought moves it.
            Assert.AreEqual(PlayerProgression.EarnedLevel.XpIntoLevel, PlayerProgression.Level.XpIntoLevel);
            Assert.AreEqual(PlayerProgression.EarnedLevel.XpForNextLevel, PlayerProgression.Level.XpForNextLevel);
        }

        [Test]
        public void TheFloorsRatchetTheEarnedLevelOnly()
        {
            Publish(Shipped());
            SaveWith(bought: 5);
            PlayerProgression.Invalidate();

            Assert.AreEqual(6, PlayerProgression.Level.Level);
            Assert.LessOrEqual(ProgressionStore.LevelHighWater, 1,
                               "a bought level fed back through the floor would be counted twice");

            // Read again: still six, not eleven.
            PlayerProgression.Invalidate();
            Assert.AreEqual(6, PlayerProgression.Level.Level);
        }

        [Test]
        public void ComposeNeverPassesTheCurvesCap()
        {
            var earned = new PlayerLevel(28, 0L, 0L, 100L, false);
            Assert.AreEqual(30, KeeperLadder.Compose(earned, 5, 30).Level);
            Assert.AreEqual(28, KeeperLadder.Compose(earned, 0, 30).Level);
            Assert.AreEqual(28, KeeperLadder.Compose(earned, -3, 30).Level);
        }

        // ---------------------------------------------------------- the purchase
        [Test]
        public void TheNextOfferIsTheLevelAboveWhereThePlayerStands()
        {
            Publish(Shipped());
            SaveWith(bought: 0);
            PlayerProgression.Invalidate();

            var offer = KeeperLedger.Next();
            Assert.IsTrue(offer.Sold);
            Assert.AreEqual(2, offer.Level);
            Assert.AreEqual(1, offer.Ordinal);
            Assert.AreEqual(Currency.Credits, offer.Currency);
            Assert.AreEqual(2000L, offer.Price);

            SaveWith(bought: 9);                    // standing at 10, the next is the first gem level
            PlayerProgression.Invalidate();
            offer = KeeperLedger.Next();
            Assert.AreEqual(11, offer.Level);
            Assert.AreEqual(10, offer.Ordinal);
            Assert.AreEqual(Currency.Gems, offer.Currency);
            Assert.AreEqual(100L, offer.Price);
        }

        [Test]
        public void BuyingDebitsThePriceUnderTheDerivedIdAndThenCounts()
        {
            Publish(Shipped());
            SaveWith(bought: 0, credits: 5000L);
            PlayerProgression.Invalidate();

            long before = PlayerProgression.Credits;
            Assert.AreEqual(KeeperBuy.Bought, KeeperLedger.TryBuy());

            Assert.AreEqual(before - 2000L, PlayerProgression.Credits);
            Assert.AreEqual(1, KeeperLedger.Bought);
            Assert.AreEqual(2, PlayerProgression.Level.Level);
            Assert.IsTrue(Wallet.Ledger(Currency.Credits).HasPending(SpendEntry.KeeperLevelId(1, 2)),
                          "the debit carries the ordinal and the level it reached");
            Assert.IsTrue(KeeperLedger.HasPendingDebit());

            // The next offer moved with it.
            Assert.AreEqual(3, KeeperLedger.Next().Level);
            Assert.AreEqual(2, KeeperLedger.Next().Ordinal);
        }

        [Test]
        public void ATooPoorWalletBuysNothingAndCountsNothing()
        {
            Publish(Shipped());
            SaveWith(bought: 0, credits: 1999L);
            PlayerProgression.Invalidate();

            Assert.AreEqual(KeeperBuy.TooPoor, KeeperLedger.TryBuy());
            Assert.AreEqual(0, KeeperLedger.Bought);
            Assert.AreEqual(1, PlayerProgression.Level.Level);
            Assert.AreEqual(1999L, PlayerProgression.Credits);
        }

        [Test]
        public void AtTheTopNothingIsOffered()
        {
            Publish(Shipped());
            var shipped = ProgressionRules.Table.KeeperLevels;
            SaveWith(bought: shipped.Top - 1, gems: 1_000_000L);
            PlayerProgression.Invalidate();

            Assert.AreEqual(shipped.Top, PlayerProgression.Level.Level);
            var offer = KeeperLedger.Next();
            Assert.IsFalse(offer.Sold);
            Assert.IsTrue(offer.AtTop);
            Assert.AreEqual(KeeperBuy.AtTop, KeeperLedger.TryBuy());
            Assert.AreEqual(1_000_000L, PlayerProgression.Gems, "nothing was charged");
        }

        [Test]
        public void ARefusedDebitTakesTheLevelBackAndEverythingBoughtAboveIt()
        {
            Publish(Shipped());
            SaveWith(bought: 4);
            PlayerProgression.Invalidate();
            Assert.AreEqual(5, PlayerProgression.Level.Level);

            // The server refuses the second purchase: the third and fourth were priced against a
            // level that was never reached, so they go with it.
            KeeperLedger.OnSpendRejected(Currency.Credits, SpendEntry.KeeperLevelId(2, 3));

            Assert.AreEqual(1, KeeperLedger.Bought);
            Assert.AreEqual(2, PlayerProgression.Level.Level);

            // A refusal for an ordinal this device never reached moves nothing.
            KeeperLedger.OnSpendRejected(Currency.Gems, SpendEntry.KeeperLevelId(7, 9));
            Assert.AreEqual(1, KeeperLedger.Bought);

            // And a debit that is not a keeper's is not this ledger's business.
            KeeperLedger.OnSpendRejected(Currency.Gems, SpendEntry.SeasonPassId("watch_0001"));
            Assert.AreEqual(1, KeeperLedger.Bought);
        }

        [Test]
        public void TheServersCountIsFoldedUpAlwaysAndDownOnlyWhenNothingIsInFlight()
        {
            Publish(Shipped());
            SaveWith(bought: 2, credits: 10_000L);
            PlayerProgression.Invalidate();

            // Up: another device bought more.
            KeeperLedger.ApplyServerState(carried: true, bought: 5);
            Assert.AreEqual(5, KeeperLedger.Bought);
            Assert.AreEqual(6, PlayerProgression.Level.Level);

            // Down, with nothing in flight: the server's word.
            KeeperLedger.ApplyServerState(carried: true, bought: 3);
            Assert.AreEqual(3, KeeperLedger.Bought);

            // Down, with a purchase pending here: the count stands until the server answers it.
            Assert.AreEqual(KeeperBuy.Bought, KeeperLedger.TryBuy());
            Assert.AreEqual(4, KeeperLedger.Bought);
            KeeperLedger.ApplyServerState(carried: true, bought: 3);
            Assert.AreEqual(4, KeeperLedger.Bought, "a debit in flight keeps its level");

            // A reply that does not carry the field says nothing.
            KeeperLedger.ApplyServerState(carried: false, bought: 0);
            Assert.AreEqual(4, KeeperLedger.Bought);

            // And a negative is not a count.
            KeeperLedger.ApplyServerState(carried: true, bought: -1);
            Assert.AreEqual(4, KeeperLedger.Bought);
        }

        // ---------------------------------------------------------------- the art
        /// <summary>
        /// The three discs a row wears are in the global set and on disk. Their addresses are
        /// written into <c>KeeperScreen</c> by hand, so <c>artnames.py</c> sees them; this is the
        /// half it cannot see - that the manifest preloads them, or the first row drawn is a
        /// white rectangle (invariant 7b).
        /// </summary>
        [Test]
        public void TheThreeNodeDiscsArePreloadedAndOnDisk()
        {
            var declared = new HashSet<string>();
            foreach (var request in AssetPipeline.AssetManifest.GlobalAssets())
                declared.Add(request.Address);

            string root = Path.Combine(TestJson.RepoRoot(), "Assets", "Game", "Art", "Ui");
            foreach (string key in new[] { "keeper_node_open", "keeper_node_locked", "keeper_node_crown" })
            {
                Assert.IsTrue(declared.Contains(AssetPipeline.AssetManifest.Ui(key)),
                              $"'{key}' is not in AssetManifest.UiSprites; a row would draw a white rectangle");
                Assert.IsTrue(System.IO.File.Exists(Path.Combine(root, key + ".png")),
                              $"'{key}.png' is not on disk; cut it with Tools/make_keeper_art.py");
            }
        }

        // --------------------------------------------------------------- the wire
        [Test]
        public void TheCountRidesTheWalletMapOnEveryLeg()
        {
            var dto = new SaveFileDto { wallet = new WalletDto { keeperLevelsBought = 7 } };

            // Wallet in and out.
            Wallet.LoadFrom(dto);
            Assert.AreEqual(7, Wallet.KeeperLevelsBought);
            var written = new SaveFileDto();
            Wallet.WriteInto(written);
            Assert.AreEqual(7, written.wallet.keeperLevelsBought);

            // The mapper both ways.
            var back = FirestoreSaveMapper.FromDocument(FirestoreSaveMapper.ToDocument(dto));
            Assert.AreEqual(7, back.wallet.keeperLevelsBought);

            // The merge takes the larger count.
            var other = new SaveFileDto { wallet = new WalletDto { keeperLevelsBought = 9 } };
            Assert.AreEqual(9, SaveMerge.Join(dto, other).wallet.keeperLevelsBought);
            Assert.AreEqual(9, SaveMerge.Join(other, dto).wallet.keeperLevelsBought);

            // The delta sees the field move.
            Assert.IsTrue(SaveDelta.Between(dto, other).ScalarsChanged);
            Assert.IsFalse(SaveDelta.Between(dto, new SaveFileDto { wallet = new WalletDto { keeperLevelsBought = 7 } }).ScalarsChanged);

            // An older file, or a negative, is nought.
            Wallet.LoadFrom(new SaveFileDto { wallet = new WalletDto { keeperLevelsBought = -2 } });
            Assert.AreEqual(0, Wallet.KeeperLevelsBought);
        }
    }
}
