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

            // **Set through an object initialiser rather than the constructor**, because they
            // are the half a Thicket rung does not have: nine of the ten groves below carry no
            // vine and two of them cannot be lost, and threading six more positional arguments
            // through every one of those would make the boards — the thing anybody reads this
            // file for — impossible to see.

            /// <summary>The vine grid, or null on a grove strung with none.</summary>
            public string[] Runners;

            /// <summary>How many vines this grove carries.</summary>
            public int Vines;

            /// <summary>
            /// Opening taps that play out differently with every vine cut, and how many of those
            /// burst <em>more</em> because a vine carried.
            ///
            /// <b><see cref="Changed"/> is the gate and <see cref="Caught"/> is the goal.</b>
            /// Nought changed means the runners are scenery on the board as dealt, which is
            /// invariant 26g's test and the state a mirror and a wick both shipped in.
            /// </summary>
            public int Changed, Caught;

            /// <summary>How many vines the best opening tap fires.</summary>
            public int Ran;

            /// <summary>
            /// Whether this grove has a fail line at all. The Thicket's first two do not —
            /// invariant 24: the rungs the heart gate lets go are the rungs the fail line lets
            /// go too.
            /// </summary>
            public bool Losable = true;

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

            /// <summary>What the run is dealt, or 0 on a grove that cannot be lost.</summary>
            public int Satchel => Losable ? Par + Spare : 0;

            /// <summary>What a careless run is measured against, budget or no budget.</summary>
            public int Room => Par + Spare;

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

                Assert.IsTrue(BudLayout.TryReadRunners(Runners, width, Rows.Length,
                                                       out var runners, out string vineError),
                              Id + ": " + vineError);

                return new BudLayout(width, Rows.Length, ground, value, deal, strip, runners);
            }
        }

        /// <summary>
        /// The Thicket. One grove, because what decides a mode is whether the verb lands and no
        /// number answers that (invariant 20j).
        /// </summary>
        internal static readonly Rung[] Thicket =
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
                     "RMoRB") { Losable = false },

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
                     "GGRYBB") { Losable = false },

            // The first grove in the game with a fail line on it, so it is dealt the most room
            // in the chapter — see `TheGrovesAPlayerLearnsTheVerbOnCannotBeLost`.
            new Rung("b01_twiceknocked", "BGR", "RYGMBC",
                     par: 3, ways: 113, careless: 3, nodes: 5683,
                     spare: 8,
                     flowers: 32, cocoons: 4,
                     bestAt: 18, bestBurst: 16, bestWaves: 5, bestFreed: 3,
                     "CMMGGM",
                     "GoRRoB",
                     "GRGGYB",
                     "RGYOYM",
                     "MoMBGG",
                     "MGYBCR"),

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

        /// <summary>
        /// The Tanglewood. What is new here is the <b>runner</b> — a vine joining two squares of
        /// the grove, and a bunch that <em>takes in</em> one end sends its colour to whatever is
        /// standing on the other (invariant 20m).
        ///
        /// <para>
        /// <b>Three of the ten carry no vine at all</b>, deliberately: a chapter of nothing but
        /// its own new object is a chapter with one board in it. What ramps is the same dial the
        /// Thicket's was — how many are shut in, six to fourteen — plus how many vines are
        /// strung across the grove.
        /// </para>
        /// <para>
        /// Every one of the seven that carries a vine is pinned with what the vines are
        /// <em>worth</em>: <c>Changed</c>, <c>Caught</c> and <c>Ran</c>. That is the guard this
        /// mode did not have when Lightfall shipped a mirror and then a wick — both passed every
        /// other check in this repository while changing nothing about any board they stood on.
        /// </para>
        /// </summary>
        internal static readonly Rung[] Tangle =
        {
            new Rung("b02_firstvine", "GBR", "RGBYMCW",
                     par: 3, ways: 49, careless: 3, nodes: 7277,
                     spare: 5,
                     flowers: 36, cocoons: 6,
                     bestAt: 8, bestBurst: 6, bestWaves: 2, bestFreed: 2,
                     "MoMMCGG",
                     "RRYRGoB",
                     "CYGRGBo",
                     "CGoGYBM",
                     "GRRBRYG",
                     "MoCMCoB")
            {
                Vines = 1, Changed = 2, Caught = 2, Ran = 1,
                Runners = new[]
                {
                    ".......",
                    ".a.....",
                    ".......",
                    ".......",
                    "....a..",
                    "......."
                },
            },

            new Rung("b02_longreach", "GBR", "RGBYMCW",
                     par: 3, ways: 43, careless: 3, nodes: 6322,
                     spare: 5,
                     flowers: 35, cocoons: 7,
                     bestAt: 8, bestBurst: 9, bestWaves: 3, bestFreed: 4,
                     "YoGCoMG",
                     "GRYGBBG",
                     "oYMRCoR",
                     "CCoCYMR",
                     "BGGMoYM",
                     "BoRRYRM")
            {
                Vines = 1, Changed = 2, Caught = 2, Ran = 1,
                Runners = new[]
                {
                    ".......",
                    ".a.....",
                    ".......",
                    ".......",
                    ".......",
                    ".....a."
                },
            },

            new Rung("b02_deepthicket", "BGR", "RGBYMC",
                     par: 3, ways: 89, careless: 3, nodes: 9398,
                     spare: 5,
                     flowers: 42, cocoons: 7,
                     bestAt: 31, bestBurst: 30, bestWaves: 7, bestFreed: 5,
                     "YoRYYoG",
                     "GBBOMYB",
                     "YRRCGMB",
                     "CoCMGoC",
                     "MBGGYBB",
                     "MGoCoMC",
                     "GRRCYMC"),

            new Rung("b02_windingway", "RGB", "RGBYMC",
                     par: 3, ways: 59, careless: 3, nodes: 11399,
                     spare: 5,
                     flowers: 41, cocoons: 8,
                     bestAt: 9, bestBurst: 27, bestWaves: 6, bestFreed: 6,
                     "MoRRGoY",
                     "GMBMGBG",
                     "oRMCBoY",
                     "CRBOBRC",
                     "CoYRMCo",
                     "GRBMRGB",
                     "GYYoBBR")
            {
                Vines = 1, Changed = 1, Caught = 1, Ran = 1,
                Runners = new[]
                {
                    ".......",
                    "..a....",
                    ".......",
                    ".......",
                    ".......",
                    "....a..",
                    "......."
                },
            },

            new Rung("b02_twovines", "BRG", "RGBYMC",
                     par: 3, ways: 68, careless: 3, nodes: 15166,
                     spare: 5,
                     flowers: 47, cocoons: 9,
                     bestAt: 9, bestBurst: 12, bestWaves: 3, bestFreed: 4,
                     "RoRBBGoC",
                     "GGCCYRCM",
                     "RCYoBGoM",
                     "RBMMORRC",
                     "YoGGoBCM",
                     "RYMBGCBM",
                     "BBoBGoCC")
            {
                Vines = 2, Changed = 1, Caught = 1, Ran = 1,
                Runners = new[]
                {
                    ".......a",
                    ".b......",
                    "........",
                    "........",
                    "........",
                    "......b.",
                    "a......."
                },
            },

            new Rung("b02_thewilds", "BRG", "RGBYM",
                     par: 3, ways: 85, careless: 4, nodes: 16321,
                     spare: 5,
                     flowers: 47, cocoons: 9,
                     bestAt: 54, bestBurst: 36, bestWaves: 7, bestFreed: 6,
                     "MoGGCOBB",
                     "MRYCYYGG",
                     "CoRYoGBo",
                     "CRCYCBYM",
                     "YOBBoCRR",
                     "MGRGYBCC",
                     "RRoGBoGY"),

            new Rung("b02_crossvine", "RGB", "RGBYMC",
                     par: 3, ways: 101, careless: 3, nodes: 14018,
                     spare: 5,
                     flowers: 46, cocoons: 10,
                     bestAt: 13, bestBurst: 24, bestWaves: 5, bestFreed: 6,
                     "MoYCMOMM",
                     "RGGYYGGB",
                     "oCYMoRBo",
                     "CGYMGGoG",
                     "MOMoBYRG",
                     "YRRBYRMY",
                     "BBoCGGoR")
            {
                Vines = 2, Changed = 3, Caught = 2, Ran = 1,
                Runners = new[]
                {
                    ".......a",
                    "..b.....",
                    "........",
                    "........",
                    "........",
                    ".....b..",
                    "a......."
                },
            },

            new Rung("b02_thornedvine", "BRG", "RGBYM",
                     par: 3, ways: 204, careless: 3, nodes: 10932,
                     spare: 5,
                     flowers: 46, cocoons: 10,
                     bestAt: 2, bestBurst: 37, bestWaves: 8, bestFreed: 7,
                     "YoRMOBBo",
                     "MRMYRRCC",
                     "YMRoMBoR",
                     "oGRYMBCG",
                     "YOGGoCMR",
                     "YMYYMMBB",
                     "MYoGGoMY")
            {
                Vines = 2, Changed = 2, Caught = 2, Ran = 1,
                Runners = new[]
                {
                    "........",
                    ".a......",
                    ".......b",
                    "........",
                    "b.......",
                    "......a.",
                    "........"
                },
            },

            new Rung("b02_thetangle", "GBR", "RGBYM",
                     par: 3, ways: 126, careless: 3, nodes: 12510,
                     spare: 5,
                     flowers: 44, cocoons: 12,
                     bestAt: 47, bestBurst: 35, bestWaves: 7, bestFreed: 10,
                     "GoBCORRo",
                     "RBCBCGGR",
                     "RCYoMMoY",
                     "oBYBYYRo",
                     "BOCYoGCC",
                     "BMCBMCGB",
                     "GoYRoCoB")
            {
                Vines = 3, Changed = 3, Caught = 3, Ran = 2,
                Runners = new[]
                {
                    "......a.",
                    ".b......",
                    "c.......",
                    "........",
                    ".......c",
                    "......b.",
                    "a......."
                },
            },

            new Rung("b02_tangleheart", "RBG", "RYGMBC",
                     par: 3, ways: 54, careless: 3, nodes: 11067,
                     spare: 5,
                     flowers: 42, cocoons: 14,
                     bestAt: 37, bestBurst: 30, bestWaves: 7, bestFreed: 11,
                     "GoGoOBBo",
                     "BBMYRYCG",
                     "RMRoCCoR",
                     "oBBCMMCo",
                     "YOCBoBMR",
                     "BRRBGMBR",
                     "CoYoOGoC")
            {
                Vines = 3, Changed = 3, Caught = 3, Ran = 1,
                Runners = new[]
                {
                    "......a.",
                    ".b......",
                    "c.......",
                    "........",
                    ".......c",
                    "......b.",
                    "a......."
                },
            },
        };

        /// <summary>
        /// Both shipped chapters, and what each is proved against.
        ///
        /// <b>Walked together wherever the rule is about the <em>mode</em></b> — that par is
        /// still what the search says, that a careless player finishes, that every grove goes off
        /// — and separately only where the rule is about a chapter, which is its ramp and its
        /// own file on disk.
        /// </summary>
        internal static readonly (string File, Rung[] Rungs)[] Chapters =
        {
            ("b01_thicket.json", Thicket),
            ("b02_tanglewood.json", Tangle),
        };

        static IEnumerable<Rung> Every
        {
            get
            {
                foreach (var chapter in Chapters)
                    foreach (var rung in chapter.Rungs)
                        yield return rung;
            }
        }

        [Test]
        public void TheLadderStillMeasuresWhatItWasAuthoredFor()
        {
            foreach (var rung in Every)
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

                // Against the room a grove *has* rather than the satchel it is dealt, so a
                // rung that cannot be lost is measured the same way as every other one — see
                // `Rung.Room`.
                Assert.AreEqual(rung.Careless, BudSolver.Careless(layout, rung.Room),
                                rung.Id + " (careless)");

                Assert.AreEqual(rung.Vines, layout.Runners, rung.Id + " (runners)");

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
            foreach (var rung in Every)
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
            foreach (var rung in Every)
            {
                var layout = rung.Layout();
                int careless = BudSolver.Careless(layout, rung.Room);

                Assert.Greater(careless, 0,
                               rung.Id + ": a player who always taps whatever sets off the " +
                               "biggest chain never finishes this grove");

                Assert.LessOrEqual(careless, rung.Room,
                                   rung.Id + ": and they run out of taps");
            }
        }

        /// <summary>
        /// The two rungs a player meets while they are still working out what the verb is cannot
        /// be lost, and the one after them is dealt the most room in the chapter.
        ///
        /// <para>
        /// <b>Invariant 24 one step in.</b> Charging a heart while somebody is learning the verb
        /// is the mistake that invariant names; ending their run for the same reason is that
        /// mistake applied to the fail line, and it is invisible because each rule is
        /// individually right. Every other mode's opening level was authored
        /// <c>budgetFactor: -1</c> and this one was not, for a whole chapter.
        /// </para>
        /// </summary>
        [Test]
        public void TheGrovesAPlayerLearnsTheVerbOnCannotBeLost()
        {
            Assert.IsFalse(Thicket[0].Losable, Thicket[0].Id + " must not have a fail line");
            Assert.IsFalse(Thicket[1].Losable, Thicket[1].Id + " must not have a fail line");

            Assert.IsTrue(Thicket[2].Losable,
                          Thicket[2].Id + " is where the satchel is first taught, so it has to " +
                          "have one");

            for (int i = 2; i < Thicket.Length; i++)
                Assert.GreaterOrEqual(Thicket[2].Spare, Thicket[i].Spare,
                                      "the first grove with a fail line on it is dealt the most " +
                                      "room in the chapter, and " + Thicket[i].Id + " has more");
        }

        /// <summary>
        /// Every vine on every shipped grove is worth something, and the reading is the one
        /// invariant 26g prescribes: cut them and see whether anything changes.
        ///
        /// <para>
        /// <b>This is the guard the mode did not have when a mirror and a wick shipped.</b> Both
        /// passed every check in this repository — solvable, correctly par'd, tight <c>ways</c>,
        /// every gate green — while changing nothing about any board they stood on, because a
        /// decoration passes all of those. What catches it is one comparison, and it has to run
        /// over the boards that actually ship rather than over a fixture's own invention.
        /// </para>
        /// <para>
        /// <c>Changed</c> is the gate and <c>Caught</c> is the goal: a tap that bursts
        /// <em>more</em> because a vine carried is the arrangement the player is making, where a
        /// tap that merely leaves a different grove behind is the vine existing.
        /// </para>
        /// </summary>
        [Test]
        public void EveryVineOnEveryShippedGroveIsWorthSomething()
        {
            foreach (var rung in Every)
            {
                var layout = rung.Layout();
                if (rung.Vines == 0)
                {
                    Assert.AreEqual(0, layout.Runners, rung.Id + " carries a vine it does not own");
                    continue;
                }

                // Through `BudRunnerReading` rather than worked out here, so this fixture and
                // `BudValidator` cannot come to disagree about what "worth something" means —
                // and so what is pinned below is the *shipping* answer against the Python
                // mirror's, which is the comparison invariant 9a is about. Writing it out a
                // second time here is exactly how the two came to differ by one on
                // `b02_crossvine`: this copy compared the chain's four numbers and the mirror
                // compared the grove, and a vine that moves a colour without setting anything
                // off changes the second and not the first.
                var reading = BudRunnerReading.Of(layout);

                Assert.AreEqual(rung.Changed, reading.Changed,
                                rung.Id + " (opening taps the vines change)");
                Assert.AreEqual(rung.Caught, reading.Caught,
                                rung.Id + " (opening taps the vines catch)");
                Assert.AreEqual(rung.Ran, reading.Ran,
                                rung.Id + " (vines the best opening tap fires)");

                Assert.Greater(reading.Changed, 0,
                               rung.Id + ": not one opening tap on this grove plays out " +
                               "differently with every vine cut, so its runners are scenery — " +
                               "which is exactly the state a mirror and a wick both shipped in");
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
            foreach (var chapter in Chapters)
            {
                var rungs = chapter.Rungs;
                int shut = 0;

                foreach (var rung in rungs)
                {
                    Assert.GreaterOrEqual(rung.Cocoons, shut,
                                          rung.Id + " holds " + rung.Cocoons + " shut in, fewer " +
                                          "than the grove before it — the one dial this chapter " +
                                          "ramps on cannot go backwards");
                    shut = rung.Cocoons;
                }

                Assert.Greater(rungs[rungs.Length - 1].Cocoons, rungs[0].Cocoons,
                               chapter.File + ": the last grove asks for more than the first, or " +
                               "there is no ramp in the chapter at all");
            }
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
            foreach (var rung in Every)
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
            foreach (var rung in Every)
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
            foreach (var chapter in Chapters) TheShippedChapterAuthors(chapter.File, chapter.Rungs);
        }

        static void TheShippedChapterAuthors(string file, Rung[] rungs)
        {
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "StreamingAssets", "Content", "chapters", file));

            Assert.IsTrue(File.Exists(path), "the chapter body is missing: " + path);

            var problems = new List<string>();
            Assert.IsTrue(ContentMapper.TryReadChapter(File.ReadAllText(path), problems,
                                                       out var body),
                          string.Join("\n", problems));

            Assert.AreEqual(rungs.Length, body.Levels.Count,
                            file + " no longer holds the groves this fixture pins");

            for (int i = 0; i < rungs.Length; i++)
            {
                var rung = rungs[i];
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
                CollectionAssert.AreEqual(rung.Runners, shipped.WrittenRunners(),
                                          rung.Id + " (vines)");
                Assert.AreEqual(rung.Colours, shipped.Deal.Written(), rung.Id + " (basket)");
                Assert.AreEqual(rung.Regrow,
                                shipped.Regrow == null ? "" : shipped.Regrow.Written(),
                                rung.Id + " (strip)");
                Assert.IsTrue(shipped.Grows,
                              rung.Id + ": every grove in this chapter is a living one — it " +
                              "falls, it grows, its white flowers are bombs and one flower " +
                              "ripens between taps (invariant 20l)");
                Assert.AreEqual(rung.Spare, rules.Spare, rung.Id + " (spare)");

                Assert.AreEqual(rung.Losable, level.Tuning.HasBudget,
                                rung.Id + ": whether this grove has a fail line at all is a " +
                                "decision (invariant 24), not something to be picked up by " +
                                "omission");

                Assert.AreEqual(0, shipped.Stones,
                                rung.Id + ": old wood is retired from this mode — a barrier can " +
                                "only ever make a chain shorter (`bud_wood` is a spent lesson id)");
            }
        }
    }
}
