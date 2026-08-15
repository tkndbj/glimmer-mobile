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

        public static int Stars(LevelId id) => Record(id).Stars;

        public static int BestMoves(LevelId id) => Record(id).BestMoves;

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

            var after = before.WithRun(stars, moves, SaveSchema.NowUnix());
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
