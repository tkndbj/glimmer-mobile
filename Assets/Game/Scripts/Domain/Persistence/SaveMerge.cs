using System;
using System.Collections.Generic;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// Joins two save files into one that loses nothing from either.
    ///
    /// This is the single most important piece of the cloud sync, and the reason the
    /// game never asks a player to choose between their local save and their cloud
    /// save. That prompt looks like consent and behaves like data loss: whichever
    /// button is pressed, half of somebody's progress is deleted, and the half that
    /// survives is chosen by a person who cannot see what is in either.
    ///
    /// It can be avoided because the interesting state is monotonic. Stars only rise,
    /// best moves only fall, clears only accumulate, high-water marks only ratchet.
    /// For values like those the merge is a <em>join</em>: applying it twice changes
    /// nothing, and merging A into B gives the same answer as merging B into A, so it
    /// does not matter how many devices sync in what order or how often.
    ///
    /// Preferences are the exception. Muting the music is not monotonic — it is an
    /// instruction, and the most recent one is the one the player meant — so settings
    /// and the display name take the newer file's value. That is last-writer-wins
    /// applied only where losing the other value is what the player asked for.
    /// </summary>
    public static class SaveMerge
    {
        /// <summary>
        /// Merges <paramref name="other"/> into <paramref name="mine"/>, without
        /// modifying either. Ties on recency resolve to <paramref name="mine"/>.
        /// </summary>
        public static SaveFileDto Join(SaveFileDto mine, SaveFileDto other)
        {
            if (mine == null && other == null) return null;
            if (other == null) return mine;
            if (mine == null) return other;

            // Only preferences use this; progress and currency are joined on value.
            bool otherIsNewer = other.updatedUnix > mine.updatedUnix;
            var newer = otherIsNewer ? other : mine;

            var merged = new SaveFileDto
            {
                schemaVersion = Math.Max(mine.schemaVersion, other.schemaVersion),
                updatedUnix = Math.Max(mine.updatedUnix, other.updatedUnix),

                settings = newer.settings ?? mine.settings ?? other.settings,
                levels = JoinLevels(mine.levels, other.levels),
                wallet = JoinWallets(mine.wallet, other.wallet, newer.wallet),
                progression = JoinProgression(mine.progression, other.progression),
                cloud = JoinCloud(mine.cloud, other.cloud),

                lastPlayedLevelId = string.IsNullOrEmpty(newer.lastPlayedLevelId)
                    ? (mine.lastPlayedLevelId ?? other.lastPlayedLevelId)
                    : newer.lastPlayedLevelId,

                // Having run anywhere means it has run. Re-importing would fold the
                // same pre-1.0 stars in a second time.
                legacyImportDone = mine.legacyImportDone || other.legacyImportDone,

                // A union: seeing a lesson cannot be undone, so between two devices the
                // player has seen whatever either of them showed.
                tipsSeen = TipLedger.Join(mine.tipsSeen, other.tipsSeen),

                // The later day outright, and the larger counts within a shared day. The
                // rule lives with the feature rather than here, for the reason Hearts.Join
                // does: a merge that holds its own copy of a rule stops agreeing with the
                // game that enforces it.
                daily = Daily.DailyChests.Join(mine.daily, other.daily),
            };

            return merged;
        }

        // -------------------------------------------------------------- levels
        /// <summary>
        /// Per level, the best of each measure independently — the same rule
        /// <see cref="LevelRecord.WithRun"/> applies to a run, for the same reason: a
        /// player can beat their star rating on one device and their move count on
        /// another, and both results are real.
        /// </summary>
        static LevelRecordDto[] JoinLevels(LevelRecordDto[] mine, LevelRecordDto[] other)
        {
            var byId = new Dictionary<string, LevelRecordDto>(StringComparer.Ordinal);

            Absorb(byId, mine);
            Absorb(byId, other);

            var result = new LevelRecordDto[byId.Count];
            byId.Values.CopyTo(result, 0);
            return result;
        }

        static void Absorb(Dictionary<string, LevelRecordDto> byId, LevelRecordDto[] records)
        {
            if (records == null) return;

            foreach (var record in records)
            {
                if (record == null || string.IsNullOrEmpty(record.levelId)) continue;

                if (!byId.TryGetValue(record.levelId, out var existing))
                {
                    byId[record.levelId] = Copy(record);
                    continue;
                }

                byId[record.levelId] = new LevelRecordDto
                {
                    levelId = record.levelId,
                    stars = Math.Max(existing.stars, record.stars),
                    bestMoves = BetterMoves(existing.bestMoves, record.bestMoves),
                    clears = Math.Max(existing.clears, record.clears),
                    firstClearedUnix = Earliest(existing.firstClearedUnix, record.firstClearedUnix),
                    lastPlayedUnix = Math.Max(existing.lastPlayedUnix, record.lastPlayedUnix),
                };
            }
        }

        /// <summary>Fewer moves is better, but zero means "never cleared", not "perfect".</summary>
        static int BetterMoves(int a, int b)
        {
            if (a <= 0) return b;
            if (b <= 0) return a;
            return Math.Min(a, b);
        }

        /// <summary>Earliest real timestamp; zero means it never happened.</summary>
        static long Earliest(long a, long b)
        {
            if (a <= 0) return b;
            if (b <= 0) return a;
            return Math.Min(a, b);
        }

        static LevelRecordDto Copy(LevelRecordDto r) => new LevelRecordDto
        {
            levelId = r.levelId,
            stars = r.stars,
            bestMoves = r.bestMoves,
            clears = r.clears,
            firstClearedUnix = r.firstClearedUnix,
            lastPlayedUnix = r.lastPlayedUnix,
        };

        // -------------------------------------------------------------- wallet
        static WalletDto JoinWallets(WalletDto mine, WalletDto other, WalletDto newer)
        {
            mine ??= WalletDto.Unwritten();
            other ??= WalletDto.Unwritten();
            newer ??= mine;

            var ledgers = new Dictionary<string, CurrencyLedger>(StringComparer.Ordinal);
            AbsorbLedgers(ledgers, mine.currencies);
            AbsorbLedgers(ledgers, other.currencies);

            var currencies = new CurrencyLedgerDto[ledgers.Count];
            int i = 0;
            foreach (var ledger in ledgers.Values) currencies[i++] = ledger.ToDto();

            var hearts = JoinedHearts(mine, other);

            return new WalletDto
            {
                // Retired v1 fields. Whichever file is newer holds the better mirror.
                coins = newer.coins,
                gems = newer.gems,

                // Hearts are consumable, so the smaller count is the honest one: taking
                // the larger would let two devices refill each other indefinitely. The
                // refill deadline travels with the count and joins the same way — see
                // Hearts.Join, which owns the rule so the merge and the game cannot
                // disagree about it.
                hearts = hearts.Count,
                heartsNextRefillUnix = hearts.NextRefillUnix,

                // The later deadline, where the count above takes the smaller. A boost
                // cannot be minted by two devices refilling each other — it comes from a
                // chest whose award is already deduplicated by its own derived id — so
                // the conservative rule would only ever cut short a boost the player has.
                heartBoostUntilUnix = Hearts.JoinBoost(mine.heartBoostUntilUnix,
                                                       other.heartBoostUntilUnix),

                // Preferences: the newest real value, and never an empty one over a
                // real one. A device that has never been renamed carries "", and
                // letting that win would silently undo the rename done on the other.
                displayName = NewestSet(newer.displayName, mine.displayName, other.displayName),
                avatarId = NewestSet(newer.avatarId, mine.avatarId, other.avatarId),

                currencies = currencies,
            };
        }

        static void AbsorbLedgers(Dictionary<string, CurrencyLedger> into, CurrencyLedgerDto[] dtos)
        {
            if (dtos == null) return;

            foreach (var dto in dtos)
            {
                var ledger = CurrencyLedger.FromDto(dto);
                if (ledger == null) continue;

                if (into.TryGetValue(ledger.Currency, out var existing)) existing.MergeFrom(ledger);
                else into[ledger.Currency] = ledger;
            }
        }

        /// <summary>
        /// The newer file's value when it has one, otherwise whichever file actually
        /// holds a value.
        ///
        /// Order-independent despite reading like a preference: whenever
        /// <paramref name="newer"/> is empty the only non-empty candidate left is the
        /// older file, and it is the same file whichever way round the two are passed.
        /// </summary>
        static string NewestSet(string newer, string mine, string other)
        {
            if (!string.IsNullOrEmpty(newer)) return newer;
            if (!string.IsNullOrEmpty(mine)) return mine;
            return other;
        }

        /// <summary>
        /// Joins two devices' hearts, honouring "never written" on either side.
        ///
        /// A -1 count means that file predates hearts being stored at all, so it holds
        /// no opinion and the other side stands. Once both are real values the rule
        /// lives in <see cref="Hearts.Join"/> — kept there rather than here so the
        /// merge cannot drift from what the game believes a heart is.
        /// </summary>
        static Hearts JoinedHearts(WalletDto mine, WalletDto other)
        {
            if (mine.hearts < 0 && other.hearts < 0) return Hearts.Full;
            if (mine.hearts < 0) return new Hearts(other.hearts, other.heartsNextRefillUnix);
            if (other.hearts < 0) return new Hearts(mine.hearts, mine.heartsNextRefillUnix);

            return Hearts.Join(new Hearts(mine.hearts, mine.heartsNextRefillUnix),
                               new Hearts(other.hearts, other.heartsNextRefillUnix));
        }

        // --------------------------------------------------------- progression
        static ProgressionStateDto JoinProgression(ProgressionStateDto mine, ProgressionStateDto other)
        {
            mine ??= ProgressionStateDto.Unwritten();
            other ??= ProgressionStateDto.Unwritten();

            return new ProgressionStateDto
            {
                xpHighWater = Math.Max(mine.xpHighWater, other.xpHighWater),
                levelHighWater = Math.Max(mine.levelHighWater, other.levelHighWater),
            };
        }

        // --------------------------------------------------------------- cloud
        static CloudStateDto JoinCloud(CloudStateDto mine, CloudStateDto other)
        {
            mine ??= new CloudStateDto();
            other ??= new CloudStateDto();

            return new CloudStateDto
            {
                userId = string.IsNullOrEmpty(mine.userId) ? other.userId : mine.userId,

                // Ahead of both, so the merged file is unambiguously the newer one and
                // a backend using revision for optimistic concurrency accepts it.
                revision = Math.Max(mine.revision, other.revision) + 1,

                lastSyncedUnix = Math.Max(mine.lastSyncedUnix, other.lastSyncedUnix),

                // The device writing the merge keeps its own id, not the other's.
                deviceId = string.IsNullOrEmpty(mine.deviceId) ? other.deviceId : mine.deviceId,
            };
        }
    }
}
