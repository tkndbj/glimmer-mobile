using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// <b>Lightfall.</b> A well of coloured motes. You never match them — you <em>cook</em> them,
    /// and a mote that reaches white bursts and washes the colour that finished it into
    /// everything beside it.
    ///
    /// <para>
    /// <b>One verb with two branches, and the branch is what makes it a game.</b> A mote dropped
    /// onto a stack either <em>enriches</em> the top of it or <em>heightens</em> it. Red onto
    /// green makes yellow and the stack does not grow. Red onto yellow adds nothing — yellow
    /// already holds red — so the mote sits on top and the well is one row nearer its brim.
    /// Every drop therefore costs one of a finite supply and, if it was the wrong one, a row of
    /// headroom as well: one mistake, two meters, and both of them visible.
    /// </para>
    /// <para>
    /// <b>The wash is what makes a chain possible, and it is the rule this class was rewritten
    /// for.</b> The mode shipped with a detonation that took its white mote and the four motes
    /// touching it, and boasted in this very file about the cascades that set off. There were
    /// none, and there could not be: nothing here changes a mote's colour except a drop, so
    /// every white on the board was taken by the first wave and the second could never find one.
    /// The wave counter, the rising pitch and the chain multiplier were all dead code against a
    /// rule that rejects them. What replaced it is <em>one</em> destruction and a spread: a
    /// white mote bursts alone, and the motes beside it gain the channel that finished it. Any
    /// of them that is thereby completed bursts in turn — so a single well-chosen drop runs
    /// through a whole connected blob of motes that were all missing the same channel, which is
    /// the chain the mode was written as if it had.
    /// </para>
    /// <para>
    /// It also decides what the mode <em>is</em>. Dropping blue clears the yellows; the reds and
    /// greens it passes are left one step better rather than untouched; and a mote buried at the
    /// bottom of a column — which no drop can ever land on — is reached by the wash from its
    /// neighbours. That is what makes a full well solvable at all, and what makes which colour
    /// goes where the whole of the thinking.
    /// </para>
    /// <para>
    /// <b>No Unity types and no randomness.</b> The whole thing is provable offline, which
    /// matters because a falling-piece game is wrong in ways a screenshot cannot show — a
    /// gravity pass that settles in the wrong order, a wash applied after the fall rather than
    /// before it, a cascade that resolves one column at a time.
    /// </para>
    /// </summary>
    public sealed class FallBoard
    {
        public readonly int Width, Height;

        readonly int[] _cells;          // Energy mask per cell, 0 = empty
        int _motes;

        /// <summary>
        /// Scratch for one wave, never read across calls — and allocated only when a wave
        /// actually happens.
        ///
        /// The search forks a board per position it tries, hundreds of thousands of them, and
        /// most of those forks resolve nothing at all. Allocating this alongside the cells made
        /// every one of them pay for a wave that never came.
        /// </summary>
        bool[] _mark;

        public FallBoard(FallLayout layout)
        {
            Width = layout.Width;
            Height = layout.Height;
            _cells = layout.Fill();
            _motes = Count();
        }

        FallBoard(FallBoard other)
        {
            Width = other.Width;
            Height = other.Height;
            _cells = new int[other._cells.Length];
            System.Array.Copy(other._cells, _cells, _cells.Length);
            _motes = other._motes;
            Flooded = other.Flooded;
        }

        /// <summary>A private copy, for a search that wants to try a drop without taking it.</summary>
        public FallBoard Fork() => new FallBoard(this);

        /// <summary>The row a mote may not come to rest in. See <see cref="FallLayout"/>.</summary>
        public const int Brim = FallLayout.Brim;

        public int Index(int x, int y) => y * Width + x;
        public int At(int x, int y) => _cells[Index(x, y)];
        public int At(int index) => _cells[index];
        public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public int X(int index) => index % Width;
        public int Y(int index) => index / Width;

        /// <summary>Motes still standing. The goal is nought of them.</summary>
        public int Motes => _motes;

        /// <summary>The well is empty and the run is won.</summary>
        public bool IsEmpty => _motes == 0;

        /// <summary>
        /// A mote came to rest above the brim line.
        ///
        /// <para>
        /// Decided after a drop has fully resolved rather than at the instant of landing, and
        /// that generosity is deliberate: a mote that lands on the brim and immediately bursts
        /// has not flooded anything, and it is the most exciting thing that can happen on this
        /// board. Reading it at the landing would end the run in the frame before the save.
        /// </para>
        /// </summary>
        public bool Flooded { get; private set; }

        /// <summary>
        /// Safe rows the tallest column can still take before the well floods. Nought means the
        /// next careless drop anywhere on the tallest column ends the run.
        /// </summary>
        public int Headroom
        {
            get
            {
                int highest = Height;
                for (int x = 0; x < Width; x++)
                {
                    int top = TopOf(x);
                    if (top >= 0 && top < highest) highest = top;
                }

                int safe = highest - 1;
                return safe < 0 ? 0 : safe;
            }
        }

        /// <summary>Every channel some mote is still missing, as a mask. Nought on an empty well.</summary>
        public int Wanted
        {
            get
            {
                int mask = Energy.None;
                for (int i = 0; i < _cells.Length; i++)
                {
                    int mote = _cells[i];
                    if (mote != Energy.None) mask |= Energy.All & ~mote;
                }
                return mask;
            }
        }

        // ------------------------------------------------------------------ reading a column
        /// <summary>The row of the highest mote in a column, or -1 for an empty column.</summary>
        public int TopOf(int x)
        {
            for (int y = 0; y < Height; y++)
                if (_cells[Index(x, y)] != Energy.None) return y;
            return -1;
        }

        /// <summary>The lowest empty row in a column, or -1 when the column is full to the top.</summary>
        public int FirstFree(int x)
        {
            for (int y = Height - 1; y >= 0; y--)
                if (_cells[Index(x, y)] == Energy.None) return y;
            return -1;
        }

        /// <summary>
        /// Where a mote of this colour would come to rest: the top of the stack if it can enrich
        /// it, otherwise the first free cell above. -1 when the column cannot take one.
        ///
        /// <para>
        /// Worth being able to ask before committing — the screen draws a ghost of it under the
        /// player's thumb, and that preview is the whole reason this verb works where tapping a
        /// cell did not.
        /// </para>
        /// </summary>
        public int Landing(int colour, int x)
        {
            if (x < 0 || x >= Width || colour == Energy.None) return -1;

            int top = TopOf(x);
            if (top >= 0)
            {
                int mote = _cells[Index(x, top)];
                if ((mote | colour) != mote) return top;     // enriches what is already there
            }

            return FirstFree(x);                             // sits on top instead
        }

        /// <summary>Whether a drop here would enrich the stack rather than heighten it.</summary>
        public bool Enriches(int colour, int x)
        {
            int top = TopOf(x);
            if (top < 0) return false;

            int mote = _cells[Index(x, top)];
            return (mote | colour) != mote;
        }

        /// <summary>
        /// Whether this drop would light a mote all the way to white, which is what the ghost
        /// promises and what the ripe halo on the board already says.
        ///
        /// It says nothing about how far the chain would run. That is the player's to read, and
        /// showing it would be showing them the answer.
        /// </summary>
        public bool Bursts(int colour, int x)
        {
            int top = TopOf(x);
            if (top < 0) return false;

            int mote = _cells[Index(x, top)];
            return (mote | colour) == Energy.All;
        }

        /// <summary>
        /// Whether this drop would come to rest above the brim. A warning rather than a verdict
        /// — the mote may burst on arrival and save the well — but an honest one, because most
        /// of the time it will not.
        /// </summary>
        public bool AtBrim(int colour, int x)
        {
            int at = Landing(colour, x);
            return at == Brim;
        }

        /// <summary>A column with somewhere for a mote of this colour to go.</summary>
        public bool CanDrop(int colour, int x)
            => !Flooded && !IsEmpty && x >= 0 && x < Width && Landing(colour, x) >= 0;

        // ------------------------------------------------------------------ dropping
        /// <summary>
        /// Drops a mote into a column and resolves everything that follows.
        ///
        /// <para>
        /// <paramref name="steps"/> may be null, and that is what lets the search run the very
        /// code the game runs rather than a copy of it (invariant 9a, for a board rule). A
        /// screen hands a list and plays the waves a beat apart, because a board handed over
        /// settled is the same information with none of the feeling; a solver hands nothing and
        /// pays for no allocation.
        /// </para>
        /// </summary>
        public FallResolution Drop(int colour, int x, List<FallStep> steps = null)
        {
            if (!CanDrop(colour, x)) return null;

            int at = Landing(colour, x);
            int index = Index(x, at);
            bool enriched = _cells[index] != Energy.None;

            if (!enriched) _motes++;
            _cells[index] |= colour;

            Resolve(colour, steps, out int waves, out int burst);

            // Read after the whole cascade rather than at the landing. It cannot differ today —
            // a mote only ever comes to rest on the brim by *heightening*, and a heightened mote
            // is a pure colour that cannot burst — but a rule that reads the board after it has
            // finished moving is the one that stays right if the wash ever reaches further.
            Flooded = BrimBreached();

            return new FallResolution(x, at, colour, enriched, waves, burst, steps);
        }

        /// <summary>
        /// Walks the board to rest: every white mote bursts, the motes beside it gain the colour
        /// that finished it, everything falls, and whatever that completed bursts in turn.
        ///
        /// <para>
        /// <b>A whole wave is decided before any of it is applied.</b> The wash is read off the
        /// positions the bursting motes are standing in, so it cannot depend on which of them
        /// was scanned first, and it is applied before gravity, so a mote is washed where it was
        /// rather than where it ends up. Resolving one burst at a time would let a mote fall
        /// into a gap the next burst in the same wave was about to make, and the well would
        /// settle differently depending on which column happened to be walked first.
        /// </para>
        /// <para>
        /// <b>It terminates because every wave destroys at least one mote.</b> Whites are always
        /// taken, never washed, so the loop cannot find the same one twice.
        /// </para>
        /// </summary>
        void Resolve(int wash, List<FallStep> steps, out int waves, out int burstCount)
        {
            int wave = 0;
            burstCount = 0;

            while (true)
            {
                // ---- what is white, decided over the whole board before anything moves
                List<int> burst = null;
                for (int i = 0; i < _cells.Length; i++)
                {
                    if (_cells[i] != Energy.All) continue;
                    if (burst == null) burst = new List<int>();
                    burst.Add(i);
                }

                if (burst == null) break;

                // ---- what the burst washes: occupied neighbours that are not bursting
                //      themselves and that this colour would actually change. A mote that
                //      already holds the channel is left off the list rather than washed to no
                //      effect, so the animation cannot promise something the rules did not do.
                if (_mark == null) _mark = new bool[_cells.Length];
                else for (int i = 0; i < _mark.Length; i++) _mark[i] = false;

                for (int b = 0; b < burst.Count; b++) _mark[burst[b]] = true;

                List<int> washed = null;
                for (int b = 0; b < burst.Count; b++)
                {
                    int at = burst[b];
                    int x = at % Width, y = at / Width;

                    for (int n = 0; n < Neighbours.Length; n++)
                    {
                        int nx = x + Neighbours[n].dx, ny = y + Neighbours[n].dy;
                        if (!Inside(nx, ny)) continue;

                        int ni = Index(nx, ny);
                        if (_mark[ni]) continue;                       // bursting, or already listed

                        int mote = _cells[ni];
                        if (mote == Energy.None) continue;             // bare ground carries nothing
                        if ((mote | wash) == mote) continue;           // already holds it

                        _mark[ni] = true;
                        if (washed == null) washed = new List<int>();
                        washed.Add(ni);
                    }
                }

                // ---- apply, in the one order that has no reading order in it
                for (int b = 0; b < burst.Count; b++)
                {
                    _cells[burst[b]] = Energy.None;
                    _motes--;
                }

                burstCount += burst.Count;

                if (washed != null)
                    for (int w = 0; w < washed.Count; w++) _cells[washed[w]] |= wash;

                var moved = Settle();
                wave++;

                steps?.Add(new FallStep(burst, (IReadOnlyList<int>)washed ?? Empty, wave, moved));
            }

            waves = wave;
        }

        static readonly int[] Empty = new int[0];

        /// <summary>
        /// Lets everything fall into the gaps, and reports what moved so the screen can animate
        /// the collapse rather than teleporting the board.
        /// </summary>
        IReadOnlyList<FallMove> Settle()
        {
            List<FallMove> moved = null;

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
                        if (moved == null) moved = new List<FallMove>();
                        moved.Add(new FallMove(at, to));
                    }

                    write--;
                }
            }

            return moved == null ? NoMoves : (IReadOnlyList<FallMove>)moved;
        }

        static readonly FallMove[] NoMoves = new FallMove[0];

        bool BrimBreached()
        {
            for (int x = 0; x < Width; x++)
                if (_cells[Index(x, Brim)] != Energy.None) return true;
            return false;
        }

        int Count()
        {
            int n = 0;
            for (int i = 0; i < _cells.Length; i++) if (_cells[i] != Energy.None) n++;
            return n;
        }

        static readonly (int dx, int dy)[] Neighbours = { (0, -1), (1, 0), (0, 1), (-1, 0) };

        // ------------------------------------------------------------------ for the search
        /// <summary>
        /// A 64-bit fingerprint of what is standing in the well, so a search can recognise a
        /// position it has already been in.
        ///
        /// <para>
        /// FNV-1a, and a hash rather than the cells themselves on purpose: a search holds
        /// hundreds of thousands of these and a phone runs it at level load, so the difference
        /// between eight bytes and a hundred is the difference between a search that fits and
        /// one that does not. Collisions are the price and they are negligible — a quarter of a
        /// million entries in a 64-bit space collide with probability around two in a billion,
        /// and the consequence of one would be a par a single drop out, which the build gate
        /// would have to have passed first.
        /// </para>
        /// </summary>
        public ulong Signature()
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                for (int i = 0; i < _cells.Length; i++)
                {
                    hash ^= (ulong)(uint)(_cells[i] + 1);
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }
    }

    /// <summary>Where a mote landed and what it set off.</summary>
    public sealed class FallResolution
    {
        public readonly int Column, Row, Colour;

        /// <summary>Whether it enriched the mote it landed on rather than sitting above it.</summary>
        public readonly bool Enriched;

        /// <summary>
        /// How far the chain ran. One is a burst; more than one is worth shouting about.
        ///
        /// <para>
        /// <b>Counted rather than read off <see cref="Steps"/></b>, and that is not tidiness. A
        /// caller that wants the number and not the choreography passes no step list — the
        /// search does exactly that, hundreds of thousands of times — and deriving this from the
        /// list would answer nought for every one of them. It did, and the first thing that
        /// noticed was the test that asked whether a burst had happened at all.
        /// </para>
        /// </summary>
        public readonly int Waves;

        /// <summary>Motes this drop destroyed, over every wave. Counted for <see cref="Waves"/>' reason.</summary>
        public readonly int Burst;

        /// <summary>
        /// The waves in order, for a screen that has to play them a beat apart. Empty when the
        /// caller asked for none — see <see cref="Waves"/>.
        /// </summary>
        public readonly IReadOnlyList<FallStep> Steps;

        static readonly FallStep[] None = new FallStep[0];

        public FallResolution(int column, int row, int colour, bool enriched,
                              int waves, int burst, IReadOnlyList<FallStep> steps)
        {
            Column = column;
            Row = row;
            Colour = colour;
            Enriched = enriched;
            Waves = waves;
            Burst = burst;
            Steps = steps ?? None;
        }
    }

    /// <summary>One wave: what burst, what the light washed, and what fell afterwards.</summary>
    public readonly struct FallStep
    {
        /// <summary>Cells that reached white and were destroyed, at the positions they stood in.</summary>
        public readonly IReadOnlyList<int> Burst;

        /// <summary>Cells the light reached and changed, at the positions they stood in.</summary>
        public readonly IReadOnlyList<int> Washed;

        /// <summary>Which wave of this drop's chain, counting from one.</summary>
        public readonly int Wave;

        /// <summary>What slid where once the gaps opened.</summary>
        public readonly IReadOnlyList<FallMove> Moved;

        public FallStep(IReadOnlyList<int> burst, IReadOnlyList<int> washed, int wave,
                        IReadOnlyList<FallMove> moved)
        {
            Burst = burst;
            Washed = washed;
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
