using System;
using System.Collections.Generic;
using System.Linq;
using GlimmerGrove.Modes;
using GlimmerGrove.Shuffle;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The Shuffle lane's schools (MODES.md 59r): five elements, each its own cards, a keystone and
    /// an ascension; the keystone dealt only to a school already attuned; at most two keystones a
    /// run; the lean that lets a started school come round a little more often, inside its tier
    /// only; every new card and every ascension seen doing what it says on the lane it ships on;
    /// no pile of frost and lightning able to hold the hill still; every school strike measured
    /// off the bolt; and the rarity printed and held, so an ascension stays the rare thing the
    /// owner asked for.
    /// </summary>
    [TestFixture]
    public sealed class ShuffleSchoolTests
    {
        // ------------------------------------------------------------------ the catalog
        [Test]
        public void EverySchoolHasItsCardsItsKeystoneAndItsAscension()
        {
            foreach (var school in ShuffleSchools.All)
            {
                var cards = ShuffleCards.All.Where(c => ShuffleSchools.Of(c) == school).ToList();
                var keystones = cards.Where(ShuffleSchools.IsKeystone).ToList();
                var ordinary = cards.Where(c => !ShuffleSchools.IsKeystone(c)).ToList();

                Assert.AreEqual(1, keystones.Count, $"{school} has {keystones.Count} keystones");
                var keystone = keystones[0];
                Assert.AreEqual(ShuffleTier.Legendary, keystone.Tier, $"{school}'s keystone is not legendary");
                Assert.AreEqual(1, keystone.Most, $"{school}'s keystone is taken more than once");
                Assert.AreEqual(ShuffleSchools.KeystoneOf(school), keystone.Effect);

                Assert.GreaterOrEqual(ordinary.Count, 4, $"{school} has too few cards to be a build");

                // **Reachable with room to spare**: every school can hold well over what ascending
                // it asks, so a run that commits is never one card short for want of a card.
                int most = ordinary.Sum(c => c.Most);
                Assert.GreaterOrEqual(most, ShuffleSchools.AscendAt + 2, $"{school} holds at most {most} points");

                var ascension = ShuffleSchools.AscensionOf(school);
                Assert.IsNotNull(ascension, $"{school} has no ascension");
                Assert.AreEqual(keystone.Id, ascension.Parts[0], $"{school}'s ascension names another keystone");
                Assert.IsTrue(ascension.IsAscension);
            }

            Assert.AreEqual(ShuffleSchools.All.Length, ShuffleBonds.All.Count(b => b.IsAscension), "an ascension of no school");
            Assert.AreEqual(ShuffleSchool.None, ShuffleSchools.Of(ShuffleCards.Find("heavy_bolts")), "a plain card was given a school");
        }

        // ------------------------------------------------------------------ the gate and the cap
        [Test]
        public void AKeystoneIsDealtOnlyOnceItsSchoolIsAttuned()
        {
            var keystone = ShuffleCards.Find("inferno_heart");
            var build = new ShuffleBuild(1u);

            Assert.IsTrue(build.Takeable(keystone), "a keystone the build could hold");
            Assert.IsFalse(ShuffleSchools.Dealable(keystone, build), "a keystone was dealt to a build with no fire");

            string[] fire = { "kindling", "fire_trail", "kindling" };
            for (int i = 0; i < fire.Length; i++)
            {
                Assert.IsFalse(ShuffleSchools.Dealable(keystone, build), $"dealt at {i} fire points");
                Assert.IsTrue(build.Take(ShuffleCards.Find(fire[i])));
            }

            Assert.AreEqual(ShuffleSchools.AttuneAt, ShuffleSchools.Points(build, ShuffleSchool.Fire));
            Assert.IsTrue(ShuffleSchools.Dealable(keystone, build), "an attuned school's keystone was withheld");

            // Never dealt where it is not dealable, over every hand of many runs.
            for (uint seed = 1; seed <= 200; seed++)
            {
                var run = new ShuffleRun(seed);
                for (int h = 0; h < 30; h++)
                {
                    var hand = run.Deal();
                    foreach (var card in hand)
                        if (ShuffleSchools.IsKeystone(card))
                            Assert.GreaterOrEqual(ShuffleSchools.Points(run.Build, ShuffleSchools.Of(card)), ShuffleSchools.AttuneAt,
                                                  $"seed {seed} hand {h + 1} dealt {card.Id} unattuned");
                    if (hand.Count > 0) run.Take(hand[0]);
                }
            }
        }

        [Test]
        public void ARunHoldsAtMostTwoKeystonesAndSoTwoAscensions()
        {
            var run = new ShuffleRun(3u);
            Ascend(run, ShuffleSchools.AscensionOf(ShuffleSchool.Fire));
            Ascend(run, ShuffleSchools.AscensionOf(ShuffleSchool.Frost));

            Assert.AreEqual(2, ShuffleSchools.Keystones(run.Build));
            Assert.AreEqual(2, run.Build.Bonds.Count(b => b.IsAscension), "two keystones did not ascend two schools");

            foreach (var school in new[] { ShuffleSchool.Laser, ShuffleSchool.Poison, ShuffleSchool.Storm })
            {
                var keystone = ShuffleCards.All.First(c => c.Effect == ShuffleSchools.KeystoneOf(school));
                foreach (var card in ShuffleCards.All.Where(c => ShuffleSchools.Of(c) == school && !ShuffleSchools.IsKeystone(c)))
                    while (run.Build.Takeable(card)) run.Build.Take(card);

                Assert.IsFalse(run.Build.Takeable(keystone), $"a third keystone ({keystone.Id}) could be taken");
                Assert.IsFalse(ShuffleSchools.Dealable(keystone, run.Build), $"a third keystone ({keystone.Id}) could be dealt");
                Assert.IsFalse(ShuffleSchools.Reachable(run.Build, school), $"{school} still reads as reachable");
                Assert.IsFalse(run.Build.Take(keystone), "the build took a third keystone");
            }

            Assert.AreEqual(2, run.Build.Bonds.Count(b => b.IsAscension), "a third ascension");
        }

        [Test]
        public void AnAscensionUnlocksOnItsLastStepInAnyOrder()
        {
            var volcano = ShuffleSchools.AscensionOf(ShuffleSchool.Fire);

            // Points first, keystone last.
            var a = new ShuffleBuild(1u);
            TakeSchool(a, ShuffleSchool.Fire, ShuffleSchools.AscendAt);
            Assert.IsFalse(a.Boosts.Has(SiegePower.Volcano), "points alone ascended");
            Assert.IsTrue(a.Take(ShuffleCards.Find("inferno_heart")));
            CollectionAssert.Contains(a.Unlocked, volcano, "the keystone, last, unlocked nothing");
            Assert.IsTrue(a.Boosts.Has(SiegePower.Volcano));
            Assert.IsTrue(a.Boosts.Ascended);

            // Keystone early, the last point last.
            var b = new ShuffleBuild(1u);
            TakeSchool(b, ShuffleSchool.Fire, ShuffleSchools.AttuneAt);
            Assert.IsTrue(b.Take(ShuffleCards.Find("inferno_heart")));
            Assert.IsFalse(b.Boosts.Has(SiegePower.Volcano), "the keystone alone ascended");
            TakeSchool(b, ShuffleSchool.Fire, ShuffleSchools.AscendAt - ShuffleSchools.AttuneAt);
            Assert.IsTrue(b.Boosts.Has(SiegePower.Volcano), "the last point unlocked nothing");
            CollectionAssert.AreEqual(a.Bonds.Where(x => x.IsAscension), b.Bonds.Where(x => x.IsAscension));

            Assert.AreEqual(ShuffleGrades.AscensionPoints, ShuffleGrades.BondPoints(volcano));
            Assert.Greater(ShuffleGrades.AscensionPoints, ShuffleGrades.BondPoints(ShuffleBonds.Find("armageddon")),
                           "an ascension is worth no more than a grand secret");
        }

        // ------------------------------------------------------------------ the lean
        [Test]
        public void TheLeanGrowsWithEachCardAndStopsAtItsCeiling()
        {
            var build = new ShuffleBuild(1u);
            var fire = ShuffleCards.Find("combust");
            var plain = ShuffleCards.Find("heavy_bolts");

            Assert.AreEqual(ShuffleSchools.PlainWeight, ShuffleSchools.Weight(fire, build), "a school leaned with no card in it");

            string[] taken = { "kindling", "fire_trail", "kindling", "fire_trail", "kindling" };
            for (int i = 0; i < taken.Length; i++)
            {
                Assert.IsTrue(build.Take(ShuffleCards.Find(taken[i])));
                int points = Math.Min(i + 1, ShuffleSchools.AffinityMostPoints);
                Assert.AreEqual(ShuffleSchools.PlainWeight + ShuffleSchools.AffinityStep * points,
                                ShuffleSchools.Weight(fire, build), $"after {i + 1} fire cards");
                Assert.AreEqual(ShuffleSchools.PlainWeight, ShuffleSchools.Weight(plain, build), "a plain card leaned");
            }
        }

        /// <summary>
        /// <b>The lean is real and only inside a tier</b>: thousands of seeded deals to a build
        /// holding three fire cards land fire within its tier at the weight the rule names, and the
        /// tiers themselves at the table's own chances (the odds a refresh prints, 59p).
        /// </summary>
        [Test]
        public void TheLeanMovesCardsWithinATierAndNeverATier()
        {
            const int Seeds = 3000;
            int rares = 0, fireRares = 0, slots = 0;
            var tiers = new int[ShuffleOdds.Tiers];

            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var run = new ShuffleRun(seed);
                foreach (var id in new[] { "kindling", "fire_trail", "kindling" }) run.Build.Take(ShuffleCards.Find(id));

                var hand = run.Deal();
                for (int i = 1; i < hand.Count; i++) // past the first: the promise lifts only it
                {
                    slots++;
                    tiers[(int)hand[i].Tier]++;
                    if (hand[i].Tier != ShuffleTier.Rare) continue;
                    rares++;
                    if (ShuffleSchools.Of(hand[i]) == ShuffleSchool.Fire) fireRares++;
                }
            }

            var build = new ShuffleBuild(1u);
            foreach (var id in new[] { "kindling", "fire_trail", "kindling" }) build.Take(ShuffleCards.Find(id));
            var pool = ShuffleCards.All.Where(c => c.Tier == ShuffleTier.Rare && ShuffleSchools.Dealable(c, build)).ToList();
            double fireWeight = pool.Where(c => ShuffleSchools.Of(c) == ShuffleSchool.Fire).Sum(c => ShuffleSchools.Weight(c, build));
            double expected = fireWeight / pool.Sum(c => ShuffleSchools.Weight(c, build));
            double plainShare = pool.Count(c => ShuffleSchools.Of(c) == ShuffleSchool.Fire) / (double)pool.Count;
            double seen = fireRares / (double)Math.Max(1, rares);

            Console.WriteLine($"fire among rares: {seen:P1} dealt, {expected:P1} by the rule, {plainShare:P1} with no lean " +
                              $"(x{expected / plainShare:0.00})");

            // A hand slot is two cards of a pool still to deal (the first slot is not counted), so
            // the share is within a few points of the single-draw rule.
            Assert.AreEqual(expected, seen, .03, "fire did not lean as the rule says");
            Assert.Greater(seen, plainShare * 1.2, "three fire cards did not lean fire");

            var table = ShuffleTuning.Shipped.Deck.TableFor(1);
            for (int t = 0; t < ShuffleOdds.Tiers; t++)
                Assert.AreEqual(table[t] / 100.0, tiers[t] / (double)slots, .02, $"{(ShuffleTier)t} moved under the lean");
        }

        // ------------------------------------------------------------------ on the board
        /// <summary>
        /// <b>Every element card is seen on the board</b>, at its most, with the card it is built to
        /// meet where it has one: what the board reports carries its own tag. A card that does
        /// nothing is decoration (5d).
        /// </summary>
        [Test]
        public void EverySchoolCardIsSeenOnTheBoard()
        {
            var cases = new (string card, string[] with, SiegeVia via)[]
            {
                ("focus_lens", new string[0], SiegeVia.Focus),
                ("refractor", new string[0], SiegeVia.Refract),
                ("photon_heart", new string[0], SiegeVia.Laser),
                ("venom_tips", new string[0], SiegeVia.Venom),
                ("contagion", new[] { "venom_tips" }, SiegeVia.Contagion),
                ("blight_heart", new string[0], SiegeVia.Venom),
                ("fire_trail", new string[0], SiegeVia.Flame),
                ("combust", new[] { "kindling" }, SiegeVia.Combust),
                ("inferno_heart", new string[0], SiegeVia.Inferno),
                ("cold_snap", new string[0], SiegeVia.Freeze),
                ("winter_heart", new string[0], SiegeVia.Freeze),
                ("static_charge", new string[0], SiegeVia.Static),
                ("thunderclap", new string[0], SiegeVia.Thunderclap),
                ("storm_heart", new string[0], SiegeVia.Jolt),
            };

            var silent = new List<string>();
            foreach (var (card, with, via) in cases)
            {
                var run = new ShuffleRun(5u);
                foreach (var id in with) ShuffleTests.Load(run, id);
                ShuffleTests.Load(run, card);

                var board = SiegeBoard.Build(ShuffleTests.Lane(), ShuffleTests.Breakers());
                board.Boosts = run.Build.Boosts;
                board.Refit();

                bool seen = false;
                ShuffleTests.Play(board, 90f, report =>
                {
                    foreach (var bolt in report.Bolts) if (bolt.Via == via) seen = true;
                    foreach (var volley in report.Volleys) if (volley.Via == via) seen = true;
                });

                if (!seen) silent.Add($"{card} never drew {via}");
            }

            Assert.IsEmpty(silent, string.Join("\n", silent));
        }

        /// <summary>The three cards that change a number rather than throw a hit, each held to the figure it prints.</summary>
        [Test]
        public void RotBlightAndBrittleMoveTheirFigures()
        {
            var build = new ShuffleBuild(1u);
            build.Take(ShuffleCards.Find("rot"));
            build.Take(ShuffleCards.Find("rot"));
            Assert.AreEqual(40, build.Boosts.RotPercent);
            Assert.AreEqual(60, build.Boosts.Rotted(100));
            Assert.AreEqual(1, build.Boosts.Rotted(1), "a rotted blow fell to nothing");

            build.Take(ShuffleCards.Find("brittle"));
            build.Take(ShuffleCards.Find("brittle"));
            build.Take(ShuffleCards.Find("blight_heart"));
            Assert.AreEqual(100, build.Boosts.Afflicted(100, false, false), "an unafflicted body took more");
            Assert.AreEqual(130, build.Boosts.Afflicted(100, true, false), "the blight");
            Assert.AreEqual(140, build.Boosts.Afflicted(100, false, true), "brittle");
            Assert.AreEqual(170, build.Boosts.Afflicted(100, true, true), "both, added and multiplied once");

            Assert.AreEqual(100, SiegeBoosts.None.Afflicted(100, true, true), "the plain line was afflicted");
            Assert.AreEqual(100, SiegeBoosts.None.Rotted(100), "the plain line was rotted");
            Assert.IsFalse(SiegeBoosts.None.AnySchool);
        }

        /// <summary>
        /// <b>No build holds the hill still</b> (the owner, 2026-10-20: "no raider ... 100% slowed and
        /// cannot walk"). The coldest, stormiest build the deck can deal - every frost and storm
        /// card at its most, both their keystones and ascensions, the hourglass, time stop, the
        /// vortex and the black hole - played for three minutes: no body on the hill is ever held
        /// for longer than time stop's own window, and every body is free to walk for most of its
        /// life. Every hold a school lays is a stun, which refuses itself until a second after the
        /// last ended (<see cref="SiegeRaider.Stagger"/>), and a chill never passes nine tenths.
        /// </summary>
        [Test]
        public void NoBuildHoldsTheHillStill()
        {
            var run = new ShuffleRun(11u);
            Ascend(run, ShuffleSchools.AscensionOf(ShuffleSchool.Frost));
            Ascend(run, ShuffleSchools.AscensionOf(ShuffleSchool.Storm));
            foreach (var card in ShuffleCards.All)
                if ((ShuffleSchools.Of(card) == ShuffleSchool.Frost || ShuffleSchools.Of(card) == ShuffleSchool.Storm)
                    && !ShuffleSchools.IsKeystone(card))
                    ShuffleTests.Load(run, card.Id);
            foreach (var id in new[] { "hourglass_heart", "spin_up", "vortex", "execution", "quake_rounds" }) ShuffleTests.Load(run, id);

            Assert.IsTrue(run.Build.Boosts.Has(SiegePower.IceAge) && run.Build.Boosts.Has(SiegePower.Tempest));
            Assert.IsTrue(run.Build.Boosts.Has(SiegePower.TimeStop) && run.Build.Boosts.Has(SiegePower.EventHorizon));

            // **What a build does, not what a player taps**: the roster without the hourglass charm,
            // because a charm the player sets off is a stop they chose to make (26h), and the
            // model player would otherwise chain one onto the end of every time stop.
            var board = SiegeBoard.Build(ShuffleTests.Lane(24, "plsfa"), ShuffleTests.Breakers());
            board.Boosts = run.Build.Boosts;
            board.Refit();

            const float Frame = 1f / 60f;
            var held = new Dictionary<int, float>();
            var streak = new Dictionary<int, float>();
            var life = new Dictionary<int, float>();
            float longest = 0f, worstShare = 0f;
            int measured = 0;

            ShuffleTests.Play(board, 180f, report =>
            {
                Immortal(board);

                foreach (var r in board.Raiders)
                {
                    if (!r.Alive || !r.OnTheHill || r.Boss || r.March >= r.Hold) continue;

                    bool still = r.Stunned || board.Stilled || r.Sunk;
                    life[r.Id] = (life.TryGetValue(r.Id, out float l) ? l : 0f) + Frame;
                    if (still) held[r.Id] = (held.TryGetValue(r.Id, out float h) ? h : 0f) + Frame;

                    float s = still ? (streak.TryGetValue(r.Id, out float k) ? k : 0f) + Frame : 0f;
                    streak[r.Id] = s;
                    longest = Math.Max(longest, s);

                    Assert.LessOrEqual(r.ChillTenths, 9, "a chill past nine tenths");
                }
            });

            foreach (var pair in life)
            {
                if (pair.Value < 8f) continue;
                measured++;
                worstShare = Math.Max(worstShare, (held.TryGetValue(pair.Key, out float h) ? h : 0f) / pair.Value);
            }

            Console.WriteLine($"{measured} bodies measured; the longest hold {longest:0.00} s; " +
                              $"the most of a life spent held {worstShare:P0}");

            Assert.Greater(measured, 10, "too few bodies lived long enough to measure");
            // The longest the rules let one body be held: a hold already running when time stops,
            // and the stop. A stop that ends gives every body its steadiness and a wave's hourglass
            // its wait (`Thawed`), so nothing is laid on the frame after.
            Assert.LessOrEqual(longest, SiegeSecrets.TimeFor + SiegeSchools.IceAgeFreeze + .1f,
                               "a body was held past the longest window two holds can make");
            Assert.Less(worstShare, .6f, "a body spent most of its life unable to walk");
        }

        /// <summary>
        /// <b>A school climbs with the bolt and nothing else</b>, which is what keeps it something
        /// the ramp outgrows (MODES.md 59c, 59r): the same seeded run with five heavy bolts on top
        /// strikes every school hit harder by the bolt's own share, and the strike is never a
        /// share of the body it lands on (bar the ice age's finishing blow, under a quarter).
        /// </summary>
        [Test]
        public void EverySchoolStrikeIsMeasuredOffTheBolt()
        {
            var vias = new[] { SiegeVia.Grid, SiegeVia.Orbital, SiegeVia.Lava, SiegeVia.Tempest, SiegeVia.Venom,
                               SiegeVia.Flame, SiegeVia.Static, SiegeVia.Thunderclap, SiegeVia.Focus, SiegeVia.Combust };

            // Beside each ascension, the school's cards it does not take on its own way up, so
            // every strike above is on the hill to be measured. (A ghoul rises off a death, and
            // nothing dies here; its burst is measured off the same `PlainShot`.)
            var extra = new Dictionary<ShuffleSchool, string[]>
            {
                { ShuffleSchool.Laser, new[] { "focus_lens" } },
                { ShuffleSchool.Poison, new[] { "venom_tips" } },
                { ShuffleSchool.Fire, new[] { "fire_trail", "combust", "kindling" } },
                { ShuffleSchool.Frost, new string[0] },
                { ShuffleSchool.Storm, new[] { "static_charge", "thunderclap" } },
            };

            Dictionary<SiegeVia, int> Strongest(bool heavy)
            {
                var found = new Dictionary<SiegeVia, int>();
                foreach (var school in ShuffleSchools.All)
                {
                    var run = new ShuffleRun(9u);
                    Ascend(run, ShuffleSchools.AscensionOf(school));
                    foreach (var id in extra[school]) ShuffleTests.Load(run, id);
                    if (heavy) ShuffleTests.Load(run, "heavy_bolts");

                    var board = SiegeBoard.Build(ShuffleTests.Lane(), ShuffleTests.Breakers());
                    board.Boosts = run.Build.Boosts;
                    board.Refit();

                    // Every body standing at full health while it is measured: a hit is never cut
                    // short by what was left to take.
                    ShuffleTests.Play(board, 40f, report =>
                    {
                        Immortal(board);
                        foreach (var bolt in report.Bolts)
                        {
                            if (Array.IndexOf(vias, bolt.Via) < 0 || bolt.Killed) continue;
                            found[bolt.Via] = Math.Max(found.TryGetValue(bolt.Via, out int had) ? had : 0, bolt.Damage);
                        }
                    });
                }

                return found;
            }

            var plain = Strongest(false);
            var heavier = Strongest(true);

            int compared = 0;
            foreach (var via in vias)
            {
                if (!plain.TryGetValue(via, out int a) || !heavier.TryGetValue(via, out int b) || a < 10) continue;
                compared++;
                Console.WriteLine($"{via}: {a} -> {b} (x{b / (double)a:0.00})");
                Assert.Greater(b, a, $"{via} did not climb with the bolt");
            }

            Assert.GreaterOrEqual(compared, 8, "too few school strikes were measured to say");
        }

        // ------------------------------------------------------------------ the rarity
        /// <summary>
        /// <b>The rarity, printed and held</b> (the owner, 2026-10-20: "truly rare, not often ... but
        /// still allow users to form them"). Hundreds of seeded runs dealt through the real deck to a
        /// player who takes the first card, one who takes at random, and one chasing a school with
        /// every pick; for each, the share of runs holding an ascension (and two) by a wave. A hand
        /// is owed every two waves, so hand <c>h</c> is wave <c>2h</c>. Read against the waves real
        /// runs reach (most fall before wave 24, gold), this is the table the gate below holds.
        /// </summary>
        [Test]
        public void TheAscensionTableIsPrintedAndAscensionsStayRare()
        {
            int[] at = { 10, 15, 20, 25, 30, 40, 60 };
            var one = new Dictionary<string, double[]>();
            var two = new Dictionary<string, double[]>();

            foreach (var chooser in Choosers)
            {
                var (first, second) = Simulate(chooser.Value, at[at.Length - 1]);
                one[chooser.Key] = at.Select(h => first.Count(x => x <= h) * 100.0 / Runs).ToArray();
                two[chooser.Key] = at.Select(h => second.Count(x => x <= h) * 100.0 / Runs).ToArray();

                Console.WriteLine($"{chooser.Key,-24} one ascension by wave " +
                                  string.Join("  ", at.Select((h, i) => $"{h * 2}: {one[chooser.Key][i],5:0.0}%")));
                Console.WriteLine($"{"",-24} two ascensions by wave " +
                                  string.Join("  ", at.Select((h, i) => $"{h * 2}: {two[chooser.Key][i],5:0.0}%")));
            }

            double Share(Dictionary<string, double[]> table, string who, int hand) => table[who][Array.IndexOf(at, hand)];

            // A run that never chooses almost never ascends before the deep waves.
            Assert.Less(Share(one, "first card", 20), 3.0, "a run that never chose ascended by wave 40");
            Assert.Less(Share(one, "random card", 20), 3.0, "a run choosing at random ascended by wave 40");

            // A player chasing a school with every pick: rare early, earned deep, never certain.
            Assert.Less(Share(one, "chasing a school", 10), 5.0, "an ascension by wave 20 is not rare");
            Assert.Less(Share(one, "chasing a school", 20), 50.0, "an ascension by wave 40 is the common case");
            Assert.Greater(Share(one, "chasing a school", 30), 15.0, "a committed player cannot reach an ascension by wave 60");
            Assert.Less(Share(two, "chasing a school", 25), 10.0, "two ascensions by wave 50 are not rare");
            Assert.Greater(Share(two, "chasing a school", 60), 5.0, "two ascensions are out of reach");
        }

        const int Runs = 600;

        static readonly Dictionary<string, Func<IReadOnlyList<ShuffleCard>, ShuffleBuild, Func<int>, ShuffleCard>> Choosers =
            new Dictionary<string, Func<IReadOnlyList<ShuffleCard>, ShuffleBuild, Func<int>, ShuffleCard>>
            {
                { "first card", (hand, build, roll) => hand[0] },
                { "random card", (hand, build, roll) => hand[roll() % hand.Count] },
                { "chasing a school", Chaser },
            };

        /// <summary>
        /// The most single-minded school player this fixture can write: the keystone of a school it
        /// is building when one is dealt; else a card of the school it leads in; else any element
        /// card (a start); else the first card.
        /// </summary>
        static ShuffleCard Chaser(IReadOnlyList<ShuffleCard> hand, ShuffleBuild build, Func<int> roll)
        {
            ShuffleSchool lead = ShuffleSchool.None;
            int best = -1;
            foreach (var school in ShuffleSchools.All)
            {
                var ascension = ShuffleSchools.AscensionOf(school);
                if (ascension.Completes(build) || !ShuffleSchools.Reachable(build, school)) continue;

                int points = ShuffleSchools.Points(build, school) + (ShuffleSchools.HoldsKeystone(build, school) ? 10 : 0);
                if (points > best) { best = points; lead = school; }
            }

            foreach (var card in hand)
                if (ShuffleSchools.IsKeystone(card) && ShuffleSchools.Of(card) == lead) return card;

            foreach (var card in hand)
                if (lead != ShuffleSchool.None && best > 0 && ShuffleSchools.Of(card) == lead && !ShuffleSchools.IsKeystone(card))
                    return card;

            foreach (var card in hand)
                if (ShuffleSchools.Of(card) != ShuffleSchool.None && ShuffleSchools.Reachable(build, ShuffleSchools.Of(card))
                    && !ShuffleSchools.AscensionOf(ShuffleSchools.Of(card)).Completes(build))
                    return card;

            return hand[0];
        }

        /// <summary>For each seeded run, the hand its first and its second ascension came on (int.MaxValue for never).</summary>
        static (List<int> first, List<int> second) Simulate(Func<IReadOnlyList<ShuffleCard>, ShuffleBuild, Func<int>, ShuffleCard> pick,
                                                            int hands)
        {
            var first = new List<int>(Runs);
            var second = new List<int>(Runs);

            for (int r = 0; r < Runs; r++)
            {
                uint seed = unchecked((uint)r * 2654435761u + 7u);
                var run = new ShuffleRun(seed == 0u ? 1u : seed);

                uint state = seed ^ 0x9E3779B9u;
                Func<int> roll = () =>
                {
                    state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                    return (int)(state % 100u);
                };

                int one = int.MaxValue, both = int.MaxValue;
                for (int h = 1; h <= hands; h++)
                {
                    var hand = run.Deal();
                    if (hand.Count > 0) Assert.IsTrue(run.Take(pick(hand, run.Build, roll)), "a chooser picked off the table");

                    int ascended = run.Build.Bonds.Count(b => b.IsAscension);
                    if (ascended >= 1 && one == int.MaxValue) one = h;
                    if (ascended >= 2 && both == int.MaxValue) both = h;
                }

                first.Add(one);
                second.Add(both);
            }

            return (first, second);
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>The health an immortal hill holds every body at: more than any one hit can take, so no hit is cut short.</summary>
        const int Deathless = 1000000;

        /// <summary>
        /// <b>A hill nothing dies on</b>: every body and every post back to full after each step, so a
        /// measurement watches whole lifetimes - a body held, a hit landing at its full size - rather
        /// than a strong build ending the bodies it is meant to be measured on.
        /// </summary>
        static void Immortal(SiegeBoard board)
        {
            foreach (var r in board.Raiders) if (r.Alive) r.Health = Deathless;
            foreach (var w in board.Wards) if (w.Alive) w.Health = w.Full;
        }

        /// <summary>
        /// Ascends <paramref name="ascension"/> on <paramref name="run"/>: its school's ordinary cards,
        /// each to its most in catalog order, until the points are there, then its keystone.
        /// </summary>
        internal static void Ascend(ShuffleRun run, ShuffleBond ascension)
        {
            Assert.IsNotNull(ascension);
            Assert.IsTrue(ascension.IsAscension, ascension.Id);

            foreach (var card in ShuffleCards.All)
            {
                if (ShuffleSchools.Points(run.Build, ascension.School) >= ascension.Needs) break;
                if (ShuffleSchools.Of(card) != ascension.School || ShuffleSchools.IsKeystone(card)) continue;

                while (run.Build.Takeable(card) && ShuffleSchools.Points(run.Build, ascension.School) < ascension.Needs)
                    run.Build.Take(card);
            }

            var keystone = ShuffleCards.Find(ascension.Parts[0]);
            Assert.IsTrue(run.Build.Take(keystone), $"{keystone.Id} could not be taken");
            Assert.IsTrue(run.Build.Boosts.Has(ascension.Power), $"{ascension.Id} did not ascend");
        }

        static void TakeSchool(ShuffleBuild build, ShuffleSchool school, int points)
        {
            int target = ShuffleSchools.Points(build, school) + points;
            foreach (var card in ShuffleCards.All)
            {
                if (ShuffleSchools.Of(card) != school || ShuffleSchools.IsKeystone(card)) continue;
                while (build.Takeable(card) && ShuffleSchools.Points(build, school) < target) build.Take(card);
            }

            Assert.AreEqual(target, ShuffleSchools.Points(build, school), $"{school} could not reach {target} points");
        }
    }
}
