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
            // are the half a Thicket rung does not have: none of its ten groves stands an
            // object, and threading eight more positional arguments through every one of those
            // would make the boards — the thing anybody reads this file for — impossible to see.

            /// <summary>Whether grafting is on, and whether a big bunch forges a special.</summary>
            public bool Grafts, Forges;

            /// <summary>Specials dealt already forged, as a second grid, or null.</summary>
            public string[] Specials;

            /// <summary>How many specials stand on the grove as dealt.</summary>
            public int Dealt;

            /// <summary>
            /// What the specials are worth — <c>BudObjectReading</c>, pinned: opening moves that
            /// forge one, and shortest plays that forge one and fire one. Nought fired on a
            /// grove that forges is the state five withdrawn mechanics shipped in.
            /// </summary>
            public int Forgeable, Forged, Fired;

            /// <summary>What kind of move the best opening is: tap or graft.</summary>
            public string BestKind = "tap";

            /// <summary>The second cell of a best opening that is a graft. -1 otherwise.</summary>
            public int BestOther = -1;

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

            public BudLayout Layout() => Grove(Rows, Colours, Regrow, Grafts, Forges, Specials, Id);
        }

        /// <summary>
        /// A grove from the letters an author writes, exactly as <c>BudMode.TryRead</c> builds
        /// one. Shared with <see cref="BudVectorTests"/> and <see cref="BudStageTests"/>, so
        /// there is one answer to how a test turns rows into a board.
        /// </summary>
        internal static BudLayout Grove(string[] rows, string colours, string regrow = null,
                                        bool grafts = false, bool forges = false,
                                        string[] specials = null, string id = "grove")
        {
            Assert.IsTrue(BudDeal.TryParse(colours, out var deal, out string dealError),
                          id + ": " + dealError);

            // A strip may deal blends where a basket may not: a basket is what the player
            // decides with, and a strip is scenery. See BudDeal.TryParse's `pure` argument.
            BudDeal strip = null;
            if (!string.IsNullOrEmpty(regrow))
                Assert.IsTrue(BudDeal.TryParse(regrow, out strip, out string growError,
                                               pure: false),
                              id + ": " + growError);

            int width = rows[0].Replace(" ", "").Length;
            Assert.IsTrue(BudLayout.TryReadRows(rows, width, rows.Length,
                                                out var ground, out var value,
                                                out string error),
                          id + ": " + error);

            Assert.IsTrue(BudLayout.TryReadSpecials(specials, width, rows.Length, ground,
                                                    out var special, out string specialError),
                          id + ": " + specialError);

            return new BudLayout(width, rows.Length, ground, value, deal, strip, grafts, special,
                                 forges);
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
        /// The Tanglewood: five groves that graft and forge. A bunch of five leaves a bolt where
        /// the player tapped and a bunch of eight a sun; a special fires when tapped, when a
        /// bunch takes it in, or when another special's reach hits it (invariant 20m).
        ///
        /// <para>
        /// Every rung is pinned with what its specials are <em>worth</em>: <c>Forgeable</c>,
        /// <c>Forged</c> and <c>Fired</c>. That is the guard this mode did not have when it
        /// shipped a runner, and then a windmill, a firefly, a puffball and a hive — all of
        /// which passed every other check in this repository while paying out as the same
        /// chain on every board they stood on.
        /// </para>
        /// </summary>
        internal static readonly Rung[] Tangle =
        {
            new Rung("b02_firstbolt", "GRB", "RGBYMC",
                     par: 3, ways: 230, careless: 3, nodes: 17427,
                     spare: 5,
                     flowers: 41, cocoons: 15,
                     bestAt: 38, bestBurst: 15, bestWaves: 2, bestFreed: 5,
                     "oOGRMCOo",
                     "GYYMGBCC",
                     "oMBOBYYo",
                     "RMGCRCRR",
                     "oYRBGCBo",
                     "GYOGYORM",
                     "oORCCGOo")
            {
                Grafts = true, Forges = true, Dealt = 1,
                Specials = new[]
                {
                    "........",
                    "........",
                    "........",
                    "......|.",
                    "........",
                    "........",
                    "........",
                },
                Forgeable = 0, Forged = 230, Fired = 230,
                BestKind = "tap", BestOther = -1,
            },
            new Rung("b02_makefive", "GRB", "RGBYMC",
                     par: 3, ways: 240, careless: 4, nodes: 12237,
                     spare: 5,
                     flowers: 41, cocoons: 15,
                     bestAt: 30, bestBurst: 30, bestWaves: 5, bestFreed: 9,
                     "oOCCYCOo",
                     "RMYRBGRB",
                     "oBMOCYCo",
                     "YRCRMYGY",
                     "oMYRMCBo",
                     "CYOYCOBR",
                     "oOMBYCOo")
            {
                Grafts = true, Forges = true, Dealt = 0,
                Forgeable = 1, Forged = 240, Fired = 240,
                BestKind = "graft", BestOther = 31,
            },
            new Rung("b02_sunspark", "GBR", "RGBYMCW",
                     par: 3, ways: 540, careless: 3, nodes: 17748,
                     spare: 5,
                     flowers: 41, cocoons: 15,
                     bestAt: 36, bestBurst: 22, bestWaves: 3, bestFreed: 4,
                     "oOCGBMOo",
                     "CGRRYYRY",
                     "oGBOCBRo",
                     "MBMRYMBM",
                     "oCYCRYCo",
                     "GCOYMOYG",
                     "oOMGRBOo")
            {
                Grafts = true, Forges = true, Dealt = 1,
                Specials = new[]
                {
                    "........",
                    ".*......",
                    "........",
                    "........",
                    "........",
                    "........",
                    "........",
                },
                Forgeable = 1, Forged = 540, Fired = 540,
                BestKind = "tap", BestOther = -1,
            },
            new Rung("b02_crossfire", "GBR", "RYGMBC",
                     par: 3, ways: 399, careless: 3, nodes: 19318,
                     spare: 5,
                     flowers: 41, cocoons: 15,
                     bestAt: 4, bestBurst: 20, bestWaves: 4, bestFreed: 6,
                     "oOGGYGOo",
                     "CBYRCCGY",
                     "oRMOYYMo",
                     "MGMBGGMG",
                     "oGBYMCRo",
                     "BBOYMORB",
                     "oOGCGGOo")
            {
                Grafts = true, Forges = true, Dealt = 0,
                Forgeable = 1, Forged = 399, Fired = 399,
                BestKind = "graft", BestOther = 12,
            },
            new Rung("b02_stormheart", "GRB", "RGBYMCW",
                     par: 3, ways: 400, careless: 3, nodes: 16718,
                     spare: 5,
                     flowers: 41, cocoons: 15,
                     bestAt: 27, bestBurst: 27, bestWaves: 5, bestFreed: 9,
                     "oOGMCCOo",
                     "BCBMGYCM",
                     "oRGOBMGo",
                     "MRCMBRCY",
                     "oGBYMRYo",
                     "GBOGMORY",
                     "oOYRRGOo")
            {
                Grafts = true, Forges = true, Dealt = 0,
                Forgeable = 2, Forged = 400, Fired = 400,
                BestKind = "graft", BestOther = 28,
            },
            new Rung("b02_sixfold", "BGR", "RGBYMCW",
                     par: 3, ways: 474, careless: 3, nodes: 14855,
                     spare: 4,
                     flowers: 40, cocoons: 16,
                     bestAt: 22, bestBurst: 25, bestWaves: 4, bestFreed: 10,
                     "oOBCMMOo",
                     "GRYBRCRB",
                     "oGMOBCGo",
                     "GBCCMMCR",
                     "oMBOGYYo",
                     "BROGBOGM",
                     "oOGYYROo")
            {
                Grafts = true, Forges = true, Dealt = 0,
                Forgeable = 2, Forged = 474, Fired = 474,
                BestKind = "tap", BestOther = -1,
            },
            new Rung("b02_thundering", "RBG", "RGBYMC",
                     par: 3, ways: 231, careless: 3, nodes: 17433,
                     spare: 4,
                     flowers: 40, cocoons: 16,
                     bestAt: 36, bestBurst: 23, bestWaves: 6, bestFreed: 7,
                     "oOGBYYOo",
                     "BYRORMGB",
                     "OGYMYBGO",
                     "CCRGCBCC",
                     "OGGOGGMO",
                     "MMOCYOBB",
                     "oOMCYMOo")
            {
                Grafts = true, Forges = true, Dealt = 0,
                Forgeable = 1, Forged = 231, Fired = 231,
                BestKind = "tap", BestOther = -1,
            },
            new Rung("b02_sunwell", "RGB", "RGBYMC",
                     par: 3, ways: 105, careless: 3, nodes: 19951,
                     spare: 4,
                     flowers: 40, cocoons: 16,
                     bestAt: 22, bestBurst: 7, bestWaves: 2, bestFreed: 3,
                     "OOGBYBOO",
                     "BGYYGCMB",
                     "oGCOMGBo",
                     "RBBCOMRG",
                     "oMGRCBBo",
                     "MROMCOMM",
                     "OOYBBMOO")
            {
                Grafts = true, Forges = true, Dealt = 0,
                Forgeable = 1, Forged = 105, Fired = 105,
                BestKind = "graft", BestOther = 30,
            },
            new Rung("b02_wildstorm", "RBG", "RYGMBC",
                     par: 3, ways: 313, careless: 3, nodes: 18526,
                     spare: 3,
                     flowers: 40, cocoons: 16,
                     bestAt: 29, bestBurst: 19, bestWaves: 4, bestFreed: 2,
                     "oOCBGGOo",
                     "BBROBRYB",
                     "OYGRYRYO",
                     "RRGBYGMB",
                     "OYYOBGRO",
                     "CCORROMR",
                     "oOCYGBOo")
            {
                Grafts = true, Forges = true, Dealt = 0,
                Forgeable = 3, Forged = 313, Fired = 313,
                BestKind = "tap", BestOther = -1,
            },
            new Rung("b02_stormcrown", "RBG", "RGBYM",
                     par: 3, ways: 136, careless: 3, nodes: 18998,
                     spare: 3,
                     flowers: 40, cocoons: 16,
                     bestAt: 24, bestBurst: 21, bestWaves: 4, bestFreed: 8,
                     "OORBRBOO",
                     "CCBMYGCY",
                     "oGYOCMMo",
                     "BMGGORGY",
                     "oMCRRBGo",
                     "YBOMCOYM",
                     "OOYRRYOO")
            {
                Grafts = true, Forges = true, Dealt = 0,
                Forgeable = 1, Forged = 136, Fired = 136,
                BestKind = "tap", BestOther = -1,
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

                Assert.AreEqual(rung.Dealt, layout.Specials, rung.Id + " (specials dealt)");

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
        public void TheBestOpeningMoveOfEveryGroveIsStillTheOneItWasAuthoredAround()
        {
            foreach (var rung in Every)
            {
                var layout = rung.Layout();
                var move = BudSolver.Opening(layout, out var best);

                Assert.AreEqual(rung.BestKind, move.Kind.ToString().ToLowerInvariant(),
                                rung.Id + " (which kind of move)");
                Assert.AreEqual(rung.BestAt, move.Cell, rung.Id + " (which cell)");
                Assert.AreEqual(rung.BestOther, move.Other, rung.Id + " (which other cell)");
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
        /// The specials on every shipped Tanglewood grove decide something, and the reading is
        /// the one invariant 26g prescribes.
        ///
        /// <para>
        /// <b>This is the guard the mode did not have when five mechanics shipped and were
        /// withdrawn.</b> Each passed every check in this repository — solvable, correctly
        /// par'd, tight <c>ways</c>, every gate green — while paying out as the same chain on
        /// every board it stood on. What catches it is two numbers: whether the board as dealt
        /// lets the player forge a special, and whether any shortest play fires one.
        /// </para>
        /// <para>
        /// Through <c>BudObjectReading</c> rather than worked out here, so this fixture and
        /// <c>BudValidator</c> cannot come to disagree about what "worth something" means — and
        /// so what is pinned below is the <em>shipping</em> answer against the Python mirror's,
        /// which is the comparison invariant 9a is about.
        /// </para>
        /// </summary>
        [Test]
        public void EverySpecialOnEveryShippedGroveDecidesSomething()
        {
            foreach (var rung in Every)
            {
                var layout = rung.Layout();
                var survey = BudSolver.Survey(layout);
                var reading = BudObjectReading.Of(layout, survey);

                Assert.AreEqual(rung.Forgeable, reading.Forgeable, rung.Id + " (opening moves that forge)");
                Assert.AreEqual(rung.Forged, reading.Forged, rung.Id + " (plays that forge)");
                Assert.AreEqual(rung.Fired, reading.Fired, rung.Id + " (plays that fire)");

                if (!layout.Forges)
                {
                    Assert.AreEqual(0, reading.Forged, rung.Id + " forges a special it does not allow");
                    continue;
                }

                Assert.Greater(reading.Fired, 0, rung.Id + ": no shortest play fires a special, " +
                                                 "so on this grove they are decoration");
            }
        }

        /// <summary>
        /// The Tanglewood's satchel only ever tightens, and never below three above par.
        ///
        /// <para>
        /// <b>The chapter's challenge is the satchel, and it is the one dial that can strand a
        /// star band.</b> Par is 3 on every rung, so the two-star line is 5 and a satchel of 5
        /// would make one star unscorable (invariant 22); <c>par + 3</c> is the least a rung may
        /// be dealt, and it is what the last two are dealt. A rung dealt more room than the one
        /// before it is a ramp running backwards.
        /// </para>
        /// </summary>
        [Test]
        public void TheTanglewoodsSatchelTightensAndNeverStrandsABand()
        {
            int spare = int.MaxValue;
            foreach (var rung in Tangle)
            {
                Assert.LessOrEqual(rung.Spare, spare,
                                   rung.Id + " is dealt more room than the rung before it");
                // The two-star line, in the integer arithmetic LevelTuning uses (1.40 in hundredths).
                int silver = (rung.Par * 140 + 99) / 100;
                Assert.Greater(rung.Par + rung.Spare, silver,
                               rung.Id + ": its satchel sits on or under the two-star line");
                spare = rung.Spare;
            }
        }

        /// <summary>
        /// Every Tanglewood grove grafts and forges, and every Thicket grove does neither.
        /// </summary>
        [Test]
        public void TheTanglewoodForgesAndTheThicketDoesNot()
        {
            foreach (var rung in Tangle)
            {
                var layout = rung.Layout();
                Assert.IsTrue(layout.Grafts, rung.Id + " does not graft");
                Assert.IsTrue(layout.Forges, rung.Id + " does not forge");
            }

            foreach (var rung in Thicket)
                Assert.IsFalse(rung.Layout().HasObjects, rung.Id + " has something the Thicket " +
                                                          "never had");
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
            // The Thicket alone: the Tanglewood's five all carry fifteen shut in, and what ramps
            // there is what is dealt — a bolt on rung one, a sun on rung three, nothing after.
            foreach (var chapter in Chapters)
            {
                if (chapter.Rungs != Thicket) continue;
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
                BudSolver.Opening(layout, out var opening);
                int best = opening.Waves;

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
                CollectionAssert.AreEqual(rung.Specials, shipped.WrittenSpecials(),
                                          rung.Id + " (specials dealt)");
                Assert.AreEqual(rung.Grafts, shipped.Grafts, rung.Id + " (grafts)");
                Assert.AreEqual(rung.Forges, shipped.Forges, rung.Id + " (forges)");
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
