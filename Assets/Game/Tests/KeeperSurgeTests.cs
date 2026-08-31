using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Where a bloom's light goes, and the two things about that walk which are correctness
    /// rules rather than taste.
    ///
    /// <para>
    /// It travels <b>seams</b>, so a grove built out of like-against-like stays dark however
    /// much of it there is — which is the mode's own rule drawn as light. And it is
    /// <b>bounded</b>, because the board is latched while a planting plays.
    /// </para>
    /// </summary>
    public sealed class KeeperSurgeTests
    {
        static KeeperLayout Grove(params string[] rows)
        {
            int h = rows.Length, w = rows[0].Length;

            Assert.IsTrue(KeeperDeal.TryParse("RGB", out var deal, out string dealError),
                          dealError);
            Assert.IsTrue(KeeperLayout.TryReadRows(rows, w, h, out var ground, out var wants,
                                                   out var sprigs, out string error), error);

            return new KeeperLayout(w, h, ground, wants, sprigs, deal);
        }

        static List<KeeperHop> Walk(KeeperBoard board, params int[] from)
        {
            var hops = new List<KeeperHop>();
            KeeperSurge.Walk(board, from, hops);
            return hops;
        }

        [Test]
        public void LightCrossesASeamAndNeverTwoTilesOfOneColour()
        {
            // A row of R G R B: every neighbouring pair is unlike, so the light walks the lot.
            var board = new KeeperBoard(Grove("RGRB",
                                              "....",
                                              "....",
                                              "...."));

            var hops = Walk(board, 0);

            Assert.AreEqual(3, hops.Count, "the light stopped short of a seam it should cross");
            Assert.AreEqual(3, KeeperSurge.Rings(hops));

            // And the same row in one colour is a grove with no seams in it at all.
            var flat = new KeeperBoard(Grove("RRRB",
                                             "....",
                                             "....",
                                             "...."));

            Assert.AreEqual(0, Walk(flat, 0).Count,
                            "light crossed between two tiles of the same colour");
        }

        [Test]
        public void ACellIsReachedOnceAndByItsNearestBloom()
        {
            // A ring of four unlike tiles: walking from one corner, the far corner is two hops
            // away by either side, and it must be lit once rather than flared twice half a beat
            // apart, which reads as a stutter rather than as light spreading.
            var board = new KeeperBoard(Grove("RG..",
                                              "BR..",
                                              "....",
                                              "...."));

            var hops = Walk(board, 0);

            Assert.AreEqual(3, hops.Count);

            var reached = new HashSet<int>();
            foreach (var hop in hops)
                Assert.IsTrue(reached.Add(hop.To), "cell " + hop.To + " was lit twice");

            Assert.IsFalse(reached.Contains(0), "the source was walked back into");
        }

        [Test]
        public void RingsOnlyEverClimb()
        {
            var board = new KeeperBoard(Grove("RGRG",
                                              "GRGR",
                                              "RGRG",
                                              "GRGR"));

            var hops = Walk(board, 0);

            int last = 0;
            foreach (var hop in hops)
            {
                Assert.GreaterOrEqual(hop.Ring, last, "the walk went backwards");
                Assert.GreaterOrEqual(hop.Ring, 1);
                last = hop.Ring;
            }

            Assert.AreEqual(last, KeeperSurge.Rings(hops));
        }

        [Test]
        public void TheWalkIsBoundedByRingsRatherThanByTheGrove()
        {
            // A nine by nine chequerboard is wired end to end, so an unbounded walk would light
            // eighty tiles. The board is latched while a planting plays, so it may not.
            var rows = new string[9];
            for (int y = 0; y < 9; y++)
            {
                var row = new char[9];
                for (int x = 0; x < 9; x++) row[x] = (x + y) % 2 == 0 ? 'R' : 'G';
                rows[y] = new string(row);
            }

            var board = new KeeperBoard(Grove(rows));
            var hops = Walk(board, 0);

            Assert.LessOrEqual(KeeperSurge.Rings(hops), KeeperSurge.MaxRings);
            Assert.Greater(hops.Count, 0);
        }

        [Test]
        public void AWalkFromNothingIsNothing()
        {
            var board = new KeeperBoard(Grove("RG..",
                                              "....",
                                              "....",
                                              "...."));

            var hops = new List<KeeperHop>();

            KeeperSurge.Walk(board, new int[0], hops);
            Assert.AreEqual(0, hops.Count);

            KeeperSurge.Walk(board, null, hops);
            Assert.AreEqual(0, hops.Count);

            // Bare ground handed in as a source is skipped rather than thrown on: a celebration
            // that took the run down with it would be far worse than one that drew nothing.
            KeeperSurge.Walk(board, new[] { 5, -1, 999 }, hops);
            Assert.AreEqual(0, hops.Count);
        }

        [Test]
        public void EveryHopIsAnEdgeOfTheGrid()
        {
            var board = new KeeperBoard(Grove("RGB.",
                                              "GBR.",
                                              "BRG.",
                                              "...."));

            int width = board.Width;

            foreach (var hop in Walk(board, 0))
            {
                int dx = hop.To % width - hop.From % width;
                int dy = hop.To / width - hop.From / width;

                Assert.AreEqual(1, System.Math.Abs(dx) + System.Math.Abs(dy),
                                "a hop crossed something that is not one edge");
                Assert.AreNotEqual(board.At(hop.From), board.At(hop.To));
            }
        }
    }
}
