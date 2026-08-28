using System;
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
    public sealed class WeaveBoard
    {
        public readonly WeaveLayout Grove;

        readonly int[] _owner;              // which pair holds each cell, -1 for free ground
        readonly List<int>[] _paths;
        readonly bool[] _threaded;          // one per bead: has its own channel come through

        /// <summary>
        /// Scratch for <see cref="Reach"/>, made once and reused.
        ///
        /// <c>WeaveSolver.Search</c>'s bargain on a much smaller search: the walk is asked again
        /// every time any channel moves, and three arrays per pair per landing is garbage a
        /// phone collects mid-drag for no reason. Not re-entrant, which costs nothing — a board
        /// is touched by one finger on one thread.
        /// </summary>
        readonly bool[] _seen;
        readonly int[] _queue, _step;

        public WeaveBoard(WeaveLayout layout)
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

            _seen = new bool[_owner.Length];
            _queue = new int[_owner.Length];
            _step = new int[_owner.Length];
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
        /// Whether a cell can be drawn through by this pair: free ground, or ground already its
        /// own — its two endpoints, its beads, and the channel it drew earlier.
        ///
        /// <para>
        /// <b>Its old channel counts as its own, and that is a change ink paid for.</b> This
        /// used to refuse a joined pair its own cells, which was harmless only because the view
        /// took the old channel up the instant a finger touched a crystal — so by the time
        /// anything asked, there was no old channel to collide with. Ink made that arrangement
        /// a trap: a redraw that the player thought better of half way through had already
        /// destroyed a channel they had paid for, and the light was gone with it. Nothing is
        /// taken up now until a replacement lands (<see cref="Draw"/> does it, and puts the old
        /// one back if the new one is refused), which means a pair genuinely is standing on its
        /// own ground while it is being redrawn — and being refused for colliding with yourself
        /// is not a rule anybody could be expected to read off the board.
        /// </para>
        /// </summary>
        public bool Free(int pair, int cell)
        {
            if (cell < 0 || cell >= _owner.Length) return false;

            int owner = _owner[cell];
            if (owner < 0) return true;

            // Its own ground is its to use; anything else standing there is in the way.
            return owner == pair;
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
            }

            return true;
        }

        /// <summary>
        /// Lays a channel down, replacing whatever this pair had before. Returns false and
        /// changes nothing if the path is refused.
        ///
        /// <para>
        /// <b>Nothing here knows what a channel costs</b>, and that is the whole reason this
        /// class is only a board. Affordability is not legality: the generator and the validator
        /// both draw the carved solution through here, on a grove that is not being played by
        /// anybody, and a rule about a player's purse has no business deciding whether a board
        /// can be proved solvable. <see cref="WeaveRun"/> is what charges for this and what
        /// remembers it happened.
        /// </para>
        /// </summary>
        /// <param name="replaced">
        /// The route this one displaced, empty when the pair had none. Handed back rather than
        /// merely dropped so a run can undo a <em>redraw</em> by putting the old route back,
        /// instead of leaving the pair bare — which would cost the player a channel they never
        /// asked to lose.
        /// </param>
        public bool Draw(int pair, IReadOnlyList<int> path, out int[] replaced)
        {
            replaced = Empty;
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

            replaced = previous.Count == 0 ? Empty : previous.ToArray();

            Rethread(pair);
            return true;
        }

        static readonly int[] Empty = new int[0];

        /// <summary>Lays a channel down when the caller has no use for what it replaced.</summary>
        public bool Draw(int pair, IReadOnlyList<int> path) => Draw(pair, path, out _);

        /// <summary>
        /// Puts a route back exactly as it stood, charging nothing and asking nothing.
        ///
        /// Only an undo calls it, and only ever with the route the stroke being undone
        /// displaced — which was legal a moment ago and, since nothing has been drawn on this
        /// board since, still is.
        /// </summary>
        public void PutBack(int pair, IReadOnlyList<int> path)
        {
            if (pair < 0 || pair >= _paths.Length) return;

            Erase(pair);
            if (path == null || path.Count == 0) return;

            Restore(pair, new List<int>(path));
        }

        /// <summary>
        /// Takes a pair's channel back, leaving its endpoints and beads where they stand.
        ///
        /// <b>It is not an undo.</b> The ground comes free and nothing else happens — no light is
        /// handed back, because taking a channel up is how a route is <em>changed</em> and a
        /// change that refunded would make the ink decorative. See <c>WeaveRun.TryUndo</c>.
        /// </summary>
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

        // ------------------------------------------------------------------ is it over
        /// <summary>Whether this pair is finished: joined, and every bead it owes threaded.</summary>
        public bool Settled(int pair)
        {
            if (pair < 0 || pair >= _paths.Length || !IsJoined(pair)) return false;

            var beads = Grove.Beads;
            for (int b = 0; b < beads.Count; b++)
                if (beads[b].Pair == pair && !_threaded[b]) return false;

            return true;
        }

        /// <summary>
        /// The fewest further cells of light any finish of this grove could take.
        ///
        /// <para>
        /// <b>A lower bound, and it has to be one.</b> This is half of what decides that a run
        /// is lost, so an over-estimate would end a run the player could still have won — the
        /// single worst thing this mode could do to somebody. So it counts, for every pair not
        /// yet <see cref="Settled"/>, that pair's own floor on an <em>empty</em> board
        /// (<c>WeaveLayout.Straight</c>, which already prices the beads it owes by Held-Karp).
        /// No arrangement can finish for less, whatever else is standing in the way, and no
        /// arrangement is ruled out — the player is always free to take another pair's channel
        /// up and redraw it, so a bound that assumed the ground stays as it is would be wrong
        /// in exactly the direction that costs a run.
        /// </para>
        /// <para>
        /// A pair that is joined but has a bead still waiting counts in full: it has to be drawn
        /// again to be threaded, and the light already spent on it is spent.
        /// </para>
        /// </summary>
        public int Floor
        {
            get
            {
                int total = 0;
                for (int p = 0; p < _paths.Length; p++)
                    if (!Settled(p)) total += Grove.Straight(p);

                return total;
            }
        }

        /// <summary>
        /// The fewest cells this pair could be joined in from where the board stands: a walk
        /// over free ground and its own, or -1 if there is no way through at all.
        ///
        /// <para>
        /// Beads are ignored, which keeps it a lower bound on what a real completion costs —
        /// the direction that errs towards letting a run carry on. Its own channel counts as
        /// passable because taking it up is something the player may do at any moment, and
        /// <see cref="Free"/> says the same.
        /// </para>
        /// </summary>
        public int Reach(int pair)
        {
            if (pair < 0 || pair >= _paths.Length) return -1;

            var ends = Grove.Pairs[pair];
            if (ends.Heart == ends.Critter) return 1;

            // Breadth-first, over scratch made once with the board. The answer changes every
            // time any channel moves, so there is nothing to cache — but there is also no reason
            // to hand a phone three arrays per pair per landing.
            var seen = _seen;
            var queue = _queue;
            var step = _step;
            System.Array.Clear(seen, 0, seen.Length);

            int head = 0, tail = 0;
            queue[tail++] = ends.Heart;
            seen[ends.Heart] = true;

            while (head < tail)
            {
                int at = queue[head++];
                if (at == ends.Critter) return step[at] + 1;

                int x = at % Grove.Width, y = at / Grove.Width;
                for (int d = 0; d < WeaveLayout.Steps.Length; d++)
                {
                    int nx = x + WeaveLayout.Steps[d].dx, ny = y + WeaveLayout.Steps[d].dy;
                    if (!Grove.Inside(nx, ny)) continue;

                    int next = Grove.Index(nx, ny);
                    if (seen[next]) continue;

                    // A hedge is a wall to this walk exactly as it is to a finger. Missed here
                    // and the bound stops being one: a pair walled away from its critter would
                    // read as affordable, WeaveVerdict would keep the run alive on a grove that
                    // cannot be finished, and the player would sit in front of a board that will
                    // not end — invariant 20g's state, arrived at by arithmetic.
                    if (!Grove.Open(at, next)) continue;

                    if (next != ends.Critter && !Free(pair, next)) continue;

                    seen[next] = true;
                    step[next] = step[at] + 1;
                    queue[tail++] = next;
                }
            }

            return -1;
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
