using GlimmerGrove.Content;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// What a player has achieved on one level.
    ///
    /// Immutable: improving a record produces a new one. That removes any chance of a
    /// half-updated record being written, and makes the "did this run actually beat
    /// the old one" question a pure comparison rather than a sequence of mutations.
    /// </summary>
    public sealed class LevelRecord
    {
        /// <summary>
        /// Three stars is the ceiling everywhere: the board cannot award more, the
        /// save clamps to it, and both the client and the server clamp again when
        /// deriving currency, because a forged record is the one place a fourth star
        /// could come from.
        /// </summary>
        public const int MaxStars = 3;

        public readonly LevelId Id;
        public readonly int Stars;
        public readonly int BestMoves;
        public readonly int Clears;
        public readonly long FirstClearedUnix;
        public readonly long LastPlayedUnix;

        /// <summary>
        /// <b>Retired.</b> The best standing this glade ever held against the published
        /// population, as percent-of-keepers-slower; zero means never ranked.
        ///
        /// <para>
        /// The standing badge and the job that published the population
        /// (<c>publishGroveStats</c>) were removed on 2026-10-07: the badge would sit on the next
        /// node on the tighter maps. Nothing produces or reads a new value. The field stays for
        /// <see cref="BestMillis"/>' reason - it is on the wire in both directions of
        /// <c>FirestoreSaveMapper</c>, every shipped build writes it, and a reader that dropped
        /// it would leave every device owing a sync against the cloud's copy (invariant 11f:
        /// retired in place means still written). It is still clamped on read and still merged
        /// by <c>max</c>, so whatever a player already holds survives untouched.
        /// </para>
        /// </summary>
        public readonly int BestRank;

        /// <summary>
        /// <b>Retired.</b> The fastest clear in milliseconds, from back when a glade was
        /// played against a clock.
        ///
        /// <para>
        /// Nothing produces a new value: the countdown was removed, so a run is graded and
        /// recorded on turns alone (<see cref="Content.LevelTuning.StarsFor"/>). The field
        /// stays because deleting it would be the one change to a save field that is not free
        /// - it is on the wire in both directions of <c>FirestoreSaveMapper</c>, and a client
        /// that still writes a key the reader has forgotten is how a rollback loses data
        /// rather than a field. Keeping it costs one int per cleared glade and keeps every
        /// device, deployed or rolled back, agreeing about the document's shape (invariant
        /// 12a, and the same call invariant 16h made for <c>homesteadOwned</c>).
        /// </para>
        /// <para>
        /// It is still merged - smaller wins, zero is absent - so times already earned survive
        /// a sync and a reinstall rather than being quietly dropped by the build that stopped
        /// measuring them. Nothing reads it: the record shown on a map node is a move count.
        /// </para>
        /// </summary>
        public readonly int BestMillis;

        public LevelRecord(LevelId id, int stars, int bestMoves, int clears,
                           long firstClearedUnix, long lastPlayedUnix, int bestRank = 0,
                           int bestMillis = 0)
        {
            Id = id;
            Stars = stars;
            BestMoves = bestMoves;
            Clears = clears;
            FirstClearedUnix = firstClearedUnix;
            LastPlayedUnix = lastPlayedUnix;
            BestRank = bestRank;
            BestMillis = bestMillis;
        }

        public static LevelRecord Empty(LevelId id) => new LevelRecord(id, 0, 0, 0, 0, 0);

        public bool IsCleared => Stars > 0;

        /// <summary>
        /// Folds a finished run into this record, keeping the best of each measure.
        /// Stars and moves are tracked independently because a player can beat their
        /// star rating on one run and their move count on another.
        /// </summary>
        public LevelRecord WithRun(int stars, int moves, long nowUnix)
            => WithRun(stars, moves, nowUnix, false);

        /// <summary>
        /// The same fold on a level graded on a count that <em>climbs</em> rather than falls.
        ///
        /// <para>
        /// <b>An endless run is the only thing in this game where a bigger count is a better
        /// one</b>, so <c>bestMoves</c> keeps the larger rather than the smaller. Everything else
        /// about the fold is unchanged, which is the point: an endless level's record is an
        /// ordinary record, so its stars, its clears, its merge and its rewards are the ones every
        /// glade already has (invariant 20a).
        /// </para>
        /// </summary>
        public LevelRecord WithRun(int stars, int moves, long nowUnix, bool climbs)
        {
            int bestStars = stars > Stars ? stars : Stars;
            int bestMoves = climbs
                ? (moves > BestMoves ? moves : BestMoves)
                : (BestMoves == 0 || (moves > 0 && moves < BestMoves) ? moves : BestMoves);

            long firstCleared = FirstClearedUnix == 0 && stars > 0 ? nowUnix : FirstClearedUnix;

            return new LevelRecord(Id, bestStars, bestMoves, Clears + 1, firstCleared, nowUnix,
                                   BestRank, BestMillis);
        }

        public bool Improves(int stars, int moves) => Improves(stars, moves, false);

        /// <summary>Whether this run beat what is stored, under this level's own direction.</summary>
        public bool Improves(int stars, int moves, bool climbs)
            => climbs
             ? stars > Stars || moves > BestMoves
             : stars > Stars || BestMoves == 0 || (moves > 0 && moves < BestMoves);

        public LevelRecordDto ToDto() => new LevelRecordDto
        {
            levelId = Id.Value,
            stars = Stars,
            bestMoves = BestMoves,
            clears = Clears,
            firstClearedUnix = FirstClearedUnix,
            lastPlayedUnix = LastPlayedUnix,
            bestRank = BestRank,
            bestMillis = BestMillis,
        };

        public static bool TryFromDto(LevelRecordDto dto, out LevelRecord record)
        {
            record = null;
            if (dto == null) return false;
            if (!LevelId.TryParse(dto.levelId, out var id, out _)) return false;

            record = new LevelRecord(id,
                                     Clamp(dto.stars, 0, MaxStars),
                                     dto.bestMoves < 0 ? 0 : dto.bestMoves,
                                     dto.clears < 0 ? 0 : dto.clears,
                                     dto.firstClearedUnix,
                                     dto.lastPlayedUnix,
                                     // Clamped to what the retired producer could emit.
                                     Clamp(dto.bestRank, 0, MaxRetiredRank),
                                     dto.bestMillis < 0 ? 0 : dto.bestMillis);
            return true;
        }

        /// <summary>
        /// The highest standing the retired population table could ever report. Kept so a
        /// forged file reads the same as it did while the producer existed.
        /// </summary>
        const int MaxRetiredRank = 95;

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
