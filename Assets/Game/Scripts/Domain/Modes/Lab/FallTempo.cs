namespace GlimmerGrove.Modes
{
    /// <summary>
    /// How long every part of a Lightfall drop takes, in one place a test can hold to it.
    ///
    /// <para>
    /// <b>Here rather than beside the paint, for <c>WeaveTempo</c>'s reason.</b> Motion is the
    /// one subsystem in this game whose failures show up only in play — a cascade that outstays
    /// its welcome, a fall that reads as a lag, a chain whose length decides how long the player
    /// waits — so the arithmetic has to be reachable without an Editor. A <c>switch</c> on a
    /// wave count inside a <c>MonoBehaviour</c> is the one place here nothing can be proved.
    /// </para>
    /// <para>
    /// <b>Every sequence is bounded and the rate gives way.</b> That is the house rule and it is
    /// what this class exists to enforce: a drop that sets off nine waves must not take three
    /// times as long as one that sets off three, because the reward for a big chain has to be
    /// the chain rather than the waiting. So <see cref="Cascade"/> is capped and
    /// <see cref="Wave"/> is whatever fits inside it — a long chain plays <em>faster</em>, which
    /// is also what it should look like.
    /// </para>
    /// </summary>
    public static class FallTempo
    {
        // ------------------------------------------------------------------ the fall
        /// <summary>Seconds a mote spends crossing one row, before the clamps.</summary>
        public const float PerRow = .026f;

        /// <summary>Shortest and longest a fall may take, however deep the well.</summary>
        public const float MinFall = .13f, MaxFall = .30f;

        /// <summary>
        /// How long a mote takes to fall <paramref name="rows"/> rows.
        ///
        /// Clamped at both ends, and the ceiling is the one that matters: this sits between
        /// every decision and its consequence, so on the tallest well it must still feel like an
        /// answer rather than an animation.
        /// </summary>
        public static float Fall(int rows)
        {
            if (rows < 0) rows = 0;
            float seconds = rows * PerRow;
            return seconds < MinFall ? MinFall : seconds > MaxFall ? MaxFall : seconds;
        }

        /// <summary>The squash on impact, and the swell of a mote that has just been enriched.</summary>
        public const float Land = .16f, Enrich = .26f;

        // ------------------------------------------------------------------ the chain
        /// <summary>
        /// The most a whole cascade may take, however far it runs.
        ///
        /// A little over two seconds is long enough for a nine-wave chain to read as an event
        /// and short enough that the player is never waiting to be allowed to play. Past this
        /// the waves compress rather than the total growing.
        /// </summary>
        public const float Ceiling = 2.2f;

        /// <summary>How long one wave takes when there is room for it to take its time.</summary>
        public const float WaveFull = .34f;

        /// <summary>
        /// How long a whole cascade of <paramref name="waves"/> takes.
        ///
        /// Never above <see cref="Ceiling"/>. That is the claim <c>FallTempoTests</c> pins, and
        /// it is a correctness rule rather than a preference: the board is latched for exactly
        /// this long, so an unbounded cascade is an unbounded freeze.
        /// </summary>
        public static float Cascade(int waves)
        {
            if (waves <= 0) return 0f;
            float full = waves * WaveFull;
            return full < Ceiling ? full : Ceiling;
        }

        /// <summary>
        /// How long one wave of a <paramref name="waves"/>-wave cascade takes.
        ///
        /// The rate giving way, expressed as division: the cascade has a budget and the waves
        /// share it, so a longer chain plays faster instead of lasting longer.
        /// </summary>
        public static float Wave(int waves)
            => waves <= 0 ? 0f : Cascade(waves) / waves;

        /// <summary>
        /// The three beats one wave is cut into: the flash that says what happened, the burst
        /// and wash, and the collapse into the gaps.
        ///
        /// Fractions rather than seconds so they cannot come to disagree with
        /// <see cref="Wave"/> when it compresses — they are shares of whatever that answered.
        /// </summary>
        public const float FlashShare = .26f, BurstShare = .40f, SettleShare = .34f;

        public static float Flash(int waves) => Wave(waves) * FlashShare;
        public static float Burst(int waves) => Wave(waves) * BurstShare;
        public static float Settle(int waves) => Wave(waves) * SettleShare;

        // ------------------------------------------------------------------ the count
        /// <summary>
        /// How briskly the running chain count pops in. Always quick, and never longer than the
        /// wave it belongs to — on a long chain the waves are shorter than this, and a count
        /// that outlasted its own wave would still be arriving as the next one landed.
        /// </summary>
        public static float CountPop(int waves)
        {
            float wave = Wave(waves) * .9f;
            return wave < .22f ? wave : .22f;
        }

        /// <summary>
        /// The word at the end of a named chain, and the one beat here that is <em>not</em>
        /// inside <see cref="Ceiling"/>.
        ///
        /// <para>
        /// Deliberately so: the cascade's cap exists because the board is latched while it plays
        /// and a player waiting to act is a player being made to wait. This is the opposite — it
        /// is the pay-off, it happens after the last mote has gone, and it is the only moment in
        /// the mode worth stopping for. It is still bounded, and it is the whole of what a
        /// chain's celebration may cost.
        /// </para>
        /// </summary>
        public const float Fanfare = .85f;

        // ------------------------------------------------------------------ the reward
        /// <summary>
        /// A chain worth naming out loud. Two waves is a chain; below that it is a burst, and
        /// calling it one would spend the word on the ordinary case.
        /// </summary>
        public const int ChainFrom = 2;

        /// <summary>A chain worth the screen flashing and confetti for.</summary>
        public const int BigChainFrom = 4;

        /// <summary>How much the board is shaken by a wave of a chain this long, in canvas units.</summary>
        public static float Shake(int waves)
        {
            if (waves < ChainFrom) return 0f;
            float amount = 5f + (waves - ChainFrom) * 3.5f;
            return amount > 22f ? 22f : amount;
        }

        /// <summary>
        /// How far up the scale the burst sounds, as a pitch multiplier.
        ///
        /// Bounded so a long chain climbs and then holds: past a certain point a rising pitch
        /// stops reading as excitement and starts reading as a fault.
        /// </summary>
        public static float Pitch(int wave)
        {
            if (wave < 1) wave = 1;
            float pitch = .92f + (wave - 1) * .13f;
            return pitch > 1.9f ? 1.9f : pitch;
        }

        // ------------------------------------------------------------------ the well
        /// <summary>How long the whole well takes to arrive when a level opens.</summary>
        public const float Entrance = .72f;

        /// <summary>
        /// When a mote in the well arrives during the entrance, as a fraction of
        /// <see cref="Entrance"/>. Bottom rows first, so the well reads as filling rather than
        /// as a grid switching on.
        /// </summary>
        public static float EntranceDelay(int row, int height)
        {
            if (height <= 1) return 0f;
            if (row < 0) row = 0;
            if (row >= height) row = height - 1;

            // Counted from the bottom, and only ever over the first two thirds of the entrance
            // so the last mote still has a third of it left to land in.
            float fromBottom = (height - 1 - row) / (float)(height - 1);
            return fromBottom * Entrance * .62f;
        }
    }
}
