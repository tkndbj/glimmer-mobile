using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Decides how many chapter bodies stay parsed in memory, and which.
    ///
    /// One small class holds the whole policy so that changing it is a one line
    /// decision rather than an archaeology exercise. The default keeps two: the
    /// chapter being played and the one before or after it, because stepping between
    /// neighbouring chapters on the map is the common navigation and re-reading a
    /// body to go back one screen would be visible.
    ///
    /// Bodies are small — a chapter of twenty levels is a few hundred kilobytes of
    /// definitions — so this bounds parsed content by a constant instead of by the
    /// size of the catalog, exactly as <c>AssetScope.Chapter</c> bounds texture memory.
    ///
    /// Not thread safe, and deliberately so: every caller is the main thread.
    /// </summary>
    public sealed class ChapterResidency
    {
        public const int DefaultCapacity = 2;

        readonly Dictionary<ChapterId, ChapterBody> _bodies = new Dictionary<ChapterId, ChapterBody>();

        /// <summary>Least recently used first, so eviction is a read off the front.</summary>
        readonly List<ChapterId> _recency = new List<ChapterId>();

        public ChapterResidency(int capacity = DefaultCapacity)
            => Capacity = capacity < 1 ? 1 : capacity;

        public int Capacity { get; }

        public int Count => _bodies.Count;

        public IEnumerable<ChapterId> Resident => _bodies.Keys;

        public bool Has(ChapterId id) => _bodies.ContainsKey(id);

        /// <summary>Reads a resident body and marks it most recently used.</summary>
        public bool TryGet(ChapterId id, out ChapterBody body)
        {
            if (!_bodies.TryGetValue(id, out body)) return false;

            Touch(id);
            return true;
        }

        /// <summary>
        /// Peeks without affecting eviction order. For diagnostics and for code that
        /// only wants to know whether a body happens to be here.
        /// </summary>
        public ChapterBody Peek(ChapterId id) => _bodies.TryGetValue(id, out var body) ? body : null;

        public void Put(ChapterBody body)
        {
            if (body == null) return;

            _bodies[body.Id] = body;
            Touch(body.Id);

            while (_bodies.Count > Capacity && _recency.Count > 0)
            {
                var evicted = _recency[0];
                _recency.RemoveAt(0);
                _bodies.Remove(evicted);
            }
        }

        public void Evict(ChapterId id)
        {
            if (!_bodies.Remove(id)) return;
            _recency.Remove(id);
        }

        public void Clear()
        {
            _bodies.Clear();
            _recency.Clear();
        }

        void Touch(ChapterId id)
        {
            _recency.Remove(id);
            _recency.Add(id);
        }
    }
}
