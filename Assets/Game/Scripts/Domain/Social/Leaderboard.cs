using System;
using System.Collections.Generic;

namespace GlimmerGrove.Social
{
    /// <summary>One row of a board: who, what their grove is worth, and where they placed.</summary>
    public readonly struct LeaderboardEntry
    {
        /// <summary>Position on this board, counting from 1.</summary>
        public readonly int Rank;

        /// <summary>The account, so tapping the row can fetch its grove.</summary>
        public readonly string OwnerId;

        /// <summary>Already in its public form — see <see cref="GroveNames"/>.</summary>
        public readonly string Name;

        public readonly string AvatarId;
        public readonly int KeeperLevel;
        public readonly long Score;
        public readonly int Stars;

        public LeaderboardEntry(int rank, string ownerId, string name, string avatarId,
                                int keeperLevel, long score, int stars)
        {
            Rank = rank < 1 ? 1 : rank;
            OwnerId = ownerId ?? string.Empty;
            Name = name ?? string.Empty;
            AvatarId = avatarId ?? string.Empty;
            KeeperLevel = keeperLevel < 1 ? 1 : keeperLevel;
            Score = score < 0L ? 0L : score;
            Stars = stars < 0 ? 0 : stars;
        }

        public bool IsValid => OwnerId.Length > 0;
    }

    /// <summary>
    /// A published board: one document, read whole, drawn as a list.
    ///
    /// <para>
    /// <b>Denormalised on purpose, and it is the difference between a feature that scales and
    /// one that does not.</b> The obvious shape is a query — order the player collection by
    /// score, take the first hundred — which is a hundred document reads every time anybody
    /// opens the screen, against a collection that grows for the life of the game, on a
    /// database billed per read. One document holding a hundred rows is one read, cacheable,
    /// the same for everybody, and its cost does not move when the game does. It is the same
    /// trade <c>config/stats</c> already makes, and the reason a scheduled job writes it.
    /// </para>
    /// <para>
    /// <b>There are exactly two kinds and no third.</b> <see cref="Global"/> is aspirational —
    /// the best groves in the world, which almost nobody is on and everybody wants to see —
    /// and a league board is reachable, because a league is the star rating the player already
    /// wears (see <see cref="GroveLeague"/>). What is deliberately absent is a "keepers near
    /// you" board: that needs an exact global ordering, which is the one thing this design
    /// refuses to maintain, and <see cref="GroveRanks"/> answers "where do I stand" to within
    /// a percentage point without it.
    /// </para>
    /// </summary>
    public sealed class LeaderboardBoard
    {
        /// <summary>
        /// The board holding the best groves anywhere. A permanent id: it names a document
        /// the server writes, so invariant 1 applies to it in full.
        /// </summary>
        public const string Global = "global";

        /// <summary>
        /// How many rows a board carries.
        ///
        /// <para>
        /// A hundred, and the number is a cost decision rather than a taste one: a row is
        /// about eighty bytes, so a board is a few kilobytes and stays inside the document
        /// limit with room for a schema that grows. Longer boards do not motivate anybody —
        /// nobody has ever been moved by being four hundred and twelfth — and the percentile
        /// covers everyone the list cannot.
        /// </para>
        /// </summary>
        public const int MaxRows = 100;

        public readonly string BoardId;

        /// <summary>Rows, best first. Never null; empty is the ordinary first-day state.</summary>
        public readonly IReadOnlyList<LeaderboardEntry> Entries;

        /// <summary>When the job that produced this ran. 0 if unknown.</summary>
        public readonly long BuiltUnix;

        /// <summary>
        /// How many keepers this board was chosen from, which is not the same as
        /// <see cref="Entries"/>'s length and is the more honest number to print: "top 100 of
        /// 214,000" says something a bare list does not.
        /// </summary>
        public readonly int Population;

        public LeaderboardBoard(string boardId, IReadOnlyList<LeaderboardEntry> entries,
                                long builtUnix, int population)
        {
            BoardId = boardId ?? string.Empty;
            Entries = entries ?? Array.Empty<LeaderboardEntry>();
            BuiltUnix = builtUnix < 0L ? 0L : builtUnix;
            Population = population < 0 ? 0 : population;
        }

        public static readonly LeaderboardBoard None =
            new LeaderboardBoard(string.Empty, null, 0L, 0);

        public bool IsEmpty => Entries.Count == 0;

        /// <summary>The board id for a league. See <see cref="GroveLeague"/> for the ids.</summary>
        public static string IdFor(string leagueId)
            => GroveLeague.IsKnown(leagueId) ? leagueId : GroveLeague.IdFor(0);

        /// <summary>
        /// Whether a board id is one this build knows how to ask for.
        ///
        /// Checked before the request rather than after the answer, because an unknown id is
        /// a path this client composed and a request for a document that cannot exist is a
        /// read nobody should pay for.
        /// </summary>
        public static bool IsKnown(string boardId)
            => string.Equals(boardId, Global, StringComparison.Ordinal) || GroveLeague.IsKnown(boardId);

        /// <summary>
        /// Where this account sits on this board, or 0 when it is not on it.
        ///
        /// Not on the board is by far the commonest answer and is not a failure — it is what
        /// every player outside the top hundred gets, and it is why the screen leads with the
        /// percentile rather than with a position.
        /// </summary>
        public int RankOf(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId)) return 0;

            for (int i = 0; i < Entries.Count; i++)
                if (string.Equals(Entries[i].OwnerId, ownerId, StringComparison.Ordinal))
                    return Entries[i].Rank;

            return 0;
        }
    }
}
