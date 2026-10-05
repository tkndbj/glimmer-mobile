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
    /// <b>The promise is three sentences, and every one of them has a fixture below.</b> A boss
    /// is alone on the hill for as long as it lives; it walks on untouchable; and from the
    /// frame it plants, everything the player has lands on it at full weight with nothing held
    /// back.
    /// </para>
    /// <para>
    /// <b>The third replaced a floor, which replaced a guard.</b> Both refused the player's
    /// damage to buy a fight its seconds, and both came back from play as a broken game. The
    /// owner's ruling on 2026-10-05 ended it: what makes a boss last is its health.
    /// </para>
    /// <para>
    /// <b>A new chapter passes through <see cref="EveryShippedBossRungIsAFight"/> by being added
    /// to the rung tables</b>, which <c>Tools/verify/rungs.py</c> already holds to the shipped
    /// bodies.
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

        /// <summary>
        /// The Infinite lane over this fixture's own field: waves that never stop, dealing a boss
        /// on <c>SiegeEndless</c>'s own schedule.
        ///
        /// <b>Built here rather than borrowed from <c>SiegeEndlessTests</c></b>, because what this
        /// file asks of it is a <em>played</em> question - the schedule fixture never stands a
        /// board up at all.
        /// </summary>
        static SiegeLayout EndlessLane()
        {
            Assert.IsTrue(ProtoGrid.TryRead(Field, Field[0].Length, Field.Length,
                                            SiegeLayout.Cells, out var grid, out string error),
                          error);

            return new SiegeLayout(grid, Gems, Wards, null, null, Cogs,
                                   new SiegeEndless(Wards, Cogs, 20, .55f));
        }

        /// <summary>Fills every tube and banks every charge, so the line can dump the most it ever could.</summary>
        static void Bank(SiegeBoard board)
        {
            for (int w = 0; w < board.Wards.Count; w++)
            {
                board.Wards[w].Fuel = board.Wards[w].Capacity;
                board.Wards[w].Charges = SiegeTuning.MostCharges;
            }
        }

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
        /// <b>A boss wears no colour, so the whole line answers it and no ward answers it
        /// better.</b> The rule lives in one place (<see cref="SiegeTuning.EveryWardReaches"/>,
        /// read through <c>SiegeWard.ReachTenths</c>); what is pinned here is that it is the
        /// <em>full</em> weight rather than some fraction of one, for every boss the mode has, so
        /// a ninth cannot be added at a discount without this going red.
        /// </summary>
        [Test]
        public void EveryWardReachesEveryBossAtFullWeight()
        {
            Assert.AreEqual(10, SiegeTuning.BossReachTenths,
                            "a boss is answered at less than a full bolt, so three of the four "
                            + "turrets a player carries are worth less against it than the one "
                            + "whose colour it happens to wear");

            foreach (var kind in EveryBoss())
                Assert.IsTrue(SiegeTuning.EveryWardReaches(kind),
                              $"{kind} is answered by its own colour alone, so a duel against it "
                              + "is fought by a quarter of the line");

            // And played: a duel against a red-tokened boss, every ward fed, every one firing.
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Boss));
            var boss = Standing(board);

            var landed = new int[board.Wards.Count];

            for (int i = 0; i < 60 * 20 && boss.Alive; i++)
            {
                for (int w = 0; w < board.Wards.Count; w++)
                    board.Wards[w].Fuel = board.Wards[w].Capacity;

                foreach (var bolt in board.Advance(1f / 60f).Bolts)
                    if (!bolt.Extra && bolt.Weak) landed[bolt.Ward]++;
            }

            for (int w = 0; w < landed.Length; w++)
                Assert.Greater(landed[w], 0,
                               $"ward {w} never landed a full-weight bolt on a boss, so the line "
                               + "does not answer a duel evenly");
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

                    Assert.IsTrue(boss.Arriving, $"{kind} was touchable on the walk in");
                    Assert.IsTrue(boss.Impervious, $"{kind} could be hurt on the walk in");
                    Assert.AreEqual(boss.MaxHealth, boss.Health, $"{kind} was hurt walking on");

                    for (int w = 0; w < board.Wards.Count; w++)
                        Assert.IsFalse(board.CanOvercharge(w),
                                       $"a tube was offered against {kind} while it walked on");

                    for (int w = 0; w < board.Wards.Count; w++)
                        Assert.IsFalse(board.Overcharge(w, null).Landed,
                                       $"a charge landed on {kind} while it was walking on");

                    Assert.AreEqual(0, board.Storm(9999, null), $"a storm hurt {kind} walking on");
                    Assert.AreEqual(0, board.Blast(boss.Lane, SiegeTuning.RowOf(boss.March), 9999, null),
                                    $"a firepot hurt {kind} walking on");
                }

                Assert.IsNotNull(boss, $"{kind} never walked on");
                Assert.IsTrue(boss.InPlace, $"{kind} never reached its ground");
                Assert.AreEqual(0, boss.Phase);
            }
        }

        /// <summary>
        /// <b>The frame a boss plants is a frame the line fires on</b>, and this is the fixture
        /// the owner's report is written into.
        ///
        /// <para>
        /// A boss used to plant and then stand behind a guard for three and a half seconds,
        /// which a player meets as four fed,
        /// pulsing turrets pointed at a boss and doing nothing - reported, repeatedly, as
        /// <em>the turrets start attacking too late</em>. There is no such window now: the only
        /// thing between a fed ward and a standing boss is <c>SiegeTuning.FireEvery</c>, the
        /// cadence every ward fires everything on.
        /// </para>
        /// <para>
        /// Held on the <em>first</em> bolt rather than on the state, because a state can be true
        /// while nothing acts on it. What is asserted is that the boss has lost health inside one
        /// firing cadence of planting, from a line that had fuel and nothing else to shoot at.
        /// </para>
        /// </summary>
        [Test]
        public void TheLineFiresOnTheFrameABossPlants()
        {
            foreach (var kind in EveryBoss())
            {
                var board = SiegeBoard.Build(DuelWith(kind));
                SiegeRaider boss = null;

                // Fed every frame, so the only thing that can stop a bolt is a rule.
                for (int i = 0; i < 60 * 60 && (boss == null || !boss.InPlace); i++)
                {
                    for (int w = 0; w < board.Wards.Count; w++)
                        board.Wards[w].Fuel = board.Wards[w].Capacity;

                    board.Advance(1f / 60f);
                    boss = board.Warlord;
                }

                Assert.IsNotNull(boss, $"{kind} never walked on");
                Assert.IsTrue(boss.InPlace, $"{kind} never reached its ground");
                Assert.IsFalse(boss.Impervious,
                               $"{kind} was still untouchable on the frame it planted");

                // One firing cadence, and a frame's slack for the plant landing mid-cooldown.
                int frames = (int)((SiegeTuning.FireEvery + 1f / 30f) * 60f) + 1;
                int landed = 0;

                for (int i = 0; i < frames; i++)
                {
                    for (int w = 0; w < board.Wards.Count; w++)
                        board.Wards[w].Fuel = board.Wards[w].Capacity;

                    foreach (var bolt in board.Advance(1f / 60f).Bolts)
                        if (bolt.Raider == boss.Id && bolt.Damage > 0) landed++;
                }

                Assert.Greater(landed, 0,
                    $"{kind} took nothing in the {SiegeTuning.FireEvery:0.00}s after it planted, "
                    + "so the line is waiting on something the player cannot see");
            }
        }

        /// <summary>
        /// <b>The overcharge is offered exactly when it lands, and this is the fixture the
        /// owner's second report is written into.</b>
        ///
        /// <para>
        /// Reported as <em>sometimes I can use it and sometimes I cannot</em>, which was the
        /// truth: the control was drawn from the tube's own readiness
        /// (<c>SiegeWard.Armed</c>) and the throw was refused by the <em>hill</em> - nothing on
        /// it this ward could hurt - so against a boss, which is the whole hill, a lit and
        /// pulsing button refused a tap for every second the boss could not be hurt. Under the
        /// guard that was three to four seconds out of every stand.
        /// </para>
        /// <para>
        /// Both halves are <c>SiegeBoard.CanOvercharge</c> now. What is held here is that the two
        /// are the same answer on every frame of a whole duel - offered and it lands, not offered
        /// and the charge is still there - so the control can never lie in either direction.
        /// </para>
        /// </summary>
        [Test]
        public void AnOverchargeIsOfferedExactlyWhenItWouldLand()
        {
            foreach (var kind in EveryBoss())
            {
                var board = SiegeBoard.Build(DuelWith(kind));
                int offered = 0, withheld = 0;

                for (int i = 0; i < 60 * 45; i++)
                {
                    var boss = board.Warlord;
                    if (boss == null || !boss.Alive) { board.Advance(1f / 60f); continue; }

                    Bank(board);

                    for (int w = 0; w < board.Wards.Count; w++)
                    {
                        bool lit = board.CanOvercharge(w);
                        int held = board.Wards[w].Charges;

                        bool landed = board.Overcharge(w, null).Landed;

                        if (lit)
                        {
                            offered++;
                            Assert.IsTrue(landed,
                                $"{kind}: the tube on ward {w} was offered and the tap did nothing");
                            Assert.AreEqual(held - 1, board.Wards[w].Charges,
                                $"{kind}: a charge that landed was not spent");
                        }
                        else
                        {
                            withheld++;
                            Assert.IsFalse(landed,
                                $"{kind}: the tube on ward {w} was not offered and the tap landed");
                            Assert.AreEqual(held, board.Wards[w].Charges,
                                $"{kind}: a refused tap took the charge anyway");
                        }
                    }

                    board.Advance(1f / 60f);
                }

                // Both readings have to be reachable, or the fixture is agreeing with itself: the
                // walk in is where a duel withholds, and everything after it is where it offers.
                Assert.Greater(offered, 0, $"{kind}: the tube was never offered at all");
                Assert.Greater(withheld, 0, $"{kind}: the tube was never withheld, so the walk in "
                                            + "is no longer untouchable");
            }
        }

        /// <summary>
        /// <b>A planted boss takes everything, at once, and this is the fixture the owner's
        /// ruling is written into</b> (2026-10-05: <em>let me attack the boss properly, do not
        /// stop me with anything</em>).
        ///
        /// <para>
        /// A stand used to be a floor: the bar rested on its notch until the stand had thrown
        /// its spell and held three seconds, so eight banked charges went one stand at a time
        /// and whatever crossed a notch was thrown away. Held here is that nothing of that is
        /// left - every charge the line holds lands on the frame it is thrown, each for
        /// everything it is worth, and a blow big enough ends the boss on the spot.
        /// </para>
        /// </summary>
        [Test]
        public void APlantedBossTakesEverythingAtOnce()
        {
            foreach (var kind in EveryBoss())
            {
                // Every banked charge, thrown in one frame: each lands whole.
                {
                    var board = SiegeBoard.Build(DuelWith(kind));
                    var boss = Standing(board);
                    Bank(board);

                    int thrown = 0;

                    for (int w = 0; w < board.Wards.Count && boss.Alive; w++)
                        while (boss.Alive && board.Wards[w].Charges > 0)
                        {
                            Assert.IsTrue(board.CanOvercharge(w),
                                $"{kind}: charge {thrown + 1} was refused against a planted boss");

                            int before = boss.Health;
                            var hits = new List<SiegeStrike>();
                            var blast = board.Overcharge(w, hits);

                            Assert.IsTrue(blast.Landed, $"{kind}: charge {thrown + 1} did not land");

                            int took = 0;
                            foreach (var hit in hits) if (hit.Raider == boss.Id) took += hit.Damage;

                            Assert.AreEqual(before - boss.Health, took);
                            Assert.AreEqual(System.Math.Min(blast.Damage, before), took,
                                $"{kind}: charge {thrown + 1} was worth {blast.Damage} and took "
                                + $"{took} of a boss holding {before}, so something held it back");

                            thrown++;
                        }

                    Assert.Greater(thrown, 1, $"{kind}: the fixture threw one charge and proved nothing");
                }

                // One blow worth the whole bar: the boss falls to it, from its first stand.
                {
                    var board = SiegeBoard.Build(DuelWith(kind));
                    var boss = Standing(board);

                    Assert.AreEqual(0, boss.Phase);
                    board.Storm(boss.MaxHealth * 10, null);

                    Assert.IsFalse(boss.Alive, $"{kind} survived a blow worth ten of it");
                }

                // And a boss is never anything but hurtable once it stands.
                {
                    var board = SiegeBoard.Build(DuelWith(kind));
                    var boss = Standing(board);

                    for (int i = 0; i < 60 * 30 && boss.Alive; i++)
                    {
                        for (int w = 0; w < board.Wards.Count; w++)
                            board.Wards[w].Fuel = board.Wards[w].Capacity;

                        board.Advance(1f / 60f);

                        if (boss.Alive)
                            Assert.IsFalse(boss.Impervious, $"{kind} could not be hurt while standing");
                    }
                }
            }
        }

        /// <summary>
        /// A stand opens when its third has been taken, and a blow through two thirds opens the
        /// last one directly: the stands are where a boss's health is, not a sequence it is owed.
        /// </summary>
        [Test]
        public void AStandOpensWhenItsThirdIsTaken()
        {
            foreach (var kind in EveryBoss())
            {
                var board = SiegeBoard.Build(DuelWith(kind));
                var boss = Standing(board);

                boss.Health = SiegeTuning.PhaseFloor(boss.MaxHealth, 0) + 1;
                board.Advance(1f / 600f);
                if (!boss.Alive) continue;
                Assert.AreEqual(0, boss.Phase, $"{kind} turned a stand above its threshold");

                boss.Health = SiegeTuning.PhaseFloor(boss.MaxHealth, 0);
                board.Advance(1f / 600f);
                Assert.AreEqual(1, boss.Phase, $"{kind} did not open its second stand at the notch");

                boss.Health = 1;
                board.Advance(1f / 600f);
                Assert.AreEqual(SiegeTuning.BossPhases - 1, boss.Phase,
                                $"{kind} did not open its last stand");
            }

            // Two thirds in one blow, from the first stand.
            {
                var board = SiegeBoard.Build(DuelWith(SiegeKind.Boss));
                var boss = Standing(board);

                boss.Health = 1;
                board.Advance(1f / 600f);

                Assert.AreEqual(SiegeTuning.BossPhases - 1, boss.Phase,
                                "a blow through two thirds left the boss in a stand it had passed");
            }
        }

        /// <summary>A stunned boss does not cast: a stun is seconds off the fight's spells.</summary>
        [Test]
        public void AStunnedBossDoesNotCast()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Boss));
            var boss = Standing(board);
            int casts = boss.Casts;

            for (int i = 0; i < 60 * 6; i++)
            {
                boss.Stun = 1f;
                boss.Steady = 0f;
                board.Advance(1f / 60f);
            }

            Assert.AreEqual(casts, boss.Casts, "a boss cast through a stun");
        }

        // ------------------------------------------------------------------ played
        /// <summary>
        /// Every boss, alone on its hill against an unhurried player who dumps every charge the
        /// moment it banks: the fight <em>ends</em>, one way or the other, and the table says
        /// how long each stood and how often it cast.
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
            }

            Assert.IsEmpty(faults, string.Join("\n", faults) + "\n\nthe duels read:\n" + table);
        }

        /// <summary>
        /// The gate: every shipped rung that sends a boss sends the bosses it says, and each is
        /// reached and felled at some rhythm on the strongest line. <b>It no longer holds a boss
        /// to a least number of spells or seconds</b> - that promise was the stand's floor, and
        /// the floor is gone (2026-10-05). What it prints is the reading a health re-tune wants:
        /// how long each boss stood and how often it cast before it fell.
        /// </summary>
        [Test]
        public void EveryShippedBossRungIsAFight()
        {
            float[] rhythms = { 2.20f, 2.25f, 2.30f, 2.35f, 2.40f, 2.45f, 2.50f, 2.55f, 2.60f };

            var table = new StringBuilder();
            var faults = new List<string>();
            // Boss rungs the player model never reached, even on four of the strongest
            // turrets on the shelf. **Four, measured on 2026-09-20** - `s04_hollowgrave`'s
            // gravemaw, `s06_cragheart`'s colossus, `s07_gorgongate`'s gorgon and
            // `s08_harrowgate`'s harrower - so a fifth is a regression and not a note.
            //
            // **The figure was guessed at two first and the gate is what corrected it**, which
            // is the reason it counts rather than exempting a named list: a list is written
            // from the failures somebody happened to see, and this gate stops at the first.
            //
            // **Nought since 2026-09-27**, when `Strongest` became four three-star pyres - the line
            // the balance run measured as strongest - and every boss rung in the game, the four
            // listed above included, was reached and fought on it. The count stays a count, so one
            // rung falling out of reach again is red rather than a note.
            const int Unreachable = 0;
            var unreached = new List<string>();

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
                        // **On the strongest line rather than on the one the chapter
                        // expects, because this gate is about the boss and not about the
                        // economy.** What it measures is whether a boss stands, casts and
                        // is hurtable before it falls; a boss that is never reached is not
                        // a fight that failed, it is a fight nobody saw. Since the refill
                        // stopped dealing free chains (37el) two finales cannot be won on
                        // any line a player can reach, so measuring here on one would
                        // report the economy under the name of the fight. Whether a rung
                        // is winnable at all is the chapter gates' question, and they
                        // carry the accepted walls.
                        var board = SiegeBoard.Build(layout, Strongest());

                        // **Every boss that walks on, by id**, because a duel sends two
                        // (`SiegeLayout.BossJoin`) and a gate that watched `Warlord` - the first
                        // one standing - would have measured whichever of the pair happened to
                        // outlive the other and called that the fight.
                        var bosses = new Dictionary<int, SiegeRaider>();

                        Hold(board, rhythm, out int _, b =>
                        {
                            foreach (var raider in b.Raiders)
                                if (raider.Boss) bosses[raider.Id] = raider;
                        });

                        // A boss waits for the hill to be cleared (37dn), so a line that fell to
                        // the wave before it never meets the boss at all - a lost run, and not a
                        // reading of the fight.
                        if (bosses.Count == 0 && board.WardsStanding == 0) continue;

                        Assert.AreEqual(layout.BossKinds.Length, bosses.Count,
                                        $"{rung.Id}: {bosses.Count} boss(es) walked on at {rhythm}, "
                                        + $"against the {layout.BossKinds.Length} the rung sends");

                        bool all = true;
                        foreach (var boss in bosses.Values) all &= !boss.Alive;

                        if (all) fell++;
                        if (board.IsFinished && board.WardsStanding >= 2) held++;

                        // **A run the line lost is not a reading of the fight.** The boss stands
                        // over a dead line with nothing to aim at and nothing shooting back, so
                        // its casts and its seconds say nothing about it; whether a rung is lost
                        // too often is the chapter sweep's question, not this one's. Asked per
                        // boss: the half of a duel that fell is a reading even when its partner
                        // outlived the line.
                        foreach (var boss in bosses.Values)
                        {
                            if (boss.Alive) continue;

                            if (boss.Casts < leastCasts) leastCasts = boss.Casts;
                            if (boss.Casts > mostCasts) mostCasts = boss.Casts;
                            if (boss.Stood < leastStood) leastStood = boss.Stood;
                            if (boss.Stood > mostStood) mostStood = boss.Stood;
                        }
                    }

                    // **A boss nobody reaches is unmeasured rather than a fight that
                    // failed, and that distinction is new.** This gate asks what a boss does
                    // on its way down - how long it stands, how often it casts. A rung the
                    // player model cannot win produces no answer to that at all, not a bad
                    // one, and failing here would report the economy a second time under the
                    // name of the fight. Whether a rung is winnable is the chapter gates'
                    // question, and they carry the walls the owner accepted on 2026-09-20.
                    //
                    // **Counted rather than waved through**, because a gate that is silent
                    // about most of its subject is not a gate: one more boss out of reach
                    // fails this even while every boss it can still see behaves.
                    if (fell == 0) unreached.Add($"{rung.Id} ({rung.Boss})");


                    table.AppendLine($"  {name,-11} {rung.Id,-20} {rung.Boss,-24}"
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

            if (unreached.Count > Unreachable)
                faults.Add($"{unreached.Count} boss rung(s) were never reached on the "
                           + $"strongest line, against the {Unreachable} measured: "
                           + string.Join(", ", unreached.ToArray()));

            if (unreached.Count > 0)
                table.AppendLine("  never reached: "
                                 + string.Join(", ", unreached.ToArray()));

            Assert.IsEmpty(faults, string.Join("\n", faults) + "\n\nthe boss rungs read:\n" + table);

            // On the console rather than through `TestContext`, which the offline runner does
            // not stand up - and this table is the one reading of the fight a re-tune wants
            // to see when everything passes.
            System.Console.WriteLine("the boss rungs read:\n" + table);
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
                               + $" place={boss.InPlace} stand={boss.InPhase:0.0} phase={boss.Phase}"
                               + $" hp={boss.Health}/{boss.MaxHealth} casts={boss.Casts} stood={boss.Stood:0.0}");
                log.AppendLine();
            });

            return log.ToString();
        }

        // ------------------------------------------------------------------ what the drawing reads
        /// <summary>
        /// <b>A tap is kept only for a boss that cannot take it yet</b>
        /// (<c>SiegeBoard.CanHold</c>, <c>SiegeView.Keeping</c>), and the three answers a tube
        /// can give - thrown, kept, refused - never overlap.
        ///
        /// <para>
        /// Walked over a whole duel with every charge banked and none thrown: the walk in is
        /// the one place a tap is kept, and everywhere a tap is kept the throw itself is still
        /// refused with the charge intact - which is the proof that keeping a tap spends
        /// nothing until the board says it lands.
        /// </para>
        /// </summary>
        [Test]
        public void ATapIsKeptOnlyForABossThatCannotTakeItYet()
        {
            foreach (var kind in EveryBoss())
            {
                var board = SiegeBoard.Build(DuelWith(kind));
                int kept = 0, live = 0;

                // Before anything has mustered: a charge, an empty hill, and no boss to wait for.
                Bank(board);
                for (int w = 0; w < board.Wards.Count; w++)
                {
                    Assert.IsTrue(board.Charged(w), $"{kind}: a banked charge was not drawn");
                    Assert.IsFalse(board.CanOvercharge(w));
                    Assert.IsFalse(board.CanHold(w),
                        $"{kind}: a tap over an empty hill was kept for whatever walks on next");
                }

                for (int i = 0; i < 60 * 45; i++)
                {
                    Bank(board);
                    board.Advance(1f / 60f);

                    var boss = board.Warlord;

                    for (int w = 0; w < board.Wards.Count; w++)
                    {
                        bool thrown = board.CanOvercharge(w), held = board.CanHold(w);

                        Assert.IsFalse(thrown && held,
                            $"{kind}: ward {w} would both throw a tap and keep it");

                        if (thrown) live++;
                        if (!held) continue;

                        kept++;

                        Assert.IsTrue(board.Charged(w));
                        Assert.IsNotNull(boss, $"{kind}: a tap was kept with no boss on the hill");
                        Assert.IsTrue(boss.Alive && boss.Impervious,
                            $"{kind}: a tap was kept against a boss that could take it");

                        int charges = board.Wards[w].Charges;
                        Assert.IsFalse(board.Overcharge(w, null).Landed,
                            $"{kind}: a kept tap landed");
                        Assert.AreEqual(charges, board.Wards[w].Charges,
                            $"{kind}: keeping a tap spent the charge");
                    }
                }

                Assert.Greater(kept, 0, $"{kind}: no tap was ever kept, so the walk in refuses them");
                Assert.Greater(live, 0, $"{kind}: no tap would ever have thrown");
            }
        }

        /// <summary>
        /// <b>Asking the board what to draw moves nothing on it.</b>
        ///
        /// <para>
        /// The overcharge key's readings are asked every frame by a view, so they have to be
        /// pure. Two boards are played through the same
        /// duel with the same hands, one of them asked every reading for every ward on every
        /// frame, and held equal on everything a run is decided by.
        /// </para>
        /// </summary>
        [Test]
        public void ReadingTheBoardForTheDrawingMovesNothing()
        {
            foreach (var kind in EveryBoss())
            {
                var plain = SiegeBoard.Build(DuelWith(kind));
                var read = SiegeBoard.Build(DuelWith(kind));

                for (int i = 0; i < 60 * 60; i++)
                {
                    foreach (var board in new[] { plain, read })
                    {
                        // Fed hard, and a charge thrown twice a second.
                        Bank(board);
                        if (i % 30 == 0) board.Overcharge(i / 30 % board.Wards.Count, null);
                    }

                    for (int w = -1; w <= read.Wards.Count; w++)
                    {
                        read.Charged(w);
                        read.CanHold(w);
                        read.CanOvercharge(w);
                    }

                    var a = plain.Advance(1f / 60f);
                    int bolts = a.Bolts.Count, spells = a.Spells.Count, casts = a.Casts.Count;

                    var b = read.Advance(1f / 60f);

                    Assert.AreEqual(bolts, b.Bolts.Count, $"{kind}: frame {i} fired differently");
                    Assert.AreEqual(spells, b.Spells.Count, $"{kind}: frame {i} was struck differently");
                    Assert.AreEqual(casts, b.Casts.Count, $"{kind}: frame {i} cast differently");
                    Assert.AreEqual(plain.Raiders.Count, read.Raiders.Count, $"{kind}: frame {i}");

                    for (int r = 0; r < plain.Raiders.Count; r++)
                    {
                        Assert.AreEqual(plain.Raiders[r].Health, read.Raiders[r].Health,
                                        $"{kind}: frame {i}, a raider's health moved");
                        Assert.AreEqual(plain.Raiders[r].Phase, read.Raiders[r].Phase,
                                        $"{kind}: frame {i}, a stand turned on a different frame");
                        Assert.AreEqual(plain.Raiders[r].March, read.Raiders[r].March,
                                        $"{kind}: frame {i}, a raider stood somewhere else");
                    }

                    for (int w = 0; w < plain.Wards.Count; w++)
                    {
                        Assert.AreEqual(plain.Wards[w].Fuel, read.Wards[w].Fuel,
                                        $"{kind}: frame {i}, ward {w}'s fuel moved");
                        Assert.AreEqual(plain.Wards[w].Charges, read.Wards[w].Charges,
                                        $"{kind}: frame {i}, ward {w}'s charges moved");
                        Assert.AreEqual(plain.Wards[w].Health, read.Wards[w].Health,
                                        $"{kind}: frame {i}, ward {w}'s health moved");
                        Assert.AreEqual(plain.Wards[w].Shots, read.Wards[w].Shots,
                                        $"{kind}: frame {i}, ward {w} fired a different number");
                    }
                }
            }
        }

        /// <summary>
        /// Every chapter's rung table, by name, so a fixture can walk the mode rather than one
        /// chapter.
        ///
        /// <b>A chapter is added here in the same change that ships it</b>, which is the whole of
        /// what a new chapter costs this gate - and Dustcrown proved the cost of forgetting, by
        /// shipping six rungs with two bosses on them that no fixture in the mode ever fought.
        /// </summary>
        static IEnumerable<(string Name, Rung[] Rungs)> ShippedChapters()
        {
            yield return ("thornwatch", Chapter);
            yield return ("broodmarch", Broodmarch);
            yield return ("barrowfell", Barrowfell);
            yield return ("ashenhold", Ashenhold);
            yield return ("thundercrag", Thundercrag);
            yield return ("dustcrown", Dustcrown);
            yield return ("bonereach", Bonereach);
            yield return ("cloudkeep", Cloudkeep);
            yield return ("cogspire", Cogspire);
            yield return ("windwreck", Windwreck);
            yield return ("neonhaven", Neonhaven);
        }
    }
}
