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
        static ISaveStore _store;
        static bool _loaded;
        static bool _dirty;

        public static bool IsLoaded => _loaded;

        public static void Load() => LoadWith(new SaveStore());

        /// <summary>
        /// Loads through a store the caller supplies.
        ///
        /// <para>
        /// Internal, and for the tests around the cloud sync — which have to prove things about
        /// a save being replaced and could not do it against the live one without erasing
        /// whoever ran them. The production path is <see cref="Load"/> and there is no second
        /// way in: this is the same method with the store named, so a test exercises the code
        /// that ships rather than a copy of it. See <see cref="ISaveStore"/> for why the
        /// parameter is an interface.
        /// </para>
        /// </summary>
        internal static void LoadWith(ISaveStore store)
        {
            if (_loaded) return;

            _store = store;
            var dto = _store.Load();

            bool imported = LegacyPlayerPrefsImport.Apply(dto);

            // Order matters only in that progress must be in place before anything
            // derives from it; the sections are otherwise independent.
            PlayerProgress.LoadFrom(dto);
            GameSettings.LoadFrom(dto);
            Wallet.LoadFrom(dto);
            TipLedger.LoadFrom(dto);
            Progression.CompanionLedger.LoadFrom(dto);
            Homestead.HomesteadLedger.LoadFrom(dto);
            Homestead.GroveLand.LoadFrom(dto);
            Homestead.HomesteadLayout.LoadFrom(dto);
            Daily.DailyChests.LoadFrom(dto);
            Daily.DailyStreak.LoadFrom(dto);
            Events.EventCollection.LoadFrom(dto);
            Ads.RewardedAds.LoadFrom(dto);
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

        /// <summary>Forgets everything, so a test can load a different file. Tests only.</summary>
        internal static void Unload()
        {
            _loaded = false;
            _dirty = false;
            _store = null;
            CloudState.Reset();
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
            Progression.CompanionLedger.WriteInto(dto);
            Homestead.HomesteadLedger.WriteInto(dto);
            Homestead.GroveLand.WriteInto(dto);
            Homestead.HomesteadLayout.WriteInto(dto);
            Daily.DailyChests.WriteInto(dto);
            Daily.DailyStreak.WriteInto(dto);
            Events.EventCollection.WriteInto(dto);
            Ads.RewardedAds.WriteInto(dto);
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
            Progression.CompanionLedger.LoadFrom(dto);
            Homestead.HomesteadLedger.LoadFrom(dto);
            Homestead.GroveLand.LoadFrom(dto);
            Homestead.HomesteadLayout.LoadFrom(dto);
            Daily.DailyChests.LoadFrom(dto);
            Daily.DailyStreak.LoadFrom(dto);
            Events.EventCollection.LoadFrom(dto);
            Ads.RewardedAds.LoadFrom(dto);
            ProgressionStore.LoadFrom(dto);
            CloudState.LoadFrom(dto);

            _dirty = true;
            return Write();
        }

        /// <summary>
        /// Erases the grove and starts again.
        ///
        /// <para>
        /// <paramref name="forgetAccount"/> decides whether the file that is left behind still
        /// belongs to anybody, and getting it wrong is the difference between a clean start and
        /// a promise the next sync breaks. Keeping the identity is right for
        /// <c>CloudSaveService</c>'s adopt path, which is about to sign in as a different
        /// account and overwrite it a line later. It is wrong — and was the whole reason the
        /// settings screen's reset button was deleted — for anything that leaves the device
        /// signed in as the same player: <c>SaveMerge.Join</c> is monotonic, so the next sync
        /// pulls the emptied grove straight back and the erasure undoes itself in front of
        /// somebody who watched it happen.
        /// </para>
        /// <para>
        /// Forgetting it leaves a file owned by nobody, which is the honest description of a
        /// device between two accounts and the state <see cref="Cloud.AccountGate"/> can safely
        /// resolve from: it names no account, so it can adopt whichever one signs in without
        /// ever having to decide between two.
        /// </para>
        /// </summary>
        public static void Wipe(bool forgetAccount = false)
        {
            _store?.Delete();
            LegacyPlayerPrefsImport.ClearLegacyKeys();

            var fresh = new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                levels = new LevelRecordDto[0],
                daily = new DailyStateDto(),
                progression = ProgressionStateDto.Unwritten(),
                legacyImportDone = true,
            };

            PlayerProgress.LoadFrom(fresh);
            Wallet.LoadFrom(fresh);
            TipLedger.LoadFrom(fresh);
            Progression.CompanionLedger.LoadFrom(fresh);
            Homestead.HomesteadLedger.LoadFrom(fresh);
            Homestead.GroveLand.LoadFrom(fresh);
            Homestead.HomesteadLayout.LoadFrom(fresh);
            Daily.DailyChests.LoadFrom(fresh);
            Daily.DailyStreak.LoadFrom(fresh);
            Events.EventCollection.LoadFrom(fresh);
            Ads.RewardedAds.LoadFrom(fresh);
            ProgressionStore.LoadFrom(fresh);

            // Settings survive either way: they describe the handset and the person holding
            // it, not the grove. The identity survives only when the caller says so.
            if (forgetAccount) CloudState.ForgetAccount();

            _dirty = true;
            Flush();

            Debug.Log(forgetAccount ? "[Save] progress erased and account forgotten"
                                    : "[Save] progress erased");
        }
    }
}
