using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Every Budburst grove that ships: that it still asks what it was authored to ask.
    ///
    /// <para>
    /// <b>A grove authors a board and a basket and nothing that can be graded</b>, so everything a
    /// player is measured against is a property of <see cref="BudSolver"/> — par, both star lines
    /// and the satchel the run is dealt. A change to the mix-and-wash rule therefore silently
    /// re-grades the whole chapter, which is <see cref="FallLadderTests"/>' argument for the
    /// second mode that works this way.
    /// </para>
    /// <para>
    /// <b>This fixture exists because that is not hypothetical here — it shipped.</b> The wash
    /// guard read <c>!_seen[nb]</c>, meaning to skip a flower that is itself bursting, but
    /// <c>_seen</c> is the flood fill's <em>visited</em> marker and so also covered every flower
    /// already scanned as part of a group of one or two that was discarded. The wash therefore
    /// stopped in one direction and not the other, by index order. Every offline gate stayed
    /// green: the board still parsed, the Python mirror still proved par 3 in 7,903 positions, and
    /// <c>content.py</c> printed <c>0 error(s), 0 warning(s)</c> — because the mirror is a
    /// <em>different copy of the rule</em> and it was the correct one. What noticed was the build
    /// gate refusing to prove par at all, twenty minutes into an Android build.
    /// </para>
    /// <para>
    /// <b><see cref="BudVectorTests"/> would have caught it and could not run.</b> It reads
    /// <c>bud-vectors.json</c> through <c>JsonUtility</c>, which is a native call, so the offline
    /// runner reports it as needing the Editor and it is the one gate nobody runs on the way past.
    /// That is the general lesson and it is why this fixture holds the board <b>inline</b>: a rule
    /// that exists twice needs at least one guard that runs where the code is edited (invariant
    /// 9a). The numbers below are the mirror's, so this fixture <em>is</em> the two copies being
    /// compared — it simply does it without a file.
    /// </para>
    /// </summary>
    public sealed class BudLadderTests
    {
        /// <summary>One authored rung: its board, its basket, and what that grove measured.</summary>
        internal sealed class Rung
        {
            public readonly string Id, Colours, Regrow;
            public readonly string[] Rows;
            public readonly int Par, Ways, Careless, Nodes, Spare;
            public readonly int Flowers, Cocoons;
            public readonly int BestAt, BestBurst, BestWaves, BestFreed;

            public Rung(string id, string colours, string regrow,
                        int par, int ways, int careless, int nodes,
                        int spare, int flowers, int cocoons,
                        int bestAt, int bestBurst, int bestWaves, int bestFreed,
                        params string[] rows)
            {
                Id = id;
                Colours = colours;
                Regrow = regrow;
                Par = par;
                Ways = ways;
                Careless = careless;
                Nodes = nodes;
                Spare = spare;
                Flowers = flowers;
                Cocoons = cocoons;
                BestAt = bestAt;
                BestBurst = bestBurst;
                BestWaves = bestWaves;
                BestFreed = bestFreed;
                Rows = rows;
            }

            public int Satchel => Par + Spare;

            public BudLayout Layout()
            {
                Assert.IsTrue(BudDeal.TryParse(Colours, out var deal, out string dealError),
                              Id + ": " + dealError);

                // A strip may deal blends where a basket may not: a basket is what the player
                // decides with, and a strip is scenery. See BudDeal.TryParse's `pure` argument.
                BudDeal strip = null;
                if (!string.IsNullOrEmpty(Regrow))
                    Assert.IsTrue(BudDeal.TryParse(Regrow, out strip, out string growError,
                                                   pure: false),
                                  Id + ": " + growError);

                int width = Rows[0].Length;
                Assert.IsTrue(BudLayout.TryReadRows(Rows, width, Rows.Length,
                                                    out var ground, out var value,
                                                    out string error),
                              Id + ": " + error);

                return new BudLayout(width, Rows.Length, ground, value, deal, strip);
            }
        }

        /// <summary>
        /// The Thicket. One grove, because what decides a mode is whether the verb lands and no
        /// number answers that (invariant 20j).
        /// </summary>
        internal static readonly Rung[] Ladder =
        {
            new Rung("b01_firstburst", "RGB", "RGBYMCW",
                     par: 3, ways: 80, careless: 3, nodes: 1989,
                     spare: 5,
                     flowers: 22, cocoons: 3,
                     bestAt: 10, bestBurst: 10, bestWaves: 3, bestFreed: 2,
                     "MRMGG",
                     "MoMRR",
                     "BRRGG",
                     "RBBoB",
                     "RMoRB"),

            new Rung("b01_catchalight", "RBG", "RGBYM",
                     par: 3, ways: 54, careless: 3, nodes: 7936,
                     spare: 5,
                     flowers: 32, cocoons: 4,
                     bestAt: 17, bestBurst: 13, bestWaves: 4, bestFreed: 2,
                     "BBYRYG",
                     "GoBGoR",
                     "BBRGRG",
                     "RRBBYY",
                     "BoGGoG",
                     "GGRYBB"),

            new Rung("b01_twiceknocked", "RBG", "RGBYMCW",
                     par: 3, ways: 87, careless: 3, nodes: 5310,
                     spare: 5,
                     flowers: 31, cocoons: 5,
                     bestAt: 23, bestBurst: 16, bestWaves: 5, bestFreed: 4,
                     "RGGYBB",
                     "RoRRoR",
                     "GYBYYB",
                     "RYORBG",
                     "BBRGBY",
                     "YoYRoY"),

            new Rung("b01_sunspill", "RBG", "RGBYMC",
                     par: 3, ways: 129, careless: 3, nodes: 3761,
                     spare: 5,
                     flowers: 30, cocoons: 6,
                     bestAt: 23, bestBurst: 20, bestWaves: 4, bestFreed: 5,
                     "YYMYRR",
                     "RoMYoB",
                     "RBBRYG",
                     "YOGGOG",
                     "BGRRYY",
                     "BoMMoR"),

            new Rung("b01_dewfall", "GBR", "RGBYMC",
                     par: 3, ways: 102, careless: 4, nodes: 5734,
                     spare: 5,
                     flowers: 36, cocoons: 6,
                     bestAt: 28, bestBurst: 22, bestWaves: 7, bestFreed: 5,
                     "MMCGRCR",
                     "CoCGBoR",
                     "CMRCBGG",
                     "BMOMOCM",
                     "BCBRBBM",
                     "CoBMMoG"),

            new Rung("b01_widewild", "BGR", "RYGMBC",
                     par: 3, ways: 109, careless: 3, nodes: 15451,
                     spare: 5,
                     flowers: 43, cocoons: 6,
                     bestAt: 44, bestBurst: 15, bestWaves: 4, bestFreed: 4,
                     "RRMBCCM",
                     "GoBRRoC",
                     "RGBCCGG",
                     "BROBOBB",
                     "BRBRRGG",
                     "CoBGGoR",
                     "CRRMMGG"),

            new Rung("b01_honeylight", "GBR", "RGBYMC",
                     par: 3, ways: 185, careless: 3, nodes: 13514,
                     spare: 5,
                     flowers: 49, cocoons: 7,
                     bestAt: 12, bestBurst: 10, bestWaves: 3, bestFreed: 1,
                     "CGCGCGGY",
                     "CoRRBCoY",
                     "GBBYGCBG",
                     "YYGOOYBG",
                     "GRRGGCYY",
                     "GoBBYGoG",
                     "BBRoYGCC"),

            new Rung("b01_wildwaking", "BRGR", "RYGMBC",
                     par: 3, ways: 86, careless: 3, nodes: 14882,
                     spare: 5,
                     flowers: 48, cocoons: 8,
                     bestAt: 31, bestBurst: 17, bestWaves: 4, bestFreed: 3,
                     "YBRRMYGM",
                     "YoYBYRoM",
                     "RMRBYRGR",
                     "RMORROGR",
                     "YBBGGBBM",
                     "YoGBBRoM",
                     "MMYooMRR"),

            new Rung("b01_everbloom", "GRB", "RGBYMCW",
                     par: 3, ways: 99, careless: 3, nodes: 15425,
                     spare: 5,
                     flowers: 47, cocoons: 9,
                     bestAt: 45, bestBurst: 25, bestWaves: 6, bestFreed: 7,
                     "YoGYMBoR",
                     "MYGYMBMR",
                     "RBOGYOGG",
                     "RBMGMGMR",
                     "GYOBMOGR",
                     "MYGYYRRB",
                     "MoRMoGoB"),

            new Rung("b01_thicketheart", "RGB", "RGBYM",
                     par: 3, ways: 61, careless: 3, nodes: 12038,
                     spare: 5,
                     flowers: 44, cocoons: 12,
                     bestAt: 18, bestBurst: 27, bestWaves: 8, bestFreed: 10,
                     "YoRGCBoR",
                     "YCCGBGBR",
                     "COGooGOM",
                     "BBYYBBRM",
                     "MOGooCOB",
                     "RYYMYRMY",
                     "YoCCRBoY"),
        };

        [Test]
        public void TheLadderStillMeasuresWhatItWasAuthoredFor()
        {
            foreach (var rung in Ladder)
            {
                var layout = rung.Layout();
                var survey = BudSolver.Survey(layout);

                Assert.IsTrue(survey.Proved,
                              rung.Id + ": the solver could no longer prove this grove inside " +
                              BudSolver.NodeBudget + " positions (it looked at " + survey.Nodes +
                              ") — the player's device runs this same search to work out par");

                Assert.AreEqual(rung.Par, survey.Par, rung.Id + " (par)");
                Assert.AreEqual(rung.Ways, survey.Ways, rung.Id + " (ways)");
                Assert.AreEqual(rung.Flowers, layout.Flowers, rung.Id + " (flowers)");
                Assert.AreEqual(rung.Cocoons, layout.Cocoons, rung.Id + " (cocoons)");

                Assert.AreEqual(rung.Careless, BudSolver.Careless(layout, rung.Satchel),
                                rung.Id + " (careless)");

                // The node cost is pinned rather than merely bounded, because it is the reading
                // that moved when the rule drifted — par came out unprovable, and the number that
                // said so was this one going from 7,903 to the whole budget.
                Assert.AreEqual(rung.Nodes, survey.Nodes, rung.Id + " (nodes)");
            }
        }

        /// <summary>
        /// The opening tap, cell for cell. Par alone would not have caught the drift that shipped:
        /// a wash that stops early makes the board <em>harder</em>, so par could plausibly have
        /// come out one higher and still looked like a level somebody authored. What cannot look
        /// plausible is the best tap moving to a different cell and taking three fewer flowers.
        /// </summary>
        [Test]
        public void TheBestOpeningTapOfEveryGroveIsStillTheOneItWasAuthoredAround()
        {
            foreach (var rung in Ladder)
            {
                var layout = rung.Layout();
                var board = new BudBoard(layout);
                int colour = layout.Deal.At(0);

                int at = -1;
                var best = BudChainResult.Nothing;

                for (int i = 0; i < layout.Count; i++)
                {
                    if (!board.CanTap(i, colour)) continue;

                    var chain = board.Preview(i, colour);
                    if (chain.Waves > best.Waves
                        || (chain.Waves == best.Waves && chain.Burst > best.Burst))
                    {
                        at = i;
                        best = chain;
                    }
                }

                Assert.AreEqual(rung.BestAt, at, rung.Id + " (which cell)");
                Assert.AreEqual(rung.BestBurst, best.Burst, rung.Id + " (flowers burst)");
                Assert.AreEqual(rung.BestWaves, best.Waves, rung.Id + " (waves)");
                Assert.AreEqual(rung.BestFreed, best.Freed, rung.Id + " (critters freed)");
            }
        }

        /// <summary>
        /// Every grove is dealt more taps than a careless player needs.
        ///
        /// This is the mode's brief read as an assertion (invariant 20k): a grove a player who
        /// never looks ahead cannot finish is asking for more than the mode promises. It is the
        /// one gate here that is deliberately the opposite way round from every other mode's, and
        /// it holds on <b>every</b> rung — nobody is meant to lose a grove in this chapter.
        /// </summary>
        [Test]
        public void ACarelessPlayerFinishesEveryGrove()
        {
            foreach (var rung in Ladder)
            {
                var layout = rung.Layout();
                int careless = BudSolver.Careless(layout, rung.Satchel);

                Assert.Greater(careless, 0,
                               rung.Id + ": a player who always taps whatever sets off the " +
                               "biggest chain never finishes this grove");

                Assert.LessOrEqual(careless, rung.Satchel,
                                   rung.Id + ": and they run out of taps");
            }
        }

        /// <summary>
        /// And the chapter asks for more as it goes, in the one dial it has.
        ///
        /// <para>
        /// <b>Par is 3 on every grove here and cannot be anything else</b>, so the ramp is not
        /// par and it is not the satchel either — every grove is dealt <c>par + 5</c>, which is
        /// eight taps for a three-tap answer, on a mode commissioned to be generous (invariant
        /// 20k). What climbs is <b>how many are shut in</b>, and with it how much grove there is
        /// and how many cocoons need two cracks rather than one. Freeing seven with the same
        /// eight taps is more to do than freeing four, and it is more to do without ever being
        /// tighter.
        /// </para>
        /// <para>
        /// <b>An earlier version ramped `greedy` instead</b> — whether a thoughtless run still
        /// scored three stars, true early and false late — and it is gone. It was a ramp built
        /// out of *withholding the reward*, which on this mode is the wrong lever twice over,
        /// and it forced the board sweep toward layouts whose biggest chain is a trap.
        /// </para>
        /// </summary>
        [Test]
        public void TheChapterAsksForMoreAsItGoes()
        {
            int shut = 0;

            foreach (var rung in Ladder)
            {
                Assert.GreaterOrEqual(rung.Cocoons, shut,
                                      rung.Id + " holds " + rung.Cocoons + " shut in, fewer " +
                                      "than the grove before it — the one dial this chapter " +
                                      "ramps on cannot go backwards");
                shut = rung.Cocoons;
            }

            Assert.Greater(Ladder[Ladder.Length - 1].Cocoons, Ladder[0].Cocoons,
                           "and the last grove asks for more than the first, or there is no " +
                           "ramp in the chapter at all");
        }

        /// <summary>
        /// Every grove can go off <b>hard</b>, which is the one thing this mode is actually for.
        ///
        /// <para>
        /// <b>The failure this catches is the one that was shipped and reported.</b> A board can
        /// be solvable, correctly par'd, fully validated and completely flat — a grove whose best
        /// play is three separate one-wave taps passes every other check in this repository and
        /// has the mode taken out of it. So the sweep held out for a cascade on every rung, and
        /// this is what stops a later re-deal quietly losing one.
        /// </para>
        /// </summary>
        [Test]
        public void AndEveryGroveCanStillGoOffHard()
        {
            foreach (var rung in Ladder)
            {
                var layout = rung.Layout();
                var board = new BudBoard(layout);
                int colour = layout.Deal.At(0);
                int best = 0;

                for (int i = 0; i < layout.Count; i++)
                {
                    if (!board.CanTap(i, colour)) continue;

                    var chain = board.Preview(i, colour);
                    if (chain.Waves > best) best = chain.Waves;
                }

                Assert.GreaterOrEqual(best, 2,
                                      rung.Id + ": the best opening tap on this grove runs " +
                                      best + " wave(s). A grove whose first tap cannot set off a " +
                                      "chain is this mode with the payout removed");
            }
        }

        /// <summary>
        /// A grove that already holds a bunch bursts in the first frame — the player is shown a
        /// chain they did not cause, and par is measured against a position they never met.
        /// </summary>
        [Test]
        public void EveryGroveIsAuthoredSettled()
        {
            foreach (var rung in Ladder)
                Assert.IsFalse(new BudBoard(rung.Layout()).AnyBunch(),
                               rung.Id + ": this grove goes off before it is touched");
        }

        /// <summary>
        /// The other half of the guard, and neither half is enough alone: this one would pass
        /// while the chapter authored something else entirely, and the one above would pass while
        /// the solver measured something else entirely.
        /// </summary>
        [Test]
        public void TheShippedChapterAuthorsExactlyThisLadder()
        {
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "StreamingAssets", "Content", "chapters",
                "b01_thicket.json"));

            Assert.IsTrue(File.Exists(path), "the chapter body is missing: " + path);

            var problems = new List<string>();
            Assert.IsTrue(ContentMapper.TryReadChapter(File.ReadAllText(path), problems,
                                                       out var body),
                          string.Join("\n", problems));

            Assert.AreEqual(Ladder.Length, body.Levels.Count,
                            "the Thicket no longer holds the groves this fixture pins");

            for (int i = 0; i < Ladder.Length; i++)
            {
                var rung = Ladder[i];
                var level = body.Levels[i];

                Assert.AreEqual(rung.Id, level.Id.Value, "rung " + (i + 1) + " (id)");

                var rules = level.RulesAs<BudRules>();
                Assert.IsNotNull(rules, rung.Id + " is no longer a Budburst grove");

                // Through a local rather than off the rules each time. `compile.py` refuses a
                // file that reads `.Layout.` without ever admitting a level's board can be
                // absent, which is a fact about `LevelDefinition` rather than about a grove — and
                // the check is coarse on purpose, so this costs one word.
                var shipped = rules.Layout;

                CollectionAssert.AreEqual(rung.Rows, shipped.Written(), rung.Id + " (board)");
                Assert.AreEqual(rung.Colours, shipped.Deal.Written(), rung.Id + " (basket)");
                Assert.AreEqual(rung.Regrow,
                                shipped.Regrow == null ? "" : shipped.Regrow.Written(),
                                rung.Id + " (strip)");
                Assert.IsTrue(shipped.Grows,
                              rung.Id + ": every grove in this chapter is a living one — it " +
                              "falls, it grows, its white flowers are bombs and one flower " +
                              "ripens between taps (invariant 20l)");
                Assert.AreEqual(rung.Spare, rules.Spare, rung.Id + " (spare)");

                Assert.AreEqual(0, shipped.Stones,
                                rung.Id + ": old wood is retired from this mode — a barrier can " +
                                "only ever make a chain shorter (`bud_wood` is a spent lesson id)");
            }
        }
    }
}
