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
        /// Tiles rotate independently, so the true minimum is just the sum of the
        /// quarter turns each tile still owes. Par is therefore derivable, which is
        /// why the content format lets authors omit it — a hand-typed par is one
        /// more thing that can silently be wrong.
        /// </summary>
        public static int MinimumMoves(Cell[] cells)
        {
            int total = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].kind == Kind.Empty || cells[i].locked) continue;
                total += TurnsOwed(cells[i]);
            }
            return total;
        }

        /// <summary>Quarter turns from this cell's start rotation back to its solution.</summary>
        public static int TurnsOwed(Cell cell)
        {
            int solved = cell.solved;
            for (int k = 0; k < 4; k++)
                if (Puzzle.Rotl(solved, (cell.rot + k) & 3) == solved) return k;
            return 0;
        }
    }
}
