using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// Emberforge's board: a wall of stolen shards packed into a raider smelter, with critters
    /// caged in among them, and what happens when three alike are forced together.
    ///
    /// <para>
    /// <b>The verb is the genre's own and the twist is what a match makes.</b> Swap two
    /// neighbours so three alike line up and they do not clear — they <em>fuse</em>, into one
    /// <b>ember</b> standing on the cell the finger ended on. Tap that ember and it throws a
    /// <b>cross</b> of light down its whole row and column; push two of them together instead
    /// and they combine into a <b>star</b> that takes the diagonals with it. That is the ladder
    /// a hundred million people already know from Royal Match's rocket and its combinations,
    /// and the reason to build on it rather than on a bespoke verb is invariant 33's: a mode may
    /// be built on a loop everybody knows so long as the twist is real.
    /// </para>
    /// <para>
    /// <b>The twist is that nothing ever refills.</b> Every ember is three shards that are not
    /// coming back, and the beams destroy the shards they cross as readily as they free the
    /// critters — so a wall is worth about three explosions and <em>where</em> they are spent is
    /// the whole game. That is the decision the mode is made of (invariant 26h): the player is
    /// not choosing which match to take, they are choosing what to spend the wall on, and being
    /// wrong costs a rescue rather than a turn.
    /// </para>
    /// <para>
    /// <b>The monotone quantity is the wall itself</b>, which is invariant 20j's second test
    /// passed as directly as it can be: a fuse consumes at least three shards and puts back one
    /// ember, a tap spends that ember, a combine spends two, and a beam only ever removes.
    /// Nothing in this mode adds anything to the board ever — gravity slides shards down but
    /// nothing falls in from above — so the state graph is a DAG, a run always ends, and
    /// <see cref="ProtoSearch"/> finds par by breadth-first walk with nothing to prove about
    /// termination.
    /// </para>
    /// <para>
    /// <b>And the cascade clears the same threshold on every beat</b> (20j's third test). A beam
    /// sets off any <em>ember</em> it crosses and passes straight through a plain shard, so a
    /// chain runs exactly as far as the embers the player built and dies the instant it reaches
    /// wall that is not already primed. A rule that spread unconditionally would be a solvent;
    /// this one spreads through the player's own work, which is invariant 20m — the payoff is a
    /// thing they made.
    /// </para>
    /// <para>
    /// It is the eleventh mode the prototype level shape has carried and it cost the save file
    /// no schema version, no merge rule, no <c>firestore.rules</c> change and no server work
    /// (invariant 20a). It authors no deal at all: the wall is everything the level hands over,
    /// which is what makes the whole thing searchable.
    /// </para>
    /// </summary>
    public sealed class EmberLayout
    {
        // ------------------------------------------------------------------ the vocabulary
        /// <summary>
        /// A breach: wall that has already been blown out. Shards slide down through it and
        /// nothing ever fills it from above.
        /// </summary>
        public const char Breach = '.';

        /// <summary>
        /// Rivetted stone. Permanent, unswappable, it holds the wall above it up, and it
        /// <b>stops a beam</b>.
        ///
        /// <para>
        /// The one piece of terrain that is purely geometry: it never changes and it decides
        /// which rows and columns are worth firing down. A wall with no stone is a wall where
        /// every cross is worth the same and the aim decides nothing (invariant 5d).
        /// </para>
        /// </summary>
        public const char Stone = '#';

        /// <summary>
        /// Frost. It cannot be swapped, it holds the wall up, and it stops nothing — a beam
        /// melts straight through it and leaves a breach.
        ///
        /// <para>
        /// <b>What it does that stone cannot</b> (invariant 26g's test, asked before it was
        /// drawn): it goes away, and when it does the wall above it falls. Stone shapes the
        /// board for ever; frost shapes it until the player spends a beam there, which turns
        /// "where do I fire" into a question with a second answer — melt this and the whole
        /// column collapses into something that matches.
        /// </para>
        /// </summary>
        public const char Frost = '*';

        /// <summary>
        /// A caged critter. A goal, freed by any beam that crosses it, and the beam carries on.
        ///
        /// <para>
        /// It does not stop light, deliberately: one cross freeing three at once is the picture
        /// this mode exists to produce, and a cage that swallowed the beam would make every
        /// rescue cost its own explosion.
        /// </para>
        /// </summary>
        public const char Cage = 'C';

        /// <summary>
        /// A warden: a raider bolted into the wall under plating. A goal, and it <b>stops a
        /// beam</b> — one hit cracks the plating and the beam dies there, so it takes two.
        /// </summary>
        public const char Warden = 'W';

        /// <summary>
        /// A warden with its plating gone. <b>Not authorable</b>, and that is the point: it is a
        /// state a board reaches and never one it is written in, exactly as a blend is in
        /// Budburst. It still has to be in the key, or two boards a plate apart would merge in
        /// the search and par would come out short — which hands out stars nobody earned.
        /// </summary>
        public const char Scarred = 'V';

        /// <summary>The four shard colours.</summary>
        public const string Shards = "rgby";

        /// <summary>
        /// An ember: three shards fused, and the only thing on this wall a beam sets off rather
        /// than destroys.
        ///
        /// <para>
        /// <b>It has no colour, and that is a rule rather than an omission.</b> A coloured ember
        /// would have to do something with its colour or it would be decoration, which invariant
        /// 5d is about — and everything it could do (match with its own kind, tint its beam) was
        /// either a second matching alphabet that clogs the wall or a difference nothing rejects.
        /// One character instead: the shards carry the colour, the ember carries the payoff.
        /// </para>
        /// </summary>
        public const char Ember = 'O';

        /// <summary>Every character a level of this mode may be written in. <see cref="Scarred"/> is not one.</summary>
        public const string Letters = ".#*CWO" + Shards;

        /// <summary>Shards alike in a line that fuse. Three, as the genre has always said.</summary>
        public const int FuseAt = 3;

        /// <summary>Plates a warden wears, which is beams that have to reach it.</summary>
        public const int Plates = 2;

        /// <summary>
        /// The eight directions a blast travels, right/left/down/up first.
        ///
        /// A cross takes the first four and a star takes all eight, which is why the order
        /// matters and why it is written once.
        /// </summary>
        public static readonly int[] StepX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        public static readonly int[] StepY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>Rays a cross throws: its row and its column.</summary>
        public const int CrossRays = 4;

        /// <summary>Rays a star throws: the row, the column and both diagonals.</summary>
        public const int StarRays = 8;

        // ------------------------------------------------------------------ reading a letter
        /// <summary>Whether this is a plain shard.</summary>
        public static bool IsShard(char c) => Shards.IndexOf(c) >= 0;

        /// <summary>Whether this is a primed ember.</summary>
        public static bool IsEmber(char c) => c == Ember;

        /// <summary>Anything a finger may take hold of, and anything gravity moves.</summary>
        public static bool IsGem(char c) => IsShard(c) || c == Ember;

        /// <summary>A cage or a warden: something the level is asking for.</summary>
        public static bool IsGoal(char c) => c == Cage || c == Warden || c == Scarred;

        /// <summary>Whether a beam dies here. Stone and armour, and nothing else.</summary>
        public static bool Stops(char c) => c == Stone || c == Warden || c == Scarred;

        // ------------------------------------------------------------------ the wall
        public readonly ProtoGrid Grid;
        public readonly int Spare;

        /// <summary>
        /// Every pair of neighbouring cells, as flat index pairs.
        ///
        /// Worked out once and kept, because the search asks for the move list at every position
        /// it expands and the adjacency of a rectangle is not news. Horizontal pairs first, then
        /// vertical, both in reading order, so the move indices a board hands out are stable and
        /// a fixture can pin them.
        /// </summary>
        public readonly int[] Pairs;

        /// <summary>What is wrong with this wall, or null. Read before anything else is asked.</summary>
        public readonly string Fault;

        public int Width => Grid.Width;
        public int Height => Grid.Height;

        public EmberLayout(ProtoGrid grid, int spare)
        {
            Grid = grid;
            Spare = spare > 0 ? spare : 0;

            int w = grid.Width, h = grid.Height;
            var pairs = new List<int>(w * h * 4);

            for (int y = 0; y < h; y++)
                for (int x = 0; x + 1 < w; x++)
                {
                    pairs.Add(grid.Index(x, y));
                    pairs.Add(grid.Index(x + 1, y));
                }

            for (int y = 0; y + 1 < h; y++)
                for (int x = 0; x < w; x++)
                {
                    pairs.Add(grid.Index(x, y));
                    pairs.Add(grid.Index(x, y + 1));
                }

            Pairs = pairs.ToArray();
            Fault = Wrong(grid);
        }

        /// <summary>
        /// Everything about an authored wall that can be read off the file itself.
        ///
        /// The refusals that need the board <em>played</em> — that it is settled, that it can be
        /// finished, that a careless player does not run it dry — belong to the mode's reader and
        /// its validator. What is here is the set of faults that make the wall unopenable rather
        /// than unwinnable, because a player's build runs this and must never open a board that
        /// cannot be drawn.
        /// </summary>
        static string Wrong(ProtoGrid grid)
        {
            int shards = 0, embers = 0, goals = 0;

            for (int i = 0; i < grid.Count; i++)
            {
                char c = grid.At(i);
                if (IsShard(c)) shards++;
                else if (IsEmber(c)) embers++;
                else if (IsGoal(c)) goals++;
            }

            if (goals == 0)
                return "this wall holds no cage and no warden, so there is nothing to do and " +
                       "the level opens finished";

            if (shards < FuseAt && embers == 0)
                return $"this wall holds {shards} shard(s) and no ember, and it takes {FuseAt} " +
                       "alike in a line to fuse one - nothing here could ever be set off";

            return null;
        }
    }

    /// <summary>What a finger did. Three things, and the board decides which from the two cells.</summary>
    public enum EmberAim
    {
        /// <summary>A swap that lines three alike shards up. They fuse into one ember.</summary>
        Fuse = 0,

        /// <summary>A tap on an ember. It throws a cross down its row and its column.</summary>
        Fire = 1,

        /// <summary>Two embers pushed together. They combine into a star.</summary>
        Merge = 2,
    }

    /// <summary>What one blast is: a cross of light, or a star that takes the diagonals too.</summary>
    public enum EmberBlow
    {
        /// <summary>One ember. Row and column, from where it stood.</summary>
        Cross = 0,

        /// <summary>Two embers combined. Row, column and both diagonals.</summary>
        Star = 1,
    }

    /// <summary>
    /// Something that happened during one move, stamped with the beat it happened on.
    ///
    /// <para>
    /// <b>A log rather than a diff</b>, for invariant 30i's reason: a view that worked out what
    /// changed by comparing two boards would be doing arithmetic no par, no <c>ways</c>, no
    /// validator and no content gate can ever see going wrong, because all of those read the
    /// model and the model is right the whole time. Every deed names the cell it happened on and
    /// the beat it happened in, and the board hands back a whole snapshot per beat beside it.
    /// </para>
    /// </summary>
    public enum EmberDeed
    {
        /// <summary>Two neighbours changed places. <c>At</c> is where the finger left, <c>To</c> where it landed.</summary>
        Swap = 0,

        /// <summary>An ember tapped. <c>At</c> is its cell.</summary>
        Tap = 1,

        /// <summary>A shard or ember taken into a fuse. <c>At</c> is where it stood, <c>To</c> the anchor.</summary>
        Fuse = 2,

        /// <summary>An ember made. <c>At</c> is where it stands, <c>To</c> is how many shards went into it.</summary>
        Forge = 3,

        /// <summary>A blast went off. <c>At</c> is the cell, <c>To</c> is the <see cref="EmberBlow"/>.</summary>
        Blast = 4,

        /// <summary>One ray. <c>At</c> is where it started, <c>To</c> the last cell it reached, <c>What</c> the direction.</summary>
        Ray = 5,

        /// <summary>A shard destroyed by a beam.</summary>
        Break = 6,

        /// <summary>Frost melted by a beam.</summary>
        Melt = 7,

        /// <summary>A cage broken open. A goal met.</summary>
        Free = 8,

        /// <summary>A warden's plating cracked, and the beam died there.</summary>
        Crack = 9,

        /// <summary>A warden destroyed. A goal met.</summary>
        Wreck = 10,

        /// <summary>An ember set off by a beam rather than by a finger. This is the chain.</summary>
        Ignite = 11,

        /// <summary>A gem slid down its column. <c>At</c> is where it was, <c>To</c> where it came to rest.</summary>
        Drop = 12,
    }

    /// <summary>One thing that happened, and where and when.</summary>
    public readonly struct EmberDeedRecord
    {
        public readonly EmberDeed Deed;

        /// <summary>The cell it happened on.</summary>
        public readonly int At;

        /// <summary>A second cell, or a small number. What it means is the deed's business.</summary>
        public readonly int To;

        /// <summary>What was standing there, for a view that needs to draw the thing that went.</summary>
        public readonly char What;

        /// <summary>Which beat of the cascade. One-based; the touch itself is nought.</summary>
        public readonly int Beat;

        public EmberDeedRecord(EmberDeed deed, int at, int to, char what, int beat)
        {
            Deed = deed;
            At = at;
            To = to;
            What = what;
            Beat = beat;
        }
    }

    /// <summary>
    /// The whole wall as it stood at the end of one beat.
    ///
    /// Handed over rather than reconstructed (invariant 30i): the view moves between two states
    /// it was given, and never does arithmetic about what a beat must have left behind.
    /// </summary>
    public readonly struct EmberFrame
    {
        public readonly char[] Cells;

        public EmberFrame(char[] cells) => Cells = cells;
    }

    /// <summary>Everything one move did, in the order it did it.</summary>
    public sealed class EmberBlast
    {
        public readonly List<EmberDeedRecord> Deeds = new List<EmberDeedRecord>(64);

        /// <summary>One per beat: the touch, then each beat of the cascade.</summary>
        public readonly List<EmberFrame> Frames = new List<EmberFrame>(8);

        /// <summary>Beats after the touch. One is an ordinary move; four is a chain.</summary>
        public int Beats { get; internal set; }

        /// <summary>Pieces this move took off the wall altogether.</summary>
        public int Took { get; internal set; }

        /// <summary>Critters freed.</summary>
        public int Freed { get; internal set; }

        /// <summary>Wardens destroyed.</summary>
        public int Wrecked { get; internal set; }

        /// <summary>Embers made. What the player is doing on most moves.</summary>
        public int Forged { get; internal set; }

        /// <summary>Crosses that went off.</summary>
        public int Crosses { get; internal set; }

        /// <summary>Stars that went off.</summary>
        public int Stars { get; internal set; }

        /// <summary>Embers set off by a beam rather than by a finger. The chain, counted.</summary>
        public int Ignited { get; internal set; }

        public int Goals => Freed + Wrecked;
    }

    /// <summary>
    /// One touch: the cell the finger started on and the one it ended on.
    ///
    /// <para>
    /// <b>The two being equal is a tap</b>, which is how one struct carries all three things a
    /// finger can do without a second field the search would have to key on.
    /// </para>
    /// <para>
    /// <b>Which end of a swap the finger ends on decides where what it makes stands</b>
    /// (invariant 20m's "what you did is what you get"). For a <em>merge</em> that really is two
    /// different walls from one pair, because both cells are spent and the star goes off on the
    /// one the finger ended on. For a <em>fuse</em> it cannot be: the two swapped cells hold
    /// different characters and a fused group is uniform, so only one of them is ever in it and
    /// the ember lands there whichever way the drag went — see <see cref="EmberBoard.Moves"/>.
    /// </para>
    /// </summary>
    public readonly struct EmberMove
    {
        /// <summary>Where the finger started.</summary>
        public readonly int From;

        /// <summary>Where it ended. Whatever this move makes, it makes here.</summary>
        public readonly int To;

        public EmberMove(int from, int to)
        {
            From = from;
            To = to;
        }

        /// <summary>A tap on one cell.</summary>
        public static EmberMove Tap(int cell) => new EmberMove(cell, cell);

        /// <summary>
        /// No move at all, which is what a cascade is played with.
        ///
        /// The swap's own cells decide where the ember <em>it</em> makes stands; everything a
        /// cascade makes afterwards settles on its own middle, because nothing about it was the
        /// player's aim. Carrying the swap into later beats is what made a fuse able to tell its
        /// two directions apart four beats after the finger had left the screen.
        /// </summary>
        public static readonly EmberMove None = new EmberMove(-1, -1);

        public bool IsTap => From == To;
    }

    /// <summary>
    /// A wall being taken apart, and the one method that does it.
    ///
    /// <para>
    /// <b>Every claim is read off the wall as it stands and only then applied</b>, which is
    /// <c>FallBoard.Resolve</c>'s discipline and the thing a second runtime diverges on
    /// silently. Within a beat, every fuse is found before any cell is emptied, and every ray of
    /// every blast is walked before any of them removes anything — so two beams crossing one
    /// warden both see plating, and a chain never depends on which blast the loop reached first.
    /// </para>
    /// </summary>
    public sealed class EmberBoard : IProtoBoard
    {
        readonly EmberLayout _layout;
        readonly char[] _cells;
        readonly int _goals;

        int _freed, _wrecked;

        EmberBoard(EmberLayout layout, char[] cells, int goals, int freed, int wrecked)
        {
            _layout = layout;
            _cells = cells;
            _goals = goals;
            _freed = freed;
            _wrecked = wrecked;
        }

        public static EmberBoard Build(EmberLayout layout)
        {
            var cells = layout.Grid.Copy();

            int goals = 0;
            for (int i = 0; i < cells.Length; i++)
                if (EmberLayout.IsGoal(cells[i])) goals++;

            return new EmberBoard(layout, cells, goals, 0, 0);
        }

        /// <summary>A copy nobody else is holding, for a preview or for the search.</summary>
        public EmberBoard Fork()
            => new EmberBoard(_layout, (char[])_cells.Clone(), _goals, _freed, _wrecked);

        public EmberLayout Layout => _layout;

        public IReadOnlyList<char> Cells => _cells;

        public char At(int cell) => cell < 0 || cell >= _cells.Length ? '\0' : _cells[cell];

        public int Width => _layout.Width;
        public int Height => _layout.Height;

        public int Goals => _goals;
        public int GoalsLeft => _goals - _freed - _wrecked;
        public bool IsFinished => GoalsLeft == 0;

        // ------------------------------------------------------------------ what may be played
        /// <summary>
        /// Which of the three things a touch is, or null when it is not a move at all.
        ///
        /// <para>
        /// <b>One predicate rather than three</b>, because every caller — the search, the view,
        /// the validator — has to agree about what a finger just did, and three of them asking
        /// three questions is three chances to disagree about whether a move happened at all.
        /// </para>
        /// <para>
        /// Two clauses do the work. <b>Two embers may always be pushed together</b>, which is
        /// the genre's own special-plus-special and the only swap on this wall that needs no
        /// match. <b>Anything else has to make one</b> — three alike shards in a line through
        /// one of the two cells — which is the rule every player of this genre already knows.
        /// Note that an ember may be swapped with a shard: it is how a shard is slid into a line
        /// it could not otherwise reach, so what is asked is whether the <em>swap</em> aligns
        /// anything and not whether both halves of it are shards.
        /// </para>
        /// </summary>
        public bool Aim(int a, int b, out EmberAim aim)
        {
            aim = EmberAim.Fuse;

            if (a == b)
            {
                if (a < 0 || a >= _cells.Length || !EmberLayout.IsEmber(_cells[a])) return false;
                aim = EmberAim.Fire;
                return true;
            }

            if (!Neighbours(a, b)) return false;

            char x = _cells[a], y = _cells[b];
            if (!EmberLayout.IsGem(x) || !EmberLayout.IsGem(y)) return false;

            if (EmberLayout.IsEmber(x) && EmberLayout.IsEmber(y))
            {
                aim = EmberAim.Merge;
                return true;
            }

            _cells[a] = y;
            _cells[b] = x;

            bool made = Aligned(a) || Aligned(b);

            _cells[a] = x;
            _cells[b] = y;

            return made;
        }

        /// <summary>
        /// Whether two cells touch across an edge.
        ///
        /// Asked rather than assumed, because a move arrives from a finger as well as from the
        /// search: a drag that crossed the board would otherwise be honoured as a swap between
        /// two cells nowhere near each other.
        /// </summary>
        public bool Neighbours(int a, int b)
        {
            if (a < 0 || b < 0 || a >= _cells.Length || b >= _cells.Length) return false;

            int w = _layout.Width;
            int ax = a % w, ay = a / w, bx = b % w, by = b / w;

            int dx = ax > bx ? ax - bx : bx - ax;
            int dy = ay > by ? ay - by : by - ay;

            return dx + dy == 1;
        }

        /// <summary>Whether this cell sits in a run of <see cref="EmberLayout.FuseAt"/> alike shards.</summary>
        bool Aligned(int cell)
        {
            char c = _cells[cell];
            if (!EmberLayout.IsShard(c)) return false;

            int w = _layout.Width;
            int x = cell % w, y = cell / w;

            int across = 1;
            for (int i = x - 1; i >= 0 && _cells[y * w + i] == c; i--) across++;
            for (int i = x + 1; i < w && _cells[y * w + i] == c; i++) across++;
            if (across >= EmberLayout.FuseAt) return true;

            int down = 1;
            for (int j = y - 1; j >= 0 && _cells[j * w + x] == c; j--) down++;
            for (int j = y + 1; j < _layout.Height && _cells[j * w + x] == c; j++) down++;

            return down >= EmberLayout.FuseAt;
        }

        /// <summary>
        /// Every move available, in a stable order: the taps first, then every legal pair — and
        /// <b>both ways round only for a merge</b>.
        ///
        /// <para>
        /// <b>A fuse cannot tell the two directions apart, and that is provable rather than
        /// observed.</b> A swap exchanges two <em>different</em> characters (two of the same are
        /// refused as a no-op), and every run in a group is a run of one character — so a group
        /// is uniform, and the two swapped cells, holding different characters, can never be in
        /// the same one. Whichever end completed the match is therefore the end
        /// <see cref="Anchor"/> settles on, whichever way the finger went. Emitting both would
        /// double the work at every position and, worse, double-count <c>ProtoAnswer.Ways</c> at
        /// every depth, because the search adds the paths of two move indices that reach one
        /// state.
        /// </para>
        /// <para>
        /// A <em>merge</em> is the exception and the reason the distinction exists at all: both
        /// cells hold an ember, both are spent, and the star goes off on the one the finger ended
        /// on. That really is two different boards from one pair, and it is the one place in this
        /// mode where where you let go decides something.
        /// </para>
        /// </summary>
        public void Moves(List<EmberMove> into)
        {
            into.Clear();

            for (int i = 0; i < _cells.Length; i++)
                if (EmberLayout.IsEmber(_cells[i])) into.Add(EmberMove.Tap(i));

            var pairs = _layout.Pairs;
            for (int i = 0; i < pairs.Length; i += 2)
            {
                int a = pairs[i], b = pairs[i + 1];
                if (!Aim(a, b, out var aim)) continue;

                into.Add(new EmberMove(a, b));
                if (aim == EmberAim.Merge) into.Add(new EmberMove(b, a));
            }
        }

        public bool AnyMove
        {
            get
            {
                for (int i = 0; i < _cells.Length; i++)
                    if (EmberLayout.IsEmber(_cells[i])) return true;

                var pairs = _layout.Pairs;
                for (int i = 0; i < pairs.Length; i += 2)
                    if (Aim(pairs[i], pairs[i + 1], out _)) return true;

                return false;
            }
        }

        /// <summary>
        /// Whether this wall can be <em>proved</em> never to finish, however many moves were
        /// bought.
        ///
        /// <para>
        /// A certainty and never a guess (invariant 28f), so it under-reports. Every goal here is
        /// met by a beam and every beam costs an ember, and an ember costs three alike shards —
        /// so a wall that could not raise one even if every shard of every colour fell into line
        /// can never light anything again, whatever is bought. Geometry is deliberately ignored:
        /// it can only make fewer things possible, which is the safe direction.
        /// </para>
        /// </summary>
        public bool Stranded
        {
            get
            {
                int could = 0;
                var perHue = new int[EmberLayout.Shards.Length];

                for (int i = 0; i < _cells.Length; i++)
                {
                    char c = _cells[i];
                    if (EmberLayout.IsEmber(c)) { could++; continue; }

                    int at = EmberLayout.Shards.IndexOf(c);
                    if (at >= 0) perHue[at]++;
                }

                for (int i = 0; i < perHue.Length; i++) could += perHue[i] / EmberLayout.FuseAt;

                return could < 1;
            }
        }

        /// <summary>
        /// Whether the wall would act before anybody touched it.
        ///
        /// A board is authored settled, for Budburst's reason and rather more sharply here: the
        /// cascade would run on from it, so the wall the player meets would not be the wall that
        /// was authored, proved or graded.
        /// </summary>
        public bool Stirred
        {
            get
            {
                for (int i = 0; i < _cells.Length; i++) if (Aligned(i)) return true;
                return false;
            }
        }

        // ------------------------------------------------------------------ playing a move
        /// <summary>
        /// Plays one touch and resolves everything that follows from it, to a standstill.
        ///
        /// <para>
        /// <b>One beat is a fuse pass or a beat of a chain, never both, and the wall settles only
        /// once the chain has finished.</b> A beam that set off an ember names the cell it stood
        /// on, so gravity in between would fire it from wherever the collapse had put something
        /// else — which is the class of fault that leaves the model right and the board wrong.
        /// </para>
        /// <para>
        /// Bounded by the wall: every beat that acts takes at least one piece off a board that
        /// never gains any, and a beat that does neither ends the move.
        /// </para>
        /// </summary>
        /// <returns>What happened, or null if the touch was not a move.</returns>
        public EmberBlast Fire(EmberMove move)
        {
            if (!Aim(move.From, move.To, out var aim)) return null;

            var log = new EmberBlast();

            var pending = new List<int>(8);
            var blows = new List<EmberBlow>(8);

            switch (aim)
            {
                case EmberAim.Fire:
                    log.Deeds.Add(new EmberDeedRecord(EmberDeed.Tap, move.From, move.From,
                                                      _cells[move.From], 0));
                    _cells[move.From] = EmberLayout.Breach;
                    log.Took++;
                    pending.Add(move.From);
                    blows.Add(EmberBlow.Cross);
                    break;

                case EmberAim.Merge:
                    log.Deeds.Add(new EmberDeedRecord(EmberDeed.Fuse, move.From, move.To,
                                                      _cells[move.From], 0));
                    log.Deeds.Add(new EmberDeedRecord(EmberDeed.Fuse, move.To, move.To,
                                                      _cells[move.To], 0));
                    _cells[move.From] = EmberLayout.Breach;
                    _cells[move.To] = EmberLayout.Breach;
                    log.Took += 2;
                    pending.Add(move.To);
                    blows.Add(EmberBlow.Star);
                    break;

                default:
                    char carried = _cells[move.From];
                    _cells[move.From] = _cells[move.To];
                    _cells[move.To] = carried;
                    log.Deeds.Add(new EmberDeedRecord(EmberDeed.Swap, move.From, move.To,
                                                      carried, 0));
                    break;
            }

            log.Frames.Add(Snapshot());

            var clump = new int[_cells.Length];
            var members = new List<int>(12);

            for (int beat = 1; ; beat++)
            {
                bool acted;

                if (pending.Count > 0)
                {
                    Detonate(log, beat, pending, blows);
                    acted = true;
                }
                else
                {
                    // The swap's cells reach the first pass and no further: what the player's
                    // own move makes stands where their finger ended, and what the cascade makes
                    // settles on its own middle (see Anchor).
                    acted = Fuse(log, beat, beat == 1 ? move : EmberMove.None, clump, members);
                }

                if (!acted) break;

                if (pending.Count == 0) Settle(log, beat);

                log.Beats = beat;
                log.Frames.Add(Snapshot());
            }

            return log;
        }

        EmberFrame Snapshot() => new EmberFrame((char[])_cells.Clone());

        /// <summary>
        /// Finds every run of alike shards, joins the runs that share a cell, and fuses each
        /// group into one ember standing on its anchor.
        /// </summary>
        /// <returns>Whether anything fused.</returns>
        bool Fuse(EmberBlast log, int beat, EmberMove move, int[] clump, List<int> members)
        {
            int w = _layout.Width, h = _layout.Height;

            for (int i = 0; i < clump.Length; i++) clump[i] = -1;

            bool any = false;

            // Union-find over cells, unioned only *within* a run - so two runs that share a cell
            // become one group and two that merely lie alongside each other do not. That is the
            // genre's own rule for an L or a T, and getting it wrong the easy way (flood filling
            // alike neighbours) would silently turn two matches into one and hand the player a
            // single ember for six shards.
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; )
                {
                    char c = _cells[y * w + x];
                    int run = 1;
                    while (x + run < w && _cells[y * w + x + run] == c) run++;

                    if (EmberLayout.IsShard(c) && run >= EmberLayout.FuseAt)
                    {
                        any = true;
                        for (int i = 1; i < run; i++) Join(clump, y * w + x, y * w + x + i);
                    }

                    x += run;
                }

            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; )
                {
                    char c = _cells[y * w + x];
                    int run = 1;
                    while (y + run < h && _cells[(y + run) * w + x] == c) run++;

                    if (EmberLayout.IsShard(c) && run >= EmberLayout.FuseAt)
                    {
                        any = true;
                        for (int i = 1; i < run; i++) Join(clump, y * w + x, (y + i) * w + x);
                    }

                    y += run;
                }

            if (!any) return false;

            // Every group is read whole before any cell is emptied, so the anchors are decided
            // against the wall as it stands.
            for (int root = 0; root < clump.Length; root++)
            {
                if (clump[root] < 0 || Root(clump, root) != root) continue;

                members.Clear();
                for (int i = 0; i < clump.Length; i++)
                    if (clump[i] >= 0 && Root(clump, i) == root) members.Add(i);

                if (members.Count < EmberLayout.FuseAt) continue;

                int anchor = Anchor(members, move);

                for (int i = 0; i < members.Count; i++)
                {
                    log.Deeds.Add(new EmberDeedRecord(EmberDeed.Fuse, members[i], anchor,
                                                      _cells[members[i]], beat));
                    _cells[members[i]] = EmberLayout.Breach;
                }

                log.Took += members.Count - 1;

                _cells[anchor] = EmberLayout.Ember;
                log.Forged++;
                log.Deeds.Add(new EmberDeedRecord(EmberDeed.Forge, anchor, members.Count,
                                                  EmberLayout.Ember, beat));
            }

            return true;
        }

        static void Join(int[] clump, int a, int b)
        {
            if (clump[a] < 0) clump[a] = a;
            if (clump[b] < 0) clump[b] = b;

            int ra = Root(clump, a), rb = Root(clump, b);
            if (ra == rb) return;

            // Lowest index wins, so the forest a wall builds is a fact about the wall and not
            // about the order the two scans happened to run in.
            if (ra < rb) clump[rb] = ra; else clump[ra] = rb;
        }

        static int Root(int[] clump, int cell)
        {
            while (clump[cell] != cell) cell = clump[cell];
            return cell;
        }

        /// <summary>
        /// Where a fused group leaves the ember it made.
        ///
        /// <para>
        /// <b>The cell the finger ended on, whenever the group touches it</b> — invariant 20m's
        /// rule that what you did is what you get. A group the <em>cascade</em> made is handed
        /// <see cref="EmberMove.None"/> and settles on its middle cell: symmetric, deterministic,
        /// and nothing about it can depend on which scan found the run first.
        /// </para>
        /// <para>
        /// <b>That split is what makes a fuse blind to which way the finger went</b>, which the
        /// search relies on (see <see cref="Moves"/>). The two swapped cells hold different
        /// characters and a group is uniform, so the first pass's group can contain at most one
        /// of them and picks the same one either way; every later pass ignores them entirely.
        /// It was written the other way first, and a cascade four beats later could still tell
        /// the two directions apart — which doubled <c>ProtoAnswer.Ways</c> for nothing.
        /// </para>
        /// </summary>
        static int Anchor(List<int> members, EmberMove move)
        {
            for (int i = 0; i < members.Count; i++) if (members[i] == move.To) return move.To;
            for (int i = 0; i < members.Count; i++) if (members[i] == move.From) return move.From;

            return members[members.Count / 2];
        }

        /// <summary>
        /// Fires everything that is primed, all at once, and collects what the beams set off.
        ///
        /// <para>
        /// Every ray of every blast is walked against the wall as it stands <em>before</em>
        /// anything is removed, and only then applied. That is what makes two beams crossing one
        /// warden both see plating, and what stops a chain depending on the order the pending
        /// list happened to be built in.
        /// </para>
        /// </summary>
        void Detonate(EmberBlast log, int beat, List<int> pending, List<EmberBlow> blows)
        {
            int w = _layout.Width, h = _layout.Height;

            var hits = new int[_cells.Length];
            var lit = new List<int>(8);

            for (int i = 0; i < pending.Count; i++)
            {
                int at = pending[i];
                var blow = blows[i];

                if (blow == EmberBlow.Cross) log.Crosses++; else log.Stars++;

                log.Deeds.Add(new EmberDeedRecord(EmberDeed.Blast, at, (int)blow, '\0', beat));

                int rays = blow == EmberBlow.Star ? EmberLayout.StarRays : EmberLayout.CrossRays;

                for (int d = 0; d < rays; d++)
                {
                    int x = at % w, y = at / w;
                    int last = at;

                    while (true)
                    {
                        x += EmberLayout.StepX[d];
                        y += EmberLayout.StepY[d];
                        if (x < 0 || y < 0 || x >= w || y >= h) break;

                        int cell = y * w + x;
                        last = cell;
                        hits[cell]++;

                        if (EmberLayout.Stops(_cells[cell])) break;
                    }

                    log.Deeds.Add(new EmberDeedRecord(EmberDeed.Ray, at, last, (char)('0' + d),
                                                      beat));
                }
            }

            pending.Clear();
            blows.Clear();

            for (int cell = 0; cell < hits.Length; cell++)
            {
                if (hits[cell] == 0) continue;

                char c = _cells[cell];

                if (EmberLayout.IsEmber(c))
                {
                    // The chain. It is spent where it stands and throws its own cross on the
                    // next beat, which is why a chain runs exactly as far as the embers the
                    // player built and no further (invariant 20j's third test).
                    log.Deeds.Add(new EmberDeedRecord(EmberDeed.Ignite, cell, 0, c, beat));
                    _cells[cell] = EmberLayout.Breach;
                    log.Took++;
                    log.Ignited++;
                    lit.Add(cell);
                    continue;
                }

                if (EmberLayout.IsShard(c))
                {
                    log.Deeds.Add(new EmberDeedRecord(EmberDeed.Break, cell, 0, c, beat));
                    _cells[cell] = EmberLayout.Breach;
                    log.Took++;
                    continue;
                }

                switch (c)
                {
                    case EmberLayout.Frost:
                        log.Deeds.Add(new EmberDeedRecord(EmberDeed.Melt, cell, 0, c, beat));
                        _cells[cell] = EmberLayout.Breach;
                        break;

                    case EmberLayout.Cage:
                        log.Deeds.Add(new EmberDeedRecord(EmberDeed.Free, cell, 0, c, beat));
                        _cells[cell] = EmberLayout.Breach;
                        _freed++;
                        log.Freed++;
                        break;

                    case EmberLayout.Warden:
                        // Two beams that reached it in the same beat take both plates, which is
                        // what "read the wall as it stands, then apply" buys: the answer is the
                        // same whichever blast the loop walked first.
                        if (hits[cell] >= EmberLayout.Plates)
                        {
                            log.Deeds.Add(new EmberDeedRecord(EmberDeed.Wreck, cell, 0, c, beat));
                            _cells[cell] = EmberLayout.Breach;
                            _wrecked++;
                            log.Wrecked++;
                        }
                        else
                        {
                            log.Deeds.Add(new EmberDeedRecord(EmberDeed.Crack, cell, 0, c, beat));
                            _cells[cell] = EmberLayout.Scarred;
                        }
                        break;

                    case EmberLayout.Scarred:
                        log.Deeds.Add(new EmberDeedRecord(EmberDeed.Wreck, cell, 0, c, beat));
                        _cells[cell] = EmberLayout.Breach;
                        _wrecked++;
                        log.Wrecked++;
                        break;
                }
            }

            for (int i = 0; i < lit.Count; i++)
            {
                pending.Add(lit[i]);
                blows.Add(EmberBlow.Cross);
            }
        }

        /// <summary>
        /// Gravity. Shards and embers slide down their column; the fittings are bolted and stay.
        ///
        /// <para>
        /// A fitting splits a column into segments and the gems inside one compact to its
        /// bottom, so stone and cages hold the wall up as well as shaping the beams. Nothing is
        /// ever added, which is what keeps the whole mode monotone (invariant 20j's second test)
        /// — a refill would make the wall inexhaustible and the search unbounded, and it is the
        /// one thing this mode may never have.
        /// </para>
        /// </summary>
        void Settle(EmberBlast log, int beat)
        {
            int w = _layout.Width, h = _layout.Height;

            for (int x = 0; x < w; x++)
            {
                int floor = h - 1;

                for (int y = h - 1; y >= 0; y--)
                {
                    char c = _cells[y * w + x];

                    if (c != EmberLayout.Breach && !EmberLayout.IsGem(c))
                    {
                        floor = y - 1;
                        continue;
                    }

                    if (!EmberLayout.IsGem(c)) continue;

                    if (y != floor)
                    {
                        _cells[floor * w + x] = c;
                        _cells[y * w + x] = EmberLayout.Breach;
                        log.Deeds.Add(new EmberDeedRecord(EmberDeed.Drop, y * w + x,
                                                          floor * w + x, c, beat));
                    }

                    floor--;
                }
            }
        }

        // ------------------------------------------------------------------ the key
        /// <summary>
        /// The wall, and nothing else.
        ///
        /// Everything a rule reads is in the cells: how many goals are left is counted from
        /// them, what may be played is decided by them, and a move resolves to a standstill
        /// before it returns, so no beat state ever survives one. A scarred warden is a
        /// different character from a plated one for exactly this reason — two boards a plate
        /// apart merging would under-report par, which hands out stars nobody earned.
        /// </summary>
        public void Write(List<byte> key)
        {
            for (int i = 0; i < _cells.Length; i++) key.Add((byte)_cells[i]);
        }
    }

    /// <summary>
    /// A wall as the search sees it: an arrangement, and the touches available from it.
    ///
    /// <para>
    /// Immutable, so <see cref="Play"/> forks. The search keeps whole layers alive at once and
    /// counts shortest answers by visiting a state from several parents, neither of which
    /// survives a position that mutates in place.
    /// </para>
    /// </summary>
    public sealed class EmberFuture : ProtoPosition
    {
        readonly EmberBoard _board;
        readonly List<EmberMove> _moves = new List<EmberMove>(32);

        public EmberFuture(EmberBoard board)
        {
            _board = board;
            _board.Moves(_moves);
        }

        public EmberBoard Board => _board;

        public override bool Won => _board.IsFinished;

        public override int MoveCount => _moves.Count;

        public override ProtoPosition Play(int move)
        {
            if (move < 0 || move >= _moves.Count) return null;

            var forked = _board.Fork();
            return forked.Fire(_moves[move]) == null ? null : new EmberFuture(forked);
        }

        /// <summary>
        /// What a player who never looks past the move in front of them would notice this one is
        /// worth: the goals it meets first, then the wall it takes down.
        ///
        /// Goals weigh a hundred times a shard, because a careless player in this genre chases
        /// the thing the level is asking for — and a reading where freeing a critter and
        /// breaking three shards score the same is a reading about tidiness rather than play.
        /// </summary>
        public override int Gain(int move)
        {
            if (move < 0 || move >= _moves.Count) return 0;

            var forked = _board.Fork();
            var log = forked.Fire(_moves[move]);

            return log == null ? 0 : log.Goals * 100 + log.Took;
        }

        public override void Write(List<byte> key) => _board.Write(key);
    }
}
