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
        /// How this chapter is played. Index knowledge in the strongest sense: the map has to
        /// know which mode a chapter belongs to before it opens the body, because that is what
        /// decides whether the chapter is on the switcher's current tab at all.
        /// </summary>
        public readonly GameMode Mode;

        /// <summary>
        /// Which ladder inside that mode this chapter belongs to.
        ///
        /// <b>Index knowledge for <see cref="Mode"/>'s reason and one more</b>: the ladder a
        /// chapter sits on decides what gates it and what it gates, and <c>LevelUnlock</c> has to
        /// answer that before any body is read. See <see cref="GameTrack"/> for why an endless
        /// lane is a track rather than a mode or an ordinary chapter.
        /// </summary>
        public readonly GameTrack Track;

        /// <summary>This chapter's lane: its mode and its track together.</summary>
        public ModeLane Lane => new ModeLane(Mode, Track);

        /// <summary>
        /// The keeper level this chapter asks for before it opens at all, or nought when it
        /// asks for none.
        ///
        /// <b>Index knowledge for <see cref="Track"/>'s reason</b>: <c>LevelUnlock</c> answers
        /// what is open for a whole lane at a time — at launch, and again every time the map is
        /// drawn — so a wall the index could not see would be one no screen could draw without
        /// pulling a chapter body in behind it. It is authored in the manifest
        /// (<c>ManifestChapterDto.minKeeperLevel</c>) rather than derived, because nothing about
        /// a chapter's levels implies how much of the game should be behind it.
        /// </summary>
        public readonly int MinKeeperLevel;

        /// <summary>
        /// Derived from the id by convention, so a chapter names itself once. The body
        /// may still override it, but the index needs a name before the body is read —
        /// a chapter carousel must be able to label a chapter it has never opened.
        /// </summary>
        public string NameKey => ChapterDefinition.DefaultNameKey(Id);

        public ChapterIndexEntry(ChapterId id, int order, int version, IReadOnlyList<LevelId> levelIds)
            : this(id, order, version, levelIds, GameMode.Default, GameTrack.Main) { }

        public ChapterIndexEntry(ChapterId id, int order, int version,
                                 IReadOnlyList<LevelId> levelIds, GameMode mode)
            : this(id, order, version, levelIds, mode, GameTrack.Main) { }

        public ChapterIndexEntry(ChapterId id, int order, int version,
                                 IReadOnlyList<LevelId> levelIds, GameMode mode, GameTrack track)
            : this(id, order, version, levelIds, mode, track, 0) { }

        public ChapterIndexEntry(ChapterId id, int order, int version,
                                 IReadOnlyList<LevelId> levelIds, GameMode mode, GameTrack track,
                                 int minKeeperLevel)
        {
            if (!id.IsValid) throw new ArgumentException("chapter needs a valid id", nameof(id));

            Id = id;
            Order = order;
            Version = version;
            LevelIds = levelIds ?? Array.Empty<LevelId>();
            Mode = mode.IsValid ? mode : GameMode.Default;
            Track = track;

            // Negative is nonsense rather than a sentinel, and reading it as "no wall" is the
            // safe direction: the alternative is a manifest typo that shuts a chapter nobody can
            // ever open. The content gates name it; a session never loses a lane to it.
            MinKeeperLevel = minKeeperLevel < 0 ? 0 : minKeeperLevel;
        }

        public int LevelCount => LevelIds.Count;

        public bool IsEmpty => LevelIds.Count == 0;

        public LevelId FirstLevel => LevelIds.Count > 0 ? LevelIds[0] : LevelId.None;

        public LevelId LastLevel => LevelIds.Count > 0 ? LevelIds[LevelIds.Count - 1] : LevelId.None;

        public override string ToString() => Id.Value;
    }
}
