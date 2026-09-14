using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// How much harder a wave is than the first one.
    ///
    /// <b>Tenths and never a float</b>, for this project's own hard-won reason: everything this
    /// multiplies ends up in a health pool a bolt is measured against, and a threshold decided by
    /// a float is a number three code generators round three ways.
    /// </summary>
    public readonly struct SiegeSurge
    {
        /// <summary>The plain wave, worth exactly what a level authored.</summary>
        public static readonly SiegeSurge None = new SiegeSurge(10, 10);

        /// <summary>Health, in tenths of what this kind ordinarily carries.</summary>
        public readonly int HealthTenths;

        /// <summary>What a swing costs a ward, in tenths of what this kind ordinarily takes.</summary>
        public readonly int BlowTenths;

        public SiegeSurge(int healthTenths, int blowTenths)
        {
            HealthTenths = healthTenths < 10 ? 10 : healthTenths;
            BlowTenths = blowTenths < 10 ? 10 : blowTenths;
        }

        public bool IsNone => HealthTenths == 10 && BlowTenths == 10;

        /// <summary>This kind's health, surged.</summary>
        public int Health(int health) => health * HealthTenths / 10;

        /// <summary>This kind's blow, surged, and never nought once it was anything.</summary>
        public int Blow(int blow)
        {
            if (blow <= 0) return 0;

            int hit = blow * BlowTenths / 10;
            return hit < 1 ? 1 : hit;
        }
    }

    /// <summary>
    /// A siege whose waves never stop: what is coming at wave <em>n</em>, for any <em>n</em>.
    ///
    /// <para>
    /// <b>A pure function of the wave number rather than a generated list, and that is the whole
    /// of why it costs nothing.</b> A level cannot author an infinite muster and a board cannot
    /// hold one, so the layout answers questions about wave <em>n</em> instead of carrying an
    /// array of them — which means an endless level parses, validates, indexes and merges exactly
    /// like the ten-wave one beside it, and the board's <c>Muster</c> did not change at all.
    /// </para>
    /// <para>
    /// <b>The ramp is in the raiders, never in the rules.</b> Every constant this mode runs on
    /// stays where invariant 20d put it — a bolt is worth what it is worth on wave one and on wave
    /// ninety — and what climbs is what is walking down the hill: more of them, tougher, hitting
    /// harder. That is the only shape that can climb for ever without eventually contradicting a
    /// number some other level depends on, and it is why a run always ends: the line's output is
    /// bounded (a ward tops out at rank four) and the hill's is not.
    /// </para>
    /// <para>
    /// <b>The boss schedule is a rule, not a table.</b> Four bosses come round one at a time to
    /// wave sixteen, and after that they come in <em>pairs</em> — every unordered pair of the four,
    /// six of them, which is thirty waves before anything repeats. A player who reaches wave
    /// forty-six has met every fight this mode has and every combination of two of them, which is
    /// as far as a schedule can honestly go before it starts being the same wave with bigger
    /// numbers.
    /// </para>
    /// </summary>
    public sealed class SiegeEndless
    {
        /// <summary>
        /// A boss every fourth wave, while they still come one at a time.
        ///
        /// <b>Four, so each of the four bosses is met once before any of them comes back.</b> Three
        /// ordinary waves between them is enough for the line to be re-fuelled and for a cog or two
        /// to have been spent, which is what makes a boss a wall rather than an interruption.
        /// </summary>
        public const int BossEvery = 4;

        /// <summary>
        /// The wave after which bosses arrive two at a time.
        ///
        /// <b>Sixteen, because that is exactly when the four have each been met once</b>
        /// (4, 8, 12, 16). A pair before that would be a player's first meeting with two fights at
        /// once, and the whole argument for four different bosses (invariant 37z) is that each of
        /// them takes a different thing and has to be learned on its own.
        /// </b>
        /// </summary>
        public const int PairsAfter = 16;

        /// <summary>A pair every fifth wave once they come two at a time.</summary>
        public const int PairEvery = 5;

        /// <summary>
        /// The four fights this lane sends, in the order a player meets them.
        ///
        /// <para>
        /// <b>Gentlest first, and it is the first chapter's own order</b>: a blightcaller takes a
        /// ward's fire and can never bring one down, a warlord takes health, a warbringer shakes
        /// the whole line, an overlord takes the rank the player earned. Meeting them in that
        /// order is meeting them in order of what they cost.
        /// </para>
        /// <para>
        /// <b>Four of the six, and the two it leaves out are left out deliberately.</b> A gravemaw
        /// eats the cogs and bombs lying on the hill and a bonecaller raises creepers onto it
        /// (invariants 37bs, 37bt) — both are fine here and both would change what this lane
        /// <em>sends</em>, and its two star waves are guesses nobody has played against yet
        /// (invariant 43). Adding them is one line and a re-measure; adding them before the
        /// measurement exists would move a ladder nothing is holding.
        /// </para>
        /// </summary>
        public static readonly SiegeKind[] Bosses =
        {
            SiegeKind.Blightcaller,
            SiegeKind.Boss,
            SiegeKind.Warbringer,
            SiegeKind.Overlord,
        };

        /// <summary>
        /// Every unordered pair of the four, in the order they are sent.
        ///
        /// <b>Written out rather than derived from a double loop</b>, because the order is a
        /// design decision and not an enumeration order: the pairs climb from two that share no
        /// answer to two that both want a mending, so wave forty-six is the hardest of the six
        /// rather than whichever one a loop happened to reach last.
        /// </summary>
        public static readonly (int A, int B)[] Pairs =
        {
            (0, 1),   // douse + smite:    put the fire out, then take the health
            (0, 2),   // douse + rally:    a dark ward while the hill charges
            (0, 3),   // douse + sunder:   the cogs, and no fire to answer with
            (1, 2),   // smite + rally
            (1, 3),   // smite + sunder
            (2, 3),   // rally + sunder:   the whole line shaken and the best of it broken
        };

        /// <summary>
        /// How much health one wave adds over the last, in tenths.
        ///
        /// <b>Linear rather than compounding.</b> A multiplier applied per wave doubles every few
        /// waves and puts an endless run's ceiling inside a minute of the one before it, which
        /// makes every run the same length; a straight line means each wave is a little worse than
        /// the last for as long as a player can hold, which is what a score chase is for.
        /// </summary>
        public const int HealthStepTenths = 12;

        /// <summary>How much harder one wave swings than the last, in tenths.</summary>
        public const int BlowStepTenths = 4;

        /// <summary>
        /// What a wave sends before the ramp: raiders, and how many more arrive every two waves.
        ///
        /// Bounded by <see cref="SiegeLayout.MaxRaiders"/> like every other muster, because a wave
        /// is drawn on a hill of five lanes and a wave nobody can see the back of is a wave whose
        /// shape decides nothing.
        /// </summary>
        public const int FirstWave = 5, MostRaiders = 22;

        /// <summary>
        /// The wave a kind first appears on.
        ///
        /// <b>One rung apart, and the order is what each of them asks of the player.</b> A brute is
        /// more of the same and comes almost at once; a bulwark asks for the right <em>colour</em>;
        /// a weaver and a thief ask for the hill to be answered while the field is being taken
        /// away, which is the hardest thing this mode asks and so arrives last.
        /// </summary>
        public const int BrutesFrom = 3, BulwarksFrom = 6, BombersFrom = 9;

        /// <summary>
        /// The colours the hill wears. The level's own deal, so an endless lane on a three-colour
        /// field never sends a raider no ward can answer.
        /// </summary>
        public readonly string Colours;

        /// <summary>How often a fresh gem comes in as a cog, per hundred. See <c>SiegeLayout.Cogs</c>.</summary>
        public readonly int Cogs;

        /// <summary>
        /// The wave a three-star run reaches, and the fraction of it a two-star run does.
        ///
        /// <b>Authored, because nothing can derive it.</b> Par everywhere else in this game is a
        /// search or an arithmetic floor over what a level sends; an endless lane sends everything,
        /// so how far is far is the one number a designer has to decide — and it is decided by
        /// playing, which is what <c>SiegeRuleTests</c> and a device are for.
        /// </summary>
        public readonly int GoldWave;

        public readonly float SilverFactor;

        public SiegeEndless(string colours, int cogs, int goldWave, float silverFactor)
        {
            Colours = string.IsNullOrEmpty(colours) ? SiegeLayout.Letters : colours;
            Cogs = cogs < 0 ? 0 : cogs;
            GoldWave = goldWave < 1 ? 1 : goldWave;
            SilverFactor = silverFactor > 0f && silverFactor < 1f ? silverFactor : .55f;
        }

        // ------------------------------------------------------------------ the schedule
        /// <summary>Whether wave <paramref name="wave"/> (1-based) is a boss wave.</summary>
        public static bool IsBossWave(int wave)
        {
            if (wave < 1) return false;
            if (wave <= PairsAfter) return wave % BossEvery == 0;

            return (wave - PairsAfter) % PairEvery == 0;
        }

        /// <summary>
        /// The bosses wave <paramref name="wave"/> sends: none, one, or two.
        ///
        /// <b>The pairs repeat rather than stopping</b>, because an endless lane that ran out of
        /// schedule would have to invent something, and the honest thing to invent is the list
        /// again — by then the ramp has moved so far that the same pair is a different fight.
        /// </summary>
        public static void BossesAt(int wave, List<SiegeKind> into)
        {
            if (into == null) return;
            into.Clear();

            if (!IsBossWave(wave)) return;

            if (wave <= PairsAfter)
            {
                into.Add(Bosses[(wave / BossEvery - 1) % Bosses.Length]);
                return;
            }

            int step = (wave - PairsAfter) / PairEvery - 1;
            var pair = Pairs[((step % Pairs.Length) + Pairs.Length) % Pairs.Length];

            into.Add(Bosses[pair.A]);
            into.Add(Bosses[pair.B]);
        }

        /// <summary>How much tougher wave <paramref name="wave"/> is than the first.</summary>
        public static SiegeSurge SurgeAt(int wave)
        {
            if (wave < 1) wave = 1;

            return new SiegeSurge(10 + (wave - 1) * HealthStepTenths,
                                  10 + (wave - 1) * BlowStepTenths);
        }

        // ------------------------------------------------------------------ the muster
        /// <summary>
        /// What wave <paramref name="wave"/> (1-based) sends.
        ///
        /// <para>
        /// <b>Deterministic from the wave number and the level's own seed</b>, so two devices on
        /// the same endless lane meet the same hill in the same order — which is what makes a
        /// score comparable at all, and what lets a bug reported at wave thirty be a bug somebody
        /// else can reach.
        /// </para>
        /// <para>
        /// <b>A boss wave sends its boss and nothing else.</b> A duel stacked on a wave still
        /// swinging at the line is two fail states arriving together, which is what invariant 37t
        /// moved the quiet before a warlord to avoid — and on an endless lane, where the ramp
        /// never lets up, it is the difference between a fight and a pile-up.
        /// </para>
        /// </summary>
        public SiegeSpec[] WaveAt(int wave, uint seed)
        {
            if (wave < 1) wave = 1;

            var bosses = new List<SiegeKind>(2);
            BossesAt(wave, bosses);

            int size = FirstWave + (wave - 1) / 2;
            if (size > MostRaiders) size = MostRaiders;
            if (size > SiegeLayout.MaxRaiders) size = SiegeLayout.MaxRaiders;

            if (bosses.Count > 0)
            {
                // **A boss that takes no health arrives with an escort, and one of the four
                // does.** A duel is a wave of its own everywhere else (37t) because a warlord,
                // a warbringer and an overlord all shell the line for as long as they live — so
                // an empty hill is the *point*, and stacking one on a wave still swinging is two
                // fail states arriving together. A blightcaller takes a ward's **fire**
                // (<c>SiegeSpell.Douse</c>, 37z), and fire is worth exactly nothing with nothing
                // on the hill to burn: reported from play as "the first boss does no damage",
                // which was true and is invariant 5d — a wave that rejects no play decides
                // nothing however big the thing standing on it is.
                //
                // It is asked as <c>EndangersTheLine</c> rather than as "is this a blightcaller",
                // because that predicate already exists for this exact question one level up
                // (<c>ModeValidator.Threatens</c>), and a fifth boss that took something other
                // than health would need this answer without anybody remembering to come back.
                //
                // **The authored ladder asks the same predicate and answers it by *merging*
                // rather than escorting** (<c>SiegeLayout</c>'s constructor): a boss that cannot
                // bring a ward down is stood at the head of the last authored wave instead of
                // being given one of its own. Two shapes, one rule, because the lanes differ —
                // this one derives its waves and has spare raiders to hand, that one authors
                // them and merging costs par nothing. What neither can use is a *shorter quiet*:
                // the muster fires the moment the hill is clear, so a player who is ahead meets a
                // lone boss however long the clock says (37k). That was believed to be the
                // authored ladder's answer, was written down as such, and was measured on
                // `s01_stonewatch` to be no answer at all.
                bool bites = false;
                for (int i = 0; i < bosses.Count; i++)
                    if (SiegeTuning.EndangersTheLine(bosses[i])) bites = true;

                int escort = bites ? 0 : size;
                if (escort > SiegeLayout.MaxRaiders - bosses.Count)
                    escort = SiegeLayout.MaxRaiders - bosses.Count;
                if (escort < 0) escort = 0;

                var duel = new SiegeSpec[bosses.Count + escort];
                for (int i = 0; i < bosses.Count; i++)
                    duel[i] = new SiegeSpec(Colour(seed, wave, i), bosses[i]);

                // **The escort is dealt from the same rolls the ordinary wave would have used**,
                // offset past the bosses, so the hill a blightcaller stands in is the hill this
                // wave was always going to send rather than a second thing to tune.
                for (int i = 0; i < escort; i++)
                    duel[bosses.Count + i] =
                        new SiegeSpec(Colour(seed, wave, bosses.Count + i),
                                      KindAt(wave, bosses.Count + i, seed));

                return duel;
            }

            var made = new SiegeSpec[size];
            for (int i = 0; i < size; i++)
                made[i] = new SiegeSpec(Colour(seed, wave, i), KindAt(wave, i, seed));

            return made;
        }

        /// <summary>
        /// What the <paramref name="index"/>-th raider of this wave is.
        ///
        /// <para>
        /// <b>At most one weaver and one thief a wave, and never both before they have each been
        /// met alone.</b> Two of a kind on one hill double the pressure and share one answer
        /// (<c>SiegeBoard.Unmeddle</c> only gives the field back when the last of them is gone), so
        /// a wave that dealt three weavers would be a wave whose field is simply gone. The cap is
        /// what keeps the ramp in the numbers rather than in a cliff.
        /// </para>
        /// </summary>
        SiegeKind KindAt(int wave, int index, uint seed)
        {
            uint roll = Roll(seed, unchecked((uint)wave * 131u + (uint)index * 17u));

            // **A bomber where the weaver and the thief used to be.** Those two were withdrawn
            // whole (invariant 40h); what the lane still wants at this depth is a raider that
            // leaves something behind, and a bomb is the only one there is.
            if (wave >= BombersFrom && index == 1) return SiegeKind.Bomber;

            if (wave >= BulwarksFrom && roll % 100u < Share(wave, BulwarksFrom, 30u))
                return SiegeKind.Bulwark;

            if (wave >= BrutesFrom && roll % 100u < Share(wave, BrutesFrom, 55u))
                return SiegeKind.Brute;

            return SiegeKind.Creeper;
        }

        /// <summary>
        /// How much of a wave a kind takes, per hundred: none before it arrives, then climbing to
        /// a ceiling.
        ///
        /// <b>A ceiling rather than a straight line</b>, because a hill that is eventually all
        /// bulwarks is a hill whose colour question has one answer — which is invariant 5d asked
        /// of a ramp: a wave that rejects no play decides nothing however tough it is.
        /// </b>
        /// </summary>
        static uint Share(int wave, int from, uint ceiling)
        {
            uint grown = (uint)(wave - from) * 4u;
            return grown > ceiling ? ceiling : grown;
        }

        /// <summary>The colour this raider wears, drawn from the level's own deal.</summary>
        char Colour(uint seed, int wave, int index)
            => Colours[(int)(Roll(seed, unchecked((uint)wave * 977u + (uint)index * 61u))
                         % (uint)Colours.Length)];

        /// <summary>
        /// One number out of the level's seed and a coordinate.
        ///
        /// <b>A hash rather than a stream</b>, deliberately: a stream would make wave forty depend
        /// on how many rolls waves one to thirty-nine happened to take, so a rule change anywhere
        /// would move a hill somebody had already learned. Hashing the coordinate means each wave
        /// is dealt on its own.
        /// </summary>
        static uint Roll(uint seed, uint at)
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
