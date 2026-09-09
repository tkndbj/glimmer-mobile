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
        readonly Dictionary<ModeLane, LevelId[]> _byLane;
        readonly Dictionary<ModeLane, ChapterIndexEntry[]> _chaptersByLane;
        readonly GameMode[] _modes;

        internal CatalogIndex(ChapterIndexEntry[] chapters, LevelId[] levelIds,
                              Dictionary<LevelId, int> levelOrder,
                              Dictionary<LevelId, ChapterId> levelChapter,
                              AvatarDefinition[] companions,
                              Events.GroveEvent[] events,
                              Dictionary<LevelId, GameMode> levelMode,
                              Dictionary<ModeLane, List<LevelId>> byLane,
                              Dictionary<ModeLane, List<ChapterIndexEntry>> chaptersByLane)
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

            _byLane = Freeze(byLane);
            _chaptersByLane = Freeze(chaptersByLane);

            // Offered in the order the modes shipped rather than in the order chapters happen
            // to appear, so the switcher never reorders itself under a thumb reaching for the
            // entry that was there yesterday. A mode with no chapters in this catalog is not
            // on the list at all - an empty tab is a promise the content did not keep.
            // A mode is on the switcher when it has a chapter on <em>any</em> track, so a mode
            // that shipped nothing but an endless lane would still be offered. Nothing does that
            // today, and the alternative - listing a mode only for its main ladder - would be a
            // rule that silently decides a content question.
            var modes = new List<GameMode>();
            foreach (var mode in GameMode.Shipped)
                foreach (var track in GameTrack.Shipped)
                    if (_chaptersByLane.ContainsKey(new ModeLane(mode, track)))
                    {
                        modes.Add(mode);
                        break;
                    }

            _modes = modes.ToArray();
        }

        static Dictionary<ModeLane, T[]> Freeze<T>(Dictionary<ModeLane, List<T>> source)
        {
            var frozen = new Dictionary<ModeLane, T[]>();
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
        /// The mode a screen opens on when nothing has said which: the first entry of
        /// <see cref="Modes"/>, which is the first row of the switcher.
        ///
        /// <para>
        /// <b>The front door and the top of the list are one answer on purpose.</b> They were
        /// briefly two — this read "the classic mode when the catalog has it, else the first" —
        /// and two answers means a map that opens on one mode while the control above it offers
        /// a different one first, which is a difference nobody could explain and nothing would
        /// have caught. Which mode leads is decided once, in <see cref="LevelModes"/>, and read
        /// here.
        /// </para>
        /// <para>
        /// <b>Not <see cref="GameMode.Default"/>, and the difference is the whole point.</b>
        /// That constant answers a question about <em>parsing</em>: a chapter with no
        /// <c>mode</c> field is a glade, for ever, so that every chapter authored before modes
        /// existed keeps working with its file untouched. This one answers a question about
        /// <em>this catalog</em>, and the two part company twice over — the front door is
        /// Thornwatch now, and the classic mode can have no chapters at all (every glade chapter
        /// disabled from a config push, a client rolled back, a drop that has not downloaded). A
        /// screen that took the parsing answer would open onto a mode with nothing in it, which
        /// is a blank map with a back arrow.
        /// </para>
        /// <para>
        /// <see cref="GameMode.None"/> for an empty catalog, which is a content failure the
        /// caller has to be able to see rather than one dressed up as a mode.
        /// </para>
        /// </summary>
        public GameMode DefaultMode => _modes.Length > 0 ? _modes[0] : GameMode.None;

        /// <summary>
        /// How a glade is played. <see cref="GameMode.Default"/> for one the catalog has never
        /// heard of, which is the same forgiving answer <see cref="ChapterOf"/> gives.
        /// </summary>
        public GameMode ModeOf(LevelId level)
            => _levelMode.TryGetValue(level, out var mode) ? mode : GameMode.Default;

        /// <summary>
        /// One mode's chapters on the <b>ordinary</b> ladder, in play order.
        ///
        /// <b>The main track and nothing else, deliberately.</b> Everything that means "what comes
        /// next" walks this - the map's arrows, the chapter gate, where the player is up to - so
        /// answering with an endless chapter too would let a lane whose waves never stop gate a
        /// real chapter on stars nobody can earn. A caller that wants another lane asks for it by
        /// name (<see cref="ChaptersIn(GameMode, GameTrack)"/>).
        /// </summary>
        public IReadOnlyList<ChapterIndexEntry> ChaptersIn(GameMode mode)
            => ChaptersIn(mode, GameTrack.Main);

        /// <summary>One lane's chapters, in play order.</summary>
        public IReadOnlyList<ChapterIndexEntry> ChaptersIn(GameMode mode, GameTrack track)
            => _chaptersByLane.TryGetValue(new ModeLane(mode, track), out var list)
             ? list : NoChapters;

        /// <summary>One mode's glades on the ordinary ladder, flattened into play order.</summary>
        public IReadOnlyList<LevelId> LevelsIn(GameMode mode) => LevelsIn(mode, GameTrack.Main);

        /// <summary>One lane's glades, flattened into play order across its chapters.</summary>
        public IReadOnlyList<LevelId> LevelsIn(GameMode mode, GameTrack track)
            => _byLane.TryGetValue(new ModeLane(mode, track), out var list) ? list : NoLevels;

        /// <summary>
        /// Which ladders this mode has chapters on, in the order a switcher offers them.
        ///
        /// <b>A written order rather than whatever the dictionary walks</b>, which is invariant
        /// 38a's rule for the mode switcher: a control that reorders itself moves the entry
        /// somebody reaches for without looking.
        /// </summary>
        public IReadOnlyList<GameTrack> TracksIn(GameMode mode)
        {
            var found = new List<GameTrack>(GameTrack.Shipped.Length);

            foreach (var track in GameTrack.Shipped)
                if (_chaptersByLane.ContainsKey(new ModeLane(mode, track))) found.Add(track);

            return found;
        }

        /// <summary>Which ladder this chapter is on. The main one for a chapter we do not know.</summary>
        public GameTrack TrackOf(ChapterId chapter)
            => FindChapter(chapter)?.Track ?? GameTrack.Main;

        /// <summary>Which ladder this level's chapter is on.</summary>
        public GameTrack TrackOf(LevelId level) => TrackOf(ChapterOf(level));

        /// <summary>The chapter a mode's map opens on when nothing else says otherwise.</summary>
        public ChapterIndexEntry FirstChapterIn(GameMode mode)
        {
            var list = ChaptersIn(mode);
            return list.Count > 0 ? list[0] : null;
        }

        LevelId[] Lane(LevelId id)
            => _byLane.TryGetValue(new ModeLane(ModeOf(id), TrackOf(id)), out var lane)
             ? lane : NoLevels;

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

            var lane = ChaptersIn(entry.Mode, entry.Track);
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

            var lane = ChaptersIn(entry.Mode, entry.Track);
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
