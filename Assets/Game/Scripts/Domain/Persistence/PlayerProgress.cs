using System;
using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// What the player has cleared, keyed by level id.
    ///
    /// Every method here takes a <see cref="LevelId"/>. That is the whole design: a
    /// level's position can change with any content update, its identity cannot, so
    /// records stay attached to the level a player actually played.
    /// </summary>
    public static class PlayerProgress
    {
        static readonly Dictionary<LevelId, LevelRecord> _records = new Dictionary<LevelId, LevelRecord>();

        public static LevelId LastPlayed { get; private set; }

        /// <summary>Raised after a run is recorded, so screens can refresh.</summary>
        public static event Action<LevelRecord> RecordChanged;

        /// <summary>
        /// Raised when the whole record set is replaced rather than added to — a load,
        /// a wipe, or a merge with another device. Anything caching a total derived
        /// from these records has to recompute, and no per-record event fires to tell
        /// it so.
        /// </summary>
        public static event Action Reloaded;

        /// <summary>
        /// Raised when a freshly published population promoted at least one glade's
        /// standing, so a screen drawing bands can repaint.
        ///
        /// <para>
        /// Separate from <see cref="Reloaded"/> because nothing about the player's own play
        /// changed — no stars, no move counts, no totals — and separate from
        /// <see cref="RecordChanged"/> because a sweep can touch a whole catalog at once and
        /// a per-record event would be thousands of callbacks to redraw one screen.
        /// </para>
        /// </summary>
        public static event Action RanksChanged;

        /// <summary>
        /// Standings are backfilled whenever a table arrives, which is normally once a
        /// session and always after this class has been touched.
        ///
        /// <para>
        /// Subscribed here rather than from a screen because the sweep has to happen whatever
        /// the player is looking at: the table lands while the splash is up, and a map opened
        /// two minutes later reads the save, not the event. <see cref="LoadFrom"/> sweeps too,
        /// which covers the other order — a save adopted from the cloud after the table was
        /// already published. Both are safe to run any number of times because a standing only
        /// ever climbs.
        /// </para>
        /// </summary>
        static PlayerProgress()
        {
            Social.GroveStats.Changed += RefreshRanks;
        }

        // ------------------------------------------------------------- reading
        public static LevelRecord Record(LevelId id)
            => _records.TryGetValue(id, out var record) ? record : LevelRecord.Empty(id);

        /// <summary>
        /// Every record held, including levels no longer in the catalog. Progression
        /// is derived from these rather than from the catalog, so a chapter hidden by
        /// <c>minAppVersion</c> cannot take back what a player already earned.
        /// </summary>
        public static IReadOnlyCollection<LevelRecord> Records => _records.Values;

        /// <summary>
        /// The same records, keyed by level id.
        ///
        /// For readers that ask about a named set of glades rather than about all of them —
        /// an event's track is the first — where walking the whole collection once per
        /// question is the same work multiplied by however many questions there are.
        /// </summary>
        public static IReadOnlyDictionary<LevelId, LevelRecord> RecordsById => _records;

        /// <summary>
        /// How many glades have been finished, counted off the records alone.
        ///
        /// <para>
        /// Deliberately <em>not</em> <c>PlayerProgression.ClearedGlades</c>, which is the same
        /// question asked of the reward arithmetic and so drops any record the catalog has
        /// never heard of — right there, because an unrecognised level must never mint credits,
        /// and wrong for a screen. The account panel asks this after a switch to say "welcome
        /// back · 26 finished levels", and that sentence must not depend on whether the content
        /// index happens to have loaded yet: a player who is told they arrived at an empty
        /// grove, and then watches it fill in a second later, has been given exactly the fright
        /// this whole flow was rewritten to stop giving.
        /// </para>
        /// </summary>
        public static int ClearedCount
        {
            get
            {
                int cleared = 0;
                foreach (var record in _records.Values) if (record.IsCleared) cleared++;
                return cleared;
            }
        }

        public static int Stars(LevelId id) => Record(id).Stars;

        public static int BestMoves(LevelId id) => Record(id).BestMoves;

        /// <summary>
        /// Best standing ever held on this glade. Read from the record rather than computed,
        /// so a map node draws instantly, offline, with no population in hand.
        /// </summary>
        public static int BestRank(LevelId id) => Record(id).BestRank;

        public static bool IsCleared(LevelId id) => Record(id).IsCleared;

        // Every question below is asked of the index rather than of the catalog, and
        // that is the point: totalling stars, checking completion and finding where the
        // player is up to need only to know which glades exist and in what order. None
        // of them needs a grid or a backdrop, so none of them should be able to cause a
        // chapter body to be read — least of all on the boot path, where all three run.

        /// <summary>Stars earned across the levels currently in the catalog.</summary>
        public static int TotalStars(CatalogIndex index)
        {
            if (index == null) return 0;

            int total = 0;
            foreach (var id in index.LevelIds) total += Stars(id);
            return total;
        }

        public static int MaxStars(CatalogIndex index) => (index?.Count ?? 0) * 3;

        /// <summary>Stars earned in one chapter, and the most that chapter can hold.</summary>
        /// <remarks>
        /// The chapter overloads, not a second reading of the whole catalog filtered down.
        /// A chapter entry already carries its level ids in play order — that is the whole
        /// of what the manifest is for — so this is the same walk over a shorter list, and
        /// it stays index knowledge: no body is read and no grid is parsed to total a
        /// chapter's stars.
        /// </remarks>
        public static int TotalStars(ChapterIndexEntry chapter)
        {
            if (chapter == null) return 0;

            int total = 0;
            var ids = chapter.LevelIds;
            for (int i = 0; i < ids.Count; i++) total += Stars(ids[i]);
            return total;
        }

        public static int MaxStars(ChapterIndexEntry chapter) => (chapter?.LevelCount ?? 0) * 3;

        public static bool AllCleared(CatalogIndex index)
        {
            if (index == null || index.IsEmpty) return false;

            foreach (var id in index.LevelIds)
                if (!IsCleared(id)) return false;
            return true;
        }

        /// <summary>The first catalogued level the player has not cleared yet.</summary>
        public static LevelId FirstUncleared(CatalogIndex index)
        {
            if (index == null) return LevelId.None;

            foreach (var id in index.LevelIds)
                if (!IsCleared(id)) return id;
            return LevelId.None;
        }

        // ------------------------------------------------------------- writing
        /// <summary>Folds a finished run in. Returns true when it beat the old record.</summary>
        public static bool RecordRun(LevelId id, int stars, int moves)
        {
            if (!id.IsValid) return false;

            var before = Record(id);
            bool improved = before.Improves(stars, moves);

            // Ranked here because this is where the new best is decided. If the table has
            // not arrived — no backend, offline, a launch that has not reached the fetch yet
            // — nothing is captured and nothing is lost: the move count is stored either
            // way, and RefreshRanks works the standing out from it later.
            var after = before.WithRun(stars, moves, SaveSchema.NowUnix(), Social.GroveStats.For(id));

            _records[id] = after;
            LastPlayed = id;

            SaveService.Save();

            try { RecordChanged?.Invoke(after); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }

            return improved;
        }

        public static void NoteOpened(LevelId id)
        {
            if (!id.IsValid || LastPlayed == id) return;
            LastPlayed = id;
            SaveService.MarkDirty();
        }

        /// <summary>
        /// Re-ranks every held record against the current published population, keeping
        /// whichever standing is better.
        ///
        /// <para>
        /// This is the migration, and it is why v13 needed no migration code: every standing
        /// is derived from a <c>bestMoves</c> that was already on disk, so the first table to
        /// land fills in a player's entire history at once. A year-old account earns its bands
        /// on the launch it updates, rather than only on glades it happens to replay.
        /// </para>
        /// <para>
        /// Idempotent, and cheap enough not to need scheduling: a dictionary walk with one
        /// interpolation each, once a session. Marks the file dirty rather than saving, so a
        /// sweep during boot does not force a write on a player who is about to get one
        /// anyway — and does neither when nothing moved, which is the common case from the
        /// second session on.
        /// </para>
        /// </summary>
        public static void RefreshRanks()
        {
            if (!PromoteRanks()) return;

            SaveService.MarkDirty();

            try { RanksChanged?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        /// <summary>
        /// The sweep itself. Returns whether anything moved, and raises nothing — so a load
        /// can promote quietly under cover of <see cref="Reloaded"/> instead of firing two
        /// events that mean "redraw" back to back.
        /// </summary>
        static bool PromoteRanks()
        {
            if (_records.Count == 0) return false;

            bool changed = false;

            // Keys copied first: the values are replaced as we go, and mutating a dictionary
            // while enumerating it throws even when only the values change.
            var ids = new LevelId[_records.Count];
            _records.Keys.CopyTo(ids, 0);

            foreach (var id in ids)
            {
                var held = _records[id];
                var promoted = held.WithRank(Social.GroveStats.For(id));
                if (ReferenceEquals(promoted, held)) continue;

                _records[id] = promoted;
                changed = true;
            }

            return changed;
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            _records.Clear();
            LastPlayed = LevelId.None;

            if (dto.levels != null)
            {
                foreach (var entry in dto.levels)
                    if (LevelRecord.TryFromDto(entry, out var record))
                        _records[record.Id] = record;
            }

            if (LevelId.TryParse(dto.lastPlayedLevelId, out var last, out _)) LastPlayed = last;

            // Covers the other order: a save adopted after the day's table had already been
            // published — a cloud pull, a linked account, the boot sequence on a fast network
            // — would otherwise wait for tomorrow's fetch to earn its bands. Quiet, because
            // Reloaded below already tells every listener to recompute.
            if (PromoteRanks()) SaveService.MarkDirty();

            try { Reloaded?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            var entries = new LevelRecordDto[_records.Count];
            int i = 0;
            foreach (var record in _records.Values) entries[i++] = record.ToDto();

            dto.levels = entries;
            dto.lastPlayedLevelId = LastPlayed.Value;
        }
    }
}
