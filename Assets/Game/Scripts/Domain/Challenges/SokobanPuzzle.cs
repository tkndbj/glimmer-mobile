namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// Sokoban: push each gem onto the pad of its colour.
    ///
    /// <para>
    /// <b>The fusion: a gem standing on its pad arms that turret</b>, which fires <c>Bolts</c>
    /// every step for as long as the gem stays there. Every step the keeper takes is a turn,
    /// pushing or not, so the route is the whole price: a long way round is raiders walking,
    /// and a gem parked on its pad early is a turret working while the rest are solved.
    /// </para>
    /// <para>
    /// <b>A push into a wall or a second gem is refused</b> and costs nothing; a gem in a
    /// corner is not detected, because the way out of a dead board is the restart key and
    /// pretending otherwise would be a rule the player cannot see.
    /// </para>
    /// <para>
    /// <b>Two layers</b>: <c>rows</c> — <c>#</c> wall, <c>.</c> floor, <c>R G B Y</c> a pad,
    /// <c>@</c> where the keeper starts — and <c>gems</c>, the same size, <c>.</c> or
    /// <c>r g b y</c> for a gem standing there when the board opens.
    /// </para>
    /// </summary>
    public sealed class SokobanPuzzle : IChallengePuzzle
    {
        readonly bool[] _wall;
        readonly int[] _pad, _gem;
        readonly int _bolts;
        readonly ChallengeMove _move = new ChallengeMove();

        public int Keeper { get; private set; }

        /// <summary>Where the keeper stood before the last step, for the view.</summary>
        public int LastFrom { get; private set; } = -1;

        /// <summary>The gem cell the last step pushed from and to, or -1.</summary>
        public int LastPushFrom { get; private set; } = -1;
        public int LastPushTo { get; private set; } = -1;

        public SokobanPuzzle(ChallengeDefinition def)
        {
            Width = def.Width;
            Height = def.Height;
            _bolts = def.Bolts;

            int n = Width * Height;
            _wall = new bool[n];
            _pad = new int[n];
            _gem = new int[n];

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int at = y * Width + x;
                    char ch = def.Rows[y][x];

                    _pad[at] = -1;
                    _gem[at] = -1;

                    if (ch == '#') _wall[at] = true;
                    else if (ch == '@') Keeper = at;
                    else if (char.IsUpper(ch)) _pad[at] = ChallengeColours.IndexOf(char.ToLowerInvariant(ch));

                    if (def.Gems.Length > y && def.Gems[y].Length > x)
                        _gem[at] = ChallengeColours.IndexOf(def.Gems[y][x]);
                }
            }
        }

        public ChallengeGenre Genre => ChallengeGenre.Sokoban;
        public int Width { get; }
        public int Height { get; }

        public bool IsWall(int cell) => cell >= 0 && cell < _wall.Length && _wall[cell];
        public int PadAt(int cell) => cell >= 0 && cell < _pad.Length ? _pad[cell] : -1;
        public int GemAt(int cell) => cell >= 0 && cell < _gem.Length ? _gem[cell] : -1;

        /// <summary>Whether a gem stands on a pad of its own colour.</summary>
        public bool Seated(int cell) => GemAt(cell) >= 0 && GemAt(cell) == PadAt(cell);

        public int Pads
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _pad.Length; i++) if (_pad[i] >= 0) n++;
                return n;
            }
        }

        public int SeatedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _gem.Length; i++) if (Seated(i)) n++;
                return n;
            }
        }

        public bool Solved => SeatedCount == Pads;
        public bool Failed => false;

        public ChallengeMove Apply(ChallengeInput input)
        {
            _move.Clear();
            LastPushFrom = LastPushTo = -1;

            if (input.Kind != ChallengeInputKind.Swipe || (input.Dx == 0) == (input.Dy == 0))
            {
                _move.Refused = true;
                return _move;
            }

            int dx = input.Dx > 0 ? 1 : input.Dx < 0 ? -1 : 0;
            int dy = input.Dy > 0 ? -1 : input.Dy < 0 ? 1 : 0;

            int kx = Keeper % Width, ky = Keeper / Width;
            int tx = kx + dx, ty = ky + dy;
            if (!Inside(tx, ty) || _wall[ty * Width + tx]) { _move.Refused = true; return _move; }

            int target = ty * Width + tx;

            if (_gem[target] >= 0)
            {
                int bx = tx + dx, by = ty + dy;
                if (!Inside(bx, by)) { _move.Refused = true; return _move; }

                int beyond = by * Width + bx;
                if (_wall[beyond] || _gem[beyond] >= 0) { _move.Refused = true; return _move; }

                _gem[beyond] = _gem[target];
                _gem[target] = -1;
                LastPushFrom = target;
                LastPushTo = beyond;
            }

            LastFrom = Keeper;
            Keeper = target;
            _move.Turn = true;

            for (int i = 0; i < _gem.Length; i++)
                if (Seated(i)) _move.Feed(_gem[i], _bolts);

            return _move;
        }

        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public static string Fault(ChallengeDefinition def)
        {
            string rows = ChallengePuzzles.RowsFault(def, def.Rows, "sokoban rows")
                          ?? ChallengePuzzles.RowsFault(def, def.Gems, "sokoban gems");
            if (rows != null) return rows;

            int keepers = 0;
            var pads = new int[ChallengeColours.Count];
            var gems = new int[ChallengeColours.Count];

            for (int y = 0; y < def.Height; y++)
            {
                for (int x = 0; x < def.Width; x++)
                {
                    char ch = def.Rows[y][x];
                    char g = def.Gems[y][x];

                    if (ch == '@') keepers++;
                    else if (ch != '#' && ch != '.')
                    {
                        int c = char.IsUpper(ch) ? ChallengeColours.IndexOf(char.ToLowerInvariant(ch)) : -1;
                        if (c < 0) return $"sokoban row {y} holds '{ch}', which is not #, ., @ or a pad letter R G B Y";
                        pads[c]++;
                    }

                    if (g != '.')
                    {
                        int c = ChallengeColours.IndexOf(g);
                        if (c < 0) return $"sokoban gems row {y} holds '{g}', which is not '.' or one of {ChallengeColours.Letters}";
                        if (ch == '#') return $"sokoban has a gem inside a wall at row {y} cell {x}";
                        if (ch == '@') return $"sokoban has a gem on the keeper at row {y} cell {x}";
                        gems[c]++;
                    }
                }
            }

            if (keepers != 1) return $"sokoban needs exactly one '@', has {keepers}";

            int total = 0;
            for (int c = 0; c < pads.Length; c++)
            {
                if (pads[c] != gems[c])
                    return $"sokoban has {pads[c]} {ChallengeColours.LetterOf(c)} pad(s) and {gems[c]} gem(s)";
                total += pads[c];
            }

            if (total == 0) return "sokoban has no pad";

            if (new SokobanPuzzle(def).Solved) return "sokoban opens with every gem already on its pad";

            return null;
        }
    }
}
