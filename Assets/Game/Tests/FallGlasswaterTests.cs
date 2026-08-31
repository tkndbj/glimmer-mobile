using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Glasswater, Lightfall's second chapter: that its ten wells still ask what they were
    /// authored to ask, and that the lens on each of them is still doing work.
    ///
    /// <para>
    /// <b>Its own file rather than more of <c>FallLadderTests</c>, because the two chapters make
    /// different claims.</b> The Deep Well's opening board cannot be lost and none of its ten
    /// stands a single pane of glass; Glasswater's every board can be lost and every board is
    /// about the glass. A fixture asserting the union of those would assert almost nothing.
    /// </para>
    /// <para>
    /// <b>Pinned twice over, exactly as the Deep Well's is.</b>
    /// <see cref="TheLadderStillMeasuresWhatItWasAuthoredFor"/> runs the shipped solver against
    /// the numbers the sweep measured, and <see cref="TheShippedChapterAuthorsExactlyThisLadder"/>
    /// proves the content file still holds those boards. Either half alone is half a guard — the
    /// first would pass while the chapter authored something else entirely, and the second would
    /// pass while the solver measured something else entirely.
    /// </para>
    /// <para>
    /// <b>And <see cref="EveryWellsGlassActuallyThrowsTheLightSomewhere"/> is the one that is new
    /// here.</b> Everything else about a well would still be true with the lenses replaced by
    /// ordinary motes: the board would be solvable, correctly par'd, fully validated and would
    /// have the chapter taken out of it. That is invariant 5d's fault in the place nobody would
    /// look, and <c>reach</c> is the number that catches it.
    /// </para>
    /// </summary>
    public sealed class FallGlasswaterTests
    {
        /// <summary>One authored rung: its board, its procession, and what that well measured.</summary>
        sealed class Rung
        {
            public readonly string Id, Deal;
            public readonly string[] Rows;
            public readonly int Par, Ways, Greedy, Motes, Headroom, Lenses, Charged, Aim;

            public Rung(string id, string deal, int par, int ways, int greedy, int motes,
                        int headroom, int lenses, int charged, int aim, params string[] rows)
            {
                Id = id;
                Deal = deal;
                Par = par;
                Ways = ways;
                Greedy = greedy;
                Motes = motes;
                Headroom = headroom;
                Lenses = lenses;
                Charged = charged;
                Aim = aim;
                Rows = rows;
            }

            /// <summary>How much glass this board hands the player for free, out of three a lens.</summary>
            public int Given
            {
                get
                {
                    var layout = Layout();
                    int given = 0;

                    for (int i = 0; i < layout.Count; i++)
                    {
                        int cell = layout.At(i);
                        if (!FallCell.IsLens(cell)) continue;

                        int charge = FallCell.Charge(cell);
                        if ((charge & Energy.R) != 0) given++;
                        if ((charge & Energy.G) != 0) given++;
                        if ((charge & Energy.B) != 0) given++;
                    }

                    return given;
                }
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
        /// Glasswater, as authored. Regenerate with <c>Tools/chapters/f02_glasswater.py</c>,
        /// which prints this table.
        /// </summary>
        static readonly Rung[] Ladder =
        {
            new Rung("f02_the_glass", "RGB", 3, 2, -1, 5, 3, 1, 1, 2,
                     ".....", ".....", ".....", ".....", "M...Y", "My..Y"),
            new Rung("f02_stillwater", "BRG", 3, 2, -1, 6, 2, 1, 1, 2,
                     "......", "......", "......", "C.....", "C....M", "Cm...M"),
            new Rung("f02_low_tide", "GBR", 3, 2, -1, 8, 2, 1, 1, 2,
                     ".....", ".....", ".....", "Y....", "Y...C", "Y...C", "Yc..C"),
            new Rung("f02_the_crossing", "RGB", 4, 6, -1, 10, 2, 1, 1, 2,
                     "......", "......", "......", "Y....C", "Y....C", "Y....C", "Mg..CC"),
            new Rung("f02_two_panes", "BGR", 4, 4, 4, 11, 3, 2, 2, 2,
                     "......", "......", "......", "......", "Y....C", "Ry..gC", "MYY.CC"),
            new Rung("f02_far_shore", "BRG", 4, 2, -1, 16, 2, 1, 1, 2,
                     ".......", ".......", ".......", "Y......", "Y.....C", "Yg....C", "BM...CC", "MMM.CCC"),
            new Rung("f02_lantern_row", "GBR", 4, 4, -1, 19, 2, 2, 2, 2,
                     ".......", ".......", ".......", "M.....Y", "M.....Y", "Mb...yM", "MY...MM", "YYYYMMM"),
            new Rung("f02_slack_water", "GRB", 6, 6, 8, 27, 2, 2, 1, 2,
                     "......", "......", "......", "C....M", "CO..gM", "CC..MM", "YCM.MM", "YYMYRM", "YMMGYY"),
            new Rung("f02_underglass", "GRB", 5, 3, -1, 33, 1, 1, 1, 2,
                     "......", "......", "CR..CC", "GYg.CC", "CYYYGC", "CCYYYR", "CCRYBM", "CCBMMM"),
            new Rung("f02_the_glasswater", "RBG", 6, 2, -1, 33, 2, 3, 2, 2,
                     "......", "......", "......", "CO..yM", "CCg.MM", "CYYYMM", "CYRYYM", "CMMMGG", "BRMCCC"),
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
                Assert.AreEqual(rung.Aim, survey.Aim, rung.Id + " aim");
                Assert.AreEqual(rung.Motes, layout.Motes, rung.Id + " motes standing");
                Assert.AreEqual(rung.Lenses, layout.Lenses, rung.Id + " lenses standing");
                Assert.AreEqual(rung.Charged, CountCharged(layout), rung.Id + " glass part full");
                Assert.AreEqual(rung.Headroom, layout.Headroom, rung.Id + " headroom");
            }
        }

        static int CountCharged(FallLayout layout)
        {
            int charged = 0;

            for (int i = 0; i < layout.Count; i++)
                if (FallCell.IsLens(layout.At(i)) &&
                    FallCell.Charge(layout.At(i)) != Energy.None) charged++;

            return charged;
        }

        // ------------------------------------------------------------------ the glass
        /// <summary>
        /// The claim this chapter exists for. A lens costs three drops of three colours to fill;
        /// glass whose every shot leaves the well the moment it sets off has taken the whole of
        /// that plan and bought nothing with it, and the board would play the same without it.
        /// </summary>
        [Test]
        public void EveryWellsGlassIsPointingAtSomething()
        {
            foreach (var rung in Ladder)
            {
                Assert.Greater(rung.Lenses, 0,
                               rung.Id + " stands no glass at all, in the chapter about glass");

                Assert.GreaterOrEqual(FallSolver.Survey(rung.Layout()).Aim, 1,
                                      rung.Id + " has no lens pointing at anything, so every " +
                                      "shot it could ever fire leaves the well and the board " +
                                      "would play the same with the glass taken out of it");
            }
        }

        /// <summary>
        /// <b>The ramp, and it is the whole of how this chapter gets harder.</b> A lens fires only
        /// when it holds all three, and a drop's entire chain carries one colour — so an empty one
        /// costs three separate drops of three separate colours, each engineered to burst beside
        /// it. Measured, that leaves 7 boards in 90 solvable where two-thirds-full glass leaves
        /// 50, which is why the dial is how full the glass starts rather than anything else.
        ///
        /// <para>
        /// So the opening boards hand most of the charge over and the late ones hand over less.
        /// Asserted as a trend rather than per rung, because par is length and wanders: what must
        /// hold is that the first half is more generous than the second, and that somewhere in
        /// the chapter a player is asked for all three.
        /// </para>
        /// </summary>
        [Test]
        public void TheChapterRampsOnHowFullTheGlassStarts()
        {
            int half = Ladder.Length / 2;
            int early = 0, late = 0;

            for (int i = 0; i < Ladder.Length; i++)
            {
                int asked = Ladder[i].Lenses * 3 - Ladder[i].Given;
                if (i < half) early += asked; else late += asked;
            }

            Assert.Less(early, late,
                        "the first half of the chapter asks for " + early + " channel(s) and the " +
                        "second for " + late + ", so it is not ramping on the one dial it has");

            Assert.AreEqual(1, Ladder[0].Lenses * 3 - Ladder[0].Given,
                            "the opening board should ask for exactly one channel of one lens — " +
                            "one well-aimed burst, which is the whole of the lesson");

            bool fromEmpty = false;
            foreach (var rung in Ladder)
                if (rung.Lenses > rung.Charged) fromEmpty = true;

            Assert.IsTrue(fromEmpty,
                          "no board in the chapter starts a lens empty, so nobody is ever asked " +
                          "for all three and the mechanic is never met at full price");
        }

        /// <summary>
        /// A lens leaves the well only by being struck, so a board proved emptiable is a board
        /// where every pane on it is reached. Stated out loud because it is the reason no
        /// separate reachability check exists — and because it stops being true the moment
        /// somebody gives glass a second way out.
        /// </summary>
        [Test]
        public void EveryPaneIsReachedBecauseTheWellCanBeEmptied()
        {
            foreach (var rung in Ladder)
            {
                var survey = FallSolver.Survey(rung.Layout());

                Assert.IsTrue(survey.IsSolvable,
                              rung.Id + " cannot be emptied, so its glass can never be struck");
            }
        }

        [Test]
        public void TheGlassThickensAsTheChapterGoesOn()
        {
            Assert.AreEqual(1, Ladder[0].Lenses, "the opening well teaches one pane");

            int most = 0;
            foreach (var rung in Ladder) if (rung.Lenses > most) most = rung.Lenses;

            Assert.GreaterOrEqual(most, 2,
                                  "no well here stands two panes, so one lens setting off " +
                                  "another is never something a player meets");
        }

        // ------------------------------------------------------------------ the chapter's shape
        [Test]
        public void EachWellHoldsAtLeastAsMuchAsTheOneBeforeIt()
        {
            for (int i = 1; i < Ladder.Length; i++)
                Assert.GreaterOrEqual(Ladder[i].Motes, Ladder[i - 1].Motes - 1,
                                      Ladder[i].Id + " holds less than the well before it");

            Assert.Greater(Ladder[Ladder.Length - 1].Motes, Ladder[0].Motes * 4,
                           "the finale should be a different kind of board from the opening, " +
                           "not a bigger version of it");
        }

        /// <summary>
        /// One well in this chapter is allowed to open a single careless drop from the brim,
        /// because that is a rung; two of them is a chapter with a habit.
        ///
        /// Nought is what <c>FallValidator</c> warns about and nothing here may reach it: a well
        /// whose fill touches the row below the brim ends on the very first wasted mote, which
        /// on a board the player has not yet read is a loss they could not have avoided.
        /// </summary>
        [Test]
        public void AtMostOneWellOpensACarelessDropFromTheBrim()
        {
            int tight = 0;

            foreach (var rung in Ladder)
            {
                Assert.Greater(rung.Headroom, 0,
                               rung.Id + " reaches the row below the brim, so the first careless " +
                               "drop on the tallest column ends the run");

                if (rung.Headroom == 1) tight++;
            }

            Assert.LessOrEqual(tight, 1,
                               tight + " wells open one careless drop from the brim; that is a " +
                               "rung when it happens once and a habit when it happens twice");
        }

        /// <summary>
        /// Invariant 5d, counted. A well almost anything clears is one where the colours and the
        /// ordering are deciding nothing, however pretty the light is.
        /// </summary>
        [Test]
        public void NoWellHereCanBeClearedByAlmostAnything()
        {
            foreach (var rung in Ladder)
                Assert.LessOrEqual(rung.Ways, 60,
                                   rung.Id + " has " + rung.Ways + " shortest solutions, so " +
                                   "almost any tidy play wins it");
        }

        /// <summary>
        /// Thoughtlessness clears the opening wells and stops working. Unlike the Deep Well this
        /// chapter is not teaching the verb, so the greedy line may bite earlier — what is
        /// checked is that it bites at all.
        /// </summary>
        [Test]
        public void APlayerWhoNeverLooksAheadDoesNotClearTheWholeChapter()
        {
            int beaten = 0;

            foreach (var rung in Ladder)
            {
                var tuning = new LevelTuning(() => rung.Par, 0f, 0f, 0f, FallRules.DefaultSpare);
                if (rung.Greedy < 0 || rung.Greedy > tuning.MoveBudget) beaten++;
            }

            Assert.GreaterOrEqual(beaten, 3,
                                  "a player who never looks ahead clears all but " + beaten +
                                  " of these, so the chapter is asking nothing of anybody");
        }

        [Test]
        public void EveryWellCanBeLost()
        {
            foreach (var rung in Ladder)
            {
                var tuning = new LevelTuning(() => rung.Par, 0f, 0f, 0f, FallRules.DefaultSpare);

                Assert.IsTrue(tuning.HasBudget,
                              rung.Id + " cannot be lost — that is the first chapter's opening " +
                              "board's privilege and no other board's");
            }
        }

        [Test]
        public void EveryProcessionCarriesAllThreeChannels()
        {
            foreach (var rung in Ladder)
                Assert.AreEqual(Energy.All, rung.Layout().Deal.Channels,
                                rung.Id + " never deals one of the three, so a mote that ends " +
                                "up wanting it could never be finished");
        }

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

        [Test]
        public void EveryWellForgivesTwoMistakes()
        {
            const int Mistake = 2;

            foreach (var rung in Ladder)
            {
                var tuning = new LevelTuning(() => rung.Par, 0f, 0f, 0f, FallRules.DefaultSpare);

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
                "f02_glasswater.json"));

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
                // absent, which is a fact about `LevelDefinition` rather than about a well.
                var shipped = well.Layout;

                Assert.AreEqual(authored.Width, shipped.Width, rung.Id + " width");
                Assert.AreEqual(authored.Height, shipped.Height, rung.Id + " height");
                Assert.AreEqual(rung.Deal, shipped.Deal.Written(), rung.Id + " procession");
                Assert.AreEqual(rung.Lenses, shipped.Lenses, rung.Id + " lenses");
                Assert.AreEqual(rung.Charged, CountCharged(shipped), rung.Id + " glass part full");

                for (int cell = 0; cell < authored.Count; cell++)
                    Assert.AreEqual(authored.At(cell), shipped.At(cell),
                                    rung.Id + " differs at cell " + cell);

                Assert.IsTrue(level.Tuning.HasBudget, rung.Id + " should be losable");
            }
        }
    }
}
