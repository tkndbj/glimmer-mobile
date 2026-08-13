using System;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// The on-disk shape of a save file.
    ///
    /// Two rules keep this survivable for the life of the game. Every record is keyed
    /// by a level's permanent id, never by its position, so content can be reordered
    /// or inserted without a player's history sliding onto the wrong levels. And every
    /// optional value has a "not written" state distinct from a real value, because
    /// JsonUtility fills missing fields with zero and a missing sound setting must not
    /// read as "muted".
    /// </summary>
    public static class SaveSchema
    {
        public const int Version = 1;

        /// <summary>Progress that predates this file: index-keyed keys in PlayerPrefs.</summary>
        public const int LegacyPlayerPrefsVersion = 0;

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

        /// <summary>Where the player left off, so the map can open in the right place.</summary>
        public string lastPlayedLevelId;

        /// <summary>Set once a legacy PlayerPrefs import has run, so it never runs twice.</summary>
        public bool legacyImportDone;

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
    /// Soft currencies. The economy is not built yet, so these are placeholders —
    /// but they live in the save file from the start so that building it later is a
    /// feature change rather than another migration of everyone's data.
    /// </summary>
    [Serializable]
    public sealed class WalletDto
    {
        /// <summary>-1 means never written, so the seeded starting balance applies.</summary>
        public int coins;
        public int gems;
        public int hearts;
        public string displayName;

        public static WalletDto Unwritten() => new WalletDto { coins = -1, gems = -1, hearts = -1 };
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
