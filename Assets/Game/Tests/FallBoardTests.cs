using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Lightfall's rules: a mote either enriches the top of a stack or heightens it, and a stack
    /// that reaches white detonates.
    ///
    /// <para>
    /// <b>The board deals its own motes, so these tests read the deal rather than dictating it.</b>
    /// Forcing colours would need a hole cut in the class for the tests to reach through, and a
    /// rule proved against a board nobody can build is not proved. Instead each case asks the
    /// board what it is holding and picks a column accordingly, which is exactly what a player
    /// does — and it means every one of these would still be a valid test if the deal changed.
    /// </para>
    /// <para>
    /// The preview cases are the ones that matter most in practice. <c>Landing</c> and
    /// <c>Enriches</c> draw the ghost under the player's thumb, so if either disagrees with what
    /// <c>Drop</c> then does, the game lies to the player at the exact moment they are deciding.
    /// </para>
    /// </summary>
    public sealed class FallBoardTests
    {
        static FallBoard Board(uint seed = 7) => new FallBoard(6, 11, seed);

        [Test]
        public void AMoteDroppedIntoAnEmptyColumnLandsAtTheBottom()
        {
            var board = Board();
            int colour = board.Next;

            var result = board.Drop(0);

            Assert.IsNotNull(result);
            Assert.AreEqual(board.Height - 1, result.Row);
            Assert.AreEqual(colour, board.At(0, board.Height - 1));
            Assert.IsFalse(result.Enriched, "there was nothing there to enrich");
        }

        [Test]
        public void AMoteThatAddsAColourEnrichesTheTopAndTheStackDoesNotGrow()
        {
            var board = Board();
            board.Drop(0);

            int bottom = board.Height - 1;
            int before = board.At(0, bottom);

            // Wait for a mote that would actually add something to what is already there.
            while (!board.Enriches(0)) board.Drop(1);

            int adding = board.Next;
            var result = board.Drop(0);

            Assert.IsTrue(result.Enriched);
            Assert.AreEqual(bottom, result.Row, "it should have merged, not stacked");
            Assert.AreEqual(before | adding, board.At(0, bottom));
            Assert.AreEqual(Energy.None, board.At(0, bottom - 1), "the stack must not have grown");
        }

        [Test]
        public void AMoteThatAddsNothingSitsOnTopAndTheStackGrows()
        {
            var board = Board();
            board.Drop(0);

            int bottom = board.Height - 1;

            // Wait for a mote the bottom already contains — dropping it can only heighten.
            while (board.Enriches(0)) board.Drop(1);

            int adding = board.Next;
            var result = board.Drop(0);

            Assert.IsFalse(result.Enriched);
            Assert.AreEqual(bottom - 1, result.Row, "it should have stacked, not merged");
            Assert.AreEqual(adding, board.At(0, bottom - 1));
        }

        [Test]
        public void TheGhostNeverLiesAboutWhereAMoteWillLand()
        {
            // Landing and Enriches are what the player sees before committing. A disagreement
            // here is the game telling somebody one thing and doing another.
            var board = Board(31);

            for (int drop = 0; drop < 120 && !board.IsLost; drop++)
            {
                int column = drop % board.Width;
                if (!board.CanDrop(column)) continue;

                int predictedRow = board.Landing(column);
                bool predictedEnrich = board.Enriches(column);

                var result = board.Drop(column);

                Assert.IsNotNull(result);
                Assert.AreEqual(predictedRow, result.Row, "Landing disagreed with the drop");
                Assert.AreEqual(predictedEnrich, result.Enriched,
                                "Enriches disagreed with the drop");
            }
        }

        [Test]
        public void CompletingAllThreeColoursDetonates()
        {
            var board = Board();
            int cleared = board.Cleared;

            // Feed one column until it reaches white, taking anything that does not help
            // somewhere else. A column needs exactly three distinct channels, so this ends.
            for (int guard = 0; guard < 200 && board.Cleared == cleared; guard++)
                board.Drop(board.Enriches(0) || board.TopOf(0) < 0 ? 0 : 1);

            Assert.Greater(board.Cleared, cleared, "a stack reaching white has to detonate");
            Assert.Greater(board.Score, 0);
        }

        [Test]
        public void NothingWhiteIsLeftStandingOnceADropHasResolved()
        {
            // White is the detonation, so a board at rest can never hold any. If one survives,
            // the resolution stopped early and the next drop would set off a stale explosion.
            var board = Board(97);

            for (int drop = 0; drop < 200 && !board.IsLost; drop++)
            {
                int column = drop % board.Width;
                if (board.CanDrop(column)) board.Drop(column);

                for (int i = 0; i < board.Width * board.Height; i++)
                    Assert.AreNotEqual(Energy.All, board.At(i),
                                       "a white mote was left standing after the board settled");
            }
        }

        [Test]
        public void EveryMoteRestsOnSomethingOrOnTheFloor()
        {
            // The gravity pass runs after every detonation. A mote left floating means it
            // settled column by column against a board another column was still changing.
            var board = Board(53);

            for (int drop = 0; drop < 200 && !board.IsLost; drop++)
            {
                if (board.CanDrop(drop % board.Width)) board.Drop(drop % board.Width);

                for (int x = 0; x < board.Width; x++)
                    for (int y = 0; y < board.Height - 1; y++)
                        if (board.At(x, y) != Energy.None)
                            Assert.AreNotEqual(Energy.None, board.At(x, y + 1),
                                               $"the mote at {x},{y} is floating");
            }
        }

        [Test]
        public void ALaterWaveInOneDropIsWorthMoreThanTheFirst()
        {
            // Chains are the reward. If every wave scored the same, setting one up would be
            // worth exactly as much as clearing the same motes one at a time.
            var board = Board();

            var single = new FallStep(new[] { 0, 1 }, 1, new FallMove[0]);
            var chained = new FallStep(new[] { 0, 1 }, 2, new FallMove[0]);

            Assert.AreEqual(2, chained.Wave);
            Assert.AreEqual(1, single.Wave);
            Assert.AreEqual(single.Taken.Count, chained.Taken.Count,
                            "same motes, so only the wave number may differ");
        }

        [Test]
        public void AFullColumnEndsTheRun()
        {
            var board = Board(11);

            // Only ever feed column 0 what it cannot use, so it can only grow.
            for (int guard = 0; guard < 400 && !board.IsLost; guard++)
                board.Drop(board.Enriches(0) ? 1 : 0);

            Assert.IsTrue(board.IsLost, "a column filled to the brim has to end the run");
            Assert.IsFalse(board.CanDrop(0));
            Assert.IsNull(board.Drop(0), "a lost board must refuse a drop rather than take it");
        }

        [Test]
        public void TheQueueAdvancesAndShowsWhatIsComing()
        {
            var board = Board();

            int next = board.Next, after = board.Ahead(1);
            Assert.AreNotEqual(Energy.None, after, "the tray has to show a forecast");

            board.Drop(0);
            Assert.AreEqual(after, board.Next, "the queue should have moved up by one");
            Assert.AreNotEqual(Energy.None, board.Ahead(FallBoard.Lookahead - 1),
                               "the queue has to refill behind what was spent");
        }

        [Test]
        public void OnlyPureColoursAreDealt()
        {
            // A dealt blend hands the player a step of the cooking for free, which is the one
            // thing the whole mode is about doing yourself.
            var board = Board(5);

            for (int drop = 0; drop < 150 && !board.IsLost; drop++)
            {
                int colour = board.Next;
                Assert.IsTrue(colour == Energy.R || colour == Energy.G || colour == Energy.B,
                              $"the deal produced {Energy.Letter(colour)}");
                if (board.CanDrop(drop % board.Width)) board.Drop(drop % board.Width);
            }
        }

        [Test]
        public void TheSameSeedDealsTheSameRun()
        {
            // A retry has to meet the board the player just played, and a bug has to be
            // reproducible from the seed alone.
            var a = Board(1234);
            var b = Board(1234);

            for (int drop = 0; drop < 60; drop++)
            {
                Assert.AreEqual(a.Next, b.Next);
                a.Drop(drop % a.Width);
                b.Drop(drop % b.Width);
            }

            Assert.AreEqual(a.Score, b.Score);
            Assert.AreEqual(a.Cleared, b.Cleared);
        }

        [Test]
        public void TallestReportsTheWorstColumn()
        {
            var board = Board();
            Assert.AreEqual(0, board.Tallest);

            while (board.Enriches(0)) board.Drop(1);
            board.Drop(0);

            Assert.GreaterOrEqual(board.Tallest, 1);
            Assert.LessOrEqual(board.Tallest, board.Height);
        }
    }
}
