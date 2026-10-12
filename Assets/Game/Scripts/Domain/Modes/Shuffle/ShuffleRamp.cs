using System;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// The Shuffle lane's waves: pure raiders, more of them every wave, every one of them
    /// tougher than the wave before.
    ///
    /// <para>
    /// <b>No bosses, and the validator holds it to that</b> (<see cref="SendsBosses"/>). A boss
    /// is a fight the player answers with the line they chose; on this lane the build is dealt
    /// and the fight is the ramp itself, so what climbs is the crowd. The lane leans on its
    /// build the way the Infinite lane leans on its cogs: a run that picks well goes further,
    /// and a run that picks badly meets the wall sooner.
    /// </para>
    /// <para>
    /// <b>Steeper than the Infinite lane on purpose.</b> That lane climbs a tenth a wave
    /// against a bought line and tops out at twenty-two bodies; this one climbs faster and
    /// grows a body a wave, because a build of forty cards compounds and a ramp that did not
    /// would be a run that never ended. The figures were swept with a model player taking
    /// random cards (<c>ShuffleHoldTests</c>), which is this lane's one instrument.
    /// </para>
    /// <para>
    /// <b>Its own coordinates under the shared hash</b> (<see cref="SiegeRamp.Roll"/>), so a
    /// Shuffle field is never the Infinite lane's hill in a different order.
    /// </para>
    /// <para>
    /// <b>Tunable between builds, and the constants below are what ships</b> (MODES.md 59l). The
    /// ramp answers with the figures it was last handed (<see cref="Use"/>, by the screen when a
    /// run opens, off <c>ShuffleConfig</c>'s snapshot); until then, and in every gate and
    /// fixture, it answers with <see cref="ShuffleRampTuning.Shipped"/>, which is these constants.
    /// The static readings (<see cref="SurgeAt(int)"/>, <see cref="SizeAt(int)"/>,
    /// <see cref="KindAt(int, int, uint)"/>) are the shipped ramp and nothing else, which is what
    /// the offline mirror and its pins hold.
    /// </para>
    /// </summary>
    public sealed class ShuffleRamp : SiegeRamp
    {
        /// <summary>What wave one sends, and the most any wave sends.</summary>
        public const int FirstWave = 4, MostRaiders = 24;

        /// <summary>
        /// How much health a wave adds over the last, in <em>twentieths</em> - three a wave is
        /// 15%, which is half again the Infinite lane's step - and how much harder it swings, in
        /// tenths every other wave.
        /// </summary>
        public const int HealthStepTwentieths = 3;

        /// <summary>Waves between one tenth of blow and the next.</summary>
        public const int BlowEveryWaves = 2;

        /// <summary>
        /// The wave a kind first appears on, one rung apart as on the Infinite lane.
        ///
        /// <b>No bombers, ever, and no cogs</b> (the body authors <c>cogs: 0</c>): nothing on
        /// this lane drops on the hill's ground - the owner's call, 2026-10-09. The hand is the
        /// whole ladder, and a hill with nothing to reach for keeps the eye on the cards. The
        /// validator and <c>ShuffleTests</c> hold the ramp to it by walking every wave.
        /// </summary>
        public const int BrutesFrom = 2, BulwarksFrom = 4;

        /// <summary>
        /// The most of a wave, per hundred, a bulwark and a brute may be, and how much nearer
        /// each wave brings them (per hundred a wave, from the wave the kind arrives on). A roll
        /// under the bulwarks' share is a bulwark and one under the brutes' a brute, so the
        /// brutes' share is read over the bulwarks', not beside it.
        /// </summary>
        public const int BruteShareMost = 55, BulwarkShareMost = 35, ShareStep = 5;

        /// <summary>The two stones, met in the chapters' order: the curse first.</summary>
        public const int CursedWave = 10, VoidWave = 16;

        /// <summary>How far a gate walks: far enough for every kind, the stones and the ceiling.</summary>
        public const int Walked = 40;

        public readonly string Colours;

        public ShuffleRamp(string colours, int goldWave, float silverFactor)
        {
            Colours = string.IsNullOrEmpty(colours) ? SiegeLayout.Letters : colours;
            GoldWave = goldWave < 1 ? 1 : goldWave;
            SilverFactor = silverFactor > 0f && silverFactor < 1f ? silverFactor : .55f;
        }

        public override int GoldWave { get; }

        public override float SilverFactor { get; }

        /// <summary>The figures this ramp is answering with. See the class note.</summary>
        public ShuffleRampTuning Tuning { get; private set; } = ShuffleRampTuning.Shipped;

        int _edition;

        /// <summary>
        /// Answers with <paramref name="tuning"/> from now on (the shipped figures for null), and
        /// moves <see cref="Edition"/> when that changes anything, so a wave the layout memoised
        /// under other figures is dealt again. Called by the Shuffle screen as a run opens,
        /// before its board is built - never during a run.
        /// </summary>
        public void Use(ShuffleRampTuning tuning)
        {
            tuning = tuning ?? ShuffleRampTuning.Shipped;
            if (ReferenceEquals(tuning, Tuning)) return;

            Tuning = tuning;
            _edition++;
        }

        public override int Edition => _edition;

        public override int CursesFrom => Tuning.CursedWave;

        public override int VoidsFrom => Tuning.VoidWave;

        public override int Proves => Walked;

        public override bool SendsBosses => false;

        /// <summary>How much tougher wave <paramref name="wave"/> (1-based) is than the first, on the shipped ramp.</summary>
        public static SiegeSurge SurgeAt(int wave) => SurgeAt(wave, ShuffleRampTuning.Shipped);

        /// <summary>How much tougher wave <paramref name="wave"/> (1-based) is than the first, on <paramref name="t"/>.</summary>
        public static SiegeSurge SurgeAt(int wave, ShuffleRampTuning t)
        {
            if (wave < 1) wave = 1;
            t = t ?? ShuffleRampTuning.Shipped;

            // Twentieths folded to tenths with the remainder kept, so two waves never read the
            // same figure for want of a half: 10, 11, 13, 14, 16, 17, 19 ...
            int health = (20 + (wave - 1) * t.HealthStepTwentieths) / 2;
            int blow = 10 + (wave - 1) / t.BlowEveryWaves;

            return new SiegeSurge(health, blow);
        }

        public override SiegeSurge SurgeFor(int wave) => SurgeAt(wave, Tuning);

        /// <summary>How many raiders wave <paramref name="wave"/> (1-based) sends, on the shipped ramp.</summary>
        public static int SizeAt(int wave) => SizeAt(wave, ShuffleRampTuning.Shipped);

        /// <summary>How many raiders wave <paramref name="wave"/> (1-based) sends, on <paramref name="t"/>.</summary>
        public static int SizeAt(int wave, ShuffleRampTuning t)
        {
            if (wave < 1) wave = 1;
            t = t ?? ShuffleRampTuning.Shipped;

            int size = t.FirstWave + (wave - 1);
            if (size > t.MostRaiders) size = t.MostRaiders;
            if (size > SiegeLayout.MaxRaiders) size = SiegeLayout.MaxRaiders;

            return size;
        }

        public override SiegeSpec[] WaveAt(int wave, uint seed)
        {
            if (wave < 1) wave = 1;

            var t = Tuning;
            int size = SizeAt(wave, t);
            var made = new SiegeSpec[size];

            for (int i = 0; i < size; i++)
                made[i] = new SiegeSpec(Colour(seed, wave, i), KindAt(wave, i, seed, t));

            return made;
        }

        /// <summary>
        /// What the <paramref name="index"/>-th raider of this wave is: brutes and bulwarks by a
        /// share that climbs to a ceiling, so the hill is never all armour (invariant 5d asked of
        /// a ramp), and never a bomber.
        /// </summary>
        public static SiegeKind KindAt(int wave, int index, uint seed)
            => KindAt(wave, index, seed, ShuffleRampTuning.Shipped);

        /// <summary>What the <paramref name="index"/>-th raider of this wave is, on <paramref name="t"/>.</summary>
        public static SiegeKind KindAt(int wave, int index, uint seed, ShuffleRampTuning t)
        {
            t = t ?? ShuffleRampTuning.Shipped;
            uint roll = Roll(seed, unchecked((uint)wave * 1543u + (uint)index * 37u));

            if (wave >= t.BulwarksFrom && roll % 100u < Share(wave, t.BulwarksFrom, t.BulwarkShareMost, t.ShareStep))
                return SiegeKind.Bulwark;

            if (wave >= t.BrutesFrom && roll % 100u < Share(wave, t.BrutesFrom, t.BruteShareMost, t.ShareStep))
                return SiegeKind.Brute;

            return SiegeKind.Creeper;
        }

        static uint Share(int wave, int from, int ceiling, int step)
        {
            uint grown = (uint)(wave - from) * (uint)step;
            return grown > (uint)ceiling ? (uint)ceiling : grown;
        }

        char Colour(uint seed, int wave, int index)
            => Colours[(int)(Roll(seed, unchecked((uint)wave * 1009u + (uint)index * 73u))
                         % (uint)Colours.Length)];
    }
}
