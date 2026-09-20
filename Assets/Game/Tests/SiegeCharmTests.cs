using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The charms: what a prism joins, what a lance takes, what a stormglass reaches, and the one
    /// property that let all three ship without re-rolling every board in the mode.
    ///
    /// <para>
    /// <b>Its own fixture rather than a region of <c>SiegeRuleTests</c></b>, because these are
    /// rules about the <em>field</em> and that file is about the hill — and because every case
    /// here needs a board with a charm stood on a known cell, which is a seam
    /// (<c>SiegeBoard.Stand</c>) nothing else in the project uses.
    /// </para>
    /// <para>
    /// <b>Fields are written out rather than dealt.</b> A charm is dealt once a window
    /// (<c>SiegeTuning.CharmWithin</c>), so a fixture that waited for one would be measuring the
    /// roll instead of the rule and would go red the day the window is retuned. The roll has its
    /// own two cases below and they are the only ones that look at it.
    /// </para>
    /// </summary>
    public sealed class SiegeCharmTests
    {
        const string Gems = "rgby";
        const string Wards = "rgby";

        /// <summary>
        /// A hill long enough to be there while the field is being poked at, and short enough that
        /// nothing about it decides anything here.
        /// </summary>
        static readonly string[] Waves = { "rgby", "rgbyrgby" };

        static SiegeLayout Layout(string[] rows, string charms)
        {
            Assert.IsTrue(ProtoGrid.TryRead(rows, rows[0].Length, rows.Length,
                                            SiegeLayout.Cells, out var grid, out string error),
                          error);

            var layout = new SiegeLayout(grid, Gems, Wards, Waves, null, 0, null, 0, charms);
            Assert.IsNull(layout.Fault, layout.Fault);
            return layout;
        }

        /// <summary>
        /// A board dealing <b>the whole roster</b> unless a case says otherwise.
        ///
        /// <b>Derived rather than spelled</b>, and that is a correction: it read <c>"plsfh"</c>,
        /// so the day a sixth charm was appended this fixture went on dealing five and
        /// <see cref="ACharmFallsOnceAWindowAndNeverTwoWindowsApart"/> failed saying the roll was
        /// not choosing evenly - which is true of the board it was handed and says nothing about
        /// the board the game deals. A fixture that walks <c>SiegeCharms.Roster</c> at one end has
        /// to read it at the other.
        /// </summary>
        static SiegeBoard Board(string[] rows, string charms = null)
            => SiegeBoard.Build(Layout(rows, charms ?? SiegeCharms.Letters));

        /// <summary>
        /// A field with nothing lining up on it, five wide and three tall.
        ///
        /// <b>Written so that every case below can say what it changed.</b> Standing a charm on it
        /// and swapping two cells is the whole of what each test does, so a board that already had
        /// a run in it would fold the case's own answer into a cascade nobody asked for.
        /// </summary>
        static string[] Quiet() => new[]
        {
            "rrgby",
            "bygbr",
            "gbyrg",
        };

        /// <summary>
        /// The same idea with the swapped cell carrying a <em>red</em>, for the two charms whose
        /// payoff is not about colour.
        ///
        /// <b>A second field rather than one shared one</b>, because the prism's case rests on the
        /// cell underneath the charm being a colour the run is <em>not</em> — that is the whole
        /// thing it proves — and these two rest on the swap itself lining something up, which a
        /// pair of identical letters cannot do.
        /// </summary>
        static string[] Crossed() => new[]
        {
            "rrbgy",
            "gyrbg",
            "bgyrb",
        };

        static int WardOf(char colour) => SiegeLayout.Letters.IndexOf(colour);

        /// <summary>Everything one turn put into one ward, over every beat of it.</summary>
        static float Fuelled(SiegeTurn turn, char colour)
        {
            float whole = 0f;
            for (int i = 0; i < turn.Beats.Count; i++) whole += turn.Beats[i].Fuel[WardOf(colour)];
            return whole;
        }

        // ------------------------------------------------------------------ the prism
        /// <summary>
        /// <b>A prism joins a run of any colour</b>, which is the one thing about a charm that is
        /// a fact about the cell rather than about what happens when it goes.
        ///
        /// The field opens <c>"r r g"</c> along its top row with the third cell a plain green, and
        /// a prism is stood on the green below it. Swapping the two puts a colourless gem at the
        /// end of two reds: three cells, two of them really red, which is a run.
        /// </summary>
        [Test]
        public void APrismJoinsARunOfAnyColour()
        {
            var board = Board(Quiet());

            // Row 1, column 2 — the 'g' under the top row's own 'g'.
            board.Stand(7, SiegeCharm.Prism);

            Assert.IsFalse(board.Lines(0, 1), "the field was not authored quiet");
            Assert.IsTrue(board.Lines(2, 7),
                          "a prism dragged to the end of two reds lines nothing up, so a wild is "
                          + "not being read as a wild at all");

            var turn = board.Swap(2, 7);
            Assert.IsNotNull(turn);
            Assert.GreaterOrEqual(turn.Worth, 3);
        }

        /// <summary>
        /// <b>And it is paid as the colour it joined, never as the letter underneath.</b>
        ///
        /// <para>
        /// The prism in this case is standing on a <c>'g'</c>. If it were paid as its own letter
        /// the green ward would take a gem's worth out of a run of reds — which is a payoff the
        /// player can neither see nor aim, and the one way a wild could be worse than an ordinary
        /// gem. It is the whole reason <c>SiegeLayout.Runs</c> hands back what each cell was worth
        /// rather than only which cells went.
        /// </para>
        /// </summary>
        [Test]
        public void APrismIsPaidAsTheColourItJoinedAndNotTheLetterUnderneath()
        {
            var board = Board(Quiet());
            board.Stand(7, SiegeCharm.Prism);

            Assert.AreEqual('g', board.At(7), "this case rests on the prism carrying a green");

            var turn = board.Swap(2, 7);
            Assert.IsNotNull(turn);

            Assert.AreEqual(3f * SiegeTuning.FuelPerGem, Fuelled(turn, 'r'), .001f,
                            "the red ward should have been paid for all three cells of the run, "
                            + "the prism included");

            Assert.AreEqual(0f, Fuelled(turn, 'g'), .001f,
                            "the green ward was paid for a prism that completed a run of reds, so "
                            + "a wild is being paid as the letter it happens to carry");
        }

        /// <summary>
        /// <b>Three prisms touching are not a run</b>, which is what "at least one real colour"
        /// buys in <c>SiegeLayout.Runs</c>.
        ///
        /// Without it a column of wilds would clear itself the moment it landed, pay a colour
        /// nobody chose, and do it again on the refill — a cascade with no player in it.
        /// </summary>
        [Test]
        public void ABlockOfPrismsAloneIsNotARun()
        {
            // Asked of the rule directly rather than through a swap, because a board is not
            // allowed to hold this arrangement for long: a prism joins whatever is beside it, so
            // any field that could show three wilds touching would almost certainly also show them
            // touching something. What is being pinned is the clause, not the board.
            // Three wide, so the wilds fill their own row and the only thing that could lengthen
            // their block is a fourth wild. Below them, no colour repeats down any column.
            var cells = "rgb" + "rgb" + "gbr";
            var charms = new SiegeCharm[cells.Length];

            charms[0] = SiegeCharm.Prism;
            charms[1] = SiegeCharm.Prism;
            charms[2] = SiegeCharm.Prism;

            Assert.AreEqual(0, SiegeLayout.Runs(cells.ToCharArray(), 3, 3, charms).Count,
                            "three prisms touching cleared themselves, so a block of wilds is "
                            + "being read as a colour of its own - it would go off the moment it "
                            + "landed and pay a colour nobody chose");

            // And the clause is narrow: take the charm off the middle one, so the same block holds
            // a real green, and the same three cells are a run.
            charms[1] = SiegeCharm.None;

            Assert.AreEqual(3, SiegeLayout.Runs(cells.ToCharArray(), 3, 3, charms).Count,
                            "a green between two prisms is not a run, so the clause is refusing "
                            + "wilds rather than refusing a block made only of them");
        }

        // ------------------------------------------------------------------ the lance
        /// <summary>
        /// <b>A lance takes its whole row and its whole column.</b>
        ///
        /// Five wide and three tall, so a cross is five plus three less the one it is standing on:
        /// seven cells, plus whatever the run that set it off took that the cross did not.
        /// </summary>
        [Test]
        public void ALanceTakesItsRowAndItsColumn()
        {
            var board = Board(Crossed());
            board.Stand(7, SiegeCharm.Lance);

            // Row 0 opens "r r b"; the lance is standing on the red below that 'b'. Swapping the
            // two makes a run of three reds with the lance at the end of it.
            var turn = board.Swap(2, 7);
            Assert.IsNotNull(turn, "the fixture field no longer lines anything up on this swap");

            var beat = turn.Beats[0];
            var took = new HashSet<int>(beat.Cleared);

            for (int x = 0; x < 5; x++)
                Assert.IsTrue(took.Contains(x), $"the lance's own row kept cell {x}");

            Assert.IsTrue(took.Contains(7) && took.Contains(12),
                          "the lance's own column was not taken");

            Assert.AreEqual(7, beat.Cleared.Count,
                            "a cross on a five-by-three field is five plus three less the one it "
                            + "is standing on, and the run that set it off was inside its own row");

            Assert.AreEqual(1, beat.Sprung.Count, "exactly one charm should have gone off");
            Assert.AreEqual(SiegeCharm.Lance, beat.Sprung[0].Charm);
            Assert.AreEqual(2, beat.Sprung[0].Cell);
        }

        /// <summary>
        /// <b>And every cell a cross takes is worth its own colour's fuel</b>, which is what stops
        /// a lance being worth a colour the player did not choose.
        ///
        /// A cross reaches seven cells here and three of them are red, so the red ward is paid for
        /// three and the other four go to whichever wards their own colours feed.
        /// </summary>
        [Test]
        public void EveryCellALanceTakesIsPaidAsItsOwnColour()
        {
            var board = Board(Crossed());
            board.Stand(7, SiegeCharm.Lance);

            // Read before the swap, because the field has refilled by the time it answers.
            var worth = new Dictionary<char, int>();

            var cross = new[] { 0, 1, 2, 3, 4, 7, 12 };
            foreach (int cell in cross)
            {
                char colour = cell == 2 ? board.At(7) : cell == 7 ? board.At(2) : board.At(cell);
                worth.TryGetValue(colour, out int seen);
                worth[colour] = seen + 1;
            }

            var turn = board.Swap(2, 7);
            Assert.IsNotNull(turn);

            for (int i = 0; i < SiegeLayout.Letters.Length; i++)
            {
                char colour = SiegeLayout.Letters[i];
                worth.TryGetValue(colour, out int gems);

                Assert.AreEqual(gems * SiegeTuning.FuelPerGem, Fuelled(turn, colour), .001f,
                                $"the '{colour}' ward was paid for the wrong number of gems");
            }
        }

        /// <summary>
        /// <b>A lance taken by a lance goes off too</b>, which is the one chain on this field that
        /// is not a cascade.
        ///
        /// <para>
        /// It is not a flourish: a cross is resolved through a queue precisely so that the rule is
        /// "a lance sets off what it takes" rather than "a lance sets off what it takes, once".
        /// A fixed depth would be a rule nobody wrote down and a player would find in a minute.
        /// </para>
        /// </summary>
        [Test]
        public void ALanceTakenByALanceGoesOffToo()
        {
            var board = Board(Crossed());

            board.Stand(7, SiegeCharm.Lance);

            // The bottom of the column the first lance will stand in, and in no run of its own —
            // so the only thing on this board that can reach it is the first lance's cross.
            board.Stand(12, SiegeCharm.Lance);

            var turn = board.Swap(2, 7);
            Assert.IsNotNull(turn);

            var beat = turn.Beats[0];
            Assert.AreEqual(2, beat.Sprung.Count,
                            "the second lance stood in the first one's column and did not go off");

            var took = new HashSet<int>(beat.Cleared);
            for (int x = 10; x < 15; x++)
                Assert.IsTrue(took.Contains(x),
                              $"the second lance's own row kept cell {x}, so a chain resolved one "
                              + "deep and stopped");
        }

        /// <summary>
        /// <b>A prism a cross sweeps up is paid as the lance's colour</b>, which is the one cell on
        /// this field with no honest answer of its own.
        ///
        /// It joined no run, so there is no colour it chose; the letter underneath is whatever the
        /// deal handed it and the player can neither see it nor have aimed at it. The beam is drawn
        /// in the lance's colour, so that is the only answer the board agrees with — and without
        /// this the fuel would go to a ward the player had no way of knowing about.
        /// </summary>
        [Test]
        public void APrismACrossSweepsUpIsPaidAsTheLancesColour()
        {
            var board = Board(Crossed());

            board.Stand(7, SiegeCharm.Lance);

            // Row 2, column 2 - in the lance's column once the swap has moved it, and in no run.
            board.Stand(12, SiegeCharm.Prism);

            Assert.AreEqual('y', board.At(12),
                            "this case rests on the prism carrying a colour the cross is not");

            var turn = board.Swap(2, 7);
            Assert.IsNotNull(turn);

            // The cross is red - the run that sprang it was three reds - and it takes seven cells:
            // the three reds of the run, a green, a genuine yellow, a blue, and the prism. So the
            // yellow ward is paid for exactly *one* gem, and the prism goes to red with the beam
            // that took it. Paid as its own letter it would have been two.
            Assert.AreEqual(SiegeTuning.FuelPerGem, Fuelled(turn, 'y'), .001f,
                            "a prism swept up by a red cross paid the yellow ward, so it was paid "
                            + "as the letter underneath - a colour the player never saw");

            Assert.AreEqual(4f * SiegeTuning.FuelPerGem, Fuelled(turn, 'r'), .001f,
                            "the red ward should have been paid for its three reds and the prism "
                            + "the beam swept up");
        }

        // ------------------------------------------------------------------ the stormglass
        /// <summary>
        /// <b>A stormglass lands after the gem it came out of has burst, and not before</b> —
        /// invariant 37s, which turn-based modes are immune to by construction and this one is not.
        ///
        /// <para>
        /// The model resolves a whole swap in an instant and the view spends most of a second
        /// drawing it, so a volley applied where it is sprung would kill raiders before the match
        /// that paid for them had finished going off. It is booked exactly as a match's fuel is.
        /// </para>
        /// </summary>
        [Test]
        public void AStormglassLandsAfterItsGemHasBurstAndNotBefore()
        {
            var board = Board(Crossed());

            // Let the first wave walk on, so there is something for a storm to reach.
            for (int i = 0; i < 60 * 8; i++) board.Advance(1f / 60f);
            Assert.Greater(board.OnTheHill, 0, "nothing walked on, so this case proves nothing");

            int whole = Standing(board);

            board.Stand(7, SiegeCharm.Storm);
            Assert.IsNotNull(board.Swap(2, 7));

            Assert.AreEqual(whole, Standing(board),
                            "a stormglass took health on the frame it was matched, so its bolts "
                            + "land before the gem that threw them has burst");

            bool landed = false;
            for (int i = 0; i < 60 && !landed; i++)
                landed = board.Advance(1f / 60f).Charmed.Count > 0;

            Assert.IsTrue(landed, "a stormglass was matched and never landed at all");
            Assert.Less(Standing(board), whole, "it landed and took nothing");
        }

        /// <summary>
        /// <b>A stormglass is fired by the line, and that is a rule about the shelf rather than
        /// about the charm.</b>
        ///
        /// <para>
        /// Every standing ward throws at every raider on the hill, and the ward wearing the
        /// charm's own colour throws twice — so what a stormglass is worth is decided by the
        /// turrets the player bought and the ranks their cogs paid for. A flat figure was the
        /// first shape and is quietly corrosive: free damage that does not scale with the shelf
        /// flattens the one ladder in this mode anybody pays for (invariant 42), and it did,
        /// measurably, over ninety runs a chapter.
        /// </para>
        /// <para>
        /// Counted rather than totalled, because the totals are the wards' own arithmetic and are
        /// pinned where that arithmetic lives. What this fixture owns is <em>who fires and how
        /// often</em>.
        /// </para>
        /// </summary>
        [Test]
        public void AStormglassIsFiredByTheWholeLine()
        {
            var board = Board(Crossed());

            for (int i = 0; i < 60 * 8; i++) board.Advance(1f / 60f);

            int hill = board.OnTheHill;
            Assert.Greater(hill, 0, "nothing walked on, so this case proves nothing");
            Assert.AreEqual(4, board.WardsStanding, "this case assumes a full line");

            // Stood on cell 7, which carries a red — so the red ward is the one that fires twice.
            Assert.AreEqual('r', board.At(7));

            board.Stand(7, SiegeCharm.Storm);
            Assert.IsNotNull(board.Swap(2, 7));

            var volley = new List<SiegeBolt>();
            for (int i = 0; i < 60 && volley.Count == 0; i++)
                volley.AddRange(board.Advance(1f / 60f).Charmed);

            Assert.IsNotEmpty(volley, "a stormglass was matched and the line never fired");

            var threw = new Dictionary<int, int>();
            foreach (var bolt in volley)
            {
                threw.TryGetValue(bolt.Ward, out int seen);
                threw[bolt.Ward] = seen + 1;
            }

            Assert.AreEqual(4, threw.Count, "a ward on the line threw nothing at all");

            int own = WardOf('r');
            foreach (var pair in threw)
            {
                int want = pair.Key == own ? SiegeTuning.CharmVolleyOwn : SiegeTuning.CharmVolley;

                // A bolt that kills takes its raider off the hill, so what is asserted is that the
                // ward threw its share at everything that was still standing when it did - never a
                // flat count, which would make this a test of how hard the line happens to hit.
                Assert.LessOrEqual(pair.Value, want * hill);
                Assert.Greater(pair.Value, 0);
            }

            Assert.Greater(threw[own], 0,
                           "the ward wearing the charm's own colour threw nothing, so the gem's "
                           + "colour is a picture that decides nothing");
        }

        /// <summary>
        /// <b>And a fallen ward throws nothing</b>, which is what stops a stormglass being worth
        /// the same to a player who is losing as to one who is not.
        /// </summary>
        // ------------------------------------------------------------------ the furnace
        [Test]
        public void AFurnaceBanksAChargeOnTheWardOfItsColourAfterItsGemHasBurst()
        {
            var board = Board(Crossed(), "plsfh");
            for (int i = 0; i < 60 * 8; i++) board.Advance(1f / 60f);

            int own = WardOf('r');
            Assert.AreEqual(0, board.Wards[own].Charges, "this case assumes an empty tube");
            Assert.AreEqual('r', board.At(7));

            board.Stand(7, SiegeCharm.Furnace);
            Assert.IsNotNull(board.Swap(2, 7));

            Assert.AreEqual(0, board.Wards[own].Charges,
                            "a furnace banked on the frame it was matched, so the charge lands "
                            + "before the gem that paid for it has burst");

            SiegeForged? forged = null;
            for (int i = 0; i < 60 && forged == null; i++)
            {
                var report = board.Advance(1f / 60f);
                if (report.Forged.Count > 0) forged = report.Forged[0];
            }

            Assert.IsNotNull(forged, "a furnace was matched and never landed at all");
            Assert.AreEqual(own, forged.Value.Ward, "it landed on a ward of another colour");
            Assert.IsTrue(forged.Value.Banked);
            Assert.AreEqual(SiegeTuning.FurnaceCharges, board.Wards[own].Charges);
            Assert.IsTrue(board.Wards[own].Armed, "a banked furnace is not a charge that can be thrown");

            for (int w = 0; w < board.Wards.Count; w++)
                if (w != own)
                    Assert.AreEqual(0, board.Wards[w].Charges, "a furnace reached a second ward");
        }

        [Test]
        public void AFurnaceOnAFullOrFallenWardIsRefusedAndSaysSo()
        {
            var board = Board(Crossed(), "plsfh");
            for (int i = 0; i < 60 * 8; i++) board.Advance(1f / 60f);

            int own = WardOf('r');
            board.Wards[own].Charges = SiegeTuning.MostCharges;

            board.Stand(7, SiegeCharm.Furnace);
            Assert.IsNotNull(board.Swap(2, 7));

            SiegeForged? forged = null;
            for (int i = 0; i < 60 && forged == null; i++)
            {
                var report = board.Advance(1f / 60f);
                if (report.Forged.Count > 0) forged = report.Forged[0];
            }

            Assert.IsNotNull(forged, "a refused furnace was not reported, so the view draws nothing");
            Assert.IsFalse(forged.Value.Banked, "a full ward banked a third charge");
            Assert.AreEqual(SiegeTuning.MostCharges, board.Wards[own].Charges,
                            "a furnace stacked past the cap a brimming tube stops at");
        }

        // ------------------------------------------------------------------ the hourglass
        [Test]
        public void AnHourglassStopsTheHillAndTheLineKeepsFiring()
        {
            var board = Board(Crossed(), "plsfh");
            for (int i = 0; i < 60 * 8; i++) board.Advance(1f / 60f);
            Assert.Greater(board.OnTheHill, 0, "nothing walked on, so this case proves nothing");

            board.Stand(7, SiegeCharm.Hourglass);
            Assert.IsNotNull(board.Swap(2, 7));
            Assert.IsFalse(board.Stilled, "the hill stopped on the frame of the swap, before the gem burst");

            float stilled = 0f;
            for (int i = 0; i < 60 && stilled <= 0f; i++) stilled = board.Advance(1f / 60f).Stilled;

            Assert.AreEqual(SiegeTuning.HourglassFor, stilled, 1e-4f, "an hourglass was matched and never landed");
            Assert.IsTrue(board.Stilled);

            var marched = new System.Collections.Generic.Dictionary<int, float>();
            foreach (var raider in board.Raiders)
                if (raider.Alive && raider.OnTheHill) marched[raider.Id] = raider.March;

            int whole = Standing(board);
            for (int i = 0; i < 60; i++) board.Advance(1f / 60f);

            Assert.IsTrue(board.Stilled, "the hill walked again inside the first second of a three-second stop");
            foreach (var raider in board.Raiders)
                if (marched.TryGetValue(raider.Id, out float was))
                    Assert.AreEqual(was, raider.March, 1e-5f, $"raider {raider.Id} walked while the hill stood still");

            Assert.Less(Standing(board), whole, "the line fired nothing into a stopped hill");

            for (int i = 0; i < 60 * 3; i++) board.Advance(1f / 60f);
            Assert.IsFalse(board.Stilled, "the hill never walked again");
        }

        [Test]
        public void AnHourglassIsExtendedByASecondAndNeverStacked()
        {
            var board = Board(Crossed(), "plsfh");
            for (int i = 0; i < 60 * 8; i++) board.Advance(1f / 60f);

            board.Stand(7, SiegeCharm.Hourglass);
            Assert.IsNotNull(board.Swap(2, 7));
            for (int i = 0; i < 60 && !board.Stilled; i++) board.Advance(1f / 60f);
            Assert.IsTrue(board.Stilled);

            for (int i = 0; i < 30; i++) board.Advance(1f / 60f);
            float left = board.StillLeft;
            Assert.Less(left, SiegeTuning.HourglassFor);

            // A second landing half a second in: the clock is set back to a whole window, not
            // to a window and a half. The first swap changed the field, so the second is found
            // rather than typed - with an hourglass stood on every gem, whatever clears springs
            // one, and two springing in one beat is the stacking this case is about.
            for (int i = 0; i < board.Count; i++)
                if (SiegeLayout.IsGem(board.At(i))) board.Stand(i, SiegeCharm.Hourglass);

            bool swapped = false;
            for (int a = 0; a < board.Count && !swapped; a++)
            {
                if (a % board.Width + 1 < board.Width && board.Swap(a, a + 1) != null) swapped = true;
                else if (a + board.Width < board.Count && board.Swap(a, a + board.Width) != null) swapped = true;
            }
            Assert.IsTrue(swapped, "no swap on this field lines anything up");

            for (int i = 0; i < 60 && board.StillLeft <= left; i++) board.Advance(1f / 60f);
            Assert.Greater(board.StillLeft, left, "the second hourglass never landed");

            Assert.LessOrEqual(board.StillLeft, SiegeTuning.HourglassFor + 1e-4f,
                               "two hourglasses stacked into a stop longer than one window");
        }

        // ------------------------------------------------------------------ the anvil
        [Test]
        public void AnAnvilDrivesTheWholeHillBackUpTheSlope()
        {
            var board = Board(Crossed());
            for (int i = 0; i < 60 * 8; i++) board.Advance(1f / 60f);
            Assert.Greater(board.OnTheHill, 0, "nothing walked on, so this case proves nothing");

            var was = new Dictionary<int, float>();
            foreach (var raider in board.Raiders)
                if (raider.Alive && raider.OnTheHill) was[raider.Id] = raider.March;

            Assert.Greater(was.Count, 0);

            board.Stand(7, SiegeCharm.Anvil);
            Assert.IsNotNull(board.Swap(2, 7));
            Assert.IsFalse(board.Heaving,
                           "the hill was thrown on the frame of the swap, before the gem burst");

            float heaved = 0f;
            for (int i = 0; i < 60 && heaved <= 0f; i++) heaved = board.Advance(1f / 60f).Heaved;

            Assert.AreEqual(SiegeTuning.AnvilHeave, heaved, 1e-4f,
                            "an anvil was matched and never landed");

            // **The shove is worked off over a beat rather than applied in one frame**, which is
            // the whole reason it is a debt: the view draws a raider wherever the model says it
            // is, once a frame, so an instant shove is a hill that teleports.
            Assert.IsTrue(board.Heaving, "the shove landed and nothing was being thrown");

            for (int i = 0; i < 60 && board.Heaving; i++) board.Advance(1f / 60f);
            Assert.IsFalse(board.Heaving, "the shove never finished");

            int moved = 0;

            foreach (var raider in board.Raiders)
            {
                if (!raider.Alive || !was.TryGetValue(raider.Id, out float before)) continue;

                Assert.LessOrEqual(raider.March, before + 1e-4f,
                                   $"raider {raider.Id} was further down the hill after an anvil");

                if (before - raider.March > 1e-3f) moved++;

                Assert.GreaterOrEqual(raider.March, -1e-4f,
                                      "a raider was thrown past the crest");
            }

            Assert.Greater(moved, 0, "an anvil landed on a full hill and moved nothing");
        }

        /// <summary>
        /// **An anvil pays in ground and never in damage, and that is what keeps it off par.**
        /// A stormglass pays in damage and `SiegeTuning.Par` counts it; a shove is worth seconds
        /// of the *line's* own fire, which par already credits (invariant 37ed).
        ///
        /// <b>Asked of the charm's own channel rather than of raider health</b>, and the first
        /// cut asked the wrong one: the line is firing throughout, so every raider on a live hill
        /// loses health over any sixty frames whatever the charm did. <c>SiegeReport.Charmed</c>
        /// is what a charm puts on the hill, so an empty one is the claim - and
        /// <c>Shoved</c> beside it is the claim that the beat was not simply a dud.
        /// </summary>
        [Test]
        public void AnAnvilPaysInGroundAndNeverInDamage()
        {
            var board = Board(Crossed());
            for (int i = 0; i < 60 * 8; i++) board.Advance(1f / 60f);
            Assert.Greater(board.OnTheHill, 0, "nothing walked on, so this case proves nothing");

            board.Stand(7, SiegeCharm.Anvil);
            Assert.IsNotNull(board.Swap(2, 7));

            var charmed = new List<SiegeBolt>();
            int shoved = 0;

            for (int i = 0; i < 60; i++)
            {
                var report = board.Advance(1f / 60f);
                charmed.AddRange(report.Charmed);
                if (report.Shoved > shoved) shoved = report.Shoved;
            }

            Assert.IsEmpty(charmed, "an anvil threw bolts, so it is a stormglass with a shove");
            Assert.Greater(shoved, 0, "an anvil landed on a full hill and reported nothing moved");
        }

        [Test]
        public void AShovedRaiderStopsSwinging()
        {
            var board = Board(Crossed());

            // Walk one body all the way to the line, which is where a shove is worth most.
            for (int i = 0; i < 60 * 60; i++)
            {
                board.Advance(1f / 60f);

                bool arrived = false;
                foreach (var raider in board.Raiders)
                    if (raider.AtTheLine) arrived = true;

                if (arrived) break;
            }

            SiegeRaider standing = null;
            foreach (var raider in board.Raiders) if (raider.AtTheLine) standing = raider;

            Assert.IsNotNull(standing, "nothing reached the line, so this case proves nothing");
            Assert.IsTrue(standing.Shove(SiegeTuning.AnvilHeave));

            board.Advance(1f / 60f);

            Assert.IsFalse(standing.AtTheLine,
                           "a raider shoved off the line is still at it, so the shove buys the "
                           + "line no seconds at all - which is the whole of what it is for");
        }

        [Test]
        public void AnAnvilIsWorthMoreSecondsAgainstArmourThanAgainstASwarm()
        {
            // **It scales with the board and not with a table** (37ed). The shove is a share of
            // the hill and `MarchOf` is how long a kind takes to walk it, so the same ground
            // costs a bulwark more than twice what it costs a creeper - the charm is worth most
            // against exactly the thing a surged chapter is made of, and nothing says so but this.
            float creeper = SiegeTuning.AnvilHeave * SiegeTuning.CreeperMarch;
            float bulwark = SiegeTuning.AnvilHeave * SiegeTuning.BulwarkMarch;

            Assert.Greater(bulwark, creeper * 2f,
                           "a shove buys the line less than twice as long against a bulwark as "
                           + "against a creeper, so the charm no longer answers armour");

            Assert.Greater(creeper, SiegeTuning.FireEvery * 4f,
                           "a shove buys the line under four bolts against the lightest body on "
                           + "the hill, which is a payoff nobody would hold a gem for");
        }

        [Test]
        public void AFallenWardThrowsNothingIntoAVolley()
        {
            var board = Board(Crossed());

            for (int i = 0; i < 60 * 8; i++) board.Advance(1f / 60f);
            Assert.Greater(board.OnTheHill, 0);

            board.Wards[0].Health = 0;
            board.Wards[0].Alive = false;

            board.Stand(7, SiegeCharm.Storm);
            Assert.IsNotNull(board.Swap(2, 7));

            var volley = new List<SiegeBolt>();
            for (int i = 0; i < 60 && volley.Count == 0; i++)
                volley.AddRange(board.Advance(1f / 60f).Charmed);

            Assert.IsNotEmpty(volley);

            foreach (var bolt in volley)
                Assert.AreNotEqual(0, bolt.Ward, "a ward that has fallen threw into the volley");
        }

        static int Standing(SiegeBoard board)
        {
            int whole = 0;
            var raiders = board.Raiders;

            for (int i = 0; i < raiders.Count; i++)
                if (raiders[i].Alive) whole += raiders[i].Health;

            return whole;
        }

        // ------------------------------------------------------------------ the deal
        /// <summary>
        /// <b>Dealing a charm costs no extra draw, and that is what let this ship without
        /// re-rolling every board in the mode.</b>
        ///
        /// <para>
        /// Invariant 41: a random stream is part of a level's content, and anything that changes
        /// how often it is drawn from is a content change. A second <c>Next()</c> for the charm
        /// roll would have dealt a different gem into every column of every rung from the first
        /// refill on — the last time that happened three of the first chapter's ten became
        /// unholdable with nothing in any file wrong. So the charm is read out of the same word,
        /// and this is the case that says so: two boards from one field, one dealing every charm
        /// and one dealing none, deal the <em>same letters</em> for five hundred gems.
        /// </para>
        /// </summary>
        [Test]
        public void DealingACharmCostsNoExtraDraw()
        {
            var plain = Board(Quiet(), "");
            var charmed = Board(Quiet(), "pls");

            int found = 0;

            for (int i = 0; i < 500; i++)
            {
                char a = plain.Deal(0, out var none);
                char b = charmed.Deal(0, out var charm);

                Assert.AreEqual(a, b,
                                $"gem {i} of the deal differs between a charmed field and a plain "
                                + "one, so a charm roll is costing a draw and every board in this "
                                + "mode has been re-rolled");

                Assert.AreEqual(SiegeCharm.None, none,
                                "a field authoring no charms dealt one");

                if (charm != SiegeCharm.None) found++;
            }

            Assert.Greater(found, 0, "five hundred gems and not one charm");
        }

        /// <summary>
        /// <b>A charm falls once a window, and the gap between two of them is bounded.</b>
        ///
        /// <para>
        /// <b>Bounded is the assertion, not the average — and the average is what the first
        /// version shipped.</b> A rate rolled per gem is uniform over a long enough stream and
        /// useless on a board, because this stream is deterministic: a geometric gap is sometimes
        /// long, and a long one on a shipped field is the same board dealing the same nothing to
        /// every player who ever opens it. <c>s01_stonewatch</c> put its first charm at deal 351
        /// against a run that ends at about 324 — the rung that <em>introduces</em> the prism,
        /// dealing none, for ever, with every gate green. It was found by somebody playing it.
        /// </para>
        /// <para>
        /// So what is pinned is the shape a window guarantees: never two windows apart, and every
        /// charm on the roster turning up. The count follows from the bound and is not asserted
        /// twice.
        /// </para>
        /// </summary>
        [Test]
        public void ACharmFallsOnceAWindowAndNeverTwoWindowsApart()
        {
            // **Sixty thousand rather than twenty, and that is the roster growing.** The
            // per-charm band below is a share of the sample, so a six-way split of 362 charms
            // has a standard deviation of seven against an even share of sixty - a third either
            // side is three sigma, which is a gate that flakes. At this sample it is four.
            const int Gems = 60000;

            var board = Board(Quiet());
            var counted = new Dictionary<SiegeCharm, int>();

            int found = 0, since = 0, widest = 0;

            for (int i = 0; i < Gems; i++)
            {
                board.Deal(0, out var charm);
                since++;

                if (charm == SiegeCharm.None) continue;

                if (since > widest) widest = since;
                since = 0;

                found++;
                counted.TryGetValue(charm, out int seen);
                counted[charm] = seen + 1;
            }

            Assert.LessOrEqual(widest, SiegeTuning.CharmWithin,
                               $"the widest gap between two charms was {widest} gems against the "
                               + $"{SiegeTuning.CharmWithin} the window allows - a gap that is "
                               + "only bounded on average is a board that deals none");

            // **The mean is twice the floor, and both are asserted because they are different
            // claims.** A gap anywhere in 1..n averages half of n, so a run clearing a window's
            // worth of gems is dealt at least one and meets about two. The gates refuse on the
            // floor; this is the only place the mean is pinned, and the two being a factor of two
            // apart is exactly why the constant is named for the bound.
            int want = Gems * 2 / SiegeTuning.CharmWithin;

            Assert.Greater(found, want * 3 / 4, $"{found} charms in {Gems} gems against {want}");
            Assert.Less(found, want * 5 / 4, $"{found} charms in {Gems} gems against {want}");

            // **An even share of the roster, with a band round it, and both halves matter.**
            // It read `want / 6` - five sixths of an even share *of five charms*, written as a
            // constant - so appending a sixth made the floor 98% of the new even share and the
            // fixture failed on a sampling wobble rather than on a bias. The share is derived
            // now, and the band is a third either side: at this sample that is four standard
            // deviations of a six-way split, so it cannot flake and still refuses a charm that
            // is dealt half as often as its neighbours.
            //
            // **And a ceiling, which was never here.** A floor alone passes a roll that deals
            // one charm twice as often as the rest, which is the same fault the other way up.
            int even = want / SiegeCharms.Roster.Length;

            for (int i = 0; i < SiegeCharms.Roster.Length; i++)
            {
                var charm = SiegeCharms.Roster[i].Charm;
                counted.TryGetValue(charm, out int seen);

                Assert.Greater(seen, even * 2 / 3,
                               $"{charm} came up {seen} times in {found} charms against an even "
                               + $"share of {even}, so the roll is not choosing evenly between "
                               + "them");

                Assert.Less(seen, even * 3 / 2,
                            $"{charm} came up {seen} times in {found} charms against an even "
                            + $"share of {even}, so the roll is not choosing evenly between them");
            }
        }

        /// <summary>
        /// <b>And the rate holds on the boards the game actually deals, which is a different
        /// question from whether it holds in a loop.</b>
        ///
        /// <para>
        /// <c>ACharmIsRareAndEveryOneOfThemTurnsUp</c> calls <c>Deal</c> back to back on one field,
        /// which feeds the roll a clean walk of the whole stream — and the roll passed that while
        /// being <em>broken</em>. In a run the stream is also drawn from by the shuffle and the
        /// muster, so what reaches the roll is a subsequence, and the first version's "mix" only
        /// relabelled xorshift32's weakest bits: two shipped rungs dealt no charm at all, in any
        /// run, at any rhythm. What found it was somebody playing the game.
        /// </para>
        /// <para>
        /// So this asks the question the way a player does — <b>several different shipped fields,
        /// interleaved with the shuffles a real board performs</b> — and holds every one of them to
        /// the same band. A board that deals none is the failure this exists for, so the floor is
        /// per board and not an average across them.
        /// </para>
        /// </summary>
        [Test]
        public void EveryFieldDealsCharmsAtTheRateTheModeAsksFor()
        {
            // The opening fields of the three shipped siege chapters, which is three unrelated
            // seeds - and an unrelated one of this fixture's own, so the case still means something
            // on a day the content moves.
            string[][] fields =
            {
                Crossed(),
                Quiet(),
                new[] { "rrggbggb", "gbrbrbrb", "bggrgbrr", "rrgbrggb", "rgrbrbgg" },
                new[] { "gbrryrbb", "rygbrrbr", "bbgybggy", "ybygyybb", "gyrbbggr" },
                new[] { "grrgbybg", "brbygrgy", "gbyrbbyr", "rrggrrbr", "rggbbgrg" },
                new[] { "byrrggyr", "ggbyrgby", "ybbgrbgb", "rygbybry", "bgyryrrb" },
            };

            const int Gems = 4000;

            int want = Gems * 2 / SiegeTuning.CharmWithin;
            var faults = new List<string>();

            for (int f = 0; f < fields.Length; f++)
            {
                var board = Board(fields[f]);
                int found = 0;

                for (int i = 0; i < Gems; i++)
                {
                    // **A shuffle between every few deals, because a run has them.** It is what
                    // makes the stream a real board's stream rather than a clean walk of it - and
                    // the broken roll passed a clean walk.
                    if (i % 7 == 0) board.Reshuffle();

                    board.Deal(0, out var charm);
                    if (charm != SiegeCharm.None) found++;
                }

                if (found < want * 3 / 4 || found > want * 5 / 4)
                    faults.Add($"field {f} dealt {found} charm(s) in {Gems} gems against the {want} "
                               + $"{nameof(SiegeTuning.CharmWithin)} asks for");
            }

            Assert.IsEmpty(faults,
                           string.Join("\n", faults)
                           + "\n\nA roll that is right on one field and dead on another is a "
                           + "roll reading bits that are not independent of the letter draw - "
                           + "see `SiegeBoard.Avalanche`.");
        }

        /// <summary>
        /// <b>And the gap before the <em>first</em> charm is bounded too, which is the half the
        /// window did not cover.</b>
        ///
        /// <para>
        /// <c>CharmWithin</c> bounds the gap between one charm and the next; the opening gap was
        /// drawn from that same window, so a run could be a whole window old before it met one.
        /// That is invisible to every fixture above, because all of them walk thousands of gems
        /// off one board and a run is a few hundred — the rate was right and the <em>opening</em>
        /// was empty. Reported twice, at two different rates, as "I never see them"
        /// (<c>SiegeTuning.CharmOpening</c>).
        /// </para>
        /// <para>
        /// <b>Measured per field and not averaged</b>, for <c>EveryFieldDealsCharmsAtTheRateTheMode
        /// AsksFor</c>'s reason: an opening that is short on average and long on one shipped seed is
        /// one board dealing the same nothing to every player who ever opens it, which is exactly
        /// what invariant 37ci is about.
        /// </para>
        /// </summary>
        [Test]
        public void TheFirstCharmOfARunArrivesInsideTheOpeningWindow()
        {
            string[][] fields =
            {
                Crossed(),
                Quiet(),
                new[] { "rrggbggb", "gbrbrbrb", "bggrgbrr", "rrgbrggb", "rgrbrbgg" },
                new[] { "gbrryrbb", "rygbrrbr", "bbgybggy", "ybygyybb", "gyrbbggr" },
                new[] { "grrgbybg", "brbygrgy", "gbyrbbyr", "rrggrrbr", "rggbbgrg" },
                new[] { "byrrggyr", "ggbyrgby", "ybbgrbgb", "rygbybry", "bgyryrrb" },
            };

            int within = SiegeTuning.CharmFirstWithin;
            var faults = new List<string>();

            for (int f = 0; f < fields.Length; f++)
            {
                var board = Board(fields[f]);
                int first = -1;

                // One gem past the bound, so a field that is exactly at it still passes and one
                // that is over it is caught rather than merely not observed.
                for (int i = 0; i <= within; i++)
                {
                    board.Deal(0, out var charm);
                    if (charm == SiegeCharm.None) continue;

                    first = i;
                    break;
                }

                if (first < 0)
                    faults.Add($"field {f} dealt no charm in its first {within} gems");
            }

            Assert.IsEmpty(faults,
                           string.Join(System.Environment.NewLine, faults)
                           + $"{System.Environment.NewLine}{System.Environment.NewLine}The opening gap is bounded at {within} gems "
                           + $"({nameof(SiegeTuning.CharmWithin)} / {nameof(SiegeTuning.CharmOpening)}), so the mechanic is met early in every run - including "
                           + "one short enough that a whole window is longer than the level.");

            // **And it is genuinely narrower than the running window**, or the constant is a
            // second name for a number that changes nothing - which is a check that cannot fail.
            Assert.Less(within, SiegeTuning.CharmWithin,
                        "the opening window is not narrower than the running one, so it bounds "
                        + "nothing the running window did not already bound");
        }

        // ------------------------------------------------------------------ the roster
        /// <summary>
        /// <b>Every charm in the enum is on the roster and the other way round.</b>
        ///
        /// <c>SiegeCharms.Roster</c> is what a body is parsed against, what a chapter's ladder is
        /// cut from and what the art table is keyed on, so a charm added to the enum and forgotten
        /// here is one no level can ever deal — a rule, its picture and its lesson, all shipped and
        /// unreachable, with every gate green. Invariant 40a, which cost this mode two whole raider
        /// kinds.
        /// </summary>
        [Test]
        public void EveryCharmIsOnTheRosterAndEveryRosterLetterIsACharm()
        {
            foreach (SiegeCharm charm in System.Enum.GetValues(typeof(SiegeCharm)))
            {
                if (charm == SiegeCharm.None) continue;

                Assert.AreNotEqual('\0', SiegeCharms.LetterOf(charm),
                                   $"{charm} is a charm no chapter body can name");
            }

            for (int i = 0; i < SiegeCharms.Roster.Length; i++)
            {
                var row = SiegeCharms.Roster[i];

                Assert.AreEqual(row.Charm, SiegeCharms.Named(row.Letter));
                Assert.AreEqual(row.Letter, SiegeCharms.LetterOf(row.Charm));

                Assert.IsTrue(SiegeLayout.Letters.IndexOf(row.Letter) < 0,
                              $"'{row.Letter}' names a charm and a gem colour, so a body would "
                              + "read the same character two ways");
            }

            Assert.AreEqual(SiegeCharms.Roster.Length, SiegeCharms.Letters.Length);
        }

        /// <summary>
        /// <b>A chapter deals the first <em>n</em> charms of the roster</b>, so a player meets one
        /// new one a chapter and never two at once.
        /// </summary>
        [Test]
        public void AChapterDealsOneMoreCharmThanTheOneBeforeIt()
        {
            Assert.AreEqual("", SiegeCharms.Upto(0));
            Assert.AreEqual("p", SiegeCharms.Upto(1));
            Assert.AreEqual("pl", SiegeCharms.Upto(2));
            Assert.AreEqual("pls", SiegeCharms.Upto(3));
            Assert.AreEqual("plsf", SiegeCharms.Upto(4));
            Assert.AreEqual("plsfh", SiegeCharms.Upto(5));
            Assert.AreEqual("plsfha", SiegeCharms.Upto(6));

            // **It runs out, and a fifth chapter has to know that before it is commissioned** —
            // invariant 37br's argument about boss verbs, which is the same argument.
            Assert.AreEqual(SiegeCharms.Letters, SiegeCharms.Upto(SiegeCharms.Roster.Length + 4));
        }

        /// <summary>
        /// <b>A charm letter this build does not know is refused by name rather than ignored.</b>
        ///
        /// Invariant 5f. <c>JsonUtility</c> drops a field it has never heard of without a word, so
        /// the opposite failure — a body naming a charm that has been retired — has to be loud, or
        /// the level deals the charms it recognised and ships a field nobody composed.
        /// </summary>
        [Test]
        public void ACharmLetterThisBuildDoesNotKnowIsRefusedByName()
        {
            Assert.IsTrue(ProtoGrid.TryRead(Quiet(), 5, 3, SiegeLayout.Cells, out var grid,
                                            out string error), error);

            var layout = new SiegeLayout(grid, Gems, Wards, Waves, null, 0, null, 0, "px");

            Assert.IsNotNull(layout.Fault,
                             "a body naming a charm this mode does not have validated and would "
                             + "have shipped dealing only the letters it recognised");

            StringAssert.Contains("charm", layout.Fault);
        }

        /// <summary>
        /// <b>An empty charm field is not a fault</b>, because the opening rung of the first
        /// chapter deals none at all — the one moment a player is still working out what the verb
        /// is (invariant 24).
        /// </summary>
        [Test]
        public void AFieldMayDealNoCharmsAtAll()
        {
            var layout = Layout(Quiet(), "");

            Assert.IsNull(layout.Fault);
            Assert.AreEqual(0, layout.Charms.Length);

            var board = SiegeBoard.Build(layout);
            for (int i = 0; i < 2000; i++)
            {
                board.Deal(0, out var charm);
                Assert.AreEqual(SiegeCharm.None, charm);
            }
        }

        /// <summary>
        /// <b>A letter written twice is one charm, not two.</b> The deal picks uniformly from the
        /// authored array, so keeping the repeat would weight one charm double with nothing in the
        /// file saying so.
        /// </summary>
        [Test]
        public void ACharmNamedTwiceIsStillOneCharm()
        {
            var layout = Layout(Quiet(), "ppl");

            Assert.AreEqual(2, layout.Charms.Length);
            Assert.AreEqual(SiegeCharm.Prism, layout.Charms[0]);
            Assert.AreEqual(SiegeCharm.Lance, layout.Charms[1]);
        }

        /// <summary>
        /// <b>An authored field never opens with a charm on it.</b>
        ///
        /// A field is authored settled and dealt with nothing on it: a charm standing in an
        /// authored cell would be a payoff its author placed rather than one the player was dealt
        /// (invariant 20m), and it would have to be proved settled against the wild's own match
        /// rule as well as against the ordinary one.
        /// </summary>
        [Test]
        public void AnAuthoredFieldOpensWithNoCharmOnIt()
        {
            var board = Board(Quiet());

            for (int i = 0; i < board.Count; i++)
                Assert.AreEqual(SiegeCharm.None, board.CharmAt(i));

            Assert.IsFalse(board.AnyCharm);
        }
    }
}
