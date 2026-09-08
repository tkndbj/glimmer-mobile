using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Modes;
using GlimmerGrove.Persistence;
using GlimmerGrove.Utilities;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The utilities a player carries into a siege.
    ///
    /// <para>
    /// Three properties carry the feature and are pinned hardest here. What a player is holding
    /// must survive being merged across devices in any order any number of times, which is
    /// invariant 11b and is proved against plain integers. A utility must never be able to improve
    /// a run's <em>grade</em>, which is invariant 39 and is the reason nothing about them is
    /// adjudicated — proved as arithmetic over <c>SiegeTuning</c>'s own exchange rate rather than
    /// asserted about one board. And a utility must never be charged for unless it landed, which
    /// is invariant 23's rule about a continue that does not continue, applied to a consumable
    /// somebody may have paid gems for.
    /// </para>
    /// </summary>
    public sealed class UtilityTests
    {
        // =================================================================== the stock
        static UtilityStockDto Row(string id, int earned, int spent)
            => new UtilityStockDto { id = id, earned = earned, spent = spent };

        static UtilityStock Stock(params UtilityStockDto[] rows)
        {
            var stock = new UtilityStock();
            stock.LoadFrom(rows);
            return stock;
        }

        [Test]
        public void WhatIsInHandIsEarnedMinusSpent()
        {
            var stock = Stock(Row("firepot", 5, 2));

            Assert.AreEqual(3, stock.Held("firepot"));
            Assert.AreEqual(0, stock.Held("mending"));
        }

        /// <summary>
        /// The clamp that is not decoration: two devices can each spend the last one before either
        /// has synced, so a merged file legitimately holds more spent than earned. Answering "none
        /// left" costs nothing; reaching into either counter to balance the identity would break
        /// the monotonicity the join rests on.
        /// </summary>
        [Test]
        public void MoreSpentThanEarnedReadsAsNoneLeftRatherThanAsADebt()
        {
            var stock = Stock(Row("firepot", 2, 5));

            Assert.AreEqual(0, stock.Held("firepot"));
            Assert.AreEqual(5, stock.SpentOf("firepot"), "the counter is not repaired");
        }

        [Test]
        public void UsingNeverTakesMoreThanIsInHand()
        {
            var stock = Stock(Row("firepot", 3, 0));

            Assert.AreEqual(3, stock.Use("firepot", 9));
            Assert.AreEqual(0, stock.Held("firepot"));
            Assert.AreEqual(0, stock.Use("firepot", 1));
        }

        /// <summary>
        /// The property the whole section exists for: a per-id maximum on each counter, so the
        /// larger side is always the one that has heard about more.
        /// </summary>
        [Test]
        public void TheJoinTakesTheLargerOfEachCounterPerId()
        {
            var joined = UtilityStock.Join(
                new[] { Row("firepot", 4, 1), Row("mending", 2, 2) },
                new[] { Row("firepot", 2, 3), Row("surge", 1, 0) });

            var stock = Stock(joined);

            Assert.AreEqual(4, stock.EarnedOf("firepot"));
            Assert.AreEqual(3, stock.SpentOf("firepot"));
            Assert.AreEqual(2, stock.EarnedOf("mending"));
            Assert.AreEqual(1, stock.EarnedOf("surge"), "an id only one device knows is kept");
        }

        [Test]
        public void TheJoinIsIdempotentCommutativeAndAssociative()
        {
            var a = new[] { Row("firepot", 4, 1) };
            var b = new[] { Row("firepot", 2, 3), Row("surge", 5, 0) };
            var c = new[] { Row("mending", 1, 0), Row("surge", 3, 2) };

            string Fold(UtilityStockDto[] rows)
            {
                var parts = new List<string>();
                foreach (var row in rows) parts.Add($"{row.id}:{row.earned}:{row.spent}");
                return string.Join(",", parts);
            }

            string abc = Fold(UtilityStock.Join(UtilityStock.Join(a, b), c));

            Assert.AreEqual(abc, Fold(UtilityStock.Join(a, UtilityStock.Join(b, c))));
            Assert.AreEqual(abc, Fold(UtilityStock.Join(UtilityStock.Join(c, b), a)));
            Assert.AreEqual(abc, Fold(UtilityStock.Join(UtilityStock.Join(a, b), UtilityStock.Join(b, c))));

            // Idempotent: joining a merged file against itself changes nothing, which is what
            // makes a re-uploaded save safe.
            var once = UtilityStock.Join(UtilityStock.Join(a, b), c);
            Assert.AreEqual(abc, Fold(UtilityStock.Join(once, once)));
        }

        /// <summary>
        /// The inexactness the join buys, stated as a test so nobody 'fixes' it into addition.
        ///
        /// Two devices offline from the same five, spending two and three, merge to three spent
        /// rather than five — two uses forgiven. Adding them would not be idempotent, so a
        /// re-uploaded save or a sync retried after a dropped reply would charge them again, which
        /// is the failure that actually loses a player something they paid gems for.
        /// </summary>
        [Test]
        public void TwoDevicesSpendingOfflineAreForgivenRatherThanChargedTwice()
        {
            var joined = UtilityStock.Join(new[] { Row("firepot", 5, 2) },
                                           new[] { Row("firepot", 5, 3) });

            Assert.AreEqual(2, Stock(joined).Held("firepot"));
        }

        [Test]
        public void ARowThatSaysNothingIsNeverWritten()
        {
            var stock = Stock(Row("firepot", 3, 0), Row("surge", 0, 0));
            var rows = stock.Write();

            Assert.AreEqual(1, rows.Length);
            Assert.AreEqual("firepot", rows[0].id);
        }

        /// <summary>
        /// Sorted, and it is not tidiness: <c>SaveChecksum</c> hashes the serialised file and
        /// <c>SaveDelta</c> walks these in order, so dictionary order would make an unchanged save
        /// look changed on every launch and push a write for nothing, for ever.
        /// </summary>
        [Test]
        public void RowsAreWrittenSortedById()
        {
            var rows = Stock(Row("surge", 1, 0), Row("firepot", 1, 0), Row("mending", 1, 0)).Write();

            Assert.AreEqual("firepot", rows[0].id);
            Assert.AreEqual("mending", rows[1].id);
            Assert.AreEqual("surge", rows[2].id);
        }

        /// <summary>
        /// A duplicated row is a malformed file rather than two things in one place (invariant
        /// 11a), and it is read the way two devices holding it would have been merged rather than
        /// by whichever row came last.
        /// </summary>
        [Test]
        public void ADuplicatedRowIsReadAsAJoinRatherThanAsWhicheverCameLast()
        {
            var stock = Stock(Row("firepot", 2, 2), Row("firepot", 5, 1));

            Assert.AreEqual(5, stock.EarnedOf("firepot"));
            Assert.AreEqual(2, stock.SpentOf("firepot"));
        }

        [Test]
        public void AnUnknownIdIsCarriedThroughRatherThanConfiscated()
        {
            var joined = UtilityStock.Join(new[] { Row("from_a_newer_build", 3, 1) },
                                           new UtilityStockDto[0]);

            Assert.AreEqual(1, joined.Length);
            Assert.AreEqual("from_a_newer_build", joined[0].id);
        }

        // =================================================================== the catalog
        static UtilitiesDto Authored(params UtilityDto[] items)
            => new UtilitiesDto { items = items };

        static UtilityDto Entry(string id, string kind, int order,
                                int magnitude = 10, int reach = 20, int price = 5, int max = 9)
            => new UtilityDto
            {
                id = id, kind = kind, magnitude = magnitude, reach = reach,
                gemPrice = price, maxHeld = max, order = order,
            };

        [Test]
        public void TheBuiltInCatalogCarriesThreeUtilitiesInBarOrder()
        {
            var items = UtilityCatalog.Default.Items;

            Assert.AreEqual(3, items.Count);
            for (int i = 1; i < items.Count; i++)
                Assert.Less(items[i - 1].Order, items[i].Order);
        }

        /// <summary>
        /// Three silhouettes and three jobs. Stated as a test because the whole bar rests on
        /// telling them apart at a glance, and a fourth utility that duplicated a kind would be
        /// the mirror invariant 26g withdrew.
        /// </summary>
        [Test]
        public void EveryShippedUtilityDoesSomethingTheOthersCannot()
        {
            var kinds = new HashSet<UtilityKind>();

            foreach (var item in UtilityCatalog.Default.Items)
                Assert.IsTrue(kinds.Add(item.Kind), $"'{item.Id}' repeats a kind");
        }

        [Test]
        public void AnUnknownKindIsSkippedRatherThanLosingTheCatalog()
        {
            var problems = new List<string>();
            var catalog = UtilityCatalog.Resolve(
                Authored(Entry("firepot", "blast", 1), Entry("future", "teleport", 2)), problems);

            Assert.AreEqual(1, catalog.Count);
            Assert.IsNotNull(catalog.Find("firepot"));
            CollectionAssert.IsNotEmpty(problems);
        }

        [Test]
        public void ABlastWithNoReachIsRefused()
        {
            var problems = new List<string>();
            var catalog = UtilityCatalog.Resolve(
                Authored(Entry("firepot", "blast", 1, reach: 0)), problems);

            Assert.AreSame(UtilityCatalog.Default, catalog);
            CollectionAssert.IsNotEmpty(problems);
        }

        /// <summary>
        /// The ladder is authored and never derived, which is invariant 16j's rule: a duplicated
        /// or unauthored rung would reorder the bar under a player between one content push and
        /// the next.
        /// </summary>
        [Test]
        public void TwoUtilitiesCannotShareABarPosition()
        {
            var problems = new List<string>();
            var catalog = UtilityCatalog.Resolve(
                Authored(Entry("firepot", "blast", 1), Entry("mending", "mend", 1)), problems);

            Assert.AreSame(UtilityCatalog.Default, catalog);
        }

        [Test]
        public void TwoEntriesCannotShareAnId()
        {
            var problems = new List<string>();
            var catalog = UtilityCatalog.Resolve(
                Authored(Entry("firepot", "blast", 1), Entry("firepot", "mend", 2)), problems);

            Assert.AreSame(UtilityCatalog.Default, catalog);
        }

        [Test]
        public void AnAbsentBlockLeavesTheBuiltInCatalogStanding()
        {
            var problems = new List<string>();

            Assert.AreSame(UtilityCatalog.Default, UtilityCatalog.Resolve(null, problems));
            CollectionAssert.IsEmpty(problems, "absent is not an error");
        }

        /// <summary>
        /// A loc key is derived from the id and never authored, which is invariant 5a's rule: it
        /// is what lets anything holding an id name the thing without reading the catalog.
        /// </summary>
        [Test]
        public void EverythingAPlayerSeesIsDerivedFromTheId()
        {
            var item = UtilityCatalog.Default.Find("firepot");

            Assert.AreEqual("utility.firepot.name", item.NameKey);
            Assert.AreEqual("utility.firepot.note", item.NoteKey);
            Assert.AreEqual("Ui/Utility/firepot", item.Art);
        }

        // =================================================================== the grade
        /// <summary>
        /// Invariant 39, as arithmetic. <c>PerfectMatch</c> is the most one match can ever
        /// deliver, so <c>ceil(damage / PerfectMatch)</c> is the fewest matches that could have
        /// delivered the same — which makes the charge a floor on what the utility saved.
        /// </summary>
        [Test]
        public void AUtilityIsChargedTheFewestMatchesThatCouldHaveDoneTheSameWork()
        {
            int per = SiegeTuning.PerfectMatch;

            Assert.AreEqual(0, SiegeUtility.MatchesFor(0));
            Assert.AreEqual(1, SiegeUtility.MatchesFor(1));
            Assert.AreEqual(1, SiegeUtility.MatchesFor(per));
            Assert.AreEqual(2, SiegeUtility.MatchesFor(per + 1));
            Assert.AreEqual(3, SiegeUtility.MatchesFor(per * 3));
        }

        /// <summary>
        /// The property that closes the leak the whole feature would otherwise open: stars derive
        /// credits, credits are a grove's worth, and a grove's worth reaches a public board
        /// (invariant 19a). Swept over every damage a shipped utility could deliver rather than
        /// asserted at one point, because "usually neutral" is not a bound.
        /// </summary>
        [Test]
        public void NoAmountOfDamageIsEverCheaperThroughAUtilityThanThroughPlaying()
        {
            for (int damage = 1; damage <= SiegeTuning.PerfectMatch * 40; damage++)
            {
                int charged = SiegeUtility.MatchesFor(damage);

                Assert.GreaterOrEqual(charged * SiegeTuning.PerfectMatch, damage,
                    $"{damage} damage was charged {charged} matches, which could not have "
                    + "delivered it — a player could buy a star");
            }
        }

        [Test]
        public void FuelIsPricedAtTheMostItCouldEverBeWorth()
        {
            // Ten tenths is one shot; a shot is ShotDamage, doubled against a raider that ward
            // is strong against. Anything less would under-charge, which is the unsafe direction.
            Assert.AreEqual(SiegeTuning.ShotDamage * SiegeTuning.WeakMultiplier,
                            SiegeUtility.DamageOfFuel(10));
            Assert.AreEqual(0, SiegeUtility.DamageOfFuel(0));
        }

        // =================================================================== the board
        static SiegeLayout Field()
        {
            // Four colours, four wards, one small wave — enough to have something to burn and a
            // line to mend, and small enough that nothing here depends on a shipped level.
            var rows = new[]
            {
                "rgbyrg",
                "bygrby",
                "grbygr",
                "ybgrby",
            };

            return new SiegeLayout(
                ProtoGrid.TryRead(rows, 6, 4, SiegeLayout.Letters, out var grid, out _)
                    ? grid : null,
                "rgby", "rgby", new[] { "rrgg" });
        }

        static SiegeBoard Board()
        {
            var layout = Field();
            Assert.IsNull(layout.Fault, layout.Fault);
            return SiegeBoard.Build(layout);
        }

        /// <summary>
        /// Raiders only exist once a wave has mustered, so every board test walks the clock on
        /// first. Real seconds rather than one long step: <c>Advance</c> clamps a step to a
        /// quarter-second so a resumed app cannot teleport a wave into the line.
        /// </summary>
        static void Settle(SiegeBoard board, float seconds)
        {
            for (float t = 0f; t < seconds; t += .1f) board.Advance(.1f);
        }

        [Test]
        public void ABlastBurnsWhatIsNearItAndReportsWhatWasAbsorbed()
        {
            var board = Board();
            Settle(board, SiegeTuning.FirstWaveAfter + 1f);

            var raider = board.Raiders[0];
            float lane = raider.Lane / (float)(SiegeTuning.Lanes - 1);

            var strikes = new List<SiegeStrike>();
            int absorbed = board.Blast(lane, raider.March, .5f, 5, strikes);

            Assert.Greater(absorbed, 0);
            CollectionAssert.IsNotEmpty(strikes);
        }

        /// <summary>
        /// Overkill is not work the player was spared, so it is not charged for. A firepot that
        /// finishes a raider with three health left has saved three health's worth of matches and
        /// no more.
        /// </summary>
        [Test]
        public void OverkillIsNotCountedAsDamageDelivered()
        {
            var board = Board();
            Settle(board, SiegeTuning.FirstWaveAfter + 1f);

            var raider = board.Raiders[0];
            raider.Health = 3;

            float lane = raider.Lane / (float)(SiegeTuning.Lanes - 1);
            var strikes = new List<SiegeStrike>();

            // A radius small enough to take this raider and, at worst, whoever is standing on it.
            int absorbed = board.Blast(lane, raider.March, .02f, 9_999, strikes);

            Assert.LessOrEqual(absorbed, 3 + SiegeTuning.BruteHealth,
                "absorbed damage is bounded by the health that was actually there");
            Assert.Greater(absorbed, 0);
        }

        [Test]
        public void ABlastThatReachesNobodyIsRefusedAndCostsNothing()
        {
            var board = Board();
            Settle(board, SiegeTuning.FirstWaveAfter + 1f);

            var item = UtilityCatalog.Default.Find("firepot");

            // The foot of the hill, before anything has walked down it.
            var use = SiegeUtility.Apply(board, item, SiegeAim.OnTheHill(.5f, 1f),
                                         new List<SiegeStrike>());

            Assert.IsFalse(use.Landed);
            Assert.AreEqual(0, use.Matches);
        }

        /// <summary>
        /// The rule that protects an existing invariant rather than adding one.
        /// <c>SiegeBoard.Stranded</c> is a certainty that decides whether money changes hands
        /// (invariant 28f), and it is only allowed to say "no purchase rescues this" because
        /// nothing can put a ward back up.
        /// </summary>
        [Test]
        public void AMendingCannotRaiseAFallenWard()
        {
            var board = Board();
            var ward = board.Wards[0];

            ward.Health = 0;
            ward.Alive = false;

            Assert.AreEqual(0, board.Mend(0, 5));
            Assert.IsFalse(ward.Alive);
            Assert.AreEqual(0, board.RoomForHealth(0));
        }

        [Test]
        public void AMendingChargesTheGradeNothingBecauseItDeliversNoDamage()
        {
            var board = Board();
            board.Wards[0].Health = 1;

            var item = UtilityCatalog.Default.Find("mending");
            var use = SiegeUtility.Apply(board, item, SiegeAim.AtWard(0), null);

            Assert.IsTrue(use.Landed);
            Assert.AreEqual(0, use.Matches, "a mending buys a finish, never a grade");
            Assert.Greater(board.Wards[0].Health, 1);
        }

        [Test]
        public void AMendingOnAnUnhurtWardIsRefusedRatherThanSpentForNothing()
        {
            var board = Board();
            var item = UtilityCatalog.Default.Find("mending");

            Assert.IsFalse(SiegeUtility.Would(board, item, SiegeAim.AtWard(0)));
            Assert.IsFalse(SiegeUtility.Apply(board, item, SiegeAim.AtWard(0), null).Landed);
        }

        [Test]
        public void ASurgeFillsAWardAndIsChargedForWhatThatFuelCouldDeliver()
        {
            var board = Board();
            var item = UtilityCatalog.Default.Find("surge");

            float before = board.Wards[0].Fuel;
            var use = SiegeUtility.Apply(board, item, SiegeAim.AtWard(0), null);

            Assert.IsTrue(use.Landed);
            Assert.Greater(board.Wards[0].Fuel, before);
            Assert.AreEqual(SiegeUtility.MatchesFor(SiegeUtility.DamageOfFuel(item.Magnitude)),
                            use.Matches);
        }

        /// <summary>
        /// A ward with less than half a pour's room is refused, because an item spent for a
        /// tenth of its effect is an item spent for nothing — and the charge is the whole pour
        /// whatever lands, so it would also be over-charged.
        /// </summary>
        [Test]
        public void ASurgeIntoAFullWardIsRefused()
        {
            var board = Board();
            var item = UtilityCatalog.Default.Find("surge");

            board.Wards[0].Fuel = SiegeTuning.WardCapacity;

            Assert.AreEqual(0, board.RoomForFuel(0));
            Assert.IsFalse(SiegeUtility.Would(board, item, SiegeAim.AtWard(0)));
        }

        [Test]
        public void NothingLandsOnABoardWhoseLineHasAlreadyFallen()
        {
            var board = Board();
            foreach (var ward in board.Wards) { ward.Health = 0; ward.Alive = false; }

            Assert.IsTrue(board.Stranded);

            foreach (var item in UtilityCatalog.Default.Items)
                Assert.IsFalse(SiegeUtility.Would(board, item, SiegeAim.AtWard(0)),
                               $"'{item.Id}' would land on a board that is already lost");
        }

        // =================================================================== the chest
        [Test]
        public void AUtilityBandCarriesWhichUtilityItPays()
        {
            var chest = new ChestDefinition(
                new[] { new ChestBand(ChestDropKind.Utility, 1, 1, "firepot") },
                new ChestOption[0]);

            var drops = chest.Roll("player", 20_315, 0);

            Assert.AreEqual(1, drops.Count);
            Assert.AreEqual(ChestDropKind.Utility, drops[0].Kind);
            Assert.AreEqual("firepot", drops[0].Item);
            Assert.AreEqual(1, drops[0].Amount);
        }

        /// <summary>
        /// Two utilities in one chest are two rewards. Folding them on kind alone would grant one
        /// id twice and the other never — the fault <c>ChestDefinition.Merge</c> already avoids
        /// for two credit bands, one field wider.
        /// </summary>
        [Test]
        public void TwoDifferentUtilitiesInOneChestDoNotFoldIntoEachOther()
        {
            var chest = new ChestDefinition(
                new[]
                {
                    new ChestBand(ChestDropKind.Utility, 1, 1, "firepot"),
                    new ChestBand(ChestDropKind.Utility, 2, 2, "mending"),
                },
                new ChestOption[0]);

            var drops = chest.Roll("player", 20_315, 0);

            Assert.AreEqual(2, drops.Count);
        }

        [Test]
        public void TwoBandsOfTheSameUtilityDoFoldIntoOne()
        {
            var chest = new ChestDefinition(
                new[]
                {
                    new ChestBand(ChestDropKind.Utility, 1, 1, "firepot"),
                    new ChestBand(ChestDropKind.Utility, 2, 2, "firepot"),
                },
                new ChestOption[0]);

            var drops = chest.Roll("player", 20_315, 0);

            Assert.AreEqual(1, drops.Count);
            Assert.AreEqual(3, drops[0].Amount);
        }

        /// <summary>
        /// A kind that names a thing and does not name one would be drawn on the chest panel as a
        /// prize and grant nothing, which is the one failure a chest may never have.
        /// </summary>
        [Test]
        public void AUtilityBandThatNamesNoItemIsRefusedByTheReader()
        {
            var problems = new List<string>();

            var table = DailyChestTable.Resolve(new DailyChestDto
            {
                runsPerChest = 3,
                chests = new[]
                {
                    new DailyChestEntryDto
                    {
                        guaranteed = new[]
                        {
                            new DailyDropDto { kind = "utility", min = 1, max = 1 },
                        },
                    },
                },
            }, problems);

            Assert.AreSame(DailyChestTable.Default, table);
            CollectionAssert.IsNotEmpty(problems);
        }

        [Test]
        public void ADropThatNamesNoItemIsNotAValidReward()
        {
            Assert.IsFalse(new ChestDrop(ChestDropKind.Utility, 1).IsValid);
            Assert.IsTrue(new ChestDrop(ChestDropKind.Utility, 1, "firepot").IsValid);
        }

        // =================================================================== the shipped content
        /// <summary>
        /// The catalog that actually ships, read the way the game reads it.
        ///
        /// <b>Inline rather than through a vector file</b>, for the reason every ladder fixture
        /// in this project is: a fixture that needs <c>JsonUtility</c> is reported as "needs the
        /// Editor" by the offline runner and is therefore the one gate nobody runs on the way
        /// past. What is checked here needs no serialiser at all.
        /// </summary>
        [Test]
        public void EveryShippedUtilityHasAnIconTheBuildActuallyCarries()
        {
            var declared = new HashSet<string>();
            foreach (var request in AssetPipeline.AssetManifest.GlobalAssets())
                declared.Add(request.Address);

            foreach (var item in UtilityCatalog.Default.Items)
                Assert.IsTrue(declared.Contains(AssetPipeline.AssetManifest.ArtRoot + item.Art),
                    $"'{item.Id}' draws '{item.Art}', which AssetManifest does not name — it "
                    + "would be a white rectangle on the bar (invariant 7b)");
        }

        [Test]
        public void EveryShippedUtilityIsPricedOrExplicitlyChestOnly()
        {
            foreach (var item in UtilityCatalog.Default.Items)
            {
                Assert.GreaterOrEqual(item.GemPrice, 0);
                Assert.GreaterOrEqual(item.MaxHeld, 1);
                Assert.LessOrEqual(item.MaxHeld, UtilityStock.MaxHeld);
            }
        }

        /// <summary>
        /// The wire's bound and the rules' bound are one number. A save the client will write and
        /// the rules refuse loses <em>every</em> save write rather than the extra rows
        /// (invariant 12a), so the two are held together here rather than by memory.
        /// </summary>
        [Test]
        public void TheStockBoundMatchesTheSecurityRules()
        {
            string rules = System.IO.File.ReadAllText(
                System.IO.Path.Combine(
                    System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName,
                    "firebase", "firestore.rules"));

            Assert.IsTrue(rules.Contains("'utilityStock'"),
                "utilityStock is not in the hasOnly list; every save write would be rejected");

            Assert.IsTrue(rules.Contains($"d.utilityStock.size() <= {UtilityStock.MaxIds}"),
                $"the rules' utilityStock bound does not match UtilityStock.MaxIds "
                + $"({UtilityStock.MaxIds})");
        }
    }
}
