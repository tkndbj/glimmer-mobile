using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The lens: Lightfall's second chapter, and the one thing in a well that is not made of
    /// light.
    ///
    /// <para>
    /// <b>Glass fills up and then it fires.</b> It takes a channel at a time — free from any
    /// burst beside it, and a drop at a time by hand — and when it holds all three it throws its
    /// beams. That is the mote rule said twice, which is why the mechanic needed no new threshold
    /// taught, and why <see cref="OneDropCanOnlyEverAddOneChannelToGlass"/> is the most important
    /// case here: it is the whole of what makes a shot cost three drops rather than one.
    /// </para>
    /// <para>
    /// <b>What it throws is white, and how far round it throws says where its own light came
    /// from.</b> Glass holds all three by the time it goes off, so every mote a beam lands on is
    /// completed and pops whatever colour it was — bought rather than given, because the charge
    /// costs three drops of three colours. A lens filled the ordinary way fires <em>sideways</em>,
    /// which on a board with gravity is the only pair worth anything; a lens <em>struck by
    /// another lens's beam</em> fires along all four axes. Those two rules are
    /// <see cref="ABeamIsWhiteSoItPopsWhateverItLandsOn"/> and
    /// <see cref="ALensStruckByAnotherLensFiresEveryWay"/>, and between them they are the chain
    /// the second chapter is built on.
    /// </para>
    /// <para>
    /// <b>Feeding it by hand is a valve, not a shortcut.</b> A burst beside a lens is usually
    /// free, because it was clearing a blob anyway; a drop is one of the five a well is dealt
    /// above par. The search prefers the burst, so par is unmoved on eight of the ten shipped
    /// boards — what the drop buys is that a player who cleared every mote first is one drop
    /// from a win instead of stranded, which is how the mode shipped and was reported.
    /// </para>
    /// <para>
    /// <b>This file exists because <c>FallVectorTests</c> needs the Editor and this does not.</b>
    /// The vector file is the contract between the shipping rule and <c>Tools/verify/fall.py</c>,
    /// and it is loaded through <c>JsonUtility</c> — a native call — so the offline runner reports
    /// the whole fixture as "needs the Editor" and it is the one gate nobody runs on the way past.
    /// Budburst's wash rule drifted from its mirror with every offline gate green, because the
    /// mirror happened to be the correct copy; what noticed was an Android build twenty minutes
    /// in. So the shapes are here inline as well, and these are the ones that actually run on
    /// every compile.
    /// </para>
    /// <para>
    /// <b>Every expectation here was measured against the mirror first.</b> That is the whole
    /// point of having two copies: a number typed from reasoning proves the reasoning, and a
    /// number taken from the other implementation proves the implementations.
    /// </para>
    /// </summary>
    public sealed class FallGlassTests
    {
        static FallLayout Layout(string deal, params string[] rows)
        {
            Assert.IsTrue(FallDeal.TryParse(deal, out var procession, out string dealError),
                          dealError);

            int width = rows[0].Replace(" ", "").Length;
            Assert.IsTrue(FallLayout.TryReadRows(rows, width, rows.Length, out var fill,
                                                 out string fillError), fillError);

            return new FallLayout(width, rows.Length, fill, procession);
        }

        static FallBoard Board(params string[] rows) => new FallBoard(Layout("RGB", rows));

        /// <summary>What the board holds, written the way the case that built it wrote it.</summary>
        static string[] Read(FallBoard board)
        {
            var rows = new string[board.Height];
            for (int y = 0; y < board.Height; y++)
            {
                var row = new char[board.Width];
                for (int x = 0; x < board.Width; x++) row[x] = FallCell.Letter(board.At(x, y));
                rows[y] = new string(row);
            }
            return rows;
        }

        // ------------------------------------------------------------------ what a shot costs
        /// <summary>
        /// <b>The claim the whole chapter's difficulty rests on.</b> Every wave of one drop
        /// carries that drop's colour, so however many bursts a single drop sets off beside a
        /// lens, the glass gains exactly one channel. Filling an empty one therefore takes three
        /// separate drops of three separate colours, each engineered to burst beside it.
        ///
        /// <para>
        /// The board below bursts twice next to the glass on one blue drop — the first mote goes,
        /// washes the second, and that bursts in the wave after, right beside the lens. The lens
        /// ends holding blue and nothing else.
        /// </para>
        /// <para>
        /// If this ever stops being true the mechanic is free again, which is exactly what the
        /// first cut of it was and exactly what was reported: too easy, and an effect not worth
        /// watching. Those were one fault.
        /// </para>
        /// </summary>
        [Test]
        public void OneDropCanOnlyEverAddOneChannelToGlass()
        {
            var board = Board("......",
                              "......",
                              "......",
                              "......",
                              "......",
                              "YYOYY.");

            var steps = new List<FallStep>();
            board.Drop(Energy.B, 0, steps);

            Assert.AreEqual(2, steps.Count, "two waves, both of them blue");

            int lens = board.At(2, 5);
            Assert.IsTrue(FallCell.IsLens(lens), "the glass is still glass");
            Assert.AreEqual(Energy.B, FallCell.Charge(lens),
                            "two bursts beside it on one drop, and it holds one channel");
        }

        [Test]
        public void GlassIsNeverAuthoredFull()
        {
            Assert.IsFalse(FallLayout.TryReadRows(new[] { "....", "....", "....",
                                                          "....", "....", "w..." },
                                                  4, 6, out _, out string why),
                           "a lens authored full would fire before anybody touched the board");
            StringAssert.Contains("fire", why);
        }

        [Test]
        public void HowFullGlassStartsIsAuthorable()
        {
            // The chapter's difficulty dial. Measured: an empty lens leaves 7 boards in 90
            // solvable where two-thirds-full glass leaves 50.
            Assert.IsTrue(FallCell.TryParse('O', out int empty));
            Assert.IsTrue(FallCell.TryParse('g', out int third));
            Assert.IsTrue(FallCell.TryParse('y', out int twoThirds));

            Assert.AreEqual(Energy.None, FallCell.Charge(empty));
            Assert.AreEqual(Energy.G, FallCell.Charge(third));
            Assert.AreEqual(Energy.R | Energy.G, FallCell.Charge(twoThirds));

            Assert.IsTrue(FallCell.IsLens(twoThirds), "still glass, however full");
            Assert.IsFalse(FallCell.IsMote(twoThirds), "and never light");

            // Light is upper case and glass is lower, so a board reads as what is made of what.
            Assert.AreEqual('O', FallCell.Letter(empty));
            Assert.AreEqual('y', FallCell.Letter(twoThirds));
            Assert.AreEqual('Y', FallCell.Letter(Energy.R | Energy.G));
        }

        // ------------------------------------------------------------------ the shot
        /// <summary>
        /// <b>A lens filled the ordinary way fires sideways, and that is not a reduction of
        /// four.</b> A well has gravity, so a lens rests on something: its downward beam would
        /// travel exactly one cell, into the thing holding it up, and its upward one would fly
        /// into the air above the stack and leave. Only across is there anything to cross.
        ///
        /// <para>
        /// Two boards of the shipped chapter had been authored under the four-way rule and both
        /// leaned on the downward shot — the search answered par 3 and par 4 while it existed and
        /// par 6 with fifty-odd winning lines once it did not, which is a board that has stopped
        /// deciding anything. Both stand their glass on the floor now.
        /// </para>
        /// </summary>
        [Test]
        public void GlassFilledTheOrdinaryWayFiresSideways()
        {
            var board = Board("......",
                              "......",
                              "......",
                              "......",
                              ".C....",
                              "YyM..M");

            var steps = new List<FallStep>();
            board.Drop(Energy.B, 0, steps);

            Assert.AreEqual(1, steps[0].Charged.Count, "the burst fills the two-thirds lens");
            Assert.AreEqual(0, steps[0].Fired.Count, "which fires in the wave after, not this one");

            var shot = steps[1];
            Assert.AreEqual(1, shot.Fired.Count, "and now it goes off");
            Assert.AreEqual(2, shot.Beams.Count, "sideways, so two beams and not four");

            foreach (var beam in shot.Beams)
                Assert.AreEqual(0, beam.Dy, "neither of them is up or down");

            Assert.AreEqual(Energy.G, shot.WashedWith[0],
                            "the magenta across the gap took the one channel it lacked");

            Assert.AreEqual(Energy.G | Energy.B, board.At(1, 5),
                            "and the cyan that was standing directly over the glass is untouched, " +
                            "having merely fallen into the gap the lens left — an upward beam " +
                            "would have popped it");

            Assert.AreEqual(Energy.R | Energy.B, board.At(5, 5),
                            "and the magenta beyond it is untouched too: a beam still only " +
                            "reaches the first thing in its line");
        }

        /// <summary>
        /// <b>A beam is white, so what it lands on is completed rather than improved.</b> Glass
        /// holds all three channels by the time it goes off, and that is the one thing in this
        /// mode that pops a mote of any colour.
        ///
        /// <para>
        /// The magenta across the gap already holds the blue that was dropped, so a <em>wash</em>
        /// would have been absorbed by it and taken nothing — that clause is untouched and is
        /// still what makes colour a decision. A shot is what buys past it, and it is bought
        /// dearly: three separate drops of three separate colours, and it still only reaches the
        /// first thing in its line.
        /// </para>
        /// </summary>
        [Test]
        public void ABeamIsWhiteSoItPopsWhateverItLandsOn()
        {
            var board = Board("......",
                              "......",
                              "......",
                              "......",
                              "......",
                              "Yy..M.");

            var steps = new List<FallStep>();
            board.Drop(Energy.B, 0, steps);

            var shot = steps[1];
            Assert.AreEqual(1, shot.Fired.Count);
            Assert.AreEqual(1, shot.Washed.Count, "the magenta two cells across took it");
            Assert.AreEqual(Energy.G, shot.WashedWith[0],
                            "and took exactly the channel it lacked, out of the three the beam " +
                            "carried — a beam hands over white, not a colour");

            Assert.AreEqual(3, steps.Count, "so it reached white and popped on the next wave");
            Assert.IsTrue(board.IsEmpty, "one drop, and the well is gone");
        }

        /// <summary>
        /// <b>A lens another lens strikes fires along all four axes, and that is the chain.</b>
        /// One well-aimed shot down a row of glass opens every pane in it, and each of those then
        /// opens its own column — which is the one thing in this mode that reaches upward.
        ///
        /// <para>
        /// The board below takes one drop. The burst fills the two-thirds pane; it fires sideways
        /// into the empty pane, filling it outright because a beam is white; and that pane was
        /// <em>struck</em> rather than filled, so it fires up as well and takes the yellow
        /// standing over it.
        /// </para>
        /// </summary>
        [Test]
        public void ALensStruckByAnotherLensFiresEveryWay()
        {
            var board = Board("......",
                              "......",
                              "......",
                              "......",
                              "..Y...",
                              "YyOC..");

            var steps = new List<FallStep>();
            board.Drop(Energy.B, 0, steps);

            var filled = steps[1];
            Assert.AreEqual(2, filled.Beams.Count, "the first pane was charged, so it fires two");
            Assert.AreEqual(1, filled.Charged.Count, "and one of them lands on the empty pane");
            Assert.AreEqual(Energy.All, filled.ChargedWith[0],
                            "white, so it is filled outright rather than a channel at a time");

            var struck = steps[2];
            Assert.AreEqual(1, struck.Fired.Count, "the struck pane goes off on the next wave");
            Assert.AreEqual(4, struck.Beams.Count, "and every way, because it was struck");

            var ways = new HashSet<(int, int)>();
            foreach (var beam in struck.Beams) ways.Add((beam.Dx, beam.Dy));
            Assert.AreEqual(4, ways.Count, "four different directions, not four down one line");

            Assert.Contains(board.Index(2, 4), (System.Collections.ICollection)struck.Washed,
                            "including upward, which nothing else in this mode can do");

            Assert.IsTrue(board.IsEmpty, "so one drop takes the whole board");
        }

        /// <summary>
        /// <b>One drop can still only add one channel to a lens it merely charges.</b> A beam is
        /// the exception and it has to be: a shot is three drops already paid for, so the light
        /// coming out of it is not the drop's any more.
        /// </summary>
        [Test]
        public void ABurstStillOnlyEverHandsGlassTheDropsOwnColour()
        {
            var board = Board("......",
                              "......",
                              "......",
                              "......",
                              "......",
                              "YyM..M");

            var steps = new List<FallStep>();
            board.Drop(Energy.B, 0, steps);

            Assert.AreEqual(Energy.B, steps[0].ChargedWith[0],
                            "the burst beside the glass hands it blue and nothing else");
        }

        [Test]
        public void ABeamThatReachesNothingLeavesTheWellAndIsStillDrawn()
        {
            var board = Board("......",
                              "......",
                              "......",
                              "......",
                              "..Y...",
                              "YyOC..");

            var steps = new List<FallStep>();
            board.Drop(Energy.B, 0, steps);

            int missed = 0;
            foreach (var beam in steps[1].Beams) if (!beam.Landed) missed++;
            Assert.AreEqual(1, missed,
                            "the first pane's leftward shot goes out through the wall it was " +
                            "lit through, and it is still reported — three drops of charge " +
                            "spent on nothing is a decision that went wrong, and the player is " +
                            "entitled to watch it happen");

            missed = 0;
            foreach (var beam in steps[2].Beams) if (!beam.Landed) missed++;
            Assert.AreEqual(2, missed, "and the struck pane loses its downward and leftward ones");
        }

        // ------------------------------------------------------------------ glass is not light
        /// <summary>
        /// A drop is taken <em>in</em> by glass rather than stacking on it — the valve that stops
        /// a well ever becoming unwinnable. It costs a drop and gives nothing back, so it is a
        /// price rather than a shortcut.
        /// </summary>
        [Test]
        public void ADropIsTakenInByGlassRatherThanStackingOnIt()
        {
            var board = Board("....",
                              "....",
                              "....",
                              "....",
                              "....",
                              ".OY.");

            Assert.IsTrue(board.Charges(Energy.B, 1), "the glass will take blue");
            Assert.IsFalse(board.Enriches(Energy.B, 1), "which is not the same as enriching a mote");
            Assert.AreEqual(5, board.Landing(Energy.B, 1),
                            "so it lands on the glass, not above it — a row, not an index");

            board.Drop(Energy.B, 1);

            Assert.AreEqual("....", Read(board)[4], "nothing stacked, so no row of headroom went");
            Assert.AreEqual(".bY.", Read(board)[5], "and the glass now holds blue");
        }

        /// <summary>
        /// A colour the glass already holds is not taken, so the drop stacks above it exactly as
        /// it would on a mote that already holds the colour. Without this clause a lens would be
        /// a bottomless sink and a column topped by one could never be built on.
        /// </summary>
        /// <summary>
        /// <b>"Does the thing on top take this drop" is one question, and it has to be asked as
        /// one.</b> A mote lacking the colour is enriched and a lens lacking it is charged; both
        /// are absorbed and neither makes the well taller. <c>Enriches</c> answers only the first
        /// half — it is <c>IsMote(...) &amp;&amp; ...</c> — which is right for what it asks and
        /// was wrong everywhere it stood in for the whole question.
        ///
        /// <para>
        /// What that cost: <c>FallView</c> used <c>Enriches</c> to decide whether the falling
        /// widget was handed back or left standing in the cell, so every drop taken in by glass
        /// was drawn as one that had come to rest on top. The falling mote took the lens's place
        /// in the view's index and the lens's own widget fell out of it — still on screen, owned
        /// by nothing, so it never repainted, never fell and never left. Reported from play as a
        /// pane hanging in the air over an emptied column, showing the charge it held before the
        /// drop.
        /// </para>
        /// </summary>
        [Test]
        public void WhateverIsOnTopTakesTheDropWhetherItIsAMoteOrALens()
        {
            var board = Board("......",
                              "......",
                              "......",
                              "......",
                              "......",
                              "MyM...");

            Assert.IsTrue(board.Takes(Energy.B, 1), "the two-thirds lens lacks blue, so it takes it");
            Assert.IsFalse(board.Enriches(Energy.B, 1), "which Enriches cannot say, and must not");
            Assert.IsTrue(board.Charges(Energy.B, 1), "because the lens is charged rather than enriched");

            Assert.IsTrue(board.Takes(Energy.G, 0), "the magenta mote lacks green, so it takes it");
            Assert.IsTrue(board.Enriches(Energy.G, 0));

            Assert.IsFalse(board.Takes(Energy.B, 0),
                           "magenta already holds blue, so that drop comes to rest above it and " +
                           "the well grows a row — which is the one case the falling mote stays");

            Assert.IsFalse(board.Takes(Energy.R, 1),
                           "and the lens already holds red, so that drop stacks above it too — " +
                           "glass is a wall to a colour it holds exactly as a mote is");

            Assert.AreEqual(board.TopOf(1), board.Landing(Energy.B, 1),
                            "Takes is the exact clause Landing turns on, which is what makes it " +
                            "impossible for the two to disagree");
        }

        [Test]
        public void GlassThatAlreadyHoldsAColourLetsTheDropStackAboveIt()
        {
            var board = Board("....",
                              "....",
                              "....",
                              "....",
                              "....",
                              ".bY.");

            Assert.IsFalse(board.Charges(Energy.B, 1), "it holds blue already");
            Assert.AreEqual(4, board.Landing(Energy.B, 1), "so blue sits on top of it");

            board.Drop(Energy.B, 1);

            Assert.AreEqual(".B..", Read(board)[4], "the mote is above the glass");
            Assert.AreEqual(".bY.", Read(board)[5], "and the glass is unchanged");
        }

        [Test]
        public void GlassFallsIntoAGapLikeAnythingElse()
        {
            var board = Board("......",
                              "......",
                              "......",
                              "..O...",
                              "..C...",
                              ".YY..Y");

            board.Drop(Energy.B, 1);

            Assert.AreEqual("..O...", Read(board)[4], "the glass came down a row");
            Assert.AreEqual("..C..Y", Read(board)[5]);
        }

        [Test]
        public void GlassWantsWhatItLacksExactlyAsAMoteDoes()
        {
            var board = Board("......",
                              "......",
                              "......",
                              "......",
                              "......",
                              "y....O");

            Assert.AreEqual(Energy.B | Energy.All & ~(Energy.R | Energy.G), Energy.B);
            Assert.AreEqual(Energy.All, board.Wanted,
                            "one lens wants blue and one wants everything, so the well wants all " +
                            "three — read through the raw mask a lens answers nonsense");

            Assert.AreEqual(2, board.Lenses);
            Assert.AreEqual(2, board.Motes, "glass still has to be got rid of, so it still counts");
            Assert.IsFalse(board.Cookable, "and nothing here could ever burst to charge it");
        }

        // ------------------------------------------------------------------ the money seam
        /// <summary>
        /// <b>A well down to its last lens is one drop from finished, so it is offered a
        /// continue.</b>
        ///
        /// <para>
        /// This case asserted the opposite until play found the reason. Glass used to be fed
        /// only by light from a burst, so a player who cleared every mote first had destroyed
        /// the one thing that could ever fill it and was left tapping at a board that could not
        /// be finished and would not end — reported as <em>"I have destroyed all the motes, only
        /// this prism ball left, but I cannot finish the level"</em>, and measured afterwards at
        /// three drops away on the fifth board. A drop now feeds glass it lands on, so the
        /// state is a drop from a win rather than a dead end, and refusing the offer here would
        /// refuse it on the board where it is most obviously worth taking.
        /// </para>
        /// </summary>
        [Test]
        public void AWellDownToItsLastLensIsStillWorthContinuing()
        {
            var layout = Layout("RGB",
                                "......",
                                "......",
                                "......",
                                "......",
                                "......",
                                "....c.");

            var board = new FallBoard(layout);
            Assert.IsFalse(board.Cookable, "nothing left that could burst");
            Assert.IsTrue(board.Charges(Energy.R, 4), "but the glass will take a drop by hand");

            var verdict = FallVerdict.Read(board, new FallSupply(0), layout.Deal);

            Assert.AreEqual(FallEnding.Starved, verdict.Ending);
            Assert.AreNotEqual(RunContinueDeficit.None, verdict.Deficit,
                               "one red is one shot, so motes are exactly what this run needs");

            board.Drop(Energy.R, 4);
            Assert.IsTrue(board.IsEmpty, "and that drop finishes the well outright");
        }

        [Test]
        public void AWellWithAMoteLeftToCookIsStillWorthContinuing()
        {
            var layout = Layout("RGB",
                                "......",
                                "......",
                                "......",
                                "......",
                                "......",
                                "O.Y..y");

            var verdict = FallVerdict.Read(new FallBoard(layout), new FallSupply(0), layout.Deal);

            Assert.AreEqual(FallEnding.Starved, verdict.Ending);
            Assert.AreNotEqual(RunContinueDeficit.None, verdict.Deficit,
                               "one yellow is one burst, and one burst is what glass needs");
        }

        // ------------------------------------------------------------------ the reading
        /// <summary>
        /// Invariant 5d for the lens. Glass charged over three drops that then fires into open
        /// air on every axis has cost the player everything and done nothing, and the board would
        /// play the same without it. <c>Aim</c> is what the validator warns on.
        /// </summary>
        [Test]
        public void AimCountsOnlyTheShotsThatLandOnSomething()
        {
            var pointless = FallSolver.Survey(Layout("RGB", "......", "......", "......",
                                                     "......", "......", "O....."));
            Assert.AreEqual(0, pointless.Aim,
                            "alone against a wall, all four shots leave the well");

            var useful = FallSolver.Survey(Layout("RGB", "......", "......", "......",
                                                  "......", "......", "YO...Y"));
            Assert.AreEqual(2, useful.Aim, "one either side");
            Assert.AreEqual(4, useful.Reach, "and the far one is four cells away");

            var none = FallSolver.Survey(Layout("RGB", "......", "......", "......",
                                                "......", "......", "Y....Y"));
            Assert.AreEqual(0, none.Aim, "no glass, nothing to measure");
        }

        // ------------------------------------------------------------------ the pacing
        /// <summary>
        /// The board is latched while a shot plays, so it has to be bounded — and bounded over
        /// the whole cascade rather than per lens, or a well where four go off would freeze for
        /// four times as long.
        /// </summary>
        [Test]
        public void TheShotAllowanceIsBoundedHoweverManyLensesFire()
        {
            for (int firing = 1; firing <= 12; firing++)
            {
                float each = FallTempo.Shot(firing);
                float all = each * firing;

                Assert.LessOrEqual(all, FallTempo.ShotCeiling + 1e-4f,
                    $"{firing} firing wave(s) spend {all:0.###}s against a ceiling of " +
                    $"{FallTempo.ShotCeiling:0.###}s");

                Assert.AreEqual(each, FallTempo.Gather(firing) + FallTempo.Throw(firing), 1e-4f,
                    "the gather and the throw are the whole of a shot, so they sum to it");
            }

            Assert.AreEqual(0f, FallTempo.Shot(0), "and a cascade with no glass in it spends none");
        }

        [Test]
        public void OneShotIsWorthStoppingTheBoardForAndABurstIsNot()
        {
            // The gather alone should outlast an ordinary wave's whole burst beat on a short
            // chain: that difference is the entire reason a shot reads as a different kind of
            // event rather than as a louder burst.
            Assert.Greater(FallTempo.Shot(1), FallTempo.Burst(1),
                           "a shot that fitted inside an ordinary burst would be a burst");
        }
    }
}
