using System;
using System.Collections.Generic;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The gravity well (<c>UtilityKind.Gravity</c>): every raider on the hill dragged to one of
    /// four places, held there, and let go slowed.
    ///
    /// <para>
    /// What is pinned hardest is what the item promises a player who paid for it - <em>all</em> of
    /// them, in <em>one</em> place, for the time on the card - and the three things it must not
    /// do: move a boss, cost the grade anything (invariant 39), or leave a body standing
    /// somewhere the rules do not think it is (invariant 39k).
    /// </para>
    /// </summary>
    public sealed class GravityTests
    {
        const int Hold = 10;                       // tenths: the second the shipped item holds for

        static SiegeLayout Layout(string wave, string boss)
        {
            var rows = new[]
            {
                "rgbyrg",
                "bygrby",
                "grbygr",
                "ybgrby",
            };

            return new SiegeLayout(
                ProtoGrid.TryRead(rows, 6, 4, SiegeLayout.Letters, out var grid, out _)
                    ? grid : null,
                "rgby", "rgby", new[] { wave }, boss: boss);
        }

        static SiegeBoard Board(string wave = "rrggbbyy", string boss = null)
        {
            var layout = Layout(wave, boss);
            Assert.IsNull(layout.Fault, layout.Fault);
            return SiegeBoard.Build(layout);
        }

        static void Run(SiegeBoard board, float seconds, float step = .05f)
        {
            for (float t = 0f; t < seconds - step * .5f; t += step) board.Advance(step);
        }

        /// <summary>A board with several raiders walking and nothing yet at the line.</summary>
        static SiegeBoard Walking()
        {
            var board = Board();
            Run(board, SiegeTuning.FirstWaveAfter + SiegeTuning.RaiderSpacing * 3f, .1f);
            return board;
        }

        static List<SiegeRaider> OnTheHill(SiegeBoard board)
        {
            var list = new List<SiegeRaider>();

            foreach (var raider in board.Raiders)
                if (raider.Alive && raider.OnTheHill) list.Add(raider);

            return list;
        }

        static UtilityItem Item()
            => new UtilityItem("gravityhole", UtilityKind.Gravity, Hold, 60, 100, 5, 0, 45);

        // ------------------------------------------------------------------ the pull
        [Test]
        public void EveryRaiderOnTheHillIsDraggedToTheWell()
        {
            for (int well = 0; well < SiegeTuning.GravityWells; well++)
            {
                var board = Walking();
                var standing = OnTheHill(board);

                Assert.Greater(standing.Count, 1, "the fixture has to put a hill up first");
                Assert.AreEqual(standing.Count, board.Gravity(well, Hold));

                Run(board, SiegeTuning.GravityGather + .05f);

                Assert.IsTrue(board.Gathered, "the hill should be in the well by now");

                foreach (var raider in standing)
                {
                    Assert.IsTrue(raider.Sunk);
                    Assert.AreEqual(SiegeTuning.WellMarch(well), raider.March, 1e-4f,
                                    "well " + well + " did not bring a raider down the hill to it");
                    Assert.AreEqual(SiegeTuning.WellLane(well), raider.Column,
                                    "well " + well + " did not bring a raider across the hill to it");
                }
            }
        }

        [Test]
        public void TheFourWellsAreFourDifferentPlaces()
        {
            var seen = new HashSet<string>();

            for (int well = 0; well < SiegeTuning.GravityWells; well++)
            {
                int lane = SiegeTuning.WellLane(well);
                float march = SiegeTuning.WellMarch(well);

                Assert.That(lane, Is.InRange(0, SiegeTuning.Lanes - 1));
                Assert.That(march, Is.GreaterThan(0f).And.LessThan(1f),
                            "a well at the line would hold raiders where they swing");
                Assert.IsTrue(seen.Add(lane + ":" + march), "two wells share a place");
            }
        }

        /// <summary>
        /// The pull is model time, so a body is somewhere between where it stood and the well
        /// while it lasts - never teleported, which is what lets the drawing follow it.
        /// </summary>
        [Test]
        public void TheDragTakesTimeAndOnlyEverClosesTheDistance()
        {
            var board = Walking();
            var raider = OnTheHill(board)[0];

            float gap = Math.Abs(raider.March - SiegeTuning.WellMarch(2));
            Assert.Greater(gap, .05f, "the fixture needs a raider away from the well");

            board.Gravity(2, Hold);

            for (float t = 0f; t < SiegeTuning.GravityGather; t += .05f)
            {
                board.Advance(.05f);

                float now = Math.Abs(raider.March - SiegeTuning.WellMarch(2));
                Assert.LessOrEqual(now, gap + 1e-5f, "a dragged body moved away from the well");
                gap = now;
            }

            Assert.AreEqual(0f, gap, 1e-4f);
        }

        // ------------------------------------------------------------------ the hold
        [Test]
        public void TheHillIsHeldForWhatWasBoughtAndNoLonger()
        {
            var board = Walking();
            var standing = OnTheHill(board);

            board.Gravity(0, Hold);
            Run(board, SiegeTuning.GravityGather + Hold / 10f - .1f);

            Assert.IsTrue(board.Sinking, "the well shut early");
            foreach (var raider in standing)
                Assert.AreEqual(SiegeTuning.WellMarch(0), raider.March, 1e-4f, "a held raider walked");

            Run(board, .2f);

            Assert.IsFalse(board.Sinking, "the well outstayed what was paid for");
            foreach (var raider in standing) Assert.IsFalse(raider.Sunk, "a raider was never let go");
        }

        /// <summary>
        /// A step long enough to swallow the whole well - a resumed app - leaves the hill where
        /// a frame-by-frame run would have.
        /// </summary>
        [Test]
        public void AWellSwallowedByLongStepsStillLetsEveryBodyGoAtTheWell()
        {
            var board = Walking();
            var standing = OnTheHill(board);

            board.Gravity(1, 1);
            for (int i = 0; i < 6; i++) board.Advance(.25f);

            Assert.IsFalse(board.Sinking);

            foreach (var raider in standing)
            {
                Assert.IsFalse(raider.Sunk);
                Assert.GreaterOrEqual(raider.March, SiegeTuning.WellMarch(1) - 1e-4f);
            }
        }

        [Test]
        public void NothingHeldInAWellStrikesTheLine()
        {
            var board = Board();

            // Walk the wave all the way down, so the well opens over raiders that are swinging.
            for (int i = 0; i < 600 && board.WardsStanding == board.Wards.Count; i++)
            {
                bool swinging = false;
                foreach (var raider in board.Raiders) if (raider.AtTheLine) swinging = true;
                if (swinging) break;

                board.Advance(.1f);
            }

            Assert.Greater(board.Gravity(3, Hold), 0);

            var health = new int[board.Wards.Count];
            for (int i = 0; i < health.Length; i++) health[i] = board.Wards[i].Health;

            while (board.Sinking)
            {
                board.Advance(.05f);

                for (int i = 0; i < health.Length; i++)
                    Assert.AreEqual(health[i], board.Wards[i].Health,
                                    "a ward was struck by something a well had hold of");
            }
        }

        // ------------------------------------------------------------------ the slow
        [Test]
        public void ABodyLetGoWalksSlowedForTwoSecondsAndThenAtItsOwnPace()
        {
            var board = Walking();
            var raider = OnTheHill(board)[0];

            board.Gravity(0, Hold);
            Run(board, SiegeTuning.GravityGather + Hold / 10f + .05f);

            Assert.IsTrue(raider.Weighed, "a body let go is not slowed");
            Assert.AreEqual((10 - SiegeTuning.GravitySlowTenths) / 10f, raider.Pace, 1e-5f);

            float was = raider.March;
            Run(board, .5f);
            Assert.Greater(raider.March, was, "a slowed body has to keep walking");

            Run(board, SiegeTuning.GravitySlowFor);

            Assert.IsFalse(raider.Weighed, "the slow outlasted its two seconds");
            Assert.AreEqual(1f, raider.Pace, 1e-5f);
        }

        /// <summary>
        /// The slower of the two and never their product: a chill and a well together may not
        /// add up to a raider that never arrives.
        /// </summary>
        [Test]
        public void AWellsSlowAndAChillTakeTheSlowerRatherThanMultiplying()
        {
            var board = Walking();
            var raider = OnTheHill(board)[0];

            raider.Drag = 1f;
            raider.Freeze(3, 1f);
            Assert.AreEqual(.5f, raider.Pace, 1e-5f);

            raider.Freeze(8, 1f);
            Assert.AreEqual(.2f, raider.Pace, 1e-5f);
        }

        [Test]
        public void ABodyLetGoWalksBackToItsOwnLane()
        {
            var board = Walking();
            var standing = OnTheHill(board);

            board.Gravity(0, Hold);
            Run(board, SiegeTuning.GravityGather + Hold / 10f + .05f);

            bool moved = false;
            foreach (var raider in standing) if (raider.Drift != 0f) moved = true;
            Assert.IsTrue(moved, "the fixture needs a raider the well took out of its lane");

            Run(board, 8f, .1f);

            foreach (var raider in standing)
            {
                if (!raider.Alive) continue;

                Assert.AreEqual(0f, raider.Drift, "a body never got back to its lane");
                Assert.AreEqual(raider.Lane, raider.Column);
            }
        }

        // ------------------------------------------------------------------ what it may not do
        [Test]
        public void ABossIsNeverMovedAndAWellOverOneAloneIsRefused()
        {
            var board = Board("rg", "warlord:r");

            Run(board, SiegeTuning.FirstWaveAfter + SiegeTuning.BetweenWaves * 4f, .1f);
            foreach (var raider in board.Raiders) { raider.Health = 0; raider.Alive = false; }
            Run(board, SiegeTuning.Breather + SiegeTuning.BossMarch + 2f, .1f);

            SiegeRaider boss = null;
            foreach (var raider in board.Raiders)
                if (raider.Alive && raider.Boss) boss = raider;

            Assert.IsNotNull(boss, "the fixture has to send a boss");

            float march = boss.March;

            Assert.IsFalse(SiegeUtility.Would(board, Item(), SiegeAim.AtWell(0)));
            Assert.AreEqual(0, board.Gravity(0, Hold));
            Assert.IsFalse(boss.Sunk);
            Assert.AreEqual(march, boss.March);
        }

        [Test]
        public void ARaiderThatStepsOutAfterTheWellOpenedWalksPastIt()
        {
            var board = Board();
            Run(board, SiegeTuning.FirstWaveAfter + SiegeTuning.RaiderSpacing * 1.5f, .1f);

            var late = new List<SiegeRaider>();
            foreach (var raider in board.Raiders)
                if (raider.Alive && !raider.OnTheHill) late.Add(raider);

            Assert.Greater(late.Count, 0, "the fixture needs a raider still in the wings");
            Assert.Greater(board.Gravity(0, Hold), 0);

            Run(board, SiegeTuning.GravityGather + .1f);

            foreach (var raider in late)
            {
                Assert.IsFalse(raider.Sunk, "a well took a raider that was not on the hill");
                Assert.AreEqual(0f, raider.Drift);
            }
        }

        [Test]
        public void OneWellAtATime()
        {
            var board = Walking();

            Assert.Greater(board.Gravity(0, Hold), 0);
            Assert.AreEqual(0, board.Gravity(3, Hold));
            Assert.IsFalse(SiegeUtility.Would(board, Item(), SiegeAim.AtWell(3)));
            Assert.AreEqual(0, board.Well);
        }

        [Test]
        public void AWellOverAnEmptyHillIsRefusedRatherThanSpent()
        {
            var board = Board();

            Assert.AreEqual(0, board.OnTheHill, "no wave has mustered yet");
            Assert.IsFalse(SiegeUtility.Would(board, Item(), SiegeAim.AtWell(0)));
            Assert.IsFalse(SiegeUtility.Apply(board, Item(), SiegeAim.AtWell(0), null).Landed);
        }

        [Test]
        public void AWellThatIsNotOneOfTheFourIsRefused()
        {
            var board = Walking();

            Assert.AreEqual(0, board.Gravity(-1, Hold));
            Assert.AreEqual(0, board.Gravity(SiegeTuning.GravityWells, Hold));
            Assert.AreEqual(0, board.Gravity(0, 0));
            Assert.IsFalse(board.Sinking);
        }

        // ------------------------------------------------------------------ the grade, and the picture
        /// <summary>
        /// Invariant 39 asked of the one utility that hurts nothing: it delivers no damage, so it
        /// saves no matches and is charged none.
        /// </summary>
        [Test]
        public void AWellHurtsNothingAndCostsTheGradeNothing()
        {
            var board = Walking();

            int health = 0;
            foreach (var raider in board.Raiders) health += raider.Health;

            var strikes = new List<SiegeStrike>();
            var use = SiegeUtility.Apply(board, Item(), SiegeAim.AtWell(1), strikes);

            Assert.IsTrue(use.Landed);
            Assert.AreEqual(0, use.Matches);
            Assert.Greater(use.Delivered, 0);
            Assert.AreEqual(0, strikes.Count);

            Run(board, SiegeTuning.GravityGather + Hold / 10f + .1f);

            int after = 0;
            foreach (var raider in board.Raiders) after += raider.Health;

            Assert.AreEqual(health, after, "a well took health off something");
        }

        /// <summary>
        /// Invariant 39k: what is drawn gathered in a box is what a firepot thrown at that box
        /// finds there.
        /// </summary>
        [Test]
        public void AFirepotThrownAtTheWellFindsEveryBodyInIt()
        {
            var board = Walking();
            var standing = OnTheHill(board);

            board.Gravity(0, Hold);
            Run(board, SiegeTuning.GravityGather + .05f);

            var strikes = new List<SiegeStrike>();
            board.Blast(SiegeTuning.WellLane(0), SiegeTuning.RowOf(SiegeTuning.WellMarch(0)), 1, strikes);

            var struck = new HashSet<int>();
            foreach (var hit in strikes) struck.Add(hit.Raider);

            foreach (var raider in standing)
                Assert.IsTrue(struck.Contains(raider.Id), "a body in the well was missed by a firepot on it");
        }

        [Test]
        public void ABoardNoWellWasOpenedOnStandsEveryRaiderInItsOwnLane()
        {
            var board = Walking();
            Run(board, 6f, .1f);

            foreach (var raider in board.Raiders)
            {
                Assert.AreEqual(raider.Lane, raider.Column);
                Assert.AreEqual(0f, raider.Drift);
                Assert.IsFalse(raider.Sunk);
                Assert.IsFalse(raider.Weighed);
            }
        }

        // ------------------------------------------------------------------ the taxonomy
        [Test]
        public void TheKindIsSpelledAimedAndMeasuredAsItShips()
        {
            Assert.AreEqual(UtilityKind.Gravity, UtilityKinds.Parse("gravity"));
            Assert.AreEqual("gravity", UtilityKinds.Id(UtilityKind.Gravity));
            Assert.AreEqual(UtilityTarget.Well, UtilityKinds.TargetOf(UtilityKind.Gravity));
            Assert.AreEqual(UtilityUnit.Time, UtilityUnits.Of(UtilityKind.Gravity));

            // The three older kinds are aimed where they always were.
            Assert.AreEqual(UtilityTarget.Hill, UtilityKinds.TargetOf(UtilityKind.Blast));
            Assert.AreEqual(UtilityTarget.Ward, UtilityKinds.TargetOf(UtilityKind.Mend));
            Assert.AreEqual(UtilityTarget.Ward, UtilityKinds.TargetOf(UtilityKind.Surge));
            Assert.AreEqual(UtilityTarget.Everywhere, UtilityKinds.TargetOf(UtilityKind.Storm));
        }
    }
}
