using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Proves a level is solvable before anyone ever sees it.
    ///
    /// A puzzle game ships a broken level exactly once before it costs a week of
    /// reviews, so this runs over every level on every build and blocks the build
    /// on any error. It is pure and Editor-free by design: the same code guards the
    /// build, the authoring tool and any test.
    /// </summary>
    public static class LevelValidator
    {
        public static LevelValidationReport Validate(LevelDefinition level)
        {
            var issues = new List<LevelIssue>();

            var parsed = LevelGridParser.Parse(level.Layout);
            foreach (var e in parsed.Errors) Error(issues, e);
            if (!parsed.Ok) return new LevelValidationReport(level.Id, issues, 0);

            var cells = parsed.Cells;
            int computedPar = PuzzleFactory.MinimumMoves(cells);

            CheckPopulation(cells, issues);
            CheckArmsMate(level, cells, issues);
            CheckAuthoredSolution(level, cells, issues);
            CheckFragileConduits(level, cells, issues);
            CheckPar(level, computedPar, issues);
            CheckPresentation(level, issues);

            return new LevelValidationReport(level.Id, issues, computedPar);
        }

        /// <summary>
        /// Validates a set of levels. Takes the definitions rather than a catalog
        /// because validation is an Editor pass over content that is already in hand —
        /// the game never has every level loaded at once, and a signature implying it
        /// could would invite exactly that.
        /// </summary>
        public static List<LevelValidationReport> ValidateAll(IEnumerable<LevelDefinition> levels)
        {
            var reports = new List<LevelValidationReport>();
            if (levels == null) return reports;

            foreach (var level in levels) reports.Add(Validate(level));
            return reports;
        }

        // ------------------------------------------------------------- the checks
        static void CheckPopulation(Cell[] cells, List<LevelIssue> issues)
        {
            int sources = 0, lamps = 0, turnable = 0;
            foreach (var c in cells)
            {
                if (c.kind == Kind.Source) sources++;
                else if (c.kind == Kind.Lamp) lamps++;
                if (c.kind != Kind.Empty && !c.locked && Puzzle.Rotl(c.solved, 1) != c.solved) turnable++;
            }

            if (sources == 0) Error(issues, "no heart-crystal, nothing can ever light up");
            if (lamps == 0) Error(issues, "no sleeping critters, the level can never be won");
            if (turnable == 0) Warn(issues, "no tile can be turned, the player has nothing to do");
        }

        /// <summary>Every arm must point at a neighbour whose arm points back.</summary>
        static void CheckArmsMate(LevelDefinition level, Cell[] cells, List<LevelIssue> issues)
        {
            int w = level.Layout.Width, h = level.Layout.Height;

            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].kind == Kind.Empty) continue;
                int x = i % w, y = i / w;

                for (int d = 0; d < 4; d++)
                {
                    if ((cells[i].solved & Puzzle.Bits[d]) == 0) continue;

                    int nx = x + Puzzle.Step[d].x, ny = y + Puzzle.Step[d].y;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                    {
                        Error(issues, $"arm at {x},{y} points off the board");
                        continue;
                    }

                    int j = ny * w + nx;
                    if (cells[j].kind == Kind.Empty)
                    {
                        Error(issues, $"arm at {x},{y} points at an empty cell");
                        continue;
                    }


                    if ((cells[j].solved & Puzzle.Bits[(d + 2) & 3]) == 0)
                        Error(issues, $"arm at {x},{y} is not mated by its neighbour at {nx},{ny}");
                }
            }
        }

        /// <summary>
        /// The authored orientation must actually be a winning board.
        ///
        /// Judged by building a real <see cref="Puzzle"/> and asking it, rather than by
        /// reimplementing the rules here. That is what stops the validator and the game
        /// from ever disagreeing about what "solved" or "detonated" means — a drift that
        /// would ship a level nobody can finish.
        /// </summary>
        static void CheckAuthoredSolution(LevelDefinition level, Cell[] cells, List<LevelIssue> issues)
        {
            var solved = new Cell[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                solved[i] = cells[i];
                solved[i].rot = 0;
            }

            var probe = new Puzzle(level.Id, level.Layout.Width, level.Layout.Height, level.Tuning, solved);

            if (!probe.Won)
                Error(issues, $"the authored solution lights only {probe.LampsLit} of {probe.LampCount} critters");
        }


        /// <summary>
        /// Fragile conduits must be able to reach their own solution.
        ///
        /// This is the check that keeps the mechanic honest. A conduit authored three
        /// turns from solved but able to survive only two is a level nobody can finish —
        /// and unlike most authoring mistakes it looks completely fine, because every
        /// arm mates and the solved board lights perfectly. The player would simply lose
        /// hearts against it forever.
        ///
        /// Cheap to prove: turns owed at the opening rotation is exactly how many turns
        /// the tile needs, and its count is exactly how many it has.
        /// </summary>
        static void CheckFragileConduits(LevelDefinition level, Cell[] cells, List<LevelIssue> issues)
        {
            int w = level.Layout.Width;

            var probe = new Puzzle(level.Id, w, level.Layout.Height, level.Tuning, Copy(cells));

            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].fragile == 0) continue;

                int x = i % w, y = i / w;

                if (cells[i].locked)
                {
                    Warn(issues, $"the fragile conduit at {x},{y} is also rooted, so it can never be " +
                                 "turned and never crumbles — one of the two is a mistake");
                    continue;
                }

                if (probe.Inert(i))
                {
                    Warn(issues, $"the conduit at {x},{y} looks the same in every orientation, so " +
                                 "turning it is pointless and its fragility can never matter");
                    continue;
                }

                int owed = probe.TurnsOwed(i);
                if (owed > cells[i].fragile)
                    Error(issues, $"the fragile conduit at {x},{y} needs {owed} turn(s) to reach its " +
                                  $"solution but survives only {cells[i].fragile}; it would crumble on the " +
                                  "way and lose the glade");
            }
        }

        static Cell[] Copy(Cell[] cells)
        {
            var copy = new Cell[cells.Length];
            for (int i = 0; i < cells.Length; i++) copy[i] = cells[i];
            return copy;
        }

        static void CheckPar(LevelDefinition level, int computedPar, List<LevelIssue> issues)
        {
            if (computedPar == 0)
            {
                Warn(issues, "the board starts already solved, par would be zero");
                return;
            }
            if (level.Tuning.Par != computedPar)
                Warn(issues, $"par is {level.Tuning.Par} but the board needs {computedPar} turns; " +
                             "omit par in the content file to derive it automatically");
        }

        static void CheckPresentation(LevelDefinition level, List<LevelIssue> issues)
        {
            var p = level.Presentation.MapPosition;
            if (p.x < 0f || p.x > 1f || p.y < 0f || p.y > 1f)
                Warn(issues, $"map position {p} falls outside the 0..1 strip and will not be visible");
        }

        static void Error(List<LevelIssue> issues, string message)
            => issues.Add(new LevelIssue(LevelIssueSeverity.Error, message));

        static void Warn(List<LevelIssue> issues, string message)
            => issues.Add(new LevelIssue(LevelIssueSeverity.Warning, message));
    }
}
