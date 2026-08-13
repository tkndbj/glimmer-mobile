using System.Collections.Generic;
using UnityEngine;

namespace GlimmerGrove
{
    public sealed class LevelDef
    {
        public readonly string Name, Tagline, Lesson;
        public readonly int W, H, Par;
        public readonly string[] Rows;
        public readonly Vector2 MapPos;     // x across the map, y up from the bottom of the chain
        public readonly Color Accent;       // highlight colour on the map
        public readonly Color Slate;        // board tint, also drives the conduit greys
        public readonly string Backdrop;    // Resources sprite under Art/Bg

        public LevelDef(string name, string tagline, string lesson, int w, int h, int par,
                        Vector2 mapPos, Color accent, Color slate, string backdrop, string[] rows)
        {
            Name = name; Tagline = tagline; Lesson = lesson;
            W = w; H = h; Par = par; Rows = rows;
            MapPos = mapPos; Accent = accent; Slate = slate; Backdrop = backdrop;
        }
    }

    /// <summary>
    /// Level data. Each token is  head + arms [+ #colour] + /startRotation [+ !]
    ///   head  -  conduit   *  heart-crystal   @  sleeping critter   .  empty
    ///   arms  any of N E S W, written in the solved orientation
    ///   colour  R G B  Y(=R+G)  M(=R+B)  C(=G+B)  W(=R+G+B)  A(any)
    ///   !     rooted, the player cannot turn it
    /// Layouts were generated as spanning forests and machine-verified: every arm
    /// mates with a neighbour and every critter is satisfied in the solved state.
    /// </summary>
    public static class Levels
    {
        public static readonly LevelDef[] All =
        {
            new LevelDef(
                "First Light", "A single heart wakes the glade",
                "Tap a conduit to turn it. Light every sleeping critter.",
                5, 5, 34, new Vector2(0.33f, 0.205f), Pal.Sun, Pal.Hex("#123640"), "play_0",
                new[]
                {
                    ". -ES/2 -EW/3 @W#A/3 .",
                    "@E#A/2 -NEW/3 -ESW/3 -EW/3 @W#A/2",
                    "-ES/3 -ESW/3 -NEW/1 -ESW/2 @W#A/1",
                    "*N#W/3 -N/2 -ES/2 -NESW/0 @W#A/1",
                    ". -E/1 -NW/3 -N/3 .",
                }),

            new LevelDef(
                "Twin Streams", "Two hearts, two hues, no mixing",
                "Critters only wake to their own colour. Keep the streams apart.",
                6, 6, 49, new Vector2(0.67f, 0.415f), Pal.Azure, Pal.Hex("#0F2A4A"), "play_1",
                new[]
                {
                    ". @E#R/3 -EW/1 -SW/3 @S#R/3 .",
                    "@E#B/3 -ESW/2 @W#B/2 -NE/1 -NSW/3 @S#R/2",
                    "-E/3 -NSW/3 @S#B/1 -ES/3 -NESW/0 -NW/1",
                    "-ES/1 -NESW/0 -NSW/2 -NS/1 -NE/3 -SW/2",
                    "@N#B/2 -NS/1 -N/3 -NS/3 -ES/0! -NW/0!",
                    ". -NE/1 *W#B/2 @N#R/1 *N#R/1 .",
                }),

            new LevelDef(
                "Prism Heart", "Blend the light, wake the deep grove",
                "Joined hearts blend: red plus blue makes blossom, all three make radiance.",
                6, 7, 71, new Vector2(0.32f, 0.625f), Pal.Bloom, Pal.Hex("#241540"), "play_2",
                new[]
                {
                    "*S#B/1 @E#M/2 -SW/3 -ES/3 *W#G/2 @S#G/1",
                    "-NES/1 -EW/3 -NSW/1 -NES/3 -ESW/3 -NW/1",
                    "*N#R/3 @E#M/2 -NSW/2 -NS/3 -NE/3 -SW/1",
                    "@S#W/3 -E/2 -NSW/0! -NS/3 @S#G/1 @N#G/3",
                    "-NES/0! -SW/1 @N#M/1 -NE/2 -NEW/3 -SW/2",
                    "@N#W/3 -NES/1 -EW/1 -ESW/1 *W#G/2 -NS/3",
                    "@E#W/3 -NEW/0! *W#R/1 -NE/3 *W#B/3 @N#G/3",
                }),
        };

        public static int Count => All.Length;

        static int ColourOf(char c)
        {
            switch (c)
            {
                case 'R': return Pal.R;
                case 'G': return Pal.G;
                case 'B': return Pal.B;
                case 'Y': return Pal.R | Pal.G;
                case 'M': return Pal.R | Pal.B;
                case 'C': return Pal.G | Pal.B;
                case 'W': return Pal.R | Pal.G | Pal.B;
                default: return 0;   // 'A' = any
            }
        }

        public static Puzzle Build(int index)
        {
            var def = All[Mathf.Clamp(index, 0, All.Length - 1)];
            var cells = new Cell[def.W * def.H];
            int critterCursor = 0;

            for (int y = 0; y < def.H; y++)
            {
                var tokens = def.Rows[y].Split(' ');
                for (int x = 0; x < def.W && x < tokens.Length; x++)
                {
                    var tok = tokens[x];
                    int i = y * def.W + x;
                    if (tok == "." || tok.Length == 0) { cells[i].kind = Kind.Empty; continue; }

                    var cell = new Cell();
                    switch (tok[0])
                    {
                        case '*': cell.kind = Kind.Source; break;
                        case '@': cell.kind = Kind.Lamp; break;
                        default: cell.kind = Kind.Pipe; break;
                    }

                    int p = 1;
                    int mask = 0;
                    for (; p < tok.Length; p++)
                    {
                        char c = tok[p];
                        if (c == 'N') mask |= Puzzle.N;
                        else if (c == 'E') mask |= Puzzle.E;
                        else if (c == 'S') mask |= Puzzle.S;
                        else if (c == 'W') mask |= Puzzle.W;
                        else break;
                    }
                    cell.solved = (byte)mask;

                    if (p < tok.Length && tok[p] == '#') { cell.colour = (byte)ColourOf(tok[p + 1]); p += 2; }
                    if (p < tok.Length && tok[p] == '/') { cell.rot = (byte)(tok[p + 1] - '0'); p += 2; }
                    if (p < tok.Length && tok[p] == '!') cell.locked = true;

                    if (cell.kind == Kind.Lamp) cell.critter = (byte)(critterCursor++ % 5);
                    cells[i] = cell;
                }
            }

            return new Puzzle(index, def.Name, def.W, def.H, def.Par, cells);
        }

        /// <summary>Sanity net: shouts in the console if a layout was mis-typed.</summary>
        public static string Validate(int index)
        {
            var p = Build(index);
            for (int i = 0; i < p.C.Length; i++)
            {
                if (!p.Used(i)) continue;
                if (p.C[i].solved == 0) return $"L{index}: isolated cell at {p.X(i)},{p.Y(i)}";
                for (int d = 0; d < 4; d++)
                {
                    if ((p.C[i].solved & Puzzle.Bits[d]) == 0) continue;
                    int j = p.Neighbour(i, d);
                    if (j < 0 || (p.C[j].solved & Puzzle.Bits[(d + 2) & 3]) == 0)
                        return $"L{index}: dangling arm at {p.X(i)},{p.Y(i)} dir {d}";
                }
            }
            var start = p.Snapshot();
            for (int i = 0; i < p.C.Length; i++) p.C[i].rot = 0;
            p.Evaluate();
            if (!p.Won) return $"L{index}: authored solution does not light every critter";
            p.Reset(start);
            return null;
        }
    }
}
