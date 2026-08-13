using UnityEngine;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// How one level looks on the map and on the board.
    ///
    /// Everything here is optional and falls back to the level's chapter. That
    /// fallback is the whole point: twenty levels sharing one chapter backdrop is
    /// the difference between a 60 MB game and a 2 GB one, and a level only pays
    /// for art when it genuinely needs its own.
    /// </summary>
    public sealed class LevelPresentation
    {
        /// <summary>Position on the map, 0..1 across and up the chapter's strip.</summary>
        public readonly Vector2 MapPosition;

        /// <summary>Highlight colour for the map node. Null means take the chapter's.</summary>
        public readonly Color? Accent;

        /// <summary>Board tint, which also drives the conduit greys. Null means chapter.</summary>
        public readonly Color? Slate;

        /// <summary>Sprite key under Art/Bg. Null means chapter.</summary>
        public readonly string Backdrop;

        public LevelPresentation(Vector2 mapPosition, Color? accent, Color? slate, string backdrop)
        {
            MapPosition = mapPosition;
            Accent = accent;
            Slate = slate;
            Backdrop = string.IsNullOrEmpty(backdrop) ? null : backdrop;
        }

        public Color ResolveAccent(ChapterDefinition chapter)
            => Accent ?? chapter.Accent;

        public Color ResolveSlate(ChapterDefinition chapter)
            => Slate ?? chapter.Slate;

        public string ResolveBackdrop(ChapterDefinition chapter)
            => Backdrop ?? chapter.Backdrop;
    }
}
