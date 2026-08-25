namespace GlimmerGrove.Modes
{
    /// <summary>
    /// How a Lightweave grove sounds and moves: how fast light runs a channel, what note a
    /// waking critter rings, how the finale cascades, and when the clock starts to press.
    ///
    /// <para>
    /// <b>In Domain, for <c>TweenCycle</c>'s reason.</b> This is the one part of the mode whose
    /// failures are invisible in a screenshot and obvious only in motion — a pulse that takes
    /// three seconds to cross a long channel, a finale that fires six chimes inside one frame, a
    /// note ladder that repeats so the sixth critter sounds like the third. Every one of those
    /// compiles, validates and reads perfectly in the source. So the arithmetic lives here with
    /// no Unity types in it and <c>WeaveTempoTests</c> runs it a thousand simulated grooves at a
    /// time, and <see cref="WeaveView"/> is left holding only the drawing.
    /// </para>
    /// <para>
    /// <b>Every duration here is bounded, and the rate is what gives way.</b> That is
    /// <c>GroveGrowth.MaxSpread</c>'s rule for the third time: a grove twice the size must not be
    /// twice the wait, because the wait is time the player is not playing. A drop that sells a
    /// 9x12 grove cannot silently ship a three-second pause on every channel.
    /// </para>
    /// </summary>
    public static class WeaveTempo
    {
        // ------------------------------------------------------------------ light on the wire
        /// <summary>Seconds the light spends crossing one cell of a channel, before the ceiling.</summary>
        public const float CellSeconds = .020f;

        /// <summary>
        /// The shortest a channel may take to light.
        ///
        /// A five-cell channel at the raw rate is a tenth of a second, which is not a thing
        /// travelling — it is a flicker, and the player reads it as the line simply changing
        /// colour. The floor is what keeps the shortest hop still legible as light going
        /// somewhere.
        /// </summary>
        public const float MinTravel = .17f;

        /// <summary>
        /// The longest, however far the light has to go. See the type summary: past this the
        /// rate gives way and a long channel simply lights faster per cell.
        /// </summary>
        public const float MaxTravel = .46f;

        /// <summary>How long light takes to run a channel of <paramref name="cells"/> cells.</summary>
        public static float TravelSeconds(int cells)
        {
            if (cells < 2) return MinTravel;

            float raw = cells * CellSeconds;
            if (raw < MinTravel) return MinTravel;
            return raw > MaxTravel ? MaxTravel : raw;
        }

        // ------------------------------------------------------------------ the note ladder
        /// <summary>
        /// Semitones of a major pentatonic scale. Five notes, and the sixth is the octave.
        ///
        /// Pentatonic because no two of its notes clash: a player who joins channels out of the
        /// order the ladder expects — which is every player — still hears a chord rather than a
        /// mistake. A diatonic ladder has a semitone in it and the wrong pair of neighbours
        /// sounds like a wrong answer at the exact moment the game means "well done".
        /// </summary>
        static readonly int[] Ladder = { 0, 2, 4, 7, 9 };

        /// <summary>
        /// The pitch the <paramref name="joined"/>'th channel of a grove rings at, 1-based.
        ///
        /// <para>
        /// Rising, and never repeating inside one grove: six is the most pairs a grove can hold
        /// (<see cref="WeaveGenerator.Palette"/>) and the ladder plus its octave is exactly six
        /// notes, so the last critter of the hardest grove is a clean octave above the first.
        /// Beyond that it keeps climbing by octaves rather than wrapping back down, because a
        /// ladder that falls in the middle tells the player they have gone backwards.
        /// </para>
        /// </summary>
        public static float Pitch(int joined)
        {
            int index = joined < 1 ? 0 : joined - 1;
            int semitones = Ladder[index % Ladder.Length] + 12 * (index / Ladder.Length);

            // 2^(n/12), by repeated multiplication so this needs no maths library and stays
            // identical everywhere it is run.
            float pitch = 1f;
            for (int i = 0; i < semitones; i++) pitch *= 1.0594631f;
            return pitch;
        }

        // ------------------------------------------------------------------ the finale
        /// <summary>
        /// The whole closing cascade, however many channels there are.
        ///
        /// Short on purpose. It sits between the last channel landing and the victory panel
        /// opening, so every millisecond of it is a millisecond the player has already won and
        /// is waiting to be told so.
        /// </summary>
        public const float MaxFinaleSeconds = 1.25f;

        /// <summary>The gap the cascade would like between two channels lighting.</summary>
        public const float FinaleGap = .13f;

        /// <summary>
        /// The gap it actually gets. The ceiling is fixed and the gap is what gives way, so six
        /// channels close in the same breath three do.
        /// </summary>
        public static float GapFor(int channels)
        {
            if (channels < 2) return 0f;

            float wanted = FinaleGap;
            float most = MaxFinaleSeconds / (channels - 1);
            return wanted < most ? wanted : most;
        }

        /// <summary>When the <paramref name="index"/>'th channel of the cascade lights, 0-based.</summary>
        public static float FinaleAt(int index, int channels)
            => index < 1 ? 0f : GapFor(channels) * index;

        /// <summary>How long the whole cascade runs. Never more than <see cref="MaxFinaleSeconds"/>.</summary>
        public static float FinaleSeconds(int channels)
            => channels < 2 ? 0f : FinaleAt(channels - 1, channels);

        // ------------------------------------------------------------------ the clock
        /// <summary>
        /// The share of a grove's light left when it starts to press — the last fifth of it.
        ///
        /// Late enough that an ordinary run never sees it, which is what stops the effect
        /// becoming wallpaper; early enough that when it does arrive there is still time to act
        /// on it, which is the only reason to warn somebody at all.
        /// </summary>
        public const float PressesUnder = .20f;

        /// <summary>
        /// How hard the clock is pressing, 0 (not at all) to 1 (out of light).
        ///
        /// <para>
        /// <b>Takes the limit rather than the time left, and that is the whole reason it is a
        /// function.</b> An untimed grove reports <c>int.MaxValue</c> for its limit and
        /// <c>WeaveScreen.Remaining</c> answers <b>0</b> for one — the honest reading of "there
        /// is no countdown", and indistinguishable from "the countdown has run out" to anything
        /// reading it directly. A view wired to that would put a grove with no clock at all
        /// under a full-brightness alarm from its first frame. Handed the elapsed time and the
        /// limit, this cannot make that mistake, and <c>WeaveTempoTests</c> pins it.
        /// </para>
        /// </summary>
        public static float Urgency(int elapsedMillis, int limitMillis)
        {
            if (limitMillis <= 0 || limitMillis == int.MaxValue) return 0f;

            int left = limitMillis - elapsedMillis;
            if (left <= 0) return 1f;

            float share = left / (float)limitMillis;
            if (share >= PressesUnder) return 0f;

            return 1f - share / PressesUnder;
        }
    }
}
