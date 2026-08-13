using System;
using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// What the manifest knows about one chapter: its identity, where it sits, and
    /// which glades belong to it.
    ///
    /// Deliberately holds no art, no colours and no grids. Those live in the chapter
    /// body, which is only read when the player actually enters the chapter — the
    /// whole point of splitting the two is that this half can describe a five hundred
    /// chapter game in a few kilobytes and still be parsed on every launch.
    /// </summary>
    public sealed class ChapterIndexEntry
    {
        public readonly ChapterId Id;

        /// <summary>Sort order across chapters, from the manifest and nowhere else.</summary>
        public readonly int Order;

        /// <summary>Bumped when the body changes, so the cache knows to refetch it.</summary>
        public readonly int Version;

        /// <summary>Level ids in play order.</summary>
        public readonly IReadOnlyList<LevelId> LevelIds;

        /// <summary>
        /// Derived from the id by convention, so a chapter names itself once. The body
        /// may still override it, but the index needs a name before the body is read —
        /// a chapter carousel must be able to label a chapter it has never opened.
        /// </summary>
        public string NameKey => ChapterDefinition.DefaultNameKey(Id);

        public ChapterIndexEntry(ChapterId id, int order, int version, IReadOnlyList<LevelId> levelIds)
        {
            if (!id.IsValid) throw new ArgumentException("chapter needs a valid id", nameof(id));

            Id = id;
            Order = order;
            Version = version;
            LevelIds = levelIds ?? Array.Empty<LevelId>();
        }

        public int LevelCount => LevelIds.Count;

        public bool IsEmpty => LevelIds.Count == 0;

        public LevelId FirstLevel => LevelIds.Count > 0 ? LevelIds[0] : LevelId.None;

        public LevelId LastLevel => LevelIds.Count > 0 ? LevelIds[LevelIds.Count - 1] : LevelId.None;

        public override string ToString() => Id.Value;
    }
}
