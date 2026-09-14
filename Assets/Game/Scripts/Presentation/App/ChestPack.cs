using System.Collections.Generic;
using GlimmerGrove.Tasks;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Where each chest of a pack stands: a symmetric arch, biggest in the middle, on a floor
    /// that dips with it, packed until the chests overlap.
    ///
    /// <para>
    /// <b>Two screens draw this row</b> — the hub's Tasks &amp; Bonuses box and the tasks page's
    /// own ladder — and a pack is a shape rather than a picture, so the arithmetic lives here
    /// and each screen builds its own widgets from the answer. Two copies would be two answers
    /// to a question settled once, which is the argument the interface kit itself rests on
    /// (invariant 44).
    /// </para>
    /// <para>
    /// <b>The grandest chest takes the crest.</b> That is the whole of what makes a pack a
    /// pack: both screens exist to advertise the best thing on the ladder, and a plain
    /// left-to-right ladder puts that chest on an end — drawn smallest, and half behind its
    /// neighbour. Seats are handed out tallest first, so a fifth tier joins a balanced row
    /// rather than needing a second arrangement, and a tie between two equal seats goes to the
    /// right so the row still reads as climbing before it steps back.
    /// </para>
    /// <para>
    /// <b>The bell is taken off the distance from the middle, out of an integer numerator.</b>
    /// Two seats that ought to be the same height have to be the same height <em>to the bit</em>,
    /// because the tie between them is what decides which chest takes the crest:
    /// <c>i / (n-1) * 2 - 1</c> gives -.33333334 and .33333337 for a row of four, so the seats
    /// differ by a float's last digit and the grandest chest picks its own side out of rounding
    /// noise.
    /// </para>
    /// </summary>
    public static class ChestPack
    {
        /// <summary>
        /// The closed chest is <em>frame nought of the opening reel</em>, so its 176x244 sprite
        /// carries the lid's headroom: the drawn chest fills .635 of the sprite's height and
        /// .864 of its width, and its middle sits .158 of that height below the sprite's own.
        /// The four tiers agree to a pixel.
        ///
        /// <para>
        /// <b>It cannot be trimmed, which is why these exist.</b> The ceremony hands this icon
        /// over to the reel, and an icon trimmed to its alpha against a reel that is not is a
        /// chest that jumps the moment one is opened. So a pack is laid out in <em>drawn</em>
        /// heights and converted once, by <see cref="Seat.Box"/> and <see cref="Seat.Anchor"/> —
        /// a row packed edge to edge off the sprite's own box would stand a quarter of a chest
        /// apart and float a quarter of a chest high.
        /// </para>
        /// <para>
        /// <c>Tools/render_tasks.py</c> measures the same four numbers off the PNG and prints
        /// them on every run, which is the only thing that can say they have gone stale after a
        /// re-cut (invariant 44b: measured, not typed).
        /// </para>
        /// </summary>
        public const float Fill = 155f / 244f;
        public const float Wide = 151f / 155f;      // drawn width per drawn height
        public const float Lift = 38.5f / 155f;     // ... in drawn heights, so it scales with the row
        public const float Aspect = 176f / 244f;

        /// <summary>One chest's place in the row, in drawn units about the pack's own middle.</summary>
        public struct Seat
        {
            public ChestTier Tier;

            /// <summary>The seat's place in the row, left to right. For staggering phases.</summary>
            public int Index;

            public float X;
            public float Foot;
            public float Tall;
            public float Width;

            /// <summary>Where the middle of the drawn chest goes.</summary>
            public Vector2 Middle => new Vector2(X, Foot + Tall * .5f);

            /// <summary>Where the sprite's own box goes, which is higher (see <see cref="Lift"/>).</summary>
            public Vector2 Anchor => new Vector2(X, Foot + Tall * .5f + Tall * Lift);

            /// <summary>How big the sprite's own box is.</summary>
            public Vector2 Box => new Vector2(Tall / Fill * Aspect, Tall / Fill);
        }

        /// <summary>
        /// Lays the pack out and centres it on nought.
        ///
        /// <para>
        /// The positions come off a <b>cursor</b> rather than off index arithmetic, because the
        /// chests are not one width: stepping by a constant leaves the short ends floating while
        /// the tall middles collide (37bc's argument, on a row rather than a grid). The run is
        /// then measured and centred, so the caller never has to know how wide it came out.
        /// </para>
        /// </summary>
        /// <param name="tall">The drawn height of the chest at the crest.</param>
        /// <param name="shortest">The drawn height of the two on the ends.</param>
        /// <param name="dip">How much lower than the ends the crest stands.</param>
        /// <param name="floor">Where an end chest's feet are, from the host's middle.</param>
        /// <param name="overlap">
        /// How far a chest pushes into its neighbour, as a fraction of the narrower of the two —
        /// and <b>negative for a row that stands apart</b>, which is the same arithmetic with one
        /// sign and is what the two callers really differ by: the hub's box is a picture of a
        /// pack, while every chest on the tasks page is a button, and buttons that overlap are
        /// targets whose edges belong to whichever was drawn last.
        /// </param>
        public static Seat[] Lay(IReadOnlyList<ChestTier> tiers, float tall, float shortest,
                                 float dip, float floor, float overlap)
        {
            int n = tiers?.Count ?? 0;
            if (n == 0) return new Seat[0];

            var bell = new float[n];
            var high = new float[n];
            var wide = new float[n];
            var x = new float[n];

            // How far from the middle a seat is, in half-steps, so the numerator is an integer
            // and two seats that ought to tie do tie (see the remarks on the class). The range
            // is rescaled onto the nearest and furthest seats the row actually *has*, which is
            // what makes `tall` and `shortest` mean what they say: an even row has no seat in
            // the middle, so dividing by `n - 1` alone leaves its crest short of `tall` — and
            // for a row of two it leaves *every* seat at `shortest`, an arch with no crest in
            // it at all.
            int near = (n - 1) % 2;
            int far = n - 1;
            float span = far - near;

            for (int i = 0; i < n; i++)
            {
                float u = span <= 0f ? 0f : (Mathf.Abs(2 * i - (n - 1)) - near) / span;
                bell[i] = Mathf.Cos(u * Mathf.PI * .5f);
                high[i] = Mathf.Lerp(shortest, tall, bell[i]);
                wide[i] = high[i] * Wide;

                x[i] = i == 0
                     ? 0f
                     : x[i - 1] + (wide[i - 1] + wide[i]) * .5f - Mathf.Min(wide[i - 1], wide[i]) * overlap;
            }

            float mid = (x[0] - wide[0] * .5f + x[n - 1] + wide[n - 1] * .5f) * .5f;

            // Which chest stands in which seat: the grandest to the tallest seat, and so on
            // outward. The tie goes to the right (see the remarks on the class).
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => bell[a] == bell[b] ? b.CompareTo(a) : bell[b].CompareTo(bell[a]));

            var seats = new Seat[n];
            for (int k = 0; k < n; k++)
            {
                int i = order[k];
                seats[i] = new Seat
                {
                    Tier = tiers[n - 1 - k],
                    Index = i,
                    X = x[i] - mid,
                    Foot = floor - dip * bell[i],
                    Tall = high[i],
                    Width = wide[i],
                };
            }

            return seats;
        }

        /// <summary>
        /// The seats shortest first, which is the order they have to be <em>built</em> in: an
        /// arch whose far end overlaps the crest is an arch drawn back to front.
        /// </summary>
        public static Seat[] InDrawOrder(Seat[] seats)
        {
            var order = (Seat[])seats.Clone();
            System.Array.Sort(order, (a, b) => a.Tall.CompareTo(b.Tall));
            return order;
        }
    }
}
