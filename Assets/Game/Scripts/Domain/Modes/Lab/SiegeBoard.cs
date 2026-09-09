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
    /// <summary>
    /// One raider a wave names: what colour it wears and what kind it is.
    ///
    /// <b>A pair rather than a character</b>, because a wave's text stopped being one character
    /// per raider the moment a third kind arrived (<see cref="SiegeLayout.Shield"/>). Everything
    /// that used to index into the string indexes into these instead, so a modifier can never be
    /// counted as a raider.
    /// </summary>
    public readonly struct SiegeSpec
    {
        /// <summary>The colour it wears, lower case, or <c>'\0'</c> for nothing at all.</summary>
        public readonly char Colour;

        /// <summary>What it is.</summary>
        public readonly SiegeKind Kind;

        public SiegeSpec(char colour, SiegeKind kind)
        {
            Colour = colour;
            Kind = kind;
        }
    }

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

        /// <summary>Everything a wave may be written with: the raiders, and the shield modifier.</summary>
        public const string WaveLetters = RaiderLetters + "#";

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
            if (wave == BossWave && wave >= 0 && wave < Coming.Length) return BossKind;

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
            => wave < 0 || wave >= Coming.Length ? 0 : Coming[wave].Length;

        /// <summary>The raider at <paramref name="index"/> of <paramref name="wave"/>.</summary>
        public SiegeSpec At(int wave, int index)
        {
            if (wave < 0 || wave >= Coming.Length) return default;

            var line = Coming[wave];
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

        /// <summary>Parses one authored wave into the raiders it names.</summary>
        static SiegeSpec[] Read(string wave, SiegeKind bossKind, bool boss)
        {
            if (string.IsNullOrEmpty(wave)) return Array.Empty<SiegeSpec>();

            var made = new List<SiegeSpec>(wave.Length);

            for (int i = 0; i < wave.Length; i++)
            {
                bool shielded = wave[i] == Shield;
                if (shielded && ++i >= wave.Length) break;

                char letter = wave[i];
                var kind = boss ? bossKind
                         : shielded ? SiegeKind.Bulwark
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
                           int cogs = 0)
        {
            Grid = grid;
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

        /// <summary>Drops any <see cref="Shield"/> that has no colour letter after it.</summary>
        static string Sweep(string wave)
        {
            if (string.IsNullOrEmpty(wave)) return string.Empty;

            var kept = new System.Text.StringBuilder(wave.Length);
            for (int i = 0; i < wave.Length; i++)
            {
                if (wave[i] == Shield && (i + 1 >= wave.Length || wave[i + 1] == Shield)) continue;
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

            // Authored settled. A field that goes off before anybody has touched it is a board
            // whose opening move its author played, and the count the run is graded against would
            // already have moved - Budburst's rule, and Prismvale's, asked of a jewel board.
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

        /// <summary>Every cell standing in a run of three or more, as one set.</summary>
        internal static HashSet<int> Runs(char[] cells, int width, int height)
        {
            var hit = new HashSet<int>();

            for (int y = 0; y < height; y++)
            {
                int run = 1;
                for (int x = 1; x <= width; x++)
                {
                    bool same = x < width && IsGem(cells[y * width + x])
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
                    bool same = y < height && IsGem(cells[y * width + x])
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
                int n = 0;
                for (int i = 0; i < Coming.Length; i++) n += Coming[i].Length;
                return n;
            }
        }
    }

    /// <summary>
    /// Every number this mode runs on, in one place.
    ///
    /// <para>
    /// <b>Constants rather than content, and that is the shape invariant 20d asks for.</b> A level
    /// authors what is standing there and what is coming; how much a match is worth and how hard
    /// a bolt lands are facts about the <em>mode</em>, so a retune is one edit and cannot leave
    /// two levels disagreeing about what a gem does. Par is derived from them, so moving one of
    /// these moves every star line in the mode at once — which is correct, and is why they are
    /// here where that is obvious rather than spread over a board and a view.
    /// </para>
    /// </summary>
    public static class SiegeTuning
    {
        /// <summary>Gems that have to line up. Three, as every game of this shape.</summary>
        public const int MinRun = 3;

        /// <summary>
        /// Fuel a matched gem is worth, in tenths.
        ///
        /// <para>
        /// <b>Two fuel a gem against a bolt's one, which is the whole of "the wards should shoot
        /// more".</b> It was one each and a bolt cost one, so a gem was exactly a bolt — an
        /// identity nothing said out loud and that <see cref="PerfectMatch"/> quietly depended on.
        /// Doubling it and halving <see cref="ShotDamage"/> is the same match delivering the same
        /// damage as <em>twice as many, half as heavy</em> bolts: a fed ward stays alight for
        /// twice as long, which is what a player watches, and not one graded number moves.
        /// </para>
        /// <para>
        /// <b>Subdividing the fuel unit rather than making a bolt cost half is what keeps the rank
        /// ladder exact.</b> A rank is ten per cent less fuel a bolt
        /// (<see cref="FuelShotTenths"/>), which at a base of ten tenths is 10, 9, 8, 7, 6 — five
        /// exact integers. Halving the bolt to five tenths would make the same ladder 5, 4, 4, 3, 3
        /// after truncation, so two pairs of ranks would buy nothing and the 2.33× a rank-four ward
        /// is worth would quietly stop being true. Everything measured in <em>fuel</em> doubles
        /// (this, <see cref="WardCapacity"/>, the surge's authored magnitude); everything measured
        /// <em>per bolt</em> is untouched.
        /// </para>
        /// </summary>
        public const int FuelPerGemTenths = 20;

        /// <summary>Fuel a matched gem is worth. See <see cref="FuelPerGemTenths"/>.</summary>
        public const float FuelPerGem = FuelPerGemTenths / 10f;

        /// <summary>
        /// The most fuel a ward holds. A big match tops it up rather than banking.
        ///
        /// <b>In fuel, so it doubled with the unit</b> — it is fourteen <em>gems</em>' worth
        /// before and after, which is what it was chosen as and what a player experiences.
        /// </summary>
        public const float WardCapacity = 28f;

        // **Fuel used to fade on a clock and no longer does.** The rule was that a standing
        // ward lost fuel every second whether or not it was shooting, so a colour matched early
        // was a colour wasted and the question was always which ward wanted feeding *now*. It was
        // withdrawn by the owner after playing it: what it actually produced was a meter draining
        // while nothing was happening, which reads as the game taking something away rather than
        // as a reason to hurry. Fuel now leaves a ward one way only - as a bolt - so a match is
        // worth what it is worth whenever it is spent, and what makes the colour of a match a
        // decision is the elemental double and the capacity below rather than a clock.
        //
        // Note what that costs and what it does not. It costs the banking pressure, so a player
        // may now feed a ward before its colour arrives; `WardCapacity` is what stops that being
        // unlimited, and it is the number to move if hoarding turns out to be the whole game.
        // It costs nothing about par, which counts matches and never counted time.

        /// <summary>
        /// Seconds between a ward's bolts while it has fuel.
        ///
        /// <b>It buys no damage and it buys the whole feel.</b> A bolt costs one fuel whatever the
        /// cadence, so a ward with three fuel does twelve damage at any speed - what this decides
        /// is whether feeding a ward reads as *opening fire* or as a lamp ticking over.
        ///
        /// <para>
        /// <b>It was .14 and that was too fast, which is the owner's verdict after playing it.</b>
        /// Seven bolts a second per ward is twenty-eight across a lit line, and at that rate a
        /// bolt is not an event - it is a hose, and nothing crossing the hill can be looked at.
        /// The fix is here rather than in the view, because a shorter flight or a smaller bolt
        /// would be treating the symptom: what was wrong is how often the thing happens. At .22 a
        /// ward still opens fire rather than ticking over, and a bolt has all but landed before
        /// the next one leaves (<c>SiegeView.LongestFlight</c> is .20).
        /// </para>
        /// <para>
        /// <b>And it is not free, which is why it went back through the hold simulation.</b> Fuel
        /// still buys the same damage, but it arrives later - and the raiders do not wait, so a
        /// slower line lets them further down the hill and takes more blows. See
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>, which is this mode's only
        /// instrument (invariant 37j). It is also what picked that number: .20 and .22 hold the
        /// line, and <b>.26 loses it outright</b> - the hill unclear and every ward down. So the
        /// band between "too fast to watch" and "too slow to hold" is narrow, and anything slower
        /// than this has to buy the time back somewhere else.
        /// </para>
        /// <para>
        /// <b>And it stayed at .22 when a bolt's damage halved, which is the whole of what that
        /// change was for.</b> A gem now buys two bolts (<see cref="FuelPerGemTenths"/>) and each
        /// is worth half (<see cref="ShotDamage"/>), so at an unchanged cadence a fed ward
        /// <em>keeps firing for twice as long</em> — which is what "see them shoot more" means.
        /// Halving this as well was tried and undoes exactly that: the same bolts go through the
        /// same window twice as densely and a ward stops firing when it always did, so there is
        /// nothing more to watch. <b>The rate is what makes a ward's fire last; the fuel is what
        /// makes it long.</b>
        /// </para>
        /// <para>
        /// <b>What that costs is peak damage, and the levels pay it rather than this number.</b>
        /// Bolts a second times damage a bolt is the line's output, so half-weight bolts at this
        /// cadence is half the <em>peak</em> — which barely touches attrition (a ward lit twice as
        /// long kills a marching column better, because less of each burst is spent as overkill)
        /// and bites hard on an emergency, where one big health pool has to be answered at once.
        /// Measured through <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>, and it moved
        /// three things in three different directions: a duel got dearer
        /// (<c>s01_warlordsgate</c> wanted more hill and got it), a long attrition rung got
        /// <em>safer</em> until nothing reached its line at all (<c>s01_thornsiege</c>, two more
        /// brutes), and the rule-test fixture — the one siege in this project that sent a boss and
        /// dealt no cogs — stopped being holdable, and now deals them like every shipped rung.
        /// </para>
        /// </summary>
        public const float FireEvery = .22f;

        /// <summary>
        /// Fuel one bolt spends at rank nought, in tenths.
        ///
        /// <b>Unchanged when the fuel unit was subdivided</b>, which is the point of having done it
        /// that way: every number in <see cref="FuelShotTenths"/> and every rank the cogs pay for
        /// is bit-identical to what shipped, and what moved is how much fuel a <em>gem</em> is
        /// worth.
        /// </summary>
        public const int FuelPerShotTenths = 10;

        /// <summary>Fuel one bolt spends at rank nought. See <see cref="FuelShotTenths"/>.</summary>
        public const float FuelPerShot = FuelPerShotTenths / 10f;

        /// <summary>
        /// What one bolt takes off a raider it is not strong against, at rank nought.
        ///
        /// <para>
        /// <b>Twenty rather than two, and every raider's health went up by the same ten, so not
        /// one graded number moved.</b> Par is <see cref="Par"/> — health over
        /// <see cref="PerfectMatch"/> — and both sides of that division scaled together, so every
        /// shipped level's par, both its star lines and every utility's charge came out
        /// identical. What the scale buys is the one thing the old numbers could not represent: a
        /// <b>ten per cent</b> step. A ward that gains a rank fires for <c>ShotDamage * 11 / 10</c>,
        /// which at two is two and at twenty is twenty-two — so the upgrade the cogs pay for is
        /// exact integer arithmetic rather than a float three code generators round three ways
        /// (see <c>LevelTuning</c>, and this project's own hard-won note about
        /// <c>Mathf.CeilToInt(45 * 1.20f)</c>).
        /// </para>
        /// <para>
        /// <b>Then ten, because the owner asked to see the wards shoot more.</b> A gem now buys two
        /// bolts rather than one (<see cref="FuelPerGemTenths"/>) and each is worth half, so the
        /// same match delivers the same damage over twice as many bolts and a fed ward stays alight
        /// for twice as long. <see cref="PerfectMatch"/> is unmoved, so par, both star lines and
        /// every utility's charge are unmoved with it. <b>Ten is the floor for the ten per cent
        /// step</b> — at ten the ladder is 10, 11, 12, 13, 14, which is still exact and has no room
        /// under it, so anything that halves this again has to give the rank ladder a finer unit
        /// first.
        /// </para>
        /// </summary>
        public const int ShotDamage = 10;

        /// <summary>What a bolt is worth against a raider of its own colour.</summary>
        public const int WeakMultiplier = 2;

        // ------------------------------------------------------------------ the bulwark
        /// <summary>
        /// What a bolt of the <em>wrong</em> colour is worth against a shield, in tenths.
        ///
        /// <para>
        /// <b>Halved, and its own colour is not reduced at all</b> — which is what makes this the
        /// only raider on the hill whose answer is a colour rather than a quantity. Against a
        /// creeper the difference between the right ward and any other is
        /// <see cref="WeakMultiplier"/>: two to one. Against a bulwark it is <b>four</b> to one,
        /// because the wrong answer is also halved. That is a big enough spread that a player who
        /// keeps taking the biggest match on the field loses ground while one who reads the hill
        /// gains it, and it needs no explaining: the bolts visibly bounce.
        /// </para>
        /// <para>
        /// <b>The shield is pierced rather than worn down</b>, deliberately. A shield with its own
        /// health would be a second bar on a raider that already has one, and the player's answer
        /// to it would be "shoot it more" — which is what every other raider already asks for.
        /// Piercing keeps the whole mechanic in the one decision this mode is about.
        /// </para>
        /// </summary>
        public const int ShieldSoakTenths = 5;

        /// <summary>
        /// What a bolt from a ward of <paramref name="rank"/> takes off <paramref name="kind"/>,
        /// given whether it is the raider's own colour.
        ///
        /// <b>One place, because a second copy is a second opinion about what a shield is worth.</b>
        /// The soak is applied after the double, so a bulwark's own colour reaches it in full.
        /// </summary>
        public static int DamageTo(SiegeKind kind, int rank, bool weak)
        {
            int hit = DamageAt(rank) * (weak ? WeakMultiplier : 1);
            if (kind != SiegeKind.Bulwark || weak) return hit;

            int soaked = hit * ShieldSoakTenths / 10;

            // Never nought: a bolt that reads as landing and takes nothing is a turret the player
            // will believe is broken, and at rank nought against a low `ShotDamage` the division
            // can get there.
            return soaked < 1 ? 1 : soaked;
        }

        /// <summary>
        /// A bulwark: slower than a creeper and much slower than a brute, so the shield is paid
        /// for in the one currency this mode measures in — time on the hill.
        ///
        /// <b>Slower is the whole price, and it is a real one.</b> A raider that is hard to kill
        /// and quick would be a wave the player simply cannot answer; one that is hard to kill and
        /// slow is a wave they may choose to <em>ignore</em> for a while, which is a decision.
        /// Its health sits between a creeper's and a brute's, because the shield is already doing
        /// the work and stacking both would make it a boss.
        /// </summary>
        public const int BulwarkHealth = 700;

        /// <summary>Seconds a bulwark takes to cross the hill. The slowest thing that is not a boss.</summary>
        public const float BulwarkMarch = 34f;

        /// <summary>What one bulwark swing costs a ward. A brute's, because it arrives armoured.</summary>
        public const int BulwarkBlow = 2;

        // ------------------------------------------------------------------ ward ranks
        /// <summary>
        /// How many times a ward may be upgraded. Five tiers, so four ranks above the one it
        /// stands up in.
        /// </summary>
        public const int MaxRank = 4;

        /// <summary>
        /// What each rank is worth, in tenths: ten per cent more damage and ten per cent less fuel
        /// a bolt.
        ///
        /// <para>
        /// <b>Both halves of one upgrade, and they multiply rather than add.</b> A rank-four ward
        /// hits for forty per cent more and gets a bolt out of six tenths rather than ten, so the
        /// same match is worth <em>2.33 times</em> as much damage through it — which is why a
        /// level built around cogs has to send considerably more hill than one that is not, and
        /// why <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> is run over every rung of the
        /// chapter rather than over the first.
        /// </para>
        /// </summary>
        public const int RankDamageTenths = 1, RankFuelTenths = 1;

        /// <summary>How much a bolt from a ward of this rank takes off, before the weak double.</summary>
        public static int DamageAt(int rank)
        {
            if (rank < 0) rank = 0;
            if (rank > MaxRank) rank = MaxRank;

            return ShotDamage * (10 + rank * RankDamageTenths) / 10;
        }

        /// <summary>What one bolt from a ward of this rank costs it, in tenths of fuel.</summary>
        public static int FuelShotTenths(int rank)
        {
            if (rank < 0) rank = 0;
            if (rank > MaxRank) rank = MaxRank;

            return FuelPerShotTenths - rank * RankFuelTenths;
        }

        /// <summary>What one bolt from a ward of this rank costs it.</summary>
        public static float FuelShot(int rank) => FuelShotTenths(rank) / 10f;

        /// <summary>
        /// The most cogs that may stand on a field at once.
        ///
        /// <b>A cap on the board rather than on the deal</b>, because the deal is a rate and a
        /// rate has no upper bound over a long run. Three is enough for the player to have a
        /// choice about which one to take next and few enough that the field is still a field.
        /// </summary>
        public const int MostCogs = 3;

        /// <summary>A creeper: the ordinary raider, and what most of a wave is.</summary>
        public const int CreeperHealth = 200;
        public const float CreeperMarch = 15f;
        public const int CreeperBlow = 1;

        /// <summary>A brute: slower, far tougher, and twice as expensive to let through.</summary>
        public const int BruteHealth = 480;
        public const float BruteMarch = 26f;
        public const int BruteBlow = 2;

        // ------------------------------------------------------------------ the warlord
        /// <summary>
        /// The warlord's health, and it is deliberately under a quarter of the whole hill's.
        ///
        /// <para>
        /// <b>A boss has to be worth a fight rather than a raider with a bigger number.</b> At a
        /// hundred and eighty it is nearly four brutes standing still, which under the ordinary
        /// play <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> models is about thirteen
        /// matches of nothing else and twenty-five seconds of clock — long enough that the player
        /// has to keep choosing the right colour under fire, short enough that the last stretch of
        /// a two-minute level is not a grind.
        /// </para>
        /// <para>
        /// <b>And the ceiling on it is arithmetic rather than taste, which is the half worth
        /// knowing before anybody makes a warlord bigger.</b> <see cref="PerfectMatch"/> assumes
        /// every gem a match clears is spent as a bolt that lands <em>double</em> — which a hill
        /// wearing all four colours very nearly allows, because each ward finds its own. A duel
        /// cannot: the warlord is one colour, so one ward doubles and three do not, and a match
        /// therefore delivers about 13.75 rather than 22. Par is still a genuine floor (no run of
        /// fewer matches could destroy this), it is simply a <b>looser</b> one over a duel, so
        /// every point of warlord pushes the three-star line further from real play. Measured on
        /// the shipped level: at 180 an unhurried player needs 44 matches against a three-star line
        /// of 44, and at 200 it is 47 against 46 and the ladder's top rung is gone. <b>Health moved
        /// from the hill to the warlord makes three stars harder without par saying so.</b>
        /// </para>
        /// </summary>
        public const int BossHealth = 1800;

        /// <summary>
        /// The overlord: the thing the last rung of a chapter ends on.
        ///
        /// <para>
        /// <b>A kind rather than a bigger number, and what makes it one is that all three of these
        /// move together.</b> It carries a little under twice a warlord's health, it throws half as
        /// often again, and each spell takes nearly twice as much off a ward — so it is not
        /// answered by doing what beat a warlord for longer. It stops <em>further up</em> the
        /// hill, which is the half a player feels first: there is more ground between it and the
        /// line, so the wards have longer to work on it, and that is the compensation for
        /// everything above.
        /// </para>
        /// <para>
        /// Its ceiling is <see cref="BossHealth"/>'s ceiling, and the same arithmetic: a duel is
        /// fought against <em>one</em> colour, so one ward doubles and three do not and a match
        /// really delivers about 62% of <see cref="PerfectMatch"/>. Every point of overlord
        /// therefore pushes the three-star line further from real play without par saying so, and
        /// the only instrument that can see it is
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>.
        /// </para>
        /// </summary>
        public const int OverlordHealth = 3200;

        public const float OverlordHold = .38f;
        public const float OverlordMarch = 8f;

        /// <summary>
        /// Seconds between one omen and the next, and the cliff either side of it is sharp.
        ///
        /// <b>It was 3.4 while an omen only took health, and a sunder is a second cost on the same
        /// spell</b> — every rank it knocks off the line is ten per cent of a turret's damage and
        /// ten per cent of its fuel, compounding for the rest of the duel, and none of that is
        /// visible to par (invariant 37w). Measured on the shipped finale through
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>, which is the only instrument that
        /// can see it: at 3.4 the line falls with a raider left, at 3.8 it still falls, at 4.0 an
        /// unhurried player finishes on 16 of 56, and at 4.1 on 20. Two tenths of a second is the
        /// difference between a lost run and a comfortable one, which is itself the argument for
        /// measuring this rather than arguing about it.
        /// </summary>
        public const float OverlordCastEvery = 4.0f;

        public const int OverlordCast = 5;

        /// <summary>
        /// What an overlord's spell also does: knocks the ward it lands on down a rank.
        ///
        /// <b>It attacks the thing the chapter taught rather than the thing every boss attacks.</b>
        /// A cog is the one upgrade a player <em>earns</em> in this mode (invariant 37w), so the
        /// finale is the one fight where where those cogs went is a question with a wrong answer —
        /// pour every one into a single turret and the overlord can take the whole investment off
        /// it, and a mending cannot put a rank back. It is bounded at nought by
        /// <see cref="SiegeWard.Sunder"/> and never touches health beyond
        /// <see cref="OverlordCast"/>, so it can neither fell a ward on its own nor make
        /// <c>Stranded</c> anything but the certainty invariant 28f needs.
        /// </summary>
        public const int OverlordSunder = 1;

        // ------------------------------------------------------------------ the blightcaller
        /// <summary>
        /// The blightcaller: the first boss a chapter shows, and the only one that takes no health.
        ///
        /// <para>
        /// <b>It puts a ward <em>out</em>.</b> Its spell empties the fuel a ward is holding and
        /// smothers it for <see cref="Douse"/> seconds, in which it cannot fire at all — so what
        /// it costs is not the line's health but the player's <em>work</em>, and it is the one
        /// boss whose answer is not a mending. The decision it asks is the mode's own decision
        /// (which colour is worth feeding next) under a constraint that moves every few seconds,
        /// and the way to be wrong about it is to keep pouring into the ward that has just gone
        /// dark. A <c>surge</c> re-lights one, which is the first time in this chapter a utility
        /// answers a boss rather than a wave.
        /// </para>
        /// <para>
        /// <b>Taking no health is what makes it the boss a chapter opens with.</b> It teaches the
        /// tell — the ring closing over a ward, the second and a bit to react in — without the
        /// punishment a warlord's version carries, so a player meets the shape of a boss fight
        /// before they meet its cost. It is also why <c>ModeValidator.Threatens</c> stopped
        /// counting "there is a boss" as a threat: a level whose only threat were this could not
        /// be lost, which is invariant 5d asked of a fail state.
        /// </para>
        /// </summary>
        public const int BlightHealth = 1100;

        public const float BlightHold = .58f;
        public const float BlightMarch = 7f;
        public const float BlightCastEvery = 4.5f;

        /// <summary>Seconds a doused ward stands dark. Long enough to notice, short enough to wait out.</summary>
        public const float Douse = 5f;

        // ------------------------------------------------------------------ the warbringer
        /// <summary>
        /// The warbringer: the boss that hits the <em>whole line</em> at once.
        ///
        /// <para>
        /// <b>It roars, and the roar does two things.</b> It takes <see cref="WarbringerCast"/>
        /// off every ward still standing — not one ward, all of them — and it sets every raider on
        /// the hill charging at <see cref="Rally"/> times its own pace for
        /// <see cref="RallyFor"/> seconds. That is a different verb from the other three: a warlord
        /// and an overlord pick one ward and hit it hard, so the line comes down unevenly and the
        /// player chooses what to save; a warbringer shakes the entire line, so nothing it does can
        /// be answered by protecting one turret and the only answer is to kill it faster.
        /// </para>
        /// <para>
        /// <b>It used to walk to the line, and that was withdrawn after play.</b> The design was
        /// that it took <em>ground</em>: each roar lunged it further down the hill until it arrived
        /// and swung with its hands, which made the fight a countdown rather than a duel. What it
        /// actually produced was a boss that spent most of the level walking — reported as "it
        /// takes forever to move down and start doing its damage, because it keeps moving
        /// downwards". <b>A boss standing still is not a limitation of this mode, it is the shape
        /// of the fight</b>: every one of them holds the middle of the hill where nothing can reach
        /// it and the player answers it with the board, and a boss that walks spends the fight
        /// being somewhere else. So it holds like the rest and the ground it used to take is gone.
        /// </para>
        /// <para>
        /// <b>The rally half survives and is why it still comes early</b> — see
        /// <see cref="RestBefore"/>. A roar over an empty hill would be half a mechanic doing
        /// nothing, which is invariant 5d: the whole point of a rally is that there is something to
        /// rally.
        /// </para>
        /// <para>
        /// <b>It adds no health to the hill, so par does not move</b> — which is exactly why the
        /// only instrument that can say whether it is tuned is
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> (invariant 37j). A warbringer that
        /// is too strong shows up there as a line that falls and nowhere else at all.
        /// </para>
        /// </summary>
        public const int WarbringerHealth = 2400;

        /// <summary>
        /// Where it stops, and it stays there.
        ///
        /// The warlord's ground, because it is the warlord's kind of fight: stand out of reach and
        /// work on the line. It was .34 and moving while it had a lunge.
        /// </summary>
        public const float WarbringerHold = .46f;

        public const float WarbringerMarch = 8f;

        /// <summary>
        /// Seconds between one roar and the next, and it is the slowest cadence of the four.
        ///
        /// <b>Because a roar lands four times, the cadence is what prices it.</b> Four wards at
        /// <see cref="WarbringerCast"/> every nine seconds is 0.89 health a second off the line,
        /// which sits where the rung does: a warlord takes 0.60 and an overlord 1.25, so rungs 5,
        /// 8 and 10 climb. It was 6, which is 1.33 — <em>more</em> than the finale, and measured
        /// on the shipped rung it left an unhurried player on 6 of 56 (the chapter's bloodiest
        /// line, one rung before its climax, which is the ramp inverted).
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> is the only thing that can see any
        /// of that.
        /// </summary>
        public const float WarbringerCastEvery = 9f;

        /// <summary>
        /// What one roar takes off <em>every</em> standing ward.
        ///
        /// <b>Small, because it lands four times.</b> Two off a four-ward line is eight health a
        /// roar against a warlord's three, which is more in total and much less per ward — so the
        /// line comes down flat rather than one turret at a time, and no single mending answers it.
        /// Spread damage is the weaker shape for the same total, which is the right side to err on
        /// for the rung before the finale.
        /// </summary>
        public const int WarbringerCast = 2;

        /// <summary>
        /// How much faster the hill walks while a warbringer's roar is on it.
        ///
        /// <b>It was 1.9 and that took the line apart.</b> Measured on the shipped rung through
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>: at 1.9 an unhurried player
        /// finished with one ward standing out of four, which is a rung that is lost by anybody
        /// having a worse afternoon. At 1.55, with a longer quiet in front of it
        /// (<see cref="WarbringerAfter"/>), the same player finishes with three and the line down
        /// to 38 of 56 — bled hard, which is what a penultimate rung should feel like.
        /// </summary>
        public const float Rally = 1.55f;

        /// <summary>Seconds a roar lasts. Two roars never stack — the later one restarts it.</summary>
        public const float RallyFor = 5f;

        /// <summary>
        /// Where the warlord stops, as a march reading.
        ///
        /// <b>It never reaches the line, and that is the whole shape of the fight.</b> Everything
        /// else on this hill is answered by killing it before it arrives; a warlord walks to the
        /// middle of the hill, stands there, and hits the line from where nothing can stop it
        /// except the wards themselves. So the pressure it applies cannot be outrun — it can only
        /// be out-damaged, which is what makes the last wave a duel rather than a longer wave.
        /// </summary>
        public const float BossHold = .46f;

        /// <summary>
        /// Seconds it would take to cross the whole hill. It only walks <see cref="BossHold"/> of
        /// it, so the entrance a player actually watches is a little under six seconds.
        ///
        /// <b>It was 22, then 13, and it is 7 — the owner has now asked for it faster twice.</b> A
        /// creeper crosses the whole hill in fifteen; there was no reason for a boss to be slower
        /// per unit of ground than the smallest thing on the board except that slow reads as heavy
        /// — and past a few seconds it stops reading as heavy and starts reading as a wait. What
        /// carries the weight instead is its size, its own frames and the shake it arrives with.
        /// <c>SiegeRuleTests.AWarlordWalksOnBeforeItStands</c> holds the entrance under
        /// <b>four</b> seconds now, so the number cannot drift back.
        /// <para>
        /// <b>All four bosses moved together and the entrances stay in the order the sizes are
        /// in</b>, because the ladder a player reads is size-then-weight: the blightcaller walks on
        /// in about 4.1 seconds, the warlord in 3.2, the overlord in 3.0 and the warbringer in 2.7.
        /// Halving these is not free — a boss on its ground is a boss casting, so every one of them
        /// now opens fire three to four seconds earlier, which is why this went back through
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> like every other pacing change
        /// (37s).
        /// </para>
        /// </summary>
        public const float BossMarch = 7f;

        /// <summary>
        /// The quiet before the warlord, which is longer than the quiet before any other wave.
        ///
        /// <para>
        /// <b>He sends his raiders first and comes himself after them</b>, which is the pacing the
        /// finale wants and is also the one place this mode's own wave rule needed an exception.
        /// Invariant 37k's shortcut still applies — a player who has cleared the hill gets the
        /// warlord at once rather than standing about — so what this longer clock buys is entirely
        /// for the player who is <em>behind</em>: it stops a duel being stacked on top of a wave
        /// still swinging at the line, which is two fail states arriving together and reads as
        /// being cheated rather than as being outpaced.
        /// </para>
        /// <para>
        /// It is pinned by <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> like everything
        /// else here, and it is the number that moved when the warlord was first put on the hill:
        /// at <see cref="BetweenWaves"/> the shipped level's last brutes and its warlord reached
        /// the line within seconds of each other and the run was lost with two raiders left.
        /// </para>
        /// <para>
        /// <b>It came back down from 34 to 28 for the same verdict that shortened
        /// <see cref="BossMarch"/>, and that is not free.</b> The warlord now opens fire about
        /// eleven seconds earlier, which is eleven seconds of it overlapping the tail of the last
        /// wave — measured, an unhurried player finishes with 15 of the line's 56 rather than 21.
        /// <b>A pacing change is a difficulty change</b> (37s said the same thing about the fuel
        /// flight), and this one is the level getting tighter rather than the boss getting
        /// stronger. If it plays too tight, <see cref="BossCastEvery"/> is the constant to move —
        /// not this one, which is what the player asked for.
        /// </para>
        /// </summary>
        public const float BossAfter = 28f;

        /// <summary>Quiet between the warlord reaching its ground and its first spell.</summary>
        public const float BossWakes = 3.4f;

        /// <summary>Seconds between one spell and the next.</summary>
        public const float BossCastEvery = 5f;

        /// <summary>
        /// How long a spell is telegraphed before it leaves, and how long it is then in the air.
        ///
        /// <para>
        /// <b>Drawing numbers living in the rules, for <see cref="SwapFor"/>'s reason and one
        /// more.</b> A spell that landed on the frame the warlord decided to cast would take a
        /// ward down before anything had crossed the hill, which is the fault invariant 37s is
        /// about. And the wind-up is not only an animation: it is the window in which a
        /// <c>mending</c> is worth pouring into the ward that is about to be hit, so how long it
        /// lasts is a rule the player plays against rather than a flourish.
        /// </para>
        /// </summary>
        public const float BossTell = 1.15f, BossFlight = .45f;

        /// <summary>What one spell takes off a ward.</summary>
        public const int BossCast = 3;

        /// <summary>
        /// Blows a ward takes before it falls.
        ///
        /// <b>Drawn as a bar rather than as pips, because it is too many to count.</b> It was four
        /// while fuel faded on a clock; taking the fade out roughly doubled what a match delivers,
        /// so the hill had to grow, and a hill that can put four brutes on the line at once takes
        /// a line apart in three seconds against four. What this number really sets is *how long
        /// a leak is survivable*.
        ///
        /// <para>
        /// <b>It went from ten to fourteen when the warlord arrived, and that is a fact about the
        /// shape of a level rather than about how hard one should be.</b> At ten the shipped siege
        /// ended within seconds of the line coming down — which is the right texture for a level
        /// whose last wave is its climax, and the wrong one for a level that has a duel *after*
        /// its last wave: measured, the run was lost with two raiders left, the line falling to
        /// brutes that used to be the finale. A longer level needs a line that can carry a leak
        /// into the next phase, and the compensation is that a leak now bleeds for longer rather
        /// than that the level got easier — an unhurried player still finishes 43% down
        /// (<c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>, invariant 37j).
        /// </para>
        /// </summary>
        public const int WardHealth = 14;

        /// <summary>Seconds between a raider's blows once it has reached the line.</summary>
        public const float BlowEvery = 1.9f;

        /// <summary>
        /// Quiet before the first wave, and between one wave and the next.
        ///
        /// <para>
        /// <b>A wave now comes on a clock rather than when the last one is gone</b>, which is the
        /// owner's verdict after play: waiting for a clear meant a player who was winning was
        /// never under any pressure at all, because the hill politely stopped until they had
        /// finished. On a clock the waves overlap, so falling behind compounds - which is what a
        /// siege is.
        /// </para>
        /// <para>
        /// What it costs is the one guarantee the old rule bought for free: a run can now be
        /// *outpaced*, so <see cref="BetweenWaves"/> is the number that decides whether the level
        /// is holdable at all, and it is pinned by the simulation in
        /// <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> rather than reasoned about.
        /// </para>
        /// </summary>
        public const float FirstWaveAfter = 3.4f;
        public const float BetweenWaves = 26f;

        /// <summary>Seconds between one raider of a wave stepping out and the next.</summary>
        public const float RaiderSpacing = 1.35f;

        /// <summary>Lanes a raider may walk down. Wider than the ward line, so blows travel.</summary>
        public const int Lanes = 5;

        /// <summary>
        /// How many bands the hill is cut into for aiming, down its length.
        ///
        /// <para>
        /// <b>The hill is a grid, and it is a grid because a target has to be a thing you can
        /// see.</b> A blast used to be a radius around wherever a finger left the board, which is
        /// exact in the rule and unreadable on the screen: the ring said how far it reached and
        /// nothing said what was in it, so the player was asked to judge a distance against
        /// raiders that were walking. Four rows against five lanes makes twenty boxes, each about
        /// a raider and a half wide - big enough to be tapped, small enough that which box is
        /// obviously a decision.
        /// </para>
        /// <para>
        /// It is <c>SiegeView</c>'s number too: the boxes it draws are these boxes, which is
        /// invariant 33g's rule about the haul-road - the drawn thing and the played thing have
        /// to be one thing, and the cheapest way to guarantee that is for there to be only one.
        /// </para>
        /// </summary>
        public const int BlastRows = 4;

        /// <summary>Which band of the hill a march reading falls in.</summary>
        public static int RowOf(float march)
        {
            int row = (int)(march * BlastRows);
            return row < 0 ? 0 : row >= BlastRows ? BlastRows - 1 : row;
        }

        /// <summary>
        /// How many of the hill's rows this kind's <em>body</em> covers, counting up from its feet.
        ///
        /// <para>
        /// <b>A firepot hits what a player can see, and for a boss those were two different
        /// things.</b> A raider stands at one point on the hill and a blast takes the box that
        /// point is in (<see cref="RowOf"/>), which is exactly right while a raider is about a cell
        /// tall and its body and its feet are in the same box. A boss is drawn three cells and more
        /// on a hill four rows deep, so most of what a player is aiming at is in the box *above*
        /// the one it occupies — tap the thing and nothing happens, which came back from play as
        /// "the bombs don't hit bosses". They did; they hit its feet.
        /// </para>
        /// <para>
        /// <b>A drawing number living in the rules</b>, exactly as <see cref="BossTell"/> and
        /// <see cref="SwapFor"/> are and for the same reason: what a blast hits and what the board
        /// draws have to be one fact, and the cheapest way to guarantee that is for there to be
        /// only one. <c>SiegeView.TallOf</c> is the height this is the row count of, and
        /// <c>SiegeRuleTests</c> holds the two in step.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <b>Three, and it is measured rather than picked.</b> The hill is 4.34 cells deep across
        /// <see cref="BlastRows"/> rows, so a row is about 1.09 cells; a boss is drawn 3.0 to 3.5
        /// cells tall (<c>SiegeView.TallOf</c>), which is 2.8 to 3.2 rows of body. Ordinary raiders
        /// are one and a bit cells and stay at one.
        /// </remarks>
        public static int RowsOf(SiegeKind kind) => IsBoss(kind) ? 3 : 1;

        /// <summary>
        /// Whether a raider standing at <paramref name="march"/> is caught by a blast on
        /// <paramref name="row"/>.
        ///
        /// Its feet are in <see cref="RowOf"/> and its body reaches <em>up</em> the hill from
        /// there — up the screen is toward the top, which is the lower row index, because row
        /// nought is where a wave steps out and the last row is the ward line.
        /// </summary>
        public static bool Caught(SiegeKind kind, float march, int row)
        {
            int feet = RowOf(march);
            return row <= feet && row > feet - RowsOf(kind);
        }

        // ------------------------------------------------------------------ fuel in flight
        /// <summary>
        /// How long a match takes to reach the wards it feeds, and the three numbers it is made
        /// of.
        ///
        /// <para>
        /// <b>These are drawing numbers living in the rules, and that is deliberate.</b> A swap
        /// resolves in an instant and its animation takes the better part of a second — the gems
        /// swap, they burst, motes fly up to the line. Fuel credited at the *instant of the swap*
        /// therefore reaches the wards before the player has seen anything leave the field, and
        /// what that looks like is a turret killing a raider before the gems it was paid for have
        /// gone off. Reported from play in exactly those words.
        /// </para>
        /// <para>
        /// So fuel is <b>in flight</b>: <see cref="Swap"/> books it and <see cref="Advance"/>
        /// lands it, on the schedule the view really draws. Putting the schedule here rather than
        /// in the view is what stops the two drifting — a mote that arrives before or after its
        /// fuel does is the same bug again, and there is no gate that could see it.
        /// </para>
        /// </summary>
        public const float SwapFor = .16f, BeatFor = .40f, FuelFlight = .42f;

        /// <summary>When a cascade's <paramref name="beat"/>th wave of fuel reaches the line.</summary>
        public static float FuelLands(int beat) => SwapFor + beat * BeatFor + FuelFlight;

        /// <summary>Whether this kind is one of the four bosses — the things that stand and cast.</summary>
        public static bool IsBoss(SiegeKind kind)
            => kind == SiegeKind.Boss || kind == SiegeKind.Overlord
            || kind == SiegeKind.Blightcaller || kind == SiegeKind.Warbringer;

        /// <summary>
        /// What each of the four bosses does when its spell lands.
        ///
        /// <para>
        /// <b>Four verbs rather than four numbers, and that is the whole of what makes them four
        /// bosses.</b> A chapter shipped two of these told apart by their health, their cadence
        /// and their hue, and it read — correctly — as one fight with the dial moved. So each one
        /// takes a different thing: <see cref="SiegeSpell.Smite"/> takes a ward's <em>health</em>,
        /// <see cref="SiegeSpell.Douse"/> takes its <em>fire</em>,
        /// <see cref="SiegeSpell.Rally"/> takes the player's <em>clock</em>, and
        /// <see cref="SiegeSpell.Sunder"/> takes the <em>rank</em> they earned. Each has a
        /// different answer, and only two of the four are answered by a mending.
        /// </para>
        /// <para>
        /// This is invariant 26h's test asked of a boss: what does the player decide about it, and
        /// can they be wrong. A boss that only ever did what the boss before it did, harder, is
        /// the mirror and the wick a sister mode withdrew twice for competing on degree rather
        /// than on kind (26g).
        /// </para>
        /// </summary>
        public static SiegeSpell SpellOf(SiegeKind kind)
            => kind == SiegeKind.Overlord ? SiegeSpell.Sunder
             : kind == SiegeKind.Blightcaller ? SiegeSpell.Douse
             : kind == SiegeKind.Warbringer ? SiegeSpell.Rally
             : SiegeSpell.Smite;

        /// <summary>
        /// Whether this boss's spell is aimed at a ward at all.
        ///
        /// <b>A warbringer's is not</b>, and that is the one clause the cast/flight/land spine
        /// needed for a fourth boss: a roar is thrown at the hill, so its flight carries no ward
        /// and everything downstream — the tell, <c>Arrive</c>, the view's ring — asks this rather
        /// than testing an index nobody set.
        /// </summary>
        public static bool AimsAtAWard(SiegeKind kind) => SpellOf(kind) != SiegeSpell.Rally;

        /// <summary>What a boss is called, for a message. Never shown to a player.</summary>
        public static string NameOf(SiegeKind kind)
        {
            for (int i = 0; i < SiegeLayout.BossNames.Length; i++)
                if (SiegeLayout.BossNames[i].Kind == kind) return SiegeLayout.BossNames[i].Name;

            return kind == SiegeKind.Bulwark ? "bulwark"
                 : kind == SiegeKind.Brute ? "brute" : "creeper";
        }

        public static int HealthOf(SiegeKind kind)
            => kind == SiegeKind.Overlord ? OverlordHealth
             : kind == SiegeKind.Warbringer ? WarbringerHealth
             : kind == SiegeKind.Boss ? BossHealth
             : kind == SiegeKind.Blightcaller ? BlightHealth
             : kind == SiegeKind.Bulwark ? BulwarkHealth
             : kind == SiegeKind.Brute ? BruteHealth : CreeperHealth;

        public static float MarchOf(SiegeKind kind)
            => kind == SiegeKind.Overlord ? OverlordMarch
             : kind == SiegeKind.Warbringer ? WarbringerMarch
             : kind == SiegeKind.Boss ? BossMarch
             : kind == SiegeKind.Blightcaller ? BlightMarch
             : kind == SiegeKind.Bulwark ? BulwarkMarch
             : kind == SiegeKind.Brute ? BruteMarch : CreeperMarch;

        /// <summary>Seconds between one spell and the next, for whichever boss this is.</summary>
        public static float CastEveryFor(SiegeKind kind)
            => kind == SiegeKind.Overlord ? OverlordCastEvery
             : kind == SiegeKind.Warbringer ? WarbringerCastEvery
             : kind == SiegeKind.Blightcaller ? BlightCastEvery : BossCastEvery;

        /// <summary>
        /// What one of this boss's spells takes off a ward's health.
        ///
        /// <b>Nought for the blightcaller, and that is not an omission</b> — it takes a ward's
        /// fire rather than its health, so asking what a mending is worth against it has the answer
        /// "nothing". The warbringer's number is per ward and lands on <em>all</em> of them
        /// (<see cref="WarbringerCast"/>), which is why it is the smallest of the three.
        /// </summary>
        public static int CastOf(SiegeKind kind)
            => kind == SiegeKind.Overlord ? OverlordCast
             : kind == SiegeKind.Warbringer ? WarbringerCast
             : kind == SiegeKind.Boss ? BossCast : 0;

        /// <summary>
        /// What one swing at the line costs a ward.
        ///
        /// <b>A boss swings at nothing</b>, because none of the four reaches the line — what they
        /// cost the line is their spell, from where they stand. The warbringer briefly was the
        /// exception, and holding the middle is what it was changed back to.
        /// </summary>
        public static int BlowOf(SiegeKind kind)
            => IsBoss(kind) ? 0
             : kind == SiegeKind.Bulwark ? BulwarkBlow
             : kind == SiegeKind.Brute ? BruteBlow : CreeperBlow;

        /// <summary>How far down the hill this kind comes before it stops. A boss stops early.</summary>
        public static float HoldOf(SiegeKind kind)
            => kind == SiegeKind.Overlord ? OverlordHold
             : kind == SiegeKind.Warbringer ? WarbringerHold
             : kind == SiegeKind.Boss ? BossHold
             : kind == SiegeKind.Blightcaller ? BlightHold : 1f;

        /// <summary>
        /// Whether this boss can bring a ward down at all, given long enough.
        ///
        /// <b>One of the four cannot.</b> A blightcaller takes fuel and never health, so a level
        /// whose only threat were one could not be lost — which is what
        /// <c>ModeValidator.Threatens</c> asks rather than assuming that a boss is by definition
        /// dangerous. It was two while the warbringer took ground instead of health.
        /// </summary>
        public static bool EndangersTheLine(SiegeKind kind) => CastOf(kind) > 0;

        /// <summary>
        /// The quiet before a wave, which is longer before a boss and shortest before a
        /// warbringer.
        ///
        /// <para>
        /// <b>Per kind, and the two exceptions point opposite ways for the same reason.</b>
        /// Invariant 37t made the quiet before a <em>warlord</em> longer than any other so that a
        /// duel is never stacked on a wave still swinging at the line — two fail states arriving
        /// together read as being cheated. A warbringer wants precisely the opposite: half its
        /// spell is a rally, and a rally over an empty hill is a mechanic that rejects nothing
        /// (invariant 5d), so it comes while the last wave is still walking and the two are the
        /// fight.
        /// </para>
        /// <para>
        /// Both numbers are pinned by <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> rather
        /// than argued about, because a pacing change is a difficulty change (37s).
        /// </para>
        /// </summary>
        public static float RestBefore(SiegeKind kind)
            => kind == SiegeKind.Warbringer ? WarbringerAfter
             : IsBoss(kind) ? BossAfter : BetweenWaves;

        /// <summary>
        /// The quiet before a warbringer: short, so it arrives into a hill worth rallying.
        ///
        /// <b>Shorter than <see cref="BetweenWaves"/> and much shorter than
        /// <see cref="BossAfter"/></b>, which is the whole of why it is a number of its own — see
        /// <see cref="RestBefore"/>. It was 14, which with a rally of 1.9 cost an unhurried player
        /// three of four wards; 17 with a rally of 1.55 leaves three standing on a line bled to 38
        /// of 56. Neither number was reasoned about.
        /// </summary>
        public const float WarbringerAfter = 17f;

        /// <summary>
        /// Gems an ordinary match clears, cascades included, in tenths.
        ///
        /// <para>
        /// <b>Measured, not reasoned about — and the first version of this number was reasoned
        /// about and wrong.</b> Par was <c>health / (3 gems x damage)</c> on the argument that a
        /// match clears at least three, so no run of fewer matches could deliver the health: a
        /// floor. It is not one, because a match on a full field <em>cascades</em>, and the gems a
        /// cascade takes are fuel too. Measured over a played run it is about five and a half, so
        /// the "floor" sat nearly twice above real play and three stars was free.
        /// </para>
        /// <para>
        /// <b>So par here is a calibrated estimate and not a proof, and that is the honest name
        /// for it.</b> It is pinned by <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>, which
        /// plays a level and fails if an ordinary run lands outside the ladder - which is the only
        /// instrument a mode with no search has. It is also a property of a *four-colour* field:
        /// a level dealing five colours would cascade less and want a smaller number, which is
        /// the limitation to remember before authoring one.
        /// </para>
        /// </summary>
        public const int MatchGemsTenths = 55;

        /// <summary>
        /// What one match delivers: the gems it clears, turned into bolts and every one of them
        /// landing on the raider that ward is strong against.
        ///
        /// <para>
        /// <b>Bolts, not gems, and that was an identity rather than a rule.</b> It read
        /// <c>gems x damage x 2</c>, which is only the same thing while a gem buys exactly one
        /// bolt — true for as long as a gem was worth one fuel and a bolt cost one, and silently
        /// load-bearing under every par, every star line and every utility charge in the mode. The
        /// moment a gem was worth two (<see cref="FuelPerGemTenths"/>) that formula would have
        /// halved what a match delivers and doubled every par in the chapter, with each number
        /// still looking perfectly plausible. It says <c>bolts</c> out loud now.
        /// </para>
        /// <para>
        /// Integer arithmetic throughout, for <c>LevelTuning</c>'s reason: a graded number decided
        /// by a float is a number three code generators round three ways. One division, at the
        /// end, so nothing truncates on the way.
        /// </para>
        /// </summary>
        public const int PerfectMatch =
            MatchGemsTenths * FuelPerGemTenths * ShotDamage * WeakMultiplier
            / (FuelPerShotTenths * 10);

        /// <summary>
        /// The fewest matches that could hold this level, and so what a run is graded against.
        ///
        /// <para>
        /// <b>Arithmetic rather than a search, and it is a genuine floor.</b> A match puts at most
        /// <see cref="PerfectMatch"/> damage into the hill — three gems, every one of them spent
        /// as a bolt, every bolt landing on a raider of that ward's own colour — so no run of
        /// fewer matches than this can have destroyed what the level sends. Everything a good
        /// player does beyond that is free upside the count does not model: a four-match is worth
        /// more than a three, and a cascade fuels a second ward for no move at all. That is the
        /// right direction for a floor to be wrong in — three stars stays reachable, which is the
        /// failure invariant 22 says never to ship (a band nothing can land in).
        /// </para>
        /// <para>
        /// It is <em>not</em> a proof the way every other mode's par is, and that is the honest
        /// summary of what a clock costs. See <see cref="SiegeLayout"/>.
        /// </para>
        /// <para>
        /// <b>And a level that deals cogs loosens it further, deliberately.</b> A rank-four ward
        /// turns a match into 2.33 times what <see cref="PerfectMatch"/> assumes, so on such a
        /// level this is not even the floor it is elsewhere — it over-states the matches a good
        /// run needs, which keeps three stars reachable and is the direction invariant 22 says to
        /// err in. Modelling ranks here was the alternative and is worse: it would have to guess
        /// how many cogs a player takes and which turret they spend them on, and a par built on a
        /// guess about play is a par nobody can check. What checks it instead is somebody playing
        /// it — <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>, run over every rung.
        /// </para>
        /// </summary>
        public static int Par(SiegeLayout layout)
        {
            if (layout == null) return 1;

            int health = 0;
            for (int w = 0; w < layout.Coming.Length; w++)
                for (int i = 0; i < layout.Coming[w].Length; i++)
                    health += HealthOf(layout.KindAt(w, i));

            int par = (health + PerfectMatch - 1) / PerfectMatch;
            return par < 1 ? 1 : par;
        }
    }

    /// <summary>
    /// What is walking down the hill: an ordinary raider, a brute, or the warlord.
    ///
    /// <b>An enum rather than a second bool, and the third member is why.</b> It was
    /// <c>bool Brute</c>, which is exactly right for two kinds and becomes a pair of flags that
    /// can both be true the moment there is a third — and every table keyed on it (health, march,
    /// blow, how far it comes) would then have had to agree about which flag wins.
    /// </summary>
    public enum SiegeKind
    {
        Creeper,
        Brute,

        /// <summary>The warlord: it holds the middle of the hill and hits the line from there.</summary>
        Boss,

        /// <summary>
        /// The overlord: what the last rung of a chapter ends on.
        ///
        /// <b>Appended rather than inserted</b>, because these ordinals reach analytics on every
        /// run this mode has ever recorded — the same rule <c>DefeatReason</c> keeps its retired
        /// members for.
        /// </summary>
        Overlord,

        /// <summary>The blightcaller: it puts a ward out rather than taking it down.</summary>
        Blightcaller,

        /// <summary>The warbringer: it roars, and the hill charges.</summary>
        Warbringer,

        /// <summary>
        /// The bulwark: it carries a shield, so it walks slowly and shrugs off anything that is
        /// not its own colour.
        ///
        /// <para>
        /// <b>Appended</b>, for the reason every member of this enum is: the ordinals reach
        /// analytics on every run this mode has recorded.
        /// </para>
        /// <para>
        /// <b>It is the first raider whose answer is a colour rather than a quantity.</b> A
        /// creeper and a brute differ only in how much of the same thing they need — health,
        /// speed, blow — so a player beats both by matching more. A bulwark halves every bolt
        /// that is not its own colour and takes its own colour in full (<see
        /// cref="SiegeTuning.ShieldSoakTenths"/>), which puts a <b>fourfold</b> spread between
        /// feeding the right ward and feeding any other: the elemental double is already 2x, and
        /// the shield makes the wrong answer 0.5x. That is the one raider on this hill that
        /// punishes taking the biggest match on the field, which is the mistake this mode is
        /// about.
        /// </para>
        /// </summary>
        Bulwark,
    }

    /// <summary>
    /// What a boss's spell does when it lands.
    ///
    /// <para>
    /// <b>Its own vocabulary rather than a flag on the kind, because the view reads it too.</b>
    /// Every one of the four is drawn differently, aimed differently and answered differently, and
    /// a rule that only <c>SiegeTuning</c> knew would leave the drawing to guess from health
    /// numbers — which is how a mode ends up with two bosses that look the same.
    /// </para>
    /// <para>
    /// <b>Appended, like <see cref="SiegeKind"/></b>: these reach analytics through
    /// <c>SiegeSpellLanded</c> and an ordinal that moves rewrites history.
    /// </para>
    /// </summary>
    public enum SiegeSpell
    {
        /// <summary>Takes a ward's health. The warlord's, and the one a mending answers.</summary>
        Smite,

        /// <summary>Empties a ward's fuel and smothers it. The blightcaller's.</summary>
        Douse,

        /// <summary>Sets the whole hill charging. The warbringer's, and aimed at no ward.</summary>
        Rally,

        /// <summary>Takes health <em>and</em> a rank the player earned. The overlord's.</summary>
        Sunder,
    }

    /// <summary>One raider on the hill.</summary>
    public sealed class SiegeRaider
    {
        public readonly int Id;

        /// <summary>Which of <see cref="SiegeLayout.Letters"/> it wears, as an index.</summary>
        public readonly int Colour;

        public readonly SiegeKind Kind;

        /// <summary>Which lane it walks down, 0 at the left.</summary>
        public readonly int Lane;

        /// <summary>Seconds before it steps out. Not on the hill until this reaches nought.</summary>
        public float Wait;

        /// <summary>How far down the hill it is: 0 at the top, 1 at the ward line.</summary>
        public float March;

        /// <summary>
        /// How far down it comes before it stops. One for everything but a boss.
        ///
        /// <b>Readonly again.</b> It was briefly written by a warbringer's roar, which lunged it
        /// further down the hill each time; that was withdrawn after play (a boss that walks spends
        /// the fight being somewhere else), and with it went the only thing that ever moved a
        /// raider's ground after it was minted.
        /// </summary>
        public readonly float Hold;

        public int Health;
        public readonly int MaxHealth;

        public bool Alive = true;

        /// <summary>Seconds until its next blow, once it has arrived.</summary>
        public float Blow;

        /// <summary>Seconds until its next spell. A warlord's only, and only once it is in place.</summary>
        public float Spell;

        /// <summary>Set for one frame after it has been hit, so the view can flash it.</summary>
        public float Flash;

        public SiegeRaider(int id, int colour, SiegeKind kind, int lane, float wait)
        {
            Id = id;
            Colour = colour;
            Kind = kind;
            Lane = lane;
            Wait = wait;
            Hold = SiegeTuning.HoldOf(kind);
            MaxHealth = SiegeTuning.HealthOf(kind);
            Health = MaxHealth;
            Blow = SiegeTuning.BlowEvery * .5f;
            Spell = SiegeTuning.BossWakes;
        }

        public bool Brute => Kind == SiegeKind.Brute;

        /// <summary>Whether this is a boss of any of the four — the thing that stands and casts.</summary>
        public bool Boss => SiegeTuning.IsBoss(Kind);

        /// <summary>Whether this is the greatest of the four.</summary>
        public bool Overlord => Kind == SiegeKind.Overlord;

        /// <summary>What this one's spell does. <see cref="SiegeSpell.Smite"/> for anything that has none.</summary>
        public SiegeSpell Spellcraft => SiegeTuning.SpellOf(Kind);

        public bool OnTheHill => Wait <= 0f;

        /// <summary>
        /// Whether it is standing at the line and swinging.
        ///
        /// <b>A warlord never is</b>, because <see cref="Hold"/> stops it short of it — which is
        /// what makes <see cref="SiegeBoard.Swing"/> need no clause about bosses at all.
        /// </summary>
        public bool AtTheLine => Alive && OnTheHill && Hold >= 1f && March >= 1f;

        /// <summary>Whether it has reached the ground it holds and may start casting.</summary>
        public bool InPlace => Alive && OnTheHill && March >= Hold;
    }

    /// <summary>One ward on the line.</summary>
    public sealed class SiegeWard
    {
        /// <summary>Which of <see cref="SiegeLayout.Letters"/> it burns, as an index.</summary>
        public readonly int Colour;

        public float Fuel;
        public int Health = SiegeTuning.WardHealth;
        public bool Alive = true;

        /// <summary>Seconds until its next bolt.</summary>
        public float Cool;

        /// <summary>
        /// How many cogs have been spent on this ward, nought to <see cref="SiegeTuning.MaxRank"/>.
        ///
        /// <b>Rank rather than level, so the arithmetic has no off-by-one in it.</b> Every table
        /// that reads it is a multiplier on nought (<see cref="SiegeTuning.DamageAt"/>,
        /// <see cref="SiegeTuning.FuelShotTenths"/>); the number a <em>player</em> is shown is
        /// <see cref="Level"/>, which is this plus one, and it exists exactly once so a badge and
        /// a bolt can never disagree about what tier a turret is.
        /// </summary>
        public int Rank;

        /// <summary>What the badge on this ward says: one to five.</summary>
        public int Level => Rank + 1;

        /// <summary>
        /// Seconds this ward stands dark, having been doused by a blightcaller.
        ///
        /// <b>A countdown rather than a flag</b>, because what a player has to read off it is
        /// <em>how long</em>: the decision the blightcaller asks is which colour to feed next, and
        /// that is only a decision if the answer changes as the seconds run out.
        /// </summary>
        public float Dark;

        public SiegeWard(int colour) => Colour = colour;

        /// <summary>Whether it is standing but smothered. Fuel poured in is still fuel.</summary>
        public bool Doused => Alive && Dark > 0f;

        /// <summary>Whether it can get a bolt away. An upgraded ward needs less to do it.</summary>
        public bool Fuelled => Alive && !Doused && Fuel >= SiegeTuning.FuelShot(Rank);

        public float Charge => Fuel / SiegeTuning.WardCapacity;

        /// <summary>Whether another cog would be worth anything to this ward.</summary>
        public bool Upgradable => Alive && Rank < SiegeTuning.MaxRank;

        /// <summary>
        /// Puts this ward out: what a blightcaller's spell does when it lands.
        ///
        /// It takes the fuel <em>and</em> the seconds, because taking only one of the two is not
        /// a mechanic — emptying a full ward it is about to fire from costs nothing a moment
        /// later, and smothering a ward with nothing in it costs nothing at all.
        /// </summary>
        public void Snuff()
        {
            Fuel = 0f;
            Dark = SiegeTuning.Douse;
        }

        /// <summary>
        /// Knocks a rank off: what an overlord's spell does on top of its damage.
        ///
        /// Clamped at nought and answering whether it really took one, so a view can draw the
        /// badge falling and say nothing when there was nothing to take.
        /// </summary>
        public bool Sunder()
        {
            if (Rank <= 0) return false;

            Rank -= SiegeTuning.OverlordSunder;
            if (Rank < 0) Rank = 0;
            return true;
        }
    }

    /// <summary>A bolt that left a ward, for the view to draw.</summary>
    public readonly struct SiegeBolt
    {
        public readonly int Ward, Raider, Damage;
        public readonly bool Weak, Killed;

        public SiegeBolt(int ward, int raider, int damage, bool weak, bool killed)
        {
            Ward = ward;
            Raider = raider;
            Damage = damage;
            Weak = weak;
            Killed = killed;
        }
    }

    /// <summary>A blow that landed on the line, for the view to draw.</summary>
    /// <summary>
    /// What a utility did to one raider - a hit that came from the player's own hand rather
    /// than out of a ward.
    ///
    /// Its own record and not a <see cref="SiegeBolt"/>, because a bolt names the ward that
    /// fired it and this has no ward: the view draws the two differently and analytics has to
    /// be able to tell them apart.
    /// </summary>
    public readonly struct SiegeStrike
    {
        public readonly int Raider, Damage;
        public readonly bool Killed;

        public SiegeStrike(int raider, int damage, bool killed)
        {
            Raider = raider;
            Damage = damage;
            Killed = killed;
        }
    }

    /// <summary>
    /// A cog going, and what it was worth.
    ///
    /// <para>
    /// <b>Its own record on the beat rather than a flag on the ward, because the view has to draw
    /// a journey.</b> The player's decision was made on the field — <em>which colour do I line up
    /// beside that cog</em> — and it is paid on the line, so what the drawing has to say is that
    /// those two things are one thing. <see cref="Cell"/> is where it stood and
    /// <see cref="Ward"/> is where it went.
    /// </para>
    /// <para>
    /// <see cref="Rank"/> is what the ward came out at, and it is <b>nought when nothing
    /// happened</b> — a cog taken by a colour whose ward has fallen, or is already at the top of
    /// the ladder, is a cog spent for nothing. That is a real mistake with a real cost, which is
    /// what makes choosing the colour a decision (invariant 26h) rather than a formality, and the
    /// view says so by drawing the cog coming apart where it stood and going nowhere.
    /// </para>
    /// </summary>
    public readonly struct SiegeRise
    {
        public readonly int Cell, Ward, Rank;

        /// <summary>Which of <see cref="SiegeLayout.Letters"/> took it, as an index.</summary>
        public readonly int Colour;

        public SiegeRise(int cell, int ward, int rank, int colour)
        {
            Cell = cell;
            Ward = ward;
            Rank = rank;
            Colour = colour;
        }

        /// <summary>Whether a ward really went up. False for a cog that bought nothing.</summary>
        public bool Rose => Ward >= 0 && Rank > 0;
    }

    public readonly struct SiegeBlow
    {
        public readonly int Ward, Raider, Damage;
        public readonly bool Felled;

        public SiegeBlow(int ward, int raider, int damage, bool felled)
        {
            Ward = ward;
            Raider = raider;
            Damage = damage;
            Felled = felled;
        }
    }

    /// <summary>
    /// A warlord beginning a spell: which ward it has chosen, and how long the player has.
    ///
    /// <b>Its own record and raised the moment the spell is <em>decided</em></b>, which is the
    /// point of it: the wind-up is what the view draws over the ward that is about to be hit, and
    /// it is the window a <c>mending</c> is worth spending in. A cast that was reported only when
    /// it landed would be a fail state arriving with no warning, which is the one thing this mode
    /// already refuses to do with a wave.
    /// </summary>
    public readonly struct SiegeCast
    {
        public readonly int Raider;

        /// <summary>Which ward it is aimed at, or <b>-1</b> for a warbringer's roar at the hill.</summary>
        public readonly int Ward;

        /// <summary>Which of the four it is. The tell is drawn differently for each.</summary>
        public readonly SiegeSpell Craft;

        /// <summary>Seconds from now until it lands. <see cref="SiegeTuning.BossTell"/> of that is the tell.</summary>
        public readonly float In;

        public SiegeCast(int raider, int ward, SiegeSpell craft, float @in)
        {
            Raider = raider;
            Ward = ward;
            Craft = craft;
            In = @in;
        }
    }

    /// <summary>
    /// A spell landing on the line.
    ///
    /// Its own record and not a <see cref="SiegeBlow"/>, for <see cref="SiegeStrike"/>'s reason:
    /// a blow is swung at the line by something standing at it, a spell is thrown from the middle
    /// of the hill, and the view draws the two nothing alike.
    /// </summary>
    public readonly struct SiegeSpellLanded
    {
        public readonly int Raider, Ward, Damage;
        public readonly bool Felled;

        /// <summary>Which of the four it was. The view draws each of them nothing alike.</summary>
        public readonly SiegeSpell Craft;

        /// <summary>Whether a rank really came off. Only ever true of a <see cref="SiegeSpell.Sunder"/>.</summary>
        public readonly bool Sundered;

        public SiegeSpellLanded(int raider, int ward, SiegeSpell craft, int damage, bool felled,
                                bool sundered = false)
        {
            Raider = raider;
            Ward = ward;
            Craft = craft;
            Damage = damage;
            Felled = felled;
            Sundered = sundered;
        }
    }

    /// <summary>Everything that happened in one step of the clock.</summary>
    public sealed class SiegeReport
    {
        public readonly List<SiegeBolt> Bolts = new List<SiegeBolt>(16);
        public readonly List<SiegeBlow> Blows = new List<SiegeBlow>(4);
        public readonly List<SiegeCast> Casts = new List<SiegeCast>(2);
        public readonly List<SiegeSpellLanded> Spells = new List<SiegeSpellLanded>(2);
        public readonly List<int> Arrived = new List<int>(8);

        /// <summary>The wave that has just stepped out, or -1.</summary>
        public int Wave = -1;

        public void Clear()
        {
            Bolts.Clear();
            Blows.Clear();
            Casts.Clear();
            Spells.Clear();
            Arrived.Clear();
            Wave = -1;
        }

        public bool Any => Bolts.Count > 0 || Blows.Count > 0 || Casts.Count > 0
                        || Spells.Count > 0 || Arrived.Count > 0 || Wave >= 0;
    }

    /// <summary>Fuel a match has earned that has not reached its ward yet.</summary>
    public struct SiegeCharge
    {
        public int Ward;
        public float Fuel;
        public float In;
    }

    /// <summary>One gem falling, or arriving. <see cref="From"/> below nought is a new gem.</summary>
    public readonly struct SiegeDrop
    {
        public readonly int Column, From, To, Colour;

        public SiegeDrop(int column, int from, int to, int colour)
        {
            Column = column;
            From = from;
            To = to;
            Colour = colour;
        }

        public bool IsNew => From < 0;
    }

    /// <summary>One beat of a cascade: what went, what it fuelled, and what fell into the gap.</summary>
    public sealed class SiegeBeat
    {
        public readonly List<int> Cleared = new List<int>(12);
        public readonly List<SiegeDrop> Drops = new List<SiegeDrop>(24);

        /// <summary>Every cog this beat took, and what each was worth.</summary>
        public readonly List<SiegeRise> Rises = new List<SiegeRise>(2);

        /// <summary>Fuel this beat put into each ward, in ward order.</summary>
        public float[] Fuel;

        /// <summary>Which beat of the cascade this is, counting from one.</summary>
        public int Depth;
    }

    /// <summary>What one swap turned into.</summary>
    public sealed class SiegeTurn
    {
        public int A, B;
        public readonly List<SiegeBeat> Beats = new List<SiegeBeat>(4);

        /// <summary>Gems cleared altogether, which is what the flourish readout counts.</summary>
        public int Worth;
    }

    /// <summary>
    /// A siege: a field of gems, a line of wards and a hill with raiders coming down it.
    ///
    /// <para>
    /// <b>The clock is passed in.</b> Nothing here reads <c>Time</c> — <see cref="Advance"/> takes
    /// the seconds that have gone by, so the whole mode is Domain and can be stepped by a test at
    /// whatever rate a test likes. That is the same bargain every board here makes and the reason
    /// this one is not simply written inside the view.
    /// </para>
    /// </summary>
    public sealed class SiegeBoard : IProtoBoard
    {
        /// <summary>A cell with nothing in it, which only ever exists mid-resolve.</summary>
        public const char Hole = '.';

        public readonly SiegeLayout Layout;

        readonly char[] _cells;
        readonly SiegeWard[] _wards;
        readonly List<SiegeRaider> _raiders = new List<SiegeRaider>(24);
        readonly SiegeReport _report = new SiegeReport();
        readonly List<SiegeCharge> _flying = new List<SiegeCharge>(16);

        /// <summary>A spell that has been cast and has not arrived. See <see cref="Conjure"/>.</summary>
        struct Flight
        {
            public int Raider, Ward;
            public SiegeSpell Craft;
            public float In;
        }

        readonly List<Flight> _spells = new List<Flight>(4);

        /// <summary>
        /// Seconds left on a warbringer's roar, or nought.
        ///
        /// <b>One number for the whole hill rather than one per raider</b>, because a roar is a
        /// fact about the ground and not about who is standing on it — a raider that steps out
        /// mid-roar charges with the rest, which is what a player watching the hill expects and
        /// what a per-raider timer would quietly get wrong.
        /// </summary>
        float _roar;

        uint _rng;
        int _wave;
        int _minted;
        int _felled;
        float _rest;

        /// <summary>Whether the authored field stood a cog on it. See <see cref="Upgrades"/>.</summary>
        readonly bool _seeded;

        SiegeBoard(SiegeLayout layout)
        {
            Layout = layout;
            _cells = layout.Grid.Copy();
            _rng = layout.Seed;

            for (int i = 0; i < _cells.Length; i++)
                if (_cells[i] == SiegeLayout.Cog) { _seeded = true; break; }

            _wards = new SiegeWard[layout.Wards.Length];
            for (int i = 0; i < _wards.Length; i++)
                _wards[i] = new SiegeWard(SiegeLayout.Letters.IndexOf(layout.Wards[i]));

            _rest = SiegeTuning.FirstWaveAfter;
        }

        public static SiegeBoard Build(SiegeLayout layout) => new SiegeBoard(layout);

        // ------------------------------------------------------------------ the field
        public int Width => Layout.Grid.Width;
        public int Height => Layout.Grid.Height;
        public int Count => _cells.Length;

        public char At(int index) => index >= 0 && index < _cells.Length ? _cells[index] : Hole;

        public int ColourAt(int index) => SiegeLayout.Letters.IndexOf(At(index));

        public int IndexOf(int x, int y) => y * Width + x;

        public IReadOnlyList<SiegeWard> Wards => _wards;

        /// <summary>Fuel booked and still crossing the field. Nothing grades on it.</summary>
        public IReadOnlyList<SiegeCharge> Flying => _flying;
        public IReadOnlyList<SiegeRaider> Raiders => _raiders;

        public int WardsStanding
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _wards.Length; i++) if (_wards[i].Alive) n++;
                return n;
            }
        }

        /// <summary>The wave now on the hill, counting from one. Nought before the first.</summary>
        public int Wave => _wave;

        /// <summary>How many waves this siege sends, the warlord's included.</summary>
        public int Waves => Layout.Waves.Length;

        /// <summary>Whether the wave now on the hill is the warlord's.</summary>
        public bool BossWave => Layout.HasBoss && _wave == Layout.BossWave + 1;

        /// <summary>The warlord, while it is standing, or null.</summary>
        public SiegeRaider Warlord
        {
            get
            {
                for (int i = 0; i < _raiders.Count; i++)
                    if (_raiders[i].Boss && _raiders[i].Alive) return _raiders[i];

                return null;
            }
        }

        // ------------------------------------------------------------------ IProtoBoard
        public bool IsFinished => GoalsLeft == 0;

        public int Goals => Layout.RaiderCount;

        public int GoalsLeft => Goals - _felled;

        /// <summary>
        /// How many raiders are standing on the hill right now.
        ///
        /// <b>Not <see cref="GoalsLeft"/>, which counts everything the level has left to send.</b>
        /// A utility that lands on the hill can only reach what is already walking, so this is what
        /// decides whether one would do anything at all.
        /// </summary>
        public int OnTheHill
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _raiders.Count; i++)
                    if (_raiders[i].Alive && _raiders[i].OnTheHill) n++;

                return n;
            }
        }

        /// <summary>
        /// Whether a move on this board could still mean anything.
        ///
        /// <b>A line with nothing standing on it is where a run of this mode ends</b>, and it is
        /// honestly this question rather than a special case: with every ward down, a match feeds
        /// nothing, so there is no legal move left however many gems are on the field. The gem
        /// field itself always has a swap — <see cref="Settle"/> deals it again rather than
        /// letting it lock (invariant 20j's second test: every input has to move something).
        /// </summary>
        public bool AnyMove => WardsStanding > 0;

        /// <summary>
        /// <b>Never stranded, and that is arithmetic rather than generosity.</b> A run here ends
        /// when the last ward falls — <see cref="AnyMove"/> — and what a continue sells is the
        /// line itself: <see cref="Rally"/> puts every turret back up at full health with the
        /// hill exactly where it stood. So a fallen line is a shortage that a purchase really
        /// does fix, which is the only thing this predicate is asked (invariant 28f), and
        /// answering <c>true</c> would refuse a rescue to somebody who could still win.
        ///
        /// <para>
        /// <b>Note what does not make it true either.</b> A pod carrying a cage jams the line
        /// rather than passing through it (invariant 33b), so every goal a board opened with is
        /// still standing on it however long the run goes on — more cores always help. And the
        /// gem field is dealt again rather than allowed to lock (invariant 20j), so there is
        /// always a swap. There is no siege state no purchase rescues.
        /// </para>
        /// <para>
        /// It is <em>this</em> rather than the run's own gate that decides whether an offer is
        /// honest. Whether one is actually made is <c>RunContinueFlow</c>'s — a run nobody is
        /// charged for is a run nobody is sold, so the opening rungs of the chapter never see it.
        /// </para>
        /// </summary>
        public bool Stranded => false;

        // ------------------------------------------------------------------ swapping
        public bool Adjacent(int a, int b)
        {
            if (a < 0 || b < 0 || a >= Count || b >= Count || a == b) return false;

            int ax = a % Width, ay = a / Width, bx = b % Width, by = b / Width;
            return Math.Abs(ax - bx) + Math.Abs(ay - by) == 1;
        }

        /// <summary>Whether swapping these two would line anything up.</summary>
        public bool Lines(int a, int b)
        {
            if (!Adjacent(a, b)) return false;
            if (_cells[a] == _cells[b]) return false;

            char keepA = _cells[a], keepB = _cells[b];
            _cells[a] = keepB;
            _cells[b] = keepA;

            bool any = SiegeLayout.Runs(_cells, Width, Height).Count > 0;

            _cells[a] = keepA;
            _cells[b] = keepB;
            return any;
        }

        /// <summary>Whether any swap on this field would line anything up.</summary>
        public bool AnySwap()
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int here = IndexOf(x, y);
                    if (x + 1 < Width && Lines(here, here + 1)) return true;
                    if (y + 1 < Height && Lines(here, here + Width)) return true;
                }

            return false;
        }

        /// <summary>
        /// Swaps two gems and resolves everything that follows, fuelling the wards as it goes.
        ///
        /// Answers null for a swap that lines nothing up: the field is left exactly as it was and
        /// the run is charged nothing, which is what makes trying a swap free and keeping one
        /// expensive.
        /// </summary>
        public SiegeTurn Swap(int a, int b)
        {
            if (!Lines(a, b)) return null;

            char keep = _cells[a];
            _cells[a] = _cells[b];
            _cells[b] = keep;

            var turn = new SiegeTurn { A = a, B = b };

            int depth = 0;
            while (true)
            {
                var hit = SiegeLayout.Runs(_cells, Width, Height);
                if (hit.Count == 0) break;

                depth++;
                var beat = new SiegeBeat { Depth = depth, Fuel = new float[_wards.Length] };

                foreach (int cell in hit) beat.Cleared.Add(cell);
                beat.Cleared.Sort();

                // **Cogs are claimed before anything comes off the field, and in cell order.**
                // Both halves matter: the colour that takes a cog has to be read off the board as
                // it stands, and the order has to be an ordering rather than whichever way a hash
                // set happened to enumerate — a `HashSet<int>` walk is not promised to be the same
                // on two runtimes, and this decides which turret a player's upgrade went to.
                Claim(beat);

                for (int i = 0; i < beat.Cleared.Count; i++)
                {
                    int cell = beat.Cleared[i];

                    int ward = Layout.WardOf(_cells[cell]);
                    if (ward >= 0) beat.Fuel[ward] += SiegeTuning.FuelPerGem;

                    _cells[cell] = Hole;
                }

                turn.Worth += beat.Cleared.Count;

                for (int w = 0; w < _wards.Length; w++)
                {
                    if (beat.Fuel[w] <= 0f) continue;

                    // **Booked, not paid.** It lands when the motes do - see
                    // `SiegeTuning.FuelLands`. A fallen ward is not filtered here either: it may
                    // still be standing now and down by the time this arrives, and the ward that
                    // is asked is the one that exists when the fuel gets there.
                    _flying.Add(new SiegeCharge
                    {
                        Ward = w,
                        Fuel = beat.Fuel[w],
                        In = SiegeTuning.FuelLands(depth - 1),
                    });
                }

                Collapse(beat);
                turn.Beats.Add(beat);
            }

            Settle();
            return turn;
        }

        /// <summary>
        /// Takes every cog standing beside something this beat cleared, and spends it on the ward
        /// of the colour that took it.
        ///
        /// <para>
        /// <b>The colour of the run decides the ward, which is the whole mechanic.</b> A cog is
        /// not a colour and can never be lined up; what it is worth is a rank, and which turret
        /// gets it is settled by what the player chose to match beside it. So the question a cog
        /// asks is the mode's own question — <em>which colour is wanted</em> — asked about the
        /// line rather than about the hill, and a player who answers it carelessly upgrades the
        /// wrong turret and cannot take it back.
        /// </para>
        /// <para>
        /// <b>First neighbour in cell order wins a cog with two colours beside it</b>, and that is
        /// a rule rather than a tie-break: it is deterministic, it is the same on both runtimes,
        /// and it is *stated* rather than emergent. A player who wants a particular colour to take
        /// a cog can always arrange for that colour to be the only one touching it.
        /// </para>
        /// <para>
        /// <b>The rank lands here rather than with the fuel</b>, unlike everything else this turn
        /// books (invariant 37s). A rank is a property and not a hit: nothing about it is visible
        /// until a bolt leaves, and a bolt costs fuel, which is still crossing the field — so
        /// there is no moment where the player sees an effect arrive before its cause. Keeping it
        /// here is what keeps <see cref="SiegeWard.Rank"/> a single source of truth for the badge,
        /// the damage and the fuel cost at once.
        /// </para>
        /// </summary>
        void Claim(SiegeBeat beat)
        {
            for (int i = 0; i < beat.Cleared.Count; i++)
            {
                int cell = beat.Cleared[i];
                char colour = _cells[cell];

                int x = cell % Width, y = cell / Width;

                Take(x - 1, y, colour, beat);
                Take(x + 1, y, colour, beat);
                Take(x, y - 1, colour, beat);
                Take(x, y + 1, colour, beat);
            }
        }

        /// <summary>Takes the cog at this cell, if there is one, for this colour.</summary>
        void Take(int x, int y, char colour, SiegeBeat beat)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;

            int cell = IndexOf(x, y);
            if (_cells[cell] != SiegeLayout.Cog) return;

            // Emptied here rather than left for the sweep below, so a cog with three cleared gems
            // around it is taken once and by the first of them.
            _cells[cell] = Hole;

            int ward = Layout.WardOf(colour);

            // A cog taken by a colour whose ward has fallen — or is already at the top of the
            // ladder — is spent for nothing, and that is reported rather than hidden. It is the
            // cost of the decision, and a mechanic whose wrong answer costs nothing is a mechanic
            // with no decision in it (invariant 5d).
            if (ward >= 0 && _wards[ward].Upgradable)
            {
                _wards[ward].Rank++;
                beat.Rises.Add(new SiegeRise(cell, ward, _wards[ward].Rank,
                                             SiegeLayout.Letters.IndexOf(colour)));
                return;
            }

            beat.Rises.Add(new SiegeRise(cell, ward, 0, SiegeLayout.Letters.IndexOf(colour)));
        }

        /// <summary>How many cogs are standing on the field. Bounded by <c>MostCogs</c>.</summary>
        public int CogsStanding()
        {
            int n = 0;
            for (int i = 0; i < _cells.Length; i++) if (_cells[i] == SiegeLayout.Cog) n++;
            return n;
        }

        /// <summary>Whether this cell is holding a cog rather than a gem.</summary>
        public bool IsCog(int index)
            => index >= 0 && index < _cells.Length && _cells[index] == SiegeLayout.Cog;

        /// <summary>Whether this level ever deals a cog, authored or refilled.</summary>
        public bool Upgrades => Layout.Cogs > 0 || _seeded;

        /// <summary>
        /// Whether anything walking down this hill carries a shield.
        ///
        /// <b>Read off the muster rather than off the raiders standing now</b>, because a lesson
        /// has to go up when the board opens and the first bulwark may be two waves away — and a
        /// lesson is shown once in a player's life, so raising it on a rung that sends none would
        /// spend it on something never on the screen.
        /// </summary>
        public bool Shielded
        {
            get
            {
                for (int w = 0; w < Layout.Coming.Length; w++)
                    for (int i = 0; i < Layout.Coming[w].Length; i++)
                        if (Layout.KindAt(w, i) == SiegeKind.Bulwark) return true;

                return false;
            }
        }

        /// <summary>Gravity, then a refill, both written into the beat for the view to animate.</summary>
        void Collapse(SiegeBeat beat)
        {
            for (int x = 0; x < Width; x++)
            {
                int write = Height - 1;

                for (int y = Height - 1; y >= 0; y--)
                {
                    char c = _cells[IndexOf(x, y)];
                    if (c == Hole) continue;

                    if (write != y)
                    {
                        _cells[IndexOf(x, write)] = c;
                        _cells[IndexOf(x, y)] = Hole;
                        beat.Drops.Add(new SiegeDrop(x, y, write, SiegeLayout.Letters.IndexOf(c)));
                    }

                    write--;
                }

                // Everything above the write head is new, and it falls in from above the field.
                int fresh = 0;
                for (int y = write; y >= 0; y--)
                {
                    char c = Deal();
                    _cells[IndexOf(x, y)] = c;
                    beat.Drops.Add(new SiegeDrop(x, -1 - fresh, y, SiegeLayout.Letters.IndexOf(c)));
                    fresh++;
                }
            }
        }

        /// <summary>
        /// Deals the field again if nothing on it lines up.
        ///
        /// <b>A field that cannot be played is the one thing this mode may never show</b>, because
        /// its clock does not stop: a locked board with raiders still walking is a run the player
        /// watches themselves lose. It is deterministic, so two devices reshuffle the same way.
        /// </summary>
        void Settle()
        {
            for (int attempt = 0; attempt < 40 && !AnySwap(); attempt++)
            {
                for (int i = _cells.Length - 1; i > 0; i--)
                {
                    int j = (int)(Next() % (uint)(i + 1));
                    char keep = _cells[i];
                    _cells[i] = _cells[j];
                    _cells[j] = keep;
                }

                // A shuffle that lands three alike together would go off with nobody having
                // touched it, so it is dealt again rather than resolved.
                if (SiegeLayout.Runs(_cells, Width, Height).Count > 0) continue;
            }
        }

        /// <summary>
        /// One fresh cell falling into the top of a column: usually a gem, occasionally a cog.
        ///
        /// <para>
        /// <b>The cog roll is only taken when there is room for one</b>, and that keeps the stream
        /// deterministic rather than breaking it: how many cogs are standing is a fact about the
        /// board, and two devices playing the same swaps hold the same board — so both take the
        /// roll or neither does, and the gems that follow line up exactly.
        /// </para>
        /// </summary>
        char Deal()
        {
            if (Layout.Cogs > 0 && CogsStanding() < SiegeTuning.MostCogs
                && Next() % 100u < (uint)Layout.Cogs)
                return SiegeLayout.Cog;

            return Layout.Deal[(int)(Next() % (uint)Layout.Deal.Length)];
        }

        /// <summary>xorshift32. Thirty-two bit throughout so the Python mirror reaches the same field.</summary>
        uint Next()
        {
            uint x = _rng;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _rng = x == 0u ? 2463534242u : x;
            return _rng;
        }

        // ------------------------------------------------------------------ the clock
        /// <summary>
        /// Steps the siege by <paramref name="dt"/> seconds and says what happened.
        ///
        /// <para>
        /// The order matters and is the order a player would want: the wave arrives, then the
        /// raiders walk, then the wards shoot at where the raiders now are, then the warlord casts,
        /// then whatever reached the line swings. A ward that has just been fuelled therefore gets
        /// its bolt away in the same step, and a raider killed by that bolt never lands the blow it
        /// was about to — nor does a warlord killed by it ever start the spell it was about to.
        /// </para>
        /// </summary>
        public SiegeReport Advance(float dt)
        {
            _report.Clear();

            if (dt <= 0f) return _report;
            if (dt > .25f) dt = .25f;      // a resumed app must not teleport a wave into the line

            Land(dt);
            Muster(dt);
            Walk(dt);
            Shoot(dt);
            Conjure(dt);
            Swing(dt);

            for (int i = _raiders.Count - 1; i >= 0; i--)
                if (!_raiders[i].Alive) _raiders.RemoveAt(i);

            return _report;
        }

        /// <summary>
        /// Lands whatever fuel has finished crossing the field.
        ///
        /// <b>Before anything else in the step</b>, so a ward fed on this frame may fire on it -
        /// the delay is the flight, not a further beat of hesitation once it has arrived.
        /// </summary>
        void Land(float dt)
        {
            for (int i = _flying.Count - 1; i >= 0; i--)
            {
                var charge = _flying[i];
                charge.In -= dt;

                if (charge.In > 0f)
                {
                    _flying[i] = charge;
                    continue;
                }

                _flying.RemoveAt(i);

                // A fallen ward takes nothing. The gems still went, which is the whole cost of
                // losing one: the colour is still on the field and is worth nothing now.
                var ward = _wards[charge.Ward];
                if (!ward.Alive) continue;

                ward.Fuel = Math.Min(SiegeTuning.WardCapacity, ward.Fuel + charge.Fuel);
            }

            Arrive(dt);
        }

        /// <summary>
        /// Lands whatever spell has finished crossing the hill.
        ///
        /// <para>
        /// <b>A spell whose caster has been destroyed fizzles</b>, and that is a decision rather
        /// than tidiness. The alternative is a ward coming down — and a run being lost — to
        /// something thrown by a warlord the player had already beaten, which reads as the game
        /// getting the last word. It also makes killing a warlord mid-wind-up worth something,
        /// which the tell is long enough to make possible.
        /// </para>
        /// <para>
        /// A spell aimed at a ward that has since fallen is dropped for <see cref="Land"/>'s own
        /// reason: the ward that is asked is the one standing when it gets there.
        /// </para>
        /// </summary>
        void Arrive(float dt)
        {
            for (int i = _spells.Count - 1; i >= 0; i--)
            {
                var spell = _spells[i];
                spell.In -= dt;

                if (spell.In > 0f) { _spells[i] = spell; continue; }

                _spells.RemoveAt(i);

                var caster = Find(spell.Raider);
                if (caster == null || !caster.Alive) continue;

                // **A roar is aimed at no ward and lands on every one of them.** It is settled
                // here rather than falling through to the single-target path below, because that
                // path indexes `spell.Ward` and a roar carries -1. It *restarts* rather than
                // stacks: two warbringers on one hill would otherwise multiply the charge into
                // something no level was tuned against.
                if (spell.Craft == SiegeSpell.Rally)
                {
                    _roar = SiegeTuning.RallyFor;

                    int shook = SiegeTuning.CastOf(caster.Kind);

                    for (int w = 0; w < _wards.Length; w++)
                    {
                        var shaken = _wards[w];
                        if (!shaken.Alive) continue;

                        shaken.Health -= shook;

                        bool down = shaken.Health <= 0;
                        if (down)
                        {
                            shaken.Health = 0;
                            shaken.Alive = false;
                            shaken.Fuel = 0f;
                        }

                        // One record per ward, so the view draws a hit on each of them from the
                        // same list every other spell arrives on. The roar itself is drawn off the
                        // *cast* (`SiegeView.Roar`), which is where its ring already comes from.
                        _report.Spells.Add(
                            new SiegeSpellLanded(spell.Raider, w, SiegeSpell.Rally, shook, down));
                    }

                    continue;
                }

                var ward = _wards[spell.Ward];
                if (!ward.Alive) continue;

                // Douse takes no health at all, which is why `CastOf` answers nought for it rather
                // than the rules carrying a second damage table nobody would keep in step.
                if (spell.Craft == SiegeSpell.Douse)
                {
                    ward.Snuff();
                    _report.Spells.Add(
                        new SiegeSpellLanded(spell.Raider, spell.Ward, SiegeSpell.Douse, 0, false));
                    continue;
                }

                // A rank is taken *before* the health, so a spell that fells a ward has still
                // taken the rank it came for — and the view is told both in one record rather than
                // having to work out which order they happened in.
                bool sundered = spell.Craft == SiegeSpell.Sunder && ward.Sunder();

                int cast = SiegeTuning.CastOf(caster.Kind);
                ward.Health -= cast;

                bool felled = ward.Health <= 0;
                if (felled)
                {
                    ward.Health = 0;
                    ward.Alive = false;
                    ward.Fuel = 0f;
                }

                _report.Spells.Add(new SiegeSpellLanded(spell.Raider, spell.Ward, spell.Craft,
                                                        cast, felled, sundered));
            }
        }

        void Muster(float dt)
        {
            if (_wave >= Layout.Waves.Length) return;

            // **On a clock, or the moment the hill is empty — whichever comes first.**
            //
            // The clock alone was the fix for waiting-on-a-clear, which let a winning player
            // stroll; a clear alone is what it replaced. Both together are what the mode actually
            // wants: the clock is the pressure and never lets up, and the shortcut means a player
            // who is *ahead* of it is rewarded with the next wave rather than made to stand and
            // watch an empty field. Note the guard — the shortcut cannot fire before the first
            // wave, because the hill is legitimately empty at the start of every run.
            _rest -= dt;

            if (_rest > 0f)
            {
                if (_wave == 0) return;

                for (int i = 0; i < _raiders.Count; i++)
                    if (_raiders[i].Alive) return;
            }

            bool boss = _wave == Layout.BossWave;
            int size = Layout.SizeOf(_wave);

            for (int i = 0; i < size; i++)
            {
                var kind = Layout.KindAt(_wave, i);
                int colour = Layout.ColourAt(_wave, i);

                // Lanes are dealt from the same stream the field is, so a wave arrives spread out
                // rather than in a column - and spread the same way on every device.
                //
                // **The warlord is the exception and walks down the middle**, because where it
                // stands is not a fact anybody should have to hunt for: it is the largest thing on
                // the board and it stays put for the rest of the run, so a dealt lane would put it
                // over a ward on some devices and off the edge of the hill on others.
                int lane = boss ? SiegeTuning.Lanes / 2 : (int)(Next() % SiegeTuning.Lanes);

                _raiders.Add(new SiegeRaider(_minted++, colour, kind, lane,
                                             i * SiegeTuning.RaiderSpacing));
            }

            _report.Wave = _wave;
            _wave++;

            // A boss gets its own quiet in front of it - long for a warlord, short for a
            // warbringer, and `SiegeTuning.RestBefore` says why each. The shortcut above is
            // unaffected, so a player who has cleared the hill still gets the boss at once.
            _rest = _wave == Layout.BossWave
                  ? SiegeTuning.RestBefore(Layout.BossKind)
                  : SiegeTuning.BetweenWaves;
        }

        void Walk(float dt)
        {
            // **The roar runs down on the board's own clock and lifts by itself.** A raider never
            // holds a copy of it, so nothing can be left charging after the warbringer that
            // started it is dead — which is the same rule `Arrive` keeps for a spell whose caster
            // has fallen, and for the same reason: a boss that goes on affecting the hill after it
            // is destroyed reads as the game getting the last word.
            if (_roar > 0f) _roar = Math.Max(0f, _roar - dt);

            float charge = _roar > 0f ? SiegeTuning.Rally : 1f;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive) continue;

                if (raider.Flash > 0f) raider.Flash = Math.Max(0f, raider.Flash - dt);

                if (raider.Wait > 0f)
                {
                    raider.Wait -= dt;
                    continue;
                }

                if (raider.March >= raider.Hold) continue;

                // **A boss does not answer its own roar.** A warbringer that hurried itself into
                // place would shorten the entrance the roar exists to make frightening, and a
                // warlord hastened by somebody else's roar could reach its ground before the level
                // meant it to — so the charge is the hill's, and the hill is what walks.
                raider.March += dt * (raider.Boss ? 1f : charge) / SiegeTuning.MarchOf(raider.Kind);

                if (raider.March < raider.Hold) continue;

                // **Stopped where its kind stops**, which for a warlord is the middle of the hill
                // and for everything else is the line. Nothing here needs to know which: the two
                // differ by one number the raider was minted with.
                raider.March = raider.Hold;

                if (!raider.AtTheLine) continue;

                raider.Blow = SiegeTuning.BlowEvery;
                _report.Arrived.Add(raider.Id);
            }
        }

        void Shoot(float dt)
        {
            for (int w = 0; w < _wards.Length; w++)
            {
                var ward = _wards[w];
                if (!ward.Alive) continue;

                // **A doused ward burns its seconds down here rather than in a step of its own**,
                // because the one thing that must never happen is a ward whose dark has expired
                // waiting a frame to notice: `Fuelled` is false while it is dark, so the frame
                // that clears it is the frame it may fire again.
                if (ward.Dark > 0f) ward.Dark = Math.Max(0f, ward.Dark - dt);

                if (!ward.Fuelled) { ward.Cool = 0f; continue; }

                ward.Cool -= dt;
                if (ward.Cool > 0f) continue;

                var target = Aim(ward.Colour);
                if (target == null) { ward.Cool = 0f; continue; }

                ward.Cool = SiegeTuning.FireEvery;

                // **Both halves of a rank are spent here**, and they are the reason a cog is worth
                // more than the sum of its parts: an upgraded ward hits harder *and* gets more
                // bolts out of the same match, so a rank-four turret turns one match into 2.33
                // times the damage a fresh one would.
                ward.Fuel = Math.Max(0f, ward.Fuel - SiegeTuning.FuelShot(ward.Rank));

                bool weak = target.Colour == ward.Colour;
                int damage = SiegeTuning.DamageTo(target.Kind, ward.Rank, weak);

                target.Health -= damage;
                target.Flash = .18f;

                bool killed = target.Health <= 0;
                if (killed)
                {
                    target.Alive = false;
                    _felled++;
                }

                _report.Bolts.Add(new SiegeBolt(w, target.Id, damage, weak, killed));
            }
        }

        /// <summary>
        /// What a ward shoots at: the raider of its own colour that is furthest down the hill,
        /// and otherwise whichever raider is furthest down.
        ///
        /// <b>Its own colour first, so the rule is visible in play.</b> A player who has fed the
        /// right ward sees the bolts go to the thing that colour hurts; one who has not sees them
        /// spread. Nothing has to be told about it.
        /// </summary>
        SiegeRaider Aim(int colour)
        {
            SiegeRaider weak = null, near = null;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive || !raider.OnTheHill) continue;

                if (near == null || raider.March > near.March) near = raider;

                if (raider.Colour != colour) continue;
                if (weak == null || raider.March > weak.March) weak = raider;
            }

            return weak ?? near;
        }

        /// <summary>
        /// The warlord's spells: chosen, telegraphed, and thrown at the line from where it stands.
        ///
        /// <para>
        /// <b>Nothing lands here.</b> A cast decides a target and books a
        /// <see cref="Flight"/>; <see cref="Arrive"/> is what takes the ward's health, a whole
        /// <see cref="SiegeTuning.BossTell"/> plus <see cref="SiegeTuning.BossFlight"/> later. That
        /// split is invariant 37s — a move's effect may not land before its animation does, and on
        /// a board whose clock never stops the only way to guarantee it is for the schedule to be
        /// a rule the view reads rather than a duration the view invents.
        /// </para>
        /// </summary>
        void Conjure(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var boss = _raiders[i];
                if (!boss.Boss || !boss.Alive || !boss.InPlace) continue;

                boss.Spell -= dt;
                if (boss.Spell > 0f) continue;

                boss.Spell = SiegeTuning.CastEveryFor(boss.Kind);

                var craft = boss.Spellcraft;

                // **A roar is thrown at the hill, so it carries no ward.** Three of the four aim
                // at the line and one does not, and the difference is asked once here rather than
                // by every reader of a ward index nobody set.
                int ward = SiegeTuning.AimsAtAWard(boss.Kind) ? Wanted(craft) : -1;
                if (ward < 0 && craft != SiegeSpell.Rally) continue;

                float lands = SiegeTuning.BossTell + SiegeTuning.BossFlight;

                _spells.Add(new Flight { Raider = boss.Id, Ward = ward, Craft = craft, In = lands });
                _report.Casts.Add(new SiegeCast(boss.Id, ward, craft, lands));
            }
        }

        /// <summary>
        /// Which ward a boss throws at, and each of the three that aim asks a different question.
        ///
        /// <para>
        /// <b>The freshest rather than the weakest, for a smite, and that is what keeps the fight
        /// winnable.</b> A warlord that finished off whatever was nearly down would take the line
        /// apart one ward at a time — and the ward it would reach first is the one whose colour the
        /// player has to feed to answer it, so the mode's own answer would be the thing it
        /// destroyed. Picking the freshest spreads the damage instead: the line comes down evenly,
        /// no colour is ever locked out, and a run that is losing is losing to arithmetic rather
        /// than to a trap. It is also what keeps <see cref="Stranded"/> an honest certainty
        /// (invariant 28f).
        /// </para>
        /// <para>
        /// <b>A douse wants the ward the player is filling</b>, because that is what makes it a
        /// decision rather than a tax: the fuel it takes is fuel somebody just earned, and the
        /// answer — feed a different colour, or spend a surge — is one they choose every few
        /// seconds. An already-dark ward is never chosen twice; there is nothing left to take and
        /// a second one would read as the boss doing nothing.
        /// </para>
        /// <para>
        /// <b>A sunder wants the best turret on the line</b>, which is the one thing in this
        /// chapter a player <em>earned</em> (invariant 37w). That makes where the cogs went a
        /// question the finale asks and a player can get wrong in both directions — pile them into
        /// one ward and the overlord can take the pile; spread them and nothing on the line is
        /// strong. Ties go to the freshest, so once the ranks are level it spreads exactly as a
        /// smite does and cannot dismantle the line one ward at a time.
        /// </para>
        /// </summary>
        int Wanted(SiegeSpell craft)
        {
            int best = -1;
            long most = -1;

            for (int w = 0; w < _wards.Length; w++)
            {
                var ward = _wards[w];
                if (!ward.Alive) continue;

                // Ranked so a single comparison decides, with health as the low half of the key —
                // written this way rather than as three loops because three loops is three places
                // that can come to disagree about what "standing" means.
                long rank;
                switch (craft)
                {
                    case SiegeSpell.Douse:
                        if (ward.Doused) continue;
                        rank = (long)(ward.Fuel * 1000f) * 64L + ward.Health;
                        break;

                    case SiegeSpell.Sunder:
                        rank = (long)ward.Rank * 64L + ward.Health;
                        break;

                    default:
                        rank = ward.Health;
                        break;
                }

                if (rank <= most) continue;

                most = rank;
                best = w;
            }

            return best;
        }

        void Swing(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.AtTheLine) continue;

                raider.Blow -= dt;
                if (raider.Blow > 0f) continue;

                raider.Blow = SiegeTuning.BlowEvery;

                int w = Nearest(raider.Lane);
                if (w < 0) continue;

                var ward = _wards[w];
                int damage = SiegeTuning.BlowOf(raider.Kind);
                ward.Health -= damage;

                bool felled = ward.Health <= 0;
                if (felled)
                {
                    ward.Health = 0;
                    ward.Alive = false;
                    ward.Fuel = 0f;
                }

                _report.Blows.Add(new SiegeBlow(w, raider.Id, damage, felled));
            }
        }

        /// <summary>
        /// The standing ward nearest a lane, or -1 when the line is gone.
        ///
        /// A raider whose own ward has fallen walks along the line to the next one rather than
        /// standing in front of a hole, which is what stops a run being decided by which lane a
        /// raider happened to be dealt.
        /// </summary>
        int Nearest(int lane)
        {
            // Lanes and wards are both spread evenly across the same width, so a lane's place on
            // the line is a fraction rather than an index.
            float want = SiegeTuning.Lanes <= 1 ? 0f : lane / (float)(SiegeTuning.Lanes - 1);

            int best = -1;
            float closest = float.MaxValue;

            for (int w = 0; w < _wards.Length; w++)
            {
                if (!_wards[w].Alive) continue;

                float at = _wards.Length <= 1 ? 0f : w / (float)(_wards.Length - 1);
                float gap = Math.Abs(at - want);

                if (gap >= closest) continue;

                closest = gap;
                best = w;
            }

            return best;
        }

        // ------------------------------------------------------------------ utilities
        /// <summary>
        /// Burns everything standing in one box of the hill.
        ///
        /// <para>
        /// <b>A box rather than a radius, and that is a change of kind rather than of degree.</b>
        /// A blast used to take everything within a distance of the point a finger left; exact in
        /// the rule, and on the screen it asked the player to judge a radius against raiders that
        /// were moving. The hill is a grid now (<see cref="SiegeTuning.BlastRows"/> bands by
        /// <see cref="SiegeTuning.Lanes"/> lanes), the boxes are drawn, and a firepot takes
        /// exactly what is standing in the one that was tapped - which is invariant 33g's rule at
        /// its strongest, because the drawn thing and the played thing are now the same integers.
        /// </para>
        /// <para>
        /// <b>It reports damage <em>absorbed</em>, not damage offered</b>, and that number is
        /// what the run is charged for (invariant 39). Overkill on a raider with three health
        /// left is not work the player was spared, so charging for it would price a firepot
        /// above what it saved - safe, but wrong in a way the player would feel.
        /// </para>
        /// <para>
        /// A raider still walking on (<c>Wait &gt; 0</c>) is untouched: it is not on the hill
        /// yet, so it is not drawn there, and burning something the player cannot see is the
        /// class of fault invariant 32c refuses.
        /// </para>
        /// </summary>
        public int Blast(int lane, int row, int damage, List<SiegeStrike> into)
        {
            if (damage <= 0) return 0;
            if (lane < 0 || lane >= SiegeTuning.Lanes) return 0;
            if (row < 0 || row >= SiegeTuning.BlastRows) return 0;

            int absorbed = 0;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive || !raider.OnTheHill) continue;

                if (raider.Lane != lane) continue;

                // **What its body covers, not the box its feet are in.** A boss is three cells of
                // silhouette on a four-row hill, so aiming at the thing you can see and missing is
                // the whole of the "bombs don't hit bosses" report.
                if (!SiegeTuning.Caught(raider.Kind, raider.March, row)) continue;

                int took = damage < raider.Health ? damage : raider.Health;
                absorbed += took;

                raider.Health -= took;
                raider.Flash = .18f;

                bool killed = raider.Health <= 0;
                if (killed)
                {
                    raider.Alive = false;
                    _felled++;
                }

                into?.Add(new SiegeStrike(raider.Id, took, killed));
            }

            // Felled raiders are cleared at the top of the next Advance, exactly as a bolt's
            // are, so the view sees them one last time and can play the death it was handed.
            return absorbed;
        }

        /// <summary>
        /// Strikes <b>everything on the hill</b> for the same damage, and answers how much was
        /// absorbed.
        ///
        /// <para>
        /// <b>The strikes are shuffled, and they are shuffled off the board's own stream.</b> The
        /// view draws them one at a time down the list, so the order is what a player sees — bolts
        /// falling here and there across the hill rather than a tidy sweep left to right, which
        /// reads as a list being processed. It comes from <see cref="Next"/> rather than from
        /// <c>Random</c> because everything else this mode deals does (invariant 37e): two devices
        /// on the same board see the same storm, so a report about one is a report somebody else
        /// can meet.
        /// </para>
        /// <para>
        /// <b>A shield is not read here</b>, and that is the rule rather than an omission. A
        /// bulwark halves what a <em>ward's bolt</em> does to it because the shield is answered by
        /// <em>colour</em>; a storm has no colour, so there is nothing for the soak to be measured
        /// against. See <see cref="SiegeKind.Bulwark"/>.
        /// </para>
        /// </summary>
        public int Storm(int damage, List<SiegeStrike> into)
        {
            if (damage <= 0) return 0;

            int first = into != null ? into.Count : 0;
            int absorbed = 0;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive || !raider.OnTheHill) continue;

                int took = damage < raider.Health ? damage : raider.Health;
                absorbed += took;

                raider.Health -= took;
                raider.Flash = .18f;

                bool killed = raider.Health <= 0;
                if (killed)
                {
                    raider.Alive = false;
                    _felled++;
                }

                into?.Add(new SiegeStrike(raider.Id, took, killed));
            }

            // Fisher-Yates over what this call added, so a storm on a board that already had
            // strikes pending cannot reorder somebody else's.
            if (into != null)
            {
                for (int i = into.Count - 1; i > first; i--)
                {
                    int j = first + (int)(Next() % (uint)(i - first + 1));
                    var held = into[i];
                    into[i] = into[j];
                    into[j] = held;
                }
            }

            return absorbed;
        }

        /// <summary>
        /// Mends a ward, and answers how much health it actually took.
        ///
        /// <para>
        /// <b>It mends a standing ward and can never raise a fallen one.</b> That is not a
        /// kindness withheld, and the reason moved once without the rule moving with it. It used
        /// to be that nothing at all could put a ward back up, so <see cref="Stranded"/> could
        /// honestly say a fallen line was beyond rescue. <see cref="Rally"/> now can — but only
        /// as a <em>continue</em>, which is a purchase made after the run is over, offered before
        /// any of the accounting happens and priced against the grade (invariant 23). A mending
        /// that raised a ward would be that same purchase sold for eight gems in the middle of a
        /// run, outside the ordering the continue exists to keep and outside the toll that stops
        /// it buying a star. What a utility may sell is survival, never resurrection.
        /// </para>
        /// </summary>
        public int Mend(int ward, int health)
        {
            if (ward < 0 || ward >= _wards.Length || health <= 0) return 0;

            var post = _wards[ward];
            if (!post.Alive) return 0;

            int room = SiegeTuning.WardHealth - post.Health;
            if (room <= 0) return 0;

            int given = health < room ? health : room;
            post.Health += given;

            return given;
        }

        /// <summary>
        /// Pours fuel into a ward, in tenths, and answers how many tenths it took.
        ///
        /// <para>
        /// <b>Tenths rather than the ward's own float</b>, because what comes back decides a
        /// graded number: <c>SiegeUtility</c> converts it to matches, and a graded number
        /// decided by a float is one three code generators round three ways. The ward's live
        /// fuel stays a float because it is drained by a clock, which is the one quantity here
        /// that genuinely is continuous.
        /// </para>
        /// <para>
        /// Room is <em>floored</em> to whole tenths, so this can never report taking more than
        /// it gave. What refuses a ward too full to be worth it is <c>SiegeUtility</c>, before
        /// an item is spent.
        /// </para>
        /// </summary>
        public int Surge(int ward, int tenths)
        {
            if (ward < 0 || ward >= _wards.Length || tenths <= 0) return 0;

            var post = _wards[ward];
            if (!post.Alive) return 0;

            int room = RoomForFuel(ward);
            if (room <= 0) return 0;

            // **A surge lifts a douse, and that is what makes it the blightcaller's answer.**
            // Fuel poured into a ward that cannot fire is fuel spent on nothing until the dark
            // runs out on its own, which is a utility charged for a delay — so pouring re-lights
            // it. The player is buying the seconds rather than the fuel, which is exactly what
            // invariant 39 says a utility may sell: a finish, never a grade.
            post.Dark = 0f;

            int given = tenths < room ? tenths : room;
            post.Fuel += given / 10f;

            if (post.Fuel > SiegeTuning.WardCapacity) post.Fuel = SiegeTuning.WardCapacity;

            return given;
        }

        /// <summary>
        /// Puts the ward line back up, because a continue was paid for. Answers how many turrets
        /// really stood again.
        ///
        /// <para>
        /// <b>The one thing in this mode that raises a fallen ward, and it is a continue rather
        /// than a utility</b> — see <see cref="Mend"/> for the difference and why it matters.
        /// The hill is not touched: every raider stands where it stood, mid-march or mid-swing,
        /// with the wave clock where it was, which is what "carry on from where you left off"
        /// has to mean when the thing that ended the run was the line and not the board.
        /// </para>
        /// <para>
        /// <b>Full health, and the rank the cogs bought.</b> Health, because a line raised at
        /// anything less is a line that falls again in a breath and a continue that does not
        /// continue is a charge (invariant 23). Rank, because it is the one thing in this mode a
        /// player earns (invariant 37w) and nothing has taken it away — a continue that
        /// confiscated it would be selling back less than was lost.
        /// </para>
        /// <para>
        /// <b>No fuel, deliberately.</b> Fuel is damage, and damage is progress the run was
        /// graded against; a continue may buy a finish and never a grade, so the wards come back
        /// standing and empty and the player matches to light them. The dark a blightcaller left
        /// is cleared for the opposite reason — it is seconds the player would be paying for and
        /// not receiving.
        /// </para>
        /// <para>
        /// Raised left to right so that a published figure smaller than the line is at least
        /// deterministic. It ships as the whole line, so in practice the order decides nothing.
        /// </para>
        /// </summary>
        public int Rally(int wards)
        {
            if (wards <= 0) return 0;

            int raised = 0;

            for (int i = 0; i < _wards.Length && raised < wards; i++)
            {
                var post = _wards[i];
                if (post.Alive) continue;

                post.Alive = true;
                post.Health = SiegeTuning.WardHealth;
                post.Fuel = 0f;
                post.Dark = 0f;
                post.Cool = 0f;

                raised++;
            }

            return raised;
        }

        /// <summary>Whether this ward is standing but smothered. Asked by a surge's offer.</summary>
        public bool Doused(int ward)
            => ward >= 0 && ward < _wards.Length && _wards[ward].Doused;

        /// <summary>
        /// Seconds left on a warbringer's roar, or nought. The view draws the hill charging.
        /// </summary>
        public float Roaring => _roar;

        /// <summary>
        /// How much more fuel a ward could take, in whole tenths. Nought for a fallen one.
        ///
        /// Floored, so an offer is never made on room that turns out not to be there.
        /// </summary>
        public int RoomForFuel(int ward)
        {
            if (ward < 0 || ward >= _wards.Length) return 0;

            var post = _wards[ward];
            if (!post.Alive) return 0;

            float room = SiegeTuning.WardCapacity - post.Fuel;
            return room <= 0f ? 0 : (int)(room * 10f);
        }

        /// <summary>How much more health a ward could take. Nought for a fallen one.</summary>
        public int RoomForHealth(int ward)
        {
            if (ward < 0 || ward >= _wards.Length) return 0;

            var post = _wards[ward];
            if (!post.Alive) return 0;

            int room = SiegeTuning.WardHealth - post.Health;
            return room < 0 ? 0 : room;
        }

        /// <summary>The raider with this id, or null once it has been taken off the hill.</summary>
        public SiegeRaider Find(int id)
        {
            for (int i = 0; i < _raiders.Count; i++)
                if (_raiders[i].Id == id) return _raiders[i];

            return null;
        }
    }
}
