using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// The glade: turn the conduits until every sleeping critter is lit in the colour it wants.
    /// The classic mode's board, standing under the challenge hill.
    ///
    /// <para>
    /// <b>It re-implements nothing.</b> The board is a real <see cref="Puzzle"/> dealt by the
    /// real <see cref="LevelGridParser"/> from the real grammar (<see cref="LevelLayout.Grammar"/>):
    /// arms, crossings, briars, rooted tiles and taproots are all the mode's own, and "is this tile solved" is still <see cref="Puzzle.Alike"/> asked exactly once
    /// (invariant 5b). What this class adds is the fusion with the line, and it is the same
    /// sentence the pipes had: <b>a critter lit in its colour fires its turret every turn it
    /// stays lit.</b> So the order the critters are woken in is the decision - wake the colour
    /// whose raiders are nearest first and it fires while the rest of the glade is being
    /// built, and a tap that darkens a lit critter to fix another is a turn that colour goes
    /// quiet.
    /// </para>
    /// <para>
    /// <b>A colour is a lane, and a lane is a gem</b>: red, green, blue and amber light are the
    /// four turrets of the line, painted in the gems' own colours (<c>GladeView</c>). <b>Light
    /// never mixes here</b> (invariant 56l, the owner's instruction on 2026-09-26): the board is
    /// dealt with <see cref="Puzzle.Blends"/> off, so an amber critter is woken by an amber
    /// crystal (<c>#Y</c>, which the grammar has always spelled R|G) and a red network joined to
    /// a green one goes dark rather than amber. A critter or crystal in any other colour has no
    /// turret, and a solution in which two colours meet has a dark network in it; <see cref="Fault"/>
    /// refuses both at read.
    /// </para>
    /// <para>
    /// <b>A lit critter's fire does not bank</b> (<see cref="ChallengeMove.Stream"/>): it fires
    /// at its colour's front raider this turn or into the air. Banked, a critter woken early
    /// stockpiled a turret against every raider still to come and the hill stood empty.
    /// </para>
    /// <para>
    /// <b>Authored solved, dealt turned, and the dealt board is what ships</b> (invariant 5g).
    /// The row is written in its solved orientation with a <c>/k</c> per tile, exactly as a
    /// chapter body is, and <see cref="Fault"/> proves the zeroed board wakes every critter,
    /// that every arm meets an arm, and that at least one tile is owed a turn. Fragile conduits
    /// are refused by name: a crumble is a loss the hill cannot show, and a challenge with a
    /// second way to lose is a challenge with a second thing to explain.
    /// </para>
    /// </summary>
    public sealed class GladePuzzle : IChallengePuzzle
    {
        readonly int _bolts;
        readonly ChallengeMove _move = new ChallengeMove();

        public GladePuzzle(ChallengeDefinition def)
        {
            Width = def.Width;
            Height = def.Height;
            _bolts = def.Bolts;

            var parsed = LevelGridParser.Parse(new LevelLayout(def.Width, def.Height, def.Rows));
            Board = Deal(def, parsed.Cells);

            var zeroed = new Cell[parsed.Cells.Length];
            for (int i = 0; i < zeroed.Length; i++) { zeroed[i] = parsed.Cells[i]; zeroed[i].rot = 0; }
            _solved = Deal(def, zeroed);
        }

        /// <summary>The same board with every rotation at nought: the authored solution, for its networks.</summary>
        readonly Puzzle _solved;

        /// <summary>A challenge glade over these cells: one tile of hill a turn, and light that never blends.</summary>
        static Puzzle Deal(ChallengeDefinition def, Cell[] cells)
            => new Puzzle(LevelId.None, def.Width, def.Height, LevelTuning.Default(1), cells, blends: false);

        /// <summary>
        /// The cells of the solved network a critter belongs to - every conduit and crystal
        /// whose light reaches it once the glade is finished, itself included, nearest the
        /// crystal first. Read off the solution's own components (<see cref="Puzzle.Comp"/>)
        /// rather than walked again, so it cannot disagree with the light. It is what "wake
        /// this critter" means to a player, and the fixture's bot wakes them one at a time.
        /// </summary>
        public void SolutionNetwork(int lamp, List<int> into)
        {
            into.Clear();
            if (lamp < 0 || lamp >= _solved.C.Length || !_solved.Used(lamp)) return;

            int group = _solved.Comp(lamp, 0);
            if (group < 0) return;

            for (int i = 0; i < _solved.C.Length; i++)
            {
                if (!_solved.Used(i)) continue;
                for (int s = 0; s < _solved.StrandCount(i); s++)
                    if (_solved.Comp(i, s) == group) { into.Add(i); break; }
            }

            into.Sort((a, b) => Board.SolutionDepth[a].CompareTo(Board.SolutionDepth[b]));
        }

        public ChallengeGenre Genre => ChallengeGenre.Glade;
        public int Width { get; }
        public int Height { get; }

        /// <summary>The board itself, for the view to draw and the fixture to read. Never turned but through <see cref="Apply"/>.</summary>
        public Puzzle Board { get; }

        public int LampsLit => Board.LampsLit;
        public int LampCount => Board.LampCount;

        /// <summary>Solved the moment every critter is awake - <see cref="Puzzle.Won"/>, the mode's own word.</summary>
        public bool Solved => Board.Won;

        /// <summary>Never: nothing here crumbles, so every board stays solvable.</summary>
        public bool Failed => false;

        /// <summary>
        /// The turret a light feeds, or -1 for a colour no turret fires. R, G and B are the three
        /// lanes of their name; <c>Y</c> (the mask R|G) is the amber lane - its own light here,
        /// shone by its own crystal, because this board never blends (<see cref="Puzzle.Blends"/>).
        /// </summary>
        public static int LaneOf(int energy)
        {
            switch (energy)
            {
                case Energy.R: return 0;
                case Energy.G: return 1;
                case Energy.B: return 2;
                case Energy.R | Energy.G: return 3;
                default: return -1;
            }
        }

        public ChallengeMove Apply(ChallengeInput input)
        {
            _move.Clear();

            if (input.Kind != ChallengeInputKind.Tap || input.Cell < 0 || input.Cell >= Board.C.Length
                || !Board.CanTurn(input.Cell))
            {
                _move.Refused = true;
                return _move;
            }

            Board.Turn(input.Cell);
            Board.Moves++;
            Board.Evaluate();
            _move.Turn = true;

            for (int i = 0; i < Board.C.Length; i++)
            {
                if (Board.C[i].kind != Kind.Lamp || !Board.Lit[i]) continue;
                int lane = LaneOf(Board.C[i].colour);
                if (lane >= 0) _move.Stream(lane, _bolts);
            }

            return _move;
        }

        // ------------------------------------------------------------------ reading a row
        public static string Fault(ChallengeDefinition def)
        {
            if (def.Rows == null || def.Rows.Length != def.Height)
                return $"glade must have {def.Height} row(s), has {(def.Rows == null ? 0 : def.Rows.Length)}";

            var parsed = LevelGridParser.Parse(new LevelLayout(def.Width, def.Height, def.Rows));
            if (!parsed.Ok) return "glade " + parsed.Errors[0];

            var cells = parsed.Cells;
            int lamps = 0, tiles = 0;
            var runes = new int[Puzzle.MaxRunes + 1];

            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                if (cell.kind == Kind.Empty) continue;
                tiles++;

                if (cell.fragile > 0)
                    return $"glade cell {i % def.Width},{i / def.Width} is brittle; a challenge conduit cannot crumble (the hill is the only way to lose)";

                if (cell.kind == Kind.Lamp)
                {
                    lamps++;
                    if (LaneOf(cell.colour) < 0)
                        return $"glade critter at {i % def.Width},{i / def.Width} wants a light no turret fires; a critter wants R, G, B or Y";
                }

                if (cell.kind == Kind.Source && LaneOf(cell.colour) < 0)
                    return $"glade heart-crystal at {i % def.Width},{i / def.Width} emits a light no turret fires; a crystal emits R, G, B or Y";

                if (cell.link > 0 && cell.link <= Puzzle.MaxRunes) runes[cell.link]++;
            }

            if (tiles == 0) return "glade has no tile";
            if (lamps == 0) return "glade has no critter to wake";

            for (int r = 1; r < runes.Length; r++)
                if (runes[r] == 1)
                    return $"glade taproot '{(char)('A' + r - 1)}' binds one conduit; a root needs at least two";

            // Every arm of the solved board meets an arm pointing back, so nothing is left
            // pointing at a wall or an empty cell - the validator's rule, asked of a row.
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].kind == Kind.Empty) continue;
                int x = i % def.Width, y = i / def.Width;
                for (int d = 0; d < 4; d++)
                {
                    if ((cells[i].solved & Puzzle.Bits[d]) == 0) continue;
                    int nx = x + Puzzle.Step[d].x, ny = y + Puzzle.Step[d].y;
                    if (nx < 0 || ny < 0 || nx >= def.Width || ny >= def.Height)
                        return $"glade cell {x},{y} has an arm pointing off the board";
                    var other = cells[ny * def.Width + nx];
                    int back = Puzzle.Bits[(d + 2) & 3];
                    if (other.kind == Kind.Empty || (other.solved & back) == 0)
                        return $"glade cell {x},{y} has an arm nothing meets";
                }
            }

            // The solved board (every rotation at nought) has to wake every critter, or the
            // row is unsolvable and every gate reads green (invariant 5c: proved with the
            // rotations zeroed, asked of Puzzle.Won and never of rot == 0).
            var zeroed = new Cell[cells.Length];
            for (int i = 0; i < cells.Length; i++) { zeroed[i] = cells[i]; zeroed[i].rot = 0; }
            var solved = Deal(def, zeroed);
            if (!solved.Won) return "glade's solved layout does not wake every critter; check the arms and colours";

            // Light never mixes here, so a crystal dark in the solution is two colours meeting
            // on its network: a solved board with a dead light in it, drawn as a fault.
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].kind == Kind.Source && solved.EnergyOn(i, 0) == 0)
                    return $"glade heart-crystal at {i % def.Width},{i / def.Width} meets another colour in the solution; a challenge glade never mixes light";

            var dealt = Deal(def, cells);
            if (dealt.Won) return "glade opens already solved: give at least one tile a /k";
            if (dealt.TurnsToSolution <= 0) return "glade is dealt with no turn owed; give a tile the solution needs a /k";

            return null;
        }

        /// <summary>The tiles still owed a turn, nearest the light first - the hint's order, for a bot.</summary>
        public void Owed(List<int> into) => Board.Owed(into);
    }
}
