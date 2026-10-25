using System.Collections.Generic;
using GlimmerGrove.Modes;
using GlimmerGrove.Shuffle;
using GlimmerGrove.Utilities;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The one rule that has to hold on every chapter this mode will ever ship: what a utility is
    /// worth, measured against what it is used on.
    ///
    /// <para>
    /// <b>Why a fixture rather than a number somebody checks.</b> A chapter is made harder by
    /// surging what its raiders carry (invariant 37by), and nothing else in the mode moves with
    /// it - so an authored 440 quietly means less on every chapter after the second, and there is
    /// no reading anywhere that would say so. Every gate stays green: the level parses, par is
    /// derived from the surged health and is correct, the hold simulation never taps a utility at
    /// all, and the shop card prints no number. It is invisible until somebody plays chapter
    /// twenty and says a firepot does nothing.
    /// </para>
    /// <para>
        /// Firepots gain half the health increase; storm damage retains full scaling but reaches
        /// only part of the hill. Bosses take limited damage and cannot be finished by either.
    /// </para>
    /// </summary>
    public sealed class SiegeUtilityScaleTests
    {
        const string Gems = "rgby", Wards = "rgby";

        /// <summary>One wave, four colours, nothing about it deciding anything here.</summary>
        static readonly string[] Waves = { "rgby" };

        /// <summary>A settled field with no run in it, so nothing cascades while the clock walks.</summary>
        static string[] Field() => new[]
        {
            "rrgby",
            "bygbr",
            "gbyrg",
        };

        /// <summary>
        /// A board whose raiders carry <paramref name="tough"/> tenths of their ordinary health.
        /// Ten is the plain chapter; thirteen is a chapter carrying three tenths.
        /// </summary>
        static SiegeBoard Board(int tough)
        {
            var rows = Field();

            Assert.IsTrue(ProtoGrid.TryRead(rows, rows[0].Length, rows.Length,
                                            SiegeLayout.Cells, out var grid, out string error),
                          error);

            var layout = new SiegeLayout(grid, Gems, Wards, Waves, null, 0, null, tough, null);
            Assert.IsNull(layout.Fault, layout.Fault);

            return SiegeBoard.Build(layout);
        }

        /// <summary>
        /// The same board with its first wave standing on the hill.
        ///
        /// <b>The clock is walked rather than the raiders placed</b>, because a raider is minted by
        /// the muster and it is the muster that hands it its surge - placing one here would be the
        /// fixture deciding the very thing it is testing.
        /// </summary>
        static SiegeBoard Mustered(int tough)
        {
            var board = Board(tough);

            for (int i = 0; i < 60 * 120 && board.OnTheHill < 4; i++) board.Advance(1f / 60f);

            Assert.Greater(board.OnTheHill, 0, "the wave never reached the hill");
            return board;
        }

        /// <summary>
        /// <b>A baseline small enough that nothing is capped by the health it lands on</b>, which
        /// is what lets these cases read the multiplier rather than the overkill clamp: a storm of
        /// 700 kills a creeper on both boards and absorbs its health rather than its own figure,
        /// so the two would come out equal and the test would pass while proving nothing.
        /// </summary>
        const int Nibble = 10;

        // ------------------------------------------------------------------ the two multipliers
        /// <summary>
        /// <b><c>Hurt</c> and <c>Health</c> are one multiplier, and that identity is the whole
        /// feature.</b> They are written as two members because they say different things at a
        /// call site; the moment they answer differently, a utility's share of a raider stops
        /// being a constant and every argument below it collapses.
        /// </summary>
        [Test]
        public void DamageAndHealthGoThroughOneMultiplier()
        {
            for (int tenths = 10; tenths <= 60; tenths++)
            {
                var surge = new SiegeSurge(tenths, 10);

                for (int amount = 1; amount <= 2_000; amount += 7)
                    Assert.AreEqual(surge.Health(amount), surge.Hurt(amount),
                                    "tenths " + tenths + ", amount " + amount);
            }
        }

        /// <summary>
        /// Never nought once it was anything, for <c>SiegeSurge.Blow</c>'s reason: a utility that
        /// reads as landing and takes nothing is one the player will believe is broken.
        /// </summary>
        [Test]
        public void ADamageFigureIsNeverRoundedAwayToNothing()
        {
            Assert.AreEqual(0, new SiegeSurge(10, 10).Hurt(0));
            Assert.AreEqual(0, new SiegeSurge(10, 10).Hurt(-5));
            Assert.AreEqual(1, new SiegeSurge(10, 10).Hurt(1));
        }

        // ------------------------------------------------------------------ the hill
        /// <summary>
        /// Half the increase, preserving the original baseline, on every board mode and per target.
        /// </summary>
        [Test]
        public void AFirepotGainsHalfTheHealthIncreaseInEveryMode()
        {
            for (int mode = 0; mode < 3; mode++)
            {
                var plain = Target(0, surge: 10);
                var doubled = Target(1, surge: 20);
                var uneven = Target(2, surge: 13);
                var board = WithTargets(mode, plain, doubled, uneven);
                var hits = new List<SiegeStrike>();
                Assert.AreEqual(73, board.Blast(0, SiegeTuning.RowOf(.5f), 20, hits));
                Assert.AreEqual(20, plain.MaxHealth - plain.Health);
                Assert.AreEqual(30, doubled.MaxHealth - doubled.Health);
                Assert.AreEqual(23, uneven.MaxHealth - uneven.Health);
                Assert.AreEqual(3, hits.Count);
            }
        }

        /// <summary>The same of a stormcall, which is the other kind measured in hill health.</summary>
        [Test]
        public void AStormcallTakesTheSameShareOfASurgedHillAsOfAPlainOne()
        {
            int plain = Mustered(10).Storm(Nibble, null);
            int surged = Mustered(13).Storm(Nibble, null);

            Assert.Greater(plain, 0, "the storm reached nobody, so this case proves nothing");
            Assert.AreEqual(plain * 13, surged * 10);
        }

        /// <summary>
        /// The hill it is measured against really did climb - the other half of the two cases
        /// above, which would both pass if <em>nothing</em> scaled.
        /// </summary>
        [Test]
        public void ASurgedChaptersRaidersReallyDoCarryMore()
        {
            int plain = Health(Mustered(10));
            int surged = Health(Mustered(13));

            Assert.AreEqual(plain * 13, surged * 10, "the fixture's own premise");
        }

        /// <summary>
        /// <b><c>UtilityKind.Storm</c>'s own promise, kept by the ratio rather than by a number.</b>
        /// Its remarks say it hurts a boss and never fells one - which was a fact about 700 against
        /// 2,050 and would have stopped being true at the chapter that surged past it. Both sides
        /// climb together, so it is now true at every toughness this mode can reach.
        /// </summary>
        [Test]
        public void AStormcallStillCannotFellABossHoweverToughTheChapter()
        {
            for (int tenths = 10; tenths <= 120; tenths++)
            {
                var surge = new SiegeSurge(tenths, 10);

                Assert.Less(surge.Hurt(700), surge.Health(SiegeTuning.BossHealth),
                            "a storm fells the smallest boss at " + tenths + " tenths");
            }
        }

        // ------------------------------------------------------------------ the taxonomy
        /// <summary>
        /// Every kind says what its magnitude is measured in. <b>The guard against the fault this
        /// whole fixture is about</b>: a damaging kind added years from now that nobody thought to
        /// scale would be a number that quietly decays, and the only moment anybody is in a
        /// position to notice is the moment the kind is written.
        /// </summary>
        [Test]
        public void EveryUtilityKindDeclaresWhatItsMagnitudeIsMeasuredIn()
        {
            foreach (UtilityKind kind in System.Enum.GetValues(typeof(UtilityKind)))
            {
                var unit = UtilityUnits.Of(kind);

                if (kind == UtilityKind.None)
                {
                    Assert.AreEqual(UtilityUnit.None, unit);
                    continue;
                }

                Assert.AreNotEqual(UtilityUnit.None, unit,
                                   kind + " has no unit, so nothing knows whether its magnitude "
                                   + "climbs with the board it is used on");
            }
        }

        /// <summary>
        /// Which kinds climb, written down. <b>A list rather than a rule</b>, because the point of
        /// it is to fail when the set changes: adding a kind to it is a decision somebody has to
        /// take on purpose, and taking one out is the decay this fixture exists to catch.
        /// </summary>
        [Test]
        public void OnlyTheKindsMeasuredInHillHealthClimb()
        {
            Assert.IsTrue(UtilityUnits.Climbs(UtilityKind.Blast));
            Assert.IsTrue(UtilityUnits.Climbs(UtilityKind.Storm));

            Assert.IsFalse(UtilityUnits.Climbs(UtilityKind.Mend),
                           "a ward's stones are a fixed 14 and a blow is never surged, so a "
                           + "mending is worth the same share of the line on every chapter");

            Assert.IsFalse(UtilityUnits.Climbs(UtilityKind.Surge),
                           "fuel is bounded by the tube it is poured into, so a surged pour would "
                           + "be a pour a ward cannot hold");

            Assert.IsFalse(UtilityUnits.Climbs(UtilityKind.Gravity),
                           "a second a raider is held is a second on every chapter");

            Assert.IsFalse(UtilityUnits.Climbs(UtilityKind.None));
        }

        [Test]
        public void AStormHitsAtLeastFiveOrSixtyPercentInEveryMode()
        {
            int[] expected = { 0, 1, 2, 3, 4, 5, 5, 5, 5, 5, 6, 6, 7 };
            for (int mode = 0; mode < 3; mode++)
                for (int count = 0; count < expected.Length; count++)
                {
                    var targets = new SiegeRaider[count];
                    for (int i = 0; i < count; i++) { targets[i] = Target(i); targets[i].Health = 1; }
                    var board = WithTargets(mode, targets);
                    var hits = new List<SiegeStrike>();
                    var item = new UtilityItem("stormcall", UtilityKind.Storm, 700, 40, 3, 4);
                    Assert.AreEqual(count > 0, SiegeUtility.Would(board, item, default));
                    var use = SiegeUtility.Apply(board, item, default, hits);
                    Assert.AreEqual(expected[count], hits.Count, $"mode {mode}, count {count}");
                    Assert.AreEqual(expected[count], use.Delivered);
                    Assert.AreEqual(count > 0, use.Landed);
                    Assert.AreEqual(SiegeUtility.MatchesFor(use.Delivered), use.Matches);
                    int alive = 0;
                    foreach (var target in targets) if (target.Alive) alive++;
                    Assert.AreEqual(count - expected[count], alive);
                    if (count > 5) Assert.Greater(alive, 0);
                }
        }

        [Test]
        public void StormsReserveBossesBeforeChoosingOrdinaryTargetsInEveryMode()
        {
            for (int mode = 0; mode < 3; mode++)
            {
                var targets = new SiegeRaider[10];
                for (int i = 0; i < 10; i++) targets[i] = Target(i, i < 2 ? SiegeKind.Boss : SiegeKind.Creeper);
                var board = WithTargets(mode, targets);
                var hits = new List<SiegeStrike>();
                board.Storm(1, hits);
                Assert.AreEqual(6, hits.Count);
                var ids = new HashSet<int>();
                foreach (var hit in hits) ids.Add(hit.Raider);
                Assert.AreEqual(6, ids.Count);
                Assert.IsTrue(ids.Contains(0));
                Assert.IsTrue(ids.Contains(1));
            }
        }

        [Test]
        public void BothUtilitiesLimitReducedBossPoolsAndNeverFinishABossInEveryMode()
        {
            for (int mode = 0; mode < 3; mode++)
                foreach (bool storm in new[] { false, true })
                {
                    var boss = Target(0, SiegeKind.Boss, share: 25);
                    boss.Hexed = 1f;
                    var board = WithTargets(mode, boss);
                    int before = boss.Health;
                    if (storm) board.Storm(20, null);
                    else board.Blast(boss.Column, SiegeTuning.RowOf(boss.March), 20, null);
                    Assert.AreEqual(5, before - boss.Health,
                                    "a reduced boss pool or a hex must not amplify the utility's share");

                    boss.Health = 2;
                    if (storm) board.Storm(99999, null);
                    else board.Blast(boss.Column, SiegeTuning.RowOf(boss.March), 99999, null);
                    Assert.AreEqual(1, boss.Health);
                    Assert.IsTrue(boss.Alive);

                    var hits = new List<SiegeStrike>();
                    if (storm) Assert.AreEqual(0, board.Storm(99999, hits));
                    else Assert.AreEqual(0, board.Blast(boss.Column, SiegeTuning.RowOf(boss.March), 99999, hits));
                    Assert.IsEmpty(hits);
                    Assert.AreEqual(1, boss.Health);
                }
        }

        [Test]
        public void AStormIncludesEveryBossEvenWhenTheyExceedTheQuota()
        {
            var bosses = new SiegeRaider[6];
            for (int i = 0; i < bosses.Length; i++) bosses[i] = Target(i, SiegeKind.Boss);
            var board = WithTargets(1, bosses);
            var hits = new List<SiegeStrike>();
            board.Storm(99999, hits);
            Assert.AreEqual(6, hits.Count);
            foreach (var boss in bosses) { Assert.IsTrue(boss.Alive); Assert.AreEqual(1, boss.Health); }
            Assert.IsFalse(board.CanStorm);
        }

        [Test]
        public void AStormExcludesDeadWaitingAndArrivingEnemies()
        {
            var targets = new SiegeRaider[8];
            for (int i = 0; i < 7; i++) targets[i] = Target(i);
            targets[5].Alive = false;
            targets[6].Wait = 10f;
            targets[7] = Target(7, SiegeKind.Boss);
            targets[7].March = 0f;
            var hits = new List<SiegeStrike>();
            WithTargets(0, targets).Storm(1, hits);
            Assert.AreEqual(5, hits.Count);
            foreach (var hit in hits) Assert.Less(hit.Raider, 5);
        }

        [Test]
        public void AStormChoosesTheSameTargetsWithOrWithoutAStrikeReport()
        {
            var a = new SiegeRaider[10];
            var b = new SiegeRaider[10];
            for (int i = 0; i < 10; i++) { a[i] = Target(i); b[i] = Target(i); }
            var hits = new List<SiegeStrike> { new SiegeStrike(999, 1, false) };
            Assert.AreEqual(6, WithTargets(0, a).Storm(1, hits));
            Assert.AreEqual(6, WithTargets(0, b).Storm(1, null));
            Assert.AreEqual(999, hits[0].Raider, "an existing report entry must not be shuffled");
            for (int i = 0; i < 10; i++) Assert.AreEqual(a[i].Health, b[i].Health);
        }

        [Test]
        public void DroppedBombsKeepTheirFullHealthScalingInEveryMode()
        {
            for (int mode = 0; mode < 3; mode++)
            {
                var bomber = Target(0, SiegeKind.Bomber, surge: 20);
                bomber.Health = 1;
                var enemy = Target(1, surge: 20);
                var board = WithTargets(mode, bomber, enemy);
                board.BombDamage = 10;
                board.Blast(0, SiegeTuning.RowOf(.5f), 1, null);
                Assert.AreEqual(1, board.Bombs.Count);
                int before = enemy.Health;
                Assert.AreEqual(20, board.Detonate(board.Bombs[0].Id, null));
                Assert.AreEqual(20, before - enemy.Health);
            }
        }

        // ------------------------------------------------------------------ helpers
        static SiegeRaider Target(int id, SiegeKind kind = SiegeKind.Creeper, int surge = 10,
                                  int share = 100)
        {
            var target = new SiegeRaider(id, 0, kind, 0, 0f, new SiegeSurge(surge, 10), share);
            target.March = target.Boss ? target.Hold : .5f;
            return target;
        }

        static SiegeBoard WithTargets(int mode, params SiegeRaider[] targets)
        {
            var rows = Field();
            Assert.IsTrue(ProtoGrid.TryRead(rows, rows[0].Length, rows.Length, SiegeLayout.Cells,
                                            out var grid, out string error), error);
            SiegeRamp ramp = mode == 1 ? (SiegeRamp)new SiegeEndless("rgby", 0, 20, .55f)
                           : mode == 2 ? new ShuffleRamp("rgby", 24, .5f) : null;
            var layout = new SiegeLayout(grid, Gems, Wards, ramp == null ? Waves : null,
                                         null, 0, ramp);
            Assert.IsNull(layout.Fault, layout.Fault);
            var board = SiegeBoard.Build(layout);
            // Place the same controlled hill on each real mode; spawning and timing are unrelated.
            ((List<SiegeRaider>)board.Raiders).AddRange(targets);
            return board;
        }

        /// <summary>What the whole hill is carrying, which is what a utility is measured against.</summary>
        static int Health(SiegeBoard board)
        {
            int whole = 0;

            for (int i = 0; i < board.Raiders.Count; i++)
                if (board.Raiders[i].Alive) whole += board.Raiders[i].MaxHealth;

            return whole;
        }
    }
}
