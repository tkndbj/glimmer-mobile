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

        /// <summary>Reads a run of N E S W, leaving the cursor on the first character that is not one.</summary>
        static int ReadArms(string token, ref int p)
        {
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
            return mask;
        }

        static int CountArms(int mask)
        {
            int n = 0;
            for (int d = 0; d < 4; d++) if ((mask & Puzzle.Bits[d]) != 0) n++;
            return n;
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
                case 'x': cell.kind = Kind.Duskcap; break;
                case '-': cell.kind = Kind.Pipe; break;
                case '=': cell.kind = Kind.Crossing; break;
                case '%': cell.kind = Kind.Briar; break;
                default:
                    error = $"unknown head '{token[0]}', expected one of - = % * @ x .";
                    return false;
            }

            int p = 1;
            int mask = ReadArms(token, ref p);

            if (mask == 0) { error = "no arms, every filled cell needs at least one of N E S W"; return false; }

            // '+' separates a tile's two pairs of arms: on a crossing the arms carrying one
            // flow and then the arms carrying the other, on a briar the arms that are open and
            // then the arms the thorns have closed. Written out rather than implied, because
            // which pair is which is the whole of what both tiles are, and a reader of the grid
            // has to be able to see it without deriving anything.
            //
            // The *order* is what differs, and the difference is the difference between the two
            // mechanics. A crossing's pairs are interchangeable labels, so writing the other one
            // first says nothing new; a briar's are a way through and a wall, so the pair
            // written first is the open one and swapping them is a different tile.
            if (p < token.Length && token[p] == '+')
            {
                if (cell.kind != Kind.Crossing && cell.kind != Kind.Briar)
                {
                    error = "'+' separates the two pairs of arms on a crossing ('=') or a briar ('%')";
                    return false;
                }

                p++;
                int second = ReadArms(token, ref p);
                if (second == 0) { error = "'+' with no arms after it"; return false; }
                if ((mask & second) != 0)
                {
                    error = cell.kind == Kind.Crossing
                        ? "the two strands of a crossing cannot share an arm; light entering " +
                          "one would leave by the other, which is a crossroads rather than a crossing"
                        : "a briar's open pair and its thorned pair cannot share an arm; one way " +
                          "through cannot be both open and shut";
                    return false;
                }

                if (cell.kind == Kind.Crossing) cell.cross = (byte)mask;
                else cell.gate = (byte)mask;
                mask |= second;
            }

            if (cell.kind == Kind.Crossing)
            {
                if (cell.cross == 0)
                {
                    error = "a crossing must say which arms belong to which strand, as '=NS+EW'";
                    return false;
                }

                int other = mask & ~cell.cross & 15;
                if (CountArms(cell.cross) != 2 || CountArms(other) != 2)
                {
                    error = "a crossing carries exactly two arms on each of its two strands; " +
                            "a strand of one is a flow that arrives and stops, and a strand of " +
                            "three is a junction wearing a crossing's mark";
                    return false;
                }
            }

            if (cell.kind == Kind.Briar)
            {
                if (cell.gate == 0)
                {
                    error = "a briar must say which way is open and which way the thorns have " +
                            "closed, as '%NS+EW'";
                    return false;
                }

                int shut = mask & ~cell.gate & 15;
                if (CountArms(cell.gate) != 2 || CountArms(shut) != 2)
                {
                    error = "a briar carries exactly two open arms and two thorned ones; that is " +
                            "what makes all four of its neighbours mate it at every angle, which " +
                            "is the only reason its arms cannot settle it";
                    return false;
                }
            }

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

            // '&A'..'&Z' — the taproot this conduit shares. Every conduit carrying the
            // same rune turns as one.
            if (p < token.Length && token[p] == '&')
            {
                if (p + 1 >= token.Length) { error = "'&' with no root rune after it"; return false; }

                char rune = token[p + 1];
                if (rune < 'A' || rune > 'Z')
                {
                    error = $"root rune '{rune}' out of range, expected A to Z";
                    return false;
                }

                cell.link = (byte)(rune - 'A' + 1);
                p += 2;
            }

            if (p != token.Length) { error = $"trailing characters '{token.Substring(p)}'"; return false; }

            if (cell.fragile > 0 && !cell.IsConduit)
            {
                error = "only a conduit can be fragile; a heart-crystal or critter that " +
                        "crumbled would make the level unwinnable rather than harder";
                return false;
            }

            if (cell.link > 0 && !cell.IsConduit)
            {
                error = "only a conduit can share a taproot; a heart-crystal or creature " +
                        "turning by remote control is a rule nothing on the board could show";
                return false;
            }

            if (cell.link > 0 && cell.locked)
            {
                error = "a rooted conduit cannot share a taproot: one of the two says the " +
                        "player may never turn it and the other says turning its partner does";
                return false;
            }

            // A crumbling taproot would break several conduits on one tap, and only one of
            // them can be reported as the tile that gave way — so the board would lose cells
            // the view never showed going. Refused here rather than warned about, because
            // there is no reading of the pair that does something sensible.
            if (cell.link > 0 && cell.fragile > 0)
            {
                error = "a conduit cannot be both brittle and bound to a taproot";
                return false;
            }

            if (cell.kind == Kind.Source && cell.colour == 0)
            {
                error = "a heart-crystal must emit a colour";
                return false;
            }

            if (cell.kind == Kind.Duskcap && cell.colour != 0)
            {
                error = "a duskcap takes no colour; any light at all wakes it";
                return false;
            }

            // A crossing carries whatever each strand is given and asks for nothing. Colouring
            // one would be claiming a crossing is a kind of light rather than a kind of tile.
            if (cell.kind == Kind.Crossing && cell.colour != 0)
            {
                error = "a crossing takes no colour; each of its strands simply carries " +
                        "whatever reaches it";
                return false;
            }

            // Nor does a briar. It decides *whether* light passes and never *which* light,
            // which is the one sentence that tells it from every coloured tile on the board.
            if (cell.kind == Kind.Briar && cell.colour != 0)
            {
                error = "a briar takes no colour; it decides which way light may go, never " +
                        "which light may go there";
                return false;
            }

            if (cell.kind == Kind.Lamp)
                cell.critter = (byte)(critterCursor++ % CritterVariants);

            return true;
        }
    }
}
