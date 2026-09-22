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
        public readonly string Sources, Sinks;
        public readonly int Target, Hill, Bolts;
        public readonly ChallengeWave[] Waves;

        public ChallengeDefinition(string id, ChallengeGenre genre, uint seed, int width, int height,
                                   string[] rows, string[] gems, string sources, string sinks,
                                   int target, int hill, int bolts, ChallengeWave[] waves)
        {
            Id = id;
            Genre = genre;
            Seed = seed;
            Width = width;
            Height = height;
            Rows = rows ?? Array.Empty<string>();
            Gems = gems ?? Array.Empty<string>();
            Sources = sources ?? string.Empty;
            Sinks = sinks ?? string.Empty;
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
    /// Every challenge this build can deal, read from <c>challenges.json</c>.
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
    /// <b>Versions independently of everything</b> (9b's rule): this file says its own
    /// <see cref="Version"/>, and a build refuses a file from the future rather than reading
    /// it half.
    /// </para>
    /// </summary>
    public sealed class ChallengeTable
    {
        public const int Version = 1;

        /// <summary>The built-in default is an empty slate: no challenge is ever hard-coded.</summary>
        public static readonly ChallengeTable Default =
            new ChallengeTable(new ChallengeLine(1, 3, 1), Array.Empty<ChallengeDefinition>());

        public readonly ChallengeLine Line;

        readonly ChallengeDefinition[] _rows;

        ChallengeTable(ChallengeLine line, ChallengeDefinition[] rows)
        {
            Line = line;
            _rows = rows;
        }

        public IReadOnlyList<ChallengeDefinition> All => _rows;
        public int Count => _rows.Length;
        public bool IsEmpty => _rows.Length == 0;

        public ChallengeDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _rows.Length; i++)
                if (_rows[i].Id == id) return _rows[i];
            return null;
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
        /// id) and true with problems listed when individual rows were dropped — a bad row costs
        /// that challenge, never the slate.
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
            if (lineDto.damage <= 0 || lineDto.health <= 0 || lineDto.strike <= 0)
            {
                problems.Add("challenges line needs damage, health and strike all above nought");
                return false;
            }

            var line = new ChallengeLine(lineDto.damage, lineDto.health, lineDto.strike);

            var rows = new List<ChallengeDefinition>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool ok = true;

            var list = dto.challenges ?? Array.Empty<ChallengeDto>();
            for (int i = 0; i < list.Length; i++)
            {
                var row = list[i];
                string where = string.IsNullOrEmpty(row.id) ? $"challenge #{i}" : $"challenge '{row.id}'";

                if (string.IsNullOrEmpty(row.id))
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

            table = new ChallengeTable(line, rows.ToArray());
            return ok;
        }

        static ChallengeDefinition Build(ChallengeDto row, ChallengeLine line, string where, List<string> problems)
        {
            int before = problems.Count;

            if (!ChallengeGenres.TryParse(row.genre, out var genre))
            {
                problems.Add($"{where} names genre '{row.genre}', which this build does not play");
                return null;
            }

            if (row.width <= 0 || row.height <= 0)
                problems.Add($"{where} needs a width and a height");

            if (row.hill <= 0)
                problems.Add($"{where} needs a hill of at least one step");

            var waves = ParseWaves(row.waves, where, problems);

            var def = new ChallengeDefinition(row.id, genre, (uint)row.seed, row.width, row.height,
                                              row.rows, row.gems, row.sources, row.sinks,
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
