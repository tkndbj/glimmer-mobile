using System;
using System.Collections.Generic;
using UnityEngine;

namespace GlimmerGrove.Challenges
{
    /// <summary>The four gem colours, in the order every board in this game uses: r g b y.</summary>
    public static class ChallengeColours
    {
        public const string Letters = "rgby";
        public const int Count = 4;

        public static int IndexOf(char letter) => Letters.IndexOf(letter);

        public static char LetterOf(int colour)
            => colour >= 0 && colour < Count ? Letters[colour] : '.';
    }

    /// <summary>The fixed line: see <see cref="ChallengeLineDto"/>.</summary>
    public sealed class ChallengeLine
    {
        public readonly int Damage, Health, Strike;

        public ChallengeLine(int damage, int health, int strike)
        {
            Damage = damage;
            Health = health;
            Strike = strike;
        }
    }

    /// <summary>One raider of a wave: which turret it walks at and how many bolts it takes.</summary>
    public readonly struct ChallengeRaiderSpec
    {
        public readonly int Colour, Health;

        public ChallengeRaiderSpec(int colour, int health)
        {
            Colour = colour;
            Health = health;
        }
    }

    public sealed class ChallengeWave
    {
        /// <summary>After which move the wave musters. Nought is on the hill before the first.</summary>
        public readonly int Turn;

        public readonly ChallengeRaiderSpec[] Raiders;

        public ChallengeWave(int turn, ChallengeRaiderSpec[] raiders)
        {
            Turn = turn;
            Raiders = raiders;
        }
    }

    /// <summary>
    /// One challenge as the game plays it: the DTO with every string parsed and every rule
    /// asked once, at read.
    /// </summary>
    public sealed class ChallengeDefinition
    {
        public readonly string Id;
        public readonly ChallengeGenre Genre;
        public readonly uint Seed;
        public readonly int Width, Height;
        public readonly string[] Rows, Gems;
        public readonly int Target, Hill, Bolts;
        public readonly ChallengeWave[] Waves;

        public ChallengeDefinition(string id, ChallengeGenre genre, uint seed, int width, int height,
                                   string[] rows, string[] gems,
                                   int target, int hill, int bolts, ChallengeWave[] waves)
        {
            Id = id;
            Genre = genre;
            Seed = seed;
            Width = width;
            Height = height;
            Rows = rows ?? Array.Empty<string>();
            Gems = gems ?? Array.Empty<string>();
            Target = target;
            Hill = hill;
            Bolts = bolts;
            Waves = waves ?? Array.Empty<ChallengeWave>();
        }

        /// <summary>A challenge's strings derive from its id and cannot be overridden (invariant 5a).</summary>
        public string NameKey => "challenge." + Id + ".name";
        public string BlurbKey => "challenge." + Id + ".blurb";

        /// <summary>The whole hill's health, for a gate to print against the bolts a board can pay.</summary>
        public int HillHealth
        {
            get
            {
                int n = 0;
                for (int w = 0; w < Waves.Length; w++)
                    for (int r = 0; r < Waves[w].Raiders.Length; r++)
                        n += Waves[w].Raiders[r].Health;
                return n;
            }
        }

        public int RaiderCount
        {
            get
            {
                int n = 0;
                for (int w = 0; w < Waves.Length; w++) n += Waves[w].Raiders.Length;
                return n;
            }
        }
    }

    /// <summary>
    /// Every challenge this build can deal, read from <c>challenges.json</c>, with the
    /// allowance, the deals and the reward rates beside them.
    ///
    /// <para>
    /// <b>The reader is the gate.</b> Everything a row can get wrong is refused here, by name,
    /// with the id in the message — an unknown genre, a wave naming a colour the line has no
    /// turret for, a board whose rows disagree about their width, a genre missing the field it
    /// plays on. The Editor validator, <c>content.py</c> and the offline test all run this same
    /// <see cref="TryBuild"/>, so there is exactly one opinion about whether a row is fit to
    /// ship, and a row that reaches a device has passed it.
    /// </para>
    /// <para>
    /// <b>A genre is a ladder of rows</b> (<see cref="RowsOf"/>), kept in authored order. The
    /// calendar deals one of them a slot (<see cref="ChallengeCalendar"/>), so adding a level to
    /// a genre is a row appended to this file and nothing else — a build, a gate and the server
    /// all learn of it through the same reader.
    /// </para>
    /// <para>
    /// <b>Versions independently of everything</b> (9b's rule): this file says its own
    /// <see cref="Version"/>, and a build refuses a file from the future rather than reading
    /// it half. <b>v2</b> added the allowance, the deals and the rewards, and is refused by a v1
    /// reader on purpose: a build that could not enforce the allowance would offer unlimited
    /// plays against a file that promises two.
    /// </para>
    /// </summary>
    public sealed class ChallengeTable
    {
        public const int Version = 2;

        /// <summary>
        /// The built-in default is an empty slate on the built-in figures: no challenge is ever
        /// hard-coded (invariant 4), but the reward rates are, because the XP a lifetime tally
        /// is worth has to be the same on a device with no file as on the server with no block
        /// (<see cref="ChallengeLimits"/>).
        /// </summary>
        public static readonly ChallengeTable Default =
            new ChallengeTable(new ChallengeLine(1, 3, 1), ChallengeLimits.DefaultFreePlays,
                               Array.Empty<ChallengeTier>(), ChallengeRewardRule.Default,
                               Array.Empty<ChallengeDefinition>());

        public readonly ChallengeLine Line;

        /// <summary>Plays of each genre a day that cost nothing.</summary>
        public readonly int FreePlays;

        /// <summary>What a cleared level pays.</summary>
        public readonly ChallengeRewardRule Rewards;

        readonly ChallengeTier[] _tiers;
        readonly ChallengeDefinition[] _rows;
        readonly Dictionary<ChallengeGenre, ChallengeDefinition[]> _byGenre;
        readonly ChallengeGenre[] _genres;

        ChallengeTable(ChallengeLine line, int freePlays, ChallengeTier[] tiers,
                       ChallengeRewardRule rewards, ChallengeDefinition[] rows)
        {
            Line = line;
            FreePlays = freePlays;
            _tiers = tiers;
            Rewards = rewards;
            _rows = rows;

            _byGenre = new Dictionary<ChallengeGenre, ChallengeDefinition[]>();
            var genres = new List<ChallengeGenre>();
            foreach (ChallengeGenre genre in Enum.GetValues(typeof(ChallengeGenre)))
            {
                var mine = new List<ChallengeDefinition>();
                for (int i = 0; i < rows.Length; i++)
                    if (rows[i].Genre == genre) mine.Add(rows[i]);

                if (mine.Count == 0) continue;
                _byGenre[genre] = mine.ToArray();
                genres.Add(genre);
            }
            _genres = genres.ToArray();
        }

        public IReadOnlyList<ChallengeDefinition> All => _rows;
        public int Count => _rows.Length;
        public bool IsEmpty => _rows.Length == 0;

        /// <summary>The deals, cheapest first, as authored.</summary>
        public IReadOnlyList<ChallengeTier> Tiers => _tiers;

        /// <summary>Every genre with at least one row, in enum order. What the list draws a card for.</summary>
        public IReadOnlyList<ChallengeGenre> Genres => _genres;

        /// <summary>A genre's rows in authored order, or none.</summary>
        public IReadOnlyList<ChallengeDefinition> RowsOf(ChallengeGenre genre)
            => _byGenre.TryGetValue(genre, out var rows) ? rows : Array.Empty<ChallengeDefinition>();

        public ChallengeDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _rows.Length; i++)
                if (_rows[i].Id == id) return _rows[i];
            return null;
        }

        public ChallengeTier FindTier(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _tiers.Length; i++)
                if (_tiers[i].Id == id) return _tiers[i];
            return null;
        }

        /// <summary>The largest deal, or null when the file sells none. What a gate prints the ceiling from.</summary>
        public ChallengeTier LargestTier => _tiers.Length == 0 ? null : _tiers[_tiers.Length - 1];

        /// <summary>
        /// Deal ids that were sold and withdrawn, refused by name at read (invariant 5f). A tier
        /// id is a spend id and a wallet field, so a withdrawn one keeps meaning the deal it was
        /// for ever; none has been withdrawn yet, and the list exists so the first one has
        /// somewhere to go. Mirrored by <c>content.py</c> and the seeder.
        /// </summary>
        public static readonly string[] RetiredTierIds = { };

        public static bool IsRetiredTierId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < RetiredTierIds.Length; i++)
                if (RetiredTierIds[i] == id) return true;
            return false;
        }

        /// <summary>
        /// The most credits the largest deal could pay in a day on this file: its plays, times
        /// the genres shipped, times the rate. Printed by the gates and held under
        /// <see cref="ChallengeLimits.MaxDailyCoins"/>.
        /// </summary>
        public long LargestDailyCoins
        {
            get
            {
                int plays = LargestTier != null ? LargestTier.Plays : FreePlays;
                return (long)plays * _genres.Length * Rewards.Coins;
            }
        }

        // ------------------------------------------------------------------ reading
        public static bool TryRead(string json, out ChallengeTable table, List<string> problems)
        {
            table = Default;
            if (problems == null) problems = new List<string>();

            ChallengeTableDto dto;
            try
            {
                dto = JsonUtility.FromJson<ChallengeTableDto>(json);
            }
            catch (Exception e)
            {
                problems.Add("challenges file is not valid JSON: " + e.Message);
                return false;
            }

            return TryBuild(dto, out table, problems);
        }

        /// <summary>
        /// Builds the table from its DTO, refusing anything a genre could not play.
        ///
        /// <b>Returns false when the file is unusable</b> (wrong version, no line, a duplicated
        /// id, a deal ladder that does not climb) and true with problems listed when individual
        /// rows were dropped — a bad row costs that challenge, never the slate.
        /// </summary>
        public static bool TryBuild(ChallengeTableDto dto, out ChallengeTable table, List<string> problems)
        {
            table = Default;
            if (problems == null) problems = new List<string>();

            if (dto == null)
            {
                problems.Add("challenges file is empty");
                return false;
            }

            if (dto.schemaVersion != Version)
            {
                problems.Add($"challenges file is schema v{dto.schemaVersion}, this build reads v{Version}");
                return false;
            }

            var lineDto = dto.line;
            if (lineDto == null || lineDto.damage <= 0 || lineDto.health <= 0 || lineDto.strike <= 0)
            {
                problems.Add("challenges line needs damage, health and strike all above nought");
                return false;
            }

            var line = new ChallengeLine(lineDto.damage, lineDto.health, lineDto.strike);

            bool ok = true;

            int freePlays = ReadAllowance(dto.allowance, problems);
            var tiers = ReadTiers(dto.tiers, freePlays, problems, ref ok);
            var rewards = ChallengeRewardRule.Resolve(dto.rewards, problems);

            var rows = new List<ChallengeDefinition>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            var list = dto.challenges ?? Array.Empty<ChallengeDto>();
            for (int i = 0; i < list.Length; i++)
            {
                var row = list[i];
                string where = row == null || string.IsNullOrEmpty(row.id) ? $"challenge #{i}" : $"challenge '{row.id}'";

                if (row == null || string.IsNullOrEmpty(row.id))
                {
                    problems.Add($"{where} has no id");
                    continue;
                }

                if (!seen.Add(row.id))
                {
                    problems.Add($"{where} is listed twice");
                    ok = false;
                    continue;
                }

                var built = Build(row, line, where, problems);
                if (built != null) rows.Add(built);
            }

            table = new ChallengeTable(line, freePlays, tiers, rewards, rows.ToArray());

            // The economy gate (56k): the largest deal's daily maximum, across every genre the
            // file ships, is held under a ceiling — a fifth genre or a raised rate is otherwise a
            // silent multiplication of what the mode pays. Named as a problem so the build gate
            // fails; the slate is still usable, because the bound is about money already
            // adjudicated per claim and not about a board.
            if (table.LargestDailyCoins > ChallengeLimits.MaxDailyCoins)
            {
                problems.Add($"the largest deal could pay {table.LargestDailyCoins:N0} credits a day across " +
                             $"{table.Genres.Count} genre(s), above the {ChallengeLimits.MaxDailyCoins:N0} " +
                             "ceiling (ChallengeLimits.MaxDailyCoins); lower the rate, the plays or the ceiling");
                ok = false;
            }

            return ok;
        }

        /// <summary>The free allowance: unwritten inherits, out of range is clamped and named.</summary>
        static int ReadAllowance(ChallengeAllowanceDto dto, List<string> problems)
        {
            if (dto == null || !dto.IsAuthored) return ChallengeLimits.DefaultFreePlays;

            if (dto.freePlays > ChallengeLimits.MaxFreePlays)
            {
                problems.Add($"challenges allowance.freePlays is {dto.freePlays}, above the supported " +
                             $"maximum {ChallengeLimits.MaxFreePlays}; clamped");
                return ChallengeLimits.MaxFreePlays;
            }

            return dto.freePlays;
        }

        /// <summary>
        /// The deals, refused as a set rather than row by row.
        ///
        /// <b>A ladder that does not climb is unusable</b>, for the season's reason about a paid
        /// column with a hole in it (47b): a deal is "more plays for more gems", and two rows
        /// where the dearer one gives fewer plays — or the same — is a page that sells a worse
        /// thing for more money. Every row must beat the free figure, ids must be unique and
        /// key-shaped, and the price, the plays and the days are each bounded.
        /// </summary>
        static ChallengeTier[] ReadTiers(ChallengeTierDto[] raw, int freePlays, List<string> problems, ref bool ok)
        {
            if (raw == null || raw.Length == 0) return Array.Empty<ChallengeTier>();

            if (raw.Length > ChallengeLimits.MaxTiers)
            {
                problems.Add($"challenges lists {raw.Length} deals; at most {ChallengeLimits.MaxTiers} are supported");
                ok = false;
                return Array.Empty<ChallengeTier>();
            }

            var tiers = new List<ChallengeTier>(raw.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int lastPlays = freePlays, lastGems = 0;

            for (int i = 0; i < raw.Length; i++)
            {
                var row = raw[i];
                string where = row == null || string.IsNullOrEmpty(row.id) ? $"deal #{i}" : $"deal '{row.id}'";
                int before = problems.Count;

                if (row == null || !IsValidTierId(row.id))
                    problems.Add($"{where} needs an id of 1-32 lower-case letters, digits or underscores");
                else if (IsRetiredTierId(row.id))
                    problems.Add($"{where} names a retired deal id, which may never be re-minted (invariant 5f)");
                else if (!seen.Add(row.id))
                    problems.Add($"{where} is listed twice");

                if (row != null)
                {
                    if (row.gems <= 0 || row.gems > ChallengeLimits.MaxTierGems)
                        problems.Add($"{where} needs a price between 1 and {ChallengeLimits.MaxTierGems} gems");
                    if (row.plays <= 0 || row.plays > ChallengeLimits.MaxTierPlays)
                        problems.Add($"{where} needs plays between 1 and {ChallengeLimits.MaxTierPlays}");
                    if (row.days <= 0 || row.days > ChallengeLimits.MaxTierDays)
                        problems.Add($"{where} needs days between 1 and {ChallengeLimits.MaxTierDays}");

                    if (row.plays > 0 && row.plays <= lastPlays)
                        problems.Add($"{where} gives {row.plays} plays a day, which does not beat the " +
                                     $"{lastPlays} before it; deals must climb");
                    if (row.gems > 0 && row.gems <= lastGems)
                        problems.Add($"{where} costs {row.gems} gems, which does not exceed the " +
                                     $"{lastGems} before it; deals must climb");
                }

                if (problems.Count > before)
                {
                    ok = false;
                    continue;
                }

                lastPlays = row.plays;
                lastGems = row.gems;
                tiers.Add(new ChallengeTier(row.id, row.gems, row.plays, row.days));
            }

            return ok ? tiers.ToArray() : Array.Empty<ChallengeTier>();
        }

        /// <summary>
        /// A deal id is a spend id, a wallet field and a loc key, so it is held to the shape all
        /// three accept. Mirrors <c>TIER_ID</c> in <c>functions/src/challenges.ts</c>.
        /// </summary>
        public static bool IsValidTierId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 32) return false;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }

        static ChallengeDefinition Build(ChallengeDto row, ChallengeLine line, string where, List<string> problems)
        {
            int before = problems.Count;

            if (!ChallengeGenres.TryParse(row.genre, out var genre))
            {
                problems.Add(ChallengeGenres.IsRetired(row.genre)
                             ? $"{where} names genre '{row.genre}', which was withdrawn and may never come back under that spelling (invariant 5f)"
                             : $"{where} names genre '{row.genre}', which this build does not play");
                return null;
            }

            if (row.width <= 0 || row.height <= 0)
                problems.Add($"{where} needs a width and a height");

            if (row.hill <= 0)
                problems.Add($"{where} needs a hill of at least one step");

            // The pipes genre's two edge strings, retired with it (2026-09-23). Refused by name
            // rather than ignored, because JsonUtility drops an unknown field without a word (5f).
            if (!string.IsNullOrEmpty(row.sources) || !string.IsNullOrEmpty(row.sinks))
                problems.Add($"{where} writes 'sources' or 'sinks', which belonged to the withdrawn pipes genre and are refused (invariant 5f)");

            var waves = ParseWaves(row.waves, where, problems);

            var def = new ChallengeDefinition(row.id, genre, (uint)row.seed, row.width, row.height,
                                              row.rows, row.gems,
                                              row.target, row.hill, row.bolts <= 0 ? 1 : row.bolts,
                                              waves);

            if (problems.Count > before) return null;

            // The genre's own reading of its board, asked once at read rather than on a device.
            string fault = ChallengePuzzles.Fault(def);
            if (fault != null)
            {
                problems.Add($"{where}: {fault}");
                return null;
            }

            return def;
        }

        /// <summary>
        /// <c>"3 r2 g2"</c> → after the third move, a red and a green raider of two health.
        ///
        /// <b>Strict on purpose</b>: a colour letter outside the line's four, a health of nought
        /// or a turn out of order is refused with the wave quoted, because a wave that parses to
        /// nothing is a challenge whose hill is empty and every gate reads green.
        /// </summary>
        static ChallengeWave[] ParseWaves(string[] raw, string where, List<string> problems)
        {
            var waves = new List<ChallengeWave>();
            if (raw == null || raw.Length == 0)
            {
                problems.Add($"{where} sends no waves");
                return waves.ToArray();
            }

            int lastTurn = -1;

            for (int i = 0; i < raw.Length; i++)
            {
                string text = raw[i] ?? string.Empty;
                var parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 2 || !int.TryParse(parts[0], out int turn) || turn < 0)
                {
                    problems.Add($"{where} wave \"{text}\" must read \"<turn> <colour><health> ...\"");
                    continue;
                }

                if (turn <= lastTurn)
                {
                    problems.Add($"{where} wave \"{text}\" is out of order: turns must strictly climb");
                    continue;
                }
                lastTurn = turn;

                var raiders = new List<ChallengeRaiderSpec>();
                for (int p = 1; p < parts.Length; p++)
                {
                    string tok = parts[p];
                    int colour = tok.Length > 0 ? ChallengeColours.IndexOf(tok[0]) : -1;

                    if (colour < 0 || tok.Length < 2 || !int.TryParse(tok.Substring(1), out int health) || health <= 0)
                    {
                        problems.Add($"{where} wave \"{text}\": '{tok}' is not a colour letter followed by a health");
                        continue;
                    }

                    raiders.Add(new ChallengeRaiderSpec(colour, health));
                }

                if (raiders.Count > 0) waves.Add(new ChallengeWave(turn, raiders.ToArray()));
            }

            return waves.ToArray();
        }
    }
}
