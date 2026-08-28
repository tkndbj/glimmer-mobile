using System;
using System.IO;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The contract between the shipping Groovekeeper rules and the Python mirror that proves
    /// content without Unity.
    ///
    /// <para>
    /// <b>Invariant 9a, for a board rule.</b> Groovekeeper's rules exist twice — here in
    /// <c>KeeperBoard</c> and <c>KeeperSolver</c>, and in <c>Tools/verify/keeper.py</c>, which is
    /// what <c>content.py</c> and the chapter script run because they have no Unity. Neither can
    /// call the other, so the only thing that can stop them drifting is a file both are held to:
    /// this test runs <c>keeper-vectors.json</c> through the C# copy and <c>content.py</c> runs
    /// the same file through the Python one.
    /// </para>
    /// <para>
    /// Every case is a rule somebody could plausibly get wrong in one copy and not the other:
    /// the bloom test itself, the ceiling on a flourish, the "was it already blooming" reading
    /// that the obvious implementation gets backwards, stone, a heartbed's refusal, a board
    /// proved unopenable, and a pocket only a prism can open.
    /// </para>
    /// </summary>
    public sealed class KeeperVectorTests
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
            public string tiles;

            /// <summary>Fewest tiles that open every bed, or nought when nothing does.</summary>
            public int par;

            /// <summary>How many different grooves of exactly par tiles win. Invariant 5d.</summary>
            public int ways;

            /// <summary>Tiles a player who never looks ahead spends, or -1 when they never finish.</summary>
            public int greedy;

            /// <summary>Whether the search finished rather than running out of budget.</summary>
            public bool proved;

            public int beds;
            public int heartbeds;
            public int room;
            public int sprigs;
        }

        static string VectorPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "verify",
                                          "keeper-vectors.json"));

        static VectorFile Load()
        {
            Assert.IsTrue(File.Exists(VectorPath), "keeper-vectors.json is missing: " + VectorPath);

            var file = JsonUtility.FromJson<VectorFile>(File.ReadAllText(VectorPath));

            Assert.IsNotNull(file, "keeper-vectors.json could not be read");
            Assert.IsNotNull(file.cases, "keeper-vectors.json has no cases");
            Assert.Greater(file.cases.Length, 0, "keeper-vectors.json has no cases");

            return file;
        }

        static KeeperLayout Layout(VectorCase test)
        {
            Assert.IsTrue(KeeperDeal.TryParse(test.tiles, out var deal, out string dealError),
                          test.name + ": " + dealError);

            int width = test.rows[0].Replace(" ", "").Length;
            Assert.IsTrue(KeeperLayout.TryReadRows(test.rows, width, test.rows.Length,
                                                   out var ground, out var wants, out var sprigs,
                                                   out string error),
                          test.name + ": " + error);

            return new KeeperLayout(width, test.rows.Length, ground, wants, sprigs, deal);
        }

        [Test]
        public void TheShippingRuleAgreesWithEveryVector()
        {
            var file = Load();

            foreach (var test in file.cases)
            {
                var layout = Layout(test);
                var survey = KeeperSolver.Survey(layout);

                string why = test.name + " — " + test.why;

                Assert.AreEqual(test.proved, survey.Proved, why + " (proved)");
                Assert.AreEqual(test.par, survey.Par, why + " (par)");
                Assert.AreEqual(test.beds, layout.Beds, why + " (beds)");
                Assert.AreEqual(test.heartbeds, layout.Heartbeds, why + " (heartbeds)");
                Assert.AreEqual(test.room, layout.Room, why + " (room)");
                Assert.AreEqual(test.sprigs, layout.Sprigs, why + " (sprigs)");

                // Ways is only meaningful where there is an answer to count grooves to.
                if (test.par > 0)
                {
                    Assert.AreEqual(test.ways, survey.Ways, why + " (ways)");

                    int budget = test.par + Content.KeeperRules.DefaultSpare;
                    Assert.AreEqual(test.greedy, KeeperSolver.Greedy(layout, budget),
                                    why + " (greedy)");
                }
            }
        }

        /// <summary>
        /// A vector file that has quietly lost its teeth is worse than none: it passes, it is
        /// printed beside the word "ok", and nothing says the rule stopped being checked. So the
        /// set is held to covering the shapes it exists for.
        /// </summary>
        [Test]
        public void TheVectorsStillCoverWhatTheyWereWrittenFor()
        {
            var file = Load();

            bool flourish = false, unopenable = false, heartbed = false, stone = false,
                 prism = false, single = false;

            foreach (var test in file.cases)
            {
                var layout = Layout(test);
                var survey = KeeperSolver.Survey(layout);

                if (layout.Beds >= KeeperFlourish.Most - 1) flourish = true;
                if (survey.Proved && survey.Par == 0) unopenable = true;
                if (layout.Heartbeds > 0) heartbed = true;
                if (layout.Room < layout.Count) stone = true;
                if (layout.Deal.Prisms > 0) prism = true;
                if (survey.Par > 0 && survey.Ways == 1) single = true;
            }

            Assert.IsTrue(flourish, "no case with four beds, so nothing here would notice a " +
                                    "planting that stopped reaching its neighbours");
            Assert.IsTrue(unopenable, "no case that is proved unopenable, so nothing here would " +
                                      "notice a search that reported every board as timed out");
            Assert.IsTrue(heartbed, "no case with a heartbed, so nothing here would notice the " +
                                    "colour refusal going away");
            Assert.IsTrue(stone, "no case with stone on it, so nothing here would notice stone " +
                                 "becoming plantable");
            Assert.IsTrue(prism, "no case dealing a prism, so nothing here would notice a prism " +
                                 "stopping carrying all three");
            Assert.IsTrue(single, "no case with exactly one shortest answer, so nothing here " +
                                  "would notice the search counting grooves it should not");
        }
    }
}
