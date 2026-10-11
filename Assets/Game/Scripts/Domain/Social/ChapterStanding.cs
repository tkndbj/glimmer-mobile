using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Social
{
    /// <summary>
    /// Where a finished chapter stands against everybody else who finished it (invariant 62).
    ///
    /// <para>
    /// <b>The player's half is computed here, on the handset, from their own save, every time
    /// the map is drawn.</b> The population's half is a table of cut points the server
    /// publishes from a bounded sample of saves (<c>functions/src/standings.ts</c>), read once
    /// an hour at most and held in <see cref="ChapterStandings"/>. That split is what makes the
    /// number fresh - an improved clear moves it the moment the run ends, with no server in the
    /// loop - and what makes it cost nothing per player: no document is written when a level is
    /// cleared, and the table is one read for the whole catalog.
    /// </para>
    /// <para>
    /// <b>The score exists twice and must stay identical</b> (9a): <see cref="ScoreOf"/> here
    /// folds the player's own records, <c>chapterScore</c> on the server folds everybody else's
    /// into the cut points this is compared against. A disagreement would crash nothing - it
    /// would tell a player a number that is quietly about a different population - so both run
    /// <c>standingCases</c> in <c>firebase/shared/grove-vectors.json</c>
    /// (<c>Tools/make_standing_vectors.py</c>).
    /// </para>
    /// </summary>
    public static class ChapterStanding
    {
        /// <summary>
        /// What one star is worth against moves. Stars are the primary grade and moves the
        /// tiebreak, so a star has to outweigh every move a chapter could possibly take: with
        /// each level's moves clamped to <see cref="MovesCap"/>, a thousand-level chapter still
        /// cannot bleed into the next star. Shared with the server (<c>STAR_WEIGHT</c>).
        /// </summary>
        public const long StarWeight = 10_000_000L;

        /// <summary>
        /// The most moves a level may count. A forgery guard rather than a difficulty opinion;
        /// a cleared level recorded with no move count at all (a very old import) counts as the
        /// cap, which is the worst honest reading rather than a free best.
        /// </summary>
        public const int MovesCap = 9_999;

        /// <summary>
        /// What the map says when the table cannot say: "top 1%". The owner's rule, 62 - a
        /// chapter too few keepers have finished for a percentile to mean anything flatters the
        /// ones who have rather than drawing nothing, and so does a table that has not arrived.
        /// </summary>
        public const int WhenUnknown = 1;

        /// <summary>
        /// A finished chapter's score, or null for a chapter not yet finished.
        ///
        /// <para>
        /// Stars first (a star outweighs any number of moves), moves second (fewer is better), as
        /// one integer that is larger for a better result - so a sorted list of them is a ranking
        /// and a cut point through it is a percentile. Every level has to be cleared: a chapter
        /// half done says nothing about where its finisher stands, and null rather than nought
        /// keeps "not finished" apart from "finished badly", which the sampler treats
        /// differently (out of the population, versus in it).
        /// </para>
        /// </summary>
        public static long? ScoreOf(IReadOnlyList<LevelId> levels, Func<LevelId, LevelRecord> recordOf)
        {
            if (levels == null || levels.Count == 0 || recordOf == null) return null;

            long stars = 0L;
            long moves = 0L;

            for (int i = 0; i < levels.Count; i++)
            {
                var record = recordOf(levels[i]);
                if (record == null || record.Stars <= 0) return null;

                stars += Math.Min(record.Stars, LevelRecord.MaxStars);
                moves += record.BestMoves <= 0 ? MovesCap : Math.Min(record.BestMoves, MovesCap);
            }

            return stars * StarWeight - moves;
        }

        /// <summary>This device's score on a chapter, off the save as it stands.</summary>
        public static long? ScoreOf(ChapterIndexEntry chapter)
            => chapter == null ? null : ScoreOf(chapter.LevelIds, PlayerProgress.Record);

        /// <summary>
        /// The "top N%" the map prints for a finished chapter: the table's reading when it has
        /// one, <see cref="WhenUnknown"/> when it has not. Never asked about an unfinished
        /// chapter - that is a different sentence, decided by whether <see cref="ScoreOf"/>
        /// answered at all.
        /// </summary>
        public static int Shown(long score, StandingTable table)
        {
            int top = table.TopPercent(score);
            return top < 0 ? WhenUnknown : top;
        }
    }

    /// <summary>
    /// Everybody else's finished scores on one chapter, as ninety-nine cut points.
    ///
    /// <para>
    /// <b>Percentiles rather than deciles</b>, which is the one departure from
    /// <see cref="GroveRankTable"/>: this is printed as "top N%" with N a whole number, and a
    /// decile table would put everybody above the ninetieth percentile at the same "top 1%".
    /// Nearest-rank cut points let the reading be integer arithmetic - the share of keepers
    /// at or below a score is the number of cut points at or below it - with no interpolation
    /// to get wrong and nothing a float could round differently on two devices.
    /// </para>
    /// <para>
    /// <b>Deciles of what population.</b> Keepers who have cleared every level of the chapter.
    /// Anybody short of that is not in it, and is not asked about it (<see cref="ChapterStanding.ScoreOf"/>).
    /// </para>
    /// </summary>
    public readonly struct StandingTable
    {
        /// <summary>How many cut points a usable table carries: p1 to p99, ascending.</summary>
        public const int CutCount = 99;

        /// <summary>
        /// Below this many finishers the cut points are noise, and the map says "top 1%" instead.
        /// Published with the table by the server; this is only what a document that omits it
        /// is read as.
        /// </summary>
        public const int DefaultMinimumSamples = 50;

        /// <summary>The narrowest and widest standing this will report.</summary>
        public const int MinTop = 1, MaxTop = 99;

        /// <summary>How many finishers this was measured over. The claim's own credibility.</summary>
        public readonly int Samples;

        /// <summary>The minimum the publisher asks for before the table should be read at all.</summary>
        public readonly int MinimumSamples;

        /// <summary>Scores at p1 … p99, ascending. Always ninety-nine, or empty.</summary>
        public readonly IReadOnlyList<long> Cuts;

        public StandingTable(int samples, int minimumSamples, IReadOnlyList<long> cuts)
        {
            Samples = samples < 0 ? 0 : samples;
            MinimumSamples = minimumSamples <= 0 ? DefaultMinimumSamples : minimumSamples;
            Cuts = cuts ?? Array.Empty<long>();
        }

        public static readonly StandingTable None = new StandingTable(0, DefaultMinimumSamples, null);

        /// <summary>Ninety-nine ascending cut points and enough finishers to mean them.</summary>
        public bool IsUsable
        {
            get
            {
                if (Samples < MinimumSamples || Cuts == null || Cuts.Count != CutCount) return false;

                for (int i = 1; i < Cuts.Count; i++)
                    if (Cuts[i] < Cuts[i - 1]) return false;

                return true;
            }
        }

        /// <summary>
        /// The share of finishers at or below <paramref name="score"/>, 0 to 99, or -1 when
        /// there is not enough to say. Counting "at or below" is what puts a tied score at the
        /// top of its own tie, which is the reading a player checking against a friend expects.
        /// </summary>
        public int PercentAtOrBelow(long score)
        {
            if (!IsUsable) return -1;

            // The cuts ascend, so the count of cuts at or below the score is the first index
            // above it - found by a binary search rather than a walk, because this runs on every
            // map repaint and a table is ninety-nine entries by as many chapters as the catalog.
            int low = 0, high = Cuts.Count;
            while (low < high)
            {
                int mid = (low + high) >> 1;
                if (Cuts[mid] <= score) low = mid + 1;
                else high = mid;
            }

            return low;
        }

        /// <summary>
        /// The "top N%" the map prints: the share of finishers this score is not behind, clamped
        /// to [<see cref="MinTop"/>, <see cref="MaxTop"/>] so the line never claims to be ahead
        /// of everybody or behind everybody. -1 when there is not enough to say.
        /// </summary>
        public int TopPercent(long score)
        {
            int below = PercentAtOrBelow(score);
            if (below < 0) return -1;

            int top = 100 - below;
            return top < MinTop ? MinTop : top > MaxTop ? MaxTop : top;
        }
    }

    /// <summary>
    /// One run of the standings job, as the client reads it: a table per chapter id.
    ///
    /// A named type rather than a tuple, for <see cref="GroveRankPublication"/>'s reason. Every
    /// part of it is optional in the honest sense: a chapter the document does not name reads as
    /// <see cref="StandingTable.None"/>, which every reader treats as "nothing to say".
    /// </summary>
    public readonly struct ChapterStandingPublication
    {
        /// <summary>Each chapter's table, keyed by chapter id.</summary>
        public readonly IReadOnlyDictionary<string, StandingTable> Chapters;

        /// <summary>When the job that produced this ran, as a Unix timestamp. 0 if unknown.</summary>
        public readonly long BuiltUnix;

        public ChapterStandingPublication(IReadOnlyDictionary<string, StandingTable> chapters, long builtUnix)
        {
            Chapters = chapters;
            BuiltUnix = builtUnix < 0L ? 0L : builtUnix;
        }

        /// <summary>What a failed read, an absent document and a build with no backend all are.</summary>
        public static readonly ChapterStandingPublication None = new ChapterStandingPublication(null, 0L);
    }

    /// <summary>
    /// The published chapter tables, held for the session.
    ///
    /// <para>
    /// Read from the server, never computed here - the client holds no other player's save and
    /// never will (19). Absent is the ordinary state and costs nothing: no backend, no network,
    /// a game whose first day it is. Every reader gets <see cref="StandingTable.None"/> and
    /// prints "top 1%" (<see cref="ChapterStanding.WhenUnknown"/>), which is why no screen has
    /// to know whether this arrived.
    /// </para>
    /// </summary>
    public static class ChapterStandings
    {
        static Dictionary<string, StandingTable> _tables = new Dictionary<string, StandingTable>(StringComparer.Ordinal);

        /// <summary>Raised when a new table is published, so an open map can repaint.</summary>
        public static event Action Changed;

        public static bool IsLoaded { get; private set; }

        /// <summary>When the job that produced this ran, as a Unix timestamp. 0 if unknown.</summary>
        public static long BuiltUnix { get; private set; }

        /// <summary>One chapter's table, or <see cref="StandingTable.None"/> for one not published.</summary>
        public static StandingTable TableFor(ChapterId chapter)
            => chapter.IsValid && _tables.TryGetValue(chapter.Value, out var table) ? table : StandingTable.None;

        /// <summary>
        /// Adopts a publication. Replaces wholesale rather than merging, so a chapter withdrawn
        /// from the catalog cannot leave a stale table behind claiming to be current.
        /// </summary>
        public static void Publish(ChapterStandingPublication published)
        {
            var next = new Dictionary<string, StandingTable>(StringComparer.Ordinal);

            if (published.Chapters != null)
                foreach (var pair in published.Chapters)
                    if (!string.IsNullOrEmpty(pair.Key)) next[pair.Key] = pair.Value;

            _tables = next;
            BuiltUnix = published.BuiltUnix;
            IsLoaded = true;

            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        /// <summary>Forgets everything. Dev only, and used by the wipe.</summary>
        public static void Clear()
        {
            _tables = new Dictionary<string, StandingTable>(StringComparer.Ordinal);
            BuiltUnix = 0L;
            IsLoaded = false;
        }
    }
}
