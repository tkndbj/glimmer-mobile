using System;
using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Ranks
{
    /// <summary>
    /// The rank ladder: every rung, humblest first, immutable once built.
    ///
    /// <para>
    /// <b>Content, and the whole of it.</b> Which rungs exist, in what order and asking for
    /// what are all rows of <c>progression.json</c>, so a rung retuned, added or withdrawn is a
    /// content push. What a push cannot do is ship a picture, which is the one place this
    /// stops — a new rung needs its badge cut into <c>Art/Ui/Rank/</c> by
    /// <c>Tools/make_rank_art.py</c> and addressed, exactly as a new utility needs its icon
    /// (invariant 7b). <c>check_ranks</c> refuses a rung whose badge is not on disk rather than
    /// letting it draw a white rectangle.
    /// </para>
    /// <para>
    /// <b>Rides with the curve</b> for the reason every block in <c>ProgressionTable</c> does,
    /// and one of its own: a rung asks about keeper levels, stars and waves, every one of which
    /// is decided by a number in the same file. A ladder loaded separately from the curve would
    /// be a set of goals measured against a game that had moved — the same window the chest
    /// table and the ad payouts are published here to close.
    /// </para>
    /// <para>
    /// <b>The default is empty, and that is deliberate.</b> Every other table here has a
    /// built-in copy because a game with no chest odds or no heart gate cannot be played; a
    /// game with no ranks can. A hard-coded ladder would be a second answer nobody maintains,
    /// and the first thing to go stale the moment content moved — so a file that cannot be read
    /// draws no badge and no page rather than a ladder that disagrees with the one the owner
    /// authored. Both readouts take themselves off screen when the ladder is empty.
    /// </para>
    /// </summary>
    public sealed class RankLadder
    {
        readonly RankDefinition[] _rungs;
        readonly Dictionary<string, RankDefinition> _byId;

        RankLadder(RankDefinition[] rungs)
        {
            _rungs = rungs ?? Array.Empty<RankDefinition>();

            _byId = new Dictionary<string, RankDefinition>(StringComparer.Ordinal);
            foreach (var rung in _rungs) _byId[rung.Id] = rung;
        }

        /// <summary>No ranks at all. See the class remarks for why this is the default.</summary>
        public static readonly RankLadder Empty = new RankLadder(Array.Empty<RankDefinition>());

        /// <summary>Every rung, humblest first. Authored order <em>is</em> the ladder.</summary>
        public IReadOnlyList<RankDefinition> Rungs => _rungs;

        public int Count => _rungs.Length;

        public bool IsEmpty => _rungs.Length == 0;

        public RankDefinition Find(string id)
            => id != null && _byId.TryGetValue(id, out var rung) ? rung : null;

        /// <summary>The rung at an ordinal, one-based. Null outside the ladder.</summary>
        public RankDefinition At(int ordinal)
            => ordinal >= 1 && ordinal <= _rungs.Length ? _rungs[ordinal - 1] : null;

        // ------------------------------------------------------------------ the reading
        /// <summary>
        /// The highest rung whose lines are met <em>and</em> every rung below which is too.
        /// Null for a player who has not reached the first one.
        ///
        /// <para>
        /// <b>Consecutive rather than highest-met, and that is a safety property rather than a
        /// nicety.</b> A ladder is authored by a person, and one rung asking for something the
        /// rung above it does not is an ordinary authoring slip — with "highest met" that slip
        /// hands out rank six to somebody who never met rank five, and the page then draws a
        /// held badge above unmet lines. Walking up from the bottom makes the ladder monotone by
        /// construction whatever content says, and the gate still errors on the slip so it is
        /// fixed rather than absorbed.
        /// </para>
        /// <para>
        /// It is also what makes a rank unable to fall: every line only ever rises (invariant
        /// 52), so the run of met rungs only ever grows.
        /// </para>
        /// </summary>
        public RankDefinition Held(CatalogIndex index)
        {
            RankDefinition held = null;

            for (int i = 0; i < _rungs.Length; i++)
            {
                if (!_rungs[i].IsMet(index)) break;
                held = _rungs[i];
            }

            return held;
        }

        /// <summary>
        /// The rung being climbed: the first one not yet held. Null at the top of the ladder,
        /// which is the one state a progress bar has nothing to say about.
        /// </summary>
        public RankDefinition Next(CatalogIndex index)
        {
            for (int i = 0; i < _rungs.Length; i++)
                if (!_rungs[i].IsMet(index)) return _rungs[i];

            return null;
        }

        /// <summary>
        /// How far up the ladder the player stands, one-based; nought for a player below the
        /// first rung. The number a screen counts "3 / 7" from.
        /// </summary>
        public int OrdinalHeld(CatalogIndex index)
        {
            var held = Held(index);
            return held == null ? 0 : held.Ordinal;
        }

        /// <summary>
        /// Whether a rung is held, which is <em>not</em> the same as its own lines being met —
        /// see <see cref="Held"/>. The question a page's badge asks per row.
        /// </summary>
        public bool IsHeld(RankDefinition rung, CatalogIndex index)
            => rung != null && rung.Ordinal <= OrdinalHeld(index);

        // ------------------------------------------------------------------ building
        /// <summary>Sanity bounds, not tuning. A ladder longer than this is a typo.</summary>
        public const int MaxRungs = 24;
        public const int MaxRequirements = 8;

        /// <summary>
        /// Reads the optional <c>ranks</c> block. Never throws and never returns null.
        ///
        /// <para>
        /// <b>Refused whole on a structural fault, degraded on an unknown</b> — the split
        /// <c>TaskTable.Resolve</c> draws, and it matters more here than anywhere: a ladder
        /// missing one line of one rung is a rank handed out for less than it asks for, which
        /// nobody would ever notice. So a malformed rung takes the whole ladder down to
        /// <see cref="Empty"/> and names itself in <paramref name="problems"/>, where the build
        /// gate turns it into an error.
        /// </para>
        /// <para>
        /// The one thing that is <em>not</em> a structural fault is a measure this build has
        /// never heard of: that is a newer content pack reaching an older client, and dropping
        /// the whole ladder for it would take the feature off the screen of every player who
        /// had not updated. Such a rung is dropped by name, which can only ever make a ladder
        /// easier to climb on an old build — never harder — and the rungs above it still ask
        /// for everything they always did.
        /// </para>
        /// </summary>
        public static RankLadder Resolve(RanksDto dto, List<string> problems)
        {
            if (problems == null) problems = new List<string>();

            // Absent is not an error: JsonUtility instantiates the block whether or not the
            // file wrote one, so "absent" is a block with no array — a value a real one cannot
            // hold (`IsAuthored`), which is the fixed shape for a serialised class field.
            if (dto == null || !dto.IsAuthored) return Empty;

            if (dto.rungs.Length > MaxRungs)
            {
                problems.Add($"ranks block lists {dto.rungs.Length} rung(s), more than the " +
                             $"supported {MaxRungs}; no ranks are drawn");
                return Empty;
            }

            var rungs = new List<RankDefinition>(dto.rungs.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < dto.rungs.Length; i++)
            {
                var entry = dto.rungs[i];
                if (entry == null) { problems.Add($"ranks rung {i} is empty"); return Empty; }

                if (!RankDefinition.IsValidId(entry.id))
                {
                    problems.Add($"ranks rung {i} id '{entry.id}' is rejected: ids are lower-case " +
                                 "letters, digits and underscores, because they name a badge on " +
                                 "disk and a loc key");
                    return Empty;
                }

                if (!seen.Add(entry.id))
                {
                    problems.Add($"ranks rung '{entry.id}' is listed twice; the badge and the name " +
                                 "are derived from the id, so two rungs sharing one are one rung " +
                                 "drawn twice");
                    return Empty;
                }

                var lines = ReadRequirements(entry, problems, out bool unknown);
                if (lines == null) return Empty;

                if (unknown)
                {
                    problems.Add($"ranks rung '{entry.id}' names a measure this build cannot read " +
                                 "and is dropped; it is a newer content pack reaching an older " +
                                 "client, and a dropped rung can only make the ladder easier");
                    continue;
                }

                rungs.Add(new RankDefinition(entry.id, rungs.Count + 1, lines));
            }

            return rungs.Count == 0 ? Empty : new RankLadder(rungs.ToArray());
        }

        /// <summary>
        /// One rung's lines. Null on a structural fault; <paramref name="unknown"/> when the
        /// rung is well formed but names something this build cannot read.
        /// </summary>
        static RankRequirement[] ReadRequirements(RankRungDto entry, List<string> problems,
                                                  out bool unknown)
        {
            unknown = false;

            if (entry.requires == null || entry.requires.Length == 0)
            {
                problems.Add($"ranks rung '{entry.id}' asks for nothing; a rung with no " +
                             "requirement is a badge every account already holds");
                return null;
            }

            if (entry.requires.Length > MaxRequirements)
            {
                problems.Add($"ranks rung '{entry.id}' has {entry.requires.Length} requirement(s), " +
                             $"more than the supported {MaxRequirements}");
                return null;
            }

            var lines = new RankRequirement[entry.requires.Length];

            for (int i = 0; i < entry.requires.Length; i++)
            {
                var line = entry.requires[i];
                if (line == null)
                {
                    problems.Add($"ranks rung '{entry.id}' requirement {i} is empty");
                    return null;
                }

                var measure = RankMeasures.Parse(line.measure);
                if (measure.IsNone) { unknown = true; return Array.Empty<RankRequirement>(); }

                if (line.target < 1)
                {
                    problems.Add($"ranks rung '{entry.id}' asks for {line.target} of " +
                                 $"'{line.measure}'; a target below one is met by every account");
                    return null;
                }

                if (line.target > Tasks.LifetimeTally.Ceiling)
                {
                    problems.Add($"ranks rung '{entry.id}' asks for {line.target} of " +
                                 $"'{line.measure}', above the {Tasks.LifetimeTally.Ceiling} a " +
                                 "counter may ever reach; it could never be met");
                    return null;
                }

                string scope = line.scope ?? string.Empty;
                if (scope.Length > 0 && measure.Scope == RankScopeKind.None)
                {
                    // Refused rather than ignored: a requirement that reads as "clear ten glades
                    // of Barrowfell" and is met by clearing ten glades anywhere is a rung that
                    // lies on the page, which nothing downstream could ever catch.
                    problems.Add($"ranks rung '{entry.id}' scopes '{line.measure}' to '{scope}', " +
                                 "and that measure is about the whole account; a scope it cannot " +
                                 "honour would be a sentence the ladder does not mean");
                    return null;
                }

                lines[i] = new RankRequirement(measure, scope, line.target);
            }

            return lines;
        }
    }
}
