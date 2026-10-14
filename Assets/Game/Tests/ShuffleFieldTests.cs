using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using GlimmerGrove.Shuffle;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The Shuffle lane's 2026-10-14 cards (MODES.md 59m): the field cards (power gems, explosive
    /// gems), the pacts, the synergies, the wider hand and the laser gun. Each is held to the
    /// figure or the rule its card names, and to doing nothing on a board that holds no build.
    /// </summary>
    [TestFixture]
    public sealed class ShuffleFieldTests
    {
        // ------------------------------------------------------------------ the fields
        /// <summary>
        /// Settled (nothing lined up), and one swap - cell 10 down into cell 18 - makes row two a
        /// run of four reds and nothing else.
        /// </summary>
        static readonly string[] Four =
        {
            "gbygbygb",
            "byrbygby",
            "rrgrgbyg",
            "ygbybygb",
            "bygbgbyg",
        };

        /// <summary>The same swap makes row two a run of five.</summary>
        static readonly string[] Five =
        {
            "gbygbygb",
            "byrbygby",
            "rrgrrbyg",
            "ygbybygb",
            "bygbgbyg",
        };

        const int Above = 10, Below = 18;

        static SiegeLayout Field(string[] rows)
        {
            Assert.IsTrue(ProtoGrid.TryRead(rows, 8, 5, SiegeLayout.Cells, out var grid, out string error), error);

            // No charms dealt, so a refill can only deal plain gems and what a case reads off
            // the field after a swap is what the swap did.
            var layout = new SiegeLayout(grid, "rgby", "rgby", new[] { "rgby", "rgbyrgby" }, null);
            Assert.IsNull(layout.Fault, layout.Fault);
            return layout;
        }

        static SiegeBoard Board(string[] rows, params string[] cards)
        {
            var board = SiegeBoard.Build(Field(rows));
            if (cards.Length == 0) return board;

            var build = new ShuffleBuild(1u);
            foreach (var id in cards)
            {
                var card = ShuffleCards.Find(id);
                Assert.IsNotNull(card, id);
                Assert.IsTrue(build.Take(card), id);
            }

            board.Boosts = build.Boosts;
            board.Refit();
            return board;
        }

        static IEnumerable<SiegeCharm> Sprung(SiegeTurn turn)
        {
            foreach (var beat in turn.Beats)
                foreach (var spark in beat.Sprung)
                    yield return spark.Charm;
        }

        // ------------------------------------------------------------------ power gems
        [Test]
        public void AFourForgesARocketWhereThePlayerMoved()
        {
            var board = Board(Four, "power_gems");
            var turn = board.Swap(Above, Below);
            Assert.IsNotNull(turn, "the swap lined nothing up");

            var first = turn.Beats[0];
            Assert.AreEqual(1, first.Forged.Count, "a run of four forged nothing");

            var forged = first.Forged[0];
            Assert.AreEqual(SiegeCharm.RocketRow, forged.Charm, "a row of four forged the wrong gem");
            Assert.AreEqual(Below, forged.Cell, "the rocket did not stand where the player moved");
            Assert.AreEqual(SiegeLayout.Letters.IndexOf('r'), forged.Colour);
            Assert.AreEqual(3, forged.Run.Length);

            CollectionAssert.DoesNotContain(first.Cleared, Below, "the forge cleared its own cell");
            foreach (int cell in forged.Run) CollectionAssert.Contains(first.Cleared, cell);

            // It stands on the field afterwards - or a cascade has already set it off.
            bool standing = false;
            for (int i = 0; i < board.Count; i++) if (board.CharmAt(i) == SiegeCharm.RocketRow) standing = true;
            Assert.IsTrue(standing || new List<SiegeCharm>(Sprung(turn)).Contains(SiegeCharm.RocketRow),
                          "the rocket was forged and then neither stood nor went off");
        }

        [Test]
        public void AFiveForgesANova()
        {
            var board = Board(Five, "power_gems");
            var turn = board.Swap(Above, Below);
            Assert.IsNotNull(turn);

            Assert.AreEqual(1, turn.Beats[0].Forged.Count);
            Assert.AreEqual(SiegeCharm.Nova, turn.Beats[0].Forged[0].Charm);
            Assert.AreEqual(4, turn.Beats[0].Forged[0].Run.Length);
        }

        [Test]
        public void ARocketTakesItsWholeLine()
        {
            // A rocket stood on the run's first cell, and no build at all: a power gem goes off
            // through `Spring` whoever made it.
            var board = Board(Four);
            board.Stand(16, SiegeCharm.RocketRow);

            var turn = board.Swap(Above, Below);
            Assert.IsNotNull(turn);

            var first = turn.Beats[0];
            CollectionAssert.Contains(new List<SiegeCharm>(Sprung(turn)), SiegeCharm.RocketRow);
            for (int x = 0; x < 8; x++) CollectionAssert.Contains(first.Cleared, 16 + x, $"the rocket missed cell {16 + x}");
            Assert.AreEqual(0, first.Forged.Count, "a board with no build forged a gem");
        }

        [Test]
        public void ANovaTakesEveryGemOfItsColour()
        {
            var board = Board(Four);
            board.Stand(16, SiegeCharm.Nova);

            int reds = 0;
            for (int i = 0; i < board.Count; i++) if (board.At(i) == 'r') reds++;

            var turn = board.Swap(Above, Below);
            Assert.IsNotNull(turn);

            int took = 0;
            var first = turn.Beats[0];
            for (int i = 0; i < first.Cleared.Count; i++)
                if (first.Paid[i] == SiegeLayout.Letters.IndexOf('r')) took++;

            Assert.AreEqual(reds, took, "the nova left a gem of its colour standing");
        }

        // ------------------------------------------------------------------ explosive gems
        [Test]
        public void AMatchBlowsUpTheGemsAroundIt()
        {
            var board = Board(Four, "blast_gems");
            var turn = board.Swap(Above, Below);
            Assert.IsNotNull(turn);

            var first = turn.Beats[0];
            Assert.AreEqual(1, first.Blasts.Count, "one run, one blast");

            // Row two's four reds: the four above, the four below and the one at its open end.
            CollectionAssert.AreEquivalent(new[] { 8, 9, 10, 11, 24, 25, 26, 27, 20 }, first.Blasted);
            foreach (int cell in first.Blasted) CollectionAssert.Contains(first.Cleared, cell);
        }

        [Test]
        public void ASecondBlastReachesTheCorners()
        {
            var board = Board(Four, "blast_gems", "blast_gems");
            var turn = board.Swap(Above, Below);
            Assert.IsNotNull(turn);

            // The corners at the run's open end join the sides and the end.
            CollectionAssert.IsSubsetOf(new[] { 12, 28 }, turn.Beats[0].Blasted);
            Assert.AreEqual(11, turn.Beats[0].Blasted.Count);
        }

        [Test]
        public void ABlastSparesTheGemBeingForged()
        {
            var board = Board(Four, "blast_gems", "power_gems");
            var turn = board.Swap(Above, Below);
            Assert.IsNotNull(turn);

            var first = turn.Beats[0];
            Assert.AreEqual(1, first.Forged.Count);
            CollectionAssert.DoesNotContain(first.Blasted, first.Forged[0].Cell);
            CollectionAssert.DoesNotContain(first.Cleared, first.Forged[0].Cell);
        }

        /// <summary>
        /// <b>The plain line forges nothing and blows up nothing</b>: a shipped-shape field swapped
        /// on a board holding no build clears the run and only the run.
        /// </summary>
        [Test]
        public void ThePlainFieldNeitherForgesNorBlasts()
        {
            var board = Board(Five);
            var turn = board.Swap(Above, Below);
            Assert.IsNotNull(turn);

            foreach (var beat in turn.Beats)
            {
                Assert.AreEqual(0, beat.Forged.Count);
                Assert.AreEqual(0, beat.Blasts.Count);
                Assert.AreEqual(0, beat.Blasted.Count);
            }

            Assert.AreEqual(5, turn.Beats[0].Cleared.Count, "the plain field cleared more than its run");
        }

        /// <summary>A field card draws nothing from any stream: two boards swapped alike deal alike.</summary>
        [Test]
        public void TheFieldCardsDrawNothingFromTheStream()
        {
            var a = Board(Five, "power_gems", "blast_gems");
            var b = Board(Five, "power_gems", "blast_gems");

            a.Swap(Above, Below);
            b.Swap(Above, Below);

            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a.At(i), b.At(i), $"cell {i}");
                Assert.AreEqual(a.CharmAt(i), b.CharmAt(i), $"cell {i}");
            }
        }

        // ------------------------------------------------------------------ chain fuel
        [Test]
        public void AChainPaysMoreOnEveryBeatPastTheFirst()
        {
            var live = new SiegeBoosts(1u);
            Assert.AreEqual(2f, live.FuelGem(2f, 3), 1e-6f, "a build without the card paid a chain");

            live.AddChainFuel(40);
            Assert.AreEqual(2f, live.FuelGem(2f, 1), 1e-6f, "the swap's own beat paid a chain");
            Assert.AreEqual(2.8f, live.FuelGem(2f, 2), 1e-5f);
            Assert.AreEqual(3.6f, live.FuelGem(2f, 3), 1e-5f);
            Assert.AreEqual(6f, live.FuelGem(2f, 99), 1e-5f, "a deep chain passed its ceiling");

            Assert.AreEqual(2f, SiegeBoosts.None.FuelGem(2f, 5), 1e-6f);
        }

        // ------------------------------------------------------------------ the pacts
        [Test]
        public void APactMultipliesAndTwoPactsStack()
        {
            var b = new ShuffleBuild(1u);
            b.Take(ShuffleCards.Find("frenzy_pact"));

            // Three times as fast, half the damage.
            Assert.AreEqual(50, b.Boosts.Bolt(100, SiegeKind.Creeper, out _));
            Assert.AreEqual(.30f, b.Boosts.FireEvery(.9f, 100), 1e-5f);

            // Taken twice: a quarter of the damage, never nothing, and never quicker than the floor.
            b.Take(ShuffleCards.Find("frenzy_pact"));
            Assert.AreEqual(25, b.Boosts.Bolt(100, SiegeKind.Creeper, out _));
            Assert.AreEqual(SiegeBoosts.QuickestShot, b.Boosts.FireEvery(.22f, 100), 1e-6f);

            // A titan's double on top of a frenzy's half is the hit it was.
            var c = new ShuffleBuild(1u);
            c.Take(ShuffleCards.Find("titan_pact"));
            c.Take(ShuffleCards.Find("frenzy_pact"));
            Assert.AreEqual(100, c.Boosts.Bolt(100, SiegeKind.Creeper, out _));

            // And the order cards came in moves nothing.
            var d = new ShuffleBuild(1u);
            d.Take(ShuffleCards.Find("frenzy_pact"));
            d.Take(ShuffleCards.Find("titan_pact"));
            Assert.AreEqual(c.Boosts.PactDamage, d.Boosts.PactDamage);
            Assert.AreEqual(c.Boosts.PactFire, d.Boosts.PactFire);
        }

        [Test]
        public void EachPactCostsWhatItSays()
        {
            var miser = new ShuffleBuild(1u);
            miser.Take(ShuffleCards.Find("miser_pact"));
            Assert.AreEqual(1f, miser.Boosts.FuelShot(2f), 1e-6f);
            Assert.AreEqual(80, miser.Boosts.Bolt(100, SiegeKind.Creeper, out _));

            var blood = new ShuffleBuild(1u);
            blood.Take(ShuffleCards.Find("blood_pact"));
            Assert.AreEqual(50, blood.Boosts.Guarded(100));
            Assert.AreEqual(120, blood.Boosts.Bolt(100, SiegeKind.Creeper, out _));

            var reckless = new ShuffleBuild(1u);
            reckless.Take(ShuffleCards.Find("reckless_pact"));
            Assert.AreEqual(1.2f, reckless.Boosts.HillPace, 1e-6f);
            Assert.AreEqual(135, reckless.Boosts.Bolt(100, SiegeKind.Creeper, out _));

            // The plain line is untouched by every reading a pact moves.
            Assert.AreEqual(100, SiegeBoosts.None.Guarded(100));
            Assert.AreEqual(2f, SiegeBoosts.None.FuelShot(2f), 1e-6f);
        }

        [Test]
        public void ABloodPactTakesWardHealthOnTheBoard()
        {
            var plain = Board(Four);
            var bled = Board(Four, "blood_pact");

            for (int w = 0; w < plain.Wards.Count; w++)
                Assert.AreEqual(plain.Wards[w].Full / 2, bled.Wards[w].Full, $"ward {w}");
        }

        // ------------------------------------------------------------------ the synergies
        [Test]
        public void ASynergyReadsTheFigureItFeedsOff()
        {
            var b = new ShuffleBuild(1u);
            b.Take(ShuffleCards.Find("kinetic_gems"));
            Assert.AreEqual(2f, b.Boosts.FuelGem(2f), 1e-6f, "kinetic gems paid with no bonus speed");

            b.Take(ShuffleCards.Find("quick_barrels"));              // +12% fire
            Assert.AreEqual(2f * 106 / 100f, b.Boosts.FuelGem(2f), 1e-5f);

            var iron = new ShuffleBuild(1u);
            iron.Take(ShuffleCards.Find("iron_posts"));              // +30% health
            iron.Take(ShuffleCards.Find("iron_cannons"));
            Assert.AreEqual(115, iron.Boosts.Bolt(100, SiegeKind.Creeper, out _));

            var flow = new ShuffleBuild(1u);
            flow.Take(ShuffleCards.Find("wide_tubes"));              // +25% capacity
            flow.Take(ShuffleCards.Find("overflow"));
            Assert.AreEqual(112, flow.Boosts.EffectiveFirePercent);

            // A synergy follows a card taken after it, and is capped.
            var big = new SiegeBoosts(1u);
            big.AddKinetic(1000);
            big.AddFire(1000);
            Assert.AreEqual(SiegeBoosts.MostSynergy, big.KineticBonus);
        }

        // ------------------------------------------------------------------ the hand
        [Test]
        public void AGrandHandDealsFiveCardsLaidOutThreeOverTwo()
        {
            var run = new ShuffleRun(11u);
            Assert.AreEqual(ShuffleDeck.HandSize, run.Build.HandSize);

            var grand = ShuffleCards.Find("grand_hand");
            while (run.Build.Takeable(grand)) run.Build.Take(grand);
            Assert.AreEqual(5, run.Build.HandSize);
            Assert.LessOrEqual(run.Build.HandSize, ShuffleDeck.MostHand);

            var hand = run.Deal();
            Assert.AreEqual(5, hand.Count);
            for (int i = 0; i < hand.Count; i++)
            {
                Assert.IsTrue(run.Build.Takeable(hand[i]));
                for (int j = i + 1; j < hand.Count; j++) Assert.AreNotSame(hand[i], hand[j]);
            }

            var L = Layout.ShuffleHandLayout.Default;
            Assert.IsTrue(L.IsClear(ShuffleDeck.MostHand, out string fault), fault);
            Assert.AreEqual(2, L.Rows(5));

            // No two cards overlap, the bottom row sits under the top one, and every card is
            // inside the panel.
            var boxes = new List<(float x, float top)>();
            for (int i = 0; i < 5; i++)
            {
                L.Place(i, 5, out float x, out float top);
                Assert.LessOrEqual(Math.Abs(x) + L.CardWidth * .5f, L.PanelWidth * .5f - L.Inset, $"card {i} leaves the panel");
                Assert.LessOrEqual(top + L.CardHeight, L.PanelHeightFor(5) - L.Foot + 1e-3f, $"card {i} runs off the foot");
                boxes.Add((x, top));
            }

            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    bool apart = Math.Abs(boxes[i].x - boxes[j].x) >= L.CardWidth
                              || Math.Abs(boxes[i].top - boxes[j].top) >= L.CardHeight;
                    Assert.IsTrue(apart, $"cards {i} and {j} overlap");
                }

            Assert.AreEqual(0f, (boxes[3].x + boxes[4].x) * .5f, 1e-3f, "the bottom row is not centred");

            // A hand of three still lays out exactly as it always did.
            for (int i = 0; i < 3; i++)
            {
                L.Place(i, 3, out float x, out float top);
                Assert.AreEqual(L.CardsTop, top);
                Assert.AreEqual((i - 1) * (L.CardWidth + L.CardGap), x, 1e-3f);
            }
            Assert.AreEqual(L.PanelHeight, L.PanelHeightFor(3));
        }

        // ------------------------------------------------------------------ the laser
        [Test]
        public void EachLaserCardMakesOneMoreTurretALaserGunStrongestFirst()
        {
            var board = SiegeBoard.Build(Field(Four), Mixed());
            var build = new ShuffleBuild(1u);
            board.Boosts = build.Boosts;

            for (int w = 0; w < board.Wards.Count; w++) Assert.IsFalse(board.Lasered(w));

            var laser = ShuffleCards.Find("laser_core");
            var order = new List<int>();

            for (int n = 1; n <= laser.Most; n++)
            {
                Assert.IsTrue(build.Take(laser));

                int lasered = 0;
                for (int w = 0; w < board.Wards.Count; w++)
                {
                    if (!board.Lasered(w)) continue;
                    lasered++;
                    if (!order.Contains(w)) order.Add(w);
                }

                Assert.AreEqual(n, lasered, $"{n} copies made {lasered} laser guns");
            }

            // The first is the line's strongest turret.
            int strongest = 0;
            for (int w = 1; w < board.Wards.Count; w++)
                if (Strength(board.Wards[w]) > Strength(board.Wards[strongest])) strongest = w;

            Assert.AreEqual(strongest, order[0], "the first laser gun was not the strongest turret");
            Assert.IsFalse(SiegeBoard.Build(Field(Four)).Lasered(0), "the plain line made a laser gun");
        }

        [Test]
        public void ALaserBouncesThroughRaidersAndNoOtherTurretFiresOne()
        {
            var run = new ShuffleRun(5u);
            run.Build.Take(ShuffleCards.Find("laser_core"));

            var board = SiegeBoard.Build(Lane(), Breakers());
            board.Boosts = run.Build.Boosts;
            board.Refit();

            int beams = 0;
            Play(board, 90f, report =>
            {
                foreach (var bolt in report.Bolts)
                {
                    if (bolt.Via != SiegeVia.Laser) continue;
                    beams++;
                    Assert.IsTrue(board.Lasered(bolt.Ward), $"ward {bolt.Ward} fired a laser and is not a laser gun");
                }
            });

            Assert.Greater(beams, 0, "a laser gun's beam never went on into another raider");
        }

        // ------------------------------------------------------------------ the helpers
        static int Strength(SiegeWard ward) => SiegeTuning.DamageTo(SiegeKind.Creeper, ward.Rank, true, ward.Build);

        /// <summary>A line of four different turrets, so "the strongest" has an answer.</summary>
        static WardLine Mixed()
            => WardLine.Resolve(WardCatalog.Default,
                                new[] { new WardSlot('g', "howitzer"), new WardSlot('b', "eclipse"), new WardSlot('y', "pyre") },
                                (model, colour) => true);

        static readonly string[] Shipped =
        {
            "gbryrrgb",
            "ggyrbgry",
            "rybbgbyy",
            "brgbyygg",
            "bryrbrgr",
        };

        static SiegeLayout Lane()
        {
            Assert.IsTrue(ProtoGrid.TryRead(Shipped, 8, 5, SiegeLayout.Cells, out var grid, out string error), error);
            var layout = new SiegeLayout(grid, "rgby", "rgby", new string[0], "", 0,
                                         new ShuffleRamp("rgby", 24, .5f), 0, "plsfha", true, true);
            Assert.IsNull(layout.Fault, layout.Fault);
            return layout;
        }

        static WardLine Breakers()
            => WardLine.Uniform(WardCatalog.Default, WardCatalog.Default.Find(WardModel.Elemental), WardStars.Least);

        static void Play(SiegeBoard board, float seconds, Action<SiegeReport> look)
        {
            const float Frame = 1f / 60f;
            float since = 0f;

            for (int i = 0; i < seconds * 60f && !board.IsFinished; i++)
            {
                look(board.Advance(Frame));

                since += Frame;
                if (since < 1.2f) continue;

                var swap = board.FindSwap(i);
                if (!swap.Found) continue;

                board.Swap(swap.A, swap.B);
                since = 0f;
            }
        }
    }
}
