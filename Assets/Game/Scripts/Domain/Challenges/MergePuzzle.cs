using System.Collections.Generic;

namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// One gem's journey on one slide: the cell it left, the cell it packed against, and the
    /// rank it carried on the way. See <see cref="MergePuzzle.LastSlides"/>.
    /// </summary>
    public readonly struct MergeSlide
    {
        public readonly int From, To, Rank;

        public MergeSlide(int from, int to, int rank)
        {
            From = from;
            To = to;
            Rank = rank;
        }

        /// <summary>Whether the gem travelled at all, or only met a partner where it stood.</summary>
        public bool Moved => From != To;
    }

    /// <summary>
    /// Merge (2048): slide the board; two gems of a rank meet and become the next rank.
    ///
    /// <para>
    /// <b>The fusion: a gem's colour is its rank, and a merge fires the colour it made.</b>
    /// Rank one is red, two green, three blue, four yellow, five red again — so which turret a
    /// slide feeds is which rank it merges, and a hill of red raiders is asking for small
    /// merges while a yellow wave wants fours. Every merge feeds <c>Bolts</c>; a slide that
    /// moves nothing is refused and costs no step.
    /// </para>
    /// <para>
    /// <b>Won at a rank</b> (<c>target</c>), lost when no slide is left. The deal after each
    /// slide is one rank-one gem, or one in ten a rank two, at a cell drawn off the row's own
    /// stream — so a fixture replays exactly the board a device meets.
    /// </para>
    /// <para>
    /// <b>The alphabet</b>: <c>.</c> empty, a digit <c>1</c>-<c>9</c> a gem of that rank.
    /// </para>
    /// </summary>
    public sealed class MergePuzzle : IChallengePuzzle
    {
        readonly int[] _rank;
        readonly int _bolts;
        readonly ChallengeMove _move = new ChallengeMove();
        readonly List<int> _merged = new List<int>(8);
        readonly List<MergeSlide> _slides = new List<MergeSlide>(24);

        ChallengeRng _rng;

        /// <summary>Cells a merge landed on last turn, for the view to pop.</summary>
        public IReadOnlyList<int> LastMerged => _merged;

        /// <summary>
        /// Where every gem that moved or met another went on the last slide, in the order the
        /// lines were walked. <b>The trace exists because a settled board cannot say it</b>: the
        /// state after a slide holds where each gem ended and has no opinion about where it
        /// came from, so only the move that made it knows both ends (the rule every moving
        /// board in this game keeps, <c>CRAFT.md</c>). A gem that neither moved nor merged is
        /// not listed. Both halves of a merge are listed with the same <c>To</c>, each with the
        /// rank it carried <em>before</em> they met.
        /// </summary>
        public IReadOnlyList<MergeSlide> LastSlides => _slides;

        /// <summary>The cell dealt last turn, or -1.</summary>
        public int LastDealt { get; private set; } = -1;

        /// <summary>The rank the dealt gem carries, or nought when nothing was dealt.</summary>
        public int LastDealtRank { get; private set; }

        public int Target { get; }

        public MergePuzzle(ChallengeDefinition def)
        {
            Width = def.Width;
            Height = def.Height;
            _bolts = def.Bolts;
            Target = def.Target;
            _rng = new ChallengeRng(def.Seed);

            _rank = new int[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    char ch = def.Rows[y][x];
                    _rank[y * Width + x] = ch >= '1' && ch <= '9' ? ch - '0' : 0;
                }
        }

        public ChallengeGenre Genre => ChallengeGenre.Merge;
        public int Width { get; }
        public int Height { get; }

        public int RankAt(int cell) => cell >= 0 && cell < _rank.Length ? _rank[cell] : 0;

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

        public bool Failed
        {
            get
            {
                for (int i = 0; i < _rank.Length; i++) if (_rank[i] == 0) return false;

                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        int r = _rank[y * Width + x];
                        if (x + 1 < Width && _rank[y * Width + x + 1] == r) return false;
                        if (y + 1 < Height && _rank[(y + 1) * Width + x] == r) return false;
                    }

                return true;
            }
        }

        public ChallengeMove Apply(ChallengeInput input)
        {
            _move.Clear();
            _merged.Clear();
            _slides.Clear();
            LastDealt = -1;
            LastDealtRank = 0;

            if (input.Kind != ChallengeInputKind.Swipe || (input.Dx == 0) == (input.Dy == 0))
            {
                _move.Refused = true;
                return _move;
            }

            // Screen up is row minus one.
            int dx = input.Dx > 0 ? 1 : input.Dx < 0 ? -1 : 0;
            int dy = input.Dy > 0 ? -1 : input.Dy < 0 ? 1 : 0;

            bool moved = Slide(dx, dy);
            if (!moved)
            {
                _move.Refused = true;
                return _move;
            }

            for (int i = 0; i < _merged.Count; i++)
                _move.Feed(ColourOf(_rank[_merged[i]]), _bolts);

            Deal();

            _move.Turn = true;
            return _move;
        }

        /// <summary>
        /// One slide: every line is walked from the far wall, gems pack against it and a pair
        /// of equal ranks meeting merges once. True if anything moved.
        /// </summary>
        bool Slide(int dx, int dy)
        {
            bool moved = false;
            int lines = dx != 0 ? Height : Width;
            int length = dx != 0 ? Width : Height;

            var line = new int[length];
            var from = new int[length];

            for (int l = 0; l < lines; l++)
            {
                // The cells of this line, in the order they pack (the first is against the wall).
                for (int i = 0; i < length; i++)
                {
                    int x, y;
                    if (dx != 0) { y = l; x = dx > 0 ? length - 1 - i : i; }
                    else { x = l; y = dy > 0 ? length - 1 - i : i; }
                    from[i] = y * Width + x;
                    line[i] = _rank[from[i]];
                }

                int write = 0;
                bool lastMerged = false;
                var packed = new int[length];
                var mergedAt = new bool[length];
                var dest = new int[length];

                for (int i = 0; i < length; i++)
                {
                    int r = line[i];
                    if (r == 0) continue;

                    if (write > 0 && packed[write - 1] == r && !lastMerged)
                    {
                        packed[write - 1] = r + 1;
                        mergedAt[write - 1] = true;
                        lastMerged = true;
                        dest[i] = write - 1;
                    }
                    else
                    {
                        dest[i] = write;
                        packed[write++] = r;
                        lastMerged = false;
                    }
                }

                // The trace, read before the line is written back so every rank is the one the
                // gem carried on the way. A gem that stood still and met nothing is left out.
                for (int i = 0; i < length; i++)
                {
                    if (line[i] == 0) continue;
                    if (dest[i] == i && !mergedAt[i]) continue;
                    _slides.Add(new MergeSlide(from[i], from[dest[i]], line[i]));
                }

                for (int i = 0; i < length; i++)
                {
                    if (_rank[from[i]] != packed[i]) moved = true;
                    _rank[from[i]] = packed[i];
                    if (mergedAt[i]) _merged.Add(from[i]);
                }
            }

            return moved;
        }

        void Deal()
        {
            int empty = 0;
            for (int i = 0; i < _rank.Length; i++) if (_rank[i] == 0) empty++;
            if (empty == 0) return;

            int pick = _rng.Below(empty);
            int rank = _rng.Below(10) == 0 ? 2 : 1;

            for (int i = 0; i < _rank.Length; i++)
            {
                if (_rank[i] != 0) continue;
                if (pick-- == 0)
                {
                    _rank[i] = rank;
                    LastDealt = i;
                    LastDealtRank = rank;
                    return;
                }
            }
        }

        public static string Fault(ChallengeDefinition def)
        {
            if (def.Width < 2 || def.Height < 2) return "merge needs a board at least 2x2";

            string rows = ChallengePuzzles.RowsFault(def, def.Rows, "merge rows");
            if (rows != null) return rows;

            int gems = 0, best = 0;
            for (int y = 0; y < def.Height; y++)
            {
                for (int x = 0; x < def.Width; x++)
                {
                    char ch = def.Rows[y][x];
                    if (ch == '.') continue;
                    if (ch < '1' || ch > '9') return $"merge row {y} holds '{ch}', which is not '.' or a rank 1-9";
                    gems++;
                    if (ch - '0' > best) best = ch - '0';
                }
            }

            if (gems == 0) return "merge deals no gem to start";
            if (def.Target <= best) return $"merge target {def.Target} is already on the board";
            if (def.Target < 2) return "merge needs a target rank of at least 2";

            return null;
        }
    }
}
