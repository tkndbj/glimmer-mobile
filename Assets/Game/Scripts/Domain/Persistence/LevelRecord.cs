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
        public readonly LevelId Id;
        public readonly int Stars;
        public readonly int BestMoves;
        public readonly int Clears;
        public readonly long FirstClearedUnix;
        public readonly long LastPlayedUnix;

        public LevelRecord(LevelId id, int stars, int bestMoves, int clears,
                           long firstClearedUnix, long lastPlayedUnix)
        {
            Id = id;
            Stars = stars;
            BestMoves = bestMoves;
            Clears = clears;
            FirstClearedUnix = firstClearedUnix;
            LastPlayedUnix = lastPlayedUnix;
        }

        public static LevelRecord Empty(LevelId id) => new LevelRecord(id, 0, 0, 0, 0, 0);

        public bool IsCleared => Stars > 0;

        /// <summary>
        /// Folds a finished run into this record, keeping the best of each measure.
        /// Stars and moves are tracked independently because a player can beat their
        /// star rating on one run and their move count on another.
        /// </summary>
        public LevelRecord WithRun(int stars, int moves, long nowUnix)
        {
            int bestStars = stars > Stars ? stars : Stars;
            int bestMoves = BestMoves == 0 || (moves > 0 && moves < BestMoves) ? moves : BestMoves;
            long firstCleared = FirstClearedUnix == 0 && stars > 0 ? nowUnix : FirstClearedUnix;

            return new LevelRecord(Id, bestStars, bestMoves, Clears + 1, firstCleared, nowUnix);
        }

        public bool Improves(int stars, int moves)
            => stars > Stars || BestMoves == 0 || (moves > 0 && moves < BestMoves);

        public LevelRecordDto ToDto() => new LevelRecordDto
        {
            levelId = Id.Value,
            stars = Stars,
            bestMoves = BestMoves,
            clears = Clears,
            firstClearedUnix = FirstClearedUnix,
            lastPlayedUnix = LastPlayedUnix,
        };

        public static bool TryFromDto(LevelRecordDto dto, out LevelRecord record)
        {
            record = null;
            if (dto == null) return false;
            if (!LevelId.TryParse(dto.levelId, out var id, out _)) return false;

            record = new LevelRecord(id,
                                     Clamp(dto.stars, 0, 3),
                                     dto.bestMoves < 0 ? 0 : dto.bestMoves,
                                     dto.clears < 0 ? 0 : dto.clears,
                                     dto.firstClearedUnix,
                                     dto.lastPlayedUnix);
            return true;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
