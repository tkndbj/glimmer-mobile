using System;
using System.Collections.Generic;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// The shape of the whole game: every chapter, every glade id, in play order.
    ///
    /// This is the half of the catalog that is always resident, and it is built from
    /// the manifest alone. That is the decision that makes content scale: the boot
    /// path needs to answer "what exists, in what order, in which chapter" — to total
    /// stars, to derive XP, to find where the player is up to — and none of those
    /// questions need a grid, a backdrop or a par. Reading one small file answers all
    /// of them, so launching the game costs the same at chapter one hundred as at
    /// chapter one.
    ///
    /// Immutable. A content refresh publishes a new index rather than mutating this
    /// one, so nothing can observe a half-updated world.
    ///
    /// It is also the game's <see cref="IChapterMap"/>: deriving currency has to count
    /// only glades that genuinely exist, and this is precisely the set that exists.
    /// </summary>
    public sealed class CatalogIndex : IChapterMap
    {
        public static readonly CatalogIndex Empty =
            new CatalogIndex(Array.Empty<ChapterIndexEntry>(), Array.Empty<LevelId>(),
                             new Dictionary<LevelId, int>(), new Dictionary<LevelId, ChapterId>(),
                             Array.Empty<AvatarDefinition>(), Array.Empty<Events.GroveEvent>());

        readonly ChapterIndexEntry[] _chapters;
        readonly LevelId[] _levelIds;
        readonly Dictionary<LevelId, int> _levelOrder;
        readonly Dictionary<LevelId, ChapterId> _levelChapter;
        readonly Dictionary<ChapterId, ChapterIndexEntry> _chapterById;
        readonly AvatarDefinition[] _companions;
        readonly Events.GroveEvent[] _events;

        internal CatalogIndex(ChapterIndexEntry[] chapters, LevelId[] levelIds,
                              Dictionary<LevelId, int> levelOrder,
                              Dictionary<LevelId, ChapterId> levelChapter,
                              AvatarDefinition[] companions,
                              Events.GroveEvent[] events)
        {
            _chapters = chapters;
            _levelIds = levelIds;
            _levelOrder = levelOrder;
            _levelChapter = levelChapter;
            _companions = companions ?? Array.Empty<AvatarDefinition>();
            _events = events ?? Array.Empty<Events.GroveEvent>();

            _chapterById = new Dictionary<ChapterId, ChapterIndexEntry>(chapters.Length);
            foreach (var c in chapters) _chapterById[c.Id] = c;
        }

        /// <summary>
        /// The companion roster, in display order.
        ///
        /// Index knowledge in exactly the same sense the chapter list is: it comes from
        /// the manifest, it is small, and it is wanted everywhere without a file read.
        /// Empty when the manifest carried none, which leaves
        /// <see cref="AvatarCatalog"/> on the roster this build shipped with.
        /// </summary>
        public IReadOnlyList<AvatarDefinition> Companions => _companions;

        /// <summary>
        /// The event calendar, in start order, past and future alike.
        ///
        /// Index knowledge for a stronger reason than the companion roster is: an event's
        /// reward is derived from the star ledger, so every place that computes credits
        /// needs the whole calendar — including events that closed months ago, which still
        /// pay what they paid. A calendar that only held live events would take currency
        /// away from a player the day one ended.
        /// </summary>
        public IReadOnlyList<Events.GroveEvent> Events => _events;

        /// <summary>The event running at <paramref name="nowUnix"/>, or null.</summary>
        public Events.GroveEvent LiveEventAt(long nowUnix)
        {
            for (int i = 0; i < _events.Length; i++)
                if (_events[i].IsLiveAt(nowUnix)) return _events[i];

            return null;
        }

        // ------------------------------------------------------------- chapters
        /// <summary>Every chapter, in play order.</summary>
        public IReadOnlyList<ChapterIndexEntry> Chapters => _chapters;

        public int ChapterCount => _chapters.Length;

        public ChapterIndexEntry FindChapter(ChapterId id)
            => _chapterById.TryGetValue(id, out var c) ? c : null;

        public bool ContainsChapter(ChapterId id) => _chapterById.ContainsKey(id);

        public ChapterIndexEntry FirstChapter => _chapters.Length > 0 ? _chapters[0] : null;

        /// <summary>Position of a chapter in play order, or -1. Display only.</summary>
        public int ChapterOrderOf(ChapterId id)
        {
            for (int i = 0; i < _chapters.Length; i++)
                if (_chapters[i].Id == id) return i;
            return -1;
        }

        /// <summary>The chapter <paramref name="step"/> places away, or null at either end.</summary>
        public ChapterIndexEntry ChapterNeighbour(ChapterId id, int step)
        {
            int i = ChapterOrderOf(id);
            if (i < 0) return null;

            int j = i + step;
            return j >= 0 && j < _chapters.Length ? _chapters[j] : null;
        }

        /// <summary>Level ids of one chapter, in order. Empty for an unknown chapter.</summary>
        public IReadOnlyList<LevelId> LevelsOf(ChapterId chapter)
            => FindChapter(chapter)?.LevelIds ?? Array.Empty<LevelId>();

        // --------------------------------------------------------------- levels
        /// <summary>Every level id, flattened into play order across every chapter.</summary>
        public IReadOnlyList<LevelId> LevelIds => _levelIds;

        public int Count => _levelIds.Length;
        public bool IsEmpty => _levelIds.Length == 0;

        public bool Contains(LevelId id) => _levelOrder.ContainsKey(id);

        /// <summary>
        /// Zero-based position in play order, or -1. Use this for display numbering and
        /// for nothing else — never persist it. Position moves when a chapter is
        /// inserted; a <see cref="LevelId"/> never does.
        /// </summary>
        public int OrderOf(LevelId id) => _levelOrder.TryGetValue(id, out int i) ? i : -1;

        public LevelId At(int order)
            => order >= 0 && order < _levelIds.Length ? _levelIds[order] : LevelId.None;

        public LevelId First => _levelIds.Length > 0 ? _levelIds[0] : LevelId.None;
        public LevelId Last => _levelIds.Length > 0 ? _levelIds[_levelIds.Length - 1] : LevelId.None;

        public LevelId Next(LevelId id)
        {
            int i = OrderOf(id);
            return i >= 0 && i + 1 < _levelIds.Length ? _levelIds[i + 1] : LevelId.None;
        }

        public LevelId Previous(LevelId id)
        {
            int i = OrderOf(id);
            return i > 0 ? _levelIds[i - 1] : LevelId.None;
        }

        public bool IsLast(LevelId id) => _levelIds.Length > 0 && OrderOf(id) == _levelIds.Length - 1;

        /// <summary>The chapter a level belongs to. Also satisfies <see cref="IChapterMap"/>.</summary>
        public bool TryGetChapter(LevelId level, out ChapterId chapter)
            => _levelChapter.TryGetValue(level, out chapter);

        public ChapterId ChapterOf(LevelId level)
            => _levelChapter.TryGetValue(level, out var chapter) ? chapter : ChapterId.None;
    }
}
