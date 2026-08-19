using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Builds a playable <see cref="Puzzle"/> from a catalogued level.
    ///
    /// This is the only bridge between content and gameplay. The board never sees a
    /// definition and the catalog never sees a board, so the puzzle rules can change
    /// without touching the content pipeline and vice versa.
    /// </summary>
    public static class PuzzleFactory
    {
        public static bool TryCreate(LevelDefinition level, out Puzzle puzzle, out IReadOnlyList<string> errors)
        {
            puzzle = null;

            var parsed = LevelGridParser.Parse(level.Layout);
            errors = parsed.Errors;
            if (!parsed.Ok) return false;

            puzzle = new Puzzle(level.Id, level.Layout.Width, level.Layout.Height, level.Tuning, parsed.Cells);
            return true;
        }

        /// <summary>
        /// The fewest taps that can solve a board.
        ///
        /// <para>
        /// Tiles rotate independently, so the minimum is the sum of the quarter turns each
        /// tile still owes. Par is therefore derivable, which is why the content format
        /// lets authors omit it — a hand-typed par is one more thing that can silently be
        /// wrong.
        /// </para>
        /// <para>
        /// A taproot is the one thing that breaks the independence, and it breaks it in the
        /// player's favour: every conduit carrying a rune turns on one tap, so the root is
        /// charged once. A bound board's par is therefore lower than its tile count
        /// suggests, and since the move budget and the clock are both multiples of par, the
        /// glade is tuned by the same arithmetic without anybody authoring a number for it.
        /// </para>
        /// </summary>
        public static int MinimumMoves(Cell[] cells)
        {
            int total = 0;
            int counted = 0;                    // runes already charged, one bit each

            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].kind == Kind.Empty || cells[i].locked) continue;

                int rune = cells[i].link;
                if (rune > 0 && rune <= Puzzle.MaxRunes)
                {
                    int bit = 1 << (rune - 1);
                    if ((counted & bit) != 0) continue;
                    counted |= bit;

                    // A root whose members cannot agree has no honest cost; the validator
                    // refuses the level, and charging a negative par here would hide that
                    // behind a number that merely looks odd.
                    int root = RootTurnsOwed(cells, rune);
                    if (root > 0) total += root;
                    continue;
                }

                total += TurnsOwed(cells[i]);
            }

            return total;
        }

        /// <summary>
        /// Quarter turns from this cell's start rotation back to its solution.
        ///
        /// Asks <see cref="Puzzle.Alike"/> rather than comparing arm masks, which is not a
        /// tidying: a crossing wears all four arms at every angle, so a mask comparison calls
        /// every one of them already solved and derives a par that is short by however many
        /// crossings the board carries.
        /// </summary>
        public static int TurnsOwed(Cell cell)
        {
            for (int k = 0; k < 4; k++)
                if (Puzzle.Alike(cell, cell.rot + k)) return k;
            return 0;
        }

        /// <summary>
        /// Taps that solve every conduit on one taproot at once, or -1 when no number
        /// does. <c>LevelValidator.CheckBoundConduits</c> is what turns that -1 into a
        /// refused build; the same answer is computed here so par and the validator cannot
        /// come to disagree about what a root costs.
        /// </summary>
        public static int RootTurnsOwed(Cell[] cells, int rune)
        {
            for (int k = 0; k < 4; k++)
            {
                bool all = true;
                for (int i = 0; i < cells.Length && all; i++)
                {
                    if (cells[i].link != rune) continue;
                    if (!Puzzle.Alike(cells[i], cells[i].rot + k)) all = false;
                }
                if (all) return k;
            }
            return -1;
        }
    }
}
