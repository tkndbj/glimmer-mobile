using System.Collections.Generic;
using System.Text;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The fight: what a boss is promised, and the gate every chapter's boss rungs go through.
    ///
    /// <para>
    /// <b>This file exists because nothing measured a boss's life.</b> Four chapters shipped
    /// eight bosses that died on the walk in or on their ground before their first spell had
    /// left their hand, and every gate was green — the hold simulation scored a run
    /// <em>better</em> for killing one faster. What a boss has to be is written in
    /// <see cref="SiegeTuning.BossPhases"/>; what is held here is that it <em>is</em> that, on
    /// the rules alone (untouchable on the walk in, guarded at each phase, never taken through
    /// two phases in one blow) and on every shipped rung under a played line that dumps every
    /// charge it banks.
    /// </para>
    /// <para>
    /// <b>A new chapter passes through <see cref="EveryShippedBossRungIsAFight"/> by being added
    /// to the rung tables</b>, which <c>Tools/verify/rungs.py</c> already holds to the shipped
    /// bodies — so a boss that cannot fight on its rung is a red build, not a verdict from play.
    /// </para>
    /// </summary>
    public sealed partial class SiegeRuleTests
    {
        /// <summary>Every boss the mode has, walked off the enum so a ninth cannot be left out.</summary>
        static IEnumerable<SiegeKind> EveryBoss()
        {
            foreach (SiegeKind kind in System.Enum.GetValues(typeof(SiegeKind)))
                if (SiegeTuning.IsBoss(kind)) yield return kind;
        }

        /// <summary>A duel against <paramref name="kind"/> wearing the first ward's colour, cogs dealt.</summary>
        static SiegeLayout DuelWith(SiegeKind kind)
            => Layout(Field, Gems, Wards, new string[0], SiegeTuning.NameOf(kind) + ":r", Cogs);

        /// <summary>Fills every tube and banks every charge, so the line can dump the most it ever could.</summary>
        static void Bank(SiegeBoard board)
        {
            for (int w = 0; w < board.Wards.Count; w++)
            {
                board.Wards[w].Fuel = board.Wards[w].Capacity;
                board.Wards[w].Charges = SiegeTuning.MostCharges;
            }
        }

        /// <summary>The least a fight may be: casts thrown and seconds stood, on every shipped rung.</summary>
        const int LeastCasts = SiegeTuning.BossPhases;
        const float LeastStood = 10f;

        // ------------------------------------------------------------------ the rules
        [Test]
        public void TheFightIsPhasedInThirdsFromTheTop()
        {
            Assert.AreEqual(3, SiegeTuning.BossPhases);
            Assert.AreEqual(2000, SiegeTuning.PhaseFloor(3000, 0));
            Assert.AreEqual(1000, SiegeTuning.PhaseFloor(3000, 1));
            Assert.AreEqual(0, SiegeTuning.PhaseFloor(3000, 2));
            Assert.AreEqual(0, SiegeTuning.PhaseFloor(3000, 9), "past the last phase is the last phase");
        }

        [Test]
        public void EveryPhaseCastsFasterThanTheLastAndNoneOverlapsItsOwnTell()
        {
            var pace = SiegeTuning.PhasePaceHundredths;
            Assert.AreEqual(SiegeTuning.BossPhases, pace.Length,
                            "a cadence per phase, and exactly that many");
            Assert.AreEqual(100, pace[0], "the first phase is the kind's own cadence");

            for (int p = 1; p < pace.Length; p++)
                Assert.Less(pace[p], pace[p - 1], $"phase {p} is no faster than phase {p - 1}");

            float floor = SiegeTuning.BossTell + SiegeTuning.BossFlight;

            foreach (var kind in EveryBoss())
                for (int p = 0; p < SiegeTuning.BossPhases; p++)
                    Assert.GreaterOrEqual(SiegeTuning.CastEveryFor(kind, p), floor,
                        $"{kind} in phase {p} winds up its next spell before the last has landed");
        }

        /// <summary>
        /// A boss walks on untouched, whatever the line throws: bolts, every banked charge, a
        /// storm and a firepot all find nothing to hurt until it stands.
        /// </summary>
        [Test]
        public void EveryBossWalksOnUntouched()
        {
            foreach (var kind in EveryBoss())
            {
                var board = SiegeBoard.Build(DuelWith(kind));
                SiegeRaider boss = null;

                for (int i = 0; i < 60 * 60 && (boss == null || !boss.InPlace); i++)
                {
                    Bank(board);
                    board.Advance(1f / 60f);

                    boss = board.Warlord;
                    if (boss == null || !boss.OnTheHill || boss.InPlace) continue;

                    Assert.IsTrue(boss.Untouchable, $"{kind} was touchable on the walk in");

                    for (int w = 0; w < board.Wards.Count; w++)
                        Assert.IsFalse(board.Overcharge(w, null).Landed,
                                       $"a charge landed on {kind} while it was walking on");

                    Assert.AreEqual(0, board.Storm(9999, null), $"a storm hurt {kind} walking on");
                    Assert.AreEqual(0, board.Blast(boss.Lane, SiegeTuning.RowOf(boss.March), 9999, null),
                                    $"a firepot hurt {kind} walking on");
                }

                Assert.IsNotNull(boss, $"{kind} never walked on");
                Assert.IsTrue(boss.InPlace, $"{kind} never reached its ground");
                Assert.AreEqual(boss.MaxHealth, boss.Health, $"{kind} arrived hurt");
                Assert.AreEqual(0, boss.Phase);
                Assert.IsTrue(boss.Guarded, $"{kind} arrived with no guard up");
            }
        }

        /// <summary>
        /// Behind its guard a boss takes nothing, its opening spell is still thrown, and the
        /// guard drops the moment that spell lands - never later than the deadline.
        /// </summary>
        [Test]
        public void AGuardedBossTakesNothingUntilItsOpeningSpellHasLanded()
        {
            foreach (var kind in EveryBoss())
            {
                var board = SiegeBoard.Build(DuelWith(kind));
                var boss = Standing(board);

                Assert.IsTrue(boss.Guarded, $"{kind} stood with no guard");

                bool opened = false;
                float held = 0f;

                for (int i = 0; i < 60 * 10 && boss.Guarded; i++)
                {
                    Bank(board);
                    for (int w = 0; w < board.Wards.Count; w++) board.Overcharge(w, null);
                    board.Storm(9999, null);

                    var report = board.Advance(1f / 60f);
                    held += 1f / 60f;

                    foreach (var cast in report.Casts)
                        if (cast.Raider == boss.Id && cast.Opens) opened = true;

                    // The frame the guard drops is a frame the line may fire on - `Arrive` runs
                    // before `Shoot` - so what is held is every frame it is still up after.
                    if (boss.Guarded)
                        Assert.AreEqual(boss.MaxHealth, boss.Health,
                                        $"{kind} was hurt behind its guard");
                }

                Assert.IsFalse(boss.Guarded, $"{kind}'s guard never dropped");
                Assert.IsTrue(opened, $"{kind} dropped its guard without throwing its opening spell");
                Assert.LessOrEqual(held, SiegeTuning.GuardMost + .1f,
                                   $"{kind}'s guard outlived its deadline");

                // The promised shape: the opener lands inside the least a guard stands, so the
                // guard drops at exactly that - and no later.
                float landed = SiegeTuning.PhaseWake + SiegeTuning.BossTell + SiegeTuning.BossFlight;
                Assert.Less(landed, SiegeTuning.GuardLeast, "the opener lands after the least guard, so the floor is not the floor");
                Assert.AreEqual(SiegeTuning.GuardLeast, held, .1f, $"{kind}'s guard did not drop when it was due");
            }
        }

        /// <summary>
        /// A blow that would cross a phase's floor stops at it: the next phase opens, guard up,
        /// and what would have crossed is gone. This is what makes the phases a promise.
        /// </summary>
        [Test]
        public void NoBlowTakesABossThroughTwoPhases()
        {
            foreach (var kind in EveryBoss())
            {
                var board = SiegeBoard.Build(DuelWith(kind));
                var boss = Standing(board);

                for (int i = 0; i < 60 * 10 && boss.Guarded; i++) board.Advance(1f / 60f);
                Assert.IsFalse(boss.Guarded, $"{kind} never dropped its first guard");

                // The storm strikes everything on the hill, a bonecaller's raised creepers
                // included, so what is read is the boss's own health and never the total.
                board.Storm(boss.MaxHealth * 10, null);

                Assert.AreEqual(SiegeTuning.PhaseFloor(boss.MaxHealth, 0), boss.Health,
                                $"a storm took {kind} past its first floor");
                Assert.AreEqual(1, boss.Phase, $"{kind}'s second phase did not open at the floor");
                Assert.IsTrue(boss.Guarded, $"{kind}'s second phase opened with no guard");
                Assert.IsTrue(boss.Alive);

                // And again through the second guard, which proves the guard is per phase.
                Assert.AreEqual(0, board.Storm(9999, null), $"{kind} was hurt behind its second guard");

                for (int i = 0; i < 60 * 10 && boss.Guarded; i++) board.Advance(1f / 60f);
                Assert.IsFalse(boss.Guarded, $"{kind} never dropped its second guard");

                board.Storm(boss.MaxHealth * 10, null);
                Assert.AreEqual(2, boss.Phase);
                Assert.IsTrue(boss.Alive, $"{kind} was killed through its last floor");
                Assert.IsTrue(boss.Guarded);

                for (int i = 0; i < 60 * 10 && boss.Guarded; i++) board.Advance(1f / 60f);
                board.Storm(boss.MaxHealth * 10, null);
                Assert.IsFalse(boss.Alive, $"{kind} survived a blow in its last phase with no floor under it");
            }
        }

        /// <summary>
        /// A wall is not a guard: a boss with nothing it could ever throw drops its guard at
        /// once, and one with nothing to aim at drops it at the deadline.
        /// </summary>
        [Test]
        public void AGuardInFrontOfNoSpellDrops()
        {
            // A bonecaller with its raises spent has no spell left to open a phase with.
            {
                var board = SiegeBoard.Build(DuelWith(SiegeKind.Bonecaller));
                var boss = Standing(board);
                boss.Raised = SiegeTuning.Raises;

                float held = 0f;
                for (int i = 0; i < 60 * 10 && boss.Guarded; i++) { board.Advance(1f / 60f); held += 1f / 60f; }

                Assert.IsFalse(boss.Guarded);
                Assert.LessOrEqual(held, SiegeTuning.PhaseWake + .1f,
                                   "a bonecaller with nothing to raise stood guarded past its wake");
            }

            // A shackler whose every ward is already chained finds nothing to aim at.
            {
                var board = SiegeBoard.Build(DuelWith(SiegeKind.Shackler));
                var boss = Standing(board);
                foreach (var ward in board.Wards) ward.Shackle();

                float held = 0f;
                for (int i = 0; i < 60 * 10 && boss.Guarded; i++)
                {
                    foreach (var ward in board.Wards) ward.Shackle();
                    board.Advance(1f / 60f);
                    held += 1f / 60f;
                }

                Assert.IsFalse(boss.Guarded, "a shackler with nothing to chain stood guarded for ever");
                Assert.AreEqual(SiegeTuning.GuardMost, held, .1f, "the guard did not drop at its deadline");
            }
        }

        /// <summary>A stunned boss still runs its guard down: a stun is seconds off the fight, never a wall.</summary>
        [Test]
        public void AStunnedBossStillDropsItsGuardAtTheDeadline()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Boss));
            var boss = Standing(board);

            float held = 0f;
            for (int i = 0; i < 60 * 10 && boss.Guarded; i++)
            {
                boss.Stun = 1f;
                boss.Steady = 0f;
                board.Advance(1f / 60f);
                held += 1f / 60f;
            }

            Assert.IsFalse(boss.Guarded);
            Assert.AreEqual(SiegeTuning.GuardMost, held, .1f);
        }

        // ------------------------------------------------------------------ played
        /// <summary>
        /// Every boss, alone on its hill against an unhurried player who dumps every charge the
        /// moment it banks, throws at least one spell per phase and stands long enough to be a
        /// fight - and the fight <em>ends</em>, one way or the other.
        ///
        /// <b>A duel is harder than anything shipped</b> - no escort means no cogs, so the line
        /// fights at rank nought with nothing banked - and the overlord takes it at rank nought,
        /// which is the finale doing its job. Whether a boss is a wall is asked of the shipped
        /// rungs (<see cref="EveryShippedBossRungIsAFight"/>), where it can be answered.
        /// </summary>
        [Test]
        public void EveryBossFightsBeforeItFallsAgainstAGreedyLine()
        {
            var table = new StringBuilder();
            var faults = new List<string>();

            foreach (var kind in EveryBoss())
            {
                var board = SiegeBoard.Build(DuelWith(kind));
                SiegeRaider boss = null;

                int matches = Hold(board, Unhurried, out int seconds,
                                   b => { var w = b.Warlord; if (w != null) boss = w; });

                Assert.IsNotNull(boss, $"{kind} never walked on");

                table.AppendLine($"  {kind,-13} stood {boss.Stood,5:0.0}s  cast {boss.Casts,2}"
                                 + $"  phases {boss.Phase + 1}  fell {(!boss.Alive ? "yes" : "no"),-3}"
                                 + $"  matches {matches,3}  run {seconds,3}s");

                bool ended = !boss.Alive || board.WardsStanding == 0;

                if (!ended)
                    faults.Add($"{kind}: neither it nor the line fell in ten minutes, so the duel is a stalemate");
                if (boss.Casts < LeastCasts)
                    faults.Add($"{kind}: threw {boss.Casts} spells against a floor of {LeastCasts}");
                if (boss.Stood < LeastStood)
                    faults.Add($"{kind}: stood {boss.Stood:0.0}s against a floor of {LeastStood}");
                if (!boss.Alive && boss.Phase != SiegeTuning.BossPhases - 1)
                    faults.Add($"{kind}: fell in phase {boss.Phase + 1} of {SiegeTuning.BossPhases}");
            }

            Assert.IsEmpty(faults, string.Join("\n", faults) + "\n\nthe duels read:\n" + table);
        }

        /// <summary>
        /// The gate: on every shipped rung that sends a boss, at every rhythm the chapter sweep
        /// plays, the boss throws at least <see cref="LeastCasts"/> spells and stands at least
        /// <see cref="LeastStood"/> seconds before it falls. A boss rung that fails this is a
        /// boss the player never met.
        /// </summary>
        [Test]
        public void EveryShippedBossRungIsAFight()
        {
            float[] rhythms = { 2.20f, 2.25f, 2.30f, 2.35f, 2.40f, 2.45f, 2.50f, 2.55f, 2.60f };

            var table = new StringBuilder();
            var faults = new List<string>();

            foreach (var (name, rungs) in ShippedChapters())
            {
                for (int i = 0; i < rungs.Length; i++)
                {
                    var rung = rungs[i];
                    if (string.IsNullOrEmpty(rung.Boss)) continue;

                    var layout = rung.Built();

                    int leastCasts = int.MaxValue, mostCasts = 0, fell = 0, held = 0;
                    float leastStood = float.MaxValue, mostStood = 0f;

                    foreach (float rhythm in rhythms)
                    {
                        var board = SiegeBoard.Build(layout);
                        SiegeRaider boss = null;

                        Hold(board, rhythm, out int _, b => { var w = b.Warlord; if (w != null) boss = w; });

                        // A boss waits for the hill to be cleared (37dn), so a line that fell to
                        // the wave before it never meets the boss at all - a lost run, and not a
                        // reading of the fight.
                        if (boss == null && board.WardsStanding == 0) continue;

                        Assert.IsNotNull(boss, $"{rung.Id}: the boss never walked on at {rhythm}");

                        if (!boss.Alive) fell++;
                        if (board.IsFinished && board.WardsStanding >= 2) held++;

                        // **A run the line lost is not a reading of the fight.** The boss stands
                        // over a dead line with nothing to aim at and nothing shooting back, so
                        // its casts and its seconds say nothing about it; whether a rung is lost
                        // too often is the chapter sweep's question, not this one's.
                        if (boss.Alive) continue;

                        if (boss.Casts < leastCasts) leastCasts = boss.Casts;
                        if (boss.Casts > mostCasts) mostCasts = boss.Casts;
                        if (boss.Stood < leastStood) leastStood = boss.Stood;
                        if (boss.Stood > mostStood) mostStood = boss.Stood;

                        if (boss.Casts < LeastCasts)
                            faults.Add($"{rung.Id} at {rhythm:0.00}: {rung.Boss} threw {boss.Casts} "
                                       + $"spells against a floor of {LeastCasts}");
                        if (boss.Stood < LeastStood)
                            faults.Add($"{rung.Id} at {rhythm:0.00}: {rung.Boss} stood {boss.Stood:0.0}s "
                                       + $"against a floor of {LeastStood}");
                    }

                    // And a boss that fell at no rhythm is a wall, which is this gate's question.
                    if (fell == 0)
                        faults.Add($"{rung.Id}: {rung.Boss} fell at none of {rhythms.Length} rhythms, "
                                   + "so an unhurried player never wins this fight");

                    table.AppendLine($"  {name,-10} {rung.Id,-18} {rung.Boss,-15}"
                                     + $" cast {leastCasts,2}-{mostCasts,2}  stood {leastStood,5:0.0}-{mostStood,5:0.0}s"
                                     + $"  fell {fell}/{rhythms.Length}  held {held}/{rhythms.Length}");
                }
            }

            if (faults.Count > 0)
            {
                // The first faulting rung, traced at the first faulting rhythm, so the failure
                // says what happened rather than only that it did.
                var first = faults[0];
                foreach (var (name, rungs) in ShippedChapters())
                    foreach (var rung in rungs)
                        if (!string.IsNullOrEmpty(rung.Boss) && first.StartsWith(rung.Id + " at "))
                        {
                            float at = float.Parse(first.Substring(rung.Id.Length + 4, 4),
                                                   System.Globalization.CultureInfo.InvariantCulture);
                            table.AppendLine().AppendLine($"{rung.Id} at {at:0.00} reads:")
                                 .Append(Trace(rung.Built(), at));
                            goto traced;
                        }
                traced:;
            }

            Assert.IsEmpty(faults, string.Join("\n", faults) + "\n\nthe boss rungs read:\n" + table);

            // On the console rather than through `TestContext`, which the offline runner does
            // not stand up - and this table is the one reading of the fight a re-tune wants
            // to see when everything passes.
            System.Console.WriteLine("the boss rungs read:\n" + table);
        }

        /// <summary>Walks the clock until this board's boss is standing on its ground with its guard down.</summary>
        static SiegeRaider Unguarded(SiegeBoard board)
        {
            var boss = Standing(board);

            for (int i = 0; i < 60 * 10 && boss.Guarded; i++) board.Advance(1f / 60f);

            Assert.IsFalse(boss.Guarded, "the boss never dropped its guard");
            return boss;
        }

        /// <summary>
        /// A second-by-second reading of one rung at one rhythm, for when the fight gate names a
        /// rung and the number alone does not say why.
        /// </summary>
        static string Trace(SiegeLayout layout, float rhythm)
        {
            var board = SiegeBoard.Build(layout);
            var log = new StringBuilder();
            SiegeRaider boss = null;
            int last = -1;

            Hold(board, rhythm, out int _, b =>
            {
                var w = b.Warlord;
                if (w != null) boss = w;

                int second = (int)b.Attention.Elapsed;
                if (second == last) return;
                last = second;

                var wards = new StringBuilder();
                foreach (var ward in b.Wards)
                    wards.Append(ward.Alive ? ward.Health.ToString() : "x").Append(' ');

                int hill = 0;
                foreach (var r in b.Raiders) if (r.Alive && r.OnTheHill) hill++;

                log.Append($"  t={second,3} wave {b.Wave} hill {hill,2} wards [{wards}]");
                if (boss != null)
                    log.Append($" boss {boss.Kind} alive={boss.Alive} wait={boss.Wait:0.0} march={boss.March:0.00}"
                               + $" place={boss.InPlace} guard={boss.Guard:0.0} phase={boss.Phase}"
                               + $" hp={boss.Health}/{boss.MaxHealth} casts={boss.Casts} stood={boss.Stood:0.0}");
                log.AppendLine();
            });

            return log.ToString();
        }

        /// <summary>Every chapter's rung table, by name, so a fixture can walk the mode rather than one chapter.</summary>
        static IEnumerable<(string Name, Rung[] Rungs)> ShippedChapters()
        {
            yield return ("thornwatch", Chapter);
            yield return ("broodmarch", Broodmarch);
            yield return ("barrowfell", Barrowfell);
            yield return ("ashenhold", Ashenhold);
            yield return ("thundercrag", Thundercrag);
        }
    }
}
