using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Tasks;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The chests the keeper ladder pays on the way up (invariant 57d), the client half.
    ///
    /// <para>
    /// <b>The chest is rolled twice and a drift is a wrong payment</b> - the device rolls it,
    /// shows it and banks what is not currency; the server re-rolls it from the same account
    /// and level and grants its own figure. So both halves run
    /// <c>firebase/shared/grove-vectors.json</c>: this fixture through the shipped reader and
    /// <c>firebase/functions/test/keeper.mjs</c> through the compiled function, with
    /// <c>Tools/make_milestone_vectors.py</c> writing the cases from a copy that is neither side.
    /// Read through <see cref="TestJson"/> for invariant 29e's reason.
    /// </para>
    /// <para>
    /// <b>The rest is the ledger rule</b>: a milestone is waiting when reached and above the
    /// floor, only the earliest waiting one can be taken, taking it moves the floor first and
    /// pays currency as a claim under the derived id, a bought level passes a milestone exactly
    /// as an earned one does, and the floor rides the wallet map on every leg of the wire.
    /// </para>
    /// </summary>
    public sealed class KeeperMilestoneTests
    {
        // ------------------------------------------------------------- the file
        static Dictionary<string, object> _file;

        static Dictionary<string, object> File()
            => _file ??= TestJson.ReadShared("grove-vectors.json");

        static bool Flag(Dictionary<string, object> map, string key)
            => map.TryGetValue(key, out object v) && v is bool b && b;

        static KeeperMilestonesDto MilestonesDto(object raw)
        {
            if (raw == null) return null;
            var map = TestJson.Object(raw);
            var dto = new KeeperMilestonesDto();

            if (map.TryGetValue("rows", out object rows) && rows is List<object> list)
            {
                dto.rows = new KeeperMilestoneDto[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] == null) continue;
                    var row = TestJson.Object(list[i]);

                    // A fractional level is what a hand-edited file could carry; JsonUtility reads
                    // it as no whole number the reader accepts, which is what -1 says here.
                    int level;
                    try { level = TestJson.Int(row, "level", -1); }
                    catch (System.Exception) { level = -1; }

                    dto.rows[i] = new KeeperMilestoneDto { level = level, tier = TestJson.Str(row, "tier") };
                }
            }
            return dto;
        }

        /// <summary>The vector file's synthetic tiers, read through the real band reader.</summary>
        static Dictionary<string, ChestTier> VectorTiers()
        {
            var tiers = new Dictionary<string, ChestTier>(System.StringComparer.Ordinal);
            int rank = 0;

            foreach (object raw in TestJson.Children(File(), "keeperMilestoneChestTiers"))
            {
                var row = TestJson.Object(raw);
                var chest = TestJson.Child(row, "chest");
                var dto = new DailyChestEntryDto
                {
                    guaranteed = Bands(chest, "guaranteed"),
                    options = Options(chest, "options"),
                };

                var problems = new List<string>();
                string id = TestJson.Str(row, "id");
                var definition = DailyChestTable.ReadChest(dto, "vector tier " + id, problems);
                Assert.IsEmpty(problems, string.Join("; ", problems));
                tiers[id] = new ChestTier(id, ++rank, definition);
            }

            Assert.Greater(tiers.Count, 0, "the vector file has no milestone chest tiers");
            return tiers;
        }

        static DailyDropDto[] Bands(Dictionary<string, object> chest, string key)
        {
            var list = TestJson.Children(chest, key);
            var out_ = new DailyDropDto[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var band = TestJson.Object(list[i]);
                out_[i] = new DailyDropDto
                {
                    kind = TestJson.Str(band, "kind"),
                    min = TestJson.Int(band, "min"),
                    max = TestJson.Int(band, "max"),
                    item = TestJson.Str(band, "item", string.Empty),
                };
            }
            return out_;
        }

        static DailyOptionDto[] Options(Dictionary<string, object> chest, string key)
        {
            var list = TestJson.Children(chest, key);
            var out_ = new DailyOptionDto[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var band = TestJson.Object(list[i]);
                out_[i] = new DailyOptionDto
                {
                    kind = TestJson.Str(band, "kind"),
                    min = TestJson.Int(band, "min"),
                    max = TestJson.Int(band, "max"),
                    item = TestJson.Str(band, "item", string.Empty),
                    weight = TestJson.Int(band, "weight", 1),
                };
            }
            return out_;
        }

        static bool HoldsGrant(string currency, string id)
        {
            foreach (var entry in Wallet.Ledger(currency).PendingGrants)
                if (entry.Id == id) return true;
            return false;
        }

        static string Describe(IEnumerable<ChestDrop> drops)
        {
            var parts = new List<string>();
            foreach (var drop in drops)
                parts.Add(drop.Item.Length > 0
                    ? $"{ChestDropKinds.Id(drop.Kind)}:{drop.Item}={drop.Amount}"
                    : $"{ChestDropKinds.Id(drop.Kind)}={drop.Amount}");
            return parts.Count == 0 ? "(nothing)" : string.Join(",", parts);
        }

        static string Describe(List<object> drops)
        {
            var parts = new List<string>();
            foreach (object raw in drops)
            {
                var drop = TestJson.Object(raw);
                string item = TestJson.Str(drop, "item", string.Empty);
                parts.Add(string.IsNullOrEmpty(item)
                    ? $"{TestJson.Str(drop, "kind")}={TestJson.Int(drop, "amount")}"
                    : $"{TestJson.Str(drop, "kind")}:{item}={TestJson.Int(drop, "amount")}");
            }
            return parts.Count == 0 ? "(nothing)" : string.Join(",", parts);
        }

        // ----------------------------------------------------------- the table
        /// <summary>
        /// Publishes a whole reward table carrying the block under test through the shipped
        /// reader (<c>KeeperLevelTests.Publish</c>'s argument). The tasks block is left to its
        /// built-in default, which carries the same four tier ids the shipped file does.
        /// </summary>
        static void Publish(KeeperMilestonesDto milestones, KeeperLadderDto ladder = null, int maxLevel = 500)
        {
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                maxLevel = maxLevel,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 10,
                keeperLevels = ladder,
                keeperMilestones = milestones,
            };

            var problems = new List<string>();
            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, problems), string.Join("; ", problems));
            ProgressionRules.Publish(table);
        }

        static KeeperMilestonesDto Rows(params (int level, string tier)[] rows)
        {
            var dto = new KeeperMilestonesDto { rows = new KeeperMilestoneDto[rows.Length] };
            for (int i = 0; i < rows.Length; i++)
                dto.rows[i] = new KeeperMilestoneDto { level = rows[i].level, tier = rows[i].tier };
            return dto;
        }

        static Dictionary<string, object> ShippedProgression()
        {
            var text = System.IO.File.ReadAllText(Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets",
                                                               "Content", "progression.json"));
            return TestJson.Object(TestJson.Parse(text));
        }

        static KeeperMilestonesDto Shipped()
        {
            var map = ShippedProgression();
            Assert.IsTrue(map.ContainsKey("keeperMilestones"), "progression.json carries no keeperMilestones block");
            return MilestonesDto(map["keeperMilestones"]);
        }

        /// <summary>A save standing at <paramref name="bought"/> levels above earned, with the floor at <paramref name="claimed"/>.</summary>
        static void SaveWith(int bought, int claimed = 0)
        {
            var dto = new SaveFileDto { wallet = new WalletDto { keeperLevelsBought = bought, keeperMilestonesClaimed = claimed } };
            Wallet.LoadFrom(dto);
            PlayerProgression.Invalidate();
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
        public void EveryChestVectorRollsTheWayTheServerRolls()
        {
            var tiers = VectorTiers();
            var failures = new List<string>();
            int seen = 0;

            foreach (object raw in TestJson.Children(File(), "keeperMilestoneChestCases"))
            {
                var c = TestJson.Object(raw);
                string playerKey = TestJson.Str(c, "playerKey", string.Empty);
                int level = TestJson.Int(c, "level");

                var seed = ChestSeed.ForSubject(playerKey, KeeperMilestoneLedger.SeedTag,
                                                KeeperMilestoneLedger.Subject(level));
                string got = Describe(tiers[TestJson.Str(c, "tier")].Chest.Roll(seed));
                string want = Describe(TestJson.Children(c, "drops"));

                if (got != want) failures.Add($"'{TestJson.Str(c, "name")}': expected {want}, got {got}");
                seen++;
            }

            Assert.Greater(seen, 50, "the vector file has too few milestone cases to mean anything");
            Assert.IsEmpty(failures,
                           "the client no longer rolls milestone chests the way the server does. If this " +
                           "change was intended, regenerate firebase/shared/grove-vectors.json with " +
                           "Tools/make_milestone_vectors.py and make the same change in " +
                           "firebase/functions/src/keeper.ts - otherwise the server will grant a different " +
                           "amount than the game showed.\n" + string.Join("\n", failures));
        }

        [Test]
        public void TheSubjectIsTheLevelAndNeighbouringLevelsRollApart()
        {
            var tiers = VectorTiers();
            string a = Describe(tiers["royal"].Chest.Roll(ChestSeed.ForSubject("uid", KeeperMilestoneLedger.SeedTag, KeeperMilestoneLedger.Subject(4))));
            string b = Describe(tiers["royal"].Chest.Roll(ChestSeed.ForSubject("uid", KeeperMilestoneLedger.SeedTag, KeeperMilestoneLedger.Subject(5))));
            Assert.AreNotEqual(a, b, "a seed that dropped the level would pay every milestone the same chest");
            Assert.AreEqual("4", KeeperMilestoneLedger.Subject(4));
        }

        [Test]
        public void EveryClaimIdParsesTheWayTheServerParses()
        {
            int seen = 0;
            foreach (object raw in TestJson.Children(File(), "keeperMilestoneClaimIds"))
            {
                var c = TestJson.Object(raw);
                string id = TestJson.Str(c, "id");
                bool invalid = Flag(c, "invalid");

                bool ok = GrantEntry.TryParseKeeperMilestoneId(id, out int level, out string currency);
                if (ok && level > ProgressionTable.MaxSupportedLevel) ok = false;   // the server's ceiling, shared
                Assert.AreEqual(!invalid, ok, $"'{id}'");
                if (ok)
                {
                    Assert.AreEqual(TestJson.Int(c, "level"), level, id);
                    Assert.AreEqual(TestJson.Str(c, "currency"), currency, id);
                    Assert.AreEqual(id, GrantEntry.KeeperMilestoneId(level, currency), "round trip");
                }
                seen++;
            }
            Assert.Greater(seen, 8);
        }

        [Test]
        public void EveryRefusedBlockResolvesToNothing()
        {
            var tiers = VectorTiers();
            var tasks = TaskTable.Default;                 // carries wood, silver, gold, royal
            Assert.IsNotNull(tasks.Tier("wood"));
            Assert.IsNull(tasks.Tier("diamond"));

            int seen = 0;
            foreach (object raw in TestJson.Children(File(), "keeperMilestoneRejected"))
            {
                var c = TestJson.Object(raw);
                string name = TestJson.Str(c, "name");
                object block = c.TryGetValue("block", out object b) ? b : null;

                // A block that is not an object is what JsonUtility would read as an empty one.
                var dto = block is Dictionary<string, object> ? MilestonesDto(block) : new KeeperMilestonesDto();
                var problems = new List<string>();
                var table = KeeperMilestoneTable.Resolve(dto, tasks, ProgressionTable.MaxSupportedLevel, problems);

                Assert.IsFalse(table.Pays, name);
                Assert.AreEqual(0, table.Last, name);
                seen++;
            }
            Assert.Greater(seen, 8);
            Assert.AreEqual(4, tiers.Count);
        }

        [Test]
        public void TheShippedBlockResolvesAndIsTheOneTheVectorsPin()
        {
            var map = ShippedProgression();
            var tiers = new HashSet<string>();
            foreach (object raw in TestJson.Children(TestJson.Child(map, "tasks"), "tiers"))
                tiers.Add(TestJson.Str(TestJson.Object(raw), "id"));

            var problems = new List<string>();
            var shipped = KeeperMilestoneTable.Resolve(Shipped(), TaskTable.Default, ProgressionTable.MaxSupportedLevel, problems);
            Assert.IsTrue(shipped.Pays, string.Join("; ", problems));

            // Every row names a tier the shipped tasks block defines, not only the default's.
            foreach (var row in shipped.Rows)
                Assert.IsTrue(tiers.Contains(row.Tier.Id), $"level {row.Level} pays '{row.Tier.Id}', which progression.json's tasks block does not define");

            var pinned = MilestonesDto(File()["keeperMilestoneShipped"]);
            Assert.IsNotNull(pinned?.rows, "re-run Tools/make_milestone_vectors.py after retuning the block");
            Assert.AreEqual(pinned.rows.Length, shipped.Rows.Count, "re-run Tools/make_milestone_vectors.py after retuning the block");
            for (int i = 0; i < pinned.rows.Length; i++)
            {
                Assert.AreEqual(pinned.rows[i].level, shipped.Rows[i].Level);
                Assert.AreEqual(pinned.rows[i].tier, shipped.Rows[i].Tier.Id);
            }

            // Every few levels, never the humblest tier, and never above the price ladder's top
            // by more than the ladder can show - so every chest is on the page.
            int last = 0;
            foreach (var row in shipped.Rows)
            {
                Assert.LessOrEqual(row.Level - last, 5, $"level {row.Level}: a milestone every three to five levels");
                Assert.AreNotEqual("wood", row.Tier.Id, $"level {row.Level} pays the humblest chest in the game");
                last = row.Level;
            }
            var ladder = KeeperLadder.Resolve(KeeperLevelTestsLadder(map), new List<string>());
            Assert.LessOrEqual(shipped.Last, ladder.Top, "a milestone above the ladder's top stands above the page's last disc");
        }

        static KeeperLadderDto KeeperLevelTestsLadder(Dictionary<string, object> map)
        {
            var block = TestJson.Object(map["keeperLevels"]);
            var dto = new KeeperLadderDto { top = TestJson.Int(block, "top", -1) };
            var rows = TestJson.Children(block, "anchors");
            dto.anchors = new KeeperAnchorDto[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                var row = TestJson.Object(rows[i]);
                dto.anchors[i] = new KeeperAnchorDto
                {
                    level = TestJson.Int(row, "level"),
                    currency = TestJson.Str(row, "currency"),
                    price = TestJson.Int(row, "price"),
                };
            }
            return dto;
        }

        [Test]
        public void ADefaultTablePaysNothing()
        {
            Assert.IsFalse(ProgressionTable.Default.KeeperMilestones.Pays);
            Assert.AreEqual(0, KeeperMilestoneLedger.Waiting);
            Assert.AreEqual(0, KeeperMilestoneLedger.NextWaiting);
            Assert.IsFalse(KeeperMilestoneLedger.TryCollect(4, out _));
        }

        [Test]
        public void ARowAboveTheCurveWithdrawsTheBlock()
        {
            Publish(Rows((4, "silver"), (40, "gold")), maxLevel: 30);
            Assert.IsFalse(ProgressionRules.Table.KeeperMilestones.Pays);
        }

        // ------------------------------------------------------------ the rule
        [Test]
        public void WaitingIsEveryReachedMilestoneAboveTheFloorAndTheEarliestIsNext()
        {
            Publish(Rows((4, "silver"), (8, "silver"), (12, "gold")));

            SaveWith(bought: 0);
            Assert.AreEqual(0, KeeperMilestoneLedger.Waiting, "level 1 has reached nothing");
            Assert.IsFalse(KeeperMilestoneLedger.IsWaiting(4));

            SaveWith(bought: 8);                                   // standing at 9
            Assert.AreEqual(2, KeeperMilestoneLedger.Waiting);
            Assert.AreEqual(4, KeeperMilestoneLedger.NextWaiting);
            Assert.IsTrue(KeeperMilestoneLedger.IsWaiting(4));
            Assert.IsTrue(KeeperMilestoneLedger.IsWaiting(8));
            Assert.IsFalse(KeeperMilestoneLedger.IsWaiting(12));
            Assert.IsFalse(KeeperMilestoneLedger.IsWaiting(9), "nine is no milestone");

            Assert.IsTrue(KeeperMilestoneLedger.CanCollect(4));
            Assert.IsFalse(KeeperMilestoneLedger.CanCollect(8), "only the earliest waiting one can be taken");
            Assert.IsFalse(KeeperMilestoneLedger.CanCollect(12), "not reached");

            SaveWith(bought: 8, claimed: 4);
            Assert.AreEqual(1, KeeperMilestoneLedger.Waiting);
            Assert.AreEqual(8, KeeperMilestoneLedger.NextWaiting);
            Assert.IsTrue(KeeperMilestoneLedger.CanCollect(8));
        }

        [Test]
        public void CollectingMovesTheFloorFirstAndPaysCurrencyAsAClaim()
        {
            Publish(Rows((4, "silver"), (8, "silver")));
            SaveWith(bought: 8);

            var expected = KeeperMilestoneLedger.Preview(4);
            Assert.Greater(expected.Count, 0, "the default silver chest pays something");
            long creditsBefore = PlayerProgression.Credits;
            long gemsBefore = PlayerProgression.Gems;
            int raised = 0;
            void OnChanged() => raised++;
            KeeperMilestoneLedger.Changed += OnChanged;

            try
            {
                Assert.IsFalse(KeeperMilestoneLedger.TryCollect(8, out _), "eight is not the earliest waiting");
                Assert.IsTrue(KeeperMilestoneLedger.TryCollect(4, out var drops));
                Assert.AreEqual(Describe(expected), Describe(drops), "what was previewed is what was paid");

                Assert.AreEqual(4, KeeperMilestoneLedger.ClaimedThrough);
                Assert.AreEqual(4, Wallet.KeeperMilestonesClaimed);
                Assert.AreEqual(1, raised);
                Assert.AreEqual(1, KeeperMilestoneLedger.Waiting);
                Assert.AreEqual(8, KeeperMilestoneLedger.NextWaiting);

                // Currency reached the wallet as a claim under the derived id. (The season's note
                // needs a live season, which no offline fixture has; `SeasonLedger.NoteChest` is
                // one line and the call is read in the source.)
                long credits = 0, gems = 0;
                foreach (var drop in drops)
                {
                    if (drop.Kind == ChestDropKind.Credits) credits += drop.Amount;
                    if (drop.Kind == ChestDropKind.Gems) gems += drop.Amount;
                }
                Assert.AreEqual(creditsBefore + credits, PlayerProgression.Credits);
                Assert.AreEqual(gemsBefore + gems, PlayerProgression.Gems);
                if (credits > 0)
                    Assert.IsTrue(HoldsGrant(Currency.Credits, GrantEntry.KeeperMilestoneId(4, Currency.Credits)),
                                  "the credits are a claim under the milestone's own id");
                if (gems > 0)
                    Assert.IsTrue(HoldsGrant(Currency.Gems, GrantEntry.KeeperMilestoneId(4, Currency.Gems)),
                                  "the gems are a claim under the milestone's own id");
                // Twice is nothing.
                Assert.IsFalse(KeeperMilestoneLedger.TryCollect(4, out _));
                Assert.AreEqual(1, raised);
            }
            finally
            {
                KeeperMilestoneLedger.Changed -= OnChanged;
            }
        }

        [Test]
        public void ABoughtLevelPassesAMilestoneExactlyAsAnEarnedOne()
        {
            Publish(Rows((2, "silver")), ladder: new KeeperLadderDto
            {
                top = 10,
                anchors = new[] { new KeeperAnchorDto { level = 2, currency = "credits", price = 100 },
                                  new KeeperAnchorDto { level = 10, currency = "credits", price = 900 } },
            });
            SaveWith(bought: 0);
            Wallet.Ledger(Currency.Credits).GrantLocally(500L);
            PlayerProgression.Invalidate();

            Assert.AreEqual(0, KeeperMilestoneLedger.Waiting);
            Assert.AreEqual(KeeperBuy.Bought, KeeperLedger.TryBuy());
            Assert.AreEqual(2, PlayerProgression.Level.Level);
            Assert.AreEqual(1, KeeperMilestoneLedger.Waiting, "buying level 2 reached the milestone at 2");
            Assert.IsTrue(KeeperMilestoneLedger.CanCollect(2));
        }

        [Test]
        public void ALevelTakenBackHidesNothingAlreadyPaid()
        {
            Publish(Rows((4, "silver"), (8, "silver")));
            SaveWith(bought: 8, claimed: 8);
            Assert.AreEqual(0, KeeperMilestoneLedger.Waiting);

            // The server refuses the purchase behind level 5: the level falls to 4.
            KeeperLedger.OnSpendRejected(Currency.Credits, SpendEntry.KeeperLevelId(4, 5));
            Assert.AreEqual(4, PlayerProgression.Level.Level);
            Assert.AreEqual(8, KeeperMilestoneLedger.ClaimedThrough, "the floor never falls");
            Assert.AreEqual(0, KeeperMilestoneLedger.Waiting, "a chest already opened is not owed again");
            Assert.IsFalse(KeeperMilestoneLedger.TryCollect(8, out _));
        }

        [Test]
        public void TheClaimForACollectedChestCarriesTheChestOverlaysShape()
        {
            Publish(Rows((4, "gold")));
            SaveWith(bought: 4);

            var claim = ChestClaim.ForKeeperMilestone(4);
            Assert.IsTrue(claim.IsValid);
            Assert.AreEqual("gold", claim.Tier.Id);
            Assert.IsTrue(claim.TryClaim(out var drops));
            Assert.Greater(drops.Count, 0);
            Assert.AreEqual(4, KeeperMilestoneLedger.ClaimedThrough);

            Assert.IsFalse(ChestClaim.ForKeeperMilestone(5).IsValid, "five is no milestone");
            Assert.IsFalse(claim.TryClaim(out _), "the second device to arrive is answered by the overlay closing");
        }

        // --------------------------------------------------------------- the wire
        [Test]
        public void TheFloorRidesTheWalletMapOnEveryLeg()
        {
            var dto = new SaveFileDto { wallet = new WalletDto { keeperMilestonesClaimed = 12 } };

            Wallet.LoadFrom(dto);
            Assert.AreEqual(12, Wallet.KeeperMilestonesClaimed);
            var written = new SaveFileDto();
            Wallet.WriteInto(written);
            Assert.AreEqual(12, written.wallet.keeperMilestonesClaimed);

            var back = FirestoreSaveMapper.FromDocument(FirestoreSaveMapper.ToDocument(dto));
            Assert.AreEqual(12, back.wallet.keeperMilestonesClaimed);

            var other = new SaveFileDto { wallet = new WalletDto { keeperMilestonesClaimed = 16 } };
            Assert.AreEqual(16, SaveMerge.Join(dto, other).wallet.keeperMilestonesClaimed);
            Assert.AreEqual(16, SaveMerge.Join(other, dto).wallet.keeperMilestonesClaimed);

            Assert.IsTrue(SaveDelta.Between(dto, other).ScalarsChanged);
            Assert.IsFalse(SaveDelta.Between(dto, new SaveFileDto { wallet = new WalletDto { keeperMilestonesClaimed = 12 } }).ScalarsChanged);

            Wallet.LoadFrom(new SaveFileDto { wallet = new WalletDto { keeperMilestonesClaimed = -3 } });
            Assert.AreEqual(0, Wallet.KeeperMilestonesClaimed);

            // Raised locally, never lowered.
            Wallet.RaiseKeeperMilestonesClaimed(8);
            Wallet.RaiseKeeperMilestonesClaimed(4);
            Assert.AreEqual(8, Wallet.KeeperMilestonesClaimed);
        }

        [Test]
        public void EveryShippedTiersClosedIconIsPreloaded()
        {
            var declared = new HashSet<string>();
            foreach (var request in AssetPipeline.AssetManifest.GlobalAssets())
                declared.Add(request.Address);

            var shipped = KeeperMilestoneTable.Resolve(Shipped(), TaskTable.Default, ProgressionTable.MaxSupportedLevel, new List<string>());
            foreach (var row in shipped.Rows)
                Assert.IsTrue(declared.Contains(AssetPipeline.AssetManifest.ArtRoot + row.Tier.Icon),
                              $"'{row.Tier.Icon}' is not in the global preload set; a milestone row would draw a white rectangle (7b)");
        }
    }
}
