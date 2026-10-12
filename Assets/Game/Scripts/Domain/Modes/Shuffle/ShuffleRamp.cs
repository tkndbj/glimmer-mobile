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
        /// <b>The deep surge</b> (MODES.md 59c, 2026-10-12, the owner: "make waves come much stronger
        /// after wave 30, 40, 50 ..."). A build compounds - cards multiply each other - and a ramp
        /// that only adds is one a lucky build leaves behind for good, so from
        /// <see cref="SurgeFrom"/> every wave also <em>multiplies</em> health and blow by a per cent
        /// that itself climbs every <see cref="SurgeEvery"/> waves: 6% a wave through the thirties,
        /// 8% through the forties, 10% through the fifties. The ramp becomes a race between two
        /// growth rates, which only a build that compounds faster than the hill wins. Nothing
        /// before the surge moves, and no card is touched.
        /// </summary>
        public const int SurgeFrom = 30, SurgeEvery = 10;

        /// <summary>The deep surge's health: per cent a wave in its first stretch, and how many more each stretch after.</summary>
        public const int SurgeHealthPercent = 6, SurgeHealthPercentStep = 2;

        /// <summary>
        /// The deep surge's blow, gentler than its health because a ward is fourteen points and a
        /// blow that one-shoots it ends the argument the health was making.
        /// </summary>
        public const int SurgeBlowPercent = 2, SurgeBlowPercentStep = 1;

        /// <summary>
        /// <b>The pace surge</b> (2026-10-12): once health stands at <see cref="MostHealthTenths"/>,
        /// every wave after walks faster, by a per cent that climbs every <see cref="SurgeEvery"/>
        /// waves, up to <see cref="SurgePaceMostHundredths"/>. Without it the hill stops growing at
        /// the health ceiling and a build that clears one capped wave clears every one after it;
        /// with it the time a build has to kill shrinks until no build has enough. It starts where
        /// health stops, read off the tuning (<see cref="ShuffleRampTuning.HealthCeilingWave"/>),
        /// so retuning the health surge moves it with no second figure to keep in step.
        /// </summary>
        public const int SurgePacePercent = 4, SurgePacePercentStep = 2;

        /// <summary>The fastest the pace surge walks a raider, in hundredths of its own march: five times.</summary>
        public const int SurgePaceMostHundredths = 500;

        /// <summary>
        /// The most a surge may raise health, in tenths: five thousand times the plain figure.
        /// <b>Not a tuning, a contract with the board's integer arithmetic</b>, whose binding chain
        /// is a death derived from a body's full health: a chain-reaction blast at the widest a
        /// tuning allows (4.8 times the body, <c>SiegeBoard.Fallen</c>) landing on a hexed body
        /// (<c>SiegeTuning.Hexing</c>, x 1.2) must stay inside an int. The heaviest raider this
        /// lane sends (a bulwark, 700) carries three and a half million here.
        /// <c>ShuffleTuningTests.TheDeepestSurgeFitsTheBoardsArithmetic</c> walks every such chain off
        /// the card catalog and the tuning ranges, so a new card or a wider range that breaks it
        /// is a red test, not a wrapped number on a phone.
        /// </summary>
        public const int MostHealthTenths = 50_000;

        /// <summary>The most a surge may raise a blow, in tenths. A ward is fourteen points, so long before this a blow ends it.</summary>
        public const int MostBlowTenths = 100_000;

        /// <summary>
        /// The most any tuning may walk a raider, in hundredths of its own march: six times, so a
        /// creeper still takes nearly three seconds to cross and every frame of a crossing is a
        /// frame the line can fire in (a body that crosses inside a frame is one no rule saw).
        /// The ceiling on <see cref="ShuffleRampTuning.SurgePaceMostHundredths"/>'s range.
        /// </summary>
        public const int MostPaceHundredths = 600;

        /// <summary>
        /// How far <see cref="ShuffleRampTuning.HealthCeilingWave"/> looks for the ceiling. A wave
        /// no run reaches; a tuning whose health never gets there by it never speeds up.
        /// </summary>
        public const int CeilingSearch = 1_000_000;

        /// <summary>A surge's multiplier is carried in ten-thousandths between waves, so a wave's rounding never compounds.</summary>
        const long SurgeFine = 10_000;

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

            // A tenth more every other wave, then the deep surge.
            int blow = 10 + (wave - 1) / t.BlowEveryWaves;
            blow = Compound(blow, t.SurgeFrom, wave, t.SurgeEvery, t.SurgeBlowPercent, t.SurgeBlowPercentStep, MostBlowTenths);

            return new SiegeSurge(HealthAt(wave, t), blow, PaceAt(wave, t));
        }

        /// <summary>Wave <paramref name="wave"/>'s health, in tenths, on <paramref name="t"/>: the plain climb, then the deep surge.</summary>
        internal static int HealthAt(int wave, ShuffleRampTuning t)
        {
            if (wave < 1) wave = 1;

            // Twentieths folded to tenths with the remainder kept, so two waves never read the
            // same figure for want of a half: 10, 11, 13, 14, 16, 17, 19 ... Long, because the
            // plain climb alone passes an int at a wave far beyond any run.
            long plain = (20 + (wave - 1) * (long)t.HealthStepTwentieths) / 2;
            int health = (int)Math.Min(plain, MostHealthTenths);

            return Compound(health, t.SurgeFrom, wave, t.SurgeEvery, t.SurgeHealthPercent, t.SurgeHealthPercentStep, MostHealthTenths);
        }

        /// <summary>
        /// Wave <paramref name="wave"/>'s pace, in hundredths of a raider's own march, on
        /// <paramref name="t"/>: a hundred up to and including the wave health reaches its
        /// ceiling, then the pace surge.
        /// </summary>
        static int PaceAt(int wave, ShuffleRampTuning t)
        {
            const int ordinary = 100;

            int ceiling = t.HealthCeilingWave;
            if (ceiling == int.MaxValue || wave <= ceiling) return ordinary;

            return Compound(ordinary, ceiling + 1, wave, t.SurgeEvery, t.SurgePacePercent, t.SurgePacePercentStep,
                            t.SurgePaceMostHundredths);
        }

        /// <summary>
        /// The first wave whose health stands at <see cref="MostHealthTenths"/> on
        /// <paramref name="t"/>, or <see cref="int.MaxValue"/> when none does by
        /// <see cref="CeilingSearch"/>. Health never falls from one wave to the next, so this is a
        /// binary search; <see cref="ShuffleRampTuning"/> asks it once and keeps the answer.
        /// </summary>
        internal static int CeilingWaveOf(ShuffleRampTuning t)
        {
            if (HealthAt(CeilingSearch, t) < MostHealthTenths) return int.MaxValue;

            int lo = 1, hi = CeilingSearch;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (HealthAt(mid, t) >= MostHealthTenths) hi = mid;
                else lo = mid + 1;
            }

            return lo;
        }

        /// <summary>
        /// <paramref name="value"/> compounded from wave <paramref name="from"/>: wave <c>k</c>
        /// multiplies by <c>100 + percent + step x ((k - from) / every)</c> per cent, capped at
        /// <paramref name="most"/>, in whatever unit the value is kept in (tenths of health and
        /// blow, hundredths of pace). Whole numbers only (the project's rule for anything a health
        /// pool is measured against), carried in ten-thousandths so no wave's rounding compounds.
        /// The walk stops at the ceiling, so it is short at any wave a run can reach, and a rate
        /// of nought is the figure unchanged.
        /// </summary>
        static int Compound(int value, int from, int wave, int every, int percent, int step, int most)
        {
            if (value >= most) return most;
            if (wave < from || (percent <= 0 && step <= 0)) return value;

            long fine = value * SurgeFine;
            long ceiling = most * SurgeFine;

            for (long k = from; k <= wave && fine < ceiling; k++)
                fine = fine * (100 + percent + step * ((k - from) / every)) / 100;

            return (int)Math.Min(fine / SurgeFine, most);
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
