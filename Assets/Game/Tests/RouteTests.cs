using System.Collections.Generic;
using GlimmerGrove.Content;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The keeper's route: the player's turns measured against the distance the glade was
    /// authored around.
    ///
    /// <para>
    /// The whole reason this needs pinning is that the number is <b>beatable</b>.
    /// <see cref="Puzzle.TurnsToSolution"/> counts the turns to the authored solution, but a
    /// glade is won when every lamp is lit — spare conduits may be left pointing anywhere. So
    /// a player can finish in fewer turns than the route, and any design that called the
    /// route "perfect" or "the minimum" would be showing those players a contradiction. Three
    /// readings, not two, and this fixture exists to keep the third one from being optimised
    /// away by somebody who assumes it cannot happen.
    /// </para>
    /// </summary>
    public sealed class RouteTests
    {
        static readonly LevelId Glade = LevelId.Parse("plain_one");

        /// <summary>
        /// A one-cell board, solved. The factories read the board only for its id, move count
        /// and thresholds; the route is handed in, because the screen measures it before the
        /// player has touched anything and the board is by then a different board.
        /// </summary>
        static Puzzle Board(int moves)
        {
            var cells = new Cell[1];
            cells[0] = new Cell { kind = Kind.Lamp, solved = Puzzle.N, rot = 0, colour = 0 };

            var board = new Puzzle(Glade, 1, 1, LevelTuning.Default(3), cells);
            board.Moves = moves;
            return board;
        }

        static RunOutcome Win(int moves, int route)
            => RunOutcome.Win(Board(moves), stars: 3, previousBest: 0, firstClear: true,
                              attempt: 1, hintsUsed: 0, seconds: 20f, millis: 20_000, route: route);

        /// <summary>
        /// A replay that beat nothing, so <see cref="RunOutcome.NewBest"/> is false and the
        /// band is the only thing that can earn the panel.
        /// </summary>
        static RunOutcome Replay(int moves, int route)
            => RunOutcome.Win(Board(moves), stars: 3, previousBest: 1, firstClear: false,
                              attempt: 2, hintsUsed: 0, seconds: 20f, millis: 20_000, route: route);

        // -------------------------------------------------------------- the readings
        [Test]
        public void TurnsSpentBeyondTheRouteAreCounted()
        {
            var run = Win(moves: 40, route: 34);

            Assert.IsTrue(run.HasRoute);
            Assert.AreEqual(6, run.TurnsOverRoute);
            Assert.IsFalse(run.MatchedRoute);
            Assert.IsFalse(run.BeatRoute);
        }

        [Test]
        public void MatchingTheRouteExactlyIsPerfect()
        {
            var run = Win(moves: 34, route: 34);

            Assert.AreEqual(0, run.TurnsOverRoute);
            Assert.IsTrue(run.MatchedRoute);
            Assert.IsFalse(run.BeatRoute);
        }

        /// <summary>
        /// The reading the whole design turns on. A player who lights every lamp without
        /// straightening conduits the authored solution turned finishes under the route — and
        /// this is a real result, not a miscount. The save that prompted this feature has 31
        /// moves on a glade whose route is 34.
        /// </summary>
        [Test]
        public void FinishingUnderTheRouteIsAResultAndNotAnError()
        {
            var run = Win(moves: 31, route: 34);

            Assert.IsTrue(run.BeatRoute);
            Assert.IsFalse(run.MatchedRoute);
            Assert.AreEqual(-3, run.TurnsOverRoute, "negative, and the panel reads it as 3 under");
            Assert.IsTrue(run.RouteWorthSaying);
        }

        // -------------------------------------------------------------- the refusals
        [Test]
        public void ARunWithNoMeasuredRouteSaysNothing()
        {
            var run = Win(moves: 40, route: 0);

            Assert.IsFalse(run.HasRoute);
            Assert.IsFalse(run.RouteWorthSaying);
            Assert.AreEqual(0, run.TurnsOverRoute);
            Assert.IsFalse(run.MatchedRoute, "no route means no perfect, rather than 0 == 0");
            Assert.IsFalse(run.BeatRoute);
        }

        [Test]
        public void ANegativeRouteIsClampedAway()
        {
            var run = Win(moves: 40, route: -12);

            Assert.AreEqual(0, run.Route);
            Assert.IsFalse(run.HasRoute);
        }

        /// <summary>
        /// A loss never earns the panel. It has no stars, no record and no reward, and a
        /// screen that measured how efficiently somebody failed would be the least welcome
        /// thing in the game.
        /// </summary>
        [Test]
        public void ALostRunIsNeverMeasured()
        {
            var lost = RunOutcome.Loss(Board(40), DefeatReason.OutOfMoves, previousBest: 0,
                                       attempt: 1, hintsUsed: 0, seconds: 20f, millis: 20_000,
                                       route: 34);

            Assert.IsFalse(lost.HasRoute);
            Assert.IsFalse(lost.RouteWorthSaying);
        }

        // ------------------------------------------------------------ what is worth saying
        /// <summary>
        /// Drawn upward only. The bars themselves are now a section of the victory panel and
        /// appear on every run that has a route — they cost nothing, so there is nothing to
        /// ration. What is rationed is the <em>sentence</em> under them, because a line
        /// reporting twenty wasted turns after every win is a scolding on a victory screen.
        /// </summary>
        [Test]
        public void RunsInsideTheBandEarnTheSentence()
        {
            // Route 34 -> band is 34/8 = 4.
            Assert.IsTrue(Replay(moves: 31, route: 34).RouteWorthSaying, "under the route");
            Assert.IsTrue(Replay(moves: 34, route: 34).RouteWorthSaying, "exactly on it");
            Assert.IsTrue(Replay(moves: 38, route: 34).RouteWorthSaying, "four over is the boundary");

            Assert.IsFalse(Replay(moves: 39, route: 34).RouteWorthSaying, "five over says nothing");
            Assert.IsFalse(Replay(moves: 90, route: 34).RouteWorthSaying);
        }

        /// <summary>
        /// A personal best does <b>not</b> buy the sentence, and the reversal is deliberate.
        ///
        /// <para>
        /// It used to, and the argument held while this decided whether the player was sent to
        /// a panel of its own: a player who has just played their own best game deserves to be
        /// shown how it measured up at whatever standard they are currently at. Merging that
        /// panel into the victory screen removed the trip, so all this can buy now is a line —
        /// and the only line available to a record that is still 56 turns over the route is
        /// "56 turns from a perfect route", printed directly beside a stamp saying the run was
        /// the player's finest yet. The stamp keeps the recognition; the sentence keeps the half
        /// it can say something kind about.
        /// </para>
        /// </summary>
        [Test]
        public void APersonalBestFarOverTheRouteIsRecognisedButNotLectured()
        {
            var sloppyRecord = Win(moves: 90, route: 34);

            Assert.IsTrue(sloppyRecord.NewBest, "first clear, so there is no record to beat");
            Assert.IsTrue(sloppyRecord.HasRoute, "the bars are still drawn — they always are");
            Assert.IsFalse(sloppyRecord.RouteWorthSaying,
                           "the stamp says the kind thing; a sentence here could only scold");

            // And a replay that beats a previous record, still nowhere near the route.
            var beatIt = RunOutcome.Win(Board(80), stars: 3, previousBest: 90, firstClear: false,
                                        attempt: 2, hintsUsed: 0, seconds: 20f, millis: 20_000,
                                        route: 34);
            Assert.IsTrue(beatIt.NewBest);
            Assert.IsFalse(beatIt.RouteWorthSaying);
        }

        /// <summary>
        /// Proportional rather than flat. Shipped first at a flat two turns, which on the live
        /// save excluded three glades out of four and made the line effectively unreachable.
        /// </summary>
        [Test]
        public void TheBandScalesWithTheRouteAndNeverFallsBelowTheFloor()
        {
            Assert.AreEqual(RunOutcome.RouteNearFloor, Replay(1, 4).RouteNearBand, "a tiny route keeps the floor");
            Assert.AreEqual(RunOutcome.RouteNearFloor, Replay(1, 16).RouteNearBand, "16/8 == the floor");
            Assert.AreEqual(4, Replay(1, 34).RouteNearBand);
            Assert.AreEqual(6, Replay(1, 49).RouteNearBand);
            Assert.AreEqual(12, Replay(1, 100).RouteNearBand);
        }

        [Test]
        public void TheNearBandIsExactlyWhatTheRuleSays()
        {
            int route = 40;                       // band 5

            for (int over = -5; over <= 9; over++)
            {
                var run = Replay(route + over, route);
                bool expected = over <= run.RouteNearBand;

                Assert.AreEqual(expected, run.RouteWorthSaying,
                                $"{over} turns over a route of {route}");
            }
        }

        /// <summary>
        /// Every run the panel accepts must have exactly one of the three readings to draw,
        /// or it opens with no sentence on it.
        /// </summary>
        [Test]
        public void EveryShownRunHasExactlyOneReading()
        {
            int route = 20;

            for (int moves = 1; moves <= 40; moves++)
            {
                var run = Replay(moves, route);
                if (!run.RouteWorthSaying) continue;

                int readings = 0;
                if (run.BeatRoute) readings++;
                if (run.MatchedRoute) readings++;
                if (!run.BeatRoute && !run.MatchedRoute) readings++;

                Assert.AreEqual(1, readings, $"{moves} turns against a route of {route}");
            }
        }
    }
}
