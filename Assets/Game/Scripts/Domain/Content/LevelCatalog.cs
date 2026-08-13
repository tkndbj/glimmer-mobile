using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Every level the game knows about — its shape always, its contents on request.
    ///
    /// The catalog is two halves with different costs, and this is the façade that
    /// keeps callers from having to care which half they are touching:
    ///
    /// <list type="bullet">
    /// <item><see cref="Index"/> is built from the manifest and is always resident. It
    /// answers identity, order and membership, which is everything the boot path,
    /// progression and the save file need.</item>
    /// <item>A <see cref="ChapterBody"/> holds grids, tuning and art keys. It is read
    /// when the player enters a chapter and evicted when they leave, so parsed content
    /// is bounded by a constant rather than by the size of the catalog.</item>
    /// </list>
    ///
    /// This type deliberately contains no logic of its own — ordering lives in
    /// <see cref="CatalogIndex"/>, reading in <see cref="ChapterLoader"/>, and the
    /// keep-or-drop rule in <see cref="ChapterResidency"/>. It exists so the fifteen
    /// call sites that had a catalog before still have one, and so that swapping any of
    /// those three parts is invisible to them.
    /// </summary>
    public sealed class LevelCatalog
    {
        public static readonly LevelCatalog Empty = new LevelCatalog(CatalogIndex.Empty, null, null);

        readonly CatalogIndex _index;
        readonly ChapterLoader _loader;
        readonly ChapterResidency _residency;

        /// <summary>In-flight loads, so two screens asking at once cause one fetch.</summary>
        readonly Dictionary<ChapterId, Task<ChapterBody>> _inFlight =
            new Dictionary<ChapterId, Task<ChapterBody>>();

        internal LevelCatalog(CatalogIndex index, ChapterLoader loader, ChapterResidency residency)
        {
            _index = index ?? CatalogIndex.Empty;
            _loader = loader;
            _residency = residency ?? new ChapterResidency();
        }

        /// <summary>
        /// A catalog whose bodies are already in hand and never need fetching. Used by
        /// the Editor tools and the tests, which want the whole game at once and can
        /// afford it; the game itself never takes this path.
        /// </summary>
        public static LevelCatalog FromLoaded(CatalogIndex index, IEnumerable<ChapterBody> bodies)
        {
            var list = new List<ChapterBody>();
            if (bodies != null) list.AddRange(bodies);

            var residency = new ChapterResidency(Math.Max(1, list.Count));
            foreach (var body in list) residency.Put(body);

            return new LevelCatalog(index ?? CatalogIndex.Empty, null, residency);
        }

        /// <summary>
        /// Raised when reading a chapter body turns up something wrong. Lazy loading
        /// means these arrive on entering a chapter rather than at boot, so there has
        /// to be somewhere for them to go that is not a decision made inside a data
        /// type — <c>ContentBootstrap</c> subscribes and logs.
        /// </summary>
        public event Action<string> ProblemReported;

        // ------------------------------------------------------- the index half
        public CatalogIndex Index => _index;

        public IReadOnlyList<ChapterIndexEntry> Chapters => _index.Chapters;
        public IReadOnlyList<LevelId> LevelIds => _index.LevelIds;

        public int Count => _index.Count;
        public bool IsEmpty => _index.IsEmpty;

        public bool Contains(LevelId id) => _index.Contains(id);
        public int OrderOf(LevelId id) => _index.OrderOf(id);
        public LevelId At(int order) => _index.At(order);
        public LevelId First => _index.First;
        public LevelId Last => _index.Last;
        public LevelId Next(LevelId id) => _index.Next(id);
        public LevelId Previous(LevelId id) => _index.Previous(id);
        public bool IsLast(LevelId id) => _index.IsLast(id);

        public ChapterId ChapterOf(LevelId level) => _index.ChapterOf(level);
        public ChapterIndexEntry FindChapter(ChapterId id) => _index.FindChapter(id);
        public IReadOnlyList<LevelId> LevelsOf(ChapterId chapter) => _index.LevelsOf(chapter);

        // -------------------------------------------------------- the body half
        /// <summary>
        /// The chapter's body, fetching it if it is not resident. Returns null when it
        /// cannot be read at all, having reported why.
        /// </summary>
        public async Task<ChapterBody> ChapterAsync(ChapterId id, CancellationToken cancellation = default)
        {
            if (!id.IsValid) return null;
            if (_residency.TryGet(id, out var resident)) return resident;

            // Two screens can want the same chapter in the same frame; one fetch.
            if (_inFlight.TryGetValue(id, out var pending)) return await pending;
            if (_loader == null) return null;

            var task = FetchAsync(id, cancellation);
            _inFlight[id] = task;

            try { return await task; }
            finally { _inFlight.Remove(id); }
        }

        async Task<ChapterBody> FetchAsync(ChapterId id, CancellationToken cancellation)
        {
            var result = await _loader.LoadAsync(id, cancellation);

            foreach (var problem in result.Problems) ProblemReported?.Invoke(problem);

            if (result.Body != null) _residency.Put(result.Body);
            return result.Body;
        }

        /// <summary>The level's full definition, fetching its chapter if it is not resident.</summary>
        public async Task<LevelDefinition> LevelAsync(LevelId id, CancellationToken cancellation = default)
        {
            if (!_index.TryGetChapter(id, out var chapter)) return null;

            var body = await ChapterAsync(chapter, cancellation);
            return body?.Find(id);
        }

        /// <summary>
        /// The chapter's body if it happens to be loaded already. For call sites that
        /// genuinely cannot await — never as a substitute for entering a chapter
        /// properly, which is what puts the body there in the first place.
        /// </summary>
        public bool TryResidentChapter(ChapterId id, out ChapterBody body)
            => _residency.TryGet(id, out body);

        public bool TryResidentLevel(LevelId id, out LevelDefinition level)
        {
            level = null;

            return _index.TryGetChapter(id, out var chapter)
                && _residency.TryGet(chapter, out var body)
                && body.TryFind(id, out level);
        }

        /// <summary>
        /// Bodies that happen to be loaded, in index order rather than in whatever order
        /// they were read. Deterministic on purpose — the Editor tools that group assets
        /// and validate content iterate this, and a run whose output depends on dictionary
        /// ordering is a run that cannot be diffed.
        /// </summary>
        public IEnumerable<ChapterBody> LoadedBodies()
        {
            foreach (var chapter in _index.Chapters)
            {
                var body = _residency.Peek(chapter.Id);
                if (body != null) yield return body;
            }
        }

        /// <summary>Drops every parsed body. The index is untouched.</summary>
        public void ReleaseBodies() => _residency.Clear();

        /// <summary>Diagnostics for the dev overlay.</summary>
        public string Describe()
            => $"{_index.Count} glade(s) in {_index.ChapterCount} chapter(s), " +
               $"{_residency.Count}/{_residency.Capacity} body/bodies resident";
    }
}
