using System;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What a siege whose waves never stop answers about wave <em>n</em>, for any <em>n</em>.
    ///
    /// <para>
    /// <b>The seam between a lane and the board.</b> <see cref="SiegeEndless"/> was the one ramp
    /// this mode had and <see cref="SiegeLayout"/> held it by name, so a second lane whose waves
    /// also never stop - the Shuffle lane, which sends no bosses and climbs faster - would have
    /// been a second field on the layout and a second branch in every reading that asks "what is
    /// coming". A ramp is one abstract thing instead: the layout holds one, and
    /// <c>SizeOf</c>, <c>At</c>, <c>SurgeOf</c> and the two stone gates ask it rather than a
    /// named class. The Infinite lane reads <em>exactly</em> what it always did, because
    /// <see cref="SiegeEndless"/> is this with its own numbers in it.
    /// </para>
    /// <para>
    /// <b>Every answer is a pure function of the wave number and the layout's seed</b>, which is
    /// the whole of why a lane costs nothing: a level cannot author an infinite muster and a
    /// board cannot hold one, so the lane answers questions about wave <em>n</em> and the board's
    /// muster did not change at all (<c>SiegeBoard.Muster</c> asks the layout per raider).
    /// </para>
    /// </summary>
    public abstract class SiegeRamp
    {
        /// <summary>What wave <paramref name="wave"/> (1-based) sends.</summary>
        public abstract SiegeSpec[] WaveAt(int wave, uint seed);

        /// <summary>How much tougher wave <paramref name="wave"/> (1-based) is than the first.</summary>
        public abstract SiegeSurge SurgeFor(int wave);

        /// <summary>
        /// The wave a three-star run reaches, and the fraction of it a two-star run does.
        ///
        /// <b>Authored, because nothing can derive it.</b> Par everywhere else in this game is a
        /// search or an arithmetic floor over what a level sends; a lane that never ends sends
        /// everything, so how far is far is the one number a designer has to decide.
        /// </summary>
        public abstract int GoldWave { get; }

        public abstract float SilverFactor { get; }

        /// <summary>
        /// The wave the field starts dealing the cursed stone on, on a body that deals it at all
        /// (<see cref="SiegeLayout.CursesOn"/>), and the void stone's. A lane that never ends meets
        /// its rules in order rather than all at once.
        /// </summary>
        public abstract int CursesFrom { get; }

        public abstract int VoidsFrom { get; }

        /// <summary>
        /// How many waves a gate walks to prove the ramp sends nothing the line cannot answer.
        /// Far enough that every kind and every schedule the lane has has been met.
        /// </summary>
        public abstract int Proves { get; }

        /// <summary>
        /// Whether this lane ever sends a boss. A lane that promises pure raiders is held to it by
        /// the validator, which walks <see cref="Proves"/> waves and refuses one on this answer.
        /// </summary>
        public abstract bool SendsBosses { get; }

        /// <summary>
        /// One number out of a seed and a coordinate: FNV-1a with two avalanche steps.
        ///
        /// <b>A hash rather than a stream</b>, deliberately: a stream would make wave forty depend
        /// on how many rolls waves one to thirty-nine happened to take, so a rule change anywhere
        /// would move a hill somebody had already learned. Hashing the coordinate means each wave
        /// is dealt on its own. Shared by every ramp so the two lanes cannot come to disagree about
        /// what a roll is; what tells them apart is the <em>coordinates</em> they hash.
        /// </summary>
        protected static uint Roll(uint seed, uint at)
        {
            uint h = seed ^ 2166136261u;

            h ^= at;
            h = unchecked(h * 16777619u);
            h ^= h >> 15;
            h = unchecked(h * 2246822519u);
            h ^= h >> 13;

            return h;
        }
    }
}
