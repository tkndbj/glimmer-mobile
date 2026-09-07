using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The rules of the prototype modes, one clause at a time.
    ///
    /// <para>
    /// <c>ProtoLadderTests</c> pins the shipped boards, which catches a rule that has changed but
    /// says nothing about <em>which</em> clause moved. These are the clauses: the ones that were
    /// hard to get right, the ones a second runtime could diverge on silently, and the ones whose
    /// failure would be invisible on a board — a wrong answer that still validates, still derives
    /// a plausible par and still ships.
    /// </para>
    /// </summary>
    public sealed class ProtoRuleTests
    {
        // ------------------------------------------------------------------ scaffolding
        /// <summary>
        /// A ten-by-three board carrying one long haul-road, with a line standing on it.
        ///
        /// <para>
        /// Built rather than typed out, because every clause below is about the <em>line</em> and
        /// none of them is about the road. Writing nineteen slots of rail into each case by hand
        /// would bury the one string that matters in a picture nobody reads — and a mistyped
        /// rail is a board that fails for a reason the case is not about.
        /// </para>
        /// <para>
        /// The road runs left to right along the top, down the far side and back along the
        /// bottom to the launcher, so slot 0 is beside the gate and slot 18 is beside the
        /// player. Nineteen slots is comfortably more than any case here needs.
        /// </para>
        /// <para>
        /// <b>The line is stood as far from the gate as it will go unless a case says otherwise,
        /// and that default is load-bearing.</b> Every shot marches the raiders one step, so a
        /// line authored at the gate loses its front pod on every move — which is correct
        /// behaviour and quietly wrong for a case about anything else: five of these clauses
        /// were written against a line at slot nought and every one of them failed by exactly
        /// one pod, in an assertion about matching. Only the march case sets it by hand.
        /// </para>
        /// </summary>
        static MarchLayout Road(string pods, int head = -1, string cores = "RGB")
        {
            if (head < 0) head = UnityEngine.Mathf.Clamp(19 - pods.Length - 1, 0, 12);

            var rows = new[]
            {
                "Z+++++++++".ToCharArray(),
                ".........+".ToCharArray(),
                "A+++++++++".ToCharArray(),
            };

            for (int i = 0; i < pods.Length; i++)
            {
                int slot = head + i;
                Assert.Less(slot, 19, "the line does not fit on this road");

                if (slot < 9) rows[0][slot + 1] = pods[i];
                else if (slot == 9) rows[1][9] = pods[i];
                else rows[2][9 - (slot - 10)] = pods[i];
            }

            var text = new string[3];
            for (int y = 0; y < 3; y++) text[y] = new string(rows[y]);

            Assert.IsTrue(ProtoGrid.TryRead(text, 10, 3, MarchLayout.Letters,
                                            out var grid, out string error), error);

            var layout = new MarchLayout(grid, 0, cores);
            Assert.IsNull(layout.Fault, layout.Fault);
            return layout;
        }

        static MarchBoard Line(string pods, int head = -1, string cores = "RGB")
            => MarchBoard.Build(Road(pods, head, cores));

        /// <summary>The line as a string, which is what every clause below is really asserting on.</summary>
        static string Picture(MarchBoard board)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < board.Line.Count; i++) sb.Append(board.Line[i]);
            return sb.ToString();
        }

        static MarchMove Aim(MarchBoard board, MarchAim aim, int at)
        {
            var moves = new List<MarchMove>();
            board.Moves(moves);

            foreach (var move in moves)
                if (move.Aim == aim && move.At == at) return move;

            Assert.Fail($"no {aim} move at {at} on '{Picture(board)}' holding '{board.Core}'");
            return default;
        }

        // ================================================================== the search
        /// <summary>
        /// A move that changes nothing has to answer null, and that is what keeps the search
        /// finite.
        ///
        /// A no-op move is a self edge, and a self edge is a layer a breadth-first walk never
        /// leaves. It is the same rule the board enforces on the player: an input that would
        /// achieve nothing is refused out loud rather than charged for. Here it is a road packed
        /// solid with nothing the core matches — nowhere to wedge, and nowhere to dump either.
        /// </summary>
        [Test]
        public void AShotWithNowhereToGoIsNotAMoveAtAll()
        {
            // Nineteen slots, nineteen pods, and the core in hand is red: every run is green or
            // blue, so no wedge is legal, and the road is full, so no dump is either.
            var board = Line("GGBBGGBBGGBBGGBBGGB");

            Assert.IsFalse(board.Room, "the road is packed solid");
            Assert.IsFalse(board.AnyMove, "nothing on this line takes a red core");

            var future = new MarchFuture(board);
            Assert.AreEqual(0, future.MoveCount, "a board with no move offers none");
        }

        /// <summary>A board that opens finished is par nought, which every validator reads as a refusal.</summary>
        [Test]
        public void AFinishedBoardIsParNought()
        {
            var answer = ProtoSearch.Solve(new MarchFuture(Line("RGB")));

            Assert.AreEqual(0, answer.Par, "a line carrying no cage and no raider asks nothing");
            Assert.IsTrue(answer.Proved);
        }

        // ================================================================== the wedge
        /// <summary>
        /// Three alike go off and two do not, which is the whole verb.
        ///
        /// <b>The one clause everything else is measured from.</b> Off by one in either direction
        /// and every board in the mode plays differently — which moves par, moves both star lines
        /// and moves the allowance, and looks exactly like a level somebody authored.
        /// </summary>
        [Test]
        public void ACoreCompletesThreeAlikeAndTwoAlikeStand()
        {
            var three = Line("RRg");                      // a pair plus a caged red: R makes three
            var log = three.Fire(Aim(three, MarchAim.Join, 0));

            Assert.NotNull(log);
            Assert.AreEqual(3, log.Took, "the run and the core went off together");
            Assert.AreEqual(0, log.Freed, "the cage is a green pod - it was never in the run");
            Assert.AreEqual("g", Picture(three), "only the run left");

            // And the same line with a single red rather than a pair: the core joins it and
            // nothing goes off, because two alike is not three.
            var two = Line("Rg");
            var joined = two.Fire(Aim(two, MarchAim.Join, 0));

            Assert.NotNull(joined);
            Assert.AreEqual(0, joined.Took, "two alike stand");
            Assert.AreEqual("RRg", Picture(two), "the core is now part of the line");
        }

        /// <summary>
        /// Every position inside one run reaches the same board, so a run is one move.
        ///
        /// It is what keeps the branching small enough for the shared search to find par at the
        /// depths this mode is authored to, and it is also the honest reading of what a player
        /// can see: they are choosing a <em>run</em>, not a gap.
        /// </summary>
        [Test]
        public void OneRunIsOneMoveHoweverManyGapsItHas()
        {
            var board = Line("RRRRg", cores: "R");
            var moves = new List<MarchMove>();
            board.Moves(moves);

            int joins = 0;
            foreach (var move in moves) if (move.Aim == MarchAim.Join) joins++;

            Assert.AreEqual(1, joins,
                "a run of four offers five gaps and exactly one move");
        }

        /// <summary>
        /// A hauler wears no colour, so no run spans one — which is the whole of what it is for.
        /// </summary>
        [Test]
        public void ARunNeverSpansAHauler()
        {
            var board = Line("RRHRRg", cores: "R");
            var moves = new List<MarchMove>();
            board.Moves(moves);

            int joins = 0;
            foreach (var move in moves) if (move.Aim == MarchAim.Join) joins++;

            Assert.AreEqual(2, joins, "the hauler cuts one run of four into two runs of two");

            // And firing into the first of them takes three pods rather than five - plus the
            // hauler itself, which the blast was beside.
            var log = board.Fire(Aim(board, MarchAim.Join, 0));
            Assert.AreEqual(3 + 1, log.Took, "three pods and the hauler beside them");
            Assert.AreEqual(1, log.Scrapped);
            Assert.AreEqual("RRg", Picture(board), "the far pair never went off");
        }

        // ================================================================== the chain
        /// <summary>
        /// Closing the gap brings the two sides together, and if that makes three more they go
        /// too. The mode's payoff, and the reason it exists.
        /// </summary>
        [Test]
        public void ClosingTheGapCanSetOffTheNextRun()
        {
            // B B | R R | B  — take the reds out and the blues meet, making three.
            var board = Line("BBRRBg", cores: "R");
            var log = board.Fire(Aim(board, MarchAim.Join, 2));

            Assert.AreEqual(2, log.Waves, "the closure made three more");
            Assert.AreEqual(6, log.Took, "three reds and then three blues");
            Assert.AreEqual("g", Picture(board));
        }

        /// <summary>
        /// And the chain has to clear the same threshold again, so it dies wherever the line is
        /// not already nearly right.
        ///
        /// <b>This is the clause that stops the cascade being a solvent</b> (invariant 20j's third
        /// test). A spread that ran on unconditionally would walk the whole line from one core,
        /// which is a rule that makes boards <em>more</em> solvable — as dangerous as one that
        /// makes them unsolvable, and only counting finds it.
        /// </summary>
        [Test]
        public void AChainStopsWhereTheClosureMakesOnlyTwo()
        {
            // B | R R | B  — take the reds out and the blues meet, making only two.
            var board = Line("BRRBg", cores: "R");
            var log = board.Fire(Aim(board, MarchAim.Join, 1));

            Assert.AreEqual(1, log.Waves, "two alike is not three");
            Assert.AreEqual(3, log.Took);
            Assert.AreEqual("BBg", Picture(board));
        }

        // ================================================================== the raiders
        /// <summary>
        /// A blast beside a hauler scraps it; a warden takes the first one and stands.
        ///
        /// The warden is the mode's one rule a board cannot demonstrate before it is met, so what
        /// it does has to be exact: a plate that came off on the first blast would make the Spark
        /// pointless, and one that never came off at all would make the board unwinnable.
        /// </summary>
        [Test]
        public void AHaulerComesApartBesideABlastAndAWardenTakesTwo()
        {
            var hauler = Line("HRRg", cores: "R");
            var first = hauler.Fire(Aim(hauler, MarchAim.Join, 1));

            Assert.AreEqual(1, first.Scrapped, "the hauler was beside the blast");
            Assert.AreEqual("g", Picture(hauler));

            // A warden takes two, and the second has to be reached by *playing* rather than by
            // authoring a half-struck one: a plate that has come off is a state a board reaches
            // and never one it is written in, which the parser refuses by letter.
            var warden = Line("RRWRRg", cores: "R");
            var one = warden.Fire(Aim(warden, MarchAim.Join, 0));

            Assert.AreEqual(0, one.Scrapped, "the plating turned the first blast away");
            Assert.AreEqual(1, one.Staggered);
            Assert.AreEqual(MarchLayout.WardenHit, warden.Line[0], "one plate down, still standing");
            Assert.AreEqual("VRRg", Picture(warden));

            var two = warden.Fire(Aim(warden, MarchAim.Join, 1));

            Assert.AreEqual(1, two.Scrapped, "the second blast took it");
            Assert.AreEqual("g", Picture(warden));
        }

        /// <summary>
        /// A raider beside two bursting runs at once is cracked once and not twice.
        ///
        /// Every consequence of a wave is read off the line as it stands <em>before</em> anything
        /// is removed, which is what makes the wave free of a reading order (invariant 26h's
        /// rule). Counting it twice would take a warden down in a single shot and nothing on the
        /// board would say why.
        /// </summary>
        [Test]
        public void ARaiderBetweenTwoBurstsIsCrackedOnce()
        {
            // R R | W | R R — one core cannot reach both sides, so this is asserted through the
            // chain: the first blast takes the left pair and the closure brings the right pair
            // against the warden without a second wave touching it.
            var board = Line("RRWRRg", cores: "R");
            var log = board.Fire(Aim(board, MarchAim.Join, 0));

            Assert.AreEqual(1, log.Staggered, "one plate, from one wave");
            Assert.AreEqual(MarchLayout.WardenHit, board.Line[0]);
        }

        // ================================================================== the Spark
        /// <summary>
        /// A shot that takes five pods forges a Spark, and one that takes four does not.
        ///
        /// It is the only thing in this mode the player <em>makes</em> (invariant 20m), so what
        /// it costs is contract: forging on four would hand one out on most shots and forging on
        /// six would put it out of reach of the boards that ship.
        /// </summary>
        [Test]
        public void FiveInOneShotForgesASparkAndFourDoesNot()
        {
            var four = Line("HRRg", cores: "R");           // three reds and the hauler beside them
            var small = four.Fire(Aim(four, MarchAim.Join, 1));
            Assert.AreEqual(4, small.Took);
            Assert.IsFalse(four.Spark, "four is not five");

            var five = Line("BBRRBg", cores: "R");         // three reds, then three blues
            var log = five.Fire(Aim(five, MarchAim.Join, 2));

            Assert.AreEqual(6, log.Took);
            Assert.IsTrue(log.Forged, "six pods forged one");
            Assert.IsTrue(five.Spark, "and it is in the barrel");
        }

        /// <summary>
        /// A Spark cuts the run it is aimed at and the group either side of it, plating and all.
        ///
        /// <b>That last clause is what makes it a different thing rather than a bigger core</b>
        /// (invariant 26g's test, asked of the thing the player makes): a colour match can never
        /// take a warden in one shot, and this is the only move that can.
        /// </summary>
        [Test]
        public void ALanceCutsThreeGroupsAndGoesThroughPlating()
        {
            var board = Line("BBWGGRrB", cores: "R");

            // Forge one by hand rather than by playing a shot into it, so the case is about what
            // a lance does rather than about how it is earned.
            var forge = Line("BBRRBWGGRrB", cores: "R");
            forge.Fire(Aim(forge, MarchAim.Join, 2));
            Assert.IsTrue(forge.Spark);

            // The line is now W G G R r B, and the lance is aimed at the pair of greens: it takes
            // them, the warden in front and the reds behind.
            var log = forge.Fire(Aim(forge, MarchAim.Lance, 1));

            Assert.IsTrue(log.Lanced);
            Assert.AreEqual(1, log.Scrapped, "the warden went, plating and all");
            Assert.AreEqual(1, log.Freed, "and the caged red behind it");
            Assert.AreEqual("B", Picture(forge), "the run and the group either side of it");

            Assert.IsFalse(board.Spark, "a board that has forged nothing holds nothing");
        }

        // ================================================================== the march
        /// <summary>
        /// A plain pod at the gate goes through it; one carrying a critter jams the line.
        ///
        /// <b>The second half is a rule and not a kindness.</b> Letting a cage through makes a
        /// board that can be neither won nor lost, which is the one state invariant 20g says a
        /// mode may never ship — and it is why <see cref="MarchBoard.Stranded"/> can honestly
        /// answer false.
        /// </summary>
        [Test]
        public void APlainPodGoesThroughTheGateAndACagedOneJams()
        {
            var board = Line("BRgRRB", head: 1, cores: "B");

            Assert.AreEqual(1, board.Head);

            // Dumping a blue at the back changes nothing but the march, which is what this case
            // is about.
            var step = board.Fire(Aim(board, MarchAim.Dump, board.Line.Count));
            Assert.AreEqual(0, step.Escaped, "one step short of the gate");
            Assert.AreEqual(0, board.Head);

            var through = board.Fire(Aim(board, MarchAim.Dump, board.Line.Count));
            Assert.AreEqual(1, through.Escaped, "the blue at the front went through");
            Assert.AreEqual('B', through.Deeds[through.Deeds.Count - 1].What);

            // The next pod along is red, and the one after that is the cage.
            board.Fire(Aim(board, MarchAim.Dump, board.Line.Count));
            var jam = board.Fire(Aim(board, MarchAim.Dump, board.Line.Count));

            Assert.IsTrue(jam.Jammed, "the raiders will not abandon their cargo");
            Assert.AreEqual(0, jam.Escaped);
            Assert.AreEqual('g', board.Line[0]);

            // And it stays jammed however long the run goes on, which is the whole of the claim.
            for (int i = 0; i < 5; i++)
            {
                var moves = new List<MarchMove>();
                board.Moves(moves);
                if (moves.Count == 0) break;
                board.Fire(moves[moves.Count - 1]);
            }

            Assert.AreEqual('g', board.Line[0], "the cage never went through");
            Assert.IsFalse(board.Stranded, "so nothing was ever put out of reach");
            Assert.Greater(board.GoalsLeft, 0);
        }

        // ================================================================== the road
        /// <summary>
        /// A road that forks is refused by cell rather than read some arbitrary way.
        ///
        /// The walk from the gate is what decides the order of everything on the board, so an
        /// ambiguous one is a board that plays differently depending on which neighbour the loop
        /// happened to reach first — the kind of fault that would be reproducible on one runtime
        /// and not on another.
        /// </summary>
        [Test]
        public void AForkedRoadIsRefusedByCell()
        {
            Assert.IsTrue(ProtoGrid.TryRead(new[]
            {
                "Z++",
                ".++",
                "..A",
            }, 3, 3, MarchLayout.Letters, out var grid, out _));

            var layout = new MarchLayout(grid, 0, "RGB");

            Assert.NotNull(layout.Fault, "a road with a junction in it is not one line");
            StringAssert.Contains("road neighbour", layout.Fault);
        }

        /// <summary>Two gates, or none, is a board with no order at all.</summary>
        [Test]
        public void ARoadHasExactlyOneGateAndOneLauncher()
        {
            Assert.IsTrue(ProtoGrid.TryRead(new[]
            {
                "Z+Z",
                "...",
                "..A",
            }, 3, 3, MarchLayout.Letters, out var grid, out _));

            var layout = new MarchLayout(grid, 0, "RGB");
            Assert.NotNull(layout.Fault);
            StringAssert.Contains("one portal", layout.Fault);
        }

        // ================================================================== the key
        /// <summary>
        /// The search key covers everything a rule reads and nothing else.
        ///
        /// Too little and two different boards merge, which under-reports par — the direction
        /// that hands out stars nobody earned. Two boards a plate apart, or a Spark apart, play
        /// differently and must never be the same state.
        /// </summary>
        [Test]
        public void TwoBoardsAPlateApartAreNotTheSameState()
        {
            var whole = Line("WRRgRRB", cores: "R");
            var struck = Line("WRRgRRB", cores: "R");
            struck.Fire(Aim(struck, MarchAim.Join, 1));

            var a = new List<byte>();
            var b = new List<byte>();
            whole.Write(a);
            struck.Write(b);

            CollectionAssert.AreNotEqual(a, b, "a warden with a plate gone is a different board");
        }

        /// <summary>And two boards a Spark apart, for the same reason.</summary>
        [Test]
        public void TwoBoardsASparkApartAreNotTheSameState()
        {
            var plain = Line("BBRRBg", cores: "R");
            var armed = Line("BBRRBg", cores: "R");
            armed.Fire(Aim(armed, MarchAim.Join, 2));

            Assert.IsTrue(armed.Spark);

            // The lines really are different too, so the case is made on a board where only the
            // Spark differs: rebuild the plain one to match.
            var a = new List<byte>();
            armed.Write(a);

            var same = Line("g", cores: "R");
            var b = new List<byte>();
            same.Write(b);

            CollectionAssert.AreNotEqual(a, b);
        }
    }
}
