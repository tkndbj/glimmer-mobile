using System.Collections.Generic;
using System.Text;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The advert's boards and the model that plays them, held offline.
    ///
    /// A board here passes through no content gate (it is not a chapter, 53d), so this is the
    /// only thing that would ever say it had stopped being a board, that its hand points at a
    /// move the drag refuses, or that the run it records no longer ends the way it was tuned
    /// to. The model played here is the same <c>SiegeShowcase</c> readings
    /// <c>ShowcaseScreen</c>'s hand acts on, at the pace a hand can act on them, so what is
    /// proved is the recording and not a stand-in for it.
    /// </summary>
    public sealed class ShowcaseTests
    {
        const float Frame = 1f / 60f;

        /// <summary>Ten minutes of frames: far longer than any run these boards can produce.</summary>
        const int Patience = 60 * 600;

        /// <summary>
        /// The wall-clock gaps between swaps the slowed hand plays in: its reach, press and
        /// drag (about 1.4 s), the cascade it waits out (half a second to a second and a half),
        /// and the thinking time (<c>ShowcaseScreen.Think</c> / <c>Hurry</c> plus jitter).
        /// Below the band the hand cannot go by its own timings; above it nobody plays.
        /// </summary>
        static readonly float[] Rhythms = { 2.8f, 3.1f, 3.4f, 3.7f, 4.0f, 4.4f };

        /// <summary>The least a recording may last before it ends, either way.</summary>
        const int LastsAtLeast = 70;

        static SiegeBoard Fresh(ShowcaseBoard board) => (SiegeBoard)board.Rules().Fresh();

        // ------------------------------------------------------------------ every board
        [Test]
        public void EveryBoardIsFitToShip()
        {
            foreach (var board in SiegeShowcase.All)
                Assert.IsNull(board.Fault(),
                              $"{board.Id}: the field is read by no content gate, so this is the "
                              + "only thing that would ever say it had stopped being a board");
        }

        [Test]
        public void TheFieldIsAuthoredSettledAndOffersAMove()
        {
            foreach (var preset in SiegeShowcase.All)
            {
                var board = Fresh(preset);

                Assert.AreEqual(0, SiegeLayout.Runs(Cells(board), board.Width, board.Height, null).Count,
                                $"{preset.Id}: a field dealt with a run already on it cascades in the "
                                + "first frame of the recording");

                uint seed = 1u;
                Assert.IsTrue(SiegeShowcase.Aimed(board, ref seed, out int a, out int b),
                              $"{preset.Id}: the field has to offer the hand a first move");
                Assert.IsTrue(board.Adjacent(a, b) && board.Lines(a, b),
                              $"{preset.Id}: the hand would point at a move the drag refuses");
            }
        }

        [Test]
        public void EveryBoardDealsCogsAndEveryCharmAndNoBomber()
        {
            foreach (var preset in SiegeShowcase.All)
            {
                var layout = preset.Layout();

                Assert.Greater(layout.Cogs, 0, $"{preset.Id}: cogs have to be seen");
                Assert.AreEqual(SiegeCharms.Roster.Length, layout.Charms.Length, $"{preset.Id}: every charm");
                Assert.LessOrEqual(layout.RaiderCount, SiegeLayout.MaxRaiders, preset.Id);

                for (int w = 0; w < layout.Coming.Length; w++)
                    for (int i = 0; i < layout.Coming[w].Length; i++)
                        Assert.AreNotEqual(SiegeKind.Bomber, layout.KindAt(w, i),
                                           $"{preset.Id}: no bombers, at the owner's instruction");
            }
        }

        [Test]
        public void EveryRaiderHasATurretThatAnswersIt()
        {
            foreach (var preset in SiegeShowcase.All)
            {
                var layout = preset.Layout();

                for (int w = 0; w < layout.Coming.Length; w++)
                    for (int i = 0; i < layout.Coming[w].Length; i++)
                    {
                        // A boss wears no colour (37dn): every ward reaches it at full weight.
                        if (SiegeTuning.IsBoss(layout.KindAt(w, i))) continue;

                        char wears = SiegeLayout.Letters[layout.ColourAt(w, i)];
                        Assert.Greater(System.Array.IndexOf(layout.Wards, wears), -1,
                                       $"{preset.Id}: a '{wears}' raider walks a hill with no '{wears}' turret");
                    }
            }
        }

        [Test]
        public void EveryLineIsFourDistinctLegendariesAtFiveStars()
        {
            var used = new HashSet<string>();

            foreach (var preset in SiegeShowcase.All)
            {
                var line = preset.LineUp();
                var here = new HashSet<string>();

                for (int i = 0; i < preset.Seats.Length; i++)
                {
                    var (colour, ward) = preset.Seats[i];
                    var model = line[colour];

                    Assert.AreEqual(ward, model.Id, $"{preset.Id}: the '{colour}' seat did not resolve to {ward}");
                    Assert.IsTrue(model.Legendary, $"{preset.Id}: {ward} is not on the legendary band");
                    Assert.AreEqual(WardStars.Most, line.BuildAt(WardLine.Colours.IndexOf(colour)).Stars);
                    Assert.IsTrue(here.Add(ward), $"{preset.Id}: {ward} stands twice");
                }

                foreach (string ward in here)
                    Assert.IsTrue(used.Add(ward),
                                  $"{ward} stands on two boards; the owner asked for four different turrets");
            }
        }

        [Test]
        public void EverySeatsPictureIsAskedOnce()
        {
            foreach (var preset in SiegeShowcase.All)
            {
                var art = preset.LineUp().Art();
                Assert.Greater(art.Count, 0);

                var seen = new HashSet<string>();
                for (int i = 0; i < art.Count; i++)
                    Assert.IsTrue(seen.Add(art[i].Address), $"{preset.Id}: a line asks for one address once");
            }
        }

        // ------------------------------------------------------------------ the boards apart
        [Test]
        public void TheOverrunSendsTheCapAndNoBoss()
        {
            var layout = SiegeShowcase.Overrun.Layout();

            Assert.IsFalse(layout.HasBoss,
                           "a boss walks onto a cleared hill only, and this run ends with the hill full");
            Assert.AreEqual(0, SiegeShowcase.Overrun.BossKinds().Count);
            Assert.AreEqual(SiegeLayout.MaxRaiders, layout.RaiderCount,
                            "as many raiders as a run may hold, because the crowd is the point");

            var kinds = Kinds(layout);
            foreach (var kind in new[] { SiegeKind.Creeper, SiegeKind.Brute, SiegeKind.Bulwark })
                Assert.IsTrue(kinds.Contains(kind), $"the hill never sends a {kind}");
        }

        [Test]
        public void TheWarlordsSendTwoBossesAtTheHeadOfTheFirstWaveWithACrowdBehind()
        {
            var layout = SiegeShowcase.Warlords.Layout();

            Assert.AreEqual(2, layout.BossesIn(0), "two bosses in the first wave");
            Assert.AreEqual(SiegeKind.Overlord, layout.KindAt(0, 0));
            Assert.AreEqual(SiegeKind.Harrower, layout.KindAt(0, 1));
            Assert.Greater(layout.SizeOf(0), 2 + 8, "and the insects walk in behind them");

            for (int i = 2; i < layout.SizeOf(0); i++)
                Assert.IsFalse(SiegeTuning.IsBoss(layout.KindAt(0, i)), "only the two lead");

            for (int w = 1; w < layout.Coming.Length; w++)
                Assert.AreEqual(0, layout.BossesIn(w), "the waves after are crowds");

            CollectionAssert.AreEqual(new[] { SiegeKind.Overlord, SiegeKind.Harrower },
                                      SiegeShowcase.Warlords.BossKinds());
            Assert.AreEqual(SiegeLayout.MaxRaiders, layout.RaiderCount);

            // The board seats them either side of the middle, as it seats an endless pair.
            var board = Fresh(SiegeShowcase.Warlords);
            Run(board, () => board.Raiders.Count >= 2);

            Assert.IsTrue(board.Raiders[0].Boss && board.Raiders[1].Boss, "the pair leads");
            Assert.AreNotEqual(board.Raiders[0].Lane, board.Raiders[1].Lane, "and stands apart");
            Assert.Less(board.Raiders[1].Wait, SiegeTuning.RaiderSpacing * 1.5f,
                        "the second walks in on the first's heels");
        }

        [Test]
        public void ASpecRosterSpellsBackIntoTheGrammar()
        {
            var layout = SiegeShowcase.Warlords.Layout();

            Assert.AreEqual(SiegeShowcase.Warlords.Roster.Length, layout.Waves.Length);
            StringAssert.StartsWith("rb", layout.Waves[0], "a boss is written as the letter it wears");
            StringAssert.Contains("#g", layout.Waves[0], "a bulwark keeps its mark");
            StringAssert.Contains("RG", layout.Waves[0], "a brute keeps its case");

            Assert.Throws<System.ArgumentException>(() => SiegeShowcase.Specs("rg!b"),
                                                    "a bomber token is refused rather than read");
        }

        // ------------------------------------------------------------------ the gift
        [Test]
        public void APlantedCharmNeverLinesUpWhereItStands()
        {
            foreach (var charm in SiegeShowcase.Planted)
                for (uint seed = 1u; seed <= 24u; seed++)
                {
                    var board = Fresh(SiegeShowcase.Overrun);
                    uint s = seed;

                    int cell = SiegeShowcase.Plant(board, charm, ref s);
                    Assert.GreaterOrEqual(cell, 0, $"nowhere to stand a {charm}");
                    Assert.AreEqual(charm, board.CharmAt(cell));

                    var charms = new SiegeCharm[board.Count];
                    for (int i = 0; i < charms.Length; i++) charms[i] = board.CharmAt(i);

                    Assert.IsFalse(SiegeLayout.Lined(Cells(board), board.Width, board.Height, charms, cell),
                                   $"a {charm} stood at {cell} is already three alike, so it would "
                                   + "go off before anybody moved a gem");

                    Assert.IsTrue(SiegeShowcase.Aimed(board, ref s, out int a, out int b));
                    Assert.IsTrue(Springs(board, a, b),
                                  $"the move after standing a {charm} at {cell} does not spring it "
                                  + $"(chose {a}<->{b})");
                }
        }

        [Test]
        public void ASecondCharmIsRefusedWhileOneStands()
        {
            var board = Fresh(SiegeShowcase.Overrun);
            uint seed = 3u;

            Assert.GreaterOrEqual(SiegeShowcase.Plant(board, SiegeCharm.Lance, ref seed), 0);
            Assert.AreEqual(-1, SiegeShowcase.Plant(board, SiegeCharm.Storm, ref seed),
                            "two hand-stood charms on one field is a field nobody composed");
        }

        [Test]
        public void TheScheduleStandsEveryCharmOnceInOrder()
        {
            int planted = 0;
            var order = new List<SiegeCharm>();

            for (int match = 0; match < 40; match++)
            {
                var due = SiegeShowcase.Due(match, planted);
                if (due == SiegeCharm.None) continue;

                order.Add(due);
                planted++;
            }

            CollectionAssert.AreEqual(SiegeShowcase.Planted, order);
            Assert.AreEqual(SiegeCharm.None, SiegeShowcase.Due(1000, planted), "and then no more");
        }

        // ------------------------------------------------------------------ the runs
        /// <summary>
        /// Custom, played at every pace a hand plays it, has to be <b>lost</b> — to the crowd,
        /// on the last wave, late — and has to have met everything it was built to show.
        ///
        /// <b>The table is printed on a pass</b>: <c>Tough</c> is the dial and the seconds
        /// column is what it moves, and it is chaotic (a step of twenty moves which wave the
        /// line falls on), so retune by reading rather than by arithmetic.
        /// </summary>
        [Test]
        public void TheOverrunIsLostToTheLastWaveAtEveryRhythm()
        {
            var preset = SiegeShowcase.Overrun;
            var table = new StringBuilder();
            var wrong = new List<string>();

            foreach (float rhythm in Rhythms)
            {
                var board = Fresh(preset);
                var log = Play(board, rhythm);
                bool overrun = board.WardsStanding == 0;

                table.AppendLine(Row(rhythm, board, log, overrun ? "OVERRUN" : board.IsFinished ? "won" : "open"));

                if (!overrun) wrong.Add($"{rhythm:0.0}s ({board.WardsStanding} standing, finished={board.IsFinished})");
                else if (board.Wave < preset.Waves.Length) wrong.Add($"{rhythm:0.0}s (lost on wave {board.Wave}, before the crowd)");
                else if (log.Seconds < LastsAtLeast) wrong.Add($"{rhythm:0.0}s (lost in {log.Seconds}s, too soon)");

                Shown(rhythm, board, log, wrong);
            }

            System.Console.WriteLine("custom (overrun):");
            System.Console.Write(table.ToString());

            Assert.IsEmpty(wrong, "the recording has to be overrun by the last wave, late, at "
                                  + "every rhythm; wrong at " + string.Join(", ", wrong.ToArray()));
        }

        /// <summary>
        /// Custom 2, played at every pace a hand plays it, has to be <b>won</b> with every
        /// turret standing, both bosses felled, and has to have met everything it was built to
        /// show. The table is printed on a pass, for the reason above.
        /// </summary>
        [Test]
        public void TheWarlordsAreFelledAndTheLineHoldsAtEveryRhythm()
        {
            var preset = SiegeShowcase.Warlords;
            var table = new StringBuilder();
            var wrong = new List<string>();

            foreach (float rhythm in Rhythms)
            {
                var board = Fresh(preset);
                var log = Play(board, rhythm);
                bool won = board.IsFinished && board.WardsStanding == board.Wards.Count;

                table.AppendLine(Row(rhythm, board, log, won ? "WON" : board.IsFinished ? "won, hurt" : board.WardsStanding == 0 ? "lost" : "open")
                                 + $"  bosses felled {board.Attention.BossesFelled} at {log.BossesDownAt}s");

                if (!won) wrong.Add($"{rhythm:0.0}s ({board.WardsStanding} standing, finished={board.IsFinished})");
                else if (board.Attention.BossesFelled < 2) wrong.Add($"{rhythm:0.0}s (only {board.Attention.BossesFelled} bosses felled)");
                else if (log.Seconds < LastsAtLeast) wrong.Add($"{rhythm:0.0}s (won in {log.Seconds}s, too soon)");

                Shown(rhythm, board, log, wrong);
            }

            System.Console.WriteLine("custom 2 (warlords):");
            System.Console.Write(table.ToString());

            Assert.IsEmpty(wrong, "the recording has to be won with both bosses felled and every "
                                  + "turret standing, at every rhythm; wrong at "
                                  + string.Join(", ", wrong.ToArray()));
        }

        static string Row(float rhythm, SiegeBoard board, Log log, string ending)
            => $"  rhythm {rhythm:0.0}s  {log.Seconds,4}s  {log.Matches,3} swaps  "
             + $"wards {board.WardsStanding}/{board.Wards.Count}  wave {board.Wave}  "
             + $"felled {board.Attention.RaidersFelled}/{SiegeLayout.MaxRaiders}  "
             + $"sprung {board.Attention.CharmsSprung}  cogs {board.Attention.CogsTaken}  "
             + $"thrown {log.Thrown}  {ending}";

        /// <summary>What every recording has to have shown, whichever way it ends.</summary>
        static void Shown(float rhythm, SiegeBoard board, Log log, List<string> wrong)
        {
            if (log.Planted < SiegeShowcase.Planted.Length)
                wrong.Add($"{rhythm:0.0}s (only {log.Planted} charms stood: the run ended too soon)");
            if (board.Attention.CharmsSprung < SiegeShowcase.Planted.Length)
                wrong.Add($"{rhythm:0.0}s (a stood charm was never sprung)");
            if (board.Attention.CogsTaken == 0) wrong.Add($"{rhythm:0.0}s (no cog picked up)");
            if (board.Attention.BombsDropped > 0) wrong.Add($"{rhythm:0.0}s (a bomb was dropped)");
            if (log.Thrown == 0) wrong.Add($"{rhythm:0.0}s (no overcharge thrown)");
        }

        // ------------------------------------------------------------------ driving
        struct Log
        {
            public int Matches, Planted, Thrown, Seconds, BossesDownAt;
        }

        /// <summary>
        /// The hand's loop, on the model's clock: what <c>ShowcaseScreen.Direct</c> does, with
        /// the animation waits collapsed into the rhythm.
        /// </summary>
        static Log Play(SiegeBoard board, float rhythm)
        {
            var log = new Log();
            uint seed = 0x5EED2026u;
            float since = rhythm, clock = 0f;
            bool met = false;

            for (int i = 0; i < Patience; i++)
            {
                board.Advance(Frame);
                clock += Frame;

                if (board.IsFinished || board.WardsStanding == 0) break;

                if (board.BossStanding) met = true;
                else if (met && log.BossesDownAt == 0) log.BossesDownAt = (int)clock;

                // **Paid only when the tap landed.** A reading that says yes to a tap the board
                // refuses is a hand tapping the same thing every frame; charging the rhythm for
                // it hid exactly that fault once (a bomb under a boss on its floor), so a refusal
                // here is asserted rather than absorbed.
                int buried = SiegeShowcase.Buried(board);
                if (buried >= 0)
                {
                    Assert.IsTrue(board.Dig(buried), "the model dug a turret that was not buried");
                    while (board.Dig(buried)) { }
                    since -= .5f;
                }

                int cog = SiegeShowcase.Loot(board);
                if (cog >= 0)
                {
                    Assert.IsTrue(board.Take(cog).Landed, "the model reached for a cog the board refused");
                    since -= .3f;
                }

                int bomb = SiegeShowcase.Fuse(board);
                if (bomb >= 0)
                {
                    Assert.Greater(board.Detonate(bomb, null), 0, "the model tapped a bomb the board refused");
                    since -= .3f;
                }

                int armed = SiegeShowcase.Armed(board);
                if (armed >= 0)
                {
                    Assert.IsTrue(board.Overcharge(armed, null).Landed, "the model threw a charge the board refused");
                    log.Thrown++;
                    since -= .3f;
                }

                since += Frame;

                // The hand quickens for a boss, by the screen's own rule (`ShowcaseScreen.Pace`).
                float beat = board.BossStanding ? rhythm * (ShowcaseScreen.Hurry / ShowcaseScreen.Think)
                                                : rhythm;
                if (since < beat) continue;

                if (!SiegeShowcase.Aimed(board, ref seed, out int a, out int b)) continue;

                Assert.IsNotNull(board.Swap(a, b), "the model chose a move the board refused");
                log.Matches++;
                since = 0f;

                var due = SiegeShowcase.Due(log.Matches, log.Planted);
                if (due != SiegeCharm.None && SiegeShowcase.Plant(board, due, ref seed) >= 0) log.Planted++;
            }

            log.Seconds = (int)clock;
            return log;
        }

        static int Run(SiegeBoard board, System.Func<bool> until)
        {
            for (int i = 1; i <= Patience; i++)
            {
                board.Advance(Frame);
                if (until()) return i;
            }

            return 0;
        }

        static HashSet<SiegeKind> Kinds(SiegeLayout layout)
        {
            var kinds = new HashSet<SiegeKind>();
            for (int w = 0; w < layout.Coming.Length; w++)
                for (int i = 0; i < layout.Coming[w].Length; i++)
                    kinds.Add(layout.KindAt(w, i));
            return kinds;
        }

        static bool Springs(SiegeBoard board, int a, int b)
        {
            var cells = Cells(board);
            var charms = new SiegeCharm[board.Count];
            for (int i = 0; i < charms.Length; i++) charms[i] = board.CharmAt(i);

            (cells[a], cells[b]) = (cells[b], cells[a]);
            (charms[a], charms[b]) = (charms[b], charms[a]);

            var hit = SiegeLayout.Runs(cells, board.Width, board.Height, charms);

            foreach (int cell in hit)
                if (charms[cell] != SiegeCharm.None) return true;

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
