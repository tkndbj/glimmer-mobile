using UnityEngine;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// Owns the save file and the moment it is written.
    ///
    /// The three facades over it — <see cref="PlayerProgress"/>, <see cref="GameSettings"/>
    /// and <see cref="Wallet"/> — each own one section and know nothing about the file.
    /// This type is the only place that knows the whole layout, so adding a section
    /// later touches one method rather than every caller.
    ///
    /// Writes are immediate on meaningful change and flushed again when the app is
    /// backgrounded, which on mobile is the last reliable moment before the OS may
    /// kill the process without further warning.
    /// </summary>
    public static class SaveService
    {
        static SaveStore _store;
        static bool _loaded;
        static bool _dirty;

        public static bool IsLoaded => _loaded;

        public static void Load()
        {
            if (_loaded) return;

            _store = new SaveStore();
            var dto = _store.Load();

            bool imported = LegacyPlayerPrefsImport.Apply(dto);

            // Order matters only in that progress must be in place before anything
            // derives from it; the sections are otherwise independent.
            PlayerProgress.LoadFrom(dto);
            GameSettings.LoadFrom(dto);
            Wallet.LoadFrom(dto);
            TipLedger.LoadFrom(dto);
            ProgressionStore.LoadFrom(dto);
            CloudState.LoadFrom(dto);

            _loaded = true;

            if (imported)
            {
                // Write first, and only clear the old keys once that write succeeded,
                // so an interrupted upgrade retries cleanly instead of losing data.
                if (Write()) LegacyPlayerPrefsImport.ClearLegacyKeys();
            }
        }

        /// <summary>Marks the file as needing a write and performs it now.</summary>
        public static void Save()
        {
            _dirty = true;
            Flush();
        }

        public static void MarkDirty() => _dirty = true;

        /// <summary>Writes if anything changed. Safe to call often.</summary>
        public static void Flush()
        {
            if (!_loaded || !_dirty) return;
            Write();
        }

        static bool Write()
        {
            var dto = Snapshot();

            bool ok = _store.Save(dto);
            if (ok) _dirty = false;
            return ok;
        }

        /// <summary>
        /// The current state as a file, without writing it.
        ///
        /// The cloud sync needs exactly this — a snapshot it can merge against what the
        /// server holds — and building it here rather than in the sync keeps one place
        /// that knows the whole layout, which is the point of this type.
        /// </summary>
        public static SaveFileDto Snapshot()
        {
            CloudState.NextRevision();

            var dto = new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,

                // Stamped here, not only in SaveStore.Save. The cloud sync takes a
                // snapshot without writing it to disk, and SaveMerge decides which side
                // holds the newer preferences by comparing this — a snapshot claiming
                // the epoch would lose every one of those comparisons.
                updatedUnix = SaveSchema.NowUnix(),
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                legacyImportDone = true,
            };

            PlayerProgress.WriteInto(dto);
            GameSettings.WriteInto(dto);
            Wallet.WriteInto(dto);
            TipLedger.WriteInto(dto);
            ProgressionStore.WriteInto(dto);
            CloudState.WriteInto(dto);

            return dto;
        }

        /// <summary>
        /// Replaces everything in memory with a merged file and writes it.
        ///
        /// Used by the cloud sync once it has joined the local and remote saves. It
        /// goes through the same load path as a launch, so there is no second way for
        /// state to enter the game and no chance of the two drifting apart.
        /// </summary>
        public static bool Adopt(SaveFileDto dto)
        {
            if (dto == null || !_loaded) return false;

            PlayerProgress.LoadFrom(dto);
            GameSettings.LoadFrom(dto);
            Wallet.LoadFrom(dto);
            TipLedger.LoadFrom(dto);
            ProgressionStore.LoadFrom(dto);
            CloudState.LoadFrom(dto);

            _dirty = true;
            return Write();
        }

        /// <summary>Erases everything and starts again. Used by the settings screen.</summary>
        public static void Wipe()
        {
            _store?.Delete();
            LegacyPlayerPrefsImport.ClearLegacyKeys();

            var fresh = new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                levels = new LevelRecordDto[0],
                progression = ProgressionStateDto.Unwritten(),
                legacyImportDone = true,
            };

            PlayerProgress.LoadFrom(fresh);
            Wallet.LoadFrom(fresh);
            TipLedger.LoadFrom(fresh);
            ProgressionStore.LoadFrom(fresh);
            // settings and the cloud identity deliberately survive a progress wipe

            _dirty = true;
            Flush();

            Debug.Log("[Save] progress erased");
        }
    }
}
