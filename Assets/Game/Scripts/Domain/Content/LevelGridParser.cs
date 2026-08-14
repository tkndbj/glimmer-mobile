using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>Cells plus whatever went wrong producing them.</summary>
    public sealed class LevelGridParseResult
    {
        public readonly Cell[] Cells;
        public readonly IReadOnlyList<string> Errors;

        public LevelGridParseResult(Cell[] cells, IReadOnlyList<string> errors)
        {
            Cells = cells;
            Errors = errors;
        }

        public bool Ok => Errors.Count == 0;
    }

    /// <summary>
    /// Turns the authored text grid into board cells.
    ///
    /// Pure and side-effect free: no Unity objects, no logging, no statics touched.
    /// That is deliberate — this is the piece that has to be trustworthy, so it can
    /// be run over every level in a build step and in tests without an Editor.
    /// See <see cref="LevelLayout.Grammar"/> for the token language.
    /// </summary>
    public static class LevelGridParser
    {
        /// <summary>How many critter sprite sets exist under Art/Critters.</summary>
        public const int CritterVariants = 5;

        public static LevelGridParseResult Parse(LevelLayout layout)
        {
            var errors = new List<string>();
            var cells = new Cell[layout.CellCount];

            if (layout.Rows.Length != layout.Height)
            {
                errors.Add($"declared {layout.Height} rows but found {layout.Rows.Length}");
                return new LevelGridParseResult(cells, errors);
            }

            int critterCursor = 0;

            for (int y = 0; y < layout.Height; y++)
            {
                var tokens = Tokenise(layout.Rows[y]);

                if (tokens.Count != layout.Width)
                {
                    errors.Add($"row {y} has {tokens.Count} tokens, expected {layout.Width}");
                    continue;
                }

                for (int x = 0; x < layout.Width; x++)
                {
                    int i = layout.Index(x, y);
                    if (!TryParseCell(tokens[x], ref critterCursor, out cells[i], out string error))
                        errors.Add($"cell {x},{y} ('{tokens[x]}'): {error}");
                }
            }

            return new LevelGridParseResult(cells, errors);
        }

        /// <summary>Splits on runs of whitespace, tolerating ragged authoring.</summary>
        static List<string> Tokenise(string row)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(row)) return tokens;

            int i = 0;
            while (i < row.Length)
            {
                while (i < row.Length && char.IsWhiteSpace(row[i])) i++;
                if (i >= row.Length) break;
                int start = i;
                while (i < row.Length && !char.IsWhiteSpace(row[i])) i++;
                tokens.Add(row.Substring(start, i - start));
            }
            return tokens;
        }

        static bool TryParseCell(string token, ref int critterCursor, out Cell cell, out string error)
        {
            cell = default;
            error = null;

            if (string.IsNullOrEmpty(token)) { error = "empty token"; return false; }

            if (token == ".")
            {
                cell.kind = Kind.Empty;
                return true;
            }


            switch (token[0])
            {
                case '*': cell.kind = Kind.Source; break;
                case '@': cell.kind = Kind.Lamp; break;
                case '-': cell.kind = Kind.Pipe; break;
                default:
                    error = $"unknown head '{token[0]}', expected one of - * @ .";
                    return false;
            }

            int p = 1;
            int mask = 0;
            for (; p < token.Length; p++)
            {
                char c = token[p];
                if (c == 'N') mask |= Puzzle.N;
                else if (c == 'E') mask |= Puzzle.E;
                else if (c == 'S') mask |= Puzzle.S;
                else if (c == 'W') mask |= Puzzle.W;
                else break;
            }

            if (mask == 0) { error = "no arms, every filled cell needs at least one of N E S W"; return false; }
            cell.solved = (byte)mask;

            if (p < token.Length && token[p] == '#')
            {
                if (p + 1 >= token.Length) { error = "'#' with no colour after it"; return false; }
                if (!Energy.TryParse(token[p + 1], out int colour))
                {
                    error = $"unknown colour '{token[p + 1]}', expected R G B Y M C W or A";
                    return false;
                }
                cell.colour = (byte)colour;
                p += 2;
            }

            if (p < token.Length && token[p] == '/')
            {
                if (p + 1 >= token.Length) { error = "'/' with no rotation after it"; return false; }
                char r = token[p + 1];
                if (r < '0' || r > '3') { error = $"rotation '{r}' out of range, expected 0 to 3"; return false; }
                cell.rot = (byte)(r - '0');
                p += 2;
            }

            if (p < token.Length && token[p] == '!')
            {
                cell.locked = true;
                p++;
            }

            // '~N' — a conduit that crumbles after N turns.
            if (p < token.Length && token[p] == '~')
            {
                if (p + 1 >= token.Length) { error = "'~' with no turn count after it"; return false; }

                char n = token[p + 1];
                if (n < '1' || n > '9')
                {
                    error = $"fragility '{n}' out of range, expected 1 to 9";
                    return false;
                }

                cell.fragile = (byte)(n - '0');
                p += 2;
            }

            if (p != token.Length) { error = $"trailing characters '{token.Substring(p)}'"; return false; }

            if (cell.fragile > 0 && cell.kind != Kind.Pipe)
            {
                error = "only a conduit can be fragile; a heart-crystal or critter that " +
                        "crumbled would make the level unwinnable rather than harder";
                return false;
            }

            if (cell.kind == Kind.Source && cell.colour == 0)
            {
                error = "a heart-crystal must emit a colour";
                return false;
            }

            if (cell.kind == Kind.Lamp)
                cell.critter = (byte)(critterCursor++ % CritterVariants);

            return true;
        }
    }
}
