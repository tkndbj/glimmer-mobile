namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What is drawn on top of a wave, as a function of how far into the chain it is.
    /// </summary>
    public readonly struct BudLayers
    {
        /// <summary>A ring of the wave's own colour thrown right across the grove.</summary>
        public readonly bool Sweep;

        /// <summary>Sparks arcing up out of the grove and going off above it.</summary>
        public readonly bool Fireworks;

        /// <summary>A slow star turning behind the whole board.</summary>
        public readonly bool Rays;

        public readonly bool Confetti;

        /// <summary>How hard the rest of the grove jolts as the wave passes over it, 0..1.</summary>
        public readonly float Ripple;

        /// <summary>How much of the screen the wave's colour takes, 0..1.</summary>
        public readonly float Tint;

        /// <summary>How many fireworks go up.</summary>
        public readonly int Rockets;

        public BudLayers(bool sweep, bool fireworks, bool rays, bool confetti,
                         float ripple, float tint, int rockets)
        {
            Sweep = sweep;
            Fireworks = fireworks;
            Rays = rays;
            Confetti = confetti;
            Ripple = ripple;
            Tint = tint;
            Rockets = rockets;
        }

        /// <summary>How many distinct kinds of thing this wave draws. The reading that matters.</summary>
        public int Kinds
        {
            get
            {
                int n = 1;                       // the burst itself, always
                if (Ripple > 0f) n++;
                if (Sweep) n++;
                if (Fireworks) n++;
                if (Rays) n++;
                if (Confetti) n++;
                return n;
            }
        }
    }

    /// <summary>
    /// <b>How a chain escalates, in <em>kinds</em> of thing rather than in amounts of one thing.</b>
    ///
    /// <para>
    /// <b>This exists because the first attempt did not work, and the reason generalises.</b>
    /// Every wave of every chain drew the same event — petals, a flash, a ring, sparks — and the
    /// escalation was carried entirely by numbers: a bigger swell, a harder shake, a brighter
    /// flash, a larger ring. Played for seven levels, that reads as <em>no change at all</em>,
    /// and it was reported exactly that way. A number going up is not something anybody sees; a
    /// thing that was not there before is.
    /// </para>
    /// <para>
    /// So each wave switches a whole new <em>kind</em> of thing on, and they are cumulative. Wave
    /// one is the burst and the grove jolting under it. Wave two throws a ring of its own colour
    /// right across the board and takes the screen. Wave three sends fireworks up out of the
    /// grove. Wave four lights a star behind the whole thing. Wave five brings confetti. A
    /// five-wave chain is therefore not "the same thing five times, louder" — it is six different
    /// things arriving one after another, which is what a cascade is supposed to feel like.
    /// </para>
    /// <para>
    /// <b>The thresholds are set against what the shipped boards actually run.</b> Most taps run
    /// one or two waves and a good tap runs five to nine, so the first three rungs have to land
    /// inside ordinary play or they are decoration for a chain nobody reaches — which is the
    /// mistake <c>BudTempo</c>'s first swell ladder made, spread over nine waves on a board whose
    /// best tap ran three.
    /// </para>
    /// <para>
    /// In Domain rather than beside the paint, for <c>BudChain</c>'s and <c>FallChain</c>'s
    /// reason: a switch on a wave count inside a <c>MonoBehaviour</c> is the one place in this
    /// game nothing can be proved, and how loud to shout is exactly the decision that gets
    /// retuned.
    /// </para>
    /// </summary>
    public static class BudSpectacle
    {
        /// <summary>The wave each new kind of thing arrives on.</summary>
        /// <remarks>
        /// <b>Every rung moved down one, and the argument is the one this ladder was built on
        /// in the first place.</b> It was written to stop the escalation being spent on waves
        /// nobody reaches — and it still was: most taps in this mode run <em>one</em> wave, so
        /// the commonest thing that happens in Budburst drew a burst and a jolt and nothing
        /// else, and the first genuinely new kind of thing arrived on a chain that half the
        /// groves never produce. A single tap now takes the board in its own colour, two waves
        /// send fireworks up over the grove, three light it from behind and four bring
        /// confetti. The shape of the ladder is untouched — one new kind per rung, nothing ever
        /// taken away, every kind on by the top — it has simply been slid onto the waves the
        /// mode actually plays.
        /// </remarks>
        public const int SweepFrom = 1, FireworksFrom = 2, RaysFrom = 3, ConfettiFrom = 4;

        /// <summary>The most kinds any wave draws at once. Every rung, and the burst.</summary>
        public const int MostKinds = 6;

        public static BudLayers Of(int wave)
        {
            if (wave < 1) wave = 1;

            float ripple = .34f + (wave - 1) * .16f;
            if (ripple > 1f) ripple = 1f;

            float tint = wave < SweepFrom ? 0f : .07f + (wave - SweepFrom) * .035f;
            if (tint > .24f) tint = .24f;

            int rockets = wave < FireworksFrom ? 0 : 3 + (wave - FireworksFrom);
            if (rockets > 8) rockets = 8;

            return new BudLayers(wave >= SweepFrom, wave >= FireworksFrom, wave >= RaysFrom,
                                 wave >= ConfettiFrom, ripple, tint, rockets);
        }

        // ------------------------------------------------------------------ the grove's jolt
        /// <summary>
        /// How long the jolt takes to cross the grove, given the beat it has to fit inside.
        ///
        /// <b>Bounded by the wave, like everything else here.</b> The ripple is the one effect
        /// that touches every cell on the board, so a ripple still travelling when the next wave
        /// charges would put two gestures on the same transforms — the bug this file's
        /// neighbours have paid for twice.
        /// </summary>
        public static float RippleOver(float burn)
        {
            float over = burn * .62f;
            return over > .42f ? .42f : over;
        }

        /// <summary>
        /// When a cell this far from the middle of the wave gets jolted, as a share of
        /// <see cref="RippleOver"/>.
        /// </summary>
        public static float RippleAt(float distance, float far)
        {
            if (far <= 0f) return 0f;

            float at = distance / far;
            if (at < 0f) at = 0f;
            return at > 1f ? 1f : at;
        }

        /// <summary>
        /// And how hard, which falls away with distance so the jolt reads as travelling outward
        /// rather than as the whole board being shaken at once — which is what
        /// <c>BudTempo.Heave</c> already does and would drown this out.
        /// </summary>
        public static float RippleForce(float strength, float distance, float far)
        {
            float fade = 1f - RippleAt(distance, far);
            float force = strength * .13f * fade * fade;
            return force < 0f ? 0f : force;
        }
    }
}
