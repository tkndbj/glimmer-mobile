using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The whorl: Lightfall's third chapter, and the one thing here that <em>moves</em> a mote.
    ///
    /// <para>
    /// <b>Light opens it, and on the next wave it draws the motes standing either side of it
    /// together and mixes them into one.</b> Every case here is one of the things that sentence
    /// implies: it holds no channels at all
    /// (<see cref="AWhorlHoldsNoChannelsAndWantsNothing"/>), a drop opens it rather than filling
    /// it (<see cref="ADropOpensAWhorlWhateverColourItIs"/>), the pair is mixed by the mode's own
    /// arithmetic (<see cref="AWhorlMixesThePairBesideItIntoOne"/>), it draws light and nothing
    /// else (<see cref="AWhorlDrawsLightAndNothingElse"/>), it closes rather than waiting when
    /// there is nothing to take (<see cref="AWhorlWithNothingBesideItClosesAndIsGone"/>), and —
    /// the two that keep the wave free of a reading order — a mote already leaving is never drawn
    /// in (<see cref="AMoteThatIsBurstingIsNeverAlsoDrawnIn"/>) and a mote two whorls both reach
    /// is let go by both (<see cref="AMoteTwoWhorlsBothReachIsLetGoByBoth"/>).
    /// </para>
    /// <para>
    /// <b>This file replaced <c>FallWickTests</c>, which had replaced <c>FallMirrorTests</c>, and
    /// the reason is the most useful thing in it.</b> The third chapter first shipped a
    /// <em>mirror</em> that turned a lens's beam ninety degrees, and then a <em>wick</em> that
    /// washed one authored colour into the four cells beside it. Every fixture passed both times.
    /// Both were solvable, correctly par'd, <c>ways</c> was tight and <c>greedy</c> lost — and
    /// both came back from one session of play as the same complaint: they were the lens again.
    /// The mirror had no event of its own; the wick had one, and no <em>decision</em> in it — its
    /// colour was the author's and its trigger was free, so the player never chose anything about
    /// it. <b>A decoration passes every reading this repository takes</b>, which is why
    /// <see cref="FallBoard.Kindled"/> exists and why
    /// <c>FallWhorlwaterTests.EveryWellsWhorlsActuallyDecideSomething</c> is the case that
    /// matters most in the chapter fixture.
    /// </para>
    /// <para>
    /// <b>Every expectation here was measured against <c>Tools/verify/fall.py</c> first.</b> That
    /// is the point of having two copies: a number typed from reasoning proves the reasoning, and
    /// a number taken from the other implementation proves the implementations.
    /// </para>
    /// </summary>
    public sealed class FallWhorlTests
    {
        static FallLayout Layout(string deal, params string[] rows)
        {
            Assert.IsTrue(FallDeal.TryParse(deal, out var procession, out string dealError),
                          dealError);

            int width = rows[0].Length;
            Assert.IsTrue(FallLayout.TryReadRows(rows, width, rows.Length, out var fill,
                                                 out string fillError), fillError);

            return new FallLayout(width, rows.Length, fill, procession);
        }

        static FallBoard Board(string deal, params string[] rows)
            => new FallBoard(Layout(deal, rows));

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

        static readonly string[] Bare = { ".....", ".....", ".....", ".....", "....." };

        static string[] Well(string floor)
        {
            var rows = new List<string>(Bare) { floor };
            return rows.ToArray();
        }

        // ------------------------------------------------------------------ what a whorl is
        /// <summary>
        /// A whorl is occupied, is not light, is not glass, holds nothing and wants nothing.
        ///
        /// <para>
        /// The two predicates that had to be <em>narrowed</em> for this mechanic are the ones
        /// with teeth, and each would have been a silent failure: <see cref="FallCell.IsMote"/>
        /// read "occupied and not glass" and <see cref="FallCell.Wants"/> read "everything this
        /// cell lacks", so a whorl read as a mote wanting all three — a wash beside one would
        /// have poured colour into a cell that holds none, a drop onto it would have been
        /// swallowed, and a whorl would have drawn in another whorl.
        /// </para>
        /// </summary>
        [Test]
        public void AWhorlHoldsNoChannelsAndWantsNothing()
        {
            int cell = FallCell.Whorl;

            Assert.IsTrue(FallCell.Occupied(cell), "a whorl stands in the way of the fall");
            Assert.IsTrue(FallCell.IsWhorl(cell));
            Assert.IsFalse(FallCell.IsMote(cell), "a whorl is not light and can never burst");
            Assert.IsFalse(FallCell.IsLens(cell), "nor is it glass");
            Assert.IsFalse(FallCell.IsLit(cell), "and it is authored closed");

            Assert.AreEqual(Energy.None, FallCell.Charge(cell),
                            "it holds nothing, so there is nothing to read out of it");
            Assert.AreEqual(Energy.None, FallCell.Wants(cell),
                            "and it wants nothing: it opens, which is not the same as filling up");

            Assert.AreEqual(FallCell.WhorlLetter, FallCell.Letter(cell));
            Assert.AreEqual(FallCell.WhorlLetter, FallCell.Letter(cell | FallCell.Lit),
                            "an open whorl writes as an ordinary one — the flag is state rather " +
                            "than content, and it cannot be authored");
        }

        /// <summary>
        /// The three digits a wick was authored with are refused rather than read as anything.
        ///
        /// Invariant 5f's rule for the duskcap, applied to the mechanic this one replaced: a
        /// chapter file carrying one is content written for a build that no longer exists, and
        /// reading it as a mote would put a cell on the board no rule here knows what to do with.
        /// </summary>
        [Test]
        public void TheWicksLettersAreRetiredAndRefusedByName()
        {
            foreach (char c in FallCell.RetiredWickLetters)
            {
                Assert.IsFalse(FallCell.TryParse(c, out _),
                               $"'{c}' was a wick and must never parse as anything again");

                var rows = new[] { ".....", ".....", ".....", ".....", ".....", "." + c + "..." };
                Assert.IsFalse(FallLayout.TryReadRows(rows, 5, rows.Length, out _,
                                                      out string why),
                               $"a board carrying '{c}' must be refused");
                StringAssert.Contains("wick", why,
                    "and refused by name, so whoever wrote it is told what happened to it");
            }
        }

        // ------------------------------------------------------------------ the valve
        /// <summary>
        /// A drop opens a whorl whatever colour it is, and the stack does not grow.
        ///
        /// <para>
        /// <b>The valve, and it is a rule rather than a convenience.</b> A whorl is otherwise
        /// only reached by a chain, so without this a player who cleared every mote around one
        /// would be left tapping at a board that could not be finished and would not end — which
        /// is exactly the state the lens shipped with and had to have a valve added for after a
        /// player reported it (invariant 26f). Here it is the rule from the start, which is why
        /// <c>FallVerdict</c> needs no clause about whorls at all.
        /// </para>
        /// </summary>
        [Test]
        public void ADropOpensAWhorlWhateverColourItIs()
        {
            foreach (int colour in new[] { Energy.R, Energy.G, Energy.B })
            {
                var board = Board("RGB", Well("..@.."));

                Assert.IsTrue(board.Takes(colour, 2),
                              "a whorl takes the drop rather than letting the stack grow");
                Assert.AreEqual(5, board.Landing(colour, 2),
                                "it comes to rest on the whorl's own row rather than above it");
                Assert.IsFalse(board.Enriches(colour, 2), "it is not enriched: it holds nothing");
                Assert.IsFalse(board.Charges(colour, 2), "nor charged: it is not glass");

                board.Drop(colour, 2);

                Assert.AreEqual(0, board.Whorls,
                                "one drop is enough to be rid of a whorl with nothing beside it");
                Assert.IsTrue(board.IsEmpty, "so a well of nothing but whorls is always winnable");
            }
        }

        // ------------------------------------------------------------------ the mechanic
        /// <summary>
        /// A whorl draws the two motes beside it together and leaves one holding both.
        ///
        /// <para>
        /// <b>This is the whole mechanic, and it is the mode's own arithmetic on a pair of
        /// operands it never had.</b> Every other rule here adds a <em>colour</em> to a cell — a
        /// drop adds one, a wash adds one, a beam adds three. Nothing else combines two
        /// <em>motes</em>, so a yellow and a blue that were each a drop away from white are none.
        /// </para>
        /// <para>
        /// It also pins the accounting the termination proof rests on: two go in and one comes
        /// out, with the whorl gone, so a wave can never leave the well as full as it found it.
        /// </para>
        /// </summary>
        [Test]
        public void AWhorlMixesThePairBesideItIntoOne()
        {
            var board = Board("RGB", Well(".Y@B."));
            Assert.AreEqual(3, board.Motes, "a yellow, a whorl and a blue");

            var steps = new List<FallStep>();
            var result = board.Drop(Energy.R, 2, steps);

            Assert.AreEqual(2, result.Waves, "it turns on one wave and the white bursts on the next");

            Assert.AreEqual(1, steps[0].Fuses.Count, "the whorl turned");
            var fuse = steps[0].Fuses[0];

            Assert.AreEqual(board.Index(2, 5), fuse.At, "where it stood");
            Assert.AreEqual(board.Index(1, 5), fuse.Left, "and it took the yellow");
            Assert.AreEqual(board.Index(3, 5), fuse.Right, "and the blue");
            Assert.AreEqual(Energy.All, fuse.Into, "which between them are white");
            Assert.AreEqual(2, fuse.Drawn);
            Assert.IsTrue(fuse.Kindled, "so the merge itself completed a mote");

            Assert.AreEqual(1, steps[1].Burst.Count, "and the white bursts where the whorl stood");
            Assert.AreEqual(fuse.At, steps[1].Burst[0]);

            Assert.IsTrue(board.IsEmpty, "one drop, and the well is finished");
            Assert.AreEqual(1, board.Fused);
            Assert.AreEqual(1, board.Kindled);
        }

        /// <summary>
        /// A merge that does not reach white is still a merge, and it is not the same thing.
        ///
        /// <para>
        /// The distinction <see cref="FallBoard.Fused"/> and <see cref="FallBoard.Kindled"/> exist
        /// to keep apart, and the reason a board is authored against the second. Two motes of one
        /// colour drawn together make a tidier board and decide nothing; a pair whose union is
        /// white is a burst the player <em>arranged</em> and could not have bought with any single
        /// drop.
        /// </para>
        /// </summary>
        [Test]
        public void AMergeThatDoesNotReachWhiteIsStillAMerge()
        {
            var board = Board("RGB", Well(".Y@Y."));

            var steps = new List<FallStep>();
            board.Drop(Energy.R, 2, steps);

            var fuse = steps[0].Fuses[0];
            Assert.AreEqual(2, fuse.Drawn, "both were taken");
            Assert.AreEqual(Energy.R | Energy.G, fuse.Into, "and yellow and yellow are yellow");
            Assert.IsFalse(fuse.Kindled, "which is not a burst, and must not be counted as one");

            Assert.AreEqual(1, board.Fused);
            Assert.AreEqual(0, board.Kindled,
                            "the reading a board is authored against is the strict one");

            Assert.AreEqual(1, board.Motes, "three cells became one");
        }

        /// <summary>
        /// Glass beside a whorl stays where it stands, and so does another whorl.
        ///
        /// <para>
        /// <b>A whorl draws light and nothing else.</b> Pulling glass in would mean deciding what
        /// a lens and a mote mix into, and two whorls tugging at each other is a rule nobody can
        /// read off a board. It is <see cref="FallCell.IsMote"/> doing the work, which is exactly
        /// why that predicate had to be narrowed when this mechanic arrived.
        /// </para>
        /// </summary>
        [Test]
        public void AWhorlDrawsLightAndNothingElse()
        {
            var board = Board("RGB", Well("c@Y.."));

            var steps = new List<FallStep>();
            board.Drop(Energy.R, 1, steps);

            var fuse = steps[0].Fuses[0];
            Assert.AreEqual(-1, fuse.Left, "the glass on its left was not taken");
            Assert.AreEqual(board.Index(2, 5), fuse.Right, "the mote on its right was");
            Assert.AreEqual(Energy.R | Energy.G, fuse.Into);
            Assert.AreEqual(1, fuse.Drawn);

            Assert.AreEqual(1, board.Lenses, "the pane is still standing");
            Assert.AreEqual(Energy.G | Energy.B, FallCell.Charge(board.At(0, 5)),
                            "and holding exactly what it was authored holding");
            Assert.AreEqual(0, board.Fused, "one is not a pair");
        }

        /// <summary>
        /// A whorl opened with nothing beside it closes and is gone.
        ///
        /// <para>
        /// <b>The other half of the valve.</b> A whorl that <em>waited</em> for a pair could never
        /// be got rid of on a board that had none, and a well holding one could then never be
        /// emptied — invariant 20g's state, arrived at by a rule nobody could see. It also makes
        /// being early a real mistake with a real cost, which is what makes the timing a decision.
        /// </para>
        /// </summary>
        [Test]
        public void AWhorlWithNothingBesideItClosesAndIsGone()
        {
            var board = Board("RGB", Well("..@.."));

            var steps = new List<FallStep>();
            board.Drop(Energy.R, 2, steps);

            var fuse = steps[0].Fuses[0];
            Assert.AreEqual(0, fuse.Drawn, "there was nothing to take");
            Assert.AreEqual(Energy.None, fuse.Into, "so it leaves nothing behind");
            Assert.IsFalse(fuse.Kindled);

            Assert.IsTrue(board.IsEmpty, "and it is gone rather than standing there for ever");
        }

        // ------------------------------------------------------------------ no reading order
        /// <summary>
        /// A mote two whorls both reach is let go by both.
        ///
        /// <para>
        /// <b>The only symmetric answer available, and that is the argument for it.</b> Giving it
        /// to one of them would be a reading order, in the one method this whole class is arranged
        /// to keep free of one — and a board that settles differently depending on which cell was
        /// scanned first is a divergence between two runtimes that nothing here could see.
        /// </para>
        /// <para>
        /// The chain is worth reading: one drop bursts the middle of a row of cyans, the wash
        /// makes both its neighbours white, those burst together on the next wave and open both
        /// whorls at once, and on the wave after that both turn and both find the yellow between
        /// them contested.
        /// </para>
        /// </summary>
        [Test]
        public void AMoteTwoWhorlsBothReachIsLetGoByBoth()
        {
            var board = Board("RGB", "......", "......", "......", "......", "CCC...", "@Y@...");

            var steps = new List<FallStep>();
            var result = board.Drop(Energy.R, 1, steps);

            Assert.AreEqual(3, result.Waves);
            Assert.AreEqual(2, steps[1].Caught.Count, "both whorls were opened by the same wave");

            var turn = steps[2].Fuses;
            Assert.AreEqual(2, turn.Count, "and both turn together");

            foreach (var fuse in turn)
            {
                Assert.AreEqual(0, fuse.Drawn,
                    "neither may take the mote between them, or which one got it would depend " +
                    "on the order the board happened to be scanned in");
                Assert.AreEqual(Energy.None, fuse.Into);
            }

            Assert.AreEqual(Energy.R | Energy.G, board.At(1, 5),
                            "so the yellow is still standing exactly where it was");
            Assert.AreEqual(1, board.Motes);
        }

        /// <summary>
        /// A mote that is bursting on this wave is never also drawn in.
        ///
        /// <para>
        /// The light got to it first, which is the honest reading and the one that keeps a mote
        /// from both bursting and being taken. It is the same <c>_going</c> set the wash and the
        /// beams consult, asked once rather than copied.
        /// </para>
        /// <para>
        /// <b>The board was found rather than reasoned about</b>
        /// (<c>scratchpad/bursting.py</c>), because the interleaving is a corner: the whorl has to
        /// be opened on the wave <em>before</em> the one on which its neighbour reaches white.
        /// Finding one is also what proves the clause is reachable in play at all.
        /// </para>
        /// </summary>
        [Test]
        public void AMoteThatIsBurstingIsNeverAlsoDrawnIn()
        {
            var board = Board("BGR", ".....", ".....", ".....", "..C..", "G.YR.", "GYY@C");

            var steps = new List<FallStep>();
            board.Drop(Energy.B, 1, steps);

            var fuse = steps[2].Fuses[0];
            int whorl = fuse.At;

            bool leftIsBursting = false;
            for (int i = 0; i < steps[2].Burst.Count; i++)
                if (steps[2].Burst[i] == whorl - 1) leftIsBursting = true;

            Assert.IsTrue(leftIsBursting,
                          "the cell on its left is bursting on this very wave");
            Assert.AreEqual(-1, fuse.Left,
                            "so it is not also drawn in — a cell cannot both go off and be taken");
            Assert.AreEqual(whorl + 1, fuse.Right, "while the cell on its right is");
        }

        // ------------------------------------------------------------------ the board
        /// <summary>
        /// Gravity never moves a whorl sideways, which is what makes the wall reading exact.
        ///
        /// <para>
        /// The lens's <c>aim</c> is geometry of the authored position and warns rather than
        /// refuses, because a well collapses under a chain and a lens fires from wherever it has
        /// fallen to. A whorl's <em>columns</em> are not like that: it draws from the two beside
        /// it, and the column it stands in is fixed for its whole life — so one authored against
        /// a wall can never merge a pair whatever the well does. That is the one thing about this
        /// mechanic a validator can prove rather than measure.
        /// </para>
        /// </summary>
        [Test]
        public void AWhorlKeepsItsColumnHoweverFarTheWellCollapses()
        {
            var board = Board("RGB", "......", "......", "..Y...", "..Y...", "..@...", "..C...");
            int column = board.X(board.Index(2, 4));

            board.Drop(Energy.B, 2);

            for (int i = 0; i < board.Width * board.Height; i++)
            {
                if (!FallCell.IsWhorl(board.At(i))) continue;
                Assert.AreEqual(column, board.X(i),
                    "a whorl only ever falls, so the two columns it can draw from are the two " +
                    "it was authored between: " + string.Join("|", Read(board)));
            }
        }

        /// <summary>
        /// A well holding nothing but glass and whorls can never be emptied, and the validator
        /// says so in words about the board rather than about a search.
        ///
        /// A whorl is always removable and emits no light whatever, so it gives back only what it
        /// drew in — a well with no mote in it has nothing to draw and nothing to cook, and the
        /// glass in it is there for ever.
        /// </summary>
        [Test]
        public void AWellOfNothingButGlassAndWhorlsIsRefused()
        {
            var layout = Layout("RGB", Well(".O@.."));
            var level = new LevelDefinition(
                LevelId.Parse("t_glassy"), ChapterId.Parse("t_chapter"),
                new FallRules(layout), LevelTuning.Default(1),
                new LevelPresentation(new Vector2(.5f, .5f), null, null, null));

            var report = LevelValidator.Validate(level);

            bool refused = false;
            var said = new List<string>();
            foreach (var issue in report.Issues)
            {
                said.Add(issue.Message);
                if (issue.Severity == LevelIssueSeverity.Error &&
                    issue.Message.Contains("glass or a whorl")) refused = true;
            }

            Assert.IsTrue(refused,
                "a well with no mote to cook can never charge its glass, and the search would " +
                "only ever say so after spending the whole node budget failing to prove it: " +
                string.Join("; ", said));
        }
    }
}
