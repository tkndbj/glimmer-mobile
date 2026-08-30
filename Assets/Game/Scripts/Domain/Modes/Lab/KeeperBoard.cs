using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What one planting made: which tiles burst into bloom, how many of them were beds, and how
    /// many seams of new colour it lit.
    /// </summary>
    public readonly struct KeeperGain
    {
        /// <summary>Tiles that bloomed because of this planting, the planted one included.</summary>
        public readonly int Blooms;

        /// <summary>How many of those blooms opened a bed. The only half the goal counts.</summary>
        public readonly int Beds;

        /// <summary>Edges of unlike colour this planting lit. What the grove is made of.</summary>
        public readonly int Seams;

        public KeeperGain(int blooms, int beds, int seams)
        {
            Blooms = blooms;
            Beds = beds;
            Seams = seams;
        }

        /// <summary>Nothing happened here — the reading of a cell nothing may be planted on.</summary>
        public static readonly KeeperGain Nothing = new KeeperGain(0, 0, 0);

        public bool Any => Blooms > 0 || Seams > 0;

        public override string ToString() => $"{Blooms} bloom(s), {Beds} bed(s), {Seams} seam(s)";
    }

    /// <summary>
    /// <b>Groovekeeper.</b> You are handed tiles of coloured light, one at a time and in an order
    /// you can see coming, and you lay them out to grow a grove. Every edge-matching game ever
    /// made rewards putting like against like; this one rewards the opposite.
    ///
    /// <para>
    /// <b>A seam between two unlike colours is worth something and a seam between two of the same
    /// is worth nothing</b>, and everything else follows from that one inversion. A tile whose
    /// own colour and its neighbours' between them carry all three channels <b>blooms</b> — and
    /// the goal is not a score, it is the <b>beds</b>: cells the author marked, each of which has
    /// to end up holding a bloomed tile. So the question every turn is not "where does this fit"
    /// but "what does this one complete, and what does it leave the next one able to complete".
    /// </para>
    /// <para>
    /// <b>Planting one tile can bloom five.</b> A planting is checked against the cell it lands
    /// on <em>and</em> the four beside it, because a tile that was one channel short a moment ago
    /// is not short any more. That is where the mode's best moment lives: arrange a cross of beds
    /// around one hole, hold the colour they are all missing, and open four of them with a single
    /// tile. It is also exactly what par rewards — see <see cref="KeeperSolver"/> — so the
    /// prettiest play and the most efficient one are the same play.
    /// </para>
    /// <para>
    /// <b>Blooming is derived rather than stored, and that is what makes the search tractable.</b>
    /// Nothing is ever taken off this board, so a tile's neighbourhood only ever grows: "has this
    /// bloomed" is therefore a pure function of the grid at any moment, and a solver's state is
    /// the grid and nothing else. A stored flag would be a second answer for the two to disagree
    /// about (invariant 9a) and would double the state space for nothing.
    /// </para>
    /// <para>
    /// <b>There is no undo, on purpose.</b> A planting is permanent, exactly as a Lightfall drop
    /// is, and for the same reason: the mode hands over its whole future — the procession is
    /// visible and <see cref="Preview"/> says precisely what a cell would make before it is
    /// committed — so a wrong tile is a misjudgement rather than a surprise, and a fail state
    /// that can be walked out of for nothing rejects nothing (invariant 5d). What a player has
    /// instead is <see cref="KeeperRun.Compost"/>, which spends a tile to move the procession on
    /// — a real decision with a real price rather than a way of taking one back.
    /// </para>
    /// </summary>
    public sealed class KeeperBoard
    {
        public readonly KeeperLayout Layout;

        readonly int[] _cells;
        readonly List<int> _beside = new List<int>(4);
        readonly List<int> _scratch = new List<int>(5);

        public KeeperBoard(KeeperLayout layout)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _cells = layout.Standing();
        }

        /// <summary>A board holding exactly what another one holds, for a search that branches.</summary>
        public KeeperBoard(KeeperBoard other)
        {
            Layout = other.Layout;
            _cells = new int[other._cells.Length];
            Array.Copy(other._cells, _cells, _cells.Length);
        }

        public int Width => Layout.Width;
        public int Height => Layout.Height;
        public int Count => _cells.Length;

        public int Index(int x, int y) => Layout.Index(x, y);

        /// <summary>The tile standing here, or <see cref="Energy.None"/> for empty ground.</summary>
        public int At(int index) => _cells[index];
        public int At(int x, int y) => _cells[Layout.Index(x, y)];

        public bool Standing(int index) => _cells[index] != Energy.None;

        /// <summary>The grid itself, for a solver that wants to key a state on it. Never written to.</summary>
        public int[] Cells => _cells;

        // ------------------------------------------------------------------ blooming
        /// <summary>
        /// Every channel this cell's tile has within reach: its own colour and its neighbours'.
        ///
        /// Nought for empty ground, which is what makes <see cref="Bloomed"/> false there without
        /// a second test.
        /// </summary>
        public int Gathered(int index) => Gathered(index, -1);

        /// <summary>
        /// The same reading with one neighbour left out of it, which is how a planting knows what
        /// it <em>changed</em> rather than only what is true now.
        ///
        /// <para>
        /// Walked by arithmetic rather than through <c>Layout.Beside</c>, and that is not a
        /// micro-optimisation: this runs inside the solver's inner loop, where a shared scratch
        /// list is both an allocation and a trap — the obvious version overwrote the very list
        /// the caller was iterating.
        /// </para>
        /// </summary>
        int Gathered(int index, int except)
        {
            if (_cells[index] == Energy.None) return Energy.None;

            int mask = _cells[index];
            int width = Width;
            int x = index % width, y = index / width;

            if (y > 0 && index - width != except) mask |= _cells[index - width];
            if (x < width - 1 && index + 1 != except) mask |= _cells[index + 1];
            if (y < Height - 1 && index + width != except) mask |= _cells[index + width];
            if (x > 0 && index - 1 != except) mask |= _cells[index - 1];

            return mask;
        }

        /// <summary>
        /// The channels this tile is still short of, for the halo that says what it is waiting
        /// for. Nought on empty ground and on anything already bloomed.
        /// </summary>
        public int Wanting(int index)
        {
            int gathered = Gathered(index);
            return gathered == Energy.None ? Energy.None : Energy.All & ~gathered;
        }

        /// <summary>Whether the tile standing here has all three channels around it.</summary>
        public bool Bloomed(int index) => Gathered(index) == Energy.All;

        /// <summary>
        /// Whether this bed is open: it holds a bloomed tile, and — if it insists on a colour —
        /// one carrying it.
        ///
        /// <para>
        /// The colour clause can never fail in a played game, because <see cref="CanPlant"/>
        /// refuses the wrong tile outright rather than letting somebody kill a heartbed with a
        /// mis-tap. It is asked anyway, because this is the predicate the solver and the
        /// validator ask, and a rule that is only enforced at one end is a rule with one place
        /// left to get it wrong.
        /// </para>
        /// </summary>
        public bool IsOpen(int index)
        {
            if (!Layout.IsBed(index) || !Bloomed(index)) return false;

            int wants = Layout.Wants(index);
            return wants == Energy.None || (_cells[index] & wants) == wants;
        }

        /// <summary>Beds still waiting. Nought is the grove finished.</summary>
        public int BedsLeft
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _cells.Length; i++)
                    if (Layout.IsBed(i) && !IsOpen(i)) n++;
                return n;
            }
        }

        /// <summary>Every bed on this grove is open.</summary>
        public bool IsFinished => BedsLeft == 0;

        /// <summary>Tiles standing, which is what the grove is worth looking at.</summary>
        public int Planted
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _cells.Length; i++) if (_cells[i] != Energy.None) n++;
                return n;
            }
        }

        /// <summary>Edges where two unlike tiles meet. Counted once each, not twice.</summary>
        public int Seams
        {
            get
            {
                int n = 0;
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        int at = Layout.Index(x, y);
                        if (_cells[at] == Energy.None) continue;

                        if (x + 1 < Width && Unlike(at, at + 1)) n++;
                        if (y + 1 < Height && Unlike(at, at + Width)) n++;
                    }
                return n;
            }
        }

        bool Unlike(int a, int b)
            => _cells[a] != Energy.None && _cells[b] != Energy.None && _cells[a] != _cells[b];

        // ------------------------------------------------------------------ planting
        /// <summary>
        /// Whether this tile may be planted here, and it is three separate rules rather than one.
        ///
        /// <para>
        /// The cell has to be bare ground the grove can reach — <b>orthogonally beside something
        /// already standing</b>, which is what makes the result a <em>grove</em> rather than a
        /// spray of tiles, and what makes reaching a far bed cost tiles. And a heartbed refuses
        /// any colour but its own: refusing outright rather than letting a wrong tile land and
        /// kill the bed is deliberate, because an unrecoverable mistake made with one mis-tap is
        /// the worst thing a puzzle can hand somebody, and the bed wears its colour where anyone
        /// can see it.
        /// </para>
        /// <para>
        /// Split so that <see cref="Adrift"/> can be the same rules with the reach inverted
        /// rather than a second list of them.
        /// </para>
        /// </summary>
        public bool CanPlant(int colour, int index) => Accepts(colour, index) && Touching(index);

        /// <summary>
        /// Whether the <em>only</em> thing standing between this tile and this cell is that the
        /// cell is not beside anything yet.
        ///
        /// <para>
        /// The one refusal this board cannot answer for itself. Every other way a tap is turned
        /// down is written on the cell that turned it down — stone is drawn as a rock, an
        /// occupied cell already holds a tile, and a heartbed wears the colour it is holding out
        /// for and flares it. Bare ground away from the grove looks exactly like bare ground
        /// beside it, so a shake there is a button that did nothing, and the rule has to be said
        /// in words (see <c>KeeperView.Refuse</c>).
        /// </para>
        /// <para>
        /// Expressed as <see cref="Accepts"/> without <see cref="Touching"/> rather than as its
        /// own list of clauses, so it cannot come to disagree with <see cref="CanPlant"/> about
        /// what else would have refused — a second copy of those three rules is a second place
        /// for a heartbed's refusal to be reported as a reach.
        /// </para>
        /// </summary>
        public bool Adrift(int colour, int index) => Accepts(colour, index) && !Touching(index);

        /// <summary>Everything a planting is refused for except how far it is from the grove.</summary>
        bool Accepts(int colour, int index)
        {
            if (colour == Energy.None) return false;
            if (index < 0 || index >= _cells.Length) return false;
            if (!Layout.IsPlantable(index) || _cells[index] != Energy.None) return false;

            int wants = Layout.Wants(index);
            if (Layout.IsBed(index) && wants != Energy.None && (colour & wants) != wants)
                return false;

            return true;
        }

        /// <summary>Whether anything at all is standing next to this cell.</summary>
        bool Touching(int index)
        {
            Layout.Beside(index, _beside);
            for (int i = 0; i < _beside.Count; i++)
                if (_cells[_beside[i]] != Energy.None) return true;
            return false;
        }

        /// <summary>
        /// Whether the grove has anywhere left to grow at all, whatever colour comes next.
        ///
        /// <para>
        /// Deliberately blind to colour, and that is what makes it a <em>fail state</em> rather
        /// than a bad hand: a grove with room but the wrong tile in hand is composted past
        /// (<see cref="KeeperRun.Compost"/>), while a grove with nowhere left to plant can never
        /// be helped by any tile and is over. See <see cref="KeeperVerdict"/>.
        /// </para>
        /// </summary>
        public bool AnyRoom
        {
            get
            {
                for (int i = 0; i < _cells.Length; i++)
                {
                    if (!Layout.IsPlantable(i) || _cells[i] != Energy.None) continue;
                    if (Touching(i)) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Whether some bed on this grove can be proved unopenable, whatever is dealt next.
        ///
        /// <para>
        /// <b>It never ends a run and it is not allowed to.</b> Lightfall shipped exactly this
        /// proof as a loss condition, and it came back from play as a run that ended while the
        /// tray still had motes in it — which reads as the game deciding on the player's behalf
        /// and is indistinguishable from a bug unless you already know the rule being enforced.
        /// A player who wants to spend their last three tiles on a grove that cannot be finished
        /// is entitled to. What the proof is <em>for</em> is the one question where it is exactly
        /// right: whether it would be honest to sell somebody a continue. See
        /// <see cref="KeeperVerdict.Deficit"/>.
        /// </para>
        /// <para>
        /// Both clauses are certainties rather than heuristics, because the answer decides
        /// whether money changes hands. A bed holding a tile with no bare ground beside it can
        /// never gather another channel; a bed no chain of bare ground connects to anything
        /// standing can never be planted on at all. Anything less certain than that is left
        /// alone, so this under-reports and never over-reports.
        /// </para>
        /// </summary>
        public bool AnyBedLost()
        {
            bool anyEmptyBed = false;

            for (int i = 0; i < _cells.Length; i++)
            {
                if (!Layout.IsBed(i) || IsOpen(i)) continue;

                if (_cells[i] != Energy.None)
                {
                    if (!HasBareNeighbour(i)) return true;
                    continue;
                }

                anyEmptyBed = true;
            }

            return anyEmptyBed && SomeEmptyBedIsCutOff();
        }

        bool HasBareNeighbour(int index)
        {
            int width = Width;
            int x = index % width, y = index / width;

            if (y > 0 && Bare(index - width)) return true;
            if (x < width - 1 && Bare(index + 1)) return true;
            if (y < Height - 1 && Bare(index + width)) return true;
            if (x > 0 && Bare(index - 1)) return true;

            return false;
        }

        bool Bare(int index) => Layout.IsPlantable(index) && _cells[index] == Energy.None;

        /// <summary>
        /// Whether any empty bed is walled off from the grove: no run of bare ground joins it to
        /// anything standing, so no sequence of plantings could ever reach it.
        /// </summary>
        bool SomeEmptyBedIsCutOff()
        {
            var reached = new bool[_cells.Length];
            var queue = new List<int>(_cells.Length);

            for (int i = 0; i < _cells.Length; i++)
            {
                if (!Bare(i) || !Touching(i)) continue;
                reached[i] = true;
                queue.Add(i);
            }

            for (int head = 0; head < queue.Count; head++)
            {
                int at = queue[head];
                int width = Width;
                int x = at % width, y = at / width;

                if (y > 0) Spread(at - width, reached, queue);
                if (x < width - 1) Spread(at + 1, reached, queue);
                if (y < Height - 1) Spread(at + width, reached, queue);
                if (x > 0) Spread(at - 1, reached, queue);
            }

            for (int i = 0; i < _cells.Length; i++)
                if (Layout.IsBed(i) && _cells[i] == Energy.None && !reached[i]) return true;

            return false;
        }

        void Spread(int at, bool[] reached, List<int> queue)
        {
            if (reached[at] || !Bare(at)) return;

            reached[at] = true;
            queue.Add(at);
        }

        /// <summary>Every cell this colour may be planted on, for the screen to mark as ground.</summary>
        public void Openings(int colour, List<int> into)
        {
            into.Clear();
            for (int i = 0; i < _cells.Length; i++)
                if (CanPlant(colour, i)) into.Add(i);
        }

        /// <summary>
        /// What planting here would make, without planting it.
        ///
        /// <para>
        /// <b>The preview is the game.</b> Nothing on this board is hidden — the ground, the
        /// procession and the colour every tile is still waiting for are all drawn — so what is
        /// being asked of the player is a judgement rather than a guess, and a builder that
        /// punishes guessing is not one anybody plays twice. It is also what makes a permanent
        /// planting fair (see the class remarks).
        /// </para>
        /// </summary>
        public KeeperGain Preview(int colour, int index)
        {
            if (!CanPlant(colour, index)) return KeeperGain.Nothing;

            _cells[index] = colour;
            var gain = Reading(index);
            _cells[index] = Energy.None;

            return gain;
        }

        /// <summary>
        /// Plants the tile and reports what it made, filling <paramref name="bloomed"/> with the
        /// cells that burst — the planted one first, so a cascade can be played outward from it.
        /// </summary>
        public KeeperGain Plant(int colour, int index, List<int> bloomed)
        {
            if (!CanPlant(colour, index))
            {
                bloomed?.Clear();
                return KeeperGain.Nothing;
            }

            // Read before, plant, read after: a tile blooms because of this planting only if it
            // was not blooming before it. Nothing is removed from this board, so "was blooming"
            // can never come back false and the two readings differ by exactly what happened.
            _cells[index] = colour;
            var gain = Reading(index, bloomed);

            return gain;
        }

        /// <summary>
        /// What the tile just laid at <paramref name="index"/> completed: itself, and any of the
        /// four beside it that were one channel short until it arrived.
        /// </summary>
        KeeperGain Reading(int index, List<int> bloomed = null)
        {
            var found = bloomed ?? _scratch;
            found.Clear();

            int colour = _cells[index];
            int seams = 0;

            if (Bloomed(index)) found.Add(index);

            int width = Width;
            int x = index % width, y = index / width;

            if (y > 0) Beside(index - width, index, colour, found, ref seams);
            if (x < width - 1) Beside(index + 1, index, colour, found, ref seams);
            if (y < Height - 1) Beside(index + width, index, colour, found, ref seams);
            if (x > 0) Beside(index - 1, index, colour, found, ref seams);

            int beds = 0;
            for (int i = 0; i < found.Count; i++) if (IsOpen(found[i])) beds++;

            return new KeeperGain(found.Count, beds, seams);
        }

        /// <summary>
        /// One neighbour of a tile that has just been laid: whether it makes a seam with it, and
        /// whether the new tile is what finished it.
        ///
        /// <para>
        /// <b>The test is what it was before, with this tile taken back out.</b> Asking whether
        /// the neighbour has all three "except this colour" is the version that reads right and
        /// is wrong: a neighbour that already held red from somewhere else would have its red
        /// taken away too, and a tile that bloomed a turn ago would be reported as blooming
        /// again every time a red landed near it. The only correct reading is the board without
        /// the tile that has just arrived on it.
        /// </para>
        /// </summary>
        void Beside(int at, int from, int colour, List<int> found, ref int seams)
        {
            int mate = _cells[at];
            if (mate == Energy.None) return;

            if (mate != colour) seams++;

            if (Gathered(at) != Energy.All) return;             // not blooming now
            if (Gathered(at, from) == Energy.All) return;       // was blooming already

            found.Add(at);
        }
    }
}
