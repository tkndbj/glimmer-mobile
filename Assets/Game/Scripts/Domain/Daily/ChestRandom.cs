namespace GlimmerGrove.Daily
{
    /// <summary>
    /// The random number generator behind a chest, specified rather than chosen.
    ///
    /// <para>
    /// Three properties are required of it, and no stock generator has all three.
    /// </para>
    /// <list type="number">
    /// <item><b>It must not be re-rollable.</b> With <c>UnityEngine.Random</c> a player
    /// who does not like their prize force-quits during the opening animation and opens
    /// the chest again for a different one. Seeding from (player, day, chest) instead
    /// makes the contents a fact about that chest rather than about the moment it was
    /// tapped, so the reward is identical however many times the app dies first.</item>
    /// <item><b>It must be reproducible on the server.</b> Currency that was given rather
    /// than earned is server-owned — the client may never raise its own granted balance —
    /// so the server has to be able to work out what a chest contained without being told
    /// by the client. It recomputes this sequence from the same three inputs and grants
    /// its own answer. That is why the algorithm is written out here in full and pinned
    /// by shared vectors, rather than delegated to a library: the TypeScript half has to
    /// agree with it exactly, forever.</item>
    /// <item><b>It must be reproducible in JavaScript.</b> Everything below is 32-bit
    /// integer arithmetic, which JavaScript does natively and exactly. Anything wider
    /// would land in the middle of the 53-bit float mantissa and the two sides would
    /// diverge on some inputs and not others — the worst possible failure for money.</item>
    /// </list>
    /// </summary>
    public struct ChestRandom
    {
        const uint FnvOffsetBasis = 2166136261u;
        const uint FnvPrime = 16777619u;

        uint _state;

        /// <summary>
        /// Seeds from the three things that identify a chest: who owns it, which day it
        /// belongs to, and which of that day's chests it is.
        ///
        /// <paramref name="stream"/> separates independent draws within one chest, so the
        /// credit amount and the bonus roll do not share a sequence. Without it the second
        /// draw would be a pure function of the first, and the table's odds would quietly
        /// stop being the odds.
        /// </summary>
        public ChestRandom(string playerKey, int dayKey, int chestIndex, int stream)
        {
            uint hash = FnvOffsetBasis;

            Absorb(ref hash, playerKey ?? string.Empty);
            Absorb(ref hash, '|');
            Absorb(ref hash, dayKey);
            Absorb(ref hash, '|');
            Absorb(ref hash, chestIndex);
            Absorb(ref hash, '|');
            Absorb(ref hash, stream);

            // xorshift32 has one fixed point, and it is zero. A seed that lands there
            // would return zero forever, which is a jackpot or a wooden spoon depending
            // on the table — either way it is not a one-in-four-billion bug worth having.
            _state = hash == 0u ? FnvOffsetBasis : hash;
        }

        /// <summary>
        /// FNV-1a over the UTF-16 code units of a string, one byte at a time, low byte
        /// first. Player keys are ASCII ids, so this is the same as hashing the bytes —
        /// it is spelled out per code unit so a non-ASCII key can never make the two
        /// implementations disagree about what "the bytes" were.
        /// </summary>
        static void Absorb(ref uint hash, string text)
        {
            for (int i = 0; i < text.Length; i++) Absorb(ref hash, text[i]);
        }

        static void Absorb(ref uint hash, char c)
        {
            hash = (hash ^ (byte)(c & 0xFF)) * FnvPrime;
            hash = (hash ^ (byte)((c >> 8) & 0xFF)) * FnvPrime;
        }

        /// <summary>Decimal digits, so the hashed form is the one a human would write.</summary>
        static void Absorb(ref uint hash, int value)
        {
            if (value < 0) { Absorb(ref hash, '-'); value = -value; }

            // Written most-significant digit first without allocating a string, which is
            // what ToString() would do on a path the home screen walks on every build.
            int divisor = 1;
            while (value / divisor >= 10) divisor *= 10;

            while (divisor > 0)
            {
                Absorb(ref hash, (char)('0' + value / divisor % 10));
                divisor /= 10;
            }
        }

        /// <summary>The next value in the sequence. xorshift32, exactly as Marsaglia gave it.</summary>
        public uint Next()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>
        /// A value in <c>[0, bound)</c>.
        ///
        /// Plain modulo, with the bias that implies. The bias is at most one part in
        /// 2^32 / bound, and every bound this is called with is a weight total in the
        /// low hundreds — so the skew is far below the resolution of any odds a player
        /// or a regulator is shown. Rejection sampling would remove it and would add a
        /// loop whose iteration count both implementations would have to match exactly,
        /// which is a real risk traded for an imaginary one.
        /// </summary>
        public int Below(int bound) => bound <= 1 ? 0 : (int)(Next() % (uint)bound);

        /// <summary>An inclusive integer band, the shape amounts are authored in.</summary>
        public int Between(int min, int max)
        {
            if (max <= min) return min;
            return min + Below(max - min + 1);
        }
    }
}
