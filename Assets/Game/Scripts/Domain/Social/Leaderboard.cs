using System;
using System.Collections.Generic;

namespace GlimmerGrove.Social
{
    /// <summary>One row of a board: who, what they are ranked on, and where they placed.</summary>
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

        /// <summary>
        /// The rank this keeper holds, as a rung id — the badge the row draws. Empty for a
        /// keeper below the first rung, and for every row a server wrote before it derived one.
        ///
        /// <para>
        /// <b>Called a rung and not a rank, deliberately.</b> <see cref="Rank"/> on this very
        /// struct is the row's <em>position</em>, and invariant 52g is the note that two things
        /// in this game are called a rank; one field away from the other is the last place that
        /// ambiguity should be allowed to live. The wire spelling is <c>rung</c> for the same
        /// reason.
        /// </para>
        /// </summary>
        public readonly string RungId;

        /// <summary>
        /// The furthest wave this keeper has held out to on the Infinite lane, or nought.
        ///
        /// <para>
        /// <b>One row type for both boards rather than two, because a row is the same row.</b> A
        /// board decides which figure it is <em>ordered</em> on and which one it draws; the person,
        /// their companion and their keeper level are the same facts either way, and they come out
        /// of the same published card. Two entry types would be two readers, two row widgets and
        /// two places for the portrait fallback to be got wrong.
        /// </para>
        /// </summary>
        public readonly int Wave;

        public LeaderboardEntry(int rank, string ownerId, string name, string avatarId,
                                int keeperLevel, long score, int stars, int wave = 0,
                                string rungId = null)
        {
            RungId = rungId ?? string.Empty;
            Rank = rank < 1 ? 1 : rank;
            OwnerId = ownerId ?? string.Empty;
            Name = name ?? string.Empty;
            AvatarId = avatarId ?? string.Empty;
            KeeperLevel = keeperLevel < 1 ? 1 : keeperLevel;
            Score = score < 0L ? 0L : score;
            Stars = stars < 0 ? 0 : stars;
            Wave = wave < 0 ? 0 : wave;
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
    /// <b>There are exactly two boards and they are the game's two ladders.</b>
    /// <see cref="Global"/> is the finest groves anywhere — what a keeper has built — and
    /// <see cref="Endless"/> is the Infinite lane's own board: how far anybody has held the
    /// line (invariant 43). Both are one document a day, both are exact at any player count,
    /// and neither costs a read that grows with the game.
    /// </para>
    /// <para>
    /// <b>What was here and is gone is the league board.</b> Nine boards, nine queries and nine
    /// counts a night bought a second reading of the <em>same</em> number the global board is
    /// ordered on, cut into bands no screen in the game ever named — and the question it existed
    /// to answer, "where do I stand", is already answered exactly, at O(1) and at any
    /// population, by the published distribution (<see cref="GroveRanks"/>, invariant 19c). The
    /// ids <c>l0</c> to <c>l8</c> are spent and must never be reused.
    /// </para>
    /// <para>
    /// What is still deliberately absent is a "keepers near you" board: that needs an exact
    /// global ordering, which is the one thing this design refuses to maintain.
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
        /// The Infinite lane's board: the furthest wave anybody has held out to.
        ///
        /// <para>
        /// A permanent id, exactly as <see cref="Global"/> is — it names a document a scheduled
        /// job writes, so renaming it orphans whatever the last run left behind and shows every
        /// player an empty list until the next one.
        /// </para>
        /// <para>
        /// <b>It is the lane's board rather than a level's</b>, which is what keeps a second
        /// Infinite level a content decision instead of a new board id (invariant 43, and
        /// <c>EndlessLedger.Best</c> for the same argument from the save's end).
        /// </para>
        /// </summary>
        public const string Endless = "endless";

        /// <summary>
        /// Every board this build knows how to ask for, in the order the screen offers them.
        ///
        /// Written out rather than composed: these key documents a server writes, and invariant
        /// 6's argument about loc keys is the same argument — a string built by concatenation is
        /// one no search can find and no gate can check. Mirrored by <c>BOARD_IDS</c> in
        /// <c>functions/src/grove.ts</c>, which is what decides the boards that actually exist.
        /// </summary>
        public static readonly IReadOnlyList<string> All = new[] { Global, Endless };

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

        /// <summary>
        /// How often the boards are re-read whole, in minutes.
        ///
        /// <para>
        /// <b>The boards are live</b>: a keeper's row is placed by the same server call that
        /// publishes their card, so their own run is on the list the moment the sync that
        /// carried it settles. What this number describes is the net under that — the job that
        /// re-reads the top hundred off the cards and repairs anything the live placement
        /// missed. It is what the panel says when it explains why a list might lag.
        /// </para>
        /// <para>
        /// <b>A mirror of <c>publishGroveBoards</c>' own schedule</b> — <c>"*/15 * * * *"</c> in
        /// <c>functions/src/index.ts</c> — and the only copy of it on this side. It is a
        /// <em>sentence's</em> number rather than a rule's: nothing here waits on it, caches
        /// against it or refuses anything because of it, so the worst a drift costs is a panel
        /// that over- or under-states the wait. That is the same bargain <c>EndlessLedger.MaxWave</c>
        /// strikes with <c>MAX_WAVE</c>, and it is affordable for the same reason — the two are
        /// one idea with one place to change it on each side.
        /// </para>
        /// <para>
        /// The panel that prints it also prints <see cref="BuiltUnix"/>, which is not a mirror
        /// at all: it is what the job that wrote this board actually recorded, so the honest
        /// half of the sentence cannot go stale however this constant drifts.
        /// </para>
        /// </summary>
        public const int RebuildMinutes = 15;

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

        /// <summary>
        /// Whether a board id is one this build knows how to ask for.
        ///
        /// Checked before the request rather than after the answer, because an unknown id is
        /// a path this client composed and a request for a document that cannot exist is a
        /// read nobody should pay for. It is also what makes a retired board id — a league's,
        /// say, reached through an old deep link — refused rather than merely empty.
        /// </summary>
        public static bool IsKnown(string boardId)
            => string.Equals(boardId, Global, StringComparison.Ordinal)
            || string.Equals(boardId, Endless, StringComparison.Ordinal);

        /// <summary>
        /// Whether this board is ordered on waves rather than on grove worth.
        ///
        /// <para>
        /// Asked of the board rather than carried on a row, because it decides what a row
        /// <em>says</em> and every row on one board says the same thing. A flag per entry would
        /// be the same bit written a hundred times and a hundred chances for one row to disagree
        /// with the list it is in.
        /// </para>
        /// </summary>
        public static bool IsEndless(string boardId)
            => string.Equals(boardId, Endless, StringComparison.Ordinal);

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
