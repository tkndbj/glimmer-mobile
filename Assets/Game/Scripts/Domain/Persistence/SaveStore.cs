using System;
using System.IO;
using UnityEngine;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// Somewhere a save file lives.
    ///
    /// <para>
    /// Three members, and it exists for one reason: <see cref="SaveService"/> is the seam the
    /// whole cloud sync runs through, and while it held a concrete <see cref="SaveStore"/>
    /// nothing above it could be tested without <c>JsonUtility</c> and a real directory — which
    /// meant the account switch, the merge adoption and every ordering they depend on could only
    /// be proved with the Editor open. Those are the parts of this project whose failures are
    /// unrecoverable, so "prove it, do not assert it" needs them runnable offline.
    /// </para>
    /// <para>
    /// It deliberately does <b>not</b> abstract the thing that makes <see cref="SaveStore"/>
    /// worth having — the atomic write, the backup rotation, the corrupt-file recovery. That is
    /// a filesystem behaviour, it is tested against a real filesystem in <c>SaveStoreTests</c>,
    /// and a second implementation of it would be a second thing to get wrong. This is a seam
    /// for the layers above, not a strategy.
    /// </para>
    /// </summary>
    public interface ISaveStore
    {
        SaveFileDto Load();
        bool Save(SaveFileDto dto);
        void Delete();
    }

    /// <summary>
    /// Reads and writes the save file on disk.
    ///
    /// Replaces PlayerPrefs, which has no atomic write, no backup and — on Android —
    /// a habit of losing its XML if the process dies at the wrong moment. Losing two
    /// hundred levels of progress is the fastest route to a one-star review, so every
    /// write goes to a temporary file, the previous good copy is rotated to a backup,
    /// and only then does the real file get replaced. A load that finds a corrupt file
    /// silently falls back to that backup.
    ///
    /// The file is plain JSON. Obfuscating a single-player puzzle game's save buys
    /// nothing except an inability to support a player who has hit a bug.
    /// </summary>
    public sealed class SaveStore : ISaveStore
    {
        readonly string _path;
        readonly string _backupPath;
        readonly string _tempPath;

        public SaveStore(string directory = null)
        {
            string dir = directory ?? Application.persistentDataPath;
            _path = Path.Combine(dir, SaveSchema.FileName);
            _backupPath = Path.Combine(dir, SaveSchema.BackupFileName);
            _tempPath = _path + ".tmp";
        }

        public string Path_ => _path;

        public bool Exists => File.Exists(_path) || File.Exists(_backupPath);

        public SaveFileDto Load()
        {
            if (TryRead(_path, out var dto)) return dto;

            if (TryRead(_backupPath, out dto))
            {
                Debug.LogWarning("[Save] main file unreadable, recovered from the backup");
                return dto;
            }

            return CreateDefault();
        }

        public bool Save(SaveFileDto dto)
        {
            if (dto == null) return false;

            dto.schemaVersion = SaveSchema.Version;
            dto.updatedUnix = SaveSchema.NowUnix();

            try
            {
                dto.checksum = SaveChecksum.Compute(dto);
                string json = JsonUtility.ToJson(dto, true);

                File.WriteAllText(_tempPath, json);

                // rotate: the copy we are about to replace becomes the backup
                if (File.Exists(_path))
                {
                    if (File.Exists(_backupPath)) File.Delete(_backupPath);
                    File.Move(_path, _backupPath);
                }

                File.Move(_tempPath, _path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[Save] could not write progress: " + e.Message);
                return false;
            }
        }

        public void Delete()
        {
            TryDelete(_path);
            TryDelete(_backupPath);
            TryDelete(_tempPath);
        }

        // ------------------------------------------------------------- internals
        bool TryRead(string path, out SaveFileDto dto)
        {
            dto = null;
            try
            {
                if (!File.Exists(path)) return false;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return false;

                dto = JsonUtility.FromJson<SaveFileDto>(json);
                if (dto == null) return false;

                // A truncated file can still parse. Better to fall through to the
                // backup than to load a plausible-looking half of someone's progress.
                if (!SaveChecksum.Verify(dto))
                {
                    Debug.LogWarning($"[Save] {path} failed its integrity check, treating as corrupt");
                    dto = null;
                    return false;
                }

                // A file from a future build may hold fields this one cannot honour.
                // Reading it anyway is safer than discarding a player's progress; the
                // unknown fields survive untouched only if we do not rewrite it, so
                // callers are warned rather than silently downgrading.
                if (dto.schemaVersion > SaveSchema.Version)
                    Debug.LogWarning($"[Save] file is schema v{dto.schemaVersion}, this build knows " +
                                     $"v{SaveSchema.Version}; reading what it understands");

                Normalise(dto);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] could not read {path}: {e.Message}");
                return false;
            }
        }

        static void Normalise(SaveFileDto dto)
        {
            dto.settings ??= new SettingsDto();
            dto.wallet ??= WalletDto.Unwritten();
            dto.levels ??= Array.Empty<LevelRecordDto>();
            dto.cloud ??= new CloudStateDto();

            // A v1 file has no progression section at all, and an absent one must read
            // as "no floor yet" rather than as a floor of zero written on purpose.
            dto.progression ??= ProgressionStateDto.Unwritten();
        }

        static SaveFileDto CreateDefault() => new SaveFileDto
        {
            schemaVersion = SaveSchema.Version,
            settings = new SettingsDto(),
            wallet = WalletDto.Unwritten(),
            levels = Array.Empty<LevelRecordDto>(),
            progression = ProgressionStateDto.Unwritten(),
            cloud = new CloudStateDto(),
        };

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { Debug.LogWarning($"[Save] could not delete {path}: {e.Message}"); }
        }
    }
}
