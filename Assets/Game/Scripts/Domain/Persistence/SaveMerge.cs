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

                // Same shape, opposite instinct within a shared day: an ad allowance is
                // consumable, so the larger count wins and two devices cannot refill each
                // other by taking turns. The rule lives with the feature for the reason
                // the one above it does.
                ads = Ads.RewardedAds.Join(mine.ads, other.ads),

                // Two dates, both taken at their larger value, and the length derived from
                // the pair rather than stored — which is the only reason a streak is
                // mergeable at all. See invariant 11b and DailyStreak, which owns the rule
                // for the reason the two above it do.
                streak = Daily.DailyStreak.Join(mine.streak, other.streak),

                // A floor per event, unioned by id and taken at its larger value — the
                // fourth thing in this file shaped that way, and the rule lives with the
                // feature for the reason the three above it do.
                events = Events.EventCollection.Join(mine.events, other.events),

                // Having been through a build that collects by hand cannot be undone, so
                // the union is "either". A device still on the old build joins as false and
                // takes the true from the other side, which is right: the floors it inherits
                // came from a pass that has already run.
                eventsSeeded = mine.eventsSeeded || other.eventsSeeded,

                // A union, for the reason tipsSeen is one: buying cannot be undone, so
                // between two devices the player owns whatever either of them bought. The
                // rule lives with the feature for the reason the four above it do.
                companionsOwned = Progression.CompanionLedger.Join(mine.companionsOwned,
                                                                   other.companionsOwned),

                // The grove's two halves, and they are joined differently on purpose. What
                // was bought is a union, for the reason directly above. Where things stand
                // is the only part of this file merged by *recency* other than the keeper's
                // name and their worn companion — an arrangement is an instruction rather
                // than an achievement, so the most recent one is the one the player meant.
                // Invariant 11c is what keeps that from losing a grove: the stamp travels
                // per slot rather than being read off this file's updatedUnix, and an
                // untouched slot writes nothing at all.
                homesteadOwned = Homestead.HomesteadLedger.Join(mine.homesteadOwned,
                                                                other.homesteadOwned),
                homesteadPlaced = Homestead.HomesteadLayout.Join(mine.homesteadPlaced,
                                                                 other.homesteadPlaced),
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

                    // Larger wins, and it has to: a standing is measured against a
                    // population that moves, so two devices that ranked the same move count
                    // months apart hold different honest answers. The larger is the one that
                    // knows the player at their best, and it is the only rule that makes
                    // this a join — see invariant 11b. Absent reads as zero, which no real
                    // standing can be, so a device on an older build contributes nothing
                    // rather than clearing a band the other device earned.
                    bestRank = Math.Max(existing.bestRank, record.bestRank),

                    // Smaller wins, exactly like the move count and for the same reason: a
                    // best only ever falls, so both sides hold a real achievement and the
                    // lower one is the better. Zero is absent rather than instant — see
                    // RunClock — so a device that never timed a glade cannot beat one that did.
                    bestMillis = RunClock.Better(existing.bestMillis, record.bestMillis),
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
            bestRank = r.bestRank,
            bestMillis = r.bestMillis,
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

            var name = Chosen(mine.displayName, mine.displayNameSetUnix,
                              other.displayName, other.displayNameSetUnix);

            var avatar = Chosen(mine.avatarId, mine.avatarSetUnix,
                                other.avatarId, other.avatarSetUnix);

            return new WalletDto
            {
                // Retired v1 fields. Whichever file is newer holds the better mirror.
                coins = newer.coins,
                gems = newer.gems,

                // Hearts join as a ledger — everything produced, everything spent, both
                // taken at their larger value — so a grant and a spend both survive and
                // neither device can mint. Owned by Hearts.Join so the merge and the game
                // cannot disagree about what a heart is.
                //
                // This is where hearts used to be destroyed. The old rule joined a stored
                // count by taking the smaller, which is only defensible if the two sides
                // are concurrent peers; a sync is pull, join, push, so the usual case is a
                // stale snapshot winning against a device that had just been paid a
                // refill, and then that loss being pushed back to the server.
                heartsProduced = hearts.Produced,
                heartsSpent = hearts.Spent,
                heartsDueUnix = hearts.DueUnix,

                hearts = hearts.Count,
                heartsNextRefillUnix = hearts.NextRefillUnix,

                // The later deadline, where the count above takes the smaller. A boost
                // cannot be minted by two devices refilling each other — it comes from a
                // chest whose award is already deduplicated by its own derived id — so
                // the conservative rule would only ever cut short a boost the player has.
                heartBoostUntilUnix = Hearts.JoinBoost(mine.heartBoostUntilUnix,
                                                       other.heartBoostUntilUnix),

                // Preferences: the value chosen most recently, dated by its own stamp
                // rather than by the file's. See Chosen for why the file's date was the
                // wrong clock and what it cost.
                displayName = name.Value,
                displayNameSetUnix = name.At,
                avatarId = avatar.Value,
                avatarSetUnix = avatar.At,

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
        /// Picks between two preferences and keeps the date of the one it picked.
        ///
        /// <para>
        /// This is the only last-writer-wins rule in the file, and it needs a clock that
        /// describes the <em>value</em>. It used to use the file's own
        /// <c>updatedUnix</c>, which reads correctly and is wrong in practice:
        /// <see cref="SaveService.Snapshot"/> stamps it with the current moment, and the
        /// cloud sync merges against a snapshot, so the local side was newer in every
        /// comparison it ever took part in. "The newest choice wins" therefore meant
        /// "this device wins", and the damage ran the one way that matters — a phone that
        /// had never been renamed pushed its default over a name chosen on a tablet, and a
        /// fresh install overwrote the name it had just downloaded. Per-field stamps
        /// (schema v15) make the comparison mean what it says.
        /// </para>
        ///
        /// <para>
        /// Still a join. It is a maximum over a total order — a real value beats an absent
        /// one, then the later stamp wins, then ordinal order settles a tie — so it is
        /// idempotent and gives the same answer whichever device runs it, which is what
        /// <see cref="Join"/> promises. The empty test comes first and outranks the stamps
        /// because empty is never something a player asked for: nothing in the game can
        /// store it, so it only ever means "this device has no opinion", and an opinion
        /// beats none however old it is.
        /// </para>
        /// </summary>
        static (string Value, long At) Chosen(string mine, long mineAt, string other, long otherAt)
        {
            bool haveMine = !string.IsNullOrEmpty(mine);
            bool haveOther = !string.IsNullOrEmpty(other);

            if (!haveMine && !haveOther) return (string.Empty, 0L);
            if (!haveOther) return (mine, Stamp(mineAt));
            if (!haveMine) return (other, Stamp(otherAt));

            if (mineAt != otherAt)
                return mineAt > otherAt ? (mine, Stamp(mineAt)) : (other, Stamp(otherAt));

            // Same moment, two different values — two devices renamed inside one second,
            // or, far more likely, both files predate the stamps and carry zero. Ordinal
            // order is not a better answer, only a *stable* one, and stability is what
            // keeps the join commutative: an arbitrary choice that depended on argument
            // order would leave two devices pushing over each other for ever.
            return string.CompareOrdinal(mine, other) >= 0 ? (mine, Stamp(mineAt))
                                                           : (other, Stamp(otherAt));
        }

        /// <summary>A negative stamp is a corrupt one; zero already means "undated".</summary>
        static long Stamp(long at) => at < 0 ? 0L : at;

        /// <summary>
        /// Joins two devices' hearts, honouring both "never written" and "written by a
        /// build that kept no ledger".
        ///
        /// <para>
        /// Where both sides carry a v8 ledger the rule is <see cref="Hearts.Join"/> and
        /// nothing else — kept there rather than here so the merge cannot drift from what
        /// the game believes a heart is.
        /// </para>
        /// <para>
        /// A pre-v8 side carries a count and no history, which is not a ledger and cannot
        /// be joined with one directly: its <c>spent</c> would read as zero and every
        /// heart the modern side had spent would come back. So it is rebased onto the
        /// modern side's <c>spent</c> first — see <see cref="Hearts.Observation"/> — and
        /// the join then resolves to whichever side holds more. Generous, bounded by the
        /// cap, and confined to the upgrade window; the alternative is deleting hearts
        /// from whichever device happened to update second, which is the bug this whole
        /// change exists to end.
        /// </para>
        /// <para>
        /// When neither side has a ledger — a device's first sync after updating, against
        /// a cloud document a pre-v8 build last wrote — both rebase onto zero and the
        /// larger count wins. That is deliberately the generous direction: it is the exact
        /// moment the old rule did its damage, so it is the moment worth repairing rather
        /// than preserving.
        /// </para>
        /// </summary>
        static Hearts JoinedHearts(WalletDto mine, WalletDto other)
        {
            // > 0, never >= 0. An absent field deserialises as zero, so a ledger has to be
            // recognisable by a value no absent one can hold — see WalletDto.heartsProduced.
            bool mineHasLedger = mine.heartsProduced > 0;
            bool otherHasLedger = other.heartsProduced > 0;

            if (mineHasLedger && otherHasLedger)
                return Hearts.Join(LedgerOf(mine), LedgerOf(other));

            if (mineHasLedger) return Hearts.Join(LedgerOf(mine), RebasedOnto(other, LedgerOf(mine)));
            if (otherHasLedger) return Hearts.Join(LedgerOf(other), RebasedOnto(mine, LedgerOf(other)));

            // Neither keeps a ledger. -1 is "never written at all", which holds no opinion
            // and must not be read as a count of zero.
            if (mine.hearts < 0 && other.hearts < 0) return Hearts.Full;
            if (mine.hearts < 0) return new Hearts(other.hearts, other.heartsNextRefillUnix);
            if (other.hearts < 0) return new Hearts(mine.hearts, mine.heartsNextRefillUnix);

            return Hearts.Join(new Hearts(mine.hearts, mine.heartsNextRefillUnix),
                               new Hearts(other.hearts, other.heartsNextRefillUnix));
        }

        static Hearts LedgerOf(WalletDto w)
            => Hearts.Ledger(w.heartsProduced, w.heartsSpent, w.heartsDueUnix);

        /// <summary>
        /// A pre-v8 side's count, expressed against the ledger it is being joined with.
        ///
        /// An unwritten count holds no opinion, so it rebases to exactly the other side
        /// rather than to empty — otherwise a wallet section that was never written would
        /// read as a player who had spent everything.
        /// </summary>
        static Hearts RebasedOnto(WalletDto legacy, Hearts ledger)
            => legacy.hearts < 0
                ? ledger
                : Hearts.Observation(legacy.hearts, legacy.heartsNextRefillUnix, ledger.Spent);

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
