using System;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// The board itself: the authored grid, still in its compact text form.
    ///
    /// This is the one part of a level that is frozen once it ships. Players hold
    /// best-move records against it, so changing a layout silently invalidates
    /// their history — publish a new level id instead. Everything mutable about a
    /// level lives in <see cref="LevelTuning"/> or <see cref="LevelPresentation"/>.
    /// </summary>
    public sealed class LevelLayout
    {
        /// <summary>Token grammar, kept next to the data it describes.</summary>
        public const string Grammar =
            "head + arms [+ #colour] + /startRotation [+ !]   " +
            "head: '-' conduit, '*' heart-crystal, '@' sleeping critter, '.' empty   " +
            "arms: any of N E S W in the solved orientation   " +
            "colour: R G B, Y=R+G, M=R+B, C=G+B, W=R+G+B, A=any   " +
            "'!' marks a rooted tile the player cannot turn";

        public readonly int Width;
        public readonly int Height;

        /// <summary>One entry per row, top row first, tokens separated by spaces.</summary>
        public readonly string[] Rows;

        public LevelLayout(int width, int height, string[] rows)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            Rows = rows ?? throw new ArgumentNullException(nameof(rows));

            Width = width;
            Height = height;
        }

        public int CellCount => Width * Height;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public int Index(int x, int y) => y * Width + x;
    }
}
