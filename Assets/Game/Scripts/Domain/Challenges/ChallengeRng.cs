namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// The one random stream a challenge draws from: xorshift32 over the row's seed, all
    /// 32-bit, so a merge board deals the same on every device
    /// and in every test (invariant 9c's arithmetic, used here for a board rather than a chest).
    ///
    /// <b>A struct passed by reference</b>, so a puzzle owns its stream and a test can fork one.
    /// Never nought, because xorshift is stuck there.
    /// </summary>
    public struct ChallengeRng
    {
        uint _state;

        public ChallengeRng(uint seed)
        {
            _state = seed * 2654435761u;
            if (_state == 0u) _state = 2463534242u;
        }

        public uint Next()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>A draw in <c>[0, n)</c>. Nought for a bound of nought or less.</summary>
        public int Below(int n) => n <= 0 ? 0 : (int)(Next() % (uint)n);
    }
}
