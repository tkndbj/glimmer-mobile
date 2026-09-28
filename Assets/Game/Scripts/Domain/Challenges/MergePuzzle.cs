using System.Collections.Generic;

namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// Merge: an authored board of gems and rocks. <b>One gem moves at a time</b>: dragged, it
    /// slides that way until something stops it, and if what stops it is a gem of its own
    /// size the two become one of the next size. Everything else on the board stands still.
    ///
    /// <para>
    /// <b>Why it is not 2048 any more</b> (2026-09-27, the owner: "it doesn't feel like a
    /// puzzle, it feels like you are doing random stuff … I don't like when all the gems move
    /// all together"). A whole-board slide moves every gem at once and deals a random one
    /// after it, so what a swipe did could not be foreseen and nothing was ever planned. Here
    /// nothing is dealt and nothing moves but the gem under the finger, so every consequence
    /// is visible before the drag: the rocks are where a slide stops, and which pair can meet
    /// is a question of geometry that has a shortest answer (<c>Tools/make_merge_challenges.py</c>
    /// finds it, and <c>ChallengeTests</c> plays it).
    /// </para>
    /// <para>
    /// <b>The fusion: a gem's colour is its rank, and a merge fires the colour it made.</b>
    /// Rank one is red, two green, three blue, four yellow, five red again. A merge feeds
    /// <c>Bolts</c> to that turret; a slide that meets nothing feeds nothing but still costs a
    /// step, which is what makes the repositioning moves the price of a plan.
    /// </para>
    /// <para>
    /// <b>Undo is a move.</b> It takes the last slide back and the hill still walks, so a
    /// stranded gem costs time rather than the run. <b>A merge pays once</b>: the k-th merge
    /// into a size pays only the first time the board has held k of them, so merge, undo,
    /// merge again buys nothing - the total a run can be fed is exactly what one solution
    /// feeds (invariant 5d's bound, said of a take-back).
    /// </para>
    /// <para>
    /// <b>The alphabet</b>: <c>.</c> empty, <c>#</c> a rock, a digit <c>1</c>-<c>9</c> a gem of
    /// that rank. Won when a gem of rank <c>target</c> is made.
    /// </para>
    /// </summary>
    public sealed class MergePuzzle : IChallengePuzzle
    {
        /// <summary>A rock: nothing slides into it and nothing moves it.</summary>
        public const int Rock = -1;

        readonly struct Step
        {
            public readonly int From, To, Rank;
            public readonly bool Merged;

            public Step(int from, int to, int rank, bool merged)
            {
                From = from;
                To = to;
                Rank = rank;
                Merged = merged;
            }
        }

        readonly int[] _rank;
        readonly int _bolts;
        readonly ChallengeMove _move = new ChallengeMove();
        readonly List<Step> _history = new List<Step>(32);

        /// <summary>Merges into each rank the board holds now, and the most ever paid for.</summary>
        readonly int[] _made = new int[16], _paid = new int[16];

        public int Target { get; }

        /// <summary>The cell the last move's gem left, or -1 when nothing has moved.</summary>
        public int LastFrom { get; private set; } = -1;

        /// <summary>The cell the last move's gem arrived in.</summary>
        public int LastTo { get; private set; } = -1;

        /// <summary>The rank the gem carried on the way.</summary>
        public int LastRank { get; private set; }

        /// <summary>The rank a merge made at <see cref="LastTo"/>, or nought when the gem met nothing.</summary>
        public int LastMade { get; private set; }

        /// <summary>Whether the last move was a take-back, drawn as the gem sliding home.</summary>
        public bool LastUndone { get; private set; }

        /// <summary>Whether there is a slide to take back.</summary>
        public bool CanUndo => _history.Count > 0;

        /// <summary>How many slides stand on the board, which is what an undo walks back.</summary>
        public int Slides => _history.Count;

        public MergePuzzle(ChallengeDefinition def)
        {
            Width = def.Width;
            Height = def.Height;
            _bolts = def.Bolts;
            Target = def.Target;

            _rank = new int[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    _rank[y * Width + x] = Read(def.Rows[y][x]);
        }

        static int Read(char ch) => ch == '#' ? Rock : ch >= '1' && ch <= '9' ? ch - '0' : 0;

        public ChallengeGenre Genre => ChallengeGenre.Merge;
        public int Width { get; }
        public int Height { get; }

        /// <summary>The gem's rank on a cell: nought for empty, <see cref="Rock"/> for a rock.</summary>
        public int RankAt(int cell) => cell >= 0 && cell < _rank.Length ? _rank[cell] : Rock;

        public bool IsRock(int cell) => RankAt(cell) == Rock;

        /// <summary>Which turret a rank feeds: one is red, and it laps every four.</summary>
        public static int ColourOf(int rank) => rank <= 0 ? -1 : (rank - 1) % ChallengeColours.Count;

        public int Best
        {
            get
            {
                int best = 0;
                for (int i = 0; i < _rank.Length; i++) if (_rank[i] > best) best = _rank[i];
                return best;
            }
        }

        public bool Solved => Best >= Target;

        /// <summary>No gem can go anywhere and there is nothing to take back.</summary>
        public bool Failed
        {
            get
            {
                if (CanUndo) return false;
                for (int c = 0; c < _rank.Length; c++)
                {
                    if (_rank[c] <= 0) continue;
                    if (Where(c, 1, 0, out _) || Where(c, -1, 0, out _) || Where(c, 0, 1, out _) || Where(c, 0, -1, out _))
                        return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Where a gem on <c>cell</c> would end if slid by (dx, dy) in board rows (down
        /// positive), and whether it would merge there. False when it cannot move at all.
        /// </summary>
        public bool Where(int cell, int dx, int dy, out bool merges) => Where(cell, dx, dy, out merges, out _);

        bool Where(int cell, int dx, int dy, out bool merges, out int to)
        {
            merges = false;
            to = cell;

            int rank = RankAt(cell);
            if (rank <= 0) return false;

            int x = cell % Width, y = cell / Width;
            while (true)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) break;

                int next = _rank[ny * Width + nx];
                if (next == 0)
                {
                    x = nx;
                    y = ny;
                    continue;
                }

                if (next == rank)
                {
                    merges = true;
                    to = ny * Width + nx;
                    return true;
                }

                break;
            }

            to = y * Width + x;
            return to != cell;
        }

        public ChallengeMove Apply(ChallengeInput input)
        {
            _move.Clear();

            if (input.Kind == ChallengeInputKind.Undo) return Undo();

            if (input.Kind != ChallengeInputKind.Swipe || (input.Dx == 0) == (input.Dy == 0))
                return Refuse();

            // Screen up is row minus one.
            int dx = input.Dx > 0 ? 1 : input.Dx < 0 ? -1 : 0;
            int dy = input.Dy > 0 ? -1 : input.Dy < 0 ? 1 : 0;

            int from = input.Cell;
            if (!Where(from, dx, dy, out bool merges, out int to)) return Refuse();

            int rank = _rank[from];
            _rank[from] = 0;
            _rank[to] = merges ? rank + 1 : rank;
            _history.Add(new Step(from, to, rank, merges));

            Mark(from, to, rank, merges ? rank + 1 : 0, false);

            if (merges)
            {
                int made = rank + 1;
                if (made < _made.Length)
                {
                    _made[made]++;
                    if (_made[made] > _paid[made])
                    {
                        _paid[made] = _made[made];
                        _move.Feed(ColourOf(made), _bolts);
                    }
                }
            }

            _move.Turn = true;
            return _move;
        }

        ChallengeMove Undo()
        {
            if (_history.Count == 0) return Refuse();

            var step = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);

            _rank[step.To] = step.Merged ? step.Rank : 0;
            _rank[step.From] = step.Rank;
            if (step.Merged && step.Rank + 1 < _made.Length) _made[step.Rank + 1]--;

            // Drawn as the gem going home: from where it arrived to where it left.
            Mark(step.To, step.From, step.Rank, 0, true);

            _move.Turn = true;
            return _move;
        }

        void Mark(int from, int to, int rank, int made, bool undone)
        {
            LastFrom = from;
            LastTo = to;
            LastRank = rank;
            LastMade = made;
            LastUndone = undone;
        }

        ChallengeMove Refuse()
        {
            _move.Refused = true;
            return _move;
        }

        public static string Fault(ChallengeDefinition def)
        {
            if (def.Width < 2 || def.Height < 2) return "merge needs a board at least 2x2";

            string rows = ChallengePuzzles.RowsFault(def, def.Rows, "merge rows");
            if (rows != null) return rows;

            if (def.Target < 2 || def.Target > 9) return "merge needs a target rank from 2 to 9";

            int gems = 0, best = 0;
            long mass = 0;
            for (int y = 0; y < def.Height; y++)
            {
                for (int x = 0; x < def.Width; x++)
                {
                    char ch = def.Rows[y][x];
                    if (ch == '.' || ch == '#') continue;
                    if (ch < '1' || ch > '9') return $"merge row {y} holds '{ch}', which is not '.', '#' or a rank 1-9";
                    gems++;
                    mass += 1L << (ch - '1');
                    if (ch - '0' > best) best = ch - '0';
                }
            }

            if (gems < 2) return "merge needs at least two gems";
            if (def.Target <= best) return $"merge target {def.Target} is already on the board";

            // Nothing is dealt, so the gems on the board are all the merging there will ever be.
            if (mass < 1L << (def.Target - 1))
                return $"merge target {def.Target} needs more gems than the board holds";

            return null;
        }
    }
}
