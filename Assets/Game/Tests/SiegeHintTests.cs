using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The swap an idle field points at.
    ///
    /// <para>
    /// <b>The finder is in Domain and the drawing is not, which is what makes this checkable.</b>
    /// A hint that found its own answer would be a second opinion about the only question this
    /// field asks — <c>SiegeBoard.Lines</c> — and the two would drift the first time either
    /// moved. <c>AnySwap</c> is literally this scan, so a field the shuffle believes is playable
    /// is a field a hint can always point at.
    /// </para>
    /// <para>
    /// Nothing here is about the nudge's timing or its drawing: those are <c>SiegeView.Hint</c>'s
    /// and they decide nothing, which is the whole argument for a hint that cannot reach the
    /// rules.
    /// </para>
    /// </summary>
    public sealed class SiegeHintTests
    {
        /// <summary>
        /// The shipped opening rung's own field — settled, playable, and carrying every colour.
        ///
        /// Hand-drawn fields are the trap <c>SiegeFieldRaiderTests</c> already records: a board
        /// with no run and no swap on it re-deals itself the moment it advances, so a fixture
        /// built on one passes or fails for reasons that have nothing to do with the thing under
        /// test. Nothing here advances a board, but the field is the real one anyway.
        /// </summary>
        static SiegeBoard Playable()
            => SiegeBoard.Build(Layout("ryybgyyg",
                                       "bybgrgyy",
                                       "rbryyggr",
                                       "grgrgbbr",
                                       "yybgrrbg"));

        /// <summary>
        /// A field with no run on it and no legal swap either, which is exactly the state the
        /// shuffle exists to get out of. Never advanced, or it would deal itself away.
        ///
        /// <para>
        /// <b>Four colours stepped by one a row, not a two-colour checkerboard.</b> A checkerboard
        /// is the obvious fixture and it is wrong: it has no <em>run</em> on it, which is what
        /// makes it look right, and it has plenty of legal swaps — swapping across a row leaves
        /// two alike either side of the cell that moved. Written down because it was the first
        /// thing tried here and the test that caught it is the one below.
        /// </para>
        /// </summary>
        static SiegeBoard Locked()
            => SiegeBoard.Build(Layout("rgbyrgby",
                                       "gbyrgbyr",
                                       "byrgbyrg",
                                       "yrgbyrgb",
                                       "rgbyrgby"));

        static SiegeLayout Layout(params string[] rows)
            => new SiegeLayout(GridOf(rows), "rgby", "rgby", new[] { "~r" }, null, 0);

        static ProtoGrid GridOf(string[] rows)
        {
            Assert.IsTrue(ProtoGrid.TryRead(rows, rows[0].Length, rows.Length, SiegeLayout.Cells,
                                            out var grid, out string error), error);
            return grid;
        }

        [Test]
        public void AFieldWithNoSwapOnItOffersNone()
        {
            var board = Locked();

            Assert.IsFalse(board.AnySwap(), "the fixture is wrong, not the finder");
            Assert.IsFalse(board.FindSwap(0).Found);
        }

        /// <summary>
        /// The one that would have caught the struct's default reading as a real answer: a
        /// <c>default(SiegeSwap)</c> names cell nought twice, so a <see cref="SiegeSwap.Found"/>
        /// written as a test on the cells answers <b>true</b> for "nothing found" and an idle
        /// field rings its own corner on a board with no move on it.
        /// </summary>
        [Test]
        public void NothingFoundIsNotCellNoughtTwice()
        {
            Assert.IsFalse(default(SiegeSwap).Found);
        }

        [Test]
        public void EverySwapItOffersIsOneTheFieldWouldReallyTake()
        {
            var board = Playable();

            Assert.IsTrue(board.AnySwap(), "the fixture is wrong, not the finder");

            // Walked the whole way round, so every answer the scan can give is checked rather
            // than only the first.
            int pairs = board.Count * 2;
            int found = 0;

            for (int from = 0; from < pairs; from++)
            {
                var swap = board.FindSwap(from);
                if (!swap.Found) continue;

                found++;
                Assert.IsTrue(board.Adjacent(swap.A, swap.B),
                              $"offered {swap.A} <-> {swap.B}, which are not neighbours");
                Assert.IsTrue(board.Lines(swap.A, swap.B),
                              $"offered {swap.A} <-> {swap.B}, which the field refuses");
            }

            Assert.AreEqual(pairs, found, "a field with a swap on it fell silent from some start");
        }

        /// <summary>
        /// Asking again from where the last answer stopped walks the field rather than handing
        /// back the same pair — which is the whole reason <see cref="SiegeSwap.At"/> exists. Three
        /// nudges pointing at one pair reads as the board repeating itself, not as it helping.
        /// </summary>
        [Test]
        public void AskingAgainMovesOn()
        {
            var board = Playable();

            var first = board.FindSwap(0);
            Assert.IsTrue(first.Found);

            var second = board.FindSwap(first.At + 1);
            Assert.IsTrue(second.Found, "a field with several swaps ran out after one");
            Assert.AreNotEqual(first.At, second.At, "the second ask returned the first answer");
        }

        /// <summary>
        /// It wraps, so a nudge asked late in the scan still gets an answer rather than falling
        /// silent on a field that plainly has moves on it.
        /// </summary>
        [Test]
        public void ItWrapsRoundRatherThanRunningOut()
        {
            var board = Playable();
            var last = board.FindSwap(board.Count * 2 - 1);

            Assert.IsTrue(last.Found, "the scan gave up at the end of the field instead of wrapping");
            Assert.IsTrue(board.Lines(last.A, last.B));
        }

        /// <summary>
        /// A start outside the field is answered rather than thrown at, because the caller keeps
        /// a running position and nothing clamps it for them.
        /// </summary>
        [Test]
        public void AStartOutsideTheFieldIsStillAnswered()
        {
            var board = Playable();

            foreach (int from in new[] { -1, -97, board.Count * 40 })
            {
                var swap = board.FindSwap(from);
                Assert.IsTrue(swap.Found, $"a start of {from} fell silent");
                Assert.IsTrue(board.Lines(swap.A, swap.B));
            }
        }

        /// <summary>
        /// The board reads nought waves for the whole opening quiet and never again, which is the
        /// fact the nudge's own opening gate rests on (<c>SiegeView.Opening</c>).
        ///
        /// <para>
        /// <b>It matters because the count-in holds nothing.</b> <c>SiegeView.CountIn</c> draws
        /// four beats over a board that is already <c>Playable</c> — the clock runs underneath it
        /// deliberately — so "has the run started" cannot be asked of the view's latches at all,
        /// and the first nudge landed six tenths of a second after GO!. A flag set by the count
        /// was the obvious fix and has an exit that clears nothing; this is the same fact with
        /// nothing to keep in step, so it is worth knowing that it holds.
        /// </para>
        /// </summary>
        [Test]
        public void NoWaveHasMusteredUntilTheOpeningQuietIsOver()
        {
            var board = Playable();

            Assert.AreEqual(0, board.Wave, "a wave had mustered before the board was advanced");

            // Just short of the quiet, in the steps the view really advances in.
            for (int i = 0; i < (int)(SiegeTuning.FirstWaveAfter * 60f) - 2; i++)
                board.Advance(1f / 60f);

            Assert.AreEqual(0, board.Wave, "the first wave mustered inside the opening quiet");

            for (int i = 0; i < 8; i++) board.Advance(1f / 60f);

            Assert.GreaterOrEqual(board.Wave, 1, "the first wave never mustered");
        }

        /// <summary>
        /// <c>AnySwap</c> is the finder, which is what stops the shuffle and the hint being able
        /// to disagree about whether a field is playable.
        /// </summary>
        [Test]
        public void TheShuffleAndTheHintAskOneQuestion()
        {
            Assert.AreEqual(Playable().AnySwap(), Playable().FindSwap(0).Found);
            Assert.AreEqual(Locked().AnySwap(), Locked().FindSwap(0).Found);
        }
    }
}
