using System;
using UnityEngine;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// A run of levels that ship together and share their art.
    ///
    /// The chapter is the delivery unit: one JSON file, one backdrop, one set of map
    /// strips, downloaded and cached as a whole. Adding a fortnightly content drop
    /// means publishing one more of these, and nothing that already shipped is touched.
    ///
    /// It holds what a chapter *is*, not where it sits or what belongs to it. Order and
    /// membership are the manifest's, recorded in <see cref="ChapterIndexEntry"/> — so
    /// a chapter body carries no opinion about its own position, and reordering the
    /// game never means reshipping one.
    /// </summary>
    public sealed class ChapterDefinition
    {
        public readonly ChapterId Id;

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

        /// <summary>
        /// Where the end-of-chapter marker sits across the map, 0..1.
        ///
        /// Authored rather than derived because the trail zigzags and the marker caps it:
        /// which side it belongs on is a fact about where this chapter's last glades were
        /// put, and one constant is right for a chapter that ends on the left and wrong
        /// for one that ends on the right. An unauthored chapter takes
        /// <see cref="ChapterMap.TeaserX"/>, so nothing already shipped moves.
        /// </summary>
        public readonly float TeaserX;

        public ChapterDefinition(ChapterId id, string nameKey,
                                 Color accent, Color slate, string backdrop, string[] mapStrips,
                                 float teaserX = 0f)
        {
            if (!id.IsValid) throw new ArgumentException("chapter needs a valid id", nameof(id));

            Id = id;
            NameKey = string.IsNullOrEmpty(nameKey) ? DefaultNameKey(id) : nameKey;
            Accent = accent;
            Slate = slate;
            Backdrop = backdrop;
            MapStrips = mapStrips != null && mapStrips.Length > 0 ? mapStrips : new[] { "strip0" };
            TeaserX = ChapterMap.TeaserAcross(teaserX);
        }

        public int StripCount => MapStrips.Length;

        public static string DefaultNameKey(ChapterId id) => "chapter." + id.Value + ".name";

        public override string ToString() => Id.Value;
    }
}
