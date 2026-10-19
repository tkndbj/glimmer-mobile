using System.Collections.Generic;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The revive (<c>UtilityKind.Revive</c>) and the wall (<c>UtilityKind.Wall</c>).
    ///
    /// <para>
    /// What is pinned is what each promises a player who paid gems for it - a fallen turret back
    /// whole and untouchable for the time on the card; nothing behind the wall getting past it
    /// while it stands - and what neither may do: cost the grade anything (invariant 39), raise a
    /// line that is already over (23), hold a boss, or change a board nobody used one on.
    /// </para>
    /// </summary>
    public sealed class ReviveAndWallTests
    {
        const int Barrier = 30;   // tenths: the shipped revive's three seconds
        const int Stands = 70;    // tenths: the shipped wall's seven seconds

        static SiegeLayout Layout(string wave, string boss = null)
        {
            var rows = new[] { "rgbyrg", "bygrby", "grbygr", "ybgrby" };

            return new SiegeLayout(
                ProtoGrid.TryRead(rows, 6, 4, SiegeLayout.Letters, out var grid, out _) ? grid : null,
                "rgby", "rgby", new[] { wave }, boss: boss);
        }

        static SiegeBoard Board(string wave = "rrggbbyy", string boss = null)
        {
            var layout = Layout(wave, boss);
            Assert.IsNull(layout.Fault, layout.Fault);
            return SiegeBoard.Build(layout);
        }

        static void Run(SiegeBoard board, float seconds, float step = .05f)
        {
            for (float t = 0f; t < seconds - step * .5f; t += step) board.Advance(step);
        }

        static int Turned(SiegeBoard board, float seconds, float step = .05f)
        {
            int n = 0;
            for (float t = 0f; t < seconds - step * .5f; t += step) n += board.Advance(step).Turned.Count;
            return n;
        }

        static void Fell(SiegeWard ward)
        {
            ward.Health = 0;
            ward.Alive = false;
            ward.Fuel = 0f;
        }

        static UtilityItem Revive() => new UtilityItem("revive", UtilityKind.Revive, Barrier, 80, 100, 6, 0, 60);

        static UtilityItem Wall() => new UtilityItem("wall", UtilityKind.Wall, Stands, 40, 100, 7, 0, 30);

        // ------------------------------------------------------------------ the kinds
        [Test]
        public void BothKindsAreReadAimedAndMeasuredAsTheyShould()
        {
            Assert.AreEqual(UtilityKind.Revive, UtilityKinds.Parse("revive"));
            Assert.AreEqual(UtilityKind.Wall, UtilityKinds.Parse("wall"));
            Assert.AreEqual("revive", UtilityKinds.Id(UtilityKind.Revive));
            Assert.AreEqual("wall", UtilityKinds.Id(UtilityKind.Wall));

            Assert.AreEqual(UtilityTarget.Fallen, UtilityKinds.TargetOf(UtilityKind.Revive));
            Assert.AreEqual(UtilityTarget.Row, UtilityKinds.TargetOf(UtilityKind.Wall));

            Assert.AreEqual(UtilityUnit.Time, UtilityUnits.Of(UtilityKind.Revive));
            Assert.AreEqual(UtilityUnit.Time, UtilityUnits.Of(UtilityKind.Wall));
        }

        // ------------------------------------------------------------------ the revive
        [Test]
        public void AReviveStandsAFallenTurretUpWholeAndEmptyAndChargesNothing()
        {
            var board = Board();
            var ward = board.Wards[1];
            Fell(ward);

            Assert.IsTrue(board.AnyFallen);

            var use = SiegeUtility.Apply(board, Revive(), SiegeAim.AtWard(1), null);

            Assert.IsTrue(use.Landed);
            Assert.AreEqual(0, use.Matches, "a revive delivers no damage, so it saves no matches (39)");
            Assert.IsTrue(ward.Alive);
            Assert.AreEqual(ward.Full, ward.Health);
            Assert.AreEqual(0f, ward.Fuel, "fuel is what the player matched for");
            Assert.AreEqual(Barrier / 10f, ward.Barrier, 1e-5f);
            Assert.IsTrue(ward.Shielded);
            Assert.IsFalse(board.AnyFallen);
        }

        [Test]
        public void AReviveIsRefusedOnAStandingTurretAndOnALineThatHasFallenWhole()
        {
            var board = Board();

            Assert.IsFalse(SiegeUtility.Would(board, Revive(), SiegeAim.AtWard(0)),
                           "a standing turret is the mending's, not the revive's");

            foreach (var ward in board.Wards) Fell(ward);

            Assert.IsFalse(SiegeUtility.Would(board, Revive(), SiegeAim.AtWard(0)),
                           "a run with nothing standing is over; only a continue answers that (23)");
            Assert.IsFalse(board.Restore(0, Barrier));
        }

        /// <summary>
        /// The barrier is the phoenix's mantle on one post: every hit aimed at it is turned away
        /// while it lasts, and it runs out on the board's own clock.
        /// </summary>
        [Test]
        public void TheBarrierTurnsEveryHitAwayUntilItsTimeRunsOut()
        {
            var board = Board("rrggbbyyrrggbbyy");

            // Three posts down and stood back up behind a long barrier, so the hill has no
            // choice but to swing at covered posts; one left bare so the run goes on.
            for (int w = 0; w < 3; w++)
            {
                Fell(board.Wards[w]);
                Assert.IsTrue(board.Restore(w, 600));
            }

            int turned = Turned(board, 45f);

            Assert.Greater(turned, 0, "the fixture has to put a raider at the line");

            for (int w = 0; w < 3; w++)
            {
                Assert.IsTrue(board.Wards[w].Alive, "post " + w + " fell under its barrier");
                Assert.AreEqual(board.Wards[w].Full, board.Wards[w].Health,
                                "post " + w + " was hurt under its barrier");
            }

            var fresh = Board();
            Fell(fresh.Wards[0]);
            fresh.Restore(0, Barrier);

            Run(fresh, Barrier / 10f + .1f);
            Assert.AreEqual(0f, fresh.Wards[0].Barrier, "the barrier outlived its time");
            Assert.AreEqual(0f, fresh.CoverLeft(0));
        }

        // ------------------------------------------------------------------ the wall
        static SiegeBoard Walking()
        {
            var board = Board();
            Run(board, SiegeTuning.FirstWaveAfter + SiegeTuning.RaiderSpacing * 3f, .1f);
            return board;
        }

        [Test]
        public void NothingBehindTheWallGetsPastItWhileItStands()
        {
            for (int row = 0; row < SiegeTuning.BlastRows; row++)
            {
                var board = Walking();
                float line = SiegeTuning.WallMarch(row);

                var behind = new List<SiegeRaider>();
                foreach (var raider in board.Raiders)
                    if (raider.Alive && !raider.Boss && raider.March < line) behind.Add(raider);

                Assert.Greater(behind.Count, 0, "the fixture has to put something behind row " + row);

                var use = SiegeUtility.Apply(board, Wall(), SiegeAim.AtRow(row), null);
                Assert.IsTrue(use.Landed);
                Assert.AreEqual(0, use.Matches, "a wall delivers no damage, so it saves no matches (39)");
                Assert.AreEqual(behind.Count, use.Delivered);

                for (float t = 0f; t < Stands / 10f - .1f; t += .05f)
                {
                    board.Advance(.05f);

                    foreach (var raider in behind)
                        Assert.Less(raider.March, line, "a raider walked through the wall on row " + row);
                }
            }
        }

        [Test]
        public void TheHillWalksOnOnceTheWallFalls()
        {
            var board = Walking();
            float line = SiegeTuning.WallMarch(0);

            var behind = new List<SiegeRaider>();
            foreach (var raider in board.Raiders)
                if (raider.Alive && !raider.Boss && raider.March < line) behind.Add(raider);

            Assert.Greater(board.Wall(0, Stands), 0);

            Run(board, Stands / 10f + .1f);
            Assert.IsFalse(board.Walled);

            // Held bodies walk on: within a few seconds one of them is past where it stood,
            // unless the line killed every one of them first.
            bool past = false, alive = false;
            for (float t = 0f; t < 6f && !past; t += .05f)
            {
                board.Advance(.05f);
                alive = false;
                foreach (var raider in behind)
                {
                    if (!raider.Alive) continue;
                    alive = true;
                    if (raider.March >= line) past = true;
                }
                if (!alive) break;
            }

            Assert.IsTrue(past || !alive, "the hill never walked past where the wall stood");
        }

        [Test]
        public void OneWallAtATimeAndNeverOverAnEmptyHill()
        {
            var board = Walking();

            Assert.Greater(board.Wall(3, Stands), 0);
            Assert.IsFalse(SiegeUtility.Would(board, Wall(), SiegeAim.AtRow(2)), "a second wall while one stands");
            Assert.AreEqual(0, board.Wall(2, Stands));

            var empty = Board();
            foreach (var raider in empty.Raiders) { raider.Health = 0; raider.Alive = false; }
            Assert.IsFalse(SiegeUtility.Would(empty, Wall(), SiegeAim.AtRow(0)));
        }

        [Test]
        public void ABossIsNeverHeldByAWall()
        {
            var board = Board("rg", "warlord:r");

            Run(board, SiegeTuning.FirstWaveAfter + SiegeTuning.BetweenWaves * 4f, .1f);
            foreach (var raider in board.Raiders) { raider.Health = 0; raider.Alive = false; }
            Run(board, SiegeTuning.Breather + .5f, .1f);

            SiegeRaider boss = null;
            foreach (var raider in board.Raiders)
                if (raider.Alive && raider.Boss) boss = raider;

            Assert.IsNotNull(boss, "the fixture has to send a boss");
            Assert.AreEqual(0, board.Holdable(0), "a hill holding only a boss holds nothing");
            Assert.IsFalse(SiegeUtility.Would(board, Wall(), SiegeAim.AtRow(0)));
        }
    }
}
