using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// One hop of a surge: light crossing a single seam, and the ring it was reached on.
    ///
    /// <para>
    /// A hop is always a <em>seam</em> — two standing tiles of unlike colour — because that is
    /// the one thing this mode is about. Light will not cross between two tiles of the same
    /// colour, so the shape the surge takes is exactly the shape the player arranged, and a
    /// grove built out of like-against-like stays dark however much of it there is.
    /// </para>
    /// </summary>
    public readonly struct KeeperHop
    {
        /// <summary>The cell the light leaves.</summary>
        public readonly int From;

        /// <summary>The cell it arrives at. Reached exactly once in a walk.</summary>
        public readonly int To;

        /// <summary>How many seams from the nearest bloom this is, counting from one.</summary>
        public readonly int Ring;

        public KeeperHop(int from, int to, int ring)
        {
            From = from;
            To = to;
            Ring = ring;
        }
    }

    /// <summary>
    /// <b>Where a bloom's light goes after the flower has opened.</b>
    ///
    /// <para>
    /// <b>This exists because Groovekeeper had no propagating event, and that was the whole of
    /// what was wrong with it.</b> Every other mode here has something that <em>travels</em> —
    /// a glade's light walks the network it was wired into, a well's burst washes into the motes
    /// beside it, a grove's chain crosses the board wave by wave — and this one laid a tile,
    /// opened a flower and stopped. There was nothing on the board for a celebration to be
    /// <em>about</em>, so every attempt at one came out as more decoration around a single cell.
    /// </para>
    /// <para>
    /// A surge is not a rule and changes nothing: it is read off the finished board, after
    /// <see cref="KeeperBoard.Plant"/> has settled it, and no par, star line or verdict has ever
    /// heard of it. What it buys is that the celebration walks <b>the shape the player made</b>,
    /// which is the one flourish this mode can have that no other could — two people who finish
    /// the same grove differently get visibly different light.
    /// </para>
    /// <para>
    /// <b>It is bounded by <see cref="MaxRings"/> rather than by the grove.</b> The board is
    /// latched while a planting plays, so an unbounded walk is an unbounded freeze — the same
    /// rule <c>KeeperTempo.Ceiling</c> holds over the flowers. Four rings is more than any
    /// shipped grove is wide, so on today's content it never binds; it is there so that a nine
    /// by nine grove wired end to end cannot cost a second of waiting to say something the first
    /// four rings already said.
    /// </para>
    /// <para>
    /// <b>A cell is claimed once.</b> The walk is a breadth-first search over the seam graph, so
    /// every tile is lit by the <em>nearest</em> bloom and lit exactly once — without that a
    /// grove with a ring in it would flare some tiles twice half a beat apart, which reads as a
    /// stutter rather than as light spreading.
    /// </para>
    /// </summary>
    public static class KeeperSurge
    {
        /// <summary>How far light travels from a bloom, in seams. See the class remarks.</summary>
        public const int MaxRings = 4;

        /// <summary>
        /// Walks the seam graph outward from <paramref name="from"/>, filling
        /// <paramref name="into"/> with every hop in ring order.
        ///
        /// <para>
        /// Sources that are not standing are skipped rather than refused: the caller hands over
        /// the cells a planting bloomed, and a bloom is by definition a tile, but a bounds check
        /// costs nothing and a walk that threw would take a celebration down with it.
        /// </para>
        /// </summary>
        public static void Walk(KeeperBoard board, IList<int> from, List<KeeperHop> into)
        {
            if (into == null) return;
            into.Clear();

            if (board == null || from == null || from.Count == 0) return;

            int count = board.Count;
            var seen = new bool[count];

            var frontier = new List<int>(from.Count);
            for (int i = 0; i < from.Count; i++)
            {
                int at = from[i];
                if (at < 0 || at >= count || seen[at] || !board.Standing(at)) continue;

                seen[at] = true;
                frontier.Add(at);
            }

            var next = new List<int>(8);

            for (int ring = 1; ring <= MaxRings && frontier.Count > 0; ring++)
            {
                next.Clear();

                for (int i = 0; i < frontier.Count; i++)
                {
                    int at = frontier[i];
                    int width = board.Width;
                    int x = at % width, y = at / width;

                    if (y > 0) Cross(board, at, at - width, ring, seen, into, next);
                    if (x < width - 1) Cross(board, at, at + 1, ring, seen, into, next);
                    if (y < board.Height - 1) Cross(board, at, at + width, ring, seen, into, next);
                    if (x > 0) Cross(board, at, at - 1, ring, seen, into, next);
                }

                var swap = frontier;
                frontier = next;
                next = swap;
            }
        }

        static void Cross(KeeperBoard board, int from, int to, int ring, bool[] seen,
                          List<KeeperHop> into, List<int> next)
        {
            if (seen[to]) return;

            int here = board.At(from), there = board.At(to);
            if (there == Energy.None || here == there) return;   // no tile, or no seam

            seen[to] = true;
            into.Add(new KeeperHop(from, to, ring));
            next.Add(to);
        }

        /// <summary>How far a walk reached, in rings. Nought for one that went nowhere.</summary>
        public static int Rings(IList<KeeperHop> hops)
            => hops == null || hops.Count == 0 ? 0 : hops[hops.Count - 1].Ring;
    }
}
