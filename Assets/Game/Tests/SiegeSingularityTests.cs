using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The singularity (<see cref="SiegeLayout.Singularity"/>): the rare void stone, the field it
    /// swallows and the beam it fires.
    ///
    /// <para>
    /// The clauses pinned hardest are the ones a player or a neighbouring chapter would pay for
    /// if they slipped: a field that does not deal the stone deals exactly what it always did
    /// (invariant 41); a collapse takes the <em>whole</em> field and pays and springs nothing;
    /// the beam lands after the field has gone in and never before; and nothing about it lets a
    /// boss skip a stand or reaches a body that was not on the hill.
    /// </para>
    /// </summary>
    public sealed class SiegeSingularityTests
    {
        const string Gems = "rgby";
        const string Wards = "rgby";
        static readonly string[] Waves = { "rgbyrgbyrgby", "rgbyrgbyrgby" };

        /// <summary>A settled field with no three alike anywhere (<c>SiegeObsidianTests.Field</c>).</summary>
        static readonly string[] Field =
        {
            "rgbyrgby",
            "gbyrgbyr",
            "byrgbyrg",
            "yrgbyrgb",
            "rgbyrgby",
        };

        const char Stone = SiegeLayout.Singularity;

        static SiegeLayout Layout(bool singular, bool cursed = false, string charms = null,
                                  string boss = null, string[] waves = null)
        {
            Assert.IsTrue(ProtoGrid.TryRead(Field, Field[0].Length, Field.Length,
                                            SiegeLayout.Cells, out var grid, out string error),
                          error);

            var layout = new SiegeLayout(grid, Gems, Wards, waves ?? Waves, boss, 0, null, 0,
                                         charms, cursed, singular);
            Assert.IsNull(layout.Fault, layout.Fault);
            return layout;
        }

        static SiegeBoard Board(bool singular = true, bool cursed = false)
            => SiegeBoard.Build(Layout(singular, cursed));

        /// <summary>Lays three stones so that swapping cells 2 and 3 lines them up, and swaps.</summary>
        static SiegeTurn Collapse(SiegeBoard board)
        {
            board.Lay(0, Stone);
            board.Lay(1, Stone);
            board.Lay(3, Stone);

            var turn = board.Swap(2, 3);
            Assert.IsNotNull(turn, "three void stones in a line is not a legal swap");
            return turn;
        }

        static void Walk(SiegeBoard board, int most)
        {
            for (int i = 0; i < 60 * 10 && board.OnTheHill < most; i++) board.Advance(1f / 60f);
            Assert.GreaterOrEqual(board.OnTheHill, most, "the first wave never walked on");
        }

        // ----------------------------------------------------------------- the deal
        [Test]
        public void DealingAVoidStoneCostsNoExtraDraw()
        {
            var plain = Board(false);
            var singular = Board(true);

            int stones = 0;
            for (int i = 0; i < 20000; i++)
            {
                char a = plain.Deal(9, out _);
                char b = singular.Deal(9, out _);

                if (b == Stone) { stones++; continue; }
                Assert.AreEqual(a, b, $"draw {i}: the field dealt a different gem, so it drew "
                                      + "from the stream a different number of times");
            }

            Assert.Greater(stones, 0, "a singular field never dealt a void stone");
        }

        [Test]
        public void AFieldThatDoesNotDealItNeverDoes()
        {
            var plain = Board(false);
            var cursed = Board(false, cursed: true);

            for (int i = 0; i < 6000; i++)
            {
                Assert.AreNotEqual(Stone, plain.Deal(i % 40, out _));
                Assert.AreNotEqual(Stone, cursed.Deal(i % 40, out _),
                                   "a cursed field that is not singular dealt a void stone");
            }
        }

        /// <summary>
        /// Rare: about <see cref="SiegeTuning.SingularityPermille"/> in a thousand, and rarer
        /// than the cursed stone it shares a chapter with.
        /// </summary>
        [Test]
        public void TheVoidStoneIsRare()
        {
            var board = Board(true);
            int stones = 0, draws = 40000;

            for (int i = 0; i < draws; i++)
                if (board.Deal(i % 40, out _) == Stone) stones++;

            float permille = stones * 1000f / draws;

            Assert.That(permille, Is.InRange(SiegeTuning.SingularityPermille * .6f,
                                             SiegeTuning.SingularityPermille * 1.15f),
                        $"void stones were {permille:F1} per thousand");
            Assert.Less(SiegeTuning.SingularityPermille, SiegeTuning.ObsidianPercent * 10,
                        "the void stone is meant to be the rarer of the two");
        }

        [Test]
        public void ADealtVoidStoneNeverLandsLined()
        {
            var board = Board(true);
            board.Lay(0, Stone);
            board.Lay(1, Stone);

            for (int i = 0; i < 20000; i++)
                Assert.AreNotEqual(Stone, board.Deal(2, out _),
                                   "a void stone was dealt into a cell where it made three");
        }

        [Test]
        public void AVoidStoneNeverCarriesACharm()
        {
            var board = SiegeBoard.Build(Layout(true, charms: SiegeCharms.Letters));

            for (int i = 0; i < 20000; i++)
            {
                char gem = board.Deal(i % 40, out var charm);
                if (gem == Stone) Assert.AreEqual(SiegeCharm.None, charm);
            }
        }

        // ----------------------------------------------------------------- the run
        [Test]
        public void TheTwoStonesLineUpWithTheirOwnKindAndNeverWithEachOther()
        {
            var board = Board(true, cursed: true);

            // Two void stones and an obsidian in a row are no run of anything.
            board.Lay(0, Stone);
            board.Lay(1, Stone);
            board.Lay(3, SiegeLayout.Obsidian);

            Assert.IsNull(board.Swap(2, 3), "a void stone lined up with an obsidian");
        }

        [Test]
        public void OneCellReadsTheSameAsTheWholeFieldWithBothStonesOnIt()
        {
            var board = Board(true, cursed: true);
            var rng = new System.Random(7);

            for (int round = 0; round < 200; round++)
            {
                var cells = new char[board.Count];
                for (int i = 0; i < cells.Length; i++)
                {
                    int roll = rng.Next(10);
                    cells[i] = roll == 0 ? Stone
                             : roll == 1 ? SiegeLayout.Obsidian
                             : Gems[rng.Next(Gems.Length)];
                }

                var hit = SiegeLayout.Runs(cells, board.Width, board.Height, null, null);

                for (int i = 0; i < cells.Length; i++)
                    Assert.AreEqual(hit.Contains(i),
                                    SiegeLayout.Lined(cells, board.Width, board.Height, null, i),
                                    $"round {round}, cell {i} ('{cells[i]}')");
            }
        }

        // ----------------------------------------------------------------- the collapse
        [Test]
        public void ACollapseTakesTheWholeFieldAndPaysNothing()
        {
            var board = Board(true);
            var beat = Collapse(board).Beats[0];

            Assert.IsTrue(beat.Swallowed, "the singularity did not collapse");
            Assert.AreEqual(board.Count, beat.Cleared.Count, "the collapse left gems standing");
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, beat.Eaters);

            for (int w = 0; w < beat.Fuel.Length; w++)
                Assert.AreEqual(0f, beat.Fuel[w], $"a swallowed gem paid ward {w}");

            Assert.AreEqual(0, board.Flying.Count, "fuel was booked for a field that was eaten");
        }

        [Test]
        public void TheFieldIsDealtWholeAgainAndHasAMove()
        {
            var board = Board(true);
            Collapse(board);

            for (int i = 0; i < board.Count; i++)
                Assert.AreNotEqual(SiegeBoard.Hole, board.At(i), $"cell {i} was left empty");

            Assert.IsTrue(board.FindSwap(0).Found,
                          "the fresh field has nothing to swap");
        }

        /// <summary>
        /// Nothing the hole swallows goes off inside it: a charm on the field is lost, not
        /// sprung, which is the stone's price.
        /// </summary>
        [Test]
        public void ASwallowedCharmIsNotSprung()
        {
            var board = Board(true);

            board.Stand(20, SiegeCharm.Storm);
            board.Stand(27, SiegeCharm.Hourglass);
            board.Stand(33, SiegeCharm.Anvil);

            var turn = Collapse(board);

            Assert.AreEqual(0, turn.Beats[0].Sprung.Count, "a swallowed charm went off");
            Assert.IsFalse(board.Stilled, "a swallowed hourglass stopped the hill");
        }

        [Test]
        public void SwallowedObsidiansBreakNoCurse()
        {
            var board = Board(true, cursed: true);

            board.Lay(21, SiegeLayout.Obsidian);
            board.Lay(38, SiegeLayout.Obsidian);

            var beat = Collapse(board).Beats[0];

            Assert.IsTrue(beat.Swallowed);
            Assert.IsFalse(beat.Unbound, "a curse broke that nobody lined up");
        }

        [Test]
        public void ARunOfGemsBesideAVoidStoneCollapsesNothing()
        {
            var board = Board(true);
            board.Lay(8, Stone);
            board.Lay(1, 'r');
            board.Lay(3, 'r');

            var turn = board.Swap(2, 3);
            Assert.IsNotNull(turn);
            Assert.IsFalse(turn.Beats[0].Swallowed, "a run of gems collapsed the field");
            Assert.IsFalse(board.Beaming);
        }

        [Test]
        public void AFieldThatIsNotSingularNeverCollapses()
        {
            var board = Board(false);
            var beat = Collapse(board).Beats[0];

            // The stones still line up - they are gems - but nothing follows from it.
            Assert.IsFalse(beat.Swallowed);
            Assert.IsFalse(board.Beaming);
        }

        // ----------------------------------------------------------------- the beam
        /// <summary>
        /// The beam fires after the field has gone in and not before (invariant 37s), and it
        /// strikes every body standing on the hill at that moment.
        /// </summary>
        [Test]
        public void TheBeamFiresWhenItIsBookedAndStrikesEveryoneStanding()
        {
            var board = Board(true);
            Walk(board, 3);
            Collapse(board);

            Assert.IsTrue(board.Beaming);

            float lands = SiegeTuning.BeamLands(0);
            float t = 0f;
            bool fired = false;
            var struck = new HashSet<int>();
            var standing = new HashSet<int>();

            while (t < lands + .5f && !fired)
            {
                standing.Clear();
                foreach (var raider in board.Raiders)
                    if (raider.Alive && raider.OnTheHill) standing.Add(raider.Id);

                var report = board.Advance(1f / 60f);
                t += 1f / 60f;

                if (!report.Beamed) continue;

                fired = true;
                Assert.GreaterOrEqual(t, lands - 1f / 30f, "the beam fired before the field was in");

                foreach (var hit in report.Beam)
                {
                    Assert.Greater(hit.Damage, 0);
                    Assert.IsTrue(struck.Add(hit.Raider), "a body was struck twice by one beam");
                }
            }

            Assert.IsTrue(fired, "the beam never fired");
            Assert.IsFalse(board.Beaming);
            CollectionAssert.AreEquivalent(standing, struck,
                                           "the beam struck other than exactly who was standing");
        }

        /// <summary>
        /// What it is worth is the line's: every standing ward lands
        /// <see cref="SiegeTuning.VoidBolts"/> of its bolts, so on a starter line at rank nought
        /// that is exactly that many full bolts a ward - and nothing from a ward that has fallen.
        /// </summary>
        [Test]
        public void TheBeamIsWorthTheStandingLineAndNoMore()
        {
            int Struck(int fallen)
            {
                var board = Board(true);
                Walk(board, 1);

                // A brute's worth of health, so nothing dies and the figure is the whole blow.
                SiegeRaider body = null;
                foreach (var raider in board.Raiders)
                    if (raider.Alive && raider.OnTheHill) { body = raider; break; }

                Assert.IsNotNull(body);
                body.Health = 100000;

                for (int w = 0; w < fallen; w++) board.Wards[w].Alive = false;

                Collapse(board);

                for (float t = 0f; t < SiegeTuning.BeamLands(0) + .2f; t += 1f / 60f)
                {
                    var report = board.Advance(1f / 60f);
                    if (!report.Beamed) continue;

                    foreach (var hit in report.Beam)
                        if (hit.Raider == body.Id) return hit.Damage;

                    return 0;
                }

                Assert.Fail("the beam never fired");
                return 0;
            }

            int whole = Struck(0);
            int bolt = SiegeTuning.DamageTo(SiegeKind.Creeper, 0, true);

            Assert.AreEqual(4 * SiegeTuning.VoidBolts * bolt, whole,
                            "four starter wards did not land their bolts");
            Assert.AreEqual(whole / 2, Struck(2), "a fallen ward still fired");
            Assert.AreEqual(0, Struck(4), "a line with nothing standing fired a beam");
        }

        [Test]
        public void ABeamOverAnEmptyHillIsReportedAndHurtsNothing()
        {
            var board = Board(true);
            Assert.AreEqual(0, board.OnTheHill);

            Collapse(board);

            // Inside the opening quiet, so the hill is still empty when it fires.
            Assert.Less(SiegeTuning.BeamLands(0), SiegeTuning.FirstWaveAfter,
                        "the fixture needs the beam to fire before the first wave");

            bool fired = false;
            for (float t = 0f; t < SiegeTuning.BeamLands(0) + .2f && !fired; t += 1f / 60f)
            {
                var report = board.Advance(1f / 60f);
                if (!report.Beamed) continue;

                fired = true;
                Assert.AreEqual(0, report.Beam.Count);
            }

            Assert.IsTrue(fired, "a beam over an empty hill was not reported, so nothing draws it");
        }

        /// <summary>
        /// A boss is hurt and is never taken under its stand's floor - the beam goes through the
        /// one door every other blow does (invariant 37dj).
        /// </summary>
        [Test]
        public void TheBeamHurtsABossAndNeverSkipsAStand()
        {
            var board = SiegeBoard.Build(Layout(true, boss: "warlord:r", waves: new[] { "rg" }));

            for (float t = 0f; t < SiegeTuning.FirstWaveAfter + SiegeTuning.BetweenWaves * 4f; t += .1f)
                board.Advance(.1f);
            foreach (var raider in board.Raiders) { raider.Health = 0; raider.Alive = false; }
            for (float t = 0f; t < SiegeTuning.Breather + SiegeTuning.BossMarch + 2f; t += .1f)
                board.Advance(.1f);

            SiegeRaider boss = null;
            foreach (var raider in board.Raiders)
                if (raider.Alive && raider.Boss) boss = raider;

            Assert.IsNotNull(boss, "the fixture has to send a boss");
            Assert.IsTrue(boss.InPlace, "the boss is still walking on");

            // The line the wait may have cost is stood back up: a beam is worth what is standing.
            foreach (var ward in board.Wards) { ward.Alive = true; ward.Health = ward.Full; }

            // Ten points above its stand's floor, so the beam is many times what the stand
            // has left and the floor is the only thing that can hold it.
            int floor = boss.Floor;
            boss.Health = floor + 10;

            int was = boss.Health;

            Collapse(board);
            for (float t = 0f; t < SiegeTuning.BeamLands(0) + .2f; t += 1f / 60f)
                board.Advance(1f / 60f);

            Assert.Less(boss.Health, was, "the beam has to hurt a boss in place");
            Assert.IsTrue(boss.Alive, "the beam felled a boss through its stand");
            Assert.GreaterOrEqual(boss.Health, floor, "the beam took a boss under its stand's floor");
        }

        // ----------------------------------------------------------------- the content
        /// <summary>
        /// The eleventh chapter introduces it and nothing before it deals it - so every board
        /// that shipped earlier deals exactly what it always did. **The Infinite lane deals it
        /// too** (since 2026-10-03), and only from its own wave (<c>EndlessStoneTests</c>).
        /// </summary>
        [Test]
        public void OnlyNeonhavenAndTheInfiniteLaneDealTheVoidStone()
        {
            string chapters = Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets",
                                           "Content", "chapters");

            foreach (string path in Directory.GetFiles(chapters, "s*.json"))
            {
                string text = File.ReadAllText(path);
                string name = Path.GetFileNameWithoutExtension(path);

                int dealt = Regex.Matches(text, "\"singularity\"\\s*:\\s*true").Count;
                int levels = Regex.Matches(text, "\"siege\"\\s*:").Count;

                if (name == "s12_neonhaven" || name == "s02_endlesswatch")
                    Assert.AreEqual(levels, dealt, $"a rung of {name} deals no void stone");
                else
                    Assert.AreEqual(0, dealt, $"{name} deals the void stone");
            }
        }

        [Test]
        public void TheStonesArtIsOnDiskAndScoped()
        {
            string art = Path.Combine(TestJson.RepoRoot(), "Assets", "Game", "Art", "Siege");

            Assert.IsTrue(File.Exists(Path.Combine(art, "gem_void.png")),
                          "no gem_void.png - run python Tools/make_gravity_fx.py --write");
            Assert.IsTrue(File.Exists(Path.Combine(art, "gem_void.png.meta")));

            var names = new HashSet<string>();
            foreach (var request in new SiegeMode().Art) names.Add(request.Address);

            Assert.IsTrue(names.Contains(AssetPipeline.AssetManifest.SiegeArt("gem_void")),
                          "the siege never names gem_void, so it ships addressed and unloadable");
        }

        // ----------------------------------------------------------------- the drawing's clock
        /// <summary>
        /// **The field is under the horizon before the beam leaves it.** The beam is model time
        /// (<see cref="SiegeTuning.VoidGather"/>) and the fall is drawn in real time with the run's
        /// clock slowed to <see cref="SiegeView.SwallowPace"/> for exactly as long as it lasts, so
        /// the model time that passes during the fall has to be less than the gather - or a
        /// longer fall would have the beam fire through gems still falling in. Asked of the
        /// figures rather than of a frame, because no offline fixture can run the view's clock.
        /// </summary>
        [Test]
        public void TheFieldIsSwallowedBeforeTheBeamLeaves()
        {
            float fall = SiegeView.GulpSpread + SiegeView.GulpFor;

            Assert.Greater(SiegeView.SwallowPace, 0f, "the swallow stops the hill dead");
            Assert.Less(SiegeView.SwallowPace, 1f, "the swallow does not slow the clock at all");
            Assert.Less(SiegeView.SwallowPace * fall, SiegeTuning.VoidGather,
                        "the beam leaves before the last gem is under the horizon");

            // And the refill's patience outlasts the whole swallow at full pace afterwards, with
            // room to spare - it is the net under a beam that never comes, never a clock that
            // cuts a real one short.
            Assert.Greater(SiegeView.VoidPatience, fall + SiegeTuning.VoidGather + 1f,
                           "the refill gives up on a beam that is still on its way");
        }
    }
}
