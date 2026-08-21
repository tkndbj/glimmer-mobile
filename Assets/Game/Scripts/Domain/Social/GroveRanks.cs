using System;
using System.Collections.Generic;

namespace GlimmerGrove.Social
{
    /// <summary>
    /// What everybody else's grove is worth, as nine numbers.
    ///
    /// <para>
    /// <b>This is <see cref="LevelStats"/>'s bargain taken to the leaderboard, and it is why
    /// there is no global sort anywhere in this feature.</b> The obvious implementation of
    /// "where do I stand" keeps every player's score in one ordered structure and asks it for
    /// a rank. That is a write on every purchase and a read that has to walk a list which
    /// grows with the game — exactly the trade <c>stats.ts</c> refused for move counts, for
    /// exactly the same reasons. Nine scores published once a day answer the same question to
    /// within a percentage point, in one document, at O(1), at any player count. There is no
    /// scale at which the exact version buys a player anything they could notice.
    /// </para>
    /// <para>
    /// <b>Higher is better here, which is the one difference from <see cref="LevelStats"/>.</b>
    /// A move count is a score you want small, so its reading is "how many keepers took more".
    /// A grove's worth is a score you want large, so the reading is "how many keepers hold
    /// less" — <see cref="PercentBelow"/>. The two are not the same function with a sign
    /// flipped, because the deciles ascend in both cases and only one of them ascends
    /// <em>towards</em> the good end. Written out separately rather than shared, because a
    /// shared one would take a flag and the flag would be got wrong exactly once.
    /// </para>
    /// <para>
    /// <b>Deciles of what population.</b> Every sampled save with a grove worth more than
    /// nothing. Groves worth zero are excluded deliberately: on the day the feature ships,
    /// most accounts have bought nothing, so including them would put the median at zero and
    /// tell the first player who bought a fence that they are ahead of ninety per cent of the
    /// world. The population that means something is "keepers who have built something", and
    /// that is the one a player joins by building something.
    /// </para>
    /// </summary>
    public readonly struct GroveRankTable
    {
        /// <summary>How many keepers this was measured over. The claim's own credibility.</summary>
        public readonly int Samples;

        /// <summary>Grove worth at p10 … p90, ascending. Always nine, or empty.</summary>
        public readonly IReadOnlyList<long> Deciles;

        public GroveRankTable(int samples, IReadOnlyList<long> deciles)
        {
            Samples = samples < 0 ? 0 : samples;
            Deciles = deciles ?? Array.Empty<long>();
        }

        public static readonly GroveRankTable None = new GroveRankTable(0, null);

        /// <summary>
        /// Below this many keepers the reading is noise wearing a decimal point.
        ///
        /// The same two hundred <see cref="LevelStats.MinimumSamples"/> uses, and for the
        /// same reason rather than by coincidence: the first players to reach a new feature
        /// are the most engaged accounts in the game, and they are exactly the ones a
        /// too-small sample would tell something false about themselves.
        /// </summary>
        public const int MinimumSamples = 200;

        /// <summary>
        /// The narrowest and widest standing this will report.
        ///
        /// Deliberately short of 0 and 100, which is <see cref="LevelStats.MinRank"/>'s
        /// argument: a line claiming a player is ahead of everybody is a line somebody will
        /// find a counterexample to, and on a leaderboard the counterexample is printed three
        /// rows above them.
        /// </summary>
        public const int MinRank = 1, MaxRank = 99;

        /// <summary>Nine deciles and enough keepers to mean them.</summary>
        public bool IsUsable => Samples >= MinimumSamples && Deciles != null && Deciles.Count == 9;

        /// <summary>
        /// What share of keepers hold a grove worth <em>less</em> than
        /// <paramref name="score"/>, as a percentage. -1 when there is not enough to say.
        ///
        /// <para>
        /// Linear interpolation between the deciles, which is the honest reading of what a
        /// decile table knows — it says where the boundaries are and nothing about the shape
        /// between them, so a straight line is the least invented answer. A score of zero
        /// gets -1 rather than a percentile: a player who has built nothing is not in the
        /// population these deciles describe, and telling them they are behind everybody is
        /// the one thing <see cref="LevelStats.IsWorthSaying"/> exists to avoid.
        /// </para>
        /// </summary>
        public int PercentBelow(long score)
        {
            if (!IsUsable || score <= 0L) return -1;

            // Below the tenth percentile: at most a tenth of keepers hold less.
            if (score <= Deciles[0]) return MinRank;

            // Above the ninetieth.
            if (score >= Deciles[8]) return MaxRank;

            for (int i = 0; i < 8; i++)
            {
                long low = Deciles[i], high = Deciles[i + 1];
                if (score > high) continue;

                double span = high - low;
                double within = span <= 0d ? 0d : (score - low) / span;
                double percentile = (i + 1) * 10d + within * 10d;

                int below = (int)(percentile + .5d);
                return below < MinRank ? MinRank : below > MaxRank ? MaxRank : below;
            }

            return MaxRank;
        }

        /// <summary>
        /// The share of keepers this grove is <em>ahead of</em>, phrased as the "top N%" a
        /// board draws. -1 when there is not enough to say.
        ///
        /// One method rather than two subtractions at the call sites, because a percentile
        /// and its complement are the easiest pair of numbers in any codebase to swap by
        /// accident and the mistake reads as plausible on screen.
        /// </summary>
        public int TopPercent(long score)
        {
            int below = PercentBelow(score);
            if (below < 0) return -1;

            int top = 100 - below;
            return top < MinRank ? MinRank : top > MaxRank ? MaxRank : top;
        }
    }

    /// <summary>
    /// The published distribution of grove worth, and how many keepers stand in each league.
    ///
    /// <para>
    /// Read from the server, never computed here — <c>publishGroveRanks</c> writes one
    /// document a day and the client holds it for the session, exactly as
    /// <see cref="GroveStats"/> does. Absent is the ordinary state and costs nothing: no
    /// backend, no network, a game whose first day it is. Every reader gets
    /// <see cref="GroveRankTable.None"/> and draws no percentile, which is why no screen has
    /// to know whether this arrived.
    /// </para>
    /// </summary>
    public static class GroveRanks
    {
        static GroveRankTable _table = GroveRankTable.None;
        static Dictionary<string, int> _population = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Raised when a new table is published, so an open board can repaint.</summary>
        public static event Action Changed;

        public static bool IsLoaded { get; private set; }

        /// <summary>When the job that produced this ran, as a Unix timestamp. 0 if unknown.</summary>
        public static long BuiltUnix { get; private set; }

        public static GroveRankTable Table => _table;

        /// <summary>How many keepers stand in a league. Zero for one nobody has reached.</summary>
        public static int PopulationOf(string leagueId)
            => !string.IsNullOrEmpty(leagueId) && _population.TryGetValue(leagueId, out int count)
                ? count
                : 0;

        /// <summary>Every keeper the last job counted, across all leagues.</summary>
        public static int Population
        {
            get
            {
                int total = 0;
                foreach (var pair in _population) total += pair.Value;
                return total;
            }
        }

        /// <summary>
        /// Adopts a table. Replaces wholesale rather than merging, so a league that emptied
        /// cannot leave a stale count behind claiming to be current — <see cref="GroveStats"/>
        /// replaces for the same reason.
        /// </summary>
        public static void Publish(GroveRankTable table, IReadOnlyDictionary<string, int> population,
                                   long builtUnix)
        {
            var next = new Dictionary<string, int>(StringComparer.Ordinal);

            if (population != null)
                foreach (var pair in population)
                    if (GroveLeague.IsKnown(pair.Key) && pair.Value > 0) next[pair.Key] = pair.Value;

            _table = table;
            _population = next;
            BuiltUnix = builtUnix < 0L ? 0L : builtUnix;
            IsLoaded = true;

            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        /// <summary>Forgets everything. Dev only, and used by the wipe.</summary>
        public static void Clear()
        {
            _table = GroveRankTable.None;
            _population = new Dictionary<string, int>(StringComparer.Ordinal);
            BuiltUnix = 0L;
            IsLoaded = false;
        }
    }
}
