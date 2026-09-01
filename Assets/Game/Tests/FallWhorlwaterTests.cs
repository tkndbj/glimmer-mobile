using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Whorlwater, Lightfall's third chapter: that its ten wells still ask what they were authored
    /// to ask, and — the case that matters most here —
    /// <see cref="EveryWellsWhorlsActuallyDecideSomething"/>.
    ///
    /// <para>
    /// <b>That one exists because this chapter shipped two mechanics that decided nothing, and
    /// every fixture passed both times.</b> The first cut brought a <em>mirror</em> that turned a
    /// lens's beam ninety degrees; the second brought a <em>wick</em> that washed one authored
    /// colour into the four cells beside it. Both were solvable, correctly par'd, <c>ways</c> was
    /// tight, <c>greedy</c> lost, the vectors agreed and the ladder held — and both came back from
    /// one session of play as the lens again. <b>A decoration passes every other reading this
    /// repository takes.</b> So <see cref="FallBoard.Kindled"/> counts merges that reached white
    /// along a shortest line, and a board where that is nought is a board the whorls are
    /// decorating. Invariant 26h.
    /// </para>
    /// <para>
    /// <b>Pinned twice over, exactly as the other two chapters are.</b>
    /// <see cref="TheLadderStillMeasuresWhatItWasAuthoredFor"/> runs the shipped solver against
    /// the numbers the sweep measured, and <see cref="TheShippedChapterAuthorsExactlyThisLadder"/>
    /// proves the content file still holds those boards. Either half alone is half a guard.
    /// </para>
    /// </summary>
    public sealed class FallWhorlwaterTests
    {
        /// <summary>One authored rung: its board, its procession, and what that well measured.</summary>
        sealed class Rung
        {
            public readonly string Id, Deal;
            public readonly string[] Rows;
            public readonly int Par, Ways, Greedy, Motes, Headroom, Lenses, Whorls;
            public readonly int Fused, Kindled;

            public Rung(string id, string deal, int par, int ways, int greedy, int motes,
                        int headroom, int lenses, int whorls, int fused, int kindled,
                        params string[] rows)
            {
                Id = id;
                Deal = deal;
                Par = par;
                Ways = ways;
                Greedy = greedy;
                Motes = motes;
                Headroom = headroom;
                Lenses = lenses;
                Whorls = whorls;
                Fused = fused;
                Kindled = kindled;
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
        /// Whorlwater, as authored. Regenerate with <c>Tools/chapters/f03_whorlwater.py</c>, which
        /// prints this table.
        /// </summary>
        static readonly Rung[] Ladder =
        {
            new Rung("f03_first_whorl", "RGB", 2, 1, 4, 4, 4, 0, 1, 1, 1,
                     ".....",
                     ".....",
                     ".....",
                     ".....",
                     ".....",
                     "CY@C."),

            new Rung("f03_make_the_pair", "RGBB", 3, 2, -1, 6, 3, 0, 1, 1, 1,
                     "......",
                     "......",
                     "......",
                     "......",
                     ".CC...",
                     ".CM@B."),

            new Rung("f03_the_span", "GBR", 3, 1, -1, 9, 2, 0, 1, 1, 1,
                     "......",
                     "......",
                     "......",
                     ".Y...C",
                     ".Y...C",
                     ".YY@YC"),

            new Rung("f03_buried", "BRGB", 3, 1, -1, 10, 2, 0, 1, 1, 1,
                     "......",
                     "......",
                     "......",
                     "...Y..",
                     ".M.Y..",
                     ".MMM..",
                     ".MM@M."),

            new Rung("f03_two_shores", "GBR", 5, 6, -1, 17, 3, 0, 1, 1, 1,
                     ".......",
                     ".......",
                     ".......",
                     ".......",
                     "YYY..YY",
                     "YYY.MMM",
                     "YYY.G@B"),

            new Rung("f03_twin_mouths", "GBR", 5, 4, -1, 18, 3, 0, 2, 1, 1,
                     ".......",
                     ".......",
                     ".......",
                     ".......",
                     "CCC.YYY",
                     "CCC.YYY",
                     "C@R.R@G"),

            new Rung("f03_through_the_glass", "BRGB", 4, 9, 15, 23, 3, 1, 1, 1, 1,
                     ".......",
                     ".......",
                     ".......",
                     ".......",
                     "YYY..YY",
                     "YYY.YYY",
                     "YYY.YYY",
                     "YOY.Y@C"),

            new Rung("f03_the_deep_draw", "GBBR", 5, 12, -1, 25, 2, 0, 1, 1, 1,
                     ".......",
                     ".......",
                     ".......",
                     "MMM....",
                     "MMM..C.",
                     "MMM.CCC",
                     "MMM.CCC",
                     "C@C.CCC"),

            new Rung("f03_every_light", "GBBR", 5, 16, -1, 24, 3, 1, 2, 2, 2,
                     ".......",
                     ".......",
                     ".......",
                     ".......",
                     "MMM.YYY",
                     "MMM.YYY",
                     "MMM.OMM",
                     "M@B.G@M"),

            new Rung("f03_whorlwater", "BRGG", 5, 12, -1, 26, 2, 0, 2, 2, 2,
                     ".......",
                     ".......",
                     ".......",
                     ".Y...Y.",
                     "MMM.MMM",
                     "MMM.MMM",
                     "MMM.YYY",
                     "C@Y.G@Y"),
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
                Assert.AreEqual(rung.Lenses, layout.Lenses, rung.Id + " lenses standing");
                Assert.AreEqual(rung.Whorls, layout.Whorls, rung.Id + " whorls standing");
                Assert.AreEqual(rung.Headroom, layout.Headroom, rung.Id + " headroom");
            }
        }

        // ------------------------------------------------------------------ the whorl
        /// <summary>
        /// <b>The claim this chapter exists for, and the one two mechanics before it failed.</b> A
        /// whorl earns its place only if the pair it draws in <em>completes</em> something no
        /// single drop could have — so every shortest solution is replayed here and the merges are
        /// counted. Nought kindled is the answer that condemns a board.
        ///
        /// <para>
        /// Note what this is not: it is not "is the board solvable", "is par right" or "is
        /// <c>ways</c> tight". A mirror and then a wick passed all three on ten boards apiece and
        /// were still deciding nothing. This is the difference between a mechanic and a
        /// decoration, stated as an integer.
        /// </para>
        /// <para>
        /// And it is the <em>strict</em> reading. <c>fused</c> counts whorls that drew in a pair;
        /// <c>kindled</c> counts those whose union reached white. Two yellows drawn together make
        /// a yellow — a tidier board, deciding nothing — where a yellow and a blue make a burst
        /// the player arranged and could not have bought with any single drop.
        /// </para>
        /// </summary>
        [Test]
        public void EveryWellsWhorlsActuallyDecideSomething()
        {
            foreach (var rung in Ladder)
            {
                Assert.Greater(rung.Whorls, 0,
                               rung.Id + " stands no whorl at all, in the chapter about whorls");

                var layout = rung.Layout();
                Merges(layout, out int fused, out int kindled);

                Assert.AreEqual(rung.Fused, fused, rung.Id + " merges");
                Assert.AreEqual(rung.Kindled, kindled, rung.Id + " merges reaching white");

                Assert.Greater(kindled, 0,
                    rung.Id + " never merges a pair that reaches white, so every whorl on it " +
                    "could be replaced with bare ground and the board would still play — which " +
                    "is exactly what was wrong with the two mechanics this chapter replaced");
            }
        }

        /// <summary>
        /// Replays <em>every</em> shortest solution and reports the best it makes of the whorls.
        ///
        /// <para>
        /// <b>Over all of them rather than the first one found, and that is not tidiness.</b>
        /// <c>ways</c> is rarely one, so whichever winning line a breadth-first walk happens to
        /// reach first is arbitrary among several — and an author tuning a board against an
        /// arbitrary one is tuning against a coin toss. The claim being made is that the best
        /// shortest play uses the whorls, which is the claim a player can actually meet.
        /// </para>
        /// <para>
        /// It mirrors <c>fall.best_merges</c> exactly, which is why both are ranked on
        /// <c>kindled</c> first and <c>fused</c> second (invariant 9a — the two copies have to
        /// agree about which line is "best", not only about how to count one).
        /// </para>
        /// </summary>
        static void Merges(FallLayout layout, out int fused, out int kindled)
        {
            fused = 0;
            kindled = 0;

            var survey = FallSolver.Survey(layout);
            if (!survey.Proved || survey.Par < 1) return;

            var frontier = new List<FallBoard> { new FallBoard(layout) };

            for (int depth = 0; depth < survey.Par; depth++)
            {
                int colour = layout.Deal.At(depth);
                var next = new List<FallBoard>();

                foreach (var board in frontier)
                    for (int x = 0; x < layout.Width; x++)
                    {
                        if (!board.CanDrop(colour, x)) continue;

                        var child = board.Fork();
                        child.Drop(colour, x);

                        if (child.Flooded) continue;

                        if (child.IsEmpty)
                        {
                            if (child.Kindled > kindled ||
                                (child.Kindled == kindled && child.Fused > fused))
                            {
                                fused = child.Fused;
                                kindled = child.Kindled;
                            }
                            continue;
                        }

                        next.Add(child);
                    }

                frontier = next;
                if (frontier.Count == 0) break;
            }
        }

        /// <summary>
        /// No whorl in this chapter stands against a wall.
        ///
        /// <para>
        /// The one reading about this mechanic that is <em>exact</em> rather than geometry:
        /// gravity only ever moves a whorl down, so the two columns it can draw from are the two
        /// it is authored between, whatever the well collapses into. One against a wall has one
        /// side for its whole life and can never merge a pair. <c>FallValidator</c> warns about
        /// it; a chapter built on the mechanic should have none at all.
        /// </para>
        /// </summary>
        [Test]
        public void NoWhorlHereStandsAgainstAWall()
        {
            foreach (var rung in Ladder)
            {
                var layout = rung.Layout();

                for (int at = 0; at < layout.Count; at++)
                {
                    if (!FallCell.IsWhorl(layout.At(at))) continue;

                    int x = at % layout.Width;
                    Assert.IsTrue(x > 0 && x < layout.Width - 1,
                        rung.Id + " stands a whorl in column " + x + ", which has a wall on one " +
                        "side for the whole of the run and can therefore never merge a pair");
                }
            }
        }

        // ------------------------------------------------------------------ the chapter's shape
        /// <summary>
        /// The ladder's spine is how much is standing in the well, because that is what a player
        /// has to plan around and it is the dial this chapter's difficulty actually rides on.
        ///
        /// <para>
        /// <b>Par is not the spine and must not be</b> — it is length, and it wanders here on
        /// purpose (invariant 26e's neighbour: the fail line is a count of drops, so a longer
        /// board is not a tighter one). The opening is four motes and the finale twenty-six.
        /// </para>
        /// <para>
        /// Stated as a shape rather than as a rung-by-rung inequality, which is how the other two
        /// chapters state theirs. A chapter that may never step sideways is a chapter whose boards
        /// are chosen by the assertion rather than by what they teach.
        /// </para>
        /// </summary>
        [Test]
        public void TheChapterFillsUpAsItGoesOn()
        {
            for (int i = 1; i < Ladder.Length; i++)
                Assert.GreaterOrEqual(Ladder[i].Motes, Ladder[i - 1].Motes - 2,
                    Ladder[i].Id + " stands " + Ladder[i].Motes + " mote(s) against " +
                    Ladder[i - 1].Id + "'s " + Ladder[i - 1].Motes + ", which is more than a " +
                    "step sideways");

            Assert.LessOrEqual(Ladder[0].Motes, 5,
                               "the opening well should carry exactly one idea");

            Assert.GreaterOrEqual(Ladder[Ladder.Length - 1].Motes, Ladder[0].Motes * 5,
                                  "the finale should be a different kind of board from the " +
                                  "opening, not a slightly bigger version of it");
        }

        /// <summary>
        /// And the room to be wrong narrows. Headroom is the dial that makes every individual
        /// drop frightening, because a wasted mote costs a row as well as a mote.
        /// </summary>
        [Test]
        public void HeadroomNarrowsAsTheChapterGoesOn()
        {
            Assert.AreEqual(4, Ladder[0].Headroom, "the opening well is roomy on purpose");

            for (int i = 0; i < Ladder.Length / 2; i++)
                Assert.GreaterOrEqual(Ladder[i].Headroom, 2,
                    Ladder[i].Id + " is in the first half and already one careless drop from the " +
                    "brim, which is a finale's job");

            Assert.AreEqual(2, Ladder[Ladder.Length - 1].Headroom,
                            "and the finale is the tightest the mode goes");
        }

        /// <summary>
        /// <b>A player who never looks ahead clears none of these but the first.</b> This is the
        /// mode's third chapter, so the teaching is behind us — except on the one board that is
        /// teaching, where thoughtlessness is <em>supposed</em> to work: that is what meeting a
        /// verb looks like.
        /// </summary>
        [Test]
        public void APlayerWhoNeverLooksAheadClearsOnlyTheOpeningWell()
        {
            var opening = FallSolver.Survey(Ladder[0].Layout());
            Assert.IsTrue(opening.Greedy >= 0 &&
                          opening.Greedy <= Ladder[0].Par + FallRules.DefaultSpare,
                "the first board of a mechanic has to be clearable by somebody who has not " +
                "worked out what it does yet");

            for (int i = 1; i < Ladder.Length; i++)
            {
                var rung = Ladder[i];
                var survey = FallSolver.Survey(rung.Layout());
                int supply = rung.Par + FallRules.DefaultSpare;

                Assert.IsTrue(survey.Greedy < 0 || survey.Greedy > supply,
                    rung.Id + " is cleared in " + survey.Greedy + " drops against a supply of " +
                    supply + " by always taking the biggest burst going, which is not what a " +
                    "mode's third chapter is for");
            }
        }

        /// <summary>Invariant 5d, counted: no well here is cleared by almost any tidy play.</summary>
        [Test]
        public void NoWellHereCanBeClearedByAlmostAnything()
        {
            foreach (var rung in Ladder)
                Assert.LessOrEqual(rung.Ways, 16,
                    rung.Id + " has " + rung.Ways + " shortest solutions, so the procession and " +
                    "the colours are deciding very little");
        }

        /// <summary>Invariant 26c: a procession that cannot supply a channel strands a mote.</summary>
        [Test]
        public void EveryProcessionCarriesAllThreeChannels()
        {
            foreach (var rung in Ladder)
            {
                Assert.IsTrue(FallDeal.TryParse(rung.Deal, out var deal, out string why),
                              rung.Id + ": " + why);

                Assert.AreEqual(Energy.All, deal.Channels,
                                rung.Id + " never deals every channel, so a drop onto bare " +
                                "ground could make a mote nothing could ever finish");
            }
        }

        /// <summary>
        /// The player's device runs this same search when somebody opens the level, so a well
        /// that is expensive to prove is a pause on the way in (invariant 26d).
        /// </summary>
        [Test]
        public void EveryWellIsCheapEnoughToProveOnAPhone()
        {
            foreach (var rung in Ladder)
            {
                var survey = FallSolver.Survey(rung.Layout());

                Assert.LessOrEqual(survey.Nodes, 40_000,
                    rung.Id + " costs " + survey.Nodes + " positions to prove, above the 40,000 " +
                    "a level is expected to cost");
            }
        }

        /// <summary>Invariant 26e: a well forgives two mistakes and a little, wherever it is.</summary>
        [Test]
        public void EveryWellForgivesTwoMistakes()
        {
            foreach (var rung in Ladder)
            {
                var tuning = new LevelTuning(() => rung.Par, 0f, 0f, 0f, FallRules.DefaultSpare);

                Assert.AreEqual(rung.Par + FallRules.DefaultSpare, tuning.MoveBudget,
                    rung.Id + " is dealt " + tuning.MoveBudget + " motes against a par of " +
                    rung.Par + ", so its room to err is not the count every well gets");
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
                "f03_whorlwater.json"));

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

                // Through a local rather than off the rules each time, for `compile.py`'s reason.
                var shipped = well.Layout;

                Assert.AreEqual(authored.Width, shipped.Width, rung.Id + " width");
                Assert.AreEqual(authored.Height, shipped.Height, rung.Id + " height");
                Assert.AreEqual(rung.Deal, shipped.Deal.Written(), rung.Id + " procession");

                for (int cell = 0; cell < authored.Count; cell++)
                    Assert.AreEqual(authored.At(cell), shipped.At(cell),
                                    rung.Id + " cell " + cell + " differs from the sweep's board");
            }
        }
    }
}
