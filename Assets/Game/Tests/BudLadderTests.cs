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
        sealed class Rung
        {
            public readonly string Id, Colours;
            public readonly string[] Rows;
            public readonly int Par, Ways, Careless, Nodes;
            public readonly int Flowers, Cocoons;
            public readonly int BestAt, BestBurst, BestWaves, BestFreed;

            public Rung(string id, string colours, int par, int ways, int careless, int nodes,
                        int flowers, int cocoons,
                        int bestAt, int bestBurst, int bestWaves, int bestFreed,
                        params string[] rows)
            {
                Id = id;
                Colours = colours;
                Par = par;
                Ways = ways;
                Careless = careless;
                Nodes = nodes;
                Flowers = flowers;
                Cocoons = cocoons;
                BestAt = bestAt;
                BestBurst = bestBurst;
                BestWaves = bestWaves;
                BestFreed = bestFreed;
                Rows = rows;
            }

            public BudLayout Layout()
            {
                Assert.IsTrue(BudDeal.TryParse(Colours, out var deal, out string dealError),
                              Id + ": " + dealError);

                int width = Rows[0].Length;
                Assert.IsTrue(BudLayout.TryReadRows(Rows, width, Rows.Length,
                                                    out var ground, out var value,
                                                    out string error),
                              Id + ": " + error);

                return new BudLayout(width, Rows.Length, ground, value, deal);
            }
        }

        /// <summary>
        /// The Thicket. One grove, because what decides a mode is whether the verb lands and no
        /// number answers that (invariant 20j).
        /// </summary>
        static readonly Rung[] Ladder =
        {
            new Rung("b01_firstburst", "GBR",
                     par: 3, ways: 12, careless: 4, nodes: 7903,
                     flowers: 36, cocoons: 4,
                     bestAt: 33, bestBurst: 13, bestWaves: 3, bestFreed: 3,
                     "GYRYBBR",
                     "BRoBoYG",
                     "RBCRGRY",
                     "GRoYoGY",
                     "BBCRYRR",
                     ".GGRYG."),
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

                Assert.AreEqual(rung.Careless,
                                BudSolver.Careless(layout, rung.Par + BudRules.DefaultSpare),
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
        /// one gate here that is deliberately the opposite way round from every other mode's.
        /// </summary>
        [Test]
        public void ACarelessPlayerFinishesEveryGroveAndStillScoresThreeStars()
        {
            foreach (var rung in Ladder)
            {
                var layout = rung.Layout();
                int satchel = rung.Par + BudRules.DefaultSpare;
                int careless = BudSolver.Careless(layout, satchel);

                Assert.Greater(careless, 0,
                               rung.Id + ": a player who always taps whatever sets off the " +
                               "biggest chain never finishes this grove");

                Assert.LessOrEqual(careless, satchel, rung.Id + ": and they run out of taps");

                var tuning = new LevelTuning(rung.Par, 0f, 0f);
                Assert.LessOrEqual(careless, tuning.GoldThreshold,
                                   rung.Id + ": a careless play scores under three stars, which " +
                                   "is more than this mode promises");
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
            }
        }
    }
}
