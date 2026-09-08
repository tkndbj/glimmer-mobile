using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Prismvale's rules, one clause at a time.
    ///
    /// <para>
    /// <b>Every rule here exists twice</b> — once in <c>PrismBoard.cs</c> and once in
    /// <c>Tools/verify/prism.py</c> — so what this fixture is really guarding is the C# half of a
    /// pair that has to stay identical (invariant 9a). The disagreements that matter are silent:
    /// a flood that stops one cell early makes a board <em>harder</em> and par comes out one
    /// higher, which looks exactly like a level somebody authored. That is Budburst's wash bug,
    /// and it is why the shipped boards are also pinned inline in <c>ProtoLadderTests</c> rather
    /// than in a JSON fixture the offline runner cannot read.
    /// </para>
    /// <para>
    /// Boards are written as rows of characters and read through the real layout, so a change to
    /// the alphabet fails here rather than somewhere downstream.
    /// </para>
    /// </summary>
    public sealed class PrismRuleTests
    {
        static PrismLayout Layout(params string[] rows)
        {
            Assert.IsTrue(ProtoGrid.TryRead(rows, rows[0].Length, rows.Length,
                                            PrismLayout.Letters, out var grid, out string bad),
                          bad);
            return new PrismLayout(grid, 0);
        }

        static PrismBoard Board(params string[] rows) => PrismBoard.Build(Layout(rows));

        static int Cell(PrismBoard board, int x, int y) => y * board.Width + x;

        // ------------------------------------------------------------------ the swap
        [Test]
        public void TwoTouchingGemsOfDifferentColoursMaySwap()
        {
            var board = Board(
                "Rrg",
                "bgr",
                "..@");

            Assert.IsTrue(board.CanSwap(Cell(board, 1, 0), Cell(board, 2, 0)));
        }

        /// <summary>
        /// <b>The whole of what keeps the search finite.</b> A move that changes nothing is a
        /// self-edge, and a self-edge in a breadth-first walk is a layer the frontier never
        /// leaves — so two gems of one colour are refused rather than swapped for free.
        /// </summary>
        [Test]
        public void TwoTouchingGemsOfOneColourAreNotAMove()
        {
            var board = Board(
                "Rrr",
                "bgr",
                "..@");

            Assert.IsFalse(board.CanSwap(Cell(board, 1, 0), Cell(board, 2, 0)));
        }

        [Test]
        public void ALanternNeverMoves()
        {
            var board = Board(
                "Rrg",
                "bgr",
                "..@");

            Assert.IsFalse(board.CanSwap(Cell(board, 0, 0), Cell(board, 1, 0)));
            Assert.IsFalse(board.CanSwap(Cell(board, 0, 0), Cell(board, 0, 1)));
        }

        [Test]
        public void ASleepingCritterNeverMoves()
        {
            var board = Board(
                "Rrg",
                "bg@",
                "...");

            Assert.IsFalse(board.CanSwap(Cell(board, 2, 1), Cell(board, 1, 1)));
        }

        [Test]
        public void BareGroundIsNotAGem()
        {
            var board = Board(
                "Rr.",
                "bg@",
                "...");

            Assert.IsFalse(board.CanSwap(Cell(board, 2, 0), Cell(board, 1, 0)));
        }

        /// <summary>
        /// Which cells hold gems never changes, so the swap list is a fact about the layout. If
        /// that ever stopped being true every move index in the search would mean something
        /// different from one position to the next.
        /// </summary>
        [Test]
        public void TheSwapListIsEveryTouchingPairOfGemCells()
        {
            var layout = Layout(
                "Rrg",
                "bg@",
                "...");

            // Gem cells: (1,0) (2,0) (0,1) (1,1). Touching pairs: 1-2 across the top, 0-1 across
            // the middle, and (1,0)-(1,1) down. Bare ground and the critter break the rest.
            Assert.AreEqual(3 * 2, layout.Swaps.Length);
        }

        // ------------------------------------------------------------------ the light
        [Test]
        public void ALanternLightsAGemOfItsOwnColourBesideIt()
        {
            var board = Board(
                "Rrg",
                "bg@",
                "...");

            Assert.AreEqual(0, board.Vein(Cell(board, 1, 0)));
        }

        [Test]
        public void ALanternLightsNothingOfAnotherColour()
        {
            var board = Board(
                "Rgg",
                "bg@",
                "...");

            Assert.AreEqual(-1, board.Vein(Cell(board, 1, 0)));
        }

        /// <summary>
        /// A vein runs on through every touching gem of the same colour, which is what makes it
        /// something to build rather than something to place.
        /// </summary>
        [Test]
        public void LightRunsOnThroughEveryTouchingGemOfThatColour()
        {
            var board = Board(
                "Rrrr",
                "bggg",
                "....");

            for (int x = 1; x <= 3; x++)
                Assert.AreEqual(0, board.Vein(Cell(board, x, 0)), $"({x},0)");
        }

        [Test]
        public void AGemOfAnotherColourStopsTheVeinDead()
        {
            var board = Board(
                "Rrgr",
                "bggg",
                "....");

            Assert.AreEqual(0, board.Vein(Cell(board, 1, 0)));
            Assert.AreEqual(-1, board.Vein(Cell(board, 3, 0)));
        }

        [Test]
        public void BareGroundStopsTheVeinDead()
        {
            var board = Board(
                "Rr.r",
                "bggg",
                "....");

            Assert.AreEqual(0, board.Vein(Cell(board, 1, 0)));
            Assert.AreEqual(-1, board.Vein(Cell(board, 3, 0)));
        }

        /// <summary>
        /// <b>Light is read off the arrangement rather than stored</b>, which is the one thing
        /// that makes a careless swap cost something. Pull the gem that feeds a vein out of it
        /// and the whole vein goes out.
        /// </summary>
        [Test]
        public void BreakingAVeinPutsItOut()
        {
            var board = Board(
                "Rrrr",
                "bggg",
                "....");

            Assert.AreEqual(0, board.Vein(Cell(board, 3, 0)));

            var log = board.Play(new PrismMove(Cell(board, 1, 0), Cell(board, 1, 1)));

            Assert.IsNotNull(log);
            Assert.AreEqual(-1, board.Vein(Cell(board, 3, 0)));
            Assert.Greater(log.Dark, 0);
        }

        // ------------------------------------------------------------------ waking
        [Test]
        public void ACritterWakesWhenALitGemStandsBesideIt()
        {
            var board = Board(
                "Rg@",
                "rrb",
                "b.g");

            Assert.AreEqual(1, board.GoalsLeft);

            var log = board.Play(new PrismMove(Cell(board, 1, 0), Cell(board, 1, 1)));

            Assert.IsNotNull(log);
            Assert.AreEqual(1, log.Woke);
            Assert.IsTrue(board.IsFinished);
            Assert.AreEqual(PrismLayout.Awake, board.At(Cell(board, 2, 0)));
        }

        /// <summary>
        /// A critter that has woken never sleeps again, which is the only monotone quantity this
        /// mode has — and the reason the search terminates at all, because a swap on its own can
        /// be undone for ever.
        /// </summary>
        [Test]
        public void AWokenCritterStaysWokenWhenTheVeinIsBroken()
        {
            var board = Board(
                "Rg@",
                "rrb",
                "b.g");

            board.Play(new PrismMove(Cell(board, 1, 0), Cell(board, 1, 1)));
            Assert.IsTrue(board.IsFinished);

            board.Play(new PrismMove(Cell(board, 1, 0), Cell(board, 1, 1)));

            Assert.IsTrue(board.IsFinished);
            Assert.AreEqual(0, board.GoalsLeft);
        }

        /// <summary>
        /// A board dealt with a vein already against a sleeper is a board whose first move its
        /// author played — Budburst's "authored settled" rule, and the mode's reader refuses one.
        /// </summary>
        [Test]
        public void ABoardWithLightAlreadyOnACritterIsStirred()
        {
            Assert.IsTrue(Board(
                "Rr@",
                "bgg",
                "...").Stirred);

            Assert.IsFalse(Board(
                "Rg@",
                "rrb",
                "b.g").Stirred);
        }

        // ------------------------------------------------------------------ what is provable
        /// <summary>
        /// <b>Certain rather than clever</b> (invariant 28f). Which cells hold gems never
        /// changes, so a critter whose gems belong to a run no lantern touches can never be
        /// woken however the colours are arranged.
        /// </summary>
        [Test]
        public void ACritterInARunNoLanternTouchesIsMarooned()
        {
            var layout = Layout(
                "Rrg.",
                "bgg.",
                "....",
                "..g@");

            Assert.AreEqual(1, layout.Marooned.Length);
            Assert.AreEqual(3 * 4 + 3, layout.Marooned[0]);

            Assert.IsTrue(PrismBoard.Build(layout).Stranded);
        }

        [Test]
        public void ACritterALanternCouldReachIsNotMarooned()
        {
            var layout = Layout(
                "Rrg",
                "bgr",
                "bg@");

            Assert.AreEqual(0, layout.Marooned.Length);
            Assert.IsFalse(PrismBoard.Build(layout).Stranded);
        }

        /// <summary>
        /// The fail state is the meter and nothing else: nothing here is ever consumed, so there
        /// is always a swap left to make.
        /// </summary>
        [Test]
        public void ABoardOfMixedGemsAlwaysHasAMoveLeft()
        {
            var board = Board(
                "Rrg",
                "bgr",
                "bg@");

            Assert.IsTrue(board.AnyMove);

            board.Play(new PrismMove(Cell(board, 1, 0), Cell(board, 2, 0)));
            Assert.IsTrue(board.AnyMove);
        }

        // ------------------------------------------------------------------ the log
        /// <summary>
        /// <b>The lit deeds are in flood order out of the lantern</b>, which is what lets the view
        /// draw a vein running outward rather than switching a set of cells on at once. It is the
        /// one thing here no numeric gate could ever see going wrong (invariant 30i).
        /// </summary>
        [Test]
        public void LitCellsAreLoggedOutwardFromTheLantern()
        {
            var board = Board(
                "Rgrr",
                "brgg",
                "....");

            var log = board.Play(new PrismMove(Cell(board, 1, 0), Cell(board, 1, 1)));
            Assert.IsNotNull(log);

            var lit = new List<int>();
            foreach (var deed in log.Deeds)
                if (deed.Deed == PrismDeed.Lit) lit.Add(deed.At);

            Assert.AreEqual(new[] { Cell(board, 1, 0), Cell(board, 2, 0), Cell(board, 3, 0) },
                            lit.ToArray());
        }

        [Test]
        public void ARefusedSwapChangesNothingAndLogsNothing()
        {
            var board = Board(
                "Rrr",
                "bgr",
                "..@");

            Assert.IsNull(board.Play(new PrismMove(Cell(board, 1, 0), Cell(board, 2, 0))));
            Assert.AreEqual('r', board.At(Cell(board, 1, 0)));
        }

        // ------------------------------------------------------------------ the search
        /// <summary>
        /// Both halves of the reader's contract in one case: a board that deals anything is
        /// refused, because a field of gems is everything the level hands over and that is what
        /// makes par searchable at all.
        /// </summary>
        [Test]
        public void AFieldThatDealsAnythingIsRefused()
        {
            var dto = new LevelDto
            {
                id = "p01_sample",
                prism = new ProtoDto
                {
                    width = 3,
                    height = 3,
                    rows = new[] { "Rg@", "rrb", "b.g" },
                    cores = "R",
                },
            };

            var mode = LevelModes.Find(GameMode.Prism);
            Assert.NotNull(mode);

            var problems = new List<string>();
            Assert.IsFalse(mode.TryRead(dto, LevelId.Parse(dto.id), problems, out _));
            Assert.AreEqual(1, problems.Count);
        }

        [Test]
        public void TheSampleFieldIsSolvedInOneSwap()
        {
            var answer = ProtoSearch.Solve(new PrismFuture(Board(
                "Rg@",
                "rrb",
                "b.g")));

            Assert.IsTrue(answer.Proved);
            Assert.AreEqual(1, answer.Par);
        }
    }
}
