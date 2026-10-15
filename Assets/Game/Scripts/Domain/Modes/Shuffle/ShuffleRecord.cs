using System;
using System.Collections.Generic;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// What one finished Shuffle run built (MODES.md 59o): its grade, its score, how far it got and
    /// the figures its build ended on. What the victory panel draws, what the ledger keeps the best
    /// of per level, and what the keeper's card publishes.
    ///
    /// <para>
    /// <b>Immutable and canonical.</b> Stats are held sorted by id with one row per id, inside the
    /// bounds every reader enforces (<see cref="ShuffleStats"/>), so two records are equal exactly
    /// when they say the same thing and <c>SaveDelta</c> never reads a reordering as a change.
    /// </para>
    /// <para>
    /// <b>The best is a total order, so the merge is a <c>max</c></b> (invariant 11b):
    /// <see cref="Compare"/> orders on grade, then score, then wave, then the stats themselves, so
    /// two devices holding different runs agree on which one survives in whatever order they sync,
    /// and a run is never half-replaced by another's figures. <b>The grade is stored, never
    /// re-derived</b>: it is what the run earned on the floors it was graded against
    /// (<see cref="ShuffleGrades.Floors"/>).
    /// </para>
    /// <para>
    /// <b>It pays nothing and never may</b> (invariants 13 and 19l): a figure the server cannot
    /// recompute is safe on a public card only while it is bounded and buys nothing.
    /// </para>
    /// </summary>
    public sealed class ShuffleRecord
    {
        /// <summary>
        /// The grade's ordinal as it was earned, <b>carried as written</b> up to
        /// <see cref="ShuffleGrades.MaxOrdinal"/>: a grade appended by a newer build survives a
        /// trip through this one unchanged, because a reader that repaired it would make every
        /// write owe a sync (invariant 11f). <see cref="Grade"/> is what this build draws.
        /// </summary>
        public readonly int Ordinal;

        /// <summary>The grade as this build draws it: an ordinal it does not know reads as its top.</summary>
        public ShuffleGrade Grade => ShuffleGrades.Sane(Ordinal);
        public readonly int Points;
        public readonly int Wave;

        readonly ShuffleStatValue[] _stats;

        /// <summary>Every figure carried, sorted by id - including ids this build does not draw.</summary>
        public IReadOnlyList<ShuffleStatValue> Stats => _stats;

        ShuffleRecord(int ordinal, int points, int wave, ShuffleStatValue[] stats)
        {
            Ordinal = ordinal;
            Points = points;
            Wave = wave;
            _stats = stats;
        }

        /// <summary>
        /// A record built from figures a reader took off a file, the wire or a card, or null when
        /// they describe no run: no points (a run that took no card banks nothing), so "absent" is
        /// a value no real record holds (invariant 11b's rule about <c>JsonUtility</c>'s zeros).
        /// </summary>
        public static ShuffleRecord Create(int grade, int points, int wave, IEnumerable<ShuffleStatValue> stats)
        {
            if (points <= 0) return null;

            if (points > ShuffleGrades.MaxPoints) points = ShuffleGrades.MaxPoints;
            if (wave < 0) wave = 0;
            if (wave > ShuffleLedger.MaxWave) wave = ShuffleLedger.MaxWave;

            if (grade < 0) grade = 0;
            if (grade > ShuffleGrades.MaxOrdinal) grade = ShuffleGrades.MaxOrdinal;

            return new ShuffleRecord(grade, points, wave, Canonical(stats));
        }

        /// <summary>
        /// The record <paramref name="build"/> earns having seen off <paramref name="wave"/> waves,
        /// graded on today's floors. Null for a build holding no card.
        /// </summary>
        public static ShuffleRecord Of(ShuffleBuild build, int wave)
        {
            if (build == null) return null;

            int points = ShuffleGrades.Score(build, out _, out int bonds);
            return Create((int)ShuffleGrades.Of(points, bonds), points, wave, ShuffleStats.Read(build));
        }

        /// <summary>The figure carried under <paramref name="id"/>, and whether there is one.</summary>
        public bool TryGet(string id, out int value)
        {
            for (int i = 0; i < _stats.Length; i++)
                if (string.Equals(_stats[i].Id, id, StringComparison.Ordinal)) { value = _stats[i].Value; return true; }

            value = 0;
            return false;
        }

        static ShuffleStatValue[] Canonical(IEnumerable<ShuffleStatValue> stats)
        {
            var rows = new SortedDictionary<string, int>(StringComparer.Ordinal);

            if (stats != null)
                foreach (var stat in stats)
                {
                    if (!ShuffleStats.WellFormed(stat.Id)) continue;

                    // One row per id; a duplicate keeps the larger, which is the only reading of
                    // two figures for one id that no order of arrival can change.
                    if (rows.TryGetValue(stat.Id, out int held) && held >= stat.Value) continue;
                    rows[stat.Id] = stat.Value;
                }

            // **The first `MaxRows` ids in id order**, never the first that arrived: a map's
            // iteration order is the reader's business (Firestore, Mono and V8 each have their
            // own), and the server's `shuffleTop` must keep exactly the rows this keeps.
            int take = rows.Count < ShuffleStats.MaxRows ? rows.Count : ShuffleStats.MaxRows;
            var canonical = new ShuffleStatValue[take];
            int at = 0;
            foreach (var pair in rows)
            {
                if (at >= take) break;
                canonical[at++] = new ShuffleStatValue(pair.Key, pair.Value);
            }

            return canonical;
        }

        // ------------------------------------------------------------------ the order
        /// <summary>
        /// The total order the best is kept on: grade, score, wave, then the stats row by row.
        /// Null is below everything. Antisymmetric and transitive, so <see cref="Better"/> is a
        /// commutative, associative, idempotent join.
        /// </summary>
        public static int Compare(ShuffleRecord a, ShuffleRecord b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            if (a.Ordinal != b.Ordinal) return a.Ordinal.CompareTo(b.Ordinal);
            if (a.Points != b.Points) return a.Points.CompareTo(b.Points);
            if (a.Wave != b.Wave) return a.Wave.CompareTo(b.Wave);

            int n = Math.Min(a._stats.Length, b._stats.Length);
            for (int i = 0; i < n; i++)
            {
                int id = string.CompareOrdinal(a._stats[i].Id, b._stats[i].Id);
                if (id != 0) return -id;     // a row the other lacks first is the richer record
                if (a._stats[i].Value != b._stats[i].Value) return a._stats[i].Value.CompareTo(b._stats[i].Value);
            }

            return a._stats.Length.CompareTo(b._stats.Length);
        }

        /// <summary>The better of two records, either possibly null.</summary>
        public static ShuffleRecord Better(ShuffleRecord a, ShuffleRecord b) => Compare(a, b) >= 0 ? a : b;

        /// <summary>Whether two records say exactly the same thing.</summary>
        public static bool Same(ShuffleRecord a, ShuffleRecord b) => Compare(a, b) == 0;

        // ------------------------------------------------------------------ the file
        /// <summary>The record a save row carries, or null for none (or one that says nothing).</summary>
        public static ShuffleRecord FromDto(ShuffleRunDto dto)
        {
            if (dto == null) return null;

            var stats = new List<ShuffleStatValue>(dto.stats != null ? dto.stats.Length : 0);
            if (dto.stats != null)
                foreach (var row in dto.stats)
                    if (row != null) stats.Add(new ShuffleStatValue(row.id, row.value));

            return Create(dto.grade, dto.points, dto.wave, stats);
        }

        /// <summary>
        /// The row's shape for this record. <b>Never null</b>: an absent record is written as a run
        /// with no points, which is what <c>JsonUtility</c> reads an absent object back as anyway.
        /// </summary>
        public static ShuffleRunDto ToDto(ShuffleRecord record)
        {
            if (record == null) return new ShuffleRunDto { stats = Array.Empty<ShuffleStatDto>() };

            var stats = new ShuffleStatDto[record._stats.Length];
            for (int i = 0; i < stats.Length; i++)
                stats[i] = new ShuffleStatDto { id = record._stats[i].Id, value = record._stats[i].Value };

            return new ShuffleRunDto
            {
                grade = record.Ordinal,
                points = record.Points,
                wave = record.Wave,
                stats = stats,
            };
        }

        // ------------------------------------------------------------------ the wire
        /// <summary>
        /// The record as Firestore carries it - on a save row (<c>shuffleBest[].top</c>) and on a
        /// keeper's card (<c>shuffleTop</c>), one shape for both: <c>{grade, points, wave, stats:
        /// {id: value}}</c>. A map for the stats because an id is a key and can then never appear
        /// twice. Null for no record, so a caller omits the field.
        /// </summary>
        public static Dictionary<string, object> ToWire(ShuffleRecord record)
        {
            if (record == null) return null;

            var stats = new Dictionary<string, object>(record._stats.Length);
            for (int i = 0; i < record._stats.Length; i++) stats[record._stats[i].Id] = (long)record._stats[i].Value;

            return new Dictionary<string, object>
            {
                { "grade", (long)record.Ordinal },
                { "points", (long)record.Points },
                { "wave", (long)record.Wave },
                { "stats", stats },
            };
        }

        /// <summary>
        /// The record a wire map describes, or null for none or nothing readable. Tolerant the
        /// way every reader of a document another build or a console may have written is: a number
        /// may arrive as any numeric type, a malformed stat is skipped and an unknown one carried,
        /// and every figure is bounded on the way in (<see cref="Create"/>).
        /// </summary>
        public static ShuffleRecord FromWire(object raw)
        {
            if (!(raw is IDictionary<string, object> map)) return null;

            var stats = new List<ShuffleStatValue>(ShuffleStats.All.Length);
            if (map.TryGetValue("stats", out object rawStats) && rawStats is IDictionary<string, object> rows)
            {
                foreach (var pair in rows)
                    stats.Add(new ShuffleStatValue(pair.Key, Narrow(Number(pair.Value))));
            }

            return Create(Narrow(Number(Field(map, "grade"))), Narrow(Number(Field(map, "points"))),
                          Narrow(Number(Field(map, "wave"))), stats);
        }

        static object Field(IDictionary<string, object> map, string key)
            => map.TryGetValue(key, out object value) ? value : null;

        static long Number(object value)
        {
            switch (value)
            {
                case long l: return l;
                case int i: return i;
                case double d: return Truncate(d);
                case float f: return Truncate(f);
                default: return 0L;
            }
        }

        /// <summary>A wire double toward nought, NaN as nought, saturating: <c>Math.trunc</c> on the server.</summary>
        static long Truncate(double d)
            => double.IsNaN(d) ? 0L : d >= 9.2e18 ? long.MaxValue : d <= -9.2e18 ? long.MinValue : (long)d;

        static int Narrow(long value) => value < int.MinValue ? int.MinValue : value > int.MaxValue ? int.MaxValue : (int)value;

        /// <summary>
        /// Every part of the record as one stable string - grade, score, wave and each stat in id
        /// order, invariant digits - for <c>GroveCard.Fingerprint</c>. Equal exactly when
        /// <see cref="Same"/> is.
        /// </summary>
        public string Fingerprint()
        {
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            var key = new System.Text.StringBuilder(64);
            key.Append(Ordinal.ToString(invariant)).Append('/')
               .Append(Points.ToString(invariant)).Append('/')
               .Append(Wave.ToString(invariant));

            for (int i = 0; i < _stats.Length; i++)
                key.Append('/').Append(_stats[i].Id).Append('=').Append(_stats[i].Value.ToString(invariant));

            return key.ToString();
        }

        public override string ToString()
            => ShuffleGrades.Id(Grade) + " " + Points + " @" + Wave + " [" + string.Join(",", _stats) + "]";
    }
}
