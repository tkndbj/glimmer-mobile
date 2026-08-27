using System;
using System.IO;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The client half of Lightfall's rule contract.
    ///
    /// <para>
    /// The burst-and-wash rule exists twice — <c>FallBoard</c>/<c>FallSolver</c>, which is what
    /// ships, and <c>Tools/verify/fall.py</c>, which is what the offline gate and the authoring
    /// scripts run because they have no Unity anywhere. Two copies of one rule drift, and a
    /// comment saying "keep these in sync" was never going to survive a year of content drops.
    /// So both run <c>Tools/verify/fall-vectors.json</c>: this file proves the C# copy matches
    /// it, and <c>content.py</c> proves the Python one does on every offline run. Invariant 9a's
    /// shape, for a board rule rather than for money — exactly what <c>BoardVectorTests</c> does
    /// for the four-armed tile.
    /// </para>
    /// <para>
    /// <b>The cases are chosen for the places the two could plausibly part.</b> The wash is read
    /// off the positions the bursting motes are standing in, before anything is removed and
    /// before anything falls; a mote that already holds the washed colour is left off the list
    /// rather than washed to no effect; a flooded position is dead rather than losing; and an
    /// unsolvable board has to come back <em>proved</em> rather than timed out. Every one of
    /// those is a place a loose transcription reads plausibly and answers differently.
    /// </para>
    /// <para>
    /// Needs the Editor, because <c>JsonUtility</c> is a native call. <c>FallBoardTests</c> and
    /// <c>FallRunTests</c> carry the same shapes inline for the reason <c>BriarTests</c> does:
    /// those run on every offline compile without anybody opening Unity, so a green run here
    /// means all of it agrees.
    /// </para>
    /// </summary>
    public sealed class FallVectorTests
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
            public string motes;

            /// <summary>Fewest drops that empty it without flooding, or -1 when none does.</summary>
            public int par;

            /// <summary>How many shortest solutions there are. Invariant 5d, counted.</summary>
            public int ways;

            /// <summary>Drops a player who never looks ahead takes, or -1 when they lose.</summary>
            public int greedy;

            /// <summary>Whether the search finished rather than running out of budget.</summary>
            public bool proved;

            public int headroom;
            public int standing;
        }

        static string VectorPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "verify",
                                          "fall-vectors.json"));

        static VectorFile Load()
        {
            Assert.IsTrue(File.Exists(VectorPath), "fall-vectors.json is missing: " + VectorPath);

            var file = JsonUtility.FromJson<VectorFile>(File.ReadAllText(VectorPath));

            Assert.IsNotNull(file, "fall-vectors.json could not be read");
            Assert.IsNotNull(file.cases, "fall-vectors.json has no cases");
            Assert.Greater(file.cases.Length, 0, "fall-vectors.json has no cases");

            return file;
        }

        static FallLayout Layout(VectorCase test)
        {
            Assert.IsTrue(FallDeal.TryParse(test.motes, out var deal, out string dealError),
                          test.name + ": " + dealError);

            int width = test.rows[0].Replace(" ", "").Length;
            Assert.IsTrue(FallLayout.TryReadRows(test.rows, width, test.rows.Length,
                                                 out var fill, out string fillError),
                          test.name + ": " + fillError);

            return new FallLayout(width, test.rows.Length, fill, deal);
        }

        [Test]
        public void TheShippingRuleAgreesWithEveryVector()
        {
            var file = Load();

            foreach (var test in file.cases)
            {
                var layout = Layout(test);
                var survey = FallSolver.Survey(layout);

                string why = test.name + " — " + test.why;

                Assert.AreEqual(test.proved, survey.Proved, why + " (proved)");
                Assert.AreEqual(test.par, survey.Par, why + " (par)");
                Assert.AreEqual(test.greedy, survey.Greedy, why + " (greedy)");
                Assert.AreEqual(test.headroom, layout.Headroom, why + " (headroom)");
                Assert.AreEqual(test.standing, layout.Motes, why + " (motes standing)");

                // Ways is only meaningful where there is an answer to count routes to.
                if (test.par > 0) Assert.AreEqual(test.ways, survey.Ways, why + " (ways)");
            }
        }

        /// <summary>
        /// A vector file that has quietly lost its teeth is worse than none: it passes, it is
        /// printed beside the word "ok", and nothing says the rule stopped being checked. So the
        /// set is held to covering the four shapes it exists for.
        /// </summary>
        [Test]
        public void TheVectorsStillCoverWhatTheyWereWrittenFor()
        {
            var file = Load();

            bool chain = false, wall = false, unsolvable = false, brim = false;

            foreach (var test in file.cases)
            {
                var layout = Layout(test);
                var survey = FallSolver.Survey(layout);

                if (survey.Par == 1 && layout.Motes >= 4) chain = true;
                if (survey.Par > 0 && survey.Ways == 1) wall = true;
                if (survey.Proved && survey.Par < 0) unsolvable = true;
                if (layout.Headroom == 0) brim = true;
            }

            Assert.IsTrue(chain, "no case where one drop clears four or more motes, so nothing " +
                                 "here would notice if the wash stopped chaining");
            Assert.IsTrue(wall, "no case with exactly one shortest solution, so nothing here " +
                                "would notice if the search started counting routes it should not");
            Assert.IsTrue(unsolvable, "no case that is proved unsolvable, so nothing here would " +
                                      "notice a search that reported every board as timed out");
            Assert.IsTrue(brim, "no case standing at the brim, so nothing here would notice the " +
                                "flood clause going away");
        }
    }
}
