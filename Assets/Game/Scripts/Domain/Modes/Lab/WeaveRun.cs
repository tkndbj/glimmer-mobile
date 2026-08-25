using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// A Lightweave puzzle being solved: which channels have been drawn, what ground they have
    /// taken, and which beads they have been threaded through.
    ///
    /// <para>
    /// <b>Two channels may never share a cell.</b> That one rule is what makes the pairs contend
    /// for ground, and it is enforced here rather than in the drawing, because a rule the view
    /// owns is a rule the next input method breaks.
    /// </para>
    /// <para>
    /// <b>Where a channel goes is otherwise entirely the player's business.</b> This used to
    /// demand that the channels between them covered every cell of the grove, which made the
    /// direct route almost never the right one — the board sent the player the long way round
    /// for a reason nothing on screen could show, and the state where every critter was awake
    /// and the grove still unfinished read as the game failing to notice a win. That rule is
    /// gone. What replaced it is on the board: a bead is a cell one channel must be threaded
    /// through, drawn in that channel's own colour, and it asks for the same thinking while
    /// pointing at where.
    /// </para>
    /// <para>
    /// A drawn channel stays. There is no partial state and no channel in flight: the view
    /// gathers a path under the finger and offers it whole, and this either takes it or refuses
    /// it. Refused whole rather than trimmed, for the reason a hollow refuses a half-read board:
    /// a shortened channel is one the player did not draw, and on a screen where a finger is the
    /// only input that reads as the game fighting them.
    /// </para>
    /// </summary>
    public sealed class WeaveRun
    {
        public readonly WeaveLayout Grove;

        readonly int[] _owner;              // which pair holds each cell, -1 for free ground
        readonly List<int>[] _paths;
        readonly bool[] _threaded;          // one per bead: has its own channel come through

        public WeaveRun(WeaveLayout layout)
        {
            Grove = layout;

            _owner = new int[layout.Count];
            _paths = new List<int>[layout.Pairs.Count];
            for (int i = 0; i < _paths.Length; i++) _paths[i] = new List<int>();

            _threaded = new bool[layout.Beads.Count];

            // The endpoints and the beads are standing on the board from the first frame, so
            // nothing may be routed through somebody else's crystal, critter or bead. A bead
            // blocking five colours is half of what it is for — see WeaveBead.
            for (int i = 0; i < _owner.Length; i++) _owner[i] = layout.Reserved(i);
        }

        /// <summary>Which pair holds this cell, or -1 for free ground.</summary>
        public int OwnerOf(int cell) => _owner[cell];

        /// <summary>The channel drawn for a pair, empty until one is.</summary>
        public IReadOnlyList<int> PathOf(int pair) => _paths[pair];

        public bool IsJoined(int pair) => _paths[pair].Count >= 2;

        public int Joined
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _paths.Length; i++) if (IsJoined(i)) n++;
                return n;
            }
        }

        public int Pairs => _paths.Length;

        /// <summary>Whether this bead's own channel has been threaded through it.</summary>
        public bool IsThreaded(int bead)
            => bead >= 0 && bead < _threaded.Length && _threaded[bead];

        /// <summary>How many beads are still waiting for their colour.</summary>
        public int BeadsLeft
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _threaded.Length; i++) if (!_threaded[i]) n++;
                return n;
            }
        }

        /// <summary>
        /// Every critter awake and every bead threaded. The only ending this puzzle has.
        ///
        /// <para>
        /// <b>The second half is a rule a board can show, which the one it replaced was not.</b>
        /// This used to also require that no cell of the grove was left bare — a condition
        /// nothing on screen stated, that no board could demonstrate, and that produced a state
        /// looking exactly like a win the game had failed to notice. A bead says the same thing
        /// with a ring on the ground: it stands on a cell the player has to come through, in the
        /// colour that owes it, and it lights when it is satisfied. A grove with no beads on it is
        /// finished the moment the last critter wakes, which is what the first two rungs of the
        /// chapter are for.
        /// </para>
        /// </summary>
        public bool IsSolved => Joined >= _paths.Length && BeadsLeft == 0;

        /// <summary>How much of the grove is spoken for. The readout's, not the rule's.</summary>
        public int Occupied
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _owner.Length; i++) if (_owner[i] >= 0) n++;
                return n;
            }
        }

        /// <summary>
        /// Whether a cell can be drawn through by this pair: free ground, or ground reserved to
        /// it — its own two endpoints and its own beads.
        /// </summary>
        public bool Free(int pair, int cell)
        {
            if (cell < 0 || cell >= _owner.Length) return false;

            int owner = _owner[cell];
            if (owner < 0) return true;

            // Its own ground is its to use; anything else standing there is in the way,
            // including a channel it drew earlier and has not taken back.
            return owner == pair && !IsJoined(pair);
        }

        /// <summary>
        /// Whether this path is a legal channel for this pair: it runs between the pair's own two
        /// endpoints, every step is orthogonal, it never doubles back, and it crosses nothing.
        ///
        /// <para>
        /// <b>Threading is deliberately not part of legality.</b> A channel that misses one of its
        /// own beads is a perfectly legal channel — it is simply not a finished grove, and the
        /// player is told so by the bead still standing lit on the board. Refusing the drag
        /// instead would mean a finger that reached its critter being silently rejected for a
        /// reason a hundred cells away, which is the same fault the fill rule had.
        /// </para>
        /// </summary>
        public bool IsLegal(int pair, IReadOnlyList<int> path)
        {
            if (pair < 0 || pair >= _paths.Length) return false;
            if (path == null || path.Count < 2) return false;

            var ends = Grove.Pairs[pair];

            bool forward = path[0] == ends.Heart && path[path.Count - 1] == ends.Critter;
            bool backward = path[0] == ends.Critter && path[path.Count - 1] == ends.Heart;
            if (!forward && !backward) return false;

            for (int i = 0; i < path.Count; i++)
            {
                int cell = path[i];

                if (cell < 0 || cell >= _owner.Length) return false;
                for (int j = 0; j < i; j++) if (path[j] == cell) return false;

                if (i > 0 && !Grove.Adjacent(path[i - 1], cell)) return false;

                // The middle of a channel may only run over free ground or its own. The two ends
                // are the pair's own, which is why they are excused rather than the rule softened.
                bool isEnd = i == 0 || i == path.Count - 1;
                if (!isEnd && _owner[cell] >= 0 && _owner[cell] != pair) return false;
                if (!isEnd && _owner[cell] == pair && IsJoined(pair)) return false;
            }

            return true;
        }

        /// <summary>
        /// Lays a channel down, replacing whatever this pair had before. Returns false and
        /// changes nothing if the path is refused.
        /// </summary>
        public bool Draw(int pair, IReadOnlyList<int> path)
        {
            if (pair < 0 || pair >= _paths.Length) return false;

            // Taken up first, so a pair redrawing over its own ground is not refused for
            // colliding with itself. Kept aside so a refusal can put it back exactly.
            var previous = new List<int>(_paths[pair]);
            Erase(pair);

            if (!IsLegal(pair, path))
            {
                Restore(pair, previous);
                return false;
            }

            _paths[pair].Clear();
            _paths[pair].AddRange(path);
            foreach (int cell in path) _owner[cell] = pair;

            Rethread(pair);
            return true;
        }

        /// <summary>Takes a pair's channel back, leaving its endpoints and beads where they stand.</summary>
        public void Erase(int pair)
        {
            if (pair < 0 || pair >= _paths.Length) return;

            foreach (int cell in _paths[pair]) _owner[cell] = Grove.Reserved(cell);
            _paths[pair].Clear();

            Rethread(pair);
        }

        void Restore(int pair, List<int> path)
        {
            if (path.Count == 0) return;

            _paths[pair].AddRange(path);
            foreach (int cell in path) _owner[cell] = pair;

            Rethread(pair);
        }

        /// <summary>
        /// Re-reads which of this pair's beads its channel now runs through.
        ///
        /// Kept as stored state rather than derived on demand because <see cref="IsSolved"/> is
        /// asked on every landing and the readout on every repaint, and both would otherwise walk
        /// every path. Recomputed for the whole pair rather than adjusted, so there is no way for
        /// a redraw, a refusal and an erase to leave it disagreeing with the path it describes.
        /// </summary>
        void Rethread(int pair)
        {
            var beads = Grove.Beads;
            for (int b = 0; b < beads.Count; b++)
            {
                if (beads[b].Pair != pair) continue;
                _threaded[b] = _paths[pair].Contains(beads[b].Cell);
            }
        }

        /// <summary>Takes every channel back. The board returns to its endpoints and beads.</summary>
        public void Reset()
        {
            for (int i = 0; i < _paths.Length; i++) Erase(i);
        }

        /// <summary>
        /// Draws the arrangement the generator carved. Not offered to the player — it exists so
        /// the board's own claim to be solvable can be <em>checked</em> rather than trusted, and
        /// the tests and the validator do exactly that on every generated grove.
        ///
        /// It is checked all the way to <see cref="IsSolved"/>, so it proves the beads as well:
        /// a bead the carved route does not thread is a board whose own proof does not finish it.
        /// </summary>
        public bool DrawSolution()
        {
            Reset();

            for (int i = 0; i < _paths.Length; i++)
                if (!Draw(i, Grove.Solution(i))) return false;

            return IsSolved;
        }
    }
}
