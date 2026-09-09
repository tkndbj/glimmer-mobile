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
        /// Everything a cell of the field may hold: the four gems, and the cog.
        ///
        /// <para>
        /// <b>A second alphabet rather than a fifth letter in <see cref="Letters"/>, because a cog
        /// is not a colour.</b> Every rule in this mode that reads a cell is asking one of two
        /// questions — <em>what colour is this</em> (matching, fuelling, the deal) or <em>what is
        /// standing here</em> (gravity, swapping, drawing) — and folding a cog into
        /// <see cref="Letters"/> would answer the first with a thing that has no colour. It is the
        /// same split <c>FallCell</c> was cut into for the same reason (invariant 26f): a
        /// predicate about the <em>ground</em> must not answer a question about what is
        /// <em>on</em> it.
        /// </para>
        /// </summary>
        public const string Cells = "rgby*";

        /// <summary>
        /// A cog: the thing a ward is upgraded with.
        ///
        /// <para>
        /// It never matches, never falls out of the field and is never worth fuel. What it is
        /// worth is a <em>rank</em> on one ward, and which ward is decided entirely by the colour
        /// of the run that destroys it — so a cog is the one object on this field that asks the
        /// player which colour to spend next rather than which match is biggest, and the one they
        /// can be wrong about (invariant 26h's test: what does the player decide, and can they be
        /// wrong).
        /// </para>
        /// </summary>
        public const char Cog = '*';

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
        /// Written before a colour letter, it makes that raider a <see cref="SiegeKind.Weaver"/>:
        /// <c>"rg~b"</c> is two creepers and a blue weaver.
        /// </summary>
        public const char Web = '~';

        /// <summary>
        /// Written before a colour letter, it makes that raider a <see cref="SiegeKind.Thief"/>:
        /// <c>"R$y"</c> is a red brute and a yellow thief.
        /// </summary>
        public const char Loot = '$';

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
            (Web,    SiegeKind.Weaver),
            (Loot,   SiegeKind.Thief),
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
        /// A cell holding a sack a thief has taken a gem into.
        ///
        /// <para>
        /// <b>Never authored, and that is why it is not in <see cref="Cells"/>.</b> A level says
        /// what is standing on the field when it opens; a sack only ever exists because something
        /// on the hill put it there, so a file carrying one is a file written against rules this
        /// build does not have (invariant 5f).
        /// </para>
        /// <para>
        /// <b>A glyph rather than a flag, because it is not a colour.</b> A sack cannot line up
        /// with anything, so <see cref="IsGem"/> answers false for it and every rule that walks
        /// the field is correct about it unchanged — exactly what makes <see cref="Cog"/> cheap.
        /// A web is the opposite case and is a flag, because a webbed gem <em>is</em> still that
        /// colour and the player can still see what they are being denied.
        /// </para>
        /// </summary>
        public const char Sack = '%';

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
        };

        /// <summary>The colour a boss may wear. Lower case only — case no longer means anything.</summary>
        public const string BossColours = "rgby";

        /// <summary>How many wards a line may hold. Four colours, so four is the whole line.</summary>
        public const int MaxWards = 4;

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
        /// <b>It is still a wave, and that is what makes it cost nothing.</b> The warlord is
        /// appended to <see cref="Waves"/> as a one-raider wave, so the wave count the header
        /// reads, the muster, the banner, <see cref="RaiderCount"/> and
        /// <see cref="SiegeTuning.Par"/> all take it without a single special case — the only
        /// question anything has to ask is <see cref="BossWave"/>, and only because a warlord's
        /// health and its way of fighting are not a creeper's.
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

        /// <summary>Which wave the warlord is, or -1. Always the last one when there is one.</summary>
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
            if (!IsEndless && wave == BossWave && wave >= 0 && wave < Coming.Length) return BossKind;

            var spec = At(wave, index);
            return spec.Kind;
        }

        /// <summary>
        /// Which colour the raider at <paramref name="index"/> of <paramref name="wave"/> wears,
        /// as an index into <see cref="Letters"/>, or -1.
        /// </summary>
        public int ColourAt(int wave, int index) => Letters.IndexOf(At(wave, index).Colour);

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
            => IsEndless ? SiegeEndless.SurgeAt(wave + 1) : SiegeSurge.None;

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
                var kind = boss ? bossKind
                         : mark != '\0' ? Modified(mark)
                         : char.IsUpper(letter) ? SiegeKind.Brute : SiegeKind.Creeper;

                made.Add(new SiegeSpec(char.ToLowerInvariant(letter), kind));
            }

            return made.ToArray();
        }

        /// <summary>
        /// How often a fresh gem comes in as a cog, per hundred. Nought for a siege with none.
        ///
        /// <para>
        /// <b>A rate rather than a count, because the field refills.</b> A level cannot author
        /// "three cogs" the way it authors three brutes — a cog is destroyed and the column fills
        /// in behind it, so what a level really decides is how often one turns up. It is bounded
        /// at both ends: <see cref="MaxCogRate"/> stops a field that is mostly cogs, and
        /// <see cref="SiegeTuning.MostCogs"/> stops however generous a rate from putting more than
        /// a handful on the board at once.
        /// </para>
        /// <para>
        /// <b>Nought is a real answer and is how the opening level says it.</b> The first rung of
        /// this chapter teaches what a match is <em>for</em>; a second object on the field while
        /// somebody is working that out is the mistake invariant 24 names about hearts, asked of
        /// attention rather than of money.
        /// </para>
        /// </summary>
        public readonly int Cogs;

        /// <summary>
        /// The most cogs a level may ask for, per hundred.
        ///
        /// A fifth of the deal is already a cog roughly every other column of a refill, which is
        /// as far as a mechanic that upgrades the line can go before the line is upgraded whatever
        /// the player does — and a mechanic that rejects no play is decoration (invariant 5d).
        /// </summary>
        public const int MaxCogRate = 20;

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

        public SiegeLayout(ProtoGrid grid, string deal, string wards, string[] waves, string boss,
                           int cogs = 0, SiegeEndless endless = null)
        {
            Grid = grid;
            Endless = endless;
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
                BossWave = coming.Count;
                coming.Add(Boss.ToString());
            }

            Waves = coming.ToArray();

            Coming = new SiegeSpec[Waves.Length][];
            for (int i = 0; i < Waves.Length; i++)
                Coming[i] = Read(Waves[i], BossKind, i == BossWave);

            Seed = Hash(grid);
            Fault = Check(boss);
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

        string Check(string boss)
        {
            if (Grid == null) return "no field";

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

            if (Wards.Length < 2 || Wards.Length > MaxWards)
                return $"a ward line holds 2 to {MaxWards} wards; this one names {Wards.Length}";

            for (int i = 0; i < Wards.Length; i++)
                for (int j = i + 1; j < Wards.Length; j++)
                    if (Wards[i] == Wards[j])
                        return $"two wards on this line are both '{Wards[i]}', so one of them can " +
                               "never be the only thing a colour feeds and half the line is a "
                               + "spare part";

            if (Deal.Length < 2)
                return "the field refills from fewer than two colours, so every arrangement is a "
                     + "match and nothing is ever decided";

            if (Cogs > MaxCogRate)
                return $"this field deals a cog {Cogs} times in a hundred; {MaxCogRate} is the "
                     + "most a level may ask for, and past it the line is upgraded whatever the "
                     + "player does";

            int standing = 0;
            for (int i = 0; i < Grid.Count; i++) if (Grid.At(i) == Cog) standing++;

            if (standing > SiegeTuning.MostCogs)
                return $"this field is authored with {standing} cogs standing on it; "
                     + $"{SiegeTuning.MostCogs} is the most that may ever be on the board at once, "
                     + "so the rest could never be dealt back in";

            // A ward nothing feeds is decoration standing where a ward should be (invariant 5d).
            for (int i = 0; i < Wards.Length; i++)
                if (Deal.IndexOf(Wards[i]) < 0)
                    return $"the '{Wards[i]}' ward stands on a field that never deals a "
                         + $"'{Wards[i]}' gem, so nothing the player does could ever fuel it";

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
                    char colour = Coming[w][i].Colour;
                    if (Array.IndexOf(Wards, colour) >= 0) continue;

                    // The boss is the one this really matters for: it has the health of a whole
                    // wave, so answering it at half rate is a duel nobody can finish.
                    string what = w == BossWave ? $"a '{colour}' {SiegeTuning.NameOf(BossKind)}"
                                                : "a '" + colour + "' raider";

                    return $"wave {w + 1} sends {what} and no ward on this line carries "
                         + $"'{colour}', so nothing here is strong against it";
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
        /// <b>Neither a hole nor a cog</b>, and it is one predicate rather than two tests written
        /// out at each of the four places that ask, because those four are exactly where a mode
        /// with a second kind of cell goes quietly wrong (invariant 26f).
        /// </summary>
        internal static bool IsGem(char cell)
            => cell != SiegeBoard.Hole && cell != Cog && Letters.IndexOf(cell) >= 0;

        /// <summary>
        /// Every cell standing in a run of three or more, as one set.
        ///
        /// <para>
        /// <b><paramref name="locked"/> is a wall rather than a filter</b>, which is the whole of
        /// what a weaver's web does: a locked cell does not merely fail to join a run, it
        /// <em>breaks</em> one that would otherwise pass through it. Written as a filter instead,
        /// a webbed red between two reds either side would still clear them both and the web would
        /// cost the player nothing.
        /// </para>
        /// <para>
        /// Null is the ordinary case and every offline mirror passes it, so a field with nothing
        /// on it costs no allocation and no branch worth naming.
        /// </para>
        /// </summary>
        internal static HashSet<int> Runs(char[] cells, int width, int height,
                                          bool[] locked = null)
        {
            var hit = new HashSet<int>();

            bool Free(int i) => IsGem(cells[i]) && (locked == null || !locked[i]);

            for (int y = 0; y < height; y++)
            {
                int run = 1;
                for (int x = 1; x <= width; x++)
                {
                    bool same = x < width && Free(y * width + x)
                             && cells[y * width + x] == cells[y * width + x - 1];

                    if (same) { run++; continue; }

                    if (run >= SiegeTuning.MinRun)
                        for (int k = x - run; k < x; k++) hit.Add(y * width + k);

                    run = 1;
                }
            }

            for (int x = 0; x < width; x++)
            {
                int run = 1;
                for (int y = 1; y <= height; y++)
                {
                    bool same = y < height && Free(y * width + x)
                             && cells[y * width + x] == cells[(y - 1) * width + x];

                    if (same) { run++; continue; }

                    if (run >= SiegeTuning.MinRun)
                        for (int k = y - run; k < y; k++) hit.Add(k * width + x);

                    run = 1;
                }
            }

            return hit;
        }

        /// <summary>Which ward carries this colour, or -1.</summary>
        public int WardOf(char colour) => Array.IndexOf(Wards, colour);

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
