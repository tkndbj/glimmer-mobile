using System;
using System.Collections.Generic;
using UnityEngine;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// A run of levels that ship together and share their art.
    ///
    /// The chapter is the delivery unit: one JSON file, one backdrop, one map strip,
    /// downloaded and cached as a whole. Adding a fortnightly content drop means
    /// publishing one more of these, and nothing that already shipped is touched.
    /// </summary>
    public sealed class ChapterDefinition
    {
        public readonly ChapterId Id;

        /// <summary>Sort order across chapters. Sparse on purpose, so 15 fits between 10 and 20.</summary>
        public readonly int Order;

        public readonly string NameKey;

        /// <summary>Shared art, inherited by every level that does not override it.</summary>
        public readonly Color Accent;
        public readonly Color Slate;
        public readonly string Backdrop;

        /// <summary>
        /// Background strips stacked bottom to top to form this chapter's stretch of
        /// map. A chapter owns its own piece of road, so the map grows by appending
        /// chapters rather than by every level needing a place on one fixed image.
        /// </summary>
        public readonly string[] MapStrips;

        /// <summary>Level ids in play order. The catalog holds the definitions.</summary>
        public readonly IReadOnlyList<LevelId> LevelIds;

        public ChapterDefinition(ChapterId id, int order, string nameKey,
                                 Color accent, Color slate, string backdrop, string[] mapStrips,
                                 IReadOnlyList<LevelId> levelIds)
        {
            if (!id.IsValid) throw new ArgumentException("chapter needs a valid id", nameof(id));

            Id = id;
            Order = order;
            NameKey = string.IsNullOrEmpty(nameKey) ? DefaultNameKey(id) : nameKey;
            Accent = accent;
            Slate = slate;
            Backdrop = backdrop;
            MapStrips = mapStrips != null && mapStrips.Length > 0 ? mapStrips : new[] { "strip0" };
            LevelIds = levelIds ?? Array.Empty<LevelId>();
        }

        public int StripCount => MapStrips.Length;

        public static string DefaultNameKey(ChapterId id) => "chapter." + id.Value + ".name";

        public int LevelCount => LevelIds.Count;

        public override string ToString() => Id.Value;
    }
}
