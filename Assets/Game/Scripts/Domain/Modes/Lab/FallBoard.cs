using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// <b>Lightfall.</b> Motes of coloured light drop into columns. You never match them — you
    /// <em>cook</em> them, and a stack that reaches white detonates.
    ///
    /// <para>
    /// The whole game is one rule with two branches, and the branch is what makes it a game:
    /// a mote dropped onto a stack either <b>enriches</b> the top of it, or <b>heightens</b> it.
    /// Red onto green makes yellow and the stack does not grow. Red onto yellow adds nothing —
    /// yellow already contains red — so the mote sits on top and you are one row nearer the
    /// ceiling. Every drop is therefore a real decision with a visible cost, which is the thing
    /// tapping a cell never had.
    /// </para>
    /// <para>
    /// White is the goal and the reward. Completing all three channels detonates the mote and
    /// everything orthogonally touching it, the stack above falls into the gap, and what lands
    /// can complete something else — so a good drop pays out in a cascade the player set up but
    /// did not fully predict. That is Tetris's clear and Candy Crush's chain in one move.
    /// </para>
    /// <para>
    /// No Unity types: the whole thing is provable offline, which matters because a falling-piece
    /// game is wrong in ways a screenshot cannot show — a gravity pass that settles in the wrong
    /// order, a cascade that resolves one column at a time, a detonation that eats its own
    /// neighbours twice.
    /// </para>
    /// </summary>
    public sealed class FallBoard
    {
        public readonly int Width, Height;

        readonly int[] _cells;          // Energy mask per cell, 0 = empty
        readonly List<int> _queue = new List<int>();
        uint _seed;

        public FallBoard(int width, int height, uint seed)
        {
            Width = width;
            Height = height;
            _cells = new int[width * height];
            _seed = seed == 0 ? 2463534242u : seed;

            for (int i = 0; i < Lookahead; i++) _queue.Add(RollColour());
        }

        /// <summary>How many motes the player can see coming. Three is enough to plan, few enough to hold.</summary>
        public const int Lookahead = 3;

        /// <summary>Cleared motes needed before the fall speeds up. Prototype pacing.</summary>
        public const int StepEvery = 24;

        public int Index(int x, int y) => y * Width + x;
        public int At(int x, int y) => _cells[Index(x, y)];
        public int At(int index) => _cells[index];
        public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        /// <summary>The mote about to fall.</summary>
        public int Next => _queue.Count > 0 ? _queue[0] : Energy.None;

        /// <summary>What is queued behind it, for the preview.</summary>
        public int Ahead(int n) => n >= 0 && n < _queue.Count ? _queue[n] : Energy.None;

        public int Score { get; private set; }
        public int Cleared { get; private set; }
        public int Drops { get; private set; }

        /// <summary>The biggest single chain this run — the number worth shouting about.</summary>
        public int Best { get; private set; }

        /// <summary>A column is full to the brim and the run is over.</summary>
        public bool IsLost { get; private set; }

        /// <summary>How high the tallest column stands, for the pressure readout.</summary>
        public int Tallest
        {
            get
            {
                int tallest = 0;
                for (int x = 0; x < Width; x++)
                {
                    int h = Height - FirstFree(x) - 1;
                    if (h > tallest) tallest = h;
                }
                return tallest;
            }
        }

        // ------------------------------------------------------------------ dropping
        /// <summary>
        /// The row a mote dropped into this column would come to rest on, or -1 if the column is
        /// full. Rows count from the top, so a taller stack is a smaller number.
        /// </summary>
        public int FirstFree(int x)
        {
            for (int y = Height - 1; y >= 0; y--)
                if (_cells[Index(x, y)] == Energy.None) return y;
            return -1;
        }

        public bool CanDrop(int x) => !IsLost && x >= 0 && x < Width && Landing(x) >= 0;

        /// <summary>
        /// Where the mote actually ends up: the top of the stack if it can enrich it, otherwise
        /// the first free cell above. This is the rule, and it is worth being able to ask before
        /// committing — the screen draws a ghost of it under the player's thumb.
        /// </summary>
        public int Landing(int x)
        {
            int top = TopOf(x);
            if (top >= 0)
            {
                int mote = _cells[Index(x, top)];
                if ((mote | Next) != mote) return top;      // enriches what is already there
            }

            return FirstFree(x);                            // sits on top instead
        }

        /// <summary>Whether a drop here would enrich the stack rather than heighten it.</summary>
        public bool Enriches(int x)
        {
            int top = TopOf(x);
            if (top < 0) return false;

            int mote = _cells[Index(x, top)];
            return (mote | Next) != mote;
        }

        /// <summary>The row of the highest mote in a column, or -1 for an empty column.</summary>
        public int TopOf(int x)
        {
            for (int y = 0; y < Height; y++)
                if (_cells[Index(x, y)] != Energy.None) return y;
            return -1;
        }

        /// <summary>
        /// Drops the next mote into a column and resolves everything that follows.
        ///
        /// Returns the steps of the resolution in order, so the screen can play them a beat
        /// apart rather than showing the settled board. The steps are the reward — a detonation,
        /// the fall into the gap, and whatever that completes — and a board handed over settled
        /// is the same information with none of the feeling.
        /// </summary>
        public FallResolution Drop(int x)
        {
            if (!CanDrop(x)) return null;

            // Worked out *before* the queue moves, and that ordering is the whole correctness of
            // the preview. It shipped the other way round: the queue advanced first, so Landing
            // answered for the *next* mote rather than the one being dropped, and a mote could
            // come to rest somewhere other than the ghost the player was shown — which is the
            // game lying at the exact moment somebody is deciding.
            int colour = Next;
            int at = Landing(x);
            int index = Index(x, at);
            bool enriched = _cells[index] != Energy.None;

            _queue.RemoveAt(0);
            _queue.Add(RollColour());
            Drops++;

            _cells[index] = _cells[index] | colour;

            var steps = new List<FallStep>();
            Resolve(index, steps);

            // Asked of every column rather than only the one just dropped into: a detonation
            // moves motes, and the column that fills need not be the one that was touched.
            for (int c = 0; c < Width; c++)
                if (FirstFree(c) < 0) IsLost = true;

            return new FallResolution(x, at, colour, enriched, steps);
        }

        /// <summary>
        /// Walks the board to rest: anything white detonates with its neighbours, everything
        /// above falls, and whatever that completes detonates in turn.
        ///
        /// A whole wave is collected before any of it falls, for the reason a hollow's ring is:
        /// resolving one detonation at a time would drop motes into a gap that the next
        /// detonation in the same wave was about to make, and the board would settle differently
        /// depending on which column was scanned first.
        /// </summary>
        void Resolve(int seed, List<FallStep> steps)
        {
            int wave = 0;

            while (true)
            {
                var white = new List<int>();
                for (int i = 0; i < _cells.Length; i++)
                    if (_cells[i] == Energy.All) white.Add(i);

                if (white.Count == 0) break;

                // Everything the wave takes: the white motes and whatever touches them.
                var taken = new List<int>();
                foreach (int at in white)
                {
                    if (!taken.Contains(at)) taken.Add(at);

                    int x = at % Width, y = at / Width;
                    for (int n = 0; n < Neighbours.Length; n++)
                    {
                        int nx = x + Neighbours[n].dx, ny = y + Neighbours[n].dy;
                        if (!Inside(nx, ny)) continue;

                        int ni = Index(nx, ny);
                        if (_cells[ni] != Energy.None && !taken.Contains(ni)) taken.Add(ni);
                    }
                }

                foreach (int at in taken) _cells[at] = Energy.None;

                wave++;
                Cleared += taken.Count;
                if (taken.Count > Best) Best = taken.Count;

                // A later wave in the same drop is worth more, which is what makes setting up a
                // chain worth more than clearing the same motes one at a time.
                Score += taken.Count * 10 * wave;

                steps.Add(new FallStep(taken, wave, Settle()));
            }
        }

        /// <summary>
        /// Lets everything fall into the gaps, and reports what moved so the screen can animate
        /// it rather than teleporting the board.
        /// </summary>
        List<FallMove> Settle()
        {
            var moved = new List<FallMove>();

            for (int x = 0; x < Width; x++)
            {
                int write = Height - 1;
                for (int y = Height - 1; y >= 0; y--)
                {
                    int at = Index(x, y);
                    if (_cells[at] == Energy.None) continue;

                    if (y != write)
                    {
                        int to = Index(x, write);
                        _cells[to] = _cells[at];
                        _cells[at] = Energy.None;
                        moved.Add(new FallMove(at, to));
                    }
                    write--;
                }
            }

            return moved;
        }

        static readonly (int dx, int dy)[] Neighbours = { (0, -1), (1, 0), (0, 1), (-1, 0) };

        // ------------------------------------------------------------------ the deal
        /// <summary>
        /// Pure colours only, and never a blend.
        ///
        /// Dealing yellow would hand the player a step of the cooking for free, and the whole
        /// game is that a secondary colour has to be <em>made</em>. It is also what keeps the
        /// rule sayable in one line.
        /// </summary>
        int RollColour()
        {
            switch (Roll(3))
            {
                case 0: return Energy.R;
                case 1: return Energy.G;
                default: return Energy.B;
            }
        }

        uint Next32()
        {
            _seed ^= _seed << 13;
            _seed ^= _seed >> 17;
            _seed ^= _seed << 5;
            return _seed;
        }

        int Roll(int bound) => (int)(Next32() % (uint)bound);
    }

    /// <summary>Where a mote landed and what it set off.</summary>
    public sealed class FallResolution
    {
        public readonly int Column, Row, Colour;

        /// <summary>Whether it enriched the mote it landed on rather than sitting above it.</summary>
        public readonly bool Enriched;

        public readonly IReadOnlyList<FallStep> Steps;

        public FallResolution(int column, int row, int colour, bool enriched,
                              IReadOnlyList<FallStep> steps)
        {
            Column = column;
            Row = row;
            Colour = colour;
            Enriched = enriched;
            Steps = steps;
        }

        public int Waves => Steps?.Count ?? 0;

        public int Taken
        {
            get
            {
                int n = 0;
                if (Steps != null) foreach (var s in Steps) n += s.Taken.Count;
                return n;
            }
        }
    }

    /// <summary>One wave: what it took, and what fell afterwards.</summary>
    public readonly struct FallStep
    {
        public readonly IReadOnlyList<int> Taken;
        public readonly int Wave;
        public readonly IReadOnlyList<FallMove> Moved;

        public FallStep(IReadOnlyList<int> taken, int wave, IReadOnlyList<FallMove> moved)
        {
            Taken = taken;
            Wave = wave;
            Moved = moved;
        }
    }

    /// <summary>A mote sliding from one cell to another as the stack falls.</summary>
    public readonly struct FallMove
    {
        public readonly int From, To;
        public FallMove(int from, int to) { From = from; To = to; }
    }
}
