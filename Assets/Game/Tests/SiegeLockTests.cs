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

        // ------------------------------------------------------------------ the prism
        /// <summary>
        /// A prism reaches the next colour round, for a share of a full hit — and never a third.
        ///
        /// <b>The cap is on colours and not on strength</b>: under the lock a turret covering two
        /// colours is the only thing on the shelf that can answer a lane the player has not fed, so
        /// a third would not be a better rung, it would be the lock coming off.
        /// </summary>
        [Test]
        public void APrismReachesOneMoreColourAndNeverTwo()
        {
            var ward = new SiegeWard(0, new WardModel("t", WardAbility.Prism, 6, 0, 0, 0, 1, 1,
                                                      10, 10));

            Assert.AreEqual(10, ward.ReachTenths(0), "its own colour is never a share");
            Assert.AreEqual(6, ward.ReachTenths(1), "the next colour round is its magnitude");
            Assert.AreEqual(0, ward.ReachTenths(2));
            Assert.AreEqual(0, ward.ReachTenths(3));

            var greedy = new SiegeWard(0, new WardModel("g", WardAbility.Prism, 40, 0, 0, 0, 1, 1,
                                                        10, 10));

            Assert.AreEqual(10, greedy.ReachTenths(1), "a share may never exceed a full hit");
            Assert.AreEqual(0, greedy.ReachTenths(2), "a prism reached a third colour");

            Assert.AreEqual(1, SiegeWard.MostPartners);
        }

        /// <summary>
        /// <b>Own colour first, always</b>, which is what keeps the ability strictly additive: a
        /// partner shot is one this turret would otherwise not have fired, so it can never displace
        /// a full-weight hit and can never make a bolt weaker (invariant 42).
        /// </summary>
        [Test]
        public void APrismTakesItsPartnerOnlyWhenItsOwnColourIsClear()
        {
            // The roster's own prism rather than a synthetic one: what is being checked is the
            // shipped turret, and a fixture that built its own would pass over a roster whose
            // magnitude had been retuned to nought.
            var prism = WardCatalog.Default.Find("prism");

            Assert.IsNotNull(prism, "the roster no longer carries a prism");
            Assert.AreEqual(WardAbility.Prism, prism.Ability);
            Assert.Greater(prism.Magnitude, 0, "a prism with no share reaches nothing");

            var line = WardLine.Resolve(WardCatalog.Default,
                                        new[] { new WardSlot('r', prism.Id) }, (_, __) => true);

            Assert.AreEqual(prism, line.At(0), "the red seat did not take the prism");

            // A hill of nothing but the colour the red seat reaches *into*: its own has nothing on
            // it, so every shot it fires is a shot it would otherwise not have fired at all.
            var board = SiegeBoard.Build(Layout(new[] { "gggg" }), line);

            int red = Plan(board).WardOf('r');
            Assert.AreEqual(WardAbility.Prism, board.Wards[red].Ability);

            Feed(board, red, 20f);
            Frames(board, SiegeTuning.FirstWaveAfter + 4f);

            Assert.Less(board.Wards[red].Fuel, 20f,
                        "a prism never reached the colour it was bought to reach");

            // And its own colour comes first: on a hill carrying both, nothing green is touched
            // while a red is standing.
            var mixed = SiegeBoard.Build(Layout(new[] { "rg" }), line);

            Feed(mixed, red, 20f);

            // **Asked frame by frame, because the answer changes.** Collecting the bolts and
            // reading them afterwards would ask "was a red standing" long after the prism had
            // killed it - which is exactly the bolt this is supposed to allow.
            int fired = 0;

            for (int i = 0; i < 60 * 8; i++)
            {
                bool anyRed = false;

                foreach (var raider in mixed.Raiders)
                    if (raider.Alive && raider.OnTheHill && raider.Colour == 0) anyRed = true;

                foreach (var bolt in mixed.Advance(1f / 60f).Bolts)
                {
                    if (bolt.Ward != red || bolt.Extra) continue;

                    fired++;

                    var at = mixed.Find(bolt.Raider);
                    int colour = at != null ? at.Colour : 0;

                    if (!anyRed) continue;

                    Assert.AreEqual(0, colour,
                                    "a prism took its partner while its own colour was standing");
                }
            }

            Assert.Greater(fired, 0, "the prism never fired");
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
    }
}
