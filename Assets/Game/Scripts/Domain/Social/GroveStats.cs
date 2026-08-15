using System;
using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Social
{
    /// <summary>
    /// How everybody else did on a glade, as nine numbers.
    ///
    /// <para>
    /// Deciles: the move counts at the tenth, twentieth … ninetieth percentile of every
    /// keeper's best result, ascending. Nine shorts per glade is a few hundred bytes for
    /// the whole catalog, which is what lets this be one small document the client reads
    /// once rather than a query per level — and it is enough to answer the only question
    /// ever asked of it to within a percentage point.
    /// </para>
    /// <para>
    /// <b>Bests, not runs.</b> A player's own line compares their record against other
    /// players' records, because comparing a fresh run against a population of bests would
    /// tell almost everybody they are below average, which is both untrue and the single
    /// most demoralising thing a victory screen could say.
    /// </para>
    /// </summary>
    public readonly struct LevelStats
    {
        /// <summary>How many keepers this was measured over. The claim's own credibility.</summary>
        public readonly int Samples;

        /// <summary>Move counts at p10 … p90, ascending. Always nine, or empty.</summary>
        public readonly IReadOnlyList<int> Deciles;

        public LevelStats(int samples, IReadOnlyList<int> deciles)
        {
            Samples = samples < 0 ? 0 : samples;
            Deciles = deciles ?? Array.Empty<int>();
        }

        public static readonly LevelStats None = new LevelStats(0, null);

        /// <summary>
        /// Below this many keepers, the line is not drawn at all.
        ///
        /// <para>
        /// Two hundred, and the number is doing real work. A percentile computed over a
        /// dozen players is noise presented as a fact, and the first players to reach a new
        /// chapter are exactly the ones who would see it — the most engaged players in the
        /// game, told something false about themselves. Silence is the correct output of a
        /// sample too small to speak from.
        /// </para>
        /// </summary>
        public const int MinimumSamples = 200;

        public bool IsUsable => Samples >= MinimumSamples && Deciles.Count == 9;

        /// <summary>
        /// What fraction of keepers took <em>more</em> moves than <paramref name="moves"/>,
        /// as a percentage from 0 to 100. -1 when there is not enough to say.
        ///
        /// <para>
        /// Linear interpolation between the deciles, which is the honest reading of what a
        /// decile table knows: it says where the boundaries are and nothing about the shape
        /// in between, so a straight line is the least invented answer. The result is
        /// deliberately never 0 or 100 unless the player is genuinely outside the whole
        /// table — a line saying "you beat 100% of keepers" is a line somebody will find a
        /// counterexample to.
        /// </para>
        /// </summary>
        public int PercentSlower(int moves)
        {
            if (!IsUsable || moves <= 0) return -1;

            // Better than the fastest tenth: everybody in the table took more.
            if (moves <= Deciles[0]) return 95;

            // Slower than the slowest tenth.
            if (moves >= Deciles[8]) return 5;

            for (int i = 0; i < 8; i++)
            {
                int low = Deciles[i], high = Deciles[i + 1];
                if (moves > high) continue;

                // The percentile this move count sits at, interpolated within the band.
                float span = high - low;
                float within = span <= 0f ? 0f : (moves - low) / span;
                float percentile = (i + 1) * 10f + within * 10f;

                // Slower-than-you is the rest of the population.
                int slower = (int)(100f - percentile + .5f);
                return slower < 5 ? 5 : slower > 95 ? 95 : slower;
            }

            return -1;
        }

        /// <summary>
        /// True when the player is comfortably ahead of the pack — the only case worth
        /// putting on a victory screen.
        ///
        /// <para>
        /// Social comparison is a strong motivator and a sharp one. Told they are ahead, a
        /// player plays more; told they are behind, a good share of them stop, and the ones
        /// who stop are disproportionately the ones who were struggling anyway. So the line
        /// is drawn upward only. It is not flattery — every word of it is true — it is a
        /// decision about which true things are worth saying to somebody who has just won.
        /// </para>
        /// </summary>
        public bool IsWorthSaying(int moves) => PercentSlower(moves) >= 50;
    }

    /// <summary>
    /// Everybody's results, by glade — read from the server, never computed here.
    ///
    /// <para>
    /// Published by a scheduled job that samples player saves and writes one small
    /// document; the client fetches it once a session and holds it. It is deliberately
    /// <em>not</em> content in the <c>StreamingAssets</c> sense: it changes daily, it is
    /// derived from live players rather than authored, and a build that shipped a snapshot
    /// of it would be quoting last quarter's population forever.
    /// </para>
    /// <para>
    /// Absent is the normal state and costs nothing — no backend, no network, a brand new
    /// glade nobody has played. Every reader gets <see cref="LevelStats.None"/> and draws
    /// nothing, which is why no screen has to know whether this ever arrived.
    /// </para>
    /// </summary>
    public static class GroveStats
    {
        static Dictionary<LevelId, LevelStats> _byLevel = new Dictionary<LevelId, LevelStats>();

        /// <summary>Raised when a new table is published, so a screen can repaint.</summary>
        public static event Action Changed;

        public static bool IsLoaded { get; private set; }

        public static int LevelCount => _byLevel.Count;

        public static LevelStats For(LevelId level)
            => _byLevel.TryGetValue(level, out var stats) ? stats : LevelStats.None;

        /// <summary>
        /// Adopts a table. Replaces wholesale rather than merging, so a shrinking sample —
        /// a glade retired, a job that read fewer players — cannot leave a stale figure
        /// behind claiming to be current.
        /// </summary>
        public static void Publish(IReadOnlyDictionary<LevelId, LevelStats> table)
        {
            var next = new Dictionary<LevelId, LevelStats>();

            if (table != null)
                foreach (var pair in table)
                    if (pair.Value.IsUsable) next[pair.Key] = pair.Value;

            _byLevel = next;
            IsLoaded = true;

            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        /// <summary>Forgets everything. Dev only, and used by the wipe.</summary>
        public static void Clear()
        {
            _byLevel = new Dictionary<LevelId, LevelStats>();
            IsLoaded = false;
        }
    }
}
