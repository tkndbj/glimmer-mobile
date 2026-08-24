using System.Collections.Generic;
using GlimmerGrove.Progression;

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
        /// <summary>
        /// Proves a level is worth shipping, by asking its own mode.
        ///
        /// <para>
        /// <b>No branch per mode.</b> Each mode brings its own checks (see
        /// <see cref="LevelMode.Validate"/>); this runs them and adds the checks every level
        /// shares whatever it is played on. A mode added tomorrow is validated without this file
        /// being opened — which is what stops it turning into the switch it used to be, where
        /// "not a board, therefore a hollow" was true until it silently was not.
        /// </para>
        /// </summary>
        public static LevelValidationReport Validate(LevelDefinition level)
        {
            var issues = new List<LevelIssue>();

            var mode = LevelModes.Find(level.Mode);
            if (mode == null)
            {
                Error(issues, $"nothing in this build knows how to play a '{level.Mode}' level");
                return new LevelValidationReport(level.Id, issues, 0);
            }

            mode.Validate(level, issues);
            CheckPresentation(level, issues);

            return new LevelValidationReport(level.Id, issues,
                                             level.HasBoard ? level.Tuning.Par : 0);
        }

        /// <summary>
        /// Everything a conduit board has to prove. Called by <c>GladeMode</c> rather than from
        /// <see cref="Validate"/>, so the classic mode's checks belong to the classic mode.
        /// </summary>
        internal static void ValidateGlade(LevelDefinition level, List<LevelIssue> issues)
        {
            var parsed = LevelGridParser.Parse(level.Layout);
            foreach (var e in parsed.Errors) Error(issues, e);
            if (!parsed.Ok) return;

            var cells = parsed.Cells;
            int computedPar = PuzzleFactory.MinimumMoves(cells);

            CheckPopulation(cells, issues);
            CheckArmsMate(level, cells, issues);
            CheckRootedTiles(level, cells, issues);
            CheckAuthoredSolution(level, cells, issues);
            CheckFragileConduits(level, cells, issues);
            CheckBoundConduits(level, cells, issues);
            CheckCrossings(level, cells, issues);
            CheckBriars(level, cells, issues);
            CheckPar(level, computedPar, issues);
            CheckClock(level, issues);
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
                if (c.kind != Kind.Empty && !c.locked && !Puzzle.Alike(c, 1)) turnable++;
            }

            if (sources == 0) Error(issues, "no heart-crystal, nothing can ever light up");
            if (lamps == 0) Error(issues, "no sleeping critters, the level can never be won");
            if (turnable == 0) Warn(issues, "no tile can be turned, the player has nothing to do");
        }

        /// <summary>
        /// A crossing has to actually cross something.
        ///
        /// <para>
        /// The mechanic's whole promise is that the two flows through a tile are different
        /// flows. When the authored solution joins them somewhere else on the board the tile
        /// is telling the player a lie in the one place the game asks them to trust their
        /// eyes — and it is a lie that costs turns, because they will route around a
        /// separation that was never there. Invisible everywhere else: the arms mate, the
        /// solution lights, par comes out a sensible number, and the board draws beautifully.
        /// </para>
        /// <para>
        /// A warning rather than an error, and the distinction is real. Two strands meeting
        /// elsewhere is sometimes exactly the shape a finale wants — a loop that leaves by one
        /// arm and comes back by another has to close somewhere. So this is a question about
        /// intent, which is what warnings are for here.
        /// </para>
        /// </summary>
        static void CheckCrossings(LevelDefinition level, Cell[] cells, List<LevelIssue> issues)
        {
            int w = level.Layout.Width;

            var solved = Copy(cells);
            for (int i = 0; i < solved.Length; i++) solved[i].rot = 0;

            Puzzle probe = null;

            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].kind != Kind.Crossing) continue;

                probe ??= new Puzzle(level.Id, w, level.Layout.Height, level.Tuning, solved);

                if (probe.EnergyOn(i, 0) == 0 && probe.EnergyOn(i, 1) == 0)
                {
                    Warn(issues, $"neither strand of the crossing at {i % w},{i / w} carries any " +
                                 "light in the authored solution, so nothing on the board ever " +
                                 "demonstrates that it keeps two flows apart");
                    continue;
                }

                // Joined elsewhere: the two strands end up in one network anyway, so the tile
                // looks like it is separating something it is not.
                if (probe.Comp(i, 0) == probe.Comp(i, 1))
                    Warn(issues, $"the two strands of the crossing at {i % w},{i / w} are joined " +
                                 "elsewhere in the authored solution, so it crosses nothing; " +
                                 "the player will route around a separation that is not there");
            }
        }

        /// <summary>
        /// A briar's thorns have to be closing something off.
        ///
        /// <para>
        /// The mechanic's whole promise is that one of a tile's two ways is shut and the other
        /// is open. When every way it has — the open pair and both thorned arms — leads into
        /// the same network in the authored solution, the thorns are shutting a door onto the
        /// room they are already in: turning the tile changes which arms carry the light and
        /// changes nothing about where the light gets to. The player has a tile they cannot
        /// place by looking, no reason on the board to place it either way, and a par that
        /// charged them for it.
        /// </para>
        /// <para>
        /// Invisible everywhere else, exactly as with a crossing: the arms mate, the solution
        /// lights, par comes out a sensible number and the board draws beautifully. Unlike a
        /// crossing, an unlit briar is <em>not</em> evidence of that — a briar standing in an
        /// island of dark with its thorns facing the grove is one of the best tiles this
        /// mechanic has, because opening it is how a shadow wakes. So the question asked here
        /// is about what the ways touch, never about what reaches the tile.
        /// </para>
        /// <para>
        /// A warning rather than an error, for <see cref="CheckCrossings"/>' reason: it is a
        /// question about intent, and a chapter can want a briar that is scenery on the glade
        /// that teaches what a briar is.
        /// </para>
        /// </summary>
        static void CheckBriars(LevelDefinition level, Cell[] cells, List<LevelIssue> issues)
        {
            int w = level.Layout.Width;

            var solved = Copy(cells);
            for (int i = 0; i < solved.Length; i++) solved[i].rot = 0;

            Puzzle probe = null;

            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].kind != Kind.Briar) continue;

                probe ??= new Puzzle(level.Id, w, level.Layout.Height, level.Tuning, solved);

                int mine = probe.Comp(i, 0);
                bool separates = false;

                for (int d = 0; d < 4 && !separates; d++)
                {
                    // Only the thorned ways are asked about. The open pair is the network this
                    // tile is already in, so it can never disagree with itself.
                    if ((cells[i].gate & Puzzle.Bits[d]) != 0) continue;
                    if ((cells[i].solved & Puzzle.Bits[d]) == 0) continue;

                    int j = probe.Neighbour(i, d);
                    if (j < 0) continue;

                    // The way has to be open on the *other* side too, or taking these thorns
                    // off would still join nothing — which is what two briars back to back are.
                    int back = (d + 2) & 3;
                    if ((probe.Live(j) & Puzzle.Bits[back]) == 0) continue;

                    if (probe.Comp(j, probe.StrandAt(j, back)) != mine) separates = true;
                }

                if (!separates)
                    Warn(issues, $"the thorns on the briar at {i % w},{i / w} close nothing off " +
                                 "in the authored solution — every way it has leads back into one " +
                                 "network, so turning it moves the light and never where the " +
                                 "light gets to");
            }
        }

        /// <summary>
        /// Whether the clock and the move thresholds are asking for the same run.
        ///
        /// <para>
        /// Stars are the worse of the two readings, so a glade can be tuned into a state where
        /// three of them are unreachable by anybody: the clock's gold threshold and the move
        /// gold threshold together imply a sustained tap rate, and past about two a second
        /// nobody is solving a puzzle, they are drumming. Nothing else in the pipeline can see
        /// that — each number is individually reasonable — which is exactly the kind of
        /// combination <c>ValidateHearts</c> exists to warn about.
        /// </para>
        /// <para>
        /// Note the rate is <b>independent of par</b>, and that is by construction rather than
        /// luck: gold moves are <c>par × GoldFactor</c> and gold seconds are
        /// <c>par × TimeFactor × TimeGoldFraction</c>, so par cancels and the rate is a fact
        /// about the three factors alone. A level therefore only reaches this warning by
        /// overriding one of them.
        /// </para>
        /// <para>
        /// Warnings and never errors, for <c>ValidateHearts</c>' reason: these are judgements
        /// about players, the build cannot make them, and a content push that had to clear a
        /// taste check would be a content push nobody could ship on a Friday.
        /// </para>
        /// </summary>
        static void CheckClock(LevelDefinition level, List<LevelIssue> issues)
        {
            var tuning = level.Tuning;
            if (!tuning.HasTimeLimit) return;

            float limitSeconds = tuning.TimeLimitMillis / 1000f;
            if (limitSeconds <= 0f) return;

            // What merely finishing demands: par turns inside the whole clock. A glade that
            // fails this is not hard, it is unwinnable.
            float toFinish = tuning.Par / limitSeconds;
            if (toFinish > FinishTapRate)
                Warn(issues, $"the clock allows {limitSeconds:0.#}s for a par of {tuning.Par}, " +
                             $"which needs {toFinish:0.0} taps a second just to finish — " +
                             "raise timeFactor or the glade cannot be won");

            // What three stars demands: the gold move count inside the gold slice of the clock.
            float goldSeconds = tuning.TimeGoldMillis / 1000f;
            if (goldSeconds <= 0f) return;

            float toStar = tuning.GoldThreshold / goldSeconds;
            if (toStar > StarTapRate)
                Warn(issues, $"three stars needs {tuning.GoldThreshold} turns inside " +
                             $"{goldSeconds:0.#}s — {toStar:0.0} taps a second, which is drumming " +
                             "rather than solving, so the third star is effectively unreachable");

            // And the same question asked of the tightest clock anybody could publish.
            //
            // The limit is multiplied by a live `clockScale` (DifficultyRuleTable), so a glade
            // that is merely demanding as authored can be unwinnable as retuned — and that
            // retune reaches every device in the world without an app update and without
            // passing this validator again. So the build gate has to judge the worst case
            // rather than the shipped one; the floor is a constant precisely so it can.
            float atFloor = toFinish / DifficultyLimits.MinClockScale;
            if (toFinish <= FinishTapRate && atFloor > FinishTapRate)
                Warn(issues, $"the clock allows {limitSeconds:0.#}s as authored, but a published " +
                             $"clockScale of {DifficultyLimits.MinClockScale:0.##} would cut it to " +
                             $"{limitSeconds * DifficultyLimits.MinClockScale:0.#}s and need " +
                             $"{atFloor:0.0} taps a second just to finish — raise timeFactor, or " +
                             "the glade cannot survive the tightest retune anybody can push");
        }

        /// <summary>
        /// Sustained taps a second: the most that can be asked merely to finish, and the most
        /// that can be asked for three stars.
        ///
        /// <para>
        /// The shipped defaults land at 1.35 for the star rate, which is demanding on a first
        /// attempt and comfortable on a replay — the shape a three-star threshold should have.
        /// The ceilings sit above that rather than at it, because this is a warning about
        /// tuning that cannot work at all, not a second opinion about tuning that is merely
        /// hard.
        /// </para>
        /// </summary>
        const float FinishTapRate = 1.2f, StarTapRate = 1.8f;

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

            // Reported separately from the critter count, because they are opposite
            // mistakes with opposite fixes: one means the light does not reach far enough
            // and the other means it reaches too far. A single "the solution does not win"
            // would send the author looking in the wrong direction half the time.
            if (probe.DuskcapsWoken > 0)
                Error(issues, $"the authored solution wakes {probe.DuskcapsWoken} of " +
                              $"{probe.DuskcapCount} duskcap(s); a duskcap must be dark in the " +
                              "solution, so its conduits have to reach no heart-crystal at all");

            if (probe.LampsLit != probe.LampCount)
                Error(issues, $"the authored solution lights only {probe.LampsLit} of {probe.LampCount} critters");
        }

        /// <summary>
        /// Taproots must be able to reach their own solution, and be worth having.
        ///
        /// <para>
        /// The error is the same shape as the brittle-conduit one and exists for the same
        /// reason: a root whose conduits can never all be right at once is a level nobody
        /// can finish, and it looks perfectly authored — every arm mates, the solved board
        /// lights, par comes out a plausible number. The player would simply lose hearts
        /// against it for ever. It is cheap to prove, because one tap turns the whole root:
        /// either some offset in 0..3 solves every member or none does.
        /// </para>
        /// <para>
        /// A rune only one conduit carries is an error rather than a shrug. It draws a
        /// binding mark on a tile that is bound to nothing, which is a promise the board
        /// makes to the player and does not keep — and the overwhelmingly likely cause is a
        /// partner that was mistyped, which is worth stopping a build for.
        /// </para>
        /// </summary>
        static void CheckBoundConduits(LevelDefinition level, Cell[] cells, List<LevelIssue> issues)
        {
            int w = level.Layout.Width;
            var probe = new Puzzle(level.Id, w, level.Layout.Height, level.Tuning, Copy(cells));

            for (int rune = 1; rune <= Puzzle.MaxRunes; rune++)
            {
                int members = 0, movable = 0, first = -1;
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i].link != rune) continue;
                    members++;
                    if (first < 0) first = i;
                    if (!probe.InertAlone(i)) movable++;
                }

                if (members == 0) continue;

                char letter = (char)('A' + rune - 1);

                if (members == 1)
                {
                    Error(issues, $"taproot '{letter}' has only the conduit at " +
                                  $"{first % w},{first / w} on it; a root of one wears a binding " +
                                  "mark and binds nothing");
                    continue;
                }

                if (PuzzleFactory.RootTurnsOwed(cells, rune) < 0)
                {
                    Error(issues, $"the conduits on taproot '{letter}' can never all be right at " +
                                  "once — no number of turns solves every one of them, so the " +
                                  "glade cannot be finished");
                    continue;
                }

                if (movable == 0)
                    Warn(issues, $"every conduit on taproot '{letter}' looks the same in every " +
                                 "orientation, so turning the root can never matter");
            }

            // Past this the marks stop telling the roots apart, and two roots wearing one
            // identity is worse than no mark at all — the player reads a binding that is not
            // there. Said out loud rather than clamped quietly; the drawing reads the same
            // number, so the two cannot drift.
            if (probe.RootCount > Puzzle.MaxReadableRunes)
                Warn(issues, $"this board carries {probe.RootCount} taproots but a mark can only " +
                             $"tell {Puzzle.MaxReadableRunes} of them apart; split the glade in two " +
                             "or merge some of the roots");
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
        /// <summary>
        /// A rooted tile has to start in an orientation that already reads as solved.
        ///
        /// <para>
        /// This is the one check that guards the other checks. Every proof below runs
        /// against a copy of the board with every rotation set to zero, because that is
        /// the authored solution — but a rooted tile can never be turned, so a rooted
        /// tile authored away from zero is a tile the player is stuck with at an angle
        /// the proof never sees. What is proved is then a different board from the one
        /// that ships, and nothing else here can notice: every arm mates, the solved
        /// probe lights, par is unaffected because <see cref="PuzzleFactory.MinimumMoves"/>
        /// skips rooted tiles, and the glade draws perfectly.
        /// </para>
        /// <para>
        /// It also breaks the one promise <see cref="Puzzle.TurnsToSolution"/> makes.
        /// That count includes rooted tiles, so one stuck off its solution adds turns
        /// that can never be paid: a player who has in fact reached the solution is told
        /// they were one turn away, which is the near-miss line being generous — the
        /// single thing it exists not to be.
        /// </para>
        /// <para>
        /// Asked as <see cref="Puzzle.Alike"/> rather than as <c>rot == 0</c>, because a
        /// straight conduit and a straight crossing genuinely read the same half a turn
        /// round and refusing those would be refusing a tile that is already correct.
        /// </para>
        /// </summary>
        static void CheckRootedTiles(LevelDefinition level, Cell[] cells, List<LevelIssue> issues)
        {
            int w = level.Layout.Width;

            for (int i = 0; i < cells.Length; i++)
            {
                if (!cells[i].locked || cells[i].kind == Kind.Empty) continue;
                if (Puzzle.Alike(cells[i], cells[i].rot)) continue;

                Error(issues, $"the rooted tile at {i % w},{i / w} starts {PuzzleFactory.TurnsOwed(cells[i])} " +
                              "turn(s) from its solution and can never be turned, so the board that was " +
                              "proved solvable is not the board the player gets; author it at /0");
            }
        }

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
