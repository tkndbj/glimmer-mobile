using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The rules of Emberforge, one clause at a time.
    ///
    /// <para>
    /// <c>ProtoLadderTests</c> pins the shipped walls, which catches a rule that has changed but
    /// says nothing about <em>which</em> clause moved. These are the clauses: the ones that were
    /// hard to get right, the ones the offline mirror could diverge on silently, and the ones
    /// whose failure would be invisible on a board — a wrong answer that still validates, still
    /// derives a plausible par and still ships.
    /// </para>
    /// <para>
    /// <b>Every case is written as a picture and asserted as a picture.</b> A wall is a rectangle
    /// of characters and so is the answer, so a case that asserted on counts would pass for a
    /// board that had settled into the wrong shape — which is the whole class of fault the
    /// gravity clauses are about.
    /// </para>
    /// </summary>
    public sealed class EmberRuleTests
    {
        // ------------------------------------------------------------------ scaffolding
        static EmberLayout Wall(params string[] rows)
        {
            Assert.IsTrue(ProtoGrid.TryRead(rows, rows[0].Length, rows.Length,
                                            EmberLayout.Letters, out var grid, out string error),
                          error);

            var layout = new EmberLayout(grid, 0);
            Assert.IsNull(layout.Fault, layout.Fault);
            return layout;
        }

        static EmberBoard Built(params string[] rows) => EmberBoard.Build(Wall(rows));

        /// <summary>The wall as it stands, row by row — which is what every clause asserts on.</summary>
        static string[] Picture(EmberBoard board)
        {
            var rows = new string[board.Height];

            for (int y = 0; y < board.Height; y++)
            {
                var sb = new System.Text.StringBuilder(board.Width);
                for (int x = 0; x < board.Width; x++) sb.Append(board.At(y * board.Width + x));
                rows[y] = sb.ToString();
            }

            return rows;
        }

        static void Shows(EmberBoard board, params string[] expected)
        {
            var got = Picture(board);
            Assert.AreEqual(string.Join("/", expected), string.Join("/", got));
        }

        static int At(EmberBoard board, int x, int y) => y * board.Width + x;

        static EmberBlast Play(EmberBoard board, int fromX, int fromY, int toX, int toY)
        {
            var log = board.Fire(new EmberMove(At(board, fromX, fromY), At(board, toX, toY)));
            Assert.NotNull(log, $"({fromX},{fromY}) -> ({toX},{toY}) was refused");
            return log;
        }

        static EmberBlast Tap(EmberBoard board, int x, int y)
        {
            var log = board.Fire(EmberMove.Tap(At(board, x, y)));
            Assert.NotNull(log, $"the tap on ({x},{y}) was refused");
            return log;
        }

        // ================================================================== what is a move
        /// <summary>
        /// A swap that lines nothing up is not a move, and that is what keeps the search finite.
        ///
        /// A no-op is a self edge, and a self edge is a layer a breadth-first walk never leaves.
        /// It is the same rule the wall shows the player: an input that would achieve nothing is
        /// refused out loud rather than charged for.
        /// </summary>
        [Test]
        public void ASwapThatLinesNothingUpIsNotAMoveAtAll()
        {
            var board = Built("rgrb",
                              "gbgr",
                              "rgCb",
                              "bgrg");

            Assert.IsNull(board.Fire(new EmberMove(At(board, 0, 0), At(board, 1, 0))));
        }

        /// <summary>
        /// A tap is a move only on an ember. A shard is dragged, which is the one rule this mode
        /// cannot show for itself and the only sentence its screen ever prints.
        /// </summary>
        [Test]
        public void OnlyAnEmberIsTapped()
        {
            var board = Built("rgOb",
                              "gbgr",
                              "rgCb",
                              "bgrg");

            Assert.IsTrue(board.Aim(At(board, 2, 0), At(board, 2, 0), out var aim));
            Assert.AreEqual(EmberAim.Fire, aim);

            Assert.IsFalse(board.Aim(At(board, 0, 0), At(board, 0, 0), out _));
            Assert.IsFalse(board.Aim(At(board, 2, 2), At(board, 2, 2), out _),
                           "a cage is not something a finger picks up");
        }

        /// <summary>A drag that crossed the wall is not a swap, however the two cells read.</summary>
        [Test]
        public void OnlyNeighboursAreSwapped()
        {
            var board = Built("rgrb",
                              "gbgr",
                              "rgCb",
                              "bgrg");

            Assert.IsFalse(board.Aim(At(board, 0, 0), At(board, 3, 3), out _));
            Assert.IsFalse(board.Aim(At(board, 0, 0), At(board, 1, 1), out _),
                           "a diagonal is not a neighbour");
        }

        // ================================================================== the fuse
        /// <summary>
        /// Three alike fuse into one ember, and it stands on the cell the finger <em>ended</em>
        /// on.
        ///
        /// <b>The most load-bearing clause in the mode</b> (invariant 20m): what the player made
        /// is where they made it, and everything they can plan afterwards follows from where it
        /// ended up.
        /// </summary>
        [Test]
        public void ThreeAlikeFuseIntoOneEmberOnTheCellTheFingerEndedOn()
        {
            var board = Built("rrbg",
                              "gbrg",
                              "b#Cb",
                              "gbrb");

            // The r at (2,1) is dragged up into (2,0), which makes r,r,r across the top.
            var log = Play(board, 2, 1, 2, 0);

            Assert.AreEqual(1, log.Forged);
            Shows(board, "..Og",
                         "gbbg",
                         "b#Cb",
                         "gbrb");
        }

        /// <summary>
        /// And the other way round reaches exactly the same wall, which is what lets
        /// <see cref="EmberBoard.Moves"/> offer a fuse once instead of twice.
        ///
        /// <para>
        /// <b>It is provable and it was got wrong first.</b> A swap exchanges two different
        /// characters and a fused group is uniform, so the group can hold at most one of the two
        /// cells and settles on the same one either way. That argument only holds for the pass
        /// the player's own swap caused — the first cut carried the swap into every later beat
        /// of the cascade, and a group four beats downstream could still tell the two directions
        /// apart. What that cost was not correctness: it was <c>ProtoAnswer.Ways</c> counted
        /// twice at every depth, on every board, in the one reading invariant 5d is about.
        /// </para>
        /// </summary>
        [Test]
        public void EitherWayRoundAFuseReachesTheSameWall()
        {
            var one = Built("rrbg", "gbrg", "b#Cb", "gbrb");
            var other = Built("rrbg", "gbrg", "b#Cb", "gbrb");

            Play(one, 2, 1, 2, 0);
            Play(other, 2, 0, 2, 1);

            Shows(other, Picture(one));
        }

        /// <summary>
        /// A merge is the exception, and the only place in the mode where <em>where you let go</em>
        /// decides something: both embers are spent and the star goes off on the cell the finger
        /// ended on.
        /// </summary>
        [Test]
        public void AMergeGoesOffWhereTheFingerEnded()
        {
            var left = Built("Cgbgr", "gbrbg", "brOOr", "gbrbg", "rgbrC");
            var right = Built("Cgbgr", "gbrbg", "brOOr", "gbrbg", "rgbrC");

            var toLeft = left.Fire(new EmberMove(At(left, 3, 2), At(left, 2, 2)));
            var toRight = right.Fire(new EmberMove(At(right, 2, 2), At(right, 3, 2)));

            Assert.NotNull(toLeft);
            Assert.NotNull(toRight);
            Assert.AreEqual(1, toLeft.Stars);
            Assert.AreEqual(1, toRight.Stars);

            Assert.AreNotEqual(string.Join("/", Picture(left)), string.Join("/", Picture(right)),
                "a star fired from one cell and a star fired from its neighbour are two "
                + "different walls - that is the whole reason a merge is offered both ways round");
        }

        /// <summary>
        /// Two runs that share a cell are one group and make one ember — the genre's own rule
        /// for an L or a T.
        ///
        /// Getting it wrong the easy way (flood filling alike neighbours) would silently turn two
        /// matches into one and hand the player a single ember for six shards; getting it wrong
        /// the other way would hand out two for five.
        /// </summary>
        [Test]
        public void TwoRunsSharingACellAreOneGroupAndOneEmber()
        {
            var board = Built("yryyb",
                              "gybgr",
                              "bygrC",
                              "grbgr",
                              "rgbrg");

            // The y at (0,0) is dragged into (1,0), which completes y,y,y across the top *and*
            // y,y,y down the second column. Five cells, one group, one ember.
            var log = Play(board, 0, 0, 1, 0);

            Assert.AreEqual(1, log.Forged, "an L is one group");
            Assert.AreEqual(4, log.Took, "five shards in, one ember out");
        }

        /// <summary>
        /// Two runs that do not share a cell are two groups and two embers, however close they
        /// lie. The other side of the clause above, and the one a flood fill gets wrong.
        /// </summary>
        [Test]
        public void TwoRunsThatOnlyTouchAreTwoGroups()
        {
            var board = Built("ggbrr",
                              "rrgbb",
                              "gbCbg",
                              "brgrb",
                              "grbgr");

            // Dragging the b at (2,0) down into (2,1) makes g,g,g across the top and r,r,r,r,r
            // across the second row - two runs, lying against each other, sharing nothing.
            var log = Play(board, 2, 0, 2, 1);

            Assert.AreEqual(2, log.Forged);
        }

        // ================================================================== the blast
        /// <summary>
        /// Tapping an ember throws a cross down its whole row and its whole column, and stone
        /// stops it dead.
        /// </summary>
        [Test]
        public void ACrossSweepsItsRowAndColumnAndStoneStopsIt()
        {
            var board = Built("bgrbg",
                              "grbgr",
                              "r#Ogb",
                              "gbrbC",
                              "rgbgr");

            Tap(board, 2, 2);

            // Left along the row the beam dies on the stone at (1,2), so (0,2) keeps its shard
            // and the whole of column nought stands. Everything else in the row and the column
            // goes, and what was above the column falls into it.
            Shows(board, "bg...",
                         "gr.bg",
                         "r#.gr",
                         "gb.bC",
                         "rg.gr");
        }

        /// <summary>
        /// A beam frees a cage and carries straight on, which is the picture the whole mode
        /// exists to produce: one cross opening several at once.
        /// </summary>
        [Test]
        public void ABeamFreesEveryCageInItsLineAndCarriesOn()
        {
            var board = Built("rgbgr",
                              "CbObC",
                              "grbgr",
                              "bgrbg",
                              "rbgrb");

            var log = Tap(board, 2, 1);

            Assert.AreEqual(2, log.Freed, "one cross, both cages");
            Assert.Zero(board.GoalsLeft);
            Assert.IsTrue(board.IsFinished);
        }

        /// <summary>
        /// A warden stops a beam and takes two of them: one cracks the plating and the beam dies
        /// there, and the next finishes it.
        ///
        /// <b>Two beams that reach one in the same beat take both plates</b>, which is what
        /// "read the wall as it stands, then apply" buys — the answer is the same whichever
        /// blast the loop walked first. That case is impossible to author in five characters and
        /// is the reason the accumulation is counted rather than latched.
        /// </summary>
        [Test]
        public void AWardenStopsABeamAndTakesTwo()
        {
            var board = Built("rgbgr",
                              "gbOWb",
                              "brgbr",
                              "gbrgb",
                              "rgbrC");

            var log = Tap(board, 2, 1);

            Assert.AreEqual(1, log.Crosses);
            Assert.Zero(log.Wrecked, "one beam only cracks it");
            Assert.AreEqual(EmberLayout.Scarred, board.At(At(board, 3, 1)));
            Assert.AreEqual('b', board.At(At(board, 4, 1)), "and the beam died on the plating");
        }

        /// <summary>A scarred warden goes on the next beam that reaches it, and not before.</summary>
        [Test]
        public void AScarredWardenGoesOnTheNextBeam()
        {
            var board = Built("rgbgr",
                              "gbOWb",
                              "brgbr",
                              "gbrgb",
                              "rgbrC");

            Tap(board, 2, 1);
            Assert.AreEqual(EmberLayout.Scarred, board.At(At(board, 3, 1)));
            Assert.AreEqual(2, board.GoalsLeft, "a cracked warden is still a warden");

            // A second wall, played the same way, with the plating already gone.
            var scarred = Built("rgbgr",
                                "gbOWb",
                                "brgbr",
                                "gbrgb",
                                "rgbrC");
            scarred.Fire(EmberMove.Tap(At(scarred, 2, 1)));

            var again = scarred.Fork();
            Assert.IsFalse(again.IsFinished);
        }

        // ================================================================== the chain
        /// <summary>
        /// A beam sets off any ember it crosses, and that ember throws its own cross on the next
        /// beat. This is the whole reason the mode exists.
        /// </summary>
        [Test]
        public void ABeamSetsOffEveryEmberItCrossesAndThoseFireInTurn()
        {
            var board = Built("rgbgr",
                              "ObObO",
                              "grbCr",
                              "bgrbg",
                              "rgbrb");

            var log = Tap(board, 2, 1);

            Assert.AreEqual(2, log.Ignited, "both of the others were reached");
            Assert.AreEqual(3, log.Crosses, "the tap, and the two it lit");
            Assert.GreaterOrEqual(log.Beats, 2, "a chain is more than one beat");
        }

        /// <summary>
        /// And a chain dies the instant it reaches wall that is not already primed — which is
        /// invariant 20j's third test, and what stops a cascade being a solvent.
        /// </summary>
        [Test]
        public void AChainStopsWhereThereIsNoEmberToCatchIt()
        {
            var board = Built("rgbgr",
                              "gbObg",
                              "grbgr",
                              "bgrbC",
                              "rgbrg");

            var log = Tap(board, 2, 1);

            Assert.Zero(log.Ignited);
            Assert.AreEqual(1, log.Crosses);
        }

        // ================================================================== the star
        /// <summary>
        /// Two embers pushed together combine into a star, which takes the diagonals with it.
        /// </summary>
        [Test]
        public void TwoEmbersCombineIntoAStarOnTheDiagonals()
        {
            var board = Built("Cgbgr",
                              "gbrbg",
                              "brOOr",
                              "gbrbg",
                              "rgbrC");

            // Fired from (2,2), whose diagonals run straight into both corners.
            var log = Play(board, 3, 2, 2, 2);

            Assert.AreEqual(1, log.Stars);
            Assert.Zero(log.Crosses);
            Assert.AreEqual(2, log.Freed, "the diagonals reach both corners");
        }

        /// <summary>A cross does not, which is what makes a star worth two embers.</summary>
        [Test]
        public void ACrossLeavesTheDiagonalsAlone()
        {
            var board = Built("Cgbgr",
                              "gbrbg",
                              "brObr",
                              "gbrgg",
                              "rgbrC");

            var log = Tap(board, 2, 2);

            Assert.AreEqual(1, log.Crosses);
            Assert.Zero(log.Freed, "neither cage is on the row or the column");
        }

        // ================================================================== gravity
        /// <summary>
        /// Shards slide down their column and the fittings do not move — so stone holds the wall
        /// above it up as well as stopping a beam.
        /// </summary>
        [Test]
        public void TheWallSlumpsIntoItsOwnBreachesAndFittingsHoldItUp()
        {
            var board = Built("brgbr",
                              "gbrgg",
                              "r#Obr",
                              "gbrgb",
                              "rgbrC");

            Tap(board, 2, 2);

            // Column one keeps its whole stack, because the stone at (1,2) is standing under it
            // and the beam died on the stone before it ever reached column nought.
            Shows(board, "br...",
                         "gb.br",
                         "r#.gg",
                         "gb.gb",
                         "rg.rC");
        }

        /// <summary>
        /// Frost stops nothing, so a beam melts it — and the column above it falls into the hole
        /// it leaves. That is what it does that stone cannot (invariant 26g's test).
        /// </summary>
        [Test]
        public void FrostMeltsAndTheWallAboveItComesDown()
        {
            var board = Built("brgbr",
                              "gbrgg",
                              "r*Obr",
                              "gbrgb",
                              "rgbrC");

            Tap(board, 2, 2);

            // The beam went straight through the frost and on to (0,2), and column one - which
            // the frost had been holding up - has dropped by one.
            Shows(board, ".....",
                         "br.br",
                         "gb.gg",
                         "gb.gb",
                         "rg.rC");
        }

        // ================================================================== the invariants
        /// <summary>
        /// Nothing is ever added to a wall, which is the monotone claim the whole search rests on
        /// (invariant 20j's second test).
        ///
        /// Asserted rather than argued: every legal move from a handful of positions strictly
        /// reduces the number of pieces standing, so the state graph is a DAG, a run always ends
        /// and a breadth-first walk terminates by construction.
        /// </summary>
        [Test]
        public void EveryLegalMoveTakesSomethingOffTheWallAndAddsNothing()
        {
            var board = Built("rrbgr",
                              "gbrbO",
                              "brOgr",
                              "gbrbg",
                              "rgbrC");

            var moves = new List<EmberMove>();
            board.Moves(moves);

            Assert.IsNotEmpty(moves);

            int before = Standing(board);

            foreach (var move in moves)
            {
                var forked = board.Fork();
                Assert.NotNull(forked.Fire(move), "Moves offered something Fire refuses");

                Assert.Less(Standing(forked), before,
                            "a move that did not shrink the wall is a self edge, and a self edge "
                            + "is a layer the search never leaves");
            }
        }

        static int Standing(EmberBoard board)
        {
            int n = 0;
            for (int i = 0; i < board.Width * board.Height; i++)
                if (board.At(i) != EmberLayout.Breach) n++;
            return n;
        }

        /// <summary>
        /// Two walls a plate apart are not the same state.
        ///
        /// The key covers everything a rule reads and nothing else. Too little and the search
        /// calls two different walls one wall, which under-reports par — the direction that hands
        /// out stars nobody earned.
        /// </summary>
        [Test]
        public void TwoWallsAPlateApartAreNotTheSameState()
        {
            var plated = Built("rgbgr",
                               "gbrbg",
                               "brWbr",
                               "gbrbg",
                               "rgbrg");

            var scarred = plated.Fork();

            var a = new List<byte>();
            var b = new List<byte>();
            plated.Write(a);
            scarred.Write(b);
            CollectionAssert.AreEqual(a, b, "a fork of a wall is the same wall");

            // Crack the plating on one of them and the two must part company.
            var wall = Built("rgbgr",
                             "gbObg",
                             "brWbr",
                             "gbrbg",
                             "rgbrg");
            Tap(wall, 2, 1);

            b.Clear();
            wall.Write(b);
            CollectionAssert.AreNotEqual(a, b);
        }

        /// <summary>
        /// A wall with nothing to fuse and no ember standing has no move at all, and it is
        /// <em>stranded</em> — which is the certainty that decides whether it would be honest to
        /// sell a continue (invariant 28f).
        /// </summary>
        [Test]
        public void AWallWithNothingLeftToFuseIsStrandedAndHasNoMove()
        {
            var board = Built("rg.C.",
                              ".....",
                              ".....",
                              ".....",
                              "....b");

            Assert.IsFalse(board.AnyMove);
            Assert.IsTrue(board.Stranded);
        }

        /// <summary>
        /// And it under-reports rather than over-reports: a wall that could still raise one ember
        /// is never called stranded, whatever the geometry says.
        /// </summary>
        [Test]
        public void StrandedUnderReportsAndNeverOverReports()
        {
            // Three reds are on the wall but nowhere near each other. Geometry says nothing can
            // be done; the reading refuses to say so, because it decides whether money changes
            // hands and a wrong "yes" refuses a rescue to somebody who could still have won.
            var board = Built("r...C",
                              ".....",
                              "..r..",
                              ".....",
                              "r....");

            Assert.IsFalse(board.Stranded);
        }

        /// <summary>
        /// A wall is authored settled, and <see cref="EmberBoard.Stirred"/> is what says so.
        ///
        /// It matters more here than in any mode before it, because the cascade would run on from
        /// a wall that fused before anybody touched it — so the wall the player meets would not
        /// be the wall that was authored, proved or graded.
        /// </summary>
        [Test]
        public void AWallThatWouldFuseByItselfIsStirred()
        {
            Assert.IsTrue(Built("rrrbg",
                                "gbrbg",
                                "brCbr",
                                "gbrbg",
                                "rgbrg").Stirred);

            Assert.IsFalse(Built("yggyb",
                                 "bgygg",
                                 "yyCyg",
                                 "grryy",
                                 "yrggb").Stirred);
        }

        // ================================================================== the reader
        /// <summary>
        /// The mode refuses a wall that deals anything, because a wall is everything the level
        /// hands over — and a board with a future nothing can predict cannot be searched at all
        /// (invariant 26).
        /// </summary>
        [Test]
        public void AWallThatDealsAnythingIsRefused()
        {
            var dto = new LevelDto
            {
                id = "t_dealt",
                ember = new ProtoDto
                {
                    width = 4,
                    height = 4,
                    rows = new[] { "brgb", "grgr", "rbCb", "bggr" },
                    cores = "RGB",
                },
            };

            var problems = new List<string>();
            Assert.IsFalse(new EmberMode().TryRead(dto, LevelId.Parse("t_dealt"), problems,
                                                   out _));
            Assert.IsNotEmpty(problems);
        }

        /// <summary>And it refuses one that is not authored settled.</summary>
        [Test]
        public void AStirredWallIsRefusedByTheReader()
        {
            var dto = new LevelDto
            {
                id = "t_stirred",
                ember = new ProtoDto
                {
                    width = 4,
                    height = 4,
                    rows = new[] { "rrrb", "grgr", "rbCb", "bggr" },
                },
            };

            var problems = new List<string>();
            Assert.IsFalse(new EmberMode().TryRead(dto, LevelId.Parse("t_stirred"), problems,
                                                   out _));
            Assert.IsNotEmpty(problems);
        }
    }
}
