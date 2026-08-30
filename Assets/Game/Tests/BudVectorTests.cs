using System;
using System.Collections.Generic;
using System.IO;
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
    /// most.</b> Two copies can agree exactly about how many taps a grove costs and still
    /// disagree about the chain — how far it ran, how many flowers it washed on the way, whether
    /// a cocoon took one crack or four. Par would never say so; a tap-for-tap replay does.
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

            public int par;
            public int ways;
            public int nodes;
            public bool proved;
            public int careless;

            public int flowers;
            public int cocoons;
            public int tough;
            public int stones;

            /// <summary>The best chain available on the opening board.</summary>
            public int bestWaves;
            public int bestBurst;
            public int bestFreed;

            public VectorBeat[] beats;
        }

        [Serializable]
        public sealed class VectorBeat
        {
            public int tap;
            public bool allowed;

            public int burst;
            public int waves;
            public int freed;
            public int cracked;

            public int flowersLeft;
            public int shut;
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
        {
            int width = test.rows[0].Replace(" ", "").Length;

            Assert.IsTrue(BudDeal.TryParse(test.colours, out var deal, out string dealError),
                          test.name + ": " + dealError);

            Assert.IsTrue(BudLayout.TryReadRows(test.rows, width, test.rows.Length,
                                                out var ground, out var value, out string error),
                          test.name + ": " + error);

            return new BudLayout(width, test.rows.Length, ground, value, deal);
        }

        /// <summary>
        /// The best chain the opening board has in it, with the first colour in hand. Mirrors
        /// <c>bud.biggest</c>, which walks the same cells in the same order — and the order is
        /// part of the contract, because two taps can tie.
        /// </summary>
        static BudChainResult Biggest(BudLayout layout)
        {
            var board = new BudBoard(layout);
            var best = BudChainResult.Nothing;
            int colour = layout.Deal.At(0);

            for (int i = 0; i < layout.Count; i++)
            {
                if (!board.CanTap(i, colour)) continue;

                var chain = board.Preview(i, colour);
                if (chain.Waves > best.Waves
                    || (chain.Waves == best.Waves && chain.Burst > best.Burst))
                    best = chain;
            }

            return best;
        }

        [Test]
        public void TheShippingRuleAgreesWithEveryVector()
        {
            var file = Load();

            foreach (var test in file.cases)
            {
                var layout = Layout(test);
                var survey = BudSolver.Survey(layout);
                var best = Biggest(layout);

                string why = test.name + " — " + test.why;

                Assert.AreEqual(test.proved, survey.Proved, why + " (proved)");
                Assert.AreEqual(test.par, survey.Par, why + " (par)");
                Assert.AreEqual(test.flowers, layout.Flowers, why + " (flowers)");
                Assert.AreEqual(test.cocoons, layout.Cocoons, why + " (cocoons)");
                Assert.AreEqual(test.tough, layout.ToughCocoons, why + " (tough)");
                Assert.AreEqual(test.stones, layout.Stones, why + " (stones)");

                Assert.AreEqual(test.bestWaves, best.Waves, why + " (bestWaves)");
                Assert.AreEqual(test.bestBurst, best.Burst, why + " (bestBurst)");
                Assert.AreEqual(test.bestFreed, best.Freed, why + " (bestFreed)");

                if (test.par > 0)
                {
                    Assert.AreEqual(test.ways, survey.Ways, why + " (ways)");

                    int budget = test.par + Content.BudRules.DefaultSpare;
                    Assert.AreEqual(test.careless, BudSolver.Careless(layout, budget),
                                    why + " (careless)");
                }
            }
        }

        [Test]
        public void EveryRecordedPlayRunsTapForTapTheWayTheVectorsSayItDoes()
        {
            var file = Load();
            var pulses = new List<BudPulse>(64);
            var washes = new List<BudWash>(64);

            foreach (var test in file.cases)
            {
                if (test.beats == null || test.beats.Length == 0) continue;

                var layout = Layout(test);
                var board = new BudBoard(layout);
                int spent = 0;

                for (int nth = 0; nth < test.beats.Length; nth++)
                {
                    var beat = test.beats[nth];
                    string why = $"{test.name} — tap {nth + 1}";

                    int colour = layout.Deal.At(spent);
                    bool allowed = board.CanTap(beat.tap, colour);
                    Assert.AreEqual(beat.allowed, allowed, why + " (allowed)");

                    var chain = allowed ? board.Tap(beat.tap, colour, pulses, washes)
                                        : BudChainResult.Nothing;
                    if (!allowed) { pulses.Clear(); washes.Clear(); }
                    if (allowed) spent++;

                    Assert.AreEqual(beat.burst, chain.Burst, why + " (burst)");
                    Assert.AreEqual(beat.waves, chain.Waves, why + " (waves)");
                    Assert.AreEqual(beat.freed, chain.Freed, why + " (freed)");
                    Assert.AreEqual(beat.cracked, chain.Cracked, why + " (cracked)");
                    Assert.AreEqual(beat.flowersLeft, board.Flowers, why + " (flowersLeft)");
                    Assert.AreEqual(beat.shut, board.Shut, why + " (shut)");

                    // The pulses are what the view animates, so a chain that reported thirteen
                    // bursts and handed back four would draw less than a third of what happened.
                    int bursts = 0, frees = 0;
                    foreach (var pulse in pulses) { if (pulse.Freed) frees++; else bursts++; }

                    Assert.AreEqual(chain.Burst, bursts, why + " (pulses burst)");
                    Assert.AreEqual(chain.Freed, frees, why + " (pulses freed)");
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

            foreach (var test in file.cases)
            {
                var layout = Layout(test);
                var survey = BudSolver.Survey(layout);

                if (test.bestWaves >= 3) chain = true;
                if (survey.Par == 0) unfinishable = true;
                if (survey.Proved && survey.Par == 0 && layout.Flowers > 0) nomove = true;
                if (layout.ToughCocoons > 0) tough = true;
                if (layout.Stones > 0) wood = true;

                if (test.beats != null)
                    foreach (var beat in test.beats)
                        if (!beat.allowed) refused = true;
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
            Assert.IsTrue(refused, "no case where a tap is refused, so nothing here would notice " +
                                   "a colour being spent on a flower it cannot change");
        }
    }
}
