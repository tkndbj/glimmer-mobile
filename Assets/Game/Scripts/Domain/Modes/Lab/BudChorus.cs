namespace GlimmerGrove.Modes
{
    /// <summary>
    /// Which of a wave's landings are heard, and at what note.
    ///
    /// <para>
    /// <b>A falling board has to be audible or it is not falling, and the naive version of that
    /// is unlistenable.</b> The grove comes down in the same beat it burst in, and a deep wave
    /// drops twenty-odd pieces — struck one note each, that is twenty identical clunks inside
    /// half a second, which is a rattle rather than a shower. It is also more voices than
    /// <c>Audio.PlayOne</c>'s pool holds, so the tail of it would simply be dropped by the mixer
    /// and which pieces went quiet would depend on nothing the player can see.
    /// </para>
    /// <para>
    /// So a landing is a <em>chorus</em> rather than a per-piece sound: at most
    /// <see cref="MostVoices"/> of a wave's pieces are struck, spread evenly across it rather
    /// than taken off the front, and each on its own step of a pentatonic run. Evenly is what
    /// makes it read as the whole grove landing rather than as the first few — take the first
    /// five and a twenty-piece wave sounds like a five-piece one followed by silence, which is
    /// exactly backwards.
    /// </para>
    /// <para>
    /// <b>Here rather than in the view for <c>BudTempo</c>'s reason</b>, and one more of its
    /// own: a rule that decides which of twenty things is skipped is the kind that is wrong for
    /// a year without anybody being able to say why the board sounds thin.
    /// </para>
    /// </summary>
    public static class BudChorus
    {
        /// <summary>
        /// The most pieces of one wave that are struck.
        ///
        /// <para>
        /// Half <c>Audio.PlayOne</c>'s ten-voice pool, deliberately: a wave's landings are never
        /// the only thing sounding — the bursts that made the holes are still ringing, a cocoon
        /// may be cracking and the chain's own note is climbing over the top — so the landings
        /// may have half the pool at most or they take the sounds they are answering with them.
        /// </para>
        /// </summary>
        public const int MostVoices = 5;

        /// <summary>
        /// Whether the <paramref name="nth"/> of <paramref name="of"/> pieces landing in one
        /// wave is struck.
        ///
        /// <para>
        /// Evenly spread rather than the first few, and exact rather than sampled: the run picks
        /// out precisely <c>min(of, MostVoices)</c> of them, and the first piece to land is
        /// always one of them — a shower whose first arrival is silent reads as a missed cue
        /// however good the rest of it is.
        /// </para>
        /// </summary>
        public static bool Voiced(int nth, int of)
        {
            if (of <= 0 || nth < 0 || nth >= of) return false;
            if (of <= MostVoices) return true;
            if (nth == 0) return true;

            // Bresenham: the note changes exactly MostVoices times across the set, so the
            // spacing is as even as integers allow and the count is exact at every size.
            return nth * MostVoices / of != (nth - 1) * MostVoices / of;
        }

        /// <summary>
        /// The note the <paramref name="nth"/> of <paramref name="of"/> landings is struck at.
        ///
        /// <para>
        /// <b>A pentatonic run, for <c>sfx.tsv</c>'s reason.</b> These overlap each other and
        /// they overlap the bursts, and a pentatonic set carries no semitone — so no two of them
        /// can land on a beat together. It climbs, because the grove is filling up rather than
        /// emptying, and it starts below the mode's other voices so a shower of them sits under
        /// the bursts instead of arguing with them.
        /// </para>
        /// </summary>
        public static float Pitch(int nth, int of)
        {
            int rung = Rung(nth, of);
            return Base * Steps[rung];
        }

        /// <summary>Which step of the run a landing falls on.</summary>
        public static int Rung(int nth, int of)
        {
            if (of <= 1 || nth <= 0) return 0;
            if (nth >= of) nth = of - 1;

            int rung = nth * Steps.Length / of;
            return rung >= Steps.Length ? Steps.Length - 1 : rung;
        }

        /// <summary>Where the run starts — under everything else the grove is saying.</summary>
        public const float Base = .78f;

        /// <summary>
        /// And its steps: the root, a tone, a minor third, a fourth and a fifth, as frequency
        /// ratios. Written out rather than raised from semitones so this is a table a reader can
        /// check against a keyboard and so nothing here computes a power at runtime.
        /// </summary>
        public static readonly float[] Steps = { 1f, 1.1225f, 1.1892f, 1.3348f, 1.4983f };
    }
}
