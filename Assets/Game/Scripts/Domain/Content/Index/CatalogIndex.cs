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
                             Array.Empty<AvatarDefinition>(), Array.Empty<Events.GroveEvent>(),
                             null, null, null);

        static readonly LevelId[] NoLevels = Array.Empty<LevelId>();
        static readonly ChapterIndexEntry[] NoChapters = Array.Empty<ChapterIndexEntry>();

        readonly ChapterIndexEntry[] _chapters;
        readonly LevelId[] _levelIds;
        readonly Dictionary<LevelId, int> _levelOrder;
        readonly Dictionary<LevelId, ChapterId> _levelChapter;
        readonly Dictionary<ChapterId, ChapterIndexEntry> _chapterById;
        readonly AvatarDefinition[] _companions;
        readonly Events.GroveEvent[] _events;

        readonly Dictionary<LevelId, GameMode> _levelMode;
        readonly Dictionary<GameMode, LevelId[]> _byMode;
        readonly Dictionary<GameMode, ChapterIndexEntry[]> _chaptersByMode;
        readonly GameMode[] _modes;

        internal CatalogIndex(ChapterIndexEntry[] chapters, LevelId[] levelIds,
                              Dictionary<LevelId, int> levelOrder,
                              Dictionary<LevelId, ChapterId> levelChapter,
                              AvatarDefinition[] companions,
                              Events.GroveEvent[] events,
                              Dictionary<LevelId, GameMode> levelMode,
                              Dictionary<GameMode, List<LevelId>> byMode,
                              Dictionary<GameMode, List<ChapterIndexEntry>> chaptersByMode)
        {
            _chapters = chapters;
            _levelIds = levelIds;
            _levelOrder = levelOrder;
            _levelChapter = levelChapter;
            _companions = companions ?? Array.Empty<AvatarDefinition>();
            _events = events ?? Array.Empty<Events.GroveEvent>();
            _levelMode = levelMode ?? new Dictionary<LevelId, GameMode>();

            _chapterById = new Dictionary<ChapterId, ChapterIndexEntry>(chapters.Length);
            foreach (var c in chapters) _chapterById[c.Id] = c;

            _byMode = Freeze(byMode);
            _chaptersByMode = Freeze(chaptersByMode);

            // Offered in the order the modes shipped rather than in the order chapters happen
            // to appear, so the switcher never reorders itself under a thumb reaching for the
            // entry that was there yesterday. A mode with no chapters in this catalog is not
            // on the list at all - an empty tab is a promise the content did not keep.
            var modes = new List<GameMode>();
            foreach (var mode in GameMode.Shipped)
                if (_chaptersByMode.ContainsKey(mode)) modes.Add(mode);
            _modes = modes.ToArray();
        }

        static Dictionary<GameMode, T[]> Freeze<T>(Dictionary<GameMode, List<T>> source)
        {
            var frozen = new Dictionary<GameMode, T[]>();
            if (source == null) return frozen;

            foreach (var pair in source) frozen[pair.Key] = pair.Value.ToArray();
            return frozen;
        }

        // ---------------------------------------------------------------- modes
        /// <summary>
        /// Every mode this catalog has chapters for, in the order the switcher offers them.
        /// </summary>
        public IReadOnlyList<GameMode> Modes => _modes;

        /// <summary>Whether more than one way of playing exists, which is what earns the switcher.</summary>
        public bool HasSeveralModes => _modes.Length > 1;

        /// <summary>
        /// How a glade is played. <see cref="GameMode.Default"/> for one the catalog has never
        /// heard of, which is the same forgiving answer <see cref="ChapterOf"/> gives.
        /// </summary>
        public GameMode ModeOf(LevelId level)
            => _levelMode.TryGetValue(level, out var mode) ? mode : GameMode.Default;

        /// <summary>One mode's chapters, in play order.</summary>
        public IReadOnlyList<ChapterIndexEntry> ChaptersIn(GameMode mode)
            => _chaptersByMode.TryGetValue(mode, out var list) ? list : NoChapters;

        /// <summary>One mode's glades, flattened into play order across its chapters.</summary>
        public IReadOnlyList<LevelId> LevelsIn(GameMode mode)
            => _byMode.TryGetValue(mode, out var list) ? list : NoLevels;

        /// <summary>The chapter a mode's map opens on when nothing else says otherwise.</summary>
        public ChapterIndexEntry FirstChapterIn(GameMode mode)
        {
            var list = ChaptersIn(mode);
            return list.Count > 0 ? list[0] : null;
        }

        LevelId[] Lane(LevelId id)
            => _byMode.TryGetValue(ModeOf(id), out var lane) ? lane : NoLevels;

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

        /// <summary>
        /// Position of a chapter among the ones played the same way, or -1. Display only.
        ///
        /// Within its own mode rather than across the catalog, because that is the number a
        /// player is looking at: the second wisp chapter is the second one they meet, whatever
        /// it happens to sit behind in the file.
        /// </summary>
        public int ChapterOrderOf(ChapterId id)
        {
            var entry = FindChapter(id);
            if (entry == null) return -1;

            var lane = ChaptersIn(entry.Mode);
            for (int i = 0; i < lane.Count; i++)
                if (lane[i].Id == id) return i;
            return -1;
        }

        /// <summary>
        /// The chapter <paramref name="step"/> places away in the same mode, or null at either
        /// end. This is what the map's arrows walk, so they never step out of the mode the
        /// player chose.
        /// </summary>
        public ChapterIndexEntry ChapterNeighbour(ChapterId id, int step)
        {
            var entry = FindChapter(id);
            if (entry == null) return null;

            var lane = ChaptersIn(entry.Mode);
            int i = ChapterOrderOf(id);
            if (i < 0) return null;

            int j = i + step;
            return j >= 0 && j < lane.Count ? lane[j] : null;
        }

        /// <summary>Level ids of one chapter, in order. Empty for an unknown chapter.</summary>
        public IReadOnlyList<LevelId> LevelsOf(ChapterId chapter)
            => FindChapter(chapter)?.LevelIds ?? Array.Empty<LevelId>();

        // --------------------------------------------------------------- levels
        /// <summary>
        /// Every level id in the game, across every mode.
        ///
        /// This is the set totals are taken over - stars, XP, earned credits - and it is
        /// deliberately mode-blind: a glade is a glade whichever way it is played, and the
        /// reward path (see <c>ProgressionLedger</c>) has no opinion about modes, which is the
        /// whole reason a second one needed no server work. It is <em>not</em> a play order;
        /// for that see <see cref="LevelsIn"/>, <see cref="Next"/> and <see cref="Previous"/>,
        /// all of which stay inside one mode.
        /// </summary>
        public IReadOnlyList<LevelId> LevelIds => _levelIds;

        public int Count => _levelIds.Length;
        public bool IsEmpty => _levelIds.Length == 0;

        public bool Contains(LevelId id) => _levelOrder.ContainsKey(id);

        /// <summary>
        /// Zero-based position within its own mode's play order, or -1. Use this for display
        /// numbering and for nothing else — never persist it. Position moves when a chapter is
        /// inserted; a <see cref="LevelId"/> never does.
        /// </summary>
        public int OrderOf(LevelId id) => _levelOrder.TryGetValue(id, out int i) ? i : -1;

        /// <summary>The nth glade of one mode, in play order.</summary>
        public LevelId At(GameMode mode, int order)
        {
            var lane = LevelsIn(mode);
            return order >= 0 && order < lane.Count ? lane[order] : LevelId.None;
        }

        /// <summary>The nth glade of the ordinary mode. Kept for dev tools and old call sites.</summary>
        public LevelId At(int order) => At(GameMode.Default, order);

        public LevelId FirstIn(GameMode mode)
        {
            var lane = LevelsIn(mode);
            return lane.Count > 0 ? lane[0] : LevelId.None;
        }

        public LevelId LastIn(GameMode mode)
        {
            var lane = LevelsIn(mode);
            return lane.Count > 0 ? lane[lane.Count - 1] : LevelId.None;
        }

        public LevelId First => FirstIn(GameMode.Default);
        public LevelId Last => LastIn(GameMode.Default);

        /// <summary>
        /// The glade after this one <em>in the same mode</em>, or none at the end of it.
        ///
        /// Staying inside the mode is what makes the two ladders independent, and it is one
        /// rule rather than a rule per caller: the unlock, the map's numbering, the victory
        /// panel's onward button and "where was I up to" all reduce to this and its neighbour.
        /// Chained end to end instead, finishing the classic game would be the price of
        /// opening the second one.
        /// </summary>
        public LevelId Next(LevelId id)
        {
            var lane = Lane(id);
            int i = OrderOf(id);
            return i >= 0 && i + 1 < lane.Length ? lane[i + 1] : LevelId.None;
        }

        public LevelId Previous(LevelId id)
        {
            var lane = Lane(id);
            int i = OrderOf(id);
            return i > 0 && i <= lane.Length ? lane[i - 1] : LevelId.None;
        }

        public bool IsLast(LevelId id)
        {
            var lane = Lane(id);
            return lane.Length > 0 && OrderOf(id) == lane.Length - 1;
        }

        /// <summary>The chapter a level belongs to. Also satisfies <see cref="IChapterMap"/>.</summary>
        public bool TryGetChapter(LevelId level, out ChapterId chapter)
            => _levelChapter.TryGetValue(level, out chapter);

        public ChapterId ChapterOf(LevelId level)
            => _levelChapter.TryGetValue(level, out var chapter) ? chapter : ChapterId.None;
    }
}
