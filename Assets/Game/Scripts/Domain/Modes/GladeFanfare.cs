namespace GlimmerGrove.Modes
{
    /// <summary>
    /// How long a solved glade takes to celebrate itself, and where every beat of it sits.
    ///
    /// <para>
    /// <b>Here rather than beside the board, for <c>BudTempo</c>'s and <c>FallTempo</c>'s
    /// reason.</b> Motion is the one subsystem whose failures show up only in play, so the
    /// arithmetic has to be reachable without an Editor. It matters more on this mode than on
    /// the others because a glade's celebration is the only one whose length is a function of
    /// the <em>board</em> — the light walks the network it was just solved into, so a deep
    /// grove has more to say than a shallow one, and nothing but a bound stops that becoming
    /// a wait.
    /// </para>
    /// <para>
    /// <b>The shape is quiet, travel, wake, bang, settle</b>, and the ordering is the whole
    /// design. A celebration that opens at full volume has nowhere to go; the
    /// <see cref="Hush"/> is what buys the surge somewhere to arrive from, and it is the beat
    /// most often left out because it looks like nothing happening. It is also the beat in
    /// which the player reads the board they just finished.
    /// </para>
    /// <para>
    /// <b>The rate gives way, and both bounds are load-bearing.</b> A depth-thirty grove must
    /// not take thirty rings' worth of seconds (<see cref="SurgeCeiling"/>), and a depth-three
    /// one must not flash past before the eye has followed the light out of the crystal
    /// (<see cref="MinRing"/>). Where the two meet the floor wins, for <c>BudTempo</c>'s
    /// reason: a payoff nobody can follow pays out nothing, and the paying out is the point.
    /// </para>
    /// </summary>
    public static class GladeFanfare
    {
        // ------------------------------------------------------------------ the hush
        /// <summary>
        /// The held breath between the last turn and the light moving.
        ///
        /// <para>
        /// It is not dead time and it is not politeness. The turn that finishes a glade looks
        /// exactly like every other turn at the instant it is taken — a conduit swings, a
        /// critter wakes — so without a beat that says <em>stop, that was the one</em>, the
        /// celebration begins while the player is still reading their own move and the first
        /// third of it is spent catching up. Everything dims and draws in slightly, which is
        /// the only moment in the mode where the board gets quieter.
        /// </para>
        /// </summary>
        public const float Hush = .40f;

        // ------------------------------------------------------------------ the surge
        /// <summary>The most the light may take to walk the whole network, however deep it is.</summary>
        public const float SurgeCeiling = 1.35f;

        /// <summary>One depth ring at its full length, when the board is shallow enough for it.</summary>
        public const float RingFull = .14f;

        /// <summary>And the floor under a ring, however deep the grove runs.</summary>
        public const float MinRing = .05f;

        /// <summary>How long apart two consecutive depth rings light up.</summary>
        public static float Ring(int rings)
        {
            if (rings <= 0) return 0f;

            float share = SurgeCeiling / rings;
            if (share > RingFull) share = RingFull;
            return share < MinRing ? MinRing : share;
        }

        /// <summary>How long the light takes to reach the far end of a grove this deep.</summary>
        public static float Surge(int rings)
        {
            if (rings <= 0) return 0f;
            return Ring(rings) * rings;
        }

        /// <summary>When the ring at this depth lights, measured from the surge's own start.</summary>
        public static float RingAt(int depth, int rings)
        {
            if (depth <= 0 || rings <= 0) return 0f;
            return Ring(rings) * depth;
        }

        // ------------------------------------------------------------------ inside one ring
        /// <summary>
        /// How long apart two conduits of the <em>same</em> depth flare.
        ///
        /// <para>
        /// <b>A ring is a ripple, not a frame.</b> Six tiles equidistant from the crystal are
        /// one instant as far as the model is concerned, and drawing them that way reads as a
        /// flat blink rather than as six things lighting — <c>BudTempo.StaggerStep</c>'s lesson,
        /// and it costs the ring nothing because the ripple is bounded to a fraction of the
        /// beat it lives in. However wide a ring is, the next one still starts on time.
        /// </para>
        /// </summary>
        public static float StaggerStep(float ring)
        {
            float step = ring * .22f;
            if (step < .012f) step = .012f;
            return step > .05f ? .05f : step;
        }

        /// <summary>Where the nth conduit of a ring this wide falls inside that ripple.</summary>
        public static float StaggerAt(int nth, int inRing, float ring)
        {
            if (nth <= 0 || inRing <= 1) return 0f;

            float delay = nth * StaggerStep(ring);

            // The ripple may never eat the beat it lives in, or a wide ring would still be
            // lighting when the next one starts and the light would stop reading as travelling.
            float most = ring * .5f;
            return delay > most ? most : delay;
        }

        // ------------------------------------------------------------------ the notes
        /// <summary>
        /// The most notes the surge may sound, however many rings it walks.
        ///
        /// <para>
        /// <b>A ceiling on sound rather than on rings, because the two have different
        /// limits.</b> Twenty-three rings inside a second and a third is a legible sweep of
        /// light and an unlistenable machine-gun of notes — and <c>Audio.PlayOne</c> pools ten
        /// voices, so past that the sounds do not merely crowd, they cut each other off. So a
        /// deep grove sounds every nth ring and the light still walks every one of them.
        /// </para>
        /// </summary>
        public const int MostNotes = 12;

        /// <summary>Sound one ring in this many. Always at least one.</summary>
        public static int NoteStride(int rings)
        {
            if (rings <= MostNotes) return 1;

            int stride = rings / MostNotes;
            return rings % MostNotes == 0 ? stride : stride + 1;
        }

        /// <summary>
        /// What the note for a ring this far out is pitched at.
        ///
        /// A climb rather than a ladder, because the surge is one gesture travelling and not a
        /// phrase of separate events — the pitch says <em>how far the light has got</em>, which
        /// is a position along something and reads best as a slide.
        /// </summary>
        public static float Pitch(int depth, int rings)
        {
            if (rings <= 1 || depth <= 0) return Lowest;

            float t = depth / (float)(rings - 1);
            if (t > 1f) t = 1f;
            return Lowest + (Highest - Lowest) * t;
        }

        /// <summary>The pitch the light leaves the crystal at.</summary>
        public const float Lowest = .82f;

        /// <summary>And the one it arrives at the far end of the grove at — a touch over an octave.</summary>
        public const float Highest = 1.78f;

        // ------------------------------------------------------------------ a critter waking
        /// <summary>
        /// How long one critter's answer to the light takes, whichever answer it is giving —
        /// the flinch as the wave reaches it, or the leap at the bloom.
        ///
        /// <para>
        /// The wake rides <em>on</em> the surge rather than after it: a critter goes off when
        /// the light gets to it, which is what makes the wave read as causing something rather
        /// than as passing over it. So this never lengthens the sequence — <see cref="Tail"/>
        /// is what pays for the last one.
        /// </para>
        /// <para>
        /// <b>Only the bloom leaps.</b> Both moments used to, and two leaps a second apart from
        /// one creature read as a single gesture stuttering rather than as two — so the wake is
        /// now a squash and a shiver in place, and the jump is the one thing the whole grove
        /// does together. <see cref="Leap"/> and <see cref="Land"/> below describe that jump's
        /// arc and are read only by the finale.
        /// </para>
        /// </summary>
        public const float Pump = .58f;

        /// <summary>The rising half of the jump: up, and over the top of it.</summary>
        public const float Leap = .26f;

        /// <summary>And the fall, which is longer, because a landing that is quicker than the jump reads as a snap back.</summary>
        public static float Land => Pump - Leap;

        /// <summary>
        /// The room left after the light stops for the last critter it woke to finish landing.
        ///
        /// Shorter than <see cref="Pump"/> deliberately: the bloom is allowed to arrive over the
        /// tail of the last leap, because a celebration whose beats never overlap reads as a
        /// list rather than as a crescendo.
        /// </summary>
        public const float Tail = .25f;

        // ------------------------------------------------------------------ the bloom
        /// <summary>
        /// The crescendo: every critter leaves the ground at once, the grove goes white, and
        /// the shockwave crosses it.
        ///
        /// <para>
        /// The longest single beat in the sequence, and the only one that is a constant. Every
        /// other beat here is a function of the board because it is <em>about</em> the board;
        /// this one is the game congratulating the player, which is the same size whatever they
        /// just solved.
        /// </para>
        /// </summary>
        public const float Bloom = 1.00f;

        /// <summary>How long apart the two shockwave rings leave the middle of the board.</summary>
        public const float WaveGap = .16f;

        /// <summary>And how long one of them takes to cross the grove and fade.</summary>
        public const float WaveCross = .80f;

        // ------------------------------------------------------------------ the settle
        /// <summary>
        /// The grove holding its new light before the panel covers it.
        ///
        /// <para>
        /// The panel is a scrim over the board, so this is the last of the player's own work
        /// they will see. Cutting it is the cheapest saving in the sequence and the one that
        /// costs the most: it turns the payoff into a transition.
        /// </para>
        /// </summary>
        public const float Settle = .50f;

        // ------------------------------------------------------------------ the whole thing
        /// <summary>When the bloom lands, measured from the last turn.</summary>
        public static float BloomAt(int rings) => Hush + Surge(rings) + Tail;

        /// <summary>And when the panel is raised.</summary>
        public static float Total(int rings) => BloomAt(rings) + Bloom + Settle;

        /// <summary>
        /// The longest a celebration may run before it stops being one.
        ///
        /// <para>
        /// Not a clamp — nothing reads this to cut anything short. It is the bound the parts
        /// are chosen against, asserted by the suite over every grove this mode can author, so
        /// that a beat lengthened for its own good reasons cannot quietly walk the whole
        /// sequence past the point where the player is waiting rather than watching.
        /// </para>
        /// </summary>
        public const float Longest = 4.20f;

        /// <summary>
        /// The deepest grove the bound is asserted over.
        ///
        /// <para>
        /// <b>Measured, not guessed.</b> The deepest network in the forty shipped glades is
        /// fifteen rings (<c>c02_stonebridge</c>, 7x7), and this is a little over double that —
        /// past the point where <see cref="MinRing"/> takes over from
        /// <see cref="SurgeCeiling"/>, so it exercises the branch a shipped board does not.
        /// A grove cannot be deeper than the cells it has, so an 8x7 board could in principle
        /// reach fifty-six; a sequence that long is a fact about a board nobody would author
        /// rather than about this arithmetic, and pinning the bound to a number no content
        /// approaches would make the assertion say nothing.
        /// </para>
        /// </summary>
        public const int DeepestGrove = 32;
    }
}
