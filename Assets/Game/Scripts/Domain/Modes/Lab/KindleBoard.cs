using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// Kindlewake's board: cold embers scattered over a dark hollow with critters asleep among
    /// them, and the strands of light that are drawn between them.
    ///
    /// <para>
    /// <b>The classic glade's question asked with Emberforge's material.</b> A glade is won by
    /// getting light of the right colour onto every critter, and for four chapters the way to do
    /// that has been to turn conduits until a network joins up. Here there is no network and
    /// nothing turns: two embers of the <em>same</em> colour that share a row or a column are
    /// joined into a <b>strand</b>, the strand burns along the line between them for good, and
    /// every critter it crosses takes that channel. The goal, the colour arithmetic
    /// (<see cref="Energy"/>) and the word "critter" are the glade's; the verb is not.
    /// </para>
    /// <para>
    /// <b>What makes it a puzzle is that light accumulates and embers do not come back.</b> A
    /// critter wanting one channel needs one strand over it. A critter wanting a blend needs
    /// <em>two strands crossing on the cell it is standing on</em> — and every ember is spent by
    /// the strand it is joined into, so choosing which ember to pair with which is choosing which
    /// lines the hollow will ever hold. That is the decision the mode is made of (invariant 26h):
    /// the player is not choosing which pair to light, they are choosing which crossings the
    /// board will be able to make, and being wrong costs the pair rather than the turn.
    /// </para>
    /// <para>
    /// <b>The monotone quantity is the embers</b> — invariant 20j's second test, passed as
    /// directly as Emberforge passes it. Every join spends exactly two and nothing ever puts one
    /// back, so depth is bounded by half the embers on the board, the state graph is a DAG, a run
    /// always ends, and <see cref="ProtoSearch"/> finds par by breadth-first walk with nothing to
    /// prove about termination. Note which quantity that is: the thing the board <em>gains</em>
    /// is light, and light never goes out — it is the thing being <em>spent</em> that is
    /// monotone, which is invariant 33c's warning read the right way round.
    /// </para>
    /// <para>
    /// <b>And nothing here spreads on its own</b> (20j's third test). A strand lights exactly the
    /// cells on its own line and stops; there is no cascade to become a solvent, and no beat after
    /// the first. What replaces a chain as the moment worth drawing is the <b>crossing</b> — the
    /// cell where a new strand meets light the player laid earlier and the two make a colour
    /// neither could — which is a thing they arranged rather than a thing that happened to them
    /// (invariant 20m).
    /// </para>
    /// <para>
    /// It is the twelfth mode the prototype level shape has carried and it cost the save file no
    /// schema version, no merge rule, no <c>firestore.rules</c> change and no server work
    /// (invariant 20a). Like Emberforge it authors the shared block and leaves <c>cores</c>
    /// empty: a hollow is everything the level hands over, because a board with anything dealt
    /// into it has a future nothing can search.
    /// </para>
    /// </summary>
    public sealed class KindleLayout
    {
        // ------------------------------------------------------------------ the vocabulary
        /// <summary>Bare ground. Light crosses it and nothing stands on it.</summary>
        public const char Bare = '.';

        /// <summary>
        /// Standing stone. It stops a strand dead.
        ///
        /// <para>
        /// The one piece of terrain that is purely geometry, exactly as Emberforge's is: it never
        /// changes and it decides which pairs of embers can ever be joined at all. A hollow with
        /// no stone is a hollow where every ember of a colour can reach every other one, so the
        /// arrangement rejects nothing and the pairing decides nothing (invariant 5d).
        /// </para>
        /// <para>
        /// It is not the only thing that stops light — a burnt-out <see cref="Spent"/> socket
        /// does too — but it is the only thing that does so from the moment the board is dealt.
        /// Stone is the geometry the author chose; a socket is the geometry the player made.
        /// </para>
        /// </summary>
        public const char Stone = '#';

        /// <summary>
        /// The three embers, one per channel, written in lower case because they are the
        /// <em>material</em>.
        ///
        /// <para>
        /// <b>An ember is always one pure channel and a blend can never be authored as one.</b>
        /// That is the whole mode in one rule: a blend has to be <em>made</em>, by crossing two
        /// strands on the cell that wants it, and an ember that already carried orange would sell
        /// the answer to the only question the board asks. It is the same argument Lightfall
        /// makes about its procession (<c>FallDto.motes</c>) and Budburst about its basket.
        /// </para>
        /// <para>
        /// The order matters: it is <c>Energy</c>'s channel order, so index <c>i</c> here is
        /// channel <c>1 &lt;&lt; i</c>. Written once and read by <see cref="ChannelOf"/>.
        /// </para>
        /// </summary>
        public const string Embers = "rgb";

        /// <summary>
        /// Every letter a sleeping critter may be written in: the three pure channels and the
        /// four blends, in upper case because a critter is what the level is <em>asking for</em>.
        ///
        /// <para>
        /// Deliberately <see cref="Energy"/>'s own alphabet rather than a second one. A letter
        /// names a mask and never the paint it is drawn in (see <c>Energy.TryParse</c>), so a
        /// <c>M</c> critter wants red and blue and is drawn foxglove — and the four chapters of
        /// glades, both offline mirrors and every content tool already agree about what those
        /// seven letters mean. A mode that minted its own would be a second alphabet for one
        /// arithmetic.
        /// </para>
        /// </summary>
        public const string Sleepers = "RGBYMCW";

        /// <summary>
        /// An ember that has been joined into a strand and gone out: cold slag in the socket it
        /// stood in, and <b>it stops a strand</b>.
        ///
        /// <para>
        /// <b>Not authorable</b>, and that is the point: it is a state a board reaches and never
        /// one it is written in, exactly as Emberforge's scarred warden is. It has to be its own
        /// character rather than bare ground, because <see cref="Stranded"/> counts what is left
        /// to burn and a spent ember that read as bare would be counted as absent — the right
        /// answer by luck rather than by rule — while one that read as an ember would offer a
        /// rescue no purchase could honour.
        /// </para>
        /// <para>
        /// <b>That it blocks light is the rule that makes a pairing a decision, and it was added
        /// because a measurement said the mode needed one.</b> Without it every strand costs
        /// exactly two embers and light never hurts, so taking the pair that covers the most
        /// critters is very nearly always right — across three hundred and twenty swept hollows,
        /// a player who never looked ahead finished <em>every single one</em>. That is the
        /// reading Lightweave was withdrawn for (invariant 5d: a mechanic that rejects no
        /// arrangement is decoration). With it, a strand leaves two permanent holes in the
        /// geometry, so the pair you spend closes lines you may have wanted — and the numbers
        /// moved where they should: <c>ways</c> fell by more than half on the deepest board in
        /// the chapter, and boards a careless player cannot finish exist at all - three of the ten
        /// shipped, against none before.
        /// </para>
        /// </summary>
        public const char Spent = '+';

        /// <summary>
        /// A critter that has woken. Not authorable, for <see cref="Spent"/>'s reason, and it
        /// must be in the key or two boards a critter apart would merge in the search and par
        /// would come out short — which hands out stars nobody earned.
        /// </summary>
        public const char Woken = '*';

        /// <summary>Every character a level of this mode may be written in. The two states are not among them.</summary>
        public const string Letters = ".#" + Embers + Sleepers;

        /// <summary>Embers a strand joins. Two, and there is no version of this rule with three in it.</summary>
        public const int JoinAt = 2;

        // ------------------------------------------------------------------ reading a letter
        /// <summary>Whether this is an ember waiting to be joined.</summary>
        public static bool IsEmber(char c) => Embers.IndexOf(c) >= 0;

        /// <summary>Whether this is a critter still asleep.</summary>
        public static bool IsSleeper(char c) => Sleepers.IndexOf(c) >= 0;

        /// <summary>A critter, awake or asleep: something the level opened by asking for.</summary>
        public static bool IsCritter(char c) => IsSleeper(c) || c == Woken;

        /// <summary>Whether a strand dies here: standing stone, and a burnt-out socket.</summary>
        public static bool Stops(char c) => c == Stone || c == Spent;

        /// <summary>
        /// The channel an ember carries, or <see cref="Energy.None"/>.
        ///
        /// Derived from the letter's place in <see cref="Embers"/> rather than switched on,
        /// because the two facts — which letters are embers, and what each one is worth — must
        /// never be able to disagree.
        /// </summary>
        public static int ChannelOf(char c)
        {
            int at = Embers.IndexOf(c);
            return at < 0 ? Energy.None : 1 << at;
        }

        /// <summary>The mask a sleeping critter is waiting for, or <see cref="Energy.None"/>.</summary>
        public static int WantOf(char c)
            => IsSleeper(c) && Energy.TryParse(c, out int mask) ? mask : Energy.None;

        /// <summary>How many channels a mask carries. One for a pure critter, two or three for a blend.</summary>
        public static int Channels(int mask)
        {
            int n = 0;
            for (int bit = 1; bit <= Energy.B; bit <<= 1) if ((mask & bit) != 0) n++;
            return n;
        }

        // ------------------------------------------------------------------ the hollow
        public readonly ProtoGrid Grid;
        public readonly int Spare;

        /// <summary>
        /// Every pair of cells that could ever be joined, as flat index pairs: same row or same
        /// column, no stone between them, and the same ember letter as authored.
        ///
        /// <para>
        /// <b>Worked out once and kept</b>, because the search asks for the move list at every
        /// position it expands and the geometry of a fixed grid is not news. What it deliberately
        /// does <em>not</em> decide is whether a pair is legal now — an ember that has been spent
        /// is gone and a strand that would help nobody is refused (see
        /// <see cref="KindleBoard.Joins"/>) — so this is the candidate set and never the move
        /// set. Kept in reading order with the lower index first, so the move indices a board
        /// hands out are stable and a fixture can pin them.
        /// </para>
        /// <para>
        /// <b>An ember does not block another ember's strand; stone and a burnt-out socket
        /// do.</b> Light crossing light is the entire point of the mode, so a strand that stopped
        /// at the first thing it met could never make the crossing that is its own payoff — but a
        /// socket is not light, it is what is left when light has been spent, and it closes the
        /// line it stands in for good. That is what makes this list the <em>candidate</em> set
        /// and never the move set: a pair listed here can be walled off later by the player's own
        /// work, which is exactly the consequence the rule exists to create.
        /// </para>
        /// </summary>
        public readonly int[] Pairs;

        /// <summary>What is wrong with this hollow, or null. Read before anything else is asked.</summary>
        public readonly string Fault;

        public int Width => Grid.Width;
        public int Height => Grid.Height;

        public KindleLayout(ProtoGrid grid, int spare)
        {
            Grid = grid;
            Spare = spare > 0 ? spare : 0;

            Pairs = Reachable(grid);
            Fault = Wrong(grid);
        }

        /// <summary>
        /// Every pair of same-coloured embers with a clear line between them.
        ///
        /// Rows first and then columns, both outward from the lower index, so the order is a fact
        /// about the grid rather than about the loop that found it.
        /// </summary>
        static int[] Reachable(ProtoGrid grid)
        {
            int w = grid.Width, h = grid.Height;
            var pairs = new List<int>(64);

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    char here = grid.At(x, y);
                    if (!IsEmber(here)) continue;

                    for (int i = x + 1; i < w; i++)
                    {
                        char there = grid.At(i, y);
                        if (Stops(there)) break;
                        if (there != here) continue;

                        pairs.Add(grid.Index(x, y));
                        pairs.Add(grid.Index(i, y));
                    }
                }

            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    char here = grid.At(x, y);
                    if (!IsEmber(here)) continue;

                    for (int j = y + 1; j < h; j++)
                    {
                        char there = grid.At(x, j);
                        if (Stops(there)) break;
                        if (there != here) continue;

                        pairs.Add(grid.Index(x, y));
                        pairs.Add(grid.Index(x, j));
                    }
                }

            return pairs.ToArray();
        }

        /// <summary>
        /// Everything about an authored hollow that can be read off the file itself.
        ///
        /// The refusals that need the board <em>played</em> — that it can be finished, that a
        /// careless player does not walk it into a corner, that its crossings are doing work —
        /// belong to the mode's reader and its validator. What is here is the set of faults that
        /// make the hollow unopenable rather than unwinnable, because a player's build runs this
        /// and must never open a board that cannot be drawn.
        /// </summary>
        static string Wrong(ProtoGrid grid)
        {
            int sleepers = 0;
            var perChannel = new int[Embers.Length];

            for (int i = 0; i < grid.Count; i++)
            {
                char c = grid.At(i);

                if (IsSleeper(c)) { sleepers++; continue; }

                int at = Embers.IndexOf(c);
                if (at >= 0) perChannel[at]++;
            }

            if (sleepers == 0)
                return "this hollow holds no sleeping critter, so there is nothing to wake and " +
                       "the level opens finished";

            int usable = 0;
            for (int i = 0; i < perChannel.Length; i++) usable += perChannel[i] / JoinAt;

            if (usable == 0)
                return $"no channel on this hollow has {JoinAt} embers, and it takes {JoinAt} " +
                       "alike to draw a strand - so no light could ever be made here";

            return null;
        }
    }

    /// <summary>Something that happened while one strand was drawn, stamped with where.</summary>
    public enum KindleDeed
    {
        /// <summary>An ember joined into the strand and gone out. <c>At</c> is its cell.</summary>
        Spend = 0,

        /// <summary>
        /// The strand itself. <c>At</c> and <c>To</c> are its two ends and <c>What</c> is the
        /// ember letter, so a view can draw the line without walking the board.
        /// </summary>
        Strand = 1,

        /// <summary>
        /// A cell taking the strand's channel. <c>To</c> is the mask it carries afterwards.
        ///
        /// Raised for every cell on the line, including bare ground, because the light stays and
        /// the whole line is what the player has to be able to read afterwards.
        /// </summary>
        Light = 2,

        /// <summary>
        /// A cell where this strand met light that was already there, in a channel it does not
        /// carry. <c>To</c> is the mask afterwards — which is a colour neither strand could make
        /// alone, and the thing the player arranged.
        /// </summary>
        Cross = 3,

        /// <summary>
        /// A critter woken. <c>To</c> is the mask that woke it, <c>What</c> the letter it was
        /// asleep as. A goal met.
        /// </summary>
        Wake = 4,

        /// <summary>
        /// A sleeping critter that took a channel and is still short. <c>To</c> is what it holds
        /// now, <c>What</c> the letter it wants — so the view can show the gap closing.
        /// </summary>
        Stir = 5,
    }

    /// <summary>One thing that happened, and where.</summary>
    public readonly struct KindleDeedRecord
    {
        public readonly KindleDeed Deed;

        /// <summary>The cell it happened on.</summary>
        public readonly int At;

        /// <summary>A second cell, or a mask. What it means is the deed's business.</summary>
        public readonly int To;

        /// <summary>The character that was standing there, for a view drawing the thing that went.</summary>
        public readonly char What;

        public KindleDeedRecord(KindleDeed deed, int at, int to, char what)
        {
            Deed = deed;
            At = at;
            To = to;
            What = what;
        }
    }

    /// <summary>
    /// Everything one join did, in the order it did it.
    ///
    /// <para>
    /// <b>A log rather than a diff</b>, for invariant 30i's reason: a view that worked out what
    /// changed by comparing two boards would be doing arithmetic that no par, no <c>ways</c>, no
    /// validator and no content gate can ever see going wrong, because every one of those reads
    /// the model and the model is right the whole time. Deep Orbit shipped exactly that fault and
    /// nothing in this project could see it.
    /// </para>
    /// <para>
    /// There is no beat here and no frame list, and that is a fact about the mode rather than an
    /// omission: a strand does nothing after it is drawn, so a join resolves in one pass and the
    /// board it leaves is the board the log describes.
    /// </para>
    /// </summary>
    public sealed class KindleFlare
    {
        public readonly List<KindleDeedRecord> Deeds = new List<KindleDeedRecord>(32);

        /// <summary>The two ends of the strand, and the channel it carries.</summary>
        public int From { get; internal set; }
        public int To { get; internal set; }
        public int Channel { get; internal set; }

        /// <summary>Cells the strand covers, ends included. What the light is drawn along.</summary>
        public int Span { get; internal set; }

        /// <summary>Critters woken. The only thing this mode counts as progress.</summary>
        public int Woke { get; internal set; }

        /// <summary>Critters that took a channel and are still short.</summary>
        public int Stirred { get; internal set; }

        /// <summary>
        /// Cells where this strand crossed light already on the board and made a colour neither
        /// carried. The mode's payoff, counted.
        /// </summary>
        public int Crossings { get; internal set; }

        /// <summary>Critters woken <em>by</em> a crossing: a blend the player built over two moves.</summary>
        public int Blended { get; internal set; }
    }

    /// <summary>
    /// One join: the two embers a finger drew between.
    ///
    /// <para>
    /// <b>Unordered, and that is provable rather than observed.</b> A strand covers the cells
    /// between its two ends and lights every one of them with one channel, so which end the
    /// finger started at cannot reach any rule — the light is the same, the two embers are spent
    /// either way, and no cell can tell them apart afterwards. <see cref="KindleBoard.Joins"/>
    /// therefore emits each pair once, with the lower index first. Emitting both would double the
    /// work at every position and, worse, double-count <c>ProtoAnswer.Ways</c> at every depth,
    /// because the search adds the paths of two move indices that reach one state — which is
    /// exactly the fault Emberforge shipped and had to prove its way out of (invariant 34d).
    /// </para>
    /// </summary>
    public readonly struct KindleMove
    {
        public readonly int From;
        public readonly int To;

        public KindleMove(int from, int to)
        {
            From = from < to ? from : to;
            To = from < to ? to : from;
        }

        public bool IsReal => From >= 0 && To > From;
    }

    /// <summary>
    /// A hollow being lit, and the one method that does it.
    ///
    /// <para>
    /// <b>Every claim is read off the board as it stands and only then applied</b>, which is
    /// <c>FallBoard.Resolve</c>'s discipline and the thing a second runtime diverges on silently.
    /// A strand's whole line is walked and its light accumulated before any critter is asked
    /// whether it has woken, so a critter standing at a crossing of the strand with itself — the
    /// two ends of one line — is judged against the finished light and never against the light as
    /// it happened to be when the loop reached it.
    /// </para>
    /// </summary>
    public sealed class KindleBoard : IProtoBoard
    {
        readonly KindleLayout _layout;
        readonly char[] _cells;

        /// <summary>
        /// What light stands on each cell: the union of every strand that has crossed it.
        ///
        /// <para>
        /// <b>Part of the state and therefore part of the key.</b> It is the only thing on this
        /// board that grows, and two arrangements holding the same embers under different light
        /// are two different boards — merging them would under-report par, which is the direction
        /// that hands out stars nobody earned.
        /// </para>
        /// </summary>
        readonly byte[] _light;

        readonly int _goals;
        int _woke;

        KindleBoard(KindleLayout layout, char[] cells, byte[] light, int goals, int woke)
        {
            _layout = layout;
            _cells = cells;
            _light = light;
            _goals = goals;
            _woke = woke;
        }

        public static KindleBoard Build(KindleLayout layout)
        {
            var cells = layout.Grid.Copy();

            int goals = 0;
            for (int i = 0; i < cells.Length; i++)
                if (KindleLayout.IsSleeper(cells[i])) goals++;

            return new KindleBoard(layout, cells, new byte[cells.Length], goals, 0);
        }

        /// <summary>A copy nobody else is holding, for a preview or for the search.</summary>
        public KindleBoard Fork()
            => new KindleBoard(_layout, (char[])_cells.Clone(), (byte[])_light.Clone(),
                               _goals, _woke);

        public KindleLayout Layout => _layout;

        public IReadOnlyList<char> Cells => _cells;

        public char At(int cell) => cell < 0 || cell >= _cells.Length ? '\0' : _cells[cell];

        /// <summary>The light standing on a cell. What the view paints and what a critter is judged against.</summary>
        public int Light(int cell) => cell < 0 || cell >= _light.Length ? Energy.None : _light[cell];

        public int Width => _layout.Width;
        public int Height => _layout.Height;

        public int Goals => _goals;
        public int GoalsLeft => _goals - _woke;
        public bool IsFinished => GoalsLeft == 0;

        // ------------------------------------------------------------------ what may be played
        /// <summary>
        /// Whether these two cells may be joined, and what the strand would carry.
        ///
        /// <para>
        /// <b>One predicate rather than several</b>, because every caller — the search, the view,
        /// the validator, the offline mirror — has to agree about what a finger just did, and four
        /// of them asking four questions is four chances to disagree about whether a move happened
        /// at all.
        /// </para>
        /// <para>
        /// Three clauses do the work. Both cells still hold an <b>unspent ember of the same
        /// channel</b>; the line between them is <b>clear of stone</b>; and the strand
        /// <b>puts its channel somewhere that did not have it</b> — see <see cref="Changes"/>,
        /// which is the whole of what this mode calls a no-op.
        /// </para>
        /// </summary>
        public bool Join(int a, int b, out int channel)
        {
            channel = Energy.None;

            if (a < 0 || b < 0 || a >= _cells.Length || b >= _cells.Length || a == b) return false;

            char here = _cells[a];
            if (here != _cells[b] || !KindleLayout.IsEmber(here)) return false;

            if (!Aligned(a, b)) return false;

            channel = KindleLayout.ChannelOf(here);
            return Changes(a, b, channel);
        }

        /// <summary>
        /// Whether two cells share a row or a column with no stone strictly between them.
        ///
        /// Asked rather than assumed, because a move arrives from a finger as well as from the
        /// search: a drag across the hollow would otherwise be honoured as a strand between two
        /// embers with a wall between them.
        /// </summary>
        public bool Aligned(int a, int b)
        {
            int w = _layout.Width;
            int ax = a % w, ay = a / w, bx = b % w, by = b / w;

            if (ax != bx && ay != by) return false;

            int stepX = ax == bx ? 0 : (bx > ax ? 1 : -1);
            int stepY = ay == by ? 0 : (by > ay ? 1 : -1);

            int x = ax + stepX, y = ay + stepY;
            while (x != bx || y != by)
            {
                if (KindleLayout.Stops(_cells[y * w + x])) return false;
                x += stepX;
                y += stepY;
            }

            return true;
        }

        /// <summary>
        /// Whether a strand of this channel between these two cells would put light anywhere
        /// that does not already carry it.
        ///
        /// <para>
        /// <b>This is the whole of what this mode calls a no-op, and drawing that line anywhere
        /// tighter is a mistake the mode was built with and had to have taken back out.</b> The
        /// tighter rule — refuse a strand unless it reaches a <em>sleeping critter</em> that lacks
        /// the channel — is provably safe for the solver, because light only ever wakes critters
        /// and removing embers can never help, so such a strand can never be in a shortest
        /// answer. It was written that way first, and it is wrong for a reason no solver can see:
        /// <b>it removes the player's ability to be wrong</b>. A hollow under it has no move that
        /// spends material for nothing, so the allowance can never bind, the meter counts down to
        /// an ending that cannot happen, and every board ends either won or stuck. Invariant 26h
        /// asks what the player decides and whether they can be <em>wrong</em>, and under the
        /// tight rule the honest answer was "not really".
        /// </para>
        /// <para>
        /// So a strand is legal whenever it changes the board, which is
        /// <see cref="ProtoPosition.Play"/>'s own contract taken literally: refused only when
        /// every cell it would cross already carries its channel, which really is a move that
        /// achieves nothing at all. What remains is a mode where spending a pair badly is a
        /// permanent, countable mistake — and where <c>KindleReading.Life</c> exceeds par, so the
        /// allowance is a fail state rather than an ornament.
        /// </para>
        /// </summary>
        bool Changes(int a, int b, int channel)
        {
            int w = _layout.Width;
            int ax = a % w, ay = a / w, bx = b % w, by = b / w;

            int stepX = ax == bx ? 0 : (bx > ax ? 1 : -1);
            int stepY = ay == by ? 0 : (by > ay ? 1 : -1);

            int x = ax, y = ay;
            while (true)
            {
                int cell = y * w + x;

                if ((_light[cell] & channel) == 0) return true;

                if (x == bx && y == by) return false;

                x += stepX;
                y += stepY;
            }
        }

        /// <summary>
        /// Every join available, in the layout's own stable order.
        ///
        /// <b>Each pair once.</b> A strand is unordered — see <see cref="KindleMove"/> — so
        /// emitting both directions would double the work at every position and double-count
        /// <c>ProtoAnswer.Ways</c> at every depth.
        /// </summary>
        public void Joins(List<KindleMove> into)
        {
            into.Clear();

            var pairs = _layout.Pairs;
            for (int i = 0; i < pairs.Length; i += 2)
            {
                int a = pairs[i], b = pairs[i + 1];
                if (Join(a, b, out _)) into.Add(new KindleMove(a, b));
            }
        }

        public bool AnyMove
        {
            get
            {
                var pairs = _layout.Pairs;
                for (int i = 0; i < pairs.Length; i += 2)
                    if (Join(pairs[i], pairs[i + 1], out _)) return true;

                return false;
            }
        }

        /// <summary>
        /// Whether this hollow can be <em>proved</em> never to finish, however many moves were
        /// bought.
        ///
        /// <para>
        /// A certainty and never a guess (invariant 28f), so it under-reports. Every critter here
        /// is woken by light, every channel of light costs two unspent embers of that channel,
        /// and nothing ever puts one back — so a critter still wanting a channel the board can no
        /// longer raise <em>two</em> embers of can never wake, whatever is bought. Geometry is
        /// deliberately ignored: it can only make fewer things possible, which is the safe
        /// direction for an answer that decides whether money changes hands.
        /// </para>
        /// </summary>
        public bool Stranded
        {
            get
            {
                var left = new int[KindleLayout.Embers.Length];

                for (int i = 0; i < _cells.Length; i++)
                {
                    int at = KindleLayout.Embers.IndexOf(_cells[i]);
                    if (at >= 0) left[at]++;
                }

                for (int i = 0; i < _cells.Length; i++)
                {
                    char c = _cells[i];
                    if (!KindleLayout.IsSleeper(c)) continue;

                    int wanted = KindleLayout.WantOf(c) & ~_light[i];

                    for (int k = 0; k < left.Length; k++)
                        if ((wanted & (1 << k)) != 0 && left[k] < KindleLayout.JoinAt) return true;
                }

                return false;
            }
        }

        // ------------------------------------------------------------------ playing a move
        /// <summary>
        /// Draws one strand and resolves everything that follows from it.
        ///
        /// <para>
        /// <b>The whole line is lit before any critter is asked whether it has woken.</b> A
        /// strand can cover several critters and one of them may be completed by light further
        /// along its own line, so judging as the walk goes would make the answer depend on which
        /// end the loop started from — the class of divergence a second runtime cannot see. The
        /// two ends are spent last, for the same reason: they are read as embers by the walk that
        /// lights them.
        /// </para>
        /// </summary>
        /// <returns>What happened, or null if the join was not a move.</returns>
        public KindleFlare Draw(KindleMove move)
        {
            if (!Join(move.From, move.To, out int channel)) return null;

            var log = new KindleFlare
            {
                From = move.From,
                To = move.To,
                Channel = channel,
            };

            log.Deeds.Add(new KindleDeedRecord(KindleDeed.Strand, move.From, move.To,
                                               _cells[move.From]));

            int w = _layout.Width;
            int ax = move.From % w, ay = move.From / w;
            int bx = move.To % w, by = move.To / w;

            int stepX = ax == bx ? 0 : (bx > ax ? 1 : -1);
            int stepY = ay == by ? 0 : (by > ay ? 1 : -1);

            // The line, lit whole. Every cell is read as it stands, its light raised, and the
            // crossing noted - and nothing is asked about a critter until the walk is done.
            var line = new List<int>(_layout.Width + _layout.Height);

            int x = ax, y = ay;
            while (true)
            {
                int cell = y * w + x;
                line.Add(cell);

                int was = _light[cell];
                _light[cell] = (byte)(was | channel);

                // A crossing is light meeting light of *another* channel. Light meeting its own
                // is a strand laid over a strand, which is a picture and not an event - and
                // counting it would let a board claim a payoff it never made (invariant 20m).
                if (was != Energy.None && (was & channel) == 0)
                {
                    log.Crossings++;
                    log.Deeds.Add(new KindleDeedRecord(KindleDeed.Cross, cell, _light[cell],
                                                       _cells[cell]));
                }
                else
                {
                    log.Deeds.Add(new KindleDeedRecord(KindleDeed.Light, cell, _light[cell],
                                                       _cells[cell]));
                }

                if (x == bx && y == by) break;

                x += stepX;
                y += stepY;
            }

            log.Span = line.Count;

            for (int i = 0; i < line.Count; i++)
            {
                int cell = line[i];
                char c = _cells[cell];
                if (!KindleLayout.IsSleeper(c)) continue;

                int want = KindleLayout.WantOf(c);
                int held = _light[cell];

                if ((held & want) == want)
                {
                    _cells[cell] = KindleLayout.Woken;
                    _woke++;
                    log.Woke++;

                    // Woken by a blend it took more than one strand to build, which is the thing
                    // the player arranged rather than the thing they were handed.
                    if (KindleLayout.Channels(want) > 1) log.Blended++;

                    log.Deeds.Add(new KindleDeedRecord(KindleDeed.Wake, cell, held, c));
                }
                else
                {
                    log.Stirred++;
                    log.Deeds.Add(new KindleDeedRecord(KindleDeed.Stir, cell, held, c));
                }
            }

            _cells[move.From] = KindleLayout.Spent;
            _cells[move.To] = KindleLayout.Spent;

            log.Deeds.Add(new KindleDeedRecord(KindleDeed.Spend, move.From, channel,
                                               KindleLayout.Embers[0]));
            log.Deeds.Add(new KindleDeedRecord(KindleDeed.Spend, move.To, channel,
                                               KindleLayout.Embers[0]));

            return log;
        }

        // ------------------------------------------------------------------ the key
        /// <summary>
        /// The cells and the light, and nothing else.
        ///
        /// <para>
        /// Everything a rule reads is in those two: which embers are left decides what may be
        /// played, and the light decides what a critter is short of. A strand's <em>identity</em>
        /// is deliberately absent — two different pairs of embers that lit the same cells in the
        /// same channels leave a board nothing can tell apart, and merging them is what keeps the
        /// search inside its budget. The strands a view draws come from the log, which is a
        /// record of what happened rather than part of what is true (invariant 30i).
        /// </para>
        /// </summary>
        public void Write(List<byte> key)
        {
            for (int i = 0; i < _cells.Length; i++) key.Add((byte)_cells[i]);
            for (int i = 0; i < _light.Length; i++) key.Add(_light[i]);
        }
    }

    /// <summary>
    /// A hollow as the search sees it: an arrangement, and the joins available from it.
    ///
    /// <para>
    /// Immutable, so <see cref="Play"/> forks. The search keeps whole layers alive at once and
    /// counts shortest answers by visiting one state from several parents, neither of which
    /// survives a position that mutates in place.
    /// </para>
    /// </summary>
    public sealed class KindleFuture : ProtoPosition
    {
        readonly KindleBoard _board;
        readonly List<KindleMove> _moves = new List<KindleMove>(24);

        public KindleFuture(KindleBoard board)
        {
            _board = board;
            _board.Joins(_moves);
        }

        public KindleBoard Board => _board;

        public override bool Won => _board.IsFinished;

        public override int MoveCount => _moves.Count;

        public override ProtoPosition Play(int move)
        {
            if (move < 0 || move >= _moves.Count) return null;

            var forked = _board.Fork();
            return forked.Draw(_moves[move]) == null ? null : new KindleFuture(forked);
        }

        /// <summary>
        /// What a player who never looks past the line in front of them would notice this one is
        /// worth: the critters it wakes first, then the ones it stirs.
        ///
        /// Waking weighs a hundred times stirring, because a careless player in a mode about
        /// waking things chases the thing that wakes — and a reading where waking a critter and
        /// half-lighting one score the same is a reading about tidiness rather than about play.
        /// </summary>
        public override int Gain(int move)
        {
            if (move < 0 || move >= _moves.Count) return 0;

            var forked = _board.Fork();
            var log = forked.Draw(_moves[move]);

            return log == null ? 0 : log.Woke * 100 + log.Stirred * 4 + log.Crossings;
        }

        public override void Write(List<byte> key) => _board.Write(key);
    }
}
