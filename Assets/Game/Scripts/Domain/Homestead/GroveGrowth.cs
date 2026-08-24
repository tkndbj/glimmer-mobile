using System;
using System.Collections.Generic;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// The order newly bought ground arrives in, and how long the whole of it takes.
    ///
    /// <para>
    /// <b>Land is the most expensive thing in the grove, so it has to arrive as an event
    /// rather than as a screen that is already different.</b> Everything else the player buys
    /// is an object they then place; a region is the ground itself, and the only way to make
    /// that felt is to let them watch it happen. What that needs from arithmetic is an
    /// <em>order</em>: tiles that appear all at once are a rectangle switching on, and tiles
    /// that appear in reading order are a spreadsheet filling in. Ground has to grow
    /// <em>out of</em> the grove the player already had.
    /// </para>
    /// <para>
    /// So the order is a breadth-first walk seeded from whichever tiles of the new region
    /// touch land already owned — a tile's <see cref="Rings"/> entry is how many steps it is
    /// from the old grove, and everything about the ceremony is a function of that one number:
    /// when the tile rises, whether that ring makes a sound, and what pitch it makes it at.
    /// </para>
    /// <para>
    /// <b>It is in Domain and it is tested, for <c>TweenCycle</c>'s reason.</b> This is
    /// animation arithmetic, which is the one kind of failure that is invisible in a
    /// screenshot and obvious only in motion — a wave that runs backwards, a region that takes
    /// nine seconds, a hundred sounds fired inside one. None of that can be caught by
    /// compiling, and the Editor is often not running. Holding no Unity types is what lets the
    /// whole rule be run offline.
    /// </para>
    /// </summary>
    public static class GroveGrowth
    {
        /// <summary>
        /// Seconds between one ring of tiles rising and the next, before it is compressed to
        /// fit <see cref="MaxSpread"/>.
        /// </summary>
        public const float RingGap = .085f;

        /// <summary>How long one tile takes to travel from below the floor into its place.</summary>
        public const float RiseSeconds = .42f;

        /// <summary>
        /// The longest the whole wave may take, from the first tile moving to the last one
        /// settling.
        ///
        /// <para>
        /// <b>A ceiling rather than a duration, and that is what keeps the size of a region a
        /// content decision rather than a pacing one.</b> A shallow 6x4 bought along its own
        /// edge is four rings deep; a 4x4 corner reached diagonally is seven. At a fixed gap
        /// those are two noticeably different ceremonies and the longer one is the one nobody
        /// asked for — an unlock that outstays its welcome is the mistake a victory panel
        /// makes when it has to be waited out. So the gap gives way rather than the total,
        /// which means a drop that adds a 12x12 region cannot silently ship a six-second
        /// interruption.
        /// </para>
        /// </summary>
        public const float MaxSpread = 1.10f;

        /// <summary>
        /// How many rings may make a sound, however many rings there are.
        ///
        /// <para>
        /// <c>Roll.Ticks</c>'s bargain and for exactly its reason: one thud per tile is not a
        /// flourish, it is a machine gun, and on a phone speaker it clips. A fixed budget
        /// spread across the wave keeps the rhythm of the moment identical whether the player
        /// bought sixteen tiles or thirty-six — which also means re-drawing the map of what is
        /// for sale can never change how an unlock sounds.
        /// </para>
        /// </summary>
        public const int MaxVoices = 6;

        /// <summary>Lowest and highest pitch the ground lands at, first ring to last.</summary>
        public const float LowPitch = 1f, HighPitch = 1.38f;

        /// <summary>
        /// How far into the new ground each tile is, in steps out from land already owned.
        ///
        /// <para>
        /// Index <c>i</c> is the tile at column <c>region.Col + i % region.Cols</c> and row
        /// <c>region.Row + i / region.Cols</c> — row-major within the region, which is the one
        /// ordering a caller can reproduce without being handed a second array.
        /// </para>
        /// <para>
        /// <paramref name="ownedBefore"/> answers for the floor as it stood <em>before</em>
        /// this purchase. Tiles inside the region are ignored whatever it says, so a caller
        /// that forgets to exclude them — the easy mistake, since the purchase has already
        /// been recorded by the time anything animates — gets the same answer rather than a
        /// wave with no shape at all.
        /// </para>
        /// <para>
        /// <b>The fallback is not a defensive branch, it is the shipped floor.</b> Regions
        /// meet at corners: <c>dusk_field</c> touches the starter <c>hearthstead</c> only
        /// diagonally, so a player who owns nothing else buys ground with no edge against
        /// anything they hold. Rather than let the wave start nowhere it starts at the single
        /// tile nearest <paramref name="towardCol"/>/<paramref name="towardRow"/> — the hall —
        /// which is the corner somebody looking at their grove would expect it to grow from.
        /// </para>
        /// </summary>
        public static int[] Rings(GroveRegion region, Func<int, int, bool> ownedBefore,
                                  int towardCol, int towardRow)
        {
            if (region == null || !region.IsValid) return Array.Empty<int>();

            int cols = region.Cols, rows = region.Rows;

            var rings = new int[cols * rows];
            for (int i = 0; i < rings.Length; i++) rings[i] = -1;

            var queue = new Queue<int>(rings.Length);

            // Seed: every tile of the region with an edge against ground the player already
            // held. Four-neighbour rather than eight, because a diagonal touch is not a place
            // ground could plausibly spread across — and counting one would make the corner
            // case below unreachable, which is to say untested rather than impossible.
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (!Adjoins(region, ownedBefore, region.Col + c, region.Row + r)) continue;

                    rings[r * cols + c] = 0;
                    queue.Enqueue(r * cols + c);
                }

            if (queue.Count == 0)
            {
                int seed = Nearest(region, towardCol, towardRow);
                rings[seed] = 0;
                queue.Enqueue(seed);
            }

            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                int c = at % cols, r = at / cols;
                int next = rings[at] + 1;

                Step(rings, queue, cols, rows, c - 1, r, next);
                Step(rings, queue, cols, rows, c + 1, r, next);
                Step(rings, queue, cols, rows, c, r - 1, next);
                Step(rings, queue, cols, rows, c, r + 1, next);
            }

            return rings;
        }

        static void Step(int[] rings, Queue<int> queue, int cols, int rows, int c, int r, int ring)
        {
            if (c < 0 || r < 0 || c >= cols || r >= rows) return;

            int at = r * cols + c;
            if (rings[at] >= 0) return;

            rings[at] = ring;
            queue.Enqueue(at);
        }

        static bool Adjoins(GroveRegion region, Func<int, int, bool> ownedBefore, int col, int row)
            => Held(region, ownedBefore, col - 1, row)
            || Held(region, ownedBefore, col + 1, row)
            || Held(region, ownedBefore, col, row - 1)
            || Held(region, ownedBefore, col, row + 1);

        static bool Held(GroveRegion region, Func<int, int, bool> ownedBefore, int col, int row)
            => ownedBefore != null && col >= 0 && row >= 0
            && !region.Holds(col, row) && ownedBefore(col, row);

        /// <summary>The region tile closest to a point on the floor, as an index into the ring array.</summary>
        static int Nearest(GroveRegion region, int towardCol, int towardRow)
        {
            int best = 0, bestDistance = int.MaxValue;

            for (int r = 0; r < region.Rows; r++)
                for (int c = 0; c < region.Cols; c++)
                {
                    int distance = Math.Abs(region.Col + c - towardCol)
                                 + Math.Abs(region.Row + r - towardRow);

                    if (distance >= bestDistance) continue;

                    bestDistance = distance;
                    best = r * region.Cols + c;
                }

            return best;
        }

        /// <summary>How many rings deep a wave is. Zero for a region with no tiles.</summary>
        public static int RingCount(int[] rings)
        {
            if (rings == null || rings.Length == 0) return 0;

            int deepest = 0;
            foreach (int ring in rings)
                if (ring > deepest) deepest = ring;

            return deepest + 1;
        }

        /// <summary>
        /// The gap actually used between two rings, compressed so the whole wave fits inside
        /// <see cref="MaxSpread"/>. See there for why the gap gives way rather than the total.
        /// </summary>
        public static float GapFor(int ringCount)
        {
            if (ringCount <= 1) return 0f;

            float budget = Math.Max(0f, MaxSpread - RiseSeconds) / (ringCount - 1);
            return Math.Min(RingGap, budget);
        }

        /// <summary>How long after the wave starts a tile in this ring begins to rise.</summary>
        public static float DelayOf(int ring, int ringCount)
            => ring <= 0 ? 0f : ring * GapFor(ringCount);

        /// <summary>
        /// The whole wave, from the first tile moving to the last one settling. Never longer
        /// than <see cref="MaxSpread"/>.
        /// </summary>
        public static float Spread(int ringCount)
            => ringCount <= 0 ? 0f : DelayOf(ringCount - 1, ringCount) + RiseSeconds;

        /// <summary>
        /// Whether this ring is one of the ones that makes a sound. The first ring always
        /// does — that sound is what says the ground has started arriving.
        /// </summary>
        public static bool Speaks(int ring, int ringCount)
        {
            if (ring < 0 || ringCount <= 0 || ring >= ringCount) return false;
            if (ring == 0) return true;
            if (ringCount <= MaxVoices) return true;

            return ring * MaxVoices / ringCount != (ring - 1) * MaxVoices / ringCount;
        }

        /// <summary>
        /// What that sound is pitched at: rising with depth, so a wave crossing a wide region
        /// climbs rather than repeating. It is the part of the ceremony a player will remember
        /// hearing rather than seeing.
        /// </summary>
        public static float Pitch(int ring, int ringCount)
        {
            if (ringCount <= 1) return LowPitch;

            float t = Math.Min(1f, Math.Max(0f, ring / (ringCount - 1f)));
            return LowPitch + (HighPitch - LowPitch) * t;
        }
    }
}
