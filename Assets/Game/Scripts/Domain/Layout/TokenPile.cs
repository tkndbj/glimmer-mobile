namespace GlimmerGrove.Layout
{
    /// <summary>One token's place in a pile.</summary>
    public readonly struct TokenSpot
    {
        /// <summary>Its centre, against the pile's own centre.</summary>
        public readonly float X, Y;

        /// <summary>How far it leans, in degrees, away from the middle of its row.</summary>
        public readonly float Tilt;

        /// <summary>
        /// Which token of the pile this is, counted left to right along the front row and then
        /// left to right along the back one — <em>not</em> the order it is drawn in.
        ///
        /// The two differ because a pile has to be drawn back to front, and a caller filling a
        /// mixed pile (a bundle's gems and its coins) is choosing by position rather than by
        /// depth. Handing over only the draw order is what would put a bundle's gems wherever
        /// the shingle happened to start.
        /// </summary>
        public readonly int Slot;

        public TokenSpot(float x, float y, float tilt, int slot)
        {
            X = x;
            Y = y;
            Tilt = tilt;
            Slot = slot;
        }
    }

    /// <summary>
    /// A tidy heap of identical tokens — a shop card's coins, its gems, and the hearts over a
    /// vessel's lip.
    ///
    /// <para>
    /// <b>It exists once because it is drawn three times</b> (invariant 9a's argument at its
    /// smallest): a pile of currency, a pack of hearts and a container's spill were three
    /// copies of one idea that had already drifted into three different arrangements. All
    /// three were a single shallow arc with every second token dropped a little, and that
    /// alternation is what made them read as spilt rather than stacked — <c>i % 2</c> is only
    /// symmetric when the count is odd, so a pile of four or six came out visibly heavier on
    /// one side, and the side it was heavy on changed with the rung.
    /// </para>
    /// <para>
    /// <b>Two rows, wider at the bottom, centred.</b> A heap is legible because its rows are,
    /// and it is symmetric for the same reason a face is: nothing about the arrangement should
    /// be a decision the eye has to take in. The positions come from the index rather than from
    /// a random number for the reason they always did here — a grid cell is rebound as it
    /// scrolls, so a scatter would re-scatter every time a card came back on screen.
    /// </para>
    /// <para>
    /// <b>Here rather than beside the art</b> so that symmetry, the draw order and staying
    /// inside the picture's box are things that can be proved rather than looked at; a
    /// composition is the one kind of fault a compile, a validator and a screenshot of the
    /// source all agree looks fine.
    /// </para>
    /// </summary>
    public static class TokenPile
    {
        /// <summary>
        /// How far apart two tokens in a row sit, as a fraction of a token. Under one, so they
        /// overlap: a row of tokens that do not touch is a queue, not a pile.
        /// </summary>
        public const float Spacing = .80f;

        /// <summary>How far the back row sits above the front one, likewise.</summary>
        public const float RowStep = .60f;

        /// <summary>How far the outermost token of a row leans away from the middle.</summary>
        public const float Tilt = 12f;

        /// <summary>
        /// How many of the pile stand in the front row.
        ///
        /// A pyramid: the front row is the larger half, so it is never narrower than what is
        /// stacked behind it, and a pile of one or two is simply one row.
        /// </summary>
        public static int FrontRow(int total) => total <= 0 ? 0 : (total + 2) / 2;

        /// <summary>How wide and how tall a pile of <paramref name="total"/> comes out.</summary>
        public static float Width(int total, float token)
            => total <= 0 ? 0f : (FrontRow(total) - 1) * token * Spacing + token;

        public static float Height(int total, float token)
            => total <= 0 ? 0f : (total > FrontRow(total) ? token * RowStep : 0f) + token;

        /// <summary>
        /// The pile, centred on the origin and <b>in the order it should be drawn</b>: the back
        /// row first, and each row from its ends inwards.
        ///
        /// <para>
        /// The order is half of what makes this look like a heap. Drawing a row left to right
        /// shingles every token over the one before it, which points the whole pile one way;
        /// drawing from the outside in puts the middle of each row on top, so the overlap reads
        /// the same from either side. And the front row going last is what stops the thing
        /// behind being drawn in front of it, which no amount of positioning can fix.
        /// </para>
        /// </summary>
        public static TokenSpot[] Of(int total, float token)
        {
            if (total <= 0) return new TokenSpot[0];

            int front = FrontRow(total);
            int back = total - front;

            float step = back > 0 ? token * RowStep : 0f;
            var spots = new TokenSpot[total];

            int at = Row(spots, 0, back, front, token, step * .5f);
            Row(spots, at, front, 0, token, -step * .5f);

            return spots;
        }

        /// <summary>Lays one row out, from its ends inwards.</summary>
        static int Row(TokenSpot[] into, int at, int count, int firstSlot, float token, float y)
        {
            for (int n = 0; n < count; n++)
            {
                int i = OutsideIn(n, count);

                float from = i - (count - 1) * .5f;              // steps from the row's middle
                float reach = (count - 1) * .5f;

                into[at++] = new TokenSpot(from * token * Spacing, y,
                                           reach <= 0f ? 0f : -(from / reach) * Tilt,
                                           firstSlot + i);
            }

            return at;
        }

        /// <summary>
        /// The <paramref name="n"/>th token to be drawn in a row of <paramref name="count"/>:
        /// the two ends, then the two inside them, and so on to the middle.
        /// </summary>
        static int OutsideIn(int n, int count) => (n & 1) == 0 ? n / 2 : count - 1 - n / 2;
    }
}
