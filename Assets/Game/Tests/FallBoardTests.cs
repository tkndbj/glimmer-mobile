using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Lightfall's rules: a mote either enriches the top of a stack or heightens it, one that
    /// reaches white bursts, and the burst washes the colour that finished it into whatever is
    /// standing beside it.
    ///
    /// <para>
    /// <b>The wash is what most of this file is about, and it is why the mode was rewritten.</b>
    /// The old rule destroyed a white mote and the four motes touching it, and this file used to
    /// assert cascades that could not happen: nothing changed a mote's colour except a drop, so
    /// the first wave took every white on the board and the second could never find one. The
    /// wave counter and the chain multiplier were dead code against a rule that rejects them.
    /// The claim that matters now is the one at the bottom — that a mote completed by a wash
    /// bursts in the wave after it — because that is the only thing in this mode that makes one
    /// drop worth more than one mote.
    /// </para>
    /// <para>
    /// <b>Boards are written out rather than dealt.</b> The mode used to roll its own colours,
    /// so these tests had to ask the board what it was holding and pick a column accordingly.
    /// A level now authors both the well and the procession, which is what makes par derivable
    /// — and it means a case here can state the exact position it is about.
    /// </para>
    /// </summary>
    public sealed class FallBoardTests
    {
        static FallLayout Layout(string deal, params string[] rows)
        {
            Assert.IsTrue(FallDeal.TryParse(deal, out var procession, out string dealError),
                          dealError);
            Assert.IsTrue(FallLayout.TryReadRows(rows, rows[0].Replace(" ", "").Length,
                                                 rows.Length, out var fill, out string fillError),
                          fillError);

            return new FallLayout(rows[0].Replace(" ", "").Length, rows.Length, fill, procession);
        }

        static FallBoard Board(params string[] rows) => new FallBoard(Layout("RGB", rows));

        /// <summary>What the board holds, written the way the case that built it wrote it.</summary>
        static string[] Read(FallBoard board)
        {
            var rows = new string[board.Height];
            for (int y = 0; y < board.Height; y++)
            {
                var row = new char[board.Width];
                for (int x = 0; x < board.Width; x++)
                {
                    // Through FallCell rather than Energy: a lens is not a colour, and
                    // Energy.Letter answers 'A' for one, which reads as a cell holding nothing
                    // in particular rather than as the one cell in the well that is glass.
                    row[x] = FallCell.Letter(board.At(x, y));
                }
                rows[y] = new string(row);
            }
            return rows;
        }

        // ------------------------------------------------------------------ the two branches
        [Test]
        public void AMoteDroppedOnAColourItLacksEnrichesItWithoutRaisingTheStack()
        {
            var board = Board("....",
                              "....",
                              "....",
                              "....",
                              "....",
                              "R...");

            Assert.IsTrue(board.Enriches(Energy.G, 0), "red does not hold green");
            Assert.AreEqual(5, board.Landing(Energy.G, 0), "it lands on the mote, not above it");

            board.Drop(Energy.G, 0);

            Assert.AreEqual(Energy.R | Energy.G, board.At(0, 5), "red and green make yellow");
            Assert.AreEqual(1, board.Motes, "the stack did not grow");
        }

        [Test]
        public void AMoteDroppedOnAColourItAlreadyHoldsSitsOnTopAndRaisesTheStack()
        {
            var board = Board("....",
                              "....",
                              "....",
                              "....",
                              "....",
                              "Y...");

            Assert.IsFalse(board.Enriches(Energy.R, 0), "yellow already holds red");
            Assert.AreEqual(4, board.Landing(Energy.R, 0), "so it rests above");

            board.Drop(Energy.R, 0);

            Assert.AreEqual(Energy.R, board.At(0, 4));
            Assert.AreEqual(2, board.Motes, "one row taller than it was");
        }

        /// <summary>
        /// The preview and the drop are the same rule read twice, and a disagreement between them
        /// is the game lying at the exact moment somebody is deciding. Asked of every column of a
        /// board holding all six blends, so no case is a lucky one.
        /// </summary>
        [Test]
        public void TheGhostLandsWhereTheDropLands()
        {
            int[] colours = { Energy.R, Energy.G, Energy.B };

            foreach (int colour in colours)
            {
                for (int x = 0; x < 6; x++)
                {
                    var board = Board("......",
                                      "......",
                                      "......",
                                      "..R...",
                                      ".YGCM.",
                                      "RYGCMB");

                    int promised = board.Landing(colour, x);
                    bool enriches = board.Enriches(colour, x);

                    var result = board.Drop(colour, x);

                    Assert.IsNotNull(result, "column " + x + " should take a mote");
                    Assert.AreEqual(promised, result.Row, "column " + x + " landed elsewhere");
                    Assert.AreEqual(enriches, result.Enriched, "column " + x + " enriched elsewhere");
                }
            }
        }

        // ------------------------------------------------------------------ the burst
        [Test]
        public void AMoteFinishedToWhiteBurstsAndIsGone()
        {
            var board = Board("....",
                              "....",
                              "....",
                              "....",
                              "....",
                              "Y...");

            var result = board.Drop(Energy.B, 0);

            Assert.AreEqual(1, result.Waves);
            Assert.AreEqual(1, result.Burst);
            Assert.AreEqual(0, board.Motes, "the well is empty");
            Assert.IsTrue(board.IsEmpty);
        }

        [Test]
        public void ABurstWashesTheColourThatFinishedItIntoTheMotesBesideIt()
        {
            var board = Board("......",
                              "......",
                              "......",
                              ".RYR..",
                              ".GGG..",
                              ".GGG..");

            // Blue finishes the yellow at the top of its column. Its three neighbours - the two
            // reds beside it and the green under it - all lack blue, so all three take it, and
            // none of them is completed by it.
            var result = board.Drop(Energy.B, 2);

            Assert.AreEqual(1, result.Waves, "nothing the wash touched was completed by it");
            Assert.AreEqual(1, result.Burst);

            var rows = Read(board);
            Assert.AreEqual(".M.M..", rows[3], "red plus blue is magenta");
            Assert.AreEqual(".GCG..", rows[4], "green plus blue is cyan");
        }

        [Test]
        public void ABurstDoesNotWashAMoteThatAlreadyHoldsTheColour()
        {
            var board = Board(".....",
                              ".....",
                              ".....",
                              ".....",
                              ".....",
                              ".MYM.");

            var steps = new List<FallStep>();
            board.Drop(Energy.B, 2, steps);

            Assert.AreEqual(1, steps.Count);
            Assert.AreEqual(0, steps[0].Washed.Count,
                            "magenta already holds blue, so the wash changes nothing and is not " +
                            "reported - an animation must never promise what the rules did not do");
        }

        /// <summary>
        /// The claim the whole mode rests on. Without it a drop is worth exactly one mote and
        /// there is no reason to think about which column.
        /// </summary>
        [Test]
        public void AMoteCompletedByAWashBurstsInTheWaveAfterIt()
        {
            var board = Board(".....",
                              ".....",
                              ".....",
                              ".....",
                              ".....",
                              "YYYYY");

            var result = board.Drop(Energy.B, 2);

            Assert.AreEqual(3, result.Waves,
                            "the middle bursts, then its two neighbours, then theirs");
            Assert.AreEqual(5, result.Burst, "the whole row went");
            Assert.IsTrue(board.IsEmpty);
        }

        [Test]
        public void AChainRunsOnlyThroughMotesMissingTheColourThatWasDropped()
        {
            var board = Board(".....",
                              ".....",
                              ".....",
                              ".....",
                              ".....",
                              "YYCYY");

            // Cyan already holds blue, so it is neither burst nor washed - and it is a wall the
            // chain cannot cross. Which colour goes where is the whole of the thinking.
            var result = board.Drop(Energy.B, 1);

            Assert.AreEqual(2, result.Waves);
            Assert.AreEqual(2, result.Burst, "only the two yellows on this side of the cyan");
            Assert.AreEqual(3, board.Motes);
        }

        /// <summary>
        /// A whole wave is decided before any of it is applied, so the wash is read off the
        /// positions the bursting motes are standing in. Resolve one burst at a time and this
        /// board settles differently depending on which column is walked first.
        /// </summary>
        [Test]
        public void TheWashIsReadBeforeAnythingFalls()
        {
            var board = Board("......",
                              "......",
                              "......",
                              "......",
                              "..R...",
                              ".YY...");

            // The first yellow is finished by the drop and washes the second, which bursts in
            // the wave after it. The red sitting on that second yellow is washed *where it
            // stands* and only then falls into the gap it left. Read the wash after the fall
            // instead and the red would be standing in the burst cell rather than beside it, so
            // nothing would ever have touched it.
            var result = board.Drop(Energy.B, 1);

            Assert.AreEqual(2, result.Waves);

            var rows = Read(board);
            Assert.AreEqual("..M...", rows[5], "the red took the blue, then fell");
            Assert.AreEqual(1, board.Motes);
        }

        [Test]
        public void EverythingAboveABurstFallsIntoTheGap()
        {
            var board = Board("......",
                              "......",
                              "..M...",
                              "..M...",
                              "..M...",
                              ".YY...");

            // Gravity only pulls down, so a burst at the top of a column moves nothing. What
            // makes a well collapse is a burst *under* something - which only a wash can reach,
            // because a drop can only ever land on the top of a stack.
            var steps = new List<FallStep>();
            board.Drop(Energy.B, 1, steps);

            Assert.AreEqual(2, steps.Count);
            Assert.AreEqual(0, steps[0].Moved.Count, "the first burst was itself a column top");
            Assert.AreEqual(3, steps[1].Moved.Count, "then three motes slid down one");

            var rows = Read(board);
            Assert.AreEqual("..M...", rows[5]);
            Assert.AreEqual("..M...", rows[4]);
            Assert.AreEqual("..M...", rows[3]);
            Assert.AreEqual("......", rows[2]);
        }

        // ------------------------------------------------------------------ the brim
        [Test]
        public void AMoteComingToRestAboveTheBrimFloodsTheWell()
        {
            var board = Board("....",
                              "M...",
                              "M...",
                              "M...",
                              "M...",
                              "M...");

            Assert.AreEqual(0, board.Headroom, "the stack already reaches the row below the brim");
            Assert.IsTrue(board.AtBrim(Energy.B, 0), "the ghost warns before it is committed");

            board.Drop(Energy.B, 0);

            Assert.IsTrue(board.Flooded);
        }

        /// <summary>
        /// A column filled to the row below the brim has exactly one way out, and both branches
        /// of it are on this board: a colour the top mote lacks is taken <em>by</em> that mote
        /// and the stack does not grow, and a colour it already holds has nowhere to go but the
        /// brim. That is the whole reason the ghost draws a ring at all.
        /// </summary>
        [Test]
        public void AFullColumnIsSavedByAColourItsTopMoteLacksAndKilledByOneItHolds()
        {
            var safe = Board("....",
                             "Y...",
                             "M...",
                             "M...",
                             "M...",
                             "M...");

            Assert.IsFalse(safe.AtBrim(Energy.B, 0), "blue enriches the yellow rather than resting");

            safe.Drop(Energy.B, 0);

            Assert.IsFalse(safe.Flooded);
            Assert.AreEqual(4, safe.Motes, "the yellow burst; the magentas already held blue");

            var doomed = Board("....",
                               "Y...",
                               "M...",
                               "M...",
                               "M...",
                               "M...");

            Assert.IsTrue(doomed.AtBrim(Energy.R, 0), "yellow already holds red, so red rests above");

            doomed.Drop(Energy.R, 0);

            Assert.IsTrue(doomed.Flooded);
        }

        [Test]
        public void HeadroomCountsTheCarelessDropsTheTallestColumnCanStillTake()
        {
            var board = Board(".....",
                              ".....",
                              ".....",
                              "..R..",
                              "..R..",
                              "R.R.R");

            Assert.AreEqual(2, board.Headroom, "rows one and two are free above the tallest");
        }

        // ------------------------------------------------------------------ the fingerprint
        [Test]
        public void TwoWellsHoldingTheSameThingShareASignature()
        {
            var a = Board(".....", ".....", ".....", ".....", ".....", "RYGCM");
            var b = Board(".....", ".....", ".....", ".....", ".....", "RYGCM");
            var c = Board(".....", ".....", ".....", ".....", ".....", "RYGCB");

            Assert.AreEqual(a.Signature(), b.Signature());
            Assert.AreNotEqual(a.Signature(), c.Signature(),
                               "one mote's difference has to be visible, or the search would " +
                               "treat two different positions as one and report a par that is " +
                               "reachable from neither");
        }

        [Test]
        public void AForkedWellIsIndependentOfTheOneItCameFrom()
        {
            var board = Board(".....", ".....", ".....", ".....", ".....", "..Y..");
            var fork = board.Fork();

            fork.Drop(Energy.B, 2);

            Assert.IsTrue(fork.IsEmpty);
            Assert.AreEqual(1, board.Motes, "the search must not disturb the position it is trying");
        }

        // ------------------------------------------------------------------ what cannot happen
        [Test]
        public void AColumnFullToTheBrimRefusesAMoteThatWouldOnlyRestOnIt()
        {
            var board = Board("R...",
                              "R...",
                              "R...",
                              "R...",
                              "R...",
                              "R...");

            Assert.AreEqual(-1, board.Landing(Energy.R, 0), "red adds nothing and there is no room");
            Assert.IsFalse(board.CanDrop(Energy.R, 0));

            Assert.AreEqual(0, board.Landing(Energy.G, 0), "green still has the top mote to enrich");
            Assert.IsTrue(board.CanDrop(Energy.G, 0));
        }

        [Test]
        public void AnEmptyWellTakesNothingMore()
        {
            var board = Board("....", "....", "....", "....", "....", "Y...");

            board.Drop(Energy.B, 0);

            Assert.IsTrue(board.IsEmpty);
            Assert.IsFalse(board.CanDrop(Energy.R, 1), "a won board is not still being played");
        }
    }
}
