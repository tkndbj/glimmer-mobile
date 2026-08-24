using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// <b>Grovekeeper.</b> You are handed tiles of coloured light and you lay them out to grow a
    /// grove. Every edge-matching game ever made rewards putting like against like; this one
    /// rewards the opposite.
    ///
    /// <para>
    /// <b>A seam between two different colours blooms; a seam between two of the same is worth
    /// nothing.</b> That single inversion is the game. Red against red is a wasted edge. Red
    /// against green is a seam of amber. And a tile whose neighbours between them supply all
    /// three channels opens a <em>bloom</em> — the big score, and the thing you plan three tiles
    /// ahead for.
    /// </para>
    /// <para>
    /// There is no fail state and no clock. You get a fixed number of tiles and the only question
    /// is how much grove you made with them, which is the shape a cozy builder wants: the
    /// pressure is entirely "could I have placed that better", never "I am about to die".
    /// </para>
    /// </summary>
    public sealed class KeeperBoard
    {
        public readonly int Width, Height;

        readonly int[] _cells;          // Energy mask, 0 = empty ground
        readonly bool[] _bloomed;
        readonly List<int> _queue = new List<int>();
        uint _seed;

        public KeeperBoard(int width, int height, int tiles, uint seed)
        {
            Width = width;
            Height = height;
            Tiles = tiles;
            _cells = new int[width * height];
            _bloomed = new bool[width * height];
            _seed = seed == 0 ? 22695477u : seed;

            for (int i = 0; i < Lookahead; i++) _queue.Add(RollColour());

            // The first tile is placed for you, in the middle. A builder that opens on empty
            // ground has to explain "tap anywhere" before it can explain anything else, and the
            // rule that matters — place it *next to* something — cannot be shown until there is
            // something to be next to.
            _cells[Index(width / 2, height / 2)] = RollColour();
            Placed = 1;
        }

        public const int Lookahead = 3;

        /// <summary>What a seam between two different colours is worth.</summary>
        public const int SeamScore = 10;

        /// <summary>What it is worth to gather all three channels around one tile.</summary>
        public const int BloomScore = 60;

        /// <summary>How many tiles the run hands out.</summary>
        public readonly int Tiles;

        public int Index(int x, int y) => y * Width + x;
        public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public int At(int index) => _cells[index];
        public int At(int x, int y) => _cells[Index(x, y)];
        public bool IsBloomed(int index) => _bloomed[index];

        public int Next => _queue.Count > 0 ? _queue[0] : Energy.None;
        public int Ahead(int n) => n >= 0 && n < _queue.Count ? _queue[n] : Energy.None;

        public int Score { get; private set; }
        public int Placed { get; private set; }
        public int Blooms { get; private set; }
        public int Seams { get; private set; }

        public int Left => Tiles - Placed + 1;
        public bool IsDone => Left <= 0;

        /// <summary>
        /// A tile may go on empty ground that touches something already placed. Growing outward
        /// from one seed rather than scattering is what makes the result a <em>grove</em> and not
        /// a spray of tiles.
        /// </summary>
        public bool CanPlace(int index)
        {
            if (IsDone || index < 0 || index >= _cells.Length) return false;
            if (_cells[index] != Energy.None) return false;

            int x = index % Width, y = index / Width;
            for (int n = 0; n < Neighbours.Length; n++)
            {
                int nx = x + Neighbours[n].dx, ny = y + Neighbours[n].dy;
                if (Inside(nx, ny) && _cells[Index(nx, ny)] != Energy.None) return true;
            }
            return false;
        }

        /// <summary>
        /// What placing the next tile here would be worth, without placing it. The screen shows
        /// this under the player's thumb — a builder with no preview is a builder played by
        /// guesswork, and the whole pleasure is seeing a good spot before you commit.
        /// </summary>
        public KeeperGain Preview(int index)
        {
            if (!CanPlace(index)) return default;

            int colour = Next;
            int gathered = colour;
            int seams = 0;

            int x = index % Width, y = index / Width;
            for (int n = 0; n < Neighbours.Length; n++)
            {
                int nx = x + Neighbours[n].dx, ny = y + Neighbours[n].dy;
                if (!Inside(nx, ny)) continue;

                int mate = _cells[Index(nx, ny)];
                if (mate == Energy.None) continue;

                gathered |= mate;
                if (mate != colour) seams++;
            }

            bool bloom = gathered == Energy.All;
            return new KeeperGain(seams, bloom, seams * SeamScore + (bloom ? BloomScore : 0));
        }

        /// <summary>Lays the next tile down and reports what it made.</summary>
        public KeeperGain Place(int index)
        {
            var gain = Preview(index);
            if (gain.Score <= 0 && !CanPlace(index)) return default;

            _cells[index] = Next;
            _bloomed[index] = gain.Bloom;

            _queue.RemoveAt(0);
            _queue.Add(RollColour());

            Placed++;
            Score += gain.Score;
            Seams += gain.Seams;
            if (gain.Bloom) Blooms++;

            return gain;
        }

        /// <summary>Every cell a tile could still go on, for the screen to mark as ground.</summary>
        public List<int> Openings()
        {
            var open = new List<int>();
            for (int i = 0; i < _cells.Length; i++) if (CanPlace(i)) open.Add(i);
            return open;
        }

        static readonly (int dx, int dy)[] Neighbours = { (0, -1), (1, 0), (0, 1), (-1, 0) };

        /// <summary>Pure colours only. A dealt blend would hand over a seam already made.</summary>
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

    /// <summary>What one placement made: its seams, whether it bloomed, and what it scored.</summary>
    public readonly struct KeeperGain
    {
        public readonly int Seams;
        public readonly bool Bloom;
        public readonly int Score;

        public KeeperGain(int seams, bool bloom, int score)
        {
            Seams = seams;
            Bloom = bloom;
            Score = score;
        }
    }
}
