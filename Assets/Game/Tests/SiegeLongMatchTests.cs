using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using GlimmerGrove.Shuffle;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The ladder's and the Infinite lane's long matches (MODES.md 37fk, <see cref="SiegeLongMatch"/>):
    /// a run of four merges into a gem bomb that pays to merge and blows up the square round it when
    /// tapped, and a run of five fires a salvo at the hill. Each rule held to its figure, and to
    /// doing nothing on every board that does not forge - the Shuffle lane above all.
    /// </summary>
    [TestFixture]
    public sealed class SiegeLongMatchTests
    {
        // ------------------------------------------------------------------ the fields
        /// <summary>
        /// Settled, and one swap - cell 10 down into cell 18 - makes row two a run of four reds and
        /// nothing else. The same field <c>ShuffleFieldTests</c> forges its power gems on.
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

        /// <summary>The red ward: wards are dealt "rgby", so red is post nought.</summary>
        const int Red = 0;

        static SiegeLayout Field(string[] rows, SiegeRamp ramp = null)
        {
            Assert.IsTrue(ProtoGrid.TryRead(rows, 8, 5, SiegeLayout.Cells, out var grid, out string error), error);

            // No charms dealt, so a refill can only deal plain gems and what a case reads off the
            // field after a swap is what the swap did. Six raiders of every colour but red in the
            // first wave, so a salvo has distinct bodies to aim at.
            var layout = ramp == null
                ? new SiegeLayout(grid, "rgby", "rgby", new[] { "gbygby", "gbygbygb" }, null)
                : new SiegeLayout(grid, "rgby", "rgby", new string[0], "", 0, ramp);

            Assert.IsNull(layout.Fault, layout.Fault);
            return layout;
        }

        static SiegeBoard Board(string[] rows, bool longMatches = true)
        {
            var board = SiegeBoard.Build(Field(rows));
            board.LongMatches = longMatches;
            return board;
        }

        static List<SiegeCharm> Sprung(SiegeTurn turn)
        {
            var all = new List<SiegeCharm>();
            foreach (var beat in turn.Beats)
                foreach (var spark in beat.Sprung)
                    all.Add(spark.Charm);

            return all;
        }

        static string Snapshot(SiegeBoard board)
        {
            var cells = new char[board.Count];
            for (int i = 0; i < board.Count; i++) cells[i] = board.At(i);
            return new string(cells);
        }

        /// <summary>Walks the hill until something is standing on it, with nothing matched.</summary>
        static void Populate(SiegeBoard board)
        {
            for (int i = 0; i < 60 * 8 && board.OnTheHill < 3; i++) board.Advance(1f / 60f);
            Assert.GreaterOrEqual(board.OnTheHill, 3, "too few walked on, so this case proves nothing");
        }

        // ------------------------------------------------------------------ the bomb
        [Test]
        public void AFourMergesIntoABombWhereThePlayerMoved()
        {
            var board = Board(Four);
            var turn = board.Swap(Above, Below);
            Assert.IsNotNull(turn, "the swap lined nothing up");

            var first = turn.Beats[0];
            Assert.AreEqual(1, first.Forged.Count, "a run of four merged into nothing");

            var forged = first.Forged[0];
            Assert.AreEqual(SiegeCharm.Bomb, forged.Charm, "a run of four forged the wrong gem");
            Assert.AreEqual(Below, forged.Cell, "the bomb did not stand where the player moved");
            Assert.AreEqual(SiegeLayout.Letters.IndexOf('r'), forged.Colour);
            Assert.AreEqual(3, forged.Run.Length);

            CollectionAssert.DoesNotContain(first.Cleared, Below, "the merge cleared its own cell");
            foreach (int cell in forged.Run) CollectionAssert.Contains(first.Cleared, cell);
            Assert.AreEqual(0, first.Rockets.Count, "a four fired a salvo");

            bool standing = false;
            for (int i = 0; i < board.Count; i++) if (board.CharmAt(i) == SiegeCharm.Bomb) standing = true;
            Assert.IsTrue(standing || Sprung(turn).Contains(SiegeCharm.Bomb),
                          "the bomb was merged and then neither stood nor went off");
        }

        /// <summary>
        /// <b>Merging is never free</b> (the owner's rule): the three gems that clear pay as any match
        /// does, and the merge pays <see cref="SiegeLongMatch.MergeGems"/> more on top - so the four
        /// that forges pays more of its colour than the same four on a board that forges nothing.
        /// </summary>
        [Test]
        public void MergingPaysTheRunsColourOnTopOfTheMatch()
        {
            var plain = Board(Four, longMatches: false).Swap(Above, Below).Beats[0];
            var merged = Board(Four).Swap(Above, Below).Beats[0];

            float gem = SiegeTuning.FuelPerGem;

            Assert.AreEqual(4 * gem, plain.Fuel[Red], 1e-4f, "a plain four did not pay four gems");
            Assert.AreEqual((3 + SiegeLongMatch.MergeGems) * gem, merged.Fuel[Red], 1e-4f,
                            "a merged four did not pay its three gems and the merge");
            Assert.Greater(merged.Fuel[Red], plain.Fuel[Red], "merging cost the turret fuel");
        }

        /// <summary>
        /// A tapped bomb takes the square round it, and every gem that goes pays its own colour's
        /// turret - the owner's rule that whatever is exploded feeds the turret it belongs to.
        /// </summary>
        [Test]
        public void ATappedBombTakesTheSquareRoundItAndEveryColourPaysItsOwnTurret()
        {
            var board = Board(Four);
            const int bomb = 19;           // row two, column three: a red with a full square round it
            board.Stand(bomb, SiegeCharm.Bomb);

            Assert.IsTrue(board.Detonates(bomb));
            var turn = board.Detonate(bomb);
            Assert.IsNotNull(turn, "the bomb did not go off");
            Assert.IsTrue(turn.Tapped);
            Assert.AreEqual(bomb, turn.A);
            Assert.AreEqual(bomb, turn.B);

            var first = turn.Beats[0];
            CollectionAssert.Contains(Sprung(turn), SiegeCharm.Bomb);

            int[] square = { 10, 11, 12, 18, 19, 20, 26, 27, 28 };
            foreach (int cell in square) CollectionAssert.Contains(first.Cleared, cell, $"the blast missed cell {cell}");
            Assert.AreEqual(square.Length, first.Cleared.Count, "the blast reached past its square");

            // Rows one to three, columns two to four: r b y / g r g / b y b.
            float gem = SiegeTuning.FuelPerGem;
            Assert.AreEqual(2 * gem, first.Fuel[0], 1e-4f, "red was not paid its two gems");
            Assert.AreEqual(2 * gem, first.Fuel[1], 1e-4f, "green was not paid its two gems");
            Assert.AreEqual(3 * gem, first.Fuel[2], 1e-4f, "blue was not paid its three gems");
            Assert.AreEqual(2 * gem, first.Fuel[3], 1e-4f, "yellow was not paid its two gems");

            Assert.AreEqual(1, Sprung(turn).FindAll(c => c == SiegeCharm.Bomb).Count, "the bomb went off more than once");
            Assert.AreEqual(0, board.Attention.CharmsSprung, "a forged bomb was counted as a dealt charm spent");
        }

        [Test]
        public void TappingAnythingButABombDoesNothing()
        {
            var board = Board(Four);
            string before = Snapshot(board);

            for (int cell = 0; cell < board.Count; cell++)
            {
                Assert.IsFalse(board.Detonates(cell), $"cell {cell} reads as a bomb");
                Assert.IsNull(board.Detonate(cell), $"tapping cell {cell} did something");
            }

            Assert.AreEqual(before, Snapshot(board), "a refused tap moved the field");
            Assert.AreEqual(0, board.Flying.Count, "a refused tap paid fuel");
        }

        /// <summary>A bomb a match reaches goes off as any charm does - the tap is not the only door.</summary>
        [Test]
        public void ABombAMatchTakesGoesOff()
        {
            var board = Board(Four, longMatches: false);
            board.Stand(16, SiegeCharm.Bomb);    // the run's first red

            var turn = board.Swap(Above, Below);
            Assert.IsNotNull(turn);
            CollectionAssert.Contains(Sprung(turn), SiegeCharm.Bomb);

            // Column nought of row two: the square is clipped by the field's edge.
            foreach (int cell in new[] { 8, 9, 17, 24, 25 })
                CollectionAssert.Contains(turn.Beats[0].Cleared, cell, $"the blast missed cell {cell}");
        }

        // ------------------------------------------------------------------ the salvo
        [Test]
        public void AFiveFiresASalvoAndForgesNothing()
        {
            var board = Board(Five);
            var turn = board.Swap(Above, Below);
            Assert.IsNotNull(turn);

            var first = turn.Beats[0];
            Assert.AreEqual(0, first.Forged.Count, "a five merged into a gem");
            Assert.AreEqual(SiegeLongMatch.RocketsFor(5), first.Rockets.Count, "a five fired the wrong salvo");
            for (int x = 0; x < 5; x++) CollectionAssert.Contains(first.Cleared, 16 + x, "a five kept a gem back");

            // From both ends of the run and its middle.
            CollectionAssert.AreEquivalent(new[] { 16, 18, 20 },
                                           new[] { first.Rockets[0].Cell, first.Rockets[1].Cell, first.Rockets[2].Cell });

            // **All five pay**, as any five does: the salvo is on top of the match.
            Assert.AreEqual(5 * SiegeTuning.FuelPerGem, first.Fuel[Red], 1e-4f);
        }

        /// <summary>
        /// The salvo lands no sooner than its flight, on the bodies nearest the line, one each, and
        /// takes <see cref="SiegeLongMatch.RocketDamageAt"/> off every one of them.
        /// </summary>
        [Test]
        public void TheSalvoLandsAfterItsFlightOnTheBodiesNearestTheLine()
        {
            var board = Board(Five);
            Populate(board);

            var nearest = new List<SiegeRaider>();
            foreach (var r in board.Raiders) if (r.Alive && r.OnTheHill) nearest.Add(r);
            nearest.Sort((a, b) => b.March.CompareTo(a.March) != 0 ? b.March.CompareTo(a.March) : a.Id.CompareTo(b.Id));

            var turn = board.Swap(Above, Below);
            Assert.IsNotNull(turn);

            var rockets = turn.Beats[0].Rockets;
            Assert.AreEqual(3, rockets.Count);
            for (int i = 0; i < 3; i++)
                Assert.AreEqual(nearest[i].Id, rockets[i].Raider, $"rocket {i} was not aimed at the {i + 1}th nearest body");

            var hits = new List<SiegeRocketHit>();
            float elapsed = 0f;
            float first = -1f;

            for (int frame = 0; frame < 60 * 3 && hits.Count < 3; frame++)
            {
                var health = new Dictionary<int, int>();
                var bodies = new Dictionary<int, SiegeRaider>();
                foreach (var r in board.Raiders) { health[r.Id] = r.Health; bodies[r.Id] = r; }

                const float dt = 1f / 60f;
                var report = board.Advance(dt);
                elapsed += dt;

                foreach (var hit in report.Rocketed)
                {
                    if (first < 0f) first = elapsed;
                    hits.Add(hit);

                    // Salvoes land before anything on the line fires in the same step, so the
                    // health before the step is the health the rocket met.
                    Assert.IsTrue(bodies.TryGetValue(hit.Raider, out var target), $"rocket {hit.Id} struck nobody on the board");
                    int expected = SiegeLongMatch.RocketDamageAt(target.Surge.HealthTenths);
                    Assert.AreEqual(System.Math.Min(expected, health[hit.Raider]), hit.Damage,
                                    $"rocket {hit.Id} took the wrong amount");
                }
            }

            Assert.AreEqual(3, hits.Count, "the salvo did not land");
            Assert.GreaterOrEqual(first, SiegeLongMatch.RocketLands(1) - 1f / 60f, "the salvo landed before its flight");
            Assert.AreEqual(0, board.RocketsInFlight);
        }

        /// <summary>A salvo fired at an empty hill still lands, on the ground, and says so.</summary>
        [Test]
        public void ASalvoAtAnEmptyHillBurstsOnTheGround()
        {
            var board = Board(Five);
            Assert.AreEqual(0, board.OnTheHill);

            var turn = board.Swap(Above, Below);
            foreach (var rocket in turn.Beats[0].Rockets) Assert.AreEqual(-1, rocket.Raider);

            var hits = new List<SiegeRocketHit>();
            for (int frame = 0; frame < 60 && hits.Count < 3; frame++)
                hits.AddRange(board.Advance(1f / 60f).Rocketed);

            Assert.AreEqual(3, hits.Count);
            foreach (var hit in hits)
            {
                Assert.AreEqual(-1, hit.Raider);
                Assert.AreEqual(0, hit.Damage);
            }
        }

        // ------------------------------------------------------------------ the figures
        /// <summary>
        /// A rocket scales at 130% of the hill's health bonus (the owner's figure): the baseline at
        /// ten tenths, and +130% of the bonus above it.
        /// </summary>
        [Test]
        public void ARocketScalesWithTheHillsHealthBonus()
        {
            Assert.AreEqual(SiegeLongMatch.RocketDamage, SiegeLongMatch.RocketDamageAt(10));
            Assert.AreEqual(SiegeLongMatch.RocketDamage * 230 / 100, SiegeLongMatch.RocketDamageAt(20));
            Assert.AreEqual(SiegeLongMatch.RocketDamage * 165 / 100, SiegeLongMatch.RocketDamageAt(15));
            Assert.AreEqual(SiegeLongMatch.RocketDamage, SiegeLongMatch.RocketDamageAt(0), "a missing surge read as weaker than the baseline");

            for (int tenths = 10; tenths < 200; tenths++)
                Assert.LessOrEqual(SiegeLongMatch.RocketDamageAt(tenths), SiegeLongMatch.RocketDamageAt(tenths + 1));
        }

        [Test]
        public void ASalvoIsThreeRocketsForFiveAndCapped()
        {
            Assert.AreEqual(0, SiegeLongMatch.RocketsFor(4));
            Assert.AreEqual(3, SiegeLongMatch.RocketsFor(5));
            Assert.AreEqual(4, SiegeLongMatch.RocketsFor(6));
            Assert.AreEqual(SiegeLongMatch.MostRockets, SiegeLongMatch.RocketsFor(8));
        }

        // ------------------------------------------------------------------ where it is off
        /// <summary>
        /// <b>A board nobody switched on forges nothing</b> - every gate, mirror and fixture builds
        /// one, so the mode they measure is the mode that shipped before this.
        /// </summary>
        [Test]
        public void ABoardBuiltDirectlyForgesNothing()
        {
            var four = Board(Four, longMatches: false).Swap(Above, Below);
            var five = Board(Five, longMatches: false).Swap(Above, Below);

            foreach (var beat in four.Beats)
            {
                Assert.AreEqual(0, beat.Forged.Count, "an unswitched board merged a bomb");
                Assert.AreEqual(0, beat.Rockets.Count);
            }

            foreach (var beat in five.Beats)
            {
                Assert.AreEqual(0, beat.Forged.Count);
                Assert.AreEqual(0, beat.Rockets.Count, "an unswitched board fired a salvo");
            }
        }

        /// <summary>
        /// <b>The Shuffle lane is untouched, by two locks.</b> Its rules never switch a board on, and
        /// a board holding a build forges nothing of this file's even if somebody did: an empty build
        /// forges nothing at all, and <c>power_gems</c> still forges its own rocket.
        /// </summary>
        [Test]
        public void TheShuffleLaneNeverForgesABombOrFiresASalvo()
        {
            var empty = Board(Five);
            empty.Boosts = new ShuffleBuild(1u).Boosts;
            foreach (var beat in empty.Swap(Above, Below).Beats)
            {
                Assert.AreEqual(0, beat.Forged.Count, "a Shuffle board merged a bomb");
                Assert.AreEqual(0, beat.Rockets.Count, "a Shuffle board fired a salvo");
            }

            var carded = Board(Four);
            var build = new ShuffleBuild(1u);
            Assert.IsTrue(build.Take(ShuffleCards.Find("power_gems")));
            carded.Boosts = build.Boosts;
            carded.Refit();

            var forged = carded.Swap(Above, Below).Beats[0].Forged;
            Assert.AreEqual(1, forged.Count);
            Assert.AreEqual(SiegeCharm.RocketRow, forged[0].Charm, "power_gems no longer forges its own rocket");
        }

        [Test]
        public void TheLadderTheInfiniteLaneAndTheTutorialForgeAndTheShuffleLaneDoesNot()
        {
            var ladder = new SiegeRules(Field(Four));
            var infinite = new SiegeRules(Field(Four, new SiegeEndless("rgby", 4, 20, .55f)));
            var shuffle = new SiegeRules(Field(Four, new ShuffleRamp("rgby", 24, .5f)));

            Assert.IsTrue(ladder.LongMatches, "the ladder does not forge");
            Assert.IsTrue(infinite.LongMatches, "the Infinite lane does not forge");
            Assert.IsFalse(shuffle.LongMatches, "the Shuffle lane forges");
            // The tutorial forges: it is where the gem bomb is taught (2026-10-26).
            Assert.IsTrue(SiegeTutorial.Rules().LongMatches, "the tutorial cannot teach the gem bomb");

            Assert.IsTrue(((SiegeBoard)ladder.Fresh()).LongMatches, "a ladder board was dealt switched off");
            Assert.IsTrue(((SiegeBoard)infinite.Fresh()).LongMatches);
            Assert.IsFalse(((SiegeBoard)shuffle.Fresh()).LongMatches);

            // A checkpoint and the helping hand deal the board the run began on.
            Assert.IsTrue(infinite.From(new SiegeStart(5, 1)).LongMatches);
            Assert.IsTrue(ladder.Eased(20).LongMatches);
        }
    }
}
