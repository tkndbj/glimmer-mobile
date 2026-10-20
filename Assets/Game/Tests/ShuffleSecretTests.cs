using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Modes;
using GlimmerGrove.Shuffle;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The Shuffle lane's secrets and evolutions (MODES.md 59n): the catalog is well formed and
    /// worded in every language, a bond unlocks the moment its last part is taken and in any
    /// order, the two diamonds mark exactly the cards that bring one closer, and every power is
    /// seen doing what it says on the lane it ships on - and nothing on the plain line, which
    /// <c>ShuffleTests.ThePlainLineNeverDrawsTheArsenal</c> and
    /// <c>TheIdentityBoostChangesNothing</c> already hold for every tag a power reports.
    /// </summary>
    [TestFixture]
    public sealed class ShuffleSecretTests
    {
        // ------------------------------------------------------------------ the catalog
        [Test]
        public void EveryBondIsWellFormed()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var powers = new HashSet<SiegePower>();
            var recipes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var bond in ShuffleBonds.All)
            {
                Assert.IsTrue(ids.Add(bond.Id), $"bond id '{bond.Id}' is used twice");
                CollectionAssert.DoesNotContain(ShuffleBonds.Retired, bond.Id, $"'{bond.Id}' is a spent id");
                Assert.AreNotEqual(SiegePower.None, bond.Power, bond.Id);
                Assert.IsTrue(Enum.IsDefined(typeof(SiegePower), bond.Power), bond.Id);
                Assert.Less((int)bond.Power, 64, $"{bond.Id}'s power is not a bit the build can hold");
                Assert.IsTrue(powers.Add(bond.Power), $"{bond.Power} is granted by two bonds");

                foreach (string part in bond.Parts)
                {
                    var card = ShuffleCards.Find(part);
                    Assert.IsNotNull(card, $"{bond.Id} names '{part}', which is no card");
                    CollectionAssert.DoesNotContain(ShuffleCards.Retired, part, $"{bond.Id} names a withdrawn card");
                }

                if (bond.IsSecret)
                {
                    Assert.That(bond.Parts.Length, Is.InRange(2, 4), $"secret {bond.Id} takes {bond.Parts.Length} cards");
                    Assert.AreEqual(1, bond.Needs, $"secret {bond.Id} asks for copies");
                    Assert.AreEqual(new HashSet<string>(bond.Parts).Count, bond.Parts.Length, $"secret {bond.Id} names a card twice");

                    var tier = bond.Parts.Length == 2 ? ShuffleTier.Rare
                             : bond.Parts.Length == 3 ? ShuffleTier.Epic : ShuffleTier.Legendary;
                    Assert.AreEqual(tier, bond.Tier, $"secret {bond.Id}'s tier is not its size");

                    var sorted = new List<string>(bond.Parts);
                    sorted.Sort(StringComparer.Ordinal);
                    Assert.IsTrue(recipes.Add(string.Join("+", sorted.ToArray())), $"secret {bond.Id} is another secret's recipe");
                }
                else if (bond.IsAscension)
                {
                    // An ascension names its school's keystone and asks for the school's points
                    // (MODES.md 59r); `ShuffleSchoolTests` holds the rest of its shape.
                    Assert.AreEqual(1, bond.Parts.Length, $"ascension {bond.Id} names {bond.Parts.Length} cards");
                    Assert.IsTrue(ShuffleSchools.IsKeystone(ShuffleCards.Find(bond.Parts[0])), $"ascension {bond.Id} is not named for a keystone");
                    Assert.AreEqual(bond.School, ShuffleSchools.Of(ShuffleCards.Find(bond.Parts[0])), $"ascension {bond.Id} names another school's keystone");
                    Assert.AreEqual(ShuffleSchools.AscendAt, bond.Needs, bond.Id);
                    Assert.AreEqual(ShuffleTier.Legendary, bond.Tier, bond.Id);
                }
                else
                {
                    Assert.AreEqual(1, bond.Parts.Length, $"evolution {bond.Id} names {bond.Parts.Length} cards");

                    var card = ShuffleCards.Find(bond.Parts[0]);
                    Assert.GreaterOrEqual(bond.Needs, 2, $"evolution {bond.Id} is a single card");
                    Assert.AreEqual(card.Most, bond.Needs, $"evolution {bond.Id} is not reached at {card.Id}'s most");
                    Assert.GreaterOrEqual(bond.Tier, ShuffleTier.Rare, $"evolution {bond.Id} is common");
                }
            }

            Assert.GreaterOrEqual(ShuffleBonds.All.Length, 20, "the owner asked for many secrets");
            foreach (var tier in new[] { ShuffleTier.Rare, ShuffleTier.Epic, ShuffleTier.Legendary })
            {
                bool secret = false, evolution = false;
                foreach (var bond in ShuffleBonds.All)
                {
                    if (bond.Tier != tier) continue;
                    if (bond.IsSecret) secret = true;
                    else if (bond.IsEvolution) evolution = true;
                }

                Assert.IsTrue(secret, $"no {tier} secret");
                Assert.IsTrue(evolution, $"no {tier} evolution");
            }
        }

        [Test]
        public void EveryBondIsWordedInEveryLanguage()
        {
            string root = Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets", "Content", "loc");
            string[] ui = { "ui.shuffle.secret_unlocked", "ui.shuffle.evolution_unlocked", "ui.shuffle.ascension_unlocked", "ui.shuffle.okay" };

            foreach (string file in Directory.GetFiles(root, "*.json"))
            {
                var doc = TestJson.Parse(File.ReadAllText(file)) as Dictionary<string, object>;
                Assert.IsNotNull(doc, file);

                var texts = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var row in (List<object>)doc["entries"])
                {
                    var entry = (Dictionary<string, object>)row;
                    texts[(string)entry["key"]] = (string)entry["text"];
                }

                string name = Path.GetFileName(file);
                foreach (string key in ui)
                    Assert.IsTrue(texts.ContainsKey(key), $"{name} has no {key}");

                foreach (var bond in ShuffleBonds.All)
                {
                    foreach (string key in new[] { bond.NameKey, bond.NoteKey })
                    {
                        Assert.IsTrue(texts.TryGetValue(key, out string text), $"{name} has no {key}");
                        Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{name} {key} is empty");
                        StringAssert.DoesNotContain("{", text, $"{name} {key} carries a slot nothing fills");
                    }
                }
            }
        }

        // ------------------------------------------------------------------ unlocking
        [Test]
        public void ASecretUnlocksOnItsLastCardInAnyOrder()
        {
            var bond = ShuffleBonds.Find("frostfire");

            var a = new ShuffleBuild(1u);
            Take(a, "frostbite");
            Assert.IsEmpty(a.Unlocked);
            Assert.IsFalse(a.Boosts.Has(SiegePower.Frostfire), "half a secret granted its power");
            Take(a, "kindling");
            CollectionAssert.AreEqual(new[] { bond }, a.Unlocked, "the last part unlocked nothing");
            Assert.IsTrue(a.Boosts.Has(SiegePower.Frostfire));

            var b = new ShuffleBuild(1u);
            Take(b, "kindling");
            Take(b, "heavy_bolts");
            Take(b, "frostbite");
            CollectionAssert.AreEqual(new[] { bond }, b.Unlocked);
            CollectionAssert.AreEqual(a.Bonds, b.Bonds);

            // Unlocked once: another card taken after it unlocks nothing new.
            Take(b, "heavy_bolts");
            Assert.IsEmpty(b.Unlocked);
            Assert.IsTrue(b.Boosts.Has(SiegePower.Frostfire), "a later card took the power away");
        }

        [Test]
        public void AnEvolutionUnlocksAtTheCardsMost()
        {
            var build = new ShuffleBuild(1u);
            var shatter = ShuffleCards.Find("shatter");

            Take(build, "shatter");
            Assert.IsFalse(build.Boosts.Has(SiegePower.DiamondDust));
            Take(build, "shatter");
            Assert.AreEqual(1, build.Unlocked.Count);
            Assert.AreEqual("diamond_dust", build.Unlocked[0].Id);
            Assert.IsTrue(build.Boosts.Has(SiegePower.DiamondDust));
            Assert.IsFalse(build.Takeable(shatter));
        }

        [Test]
        public void OneTakeMayUnlockSeveralAndTheGrandestIsLast()
        {
            // Frostbite is the last part of frostfire (rare) and of permafrost (epic) at once.
            var build = new ShuffleBuild(1u);
            Take(build, "shatter");
            Take(build, "slow_march");
            Take(build, "kindling");
            Assert.AreEqual(0, build.Unlocked.Count);

            Take(build, "frostbite");
            Assert.AreEqual(2, build.Unlocked.Count);
            Assert.AreEqual("frostfire", build.Unlocked[0].Id);
            Assert.AreEqual("permafrost", build.Unlocked[1].Id, "the grander unlock was not shown last");
        }

        [Test]
        public void APowerIsOnTheBuildThatUnlockedItAndNowhereElse()
        {
            Assert.IsFalse(SiegeBoosts.None.Has(SiegePower.TimeStop));
            Assert.IsFalse(SiegeBoosts.None.AnyPower);
            Assert.Throws<InvalidOperationException>(() => SiegeBoosts.None.SetPower(SiegePower.TimeStop));

            var empty = new ShuffleBuild(1u);
            Assert.IsFalse(empty.Boosts.AnyPower, "an empty build holds a power");

            foreach (var card in ShuffleCards.All)
            {
                var one = new ShuffleBuild(1u);
                Take(one, card.Id);
                Assert.IsFalse(one.Boosts.AnyPower, $"one {card.Id} unlocked a power on its own");
            }
        }

        // ------------------------------------------------------------------ the diamonds
        [Test]
        public void TheGreenDiamondMarksACardThatGoesWithOneTaken()
        {
            var build = new ShuffleBuild(1u);
            var frost = ShuffleCards.Find("frostbite");
            var kindling = ShuffleCards.Find("kindling");
            var heavy = ShuffleCards.Find("heavy_bolts");

            Assert.IsFalse(ShuffleBonds.Advances(kindling, build), "a card was marked before any part was taken");

            Take(build, "frostbite");
            Assert.IsTrue(ShuffleBonds.Advances(kindling, build), "kindling goes with frostbite and was not marked");
            Assert.IsFalse(SecretMarked(frost, build), "a secret's part already held was marked for it");
            Assert.IsFalse(ShuffleBonds.Advances(heavy, build), "a card in no started secret was marked");

            // **A second frostbite is marked for the frost ascension** (MODES.md 59r): every copy
            // of a school's card is a step toward it, so a card held is marked again - for the
            // school, never for a secret it already counts toward.
            var into = new List<ShuffleBond>();
            ShuffleBonds.Advanced(frost, build, into);
            CollectionAssert.AreEqual(new[] { "ice_age" }, into.ConvertAll(b => b.Id), "a held frost card is marked for its ascension");

            // Shatter and slow march go with frostbite too (permafrost).
            Assert.IsTrue(ShuffleBonds.Advances(ShuffleCards.Find("shatter"), build));

            Take(build, "kindling");
            Assert.IsFalse(SecretMarked(kindling, build), "an unlocked secret still marked its part");
        }

        static bool SecretMarked(ShuffleCard card, ShuffleBuild build)
        {
            var into = new List<ShuffleBond>();
            ShuffleBonds.Advanced(card, build, into);
            return into.Exists(b => b.IsSecret);
        }

        /// <summary>
        /// <b>A card wears one diamond per secret it goes with</b>, so kindling beside frostbite
        /// and wildfire says both frostfire and fire dragon - in the order the catalog lists them.
        /// </summary>
        [Test]
        public void ACardIsMarkedForEverySecretItGoesWith()
        {
            var build = new ShuffleBuild(1u);
            var kindling = ShuffleCards.Find("kindling");
            var into = new List<ShuffleBond>();

            Take(build, "frostbite");
            Assert.AreEqual(1, ShuffleBonds.Advanced(kindling, build, into));
            Assert.AreEqual("frostfire", into[0].Id);

            // Wildfire starts two things kindling goes with: the fire dragon, and the fire school's
            // ascension - in the catalog's order, the ascension last.
            Take(build, "wildfire");
            into.Clear();
            Assert.AreEqual(3, ShuffleBonds.Advanced(kindling, build, into), "kindling goes with two started secrets and its school");
            CollectionAssert.AreEqual(new[] { "frostfire", "fire_dragon", "volcano" }, into.ConvertAll(b => b.Id));

            Assert.AreEqual(3, ShuffleBonds.Advanced(kindling, build, null), "the count needs no list");
        }

        /// <summary>
        /// <b>Every secret wears its own colour</b> (owner, 2026-10-14), none of them the
        /// evolutions' purple, and every diamond a bond names is a picture in the repo - cut by
        /// <c>make_shuffle_art.py</c> from these same figures (its <c>--check</c> holds the pixels).
        /// </summary>
        [Test]
        public void EverySecretWearsItsOwnColour()
        {
            var colours = new HashSet<string>(StringComparer.Ordinal)
            {
                ShuffleBond.EvolutionHue + "/" + ShuffleBond.EvolutionSaturation,
            };
            string ui = Path.Combine(TestJson.RepoRoot(), "Assets", "Game", "Art", "Ui");

            foreach (var bond in ShuffleBonds.All)
            {
                Assert.IsTrue(File.Exists(Path.Combine(ui, bond.Mark + ".png")), $"{bond.Id}'s diamond '{bond.Mark}' was never cut");

                if (bond.IsEvolution)
                {
                    Assert.AreEqual(ShuffleBond.EvolutionMark, bond.Mark, bond.Id);
                    continue;
                }

                Assert.That(bond.Hue, Is.InRange(0, 359), bond.Id);
                Assert.IsTrue(colours.Add(bond.Hue + "/" + bond.Saturation), $"secret {bond.Id} wears another bond's colour");
            }
        }

        [Test]
        public void ThePurpleDiamondMarksACardOnItsWayToEvolving()
        {
            var build = new ShuffleBuild(1u);
            var meteor = ShuffleCards.Find("meteor_call");

            Assert.IsFalse(ShuffleBonds.Evolves(meteor, build), "a card not yet held was marked");

            Take(build, "meteor_call");
            Assert.IsTrue(ShuffleBonds.Evolves(meteor, build), "a second meteor brings comet storm and was not marked");
            Assert.IsFalse(ShuffleBonds.Evolves(ShuffleCards.Find("quick_barrels"), build), "a card with no evolution was marked");

            Take(build, "meteor_call");
            Assert.IsFalse(ShuffleBonds.Evolves(meteor, build), "an evolved card at its most was marked");
        }

        // ------------------------------------------------------------------ on the board
        /// <summary>
        /// <b>Every power is seen on the board</b>, on the lane it ships on: each bond's cards
        /// taken (and for an evolution, to its most) and the model player run; what the board
        /// reports has to carry the power's own tag. A power that never fires is decoration (5d).
        /// </summary>
        [Test]
        public void EveryPowerIsSeenOnTheBoard()
        {
            var tags = new Dictionary<SiegePower, SiegeVia>
            {
                { SiegePower.Thunderhead, SiegeVia.Storm },
                { SiegePower.Frostfire, SiegeVia.Steam },
                { SiegePower.BladeOrbit, SiegeVia.Blade },
                { SiegePower.Plague, SiegeVia.Plague },
                { SiegePower.GemComets, SiegeVia.Comet },
                { SiegePower.Permafrost, SiegeVia.Permafrost },
                { SiegePower.EventHorizon, SiegeVia.Horizon },
                { SiegePower.FireDragon, SiegeVia.Dragon },
                { SiegePower.GoldenDome, SiegeVia.Dome },
                { SiegePower.RainbowCascade, SiegeVia.Rainbow },
                { SiegePower.SolarCannon, SiegeVia.Solar },
                { SiegePower.Armageddon, SiegeVia.Armageddon },
                { SiegePower.TimeStop, SiegeVia.TimeStop },
                { SiegePower.TitanShells, SiegeVia.Shell },
                { SiegePower.Deadeye, SiegeVia.Snipe },
                { SiegePower.Fortress, SiegeVia.Fortress },
                { SiegePower.ClusterBombs, SiegeVia.Bomblet },
                { SiegePower.BallLightning, SiegeVia.Orb },
                { SiegePower.Swarm, SiegeVia.Swarm },
                { SiegePower.DiamondDust, SiegeVia.Dust },
                { SiegePower.CometStorm, SiegeVia.Shard },
                { SiegePower.PrismLance, SiegeVia.Prism },
                { SiegePower.LaserGrid, SiegeVia.Orbital },
                { SiegePower.Necromancy, SiegeVia.Ghoul },
                { SiegePower.Volcano, SiegeVia.Lava },
                { SiegePower.IceAge, SiegeVia.IceAge },
                { SiegePower.Tempest, SiegeVia.Tempest },
            };

            var silent = new List<string>();

            foreach (var bond in ShuffleBonds.All)
            {
                Assert.IsTrue(tags.TryGetValue(bond.Power, out var via), $"no tag named for {bond.Power}");

                var run = new ShuffleRun(5u);
                if (bond.IsAscension) ShuffleSchoolTests.Ascend(run, bond);
                else foreach (string part in bond.Parts) ShuffleTests.Load(run, part);
                Assert.IsTrue(run.Build.Boosts.Has(bond.Power), $"{bond.Id}'s cards did not unlock it");

                var board = SiegeBoard.Build(ShuffleTests.Lane(), ShuffleTests.Breakers());
                board.Boosts = run.Build.Boosts;
                board.Refit();

                bool seen = false;
                ShuffleTests.Play(board, 90f, report =>
                {
                    foreach (var bolt in report.Bolts) if (bolt.Via == via) seen = true;
                    foreach (var volley in report.Volleys) if (volley.Via == via) seen = true;
                }, (b, turn) =>
                {
                    foreach (var beat in turn.Beats)
                        foreach (var comet in beat.Comets) if (comet.Via == via) seen = true;
                });

                if (!seen) silent.Add($"{bond.Id} never drew {via}");
            }

            Assert.IsEmpty(silent, string.Join("\n", silent.ToArray()));
        }

        /// <summary>A time stop holds the whole hill where it stands and the line keeps firing into it.</summary>
        [Test]
        public void ATimeStopHoldsTheHillAndTheLineFiresOn()
        {
            var board = Armed("time_stop");

            bool held = false;
            for (int i = 0; i < 60 * 60 && !board.IsFinished && !held; i++)
            {
                board.Advance(1f / 60f);
                if (board.TimeStopLeft <= .5f) continue;

                var before = new Dictionary<int, float>();
                foreach (var r in board.Raiders) if (r.Alive && r.OnTheHill) before[r.Id] = r.March;

                int shots = 0;
                for (int f = 0; f < 10; f++)
                    foreach (var bolt in board.Advance(1f / 60f).Bolts) if (!bolt.Extra) shots++;

                foreach (var r in board.Raiders)
                    if (before.TryGetValue(r.Id, out float march) && r.Alive && r.Heave <= 0f)
                        Assert.AreEqual(march, r.March, 1e-6f, "a raider walked through stopped time");

                Assert.IsTrue(board.Stilled, "a time stop did not still the hill");
                held = true;
            }

            Assert.IsTrue(held, "time never stopped");
        }

        /// <summary>The golden dome blocks its blows each wave and no more: a blocked blow never reaches a post.</summary>
        [Test]
        public void TheDomeBlocksItsBlowsEachWaveAndNoMore()
        {
            var board = Armed("golden_dome");
            foreach (var ward in board.Wards) ward.Fuel = 0f;

            int blocked = 0, landed = 0, raised = 0;
            for (int i = 0; i < 60 * 120 && !board.IsFinished; i++)
            {
                var report = board.Advance(1f / 60f);

                foreach (var volley in report.Volleys)
                {
                    if (volley.Via != SiegeVia.Dome) continue;

                    if (volley.Ward < 0)
                    {
                        Assert.AreEqual(SiegeSecrets.DomeBlocks, volley.Reach, "the dome was raised with the wrong blocks");
                        raised++;
                    }
                    else blocked++;
                }

                landed += report.Blows.Count;
                Assert.That(board.DomeLeft, Is.InRange(0, SiegeSecrets.DomeBlocks));
            }

            Assert.Greater(raised, 0, "the dome was never raised");
            Assert.Greater(blocked, 0, "the dome never blocked a blow on a dry line");
            Assert.LessOrEqual(blocked, raised * SiegeSecrets.DomeBlocks, "the dome blocked more than its blocks");
            Assert.Greater(landed, 0, "a dry line under a dome was never struck: the dome is endless");
        }

        /// <summary>
        /// <b>A run is the same run twice</b>: every power unlocked at once, played twice off one
        /// seed, reads identically - every roll a power takes is the build's own stream (41).
        /// </summary>
        [Test]
        public void EveryPowerAtOncePlaysTheSameTwice()
        {
            string Trace()
            {
                // Every secret and evolution, and the two ascensions a run may hold (MODES.md 59r).
                var run = new ShuffleRun(7u);
                int ascended = 0;
                foreach (var bond in ShuffleBonds.All)
                {
                    if (!bond.IsAscension) foreach (string part in bond.Parts) ShuffleTests.Load(run, part);
                    else if (ascended++ < ShuffleSchools.MostAscensions) ShuffleSchoolTests.Ascend(run, bond);
                }

                int ascensions = 0;
                foreach (var bond in ShuffleBonds.All) if (bond.IsAscension) ascensions++;
                Assert.AreEqual(ShuffleBonds.All.Length - ascensions + ShuffleSchools.MostAscensions, run.Build.Bonds.Count,
                                "not every bond unlocked");

                var board = SiegeBoard.Build(ShuffleTests.Lane(), ShuffleTests.Breakers());
                board.Boosts = run.Build.Boosts;
                board.Refit();

                var text = new System.Text.StringBuilder(4096);
                int frame = 0;
                ShuffleTests.Play(board, 60f, report =>
                {
                    if (frame++ % 20 != 0) return;
                    foreach (var r in board.Raiders) text.Append(r.Id).Append(':').Append(r.Health).Append(',');
                    foreach (var w in board.Wards) text.Append(w.Health).Append('/');
                    text.Append(report.Volleys.Count).Append(';');
                });

                return text.ToString();
            }

            Assert.AreEqual(Trace(), Trace(), "two runs off one seed played different boards");
        }

        // ------------------------------------------------------------------ the panel
        /// <summary>The unlock panel's column clears itself and the shortest canvas for the largest recipe there is.</summary>
        [Test]
        public void TheUnlockPanelIsClear()
        {
            int most = 0;
            foreach (var bond in ShuffleBonds.All) most = Math.Max(most, bond.Parts.Length);

            Assert.IsTrue(Layout.ShuffleUnlockLayout.Default.IsClear(most, out string fault), fault);
            Assert.IsTrue(Layout.ShuffleHandLayout.Default.IsClear(5, out fault), fault);
        }

        // ------------------------------------------------------------------ helpers
        static void Take(ShuffleBuild build, string id)
        {
            var card = ShuffleCards.Find(id);
            Assert.IsNotNull(card, id);
            Assert.IsTrue(build.Take(card), id);
        }

        static SiegeBoard Armed(string bond)
        {
            var run = new ShuffleRun(5u);
            foreach (string part in ShuffleBonds.Find(bond).Parts) ShuffleTests.Load(run, part);

            var board = SiegeBoard.Build(ShuffleTests.Lane(), ShuffleTests.Breakers());
            board.Boosts = run.Build.Boosts;
            board.Refit();
            return board;
        }
    }
}
