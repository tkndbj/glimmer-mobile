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
        ///
        /// <para>
        /// The same reading is taken the other way round by <c>CloudSaveService</c> — the merged
        /// save against the <em>local</em> file it is about to replace — to say what the device
        /// learned from the server (<c>CloudSaveService.Learned</c>). Both answers come out of one
        /// comparison on purpose: a field this method ignores is a field neither side can notice
        /// moving, so adding one here adds it to both.
        /// </para>
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
            || a.lastPlayedUnix != b.lastPlayedUnix
            // A backfilled standing is the one change here that no run produced, so without
            // this line the first table to land would raise bands on the device and upload
            // none of them.
            || a.bestRank != b.bestRank
            || a.bestMillis != b.bestMillis;

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

            // The heart containers. These travel for the companions' reason with the stakes
            // raised: a container is a real-money purchase, so a set that stayed on one phone
            // is a payment the player made and cannot see on their other device. The
            // revocations travel with them, or a refund honoured on one device would be
            // undone by the next sync from another.
            if (!SameSet(remote.heartContainersOwned, merged.heartContainersOwned)) return true;
            if (!SameSet(remote.heartContainersRevoked, merged.heartContainersRevoked)) return true;

            // The utilities. These travel for the companions' reason with one addition: a
            // utility can be bought with gems, so a row that stayed on one phone is a purchase
            // the player made and cannot see on their other device — and *spent* has to travel
            // with *earned*, or the two devices would each hand back what the other used.
            if (!SameUtilities(remote.utilityStock, merged.utilityStock)) return true;

            // The line. Its purchases travel for the companions' reason; its arrangement travels
            // because it is the one thing here a player can see is wrong on another device — a
            // loadout that stayed on one phone is an evening's decisions lost on reinstall. The
            // stamp travels with the rows, or a device would push an arrangement whose date said
            // it was older than the one it just replaced.
            if (!SameSet(remote.wardsOwned, merged.wardsOwned)) return true;
            if (!SameLoadout(remote.wardLoadout, merged.wardLoadout)) return true;
            if (remote.wardLoadoutSetUnix != merged.wardLoadoutSetUnix) return true;

            // How deep an endless run got. A floor, so a device that has just beaten its best has
            // something the server does not.
            if (!SameEndless(remote.endlessBest, merged.endlessBest)) return true;
            if (!SameStars(remote.wardStars, merged.wardStars)) return true;

            // The daily challenges. Today's rows travel for the ad allowance's reason — the cap
            // is the only thing between a second device and a fresh set of plays; the tally
            // travels because the server derives XP from it; and a deal's date travels because a
            // deal bought on one phone is a page the other draws as free.
            if (!SameChallenges(remote.challenges, merged.challenges)) return true;
            if (!Same(remote.lastPlayedLevelId, merged.lastPlayedLevelId)) return true;

            var a = remote.settings ?? new SettingsDto();
            var b = merged.settings ?? new SettingsDto();
            if (a.music.state != b.music.state) return true;
            if (a.sfx.state != b.sfx.state) return true;
            if (a.haptics.state != b.haptics.state) return true;
            if (a.board.state != b.board.state) return true;
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
            if (walletA.xpBoostWatchedUntilUnix != walletB.xpBoostWatchedUntilUnix) return true;
            if (walletA.xpBoostBoughtUntilUnix != walletB.xpBoostBoughtUntilUnix) return true;
            if (walletA.xpBoostEarned != walletB.xpBoostEarned) return true;

            // The hint ledger, for the reason above it: three counters and no derived count,
            // because the refill deadline moves without the count moving and that is exactly
            // what the other device needs in order to merge correctly.
            if (walletA.hintsProduced != walletB.hintsProduced) return true;
            if (walletA.hintsSpent != walletB.hintsSpent) return true;
            if (walletA.hintsDueUnix != walletB.hintsDueUnix) return true;
            if (!Same(walletA.displayName, walletB.displayName)) return true;
            if (!Same(walletA.avatarId, walletB.avatarId)) return true;

            // The stamps behind those two, compared in their own right. A device holding
            // the same name under a later stamp knows something the server does not — that
            // the name was re-chosen, and so outranks a third device still carrying the
            // older date — and without this the merge would keep deriving that answer
            // locally and never send it. It settles rather than oscillates: the push
            // carries the stamp with the value, so the following sync agrees and writes
            // nothing.
            if (walletA.displayNameSetUnix != walletB.displayNameSetUnix) return true;
            if (walletA.avatarSetUnix != walletB.avatarSetUnix) return true;

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

            // The streak's four dates. All monotonic, so a difference always means one
            // side has seen a night the other has not — which is exactly when the other
            // device needs to hear about it, since a streak that does not travel is a
            // streak that restarts on every device the player owns. The shield's date is
            // the sharpest case of that: a player who paid to be away and then opened the
            // game on their tablet must not find the streak they bought protection for
            // already broken.
            var streakA = remote.streak ?? new StreakStateDto();
            var streakB = merged.streak ?? new StreakStateDto();
            if (streakA.startDay != streakB.startDay) return true;
            if (streakA.lastPlayedDay != streakB.lastPlayedDay) return true;
            if (streakA.collectedThroughDay != streakB.collectedThroughDay) return true;
            if (streakA.shieldFromDay != streakB.shieldFromDay) return true;

            // The tasks. Both periods' counters and claims have to travel: a counter that stays
            // on one phone is a task that reads half done on the other, and a claim that stays
            // is a chest the other device would pay a second time. Compared by walking, so the
            // writer sorts — see TaskLedger.Write.
            if (!SameTasks(remote.tasks, merged.tasks)) return true;

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
        /// The utility ledgers, as an ordered walk. Both sides are written sorted by
        /// <c>UtilityStock.Write</c>, so order is part of the comparison rather than something
        /// this has to normalise — the rule <see cref="SameUtilities"/> already follows.
        /// </summary>
        /// <summary>
        /// Whether two ward lines are the same arrangement.
        ///
        /// An ordered walk, because <c>WardLoadout</c> writes its rows in colour order — the
        /// property that stops an unstable order reading as changed on every launch and pushing a
        /// write for nothing, for ever.
        /// </summary>
        static bool SameLoadout(WardSlotDto[] a, WardSlotDto[] b)
        {
            int an = a?.Length ?? 0, bn = b?.Length ?? 0;
            if (an != bn) return false;

            for (int i = 0; i < an; i++)
            {
                var x = a[i]; var y = b[i];
                if (x == null || y == null) return x == y;
                if (!Same(x.colour, y.colour) || !Same(x.ward, y.ward)) return false;
            }

            return true;
        }

        /// <summary>Whether two endless high-water lists agree. Ordered, for the reason above.</summary>
        /// <summary>
        /// Whether two ladders say the same thing. Rows are written sorted, so this is a walk.
        /// </summary>
        static bool SameStars(WardStarDto[] a, WardStarDto[] b)
        {
            int an = a == null ? 0 : a.Length;
            int bn = b == null ? 0 : b.Length;

            if (an != bn) return false;

            for (int i = 0; i < an; i++)
            {
                if (a[i] == null || b[i] == null) return a[i] == b[i];
                if (a[i].ward != b[i].ward || a[i].stars != b[i].stars) return false;
            }

            return true;
        }

        /// <summary>
        /// Whether two challenge blocks say the same thing. Every list is written sorted by
        /// <c>ChallengeLedger.Write</c>, so each is an ordered walk.
        /// </summary>
        static bool SameChallenges(ChallengeStateDto a, ChallengeStateDto b)
        {
            int dayA = a?.day ?? 0, dayB = b?.day ?? 0;
            if (dayA != dayB) return false;

            var todayA = a?.today; var todayB = b?.today;
            int ta = todayA?.Length ?? 0, tb = todayB?.Length ?? 0;
            if (ta != tb) return false;
            for (int i = 0; i < ta; i++)
            {
                if (todayA[i] == null || todayB[i] == null) return todayA[i] == todayB[i];
                if (!Same(todayA[i].genre, todayB[i].genre)) return false;
                if (todayA[i].attempts != todayB[i].attempts || todayA[i].wins != todayB[i].wins) return false;
            }

            var clearsA = a?.clears; var clearsB = b?.clears;
            int ca = clearsA?.Length ?? 0, cb = clearsB?.Length ?? 0;
            if (ca != cb) return false;
            for (int i = 0; i < ca; i++)
            {
                if (clearsA[i] == null || clearsB[i] == null) return clearsA[i] == clearsB[i];
                if (!Same(clearsA[i].genre, clearsB[i].genre) || clearsA[i].count != clearsB[i].count) return false;
            }

            var tiersA = a?.tiers; var tiersB = b?.tiers;
            int na = tiersA?.Length ?? 0, nb = tiersB?.Length ?? 0;
            if (na != nb) return false;
            for (int i = 0; i < na; i++)
            {
                if (tiersA[i] == null || tiersB[i] == null) return tiersA[i] == tiersB[i];
                if (!Same(tiersA[i].id, tiersB[i].id) || tiersA[i].fromDay != tiersB[i].fromDay) return false;
            }

            return true;
        }

        static bool SameEndless(EndlessBestDto[] a, EndlessBestDto[] b)
        {
            int an = a?.Length ?? 0, bn = b?.Length ?? 0;
            if (an != bn) return false;

            for (int i = 0; i < an; i++)
            {
                var x = a[i]; var y = b[i];
                if (x == null || y == null) return x == y;
                if (!Same(x.level, y.level) || x.wave != y.wave || x.waves != y.waves) return false;
            }

            return true;
        }

        static bool SameUtilities(UtilityStockDto[] a, UtilityStockDto[] b)
        {
            int an = a?.Length ?? 0;
            int bn = b?.Length ?? 0;
            if (an != bn) return false;

            for (int i = 0; i < an; i++)
            {
                var x = a[i] ?? new UtilityStockDto();
                var y = b[i] ?? new UtilityStockDto();

                if (!string.Equals(x.id, y.id, StringComparison.Ordinal)) return false;
                if (x.earned != y.earned || x.spent != y.spent) return false;
            }

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

                // Every field the row carries on the wire, not only the free floor. Comparing
                // one field made a paid-track claim, a mark and the pass flag invisible on their
                // own: a sync that moved nothing else said "same" and pushed nothing, and the
                // server's row lagged by however long that lasted.
                if (x.premiumGoal != y.premiumGoal) return false;
                if (x.marks != y.marks) return false;
                if (x.pass != y.pass) return false;
            }

            return true;
        }

        static bool SameTasks(TaskStateDto a, TaskStateDto b)
            => SamePeriod(a?.daily, b?.daily)
            && SamePeriod(a?.weekly, b?.weekly)
            && SameCounts(a?.lifetime, b?.lifetime);

        /// <summary>
        /// The lifetime tally, walked in order like every other id-keyed list here — both sides
        /// are written sorted (<c>LifetimeTally.Write</c>), so a walk is enough and a set
        /// comparison would only hide an unsorted writer.
        ///
        /// It has to travel: a rank is derived from it, so a battle played on one phone that
        /// never reached the document is a rung the other phone will not agree the player is on.
        /// </summary>
        static bool SameCounts(TaskCountDto[] a, TaskCountDto[] b)
        {
            int na = a?.Length ?? 0, nb = b?.Length ?? 0;
            if (na != nb) return false;

            for (int i = 0; i < na; i++)
            {
                var p = a[i] ?? new TaskCountDto();
                var q = b[i] ?? new TaskCountDto();
                if (!Same(p.goal, q.goal) || p.count != q.count) return false;
            }

            return true;
        }

        static bool SamePeriod(TaskPeriodDto a, TaskPeriodDto b)
        {
            var x = a ?? new TaskPeriodDto();
            var y = b ?? new TaskPeriodDto();
            if (x.key != y.key) return false;

            int na = x.counts?.Length ?? 0, nb = y.counts?.Length ?? 0;
            if (na != nb) return false;
            for (int i = 0; i < na; i++)
            {
                var p = x.counts[i] ?? new TaskCountDto();
                var q = y.counts[i] ?? new TaskCountDto();
                if (!Same(p.goal, q.goal) || p.count != q.count) return false;
            }

            int ca = x.claimed?.Length ?? 0, cb = y.claimed?.Length ?? 0;
            if (ca != cb) return false;
            for (int i = 0; i < ca; i++)
                if (!Same(x.claimed[i], y.claimed[i])) return false;

            return true;
        }

        static bool Same(string a, string b)
            => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
    }
}
