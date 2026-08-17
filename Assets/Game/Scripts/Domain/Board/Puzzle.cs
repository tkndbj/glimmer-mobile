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

        /// <summary>
        /// How many single turns still separate this board from the authored solution,
        /// or -1 when the solution can no longer be reached.
        ///
        /// <para>
        /// <b>This is an upper bound, and the bound is the point.</b> A board is won when
        /// every lamp is lit (see <see cref="Evaluate"/>), which can happen with spare
        /// conduits still pointing anywhere — so the true minimum number of turns to a win
        /// may be lower than this, and computing it exactly would mean searching the whole
        /// rotation space of the board on the frame a run ends. What is cheap is the
        /// distance along the solution the level was authored with, and that is sound in
        /// the direction that matters: if this reads 1, then one turn <em>definitely</em>
        /// finishes the glade. Nothing that quotes this number can therefore overstate how
        /// close the player was, which is the whole reason it exists — a near-miss line the
        /// player can catch being generous is worse than none.
        /// </para>
        /// <para>
        /// Only conduits the solution's own light graph reaches are counted. A decorative
        /// pipe strung off the network can sit at any angle in a perfectly winnable board,
        /// and charging the player turns for straightening it would inflate every reading.
        /// <see cref="SolutionDepth"/> already distinguishes the two.
        /// </para>
        /// <para>
        /// -1 when a conduit the solution needs has crumbled. There is then no turn count
        /// that means anything — the board this measures against no longer exists — and
        /// returning a number computed over the survivors would quietly report the player
        /// as closer than they were, because <see cref="Used"/> drops a shattered cell and
        /// takes its owed turns with it.
        /// </para>
        /// </summary>
        public int TurnsToSolution
        {
            get
            {
                int total = 0;

                for (int i = 0; i < C.Length; i++)
                {
                    // Unreached by the solution's own light: decoration, and free to be
                    // pointing anywhere at all.
                    if (SolutionDepth[i] == int.MaxValue) continue;

                    if (Shattered(i)) return -1;
                    if (C[i].kind == Kind.Empty) continue;

                    total += TurnsOwed(i);
                }

                return total;
            }
        }

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

        /// <summary>
        /// Every conduit the authored solution still wants turned, nearest the source
        /// first. The tiles <see cref="TurnsToSolution"/> is counting.
        ///
        /// Shared rather than re-derived by each caller, because a second copy of "which
        /// tiles matter" is a second copy that can disagree with the number the player was
        /// quoted — and the one place this is used is the moment a defeat screen points at
        /// them and says how close it was.
        /// </summary>
        public void Owed(List<int> into)
        {
            if (into == null) return;
            into.Clear();

            for (int i = 0; i < C.Length; i++)
            {
                if (SolutionDepth[i] == int.MaxValue) continue;
                if (!Used(i) || TurnsOwed(i) == 0) continue;
                into.Add(i);
            }

            into.Sort((a, b) => SolutionDepth[a].CompareTo(SolutionDepth[b]));
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

        /// <summary>
        /// What a run of this many turns, taking this long, is worth.
        ///
        /// <para>
        /// There is deliberately no moves-only overload. Stars are the worse of the two
        /// readings (<see cref="Content.LevelTuning.StarsFor"/>), so a caller that could ask
        /// for the moves half alone would get a number that is right up until a glade is
        /// timed — and the compiler would never mention it. Pass 0 for an untimed run; the
        /// clock half then costs nothing, which is what 0 means everywhere else here.
        /// </para>
        /// </summary>
        public int StarsFor(int moves, int millis) => Tuning.StarsFor(moves, millis);

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

        // ------------------------------------------------------------------ clock
        // As with the thresholds above, the board has no opinion on how long a glade is
        // worth — it only passes the question on to its tuning.
        public bool HasTimeLimit => Tuning.HasTimeLimit;
        public int TimeLimitMillis => Tuning.TimeLimitMillis;
    }
}
