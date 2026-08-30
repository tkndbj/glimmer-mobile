using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Every Lightfall well that ships: that they still ask what they were authored to ask.
    ///
    /// <para>
    /// <b>A well authors a board and a procession and nothing that can be graded</b>, so
    /// everything a player is measured against is a property of <c>FallSolver</c> — par, the two
    /// star lines and the supply the run is dealt. A change to the burst-and-wash rule therefore
    /// silently re-grades the whole chapter. Nothing else here can notice that: every board would
    /// still be solvable, so <c>Validate Content</c> would still pass; the chapter would simply
    /// stop being a ladder, and the first anybody would know is the retention curve.
    /// </para>
    /// <para>
    /// So it is pinned twice over, exactly as the other modes' are.
    /// <see cref="TheLadderStillMeasuresWhatItWasAuthoredFor"/> runs the shipped solver against
    /// the numbers the sweep measured, and <see cref="TheShippedChapterAuthorsExactlyThisLadder"/>
    /// proves the content file still holds those boards. Either half alone is half a guard — the
    /// first would pass while the chapter authored something else entirely, and the second would
    /// pass while the solver measured something else entirely.
    /// </para>
    /// <para>
    /// <b>Par is deliberately not the ladder.</b> It wanders — 2, 2, 3, 5, 4, 5, 6, 6, 6, 6 —
    /// because par is length rather than difficulty, which is the same thing every glade chapter
    /// does with its own. What climbs is what is standing in the well, how little headroom it
    /// leaves, and whether a player who never looks ahead survives it.
    /// </para>
    /// </summary>
    public sealed class FallLadderTests
    {
        /// <summary>One authored rung: its board, its procession, and what that well measured.</summary>
        sealed class Rung
        {
            public readonly string Id, Deal;
            public readonly string[] Rows;
            public readonly int Par, Ways, Greedy, Motes, Headroom;
            public readonly bool Unlosable;

            public Rung(string id, string deal, int par, int ways, int greedy, int motes,
                        int headroom, bool unlosable, params string[] rows)
            {
                Id = id;
                Deal = deal;
                Par = par;
                Ways = ways;
                Greedy = greedy;
                Motes = motes;
                Headroom = headroom;
                Unlosable = unlosable;
                Rows = rows;
            }

            public FallLayout Layout()
            {
                Assert.IsTrue(FallDeal.TryParse(Deal, out var deal, out string dealError),
                              Id + ": " + dealError);

                int width = Rows[0].Length;
                Assert.IsTrue(FallLayout.TryReadRows(Rows, width, Rows.Length, out var fill,
                                                     out string fillError),
                              Id + ": " + fillError);

                return new FallLayout(width, Rows.Length, fill, deal);
            }
        }

        /// <summary>
        /// The Deep Well, as authored. Regenerate with <c>Tools/chapters/f01_lightfall.py</c>,
        /// which prints this table.
        /// </summary>
        static readonly Rung[] Ladder =
        {
            new Rung("f01_first_fall", "GBR", 2, 3, 2, 3, 4, true,
                     "....", "....", "....", "....", "....", "RYY."),

            new Rung("f01_two_lights", "BGRGBR", 2, 2, 2, 7, 3, false,
                     "....", "....", "....", "....", "Y.YY", "YYYM"),

            new Rung("f01_the_kindling", "GBRBRG", 3, 1, 3, 11, 3, false,
                     ".....", ".....", ".....", ".....", "YB...", "YMM.M", "YYMMM"),

            new Rung("f01_narrow_water", "GBRGRBR", 5, 2, -1, 16, 3, false,
                     ".....", ".....", ".....", ".....", "..YY.", ".CRYY", "CBGYM", "CCMRM"),

            new Rung("f01_the_stack", "BGRGBRB", 4, 2, -1, 17, 2, false,
                     ".....", ".....", ".....", ".YRB.", "YYGC.", "YYYCM", "YYGCM"),

            new Rung("f01_deep_cistern", "RGBGBRGR", 5, 1, 13, 20, 3, false,
                     "......", "......", "......", "......", "..MRB.", "M.MRCC", "YMRBCC",
                     "YYYYCC"),

            new Rung("f01_brimming", "BGRGRBGB", 6, 4, 22, 25, 2, false,
                     "......", "......", "......", ".YYCG.", ".YGBB.", ".YYMBC", "YYGMMM",
                     "YYYRMM"),

            new Rung("f01_the_undertow", "BRGBRGRB", 6, 8, -1, 30, 2, false,
                     "......", "......", "......", "..B...", ".RBMMM", "YYGMRM", "YYYGBC",
                     "YYYGCC", "YYYYBC"),

            new Rung("f01_last_ember", "GRBRBGRG", 6, 5, 19, 29, 2, false,
                     "......", "......", "......", ".Y....", "RGYY.Y", "RYYY.Y", "GYGYYY",
                     "CBYYYY", "CCBRYY"),

            new Rung("f01_the_deep_well", "GBRGRBBG", 6, 2, 12, 30, 2, false,
                     "......", "......", "......", ".R.RM.", ".YRRM.", "YGGMM.", "YYRMMM",
                     "YYRMMM", "YYRMMM"),
        };

        // ------------------------------------------------------------------ the measurements
        [Test]
        public void TheLadderStillMeasuresWhatItWasAuthoredFor()
        {
            foreach (var rung in Ladder)
            {
                var layout = rung.Layout();
                var survey = FallSolver.Survey(layout);

                Assert.IsTrue(survey.Proved, rung.Id + " could not be proved");
                Assert.AreEqual(rung.Par, survey.Par, rung.Id + " par");
                Assert.AreEqual(rung.Ways, survey.Ways, rung.Id + " ways");
                Assert.AreEqual(rung.Greedy, survey.Greedy, rung.Id + " greedy");
                Assert.AreEqual(rung.Motes, layout.Motes, rung.Id + " motes standing");
                Assert.AreEqual(rung.Headroom, layout.Headroom, rung.Id + " headroom");
            }
        }

        // ------------------------------------------------------------------ the chapter's shape
        /// <summary>
        /// What is standing in the well is the ladder's spine, and it is the one reading that
        /// means the same thing on a four-wide opening board and a six-by-nine finale.
        /// </summary>
        [Test]
        public void EachWellHoldsAtLeastAsMuchAsTheOneBeforeIt()
        {
            for (int i = 1; i < Ladder.Length; i++)
            {
                // One rung is allowed to be a shade lighter than the one before — a chapter that
                // only ever climbs reads as a treadmill, which is the same argument that keeps
                // par from being monotonic. What is refused is a step backwards.
                Assert.GreaterOrEqual(Ladder[i].Motes, Ladder[i - 1].Motes - 1,
                                      Ladder[i].Id + " holds less than the well before it");
            }

            Assert.Greater(Ladder[Ladder.Length - 1].Motes, Ladder[0].Motes * 5,
                           "the finale should be a different kind of board from the opening, " +
                           "not a bigger version of it");
        }

        [Test]
        public void HeadroomNarrowsAsTheChapterGoesOn()
        {
            Assert.AreEqual(4, Ladder[0].Headroom, "the opening well is roomy on purpose");

            for (int i = 0; i < Ladder.Length; i++)
                Assert.GreaterOrEqual(Ladder[i].Headroom, 2,
                                      Ladder[i].Id + " leaves less than two rows, which is one " +
                                      "careless drop from the brim before anybody has touched it");

            for (int i = Ladder.Length - 4; i < Ladder.Length; i++)
                Assert.AreEqual(2, Ladder[i].Headroom,
                                Ladder[i].Id + " is one of the last four and should be tight");
        }

        /// <summary>
        /// Invariant 5d, counted. Thoughtlessness clears the opening wells — that is what
        /// teaching the verb looks like — and stops working before the chapter is half over.
        /// </summary>
        [Test]
        public void APlayerWhoNeverLooksAheadStopsWinningEarly()
        {
            int lastWin = -1;

            for (int i = 0; i < Ladder.Length; i++)
            {
                var rung = Ladder[i];
                var tuning = new LevelTuning(rung.Par, 0f, 0f, rung.Unlosable ? -1f : 0f);
                bool survives = rung.Greedy >= 0 &&
                                (!tuning.HasBudget || rung.Greedy <= tuning.MoveBudget);

                if (survives) lastWin = i;
            }

            Assert.AreEqual(2, lastWin,
                            "the third well is the last one a player who never looks ahead " +
                            "clears; after that the chapter is asking something");
        }

        /// <summary>
        /// A well nothing rejects is decoration. None of these is anywhere near the validator's
        /// complaint threshold, and the finale is down to two shortest answers.
        /// </summary>
        [Test]
        public void NoWellHereCanBeClearedByAlmostAnything()
        {
            foreach (var rung in Ladder)
                Assert.LessOrEqual(rung.Ways, 8,
                                   rung.Id + " has too many shortest answers to be deciding much");

            Assert.LessOrEqual(Ladder[Ladder.Length - 1].Ways, 2, "the finale should be tight");
        }

        [Test]
        public void OnlyTheFirstWellCannotBeLost()
        {
            Assert.IsTrue(Ladder[0].Unlosable);

            for (int i = 1; i < Ladder.Length; i++)
                Assert.IsFalse(Ladder[i].Unlosable,
                               Ladder[i].Id + " cannot be lost, and exactly one level in this " +
                               "game is allowed that");
        }

        /// <summary>
        /// Invariant 26c. A drop onto bare ground makes a fresh pure mote, so a procession short
        /// of a channel can be walked into a position no amount of play recovers from — and on
        /// the opening well, which has no supply, that is a board that can be neither won nor
        /// lost.
        /// </summary>
        [Test]
        public void EveryProcessionCarriesAllThreeChannels()
        {
            foreach (var rung in Ladder)
            {
                Assert.IsTrue(FallDeal.TryParse(rung.Deal, out var deal, out _));
                Assert.AreEqual(Energy.All, deal.Channels,
                                rung.Id + " deals " + rung.Deal + ", which is short a channel");
            }
        }

        /// <summary>
        /// The player's device runs this search when somebody opens the level, so the chapter's
        /// dearest proof is a real number and worth watching. See invariant 26d.
        /// </summary>
        [Test]
        public void EveryWellIsCheapEnoughToProveOnAPhone()
        {
            int dearest = 0;
            string worst = null;

            foreach (var rung in Ladder)
            {
                var survey = FallSolver.Survey(rung.Layout());
                if (survey.Nodes <= dearest) continue;

                dearest = survey.Nodes;
                worst = rung.Id;
            }

            Assert.Less(dearest, 40_000,
                        worst + " costs " + dearest + " positions to prove, which is where " +
                        "FallValidator starts complaining");
        }

        /// <summary>
        /// The claim that was missing, and the one a player made for us instead: that every well
        /// forgives more than a single mistake.
        ///
        /// <para>
        /// A wasted drop costs one from the supply and leaves a pure mote that still has to be
        /// cooked, so it is worth about two. Four drops of room is therefore two mistakes, and
        /// the whole chapter is held to it — including, and especially, the short wells at the
        /// start, which is where the multiplicative budget it replaced gave the least room and
        /// where the complaint came from.
        /// </para>
        /// </summary>
        [Test]
        public void EveryWellForgivesTwoMistakes()
        {
            const int Mistake = 2;

            foreach (var rung in Ladder)
            {
                var tuning = new LevelTuning(() => rung.Par, 0f, 0f,
                                             rung.Unlosable ? LevelTuning.Unlimited : 0f,
                                             FallRules.DefaultSpare);

                if (!tuning.HasBudget) continue;              // the opening well cannot be lost

                Assert.GreaterOrEqual(tuning.MoveBudget - rung.Par, Mistake * 2,
                                      rung.Id + " leaves " + (tuning.MoveBudget - rung.Par) +
                                      " drop(s) of room, which is one mistake or fewer");

                Assert.Greater(tuning.MoveBudget, tuning.SilverThreshold,
                               rung.Id + " ends the run inside the two-star band, so one star " +
                               "could never be scored on it");
            }
        }

        // ------------------------------------------------------------------ the content file
        /// <summary>
        /// The other half of the guard: that the shipped chapter still holds these boards.
        ///
        /// Needs the Editor, because <c>JsonUtility</c> is a native call. Everything above runs
        /// on every offline compile.
        /// </summary>
        [Test]
        public void TheShippedChapterAuthorsExactlyThisLadder()
        {
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "StreamingAssets", "Content", "chapters",
                "f01_lightfall.json"));

            Assert.IsTrue(File.Exists(path), "the chapter body is missing: " + path);

            var problems = new List<string>();
            Assert.IsTrue(ContentMapper.TryReadChapter(File.ReadAllText(path), problems,
                                                       out var body),
                          string.Join("\n", problems));

            var levels = body.Levels;
            Assert.AreEqual(Ladder.Length, levels.Count, "the chapter has grown or shrunk");

            for (int i = 0; i < Ladder.Length; i++)
            {
                var rung = Ladder[i];
                var level = levels[i];

                Assert.AreEqual(rung.Id, level.Id.Value, "level " + i + " is a different level");

                var well = level.RulesAs<FallRules>();
                Assert.IsNotNull(well, rung.Id + " is not a well");

                var authored = rung.Layout();

                // Through a local rather than off the rules each time. `compile.py` refuses a
                // file that reads `.Layout.` without ever admitting a level's board can be
                // absent, which is a fact about `LevelDefinition` rather than about a well - and
                // the check is coarse on purpose, so this costs one word.
                var shipped = well.Layout;

                Assert.AreEqual(authored.Width, shipped.Width, rung.Id + " width");
                Assert.AreEqual(authored.Height, shipped.Height, rung.Id + " height");
                Assert.AreEqual(rung.Deal, shipped.Deal.Written(), rung.Id + " procession");

                for (int cell = 0; cell < authored.Count; cell++)
                    Assert.AreEqual(authored.At(cell), shipped.At(cell),
                                    rung.Id + " differs at cell " + cell);

                Assert.AreEqual(rung.Unlosable, !level.Tuning.HasBudget,
                                rung.Id + " disagrees about whether it can be lost");
            }
        }
    }
}
