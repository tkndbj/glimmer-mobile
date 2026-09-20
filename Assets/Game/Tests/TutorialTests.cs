using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The opening tutorial's board, and the script played end to end against the real rules.
    ///
    /// <para>
    /// <b>This board goes through none of the content gates, which is the whole reason this
    /// fixture exists.</b> <c>content.py</c> reads the manifest, and the tutorial is not in it —
    /// so nothing else in this project would notice a field that had stopped being settled, a
    /// taught pair that had stopped lining anything up, or a hill that could no longer be
    /// cleared. Each of those is invisible until a first-time player meets it, and a first-time
    /// player is the one person who cannot tell a broken game from a hard one.
    /// </para>
    /// <para>
    /// <b>It plays the script rather than asserting about it.</b> Every step below is the call
    /// <c>TutorialScreen</c> makes, in the order it makes it, against <c>SiegeBoard</c> stepped
    /// at sixty frames a second — so what is proved is that the sequence terminates, not that
    /// each piece of it looks right on its own.
    /// </para>
    /// </summary>
    public sealed class TutorialTests
    {
        const float Frame = 1f / 60f;

        /// <summary>Longest any one beat of the script may take before the fixture gives up.</summary>
        const int Patience = 60 * 60;

        static SiegeBoard Fresh() => (SiegeBoard)SiegeTutorial.Rules().Fresh();

        // ------------------------------------------------------------------ the board
        [Test]
        public void TheTutorialBoardIsFitToShip()
        {
            Assert.IsNull(SiegeTutorial.Fault(),
                          "the tutorial's own field is read by no content gate, so this is the "
                          + "only thing that would ever say it had stopped being a board");
        }

        [Test]
        public void TheFieldIsAuthoredSettled()
        {
            var board = Fresh();

            Assert.AreEqual(0, SiegeLayout.Runs(Cells(board), board.Width, board.Height, null).Count,
                            "a field dealt with a run already on it cascades in front of a "
                            + "first-timer before they have touched anything");
        }

        [Test]
        public void TheTaughtPairIsTheOneTheHandIsDrawnBetween()
        {
            var board = Fresh();

            Assert.IsTrue(board.Adjacent(SiegeTutorial.TaughtA, SiegeTutorial.TaughtB),
                          "a coaching hand is drawn between two cells and a drag is one step");

            Assert.IsTrue(board.Lines(SiegeTutorial.TaughtA, SiegeTutorial.TaughtB),
                          "the hand would point at a move the drag refuses");

            var swap = SiegeTutorial.Taught(board);

            Assert.IsTrue(swap.Found);
            Assert.AreEqual(SiegeTutorial.TaughtA, swap.A);
            Assert.AreEqual(SiegeTutorial.TaughtB, swap.B);
        }

        /// <summary>
        /// The tutorial deals nothing it does not teach.
        ///
        /// A cog, a charm, a bomber, a bulwark or a boss is a second thing to learn, and a lesson
        /// is spent once in a player's life — so anything appearing here would be met with no
        /// panel at all and never explained afterwards.
        /// </summary>
        [Test]
        public void TheTutorialDealsNothingItDoesNotTeach()
        {
            var layout = SiegeTutorial.Layout();

            Assert.AreEqual(0, layout.Cogs);
            Assert.AreEqual(0, layout.Charms.Length);
            Assert.IsFalse(layout.HasBoss);
            Assert.IsFalse(layout.IsEndless);
            Assert.AreEqual(1, layout.Waves.Length, "one wave, and it is the whole raid");

            for (int w = 0; w < layout.Coming.Length; w++)
                for (int i = 0; i < layout.Coming[w].Length; i++)
                    Assert.AreEqual(SiegeKind.Creeper, layout.KindAt(w, i),
                                    "every raider in the tutorial is the plain one");
        }

        /// <summary>
        /// Every colour the field deals has a turret in front of it and a raider behind it.
        ///
        /// The first is already a rule (<c>SiegeLayout.Check</c>); the second is this board's own,
        /// and it is what makes <c>SiegeTutorial.Sweep</c> certain to finish rather than likely
        /// to.
        /// </summary>
        [Test]
        public void EveryRaiderHasATurretThatAnswersIt()
        {
            var layout = SiegeTutorial.Layout();

            for (int w = 0; w < layout.Coming.Length; w++)
                for (int i = 0; i < layout.Coming[w].Length; i++)
                {
                    char wears = SiegeLayout.Letters[layout.ColourAt(w, i)];

                    Assert.Greater(System.Array.IndexOf(layout.Wards, wears), -1,
                                   $"a '{wears}' raider walks down a hill with no '{wears}' turret "
                                   + "on it, so the last beat could never clear it");
                }
        }

        // ------------------------------------------------------------------ the script
        /// <summary>
        /// The whole of what the screen does, in order, against the real rules — and it ends.
        /// </summary>
        [Test]
        public void TheScriptPlayedThroughEndsInAVictoryWithTheLineIntact()
        {
            var board = Fresh();
            board.Sheltered = true;

            // The first panel is up and the hill is latched, so nothing has moved yet.
            var swap = SiegeTutorial.Taught(board);
            Assert.IsTrue(swap.Found);

            Assert.IsNotNull(board.Swap(swap.A, swap.B), "the taught drag has to land");

            // The tube climbs a match at a time and arms within the target, never past it.
            int match = 0;

            while (SiegeTutorial.Charged(board) < 0)
            {
                Assert.Less(match, SiegeTutorial.MatchesToArm,
                            "a feed past the target has to brim the tube whatever else is true, "
                            + "or the screen's loop has nothing to end on");

                if (match > 0)
                {
                    var next = SiegeTutorial.Taught(board);
                    Assert.IsTrue(next.Found, "the field ran out of moves mid-tutorial");
                    Assert.IsNotNull(board.Swap(next.A, next.B));
                }

                Assert.Greater(Run(board, () => SiegeTutorial.Fed(board) >= 0), 0,
                               "match " + (match + 1) + " never reached a turret");

                match++;
                Pour(board, SiegeTutorial.Fed(board), match);
            }

            Assert.AreEqual(1, board.Wards[SiegeTutorial.Charged(board)].Charges,
                            "the matches have to be worth one charge and never two");

            // The second panel waits for something to throw the charge at.
            int armed = -1;
            Run(board, () => (armed = SiegeTutorial.Armed(board)) >= 0);
            Assert.GreaterOrEqual(armed, 0, "the wave never arrived");

            var blast = board.Overcharge(armed, new List<SiegeStrike>());
            Assert.IsTrue(blast.Landed, "the tap the second panel asks for has to do something");

            // The last beat: the grove answers, and the run finishes on its own reading.
            Run(board, () => board.IsFinished, sweep: true);


            Assert.IsTrue(board.IsFinished, "the tutorial's hill has to clear");
            Assert.AreEqual(board.Wards.Count, board.WardsStanding,
                            "no turret may fall in the tutorial, ever");
        }

        /// <summary>
        /// A player who puts the phone down still has a line when they pick it up.
        ///
        /// <b>Two minutes of raiders swinging at it</b>, which is far past what any panel costs
        /// and is the case the guarantee exists for: the hill walks whether or not anybody is
        /// looking at it, and the opening tutorial may not end in a defeat panel.
        /// </summary>
        [Test]
        public void TheLineCannotFallHoweverLongNobodyPlays()
        {
            var board = Fresh();
            board.Sheltered = true;

            for (int i = 0; i < 60 * 120; i++) board.Advance(Frame);

            Assert.AreEqual(board.Wards.Count, board.WardsStanding);

            for (int i = 0; i < board.Wards.Count; i++)
                Assert.AreEqual(board.Wards[i].Full, board.Wards[i].Health,
                                "a turret is held whole rather than merely alive");

            Assert.IsFalse(board.IsFinished,
                           "nothing kills a raider until the player does, so the run is still open");
        }

        /// <summary>
        /// And the guarantee is doing real work: without it, this same board is lost.
        ///
        /// <b>The differential matters more than the absolute.</b> A fixture that only asserted
        /// the line survives would go on passing if the hill were ever quietly made harmless —
        /// at which point the tutorial would be teaching a mode that does not exist.
        /// </summary>
        [Test]
        public void WithoutTheGuaranteeTheSameBoardIsLost()
        {
            var board = Fresh();

            for (int i = 0; i < 60 * 120 && board.WardsStanding > 0; i++) board.Advance(Frame);

            Assert.AreEqual(0, board.WardsStanding,
                            "the tutorial's hill is the real one, and a real hill takes a line "
                            + "that never answers it");
        }

        /// <summary>
        /// A tube that is full over an empty hill is not a control to ring, and the script waits.
        ///
        /// <b><c>SiegeBoard.CanOvercharge</c>'s own rule, asked where the tutorial depends on
        /// it.</b> The second panel says <em>tap this</em>, and a panel that says that while the
        /// tap would be refused teaches the refusal (invariant 6b). The whole reason the script
        /// waits for a raider rather than raising the panel when the tube fills is this reading,
        /// so it is pinned rather than trusted.
        /// </summary>
        [Test]
        public void AFullTubeIsNotRungWhileTheHillIsEmpty()
        {
            var board = Fresh();

            board.Wards[0].Fill(board.Wards[0].Capacity);

            Assert.Greater(board.Wards[0].Charges, 0, "the tube is armed");
            Assert.AreEqual(0, board.OnTheHill, "and nothing has mustered yet");
            Assert.AreEqual(-1, SiegeTutorial.Armed(board));

            // **And the pair that stops the feeding loop hanging.** `Charged` has to see the
            // banked tube in exactly the state `Armed` refuses it, or a player who brims one
            // before the wave arrives leaves the script waiting for a feed that never comes.
            Assert.AreEqual(0, SiegeTutorial.Charged(board),
                            "a banked tube is a tube that has finished filling, hill or no hill");
        }

        // ------------------------------------------------------------------ driving
        /// <summary>
        /// Steps the board until <paramref name="until"/> answers true, holding the line exactly
        /// as the screen does. Answers how many frames it took, or 0.
        /// </summary>
        static int Run(SiegeBoard board, System.Func<bool> until, bool sweep = false)
        {
            for (int i = 1; i <= Patience; i++)
            {
                if (sweep) board.Kindle();

                board.Advance(Frame);

                if (until()) return i;
            }

            return 0;
        }

        /// <summary>
        /// One feed dripped in over <c>SiegeTutorial.PourSeconds</c>, exactly as
        /// <c>TutorialScreen.Pour</c> drips it. Answers whether it armed the tube.
        /// </summary>
        static bool Pour(SiegeBoard board, int ward, int match)
        {
            float want = SiegeTutorial.Feed(board, ward, match);
            float given = 0f;

            for (int i = 0; i < Patience && given < want; i++)
            {
                float step = want * Frame / SiegeTutorial.PourSeconds;
                if (step > want - given) step = want - given;

                given += step;

                if (board.Pour(ward, step)) return true;

                board.Advance(Frame);
            }

            return false;
        }

        static char[] Cells(SiegeBoard board)
        {
            var cells = new char[board.Count];
            for (int i = 0; i < cells.Length; i++) cells[i] = board.At(i);
            return cells;
        }
    }
}
