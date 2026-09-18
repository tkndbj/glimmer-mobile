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
                                int magnitude = 10, int price = 5, int max = 9)
            => new UtilityDto
            {
                id = id, kind = kind, magnitude = magnitude,
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
            // One bolt's fuel is one bolt; a bolt is ShotDamage, doubled against a raider that
            // ward is strong against. Anything less would under-charge, which is the unsafe
            // direction. **Asked as `FuelPerShotTenths` rather than as ten**, because a bolt's
            // cost is a scale that has moved twice and this is a statement about a *bolt*.
            Assert.AreEqual(SiegeTuning.ShotDamage * SiegeTuning.WeakMultiplier,
                            SiegeUtility.DamageOfFuel(SiegeTuning.FuelPerShotTenths));
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
                "rgby", "rgby", new[] { "rrgg" }, boss: null);
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

        // =================================================================== the storm
        /// <summary>A hill with a bulwark and a boss on it, which is what a storm is judged on.</summary>
        static SiegeBoard Stormy()
        {
            var rows = new[]
            {
                "rgbyrg",
                "bygrby",
                "grbygr",
                "ybgrby",
            };

            var layout = new SiegeLayout(
                ProtoGrid.TryRead(rows, 6, 4, SiegeLayout.Letters, out var grid, out _)
                    ? grid : null,
                "rgby", "rgby", new[] { "rg#bR" }, boss: "warlord:r");

            Assert.IsNull(layout.Fault, layout.Fault);
            return SiegeBoard.Build(layout);
        }

        [Test]
        public void AStormStrikesEveryRaiderStandingOnTheHill()
        {
            var board = Stormy();
            // A wave arrives spaced by `RaiderSpacing`, so the whole of it is only on the
            // hill several seconds after the first of it is.
            Settle(board, SiegeTuning.FirstWaveAfter
                        + SiegeTuning.RaiderSpacing * 5f);

            var standing = new HashSet<int>();
            foreach (var raider in board.Raiders)
                if (raider.Alive && raider.OnTheHill) standing.Add(raider.Id);

            Assert.Greater(standing.Count, 1, "the fixture has to put a hill up first");

            var strikes = new List<SiegeStrike>();
            board.Storm(1, strikes);

            var struck = new HashSet<int>();
            foreach (var hit in strikes) struck.Add(hit.Raider);

            CollectionAssert.AreEquivalent(standing, struck);
        }

        /// <summary>
        /// The clause that keeps the finale a fight rather than a purchase. Every raider takes the
        /// same magnitude, and a boss carries the health of several waves - so a storm hurts one
        /// and can never end one.
        /// </summary>
        [Test]
        public void AStormHurtsABossAndNeverFellsIt()
        {
            var board = Stormy();
            Settle(board, SiegeTuning.FirstWaveAfter + 1f);

            // Walk on until every authored wave has mustered, then clear the hill: a boss comes
            // in alone once nothing else stands (37dn), and is hurtable from the frame it plants.
            Settle(board, SiegeTuning.BetweenWaves * 4f);
            foreach (var raider in board.Raiders) { raider.Health = 0; raider.Alive = false; }
            Settle(board, SiegeTuning.Breather + SiegeTuning.BossMarch + 2f);

            SiegeRaider boss = null;
            foreach (var raider in board.Raiders)
                if (raider.Alive && SiegeTuning.IsBoss(raider.Kind)) boss = raider;

            Assert.IsNotNull(boss, "the fixture has to send a boss");
            Assert.IsFalse(boss.Impervious, "the boss is still walking on");

            int was = boss.Health;
            board.Storm(SiegeTuning.BulwarkHealth, null);

            Assert.Less(boss.Health, was, "a storm has to hurt it");
            Assert.IsTrue(boss.Alive, "and must never fell it");
        }

        /// <summary>
        /// A shield is answered by <em>colour</em>, and a storm has none - so the soak a ward's
        /// bolt pays is not a rule about this. It is also the item's reason to exist: a wave of
        /// armour is the one thing a player cannot simply out-match.
        /// </summary>
        [Test]
        public void AStormIsNotSoakedByAShield()
        {
            var board = Stormy();
            // A wave arrives spaced by `RaiderSpacing`, so the whole of it is only on the
            // hill several seconds after the first of it is.
            Settle(board, SiegeTuning.FirstWaveAfter
                        + SiegeTuning.RaiderSpacing * 5f);

            SiegeRaider bulwark = null;
            foreach (var raider in board.Raiders)
                if (raider.Alive && raider.Kind == SiegeKind.Bulwark) bulwark = raider;

            Assert.IsNotNull(bulwark, "the fixture has to send a bulwark");

            var strikes = new List<SiegeStrike>();
            board.Storm(100, strikes);

            int took = 0;
            foreach (var hit in strikes) if (hit.Raider == bulwark.Id) took = hit.Damage;

            Assert.AreEqual(100, took, "a shield halves a bolt and never a storm");
        }

        /// <summary>
        /// Invariant 39 asked of the biggest item on the bar: it is billed the fewest matches that
        /// could have delivered the same damage, so clearing a hill with one can never come out
        /// cheaper than clearing it by playing.
        /// </summary>
        [Test]
        public void AStormIsChargedForEverythingItAbsorbed()
        {
            var board = Stormy();
            Settle(board, SiegeTuning.FirstWaveAfter + 1f);

            var item = new UtilityItem("stormcall", UtilityKind.Storm, 700, 40, 3, 4);
            var strikes = new List<SiegeStrike>();

            var use = SiegeUtility.Apply(board, item, default, strikes);

            Assert.IsTrue(use.Landed);
            Assert.Greater(use.Delivered, 0);
            Assert.AreEqual(SiegeUtility.MatchesFor(use.Delivered), use.Matches);
        }

        /// <summary>A storm over an empty hill is refused rather than spent.</summary>
        [Test]
        public void AStormIsRefusedWhenNothingIsOnTheHill()
        {
            var board = Stormy();

            var item = new UtilityItem("stormcall", UtilityKind.Storm, 700, 40, 3, 4);

            Assert.AreEqual(0, board.OnTheHill, "no wave has mustered yet");
            Assert.IsFalse(SiegeUtility.Would(board, item, default));
        }

        [Test]
        public void ABlastBurnsTheBoxItIsThrownAtAndReportsWhatWasAbsorbed()
        {
            var board = Board();
            Settle(board, SiegeTuning.FirstWaveAfter + 1f);

            var raider = board.Raiders[0];

            var strikes = new List<SiegeStrike>();
            int absorbed = board.Blast(raider.Lane, SiegeTuning.RowOf(raider.March), 5, strikes);

            Assert.Greater(absorbed, 0);
            CollectionAssert.IsNotEmpty(strikes);
        }

        /// <summary>
        /// The property the boxes exist for: a firepot is <em>aimed</em>, and how far it carries
        /// is bounded by a rule rather than by a distance.
        ///
        /// <para>
        /// A radius could take a raider the player had not aimed at and leave one they had; boxes
        /// cannot, which is what makes the panes on the board honest (invariant 33g). What bounds
        /// it is <c>BlastReach</c> plus the width of the thing being hit - so an ordinary raider is
        /// never touched from two lanes away, and every raider that <em>was</em> touched really was
        /// standing on one of the boxes that burned.
        /// </para>
        /// </summary>
        [Test]
        public void ABlastIsBoundedByItsBoxesAndNotByADistance()
        {
            var board = Board();
            Settle(board, SiegeTuning.FirstWaveAfter + 1f);

            var target = board.Raiders[0];
            int lane = target.Lane, row = SiegeTuning.RowOf(target.March);

            var strikes = new List<SiegeStrike>();
            board.Blast(lane, row, 1, strikes);

            var struck = new HashSet<int>();
            foreach (var hit in strikes) struck.Add(hit.Raider);

            Assert.IsTrue(struck.Contains(target.Id),
                          "a firepot on the box a raider stands in has to reach it");

            foreach (var raider in board.Raiders)
            {
                if (!raider.Alive || !raider.OnTheHill) continue;

                // Everything struck was standing on one of the boxes that burned. This is the half
                // a radius cannot promise.
                if (struck.Contains(raider.Id))
                {
                    bool standing = false;

                    for (int r = 0; r < SiegeTuning.BlastRows; r++)
                        for (int l = 0; l < SiegeTuning.Lanes; l++)
                            if (SiegeTuning.InBlast(lane, row, l, r)
                                && SiegeTuning.OnBody(raider.Kind, raider.Lane, raider.March, l, r))
                                standing = true;

                    Assert.IsTrue(standing, "nothing burns that was not on a box that burned");
                    continue;
                }

                Assert.IsFalse(SiegeTuning.OnBody(raider.Kind, raider.Lane, raider.March, lane, row),
                               "anything standing on the box that was tapped has to burn");
            }

            // And an ordinary raider two lanes off is never touched, whatever row it is in: its
            // body is one lane wide and the plus carries one.
            foreach (var raider in board.Raiders)
            {
                if (SiegeTuning.LanesOf(raider.Kind) > 1) continue;

                int across = raider.Lane > lane ? raider.Lane - lane : lane - raider.Lane;

                if (across >= 2)
                    Assert.IsFalse(struck.Contains(raider.Id),
                                   "a firepot does not carry two lanes");
            }
        }

        [Test]
        public void ABlastAimedOffTheGridBurnsNothing()
        {
            var board = Board();
            Settle(board, SiegeTuning.FirstWaveAfter + 1f);

            Assert.AreEqual(0, board.Blast(-1, 0, 50, null));
            Assert.AreEqual(0, board.Blast(SiegeTuning.Lanes, 0, 50, null));
            Assert.AreEqual(0, board.Blast(0, SiegeTuning.BlastRows, 50, null));
        }

        /// <summary>
        /// Every march reading lands in a band, including the ends. A raider at the line reads as
        /// the last row rather than as one past it, which is what an unclamped cast would do.
        /// </summary>
        [Test]
        public void EveryPointOnTheHillFallsInABand()
        {
            Assert.AreEqual(0, SiegeTuning.RowOf(0f));
            Assert.AreEqual(SiegeTuning.BlastRows - 1, SiegeTuning.RowOf(1f));
            Assert.AreEqual(SiegeTuning.BlastRows - 1, SiegeTuning.RowOf(2f));
            Assert.AreEqual(0, SiegeTuning.RowOf(-1f));

            for (int row = 0; row < SiegeTuning.BlastRows; row++)
            {
                float middle = (row + .5f) / SiegeTuning.BlastRows;
                Assert.AreEqual(row, SiegeTuning.RowOf(middle));
            }
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
            int lane = raider.Lane, row = SiegeTuning.RowOf(raider.March);

            // Health as it stood, by id, so the bound is what was really there rather than a
            // second copy of the rule that decides who is caught.
            var held = new Dictionary<int, int>();
            foreach (var other in board.Raiders)
                if (other.Alive && other.OnTheHill) held[other.Id] = other.Health;

            var strikes = new List<SiegeStrike>();
            int absorbed = board.Blast(lane, row, 9_999, strikes);

            int standing = 0;
            foreach (var hit in strikes) standing += held[hit.Raider];

            Assert.AreEqual(standing, absorbed,
                "absorbed damage is exactly the health that was actually there");
            Assert.Greater(absorbed, 0);
        }

        [Test]
        public void ABlastThatReachesNobodyIsRefusedAndCostsNothing()
        {
            var board = Board();
            Settle(board, SiegeTuning.FirstWaveAfter + 1f);

            var item = UtilityCatalog.Default.Find("firepot");

            // A tap that reaches nobody: the foot of the hill, before anything has walked down
            // it. Which box that is has to be found rather than assumed, because a wave is dealt
            // into lanes deterministically but not predictably - and it is asked of the whole
            // plus, since a firepot carries a box in every direction.
            int lane = 0, row = SiegeTuning.BlastRows - 1;
            for (; lane < SiegeTuning.Lanes; lane++)
            {
                bool clear = true;
                foreach (var raider in board.Raiders)
                    if (raider.Alive && raider.OnTheHill
                        && SiegeTuning.Caught(raider.Kind, raider.Lane, raider.March, lane, row))
                        clear = false;

                if (clear) break;
            }

            Assert.Less(lane, SiegeTuning.Lanes, "every box at the foot of the hill is occupied");

            var use = SiegeUtility.Apply(board, item, SiegeAim.OnTheHill(lane, row),
                                         new List<SiegeStrike>());

            Assert.IsFalse(use.Landed);
            Assert.AreEqual(0, use.Matches);
        }

        /// <summary>
        /// <b>A mending raises a fallen ward, and the colour lock is what made that necessary.</b>
        ///
        /// <para>
        /// It used to be refused, on the reasoning that <c>SiegeBoard.Stranded</c> is a certainty
        /// deciding whether money changes hands (invariant 28f). That reasoning does not survive
        /// reading which way round it points: <c>Stranded</c> is <em>false</em> here, so what it
        /// says is "a purchase <em>would</em> rescue this" - and something that puts a ward back
        /// up makes that more true rather than less. The continue has raised a whole line for
        /// twenty gems since the mode shipped.
        /// </para>
        /// <para>
        /// <b>What made it necessary is that a dead ward is now a dead colour.</b> While a bolt
        /// merely preferred its own colour a fallen turret cost the line a quarter of its output;
        /// under the lock its colour can never be hurt again except by a splash, a chain or an
        /// overcharge - so a line that loses one early walks into a colour it cannot answer and
        /// the run spirals for a reason the player can do nothing about.
        /// </para>
        /// <para>
        /// <b>It does not make the continue redundant</b>, which is invariant 23a's split: a
        /// mending raises <em>one</em> ward while others still stand, and a continue is offered
        /// only when the last one has fallen and the run is already lost. Different moments, in a
        /// fixed order, so nobody is ever quoted both at once.
        /// </para>
        /// </summary>
        [Test]
        public void AMendingRaisesAFallenWardWithWhatItGives()
        {
            var board = Board();
            var ward = board.Wards[0];

            ward.Health = 0;
            ward.Alive = false;
            ward.Fuel = 9f;

            Assert.AreEqual(ward.Full, board.RoomForHealth(0),
                            "a fallen ward has room for a whole mending");

            Assert.AreEqual(5, board.Mend(0, 5));

            Assert.IsTrue(ward.Alive, "a mending left a fallen ward down");
            Assert.AreEqual(5, ward.Health, "it came back with more than the mending gave");
            Assert.AreEqual(0f, ward.Fuel, "a raised ward starts empty - fuel is matched for");

            // And it is an ordinary heal from there on.
            Assert.AreEqual(3, board.Mend(0, 3));
            Assert.AreEqual(8, ward.Health);
        }

        /// <summary>A mending that would raise a ward is still charged the grade nothing.</summary>
        [Test]
        public void RaisingAWardChargesTheGradeNothing()
        {
            var board = Board();
            board.Wards[0].Health = 0;
            board.Wards[0].Alive = false;

            var item = UtilityCatalog.Default.Find("mending");
            var use = SiegeUtility.Apply(board, item, SiegeAim.AtWard(0), null);

            Assert.IsTrue(use.Landed, "a mending was refused on a ward that had fallen");
            Assert.AreEqual(0, use.Matches, "a mending delivers no damage, so it saves no matches");
            Assert.IsTrue(board.Wards[0].Alive);
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
        ///
        /// <para>
        /// <b>"Full" means the tube <em>and</em> the charge rack</b>, which is what the overcharge
        /// changed. A full tube with a charge still to bank has somewhere for a pour to go — and
        /// what it buys there is worth more than the fuel was, so refusing it would refuse the item
        /// at the one moment it is most valuable.
        /// </para>
        /// </summary>
        [Test]
        public void ASurgeIntoAWardWithNothingLeftToFillIsRefused()
        {
            var board = Board();
            var item = UtilityCatalog.Default.Find("surge");

            var ward = board.Wards[0];

            // A full tube alone is not full: the next pour banks an overcharge.
            ward.Fuel = ward.Capacity;

            Assert.Greater(board.RoomForFuel(0), 0,
                           "a full tube with a charge still to bank has somewhere to put a pour");
            Assert.IsTrue(SiegeUtility.Would(board, item, SiegeAim.AtWard(0)));

            // Tube full and every charge held: now there is nowhere for it to go.
            ward.Charges = SiegeTuning.MostCharges;

            Assert.AreEqual(0, board.RoomForFuel(0));
            Assert.IsFalse(SiegeUtility.Would(board, item, SiegeAim.AtWard(0)));
        }

        /// <summary>
        /// And a surge that tops a tube up banks an overcharge, exactly as a match would.
        ///
        /// <b>Both doors pour through <c>SiegeWard.Fill</c></b>, because a conversion written at
        /// one of them is a ward that can never bank from the other.
        /// </summary>
        [Test]
        public void ASurgeThatFillsATubeBanksAnOvercharge()
        {
            var board = Board();
            var item = UtilityCatalog.Default.Find("surge");

            var ward = board.Wards[0];
            ward.Fuel = ward.Capacity - item.Magnitude / 20f;      // half a pour short of full

            Assert.AreEqual(0, ward.Charges);

            var use = SiegeUtility.Apply(board, item, SiegeAim.AtWard(0), null);

            Assert.IsTrue(use.Landed);
            Assert.AreEqual(1, ward.Charges, "a surge that filled the tube banked nothing");
            Assert.IsTrue(ward.Armed);
        }

        [Test]
        public void NothingLandsOnABoardWhoseLineHasAlreadyFallen()
        {
            var board = Board();
            foreach (var ward in board.Wards) { ward.Health = 0; ward.Alive = false; }

            // **Not `Stranded`, which this asked until a continue could raise a fallen line.**
            // That predicate answers "would a purchase rescue this", and the answer is now yes —
            // which is precisely why it is the wrong question here: what a utility must not land
            // on is a run that has already ended, and that is `AnyMove`.
            Assert.IsFalse(board.AnyMove);
            Assert.IsFalse(board.Stranded, "a continue would still rescue it");

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
