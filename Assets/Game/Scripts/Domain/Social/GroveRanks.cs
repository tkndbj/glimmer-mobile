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
    /// <b>Deciles of what population.</b> Every sampled save that has actually done the thing.
    /// For grove worth that is "keepers who have built something" — groves worth zero are
    /// excluded deliberately, because on the day the feature ships most accounts have bought
    /// nothing and including them would put the median at zero and tell the first player who
    /// bought a fence that they are ahead of ninety per cent of the world. For waves it is
    /// "keepers who have run the Infinite lane", for the same reason read across.
    /// </para>
    /// <para>
    /// <b>One type, two distributions, and no flag.</b> Grove worth and a wave count are both
    /// scores you want <em>large</em>, so <see cref="PercentBelow"/> is the same function over
    /// both and this is used twice rather than being copied or parameterised — the reading that
    /// genuinely differs is <c>LevelStats</c>', where a move count is a score you want small,
    /// and that one is written out separately on purpose.
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
    /// One night's ranking job, as the client reads it.
    ///
    /// <para>
    /// <b>A named type rather than a tuple, and the second distribution is why.</b> This
    /// arrived as <c>(result, table, population, builtUnix)</c> and adding waves would have made
    /// it six positional values threaded through an interface, an implementation, a test double
    /// and a caller — where the only thing stopping two same-typed members being swapped is
    /// whoever is reading the diff. Every field here is named at every call site, and a third
    /// distribution costs one member instead of one more position.
    /// </para>
    /// <para>
    /// Every part of it is optional in the honest sense: a document written before waves were
    /// published leaves <see cref="Waves"/> at <see cref="GroveRankTable.None"/>, which every
    /// reader already treats as "nothing to say".
    /// </para>
    /// </summary>
    public readonly struct GroveRankPublication
    {
        /// <summary>Where a grove's worth stands against everybody who has built something.</summary>
        public readonly GroveRankTable Groves;

        /// <summary>Where a wave stands against everybody who has run the Infinite lane.</summary>
        public readonly GroveRankTable Waves;

        /// <summary>How many keepers each board was chosen from, keyed by board id.</summary>
        public readonly IReadOnlyDictionary<string, int> Population;

        /// <summary>When the job that produced this ran, as a Unix timestamp. 0 if unknown.</summary>
        public readonly long BuiltUnix;

        public GroveRankPublication(GroveRankTable groves, GroveRankTable waves,
                                    IReadOnlyDictionary<string, int> population, long builtUnix)
        {
            Groves = groves;
            Waves = waves;
            Population = population;
            BuiltUnix = builtUnix < 0L ? 0L : builtUnix;
        }

        /// <summary>What a failed read, an absent document and a build with no backend all are.</summary>
        public static readonly GroveRankPublication None =
            new GroveRankPublication(GroveRankTable.None, GroveRankTable.None, null, 0L);
    }

    /// <summary>
    /// The published distributions, and how many keepers each board was chosen from.
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

        static GroveRankTable _waves = GroveRankTable.None;

        /// <summary>Where a grove's worth stands. Empty until a job has published one.</summary>
        public static GroveRankTable Table => _table;

        /// <summary>
        /// Where a wave stands, against keepers who have run the Infinite lane.
        ///
        /// <b>A hundred rows is a board; this is what answers the same question for everybody
        /// else.</b> At ten million players the Endless Watch reaches 0.001% of them, so without
        /// this the lane would be ranked for a hundred people and silent for the rest — and it
        /// costs no reads at all, because it comes out of the sample the worth deciles already
        /// walk.
        /// </summary>
        public static GroveRankTable Waves => _waves;

        /// <summary>
        /// How many keepers one board was chosen from. Zero for a board nobody is on.
        ///
        /// Keyed by <see cref="LeaderboardBoard"/> id, so "the finest of N" and "N keepers have
        /// held the line" are the same reading asked of two boards rather than two fields.
        /// </summary>
        public static int PopulationOf(string boardId)
            => !string.IsNullOrEmpty(boardId) && _population.TryGetValue(boardId, out int count)
                ? count
                : 0;

        /// <summary>
        /// Every keeper on the global board — which is the game's population of keepers who
        /// have built something, and not a sum.
        ///
        /// <b>Deliberately not added up across the boards.</b> A keeper with a grove and an
        /// endless best is counted on both, so a total would count them twice and would move when
        /// a board was added. The global count is the one that means "how many people is this".
        /// </summary>
        public static int Population => PopulationOf(LeaderboardBoard.Global);

        /// <summary>
        /// Adopts a table. Replaces wholesale rather than merging, so a board that emptied
        /// cannot leave a stale count behind claiming to be current — <see cref="GroveStats"/>
        /// replaces for the same reason.
        /// </summary>
        public static void Publish(GroveRankPublication published)
        {
            var next = new Dictionary<string, int>(StringComparer.Ordinal);

            if (published.Population != null)
                foreach (var pair in published.Population)
                    if (LeaderboardBoard.IsKnown(pair.Key) && pair.Value > 0) next[pair.Key] = pair.Value;

            _table = published.Groves;
            _waves = published.Waves;
            _population = next;
            BuiltUnix = published.BuiltUnix;
            IsLoaded = true;

            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        /// <summary>Forgets everything. Dev only, and used by the wipe.</summary>
        public static void Clear()
        {
            _table = GroveRankTable.None;
            _waves = GroveRankTable.None;
            _population = new Dictionary<string, int>(StringComparer.Ordinal);
            BuiltUnix = 0L;
            IsLoaded = false;
        }
    }
}
