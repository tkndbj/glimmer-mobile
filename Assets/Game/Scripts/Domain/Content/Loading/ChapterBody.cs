using System;
using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// One chapter, fully read: its shared art and every level definition inside it.
    ///
    /// This is the expensive half of the catalog — grids, tuning, colours — and it is
    /// scoped exactly the way chapter art is, loaded on entering a chapter and dropped
    /// on leaving it. That symmetry is deliberate: a chapter is the unit of delivery,
    /// the unit of shared art and now also the unit of parsed content, so all three
    /// costs are bounded by one chapter's size rather than by the size of the catalog.
    ///
    /// Immutable once built.
    /// </summary>
    public sealed class ChapterBody
    {
        public readonly ChapterDefinition Definition;

        readonly LevelDefinition[] _levels;
        readonly Dictionary<LevelId, LevelDefinition> _byId;

        public ChapterBody(ChapterDefinition definition, IReadOnlyList<LevelDefinition> levels)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));

            _levels = new LevelDefinition[levels?.Count ?? 0];
            for (int i = 0; i < _levels.Length; i++) _levels[i] = levels[i];

            _byId = new Dictionary<LevelId, LevelDefinition>(_levels.Length);
            foreach (var level in _levels) _byId[level.Id] = level;
        }

        public ChapterId Id => Definition.Id;

        /// <summary>Levels in the order the chapter body authored them.</summary>
        public IReadOnlyList<LevelDefinition> Levels => _levels;

        public int Count => _levels.Length;

        public bool Contains(LevelId id) => _byId.ContainsKey(id);

        public LevelDefinition Find(LevelId id) => _byId.TryGetValue(id, out var level) ? level : null;

        public bool TryFind(LevelId id, out LevelDefinition level) => _byId.TryGetValue(id, out level);

        /// <summary>
        /// Levels in the order the index gives, resolved against this body.
        ///
        /// The index is the authority on order, so a body that happens to list its
        /// levels differently does not change what the player sees. A level the index
        /// names but the body does not carry is skipped — validation fails the build on
        /// exactly that case, so at runtime it can only mean a partial download.
        /// </summary>
        public IEnumerable<LevelDefinition> InIndexOrder(IReadOnlyList<LevelId> order)
        {
            if (order == null) yield break;

            foreach (var id in order)
                if (_byId.TryGetValue(id, out var level)) yield return level;
        }

        public override string ToString() => $"{Id} ({Count} level(s))";
    }
}
