using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What a Thornwatch level authors: the gem field, the colours it refills from, the ward
    /// line standing in front of it, and the raiders walking down at it.
    ///
    /// <para>
    /// <b>Four things and no numbers</b>, which is invariant 20d's rule read across. How much
    /// fuel a match is worth, how hard a bolt hits, how fast a ward's fuel fades, how long a
    /// raider takes to cross the hill and what a blow costs a ward are all <see cref="SiegeTuning"/>
    /// — constants in code, one place, retuned for every level at once. A level says what is
    /// standing there and what is coming; the mode says what any of it does.
    /// </para>
    /// <para>
    /// <b>This is the one mode in this game that runs on a clock, and that is a deliberate
    /// departure worth naming rather than hiding.</b> Everything else here is turn-based and
    /// searchable: a move is a layer of a breadth-first walk, par is the depth of the first
    /// layer that wins, and both star lines fall out of it (invariant 20j). Raiders that walk
    /// while nobody is touching the board have no state graph, so par cannot be searched — it is
    /// <see cref="SiegeTuning.Par"/>, arithmetic over the raiders a level authors. Note what that
    /// does <em>not</em> change: a run is still graded on a count the player spends (matches), the
    /// star lines are still the same multiples of par every other mode uses, the fail state is
    /// still real, and the level is still an ordinary level with its own permanent id — so this
    /// mode cost the save file no schema version, no merge rule and no server work (invariant
    /// 20a), and can be withdrawn for the price of a chapter body exactly as five modes before it
    /// were.
    /// </para>
    /// </summary>
    public sealed class SiegeLayout
    {
        /// <summary>Gems the field is drawn with. Four, so a ward line of four has one each.</summary>
        public const string Letters = "rgby";

        /// <summary>
        /// Everything a cell of the field may hold: the four gems, and nothing else.
        ///
        /// <para>
        /// <b>The field is gems again.</b> It used to carry the cog as a second alphabet, which
        /// was the right shape for a cog that stood <em>on the field</em> — every rule that reads
        /// a cell is asking either <em>what colour is this</em> or <em>what is standing here</em>,
        /// and one alphabet answers the first with a thing that has no colour. A cog is dropped by
        /// a raider now (<c>SiegeBoard.Drop</c>), so the second question has no second answer and
        /// the split is gone with the thing that needed it.
        /// </para>
        /// <para>
        /// It is kept as a name rather than folded into <see cref="Letters"/>, because every
        /// caller asking "what may a cell hold" should keep asking this and not a constant that
        /// happens to agree with it today.
        /// </para>
        /// </summary>
        public const string Cells = Letters;

        /// <summary>
        /// <b>Retired: <c>'*'</c> was a cog standing on the field and is refused by name.</b>
        ///
        /// <para>
        /// A cog is dropped by a felled raider and lies on the hill; nothing puts one in a cell
        /// any more. The letter is refused rather than ignored for the duskcap's reason
        /// (invariant 5f): a chapter body carrying one was authored for a build that no longer
        /// exists, and quietly reading it as a gem would ship a field nobody composed.
        /// </para>
        /// </summary>
        public const char RetiredCog = '*';

        /// <summary>What a ward may be. The same four, because a ward is fuelled by its own colour.</summary>
        public const string WardLetters = "rgby";

        /// <summary>
        /// What a wave may hold: a creeper in lower case, a brute in upper.
        ///
        /// One letter per raider rather than a count and a kind, so a wave reads as the thing
        /// that is coming — <c>"rrGb"</c> is two creepers, a brute and a creeper, in that order,
        /// and its shape is visible in the file.
        /// </summary>
        public const string RaiderLetters = "rgbyRGBY";

        /// <summary>
        /// Written before a colour letter, it makes that raider a <see cref="SiegeKind.Bulwark"/>:
        /// <c>"rg#by"</c> is a red creeper, a green creeper, a shielded blue one and a yellow
        /// creeper.
        ///
        /// <para>
        /// <b>A modifier rather than four more letters, and that is what made the parse change
        /// worth doing.</b> Case already carries one axis (a brute is a capital), so a third kind
        /// has nowhere to go in a one-character-per-raider alphabet — and the obvious repair, four
        /// new letters, spends the alphabet on a kind and leaves the next one with the same
        /// problem. A prefix costs one rule and any number of future kinds.
        /// </para>
        /// <para>
        /// <b>What it cost is the assumption that a wave string's length is its raider count</b>,
        /// which six places made — <see cref="RaiderCount"/>, two clauses of <see cref="Check"/>,
        /// the spawn loop and two readings in <c>ModeValidator</c>. Every one of them would have
        /// gone on compiling and quietly counted a shielded raider twice, so the string is now
        /// parsed exactly once into <see cref="Coming"/> and nothing asks the raw text how many
        /// raiders it holds.
        /// </para>
        /// </summary>
        public const char Shield = '#';

        /// <summary>
        /// Written before a colour letter, it makes that raider a <see cref="SiegeKind.Bomber"/>:
        /// <c>"rg!b"</c> is two creepers and a blue bomber.
        /// </summary>
        public const char Drop = '!';

        /// <summary>
        /// Every prefix a wave may carry, and the kind each one names.
        ///
        /// <para>
        /// <b>A table rather than three <c>if</c>s, and that is what the third modifier bought.</b>
        /// The shield shipped as a special case in four places — the alphabet, the sweep, the
        /// parse and the messages — and every one of them would have had to be extended twice
        /// more, in step, by hand. A modifier is now a row here and nothing else, so a fourth
        /// costs one line and cannot be half-added.
        /// </para>
        /// </summary>
        public static readonly (char Mark, SiegeKind Kind)[] Modifiers =
        {
            (Shield, SiegeKind.Bulwark),
            (Drop,   SiegeKind.Bomber),
        };

        /// <summary>Whether this character is a modifier rather than a raider.</summary>
        public static bool IsModifier(char mark)
        {
            for (int i = 0; i < Modifiers.Length; i++)
                if (Modifiers[i].Mark == mark) return true;

            return false;
        }

        /// <summary>What kind this modifier names, or <see cref="SiegeKind.Creeper"/>.</summary>
        public static SiegeKind Modified(char mark)
        {
            for (int i = 0; i < Modifiers.Length; i++)
                if (Modifiers[i].Mark == mark) return Modifiers[i].Kind;

            return SiegeKind.Creeper;
        }

        /// <summary>Everything a wave may be written with: the raiders, and every modifier.</summary>
        public static readonly string WaveLetters = Marks();

        static string Marks()
        {
            var all = new System.Text.StringBuilder(RaiderLetters);
            for (int i = 0; i < Modifiers.Length; i++) all.Append(Modifiers[i].Mark);
            return all.ToString();
        }

        /// <summary>
        /// <b>Retired: nothing writes either of these and the two characters must never be
        /// reused as a cell.</b>
        ///
        /// <c>'%'</c> was a sack, left where a thief took a gem; <c>'!'</c> was a bomb standing on
        /// the field. Both belonged to the withdrawn idea of the hill reaching into the gem board
        /// (invariant 40h). A bomb now stands on the <em>hill</em> and is not a cell at all - see
        /// <see cref="SiegeBomb"/>. Written down because a file carrying one is a file written
        /// against rules this build does not have, which is the duskcap's reason (invariant 5f).
        /// </summary>
        public const char RetiredSack = '%', RetiredBomb = '!';

        /// <summary>
        /// How a level names its boss: <c>"warlord:r"</c> — a <b>kind</b> and the colour it wears.
        ///
        /// <para>
        /// <b>It was one letter whose case said which of two bosses this was, and that stopped
        /// working the moment there were four.</b> The creeper/brute idiom — capitalise the letter
        /// for the bigger one — is exactly right for two kinds and has nowhere to go for a third:
        /// with four bosses a case bit cannot name one, and the two warlords a chapter shipped
        /// were told apart by <em>nothing but hue</em>, which is what a player reported as "the
        /// bosses look exactly the same". A boss is a way of fighting rather than a size, so the
        /// level says which one out loud.
        /// </para>
        /// <para>
        /// <b>Spelled rather than lettered, and that is the one place this mode's terseness is
        /// wrong.</b> Everything else a siege authors is a grid or a wave — a shape whose meaning
        /// is that it is read at a glance — and this is a single scalar naming a thing. A reader
        /// of the chapter body sees <c>"warbringer:y"</c> and knows what ships; they would not
        /// have known what <c>"Wy"</c> was.
        /// </para>
        /// <para>
        /// <b>The old one-letter form is refused rather than reinterpreted</b> (invariant 5f's
        /// rule): a file carrying <c>"boss": "r"</c> was written for a build that no longer
        /// exists, and salvaging a red warlord out of it would ship a fight nobody authored.
        /// </para>
        /// </summary>
        public static readonly (string Name, SiegeKind Kind)[] BossNames =
        {
            ("blightcaller", SiegeKind.Blightcaller),
            ("warlord", SiegeKind.Boss),
            ("warbringer", SiegeKind.Warbringer),
            ("overlord", SiegeKind.Overlord),
            ("gravemaw", SiegeKind.Gravemaw),
            ("bonecaller", SiegeKind.Bonecaller),
            ("shackler", SiegeKind.Shackler),
            ("ironclad", SiegeKind.Ironclad),
        };

        /// <summary>The colour a boss may wear. Lower case only — case no longer means anything.</summary>
        public const string BossColours = "rgby";

        /// <summary>How many wards a line may hold. Four colours, so four is the whole line.</summary>
        public const int MaxWards = 4;

        /// <summary>
        /// The fewest wards — and so the fewest gem colours — a siege may stand.
        ///
        /// <para>
        /// <b>Three, and it is a measurement rather than a preference.</b> Under the colour lock a
        /// field may only ever deal colours the line can burn (see <see cref="Check"/>), so the
        /// ward count <em>is</em> the colour count — and a two-colour match-three is not a board.
        /// Measured over twenty thousand dealt seeds: <b>not one</b> two-colour field is settled
        /// (three alike are always already touching, so it would go off before anybody moved a
        /// gem), and a match on one clears <b>202</b> gems against a four-colour field's 5.6 —
        /// the refill lands beside its own kind so often that the board cascades until it runs out
        /// of things to remove.
        /// </para>
        /// <para>
        /// So an opening rung teaches the lock on <b>three</b>, which is the fewest that behaves
        /// like a jewel board: 0.5% of seeds are settled and a match clears 9.5 gems. Par is
        /// deliberately <em>not</em> corrected for that, and the reason is worth reading before
        /// anybody corrects it — see the note beside <see cref="SiegeTuning.MatchGemsTenths"/>.
        /// </para>
        /// </summary>
        public const int MinWards = 3;

        /// <summary>The most raiders one level may author. A bound on the run, not a taste.</summary>
        public const int MaxRaiders = 60;

        /// <summary>The opening field, exactly as it is dealt. Authored settled — no run of three.</summary>
        public readonly ProtoGrid Grid;

        /// <summary>
        /// The colours the field refills from, in the order they are written.
        ///
        /// An alphabet rather than a queue: a match-three board that refills has no fixed future
        /// whichever way the next gem is chosen, so nothing is bought by making the stream
        /// authorable. What it does buy is a level that can leave a colour <em>out</em>, which is
        /// the one thing about a refill an author might genuinely want to say.
        /// </summary>
        public readonly string Deal;

        /// <summary>The ward line, left to right. One colour each, and no two the same.</summary>
        public readonly char[] Wards;

        /// <summary>
        /// The waves, in the order they come — <b>including the warlord's</b>, which is appended
        /// rather than authored.
        ///
        /// See <see cref="Boss"/> for why a level says only <em>whether</em> there is one.
        /// </summary>
        public readonly string[] Waves;

        /// <summary>
        /// The warlord that comes last, as a colour letter, or <c>'\0'</c> for a siege that sends
        /// none.
        ///
        /// <para>
        /// <b>A level says whether there is one and what colour it wears, and never which wave it
        /// is in</b> — the last wave <em>is</em> the boss wave, by rule, so it cannot be authored
        /// into the middle of a siege and cannot be left off the end of one. That is the same
        /// bargain invariant 33g strikes with the haul-road and invariant 4a with the manifest:
        /// where a fact can be derived from a shape rather than typed beside it, the two can never
        /// come apart.
        /// </para>
        /// <para>
        /// <b>It is still a wave, and that is what makes it cost nothing.</b> A warlord is
        /// appended to <see cref="Waves"/> as a one-raider wave, so the wave count the header
        /// reads, the muster, the banner, <see cref="RaiderCount"/> and
        /// <see cref="SiegeTuning.Par"/> all take it without a single special case — the only
        /// question anything has to ask is <see cref="BossWave"/>, and only because a warlord's
        /// health and its way of fighting are not a creeper's.
        /// </para>
        /// <para>
        /// <b>A boss that cannot bring a ward down does not get a wave of its own; it is stood at
        /// the head of the last authored one.</b> A warlord, a warbringer and an overlord shell
        /// the line for as long as they live, so an empty hill is the point (invariant 37t); a
        /// blightcaller takes a ward's <em>fire</em>, which is worth exactly what there is to
        /// burn, so alone it costs nothing at all — invariant 5d, and reported from play twice.
        /// The predicate is <see cref="SiegeTuning.EndangersTheLine"/>, the same one the endless
        /// lane's escort asks (invariant 43), and it costs par nothing because the company is the
        /// raiders the level already sends. See the constructor.
        /// </para>
        /// </summary>
        public readonly char Boss;

        /// <summary>
        /// Which of the four bosses this siege ends with, or <see cref="SiegeKind.Creeper"/> for
        /// one that sends none.
        ///
        /// <b>Held here rather than read off the letter</b>, because the letter is a colour and a
        /// colour cannot say what a thing does. Everything that used to ask <c>char.IsUpper</c>
        /// asks <see cref="KindAt"/>, which is the one place that knows which wave is the boss's.
        /// </summary>
        public readonly SiegeKind BossKind = SiegeKind.Creeper;

        /// <summary>
        /// Which wave the boss stands in, or -1. Always the last one when there is one — either a
        /// wave of its own, or, for a boss that cannot bring a ward down, the last authored wave
        /// with the boss at its head. <see cref="BossesIn"/> says how many of that wave are
        /// bosses; the boss itself is always index nought.
        /// </summary>
        public readonly int BossWave = -1;

        /// <summary>Whether this siege ends with a boss of any of the four kinds.</summary>
        public bool HasBoss => Boss != '\0';

        /// <summary>
        /// What kind of raider stands at <paramref name="index"/> of wave <paramref name="wave"/>.
        ///
        /// <b>The wave decides, never the letter</b>, and the layout is the only thing that knows
        /// which wave is the boss's — so this exists once and every caller (par, the muster, the
        /// build gate's threat reading, the offline mirror) asks it rather than forming a second
        /// opinion about a fact already written down.
        /// </summary>
        public SiegeKind KindAt(int wave, int index)
        {
            if (!IsEndless && wave == BossWave && index == 0
                && wave >= 0 && wave < Coming.Length) return BossKind;

            var spec = At(wave, index);
            return spec.Kind;
        }

        /// <summary>
        /// Which colour the raider at <paramref name="index"/> of <paramref name="wave"/> wears,
        /// as an index into <see cref="Letters"/>, or -1.
        /// </summary>
        public int ColourAt(int wave, int index) => Letters.IndexOf(At(wave, index).Colour);

        /// <summary>
        /// How many of wave <paramref name="wave"/>'s raiders are bosses.
        ///
        /// <b>Asked rather than assumed, because it is no longer the wave's size.</b> It was, for
        /// as long as a boss wave held nothing but bosses — an endless lane's pair wave, an
        /// authored ladder's appended duel — and a boss that rides the last authored wave
        /// (invariant 43's escort, in this lane's idiom) breaks that identity. <c>Muster</c> hands
        /// this to <see cref="SiegeTuning.BossLane"/>, which decides whether a boss stands in the
        /// middle of the hill or beside it, and handing it the wave's size instead would have put
        /// a lone blightcaller in a pair's lane the moment it had company.
        /// </summary>
        public int BossesIn(int wave)
        {
            int bosses = 0;
            for (int i = 0; i < SizeOf(wave); i++)
                if (SiegeTuning.IsBoss(KindAt(wave, i))) bosses++;

            return bosses;
        }

        /// <summary>How many raiders wave <paramref name="wave"/> holds.</summary>
        public int SizeOf(int wave)
        {
            if (wave < 0) return 0;
            if (IsEndless) return Generated(wave).Length;

            return wave >= Coming.Length ? 0 : Coming[wave].Length;
        }

        /// <summary>The raider at <paramref name="index"/> of <paramref name="wave"/>.</summary>
        public SiegeSpec At(int wave, int index)
        {
            if (wave < 0) return default;

            var line = IsEndless ? Generated(wave)
                     : wave >= Coming.Length ? null : Coming[wave];

            if (line == null) return default;
            return index < 0 || index >= line.Length ? default : line[index];
        }

        /// <summary>
        /// Every wave parsed into raiders, which is the one place that knows what a wave's text
        /// means.
        ///
        /// <b>Parallel to <see cref="Waves"/> and always the same length</b>, because the strings
        /// are what a level authored and this is what it said. Nothing outside this class reads
        /// the strings for anything but a message.
        /// </summary>
        public readonly SiegeSpec[][] Coming = Array.Empty<SiegeSpec[]>();

        /// <summary>
        /// The ramp, for a lane whose waves never stop, or null for an authored muster.
        ///
        /// <b>The one field that changes what every question below means.</b> With it set,
        /// <see cref="SizeOf"/>, <see cref="At"/> and <see cref="WaveCount"/> stop reading
        /// <see cref="Coming"/> and start asking <see cref="SiegeEndless"/> - which is a pure
        /// function of the wave number, so an endless level parses, validates, indexes and merges
        /// exactly like the ten-wave one beside it.
        /// </summary>
        public readonly SiegeEndless Endless;

        /// <summary>Whether this lane's waves never stop.</summary>
        public bool IsEndless => Endless != null;

        /// <summary>
        /// How many waves are coming, or <see cref="int.MaxValue"/> on an endless lane.
        ///
        /// <b>Asked rather than <c>Waves.Length</c></b>, which is the one thing an endless lane
        /// broke: the authored strings are empty on an endless lane, so a caller counting them
        /// would muster nothing at all.
        /// </summary>
        public int WaveCount => IsEndless ? int.MaxValue : Waves.Length;

        /// <summary>How much tougher this wave is than the first. Nothing at all, unless endless.</summary>
        public SiegeSurge SurgeOf(int wave)
            => IsEndless ? SiegeEndless.SurgeAt(wave + 1) : Tough;

        /// <summary>
        /// The raiders of one endless wave, dealt on demand.
        ///
        /// <b>A memo of one, and it is safe for the reason <c>LevelTuning</c>'s is</b>: what it
        /// holds is a pure function of the wave number and the layout's own seed, so a race can
        /// only ever recompute the same answer. It exists because a muster asks four questions
        /// about the same wave in the same frame, not because dealing one is expensive.
        /// </summary>
        SiegeSpec[] Generated(int wave)
        {
            if (_dealt == null || _dealtWave != wave)
            {
                _dealt = Endless.WaveAt(wave + 1, Seed);
                _dealtWave = wave;
            }

            return _dealt;
        }

        SiegeSpec[] _dealt;

        int _dealtWave = -1;

        /// <summary>Parses one authored wave into the raiders it names.</summary>
        static SiegeSpec[] Read(string wave, SiegeKind bossKind, bool boss)
        {
            if (string.IsNullOrEmpty(wave)) return Array.Empty<SiegeSpec>();

            var made = new List<SiegeSpec>(wave.Length);

            for (int i = 0; i < wave.Length; i++)
            {
                char mark = IsModifier(wave[i]) ? wave[i] : '\0';
                if (mark != '\0' && ++i >= wave.Length) break;

                char letter = wave[i];
                // **The first raider of the boss wave, and only the first.** It used to be every
                // raider in it, which was exactly right while a boss wave held nothing else - and
                // a boss that rides the last wave (see the constructor) stands at its head with
                // an ordinary wave behind it.
                var kind = boss && made.Count == 0 ? bossKind
                         : mark != '\0' ? Modified(mark)
                         : char.IsUpper(letter) ? SiegeKind.Brute : SiegeKind.Creeper;

                made.Add(new SiegeSpec(char.ToLowerInvariant(letter), kind));
            }

            return made.ToArray();
        }

        /// <summary>
        /// How often a felled raider leaves a cog on the hill, per hundred. Nought for a siege
        /// with none.
        ///
        /// <para>
        /// <b>A rate rather than a count, and the denominator is now <em>kills</em>.</b> It used
        /// to be dealt gems, because a cog stood in the gem field and the field refills. A cog is
        /// dropped by a raider that died now (<c>SiegeBoard.Drop</c>), so what a level decides is
        /// how often a kill pays — and the same field means something quite different: twenty to
        /// thirty-six raiders rather than several hundred gems, so a rung that used to author 3
        /// authors about 25 for the same handful of cogs a run.
        /// </para>
        /// <para>
        /// It is bounded at both ends: <see cref="MaxCogRate"/> is the ceiling on the rate, and
        /// <see cref="SiegeTuning.MostCogs"/> stops however generous a rate from putting more than
        /// a handful on the hill at once.
        /// </para>
        /// <para>
        /// <b>Nought is a real answer and is how the opening level says it.</b> The first rung of
        /// this chapter teaches what a match is <em>for</em>; a second thing to reach for while
        /// somebody is working that out is the mistake invariant 24 names about hearts, asked of
        /// attention rather than of money.
        /// </para>
        /// </summary>
        public readonly int Cogs;

        /// <summary>
        /// The most cogs a level may ask for, per hundred kills.
        ///
        /// <b>A hundred, and that is the honest ceiling rather than a shrug.</b> When the
        /// denominator was dealt gems a fifth was already a cog every other column, and past it
        /// the line went up whatever the player did — the decoration invariant 5d names. Per kill
        /// there is no such cliff: a level sends a few dozen raiders, so every one of them paying
        /// is still fewer cogs than a full line has rungs, and what bounds the feature is
        /// <see cref="SiegeTuning.MaxRank"/> and <see cref="SiegeTuning.MostCogs"/>.
        /// </summary>
        public const int MaxCogRate = 100;

        /// <summary>
        /// Where the refill stream starts, derived from the authored field rather than typed.
        ///
        /// <b>Derived, so no level ever authors a seed.</b> Two devices dealing the same board the
        /// same way is worth having — a bug reported against a level is a bug somebody else can
        /// meet — and a number in the file is a number an author has to invent and can mistype.
        /// </summary>
        public readonly uint Seed;

        /// <summary>What is wrong with this level, or null.</summary>
        public readonly string Fault;

        /// <summary>
        /// How much health every raider on this hill carries, in tenths. See <c>SiegeDto.tough</c>.
        ///
        /// <b>Last in the constructor rather than beside <c>cogs</c> where it belongs</b>, because
        /// <c>endless</c> is already passed positionally by three fixtures and moving it would
        /// change what they mean without changing what they say.
        /// </summary>
        public readonly SiegeSurge Tough;

        /// <summary>
        /// The charms this field's refill may carry, in the order the chapter introduces them, or
        /// empty for a level that deals none. See <see cref="SiegeCharms"/>.
        ///
        /// <para>
        /// <b>Charms rather than a charm, because a rung deals the whole of its chapter's set.</b>
        /// A chapter at ordinal <em>n</em> deals the first <em>n</em> of the roster
        /// (<see cref="SiegeCharms.Upto"/>), so the third chapter's boards hold three kinds at the
        /// same rarity between them rather than three times as many charms — which is what keeps
        /// "rare" a fact about the mode rather than about the chapter.
        /// </para>
        /// <para>
        /// <b>Carried here and never in <see cref="Grid"/>.</b> A charm is dealt into a refill and
        /// is never authored into a cell: a field is authored settled, and a charm standing in one
        /// would be a payoff its author placed (invariant 20m) as well as one more thing the
        /// settled proof would have to know about.
        /// </para>
        /// <para>
        /// <b>Last in the constructor for <see cref="Tough"/>'s reason</b> — three fixtures pass
        /// <c>endless</c> positionally, and a parameter inserted before it would change what they
        /// mean without changing what they say.
        /// </para>
        /// </summary>
        public readonly SiegeCharm[] Charms;

        public SiegeLayout(ProtoGrid grid, string deal, string wards, string[] waves, string boss,
                           int cogs = 0, SiegeEndless endless = null, int tough = 0,
                           string charms = null)
        {
            Grid = grid;
            Endless = endless;

            // **Refused by name rather than salvaged**, which is `Tidy`'s opposite and invariant
            // 5f's rule: a body naming a charm this build does not have was written against rules
            // that are not these, and reading it as "the charms I recognise" would ship a field
            // the author did not compose. `Check` is what says so out loud; this only has to leave
            // the evidence, which is `null` against a non-empty string.
            Charms = Charmed(charms);

            // Health only; a blow is never surged (see `SiegeTuning.ChapterToughStep`). Nought is
            // what an older body and every fixture that does not care about this pass, and it is
            // the plain figure rather than a raider with no health at all.
            Tough = new SiegeSurge(tough <= 0 ? 10 : tough, 10);
            Deal = Tidy(deal, Letters);
            Wards = Tidy(wards, WardLetters).ToCharArray();
            Cogs = cogs < 0 ? 0 : cogs;

            // **Exactly one legal name and one legal colour, or nothing** — not `Tidy`, which
            // keeps whatever it recognises and throws the rest away. "dragon:r" would salvage an
            // 'r' out of itself and ship a warlord nobody authored, which is the shape of accident
            // invariant 5f exists to refuse: content written for a build that is not this one has
            // to be said out loud rather than quietly interpreted. The same clause is what refuses
            // the retired one-letter form.
            if (Named((boss ?? string.Empty).Trim(), out var kind, out char colour))
            {
                BossKind = kind;
                Boss = colour;
            }

            var coming = new List<string>(Trim(waves));

            if (HasBoss)
            {
                // **A boss that cannot bring a ward down is not a wave of its own; it rides the
                // last one.**
                //
                // A warlord, a warbringer and an overlord all shell the line for as long as they
                // live, so a wave to themselves is the *point* - a duel stacked on a wave still
                // swinging is two fail states arriving together (invariant 37t). A blightcaller
                // takes a ward's **fire**, and fire is worth exactly what there is to burn: alone
                // on a hill the player has cleared, five seconds of one turret's dark costs
                // nothing at all, so the boss takes no health *and* no time. That is invariant 5d
                // arriving through the pacing, and it was reported from play twice - once on the
                // endless lane, where the answer was an escort (invariant 43), and once here.
                //
                // **A shorter quiet cannot fix it**, which is the half that had to be measured:
                // `SiegeBoard.Muster` sends the next wave the moment the hill is clear (invariant
                // 37k), so on `s01_stonewatch` every wave went early and the boss met an empty
                // hill however long the clock said. Merging is the endless lane's escort said in
                // the idiom of an authored ladder, and it is the cheaper half of the bargain: the
                // company is the raiders the level already sends, so **par, both star lines and
                // `RaiderCount` do not move by one**.
                // **A boss is always a wave of its own now (37dn)**: it comes in alone, after
                // the hill has been cleared (`SiegeBoard.Muster` holds the clock for it), so the
                // riding above is history. Kept as prose because it explains why three bosses
                // once had company, and why every boss spell takes health now.
                BossWave = coming.Count;
                coming.Add(Boss.ToString());
            }

            Waves = coming.ToArray();

            Coming = new SiegeSpec[Waves.Length][];
            for (int i = 0; i < Waves.Length; i++)
                Coming[i] = Read(Waves[i], BossKind, i == BossWave);

            Seed = Hash(grid);
            Fault = Check(boss, charms);
        }

        /// <summary>
        /// Reads <c>"warlord:r"</c>, and answers false for anything at all that is not exactly
        /// that shape.
        ///
        /// No trimming inside the halves and no case folding: a token is either what a build of
        /// this game writes or it is content from somewhere else, and the second one is refused.
        /// </summary>
        static bool Named(string token, out SiegeKind kind, out char colour)
        {
            kind = SiegeKind.Creeper;
            colour = '\0';

            if (string.IsNullOrEmpty(token)) return false;

            int split = token.IndexOf(':');
            if (split <= 0 || split != token.Length - 2) return false;

            char wears = token[token.Length - 1];
            if (BossColours.IndexOf(wears) < 0) return false;

            string name = token.Substring(0, split);
            for (int i = 0; i < BossNames.Length; i++)
            {
                if (BossNames[i].Name != name) continue;

                kind = BossNames[i].Kind;
                colour = wears;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reads a <c>charms</c> field, and answers <b>null</b> for anything holding a letter this
        /// build does not know.
        ///
        /// <para>
        /// <b>Null rather than "the ones I recognised", which is the whole difference between this
        /// and <see cref="Tidy"/>.</b> A body naming a retired or unknown charm was written for a
        /// build that is not this one, and salvaging what is left of it ships a field its author
        /// never composed — invariant 5f, and the same clause that refuses a boss token this mode
        /// cannot draw. <see cref="Check"/> turns the null into a sentence; nothing here guesses.
        /// </para>
        /// <para>
        /// Empty is the ordinary case and is not a fault: the opening rung of the first chapter
        /// deals no charms at all, which is invariant 24's rule about the one moment a player is
        /// still working out what the verb is.
        /// </para>
        /// </summary>
        static SiegeCharm[] Charmed(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return Nothing;

            var kept = new List<SiegeCharm>(raw.Length);

            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == ' ') continue;

                var charm = SiegeCharms.Named(raw[i]);
                if (charm == SiegeCharm.None) return null;

                // A letter written twice is an author saying one thing twice, not two charms: the
                // deal picks uniformly from this array, so keeping the repeat would silently
                // weight one charm double with nothing in the file saying so.
                if (!kept.Contains(charm)) kept.Add(charm);
            }

            return kept.Count == 0 ? Nothing : kept.ToArray();
        }

        static readonly SiegeCharm[] Nothing = new SiegeCharm[0];

        static string Tidy(string raw, string legal)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var kept = new System.Text.StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
                if (raw[i] != ' ' && legal.IndexOf(raw[i]) >= 0) kept.Append(raw[i]);

            return kept.ToString();
        }

        /// <summary>Drops any modifier that has no colour letter after it.</summary>
        static string Sweep(string wave)
        {
            if (string.IsNullOrEmpty(wave)) return string.Empty;

            var kept = new System.Text.StringBuilder(wave.Length);
            for (int i = 0; i < wave.Length; i++)
            {
                bool orphan = IsModifier(wave[i])
                           && (i + 1 >= wave.Length || IsModifier(wave[i + 1]));

                if (orphan) continue;
                kept.Append(wave[i]);
            }

            return kept.ToString();
        }

        static string[] Trim(string[] waves)
        {
            if (waves == null) return Array.Empty<string>();

            var kept = new List<string>(waves.Length);
            for (int i = 0; i < waves.Length; i++)
            {
                // **Tidied against the wider alphabet and then swept of a trailing modifier.** A
                // '#' with no letter after it is not a raider, and left in place it would be a
                // wave whose text and whose muster disagree about its own length - which is the
                // whole class of fault the muster exists to remove.
                string wave = Sweep(Tidy(waves[i], WaveLetters));
                if (wave.Length > 0) kept.Add(wave);
            }

            return kept.ToArray();
        }

        /// <summary>
        /// FNV-1a over the authored field. Thirty-two bit throughout, for
        /// <c>DailyChestTable</c>'s reason: the offline mirror runs in Python and has to reach the
        /// same board.
        /// </summary>
        static uint Hash(ProtoGrid grid)
        {
            uint h = 2166136261u;

            for (int i = 0; grid != null && i < grid.Count; i++)
            {
                h ^= grid.At(i);
                h = unchecked(h * 16777619u);
            }

            return h == 0u ? 1u : h;
        }

        string Check(string boss, string charms)
        {
            if (Grid == null) return "no field";

            // Refused by name, for the boss token's reason one line down: a body naming a charm
            // this build does not have would otherwise deal the ones it recognised and ship a
            // field nobody composed, with every other gate green (invariant 5f).
            if (Charms == null)
                return $"'{charms}' names a charm this mode does not have; a charm is one of "
                     + $"'{SiegeCharms.Letters}' and an empty field is how a level says it deals "
                     + "none";

            // Refused by name rather than ignored, which is invariant 5f's rule for a token a
            // build no longer knows read the other way round: a level that names a warlord this
            // mode cannot draw would otherwise index, validate and ship as a siege with no boss in
            // it, and nothing anywhere would say so.
            if (!string.IsNullOrEmpty(boss) && boss.Trim().Length > 0 && !HasBoss)
            {
                var known = new System.Text.StringBuilder();
                for (int i = 0; i < BossNames.Length; i++)
                    known.Append(i == 0 ? "" : ", ").Append(BossNames[i].Name).Append(":<colour>");

                return $"'{boss}' is not a boss this mode knows; a boss is written as a kind and "
                     + $"the colour it wears ({known}, colour one of '{BossColours}'), and an "
                     + "empty field is how a siege says it sends none";
            }

            if (Wards.Length < MinWards || Wards.Length > MaxWards)
                return $"a ward line holds {MinWards} to {MaxWards} wards; this one names "
                     + $"{Wards.Length}. Fewer than {MinWards} is a field of fewer than "
                     + $"{MinWards} colours, which cascades without stopping and cannot be "
                     + "authored settled at all";

            for (int i = 0; i < Wards.Length; i++)
                for (int j = i + 1; j < Wards.Length; j++)
                    if (Wards[i] == Wards[j])
                        return $"two wards on this line are both '{Wards[i]}', so one of them can " +
                               "never be the only thing a colour feeds and half the line is a "
                               + "spare part";

            if (Colours < MinWards)
                return $"the field refills from {Colours} colour(s); {MinWards} is the fewest a "
                     + "field can hold and still be a board";

            if (Cogs > MaxCogRate)
                return $"this hill drops a cog {Cogs} times in a hundred kills; {MaxCogRate} is "
                     + "the most a level may ask for";

            // A ward nothing feeds is decoration standing where a ward should be (invariant 5d).
            for (int i = 0; i < Wards.Length; i++)
                if (Deal.IndexOf(Wards[i]) < 0)
                    return $"the '{Wards[i]}' ward stands on a field that never deals a "
                         + $"'{Wards[i]}' gem, so nothing the player does could ever fuel it";

            // **And the other way round, which only became a rule when the lock arrived.** A ward
            // burns its own colour and nothing else, so a gem no ward carries is fuel with nowhere
            // to go: every match of it is a move the player spent for nothing, and there is no
            // reading anywhere that would report it. While a bolt merely preferred its own colour
            // this was a tuning curiosity; under the lock it is a quarter of the board that does
            // not work.
            for (int i = 0; i < Deal.Length; i++)
                if (Array.IndexOf(Wards, Deal[i]) < 0)
                    return $"this field deals '{Deal[i]}' gems and no ward on the line burns "
                         + $"'{Deal[i]}', so every match of that colour is a move spent on "
                         + "nothing. A siege deals exactly the colours its line stands";

            // **An endless lane authors no waves, and that is the shape rather than an
            // omission.** What comes at wave n is a rule (`SiegeEndless`), so the list is empty by
            // construction — and the two clauses below, which walk the authored list, have nothing
            // to walk. What replaces them is a gate that walks the *ramp* (`content.py`'s
            // `check_endless` and `SiegeValidator`), because the question is the same one asked of
            // a hill nobody wrote down: does this line have an answer to what is coming.
            if (IsEndless) return Settled();

            if (Waves.Length == 0) return "nothing is coming, so there is nothing to hold";

            int raiders = 0;
            for (int i = 0; i < Coming.Length; i++) raiders += Coming[i].Length;

            if (raiders > MaxRaiders)
                return $"this level sends {raiders} raiders; {MaxRaiders} is the most a run may "
                     + "hold, and a longer siege wants a second level rather than a longer wave";

            // A raider no ward on the line is strong against is one the player cannot answer
            // properly - it is still killable, at half rate, which is a level asking for
            // something it never taught. Certain, and the arithmetic par assumes it is not so.
            for (int w = 0; w < Waves.Length; w++)
                for (int i = 0; i < Coming[w].Length; i++)
                {
                    // **A boss wears no colour** (37dn): every ward reaches it at full weight,
                    // so the letter on its token decides nothing about the line and is not
                    // checked against it. A bonecaller's raised creepers still wear it, and
                    // `ModeValidator.Bossed` warns when no ward does.
                    if (w == BossWave && i == 0) continue;

                    char colour = Coming[w][i].Colour;
                    if (Array.IndexOf(Wards, colour) >= 0) continue;

                    return $"wave {w + 1} sends a '{colour}' raider and no ward on this line "
                         + $"carries '{colour}', so nothing here is strong against it";
                }

            return Settled();
        }

        /// <summary>
        /// Authored settled: no three alike already touching.
        ///
        /// <b>Its own method because an endless lane needs it and needs nothing else</b> — a field
        /// that goes off before anybody has touched it is a board whose opening move its author
        /// played, and the count the run is graded against would already have moved. Budburst's
        /// rule, and Prismvale's, asked of a jewel board.
        /// </summary>
        string Settled()
        {
            var cells = Grid.Copy();
            if (Runs(cells, Grid.Width, Grid.Height).Count > 0)
                return "three alike are already touching on this field, so it would go off before "
                     + "anybody had moved a gem - a field is authored settled";

            return null;
        }

        /// <summary>
        /// Whether this cell is a gem — something that can line up and is worth fuel.
        ///
        /// <b>Kept as a predicate now that a hole is the only thing it excludes</b>, because the
        /// four places that ask it are exactly where a mode with a second kind of cell goes
        /// quietly wrong (invariant 26f) — and this field has carried one twice.
        /// </summary>
        internal static bool IsGem(char cell)
            => cell != SiegeBoard.Hole && Letters.IndexOf(cell) >= 0;

        /// <summary>
        /// Every cell standing in a run of three or more, as one set — and, optionally, the colour
        /// each of them is <em>paid</em> as.
        ///
        /// <para>
        /// <b>Scanned once per colour rather than once per row, and the prism is the whole reason.</b>
        /// It used to compare each cell with the one before it, which is exactly right while every
        /// gem is its own colour and cannot survive a wild: <c>"r P r"</c> has no two neighbours
        /// alike in it and is a run, and <c>"r r P g g"</c> is <em>two</em> runs sharing one cell.
        /// A neighbour comparison cannot express either, and every repair of it that keeps the
        /// shape ("treat a wild as whatever came before") quietly answers a different question at
        /// each end of the row. So a run of colour <em>c</em> is defined instead as a maximal block
        /// of cells that are <em>c</em>-or-wild holding at least one real <em>c</em>, and the four
        /// colours are walked in turn. On an eight-by-five field that is four times thirteen short
        /// scans, which is nothing, and it is a definition rather than a procedure.
        /// </para>
        /// <para>
        /// <b>A block of wilds alone is not a run</b>, which is what "at least one real
        /// <em>c</em>" buys: without it three prisms falling into a column would clear themselves
        /// and pay a colour nobody chose.
        /// </para>
        /// <para>
        /// <b><paramref name="paid"/> is what a cleared cell is worth, and for a wild it is not
        /// the letter underneath.</b> A prism is drawn colourless because it <em>is</em> colourless
        /// — the letter it carries is only what the deal happened to hand it — so paying it as that
        /// letter would be a payoff the player could neither see nor aim. It is paid as the colour
        /// of the run it completed, and a prism completing two runs at once is paid as the first of
        /// them in scan order (rows before columns, <see cref="Letters"/> in order): arbitrary, and
        /// <em>stated</em> rather than emergent, for <c>SiegeBoard</c>'s reason about contested
        /// cogs — a rule nobody wrote down is a rule two runtimes may answer differently.
        /// </para>
        /// <para>
        /// Both trailing arguments are null on every reading that only asks <em>whether</em>
        /// anything lines up — <c>Lines</c>, <c>AnySwap</c>, the settled proof and both offline
        /// mirrors' cheap paths — so a field with no charms on it costs no allocation and no branch
        /// worth naming.
        /// </para>
        /// </summary>
        internal static HashSet<int> Runs(char[] cells, int width, int height,
                                          SiegeCharm[] charms = null, char[] paid = null)
        {
            var hit = new HashSet<int>();

            bool Wild(int i) => charms != null && SiegeCharms.IsWild(charms[i]);

            // Written into `paid` only the first time a cell is claimed, which is what makes the
            // scan order above a rule rather than a coincidence.
            void Take(int i, char colour)
            {
                hit.Add(i);
                if (paid == null) return;
                if (paid[i] == '\0') paid[i] = Wild(i) ? colour : cells[i];
            }

            for (int c = 0; c < Letters.Length; c++)
            {
                char colour = Letters[c];

                for (int y = 0; y < height; y++) Scan(y * width, 1, width);
                for (int x = 0; x < width; x++) Scan(x, width, height);

                // One line of a field, walked in whichever direction `step` names. A block ends at
                // the first cell that is neither this colour nor a wild, which is also what a hole
                // is, so nothing needs a second test for one.
                void Scan(int from, int step, int span)
                {
                    int run = 0, real = 0;

                    for (int k = 0; k <= span; k++)
                    {
                        int i = from + k * step;

                        bool joins = k < span && IsGem(cells[i]) && (Wild(i) || cells[i] == colour);
                        bool solid = joins && !Wild(i);

                        if (joins)
                        {
                            run++;
                            if (solid) real++;
                            continue;
                        }

                        if (run >= SiegeTuning.MinRun && real > 0)
                            for (int back = k - run; back < k; back++) Take(from + back * step, colour);

                        run = 0;
                        real = 0;
                    }
                }
            }

            return hit;
        }

        /// <summary>Which ward carries this colour, or -1.</summary>
        public int WardOf(char colour) => Array.IndexOf(Wards, colour);

        /// <summary>
        /// How many <em>distinct</em> colours this field deals.
        ///
        /// <b>Distinct rather than <c>Deal.Length</c></b>, because the deal is a bag and an author
        /// may weight it by writing a letter twice. What par asks is how many kinds of gem can
        /// land beside each other, which is the count of kinds and not the size of the bag.
        /// </summary>
        public int Colours
        {
            get
            {
                int seen = 0, mask = 0;

                for (int i = 0; i < Deal.Length; i++)
                {
                    int bit = 1 << Letters.IndexOf(Deal[i]);
                    if ((mask & bit) != 0) continue;

                    mask |= bit;
                    seen++;
                }

                return seen;
            }
        }

        /// <summary>Every raider this level sends, in the order they arrive.</summary>
        public int RaiderCount
        {
            get
            {
                // **An endless lane never runs out of goals**, which is what stops a board that
                // cannot be finished from reading as finished the moment its last wave is cleared.
                // What ends an endless run is the ward line, and nothing else.
                if (IsEndless) return int.MaxValue;

                int n = 0;
                for (int i = 0; i < Coming.Length; i++) n += Coming[i].Length;
                return n;
            }
        }
    }
}
