using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Kindlewake's rules, one clause at a time.
    ///
    /// <para>
    /// <b>Every rule here exists twice</b> — once in <c>KindleBoard.cs</c> and once in
    /// <c>Tools/verify/kindle.py</c> — so what this fixture is really guarding is the C# half of
    /// a pair that has to stay identical (invariant 9a). The disagreements that matter are
    /// silent: a clause that refuses one strand too many makes a board <em>harder</em> and par
    /// comes out one higher, which looks exactly like a level somebody authored. That is
    /// Budburst's wash bug, and it is why the shipped hollows are also pinned inline in
    /// <c>ProtoLadderTests</c> rather than in a JSON fixture the offline runner cannot read.
    /// </para>
    /// <para>
    /// Boards are written as rows of characters and read through the real layout, so a change to
    /// the alphabet fails here rather than somewhere downstream.
    /// </para>
    /// </summary>
    public sealed class KindleRuleTests
    {
        static KindleLayout Layout(params string[] rows)
        {
            Assert.IsTrue(ProtoGrid.TryRead(rows, rows[0].Length, rows.Length,
                                            KindleLayout.Letters, out var grid, out string bad),
                          bad);
            return new KindleLayout(grid, 0);
        }

        static KindleBoard Board(params string[] rows) => KindleBoard.Build(Layout(rows));

        static int Cell(KindleBoard board, int x, int y) => y * board.Width + x;

        // ------------------------------------------------------------------ the join
        [Test]
        public void TwoEmbersOfOneColourOnARowMayBeJoined()
        {
            var board = Board(
                "r.R.r",
                ".....",
                ".....");

            Assert.IsTrue(board.Join(Cell(board, 0, 0), Cell(board, 4, 0), out int channel));
            Assert.AreEqual(Energy.R, channel);
        }

        [Test]
        public void TwoEmbersOfDifferentColoursAreNotAPair()
        {
            var board = Board(
                "r.R.b",
                ".....",
                ".....");

            Assert.IsFalse(board.Join(Cell(board, 0, 0), Cell(board, 4, 0), out _));
        }

        [Test]
        public void TwoEmbersThatShareNeitherRowNorColumnAreNotAPair()
        {
            var board = Board(
                "r...R",
                ".....",
                "....r");

            Assert.IsFalse(board.Join(Cell(board, 0, 0), Cell(board, 4, 2), out _));
        }

        [Test]
        public void StoneBetweenTwoEmbersRefusesTheStrand()
        {
            var board = Board(
                "r#R.r",
                ".....",
                ".....");

            Assert.IsFalse(board.Join(Cell(board, 0, 0), Cell(board, 4, 0), out _),
                           "stone is the one thing on a dealt board that stops a strand");
        }

        /// <summary>
        /// A strand that puts its channel nowhere new is not a move.
        ///
        /// <b>This is the whole of what this mode calls a no-op</b>, and it is
        /// <c>ProtoPosition.Play</c>'s own contract met on the board rather than only in the
        /// solver: a self-edge in a breadth-first walk is a layer the frontier never leaves.
        /// </summary>
        [Test]
        public void AStrandThatLightsNothingNewIsRefused()
        {
            var board = Board(
                "r.R.r",
                "r...r",
                ".....");

            Assert.IsNotNull(board.Draw(new KindleMove(Cell(board, 0, 1), Cell(board, 4, 1))));

            // The second row is now entirely red, and the row above it shares no cell with it -
            // so a strand along row 1 is impossible (its embers are spent) while one along row 0
            // still lights three new cells.
            Assert.IsTrue(board.Join(Cell(board, 0, 0), Cell(board, 4, 0), out _));
        }

        [Test]
        public void AStrandOverGroundThatAlreadyHoldsItsChannelIsRefused()
        {
            var board = Board(
                "r.r.r",
                ".....",
                ".R...");

            // (0,0)-(2,0) lights columns 0..2 of row 0 with red.
            Assert.IsNotNull(board.Draw(new KindleMove(Cell(board, 0, 0), Cell(board, 2, 0))));

            // Nothing left on that row can be joined - both its ends are spent - and the rule
            // that would have refused a repeat is the same one.
            Assert.IsFalse(board.Join(Cell(board, 0, 0), Cell(board, 4, 0), out _));
        }

        // ------------------------------------------------------------------ what a strand does
        [Test]
        public void AStrandLightsEveryCellBetweenItsEndsAndBoth()
        {
            var board = Board(
                "r...r",
                ".....",
                ".....");

            var log = board.Draw(new KindleMove(Cell(board, 0, 0), Cell(board, 4, 0)));

            Assert.IsNotNull(log);
            Assert.AreEqual(5, log.Span);

            for (int x = 0; x < 5; x++)
                Assert.AreEqual(Energy.R, board.Light(Cell(board, x, 0)),
                                $"column {x} of the strand's own row");

            Assert.AreEqual(Energy.None, board.Light(Cell(board, 0, 1)),
                            "a strand lights its own line and nothing else");
        }

        [Test]
        public void BothEmbersAreSpentAndBecomeSockets()
        {
            var board = Board(
                "r...r",
                ".....",
                ".....");

            board.Draw(new KindleMove(Cell(board, 0, 0), Cell(board, 4, 0)));

            Assert.AreEqual(KindleLayout.Spent, board.At(Cell(board, 0, 0)));
            Assert.AreEqual(KindleLayout.Spent, board.At(Cell(board, 4, 0)));
        }

        /// <summary>
        /// A burnt-out socket stops a strand, exactly as stone does.
        ///
        /// <b>The rule that makes a pairing a decision.</b> Without it every strand costs the
        /// same two embers and light never hurts, so covering the most critters is nearly always
        /// right — measured across three hundred and twenty swept hollows, a player who never
        /// looked ahead finished every one of them. With it, a strand leaves two permanent holes
        /// in the geometry.
        /// </summary>
        [Test]
        public void ASpentSocketBlocksALaterStrand()
        {
            var board = Board(
                ".b...",
                "rbr.R",
                ".....");

            // Only a strand's *ends* are spent, so the socket that does the blocking has to be
            // one of them: the blue pair is (1,0) and (1,1), and (1,1) sits between the reds.
            Assert.IsNotNull(board.Draw(new KindleMove(Cell(board, 1, 0), Cell(board, 1, 1))));
            Assert.AreEqual(KindleLayout.Spent, board.At(Cell(board, 1, 1)));

            Assert.IsFalse(board.Join(Cell(board, 0, 1), Cell(board, 2, 1), out _),
                           "the socket left by the blue pair now walls the red pair apart");
        }

        // ------------------------------------------------------------------ waking
        [Test]
        public void ACritterWantingOneChannelWakesToOneStrand()
        {
            var board = Board(
                "rRr..",
                ".....",
                ".....");

            Assert.AreEqual(1, board.Goals);
            Assert.AreEqual(1, board.GoalsLeft);

            var log = board.Draw(new KindleMove(Cell(board, 0, 0), Cell(board, 2, 0)));

            Assert.AreEqual(1, log.Woke);
            Assert.AreEqual(0, log.Blended, "one channel is not a blend");
            Assert.IsTrue(board.IsFinished);
        }

        [Test]
        public void ACritterWantingABlendNeedsTwoStrandsCrossingOnIt()
        {
            var board = Board(
                ".b...",
                "rMr..",
                ".b...");

            // Red along the row gives it one of the two channels and no more.
            var first = board.Draw(new KindleMove(Cell(board, 0, 1), Cell(board, 2, 1)));

            Assert.AreEqual(0, first.Woke);
            Assert.AreEqual(1, first.Stirred, "it took a channel and is still short");
            Assert.IsFalse(board.IsFinished);

            // Blue down the column crosses it, and the two together are what it wanted.
            var second = board.Draw(new KindleMove(Cell(board, 1, 0), Cell(board, 1, 2)));

            Assert.AreEqual(1, second.Woke);
            Assert.AreEqual(1, second.Blended, "woken by a colour neither strand carried");
            Assert.AreEqual(1, second.Crossings);
            Assert.IsTrue(board.IsFinished);
        }

        /// <summary>
        /// A crossing is light meeting light of <em>another</em> channel.
        ///
        /// Light meeting its own is a strand laid over a strand, which is a picture and not an
        /// event — and counting it would let a board claim a payoff it never made (invariant 20m).
        /// </summary>
        [Test]
        public void LightMeetingItsOwnChannelIsNotACrossing()
        {
            var board = Board(
                ".r...",
                "rRr..",
                ".r...");

            board.Draw(new KindleMove(Cell(board, 0, 1), Cell(board, 2, 1)));
            var second = board.Draw(new KindleMove(Cell(board, 1, 0), Cell(board, 1, 2)));

            Assert.IsNotNull(second);
            Assert.AreEqual(0, second.Crossings,
                            "red over red is a strand on a strand, not a colour neither had");
        }

        /// <summary>
        /// The whole line is lit before any critter is asked whether it has woken.
        ///
        /// Judging as the walk goes would make the answer depend on which end the loop started
        /// from — the class of divergence a second runtime cannot see, and the discipline
        /// <c>FallBoard.Resolve</c> is built around.
        /// </summary>
        [Test]
        public void EveryCritterOnAStrandIsJudgedAgainstTheFinishedLine()
        {
            var board = Board(
                "rRRRr",
                ".....",
                ".....");

            var log = board.Draw(new KindleMove(Cell(board, 0, 0), Cell(board, 4, 0)));

            Assert.AreEqual(3, log.Woke, "one strand wakes every critter it covers");
            Assert.IsTrue(board.IsFinished);
        }

        [Test]
        public void ACritterWantingAllThreeChannelsNeedsThreeStrands()
        {
            var board = Board(
                ".b.g.",
                "rWr..",
                ".b.g.");

            board.Draw(new KindleMove(Cell(board, 0, 1), Cell(board, 2, 1)));
            board.Draw(new KindleMove(Cell(board, 1, 0), Cell(board, 1, 2)));

            Assert.IsFalse(board.IsFinished, "red and blue are not white");
            Assert.AreEqual(Energy.R | Energy.B, board.Light(Cell(board, 1, 1)));
        }

        // ------------------------------------------------------------------ the move list
        /// <summary>
        /// Each pair is offered once, and that is provable rather than observed.
        ///
        /// A strand covers the cells between its two ends and lights every one with one channel,
        /// so which end the finger started at cannot reach any rule. Emitting both would double
        /// the work at every position and, worse, double-count <c>ProtoAnswer.Ways</c> at every
        /// depth — which is exactly the fault Emberforge shipped (invariant 34d).
        /// </summary>
        [Test]
        public void EachPairIsOfferedOnceAndNeverBothWaysRound()
        {
            var board = Board(
                "rRr..",
                ".....",
                ".....");

            var moves = new List<KindleMove>();
            board.Joins(moves);

            Assert.AreEqual(1, moves.Count);
            Assert.Less(moves[0].From, moves[0].To, "a pair is stored lower index first");
        }

        [Test]
        public void AMoveIsUnorderedWhicheverWayItIsBuilt()
        {
            var there = new KindleMove(7, 3);
            var back = new KindleMove(3, 7);

            Assert.AreEqual(back.From, there.From);
            Assert.AreEqual(back.To, there.To);
        }

        // ------------------------------------------------------------------ the endings
        /// <summary>
        /// The proof that a hollow can never be finished, and it is a certainty rather than a
        /// guess (invariant 28f) — it decides whether money changes hands.
        /// </summary>
        [Test]
        public void AHollowThatCanNoLongerRaiseAPairIsStranded()
        {
            var board = Board(
                "rRr.b",
                ".....",
                ".....");

            Assert.IsFalse(board.Stranded, "two reds are still standing");

            board.Draw(new KindleMove(Cell(board, 0, 0), Cell(board, 2, 0)));

            // The critter woke, so nothing is wanted and nothing is stranded.
            Assert.IsTrue(board.IsFinished);
            Assert.IsFalse(board.Stranded);
        }

        [Test]
        public void ACritterWantingAChannelWithOneEmberLeftIsStranded()
        {
            var board = Board(
                "rBr..",
                ".....",
                "....b");

            Assert.IsTrue(board.Stranded,
                          "one blue ember can never be joined to anything, so the critter "
                          + "wanting blue can never wake");
        }

        [Test]
        public void AHollowWithNoMoveLeftSaysSo()
        {
            var board = Board(
                "rRb..",
                ".....",
                ".....");

            Assert.IsFalse(board.AnyMove, "one red and one blue make no pair at all");
        }

        // ------------------------------------------------------------------ the key
        /// <summary>
        /// The key covers the cells <em>and</em> the light.
        ///
        /// Two arrangements holding the same embers under different light are two different
        /// boards; merging them would under-report par, which is the direction that hands out
        /// stars nobody earned.
        /// </summary>
        [Test]
        public void TwoBoardsWithTheSameEmbersButDifferentLightAreDifferentStates()
        {
            var lit = Board(
                "r.r.M",
                ".....",
                ".....");
            var dark = Board(
                "r.r.M",
                ".....",
                ".....");

            var a = new List<byte>();
            var b = new List<byte>();

            lit.Write(a);
            dark.Write(b);
            CollectionAssert.AreEqual(a, b, "two untouched copies are the same state");

            lit.Draw(new KindleMove(Cell(lit, 0, 0), Cell(lit, 2, 0)));

            a.Clear();
            lit.Write(a);
            CollectionAssert.AreNotEqual(a, b);
        }

        // ------------------------------------------------------------------ the alphabet
        [Test]
        public void NeitherStateCharacterMayBeAuthored()
        {
            Assert.AreEqual(-1, KindleLayout.Letters.IndexOf(KindleLayout.Spent),
                            "a socket is a state a board reaches, never one it is written in");
            Assert.AreEqual(-1, KindleLayout.Letters.IndexOf(KindleLayout.Woken));
        }

        [Test]
        public void EveryEmberLetterNamesOneOfEnergysThreeChannels()
        {
            int seen = Energy.None;

            foreach (char c in KindleLayout.Embers)
            {
                int channel = KindleLayout.ChannelOf(c);
                Assert.AreNotEqual(Energy.None, channel, $"'{c}' carries no channel");
                Assert.AreEqual(1, KindleLayout.Channels(channel), $"'{c}' is not pure");
                seen |= channel;
            }

            Assert.AreEqual(Energy.All, seen, "the three embers are the three channels");
        }

        /// <summary>
        /// A critter's letter is <see cref="Energy"/>'s own, not a second alphabet for one
        /// arithmetic.
        /// </summary>
        [Test]
        public void EverySleeperLetterIsOneEnergyAlreadyKnows()
        {
            foreach (char c in KindleLayout.Sleepers)
            {
                Assert.IsTrue(Energy.TryParse(c, out int mask), $"'{c}' is not an Energy letter");
                Assert.AreEqual(mask, KindleLayout.WantOf(c));
                Assert.AreNotEqual(Energy.None, mask, $"'{c}' asks for nothing");
            }
        }

        // ------------------------------------------------------------------ the reader
        [Test]
        public void AHollowWithNoSleeperIsRefused()
        {
            var layout = Layout(
                "r.r..",
                ".....",
                ".....");

            Assert.IsNotNull(layout.Fault, "nothing to wake is nothing to do");
        }

        [Test]
        public void AHollowWhoseColoursCannotMakeOnePairIsRefused()
        {
            var layout = Layout(
                "rRb..",
                ".....",
                ".....");

            Assert.IsNotNull(layout.Fault);
        }

        [Test]
        public void AHollowThatDealsAnythingIsRefused()
        {
            var dto = new LevelDto
            {
                id = "x01_probe",
                kindle = new ProtoDto
                {
                    width = 5,
                    height = 3,
                    rows = new[] { "rRr..", ".....", "....." },
                    cores = "RGB",
                },
            };

            var mode = LevelModes.Find(GameMode.Kindle);
            Assert.NotNull(mode, "the mode is not registered");

            var problems = new List<string>();
            Assert.IsFalse(mode.TryRead(dto, LevelId.Parse("x01_probe"), problems, out _),
                           "a hollow is everything the level hands over");
            Assert.IsNotEmpty(problems);
        }

        // ------------------------------------------------------------------ the search
        /// <summary>
        /// Every join spends two embers, so depth is bounded by half the material and the state
        /// graph is a DAG. That is invariant 20j's second test, and it is what lets
        /// <see cref="ProtoSearch"/> find par with nothing to prove about termination.
        /// </summary>
        [Test]
        public void EveryMoveSpendsExactlyTwoEmbersSoTheSearchTerminates()
        {
            var board = Board(
                "rRr.b",
                ".....",
                "b....");

            int before = Embers(board);
            board.Draw(new KindleMove(Cell(board, 0, 0), Cell(board, 2, 0)));

            Assert.AreEqual(before - 2, Embers(board));
        }

        static int Embers(KindleBoard board)
        {
            int n = 0;
            for (int i = 0; i < board.Width * board.Height; i++)
                if (KindleLayout.IsEmber(board.At(i))) n++;
            return n;
        }

        [Test]
        public void ASearchOverASmallHollowFindsTheParAHandCanCount()
        {
            var layout = Layout(
                ".b.",
                "rMr",
                ".b.");

            var answer = ProtoSearch.Solve(new KindleFuture(KindleBoard.Build(layout)));

            Assert.IsTrue(answer.Proved);

            // One critter wanting two channels, one pair of each colour, and exactly one way to
            // deliver either: red across the row, blue down the column. Two strands, and no
            // shorter answer exists because a strand carries one channel.
            Assert.AreEqual(2, answer.Par);
            Assert.AreEqual(2, answer.Ways, "the two strands can be drawn in either order");
        }
    }
}
