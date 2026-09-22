using System.Collections.Generic;

namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// Pipes: rotate tiles until each colour runs from its source at the top of the board to
    /// its turret at the bottom.
    ///
    /// <para>
    /// <b>The fusion: a connected colour fires every turn it stays connected.</b> A tap turns a
    /// tile a quarter clockwise and costs a step; after every step, each colour whose source
    /// reaches its sink feeds <c>Bolts</c>. So the order the four are joined in is the
    /// decision — join the colour whose raiders are closest first and it fires while the
    /// other three are being built — and a turn that breaks a joined pipe to fix another is a
    /// turn that colour goes quiet.
    /// </para>
    /// <para>
    /// <b>Authored solved, shipped scrambled.</b> A tile's arms are written in their solved
    /// orientation and a <c>/k</c> after them is the quarter-turns the player meets it at, so
    /// the row is solvable by construction and <see cref="Fault"/> proves the solved board
    /// connects every colour before the row ships. Colours flood independently, so a cross
    /// tile carries two; a tile no colour reaches is dark.
    /// </para>
    /// <para>
    /// <b>The alphabet</b>: per cell, space-separated, letters from <c>NESW</c> for the arms
    /// (so <c>NS</c> is a straight and <c>NESW</c> a cross), an optional <c>/1</c>, <c>/2</c> or
    /// <c>/3</c>, or <c>.</c> for no tile. <c>sources</c> and <c>sinks</c> are one letter per
    /// column, a colour or <c>.</c>.
    /// </para>
    /// </summary>
    public sealed class PipesPuzzle : IChallengePuzzle
    {
        public const int N = 1, E = 2, S = 4, W = 8;

        readonly int[] _arms, _rot, _lit;
        readonly int[] _sourceCol = new int[ChallengeColours.Count];
        readonly int[] _sinkCol = new int[ChallengeColours.Count];
        readonly bool[] _joined = new bool[ChallengeColours.Count];
        readonly int _bolts;
        readonly ChallengeMove _move = new ChallengeMove();

        public PipesPuzzle(ChallengeDefinition def)
        {
            Width = def.Width;
            Height = def.Height;
            _bolts = def.Bolts;

            _arms = new int[Width * Height];
            _rot = new int[_arms.Length];
            _lit = new int[_arms.Length];

            for (int y = 0; y < Height; y++)
            {
                var tokens = Tokens(def.Rows[y]);
                for (int x = 0; x < Width; x++)
                {
                    Parse(tokens[x], out _arms[y * Width + x], out _rot[y * Width + x]);
                }
            }

            for (int c = 0; c < ChallengeColours.Count; c++)
            {
                _sourceCol[c] = ColumnOf(def.Sources, c);
                _sinkCol[c] = ColumnOf(def.Sinks, c);
            }

            Flow();
        }

        public ChallengeGenre Genre => ChallengeGenre.Pipes;
        public int Width { get; }
        public int Height { get; }

        /// <summary>A cell's solved arms, before any rotation.</summary>
        public int ArmsAt(int cell) => cell >= 0 && cell < _arms.Length ? _arms[cell] : 0;

        /// <summary>Quarter-turns clockwise the cell is turned from solved.</summary>
        public int RotationAt(int cell) => cell >= 0 && cell < _rot.Length ? _rot[cell] : 0;

        /// <summary>The arms as they stand on the board now.</summary>
        public int CurrentArms(int cell) => Rotate(ArmsAt(cell), RotationAt(cell));

        /// <summary>Which colours run through a cell, as a bitmask over the four.</summary>
        public int LitAt(int cell) => cell >= 0 && cell < _lit.Length ? _lit[cell] : 0;

        public bool HasTile(int cell) => ArmsAt(cell) != 0;

        public int SourceColumn(int colour) => _sourceCol[colour];
        public int SinkColumn(int colour) => _sinkCol[colour];
        public bool IsJoined(int colour) => _joined[colour];

        /// <summary>Colours this board asks to be joined: those with both a source and a sink.</summary>
        public int Asked
        {
            get
            {
                int n = 0;
                for (int c = 0; c < ChallengeColours.Count; c++) if (_sourceCol[c] >= 0 && _sinkCol[c] >= 0) n++;
                return n;
            }
        }

        public int Joined
        {
            get
            {
                int n = 0;
                for (int c = 0; c < ChallengeColours.Count; c++) if (_joined[c]) n++;
                return n;
            }
        }

        public bool Solved => Joined == Asked;
        public bool Failed => false;

        /// <summary>The fewest taps that would solve the board from where it stands.</summary>
        public int TapsLeft
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _arms.Length; i++) n += TapsToSolve(_arms[i], _rot[i]);
                return n;
            }
        }

        public ChallengeMove Apply(ChallengeInput input)
        {
            _move.Clear();

            if (input.Kind != ChallengeInputKind.Tap || input.Cell < 0 || input.Cell >= _arms.Length
                || _arms[input.Cell] == 0)
            {
                _move.Refused = true;
                return _move;
            }

            _rot[input.Cell] = (_rot[input.Cell] + 1) & 3;
            _move.Turn = true;

            Flow();

            for (int c = 0; c < ChallengeColours.Count; c++)
                if (_joined[c]) _move.Feed(c, _bolts);

            return _move;
        }

        // ------------------------------------------------------------------ the flow
        void Flow()
        {
            for (int i = 0; i < _lit.Length; i++) _lit[i] = 0;

            for (int c = 0; c < ChallengeColours.Count; c++)
                _joined[c] = Reaches(c, _lit);
        }

        /// <summary>Whether a colour runs from its source to its sink through the board as it stands.</summary>
        bool Reaches(int colour, int[] lit)
        {
            int from = _sourceCol[colour], to = _sinkCol[colour];
            if (from < 0 || to < 0) return false;

            int start = from;
            if ((CurrentArms(start) & N) == 0) return false;

            var seen = new bool[_arms.Length];
            var stack = new Stack<int>();
            stack.Push(start);
            seen[start] = true;

            bool reached = false;

            while (stack.Count > 0)
            {
                int at = stack.Pop();
                int arms = CurrentArms(at);
                int x = at % Width, y = at / Width;

                if (lit != null) lit[at] |= 1 << colour;

                if (y == Height - 1 && x == to && (arms & S) != 0) reached = true;

                Visit(x, y - 1, arms, N, S, seen, stack);
                Visit(x + 1, y, arms, E, W, seen, stack);
                Visit(x, y + 1, arms, S, N, seen, stack);
                Visit(x - 1, y, arms, W, E, seen, stack);
            }

            return reached;
        }

        void Visit(int nx, int ny, int arms, int outArm, int inArm, bool[] seen, Stack<int> stack)
        {
            if ((arms & outArm) == 0) return;
            if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) return;

            int nb = ny * Width + nx;
            if (seen[nb] || (CurrentArms(nb) & inArm) == 0) return;

            seen[nb] = true;
            stack.Push(nb);
        }

        /// <summary>
        /// The cells a colour runs through once the board is solved, source first.
        ///
        /// <b>For a bot and a hint, never for the flow</b>: it floods the authored arms at
        /// rotation nought, which is the answer, so nothing a player sees may be drawn from it.
        /// </summary>
        public void SolvedPath(int colour, List<int> into)
        {
            into.Clear();
            int from = colour >= 0 && colour < _sourceCol.Length ? _sourceCol[colour] : -1;
            if (from < 0 || (_arms[from] & N) == 0) return;

            var seen = new bool[_arms.Length];
            var stack = new Stack<int>();
            stack.Push(from);
            seen[from] = true;

            while (stack.Count > 0)
            {
                int at = stack.Pop();
                into.Add(at);
                int arms = _arms[at];
                int x = at % Width, y = at / Width;

                Step(x, y - 1, arms, N, S, seen, stack);
                Step(x + 1, y, arms, E, W, seen, stack);
                Step(x, y + 1, arms, S, N, seen, stack);
                Step(x - 1, y, arms, W, E, seen, stack);
            }
        }

        void Step(int nx, int ny, int arms, int outArm, int inArm, bool[] seen, Stack<int> stack)
        {
            if ((arms & outArm) == 0) return;
            if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) return;

            int nb = ny * Width + nx;
            if (seen[nb] || (_arms[nb] & inArm) == 0) return;

            seen[nb] = true;
            stack.Push(nb);
        }

        // ------------------------------------------------------------------ arithmetic
        public static int Rotate(int mask, int turns)
        {
            turns &= 3;
            int out_ = 0;
            for (int i = 0; i < 4; i++)
                if ((mask & (1 << i)) != 0) out_ |= 1 << ((i + turns) & 3);
            return out_;
        }

        /// <summary>Taps to reach an orientation indistinguishable from solved. A straight needs at most one.</summary>
        public static int TapsToSolve(int arms, int rot)
        {
            if (arms == 0) return 0;
            for (int taps = 0; taps < 4; taps++)
                if (Rotate(arms, rot + taps) == arms) return taps;
            return 0;
        }

        static string[] Tokens(string row)
            => (row ?? string.Empty).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

        static bool Parse(string token, out int arms, out int rot)
        {
            arms = 0;
            rot = 0;
            if (string.IsNullOrEmpty(token) || token == ".") return true;

            int p = 0;
            while (p < token.Length && "NESW".IndexOf(token[p]) >= 0)
            {
                arms |= token[p] == 'N' ? N : token[p] == 'E' ? E : token[p] == 'S' ? S : W;
                p++;
            }

            if (p == 0) return false;
            if (p == token.Length) return true;

            if (token[p] != '/' || p + 2 != token.Length) return false;
            char k = token[p + 1];
            if (k < '0' || k > '3') return false;

            rot = k - '0';
            return true;
        }

        static int ColumnOf(string edge, int colour)
        {
            if (string.IsNullOrEmpty(edge)) return -1;
            return edge.IndexOf(ChallengeColours.LetterOf(colour));
        }

        public static string Fault(ChallengeDefinition def)
        {
            if (def.Rows == null || def.Rows.Length != def.Height)
                return $"pipes must have {def.Height} row(s), has {(def.Rows == null ? 0 : def.Rows.Length)}";

            int tiles = 0, scrambled = 0;
            for (int y = 0; y < def.Height; y++)
            {
                var tokens = Tokens(def.Rows[y]);
                if (tokens.Length != def.Width)
                    return $"pipes row {y} must hold {def.Width} cell token(s), holds {tokens.Length}";

                for (int x = 0; x < tokens.Length; x++)
                {
                    if (!Parse(tokens[x], out int arms, out int rot))
                        return $"pipes row {y} cell {x} '{tokens[x]}' is not arms from NESW with an optional /1../3";
                    if (arms != 0) tiles++;
                    if (TapsToSolve(arms, rot) > 0) scrambled++;
                }
            }

            if (tiles == 0) return "pipes has no tile";

            string edges = EdgeFault(def.Sources, def.Width, "sources") ?? EdgeFault(def.Sinks, def.Width, "sinks");
            if (edges != null) return edges;

            int asked = 0;
            for (int c = 0; c < ChallengeColours.Count; c++)
            {
                bool src = ColumnOf(def.Sources, c) >= 0, snk = ColumnOf(def.Sinks, c) >= 0;
                if (src != snk) return $"pipes names {ChallengeColours.LetterOf(c)} at one edge and not the other";
                if (src) asked++;
            }

            if (asked == 0) return "pipes joins no colour: name at least one in sources and sinks";
            if (scrambled == 0) return "pipes is already solved: give at least one tile a /k";

            // The solved board (every rotation at nought) has to join every colour it names, or
            // the row is unsolvable and every gate reads green.
            var solved = new PipesPuzzle(Unscrambled(def));
            if (!solved.Solved)
                return "pipes' solved layout does not join every named colour; check the arms";

            return null;
        }

        static string EdgeFault(string edge, int width, string what)
        {
            if (edge == null || edge.Length != width)
                return $"pipes {what} must be {width} letter(s), is {(edge == null ? 0 : edge.Length)}";

            for (int i = 0; i < edge.Length; i++)
            {
                char ch = edge[i];
                if (ch == '.') continue;
                if (ChallengeColours.IndexOf(ch) < 0) return $"pipes {what} holds '{ch}'";
                if (edge.IndexOf(ch) != i) return $"pipes {what} names '{ch}' twice";
            }

            return null;
        }

        /// <summary>The same row with every rotation taken off, for the solved-board proof.</summary>
        static ChallengeDefinition Unscrambled(ChallengeDefinition def)
        {
            var rows = new string[def.Rows.Length];
            for (int y = 0; y < rows.Length; y++)
            {
                var tokens = Tokens(def.Rows[y]);
                for (int x = 0; x < tokens.Length; x++)
                {
                    int slash = tokens[x].IndexOf('/');
                    if (slash >= 0) tokens[x] = tokens[x].Substring(0, slash);
                }
                rows[y] = string.Join(" ", tokens);
            }

            return new ChallengeDefinition(def.Id, def.Genre, def.Seed, def.Width, def.Height, rows,
                                           def.Gems, def.Sources, def.Sinks, def.Target,
                                           def.Hill, def.Bolts, def.Waves);
        }
    }
}
