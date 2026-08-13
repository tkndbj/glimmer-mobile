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

        // ------------------------------------------------------------- reading
        public static LevelRecord Record(LevelId id)
            => _records.TryGetValue(id, out var record) ? record : LevelRecord.Empty(id);

        public static int Stars(LevelId id) => Record(id).Stars;

        public static int BestMoves(LevelId id) => Record(id).BestMoves;

        public static bool IsCleared(LevelId id) => Record(id).IsCleared;

        /// <summary>Stars earned across the levels currently in the catalog.</summary>
        public static int TotalStars(LevelCatalog catalog)
        {
            int total = 0;
            foreach (var level in catalog.Levels) total += Stars(level.Id);
            return total;
        }

        public static int MaxStars(LevelCatalog catalog) => catalog.Count * 3;

        public static bool AllCleared(LevelCatalog catalog)
        {
            if (catalog.IsEmpty) return false;
            foreach (var level in catalog.Levels)
                if (!IsCleared(level.Id)) return false;
            return true;
        }

        /// <summary>The first catalogued level the player has not cleared yet.</summary>
        public static LevelDefinition FirstUncleared(LevelCatalog catalog)
        {
            foreach (var level in catalog.Levels)
                if (!IsCleared(level.Id)) return level;
            return null;
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
