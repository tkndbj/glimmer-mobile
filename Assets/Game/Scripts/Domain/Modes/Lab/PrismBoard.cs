using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// Prismvale's board: a field of coloured gems with lanterns standing in it and critters
    /// asleep among them, and the veins of light that run from one to the other.
    ///
    /// <para>
    /// <b>The classic glade's question asked with a jewel board's verb.</b> A glade is won by
    /// getting light onto every critter, and for four chapters the way to do that has been to
    /// turn conduits until a network joins up. Here nothing turns and nothing bursts: a lantern
    /// feeds the gems of <em>its own colour</em> that are touching it, that colour runs on
    /// through every gem of the same colour beside it, and any critter standing against that
    /// vein wakes. What the player does is <b>drag one gem onto its neighbour</b> and the two
    /// change places — the genre's own gesture, with the match-three taken out of it.
    /// </para>
    /// <para>
    /// <b>Nothing is ever removed, and that is the whole shape of the mode.</b> Gems do not
    /// burst, do not fall and are never spent: the board a player is dealt is the board they
    /// finish on, so every move is a rearrangement rather than a purchase. That is what makes a
    /// vein something to <em>build</em>, and it is also why a vein can be <em>broken</em> —
    /// light here is not stored, it is read off the arrangement, so a gem pulled out of a vein
    /// takes the light with it and a swap made carelessly on one side of the board can put out
    /// the line on the other.
    /// </para>
    /// <para>
    /// <b>What it does not pass, and what it passes instead.</b> Invariant 20j's second test
    /// asks whether every legal input strictly moves something that only goes one way, and a
    /// swap does not: swap two gems back and forth for ever and the board is where it started.
    /// So the honest statement is narrower and is the one the search actually needs — the
    /// <em>goal</em> count is monotone, because a critter that wakes never sleeps again; the
    /// board is finite, so the arrangements are finite and the visited set closes the walk; and
    /// a run can never stall, because two touching gems of different colours are always a legal
    /// move. What that costs is a real ceiling on par: cost goes as the swap count to the power
    /// of par, so these boards are authored at par three and four and
    /// <see cref="PrismValidator"/> is what proves the search stays affordable on a phone
    /// (invariant 26d).
    /// </para>
    /// <para>
    /// <b>And what falls out of it: the fail state is the meter and nothing else.</b> Every
    /// other mode built on this shape has two endings, because its material runs out — a well
    /// runs dry, a hollow runs out of embers, a wall runs out of shards. Nothing here is ever
    /// consumed, so <see cref="AnyMove"/> is true until the last critter wakes and the only way
    /// to lose is to spend the allowance. That is why a run may always be sold a continue
    /// (invariant 28f) and why this mode needs no <c>life</c> reading at all.
    /// </para>
    /// </summary>
    public sealed class PrismLayout
    {
        /// <summary>
        /// Bare ground. No gem stands on it, nothing swaps with it, and a vein stops dead at it.
        ///
        /// <para>
        /// <b>The only thing that shapes a vein, and that is deliberate.</b> A second blocking
        /// character — stone beside bare ground — would be two letters wearing one rule, which
        /// is how two letters come to disagree. What a board needs is somewhere the light may
        /// not go, and empty ground says that without bringing a second idea to teach.
        /// </para>
        /// </summary>
        public const char Bare = '.';

        /// <summary>
        /// The four gems, lower case because they are the material. Index i is colour i.
        ///
        /// <b>Four, and they differ in silhouette as well as in hue</b> — Emberforge's rule
        /// (invariant 34f) and it decides more here than it does there: the whole verb is "is
        /// this gem the same as that one", so a player who cannot separate red from green has to
        /// be able to separate a heart from a rhombus. A palette alone would make this a
        /// different game for them rather than a harder one.
        /// </summary>
        public const string Gems = "rgby";

        /// <summary>
        /// The four lanterns, upper case because they are fixed. A lantern of colour i feeds
        /// gems of colour i and no others, which is the whole of what colour decides here.
        /// </summary>
        public const string Lamps = "RGBY";

        /// <summary>
        /// A critter asleep.
        ///
        /// <para>
        /// <b>It wants light and does not care which colour brings it</b>, and that is a
        /// decision rather than an omission. The colour already decides everything through the
        /// <em>lantern</em>: which gems are worth moving, which lantern is worth using, and
        /// which of two routes is affordable. Giving a critter a colour of its own would put the
        /// same question in twice and add a fourth idea to a board that is meant to be read at a
        /// glance. A chapter that later wants blends has a whole letter space free for them.
        /// </para>
        /// </summary>
        public const char Asleep = '@';

        /// <summary>
        /// A critter that has woken. Not authorable, and it must be in the key or two boards a
        /// critter apart would merge in the search and par would come out short.
        /// </summary>
        public const char Awake = '*';

        public const string Letters = "." + Gems + Lamps + "@";

        public static bool IsGem(char c) => Gems.IndexOf(c) >= 0;
        public static bool IsLamp(char c) => Lamps.IndexOf(c) >= 0;
        public static bool IsCritter(char c) => c == Asleep || c == Awake;

        /// <summary>The colour a gem or a lantern carries, or -1.</summary>
        public static int HueOf(char c)
        {
            int gem = Gems.IndexOf(c);
            if (gem >= 0) return gem;

            int lamp = Lamps.IndexOf(c);
            return lamp >= 0 ? lamp : -1;
        }

        public readonly ProtoGrid Grid;
        public readonly int Spare;

        /// <summary>
        /// Every pair of touching cells that both hold a gem, as flat pairs of indices.
        ///
        /// <para>
        /// <b>A fact about the layout rather than about a position</b>, because a swap only ever
        /// exchanges two gems: which cells hold gems at all never changes for the life of a run.
        /// So this is worked out once and every board forked from it shares the same move
        /// numbering, which is what makes a move index mean the same thing to the search, to the
        /// validator and to the offline mirror.
        /// </para>
        /// <para>
        /// Row-major, right neighbour before the one below, so each pair appears exactly once
        /// and the order is a fact about the grid rather than about the loop that found it. The
        /// order is <b>contract</b>: <see cref="PrismFuture"/>'s move indices come from it.
        /// </para>
        /// </summary>
        public readonly int[] Swaps;

        /// <summary>Why this board cannot be opened at all, or null.</summary>
        public readonly string Fault;

        /// <summary>
        /// Cells of critters that no arrangement of this board could ever wake.
        ///
        /// <b>Certain rather than clever</b> (invariant 28f). Two facts, and both hold under
        /// every swap there is: a critter with no gem beside it can never be touched by a vein,
        /// and a critter whose gems belong to a run of the board no lantern touches can never be
        /// fed. What it deliberately does not model is whether there are <em>enough</em> gems of
        /// the right colour, or whether two critters want the same ones — that is contention,
        /// and contention is the search's job.
        /// </summary>
        public readonly int[] Marooned;

        public int Width => Grid.Width;
        public int Height => Grid.Height;
        public int Count => Grid.Count;

        public char At(int cell) => Grid.At(cell);

        public PrismLayout(ProtoGrid grid, int spare)
        {
            Grid = grid;
            Spare = spare > 0 ? spare : 0;
            Swaps = FindSwaps(grid);
            Marooned = FindMarooned(grid);
            Fault = Wrong(grid);
        }

        static int[] FindSwaps(ProtoGrid grid)
        {
            var pairs = new List<int>(grid.Count * 2);

            for (int y = 0; y < grid.Height; y++)
                for (int x = 0; x < grid.Width; x++)
                {
                    if (!IsGem(grid.At(x, y))) continue;

                    int here = grid.Index(x, y);

                    if (x + 1 < grid.Width && IsGem(grid.At(x + 1, y)))
                    {
                        pairs.Add(here);
                        pairs.Add(grid.Index(x + 1, y));
                    }

                    if (y + 1 < grid.Height && IsGem(grid.At(x, y + 1)))
                    {
                        pairs.Add(here);
                        pairs.Add(grid.Index(x, y + 1));
                    }
                }

            return pairs.ToArray();
        }

        /// <summary>Faults that make a board unopenable rather than unwinnable.</summary>
        static string Wrong(ProtoGrid grid)
        {
            int critters = 0, lamps = 0, gems = 0;

            for (int i = 0; i < grid.Count; i++)
            {
                char c = grid.At(i);
                if (c == Asleep) critters++;
                else if (IsLamp(c)) lamps++;
                else if (IsGem(c)) gems++;
            }

            if (critters == 0)
                return "this board holds no sleeping critter, so there is nothing to wake and " +
                       "the level opens finished";

            if (lamps == 0)
                return "this board holds no lantern, so no gem on it could ever carry light and " +
                       "nothing could ever be woken";

            if (gems < 2)
                return "this board holds fewer than two gems, so there is no swap to make and " +
                       "the run is over before it begins";

            return null;
        }

        /// <summary>
        /// The cells touching this one, written into <paramref name="into"/>.
        ///
        /// Left, right, up, down, in that order — which is contract, because
        /// <see cref="PrismBoard.WokeBy"/> takes the first lit neighbour it finds and a
        /// different order would hand a critter to a different lantern's colour.
        /// </summary>
        public void Around(int cell, List<int> into)
        {
            into.Clear();

            int w = Width;
            int x = cell % w, y = cell / w;

            if (x > 0) into.Add(cell - 1);
            if (x + 1 < w) into.Add(cell + 1);
            if (y > 0) into.Add(cell - w);
            if (y + 1 < Height) into.Add(cell + w);
        }

        /// <summary>
        /// Which connected run of gem cells each cell belongs to, or -1.
        ///
        /// A gem cell never becomes anything else, so this is a fact about the layout: a vein
        /// can only ever run inside one of these, whatever the player does with the colours.
        /// </summary>
        public int[] Regions() => RegionsOf(Grid);

        static int[] RegionsOf(ProtoGrid grid)
        {
            var into = new int[grid.Count];
            for (int i = 0; i < into.Length; i++) into[i] = -1;

            var stack = new List<int>(grid.Count);
            var around = new List<int>(4);
            int n = 0;

            for (int start = 0; start < grid.Count; start++)
            {
                if (into[start] >= 0 || !IsGem(grid.At(start))) continue;

                stack.Clear();
                stack.Add(start);
                into[start] = n;

                while (stack.Count > 0)
                {
                    int at = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);

                    Neighbours(grid, at, around);
                    for (int i = 0; i < around.Count; i++)
                    {
                        int nb = around[i];
                        if (into[nb] >= 0 || !IsGem(grid.At(nb))) continue;

                        into[nb] = n;
                        stack.Add(nb);
                    }
                }

                n++;
            }

            return into;
        }

        /// <summary>
        /// Critters no arrangement of this board could ever wake, worked out once.
        ///
        /// Built off the grid rather than off this object's own fields, because it runs from
        /// the constructor and <see cref="Grid"/> is the only one of them already set.
        /// </summary>
        static int[] FindMarooned(ProtoGrid grid)
        {
            var into = RegionsOf(grid);
            var around = new List<int>(4);

            var fed = new HashSet<int>();
            for (int cell = 0; cell < grid.Count; cell++)
            {
                if (!IsLamp(grid.At(cell))) continue;

                Neighbours(grid, cell, around);
                for (int i = 0; i < around.Count; i++)
                    if (into[around[i]] >= 0) fed.Add(into[around[i]]);
            }

            var stuck = new List<int>();
            for (int cell = 0; cell < grid.Count; cell++)
            {
                if (grid.At(cell) != Asleep) continue;

                bool reachable = false;
                Neighbours(grid, cell, around);
                for (int i = 0; i < around.Count; i++)
                {
                    int region = into[around[i]];
                    if (region >= 0 && fed.Contains(region)) { reachable = true; break; }
                }

                if (!reachable) stuck.Add(cell);
            }

            return stuck.ToArray();
        }

        static void Neighbours(ProtoGrid grid, int cell, List<int> into)
        {
            into.Clear();

            int w = grid.Width;
            int x = cell % w, y = cell / w;

            if (x > 0) into.Add(cell - 1);
            if (x + 1 < w) into.Add(cell + 1);
            if (y > 0) into.Add(cell - w);
            if (y + 1 < grid.Height) into.Add(cell + w);
        }
    }

    /// <summary>What one entry of a <see cref="PrismFlare"/> is.</summary>
    public enum PrismDeed
    {
        /// <summary>Two gems changed places. Always first, and always exactly one per move.</summary>
        Swap = 0,

        /// <summary>A gem took light it was not carrying. In flood order out of its lantern.</summary>
        Lit = 1,

        /// <summary>A gem lost the light it was carrying, because the vein feeding it was broken.</summary>
        Dark = 2,

        /// <summary>A critter woke.</summary>
        Wake = 3,
    }

    /// <summary>
    /// One thing a move did, for a view to replay.
    ///
    /// <b>A log rather than a recomputation</b>, which is invariant 30i: the arithmetic a view
    /// would otherwise do to work out what a move must have done is exactly the arithmetic no
    /// par, no <c>ways</c>, no validator and no content gate can ever see going wrong.
    /// </summary>
    public readonly struct PrismDeedRecord
    {
        public readonly PrismDeed Deed;

        /// <summary>The cell it happened on. For <see cref="PrismDeed.Swap"/>, where the drag began.</summary>
        public readonly int At;

        /// <summary>Where the drag ended, for a swap. Unused otherwise.</summary>
        public readonly int To;

        /// <summary>The colour involved, or -1. For a waking, the colour that reached it.</summary>
        public readonly int Hue;

        /// <summary>What is standing on the cell now.</summary>
        public readonly char What;

        public PrismDeedRecord(PrismDeed deed, int at, int to, int hue, char what)
        {
            Deed = deed;
            At = at;
            To = to;
            Hue = hue;
            What = what;
        }
    }

    /// <summary>Everything one swap did, in the order a view should replay it.</summary>
    public sealed class PrismFlare
    {
        public readonly List<PrismDeedRecord> Deeds = new List<PrismDeedRecord>(32);

        /// <summary>The two cells that changed places.</summary>
        public int From { get; internal set; }
        public int To { get; internal set; }

        /// <summary>Gems that took light.</summary>
        public int Lit { get; internal set; }

        /// <summary>Gems that lost it.</summary>
        public int Dark { get; internal set; }

        /// <summary>Critters woken.</summary>
        public int Woke { get; internal set; }
    }

    /// <summary>One swap: two touching cells that both hold a gem.</summary>
    public readonly struct PrismMove
    {
        public readonly int From;
        public readonly int To;

        public PrismMove(int from, int to)
        {
            From = from;
            To = to;
        }

        public bool IsReal => From >= 0 && To >= 0 && From != To;
    }

    /// <summary>
    /// A Prismvale board being played: the gems where they are standing now, and which critters
    /// have woken.
    ///
    /// <para>
    /// <b>The light is not state.</b> Which cells are lit is a pure function of the arrangement
    /// — walk out of each lantern through gems of its own colour — so it is derived on demand
    /// and cached until the next swap, and it is deliberately <em>not</em> part of the key. A
    /// key carrying a derived value is a key that can disagree with itself, and here it would
    /// also be a key that never merged two identical boards.
    /// </para>
    /// </summary>
    public sealed class PrismBoard : IProtoBoard
    {
        readonly PrismLayout _layout;
        readonly char[] _cells;

        int[] _veins;
        readonly int _goals;
        int _woke;

        readonly List<int> _around = new List<int>(4);
        readonly List<int> _flood = new List<int>(64);

        PrismBoard(PrismLayout layout, char[] cells, int goals, int woke)
        {
            _layout = layout;
            _cells = cells;
            _goals = goals;
            _woke = woke;
        }

        public static PrismBoard Build(PrismLayout layout)
        {
            var cells = layout.Grid.Copy();

            int goals = 0;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] == PrismLayout.Asleep) goals++;

            return new PrismBoard(layout, cells, goals, 0);
        }

        public PrismBoard Fork() => new PrismBoard(_layout, (char[])_cells.Clone(), _goals, _woke);

        public PrismLayout Layout => _layout;

        public IReadOnlyList<char> Cells => _cells;

        public char At(int cell) => cell < 0 || cell >= _cells.Length ? '\0' : _cells[cell];

        public int Width => _layout.Width;
        public int Height => _layout.Height;

        public int Goals => _goals;
        public int GoalsLeft => _goals - _woke;
        public bool IsFinished => GoalsLeft == 0;

        // ------------------------------------------------------------------ the light
        /// <summary>
        /// Which colour lights each cell, or -1.
        ///
        /// <para>
        /// A flood out of every lantern through gems of its own colour. Cached until the
        /// arrangement changes, because the search asks for it several times per position and
        /// it is by a wide margin the dearest thing here.
        /// </para>
        /// </summary>
        public int[] Veins()
        {
            if (_veins != null) return _veins;

            var lit = new int[_cells.Length];
            for (int i = 0; i < lit.Length; i++) lit[i] = -1;

            for (int cell = 0; cell < _cells.Length; cell++)
            {
                if (!PrismLayout.IsLamp(_cells[cell])) continue;

                int hue = PrismLayout.HueOf(_cells[cell]);

                _flood.Clear();

                _layout.Around(cell, _around);
                for (int i = 0; i < _around.Count; i++)
                {
                    int nb = _around[i];
                    if (lit[nb] >= 0 || !PrismLayout.IsGem(_cells[nb])) continue;
                    if (PrismLayout.HueOf(_cells[nb]) != hue) continue;

                    lit[nb] = hue;
                    _flood.Add(nb);
                }

                while (_flood.Count > 0)
                {
                    int at = _flood[_flood.Count - 1];
                    _flood.RemoveAt(_flood.Count - 1);

                    _layout.Around(at, _around);
                    for (int i = 0; i < _around.Count; i++)
                    {
                        int nb = _around[i];
                        if (lit[nb] >= 0 || !PrismLayout.IsGem(_cells[nb])) continue;
                        if (PrismLayout.HueOf(_cells[nb]) != hue) continue;

                        lit[nb] = hue;
                        _flood.Add(nb);
                    }
                }
            }

            _veins = lit;
            return lit;
        }

        /// <summary>The colour lighting this cell, or -1.</summary>
        public int Vein(int cell)
            => cell < 0 || cell >= _cells.Length ? -1 : Veins()[cell];

        /// <summary>Whether a lit gem is standing beside this cell.</summary>
        public bool Touched(int cell)
        {
            var lit = Veins();

            _layout.Around(cell, _around);
            for (int i = 0; i < _around.Count; i++)
                if (lit[_around[i]] >= 0) return true;

            return false;
        }

        /// <summary>How many gems this board is dealt with already lit. Invariant 5g, counted.</summary>
        public int DealtLit
        {
            get
            {
                var lit = Veins();
                int n = 0;
                for (int i = 0; i < lit.Length; i++) if (lit[i] >= 0) n++;
                return n;
            }
        }

        /// <summary>
        /// Whether a critter is already standing against light before anybody has played.
        ///
        /// A board dealt with a vein already reaching a sleeper is a board whose first move its
        /// author played — Budburst's "authored settled" rule, and it matters here because the
        /// goal count the player is graded against would already have moved.
        /// </summary>
        public bool Stirred
        {
            get
            {
                for (int cell = 0; cell < _cells.Length; cell++)
                    if (_cells[cell] == PrismLayout.Asleep && Touched(cell)) return true;

                return false;
            }
        }

        // ------------------------------------------------------------------ what may be played
        /// <summary>
        /// Whether these two cells may be exchanged.
        ///
        /// <b>Two gems of different colours.</b> Two alike is a move that changes nothing, and a
        /// move that changes nothing must answer null or the search never leaves the layer it is
        /// on (<see cref="ProtoPosition.Play"/>'s rule) — and the player is never charged for it
        /// either.
        /// </summary>
        public bool CanSwap(int a, int b)
        {
            int n = _cells.Length;
            if (a < 0 || b < 0 || a >= n || b >= n || a == b) return false;

            char here = _cells[a], there = _cells[b];
            return PrismLayout.IsGem(here) && PrismLayout.IsGem(there) && here != there;
        }

        /// <summary>Whether these two cells are a pair the layout offers at all.</summary>
        public bool Touching(int a, int b)
        {
            var swaps = _layout.Swaps;
            for (int i = 0; i < swaps.Length; i += 2)
            {
                if (swaps[i] == a && swaps[i + 1] == b) return true;
                if (swaps[i] == b && swaps[i + 1] == a) return true;
            }
            return false;
        }

        /// <summary>Every swap available, in the layout's own stable order.</summary>
        public void Moves(List<PrismMove> into)
        {
            into.Clear();

            var swaps = _layout.Swaps;
            for (int i = 0; i < swaps.Length; i += 2)
                if (CanSwap(swaps[i], swaps[i + 1]))
                    into.Add(new PrismMove(swaps[i], swaps[i + 1]));
        }

        public bool AnyMove
        {
            get
            {
                var swaps = _layout.Swaps;
                for (int i = 0; i < swaps.Length; i += 2)
                    if (CanSwap(swaps[i], swaps[i + 1])) return true;

                return false;
            }
        }

        /// <summary>
        /// Whether this board can be <em>proved</em> never to finish.
        ///
        /// A certainty and never a guess (invariant 28f), so it under-reports: it asks only the
        /// two questions no arrangement of the gems could ever answer differently, and those are
        /// facts about the layout worked out once. In practice a shipped board answers false,
        /// because the validator refuses one that does not — which is the honest state of
        /// affairs for a mode where nothing is ever consumed.
        /// </summary>
        public bool Stranded
        {
            get
            {
                var stuck = _layout.Marooned;
                for (int i = 0; i < stuck.Length; i++)
                    if (_cells[stuck[i]] == PrismLayout.Asleep) return true;

                return false;
            }
        }

        // ------------------------------------------------------------------ playing a move
        /// <summary>
        /// Swaps two gems and resolves what the light then reaches.
        ///
        /// <para>
        /// <b>The order is contract.</b> The gems move; the veins are read again from the
        /// arrangement that leaves; and every critter is then asked <em>once</em> whether a lit
        /// gem is standing beside it. Asking as the flood runs would hand a critter to whichever
        /// lantern the loop reached first, which is exactly the reading order
        /// <c>FallBoard.Resolve</c> is built to avoid — and a divergence of that kind is one a
        /// second runtime cannot see, because both copies would still be internally consistent.
        /// </para>
        /// </summary>
        public PrismFlare Play(PrismMove move)
        {
            int a = move.From, b = move.To;
            if (!CanSwap(a, b)) return null;

            var was = Veins();
            var before = new bool[was.Length];
            for (int i = 0; i < was.Length; i++) before[i] = was[i] >= 0;

            char keep = _cells[a];
            _cells[a] = _cells[b];
            _cells[b] = keep;
            _veins = null;

            var now = Veins();

            var log = new PrismFlare { From = a, To = b };
            log.Deeds.Add(new PrismDeedRecord(PrismDeed.Swap, a, b, PrismLayout.HueOf(_cells[b]),
                                              _cells[b]));

            // Lit first and in flood order out of the lantern, so a view replaying this draws
            // the vein running outward rather than switching a set of cells on all at once.
            var order = Order(now, before);
            for (int i = 0; i < order.Count; i++)
            {
                int cell = order[i];
                log.Lit++;
                log.Deeds.Add(new PrismDeedRecord(PrismDeed.Lit, cell, cell, now[cell],
                                                  _cells[cell]));
            }

            for (int cell = 0; cell < now.Length; cell++)
            {
                if (!before[cell] || now[cell] >= 0) continue;

                log.Dark++;
                log.Deeds.Add(new PrismDeedRecord(PrismDeed.Dark, cell, cell, -1, _cells[cell]));
            }

            for (int cell = 0; cell < _cells.Length; cell++)
            {
                if (_cells[cell] != PrismLayout.Asleep || !Touched(cell)) continue;

                _cells[cell] = PrismLayout.Awake;
                _woke++;
                log.Woke++;
                log.Deeds.Add(new PrismDeedRecord(PrismDeed.Wake, cell, cell, WokeBy(cell, now),
                                                  PrismLayout.Asleep));
            }

            // A critter's cell was never a gem, so waking one moves no vein and nothing has to
            // be read again.
            return log;
        }

        /// <summary>
        /// Newly lit cells, outward from the lanterns that light them.
        ///
        /// The walk is over the whole vein rather than only its new part, because a vein's
        /// <em>order</em> is what a view animates along and that order starts at the lantern
        /// whatever was already lit. Cells that were lit before are walked through and not
        /// reported.
        /// </summary>
        List<int> Order(int[] now, bool[] before)
        {
            var out_ = new List<int>(16);
            var seen = new bool[now.Length];
            var queue = new List<int>(32);

            for (int cell = 0; cell < _cells.Length; cell++)
            {
                if (!PrismLayout.IsLamp(_cells[cell])) continue;

                int hue = PrismLayout.HueOf(_cells[cell]);

                queue.Clear();

                _layout.Around(cell, _around);
                for (int i = 0; i < _around.Count; i++)
                {
                    int nb = _around[i];
                    if (now[nb] != hue || seen[nb]) continue;

                    seen[nb] = true;
                    queue.Add(nb);
                }

                for (int head = 0; head < queue.Count; head++)
                {
                    int at = queue[head];
                    if (!before[at]) out_.Add(at);

                    _layout.Around(at, _around);
                    for (int i = 0; i < _around.Count; i++)
                    {
                        int nb = _around[i];
                        if (now[nb] != hue || seen[nb]) continue;

                        seen[nb] = true;
                        queue.Add(nb);
                    }
                }
            }

            return out_;
        }

        /// <summary>Which colour woke this critter, for the drawing. The first lit neighbour, in order.</summary>
        int WokeBy(int cell, int[] now)
        {
            _layout.Around(cell, _around);
            for (int i = 0; i < _around.Count; i++)
                if (now[_around[i]] >= 0) return now[_around[i]];

            return -1;
        }

        /// <summary>
        /// The cells and nothing else.
        ///
        /// Light is a pure function of the arrangement, so putting it in the key would be
        /// putting one fact in twice — and the woken critters are in it because they are the one
        /// thing a swap cannot undo.
        /// </summary>
        public void Write(List<byte> key)
        {
            for (int i = 0; i < _cells.Length; i++) key.Add((byte)_cells[i]);
        }

        /// <summary>
        /// The same key as a string, for a walk that is not the shared search.
        ///
        /// <c>PrismReading</c> keeps its own frontier and would otherwise have to build a whole
        /// <see cref="PrismFuture"/> — which re-enumerates every move — just to be handed to
        /// <c>ProtoKey</c>. Same bytes, said the other way.
        /// </summary>
        public string Key() => new string(_cells);
    }

    /// <summary>A Prismvale board as the search sees it.</summary>
    public sealed class PrismFuture : ProtoPosition
    {
        readonly PrismBoard _board;
        readonly List<PrismMove> _moves = new List<PrismMove>(64);

        public PrismFuture(PrismBoard board)
        {
            _board = board;
            _board.Moves(_moves);
        }

        public PrismBoard Board => _board;

        public override bool Won => _board.IsFinished;

        public override int MoveCount => _moves.Count;

        public override ProtoPosition Play(int move)
        {
            if (move < 0 || move >= _moves.Count) return null;

            var forked = _board.Fork();
            return forked.Play(_moves[move]) == null ? null : new PrismFuture(forked);
        }

        /// <summary>
        /// What a player who is not thinking would notice this swap is worth.
        ///
        /// Waking weighs a hundred times lighting, and light lost counts against — which is the
        /// honest greedy reading here, because the mistake this mode is made of is breaking one
        /// vein to build another.
        /// </summary>
        public override int Gain(int move)
        {
            if (move < 0 || move >= _moves.Count) return 0;

            var forked = _board.Fork();
            var log = forked.Play(_moves[move]);
            return log == null ? 0 : log.Woke * 100 + log.Lit - log.Dark;
        }

        public override void Write(List<byte> key) => _board.Write(key);
    }
}
