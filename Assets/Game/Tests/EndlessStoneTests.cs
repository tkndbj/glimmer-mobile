using System.IO;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The two stones on the Infinite lane (2026-10-03): the cursed stone from wave
    /// <see cref="SiegeEndless.CursedFrom"/> and the void stone from <see cref="SiegeEndless.VoidFrom"/>,
    /// on exactly the rules, rates and drawing the chapters deal them with.
    ///
    /// <para>
    /// <b>Four promises, and every offline content gate is blind to all of them</b>, because the
    /// body carries two booleans and the rest is arithmetic on the board. The lane deals neither
    /// stone before its wave, and deals there exactly the field it dealt before the stones came
    /// (invariant 41); from its wave it deals each at the chapters' own rate; an authored rung
    /// still deals them from its first refill; and on the lane's own field the stones are met,
    /// matched and broken in play, and a field they leave with no move is dealt again.
    /// </para>
    /// </summary>
    public sealed class EndlessStoneTests
    {
        /// <summary>A settled field with no three alike anywhere (<c>SiegeObsidianTests</c>' own).</summary>
        static readonly string[] Field =
        {
            "rgbyrgby",
            "gbyrgbyr",
            "byrgbyrg",
            "yrgbyrgb",
            "rgbyrgby",
        };

        static ProtoGrid Grid(string[] rows)
        {
            Assert.IsTrue(ProtoGrid.TryRead(rows, rows[0].Length, rows.Length, SiegeLayout.Cells,
                                            out var grid, out string error), error);
            return grid;
        }

        static SiegeLayout Lane(bool stones, string[] rows = null)
        {
            var layout = new SiegeLayout(Grid(rows ?? Field), "rgby", "rgby", null, null, 30,
                                         new SiegeEndless("rgby", 30, 30, .55f), 0, "plsfha",
                                         stones, stones);
            Assert.IsNull(layout.Fault, layout.Fault);
            return layout;
        }

        /// <summary>
        /// A board on the lane with wave <paramref name="wave"/> standing on the hill: opened at
        /// that checkpoint and walked through its count-in until the wave has mustered, which is
        /// how a real run reaches it.
        /// </summary>
        static SiegeBoard OnWave(SiegeLayout layout, int wave)
        {
            var board = SiegeBoard.Build(layout, null, new SiegeStart(wave, 0));

            for (int frame = 0; frame < 60 * 30 && board.Wave < wave; frame++)
                board.Advance(1f / 60f);

            Assert.AreEqual(wave, board.Wave, "the checkpoint's wave never mustered");
            Assert.IsFalse(board.IsFinished, "the run ended in its own count-in");
            return board;
        }

        // ------------------------------------------------------------------ the gate
        /// <summary>
        /// **The gate is the wave on the lane and nothing on a rung**: an authored cursed field
        /// deals from its first refill, as it always did, and a lane with no stones deals none
        /// at any depth.
        /// </summary>
        [Test]
        public void TheGateIsTheWaveOnTheLaneAndNothingOnARung()
        {
            var lane = Lane(true);
            Assert.IsTrue(lane.Cursed && lane.Singular, "the lane's flags did not reach the layout");

            Assert.IsFalse(lane.CursesOn(SiegeEndless.CursedFrom - 1));
            Assert.IsTrue(lane.CursesOn(SiegeEndless.CursedFrom));
            Assert.IsFalse(lane.VoidsOn(SiegeEndless.VoidFrom - 1));
            Assert.IsTrue(lane.VoidsOn(SiegeEndless.VoidFrom));
            Assert.Less(SiegeEndless.CursedFrom, SiegeEndless.VoidFrom,
                        "the void stone came before the curse it is the sequel to");

            var plain = Lane(false);
            Assert.IsFalse(plain.CursesOn(1000) || plain.VoidsOn(1000),
                           "a lane that authors no stones dealt one");

            var rung = new SiegeLayout(Grid(Field), "rgby", "rgby", new[] { "rgbyrgby" }, null,
                                       0, null, 0, null, true, true);
            Assert.IsTrue(rung.CursesOn(0) && rung.VoidsOn(0),
                          "an authored rung stopped dealing its stones from the first refill");
        }

        // ------------------------------------------------------------------ the deal
        /// <summary>
        /// **Before its wave the lane deals the field it always dealt** (invariant 41): every gem
        /// a stoned lane deals on the wave before the curse is the gem a plain lane deals from
        /// the same draw, and none of them is a stone. And on the waves between the two, every
        /// deal is the plain gem or an obsidian in its place, never a void stone.
        /// </summary>
        [Test]
        public void BeforeItsWaveTheLaneDealsTheFieldItAlwaysDealt()
        {
            var stoned = OnWave(Lane(true), SiegeEndless.CursedFrom - 1);
            var plain = OnWave(Lane(false), SiegeEndless.CursedFrom - 1);

            for (int i = 0; i < 4000; i++)
            {
                char a = plain.Deal(9, out var charmA);
                char b = stoned.Deal(9, out var charmB);

                Assert.AreEqual(a, b, $"draw {i}: wave {stoned.Wave} dealt a different gem with the "
                                      + "stones authored, which is a field re-rolled before they came");
                Assert.AreEqual(charmA, charmB, $"draw {i}: the charm moved with the stones authored");
            }

            stoned = OnWave(Lane(true), SiegeEndless.VoidFrom - 1);
            plain = OnWave(Lane(false), SiegeEndless.VoidFrom - 1);

            int curses = 0;
            for (int i = 0; i < 4000; i++)
            {
                char a = plain.Deal(9, out _);
                char b = stoned.Deal(9, out _);

                Assert.AreNotEqual(SiegeLayout.Singularity, b,
                                   $"draw {i}: a void stone on wave {stoned.Wave}");

                if (b == SiegeLayout.Obsidian) { curses++; continue; }
                Assert.AreEqual(a, b, $"draw {i}: a stone cost the lane an extra draw");
            }

            Assert.Greater(curses, 0, $"no curse in 4000 deals on wave {stoned.Wave}");
        }

        /// <summary>
        /// **From its wave each stone is dealt at the chapters' own rate** - the same roll, the
        /// same constant, so "not too often" is the figure Cogspire and Neonhaven were tuned on
        /// (<see cref="SiegeTuning.ObsidianPercent"/>, <see cref="SiegeTuning.SingularityPermille"/>)
        /// rather than a second one the lane could drift to.
        /// </summary>
        [Test]
        public void FromItsWaveEachStoneIsDealtAtTheChaptersRate()
        {
            var board = OnWave(Lane(true), SiegeEndless.VoidFrom);

            const int Deals = 40000;
            int curses = 0, voids = 0;

            for (int i = 0; i < Deals; i++)
            {
                char c = board.Deal(9, out _);
                if (c == SiegeLayout.Obsidian) curses++;
                if (c == SiegeLayout.Singularity) voids++;
            }

            // The void roll is only taken where the curse roll and the charm left the cell alone,
            // so its share is the permille of what is left.
            double curseRate = SiegeTuning.ObsidianPercent / 100.0;
            double voidRate = (1 - curseRate) * SiegeTuning.SingularityPermille / 1000.0;

            Assert.AreEqual(curseRate, (double)curses / Deals, curseRate * .2,
                            "the lane deals the curse at a rate of its own");
            Assert.AreEqual(voidRate, (double)voids / Deals, voidRate * .25,
                            "the lane deals the void stone at a rate of its own");
        }

        // ------------------------------------------------------------------ in play
        /// <summary>
        /// **On the lane's own field the stones are clutter that can be cleared, and never a
        /// locked board.** Played on the shipped field past the void stone's wave by a player
        /// who takes whatever swap is offered, which never lines stones up on purpose - so what
        /// it meets is the floor of how often they break. Over every turn: the field at rest
        /// never fills with stones, curses break and the void collapses in play, and no turn ends
        /// on a field with no move (the shuffle; its drawing is <c>SiegeShuffleTests</c>').
        /// </summary>
        [Test]
        public void OnTheLanesFieldTheStonesAreMatchedAndNeverLockTheBoard()
        {
            var layout = Lane(true, ShippedRows());
            var pick = new System.Random(20261003);

            int turns = 0, breaks = 0, collapses = 0, shuffles = 0, most = 0;
            long resting = 0;

            for (int run = 0; run < 40; run++)
            {
                var board = OnWave(layout, SiegeEndless.VoidFrom);
                int n = board.Width * board.Height;

                for (int t = 0; t < 150; t++)
                {
                    var swap = board.FindSwap(pick.Next(n * 2));
                    Assert.IsTrue(swap.Found, $"run {run}, turn {t}: a field with no move was left standing");

                    var turn = board.Swap(swap.A, swap.B);
                    Assert.IsNotNull(turn, "the board refused a swap it had offered");
                    turns++;

                    foreach (var beat in turn.Beats)
                    {
                        if (beat.Breakers.Count > 0) breaks++;
                        if (beat.Eaters.Count > 0) collapses++;
                    }

                    if (turn.Shuffled != null) shuffles++;

                    int stones = 0;
                    for (int i = 0; i < n; i++)
                        if (SiegeLayout.IsStone(board.At(i))) stones++;

                    resting += stones;
                    if (stones > most) most = stones;

                    Assert.IsTrue(board.AnySwap(), $"run {run}, turn {t}: a turn ended on a dead field");
                }
            }

            double mean = (double)resting / turns;

            System.Console.WriteLine($"{turns} turns on wave {SiegeEndless.VoidFrom}: {mean:0.00} stones "
                                  + $"at rest (most {most} of 40), {breaks} curses broken, "
                                  + $"{collapses} collapses, {shuffles} fields dealt again");

            Assert.Greater(breaks, 0, "no curse was ever broken on the lane's field");
            Assert.Greater(collapses, 0, "no void stone ever collapsed on the lane's field");
            Assert.LessOrEqual(mean, 8.0, "stones crowd the lane's field at rest");
        }

        /// <summary>
        /// **The shipped body authors both stones** - the flags are the whole of *whether* (the
        /// lane's waves are *when*), and `JsonUtility` reads anything that is not `true` as
        /// false, so a body that lost them would ship a lane that never deals one with every
        /// other gate green. Read off the file, and its field handed back for the play above.
        /// </summary>
        [Test]
        public void TheShippedLaneAuthorsBothStones()
        {
            var siege = ShippedBlock();

            Assert.AreEqual(true, siege.TryGetValue("obsidian", out var curse) ? curse : null,
                            "s02_endlesswatch.json does not author the cursed stone");
            Assert.AreEqual(true, siege.TryGetValue("singularity", out var hole) ? hole : null,
                            "s02_endlesswatch.json does not author the void stone");
        }

        static System.Collections.Generic.Dictionary<string, object> ShippedBlock()
        {
            string path = Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets", "Content",
                                       "chapters", "s02_endlesswatch.json");
            var body = TestJson.Object(TestJson.Parse(File.ReadAllText(path)));
            var level = TestJson.Object(TestJson.Children(body, "levels")[0]);
            return TestJson.Child(level, "siege");
        }

        static string[] ShippedRows()
        {
            var rows = TestJson.Children(ShippedBlock(), "rows");
            var made = new string[rows.Count];
            for (int i = 0; i < made.Length; i++) made[i] = (string)rows[i];
            return made;
        }
    }
}
