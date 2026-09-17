using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The colour lock and everything that follows from it.
    ///
    /// <para>
    /// <b>A file of its own rather than more of <c>SiegeRuleTests</c></b>, because these are one
    /// idea: a turret fires only at its own colour, and every other rule here exists because of
    /// that. The banking, the breather, the forecast, the overcharge and the bulwark's new armour
    /// are all consequences, and reading them together is what makes any of them make sense.
    /// </para>
    /// </summary>
    public sealed class SiegeLockTests
    {
        static readonly string[] Field =
        {
            "rgrgyrry",
            "bgygybbg",
            "grrbbgyr",
            "bybbgryg",
            "ryygybrb",
        };

        static SiegeLayout Layout(string[] waves, string gems = "rgby", string wards = "rgby",
                                  string boss = null, int cogs = 0)
        {
            Assert.IsTrue(ProtoGrid.TryRead(Field, Field[0].Length, Field.Length,
                                            SiegeLayout.Cells, out var grid, out string error),
                          error);

            var layout = new SiegeLayout(grid, gems, wards, waves, boss, cogs);
            Assert.IsNull(layout.Fault, layout.Fault);
            return layout;
        }

        /// <summary>
        /// A board's own layout, held in a local at every call site.
        ///
        /// <b>A helper rather than <c>board.Layout.</c> inline</b>: the offline compile refuses
        /// that shape anywhere, because <c>LevelDefinition.Layout</c> is null on any level that is
        /// not a glade and the check is deliberately coarse about which <c>Layout</c> it sees.
        /// </summary>
        static SiegeLayout Plan(SiegeBoard board) => board.Layout;

        static void Frames(SiegeBoard board, float seconds)
        {
            for (int i = 0; i < (int)(seconds * 60f); i++) board.Advance(1f / 60f);
        }

        /// <summary>Pours fuel into one ward without going through the field.</summary>
        static void Feed(SiegeBoard board, int ward, float fuel)
        {
            board.Wards[ward].Fuel = fuel > board.Wards[ward].Capacity
                                   ? board.Wards[ward].Capacity
                                   : fuel;
        }

        // ------------------------------------------------------------------ the lock
        /// <summary>
        /// <b>A turret only ever fires at raiders of its own colour, and that one rule is the
        /// mode.</b>
        ///
        /// It used to prefer its own colour and fall back to whatever was nearest, which meant the
        /// double was a bonus the player received for free — four wards firing at once landed
        /// everything on its own kind whatever anybody matched, so which colour to feed decided
        /// nothing and "take the biggest match" was correctly the optimal play.
        /// </summary>
        [Test]
        public void AWardNeverFiresAtAColourItIsNotStrongAgainst()
        {
            var board = SiegeBoard.Build(Layout(new[] { "rrrr" }));

            int red = Plan(board).WardOf('r');
            int blue = Plan(board).WardOf('b');

            Feed(board, red, 20f);
            Feed(board, blue, 20f);

            Frames(board, SiegeTuning.FirstWaveAfter + 6f);

            Assert.Less(board.Wards[red].Fuel, 20f, "the red ward never fired at a red hill");
            Assert.AreEqual(20f, board.Wards[blue].Fuel, .0001f,
                            "the blue ward spent fuel on a hill with nothing blue on it");
        }

        /// <summary>
        /// <b>And that is what makes fuel a resource rather than a pass-through.</b> A ward with
        /// nothing to fire at holds what it is given, which is the half of the loop this mode
        /// never had and the half a breather is worth having for.
        /// </summary>
        [Test]
        public void AWardWithNothingToShootAtBanksWhatItIsGiven()
        {
            var board = SiegeBoard.Build(Layout(new[] { "rrrr" }));
            int blue = Plan(board).WardOf('b');

            Feed(board, blue, 1f);
            Frames(board, 2f);

            Feed(board, blue, board.Wards[blue].Fuel + 6f);
            Frames(board, SiegeTuning.FirstWaveAfter + 8f);

            Assert.AreEqual(7f, board.Wards[blue].Fuel, .0001f, "banked fuel leaked away");
            Assert.IsTrue(board.Wards[blue].Fuelled, "a banked ward reads as unfuelled");
        }

        /// <summary>
        /// Every bolt an ordinary turret lands is an own-colour hit, so <c>PerfectMatch</c> — which
        /// has always assumed exactly that of every gem — stops being an optimistic reading and
        /// becomes an identity.
        ///
        /// <b>A boss is the one thing on the hill this is not true of</b>, which is why there is no
        /// boss in this fixture: every ward answers one and only its own colour doubles — see
        /// <see cref="EveryWardOnTheLineAnswersABossWhateverColourItWears"/>. The identity survives
        /// it, because a part-weight bolt costs a part of the fuel.
        /// </summary>
        [Test]
        public void EveryOrdinaryBoltLandsAtFullWeight()
        {
            var board = SiegeBoard.Build(Layout(new[] { "rgby" }));

            for (int w = 0; w < board.Wards.Count; w++) Feed(board, w, 20f);

            var seen = new List<SiegeBolt>();

            for (int i = 0; i < 60 * 12; i++)
                seen.AddRange(board.Advance(1f / 60f).Bolts);

            Assert.IsNotEmpty(seen, "nothing fired");

            foreach (var bolt in seen)
            {
                if (bolt.Extra) continue;      // splash and chain spill onto anything, by design

                Assert.IsTrue(bolt.Weak,
                              "an ordinary bolt landed on something its ward is not strong against");
            }
        }

        // ------------------------------------------------------------------ the stun
        /// <summary>
        /// <b>No turret on the shelf reaches a colour that is not its own — unless it is a
        /// legendary, in which case it reaches every one of them.</b>
        ///
        /// <para>
        /// The roster carried an ability that widened the lock — a prism, on both its rungs — and
        /// under the lock what it bought was the moments its own colour happened to be clear,
        /// which the seat beside it was already answering at full weight. Withdrawn on the
        /// owner's reading (invariant 5d, asked of a purchase), and this is what stops it coming
        /// back by accident.
        /// </para>
        /// <para>
        /// <b>The legendary band is the one exception and it is asked for by name, which is the
        /// whole point of writing it this way.</b> It is a property of the <em>model</em>
        /// (<c>WardModel.Legendary</c>) and never of an ability, so the two halves are one
        /// assertion over the whole roster: a new ability that quietly reached a second colour
        /// would fail the first branch, and a legendary that quietly stopped reaching all four
        /// would fail the second. Either alone would be a rule nothing else in this file could
        /// see.
        /// </para>
        /// </summary>
        [Test]
        public void NoTurretOnTheShelfReachesASecondColour()
        {
            int legends = 0;

            foreach (var model in WardCatalog.Default.Models)
            {
                var ward = new SiegeWard(0, model);

                Assert.AreEqual(10, ward.ReachTenths(0), model.Id);

                for (int colour = 1; colour < SiegeLayout.Letters.Length; colour++)
                    Assert.AreEqual(model.Legendary ? 10 : 0, ward.ReachTenths(colour),
                                    model.Legendary
                                    ? $"legendary '{model.Id}' does not answer every colour"
                                    : $"'{model.Id}' fires at a colour it was not bought for");

                if (model.Legendary) legends++;
            }

            // A band nothing stands in would make the clause above vacuous, which is the shape
            // invariant 5d refuses: a check that cannot fail is not a check.
            Assert.Greater(legends, 0, "no turret in the roster is legendary");
        }

        /// <summary>
        /// <b>A legendary shoots a raider of a colour no other seat could have answered.</b>
        ///
        /// <para>
        /// Asked of a played board rather than of <c>SiegeWard.ReachTenths</c>, because the lock
        /// is spelled in two places and only one of them is that predicate: <c>SiegeBoard.Aim</c>
        /// picks what a ward fires at, and a ward that reached every colour but aimed at one
        /// would bank its fuel over a full hill and read as a turret that does not work. The two
        /// were separate lines when this was written, so this is what holds them together.
        /// </para>
        /// <para>
        /// The line stands the legendary on the <em>red</em> seat and the hill sends nothing red,
        /// so every bolt it lands is one an ordinary turret on that seat could not have fired —
        /// which is also the arithmetic that keeps the band out of par's way (invariant 22).
        /// </para>
        /// </summary>
        [Test]
        public void ALegendaryFiresAtAColourItsSeatCouldNotHaveAnswered()
        {
            WardModel legend = null;
            foreach (var model in WardCatalog.Default.Models)
                if (model.Legendary) { legend = model; break; }

            Assert.IsNotNull(legend, "no turret in the roster is legendary");

            var line = WardLine.Resolve(WardCatalog.Default,
                                        new[] { new WardSlot('r', legend.Id) }, (_, __) => true);

            // A hill wearing nothing but green, so every bolt the *red* seat lands is one the
            // colour lock would have refused.
            var board = SiegeBoard.Build(Layout(new[] { "gggg" }), line);
            int red = Plan(board).WardOf('r');

            Assert.IsTrue(board.Wards[red].Unbound, "the red seat is not standing the legendary");

            for (int w = 0; w < board.Wards.Count; w++) Feed(board, w, 20f);

            int fired = 0;

            for (int i = 0; i < 60 * 12; i++)
                foreach (var bolt in board.Advance(1f / 60f).Bolts)
                    if (bolt.Ward == red && !bolt.Extra) fired++;

            Assert.Greater(fired, 0,
                           $"legendary '{legend.Id}' on the red seat never fired at a green hill");
        }

        /// <summary>
        /// <b>A stun takes a raider out of the raid: it does not walk, it does not swing and it
        /// does not cast — and then it walks again.</b>
        ///
        /// Stopping the march alone would be a stun worth nothing against the half of the hill it
        /// matters most against — a raider already at the line, where a second of quiet is a blow
        /// the line did not take. <b>The second half is driven through the board</b>, because the
        /// countdown lives in <c>SiegeBoard.Smoulder</c> with the chill and the burn, and a stun
        /// that was applied and never aged would read exactly like one that worked.
        /// </summary>
        [Test]
        public void AStunStopsARaiderAndThenLetsItWalkAgain()
        {
            var raider = new SiegeRaider(1, 0, SiegeKind.Creeper, 0, 0f);

            Assert.AreEqual(1f, raider.Pace, .0001f, "an unhurt raider walks at its own pace");

            raider.Stagger(1f);

            Assert.IsTrue(raider.Stunned);
            Assert.AreEqual(0f, raider.Pace, .0001f, "a stunned raider is still walking");

            // A chill under a stun is the stun, not the sum: being stopped is not a rate.
            raider.Freeze(4, 2f);
            Assert.AreEqual(0f, raider.Pace, .0001f);

            // **No fuel is poured in**, so the line never fires and the raider under test lives
            // for the length of the measurement.
            var board = SiegeBoard.Build(Layout(new[] { "r" }));

            Frames(board, SiegeTuning.FirstWaveAfter + 1f);

            var walking = board.Raiders[0];

            Assert.IsTrue(walking.OnTheHill, "the wave never came out");
            Assert.Greater(walking.March, 0f, "the raider never started walking");

            float held = walking.March;

            walking.Stagger(.5f);
            Frames(board, .4f);

            Assert.AreEqual(held, walking.March, .0001f, "a stunned raider walked on");

            Frames(board, .6f);

            Assert.Greater(walking.March, held, "a stun never ran out");
        }

        /// <summary>
        /// <b>A stun can never hold a raider in place, and that bound is what makes the ability
        /// safe to sell.</b>
        ///
        /// <para>
        /// A fuelled ward gets a bolt away every <c>SiegeTuning.FireEvery</c> seconds, which is
        /// shorter than either stun on the shelf — so a stun that refreshed the way a chill and a
        /// burn do would stop its own colour for the whole run, and a raid that cannot reach the
        /// line is a fail state that rejects nothing (invariant 5d, from the other side).
        /// </para>
        /// <para>
        /// <b>Played rather than reasoned about</b>: a stun turret is stood on red, the tube is
        /// kept full, and the hill is asked how far it got. The share is what a player feels, and
        /// it is the arithmetic the family's ladder rests on.
        /// </para>
        /// </summary>
        [Test]
        public void AStunCanNeverHoldTheHillWhereItStands()
        {
            var stunner = WardCatalog.Default.Find("spectrum");

            Assert.IsNotNull(stunner);
            Assert.AreEqual(WardAbility.Stun, stunner.Ability);

            var line = WardLine.Resolve(WardCatalog.Default,
                                        new[] { new WardSlot('r', stunner.Id) }, (_, __) => true);

            // A brute, so it survives long enough to be measured, and a plain line for the other
            // seats - nothing but the red ward is fed, so nothing but the red ward fires.
            var board = SiegeBoard.Build(Layout(new[] { "R" }), line);
            int red = Plan(board).WardOf('r');

            Assert.AreEqual(WardAbility.Stun, board.Wards[red].Ability);

            Frames(board, SiegeTuning.FirstWaveAfter + .5f);

            var brute = board.Raiders[0];
            Assert.IsTrue(brute.OnTheHill, "the wave never came out");

            int frames = 0, stunned = 0;

            for (int i = 0; i < 60 * 6; i++)
            {
                // Topped up every frame, so this is the most stunning the mode can ever do.
                Feed(board, red, board.Wards[red].Capacity);
                board.Advance(1f / 60f);

                if (!brute.Alive) break;

                frames++;
                if (brute.Stunned) stunned++;
            }

            Assert.Greater(frames, 60, "the brute died before the measurement meant anything");
            Assert.Greater(stunned, 0, "the stun turret never stunned anything");

            float share = stunned / (float)frames;
            float most = stunner.Extent / 10f / (stunner.Extent / 10f + SiegeTuning.StunRest);

            Assert.LessOrEqual(share, most + .05f,
                               $"a stun held {share:P0} of the clock against a ceiling of "
                               + $"{most:P0} - a raid that cannot reach the line is a fail state "
                               + "that rejects nothing");

            Assert.Greater(brute.March, 0f, "the brute never moved at all");
        }

        /// <summary>
        /// <b>The two stun rungs differ in the number the ability actually reads.</b>
        ///
        /// A magnitude nobody reads is what made a thousand-gem breaker exactly a
        /// four-thousand-credit cleaver, and a stun reads its <c>Extent</c> alone — so a shelf
        /// whose two stun rungs differed in magnitude would pass every gate and sell one turret at
        /// two prices (invariant 5d, on the one thing a player pays for).
        /// </summary>
        [Test]
        public void TheDearerStunHoldsForLonger()
        {
            var cheap = WardCatalog.Default.Find("prism");
            var dear = WardCatalog.Default.Find("spectrum");

            Assert.IsNotNull(cheap, "the roster no longer carries the earned stun rung");
            Assert.IsNotNull(dear, "the roster no longer carries the bought stun rung");

            Assert.AreEqual(WardAbility.Stun, cheap.Ability);
            Assert.AreEqual(WardAbility.Stun, dear.Ability);

            Assert.Greater(cheap.Extent, 0, "a stun with no seconds stops nothing");
            Assert.Greater(dear.Extent, cheap.Extent,
                           "the dearer stun holds no longer than the one below it");

            Assert.Less(cheap.Order, dear.Order, "the cheaper rung sits above the dearer one");
        }

        // ------------------------------------------------------------------ the breather
        /// <summary>
        /// <b>A cleared hill buys a breather rather than the next wave.</b> The muster used to fire
        /// the instant nothing was left walking, which rewarded playing well with more pressure and
        /// left the run with no moment in which anything could be planned.
        /// </summary>
        [Test]
        public void AClearedHillWaitsTheBreatherAndNoLonger()
        {
            var board = SiegeBoard.Build(Layout(new[] { "r", "rrrr" }));

            Frames(board, SiegeTuning.FirstWaveAfter + .5f);
            Assert.AreEqual(1, board.Wave, "the first wave never came out");

            // Clear it outright, then watch the clock.
            var raider = board.Raiders[0];
            board.Blast(raider.Lane, SiegeTuning.RowOf(raider.March), 9999, null);

            Assert.IsTrue(board.Resting, "a cleared hill with a wave still to come is a breather");

            // One step, because the clamp lives in `Muster` and nothing has stepped since the
            // hill was cleared - which is the honest shape: the board notices on its own clock.
            board.Advance(1f / 60f);

            Assert.LessOrEqual(board.Rest, SiegeTuning.Breather + .05f,
                               "clearing the hill did not shorten the quiet");

            Frames(board, SiegeTuning.Breather * .5f);
            Assert.AreEqual(1, board.Wave, "the next wave came out during the breather");

            Frames(board, SiegeTuning.Breather * .6f);
            Assert.AreEqual(2, board.Wave, "the next wave never came out after the breather");
        }

        /// <summary>
        /// <b>It only ever shortens a quiet</b>, so the clock still never lets up and a hill that
        /// still holds something musters on its own schedule.
        /// </summary>
        [Test]
        public void TheBreatherNeverLengthensAQuiet()
        {
            Assert.Less(SiegeTuning.Breather, SiegeTuning.BetweenWaves,
                        "a breather longer than the wave clock would be a hill that waits");

            var board = SiegeBoard.Build(Layout(new[] { "rrrr", "rrrr" }));

            Frames(board, SiegeTuning.FirstWaveAfter + SiegeTuning.Breather + 1f);

            Assert.AreEqual(1, board.Wave,
                            "a hill that still holds raiders mustered on the breather");
            Assert.IsFalse(board.Resting, "a hill with raiders on it is not a breather");
        }

        /// <summary>What the forecast says is what the muster will really send.</summary>
        [Test]
        public void TheForecastCountsTheWaveTheMusterWillSend()
        {
            var layout = Layout(new[] { "r", "rrgbY" });

            var coming = SiegeForecast.Of(layout, 1);

            Assert.IsTrue(coming.Any);
            Assert.AreEqual(2, coming.R);
            Assert.AreEqual(1, coming.G);
            Assert.AreEqual(1, coming.B);
            Assert.AreEqual(1, coming.Y);
            Assert.AreEqual(5, coming.Count);
            Assert.AreEqual(2, coming.Most);
            Assert.IsFalse(coming.HasBoss);

            Assert.IsFalse(SiegeForecast.Of(layout, 2).Any, "a wave past the last one");
        }

        /// <summary>
        /// A boss riding the head of the last authored wave is forecast where the muster puts it.
        /// </summary>
        [Test]
        public void TheForecastNamesABossInTheWaveItRidesIn()
        {
            var layout = Layout(new[] { "rrrr", "gggg" }, boss: "warlord:b");

            var coming = SiegeForecast.Of(layout, layout.BossWave);

            Assert.IsTrue(coming.HasBoss);
            Assert.AreEqual(SiegeKind.Boss, coming.Boss);
            Assert.AreEqual(1, coming.B, "the warlord itself was not counted");
        }

        // ------------------------------------------------------------------ demand
        /// <summary>
        /// Demand is counted in health rather than heads, because a brute is two matches and a
        /// creeper is one — a head count would say four creepers matter more than two brutes.
        /// </summary>
        [Test]
        public void DemandIsWhatThisWardIsTheAnswerTo()
        {
            var board = SiegeBoard.Build(Layout(new[] { "rrRg" }));

            Frames(board, SiegeTuning.FirstWaveAfter + 6f);

            int red = Plan(board).WardOf('r');
            int green = Plan(board).WardOf('g');
            int blue = Plan(board).WardOf('b');

            Assert.AreEqual(SiegeTuning.CreeperHealth * 2 + SiegeTuning.BruteHealth,
                            board.DemandOf(red));
            Assert.AreEqual(SiegeTuning.CreeperHealth, board.DemandOf(green));
            Assert.AreEqual(0, board.DemandOf(blue));

            Assert.AreEqual(board.DemandOf(red), board.Busiest);
        }

        // ------------------------------------------------------------------ the overcharge
        /// <summary>
        /// <b>Only a full tube may be spent</b>, because an overcharge spends all of it: the one
        /// moment it may be offered is the moment there is nothing more to put in.
        /// </summary>
        [Test]
        public void OnlyABrimmingTubeMayBeOvercharged()
        {
            var board = SiegeBoard.Build(Layout(new[] { "rrrr" }));
            int red = Plan(board).WardOf('r');

            Frames(board, SiegeTuning.FirstWaveAfter + 1f);

            Assert.IsFalse(board.Wards[red].Armed);
            Assert.IsFalse(board.Overcharge(red, null).Landed, "an unarmed ward was spent");

            // A whole tube poured in, which is what banks a charge.
            Assert.IsTrue(board.Wards[red].Fill(board.Wards[red].Capacity),
                          "a full tube did not bank a charge");

            Assert.IsTrue(board.Wards[red].Armed);
            Assert.AreEqual(1, board.Wards[red].Charges);
            Assert.AreEqual(0f, board.Wards[red].Fuel, .0001f,
                            "the fuel that became a charge is still in the tube, so it would be "
                            + "fired twice");

            var blast = board.Overcharge(red, null);

            Assert.IsTrue(blast.Landed);
            Assert.AreEqual(0, board.Wards[red].Charges, "the charge was not spent");
            Assert.IsFalse(board.Wards[red].Armed);
        }

        /// <summary>
        /// <b>It delivers exactly what the tube would have delivered as ordinary bolts</b>, which
        /// is what keeps it free of par: the player has moved damage they had already matched for
        /// rather than conjured any, so invariant 39's exchange rate has nothing to charge.
        /// </summary>
        [Test]
        public void AnOverchargeIsWorthTheBoltsTheTubeWasHolding()
        {
            var board = SiegeBoard.Build(Layout(new[] { "rrrr" }));
            int red = Plan(board).WardOf('r');

            Frames(board, SiegeTuning.FirstWaveAfter + 1f);

            var ward = board.Wards[red];
            ward.Fill(ward.Capacity);

            int bolts = (int)(ward.Capacity / SiegeTuning.FuelShot(ward.Rank));
            int each = SiegeTuning.DamageTo(SiegeKind.Creeper, ward.Rank, true, ward.Build);

            var blast = board.Overcharge(red, null);

            Assert.AreEqual(bolts * each, blast.Damage,
                            "an overcharge delivered more or less than the tube was holding");
        }

        /// <summary>
        /// <b>And it hits anything</b>, which is the whole of what it is for: a colour that never
        /// comes is fuel with nowhere to go, so tapping the tube is what stops a match ever being
        /// dead.
        /// </summary>
        [Test]
        public void AnOverchargeHitsAColourItsWardCouldNeverShootAt()
        {
            var board = SiegeBoard.Build(Layout(new[] { "gggg" }));
            int red = Plan(board).WardOf('r');

            Frames(board, SiegeTuning.FirstWaveAfter + 2f);
            Assert.Greater(board.OnTheHill, 0);

            board.Wards[red].Fill(board.Wards[red].Capacity);

            var hits = new List<SiegeStrike>();
            var blast = board.Overcharge(red, hits);

            Assert.IsTrue(blast.Landed);
            Assert.IsNotEmpty(hits, "a red tube thrown at a green hill reached nobody");
            Assert.Greater(blast.Absorbed, 0);
        }

        /// <summary>
        /// <b>A banked charge survives the turret firing, and that is the whole reason it is a
        /// charge rather than a full tube.</b>
        ///
        /// <para>
        /// It shipped as "the tube is full" and was <em>unusable</em>: a ward fires the instant it
        /// has fuel and a target, so the only way to reach the brim was for its colour to be off
        /// the hill — and an overcharge over an empty hill has nothing to throw at. Reported after
        /// one session as exactly that. A full tube converts into a charge now, the tube carries on
        /// filling for ordinary bolts, and the charge waits until it is thrown.
        /// </para>
        /// </summary>
        [Test]
        public void ABankedChargeSurvivesTheWardFiringItsOrdinaryBolts()
        {
            var board = SiegeBoard.Build(Layout(new[] { "rrrrrrrr" }));
            int red = Plan(board).WardOf('r');

            var ward = board.Wards[red];

            Assert.IsTrue(ward.Fill(ward.Capacity), "a full tube did not bank a charge");
            Assert.AreEqual(1, ward.Charges);

            // Now give it ordinary fuel and a hill of its own colour, and let it shoot for a while.
            ward.Fill(ward.Capacity * .5f);
            Frames(board, SiegeTuning.FirstWaveAfter + 8f);

            Assert.Less(ward.Fuel, ward.Capacity * .5f, "the ward never fired");
            Assert.AreEqual(1, ward.Charges,
                            "the banked charge was spent by the ward firing its ordinary bolts");
            Assert.IsTrue(ward.Armed);

            Assert.IsTrue(board.Overcharge(red, null).Landed,
                          "a ward that banked a charge and then fired could not spend it");
        }

        /// <summary>
        /// The fuel that becomes a charge leaves the tube, so it can never be fired twice.
        ///
        /// <b>That is what keeps the overcharge free of par</b> — the player has moved damage they
        /// already matched for rather than conjured any (invariant 39).
        /// </summary>
        [Test]
        public void BankingAChargeTakesTheFuelOutOfTheTube()
        {
            var ward = new SiegeWard(0);

            ward.Fill(ward.Capacity * 1.25f);

            Assert.AreEqual(1, ward.Charges);
            Assert.AreEqual(ward.Capacity * .25f, ward.Fuel, .001f,
                            "the overflow was dropped rather than carried");

            // And the rack is bounded: past the cap a tube clamps exactly as it always did.
            ward.Charges = SiegeTuning.MostCharges;
            ward.Fuel = 0f;

            Assert.IsFalse(ward.Fill(ward.Capacity * 3f), "a full rack banked another charge");
            Assert.AreEqual(ward.Capacity, ward.Fuel, .001f, "fuel is not a leak");
        }

        /// <summary>An empty hill has nothing to throw a tube at, so the tube is kept.</summary>
        [Test]
        public void AnOverchargeOverAnEmptyHillIsRefusedRatherThanWasted()
        {
            var board = SiegeBoard.Build(Layout(new[] { "rrrr" }));
            int red = Plan(board).WardOf('r');

            board.Wards[red].Fill(board.Wards[red].Capacity);

            Assert.AreEqual(0, board.OnTheHill);
            Assert.IsFalse(board.Overcharge(red, null).Landed);
            Assert.AreEqual(1, board.Wards[red].Charges, "a refused overcharge spent its charge");
        }

        // ------------------------------------------------------------------ plating
        /// <summary>
        /// <b>A bulwark's shield is armour against area damage now</b>, and it had to move: its
        /// rule was "only your own colour cuts me", which the lock made true of every raider — so
        /// the soak stopped being reachable on a primary hit at all and the shield was decoration
        /// on the one raider whose whole identity it was.
        /// </summary>
        [Test]
        public void PlatingBluntsWhatAWardThrowsSidewaysAndNotItsOwnBolt()
        {
            // Its own colour, in full: the primary hit goes through `DamageTo` with `weak` true,
            // which returns before the soak.
            int own = SiegeTuning.DamageTo(SiegeKind.Bulwark, 0, true);
            int plain = SiegeTuning.DamageTo(SiegeKind.Creeper, 0, true);

            Assert.AreEqual(plain, own, "a bulwark soaked the one colour that answers it");

            // And an overcharge from a ward that does not answer it is blunted. Built rather than
            // reasoned about, because the soak lives in `SiegeBoard.Through` and nowhere else.
            var board = SiegeBoard.Build(Layout(new[] { "#g" }));

            Frames(board, SiegeTuning.FirstWaveAfter + 2f);
            Assert.AreEqual(1, board.OnTheHill);

            int red = Plan(board).WardOf('r');
            var ward = board.Wards[red];
            ward.Fill(ward.Capacity);

            int bolts = (int)(ward.Capacity / SiegeTuning.FuelShot(ward.Rank));
            int raw = bolts * SiegeTuning.DamageTo(SiegeKind.Creeper, ward.Rank, true, ward.Build);

            var hits = new List<SiegeStrike>();
            board.Overcharge(red, hits);

            Assert.IsNotEmpty(hits);
            Assert.AreEqual(raw * SiegeTuning.ShieldSoakTenths / 10, hits[0].Damage,
                            "a bulwark did not blunt an overcharge from a ward it answers to");
        }

        // ------------------------------------------------------------------ the beat
        // ------------------------------------------------------------------ lanes
        /// <summary>
        /// <b>A raider walks its colour's lane, strayed by one.</b> Sorting the hill by colour is
        /// what turns "which colour is coming" from a parse into a glance; the stray is what keeps
        /// a splash, a chain and a lance worth anything, since strictly sorted lanes would make
        /// every neighbour of a red raider red.
        /// </summary>
        [Test]
        public void ARaiderWalksItsOwnColoursLaneStrayedByOne()
        {
            var board = SiegeBoard.Build(Layout(new[] { "rrrrgggbbbyyyy" }));

            Frames(board, SiegeTuning.FirstWaveAfter + SiegeTuning.RaiderSpacing * 16f);

            int seen = 0;

            foreach (var raider in board.Raiders)
            {
                int ward = Plan(board).WardOf(SiegeLayout.Letters[raider.Colour]);
                int home = SiegeLanes.HomeOf(ward, Plan(board).Wards.Length);

                int stray = raider.Lane > home ? raider.Lane - home : home - raider.Lane;

                Assert.LessOrEqual(stray, SiegeLanes.Stray,
                                   $"a '{SiegeLayout.Letters[raider.Colour]}' raider walked lane "
                                   + $"{raider.Lane} against a home of {home}");
                seen++;
            }

            Assert.Greater(seen, 8, "the wave never came out");
        }

        /// <summary>Four wards spread over five lanes, symmetrically, leaving the middle to nobody.</summary>
        [Test]
        public void EverySeatHasALaneAndTheSeatsAreSpread()
        {
            Assert.AreEqual(0, SiegeLanes.HomeOf(0, 4));
            Assert.AreEqual(SiegeTuning.Lanes - 1, SiegeLanes.HomeOf(3, 4));

            Assert.AreEqual(0, SiegeLanes.HomeOf(0, 3));
            Assert.AreEqual(SiegeTuning.Lanes / 2, SiegeLanes.HomeOf(1, 3));
            Assert.AreEqual(SiegeTuning.Lanes - 1, SiegeLanes.HomeOf(2, 3));

            for (int wards = SiegeLayout.MinWards; wards <= SiegeLayout.MaxWards; wards++)
                for (int seat = 0; seat < wards; seat++)
                {
                    int home = SiegeLanes.HomeOf(seat, wards);

                    Assert.GreaterOrEqual(home, 0);
                    Assert.Less(home, SiegeTuning.Lanes);

                    if (seat > 0)
                        Assert.Greater(home, SiegeLanes.HomeOf(seat - 1, wards),
                                       "two seats share a lane, so half the hill is one column");
                }
        }

        /// <summary>
        /// <b>A lane costs the field's deal exactly nothing</b>, which is invariant 41's whole
        /// subject: the hill draws from the same stream the gems do, so changing how a lane is
        /// chosen without changing how many times the stream is drawn from leaves every shipped
        /// seed dealing the board it dealt before.
        /// </summary>
        [Test]
        public void TwoBoardsOfOneLevelStillDealTheSameField()
        {
            var layout = Layout(new[] { "rgby", "RGBY" });

            var a = SiegeBoard.Build(layout);
            var b = SiegeBoard.Build(layout);

            Frames(a, 30f);
            Frames(b, 30f);

            for (int i = 0; i < a.Count; i++)
                Assert.AreEqual(a.At(i), b.At(i), $"cell {i} differs");
        }

        // ------------------------------------------------------------------ the field is gems
        /// <summary>
        /// A field deals exactly the colours its line stands — both ways round, and the second
        /// half only became a rule when the lock arrived.
        /// </summary>
        [Test]
        public void AFieldDealsExactlyTheColoursItsLineStands()
        {
            Assert.IsTrue(ProtoGrid.TryRead(Field, 8, 5, SiegeLayout.Cells, out var grid, out _));

            var starved = new SiegeLayout(grid, "rgb", "rgby", new[] { "rgb" }, null);
            Assert.IsNotNull(starved.Fault);
            StringAssert.Contains("never deals", starved.Fault);

            var wasted = new SiegeLayout(grid, "rgby", "rgb", new[] { "rgb" }, null);
            Assert.IsNotNull(wasted.Fault);
            StringAssert.Contains("spent on nothing", wasted.Fault);

            var thin = new SiegeLayout(grid, "rg", "rg", new[] { "rg" }, null);
            Assert.IsNotNull(thin.Fault, "a two-colour field is not a board");
        }

        // ------------------------------------------------------------------ the duel
        /// <summary>A siege whose only wave is its boss. The boss is appended, never authored.</summary>
        static SiegeLayout Duel(string boss) => Layout(new string[0], boss: boss);

        /// <summary>Walks the clock until this board's boss is standing on its ground.</summary>
        static SiegeRaider Standing(SiegeBoard board)
        {
            for (int i = 0; i < 60 * 180; i++)
            {
                board.Advance(1f / 60f);

                var boss = board.Warlord;
                if (boss != null && boss.InPlace) return boss;
            }

            Assert.Fail("the boss never reached its ground");
            return null;
        }

        /// <summary>
        /// <b>Every ward answers a boss, whatever colour it wears.</b>
        ///
        /// <para>
        /// The one shape the lock cannot hold. A boss is one raider wearing one colour standing
        /// alone on the hill, so under the lock exactly one of the four turrets a player chose
        /// could fire at the finale and the other three banked fuel they would never spend — a
        /// duel fought by a quarter of the loadout, against the biggest number in the mode.
        /// </para>
        /// </summary>
        [Test]
        public void EveryWardOnTheLineAnswersABossWhateverColourItWears()
        {
            var board = SiegeBoard.Build(Duel("warlord:r"));
            var boss = Standing(board);

            Assert.AreEqual(0, boss.Colour, "the fixture boss is no longer red");

            for (int w = 0; w < board.Wards.Count; w++) Feed(board, w, 20f);

            var fired = new HashSet<int>();

            for (int i = 0; i < 60 * 6; i++)
                foreach (var bolt in board.Advance(1f / 60f).Bolts)
                {
                    Assert.AreEqual(boss.Id, bolt.Raider, "a bolt found something that is not the boss");
                    fired.Add(bolt.Ward);
                }

            Assert.AreEqual(board.Wards.Count, fired.Count,
                            "a duel was fought by " + fired.Count + " of "
                            + board.Wards.Count + " turrets");

            Assert.Less(boss.Health, boss.MaxHealth, "the whole line fired and the boss is whole");
        }

        /// <summary>
        /// <b>And its colour still decides the double, which is what keeps it a decision.</b>
        ///
        /// A boss answered by the colour it wears comes down twice as fast as one answered with
        /// anything else — so which colour to feed a duel has a right answer and a wrong one
        /// (invariant 26h), and the player reads it off the board rather than out of a panel: the
        /// right colour's numbers come up gold and everybody else's come up white.
        /// </summary>
        [Test]
        public void EveryWardLandsTheFullBoltOnABoss()
        {
            // A boss wears no colour (37dn): the letter on its token decides nothing, so every
            // ward fires at it, every bolt is full weight, and none is worth more than another.
            var board = SiegeBoard.Build(Duel("warlord:r"));
            var boss = Standing(board);
            while (boss.Guarded) board.Advance(1f / 60f);

            for (int w = 0; w < board.Wards.Count; w++) Feed(board, w, 20f);

            var fired = new int[board.Wards.Count];
            int weight = SiegeTuning.DamageTo(SiegeKind.Boss, 0, true);

            for (int i = 0; i < 60 * 6; i++)
                foreach (var bolt in board.Advance(1f / 60f).Bolts)
                {
                    if (bolt.Extra) continue;

                    Assert.IsTrue(bolt.Weak, "a bolt at a boss did not land at full weight");

                    // A bolt that reaches a phase's floor reports what really came off
                    // (`SiegeBoard.Wound`), which is less than a bolt; every other one is the
                    // whole bolt, from every ward alike.
                    Assert.LessOrEqual(bolt.Damage, weight);
                    if (bolt.Damage == weight) fired[bolt.Ward]++;
                }

            for (int w = 0; w < fired.Length; w++)
                Assert.Greater(fired[w], 0, $"ward {w} never landed a full bolt on the boss");

            Assert.AreEqual(0, boss.Colour, "the token's letter is still read, for a raise");
        }

        /// <summary>
        /// <b>A part-weight bolt costs a part of the fuel, and that is what makes "strictly
        /// additive" true rather than nearly true.</b>
        ///
        /// <para>
        /// A ward with nothing of its own on the hill <em>banks</em> what it is holding, so a
        /// half-weight shot at full price is not a free extra hit — it is the player's fuel
        /// converted at half the rate it would have been worth a few seconds later, spent on their
        /// behalf. Measured when it was: firing at a boss for half a hit at full price took
        /// Thornwatch from 81 held runs of 90 to 78, on a change meant to help.
        /// </para>
        /// <para>
        /// It is also what keeps <c>PerfectMatch</c> an identity over a duel: a unit of fuel is
        /// worth the same damage whoever burns it, so no arrangement of targets can make a match
        /// deliver less than par assumes.
        /// </para>
        /// </summary>
        [Test]
        public void APartWeightBoltCostsAPartOfTheFuel()
        {
            Assert.AreEqual(SiegeTuning.FuelShot(0), SiegeTuning.FuelShot(0, 10), .0001f,
                            "a full-weight bolt costs what a bolt costs");
            Assert.AreEqual(SiegeTuning.FuelShot(0) / 2f,
                            SiegeTuning.FuelShot(0, SiegeTuning.OffColourTenths), .0001f,
                            "a half-weight bolt does not cost half the fuel");
            Assert.AreEqual(10 / SiegeTuning.WeakMultiplier, SiegeTuning.OffColourTenths,
                            "the wrong colour is worth the un-doubled bolt and nothing else");

            // And played: a boss wears no colour (37dn), so every ward pays the full price for
            // the full bolt it lands - there is no part-weight bolt against a boss any more.
            var board = SiegeBoard.Build(Duel("warlord:r"));
            var boss = Standing(board);
            while (boss.Guarded) board.Advance(1f / 60f);

            int own = Plan(board).WardOf('r');
            int away = Plan(board).WardOf('b');

            Feed(board, own, 20f);
            Feed(board, away, 20f);

            float ownFuel = board.Wards[own].Fuel, awayFuel = board.Wards[away].Fuel;
            int ownShots = 0, awayShots = 0;

            for (int i = 0; i < 60 * 6; i++)
                foreach (var bolt in board.Advance(1f / 60f).Bolts)
                {
                    if (bolt.Extra) continue;
                    if (bolt.Ward == own) ownShots++;
                    if (bolt.Ward == away) awayShots++;
                }

            Assert.Greater(ownShots, 0);
            Assert.Greater(awayShots, 0);

            Assert.AreEqual(ownShots * SiegeTuning.FuelShot(0),
                            ownFuel - board.Wards[own].Fuel, .0001f,
                            "the ward of the token's colour did not pay the full price");
            Assert.AreEqual(awayShots * SiegeTuning.FuelShot(0),
                            awayFuel - board.Wards[away].Fuel, .0001f,
                            "another ward paid a part price for a full bolt");
        }

        /// <summary>
        /// <b>A boss is what a ward shoots when it has nothing of its own left to shoot.</b>
        ///
        /// Strictly a bolt it would otherwise not have fired, which is the only shape in which
        /// anything may reach past the lock at all. A
        /// boss holds the middle of the hill while its escort walks at the wards, so a line that
        /// turned to face the boss would be a line taken apart by the wave standing in front of
        /// it.
        /// </summary>
        [Test]
        public void ABossWaitsUntilTheHillIsClearedAndComesAlone()
        {
            // A creeper wave and then a boss (37dn): the boss is not on the hill while the
            // creeper stands, however long the clock runs, and comes on once it is gone.
            var board = SiegeBoard.Build(Layout(new[] { "r" }, boss: "warlord:b"));

            SiegeRaider creeper = null;

            for (int i = 0; i < 60 * 30 && creeper == null; i++)
            {
                board.Advance(1f / 60f);
                foreach (var raider in board.Raiders)
                    if (raider.Alive && raider.OnTheHill && !raider.Boss) creeper = raider;
            }

            Assert.IsNotNull(creeper, "the red creeper never reached the hill");

            for (int i = 0; i < 60 * (int)(SiegeTuning.BossAfter + 5f); i++)
            {
                board.Advance(1f / 60f);
                Assert.IsNull(board.Warlord, "the boss came onto a hill still walking");
            }

            creeper.Health = 0;
            creeper.Alive = false;

            var boss = Standing(board);
            Assert.IsTrue(boss.Boss);

            int company = 0;
            foreach (var raider in board.Raiders) if (raider.Alive && !raider.Boss) company++;
            Assert.AreEqual(0, company, "the boss did not come alone");
        }

        /// <summary>
        /// <b>The demand light says a boss is everybody's, and loudest on the colour it wears.</b>
        ///
        /// Counted as whole health it would light all four wards identically and say the thing
        /// that is not true — that it does not matter which one is fed. Counted at the share a
        /// bolt from that ward is really worth, it says both halves of the rule at once.
        /// </summary>
        [Test]
        public void ABossIsTheSameDemandOnEveryWard()
        {
            var board = SiegeBoard.Build(Duel("warlord:r"));
            var boss = Standing(board);

            int own = Plan(board).WardOf('r');

            // A boss wears no colour (37dn), so it is the whole of itself on every ward.
            for (int w = 0; w < board.Wards.Count; w++)
                Assert.AreEqual(boss.Health, board.DemandOf(w),
                                "a boss is not the whole of itself on the '"
                                + SiegeLayout.Letters[board.Wards[w].Colour] + "' ward");

            Assert.AreEqual(board.DemandOf(own), board.Busiest);
        }
    }
}
