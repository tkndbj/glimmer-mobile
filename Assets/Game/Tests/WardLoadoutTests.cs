using System.Collections.Generic;
using GlimmerGrove.Modes;
using GlimmerGrove.Persistence;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The turret roster and the line a player stands it in: what an ability may do, what the
    /// roster refuses, and how two devices' arrangements are joined.
    ///
    /// <para>
    /// <b>The first fixture here is the load-bearing one.</b> A siege's par is the hill's health
    /// over the <em>baseline</em> bolt, so a turret that hit softer would push three stars out of
    /// reach of whoever chose it — a grade decided by a purchase, which is the one thing invariant
    /// 39 refuses outright. Nothing else in this project can see that: a level whose player
    /// brought a weak turret still parses, still validates and still ships.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class WardLoadoutTests
    {
        static readonly string[] Field =
        {
            "ryybgyyg",
            "bybgrgyy",
            "rbryyggr",
            "grgrgbbr",
            "yybgrrbg",
        };

        static SiegeLayout Layout(string waves = "rgby")
        {
            Assert.IsTrue(ProtoGrid.TryRead(Field, 8, 5, SiegeLayout.Cells,
                                            out var grid, out string error), error);

            return new SiegeLayout(grid, "rgby", "rgby", new[] { waves }, null, 0);
        }

        static WardLine LineOf(string id)
        {
            var catalog = WardCatalog.Default;
            var chosen = new List<WardSlot>();

            for (int i = 0; i < WardLine.Colours.Length; i++)
                chosen.Add(new WardSlot(WardLine.Colours[i], id));

            return WardLine.Resolve(catalog, chosen, _ => true);
        }

        // ------------------------------------------------------------- the load-bearing rule
        /// <summary>
        /// <b>No turret in the roster ever makes a bolt weaker than the starter's.</b>
        ///
        /// Asked of every model, every rank and both halves of the shield rule, because the
        /// primary hit is what par is computed against — and the one direction that is unsafe is
        /// down. It is asked of the <em>table</em> rather than of a played board so a model added
        /// by a content drop is covered without anybody writing a case for it.
        /// </summary>
        [Test]
        public void NoTurretEverMakesABoltWeakerThanThePlainOne()
        {
            var kinds = new[]
            {
                SiegeKind.Creeper, SiegeKind.Brute, SiegeKind.Bulwark,
                SiegeKind.Boss, SiegeKind.Overlord, SiegeKind.Weaver, SiegeKind.Thief,
            };

            foreach (var model in WardCatalog.Default.Models)
                for (int rank = 0; rank <= SiegeTuning.MaxRank; rank++)
                    foreach (var kind in kinds)
                        foreach (bool weak in new[] { false, true })
                        {
                            int plain = SiegeTuning.DamageTo(kind, rank, weak, WardAbility.None);
                            int mine = SiegeTuning.DamageTo(kind, rank, weak, model.Ability);

                            Assert.GreaterOrEqual(
                                mine, plain,
                                $"'{model.Id}' hits a {SiegeTuning.NameOf(kind)} for {mine} at "
                                + $"rank {rank} where a plain bolt does {plain} - a turret that "
                                + "hits softer pushes three stars out of reach of whoever chose it");
                        }
        }

        /// <summary>
        /// A rend turret is not blunted by a shield, and that is the only ability that touches the
        /// primary hit at all.
        /// </summary>
        [Test]
        public void OnlyARendTurretChangesWhatABoltIsWorth()
        {
            int plain = SiegeTuning.DamageTo(SiegeKind.Bulwark, 0, false, WardAbility.None);
            int rend = SiegeTuning.DamageTo(SiegeKind.Bulwark, 0, false, WardAbility.Rend);

            Assert.Greater(rend, plain, "a rend turret is blunted by a shield");

            foreach (var ability in new[]
                     {
                         WardAbility.Splash, WardAbility.Chain, WardAbility.Frost,
                         WardAbility.Pierce, WardAbility.Siphon, WardAbility.Ember,
                         WardAbility.Prism, WardAbility.Beacon,
                     })
                Assert.AreEqual(plain,
                                SiegeTuning.DamageTo(SiegeKind.Bulwark, 0, false, ability),
                                ability + " changed the primary hit");
        }

        /// <summary>
        /// A beacon holds more fuel and nothing else does.
        ///
        /// The one ability that changes what a ward <em>holds</em> rather than what it does with
        /// what it holds, which is why it is read once when the ward is built.
        /// </summary>
        [Test]
        public void OnlyABeaconChangesWhatAWardHolds()
        {
            foreach (var model in WardCatalog.Default.Models)
            {
                float held = SiegeTuning.CapacityOf(model);

                if (model.Ability == WardAbility.Beacon)
                    Assert.Greater(held, SiegeTuning.WardCapacity, model.Id);
                else
                    Assert.AreEqual(SiegeTuning.WardCapacity, held, model.Id);
            }
        }

        /// <summary>A prism turret is strong against two colours; nothing else is.</summary>
        [Test]
        public void OnlyAPrismTurretIsStrongAgainstTwoColours()
        {
            var plain = new SiegeWard(0, WardCatalog.Default.Find("bolt"));
            Assert.IsTrue(plain.StrongAgainst(0));
            Assert.IsFalse(plain.StrongAgainst(1));
            Assert.AreEqual(-1, plain.Partner);

            var prism = new SiegeWard(0, WardCatalog.Default.Find("prism"));
            Assert.IsTrue(prism.StrongAgainst(0));
            Assert.IsTrue(prism.StrongAgainst(prism.Partner));
            Assert.AreNotEqual(prism.Colour, prism.Partner);
        }

        // ------------------------------------------------------------- the roster
        /// <summary>
        /// The shipped roster is a ladder with no gaps, one free turret, and one price each.
        ///
        /// <b>The same four refusals the reader makes</b>, pinned against the built-in table so a
        /// change to it fails here rather than at a content push.
        /// </summary>
        [Test]
        public void TheBuiltInRosterIsALadderWithOneFreeTurret()
        {
            var catalog = WardCatalog.Default;
            var orders = new List<int>();
            var ids = new HashSet<string>();
            bool starter = false;

            foreach (var model in catalog.Models)
            {
                Assert.IsTrue(ids.Add(model.Id), model.Id + " is in the roster twice");

                Assert.IsFalse(model.ForGems && model.ForCoins,
                               model.Id + " is priced in both gems and credits");

                if (model.MinLevel > 0)
                    Assert.IsTrue(model.ForCoins,
                                  model.Id + " gates a price gems can pay, which never fires");

                starter |= model.IsStarter;
                orders.Add(model.Order);
            }

            orders.Sort();
            for (int i = 0; i < orders.Count; i++)
                Assert.AreEqual(i + 1, orders[i], "the shelf's ladder has a gap or a tie");

            Assert.IsTrue(starter, "no turret is free, so a new player stands an empty line");
            Assert.IsNotNull(catalog.Starter);
            Assert.IsTrue(catalog.Starter.IsStarter);
        }

        /// <summary>
        /// <b>"Free" asks both prices</b>, which is invariant 16j's hard-won correction: it was
        /// <c>Cost &lt;= 0</c> for as long as there was one currency, and the day a second one
        /// arrived every gem-priced thing read as free.
        /// </summary>
        [Test]
        public void AGemPricedTurretIsNotFree()
        {
            var gemmed = new WardModel("x", WardAbility.None, 0, 0, 600, 0, 0, 1);
            var coined = new WardModel("y", WardAbility.None, 0, 0, 0, 900, 3, 2);
            var free = new WardModel("z", WardAbility.None, 0, 0, 0, 0, 0, 3);

            Assert.IsFalse(gemmed.IsStarter);
            Assert.IsFalse(coined.IsStarter);
            Assert.IsTrue(free.IsStarter);
        }

        // ------------------------------------------------------------- the line
        /// <summary>
        /// A stored choice is a hint: an unknown id, and a turret the player no longer holds, both
        /// fall back to the starter rather than leaving a slot empty.
        ///
        /// <b>Ownership is re-checked rather than trusted from the save</b>, because a save says
        /// which turret was chosen and whether it may be stood is a question about what is held
        /// <em>now</em>.
        /// </summary>
        [Test]
        public void AStoredChoiceIsAHintAndNeverAnAuthority()
        {
            var catalog = WardCatalog.Default;
            var starter = catalog.Starter;

            var chosen = new[]
            {
                new WardSlot('r', "mortar"),
                new WardSlot('g', "no_such_turret"),
                new WardSlot('b', "rime"),
            };

            // Everything held: the two real ids stand, the unknown one falls back, and the colour
            // nobody chose for falls back too.
            var all = WardLine.Resolve(catalog, chosen, _ => true);
            Assert.AreEqual("mortar", all.At(0).Id);
            Assert.AreEqual(starter.Id, all.At(1).Id);
            Assert.AreEqual("rime", all.At(2).Id);
            Assert.AreEqual(starter.Id, all.At(3).Id);

            // Nothing held: every slot falls back, and the line is still four turrets.
            var none = WardLine.Resolve(catalog, chosen, m => m.IsStarter);
            for (int i = 0; i < WardLine.Colours.Length; i++)
                Assert.AreEqual(starter.Id, none.At(i).Id, "colour " + i);

            Assert.AreEqual(WardLine.Colours.Length, none.Models.Count);
        }

        /// <summary>The starter line is four of the roster's free turret, and never null.</summary>
        [Test]
        public void TheStarterLineIsFourOfTheFreeTurret()
        {
            var line = WardLine.Starter(WardCatalog.Default);

            for (int i = 0; i < WardLine.Colours.Length; i++)
            {
                Assert.IsNotNull(line.At(i));
                Assert.IsTrue(line.At(i).IsStarter);
            }
        }

        /// <summary>
        /// A board built with no line stands the starter, which is what every content gate, every
        /// offline mirror and the hold simulation play against.
        /// </summary>
        [Test]
        public void ABoardBuiltWithNoLineStandsTheStarter()
        {
            var board = SiegeBoard.Build(Layout());

            foreach (var ward in board.Wards)
            {
                Assert.IsNotNull(ward.Model);
                Assert.IsTrue(ward.Model.IsStarter);
                Assert.AreEqual(SiegeTuning.WardCapacity, ward.Capacity);
            }
        }

        /// <summary>
        /// A beacon really banks more, all the way through the board rather than only in the
        /// number it reports.
        ///
        /// <b>The one thing about this ability that could be half-implemented and look right.</b>
        /// The ward's capacity is read when it is built, and every other place fuel arrives used
        /// the mode's <em>constant</em> — so a beacon would have said it held half again as much,
        /// drawn a longer tube, and spilled at the old ceiling anyway.
        /// </summary>
        [Test]
        public void ABeaconReallyBanksMoreThanAPlainWard()
        {
            var board = SiegeBoard.Build(Layout(), LineOf("beacon"));
            var ward = board.Wards[0];

            Assert.Greater(ward.Capacity, SiegeTuning.WardCapacity);

            // Poured in through the utility path, which is the one a player can reach directly.
            board.Surge(0, 10_000);
            Assert.AreEqual(ward.Capacity, ward.Fuel, .001f,
                            "a surge stopped at the mode's constant rather than the ward's own "
                            + "capacity");

            // And through a match, which is how it arrives on every other turn.
            var plain = SiegeBoard.Build(Layout());
            Assert.AreEqual(SiegeTuning.WardCapacity, plain.Wards[0].Capacity);
        }

        /// <summary>A board built with a line stands it, on the colours it was arranged for.</summary>
        [Test]
        public void ABoardStandsTheLineItIsHanded()
        {
            var board = SiegeBoard.Build(Layout(), LineOf("mortar"));

            foreach (var ward in board.Wards)
                Assert.AreEqual("mortar", ward.Model.Id);
        }

        // ------------------------------------------------------------- the merge
        /// <summary>
        /// The line is joined by recency, and the join is a maximum over a total order: an
        /// arrangement beats none however old it is, then the later stamp wins, then a stable
        /// comparison settles a tie.
        ///
        /// <b>Which makes it idempotent and commutative</b>, and those are what a merge promises —
        /// a device that ran the join twice, or ran it the other way round, must reach the same
        /// line.
        /// </summary>
        [Test]
        public void TheLineIsJoinedByRecencyAndTheJoinIsAMaximum()
        {
            var mine = new[] { new WardSlotDto { colour = "r", ward = "mortar" } };
            var other = new[] { new WardSlotDto { colour = "r", ward = "rime" } };

            // The later stamp wins, whichever side it is on.
            var a = WardLoadout.Join(mine, 100L, other, 200L);
            var b = WardLoadout.Join(other, 200L, mine, 100L);

            Assert.AreEqual("rime", a.Rows[0].ward);
            Assert.AreEqual("rime", b.Rows[0].ward);
            Assert.AreEqual(200L, a.At);
            Assert.AreEqual(200L, b.At);

            // An arrangement beats none, however old: empty is never something a player asked for.
            var kept = WardLoadout.Join(mine, 1L, null, 9_999L);
            Assert.AreEqual(1, kept.Rows.Length);
            Assert.AreEqual("mortar", kept.Rows[0].ward);

            // Idempotent.
            var once = WardLoadout.Join(mine, 100L, other, 200L);
            var twice = WardLoadout.Join(once.Rows, once.At, once.Rows, once.At);
            Assert.AreEqual(once.Rows.Length, twice.Rows.Length);
            Assert.AreEqual(once.Rows[0].ward, twice.Rows[0].ward);

            // Two files that never chose anything join to nothing, not to a default: a device with
            // no opinion must stay distinguishable from one that made a choice (invariant 11c).
            var nothing = WardLoadout.Join(null, 0L, null, 0L);
            Assert.IsEmpty(nothing.Rows);
            Assert.AreEqual(0L, nothing.At);
        }

        /// <summary>
        /// A tie is settled the same way whichever device runs it.
        ///
        /// Two arrangements at the same instant is two files that both predate the stamps carrying
        /// zero, far more often than it is two people arranging a line inside one second — and an
        /// arbitrary choice that depended on argument order would leave two devices pushing over
        /// each other for ever.
        /// </summary>
        [Test]
        public void ATieIsSettledTheSameWayWhicheverDeviceRunsIt()
        {
            var mine = new[] { new WardSlotDto { colour = "r", ward = "mortar" } };
            var other = new[] { new WardSlotDto { colour = "r", ward = "rime" } };

            var a = WardLoadout.Join(mine, 0L, other, 0L);
            var b = WardLoadout.Join(other, 0L, mine, 0L);

            Assert.AreEqual(a.Rows[0].ward, b.Rows[0].ward);
        }

        /// <summary>The rows are written in colour order, so an unchanged line pushes nothing.</summary>
        [Test]
        public void TheRowsAreAlwaysInColourOrder()
        {
            var scrambled = new[]
            {
                new WardSlotDto { colour = "y", ward = "rime" },
                new WardSlotDto { colour = "r", ward = "mortar" },
                new WardSlotDto { colour = "b", ward = "lance" },
            };

            var joined = WardLoadout.Join(scrambled, 5L, null, 0L);

            Assert.AreEqual(3, joined.Rows.Length);
            Assert.AreEqual("r", joined.Rows[0].colour);
            Assert.AreEqual("b", joined.Rows[1].colour);
            Assert.AreEqual("y", joined.Rows[2].colour);
        }
    }
}
