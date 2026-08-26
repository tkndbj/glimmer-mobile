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
        /// The best standing this glade has ever held against the published population,
        /// as percent-of-keepers-slower. Zero means never ranked.
        ///
        /// <para>
        /// <b>Only ever promoted</b>, which is the whole design — see
        /// <see cref="Promote"/>. It is the one thing in this record that is not a fact
        /// about the player's own play but about a population, and a population moves. The
        /// merge is therefore <c>max</c> like every other mergeable number in this file,
        /// and zero is unreachable for a real standing
        /// (<see cref="Social.LevelStats.MinRank"/>), so a v12 file reads as unranked
        /// rather than as badly ranked and no migration is needed.
        /// </para>
        /// <para>
        /// It buys nothing. A forged value wears a band on a map node and pays no
        /// currency, which is what makes it safe to store client-side at all — the same
        /// test invariant 15 applies to a companion entitlement.
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
        /// — it is on the wire in both directions of <c>FirestoreSaveMapper</c>, and a client
        /// that still writes a key the reader has forgotten is how a rollback loses data
        /// rather than a field. Keeping it costs one int per cleared glade and keeps every
        /// device, deployed or rolled back, agreeing about the document's shape (invariant
        /// 12a, and the same call invariant 16h made for <c>homesteadOwned</c>).
        /// </para>
        /// <para>
        /// It is still merged — smaller wins, zero is absent — so times already earned survive
        /// a sync and a reinstall rather than being quietly dropped by the build that stopped
        /// measuring them. Nothing reads it: the record shown on a map node and the population
        /// a player is ranked against are both move counts.
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
            => WithRun(stars, moves, nowUnix, Social.LevelStats.None);

        /// <summary>
        /// The same fold, also ranking the result against the published population.
        ///
        /// <para>
        /// An overload rather than an optional parameter, for the reason
        /// <c>TrySpendHeart</c> is one: a default argument is baked into every calling
        /// assembly at compile time, and <see cref="Social.LevelStats.None"/> is not a
        /// compile-time constant anyway. Every existing call site keeps the three argument
        /// form and keeps its standing untouched.
        /// </para>
        /// <para>
        /// Note the order: the run is folded <em>first</em> and the standing is taken over
        /// the new <c>bestMoves</c>, never over this run's move count. A replay that came
        /// nowhere near the record would otherwise be ranked on its own merits and — since
        /// a standing only rises — simply achieve nothing, quietly, on the one path that
        /// exists to capture it. Doing it inside the transform is what stops a call site
        /// getting that order wrong.
        /// </para>
        /// </summary>
        public LevelRecord WithRun(int stars, int moves, long nowUnix, Social.LevelStats population)
        {
            int bestStars = stars > Stars ? stars : Stars;
            int bestMoves = BestMoves == 0 || (moves > 0 && moves < BestMoves) ? moves : BestMoves;
            long firstCleared = FirstClearedUnix == 0 && stars > 0 ? nowUnix : FirstClearedUnix;

            return new LevelRecord(Id, bestStars, bestMoves, Clears + 1, firstCleared, nowUnix,
                                   Promote(BestRank, bestMoves, population), BestMillis);
        }

        /// <summary>
        /// Re-ranks the standing record against a freshly published population, without
        /// touching anything else.
        ///
        /// <para>
        /// This is what backfills a save written before the field existed, and what rescues
        /// the player who cleared a glade before the day's table had arrived: the move count
        /// was stored either way, so the standing can be worked out later from what is
        /// already on disk. Returns <c>this</c> when nothing improved, so a caller sweeping
        /// thousands of records can decide whether the file is worth rewriting with a
        /// reference comparison.
        /// </para>
        /// </summary>
        public LevelRecord WithRank(Social.LevelStats population)
        {
            int promoted = Promote(BestRank, BestMoves, population);
            return promoted == BestRank
                ? this
                : new LevelRecord(Id, Stars, BestMoves, Clears, FirstClearedUnix, LastPlayedUnix,
                                  promoted, BestMillis);
        }

        /// <summary>
        /// The one place a standing changes, and it only ever climbs.
        ///
        /// <para>
        /// Every failure mode collapses to "keep what we had": an uncleared glade, a table
        /// too thin to speak from, a population that has since got faster.
        /// <see cref="Social.LevelStats.PercentSlower"/> answers -1 in the middle case,
        /// which loses the comparison against a held zero exactly as it should.
        /// </para>
        /// </summary>
        static int Promote(int held, int bestMoves, Social.LevelStats population)
        {
            if (bestMoves <= 0) return held;

            int now = population.PercentSlower(bestMoves);
            return now > held ? now : held;
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
                                     // Clamped to what the producer can actually emit, so a
                                     // hand-edited file cannot wear a band above the ladder.
                                     Clamp(dto.bestRank, 0, Social.LevelStats.MaxRank),
                                     dto.bestMillis < 0 ? 0 : dto.bestMillis);
            return true;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
