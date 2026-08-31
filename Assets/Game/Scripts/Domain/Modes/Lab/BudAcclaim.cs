namespace GlimmerGrove.Modes
{
    /// <summary>What one rung of the word ladder is allowed to draw.</summary>
    public readonly struct BudHonours
    {
        /// <summary>A highlight sweeping across the letters.</summary>
        public readonly bool Shine;

        /// <summary>Light thrown behind the word, so it arrives out of something.</summary>
        public readonly bool Bloom;

        public readonly bool Confetti;

        /// <summary>The whole grove answering under the word, cell by cell, out from the middle.</summary>
        public readonly bool Grove;

        /// <summary>How long the word gathers before it lands.</summary>
        public readonly float Gather;

        /// <summary>How many motes converge while it does.</summary>
        public readonly int Motes;

        /// <summary>How far the run climbs — the escalation that is heard rather than seen.</summary>
        public readonly int Notes;

        /// <summary>How much of the screen takes the rung's own colour.</summary>
        public readonly float Tint;

        /// <summary>How hard the board is knocked when it lands.</summary>
        public readonly float Shove;

        public BudHonours(bool shine, bool bloom, bool confetti, bool grove, float gather,
                          int motes, int notes, float tint, float shove)
        {
            Shine = shine;
            Bloom = bloom;
            Confetti = confetti;
            Grove = grove;
            Gather = gather;
            Motes = motes;
            Notes = notes;
            Tint = tint;
            Shove = shove;
        }

        /// <summary>
        /// How many different things this rung draws, which is what the ladder is really made of.
        /// </summary>
        public int Kinds
        {
            get
            {
                int n = 1;                          // the word landing, always
                if (Shine) n++;
                if (Bloom) n++;
                if (Confetti) n++;
                if (Grove) n++;

                return n;
            }
        }
    }

    /// <summary>
    /// How loudly the game says GREAT, AMAZING, EPIC or LEGENDARY.
    ///
    /// <para>
    /// <b>This is <see cref="BudSpectacle"/>'s lesson applied to the one place that never got
    /// it.</b> A chain's waves learned it the expensive way — every wave used to draw the same
    /// event with the escalation carried entirely by numbers (a bigger swell, a harder shake, a
    /// brighter flash), and played through it reads as <em>no change at all</em>, which is how it
    /// was reported. The fix was to switch a whole new <em>kind</em> of thing on at each wave and
    /// keep the ones before it.
    /// </para>
    /// <para>
    /// The word ladder had exactly the same fault and nobody noticed, because the four rungs were
    /// each individually plausible: <c>16 + rung * 8</c> sparks, <c>.16 + rung * .07</c> of a
    /// flash, <c>8 + rung * 6</c> of a shake. GREAT and LEGENDARY drew <em>the same picture</em>
    /// at different sizes, so the biggest thing this mode can say landed as the smallest possible
    /// change — reported as the word feeling <em>dull</em>. A number going up is not something
    /// anybody sees; a thing that was not there before is.
    /// </para>
    /// <para>
    /// <b>And the escalation is heard as well as seen.</b> <see cref="Notes"/> is how far a run
    /// climbs, which is the one axis that cannot be mistaken for "the same thing, louder" — the
    /// game's own sound set was cut for it (every transposition in <c>sfx.tsv</c> is a pentatonic
    /// step precisely so overlapping notes cannot collide into a beat).
    /// </para>
    /// <para>
    /// <b>What is <em>not</em> a rung ladder is the gather.</b> Every rung draws breath before it
    /// lands, because that is what makes an impact an impact rather than an arrival — the same
    /// thing <see cref="BudTempo.Charge"/> is to a wave, and the beat this mode was missing the
    /// first time round. It comes out of <see cref="BudTempo.Fanfare"/> rather than being added
    /// beside it, so the word costs the chain exactly what it always did.
    /// </para>
    /// </summary>
    public static class BudAcclaim
    {
        /// <summary>Which rung each new kind of thing arrives on.</summary>
        public const int ShineFrom = 1, BloomFrom = 2, ConfettiFrom = 2, GroveFrom = 3;

        /// <summary>The most it ever draws at once, which is the top rung's whole set.</summary>
        public const int MostKinds = 5;

        /// <summary>The least and the most a word ever gathers for.</summary>
        public const float LeastGather = .16f, MostGather = .30f;

        public static BudHonours Of(int rung)
        {
            if (rung < 0) rung = 0;

            float gather = LeastGather + rung * .045f;
            if (gather > MostGather) gather = MostGather;

            int motes = 7 + rung * 4;

            // Three notes at the bottom and one more each rung, bounded by the scale itself —
            // the view plays one clip per note, so an unclamped rung is an unbounded number of
            // voices as well as a run that has run out of anywhere to climb to.
            int notes = 3 + rung;
            if (notes > MostNotes) notes = MostNotes;

            float tint = rung * .075f;
            if (tint > .26f) tint = .26f;

            float shove = .34f + rung * .14f;
            if (shove > .90f) shove = .90f;

            return new BudHonours(rung >= ShineFrom, rung >= BloomFrom, rung >= ConfettiFrom,
                                  rung >= GroveFrom, gather, motes, notes, tint, shove);
        }

        /// <summary>
        /// How long the word stands once it has landed.
        ///
        /// The gather is taken <em>out</em> of the fanfare rather than added beside it, which is
        /// what keeps the word costing the chain exactly what it did before — see
        /// <see cref="BudTempo.Burn"/>, which is the same bargain one level down.
        /// </summary>
        public static float Held(int rung)
        {
            float held = BudTempo.Fanfare - Of(rung).Gather;
            return held < .40f ? .40f : held;
        }

        /// <summary>
        /// The pentatonic steps a run climbs, in semitones.
        ///
        /// <b>Pentatonic because the run overlaps itself and everything else on the board.</b>
        /// A set with no semitone in it has no two notes that can collide into a beat, which is
        /// the same reason every transposition in <c>sfx.tsv</c> is one of these.
        /// </summary>
        static readonly int[] Steps = { 0, 2, 5, 7, 9, 12, 14, 17 };

        /// <summary>How many rungs the run has to climb before it repeats an octave.</summary>
        public static int MostNotes => Steps.Length;

        /// <summary>What the nth note of a run is played at, as a multiple of the clip's pitch.</summary>
        public static float NoteAt(int nth)
        {
            if (nth < 0) nth = 0;
            if (nth >= Steps.Length) nth = Steps.Length - 1;

            return (float)System.Math.Pow(2.0, Steps[nth] / 12.0);
        }

        /// <summary>How far apart the notes of a run are dealt.</summary>
        public const float NoteGap = .085f;

        /// <summary>
        /// And how long after the impact the climb begins.
        ///
        /// <b>The impact lands alone and the run rises out of it</b>, which is both the better
        /// reading and the cheaper one: `lit` is .28s long, so a run 85ms apart already stands
        /// three or four deep, and starting it on the same frame as the two impact notes puts
        /// six voices on one instant of a ten-voice pool that the board is still using.
        /// </summary>
        public const float NoteLead = .07f;
    }
}
