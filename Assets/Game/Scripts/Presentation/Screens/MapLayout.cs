using System.Collections.Generic;
using GlimmerGrove.Content;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The geometry of one chapter's stretch of map.
    ///
    /// A chapter, not the whole game. That is the decision that makes the map scale:
    /// the screen renders one chapter at a time, so the number of background strips,
    /// map nodes and loaded textures is bounded by a chapter's size — around twenty
    /// levels — and never by the size of the catalog. A fiftieth chapter costs
    /// exactly what the first one does.
    ///
    /// It is also why a level's authored <c>mapX</c>/<c>mapY</c> are fractions of its
    /// own chapter: nothing that already shipped needs renumbering when a chapter is
    /// added above or below it.
    /// </summary>
    public sealed class MapLayout
    {
        /// <summary>Canvas units per background strip.</summary>
        public const float StripHeight = 1200f;

        /// <summary>How far above the last level the end-of-chapter marker floats.</summary>
        const float TeaserGap = 0.22f;

        readonly Dictionary<LevelId, Vector2> _positions = new Dictionary<LevelId, Vector2>();

        public ChapterDefinition Chapter { get; private set; }

        public float TotalHeight { get; private set; } = StripHeight;

        /// <summary>Strip sprite keys, bottom to top.</summary>
        public IReadOnlyList<string> Strips { get; private set; } = new[] { "strip0" };

        /// <summary>Where the end-of-chapter marker sits, as fractions of this map.</summary>
        public Vector2 TeaserPosition { get; private set; } = new Vector2(0.66f, 0.845f);

        /// <summary>Levels of this chapter in play order, resolved against the catalog.</summary>
        public IReadOnlyList<LevelDefinition> Levels { get; private set; } = new LevelDefinition[0];

        /// <summary>
        /// Builds the geometry from a loaded chapter body, ordered by the index.
        ///
        /// Both halves are needed and neither substitutes for the other: the body knows
        /// where each glade sits on its strip, and the index knows what order they come
        /// in. Order is the manifest's, so a chapter body that happens to list its
        /// levels differently cannot change what the player walks along.
        /// </summary>
        public static MapLayout Build(ChapterBody chapter, IReadOnlyList<LevelId> order)
        {
            var layout = new MapLayout();
            if (chapter == null) return layout;

            var definition = chapter.Definition;

            layout.Chapter = definition;
            layout.Strips = definition.MapStrips;
            layout.TotalHeight = Mathf.Max(StripHeight, definition.StripCount * StripHeight);

            var levels = new List<LevelDefinition>(chapter.Count);
            float highest = 0f;

            foreach (var level in chapter.InIndexOrder(order))
            {
                var authored = level.Presentation.MapPosition;
                var p = new Vector2(Mathf.Clamp01(authored.x), Mathf.Clamp01(authored.y));

                layout._positions[level.Id] = p;
                levels.Add(level);
                if (p.y > highest) highest = p.y;
            }

            layout.Levels = levels;
            layout.TeaserPosition = new Vector2(0.66f, Mathf.Min(0.95f, highest + TeaserGap));
            return layout;
        }

        public Vector2 PositionOf(LevelId id)
            => _positions.TryGetValue(id, out var p) ? p : new Vector2(.5f, .5f);

        public bool Has(LevelId id) => _positions.ContainsKey(id);

        /// <summary>Distance from the bottom of the map to a strip's bottom edge.</summary>
        public float StripBottom(int index) => index * StripHeight;
    }
}
