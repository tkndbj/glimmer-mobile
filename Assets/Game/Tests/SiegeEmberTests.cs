using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The ember family: what a burn is worth, how often it pays, and the one thing it must never
    /// be mistaken for.
    ///
    /// <para>
    /// <b>A file of its own because the fault it pins was invisible to every other one.</b> A burn
    /// has always been a damage-over-time in the rules, and every tick of it was reported to the
    /// view as a <c>SiegeBolt</c> — the record that means <em>a turret fired</em>. So the view
    /// answered each one with a recoil, a muzzle flash, a comet and an impact, about thirty times
    /// a second out of one barrel, and an ember turret read as a machine gun. Nothing was wrong
    /// with the arithmetic, which is exactly why no fixture and no gate could see it: the *type*
    /// of the record was the bug.
    /// </para>
    /// <para>
    /// <b>So what is held here is the shape as well as the sum.</b> A burn pays what it is
    /// authored to pay (which was always true and now has a test), it pays it on a cadence (which
    /// is what stops the drawing being a lie), and it reaches the view as a <see cref="SiegeBurn"/>
    /// and never as a bolt (which is the clause a refactor could undo in silence).
    /// </para>
    /// </summary>
    public sealed class SiegeEmberTests
    {
        static readonly string[] Field =
        {
            "rgrgyrry",
            "bgygybbg",
            "grrbbgyr",
            "bybbgryg",
            "ryygybrb",
        };

        static SiegeLayout Layout(string[] waves, string gems = "rgby", string wards = "rgby")
        {
            Assert.IsTrue(ProtoGrid.TryRead(Field, Field[0].Length, Field.Length,
                                            SiegeLayout.Cells, out var grid, out string error),
                          error);

            var layout = new SiegeLayout(grid, gems, wards, waves, null, 0);
            Assert.IsNull(layout.Fault, layout.Fault);
            return layout;
        }

        static SiegeLayout Plan(SiegeBoard board) => board.Layout;

        static void Feed(SiegeBoard board, int ward, float fuel)
        {
            board.Wards[ward].Fuel = fuel > board.Wards[ward].Capacity
                                   ? board.Wards[ward].Capacity
                                   : fuel;
        }

        /// <summary>The cheapest turret on the shelf that sets things alight.</summary>
        static WardModel Ember()
        {
            foreach (var model in WardCatalog.Default.Models)
                if (model.Ability == WardAbility.Ember) return model;

            Assert.Fail("no turret in the roster is an ember");
            return null;
        }

        /// <summary>A line standing one ember turret on red and the starter everywhere else.</summary>
        static WardLine EmberOnRed()
            => WardLine.Resolve(WardCatalog.Default,
                                new[] { new WardSlot('r', Ember().Id) }, (_, __) => true);

        // ------------------------------------------------------------------ the record
        /// <summary>
        /// <b>A burn is reported as a burn and never as a bolt, and that is the whole fix.</b>
        ///
        /// <para>
        /// The view draws a <c>SiegeBolt</c> as a shot leaving a barrel — <c>SiegeView.Bolt</c>
        /// recoils the turret, flashes the muzzle, flies a comet across the hill and lands it — so
        /// a tick of fire wearing that record was a turret firing thirty times a second. It cost
        /// nothing in the rules and it was the entire player-facing complaint.
        /// </para>
        /// <para>
        /// <b>Asked as a count of bolts from the ember's own seat</b>, which is the honest
        /// question: the seat still fires ordinary bolts and must go on doing so, and what may
        /// never happen again is more of them arriving than the ward's own cadence allows.
        /// </para>
        /// </summary>
        [Test]
        public void AnEmbersFireIsReportedAsABurnAndNeverAsABolt()
        {
            var board = SiegeBoard.Build(Layout(new[] { "rrrr" }), EmberOnRed());
            int red = Plan(board).WardOf('r');

            Assert.AreEqual(WardAbility.Ember, board.Wards[red].Model.Ability,
                            "the red seat is not standing the ember turret");

            for (int w = 0; w < board.Wards.Count; w++) Feed(board, w, 28f);

            int bolts = 0, burns = 0;
            float ran = 0f;

            for (int i = 0; i < 60 * 14; i++)
            {
                var report = board.Advance(1f / 60f);
                ran += 1f / 60f;

                for (int k = 0; k < report.Bolts.Count; k++)
                    if (report.Bolts[k].Ward == red) bolts++;

                burns += report.Burns.Count;
            }

            Assert.Greater(burns, 0, "an ember turret fired at a hill of its own colour for "
                                     + "fourteen seconds and nothing ever caught fire");

            // **The ceiling is the ward's own cadence, with a little air in it.** Anything above
            // this is the fault coming back: ticks of a burn arriving as shots.
            int most = (int)(ran / SiegeTuning.FireEvery) + 4;

            Assert.LessOrEqual(bolts, most,
                               $"the ember seat reported {bolts} bolts in {ran:0.0}s, and a ward "
                               + $"fires every {SiegeTuning.FireEvery}s - burn ticks are being "
                               + "reported as shots again, which is what made this read as a "
                               + "machine gun");
        }

        /// <summary>
        /// The first raider to walk onto the hill, with nothing shooting at it.
        ///
        /// <b>The board's own raider rather than one built here</b>, and that is the point of this
        /// helper: what these three cases are about is <c>SiegeBoard.Smoulder</c>, and a fixture
        /// that re-implemented the tick in order to measure it would be a second copy of the rule
        /// holding itself to itself — green for ever, whatever the game had gone on to do. The
        /// wards are left dry, so the only thing that can take health off this body is the burn.
        /// </summary>
        static SiegeRaider OnTheHill(SiegeBoard board)
        {
            for (int i = 0; i < 60 * 30; i++)
            {
                board.Advance(1f / 60f);

                for (int k = 0; k < board.Raiders.Count; k++)
                    if (board.Raiders[k].Alive && board.Raiders[k].OnTheHill)
                        return board.Raiders[k];
            }

            Assert.Fail("no raider ever walked onto the hill");
            return null;
        }

        /// <summary>Runs the board on a fixed step, tallying what one raider's fire took.</summary>
        static void Smoulder(SiegeBoard board, int id, float step, float seconds,
                             out int ticks, out int took)
        {
            ticks = 0;
            took = 0;

            for (float clock = 0f; clock < seconds; clock += step)
            {
                var report = board.Advance(step);

                for (int k = 0; k < report.Burns.Count; k++)
                {
                    if (report.Burns[k].Raider != id) continue;

                    ticks++;
                    took += report.Burns[k].Damage;
                }
            }
        }

        /// <summary>
        /// <b>A burn pays on a cadence rather than every frame, and the cadence is the drawing's
        /// only defence.</b>
        ///
        /// <para>
        /// The count is bounded from both sides on purpose. Too many is the old fault in a new
        /// record; too few would mean the shortest burn on the shelf pays once or twice, which
        /// reads as a delayed hit rather than as something burning — and
        /// <c>SiegeTuning.BurnTick</c> carries that argument in as many words.
        /// </para>
        /// <para>
        /// <b>And the same burn has to pay the same number of instalments at sixty a second and at
        /// a hundred and twenty</b>, which is what the carried remainder has always been for: an
        /// ability worth a different amount on a different phone is a difficulty that varies with
        /// the device.
        /// </para>
        /// </summary>
        [Test]
        public void ABurnPaysOnItsOwnCadenceAndNotOnTheFrameRate()
        {
            int[] ticks = new int[2];
            int[] took = new int[2];

            for (int pass = 0; pass < 2; pass++)
            {
                float step = pass == 0 ? 1f / 60f : 1f / 120f;

                var board = SiegeBoard.Build(Layout(new[] { "rrrr" }));
                var raider = OnTheHill(board);

                raider.Kindle(40, 3f, -1);

                Smoulder(board, raider.Id, step, 4f, out ticks[pass], out took[pass]);

                Assert.AreEqual(3f / SiegeTuning.BurnTick, ticks[pass], 1.5f,
                                $"a three-second burn paid {ticks[pass]} instalment(s) at "
                                + $"{1f / step:0} fps, and the cadence is every "
                                + $"{SiegeTuning.BurnTick}s");

                Assert.AreEqual(120, took[pass], 2,
                                "a 40-a-second burn over three seconds is 120, whatever it is "
                                + "handed over in - the carried remainder is what makes that true");
            }

            Assert.AreEqual(ticks[0], ticks[1], 1,
                            "the same burn paid a different number of instalments at 60 fps and "
                            + "at 120 - a burn's cadence must not be a fact about the device");
        }

        /// <summary>
        /// <b>The last instalment is paid on the beat the burn ends.</b>
        ///
        /// Without it, whatever a burn had accumulated since its last boundary would simply be
        /// dropped — an ember turret quietly paying less than the figure on its card, in a way no
        /// arithmetic anywhere else in this project could have seen. The shortest ember on the
        /// shelf runs three seconds, which is a whole number of cadences; a burn refreshed
        /// mid-interval by the next bolt almost never is, which is every real one.
        /// </summary>
        [Test]
        public void ABurnEndingMidCadenceStillPaysWhatItEarned()
        {
            var board = SiegeBoard.Build(Layout(new[] { "rrrr" }));
            var raider = OnTheHill(board);

            // A burn a third of a cadence past a boundary: it has earned an instalment and a bit,
            // and the bit is what a boundary-only rule would keep.
            float odd = SiegeTuning.BurnTick * 1.34f;
            raider.Kindle(60, odd, -1);

            Smoulder(board, raider.Id, 1f / 60f, odd + 1f, out _, out int took);

            Assert.AreEqual(60f * odd, took, 1.5f,
                            "a burn that ran out between two instalments kept what it had earned "
                            + "since the last one, so an ember pays less than its card says");
        }

        /// <summary>
        /// <b>A bolt landing again while a raider is already alight refreshes the burn without
        /// pushing its next payout away.</b>
        ///
        /// <para>
        /// This is the trap <c>SiegeRaider.Sear</c> is written round, and it is <c>Stagger</c>'s
        /// read from the other side. A ward fires every <c>SiegeTuning.FireEvery</c> seconds,
        /// which is shorter than the cadence — so a counter re-armed by every bolt would be pushed
        /// past its own boundary for ever, and a raider under continuous fire from the one turret
        /// bought to burn it would never take a single point of burn damage. It would look like an
        /// ability that simply did nothing.
        /// </para>
        /// </summary>
        [Test]
        public void ARaiderUnderContinuousFireStillTakesItsBurn()
        {
            var board = SiegeBoard.Build(Layout(new[] { "rrrr" }));
            var raider = OnTheHill(board);

            int took = 0;
            float since = SiegeTuning.FireEvery;

            for (float clock = 0f; clock < 4f; clock += 1f / 60f)
            {
                since += 1f / 60f;

                // The ember seat landing another bolt, at the cadence a fuelled ward really fires
                // at. This is `SiegeBoard.Ability`'s own call, made from outside.
                if (since >= SiegeTuning.FireEvery)
                {
                    since = 0f;
                    raider.Kindle(40, 3f, -1);
                }

                var report = board.Advance(1f / 60f);

                for (int k = 0; k < report.Burns.Count; k++)
                    if (report.Burns[k].Raider == raider.Id) took += report.Burns[k].Damage;
            }

            Assert.Greater(took, 100,
                           "a raider held under continuous ember fire took almost nothing: the "
                           + "cadence is being re-armed by every bolt, so the burn never reaches "
                           + "a payout");
        }

        // ------------------------------------------------------------------ the art
        /// <summary>
        /// <b>Every ward colour has a flame, and a line holding an ember asks for exactly the one
        /// it will draw.</b>
        ///
        /// <para>
        /// The addresses are <em>built</em> from a colour (<c>WardModel.BurnFor</c>), so
        /// <c>Tools/verify/artnames.py</c> cannot hold them to disk — which is the same gap the
        /// turret projectiles have and is why both content gates walk the roster instead. This is
        /// the third leg: that the scope a run loads names them at all. A line whose flame is not
        /// in its hold draws a white rectangle a body and a half tall (invariant 7b), and only a
        /// device would ever say so.
        /// </para>
        /// <para>
        /// <b>And a line with no ember on it asks for none of them</b>, which is invariant 7b from
        /// the other side: most lines hold no ember, and four reels resident for the life of a run
        /// to draw nothing is memory bounded by how much content exists rather than by what is on
        /// the screen.
        /// </para>
        /// </summary>
        [Test]
        public void ALineHoldingAnEmberScopesItsFlameAndOneWithoutAsksForNone()
        {
            var lit = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var request in EmberOnRed().Art()) lit.Add(request.Address);

            string flame = AssetManifest.SiegeFx(WardModel.BurnFor('r'));

            Assert.IsTrue(lit.Contains(flame),
                          $"a line standing an ember on red never asks for '{flame}'");

            foreach (char colour in WardLine.Colours)
            {
                if (colour == 'r') continue;

                string other = AssetManifest.SiegeFx(WardModel.BurnFor(colour));
                Assert.IsFalse(lit.Contains(other),
                               $"a line with one ember seat asks for '{other}' as well, so three "
                               + "reels are resident to draw nothing (invariant 7b)");
            }

            var bare = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var request in WardLine.Starter(WardCatalog.Default).Art())
                bare.Add(request.Address);

            foreach (char colour in WardLine.Colours)
                Assert.IsFalse(bare.Contains(AssetManifest.SiegeFx(WardModel.BurnFor(colour))),
                               "a line with no ember on it still loads a flame");
        }
    }
}
