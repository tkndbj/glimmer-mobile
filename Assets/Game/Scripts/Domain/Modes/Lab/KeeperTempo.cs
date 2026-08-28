namespace GlimmerGrove.Modes
{
    /// <summary>
    /// How long every part of a Groovekeeper planting takes, in one place a test can hold to it.
    ///
    /// <para>
    /// <b>Here rather than beside the paint, for <c>FallTempo</c>'s reason.</b> Motion is the one
    /// subsystem in this game whose failures show up only in play — a cascade that outstays its
    /// welcome, a bloom that reads as a lag, a flourish whose size decides how long the player
    /// waits — so the arithmetic has to be reachable without an Editor.
    /// </para>
    /// <para>
    /// <b>Every sequence is bounded and the rate gives way.</b> That is the house rule and it is
    /// what this class exists to enforce: a planting that opens five flowers must not take five
    /// times as long as one that opens one, because the reward for a big flourish has to be the
    /// flourish rather than the waiting. So <see cref="Cascade"/> is capped and <see cref="Petal"/>
    /// is whatever fits inside it — a bigger flourish opens <em>faster</em>, which is also what it
    /// should look like.
    /// </para>
    /// </summary>
    public static class KeeperTempo
    {
        // ------------------------------------------------------------------ the planting
        /// <summary>How long a tile takes to drop into place, and the squash when it lands.</summary>
        public const float Land = .20f, Squash = .14f;

        /// <summary>How long a new seam takes to light along the edge it was made on.</summary>
        public const float Seam = .22f;

        // ------------------------------------------------------------------ the blooms
        /// <summary>
        /// The most a whole cascade of blooms may take, however many open.
        ///
        /// A little over a second: long enough for five flowers to read as five events and short
        /// enough that the player is never waiting to be allowed to play. Past this the blooms
        /// compress rather than the total growing.
        /// </summary>
        public const float Ceiling = 1.15f;

        /// <summary>How long one bloom takes when there is room for it to take its time.</summary>
        public const float PetalFull = .34f;

        /// <summary>
        /// How long a whole cascade of <paramref name="blooms"/> flowers takes.
        ///
        /// Never above <see cref="Ceiling"/>. That is the claim <c>KeeperTempoTests</c> pins, and
        /// it is a correctness rule rather than a preference: the board is latched for exactly
        /// this long, so an unbounded cascade is an unbounded freeze.
        /// </summary>
        public static float Cascade(int blooms)
        {
            if (blooms <= 0) return 0f;

            float full = blooms * PetalFull;
            return full < Ceiling ? full : Ceiling;
        }

        /// <summary>
        /// How long one flower of a <paramref name="blooms"/>-flower cascade takes.
        ///
        /// The rate giving way, expressed as division: the cascade has a budget and the flowers
        /// share it, so a bigger flourish opens faster instead of lasting longer.
        /// </summary>
        public static float Petal(int blooms)
            => blooms <= 0 ? 0f : Cascade(blooms) / blooms;

        /// <summary>
        /// How briskly the running count pops in. Always quick, and never longer than the flower
        /// it belongs to — on a big flourish the flowers are shorter than this, and a count that
        /// outlasted its own flower would still be arriving as the next one opened.
        /// </summary>
        public static float CountPop(int blooms)
        {
            float petal = Petal(blooms) * .9f;
            return petal < .20f ? petal : .20f;
        }

        /// <summary>
        /// The word at the end of a named flourish, and the one beat here that is <em>not</em>
        /// inside <see cref="Ceiling"/>.
        ///
        /// Deliberately so: the cascade's cap exists because the board is latched while it plays
        /// and a player waiting to act is a player being made to wait. This is the opposite — it
        /// is the pay-off, it happens after the last flower has opened, and it is the only moment
        /// in the mode worth stopping for.
        /// </summary>
        public const float Fanfare = .80f;

        /// <summary>A flourish worth the screen flashing and confetti for.</summary>
        public const int BigFrom = 4;

        /// <summary>How much the grove is shaken by a flourish this big, in canvas units.</summary>
        public static float Shake(int blooms)
        {
            if (blooms < KeeperFlourish.CountFrom) return 0f;

            float amount = 4f + (blooms - KeeperFlourish.CountFrom) * 4.5f;
            return amount > 20f ? 20f : amount;
        }

        /// <summary>
        /// How far up the scale a flower sounds, as a pitch multiplier.
        ///
        /// Bounded so a big flourish climbs and then holds: past a certain point a rising pitch
        /// stops reading as excitement and starts reading as a fault.
        /// </summary>
        public static float Pitch(int flower)
        {
            if (flower < 1) flower = 1;

            float pitch = .94f + (flower - 1) * .12f;
            return pitch > 1.7f ? 1.7f : pitch;
        }

        // ------------------------------------------------------------------ the grove
        /// <summary>How long the whole grove takes to arrive when a level opens.</summary>
        public const float Entrance = .78f;

        /// <summary>
        /// When a cell arrives during the entrance, as a fraction of <see cref="Entrance"/>.
        ///
        /// Measured from the middle outward, so the ground unrolls from where the sprig stands
        /// rather than reading as a grid switching on row by row. Only ever over the first two
        /// thirds of the entrance, so the furthest cell still has a third of it left to land in.
        /// </summary>
        public static float EntranceDelay(int x, int y, int width, int height)
        {
            if (width <= 1 && height <= 1) return 0f;

            float cx = (width - 1) * .5f, cy = (height - 1) * .5f;
            float dx = x - cx, dy = y - cy;

            float far = cx > cy ? cx : cy;
            if (far <= 0f) return 0f;

            float reach = dx < 0f ? -dx : dx;
            float down = dy < 0f ? -dy : dy;
            float distance = reach > down ? reach : down;

            return distance / far * Entrance * .62f;
        }

        /// <summary>
        /// How long the closing ripple takes: the grove lighting up from the last bloom outward,
        /// once every bed is open.
        ///
        /// Bounded like everything else here. It runs after the win has been decided rather than
        /// before it, so nothing is waiting on it, but a player is — and a celebration that has
        /// to be sat through is one they learn to tap past.
        /// </summary>
        public const float Ripple = .70f;
    }
}
