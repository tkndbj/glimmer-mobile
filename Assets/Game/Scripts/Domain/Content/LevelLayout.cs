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
            "head + arms [+ #colour] + /startRotation [+ !] [+ ~turns] [+ &rune]   " +
            "head: '-' conduit, '=' crossing, '%' briar, '*' heart-crystal, " +
            "'@' sleeping critter, '.' empty   " +
            "arms: any of N E S W in the solved orientation   " +
            "'=' a crossing, written '=NS+EW': two strands of two arms that pass through " +
            "one another and never meet, so the light entering by one pair leaves only by " +
            "that pair. A crossing whose strands are joined elsewhere on the board crosses " +
            "nothing, which the validator says out loud.   " +
            "'%' a briar, written '%NS+EW': four arms again, but only the first pair is " +
            "open and the thorns have closed the second, and one tap swaps which. Unlike a " +
            "crossing the order matters.   " +
            "Both four-armed tiles mate every neighbour at every angle, so nothing about " +
            "the pipe-fitting settles either one and only colour can. The validator turns " +
            "each of them one step and says so out loud if the glade still finishes: a tile " +
            "nothing on the board settles is one the player cannot place, and par charged " +
            "them for it.   " +
            "colour: R G B, Y=R+G, M=R+B, C=G+B, W=R+G+B, A=any   " +
            "'!' marks a rooted tile the player cannot turn   " +
            "'~1'..'~9' a fragile conduit that crumbles after that many turns and leaves " +
            "a gap. It must be able to reach its solved orientation within its own " +
            "count, which the validator proves.   " +
            "'&A'..'&Z' a taproot: every conduit carrying the same rune turns as one, and " +
            "some number of turns must solve all of them at once, which the validator " +
            "proves.";

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
