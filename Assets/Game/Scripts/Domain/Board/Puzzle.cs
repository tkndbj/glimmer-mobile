using System.Collections.Generic;
using GlimmerGrove.Content;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// What occupies a cell.
    ///
    /// Values are explicit and permanent: a board is parsed from authored text on
    /// every load, but analytics and save records travel with level ids that were
    /// authored against a particular meaning of these numbers.
    /// </summary>
    public enum Kind : byte { Empty = 0, Pipe = 1, Source = 2, Lamp = 3 }

    public struct Cell
    {
        public Kind kind;
        public byte solved;   // arm mask in the authored solution
        public byte rot;      // quarter turns clockwise from the solution
        public byte colour;   // source: emitted energy. lamp: required energy (0 = any)
        public bool locked;   // rooted, cannot be turned
        public byte critter;  // which creature art a lamp wears

        /// <summary>
        /// Turns this conduit survives before it crumbles. 0 means it never does.
        ///
        /// This is what makes exploration cost something. Without it a player can spin
        /// every tile at random forever and arrive at the solution by exhaustion, which
        /// is why a move budget alone still feels arbitrary — nothing on the board was
        /// ever at stake, only the counter.
        /// </summary>
        public byte fragile;
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

        /// <summary>Which level this board came from. Carried for scoring and analytics.</summary>
        public readonly LevelId Id;
        public readonly LevelTuning Tuning;

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

        public Puzzle(LevelId id, int w, int h, LevelTuning tuning, Cell[] cells)
        {
            Id = id; W_ = w; H_ = h; Tuning = tuning; C = cells;
            Wear = new int[cells.Length];
            Comp = new int[cells.Length];
            CompColour = new int[cells.Length];
            Depth = new int[cells.Length];
            Lit = new bool[cells.Length];
            SolutionDepth = new int[cells.Length];
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].kind == Kind.Lamp) LampCount++;
            ComputeSolutionDepth();
            Evaluate();
        }

        public int Idx(int x, int y) => y * W_ + x;
        public int X(int i) => i % W_;
        public int Y(int i) => i / W_;

        /// <summary>
        /// Whether a cell takes part in the board at all.
        ///
        /// A crumbled conduit answers false, which is the whole implementation of
        /// shattering: it drops out of the light graph, out of <see cref="Neighbour"/>
        /// and out of every arm's reach without a single other rule knowing it existed.
        /// </summary>
        public bool Used(int i) => C[i].kind != Kind.Empty && !Shattered(i);

        // -------------------------------------------------------------- fragility
        /// <summary>Turns already spent on each cell. Only fragile ones care.</summary>
        public readonly int[] Wear;

        public bool IsFragile(int i) => C[i].fragile > 0;

        /// <summary>Turns left before this conduit crumbles. 0 once it has.</summary>
        public int FragileLeft(int i)
            => IsFragile(i) ? Mathf.Max(0, C[i].fragile - Wear[i]) : int.MaxValue;

        /// <summary>
        /// Crumbled. Note the strict comparison: <c>fragile</c> is how many turns the
        /// conduit <em>survives</em>, so it breaks on the one after that.
        ///
        /// This is load-bearing now that a crumble ends the run. Validation allows a
        /// conduit to be owed exactly its whole allowance, so with a &gt;= here the last
        /// turn of a legitimate solution would break the tile and lose the glade — an
        /// unwinnable level that looks perfectly authored.
        /// </summary>
        public bool Shattered(int i) => IsFragile(i) && Wear[i] > C[i].fragile;

        /// <summary>Set when the last turn crumbled a conduit, so the view can react. -1 otherwise.</summary>
        public int ShatteredAt = -1;

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

        /// <summary>Fragile conduits still owed more turns than they can survive.</summary>
        public bool IsDoomed(int i) => IsFragile(i) && !Shattered(i) && TurnsOwed(i) > FragileLeft(i);

        /// <summary>
        /// Turns a tile. <paramref name="wear"/> is false for an undo, which rewinds the
        /// rotation but never gives fragility back — exploring costs the conduit whether
        /// or not the player keeps the result, and that is precisely what makes a
        /// fragile board worth thinking about instead of spinning.
        /// </summary>
        public bool Turn(int i, int dir = 1, bool wear = true)
        {
            if (!Used(i) || C[i].locked) return false;

            C[i].rot = (byte)(((C[i].rot + dir) % 4 + 4) % 4);

            if (wear && IsFragile(i))
            {
                Wear[i]++;
                if (Shattered(i)) ShatteredAt = i;
            }

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
            for (int i = 0; i < C.Length; i++)
            {
                C[i].rot = (byte)startRotations[i];
                Wear[i] = 0;                 // a restart mends every crumbled conduit
            }

            Moves = 0;
            HintsUsed = 0;
            ShatteredAt = -1;
            Evaluate();
        }

        public int[] Snapshot()
        {
            var s = new int[C.Length];
            for (int i = 0; i < C.Length; i++) s[i] = C[i].rot;
            return s;
        }

        // --------------------------------------------------------------- score
        // Thresholds come from the level's tuning, which is content the game can
        // retune remotely; the board itself has no opinion on difficulty.
        public int Par => Tuning.Par;
        public int Gold => Tuning.GoldThreshold;
        public int Silver => Tuning.SilverThreshold;

        public int StarsFor(int moves) => Tuning.StarsFor(moves);

        // ---------------------------------------------------------------- budget
        public bool HasBudget => Tuning.HasBudget;
        public int MoveBudget => Tuning.MoveBudget;

        /// <summary>Turns still available. <see cref="int.MaxValue"/> on an unbudgeted level.</summary>
        public int MovesLeft => HasBudget ? Mathf.Max(0, MoveBudget - Moves) : int.MaxValue;

        /// <summary>
        /// The run is over on moves.
        ///
        /// Deliberately false on a won board: a player who solves it with their last
        /// turn has solved it.
        /// </summary>
        public bool OutOfMoves => HasBudget && Moves >= MoveBudget && !Won;

        public int LiveStars => StarsFor(Mathf.Max(Moves, 1));
    }
}
