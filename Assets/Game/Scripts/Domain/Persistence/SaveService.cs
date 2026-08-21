using System;
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
        static IAccountArchive _archive = new NullAccountArchive();
        static bool _loaded;
        static bool _dirty;

        public static bool IsLoaded => _loaded;

        public static void Load() => LoadWith(new SaveStore(), new AccountArchiveStore());

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
        internal static void LoadWith(ISaveStore store, IAccountArchive archive = null)
        {
            if (_loaded) return;

            _store = store;
            _archive = archive ?? new NullAccountArchive();
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
            _archive = new NullAccountArchive();
            CloudState.Reset();
        }

        /// <summary>
        /// Whether a grove for this account is already on this device.
        ///
        /// Asked by the account screen so it can say "welcome back" rather than "please wait"
        /// before a single byte moves, and by nothing that decides anything — a switch works
        /// the same whether the answer is yes or no.
        /// </summary>
        public static bool HasLocalGroveFor(string userId) => _archive.Has(userId);

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

        /// <summary>What a local account swap found waiting for it.</summary>
        public enum SwapResult
        {
            /// <summary>Nothing was asked for, or nothing is loaded. Untouched.</summary>
            Refused,

            /// <summary>This device already was that account.</summary>
            Same,

            /// <summary>A grove for that account was already here and is now the one being played.</summary>
            Restored,

            /// <summary>Nothing here belonged to that account, so a fresh grove was started for it.</summary>
            Started,
        }

        /// <summary>
        /// Puts this device on a different account, using only what is on it.
        ///
        /// <para>
        /// <b>No network, no failure a player has to understand, and nothing destroyed.</b> The
        /// grove being left is copied into <see cref="IAccountArchive"/> under the account it
        /// belongs to, and the grove being joined is restored from there if this device has
        /// seen it before. Whatever the server holds is folded in afterwards by an ordinary
        /// sync — a pull, a monotonic join and a push — which is retried with a backoff like
        /// every other sync in the game. That ordering is the whole point: the previous design
        /// made the download part of the switch, so a dropped connection in the seconds after
        /// an OAuth consent screen left the device authenticated as one player and holding
        /// another's save, with nothing but a warning to get out of it.
        /// </para>
        /// <para>
        /// The archive is read but not dropped until the incoming grove has actually been
        /// written, so a process death in between costs a duplicate copy rather than a grove.
        /// Deliberately says nothing about the network and asks nothing of it: it is called
        /// from a deliberate switch, and from the sync's own repair when the session and the
        /// save have come to disagree.
        /// </para>
        /// <para>
        /// <paramref name="outgoingIsSafe"/> is the caller saying it already knows the grove
        /// being left cannot be lost — because it was just pushed to the server, or because the
        /// player was told it is being abandoned and agreed. When it is false and the archive
        /// cannot take a copy, nothing happens at all: this is the only line in the file that
        /// would destroy local data it had failed to duplicate, and a full disk is not a reason
        /// to do that silently. Both switch routes pass true, so the refusal belongs to the
        /// repair path alone, where it self-heals as soon as there is room.
        /// </para>
        /// </summary>
        public static SwapResult SwitchTo(string userId, bool outgoingIsSafe = false)
        {
            if (!_loaded || string.IsNullOrEmpty(userId)) return SwapResult.Refused;
            if (string.Equals(CloudState.UserId, userId, StringComparison.Ordinal)) return SwapResult.Same;

            // Out first. An account this file does not name has nowhere to be filed, which is
            // an anonymous grove that has never reached a server — there is nothing to come
            // back to, so there is nothing to keep.
            string outgoing = CloudState.UserId;
            if (!string.IsNullOrEmpty(outgoing) && !_archive.Stash(outgoing, Snapshot())
                                                && !outgoingIsSafe)
            {
                Debug.LogWarning("[Save] the grove being left could not be filed, so it is being kept " +
                                 "where it is rather than swapped away");
                return SwapResult.Refused;
            }

            var incoming = _archive.Read(userId);

            if (incoming != null)
            {
                // Adopt's answer is about the *disk write*, not about the swap: it has already
                // replaced everything in memory and left the file dirty, so a failed write is
                // retried by the next Flush exactly like any other. Reading it as "the swap did
                // not happen" and falling through to a fresh grove would throw away the one
                // thing this archive exists to hand back.
                //
                // The copy is dropped only once that write lands, though. Until then it is the
                // only thing on the device that says what this account had, and a stale copy
                // costs nothing — the sync joins it with the server and the join is monotonic.
                if (Adopt(incoming)) _archive.Forget(userId);

                Debug.Log("[Save] switched to a grove already on this device");
                return SwapResult.Restored;
            }

            // Nothing of theirs here, so a fresh grove — owned by them from the first byte.
            //
            // Adopted rather than routed through Wipe, and the difference is not tidiness.
            // Wipe means "erase this player's progress": it deletes the file, clears the
            // pre-1.0 PlayerPrefs keys so an erasure cannot be undone by the legacy importer,
            // and says so in the log. None of that describes arriving at an account, the
            // middle one is actively wrong — those keys belong to whoever installed the game
            // on this handset, not to whichever account is signed in this minute — and it is
            // what kept this whole path out of the offline test suite. A fresh file carries
            // legacyImportDone, so nothing re-imports.
            var fresh = FreshFile();
            fresh.cloud = new CloudStateDto { userId = userId, deviceId = CloudState.DeviceId };

            // The preferences travel and the grove does not. Music, sound, haptics and
            // language describe the handset and the person holding it, so resetting them
            // because somebody signed in to their other account would be a bug they would
            // report as one. The board opt-out travels with them for one moment longer than
            // it should — it belongs to the account rather than the phone — and the sync a
            // few lines later replaces it with the incoming account's own answer. Carrying
            // it is the safe direction of that error: an opt-out held a second too long
            // publishes nothing, and the reverse would publish a card for somebody who had
            // asked not to be on the boards.
            GameSettings.WriteInto(fresh);

            Adopt(fresh);   // the swap is the memory replacement; the write retries — see above

            Debug.Log("[Save] switched to an account with no grove on this device");
            return SwapResult.Started;
        }

        /// <summary>
        /// An empty save, as this build writes one.
        ///
        /// One place, because "what a grove with nothing in it looks like" is one fact and a
        /// second copy is a section somebody adds to one and not the other — which reads as a
        /// migration bug on whichever path missed it. It replaced <c>Wipe</c>, which had no
        /// caller left once a switch stopped meaning "erase what is here": erasing cleared the
        /// pre-1.0 PlayerPrefs keys, which belong to whoever installed the game on this handset
        /// rather than to whichever account is signed in this minute.
        /// </summary>
        static SaveFileDto FreshFile() => new SaveFileDto
        {
            schemaVersion = SaveSchema.Version,
            settings = new SettingsDto(),
            wallet = WalletDto.Unwritten(),
            levels = new LevelRecordDto[0],
            daily = new DailyStateDto(),
            progression = ProgressionStateDto.Unwritten(),
            legacyImportDone = true,
        };
    }
}
