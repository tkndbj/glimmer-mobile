using System.Collections.Generic;
using UnityEngine;

namespace GlimmerGrove
{
    public enum Kind : byte { Empty = 0, Pipe = 1, Source = 2, Lamp = 3 }

    public struct Cell
    {
        public Kind kind;
        public byte solved;   // arm mask in the authored solution
        public byte rot;      // quarter turns clockwise from the solution
        public byte colour;   // source: emitted energy. lamp: required energy (0 = any)
        public bool locked;   // rooted, cannot be turned
        public byte critter;  // which creature art a lamp wears
    }

    /// <summary>
    /// The board. Arms are a 4 bit mask (N=1 E=2 S=4 W=8); two neighbours are joined
    /// when both point at each other. Every joined group carries the additive mix of
    /// every source inside it, so keeping networks apart is the real puzzle.
    /// </summary>
    public sealed class Puzzle
    {
        public const int N = 1, E = 2, S = 4, W = 8;
        public static readonly int[] Bits = { N, E, S, W };
        public static readonly Vector2Int[] Step =
        {
            new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0)
        };

        public readonly int W_, H_;
        public readonly Cell[] C;
        public readonly int Par;
        public readonly string Name;
        public readonly int Index;

        public int Moves;
        public int HintsUsed;

        public readonly int[] Comp;         // group id per cell, -1 for empty
        public readonly int[] CompColour;   // additive mix per group
        public readonly int[] Depth;        // steps from the nearest source, -1 if dark
        public readonly bool[] Lit;         // per lamp cell
        public readonly int[] SolutionDepth;
        public bool Won;
        public int LampCount, LampsLit;

        int _groups;

        public Puzzle(int index, string name, int w, int h, int par, Cell[] cells)
        {
            Index = index; Name = name; W_ = w; H_ = h; Par = par; C = cells;
            Comp = new int[cells.Length];
            CompColour = new int[cells.Length];
            Depth = new int[cells.Length];
            Lit = new bool[cells.Length];
            SolutionDepth = new int[cells.Length];
            for (int i = 0; i < cells.Length; i++) if (cells[i].kind == Kind.Lamp) LampCount++;
            ComputeSolutionDepth();
            Evaluate();
        }

        public int Idx(int x, int y) => y * W_ + x;
        public int X(int i) => i % W_;
        public int Y(int i) => i / W_;
        public bool Used(int i) => C[i].kind != Kind.Empty;

        public static int Rotl(int mask, int turns)
        {
            turns &= 3;
            int outMask = 0;
            for (int i = 0; i < 4; i++)
                if ((mask & (1 << i)) != 0) outMask |= 1 << ((i + turns) & 3);
            return outMask;
        }

        public int Mask(int i) => Rotl(C[i].solved, C[i].rot);

        /// <summary>How many quarter turns are still owed on this tile.</summary>
        public int TurnsOwed(int i)
        {
            int m = C[i].solved;
            for (int k = 0; k < 4; k++)
                if (Rotl(m, (C[i].rot + k) & 3) == m) return k;
            return 0;
        }

        public bool Solved(int i) => TurnsOwed(i) == 0;

        /// <summary>Tiles whose four orientations are identical never need a turn.</summary>
        public bool Inert(int i)
        {
            int m = C[i].solved;
            return Rotl(m, 1) == m;
        }

        public bool CanTurn(int i) => Used(i) && !C[i].locked && !Inert(i);

        public bool Turn(int i, int dir = 1)
        {
            if (!Used(i) || C[i].locked) return false;
            C[i].rot = (byte)(((C[i].rot + dir) % 4 + 4) % 4);
            return true;
        }

        // --------------------------------------------------------------- solve
        readonly Queue<int> _q = new Queue<int>();

        public void Evaluate()
        {
            int n = C.Length;
            for (int i = 0; i < n; i++) { Comp[i] = -1; Depth[i] = -1; Lit[i] = false; }
            _groups = 0;

            for (int i = 0; i < n; i++)
            {
                if (!Used(i) || Comp[i] != -1) continue;
                int g = _groups++;
                int colour = 0;
                _q.Clear();
                _q.Enqueue(i);
                Comp[i] = g;
                while (_q.Count > 0)
                {
                    int a = _q.Dequeue();
                    if (C[a].kind == Kind.Source) colour |= C[a].colour;
                    int ma = Mask(a);
                    for (int d = 0; d < 4; d++)
                    {
                        if ((ma & Bits[d]) == 0) continue;
                        int b = Neighbour(a, d);
                        if (b < 0 || Comp[b] != -1) continue;
                        if ((Mask(b) & Bits[(d + 2) & 3]) == 0) continue;
                        Comp[b] = g;
                        _q.Enqueue(b);
                    }
                }
                CompColour[g] = colour;
            }

            // light travel distance, so the glow can ripple outward from the sources
            _q.Clear();
            for (int i = 0; i < n; i++)
                if (Used(i) && C[i].kind == Kind.Source) { Depth[i] = 0; _q.Enqueue(i); }
            while (_q.Count > 0)
            {
                int a = _q.Dequeue();
                int ma = Mask(a);
                for (int d = 0; d < 4; d++)
                {
                    if ((ma & Bits[d]) == 0) continue;
                    int b = Neighbour(a, d);
                    if (b < 0 || Depth[b] >= 0) continue;
                    if ((Mask(b) & Bits[(d + 2) & 3]) == 0) continue;
                    Depth[b] = Depth[a] + 1;
                    _q.Enqueue(b);
                }
            }

            LampsLit = 0;
            bool all = true;
            for (int i = 0; i < n; i++)
            {
                if (C[i].kind != Kind.Lamp) continue;
                int have = Energy(i);
                int want = C[i].colour;
                Lit[i] = want == 0 ? have != 0 : have == want;
                if (Lit[i]) LampsLit++; else all = false;
            }
            Won = all && LampCount > 0;
        }

        /// <summary>Energy currently reaching a cell.</summary>
        public int Energy(int i) => Comp[i] < 0 ? 0 : CompColour[Comp[i]];

        public int Neighbour(int i, int d)
        {
            int x = X(i) + Step[d].x, y = Y(i) + Step[d].y;
            if (x < 0 || y < 0 || x >= W_ || y >= H_) return -1;
            int j = Idx(x, y);
            return Used(j) ? j : -1;
        }

        void ComputeSolutionDepth()
        {
            for (int i = 0; i < C.Length; i++) SolutionDepth[i] = int.MaxValue;
            var q = new Queue<int>();
            for (int i = 0; i < C.Length; i++)
                if (Used(i) && C[i].kind == Kind.Source) { SolutionDepth[i] = 0; q.Enqueue(i); }
            while (q.Count > 0)
            {
                int a = q.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    if ((C[a].solved & Bits[d]) == 0) continue;
                    int b = Neighbour(a, d);
                    if (b < 0 || SolutionDepth[b] != int.MaxValue) continue;
                    if ((C[b].solved & Bits[(d + 2) & 3]) == 0) continue;
                    SolutionDepth[b] = SolutionDepth[a] + 1;
                    q.Enqueue(b);
                }
            }
        }

        /// <summary>Nearest-to-the-source tile that is still turned the wrong way.</summary>
        public int NextHint()
        {
            int best = -1, bestDepth = int.MaxValue;
            for (int i = 0; i < C.Length; i++)
            {
                if (!CanTurn(i) || Solved(i)) continue;
                int d = SolutionDepth[i];
                if (d < bestDepth) { bestDepth = d; best = i; }
            }
            return best;
        }

        public void Reset(int[] startRotations)
        {
            for (int i = 0; i < C.Length; i++) C[i].rot = (byte)startRotations[i];
            Moves = 0;
            HintsUsed = 0;
            Evaluate();
        }

        public int[] Snapshot()
        {
            var s = new int[C.Length];
            for (int i = 0; i < C.Length; i++) s[i] = C[i].rot;
            return s;
        }

        // --------------------------------------------------------------- score
        public int Gold => Mathf.CeilToInt(Par * 1.35f);
        public int Silver => Mathf.CeilToInt(Par * 2.00f);

        public int StarsFor(int moves)
        {
            if (moves <= Gold) return 3;
            if (moves <= Silver) return 2;
            return 1;
        }

        public int LiveStars => StarsFor(Mathf.Max(Moves, 1));
    }
}
