using System;
using System.Collections.Generic;
using System.Linq;
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

            return WardLine.Resolve(catalog, chosen, (_, __) => true);
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

            // **Against the free turret's whole model, not against "no ability".** A turret's bolt
            // now carries a weight of its own as well as a trick, and the thing that must never
            // happen is a *purchase* hitting softer than what every player already holds — so the
            // yardstick is the starter rather than a hypothetical plain shot.
            var starter = WardCatalog.Default.Starter;

            foreach (var model in WardCatalog.Default.Models)
                for (int rank = 0; rank <= SiegeTuning.MaxRank; rank++)
                    foreach (var kind in kinds)
                        foreach (bool weak in new[] { false, true })
                        {
                            int plain = SiegeTuning.DamageTo(kind, rank, weak, starter);
                            int mine = SiegeTuning.DamageTo(kind, rank, weak, model);

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
                         WardAbility.Stun, WardAbility.Beacon,
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

        // ------------------------------------------------------------- the legendary band
        /// <summary>
        /// <b>A legendary is written down exactly as every other turret is, and a bare row is now
        /// only ever a file from the past.</b>
        ///
        /// <para>
        /// A turret is bought per colour, so its row is <c>{id}:{colour}</c> — the legendary band
        /// included since invariant 42k. Its row was the bare id for three days, which has meant
        /// <em>every colour</em> in this file since colours shipped, because that is what a build
        /// that owned turrets outright wrote and the only reading a union merge could safely give
        /// one (<c>WardHolding</c>). <b>Both halves are asserted here</b>: nothing writes a bare
        /// row any more, and every reader still honours one.
        /// </para>
        /// <para>
        /// <b>Asked of the ledger's own predicate rather than of a string</b>, because the thing
        /// that could break is not the spelling: it is a reader that checks the exact key and
        /// silently stops honouring the bare one, which is invariant 15a's lesson about a rule
        /// with two halves.
        /// </para>
        /// </summary>
        [Test]
        public void ALegendaryIsWrittenDownLikeEveryOtherTurret()
        {
            var legend = Legendary();
            var ordinary = WardCatalog.Default.Find("cleaver");

            Assert.IsNotNull(ordinary);
            Assert.IsFalse(ordinary.Legendary);

            Assert.AreEqual(legend.Id + ":r", WardHolding.Row(legend, 'r'),
                            "a legendary is bought for a seat like everything else");
            Assert.AreEqual("cleaver:r", WardHolding.Row(ordinary, 'r'));

            // One purchase, one seat — on the band and off it alike.
            var bought = new HashSet<string>(StringComparer.Ordinal)
            {
                WardHolding.Row(legend, 'r'),
            };

            for (int i = 0; i < WardLine.Colours.Length; i++)
            {
                char colour = WardLine.Colours[i];

                Assert.AreEqual(colour == 'r', WardLedger.IsHeld(legend, colour, bought.Contains),
                                $"a legendary bought on red reads wrong on '{colour}'");
                Assert.IsFalse(WardLedger.IsHeld(ordinary, colour, bought.Contains),
                               $"a turret nobody bought is held on '{colour}'");
            }

            // **And the bare row is still every colour**, which is what a file written before
            // colours existed holds, and what a legendary bought outright holds.
            var legacy = new HashSet<string>(StringComparer.Ordinal) { legend.Id };

            for (int i = 0; i < WardLine.Colours.Length; i++)
                Assert.IsTrue(WardLedger.IsHeld(legend, WardLine.Colours[i], legacy.Contains),
                              $"a bare row stopped covering '{WardLine.Colours[i]}'");
        }

        /// <summary>
        /// <b>A legendary carries a star ladder per seat, like every other turret — and a bare
        /// ladder is still read by every seat.</b>
        ///
        /// <para>
        /// The star ledger keys on the holding (<c>WardStarLedger</c>), so this is the same fact
        /// as the row above asked of the other half of the feature. It is also the half a player
        /// complains about: while the band was bought outright, four Eclipses paid for separately
        /// shared one ladder, so upgrading the one on red upgraded the one on blue.
        /// </para>
        /// <para>
        /// The legacy clause is the half that could fail <em>silently</em> — a reader that missed
        /// the bare row would show a five-star legendary at one star on all four seats with
        /// nothing saying so.
        /// </para>
        /// </summary>
        [Test]
        public void ALegendaryCarriesAStarLadderPerSeat()
        {
            var legend = Legendary();

            WardStarLedger.LoadFrom(new[]
            {
                new WardStarDto { ward = WardHolding.Row(legend, 'r'), stars = 4 },
            });

            Assert.AreEqual(4, WardStarLedger.StarsOf(legend, 'r'),
                            "the seat that was upgraded lost its ladder");

            for (int i = 1; i < WardLine.Colours.Length; i++)
                Assert.AreEqual(WardStars.Least,
                                WardStarLedger.StarsOf(legend, WardLine.Colours[i]),
                                $"seat '{WardLine.Colours[i]}' was handed red's upgrades");

            // **A bare ladder is read by every seat**, which is a legendary upgraded while the
            // band was bought outright — confiscating it is the one thing this may not do.
            WardStarLedger.LoadFrom(new[]
            {
                new WardStarDto { ward = legend.Id, stars = 5 },
            });

            for (int i = 0; i < WardLine.Colours.Length; i++)
                Assert.AreEqual(5, WardStarLedger.StarsOf(legend, WardLine.Colours[i]),
                                $"seat '{WardLine.Colours[i]}' lost a ladder bought outright");

            // And an ordinary turret is still per seat, or the fallback above would have quietly
            // widened every holding in the game.
            WardStarLedger.LoadFrom(new[]
            {
                new WardStarDto { ward = "cleaver:r", stars = 5 },
            });

            Assert.AreEqual(5, WardStarLedger.StarsOf("cleaver", 'r'));
            Assert.AreEqual(WardStars.Least, WardStarLedger.StarsOf("cleaver", 'b'));

            WardStarLedger.LoadFrom(null);
        }

        /// <summary>
        /// <b>The legendary flag and the legendary band are one fact, and the roster is refused
        /// when they disagree.</b>
        ///
        /// <para>
        /// <c>WardModel.Legendary</c> is authored rather than read off the rung, for
        /// <c>WardTier</c>'s own reason — a band is punctuation over the shelf's order and may not
        /// decide what a turret <em>does</em>. So something has to hold the two together, and it
        /// is <c>WardCatalog.LadderProblem</c>: a turret that ignored the colour lock under a
        /// TIER II header would be the mode's central rule suspended where nothing says so, and a
        /// turret in the legendary band that still obeyed it would be the header lying the other
        /// way. Both directions, because either alone is a hole.
        /// </para>
        /// </summary>
        [Test]
        public void ALegendaryStandsInTheLegendaryBandAndNothingElseDoes()
        {
            Assert.IsNull(WardCatalog.Default.LadderProblem(), "the shipped roster is a ladder");

            foreach (var model in WardCatalog.Default.Models)
                Assert.AreEqual(model.Legendary, WardTier.Of(model) == WardTier.Count,
                                $"'{model.Id}' is in band {WardTier.Of(model)}");

            // **An ordinary turret standing under the legendary header**, which parses, prices,
            // validates and plays - and puts a lie in a header. Asked through `Resolve`, because
            // that is the door content comes in by and the only one a bad file can ever use.
            var refused = Refusal(m => m.Id == "tempest"
                                     ? new WardModel(m.Id, m.Ability, m.Magnitude, m.Extent,
                                                     m.GemPrice, m.CoinPrice, m.MinLevel, m.Order,
                                                     m.PowerTenths, m.GuardTenths, false)
                                     : m);

            Assert.IsTrue(refused.Exists(p => p.Contains("legendary")),
                          "a turret that wears a colour was accepted under LEGENDARY: "
                          + string.Join(" | ", refused));

            // And a legendary dropped into the band below it.
            var strayed = Refusal(m => m.Id == "apex"
                                     ? new WardModel(m.Id, m.Ability, m.Magnitude, m.Extent,
                                                     m.GemPrice, m.CoinPrice, m.MinLevel, m.Order,
                                                     m.PowerTenths, m.GuardTenths, true)
                                     : m);

            Assert.IsTrue(strayed.Exists(p => p.Contains("legendary")),
                          "a legendary under a TIER III header was accepted: "
                          + string.Join(" | ", strayed));
        }

        /// <summary>The first legendary on the shelf, and an assertion that there is one.</summary>
        /// <summary>
        /// <b>A legendary is bought for one seat, and a line of four of them is four purchases —
        /// which is the rule every other turret on this shelf has always obeyed.</b>
        ///
        /// <para>
        /// This seat is the hole, and it has been closed twice. A colourless turret was written
        /// into <c>wardsOwned</c> as a bare id, which has meant <em>every colour</em> since
        /// colours shipped — so one payment furnished a whole line, and every gate was green
        /// because the ledger, the holding and the line are each right and nothing asked how many
        /// of one turret a line may stand. The first answer was a second row per copy; the second
        /// and current one is that a legendary is bought per seat like everything else (invariant
        /// 42k), which also gives each seat its own star ladder.
        /// </para>
        /// <para>
        /// <b>Asked of the seats rather than of the spelling</b>, because the spelling is not what
        /// could break: what matters is that paying once buys one seat and that the other three
        /// are still for sale.
        /// </para>
        /// </summary>
        [Test]
        public void ALegendaryIsHeldOnlyOnTheSeatsItWasBoughtFor()
        {
            var legend = Legendary();
            var ordinary = WardCatalog.Default.Find("cleaver");

            var bought = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < WardLine.Colours.Length; i++)
                Assert.IsFalse(WardLedger.IsHeld(legend, WardLine.Colours[i], bought.Contains),
                               "a turret nobody bought is held nowhere");

            // **Written down exactly as every other turret is**, which is the whole change: the
            // spelling is where the band used to be an exception.
            Assert.AreEqual(WardHolding.Key(legend.Id, 'r'), WardHolding.Row(legend, 'r'));
            Assert.AreEqual(WardHolding.Key(ordinary.Id, 'g'), WardHolding.Row(ordinary, 'g'));

            // Bought on red, and red is the only seat it stands on.
            bought.Add(WardHolding.Row(legend, 'r'));

            for (int i = 0; i < WardLine.Colours.Length; i++)
                Assert.AreEqual(i == 0,
                                WardLedger.IsHeld(legend, WardLine.Colours[i], bought.Contains),
                                $"seat {WardLine.Colours[i]} after one purchase");

            // And the other three are bought one at a time, exactly as a colour turret's are.
            for (int i = 1; i < WardLine.Colours.Length; i++)
            {
                bought.Add(WardHolding.Row(legend, WardLine.Colours[i]));

                for (int seat = 0; seat < WardLine.Colours.Length; seat++)
                    Assert.AreEqual(seat <= i,
                                    WardLedger.IsHeld(legend, WardLine.Colours[seat],
                                                      bought.Contains),
                                    $"seat {WardLine.Colours[seat]} after {i + 1} purchases");
            }
        }

        /// <summary>
        /// <b>A legendary bought under the old rule keeps every seat it paid for.</b>
        ///
        /// <para>
        /// The band was bought outright for three days, so a real file can hold a bare
        /// <c>eclipse</c> row with copy rows beside it (<c>WardHolding.CopyMark</c>). A bare row
        /// has meant every colour since colours shipped and is read that way still, so the
        /// retirement hands back <em>more</em> than it takes — which is the only direction a rule
        /// about somebody's purchases may move. A copy row covers nothing by itself, exactly as
        /// it never did.
        /// </para>
        /// </summary>
        [Test]
        public void ALegendaryBoughtUnderTheOldRuleKeepsEverySeat()
        {
            var legend = Legendary();

            var bought = new HashSet<string>(StringComparer.Ordinal)
            {
                legend.Id,                  // the bare row a purchase used to write
                legend.Id + "#2",           // and a copy bought beside it
            };

            for (int i = 0; i < WardLine.Colours.Length; i++)
                Assert.IsTrue(WardLedger.IsHeld(legend, WardLine.Colours[i], bought.Contains),
                              $"seat {WardLine.Colours[i]} was confiscated from a file that "
                              + "bought the legendary outright");

            Assert.AreEqual(legend.Id, WardHolding.IdOf(legend.Id + "#2"),
                            "a copy row is still about the turret it names");

            Assert.IsFalse(WardHolding.Covers(legend.Id + "#2", legend.Id, 'r'),
                           "a copy row covers a seat only through the bare row beside it");

            Assert.IsFalse(WardHolding.TryRead(legend.Id + "#2", out _, out _),
                           "a copy row is not a colour holding");

            // Both marks are still refused in an id, because a row carrying either would be
            // ambiguous whether or not anything writes one any more.
            Assert.IsFalse(WardHolding.Spellable("bad#id"), "the copy mark is refused in an id");
            Assert.IsFalse(WardHolding.Spellable("bad:id"), "the colour mark is refused in an id");
            Assert.IsTrue(WardHolding.Spellable(legend.Id));

            // A union of two devices' purchases carries a copy row through untouched rather than
            // pruning it: nothing may drop a row from a union-joined set (invariant 11b).
            var joined = WardLedger.Join(new[] { "eclipse", "eclipse#2" },
                                         new[] { "eclipse", "eclipse:r" });

            Assert.AreEqual(new[] { "eclipse", "eclipse#2", "eclipse:r" }, joined);
        }

        /// <summary>
        /// <b>The board stands a turret on the seats it was bought for and nowhere else, and the
        /// seat it drops is the same on every device.</b>
        ///
        /// <para>
        /// <c>WardLoadout.Choose</c> will not store a seat that is not held, so an honest file
        /// never reaches this — but a merge that dropped a row, a roster that retired a turret and
        /// a hand-edited file all have to land on a line that plays. The fallback is the starter,
        /// which is what every other refusal here falls back to.
        /// </para>
        /// <para>
        /// <b>Colour order, and that is not a detail.</b> The stored arrangement is a dictionary
        /// on the way here, and the seats the server keeps (<c>publishedLine</c>) are walked in
        /// colour order. The two have to agree, or a card and the board behind it show different
        /// lines.
        /// </para>
        /// </summary>
        [Test]
        public void ALineStandsATurretOnlyOnTheSeatsItWasBoughtFor()
        {
            var catalog = WardCatalog.Default;
            var legend = Legendary();

            var chosen = new List<WardSlot>();
            for (int i = 0; i < WardLine.Colours.Length; i++)
                chosen.Add(new WardSlot(WardLine.Colours[i], legend.Id));

            for (int seats = 0; seats <= WardLine.Colours.Length; seats++)
            {
                var bought = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < seats; i++)
                    bought.Add(WardHolding.Row(legend, WardLine.Colours[i]));

                var line = WardLine.Resolve(
                    catalog, chosen,
                    (model, colour) => WardLedger.IsHeld(model, colour, bought.Contains));

                for (int i = 0; i < WardLine.Colours.Length; i++)
                    Assert.AreEqual(i < seats ? legend.Id : catalog.Starter.Id, line.At(i).Id,
                                    $"seat {WardLine.Colours[i]} with {seats} seats bought");
            }

            // **No ownership asked is no ownership applied**, which is what every rule test,
            // content gate and offline mirror plays against, and what a visitor's card resolves
            // through — the server has already refused every seat it could not vouch for.
            var uncapped = WardLine.Resolve(catalog, chosen, null);
            for (int i = 0; i < WardLine.Colours.Length; i++)
                Assert.AreEqual(legend.Id, uncapped.At(i).Id);
        }

        /// <summary>
        /// <b>The star ledger has a row for every seat of every turret on the shelf.</b>
        ///
        /// <para>
        /// <c>WardStarLedger</c> truncates at <c>MaxRows</c> on its way into a save, so a roster
        /// that outgrows it stops writing down upgrades a player paid for and says nothing. The
        /// headroom was thirty-eight rows and is eight: a legendary carried one row while the
        /// band was bought outright and carries four now (invariant 42k). Raising this means
        /// raising <c>firestore.rules</c>' own bound first, in that order (12a, 12b).
        /// </para>
        /// </summary>
        [Test]
        public void TheStarLedgerHoldsTheWholeShelf()
        {
            int rows = WardCatalog.Default.Models.Count * WardLine.Colours.Length;

            Assert.LessOrEqual(rows, WardStarLedger.MaxRows,
                               $"{WardCatalog.Default.Models.Count} turrets on four seats is "
                               + $"{rows} rows against a ledger that writes "
                               + $"{WardStarLedger.MaxRows}");
        }

        static WardModel Legendary()
        {
            foreach (var model in WardCatalog.Default.Models)
                if (model.Legendary) return model;

            Assert.Fail("no turret in the roster is legendary");
            return null;
        }

        /// <summary>
        /// What <c>WardCatalog.Resolve</c> says about the shipped roster with one entry rewritten.
        ///
        /// <b>Through <c>Resolve</c> rather than through the private constructor</b>, so what is
        /// being tested is the door content comes in by — which is the only door a bad file can
        /// ever use. A refused roster answers the built-in one and names the fault, so what an
        /// assertion reads is the fault rather than the catalog.
        /// </summary>
        static List<string> Refusal(Func<WardModel, WardModel> swap)
        {
            var dto = new Content.WardsDto();
            var rows = new List<Content.WardModelDto>();

            foreach (var model in WardCatalog.Default.Models)
            {
                var it = swap(model);

                rows.Add(new Content.WardModelDto
                {
                    id = it.Id,
                    ability = WardAbilities.NameOf(it.Ability),
                    magnitude = it.Magnitude,
                    extent = it.Extent,
                    gemPrice = it.GemPrice,
                    coinPrice = it.CoinPrice,
                    minLevel = it.MinLevel,
                    order = it.Order,
                    power = it.PowerTenths,
                    guard = it.GuardTenths,
                    legendary = it.Legendary,
                });
            }

            dto.models = rows.ToArray();

            var problems = new List<string>();
            var read = WardCatalog.Resolve(dto, problems);

            Assert.AreSame(WardCatalog.Default, read,
                           "a roster this gate should have refused was accepted");

            return problems;
        }

        /// <summary>
        /// A turret is strong against the colour it was bought for and no other, whatever it cost.
        ///
        /// <b>The roster used to carry an exception and does not.</b> A prism reached the next
        /// colour round for a share of a hit; under the colour lock that was worth something only
        /// while its own colour was clear, which the seat beside it was already answering at full
        /// weight — so both its rungs now carry a stun instead (invariant 5d, asked of a purchase).
        /// </summary>
        [Test]
        public void ATurretIsStrongAgainstOneColourAndNoOther()
        {
            foreach (string id in new[] { "bolt", "prism", "spectrum", "breaker", "apex" })
            {
                var ward = new SiegeWard(0, WardCatalog.Default.Find(id));

                Assert.IsTrue(ward.StrongAgainst(0), id);
                Assert.IsFalse(ward.StrongAgainst(1), id);
                Assert.IsFalse(ward.StrongAgainst(2), id);
                Assert.IsFalse(ward.StrongAgainst(3), id);
            }
        }

        /// <summary>
        /// <b>The line grows over a chapter, and the seat gate has to agree with the chapter's
        /// own ward lines.</b>
        ///
        /// <para>
        /// <c>WardSeats.OpensAfter</c> is one integer that mirrors how many rungs stand three
        /// wards, and a table that agrees with content by hand is a table that stops agreeing the
        /// first time either moves. So it is held to the shipped chapter: a seat is open exactly
        /// when a rung that stands it has been cleared, and a seat still shut while a level is
        /// standing it would be a padlock over a turret that is already fighting.
        /// </para>
        /// </summary>
        [Test]
        public void ASeatOpensNoLaterThanTheRungThatStandsIt()
        {
            var rungs = SiegeRuleTests.ShippedWardLines();

            Assert.AreEqual(10, rungs.Count, "the first chapter is ten rungs");

            for (int seat = 0; seat < WardLine.Colours.Length; seat++)
            {
                char colour = WardLine.Colours[seat];

                int first = -1;
                for (int i = 0; i < rungs.Count && first < 0; i++)
                    if (rungs[i].IndexOf(colour) >= 0) first = i;

                Assert.GreaterOrEqual(first, 0,
                                      $"no rung of the first chapter stands a '{colour}' ward, so "
                                      + "a seat for it can never be met");

                // A seat is open once the rung before the first one standing it has been cleared,
                // and never later: `first` rungs cleared is exactly "the player has reached it".
                Assert.LessOrEqual(WardSeats.Needed(seat), first,
                                   $"the '{colour}' seat asks for {WardSeats.Needed(seat)} clears "
                                   + $"and rung {first + 1} already stands it - a padlock over a "
                                   + "turret that is fighting for them");

                Assert.IsTrue(WardSeats.IsOpen(seat, first));
                if (WardSeats.Needed(seat) > 0)
                    Assert.IsFalse(WardSeats.IsOpen(seat, WardSeats.Needed(seat) - 1));
            }
        }

        /// <summary>
        /// At least one seat is shut at the start, and every seat opens eventually.
        ///
        /// <b>Both halves are the check.</b> A gate nothing ever closes is decoration (invariant
        /// 5d) and a gate nothing ever opens is a turret nobody can ever buy.
        /// </summary>
        [Test]
        public void TheGateShutsSomethingAndOpensEverything()
        {
            int shut = 0;

            for (int seat = 0; seat < WardLine.Colours.Length; seat++)
            {
                if (WardSeats.Needed(seat) > 0) shut++;

                Assert.IsTrue(WardSeats.IsOpen(seat, 999),
                              $"the '{WardLine.Colours[seat]}' seat never opens");
            }

            Assert.Greater(shut, 0, "no seat is ever shut, so the gate decides nothing");
            Assert.Less(shut, WardLine.Colours.Length,
                        "every seat is shut at the start, so a new player has no line to arrange");
        }

        /// <summary>A seat this build has never heard of is open rather than shut for ever.</summary>
        [Test]
        public void ASeatOutsideTheTableIsOpen()
        {
            Assert.AreEqual(0, WardSeats.Needed(-1));
            Assert.AreEqual(0, WardSeats.Needed(WardSeats.OpensAfter.Length));
            Assert.IsTrue(WardSeats.IsOpen(99, 0));
        }

        /// <summary>
        /// <b>No two rungs of the shelf are the same turret.</b>
        ///
        /// <para>
        /// The fault the shelf was re-rung to fix, stated as a property. A magnitude that nothing
        /// reads makes two rungs of one ability identical, and both <c>rend</c> and <c>prism</c>
        /// shipped exactly that way - so a thousand-gem breaker was a four-thousand-credit cleaver
        /// with a different hull, and the dearer of the two bought nothing at all. That is the
        /// decoration invariant 5d names, arriving on the one thing a player pays for, and it is
        /// invisible to every other gate: both entries parse, validate, address and play.
        /// </para>
        /// <para>
        /// The free turret is skipped because it is not a rung.
        /// </para>
        /// </summary>
        [Test]
        public void NoTwoRungsOfTheShelfAreTheSameTurret()
        {
            var seen = new Dictionary<string, string>();

            foreach (var model in WardCatalog.Default.Models)
            {
                if (model.IsStarter) continue;

                string shape = model.Ability + " " + model.Magnitude + "/" + model.Extent;

                Assert.IsFalse(seen.TryGetValue(shape, out string first),
                               $"'{first}' and '{model.Id}' are both {shape}, so whichever is "
                               + "dearer buys nothing");

                seen[shape] = model.Id;
            }
        }

        /// <summary>
        /// <b>A family's rungs climb with the shelf</b>: wherever one ability appears twice or
        /// three times, the one further down is stronger.
        ///
        /// <para>
        /// Comparable only <em>within</em> an ability, which is why it is asked that way: 600 gems
        /// and 5,000 credits do not compare and neither does a freeze against a splash, so the
        /// order of the families is authored (invariant 16j) and only this half can be proved.
        /// </para>
        /// <para>
        /// <b>What counts as stronger is the ability's own question</b>, and <c>Pierce</c> is the
        /// one that runs the other way: its extent is how many bolts apart a lance volley is, so a
        /// smaller one is a lance more often.
        /// </para>
        /// </summary>
        [Test]
        public void AFamilysRungsClimbWithTheShelf()
        {
            var below = new Dictionary<WardAbility, WardModel>();

            foreach (var model in WardCatalog.Default.Models)
            {
                if (model.IsStarter) continue;

                if (below.TryGetValue(model.Ability, out var under))
                    Assert.IsTrue(Climbs(model.Ability, under, model),
                                  $"'{model.Id}' sits below '{under.Id}' on the shelf and is no "
                                  + "stronger for it");

                below[model.Ability] = model;
            }
        }

        /// <summary>Whether <paramref name="over"/> is really more than <paramref name="under"/>.</summary>
        static bool Climbs(WardAbility ability, WardModel under, WardModel over)
        {
            if (over.Magnitude < under.Magnitude) return false;

            // A lance runs its lane every `Extent` bolts, so fewer is more often. Everything else
            // measures a span - seconds alight, seconds slowed, raiders arced to - so more is more.
            bool reach = ability == WardAbility.Pierce
                       ? over.Extent <= under.Extent
                       : over.Extent >= under.Extent;

            if (!reach) return false;

            return over.Magnitude > under.Magnitude || over.Extent != under.Extent;
        }

        /// <summary>
        /// <b>A turret drawn with two barrels fires from both of them, and the geometry is one
        /// answer rather than one per screen.</b>
        ///
        /// <para>
        /// The board draws a turret firing and <c>WardFiringStage</c> draws the same turret firing
        /// on the loadout panel. Those were two copies of the arithmetic and only one of them was
        /// taught about barrels, so a twin turret fired from both barrels on the hill and from its
        /// middle on the panel a player opens to decide what it looks like — which is the one
        /// disagreement that panel exists to rule out. Both ask
        /// <c>SiegeView.BarrelStep</c> now, and this is what stops a third copy appearing.
        /// </para>
        /// <para>
        /// <b>Keyed on the rung, never on the id</b>, because the hull <em>is</em> the shelf rung
        /// (the art tool's own rule) — so a turret moved up or down the shelf takes the right
        /// number of barrels with it.
        /// </para>
        /// </summary>
        [Test]
        public void ATurretDrawnWithTwoBarrelsFiresFromBoth()
        {
            var catalog = WardCatalog.Default;
            var twin = catalog.Find("harpoon");
            var single = catalog.Find("bolt");

            // **Named turret by turret rather than by the band.** Hulls T11 to T17 all carry two
            // barrels, but which of them actually fire from both is the owner's call after
            // looking at each on a device - so the ones turned on are pinned by name and the
            // ones deliberately left alone are pinned too, or a later "tidy-up" turns the whole
            // band on and nobody notices.
            foreach (string id in new[] { "leech", "glacier", "breaker", "harpoon" })
                Assert.AreEqual(2, SiegeView.Barrels(catalog.Find(id)), id);

            // Still single, and every one of them is a twin hull: T12, T13 and T16 are drawn with
            // two barrels and fire from one, which is the state `leech` was reported in. They are
            // listed rather than left out so that turning one on is a decision somebody made.
            foreach (string id in new[] { "bolt", "rime", "lighthouse", "pyre",
                                          "spectrum", "howitzer", "arcstorm", "apex" })
                Assert.AreEqual(1, SiegeView.Barrels(catalog.Find(id)), id);

            Assert.AreEqual(17, twin.Order, "the twin hull under test is the one at rung 17");
            Assert.AreEqual(1, SiegeView.Barrels(null),
                            "a turret nobody stood still has to be drawn");

            const float cell = 100f;

            float left = SiegeView.BarrelStep(twin, 0, cell);
            float right = SiegeView.BarrelStep(twin, 1, cell);

            Assert.Less(left, 0f, "the first barrel is the left one");
            Assert.AreEqual(-left, right, .0001f, "the barrels straddle the middle");

            // Far enough apart to be seen, and inside the turret it is drawn on: a bolt leaving
            // from beside the gun is the fault this was written to avoid in the other direction.
            Assert.Greater(right - left, cell * .2f, "two bolts this close read as one");
            Assert.Less(right, cell * SiegeView.BodyWide * .5f,
                        "a barrel outside the turret is a bolt leaving from thin air");

            Assert.AreEqual(0f, SiegeView.BarrelStep(single, 0, cell), .0001f);

            // The flash is drawn smaller when there are two, or the pair is one blob at twice the
            // brightness rather than two barrels.
            Assert.Less(SiegeView.BarrelFlare(twin), 1f);
            Assert.AreEqual(1f, SiegeView.BarrelFlare(single), .0001f);
        }

        /// <summary>
        /// <b>Every turret's figures are inside their bounds, and the roster really spreads.</b>
        ///
        /// <para>
        /// Two different properties and the second is the one worth a fixture. The bounds are
        /// enforced by <c>WardModel</c>'s own clamps, so checking them is cheap insurance; what no
        /// clamp can say is whether the roster <em>uses</em> the range — a shelf where twenty
        /// turrets carry the same two numbers has a stat system nobody can see, which is invariant
        /// 5d's decoration arriving on a card a player reads before paying.
        /// </para>
        /// </summary>
        [Test]
        public void EveryTurretsFiguresAreInBoundsAndTheRosterSpreads()
        {
            var catalog = WardCatalog.Default;
            var powers = new HashSet<int>();
            var guards = new HashSet<int>();

            foreach (var model in catalog.Models)
            {
                Assert.GreaterOrEqual(model.PowerTenths, WardModel.Baseline,
                                      model.Id + " hits softer than the free turret");

                Assert.GreaterOrEqual(model.GuardTenths, SiegeTuning.LeastGuardTenths,
                                      model.Id + " is under the toughness floor");

                Assert.Greater(SiegeTuning.HealthOf(model), 0, model.Id + " falls to nothing");

                powers.Add(model.PowerTenths);
                guards.Add(model.GuardTenths);
            }

            Assert.Greater(powers.Count, 3, "the shelf barely varies what a bolt is worth");
            Assert.Greater(guards.Count, 3, "the shelf barely varies what a turret can take");

            // The starter is the yardstick at both jobs, which is what makes every bar on every
            // card readable against it.
            Assert.AreEqual(WardModel.Baseline, catalog.Starter.PowerTenths);
            Assert.AreEqual(WardModel.Baseline, catalog.Starter.GuardTenths);

            // And the ceilings a stat bar is drawn against are the roster's own.
            Assert.AreEqual(powers.Max(), catalog.MostPowerTenths);
            Assert.AreEqual(guards.Max(), catalog.MostGuardTenths);
        }

        /// <summary>
        /// <b>A dearer rung of one ability is never worse at both jobs.</b>
        ///
        /// Trading weight for toughness is what makes the roster a choice, and it is a choice
        /// <em>between</em> abilities: within one, the rung that costs more must be better at
        /// something and no worse at the rest, or the shelf is asking a player to pay to be
        /// downgraded. Stated as the property rather than checked on the pairs somebody wrote
        /// down, because the roster is content and can grow.
        /// </summary>
        [Test]
        public void ADearerRungIsNeverWorseAtBothJobs()
        {
            var below = new Dictionary<WardAbility, WardModel>();

            foreach (var model in WardCatalog.Default.Models)
            {
                if (model.IsStarter) continue;

                if (below.TryGetValue(model.Ability, out var under))
                    Assert.IsFalse(model.PowerTenths < under.PowerTenths
                                   && model.GuardTenths < under.GuardTenths,
                                   $"'{model.Id}' costs more than '{under.Id}' and is worse at "
                                   + "both jobs");

                below[model.Ability] = model;
            }
        }

        /// <summary>
        /// <b>The shelf reads in three bands, every one of them occupied, and a band starts where
        /// it says it does.</b>
        ///
        /// <para>
        /// A tier is a label on the order the shelf already had — nothing gates on it and nothing
        /// is priced by it — so the only way it can be wrong is by describing a shelf that is no
        /// longer there. The roster is content and can grow, so this is the property rather than
        /// the twenty answers somebody wrote down: an empty band is a header with nothing under
        /// it, and a boundary past the end of the roster is a header nobody ever scrolls to.
        /// </para>
        /// </summary>
        [Test]
        public void TheShelfReadsInThreeOccupiedBands()
        {
            var catalog = WardCatalog.Default;
            var held = new Dictionary<int, int>();

            foreach (var model in catalog.Models)
            {
                int tier = WardTier.Of(model);

                Assert.GreaterOrEqual(tier, 1, model.Id);
                Assert.LessOrEqual(tier, WardTier.Count, model.Id);

                held[tier] = held.TryGetValue(tier, out int n) ? n + 1 : 1;
            }

            for (int tier = 1; tier <= WardTier.Count; tier++)
                Assert.IsTrue(held.ContainsKey(tier),
                              "band " + tier + " has a header and nothing under it");

            // Exactly one turret opens each band, and it is the one the boundary names.
            for (int tier = 1; tier <= WardTier.Count; tier++)
            {
                int opens = 0;

                foreach (var model in catalog.Models)
                    if (WardTier.Of(model) == tier && WardTier.Starts(model)) opens++;

                Assert.AreEqual(1, opens, "band " + tier + " does not open exactly once");
            }

            // The shelf the owner asked for: ten, then seven, then three.
            Assert.AreEqual(10, held[1]);
            Assert.AreEqual(7, held[2]);
            Assert.AreEqual(3, held[3]);

            // A band boundary is a shelf rung, so it has to name one.
            for (int tier = 1; tier <= WardTier.Count; tier++)
                Assert.LessOrEqual(WardTier.OpensAt(tier), catalog.Count,
                                   "band " + tier + " opens past the end of the shelf");
        }

        // ------------------------------------------------------------- the upgrade ladder
        /// <summary>
        /// <b>Every turret starts at one star, tops out at five, and every rung between costs
        /// more than the one below it.</b>
        ///
        /// A rung that costs no more than the one under it is a rung nobody chooses between, and
        /// nought is not a cheap upgrade — it is how <c>WardStars.PriceOf</c> says there is no
        /// next star at all, so an authored nought would silently top a turret out.
        /// </summary>
        [Test]
        public void TheUpgradeLadderClimbsAndStopsAtTheTop()
        {
            foreach (var model in WardCatalog.Default.Models)
            {
                int below = 0;

                for (int stars = WardStars.Least; stars < WardStars.Most; stars++)
                {
                    int price = WardStars.PriceOf(model, stars);

                    Assert.Greater(price, 0, $"'{model.Id}' prices star {stars + 1} at nought");
                    Assert.Greater(price, below,
                                   $"'{model.Id}' prices star {stars + 1} at {price}, no more "
                                   + $"than the {below} below it");

                    below = price;
                }

                Assert.AreEqual(0, WardStars.PriceOf(model, WardStars.Most),
                                model.Id + " prices a star past the top");

                // A band's ladder is its band's, so two turrets of one band cost the same to take
                // to the top and two bands do not.
                Assert.Greater(WardStars.WholeLadder(model), 0);
            }

            // The owner's three ladders, by band.
            Assert.AreEqual(49000, WardStars.WholeLadder(WardCatalog.Default.Find("bolt")));
            Assert.AreEqual(102000, WardStars.WholeLadder(WardCatalog.Default.Find("leech")));
            Assert.AreEqual(248000, WardStars.WholeLadder(WardCatalog.Default.Find("apex")));
        }

        /// <summary>
        /// <b>A star is worth the same ten per cent to a bolt and to a chassis, and a turret at
        /// the first star is exactly its model.</b>
        ///
        /// The second half is what makes every content gate, offline mirror and rule test still
        /// mean what it did: they all play an un-upgraded line, so a star that changed anything at
        /// the first rung would have moved every one of them without touching a number.
        /// </summary>
        [Test]
        public void AStarLiftsTheBoltAndTheChassisAndTheFirstOneChangesNothing()
        {
            var model = WardCatalog.Default.Find("cleaver");

            var fresh = new WardBuild(model, WardStars.Least);
            Assert.AreEqual(model.PowerTenths * 10, fresh.PowerHundredths);
            Assert.AreEqual(model.GuardTenths * 10, fresh.GuardHundredths);
            Assert.AreEqual(SiegeTuning.HealthOf(model), SiegeTuning.HealthOf(fresh));

            int wasDamage = SiegeTuning.DamageTo(SiegeKind.Creeper, 0, false, fresh);
            int wasHealth = SiegeTuning.HealthOf(fresh);

            var topped = new WardBuild(model, WardStars.Most);

            Assert.Greater(topped.PowerHundredths, fresh.PowerHundredths);
            Assert.Greater(topped.GuardHundredths, fresh.GuardHundredths);
            Assert.Greater(SiegeTuning.DamageTo(SiegeKind.Creeper, 0, false, topped), wasDamage);
            Assert.Greater(SiegeTuning.HealthOf(topped), wasHealth);

            // Never past the top and never under the first, whatever it is handed.
            Assert.AreEqual(WardStars.Most, new WardBuild(model, 99).Stars);
            Assert.AreEqual(WardStars.Least, new WardBuild(model, -3).Stars);
            Assert.AreEqual(WardStars.Least, new WardBuild(model, 0).Stars);
        }

        /// <summary>
        /// <b>An upgraded turret never hits softer than the free one</b>, which is the load-bearing
        /// rule of the whole roster asked of the second ladder.
        ///
        /// A star only ever adds, so this cannot fail by arithmetic — it is here because the
        /// <em>data</em> could change under it, and a roster whose stars took something away would
        /// push three stars out of reach of whoever paid for them.
        /// </summary>
        [Test]
        public void NoStarEverMakesABoltWeaker()
        {
            var starter = WardCatalog.Default.Starter;

            foreach (var model in WardCatalog.Default.Models)
                for (int stars = WardStars.Least; stars <= WardStars.Most; stars++)
                {
                    var build = new WardBuild(model, stars);

                    Assert.GreaterOrEqual(build.PowerHundredths, starter.PowerTenths * 10,
                                          $"'{model.Id}' at {stars} stars");

                    Assert.GreaterOrEqual(
                        SiegeTuning.DamageTo(SiegeKind.Creeper, 0, false, build),
                        SiegeTuning.DamageTo(SiegeKind.Creeper, 0, false, starter),
                        $"'{model.Id}' at {stars} stars");
                }
        }

        /// <summary>
        /// <b>Two devices' ladders join to the further of each, and the join is idempotent and
        /// order-independent.</b>
        ///
        /// The one legal shape for a stored count (invariant 11b): an upgrade cannot be undone, so
        /// a maximum loses nothing and a device that ran the join twice, or the other way round,
        /// reaches the same ladder.
        /// </summary>
        [Test]
        public void TheLadderIsJoinedByTakingTheFurtherOfEach()
        {
            var mine = new[]
            {
                new WardStarDto { ward = "mortar:r", stars = 4 },
                new WardStarDto { ward = "rime:b", stars = 2 },
            };

            var other = new[]
            {
                new WardStarDto { ward = "mortar:r", stars = 2 },
                new WardStarDto { ward = "apex:y", stars = 5 },
            };

            var a = WardStarLedger.Join(mine, other);
            var b = WardStarLedger.Join(other, mine);

            Assert.AreEqual(3, a.Length);
            Assert.AreEqual(a.Length, b.Length);

            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].ward, b[i].ward, "the rows are not written in one order");
                Assert.AreEqual(a[i].stars, b[i].stars);
            }

            var byWard = new Dictionary<string, int>();
            foreach (var row in a) byWard[row.ward] = row.stars;

            Assert.AreEqual(4, byWard["mortar:r"], "the further of the two was not kept");
            Assert.AreEqual(2, byWard["rime:b"]);
            Assert.AreEqual(5, byWard["apex:y"]);

            // Idempotent: joining the answer with itself changes nothing.
            var twice = WardStarLedger.Join(a, a);
            Assert.AreEqual(a.Length, twice.Length);

            // A row at the first star is the absent state, so it is never written down.
            var bare = WardStarLedger.Join(
                new[] { new WardStarDto { ward = "bolt:r", stars = WardStars.Least } }, null);

            Assert.IsEmpty(bare, "a turret at the first star wrote a row");

            // And a row from a newer build cannot stand a turret past the top.
            var wild = WardStarLedger.Join(
                new[] { new WardStarDto { ward = "bolt:r", stars = 99 } }, null);

            Assert.AreEqual(WardStars.Most, wild[0].stars);
        }

        /// <summary>
        /// <b>The stars a player bought reach the board, and a line that is told nothing plays at
        /// the first star.</b>
        ///
        /// The second half is what every content gate and rule test relies on: they resolve a line
        /// with no ladder at all, so "no lookup" has to mean "un-upgraded" rather than "nought".
        /// </summary>
        [Test]
        public void TheStarsAPlayerBoughtReachTheBoard()
        {
            var catalog = WardCatalog.Default;

            var chosen = new List<WardSlot>();
            foreach (char colour in WardLine.Colours)
                chosen.Add(new WardSlot(colour, "cleaver"));

            var plain = WardLine.Resolve(catalog, chosen, (_, __) => true);
            Assert.AreEqual(WardStars.Least, plain.BuildAt(0).Stars);

            var risen = WardLine.Resolve(catalog, chosen, (_, __) => true,
                                         (_, __) => WardStars.Most);

            Assert.AreEqual(WardStars.Most, risen.BuildAt(0).Stars);

            var flat = SiegeBoard.Build(Layout(), plain);
            var strong = SiegeBoard.Build(Layout(), risen);

            Assert.Greater(strong.Wards[0].Full, flat.Wards[0].Full,
                           "an upgraded turret stands no longer than a fresh one");

            Assert.Greater(
                SiegeTuning.DamageTo(SiegeKind.Creeper, 0, false, strong.Wards[0].Build),
                SiegeTuning.DamageTo(SiegeKind.Creeper, 0, false, flat.Wards[0].Build),
                "an upgraded turret hits no harder than a fresh one");
        }

        /// <summary>
        /// <b>The figures a card shows move when a turret is upgraded, and they are the board's
        /// own.</b>
        ///
        /// <para>
        /// The bug this pins shipped: the stat bars were built once from a <c>WardModel</c>, so
        /// they never carried a turret's stars and never changed when one was bought — a player
        /// upgraded a turret and watched nothing happen on the one screen that exists to say what
        /// an upgrade is worth. What makes it unrepeatable is that the figures are read back
        /// through <c>SiegeTuning</c> from a <c>WardBuild</c>, so a card and a board cannot hold
        /// two opinions.
        /// </para>
        /// <para>
        /// Asked of the arithmetic rather than of the widgets, because a Unity UI cannot be built
        /// in an offline run — what a fixture can prove is that there is something to draw.
        /// </para>
        /// </summary>
        [Test]
        public void UpgradingATurretMovesTheFiguresACardShows()
        {
            foreach (var model in WardCatalog.Default.Models)
            {
                var was = new WardBuild(model, WardStars.Least);
                var now = was.Risen();

                int wasHit = SiegeTuning.DamageFine(0, was.PowerHundredths);
                int nowHit = SiegeTuning.DamageFine(0, now.PowerHundredths);

                Assert.Greater(nowHit, wasHit,
                               $"'{model.Id}' shows the same damage after an upgrade");

                Assert.Greater(SiegeTuning.HealthOf(now), SiegeTuning.HealthOf(was),
                               $"'{model.Id}' shows the same health after an upgrade");

                // And every rung after the first moves them too, so no star on the ladder is one
                // a player pays for and cannot see.
                var climbing = was;

                for (int star = WardStars.Least; star < WardStars.Most; star++)
                {
                    var next = climbing.Risen();

                    Assert.Greater(SiegeTuning.DamageFine(0, next.PowerHundredths),
                                   SiegeTuning.DamageFine(0, climbing.PowerHundredths),
                                   $"'{model.Id}' star {next.Stars} buys no damage");

                    Assert.Greater(SiegeTuning.HealthOf(next), SiegeTuning.HealthOf(climbing),
                                   $"'{model.Id}' star {next.Stars} buys no health");

                    climbing = next;
                }
            }
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

                // **Every priced turret is behind a keeper level, gems included.** It used to be
                // the reverse — a gate belonged to a credit price alone — and what that made was
                // a shelf whose dearest half could be taken in any order by anybody holding gems.
                if (!model.IsStarter)
                    Assert.Greater(model.MinLevel, 0,
                                   model.Id + " is priced and asks for no keeper level");

                starter |= model.IsStarter;
                orders.Add(model.Order);
            }

            orders.Sort();
            for (int i = 0; i < orders.Count; i++)
                Assert.AreEqual(i + 1, orders[i], "the shelf's ladder has a gap or a tie");

            Assert.IsTrue(starter, "no turret is free, so a new player stands an empty line");
            Assert.IsNotNull(catalog.Starter);
            Assert.IsTrue(catalog.Starter.IsStarter);

            Assert.IsNull(catalog.LadderProblem(), catalog.LadderProblem());
        }

        /// <summary>
        /// <b>Every wall refuses somebody, none of them asks for more than the shelf's ceiling,
        /// and the ceiling is a number the shelf actually reaches.</b>
        ///
        /// <para>
        /// Invariant 5d asked of the one rule that is left. The shelf used to be climbed a rung
        /// at a time — a turret sealed until the one below it was held — so the walls were the
        /// second of two conditions and a slack one cost nothing. With the seal gone at the
        /// owner's decision, a keeper level is the whole of what opens a rung: a wall of nought
        /// would hand a turret to a player on their first launch, and the reader refuses one, but
        /// nothing in the build said the other end had a limit at all.
        /// </para>
        /// <para>
        /// <b>Both ends are the check.</b> A ceiling nothing reaches is a number nobody is
        /// tuning against; a rung above it is a padlock no amount of play opens, which is the
        /// same fault the home ladder's gates are watched for.
        /// </para>
        /// </summary>
        [Test]
        public void EveryWallRefusesSomebodyAndTheShelfReachesItsCeiling()
        {
            int top = 0;

            foreach (var model in WardCatalog.Default.Models)
            {
                if (model.IsStarter)
                {
                    Assert.AreEqual(0, model.MinLevel,
                                    model.Id + " is free and behind a wall, which is a turret "
                                    + "handed over and withheld at once");
                    continue;
                }

                Assert.Greater(model.MinLevel, 0,
                               model.Id + " is priced and asks for no keeper level, so a player "
                               + "on their first launch is offered it");

                Assert.LessOrEqual(model.MinLevel, WardTier.TopLevel,
                                   model.Id + " asks for keeper level " + model.MinLevel
                                   + ", above the shelf's ceiling of " + WardTier.TopLevel);

                if (model.MinLevel > top) top = model.MinLevel;
            }

            Assert.AreEqual(WardTier.TopLevel, top,
                            "no rung of the shelf reaches the ceiling, so it is a number nothing "
                            + "is being tuned against");
        }

        /// <summary>
        /// <b>A wall that falls as the shelf climbs is refused; two rungs sharing one is not.</b>
        ///
        /// <para>
        /// Both halves are the check, and both of them moved when the seal went. A tie used to be
        /// refused because a rung was sealed until the one below it was bought — so reaching it
        /// meant having met every wall under it, and a level an earlier rung had already asked
        /// for could never refuse anybody (invariant 5d). With no seal, twenty refuses everybody
        /// under twenty whatever stands beside it, so a tie is legal content and refusing it
        /// would be a gate turning away a file that is right.
        /// </para>
        /// <para>
        /// A fall is still refused, on the plainer ground: the shelf is ordered by how much of the
        /// hill an ability reaches and its prices climb with it (invariant 37ax), so a wall that
        /// drops opens the dearer, further-reaching turret first.
        /// </para>
        /// </summary>
        [Test]
        public void AWallThatFallsIsRefusedAndATieIsNot()
        {
            var problems = new List<string>();

            var dto = new GlimmerGrove.Content.WardsDto
            {
                models = new[]
                {
                    Entry("free", 0, 0, 0, 1),
                    Entry("first", 0, 1000, 6, 2),
                    Entry("second", 0, 2000, 5, 3),
                },
            };

            // Six then five: the shelf climbs and its wall does not, so the whole file is refused
            // and the built-in roster stands.
            Assert.AreSame(WardCatalog.Default, WardCatalog.Resolve(dto, problems));
            Assert.IsNotEmpty(problems);

            // The same wall twice, which the old rule refused and this one allows.
            problems.Clear();
            dto.models[2] = Entry("second", 0, 2000, 6, 3);

            var tied = WardCatalog.Resolve(dto, problems);
            Assert.AreNotSame(WardCatalog.Default, tied, string.Join("; ", problems));
            Assert.AreEqual(3, tied.Count);
        }

        /// <summary>
        /// <b>A wall outside the band whose header it is drawn under is refused.</b>
        ///
        /// <para>
        /// The rule the removal of the seal left uncovered, and the reason <c>WardTier</c> stopped
        /// being a caption. With nothing forcing the order, the three headers are all a player has
        /// to go on: TIER II means "this stretch opens between keeper level twenty and
        /// twenty-nine" and nothing else in the build says so. A rung authored outside its band
        /// parses, prices, validates and plays — what it does is put a lie in a header, which is
        /// exactly the class of fault no numeric gate can see.
        /// </para>
        /// </summary>
        [Test]
        public void AWallOutsideItsOwnBandIsRefused()
        {
            // The shipped roster, with the first rung of the second band dropped into the first
            // band's stretch. It still climbs and it is still under the rung above it, so nothing
            // but the band can see it.
            var models = new List<GlimmerGrove.Content.WardModelDto>();

            foreach (var model in WardCatalog.Default.Models)
                models.Add(Entry(model.Id, model.GemPrice, model.CoinPrice,
                                 model.MinLevel, model.Order));

            int band = WardTier.OpensAt(2);
            var offender = models.Find(m => m.order == band);

            Assert.IsNotNull(offender, "the second band opens at rung " + band);

            offender.minLevel = WardTier.ClosesAtLevel(1);

            var problems = new List<string>();
            var dto = new GlimmerGrove.Content.WardsDto { models = models.ToArray() };

            Assert.AreSame(WardCatalog.Default, WardCatalog.Resolve(dto, problems),
                           "a rung standing below its own band's opening level was accepted");
            Assert.IsNotEmpty(problems);

            // And the shipped roster itself stands in its bands, which is the half that catches a
            // retune rather than a typo.
            foreach (var model in WardCatalog.Default.Models)
            {
                if (model.IsStarter) continue;

                int tier = WardTier.Of(model);

                Assert.GreaterOrEqual(model.MinLevel, WardTier.OpensAtLevel(tier), model.Id);
                Assert.LessOrEqual(model.MinLevel, WardTier.ClosesAtLevel(tier), model.Id);
            }
        }

        /// <summary>
        /// <b>A priced turret that asks for no keeper level is refused</b>, gems included. Nought
        /// used to be how a gem price said "ungated" and is now a content mistake.
        /// </summary>
        [Test]
        public void APricedTurretWithNoKeeperLevelIsRefused()
        {
            var problems = new List<string>();

            var dto = new GlimmerGrove.Content.WardsDto
            {
                models = new[] { Entry("free", 0, 0, 0, 1), Entry("gemmed", 600, 0, 0, 2) },
            };

            Assert.AreSame(WardCatalog.Default, WardCatalog.Resolve(dto, problems));
            Assert.IsNotEmpty(problems);
        }

        static GlimmerGrove.Content.WardModelDto Entry(string id, int gems, int coins, int level, int order)
            => new GlimmerGrove.Content.WardModelDto
            {
                id = id, ability = "none", gemPrice = gems, coinPrice = coins,
                minLevel = level, order = order,
            };

        /// <summary>
        /// <b>"Free" asks both prices</b>, which is invariant 16j's hard-won correction: it was
        /// <c>Cost &lt;= 0</c> for as long as there was one currency, and the day a second one
        /// arrived every gem-priced thing read as free.
        /// </summary>
        [Test]
        public void AGemPricedTurretIsNotFree()
        {
            var gemmed = new WardModel("x", WardAbility.None, 0, 0, 600, 0, 15, 1);
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
            var all = WardLine.Resolve(catalog, chosen, (_, __) => true);
            Assert.AreEqual("mortar", all.At(0).Id);
            Assert.AreEqual(starter.Id, all.At(1).Id);
            Assert.AreEqual("rime", all.At(2).Id);
            Assert.AreEqual(starter.Id, all.At(3).Id);

            // Nothing held: every slot falls back, and the line is still four turrets.
            var none = WardLine.Resolve(catalog, chosen, (m, _) => m.IsStarter);
            for (int i = 0; i < WardLine.Colours.Length; i++)
                Assert.AreEqual(starter.Id, none.At(i).Id, "colour " + i);

            Assert.AreEqual(WardLine.Colours.Length, none.Models.Count);
        }

        // ------------------------------------------------------------- bought per colour
        /// <summary>
        /// <b>A turret bought for red is not held on green.</b>
        ///
        /// The rule the whole feature turns on, asked of the one body that answers it: a line
        /// holds four turrets and a colour is what a level's hill decides, so one purchase
        /// covering all four seats is buying one decision and receiving four.
        /// </summary>
        [Test]
        public void ATurretBoughtForOneColourIsNotHeldOnAnother()
        {
            var catalog = WardCatalog.Default;
            var mortar = catalog.Find("mortar");
            var held = new HashSet<string> { WardHolding.Key("mortar", 'r') };

            Assert.IsTrue(WardLedger.IsHeld(mortar, 'r', held.Contains));

            foreach (char colour in "gby")
                Assert.IsFalse(WardLedger.IsHeld(mortar, colour, held.Contains),
                               "mortar reads as held on " + colour);

            // The free one is held everywhere and is never written down (invariant 16e/16f).
            foreach (char colour in WardLine.Colours)
                Assert.IsTrue(WardLedger.IsHeld(catalog.Starter, colour, held.Contains));
        }

        /// <summary>
        /// <b>A row with no colour on it means every colour</b>, which is what a build that owned
        /// turrets outright wrote.
        ///
        /// The only reading a union merge could safely give it: one colour, or none, would
        /// confiscate something somebody paid for the first time an old file met a new build.
        /// </summary>
        [Test]
        public void ARowWithNoColourMeansEveryColour()
        {
            var mortar = WardCatalog.Default.Find("mortar");
            var held = new HashSet<string> { "mortar" };

            foreach (char colour in WardLine.Colours)
                Assert.IsTrue(WardLedger.IsHeld(mortar, colour, held.Contains),
                              "a bare row was not honoured on " + colour);

            Assert.AreEqual("mortar", WardHolding.IdOf("mortar"));
            Assert.AreEqual("mortar", WardHolding.IdOf(WardHolding.Key("mortar", 'b')));

            Assert.IsTrue(WardHolding.TryRead("mortar:b", out string id, out char read));
            Assert.AreEqual("mortar", id);
            Assert.AreEqual('b', read);

            // A bare row has no single colour to hand back, and a row naming a colour this build
            // does not know is not one it wrote.
            Assert.IsFalse(WardHolding.TryRead("mortar", out _, out _));
            Assert.IsFalse(WardHolding.TryRead("mortar:x", out _, out _));
        }

        /// <summary>
        /// <b>A stored choice is refused on a colour it was not bought for</b>, which is the one
        /// place the per-colour rule could otherwise be undone: the line is what a board plays.
        /// </summary>
        [Test]
        public void ALineWillNotStandATurretBoughtForAnotherColour()
        {
            var catalog = WardCatalog.Default;
            var starter = catalog.Starter;
            var held = new HashSet<string> { WardHolding.Key("mortar", 'r') };

            var chosen = new[] { new WardSlot('r', "mortar"), new WardSlot('g', "mortar") };

            var line = WardLine.Resolve(catalog, chosen,
                                        (m, c) => WardLedger.IsHeld(m, c, held.Contains));

            Assert.AreEqual("mortar", line.At(0).Id, "the seat it was bought for");
            Assert.AreEqual(starter.Id, line.At(1).Id, "a seat it was not bought for");
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

                // Its own full health, read when it was built, and standing at it.
                Assert.AreEqual(SiegeTuning.HealthOf(ward.Model), ward.Full);
                Assert.AreEqual(ward.Full, ward.Health);
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
