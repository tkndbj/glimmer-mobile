namespace GlimmerGrove.Modes
{
    /// <summary>
    /// A swap the field would accept: which two cells, and where the scan found it.
    ///
    /// <para>
    /// <b>A struct with a <see cref="Found"/> of its own rather than a bool and two
    /// <c>out</c>s.</b> Three answers travel together and the third only means anything beside
    /// the first two — <see cref="At"/> is where <c>SiegeBoard.FindSwap</c> stopped, so a caller
    /// asking again from <c>At + 1</c> gets the next pair rather than the same one. Split into
    /// parameters they are three things a call site can carry separately and get out of step.
    /// </para>
    /// <para>
    /// The default is "nothing found", which is what makes <c>default</c> a legal answer.
    /// </para>
    /// </summary>
    public readonly struct SiegeSwap
    {
        /// <summary>Whether there is a swap here at all. False on <c>default</c>.</summary>
        /// <remarks>
        /// <b>A field of its own rather than a test on the cells, and the first cut got that
        /// wrong.</b> Written as <c>A >= 0 &amp;&amp; B >= 0</c> it reads perfectly and answers
        /// <b>true</b> for <c>default</c>, because a struct's default is nought rather than minus
        /// one — so "nothing found" would have named cell nought twice and a hint would have
        /// pointed at the corner of a field with no move on it. Anything whose default has to mean
        /// *absent* needs a field that is false when it is zero.
        /// </remarks>
        public readonly bool Found;

        /// <summary>The two cells. Meaningless unless <see cref="Found"/>.</summary>
        public readonly int A, B;

        /// <summary>Which pair of the scan this was, so the next ask can carry on past it.</summary>
        public readonly int At;

        public SiegeSwap(int a, int b, int at)
        {
            Found = true;
            A = a;
            B = b;
            At = at;
        }
    }
}
