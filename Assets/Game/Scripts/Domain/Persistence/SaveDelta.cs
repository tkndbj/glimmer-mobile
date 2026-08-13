using System;
using System.Collections.Generic;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// What actually changed between the save the server holds and the one about to be
    /// pushed.
    ///
    /// Without this, every sync re-uploads the entire ledger. That is correct and
    /// perfectly affordable at three glades; at two thousand it is a hundred kilobytes
    /// of a player's mobile data every time the app is backgrounded, to communicate that
    /// one glade gained a star. Firestore bills per document write rather than per byte,
    /// so this is not a cost optimisation — it is a bandwidth and latency one, and on a
    /// phone those are the ones the player feels.
    ///
    /// It is computed by comparison rather than by tracking dirty flags. The sync
    /// already holds both sides at the moment it needs the answer, so there is no extra
    /// state to keep, nothing to reset at the wrong moment, and no way for a missed flag
    /// to lose a write. A diff cannot drift from the thing it describes.
    /// </summary>
    public sealed class SaveDelta
    {
        /// <summary>Nothing exists on the server yet, so everything has to go.</summary>
        public readonly bool IsFullWrite;

        /// <summary>Level ids whose record differs, or is new.</summary>
        public readonly IReadOnlyList<string> ChangedLevelIds;

        /// <summary>True when anything outside the ledger differs.</summary>
        public readonly bool ScalarsChanged;

        SaveDelta(bool isFullWrite, IReadOnlyList<string> changedLevelIds, bool scalarsChanged)
        {
            IsFullWrite = isFullWrite;
            ChangedLevelIds = changedLevelIds ?? Array.Empty<string>();
            ScalarsChanged = scalarsChanged;
        }

        public static readonly SaveDelta Nothing =
            new SaveDelta(false, Array.Empty<string>(), false);

        public static readonly SaveDelta Everything =
            new SaveDelta(true, Array.Empty<string>(), true);

        /// <summary>True when there is genuinely nothing to send.</summary>
        public bool IsEmpty => !IsFullWrite && !ScalarsChanged && ChangedLevelIds.Count == 0;

        public override string ToString()
            => IsFullWrite ? "full write"
             : IsEmpty ? "nothing"
             : $"{ChangedLevelIds.Count} level(s){(ScalarsChanged ? " and the header" : "")}";

        /// <summary>
        /// Diffs the merged save against what the server holds.
        ///
        /// <paramref name="remote"/> being null means the document does not exist and
        /// the whole thing has to be written.
        /// </summary>
        public static SaveDelta Between(SaveFileDto remote, SaveFileDto merged)
        {
            if (merged == null) return Nothing;
            if (remote == null) return Everything;

            var remoteLevels = Index(remote.levels);
            var changed = new List<string>();

            if (merged.levels != null)
            {
                foreach (var record in merged.levels)
                {
                    if (record == null || string.IsNullOrEmpty(record.levelId)) continue;

                    if (!remoteLevels.TryGetValue(record.levelId, out var before) || Differs(before, record))
                        changed.Add(record.levelId);
                }
            }

            return new SaveDelta(false, changed, ScalarsDiffer(remote, merged));
        }

        static Dictionary<string, LevelRecordDto> Index(LevelRecordDto[] records)
        {
            var byId = new Dictionary<string, LevelRecordDto>(StringComparer.Ordinal);
            if (records == null) return byId;

            foreach (var record in records)
                if (record != null && !string.IsNullOrEmpty(record.levelId))
                    byId[record.levelId] = record;

            return byId;
        }

        static bool Differs(LevelRecordDto a, LevelRecordDto b)
            => a.stars != b.stars
            || a.bestMoves != b.bestMoves
            || a.clears != b.clears
            || a.firstClearedUnix != b.firstClearedUnix
            || a.lastPlayedUnix != b.lastPlayedUnix;

        /// <summary>
        /// Everything outside the ledger, compared field by field.
        ///
        /// <c>revision</c>, <c>updatedUnix</c> and <c>checksum</c> are deliberately not
        /// compared: they change on every local write whether or not anything a player
        /// would notice did, so including them would make every sync a write and defeat
        /// the whole exercise. They are still sent whenever something else is.
        /// </summary>
        static bool ScalarsDiffer(SaveFileDto remote, SaveFileDto merged)
        {
            if (remote.schemaVersion != merged.schemaVersion) return true;
            if (remote.legacyImportDone != merged.legacyImportDone) return true;
            if (!Same(remote.lastPlayedLevelId, merged.lastPlayedLevelId)) return true;

            var a = remote.settings ?? new SettingsDto();
            var b = merged.settings ?? new SettingsDto();
            if (a.music.state != b.music.state) return true;
            if (a.sfx.state != b.sfx.state) return true;
            if (a.haptics.state != b.haptics.state) return true;
            if (!Same(a.language, b.language)) return true;

            var walletA = remote.wallet ?? WalletDto.Unwritten();
            var walletB = merged.wallet ?? WalletDto.Unwritten();
            if (walletA.hearts != walletB.hearts) return true;
            if (!Same(walletA.displayName, walletB.displayName)) return true;

            var progressA = remote.progression ?? ProgressionStateDto.Unwritten();
            var progressB = merged.progression ?? ProgressionStateDto.Unwritten();
            if (progressA.xpHighWater != progressB.xpHighWater) return true;
            if (progressA.levelHighWater != progressB.levelHighWater) return true;

            // The account this save belongs to. A change here means it has just been
            // linked, which the server needs to know about immediately.
            if (!Same(remote.cloud?.userId, merged.cloud?.userId)) return true;

            return false;
        }

        static bool Same(string a, string b)
            => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
    }
}
