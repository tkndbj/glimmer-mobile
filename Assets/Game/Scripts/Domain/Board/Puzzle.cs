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
    public enum Kind : byte { Empty = 0, Pipe = 1, Source = 2, Lamp = 3, Duskcap = 4 }

    public struct Cell
    {
        public Kind kind;
        public byte solved;   // arm mask in the authored solution
        public byte rot;      // quarter turns clockwise from the solution
        public byte colour;   // source: emitted energy. lamp: required energy (0 = any)
        public bool locked;   // rooted, cannot be turned
        public byte critter;  // which creature art a lamp wears

        /// <summary>
        /// Which taproot this conduit shares, 1..26, or 0 for none.
        ///
        /// Every conduit carrying the same rune turns as one. That is the whole
        /// mechanic, and it is a rune rather than a pair so a root can hold three
        /// conduits as easily as two without a second concept.
        /// </summary>
        public byte link;

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
    ///
    /// <para>
    /// Two rules bend that shape rather than adding to it, which is why neither needed a
    /// second graph or a second pass. A <b>taproot</b> (<see cref="Cell.link"/>) makes
    /// several conduits turn as one, so a tap stops being a local act; nothing about how
    /// light travels changes. A <b>duskcap</b> (<see cref="Kind.Duskcap"/>) is an ordinary
    /// cell in that same graph whose being reached is a failure rather than a success, so
    /// it costs one term in <see cref="Won"/> and no new traversal at all.
    /// </para>
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

        /// <summary>Duskcaps on this board, and how many of them the light has woken.</summary>
        public int DuskcapCount, DuskcapsWoken;


        int _groups;

        /// <summary>
        /// Conduits by taproot rune, 1..26. Null where no conduit carries that rune.
        ///
        /// Built once because every turn, every owed-turn count and the near-miss
        /// reading all need it, and walking the whole board for a rune on each of
        /// those would make a tap O(cells) for no reason.
        /// </summary>
        readonly List<int>[] _bound = new List<int>[MaxRunes + 1];

        /// <summary>Runes a conduit may carry: A..Z, which is far more than a board needs.</summary>
        public const int MaxRunes = 26;

        /// <summary>
        /// How many distinct taproots one board can wear before the marks stop telling them
        /// apart.
        ///
        /// <para>
        /// A root's identity is carried by pips, because a colour would claim to be a colour
        /// of light (see <c>Pal.Rope</c>), and past about six pips nobody counts them at a
        /// glance on a phone. This lives in Domain rather than beside the tile that draws
        /// them for <see cref="Content.ChapterMap"/>'s reason: it is an authoring limit, so
        /// the build gate has to be able to state it, and the gate cannot reach into
        /// Presentation. A second copy of the number would agree with the drawing right up
        /// until somebody changed one.
        /// </para>
        /// <para>
        /// A warning rather than an error, and only in the validator: this is a judgement
        /// about what a player can read, which is exactly the class of thing
        /// <c>ValidateHearts</c> and <c>CheckClock</c> also decline to fail a build over.
        /// What it must never do is nothing — a board silently drawing its seventh root with
        /// the sixth root's mark is two different roots wearing one identity.
        /// </para>
        /// </summary>
        public const int MaxReadableRunes = 6;

        /// <summary>How many distinct taproots this board carries.</summary>
        public int RootCount
        {
            get
            {
                int n = 0;
                for (int rune = 1; rune <= MaxRunes; rune++)
                    if (_bound[rune] != null && _bound[rune].Count > 1) n++;
                return n;
            }
        }

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
            {
                if (cells[i].kind == Kind.Lamp) LampCount++;
                else if (cells[i].kind == Kind.Duskcap) DuskcapCount++;

                int rune = cells[i].link;
                if (rune == 0 || rune > MaxRunes) continue;
                (_bound[rune] ??= new List<int>()).Add(i);
            }
            ComputeSolutionDepth();
            Evaluate();
        }

        /// <summary>
        /// Every conduit sharing this one's taproot, itself included, or null when it
        /// has none. A group of one is returned as null: a rune nothing else carries is
        /// an authoring mistake the validator refuses, and treating it as a group here
        /// would only make the mistake harder to see.
        /// </summary>
        public List<int> Bound(int i)
        {
            int rune = C[i].link;
            if (rune == 0 || rune > MaxRunes) return null;
            var group = _bound[rune];
            return group != null && group.Count > 1 ? group : null;
        }

        public bool IsBound(int i) => Bound(i) != null;

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

        /// <summary>
        /// How many quarter turns are still owed on this tile, on its own.
        ///
        /// Rarely the number a caller wants — see <see cref="TurnsOwed"/>, which asks the
        /// same question of a bound tile's whole taproot. This one exists because the
        /// group answer is defined in terms of it.
        /// </summary>
        public int TurnsOwedAlone(int i)
        {
            int m = C[i].solved;
            for (int k = 0; k < 4; k++)
                if (Rotl(m, (C[i].rot + k) & 3) == m) return k;
            return 0;
        }

        /// <summary>
        /// How many taps still separate this tile from its solved orientation.
        ///
        /// <para>
        /// For a bound conduit that is the count for its whole taproot, because one tap
        /// turns every conduit on it: the answer is the smallest number of turns after
        /// which <em>every</em> member is solved. That is not generally the largest of
        /// their individual counts — a straight conduit reads the same every half turn, so
        /// it is solved at two of the four offsets and simply goes along with whatever the
        /// elbows on its root demand.
        /// </para>
        /// <para>
        /// A root whose members can never agree is an unwinnable board that looks perfectly
        /// authored, exactly like a brittle conduit owed more turns than it survives, and
        /// <c>LevelValidator.CheckBoundConduits</c> refuses it for the same reason. If one
        /// ever shipped anyway this falls back to the tile's own count rather than
        /// returning zero, because reporting "nothing left to do" on a board that cannot be
        /// finished is the one answer that would make the hint and the near-miss line lie.
        /// </para>
        /// <para>
        /// A crumbled member is skipped. Its arms are gone from the board, so asking it to
        /// reach an orientation would hold the whole root to a tile nothing can see.
        /// </para>
        /// </summary>
        public int TurnsOwed(int i)
        {
            var group = Bound(i);
            if (group == null) return TurnsOwedAlone(i);

            for (int k = 0; k < 4; k++)
            {
                bool all = true;
                for (int m = 0; m < group.Count; m++)
                {
                    int j = group[m];
                    if (Shattered(j)) continue;
                    int mask = C[j].solved;
                    if (Rotl(mask, (C[j].rot + k) & 3) != mask) { all = false; break; }
                }
                if (all) return k;
            }

            return TurnsOwedAlone(i);
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

                // Runes already paid for. A taproot costs its turns once however many
                // conduits ride on it, which is the whole reason a bound board's par is
                // lower than its tile count suggests.
                int counted = 0;

                for (int i = 0; i < C.Length; i++)
                {
                    // Unreached by the solution's own light: decoration, and free to be
                    // pointing anywhere at all.
                    if (SolutionDepth[i] == int.MaxValue) continue;

                    if (Shattered(i)) return -1;
                    if (C[i].kind == Kind.Empty) continue;

                    if (IsBound(i))
                    {
                        int bit = 1 << (C[i].link - 1);
                        if ((counted & bit) != 0) continue;
                        counted |= bit;
                    }

                    total += TurnsOwed(i);
                }

                return total;
            }
        }

        /// <summary>Whether this tile alone reads the same at every angle.</summary>
        public bool InertAlone(int i)
        {
            int m = C[i].solved;
            return Rotl(m, 1) == m;
        }

        /// <summary>
        /// Tiles whose four orientations are identical never need a turn.
        ///
        /// A bound conduit is inert only when every conduit on its taproot is, because
        /// tapping it turns them all — a crossroads that happens to sit on a root worth
        /// turning is still worth tapping, and refusing the tap would leave the player
        /// poking a tile that visibly moves its partners for everyone else.
        /// </summary>
        public bool Inert(int i)
        {
            var group = Bound(i);
            if (group == null) return InertAlone(i);

            for (int m = 0; m < group.Count; m++)
                if (!InertAlone(group[m])) return false;

            return true;
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

            var group = Bound(i);
            if (group == null) { TurnOne(i, dir, wear); return true; }

            // The taproot moves as one. A member that has already crumbled is skipped
            // rather than blocking the rest: the root is still there, that conduit is not.
            for (int m = 0; m < group.Count; m++)
            {
                int j = group[m];
                if (Used(j) && !C[j].locked) TurnOne(j, dir, wear);
            }

            return true;
        }

        void TurnOne(int i, int dir, bool wear)
        {
            C[i].rot = (byte)(((C[i].rot + dir) % 4 + 4) % 4);

            if (wear && IsFragile(i))
            {
                Wear[i]++;
                if (Shattered(i)) ShatteredAt = i;
            }
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
            DuskcapsWoken = 0;
            bool all = true;
            for (int i = 0; i < n; i++)
            {
                if (C[i].kind == Kind.Duskcap)
                {
                    // Lit means "awake" for both creatures on the board. For a critter
                    // that is the goal and for a duskcap it is the failure, which is
                    // exactly one rule stated twice rather than two rules — and it lets
                    // the view diff waking and sleeping with the array it already has.
                    Lit[i] = Energy(i) != 0;
                    if (Lit[i]) DuskcapsWoken++;
                    continue;
                }

                if (C[i].kind != Kind.Lamp) continue;
                int have = Energy(i);
                int want = C[i].colour;
                Lit[i] = want == 0 ? have != 0 : have == want;
                if (Lit[i]) LampsLit++; else all = false;
            }

            // A glade settles when every critter is awake and every duskcap is still
            // asleep. The second half is a whole mechanic and one term: light spilling
            // where it was not wanted is as unfinished as light that never arrived.
            Won = all && LampCount > 0 && DuskcapsWoken == 0;
        }

        /// <summary>A duskcap the light has reached. Always false for anything else.</summary>
        public bool Woken(int i) => C[i].kind == Kind.Duskcap && Lit[i];


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
