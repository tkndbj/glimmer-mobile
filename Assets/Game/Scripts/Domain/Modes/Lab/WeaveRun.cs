using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// A Lightweave puzzle being solved: which channels have been drawn, and what ground they
    /// have taken.
    ///
    /// <para>
    /// <b>Two channels may never share a cell.</b> That one rule is the whole puzzle — without
    /// it every pair is joined by walking straight there and the grove is a formality. It is
    /// enforced here rather than in the drawing, because a rule the view owns is a rule the next
    /// input method breaks.
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

        public WeaveRun(WeaveLayout layout)
        {
            Grove = layout;

            _owner = new int[layout.Count];
            for (int i = 0; i < _owner.Length; i++) _owner[i] = -1;

            _paths = new List<int>[layout.Pairs.Count];
            for (int i = 0; i < _paths.Length; i++) _paths[i] = new List<int>();

            // The endpoints are standing on the board from the first frame, so nothing may be
            // routed through somebody else's crystal or critter.
            for (int i = 0; i < layout.Pairs.Count; i++)
            {
                _owner[layout.Pairs[i].Heart] = i;
                _owner[layout.Pairs[i].Critter] = i;
            }
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

        /// <summary>Every critter awake. The only ending this puzzle has.</summary>
        public bool IsSolved => Joined >= _paths.Length;

        /// <summary>How much of the grove is spoken for, for the readout.</summary>
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
        /// Whether a cell can be drawn through by this pair: free ground, or one of its own two
        /// endpoints.
        /// </summary>
        public bool Free(int pair, int cell)
        {
            if (cell < 0 || cell >= _owner.Length) return false;

            int owner = _owner[cell];
            if (owner < 0) return true;

            // Its own endpoints are its to use; anything else standing there is in the way,
            // including a channel it drew earlier and has not taken back.
            return owner == pair && !IsJoined(pair);
        }

        /// <summary>
        /// Whether this path is a legal channel for this pair: it runs between the pair's own two
        /// endpoints, every step is orthogonal, it never doubles back, and it crosses nothing.
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

                // The middle of a channel may only run over free ground. The two ends are the
                // pair's own, which is why they are excused rather than the rule being softened.
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
            return true;
        }

        /// <summary>Takes a pair's channel back, leaving its endpoints where they stand.</summary>
        public void Erase(int pair)
        {
            if (pair < 0 || pair >= _paths.Length) return;

            foreach (int cell in _paths[pair]) _owner[cell] = -1;
            _paths[pair].Clear();

            var ends = Grove.Pairs[pair];
            _owner[ends.Heart] = pair;
            _owner[ends.Critter] = pair;
        }

        void Restore(int pair, List<int> path)
        {
            if (path.Count == 0) return;

            _paths[pair].AddRange(path);
            foreach (int cell in path) _owner[cell] = pair;
        }

        /// <summary>Takes every channel back. The board returns to its endpoints.</summary>
        public void Reset()
        {
            for (int i = 0; i < _paths.Length; i++) Erase(i);
        }

        /// <summary>
        /// Draws the arrangement the generator carved. Not offered to the player — it exists so
        /// the board's own claim to be solvable can be <em>checked</em> rather than trusted, and
        /// the tests do exactly that on every generated grove.
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
