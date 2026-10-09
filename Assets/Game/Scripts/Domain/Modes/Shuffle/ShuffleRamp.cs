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
    /// is a fight the player answers with the line they chose; on this lane the line is dealt
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

        public override int CursesFrom => CursedWave;

        public override int VoidsFrom => VoidWave;

        public override int Proves => Walked;

        public override bool SendsBosses => false;

        /// <summary>How much tougher wave <paramref name="wave"/> (1-based) is than the first.</summary>
        public static SiegeSurge SurgeAt(int wave)
        {
            if (wave < 1) wave = 1;

            // Twentieths folded to tenths with the remainder kept, so two waves never read the
            // same figure for want of a half: 10, 11, 13, 14, 16, 17, 19 ...
            int health = (20 + (wave - 1) * HealthStepTwentieths) / 2;
            int blow = 10 + (wave - 1) / BlowEveryWaves;

            return new SiegeSurge(health, blow);
        }

        public override SiegeSurge SurgeFor(int wave) => SurgeAt(wave);

        /// <summary>How many raiders wave <paramref name="wave"/> (1-based) sends.</summary>
        public static int SizeAt(int wave)
        {
            if (wave < 1) wave = 1;

            int size = FirstWave + (wave - 1);
            if (size > MostRaiders) size = MostRaiders;
            if (size > SiegeLayout.MaxRaiders) size = SiegeLayout.MaxRaiders;

            return size;
        }

        public override SiegeSpec[] WaveAt(int wave, uint seed)
        {
            if (wave < 1) wave = 1;

            int size = SizeAt(wave);
            var made = new SiegeSpec[size];

            for (int i = 0; i < size; i++)
                made[i] = new SiegeSpec(Colour(seed, wave, i), KindAt(wave, i, seed));

            return made;
        }

        /// <summary>
        /// What the <paramref name="index"/>-th raider of this wave is: brutes and bulwarks by a
        /// share that climbs to a ceiling, so the hill is never all armour (invariant 5d asked of
        /// a ramp), and never a bomber.
        /// </summary>
        public static SiegeKind KindAt(int wave, int index, uint seed)
        {
            uint roll = Roll(seed, unchecked((uint)wave * 1543u + (uint)index * 37u));

            if (wave >= BulwarksFrom && roll % 100u < Share(wave, BulwarksFrom, 35u))
                return SiegeKind.Bulwark;

            if (wave >= BrutesFrom && roll % 100u < Share(wave, BrutesFrom, 55u))
                return SiegeKind.Brute;

            return SiegeKind.Creeper;
        }

        static uint Share(int wave, int from, uint ceiling)
        {
            uint grown = (uint)(wave - from) * 5u;
            return grown > ceiling ? ceiling : grown;
        }

        char Colour(uint seed, int wave, int index)
            => Colours[(int)(Roll(seed, unchecked((uint)wave * 1009u + (uint)index * 73u))
                         % (uint)Colours.Length)];
    }
}
