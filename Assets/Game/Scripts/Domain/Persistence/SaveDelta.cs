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
            if (!SameSet(remote.tipsSeen, merged.tipsSeen)) return true;

            // The companions the player bought. These have to travel, and the reason is
            // sharper than the tip set's: a purchase is the one thing in this file that
            // cannot be re-derived, so a set that stayed on one phone is a companion the
            // player paid for and loses on reinstall. Compared as an ordered walk because
            // both sides are written sorted — see CompanionLedger.
            if (!SameSet(remote.companionsOwned, merged.companionsOwned)) return true;
            if (!Same(remote.lastPlayedLevelId, merged.lastPlayedLevelId)) return true;

            var a = remote.settings ?? new SettingsDto();
            var b = merged.settings ?? new SettingsDto();
            if (a.music.state != b.music.state) return true;
            if (a.sfx.state != b.sfx.state) return true;
            if (a.haptics.state != b.haptics.state) return true;
            if (!Same(a.language, b.language)) return true;

            var walletA = remote.wallet ?? WalletDto.Unwritten();
            var walletB = merged.wallet ?? WalletDto.Unwritten();
            // The heart ledger, and not the count beside it. The count is derived from
            // these three, so comparing it as well would only add a way for the two
            // answers to disagree — and comparing it *instead* would miss a refill
            // deadline that moved without the count moving, which is precisely the state
            // the other device needs in order to merge correctly.
            if (walletA.heartsProduced != walletB.heartsProduced) return true;
            if (walletA.heartsSpent != walletB.heartsSpent) return true;
            if (walletA.heartsDueUnix != walletB.heartsDueUnix) return true;
            if (walletA.heartBoostUntilUnix != walletB.heartBoostUntilUnix) return true;
            if (!Same(walletA.displayName, walletB.displayName)) return true;
            if (!Same(walletA.avatarId, walletB.avatarId)) return true;

            // Today's chest counters. Small, and they change several times a session, so
            // they are compared rather than assumed — a day that rolled over on one device
            // has to reach the other or its chests would still look unopened.
            var dailyA = remote.daily ?? new DailyStateDto();
            var dailyB = merged.daily ?? new DailyStateDto();
            if (dailyA.dayKey != dailyB.dayKey) return true;
            if (dailyA.runs != dailyB.runs) return true;
            if (dailyA.claimed != dailyB.claimed) return true;

            // Today's ad allowance, compared for the same reason and with one of its own:
            // the cap is the only thing standing between a second device and a fresh set
            // of ads, so a count that stays on one phone is a cap that does not exist.
            var adsA = remote.ads ?? new AdStateDto();
            var adsB = merged.ads ?? new AdStateDto();
            if (adsA.dayKey != adsB.dayKey) return true;
            if (adsA.lastWatchedUnix != adsB.lastWatchedUnix) return true;
            if (!SameCounts(adsA.watched, adsB.watched)) return true;

            // The streak's three dates. All monotonic, so a difference always means one
            // side has seen a night the other has not — which is exactly when the other
            // device needs to hear about it, since a streak that does not travel is a
            // streak that restarts on every device the player owns.
            var streakA = remote.streak ?? new StreakStateDto();
            var streakB = merged.streak ?? new StreakStateDto();
            if (streakA.startDay != streakB.startDay) return true;
            if (streakA.lastPlayedDay != streakB.lastPlayedDay) return true;
            if (streakA.collectedThroughDay != streakB.collectedThroughDay) return true;

            // The event floors. These have to travel, and for a stronger reason than the
            // streak's dates do: the server re-derives what a save is worth, and a floor it
            // has not been told about is a milestone it will not pay for. A collect that
            // never reached the server would look to the next device exactly like a rung
            // still waiting.
            if (remote.eventsSeeded != merged.eventsSeeded) return true;
            if (!SameEvents(remote.events, merged.events)) return true;

            var progressA = remote.progression ?? ProgressionStateDto.Unwritten();
            var progressB = merged.progression ?? ProgressionStateDto.Unwritten();
            if (progressA.xpHighWater != progressB.xpHighWater) return true;
            if (progressA.levelHighWater != progressB.levelHighWater) return true;

            // The account this save belongs to. A change here means it has just been
            // linked, which the server needs to know about immediately.
            if (!Same(remote.cloud?.userId, merged.cloud?.userId)) return true;

            return false;
        }

        /// <summary>
        /// Both lists are written sorted, so a plain ordered walk is enough — and any
        /// difference in length is a difference in content, because the union only grows.
        /// </summary>
        static bool SameSet(string[] a, string[] b)
        {
            int na = a?.Length ?? 0, nb = b?.Length ?? 0;
            if (na != nb) return false;

            for (int i = 0; i < na; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;

            return true;
        }

        /// <summary>
        /// Ad view counters, compared as an ordered walk for the same reason
        /// <see cref="SameSet"/> can: they are written sorted by placement id and
        /// deduplicated, so equal content is byte-equal content.
        /// </summary>
        static bool SameCounts(AdViewCountDto[] a, AdViewCountDto[] b)
        {
            int na = a?.Length ?? 0, nb = b?.Length ?? 0;
            if (na != nb) return false;

            for (int i = 0; i < na; i++)
            {
                var x = a[i] ?? new AdViewCountDto();
                var y = b[i] ?? new AdViewCountDto();

                if (!Same(x.placement, y.placement)) return false;
                if (x.count != y.count) return false;
            }

            return true;
        }

        /// <summary>
        /// Event collection floors, walked in order for the reason <see cref="SameCounts"/>
        /// can be: they are written sorted by event id and deduplicated, so equal content is
        /// byte-equal content.
        /// </summary>
        static bool SameEvents(EventStateDto[] a, EventStateDto[] b)
        {
            int na = a?.Length ?? 0, nb = b?.Length ?? 0;
            if (na != nb) return false;

            for (int i = 0; i < na; i++)
            {
                var x = a[i] ?? new EventStateDto();
                var y = b[i] ?? new EventStateDto();

                if (!Same(x.id, y.id)) return false;
                if (x.collectedGoal != y.collectedGoal) return false;
            }

            return true;
        }

        static bool Same(string a, string b)
            => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
    }
}
