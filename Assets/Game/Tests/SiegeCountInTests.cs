using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The three, two, one that opens a siege - and the one thing about it that is a rule rather
    /// than a flourish: it is paced by the run's own clock.
    ///
    /// <para>
    /// <b>It was a coroutine on a wall clock, and that is the fault this fixture exists for.</b>
    /// The count drew four beats over <see cref="SiegeTuning.FirstWaveAfter"/> seconds of
    /// <c>WaitForSecondsRealtime</c>, and agreed with the first wave only because both were the
    /// same constant - two clocks held in step by arithmetic. Everything that holds a run stops
    /// the board's clock and none of them stops a wall clock: a first-timer's tip
    /// (<c>RunHold.Teaching</c>), the pause menu, a panel over the board (<c>RunHold.Covered</c>)
    /// and the transition that is still hiding the screen (<c>RunHold.Opening</c>). So a player
    /// meeting this mode for the first time read their tip boxes while the count ran out behind
    /// them, closed the last one, and was handed a hill with no count-in at all and a wave already
    /// due. Every gate here was green, because nothing in this project can see two clocks
    /// disagreeing.
    /// </para>
    /// <para>
    /// <b>What replaced it is a derivation rather than a second clock</b> -
    /// <see cref="SiegeBoard.BeforeFirstWave"/> is the quiet, and <c>SiegeView.BeatsBy</c> is how
    /// many beats that quiet owes - so the two cannot come apart on any frame for any reason
    /// (invariant 33g). What a test can hold is the quiet and the arithmetic over it; the drawing
    /// is <c>SiegeView</c>'s and decides nothing.
    /// </para>
    /// </summary>
    public sealed class SiegeCountInTests
    {
        const float Quiet = SiegeTuning.FirstWaveAfter;
        const float Frame = 1f / 60f;

        /// <summary>
        /// The shipped opening rung's own field, with waves that send something.
        ///
        /// A hand-drawn field is the trap <c>SiegeHintTests</c> already records: a board with no
        /// run and no swap on it re-deals itself the moment it advances, and this fixture advances
        /// boards.
        /// </summary>
        static SiegeBoard Board()
            => SiegeBoard.Build(Layout("ryybgyyg",
                                       "bybgrgyy",
                                       "rbryyggr",
                                       "grgrgbbr",
                                       "yybgrrbg"));

        static SiegeLayout Layout(params string[] rows)
        {
            Assert.IsTrue(ProtoGrid.TryRead(rows, rows[0].Length, rows.Length, SiegeLayout.Cells,
                                            out var grid, out string error), error);

            return new SiegeLayout(grid, "rgby", "rgby", new[] { "rgby", "rgbyrgby" }, null, 0);
        }

        /// <summary>Steps a board in the frames the view really steps it in.</summary>
        static void Frames(SiegeBoard board, float seconds)
        {
            for (int i = 0; i < (int)(seconds / Frame); i++) board.Advance(Frame);
        }

        // ------------------------------------------------------------------ the quiet
        /// <summary>
        /// <b>The whole fix in one assertion.</b> A board nobody advances is a board whose quiet
        /// has not moved - so a run held for a tip, a pause or a panel spends none of its
        /// count-in, whatever the wall clock has been doing meanwhile.
        /// </summary>
        [Test]
        public void AHeldRunSpendsNoneOfItsCountIn()
        {
            var board = Board();

            Assert.AreEqual(Quiet, board.BeforeFirstWave, .0001f,
                            "the quiet did not start at the full opening");

            // A held run is one nothing hands seconds to, for as many frames as it is held.
            for (int i = 0; i < 600; i++)
            {
                Assert.AreEqual(Quiet, board.BeforeFirstWave, .0001f,
                                "the quiet moved on a frame the run was not given");
                Assert.AreEqual(0, SiegeView.BeatsBy(board.BeforeFirstWave),
                                "a beat was owed before the run had advanced at all");
            }
        }

        /// <summary>
        /// The quiet runs out exactly when the first wave steps out, and stays out - so the
        /// count-in can never draw itself again, whatever the hill does afterwards.
        /// </summary>
        [Test]
        public void TheQuietIsOverExactlyWhenTheFirstWaveMusters()
        {
            var board = Board();

            Frames(board, Quiet - .2f);
            Assert.AreEqual(0, board.Wave, "the first wave mustered inside the quiet");
            Assert.Greater(board.BeforeFirstWave, 0f, "the quiet ran out before the wave did");

            Frames(board, .5f);
            Assert.GreaterOrEqual(board.Wave, 1, "the first wave never mustered");
            Assert.AreEqual(0f, board.BeforeFirstWave, "the quiet outlived the wave it counts");

            Frames(board, 60f);
            Assert.AreEqual(0f, board.BeforeFirstWave,
                            "the quiet came back, so the count-in would draw itself again");
        }

        // ------------------------------------------------------------------ the beats
        /// <summary>
        /// Swept frame by frame over a real board: every beat is owed exactly once, in order, the
        /// count never goes backwards, and GO! is owed by the time the hill starts walking.
        ///
        /// <para>
        /// Driven by advancing the board rather than by handing <c>BeatsBy</c> made-up numbers,
        /// because the property worth holding is about the pair - a formula restated here would
        /// agree with a wrong one as happily as with a right one (<c>SiegeView.GroundSize</c>'s
        /// own rule).
        /// </para>
        /// </summary>
        [Test]
        public void EveryBeatIsOwedOnceAndInOrderBeforeTheFirstWaveArrives()
        {
            var board = Board();

            int last = SiegeView.BeatsBy(board.BeforeFirstWave);
            Assert.AreEqual(0, last, "a beat was owed on a board that had not moved");

            var drawn = new bool[SiegeView.Beats];
            int went = -1, mustered = -1;

            for (int frame = 0; frame < (int)((Quiet + 1f) / Frame); frame++)
            {
                board.Advance(Frame);

                int owed = SiegeView.BeatsBy(board.BeforeFirstWave);

                // The first beat is owed the instant the run is allowed to advance, because that
                // is the moment the player is first looking at the hill. A count that starts a
                // step late is a count that starts on "2".
                if (frame == 0)
                    Assert.AreEqual(1, owed, "the first beat was not owed on the first live frame");

                Assert.GreaterOrEqual(owed, last, "the count went backwards");
                Assert.LessOrEqual(owed, SiegeView.Beats, "more beats were owed than exist");

                // What the view draws: the newest beat, once, and never the same one twice.
                if (owed > last)
                {
                    Assert.IsFalse(drawn[owed - 1], "a beat was owed twice");
                    drawn[owed - 1] = true;
                }

                if (went < 0 && owed >= SiegeView.Beats) went = frame;
                if (mustered < 0 && board.Wave >= 1) mustered = frame;

                last = owed;
            }

            Assert.GreaterOrEqual(went, 0, "GO! was never owed");
            Assert.GreaterOrEqual(mustered, 0, "the first wave never mustered");

            // GO! reads over an empty hill and the raiders walk on under it — which is what makes
            // it an announcement rather than a caption on something already happening.
            Assert.Less(went, mustered,
                        "GO! was owed no earlier than the first raider, so the count no longer "
                        + "announces the wave it is counting to");

            for (int i = 0; i < drawn.Length; i++)
                Assert.IsTrue(drawn[i], $"beat {i} was never owed");
        }

        /// <summary>
        /// A reading outside the quiet is clamped rather than running off either end of the count
        /// - the board answers nought for the rest of the run, and this is what makes that safe to
        /// read every frame.
        /// </summary>
        [Test]
        public void AQuietOutsideItsOwnRangeIsClamped()
        {
            Assert.AreEqual(SiegeView.Beats, SiegeView.BeatsBy(0f),
                            "the count was unfinished with no quiet left");
            Assert.AreEqual(SiegeView.Beats, SiegeView.BeatsBy(-5f));
            Assert.AreEqual(0, SiegeView.BeatsBy(Quiet));
            Assert.AreEqual(0, SiegeView.BeatsBy(Quiet + 10f));
        }
    }
}
