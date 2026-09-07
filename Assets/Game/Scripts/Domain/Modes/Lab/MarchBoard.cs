using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// Hollowmarch's board: a line of raider pods walking a haul-road to a portal, some of them
    /// carrying caged critters, and what one core fired into it does to the rest.
    ///
    /// <para>
    /// <b>The verb is a wedge, and the whole design rests on what happens when the gap closes.</b>
    /// A core fired into the line wedges in beside its own colour; three alike go off; the line
    /// slides shut behind them — and if the closure brings three more together, that goes off
    /// too, and again, each wave louder than the last. That is the genre's own engine, and it is
    /// safe here for the reason invariant 20j's third test asks for: the spread has to clear the
    /// same threshold of <see cref="MarchLayout.BurstAt"/> again on every wave, so a chain dies
    /// wherever the line is not already nearly right. A cascade that spread unconditionally
    /// would be a solvent.
    /// </para>
    /// <para>
    /// <b>Every legal shot advances the magazine by exactly one, so the state graph is layered
    /// by construction.</b> That is invariant 20j's second test and it is passed more cleanly
    /// here than by any mode before it: depth <em>is</em> cores spent, a run always ends, the
    /// board cannot stall, and <see cref="ProtoSearch"/> finds par by breadth-first walk with
    /// nothing to prove about termination. Note where the monotone quantity lives — it is not
    /// the pods, which a dumped core adds to; it is the magazine, which only ever empties.
    /// </para>
    /// <para>
    /// <b>The march is the allowance made visible.</b> Every core spent is a step the raiders
    /// take toward the gate, so how much time is left is something the player reads off the
    /// board rather than out of a corner. A plain pod that reaches the gate goes through it and
    /// is gone — a real cost, because it was match material — and a pod carrying a critter
    /// <em>jams</em>, because the raiders will not abandon their cargo. That second half is a
    /// rule rather than a kindness: letting a cage through would make a board that can be
    /// neither won nor lost, which is the one state invariant 20g says a mode may never ship.
    /// </para>
    /// <para>
    /// It is the tenth mode the prototype level shape has carried, and like every one before it
    /// it cost the save file no schema version, no merge rule, no <c>firestore.rules</c> change
    /// and no server work (invariant 20a).
    /// </para>
    /// </summary>
    public sealed class MarchLayout
    {
        // ------------------------------------------------------------------ the vocabulary
        /// <summary>Open ground beside the road. Scenery, and nothing ever stands on it.</summary>
        public const char Ground = '.';

        /// <summary>
        /// Rubble. Scenery with no rule at all, and deliberately so — a board is read by looking
        /// at it, so the one thing here that exists purely to be looked at is the one thing that
        /// changes nothing. <c>QuarryLayout.Pad</c>'s argument, kept.
        /// </summary>
        public const char Rubble = '#';

        /// <summary>Bare haul-road: a slot the line will walk over but nothing is standing on yet.</summary>
        public const char Rail = '+';

        /// <summary>The gate the raiders are walking to. Exactly one, at one end of the road.</summary>
        public const char Portal = 'Z';

        /// <summary>Where you are standing with the launcher. Exactly one, at the other end.</summary>
        public const char Launcher = 'A';

        /// <summary>
        /// The colours a pod may wear. <b>Four, and the fourth is what stops a board reading
        /// itself.</b> Three colours across a line of eighteen makes a run of three almost
        /// unavoidable, so the line would keep going off without being aimed at — which is 20j's
        /// solvent arriving through the front door rather than through the cascade.
        /// </summary>
        public const string Colours = "RGBY";

        /// <summary>
        /// The same four, carrying a caged critter. Lower case throughout, so a board reads as a
        /// line of colours with the cargo picked out rather than as two alphabets — and so a
        /// cage keeps the colour of the pod under it, which is what makes it a target a core can
        /// actually be aimed at rather than a separate kind of thing.
        /// </summary>
        public const string Caged = "rgby";

        /// <summary>
        /// A hauler. It wears no colour, so a run of pods can never span one — which is what it
        /// is <em>for</em>: it cuts the line into pieces and decides which matches exist at all.
        /// One blast beside it is enough.
        /// </summary>
        public const char Hauler = 'H';

        /// <summary>A warden: a hauler under plating. Two blasts beside it, or one lance.</summary>
        public const char Warden = 'W';

        /// <summary>
        /// A warden with its plating gone.
        ///
        /// <b>Not authorable, and that is the point.</b> It is a state a board reaches and never
        /// one it is written in, exactly as a blend is in Budburst. It still has to be in the
        /// key, or two boards a plate apart would merge in the search and par would come out
        /// short — which is the class of fault that looks exactly like a level somebody authored.
        /// </summary>
        public const char WardenHit = 'V';

        /// <summary>Every character a level of this mode may be written in.</summary>
        public const string Letters = ".#+ZA" + Colours + Caged + "HW";

        /// <summary>Pods alike that go off, and the threshold every wave of a chain must clear again.</summary>
        public const int BurstAt = 3;

        /// <summary>
        /// Pods a single shot has to destroy to forge a Spark into the next core.
        ///
        /// <b>Counted over the whole shot rather than over one run</b>, which is the difference
        /// between rewarding a lucky run of five and rewarding a chain the player engineered.
        /// The Spark is the only thing in this mode the player <em>makes</em> (invariant 20m), so
        /// what it costs is deliberately the thing a good move was already aiming at.
        /// </summary>
        public const int ForgeAt = 5;

        static readonly int[] StepX = { 0, 1, 0, -1 };
        static readonly int[] StepY = { -1, 0, 1, 0 };

        // ------------------------------------------------------------------ reading a letter
        public static bool IsColour(char c) => Colours.IndexOf(c) >= 0 || Caged.IndexOf(c) >= 0;

        /// <summary>The colour a pod wears, upper case, or <c>'\0'</c> for something that wears none.</summary>
        public static char Hue(char c)
        {
            if (Colours.IndexOf(c) >= 0) return c;
            int caged = Caged.IndexOf(c);
            return caged >= 0 ? Colours[caged] : '\0';
        }

        public static bool IsCaged(char c) => Caged.IndexOf(c) >= 0;

        public static bool IsRaider(char c) => c == Hauler || c == Warden || c == WardenHit;

        /// <summary>What the board counts down: every caged critter and every raider hauling one.</summary>
        public static bool IsGoal(char c) => IsCaged(c) || IsRaider(c);

        /// <summary>Anything that stands on the road, so anything the line can be made of.</summary>
        public static bool IsPod(char c) => IsColour(c) || IsRaider(c);

        /// <summary>Any road cell: the rail, its two ends, and anything standing on it.</summary>
        public static bool IsRoad(char c)
            => c == Rail || c == Portal || c == Launcher || IsPod(c);

        /// <summary>A raider one plate down, or <c>'\0'</c> when that plate was its last.</summary>
        public static char Struck(char c) => c == Warden ? WardenHit : '\0';

        // ------------------------------------------------------------------ the road
        public readonly ProtoGrid Grid;
        public readonly int Spare;

        /// <summary>The magazine, in order. It repeats, so one lap is enough to author.</summary>
        public readonly string Cores;

        /// <summary>The road from the gate to the launcher, as grid cells.</summary>
        public readonly int[] Path;

        /// <summary>
        /// The slots a pod may stand on: the road less its two ends. The portal is a mouth and
        /// the launcher is where you are standing, and neither is a place anything waits.
        /// </summary>
        public readonly int[] Slots;

        /// <summary>The line as authored, front first.</summary>
        public readonly char[] Line;

        /// <summary>Which slot the front of the line was authored on.</summary>
        public readonly int Head;

        /// <summary>What went wrong with the road, or null. Read before anything else is asked.</summary>
        public readonly string Fault;

        public int Width => Grid.Width;
        public int Height => Grid.Height;
        public int Track => Slots == null ? 0 : Slots.Length;

        public MarchLayout(ProtoGrid grid, int spare, string cores)
        {
            Grid = grid;
            Spare = spare;
            Cores = Clean(cores);

            Path = Walk(grid, out string fault);
            Fault = fault;

            if (Path == null)
            {
                Slots = new int[0];
                Line = new char[0];
                Head = 0;
                return;
            }

            Slots = new int[Path.Length - 2];
            for (int i = 0; i < Slots.Length; i++) Slots[i] = Path[i + 1];

            var line = new List<char>(Slots.Length);
            int head = -1;

            for (int at = 0; at < Slots.Length; at++)
            {
                char c = grid.At(Slots[at]);
                if (!IsPod(c)) continue;
                if (head < 0) head = at;
                line.Add(c);
            }

            Line = line.ToArray();
            Head = head < 0 ? 0 : head;
        }

        /// <summary>
        /// The magazine, with anything that is not a colour dropped and an empty one filled in.
        ///
        /// Forgiving here and strict in the validator, which is <c>ContentMapper.ReadStory</c>'s
        /// asymmetry and safe for the same reason: a player's build must open the board it was
        /// given rather than refuse it, and the build gate is what stops a wrong one shipping.
        /// </summary>
        static string Clean(string cores)
        {
            if (string.IsNullOrEmpty(cores)) return Colours.Substring(0, 3);

            var kept = new System.Text.StringBuilder(cores.Length);
            for (int i = 0; i < cores.Length; i++)
                if (Colours.IndexOf(cores[i]) >= 0) kept.Append(cores[i]);

            return kept.Length > 0 ? kept.ToString() : Colours.Substring(0, 3);
        }

        /// <summary>
        /// Walks the road from the gate to the launcher, or says which cell makes that ambiguous.
        ///
        /// <para>
        /// <b>The order is derived rather than authored twice.</b> A level draws a winding rail
        /// through the grid and the walk reads the order off it, which is the only shape where
        /// what is drawn and what is played cannot come apart — invariant 4a's argument about
        /// the manifest, applied to a board. It costs exactly one rule: every road cell has two
        /// road neighbours except the two ends, which have one. A road that forks is then
        /// refused <em>by cell</em> rather than read some arbitrary way, which is
        /// <see cref="ProtoGrid"/>'s rule about naming the row and the column.
        /// </para>
        /// </summary>
        static int[] Walk(ProtoGrid grid, out string fault)
        {
            fault = null;

            int mouth = -1, pad = -1, mouths = 0, pads = 0;

            for (int i = 0; i < grid.Count; i++)
            {
                char c = grid.At(i);
                if (c == Portal) { mouth = i; mouths++; }
                else if (c == Launcher) { pad = i; pads++; }
            }

            if (mouths != 1)
            {
                fault = $"a haul-road has exactly one portal '{Portal}'; this one draws {mouths}";
                return null;
            }

            if (pads != 1)
            {
                fault = $"a haul-road has exactly one launcher '{Launcher}'; this one draws {pads}";
                return null;
            }

            for (int i = 0; i < grid.Count; i++)
            {
                char c = grid.At(i);
                if (!IsRoad(c)) continue;

                int want = c == Portal || c == Launcher ? 1 : 2;
                int got = Neighbours(grid, i, null);

                if (got == want) continue;

                fault = $"the road cell at {grid.XOf(i)},{grid.YOf(i)} has {got} road " +
                        $"neighbour(s) and needs {want}, so the walk from the portal is " +
                        "ambiguous - a haul-road is one unbranched line";
                return null;
            }

            var path = new List<int>(grid.Count) { mouth };
            var seen = new bool[grid.Count];
            seen[mouth] = true;

            var step = new List<int>(4);
            while (true)
            {
                step.Clear();
                Neighbours(grid, path[path.Count - 1], step);

                int next = -1;
                for (int i = 0; i < step.Count; i++)
                    if (!seen[step[i]]) { next = step[i]; break; }

                if (next < 0) break;

                path.Add(next);
                seen[next] = true;
            }

            if (path[path.Count - 1] != pad)
            {
                fault = "the road from the portal does not reach the launcher, so the board " +
                        "draws two roads rather than one";
                return null;
            }

            if (path.Count < 5)
            {
                fault = $"a haul-road of {path.Count} cells is too short to march anything along";
                return null;
            }

            return path.ToArray();
        }

        static int Neighbours(ProtoGrid grid, int cell, List<int> into)
        {
            int x = grid.XOf(cell), y = grid.YOf(cell), n = 0;

            for (int way = 0; way < 4; way++)
            {
                int nx = x + StepX[way], ny = y + StepY[way];
                if (!grid.Inside(nx, ny) || !IsRoad(grid.At(nx, ny))) continue;

                n++;
                into?.Add(grid.Index(nx, ny));
            }

            return n;
        }

        /// <summary>Where on the grid a track slot is, or -1.</summary>
        public int CellOf(int slot)
            => slot >= 0 && slot < Track ? Slots[slot] : -1;
    }

    // ---------------------------------------------------------------------------- one shot

    /// <summary>What happened, in the order it happened, so the view can replay it exactly.</summary>
    public enum MarchDeed
    {
        /// <summary>A core wedged into the line. <c>At</c> is the slot it landed on.</summary>
        Wedge = 0,

        /// <summary>A plain pod went off.</summary>
        Burst = 1,

        /// <summary>A cage broke open and a critter is out.</summary>
        Free = 2,

        /// <summary>A raider came apart.</summary>
        Scrap = 3,

        /// <summary>A warden took a blast and held. The one refusal this board shows for itself.</summary>
        Stagger = 4,

        /// <summary>A Spark cut the line. <c>At</c> and <c>Span</c> are the first and last slots.</summary>
        Lance = 5,

        /// <summary>The gap closed and the line slid forward. <c>Span</c> is how many pods left.</summary>
        Close = 6,

        /// <summary>A plain pod reached the gate and went through it.</summary>
        Escape = 7,

        /// <summary>A cage reached the gate. The raiders will not abandon it, so the line stops.</summary>
        Jam = 8,
    }

    /// <summary>
    /// One thing that happened, placed on the road rather than in the line.
    ///
    /// <b><c>At</c> is a track slot and never a position in the line</b>, which is invariant
    /// 30i's rule obeyed in advance: the line shifts under its own indices as pods leave it, so
    /// a record written in line positions would name a different pod by the time it was drawn.
    /// A slot is a fact about the road and cannot move.
    /// </summary>
    public readonly struct MarchDeedRecord
    {
        public readonly MarchDeed Deed;

        /// <summary>The track slot it happened on.</summary>
        public readonly int At;

        /// <summary>A lance's far end, or a close's count. 0 where there is none.</summary>
        public readonly int Span;

        /// <summary>What was standing there, for anything already removed.</summary>
        public readonly char What;

        /// <summary>Which wave of the chain this belongs to. One is the shot itself.</summary>
        public readonly int Wave;

        public MarchDeedRecord(MarchDeed deed, int at, int span, char what, int wave)
        {
            Deed = deed;
            At = at;
            Span = span;
            What = what;
            Wave = wave;
        }
    }

    /// <summary>
    /// The line as it stood at the end of one wave.
    ///
    /// <b>Snapshots rather than a reconstruction</b>, because the view has to animate the slide
    /// and the slide is the mode's signature. Working out where every pod ended up by replaying
    /// removals against shifting indices is exactly the arithmetic invariant 30i names as the
    /// one class of fault nothing else in this project can see — par, <c>ways</c>, both
    /// validators and every content gate read the model, and the model is right the whole time.
    /// A few dozen characters per wave is not worth being clever about.
    /// </summary>
    public readonly struct MarchFrame
    {
        public readonly char[] Line;
        public readonly int Head;

        public MarchFrame(char[] line, int head)
        {
            Line = line;
            Head = head;
        }
    }

    /// <summary>What one core did: every deed in order, how many waves it ran and what it was worth.</summary>
    public sealed class MarchShot
    {
        public readonly List<MarchDeedRecord> Deeds = new List<MarchDeedRecord>(32);

        /// <summary>The line after the wedge and after every wave, for the view to slide between.</summary>
        public readonly List<MarchFrame> Frames = new List<MarchFrame>(6);

        /// <summary>How many waves the chain ran for. One is a single burst and nothing further.</summary>
        public int Waves { get; internal set; }

        /// <summary>Pods destroyed. What <see cref="MarchLayout.ForgeAt"/> is read against.</summary>
        public int Took { get; internal set; }

        /// <summary>Cages opened, which is what the story's <c>Freed</c> cue counts.</summary>
        public int Freed { get; internal set; }

        /// <summary>Raiders scrapped, which is what the story's <c>Kill</c> cue counts.</summary>
        public int Scrapped { get; internal set; }

        /// <summary>Raiders that lost a plate and stood. The board answering for itself.</summary>
        public int Staggered { get; internal set; }

        /// <summary>Plain pods that reached the gate and went through it.</summary>
        public int Escaped { get; internal set; }

        /// <summary>Whether this shot forged a Spark. The story's <c>Forged</c> cue.</summary>
        public bool Forged { get; internal set; }

        /// <summary>Whether this shot spent one.</summary>
        public bool Lanced { get; internal set; }

        /// <summary>Whether the line reached the gate and stopped. The story's <c>Tight</c> cue.</summary>
        public bool Jammed { get; internal set; }

        /// <summary>Cages opened and raiders scrapped. The move's worth, and the flourish readout.</summary>
        public int Goals => Freed + Scrapped;
    }

    /// <summary>How a core may be spent. Three kinds, and the third is the one that costs nothing else.</summary>
    public enum MarchAim
    {
        /// <summary>Wedge the core in beside a run of its own colour.</summary>
        Join = 0,

        /// <summary>Spend a Spark on a run: it and the group either side of it, plating and all.</summary>
        Lance = 1,

        /// <summary>Drop the core at the back of the line. Always available, and always a choice.</summary>
        Dump = 2,
    }

    /// <summary>One shot the player could take, named the way the board would name it.</summary>
    public readonly struct MarchMove
    {
        public readonly MarchAim Aim;

        /// <summary>Which position in the line it is aimed at. The end of the line, for a dump.</summary>
        public readonly int At;

        public MarchMove(MarchAim aim, int at)
        {
            Aim = aim;
            At = at;
        }
    }

    // ---------------------------------------------------------------------------- the board

    /// <summary>
    /// The line, where its front has got to, and what is still in the magazine. See
    /// <see cref="MarchLayout"/> for what the mode is.
    /// </summary>
    public sealed class MarchBoard : IProtoBoard
    {
        readonly MarchLayout _layout;
        readonly List<char> _line;

        int _head;
        int _index;
        bool _spark;
        int _freed, _scrapped;

        MarchBoard(MarchLayout layout, List<char> line, int head, int index, bool spark,
                   int freed, int scrapped)
        {
            _layout = layout;
            _line = line;
            _head = head;
            _index = index;
            _spark = spark;
            _freed = freed;
            _scrapped = scrapped;
        }

        public static MarchBoard Build(MarchLayout layout)
            => new MarchBoard(layout, new List<char>(layout.Line), layout.Head, 0, false, 0, 0);

        public MarchBoard Fork()
            => new MarchBoard(_layout, new List<char>(_line), _head, _index, _spark,
                              _freed, _scrapped);

        public MarchLayout Layout => _layout;

        /// <summary>The line, front first. Read-only — every change goes through <see cref="Fire"/>.</summary>
        public IReadOnlyList<char> Line => _line;

        /// <summary>Which slot the front of the line is standing on.</summary>
        public int Head => _head;

        /// <summary>Whether the next core is a Spark.</summary>
        public bool Spark => _spark;

        /// <summary>How many cores have been fired. The magazine only ever empties.</summary>
        public int Index => _index;

        public int Track => _layout.Track;

        /// <summary>The first slot past the back of the line.</summary>
        public int Tail => _head + _line.Count;

        /// <summary>Whether a core that will not go off has anywhere to sit.</summary>
        public bool Room => Tail < Track;

        /// <summary>The colour in hand. The magazine repeats, so one lap is enough to author.</summary>
        public char Core
        {
            get
            {
                string cores = _layout.Cores;
                return cores.Length == 0 ? 'R' : cores[_index % cores.Length];
            }
        }

        /// <summary>Shots before the front of the line reaches the gate.</summary>
        public int Runway => _head;

        /// <summary>Whether the front of the line has reached the gate and cannot go further.</summary>
        public bool Jammed
            => _head == 0 && _line.Count > 0 && MarchLayout.IsGoal(_line[0]);

        /// <summary>Where a position in the line is standing, as a track slot.</summary>
        public int SlotOf(int at) => _head + at;

        // ------------------------------------------------------------------ the goal
        public int Goals
        {
            get
            {
                int n = 0;
                var line = _layout.Line;
                for (int i = 0; i < line.Length; i++) if (MarchLayout.IsGoal(line[i])) n++;
                return n;
            }
        }

        public int GoalsLeft => Goals - _freed - _scrapped;

        public bool IsFinished => GoalsLeft == 0;

        public bool AnyMove
        {
            get
            {
                var moves = new List<MarchMove>(8);
                Moves(moves);
                return moves.Count > 0;
            }
        }

        /// <summary>
        /// <b>Always false here, and the jam is why.</b>
        ///
        /// <para>
        /// Every other prototype mode can reach a board it can be proved never to finish from,
        /// which is what this predicate is for: it decides whether it would be honest to sell a
        /// continue (invariant 28f). This mode cannot reach one, by construction — the raiders
        /// will not abandon their cargo, so a caged critter that reaches the gate jams the line
        /// rather than going through it, and every goal a board opened with is still standing on
        /// it however long the run goes on. More cores therefore always help, which is exactly
        /// what a deficit of nought says.
        /// </para>
        /// <para>
        /// It is written out rather than left to a default, because the reason is a rule and not
        /// an oversight — and because the alternative was tried first. Letting a cage through the
        /// gate makes a board that can be neither won nor lost, and that is the one state
        /// invariant 20g says a mode may never ship.
        /// </para>
        /// </summary>
        public bool Stranded => false;

        // ------------------------------------------------------------------ the moves
        /// <summary>
        /// Every maximal block of one colour in the line, appended as (at, length).
        ///
        /// A raider wears no colour, so it is never inside a run — which is what makes it a
        /// divider rather than an obstacle, and the whole of what one is for.
        /// </summary>
        public void Runs(List<int> into)
        {
            into.Clear();

            int at = 0;
            while (at < _line.Count)
            {
                char hue = MarchLayout.Hue(_line[at]);
                if (hue == '\0') { at++; continue; }

                int end = at;
                while (end + 1 < _line.Count && MarchLayout.Hue(_line[end + 1]) == hue) end++;

                into.Add(at);
                into.Add(end - at + 1);
                at = end + 1;
            }
        }

        /// <summary>
        /// Every legal shot.
        ///
        /// <para>
        /// <b>A core wedges in beside its own colour, or it is dumped at the back.</b> Restricting
        /// it to the runs it matches is what keeps the branching small enough for the shared
        /// breadth-first search to find par at the depths this mode is authored to — and it is
        /// the right rule for a thumb as well, because it is exactly the set of shots a player
        /// can <em>see</em>. Every position inside one run reaches the same board, so a run is
        /// one move and not several.
        /// </para>
        /// <para>
        /// <b>The dump is a real move and not an escape hatch.</b> Dropping a core at the back
        /// changes the colour in hand and leaves a pod where a later one can complete it, which
        /// is how a player sets up the chain that the whole mode is about. It is also what makes
        /// a board with nothing to match still playable, so <see cref="AnyMove"/> is answered by
        /// the road being full rather than by the colours being awkward.
        /// </para>
        /// </summary>
        public void Moves(List<MarchMove> into)
        {
            into.Clear();

            var runs = new List<int>(16);
            Runs(runs);

            if (_spark)
            {
                for (int i = 0; i < runs.Count; i += 2)
                    into.Add(new MarchMove(MarchAim.Lance, runs[i]));
            }
            else
            {
                char hue = Core;
                for (int i = 0; i < runs.Count; i += 2)
                {
                    int at = runs[i], length = runs[i + 1];
                    if (MarchLayout.Hue(_line[at]) != hue) continue;

                    // A core that will not go off has to have somewhere to sit; one that will is
                    // taking two pods off the line and always fits.
                    if (length + 1 < MarchLayout.BurstAt && !Room) continue;

                    into.Add(new MarchMove(MarchAim.Join, at));
                }
            }

            if (Room) into.Add(new MarchMove(MarchAim.Dump, _line.Count));
        }

        // ------------------------------------------------------------------ firing
        /// <summary>
        /// Plays one shot, or null when it is not legal. Mutates — <see cref="Fork"/> to look ahead.
        /// </summary>
        public MarchShot Fire(MarchMove move)
        {
            var log = new MarchShot();

            switch (move.Aim)
            {
                case MarchAim.Lance:
                {
                    if (!_spark || move.At < 0 || move.At >= _line.Count) return null;

                    _spark = false;
                    _index++;
                    log.Lanced = true;

                    // The opening frame, so the view has the same first snapshot however the
                    // core was spent - a lance does not land anywhere, so nothing changed yet.
                    Snapshot(log);
                    Lance(move.At, log);
                    break;
                }

                case MarchAim.Join:
                {
                    char hue = Core;
                    if (move.At < 0 || move.At >= _line.Count) return null;
                    if (MarchLayout.Hue(_line[move.At]) != hue) return null;

                    int length = 1;
                    while (move.At + length < _line.Count &&
                           MarchLayout.Hue(_line[move.At + length]) == hue) length++;

                    if (length + 1 < MarchLayout.BurstAt && !Room) return null;

                    _index++;
                    _line.Insert(move.At, hue);
                    log.Deeds.Add(new MarchDeedRecord(MarchDeed.Wedge, SlotOf(move.At), 0,
                                                      hue, 0));
                    Snapshot(log);
                    Settle(log, 1);
                    break;
                }

                case MarchAim.Dump:
                {
                    if (!Room) return null;

                    char hue = Core;
                    _index++;
                    _line.Add(hue);
                    log.Deeds.Add(new MarchDeedRecord(MarchDeed.Wedge, SlotOf(_line.Count - 1),
                                                      0, hue, 0));
                    Snapshot(log);
                    Settle(log, 1);
                    break;
                }

                default:
                    return null;
            }

            if (log.Took >= MarchLayout.ForgeAt && !_spark)
            {
                _spark = true;
                log.Forged = true;
            }

            March(log);
            return log;
        }

        /// <summary>
        /// The Spark: the run it lands in and the group either side of it, whatever they are.
        ///
        /// <para>
        /// <b>A lance cuts plating</b>, which is the one thing a colour match cannot do to a
        /// warden in a single shot — so the core the player forges is not a bigger version of an
        /// ordinary one, it does a different job. That is invariant 26g's test asked of the thing
        /// the player <em>makes</em>: a mirror that only ever did what a lens did was withdrawn
        /// for competing on degree rather than on kind, and the same question has to be asked of
        /// a special before it is animated.
        /// </para>
        /// </summary>
        void Lance(int at, MarchShot log)
        {
            char hue = MarchLayout.Hue(_line[at]);

            int lo = at, hi = at;
            while (lo > 0 && MarchLayout.Hue(_line[lo - 1]) == hue) lo--;
            while (hi + 1 < _line.Count && MarchLayout.Hue(_line[hi + 1]) == hue) hi++;

            // One group on each side, whatever it is: a run of another colour, or a lone raider.
            if (lo > 0)
            {
                char left = MarchLayout.Hue(_line[lo - 1]);
                lo--;
                while (lo > 0 && left != '\0' && MarchLayout.Hue(_line[lo - 1]) == left) lo--;
            }

            if (hi + 1 < _line.Count)
            {
                char right = MarchLayout.Hue(_line[hi + 1]);
                hi++;
                while (hi + 1 < _line.Count && right != '\0' &&
                       MarchLayout.Hue(_line[hi + 1]) == right) hi++;
            }

            log.Deeds.Add(new MarchDeedRecord(MarchDeed.Lance, SlotOf(lo), SlotOf(hi), '\0', 1));

            var going = new List<int>(hi - lo + 1);
            for (int i = lo; i <= hi; i++) going.Add(i);

            Blow(going, log, 1, true);
            Settle(log, 2);
        }

        /// <summary>
        /// Wave after wave, until nothing is three alike any more.
        ///
        /// <para>
        /// <b>Every wave is read off the line as it stands and only then acted on</b>, so nothing
        /// here depends on which end the loop started from. That is the rule invariant 26h asks
        /// of any resolution a second runtime has to reproduce exactly, and it is why every run
        /// of three or more goes at once rather than the loop taking the first it finds: two
        /// orderings of the same wave would be two different boards, and only one of them would
        /// be in the vectors.
        /// </para>
        /// </summary>
        void Settle(MarchShot log, int beat)
        {
            var runs = new List<int>(16);
            var going = new List<int>(24);

            int guard = 0;
            while (guard++ <= Track * 2)
            {
                Runs(runs);

                going.Clear();
                for (int i = 0; i < runs.Count; i += 2)
                {
                    if (runs[i + 1] < MarchLayout.BurstAt) continue;
                    for (int k = 0; k < runs[i + 1]; k++) going.Add(runs[i] + k);
                }

                if (going.Count == 0) break;

                Blow(going, log, beat, false);
                beat++;
            }

            log.Waves = beat - 1;
        }

        /// <summary>Takes a set of pods off the line, cracks what is beside them, and closes the gap.</summary>
        void Blow(List<int> going, MarchShot log, int beat, bool cuts)
        {
            if (going.Count == 0) return;

            var gone = new HashSet<int>(going);

            // Read every consequence off the line as it stands before anything is removed, so a
            // raider beside two bursting runs is cracked once rather than twice.
            var touched = new List<int>(4);
            for (int k = 0; k < going.Count; k++)
            {
                int i = going[k];
                for (int d = -1; d <= 1; d += 2)
                {
                    int j = i + d;
                    if (j < 0 || j >= _line.Count) continue;
                    if (gone.Contains(j) || touched.Contains(j)) continue;
                    if (MarchLayout.IsRaider(_line[j])) touched.Add(j);
                }
            }

            for (int k = 0; k < going.Count; k++)
            {
                int i = going[k];
                char c = _line[i];

                if (MarchLayout.IsCaged(c))
                {
                    _freed++;
                    log.Freed++;
                    log.Deeds.Add(new MarchDeedRecord(MarchDeed.Free, SlotOf(i), 0, c, beat));
                }
                else if (MarchLayout.IsRaider(c))
                {
                    // Only a lance ever puts a raider inside the blast: a colour run never holds
                    // one, so this is the Spark cutting straight through plating.
                    _scrapped++;
                    log.Scrapped++;
                    log.Deeds.Add(new MarchDeedRecord(MarchDeed.Scrap, SlotOf(i), 0, c, beat));
                }
                else
                {
                    log.Deeds.Add(new MarchDeedRecord(MarchDeed.Burst, SlotOf(i), 0, c, beat));
                }

                log.Took++;
            }

            touched.Sort();
            for (int k = 0; k < touched.Count; k++)
            {
                int i = touched[k];
                char c = _line[i];
                char left = MarchLayout.Struck(c);

                if (left == '\0' || cuts)
                {
                    _scrapped++;
                    log.Scrapped++;
                    log.Deeds.Add(new MarchDeedRecord(MarchDeed.Scrap, SlotOf(i), 0, c, beat));
                    gone.Add(i);
                    log.Took++;
                }
                else
                {
                    _line[i] = left;
                    log.Staggered++;
                    log.Deeds.Add(new MarchDeedRecord(MarchDeed.Stagger, SlotOf(i), 0, c, beat));
                }
            }

            var kept = new List<char>(_line.Count);
            for (int i = 0; i < _line.Count; i++) if (!gone.Contains(i)) kept.Add(_line[i]);

            _line.Clear();
            _line.AddRange(kept);

            log.Deeds.Add(new MarchDeedRecord(MarchDeed.Close, _head, gone.Count, '\0', beat));
            Snapshot(log);
        }

        /// <summary>
        /// One step nearer the gate, and whatever that costs.
        ///
        /// <para>
        /// <b>A plain pod goes through the gate and is gone; a pod carrying a critter jams.</b>
        /// Both halves matter. The first is a real cost with no fail state attached — the line
        /// bleeds the very material a core needs to match, so a player who ignores the front of
        /// it finds their options narrowing. The second is what keeps every board winnable for
        /// as long as there are cores to fire, which is what <see cref="Stranded"/> rests on.
        /// </para>
        /// </summary>
        void March(MarchShot log)
        {
            if (_line.Count == 0)
            {
                if (_head > 0) _head--;
                Snapshot(log);
                return;
            }

            if (Jammed)
            {
                log.Jammed = true;
                log.Deeds.Add(new MarchDeedRecord(MarchDeed.Jam, 0, 0, _line[0],
                                                  log.Waves + 1));
                Snapshot(log);
                return;
            }

            if (_head > 0)
            {
                _head--;
                Snapshot(log);
                return;
            }

            char through = _line[0];
            _line.RemoveAt(0);
            log.Escaped++;
            log.Deeds.Add(new MarchDeedRecord(MarchDeed.Escape, 0, 0, through, log.Waves + 1));
            Snapshot(log);
        }

        void Snapshot(MarchShot log) => log.Frames.Add(new MarchFrame(_line.ToArray(), _head));

        // ------------------------------------------------------------------ the key
        /// <summary>
        /// Everything a rule reads and nothing else.
        ///
        /// The magazine's position is left out deliberately: the search walks in layers and one
        /// shot is one layer, so a core index would only stop two identical boards merging.
        /// <see cref="Head"/> is carried because the jam means it is <em>not</em> simply the
        /// depth — two boards a step apart at the gate play differently, and merging them would
        /// under-report par, which is the direction that hands out stars nobody earned.
        /// </summary>
        public void Write(List<byte> key)
        {
            key.Add((byte)(_head & 0xFF));
            key.Add((byte)(_spark ? 1 : 0));
            for (int i = 0; i < _line.Count; i++) key.Add((byte)_line[i]);
        }
    }

    /// <summary>One arrangement as the solver sees it. See <see cref="ProtoPosition"/>.</summary>
    public sealed class MarchFuture : ProtoPosition
    {
        readonly MarchBoard _board;
        readonly List<MarchMove> _moves = new List<MarchMove>(8);

        public MarchFuture(MarchBoard board)
        {
            _board = board;
            board.Moves(_moves);
        }

        public override bool Won => _board.IsFinished;

        public override int MoveCount => _moves.Count;

        public override ProtoPosition Play(int move)
        {
            if (move < 0 || move >= _moves.Count) return null;

            var forked = _board.Fork();
            return forked.Fire(_moves[move]) == null ? null : new MarchFuture(forked);
        }

        /// <summary>
        /// What a player who never looks ahead would notice this move is worth.
        ///
        /// A goal counts for four pods, because a rescue is the thing on the screen somebody is
        /// actually chasing — a reading that weighed a freed critter the same as a burst pod
        /// would be describing nobody.
        /// </summary>
        public override int Gain(int move)
        {
            if (move < 0 || move >= _moves.Count) return 0;

            var forked = _board.Fork();
            var log = forked.Fire(_moves[move]);
            return log == null ? 0 : log.Took + log.Goals * 4;
        }

        public override void Write(List<byte> key) => _board.Write(key);
    }
}
