using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The contract between the shipping Budburst rules and the Python mirror that proves content
    /// without Unity.
    ///
    /// <para>
    /// <b>Invariant 9a, for a board rule.</b> The mix-and-wash rule exists twice — here in
    /// <c>BudBoard</c> and <c>BudSolver</c>, and in <c>Tools/verify/bud.py</c>, which is what
    /// <c>content.py</c> and the chapter script run. Neither can call the other, so the only
    /// thing that can stop them drifting is a file both are held to: this runs
    /// <c>bud-vectors.json</c> through the C# copy and <c>content.py</c> runs the same file
    /// through the Python one.
    /// </para>
    /// <para>
    /// <b>Every case carries a play as well as a par, and the play is the half that matters
    /// most.</b> Two copies can agree exactly about how many moves a grove costs and still
    /// disagree about the chain — how far it ran, how many flowers it washed on the way, whether
    /// a cocoon took one crack or four. Par would never say so; a move-for-move replay does. And
    /// a play here may be a gust, a graft or a firefly tap as well as a tap, because the second
    /// chapter's moves are exactly the ones the two copies are likeliest to disagree about.
    /// </para>
    /// </summary>
    public sealed class BudVectorTests
    {
        [Serializable]
        public sealed class VectorFile
        {
            public int schemaVersion;
            public VectorCase[] cases;
        }

        [Serializable]
        public sealed class VectorCase
        {
            public string name;
            public string why;
            public string[] rows;

            /// <summary>The basket, in pure colour letters. See <c>BudDeal</c>.</summary>
            public string colours;

            /// <summary>
            /// The strip a living grove grows from. Absent is a <em>still</em> grove — one that
            /// does not fall, does not grow, has no bomb and no creep.
            ///
            /// <b>Both shapes are in this file on purpose.</b> The eight still cases pin the base
            /// rule — mix, burst, wash — in isolation from everything built on top of it, which
            /// is exactly what a vector file is for; the living ones pin what was built on top.
            /// </summary>
            public string regrow;

            /// <summary>Whether grafting is on, and whether a big bunch forges a special.</summary>
            public bool grafts;
            public bool forges;

            /// <summary>Specials dealt already forged, as a second grid. Absent on most cases.</summary>
            public string[] specials;

            public int par;
            public int ways;
            public int nodes;
            public bool proved;
            public int careless;

            public int flowers;
            public int cocoons;
            public int tough;
            public int stones;

            /// <summary>How many specials the grid above deals. Named for the field it counts.</summary>
            public int specialsDealt;

            /// <summary>The best chain available on the opening board, over every move.</summary>
            public int bestWaves;
            public int bestBurst;
            public int bestFreed;

            /// <summary>What the specials are worth. See <c>BudObjectReading</c>.</summary>
            public int forgeable;
            public int forged;
            public int fired;

            public VectorBeat[] beats;
        }

        [Serializable]
        public sealed class VectorBeat
        {
            /// <summary>tap or graft.</summary>
            public string kind;

            /// <summary>The cell for a tap, the first cell for a graft.</summary>
            public int tap;

            /// <summary>The second cell of a graft. -1 otherwise.</summary>
            public int other;

            public bool allowed;

            public int burst;
            public int waves;
            public int freed;
            public int cracked;
            public int forged;
            public int fired;

            public int flowersLeft;
            public int shut;

            /// <summary>Specials standing afterwards, and how many of each kind.</summary>
            public int specialsLeft;
            public int bolt;
            public int sun;
        }

        static string VectorPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "verify",
                                          "bud-vectors.json"));

        static VectorFile Load()
        {
            Assert.IsTrue(File.Exists(VectorPath), "bud-vectors.json is missing: " + VectorPath);

            var file = JsonUtility.FromJson<VectorFile>(File.ReadAllText(VectorPath));

            Assert.IsNotNull(file, "bud-vectors.json could not be read");
            Assert.IsNotNull(file.cases, "bud-vectors.json has no cases");
            Assert.Greater(file.cases.Length, 0, "bud-vectors.json has no cases");

            return file;
        }

        static BudLayout Layout(VectorCase test)
            => BudLadderTests.Grove(test.rows, test.colours, test.regrow, test.grafts, test.forges,
                                    test.specials != null && test.specials.Length > 0 ? test.specials : null);

        static BudMove Move(VectorBeat beat)
            => beat.kind == "graft" ? BudMove.Graft(beat.tap, beat.other) : BudMove.Tap(beat.tap);

        [Test]
        public void TheShippingRuleAgreesWithEveryVector()
        {
            var file = Load();

            foreach (var test in file.cases)
            {
                var layout = Layout(test);
                var survey = BudSolver.Survey(layout);
                BudSolver.Opening(layout, out var best);
                var reading = BudObjectReading.Of(layout, survey);

                string why = test.name + " — " + test.why;

                Assert.AreEqual(test.proved, survey.Proved, why + " (proved)");
                Assert.AreEqual(test.par, survey.Par, why + " (par)");
                Assert.AreEqual(test.flowers, layout.Flowers, why + " (flowers)");
                Assert.AreEqual(test.cocoons, layout.Cocoons, why + " (cocoons)");
                Assert.AreEqual(test.tough, layout.ToughCocoons, why + " (tough)");
                Assert.AreEqual(test.stones, layout.Stones, why + " (stones)");
                Assert.AreEqual(test.specialsDealt, layout.Specials, why + " (specials dealt)");

                Assert.AreEqual(test.bestWaves, best.Waves, why + " (bestWaves)");
                Assert.AreEqual(test.bestBurst, best.Burst, why + " (bestBurst)");
                Assert.AreEqual(test.bestFreed, best.Freed, why + " (bestFreed)");

                Assert.AreEqual(test.forgeable, reading.Forgeable, why + " (forgeable)");

                if (test.par > 0)
                {
                    Assert.AreEqual(test.ways, survey.Ways, why + " (ways)");
                    Assert.AreEqual(test.forged, survey.Forged, why + " (forged)");
                    Assert.AreEqual(test.fired, survey.Fired, why + " (fired)");

                    int budget = test.par + BudRules.DefaultSpare;
                    Assert.AreEqual(test.careless, BudSolver.Careless(layout, budget),
                                    why + " (careless)");
                }
            }
        }

        /// <summary>
        /// Whether a move is legal on a bare board, which is what the mirror asks: the run's
        /// own refusals (an empty satchel, a finished grove) are not part of the rule.
        /// </summary>
        static bool Legal(BudBoard board, BudMove move, int hand)
            => move.Kind == BudMoveKind.Graft ? board.CanGraft(move.Cell, move.Other)
                                              : board.CanTap(move.Cell, hand);

        [Test]
        public void EveryRecordedPlayRunsMoveForMoveTheWayTheVectorsSayItDoes()
        {
            var file = Load();
            var pulses = new List<BudPulse>(64);
            var washes = new List<BudWash>(64);

            foreach (var test in file.cases)
            {
                if (test.beats == null || test.beats.Length == 0) continue;

                var layout = Layout(test);
                var board = new BudBoard(layout);
                int dealt = 0;

                for (int nth = 0; nth < test.beats.Length; nth++)
                {
                    var beat = test.beats[nth];
                    var move = Move(beat);
                    int hand = layout.Deal.At(dealt);
                    string why = $"{test.name} — move {nth + 1} ({move})";

                    bool allowed = Legal(board, move, hand);
                    Assert.AreEqual(beat.allowed, allowed, why + " (allowed)");

                    var chain = BudChainResult.Nothing;
                    pulses.Clear();
                    washes.Clear();
                    if (allowed) dealt += BudRun.Apply(board, move, hand, pulses, out chain, washes);

                    Assert.AreEqual(beat.burst, chain.Burst, why + " (burst)");
                    Assert.AreEqual(beat.waves, chain.Waves, why + " (waves)");
                    Assert.AreEqual(beat.freed, chain.Freed, why + " (freed)");
                    Assert.AreEqual(beat.cracked, chain.Cracked, why + " (cracked)");
                    Assert.AreEqual(beat.forged, chain.Forged, why + " (forged)");
                    Assert.AreEqual(beat.fired, chain.Fired, why + " (fired)");
                    Assert.AreEqual(beat.flowersLeft, board.Flowers, why + " (flowersLeft)");
                    Assert.AreEqual(beat.shut, board.Shut, why + " (shut)");
                    Assert.AreEqual(beat.specialsLeft, board.Specials, why + " (specialsLeft)");

                    int bolts = 0, suns = 0;
                    for (int i = 0; i < board.Count; i++)
                    {
                        if (!board.IsFlower(i)) continue;
                        if (board.SpecialAt(i) == BudSpecial.Bolt) bolts++;
                        if (board.SpecialAt(i) == BudSpecial.Sun) suns++;
                    }
                    Assert.AreEqual(beat.bolt, bolts, why + " (bolts standing)");
                    Assert.AreEqual(beat.sun, suns, why + " (suns standing)");

                    // The pulses are what the view animates, so a chain that reported thirteen
                    // bursts and handed back four would draw less than a third of what happened.
                    int bursts = 0, frees = 0, cracks = 0, forges = 0, fires = 0;
                    foreach (var pulse in pulses)
                    {
                        switch (pulse.Kind)
                        {
                            case BudPulseKind.Freed: frees++; break;
                            case BudPulseKind.Crack: cracks++; break;
                            case BudPulseKind.Forged: forges++; break;
                            case BudPulseKind.Fired: fires++; break;
                            case BudPulseKind.Burst: bursts++; break;
                        }
                    }

                    Assert.AreEqual(chain.Burst, bursts, why + " (pulses burst)");
                    Assert.AreEqual(chain.Freed, frees, why + " (pulses freed)");
                    Assert.AreEqual(chain.Cracked, cracks, why + " (pulses cracked)");
                    Assert.AreEqual(chain.Forged, forges, why + " (pulses forged)");
                    Assert.AreEqual(chain.Fired, fires, why + " (pulses fired)");
                }
            }
        }

        /// <summary>
        /// A vector file that has quietly lost its teeth is worse than none: it passes, it is
        /// printed beside the word "ok", and nothing says the rule stopped being checked.
        /// </summary>
        [Test]
        public void TheVectorsStillCoverWhatTheyWereWrittenFor()
        {
            var file = Load();

            bool chain = false, unfinishable = false, nomove = false;
            bool tough = false, wood = false, refused = false;
            bool bolt = false, sun = false, fired = false, chained = false;
            bool graft = false, snapped = false;

            foreach (var test in file.cases)
            {
                var layout = Layout(test);
                var survey = BudSolver.Survey(layout);

                if (test.bestWaves >= 3) chain = true;
                if (survey.Par == 0) unfinishable = true;
                if (survey.Proved && survey.Par == 0 && layout.Flowers > 0) nomove = true;
                if (layout.ToughCocoons > 0) tough = true;
                if (layout.Stones > 0) wood = true;

                if (test.beats == null) continue;

                foreach (var beat in test.beats)
                {
                    if (!beat.allowed) refused = true;
                    if (beat.kind == "graft" && beat.allowed) graft = true;
                    if (beat.kind == "graft" && !beat.allowed) snapped = true;
                    if (beat.forged > 0 && beat.bolt > 0) bolt = true;
                    if (beat.forged > 0 && beat.sun > 0) sun = true;
                    if (beat.fired == 1) fired = true;
                    if (beat.fired >= 2) chained = true;
                }
            }

            Assert.IsTrue(chain, "no case whose chain runs past two waves, so nothing here would " +
                                 "notice a burst stopping washing its colour outward — which is " +
                                 "the whole mode");
            Assert.IsTrue(unfinishable, "no case that cannot be finished, so nothing here would " +
                                        "notice a search reporting every grove as solved");
            Assert.IsTrue(nomove, "no case with flowers on it and no legal tap in it, so nothing " +
                                  "here would notice 'any flower left' coming back in place of " +
                                  "'any move left' — which is a grove that can be neither won " +
                                  "nor ended");
            Assert.IsTrue(tough, "no case with a two-crack cocoon, so nothing here would notice " +
                                 "one taking every crack of a wave at once");
            Assert.IsTrue(wood, "no case with old wood on it, so nothing here would notice a " +
                                "bunch or a wash starting to cross it");
            Assert.IsTrue(refused, "no case where a move is refused, so nothing here would notice " +
                                   "a colour being spent on a flower it cannot change");
            Assert.IsTrue(bolt, "no case where a bunch of five leaves a bolt standing, so nothing " +
                                "here would notice the forge threshold moving");
            Assert.IsTrue(sun, "no case where a bunch of eight leaves a sun standing, so nothing " +
                               "here would notice a sun being forged at five");
            Assert.IsTrue(fired, "no case where exactly one special fires, so nothing here would " +
                                 "notice a bolt clearing the wrong line");
            Assert.IsTrue(chained, "no case where one special fires another, so nothing here " +
                                   "would notice the chain going away — which is the chapter");
            Assert.IsTrue(graft && snapped, "no case with a graft that works and one that snaps " +
                                            "back, so nothing here would notice the bunch " +
                                            "threshold going away — a graft that makes nothing " +
                                            "is a free colour skip");
        }
    }
}
