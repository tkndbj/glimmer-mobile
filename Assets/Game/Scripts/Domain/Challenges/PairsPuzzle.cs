namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// Pairs: a grid of face-down gems. Turn two; a pair fires its turret.
    ///
    /// <para>
    /// <b>The fusion in one line: a turn is two flips, whichever way they went.</b> The first
    /// flip is free — it is half a decision — and the second settles it: a match stays up and
    /// feeds <c>Bolts</c> to the ward of that colour, a miss goes back down and the hill still
    /// walks. So a remembered board is a fast line and a forgotten one is a slow one, which is
    /// the whole of what memory is worth here.
    /// </para>
    /// <para>
    /// <b>The layout is authored</b>, not shuffled: the board is face down, so an authored
    /// layout is as secret as a dealt one and a fixture can name the cells it flips.
    /// </para>
    /// </summary>
    public sealed class PairsPuzzle : IChallengePuzzle
    {
        public enum Face { Hidden, Up, Matched }

        readonly int[] _colour;
        readonly Face[] _face;
        readonly int _bolts;
        readonly ChallengeMove _move = new ChallengeMove();

        int _first = -1;

        /// <summary>The two cells of the last completed turn and whether they matched, for the view.</summary>
        public int LastA { get; private set; } = -1;
        public int LastB { get; private set; } = -1;
        public bool LastMatched { get; private set; }

        public PairsPuzzle(ChallengeDefinition def)
        {
            Width = def.Width;
            Height = def.Height;
            _bolts = def.Bolts;

            _colour = new int[Width * Height];
            _face = new Face[_colour.Length];

            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    _colour[y * Width + x] = ChallengeColours.IndexOf(def.Rows[y][x]);
        }

        public ChallengeGenre Genre => ChallengeGenre.Pairs;
        public int Width { get; }
        public int Height { get; }

        public int ColourAt(int cell) => cell >= 0 && cell < _colour.Length ? _colour[cell] : -1;
        public Face FaceAt(int cell) => cell >= 0 && cell < _face.Length ? _face[cell] : Face.Hidden;

        /// <summary>The cell turned up and waiting for its partner, or -1.</summary>
        public int First => _first;

        public int Pairs => _colour.Length / 2;

        public int Matched
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _face.Length; i++) if (_face[i] == Face.Matched) n++;
                return n / 2;
            }
        }

        public bool Solved => Matched == Pairs;
        public bool Failed => false;

        public ChallengeMove Apply(ChallengeInput input)
        {
            _move.Clear();

            if (input.Kind != ChallengeInputKind.Tap || input.Cell < 0 || input.Cell >= _face.Length
                || _face[input.Cell] != Face.Hidden)
            {
                _move.Refused = true;
                return _move;
            }

            int cell = input.Cell;

            if (_first < 0)
            {
                _face[cell] = Face.Up;
                _first = cell;
                return _move;
            }

            int a = _first, b = cell;
            _first = -1;

            LastA = a;
            LastB = b;
            LastMatched = _colour[a] == _colour[b];

            if (LastMatched)
            {
                _face[a] = _face[b] = Face.Matched;
                _move.Feed(_colour[a], _bolts);
            }
            else
            {
                _face[a] = _face[b] = Face.Hidden;
            }

            _move.Turn = true;
            return _move;
        }

        public static string Fault(ChallengeDefinition def)
        {
            string rows = ChallengePuzzles.RowsFault(def, def.Rows, "pairs rows");
            if (rows != null) return rows;

            var counts = new int[ChallengeColours.Count];
            for (int y = 0; y < def.Height; y++)
            {
                for (int x = 0; x < def.Width; x++)
                {
                    int c = ChallengeColours.IndexOf(def.Rows[y][x]);
                    if (c < 0) return $"pairs row {y} holds '{def.Rows[y][x]}', which is not one of {ChallengeColours.Letters}";
                    counts[c]++;
                }
            }

            for (int c = 0; c < counts.Length; c++)
                if ((counts[c] & 1) == 1)
                    return $"pairs deals {counts[c]} {ChallengeColours.LetterOf(c)} gems, which cannot all pair";

            if (def.Width * def.Height < 2) return "pairs needs at least one pair";

            return null;
        }
    }
}
