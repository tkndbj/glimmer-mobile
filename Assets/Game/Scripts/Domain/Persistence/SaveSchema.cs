using System;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// The on-disk shape of a save file.
    ///
    /// Three rules keep this survivable for the life of the game. Every record is
    /// keyed by a level's permanent id, never by its position, so content can be
    /// reordered or inserted without a player's history sliding onto the wrong levels.
    /// Every optional value has a "not written" state distinct from a real value,
    /// because JsonUtility fills missing fields with zero and a missing sound setting
    /// must not read as "muted". And nothing derivable is stored — XP and earned
    /// credits are recomputed from the level records, so they cannot drift, be
    /// double-counted across devices, or be forged by editing a number.
    ///
    /// <para>
    /// <b>Adding a field is not free.</b> <see cref="SaveChecksum"/> hashes the
    /// serialised object, so a file written by an older schema can never match a
    /// newer build's hash. That is why verification is skipped across versions —
    /// without it, growing this file would fail every save on every device at once.
    /// </para>
    /// </summary>
    public static class SaveSchema
    {
        /// <summary>
        /// v1 — levels, settings, flat coin/gem balances.
        /// v2 — currency ledgers (granted/spent/earned high-water), progression
        ///      high-water marks, cloud sync state.
        /// v3 — the chosen profile companion (<see cref="WalletDto.avatarId"/>).
        /// v4 — the heart refill deadline (<see cref="WalletDto.heartsNextRefillUnix"/>),
        ///      which turned hearts from a number nothing moved into a resource that
        ///      regenerates and gates play.
        /// v5 — the set of mechanic tips already shown (<see cref="SaveFileDto.tipsSeen"/>),
        ///      so a lesson taught once is never repeated on any of a player's devices.
        /// </summary>
        public const int Version = 5;

        /// <summary>Progress that predates this file: index-keyed keys in PlayerPrefs.</summary>
        public const int LegacyPlayerPrefsVersion = 0;

        /// <summary>Flat <c>wallet.coins</c> / <c>wallet.gems</c> balances, before ledgers.</summary>
        public const int FlatWalletVersion = 1;

        public const string FileName = "progress.json";
        public const string BackupFileName = "progress.backup.json";

        public static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    /// <summary>Tri-state flag: 0 means the field was never written, so use the default.</summary>
    [Serializable]
    public struct StoredFlag
    {
        public int state;

        public const int Unset = 0;
        public const int On = 1;
        public const int Off = 2;

        public bool Resolve(bool fallback) => state == Unset ? fallback : state == On;

        public void Set(bool value) => state = value ? On : Off;

        public static StoredFlag From(bool value)
        {
            var f = new StoredFlag();
            f.Set(value);
            return f;
        }
    }

    [Serializable]
    public sealed class SaveFileDto
    {
        public int schemaVersion;
        public long updatedUnix;

        public SettingsDto settings;
        public WalletDto wallet;
        public LevelRecordDto[] levels;

        /// <summary>High-water marks that stop a retune from taking anything away.</summary>
        public ProgressionStateDto progression;

        /// <summary>Who this save belongs to and when it last reached the server.</summary>
        public CloudStateDto cloud;

        /// <summary>Where the player left off, so the map can open in the right place.</summary>
        public string lastPlayedLevelId;

        /// <summary>Set once a legacy PlayerPrefs import has run, so it never runs twice.</summary>
        public bool legacyImportDone;

        /// <summary>
        /// Permanent ids of the mechanic tips this player has been shown. Unknown ids
        /// are carried through untouched — a lesson learned on a newer build must not
        /// be re-taught after a trip through an older one.
        /// </summary>
        public string[] tipsSeen;

        /// <summary>
        /// Integrity check over the rest of the file. Empty on files written before
        /// checksums existed, which are accepted and gain one on the next write.
        /// </summary>
        public string checksum;
    }

    [Serializable]
    public sealed class SettingsDto
    {
        public StoredFlag music;
        public StoredFlag sfx;
        public StoredFlag haptics;
        public string language;
    }

    /// <summary>
    /// Currencies, and the player's chosen name.
    ///
    /// <see cref="coins"/> and <see cref="gems"/> are the v1 shape: flat balances the
    /// client was free to set. They are read once, folded into a ledger's granted
    /// baseline so nobody loses what they had, and never written again.
    /// </summary>
    [Serializable]
    public sealed class WalletDto
    {
        /// <summary>-1 means never written, so the seeded starting balance applies.</summary>
        public int coins;
        public int gems;

        /// <summary>Hearts held. -1 means never written, so a full set is seeded.</summary>
        public int hearts;

        /// <summary>
        /// When the next heart lands, as a Unix timestamp; 0 while the player is full
        /// and no timer is running.
        ///
        /// The deadline is stored rather than "when we last topped up" so that refills
        /// cannot drift: each one advances this by exactly one period, and closing the
        /// app mid-timer neither loses nor gains the remainder. A v3 file has no value
        /// here, which reads as 0 — see <see cref="Hearts.At"/>, which starts the clock
        /// from the next read instead of back-paying time nobody waited.
        /// </summary>
        public long heartsNextRefillUnix;

        public string displayName;

        /// <summary>
        /// The companion shown on the profile, by permanent avatar id. Empty means the
        /// player has never chosen one, which is not the same as choosing the first —
        /// the roster's default may change, and a real choice must survive that.
        /// </summary>
        public string avatarId;

        /// <summary>One ledger per currency, keyed by a permanent currency id.</summary>
        public CurrencyLedgerDto[] currencies;

        public static WalletDto Unwritten() => new WalletDto { coins = -1, gems = -1, hearts = -1 };
    }

    /// <summary>
    /// Double-entry state for one currency.
    ///
    /// A balance is <c>max(derived earned, earnedHighWater) + granted - spent</c>. Only
    /// the terms that cannot be derived are stored, and each is monotonic or
    /// server-owned, which is what lets two devices be merged without inventing money
    /// or losing a purchase.
    /// </summary>
    [Serializable]
    public sealed class CurrencyLedgerDto
    {
        /// <summary>Permanent id — <c>credits</c>, <c>gems</c>. Never renamed or reused.</summary>
        public string currency;

        /// <summary>
        /// Everything given rather than earned: the starting seed, purchases, gifts.
        /// Server-owned once cloud save is live; the client may never raise it.
        /// </summary>
        public long grantedBaseline;

        /// <summary>Spends the server has confirmed and folded in.</summary>
        public long spentBaseline;

        /// <summary>
        /// Floor under the derived earnings. Stops a reward retune, or a chapter that
        /// is temporarily out of the catalog, from reducing a balance a player is
        /// already holding.
        /// </summary>
        public long earnedHighWater;

        /// <summary>Spends made since the last sync, each with an idempotency key.</summary>
        public SpendEntryDto[] pendingSpends;

        /// <summary>
        /// Debits at or before this moment are already inside <see cref="spentBaseline"/>.
        /// Persisted because a merge on a later launch still needs it to tell a debit
        /// the server has absorbed from one it has never seen.
        /// </summary>
        public long confirmedThroughUnix;
    }

    /// <summary>
    /// One debit, identified so that submitting it twice can only charge once.
    ///
    /// The id is generated where the spend happens and never reused. It is what makes
    /// a retry after a dropped response safe, which is the whole reason a bare
    /// counter is not good enough here.
    /// </summary>
    [Serializable]
    public sealed class SpendEntryDto
    {
        public string id;
        public long amount;
        public long unix;

        /// <summary>What it was spent on. Carried for support and for analytics.</summary>
        public string reason;
    }

    [Serializable]
    public sealed class ProgressionStateDto
    {
        /// <summary>-1 means never written.</summary>
        public long xpHighWater;
        public int levelHighWater;

        public static ProgressionStateDto Unwritten()
            => new ProgressionStateDto { xpHighWater = -1, levelHighWater = -1 };
    }

    [Serializable]
    public sealed class CloudStateDto
    {
        /// <summary>The authenticated account this save belongs to. Empty when local only.</summary>
        public string userId;

        /// <summary>Bumped on every local write, so a backend can order two snapshots.</summary>
        public long revision;

        public long lastSyncedUnix;

        /// <summary>Identifies the writing device in a merge, for support and diagnostics.</summary>
        public string deviceId;
    }

    [Serializable]
    public sealed class LevelRecordDto
    {
        public string levelId;
        public int stars;
        public int bestMoves;
        public int clears;
        public long firstClearedUnix;
        public long lastPlayedUnix;
    }
}
