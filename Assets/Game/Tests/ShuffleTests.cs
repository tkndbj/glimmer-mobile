using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using GlimmerGrove.Progression;
using GlimmerGrove.Shuffle;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The Shuffle lane (MODES.md 59): its ramp, its deck, its build, the seam the board reads it
    /// through, and the one instrument the lane has - a model player taking random cards.
    ///
    /// <para>
    /// <b>The first fixture here is the one that matters to every other mode</b>:
    /// <see cref="TheIdentityBoostChangesNothing"/> plays a shipped rung step for step with the
    /// plain line and with a live, empty build, and holds every reading identical - which is
    /// the whole promise the seam makes to Thornwatch and the Infinite lane.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class ShuffleTests
    {
        // ------------------------------------------------------------------ the shipped lane
        /// <summary>`s13_shuffle`'s field, as `Tools/chapters/s13_shufflewatch.py` deals it.</summary>
        static readonly string[] Field =
        {
            "gbryrrgb",
            "ggyrbgry",
            "rybbgbyy",
            "brgbyygg",
            "bryrbrgr",
        };

        const uint ShippedSeed = 948309542u;

        static SiegeLayout Lane(int gold = 24)
        {
            Assert.IsTrue(ProtoGrid.TryRead(Field, 8, 5, SiegeLayout.Cells, out var grid, out string error), error);

            // Cogs at nought, as the shipped body authors them: nothing drops on this hill.
            var layout = new SiegeLayout(grid, "rgby", "rgby", new string[0], "", 0,
                                         new ShuffleRamp("rgby", gold, .5f), 0, "plsfha", true, true);
            Assert.IsNull(layout.Fault, layout.Fault);
            return layout;
        }

        /// <summary>A plain chapter rung, for the identity proof: `s01_ironward`.</summary>
        static SiegeLayout Rung()
        {
            string[] rows = { "rrgbbgbg", "bbgrbrbr", "rbbgrrgb", "grgbgbgr", "rggrgbbg" };
            Assert.IsTrue(ProtoGrid.TryRead(rows, 8, 5, SiegeLayout.Cells, out var grid, out string error), error);

            var layout = new SiegeLayout(grid, "rgb", "rgb",
                                         new[] { "rgbrgb", "rgbrgbrgb", "rgBrgbrg" }, "", 25);
            Assert.IsNull(layout.Fault, layout.Fault);
            return layout;
        }

        // ------------------------------------------------------------------ the track
        [Test]
        public void TheShuffleLaneIsATrackWithNoLadderThatDealsItsLine()
        {
            Assert.IsTrue(GameTrack.TryParse("shuffle", out var track, out string error), error);
            Assert.AreEqual(GameTrack.Shuffle, track);
            Assert.IsFalse(GameTrack.Shuffle.Laddered, "the Shuffle lane reads as a ladder");
            Assert.IsTrue(GameTrack.Shuffle.Dealt, "the Shuffle lane does not deal its line");
            Assert.IsFalse(GameTrack.Infinite.Dealt, "the Infinite lane deals its line");
            Assert.IsFalse(GameTrack.Main.Dealt);
            Assert.AreEqual("track.shuffle.point2", GameTrack.Shuffle.PointKey(2));

            Assert.Contains(GameTrack.Shuffle, GameTrack.Shipped);
        }

        [Test]
        public void TheLaneIsBoughtAtTheGateLikeTheInfiniteOne()
        {
            var builder = new CatalogIndexBuilder();
            builder.Add(new ManifestChapterDto
            {
                id = "s01_one", order = 10, version = 1, mode = "siege",
                levels = new[] { "a1", "a2", "a3", "a4" },
            }, 1);
            builder.Add(new ManifestChapterDto
            {
                id = "s13_shufflewatch", order = 160, version = 1, mode = "siege", track = "shuffle",
                levels = new[] { "s13_shuffle" },
            }, 1);

            var index = builder.Build();

            Assert.AreEqual(HeartPrice.Entry, HeartStake.PriceOf(index, LevelId.Parse("s13_shuffle")));
            Assert.AreEqual(GameTrack.Shuffle, index.TrackOf(LevelId.Parse("s13_shuffle")));

            // And it is not on the main ladder, so it gates no chapter and is gated by none.
            Assert.AreEqual(4, index.LevelsIn(GameMode.Siege).Count);
        }

        [Test]
        public void TheLaneDrawsTheMedleyAndIsRoutedToItsOwnScreen()
        {
            Assert.AreEqual(SiegeMode.Medley, SiegeMode.CastFor(GameTrack.Shuffle, 0));
            Assert.AreEqual(SiegeMode.Medley, SiegeMode.CastFor(GameTrack.Infinite, 0));
        }

        // ------------------------------------------------------------------ the ramp
        [Test]
        public void TheRampSendsNoBossAndNothingTheLineCannotAnswer()
        {
            var lane = Lane();
            var ramp = (ShuffleRamp)lane.Ramp;

            Assert.IsFalse(ramp.SendsBosses);
            Assert.IsTrue(lane.IsEndless);
            Assert.IsNull(lane.Endless, "a Shuffle ramp read as the Infinite lane's");

            for (int wave = 1; wave <= ramp.Proves; wave++)
            {
                var coming = ramp.WaveAt(wave, lane.Seed);
                Assert.AreEqual(ShuffleRamp.SizeAt(wave), coming.Length);

                for (int i = 0; i < coming.Length; i++)
                {
                    Assert.IsFalse(SiegeTuning.IsBoss(coming[i].Kind), $"wave {wave} sends a boss");
                    Assert.GreaterOrEqual(Array.IndexOf(lane.Wards, coming[i].Colour), 0,
                                          $"wave {wave} sends a colour the line cannot answer");
                }

                Assert.AreEqual(0, lane.BossesIn(wave - 1));
            }
        }

        [Test]
        public void EveryWaveIsTougherAndLongerUntilTheCeiling()
        {
            for (int wave = 2; wave <= ShuffleRamp.Walked; wave++)
            {
                Assert.Greater(ShuffleRamp.SurgeAt(wave).HealthTenths, ShuffleRamp.SurgeAt(wave - 1).HealthTenths,
                               $"wave {wave} is no tougher than wave {wave - 1}");
                Assert.GreaterOrEqual(ShuffleRamp.SurgeAt(wave).BlowTenths, ShuffleRamp.SurgeAt(wave - 1).BlowTenths);

                int grow = ShuffleRamp.SizeAt(wave) - ShuffleRamp.SizeAt(wave - 1);
                Assert.IsTrue(grow == 1 || (grow == 0 && ShuffleRamp.SizeAt(wave) == ShuffleRamp.MostRaiders),
                              $"wave {wave} grew by {grow}");
            }

            Assert.AreEqual(ShuffleRamp.FirstWave, ShuffleRamp.SizeAt(1));
            Assert.AreEqual(ShuffleRamp.MostRaiders, ShuffleRamp.SizeAt(ShuffleRamp.Walked));
            Assert.LessOrEqual(ShuffleRamp.MostRaiders, SiegeLayout.MaxRaiders);

            // Steeper than the Infinite lane, which is the point of it.
            Assert.Greater(ShuffleRamp.SurgeAt(20).HealthTenths, SiegeEndless.SurgeAt(20).HealthTenths);
        }

        /// <summary>
        /// The shipped seed's waves, pinned inline against `Tools/verify/siege.py`'s mirror
        /// (invariant 29e: a vector only the Editor can read is not a guard). Printed by
        /// `Tools/chapters/s13_shufflewatch.py`'s walk; a drift on either side lands here.
        /// </summary>
        [Test]
        public void TheRampAgreesWithTheOfflineMirror()
        {
            var lane = Lane();
            Assert.AreEqual(ShippedSeed, lane.Seed, "the shipped field's seed moved");

            Assert.AreEqual("rc rc yc yc", Spell(lane, 1));
            Assert.AreEqual("gB gc rc gB r# yc bB b# b# yc", Spell(lane, 7));
            Assert.AreEqual("b# bc g# b# yB r# rB r# bc g# yB b# bc yB b# y# gc y# b# yc yB yc y#",
                            Spell(lane, 20));
        }

        static string Spell(SiegeLayout lane, int wave)
        {
            var parts = new List<string>();
            foreach (var spec in lane.Ramp.WaveAt(wave, lane.Seed))
            {
                char kind = spec.Kind == SiegeKind.Brute ? 'B'
                          : spec.Kind == SiegeKind.Bulwark ? '#'
                          : spec.Kind == SiegeKind.Bomber ? '!' : 'c';
                parts.Add(spec.Colour.ToString() + kind);
            }
            return string.Join(" ", parts.ToArray());
        }

        /// <summary>
        /// Nothing drops on this lane's hill (the owner's call, 2026-10-09): the body authors no
        /// cogs, the ramp never sends a bomber, and no card is about either - so the hand is the
        /// whole ladder. Held on the ramp's walk and on the catalog, not on a constant.
        /// </summary>
        [Test]
        public void NothingDropsOnTheHill()
        {
            var lane = Lane();
            Assert.AreEqual(0, lane.Cogs, "the Shuffle lane authors a cog rate");

            for (int wave = 1; wave <= lane.Ramp.Proves; wave++)
                foreach (var spec in lane.Ramp.WaveAt(wave, lane.Seed))
                    Assert.IsFalse(SiegeTuning.LeavesABomb(spec.Kind), $"wave {wave} sends a bomber");

            foreach (var card in ShuffleCards.All)
                Assert.IsFalse(card.Id.Contains("cog") || card.Id.Contains("bomb"),
                               $"'{card.Id}' is a card about the hill's ground");
        }

        [Test]
        public void TheStonesComeOnTheLanesOwnWaves()
        {
            var lane = Lane();
            Assert.IsFalse(lane.CursesOn(ShuffleRamp.CursedWave - 1));
            Assert.IsTrue(lane.CursesOn(ShuffleRamp.CursedWave));
            Assert.IsFalse(lane.VoidsOn(ShuffleRamp.VoidWave - 1));
            Assert.IsTrue(lane.VoidsOn(ShuffleRamp.VoidWave));
            Assert.Less(ShuffleRamp.CursedWave, ShuffleRamp.VoidWave);
        }

        // ------------------------------------------------------------------ the deck
        [Test]
        public void TheTierWeightsSumToAHundredAndEveryTierHasCards()
        {
            foreach (var table in new[] { ShuffleDeck.Weights, ShuffleDeck.Heated, ShuffleDeck.Wild })
            {
                int sum = 0;
                foreach (int w in table) sum += w;
                Assert.AreEqual(100, sum);
                Assert.AreEqual(4, table.Length, "a table that does not name every tier");
            }

            foreach (ShuffleTier tier in Enum.GetValues(typeof(ShuffleTier)))
                Assert.Greater(ShuffleCards.Of(tier).Count, 0, $"no {tier} card");

            Assert.AreEqual(ShuffleTier.Common, ShuffleDeck.TierFor(0));
            Assert.AreEqual(ShuffleTier.Legendary, ShuffleDeck.TierFor(99));

            // **The deck heats up and never cools**: each later table deals the top two tiers
            // more often than the one before it, and a hand is dealt off the table its number
            // has reached.
            Assert.Less(ShuffleDeck.Weights[3], ShuffleDeck.Heated[3]);
            Assert.Less(ShuffleDeck.Heated[3], ShuffleDeck.Wild[3]);
            Assert.Less(ShuffleDeck.Weights[2], ShuffleDeck.Heated[2]);
            Assert.Less(ShuffleDeck.Heated[2], ShuffleDeck.Wild[2]);
            Assert.Less(ShuffleDeck.HeatedFrom, ShuffleDeck.WildFrom);
            Assert.AreSame(ShuffleDeck.Weights, ShuffleDeck.WeightsFor(ShuffleDeck.HeatedFrom - 1));
            Assert.AreSame(ShuffleDeck.Heated, ShuffleDeck.WeightsFor(ShuffleDeck.HeatedFrom));
            Assert.AreSame(ShuffleDeck.Wild, ShuffleDeck.WeightsFor(ShuffleDeck.WildFrom + 20));
        }

        /// <summary>
        /// A withdrawn card is gone from the deck and its id finds nothing, and its picture is not
        /// worn by anything new - Charm Magnet, withdrawn 2026-10-09.
        /// </summary>
        [Test]
        public void AWithdrawnCardIsNeverDealt()
        {
            CollectionAssert.Contains(ShuffleCards.Retired, "charm_magnet");
            Assert.IsNull(ShuffleCards.Find("charm_magnet"));

            foreach (var card in ShuffleCards.All)
                Assert.AreNotEqual(15, card.Picture, $"'{card.Id}' wears the withdrawn card's picture");

            var run = new ShuffleRun(7u);
            for (int hand = 1; hand <= 40; hand++)
                foreach (var card in ShuffleDeck.Deal(run.Build, hand, run.Build.Boosts.Roll100))
                    Assert.AreNotEqual("charm_magnet", card.Id);
        }

        [Test]
        public void EveryCardIsUniqueByIdPictureAndNotRetired()
        {
            var ids = new HashSet<string>();
            var pictures = new HashSet<int>();

            foreach (var card in ShuffleCards.All)
            {
                Assert.IsTrue(ids.Add(card.Id), $"two cards named '{card.Id}'");
                Assert.IsTrue(pictures.Add(card.Picture), $"'{card.Id}' wears another card's picture");
                Assert.IsFalse(Array.IndexOf(ShuffleCards.Retired, card.Id) >= 0, $"'{card.Id}' is retired");
                Assert.AreSame(card, ShuffleCards.Find(card.Id));
                Assert.AreEqual("shuffle." + card.Id + ".name", card.NameKey);
                Assert.AreEqual("Ui/Shuffle/" + card.Id, card.Icon);
            }

            Assert.IsNull(ShuffleCards.Find("not_a_card"));
        }

        [Test]
        public void AHandIsThreeDistinctTakeableCards()
        {
            for (uint seed = 1; seed <= 60; seed++)
            {
                var run = new ShuffleRun(seed);
                var hand = run.Deal();

                Assert.AreEqual(ShuffleDeck.HandSize, hand.Count);
                Assert.AreNotSame(hand[0], hand[1]);
                Assert.AreNotSame(hand[1], hand[2]);
                Assert.AreNotSame(hand[0], hand[2]);

                foreach (var card in hand) Assert.IsTrue(run.Build.Takeable(card));
            }
        }

        [Test]
        public void TwoRunsOnOneSeedAreDealtTheSameHandsAndTwoSeedsAreNot()
        {
            var a = new ShuffleRun(77u);
            var b = new ShuffleRun(77u);
            var c = new ShuffleRun(78u);

            bool differs = false;

            for (int hand = 0; hand < 6; hand++)
            {
                var ha = a.Deal(); var hb = b.Deal(); var hc = c.Deal();

                for (int i = 0; i < ha.Count; i++)
                {
                    Assert.AreSame(ha[i], hb[i]);
                    if (!ReferenceEquals(ha[i], hc[i])) differs = true;
                }

                a.Take(ha[0]); b.Take(hb[0]); c.Take(hc[0]);
            }

            Assert.IsTrue(differs, "two seeds dealt the same six hands");
        }

        [Test]
        public void ThePityFloorsTheFirstSlot()
        {
            for (uint seed = 100; seed < 160; seed++)
            {
                var run = new ShuffleRun(seed);

                for (int hand = 1; hand <= 10; hand++)
                {
                    var dealt = run.Deal();
                    if (hand % ShuffleDeck.EpicPity == 0)
                        Assert.GreaterOrEqual(dealt[0].Tier, ShuffleTier.Epic, $"seed {seed} hand {hand}");
                    else if (hand % ShuffleDeck.RarePity == 0)
                        Assert.GreaterOrEqual(dealt[0].Tier, ShuffleTier.Rare, $"seed {seed} hand {hand}");

                    run.Take(dealt[dealt.Count - 1]);
                }
            }
        }

        [Test]
        public void ADeckRunsOutWhenEveryCardIsHeldToItsMost()
        {
            var run = new ShuffleRun(5u);
            int most = 0;
            foreach (var card in ShuffleCards.All) most += card.Most;

            int taken = 0;
            for (int i = 0; i < most + 5; i++)
            {
                var hand = run.Deal();
                if (hand.Count == 0) break;
                Assert.IsTrue(run.Take(hand[0]));
                taken++;
            }

            Assert.AreEqual(most, taken, "the deck ran out early or dealt past its most");
            Assert.AreEqual(0, run.Deal().Count);
            Assert.IsNull(run.Open);
        }

        [Test]
        public void ACardNotOnTheTableIsRefused()
        {
            var run = new ShuffleRun(9u);
            Assert.IsFalse(run.Take(ShuffleCards.All[0]), "a card was taken with no hand dealt");

            var hand = run.Deal();
            ShuffleCard other = null;
            foreach (var card in ShuffleCards.All)
            {
                bool dealt = false;
                for (int i = 0; i < hand.Count; i++) if (ReferenceEquals(hand[i], card)) dealt = true;
                if (!dealt) { other = card; break; }
            }

            Assert.IsFalse(run.Take(other));
            Assert.IsTrue(run.Take(hand[1]));
            Assert.IsNull(run.Open);
            Assert.AreEqual(1, run.Build.Copies(hand[1]));
        }

        [Test]
        public void AHandIsOwedEveryTwoWaves()
        {
            Assert.AreEqual(0, ShuffleRun.Earned(0));
            Assert.AreEqual(0, ShuffleRun.Earned(1));
            Assert.AreEqual(1, ShuffleRun.Earned(2));
            Assert.AreEqual(1, ShuffleRun.Earned(3));
            Assert.AreEqual(2, ShuffleRun.Earned(4));

            var board = SiegeBoard.Build(Lane(), ShuffleLine.Of(null));
            var run = new ShuffleRun(3u);

            // Nobody matches on this board, so the line is sheltered for the walk: what is
            // being asked is when a hand is owed, not whether four unfed posts can stand.
            board.Sheltered = true;

            Assert.IsFalse(run.Owed(board), "a hand was owed before the first wave");

            // Walk to the third wave stepping out: the second has been seen off by then.
            for (int i = 0; i < 60 * 80 && board.WavesCleared < 2; i++) board.Advance(1f / 60f);
            Assert.GreaterOrEqual(board.WavesCleared, 2);
            Assert.IsTrue(run.Owed(board), "no hand owed after two waves");

            run.Deal();
            Assert.IsFalse(run.Owed(board), "a second hand was owed with one on the table");
        }

        // ------------------------------------------------------------------ the build
        [Test]
        public void EveryCardMovesTheFigureItNamesAndNothingElseMovesWithout()
        {
            foreach (var card in ShuffleCards.All)
            {
                var build = new ShuffleBuild(1u);
                var before = Snapshot(build.Boosts);

                Assert.IsTrue(build.Take(card), card.Id);

                var after = Snapshot(build.Boosts);
                Assert.AreNotEqual(before, after, $"'{card.Id}' moved nothing");

                // Every copy to its most, and never one past it.
                for (int n = 1; n < card.Most; n++) Assert.IsTrue(build.Take(card), $"{card.Id} copy {n + 1}");
                Assert.IsFalse(build.Take(card), $"'{card.Id}' was taken past its most");
                Assert.AreEqual(card.Most, build.Copies(card));
            }
        }

        [Test]
        public void ABuildIsAFunctionOfTheCardsAndNotTheirOrder()
        {
            var a = new ShuffleBuild(1u);
            var b = new ShuffleBuild(1u);

            var heavy = ShuffleCards.Find("heavy_bolts");
            var quick = ShuffleCards.Find("quick_barrels");
            var splash = ShuffleCards.Find("splash_shot");

            a.Take(heavy); a.Take(quick); a.Take(splash); a.Take(heavy);
            b.Take(splash); b.Take(heavy); b.Take(heavy); b.Take(quick);

            Assert.AreEqual(Snapshot(a.Boosts), Snapshot(b.Boosts));
            Assert.AreEqual(130, a.Boosts.DamagePercent);
            Assert.AreEqual(112, a.Boosts.FirePercent);
            Assert.AreEqual(4, a.Boosts.SplashTenths);
        }

        static string Snapshot(SiegeBoosts b)
            => string.Join("|", new object[]
            {
                b.DamagePercent, b.HeavyPercent, b.CritChance, b.CritPercent, b.DesperatePercent,
                b.FirePercent, b.FuelShotPercent, b.FuelGemPercent, b.SpillPercent,
                b.SplashTenths, b.SplashReach, b.ChainTenths, b.ChainHops, b.PierceEvery, b.PierceTenths,
                b.FrostTenths, b.FrostFor, b.BurnTenths, b.BurnFor, b.StunChance, b.StunFor,
                b.HexChance, b.HexFor, b.ExecutePercent, b.TwinChance, b.TwinTenths, b.BlastTenths,
                b.OffColourTenths, b.SiphonTenths, b.LeechHealth, b.GuardPercent,
                b.CapacityPercent, b.ExtraCharges, b.OverchargePercent, b.Armour,
                b.ThornsPercent, b.RepelChance, b.RegenEvery, b.SecondWinds, b.Phoenixes,
                b.HillPacePercent, b.RestPercent, b.WaveFuelTenths, b.WaveStill,
                b.WaveCharges,
                b.ScatterTenths, b.ScatterPellets, b.RicochetTenths, b.RicochetBounces,
                b.MissileEvery, b.MissileCount, b.MissileTenths, b.QuakeEvery, b.QuakeTenths,
                b.ToxicTenths, b.ToxicFor, b.TeslaTenths, b.TeslaArcs, b.SpinStep, b.SpinMost,
                b.ShatterTenths, b.ShatterReach, b.WildfireReach, b.WildfireTenths,
                b.MeteorEvery, b.MeteorTenths, b.VortexEvery, b.VortexTenths,
                b.NukeEvery, b.NukePercent, b.RayEvery, b.RayTenths, b.HydraTenths, b.HydraHeads,
                b.WildChance,
            });

        // ------------------------------------------------------------------ the seam
        [Test]
        public void ThePlainLineIsAnIdentityAndCannotBeWritten()
        {
            var none = SiegeBoosts.None;
            Assert.IsTrue(none.IsIdentity);
            Assert.AreEqual(20, none.Bolt(20, SiegeKind.Brute, out bool crit));
            Assert.IsFalse(crit);
            Assert.AreEqual(.44f, none.FireEvery(.44f, 10), 1e-6f);
            Assert.AreEqual(2f, none.FuelShot(2f), 1e-6f);
            Assert.AreEqual(3, none.Blow(3));
            Assert.AreEqual(0, none.Thorns(20));
            Assert.AreEqual(.44f, none.Spun(.44f, 10), 1e-6f);
            Assert.AreEqual(0, none.PlainShot(10, SiegeKind.Creeper));
            Assert.AreEqual(-1, none.WildElement());
            Assert.AreEqual(1f, none.HillPace, 1e-6f);
            Assert.IsFalse(none.Twins());
            Assert.IsFalse(none.TakeSecondWind());
            Assert.IsFalse(none.TakePhoenix());
            Assert.AreEqual(0, none.Roll100());

            Assert.Throws<InvalidOperationException>(() => none.AddDamage(1));
            Assert.Throws<InvalidOperationException>(() => none.Reset());

            var board = SiegeBoard.Build(Rung());
            Assert.AreSame(SiegeBoosts.None, board.Boosts, "a board is built holding the plain line");

            board.Boosts = null;
            Assert.AreSame(SiegeBoosts.None, board.Boosts, "a null build is not the plain line");
        }

        [Test]
        public void ALiveBuildReadsTheArithmeticItNames()
        {
            var live = new SiegeBoosts(4u);
            live.AddDamage(50);
            Assert.AreEqual(30, live.Bolt(20, SiegeKind.Creeper, out _));

            live.AddHeavy(50);
            Assert.AreEqual(40, live.Bolt(20, SiegeKind.Brute, out _));
            Assert.AreEqual(30, live.Bolt(20, SiegeKind.Creeper, out _));

            live.AddFire(100);
            Assert.AreEqual(.22f, live.FireEvery(.44f, 100), 1e-5f);

            live.AddFuelShot(-50);
            Assert.AreEqual(1f, live.FuelShot(2f), 1e-6f);

            live.AddArmour(1);
            Assert.AreEqual(1, live.Blow(2));
            Assert.AreEqual(1, live.Blow(1), "armour took a blow under one");

            // Spin-up: each unbroken shot quickens the next, to its most and never under the floor.
            Assert.AreEqual(.40f, live.Spun(.40f, 3), 1e-6f, "spin moved a build without it");
            live.SetSpin(25, 4);
            Assert.AreEqual(.20f, live.Spun(.40f, 4), 1e-5f);
            Assert.AreEqual(.20f, live.Spun(.40f, 9), 1e-5f, "spin climbed past its most");
            live.SetSpin(1000, 10);
            Assert.AreEqual(SiegeBoosts.QuickestShot, live.Spun(.40f, 10), 1e-6f);

            live.AddHillPace(-15);
            Assert.AreEqual(.85f, live.HillPace, 1e-6f);

            live.SetDesperate(60, 30);
            Assert.AreEqual(32, live.Desperate(20, 30));
            Assert.AreEqual(20, live.Desperate(20, 31));

            live.AddSecondWinds(2);
            Assert.IsTrue(live.TakeSecondWind());
            Assert.IsTrue(live.TakeSecondWind());
            Assert.IsFalse(live.TakeSecondWind());

            // A crit at a certainty doubles, and the roll comes off the build's own stream.
            var sure = new SiegeBoosts(9u);
            sure.AddCrit(100, 200);
            Assert.AreEqual(40, sure.Bolt(20, SiegeKind.Creeper, out bool crit));
            Assert.IsTrue(crit);
        }

        /// <summary>
        /// A shipped rung played step for step with the plain line and with a live build that
        /// holds no cards reads identically on every frame: every raider's health, every ward's
        /// fuel and health, every cog and bomb. A build with one card does not - which is what
        /// says the comparison can see a change at all.
        /// </summary>
        [Test]
        public void TheIdentityBoostChangesNothing()
        {
            string plain = Trace(SiegeBoard.Build(Rung()), null);

            var empty = new ShuffleBuild(1u);
            string live = Trace(SiegeBoard.Build(Rung()), empty.Boosts);

            Assert.AreEqual(plain, live, "a live build holding no cards played a different board");

            var heavy = new ShuffleBuild(1u);
            heavy.Take(ShuffleCards.Find("heavy_bolts"));
            Assert.AreNotEqual(plain, Trace(SiegeBoard.Build(Rung()), heavy.Boosts),
                               "a card changed nothing the trace can see");
        }

        /// <summary>
        /// <b>The plain line never reaches the arsenal</b>: a shipped rung played through with
        /// <see cref="SiegeBoosts.None"/> reports no hit drawn by a build, no wild element, no
        /// volley, no pool, no spin, no vortex and no ray, on any frame. The other half of
        /// <see cref="TheIdentityBoostChangesNothing"/>: that one holds the numbers, this one the
        /// drawing a chapter is shown.
        /// </summary>
        [Test]
        public void ThePlainLineNeverDrawsTheArsenal()
        {
            var board = SiegeBoard.Build(Rung());
            float since = 0f;

            for (int i = 0; i < 60 * 150 && !board.IsFinished; i++)
            {
                var report = board.Advance(1f / 60f);

                foreach (var bolt in report.Bolts)
                {
                    Assert.AreEqual(SiegeVia.None, bolt.Via, "a chapter drew a hit as a build's");
                    Assert.AreEqual(-1, bolt.Element, "a chapter rolled a wild element");
                    Assert.AreEqual(-1, bolt.From);
                }

                Assert.AreEqual(0, report.Volleys.Count, "a chapter set off a volley");
                Assert.AreEqual(0, board.Pools.Count);
                Assert.IsFalse(board.VortexOpen);
                Assert.IsFalse(board.RayLive);
                for (int w = 0; w < board.Wards.Count; w++) Assert.AreEqual(0, board.SpinOf(w));

                since += 1f / 60f;
                if (since >= 2.4f && Aimed(board, out int a, out int b)) { board.Swap(a, b); since = 0f; }
            }
        }

        /// <summary>
        /// <b>Every attack card is seen doing what it says, on the lane it ships on.</b> Each is
        /// taken to its most (with the one card it is built to meet, where it needs one - a
        /// shatter needs something frozen, wildfire something burning) and played by the model
        /// player; what the board reports has to carry the card's own drawing. A card whose rule
        /// never fires is decoration (5d), and this is where that would show.
        /// </summary>
        [Test]
        public void EveryAttackCardIsSeenOnTheBoard()
        {
            var cases = new (string card, string partner, SiegeVia via)[]
            {
                ("scatter_shot", null, SiegeVia.Pellet),
                ("ricochet", null, SiegeVia.Ricochet),
                ("missile_pod", null, SiegeVia.Missile),
                ("quake_rounds", null, SiegeVia.Quake),
                ("toxic_rounds", null, SiegeVia.Toxic),
                ("tesla_coil", null, SiegeVia.Tesla),
                ("shatter", "frostbite", SiegeVia.Shatter),
                ("wildfire", "kindling", SiegeVia.Wildfire),
                ("meteor_call", null, SiegeVia.Meteor),
                ("vortex", null, SiegeVia.Vortex),
                ("doomsday", null, SiegeVia.Nuke),
                ("death_ray", null, SiegeVia.Ray),
                ("hydra", null, SiegeVia.Hydra),
                ("splash_shot", null, SiegeVia.Splash),
                ("chain_arc", null, SiegeVia.Arc),
                ("pierce_rounds", null, SiegeVia.Lance),
                ("execution", null, SiegeVia.Execute),
                ("chain_reaction", null, SiegeVia.Blast),
            };

            var silent = new List<string>();

            foreach (var c in cases)
            {
                var run = new ShuffleRun(5u);
                Load(run, c.card);
                if (c.partner != null) Load(run, c.partner);

                var board = SiegeBoard.Build(Lane(), ShuffleLine.Of(null));
                board.Boosts = run.Build.Boosts;
                board.Refit();

                bool seen = false;
                Play(board, 90f, report =>
                {
                    foreach (var bolt in report.Bolts) if (bolt.Via == c.via) seen = true;
                    foreach (var volley in report.Volleys) if (volley.Via == c.via) seen = true;
                });

                if (!seen) silent.Add($"{c.card} never drew {c.via}");
            }

            Assert.IsEmpty(silent, string.Join("\n", silent));
        }

        /// <summary>Spin-up climbs with every unbroken shot to its most, and the cadence quickens with it.</summary>
        [Test]
        public void SpinUpQuickensAWardThatKeepsFiring()
        {
            var run = new ShuffleRun(5u);
            Load(run, "spin_up");

            var board = SiegeBoard.Build(Lane(), ShuffleLine.Of(null));
            board.Boosts = run.Build.Boosts;
            board.Refit();

            float most = 0f;
            Play(board, 90f, _ =>
            {
                for (int w = 0; w < board.Wards.Count; w++) most = Math.Max(most, board.SpinShare(w));
            });

            Assert.AreEqual(1f, most, 1e-6f, "no ward ever spun all the way up");
        }

        /// <summary>
        /// Wild magic strikes every primary bolt in an element, and over a run all four come up -
        /// the roll is off the build's stream, so this is a fact about a seed and holds.
        /// </summary>
        [Test]
        public void WildMagicRollsEveryElement()
        {
            var run = new ShuffleRun(5u);
            Load(run, "wild_magic");

            var board = SiegeBoard.Build(Lane(), ShuffleLine.Of(null));
            board.Boosts = run.Build.Boosts;
            board.Refit();

            var elements = new HashSet<int>();
            int primaries = 0;
            Play(board, 60f, report =>
            {
                foreach (var bolt in report.Bolts)
                {
                    if (bolt.Extra) continue;
                    primaries++;
                    Assert.GreaterOrEqual(bolt.Element, 0, "wild magic at a certainty left a bolt plain");
                    elements.Add(bolt.Element);
                }
            });

            Assert.Greater(primaries, 0);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, elements);
        }

        /// <summary>A tesla coil fights with an empty tube: a line never fed still hurts the hill.</summary>
        [Test]
        public void ATeslaCoilFightsDry()
        {
            var run = new ShuffleRun(5u);
            Load(run, "tesla_coil");

            var board = SiegeBoard.Build(Lane(), ShuffleLine.Of(null));
            board.Boosts = run.Build.Boosts;
            board.Refit();
            foreach (var ward in board.Wards) ward.Fuel = 0f;

            int zaps = 0;
            for (int i = 0; i < 60 * 40 && !board.IsFinished; i++)
            {
                var report = board.Advance(1f / 60f);
                foreach (var bolt in report.Bolts)
                {
                    if (bolt.Via == SiegeVia.Tesla) zaps++;
                    else Assert.AreNotEqual(SiegeVia.None, bolt.Via, "a dry, unfed line fired a bolt");
                }
            }

            Assert.Greater(zaps, 0, "a coil never fired from a dry line");
        }

        /// <summary>
        /// <b>Everything at once is playable.</b> Every card in the deck held to its most - a
        /// hundred-odd takes, which no run reaches (a hand comes every two waves) - played by the
        /// model player for the fixture's whole budget: the arsenal's recursions (a shatter
        /// bursting a frozen neighbour, a hydra's heads, a ricochet off a kill, a nuke's own kills)
        /// all terminate on every frame, and the whole deck reaches further than no deck. Prints
        /// how far it gets; whether a <em>real</em> run ends is
        /// <see cref="AModelPlayerTakingRandomCardsIsHeldAndThenFalls"/>'s question.
        /// </summary>
        [Test]
        public void EveryCardAtOnceIsPlayable()
        {
            var run = new ShuffleRun(5u);
            foreach (var card in ShuffleCards.All) Load(run, card.Id);

            var board = SiegeBoard.Build(Lane(), ShuffleLine.Of(null));
            board.Boosts = run.Build.Boosts;
            board.Refit();

            int reached = Hold(board, null, 2.4f, out int seconds, out _);
            Console.WriteLine($"Every card at its most: wave {reached} after {seconds}s"
                              + (board.IsFinished ? "" : " and still standing"));

            var bare = SiegeBoard.Build(Lane(), ShuffleLine.Of(null));
            int plain = Hold(bare, null, 2.4f, out _, out _);

            Assert.Greater(reached, plain, "the whole deck reached no further than none");
        }

        /// <summary>Takes <paramref name="id"/> to its most.</summary>
        static void Load(ShuffleRun run, string id)
        {
            var card = ShuffleCards.Find(id);
            Assert.IsNotNull(card, id);
            while (run.Build.Takeable(card)) run.Build.Take(card);
        }

        /// <summary>The model player for <paramref name="seconds"/>, handing every report to <paramref name="look"/>.</summary>
        static void Play(SiegeBoard board, float seconds, Action<SiegeReport> look)
        {
            const float Frame = 1f / 60f;
            float since = 0f;

            for (int i = 0; i < seconds * 60f && !board.IsFinished; i++)
            {
                look(board.Advance(Frame));

                for (int w = 0; w < board.Wards.Count; w++)
                    if (board.Wards[w].Armed) board.Overcharge(w, null);

                since += Frame;
                if (since < 1.2f || !Aimed(board, out int a, out int b)) continue;

                board.Swap(a, b);
                since = 0f;
            }
        }

        static string Trace(SiegeBoard board, SiegeBoosts boosts)
        {
            if (boosts != null) { board.Boosts = boosts; board.Refit(); }

            var text = new System.Text.StringBuilder(4096);
            float since = 0f;

            for (int i = 0; i < 60 * 150 && !board.IsFinished; i++)
            {
                board.Advance(1f / 60f);
                since += 1f / 60f;

                if (since >= 2.4f && Aimed(board, out int a, out int b)) { board.Swap(a, b); since = 0f; }

                if (i % 30 != 0) continue;

                foreach (var raider in board.Raiders) text.Append(raider.Id).Append(':').Append(raider.Health).Append(',');
                foreach (var ward in board.Wards) text.Append((int)(ward.Fuel * 10)).Append('/').Append(ward.Health).Append(',');
                text.Append(board.Cogs.Count).Append('#').Append(board.Bombs.Count).Append(';');
            }

            return text.ToString();
        }

        // ------------------------------------------------------------------ the line
        [Test]
        public void TheDealtLineIsFourBreakersAtOneStar()
        {
            var line = ShuffleLine.Of(null);

            foreach (char colour in WardLine.Colours)
            {
                Assert.AreEqual(WardModel.Elemental, line[colour].Id);
                Assert.AreEqual(WardStars.Least, line.BuildAt(WardLine.Colours.IndexOf(colour)).Stars);
            }

            Assert.IsNotNull(WardCatalog.Default.Find(ShuffleLine.TurretId), "the shipped roster has no Breaker");

            var board = SiegeBoard.Build(Lane(), line);
            foreach (var ward in board.Wards) Assert.AreEqual(WardModel.Elemental, ward.Model.Id);
        }

        [Test]
        public void EveryCardHasItsPictureOnDisk()
        {
            string root = TestJson.RepoRoot();

            foreach (var card in ShuffleCards.All)
            {
                string path = Path.Combine(root, "Assets", "Game", "Art", card.Icon.Replace('/', Path.DirectorySeparatorChar) + ".png");
                Assert.IsTrue(File.Exists(path), $"'{card.Id}' has no picture at {path}");
                Assert.IsTrue(File.Exists(path + ".meta"), $"'{card.Id}' has no .meta");
            }

            Assert.IsTrue(File.Exists(Path.Combine(root, "Assets", "Game", "Art", "Bg", "plain_shuffle.png")));
        }

        [Test]
        public void TheHandFitsItsPanelAndTheShortestCanvas()
        {
            Assert.IsTrue(Layout.ShuffleHandLayout.Default.IsClear(ShuffleDeck.HandSize, out string fault), fault);
        }

        /// <summary>
        /// The layout's copy of how far the frame's banner reaches is the frame's own figure
        /// (Domain cannot read Presentation, so the number is held here instead).
        /// </summary>
        [Test]
        public void TheHandsCrestFootIsTheFramesBanner()
        {
            var L = Layout.ShuffleHandLayout.Default;
            Assert.AreEqual(-VictoryFrame.BannerY + VictoryFrame.BannerSize.y * .5f, L.CrestFoot, 1e-3f);
            Assert.AreEqual(1000f, L.PanelWidth, "the hand's window is the Deals sheet's width");
        }

        // ------------------------------------------------------------------ the instrument
        /// <summary>
        /// A model player on the shipped lane, taking the first card of every hand it is dealt,
        /// at three rhythms over a handful of seeds. It prints where each run ends, so the two
        /// authored waves can be set from a reading rather than a feeling, and it holds three
        /// things: every run ends, a build reaches further than no build, and the lane's
        /// revival cards do what they say on a lost run.
        /// </summary>
        [Test]
        public void AModelPlayerTakingRandomCardsIsHeldAndThenFalls()
        {
            float[] rhythms = { 2.25f, 2.55f };
            uint[] seeds = { 11u, 23u, 47u };

            var table = new System.Text.StringBuilder();
            int built = 0, bare = 0;

            foreach (float rhythm in rhythms)
            {
                foreach (uint seed in seeds)
                {
                    var run = new ShuffleRun(seed);
                    var board = SiegeBoard.Build(Lane(), ShuffleLine.Of(null));
                    board.Boosts = run.Build.Boosts;
                    board.Refit();

                    int reached = Hold(board, run, rhythm, out int seconds, out int revived);
                    Assert.IsTrue(board.IsFinished, $"seed {seed} at {rhythm}: the run never ended ({reached} waves in {seconds}s)");

                    built += reached;
                    table.Append($"  seed {seed} rhythm {rhythm:0.00}: wave {reached} after {seconds}s, "
                                 + $"{run.Build.Taken.Count} card(s), {revived} revival(s)\n");
                }

                var plain = SiegeBoard.Build(Lane(), ShuffleLine.Of(null));
                bare += Hold(plain, null, rhythm, out int plainSeconds, out _);
                table.Append($"  no build, rhythm {rhythm:0.00}: wave {plain.WavesCleared} after {plainSeconds}s\n");
            }

            Console.WriteLine("Shuffle lane, first card of every hand:\n" + table);

            Assert.Greater(built, bare * rhythms.Length, "a build reached no further than none");
        }

        /// <summary>The model player: eager with charges and hands; a match every rhythm.</summary>
        static int Hold(SiegeBoard board, ShuffleRun run, float rhythm, out int seconds, out int revived)
        {
            const float Frame = 1f / 60f;
            float since = rhythm, clock = 0f;
            revived = 0;

            for (int i = 0; i < 60 * 1500 && !board.IsFinished; i++)
            {
                var report = board.Advance(Frame);
                clock += Frame;
                revived += report.Revived.Count;

                if (run != null && run.Owed(board))
                {
                    var hand = run.Deal();
                    if (hand.Count > 0 && run.Take(hand[0])) board.Refit();
                }

                for (int w = 0; w < board.Wards.Count; w++)
                    if (board.Wards[w].Armed) board.Overcharge(w, null);

                since += Frame;
                if (since < rhythm) continue;

                if (!Aimed(board, out int a, out int b)) continue;

                board.Swap(a, b);
                since = 0f;
            }

            seconds = (int)clock;
            return board.WavesCleared;
        }

        /// <summary>A swap that lines up the colour furthest down the hill, else any swap.</summary>
        static bool Aimed(SiegeBoard board, out int a, out int b)
        {
            var plan = board.Layout;
            char want = '\0';
            float furthest = -1f;

            foreach (var raider in board.Raiders)
            {
                if (!raider.Alive || !raider.OnTheHill || raider.March <= furthest) continue;

                char colour = SiegeLayout.Letters[raider.Colour];
                int ward = plan.WardOf(colour);
                if (ward < 0 || !board.Wards[ward].Alive) continue;

                furthest = raider.March;
                want = colour;
            }

            int fallbackA = -1, fallbackB = -1;

            for (int y = 0; y < board.Height; y++)
                for (int x = 0; x < board.Width; x++)
                {
                    int here = board.IndexOf(x, y);

                    for (int d = 0; d < 2; d++)
                    {
                        if (d == 0 && x + 1 >= board.Width) continue;
                        if (d == 1 && y + 1 >= board.Height) continue;

                        int other = d == 0 ? here + 1 : here + board.Width;
                        if (!board.Lines(here, other)) continue;

                        if (fallbackA < 0) { fallbackA = here; fallbackB = other; }
                        if (want == '\0') continue;
                        if (board.At(here) != want && board.At(other) != want) continue;

                        a = here; b = other;
                        return true;
                    }
                }

            a = fallbackA; b = fallbackB;
            return a >= 0;
        }
    }
}
